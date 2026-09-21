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

    [Header("Grid Visibility")]
    [Tooltip("한 줄에 배치할 아이템 수입니다. 4열 x 3행 = 12칸.")]
    [SerializeField, Min(1)] private int gridColumns = 4;
    [Tooltip("설명창이 없을 때 세로로 모두 보이게 할 줄 수입니다.")]
    [SerializeField, Min(1)] private int fullVisibleRows = 3;
    [Tooltip("설명창이 열렸을 때 완전히 보일 줄 수입니다. 그 다음 줄부터 가려집니다.")]
    [SerializeField, Min(1)] private int visibleRowsWhenInfoOpen = 2;
    [Tooltip("아이템 그리드와 설명창 사이 간격입니다. -1이면 Grid Spacing Y와 동일하게 씁니다.")]
    [SerializeField] private float informationBoxTopMargin = -1f;

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
    private bool scrollTopCached;
    private float cachedScrollAnchoredX;
    private float cachedScrollTopAnchoredY;
    private float cachedScrollWidth;

    public ItemPageMode PageMode => pageMode;
    public string SelectedItemId => selectedItemId;

    private void Awake()
    {
        // 상점과 마이페이지에서는 카드를 선택하면 항상 더리에게 미리보기가 적용됩니다.
        previewSelectedItemOnDurry = true;
        ResolveAllReferences();
        EnsureScrollableContentLayout(resetScrollToTop: true);
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

        if (manager.BuyShopItem(selectedItemId))
        {
            PlayDurryHappyReactionOnly();
        }

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
            PlayDurryHappyReactionOnly();
        }

        RefreshPage();
    }

    private static void NotifyPageInteraction()
    {
        FindFirstObjectByType<DustinyDemoFlow>()?.NotifyPageInteraction();
    }

    private static void PlayDurryHappyReactionOnly()
    {
        FindFirstObjectByType<DustinyDemoFlow>()?.PlayDurryHappyReactionOnly();
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
            if (autoSelectFirstItem)
            {
                selectedItemId = firstItemId;
            }
            else
            {
                // 상점/마이페이지 모두 사용자가 카드를 고를 때까지 자동 선택하지 않습니다.
                selectedItemId = string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedItemId) && previewSelectedItemOnDurry)
        {
            manager.PreviewItem(selectedItemId);
        }
        else
        {
            manager.ClearAllPreviews();
        }

        EnsureScrollableContentLayout(resetScrollToTop: true);
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

        EnsureScrollableContentLayout(resetScrollToTop: false);
        RefreshCardsOnly();
        RefreshInformationBox();
    }

    /// <summary>
    /// Sizes the item scroll area to whole card rows so nothing is clipped mid-card.
    /// Default: exactly 3 rows (4x3 = 12). With InformationBox: exactly 2 rows, row 3 covered.
    /// </summary>
    private void EnsureScrollableContentLayout(bool resetScrollToTop = false)
    {
        RectTransform content = contentRoot as RectTransform;
        if (content == null)
        {
            return;
        }

        RectTransform scrollRoot = content.parent as RectTransform;
        if (scrollRoot == null)
        {
            return;
        }

        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(100f, 100f);
            grid.spacing = new Vector2(20f, 20f);
        }

        int columns = Mathf.Max(1, gridColumns);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;

        CacheScrollTopIfNeeded(scrollRoot);

        bool infoVisible = informationBox != null && informationBox.activeSelf;
        int visibleRows = infoVisible
            ? Mathf.Max(1, visibleRowsWhenInfoOpen)
            : Mathf.Max(1, fullVisibleRows);

        float cellsWidth = columns * grid.cellSize.x +
                           Mathf.Max(0, columns - 1) * grid.spacing.x;
        float viewportHeight = GetGridHeightForRows(grid, visibleRows);
        float scrollWidth = cachedScrollWidth > 1f
            ? cachedScrollWidth
            : Mathf.Max(scrollRoot.sizeDelta.x, cellsWidth);

        ScrollRect existingScroll = scrollRoot.GetComponent<ScrollRect>();
        float preservedContentY = content.anchoredPosition.y;

        // Keep the top edge fixed under the header/coin row while height changes.
        scrollRoot.anchorMin = new Vector2(0.5f, 0.5f);
        scrollRoot.anchorMax = new Vector2(0.5f, 0.5f);
        scrollRoot.pivot = new Vector2(0.5f, 1f);
        scrollRoot.anchoredPosition = new Vector2(cachedScrollAnchoredX, cachedScrollTopAnchoredY);
        scrollRoot.sizeDelta = new Vector2(scrollWidth, viewportHeight);

        int horizontalPad = Mathf.Max(
            0,
            Mathf.RoundToInt((scrollWidth - cellsWidth) * 0.5f)
        );
        grid.padding.left = horizontalPad;
        grid.padding.right = horizontalPad;
        grid.padding.top = 0;
        grid.padding.bottom = 0;

        // Content grows downward from the top of the scroll area.
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = new Vector2(0f, resetScrollToTop ? 0f : preservedContentY);
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
        content.localScale = Vector3.one;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        }

        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image scrollImage = scrollRoot.GetComponent<Image>();
        if (scrollImage == null)
        {
            scrollImage = scrollRoot.gameObject.AddComponent<Image>();
            scrollImage.color = new Color(1f, 1f, 1f, 0f);
        }

        scrollImage.raycastTarget = true;

        if (scrollRoot.GetComponent<RectMask2D>() == null)
        {
            scrollRoot.gameObject.AddComponent<RectMask2D>();
        }

        ScrollRect scrollRect = existingScroll;
        if (scrollRect == null)
        {
            scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
        }

        scrollRect.content = content;
        scrollRect.viewport = scrollRoot;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRoot);
        scrollRect.StopMovement();

        if (resetScrollToTop)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
        }
        else
        {
            // Keep the user's current place in the list after selecting an item.
            // Prefer content Y so viewport height changes don't jump back to the top.
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, preservedContentY);
        }

        if (infoVisible)
        {
            float gap = informationBoxTopMargin >= 0f
                ? informationBoxTopMargin
                : Mathf.Max(0f, grid.spacing.y);
            AlignInformationBoxBelowScroll(scrollRoot, gap);
        }
    }

    private void CacheScrollTopIfNeeded(RectTransform scrollRoot)
    {
        if (scrollTopCached || scrollRoot == null)
        {
            return;
        }

        // Convert whatever inspector pivot/anchors were to a stable top-edge Y.
        float topY = scrollRoot.anchoredPosition.y +
                     scrollRoot.sizeDelta.y * (1f - scrollRoot.pivot.y);
        cachedScrollAnchoredX = scrollRoot.anchoredPosition.x;
        cachedScrollTopAnchoredY = topY;
        cachedScrollWidth = Mathf.Max(1f, scrollRoot.sizeDelta.x);
        scrollTopCached = true;
    }

    private static float GetGridHeightForRows(GridLayoutGroup grid, int rows)
    {
        int safeRows = Mathf.Max(1, rows);
        return safeRows * grid.cellSize.y +
               Mathf.Max(0, safeRows - 1) * grid.spacing.y;
    }

    private void AlignInformationBoxBelowScroll(RectTransform scrollRoot, float topGap)
    {
        if (informationBox == null || scrollRoot == null)
        {
            return;
        }

        RectTransform infoRect = informationBox.transform as RectTransform;
        RectTransform parent = scrollRoot.parent as RectTransform;
        if (infoRect == null || parent == null)
        {
            return;
        }

        Vector3[] corners = new Vector3[4];
        scrollRoot.GetWorldCorners(corners);
        Vector3 scrollBottomLocal = parent.InverseTransformPoint(
            (corners[0] + corners[3]) * 0.5f
        );

        float parentBottomLocalY = -parent.rect.height * parent.pivot.y;
        float scrollBottomFromParentBottom = scrollBottomLocal.y - parentBottomLocalY;

        // Leave the same vertical breathing room as between item rows, then place the info panel.
        float infoTopFromParentBottom = scrollBottomFromParentBottom - Mathf.Max(0f, topGap);

        infoRect.anchorMin = new Vector2(0f, 0f);
        infoRect.anchorMax = new Vector2(1f, 0f);
        infoRect.pivot = new Vector2(0.5f, 1f);
        infoRect.anchoredPosition = new Vector2(
            infoRect.anchoredPosition.x,
            infoTopFromParentBottom
        );
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
        ResolveInformationTextReferences();

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
            if (!itemNameText.gameObject.activeSelf)
            {
                itemNameText.gameObject.SetActive(true);
            }

            itemNameText.text = item.displayName;
            itemNameText.ForceMeshUpdate();
        }

        if (itemDescriptionText != null)
        {
            if (!itemDescriptionText.gameObject.activeSelf)
            {
                itemDescriptionText.gameObject.SetActive(true);
            }

            itemDescriptionText.enabled = true;
            itemDescriptionText.text = item.description ?? string.Empty;
            itemDescriptionText.ForceMeshUpdate();
        }
        else
        {
            Debug.LogWarning(
                "[아이템 페이지] 아이템 설명 텍스트를 찾지 못했습니다. " +
                "InformationBox 하위의 ItemDescription / ItemDescription1My를 확인하세요.",
                this
            );
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
                          FindExact("4ContentBox") ??
                          FindExact("ContentBoxMy") ??
                          FindExact("4ContentBoxMy") ??
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
                : FindExact("ItemDescription1My") ??
                  FindExact("ItemDescriptionMy") ??
                  FindContains("itemdescription");

            itemDescriptionText = found != null ? found.GetComponent<TMP_Text>() : null;
        }

        ResolveInformationTextReferences();

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

        EnsureScrollableContentLayout(resetScrollToTop: false);
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

    private void ResolveInformationTextReferences()
    {
        if (informationBox == null)
        {
            return;
        }

        Transform infoRoot = informationBox.transform;

        if (itemNameText == null || !IsTransformUnder(itemNameText.transform, infoRoot))
        {
            Transform found = pageMode == ItemPageMode.Shop
                ? FindExactUnder(infoRoot, "ItemName")
                : FindExactUnder(infoRoot, "ItemNameMy") ?? FindExactUnder(infoRoot, "ItemName");
            itemNameText = found != null ? found.GetComponent<TMP_Text>() : null;
        }

        if (itemDescriptionText == null || !IsTransformUnder(itemDescriptionText.transform, infoRoot))
        {
            Transform found = pageMode == ItemPageMode.Shop
                ? FindExactUnder(infoRoot, "ItemDescription")
                : FindExactUnder(infoRoot, "ItemDescription1My") ??
                  FindExactUnder(infoRoot, "ItemDescriptionMy") ??
                  FindContainsUnder(infoRoot, "itemdescription");
            itemDescriptionText = found != null ? found.GetComponent<TMP_Text>() : null;
        }
    }

    private static bool IsTransformUnder(Transform child, Transform ancestor)
    {
        if (child == null || ancestor == null)
        {
            return false;
        }

        Transform current = child;
        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private Transform FindExactUnder(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child != root && child.name == exactName)
            {
                return child;
            }
        }

        return null;
    }

    private Transform FindContainsUnder(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string normalizedKeyword = keyword.ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null &&
                child != root &&
                child.name.ToLowerInvariant().Contains(normalizedKeyword))
            {
                return child;
            }
        }

        return null;
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
