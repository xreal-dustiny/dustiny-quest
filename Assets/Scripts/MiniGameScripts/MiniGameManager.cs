using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MiniGameManager : MonoBehaviour
{
    public enum ToolType { None, Sponge, Shower }
    public enum GamePhase { Intro, SpongeTurn, ShowerTurn, SetComplete, GameClear }

    private const string MainSceneName = "MainMRScene";
    private const string BackPromptInProgress =
        "아직 게임이 끝나지 않았어. 중간에 나가면 다시 이어할 수 없어. 그래도 메인 씬으로 돌아갈래?";
    private const string BackPromptGameClear = "메인 씬으로 돌아갈래?";
    private const string GameClearSpeech =
        "보상이 지급됐어. 메인 씬으로 돌아가자!";
    private static readonly Vector2 SpeechBubblePosition = new Vector2(0f, -40f);

    [Header("Manager / Controller References")]
    public DollController dollController;

    [Header("Item GameObjects")]
    public GameObject spongeObject;
    public GameObject showerObject;
    public ParticleSystem showerParticle;

    [Header("Hand Tracking (Right Hand Tool Holding)")]
    public Transform rightHandAnchor;
    [Tooltip("오른손 손목(RightHandAnchor) 기준 손바닥 중심입니다.")]
    public Vector3 palmLocalPosition = new Vector3(0f, -0.03f, 0.075f);
    public Vector3 spongeHoldOffset = new Vector3(0f, 0f, 0f);
    public Vector3 spongeHoldEuler = new Vector3(0f, -90f, 0f);
    public Vector3 spongeGrabLocalPosition = new Vector3(0f, 0.012f, 0f);

    public Vector3 showerHoldOffset = new Vector3(0f, 0f, 0f);
    public Vector3 showerHoldEuler = new Vector3(80f, 0f, 0f);
    public Vector3 showerGrabLocalPosition = new Vector3(0f, 0.25f, 0f);

    private Transform spongeGrabPoint;
    private Transform showerGrabPoint;

    [Header("Bathtub Grab Volume")]
    [Tooltip("오른손이 이 트리거 안에 있을 때만 선택한 도구를 집습니다.")]
    public Collider bathtubVolume;

    [Header("Navigation Bar Buttons")]
    public Button spongeButton;
    public Button showerButton;
    public Button backButton;

    [Header("Dialogue UI")]
    public TMP_Text speechText;
    public TMP_Text descriptionText;
    public Button speechConfirmButton;
    public Button descriptionConfirmButton;
    public GameObject speechBubbleObject;
    public GameObject descriptionObject;

    [Header("Audio & Voice Clips")]
    public AudioSource voiceAudioSource;
    public AudioClip startVoice;
    public AudioClip spongeVoice;
    public AudioClip showerVoice;
    public AudioClip levelUpVoice;
    public AudioClip clearVoice;

    [Header("Tool Loop SFX")]
    public AudioClip spongeLoopClip;
    public AudioClip showerLoopClip;
    public AudioSource spongeSfxSource;
    public AudioSource showerSfxSource;
    [Range(0f, 1f)] public float toolSfxVolume = 0.55f;
    public float toolSfxFadeSpeed = 5f;

    [Header("Gameplay State")]
    [SerializeField] private ToolType currentTool = ToolType.None;
    [SerializeField] private GamePhase currentPhase = GamePhase.Intro;
    [SerializeField] private int currentSet = 1;
    [SerializeField] private int currentCycle = 0;
    [SerializeField] private int cleanlinessLevel = 1;
    [SerializeField] private float turnTargetDuration = 3f;
    [SerializeField] private float turnProgressDuration = 0f;

    private bool isTouchingSponge = false;
    private bool isTouchingShower = false;
    private bool isReturningHomePrompt = false;
    private string previousSpeechText = string.Empty;
    private bool isHoldingTool;
    private bool spongeRestCached;
    private bool showerRestCached;
    private Vector3 spongeRestPosition;
    private Quaternion spongeRestRotation;
    private Vector3 showerRestPosition;
    private Quaternion showerRestRotation;
    private bool speechCanvasPinned;
    private bool wasPostClearToolTouching;
    private OVRHand rightHand;
    private bool wasRightIndexPinching;
    private bool wasRightMiddlePinching;
    private float ignoreBackUntil;

    public GameObject SpongeObject => spongeObject;
    public GameObject ShowerObject => showerObject;

    private void Awake()
    {
        AutoFindSceneReferences();
    }

private void Start()
    {
        AutoFindSceneReferences();
        SetupUI();
        PinSpeechCanvasInWorld();
        ConfigureShowerWaterVisibility();
        CacheToolRestPoses();
        StartCoroutine(PlayIntroThenStartGame());
    }




private IEnumerator PlayIntroThenStartGame()
    {
        currentPhase = GamePhase.Intro;
        ignoreBackUntil = Time.unscaledTime + 4.5f;
        EquipTool(ToolType.None);

        if (backButton != null)
        {
            backButton.interactable = false;
        }

        if (dollController != null)
        {
            dollController.gameObject.SetActive(false);
        }

        SetSpeechText("내 인형을 빨아줘.. 너무 더러워졌어...");
        PlayVoice(startVoice);
        yield return new WaitForSeconds(3.2f);

        if (dollController != null)
        {
            dollController.gameObject.SetActive(true);
            dollController.FacePlayerInitially();
        }

        BindButton(backButton, OnBackButtonClicked);
        if (backButton != null)
        {
            backButton.interactable = false;
        }

        ignoreBackUntil = Time.unscaledTime + 1.25f;
        EquipTool(ToolType.Sponge);
        StartMiniGame();
    }



private void Update()
    {
        ProcessGameTurn();
        UpdateToolLoopSfx();
        UpdateReturnPromptPinch();
        UpdateBackButtonArming();
    }


    private void LateUpdate()
    {
        UpdateToolHandTransform();
        PinSpeechCanvasInWorld();
    }

    private void AutoFindSceneReferences()
    {
        if (dollController == null)
        {
            dollController = FindFirstObjectByType<DollController>();
        }

        if (spongeObject == null)
        {
            spongeObject = GameObject.Find("Sponge");
        }

        if (showerObject == null)
        {
            showerObject = GameObject.Find("Showering");
        }

        EnsureToolGrabPoints();

        if (showerParticle == null)
        {
            var pGo = GameObject.Find("Shower");
            if (pGo != null) showerParticle = pGo.GetComponent<ParticleSystem>();
        }

        if (rightHandAnchor == null)
        {
            var rGo = GameObject.Find("RightHandAnchor");
            if (rGo != null) rightHandAnchor = rGo.transform;
        }

        ResolveRightHand();

        EnsureBathtubVolume();

        if (spongeButton == null)
        {
            var btnGo = GameObject.Find("SpongeButton");
            if (btnGo != null) spongeButton = btnGo.GetComponent<Button>();
        }

        if (showerButton == null)
        {
            var btnGo = GameObject.Find("ShoweringButton");
            if (btnGo != null) showerButton = btnGo.GetComponent<Button>();
        }

        if (backButton == null)
        {
            var btnGo = GameObject.Find("BackButton");
            if (btnGo != null) backButton = btnGo.GetComponent<Button>();
        }

        if (speechText == null)
        {
            var textGo = GameObject.Find("DurryText");
            if (textGo != null) speechText = textGo.GetComponent<TMP_Text>();
        }

        if (descriptionText == null)
        {
            var textGo = GameObject.Find("DescriptionText");
            if (textGo != null) descriptionText = textGo.GetComponent<TMP_Text>();
        }

        if (speechConfirmButton == null)
        {
            var btnGo = GameObject.Find("SpeechConfirmButton");
            if (btnGo != null) speechConfirmButton = btnGo.GetComponent<Button>();
        }

        if (descriptionConfirmButton == null)
        {
            var btnGo = GameObject.Find("DescriptionConfirmButton");
            if (btnGo != null) descriptionConfirmButton = btnGo.GetComponent<Button>();
        }

        if (speechBubbleObject == null)
        {
            speechBubbleObject = GameObject.Find("SpeechBubble");
        }

        if (descriptionObject == null)
        {
            descriptionObject = GameObject.Find("Description");
        }

        if (voiceAudioSource == null)
        {
            voiceAudioSource = GetComponent<AudioSource>();
            if (voiceAudioSource == null) voiceAudioSource = gameObject.AddComponent<AudioSource>();
        }

#if UNITY_EDITOR
        if (startVoice == null)
            startVoice = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/Voice/Durry_Start_01.wav");
        if (spongeVoice == null)
            spongeVoice = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/Voice/Durry_RequestClean_01.wav");
        if (showerVoice == null)
            showerVoice = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/Voice/Durry_FindDust_01.wav");
        if (levelUpVoice == null)
            levelUpVoice = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/Voice/Durry_LevelUp_01.wav");
        if (clearVoice == null)
            clearVoice = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/Voice/Durry_Clear_01.wav");
        if (spongeLoopClip == null)
            spongeLoopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/SFX/bubble.mp3");
        if (showerLoopClip == null)
            showerLoopClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Dustiny/Audio/SFX/shower.mp3");
#endif
    }

private void SetupUI()
    {
        CacheNavButtonSprites();

        BindButton(spongeButton, () => EquipTool(ToolType.Sponge));
        BindButton(showerButton, () => EquipTool(ToolType.Shower));
        BindButton(speechConfirmButton, OnSpeechConfirmClicked);

        if (backButton != null)
        {
            backButton.onClick = new Button.ButtonClickedEvent();
            backButton.interactable = false;
        }

        if (descriptionConfirmButton != null)
        {
            descriptionConfirmButton.gameObject.SetActive(false);
        }

        SetSpeechConfirmVisible(false);
        HideDescription();
        PlaceSpeechBubbleAtDescriptionPosition();
        EnsureToolGrabPoints();
        DisableConflictingHandGrabs();
        DisableBrokenSampleComponents();
        EnsureUiPointerModule();
        EnsureToolSfxSources();
        CacheToolRestPoses();
    }


    private void CacheNavButtonSprites()
    {
        ResetNavButtonAppearance(spongeButton);
        ResetNavButtonAppearance(showerButton);
        ResetNavButtonAppearance(backButton);
    }

    private static void ResetNavButtonAppearance(Button button)
    {
        if (button == null || button.image == null)
        {
            return;
        }

        button.image.color = Color.white;
        button.transform.localScale = Vector3.one;
    }

private static void BindButton(Button button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() =>
        {
            action();
            DustinySfx.PlayYes();
        });
    }

    private void HideDescription()
    {
        if (descriptionObject != null)
        {
            descriptionObject.SetActive(false);
        }

        if (descriptionConfirmButton != null)
        {
            descriptionConfirmButton.gameObject.SetActive(false);
        }
    }

    private void PlaceSpeechBubbleAtDescriptionPosition()
    {
        if (speechBubbleObject == null)
        {
            return;
        }

        speechBubbleObject.SetActive(true);

        var autoSize = speechBubbleObject.GetComponent<SpeechBubbleAutoSize>();
        if (autoSize != null)
        {
            autoSize.lockBubblePosition = true;
            autoSize.forceCenterAnchorAndPivot = true;
            autoSize.bubbleAnchoredPosition = SpeechBubblePosition;
            autoSize.ApplyBubblePosition();
            autoSize.ResizeBubble();
            return;
        }

        var rt = speechBubbleObject.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = SpeechBubblePosition;
        }
    }

