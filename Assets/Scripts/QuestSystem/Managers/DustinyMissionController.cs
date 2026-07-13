using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Connects the real AI scan to the Dustiny quest flow.
///
/// Flow:
/// locked note -> before scan -> save every detected object -> note enabled
/// -> show three cards per page -> manual checkbox or after scan -> three clears -> +1 cleanliness
/// -> note locked again.
/// </summary>
public class DustinyMissionController : MonoBehaviour
{
    public enum MissionRuntimeState
    {
        Locked,
        BeforeScanning,
        Active,
        AfterScanning,
        Completed
    }

    [Header("[ 필수 시스템 연결 ]")]
    [SerializeField] private QuestCameraYoloTester questCameraYoloTester;
    [SerializeField] private QuestGenerator questGenerator;
    [SerializeField] private QuestProgressManager questProgressManager;
    [SerializeField] private DetectionMarkerManager detectionMarkerManager;
    [SerializeField] private QuestStatusUI questStatusUI;
    [SerializeField] private DustinyDemoFlow demoFlow;

    [Header("[ 버튼 연결 ]")]
    [Tooltip("하단 네비게이션 바의 더리 노트 버튼")]
    [SerializeField] private Button durryNoteNavigationButton;
    [Tooltip("더리 노트 안의 재스캔 버튼")]
    [SerializeField] private Button rescanButton;
    [SerializeField] private bool autoConnectRescanButton = true;

    [Header("[ 실제 퀘스트 규칙 ]")]
    [FormerlySerializedAs("requiredMissionCount")]
    [SerializeField, Min(1)] private int requiredClearedObjectCount = 3;
    [SerializeField] private bool autoOpenNoteAfterInitialScan = true;
    [SerializeField] private bool autoOpenNoteAfterRescan = true;
    [SerializeField] private bool showMissionMarkers = true;

    [Header("[ 재스캔 판정 ]")]
    [Tooltip("정리 전·후 같은 물체로 볼 최소 IoU입니다.")]
    [SerializeField, Range(0f, 1f)] private float matchingIouThreshold = 0.05f;
    [Tooltip("IoU가 낮아도 중심점이 이 거리(px) 안이면 같은 물체로 봅니다. YOLO 640 기준입니다.")]
    [SerializeField, Min(0f)] private float maximumCenterDistancePixels = 160f;

    [Header("[ 런타임 확인 ]")]
    [SerializeField] private MissionRuntimeState currentState = MissionRuntimeState.Locked;

    private readonly List<ConfirmedObjectInfo> initialMissionObjects = new List<ConfirmedObjectInfo>();
    private bool scannerSubscribed;
    private bool completionSubscribed;
    private bool rescanButtonBound;

    public MissionRuntimeState CurrentState => currentState;
    public bool CanOpenNote => currentState == MissionRuntimeState.Active;
    public bool CanRetryInitialScan => currentState == MissionRuntimeState.Locked;
    public bool IsScanning => currentState == MissionRuntimeState.BeforeScanning ||
                              currentState == MissionRuntimeState.AfterScanning;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeEvents();
        BindRescanButton();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeEvents();
        BindRescanButton();

        if (questProgressManager != null && questProgressManager.IsTodayMissionCompleted)
        {
            currentState = MissionRuntimeState.Completed;
        }
        else
        {
            currentState = MissionRuntimeState.Locked;
        }

