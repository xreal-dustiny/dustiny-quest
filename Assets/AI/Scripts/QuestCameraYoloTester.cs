using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

public enum ScanPhase
{
    BeforeCleaning,
    AfterCleaning
}

/// <summary>
/// Repeatedly reads Quest passthrough camera frames, runs YOLO, tracks objects,
/// and publishes one confirmed scan result.
///
/// Integration fixes:
/// - Waits for a real camera texture before starting the scan timer.
/// - Counts only successful inference calls.
/// - Uses a lower raw YOLO threshold during mission scans. The old code only
///   lowered the later tracking threshold, so detections filtered out by
///   AIInferenceTest could never return.
/// - Can use the best tracked candidates when strict confirmation yields zero.
/// - Logs exactly whether failure came from camera readiness, AI initialization,
///   raw detections, tracking, or confirmation filtering.
/// </summary>
public class QuestCameraYoloTester : MonoBehaviour
{
    private const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;
    public AIInferenceTest aiInferenceTest;

    [Header("[ 테스트 입력 ]")]
    [Tooltip("실제 게임에서는 해제합니다.")]
    public bool useDebugAButton = false;

    [Header("[ 화면 표시 - 선택사항 ]")]
    public RawImage cameraPreview;
    public TMP_Text scanStatusText;

    [Header("[ 스캔 UI ]")]
    [SerializeField] private GameObject scanDimOverlay;
    [Tooltip("검정 스캔 오버레이는 모든 상황에서 사용하지 않습니다.")]
    [SerializeField] private bool disableScanDimOverlayCompletely = true;
    [SerializeField] private GameObject scanBox;
    [TextArea(2, 4)] [SerializeField] private string scanningMessage = "스캔 중...";
    [TextArea(2, 4)] [SerializeField] private string waitingForCameraMessage = "카메라 준비 중...";

    [Header("[ 카메라 준비 ]")]
    [Tooltip("카메라 Texture가 생기기 전에 스캔 시간이 끝나는 문제를 막습니다.")]
    [SerializeField] private bool waitForValidCameraTexture = true;
    [SerializeField, Min(0.5f)] private float cameraReadyTimeout = 6f;
    [SerializeField, Min(16)] private int minimumValidTextureSize = 64;

    [Header("[ 스캔 설정 ]")]
    [Min(0.5f)] public float scanDuration = 2.4f;
    [Min(0.05f)] public float inferenceInterval = 0.3f;

    [Tooltip("스캔 시간 동안 이 횟수보다 적게 성공하면 제한 시간 안에서 추가 추론합니다.")]
    [SerializeField, Min(1)] private int minimumSuccessfulInferences = 5;
    [SerializeField, Min(1f)] private float maximumTotalScanTime = 9f;

    [Min(1)] public int minimumDetectionCount = 3;
    [Range(0f, 1f)] public float minimumAverageConfidence = 0.4f;

    [Header("[ 미션 스캔 완화 설정 ]")]
    public bool useLenientMissionThresholds = true;

    [Tooltip("AIInferenceTest의 프레임 단위 필터에 실제로 적용되는 값입니다.")]
    [Range(0f, 1f)] public float missionInferenceConfidenceThreshold = 0.18f;

    [Min(1)] public int missionMinimumDetectionCount = 1;
    [Range(0f, 1f)] public float missionMinimumAverageConfidence = 0.20f;

    [Tooltip("추적 후보는 있었지만 확정 결과가 0개일 때 가장 신뢰도 높은 후보를 사용합니다.")]
    [SerializeField] private bool useTrackedCandidateFallback = true;
    [SerializeField, Range(0f, 1f)] private float trackedCandidateFallbackConfidence = 0.12f;
    [SerializeField, Min(1)] private int maximumFallbackObjectCount = 20;

    [Header("[ 물체 추적 설정 ]")]
    [Range(0f, 1f)] public float trackingIouThreshold = 0.2f;
    [Min(1)] public int maximumTrackingGap = 3;

    [Header("[ 물체 표시 ]")]
    public DetectionMarkerManager detectionMarkerManager;
    [SerializeField] private bool autoShowMarkersForDebug = false;

