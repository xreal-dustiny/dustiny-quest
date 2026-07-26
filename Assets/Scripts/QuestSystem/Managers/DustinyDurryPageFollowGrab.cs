using System;
using UnityEngine;

/// <summary>
/// BigNoteRoot를 카메라 고정 UI에서 분리된 것처럼 동작시키고,
/// 더리 오른쪽의 월드 위치를 유지하면서 더리와 함께 이동시킵니다.
///
/// 오른손 검지 핀치로 Grab Handle을 직접 집으면 페이지를 옮길 수 있으며,
/// 놓은 위치는 더리 기준 오프셋으로 저장되어 이후에도 더리와 함께 움직입니다.
/// </summary>
[DisallowMultipleComponent]
public class DustinyDurryPageFollowGrab : MonoBehaviour
{
    [Header("필수 참조 - 비워두면 자동 탐색")]
    [SerializeField] private RectTransform pageRoot;
    [SerializeField] private Transform durryRoot;
    [SerializeField] private Transform viewer;
    [SerializeField] private OVRHand rightHand;
    [SerializeField] private DustinyDemoFlow demoFlow;

    [Header("더리 오른쪽 기본 위치")]
    [Tooltip("처음 열릴 때 사용자의 화면 기준으로 더리 오른쪽에 떨어지는 거리입니다.")]
    [SerializeField, Min(0f)] private float sideOffset = 0.75f;

    [Tooltip("더리 기준 위쪽 거리입니다.")]
    [SerializeField] private float heightOffset = 0.28f;

    [Tooltip("양수면 더리보다 사용자 쪽, 음수면 더리 뒤쪽입니다.")]
    [SerializeField] private float depthOffset = 0.02f;

    [Tooltip("처음 열릴 때 페이지가 사용자를 바라보도록 회전합니다.")]
    [SerializeField] private bool faceViewerOnFirstPlacement = true;

    [Header("검지 핀치 이동")]
    [Tooltip("집을 영역입니다. NoteHeader 또는 별도 GrabHandle을 연결하는 것을 권장합니다.")]
    [SerializeField] private RectTransform grabHandleRect;

    [Tooltip("GrabHandle이 비어 있으면 이름에 GrabHandle/NoteHeader/Header가 들어간 RectTransform을 찾습니다.")]
    [SerializeField] private bool autoFindGrabHandle = true;

    [Tooltip("GrabHandle 영역 바깥으로 허용할 UI 로컬 여백입니다.")]
    [SerializeField, Min(0f)] private float grabPadding = 35f;

    [Tooltip("손과 GrabHandle 평면 사이의 최대 월드 거리입니다.")]
    [SerializeField, Min(0.01f)] private float maximumGrabPlaneDistance = 0.22f;

    [Tooltip("RectTransform Handle을 찾지 못했을 때 사용할 중심점 거리입니다.")]
    [SerializeField, Min(0.01f)] private float fallbackGrabDistance = 0.40f;

    [Tooltip("노트 페이지가 열려 있을 때만 직접 잡기를 허용합니다.")]
    [SerializeField] private bool onlyGrabWhileNotePageOpen = true;

    [Header("따라가기")]
    [SerializeField] private bool followDurryRotation = true;
    [SerializeField] private bool logGrabEvents = false;

    private bool poseInitialized;
    private bool isDragging;
    private bool wasIndexPinching;

    private Vector3 durryLocalOffset;
    private Quaternion durryLocalRotation = Quaternion.identity;
    private Vector3 preservedWorldOffset;
    private Quaternion preservedWorldRotation = Quaternion.identity;

    private Vector3 dragStartHandPosition;
    private Vector3 dragStartPagePosition;
    private Quaternion dragStartPageRotation;

