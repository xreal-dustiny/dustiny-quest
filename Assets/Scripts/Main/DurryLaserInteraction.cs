// VERSION: 75_DURRY_LASER_INTERACTION_ONLY_2026-08-16
// 더리 레이캐스트 + 검지 핀치 리액션 전용 컴포넌트입니다.
// 1~2회 터치: Joyful / 3회 연속 터치: Focused 핀잔 + 반응 종료까지 입력 잠금.
// 위치 이동 및 페이지 관련 기능은 포함하지 않습니다.

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public class DurryLaserInteraction : MonoBehaviour
{
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

    [Header("Touch Reaction")]
    public bool enableTouchReaction = true;

    [Tooltip("연속 몇 회째에 핀잔 반응을 낼지 설정합니다. 현재 기준은 3회입니다.")]
    [Min(2)] public int focusedReactionThreshold = 3;

    [Tooltip("이 시간 이상 터치 간격이 벌어지면 연속 터치 횟수를 초기화합니다.")]
    [Min(0.5f)] public float tapSequenceResetSeconds = 6f;

    public string happyExpressionState = "Joyful";
    public string focusedExpressionState = "Focused";

    [TextArea(2, 4)]
    public string focusedReactionMessage = "왜 이렇게 자꾸 만지는 거지...?";

    [Tooltip("Focused 핀잔 반응이 유지되는 시간입니다. 이 시간 동안 추가 터치를 받지 않습니다.")]
    [Min(0.5f)] public float focusedMessageDuration = 2.4f;

    public bool playHappyVoice = true;

    [Header("Focused Reaction Lock")]
    [Tooltip("켜면 3번째 핀잔 반응이 끝날 때까지 더리를 다시 만질 수 없습니다.")]
    public bool lockInteractionDuringFocusedReaction = true;

    [Tooltip("핀잔 반응이 끝난 뒤 말풍선을 닫고 Idle 표정으로 복귀합니다.")]
    public bool hideFocusedMessageWhenFinished = true;

    [Header("Facing")]
    [Tooltip("위치 이동은 하지 않고, 더리가 사용자를 바라보는 회전만 유지합니다.")]
    public bool alwaysFaceUser = true;
    public bool useDemoFlowYawOffset = true;
    public float faceUserYawOffset = 180f;
    [Min(0f)] public float faceRotationSmoothing = 24f;

    [Header("Debug")]
    public bool logInteraction = true;

    public bool IsFocusedReactionLocked => focusedReactionLocked;
    public int CurrentConsecutiveTouchCount => shortTapCount;

    private bool wasIndexPinching;
    private bool pinchStartedOnDurry;

    private int shortTapCount;
    private float lastShortTapTime = -999f;
    private bool focusedReactionLocked;
    private Coroutine focusedMessageCoroutine;

    private Transform CenterEye => demoFlow != null ? demoFlow.centerEyeAnchor : null;

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
        pinchStartedOnDurry = false;
        focusedReactionLocked = false;
    }

    private void OnDisable()
    {
        pinchStartedOnDurry = false;
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
        rayDistance = Mathf.Max(0.1f, rayDistance);
        targetPaddingWorld = Mathf.Max(0f, targetPaddingWorld);
        faceRotationSmoothing = Mathf.Max(0f, faceRotationSmoothing);

        if (rayLocalDirection.sqrMagnitude < 0.0001f)
        {
            rayLocalDirection = Vector3.forward;
        }
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
            pinchStartedOnDurry = false;
            return;
        }

        // Focused 핀잔 도중에는 추가 입력을 무시합니다.
        // 현재 핀치 상태는 계속 추적하므로 락이 풀리는 순간 새 입력으로 오인하지 않습니다.
        if (focusedReactionLocked && lockInteractionDuringFocusedReaction)
        {
            pinchStartedOnDurry = false;
            return;
        }

        if (pinchDown)
        {
            BeginTouchIfPointingAtDurry();
        }

        // 더리 위에서 시작한 핀치가 미션 확인 입력으로 중복 처리되지 않도록
        // 손가락을 붙이고 있는 동안 전역 검지 확인을 계속 막습니다.
        if (isIndexPinching && pinchStartedOnDurry)
        {
            demoFlow?.SuppressGlobalConfirmInput(0.35f);
        }

        if (pinchUp)
        {
            EndTouch();
        }
    }

    private void LateUpdate()
    {
        if (alwaysFaceUser)
        {
            FaceDurryTowardUser();
        }
    }

    private void BeginTouchIfPointingAtDurry()
    {
        pinchStartedOnDurry = false;

        if (!enableTouchReaction ||
            (focusedReactionLocked && lockInteractionDuringFocusedReaction) ||
            !CanReceiveDurryTouch() ||
            !IsPointingAtDurry())
        {
            return;
        }

        pinchStartedOnDurry = true;

        // DurryLaserInteraction은 DemoFlow보다 먼저 실행되므로
        // 같은 프레임의 검지 핀치가 미션 Yes 입력으로 넘어가는 것을 막습니다.
        demoFlow?.SuppressGlobalConfirmInput(0.40f);

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] 더리를 향한 검지 핀치 시작");
        }
    }

    private void EndTouch()
    {
        if (!pinchStartedOnDurry)
        {
            return;
        }

        pinchStartedOnDurry = false;

        if (!enableTouchReaction ||
            (focusedReactionLocked && lockInteractionDuringFocusedReaction) ||
            !CanReceiveDurryTouch())
        {
            return;
        }

        RegisterTouchReaction();
    }

    private bool CanReceiveDurryTouch()
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

        return demoFlow == null || demoFlow.CanReceiveFreeDurryInteraction();
    }

    private void RegisterTouchReaction()
    {
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

        // 1~2번째는 락 없이 Joyful 반응만 재생하므로
        // 애니메이션이 끝나기 전에도 다시 터치할 수 있습니다.
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

        if (lockInteractionDuringFocusedReaction)
        {
            focusedReactionLocked = true;
        }

        pinchStartedOnDurry = false;
        demoFlow.SuppressGlobalConfirmInput(focusedMessageDuration + 0.15f);
        demoFlow.ShowDurryInteractionMessage(message, focusedExpressionState, clip);

        if (focusedMessageCoroutine != null)
        {
            StopCoroutine(focusedMessageCoroutine);
        }

        focusedMessageCoroutine = StartCoroutine(FinishFocusedReactionAfterDelay(message));

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] 3회 연속 터치 → Focused 핀잔. 반응 종료까지 터치 잠금");
        }
    }

    private IEnumerator FinishFocusedReactionAfterDelay(string expectedMessage)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, focusedMessageDuration));

        focusedMessageCoroutine = null;

        bool sameMessageIsVisible =
            demoFlow != null &&
            demoFlow.speechText != null &&
            demoFlow.speechText.gameObject.activeInHierarchy &&
            demoFlow.speechText.text == expectedMessage;

        if (hideFocusedMessageWhenFinished && sameMessageIsVisible)
        {
            demoFlow.HideDialoguePanels();
        }

        focusedReactionLocked = false;

        if (logInteraction)
        {
            Debug.Log("[더리 인터렉션] Focused 핀잔 종료 → 다시 더리를 만질 수 있습니다.");
        }
    }

    private bool IsPointingAtDurry()
    {
        if (durryObject == null || rayOrigin == null)
        {
            return false;
        }

        Ray ray = GetPointerRay();

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            rayDistance,
            raycastLayerMask,
            QueryTriggerInteraction.Collide
        );

        float nearestDurryDistance = float.PositiveInfinity;
        float nearestOtherDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            if (IsTransformPartOfDurry(hit.collider.transform))
            {
                nearestDurryDistance = Mathf.Min(nearestDurryDistance, hit.distance);
            }
            else
            {
                nearestOtherDistance = Mathf.Min(nearestOtherDistance, hit.distance);
            }
        }

        // 다른 Collider가 더리 앞을 가리고 있으면 그 뒤의 더리를 터치한 것으로 처리하지 않습니다.
        if (!float.IsPositiveInfinity(nearestDurryDistance))
        {
            return nearestDurryDistance <= nearestOtherDistance + 0.001f;
        }

        if (!useRendererBoundsFallback)
        {
            return false;
        }

        Renderer[] renderers = durryObject.GetComponentsInChildren<Renderer>(true);
        float nearestRendererDistance = float.PositiveInfinity;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            bounds.Expand(targetPaddingWorld * 2f);

            if (bounds.IntersectRay(ray, out float enter) &&
                enter >= 0f &&
                enter <= rayDistance)
            {
                nearestRendererDistance = Mathf.Min(nearestRendererDistance, enter);
            }
        }

        return !float.IsPositiveInfinity(nearestRendererDistance) &&
               nearestRendererDistance <= nearestOtherDistance + 0.001f;
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
        Vector3 direction = rayOrigin.TransformDirection(rayLocalDirection.normalized);

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = rayOrigin.forward;
        }

        return new Ray(rayOrigin.position, direction.normalized);
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
                if (hand == null)
                {
                    continue;
                }

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