    [Header("[ 정돈도 점수 비교 ]")]
    public bool compareTidinessScore = true;

    [Header("[ 디버그 ]")]
    public bool printScanLog = true;
    public bool printPerFrameDetectionLog = false;

    [Header("[ 런타임 확인 ]")]
    [SerializeField] private int successfulInferenceCount;
    [SerializeField] private int totalFrameDetections;
    [SerializeField] private string lastScanFailureReason;

    private bool isScanning;
    private int inferenceCount;
    private int nextTrackId = 1;
    private bool hasBeforeScan;

    public int BeforeTidinessScore { get; private set; }
    public int AfterTidinessScore { get; private set; }
    public bool IsScanning => isScanning;
    public bool HasBeforeScan => hasBeforeScan;
    public int SuccessfulInferenceCount => successfulInferenceCount;
    public string LastScanFailureReason => lastScanFailureReason;

    public ScanPhase CurrentScanPhase { get; private set; } = ScanPhase.BeforeCleaning;

    public event System.Action<List<ConfirmedObjectInfo>> OnScanCompleted;
    public event System.Action<ScanPhase, List<ConfirmedObjectInfo>, int> OnScanResultReady;

    private readonly List<TrackedObject> trackedObjects = new List<TrackedObject>();
    public List<ConfirmedObjectInfo> ConfirmedObjects { get; private set; } = new List<ConfirmedObjectInfo>();

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        SetScanningUI(false);
        SetStatusText(string.Empty);
        RequestCameraPermission();
        aiInferenceTest?.EnsureInitialized();
    }

    private void Update()
    {
        if (!useDebugAButton || isScanning)
        {
            return;
        }

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            if (hasBeforeScan)
            {
                StartAfterScan();
            }
            else
            {
                StartBeforeScan();
            }
        }
    }

    private void ResolveReferences()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
        }

        if (aiInferenceTest == null)
        {
            aiInferenceTest = FindFirstObjectByType<AIInferenceTest>();
        }

        if (detectionMarkerManager == null)
        {
            detectionMarkerManager = FindFirstObjectByType<DetectionMarkerManager>();
        }
    }

    public void StartBeforeScan()
    {
        TryStartBeforeScan();
    }

    public bool TryStartBeforeScan()
    {
        CurrentScanPhase = ScanPhase.BeforeCleaning;
        if (autoShowMarkersForDebug)
        {
            detectionMarkerManager?.ClearMarkers();
        }
        return TryStartScan();
    }

    public void StartAfterScan()
    {
        TryStartAfterScan();
    }

    public bool TryStartAfterScan()
    {
        if (!hasBeforeScan)
        {
            Debug.LogWarning("[Quest Camera] 정리 전 스캔이 없어 정리 후 스캔을 시작할 수 없습니다.");
            return false;
        }

        CurrentScanPhase = ScanPhase.AfterCleaning;
        if (autoShowMarkersForDebug)
        {
            detectionMarkerManager?.ClearMarkers();
        }
        return TryStartScan();
    }

    public void StartScan()
    {
        TryStartScan();
    }

    public bool TryStartScan()
    {
        ResolveReferences();

        if (isScanning)
        {
            return false;
        }

        if (!HasCameraPermission())
        {
            lastScanFailureReason = "카메라 권한 없음";
            Debug.LogWarning("[Quest Camera] 카메라 권한이 필요합니다.");
            RequestCameraPermission();
            return false;
        }

        if (!ValidateComponents())
        {
            return false;
        }

        trackedObjects.Clear();
        ConfirmedObjects.Clear();
        nextTrackId = 1;
        inferenceCount = 0;
        successfulInferenceCount = 0;
        totalFrameDetections = 0;
        lastScanFailureReason = string.Empty;
        isScanning = true;

        SetStatusText(waitForValidCameraTexture ? waitingForCameraMessage : scanningMessage);
        SetScanningUI(true);
        aiInferenceTest.ResetCurrentScanResult();
        StartCoroutine(ScanRoutine());
        return true;
    }

    private IEnumerator ScanRoutine()
    {
        if (printScanLog)
        {
            Debug.Log($"[Quest Camera] {CurrentScanPhase} 스캔 시작");
        }

        float absoluteDeadline = Time.unscaledTime + Mathf.Max(maximumTotalScanTime, scanDuration + cameraReadyTimeout);

        if (waitForValidCameraTexture)
        {
            float cameraDeadline = Time.unscaledTime + cameraReadyTimeout;
            while (Time.unscaledTime < cameraDeadline && !HasValidCameraTexture())
            {
                yield return null;
            }

            if (!HasValidCameraTexture())
            {
                lastScanFailureReason = "카메라 Texture 준비 시간 초과";
                Debug.LogError(
                    "[Quest Camera] 패스스루 카메라 Texture가 준비되지 않았습니다. " +
                    "PassthroughCameraAccess 연결·활성화·권한을 확인하세요."
                );
                FinishScan();
                yield break;
            }
        }

        SetStatusText(scanningMessage);
        float normalScanEnd = Time.unscaledTime + scanDuration;

        while (Time.unscaledTime < absoluteDeadline &&
               (Time.unscaledTime < normalScanEnd || successfulInferenceCount < minimumSuccessfulInferences))
        {
            RunSingleInference();
            yield return new WaitForSecondsRealtime(inferenceInterval);
        }

        FinishScan();
    }

    private bool HasValidCameraTexture()
    {
        if (passthroughCameraAccess == null || !passthroughCameraAccess.isActiveAndEnabled)
        {
            return false;
        }

        Texture texture = passthroughCameraAccess.GetTexture();
        return texture != null &&
               texture.width >= minimumValidTextureSize &&
               texture.height >= minimumValidTextureSize;
    }

    private bool RunSingleInference()
    {
        Texture cameraTexture = passthroughCameraAccess != null
            ? passthroughCameraAccess.GetTexture()
            : null;

        if (cameraTexture == null ||
            cameraTexture.width < minimumValidTextureSize ||
            cameraTexture.height < minimumValidTextureSize)
        {
            lastScanFailureReason = "유효한 카메라 Texture 없음";
            return false;
        }

        if (cameraPreview != null)
        {
            cameraPreview.texture = cameraTexture;
        }

        float rawThreshold = useLenientMissionThresholds
            ? Mathf.Min(aiInferenceTest.confidenceThreshold, missionInferenceConfidenceThreshold)
            : aiInferenceTest.confidenceThreshold;

        bool executed = aiInferenceTest.TryRunRealtimeInference(cameraTexture, rawThreshold);
        if (!executed)
        {
            lastScanFailureReason = aiInferenceTest.LastFailureReason;
            if (printScanLog && !string.IsNullOrWhiteSpace(lastScanFailureReason))
            {
                Debug.LogWarning($"[Quest Camera] 추론 건너뜀: {lastScanFailureReason}");
            }
            return false;
        }

        inferenceCount++;
        successfulInferenceCount++;

        List<Detection> frameDetections = aiInferenceTest.LastDetections;
        int frameCount = frameDetections != null ? frameDetections.Count : 0;
        totalFrameDetections += frameCount;
        TrackDetections(frameDetections);

        if (printPerFrameDetectionLog)
        {
            Debug.Log(
                $"[Quest Camera Frame {successfulInferenceCount}] " +
                $"탐지 {frameCount}개, 누적 Track {trackedObjects.Count}개, rawThreshold={rawThreshold:F2}"
            );
        }

        return true;
    }

    private void TrackDetections(List<Detection> detections)
    {
        if (detections == null || detections.Count == 0)
        {
            return;
        }

        HashSet<int> matchedTrackIds = new HashSet<int>();
        IEnumerable<Detection> sorted = detections.OrderByDescending(item => item.confidence);

        foreach (Detection detection in sorted)
        {
            TrackedObject track = FindBestTrack(detection, matchedTrackIds);
            if (track == null)
            {
                track = CreateTrack(detection);
            }
            else
            {
                UpdateTrack(track, detection);
            }

            matchedTrackIds.Add(track.id);
        }
    }

    private TrackedObject FindBestTrack(Detection detection, HashSet<int> matchedTrackIds)
    {
        TrackedObject bestTrack = null;
        float bestIou = trackingIouThreshold;

        foreach (TrackedObject track in trackedObjects)
        {
            if (matchedTrackIds.Contains(track.id) || track.className != detection.className)
            {
                continue;
            }

            int gap = inferenceCount - track.lastSeenInference;
            if (gap > maximumTrackingGap)
            {
                continue;
            }

            float iou = CalculateIoU(track.lastRect, detection.rect);
            if (iou >= bestIou)
            {
                bestIou = iou;
                bestTrack = track;
            }
        }

        return bestTrack;
    }

    private TrackedObject CreateTrack(Detection detection)
    {
        TrackedObject track = new TrackedObject
        {
            id = nextTrackId++,
            classId = detection.classId,
            className = detection.className,
            lastRect = detection.rect,
            detectionCount = 1,
            confidenceSum = detection.confidence,
            lastSeenInference = inferenceCount
        };
        trackedObjects.Add(track);
        return track;
    }

    private void UpdateTrack(TrackedObject track, Detection detection)
    {
        track.lastRect = detection.rect;
        track.detectionCount++;
        track.confidenceSum += detection.confidence;
        track.lastSeenInference = inferenceCount;
    }

    private void FinishScan()
    {
        isScanning = false;
        SetScanningUI(false);
        SetStatusText(string.Empty);

        int effectiveMinimumDetectionCount = useLenientMissionThresholds
            ? Mathf.Max(1, missionMinimumDetectionCount)
            : Mathf.Max(1, minimumDetectionCount);

        float effectiveMinimumAverageConfidence = useLenientMissionThresholds
            ? Mathf.Clamp01(missionMinimumAverageConfidence)
            : Mathf.Clamp01(minimumAverageConfidence);

        ConfirmedObjects = ConvertTracksToConfirmed(
            trackedObjects.Where(track =>
                track.detectionCount >= effectiveMinimumDetectionCount &&
                track.AverageConfidence >= effectiveMinimumAverageConfidence)
        );

        bool usedFallback = false;
        if (ConfirmedObjects.Count == 0 && useTrackedCandidateFallback && trackedObjects.Count > 0)
        {
            ConfirmedObjects = ConvertTracksToConfirmed(
                trackedObjects
                    .Where(track => track.AverageConfidence >= trackedCandidateFallbackConfidence)
                    .OrderByDescending(track => track.detectionCount)
                    .ThenByDescending(track => track.AverageConfidence)
                    .Take(maximumFallbackObjectCount)
            );
            usedFallback = ConfirmedObjects.Count > 0;
        }

        if (autoShowMarkersForDebug && detectionMarkerManager != null)
        {
            detectionMarkerManager.ShowMarkers(ConfirmedObjects);
        }

        int currentScore = CalculateTidinessScore(ConfirmedObjects);
        if (compareTidinessScore)
        {
            HandleTidinessScoreComparison(currentScore);
        }
        else if (printScanLog)
        {
            Debug.Log($"[Quest Camera] 일반 스캔 완료\n{BuildObjectSummary()}");
        }

        if (successfulInferenceCount == 0)
        {
            lastScanFailureReason = string.IsNullOrWhiteSpace(lastScanFailureReason)
                ? "성공한 추론이 0회"
                : lastScanFailureReason;
        }
        else if (totalFrameDetections == 0)
        {
            lastScanFailureReason =
                "카메라와 모델 추론은 실행됐지만 프레임 탐지가 0개입니다. " +
                "AIInferenceTest의 Model Asset, Classes File, Output Layout, confidence를 확인하세요.";
        }
        else if (ConfirmedObjects.Count == 0)
        {
            lastScanFailureReason = "프레임 탐지는 있었지만 추적/확정 기준에서 모두 제외됨";
        }
        else
        {
            lastScanFailureReason = string.Empty;
        }

        // Set the diagnostic before publishing the event so the mission controller
        // can show the actual failure reason immediately.
        List<ConfirmedObjectInfo> resultCopy = new List<ConfirmedObjectInfo>(ConfirmedObjects);
        OnScanCompleted?.Invoke(resultCopy);
        OnScanResultReady?.Invoke(CurrentScanPhase, resultCopy, currentScore);

        if (printScanLog)
        {
            Debug.Log(
                $"[Quest Camera] 스캔 진단\n" +
                $"- 성공 추론: {successfulInferenceCount}회\n" +
                $"- 프레임 탐지 누계: {totalFrameDetections}개\n" +
                $"- 누적 Track: {trackedObjects.Count}개\n" +
                $"- 확정 물체: {ConfirmedObjects.Count}개\n" +
                $"- 후보 fallback 사용: {usedFallback}\n" +
                $"- AI output length: {(aiInferenceTest != null ? aiInferenceTest.LastOutputLength : 0)}\n" +
                $"- 마지막 실패 사유: {(string.IsNullOrWhiteSpace(lastScanFailureReason) ? "없음" : lastScanFailureReason)}"
            );
            PrintDetailedResult();
        }
    }

    private static List<ConfirmedObjectInfo> ConvertTracksToConfirmed(IEnumerable<TrackedObject> tracks)
    {
        return tracks
            .OrderByDescending(track => track.detectionCount)
            .ThenByDescending(track => track.AverageConfidence)
            .Select(track => new ConfirmedObjectInfo
            {
                objectId = track.id,
                classId = track.classId,
                className = track.className,
                detectionCount = track.detectionCount,
                averageConfidence = track.AverageConfidence,
                lastRect = track.lastRect
            })
            .ToList();
    }

    private void HandleTidinessScoreComparison(int currentScore)
    {
        if (CurrentScanPhase == ScanPhase.BeforeCleaning)
        {
            BeforeTidinessScore = currentScore;
            hasBeforeScan = true;
            Debug.Log(
                $"[AI Scan Result] 탐지 물체: {ConfirmedObjects.Count}개, 정돈도: {currentScore}점\n" +
                BuildObjectSummary()
            );
            return;
        }

        if (!hasBeforeScan)
        {
            AfterTidinessScore = currentScore;
            Debug.LogWarning("[Tidiness Score] 정리 전 점수가 없어 비교하지 못했습니다.");
            return;
        }

        AfterTidinessScore = currentScore;
        int difference = AfterTidinessScore - BeforeTidinessScore;
        Debug.Log(
            $"[Tidiness Score] 비교 완료\n정리 전: {BeforeTidinessScore}\n" +
            $"정리 후: {AfterTidinessScore}\n변화: {(difference > 0 ? "+" : string.Empty)}{difference}"
        );
    }

    /// <summary>
    /// 디버그 전체 초기화 중 진행 중인 스캔 결과가 뒤늦게 미션에 반영되지 않도록
    /// 현재 스캔 코루틴과 임시 추적 데이터를 즉시 취소합니다.
    /// </summary>
    public void CancelCurrentScanForReset()
    {
        StopAllCoroutines();
        isScanning = false;
        trackedObjects.Clear();
        ConfirmedObjects.Clear();
        inferenceCount = 0;
        successfulInferenceCount = 0;
        totalFrameDetections = 0;
        lastScanFailureReason = string.Empty;
        nextTrackId = 1;
        SetScanningUI(false);
        SetStatusText(string.Empty);
        aiInferenceTest?.ResetCurrentScanResult();
        Debug.Log("[Quest Camera] 전체 초기화를 위해 진행 중인 스캔을 취소했습니다.");
    }

    public void ResetScoreComparison()
    {
        hasBeforeScan = false;
        BeforeTidinessScore = 0;
        AfterTidinessScore = 0;
        SetScanningUI(false);
        SetStatusText(string.Empty);
        Debug.Log("[Tidiness Score] 점수 비교 초기화");
    }

    private string BuildObjectSummary()
    {
        if (ConfirmedObjects == null || ConfirmedObjects.Count == 0)
        {
            return "Total objects: 0";
        }

        IEnumerable<string> lines = ConfirmedObjects
            .GroupBy(item => item.className)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => $"{group.Key} x{group.Count()}");

        return $"Total objects: {ConfirmedObjects.Count}\n" + string.Join("\n", lines);
    }

    private int CalculateTidinessScore(List<ConfirmedObjectInfo> objects)
    {
        int count = objects?.Count ?? 0;
        return Mathf.RoundToInt(Mathf.Clamp(100f - count * 12.5f, 0f, 100f));
    }

    private void PrintDetailedResult()
    {
        if (!printScanLog)
        {
            return;
        }

        IEnumerable<string> lines = ConfirmedObjects.Select(item =>
            $"Object #{item.objectId} / {item.className} / " +
            $"{item.detectionCount}/{Mathf.Max(1, inferenceCount)} scans / " +
            $"{item.averageConfidence * 100f:0}%"
        );

        Debug.Log(
            $"[Quest Camera] 세부 결과\n확정 물체: {ConfirmedObjects.Count}개\n" +
            string.Join("\n", lines)
        );
    }

    private bool ValidateComponents()
    {
        if (passthroughCameraAccess == null)
        {
            lastScanFailureReason = "PassthroughCameraAccess 미연결";
            Debug.LogError("[Quest Camera] PassthroughCameraAccess가 없습니다.");
            return false;
        }

        if (!passthroughCameraAccess.isActiveAndEnabled)
        {
            lastScanFailureReason = "PassthroughCameraAccess 비활성";
            Debug.LogError("[Quest Camera] 카메라 접근 컴포넌트가 비활성화되어 있습니다.");
            return false;
        }

        if (aiInferenceTest == null)
        {
            lastScanFailureReason = "AIInferenceTest 미연결";
            Debug.LogError("[Quest Camera] AIInferenceTest가 없습니다.");
            return false;
        }

        if (!aiInferenceTest.EnsureInitialized())
        {
            lastScanFailureReason = aiInferenceTest.LastFailureReason;
            Debug.LogError($"[Quest Camera] AIInferenceTest 초기화 실패: {lastScanFailureReason}");
            return false;
        }

        return true;
    }

    private bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(HeadsetCameraPermission);
