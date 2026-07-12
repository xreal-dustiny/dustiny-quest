using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

public class AIInferenceTest : MonoBehaviour
{
    [Header("[ AI 모델 설정 ]")]
    public ModelAsset modelAsset;
    public TextAsset classesFile;

    [Header("[ 추론 파라미터 ]")]
    [Range(0f, 1f)]
    public float confidenceThreshold = 0.4f;

    [Range(0f, 1f)]
    public float iouThreshold = 0.45f;

    private Model runtimeModel;
    private Worker worker;
    private string[] classNames;

    // 추론 중복 실행 방지
    private bool isRunningInference;

    // 현재 스캔 결과 저장
    private AIScanResultJson currentScanResult;

    // 가장 최근 한 번의 추론 결과
    public List<Detection> LastDetections
    {
        get;
        private set;
    } = new List<Detection>();

    // YOLOv8 입력 해상도
    private const int InputSize = 640;

    // QuestCameraYoloTester에서 입력 해상도를 사용할 때 접근
    public int inputSize => InputSize;

    // 현재 스캔에서 정확도가 가장 높은 탐지 결과
    public Detection? BestDetectionInCurrentScan
    {
        get;
        private set;
    }

    // 현재 JSON 스캔 결과
    public AIScanResultJson CurrentScanResult =>
        currentScanResult;

    // YOLOv8 640x640 기준 Anchor 수
    private const int NumAnchors = 8400;

    private void Start()
    {
        LoadClassNames();
        LoadModel();
        ResetCurrentScanResult();
    }

    private void LoadClassNames()
    {
        if (classesFile != null)
        {
            // classes.txt 파일을 줄 단위로 나누어 저장
            classNames = classesFile.text.Split(
                new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries
            );

            // 클래스 이름 앞뒤 공백 제거
            for (int i = 0; i < classNames.Length; i++)
            {
                classNames[i] =
                    classNames[i].Trim();
            }

            Debug.Log(
                $"[AI] 클래스 {classNames.Length}개를 불러왔습니다."
            );
        }
        else
        {
            Debug.LogError(
                "[AI Error] classes.txt 파일이 연결되지 않았습니다."
            );

            // classes.txt 미연결 시 임시 클래스 목록 사용
            classNames = new[]
            {
                "cable_charger",
                "cup_bottle",
                "laptop",
                "paper_book",
                "small_device",
                "toy_decor",
                "writing_tool"
            };
        }
    }

    private void LoadModel()
    {
        if (modelAsset == null)
        {
            Debug.LogError(
                "[AI Error] ONNX Model Asset이 연결되지 않았습니다."
            );

            return;
        }

        // ONNX 모델 로드
        runtimeModel =
            ModelLoader.Load(modelAsset);

        // GPU를 사용하는 추론 Worker 생성
        worker = new Worker(
            runtimeModel,
            BackendType.GPUCompute
        );

        Debug.Log(
            "[AI] YOLO 모델과 GPU Worker를 생성했습니다."
        );
    }

