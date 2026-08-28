using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the Dustiny item catalog and persists purchased/equipped item IDs.
///
/// Important rule:
/// - The player can equip exactly one item at a time across every category.
/// - Shop/MyPage preview also shows exactly one temporary item at a time.
/// - Preview is never saved and returns to the saved equipped item when the page closes.
/// </summary>
public class ShopInventoryManager : MonoBehaviour
{
    [Serializable]
    public class ShopItemDefinition
    {
        [Header("Identity")]
        public string itemId = "item1";
        public string displayName = "아이템";

        [TextArea(2, 5)]
        public string description = "아이템 설명";

        [Tooltip("아이템의 부착 위치 자동 탐색에 사용합니다. 착용 가능 개수와는 관계없으며, 전체 아이템 중 한 개만 착용됩니다.")]
        public string category = "head";

        [Range(10, 20)]
        public int price = 10;

        [Header("Card Icon")]
        [Tooltip("ShopPage와 MyPage 카드에 공통으로 표시할 2D Sprite입니다.")]
        public Sprite icon;

        [Header("Durry Visual")]
        [Tooltip("더리 비주얼 루트 아래 실제 착용 오브젝트 이름입니다.")]
        public string visualObjectName;

        [Tooltip("비워두면 category를 기준으로 머리/몸통 뼈를 자동 탐색합니다. 정확한 뼈 이름을 알고 있을 때만 입력하세요.")]
        public string attachmentTargetName;
    }

    [Serializable]
    private class StringListSaveData
    {
        public List<string> values = new List<string>();
    }

    // Legacy V2 save format. It allowed one item per category.
    [Serializable]
    private class EquippedItemEntry
    {
        public string category;
        public string itemId;
    }

    [Serializable]
    private class EquippedItemSaveData
    {
        public List<EquippedItemEntry> entries = new List<EquippedItemEntry>();
    }

    public static ShopInventoryManager Instance { get; private set; }

    public event Action<string> OnItemPurchased;
    public event Action<string> OnItemEquipped;
    public static event Action OnInventoryChanged;
    public static event Action OnPreviewChanged;

    private const string OwnedItemsKey = "Dustiny_OwnedItems_V2";
    private const string EquippedSingleItemKey = "Dustiny_EquippedSingleItem_V3";
    private const string LegacyEquippedItemsKey = "Dustiny_EquippedItems_V2";

    [Header("[ 더스티니 아이템 카탈로그 ]")]
    [Tooltip("아이콘은 여기에서 아이템마다 한 번만 연결합니다. 두 페이지가 같은 아이콘을 재사용합니다.")]
    [SerializeField] private List<ShopItemDefinition> shopItems = CreateDefaultShopItems();

    [Header("[ Legacy Catalog Auto Upgrade ]")]
    [Tooltip("기존 item1~item4 카탈로그가 남아 있으면 더스티니 기본 6개 아이템으로 자동 교체합니다.")]
    [SerializeField] private bool automaticallyReplaceLegacyCatalog = true;

    [Header("[ 저장된 인벤토리 - Runtime Read Only ]")]
    [SerializeField] private List<string> ownedItemIds = new List<string>();
    [SerializeField] private string equippedItemId = string.Empty;
    [SerializeField] private string previewItemId = string.Empty;

    private readonly Dictionary<string, ShopItemDefinition> itemById =
        new Dictionary<string, ShopItemDefinition>();

    public IReadOnlyList<ShopItemDefinition> ShopItems => shopItems;
    public IReadOnlyList<string> OwnedItemIds => ownedItemIds;
    public string EquippedItemId => equippedItemId;
    public string PreviewItemId => previewItemId;
    public string DisplayedItemId => !string.IsNullOrWhiteSpace(previewItemId)
        ? previewItemId
        : equippedItemId;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            if (automaticallyReplaceLegacyCatalog && CatalogLooksLegacy())
            {
                shopItems = CreateDefaultShopItems();
                Debug.Log("[상점 카탈로그] 기존 item1~item4 목록을 더스티니 기본 상품으로 자동 교체했습니다.");
            }

