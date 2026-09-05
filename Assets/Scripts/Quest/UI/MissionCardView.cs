using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NotePage의 미션 한 줄을 표시하고, MissionCheck를 수동 완료 버튼으로 사용합니다.
///
/// 잘못된 Inspector 연결을 방지하기 위해 MissionCheck Button이 이 카드 밖의
/// MenuButton/NavigationButton 등에 연결되어 있으면 자동으로 무시합니다.
/// MissionCheck 오브젝트에 Button이 없어도 실행 중 자동으로 추가합니다.
/// </summary>
public class MissionCardView : MonoBehaviour
{
    [Header("[ 카드 UI 연결 ]")]
    [SerializeField] private TextMeshProUGUI missionText;
    [SerializeField] private Button missionCheckButton;
    [SerializeField] private Image missionCheckImage;

    [Header("[ 체크박스 이미지 ]")]
    [SerializeField] private Sprite uncheckedSprite;
    [SerializeField] private Sprite checkedSprite;

    [Tooltip("체크 표시가 별도 자식 오브젝트일 때만 연결합니다. MissionCheck 버튼 자체는 넣지 마세요.")]
    [SerializeField] private GameObject separateCheckObject;

    [Header("[ 입력 영역 ]")]
    [SerializeField, Min(1f), Tooltip("손 레이/터치로 누르기 쉽도록 MissionCheck의 최소 크기를 보장합니다.")]
    private float minimumCheckHitSize = 60f;

    [Header("[ 레이아웃 ]")]
    [SerializeField] private LayoutElement layoutElement;

    private string boundQuestId;
    private Action<string> onCheckRequested;

    public Button CheckButton => missionCheckButton;
    public Image CheckImage => missionCheckImage;

    private void Awake()
    {
        ResolveReferences(createMissingButton: true);
        ConfigureRaycastTargets();
    }

    private void OnEnable()
    {
        ResolveReferences(createMissingButton: true);
        ConfigureRaycastTargets();
        ForceVisibleState();
    }

    private void OnValidate()
    {
        minimumCheckHitSize = Mathf.Max(1f, minimumCheckHitSize);
        ResolveReferences(createMissingButton: false);
        ConfigureRaycastTargets();
    }

    private void OnDestroy()
    {
        RemoveRuntimeListener();
    }

    public void SetPreferredHeight(float height)
    {
        ResolveReferences(createMissingButton: Application.isPlaying);

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }

