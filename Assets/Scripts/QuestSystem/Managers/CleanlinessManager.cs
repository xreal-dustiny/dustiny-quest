using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 더리의 보송력을 저장하고 관리합니다.
/// - 범위: 0 ~ 4
/// - 최초 시작값: 2
/// - 청소 라운드 완료: +1
/// - 최대값 4 유지
/// - 청소하지 않으면 2시간마다 -1
/// - 앱이 꺼져 있던 시간도 반영
/// </summary>
public class CleanlinessManager : MonoBehaviour
{
    public static CleanlinessManager Instance { get; private set; }
    public static event Action<int> OnStateChanged;

    private const int MinimumScore = 0;
    private const int MaximumScore = 4;
    private const int InitialScore = 2;
    private const float DefaultDecayHours = 2f;

    // 이전 버전 저장값과 충돌하지 않도록 V3 키를 사용합니다.
    private const string InitializedKey = "Dustiny_CleanlinessInitialized_V3";
    private const string ScoreKey = "Dustiny_CleanlinessScore_V3";
    private const string ReferenceTimeKey = "Dustiny_CleanlinessReferenceTime_V3";

    [Header("[ 보송력 시스템 (0 ~ 4) ]")]
    [SerializeField, Range(MinimumScore, MaximumScore)]
    private int cleanlinessScore = InitialScore;

    [Header("[ 자동 감소 ]")]
    [SerializeField, Min(0.01f)]
    [Tooltip("청소하지 않았을 때 보송력이 1 감소하는 시간입니다. 기본값은 2시간입니다.")]
    private float decayHours = DefaultDecayHours;

    [Header("[ 런타임 확인 주기 ]")]
    [SerializeField, Min(1f)]
    [Tooltip("실행 중 시간 경과를 확인하는 주기입니다.")]
    private float runtimeCheckIntervalSeconds = 30f;

    public int CleanlinessScore => cleanlinessScore;
    public string CurrentStateName => GetBosongStateName(cleanlinessScore);
    public float ConfiguredDecayHours => Mathf.Max(0.01f, decayHours);

