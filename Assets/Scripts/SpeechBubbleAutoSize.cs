using TMPro;
using UnityEngine;

[ExecuteAlways]
public class SpeechBubbleAutoSize : MonoBehaviour
{
    [Header("References")]
    public RectTransform bubbleRect;
    public RectTransform textRect;

    [Tooltip("말풍선 또는 디스크립션에서 실제로 표시할 TMP 텍스트입니다.")]
    public TMP_Text questText;

    [Header("Size Settings")]
    public float maxTextWidth = 600f;
    public Vector2 padding = new Vector2(160f, 90f);
    public Vector2 minBubbleSize = new Vector2(520f, 140f);

    [Header("Position Settings")]
    public bool lockBubblePosition = true;
    public Vector2 bubbleAnchoredPosition = new Vector2(0f, -260f);
    public bool forceCenterAnchorAndPivot = true;

    private string lastText;
    private Vector2 lastPreferredSize;

    private void Reset()
    {
        AutoResolveReferences();
    }

    private void OnEnable()
    {
        AutoResolveReferences();
        ApplyBubblePosition();
        ResizeBubble();
    }

    private void OnValidate()
    {
        AutoResolveReferences();
        ApplyBubblePosition();
        ResizeBubble();
    }

    private void LateUpdate()
    {
        AutoResolveReferences();
        ApplyBubblePosition();
        ResizeBubbleIfNeeded();
    }

    public void SetText(string message)
    {
        AutoResolveReferences();
        if (questText == null)
        {
            return;
        }

        questText.text = message;
        ResizeBubble();
    }

    private void AutoResolveReferences()
    {
        if (bubbleRect == null)
        {
            bubbleRect = GetComponent<RectTransform>();
        }

        if (questText == null)
        {
            questText = GetComponentInChildren<TMP_Text>(true);
        }

        if (textRect == null && questText != null)
        {
            textRect = questText.GetComponent<RectTransform>();
        }
    }

    public void ApplyBubblePosition()
    {
        if (!lockBubblePosition || bubbleRect == null)
        {
            return;
        }

        if (forceCenterAnchorAndPivot)
        {
            bubbleRect.anchorMin = new Vector2(0.5f, 0.5f);
            bubbleRect.anchorMax = new Vector2(0.5f, 0.5f);
            bubbleRect.pivot = new Vector2(0.5f, 0.5f);
        }

        bubbleRect.anchoredPosition = bubbleAnchoredPosition;
    }

    private void ResizeBubbleIfNeeded()
    {
        if (bubbleRect == null || textRect == null || questText == null)
        {
            return;
        }

        PrepareTextForMeasurement();

        Vector2 preferredSize = questText.GetPreferredValues(questText.text, maxTextWidth, 0f);
        bool textChanged = lastText != questText.text;
        bool sizeChanged = (preferredSize - lastPreferredSize).sqrMagnitude > 0.5f;

        if (textChanged || sizeChanged)
        {
            ResizeBubble(preferredSize);
        }
    }

    public void ResizeBubble()
    {
        if (bubbleRect == null || textRect == null || questText == null)
        {
            return;
        }

        PrepareTextForMeasurement();
        Vector2 preferredSize = questText.GetPreferredValues(questText.text, maxTextWidth, 0f);
        ResizeBubble(preferredSize);
    }

    private void PrepareTextForMeasurement()
    {
        // Unity 6 / current TMP replacement for the obsolete enableWordWrapping property.
        questText.textWrappingMode = TextWrappingModes.Normal;
        questText.ForceMeshUpdate();
    }

    private void ResizeBubble(Vector2 preferredSize)
    {
        ApplyBubblePosition();

        float textWidth = Mathf.Min(preferredSize.x, maxTextWidth);
        float textHeight = preferredSize.y;

        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
        textRect.anchoredPosition = Vector2.zero;

        float bubbleWidth = Mathf.Max(textWidth + padding.x, minBubbleSize.x);
        float bubbleHeight = Mathf.Max(textHeight + padding.y, minBubbleSize.y);

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);

        lastText = questText.text;
        lastPreferredSize = preferredSize;
    }
}
