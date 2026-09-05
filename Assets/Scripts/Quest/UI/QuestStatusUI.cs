using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Displays every detected quest in one vertical Scroll View.
/// The viewport is sized to show three large cards at once, while the content
/// grows to contain the complete AI-detected quest list.
/// </summary>
public class QuestStatusUI : MonoBehaviour
{
    [Header("< 기존 요약 UI 연결 >")]
    [FormerlySerializedAs("totalClearedQuestText")]
    [SerializeField] private TextMeshProUGUI missionProgressText;

    [FormerlySerializedAs("cleanlinessScoreText")]
    [SerializeField] private TextMeshProUGUI myPageCleanlinessText;

    [SerializeField] private TextMeshProUGUI creditText;
    [SerializeField] private TextMeshProUGUI continuousCleanDaysText;
    [SerializeField] private TextMeshProUGUI tidinessScoreText;

    [Header("< 전체 미션 세로 스크롤 >")]
    [Tooltip("NotePage 안의 Scroll View 오브젝트에 붙은 ScrollRect입니다.")]
    [SerializeField] private ScrollRect missionScrollRect;

    [Tooltip("Scroll View의 Viewport RectTransform입니다. 비워두면 자동 탐색합니다.")]
    [SerializeField] private RectTransform missionViewport;

    [Tooltip("Scroll View/Viewport/Content의 Content RectTransform입니다.")]
    [SerializeField] private RectTransform missionContent;

    [Tooltip("Content 아래에 둔 비활성 MissionCardTemplate입니다.")]
    [SerializeField] private MissionCardView missionCardTemplate;

    [Tooltip("미션이 없을 때 표시할 안내 오브젝트입니다. 필요 없으면 비워도 됩니다.")]
    [SerializeField] private GameObject emptyStateObject;

    [SerializeField] private TextMeshProUGUI emptyStateText;
    [SerializeField] private string emptyMissionMessage = "오늘의 미션을 시작하면 정리할 물건이 표시돼요.";

    [Header("< 한 화면에 큰 카드 3개 표시 >")]
    [SerializeField, Min(1)] private int visibleCardCount = 3;
    [SerializeField, Min(1f)] private float missionCardHeight = 180f;
    [SerializeField, Min(0f)] private float cardSpacing = 18f;
    [SerializeField, Min(0)] private int contentPaddingTop = 0;
    [SerializeField, Min(0)] private int contentPaddingBottom = 0;
    [SerializeField, Min(0)] private int contentPaddingLeft = 0;
    [SerializeField, Min(0)] private int contentPaddingRight = 0;

    [Tooltip("켜면 Scroll View 루트 높이를 카드 3개가 정확히 보이도록 자동 계산합니다.")]
    [SerializeField] private bool autoResizeScrollAreaForVisibleCards = false;

    [Tooltip("현재 Inspector에서 맞춰 둔 Scroll View와 Viewport 크기를 코드가 변경하지 않도록 합니다.")]
    [SerializeField] private bool preserveInspectorScrollAreaSize = true;

    [Header("< Viewport 잘라내기 >")]
    [Tooltip("Viewport에 RectMask2D를 강제로 적용하여 영역 밖 미션 카드를 숨깁니다.")]
    [SerializeField] private bool forceViewportRectMask = true;

    [Tooltip("기존 Mask와 RectMask2D가 동시에 동작하지 않도록 일반 Mask를 비활성화합니다.")]
    [SerializeField] private bool disableLegacyViewportMask = true;

    [Tooltip("동적으로 생성된 카드의 TMP/Image가 Viewport 마스크를 따르도록 강제합니다.")]
    [SerializeField] private bool forceMissionGraphicsMaskable = true;

    [Tooltip("켜면 Content에 VerticalLayoutGroup과 ContentSizeFitter를 자동 구성합니다.")]
    [SerializeField] private bool autoConfigureContentLayout = true;

    [SerializeField] private bool resetScrollToTopWhenQuestListChanges = true;

