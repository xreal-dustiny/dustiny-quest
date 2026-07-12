using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns quest progress, daily completion, streaks, and reward timing.
/// Individual objects grant credit. A complete cleaning round grants cleanliness once.
/// </summary>
public class QuestProgressManager : MonoBehaviour
{
    public static QuestProgressManager Instance { get; private set; }

    public static event Action<int, int, int, int, int, int> OnQuestUpdated;
    public static event Action OnQuestListChanged;
    public static event Action OnCleaningRoundCompleted;

    private const string LastLoginDateKey = "Dustiny_LastLoginDate_V2";
    private const string CompletedTodayKey = "Dustiny_CompletedQuestsToday_V2";
    private const string TodayCompletedKey = "Dustiny_IsTodayMissionCompleted_V2";
    private const string LastCleanDateKey = "Dustiny_LastCleanDate_V2";
    private const string ContinuousDaysKey = "Dustiny_ContinuousCleanDays_V2";

    [Header("[ 실시간 미션 진행 목록 ]")]
    public List<QuestData> currentQuests = new List<QuestData>();

    [Header("[ AI 계산 책상 종합 성적표 데이터 ]")]
    [SerializeField] private int currentTidinessScore;
    [SerializeField] private bool needsCleaning;

    [Header("[ 오늘의 미션 ]")]
    [SerializeField, Min(1)] private int requiredQuestsForToday = 3;
    [SerializeField, Min(0)] private int completedQuestsToday;
    [SerializeField] private bool isTodayMissionCompleted;

    [Header("[ 보상 설정 ]")]
    [SerializeField, Min(0)] private int dailyCompletionBonusCredit = 1;
    [SerializeField, Min(0)] private int manualQuestRewardCredit = 1;
    [SerializeField, Tooltip("AI 퀘스트 수가 부족해도 약지 핀치로 남은 칸을 완료 처리합니다.")]
    private bool fillMissingQuestSlotsOnManualCompletion = true;

    [Header("[ 연속 청소 데이터 ]")]
    [SerializeField, Min(0)] private int continuousCleanDays;

    private readonly Dictionary<string, int> todayClearedCounts = new Dictionary<string, int>();

