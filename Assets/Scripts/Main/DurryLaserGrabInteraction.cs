// VERSION: 73_DURRY_TOUCH_LOCK_RESTORED_2026-08-16
// Restores Durry raycast + index-pinch reactions while preserving 2-second long-pinch movement.
// 1st/2nd short touch: Joyful reaction. 3rd consecutive short touch: Focused scolding reaction.
// While the Focused scolding reaction is active, all further Durry touch/grab input is ignored.

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public class DurryLaserGrabInteraction : MonoBehaviour
{
    private enum GrabTarget
    {
        None,
        Durry,
        Page
    }

    [Header("Required References")]
    [Tooltip("씬의 DustinyDemoFlow입니다. 비워두면 자동 탐색합니다.")]
    public DustinyDemoFlow demoFlow;

    [Tooltip("DurryRoot입니다. 비워두면 DustinyDemoFlow의 Durry Object를 사용합니다.")]
    public GameObject durryObject;

    [Tooltip("오른손 OVRHand입니다. 비워두면 이름에 Right가 포함된 OVRHand를 자동 탐색합니다.")]
    public OVRHand rightHand;

    [Header("Laser")]
    [Tooltip("실제 오른손 레이저 시작 Transform입니다. 비워두면 RightHand 아래 PointerPose/RayOrigin 등을 자동 탐색합니다.")]
    public Transform rayOrigin;

    [Tooltip("Ray Origin의 로컬 발사 방향입니다. 보통 Z+입니다. 반대면 Z-로 바꾸세요.")]
    public Vector3 rayLocalDirection = Vector3.forward;

    [Min(0.1f)] public float rayDistance = 8f;
    public LayerMask raycastLayerMask = ~0;

    [Tooltip("더리 감지용 Collider입니다. 비워두면 자식 Collider를 찾고, 없으면 BoxCollider를 자동 생성합니다.")]
    public Collider durryInteractionCollider;
    public bool autoCreateDurryCollider = true;

    [Tooltip("Collider 판정이 빗나갔을 때 Renderer Bounds로 한 번 더 더리를 검사합니다.")]
    public bool useRendererBoundsFallback = true;

    [Min(0f)] public float targetPaddingWorld = 0.07f;

    [Header("Short Touch Reaction")]
    public bool enableShortPinchReaction = true;

    [Tooltip("연속 몇 회째에 핀잔 반응을 낼지 설정합니다. 요청 기준은 3회입니다.")]
    [Min(2)] public int focusedReactionThreshold = 3;

    [Tooltip("이 시간 이상 터치 간격이 벌어지면 연속 터치 횟수를 0으로 초기화합니다.")]
    [Min(0.5f)] public float tapSequenceResetSeconds = 6f;

    public string happyExpressionState = "Joyful";
    public string focusedExpressionState = "Focused";

    [TextArea(2, 4)]
    public string focusedReactionMessage = "왜 이렇게 자꾸 만지는 거지...?";

    [Tooltip("Focused 핀잔 반응이 유지되는 시간입니다. 이 시간 동안 추가 터치/이동 입력을 받지 않습니다.")]
    [Min(0.5f)] public float focusedMessageDuration = 2.4f;

    public bool playHappyVoice = true;

    [Header("Focused Reaction Lock")]
    [Tooltip("켜면 3번째 핀잔 반응이 끝날 때까지 더리를 다시 만지거나 잡을 수 없습니다.")]
    public bool lockInteractionDuringFocusedReaction = true;

    [Tooltip("핀잔 반응이 끝난 뒤 말풍선을 닫고 Idle 표정으로 복귀합니다.")]
    public bool hideFocusedMessageWhenFinished = true;

    [Header("Long Pinch Move")]
    [Tooltip("2초 이상 검지 핀치를 유지하면 더리 또는 열린 페이지를 잡아서 이동합니다.")]
    public bool enableLongPinchMove = true;

    [Tooltip("켜면 롱핀치 기준을 항상 정확히 2초로 사용합니다.")]
    public bool enforceTwoSecondHold = true;

    [Min(0.1f)] public float longPressToGrabSeconds = 2f;
    public bool allowGrabFromDurry = true;
    public bool allowGrabFromOpenPage = true;

    [Tooltip("열린 페이지 전체가 아니라 특정 Grab 영역만 사용하려면 연결하세요. 비우면 BigNoteRoot 전체를 사용합니다.")]
    public RectTransform pageGrabArea;

    [Header("X / Y / Z Movement")]
    public bool allowScreenPlaneMovement = true;
    public bool allowDepthMovement = true;
    [Min(0f)] public float depthSensitivity = 3f;
    [Min(0f)] public float depthDeadZone = 0.008f;
    [Min(0.1f)] public float minimumDistanceFromHead = 0.45f;
    [Min(0.2f)] public float maximumDistanceFromHead = 4f;
    [Min(0f)] public float dragFollowSpeed = 20f;

    [Header("Always Face User")]
    public bool alwaysFaceUser = true;
    public bool useDemoFlowYawOffset = true;
    public float faceUserYawOffset = 180f;
    [Min(0f)] public float faceRotationSmoothing = 24f;

    [Header("Debug")]
    public bool logInteraction = true;

    public bool IsDragging => isDragging;
    public bool IsFocusedReactionLocked => focusedReactionLocked;
    public int CurrentConsecutiveTouchCount => shortTapCount;

    private bool wasIndexPinching;
    private bool pinchStartedOnTarget;
    private bool isDragging;
    private float pinchStartedTime;
    private GrabTarget activeTarget = GrabTarget.None;

    private Vector3 grabEyePosition;
    private Vector3 grabRayOriginPosition;
    private Vector3 grabForward;
    private Vector3 grabRight;
    private Vector3 grabUp;
    private float initialObjectDepth;
    private float grabOffsetRight;
    private float grabOffsetUp;

    private int shortTapCount;
    private float lastShortTapTime = -999f;
    private bool focusedReactionLocked;
    private Coroutine focusedMessageCoroutine;
    private string activeFocusedMessage = string.Empty;

    private Transform CenterEye => demoFlow != null ? demoFlow.centerEyeAnchor : null;

    private RectTransform PageRoot
    {
        get
        {
            if (pageGrabArea != null)
            {
                return pageGrabArea;
            }

            return demoFlow != null ? demoFlow.bigNoteRect : null;
        }
    }

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        NormalizeSettings();
        ResolveReferences();
        SetupDurryCollider();
    }

    private void OnEnable()
    {
        NormalizeSettings();
        ResolveReferences();
        SetupDurryCollider();
        CancelCurrentPinch();
        focusedReactionLocked = false;
    }

    private void OnDisable()
    {
        CancelCurrentPinch();
        focusedReactionLocked = false;

        if (focusedMessageCoroutine != null)
        {
            StopCoroutine(focusedMessageCoroutine);
            focusedMessageCoroutine = null;
        }
    }

    private void OnValidate()
    {
        NormalizeSettings();
    }

    private void NormalizeSettings()
    {
        focusedReactionThreshold = Mathf.Max(2, focusedReactionThreshold);
        tapSequenceResetSeconds = Mathf.Max(0.5f, tapSequenceResetSeconds);
        focusedMessageDuration = Mathf.Max(0.5f, focusedMessageDuration);

        if (enforceTwoSecondHold)
        {
            longPressToGrabSeconds = 2f;
        }
        else
        {
            longPressToGrabSeconds = Mathf.Max(0.1f, longPressToGrabSeconds);
        }

        rayDistance = Mathf.Max(0.1f, rayDistance);
        targetPaddingWorld = Mathf.Max(0f, targetPaddingWorld);
        depthSensitivity = Mathf.Max(0f, depthSensitivity);
        depthDeadZone = Mathf.Max(0f, depthDeadZone);
        minimumDistanceFromHead = Mathf.Max(0.1f, minimumDistanceFromHead);
        maximumDistanceFromHead = Mathf.Max(minimumDistanceFromHead + 0.05f, maximumDistanceFromHead);
        dragFollowSpeed = Mathf.Max(0f, dragFollowSpeed);
        faceRotationSmoothing = Mathf.Max(0f, faceRotationSmoothing);

        if (rayLocalDirection.sqrMagnitude < 0.0001f)
        {
            rayLocalDirection = Vector3.forward;
        }
    }

    private float GetRequiredHoldSeconds()
    {
        return enforceTwoSecondHold ? 2f : Mathf.Max(0.1f, longPressToGrabSeconds);
    }

    private void Update()
    {
        ResolveReferences();

        bool isTracked = rightHand != null && rightHand.IsTracked && rightHand.IsDataValid;
        bool isIndexPinching = isTracked &&
                               rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        bool pinchDown = isIndexPinching && !wasIndexPinching;
        bool pinchUp = !isIndexPinching && wasIndexPinching;
        wasIndexPinching = isIndexPinching;

        if (!isTracked)
        {
            CancelCurrentPinch();
            return;
        }

        // 핀잔 반응 중에는 검지 핀치가 들어와도 아무것도 시작하지 않습니다.
        // 현재 핀치 상태만 추적해서, 락 해제 직후 손가락이 계속 붙어 있어도 새 입력으로 오인하지 않습니다.
        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            CancelCurrentPinch();
            return;
        }

        if (pinchDown)
        {
            BeginPinchIfPointingAtTarget();
        }

        if (isIndexPinching && pinchStartedOnTarget)
        {
            UpdateHeldPinch();
        }

        if (pinchUp)
        {
            EndPinch();
        }
    }

    private void LateUpdate()
    {
        if (alwaysFaceUser)
        {
            FaceDurryTowardUser();
        }
    }

    private void BeginPinchIfPointingAtTarget()
    {
        CancelCurrentPinch();

        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            return;
        }

        if (!CanBeginGrab() || !TryGetMovableTarget(out GrabTarget target))
        {
            return;
        }

        activeTarget = target;
        pinchStartedOnTarget = true;
        isDragging = false;
        pinchStartedTime = Time.unscaledTime;

        // 더리/페이지를 향한 검지 핀치를 DustinyDemoFlow가 미션 확인 입력으로 중복 처리하지 않게 합니다.
        demoFlow?.SuppressGlobalConfirmInput(
            Mathf.Max(0.35f, GetRequiredHoldSeconds() + 0.15f)
        );

        if (logInteraction)
        {
            Debug.Log(
                target == GrabTarget.Durry
                    ? "[더리 인터렉션] 더리 위에서 검지 핀치 시작"
                    : "[더리 인터렉션] 열린 페이지 위에서 검지 핀치 시작"
            );
        }
    }

    private void UpdateHeldPinch()
    {
        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            CancelCurrentPinch();
            return;
        }

        demoFlow?.SuppressGlobalConfirmInput(0.35f);

        if (!enableLongPinchMove || isDragging)
        {
            if (isDragging)
            {
                UpdateDragging();
            }
            return;
        }

        if (Time.unscaledTime - pinchStartedTime < GetRequiredHoldSeconds())
        {
            return;
        }

        StartDragging();
        UpdateDragging();
    }

    private void EndPinch()
    {
        if (!pinchStartedOnTarget)
        {
            return;
        }

        bool completedDrag = isDragging;
        float heldSeconds = Time.unscaledTime - pinchStartedTime;
        GrabTarget completedTarget = activeTarget;

        CancelCurrentPinch();

        if (completedDrag)
        {
            if (logInteraction)
            {
                Debug.Log("[더리 인터렉션] 롱핀치 이동 종료");
            }
            return;
        }

        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            return;
        }

        if (completedTarget == GrabTarget.Durry &&
            enableShortPinchReaction &&
            heldSeconds < GetRequiredHoldSeconds() &&
            CanPlayShortDurryReaction())
        {
            RegisterShortPinchReaction();
        }
    }

    private void RegisterShortPinchReaction()
    {
        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now - lastShortTapTime > tapSequenceResetSeconds)
        {
            shortTapCount = 0;
        }

        lastShortTapTime = now;
        shortTapCount++;

        int threshold = Mathf.Max(2, focusedReactionThreshold);

        if (shortTapCount >= threshold)
        {
            shortTapCount = 0;
            ShowFocusedReaction();
            return;
        }

        // 1~2번째 터치는 Joyful만 재생합니다.
        // 별도 락을 걸지 않으므로 Joyful 애니메이션 도중에도 계속 만질 수 있습니다.
        if (demoFlow != null)
        {
            demoFlow.PlayDurryExpression(happyExpressionState);

            if (playHappyVoice)
            {
                AudioClip clip = demoFlow.voiceHappy != null
                    ? demoFlow.voiceHappy
                    : demoFlow.voiceJoy != null
                        ? demoFlow.voiceJoy
                        : demoFlow.voiceCheerful;

                demoFlow.PlayDurryVoice(clip);
            }
        }

        if (logInteraction)
        {
            Debug.Log($"[더리 인터렉션] 기쁜 터치 반응 {shortTapCount}/{threshold - 1}");
        }
    }

    private void ShowFocusedReaction()
    {
        if (demoFlow == null)
        {
            return;
        }

        string message = string.IsNullOrWhiteSpace(focusedReactionMessage)
            ? "왜 이렇게 자꾸 만지는 거지...?"
            : focusedReactionMessage;

        AudioClip clip = demoFlow.voiceDizzy != null
            ? demoFlow.voiceDizzy
            : demoFlow.voiceWaiting != null
                ? demoFlow.voiceWaiting
                : demoFlow.voiceSpecial;

        // 3번째 터치부터는 먼저 락을 걸고 Focused 반응을 재생합니다.
        // 락이 풀릴 때까지 추가 검지 핀치/드래그를 받지 않습니다.
        if (lockInteractionDuringFocusedReaction)
        {
            focusedReactionLocked = true;
        }

        CancelCurrentPinch();
        demoFlow.SuppressGlobalConfirmInput(focusedMessageDuration + 0.15f);

        activeFocusedMessage = message;
        demoFlow.ShowDurryInteractionMessage(message, focusedExpressionState, clip);

        if (focusedMessageCoroutine != null)
        {
            StopCoroutine(focusedMessageCoroutine);
        }

        focusedMessageCoroutine = StartCoroutine(FinishFocusedReactionAfterDelay(message));

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] 3회 연속 터치 → Focused 핀잔. 반응 종료까지 입력 잠금");
        }
    }

    private IEnumerator FinishFocusedReactionAfterDelay(string expectedMessage)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, focusedMessageDuration));

        focusedMessageCoroutine = null;

        // 그 사이 다른 시스템이 다른 문구를 띄웠다면 그 UI는 닫지 않습니다.
        bool sameMessageIsVisible =
            demoFlow != null &&
            demoFlow.speechText != null &&
            demoFlow.speechText.gameObject.activeInHierarchy &&
            demoFlow.speechText.text == expectedMessage;

        if (hideFocusedMessageWhenFinished && sameMessageIsVisible)
        {
            demoFlow.HideDialoguePanels();
        }

        activeFocusedMessage = string.Empty;
        focusedReactionLocked = false;

        // 락 해제 순간 검지가 아직 핀치 상태라면 다음 프레임에 새 pinchDown으로 오인하지 않도록
        // wasIndexPinching은 Update에서 현재 손 상태를 계속 유지합니다.
        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] Focused 핀잔 종료 → 다시 더리를 만질 수 있습니다.");
        }
    }

    private bool CanBeginGrab()
    {
        if (!enabled ||
            !gameObject.activeInHierarchy ||
            durryObject == null ||
            !durryObject.activeInHierarchy ||
            rayOrigin == null)
        {
            return false;
        }

        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            return false;
        }

        if (demoFlow == null)
        {
            return true;
        }

        // 일반 상태에서는 더리 자유 상호작용을 허용하고,
        // 열린 페이지에서는 롱핀치 이동만 유지할 수 있도록 타겟 판정 단계에서 구분합니다.
        return demoFlow.CanReceiveFreeDurryInteraction() || demoFlow.IsAnyPageOpen;
    }

    private bool CanPlayShortDurryReaction()
    {
        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            return false;
        }

        return demoFlow == null || demoFlow.CanReceiveFreeDurryInteraction();
    }

    private bool TryGetMovableTarget(out GrabTarget target)
    {
        target = GrabTarget.None;
        Ray ray = GetPointerRay();

        float durryDistance = float.PositiveInfinity;
        float pageDistance = float.PositiveInfinity;

        bool hitDurry = allowGrabFromDurry && TryGetDurryDistance(ray, out durryDistance);
        bool hitPage = allowGrabFromOpenPage &&
                       demoFlow != null &&
                       demoFlow.IsAnyPageOpen &&
                       TryGetPageDistance(ray, out pageDistance);

        if (!hitDurry && !hitPage)
        {
            return false;
        }

        if (hitDurry && (!hitPage || durryDistance <= pageDistance))
        {
            target = GrabTarget.Durry;
        }
        else
        {
            target = GrabTarget.Page;
        }

        return true;
    }

    private bool TryGetDurryDistance(Ray ray, out float distance)
    {
        distance = float.PositiveInfinity;
        bool found = false;

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            rayDistance,
            raycastLayerMask,
            QueryTriggerInteraction.Collide
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || !IsTransformPartOfDurry(hit.collider.transform))
            {
                continue;
            }

            if (hit.distance < distance)
            {
                distance = hit.distance;
                found = true;
            }
        }

        if (found || !useRendererBoundsFallback || durryObject == null)
        {
            return found;
        }

        Renderer[] renderers = durryObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            bounds.Expand(targetPaddingWorld * 2f);

            if (!bounds.IntersectRay(ray, out float enter) || enter < 0f || enter > rayDistance)
            {
                continue;
            }

            if (enter < distance)
            {
                distance = enter;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetPageDistance(Ray ray, out float distance)
    {
        distance = float.PositiveInfinity;
        RectTransform page = PageRoot;

        if (page == null || !page.gameObject.activeInHierarchy)
        {
            return false;
        }

        Plane plane = new Plane(page.forward, page.position);
        if (!plane.Raycast(ray, out float enter) || enter < 0f || enter > rayDistance)
        {
            return false;
        }

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector3 localPoint3D = page.InverseTransformPoint(hitPoint);
        Vector2 localPoint = new Vector2(localPoint3D.x, localPoint3D.y);

        if (!page.rect.Contains(localPoint))
        {
            return false;
        }

        distance = enter;
        return true;
    }

    private bool IsTransformPartOfDurry(Transform target)
    {
        if (target == null || durryObject == null)
        {
            return false;
        }

        Transform durryTransform = durryObject.transform;
        return target == durryTransform || target.IsChildOf(durryTransform);
    }

    private Ray GetPointerRay()
    {
        Vector3 direction = rayOrigin != null
            ? rayOrigin.TransformDirection(rayLocalDirection.normalized)
            : Vector3.forward;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = rayOrigin != null ? rayOrigin.forward : Vector3.forward;
        }

        return new Ray(
            rayOrigin != null ? rayOrigin.position : Vector3.zero,
            direction.normalized
        );
    }

    private void StartDragging()
    {
        if (!enableLongPinchMove ||
            focusedReactionLocked ||
            durryObject == null ||
            rayOrigin == null)
        {
            CancelCurrentPinch();
            return;
        }

        Transform eye = CenterEye;
        grabEyePosition = eye != null ? eye.position : rayOrigin.position;
        grabRayOriginPosition = rayOrigin.position;

        grabForward = eye != null ? eye.forward : rayOrigin.forward;
        if (grabForward.sqrMagnitude < 0.0001f) grabForward = Vector3.forward;
        grabForward.Normalize();

        grabRight = eye != null ? eye.right : rayOrigin.right;
        if (grabRight.sqrMagnitude < 0.0001f) grabRight = Vector3.right;
        grabRight.Normalize();

        grabUp = eye != null ? eye.up : Vector3.up;
        if (grabUp.sqrMagnitude < 0.0001f) grabUp = Vector3.up;
        grabUp.Normalize();

        initialObjectDepth = Vector3.Dot(
            durryObject.transform.position - grabEyePosition,
            grabForward
        );
        initialObjectDepth = Mathf.Clamp(initialObjectDepth, minimumDistanceFromHead, maximumDistanceFromHead);

        Plane startPlane = new Plane(grabForward, grabEyePosition + grabForward * initialObjectDepth);
        Ray ray = GetPointerRay();

        if (startPlane.Raycast(ray, out float enter) && enter >= 0f)
        {
            Vector3 pointerPoint = ray.GetPoint(enter);
            Vector3 offset = durryObject.transform.position - pointerPoint;
            grabOffsetRight = Vector3.Dot(offset, grabRight);
            grabOffsetUp = Vector3.Dot(offset, grabUp);
        }
        else
        {
            grabOffsetRight = 0f;
            grabOffsetUp = 0f;
        }

        isDragging = true;

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] 2초 롱핀치 → 더리 이동 시작");
        }
    }

    private void UpdateDragging()
    {
        if (!isDragging ||
            focusedReactionLocked ||
            durryObject == null ||
            rayOrigin == null)
        {
            return;
        }

        float desiredDepth = initialObjectDepth;

        if (allowDepthMovement)
        {
            Vector3 handDelta = rayOrigin.position - grabRayOriginPosition;
            float rawDepthDelta = Vector3.Dot(handDelta, grabForward);

            if (Mathf.Abs(rawDepthDelta) < depthDeadZone)
            {
                rawDepthDelta = 0f;
            }
            else
            {
                rawDepthDelta -= Mathf.Sign(rawDepthDelta) * depthDeadZone;
            }

            desiredDepth += rawDepthDelta * depthSensitivity;
        }

        desiredDepth = Mathf.Clamp(desiredDepth, minimumDistanceFromHead, maximumDistanceFromHead);

        Vector3 planePoint = grabEyePosition + grabForward * desiredDepth;
        Plane movementPlane = new Plane(grabForward, planePoint);
        Ray pointerRay = GetPointerRay();
        Vector3 targetPosition;

        if (allowScreenPlaneMovement &&
            movementPlane.Raycast(pointerRay, out float enter) &&
            enter >= 0f &&
            enter <= rayDistance * 3f)
        {
            Vector3 pointerPoint = pointerRay.GetPoint(enter);
            targetPosition = pointerPoint +
                             grabRight * grabOffsetRight +
                             grabUp * grabOffsetUp;
        }
        else
        {
            Vector3 currentOffset = durryObject.transform.position - planePoint;
            float currentRight = Vector3.Dot(currentOffset, grabRight);
            float currentUp = Vector3.Dot(currentOffset, grabUp);

            targetPosition = planePoint +
                             grabRight * currentRight +
                             grabUp * currentUp;
        }

        if (dragFollowSpeed <= 0f)
        {
            durryObject.transform.position = targetPosition;
        }
        else
        {
            float t = 1f - Mathf.Exp(-dragFollowSpeed * Time.unscaledDeltaTime);
            durryObject.transform.position = Vector3.Lerp(
                durryObject.transform.position,
                targetPosition,
                t
            );
        }

        if (alwaysFaceUser)
        {
            FaceDurryTowardUser();
        }
    }

    private void FaceDurryTowardUser()
    {
        if (durryObject == null || CenterEye == null || !durryObject.activeInHierarchy)
        {
            return;
        }

        Vector3 toUser = CenterEye.position - durryObject.transform.position;
        toUser.y = 0f;

        if (toUser.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float yawOffset = useDemoFlowYawOffset && demoFlow != null
            ? demoFlow.durryYawOffset
            : faceUserYawOffset;

        Quaternion targetRotation =
            Quaternion.LookRotation(toUser.normalized, Vector3.up) *
            Quaternion.Euler(0f, yawOffset, 0f);

        if (faceRotationSmoothing <= 0f)
        {
            durryObject.transform.rotation = targetRotation;
            return;
        }

        float t = 1f - Mathf.Exp(-faceRotationSmoothing * Time.unscaledDeltaTime);
        durryObject.transform.rotation = Quaternion.Slerp(
            durryObject.transform.rotation,
            targetRotation,
            t
        );
    }

    private void CancelCurrentPinch()
    {
        pinchStartedOnTarget = false;
        isDragging = false;
        pinchStartedTime = 0f;
        activeTarget = GrabTarget.None;
    }

    private void ResolveReferences()
    {
        if (demoFlow == null)
        {
            demoFlow = FindFirstObjectByType<DustinyDemoFlow>();
        }

        if (durryObject == null && demoFlow != null)
        {
            durryObject = demoFlow.durryObject;
        }

        if (rightHand == null)
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (OVRHand hand in hands)
            {
                if (hand == null) continue;

                string ownName = hand.gameObject.name.ToLowerInvariant();
                string parentName = hand.transform.parent != null
                    ? hand.transform.parent.name.ToLowerInvariant()
                    : string.Empty;

                if ((ownName + " " + parentName).Contains("right"))
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        if (rayOrigin == null && rightHand != null)
        {
            Transform root = rightHand.transform;
            rayOrigin =
                FindChildContains(root, "pointerpose") ??
                FindChildContains(root, "rayorigin") ??
                FindChildContains(root, "handrayorigin") ??
                FindChildContains(root, "aim") ??
                FindChildContains(root, "ray") ??
                root;
        }
    }

    private void SetupDurryCollider()
    {
        if (durryObject == null)
        {
            return;
        }

        if (durryInteractionCollider == null)
        {
            durryInteractionCollider = durryObject.GetComponentInChildren<Collider>(true);
        }

        if (durryInteractionCollider != null || !autoCreateDurryCollider)
        {
            return;
        }

        Renderer[] renderers = durryObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        bool hasBounds = false;
        Bounds worldBounds = new Bounds(durryObject.transform.position, Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                worldBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return;
        }

        BoxCollider box = durryObject.AddComponent<BoxCollider>();
        Vector3 localCenter = durryObject.transform.InverseTransformPoint(worldBounds.center);
        Vector3 localSize = durryObject.transform.InverseTransformVector(worldBounds.size);

        box.center = localCenter;
        box.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z)
        ) + Vector3.one * 0.03f;

        box.isTrigger = true;
        durryInteractionCollider = box;

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] Collider가 없어 DurryRoot에 BoxCollider를 자동 생성했습니다.");
        }
    }

    private static Transform FindChildContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string lowerKeyword = keyword.ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != null && child.name.ToLowerInvariant().Contains(lowerKeyword))
            {
                return child;
            }
        }

        return null;
    }
}
