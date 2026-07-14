using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the Dustiny item catalog and persists purchased/equipped item IDs.
///
/// Card icons are assigned once in this manager and are reused by both
/// ShopPage and MyPage. No item data is assigned to individual cards.
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

        [Tooltip("같은 카테고리에서는 한 아이템만 착용됩니다. 예: head, body, back")]
        public string category = "head";

        [Range(10, 20)]
        public int price = 10;

        [Header("Card Icon")]
        [Tooltip("ShopPage와 MyPage 카드에 공통으로 표시할 2D Sprite입니다.")]
        public Sprite icon;

        [Header("Durry Visual")]
        [Tooltip("Durry_Main 아래 실제 착용 오브젝트 이름입니다. 비워두면 itemId로 기본 이름을 자동 지정합니다.")]
        public string visualObjectName;
    }

    [Serializable]
    private class StringListSaveData
    {
        public List<string> values = new List<string>();
    }

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
    private const string EquippedItemsKey = "Dustiny_EquippedItems_V2";

    [Header("[ 더스티니 아이템 카탈로그 ]")]
    [Tooltip("아이콘은 여기에서 아이템마다 한 번만 연결합니다. 두 페이지가 같은 아이콘을 재사용합니다.")]
    [SerializeField] private List<ShopItemDefinition> shopItems = CreateDefaultShopItems();

    [Header("[ Legacy Catalog Auto Upgrade ]")]
    [Tooltip("기존 item1~item4 카탈로그가 남아 있으면 더스티니 기본 6개 아이템으로 자동 교체합니다.")]
    [SerializeField] private bool automaticallyReplaceLegacyCatalog = true;

    [Header("[ 저장된 인벤토리 - Runtime Read Only ]")]
    [SerializeField] private List<string> ownedItemIds = new List<string>();

    private readonly Dictionary<string, ShopItemDefinition> itemById =
        new Dictionary<string, ShopItemDefinition>();

    private readonly Dictionary<string, string> equippedItems =
        new Dictionary<string, string>();

    // Preview state is temporary and is never saved.
    private readonly Dictionary<string, string> previewItems =
        new Dictionary<string, string>();

    public IReadOnlyList<ShopItemDefinition> ShopItems => shopItems;
    public IReadOnlyList<string> OwnedItemIds => ownedItemIds;

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
        Debug.Log($"[상점 구매 완료] {item.displayName} / {price} CR");
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

    public bool EquipItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null)
        {
            Debug.LogWarning($"[장착 실패] 카탈로그에 없는 아이템입니다: {itemId}");
            return false;
        }

        return EquipItem(item.itemId, item.category);
    }

    public bool EquipItem(string itemId, string category)
    {
        if (!IsOwned(itemId))
        {
            Debug.LogWarning($"[장착 실패] 보유하지 않은 아이템입니다: {itemId}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            Debug.LogWarning("[장착 실패] 카테고리가 비어 있습니다.");
            return false;
        }

        string normalizedCategory = category.Trim();
        string normalizedItemId = itemId.Trim();

        equippedItems[normalizedCategory] = normalizedItemId;
        previewItems.Remove(normalizedCategory);

        SaveData();
        OnItemEquipped?.Invoke(normalizedItemId);
        OnInventoryChanged?.Invoke();
        OnPreviewChanged?.Invoke();

        Debug.Log($"[장착 완료] {normalizedCategory}: {normalizedItemId}");
        return true;
    }

    public bool UnequipCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        string normalizedCategory = category.Trim();
        bool removed = equippedItems.Remove(normalizedCategory);
        previewItems.Remove(normalizedCategory);

        if (!removed)
        {
            return false;
        }

        SaveData();
        OnInventoryChanged?.Invoke();
        OnPreviewChanged?.Invoke();
        return true;
    }

    public string GetEquippedItemId(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        return equippedItems.TryGetValue(category.Trim(), out string itemId)
            ? itemId
            : string.Empty;
    }

    /// <summary>
    /// Temporary page preview takes priority over the saved equipped item.
    /// </summary>
    public string GetDisplayedItemId(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        string normalizedCategory = category.Trim();
        if (previewItems.TryGetValue(normalizedCategory, out string previewItemId))
        {
            return previewItemId;
        }

        return GetEquippedItemId(normalizedCategory);
    }

    public bool PreviewItem(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        if (item == null || string.IsNullOrWhiteSpace(item.category))
        {
            return false;
        }

        previewItems[item.category.Trim()] = item.itemId;
        OnPreviewChanged?.Invoke();
        return true;
    }

    public void ClearPreviewForCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        if (previewItems.Remove(category.Trim()))
        {
            OnPreviewChanged?.Invoke();
        }
    }

    public void ClearAllPreviews()
    {
        if (previewItems.Count == 0)
        {
            return;
        }

        previewItems.Clear();
        OnPreviewChanged?.Invoke();
    }

    public bool IsEquipped(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        string normalizedId = itemId.Trim();
        foreach (string equippedId in equippedItems.Values)
        {
            if (equippedId == normalizedId)
            {
                return true;
            }
        }

        return false;
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
        equippedItems.Clear();
        previewItems.Clear();

        PlayerPrefs.DeleteKey(OwnedItemsKey);
        PlayerPrefs.DeleteKey(EquippedItemsKey);
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

        if (string.IsNullOrWhiteSpace(item.visualObjectName))
        {
            item.visualObjectName = GetDefaultVisualObjectName(item.itemId);
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

        EquippedItemSaveData equippedSaveData = new EquippedItemSaveData();
        foreach (KeyValuePair<string, string> pair in equippedItems)
        {
            equippedSaveData.entries.Add(new EquippedItemEntry
            {
                category = pair.Key,
                itemId = pair.Value
            });
        }

        PlayerPrefs.SetString(OwnedItemsKey, JsonUtility.ToJson(ownedSaveData));
        PlayerPrefs.SetString(EquippedItemsKey, JsonUtility.ToJson(equippedSaveData));
        PlayerPrefs.Save();
    }

    private void LoadData()
    {
        ownedItemIds.Clear();
        equippedItems.Clear();
        previewItems.Clear();

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

        string equippedJson = PlayerPrefs.GetString(EquippedItemsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(equippedJson))
        {
            EquippedItemSaveData equippedSaveData =
                JsonUtility.FromJson<EquippedItemSaveData>(equippedJson);

            if (equippedSaveData?.entries != null)
            {
                foreach (EquippedItemEntry entry in equippedSaveData.entries)
                {
                    if (entry == null ||
                        string.IsNullOrWhiteSpace(entry.category) ||
                        string.IsNullOrWhiteSpace(entry.itemId))
                    {
                        continue;
                    }

                    string category = entry.category.Trim();
                    string itemId = entry.itemId.Trim();

                    if (IsOwned(itemId) && GetItem(itemId) != null)
                    {
                        equippedItems[category] = itemId;
                    }
                }
            }
        }
    }
}
