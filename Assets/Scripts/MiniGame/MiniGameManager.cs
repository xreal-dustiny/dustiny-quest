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
        "아직 게임이 끝나지 않았어.\n중간에 나가면 다시 이어할 수 없어.\n그래도 메인 씬으로 돌아갈래?";
    private const string BackPromptGameClear = "메인 씬으로 돌아갈래?";
    private const string GameClearSpeech =
        "30코인이 지급됐어.\n메인 씬으로 돌아가자!";
    private static readonly Vector2 SpeechBubblePosition = new Vector2(0f, -40f);

    [Header("Manager / Controller References")]
    public DollController dollController;

    [Header("Item GameObjects")]
    public GameObject spongeObject;
    public GameObject showerObject;
    public ParticleSystem showerParticle;

    [Header("Hand Tracking (Right Hand Tool Holding)")]
    public Transform rightHandAnchor;
    [Tooltip("OVR RightHandAnchor: palm ≈ -Y. 손바닥 중앙 (너무 아래/손등 쪽 금지).")]
    public Vector3 palmLocalPosition = new Vector3(0f, 0f, 0.07f);
    public Vector3 spongeHoldOffset = new Vector3(0f, 0f, 0f);
    public Vector3 spongeHoldEuler = new Vector3(0f, -90f, 0f);

    [Tooltip("샤워기만 손바닥 쪽으로 살짝 당김 (−Y = palm).")]
    public Vector3 showerHoldOffset = new Vector3(0f, -0.02f, 0.01f);
    [Tooltip("샤워기 손 그립 회전 (핸들 아래 / 노즐 전방).")]
    public Vector3 showerHoldEuler = new Vector3(80f, 0f, 0f);

    private Transform spongeGrabPoint;
    private Transform showerGrabPoint;

    [Header("Bathtub Grab Volume")]
    [Tooltip("오른손이 이 트리거 안에 있을 때만 선택한 도구를 집습니다.")]
    public Collider bathtubVolume;
    [Tooltip("기존/자동 볼륨 높이(Y)에 더하는 여유(미터). 위쪽으로만 확장됩니다.")]
    public float bathtubVolumeExtraHeight = 0.12f;
    private bool bathtubVolumeHeightExpanded;

    [Header("Navigation Bar Buttons")]
    public Button spongeButton;
    public Button showerButton;
    public Button backButton;

    [Header("Dialogue UI")]
    public TMP_Text speechText;
    [Tooltip("말풍선 대사 전용 폰트. 비워두면 KyoboHandwriting TMP를 자동 로드합니다.")]
    public TMP_FontAsset speechBubbleFont;
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
    [SerializeField] private int cleanlinessLevel = 0;
    [SerializeField] private float turnTargetDuration = 3f;
    [SerializeField] private float turnProgressDuration = 0f;

    private bool isTouchingSponge = false;
    private bool isTouchingShower = false;
    private bool isReturningHomePrompt = false;
    private string previousSpeechText = string.Empty;
    private bool isHoldingTool;
    private bool navHiddenForScrub;
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
    private Coroutine transitionCoroutine;

    [Header("MiniGame Intro Overlay")]
    public GameObject introRoot;
    public Transform introDurrySlot;
    public Transform introDollHoldSlot;
    public Animator introDurryAnimator;
    public string introExpressionState = "Joyful";
    [Range(0f, 1f)] public float introOverlayOpacity = 0.85f;
    public float introOverlayPlaneDistance = 3.5f;
    public int introOverlaySortingOrder = -1000;
    public float introDurryDistance = 1.15f;
    public float introDurryHeightOffset = -0.2f;
    public float introDurryVisualScale = 10f;
    public Vector3 introDollLocalPosition = new Vector3(0.035f, -0.005f, 0.03f);
    public Vector3 introDollLocalEuler = new Vector3(0f, -25f, 10f);
    public float introDollUniformScale = 0.055f;
    public bool requirePinchToStart = true;
    public float introOverlayFadeOutDuration = 0.45f;

    [Header("Dialogue Skip")]
    [Tooltip("대사/음성 재생 중 오른손 검지 핀치로 현재 대사를 건너뜁니다.")]
    public bool allowPinchToSkipSpeech = true;

    [Header("MiniGame Nav Pose")]
    [Tooltip("미니게임에서만 WaistNavCanvas에 적용. Y를 올려 욕조와 겹치지 않게 합니다. (메인 씬 높이는 변경하지 않음)")]
    public Vector3 miniGameNavLocalPosition = new Vector3(0f, -0.18f, 0.3f);

    private Canvas introOverlayCanvas;
    private Image introOverlayImage;
    private Transform dollIntroCachedParent;
    private Vector3 dollIntroCachedLocalPos;
    private Quaternion dollIntroCachedLocalRot;
    private Vector3 dollIntroCachedLocalScale;
    private bool dollIntroPoseCached;
    private bool introPresentationActive;
    private DustinyWaistNavFollow introHiddenNav;


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
        ConfigureMiniGameNavDrawOrder();
        PinSpeechCanvasInWorld();
        ConfigureShowerWaterVisibility();
        CacheToolRestPoses();
        StartCoroutine(PlayIntroThenStartGame());
    }




