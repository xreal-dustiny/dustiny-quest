using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 더리의 실제 착용 아이템 및 상점 미리보기를 표시합니다.
///
/// 규칙:
/// - 전체 아이템 중 하나만 표시됩니다.
/// - Body_Sleepwear는 SkinnedMeshRenderer일 때 더리의 실제 본으로 다시 연결됩니다.
/// - 슬립웨어의 메시와 전용 머터리얼은 그대로 유지합니다.
/// - 일반 MeshRenderer 슬립웨어는 변형할 수 없으므로 명확한 오류를 출력합니다.
/// - 모자, 안대, 네임택 등의 단단한 아이템은 적절한 본 아래에 붙습니다.
/// </summary>
public class DustinyEquippedItemVisual : MonoBehaviour
{
    [Header("더리 루트")]

    [Tooltip(
        "Durry_Main과 아이템들이 함께 들어 있는 부모입니다.\n" +
        "예: DurryVisual"
    )]
    [SerializeField]
    private Transform visualRoot;

    [Tooltip(
        "더리 Animator와 본이 들어 있는 루트입니다.\n" +
        "예: Durry_Main"
    )]
    [SerializeField]
    private Transform animatedRoot;

    [Tooltip(
        "자동 부착에 실패했을 때 아이템을 붙일 대상입니다.\n" +
        "보통 Durry_Main입니다."
    )]
    [SerializeField]
    private Transform fallbackFollowTarget;

    [SerializeField]
    private bool autoResolveRoots = true;

    [Header("초기 표시 안전장치")]
    [Tooltip("게임이 시작되는 즉시 모든 착용 아이템을 먼저 숨깁니다. 저장된 실제 착용 아이템만 이후 Refresh에서 다시 표시됩니다.")]
    [SerializeField] private bool hideAllWearablesImmediatelyOnAwake = true;

    [Tooltip("매니저 초기화 순서 차이를 대비해 다음 프레임에 한 번 더 착용 상태를 갱신합니다.")]
    [SerializeField] private bool refreshAgainNextFrame = true;

    [Header("슬립웨어 스키닝 설정")]

    [Tooltip(
        "슬립웨어의 메시와 전용 머터리얼은 유지하고, " +
        "SkinnedMeshRenderer의 bones/rootBone만 더리 본으로 다시 연결합니다."
    )]
    [SerializeField]
    private bool keepSleepwearOriginalRenderer = true;

    [Tooltip(
        "Body_Sleepwear가 SkinnedMeshRenderer일 때 본 이름을 기준으로 " +
        "Durry_Main의 실제 본에 다시 연결합니다."
    )]
    [SerializeField]
    private bool bindSleepwearToDurrySkeleton = true;

    [Tooltip(
        "슬립웨어가 아직 MeshRenderer인 경우 위치만 따라가는 기존 방식으로 폴백합니다. " +
        "이 상태에서는 몸 형태에 맞게 늘어나거나 줄어들지 않습니다."
    )]
    [SerializeField]
    private bool fallbackToRigidParentWhenSkinningUnavailable = true;

    [Tooltip(
        "부모를 변경할 때 현재 월드 위치와 회전을 유지합니다."
    )]
    [SerializeField]
    private bool preserveWorldPoseWhenParenting = true;

    [Tooltip(
        "슬립웨어 본 연결 결과와 누락된 본 이름을 Console에 표시합니다."
    )]
    [SerializeField]
    private bool logSleepwearBindingDetails = true;

    [Header("일반 아이템 설정")]

    [Tooltip(
        "모자, 안대, 네임택 같은 일반 아이템을 " +
        "머리 또는 몸통 본 아래에 붙입니다."
    )]
    [SerializeField]
    private bool attachRigidItemsToBone = true;

    [Tooltip(
        "아이템 안에 별도 Animator가 있다면 꺼서 " +
        "더리 Animator와 충돌하지 않게 합니다."
    )]
    [SerializeField]
    private bool disableItemAnimators = true;

    [Header("네임택 부착")]
    [Tooltip("Back_Nametag_01을 붙일 전용 앵커입니다. 비워두면 BackItemAnchor/NametagAnchor를 자동 탐색하고, 없으면 Durry_Main에 붙입니다.")]
    [SerializeField] private Transform backNametagAnchor;

