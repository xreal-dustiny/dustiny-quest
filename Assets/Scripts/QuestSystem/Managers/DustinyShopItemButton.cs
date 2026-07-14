using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated selectable item card.
///
/// A card template only needs an Image and Button. This component is added
/// automatically when missing, and it creates/fills the child ItemIcon Image.
/// No item ID or icon is assigned on individual cards.
/// </summary>
public class DustinyShopItemButton : MonoBehaviour
{
    [Header("Auto References")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Image iconImage;

    [Header("Generated Icon Layout")]
    [SerializeField, Min(0f)] private float iconPadding = 14f;

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

        SetMark(selectedMark, selected);
        SetMark(ownedMark, owned);
        SetMark(equippedMark, equipped);

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