        if (layoutElement == null && Application.isPlaying)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }

        if (layoutElement == null)
        {
            return;
        }

        float safeHeight = Mathf.Max(1f, height);
        layoutElement.minHeight = safeHeight;
        layoutElement.preferredHeight = safeHeight;
        layoutElement.flexibleHeight = 0f;
    }

    public float GetConfiguredHeight(float fallback)
    {
        ResolveReferences(createMissingButton: false);

        if (layoutElement != null)
        {
            if (layoutElement.preferredHeight > 0f)
            {
                return layoutElement.preferredHeight;
            }

            if (layoutElement.minHeight > 0f)
            {
                return layoutElement.minHeight;
            }
        }

        RectTransform rect = transform as RectTransform;
        if (rect != null && rect.rect.height > 0.01f)
        {
            return rect.rect.height;
        }

        return Mathf.Max(1f, fallback);
    }

    public void Bind(
        QuestData quest,
        string displayLabel,
        bool interactionEnabled,
        Action<string> checkRequested)
    {
        ResolveReferences(createMissingButton: true);
        ConfigureRaycastTargets();

        bool hasQuest = quest != null;
        boundQuestId = hasQuest ? quest.questId : string.Empty;
        onCheckRequested = checkRequested;

        gameObject.SetActive(hasQuest);
        if (!hasQuest)
        {
            return;
        }

        transform.localScale = Vector3.one;
        ForceVisibleState();

        if (missionText != null)
        {
            missionText.gameObject.SetActive(true);
            missionText.enabled = true;
            missionText.alpha = 1f;
            missionText.text = displayLabel ?? string.Empty;
            missionText.raycastTarget = false;
        }

        bool isCleared = quest.isCleared;
        ApplyCheckVisual(isCleared);

        if (missionCheckButton != null)
        {
            missionCheckButton.gameObject.SetActive(true);
            missionCheckButton.enabled = true;
            missionCheckButton.interactable = !isCleared && interactionEnabled;

            RemoveRuntimeListener();
            missionCheckButton.onClick.AddListener(HandleCheckPressed);
        }
        else
        {
            Debug.LogError(
                $"[MissionCardView] '{name}' 아래에서 MissionCheck Button을 만들지 못했습니다. " +
                "MissionCardTemplate 아래에 MissionCheck 오브젝트와 Image가 있는지 확인하세요."
            );
        }

        if (missionCheckImage != null)
        {
            missionCheckImage.gameObject.SetActive(true);
            missionCheckImage.enabled = true;
            missionCheckImage.raycastTarget = true;

            Color color = missionCheckImage.color;
            color.a = 1f;
            missionCheckImage.color = color;
        }
    }

    public void Unbind()
    {
        boundQuestId = string.Empty;
        onCheckRequested = null;
        RemoveRuntimeListener();
        gameObject.SetActive(false);
    }

    private void HandleCheckPressed()
    {
        if (string.IsNullOrWhiteSpace(boundQuestId))
        {
            Debug.LogWarning($"[MissionCardView] '{name}'에 연결된 Quest ID가 없어 체크를 무시합니다.");
            return;
        }

        Debug.Log($"[미션 수동 체크] {boundQuestId}");
        onCheckRequested?.Invoke(boundQuestId);
    }

    private void ApplyCheckVisual(bool isCleared)
    {
        ResolveCheckboxSprites();

        if (missionCheckImage != null)
        {
            Sprite targetSprite = isCleared ? checkedSprite : uncheckedSprite;
            if (targetSprite != null)
            {
                missionCheckImage.overrideSprite = null;
                missionCheckImage.sprite = targetSprite;
            }
        }

        if (separateCheckObject != null &&
            (missionCheckButton == null ||
             separateCheckObject != missionCheckButton.gameObject))
        {
            separateCheckObject.SetActive(isCleared);
        }
    }

    private void RemoveRuntimeListener()
    {
        if (missionCheckButton != null)
        {
            missionCheckButton.onClick.RemoveListener(HandleCheckPressed);
        }
    }

    private void ResolveReferences(bool createMissingButton)
    {
        if (missionText != null && !IsInsideThisCard(missionText.transform))
        {
            Debug.LogWarning(
                $"[MissionCardView] '{name}'의 Mission Text가 카드 밖의 '{missionText.name}'에 연결되어 자동 해제합니다."
            );
            missionText = null;
        }

        if (missionCheckButton != null && !IsInsideThisCard(missionCheckButton.transform))
        {
            Debug.LogWarning(
                $"[MissionCardView] '{name}'의 Mission Check Button이 카드 밖의 " +
                $"'{missionCheckButton.name}'에 연결되어 자동 해제합니다. " +
                "MissionCheck 자식 오브젝트를 사용합니다."
            );
            missionCheckButton = null;
        }

        if (missionCheckImage != null && !IsInsideThisCard(missionCheckImage.transform))
        {
            missionCheckImage = null;
        }

        Transform exactText = FindChildExact(transform, "MissionText");
        if (exactText != null)
        {
            missionText = exactText.GetComponent<TextMeshProUGUI>();
        }
        else if (missionText == null)
        {
            missionText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        Transform exactCheck = FindChildExact(transform, "MissionCheck");
        if (exactCheck != null)
        {
            missionCheckImage = exactCheck.GetComponent<Image>() ??
                                exactCheck.GetComponentInChildren<Image>(true);

            missionCheckButton = exactCheck.GetComponent<Button>();
            if (missionCheckButton == null && createMissingButton && Application.isPlaying)
            {
                missionCheckButton = exactCheck.gameObject.AddComponent<Button>();
                Debug.Log(
                    $"[MissionCardView] '{exactCheck.name}'에 수동 체크용 Button을 자동 추가했습니다."
                );
            }
        }

        if (missionCheckButton == null)
        {
            Button[] localButtons = GetComponentsInChildren<Button>(true);
            foreach (Button candidate in localButtons)
            {
                if (candidate != null && IsInsideThisCard(candidate.transform))
                {
                    missionCheckButton = candidate;
                    break;
                }
            }
        }

        if (missionCheckImage == null && missionCheckButton != null)
        {
            missionCheckImage = missionCheckButton.targetGraphic as Image ??
                                missionCheckButton.GetComponent<Image>() ??
                                missionCheckButton.GetComponentInChildren<Image>(true);
        }

        if (missionCheckButton != null && missionCheckImage != null)
        {
            missionCheckButton.targetGraphic = missionCheckImage;
        }

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }

        EnsureCheckHitSize();
        ResolveCheckboxSprites();
    }

    private void ConfigureRaycastTargets()
    {
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null)
            {
                continue;
            }

            // 카드 배경과 글자가 체크 버튼보다 먼저 레이를 잡지 않도록 합니다.
            graphic.raycastTarget = graphic == missionCheckImage;
        }

        if (missionCheckImage != null)
        {
            missionCheckImage.raycastTarget = true;
        }
    }

    private void EnsureCheckHitSize()
    {
        if (missionCheckImage == null)
        {
            return;
        }

        RectTransform checkRect = missionCheckImage.rectTransform;
        Vector2 size = checkRect.sizeDelta;
        size.x = Mathf.Max(size.x, minimumCheckHitSize);
        size.y = Mathf.Max(size.y, minimumCheckHitSize);
        checkRect.sizeDelta = size;
    }

    private void ResolveCheckboxSprites()
    {
        if (uncheckedSprite == null && missionCheckImage != null)
        {
            uncheckedSprite = missionCheckImage.sprite;
        }

        if (checkedSprite == null && missionCheckButton != null)
        {
            SpriteState state = missionCheckButton.spriteState;
            checkedSprite = state.selectedSprite != null
                ? state.selectedSprite
                : state.pressedSprite != null
                    ? state.pressedSprite
                    : state.highlightedSprite;
        }
    }

    private void ForceVisibleState()
    {
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null)
            {
                continue;
            }

            graphic.enabled = true;
            Color color = graphic.color;
            if (color.a <= 0.001f)
            {
                color.a = 1f;
                graphic.color = color;
            }
        }

        ConfigureRaycastTargets();
    }

    private bool IsInsideThisCard(Transform candidate)
    {
        return candidate != null &&
               (candidate == transform || candidate.IsChildOf(transform));
    }

    private static Transform FindChildExact(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null &&
                string.Equals(child.name, exactName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }
}