    [SerializeField] private bool autoResolveBackNametagAnchor = true;

    [Tooltip("전용 앵커를 사용할 때 네임택 루트를 앵커 원점에 맞춥니다.")]
    [SerializeField] private bool snapBackNametagToAnchor = true;

    [SerializeField] private Vector3 backNametagLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 backNametagLocalEuler = Vector3.zero;

    [Tooltip("착용 오브젝트가 활성화됐는데 Renderer가 꺼져 있는 경우 자동으로 다시 켭니다.")]
    [SerializeField] private bool forceDisplayedRenderersEnabled = true;

    [Tooltip("아이템 루트만 켜지고 내부 Mesh 자식이 비활성화되어 보이지 않는 문제를 막습니다.")]
    [SerializeField] private bool forceDisplayedHierarchyActive = true;

    [Tooltip("비활성화된 네임택 앵커나 부착 부모 때문에 아이템 전체가 숨는 문제를 막습니다.")]
    [SerializeField] private bool forceDisplayedParentChainActive = true;

    [Header("아이템 그림자")]
    [Tooltip("모자, 안대, 네임택, 잠옷 등 모든 착용/미리보기 아이템이 더리 얼굴과 몸에 그림자를 만들지 않게 합니다.")]
    [SerializeField] private bool disableItemShadowCasting = true;

    [Tooltip("아이템 자체에도 주변 그림자가 드리워지지 않게 합니다.")]
    [SerializeField] private bool disableItemShadowReceiving = true;

    [Header("디버그")]

    [SerializeField]
    private bool logMissingVisualWarnings = true;

    [SerializeField]
    private bool logAttachmentResults = false;

    private readonly Dictionary<string, GameObject> visualByItemId =
        new Dictionary<string, GameObject>();

    private readonly HashSet<string> warnedMissingItemIds =
        new HashSet<string>();

    private Coroutine delayedRefreshCoroutine;

    private static readonly string[] KnownWearableObjectNames =
    {
        "Back_Nametag_01",
        "Body_Sleepwear",
        "Head_Sleepmask",
        "Head_Hat_Hard",
        "Head_Cone",
        "Head_Cap_Baseball"
    };

    private void Awake()
    {
        ResolveRoots();

        // 씬/프리팹에서 네임택이 실수로 Active 상태여도 첫 화면에는 보이지 않게 합니다.
        if (hideAllWearablesImmediatelyOnAwake)
        {
            HideAllWearableVisualsImmediately();
        }
    }

    private void OnEnable()
    {
        ShopInventoryManager.OnInventoryChanged += HandleVisualStateChanged;
        ShopInventoryManager.OnPreviewChanged += HandleVisualStateChanged;

        ResolveRoots();

        if (hideAllWearablesImmediatelyOnAwake)
        {
            HideAllWearableVisualsImmediately();
        }

        RebuildVisualCache();
        Refresh();

        if (refreshAgainNextFrame)
        {
            if (delayedRefreshCoroutine != null)
            {
                StopCoroutine(delayedRefreshCoroutine);
            }

            delayedRefreshCoroutine = StartCoroutine(RefreshNextFrame());
        }
    }

    private void Start()
    {
        RebuildVisualCache();
        Refresh();
    }

    private void OnDisable()
    {
        ShopInventoryManager.OnInventoryChanged -= HandleVisualStateChanged;
        ShopInventoryManager.OnPreviewChanged -= HandleVisualStateChanged;

        if (delayedRefreshCoroutine != null)
        {
            StopCoroutine(delayedRefreshCoroutine);
            delayedRefreshCoroutine = null;
        }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        delayedRefreshCoroutine = null;
        RebuildVisualCache();
        Refresh();
    }

    private void HandleVisualStateChanged()
    {
        // 아이템이 본/앵커 아래로 이동했거나 씬 활성 상태가 바뀐 경우까지 복구합니다.
        RebuildVisualCache();
        Refresh();
    }

    [ContextMenu("Force Refresh Equipped Item Visual")]
    public void ForceRefreshVisual()
    {
        RebuildVisualCache();
        Refresh();
    }