        ApplyInteractionState();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        UnbindRescanButton();
    }

    private void BindRescanButton()
    {
        if (!autoConnectRescanButton || rescanButton == null || rescanButtonBound)
        {
            return;
        }

        rescanButton.onClick.RemoveListener(OnRescanPressed);
        rescanButton.onClick.AddListener(OnRescanPressed);
        rescanButtonBound = true;
    }

    private void UnbindRescanButton()
    {
        if (!rescanButtonBound || rescanButton == null)
        {
            return;
        }

        rescanButton.onClick.RemoveListener(OnRescanPressed);
        rescanButtonBound = false;
    }

    private void ResolveReferences()
    {
        if (questCameraYoloTester == null)
        {
            questCameraYoloTester = FindFirstObjectByType<QuestCameraYoloTester>();
        }

        if (questGenerator == null)
        {
            questGenerator = QuestGenerator.Instance != null
                ? QuestGenerator.Instance
                : FindFirstObjectByType<QuestGenerator>();
        }

        if (questProgressManager == null)
        {
            questProgressManager = QuestProgressManager.Instance != null
                ? QuestProgressManager.Instance
                : FindFirstObjectByType<QuestProgressManager>();
        }

        if (detectionMarkerManager == null)
        {
            detectionMarkerManager = FindFirstObjectByType<DetectionMarkerManager>();
        }

        if (questStatusUI == null)
        {
            questStatusUI = FindFirstObjectByType<QuestStatusUI>();
        }

        if (demoFlow == null)
        {
            demoFlow = FindFirstObjectByType<DustinyDemoFlow>();
        }
    }

    private void SubscribeEvents()
    {
        if (!scannerSubscribed && questCameraYoloTester != null)
        {
            questCameraYoloTester.OnScanResultReady += HandleScanResult;
            scannerSubscribed = true;
        }

        if (!completionSubscribed)
        {
            QuestProgressManager.OnQuestCleared += HandleQuestCleared;
            QuestProgressManager.OnCleaningRoundCompleted += HandleCleaningRoundCompleted;
            completionSubscribed = true;
        }
    }

    private void UnsubscribeEvents()
    {
        if (scannerSubscribed && questCameraYoloTester != null)
        {
            questCameraYoloTester.OnScanResultReady -= HandleScanResult;
        }

        scannerSubscribed = false;

        if (completionSubscribed)
        {
            QuestProgressManager.OnQuestCleared -= HandleQuestCleared;
            QuestProgressManager.OnCleaningRoundCompleted -= HandleCleaningRoundCompleted;
        }

        completionSubscribed = false;
    }

    /// <summary>
    /// Opening의 [확인] 버튼 또는 실제 미션 시작 버튼에서 호출한다.
    /// </summary>
    public void BeginMissionScan()
    {
        ResolveReferences();
        SubscribeEvents();

        if (questProgressManager != null && questProgressManager.IsTodayMissionCompleted)
        {
            HandleAlreadyCompleted();
            return;
        }

        if (questCameraYoloTester == null || questGenerator == null || questProgressManager == null)
        {
            Debug.LogError("[DustinyMission] AI 또는 퀘스트 시스템 연결이 부족합니다.");
            demoFlow?.ShowDescriptionMessage("스캔 시스템 연결을 확인해줘!");
            return;
        }

        if (questCameraYoloTester.IsScanning)
        {
            return;
        }

        demoFlow?.CloseBigNote();
        detectionMarkerManager?.ClearMarkers();
        initialMissionObjects.Clear();
        questProgressManager.ResetCurrentQuestSession();
        questCameraYoloTester.ResetScoreComparison();

        currentState = MissionRuntimeState.BeforeScanning;
        ApplyInteractionState();

        demoFlow?.ShowDescriptionMessage(
            "책상을 바라봐 줘!\n정리할 물건을 찾는 중이야..."
        );
        demoFlow?.PlayDurryExpression("Focused");

        bool started = questCameraYoloTester.TryStartBeforeScan();
        if (!started)
        {
            currentState = MissionRuntimeState.Locked;
            ApplyInteractionState();
            demoFlow?.ShowDescriptionMessage(
                "카메라 권한이나 AI 연결을 확인한 뒤\n[확인]을 눌러 다시 스캔해줘!"
            );
        }
    }

    /// <summary>
    /// 더리 노트의 재스캔 버튼에 연결한다.
    /// </summary>
    public void OnRescanPressed()
    {
        ResolveReferences();

        if (currentState == MissionRuntimeState.Locked)
        {
            BeginMissionScan();
            return;
        }

        if (currentState != MissionRuntimeState.Active ||
            questCameraYoloTester == null ||
            questCameraYoloTester.IsScanning)
        {
            return;
        }

        demoFlow?.CloseBigNote();
        detectionMarkerManager?.ClearMarkers();

        currentState = MissionRuntimeState.AfterScanning;
        ApplyInteractionState();
        demoFlow?.ShowDescriptionMessage("정리한 결과를 다시 확인하는 중이야...");
        demoFlow?.PlayDurryExpression("Focused");

        bool started = questCameraYoloTester.TryStartAfterScan();
        if (!started)
        {
            currentState = MissionRuntimeState.Active;
            ApplyInteractionState();
            RefreshMarkersForIncompleteQuests();
            demoFlow?.ShowDescriptionMessage("재스캔을 시작하지 못했어. 잠시 후 다시 눌러줘!");
        }
    }

    /// <summary>
    /// Opening의 공용 [확인] 버튼이 MissionInProgress 상태에서 눌렸을 때 호출한다.
    /// 최초 스캔 실패 상태면 다시 시도하고, 활성 상태면 노트를 연다.
    /// </summary>
    public void HandleMissionConfirmPressed()
    {
        if (currentState == MissionRuntimeState.Locked)
        {
            BeginMissionScan();
        }
        else if (currentState == MissionRuntimeState.Active)
        {
            demoFlow?.OpenNotePage();
        }
    }

    private void HandleScanResult(ScanPhase phase, List<ConfirmedObjectInfo> objects, int score)
    {
        if (phase == ScanPhase.BeforeCleaning)
        {
            HandleBeforeScanResult(objects, score);
        }
        else
        {
            HandleAfterScanResult(objects, score);
        }
    }

    private void HandleBeforeScanResult(List<ConfirmedObjectInfo> objects, int score)
    {
        List<ConfirmedObjectInfo> validObjects = objects?
            .Where(item => item != null)
            .ToList() ?? new List<ConfirmedObjectInfo>();

        if (validObjects.Count < requiredClearedObjectCount)
        {
            currentState = MissionRuntimeState.Locked;
            ApplyInteractionState();
            detectionMarkerManager?.ClearMarkers();
            demoFlow?.ShowDescriptionMessage(
                $"정리할 물건이 {requiredClearedObjectCount}개보다 적게 인식됐어.\n" +
                "책상 전체가 보이도록 바라보고 [확인]을 눌러 다시 스캔해줘!"
            );
            demoFlow?.PlayDurryExpression("Woried");
            return;
        }

        List<QuestData> quests = questGenerator.GenerateQuestsFromConfirmedObjects(
            validObjects,
            score
        );

        if (quests.Count < requiredClearedObjectCount)
        {
            currentState = MissionRuntimeState.Locked;
            ApplyInteractionState();
            demoFlow?.ShowDescriptionMessage("미션 생성에 실패했어. [확인]을 눌러 다시 스캔해줘!");
            return;
        }

        initialMissionObjects.Clear();
        HashSet<int> generatedIds = quests.Select(quest => quest.sourceObjectId).ToHashSet();
        initialMissionObjects.AddRange(validObjects.Where(item => generatedIds.Contains(item.objectId)));

        currentState = MissionRuntimeState.Active;
        ApplyInteractionState();
        RefreshMarkersForIncompleteQuests();

        demoFlow?.ShowDescriptionMessage(
            $"정리 대상 {quests.Count}개를 찾았어!\n더리 노트에는 3개씩 보여. 이 중 {requiredClearedObjectCount}개를 정리한 뒤 재스캔해줘."
        );
        demoFlow?.PlayDurryExpression("Look");

        if (autoOpenNoteAfterInitialScan)
        {
            demoFlow?.OpenNotePage();
        }
    }

    private void HandleAfterScanResult(List<ConfirmedObjectInfo> afterObjects, int score)
    {
        if (questProgressManager == null || questProgressManager.IsTodayMissionCompleted)
        {
            return;
        }

        List<string> removedQuestIds = FindRemovedQuestIds(afterObjects ?? new List<ConfirmedObjectInfo>());

        foreach (string questId in removedQuestIds)
        {
            if (questProgressManager.IsTodayMissionCompleted)
            {
                break;
            }

            questProgressManager.ClearQuest(questId);
        }

        if (questProgressManager.IsTodayMissionCompleted)
        {
            return;
        }

        currentState = MissionRuntimeState.Active;
        ApplyInteractionState();
        RefreshMarkersForIncompleteQuests();

        int clearedCount = removedQuestIds.Count;
        demoFlow?.ShowDescriptionMessage(
            clearedCount > 0
                ? $"재스캔으로 {clearedCount}개를 완료했어!\n남은 미션도 확인해보자."
                : "아직 사라진 미션 물건을 찾지 못했어.\n직접 체크하거나 정리 후 다시 스캔해줘!"
        );

        if (autoOpenNoteAfterRescan)
        {
            demoFlow?.OpenNotePage();
        }
    }

    private List<string> FindRemovedQuestIds(List<ConfirmedObjectInfo> afterObjects)
    {
        List<QuestData> incompleteQuests = questProgressManager.GetIncompleteQuests();
        List<string> removedQuestIds = new List<string>();

        IEnumerable<IGrouping<string, QuestData>> questGroups = incompleteQuests
            .GroupBy(quest => QuestGenerator.NormalizeQuestType(quest.questType));

        foreach (IGrouping<string, QuestData> questGroup in questGroups)
        {
            List<QuestData> unmatchedQuests = questGroup.ToList();
            List<ConfirmedObjectInfo> unmatchedAfter = afterObjects
                .Where(item => item != null &&
                    QuestGenerator.NormalizeQuestType(item.className) == questGroup.Key)
                .ToList();

            while (unmatchedQuests.Count > 0 && unmatchedAfter.Count > 0)
            {
                int bestQuestIndex = -1;
                int bestAfterIndex = -1;
                float bestScore = float.NegativeInfinity;

                for (int questIndex = 0; questIndex < unmatchedQuests.Count; questIndex++)
                {
                    for (int afterIndex = 0; afterIndex < unmatchedAfter.Count; afterIndex++)
                    {
                        Rect beforeRect = unmatchedQuests[questIndex].sourceRect;
                        Rect afterRect = unmatchedAfter[afterIndex].lastRect;
                        float iou = CalculateIoU(beforeRect, afterRect);
                        float centerDistance = Vector2.Distance(beforeRect.center, afterRect.center);

                        bool isSameObject = iou >= matchingIouThreshold ||
                                            centerDistance <= maximumCenterDistancePixels;
                        if (!isSameObject)
                        {
                            continue;
                        }

                        float distanceScore = 1f - Mathf.Clamp01(
                            centerDistance / Mathf.Max(1f, maximumCenterDistancePixels)
                        );
                        float score = iou * 2f + distanceScore;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestQuestIndex = questIndex;
                            bestAfterIndex = afterIndex;
                        }
                    }
                }

                if (bestQuestIndex < 0 || bestAfterIndex < 0)
                {
                    break;
                }

                unmatchedQuests.RemoveAt(bestQuestIndex);
                unmatchedAfter.RemoveAt(bestAfterIndex);
            }

            removedQuestIds.AddRange(unmatchedQuests.Select(quest => quest.questId));
        }

        return removedQuestIds;
    }

    private void HandleQuestCleared(QuestData clearedQuest)
    {
        if (currentState == MissionRuntimeState.Active || currentState == MissionRuntimeState.AfterScanning)
        {
            RefreshMarkersForIncompleteQuests();
        }
    }

    private void HandleCleaningRoundCompleted()
    {
        currentState = MissionRuntimeState.Completed;
        ApplyInteractionState();
        detectionMarkerManager?.ClearMarkers();
        questCameraYoloTester?.ResetScoreComparison();
        demoFlow?.CloseBigNote();
        demoFlow?.NotifyMissionCompleted();

        int cleanliness = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 0;
        int credit = CreditManager.Instance != null
            ? CreditManager.Instance.CurrentCredit
            : 0;

        demoFlow?.ShowDescriptionMessage(
            $"퀘스트 완료!\n물건 {requiredClearedObjectCount}개를 정리해서 보송력 +1!\n현재 {cleanliness} / 4 · 보유 {credit} CR"
        );
        demoFlow?.PlayDurryExpression("Joyful");
    }

    private void HandleAlreadyCompleted()
    {
        currentState = MissionRuntimeState.Completed;
        ApplyInteractionState();
        demoFlow?.CloseBigNote();
        demoFlow?.ShowDescriptionMessage("오늘의 퀘스트는 이미 완료했어!");
        demoFlow?.PlayDurryExpression("Joyful");
    }

    private void RefreshMarkersForIncompleteQuests()
    {
        if (!showMissionMarkers || detectionMarkerManager == null || questProgressManager == null)
        {
            return;
        }

        HashSet<int> incompleteSourceIds = questProgressManager.GetIncompleteQuests()
            .Select(quest => quest.sourceObjectId)
            .ToHashSet();

        List<ConfirmedObjectInfo> markerObjects = initialMissionObjects
            .Where(item => item != null && incompleteSourceIds.Contains(item.objectId))
            .ToList();

        if (markerObjects.Count == 0)
        {
            detectionMarkerManager.ClearMarkers();
        }
        else
        {
            detectionMarkerManager.ShowMarkers(markerObjects);
        }
    }

    private void ApplyInteractionState()
    {
        bool missionActive = currentState == MissionRuntimeState.Active;

        if (durryNoteNavigationButton != null)
        {
            durryNoteNavigationButton.interactable = missionActive;
        }

        if (rescanButton != null)
        {
            rescanButton.interactable = missionActive;
            rescanButton.gameObject.SetActive(missionActive);
        }

        questStatusUI?.SetMissionInteractionEnabled(missionActive);
    }

    private static float CalculateIoU(Rect first, Rect second)
    {
        float overlapWidth = Mathf.Max(0f, Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin));
        float overlapHeight = Mathf.Max(0f, Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin));
        float intersectionArea = overlapWidth * overlapHeight;
        float firstArea = Mathf.Max(0f, first.width) * Mathf.Max(0f, first.height);
        float secondArea = Mathf.Max(0f, second.width) * Mathf.Max(0f, second.height);
        float unionArea = firstArea + secondArea - intersectionArea;
        return unionArea > 0f ? intersectionArea / unionArea : 0f;
    }
}
