using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// Runs YOLO inference for one camera frame.
///
/// Integration fixes:
/// - Initializes in Awake and lazily retries before inference.
/// - Allows QuestCameraYoloTester to use a lower mission-only confidence threshold.
/// - Calculates the anchor count from the output length instead of assuming 8400.
/// - Supports the common [features, anchors] layout and falls back to
///   [anchors, features] when the first layout produces no candidates.
/// - Exposes explicit diagnostics instead of silently returning.
/// </summary>
public class AIInferenceTest : MonoBehaviour
{
    public enum OutputLayout
    {
        Auto,
        FeaturesFirst,
        AnchorsFirst
    }

    [Header("[ AI 모델 설정 ]")]
    public ModelAsset modelAsset;
    public TextAsset classesFile;

    [Header("[ 추론 파라미터 ]")]
    [Range(0f, 1f)] public float confidenceThreshold = 0.4f;
    [Range(0f, 1f)] public float iouThreshold = 0.45f;

    [Tooltip("일반 YOLOv8 출력은 FeaturesFirst입니다. 결과가 계속 0이면 Auto로 둡니다.")]
    public OutputLayout outputLayout = OutputLayout.Auto;

    [Header("[ 디버그 ]")]
    public bool printInitializationLog = true;
    public bool printInferenceSummary = false;

    private Model runtimeModel;
    private Worker worker;
    private string[] classNames;
    private bool isRunningInference;
    private bool initializationAttempted;
    private AIScanResultJson currentScanResult;

    private const int InputSize = 640;

    public int inputSize => InputSize;
    public bool IsReady => worker != null && classNames != null && classNames.Length > 0;
    public string LastFailureReason { get; private set; } = string.Empty;
    public int LastOutputLength { get; private set; }
    public int LastParsedCandidateCount { get; private set; }
    public int LastFinalDetectionCount { get; private set; }

    public List<Detection> LastDetections { get; private set; } = new List<Detection>();
    public Detection? BestDetectionInCurrentScan { get; private set; }
    public AIScanResultJson CurrentScanResult => currentScanResult;

    private void Awake()
    {
        EnsureInitialized();
        ResetCurrentScanResult();
    }

    private void Start()
    {
        // Awake usually initializes first. This is a safe retry for scene-order issues.
        EnsureInitialized();
    }

    public bool EnsureInitialized()
    {
        if (IsReady)
        {
            return true;
        }

        if (classNames == null || classNames.Length == 0)
        {
            LoadClassNames();
        }

        if (worker == null)
        {
            LoadModel();
        }

        initializationAttempted = true;
        return IsReady;
    }

    private void LoadClassNames()
    {
        if (classesFile != null)
        {
            classNames = classesFile.text.Split(
                new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries
            );

            for (int index = 0; index < classNames.Length; index++)
            {
                classNames[index] = classNames[index].Trim();
            }

            if (printInitializationLog)
            {
                Debug.Log($"[AI] 클래스 {classNames.Length}개를 불러왔습니다.");
            }
            return;
        }

        Debug.LogError("[AI Error] classes.txt 파일이 연결되지 않았습니다. 임시 7개 클래스 목록을 사용합니다.");
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

    private void LoadModel()
    {
        if (modelAsset == null)
        {
            LastFailureReason = "ONNX Model Asset 미연결";
            if (!initializationAttempted)
            {
                Debug.LogError("[AI Error] ONNX Model Asset이 연결되지 않았습니다.");
            }
            return;
        }

        try
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
            LastFailureReason = string.Empty;

            if (printInitializationLog)
            {
                Debug.Log("[AI] YOLO 모델과 GPU Worker를 생성했습니다.");
            }
        }
        catch (Exception exception)
        {
            worker = null;
            LastFailureReason = $"Worker 생성 실패: {exception.Message}";
            Debug.LogError($"[AI Error] YOLO Worker 생성 실패\n{exception}");
        }
    }

    /// <summary>
    /// Legacy-compatible API. Uses the Inspector confidence threshold.
    /// </summary>
    public void RunRealtimeInference(Texture cameraTexture)
    {
        TryRunRealtimeInference(cameraTexture, confidenceThreshold);
    }

