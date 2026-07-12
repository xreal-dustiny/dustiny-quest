using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Owns the single source of truth for Durry's cleanliness score.
/// Score range: 0 to 4.
/// Cleaning success adds 1 and resets the six-hour decay timer.
/// </summary>
public class CleanlinessManager : MonoBehaviour
{
    public static CleanlinessManager Instance { get; private set; }

    public static event Action<int> OnStateChanged;

    private const int MinCleanliness = 0;
    private const int MaxCleanliness = 4;
    private const int DefaultCleanliness = 4;
    private const double DecayIntervalHours = 6.0;

    private const string FirstPlayKey = "Dustiny_IsFirstPlay_V2";
    private const string CleanlinessKey = "Dustiny_CleanlinessScore_V2";
    private const string DecayReferenceTimeKey = "Dustiny_CleanlinessDecayReference_V2";

    // Previous-version keys are kept only for migration.
    private const string LegacyFirstPlayKey = "IsFirstPlay";
    private const string LegacyCleanlinessKey = "SavedCleanlinessScore";
    private const string LegacyLastSaveTimeKey = "LastSaveTime";

    [Header("[ 보송력 시스템 (0 ~ 4) ]")]
    [SerializeField, Range(MinCleanliness, MaxCleanliness)]
    private int cleanlinessScore = DefaultCleanliness;

    [Header("[ 런타임 체크 ]")]
    [SerializeField, Tooltip("실행 중 경과 시간을 확인하는 간격(초)")]
    private float runtimeCheckIntervalSeconds = 30f;

    public int CleanlinessScore => cleanlinessScore;
    public string CurrentStateName => GetBosongStateName(cleanlinessScore);

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
            Destroy(this);
        }
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

        nextRuntimeCheckTime = Time.unscaledTime + Mathf.Max(1f, runtimeCheckIntervalSeconds);
        ProcessElapsedDecay(DateTime.Now);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveData();
        }
        else
        {
            ProcessElapsedDecay(DateTime.Now);
        }
    }

    private void OnApplicationQuit()
    {
        SaveData();
    }

    /// <summary>
    /// Call once after a complete cleaning round succeeds.
    /// </summary>
    public void OnCleanSuccess()
    {
        decayReferenceTime = DateTime.Now;
        ApplyCleanlinessScoreChange(1);
    }

    public void ApplyCleanlinessScoreChange(int amount)
    {
        int previousScore = cleanlinessScore;
        cleanlinessScore = Mathf.Clamp(cleanlinessScore + amount, MinCleanliness, MaxCleanliness);

        SaveData();

        if (previousScore != cleanlinessScore)
        {
            Debug.Log($"[보송력 변경] {previousScore} → {cleanlinessScore} ({CurrentStateName})");
            OnStateChanged?.Invoke(cleanlinessScore);
        }
    }

    public string GetBosongStateName(int score)
    {
        switch (Mathf.Clamp(score, MinCleanliness, MaxCleanliness))
        {
            case 0: return "심각하게 더러움";
            case 1: return "약간 더러움";
            case 2: return "보통";
            case 3: return "양호";
            case 4: return "아주 깨끗";
            default: return "알 수 없음";
        }
    }

    [ContextMenu("Reset Cleanliness To Default")]
    public void ResetToDefaultForDebug()
    {
        cleanlinessScore = DefaultCleanliness;
        decayReferenceTime = DateTime.Now;
        SaveData();
        OnStateChanged?.Invoke(cleanlinessScore);
    }

    private void LoadData()
    {
        bool hasV2Data = PlayerPrefs.HasKey(FirstPlayKey);

        if (!hasV2Data)
        {
            MigrateLegacyDataOrCreateDefault();
        }
        else
        {
            cleanlinessScore = Mathf.Clamp(
                PlayerPrefs.GetInt(CleanlinessKey, DefaultCleanliness),
                MinCleanliness,
                MaxCleanliness
            );

            decayReferenceTime = ReadDateTime(
                PlayerPrefs.GetString(DecayReferenceTimeKey, string.Empty),
                DateTime.Now
            );
        }

        ProcessElapsedDecay(DateTime.Now, saveEvenWhenUnchanged: true);
    }

    private void MigrateLegacyDataOrCreateDefault()
    {
        bool hasLegacyData = PlayerPrefs.HasKey(LegacyFirstPlayKey) ||
                             PlayerPrefs.HasKey(LegacyCleanlinessKey);

        if (hasLegacyData)
        {
            cleanlinessScore = Mathf.Clamp(
                PlayerPrefs.GetInt(LegacyCleanlinessKey, DefaultCleanliness),
                MinCleanliness,
                MaxCleanliness
            );

            decayReferenceTime = ReadDateTime(
                PlayerPrefs.GetString(LegacyLastSaveTimeKey, string.Empty),
                DateTime.Now
            );
        }
        else
        {
            cleanlinessScore = DefaultCleanliness;
            decayReferenceTime = DateTime.Now;
        }

        PlayerPrefs.SetInt(FirstPlayKey, 1);
        SaveData();
    }

    private void ProcessElapsedDecay(DateTime now, bool saveEvenWhenUnchanged = false)
    {
        if (decayReferenceTime == default)
        {
            decayReferenceTime = now;
        }

        double elapsedHours = Math.Max(0.0, (now - decayReferenceTime).TotalHours);
        int decreaseAmount = Mathf.FloorToInt((float)(elapsedHours / DecayIntervalHours));

        if (decreaseAmount <= 0)
        {
            if (saveEvenWhenUnchanged)
            {
                SaveData();
            }
            return;
        }

        int previousScore = cleanlinessScore;
        cleanlinessScore = Mathf.Clamp(cleanlinessScore - decreaseAmount, MinCleanliness, MaxCleanliness);

        // Preserve the remainder instead of resetting the timer to now.
        decayReferenceTime = decayReferenceTime.AddHours(decreaseAmount * DecayIntervalHours);

        Debug.Log($"[보송력 자동 감소] {elapsedHours:F1}시간 경과, {decreaseAmount}단계 차감");
        SaveData();

        if (previousScore != cleanlinessScore)
        {
            OnStateChanged?.Invoke(cleanlinessScore);
        }
    }

    private void SaveData()
    {
        PlayerPrefs.SetInt(FirstPlayKey, 1);
        PlayerPrefs.SetInt(CleanlinessKey, cleanlinessScore);
        PlayerPrefs.SetString(
            DecayReferenceTimeKey,
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

        if (DateTime.TryParse(rawValue, out parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
