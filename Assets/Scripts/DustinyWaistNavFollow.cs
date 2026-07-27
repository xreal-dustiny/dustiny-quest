using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Touch/ray waist navigation follow controller for Dustiny.
///
/// Recommended hierarchy:
/// MainMRScene
/// └─ WaistFollowRoot                 <- attach this script here
///    └─ WaistNavCanvas               <- World Space Canvas + PointableCanvas + Ray/Poke Interactable
///       └─ NavigationBar
///
/// The follow root tracks the user's head position/yaw.
/// WaistNavCanvas keeps an explicit local pose and script-controlled scale.
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

    [Header("Debug")]
    public bool drawDebugRay = false;

    private bool isCurrentlyVisible = true;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
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
    }

    private void LateUpdate()
    {
        ApplyFollow(false);

        if (enforceNavigationScaleEveryFrame)
        {
            ApplyConfiguredScale();
        }

        UpdateGazeVisibility();
    }

    private void UpdateGazeVisibility()
    {
        if (!enableLookDownToOpen || centerEyeAnchor == null || waistNavRoot == null)
        {
            return;
        }

        // centerEyeAnchor.forward.y 는 정면일 때 0, 바닥을 볼 때 음수(-1)가 됩니다.
        float lookDownAmount = -centerEyeAnchor.forward.y;
        bool shouldBeVisible = lookDownAmount > lookDownThreshold;

        if (shouldBeVisible != isCurrentlyVisible)
        {
            isCurrentlyVisible = shouldBeVisible;
            waistNavRoot.gameObject.SetActive(isCurrentlyVisible);
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
                return;
            }

            Transform foundRoot = FindChildByName(transform, "WaistNavRoot");
            if (foundRoot != null)
            {
                waistNavRoot = foundRoot;
                return;
            }

            Transform foundOldRoot = FindChildByName(transform, "WaistNavGrabbableRoot");
            if (foundOldRoot != null)
            {
                waistNavRoot = foundOldRoot;
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