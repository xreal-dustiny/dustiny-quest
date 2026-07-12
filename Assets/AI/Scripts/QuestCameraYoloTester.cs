using System.Collections.Generic;
using System.Linq;
using Meta.XR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuestCameraYoloTester : MonoBehaviour
{
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

    [Tooltip("개별 물체가 최소 몇 번 탐지되어야 확정할지")]
    [Min(1)]
    public int minimumDetectionCount = 3;

    [Tooltip("개별 물체 확정에 필요한 최소 평균 정확도")]
    [Range(0f, 1f)]
    public float minimumAverageConfidence = 0.4f;

    [Header("[ 개별 물체 추적 설정 ]")]
    [Tooltip(
        "같은 클래스의 박스가 이 값 이상 겹치면 " +
        "같은 물체로 판단합니다."
    )]
    [Range(0f, 1f)]
    public float trackingIouThreshold = 0.3f;

    [Tooltip(
        "물체가 잠시 누락되어도 몇 번의 추론까지 " +
        "기존 물체로 다시 연결할지 설정합니다."
    )]
    [Min(1)]
    public int maximumTrackingGap = 2;

    [Header("[ 디버그 ]")]
    public bool printScanLog = true;

    private bool isScanning;
    private float scanElapsedTime;
    private float inferenceElapsedTime;
    private int inferenceCount;
    private int nextTrackId = 1;

    // 스캔 중 추적 중인 개별 물체 목록
    private readonly List<TrackedObject> trackedObjects =
        new List<TrackedObject>();

    // 스캔 종료 후 확정된 개별 물체 목록
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
    }

    private void Update()
    {
        // Quest 오른쪽 컨트롤러 A 버튼
        if (OVRInput.GetDown(
                OVRInput.Button.One,
                OVRInput.Controller.RTouch))
        {
            if (!isScanning)
            {
                StartScan();
            }
        }

        if (isScanning)
        {
            UpdateScan();
        }
    }

    public void StartScan()
    {
        if (passthroughCameraAccess == null)
        {
            Debug.LogError(
                "[Quest Camera] PassthroughCameraAccess가 없습니다."
            );

            return;
        }

        if (aiInferenceTest == null)
        {
            Debug.LogError(
                "[Quest Camera] AIInferenceTest가 없습니다."
            );

            return;
        }

        if (!passthroughCameraAccess.isActiveAndEnabled)
        {
            Debug.LogError(
                "[Quest Camera] PassthroughCameraAccess가 " +
                "비활성화되어 있습니다."
            );

            return;
        }

        // 이전 스캔 결과 초기화
        trackedObjects.Clear();
        ConfirmedObjects.Clear();
        nextTrackId = 1;

        aiInferenceTest.ResetCurrentScanResult();

        scanElapsedTime = 0f;
        inferenceElapsedTime = 0f;
        inferenceCount = 0;
        isScanning = true;

        SetStatusText(
            "Scanning...\nPlease keep your head still"
        );

        // 시작 즉시 첫 추론
        RunSingleInference();

        if (printScanLog)
        {
            Debug.Log(
                "[Quest Camera] 개별 물체 추적 스캔 시작"
            );
        }
    }

    private void UpdateScan()
    {
        scanElapsedTime += Time.deltaTime;
        inferenceElapsedTime += Time.deltaTime;

        if (inferenceElapsedTime >= inferenceInterval)
        {
            RunSingleInference();
        }

        if (scanElapsedTime >= scanDuration)
        {
            FinishScan();
        }
    }

    private void RunSingleInference()
    {
        Texture cameraTexture =
            passthroughCameraAccess.GetTexture();

        if (cameraTexture == null)
        {
            if (printScanLog)
            {
                Debug.LogWarning(
                    "[Quest Camera] 카메라 Texture가 " +
                    "아직 준비되지 않았습니다."
                );
            }

            return;
        }

        if (cameraPreview != null)
        {
            cameraPreview.texture = cameraTexture;
        }

        inferenceElapsedTime = 0f;
        inferenceCount++;

        // 한 프레임 YOLO 추론
        aiInferenceTest.RunRealtimeInference(
            cameraTexture
        );

        // 이번 프레임의 탐지 결과를 개별 물체 단위로 추적
        TrackDetections(
            aiInferenceTest.LastDetections
        );

        SetStatusText(
            $"Scanning... {inferenceCount}\n" +
            "Please keep your head still"
        );

        if (printScanLog)
        {
            Debug.Log(
                $"[Quest Camera] {inferenceCount}회차 추론 / " +
                $"현재 추적 물체 {trackedObjects.Count}개"
            );
        }
    }

    private void TrackDetections(
        List<Detection> detections
    )
    {
        if (detections == null ||
            detections.Count == 0)
        {
            return;
        }

        /*
         * 한 프레임에서 하나의 기존 Track에
         * 두 Detection이 동시에 연결되는 것을 방지합니다.
         */
        HashSet<int> matchedTrackIds =
            new HashSet<int>();

        // 정확도가 높은 탐지부터 기존 물체에 연결
        List<Detection> sortedDetections =
            detections
                .OrderByDescending(
                    detection =>
                        detection.confidence
                )
                .ToList();

        foreach (
            Detection detection
            in sortedDetections
        )
        {
            TrackedObject bestTrack = null;
            float bestIou = trackingIouThreshold;

            foreach (
                TrackedObject track
                in trackedObjects
            )
            {
                // 같은 프레임에서 이미 사용한 Track은 제외
                if (matchedTrackIds.Contains(track.id))
                {
                    continue;
                }

                // 클래스가 다르면 다른 물체
                if (track.className != detection.className)
                {
                    continue;
                }

                // 너무 오래 전에 사라진 Track은 다시 사용하지 않음
                int inferenceGap =
                    inferenceCount -
                    track.lastSeenInference;

                if (inferenceGap > maximumTrackingGap)
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

            if (bestTrack != null)
            {
                // 기존 물체가 이번 추론에서도 다시 발견됨
                bestTrack.lastRect =
                    detection.rect;

                bestTrack.detectionCount++;

                bestTrack.confidenceSum +=
                    detection.confidence;

                bestTrack.lastSeenInference =
                    inferenceCount;

                matchedTrackIds.Add(
                    bestTrack.id
                );
            }
            else
            {
                // 겹치는 기존 물체가 없으면 새로운 물체로 등록
                TrackedObject newTrack =
                    new TrackedObject
                    {
                        id = nextTrackId++,
                        className =
                            detection.className,
                        lastRect =
                            detection.rect,
                        detectionCount = 1,
                        confidenceSum =
                            detection.confidence,
                        lastSeenInference =
                            inferenceCount
                    };

                trackedObjects.Add(newTrack);

                matchedTrackIds.Add(
                    newTrack.id
                );
            }
        }
    }

    private void FinishScan()
    {
        isScanning = false;

        // 반복적으로 탐지된 개별 물체만 최종 확정
        ConfirmedObjects =
            trackedObjects
                .Where(track =>
                {
                    float averageConfidence =
                        track.confidenceSum /
                        track.detectionCount;

                    return
                        track.detectionCount >=
                        minimumDetectionCount &&
                        averageConfidence >=
                        minimumAverageConfidence;
                })
                .OrderByDescending(track =>
                    track.detectionCount
                )
                .ThenByDescending(track =>
                    track.confidenceSum /
                    track.detectionCount
                )
                .Select(track =>
                    new ConfirmedObjectInfo
                    {
                        objectId = track.id,
                        className =
                            track.className,
                        detectionCount =
                            track.detectionCount,
                        averageConfidence =
                            track.confidenceSum /
                            track.detectionCount,
                        lastRect =
                            track.lastRect
                    }
                )
                .ToList();

        ShowFinalResult();

        if (printScanLog)
        {
            PrintDetailedResult();
        }

        /*
         * 나중에 퀘스트 코드를 연동할 때는
         * ConfirmedObjects를 QuestGenerator에 전달하면 됩니다.
         *
         * 예:
         * QuestGenerator.Instance
         *     .GenerateQuests(ConfirmedObjects);
         */
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

        // 클래스별 실제 확정 물체 개수 집계
        List<string> classCountLines =
            ConfirmedObjects
                .GroupBy(
                    confirmedObject =>
                        confirmedObject.className
                )
                .OrderByDescending(group =>
                    group.Count()
                )
                .ThenBy(group =>
                    group.Key
                )
                .Select(group =>
                    $"{group.Key} x{group.Count()}"
                )
                .ToList();

        SetStatusText(
            $"Scan complete!\n" +
            $"Total objects: {ConfirmedObjects.Count}\n" +
            string.Join("\n", classCountLines)
        );
    }

    private void PrintDetailedResult()
    {
        List<string> detailLines =
            new List<string>();

        foreach (
            ConfirmedObjectInfo confirmedObject
            in ConfirmedObjects
        )
        {
            detailLines.Add(
                $"Object #{confirmedObject.objectId} / " +
                $"{confirmedObject.className} / " +
                $"{confirmedObject.detectionCount}" +
                $"/{inferenceCount} scans / " +
                $"{confirmedObject.averageConfidence * 100f:0}%"
            );
        }

        Debug.Log(
            $"[Quest Camera] 스캔 완료\n" +
            $"총 추론 횟수: {inferenceCount}\n" +
            $"확정된 실제 물체 수: " +
            $"{ConfirmedObjects.Count}\n" +
            string.Join("\n", detailLines)
        );
    }

    private float CalculateIoU(
        Rect first,
        Rect second
    )
    {
        float intersectionX1 =
            Mathf.Max(
                first.xMin,
                second.xMin
            );

        float intersectionY1 =
            Mathf.Max(
                first.yMin,
                second.yMin
            );

        float intersectionX2 =
            Mathf.Min(
                first.xMax,
                second.xMax
            );

        float intersectionY2 =
            Mathf.Min(
                first.yMax,
                second.yMax
            );

        float intersectionWidth =
            Mathf.Max(
                0f,
                intersectionX2 -
                intersectionX1
            );

        float intersectionHeight =
            Mathf.Max(
                0f,
                intersectionY2 -
                intersectionY1
            );

        float intersectionArea =
            intersectionWidth *
            intersectionHeight;

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

        if (unionArea <= 0f)
        {
            return 0f;
        }

        return intersectionArea /
               unionArea;
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

/*
 * 스캔 중에만 사용하는 내부 추적 객체
 */
internal class TrackedObject
{
    public int id;
    public string className;
    public Rect lastRect;
    public int detectionCount;
    public float confidenceSum;
    public int lastSeenInference;
}

/*
 * 스캔 완료 후 퀘스트 시스템에 넘길 확정 객체
 */
[System.Serializable]
public class ConfirmedObjectInfo
{
    public int objectId;
    public string className;
    public int detectionCount;
    public float averageConfidence;
    public Rect lastRect;
}