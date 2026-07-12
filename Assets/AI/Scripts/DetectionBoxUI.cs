using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DetectionBoxUI : MonoBehaviour
{
    [Header("[ 마커 설정 ]")]
    public RectTransform boxRect;
    public TMP_Text markerText;
    public TMP_Text labelText;

    [Min(8f)]
    public float markerSize = 40f;

    [Tooltip("나중에 ☘, ✿, ★ , ◎, ❤,● 같은 걸로 바꿔도 됨")]
    public string markerSymbol = "★";

    public Color markerColor = Color.green;

    private void Awake()
    {
        PrepareUI();
    }

    public static DetectionBoxUI CreateRuntimeBox(
        RectTransform parent
    )
    {
        GameObject markerObject = new GameObject(
            "DetectionMarker",
            typeof(RectTransform),
            typeof(DetectionBoxUI)
        );

        markerObject.transform.SetParent(
            parent,
            false
        );

        DetectionBoxUI detectionMarker =
            markerObject.GetComponent<DetectionBoxUI>();

        detectionMarker.PrepareUI();

        return detectionMarker;
    }

    public void PrepareUI()
    {
        if (boxRect == null)
        {
            boxRect =
                GetComponent<RectTransform>();
        }

        if (markerText == null)
        {
            CreateMarker();
        }

        if (labelText == null)
        {
            CreateLabel();
        }
    }

    private void CreateMarker()
    {
        GameObject markerObject =
            new GameObject(
                "MarkerText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );

        markerObject.transform.SetParent(
            transform,
            false
        );

        RectTransform markerRect =
            markerObject.GetComponent<RectTransform>();

        markerRect.anchorMin =
            new Vector2(0f, 0f);

        markerRect.anchorMax =
            new Vector2(1f, 1f);

        markerRect.pivot =
            new Vector2(0.5f, 0.5f);

        markerRect.anchoredPosition =
            Vector2.zero;

        markerRect.sizeDelta =
            Vector2.zero;

        TextMeshProUGUI tmp =
            markerObject.GetComponent<TextMeshProUGUI>();

        tmp.text = markerSymbol;
        tmp.fontSize = 36f;
        tmp.color = markerColor;
        tmp.alignment =
            TextAlignmentOptions.Center;
        tmp.textWrappingMode =
            TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        markerText = tmp;
    }

    private void CreateLabel()
    {
        GameObject labelObject =
            new GameObject(
                "DetectionLabel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );

        labelObject.transform.SetParent(
            transform,
            false
        );

        RectTransform labelRect =
            labelObject.GetComponent<RectTransform>();

        labelRect.anchorMin =
            new Vector2(0.5f, 0f);

        labelRect.anchorMax =
            new Vector2(0.5f, 0f);

        labelRect.pivot =
            new Vector2(0.5f, 1f);

        labelRect.anchoredPosition =
            new Vector2(0f, -8f);

        labelRect.sizeDelta =
            new Vector2(220f, 40f);

        TextMeshProUGUI tmp =
            labelObject.GetComponent<TextMeshProUGUI>();

        tmp.fontSize = 20f;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment =
            TextAlignmentOptions.Center;
        tmp.textWrappingMode =
            TextWrappingModes.NoWrap;
        tmp.overflowMode =
            TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        labelText = tmp;
    }

    public void SetDetection(
        Detection detection,
        float scaleX,
        float scaleY,
        float overlayWidth,
        float overlayHeight,
        bool mirrorX,
        bool mirrorY
    )
    {
        PrepareUI();

        Rect modelRect =
            detection.rect;

        float width =
            modelRect.width * scaleX;

        float height =
            modelRect.height * scaleY;

        float left =
            modelRect.xMin * scaleX;

        float bottom =
            overlayHeight
            - modelRect.yMax * scaleY;

        if (mirrorX)
        {
            left =
                overlayWidth
                - left
                - width;
        }

        if (mirrorY)
        {
            bottom =
                overlayHeight
                - bottom
                - height;
        }

        float centerX =
            left + width * 0.5f;

        float centerY =
            bottom + height * 0.5f;

        centerX = Mathf.Clamp(
            centerX,
            0f,
            overlayWidth
        );

        centerY = Mathf.Clamp(
            centerY,
            0f,
            overlayHeight
        );

        boxRect.sizeDelta =
            new Vector2(
                markerSize,
                markerSize
            );

        // 현재 boxRect pivot이 (0,0) 기준으로 사용되므로
        // 마커의 중심이 탐지 중심에 오도록 반만큼 빼줌
        boxRect.anchoredPosition =
            new Vector2(
                centerX - markerSize * 0.5f,
                centerY - markerSize * 0.5f
            );

        if (markerText != null)
        {
            markerText.text = markerSymbol;
            markerText.color = markerColor;
        }

        if (labelText != null)
        {
            labelText.text =
                $"{detection.className} " +
                $"{detection.confidence * 100f:0}%";
        }
    }
}