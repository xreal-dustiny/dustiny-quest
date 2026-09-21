using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Converts every confirmed AI detection into a QuestData entry.
/// The quest list is not limited to three items. Three is only the number
/// of objects the player must clear to finish the cleaning round.
/// </summary>
public class QuestGenerator : MonoBehaviour
{
    public static QuestGenerator Instance { get; private set; }
    public static event Action<YOLOScanData> OnObjectsDetected;

    [Header("[ 실제 게임 규칙 ]")]
    [Tooltip("레거시 필드입니다. 현재는 물건별 보상을 지급하지 않습니다.")]
    [SerializeField, Min(0)] private int creditPerCompletedObject = 0;
    [SerializeField] private string defaultSuggestedAction = "organize";
    [SerializeField] private bool sortByDetectionReliability = true;

    [Header("[ JSON 연동용 보상 프리셋 - 선택사항 ]")]
    [SerializeField] private List<QuestRewardPreset> rewardPresets = new List<QuestRewardPreset>();

    [Serializable]
    public struct QuestRewardPreset
    {
        public string questType;
        [Min(0)] public int rewardCredit;
    }

    private string lastProcessedScanId = string.Empty;
    private int runtimeScanSequence;

    public int CreditPerCompletedObject => 0;

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

    /// <summary>
    /// Converts all confirmed objects from the before-cleaning scan into quests.
    /// No three-item cap is applied here.
    /// </summary>
    public List<QuestData> GenerateQuestsFromConfirmedObjects(
        List<ConfirmedObjectInfo> confirmedObjects,
        int tidinessScore)
    {
        IEnumerable<ConfirmedObjectInfo> validObjects =
            (confirmedObjects ?? new List<ConfirmedObjectInfo>())
            .Where(item => item != null);

        if (sortByDetectionReliability)
        {
            validObjects = validObjects
                .OrderByDescending(item => item.detectionCount)
                .ThenByDescending(item => item.averageConfidence)
                .ThenBy(item => item.objectId);
        }

        List<ConfirmedObjectInfo> selectedObjects = validObjects.ToList();

        runtimeScanSequence++;
        string scanId = $"runtime_{DateTime.Now:yyyyMMdd_HHmmss}_{runtimeScanSequence}";

        List<QuestData> generatedQuests = new List<QuestData>();
        List<YOLOObjectData> yoloObjects = new List<YOLOObjectData>();

        for (int index = 0; index < selectedObjects.Count; index++)
        {
            ConfirmedObjectInfo detectedObject = selectedObjects[index];
            string normalizedType = NormalizeQuestType(detectedObject.className);
            Rect rect = detectedObject.lastRect;

            generatedQuests.Add(new QuestData
            {
                questId = $"{scanId}_{detectedObject.objectId}_{index}",
                questType = normalizedType,
                isCleared = false,
                isRewardGiven = false,
                rewardCredit = 0,
                sourceObjectId = detectedObject.objectId,
                sourceClassId = detectedObject.classId,
                sourceRect = rect,
                confidence = Mathf.Clamp01(detectedObject.averageConfidence),
                suggestedAction = defaultSuggestedAction,
                userConfirmRequired = true
            });

            yoloObjects.Add(new YOLOObjectData
            {
                object_id = detectedObject.objectId.ToString(),
                class_id = detectedObject.classId,
                class_name = normalizedType,
                confidence = Mathf.Clamp01(detectedObject.averageConfidence),
                bbox_2d = new BBox2D
                {
                    x1 = rect.xMin,
                    y1 = rect.yMin,
                    x2 = rect.xMax,
                    y2 = rect.yMax
                },
                suggested_action = defaultSuggestedAction,
                user_confirm_required = true
            });
        }

        YOLOScanData scanData = new YOLOScanData
        {
            scan_id = scanId,
            scan_type = "before_cleaning",
            area_type = "desk",
            candidate_objects = yoloObjects,
            scan_summary = new YOLOScanSummary
            {
                total_object_count = selectedObjects.Count,
                class_counts = selectedObjects
                    .GroupBy(item => NormalizeQuestType(item.className))
                    .Select(group => new ClassCountData
                    {
                        class_name = group.Key,
                        count = group.Count()
                    })
                    .ToList(),
                tidiness_score = Mathf.Clamp(tidinessScore, 0, 100),
                needs_cleaning = selectedObjects.Count > 0
            }
        };

        OnObjectsDetected?.Invoke(scanData);

        if (QuestProgressManager.Instance == null)
        {
            Debug.LogWarning("[QuestGenerator] QuestProgressManager가 없어 퀘스트를 전달하지 못했습니다.");
            return generatedQuests;
        }

        QuestProgressManager.Instance.SetupRoomAndQuests(scanData, generatedQuests);
        Debug.Log($"[QuestGenerator] AI가 인식한 전체 물체 {generatedQuests.Count}개를 미션 목록에 저장했습니다.");
        return generatedQuests;
    }