public void StartMiniGame()
    {
        currentSet = 1;
        currentCycle = 0;
        cleanlinessLevel = 1;
        isReturningHomePrompt = false;
        previousSpeechText = string.Empty;

        if (dollController != null)
        {
            dollController.SetCleanlinessVisual(cleanlinessLevel);
            dollController.SetBubbleProgress(0f, true);
        }

        StartSpongeTurn();
    }

    private void StartSpongeTurn()
    {
        currentPhase = GamePhase.SpongeTurn;
        turnTargetDuration = Random.Range(2.0f, 4.0f);
        turnProgressDuration = 0f;

        if (currentCycle == 0 && currentSet == 1)
        {
            SetSpeechText("나 좀 씻겨줄래? 아래 스펀지 버튼을 누르고, 나를 살살 문질러줘.");
        }
        else
        {
            SetSpeechText("좋아, 스펀지 버튼을 누르고 한 번 더 문질러줄래?");
        }

        PlayVoice(spongeVoice);
    }

    private void StartShowerTurn()
    {
        currentPhase = GamePhase.ShowerTurn;
        turnTargetDuration = Random.Range(2.0f, 4.0f);
        turnProgressDuration = 0f;

        SetSpeechText("거품이 많이 났어! 이번엔 샤워기 버튼을 눌러서 헹궈줘.");
        PlayVoice(showerVoice);
    }

    private void ProcessGameTurn()
    {
        if (isReturningHomePrompt)
        {
            return;
        }

        if (currentPhase == GamePhase.GameClear)
        {
            UpdatePostClearToolVisuals();
            return;
        }

        if (currentPhase == GamePhase.SpongeTurn)
        {
            if (isTouchingSponge && currentTool == ToolType.Sponge && isHoldingTool)
            {
                turnProgressDuration += Time.deltaTime;
                float progress = Mathf.Clamp01(turnProgressDuration / turnTargetDuration);

                if (dollController != null)
                {
                    dollController.SetBubbleProgress(progress, true);
                }

                if (turnProgressDuration >= turnTargetDuration)
                {
                    StartShowerTurn();
                }
            }
        }
        else if (currentPhase == GamePhase.ShowerTurn)
        {
            if (isTouchingShower && currentTool == ToolType.Shower && isHoldingTool)
            {
                turnProgressDuration += Time.deltaTime;
                float progress = Mathf.Clamp01(turnProgressDuration / turnTargetDuration);

                if (dollController != null)
                {
                    dollController.SetBubbleProgress(progress, false);
                }

                if (turnProgressDuration >= turnTargetDuration)
                {
                    OnCycleComplete();
                }
            }
        }
    }