    [ContextMenu("Resolve Durry Roots")]
    public void ResolveRoots()
    {
        if (!autoResolveRoots)
        {
            return;
        }

        /*
         * 권장 구조:
         *
         * DurryVisual
         * ├─ Durry_Main
         * ├─ Back_Nametag_01
         * ├─ Body_Sleepwear
         * ├─ Head_Sleepmask
         * ├─ Head_Hat_Hard
         * ├─ Head_Cone
         * └─ Head_Cap_Baseball
         */

        if (animatedRoot == null)
        {
            if (string.Equals(
                    transform.name,
                    "Durry_Main",
                    StringComparison.OrdinalIgnoreCase))
            {
                animatedRoot = transform;
            }
            else
            {
                animatedRoot = FindTransformExact(
                    transform,
                    "Durry_Main"
                );

                if (animatedRoot == null &&
                    transform.parent != null)
                {
                    animatedRoot = FindTransformExact(
                        transform.parent,
                        "Durry_Main"
                    );
                }
            }
        }

        if (visualRoot == null)
        {
            if (animatedRoot != null &&
                animatedRoot.parent != null)
            {
                visualRoot = animatedRoot.parent;
            }
            else if (transform.parent != null)
            {
                visualRoot = transform.parent;
            }
            else
            {
                visualRoot = transform;
            }
        }

        if (fallbackFollowTarget == null)
        {
            fallbackFollowTarget =
                animatedRoot != null
                    ? animatedRoot
                    : transform;
        }

        ResolveBackNametagAnchor();
    }