    /// <summary>
    /// 메타퀘스트 카메라 Texture를 전달받아 실시간 추론 실행
    /// </summary>
    public void RunRealtimeInference(
        Texture cameraTexture
    )
    {
        if (worker == null ||
            cameraTexture == null)
        {
            return;
        }

        // 이전 추론이 끝나지 않은 경우 중복 실행하지 않음
        if (isRunningInference)
        {
            return;
        }

        isRunningInference = true;

        // 이전 추론 결과가 다음 추론에 재사용되지 않도록 초기화
        LastDetections.Clear();

        Tensor<float> inputTensor = null;

        try
        {
            // YOLO 입력 크기에 맞는 Tensor 생성
            inputTensor =
                new Tensor<float>(
                    new TensorShape(
                        1,
                        3,
                        InputSize,
                        InputSize
                    )
                );

            // Quest 카메라 Texture를 640x640 RGB Tensor로 변환
            TextureConverter.ToTensor(
                cameraTexture,
                inputTensor
            );

            // 모델 추론 실행
            worker.Schedule(inputTensor);

            // 모델 출력 Tensor 가져오기
            Tensor<float> outputTensor =
                worker.PeekOutput()
                as Tensor<float>;

            if (outputTensor == null)
            {
                Debug.LogWarning(
                    "[AI Warning] 모델 출력 Tensor가 없습니다."
                );
                return;
            }

            // GPU에 있는 결과를 CPU 배열로 변환
            float[] outputData =
                outputTensor.DownloadToArray();

            // YOLO 출력 데이터에서 바운딩박스 추출
            List<Detection> detections =
                ParseYoloOutput(outputData);

            // 중복 바운딩박스 제거
            List<Detection> finalDetections =
                ApplyNms(
                    detections,
                    iouThreshold
                );

            // 정확도가 높은 순서대로 정렬
            finalDetections.Sort(
                (first, second) =>
                    second.confidence.CompareTo(
                        first.confidence
                    )
            );

            // QuestCameraYoloTester가 여러 추론 결과를 집계할 수 있도록 저장
            LastDetections =
                new List<Detection>(finalDetections);

                        // 가장 정확도가 높은 탐지 결과 저장
            // 이번 프레임의 최고 탐지 결과를 스캔 전체 결과와 비교
            if (finalDetections.Count > 0)
            {
                Detection bestDetectionInThisFrame =
                    finalDetections[0];

                // 기존 탐지 결과가 없거나 이번 결과의 정확도가 더 높으면 갱신
                if (!BestDetectionInCurrentScan.HasValue ||
                    bestDetectionInThisFrame.confidence >
                    BestDetectionInCurrentScan.Value.confidence)
                {
                    BestDetectionInCurrentScan =
                        bestDetectionInThisFrame;

                    Debug.Log(
                        $"[AI 스캔 결과 갱신] " +
                        $"{bestDetectionInThisFrame.className} " +
                        $"{bestDetectionInThisFrame.confidence:F2}"
                    );
                }
            }



            //퀘스트 기능 연동 전까지 임시 비활성화
            /*
            // 감지된 클래스 목록을 노이즈 필터로 전달
            if (YoloFilterTrigger.Instance != null)
            {
                YoloFilterTrigger.Instance
                    .ProcessIncomingYoloData(
                        detectedClassNames
                    );
            }
            */

            // 현재 스캔 결과 저장
            currentScanResult =
                BuildScanResultJson(
                    finalDetections
                );

            // 필요할 경우 로그 확인용으로 사용
            // Debug.Log(
            //     $"[AI 실시간 분석 데이터]\n" +
            //     JsonUtility.ToJson(currentScanResult, true)
            // );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[AI 추론 오류]\n{exception}"
            );
        }
        finally
        {
            // 입력 Tensor 메모리 해제
            if (inputTensor != null)
            {
                inputTensor.Dispose();
            }

            isRunningInference = false;
        }
    }

    /// <summary>
    /// YOLOv8 출력 배열을 Detection 목록으로 변환
    /// </summary>
    private List<Detection> ParseYoloOutput(
        float[] outputData
    )
    {
        List<Detection> detections =
            new List<Detection>();

        if (outputData == null)
        {
            return detections;
        }

        if (classNames == null ||
            classNames.Length == 0)
        {
            Debug.LogError(
                "[AI Error] 클래스 목록이 없습니다."
            );

            return detections;
        }

        /*
         * YOLOv8 출력 형태
         *
         * [1, 4 + 클래스 수, 8400]
         *
         * 0 = centerX
         * 1 = centerY
         * 2 = width
         * 3 = height
         * 4 이후 = 클래스별 점수
         */

        int numClasses =
            classNames.Length;

        int rowCount =
            4 + numClasses;

        int expectedLength =
            rowCount * NumAnchors;

        if (outputData.Length < expectedLength)
        {
            Debug.LogError(
                "[AI Error] YOLO 출력 배열 크기가 예상과 다릅니다.\n" +
                $"현재 크기: {outputData.Length}\n" +
                $"예상 최소 크기: {expectedLength}"
            );

            return detections;
        }

        for (
            int anchorIndex = 0;
            anchorIndex < NumAnchors;
            anchorIndex++
        )
        {
            // 바운딩박스 중심 좌표와 크기
            float centerX =
                outputData[
                    0 * NumAnchors
                    + anchorIndex
                ];

            float centerY =
                outputData[
                    1 * NumAnchors
                    + anchorIndex
                ];

            float width =
                outputData[
                    2 * NumAnchors
                    + anchorIndex
                ];

            float height =
                outputData[
                    3 * NumAnchors
                    + anchorIndex
                ];

            // 가장 높은 클래스 점수 탐색
            float maxScore = 0f;
            int bestClassId = -1;

            for (
                int classIndex = 0;
                classIndex < numClasses;
                classIndex++
            )
            {
                int outputIndex =
                    (4 + classIndex)
                    * NumAnchors
                    + anchorIndex;

                float score =
                    outputData[outputIndex];

                if (score > maxScore)
                {
                    maxScore = score;
                    bestClassId = classIndex;
                }
            }

            // 설정한 Confidence보다 낮은 결과 제외
            if (maxScore < confidenceThreshold)
            {
                continue;
            }

            if (bestClassId < 0 ||
                bestClassId >= classNames.Length)
            {
                continue;
            }

            // 중심 좌표를 좌측 상단 기준 Rect 좌표로 변환
            Rect detectionRect =
                new Rect(
                    centerX - width / 2f,
                    centerY - height / 2f,
                    width,
                    height
                );

            Detection detection =
                new Detection
                {
                    classId = bestClassId,

                    className =
                        classNames[bestClassId],

                    confidence = maxScore,

                    rect = detectionRect
                };

            detections.Add(detection);
        }

        return detections;
    }

    /// <summary>
    /// 같은 클래스의 중복 바운딩박스를 제거
    /// </summary>
    private List<Detection> ApplyNms(
        List<Detection> boxes,
        float iouThresholdValue
    )
    {
        List<Detection> candidates =
            new List<Detection>(boxes);

        List<Detection> results =
            new List<Detection>();

        while (candidates.Count > 0)
        {
            // 가장 정확도가 높은 결과를 먼저 선택
            candidates.Sort(
                (first, second) =>
                    second.confidence.CompareTo(
                        first.confidence
                    )
            );

            Detection bestDetection =
                candidates[0];

            results.Add(bestDetection);
            candidates.RemoveAt(0);

            for (
                int i = candidates.Count - 1;
                i >= 0;
                i--
            )
            {
                Detection candidate =
                    candidates[i];

                bool isSameClass =
                    bestDetection.classId
                    == candidate.classId;

                float iou =
                    CalculateIoU(
                        bestDetection.rect,
                        candidate.rect
                    );

                // 같은 클래스이면서 많이 겹치는 박스만 제거
                if (isSameClass &&
                    iou > iouThresholdValue)
                {
                    candidates.RemoveAt(i);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 두 바운딩박스의 IoU 계산
    /// </summary>
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
                intersectionX2 - intersectionX1
            );

        float intersectionHeight =
            Mathf.Max(
                0f,
                intersectionY2 - intersectionY1
            );

        float intersectionArea =
            intersectionWidth
            * intersectionHeight;

        float firstArea =
            Mathf.Max(0f, first.width)
            * Mathf.Max(0f, first.height);

        float secondArea =
            Mathf.Max(0f, second.width)
            * Mathf.Max(0f, second.height);

        float unionArea =
            firstArea
            + secondArea
            - intersectionArea;

        if (unionArea <= 0f)
        {
            return 0f;
        }

        return intersectionArea / unionArea;
    }

    /// <summary>
    /// 탐지 결과를 JSON 구조로 변환
    /// </summary>
    private AIScanResultJson BuildScanResultJson(
        List<Detection> finalDetections
    )
    {
        AIScanResultJson result =
            new AIScanResultJson();

        result.detectedObjects =
            new List<string>();

        float totalCleanScore = 100f;

        foreach (
            Detection detection
            in finalDetections
        )
        {
            result.detectedObjects.Add(
                detection.className
            );

            // 감지된 물체 한 개당 정돈도 점수 감소
            totalCleanScore -= 12.5f;
        }

        result.roomCleanlinessScore =
            Mathf.Clamp(
                totalCleanScore,
                0f,
                100f
            );

        return result;
    }

    /// <summary>
    /// 현재 스캔 결과와 화면의 바운딩박스 초기화
    /// </summary>
    public void ResetCurrentScanResult()
    {
        // 최고 탐지 결과 초기화
        BestDetectionInCurrentScan = null;

        // 현재 저장된 탐지 결과 초기화
        currentScanResult =
            new AIScanResultJson
            {
                roomCleanlinessScore = 100f,
                detectedObjects = new List<string>()
            };

        // 퀘스트 기능 연동 전까지 임시 비활성화
        /*
        if (YoloFilterTrigger.Instance != null)
        {
            YoloFilterTrigger.Instance
                .ProcessIncomingYoloData(
                    new List<string>()
                );
        }
        */
        LastDetections.Clear();
        Debug.Log(
            "[AI] 현재 스캔 결과를 초기화했습니다."
        );
    }

    private void OnDestroy()
    {
        // 게임 종료 시 Worker 메모리 해제
        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }
    }
}

[Serializable]
public struct Detection
{
    public int classId;
    public string className;
    public float confidence;
    public Rect rect;
}

[Serializable]
public class AIScanResultJson
{
    public float roomCleanlinessScore;
    public List<string> detectedObjects;
}