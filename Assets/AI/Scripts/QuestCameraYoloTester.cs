using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Meta.XR;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;


/// <summary>
/// Meta Quest의 패스스루 카메라 영상을 가져와 YOLO 추론을 실행하고,
/// 짧은 시간 동안 반복 탐지된 개별 물체를 추적·확정하는 스크립트.
///
/// 담당 기능:
/// 1. 헤드셋 카메라 권한 요청
/// 2. A 버튼 입력 감지
/// 3. 패스스루 카메라 Texture 가져오기
/// 4. 일정 시간 동안 반복 YOLO 추론
/// 5. IoU를 이용해 같은 물체 추적
/// 6. 실제 물체 개수 확정
/// 7. 스캔 결과를 UI와 Console에 출력
/// </summary>




/// Quest 카메라를 반복 추론하고,
/// 같은 물체를 추적해 최종 물체 목록을 생성
public class QuestCameraYoloTester : MonoBehaviour
{
    private const string HeadsetCameraPermission =
        "horizonos.permission.HEADSET_CAMERA";

    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;
    public AIInferenceTest aiInferenceTest;

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

    [Header("[ 디버그 ]")]
    public bool printScanLog = true;

    private bool isScanning;
    private int inferenceCount;
    private int nextTrackId = 1;

    private readonly List<TrackedObject> trackedObjects =
        new List<TrackedObject>();

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
    }

    private void Start()
    {
        SetStatusText("");
        RequestCameraPermission();
    }

    private void Update()
    {
        bool pressedA =
            OVRInput.GetDown(
                OVRInput.Button.One,
                OVRInput.Controller.RTouch
            );

        if (pressedA && !isScanning)
        {
            StartScan();
        }
    }

    /// 새로운 객체 스캔을 시작한다.
    public void StartScan()
    {
        if (!HasCameraPermission())
        {
            SetStatusText("Camera permission required");
            RequestCameraPermission();
            return;
        }

        if (!ValidateComponents())
        {
            return;
        }

        trackedObjects.Clear();
        ConfirmedObjects.Clear();

        nextTrackId = 1;
        inferenceCount = 0;
        isScanning = true;

        aiInferenceTest.ResetCurrentScanResult();

        StartCoroutine(ScanRoutine());
    }

    // 설정된 시간 동안 일정 간격으로 추론
    private IEnumerator ScanRoutine()
    {
        SetStatusText(
            "Scanning...\nPlease keep your head still"
        );

        if (printScanLog)
        {
            Debug.Log("[Quest Camera] 스캔 시작");
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

    // 패스스루 카메라 프레임 한 장을 추론
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

    // 현재 Detection과 가장 잘 겹치는 기존 물체를 찾기
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

    // 새로운 물체 추적 정보를 생성
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

        trackedObjects.Add(track);

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

    // 반복적으로 탐지된 안정적인 물체만 최종 확정
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

        ShowFinalResult();

        if (printScanLog)
        {
            PrintDetailedResult();
        }
    }

    private void ShowFinalResult()
    {
        if (ConfirmedObjects.Count == 0)
        {
            SetStatusText(
                "Scan complete!\nNo objects detected"
            );

            return;
        }

        IEnumerable<string> classLines =
            ConfirmedObjects
                .GroupBy(item =>
                    item.className
                )
                .OrderByDescending(group =>
                    group.Count()
                )
                .Select(group =>
                    $"{group.Key} x{group.Count()}"
                );

        SetStatusText(
            $"Scan complete!\n" +
            $"Total objects: {ConfirmedObjects.Count}\n" +
            string.Join("\n", classLines)
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
            string.Join("\n", detailLines)
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
                Mathf.Min(first.xMax, second.xMax) -
                Mathf.Max(first.xMin, second.xMin)
            );

        float overlapHeight =
            Mathf.Max(
                0f,
                Mathf.Min(first.yMax, second.yMax) -
                Mathf.Max(first.yMin, second.yMin)
            );

        float intersectionArea =
            overlapWidth * overlapHeight;

        float firstArea =
            Mathf.Max(0f, first.width) *
            Mathf.Max(0f, first.height);

        float secondArea =
            Mathf.Max(0f, second.width) *
            Mathf.Max(0f, second.height);

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

        scanStatusText.text = message;

        scanStatusText.gameObject.SetActive(
            !string.IsNullOrEmpty(message)
        );
    }
}

// 스캔 도중 사용하는 임시 물체 추적 정보
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


/// 스캔이 끝난 뒤 확정된 개별 물체 정보.
[System.Serializable]
public class ConfirmedObjectInfo
{
    public int objectId;
    public string className;
    public int detectionCount;
    public float averageConfidence;
    public Rect lastRect;
}