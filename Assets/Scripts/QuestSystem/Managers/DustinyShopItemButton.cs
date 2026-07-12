using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connect one instance to one Shop item button/slot.
/// It buys an unowned item and equips an owned item.
/// </summary>
public class DustinyShopItemButton : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private string itemId = "item1";
    [SerializeField] private bool equipImmediatelyAfterPurchase;

    [Header("UI References")]
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject ownedMark;
    [SerializeField] private GameObject equippedMark;

    private void Awake()
    {
        if (actionButton == null)
        {
            actionButton = GetComponent<Button>();
        }
    }

    private void OnEnable()
    {
        if (actionButton != null)
        {
            actionButton.onClick.RemoveListener(HandleClick);
            actionButton.onClick.AddListener(HandleClick);
        }

        ShopInventoryManager.OnInventoryChanged += Refresh;
        CreditManager.OnCreditChanged += HandleCreditChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (actionButton != null)
        {
            actionButton.onClick.RemoveListener(HandleClick);
        }

        ShopInventoryManager.OnInventoryChanged -= Refresh;
        CreditManager.OnCreditChanged -= HandleCreditChanged;
    }

    public void SetItemId(string newItemId)
    {
        itemId = newItemId;
        Refresh();
    }

    public void HandleClick()
    {
        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[상점 버튼] ShopInventoryManager가 없습니다.", this);
            return;
        }

        if (!manager.IsOwned(itemId))
        {
            bool purchased = manager.BuyShopItem(itemId);
            if (purchased && equipImmediatelyAfterPurchase)
            {
                manager.EquipItem(itemId);
            }
        }
        else if (!manager.IsEquipped(itemId))
        {
            manager.EquipItem(itemId);
        }

        Refresh();
    }

    public void Refresh()
    {
        ShopInventoryManager manager = ShopInventoryManager.Instance;
        ShopInventoryManager.ShopItemDefinition item = manager?.GetItem(itemId);

        if (item == null)
        {
            if (stateText != null)
            {
                stateText.text = "ITEM ID 확인";
            }

            if (actionButton != null)
            {
                actionButton.interactable = false;
            }

            return;
        }

        bool owned = manager.IsOwned(item.itemId);
        bool equipped = manager.IsEquipped(item.itemId);
        bool affordable = manager.CanAfford(item.itemId);

        if (itemNameText != null)
        {
            itemNameText.text = item.displayName;
        }

        if (priceText != null)
        {
            priceText.text = owned ? string.Empty : $"{item.price} CR";
        }

        if (iconImage != null)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = item.icon != null;
        }

        if (ownedMark != null)
        {
            ownedMark.SetActive(owned);
        }

        if (equippedMark != null)
        {
            equippedMark.SetActive(equipped);
        }

        if (stateText != null)
        {
            if (equipped)
            {
                stateText.text = "장착 중";
            }
            else if (owned)
            {
                stateText.text = "장착";
            }
            else if (affordable)
            {
                stateText.text = "구매";
            }
            else
            {
                stateText.text = "CR 부족";
            }
        }

        if (actionButton != null)
        {
            actionButton.interactable = !equipped && (owned || affordable);
        }
    }

    private void HandleCreditChanged(int _)
    {
        Refresh();
    }
}
