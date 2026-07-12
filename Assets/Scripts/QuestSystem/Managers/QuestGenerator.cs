using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts YOLO JSON results into QuestData objects.
/// It does not grant rewards or change cleanliness.
/// </summary>
public class QuestGenerator : MonoBehaviour
{
    public static QuestGenerator Instance { get; private set; }
    public static event Action<YOLOScanData> OnObjectsDetected;

    [System.Serializable]
    public struct QuestRewardPreset
    {
        public string questType;
        [Min(0)] public int rewardCredit;
    }

    [Header("[ 기획 데이터 테이블 ]")]
    [SerializeField] private List<QuestRewardPreset> rewardPresets = new List<QuestRewardPreset>();

    [Header("[ 기본 보상 ]")]
    [SerializeField, Min(0)] private int defaultRewardCredit = 1;

    private string lastProcessedScanId = string.Empty;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            return;
        }

        if (Instance != this)
        {
            Destroy(this);
        }
    }

    public void GenerateQuestsFromJSON(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            Debug.LogError("[QuestGenerator] 수신된 JSON 데이터가 비어 있습니다.");
            return;
        }

        try
        {
            YOLOScanData scanData = JsonUtility.FromJson<YOLOScanData>(jsonString);

            if (scanData == null)
            {
                Debug.LogError("[QuestGenerator] JSON을 YOLOScanData로 변환하지 못했습니다.");
                return;
            }

            if (scanData.candidate_objects == null)
            {
                scanData.candidate_objects = new List<YOLOObjectData>();
            }

            if (!string.IsNullOrWhiteSpace(scanData.scan_id) &&
                scanData.scan_id == lastProcessedScanId)
            {
                Debug.Log($"[QuestGenerator] 동일한 스캔 ID이므로 갱신하지 않습니다: {scanData.scan_id}");
                return;
            }

            lastProcessedScanId = scanData.scan_id ?? string.Empty;
            Debug.Log($"[QuestGenerator] 스캔 파싱 성공: {lastProcessedScanId}, 오브젝트 {scanData.candidate_objects.Count}개");

            OnObjectsDetected?.Invoke(scanData);
            GenerateQuestsFromYOLO(scanData);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[QuestGenerator] JSON 파싱 실패: {exception.Message}");
        }
    }

    public void GenerateQuestsFromYOLO(YOLOScanData scanData)
    {
        if (scanData == null)
        {
            Debug.LogError("[QuestGenerator] scanData가 null입니다.");
            return;
        }

        List<QuestData> generatedQuests = new List<QuestData>();
        List<YOLOObjectData> objects = scanData.candidate_objects ?? new List<YOLOObjectData>();

        for (int index = 0; index < objects.Count; index++)
        {
            YOLOObjectData rawObject = objects[index];
            if (rawObject == null)
            {
                continue;
            }

            string cleanedType = NormalizeQuestType(rawObject.class_name);
            QuestRewardPreset preset = FindPresetForType(cleanedType);

            generatedQuests.Add(new QuestData
            {
                questId = string.IsNullOrWhiteSpace(rawObject.object_id)
                    ? $"{scanData.scan_id}_{index}"
                    : rawObject.object_id,
                questType = cleanedType,
                confidence = Mathf.Clamp01(rawObject.confidence),
                isCleared = false,
                isRewardGiven = false,
                rewardCredit = Mathf.Max(0, preset.rewardCredit),
                suggestedAction = rawObject.suggested_action,
                userConfirmRequired = rawObject.user_confirm_required
            });
        }

        if (QuestProgressManager.Instance == null)
        {
            Debug.LogWarning("[QuestGenerator] QuestProgressManager가 없어 퀘스트를 전달하지 못했습니다.");
            return;
        }

        QuestProgressManager.Instance.SetupRoomAndQuests(scanData, generatedQuests);
        Debug.Log($"[QuestGenerator] 퀘스트 {generatedQuests.Count}개를 전달했습니다.");
    }

    private QuestRewardPreset FindPresetForType(string questType)
    {
        foreach (QuestRewardPreset preset in rewardPresets)
        {
            if (NormalizeQuestType(preset.questType) == questType)
            {
                return preset;
            }
        }

        return new QuestRewardPreset
        {
            questType = questType,
            rewardCredit = defaultRewardCredit
        };
    }

    private static string NormalizeQuestType(string rawType)
    {
        return string.IsNullOrWhiteSpace(rawType)
            ? "unknown"
            : rawType.Trim().ToLowerInvariant();
    }
}