private void OnCycleComplete()
    {
        currentCycle++;

        if (currentCycle >= 3)
        {
            currentCycle = 0;
            cleanlinessLevel++;

            if (dollController != null)
            {
                dollController.SetCleanlinessVisual(cleanlinessLevel);
            }

            if (cleanlinessLevel >= 3)
            {
                currentPhase = GamePhase.GameClear;
                wasPostClearToolTouching = false;
                if (dollController != null)
                {
                    dollController.SetBubbleProgress(0f, true);
                }

                SetSpeechText(GameClearSpeech);
                PlayVoice(clearVoice);

                if (CreditManager.Instance != null)
                {
                    CreditManager.Instance.AddCredit(30);
                }

                if (CleanlinessManager.Instance != null)
                {
                    CleanlinessManager.Instance.OnCleanSuccess();
                }

                return;
            }

            currentSet++;
            SetSpeechText("와, 나 조금 더 깨끗해진 것 같아. 조금만 더 씻겨줘!");
            PlayVoice(levelUpVoice);
            StartCoroutine(TransitionToNextSpongeTurn());
            return;
        }

        StartSpongeTurn();
    }


    private IEnumerator TransitionToNextSpongeTurn()
    {
        currentPhase = GamePhase.SetComplete;
        yield return new WaitForSeconds(1.5f);
        StartSpongeTurn();
    }

    public void EquipTool(ToolType tool)
    {
        if (isReturningHomePrompt)
        {
            CancelReturnHomePrompt();
        }

        currentTool = tool;

        if (spongeObject != null)
        {
            spongeObject.SetActive(tool == ToolType.Sponge);
            if (tool == ToolType.Sponge && !IsHandInsideBathtub())
            {
                RestoreToolRestPose(spongeObject.transform, spongeRestCached, spongeRestPosition, spongeRestRotation);
            }
        }

        if (showerObject != null)
        {
            showerObject.SetActive(tool == ToolType.Shower);
            if (tool == ToolType.Shower && !IsHandInsideBathtub())
            {
                RestoreToolRestPose(showerObject.transform, showerRestCached, showerRestPosition, showerRestRotation);
            }
        }

        if (isHoldingTool && !IsHandInsideBathtub())
        {
            isHoldingTool = false;
        }

        UpdateShowerParticle();
        UpdateNavButtonVisuals();
        UpdateToolLoopSfx();
    }

    private void UpdateNavButtonVisuals()
    {
        if (EventSystem.current == null)
        {
            return;
        }

        GameObject selected = null;
        if (currentTool == ToolType.Sponge && spongeButton != null)
        {
            selected = spongeButton.gameObject;
        }
        else if (currentTool == ToolType.Shower && showerButton != null)
        {
            selected = showerButton.gameObject;
        }

        EventSystem.current.SetSelectedGameObject(selected);
    }

    private void UpdateToolHandTransform()
    {
        if (rightHandAnchor == null || currentTool == ToolType.None)
        {
            return;
        }

        if (IsHandInsideBathtub())
        {
            HoldSelectedTool();
            return;
        }

        if (isHoldingTool)
        {
            DropHeldTool();
        }
    }

    private void AlignGrabToPalm(
        Transform tool,
        Transform grabPoint,
        Vector3 palmLocalPos,
        Vector3 holdEuler)
    {
        if (tool == null || rightHandAnchor == null)
        {
            return;
        }

        Quaternion palmRot = rightHandAnchor.rotation * Quaternion.Euler(holdEuler);
        Vector3 palmPos = rightHandAnchor.TransformPoint(palmLocalPos);
        tool.rotation = palmRot;

        if (grabPoint != null)
        {
            tool.position += palmPos - grabPoint.position;
        }
        else
        {
            tool.position = palmPos;
        }
    }

