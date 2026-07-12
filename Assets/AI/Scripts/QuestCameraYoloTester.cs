using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Meta.XR;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;


/// Quest 카메라를 반복 추론하고 개별 물체를 추적
/// 첫 번째 스캔과 두 번째 스캔의 정돈도 점수를 비교
public class QuestCameraYoloTester : MonoBehaviour
{
    private const string HeadsetCameraPermission =
        "horizonos.permission.HEADSET_CAMERA";

    private const float ModelInputSize = 640f;

    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;
    public AIInferenceTest aiInferenceTest;

    [Header("[ 테스트 입력 ]")]
    [Tooltip("테스트 중에는 체크합니다. 손 인식 연동 후에는 해제합니다.")]
    public bool useDebugAButton = true;

    [Header("[ 화면 표시 - 선택사항 ]")]
    public RawImage cameraPreview;
    public TMP_Text scanStatusText;

    [Header("[ 스캔 설정 ]")]
    [Min(0.5f)]
    public float scanDuration = 1.5f;

    [Min(0.1f)]
    public float inferenceInterval = 0.3f;

    [Min(1)]
    public int minimumDetectionCount = 3;

    [Range(0f, 1f)]
    public float minimumAverageConfidence = 0.4f;

    [Header("[ 물체 추적 설정 ]")]
    [Range(0f, 1f)]
    public float trackingIouThreshold = 0.3f;

    [Min(1)]
    public int maximumTrackingGap = 2;

    [Header("[ 물체 표시 ]")]
    public DetectionMarkerManager detectionMarkerManager;

    [Header("[ 정돈도 점수 비교 ]")]
    public bool compareTidinessScore = true;

    [Header("[ 디버그 ]")]
    public bool printScanLog = true;

    private bool isScanning;
    private int inferenceCount;
    private int nextTrackId = 1;

    // 첫 번째 스캔 점수가 저장되었는지
    private bool hasBeforeScan;

    public int BeforeTidinessScore
    {
        get;
        private set;
    }

    public int AfterTidinessScore
    {
        get;
        private set;
    }

    // 스캔 중 임시로 추적하는 물체
    private readonly List<TrackedObject> trackedObjects =
        new List<TrackedObject>();

    // 스캔 종료 후 확정된 실제 물체
    public List<ConfirmedObjectInfo> ConfirmedObjects
    {
        get;
        private set;
    } = new List<ConfirmedObjectInfo>();