    [Header("< 런타임 확인 >")]
    [SerializeField, Min(0)] private int spawnedCardCount;
    [SerializeField, Min(0)] private int currentQuestCount;

    private readonly List<MissionCardView> spawnedCards = new List<MissionCardView>();
    private bool missionInteractionEnabled = true;
    private string lastQuestListSignature = string.Empty;
    private Coroutine resetScrollCoroutine;
    private bool missingScrollReferencesLogged;

    private void Awake()
    {
        SanitizeSummaryTextReferences();
        ResolveScrollReferences();
        ConfigureScrollLayout();

        if (missionCardTemplate != null)
        {
            missionCardTemplate.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        QuestProgressManager.OnQuestUpdated += UpdateUIWindow;
        QuestProgressManager.OnQuestListChanged += HandleQuestListChanged;
        CreditManager.OnCreditChanged += UpdateCreditOnly;
        CleanlinessManager.OnStateChanged += UpdateCleanlinessOnly;
    }

    private void OnDisable()
    {
        QuestProgressManager.OnQuestUpdated -= UpdateUIWindow;
        QuestProgressManager.OnQuestListChanged -= HandleQuestListChanged;
        CreditManager.OnCreditChanged -= UpdateCreditOnly;
        CleanlinessManager.OnStateChanged -= UpdateCleanlinessOnly;
    }

    private void Start()
    {
        RefreshAllUI();
        HandleQuestListChanged();
    }

    private void OnValidate()
    {
        SanitizeSummaryTextReferences();
        visibleCardCount = Mathf.Max(1, visibleCardCount);
        missionCardHeight = Mathf.Max(1f, missionCardHeight);
        cardSpacing = Mathf.Max(0f, cardSpacing);

        if (!Application.isPlaying)
        {
            ResolveScrollReferences();
            ConfigureScrollLayout();
        }
    }

    /// <summary>
    /// Prevents the common Inspector mistake where every status field is connected
    /// to WorldCanvas/Description/DescriptionText. That would overwrite scan messages.
    /// </summary>
    private void SanitizeSummaryTextReferences()
    {
        missionProgressText = RejectDialogueText(missionProgressText, nameof(missionProgressText));
        myPageCleanlinessText = RejectDialogueText(
            myPageCleanlinessText,
            nameof(myPageCleanlinessText)
        );
        creditText = RejectDialogueText(creditText, nameof(creditText));
        continuousCleanDaysText = RejectDialogueText(
            continuousCleanDaysText,
            nameof(continuousCleanDaysText)
        );
        tidinessScoreText = RejectDialogueText(tidinessScoreText, nameof(tidinessScoreText));
    }

    private static TextMeshProUGUI RejectDialogueText(
        TextMeshProUGUI candidate,
        string fieldName)
    {
        if (candidate == null)
        {
            return null;
        }

        string path = GetHierarchyPath(candidate.transform).ToLowerInvariant();
        bool isDialogueText =
            candidate.name.ToLowerInvariant().Contains("description") ||
            path.Contains("/description/") ||
            path.Contains("speechbubble");

        if (!isDialogueText)
        {
            return candidate;
        }

        Debug.LogWarning(
            $"[QuestStatusUI] {fieldName}에 '{candidate.name}'이 연결되어 있어 해제했습니다. " +
            "DescriptionText를 상태 UI에 연결하면 '책상 스캔 중' 문구가 덮어써집니다."
        );
        return null;
    }

    public void SetMissionInteractionEnabled(bool enabled)
    {
        missionInteractionEnabled = enabled;
        RefreshMissionList(false);
    }

    public void RefreshAllUI()
    {
        int cleared = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.CompletedRoundsToday
            : 0;

        int total = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.RequiredCleaningRoundsPerDay
            : 3;

        int cleanliness = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 0;

        int credit = CreditManager.Instance != null
            ? CreditManager.Instance.CurrentCredit
            : 0;

        int days = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.ContinuousCleanDays
            : 0;

        int tidiness = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.CurrentTidinessScore
            : 0;

        UpdateUIWindow(cleared, total, cleanliness, credit, days, tidiness);
    }

    public void UpdateUIWindow(int cleared, int total, int score, int credit, int days, int tidiness)
    {
        if (missionProgressText != null)
        {
            int detectedCount = QuestProgressManager.Instance != null &&
                                QuestProgressManager.Instance.currentQuests != null
                ? QuestProgressManager.Instance.currentQuests.Count
                : 0;

            int roundCleared = QuestProgressManager.Instance != null
                ? QuestProgressManager.Instance.CurrentRoundClearedObjectCount
                : 0;
            int roundRequired = QuestProgressManager.Instance != null
                ? QuestProgressManager.Instance.CurrentRoundRequiredObjectCount
                : 3;

            string currentRoundLine = detectedCount > 0
                ? $"이번 미션: {roundCleared} / {roundRequired}개"
                : "이번 미션: 스캔 전";

            missionProgressText.text =
                $"오늘의 미션: {cleared} / {total}회\n" +
                currentRoundLine + "\n" +
                $"인식된 정리 대상: {detectedCount}개";
        }

        if (myPageCleanlinessText != null)
        {
            string stateName = CleanlinessManager.Instance != null
                ? CleanlinessManager.Instance.GetBosongStateName(score)
                : string.Empty;

            myPageCleanlinessText.text = $"현재 보송력: {score} / 4\n{stateName}";
        }

        if (creditText != null)
        {
            creditText.text = $"{credit} CR";
        }

        if (continuousCleanDaysText != null)
        {
            continuousCleanDaysText.text = days > 0
                ? $"연속 청소 {days}일 차"
                : "오늘부터 연속 청소 도전!";
        }

        if (tidinessScoreText != null)
        {
            tidinessScoreText.text = $"책상 정돈도: {tidiness}점";
        }
    }

    private void HandleQuestListChanged()
    {
        List<QuestData> quests = GetCurrentQuests();
        string signature = BuildQuestListSignature(quests);
        bool listIdentityChanged = signature != lastQuestListSignature;
        lastQuestListSignature = signature;

        RefreshMissionList(listIdentityChanged && resetScrollToTopWhenQuestListChanges);
        RefreshAllUI();
    }

    /// <summary>
    /// Compatibility alias for older controllers or Inspector events.
    /// </summary>
    public void RefreshMissionSlots()
    {
        RefreshMissionList(false);
    }

    public void RefreshMissionList(bool resetScrollToTop)
    {
        RebindSceneUiReferences();
        ResolveScrollReferences();
        ConfigureScrollLayout();

        if (missionScrollRect != null)
        {
            missionScrollRect.gameObject.SetActive(true);
        }

        if (missionScrollRect != null && missionScrollRect.viewport != null)
        {
            missionScrollRect.viewport.gameObject.SetActive(true);
        }

        if (missionContent != null)
        {
            missionContent.gameObject.SetActive(true);
        }

        List<QuestData> quests = GetCurrentQuests();
        currentQuestCount = quests.Count;

        // 오늘의 보상 3회를 모두 받았더라도 추가 미션 카드는 계속 체크할 수 있어야 합니다.
        bool roundCompleted = QuestProgressManager.Instance != null &&
                              QuestProgressManager.Instance.IsCurrentRoundCompleted;

        EnsureCardPoolSize(quests.Count);

        for (int index = 0; index < spawnedCards.Count; index++)
        {
            MissionCardView card = spawnedCards[index];
            if (card == null)
            {
                continue;
            }

            if (index >= quests.Count)
            {
                card.Unbind();
                continue;
            }

            QuestData quest = quests[index];
            card.SetPreferredHeight(missionCardHeight);
            card.gameObject.SetActive(true);
            card.Bind(
                quest,
                BuildMissionLabel(quest, index + 1),
                missionInteractionEnabled &&
                !roundCompleted,
                HandleMissionCheckRequested
            );
        }

        EnforceMissionCardMasking();

        bool hasQuests = quests.Count > 0;
        if (emptyStateObject != null)
        {
            emptyStateObject.SetActive(!hasQuests);
        }

        if (emptyStateText != null)
        {
            bool rewardLimitReached = QuestProgressManager.Instance != null &&
                                      QuestProgressManager.Instance.HasReachedDailyRewardLimit;

            emptyStateText.text = rewardLimitReached
                ? "오늘의 3회 보상은 모두 완료했어요.\nNOTE를 눌러 보상 없는 추가 미션을 시작할 수 있어요."
                : emptyMissionMessage;
        }

        RebuildScrollLayout();

        if (resetScrollToTop)
        {
            RequestScrollResetToTop();
        }
    }

    /// <summary>
    /// DontDestroyOnLoad QuestStatusUI가 메인 씬 재로드 후에도
    /// 파괴된 NotePage Scroll/카드를 붙잡지 않도록 씬 UI를 다시 붙입니다.
    /// </summary>
    public void RebindSceneUiReferences()
    {
        bool scrollAlive = missionScrollRect != null;
        bool contentAlive = missionContent != null;
        bool templateAlive = missionCardTemplate != null;

        bool poolNeedsReset = false;
        for (int i = 0; i < spawnedCards.Count; i++)
        {
            if (spawnedCards[i] == null)
            {
                poolNeedsReset = true;
                break;
            }

            if (missionContent != null &&
                spawnedCards[i].transform.parent != missionContent)
            {
                poolNeedsReset = true;
                break;
            }
        }

        if (!scrollAlive)
        {
            missionScrollRect = null;
            missionViewport = null;
        }

        if (!contentAlive)
        {
            missionContent = null;
        }

        if (!templateAlive)
        {
            missionCardTemplate = null;
        }

        if (poolNeedsReset || !contentAlive || !templateAlive)
        {
            ClearSpawnedCardPool();
        }

        ResolveScrollReferences();
        missingScrollReferencesLogged = false;
    }

    private void ClearSpawnedCardPool()
    {
        for (int i = 0; i < spawnedCards.Count; i++)
        {
            MissionCardView card = spawnedCards[i];
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        spawnedCards.Clear();
        spawnedCardCount = 0;
    }

    private void EnsureCardPoolSize(int requiredCount)
    {
        ResolveScrollReferences();

        // 파괴된 카드 슬롯을 제거해 Count가 실제 유효 카드 수와 맞게 합니다.
        for (int i = spawnedCards.Count - 1; i >= 0; i--)
        {
            if (spawnedCards[i] == null)
            {
                spawnedCards.RemoveAt(i);
            }
        }

        if (missionCardTemplate == null || missionContent == null)
        {
            if (requiredCount > 0 && !missingScrollReferencesLogged)
            {
                missingScrollReferencesLogged = true;
                Debug.LogError(
                    "[QuestStatusUI] AI 미션 데이터는 생성되었지만 Scroll View 연결이 없습니다. " +
                    "Mission Scroll Rect, Mission Content, Mission Card Template을 연결하세요."
                );
            }
            return;
        }

        missingScrollReferencesLogged = false;

        while (spawnedCards.Count < requiredCount)
        {
            MissionCardView card = Instantiate(missionCardTemplate, missionContent, false);
            card.name = $"MissionCard_{spawnedCards.Count + 1:00}";
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one;
            card.transform.SetAsLastSibling();
            card.SetPreferredHeight(missionCardHeight);
            card.gameObject.SetActive(true);
            spawnedCards.Add(card);
        }

        spawnedCardCount = spawnedCards.Count;
    }

    private void HandleMissionCheckRequested(string questId)
    {
        if (!missionInteractionEnabled ||
            QuestProgressManager.Instance == null ||
            string.IsNullOrWhiteSpace(questId))
        {
            return;
        }

        QuestProgressManager.Instance.ClearQuest(questId);
    }

    private void ResolveScrollReferences()
    {
        // Unity fake-null: 파괴된 참조는 == null 이지만 필드에는 남아 있을 수 있어
        // 명시적으로 비운 뒤 새 씬에서 다시 찾습니다.
        if (missionScrollRect == null)
        {
            missionScrollRect = null;
        }

        if (missionContent == null)
        {
            missionContent = null;
        }

        if (missionViewport == null)
        {
            missionViewport = null;
        }

        if (missionCardTemplate == null)
        {
            missionCardTemplate = null;
        }

        if (missionViewport == null && missionScrollRect != null)
        {
            missionViewport = missionScrollRect.viewport;
        }

        if (missionContent == null && missionScrollRect != null)
        {
            missionContent = missionScrollRect.content;
        }

        if (missionScrollRect == null && missionContent != null)
        {
            missionScrollRect = missionContent.GetComponentInParent<ScrollRect>(true);
        }

        if (missionScrollRect == null)
        {
            ScrollRect[] scrollRects = FindObjectsByType<ScrollRect>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            int bestScore = int.MinValue;
            foreach (ScrollRect candidate in scrollRects)
            {
                if (candidate == null)
                {
                    continue;
                }

                string fullPath = GetHierarchyPath(candidate.transform).ToLowerInvariant();
                int score = 0;

                if (candidate.name.Equals("MissionScrollView", System.StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }

                if (fullPath.Contains("notepage")) score += 30;
                if (fullPath.Contains("mission")) score += 25;
                if (fullPath.Contains("quest")) score += 15;
                if (fullPath.Contains("scroll")) score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    missionScrollRect = candidate;
                }
            }
        }

        if (missionContent == null && missionScrollRect != null)
        {
            missionContent = missionScrollRect.content;

            if (missionContent == null)
            {
                Transform contentTransform = FindChildExact(
                    missionScrollRect.transform,
                    "Content"
                );
                if (contentTransform != null)
                {
                    missionContent = contentTransform as RectTransform;
                    missionScrollRect.content = missionContent;
                }
            }
        }

        if (missionScrollRect != null && missionScrollRect.viewport == null)
        {
            Transform viewportTransform = FindChildExact(
                missionScrollRect.transform,
                "Viewport"
            );
            if (viewportTransform != null)
            {
                missionScrollRect.viewport = viewportTransform as RectTransform;
            }
        }

        if (missionViewport == null && missionScrollRect != null)
        {
            missionViewport = missionScrollRect.viewport;
        }

        if (missionScrollRect != null && missionViewport != null)
        {
            missionScrollRect.viewport = missionViewport;
        }

        if (missionCardTemplate == null && missionContent != null)
        {
            Transform exactTemplate = FindChildExact(
                missionContent,
                "MissionCardTemplate"
            );

            missionCardTemplate = exactTemplate != null
                ? exactTemplate.GetComponent<MissionCardView>()
                : missionContent.GetComponentInChildren<MissionCardView>(true);

            if (missionCardTemplate == null && Application.isPlaying)
            {
                for (int index = 0; index < missionContent.childCount; index++)
                {
                    Transform child = missionContent.GetChild(index);
                    if (child == null)
                    {
                        continue;
                    }

                    string childName = child.name.ToLowerInvariant();
                    bool looksLikeMissionCard =
                        childName.Contains("mission") || childName.Contains("quest");

                    if (!looksLikeMissionCard ||
                        child.GetComponentInChildren<Button>(true) == null ||
                        child.GetComponentInChildren<TextMeshProUGUI>(true) == null)
                    {
                        continue;
                    }

                    missionCardTemplate = child.gameObject.AddComponent<MissionCardView>();
                    Debug.Log(
                        $"[QuestStatusUI] '{child.name}'을 MissionCardTemplate로 자동 연결했습니다."
                    );
                    break;
                }
            }
        }
    }

    private static Transform FindChildExact(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null &&
                string.Equals(
                    child.name,
                    exactName,
                    System.StringComparison.OrdinalIgnoreCase
                ))
            {
                return child;
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(target.name);
        Transform parent = target.parent;

        while (parent != null)
        {
            builder.Insert(0, parent.name + "/");
            parent = parent.parent;
        }

        return builder.ToString();
    }

    private void ConfigureScrollLayout()
    {
        if (missionScrollRect != null)
        {
            missionScrollRect.horizontal = false;
            missionScrollRect.vertical = true;
            missionScrollRect.movementType = ScrollRect.MovementType.Clamped;

            if (missionContent != null)
            {
                missionScrollRect.content = missionContent;
            }
        }

        ConfigureViewportClipping();

        if (missionContent != null)
        {
            missionContent.anchorMin = new Vector2(0f, 1f);
            missionContent.anchorMax = new Vector2(1f, 1f);
            missionContent.pivot = new Vector2(0.5f, 1f);

            Vector2 anchoredPosition = missionContent.anchoredPosition;
            anchoredPosition.y = 0f;
            missionContent.anchoredPosition = anchoredPosition;
        }

        if (missionContent != null && autoConfigureContentLayout)
        {
            VerticalLayoutGroup layout = missionContent.GetComponent<VerticalLayoutGroup>();
            if (layout == null && Application.isPlaying)
            {
                layout = missionContent.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            if (layout != null)
            {
                layout.padding = new RectOffset(
                    contentPaddingLeft,
                    contentPaddingRight,
                    contentPaddingTop,
                    contentPaddingBottom
                );
                layout.spacing = cardSpacing;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            ContentSizeFitter fitter = missionContent.GetComponent<ContentSizeFitter>();
            if (fitter == null && Application.isPlaying)
            {
                fitter = missionContent.gameObject.AddComponent<ContentSizeFitter>();
            }

            if (fitter != null)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        if (missionScrollRect != null &&
            autoResizeScrollAreaForVisibleCards &&
            !preserveInspectorScrollAreaSize)
        {
            RectTransform scrollAreaRect = missionScrollRect.GetComponent<RectTransform>();
            if (scrollAreaRect != null)
            {
                float visibleHeight =
                    visibleCardCount * missionCardHeight +
                    Mathf.Max(0, visibleCardCount - 1) * cardSpacing +
                    contentPaddingTop +
                    contentPaddingBottom;

                scrollAreaRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    visibleHeight
                );
            }
        }
    }

    private void ConfigureViewportClipping()
    {
        if (missionScrollRect == null)
        {
            return;
        }

        if (missionViewport == null)
        {
            missionViewport = missionScrollRect.viewport;
        }

        if (missionViewport == null)
        {
            Transform viewportTransform = FindChildExact(missionScrollRect.transform, "Viewport");
            missionViewport = viewportTransform as RectTransform;
        }

        if (missionViewport == null)
        {
            return;
        }

        missionScrollRect.viewport = missionViewport;

        Mask legacyMask = missionViewport.GetComponent<Mask>();
        if (legacyMask != null && disableLegacyViewportMask)
        {
            legacyMask.enabled = false;
        }

        if (forceViewportRectMask)
        {
            RectMask2D rectMask = missionViewport.GetComponent<RectMask2D>();
            if (rectMask == null && Application.isPlaying)
            {
                rectMask = missionViewport.gameObject.AddComponent<RectMask2D>();
            }

            if (rectMask != null)
            {
                rectMask.enabled = true;
                rectMask.padding = Vector4.zero;
            }
        }
    }

    private void EnforceMissionCardMasking()
    {
        if (!forceMissionGraphicsMaskable || missionContent == null)
        {
            return;
        }

        MaskableGraphic[] graphics = missionContent.GetComponentsInChildren<MaskableGraphic>(true);
        foreach (MaskableGraphic graphic in graphics)
        {
            if (graphic != null)
            {
                graphic.maskable = true;
            }
        }

        // Override Sorting Canvas가 카드 안에 있으면 부모 Viewport 마스크를 벗어날 수 있습니다.
        // 미션 카드에 붙은 중첩 Canvas만 정렬 오버라이드를 해제합니다.
        Canvas[] nestedCanvases = missionContent.GetComponentsInChildren<Canvas>(true);
        foreach (Canvas nestedCanvas in nestedCanvases)
        {
            if (nestedCanvas == null || nestedCanvas.transform == missionScrollRect.transform)
            {
                continue;
            }

            nestedCanvas.overrideSorting = false;
        }
    }

    private void RebuildScrollLayout()
    {
        if (missionContent == null)
        {
            return;
        }

        ConfigureViewportClipping();
        EnforceMissionCardMasking();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(missionContent);
        Canvas.ForceUpdateCanvases();
    }

    private void RequestScrollResetToTop()
    {
        if (resetScrollCoroutine != null)
        {
            StopCoroutine(resetScrollCoroutine);
        }

        resetScrollCoroutine = StartCoroutine(ResetScrollToTopNextFrame());
    }

    private IEnumerator ResetScrollToTopNextFrame()
    {
        yield return null;
        RebuildScrollLayout();

        if (missionScrollRect != null)
        {
            missionScrollRect.StopMovement();
            missionScrollRect.verticalNormalizedPosition = 1f;
        }

        resetScrollCoroutine = null;
    }

    private static List<QuestData> GetCurrentQuests()
    {
        if (QuestProgressManager.Instance == null ||
            QuestProgressManager.Instance.currentQuests == null)
        {
            return new List<QuestData>();
        }

        return QuestProgressManager.Instance.currentQuests;
    }

    private static string BuildQuestListSignature(List<QuestData> quests)
    {
        if (quests == null || quests.Count == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int index = 0; index < quests.Count; index++)
        {
            QuestData quest = quests[index];
            builder.Append(quest != null ? quest.questId : "null");
            builder.Append('|');
        }

        return builder.ToString();
    }

    private void UpdateCreditOnly(int newCredit)
    {
        if (creditText != null)
        {
            creditText.text = $"{newCredit} CR";
        }
    }

    private void UpdateCleanlinessOnly(int newScore)
    {
        if (myPageCleanlinessText == null)
        {
            return;
        }

        string stateName = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.GetBosongStateName(newScore)
            : string.Empty;

        myPageCleanlinessText.text = $"현재 보송력: {newScore} / 4\n{stateName}";
    }

    private static string BuildMissionLabel(QuestData quest, int displayNumber)
    {
        string objectName = GetKoreanQuestName(quest != null ? quest.questType : string.Empty);
        string actionName = GetKoreanActionName(quest != null ? quest.suggestedAction : string.Empty);
        string label = string.IsNullOrWhiteSpace(actionName)
            ? $"{objectName} 정리하기"
            : $"{objectName} {actionName}";

        string numberedLabel = $"{displayNumber}. {label}";
        return quest != null && quest.isCleared
            ? $"<s>{numberedLabel}</s>"
            : numberedLabel;
    }

    private static string GetKoreanQuestName(string questType)
    {
        switch ((questType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "cable":
            case "cable_charger": return "케이블·충전기";
            case "cup":
            case "cup_bottle": return "컵·물병";
            case "laptop": return "노트북";
            case "paper":
            case "book":
            case "paper_book": return "종이·책";
            case "small_device": return "소형 전자기기";
            case "toy_decor": return "장식품·피규어";
            case "pen":
            case "writing_tool": return "필기구";
            case "trash": return "쓰레기";
            case "cosmetic":
            case "cosmetics": return "화장품";
            case "clothes":
            case "clothing": return "옷";
            case "unknown": return "물건";
            default: return string.IsNullOrWhiteSpace(questType) ? "물건" : questType;
        }
    }

    private static string GetKoreanActionName(string suggestedAction)
    {
        switch ((suggestedAction ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "discard":
            case "discard_candidate": return "버리기";
            case "organize":
            case "organize_candidate": return "정리하기";
            case "move":
            case "move_candidate": return "제자리로 옮기기";
            default: return string.Empty;
        }
    }
}