private void EnsureToolGrabPoints()
    {
        if (spongeObject != null)
        {
            spongeGrabPoint = EnsureGrabPoint(spongeObject.transform, spongeGrabLocalPosition);
        }

        if (showerObject != null)
        {
            Transform nozzle = FindChildTransformByName(showerObject.transform, "ShoweringPoint");
            if (nozzle != null)
            {
                Vector3 nozzleLocal = showerObject.transform.InverseTransformPoint(nozzle.position);
                Vector3 towardHandle = -nozzleLocal;
                if (towardHandle.sqrMagnitude > 0.0001f)
                {
                    showerGrabLocalPosition = nozzleLocal + towardHandle.normalized * 0.02f;
                }
                else
                {
                    showerGrabLocalPosition = nozzleLocal;
                }
            }

            showerGrabPoint = EnsureGrabPoint(showerObject.transform, showerGrabLocalPosition);
        }
    }


    private static Vector3 GetRendererLocalCenter(GameObject tool, Vector3 fallback)
    {
        if (tool == null)
        {
            return fallback;
        }

        Renderer renderer = tool.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = tool.GetComponentInChildren<Renderer>();
        }

        return renderer != null ? renderer.localBounds.center : fallback;
    }

    private static Transform EnsureGrabPoint(Transform tool, Vector3 localPosition)
    {
        Transform grab = tool.Find("Grabpoint");
        if (grab == null)
        {
            var grabObject = new GameObject("Grabpoint");
            grab = grabObject.transform;
            grab.SetParent(tool, false);
        }

        grab.localPosition = localPosition;
        grab.localRotation = Quaternion.identity;
        grab.localScale = Vector3.one;
        return grab;
    }

    private void DisableConflictingHandGrabs()
    {
        DisableHandGrabOn(spongeObject);
        DisableHandGrabOn(showerObject);
    }

    private static void DisableHandGrabOn(GameObject tool)
    {
        if (tool == null)
        {
            return;
        }

        var handGrabs = tool.GetComponents<MonoBehaviour>();
        for (int i = 0; i < handGrabs.Length; i++)
        {
            MonoBehaviour behaviour = handGrabs[i];
            if (behaviour == null)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName == "HandGrabInteractable"
                || typeName == "PointableElement"
                || typeName == "RoundedBoxVideoController")
            {
                behaviour.enabled = false;
            }
        }
    }

    private void HoldSelectedTool()
    {
        if (currentTool == ToolType.Sponge && spongeObject != null && spongeObject.activeSelf)
        {
            isHoldingTool = true;
            AlignGrabToPalm(
                spongeObject.transform,
                spongeGrabPoint,
                palmLocalPosition + spongeHoldOffset,
                spongeHoldEuler);
        }
        else if (currentTool == ToolType.Shower && showerObject != null && showerObject.activeSelf)
        {
            isHoldingTool = true;
            AlignGrabToPalm(
                showerObject.transform,
                showerGrabPoint,
                palmLocalPosition + showerHoldOffset,
                showerHoldEuler);
        }

        UpdateShowerParticle();
    }

