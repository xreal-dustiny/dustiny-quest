using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates every Shop/MyPage item card automatically from one template.
///
/// Required hierarchy in each page:
/// - one Content root (ContentBox / ContentBoxMy / Content)
/// - one template card (ShopItemCard / MyItemCard / ItemCardTemplate)
///
/// The template itself only needs Image + Button. At runtime this controller:
/// 1) hides the template,
/// 2) clones one card per catalog item,
/// 3) adds DustinyShopItemButton if missing,
/// 4) binds the item ID and icon automatically.
/// </summary>
public class DustinyItemPageController : MonoBehaviour
{
    public enum ItemPageMode
    {
        Shop,
        MyPage
    }

    [Header("Page Mode")]
    [SerializeField] private ItemPageMode pageMode = ItemPageMode.Shop;

    [Header("Automatic Card Generation")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject cardTemplate;
    [SerializeField] private bool autoResolveHierarchyByName = true;
    [SerializeField] private bool previewSelectedItemOnDurry = true;
    [SerializeField] private bool autoSelectFirstItem = false;

    [Header("Information Box")]
    [SerializeField] private GameObject informationBox;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text itemDescriptionText;

    [Header("Shop Buy Button")]
    [SerializeField] private GameObject buyButtonObject;
    [SerializeField] private Button buyButton;
    [SerializeField] private TMP_Text buyButtonText;

    [Header("MyPage Equip Button")]
    [SerializeField] private GameObject equipButtonObject;
    [SerializeField] private Button equipButton;

    [Header("Empty MyPage - Optional")]
    [SerializeField] private GameObject noOwnedItemMessage;

    private readonly List<DustinyShopItemButton> generatedCards =
        new List<DustinyShopItemButton>();

    private string selectedItemId = string.Empty;
    private bool eventsConnected;

    public ItemPageMode PageMode => pageMode;
    public string SelectedItemId => selectedItemId;

    private void Awake()
    {
        // 상점과 마이페이지에서는 카드를 선택하면 항상 더리에게 미리보기가 적용됩니다.
        previewSelectedItemOnDurry = true;
        ResolveAllReferences();
        HideTemplate();
    }

    private void OnValidate()
    {
        previewSelectedItemOnDurry = true;
    }

    private void OnEnable()
    {
        ResolveAllReferences();
        ConnectEvents();
        RebuildCards();
    }

    private void Start()
    {
        // Manager script execution order가 늦어도 한 번 더 생성합니다.
        if (generatedCards.Count == 0)
        {
            RebuildCards();
        }
    }

    private void OnDisable()
    {
        DisconnectEvents();
        ShopInventoryManager.Instance?.ClearAllPreviews();
        selectedItemId = string.Empty;
    }

    public bool IsSelected(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && selectedItemId == itemId.Trim();
    }

    public void SelectItem(string itemId)
    {
        NotifyPageInteraction();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        ShopInventoryManager.ShopItemDefinition item = manager?.GetItem(itemId);

        if (manager == null || item == null)
        {
            Debug.LogWarning($"[아이템 페이지] 등록되지 않은 아이템 선택: {itemId}", this);
            return;
        }

        if (pageMode == ItemPageMode.MyPage && !manager.IsOwned(item.itemId))
        {
            return;
        }

        selectedItemId = item.itemId;

        if (previewSelectedItemOnDurry)
        {
            // Shop/MyPage 모두 전역 미리보기 하나만 사용합니다.
            // 새 카드를 선택하면 이전 미리보기는 자동으로 취소됩니다.
            manager.PreviewItem(item.itemId);
            RefreshDurryItemVisuals();
        }

        RefreshCardsOnly();
        RefreshInformationBox();
    }

    public void BuySelectedItem()
    {
        NotifyPageInteraction();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(selectedItemId))
        {
            return;
        }

        manager.BuyShopItem(selectedItemId);
        RefreshPage();
    }

    public void EquipSelectedItem()
    {
        NotifyPageInteraction();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(selectedItemId))
        {
            return;
        }

        // ShopInventoryManager가 이전 착용 아이템을 자동 해제하고
        // 선택한 아이템 하나만 전역 착용 상태로 저장합니다.
        if (manager.EquipItem(selectedItemId))
        {
            RefreshDurryItemVisuals();
        }

