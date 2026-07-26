using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated selectable item card.
///
/// The card template only needs an Image and Button. This component is added
/// automatically and creates the ItemIcon and purchased-state dim overlay.
/// </summary>
public class DustinyShopItemButton : MonoBehaviour
{
    [Header("Auto References")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Image iconImage;

    [Header("Generated Icon Layout")]
    [SerializeField, Min(0f)] private float iconPadding = 14f;

    [Header("Purchased Shop Card")]
    [Tooltip("상점에서 이미 구매한 카드 위에 회색 오버레이를 표시합니다.")]
    [SerializeField] private bool dimOwnedCardsInShop = true;

    [Tooltip("구매 완료 카드 위에 덮이는 회색입니다. A=0.70이면 약 70% 농도입니다.")]
    [SerializeField] private Color ownedDimColor = new Color(0.35f, 0.35f, 0.35f, 0.70f);

    [SerializeField] private Image ownedDimOverlay;

    [Header("Optional State Marks")]
    [SerializeField] private GameObject selectedMark;
    [SerializeField] private GameObject ownedMark;
    [SerializeField] private GameObject equippedMark;

    private DustinyItemPageController pageController;
    private string itemId = string.Empty;
    private bool initialized;

    public string ItemId => itemId;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConnectClick();
        ShopInventoryManager.OnInventoryChanged += Refresh;

        if (initialized)
        {
            Refresh();
        }
    }

    private void OnDisable()
    {
        DisconnectClick();
        ShopInventoryManager.OnInventoryChanged -= Refresh;
    }

    public void Initialize(DustinyItemPageController owner, string newItemId)
    {
        pageController = owner;
        itemId = string.IsNullOrWhiteSpace(newItemId) ? string.Empty : newItemId.Trim();
        initialized = true;

        ResolveReferences();
        ConnectClick();
        Refresh();
    }

