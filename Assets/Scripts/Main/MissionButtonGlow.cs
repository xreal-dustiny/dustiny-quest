using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 네비게이션 노트 버튼 뒤에서 Game Blur를 Additive로 반짝이게 합니다.
/// Game Blur는 검은 배경 + 원형 하이라이트이므로 일반 UI Image면 사각/검은 박스처럼 보입니다.
/// </summary>
public class MissionButtonGlow : MonoBehaviour
{
    [Header("Glow")]
    public CanvasGroup canvasGroup;
    public RectTransform glowRect;
    public Image glowImage;

    [Header("Animation")]
    public float animationSpeed = 1.35f;

    [Range(0f, 1f)]
    public float minAlpha = 0.25f;

    [Range(0f, 1f)]
    public float maxAlpha = 0.85f;

    public float minScale = 1.1f;
    public float maxScale = 1.45f;

    private bool shouldGlow;
    private Material additiveMaterial;

    private void Awake()
    {
        ResolveComponents();
        EnsureAdditiveMaterial();
        SetGlow(false);
    }

    private void OnDestroy()
    {
        if (additiveMaterial != null)
        {
            Destroy(additiveMaterial);
            additiveMaterial = null;
        }
    }

    private void Update()
    {
        if (!shouldGlow)
        {
            return;
        }

        ResolveComponents();

        float wave =
            (Mathf.Sin(Time.unscaledTime * animationSpeed * Mathf.PI * 2f) + 1f)
            * 0.5f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = Mathf.Lerp(minAlpha, maxAlpha, wave);
        }

        if (glowRect != null)
        {
            float scale = Mathf.Lerp(minScale, maxScale, wave);
            glowRect.localScale = Vector3.one * scale;
        }
    }

    public void ConfigureSprite(Sprite sprite)
    {
        ResolveComponents();
        EnsureAdditiveMaterial();

        if (glowImage == null)
        {
            return;
        }

        if (sprite != null)
        {
            glowImage.sprite = sprite;
            glowImage.type = Image.Type.Simple;
            glowImage.preserveAspect = true;
            glowImage.color = Color.white;
            glowImage.enabled = true;
            glowImage.material = additiveMaterial;
        }
    }

    public void SetGlow(bool active)
    {
        ResolveComponents();
        EnsureAdditiveMaterial();
        shouldGlow = active;

        if (glowImage != null && additiveMaterial != null)
        {
            glowImage.material = additiveMaterial;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = active ? minAlpha : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (glowRect != null)
        {
            glowRect.localScale = Vector3.one * minScale;
        }

        if (gameObject.activeSelf != active)
        {
            gameObject.SetActive(active);
        }
    }

    private void EnsureAdditiveMaterial()
    {
        if (additiveMaterial != null)
        {
            return;
        }

        // 검은 배경 + 흰 원형 글로우는 Additive에서만 원형으로 보입니다.
        Shader shader = Shader.Find("Mobile/Particles/Additive");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Additive");
        }

        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Particles/Additive");
        }

        if (shader == null)
        {
            shader = Shader.Find("UI/Default");
        }

        if (shader == null)
        {
            return;
        }

        additiveMaterial = new Material(shader)
        {
            name = "NoteButtonGlowAdditive (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void ResolveComponents()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (glowRect == null)
        {
            glowRect = GetComponent<RectTransform>();
        }

        if (glowImage == null)
        {
            glowImage = GetComponent<Image>();
        }

        if (glowImage != null)
        {
            glowImage.raycastTarget = false;
        }

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }
}