        RefreshPage();
    }

    private static void NotifyPageInteraction()
    {
        FindFirstObjectByType<DustinyDemoFlow>()?.NotifyPageInteraction();
    }

    [ContextMenu("Rebuild Item Cards")]
    public void RebuildCards()
    {
        ResolveAllReferences();
        ClearGeneratedCards();
        HideTemplate();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null || contentRoot == null || cardTemplate == null)
        {
            SetInformationVisible(false);

            if (manager == null)
            {
                Debug.LogWarning("[아이템 페이지] ShopInventoryManager를 찾지 못했습니다.", this);
            }
            if (contentRoot == null)
            {
                Debug.LogWarning("[아이템 페이지] Content Root를 찾지 못했습니다.", this);
            }
            if (cardTemplate == null)
            {
                Debug.LogWarning("[아이템 페이지] 카드 템플릿을 찾지 못했습니다.", this);
            }
            return;
        }

        string firstItemId = string.Empty;

        foreach (ShopInventoryManager.ShopItemDefinition item in manager.ShopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            if (pageMode == ItemPageMode.MyPage && !manager.IsOwned(item.itemId))
            {
                continue;
            }

            GameObject cardObject = Instantiate(cardTemplate, contentRoot, false);
            cardObject.name = $"{cardTemplate.name}_{item.itemId}";
            cardObject.SetActive(false);

            DustinyShopItemButton card = cardObject.GetComponent<DustinyShopItemButton>();
            if (card == null)
            {
                card = cardObject.AddComponent<DustinyShopItemButton>();
            }

            card.Initialize(this, item.itemId);
            generatedCards.Add(card);
            cardObject.SetActive(true);

            if (string.IsNullOrWhiteSpace(firstItemId))
            {
                firstItemId = item.itemId;
            }
        }

        if (noOwnedItemMessage != null)
        {
            noOwnedItemMessage.SetActive(
                pageMode == ItemPageMode.MyPage && generatedCards.Count == 0
            );
        }

        bool selectionIsStillValid = !string.IsNullOrWhiteSpace(selectedItemId) &&
                                     manager.GetItem(selectedItemId) != null &&
                                     (pageMode == ItemPageMode.Shop || manager.IsOwned(selectedItemId));

        if (!selectionIsStillValid)
        {
            selectedItemId = autoSelectFirstItem ? firstItemId : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(selectedItemId) && previewSelectedItemOnDurry)
        {
            manager.PreviewItem(selectedItemId);
        }
        else
        {
            manager.ClearAllPreviews();
        }

        RefreshCardsOnly();
        RefreshInformationBox();
    }

    [ContextMenu("Refresh Item Page")]
    public void RefreshPage()
    {
        if (generatedCards.Count == 0)
        {
            RebuildCards();
            return;
        }

        RefreshCardsOnly();
        RefreshInformationBox();
    }

    public void ClearSelectionAndPreview()
    {
        selectedItemId = string.Empty;
        ShopInventoryManager.Instance?.ClearAllPreviews();
        RefreshDurryItemVisuals();
        RefreshCardsOnly();
        RefreshInformationBox();
    }

    private static void RefreshDurryItemVisuals()
    {
        DustinyEquippedItemVisual[] visuals =
            FindObjectsByType<DustinyEquippedItemVisual>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (DustinyEquippedItemVisual visual in visuals)
        {
            if (visual != null)
            {
                visual.ForceRefreshVisual();
            }
        }
    }

    private void HandleInventoryChanged()
    {
        // MyPage의 상품 수는 구매 결과에 따라 달라지므로 다시 생성합니다.
        if (pageMode == ItemPageMode.MyPage)
        {
            RebuildCards();
        }
        else
        {
            RefreshPage();
        }
    }

    private void HandleCreditChanged(int _)
    {
        RefreshInformationBox();
    }

    private void RefreshCardsOnly()
    {
        foreach (DustinyShopItemButton card in generatedCards)
        {
            if (card != null)
            {
                card.Refresh();
            }
        }
    }

    private void RefreshInformationBox()
    {
        ShopInventoryManager manager = ShopInventoryManager.Instance;
        ShopInventoryManager.ShopItemDefinition item = manager?.GetItem(selectedItemId);

        if (manager == null || item == null)
        {
            SetInformationVisible(false);
            return;
        }

        SetInformationVisible(true);

        if (itemNameText != null)
        {
            itemNameText.text = item.displayName;
        }

        if (itemDescriptionText != null)
        {
            itemDescriptionText.text = item.description;
        }

        bool owned = manager.IsOwned(item.itemId);
        bool equipped = manager.IsEquipped(item.itemId);

        if (pageMode == ItemPageMode.Shop)
        {
            SetBuyButtonVisible(true);
            SetEquipButtonVisible(false);

            if (buyButtonText != null)
            {
                buyButtonText.text = owned
                    ? "구매 완료"
                    : $"{item.price}코인으로 구매하기";
            }

            if (buyButton != null)
            {
                buyButton.interactable = !owned && manager.CanAfford(item.itemId);
            }
        }
        else
        {
            SetBuyButtonVisible(false);
            SetEquipButtonVisible(true);

            if (equipButton != null)
            {
                equipButton.interactable = owned && !equipped;
            }
        }
    }

    private void ResolveAllReferences()
    {
        if (autoResolveHierarchyByName)
        {
            ResolveHierarchyByName();
        }

        if (buyButton == null && buyButtonObject != null)
        {
            buyButton = buyButtonObject.GetComponent<Button>() ??
                        buyButtonObject.GetComponentInChildren<Button>(true);
        }

        if (buyButtonObject == null && buyButton != null)
        {
            buyButtonObject = buyButton.gameObject;
        }

        if (buyButtonText == null && buyButtonObject != null)
        {
            buyButtonText = buyButtonObject.GetComponentInChildren<TMP_Text>(true);
        }

        if (equipButton == null && equipButtonObject != null)
        {
            equipButton = equipButtonObject.GetComponent<Button>() ??
                          equipButtonObject.GetComponentInChildren<Button>(true);
        }

        if (equipButtonObject == null && equipButton != null)
        {
            equipButtonObject = equipButton.gameObject;
        }
    }

    private void ResolveHierarchyByName()
    {
        if (contentRoot == null)
        {
            contentRoot = FindExact("ContentBox") ??
                          FindExact("ContentBoxMy") ??
                          FindExact("Content");
        }

        if (cardTemplate == null)
        {
            Transform template = pageMode == ItemPageMode.Shop
                ? FindExact("ShopItemCard")
                : FindExact("MyItemCard");

            template = template ?? FindExact("ItemCardTemplate") ?? FindContains("itemcard");
            if (template != null)
            {
                cardTemplate = template.gameObject;
            }
        }

        if (informationBox == null)
        {
            Transform found = pageMode == ItemPageMode.Shop
                ? FindExact("InformationBox")
                : FindExact("InformationBoxMy");

            found = found ?? FindContains("informationbox");
            if (found != null)
            {
                informationBox = found.gameObject;
            }
        }

        if (itemNameText == null)
        {
            Transform found = pageMode == ItemPageMode.Shop
                ? FindExact("ItemName")
                : FindExact("ItemNameMy");
            itemNameText = found != null ? found.GetComponent<TMP_Text>() : null;
        }

        if (itemDescriptionText == null)
        {
            Transform found = pageMode == ItemPageMode.Shop
                ? FindExact("ItemDescription")
                : FindExact("ItemDescriptionMy");
            itemDescriptionText = found != null ? found.GetComponent<TMP_Text>() : null;
        }

        if (pageMode == ItemPageMode.Shop && buyButtonObject == null)
        {
            Transform found = FindExact("BuyButton");
            if (found != null)
            {
                buyButtonObject = found.gameObject;
            }
        }

        if (pageMode == ItemPageMode.MyPage && equipButtonObject == null)
        {
            Transform found = FindExact("EquipButton");
            if (found != null)
            {
                equipButtonObject = found.gameObject;
            }
        }
    }

    private void HideTemplate()
    {
        if (cardTemplate != null)
        {
            cardTemplate.SetActive(false);
        }
    }

    private void ClearGeneratedCards()
    {
        foreach (DustinyShopItemButton card in generatedCards)
        {
            if (card != null)
            {
                card.gameObject.SetActive(false);
                Destroy(card.gameObject);
            }
        }

        generatedCards.Clear();
    }

    private void ConnectEvents()
    {
        if (eventsConnected)
        {
            return;
        }

        ShopInventoryManager.OnInventoryChanged += HandleInventoryChanged;
        CreditManager.OnCreditChanged += HandleCreditChanged;

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(BuySelectedItem);
            buyButton.onClick.AddListener(BuySelectedItem);
        }

        if (equipButton != null)
        {
            equipButton.onClick.RemoveListener(EquipSelectedItem);
            equipButton.onClick.AddListener(EquipSelectedItem);
        }

        eventsConnected = true;
    }

    private void DisconnectEvents()
    {
        if (!eventsConnected)
        {
            return;
        }

        ShopInventoryManager.OnInventoryChanged -= HandleInventoryChanged;
        CreditManager.OnCreditChanged -= HandleCreditChanged;

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(BuySelectedItem);
        }

        if (equipButton != null)
        {
            equipButton.onClick.RemoveListener(EquipSelectedItem);
        }

        eventsConnected = false;
    }

    private void SetInformationVisible(bool visible)
    {
        if (informationBox != null)
        {
            informationBox.SetActive(visible);
        }

        if (!visible)
        {
            SetBuyButtonVisible(false);
            SetEquipButtonVisible(false);
        }
    }

    private void SetBuyButtonVisible(bool visible)
    {
        if (buyButtonObject != null)
        {
            buyButtonObject.SetActive(visible && pageMode == ItemPageMode.Shop);
        }
    }

    private void SetEquipButtonVisible(bool visible)
    {
        if (equipButtonObject != null)
        {
            equipButtonObject.SetActive(visible && pageMode == ItemPageMode.MyPage);
        }
    }

    private Transform FindExact(string exactName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name == exactName)
            {
                return child;
            }
        }

        return null;
    }

    private Transform FindContains(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string normalizedKeyword = keyword.ToLowerInvariant();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name.ToLowerInvariant().Contains(normalizedKeyword))
            {
                return child;
            }
        }

        return null;
    }
}