private IEnumerator PlayIntroThenStartGame()
    {
        currentPhase = GamePhase.Intro;
        introPresentationActive = false;
        ignoreBackUntil = Time.unscaledTime + 4.5f;
        EquipTool(ToolType.None);

        // Skip Durry+doll overlay intro — start with the doll already in the bathtub.
        EnsureIntroReferences();
        if (introRoot != null)
        {
            introRoot.SetActive(false);
        }

        if (introOverlayCanvas != null)
        {
            introOverlayCanvas.gameObject.SetActive(false);
        }

        SetToolsVisible(true);
        SetNavVisible(true);

        if (backButton != null)
        {
            backButton.interactable = false;
        }

        PlaceDollInView();

        BindButton(backButton, OnBackButtonClicked);
        if (backButton != null)
        {
            backButton.interactable = false;
        }

        ignoreBackUntil = Time.unscaledTime + 1.25f;
        EquipTool(ToolType.Sponge);
        StartMiniGame();
        yield break;
    }

private void EnsureIntroReferences()
    {
        if (introRoot == null)
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.name != "MiniGameIntroRoot") continue;
                if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
                introRoot = t.gameObject;
                break;
            }
        }

        if (introRoot == null)
        {
            return;
        }

        if (introDurrySlot == null)
        {
            Transform found = introRoot.transform.Find("IntroDurrySlot");
            if (found != null) introDurrySlot = found;
        }

        if (introDollHoldSlot == null)
        {
            Transform found = introRoot.transform.Find("IntroDollHoldSlot");
            if (found == null && introDurrySlot != null)
            {
                found = introDurrySlot.Find("IntroDollHoldSlot");
            }
            if (found == null)
            {
                found = introRoot.transform.Find("IntroDurrySlot/IntroDollHoldSlot");
            }
            if (found != null) introDollHoldSlot = found;
        }

        if (introDurryAnimator == null && introDurrySlot != null)
        {
            Transform durry = introDurrySlot.Find("IntroDurry");
            if (durry != null)
            {
                introDurryAnimator = durry.GetComponent<Animator>();
                if (introDurryAnimator == null)
                {
                    introDurryAnimator = durry.GetComponentInChildren<Animator>(true);
                }
            }
        }
    }

    private Camera ResolveCenterEyeCamera()
    {
        Transform eye = null;
        GameObject eyeGo = GameObject.Find("CenterEyeAnchor");
        if (eyeGo != null) eye = eyeGo.transform;
        if (eye != null)
        {
            Camera cam = eye.GetComponent<Camera>();
            if (cam != null) return cam;
        }

        return Camera.main;
    }

    private void SetupIntroOverlay()
    {
        Camera eyeCam = ResolveCenterEyeCamera();
        if (eyeCam == null)
        {
            return;
        }

        if (introOverlayCanvas == null)
        {
            GameObject existing = GameObject.Find("MiniGameIntroOverlayCanvas");
            if (existing != null)
            {
                introOverlayCanvas = existing.GetComponent<Canvas>();
            }
        }

        if (introOverlayCanvas == null)
        {
            GameObject canvasObject = new GameObject(
                "MiniGameIntroOverlayCanvas",
                typeof(RectTransform),
                typeof(Canvas)
            );
            introOverlayCanvas = canvasObject.GetComponent<Canvas>();
        }

        introOverlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        introOverlayCanvas.worldCamera = eyeCam;
        introOverlayCanvas.planeDistance = Mathf.Clamp(
            introOverlayPlaneDistance,
            eyeCam.nearClipPlane + 0.05f,
            Mathf.Max(eyeCam.nearClipPlane + 0.1f, eyeCam.farClipPlane - 0.1f)
        );
        introOverlayCanvas.overrideSorting = true;
        introOverlayCanvas.sortingOrder = introOverlaySortingOrder;

        if (introOverlayImage == null)
        {
            Transform existingImage = introOverlayCanvas.transform.Find("IntroPassthroughOverlay");
            if (existingImage != null)
            {
                introOverlayImage = existingImage.GetComponent<Image>();
            }
        }

        if (introOverlayImage == null)
        {
            GameObject overlayObject = new GameObject(
                "IntroPassthroughOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.SetParent(introOverlayCanvas.transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.anchoredPosition = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            introOverlayImage = overlayObject.GetComponent<Image>();
        }

        introOverlayImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(introOverlayOpacity));
        introOverlayImage.raycastTarget = false;
        introOverlayCanvas.gameObject.SetActive(true);
        introOverlayImage.gameObject.SetActive(true);
    }

