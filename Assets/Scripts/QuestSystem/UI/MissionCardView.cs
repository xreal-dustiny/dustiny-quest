using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One reusable mission card inside the vertical Scroll View content.
/// The card keeps its own checkbox visuals and forwards the bound quest ID when clicked.
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

    [Tooltip("별도 CheckMark 자식을 사용하는 구조에서만 연결합니다. MissionCheck 버튼 자체는 넣지 마세요.")]
    [SerializeField] private GameObject separateCheckObject;

    [Header("[ 레이아웃 ]")]
    [SerializeField] private LayoutElement layoutElement;

    private string boundQuestId;
    private Action<string> onCheckRequested;

    public Button CheckButton => missionCheckButton;
    public Image CheckImage => missionCheckImage;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (missionCheckButton != null)
        {
            missionCheckButton.onClick.RemoveListener(HandleCheckPressed);
        }
    }

    public void SetPreferredHeight(float height)
    {
        ResolveReferences();

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }

        if (layoutElement == null)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.minHeight = Mathf.Max(1f, height);
        layoutElement.preferredHeight = Mathf.Max(1f, height);
        layoutElement.flexibleHeight = 0f;
    }

    public void Bind(
        QuestData quest,
        string displayLabel,
        bool interactionEnabled,
        Action<string> checkRequested)
    {
        ResolveReferences();

        boundQuestId = quest != null ? quest.questId : string.Empty;
        onCheckRequested = checkRequested;

        if (missionText != null)
        {
            missionText.text = displayLabel ?? string.Empty;
        }

        bool hasQuest = quest != null;
        bool isCleared = hasQuest && quest.isCleared;

        ApplyCheckVisual(isCleared);

        if (missionCheckButton != null)
        {
            missionCheckButton.gameObject.SetActive(hasQuest);
            missionCheckButton.interactable = hasQuest && !isCleared && interactionEnabled;
            missionCheckButton.onClick.RemoveListener(HandleCheckPressed);
            missionCheckButton.onClick.AddListener(HandleCheckPressed);
        }

        gameObject.SetActive(hasQuest);
    }

    public void Unbind()
    {
        boundQuestId = string.Empty;
        onCheckRequested = null;

        if (missionCheckButton != null)
        {
            missionCheckButton.onClick.RemoveListener(HandleCheckPressed);
        }

        gameObject.SetActive(false);
    }

    private void HandleCheckPressed()
    {
        if (string.IsNullOrWhiteSpace(boundQuestId))
        {
            return;
        }

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
            (missionCheckButton == null || separateCheckObject != missionCheckButton.gameObject))
        {
            separateCheckObject.SetActive(isCleared);
        }
    }

    private void ResolveReferences()
    {
        if (missionText == null)
        {
            missionText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (missionCheckButton == null)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            if (buttons.Length > 0)
            {
                missionCheckButton = buttons[0];
            }
        }

        if (missionCheckImage == null && missionCheckButton != null)
        {
            missionCheckImage = missionCheckButton.targetGraphic as Image;

            if (missionCheckImage == null)
            {
                missionCheckImage = missionCheckButton.GetComponent<Image>();
            }
        }

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }

        ResolveCheckboxSprites();
    }

    private void ResolveCheckboxSprites()
    {
        if (uncheckedSprite == null && missionCheckImage != null)
        {
            uncheckedSprite = missionCheckImage.sprite;
        }

        if (checkedSprite == null && missionCheckButton != null)
        {
            SpriteState spriteState = missionCheckButton.spriteState;
            checkedSprite = spriteState.selectedSprite != null
                ? spriteState.selectedSprite
                : spriteState.pressedSprite != null
                    ? spriteState.pressedSprite
                    : spriteState.highlightedSprite;
        }
    }
}
