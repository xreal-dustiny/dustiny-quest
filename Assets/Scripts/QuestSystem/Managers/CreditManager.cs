using System;
using UnityEngine;

/// <summary>
/// Owns and persists the player's credit balance.
/// The total saved balance can exceed 30 across multiple days,
/// but new credit earnings are capped at 30 CR per calendar day.
/// </summary>
public class CreditManager : MonoBehaviour
{
    public static CreditManager Instance { get; private set; }
    public static event Action<int> OnCreditChanged;

    private const string CreditKey = "Dustiny_CurrentCredit_V2";
    private const string DailyCreditDateKey = "Dustiny_DailyCreditDate_V1";
    private const string DailyEarnedCreditKey = "Dustiny_DailyEarnedCredit_V1";

    [Header("[ 크레딧 시스템 ]")]
    [SerializeField, Min(0)] private int currentCredit = 0;

    [Header("[ 하루 획득 한도 ]")]
    [SerializeField, Min(0)] private int dailyEarnedCreditCap = 30;
    [SerializeField, Min(0)] private int earnedCreditToday;

    public int CurrentCredit => currentCredit;
    public int DailyEarnedCreditCap => dailyEarnedCreditCap;
    public int EarnedCreditToday
    {
        get
        {
            EnsureDailyEarnState();
            return earnedCreditToday;
        }
    }
    public int RemainingDailyEarnableCredit =>
        Mathf.Max(0, DailyEarnedCreditCap - EarnedCreditToday);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            currentCredit = Mathf.Max(0, PlayerPrefs.GetInt(CreditKey, currentCredit));
            EnsureDailyEarnState();
            return;
        }

        if (Instance != this)
        {
            Destroy(this);
        }
    }

    private void OnValidate()
    {
        dailyEarnedCreditCap = Mathf.Max(0, dailyEarnedCreditCap);
    }

    private void Start()
    {
        EnsureDailyEarnState();
        OnCreditChanged?.Invoke(currentCredit);
    }

    /// <summary>
    /// Legacy-compatible wrapper. Daily-cap logic is applied.
    /// </summary>
    public void AddCredit(int amount)
    {
        GrantCredit(amount);
    }

    /// <summary>
    /// Adds as much of the requested amount as remains under today's 30 CR cap.
    /// Returns the amount actually granted.
    /// </summary>
    public int GrantCredit(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"[크레딧] 0 이하의 지급 요청은 무시합니다: {amount}");
            return 0;
        }

        EnsureDailyEarnState();

        int remaining = Mathf.Max(0, dailyEarnedCreditCap - earnedCreditToday);
        int granted = Mathf.Min(amount, remaining);

        if (granted <= 0)
        {
            Debug.Log($"[크레딧 일일 한도] 오늘은 이미 {earnedCreditToday}/{dailyEarnedCreditCap} CR을 획득했습니다.");
            return 0;
        }

        currentCredit += granted;
        earnedCreditToday += granted;
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);

        Debug.Log(
            $"[크레딧 획득] +{granted} CR | 오늘 {earnedCreditToday}/{dailyEarnedCreditCap} CR | 보유 {currentCredit} CR"
        );

        if (granted < amount)
        {
            Debug.Log($"[크레딧 일일 한도] 요청 {amount} CR 중 {granted} CR만 지급했습니다.");
        }

        return granted;
    }

    /// <summary>
    /// Debug/admin use only. This bypasses the daily earning cap.
    /// </summary>
    public void AddCreditIgnoringDailyLimit(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        currentCredit += amount;
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);
    }

    public bool UseCredit(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        if (currentCredit < amount)
        {
            Debug.LogWarning($"[크레딧 부족] 필요: {amount} CR | 보유: {currentCredit} CR");
            return false;
        }

        currentCredit -= amount;
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);
        Debug.Log($"[크레딧 소비] -{amount} CR | 보유: {currentCredit} CR");
        return true;
    }

    [ContextMenu("Reset Credit")]
    public void ResetCreditForDebug()
    {
        currentCredit = 0;
        earnedCreditToday = 0;
        PlayerPrefs.SetString(DailyCreditDateKey, DateTime.Today.ToString("yyyy-MM-dd"));
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);
    }

    private void EnsureDailyEarnState()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string savedDate = PlayerPrefs.GetString(DailyCreditDateKey, string.Empty);

        if (savedDate == today)
        {
            earnedCreditToday = Mathf.Clamp(
                PlayerPrefs.GetInt(DailyEarnedCreditKey, earnedCreditToday),
                0,
                Mathf.Max(0, dailyEarnedCreditCap)
            );
            return;
        }

        earnedCreditToday = 0;
        PlayerPrefs.SetString(DailyCreditDateKey, today);
        PlayerPrefs.SetInt(DailyEarnedCreditKey, 0);
        PlayerPrefs.Save();
    }

    private void SaveData()
    {
        PlayerPrefs.SetInt(CreditKey, currentCredit);
        PlayerPrefs.SetString(DailyCreditDateKey, DateTime.Today.ToString("yyyy-MM-dd"));
        PlayerPrefs.SetInt(DailyEarnedCreditKey, earnedCreditToday);
        PlayerPrefs.Save();
    }
}
