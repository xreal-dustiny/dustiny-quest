using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Touch/ray waist navigation follow controller for Dustiny.
///
/// VERSION: 2026-08-16 NAV_VISIBILITY_SYNC_FIX
///
/// Recommended hierarchy:
/// MainMRScene
/// └─ WaistFollowRoot                 <- attach this script here
///    └─ WaistNavCanvas               <- World Space Canvas + PointableCanvas + Ray/Poke Interactable
///       └─ NavigationBar
///
/// Normal mode:
/// - Look down -> show navigation.
/// - Look up   -> hide navigation.
///
/// Tutorial mode:
/// - DustinyDemoFlow can temporarily force the navigation visible/hidden.
/// - When the tutorial releases the override, visibility is refreshed immediately
///   from the current head pitch so the bar never remains stuck on screen.
/// </summary>
public class DustinyWaistNavFollow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("OVRCameraRig / TrackingSpace / CenterEyeAnchor")]
    public Transform centerEyeAnchor;

    [FormerlySerializedAs("waistNavGrabbableRoot")]
    [Tooltip("Navigation canvas transform. In the current hierarchy this is WaistNavCanvas.")]
    public Transform waistNavRoot;

    [Header("Follow User")]
    [Tooltip("Follow the user's head position every frame.")]
    public bool followHeadPosition = true;

    [Tooltip("Follow only yaw direction. Pitch and roll are ignored so the waist belt stays stable.")]
    public bool followHeadYawOnly = true;

    [Tooltip("0 = instant. 18~35 = smooth but responsive.")]
    public float followSmoothing = 20f;

    [Header("Initial Waist Nav Offset")]
    [Tooltip("Applied to WaistNavRoot on Start/Reset. X: left-right, Y: down-up, Z: forward.")]
    public Vector3 defaultLocalPosition = new Vector3(0f, -0.8f, 0.5f);

    [Tooltip("Tilt the canvas upward toward the user. If the UI is flipped, try -55 or 55.")]
    public Vector3 defaultLocalEuler = new Vector3(55f, 0f, 0f);

    [Tooltip("Reset WaistNavRoot to the configured local pose once when the scene starts.")]
    public bool applyDefaultPoseOnStart = true;

    [Header("Navigation Size - Use This")]
    [Tooltip("When enabled, this script directly controls WaistNavCanvas localScale.")]
    public bool controlNavigationScale = true;

    [Tooltip("For a 900px-wide World Space Canvas, 0.00055~0.00075 is a useful range. Current recommended value: 0.00065.")]
    [Min(0.00001f)] public float navigationUniformScale = 0.00065f;

    [Tooltip("Keeps the configured scale even if another script or prefab animation changes it.")]
    public bool enforceNavigationScaleEveryFrame = true;

    [Header("Legacy Scale Fields")]
    [Tooltip("Legacy option. Leave this off when Control Navigation Scale is on.")]
    public bool applyDefaultScaleOnStart = false;

    [Tooltip("Legacy non-uniform scale, used only when Control Navigation Scale is off.")]
    public Vector3 defaultLocalScale = Vector3.one;

    [Header("Gaze Pitch Activation (Look Down to Open)")]
    [Tooltip("고개를 아래로 숙여 네비게이션 바를 자동으로 켜고 끄는 기능을 활성화합니다.")]
    public bool enableLookDownToOpen = true;

    [Tooltip("고개를 아래로 숙인 정도 (0.2~0.5 추천). 값이 크수록 더 깊게 숙여야 켜집니다.")]
    [Range(0.1f, 0.8f)]
    public float lookDownThreshold = 0.35f;

    [Header("Draw In Front")]
    [Tooltip("월드 스페이스 UI가 욕조 같은 3D 메시에 가려지지 않도록 ZTest Always를 적용합니다.")]
    public bool alwaysDrawInFront = false;

    [Tooltip("눈-네비게이션 사이에 욕조 등이 있으면 캔버스를 사용자 쪽으로 당깁니다.")]
    public bool pullInFrontOfOccluders = true;

    [Tooltip("가림 메시와 캔버스 사이 여유 거리입니다.")]
    public float occlusionClearance = 0.08f;

    [Tooltip("눈에서 네비게이션까지 허용하는 최소 거리입니다.")]
    public float minDistanceFromEye = 0.16f;

    [Header("Debug")]
    public bool drawDebugRay = false;

    // 실제 GameObject 상태를 보조적으로 기억할 뿐, 이 값만 믿고 표시를 건너뛰지 않습니다.
    // 외부 스크립트가 SetActive를 호출해도 다음 Refresh에서 실제 상태와 다시 동기화됩니다.
    private bool isCurrentlyVisible;
    private bool uiFrontMaterialApplied;
    private readonly RaycastHit[] occlusionHits = new RaycastHit[16];

    // 튜토리얼 동안만 사용하는 강제 표시 상태입니다.
    private bool visibilityOverrideActive;
    private bool visibilityOverrideValue;

    public bool IsVisibilityOverrideActive => visibilityOverrideActive;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        SyncCachedVisibilityFromObject();
    }

    private void OnEnable()
    {
        SyncCachedVisibilityFromObject();
    }

    private void OnValidate()
    {
        navigationUniformScale = Mathf.Max(0.00001f, navigationUniformScale);
        followSmoothing = Mathf.Max(0f, followSmoothing);

        if (!Application.isPlaying)
        {
            AutoFindReferences();
            ApplyConfiguredScale();
        }
    }

    private void Start()
    {
        if (applyDefaultPoseOnStart)
        {
            ResetWaistNavPose();
        }
        else
        {
            ApplyConfiguredScale();
        }

        ApplyFollow(true);
        ApplyUiAlwaysInFront();
        PullNavInFrontOfOccluders();
        RefreshVisibilityImmediately();
    }

    private void LateUpdate()
    {
        ApplyFollow(false);

        if (enforceNavigationScaleEveryFrame)
        {
            ApplyConfiguredScale();
        }

        PullNavInFrontOfOccluders();
        UpdateGazeVisibility();
    }

    /// <summary>
    /// 튜토리얼에서 gaze 판정을 잠시 무시하고 네비게이션 표시 상태를 강제합니다.
    /// true = 강제 표시, false = 강제 숨김.
    /// </summary>
    public void SetVisibilityOverride(bool visible)
    {
        visibilityOverrideActive = true;
        visibilityOverrideValue = visible;
        ApplyNavigationVisibility(visible);
    }

    /// <summary>
    /// 튜토리얼 강제 표시를 해제하고, 같은 프레임에 현재 고개 각도로 다시 판정합니다.
    /// </summary>
    public void ClearVisibilityOverrideAndRefresh()
    {
        visibilityOverrideActive = false;
        RefreshVisibilityImmediately();
    }

    /// <summary>
    /// 외부에서 NavigationBar/Canvas의 SetActive 상태를 바꾼 뒤에도
    /// 현재 gaze 규칙과 실제 GameObject 상태를 즉시 맞출 때 사용할 수 있습니다.
    /// </summary>
    public void RefreshVisibilityImmediately()
    {
        if (waistNavRoot == null)
        {
            AutoFindReferences();
        }

        if (waistNavRoot == null)
        {
            return;
        }

        if (visibilityOverrideActive)
        {
            ApplyNavigationVisibility(visibilityOverrideValue);
            return;
        }

        if (!enableLookDownToOpen || centerEyeAnchor == null)
        {
            SyncCachedVisibilityFromObject();
            return;
        }

        ApplyNavigationVisibility(IsLookingDownEnough());
    }

    private void UpdateGazeVisibility()
    {
        if (waistNavRoot == null)
        {
            return;
        }

        if (visibilityOverrideActive)
        {
            ApplyNavigationVisibility(visibilityOverrideValue);
            return;
        }

        if (!enableLookDownToOpen || centerEyeAnchor == null)
        {
            SyncCachedVisibilityFromObject();
            return;
        }

        ApplyNavigationVisibility(IsLookingDownEnough());
    }

    private bool IsLookingDownEnough()
    {
        // centerEyeAnchor.forward.y 는 정면일 때 0, 바닥을 볼 때 음수(-1)가 됩니다.
        float lookDownAmount = -centerEyeAnchor.forward.y;
        return lookDownAmount > lookDownThreshold;
    }

    private void ApplyNavigationVisibility(bool visible)
    {
        if (waistNavRoot == null)
        {
            return;
        }

        isCurrentlyVisible = visible;

        // 중요: 캐시값이 아니라 실제 activeSelf와 비교합니다.
        // DustinyDemoFlow 같은 외부 코드가 SetActive를 바꿔도 즉시 복구됩니다.
        if (waistNavRoot.gameObject.activeSelf != visible)
        {
            waistNavRoot.gameObject.SetActive(visible);
        }
    }

    private void SyncCachedVisibilityFromObject()
    {
        if (waistNavRoot != null)
        {
            isCurrentlyVisible = waistNavRoot.gameObject.activeSelf;
        }
    }

    [ContextMenu("Reset Waist Nav Pose")]
    public void ResetWaistNavPose()
    {
        if (waistNavRoot == null)
        {
            AutoFindReferences();
        }

        if (waistNavRoot == null)
        {
            Debug.LogWarning("DustinyWaistNavFollow: WaistNavCanvas is not assigned.");
            return;
        }

        if (waistNavRoot.parent != transform)
        {
            waistNavRoot.SetParent(transform, true);
        }

        waistNavRoot.localPosition = defaultLocalPosition;
        waistNavRoot.localRotation = Quaternion.Euler(defaultLocalEuler);
        ApplyConfiguredScale();
    }

    [ContextMenu("Apply Navigation Scale")]
    public void ApplyConfiguredScale()
    {
        if (waistNavRoot == null)
        {
            AutoFindReferences();
        }

        if (waistNavRoot == null)
        {
            return;
        }

        if (controlNavigationScale)
        {
            float safeScale = Mathf.Max(0.00001f, navigationUniformScale);
            waistNavRoot.localScale = Vector3.one * safeScale;
        }
        else if (applyDefaultScaleOnStart)
        {
            waistNavRoot.localScale = defaultLocalScale;
        }
    }

    private void ApplyFollow(bool force)
    {
        if (centerEyeAnchor == null)
        {
            AutoFindReferences();
        }

        if (centerEyeAnchor == null)
        {
            return;
        }

        Vector3 targetPosition = followHeadPosition ? centerEyeAnchor.position : transform.position;
        Quaternion targetRotation = GetTargetRotation();

        if (force || followSmoothing <= 0f)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
            return;
        }

        float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
    }

    private Quaternion GetTargetRotation()
    {
        if (centerEyeAnchor == null)
        {
            return transform.rotation;
        }

        if (!followHeadYawOnly)
        {
            return centerEyeAnchor.rotation;
        }

        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

private void ApplyUiAlwaysInFront()
    {
        if (uiFrontMaterialApplied || waistNavRoot == null)
        {
            return;
        }

        Graphic[] graphics = waistNavRoot.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
            {
                continue;
            }

            graphic.raycastTarget = true;
        }

        Canvas canvas = waistNavRoot.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
        }

        uiFrontMaterialApplied = true;
    }


    private void PullNavInFrontOfOccluders()
    {
        if (!pullInFrontOfOccluders || centerEyeAnchor == null || waistNavRoot == null)
        {
            return;
        }

        Vector3 defaultWorld = transform.TransformPoint(defaultLocalPosition);
        Vector3 eye = centerEyeAnchor.position;
        Vector3 toNav = defaultWorld - eye;
        float distance = toNav.magnitude;
        if (distance < 0.001f)
        {
            return;
        }

        Vector3 direction = toNav / distance;
        int hitCount = Physics.RaycastNonAlloc(
            eye,
            direction,
            occlusionHits,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closest = distance;
        bool occluded = false;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = occlusionHits[i].transform;
            if (hitTransform == null || hitTransform == transform)
            {
                continue;
            }

            if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(waistNavRoot))
            {
                continue;
            }

            if (occlusionHits[i].distance < closest)
            {
                closest = occlusionHits[i].distance;
                occluded = true;
            }
        }

        Vector3 targetLocal = defaultLocalPosition;
        if (occluded)
        {
            float pulled = Mathf.Clamp(closest - occlusionClearance, minDistanceFromEye, distance);
            Vector3 pulledWorld = eye + direction * pulled;
            Vector3 pulledLocal = transform.InverseTransformPoint(pulledWorld);
            targetLocal = new Vector3(defaultLocalPosition.x, defaultLocalPosition.y, pulledLocal.z);
        }

        float t = followSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        waistNavRoot.localPosition = Vector3.Lerp(waistNavRoot.localPosition, targetLocal, t);
        waistNavRoot.localRotation = Quaternion.Euler(defaultLocalEuler);
    }

    private void AutoFindReferences()
    {
        if (centerEyeAnchor == null)
        {
            GameObject foundCenterEye = GameObject.Find("CenterEyeAnchor");
            if (foundCenterEye != null)
            {
                centerEyeAnchor = foundCenterEye.transform;
            }
            else if (Camera.main != null)
            {
                centerEyeAnchor = Camera.main.transform;
            }
        }

        if (waistNavRoot == null)
        {
            Transform foundCanvas = FindChildByName(transform, "WaistNavCanvas");
            if (foundCanvas != null)
            {
                waistNavRoot = foundCanvas;
                SyncCachedVisibilityFromObject();
                return;
            }

            Transform foundRoot = FindChildByName(transform, "WaistNavRoot");
            if (foundRoot != null)
            {
                waistNavRoot = foundRoot;
                SyncCachedVisibilityFromObject();
                return;
            }

            Transform foundOldRoot = FindChildByName(transform, "WaistNavGrabbableRoot");
            if (foundOldRoot != null)
            {
                waistNavRoot = foundOldRoot;
                SyncCachedVisibilityFromObject();
            }
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugRay || centerEyeAnchor == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();
        Gizmos.DrawRay(centerEyeAnchor.position, forward * 0.8f);

        if (waistNavRoot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(waistNavRoot.position, 0.04f);
        }
    }
}
