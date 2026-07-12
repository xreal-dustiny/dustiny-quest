using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the Dustiny shop catalog and persists only the player's state.
///
/// Important:
/// - Item names, prices, categories, and icons are catalog data kept in the Inspector.
/// - Purchased/equipped state is saved as item IDs in PlayerPrefs.
/// - Prefabs, sprites, and scene objects are never serialized into PlayerPrefs.
/// </summary>
public class ShopInventoryManager : MonoBehaviour
{
    [Serializable]
    public class ShopItemDefinition
    {
        public string itemId = "item1";
        public string displayName = "아이템 1";
        public string category = "accessory";
        [Min(0)] public int price = 3;
        public Sprite icon;
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

    private const string OwnedItemsKey = "Dustiny_OwnedItems_V2";
    private const string EquippedItemsKey = "Dustiny_EquippedItems_V2";

    [Header("[ MVP 상점 카탈로그 ]")]
    [Tooltip("아이템 자체는 여기에 등록하고, 저장 데이터에는 itemId만 기록합니다.")]
    [SerializeField] private List<ShopItemDefinition> shopItems = new List<ShopItemDefinition>
    {
        new ShopItemDefinition
        {
            itemId = "item1",
            displayName = "아이템 1",
            category = "accessory",
            price = 3
        },
        new ShopItemDefinition
        {
            itemId = "item2",
            displayName = "아이템 2",
            category = "accessory",
            price = 5
        },
        new ShopItemDefinition
        {
            itemId = "item3",
            displayName = "아이템 3",
            category = "accessory",
            price = 8
        },
        new ShopItemDefinition
        {
            itemId = "item4",
            displayName = "아이템 4",
            category = "accessory",
            price = 12
        }
    };

    [Header("[ 저장된 인벤토리 - Runtime Read Only ]")]
    [SerializeField] private List<string> ownedItemIds = new List<string>();

    private readonly Dictionary<string, ShopItemDefinition> itemById =
        new Dictionary<string, ShopItemDefinition>();

    private readonly Dictionary<string, string> equippedItems =
        new Dictionary<string, string>();

    public IReadOnlyList<ShopItemDefinition> ShopItems => shopItems;
    public IReadOnlyList<string> OwnedItemIds => ownedItemIds;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
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
        if (!Application.isPlaying)
        {
            ValidateCatalogInEditor();
        }
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

        int price = Mathf.Max(0, item.price);
        if (price > 0 && !CreditManager.Instance.UseCredit(price))
        {
            return false;
        }

        AddItemToInventory(item.itemId);
        OnItemPurchased?.Invoke(item.itemId);
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
        equippedItems[normalizedCategory] = itemId.Trim();
        SaveData();
        OnItemEquipped?.Invoke(itemId.Trim());
        OnInventoryChanged?.Invoke();
        Debug.Log($"[장착 완료] {normalizedCategory}: {itemId.Trim()}");
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
               CreditManager.Instance.CurrentCredit >= Mathf.Max(0, item.price);
    }

    public int GetPrice(string itemId)
    {
        ShopItemDefinition item = GetItem(itemId);
        return item != null ? Mathf.Max(0, item.price) : -1;
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

    [ContextMenu("Reset Shop Inventory For Debug")]
    public void ResetInventoryForDebug()
    {
        ownedItemIds.Clear();
        equippedItems.Clear();
        PlayerPrefs.DeleteKey(OwnedItemsKey);
        PlayerPrefs.DeleteKey(EquippedItemsKey);
        PlayerPrefs.Save();
        OnInventoryChanged?.Invoke();
        Debug.Log("[상점 디버그] 구매/장착 데이터를 초기화했습니다.");
    }

    private void RebuildCatalogTable()
    {
        itemById.Clear();

        foreach (ShopItemDefinition item in shopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            string normalizedId = item.itemId.Trim();
            item.itemId = normalizedId;
            item.displayName = string.IsNullOrWhiteSpace(item.displayName)
                ? normalizedId
                : item.displayName.Trim();
            item.category = string.IsNullOrWhiteSpace(item.category)
                ? "accessory"
                : item.category.Trim();
            item.price = Mathf.Max(0, item.price);

            if (itemById.ContainsKey(normalizedId))
            {
                Debug.LogWarning($"[상점] itemId가 중복되어 마지막 항목으로 덮어씁니다: {normalizedId}");
            }

            itemById[normalizedId] = item;
        }
    }

    private void ValidateCatalogInEditor()
    {
        HashSet<string> ids = new HashSet<string>();

        foreach (ShopItemDefinition item in shopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            string normalizedId = item.itemId.Trim();
            if (!ids.Add(normalizedId))
            {
                Debug.LogWarning($"[상점] 중복 itemId: {normalizedId}", this);
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