private void DropHeldTool()
    {
        isHoldingTool = false;
        UpdateShowerParticle();

        if (currentTool == ToolType.Sponge)
        {
            RestoreToolRestPose(spongeObject != null ? spongeObject.transform : null, spongeRestCached, spongeRestPosition, spongeRestRotation);
        }
        else if (currentTool == ToolType.Shower)
        {
            RestoreToolRestPose(showerObject != null ? showerObject.transform : null, showerRestCached, showerRestPosition, showerRestRotation);
        }
    }

    private static void RestoreToolRestPose(
        Transform tool,
        bool cached,
        Vector3 restPosition,
        Quaternion restRotation)
    {
        if (tool == null || !cached)
        {
            return;
        }

        tool.position = restPosition;
        tool.rotation = restRotation;
    }

private void UpdateShowerParticle()
    {
        if (showerParticle == null)
        {
            return;
        }

        bool shouldPlay = isHoldingTool && currentTool == ToolType.Shower;
        ParticleSystem[] systems = showerParticle.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (system == null)
            {
                continue;
            }

            if (shouldPlay)
            {
                if (!system.isPlaying)
                {
                    system.Play(true);
                }
            }
            else if (system.isPlaying)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

private bool IsHandInsideBathtub()
    {
        if (bathtubVolume == null || rightHandAnchor == null || currentTool == ToolType.None)
        {
            return false;
        }

        Vector3 palm = rightHandAnchor.TransformPoint(palmLocalPosition);
        return bathtubVolume.bounds.Contains(palm);
    }

    private void CacheToolRestPoses()
    {
        if (spongeObject != null && !spongeRestCached)
        {
            spongeRestPosition = spongeObject.transform.position;
            spongeRestRotation = spongeObject.transform.rotation;
            spongeRestCached = true;
        }

        if (showerObject != null && !showerRestCached)
        {
            showerRestPosition = showerObject.transform.position;
            showerRestRotation = showerObject.transform.rotation;
            showerRestCached = true;
        }
    }

private void EnsureBathtubVolume()
    {
        if (bathtubVolume != null)
        {
            return;
        }

        GameObject existing = GameObject.Find("BathtubVolume");
        if (existing != null)
        {
            bathtubVolume = existing.GetComponent<Collider>();
            if (bathtubVolume != null)
            {
                return;
            }
        }

        GameObject bath = GameObject.Find("Bath_mesh");
        if (bath == null)
        {
            Debug.LogWarning("[MiniGame] Bath_mesh를 찾지 못해 욕조 잡기 영역을 만들지 못했습니다.");
            return;
        }

        Renderer bathRenderer = bath.GetComponent<Renderer>();
        GameObject volumeObject = existing != null ? existing : new GameObject("BathtubVolume");
        volumeObject.transform.SetParent(bath.transform, false);
        volumeObject.transform.localRotation = Quaternion.identity;
        volumeObject.transform.localScale = Vector3.one;

        BoxCollider box = volumeObject.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = volumeObject.AddComponent<BoxCollider>();
        }

        box.isTrigger = true;
        if (bathRenderer != null)
        {
            Bounds localBounds = bathRenderer.localBounds;
            volumeObject.transform.localPosition = localBounds.center + Vector3.up * 0.04f;
            box.center = Vector3.zero;
            box.size = Vector3.Scale(localBounds.size, new Vector3(0.72f, 0.62f, 0.72f));
        }
        else
        {
            volumeObject.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            box.size = new Vector3(0.78f, 0.45f, 0.38f);
        }

        bathtubVolume = box;

        if (bath.GetComponent<MeshCollider>() == null)
        {
            MeshCollider meshCollider = bath.AddComponent<MeshCollider>();
            meshCollider.convex = false;
        }
    }

    private void DisableBrokenSampleComponents()
    {
        DisableByTypeName(spongeObject, "RoundedBoxVideoController");
        DisableByTypeName(showerObject, "RoundedBoxVideoController");
        DisableImmersiveDebuggerIcons(spongeButton != null ? spongeButton.gameObject : null);
        DisableImmersiveDebuggerIcons(showerButton != null ? showerButton.gameObject : null);
        DisableImmersiveDebuggerIcons(backButton != null ? backButton.gameObject : null);
    }

    private static void DisableByTypeName(GameObject target, string typeName)
    {
        if (target == null || string.IsNullOrEmpty(typeName))
        {
            return;
        }

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && behaviours[i].GetType().Name == typeName)
            {
                behaviours[i].enabled = false;
            }
        }
    }

    private static void DisableImmersiveDebuggerIcons(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null)
            {
                continue;
            }

            System.Type type = behaviours[i].GetType();
            if (type.Name == "Icon"
                && type.FullName != null
                && type.FullName.Contains("ImmersiveDebugger"))
            {
                behaviours[i].enabled = false;
            }
        }
    }

    private void EnsureUiPointerModule()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = FindFirstObjectByType<EventSystem>();
        }

        if (eventSystem == null)
        {
            return;
        }

        eventSystem.sendNavigationEvents = false;
        eventSystem.SetSelectedGameObject(null);

        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        BaseInputModule pointable = null;
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] == null)
            {
                continue;
            }

            if (modules[i].GetType().Name == "PointableCanvasModule")
            {
                pointable = modules[i];
                modules[i].enabled = true;
                continue;
            }

            modules[i].enabled = false;
        }

        if (pointable != null)
        {
            return;
        }

        System.Type moduleType = FindTypeByName("Oculus.Interaction.PointableCanvasModule");
        if (moduleType == null)
        {
            Debug.LogWarning("[MiniGame] PointableCanvasModule을 찾지 못했습니다. 기본 UI 입력을 유지합니다.");
            for (int i = 0; i < modules.Length; i++)
            {
                if (modules[i] != null)
                {
                    modules[i].enabled = true;
                    break;
                }
            }

            return;
        }

        eventSystem.gameObject.AddComponent(moduleType);
    }

    private static System.Type FindTypeByName(string fullName)
    {
        System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            System.Type type = assemblies[i].GetType(fullName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private void EnsureToolSfxSources()
    {
        spongeSfxSource = EnsureLoopSource(spongeSfxSource, spongeObject, "SpongeLoopSfx", spongeLoopClip);
        showerSfxSource = EnsureLoopSource(showerSfxSource, showerObject, "ShowerLoopSfx", showerLoopClip);
    }

    private AudioSource EnsureLoopSource(
        AudioSource existing,
        GameObject tool,
        string childName,
        AudioClip clip)
    {
        if (existing != null)
        {
            ConfigureLoopSource(existing, clip);
            return existing;
        }

        Transform host = tool != null ? tool.transform : transform;
        Transform child = host.Find(childName);
        AudioSource source = child != null ? child.GetComponent<AudioSource>() : null;
        if (source == null)
        {
            GameObject sourceObject = child != null ? child.gameObject : new GameObject(childName);
            if (child == null)
            {
                sourceObject.transform.SetParent(host, false);
            }

            source = sourceObject.GetComponent<AudioSource>();
            if (source == null)
            {
                source = sourceObject.AddComponent<AudioSource>();
            }
        }

        ConfigureLoopSource(source, clip);
        return source;
    }

    private void ConfigureLoopSource(AudioSource source, AudioClip clip)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = 0.2f;
        source.maxDistance = 8f;
        source.volume = 0f;
        source.clip = clip;
    }

    private void UpdateToolLoopSfx()
    {
        bool playSponge = isHoldingTool
            && currentTool == ToolType.Sponge
            && isTouchingSponge
            && !isReturningHomePrompt;
        bool playShower = isHoldingTool
            && currentTool == ToolType.Shower
            && !isReturningHomePrompt;

        TickLoopSource(spongeSfxSource, spongeLoopClip, playSponge);
        TickLoopSource(showerSfxSource, showerLoopClip, playShower);
    }

    private void TickLoopSource(AudioSource source, AudioClip clip, bool shouldPlay)
    {
        if (source == null || clip == null)
        {
            return;
        }

        if (source.clip != clip)
        {
            source.clip = clip;
        }

        if (shouldPlay)
        {
            if (!source.isPlaying)
            {
                source.time = 0f;
                source.Play();
            }

            source.volume = Mathf.MoveTowards(source.volume, toolSfxVolume, Time.deltaTime * toolSfxFadeSpeed);
            return;
        }

        source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * toolSfxFadeSpeed * 1.4f);
        if (source.volume <= 0.001f && source.isPlaying)
        {
            source.Stop();
        }
    }

    private void StopAllToolSfx()
    {
        if (spongeSfxSource != null)
        {
            spongeSfxSource.Stop();
            spongeSfxSource.volume = 0f;
        }

        if (showerSfxSource != null)
        {
            showerSfxSource.Stop();
            showerSfxSource.volume = 0f;
        }
    }

    public void SetTouchState(bool touchingSponge, bool touchingShower)
    {
        isTouchingSponge = touchingSponge;
        isTouchingShower = touchingShower;
    }

    private void SetSpeechText(string text)
    {
        if (speechBubbleObject != null && !speechBubbleObject.activeSelf)
        {
            speechBubbleObject.SetActive(true);
        }

        PlaceSpeechBubbleAtDescriptionPosition();

        if (speechText != null)
        {
            speechText.text = text;
        }

        var autoSize = speechBubbleObject != null
            ? speechBubbleObject.GetComponent<SpeechBubbleAutoSize>()
            : null;
        if (autoSize != null)
        {
            autoSize.SetText(text);
        }
    }