    public int CompletedQuestsToday => completedQuestsToday;
    public int RequiredQuestsForToday => requiredQuestsForToday;
    public int ContinuousCleanDays => continuousCleanDays;
    public int CurrentTidinessScore => currentTidinessScore;
    public bool NeedsCleaning => needsCleaning;
    public bool IsTodayMissionCompleted => isTodayMissionCompleted;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            LoadPersistentData();
            EnsureDailyState();
            return;
        }

        if (Instance != this)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        InvokeUIUpdate();
        OnQuestListChanged?.Invoke();
    }

    public void SetupRoomAndQuests(YOLOScanData scanData, List<QuestData> newQuests)
    {
        EnsureDailyState();
        currentQuests = newQuests ?? new List<QuestData>();

        if (scanData != null && scanData.scan_summary != null)
        {
            currentTidinessScore = scanData.scan_summary.tidiness_score;
            needsCleaning = scanData.scan_summary.needs_cleaning;
        }
        else
        {
            currentTidinessScore = 0;
            needsCleaning = currentQuests.Count > 0;
        }

        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
    }

    public bool ClearQuest(string questId)
    {
        EnsureDailyState();

        if (isTodayMissionCompleted || completedQuestsToday >= requiredQuestsForToday)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        QuestData quest = currentQuests.Find(item => item != null && item.questId == questId);
        if (quest == null || quest.isCleared)
        {
            return false;
        }

        quest.isCleared = true;
        completedQuestsToday = Mathf.Min(completedQuestsToday + 1, requiredQuestsForToday);
        UpdateTodayStatistics(quest.questType);

        if (!quest.isRewardGiven)
        {
            quest.isRewardGiven = true;
            int rewardCredit = Mathf.Max(0, quest.rewardCredit);
            if (rewardCredit > 0)
            {
                CreditManager.Instance?.AddCredit(rewardCredit);
            }
        }

        TryCompleteDailyMission();
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
        return true;
    }

    /// <summary>
    /// Used by the right-ring-pinch completion gesture.
    /// It clears available generated quests and, when enabled, fills missing demo slots.
    /// Returns true only when this call completes the cleaning round, or when the round
    /// was already completed earlier today.
    /// </summary>
    public bool CompleteCurrentCleaningRound()
    {
        EnsureDailyState();

        if (isTodayMissionCompleted)
        {
            return true;
        }

        List<QuestData> incompleteQuests = currentQuests.FindAll(
            quest => quest != null && !quest.isCleared
        );

        foreach (QuestData quest in incompleteQuests)
        {
            if (completedQuestsToday >= requiredQuestsForToday)
            {
                break;
            }

            ClearQuest(quest.questId);
        }

        if (fillMissingQuestSlotsOnManualCompletion)
        {
            while (!isTodayMissionCompleted && completedQuestsToday < requiredQuestsForToday)
            {
                RegisterManualQuestCompletion();
            }
        }

        return isTodayMissionCompleted;
    }

    public QuestData GetNextIncompleteQuest()
    {
        return currentQuests.Find(quest => quest != null && !quest.isCleared);
    }

    private void RegisterManualQuestCompletion()
    {
        if (isTodayMissionCompleted || completedQuestsToday >= requiredQuestsForToday)
        {
            return;
        }

        completedQuestsToday++;

        if (manualQuestRewardCredit > 0)
        {
            CreditManager.Instance?.AddCredit(manualQuestRewardCredit);
        }

        TryCompleteDailyMission();
        SaveDailyProgress();
        InvokeUIUpdate();
    }

    private void TryCompleteDailyMission()
    {
        if (isTodayMissionCompleted || completedQuestsToday < requiredQuestsForToday)
        {
            return;
        }

        isTodayMissionCompleted = true;
        completedQuestsToday = requiredQuestsForToday;

        if (dailyCompletionBonusCredit > 0)
        {
            CreditManager.Instance?.AddCredit(dailyCompletionBonusCredit);
        }

        CleanlinessManager.Instance?.OnCleanSuccess();
        ProcessContinuousCleanCheck();
        SaveDailyProgress();

        Debug.Log("[오늘의 미션 완료] 보송력 증가와 완료 보상을 지급했습니다.");
        OnCleaningRoundCompleted?.Invoke();
    }

    private void EnsureDailyState()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string lastLoginDate = PlayerPrefs.GetString(LastLoginDateKey, string.Empty);

        if (lastLoginDate == today)
        {
            return;
        }

        completedQuestsToday = 0;
        isTodayMissionCompleted = false;
        currentQuests.Clear();
        todayClearedCounts.Clear();

        PlayerPrefs.SetString(LastLoginDateKey, today);
        SaveDailyProgress();
        Debug.Log("[일일 초기화] 새로운 날의 미션을 시작합니다.");
    }

    private void ProcessContinuousCleanCheck()
    {
        string lastCleanDateRaw = PlayerPrefs.GetString(LastCleanDateKey, string.Empty);
        DateTime today = DateTime.Today;

        if (string.IsNullOrWhiteSpace(lastCleanDateRaw) ||
            !DateTime.TryParse(lastCleanDateRaw, out DateTime lastCleanDate))
        {
            continuousCleanDays = 1;
        }
        else
        {
            int dayDifference = (today - lastCleanDate.Date).Days;

            if (dayDifference == 1)
            {
                continuousCleanDays++;
            }
            else if (dayDifference > 1)
            {
                continuousCleanDays = 1;
            }
            else
            {
                return;
            }
        }

        PlayerPrefs.SetString(LastCleanDateKey, today.ToString("yyyy-MM-dd"));
        PlayerPrefs.SetInt(ContinuousDaysKey, continuousCleanDays);
        PlayerPrefs.Save();

        if (continuousCleanDays > 0 && continuousCleanDays % 3 == 0)
        {
            GiveThreeDaySpecialReward();
        }
    }

    private void GiveThreeDaySpecialReward()
    {
        if (UnityEngine.Random.Range(0, 2) == 0)
        {
            CreditManager.Instance?.AddCredit(3);
            Debug.Log("[3일 연속 청소 보상] 3 CR을 획득했습니다.");
            return;
        }

        if (ShopInventoryManager.Instance != null)
        {
            ShopInventoryManager.Instance.GiveRandomUnownedItem();
            return;
        }

        CreditManager.Instance?.AddCredit(3);
    }

    private void UpdateTodayStatistics(string questType)
    {
        string key = string.IsNullOrWhiteSpace(questType) ? "unknown" : questType;

        if (!todayClearedCounts.ContainsKey(key))
        {
            todayClearedCounts[key] = 0;
        }

        todayClearedCounts[key]++;
    }

    private void InvokeUIUpdate()
    {
        int cleanliness = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 0;

        int credit = CreditManager.Instance != null
            ? CreditManager.Instance.CurrentCredit
            : 0;

        OnQuestUpdated?.Invoke(
            completedQuestsToday,
            requiredQuestsForToday,
            cleanliness,
            credit,
            continuousCleanDays,
            currentTidinessScore
        );
    }

    private void LoadPersistentData()
    {
        completedQuestsToday = Mathf.Max(0, PlayerPrefs.GetInt(CompletedTodayKey, 0));
        isTodayMissionCompleted = PlayerPrefs.GetInt(TodayCompletedKey, 0) == 1;
        continuousCleanDays = Mathf.Max(0, PlayerPrefs.GetInt(ContinuousDaysKey, 0));
    }

    private void SaveDailyProgress()
    {
        PlayerPrefs.SetInt(CompletedTodayKey, completedQuestsToday);
        PlayerPrefs.SetInt(TodayCompletedKey, isTodayMissionCompleted ? 1 : 0);
        PlayerPrefs.SetInt(ContinuousDaysKey, continuousCleanDays);
        PlayerPrefs.Save();
    }
}
