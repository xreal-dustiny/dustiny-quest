using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automatically finds the real wearable objects under Durry_Main by the
/// catalog's visualObjectName. No per-item Inspector list is required.
///
/// Attach this once to Durry_Main. If Visual Root is empty, this transform is used.
/// </summary>
public class DustinyEquippedItemVisual : MonoBehaviour
{
    [Header("Automatic Durry Visual Search")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private bool logMissingVisualWarnings = true;

    private readonly Dictionary<string, GameObject> visualByItemId =
        new Dictionary<string, GameObject>();

    private readonly HashSet<string> warnedMissingItemIds =
        new HashSet<string>();

    private void OnEnable()
    {
        ShopInventoryManager.OnInventoryChanged += Refresh;
        ShopInventoryManager.OnPreviewChanged += Refresh;
        Refresh();
    }

    private void Start()
    {
        RebuildVisualCache();
        Refresh();
    }

    private void OnDisable()
    {
        ShopInventoryManager.OnInventoryChanged -= Refresh;
        ShopInventoryManager.OnPreviewChanged -= Refresh;
    }

    [ContextMenu("Rebuild Durry Item Visual Cache")]
    public void RebuildVisualCache()
    {
        visualByItemId.Clear();

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null)
        {
            return;
        }

        Transform root = visualRoot != null ? visualRoot : transform;
        Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);

        foreach (ShopInventoryManager.ShopItemDefinition item in manager.ShopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            string targetName = string.IsNullOrWhiteSpace(item.visualObjectName)
                ? item.itemId
                : item.visualObjectName.Trim();

            Transform found = null;
            foreach (Transform candidate in allTransforms)
            {
                if (candidate != null && candidate.name == targetName)
                {
                    found = candidate;
                    break;
                }
            }

            if (found != null)
            {
                visualByItemId[item.itemId] = found.gameObject;
            }
            else if (logMissingVisualWarnings && warnedMissingItemIds.Add(item.itemId))
            {
                Debug.LogWarning(
                    $"[더리 아이템 비주얼] Durry_Main 아래에서 '{targetName}' 오브젝트를 찾지 못했습니다.",
                    this
                );
            }
        }
    }

    [ContextMenu("Refresh Equipped And Preview Visuals")]
    public void Refresh()
    {
        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager == null)
        {
            return;
        }

        if (visualByItemId.Count == 0)
        {
            RebuildVisualCache();
        }

        // First hide every registered wearable object.
        foreach (GameObject visualObject in visualByItemId.Values)
        {
            if (visualObject != null)
            {
                visualObject.SetActive(false);
            }
        }

        // Then show one displayed item per category. Preview has priority.
        HashSet<string> processedCategories = new HashSet<string>();
        foreach (ShopInventoryManager.ShopItemDefinition item in manager.ShopItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.category))
            {
                continue;
            }

            string category = item.category.Trim();
            if (!processedCategories.Add(category))
            {
                continue;
            }

            string displayedItemId = manager.GetDisplayedItemId(category);
            if (!string.IsNullOrWhiteSpace(displayedItemId) &&
                visualByItemId.TryGetValue(displayedItemId, out GameObject visualObject) &&
                visualObject != null)
            {
                visualObject.SetActive(true);
            }
        }
    }
}
