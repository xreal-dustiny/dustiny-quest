using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the current AI-object cleaning round and the daily reward limit.
///
/// Structure:
/// - One cleaning round: clear up to three detected objects.
/// - The player may start unlimited cleaning rounds.
/// - Only the first three completed rounds each day grant credit/cleanliness rewards.
/// - Object checkboxes themselves do not pay credit.
/// </summary>
public class QuestProgressManager : MonoBehaviour
{
    public static QuestProgressManager Instance { get; private set; }

    /// <summary>
    /// Arguments: completed rounds today, required rounds today, cleanliness,
    /// credit, streak days, tidiness score.
    /// </summary>
    public static event Action<int, int, int, int, int, int> OnQuestUpdated;
    public static event Action OnQuestListChanged;
    public static event Action<QuestData> OnQuestCleared;
    public static event Action OnCleaningRoundCompleted;

    // V4 intentionally separates daily rounds from the old V3 object-count progress.
    // This prevents a previous one-round completion flag from immediately completing the new 3-round system.
    private const string LastMissionDateKey = "Dustiny_LastMissionDate_V4";
    private const string CompletedRoundsTodayKey = "Dustiny_CompletedRoundsToday_V4";
    private const string TodayCompletedKey = "Dustiny_IsTodayMissionCompleted_V4";

    // Keep the existing streak keys so users do not lose streak data.
    private const string LastCleanDateKey = "Dustiny_LastCleanDate_V3";
    private const string ContinuousDaysKey = "Dustiny_ContinuousCleanDays_V3";

    [Header("[ 현재 AI 미션 목록 ]")]
    public List<QuestData> currentQuests = new List<QuestData>();

    [Header("[ AI 계산 책상 종합 정보 ]")]
    [SerializeField] private int currentTidinessScore;
    [SerializeField] private bool needsCleaning;

    [Header("[ 오늘의 보상 한도 - 청소 라운드 3회 ]")]
    [SerializeField, Min(1)] private int requiredCleaningRoundsPerDay = 3;
    [SerializeField, Min(0)] private int completedCleaningRoundsToday;
    [SerializeField] private bool isTodayMissionCompleted;

    [Header("[ 현재 청소 라운드 - 물건 최대 3개 ]")]
    [SerializeField, Min(1)] private int currentRoundRequiredObjectCount = 3;
    [SerializeField, Min(0)] private int currentRoundClearedObjectCount;
    [SerializeField] private bool isCurrentRoundCompleted;

    [Header("[ 라운드 완료 보상 ]")]
    [Tooltip("청소 라운드 1회 완료 시 고정으로 지급되는 코인입니다.")]
    private const int RoundRewardCreditValue = 10;

    [Header("[ 연속 청소 데이터 ]")]
    [SerializeField, Min(0)] private int continuousCleanDays;

    [Header("[ 최근 라운드 결과 - 런타임 확인 ]")]
    [SerializeField] private bool lastCompletedRoundGrantedReward;

    private readonly Dictionary<string, int> todayClearedCounts = new Dictionary<string, int>();

    public int CompletedRoundsToday => completedCleaningRoundsToday;
    public int RequiredCleaningRoundsPerDay => requiredCleaningRoundsPerDay;
    public int CurrentRoundRequiredObjectCount => currentRoundRequiredObjectCount;
    public int CurrentRoundClearedObjectCount => currentRoundClearedObjectCount;
    public int RoundRewardCredit => RoundRewardCreditValue;
    public bool LastCompletedRoundGrantedReward => lastCompletedRoundGrantedReward;
    public int LastCompletedRoundRewardCredit =>
        lastCompletedRoundGrantedReward ? RoundRewardCreditValue : 0;

    // Legacy property names are preserved so older UI scripts still compile.
    public int CompletedQuestsToday => completedCleaningRoundsToday;
    public int RequiredQuestsForToday => requiredCleaningRoundsPerDay;

    public int ContinuousCleanDays => continuousCleanDays;
    public int CurrentTidinessScore => currentTidinessScore;
    public bool NeedsCleaning => needsCleaning;
    // Legacy name: this now means that today's three reward-bearing rounds are complete.
    // It does NOT block additional cleaning rounds.
    public bool IsTodayMissionCompleted => isTodayMissionCompleted;
    public bool HasReachedDailyRewardLimit => isTodayMissionCompleted;
    public bool IsCurrentRoundCompleted => isCurrentRoundCompleted;
    public bool HasActiveQuest => !isCurrentRoundCompleted && currentQuests.Count > 0;
    public bool CanStartNewRound => currentQuests.Count == 0 || isCurrentRoundCompleted;

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
    /// The round target is min(detected object count, configured maximum of three).
    /// This method changes only the current round target. It never changes the daily 3-round target.
    /// </summary>
    public void ConfigureRequiredQuestCountForCurrentRound(int requiredCount)
    {
        EnsureDailyState();

        currentRoundRequiredObjectCount = Mathf.Max(1, requiredCount);
        currentRoundClearedObjectCount = Mathf.Clamp(
            currentRoundClearedObjectCount,
            0,
            currentRoundRequiredObjectCount
        );

        SaveDailyProgress();
        InvokeUIUpdate();

        Debug.Log(
            $"[QuestProgress] 이번 청소 라운드 목표: 물건 {currentRoundRequiredObjectCount}개"
        );
    }