private void BeginIntroPresentation()
    {
        EnsureIntroReferences();

        Camera eyeCam = ResolveCenterEyeCamera();
        Transform eye = eyeCam != null ? eyeCam.transform : null;
        if (eye == null || introRoot == null)
        {
            PlaceDollInView();
            return;
        }

        introRoot.SetActive(true);

        // Place Durry in front of the player, facing the camera.
        Vector3 placePos = eye.position + eye.forward * introDurryDistance + Vector3.up * introDurryHeightOffset;
        Vector3 faceDir = eye.position - placePos;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude < 0.0001f)
        {
            faceDir = -eye.forward;
            faceDir.y = 0f;
        }
        faceDir.Normalize();

        introRoot.transform.position = placePos;
        introRoot.transform.rotation = Quaternion.LookRotation(faceDir, Vector3.up);

        if (introDurrySlot != null)
        {
            introDurrySlot.localPosition = Vector3.zero;
            introDurrySlot.localRotation = Quaternion.identity;
            introDurrySlot.localScale = Vector3.one * Mathf.Max(0.01f, introDurryVisualScale);
        }

        if (introDollHoldSlot != null)
        {
            introDollHoldSlot.localPosition = introDollLocalPosition;
            introDollHoldSlot.localRotation = Quaternion.Euler(introDollLocalEuler);
            introDollHoldSlot.localScale = Vector3.one;
        }

        PlayIntroExpression(introExpressionState);
        AttachDollToIntroHold();
    }

    private void PlayIntroExpression(string expressionName)
    {
        if (introDurryAnimator == null || string.IsNullOrWhiteSpace(expressionName))
        {
            return;
        }

        if (introDurryAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        int shortHash = Animator.StringToHash(expressionName);
        int fullHash = Animator.StringToHash("Base Layer." + expressionName);
        if (introDurryAnimator.HasState(0, fullHash))
        {
            introDurryAnimator.CrossFadeInFixedTime(fullHash, 0.05f, 0);
            return;
        }

        if (introDurryAnimator.HasState(0, shortHash))
        {
            introDurryAnimator.CrossFadeInFixedTime(shortHash, 0.05f, 0);
        }
    }

    private void AttachDollToIntroHold()
    {
        if (dollController == null)
        {
            dollController = FindFirstObjectByType<DollController>(FindObjectsInactive.Include);
        }

        if (dollController == null || introDollHoldSlot == null)
        {
            PlaceDollInView();
            return;
        }

        Transform dollTransform = dollController.transform;
        if (!dollIntroPoseCached)
        {
            dollIntroCachedParent = dollTransform.parent;
            dollIntroCachedLocalPos = dollTransform.localPosition;
            dollIntroCachedLocalRot = dollTransform.localRotation;
            dollIntroCachedLocalScale = dollTransform.localScale;
            dollIntroPoseCached = true;
        }

        dollController.gameObject.SetActive(true);
        dollTransform.SetParent(introDollHoldSlot, false);
        dollTransform.localPosition = Vector3.zero;
        dollTransform.localRotation = Quaternion.identity;
        dollTransform.localScale = Vector3.one * Mathf.Max(0.01f, introDollUniformScale);

        // Hide foam during intro hug pose.
        dollController.SetBubbleProgress(0f, true);
        dollController.SetCleanlinessVisual(0);

        Renderer[] renderers = dollController.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            string lowerName = renderer.name.ToLowerInvariant();
            if (lowerName.Contains("bubble") || lowerName.Contains("particle"))
            {
                renderer.gameObject.SetActive(false);
                continue;
            }

            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
        }
    }

    private void RestoreDollFromIntroHold()
    {
        if (dollController == null || !dollIntroPoseCached)
        {
            PlaceDollInView();
            return;
        }

        Transform dollTransform = dollController.transform;
        dollTransform.SetParent(dollIntroCachedParent, false);
        dollTransform.localPosition = dollIntroCachedLocalPos;
        dollTransform.localRotation = dollIntroCachedLocalRot;
        dollTransform.localScale = dollIntroCachedLocalScale;
        dollIntroPoseCached = false;

        PlaceDollInView();
    }

    private IEnumerator WaitForIndexPinchDown()
    {
        while (true)
        {
            ResolveRightHand();
            bool tracked = rightHand != null && rightHand.IsTracked;
            bool isIndexPinching = tracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            bool indexDown = isIndexPinching && !wasRightIndexPinching;
            wasRightIndexPinching = isIndexPinching;

            if (indexDown)
            {
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator EndIntroPresentation()
    {
        RestoreDollFromIntroHold();

        if (introRoot != null)
        {
            introRoot.SetActive(false);
        }

        float duration = Mathf.Max(0f, introOverlayFadeOutDuration);
        float startAlpha = introOverlayImage != null ? introOverlayImage.color.a : 0f;
        if (duration > 0f && introOverlayImage != null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Color color = introOverlayImage.color;
                color.a = Mathf.Lerp(startAlpha, 0f, t);
                introOverlayImage.color = color;
                yield return null;
            }
        }

        if (introOverlayImage != null)
        {
            Color color = introOverlayImage.color;
            color.a = 0f;
            introOverlayImage.color = color;
            introOverlayImage.gameObject.SetActive(false);
        }

        if (introOverlayCanvas != null)
        {
            introOverlayCanvas.gameObject.SetActive(false);
        }
    }

    private void SetToolsVisible(bool visible)
    {
        if (spongeObject != null) spongeObject.SetActive(visible);
        if (showerObject != null) showerObject.SetActive(visible);
    }

private void SetNavVisible(bool visible)
    {
        if (introHiddenNav == null)
        {
            introHiddenNav = FindFirstObjectByType<DustinyWaistNavFollow>(FindObjectsInactive.Include);
        }

        if (introHiddenNav == null)
        {
            return;
        }

        // Never disable WaistFollowRoot — that kills follow + visibility updates.
        introHiddenNav.gameObject.SetActive(true);

        if (visible)
        {
            // 메인 씬과 동일: 고개 숙임으로만 네비를 연다.
            introHiddenNav.enableLookDownToOpen = true;
            introHiddenNav.ClearAlwaysInFrontDrawState();
            if (introHiddenNav.waistNavRoot != null)
            {
                introHiddenNav.waistNavRoot.gameObject.SetActive(true);
            }

            introHiddenNav.ClearVisibilityOverrideAndRefresh();
        }
        else
        {
            introHiddenNav.SetVisibilityOverride(false);
        }
    }



private void PlaceDollInView()
    {
        if (dollController == null)
        {
            dollController = FindFirstObjectByType<DollController>(FindObjectsInactive.Include);
        }

        if (dollController == null)
        {
            return;
        }

        dollController.gameObject.SetActive(true);
        Renderer[] renderers = dollController.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            string lowerName = renderer.name.ToLowerInvariant();
            if (lowerName.Contains("bubble") || lowerName.Contains("particle"))
            {
                continue;
            }

            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
        }

        dollController.EnsureBodyCollision();
        dollController.SetCleanlinessVisual(0);
        dollController.FacePlayerInitially();
    }






private void Update()
    {
        ProcessGameTurn();
        UpdateToolLoopSfx();
        UpdateRightHandPinchInput();
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

        ApplySpeechBubbleFont();

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

private void ConfigureMiniGameNavDrawOrder()
    {
        // 메인 씬과 같은 look-down 네비 규칙으로 통일합니다.
        DustinyWaistNavFollow nav = FindFirstObjectByType<DustinyWaistNavFollow>(FindObjectsInactive.Include);
        if (nav == null)
        {
            return;
        }

        introHiddenNav = nav;
        nav.gameObject.SetActive(true);

        nav.enableLookDownToOpen = true;
        nav.drawTrackedHandsOverNav = true;
        nav.pullInFrontOfOccluders = true;
        nav.ClearAlwaysInFrontDrawState();
        // MainMRScene과 동일하게 손/가림 대비 always-in-front는 씬 설정에 맡기되,
        // 미니게임에서도 look-down 게이트는 반드시 켭니다.
        nav.alwaysDrawInFront = true;

        if (nav.waistNavRoot != null)
        {
            nav.waistNavRoot.gameObject.SetActive(true);

            Canvas canvas = nav.waistNavRoot.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;

                Camera eyeCam = null;
                GameObject eyeGo = GameObject.Find("CenterEyeAnchor");
                if (eyeGo != null)
                {
                    eyeCam = eyeGo.GetComponent<Camera>();
                }

                if (eyeCam != null)
                {
                    canvas.worldCamera = eyeCam;
                }
            }
        }

        // Raise nav above the bathtub (mini-game only; MainMRScene keeps its own height).
        nav.defaultLocalPosition = miniGameNavLocalPosition;

        nav.ClearVisibilityOverrideAndRefresh();
        nav.ResetWaistNavPose();
        nav.ApplyConfiguredScale();
        nav.RefreshVisibilityImmediately();
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

        HideDescription();
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
        cleanlinessLevel = 0;
        isReturningHomePrompt = false;
        previousSpeechText = string.Empty;

        if (dollController != null)
        {
            dollController.gameObject.SetActive(true);
            dollController.EnsureBodyCollision();
            dollController.SetCleanlinessVisual(0);
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
            SetSpeechText("내 인형 좀 빨아줘. 아래 스펀지 버튼을\n누르고, 인형을 살살 문질러줘.");
        }
        else
        {
            SetSpeechText("좋아, 스펀지 버튼을 누르고\n한 번 더 문질러줄래?");
        }

        PlayVoice(spongeVoice);
    }

    private void StartShowerTurn()
    {
        currentPhase = GamePhase.ShowerTurn;
        turnTargetDuration = Random.Range(2.0f, 4.0f);
        turnProgressDuration = 0f;

        SetSpeechText("거품이 많이 났어!\n이번엔 샤워기 버튼을 눌러서 헹궈줘.");
        PlayVoice(showerVoice);
    }

private void ProcessGameTurn()
    {
        if (currentPhase == GamePhase.Intro || introPresentationActive)
        {
            return;
        }

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
        cleanlinessLevel = Mathf.Clamp(currentCycle, 0, 3);

        if (dollController != null)
        {
            dollController.SetCleanlinessVisual(cleanlinessLevel);
        }

        if (currentCycle >= 3)
        {
            currentPhase = GamePhase.GameClear;
            wasPostClearToolTouching = false;
            if (dollController != null)
            {
                dollController.SetCleanlinessVisual(3);
                dollController.SetBubbleProgress(0f, true);
            }

            SetSpeechText(GameClearSpeech);
            PlayVoice(clearVoice);
            DustinySfx.PlayCoin();

            if (CreditManager.Instance != null)
            {
                CreditManager.Instance.GrantCredit(30, playRewardSfx: false);
            }

            return;
        }

        currentSet++;
        SetSpeechText("와, 인형이 조금 더 깨끗해진 것 같아.\n조금만 더 해보자!");
        PlayVoice(levelUpVoice);
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        transitionCoroutine = StartCoroutine(TransitionToNextSpongeTurn());
    }





    private IEnumerator TransitionToNextSpongeTurn()
    {
        currentPhase = GamePhase.SetComplete;
        yield return new WaitForSeconds(1.5f);
        transitionCoroutine = null;
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
            if (isHoldingTool)
            {
                DropHeldTool();
            }

            SetScrubNavHidden(false);
            return;
        }

        if (IsHandInsideBathtub())
        {
            HoldSelectedTool();
        }
        else if (isHoldingTool)
        {
            DropHeldTool();
        }

        // 스펀지/샤워로 욕조 안에서 문지르는 동안에만 허리 네비 숨김 (look-down은 유지).
        bool scrubbing = isHoldingTool
            && (currentTool == ToolType.Sponge || currentTool == ToolType.Shower);
        SetScrubNavHidden(scrubbing);
    }

    private void SetScrubNavHidden(bool hide)
    {
        if (navHiddenForScrub == hide)
        {
            return;
        }

        navHiddenForScrub = hide;
        SetNavVisible(!hide);
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

        Vector3 palmPos = rightHandAnchor.TransformPoint(palmLocalPos);
        Quaternion palmRot = rightHandAnchor.rotation * Quaternion.Euler(holdEuler);

        tool.rotation = palmRot;

        if (grabPoint != null)
        {
            tool.position = palmPos - (grabPoint.position - tool.position);
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
            spongeGrabPoint = ResolveGrabPoint(spongeObject.transform);
            EnsureSolidToolCollider(spongeObject);
        }

        if (showerObject != null)
        {
            showerGrabPoint = ResolveGrabPoint(showerObject.transform);
            EnsureSolidToolCollider(showerObject);
        }
    }

    private static void EnsureSolidToolCollider(GameObject tool)
    {
        if (tool == null)
        {
            return;
        }

        Collider[] colliders = tool.GetComponentsInChildren<Collider>(true);
        bool hasSolid = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled && !colliders[i].isTrigger)
            {
                hasSolid = true;
                break;
            }
        }

        if (hasSolid)
        {
            return;
        }

        Renderer renderer = tool.GetComponentInChildren<Renderer>();
        BoxCollider box = tool.AddComponent<BoxCollider>();
        box.isTrigger = false;
        if (renderer != null)
        {
            Bounds local = renderer.localBounds;
            box.center = local.center;
            box.size = local.size;
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

private static Transform ResolveGrabPoint(Transform tool)
    {
        if (tool == null)
        {
            return null;
        }

        Transform grab = tool.Find("Grabpoint");
        if (grab != null)
        {
            return grab;
        }

        Transform[] children = tool.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == "Grabpoint")
            {
                return children[i];
            }
        }

        var grabObject = new GameObject("Grabpoint");
        grab = grabObject.transform;
        grab.SetParent(tool, false);
        grab.localPosition = Vector3.zero;
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
            if (spongeGrabPoint == null)
            {
                spongeGrabPoint = ResolveGrabPoint(spongeObject.transform);
            }

            // Always glue grab point to palm. Do not run doll push while held —
            // Detectionzone / soft bounds previously threw the sponge far from the hand.
            AlignGrabToPalm(
                spongeObject.transform,
                spongeGrabPoint,
                palmLocalPosition + spongeHoldOffset,
                spongeHoldEuler);
        }
        else if (currentTool == ToolType.Shower && showerObject != null && showerObject.activeSelf)
        {
            isHoldingTool = true;
            if (showerGrabPoint == null)
            {
                showerGrabPoint = ResolveGrabPoint(showerObject.transform);
            }

            AlignGrabToPalm(
                showerObject.transform,
                showerGrabPoint,
                palmLocalPosition + showerHoldOffset,
                showerHoldEuler);
        }

        UpdateShowerParticle();
    }


