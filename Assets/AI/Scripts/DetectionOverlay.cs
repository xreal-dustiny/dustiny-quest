using System.Collections.Generic;
using UnityEngine;

public class DetectionOverlay : MonoBehaviour
{
    [Header("[ 화면 표시 영역 ]")]
    [Tooltip(
        "카메라 화면과 같은 위치와 크기의 RectTransform입니다. " +
        "비워두면 이 오브젝트의 RectTransform을 사용합니다."
    )]
    public RectTransform overlayRoot;

    [Header("[ 바운딩박스 프리팹 - 선택사항 ]")]
    [Tooltip(
        "연결하지 않아도 코드가 자동으로 박스를 생성합니다."
    )]
    public DetectionBoxUI detectionBoxPrefab;

    [Header("[ YOLO 입력 크기 ]")]
    public float modelWidth = 640f;
    public float modelHeight = 640f;

    [Header("[ 카메라 화면 반전 설정 ]")]
    [Tooltip("카메라 영상이 좌우 반전되어 있을 때 체크")]
    public bool mirrorX = false;

    [Tooltip("카메라 영상이 상하 반전되어 있을 때 체크")]
    public bool mirrorY = false;

    private readonly List<DetectionBoxUI> boxPool =
        new List<DetectionBoxUI>();

    private void Awake()
    {
        if (overlayRoot == null)
        {
            overlayRoot =
                transform as RectTransform;
        }

        if (overlayRoot == null)
        {
            Debug.LogError(
                "[DetectionOverlay] RectTransform이 필요합니다."
            );
        }
    }

    public void ShowDetections(
        List<Detection> detections,
        int visibleCount
    )
    {
        if (overlayRoot == null)
        {
            return;
        }

        if (detections == null)
        {
            HideAll();
            return;
        }

        visibleCount = Mathf.Clamp(
            visibleCount,
            0,
            detections.Count
        );

        if (visibleCount == 0)
        {
            HideAll();
            return;
        }

        EnsurePoolSize(visibleCount);

        /*
         * UI Layout이 아직 계산되지 않았을 수 있으므로
         * 현재 Canvas의 배치를 한 번 갱신함.
         */
        Canvas.ForceUpdateCanvases();

        float overlayWidth =
            overlayRoot.rect.width;

        float overlayHeight =
            overlayRoot.rect.height;

        if (overlayWidth <= 0f ||
            overlayHeight <= 0f)
        {
            return;
        }

        float scaleX =
            overlayWidth / modelWidth;

        float scaleY =
            overlayHeight / modelHeight;

        for (int i = 0;
             i < boxPool.Count;
             i++)
        {
            DetectionBoxUI box = boxPool[i];

            if (i < visibleCount)
            {
                box.gameObject.SetActive(true);

                box.SetDetection(
                    detections[i],
                    scaleX,
                    scaleY,
                    overlayWidth,
                    overlayHeight,
                    mirrorX,
                    mirrorY
                );
            }
            else
            {
                box.gameObject.SetActive(false);
            }
        }
    }

    private void EnsurePoolSize(int requiredCount)
    {
        while (boxPool.Count < requiredCount)
        {
            DetectionBoxUI newBox;

            if (detectionBoxPrefab != null)
            {
                newBox = Instantiate(
                    detectionBoxPrefab,
                    overlayRoot
                );
            }
            else
            {
                newBox =
                    DetectionBoxUI.CreateRuntimeBox(
                        overlayRoot
                    );
            }

            newBox.name =
                $"DetectionBox_{boxPool.Count}";

            RectTransform rect =
                newBox.GetComponent<RectTransform>();

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;

            boxPool.Add(newBox);
        }
    }

    public void HideAll()
    {
        for (int i = 0;
             i < boxPool.Count;
             i++)
        {
            if (boxPool[i] != null)
            {
                boxPool[i]
                    .gameObject
                    .SetActive(false);
            }
        }
    }

    private void OnDisable()
    {
        HideAll();
    }
}