    public void SetCardVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    public void HandleCardSelected()
    {
        if (!initialized || string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        pageController?.SelectItem(itemId);
    }

    public void Refresh()
    {
        if (!initialized)
        {
            return;
        }

        ResolveReferences();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        ShopInventoryManager.ShopItemDefinition item = manager?.GetItem(itemId);

        if (item == null)
        {
            if (iconImage != null)
            {
                iconImage.enabled = false;
            }

            SetOwnedDimVisible(false);

            if (selectButton != null)
            {
                selectButton.interactable = false;
            }

            SetMark(selectedMark, false);
            SetMark(ownedMark, false);
            SetMark(equippedMark, false);
            return;
        }

        EnsureIconImage();
        if (iconImage != null)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = item.icon != null;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        bool owned = manager.IsOwned(item.itemId);
        bool equipped = manager.IsEquipped(item.itemId);
        bool selected = pageController != null && pageController.IsSelected(item.itemId);
        bool isShopCard = pageController != null &&
                          pageController.PageMode == DustinyItemPageController.ItemPageMode.Shop;

        SetOwnedDimVisible(dimOwnedCardsInShop && isShopCard && owned);
        SetMark(selectedMark, selected);
        SetMark(ownedMark, owned);
        SetMark(equippedMark, equipped);
        BringStateMarksToFront();

        // 구매 완료 카드도 눌러서 정보/미리보기는 확인할 수 있습니다.
        // 실제 재구매는 InformationBox의 BuyButton과 BuyShopItem 양쪽에서 차단됩니다.
        if (selectButton != null)
        {
            selectButton.interactable = true;
        }
    }

    private void ResolveReferences()
    {
        if (selectButton == null)
        {
            selectButton = GetComponent<Button>() ?? GetComponentInChildren<Button>(true);
        }

        if (iconImage == null)
        {
            Transform iconTransform = FindChildByName(transform, "ItemIcon") ??
                                      FindChildByName(transform, "IconImage") ??
                                      FindChildByName(transform, "Icon");

            if (iconTransform != null)
            {
                iconImage = iconTransform.GetComponent<Image>();
            }
        }

        if (ownedDimOverlay == null)
        {
            Transform overlayTransform = FindChildByName(transform, "OwnedDimOverlay") ??
                                         FindChildByName(transform, "PurchasedDimOverlay");
            if (overlayTransform != null)
            {
                ownedDimOverlay = overlayTransform.GetComponent<Image>();
            }
        }

        ResolveOptionalMarks();
    }

    private void EnsureIconImage()
    {
        if (iconImage != null)
        {
            ConfigureIconRect(iconImage.rectTransform);
            return;
        }

        GameObject iconObject = new GameObject(
            "ItemIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );

        iconObject.transform.SetParent(transform, false);
        iconImage = iconObject.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.color = Color.white;

        ConfigureIconRect(iconImage.rectTransform);
        iconObject.transform.SetAsLastSibling();
    }

    private void EnsureOwnedDimOverlay()
    {
        if (ownedDimOverlay == null)
        {
            GameObject overlayObject = new GameObject(
                "OwnedDimOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );

            overlayObject.transform.SetParent(transform, false);
            ownedDimOverlay = overlayObject.GetComponent<Image>();
        }

        RectTransform overlayRect = ownedDimOverlay.rectTransform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.localRotation = Quaternion.identity;
        overlayRect.localScale = Vector3.one;

        ownedDimOverlay.color = ownedDimColor;
        ownedDimOverlay.raycastTarget = false;
        ownedDimOverlay.transform.SetAsLastSibling();
    }

    private void SetOwnedDimVisible(bool visible)
    {
        if (visible)
        {
            EnsureOwnedDimOverlay();
        }

        if (ownedDimOverlay != null)
        {
            ownedDimOverlay.color = ownedDimColor;
            ownedDimOverlay.raycastTarget = false;
            ownedDimOverlay.gameObject.SetActive(visible);

            if (visible)
            {
                ownedDimOverlay.transform.SetAsLastSibling();
            }
        }
    }

    private void ConfigureIconRect(RectTransform iconRect)
    {
        if (iconRect == null)
        {
            return;
        }

        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.offsetMin = new Vector2(iconPadding, iconPadding);
        iconRect.offsetMax = new Vector2(-iconPadding, -iconPadding);
        iconRect.localRotation = Quaternion.identity;
        iconRect.localScale = Vector3.one;
    }

    private void ResolveOptionalMarks()
    {
        if (selectedMark == null)
        {
            selectedMark = GetGameObject(FindChildByName(transform, "SelectedMark"));
        }

        if (ownedMark == null)
        {
            ownedMark = GetGameObject(FindChildByName(transform, "OwnedMark"));
        }

        if (equippedMark == null)
        {
            equippedMark = GetGameObject(FindChildByName(transform, "EquippedMark"));
        }
    }

    private void BringStateMarksToFront()
    {
        if (selectedMark != null) selectedMark.transform.SetAsLastSibling();
        if (ownedMark != null) ownedMark.transform.SetAsLastSibling();
        if (equippedMark != null) equippedMark.transform.SetAsLastSibling();
    }

    private void ConnectClick()
    {
        if (selectButton == null)
        {
            return;
        }

        selectButton.onClick.RemoveListener(HandleCardSelected);
        selectButton.onClick.AddListener(HandleCardSelected);
    }

    private void DisconnectClick()
    {
        if (selectButton != null)
        {
            selectButton.onClick.RemoveListener(HandleCardSelected);
        }
    }

    private static Transform FindChildByName(Transform root, string exactName)
    {
        if (root == null)
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

    private static GameObject GetGameObject(Transform target)
    {
        return target != null ? target.gameObject : null;
    }

    private static void SetMark(GameObject mark, bool visible)
    {
        if (mark != null)
        {
            mark.SetActive(visible);
        }
    }
}