public void OnBackButtonClicked()
    {
        if (currentPhase == GamePhase.Intro || Time.unscaledTime < ignoreBackUntil)
        {
            return;
        }

        if (isReturningHomePrompt)
        {
            CancelReturnHomePrompt();
            return;
        }

        isReturningHomePrompt = true;
        previousSpeechText = speechText != null ? speechText.text : string.Empty;

        string prompt = currentPhase == GamePhase.GameClear
            ? BackPromptGameClear
            : BackPromptInProgress;

        SetSpeechText(prompt);
        SetSpeechConfirmVisible(false);
    }

    public void OnSpeechConfirmClicked()
    {
        if (isReturningHomePrompt)
        {
            ReturnToMainScene();
            return;
        }

        SetSpeechConfirmVisible(false);
    }

    private void CancelReturnHomePrompt()
    {
        isReturningHomePrompt = false;
        SetSpeechConfirmVisible(false);
        UpdateNavButtonVisuals();

        if (!string.IsNullOrEmpty(previousSpeechText))
        {
            SetSpeechText(previousSpeechText);
        }
    }

    private void SetSpeechConfirmVisible(bool visible)
    {
        if (speechConfirmButton == null)
        {
            return;
        }

        speechConfirmButton.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        var confirmRect = speechConfirmButton.GetComponent<RectTransform>();
        var bubbleRect = speechBubbleObject != null
            ? speechBubbleObject.GetComponent<RectTransform>()
            : null;
        if (confirmRect == null || bubbleRect == null)
        {
            return;
        }

        float offset = (bubbleRect.rect.height * 0.5f) + 50f;
        confirmRect.anchoredPosition = new Vector2(0f, -offset);
    }

