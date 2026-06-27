using TMPro;
using UnityEngine;

[ExecuteAlways]
public class SpeechBubbleAutoSize : MonoBehaviour
{
    [Header("References")]
    public RectTransform bubbleRect;
    public RectTransform textRect;
    public TMP_Text questText;

    [Header("Size Settings")]
    public float maxTextWidth = 600f;
    public Vector2 padding = new Vector2(160f, 90f);
    public Vector2 minBubbleSize = new Vector2(520f, 140f);

    private string lastText;

    private void Reset()
    {
        bubbleRect = GetComponent<RectTransform>();
        questText = GetComponentInChildren<TMP_Text>();

        if (questText != null)
            textRect = questText.GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        ResizeBubble();
    }

    private void LateUpdate()
    {
        if (questText == null) return;

        if (lastText != questText.text)
        {
            ResizeBubble();
        }
    }

    public void SetText(string message)
    {
        if (questText == null) return;

        questText.text = message;
        ResizeBubble();
    }

    public void ResizeBubble()
    {
        if (bubbleRect == null || textRect == null || questText == null) return;

        questText.enableWordWrapping = true;

        questText.ForceMeshUpdate();

        Vector2 preferredSize = questText.GetPreferredValues(
            questText.text,
            maxTextWidth,
            0
        );

        float textWidth = Mathf.Min(preferredSize.x, maxTextWidth);
        float textHeight = preferredSize.y;

        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
        textRect.anchoredPosition = Vector2.zero;

        float bubbleWidth = Mathf.Max(textWidth + padding.x, minBubbleSize.x);
        float bubbleHeight = Mathf.Max(textHeight + padding.y, minBubbleSize.y);

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);

        lastText = questText.text;
    }
}