private void PushToolOutOfDoll(GameObject tool)
    {
        if (tool == null || dollController == null)
        {
            return;
        }

        // Soft bounds separation uses the tool root, but ignore oversized detection volumes.
        Bounds dollBounds = default;
        bool hasBounds = false;
        Renderer[] renderers = dollController.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            string lowerName = renderer.name.ToLowerInvariant();
            if (lowerName.Contains("bubble") || lowerName.Contains("particle"))
            {
                continue;
            }

            if (!hasBounds)
            {
                dollBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                dollBounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            Vector3 center = dollBounds.center;
            Vector3 toTool = tool.transform.position - center;
            toTool.y *= 0.55f;
            float minDistance = Mathf.Max(dollBounds.extents.x, dollBounds.extents.z) * 0.92f;
            if (toTool.sqrMagnitude < 0.0001f)
            {
                toTool = Vector3.forward;
            }

            if (toTool.magnitude < minDistance)
            {
                Vector3 pushed = center + toTool.normalized * minDistance;
                pushed.y = tool.transform.position.y;
                tool.transform.position = pushed;
            }
        }

        Collider[] toolColliders = tool.GetComponentsInChildren<Collider>();
        Collider[] dollColliders = dollController.GetComponentsInChildren<Collider>();
        for (int i = 0; i < toolColliders.Length; i++)
        {
            Collider toolCollider = toolColliders[i];
            if (toolCollider == null ||
                !toolCollider.enabled ||
                toolCollider.isTrigger ||
                IsNonSolidToolCollider(toolCollider))
            {
                continue;
            }

            for (int j = 0; j < dollColliders.Length; j++)
            {
                Collider dollCollider = dollColliders[j];
                if (dollCollider == null || !dollCollider.enabled || dollCollider.isTrigger)
                {
                    continue;
                }

                if (Physics.ComputePenetration(
                        toolCollider,
                        toolCollider.transform.position,
                        toolCollider.transform.rotation,
                        dollCollider,
                        dollCollider.transform.position,
                        dollCollider.transform.rotation,
                        out Vector3 direction,
                        out float distance) &&
                    distance > 0.0001f)
                {
                    // Cap so a bad collider never flings the held tool across the room.
                    float capped = Mathf.Min(distance + 0.012f, 0.08f);
                    tool.transform.position += direction * capped;
                }
            }
        }
    }

    private static bool IsNonSolidToolCollider(Collider collider)
    {
        if (collider == null)
        {
            return true;
        }

        string lowerName = collider.name.ToLowerInvariant();
        return lowerName.Contains("detection") ||
               lowerName.Contains("grabpoint") ||
               lowerName.Contains("trigger") ||
               lowerName.Contains("zone");
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
        if (bathtubVolume == null || currentTool == ToolType.None || rightHandAnchor == null)
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
        if (bathtubVolume == null)
        {
            GameObject existing = GameObject.Find("BathtubVolume");
            if (existing != null)
            {
                bathtubVolume = existing.GetComponent<Collider>();
            }
        }

        if (bathtubVolume == null)
        {
            GameObject bath = GameObject.Find("Bath_mesh");
            if (bath == null)
            {
                Debug.LogWarning("[MiniGame] Bath_mesh를 찾지 못해 욕조 잡기 영역을 만들지 못했습니다.");
                return;
            }

            Renderer bathRenderer = bath.GetComponent<Renderer>();
            GameObject volumeObject = new GameObject("BathtubVolume");
            volumeObject.transform.SetParent(bath.transform, false);
            volumeObject.transform.localRotation = Quaternion.identity;
            volumeObject.transform.localScale = Vector3.one;

            BoxCollider box = volumeObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (bathRenderer != null)
            {
                Bounds localBounds = bathRenderer.localBounds;
                // Extra Y padding + raise so rim / slightly-above-hand still counts as inside.
                volumeObject.transform.localPosition = localBounds.center + Vector3.up * 0.08f;
                box.center = Vector3.zero;
                box.size = Vector3.Scale(localBounds.size, new Vector3(0.72f, 0.85f, 0.72f));
            }
            else
            {
                volumeObject.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                box.size = new Vector3(0.78f, 0.58f, 0.38f);
            }

            bathtubVolume = box;

            if (bath.GetComponent<MeshCollider>() == null)
            {
                MeshCollider meshCollider = bath.AddComponent<MeshCollider>();
                meshCollider.convex = false;
            }
        }

        ApplyBathtubVolumeExtraHeight();
    }

    private void ApplyBathtubVolumeExtraHeight()
    {
        if (bathtubVolumeHeightExpanded || bathtubVolumeExtraHeight <= 0f || bathtubVolume == null)
        {
            return;
        }

        BoxCollider box = bathtubVolume as BoxCollider;
        if (box == null)
        {
            return;
        }

        float extra = bathtubVolumeExtraHeight;
        Vector3 size = box.size;
        Vector3 center = box.center;
        size.y += extra;
        // Push the added height upward (toward hand approach), not into the floor.
        center.y += extra * 0.5f;
        box.size = size;
        box.center = center;
        bathtubVolumeHeightExpanded = true;
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
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                modules[i].enabled = true;
            }
        }
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
        // SpeechBubble과 Description은 절대 동시에 표시하지 않습니다.
        HideDescription();

        if (speechBubbleObject != null && !speechBubbleObject.activeSelf)
        {
            speechBubbleObject.SetActive(true);
        }

        PlaceSpeechBubbleAtDescriptionPosition();
        ApplySpeechBubbleFont();

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

    private void ApplySpeechBubbleFont()
    {
        if (speechBubbleFont == null)
        {
            speechBubbleFont = Resources.Load<TMP_FontAsset>(
                "Fonts & Materials/Fonts/KyoboHandwriting2025lyb SDF");
#if UNITY_EDITOR
            if (speechBubbleFont == null)
            {
                speechBubbleFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/Fonts/KyoboHandwriting2025lyb SDF.asset");
            }
#endif
        }

        if (speechText == null || speechBubbleFont == null)
        {
            return;
        }

        if (speechText.font != speechBubbleFont)
        {
            speechText.font = speechBubbleFont;
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
            SetSpeechText("게임이 끝나고 보상 지급이 완료됐어.\n메인 씬으로 돌아가자!");
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
        // Shower 파티클 Transform·Simulation Space·Shape 등은 DollScene 에디터 설정을 그대로 사용합니다.
        // (이전에는 localRotation/simulationSpace 등을 런타임에 덮어써 물 방향이 에디터와 달랐습니다.)
        if (showerParticle != null)
        {
            return;
        }

        if (showerObject == null)
        {
            return;
        }

        Transform nozzle = FindChildTransformByName(showerObject.transform, "ShoweringPoint");
        if (nozzle != null)
        {
            showerParticle = nozzle.GetComponentInChildren<ParticleSystem>(true);
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

    private void UpdateRightHandPinchInput()
    {
        ResolveRightHand();
        bool tracked = rightHand != null && rightHand.IsTracked;
        bool isIndexPinching = tracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool isMiddlePinching = tracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
        bool indexDown = isIndexPinching && !wasRightIndexPinching;
        bool middleDown = isMiddlePinching && !wasRightMiddlePinching;
        wasRightIndexPinching = isIndexPinching;
        wasRightMiddlePinching = isMiddlePinching;

        if (isReturningHomePrompt)
        {
            if (indexDown)
            {
                ReturnToMainScene();
            }
            else if (middleDown)
            {
                CancelReturnHomePrompt();
            }

            return;
        }

        if (allowPinchToSkipSpeech &&
            indexDown &&
            !introPresentationActive &&
            currentPhase != GamePhase.Intro &&
            IsSpeechSkippable())
        {
            SkipCurrentSpeech();
        }
    }

    private bool IsSpeechSkippable()
    {
        if (speechBubbleObject == null || !speechBubbleObject.activeSelf)
        {
            return false;
        }

        if (currentPhase == GamePhase.SetComplete)
        {
            return true;
        }

        return voiceAudioSource != null && voiceAudioSource.isPlaying;
    }

    private void SkipCurrentSpeech()
    {
        StopVoice();

        if (currentPhase != GamePhase.SetComplete)
        {
            return;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        StartSpongeTurn();
    }

    private void StopVoice()
    {
        if (voiceAudioSource != null)
        {
            voiceAudioSource.Stop();
        }
    }

    private void PlayVoice(AudioClip clip)
    {
        if (voiceAudioSource == null)
        {
            return;
        }

        voiceAudioSource.Stop();
        if (clip != null)
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