    private void Awake()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess =
                FindFirstObjectByType<PassthroughCameraAccess>();
        }

        if (aiInferenceTest == null)
        {
            aiInferenceTest =
                FindFirstObjectByType<AIInferenceTest>();
        }

        if (detectionMarkerManager == null)
        {
            detectionMarkerManager =
                FindFirstObjectByType<DetectionMarkerManager>();
        }
    }

    private void Start()
    {
        SetStatusText("");
        RequestCameraPermission();
    }

    private void Update()
    {
        // 현재는 테스트용 A 버튼으로 스캔 시작
        if (!useDebugAButton || isScanning)
        {
            return;
        }

        bool pressedA =
            OVRInput.GetDown(
                OVRInput.Button.One,
                OVRInput.Controller.RTouch
            );

        if (pressedA)
        {
            StartScan();
        }
    }


    /// 새로운 객체 스캔을 시작한다.
    /// 나중에 손 인식 담당 코드에서 이 함수를 호출하면 된다.
    public void StartScan()
    {
        if (isScanning)
        {
            return;
        }

        if (!HasCameraPermission())
        {
            SetStatusText(
                "Camera permission required"
            );

            RequestCameraPermission();
            return;
        }

        if (!ValidateComponents())
        {
            return;
        }

        // 이전 스캔의 물체 추적 결과 초기화
        trackedObjects.Clear();
        ConfirmedObjects.Clear();

        nextTrackId = 1;
        inferenceCount = 0;
        isScanning = true;

        aiInferenceTest.ResetCurrentScanResult();

        StartCoroutine(
            ScanRoutine()
        );
    }

    // 설정된 시간 동안 일정 간격으로 추론
    private IEnumerator ScanRoutine()
    {
        SetStatusText(
            "Scanning...\n" +
            "Please keep your head still"
        );

        if (printScanLog)
        {
            Debug.Log(
                "[Quest Camera] 반복 스캔 시작"
            );
        }

        float endTime =
            Time.time + scanDuration;

        while (Time.time < endTime)
        {
            RunSingleInference();

            yield return new WaitForSeconds(
                inferenceInterval
            );
        }

        FinishScan();
    }

    // 현재 카메라 프레임 한 장을 YOLO로 추론한다.
    private void RunSingleInference()
    {
        Texture cameraTexture =
            passthroughCameraAccess.GetTexture();

        if (cameraTexture == null)
        {
            if (printScanLog)
            {
                Debug.LogWarning(
                    "[Quest Camera] 카메라 Texture가 없습니다."
                );
            }

            return;
        }

        if (cameraPreview != null)
        {
            cameraPreview.texture =
                cameraTexture;
        }

        inferenceCount++;

        aiInferenceTest.RunRealtimeInference(
            cameraTexture
        );

        TrackDetections(
            aiInferenceTest.LastDetections
        );

        SetStatusText(
            $"Scanning... {inferenceCount}\n" +
            "Please keep your head still"
        );
    }

    // 같은 클래스와 박스 위치를 기준으로 개별 물체를 추적
    private void TrackDetections(
        List<Detection> detections
    )
    {
        if (detections == null ||
            detections.Count == 0)
        {
            return;
        }

        // 한 추론에서 기존 Track 하나가 중복 연결되는 것을 방지
        HashSet<int> matchedTrackIds =
            new HashSet<int>();

        IEnumerable<Detection> sortedDetections =
            detections.OrderByDescending(
                detection =>
                    detection.confidence
            );

        foreach (Detection detection in sortedDetections)
        {
            TrackedObject track =
                FindBestTrack(
                    detection,
                    matchedTrackIds
                );

            if (track == null)
            {
                track = CreateTrack(
                    detection
                );
            }
            else
            {
                UpdateTrack(
                    track,
                    detection
                );
            }

            matchedTrackIds.Add(
                track.id
            );
        }
    }

    // 현재 Detection과 가장 잘 겹치는 기존 Track을 찾음
    private TrackedObject FindBestTrack(
        Detection detection,
        HashSet<int> matchedTrackIds
    )
    {
        TrackedObject bestTrack = null;
        float bestIou = trackingIouThreshold;

        foreach (TrackedObject track in trackedObjects)
        {
            if (matchedTrackIds.Contains(track.id))
            {
                continue;
            }

            if (track.className != detection.className)
            {
                continue;
            }

            int trackingGap =
                inferenceCount -
                track.lastSeenInference;

            if (trackingGap > maximumTrackingGap)
            {
                continue;
            }

            float iou =
                CalculateIoU(
                    track.lastRect,
                    detection.rect
                );

            if (iou >= bestIou)
            {
                bestIou = iou;
                bestTrack = track;
            }
        }

        return bestTrack;
    }

    // 기존 Track이 없으면 새 물체로 등록
    private TrackedObject CreateTrack(
        Detection detection
    )
    {
        TrackedObject track =
            new TrackedObject
            {
                id = nextTrackId++,
                className = detection.className,
                lastRect = detection.rect,
                detectionCount = 1,
                confidenceSum = detection.confidence,
                lastSeenInference = inferenceCount
            };

        trackedObjects.Add(
            track
        );

        return track;
    }

    // 기존 물체의 최신 탐지 정보를 갱신
    private void UpdateTrack(
        TrackedObject track,
        Detection detection
    )
    {
        track.lastRect =
            detection.rect;

        track.detectionCount++;

        track.confidenceSum +=
            detection.confidence;

        track.lastSeenInference =
            inferenceCount;
    }

    // 확정 조건을 통과한 물체만 최종 결과로 저장
    private void FinishScan()
    {
        isScanning = false;

        ConfirmedObjects =
            trackedObjects
                .Where(track =>
                    track.detectionCount >=
                        minimumDetectionCount &&
                    track.AverageConfidence >=
                        minimumAverageConfidence
                )
                .OrderByDescending(track =>
                    track.detectionCount
                )
                .ThenByDescending(track =>
                    track.AverageConfidence
                )
                .Select(track =>
                    new ConfirmedObjectInfo
                    {
                        objectId = track.id,
                        className = track.className,
                        detectionCount =
                            track.detectionCount,
                        averageConfidence =
                            track.AverageConfidence,
                        lastRect = track.lastRect
                    }
                )
                .ToList();

        if (detectionMarkerManager != null)
        {
            detectionMarkerManager.ShowMarkers(
                ConfirmedObjects
            );
        }

        if (compareTidinessScore)
        {
            HandleTidinessScoreComparison();
        }
        else
        {
            ShowFinalResult();
        }

        if (printScanLog)
        {
            PrintDetailedResult();
        }
    }

 
    // 첫 번째 스캔은 정리 전, 두 번째 스캔은 정리 후로 처리
    private void HandleTidinessScoreComparison()
    {
        int currentScore =
            CalculateTidinessScore(
                ConfirmedObjects
            );

        string objectSummary =
            BuildObjectSummary();

        // 첫 번째 스캔: 정리 전 점수 저장
        if (!hasBeforeScan)
        {
            BeforeTidinessScore =
                currentScore;

            hasBeforeScan = true;

            SetStatusText(
                $"Before scan saved!\n" +
                $"Score: {BeforeTidinessScore}\n" +
                $"{objectSummary}\n" +
                $"Clean up, then scan again"
            );

            Debug.Log(
                $"[Tidiness Score] 정리 전 점수: " +
                $"{BeforeTidinessScore}"
            );

            return;
        }

        // 두 번째 스캔: 정리 후 점수 계산
        AfterTidinessScore =
            currentScore;

        int scoreDifference =
            AfterTidinessScore -
            BeforeTidinessScore;

        string differenceText =
            scoreDifference > 0
                ? $"+{scoreDifference}"
                : scoreDifference.ToString();

        SetStatusText(
            $"After scan complete!\n" +
            $"Before: {BeforeTidinessScore}\n" +
            $"After: {AfterTidinessScore}\n" +
            $"Score change: {differenceText}\n" +
            objectSummary
        );

        Debug.Log(
            $"[Tidiness Score] 비교 완료\n" +
            $"정리 전: {BeforeTidinessScore}\n" +
            $"정리 후: {AfterTidinessScore}\n" +
            $"점수 변화: {differenceText}"
        );

        // 다음 스캔부터 새로운 전후 비교 시작
        hasBeforeScan = false;
    }

    /// <summary>
    /// 진행 중인 정리 전·후 점수 비교를 초기화한다.
    /// 미션 취소 또는 새 구역 선택 시 호출할 수 있다.
    /// </summary>
    public void ResetScoreComparison()
    {
        hasBeforeScan = false;

        BeforeTidinessScore = 0;
        AfterTidinessScore = 0;

        SetStatusText("");

        Debug.Log(
            "[Tidiness Score] 점수 비교 초기화"
        );
    }

    // 확정된 물체 목록을 간단한 문자열로 만들기
    private string BuildObjectSummary()
    {
        if (ConfirmedObjects == null ||
            ConfirmedObjects.Count == 0)
        {
            return "Total objects: 0";
        }

        IEnumerable<string> classLines =
            ConfirmedObjects
                .GroupBy(item =>
                    item.className
                )
                .OrderByDescending(group =>
                    group.Count()
                )
                .ThenBy(group =>
                    group.Key
                )
                .Select(group =>
                    $"{group.Key} x{group.Count()}"
                );

        return
            $"Total objects: {ConfirmedObjects.Count}\n" +
            string.Join(
                "\n",
                classLines
            );
    }

    // 반복 탐지로 확정된 정리 대상 물체 개수만으로 점수를 계산
    private int CalculateTidinessScore(
        List<ConfirmedObjectInfo> objects
    )
    {
        int objectCount =
            objects?.Count ?? 0;

        const float penaltyPerObject = 12.5f;

        float score =
            100f -
            objectCount * penaltyPerObject;

        int finalScore =
            Mathf.RoundToInt(
                Mathf.Clamp(
                    score,
                    0f,
                    100f
                )
            );

        if (printScanLog)
        {
            Debug.Log(
                $"[Tidiness Score]\n" +
                $"확정 물체 수: {objectCount}개\n" +
                $"물체당 감점: {penaltyPerObject:F1}점\n" +
                $"최종 점수: {finalScore}점"
            );
        }

        return finalScore;
    }

    // 점수 비교를 사용하지 않을 때 일반 스캔 결과를 출력
    private void ShowFinalResult()
    {
        SetStatusText(
            $"Scan complete!\n" +
            BuildObjectSummary()
        );
    }

    private void PrintDetailedResult()
    {
        IEnumerable<string> detailLines =
            ConfirmedObjects.Select(item =>
                $"Object #{item.objectId} / " +
                $"{item.className} / " +
                $"{item.detectionCount}/{inferenceCount} scans / " +
                $"{item.averageConfidence * 100f:0}%"
            );

        Debug.Log(
            $"[Quest Camera] 스캔 완료\n" +
            $"총 추론: {inferenceCount}회\n" +
            $"확정 물체: {ConfirmedObjects.Count}개\n" +
            string.Join(
                "\n",
                detailLines
            )
        );
    }

    private bool ValidateComponents()
    {
        if (passthroughCameraAccess == null)
        {
            Debug.LogError(
                "[Quest Camera] PassthroughCameraAccess가 없습니다."
            );

            return false;
        }

        if (aiInferenceTest == null)
        {
            Debug.LogError(
                "[Quest Camera] AIInferenceTest가 없습니다."
            );

            return false;
        }

        if (!passthroughCameraAccess.isActiveAndEnabled)
        {
            Debug.LogError(
                "[Quest Camera] 카메라 접근 컴포넌트가 비활성화되어 있습니다."
            );

            return false;
        }

        return true;
    }

    private bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        return Permission.HasUserAuthorizedPermission(
            HeadsetCameraPermission
        );