    /// <summary>
    /// Called after the first AI scan of a new cleaning round.
    /// Daily completed-round progress is preserved.
    /// </summary>
    public void SetupRoomAndQuests(YOLOScanData scanData, List<QuestData> newQuests)
    {
        EnsureDailyState();

        currentQuests = newQuests ?? new List<QuestData>();
        currentRoundClearedObjectCount = 0;
        isCurrentRoundCompleted = false;
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
    /// Marks one detected object as cleared. No credit is paid here.
    /// Credit is paid exactly once when the whole round reaches its target.
    /// </summary>
    public bool ClearQuest(string questId)
    {
        EnsureDailyState();

        if (isCurrentRoundCompleted ||
            string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        QuestData quest = currentQuests.Find(item => item != null && item.questId == questId);
        if (quest == null || quest.isCleared)
        {
            return false;
        }

        quest.isCleared = true;
        quest.isRewardGiven = false;
        currentRoundClearedObjectCount = Mathf.Min(
            currentRoundClearedObjectCount + 1,
            currentRoundRequiredObjectCount
        );
        UpdateTodayStatistics(quest.questType);

        OnQuestCleared?.Invoke(quest);
        TryCompleteCurrentRound();
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
    /// Clears only the temporary current-round data before a new first scan.
    /// The number of cleaning rounds already completed today is preserved.
    /// </summary>
    public void ResetCurrentQuestSession()
    {
        EnsureDailyState();

        currentQuests.Clear();
        currentRoundClearedObjectCount = 0;
        currentRoundRequiredObjectCount = 3;
        isCurrentRoundCompleted = false;
        currentTidinessScore = 0;
        needsCleaning = false;
        todayClearedCounts.Clear();
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
    }

    /// <summary>
    /// Legacy compatibility only. Individual objects are completed by either
    /// the mission-card checkbox or the after-cleaning scan.
    /// </summary>
    public bool CompleteCurrentCleaningRound()
    {
        Debug.LogWarning(
            "[QuestProgress] 라운드 전체 자동 완료는 사용하지 않습니다. 물건별 체크박스 또는 재스캔 판정을 사용하세요."
        );
        return isCurrentRoundCompleted;
    }

    private void TryCompleteCurrentRound()
    {
        if (isCurrentRoundCompleted ||
            currentRoundClearedObjectCount < currentRoundRequiredObjectCount)
        {
            return;
        }

        isCurrentRoundCompleted = true;
        currentRoundClearedObjectCount = currentRoundRequiredObjectCount;

        bool rewardAvailable =
            completedCleaningRoundsToday < requiredCleaningRoundsPerDay;
        lastCompletedRoundGrantedReward = rewardAvailable;

        if (rewardAvailable)
        {
            completedCleaningRoundsToday = Mathf.Min(
                completedCleaningRoundsToday + 1,
                requiredCleaningRoundsPerDay
            );

            if (RoundRewardCreditValue > 0)
            {
                CreditManager.Instance?.AddCredit(RoundRewardCreditValue);
            }

            // Bosong power is part of the daily round reward, so it is also capped at three.
            CleanlinessManager.Instance?.OnCleanSuccess();

            bool reachedRewardLimitThisRound =
                !isTodayMissionCompleted &&
                completedCleaningRoundsToday >= requiredCleaningRoundsPerDay;

            if (reachedRewardLimitThisRound)
            {
                completedCleaningRoundsToday = requiredCleaningRoundsPerDay;
                isTodayMissionCompleted = true;
                ProcessContinuousCleanCheck();
            }
        }

        SaveDailyProgress();

        Debug.Log(
            rewardAvailable
                ? $"[청소 라운드 완료] 물건 {currentRoundRequiredObjectCount}개 정리 확인, " +
                  $"코인 +{RoundRewardCreditValue}, 오늘 보상 {completedCleaningRoundsToday}/{requiredCleaningRoundsPerDay}회"
                : $"[추가 청소 완료] 물건 {currentRoundRequiredObjectCount}개 정리 확인, " +
                  "오늘의 3회 보상은 이미 모두 지급되어 추가 보상은 없습니다."
        );

        OnCleaningRoundCompleted?.Invoke();
    }

    private void EnsureDailyState()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string savedMissionDate = PlayerPrefs.GetString(LastMissionDateKey, string.Empty);

        if (savedMissionDate == today)
        {
            return;
        }

        completedCleaningRoundsToday = 0;
        isTodayMissionCompleted = false;
        lastCompletedRoundGrantedReward = false;
        currentQuests.Clear();
        currentRoundClearedObjectCount = 0;
        currentRoundRequiredObjectCount = 3;
        isCurrentRoundCompleted = false;
        currentTidinessScore = 0;
        needsCleaning = false;
        todayClearedCounts.Clear();

        PlayerPrefs.SetString(LastMissionDateKey, today);
        SaveDailyProgress();
        Debug.Log("[일일 초기화] 오늘의 청소 미션 3회를 새로 시작합니다.");
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
            completedCleaningRoundsToday,
            requiredCleaningRoundsPerDay,
            cleanliness,
            credit,
            continuousCleanDays,
            currentTidinessScore
        );
    }

    private void LoadPersistentData()
    {
        completedCleaningRoundsToday = Mathf.Clamp(
            PlayerPrefs.GetInt(CompletedRoundsTodayKey, 0),
            0,
            requiredCleaningRoundsPerDay
        );

        isTodayMissionCompleted =
            PlayerPrefs.GetInt(TodayCompletedKey, 0) == 1 ||
            completedCleaningRoundsToday >= requiredCleaningRoundsPerDay;

        if (isTodayMissionCompleted)
        {
            completedCleaningRoundsToday = requiredCleaningRoundsPerDay;
        }

        continuousCleanDays = Mathf.Max(0, PlayerPrefs.GetInt(ContinuousDaysKey, 0));

        lastCompletedRoundGrantedReward = false;

        // A half-finished camera round is intentionally not restored after relaunch.
        currentQuests.Clear();
        currentRoundClearedObjectCount = 0;
        currentRoundRequiredObjectCount = 3;
        isCurrentRoundCompleted = false;
    }

    private void SaveDailyProgress()
    {
        PlayerPrefs.SetInt(CompletedRoundsTodayKey, completedCleaningRoundsToday);
        PlayerPrefs.SetInt(TodayCompletedKey, isTodayMissionCompleted ? 1 : 0);
        PlayerPrefs.SetInt(ContinuousDaysKey, continuousCleanDays);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 왼손 약지 디버그 초기화용입니다.
    /// 오늘의 라운드 진행도와 현재 미션뿐 아니라 연속 청소 일수까지 초기값으로 되돌립니다.
    /// </summary>
    [ContextMenu("Debug/Reset All Mission Progress")]
    public void ResetAllMissionProgressForDebug()
    {
        completedCleaningRoundsToday = 0;
        isTodayMissionCompleted = false;
        lastCompletedRoundGrantedReward = false;
        currentQuests.Clear();
        currentRoundClearedObjectCount = 0;
        currentRoundRequiredObjectCount = 3;
        isCurrentRoundCompleted = false;
        currentTidinessScore = 0;
        needsCleaning = false;
        continuousCleanDays = 0;
        todayClearedCounts.Clear();

        PlayerPrefs.SetString(LastMissionDateKey, DateTime.Today.ToString("yyyy-MM-dd"));
        PlayerPrefs.DeleteKey(LastCleanDateKey);
        PlayerPrefs.SetInt(ContinuousDaysKey, 0);
        SaveDailyProgress();

        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
        Debug.Log("[Debug] 미션 진행도와 연속 청소 기록을 모두 초기화했습니다.");
    }

    [ContextMenu("Debug/Reset Today's Three Rounds")]
    public void ResetTodayForDebug()
    {
        completedCleaningRoundsToday = 0;
        isTodayMissionCompleted = false;
        lastCompletedRoundGrantedReward = false;
        currentQuests.Clear();
        currentRoundClearedObjectCount = 0;
        currentRoundRequiredObjectCount = 3;
        isCurrentRoundCompleted = false;
        currentTidinessScore = 0;
        needsCleaning = false;
        todayClearedCounts.Clear();
        PlayerPrefs.SetString(LastMissionDateKey, DateTime.Today.ToString("yyyy-MM-dd"));
        SaveDailyProgress();
        OnQuestListChanged?.Invoke();
        InvokeUIUpdate();
        Debug.Log("[Debug] 오늘의 청소 미션 3회 상태를 초기화했습니다.");
    }
}
