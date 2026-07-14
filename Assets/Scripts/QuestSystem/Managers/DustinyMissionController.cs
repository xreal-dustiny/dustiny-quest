using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Connects the AI scan to the Dustiny three-round daily mission.
///
/// Opening complete -> mission note unlocked -> start scan from note
/// -> clean up to three objects -> confirm by checkbox or after-scan inspection
/// -> one round reward -> repeat until today's three rounds are complete.
/// </summary>
public class DustinyMissionController : MonoBehaviour
{
    public enum MissionRuntimeState
    {
        Locked = 0,
        BeforeScanning = 1,
        Active = 2,
        AfterScanning = 3,
        Completed = 4,
        Ready = 5,
        AwaitingMissionConfirmation = 6,
        AwaitingMissionStart = 7
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

    [Tooltip("선택사항입니다. 활성 미션의 검사 재스캔 버튼입니다. 연결하지 않아도 오른손 약지 핀치로 검사 재스캔이 동작합니다.")]
    [SerializeField] private Button rescanButton;

    [Tooltip("RescanButton이 비어 있으면 비활성 오브젝트까지 포함해 이름에 scan/rescan/inspect가 들어간 Button을 자동으로 찾습니다.")]
    [SerializeField] private bool autoFindRescanButton = true;

    [SerializeField] private bool autoConnectRescanButton = true;

    [Header("[ 실제 퀘스트 규칙 ]")]
    [FormerlySerializedAs("requiredMissionCount")]
    [Tooltip("3개 이상 인식되면 3개, 1~2개만 인식되면 인식된 개수만큼 정리하면 한 라운드가 완료됩니다.")]
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
    [SerializeField] private bool missionNoteUnlocked;
    [SerializeField, Min(1)] private int currentRoundRequiredCount = 3;

    private readonly List<ConfirmedObjectInfo> initialMissionObjects = new List<ConfirmedObjectInfo>();
    private readonly List<ConfirmedObjectInfo> pendingMissionObjects = new List<ConfirmedObjectInfo>();
    private int pendingMissionScore;
    private bool scannerSubscribed;
    private bool completionSubscribed;
    private bool rescanButtonBound;
    private Coroutine initialScanCoroutine;

    public MissionRuntimeState CurrentState => currentState;
    public bool IsMissionNoteUnlocked => missionNoteUnlocked;

    // A note with mission cards opens only after the scan has created a real round.
    public bool CanOpenNote => missionNoteUnlocked &&
                               (currentState == MissionRuntimeState.Active ||
                                currentState == MissionRuntimeState.Completed);

    public bool CanRetryInitialScan => missionNoteUnlocked &&
                                       currentState == MissionRuntimeState.Ready;

    public bool IsAwaitingMissionStart =>
        currentState == MissionRuntimeState.AwaitingMissionStart;

    public bool IsAwaitingMissionConfirmation =>
        currentState == MissionRuntimeState.AwaitingMissionConfirmation;

    // The first scan starts with the index-pinch mission confirmation.
    // Ring pinch is reserved for the inspection rescan of an active mission.
    public bool CanHandleRingPinchScan => missionNoteUnlocked &&
                                          currentState == MissionRuntimeState.Active;
    public int PendingDetectedObjectCount => pendingMissionObjects.Count;
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

        // The note stays locked until DustinyDemoFlow explicitly finishes the opening.
        // If DemoFlow unlocked it earlier in the same frame, do not overwrite that state.
        if (!missionNoteUnlocked)
        {
            currentState = MissionRuntimeState.Locked;
        }
        else
        {
            // 오늘의 3회 보상을 모두 받았더라도 추가 미션은 계속 시작할 수 있습니다.
            currentState = MissionRuntimeState.Ready;
        }

        ApplyInteractionState();
    }

    private void OnDisable()
    {
        if (initialScanCoroutine != null)
        {
            StopCoroutine(initialScanCoroutine);
            initialScanCoroutine = null;
        }

        UnsubscribeEvents();
        UnbindRescanButton();
    }