#else
        return true;
#endif
    }

    public void RequestCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!HasCameraPermission())
        {
            Permission.RequestUserPermission(HeadsetCameraPermission);
        }
#endif
    }

    private static float CalculateIoU(Rect first, Rect second)
    {
        float overlapWidth = Mathf.Max(0f, Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin));
        float overlapHeight = Mathf.Max(0f, Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin));
        float intersection = overlapWidth * overlapHeight;
        float firstArea = Mathf.Max(0f, first.width) * Mathf.Max(0f, first.height);
        float secondArea = Mathf.Max(0f, second.width) * Mathf.Max(0f, second.height);
        float union = firstArea + secondArea - intersection;
        return union > 0f ? intersection / union : 0f;
    }

    private void SetScanningUI(bool visible)
    {
        // 검정 ScanDimOverlay는 항상 비활성화합니다.
        disableScanDimOverlayCompletely = true;

        if (scanDimOverlay != null)
        {
            Image dimImage = scanDimOverlay.GetComponent<Image>();
            if (dimImage != null)
            {
                Color color = dimImage.color;
                color.a = 0f;
                dimImage.color = color;
                dimImage.raycastTarget = false;
            }

            scanDimOverlay.SetActive(false);
        }

        // 스캔 프레임/안내 박스는 유지합니다.
        if (scanBox != null)
        {
            scanBox.SetActive(visible);
        }
    }

    private void SetStatusText(string message)
    {
        if (scanStatusText != null)
        {
            scanStatusText.text = message;
        }
    }

    private void OnDisable()
    {
        if (isScanning)
        {
            StopAllCoroutines();
            isScanning = false;
        }
        SetScanningUI(false);
        SetStatusText(string.Empty);
    }
}

internal class TrackedObject
{
    public int id;
    public int classId;
    public string className;
    public Rect lastRect;
    public int detectionCount;
    public float confidenceSum;
    public int lastSeenInference;

    public float AverageConfidence => detectionCount > 0 ? confidenceSum / detectionCount : 0f;
}

[System.Serializable]
public class ConfirmedObjectInfo
{
    public int objectId;
    public int classId;
    public string className;
    public int detectionCount;
    public float averageConfidence;
    public Rect lastRect;
}
