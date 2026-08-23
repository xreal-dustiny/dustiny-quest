using UnityEngine;

public class MissionButtonGlow : MonoBehaviour
{
    [Header("Glow")]
    public CanvasGroup canvasGroup;
    public RectTransform glowRect;

    [Header("Animation")]
    public float animationSpeed = 1.5f;

    [Range(0f, 1f)]
    public float minAlpha = 0.12f;

    [Range(0f, 1f)]
    public float maxAlpha = 0.45f;

    public float minScale = 0.95f;
    public float maxScale = 1.15f;

    private bool shouldGlow;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (glowRect == null)
            glowRect = GetComponent<RectTransform>();

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        SetGlow(false);
    }

    private void Update()
    {
        if (!shouldGlow)
            return;

        float wave =
            (Mathf.Sin(Time.unscaledTime * animationSpeed * Mathf.PI * 2f) + 1f)
            * 0.5f;

        canvasGroup.alpha =
            Mathf.Lerp(minAlpha, maxAlpha, wave);

        float scale =
            Mathf.Lerp(minScale, maxScale, wave);

        glowRect.localScale =
            Vector3.one * scale;
    }

    public void SetGlow(bool active)
    {
        shouldGlow = active;

        if (canvasGroup != null)
            canvasGroup.alpha = active ? minAlpha : 0f;

        if (glowRect != null)
            glowRect.localScale = Vector3.one * minScale;

        gameObject.SetActive(active);
    }
}