    /// <summary>
    /// Called exactly when Durry's opening explanation has finished.
    /// This enables the NOTE navigation button, but does not open the page yet.
    /// </summary>
    public void UnlockMissionNoteAfterOpening()
    {
        ResolveReferences();
        BindRescanButton();
        missionNoteUnlocked = true;
        ClearPendingMissionScan();

        // 보상 한도와 미션 시작 가능 여부를 분리합니다.
        // 3/3 이후에도 NOTE를 눌러 보상 없는 추가 미션을 시작할 수 있습니다.
        currentState = MissionRuntimeState.Ready;

        demoFlow?.SetScanZoneVisible(false);
        ApplyInteractionState();

        Debug.Log(
            questProgressManager != null && questProgressManager.HasReachedDailyRewardLimit
                ? "[DustinyMission] 오프닝 종료: 오늘 보상은 3/3 완료됐지만 추가 미션은 가능합니다."
                : "[DustinyMission] 오프닝 종료: NOTE 버튼을 활성화했습니다. 미션은 아직 시작 전입니다."
        );
    }

    /// <summary>
    /// Called whenever the NOTE navigation button is pressed.
    /// If no cleaning round is active, the note remains closed and a start prompt is shown.
    /// </summary>
    public bool RequestOpenMissionNote()
    {
        ResolveReferences();

        if (!missionNoteUnlocked)
        {
            demoFlow?.ShowDescriptionMessage(
                "더리의 설명을 끝까지 들으면 오늘의 미션을 시작할 수 있어!"
            );
            return false;
        }

        if (currentState == MissionRuntimeState.Active)
        {
            return true;
        }

        if (currentState == MissionRuntimeState.Ready ||
            currentState == MissionRuntimeState.Completed ||
            currentState == MissionRuntimeState.AwaitingMissionStart)
        {
            currentState = MissionRuntimeState.AwaitingMissionStart;
            ApplyInteractionState();
            demoFlow?.CloseBigNote();

            int completed = questProgressManager != null
                ? questProgressManager.CompletedRoundsToday
                : 0;
            int required = questProgressManager != null
                ? questProgressManager.RequiredCleaningRoundsPerDay
                : 3;
            bool rewardLimitReached = questProgressManager != null &&
                                      questProgressManager.HasReachedDailyRewardLimit;

            string prompt = rewardLimitReached
                ? "오늘의 보상 미션 3회를 모두 완료했어!\n" +
                  "이제부터는 보상 없이 추가 미션을 진행하게 돼.\n" +
                  "그래도 계속 진행할래?\n" +
                  "검지 핀치: 진행"
                : $"오늘의 미션을 진행할래?\n" +
                  $"현재 오늘의 미션 {completed}/{required}회 완료!\n" +
                  "검지 핀치로 미션을 시작해줘.";

            demoFlow?.ShowDescriptionMessage(prompt);
            demoFlow?.PlayDurryExpression(rewardLimitReached ? "Look" : "Focused");
            return false;
        }

        if (IsScanning)
        {
            demoFlow?.ShowDescriptionMessage(
                currentState == MissionRuntimeState.BeforeScanning
                    ? "책상 스캔 중이야!\n잠시만 기다려줘."
                    : "정리 결과를 검사하는 중이야!\n잠시만 기다려줘."
            );
        }

        return false;
    }

