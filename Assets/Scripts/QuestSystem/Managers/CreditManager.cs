using System;
using UnityEngine;

/// <summary>
/// Owns and persists the player's credit balance.
/// </summary>
public class CreditManager : MonoBehaviour
{
    public static CreditManager Instance { get; private set; }
    public static event Action<int> OnCreditChanged;

    private const string CreditKey = "Dustiny_CurrentCredit_V2";

    [Header("[ 크레딧 시스템 ]")]
    [SerializeField, Min(0)] private int currentCredit = 0;

    public int CurrentCredit => currentCredit;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            currentCredit = Mathf.Max(0, PlayerPrefs.GetInt(CreditKey, currentCredit));
            return;
        }

        if (Instance != this)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        OnCreditChanged?.Invoke(currentCredit);
    }

    public void AddCredit(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"[크레딧] 0 이하의 지급 요청은 무시합니다: {amount}");
            return;
        }

        currentCredit += amount;
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);
        Debug.Log($"[크레딧 획득] +{amount} CR | 보유: {currentCredit} CR");
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
        SaveData();
        OnCreditChanged?.Invoke(currentCredit);
    }

    private void SaveData()
    {
        PlayerPrefs.SetInt(CreditKey, currentCredit);
        PlayerPrefs.Save();
    }
}