    /// <summary>
    /// Runs one inference using the supplied per-frame threshold.
    /// Returns true when inference actually executed, even when no object was detected.
    /// </summary>
    public bool TryRunRealtimeInference(Texture cameraTexture, float thresholdOverride)
    {
        LastFailureReason = string.Empty;
        LastParsedCandidateCount = 0;
        LastFinalDetectionCount = 0;
        LastDetections.Clear();

        if (!EnsureInitialized())
        {
            LastFailureReason = string.IsNullOrWhiteSpace(LastFailureReason)
                ? "AI 모델 또는 클래스 초기화 실패"
                : LastFailureReason;
            return false;
        }

        if (cameraTexture == null)
        {
            LastFailureReason = "카메라 Texture가 null";
            return false;
        }

        if (cameraTexture.width <= 16 || cameraTexture.height <= 16)
        {
            LastFailureReason = $"카메라 Texture 크기가 유효하지 않음: {cameraTexture.width}x{cameraTexture.height}";
            return false;
        }

        if (isRunningInference)
        {
            LastFailureReason = "이전 추론이 아직 실행 중";
            return false;
        }

        isRunningInference = true;
        Tensor<float> inputTensor = null;

        try
        {
            inputTensor = new Tensor<float>(
                new TensorShape(1, 3, InputSize, InputSize)
            );

            TextureConverter.ToTensor(cameraTexture, inputTensor);
            worker.Schedule(inputTensor);

            Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
            if (outputTensor == null)
            {
                LastFailureReason = "모델 출력 Tensor 없음";
                Debug.LogWarning("[AI Warning] 모델 출력 Tensor가 없습니다.");
                return false;
            }

            float[] outputData = outputTensor.DownloadToArray();
            LastOutputLength = outputData != null ? outputData.Length : 0;

            float effectiveThreshold = Mathf.Clamp01(thresholdOverride);
            List<Detection> parsedDetections = ParseYoloOutput(outputData, effectiveThreshold);
            LastParsedCandidateCount = parsedDetections.Count;

            List<Detection> finalDetections = ApplyNms(parsedDetections, iouThreshold);
            finalDetections.Sort((first, second) => second.confidence.CompareTo(first.confidence));

            LastDetections = new List<Detection>(finalDetections);
            LastFinalDetectionCount = LastDetections.Count;

            if (finalDetections.Count > 0)
            {
                Detection best = finalDetections[0];
                if (!BestDetectionInCurrentScan.HasValue ||
                    best.confidence > BestDetectionInCurrentScan.Value.confidence)
                {
                    BestDetectionInCurrentScan = best;
                }
            }

            currentScanResult = BuildScanResultJson(finalDetections);

            if (printInferenceSummary)
            {
                Debug.Log(
                    $"[AI Frame] texture={cameraTexture.width}x{cameraTexture.height}, " +
                    $"output={LastOutputLength}, parsed={LastParsedCandidateCount}, " +
                    $"afterNMS={LastFinalDetectionCount}, threshold={effectiveThreshold:F2}"
                );
            }

            return true;
        }
        catch (Exception exception)
        {
            LastFailureReason = exception.Message;
            Debug.LogError($"[AI 추론 오류]\n{exception}");
            return false;
        }
        finally
        {
            inputTensor?.Dispose();
            isRunningInference = false;
        }
    }

    private List<Detection> ParseYoloOutput(float[] outputData, float threshold)
    {
        List<Detection> empty = new List<Detection>();

        if (outputData == null || outputData.Length == 0)
        {
            LastFailureReason = "YOLO 출력 배열이 비어 있음";
            return empty;
        }

        if (classNames == null || classNames.Length == 0)
        {
            LastFailureReason = "클래스 목록 없음";
            return empty;
        }

        int featureCount = 4 + classNames.Length;
        if (featureCount <= 4 || outputData.Length % featureCount != 0)
        {
            LastFailureReason =
                $"YOLO 출력 길이({outputData.Length})가 클래스 {classNames.Length}개 기준 " +
                $"featureCount({featureCount})로 나누어지지 않습니다. classes.txt와 모델 클래스 수를 확인하세요.";
            Debug.LogError($"[AI Error] {LastFailureReason}");
            return empty;
        }

        int anchorCount = outputData.Length / featureCount;

        if (outputLayout == OutputLayout.FeaturesFirst)
        {
            return ParseFeaturesFirst(outputData, anchorCount, featureCount, threshold);
        }

        if (outputLayout == OutputLayout.AnchorsFirst)
        {
            return ParseAnchorsFirst(outputData, anchorCount, featureCount, threshold);
        }

        // Most YOLOv8 Unity exports are [1, features, anchors]. Try that first.
        List<Detection> featuresFirst = ParseFeaturesFirst(
            outputData,
            anchorCount,
            featureCount,
            threshold
        );

        if (featuresFirst.Count > 0)
        {
            return featuresFirst;
        }

        // Some ONNX exports are transposed to [1, anchors, features].
        List<Detection> anchorsFirst = ParseAnchorsFirst(
            outputData,
            anchorCount,
            featureCount,
            threshold
        );

        if (anchorsFirst.Count > 0 && printInferenceSummary)
        {
            Debug.Log("[AI] AnchorsFirst 출력 레이아웃으로 탐지 결과를 읽었습니다.");
        }

        return anchorsFirst;
    }

    private List<Detection> ParseFeaturesFirst(
        float[] outputData,
        int anchorCount,
        int featureCount,
        float threshold)
    {
        List<Detection> detections = new List<Detection>();

        for (int anchorIndex = 0; anchorIndex < anchorCount; anchorIndex++)
        {
            float centerX = outputData[0 * anchorCount + anchorIndex];
            float centerY = outputData[1 * anchorCount + anchorIndex];
            float width = outputData[2 * anchorCount + anchorIndex];
            float height = outputData[3 * anchorCount + anchorIndex];

            float maxScore = float.NegativeInfinity;
            int bestClassId = -1;

            for (int classIndex = 0; classIndex < classNames.Length; classIndex++)
            {
                float score = outputData[(4 + classIndex) * anchorCount + anchorIndex];
                if (score > maxScore)
                {
                    maxScore = score;
                    bestClassId = classIndex;
                }
            }

            TryAddDetection(
                detections,
                centerX,
                centerY,
                width,
                height,
                maxScore,
                bestClassId,
                threshold
            );
        }

        return detections;
    }