#else

        return true;

#endif
    }


    /// Quest 헤드셋 카메라 권한을 요청
    public void RequestCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        if (!HasCameraPermission())
        {
            Permission.RequestUserPermission(
                HeadsetCameraPermission
            );
        }

#endif
    }

    private float CalculateIoU(
        Rect first,
        Rect second
    )
    {
        float overlapWidth =
            Mathf.Max(
                0f,
                Mathf.Min(
                    first.xMax,
                    second.xMax
                ) -
                Mathf.Max(
                    first.xMin,
                    second.xMin
                )
            );

        float overlapHeight =
            Mathf.Max(
                0f,
                Mathf.Min(
                    first.yMax,
                    second.yMax
                ) -
                Mathf.Max(
                    first.yMin,
                    second.yMin
                )
            );

        float intersectionArea =
            overlapWidth *
            overlapHeight;

        float firstArea =
            Mathf.Max(
                0f,
                first.width
            ) *
            Mathf.Max(
                0f,
                first.height
            );

        float secondArea =
            Mathf.Max(
                0f,
                second.width
            ) *
            Mathf.Max(
                0f,
                second.height
            );

        float unionArea =
            firstArea +
            secondArea -
            intersectionArea;

        return unionArea > 0f
            ? intersectionArea / unionArea
            : 0f;
    }

    private void SetStatusText(
        string message
    )
    {
        if (scanStatusText == null)
        {
            return;
        }

        scanStatusText.text =
            message;

        scanStatusText.gameObject.SetActive(
            !string.IsNullOrEmpty(message)
        );
    }
}

// 스캔 중 사용하는 임시 물체 정보
internal class TrackedObject
{
    public int id;
    public string className;
    public Rect lastRect;
    public int detectionCount;
    public float confidenceSum;
    public int lastSeenInference;

    public float AverageConfidence =>
        detectionCount > 0
            ? confidenceSum / detectionCount
            : 0f;
}

/// 스캔 종료 후 확정된 개별 물체 정보.
[System.Serializable]
public class ConfirmedObjectInfo
{
    public int objectId;
    public string className;
    public int detectionCount;
    public float averageConfidence;
    public Rect lastRect;
}