    [ContextMenu("Rebuild Durry Item Visual Cache")]
    public void RebuildVisualCache()
    {
        visualByItemId.Clear();
        warnedMissingItemIds.Clear();

        ResolveRoots();

        ShopInventoryManager manager =
            ShopInventoryManager.Instance;

        if (manager == null)
        {
            Debug.LogWarning(
                "[더리 아이템] ShopInventoryManager가 없습니다.",
                this
            );
            return;
        }

        Transform searchRoot =
            visualRoot != null
                ? visualRoot
                : transform;

        Transform[] allTransforms =
            searchRoot.GetComponentsInChildren<Transform>(true);

        foreach (
            ShopInventoryManager.ShopItemDefinition item
            in manager.ShopItems)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            string objectName =
                string.IsNullOrWhiteSpace(item.visualObjectName)
                    ? item.itemId.Trim()
                    : item.visualObjectName.Trim();

            Transform itemTransform =
                FindTransformExact(
                    allTransforms,
                    objectName
                );

            if (itemTransform == null)
            {
                if (logMissingVisualWarnings &&
                    warnedMissingItemIds.Add(item.itemId))
                {
                    Debug.LogWarning(
                        $"[더리 아이템] " +
                        $"'{searchRoot.name}' 아래에서 " +
                        $"'{objectName}'을 찾지 못했습니다.",
                        this
                    );
                }

                continue;
            }

            visualByItemId[item.itemId.Trim()] =
                itemTransform.gameObject;

            // 캐시를 만드는 순간에는 무조건 숨깁니다.
            // 실제 저장된 착용/미리보기 아이템만 Refresh()가 다시 켭니다.
            itemTransform.gameObject.SetActive(false);

            PrepareItem(
                item,
                itemTransform
            );
        }
    }

    [ContextMenu("Refresh Equipped Item")]
    public void Refresh()
    {
        ShopInventoryManager manager =
            ShopInventoryManager.Instance;

        if (manager == null)
        {
            if (hideAllWearablesImmediatelyOnAwake)
            {
                HideAllWearableVisualsImmediately();
            }
            return;
        }

        if (visualByItemId.Count == 0)
        {
            RebuildVisualCache();
        }

        // 모든 아이템을 먼저 끕니다.
        foreach (
            KeyValuePair<string, GameObject> pair
            in visualByItemId)
        {
            if (pair.Value != null)
            {
                pair.Value.SetActive(false);
            }
        }

        // 실제 착용 또는 상점 미리보기 중 하나만 표시합니다.
        string displayedItemId =
            manager.GetDisplayedItemId();

        if (string.IsNullOrWhiteSpace(displayedItemId))
        {
            return;
        }

        if (visualByItemId.TryGetValue(
                displayedItemId.Trim(),
                out GameObject displayedObject) &&
            displayedObject != null)
        {
            EnsureDisplayedObjectVisible(displayedObject);
        }
        else if (logMissingVisualWarnings)
        {
            Debug.LogWarning(
                $"[더리 아이템] 표시할 아이템 '{displayedItemId}'의 비주얼 캐시를 찾지 못했습니다.",
                this
            );
        }
    }

    [ContextMenu("Hide All Wearable Visuals Immediately")]
    public void HideAllWearableVisualsImmediately()
    {
        ResolveRoots();

        Transform searchRoot = visualRoot != null
            ? visualRoot
            : animatedRoot != null
                ? animatedRoot
                : transform;

        Transform[] allTransforms =
            searchRoot.GetComponentsInChildren<Transform>(true);

        HashSet<string> visualNames = new HashSet<string>(
            KnownWearableObjectNames,
            StringComparer.OrdinalIgnoreCase
        );

        ShopInventoryManager manager = ShopInventoryManager.Instance;
        if (manager != null)
        {
            foreach (ShopInventoryManager.ShopItemDefinition item in manager.ShopItems)
            {
                if (item == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.visualObjectName))
                {
                    visualNames.Add(item.visualObjectName.Trim());
                }
            }
        }

        foreach (Transform candidate in allTransforms)
        {
            if (candidate == null || !visualNames.Contains(candidate.name))
            {
                continue;
            }

            candidate.gameObject.SetActive(false);
        }
    }

    private void PrepareItem(
        ShopInventoryManager.ShopItemDefinition item,
        Transform itemRoot)
    {
        if (itemRoot == null)
        {
            return;
        }

        DisableExtraAnimators(itemRoot);
        ApplyNoShadowToItem(itemRoot);

        if (IsSleepwear(item, itemRoot))
        {
            PrepareSleepwear(itemRoot);
            return;
        }

        PrepareRigidItem(
            item,
            itemRoot
        );
    }

    private bool IsSleepwear(
        ShopInventoryManager.ShopItemDefinition item,
        Transform itemRoot)
    {
        string itemId =
            item != null && item.itemId != null
                ? item.itemId.ToLowerInvariant()
                : string.Empty;

        string objectName =
            itemRoot != null
                ? itemRoot.name.ToLowerInvariant()
                : string.Empty;

        return
            itemId.Contains("sleepwear") ||
            objectName.Contains("sleepwear") ||
            objectName == "body_sleepwear";
    }

    /// <summary>
    /// Body_Sleepwear가 SkinnedMeshRenderer로 제작되어 있으면,
    /// 메시와 전용 머터리얼은 그대로 둔 채 bones/rootBone만
    /// 현재 더리(Durry_Main)의 실제 본으로 다시 연결합니다.
    ///
    /// 주의:
    /// MeshFilter + MeshRenderer만 있는 일반 메시에는 본 가중치가 없으므로
    /// 코드만으로 몸 형태에 맞게 변형시킬 수 없습니다.
    /// </summary>
    private void PrepareSleepwear(
        Transform sleepwearRoot)
    {
        if (sleepwearRoot == null ||
            !keepSleepwearOriginalRenderer)
        {
            return;
        }

        Transform targetRoot =
            animatedRoot != null
                ? animatedRoot
                : fallbackFollowTarget;

        if (targetRoot == null)
        {
            Debug.LogError(
                "[슬립웨어 스키닝] Durry_Main(Animated Root)을 찾지 못했습니다.",
                sleepwearRoot
            );
            return;
        }

        SkinnedMeshRenderer[] sleepwearRenderers =
            sleepwearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (sleepwearRenderers.Length == 0)
        {
            MeshRenderer[] rigidRenderers =
                sleepwearRoot.GetComponentsInChildren<MeshRenderer>(true);

            if (rigidRenderers.Length > 0)
            {
                Debug.LogError(
                    "[슬립웨어 스키닝] Body_Sleepwear가 현재 Mesh Filter + Mesh Renderer인 일반 메시입니다. " +
                    "이 구조는 더리의 다리/몸 모양에 맞게 늘어나거나 줄어들 수 없습니다. " +
                    "Blender에서 더리 Armature에 Weight를 입힌 뒤 SkinnedMeshRenderer로 다시 가져와야 합니다.",
                    sleepwearRoot
                );
            }
            else
            {
                Debug.LogError(
                    "[슬립웨어 스키닝] Body_Sleepwear 아래에서 Renderer를 찾지 못했습니다.",
                    sleepwearRoot
                );
            }

            if (fallbackToRigidParentWhenSkinningUnavailable)
            {
                ParentSleepwearRoot(
                    sleepwearRoot,
                    targetRoot
                );
            }

            return;
        }

        if (!bindSleepwearToDurrySkeleton)
        {
            ParentSleepwearRoot(
                sleepwearRoot,
                targetRoot
            );
            return;
        }

        Dictionary<string, Transform> targetBoneLookup =
            BuildTargetBoneLookup(
                targetRoot,
                sleepwearRoot
            );

        int successfulRendererCount = 0;

        foreach (SkinnedMeshRenderer sleepwearRenderer in sleepwearRenderers)
        {
            if (sleepwearRenderer == null)
            {
                continue;
            }

            Mesh sleepwearMesh = sleepwearRenderer.sharedMesh;
            if (sleepwearMesh == null)
            {
                Debug.LogError(
                    $"[슬립웨어 스키닝] '{sleepwearRenderer.name}'에 Mesh가 없습니다.",
                    sleepwearRenderer
                );
                continue;
            }

            if (sleepwearMesh.bindposes == null ||
                sleepwearMesh.bindposes.Length == 0)
            {
                Debug.LogError(
                    $"[슬립웨어 스키닝] '{sleepwearRenderer.name}' 메시에는 Bind Pose가 없습니다. " +
                    "Blender에서 Armature Modifier와 Vertex Weight를 적용한 Skinned Mesh로 다시 내보내세요.",
                    sleepwearRenderer
                );
                continue;
            }

            Transform[] sourceBones = sleepwearRenderer.bones;
            if (sourceBones == null ||
                sourceBones.Length == 0)
            {
                Debug.LogError(
                    $"[슬립웨어 스키닝] '{sleepwearRenderer.name}'에 연결된 Bones가 없습니다. " +
                    "FBX Import 결과가 SkinnedMeshRenderer인지 확인하세요.",
                    sleepwearRenderer
                );
                continue;
            }

            Transform[] remappedBones =
                new Transform[sourceBones.Length];

            List<string> missingBoneNames =
                new List<string>();

            for (int boneIndex = 0;
                 boneIndex < sourceBones.Length;
                 boneIndex++)
            {
                Transform sourceBone =
                    sourceBones[boneIndex];

                Transform targetBone =
                    ResolveMatchingTargetBone(
                        sourceBone,
                        targetBoneLookup
                    );

                if (targetBone == null)
                {
                    missingBoneNames.Add(
                        sourceBone != null
                            ? sourceBone.name
                            : $"Element {boneIndex} (null)"
                    );
                    continue;
                }

                remappedBones[boneIndex] =
                    targetBone;
            }

            if (missingBoneNames.Count > 0)
            {
                Debug.LogError(
                    $"[슬립웨어 스키닝] '{sleepwearRenderer.name}'의 본 {missingBoneNames.Count}개를 " +
                    $"Durry_Main에서 찾지 못했습니다: {string.Join(", ", missingBoneNames)}\n" +
                    "더리 FBX와 슬립웨어 FBX의 본 이름이 완전히 같아야 합니다.",
                    sleepwearRenderer
                );
                continue;
            }

            Transform mappedRootBone =
                ResolveMatchingTargetBone(
                    sleepwearRenderer.rootBone,
                    targetBoneLookup
                );

            sleepwearRenderer.bones =
                remappedBones;

            sleepwearRenderer.rootBone =
                mappedRootBone != null
                    ? mappedRootBone
                    : targetRoot;

            sleepwearRenderer.updateWhenOffscreen =
                true;

            // sharedMesh와 sharedMaterials는 건드리지 않습니다.
            // 따라서 슬립웨어는 자신의 전용 머터리얼을 계속 사용합니다.
            successfulRendererCount++;

            if (logSleepwearBindingDetails)
            {
                Debug.Log(
                    $"[슬립웨어 스키닝 완료] '{sleepwearRenderer.name}'의 " +
                    $"{remappedBones.Length}개 본을 Durry_Main에 다시 연결했습니다. " +
                    $"Root Bone: {sleepwearRenderer.rootBone.name}",
                    sleepwearRenderer
                );
            }
        }

        ParentSleepwearRoot(
            sleepwearRoot,
            targetRoot
        );

        if (successfulRendererCount == 0)
        {
            Debug.LogError(
                "[슬립웨어 스키닝] SkinnedMeshRenderer는 찾았지만 더리 본 연결에 성공하지 못했습니다. " +
                "Console의 누락 본 이름과 FBX Rig 구조를 확인하세요.",
                sleepwearRoot
            );
        }
    }

    private void ParentSleepwearRoot(
        Transform sleepwearRoot,
        Transform targetRoot)
    {
        if (sleepwearRoot == null ||
            targetRoot == null ||
            sleepwearRoot == targetRoot ||
            targetRoot.IsChildOf(sleepwearRoot))
        {
            return;
        }

        if (sleepwearRoot.parent != targetRoot)
        {
            sleepwearRoot.SetParent(
                targetRoot,
                preserveWorldPoseWhenParenting
            );
        }
    }

    private static Dictionary<string, Transform> BuildTargetBoneLookup(
        Transform targetRoot,
        Transform excludedRoot)
    {
        Dictionary<string, Transform> lookup =
            new Dictionary<string, Transform>(
                StringComparer.OrdinalIgnoreCase
            );

        if (targetRoot == null)
        {
            return lookup;
        }

        Transform[] candidates =
            targetRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform candidate in candidates)
        {
            if (candidate == null)
            {
                continue;
            }

            if (excludedRoot != null &&
                (candidate == excludedRoot ||
                 candidate.IsChildOf(excludedRoot)))
            {
                continue;
            }

            AddBoneLookupKey(
                lookup,
                candidate.name,
                candidate
            );

            AddBoneLookupKey(
                lookup,
                NormalizeBoneName(candidate.name),
                candidate
            );
        }

        return lookup;
    }

    private static Transform ResolveMatchingTargetBone(
        Transform sourceBone,
        Dictionary<string, Transform> targetBoneLookup)
    {
        if (sourceBone == null ||
            targetBoneLookup == null)
        {
            return null;
        }

        if (targetBoneLookup.TryGetValue(
                sourceBone.name,
                out Transform exactMatch))
        {
            return exactMatch;
        }

        string normalizedName =
            NormalizeBoneName(sourceBone.name);

        return targetBoneLookup.TryGetValue(
            normalizedName,
            out Transform normalizedMatch)
                ? normalizedMatch
                : null;
    }

    private static void AddBoneLookupKey(
        Dictionary<string, Transform> lookup,
        string key,
        Transform value)
    {
        if (lookup == null ||
            value == null ||
            string.IsNullOrWhiteSpace(key) ||
            lookup.ContainsKey(key))
        {
            return;
        }

        lookup.Add(
            key,
            value
        );
    }

    private static string NormalizeBoneName(
        string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
        {
            return string.Empty;
        }

        string normalized =
            boneName.Trim();

        int namespaceSeparator =
            normalized.LastIndexOf(':');

        if (namespaceSeparator >= 0 &&
            namespaceSeparator < normalized.Length - 1)
        {
            normalized =
                normalized.Substring(namespaceSeparator + 1);
        }

        normalized =
            normalized
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();

        return normalized;
    }

    private void PrepareRigidItem(
        ShopInventoryManager.ShopItemDefinition item,
        Transform itemRoot)
    {
        bool isBackNametag = IsBackNametag(item, itemRoot);
        Transform target = null;

        // 네임택은 일반 spine/chest 자동 점수 탐색에서 모델 안쪽으로 파묻히는 경우가 있어
        // 전용 앵커를 최우선으로 사용하고, 앵커가 없으면 Durry_Main 루트를 사용합니다.
        if (isBackNametag)
        {
            ResolveBackNametagAnchor();
            target = backNametagAnchor != null
                ? backNametagAnchor
                : fallbackFollowTarget != null
                    ? fallbackFollowTarget
                    : animatedRoot;
        }
        else if (attachRigidItemsToBone)
        {
            target = ResolveRigidAttachmentTarget(item);
        }

        if (target == null)
        {
            target =
                fallbackFollowTarget != null
                    ? fallbackFollowTarget
                    : animatedRoot;
        }

        if (target == null ||
            itemRoot == target ||
            target.IsChildOf(itemRoot))
        {
            return;
        }

        bool useSnappedBackAnchor =
            isBackNametag &&
            backNametagAnchor != null &&
            target == backNametagAnchor &&
            snapBackNametagToAnchor;

        if (itemRoot.parent != target)
        {
            itemRoot.SetParent(
                target,
                useSnappedBackAnchor ? false : preserveWorldPoseWhenParenting
            );
        }

        if (useSnappedBackAnchor)
        {
            itemRoot.localPosition = backNametagLocalPosition;
            itemRoot.localRotation = Quaternion.Euler(backNametagLocalEuler);
        }

        if (logAttachmentResults)
        {
            Debug.Log(
                $"[아이템 부착] " +
                $"{itemRoot.name} → {target.name}" +
                (useSnappedBackAnchor ? " (네임택 앵커 스냅)" : string.Empty),
                itemRoot
            );
        }
    }

    private bool IsBackNametag(
        ShopInventoryManager.ShopItemDefinition item,
        Transform itemRoot)
    {
        string itemId = item != null && item.itemId != null
            ? item.itemId.Trim().ToLowerInvariant()
            : string.Empty;

        string objectName = itemRoot != null
            ? itemRoot.name.Trim().ToLowerInvariant()
            : string.Empty;

        return itemId == "back_nametag_01" ||
               itemId.Contains("nametag") ||
               objectName == "back_nametag_01" ||
               objectName.Contains("nametag");
    }

    private void ResolveBackNametagAnchor()
    {
        if (backNametagAnchor != null || !autoResolveBackNametagAnchor)
        {
            return;
        }

        Transform searchRoot = animatedRoot != null
            ? animatedRoot
            : visualRoot != null
                ? visualRoot
                : transform;

        string[] candidateNames =
        {
            "BackItemAnchor",
            "Back_Nametag_Anchor",
            "BackNametagAnchor",
            "NametagAnchor"
        };

        foreach (string candidateName in candidateNames)
        {
            Transform candidate = FindTransformExact(searchRoot, candidateName);
            if (candidate != null)
            {
                backNametagAnchor = candidate;
                return;
            }
        }
    }

    private void EnsureDisplayedObjectVisible(GameObject displayedObject)
    {
        if (displayedObject == null)
        {
            return;
        }

        if (forceDisplayedParentChainActive)
        {
            Transform current = displayedObject.transform.parent;
            Transform stopRoot = visualRoot != null ? visualRoot.parent : null;

            while (current != null && current != stopRoot)
            {
                // 더리 본/전용 앵커가 비활성이라 아이템까지 숨는 경우를 복구합니다.
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }

                if (current == visualRoot || current == animatedRoot)
                {
                    break;
                }

                current = current.parent;
            }
        }

        displayedObject.SetActive(true);

        if (forceDisplayedHierarchyActive)
        {
            Transform[] hierarchy =
                displayedObject.GetComponentsInChildren<Transform>(true);

            foreach (Transform child in hierarchy)
            {
                if (child != null && !child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(true);
                }
            }
        }

        EnsureDisplayedRenderersVisible(displayedObject);
    }

    private void EnsureDisplayedRenderersVisible(GameObject displayedObject)
    {
        if (!forceDisplayedRenderersEnabled || displayedObject == null)
        {
            return;
        }

        Renderer[] renderers = displayedObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = true;
            renderer.forceRenderingOff = false;

            if (disableItemShadowCasting)
            {
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (disableItemShadowReceiving)
            {
                renderer.receiveShadows = false;
            }

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
            }
        }

        LODGroup[] lodGroups = displayedObject.GetComponentsInChildren<LODGroup>(true);
        foreach (LODGroup lodGroup in lodGroups)
        {
            if (lodGroup == null)
            {
                continue;
            }

            lodGroup.enabled = true;
            lodGroup.ForceLOD(0);
        }
    }

    private void ApplyNoShadowToItem(Transform itemRoot)
    {
        if (itemRoot == null)
        {
            return;
        }

        Renderer[] itemRenderers =
            itemRoot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in itemRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (disableItemShadowCasting)
            {
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (disableItemShadowReceiving)
            {
                renderer.receiveShadows = false;
            }
        }
    }

    private void DisableExtraAnimators(
        Transform itemRoot)
    {
        if (!disableItemAnimators ||
            itemRoot == null)
        {
            return;
        }

        Animator[] animators =
            itemRoot.GetComponentsInChildren<Animator>(true);

        foreach (Animator itemAnimator in animators)
        {
            if (itemAnimator == null)
            {
                continue;
            }

            if (animatedRoot != null &&
                itemAnimator.transform == animatedRoot)
            {
                continue;
            }

            itemAnimator.enabled = false;
        }
    }

    private Transform ResolveRigidAttachmentTarget(
        ShopInventoryManager.ShopItemDefinition item)
    {
        if (animatedRoot == null)
        {
            return fallbackFollowTarget;
        }

        // 카탈로그에서 정확한 본 이름을 지정한 경우
        if (item != null &&
            !string.IsNullOrWhiteSpace(
                item.attachmentTargetName))
        {
            Transform assignedTarget =
                FindTransformExact(
                    animatedRoot,
                    item.attachmentTargetName.Trim()
                );

            if (assignedTarget != null)
            {
                return assignedTarget;
            }
        }

        string category =
            item != null &&
            !string.IsNullOrWhiteSpace(item.category)
                ? item.category.Trim().ToLowerInvariant()
                : string.Empty;

        Transform[] candidates =
            animatedRoot.GetComponentsInChildren<Transform>(true);

        Transform bestTarget = null;
        int bestScore = int.MinValue;

        foreach (Transform candidate in candidates)
        {
            if (candidate == null)
            {
                continue;
            }

            int score =
                ScoreAttachmentTarget(
                    candidate.name,
                    category
                );

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        return bestScore > 0
            ? bestTarget
            : fallbackFollowTarget;
    }

    private static int ScoreAttachmentTarget(
        string targetName,
        string category)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return int.MinValue;
        }

        string lower =
            targetName.ToLowerInvariant();

        int score = 0;

        if (category == "head")
        {
            if (lower == "head" ||
                lower.EndsWith(":head"))
            {
                score += 300;
            }

            if (lower.Contains("head"))
            {
                score += 180;
            }

            if (lower.Contains("neck"))
            {
                score += 80;
            }
        }
        else if (
            category == "body" ||
            category == "back")
        {
            if (lower.Contains("chest"))
            {
                score += 220;
            }

            if (lower.Contains("spine"))
            {
                score += 200;
            }

            if (lower.Contains("body"))
            {
                score += 150;
            }

            if (lower.Contains("torso"))
            {
                score += 140;
            }

            if (lower.Contains("root"))
            {
                score += 50;
            }
        }
        else
        {
            if (lower.Contains("root"))
            {
                score += 50;
            }

            if (lower.Contains("joint"))
            {
                score += 30;
            }
        }

        if (lower.Contains("joint") ||
            lower.Contains("bone"))
        {
            score += 30;
        }

        if (lower.Contains("geo") ||
            lower.Contains("mesh"))
        {
            score -= 100;
        }

        if (lower.Contains("eye") ||
            lower.Contains("mouth") ||
            lower.Contains("brow"))
        {
            score -= 200;
        }

        return score;
    }

    private static Transform FindTransformExact(
        Transform root,
        string exactName)
    {
        if (root == null ||
            string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] transforms =
            root.GetComponentsInChildren<Transform>(true);

        return FindTransformExact(
            transforms,
            exactName
        );
    }

    private static Transform FindTransformExact(
        Transform[] transforms,
        string exactName)
    {
        if (transforms == null ||
            string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        foreach (Transform candidate in transforms)
        {
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(
                    candidate.name,
                    exactName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}