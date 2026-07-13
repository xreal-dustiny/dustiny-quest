using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scene/XR presentation controller for Dustiny.
///
/// Data ownership after the merge:
/// - CleanlinessManager: cleanliness value and six-hour decay
/// - CreditManager: credit balance
/// - QuestGenerator: YOLO JSON conversion
/// - QuestProgressManager: quest completion, daily mission, streak rewards
/// - ShopInventoryManager: purchased/equipped items
/// - DustinyDemoFlow: Durry placement, dialogue, gestures, navigation, page visibility
///
/// Page background rule:
/// - NotePage owns its own Note background.
/// - MenuPage owns its own Menu background.
/// - ShopPage and MyPage use the existing shared ViewpointBackground.
/// - The shared background is hidden while Note or Menu is open.
/// </summary>
public class DustinyDemoFlow : MonoBehaviour
{
    public enum DustinyPage
    {
        None,
        Note,
        Shop,
        MyPage,
        Menu
    }

    private enum OnboardingState
    {
        IntroSpeech,
        MissionDescription,
        MissionInProgress,
        NameQuestionSpeech,
        NamedDescription,
        FinalSpeech,
        Finished
    }

    [Header("XR Reference")]
    public Transform centerEyeAnchor;

    [Header("World Objects")]
    public GameObject durryObject;
    public Transform durryVisual;
    public GameObject scanZoneObject;
    public Canvas worldCanvas;

    [Header("View Locked UI")]
    public Transform uiRoot;
    public bool lockUIToUserView = true;
    public bool forceUIRootUnderCenterEye = false;
    public Vector3 uiRootLocalPosition = new Vector3(0f, -0.12f, 1.35f);
    public Vector3 uiRootLocalEuler = Vector3.zero;
    public float uiRootLocalScale = 1f;

    [Header("Dialogue / Description")]
    public bool startOnboardingFlowOnStart = true;
    public float dialogueAdvanceCooldown = 0.25f;

    public GameObject speechBubbleObject;
    public TMP_Text speechText;
    public GameObject descriptionObject;
    public TMP_Text descriptionText;

    [Header("Description Screen Dim")]
    [Tooltip("디스크립션이 표시될 때 화면을 어둡게 덮는 검정 오버레이입니다. 비워두면 자동 생성합니다.")]
    public GameObject descriptionDimOverlayObject;
    public Image descriptionDimOverlayImage;
    public bool autoCreateDescriptionDimOverlay = true;
    [Range(0f, 1f)] public float descriptionDimOpacity = 0.30f;
    [Tooltip("보통 false로 둡니다. true이면 검정 오버레이가 뒤쪽 UI 입력도 막습니다.")]
    public bool descriptionDimBlocksRaycasts = false;

    [Header("Name Input")]
    public TMP_InputField durryNameInputField;
    public GameObject durryNameInputObject;
    public string defaultDurryName = "더리";
    public string durryName = "더리";
    public bool showNameInputWithNameQuestion = false;

    [Header("Onboarding Script")]
    [Tooltip("Interaction SDK의 [확인] 버튼을 누를 때마다 다음 대사로 넘어갑니다.")]
    [TextArea(2, 5)]
    public string[] openingSpeechSteps =
    {
        "으앙... 엣취!\n여긴 어디야...?",
        "나는 누구지...?\n나...? 나는 분명...",
        "이 공간을 깨끗하게 지키는\n청결요정이었던 것 같아!",
        "으으... 몸이 너무 무거워.\n내 몸에 붙은 먼지 때문인가 봐...",
        "어? 이상하다... 뭔가를 얻으면\n힘이 날 것 같은 기분이 들어.",
        "생각났어! 보송력!",
        "보송력이 있으면 내 힘을\n조금씩 되찾을 수 있을 것 같아!",
        "잠깐! 저쪽에서 아주 약하게\n보송력의 기운이 느껴져!",
        "하지만 아직 정확히 어디에\n숨어 있는지는 모르겠어.",
        "네 도움이 필요해!"
    };

    [TextArea(2, 5)]
    public string firstMissionDescription =
        "보송력이 숨어 있을 것 같은 공간을 바라보고\n[확인]을 해줘!";

    [Tooltip("기존 이름 짓기 온보딩을 이어서 사용할 때만 켭니다.")]
    public bool continueToLegacyNameFlowAfterFirstMission = false;

    [Header("Legacy Name Flow (Optional)")]
    [TextArea(2, 5)] public string nameQuestionSpeech = "그래! 난 더리랜드에서 온 청소 요정이었어!\n나에게 이름을 지어줄래?";
    [TextArea(2, 4)] public string namedDescriptionFormat = "{0}(이)라는 이름을 지어줬다!";
    [TextArea(2, 4)] public string finalSpeechFormat = "앞으로 잘 부탁해!\n나는 {0}(이)야.";

    [Header("Unified Dialogue Placement")]
    [Tooltip("켜면 말풍선과 디스크립션이 같은 앵커, 피벗, 화면 위치를 사용합니다.")]
    public bool synchronizeDialoguePosition = true;

    [Tooltip("말풍선과 디스크립션이 함께 사용하는 위치입니다. Y가 커질수록 화면 위쪽으로 올라갑니다.")]
    public Vector2 sharedDialogueAnchoredPosition = new Vector2(0f, -420f);

    [Tooltip("두 UI가 서로 다른 부모 아래 있어도 월드 좌표까지 같게 맞춥니다.")]
    public bool forceSameDialogueWorldPosition = true;

    [Header("Speech Bubble Placement - Used Only When Sync Is Off")]
    public RectTransform speechBubbleRect;
    public bool keepSpeechBubbleBelowView = false;
    public Vector2 speechBubbleAnchoredPosition = new Vector2(0f, -420f);
    public bool forceSpeechBubbleCenterAnchor = true;

    [Header("Description Placement - Used Only When Sync Is Off")]
    public RectTransform descriptionRect;
    public bool keepDescriptionBelowView = true;
    public Vector2 descriptionAnchoredPosition = new Vector2(0f, -420f);
    public bool forceDescriptionCenterAnchor = true;

    [Header("Dialogue Safe Bounds")]
    public bool clampDialogueAboveWaistNav = true;
    public float sharedDialogueMinAnchoredY = -520f;
    public float speechBubbleMinAnchoredY = -520f;
    public float descriptionMinAnchoredY = -520f;

    [Header("Navigation Bar - ISDK")]
    [Tooltip("WaistNavCanvas 아래 NavigationBar를 연결하세요. 위치는 DustinyWaistNavFollow가 담당합니다.")]
    public GameObject navigationBarObject;
    public bool navigationBarStartsVisible = true;

    [Header("Navigation Buttons")]
    public Button durryNoteButton;
    public Button shopButton;
    public Button myPageButton;
    public Button menuButton;
    public bool autoConnectNavigationButtons = true;

    [Header("Page Container - No Shared Background")]
    [Tooltip("페이지를 묶는 빈 컨테이너입니다. 이 오브젝트 자체에는 Image/RawImage를 두지 마세요.")]
    public GameObject bigNoteRoot;
    public RectTransform bigNoteRect;
    public bool bigNoteStartsOpen = false;
    public DustinyPage startBigNotePage = DustinyPage.Note;

    [Tooltip("켜면 PageRoot의 RectTransform을 코드가 수정하지 않습니다.")]
    public bool preservePageRootInspectorLayout = true;
    public bool keepPageRootInView = true;
    public Vector2 pageRootAnchoredPosition = new Vector2(0f, -40f);
    public bool forcePageRootCenterAnchor = true;
    public bool applyPageRootSize = true;
    public Vector2 pageRootSize = new Vector2(1900f, 1150f);

    public bool bringPageRootToFrontWhenOpen = true;
    public bool hideDialogueWhenPageOpens = true;
    public bool hideNavigationBarWhenPageOpen = false;

    [Header("Page Roots - Each Page Owns Its Background")]
    [Tooltip("더리 미션 노트 전체 루트. NoteBackground를 이 오브젝트 안에 둡니다.")]
    public GameObject notePageObject;

    [Tooltip("기존 Shop 배경을 포함한 ShopPage 전체 루트입니다. 코드는 내부 배경을 수정하지 않습니다.")]
    public GameObject shopPageObject;

    [Tooltip("기존 MyPage 배경을 포함한 MyPage 전체 루트입니다. 코드는 내부 배경을 수정하지 않습니다.")]
    public GameObject myPageObject;