private void ReturnToMainScene()
    {
        if (!Application.CanStreamedLevelBeLoaded(MainSceneName))
        {
            Debug.LogError(
                $"[MiniGame] '{MainSceneName}' 씬을 불러올 수 없습니다. Build Settings Scene List를 확인하세요."
            );
            return;
        }

        StopAllToolSfx();
        DustinyDemoFlow.MarkResumeIdleAfterMiniGame();
        SceneManager.LoadScene(MainSceneName);
    }

private void UpdatePostClearToolVisuals()
    {
        if (dollController != null)
        {
            dollController.SetBubbleProgress(0f, true);
        }

        bool touchingTool =
            (currentTool == ToolType.Sponge && isTouchingSponge) ||
            (currentTool == ToolType.Shower && isTouchingShower);

        if (touchingTool && !wasPostClearToolTouching)
        {
            SetSpeechText("게임이 끝나고 보상 지급이 완료됐어. 메인 씬으로 돌아가자!");
            DustinySfx.PlayNo();
        }

        wasPostClearToolTouching = touchingTool;
    }

    private void PinSpeechCanvasInWorld()
    {
        if (speechCanvasPinned || speechBubbleObject == null)
        {
            return;
        }

        Transform canvasRoot = speechBubbleObject.transform;
        Canvas canvas = speechBubbleObject.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvasRoot = canvas.transform;
            if (canvasRoot.parent != null && canvasRoot.parent.name == "UIRoot")
            {
                canvasRoot = canvasRoot.parent;
            }
        }

        if (canvasRoot.parent != null)
        {
            canvasRoot.SetParent(null, true);
        }

        speechCanvasPinned = true;
    }

