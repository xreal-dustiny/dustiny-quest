using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the current three-object quest, per-object rewards, daily completion,
/// cleanliness reward, and cleaning streak.
/// </summary>
public class QuestProgressManager : MonoBehaviour
{
    public static QuestProgressManager Instance { get; private set; }

    public static event Action<int, int, int, int, int, int> OnQuestUpdated;
    public static event Action OnQuestListChanged;
    public static event Action<QuestData> OnQuestCleared;
    public static event Action OnCleaningRoundCompleted;

    private const string LastLoginDateKey = "Dustiny_LastLoginDate_V3";
    private const string CompletedTodayKey = "Dustiny_CompletedQuestsToday_V3";
    private const string TodayCompletedKey = "Dustiny_IsTodayMissionCompleted_V3";
    private const string LastCleanDateKey = "Dustiny_LastCleanDate_V3";
    private const string ContinuousDaysKey = "Dustiny_ContinuousCleanDays_V3";

    [Header("[ 현재 AI 미션 목록 ]")]
    public List<QuestData> currentQuests = new List<QuestData>();

    [Header("[ AI 계산 책상 종합 정보 ]")]
    [SerializeField] private int currentTidinessScore;
    [SerializeField] private bool needsCleaning;

    [Header("[ 퀘스트 완료 조건 ]")]
    [SerializeField, Min(1)] private int requiredQuestsForToday = 3;
    [SerializeField, Min(0)] private int completedQuestsToday;
    [SerializeField] private bool isTodayMissionCompleted;

    [Header("[ 연속 청소 데이터 ]")]
    [SerializeField, Min(0)] private int continuousCleanDays;

    private readonly Dictionary<string, int> todayClearedCounts = new Dictionary<string, int>();

    public int CompletedQuestsToday => completedQuestsToday;
    public int RequiredQuestsForToday => requiredQuestsForToday;
    public int ContinuousCleanDays => continuousCleanDays;
    public int CurrentTidinessScore => currentTidinessScore;
    public bool NeedsCleaning => needsCleaning;
    public bool IsTodayMissionCompleted => isTodayMissionCompleted;
    public bool HasActiveQuest => !isTodayMissionCompleted && currentQuests.Count > 0;

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

    /// <summary>
    /// Called after the first AI scan. This begins a fresh three-object quest.
    /// </summary>
    public void SetupRoomAndQuests(YOLOScanData scanData, List<QuestData> newQuests)
    {
        EnsureDailyState();

        if (isTodayMissionCompleted)
        {
            currentQuests.Clear();
            OnQuestListChanged?.Invoke();
            InvokeUIUpdate();
            Debug.Log("[QuestProgress] 오늘의 퀘스트가 이미 완료되어 새 목록을 만들지 않습니다.");
            return;
        }

        currentQuests = newQuests ?? new List<QuestData>();
        completedQuestsToday = 0;
        todayClearedCounts.Clear();

        foreach (QuestData quest in currentQuests)
        {
            if (quest == null)
            {
                continue;
            }

            quest.isCleared = false;
            quest.isRewardGiven = false;
        }

        if (scanData != null && scanData.scan_summary != null)
        {
            currentTidinessScore = Mathf.Clamp(scanData.scan_summary.tidiness_score, 0, 100);
            needsCleaning = scanData.scan_summary.needs_cleaning;
        }
        else
        {
            currentTidinessScore = 0;
            needsCleaning = currentQuests.Count > 0;
        }

        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
    }

    /// <summary>
    /// Clears one mission. Each mission grants its own reward exactly once.
    /// The third cleared mission grants +1 cleanliness exactly once.
    /// </summary>
    public bool ClearQuest(string questId)
    {
        EnsureDailyState();

        if (isTodayMissionCompleted || string.IsNullOrWhiteSpace(questId))
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

        OnQuestCleared?.Invoke(quest);
        TryCompleteDailyMission();
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
        return true;
    }

    public QuestData GetQuest(string questId)
    {
        return currentQuests.Find(item => item != null && item.questId == questId);
    }

    public QuestData GetNextIncompleteQuest()
    {
        return currentQuests.Find(quest => quest != null && !quest.isCleared);
    }

    public List<QuestData> GetIncompleteQuests()
    {
        return currentQuests.FindAll(quest => quest != null && !quest.isCleared);
    }

    /// <summary>
    /// Clears temporary quest data before a new first scan. It does not reset a quest
    /// that has already been completed today.
    /// </summary>
    public void ResetCurrentQuestSession()
    {
        EnsureDailyState();

        if (isTodayMissionCompleted)
        {
            return;
        }

        currentQuests.Clear();
        completedQuestsToday = 0;
        currentTidinessScore = 0;
        needsCleaning = false;
        todayClearedCounts.Clear();
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
    }

    /// <summary>
    /// Legacy compatibility only. Completion is no longer triggered by a finger gesture.
    /// </summary>
    public bool CompleteCurrentCleaningRound()
    {
        Debug.LogWarning("[QuestProgress] CompleteCurrentCleaningRound은 더 이상 자동 완료에 사용되지 않습니다. 체크박스 또는 재스캔을 사용하세요.");
        return isTodayMissionCompleted;
    }

    private void TryCompleteDailyMission()
    {
        if (isTodayMissionCompleted || completedQuestsToday < requiredQuestsForToday)
        {
            return;
        }

        isTodayMissionCompleted = true;
        completedQuestsToday = requiredQuestsForToday;

        // Object rewards were already granted per checkbox: cleared count × 10 CR.
        // No additional credit is granted here.
        CleanlinessManager.Instance?.OnCleanSuccess();
        ProcessContinuousCleanCheck();
        SaveDailyProgress();

        Debug.Log("[오늘의 퀘스트 완료] 물건 3개 완료, 보송력 +1");
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
        Debug.Log("[일일 초기화] 새로운 날의 퀘스트를 시작합니다.");
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
        isTodayMissionCompleted = PlayerPrefs.GetInt(TodayCompletedKey, 0) == 1;
        completedQuestsToday = isTodayMissionCompleted
            ? requiredQuestsForToday
            : 0;
        continuousCleanDays = Mathf.Max(0, PlayerPrefs.GetInt(ContinuousDaysKey, 0));
    }

    private void SaveDailyProgress()
    {
        PlayerPrefs.SetInt(CompletedTodayKey, completedQuestsToday);
        PlayerPrefs.SetInt(TodayCompletedKey, isTodayMissionCompleted ? 1 : 0);
        PlayerPrefs.SetInt(ContinuousDaysKey, continuousCleanDays);
        PlayerPrefs.Save();
    }

    [ContextMenu("Debug/Reset Today's Quest")]
    public void ResetTodayForDebug()
    {
        isTodayMissionCompleted = false;
        completedQuestsToday = 0;
        currentQuests.Clear();
        currentTidinessScore = 0;
        needsCleaning = false;
        todayClearedCounts.Clear();
        PlayerPrefs.SetString(LastLoginDateKey, DateTime.Today.ToString("yyyy-MM-dd"));
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
        Debug.Log("[Debug] 오늘의 퀘스트 상태를 초기화했습니다.");
    }
}