    [Tooltip("메뉴 전체 루트. MenuBackground를 이 오브젝트 안에 둡니다.")]
    public GameObject menuPageObject;

    [Header("Note / Menu Background References (Optional)")]
    [Tooltip("NotePage 내부의 전용 배경. 비워두면 NotePage 아래에서 Background를 자동 탐색합니다.")]
    public GameObject noteBackgroundObject;

    [Tooltip("MenuPage 내부의 전용 배경. 비워두면 MenuPage 아래에서 Background를 자동 탐색합니다.")]
    public GameObject menuBackgroundObject;

    [Header("Shared Background For Shop / MyPage")]
    [Tooltip("BigNoteRoot 아래의 기존 ViewpointBackground를 연결하세요. Shop/MyPage에서만 표시됩니다.")]
    public GameObject obsoleteSharedBackgroundObject;
    [Tooltip("켜면 Shop과 MyPage에서 기존 ViewpointBackground를 사용하고, Note/Menu에서는 숨깁니다.")]
    public bool useSharedBackgroundForShopAndMyPage = true;

    [Tooltip("BigNoteRoot 자체에 Image가 붙어 있다면 해당 Graphic만 끕니다. 자식 페이지 배경은 건드리지 않습니다.")]
    public bool disableGraphicOnPageRoot = true;

    [Header("Page Side Tag Buttons")]
    [Tooltip("SideTags 루트를 연결하면 자동 탐색이 다른 페이지 버튼을 잘못 잡는 일을 막을 수 있습니다.")]
    public Transform sideTagsRoot;
    public Button closePageButton;
    public Button noteTagButton;
    public Button shopTagButton;
    public Button myPageTagButton;
    public Button menuTagButton;
    public bool autoConnectPageButtons = true;

    [Header("Page Tag Pop Out")]
    public RectTransform closeTagRect;
    public RectTransform noteTagRect;
    public RectTransform shopTagRect;
    public RectTransform myPageTagRect;
    public RectTransform menuTagRect;
    public bool popOutActiveTag = true;
    public bool closeTagAlwaysPopped = true;
    public float activeTagOffsetX = 14f;
    public float inactiveTagOffsetX = 0f;

    [Header("Real AI Mission Integration")]
    [Tooltip("AI 스캔과 더리 노트를 연결하는 DustinyMissionController입니다.")]
    public DustinyMissionController missionController;

    [Header("Start / Summon")]
    public bool summonDurryOnStart = true;
    public bool summonDurryOnMissionStart = true;
    public bool detachDurryAndScanZoneFromParent = false;

    [Header("Input")]
    [Tooltip("에디터 테스트용입니다. 실제 Quest 빌드에서는 UI 버튼과 Interaction SDK 핀치를 사용합니다.")]
    public bool allowControllerFallback = false;

    [Header("Hand Tracking Confirm")]
    [Tooltip("오른손 검지 핀치를 공용 확인 입력으로 사용합니다. 중지 핀치는 사용하지 않습니다.")]
    public bool rightIndexPinchConfirms = true;
    public OVRHand rightHand;
    public bool autoFindRightOVRHandIfMissing = true;
    [Min(0f)] public float confirmInputCooldown = 0.20f;

    [Header("Durry Expression Animation")]
    [Tooltip("더리 캐릭터에 붙은 Animator입니다. 비워두면 Durry Object 아래에서 자동 탐색합니다.")]
    public Animator durryAnimator;
    public bool autoFindDurryAnimator = true;

    [Tooltip("Animator에 같은 이름의 Trigger가 있으면 Trigger를 먼저 사용하고, 없으면 상태를 직접 재생합니다.")]
    public bool preferAnimatorTriggers = true;

    [Min(0f)] public float expressionCrossFadeDuration = 0.12f;
    public bool logMissingExpressionWarnings = true;

    [Tooltip("Opening Speech Steps와 같은 순서로 표정 상태 이름을 넣습니다.")]
    public string[] openingExpressionStates =
    {
        "Surprised",
        "Confused",
        "Look",
        "Sad",
        "Confused",
        "Surprised",
        "Joyful",
        "Look",
        "Woried",
        "Focused"
    };

    [Tooltip("디스크립션으로 미션을 안내할 때 사용할 표정입니다.")]
    public string missionDescriptionExpressionState = "Focused";
    [Tooltip("디스크립션 확인 후 실제 미션을 수행하는 동안 사용할 표정입니다.")]
    public string missionInProgressExpressionState = "Look";
    public string missionCompleteExpressionState = "Joyful";
    public string missionIncompleteExpressionState = "Woried";
    public string nameQuestionExpressionState = "Joyful";
    public string nameConfirmedExpressionState = "Joyful";
    public string finalGreetingExpressionState = "Greet_Short";
    public string idleExpressionState = "DurryIdleAni";

    [Header("Durry Fixed World Start")]
    public bool useCurrentSceneDurryPositionOnStart = false;
    public Vector3 durryStartWorldPosition = new Vector3(0f, -0.35f, 1.2f);
    public Vector3 durryStartWorldEuler = new Vector3(0f, 180f, 0f);
    public bool faceDurryToUserOnStart = true;

    [Header("Durry Summon Placement")]
    public float durryDistance = 2f;
    public float durrySideOffset = 0f;
    public float durryHeightOffset = -1f;
    public float durryYawOffset = 180f;

    [Header("Durry Size")]
    public float durryRootScale = 1f;
    public float durryVisualScale = 10f;

    [Header("Visual Cleanup")]
    public bool removeTextShadow = true;
    [Tooltip("말풍선과 디스크립션 안의 Shadow만 끕니다. Shop/MyPage 디자인의 Shadow는 유지합니다.")]
    public bool disableDialogueShadowComponents = true;
    public bool disableDurryShadows = true;
    public bool makeOnlyDialogueBlackDimTransparent = false;

    [Header("Scan Zone Placement")]
    public bool moveScanZoneWhenDurrySummoned = true;
    public float scanZoneDistance = 1.7f;
    public float scanZoneHeightOffset = -0.55f;
    public float scanZoneSize = 0.8f;

    [Header("Mission Completion Visual")]
    public bool changeDurryColorOnMissionComplete = false;

    private OnboardingState onboardingState = OnboardingState.IntroSpeech;
    private int openingSpeechIndex;
    private bool onboardingActive;
    private bool missionRoundActive;
    private float lastDialogueAdvanceTime = -999f;
    private float lastConfirmInputTime = -999f;

    private SpeechBubbleAutoSize speechAutoSize;
    private SpeechBubbleAutoSize descriptionAutoSize;

    private bool wasTriggerPressed;
    private bool wasRightIndexPinching;

    private DustinyPage currentPage = DustinyPage.None;
    private bool pageRootOpen;
    private bool tagBasePositionsSaved;
    private Vector2 closeTagBasePosition;
    private Vector2 noteTagBasePosition;
    private Vector2 shopTagBasePosition;
    private Vector2 myPageTagBasePosition;
    private Vector2 menuTagBasePosition;

    private Renderer[] durryRenderers;
    private Material[] durryRuntimeMaterials;
    private Renderer scanZoneRenderer;
    private Transform resolvedUIRoot;

    private void OnValidate()
    {
        if (synchronizeDialoguePosition)
        {
            // 인스펙터에서도 두 레거시 위치가 서로 달라 보이지 않도록 공통 위치로 맞춥니다.
            speechBubbleAnchoredPosition = sharedDialogueAnchoredPosition;
            descriptionAnchoredPosition = sharedDialogueAnchoredPosition;
        }
    }

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start - modular manager integration");

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        ResolveRightHandIfNeeded();
        ResolveUIRoot();
        SetupDurry();
        SetupScanZone();

        ResolveDialogueReferences();
        SetupDescriptionDimOverlay();
        ResolveNavigationReferences();
        ResolveNavigationButtons();
        ConnectNavigationButtonEvents();

        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
        ConnectPageButtonEvents();
        ConfigurePageRootGraphics();

        SetupWorldCanvas();
        SetNavigationBarVisible(navigationBarStartsVisible);

        if (bigNoteStartsOpen)
        {
            OpenPage(startBigNotePage);
        }
        else
        {
            SetPageRootVisible(false);
        }

        PlaceDurryAtStartWorldPosition();

        if (summonDurryOnStart)
        {
            SummonDurryToUser();
        }

        UpdateViewLockedUI();
        UpdateDialoguePlacement();
        UpdatePageRootPlacement();