            RebuildCatalogTable();
            LoadData();
            return;
        }

        if (Instance != this)
        {
            Destroy(this);
        }
    }

    private void OnValidate()
    {
        ValidateCatalogInEditor();
    }

    public ShopItemDefinition GetItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        if (itemById.Count == 0)
        {
            RebuildCatalogTable();
        }

        itemById.TryGetValue(itemId.Trim(), out ShopItemDefinition item);
        return item;
    }

    public bool BuyShopItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);

        if (item == null)
        {
            Debug.LogWarning($"[상점] 등록되지 않은 아이템입니다: {itemId}");
            return false;
        }

        if (IsOwned(item.itemId))
        {
            Debug.Log($"[상점] 이미 보유 중인 아이템입니다: {item.itemId}");
            return false;
        }

        if (CreditManager.Instance == null)
        {
            Debug.LogWarning("[상점] CreditManager가 없습니다.");
            return false;
        }

        int price = Mathf.Clamp(item.price, 10, 20);
        if (!CreditManager.Instance.UseCredit(price))
        {
            return false;
        }

        AddItemToInventory(item.itemId);
        OnItemPurchased?.Invoke(item.itemId);
        DustinySfx.PlayBuy();
        return true;
    }

    public bool BuyAndEquipShopItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null)
        {
            return false;
        }

        if (!IsOwned(item.itemId) && !BuyShopItem(item.itemId))
        {
            return false;
        }

        return EquipItem(item.itemId);
    }

    public void AddItemToInventory(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null || IsOwned(item.itemId))
        {
            return;
        }

        ownedItemIds.Add(item.itemId);
        SaveData();
        OnInventoryChanged?.Invoke();
        Debug.Log($"[인벤토리] 새로운 아이템 저장: {item.itemId}");
    }

    /// <summary>
    /// Equips one item globally. Any previously equipped item is automatically replaced.
    /// </summary>
    public bool EquipItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null)
        {
            Debug.LogWarning($"[장착 실패] 카탈로그에 없는 아이템입니다: {itemId}");
            return false;
        }

        if (!IsOwned(item.itemId))
        {
            Debug.LogWarning($"[장착 실패] 보유하지 않은 아이템입니다: {item.itemId}");
            return false;
        }

        string previousItemId = equippedItemId;
        equippedItemId = item.itemId;
        previewItemId = string.Empty;

        SaveData();
        OnItemEquipped?.Invoke(equippedItemId);
        OnInventoryChanged?.Invoke();
        OnPreviewChanged?.Invoke();
        DustinySfx.PlayYes();

        if (!string.IsNullOrWhiteSpace(previousItemId) && previousItemId != equippedItemId)
        {
            Debug.Log($"[장착 교체] {previousItemId} 해제 → {equippedItemId} 장착");
        }
        else
        {
            Debug.Log($"[장착 완료] {equippedItemId}");
        }

        return true;
    }

    /// <summary>
    /// Legacy-compatible overload. Category is ignored because only one global item can be equipped.
    /// </summary>
    public bool EquipItem(string itemId, string category)
    {
        return EquipItem(itemId);
    }

    public bool UnequipItem()
    {
        bool hadEquippedItem = !string.IsNullOrWhiteSpace(equippedItemId);
        bool hadPreviewItem = !string.IsNullOrWhiteSpace(previewItemId);

        equippedItemId = string.Empty;
        previewItemId = string.Empty;

        if (!hadEquippedItem && !hadPreviewItem)
        {
            return false;
        }

        SaveData();
        OnInventoryChanged?.Invoke();
        OnPreviewChanged?.Invoke();
        Debug.Log("[장착 해제] 현재 착용 아이템을 해제했습니다.");
        return true;
    }

    /// <summary>
    /// Legacy-compatible method. The equipped item is removed only when it belongs to the requested category.
    /// </summary>
    public bool UnequipCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        ShopItemDefinition equippedItem = GetItem(equippedItemId);
        if (equippedItem == null || !string.Equals(
                equippedItem.category,
                category.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UnequipItem();
    }

    public string GetEquippedItemId()
    {
        return equippedItemId;
    }

    /// <summary>
    /// Legacy-compatible category lookup.
    /// </summary>
    public string GetEquippedItemId(string category)
    {
        if (string.IsNullOrWhiteSpace(equippedItemId))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return equippedItemId;
        }

        ShopItemDefinition item = GetItem(equippedItemId);
        return item != null && string.Equals(
            item.category,
            category.Trim(),
            StringComparison.OrdinalIgnoreCase)
                ? equippedItemId
                : string.Empty;
    }

    public string GetDisplayedItemId()
    {
        return DisplayedItemId;
    }

    /// <summary>
    /// Legacy-compatible category lookup. Preview still has global priority.
    /// </summary>
    public string GetDisplayedItemId(string category)
    {
        string displayedItemId = DisplayedItemId;
        if (string.IsNullOrWhiteSpace(displayedItemId))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return displayedItemId;
        }

        ShopItemDefinition item = GetItem(displayedItemId);
        return item != null && string.Equals(
            item.category,
            category.Trim(),
            StringComparison.OrdinalIgnoreCase)
                ? displayedItemId
                : string.Empty;
    }

    /// <summary>
    /// Shows one temporary item globally. Selecting another card immediately cancels the old preview.
    /// </summary>
    public bool PreviewItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null)
        {
            return false;
        }

        if (previewItemId == item.itemId)
        {
            return true;
        }

        previewItemId = item.itemId;
        OnPreviewChanged?.Invoke();
        DustinySfx.PlayYes();
        return true;
    }

    public void ClearPreviewForCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(previewItemId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            ClearAllPreviews();
            return;
        }

        ShopItemDefinition previewItem = GetItem(previewItemId);
        if (previewItem != null && string.Equals(
                previewItem.category,
                category.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            ClearAllPreviews();
        }
    }

    public void ClearAllPreviews()
    {
        if (string.IsNullOrWhiteSpace(previewItemId))
        {
            return;
        }

        previewItemId = string.Empty;
        OnPreviewChanged?.Invoke();
    }

    public bool IsEquipped(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               equippedItemId == itemId.Trim();
    }

    public bool IsDisplayed(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               DisplayedItemId == itemId.Trim();
    }

    public bool IsOwned(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && ownedItemIds.Contains(itemId.Trim());
    }

    public bool CanAfford(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        return item != null &&
               CreditManager.Instance != null &&
               CreditManager.Instance.CurrentCredit >= Mathf.Clamp(item.price, 10, 20);
    }

    public int GetPrice(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        return item != null ? Mathf.Clamp(item.price, 10, 20) : -1;
    }

    public void GiveRandomUnownedItem()
    {
        List<string> unownedItems = new List<string>();

        foreach (ShopItemDefinition item in shopItems)
        {
            if (item != null &&
                !string.IsNullOrWhiteSpace(item.itemId) &&
                !IsOwned(item.itemId))
            {
                unownedItems.Add(item.itemId.Trim());
            }
        }

        if (unownedItems.Count > 0)
        {
            string randomItemId = unownedItems[UnityEngine.Random.Range(0, unownedItems.Count)];
            AddItemToInventory(randomItemId);
            Debug.Log($"[연속 보상] 랜덤 아이템 획득: {randomItemId}");
            return;
        }

        CreditManager.Instance?.AddCredit(5);
        Debug.Log("[연속 보상] 모든 아이템 보유 중이므로 5 CR을 지급했습니다.");
    }

    /// <summary>
    /// Called by left-hand reset. Purchase, equipment, and previews are cleared.
    /// </summary>
    public void ResetAllItemState()
    {
        ownedItemIds.Clear();
        equippedItemId = string.Empty;
        previewItemId = string.Empty;

        PlayerPrefs.DeleteKey(OwnedItemsKey);
        PlayerPrefs.DeleteKey(EquippedSingleItemKey);
        PlayerPrefs.DeleteKey(LegacyEquippedItemsKey);
        PlayerPrefs.Save();

        OnInventoryChanged?.Invoke();
        OnPreviewChanged?.Invoke();
        Debug.Log("[아이템 초기화] 구매/장착/미리보기 데이터를 모두 초기화했습니다.");
    }

    [ContextMenu("Reset Shop Inventory For Debug")]
    public void ResetInventoryForDebug()
    {
        ResetAllItemState();
    }

    [ContextMenu("Reset Catalog To Dustiny Defaults")]
    public void ResetCatalogToDustinyDefaults()
    {
        shopItems = CreateDefaultShopItems();
        RebuildCatalogTable();
        Debug.Log("[상점 카탈로그] 더스티니 기본 6개 상품으로 다시 구성했습니다.");
    }

    private static List<ShopItemDefinition> CreateDefaultShopItems()
    {
        return new List<ShopItemDefinition>
        {
            new ShopItemDefinition
            {
                itemId = "back_nametag_01",
                displayName = "더리 네임택",
                description = "등 뒤에 달아주는 귀여운 더리 전용 네임택이야.",
                category = "back",
                price = 10,
                visualObjectName = "Back_Nametag_01"
            },
            new ShopItemDefinition
            {
                itemId = "body_sleepwear",
                displayName = "포근한 잠옷",
                description = "청소가 끝난 뒤 편안하게 쉴 수 있는 보송보송한 잠옷이야.",
                category = "body",
                price = 16,
                visualObjectName = "Body_Sleepwear"
            },
            new ShopItemDefinition
            {
                itemId = "head_sleepmask",
                displayName = "수면 안대",
                description = "잠깐 쉬고 싶을 때 쓰는 말랑한 수면 안대야.",
                category = "head",
                price = 12,
                visualObjectName = "Head_Sleepmask"
            },
            new ShopItemDefinition
            {
                itemId = "head_hat_hard",
                displayName = "안전모",
                description = "본격적인 청소를 시작할 때 쓰는 든든한 안전모야.",
                category = "head",
                price = 18,
                visualObjectName = "Head_Hat_Hard"
            },
            new ShopItemDefinition
            {
                itemId = "head_cone",
                displayName = "파티 고깔",
                description = "미션 완료를 신나게 축하해 주는 파티 고깔이야.",
                category = "head",
                price = 14,
                visualObjectName = "Head_Cone"
            },
            new ShopItemDefinition
            {
                itemId = "head_cap_baseball",
                displayName = "야구 모자",
                description = "어디서든 가볍게 착용할 수 있는 캐주얼한 모자야.",
                category = "head",
                price = 20,
                visualObjectName = "Head_Cap_Baseball"
            }
        };
    }

    private bool CatalogLooksLegacy()
    {
        if (shopItems == null || shopItems.Count == 0)
        {
            return true;
        }

        bool foundAnyValidEntry = false;
        foreach (ShopItemDefinition item in shopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            foundAnyValidEntry = true;
            string id = item.itemId.Trim().ToLowerInvariant();
            bool legacyId = id == "item1" || id == "item2" || id == "item3" || id == "item4";
            if (!legacyId)
            {
                return false;
            }
        }

        return foundAnyValidEntry;
    }

    private void RebuildCatalogTable()
    {
        itemById.Clear();

        if (shopItems == null)
        {
            shopItems = CreateDefaultShopItems();
        }

        foreach (ShopItemDefinition item in shopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            NormalizeCatalogItem(item);

            if (itemById.ContainsKey(item.itemId))
            {
                Debug.LogWarning($"[상점] itemId가 중복되어 마지막 항목으로 덮어씁니다: {item.itemId}");
            }

            itemById[item.itemId] = item;
        }
    }

    private static void NormalizeCatalogItem(ShopItemDefinition item)
    {
        item.itemId = item.itemId.Trim();
        item.displayName = string.IsNullOrWhiteSpace(item.displayName)
            ? item.itemId
            : item.displayName.Trim();
        item.description = item.description ?? string.Empty;
        item.category = string.IsNullOrWhiteSpace(item.category)
            ? "head"
            : item.category.Trim();
        item.price = Mathf.Clamp(item.price, 10, 20);
        item.attachmentTargetName = item.attachmentTargetName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(item.visualObjectName))
        {
            item.visualObjectName = GetDefaultVisualObjectName(item.itemId);
        }
        else
        {
            item.visualObjectName = item.visualObjectName.Trim();
        }
    }

    private static string GetDefaultVisualObjectName(string itemId)
    {
        switch (itemId)
        {
            case "back_nametag_01": return "Back_Nametag_01";
            case "body_sleepwear": return "Body_Sleepwear";
            case "head_sleepmask": return "Head_Sleepmask";
            case "head_hat_hard": return "Head_Hat_Hard";
            case "head_cone": return "Head_Cone";
            case "head_cap_baseball": return "Head_Cap_Baseball";
            default: return itemId;
        }
    }

    private void ValidateCatalogInEditor()
    {
        if (shopItems == null)
        {
            return;
        }

        HashSet<string> ids = new HashSet<string>();
        foreach (ShopItemDefinition item in shopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            NormalizeCatalogItem(item);
            if (!ids.Add(item.itemId))
            {
                Debug.LogWarning($"[상점] 중복 itemId: {item.itemId}", this);
            }
        }
    }

    private void SaveData()
    {
        StringListSaveData ownedSaveData = new StringListSaveData
        {
            values = new List<string>(ownedItemIds)
        };

        PlayerPrefs.SetString(OwnedItemsKey, JsonUtility.ToJson(ownedSaveData));
        PlayerPrefs.SetString(EquippedSingleItemKey, equippedItemId ?? string.Empty);

        // Keep a one-entry V2 save for compatibility with older scene copies.
        EquippedItemSaveData legacySaveData = new EquippedItemSaveData();
        ShopItemDefinition equippedItem = GetItem(equippedItemId);
        if (equippedItem != null)
        {
            legacySaveData.entries.Add(new EquippedItemEntry
            {
                category = equippedItem.category,
                itemId = equippedItem.itemId
            });
        }

        PlayerPrefs.SetString(LegacyEquippedItemsKey, JsonUtility.ToJson(legacySaveData));
        PlayerPrefs.Save();
    }

    private void LoadData()
    {
        ownedItemIds.Clear();
        equippedItemId = string.Empty;
        previewItemId = string.Empty;

        string ownedJson = PlayerPrefs.GetString(OwnedItemsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(ownedJson))
        {
            StringListSaveData ownedSaveData = JsonUtility.FromJson<StringListSaveData>(ownedJson);
            if (ownedSaveData?.values != null)
            {
                foreach (string rawItemId in ownedSaveData.values)
                {
                    string itemId = rawItemId?.Trim();
                    if (GetItem(itemId) != null && !ownedItemIds.Contains(itemId))
                    {
                        ownedItemIds.Add(itemId);
                    }
                }
            }
        }

        string savedSingleItemId = PlayerPrefs.GetString(EquippedSingleItemKey, string.Empty).Trim();
        if (IsOwned(savedSingleItemId) && GetItem(savedSingleItemId) != null)
        {
            equippedItemId = savedSingleItemId;
            return;
        }

        // Migrate old multi-category data by keeping only the first valid owned item.
        string legacyJson = PlayerPrefs.GetString(LegacyEquippedItemsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(legacyJson))
        {
            return;
        }

        EquippedItemSaveData legacySaveData = JsonUtility.FromJson<EquippedItemSaveData>(legacyJson);
        if (legacySaveData?.entries == null)
        {
            return;
        }

        foreach (EquippedItemEntry entry in legacySaveData.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.itemId))
            {
                continue;
            }

            string itemId = entry.itemId.Trim();
            if (IsOwned(itemId) && GetItem(itemId) != null)
            {
                equippedItemId = itemId;
                SaveData();
                Debug.Log($"[아이템 저장 마이그레이션] 여러 카테고리 착용 데이터를 단일 착용 '{itemId}'로 변경했습니다.");
                break;
            }
        }
    }
}