    public bool IsDragging => isDragging;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializePoseIfNeeded();
        ApplyFollowPose();
        wasIndexPinching = false;
        isDragging = false;
    }

    private void OnDisable()
    {
        isDragging = false;
        wasIndexPinching = false;
    }

    public void Configure(
        RectTransform configuredPageRoot,
        Transform configuredDurryRoot,
        Transform configuredViewer,
        OVRHand configuredRightHand,
        DustinyDemoFlow configuredDemoFlow)
    {
        if (configuredPageRoot != null)
        {
            pageRoot = configuredPageRoot;
        }

        if (configuredDurryRoot != null)
        {
            durryRoot = configuredDurryRoot;
        }

        if (configuredViewer != null)
        {
            viewer = configuredViewer;
        }

        if (configuredRightHand != null)
        {
            rightHand = configuredRightHand;
        }

        if (configuredDemoFlow != null)
        {
            demoFlow = configuredDemoFlow;
        }

        ResolveReferences();
        InitializePoseIfNeeded();
        ApplyFollowPose();
    }

    [ContextMenu("Reset Page To Durry Right")]
    public void ResetToDurryRight()
    {
        poseInitialized = false;
        InitializePoseIfNeeded();
        ApplyFollowPose();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (pageRoot == null || durryRoot == null)
        {
            return;
        }

        InitializePoseIfNeeded();

        bool isTracked = rightHand != null && rightHand.IsTracked && rightHand.IsDataValid;
        bool isIndexPinching = isTracked &&
                               rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool pinchDown = isIndexPinching && !wasIndexPinching;

        Vector3 handPosition = GetRightHandInteractionPosition();

        if (pinchDown && CanStartGrab() && IsHandInsideGrabArea(handPosition))
        {
            BeginGrab(handPosition);
        }

        if (isDragging)
        {
            if (!isIndexPinching || !isTracked)
            {
                EndGrab();
            }
            else
            {
                UpdateGrab(handPosition);
            }
        }
        else
        {
            ApplyFollowPose();
        }

        wasIndexPinching = isIndexPinching;
    }

    private bool CanStartGrab()
    {
        if (!onlyGrabWhileNotePageOpen)
        {
            return true;
        }

        return demoFlow == null || demoFlow.IsNotePageOpen;
    }

    private void BeginGrab(Vector3 handPosition)
    {
        isDragging = true;
        dragStartHandPosition = handPosition;
        dragStartPagePosition = pageRoot.position;
        dragStartPageRotation = pageRoot.rotation;

        if (logGrabEvents)
        {
            Debug.Log("[더리 노트] 검지 핀치로 노트 이동을 시작했습니다.", this);
        }
    }

    private void UpdateGrab(Vector3 handPosition)
    {
        Vector3 handDelta = handPosition - dragStartHandPosition;
        pageRoot.position = dragStartPagePosition + handDelta;
        pageRoot.rotation = dragStartPageRotation;
        SaveCurrentPoseRelativeToDurry();
    }

    private void EndGrab()
    {
        SaveCurrentPoseRelativeToDurry();
        isDragging = false;

        if (logGrabEvents)
        {
            Debug.Log("[더리 노트] 노트 이동을 종료하고 더리 기준 위치를 저장했습니다.", this);
        }
    }

    private void InitializePoseIfNeeded()
    {
        if (poseInitialized || pageRoot == null || durryRoot == null)
        {
            return;
        }

        Vector3 viewerRight = viewer != null ? viewer.right : Vector3.right;
        viewerRight.y = 0f;
        if (viewerRight.sqrMagnitude < 0.001f)
        {
            viewerRight = Vector3.right;
        }
        viewerRight.Normalize();

        Vector3 towardViewer = viewer != null
            ? viewer.position - durryRoot.position
            : -durryRoot.forward;
        towardViewer.y = 0f;
        if (towardViewer.sqrMagnitude < 0.001f)
        {
            towardViewer = -durryRoot.forward;
        }
        towardViewer.Normalize();

        Vector3 targetPosition =
            durryRoot.position +
            viewerRight * sideOffset +
            Vector3.up * heightOffset +
            towardViewer * depthOffset;

        Quaternion targetRotation = pageRoot.rotation;
        if (faceViewerOnFirstPlacement && viewer != null)
        {
            Vector3 direction = targetPosition - viewer.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        pageRoot.position = targetPosition;
        pageRoot.rotation = targetRotation;
        SaveCurrentPoseRelativeToDurry();
        poseInitialized = true;
    }

    private void SaveCurrentPoseRelativeToDurry()
    {
        if (pageRoot == null || durryRoot == null)
        {
            return;
        }

        preservedWorldOffset = pageRoot.position - durryRoot.position;
        preservedWorldRotation = pageRoot.rotation;

        durryLocalOffset = Quaternion.Inverse(durryRoot.rotation) * preservedWorldOffset;
        durryLocalRotation = Quaternion.Inverse(durryRoot.rotation) * pageRoot.rotation;
        poseInitialized = true;
    }

    private void ApplyFollowPose()
    {
        if (!poseInitialized || pageRoot == null || durryRoot == null)
        {
            return;
        }

        if (followDurryRotation)
        {
            pageRoot.position = durryRoot.position + durryRoot.rotation * durryLocalOffset;
            pageRoot.rotation = durryRoot.rotation * durryLocalRotation;
        }
        else
        {
            pageRoot.position = durryRoot.position + preservedWorldOffset;
            pageRoot.rotation = preservedWorldRotation;
        }
    }

    private bool IsHandInsideGrabArea(Vector3 handWorldPosition)
    {
        ResolveGrabHandle();

        RectTransform handle = grabHandleRect != null ? grabHandleRect : pageRoot;
        if (handle == null)
        {
            return Vector3.Distance(handWorldPosition, transform.position) <= fallbackGrabDistance;
        }

        float planeDistance = Mathf.Abs(
            Vector3.Dot(handWorldPosition - handle.position, handle.forward)
        );

        if (planeDistance > maximumGrabPlaneDistance)
        {
            return false;
        }

        Vector3 localPoint = handle.InverseTransformPoint(handWorldPosition);
        Rect paddedRect = handle.rect;
        paddedRect.xMin -= grabPadding;
        paddedRect.xMax += grabPadding;
        paddedRect.yMin -= grabPadding;
        paddedRect.yMax += grabPadding;

        return paddedRect.Contains(new Vector2(localPoint.x, localPoint.y));
    }

    private Vector3 GetRightHandInteractionPosition()
    {
        if (rightHand == null)
        {
            return Vector3.positiveInfinity;
        }

        Transform pointerPose = rightHand.PointerPose;
        return pointerPose != null
            ? pointerPose.position
            : rightHand.transform.position;
    }

    private void ResolveReferences()
    {
        if (pageRoot == null)
        {
            pageRoot = GetComponent<RectTransform>();
        }

        if (demoFlow == null)
        {
            demoFlow = FindFirstObjectByType<DustinyDemoFlow>();
        }

        if (demoFlow != null)
        {
            if (durryRoot == null && demoFlow.durryObject != null)
            {
                durryRoot = demoFlow.durryObject.transform;
            }

            if (viewer == null)
            {
                viewer = demoFlow.centerEyeAnchor;
            }

            if (rightHand == null)
            {
                rightHand = demoFlow.rightHand;
            }
        }

        if (rightHand == null)
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (OVRHand hand in hands)
            {
                if (hand == null)
                {
                    continue;
                }

                string path = GetHierarchyPath(hand.transform).ToLowerInvariant();
                if (path.Contains("right"))
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        ResolveGrabHandle();
    }

    private void ResolveGrabHandle()
    {
        if (grabHandleRect != null || !autoFindGrabHandle || pageRoot == null)
        {
            return;
        }

        RectTransform[] rects = pageRoot.GetComponentsInChildren<RectTransform>(true);
        RectTransform best = null;
        int bestScore = int.MinValue;

        foreach (RectTransform candidate in rects)
        {
            if (candidate == null || candidate == pageRoot)
            {
                continue;
            }

            string lower = candidate.name.ToLowerInvariant();
            int score = 0;

            if (lower == "grabhandle") score += 500;
            if (lower.Contains("grabhandle")) score += 400;
            if (lower == "noteheader") score += 350;
            if (lower.Contains("note") && lower.Contains("header")) score += 300;
            if (lower.Contains("header")) score += 100;
            if (candidate.gameObject.activeInHierarchy) score += 20;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (bestScore > 0)
        {
            grabHandleRect = best;
        }
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        string path = target.name;
        Transform parent = target.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