    /// <summary>
    /// Existing JSON input path. Every candidate object becomes a quest.
    /// </summary>
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
            OnObjectsDetected?.Invoke(scanData);
            GenerateQuestsFromYOLO(scanData);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[QuestGenerator] JSON 파싱 실패: {exception.Message}");
        }
    }

    public List<QuestData> GenerateQuestsFromYOLO(YOLOScanData scanData)
    {
        if (scanData == null)
        {
            Debug.LogError("[QuestGenerator] scanData가 null입니다.");
            return new List<QuestData>();
        }

        List<YOLOObjectData> objects = scanData.candidate_objects ?? new List<YOLOObjectData>();
        List<QuestData> generatedQuests = new List<QuestData>();

        foreach (YOLOObjectData rawObject in objects.Where(item => item != null))
        {
            string normalizedType = NormalizeQuestType(rawObject.class_name);
            int reward = 0;
            Rect rect = ConvertRect(rawObject.bbox_2d);

            generatedQuests.Add(new QuestData
            {
                questId = string.IsNullOrWhiteSpace(rawObject.object_id)
                    ? $"{scanData.scan_id}_{generatedQuests.Count}"
                    : $"{scanData.scan_id}_{rawObject.object_id}_{generatedQuests.Count}",
                questType = normalizedType,
                isCleared = false,
                isRewardGiven = false,
                rewardCredit = reward,
                sourceObjectId = ParseObjectId(rawObject.object_id),
                sourceClassId = rawObject.class_id,
                sourceRect = rect,
                confidence = Mathf.Clamp01(rawObject.confidence),
                suggestedAction = string.IsNullOrWhiteSpace(rawObject.suggested_action)
                    ? defaultSuggestedAction
                    : rawObject.suggested_action,
                userConfirmRequired = rawObject.user_confirm_required
            });
        }

        QuestProgressManager.Instance?.SetupRoomAndQuests(scanData, generatedQuests);
        Debug.Log($"[QuestGenerator] JSON의 전체 물체 {generatedQuests.Count}개를 미션 목록에 저장했습니다.");
        return generatedQuests;
    }

    private int ResolveReward(string questType)
    {
        // 물건별 보상은 사용하지 않습니다. 코인은 QuestProgressManager가
        // 재스캔으로 한 청소 라운드가 완료된 순간 한 번만 지급합니다.
        return 0;
    }

    private static Rect ConvertRect(BBox2D bbox)
    {
        if (bbox == null)
        {
            return default;
        }

        return Rect.MinMaxRect(bbox.x1, bbox.y1, bbox.x2, bbox.y2);
    }

    private static int ParseObjectId(string rawObjectId)
    {
        return int.TryParse(rawObjectId, out int parsed) ? parsed : 0;
    }

    public static string NormalizeQuestType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return "unknown";
        }

        string type = rawType.Trim().ToLowerInvariant();

        switch (type)
        {
            case "cup":
            case "bottle":
                return "cup_bottle";

            case "paper":
            case "book":
                return "paper_book";

            case "cell_phone":
            case "tablet":
            case "mouse":
                return "small_device";

            default:
                return type;
        }
    }
}
