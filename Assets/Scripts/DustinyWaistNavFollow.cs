using UnityEngine;

/// <summary>
/// Keeps the waist navigation coordinate system in front of the user's body/head yaw,
/// while allowing a Meta Interaction SDK Grabbable child to be moved locally.
///
/// Recommended hierarchy:
/// MainMRScene
/// └─ WaistFollowRoot                 <- attach this script here
///    └─ WaistNavGrabbableRoot        <- ISDK Grabbable / HandGrabInteractable / DistanceGrab target
///       └─ WaistNavCanvas            <- World Space Canvas + PointableCanvas + Ray/Poke Interactable
///          └─ NavigationBar
///
/// Important:
/// - This script moves only WaistFollowRoot.
/// - It applies the initial local pose to WaistNavGrabbableRoot once.
/// - It does not force the child pose every frame, so ISDK Grab can move it.
/// </summary>
public class DustinyWaistNavFollow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("OVRCameraRig / TrackingSpace / CenterEyeAnchor")]
    public Transform centerEyeAnchor;

    [Tooltip("Child object that contains the grabbable navigation canvas. Example: WaistNavGrabbableRoot")]
    public Transform waistNavGrabbableRoot;

    [Header("Follow User")]
    [Tooltip("Follow the user's head position every frame.")]
    public bool followHeadPosition = true;

    [Tooltip("Follow only yaw direction. Pitch and roll are ignored so the waist belt stays stable.")]
    public bool followHeadYawOnly = true;

    [Tooltip("0 = instant. 18~35 = smooth but responsive.")]
    public float followSmoothing = 0f;

    [Header("Initial Waist Nav Offset")]
    [Tooltip("Applied once to WaistNavGrabbableRoot on Start/Reset. X: left-right, Y: down-up, Z: forward.")]
    public Vector3 defaultLocalPosition = new Vector3(0f, -1.45f, 0.65f);

    [Tooltip("Tilt the canvas upward toward the user. If the UI is flipped, try -72 instead.")]
    public Vector3 defaultLocalEuler = new Vector3(72f, 0f, 0f);

    [Tooltip("Usually keep this 1. Put the tiny UI scale on WaistNavCanvas, not here.")]
    public Vector3 defaultLocalScale = Vector3.one;

    [Tooltip("If true, resets WaistNavGrabbableRoot to the default local pose once when the scene starts.")]
    public bool applyDefaultPoseOnStart = true;

    [Header("Safety Clamp While Grabbed")]
    [Tooltip("Keeps the grabbable child from drifting too far after being moved.")]
    public bool clampChildLocalPosition = true;

    public Vector3 minLocalPosition = new Vector3(-0.65f, -1.80f, 0.35f);
    public Vector3 maxLocalPosition = new Vector3(0.65f, -0.85f, 1.05f);

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
        ClampChildIfNeeded();
    }

    [ContextMenu("Reset Waist Nav Pose")]
    public void ResetWaistNavPose()
    {
        if (waistNavGrabbableRoot == null)
        {
            Debug.LogWarning("DustinyWaistNavFollow: Waist Nav Grabbable Root is not assigned.");
            return;
        }

        if (waistNavGrabbableRoot.parent != transform)
        {
            waistNavGrabbableRoot.SetParent(transform, true);
        }

        waistNavGrabbableRoot.localPosition = defaultLocalPosition;
        waistNavGrabbableRoot.localRotation = Quaternion.Euler(defaultLocalEuler);
        waistNavGrabbableRoot.localScale = defaultLocalScale;
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

    private void ClampChildIfNeeded()
    {
        if (!clampChildLocalPosition || waistNavGrabbableRoot == null)
        {
            return;
        }

        Vector3 p = waistNavGrabbableRoot.localPosition;
        p.x = Mathf.Clamp(p.x, minLocalPosition.x, maxLocalPosition.x);
        p.y = Mathf.Clamp(p.y, minLocalPosition.y, maxLocalPosition.y);
        p.z = Mathf.Clamp(p.z, minLocalPosition.z, maxLocalPosition.z);
        waistNavGrabbableRoot.localPosition = p;
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

        if (waistNavGrabbableRoot == null)
        {
            Transform foundChild = transform.Find("WaistNavGrabbableRoot");
            if (foundChild != null)
            {
                waistNavGrabbableRoot = foundChild;
            }
        }
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

        if (waistNavGrabbableRoot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(waistNavGrabbableRoot.position, 0.04f);
        }
    }
}
