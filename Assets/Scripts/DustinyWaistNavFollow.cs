using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Touch-only waist navigation follow controller for Dustiny.
///
/// Recommended hierarchy after removing grab:
/// MainMRScene
/// └─ WaistFollowRoot                 <- attach this script here
///    └─ WaistNavCanvas               <- World Space Canvas + PointableCanvas + Ray/Poke Interactable
///       └─ NavigationBar
///          ├─ DurryNoteButton
///          ├─ ShopButton
///          ├─ MyPageButton
///          └─ MenuButton
///
/// This script only keeps WaistFollowRoot in front of the user's body/head yaw.
/// It can optionally place WaistNavCanvas once at Start/Reset.
/// It does not use Rigidbody, Grabbable, HandGrabInteractable, BoxCollider, or GrabHandle.
/// </summary>
public class DustinyWaistNavFollow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("OVRCameraRig / TrackingSpace / CenterEyeAnchor")]
    public Transform centerEyeAnchor;

    [FormerlySerializedAs("waistNavGrabbableRoot")]
    [Tooltip("Child transform that contains the navigation canvas. Usually WaistNavCanvas after removing grab.")]
    public Transform waistNavRoot;

    [Header("Follow User")]
    [Tooltip("Follow the user's head position every frame.")]
    public bool followHeadPosition = true;

    [Tooltip("Follow only yaw direction. Pitch and roll are ignored so the waist belt stays stable.")]
    public bool followHeadYawOnly = true;

    [Tooltip("0 = instant. 18~35 = smooth but responsive.")]
    public float followSmoothing = 20f;

    [Header("Initial Waist Nav Offset")]
    [Tooltip("Applied once to WaistNavRoot on Start/Reset. X: left-right, Y: down-up, Z: forward.")]
    public Vector3 defaultLocalPosition = new Vector3(0f, -1f, 1f);

    [Tooltip("Tilt the canvas upward toward the user. If the UI is flipped, try -60 or 60.")]
    public Vector3 defaultLocalEuler = new Vector3(60f, 0f, 0f);

    [Tooltip("Usually do not force this when WaistNavRoot is the actual Canvas. Keep the Canvas scale you set in the scene.")]
    public bool applyDefaultScaleOnStart = false;

    [Tooltip("Only used if Apply Default Scale On Start is true.")]
    public Vector3 defaultLocalScale = Vector3.one;

    [Tooltip("If true, resets WaistNavRoot to the default local pose once when the scene starts.")]
    public bool applyDefaultPoseOnStart = true;

    [Header("Debug")]
    public bool drawDebugRay = false;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
    }

    private void Start()
    {
        if (applyDefaultPoseOnStart)
        {
            ResetWaistNavPose();
        }

        ApplyFollow(true);
    }

    private void LateUpdate()
    {
        ApplyFollow(false);
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
            Debug.LogWarning("DustinyWaistNavFollow: Waist Nav Root is not assigned. Assign WaistNavCanvas.");
            return;
        }

        if (waistNavRoot.parent != transform)
        {
            waistNavRoot.SetParent(transform, true);
        }

        waistNavRoot.localPosition = defaultLocalPosition;
        waistNavRoot.localRotation = Quaternion.Euler(defaultLocalEuler);

        if (applyDefaultScaleOnStart)
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

    private Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;

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
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Gizmos.DrawRay(centerEyeAnchor.position, forward * 0.8f);

        if (waistNavRoot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(waistNavRoot.position, 0.04f);
        }
    }
}