    private List<Detection> ParseAnchorsFirst(
        float[] outputData,
        int anchorCount,
        int featureCount,
        float threshold)
    {
        List<Detection> detections = new List<Detection>();

        for (int anchorIndex = 0; anchorIndex < anchorCount; anchorIndex++)
        {
            int baseIndex = anchorIndex * featureCount;
            float centerX = outputData[baseIndex + 0];
            float centerY = outputData[baseIndex + 1];
            float width = outputData[baseIndex + 2];
            float height = outputData[baseIndex + 3];

            float maxScore = float.NegativeInfinity;
            int bestClassId = -1;

            for (int classIndex = 0; classIndex < classNames.Length; classIndex++)
            {
                float score = outputData[baseIndex + 4 + classIndex];
                if (score > maxScore)
                {
                    maxScore = score;
                    bestClassId = classIndex;
                }
            }

            TryAddDetection(
                detections,
                centerX,
                centerY,
                width,
                height,
                maxScore,
                bestClassId,
                threshold
            );
        }

        return detections;
    }

    private void TryAddDetection(
        List<Detection> detections,
        float centerX,
        float centerY,
        float width,
        float height,
        float score,
        int classId,
        float threshold)
    {
        if (!IsFinite(centerX) || !IsFinite(centerY) ||
            !IsFinite(width) || !IsFinite(height) || !IsFinite(score))
        {
            return;
        }

        if (score < threshold || score > 1.01f)
        {
            return;
        }

        if (classId < 0 || classId >= classNames.Length || width <= 0f || height <= 0f)
        {
            return;
        }

        // Reject obviously invalid boxes while allowing moderate letterbox overflow.
        float maxCoordinate = InputSize * 2f;
        if (Mathf.Abs(centerX) > maxCoordinate || Mathf.Abs(centerY) > maxCoordinate ||
            width > maxCoordinate || height > maxCoordinate)
        {
            return;
        }

        detections.Add(new Detection
        {
            classId = classId,
            className = classNames[classId],
            confidence = score,
            rect = new Rect(
                centerX - width * 0.5f,
                centerY - height * 0.5f,
                width,
                height
            )
        });
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private List<Detection> ApplyNms(List<Detection> boxes, float threshold)
    {
        List<Detection> candidates = new List<Detection>(boxes);
        List<Detection> results = new List<Detection>();

        while (candidates.Count > 0)
        {
            candidates.Sort((first, second) => second.confidence.CompareTo(first.confidence));
            Detection best = candidates[0];
            results.Add(best);
            candidates.RemoveAt(0);

            for (int index = candidates.Count - 1; index >= 0; index--)
            {
                Detection candidate = candidates[index];
                if (best.classId == candidate.classId &&
                    CalculateIoU(best.rect, candidate.rect) > threshold)
                {
                    candidates.RemoveAt(index);
                }
            }
        }

        return results;
    }

    private static float CalculateIoU(Rect first, Rect second)
    {
        float x1 = Mathf.Max(first.xMin, second.xMin);
        float y1 = Mathf.Max(first.yMin, second.yMin);
        float x2 = Mathf.Min(first.xMax, second.xMax);
        float y2 = Mathf.Min(first.yMax, second.yMax);
        float intersectionWidth = Mathf.Max(0f, x2 - x1);
        float intersectionHeight = Mathf.Max(0f, y2 - y1);
        float intersection = intersectionWidth * intersectionHeight;
        float firstArea = Mathf.Max(0f, first.width) * Mathf.Max(0f, first.height);
        float secondArea = Mathf.Max(0f, second.width) * Mathf.Max(0f, second.height);
        float union = firstArea + secondArea - intersection;
        return union > 0f ? intersection / union : 0f;
    }

    private AIScanResultJson BuildScanResultJson(List<Detection> detections)
    {
        AIScanResultJson result = new AIScanResultJson
        {
            detectedObjects = new List<string>()
        };

        float totalCleanScore = 100f;
        foreach (Detection detection in detections)
        {
            result.detectedObjects.Add(detection.className);
            totalCleanScore -= 12.5f;
        }

        result.roomCleanlinessScore = Mathf.Clamp(totalCleanScore, 0f, 100f);
        return result;
    }

    public void ResetCurrentScanResult()
    {
        BestDetectionInCurrentScan = null;
        LastDetections.Clear();
        LastFailureReason = string.Empty;
        LastParsedCandidateCount = 0;
        LastFinalDetectionCount = 0;

        currentScanResult = new AIScanResultJson
        {
            roomCleanlinessScore = 100f,
            detectedObjects = new List<string>()
        };
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        worker = null;
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
