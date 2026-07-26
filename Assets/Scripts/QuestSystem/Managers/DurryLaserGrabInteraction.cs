using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 더리를 오른손 레이저 + 검지 핀치로 상호작용하는 독립 컴포넌트입니다.
///
/// - 짧게 핀치: 더리가 귀엽게 반응합니다.
/// - 짧은 핀치 4회: Focused 표정과 반복 터치 멘트를 표시합니다.
/// - 더리를 가리킨 채 길게 핀치: 현재 깊이를 유지한 채 화면 기준 좌우/상하로 이동합니다.
///
/// DustinyDemoFlow의 직렬화 필드 구조를 변경하지 않기 위해 별도 스크립트로 분리했습니다.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class DurryLaserGrabInteraction : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("씬의 DustinyDemoFlow를 연결하세요. 비워두면 자동 탐색합니다.")]
    public DustinyDemoFlow demoFlow;

    [Tooltip("이동시킬 DurryRoot를 연결하세요. 비워두면 DustinyDemoFlow의 Durry Object를 사용합니다.")]
    public GameObject durryObject;

    [Tooltip("오른손 OVRHand를 연결하세요. 비워두면 이름에 Right가 포함된 OVRHand를 자동 탐색합니다.")]
    public OVRHand rightHand;

    [Header("Laser")]
    [Tooltip("실제 오른손 레이저가 시작되는 Transform입니다. 자동 탐색이 틀리면 직접 연결하세요.")]
    public Transform rayOrigin;

    [Tooltip("Ray Origin의 로컬 발사 방향입니다. 일반적으로 Z+입니다. 반대로 판정되면 Z-로 바꾸세요.")]
    public Vector3 rayLocalDirection = Vector3.forward;

    [Min(0.1f)] public float rayDistance = 8f;
    public LayerMask raycastLayerMask = ~0;

    [Tooltip("더리에 이미 Collider가 있다면 연결하세요. 비워두면 자식 Collider를 찾고, 없으면 BoxCollider를 자동 생성합니다.")]
    public Collider durryInteractionCollider;
    public bool autoCreateDurryCollider = true;

    [Header("Tap Reaction")]
    [Tooltip("드래그가 시작되기 전에 핀치를 놓으면 짧은 터치로 처리합니다.")]
    public bool enableShortPinchReaction = true;

    [Min(2)] public int focusedReactionThreshold = 4;
    [Min(0.5f)] public float tapSequenceResetSeconds = 6f;
    public string happyExpressionState = "Joyful";
    public string focusedExpressionState = "Focused";

    [TextArea(2, 4)]
    public string focusedReactionMessage = "왜 이렇게 자꾸 만지는 거지...?";

    [Min(0.5f)] public float focusedMessageDuration = 2.4f;
    public bool playHappyVoice = true;

    [Header("Long Pinch Drag")]
    [Tooltip("이 시간 이상 검지 핀치를 유지하면 더리 이동을 시작합니다.")]
    [Min(0.1f)] public float longPressToGrabSeconds = 0.45f;

    [Tooltip("켜면 더리가 사용자 화면과 평행한 평면에서 이동합니다. 깊이는 잡기 시작한 위치로 고정됩니다.")]
    public bool lockDepthWhileDragging = true;

    [Tooltip("0이면 손 레이저를 즉시 따라갑니다. 값이 크면 조금 부드럽게 따라갑니다.")]
    [Min(0f)] public float dragFollowSpeed = 0f;

    public bool logInteraction = true;

    private bool wasIndexPinching;
    private bool pinchStartedOnDurry;
    private bool isDragging;
    private float pinchStartedTime;

    private Plane dragPlane;
    private Vector3 dragOffset;
    private Vector3 fixedWorldPositionAtGrab;

    private int shortTapCount;
    private float lastShortTapTime = -999f;
    private Coroutine focusedMessageCoroutine;
    private string activeFocusedMessage = string.Empty;

    private void Awake()
    {
        ResolveReferences();
        SetupDurryCollider();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SetupDurryCollider();
    }

    private void OnDisable()
    {
        CancelCurrentPinch();

        if (focusedMessageCoroutine != null)
        {
            StopCoroutine(focusedMessageCoroutine);
            focusedMessageCoroutine = null;
        }
    }

    private void Update()
    {
        ResolveReferences();

        bool isTracked = rightHand != null &&
                         rightHand.IsTracked &&
                         rightHand.IsDataValid;

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

        if (pinchDown)
        {
            BeginPinchIfPointingAtDurry();
        }

        if (isIndexPinching && pinchStartedOnDurry)
        {
            UpdateHeldPinch();
        }

        if (pinchUp)
        {
            EndPinch();
        }
    }

    private void BeginPinchIfPointingAtDurry()
    {
        if (!CanReceiveInteraction() || !TryGetDurryHit(out RaycastHit hit))
        {
            CancelCurrentPinch();
            return;
        }

        pinchStartedOnDurry = true;
        isDragging = false;
        pinchStartedTime = Time.unscaledTime;
        fixedWorldPositionAtGrab = durryObject.transform.position;

        BuildDragPlane(hit);

        // DustinyDemoFlow가 같은 검지 핀치를 미션 확인 입력으로 처리하지 않도록
        // 이 컴포넌트가 더 먼저 실행되어 차단 시간을 설정합니다.
        demoFlow?.SuppressGlobalConfirmInput(
            Mathf.Max(0.30f, longPressToGrabSeconds + 0.10f)
        );

        if (logInteraction)
        {
            Debug.Log("[더리 레이저] 더리 위에서 검지 핀치를 시작했습니다.");
        }
    }

    private void UpdateHeldPinch()
    {
        if (!CanReceiveInteraction())
        {
            CancelCurrentPinch();
            return;
        }

        demoFlow?.SuppressGlobalConfirmInput(0.20f);

        if (!isDragging)
        {
            if (Time.unscaledTime - pinchStartedTime < longPressToGrabSeconds)
            {
                return;
            }

            StartDragging();
        }

        UpdateDragging();
    }

    private void StartDragging()
    {
        if (durryObject == null)
        {
            CancelCurrentPinch();
            return;
        }

        isDragging = true;

        if (TryGetPointerPointOnDragPlane(out Vector3 pointerPoint))
        {
            dragOffset = durryObject.transform.position - pointerPoint;
        }
        else
        {
            dragOffset = Vector3.zero;
        }

        if (logInteraction)
        {
            Debug.Log("[더리 레이저] 길게 핀치하여 더리 이동을 시작했습니다.");
        }
    }

    private void UpdateDragging()
    {
        if (!isDragging || durryObject == null)
        {
            return;
        }

        if (!TryGetPointerPointOnDragPlane(out Vector3 pointerPoint))
        {
            return;
        }

        Vector3 targetPosition = pointerPoint + dragOffset;

        if (!lockDepthWhileDragging)
        {
            // 옵션을 끄더라도 기본 구현은 현재 레이저-평면 교차점을 사용합니다.
            // 깊이를 실제 레이저 방향으로 바꾸는 별도 입력은 제공하지 않습니다.
            targetPosition = pointerPoint + dragOffset;
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
    }

    private void EndPinch()
    {
        if (!pinchStartedOnDurry)
        {
            return;
        }

        bool completedDrag = isDragging;
        float heldSeconds = Time.unscaledTime - pinchStartedTime;

        pinchStartedOnDurry = false;
        isDragging = false;

        if (completedDrag)
        {
            if (logInteraction)
            {
                Debug.Log("[더리 레이저] 핀치를 놓아 더리 이동을 종료했습니다.");
            }

            return;
        }

        // 길게 누르기 기준 전에 놓았을 때만 1회의 귀여운 터치 반응을 실행합니다.
        if (enableShortPinchReaction && heldSeconds < longPressToGrabSeconds)
        {
            RegisterShortPinchReaction();
        }
    }

    private void RegisterShortPinchReaction()
    {
        float now = Time.unscaledTime;
        if (now - lastShortTapTime > tapSequenceResetSeconds)
        {
            shortTapCount = 0;
        }

        lastShortTapTime = now;
        shortTapCount++;

        if (shortTapCount >= Mathf.Max(2, focusedReactionThreshold))
        {
            ShowFocusedReaction();
            shortTapCount = 0;
            return;
        }

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
            Debug.Log(
                $"[더리 레이저] 짧은 핀치 반응 {shortTapCount}/" +
                $"{Mathf.Max(2, focusedReactionThreshold)}"
            );
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

        activeFocusedMessage = message;
        demoFlow.ShowDurryInteractionMessage(
            message,
            focusedExpressionState,
            clip
        );

        if (focusedMessageCoroutine != null)
        {
            StopCoroutine(focusedMessageCoroutine);
        }

        focusedMessageCoroutine = StartCoroutine(
            HideFocusedMessageAfterDelay(message)
        );

        if (logInteraction)
        {
            Debug.Log("[더리 레이저] 반복 핀치 반응: " + message);
        }
    }

    private IEnumerator HideFocusedMessageAfterDelay(string expectedMessage)
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0.5f, focusedMessageDuration)
        );

        focusedMessageCoroutine = null;

        bool sameMessageIsVisible =
            activeFocusedMessage == expectedMessage &&
            demoFlow != null &&
            demoFlow.speechText != null &&
            demoFlow.speechText.text == expectedMessage;

        activeFocusedMessage = string.Empty;

        if (sameMessageIsVisible && demoFlow.IsDialoguePanelVisible())
        {
            demoFlow.StopDurryVoicePlayback();
            demoFlow.HideDialoguePanels();
        }
    }

    private void BuildDragPlane(RaycastHit hit)
    {
        Vector3 planeNormal;

        if (demoFlow != null && demoFlow.centerEyeAnchor != null)
        {
            planeNormal = durryObject.transform.position -
                          demoFlow.centerEyeAnchor.position;
        }
        else if (rayOrigin != null)
        {
            planeNormal = durryObject.transform.position - rayOrigin.position;
        }
        else
        {
            planeNormal = Vector3.forward;
        }

        if (planeNormal.sqrMagnitude < 0.0001f)
        {
            planeNormal = Vector3.forward;
        }

        // 잡기 시작한 더리의 깊이에 고정된, 사용자 화면과 평행한 평면입니다.
        dragPlane = new Plane(
            planeNormal.normalized,
            durryObject.transform.position
        );

        Ray pointerRay = GetPointerRay();
        if (dragPlane.Raycast(pointerRay, out float enter))
        {
            dragOffset = durryObject.transform.position - pointerRay.GetPoint(enter);
        }
        else
        {
            dragOffset = durryObject.transform.position - hit.point;
        }
    }

    private bool TryGetPointerPointOnDragPlane(out Vector3 point)
    {
        point = fixedWorldPositionAtGrab;

        if (rayOrigin == null)
        {
            return false;
        }

        Ray pointerRay = GetPointerRay();
        if (!dragPlane.Raycast(pointerRay, out float enter) || enter < 0f)
        {
            return false;
        }

        point = pointerRay.GetPoint(enter);
        return true;
    }

    private Ray GetPointerRay()
    {
        Vector3 localDirection = rayLocalDirection.sqrMagnitude > 0.0001f
            ? rayLocalDirection.normalized
            : Vector3.forward;

        Vector3 worldDirection = rayOrigin != null
            ? rayOrigin.TransformDirection(localDirection).normalized
            : Vector3.forward;

        Vector3 origin = rayOrigin != null
            ? rayOrigin.position
            : Vector3.zero;

        return new Ray(origin, worldDirection);
    }

    private bool TryGetDurryHit(out RaycastHit hit)
    {
        hit = default;

        ResolveReferences();
        if (rayOrigin == null || durryObject == null)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            GetPointerRay(),
            Mathf.Max(0.1f, rayDistance),
            raycastLayerMask,
            QueryTriggerInteraction.Collide
        );

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit candidate in hits)
        {
            if (candidate.collider == null)
            {
                continue;
            }

            Transform candidateTransform = candidate.collider.transform;

            // 오른손 리그 자체 Collider는 레이저 시작점 바로 앞에서 맞을 수 있어 무시합니다.
            if (rightHand != null &&
                (candidateTransform == rightHand.transform ||
                 candidateTransform.IsChildOf(rightHand.transform)))
            {
                continue;
            }

            if (candidateTransform == durryObject.transform ||
                candidateTransform.IsChildOf(durryObject.transform))
            {
                hit = candidate;
                return true;
            }

            // 더리 앞에 다른 Collider가 있으면 가려진 것으로 처리합니다.
            return false;
        }

        return false;
    }

    private bool CanReceiveInteraction()
    {
        return enabled &&
               gameObject.activeInHierarchy &&
               durryObject != null &&
               durryObject.activeInHierarchy &&
               (demoFlow == null || demoFlow.CanReceiveFreeDurryInteraction());
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

                string combinedName = hand.name.ToLowerInvariant() + " " +
                                      (hand.transform.parent != null
                                          ? hand.transform.parent.name.ToLowerInvariant()
                                          : string.Empty);

                if (combinedName.Contains("right"))
                {
                    rightHand = hand;
                    break;
                }
            }
        }

        if (rayOrigin == null && rightHand != null)
        {
            Transform root = rightHand.transform;
            rayOrigin = FindChildContains(root, "rayorigin") ??
                        FindChildContains(root, "ray origin") ??
                        FindChildContains(root, "pointerpose") ??
                        FindChildContains(root, "pointer pose") ??
                        FindChildContains(root, "aim") ??
                        FindChildContains(root, "ray");

            // 자동 탐색이 실패하면 오른손 Transform을 사용합니다.
            // 실제 레이저 방향과 다르면 Inspector에서 Ray Origin을 직접 연결하세요.
            if (rayOrigin == null)
            {
                rayOrigin = root;
            }
        }
    }

    private void SetupDurryCollider()
    {
        ResolveReferences();

        if (durryObject == null)
        {
            return;
        }

        if (durryInteractionCollider == null)
        {
            Collider[] colliders = durryObject.GetComponentsInChildren<Collider>(true);
            foreach (Collider candidate in colliders)
            {
                if (candidate != null && candidate.enabled)
                {
                    durryInteractionCollider = candidate;
                    break;
                }
            }
        }

        if (durryInteractionCollider != null || !autoCreateDurryCollider)
        {
            return;
        }

        Renderer[] renderers = durryObject.GetComponentsInChildren<Renderer>(true);
        bool foundBounds = false;
        Bounds worldBounds = new Bounds(
            durryObject.transform.position,
            Vector3.one * 0.25f
        );

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!foundBounds)
            {
                worldBounds = renderer.bounds;
                foundBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
        }

        BoxCollider boxCollider = durryObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;

        Vector3 lossyScale = durryObject.transform.lossyScale;
        Vector3 safeScale = new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z))
        );

        boxCollider.center = durryObject.transform.InverseTransformPoint(worldBounds.center);
        boxCollider.size = new Vector3(
            worldBounds.size.x / safeScale.x,
            worldBounds.size.y / safeScale.y,
            worldBounds.size.z / safeScale.z
        );

        durryInteractionCollider = boxCollider;

        if (logInteraction)
        {
            Debug.Log("[더리 레이저] 감지용 BoxCollider를 자동 생성했습니다.");
        }
    }

    private void CancelCurrentPinch()
    {
        pinchStartedOnDurry = false;
        isDragging = false;
    }

    private static Transform FindChildContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string normalizedKeyword = keyword.Replace(" ", string.Empty).ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == null)
            {
                continue;
            }

            string normalizedName = child.name.Replace(" ", string.Empty).ToLowerInvariant();
            if (normalizedName.Contains(normalizedKeyword))
            {
                return child;
            }
        }

        return null;
    }
}
