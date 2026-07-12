using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Binds manager data to the existing Note/MyPage text objects.
/// The transient speech/mission guidance text is owned by DustinyDemoFlow.descriptionText.
/// </summary>
public class QuestStatusUI : MonoBehaviour
{
    [Serializable]
    public class MissionSlot
    {
        public GameObject root;
        public TextMeshProUGUI missionText;
        public GameObject missionCheckObject;
    }

    [Header("< 기존 요약 UI 연결 >")]
    [FormerlySerializedAs("totalClearedQuestText")]
    [SerializeField] private TextMeshProUGUI missionProgressText;

    [FormerlySerializedAs("cleanlinessScoreText")]
    [SerializeField] private TextMeshProUGUI myPageCleanlinessText;

    [SerializeField] private TextMeshProUGUI creditText;
    [SerializeField] private TextMeshProUGUI continuousCleanDaysText;
    [SerializeField] private TextMeshProUGUI tidinessScoreText;

    [Header("< 더리 미션 노트의 3개 미션 카드 >")]
    [SerializeField] private MissionSlot[] missionSlots = new MissionSlot[3];
    [SerializeField] private string emptyMissionMessage = "스캔 후 미션이 표시돼요.";

    private void OnEnable()
    {
        QuestProgressManager.OnQuestUpdated += UpdateUIWindow;
        QuestProgressManager.OnQuestListChanged += RefreshMissionSlots;
        CreditManager.OnCreditChanged += UpdateCreditOnly;
        CleanlinessManager.OnStateChanged += UpdateCleanlinessOnly;
    }

    private void OnDisable()
    {
        QuestProgressManager.OnQuestUpdated -= UpdateUIWindow;
        QuestProgressManager.OnQuestListChanged -= RefreshMissionSlots;
        CreditManager.OnCreditChanged -= UpdateCreditOnly;
        CleanlinessManager.OnStateChanged -= UpdateCleanlinessOnly;
    }

    private void Start()
    {
        RefreshAllUI();
        RefreshMissionSlots();
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
            missionProgressText.text = $"오늘 완료한 미션: {cleared} / {total}개";
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

    public void RefreshMissionSlots()
    {
        if (missionSlots == null || missionSlots.Length == 0)
        {
            return;
        }

        var quests = QuestProgressManager.Instance != null
            ? QuestProgressManager.Instance.currentQuests
            : null;

        for (int index = 0; index < missionSlots.Length; index++)
        {
            MissionSlot slot = missionSlots[index];
            if (slot == null)
            {
                continue;
            }

            QuestData quest = quests != null && index < quests.Count ? quests[index] : null;
            bool hasQuest = quest != null;

            if (slot.root != null)
            {
                // Keep the first card visible as an empty-state guide.
                slot.root.SetActive(hasQuest || index == 0);
            }

            if (slot.missionText != null)
            {
                slot.missionText.text = hasQuest
                    ? BuildMissionLabel(quest)
                    : index == 0 ? emptyMissionMessage : string.Empty;
            }

            if (slot.missionCheckObject != null)
            {
                slot.missionCheckObject.SetActive(hasQuest && quest.isCleared);
            }
        }
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

    private static string BuildMissionLabel(QuestData quest)
    {
        string objectName = GetKoreanQuestName(quest.questType);
        string actionName = GetKoreanActionName(quest.suggestedAction);
        string label = string.IsNullOrWhiteSpace(actionName)
            ? $"{objectName} 정리하기"
            : $"{objectName} {actionName}";

        return quest.isCleared ? $"<s>{label}</s>" : label;
    }

    private static string GetKoreanQuestName(string questType)
    {
        switch ((questType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "trash": return "쓰레기";
            case "book": return "책";
            case "pen": return "필기구";
            case "cup": return "컵";
            case "cosmetic":
            case "cosmetics": return "화장품";
            case "cable": return "케이블";
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