    private DateTime decayReferenceTime;
    private float nextRuntimeCheckTime;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            LoadData();
            return;
        }

        if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnValidate()
    {
        decayHours = Mathf.Max(0.01f, decayHours);
        runtimeCheckIntervalSeconds = Mathf.Max(1f, runtimeCheckIntervalSeconds);
        cleanlinessScore = Mathf.Clamp(cleanlinessScore, MinimumScore, MaximumScore);
    }

    private void Start()
    {
        OnStateChanged?.Invoke(cleanlinessScore);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRuntimeCheckTime)
        {
            return;
        }

        nextRuntimeCheckTime = Time.unscaledTime + runtimeCheckIntervalSeconds;
        ProcessElapsedDecay(DateTime.Now);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveData();
            return;
        }

        ProcessElapsedDecay(DateTime.Now);
    }

    private void OnApplicationQuit()
    {
        SaveData();
    }

    /// <summary>
    /// 청소 라운드가 한 번 완료될 때 호출합니다.
    /// 보송력은 1 증가하고 감소 기준 시간이 현재 시각으로 초기화됩니다.
    /// 이미 4라면 점수는 4를 유지하고 감소 타이머만 다시 시작합니다.
    /// </summary>
    public void OnCleanSuccess()
    {
        decayReferenceTime = DateTime.Now;
        SetScore(cleanlinessScore + 1, notifyEvenWhenUnchanged: false);
    }

    public void ApplyCleanlinessScoreChange(int amount)
    {
        SetScore(cleanlinessScore + amount, notifyEvenWhenUnchanged: false);
    }

    public string GetBosongStateName(int score)
    {
        switch (Mathf.Clamp(score, MinimumScore, MaximumScore))
        {
            case 0: return "심각하게 더러움";
            case 1: return "약간 더러움";
            case 2: return "보통";
            case 3: return "양호";
            case 4: return "아주 깨끗";
            default: return "알 수 없음";
        }
    }

    [ContextMenu("Debug/Reset Cleanliness To 2")]
    public void ResetToDefaultForDebug()
    {
        cleanlinessScore = InitialScore;
        decayReferenceTime = DateTime.Now;
        SaveData();
        OnStateChanged?.Invoke(cleanlinessScore);
        Debug.Log("[보송력 초기화] 보송력을 2로 초기화했습니다.");
    }

    private void SetScore(int newScore, bool notifyEvenWhenUnchanged)
    {
        int previousScore = cleanlinessScore;
        cleanlinessScore = Mathf.Clamp(newScore, MinimumScore, MaximumScore);
        SaveData();

        if (previousScore != cleanlinessScore || notifyEvenWhenUnchanged)
        {
            Debug.Log($"[보송력 변경] {previousScore} → {cleanlinessScore} ({CurrentStateName})");
            OnStateChanged?.Invoke(cleanlinessScore);
        }
        else if (newScore > MaximumScore && cleanlinessScore == MaximumScore)
        {
            Debug.Log("[보송력 유지] 이미 최대치 4입니다. 감소 타이머만 다시 시작했습니다.");
        }
    }

    private void LoadData()
    {
        bool alreadyInitialized = PlayerPrefs.GetInt(InitializedKey, 0) == 1;

        if (!alreadyInitialized)
        {
            cleanlinessScore = InitialScore;
            decayReferenceTime = DateTime.Now;
            SaveData();
            return;
        }

        cleanlinessScore = Mathf.Clamp(
            PlayerPrefs.GetInt(ScoreKey, InitialScore),
            MinimumScore,
            MaximumScore
        );

        decayReferenceTime = ReadDateTime(
            PlayerPrefs.GetString(ReferenceTimeKey, string.Empty),
            DateTime.Now
        );

        ProcessElapsedDecay(DateTime.Now, saveEvenWhenUnchanged: true);
    }

    private void ProcessElapsedDecay(DateTime now, bool saveEvenWhenUnchanged = false)
    {
        if (decayReferenceTime == default)
        {
            decayReferenceTime = now;
        }

        double elapsedHours = Math.Max(0d, (now - decayReferenceTime).TotalHours);
        double safeDecayHours = Math.Max(0.01d, decayHours);
        int decreaseAmount = Mathf.FloorToInt((float)(elapsedHours / safeDecayHours));

        if (decreaseAmount <= 0)
        {
            if (saveEvenWhenUnchanged)
            {
                SaveData();
            }
            return;
        }

        int previousScore = cleanlinessScore;
        cleanlinessScore = Mathf.Clamp(
            cleanlinessScore - decreaseAmount,
            MinimumScore,
            MaximumScore
        );

        // 남은 시간을 보존하기 위해 감소한 단계 수만큼만 기준 시간을 이동합니다.
        decayReferenceTime = decayReferenceTime.AddHours(decreaseAmount * safeDecayHours);
        SaveData();

        Debug.Log(
            $"[보송력 자동 감소] {elapsedHours:F1}시간 경과, " +
            $"{decreaseAmount}단계 계산, {previousScore} → {cleanlinessScore}"
        );

        if (previousScore != cleanlinessScore)
        {
            OnStateChanged?.Invoke(cleanlinessScore);
        }
    }

    private void SaveData()
    {
        if (decayReferenceTime == default)
        {
            decayReferenceTime = DateTime.Now;
        }

        PlayerPrefs.SetInt(InitializedKey, 1);
        PlayerPrefs.SetInt(ScoreKey, cleanlinessScore);
        PlayerPrefs.SetString(
            ReferenceTimeKey,
            decayReferenceTime.ToString("O", CultureInfo.InvariantCulture)
        );
        PlayerPrefs.Save();
    }

    private static DateTime ReadDateTime(string rawValue, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        if (DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(rawValue, out parsed) ? parsed : fallback;
    }
}
