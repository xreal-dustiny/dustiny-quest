using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스타트 버튼을 StartUIManager에 연결합니다.
///
/// 지원 방식:
/// 1. Unity UI Button
/// 2. Meta Interaction SDK의 Interaction Events
/// 3. 선택적 Hand Collider 직접 접촉
///
/// SpriteRenderer 버튼을 그대로 사용한다면
/// Interaction Events에서 PressStartButton()을 연결하세요.
/// </summary>
[DisallowMultipleComponent]
public class StartButton : MonoBehaviour
{
    [Header("Start Flow")]
    [Tooltip("StartManager에 붙어 있는 StartUIManager를 연결하세요.")]
    public StartUIManager startUIManager;

    [Tooltip("켜면 버튼 선택 즉시 메인 씬으로 이동합니다.")]
    public bool startGameImmediately = true;

    [Header("Unity UI Button")]
    [Tooltip("같은 오브젝트에 Unity UI Button이 있으면 자동 연결합니다.")]
    public bool autoBindUnityUIButton = true;

    [Header("Optional Hand Touch")]
    [Tooltip("Hand 태그 Collider가 직접 닿을 때도 실행할 경우에만 켭니다.")]
    public bool allowHandTriggerFallback = false;

    [Tooltip("직접 접촉 입력에 사용할 손 Collider 태그입니다.")]
    public string handTag = "Hand";

    private Button unityButton;
    private bool inputLocked;

    private void Awake()
    {
        ResolveReferences();
        BindUnityButton();
    }

    private void OnEnable()
    {
        inputLocked = false;

        if (unityButton != null)
        {
            unityButton.interactable = true;
        }
    }

    private void OnDestroy()
    {
        if (unityButton != null)
        {
            unityButton.onClick.RemoveListener(
                PressStartButton
            );
        }
    }

    private void ResolveReferences()
    {
        if (startUIManager == null)
        {
            startUIManager =
                FindFirstObjectByType<StartUIManager>();
        }

        if (unityButton == null)
        {
            unityButton = GetComponent<Button>();
        }
    }

    private void BindUnityButton()
    {
        if (!autoBindUnityUIButton)
        {
            return;
        }

        if (unityButton == null)
        {
            return;
        }

        // 기존 Inspector 이벤트를 삭제하지 않고
        // 이 함수만 중복되지 않도록 추가합니다.
        unityButton.onClick.RemoveListener(
            PressStartButton
        );

        unityButton.onClick.AddListener(
            PressStartButton
        );
    }

    /// <summary>
    /// Meta Interaction SDK의 Interaction Events에서
    /// 이 함수를 연결하세요.
    /// </summary>
    public void PressStartButton()
    {
        if (inputLocked)
        {
            return;
        }

        ResolveReferences();

        if (startUIManager == null)
        {
            Debug.LogError(
                "[StartButton] StartUIManager를 찾지 못했습니다. " +
                "Start Manager에 StartUIManager를 붙이고 연결하세요."
            );
            return;
        }

        if (startUIManager.IsStartingGame)
        {
            return;
        }

        if (startGameImmediately)
        {
            startUIManager.StartGame();

            inputLocked = startUIManager.IsStartingGame;

            if (unityButton != null)
            {
                unityButton.interactable = !inputLocked;
            }
        }
        else
        {
            startUIManager.ToNavInform();

            inputLocked = false;

            if (unityButton != null)
            {
                unityButton.interactable = true;
            }
        }
    }

    /// <summary>
    /// 이전 이벤트 이름과 연결되어 있을 경우 사용할 호환 함수입니다.
    /// </summary>
    public void HandleStartButtonClicked()
    {
        PressStartButton();
    }

    /// <summary>
    /// 짧은 이름으로 이벤트에 연결할 수 있는 호환 함수입니다.
    /// </summary>
    public void PressStart()
    {
        PressStartButton();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!allowHandTriggerFallback ||
            other == null)
        {
            return;
        }

        if (!other.CompareTag(handTag))
        {
            return;
        }

        PressStartButton();
    }
}