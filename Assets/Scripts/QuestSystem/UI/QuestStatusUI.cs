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

    [Tooltip("Scroll View/Viewport/Content의 Content RectTransform입니다.")]
    [SerializeField] private RectTransform missionContent;

    [Tooltip("Content 아래에 둔 비활성 MissionCardTemplate입니다.")]
    [SerializeField] private MissionCardView missionCardTemplate;

    [Tooltip("미션이 없을 때 표시할 안내 오브젝트입니다. 필요 없으면 비워도 됩니다.")]
    [SerializeField] private GameObject emptyStateObject;

    [SerializeField] private TextMeshProUGUI emptyStateText;
    [SerializeField] private string emptyMissionMessage = "책상 스캔 후 미션이 표시돼요.";

    [Header("< 한 화면에 큰 카드 3개 표시 >")]
    [SerializeField, Min(1)] private int visibleCardCount = 3;
    [SerializeField, Min(1f)] private float missionCardHeight = 180f;
    [SerializeField, Min(0f)] private float cardSpacing = 18f;
    [SerializeField, Min(0)] private int contentPaddingTop = 0;
    [SerializeField, Min(0)] private int contentPaddingBottom = 0;
    [SerializeField, Min(0)] private int contentPaddingLeft = 0;
    [SerializeField, Min(0)] private int contentPaddingRight = 0;

    [Tooltip("켜면 Scroll View 루트 높이를 카드 3개가 정확히 보이도록 자동 계산합니다.")]
    [SerializeField] private bool autoResizeScrollAreaForVisibleCards = true;

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

    private void Awake()
    {
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
        visibleCardCount = Mathf.Max(1, visibleCardCount);
        missionCardHeight = Mathf.Max(1f, missionCardHeight);
        cardSpacing = Mathf.Max(0f, cardSpacing);

        if (!Application.isPlaying)
        {
            ResolveScrollReferences();
            ConfigureScrollLayout();
        }
    }

    public void SetMissionInteractionEnabled(bool enabled)
    {
        missionInteractionEnabled = enabled;
        RefreshMissionList(false);
    }

    public void RefreshAllUI()
    {
        int cleared = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.CompletedQuestsToday
            : 0;

        int total = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.RequiredQuestsForToday
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

            missionProgressText.text =
                $"퀘스트 진행도: {cleared} / {total}\n인식된 정리 대상: {detectedCount}개";
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
        ResolveScrollReferences();
        ConfigureScrollLayout();

        List<QuestData> quests = GetCurrentQuests();
        currentQuestCount = quests.Count;

        bool roundCompleted = QuestProgressManager.Instance != null &&
                              QuestProgressManager.Instance.IsTodayMissionCompleted;

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
                missionInteractionEnabled && !roundCompleted,
                HandleMissionCheckRequested
            );
        }

        bool hasQuests = quests.Count > 0;
        if (emptyStateObject != null)
        {
            emptyStateObject.SetActive(!hasQuests);
        }

        if (emptyStateText != null)
        {
            emptyStateText.text = emptyMissionMessage;
        }

        RebuildScrollLayout();

        if (resetScrollToTop)
        {
            RequestScrollResetToTop();
        }
    }

    private void EnsureCardPoolSize(int requiredCount)
    {
        if (missionCardTemplate == null || missionContent == null)
        {
            if (requiredCount > 0)
            {
                Debug.LogError("[QuestStatusUI] MissionCardTemplate 또는 MissionContent가 연결되지 않았습니다.");
            }
            return;
        }

        while (spawnedCards.Count < requiredCount)
        {
            MissionCardView card = Instantiate(missionCardTemplate, missionContent);
            card.name = $"MissionCard_{spawnedCards.Count + 1:00}";
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
        if (missionContent == null && missionScrollRect != null)
        {
            missionContent = missionScrollRect.content;
        }

        if (missionScrollRect == null && missionContent != null)
        {
            missionScrollRect = missionContent.GetComponentInParent<ScrollRect>(true);
        }
    }

    private void ConfigureScrollLayout()
    {
        if (missionScrollRect != null)
        {
            missionScrollRect.horizontal = false;
            missionScrollRect.vertical = true;
            missionScrollRect.movementType = ScrollRect.MovementType.Clamped;
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

        if (missionScrollRect != null && autoResizeScrollAreaForVisibleCards)
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

    private void RebuildScrollLayout()
    {
        if (missionContent == null)
        {
            return;
        }

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