        if (startOnboardingFlowOnStart)
        {
            BeginOnboardingFlow();
        }
        else
        {
            ShowDescription(
                "미션 시작 버튼을 누르면 책상 스캔을 시작해.\n" +
                "스캔이 끝나면 더리 노트가 활성화돼!"
            );
        }
    }

    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        UpdateViewLockedUI();
        UpdateDialoguePlacement();
        UpdatePageRootPlacement();

        if (rightIndexPinchConfirms)
        {
            UpdateRightIndexPinchInput();
        }

        if (allowControllerFallback)
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                OnPressA();
            }

            if (GetRightTriggerDown())
            {
                OnConfirmButtonPressed();
            }
        }

        if (!lockUIToUserView)
        {
            FaceCanvasToUser();
        }
    }

    #region Navigation And Pages

    private void ResolveNavigationReferences()
    {
        if (navigationBarObject != null)
        {
            return;
        }

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        Transform found = FindChildTransformContainsAll(searchRoot, "navigation", "bar") ??
                          FindChildTransformContains(searchRoot, "navbar") ??
                          FindChildTransformContains(searchRoot, "navigationbar");

        if (found != null)
        {
            navigationBarObject = found.gameObject;
        }
    }

    public void SetNavigationBarVisible(bool visible)
    {
        ResolveNavigationReferences();

        if (navigationBarObject != null)
        {
            navigationBarObject.SetActive(visible);
        }
    }

    public void ShowNavigationBar()
    {
        SetNavigationBarVisible(true);
    }

    public void HideNavigationBar()
    {
        SetNavigationBarVisible(false);
    }

    public void ToggleNavigationBar()
    {
        ResolveNavigationReferences();

        if (navigationBarObject != null)
        {
            navigationBarObject.SetActive(!navigationBarObject.activeSelf);
        }
    }

    private void ResolveNavigationButtons()
    {
        Transform searchRoot = navigationBarObject != null
            ? navigationBarObject.transform
            : worldCanvas != null ? worldCanvas.transform : transform;

        if (durryNoteButton == null)
        {
            durryNoteButton = FindButtonByKeywords(searchRoot, "durry", "note") ??
                              FindButtonByKeywords(searchRoot, "dury", "note") ??
                              FindButtonByKeywords(searchRoot, "note");
        }

        if (shopButton == null)
        {
            shopButton = FindButtonByKeywords(searchRoot, "shop");
        }

        if (myPageButton == null)
        {
            myPageButton = FindButtonByKeywords(searchRoot, "my", "page") ??
                           FindButtonByKeywords(searchRoot, "mypage");
        }

        if (menuButton == null)
        {
            menuButton = FindButtonByKeywords(searchRoot, "menu");
        }
    }

    private void ConnectNavigationButtonEvents()
    {
        if (!autoConnectNavigationButtons)
        {
            return;
        }

        ResolveNavigationButtons();
        ConnectButtonClick(durryNoteButton, OpenNotePage);
        ConnectButtonClick(shopButton, OpenShopPage);
        ConnectButtonClick(myPageButton, OpenMyPage);
        ConnectButtonClick(menuButton, OpenMenuPage);
    }

    private void ResolvePageReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (bigNoteRoot == null)
        {
            Transform foundRoot = FindChildTransformExact(searchRoot, "BigNoteRoot") ??
                                  FindChildTransformExact(searchRoot, "PageRoot") ??
                                  FindChildTransformContainsAll(searchRoot, "page", "root");

            if (foundRoot != null)
            {
                bigNoteRoot = foundRoot.gameObject;
            }
        }

        if (bigNoteRect == null && bigNoteRoot != null)
        {
            bigNoteRect = bigNoteRoot.GetComponent<RectTransform>();
        }

        if (notePageObject == null)
        {
            notePageObject = GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "note"));
        }

        if (shopPageObject == null)
        {
            shopPageObject = GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "shop"));
        }

        if (myPageObject == null)
        {
            myPageObject = GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "mypage")) ??
                           GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "my"));
        }

        if (menuPageObject == null)
        {
            menuPageObject = GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "menu"));
        }

        if (sideTagsRoot == null)
        {
            Transform tagSearchRoot = bigNoteRoot != null ? bigNoteRoot.transform : searchRoot;
            sideTagsRoot = FindChildTransformContainsAll(tagSearchRoot, "side", "tag") ??
                           FindChildTransformContains(tagSearchRoot, "sidetags");
        }
    }

    private void ResolvePageBackgroundReferences()
    {
        if (noteBackgroundObject == null && notePageObject != null)
        {
            Transform background = FindChildTransformContains(notePageObject.transform, "background");
            if (background != null)
            {
                noteBackgroundObject = background.gameObject;
            }
        }

        if (menuBackgroundObject == null && menuPageObject != null)
        {
            Transform background = FindChildTransformContains(menuPageObject.transform, "background");
            if (background != null)
            {
                menuBackgroundObject = background.gameObject;
            }
        }

        if (obsoleteSharedBackgroundObject == null && bigNoteRoot != null)
        {
            obsoleteSharedBackgroundObject = FindDirectChildExact(bigNoteRoot.transform, "ViewpointBackground") ??
                                               FindDirectChildExact(bigNoteRoot.transform, "SharedBackground") ??
                                               FindDirectChildExact(bigNoteRoot.transform, "CommonBackground");
        }
    }

    private void ConfigurePageRootGraphics()
    {
        ResolvePageReferences();
        ResolvePageBackgroundReferences();

        if (disableGraphicOnPageRoot && bigNoteRoot != null)
        {
            Graphic[] rootGraphics = bigNoteRoot.GetComponents<Graphic>();
            foreach (Graphic graphic in rootGraphics)
            {
                if (graphic != null)
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            }
        }
    }

    private void ResolvePageButtons()
    {
        ResolvePageReferences();

        Transform searchRoot = sideTagsRoot != null
            ? sideTagsRoot
            : bigNoteRoot != null ? bigNoteRoot.transform
            : worldCanvas != null ? worldCanvas.transform : transform;

        if (closePageButton == null)
        {
            closePageButton = FindButtonByKeywords(searchRoot, "close") ??
                              FindButtonByKeywords(searchRoot, "exit");
        }

        if (noteTagButton == null)
        {
            noteTagButton = FindButtonByKeywords(searchRoot, "note");
        }

        if (shopTagButton == null)
        {
            shopTagButton = FindButtonByKeywords(searchRoot, "shop");
        }

        if (myPageTagButton == null)
        {
            myPageTagButton = FindButtonByKeywords(searchRoot, "my", "page") ??
                              FindButtonByKeywords(searchRoot, "mypage");
        }

        if (menuTagButton == null)
        {
            menuTagButton = FindButtonByKeywords(searchRoot, "menu");
        }

        if (closeTagRect == null && closePageButton != null)
        {
            closeTagRect = closePageButton.GetComponent<RectTransform>();
        }

        if (noteTagRect == null && noteTagButton != null)
        {
            noteTagRect = noteTagButton.GetComponent<RectTransform>();
        }

        if (shopTagRect == null && shopTagButton != null)
        {
            shopTagRect = shopTagButton.GetComponent<RectTransform>();
        }

        if (myPageTagRect == null && myPageTagButton != null)
        {
            myPageTagRect = myPageTagButton.GetComponent<RectTransform>();
        }

        if (menuTagRect == null && menuTagButton != null)
        {
            menuTagRect = menuTagButton.GetComponent<RectTransform>();
        }

        SavePageTagBasePositions();
    }

    private void ConnectPageButtonEvents()
    {
        if (!autoConnectPageButtons)
        {
            return;
        }

        ResolvePageButtons();
        ConnectButtonClick(closePageButton, CloseBigNote);
        ConnectButtonClick(noteTagButton, OpenNotePage);
        ConnectButtonClick(shopTagButton, OpenShopPage);
        ConnectButtonClick(myPageTagButton, OpenMyPage);
        ConnectButtonClick(menuTagButton, OpenMenuPage);
    }

    public void OpenNotePage()
    {
        OpenPage(DustinyPage.Note);
    }

    public void OpenShopPage()
    {
        OpenPage(DustinyPage.Shop);
    }

    public void OpenMyPage()
    {
        OpenPage(DustinyPage.MyPage);
    }

    public void OpenMenuPage()
    {
        OpenPage(DustinyPage.Menu);
    }

    public void CloseBigNote()
    {
        currentPage = DustinyPage.None;
        SetPageRootVisible(false);
        UpdatePageTags();
    }

    public void OpenPage(DustinyPage page)
    {
        if (page == DustinyPage.None)
        {
            CloseBigNote();
            return;
        }

        if (page == DustinyPage.Note && missionController != null && !missionController.CanOpenNote)
        {
            Debug.Log("[더리 노트 잠금] 활성 미션이 없어서 노트를 열지 않습니다.");
            return;
        }

        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
        ConfigurePageRootGraphics();

        GameObject requestedPage = GetPageObject(page);
        if (requestedPage == null)
        {
            Debug.LogWarning($"[페이지 열기 실패] {page} Page Object가 연결되지 않았습니다.");
            return;
        }

        currentPage = page;
        SetPageRootVisible(true);

        SetPageObjectVisible(notePageObject, page == DustinyPage.Note);
        SetPageObjectVisible(shopPageObject, page == DustinyPage.Shop);
        SetPageObjectVisible(myPageObject, page == DustinyPage.MyPage);
        SetPageObjectVisible(menuPageObject, page == DustinyPage.Menu);

        bool showSharedBackground = useSharedBackgroundForShopAndMyPage &&
                                    (page == DustinyPage.Shop || page == DustinyPage.MyPage);
        if (obsoleteSharedBackgroundObject != null)
        {
            obsoleteSharedBackgroundObject.SetActive(showSharedBackground);
        }

        // Note and Menu own separate backgrounds. Shop/MyPage use the shared background above.
        SetSeparateBackgroundVisible(noteBackgroundObject, page == DustinyPage.Note, notePageObject);
        SetSeparateBackgroundVisible(menuBackgroundObject, page == DustinyPage.Menu, menuPageObject);

        if (bringPageRootToFrontWhenOpen && bigNoteRect != null)
        {
            bigNoteRect.SetAsLastSibling();
        }

        if (hideDialogueWhenPageOpens)
        {
            HideDialoguePanels();
        }

        UpdatePageTags();
        UpdatePageRootPlacement();
        Debug.Log($"[페이지 열기] {page}");
    }

    private GameObject GetPageObject(DustinyPage page)
    {
        switch (page)
        {
            case DustinyPage.Note: return notePageObject;
            case DustinyPage.Shop: return shopPageObject;
            case DustinyPage.MyPage: return myPageObject;
            case DustinyPage.Menu: return menuPageObject;
            default: return null;
        }
    }

    private void SetPageRootVisible(bool visible)
    {
        ResolvePageReferences();
        pageRootOpen = visible;

        if (bigNoteRoot != null)
        {
            bigNoteRoot.SetActive(visible);
        }

        if (!visible)
        {
            SetPageObjectVisible(notePageObject, false);
            SetPageObjectVisible(shopPageObject, false);
            SetPageObjectVisible(myPageObject, false);
            SetPageObjectVisible(menuPageObject, false);
            SetSeparateBackgroundVisible(noteBackgroundObject, false, notePageObject);
            SetSeparateBackgroundVisible(menuBackgroundObject, false, menuPageObject);
            if (obsoleteSharedBackgroundObject != null)
            {
                obsoleteSharedBackgroundObject.SetActive(false);
            }
        }

        if (hideNavigationBarWhenPageOpen)
        {
            SetNavigationBarVisible(!visible && navigationBarStartsVisible);
        }
    }

    private static void SetPageObjectVisible(GameObject pageObject, bool visible)
    {
        if (pageObject != null)
        {
            pageObject.SetActive(visible);
        }
    }

    private static void SetSeparateBackgroundVisible(GameObject backgroundObject, bool visible, GameObject owningPage)
    {
        if (backgroundObject == null)
        {
            return;
        }

        // A child background naturally follows its page. Setting it explicitly is still safe.
        // A separately placed background is also supported.
        if (owningPage == null || backgroundObject != owningPage)
        {
            backgroundObject.SetActive(visible);
        }
    }

    private void UpdatePageRootPlacement()
    {
        if (preservePageRootInspectorLayout)
        {
            return;
        }

        ResolvePageReferences();
        if (bigNoteRect == null || !keepPageRootInView)
        {
            return;
        }

        if (forcePageRootCenterAnchor)
        {
            bigNoteRect.anchorMin = new Vector2(0.5f, 0.5f);
            bigNoteRect.anchorMax = new Vector2(0.5f, 0.5f);
            bigNoteRect.pivot = new Vector2(0.5f, 0.5f);
        }

        bigNoteRect.anchoredPosition = pageRootAnchoredPosition;

        if (applyPageRootSize && pageRootSize.x > 0f && pageRootSize.y > 0f)
        {
            bigNoteRect.sizeDelta = pageRootSize;
        }
    }

    private void SavePageTagBasePositions()
    {
        if (tagBasePositionsSaved)
        {
            return;
        }

        if (closeTagRect != null) closeTagBasePosition = closeTagRect.anchoredPosition;
        if (noteTagRect != null) noteTagBasePosition = noteTagRect.anchoredPosition;
        if (shopTagRect != null) shopTagBasePosition = shopTagRect.anchoredPosition;
        if (myPageTagRect != null) myPageTagBasePosition = myPageTagRect.anchoredPosition;
        if (menuTagRect != null) menuTagBasePosition = menuTagRect.anchoredPosition;

        tagBasePositionsSaved = true;
    }

    private void UpdatePageTags()
    {
        if (!popOutActiveTag)
        {
            return;
        }

        ResolvePageButtons();
        SavePageTagBasePositions();

        bool isOpen = pageRootOpen;
        SetTagPopped(closeTagRect, closeTagBasePosition, isOpen && closeTagAlwaysPopped);
        SetTagPopped(noteTagRect, noteTagBasePosition, isOpen && currentPage == DustinyPage.Note);
        SetTagPopped(shopTagRect, shopTagBasePosition, isOpen && currentPage == DustinyPage.Shop);
        SetTagPopped(myPageTagRect, myPageTagBasePosition, isOpen && currentPage == DustinyPage.MyPage);
        SetTagPopped(menuTagRect, menuTagBasePosition, isOpen && currentPage == DustinyPage.Menu);
    }

    private void SetTagPopped(RectTransform tagRect, Vector2 basePosition, bool popped)
    {
        if (tagRect == null)
        {
            return;
        }

        float offset = popped ? activeTagOffsetX : inactiveTagOffsetX;
        tagRect.anchoredPosition = basePosition + new Vector2(offset, 0f);
    }

    private static void ConnectButtonClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    #endregion

    #region Dialogue And Onboarding

    private void ResolveDialogueReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (speechBubbleObject == null)
        {
            Transform speech = FindChildTransformContainsAll(searchRoot, "speech", "bubble") ??
                               FindChildTransformContains(searchRoot, "speechbubble");
            if (speech != null)
            {
                speechBubbleObject = speech.gameObject;
            }
        }

        if (descriptionObject == null)
        {
            Transform description = FindChildTransformExact(searchRoot, "Description") ??
                                    FindChildTransformContains(searchRoot, "description");
            if (description != null)
            {
                descriptionObject = description.gameObject;
            }
        }

        if (speechText == null && speechBubbleObject != null)
        {
            speechText = speechBubbleObject.GetComponentInChildren<TMP_Text>(true);
        }

        if (descriptionText == null && descriptionObject != null)
        {
            TMP_Text[] texts = descriptionObject.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text == null)
                {
                    continue;
                }

                string lowerName = text.name.ToLowerInvariant();
                if (lowerName.Contains("description") || lowerName.Contains("quest"))
                {
                    descriptionText = text;
                    break;
                }
            }

            if (descriptionText == null && texts.Length > 0)
            {
                descriptionText = texts[0];
            }
        }

        if (speechBubbleRect == null && speechBubbleObject != null)
        {
            speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
        }

        if (descriptionRect == null && descriptionObject != null)
        {
            descriptionRect = descriptionObject.GetComponent<RectTransform>();
        }

        speechAutoSize = speechBubbleObject != null
            ? speechBubbleObject.GetComponent<SpeechBubbleAutoSize>()
            : null;

        descriptionAutoSize = descriptionObject != null
            ? descriptionObject.GetComponent<SpeechBubbleAutoSize>()
            : null;

        ConfigureAutoSize(speechAutoSize, speechText, speechBubbleRect);
        ConfigureAutoSize(descriptionAutoSize, descriptionText, descriptionRect);

        if (durryNameInputObject == null && durryNameInputField != null)
        {
            durryNameInputObject = durryNameInputField.gameObject;
        }

        if (durryNameInputField != null)
        {
            durryNameInputField.text = string.Empty;
            durryNameInputField.onSubmit.RemoveListener(OnNameInputSubmitted);
            durryNameInputField.onSubmit.AddListener(OnNameInputSubmitted);
        }

        SetNameInputVisible(false);
    }

    private static void ConfigureAutoSize(SpeechBubbleAutoSize autoSize, TMP_Text text, RectTransform bubbleRect)
    {
        if (autoSize == null || text == null)
        {
            return;
        }

        // SpeechBubbleAutoSize keeps the legacy field name questText.
        autoSize.questText = text;

        if (autoSize.textRect == null)
        {
            autoSize.textRect = text.GetComponent<RectTransform>();
        }

        if (autoSize.bubbleRect == null && bubbleRect != null)
        {
            autoSize.bubbleRect = bubbleRect;
        }
    }

    private void BeginOnboardingFlow()
    {
        onboardingActive = true;
        onboardingState = OnboardingState.IntroSpeech;
        openingSpeechIndex = 0;
        missionRoundActive = false;
        durryName = string.IsNullOrWhiteSpace(durryName) ? defaultDurryName : durryName;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
        }

        ShowCurrentOpeningSpeech();
    }

    private void ShowCurrentOpeningSpeech()
    {
        if (openingSpeechSteps == null || openingSpeechSteps.Length == 0)
        {
            StartFirstMissionDescription();
            return;
        }

        openingSpeechIndex = Mathf.Clamp(openingSpeechIndex, 0, openingSpeechSteps.Length - 1);
        ShowSpeech(openingSpeechSteps[openingSpeechIndex]);
        PlayOpeningExpression(openingSpeechIndex);
    }

    private bool TryAdvanceOpeningSpeech()
    {
        if (openingSpeechSteps == null || openingSpeechSteps.Length == 0)
        {
            return false;
        }

        int nextIndex = openingSpeechIndex + 1;
        if (nextIndex >= openingSpeechSteps.Length)
        {
            return false;
        }

        openingSpeechIndex = nextIndex;
        ShowCurrentOpeningSpeech();
        return true;
    }

    private void AdvanceOnboardingFlow()
    {
        if (Time.time - lastDialogueAdvanceTime < dialogueAdvanceCooldown || !onboardingActive)
        {
            return;
        }

        lastDialogueAdvanceTime = Time.time;

        switch (onboardingState)
        {
            case OnboardingState.IntroSpeech:
                if (!TryAdvanceOpeningSpeech())
                {
                    StartFirstMissionDescription();
                }
                break;

            case OnboardingState.MissionDescription:
                ConfirmFirstMissionDescription();
                break;

            case OnboardingState.MissionInProgress:
                missionController?.HandleMissionConfirmPressed();
                break;

            case OnboardingState.NameQuestionSpeech:
                ConfirmDurryNameAndShowDescription();
                break;

            case OnboardingState.NamedDescription:
                ShowFinalSpeech();
                break;

            case OnboardingState.FinalSpeech:
                FinishOnboardingFlow();
                break;
        }
    }

    private void StartFirstMissionDescription()
    {
        onboardingState = OnboardingState.MissionDescription;
        StartMissionRound(firstMissionDescription);
        PlayDurryExpression(missionDescriptionExpressionState);
    }

    private void ConfirmFirstMissionDescription()
    {
        onboardingState = OnboardingState.MissionInProgress;
        HideDialoguePanels();
        PlayDurryExpression(missionInProgressExpressionState);

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController != null)
        {
            missionController.BeginMissionScan();
        }
        else
        {
            ShowDescription("AI 미션 컨트롤러가 연결되지 않았어!");
            Debug.LogError("[DustinyDemoFlow] DustinyMissionController가 연결되지 않았습니다.");
        }
    }

    private void ShowNameQuestionSpeech()
    {
        onboardingState = OnboardingState.NameQuestionSpeech;
        ShowSpeech(nameQuestionSpeech);
        PlayDurryExpression(nameQuestionExpressionState);

        if (!showNameInputWithNameQuestion)
        {
            return;
        }

        SetNameInputVisible(true);

        if (durryNameInputField != null)
        {
            durryNameInputField.text = string.Empty;
            durryNameInputField.ActivateInputField();
        }
    }

    private void OnNameInputSubmitted(string submittedName)
    {
        if (!onboardingActive || onboardingState != OnboardingState.NameQuestionSpeech)
        {
            return;
        }

        ConfirmDurryNameAndShowDescription(submittedName);
    }

    public void SetDurryNameFromVoice(string recognizedName)
    {
        if (!onboardingActive || onboardingState != OnboardingState.NameQuestionSpeech)
        {
            return;
        }

        ConfirmDurryNameAndShowDescription(recognizedName);
    }

    private void ConfirmDurryNameAndShowDescription(string inputName = null)
    {
        string rawName = inputName;

        if (string.IsNullOrWhiteSpace(rawName) && durryNameInputField != null)
        {
            rawName = durryNameInputField.text;
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            rawName = defaultDurryName;
        }

        durryName = rawName.Trim();
        if (string.IsNullOrWhiteSpace(durryName))
        {
            durryName = defaultDurryName;
        }

        SetNameInputVisible(false);
        onboardingState = OnboardingState.NamedDescription;
        ShowDescription(string.Format(namedDescriptionFormat, durryName));
        PlayDurryExpression(nameConfirmedExpressionState);
    }

    private void ShowFinalSpeech()
    {
        onboardingState = OnboardingState.FinalSpeech;
        ShowSpeech(string.Format(finalSpeechFormat, durryName));
        PlayDurryExpression(finalGreetingExpressionState);
    }

    private void FinishOnboardingFlow()
    {
        onboardingActive = false;
        onboardingState = OnboardingState.Finished;
        SetNameInputVisible(false);
        ShowDescription("하단바에서 NOTE / SHOP / MY PAGE / MENU를 선택할 수 있어.");
        PlayDurryExpression(idleExpressionState);
    }

    public void ShowDescriptionMessage(string message)
    {
        ShowDescription(message);
    }

    public void ShowSpeechMessage(string message)
    {
        ShowSpeech(message);
    }

    private void ShowSpeech(string message)
    {
        ResolveDialogueReferences();
        SetSpeechVisible(true);
        SetDescriptionVisible(false);

        if (speechText != null)
        {
            speechText.text = message;
        }

        if (speechAutoSize != null)
        {
            speechAutoSize.SetText(message);
            speechAutoSize.ResizeBubble();
        }

        UpdateDialoguePlacement();
    }

    private void ShowDescription(string message)
    {
        ResolveDialogueReferences();
        SetSpeechVisible(false);
        SetDescriptionVisible(true);
        SetNameInputVisible(false);

        if (descriptionText != null)
        {
            descriptionText.text = message;
        }

        if (descriptionAutoSize != null)
        {
            descriptionAutoSize.SetText(message);
            descriptionAutoSize.ResizeBubble();
        }

        UpdateDialoguePlacement();
    }

    private void HideDialoguePanels()
    {
        SetSpeechVisible(false);
        SetDescriptionVisible(false);
        SetNameInputVisible(false);
    }

    private void SetSpeechVisible(bool visible)
    {
        if (speechBubbleObject != null)
        {
            speechBubbleObject.SetActive(visible);
        }
        else if (speechText != null)
        {
            speechText.gameObject.SetActive(visible);
        }
    }

    private void SetDescriptionVisible(bool visible)
    {
        if (descriptionObject != null)
        {
            descriptionObject.SetActive(visible);
        }
        else if (descriptionText != null)
        {
            descriptionText.gameObject.SetActive(visible);
        }

        SetDescriptionDimVisible(visible);
    }

    private void SetupDescriptionDimOverlay()
    {
        if (descriptionDimOverlayObject == null && descriptionDimOverlayImage != null)
        {
            descriptionDimOverlayObject = descriptionDimOverlayImage.gameObject;
        }

        if (descriptionDimOverlayImage == null && descriptionDimOverlayObject != null)
        {
            descriptionDimOverlayImage = descriptionDimOverlayObject.GetComponent<Image>();
        }

        if (descriptionDimOverlayObject == null && autoCreateDescriptionDimOverlay)
        {
            Transform overlayParent = null;

            if (descriptionObject != null && descriptionObject.transform.parent != null)
            {
                overlayParent = descriptionObject.transform.parent;
            }
            else if (worldCanvas != null)
            {
                overlayParent = worldCanvas.transform;
            }

            if (overlayParent != null)
            {
                descriptionDimOverlayObject = new GameObject(
                    "DescriptionDimOverlay",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image)
                );

                descriptionDimOverlayObject.transform.SetParent(overlayParent, false);
                descriptionDimOverlayImage = descriptionDimOverlayObject.GetComponent<Image>();
            }
        }

        if (descriptionDimOverlayObject == null)
        {
            return;
        }

        RectTransform overlayRect = descriptionDimOverlayObject.GetComponent<RectTransform>();
        if (overlayRect != null)
        {
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
        }

        if (descriptionDimOverlayImage == null)
        {
            descriptionDimOverlayImage = descriptionDimOverlayObject.AddComponent<Image>();
        }

        Color dimColor = Color.black;
        dimColor.a = Mathf.Clamp01(descriptionDimOpacity);
        descriptionDimOverlayImage.color = dimColor;
        descriptionDimOverlayImage.raycastTarget = descriptionDimBlocksRaycasts;

        descriptionDimOverlayObject.SetActive(false);
    }

    private void SetDescriptionDimVisible(bool visible)
    {
        if (descriptionDimOverlayObject == null)
        {
            SetupDescriptionDimOverlay();
        }

        if (descriptionDimOverlayObject == null)
        {
            return;
        }

        if (descriptionDimOverlayImage != null)
        {
            Color dimColor = descriptionDimOverlayImage.color;
            dimColor.r = 0f;
            dimColor.g = 0f;
            dimColor.b = 0f;
            dimColor.a = Mathf.Clamp01(descriptionDimOpacity);
            descriptionDimOverlayImage.color = dimColor;
            descriptionDimOverlayImage.raycastTarget = descriptionDimBlocksRaycasts;
        }

        descriptionDimOverlayObject.SetActive(visible);

        if (visible)
        {
            BringDescriptionAboveDimOverlay();
        }
    }

    private void BringDescriptionAboveDimOverlay()
    {
        if (descriptionDimOverlayObject == null || descriptionObject == null)
        {
            return;
        }

        Transform overlayParent = descriptionDimOverlayObject.transform.parent;
        if (overlayParent == null)
        {
            return;
        }

        Transform descriptionLayerRoot = descriptionObject.transform;
        while (descriptionLayerRoot.parent != null && descriptionLayerRoot.parent != overlayParent)
        {
            descriptionLayerRoot = descriptionLayerRoot.parent;
        }

        descriptionDimOverlayObject.transform.SetAsLastSibling();

        if (descriptionLayerRoot.parent == overlayParent)
        {
            descriptionLayerRoot.SetAsLastSibling();
        }
    }

    private void SetNameInputVisible(bool visible)
    {
        if (durryNameInputObject != null)
        {
            durryNameInputObject.SetActive(visible);
        }
        else if (durryNameInputField != null)
        {
            durryNameInputField.gameObject.SetActive(visible);
        }
    }

    #endregion

    #region Mission Flow

    private void OnPressA()
    {
        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        missionController?.BeginMissionScan();
    }

    private void StartMissionRound(string message)
    {
        missionRoundActive = true;

        if (summonDurryOnMissionStart)
        {
            SummonDurryToUser();
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
        }

        ShowDescription(message);

        if (!onboardingActive || onboardingState != OnboardingState.MissionDescription)
        {
            PlayDurryExpression(missionDescriptionExpressionState);
        }
    }

    /// <summary>
    /// Interaction SDK의 공용 [확인] 버튼 OnClick에 연결합니다.
    /// 오른손 검지 핀치도 이 메서드를 호출하므로 두 입력의 역할이 같습니다.
    /// </summary>
    public void OnConfirmButtonPressed()
    {
        // 검지 핀치와 UI [확인] 버튼이 같은 프레임에 함께 들어와도 한 번만 처리합니다.
        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;

        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        missionController?.HandleMissionConfirmPressed();
    }

    /// <summary>
    /// 예전 UnityEvent 연결을 깨지 않기 위한 호환 메서드입니다.
    /// 자동 완료는 제거되었으며 체크박스 또는 재스캔만 사용합니다.
    /// </summary>
    public void CompleteMission()
    {
        Debug.LogWarning("[DustinyDemoFlow] 자동 미션 완료는 제거되었습니다. 체크박스 또는 재스캔을 사용하세요.");
    }

    public void NotifyMissionCompleted()
    {
        missionRoundActive = false;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
        }

        if (changeDurryColorOnMissionComplete)
        {
            RecoverDurryByCleanliness();
        }

        if (onboardingActive &&
            (onboardingState == OnboardingState.MissionDescription ||
             onboardingState == OnboardingState.MissionInProgress))
        {
            onboardingActive = false;
            onboardingState = OnboardingState.Finished;
        }
    }

    private string BuildCleanlinessMessage(string prefix)
    {
        if (CleanlinessManager.Instance == null)
        {
            return prefix;
        }

        int score = CleanlinessManager.Instance.CleanlinessScore;
        string stateName = CleanlinessManager.Instance.GetBosongStateName(score);
        return $"{prefix}\n현재 보송력 {score} / 4\n{stateName}";
    }

    #endregion

    #region Index Pinch And Controller Test Input

    private void ResolveRightHandIfNeeded()
    {
        if (!autoFindRightOVRHandIfMissing || rightHand != null)
        {
            return;
        }

        OVRHand[] hands = FindObjectsByType<OVRHand>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (OVRHand hand in hands)
        {
            if (hand == null)
            {
                continue;
            }

            string objectName = hand.gameObject.name.ToLowerInvariant();
            string parentName = hand.transform.parent != null
                ? hand.transform.parent.name.ToLowerInvariant()
                : string.Empty;

            if ((objectName + " " + parentName).Contains("right"))
            {
                rightHand = hand;
                return;
            }
        }
    }

    private void UpdateRightIndexPinchInput()
    {
        ResolveRightHandIfNeeded();

        bool isTracked = rightHand != null && rightHand.IsTracked && rightHand.IsDataValid;
        bool isPinching = isTracked &&
                          rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        bool pinchDown = isPinching && !wasRightIndexPinching;
        wasRightIndexPinching = isPinching;

        if (pinchDown)
        {
            OnConfirmButtonPressed();
        }
    }

    private bool GetRightTriggerDown()
    {
        float triggerValue = OVRInput.Get(
            OVRInput.Axis1D.PrimaryIndexTrigger,
            OVRInput.Controller.RTouch
        );

        bool isPressed = triggerValue > 0.8f;
        bool pressedThisFrame = isPressed && !wasTriggerPressed;
        wasTriggerPressed = isPressed;
        return pressedThisFrame;
    }

    #endregion

    #region Durry And Scan Zone

    private void SetupDurry()
    {
        if (durryObject == null)
        {
            Debug.LogWarning("Durry Object가 연결되지 않았습니다. DurryRoot를 넣어주세요.");
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            durryObject.transform.SetParent(null, true);
        }

        durryObject.SetActive(true);

        if (durryVisual == null)
        {
            durryVisual = durryObject.transform.Find("Durry Visual") ??
                          durryObject.transform.Find("DurryVisual");
        }

        ResolveDurryAnimator();
        ApplyDurryScale();

        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);
        durryRuntimeMaterials = new Material[durryRenderers.Length];

        for (int index = 0; index < durryRenderers.Length; index++)
        {
            Renderer renderer = durryRenderers[index];
            if (renderer == null)
            {
                continue;
            }

            if (disableDurryShadows)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Material runtimeMaterial = renderer.material;
            if (runtimeMaterial != null)
            {
                SetFloatIfHas(runtimeMaterial, "_ReceiveShadows", 0f);
            }

            durryRuntimeMaterials[index] = runtimeMaterial;
        }
    }

    private void ResolveDurryAnimator()
    {
        if (durryAnimator != null || !autoFindDurryAnimator || durryObject == null)
        {
            return;
        }

        durryAnimator = durryObject.GetComponentInChildren<Animator>(true);
    }

    private void PlayOpeningExpression(int index)
    {
        if (openingExpressionStates == null ||
            index < 0 ||
            index >= openingExpressionStates.Length)
        {
            return;
        }

        PlayDurryExpression(openingExpressionStates[index]);
    }

    public void PlayDurryExpression(string expressionName)
    {
        if (string.IsNullOrWhiteSpace(expressionName))
        {
            return;
        }

        ResolveDurryAnimator();
        if (durryAnimator == null || durryAnimator.runtimeAnimatorController == null)
        {
            if (logMissingExpressionWarnings)
            {
                Debug.LogWarning("[더리 표정] Animator가 연결되지 않았습니다.");
            }
            return;
        }

        int parameterHash = FindAnimatorTriggerHash(expressionName);
        if (preferAnimatorTriggers && parameterHash != 0)
        {
            durryAnimator.SetTrigger(parameterHash);
            return;
        }

        int shortStateHash = Animator.StringToHash(expressionName);
        int fullStateHash = Animator.StringToHash("Base Layer." + expressionName);

        if (durryAnimator.HasState(0, fullStateHash))
        {
            durryAnimator.CrossFadeInFixedTime(fullStateHash, expressionCrossFadeDuration, 0);
            return;
        }

        if (durryAnimator.HasState(0, shortStateHash))
        {
            durryAnimator.CrossFadeInFixedTime(shortStateHash, expressionCrossFadeDuration, 0);
            return;
        }

        // Trigger 사용을 선호하지 않도록 설정했지만 같은 이름의 Trigger는 존재하는 경우의 마지막 폴백입니다.
        if (parameterHash != 0)
        {
            durryAnimator.SetTrigger(parameterHash);
            return;
        }

        if (logMissingExpressionWarnings)
        {
            Debug.LogWarning($"[더리 표정] Animator에서 '{expressionName}' 상태 또는 Trigger를 찾지 못했습니다.");
        }
    }

    private int FindAnimatorTriggerHash(string parameterName)
    {
        if (durryAnimator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return 0;
        }

        AnimatorControllerParameter[] parameters = durryAnimator.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                string.Equals(parameter.name, parameterName, System.StringComparison.OrdinalIgnoreCase))
            {
                return parameter.nameHash;
            }
        }

        return 0;
    }

    private void ApplyDurryScale()
    {
        if (durryObject != null)
        {
            durryObject.transform.localScale = Vector3.one * durryRootScale;
        }

        if (durryVisual != null)
        {
            durryVisual.localScale = Vector3.one * durryVisualScale;
        }
    }

    private void PlaceDurryAtStartWorldPosition()
    {
        if (durryObject == null)
        {
            return;
        }

        if (!useCurrentSceneDurryPositionOnStart)
        {
            durryObject.transform.position = durryStartWorldPosition;
            durryObject.transform.rotation = Quaternion.Euler(durryStartWorldEuler);
        }
        else if (faceDurryToUserOnStart && centerEyeAnchor != null)
        {
            durryObject.transform.rotation = GetRotationFacingUser(
                durryObject.transform.position,
                durryYawOffset
            );
        }

        ApplyDurryScale();
    }

    private void SummonDurryToUser()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        Vector3 headPosition = centerEyeAnchor.position;
        Vector3 forward = GetFlatForward();
        Vector3 right = centerEyeAnchor.right;
        right.y = 0f;

        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.right;
        }

        right.Normalize();

        Vector3 durryPosition = headPosition +
                                forward * durryDistance +
                                right * durrySideOffset +
                                Vector3.up * durryHeightOffset;

        if (durryObject != null)
        {
            durryObject.transform.position = durryPosition;
            durryObject.transform.rotation = GetRotationFacingUser(durryPosition, durryYawOffset);
            ApplyDurryScale();
        }

        if (moveScanZoneWhenDurrySummoned)
        {
            PlaceScanZoneInFrontOfUser(forward);
        }
    }

    private void RecoverDurryByCleanliness()
    {
        if (durryRuntimeMaterials == null || durryRuntimeMaterials.Length == 0)
        {
            return;
        }

        int score = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 0;

        float normalizedScore = Mathf.Clamp01(score / 4f);
        Color currentColor = Color.Lerp(
            new Color(0.55f, 0.52f, 0.48f, 1f),
            new Color(1f, 0.94f, 0.86f, 1f),
            normalizedScore
        );

        foreach (Material material in durryRuntimeMaterials)
        {
            if (material == null)
            {
                continue;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", currentColor);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", currentColor);
            }
        }
    }

    private void SetupScanZone()
    {
        if (scanZoneObject == null)
        {
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            scanZoneObject.transform.SetParent(null, true);
        }

        scanZoneRenderer = scanZoneObject.GetComponent<Renderer>();
        SetupScanZoneMaterial();
        scanZoneObject.SetActive(true);
    }

    private void PlaceScanZoneInFrontOfUser(Vector3 forward)
    {
        if (scanZoneObject == null || centerEyeAnchor == null)
        {
            return;
        }

        Vector3 scanPosition = centerEyeAnchor.position +
                               forward * scanZoneDistance +
                               Vector3.up * scanZoneHeightOffset;

        scanZoneObject.transform.position = scanPosition;
        scanZoneObject.transform.rotation = Quaternion.LookRotation(Vector3.up, forward);
        scanZoneObject.transform.localScale = Vector3.one * scanZoneSize;
    }

    private void SetupScanZoneMaterial()
    {
        if (scanZoneRenderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");

        if (shader == null)
        {
            return;
        }

        Material material = new Material(shader)
        {
            name = "M_ScanZone_SoftBlue_Runtime",
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
        };

        Color scanColor = new Color(0.45f, 0.85f, 1f, 0.55f);

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", scanColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", scanColor);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);

        scanZoneRenderer.material = material;
        scanZoneRenderer.enabled = true;
    }

    private Vector3 GetFlatForward()
    {
        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private Quaternion GetRotationFacingUser(Vector3 objectPosition, float yawOffset)
    {
        Vector3 toUser = centerEyeAnchor.position - objectPosition;
        toUser.y = 0f;

        if (toUser.sqrMagnitude < 0.001f)
        {
            toUser = -GetFlatForward();
        }

        Quaternion lookAtUser = Quaternion.LookRotation(toUser.normalized, Vector3.up);
        return lookAtUser * Quaternion.Euler(0f, yawOffset, 0f);
    }

    #endregion

    #region UI Placement And Cleanup

    private void ResolveUIRoot()
    {
        if (uiRoot != null)
        {
            resolvedUIRoot = uiRoot;
            return;
        }

        if (worldCanvas == null)
        {
            resolvedUIRoot = null;
            return;
        }

        resolvedUIRoot = worldCanvas.transform.parent != null &&
                         worldCanvas.transform.parent != centerEyeAnchor
            ? worldCanvas.transform.parent
            : worldCanvas.transform;
    }

    private void UpdateViewLockedUI()
    {
        if (!lockUIToUserView || centerEyeAnchor == null)
        {
            return;
        }

        if (resolvedUIRoot == null)
        {
            ResolveUIRoot();
        }

        if (resolvedUIRoot == null)
        {
            return;
        }

        if (forceUIRootUnderCenterEye && resolvedUIRoot.parent != centerEyeAnchor)
        {
            resolvedUIRoot.SetParent(centerEyeAnchor, false);
        }

        resolvedUIRoot.localPosition = uiRootLocalPosition;
        resolvedUIRoot.localRotation = Quaternion.Euler(uiRootLocalEuler);
        resolvedUIRoot.localScale = Vector3.one * Mathf.Max(0.001f, uiRootLocalScale);
    }

    private void SetupWorldCanvas()
    {
        if (worldCanvas == null)
        {
            Debug.LogWarning("World Canvas가 연결되지 않았습니다.");
            return;
        }

        worldCanvas.gameObject.SetActive(true);
        worldCanvas.renderMode = RenderMode.WorldSpace;

        Camera centerEyeCamera = centerEyeAnchor != null
            ? centerEyeAnchor.GetComponent<Camera>()
            : null;

        worldCanvas.worldCamera = centerEyeCamera != null ? centerEyeCamera : Camera.main;

        if (removeTextShadow)
        {
            RemoveTMPShadow(speechText);
            RemoveTMPShadow(descriptionText);
        }

        if (disableDialogueShadowComponents)
        {
            DisableShadowEffectsInDialogue();
        }

        if (makeOnlyDialogueBlackDimTransparent)
        {
            MakeDialogueBlackDimTransparent();
        }
    }

    private void UpdateDialoguePlacement()
    {
        if (!synchronizeDialoguePosition)
        {
            UpdateSpeechBubblePlacement();
            UpdateDescriptionPlacement();
            return;
        }

        RectTransform bubble = ResolveSpeechBubbleRect();
        RectTransform description = ResolveDescriptionRect();

        Vector2 targetPosition = sharedDialogueAnchoredPosition;
        if (clampDialogueAboveWaistNav)
        {
            targetPosition.y = Mathf.Max(targetPosition.y, sharedDialogueMinAnchoredY);
        }

        ApplyDialogueRectPlacement(
            bubble,
            targetPosition,
            forceSpeechBubbleCenterAnchor
        );

        ApplyDialogueRectPlacement(
            description,
            targetPosition,
            forceDescriptionCenterAnchor
        );

        // 서로 다른 부모 아래에 있어도 실제 화면상의 피벗 위치가 정확히 같도록 맞춥니다.
        if (forceSameDialogueWorldPosition &&
            bubble != null &&
            description != null &&
            bubble.parent != description.parent)
        {
            description.position = bubble.position;
            description.rotation = bubble.rotation;
        }
    }

    private static void ApplyDialogueRectPlacement(
        RectTransform rect,
        Vector2 anchoredPosition,
        bool forceCenterAnchor
    )
    {
        if (rect == null)
        {
            return;
        }

        if (forceCenterAnchor)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        rect.anchoredPosition = anchoredPosition;
    }

    private void UpdateSpeechBubblePlacement()
    {
        if (!keepSpeechBubbleBelowView)
        {
            return;
        }

        RectTransform bubble = ResolveSpeechBubbleRect();
        if (bubble == null)
        {
            return;
        }

        if (forceSpeechBubbleCenterAnchor)
        {
            bubble.anchorMin = new Vector2(0.5f, 0.5f);
            bubble.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.pivot = new Vector2(0.5f, 0.5f);
        }

        Vector2 targetPosition = speechBubbleAnchoredPosition;
        if (clampDialogueAboveWaistNav)
        {
            targetPosition.y = Mathf.Max(targetPosition.y, speechBubbleMinAnchoredY);
        }

        bubble.anchoredPosition = targetPosition;
    }

    private void UpdateDescriptionPlacement()
    {
        if (!keepDescriptionBelowView)
        {
            return;
        }

        RectTransform rect = ResolveDescriptionRect();
        if (rect == null)
        {
            return;
        }

        if (forceDescriptionCenterAnchor)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        Vector2 targetPosition = descriptionAnchoredPosition;
        if (clampDialogueAboveWaistNav)
        {
            targetPosition.y = Mathf.Max(targetPosition.y, descriptionMinAnchoredY);
        }

        rect.anchoredPosition = targetPosition;
    }

    private RectTransform ResolveDescriptionRect()
    {
        if (descriptionRect != null)
        {
            return descriptionRect;
        }

        if (descriptionObject != null)
        {
            descriptionRect = descriptionObject.GetComponent<RectTransform>();
            return descriptionRect;
        }

        return descriptionText != null
            ? descriptionText.GetComponentInParent<RectTransform>(true)
            : null;
    }

    private RectTransform ResolveSpeechBubbleRect()
    {
        if (speechBubbleRect != null)
        {
            return speechBubbleRect;
        }

        if (speechBubbleObject != null)
        {
            speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
            return speechBubbleRect;
        }

        return speechText != null
            ? speechText.GetComponentInParent<RectTransform>(true)
            : null;
    }

    private static void RemoveTMPShadow(TMP_Text text)
    {
        if (text == null || text.fontMaterial == null)
        {
            return;
        }

        Material material = new Material(text.fontMaterial)
        {
            name = text.name + "_NoShadow_Runtime"
        };

        material.DisableKeyword("UNDERLAY_ON");
        material.DisableKeyword("UNDERLAY_INNER");
        SetFloatIfHas(material, "_OutlineWidth", 0f);
        SetFloatIfHas(material, "_OutlineSoftness", 0f);
        SetFloatIfHas(material, "_FaceDilate", 0f);
        SetFloatIfHas(material, "_UnderlayOffsetX", 0f);
        SetFloatIfHas(material, "_UnderlayOffsetY", 0f);
        SetFloatIfHas(material, "_UnderlayDilate", 0f);
        SetFloatIfHas(material, "_UnderlaySoftness", 0f);
        SetColorIfHas(material, "_UnderlayColor", new Color(0f, 0f, 0f, 0f));
        SetColorIfHas(material, "_OutlineColor", new Color(0f, 0f, 0f, 0f));
        text.fontMaterial = material;
    }

    private void DisableShadowEffectsInDialogue()
    {
        DisableShadowEffectsInRoot(speechBubbleObject);
        DisableShadowEffectsInRoot(descriptionObject);
    }

    private static void DisableShadowEffectsInRoot(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Shadow[] shadows = root.GetComponentsInChildren<Shadow>(true);
        foreach (Shadow shadow in shadows)
        {
            if (shadow != null)
            {
                shadow.enabled = false;
            }
        }
    }

    private void MakeDialogueBlackDimTransparent()
    {
        MakeBlackDimTransparentInRoot(speechBubbleObject);
        MakeBlackDimTransparentInRoot(descriptionObject);
    }

    private static void MakeBlackDimTransparentInRoot(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image == null)
            {
                continue;
            }

            string objectName = image.gameObject.name.ToLowerInvariant();
            bool isBlackDim = objectName.Contains("shadow") ||
                              objectName.Contains("dim") ||
                              objectName.Contains("black");

            if (!isBlackDim)
            {
                continue;
            }

            Color color = image.color;
            color.a = 0f;
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private void FaceCanvasToUser()
    {
        if (worldCanvas == null || centerEyeAnchor == null)
        {
            return;
        }

        Vector3 direction = worldCanvas.transform.position - centerEyeAnchor.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            worldCanvas.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    #endregion

    #region Search Helpers

    private Button FindButtonByKeywords(Transform root, params string[] keywords)
    {
        Transform found = FindChildTransformContainsAll(root, keywords);
        if (found == null)
        {
            return null;
        }

        Button directButton = found.GetComponent<Button>();
        if (directButton != null)
        {
            return directButton;
        }

        Button childButton = found.GetComponentInChildren<Button>(true);
        return childButton != null ? childButton : found.GetComponentInParent<Button>(true);
    }

    private Transform FindBestPageTransform(Transform root, string pageKeyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(pageKeyword))
        {
            return null;
        }

        string normalizedKeyword = pageKeyword.ToLowerInvariant();
        Transform[] candidates = root.GetComponentsInChildren<Transform>(true);
        Transform best = null;
        int bestScore = int.MinValue;

        foreach (Transform candidate in candidates)
        {
            if (candidate == null || candidate == bigNoteRoot?.transform)
            {
                continue;
            }

            string lowerName = candidate.name.ToLowerInvariant();
            bool keywordMatches = normalizedKeyword == "mypage"
                ? lowerName.Contains("mypage") || (lowerName.Contains("my") && lowerName.Contains("page"))
                : lowerName.Contains(normalizedKeyword);

            if (!keywordMatches || !lowerName.Contains("page"))
            {
                continue;
            }

            if (lowerName.Contains("button") ||
                lowerName.Contains("tag") ||
                lowerName.Contains("background") ||
                lowerName.Contains("title"))
            {
                continue;
            }

            int score = 0;
            if (lowerName.EndsWith("root")) score += 5;
            if (lowerName == normalizedKeyword + "page") score += 10;
            if (lowerName == normalizedKeyword + "pageroot") score += 12;
            if (candidate.GetComponent<RectTransform>() != null) score += 1;

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static GameObject GetGameObjectFromTransform(Transform target)
    {
        return target != null ? target.gameObject : null;
    }

    private Transform FindChildTransformExact(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name == exactName)
            {
                return child;
            }
        }

        return null;
    }

    private GameObject FindDirectChildExact(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform child = root.GetChild(index);
            if (child != null && child.name == exactName)
            {
                return child.gameObject;
            }
        }

        return null;
    }

    private Transform FindChildTransformContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string lowerKeyword = keyword.ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != null && child.name.ToLowerInvariant().Contains(lowerKeyword))
            {
                return child;
            }
        }

        return null;
    }

    private Transform FindChildTransformContainsAll(Transform root, params string[] keywords)
    {
        if (root == null || keywords == null || keywords.Length == 0)
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == null)
            {
                continue;
            }

            string lowerName = child.name.ToLowerInvariant();
            bool containsAll = true;

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (!lowerName.Contains(keyword.ToLowerInvariant()))
                {
                    containsAll = false;
                    break;
                }
            }

            if (containsAll)
            {
                return child;
            }
        }

        return null;
    }

    private static void SetFloatIfHas(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void SetColorIfHas(Material material, string propertyName, Color value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    #endregion
}