private void ConfigureShowerWaterVisibility()
    {
        if (showerObject != null)
        {
            Transform nozzle = FindChildTransformByName(showerObject.transform, "ShoweringPoint");
            if (nozzle != null)
            {
                if (showerParticle == null)
                {
                    showerParticle = nozzle.GetComponentInChildren<ParticleSystem>(true);
                }

                if (showerParticle != null && showerParticle.transform.parent != nozzle)
                {
                    showerParticle.transform.SetParent(nozzle, false);
                }
            }
        }

        if (showerParticle == null)
        {
            return;
        }

        showerParticle.transform.localPosition = Vector3.zero;
        showerParticle.transform.localRotation = Quaternion.identity;

        ParticleSystem[] systems = showerParticle.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (system == null)
            {
                continue;
            }

            system.transform.localPosition = Vector3.zero;

            ParticleSystem.MainModule main = system.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(main.maxParticles, 400);
            main.playOnAwake = false;
            main.gravityModifier = 0.35f;

            ParticleSystem.ShapeModule shape = system.shape;
            if (shape.enabled && shape.radius > 0.03f)
            {
                shape.radius = 0.02f;
            }

            ParticleSystemRenderer particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                particleRenderer.minParticleSize = 0.008f;
                particleRenderer.maxParticleSize = 5f;
            }
        }
    }




private static Transform FindChildTransformByName(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrEmpty(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == exactName)
            {
                return children[i];
            }
        }

        return null;
    }


    private void ResolveRightHand()
    {
        if (rightHand != null)
        {
            return;
        }

        OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < hands.Length; i++)
        {
            OVRHand hand = hands[i];
            if (hand == null)
            {
                continue;
            }

            string name = hand.gameObject.name.ToLowerInvariant();
            string parentName = hand.transform.parent != null
                ? hand.transform.parent.name.ToLowerInvariant()
                : string.Empty;
            if ((name + " " + parentName).Contains("right"))
            {
                rightHand = hand;
                return;
            }
        }
    }

    private void UpdateReturnPromptPinch()
    {
        if (!isReturningHomePrompt)
        {
            wasRightIndexPinching = false;
            wasRightMiddlePinching = false;
            return;
        }

        ResolveRightHand();
        bool tracked = rightHand != null && rightHand.IsTracked;
        bool isIndexPinching = tracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool isMiddlePinching = tracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
        bool indexDown = isIndexPinching && !wasRightIndexPinching;
        bool middleDown = isMiddlePinching && !wasRightMiddlePinching;
        wasRightIndexPinching = isIndexPinching;
        wasRightMiddlePinching = isMiddlePinching;

        if (indexDown)
        {
            ReturnToMainScene();
            return;
        }

        if (middleDown)
        {
            CancelReturnHomePrompt();
        }
    }

    private void PlayVoice(AudioClip clip)
    {
        if (clip != null && voiceAudioSource != null)
        {
            voiceAudioSource.PlayOneShot(clip);
        }
    }


private void UpdateBackButtonArming()
    {
        if (backButton == null || currentPhase == GamePhase.Intro)
        {
            return;
        }

        if (Time.unscaledTime < ignoreBackUntil)
        {
            backButton.interactable = false;
            return;
        }

        ResolveRightHand();
        bool pinching = rightHand != null &&
                        rightHand.IsTracked &&
                        (rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
                         rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle));
        if (!pinching)
        {
            backButton.interactable = true;
        }
    }
}