    /// <summary>
    /// Confirms the daily-mission prompt with an index pinch and starts the first scan.
    /// </summary>
    public void ConfirmMissionStartFromPrompt()
    {
        if (currentState != MissionRuntimeState.AwaitingMissionStart)
        {
            return;
        }

        demoFlow?.HideDialoguePanels();
        currentState = MissionRuntimeState.Ready;
        ApplyInteractionState();
        BeginMissionScan();
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

        if (rescanButton == null && autoFindRescanButton)
        {
            rescanButton = FindScanOrInspectButton();
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
    /// Starts the first scan for a new cleaning round.
    /// The request begins one frame later so the "책상 스캔 중" description renders first.
    /// </summary>
    public void BeginMissionScan()
    {
        ResolveReferences();
        SubscribeEvents();

        if (!missionNoteUnlocked)
        {
            demoFlow?.ShowDescriptionMessage(
                "더리의 설명을 끝까지 들으면 오늘의 미션을 시작할 수 있어!"
            );
            return;
        }

        if (currentState != MissionRuntimeState.Ready)
        {
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
        demoFlow?.SetScanZoneVisible(true);
        detectionMarkerManager?.ClearMarkers();
        initialMissionObjects.Clear();
        ClearPendingMissionScan();
        questProgressManager.ResetCurrentQuestSession();
        questCameraYoloTester.ResetScoreComparison();
        currentRoundRequiredCount = requiredClearedObjectCount;

        currentState = MissionRuntimeState.BeforeScanning;
        ApplyInteractionState();

        demoFlow?.ShowDescriptionMessage(
            "책상 스캔 중...\n책상 전체가 보이도록 바라봐 줘!"
        );
        demoFlow?.PlayDurryExpression("Focused");

        if (initialScanCoroutine != null)
        {
            StopCoroutine(initialScanCoroutine);
        }

        initialScanCoroutine = StartCoroutine(StartBeforeScanNextFrame());
    }

    private IEnumerator StartBeforeScanNextFrame()
    {
        yield return null;

        if (currentState != MissionRuntimeState.BeforeScanning)
        {
            initialScanCoroutine = null;
            yield break;
        }

        if (questCameraYoloTester == null)
        {
            currentState = MissionRuntimeState.Ready;
            ApplyInteractionState();
            demoFlow?.SetScanZoneVisible(false);
            demoFlow?.ShowDescriptionMessage("카메라 스캔 오브젝트가 연결되지 않았어!");
            initialScanCoroutine = null;
            yield break;
        }

        if (!questCameraYoloTester.gameObject.activeSelf)
        {
            questCameraYoloTester.gameObject.SetActive(true);
        }

        bool started = questCameraYoloTester.TryStartBeforeScan();
        initialScanCoroutine = null;

        if (started)
        {
            Debug.Log("[DustinyMission] 최초 책상 스캔을 시작했습니다.");
            yield break;
        }

        currentState = MissionRuntimeState.Ready;
        demoFlow?.SetScanZoneVisible(false);
        ApplyInteractionState();
        demoFlow?.ShowDescriptionMessage(
            "스캔을 시작하지 못했어.\n" +
            "카메라 권한과 AI 연결을 확인한 뒤 NOTE를 눌러 다시 시작해줘!"
        );
        demoFlow?.PlayDurryExpression("Worried");
    }

    /// <summary>
    /// Starts the after-cleaning inspection scan for an active mission.
    /// </summary>
    public void OnRescanPressed()
    {
        ResolveReferences();

        if (currentState != MissionRuntimeState.Active ||
            questCameraYoloTester == null ||
            questCameraYoloTester.IsScanning)
        {
            return;
        }

        demoFlow?.CloseBigNote();
        demoFlow?.SetScanZoneVisible(true);
        detectionMarkerManager?.ClearMarkers();

        currentState = MissionRuntimeState.AfterScanning;
        ApplyInteractionState();
        demoFlow?.ShowDescriptionMessage("정리한 결과를 다시 확인하는 중이야...");
        demoFlow?.PlayDurryExpression("Focused");

        bool started = questCameraYoloTester.TryStartAfterScan();
        if (!started)
        {
            currentState = MissionRuntimeState.Active;
            demoFlow?.SetScanZoneVisible(false);
            ApplyInteractionState();
            RefreshMarkersForIncompleteQuests();
            demoFlow?.ShowDescriptionMessage("재스캔을 시작하지 못했어. 잠시 후 약지 핀치로 다시 시도해줘!");
        }
    }

    /// <summary>
    /// DustinyDemoFlow의 오른손 약지 핀치에서 호출됩니다.
    /// Active 상태에서 검사 재스캔을 실행합니다.
    /// 버튼이 연결되어 있지 않아도 이 입력은 항상 사용할 수 있습니다.
    /// </summary>
    public void HandleMissionScanRingPinchPressed()
    {
        if (!CanHandleRingPinchScan)
        {
            return;
        }

        OnRescanPressed();
    }

    /// <summary>
    /// Called by the common confirm input only when no dialogue is currently open.
    /// </summary>
    public void HandleMissionConfirmPressed()
    {
        if (currentState == MissionRuntimeState.AwaitingMissionStart)
        {
            ConfirmMissionStartFromPrompt();
        }
        else if (currentState == MissionRuntimeState.AwaitingMissionConfirmation)
        {
            ConfirmPendingMissionScan();
        }
        else if (currentState == MissionRuntimeState.Ready)
        {
            RequestOpenMissionNote();
        }
        else if (currentState == MissionRuntimeState.Active)
        {
            demoFlow?.OpenNotePage();
        }
        else if (currentState == MissionRuntimeState.Completed)
        {
            RequestOpenMissionNote();
        }
    }

    /// <summary>
    /// 오른손 중지 핀치로 현재 탐지 결과를 버리고 미션 생성용 스캔을 다시 시작합니다.
    /// 탐지 결과 확인 화면이 아닐 때는 아무 동작도 하지 않습니다.
    /// </summary>
    public void HandleMissionRetryPinchPressed()
    {
        if (currentState != MissionRuntimeState.AwaitingMissionConfirmation)
        {
            return;
        }

        RetryPendingMissionScan();
    }

    private void HandleScanResult(ScanPhase phase, List<ConfirmedObjectInfo> objects, int score)
    {
        initialScanCoroutine = null;
        demoFlow?.SetScanZoneVisible(false);

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

        pendingMissionObjects.Clear();
        pendingMissionObjects.AddRange(validObjects);
        pendingMissionScore = score;

        currentState = MissionRuntimeState.AwaitingMissionConfirmation;
        ApplyInteractionState();

        if (showMissionMarkers && detectionMarkerManager != null && pendingMissionObjects.Count > 0)
        {
            detectionMarkerManager.ShowMarkers(pendingMissionObjects);
        }
        else
        {
            detectionMarkerManager?.ClearMarkers();
        }

        string diagnostic = questCameraYoloTester != null &&
                            !string.IsNullOrWhiteSpace(questCameraYoloTester.LastScanFailureReason)
            ? $"\n({questCameraYoloTester.LastScanFailureReason})"
            : string.Empty;

        if (questGenerator == null || questProgressManager == null)
        {
            demoFlow?.ShowDescriptionMessage("미션 시스템 연결을 확인해줘!");
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        if (pendingMissionObjects.Count == 0)
        {
            demoFlow?.ShowDescriptionMessage(
                "탐지 완료!\n정리할 물건을 찾지 못했어.\n" +
                "책상 전체가 보이도록 한 뒤 중지 핀치로 다시 스캔해줘." +
                diagnostic
            );
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        demoFlow?.ShowDescriptionMessage(
            $"탐지 완료!\n{pendingMissionObjects.Count}개의 물건이 탐지됐어.\n" +
            "바로 미션 노트에 추가할까?\n" +
            "검지 핀치: 추가 · 중지 핀치: 다시 스캔"
        );
        demoFlow?.PlayDurryExpression("Look");
    }

    /// <summary>
    /// 검지 핀치로 현재 탐지 결과를 승인하면 그때 처음으로 미션 목록을 생성합니다.
    /// </summary>
    public void ConfirmPendingMissionScan()
    {
        ResolveReferences();

        if (currentState != MissionRuntimeState.AwaitingMissionConfirmation)
        {
            return;
        }

        if (questGenerator == null || questProgressManager == null)
        {
            demoFlow?.ShowDescriptionMessage("미션 시스템 연결을 확인해줘!");
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        if (pendingMissionObjects.Count == 0)
        {
            demoFlow?.ShowDescriptionMessage(
                "추가할 물건이 없어.\n중지 핀치로 미션 만들기 스캔을 다시 해줘!"
            );
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        List<ConfirmedObjectInfo> acceptedObjects = pendingMissionObjects.ToList();
        int acceptedScore = pendingMissionScore;

        List<QuestData> quests = questGenerator.GenerateQuestsFromConfirmedObjects(
            acceptedObjects,
            acceptedScore
        );

        if (quests.Count == 0)
        {
            currentState = MissionRuntimeState.AwaitingMissionConfirmation;
            ApplyInteractionState();
            demoFlow?.ShowDescriptionMessage(
                "탐지 결과를 미션으로 만들지 못했어.\n중지 핀치로 다시 스캔해줘!"
            );
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        int configuredMaximum = Mathf.Max(1, requiredClearedObjectCount);
        currentRoundRequiredCount = Mathf.Clamp(
            quests.Count,
            1,
            configuredMaximum
        );
        questProgressManager.ConfigureRequiredQuestCountForCurrentRound(
            currentRoundRequiredCount
        );

        initialMissionObjects.Clear();
        HashSet<int> generatedIds = quests
            .Select(quest => quest.sourceObjectId)
            .ToHashSet();
        initialMissionObjects.AddRange(
            acceptedObjects.Where(item => generatedIds.Contains(item.objectId))
        );

        ClearPendingMissionScan();
        currentState = MissionRuntimeState.Active;
        ApplyInteractionState();
        questStatusUI?.RefreshMissionList(true);
        RefreshMarkersForIncompleteQuests();

        string missionMessage = quests.Count <= configuredMaximum
            ? $"미션 노트에 {quests.Count}개를 추가했어!\n" +
              $"{currentRoundRequiredCount}개를 정리한 뒤 체크박스로 완료하거나 오른손 약지 핀치로 검사받아줘."
            : $"미션 노트에 정리 대상 {quests.Count}개를 추가했어!\n" +
              $"전체 목록 중 {currentRoundRequiredCount}개를 정리하고 체크박스 또는 오른손 약지 핀치로 확인해줘.";

        demoFlow?.ShowDescriptionMessage(missionMessage);
        demoFlow?.PlayDurryExpression("Look");

        if (autoOpenNoteAfterInitialScan)
        {
            demoFlow?.OpenNotePage();
        }
    }

    /// <summary>
    /// 중지 핀치로 승인 대기 중인 탐지 결과를 폐기하고 같은 미션 생성 스캔을 다시 실행합니다.
    /// </summary>
    public void RetryPendingMissionScan()
    {
        ResolveReferences();

        if (currentState != MissionRuntimeState.AwaitingMissionConfirmation)
        {
            return;
        }

        ClearPendingMissionScan();
        detectionMarkerManager?.ClearMarkers();
        currentState = MissionRuntimeState.Ready;
        ApplyInteractionState();
        demoFlow?.PlayDurryExpression("Focused");

        BeginMissionScan();
    }

    private void ClearPendingMissionScan()
    {
        pendingMissionObjects.Clear();
        pendingMissionScore = 0;
    }

    private void HandleAfterScanResult(List<ConfirmedObjectInfo> afterObjects, int score)
    {
        if (questProgressManager == null)
        {
            return;
        }

        // A failed camera/AI request must never be interpreted as "every object disappeared".
        // Only a successfully completed scan is allowed to clear missing objects.
        string scanFailureReason = questCameraYoloTester != null
            ? questCameraYoloTester.LastScanFailureReason
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(scanFailureReason))
        {
            currentState = MissionRuntimeState.Active;
            ApplyInteractionState();
            RefreshMarkersForIncompleteQuests();
            demoFlow?.ShowDescriptionMessage(
                "검사 스캔에 실패해서 자동 완료하지 않았어.\n" +
                "다시 검사받거나, AI가 놓친 물건은 노트 체크박스로 직접 완료해줘!\n" +
                $"({scanFailureReason})"
            );
            demoFlow?.PlayDurryExpression("Worried");
            return;
        }

        List<string> removedQuestIds = FindRemovedQuestIds(
            afterObjects ?? new List<ConfirmedObjectInfo>()
        );

        int acceptedClearedCount = 0;

        foreach (string questId in removedQuestIds)
        {
            if (questProgressManager.IsCurrentRoundCompleted)
            {
                break;
            }

            if (questProgressManager.ClearQuest(questId))
            {
                acceptedClearedCount++;
            }
        }

        // 라운드 완료 이벤트가 이미 컨트롤러를 Ready로 옮겼다면 여기서 종료합니다.
        if (questProgressManager.IsCurrentRoundCompleted)
        {
            return;
        }

        currentState = MissionRuntimeState.Active;
        ApplyInteractionState();
        RefreshMarkersForIncompleteQuests();

        int remaining = Mathf.Max(
            0,
            questProgressManager.CurrentRoundRequiredObjectCount -
            questProgressManager.CurrentRoundClearedObjectCount
        );

        demoFlow?.ShowDescriptionMessage(
            acceptedClearedCount > 0
                ? $"재스캔으로 {acceptedClearedCount}개를 확인했어!\n이번 미션은 {remaining}개 남았어."
                : "아직 치운 물건을 확인하지 못했어.\n다시 검사받거나 노트 체크박스로 직접 완료해줘!"
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
                        float matchScore = iou * 2f + distanceScore;

                        if (matchScore > bestScore)
                        {
                            bestScore = matchScore;
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
        if (currentState == MissionRuntimeState.Active ||
            currentState == MissionRuntimeState.AfterScanning)
        {
            RefreshMarkersForIncompleteQuests();
        }
    }

    private void HandleCleaningRoundCompleted()
    {
        ResolveReferences();

        bool todayCompleted = questProgressManager != null &&
                              questProgressManager.IsTodayMissionCompleted;
        bool rewardGranted = questProgressManager != null &&
                             questProgressManager.LastCompletedRoundGrantedReward;

        // 라운드가 끝나면 항상 Ready로 돌아갑니다.
        // 오늘 보상 3/3 완료 여부는 다음 시작 안내 문구와 보상 지급에만 사용합니다.
        currentState = MissionRuntimeState.Ready;

        ClearPendingMissionScan();
        ApplyInteractionState();
        detectionMarkerManager?.ClearMarkers();
        questCameraYoloTester?.ResetScoreComparison();
        demoFlow?.SetScanZoneVisible(false);
        demoFlow?.CloseBigNote();
        demoFlow?.NotifyMissionCompleted();

        int cleanliness = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 0;
        int credit = CreditManager.Instance != null
            ? CreditManager.Instance.CurrentCredit
            : 0;
        int completedRounds = questProgressManager != null
            ? questProgressManager.CompletedRoundsToday
            : 0;
        int totalRounds = questProgressManager != null
            ? questProgressManager.RequiredCleaningRoundsPerDay
            : 3;
        int reward = questProgressManager != null
            ? questProgressManager.LastCompletedRoundRewardCredit
            : 0;
        int remainingRounds = Mathf.Max(0, totalRounds - completedRounds);

        string message;
        if (!rewardGranted)
        {
            message =
                $"추가 미션 완료!\n물건 {currentRoundRequiredCount}개 정리 확인!\n" +
                "오늘의 3회 보상은 이미 모두 받아서 이번에는 추가 보상이 없어.";
        }
        else if (todayCompleted)
        {
            message =
                $"오늘의 미션 완료!\n청소 미션 {completedRounds}/{totalRounds}회를 모두 끝냈어.\n" +
                $"이번 보상 +{reward} CR · 보송력 {cleanliness}/4 · 보유 {credit} CR\n" +
                "NOTE를 다시 누르면 보상 없이 추가 미션을 계속할 수 있어!";
        }
        else
        {
            message =
                $"청소 미션 1회 완료!\n물건 {currentRoundRequiredCount}개 정리 확인, +{reward} CR!\n" +
                $"오늘 {completedRounds}/{totalRounds}회 완료 · 앞으로 {remainingRounds}회 남았어.";
        }

        demoFlow?.ShowDescriptionMessage(message);
        demoFlow?.PlayDurryExpression("Joyful");
    }

    private void HandleAlreadyCompleted()
    {
        // 레거시 호출 호환용입니다. 더 이상 추가 미션을 막지 않고 시작 안내로 보냅니다.
        missionNoteUnlocked = true;
        currentState = MissionRuntimeState.Ready;
        ClearPendingMissionScan();
        ApplyInteractionState();
        demoFlow?.SetScanZoneVisible(false);
        RequestOpenMissionNote();
    }

    /// <summary>
    /// 왼손 약지 핀치 디버그 입력에서 호출합니다.
    /// 오늘 미션 진행도, 현재 미션, 보송력, 크레딧을 즉시 초기화합니다.
    /// </summary>
    public void ResetMissionCleanlinessAndCreditFromLeftRingPinch()
    {
        ResolveReferences();

        if (initialScanCoroutine != null)
        {
            StopCoroutine(initialScanCoroutine);
            initialScanCoroutine = null;
        }

        ClearPendingMissionScan();
        initialMissionObjects.Clear();
        detectionMarkerManager?.ClearMarkers();
        questCameraYoloTester?.CancelCurrentScanForReset();
        questCameraYoloTester?.ResetScoreComparison();
        demoFlow?.SetScanZoneVisible(false);
        demoFlow?.CloseBigNote();

        questProgressManager?.ResetAllMissionProgressForDebug();
        CleanlinessManager.Instance?.ResetToDefaultForDebug();
        CreditManager.Instance?.ResetCreditForDebug();

        missionNoteUnlocked = true;
        currentRoundRequiredCount = requiredClearedObjectCount;
        currentState = MissionRuntimeState.Ready;
        ApplyInteractionState();
        questStatusUI?.RefreshMissionList(true);

        demoFlow?.ShowDescriptionMessage(
            "초기화 완료!\n오늘의 미션, 보송력, 크레딧을 처음 상태로 되돌렸어.\n" +
            "NOTE를 눌러 새 미션을 시작해줘!"
        );
        demoFlow?.PlayDurryExpression("Focused");

        Debug.Log("[Dustiny Debug Reset] 왼손 약지 핀치로 미션·보송력·크레딧을 초기화했습니다.");
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

    private Button FindScanOrInspectButton()
    {
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        Button bestMatch = null;
        int bestScore = int.MinValue;

        foreach (Button candidate in buttons)
        {
            if (candidate == null || candidate == durryNoteNavigationButton)
            {
                continue;
            }

            string objectName = candidate.gameObject.name.ToLowerInvariant();
            string parentName = candidate.transform.parent != null
                ? candidate.transform.parent.name.ToLowerInvariant()
                : string.Empty;
            string fullName = objectName + " " + parentName;

            bool matches = fullName.Contains("rescan") ||
                           fullName.Contains("re_scan") ||
                           fullName.Contains("scan") ||
                           fullName.Contains("inspect") ||
                           fullName.Contains("검사");

            if (!matches)
            {
                continue;
            }

            int score = 0;
            if (objectName.Contains("rescan")) score += 20;
            if (objectName.Contains("scan")) score += 12;
            if (objectName.Contains("inspect")) score += 10;
            if (parentName.Contains("note")) score += 8;
            if (parentName.Contains("mission")) score += 6;

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = candidate;
            }
        }

        if (bestMatch != null)
        {
            Debug.Log($"[DustinyMission] 이미지형 스캔/검사 버튼 자동 연결: {bestMatch.name}");
        }

        return bestMatch;
    }

    private void ApplyInteractionState()
    {
        ResolveReferences();
        BindRescanButton();
        // NOTE remains clickable after the opening. Without an active round it opens
        // the start prompt instead of the page.
        bool noteAvailable = missionNoteUnlocked;

        // The image button is inspection-only. The first scan starts from the
        // index-pinch confirmation prompt.
        bool scanButtonAvailable = missionNoteUnlocked &&
                                   currentState == MissionRuntimeState.Active;
        bool questInteractionActive = currentState == MissionRuntimeState.Active;

        if (durryNoteNavigationButton != null)
        {
            durryNoteNavigationButton.interactable = noteAvailable;
        }

        if (rescanButton != null)
        {
            rescanButton.interactable = scanButtonAvailable;
            rescanButton.gameObject.SetActive(scanButtonAvailable);
        }

        // 이 버튼은 이미지형 버튼이므로 상태별 텍스트를 찾거나 변경하지 않습니다.
        // 활성 미션에서 검사 스캔용으로만 사용합니다.
        questStatusUI?.SetMissionInteractionEnabled(questInteractionActive);
    }

    private static float CalculateIoU(Rect first, Rect second)
    {
        float overlapWidth = Mathf.Max(
            0f,
            Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin)
        );
        float overlapHeight = Mathf.Max(
            0f,
            Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin)
        );
        float intersectionArea = overlapWidth * overlapHeight;
        float firstArea = Mathf.Max(0f, first.width) * Mathf.Max(0f, first.height);
        float secondArea = Mathf.Max(0f, second.width) * Mathf.Max(0f, second.height);
        float unionArea = firstArea + secondArea - intersectionArea;
        return unionArea > 0f ? intersectionArea / unionArea : 0f;
    }
}
