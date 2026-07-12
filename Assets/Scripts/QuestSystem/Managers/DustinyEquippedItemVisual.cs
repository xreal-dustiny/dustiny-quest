using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows the scene visual that matches the equipped item ID for one category.
/// Put this on Durry or on the category visual root.
/// </summary>
public class DustinyEquippedItemVisual : MonoBehaviour
{
    [Serializable]
    public class ItemVisualEntry
    {
        public string itemId;
        public GameObject visualObject;
    }

    [SerializeField] private string category = "accessory";
    [SerializeField] private GameObject defaultVisualObject;
    [SerializeField] private List<ItemVisualEntry> itemVisuals = new List<ItemVisualEntry>();

    private void OnEnable()
    {
        ShopInventoryManager.OnInventoryChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        ShopInventoryManager.OnInventoryChanged -= Refresh;
    }

    [ContextMenu("Refresh Equipped Visual")]
    public void Refresh()
    {
        string equippedItemId = ShopInventoryManager.Instance != null
            ? ShopInventoryManager.Instance.GetEquippedItemId(category)
            : string.Empty;

        bool matched = false;

        foreach (ItemVisualEntry entry in itemVisuals)
        {
            if (entry == null || entry.visualObject == null)
            {
                continue;
            }

            bool shouldShow = !string.IsNullOrWhiteSpace(equippedItemId) &&
                              entry.itemId == equippedItemId;
            entry.visualObject.SetActive(shouldShow);
            matched |= shouldShow;
        }

        if (defaultVisualObject != null)
        {
            defaultVisualObject.SetActive(!matched);
        }
    }
}
