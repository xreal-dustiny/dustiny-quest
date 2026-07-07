using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    [Header("XR Reference")]
    public Transform centerEyeAnchor;

    [Header("Objects")]
    public GameObject durryObject;       // DurryRoot 넣기. 카메라 밑에 두지 말고 월드 오브젝트로 둔다.
    public Transform durryVisual;        // Durry Visual 넣기
    public GameObject scanZoneObject;
    public Canvas worldCanvas;

    [Header("View Locked UI")]
    public Transform uiRoot;             // CenterEyeAnchor 아래의 UIRoot 넣기. 비워두면 WorldCanvas의 부모를 자동 사용.
    public bool lockUIToUserView = true;
    public bool forceUIRootUnderCenterEye = false;

    // UIRoot가 CenterEyeAnchor의 자식일 때의 로컬 위치.
    // z: 눈앞 거리, y: 시야 안에서 위/아래 위치. y를 더 음수로 하면 더 아래로 내려감.
    public Vector3 uiRootLocalPosition = new Vector3(0f, -0.45f, 2f);
    public Vector3 uiRootLocalEuler = Vector3.zero;
    public float uiRootLocalScale = 1f;

    [Header("UI")]
    [Tooltip("기존 호환용입니다. 가능하면 Speech Text / Description Text를 따로 연결해주세요.")]
    public TMP_Text questText;
    public TMP_Text bosongText;

    [Header("Dialogue / Description Flow")]
    public bool startOnboardingFlowOnStart = true;
    public bool pinchAdvancesDialogue = true;
    public float dialogueAdvanceCooldown = 0.25f;

    [Tooltip("더리 대사가 들어가는 말풍선 오브젝트입니다. 예: WorldCanvas/SpeechBubble")]
    public GameObject speechBubbleObject;
    public TMP_Text speechText;

    [Tooltip("게임 진행 방법/미션이 들어가는 설명창 오브젝트입니다. 예: WorldCanvas/Description")]
    public GameObject descriptionObject;
    public TMP_Text descriptionText;

    [Header("Name Input")]
    public TMP_InputField durryNameInputField;
    public GameObject durryNameInputObject;
    public string defaultDurryName = "더리";
    public string durryName = "더리";
    public bool showNameInputWithNameQuestion = false;

    [Header("Onboarding Script")]
    [TextArea(2, 4)] public string introSpeech = "난 누구지...\n여긴 어디?";
    [TextArea(2, 5)] public string firstMissionDescription = "미션\n더리 주변의 어질러진 물건 3개를 정리해줘.\n정리가 끝나면 오른손 약지 핀치로 완료해줘.";
    [TextArea(2, 5)] public string nameQuestionSpeech = "그래! 난 더리랜드에서 온 청소 요정이었어!\n나에게 이름을 지어줄래?";
    [TextArea(2, 4)] public string namedDescriptionFormat = "{0}(이)라는 이름을 지어줬다!";
    [TextArea(2, 4)] public string finalSpeechFormat = "앞으로 잘 부탁해!\n나는 {0}(이)야.";

    [Header("Speech Bubble Placement")]
    public RectTransform speechBubbleRect;                 // 말풍선 Bubble Rect. 비워두면 QuestText 부모에서 자동 탐색.
    public bool keepSpeechBubbleBelowView = false;          // true면 말풍선을 사용자 시야 아래쪽에 계속 고정.
    public Vector2 speechBubbleAnchoredPosition = new Vector2(0f, -1100f);
    public bool forceSpeechBubbleCenterAnchor = true;

    [Header("Description Placement")]
    public RectTransform descriptionRect;
    public bool keepDescriptionBelowView = true;
    public Vector2 descriptionAnchoredPosition = new Vector2(0f, -1100f);
    public bool forceDescriptionCenterAnchor = true;

    [Header("Navigation Bar Placement")]
    [Tooltip("항상 사용자 시야 아래에 붙일 네비게이션 바 오브젝트입니다. 예: WorldCanvas/menuPanel 또는 WorldCanvas/NavigationBar")]
    public GameObject navigationBarObject;
    public RectTransform navigationBarRect;
    public bool navigationBarStartsVisible = true;
    public bool keepNavigationBarBelowView = true;
    public Vector2 navigationBarAnchoredPosition = new Vector2(0f, -430f);
    public bool forceNavigationBarCenterAnchor = true;

    [Tooltip("true로 켜면 코드가 네비게이션 바 크기를 고정합니다. Figma에서 만든 크기를 유지하고 싶으면 false로 두세요.")]
    public bool applyNavigationBarSize = false;
    public Vector2 navigationBarSize = new Vector2(900f, 170f);


    [Header("Navigation Bar Buttons")]
    [Tooltip("비워두면 NavigationBar 아래에서 이름으로 자동 탐색합니다.")]
    public Button durryNoteButton;
    public Button shopButton;
    public Button myPageButton;
    public Button menuButton;
    public bool autoConnectNavigationButtons = true;

    [Header("Big Note / Page Panels")]
    [Tooltip("하단바를 눌렀을 때 펼쳐질 큰 노트 전체 Root입니다. 예: WorldCanvas/BigNoteRoot 또는 WorldCanvas/Viewpoint")]
    public GameObject bigNoteRoot;
    public RectTransform bigNoteRect;
    public bool bigNoteStartsOpen = false;
    public DustinyPage startBigNotePage = DustinyPage.Note;
    public bool keepBigNoteInView = true;
    public Vector2 bigNoteAnchoredPosition = new Vector2(0f, -40f);
    public bool forceBigNoteCenterAnchor = true;
    public bool applyBigNoteSize = false;
    public Vector2 bigNoteSize = new Vector2(1100f, 800f);
    public bool hideDialogueWhenBigNoteOpens = true;
    public bool hideNavigationBarWhenBigNoteOpen = false;

    [Tooltip("NOTE 화면 패널입니다. 큰 노트 배경 안쪽의 내용 오브젝트만 넣어도 됩니다.")]
    public GameObject notePageObject;
    [Tooltip("SHOP 화면 패널입니다.")]
    public GameObject shopPageObject;
    [Tooltip("MY PAGE 화면 패널입니다.")]
    public GameObject myPageObject;
    [Tooltip("MENU 화면 패널입니다. 아직 없으면 비워둬도 됩니다.")]
    public GameObject menuPageObject;

    [Header("Big Note Side Tag Buttons")]
    [Tooltip("큰 노트 오른쪽의 X 태그 버튼입니다.")]
    public Button closePageButton;
    public Button noteTagButton;
    public Button shopTagButton;
    public Button myPageTagButton;
    public Button menuTagButton;
    public bool autoConnectBigNoteButtons = true;

    [Header("Big Note Tag Pop Out")]
    public RectTransform closeTagRect;
    public RectTransform noteTagRect;
    public RectTransform shopTagRect;
    public RectTransform myPageTagRect;
    public RectTransform menuTagRect;
    public bool popOutActiveTag = true;
    public bool closeTagAlwaysPopped = true;
    public float activeTagOffsetX = 35f;
    public float inactiveTagOffsetX = 0f;

    [Header("Start / Summon")]
    public bool summonDurryOnStart = true;
    public bool summonDurryOnMissionStart = true;
    public bool recenterOnTrigger = true;
    public bool detachDurryAndScanZoneFromParent = false;

    [Header("Input")]
    public bool allowControllerFallback = true;
    public bool bButtonCompletesMission = true;
    public bool triggerAlsoCompletesMission = false;

    [Header("Hand Tracking")]
    public bool useHandTracking = true;

    // OVRCameraRig 안의 LeftHandAnchor/RightHandAnchor 아래에 있는 OVRHand를 넣는다.
    // 비어 있어도 실행은 되지만, 손 제스처는 동작하지 않는다.
    public OVRHand leftHand;
    public OVRHand rightHand;

    // 정확한 손목 본이 있으면 넣는다. 비워두면 leftHand 아래에서 wrist 이름을 자동 탐색하고,
    // 못 찾으면 leftHand Transform을 임시 손목 기준으로 사용한다.
    public Transform leftWristTransform;

    [Header("Hand Gesture Mapping")]
    public bool rightIndexPinchSummonsDurry = true;       // 오른손 엄지+검지: 더리 부르기
    public bool rightMiddlePinchStartsMission = true;     // 오른손 엄지+중지: 미션 시작
    public bool rightRingPinchCompletesMission = true;    // 오른손 엄지+약지: 데모용 미션 완료

    [Header("Wrist Band Status Window")]
    public GameObject statusWindowObject;                 // 상태창 패널. 비우면 BosongText 또는 그 부모를 사용.
    public bool statusWindowStartsOpen = false;

    // 기본 방식: 오른손 검지 끝으로 왼쪽 손목 밴드를 터치하면 상태창을 열고 닫는다.
    public bool toggleStatusByRightIndexTouchingLeftWristBand = true;
    public Transform rightIndexTipTransform;              // 비워두면 OVRSkeleton에서 Hand_IndexTip을 자동 탐색.
    public Transform statusTouchTarget;                   // 비워두면 wristBandObject, 없으면 leftWristTransform을 터치 타깃으로 사용.
    public float wristBandTouchRadius = 0.065f;            // 손가락 끝과 밴드 중심 사이 거리. 0.05~0.08 추천.
    public float wristBandTouchHoldTime = 0.12f;           // 너무 스치기만 해도 열리는 것을 막는 시간.
    public float wristBandTouchCooldown = 0.7f;
    public bool requireRightIndexPinchWhileTouchingBand = false;

    // 예비 방식. true로 켜면 예전처럼 왼쪽 손목을 시야 안에 올렸을 때도 상태창을 열 수 있다.
    public bool toggleStatusWhenLeftWristRaised = false;
    public bool requireLeftIndexPinchToToggleStatus = false;

    // 왼손 손목이 이 영역 안에 들어오면 "손목을 올렸다"고 판단한다.
    // 기본 방식에서는 꺼져 있으므로 보통 건드리지 않아도 된다.
    public float wristRaiseHoldTime = 0.45f;
    public float wristToggleCooldown = 0.8f;
    public float wristViewMinX = -0.75f;
    public float wristViewMaxX = 0.35f;
    public float wristViewMinY = -0.75f;
    public float wristViewMaxY = 0.25f;
    public float wristViewMinZ = 0.15f;
    public float wristViewMaxZ = 1.15f;

    [Header("Wrist Band Visual")]
    public GameObject wristBandObject;                    // 선택사항. 손목에 붙일 밴드 오브젝트.
    public bool showWristBandWhenHandTracked = true;
    public Vector3 wristBandLocalPosition = Vector3.zero;
    public Vector3 wristBandLocalEuler = Vector3.zero;
    public Vector3 wristBandLocalScale = Vector3.one;

    [Header("Durry Fixed World Start")]
    public bool useCurrentSceneDurryPositionOnStart = false;
    public Vector3 durryStartWorldPosition = new Vector3(0f, -0.35f, 1.2f);
    public Vector3 durryStartWorldEuler = new Vector3(0f, 180f, 0f);
    public bool faceDurryToUserOnStart = true;

    [Header("Durry Summon Placement")]
    public float durryDistance = 2f;
    public float durrySideOffset = 0f;
    public float durryHeightOffset = -1f;

    // 더리가 뒤를 보면 180, 정면을 보면 0으로 조절.
    public float durryYawOffset = 180f;

    [Header("Durry Size")]
    public float durryRootScale = 1.0f;
    public float durryVisualScale = 10f;

    [Header("Text Layout")]
    public bool applyTextLayout = true;
    public float questTextY = 360f;
    public float bosongTextY = -360f;
    public float textWidth = 1500f;
    public float questTextHeight = 180f;
    public float bosongTextHeight = 140f;
    public float questFontSize = 44f;
    public float bosongFontSize = 38f;

    [Header("Visual Cleanup")]
    public bool removeTextShadow = true;
    public bool makeCanvasBackgroundTransparent = true;
    public bool disableUIShadowComponents = true;
    public bool disableDurryShadows = true;

    [Header("Scan Zone Placement")]
    public bool moveScanZoneWhenDurrySummoned = true;
    public float scanZoneDistance = 1.7f;
    public float scanZoneHeightOffset = -0.55f;
    public float scanZoneSize = 0.8f;

    [Header("Reward")]
    public int rewardBosongPower = 1;
    public bool changeDurryColorOnMissionComplete = false; // false면 약지 핀치로 미션 완료해도 더리 색은 그대로 유지.

    private int bosongPower = 0;
    private int step = 0;

    private enum OnboardingState
    {
        IntroSpeech,
        MissionDescription,
        NameQuestionSpeech,
        NamedDescription,
        FinalSpeech,
        Finished
    }

    private OnboardingState onboardingState = OnboardingState.IntroSpeech;
    private bool onboardingActive = false;
    private float lastDialogueAdvanceTime = -999f;

    private SpeechBubbleAutoSize speechAutoSize;
    private SpeechBubbleAutoSize descriptionAutoSize;

    private bool wasTriggerPressed = false;

    private bool wasRightIndexPinching = false;
    private bool wasRightMiddlePinching = false;
    private bool wasRightRingPinching = false;
    private bool wasLeftIndexPinching = false;

    private bool statusWindowOpen = false;
    private float wristRaiseTimer = 0f;
    private float lastStatusToggleTime = -999f;
    private bool wristToggleLockedUntilLeave = false;

    private float wristBandTouchTimer = 0f;
    private float lastWristBandTouchToggleTime = -999f;
    private bool wristBandTouchLockedUntilRelease = false;

    private DustinyPage currentBigNotePage = DustinyPage.None;
    private bool bigNoteOpen = false;
    private bool bigNoteTagBasePositionsSaved = false;
    private Vector2 closeTagBasePosition;
    private Vector2 noteTagBasePosition;
    private Vector2 shopTagBasePosition;
    private Vector2 myPageTagBasePosition;
    private Vector2 menuTagBasePosition;

    private Renderer[] durryRenderers;
    private Renderer scanZoneRenderer;
    private Material[] durryRuntimeMaterials;
    private Transform resolvedUIRoot;

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start - World Durry / View Locked UI");

        ResolveUIRoot();
        SetupDurry();
        SetupScanZone();
        ResolveDialogueReferences();
        ResolveNavigationReferences();
        ResolveNavigationButtons();
        ConnectNavigationButtonEvents();
        ResolveBigNoteReferences();
        ResolveBigNoteButtons();
        ConnectBigNoteButtonEvents();
        SetupWorldCanvas();
        SetupHandTrackingUI();

        SetNavigationBarVisible(navigationBarStartsVisible);
        if (bigNoteStartsOpen)
        {
            OpenBigNotePage(startBigNotePage);
        }
        else
        {
            SetBigNoteVisible(false);
        }
        PlaceDurryAtStartWorldPosition();

        if (summonDurryOnStart)
        {
            SummonDurryToUser();
        }

        UpdateViewLockedUI(true);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
        UpdateNavigationBarPlacement();
        UpdateBigNotePlacement();

        if (startOnboardingFlowOnStart)
        {
            BeginOnboardingFlow();
        }
        else
        {
            HideDialoguePanels();
            UpdateUI(
                "오른손 검지 핀치: 더리 부르기\n오른손 중지 핀치: 미션 시작\n오른손 검지로 왼쪽 손목 밴드 터치: 상태창",
                "보송력 0"
            );
        }
    }

    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        // UI만 매 프레임 사용자 시야 아래쪽에 고정한다.
        // 더리와 스캔존은 여기서 따라오지 않는다.
        UpdateViewLockedUI(false);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
        UpdateNavigationBarPlacement();
        UpdateBigNotePlacement();

        if (useHandTracking)
        {
            UpdateHandTrackingInput();
        }

        if (allowControllerFallback)
        {
            // Quest 오른손 A 버튼
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                OnPressA();
            }

            // Quest 오른손 B 버튼: 데모용 미션 완료
            if (bButtonCompletesMission && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                CompleteMission();
            }

            // Quest 오른손 검지 Trigger: 더리를 현재 시야 앞으로 소환한다.
            if (GetRightTriggerDown())
            {
                if (onboardingActive && pinchAdvancesDialogue)
                {
                    AdvanceOnboardingFlow();
                }
                else
                {
                    OnPressTrigger();
                }
            }
        }

        // UI가 카메라 자식이 아닐 때만 월드 캔버스를 사용자를 향해 돌린다.
        if (!lockUIToUserView)
        {
            FaceCanvasToUser();
        }
    }

    private void ResolveNavigationReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (navigationBarObject == null)
        {
            Transform foundNavigation = FindChildTransformContainsAll(searchRoot, "navigation", "bar");

            if (foundNavigation == null)
            {
                foundNavigation = FindChildTransformContains(searchRoot, "navbar");
            }

            if (foundNavigation == null)
            {
                foundNavigation = FindChildTransformContains(searchRoot, "nav");
            }

            // 현재 씬 스크린샷 기준 이름이 menuPanel이라서 예비로 찾는다.
            if (foundNavigation == null)
            {
                foundNavigation = FindChildTransformContains(searchRoot, "menupanel");
            }

            if (foundNavigation != null)
            {
                navigationBarObject = foundNavigation.gameObject;
            }
        }

        if (navigationBarRect == null && navigationBarObject != null)
        {
            navigationBarRect = navigationBarObject.GetComponent<RectTransform>();
        }
    }

    private RectTransform ResolveNavigationBarRect()
    {
        if (navigationBarRect != null)
        {
            return navigationBarRect;
        }

        ResolveNavigationReferences();
        return navigationBarRect;
    }

    private void UpdateNavigationBarPlacement()
    {
        if (!keepNavigationBarBelowView)
        {
            return;
        }

        RectTransform rect = ResolveNavigationBarRect();

        if (rect == null)
        {
            return;
        }

        if (forceNavigationBarCenterAnchor)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        rect.anchoredPosition = navigationBarAnchoredPosition;

        if (applyNavigationBarSize && navigationBarSize.x > 0f && navigationBarSize.y > 0f)
        {
            rect.sizeDelta = navigationBarSize;
        }
    }


    private void ResolveNavigationButtons()
    {
        Transform searchRoot = navigationBarObject != null
            ? navigationBarObject.transform
            : (worldCanvas != null ? worldCanvas.transform : transform);

        if (durryNoteButton == null)
        {
            durryNoteButton = FindButtonByKeywords(searchRoot, "durry", "note");

            // 이미지 파일명이 Dury Note처럼 r이 하나인 경우도 있어서 예비 검색.
            if (durryNoteButton == null)
            {
                durryNoteButton = FindButtonByKeywords(searchRoot, "dury", "note");
            }
        }

        if (shopButton == null)
        {
            shopButton = FindButtonByKeywords(searchRoot, "shop");
        }

        if (myPageButton == null)
        {
            myPageButton = FindButtonByKeywords(searchRoot, "my", "page");

            if (myPageButton == null)
            {
                myPageButton = FindButtonByKeywords(searchRoot, "mypage");
            }
        }

        if (menuButton == null)
        {
            menuButton = FindButtonByKeywords(searchRoot, "menu");
        }
    }

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

        return found.GetComponentInChildren<Button>(true);
    }

    private void ConnectNavigationButtonEvents()
    {
        if (!autoConnectNavigationButtons)
        {
            return;
        }

        ResolveNavigationButtons();

        ConnectButtonClick(durryNoteButton, OnDurryNoteButtonClicked);
        ConnectButtonClick(shopButton, OnShopButtonClicked);
        ConnectButtonClick(myPageButton, OnMyPageButtonClicked);
        ConnectButtonClick(menuButton, OnMenuButtonClicked);
    }

    private void ConnectButtonClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
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

    // 네비게이션 바 버튼 OnClick에 바로 연결할 수 있는 함수들.
    // 하단바 버튼을 누르거나 핀치하면 큰 노트 Root가 펼쳐지고, 해당 페이지와 태그가 활성화된다.
    public void OnDurryNoteButtonClicked()
    {
        OpenNotePage();
    }

    public void OnShopButtonClicked()
    {
        OpenShopPage();
    }

    public void OnMyPageButtonClicked()
    {
        OpenMyPage();
    }

    public void OnMenuButtonClicked()
    {
        OpenMenuPage();
    }

    public void OpenNotePage()
    {
        OpenBigNotePage(DustinyPage.Note);
    }

    public void OpenShopPage()
    {
        OpenBigNotePage(DustinyPage.Shop);
    }

    public void OpenMyPage()
    {
        OpenBigNotePage(DustinyPage.MyPage);
    }

    public void OpenMenuPage()
    {
        // 메뉴 페이지를 아직 만들지 않았다면 NOTE 페이지로 열어두고, 나중에 menuPageObject만 연결하면 된다.
        if (menuPageObject != null)
        {
            OpenBigNotePage(DustinyPage.Menu);
        }
        else
        {
            OpenBigNotePage(DustinyPage.Note);
        }
    }

    public void CloseBigNote()
    {
        SetBigNoteVisible(false);
        currentBigNotePage = DustinyPage.None;
        UpdateBigNoteTags();
    }

    private void ResolveBigNoteReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (bigNoteRoot == null)
        {
            Transform found = FindChildTransformContainsAll(searchRoot, "big", "note");

            if (found == null)
            {
                found = FindChildTransformContains(searchRoot, "viewpoint");
            }

            if (found == null)
            {
                found = FindChildTransformContainsAll(searchRoot, "page", "root");
            }

            if (found != null)
            {
                bigNoteRoot = found.gameObject;
            }
        }

        if (bigNoteRect == null && bigNoteRoot != null)
        {
            bigNoteRect = bigNoteRoot.GetComponent<RectTransform>();
        }

        Transform pageRoot = bigNoteRoot != null ? bigNoteRoot.transform : searchRoot;

        if (notePageObject == null)
        {
            Transform foundNote = FindChildTransformContainsAll(pageRoot, "note", "page");
            if (foundNote == null)
            {
                foundNote = FindChildTransformContains(pageRoot, "notepanel");
            }
            if (foundNote != null && (bigNoteRoot == null || foundNote != bigNoteRoot.transform))
            {
                notePageObject = foundNote.gameObject;
            }
        }

        if (shopPageObject == null)
        {
            Transform foundShop = FindChildTransformContainsAll(pageRoot, "shop", "page");
            if (foundShop == null)
            {
                foundShop = FindChildTransformContains(pageRoot, "shoppanel");
            }
            if (foundShop != null && (bigNoteRoot == null || foundShop != bigNoteRoot.transform))
            {
                shopPageObject = foundShop.gameObject;
            }
        }

        if (myPageObject == null)
        {
            Transform foundMyPage = FindChildTransformContainsAll(pageRoot, "my", "page");
            if (foundMyPage == null)
            {
                foundMyPage = FindChildTransformContains(pageRoot, "mypagepanel");
            }
            if (foundMyPage != null && (bigNoteRoot == null || foundMyPage != bigNoteRoot.transform))
            {
                myPageObject = foundMyPage.gameObject;
            }
        }

        if (menuPageObject == null)
        {
            Transform foundMenu = FindChildTransformContainsAll(pageRoot, "menu", "page");
            if (foundMenu == null)
            {
                foundMenu = FindChildTransformContains(pageRoot, "menupanel");
            }
            if (foundMenu != null && (bigNoteRoot == null || foundMenu != bigNoteRoot.transform) && foundMenu.gameObject != navigationBarObject)
            {
                menuPageObject = foundMenu.gameObject;
            }
        }
    }

    private void ResolveBigNoteButtons()
    {
        ResolveBigNoteReferences();

        Transform pageRoot = bigNoteRoot != null ? bigNoteRoot.transform : (worldCanvas != null ? worldCanvas.transform : transform);

        if (closePageButton == null)
        {
            closePageButton = FindButtonByKeywords(pageRoot, "close");
            if (closePageButton == null)
            {
                closePageButton = FindButtonByKeywords(pageRoot, "x");
            }
        }

        if (noteTagButton == null)
        {
            noteTagButton = FindButtonByKeywords(pageRoot, "note");
        }

        if (shopTagButton == null)
        {
            shopTagButton = FindButtonByKeywords(pageRoot, "shop");
        }

        if (myPageTagButton == null)
        {
            myPageTagButton = FindButtonByKeywords(pageRoot, "my", "page");
            if (myPageTagButton == null)
            {
                myPageTagButton = FindButtonByKeywords(pageRoot, "mypage");
            }
        }

        if (menuTagButton == null)
        {
            menuTagButton = FindButtonByKeywords(pageRoot, "menu");
        }

        if (closeTagRect == null && closePageButton != null) closeTagRect = closePageButton.GetComponent<RectTransform>();
        if (noteTagRect == null && noteTagButton != null) noteTagRect = noteTagButton.GetComponent<RectTransform>();
        if (shopTagRect == null && shopTagButton != null) shopTagRect = shopTagButton.GetComponent<RectTransform>();
        if (myPageTagRect == null && myPageTagButton != null) myPageTagRect = myPageTagButton.GetComponent<RectTransform>();
        if (menuTagRect == null && menuTagButton != null) menuTagRect = menuTagButton.GetComponent<RectTransform>();

        SaveBigNoteTagBasePositions();
    }

    private void ConnectBigNoteButtonEvents()
    {
        if (!autoConnectBigNoteButtons)
        {
            return;
        }

        ResolveBigNoteButtons();

        ConnectButtonClick(closePageButton, CloseBigNote);
        ConnectButtonClick(noteTagButton, OpenNotePage);
        ConnectButtonClick(shopTagButton, OpenShopPage);
        ConnectButtonClick(myPageTagButton, OpenMyPage);
        ConnectButtonClick(menuTagButton, OpenMenuPage);
    }

    private void UpdateBigNotePlacement()
    {
        if (!keepBigNoteInView)
        {
            return;
        }

        ResolveBigNoteReferences();

        if (bigNoteRect == null)
        {
            return;
        }

        if (forceBigNoteCenterAnchor)
        {
            bigNoteRect.anchorMin = new Vector2(0.5f, 0.5f);
            bigNoteRect.anchorMax = new Vector2(0.5f, 0.5f);
            bigNoteRect.pivot = new Vector2(0.5f, 0.5f);
        }

        bigNoteRect.anchoredPosition = bigNoteAnchoredPosition;

        if (applyBigNoteSize && bigNoteSize.x > 0f && bigNoteSize.y > 0f)
        {
            bigNoteRect.sizeDelta = bigNoteSize;
        }
    }

    private void SetBigNoteVisible(bool visible)
    {
        ResolveBigNoteReferences();

        bigNoteOpen = visible;

        if (bigNoteRoot != null)
        {
            bigNoteRoot.SetActive(visible);
        }

        if (hideNavigationBarWhenBigNoteOpen)
        {
            SetNavigationBarVisible(!visible && navigationBarStartsVisible);
        }
    }

    private void OpenBigNotePage(DustinyPage page)
    {
        if (page == DustinyPage.None)
        {
            CloseBigNote();
            return;
        }

        ResolveBigNoteReferences();
        ResolveBigNoteButtons();

        currentBigNotePage = page;
        SetBigNoteVisible(true);

        if (hideDialogueWhenBigNoteOpens)
        {
            HideDialoguePanels();
        }

        SetPageObjectVisible(notePageObject, page == DustinyPage.Note);
        SetPageObjectVisible(shopPageObject, page == DustinyPage.Shop);
        SetPageObjectVisible(myPageObject, page == DustinyPage.MyPage);
        SetPageObjectVisible(menuPageObject, page == DustinyPage.Menu);

        UpdateBigNoteTags();
        UpdateBigNotePlacement();

        Debug.Log($"Big note page opened: {page}");
    }

    private void SetPageObjectVisible(GameObject pageObject, bool visible)
    {
        if (pageObject != null)
        {
            pageObject.SetActive(visible);
        }
    }

    private void SaveBigNoteTagBasePositions()
    {
        if (bigNoteTagBasePositionsSaved)
        {
            return;
        }

        if (closeTagRect != null) closeTagBasePosition = closeTagRect.anchoredPosition;
        if (noteTagRect != null) noteTagBasePosition = noteTagRect.anchoredPosition;
        if (shopTagRect != null) shopTagBasePosition = shopTagRect.anchoredPosition;
        if (myPageTagRect != null) myPageTagBasePosition = myPageTagRect.anchoredPosition;
        if (menuTagRect != null) menuTagBasePosition = menuTagRect.anchoredPosition;

        bigNoteTagBasePositionsSaved = true;
    }

    private void UpdateBigNoteTags()
    {
        if (!popOutActiveTag)
        {
            return;
        }

        ResolveBigNoteButtons();
        SaveBigNoteTagBasePositions();

        bool isOpen = bigNoteOpen && bigNoteRoot != null && bigNoteRoot.activeSelf;

        SetTagPopped(closeTagRect, closeTagBasePosition, isOpen && closeTagAlwaysPopped);
        SetTagPopped(noteTagRect, noteTagBasePosition, isOpen && currentBigNotePage == DustinyPage.Note);
        SetTagPopped(shopTagRect, shopTagBasePosition, isOpen && currentBigNotePage == DustinyPage.Shop);
        SetTagPopped(myPageTagRect, myPageTagBasePosition, isOpen && currentBigNotePage == DustinyPage.MyPage);
        SetTagPopped(menuTagRect, menuTagBasePosition, isOpen && currentBigNotePage == DustinyPage.Menu);
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

    private void SetupHandTrackingUI()
    {
        statusWindowOpen = statusWindowStartsOpen;
        SetStatusWindowVisible(statusWindowOpen, true);

        UpdateWristBandVisual();
    }

    private void ResolveDialogueReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (speechBubbleObject == null)
        {
            Transform foundSpeech = FindChildTransformContainsAll(searchRoot, "speech", "bubble");
            if (foundSpeech == null)
            {
                foundSpeech = FindChildTransformContains(searchRoot, "speechbubble");
            }
            if (foundSpeech != null)
            {
                speechBubbleObject = foundSpeech.gameObject;
            }
        }

        if (descriptionObject == null)
        {
            Transform foundDescription = FindChildTransformContains(searchRoot, "description");
            if (foundDescription != null)
            {
                descriptionObject = foundDescription.gameObject;
            }
        }

        if (speechText == null && speechBubbleObject != null)
        {
            speechText = speechBubbleObject.GetComponentInChildren<TMP_Text>(true);
        }

        if (descriptionText == null && descriptionObject != null)
        {
            descriptionText = descriptionObject.GetComponentInChildren<TMP_Text>(true);
        }

        if (speechText == null && questText != null)
        {
            speechText = questText;
        }

        if (questText == null && speechText != null)
        {
            questText = speechText;
        }

        if (speechBubbleRect == null && speechBubbleObject != null)
        {
            speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
        }

        if (descriptionRect == null && descriptionObject != null)
        {
            descriptionRect = descriptionObject.GetComponent<RectTransform>();
        }

        speechAutoSize = speechBubbleObject != null ? speechBubbleObject.GetComponent<SpeechBubbleAutoSize>() : null;
        descriptionAutoSize = descriptionObject != null ? descriptionObject.GetComponent<SpeechBubbleAutoSize>() : null;

        if (speechAutoSize != null && speechText != null)
        {
            speechAutoSize.questText = speechText;
            if (speechAutoSize.textRect == null)
            {
                speechAutoSize.textRect = speechText.GetComponent<RectTransform>();
            }
            if (speechAutoSize.bubbleRect == null && speechBubbleRect != null)
            {
                speechAutoSize.bubbleRect = speechBubbleRect;
            }
        }

        if (descriptionAutoSize != null && descriptionText != null)
        {
            descriptionAutoSize.questText = descriptionText;
            if (descriptionAutoSize.textRect == null)
            {
                descriptionAutoSize.textRect = descriptionText.GetComponent<RectTransform>();
            }
            if (descriptionAutoSize.bubbleRect == null && descriptionRect != null)
            {
                descriptionAutoSize.bubbleRect = descriptionRect;
            }
        }

        if (durryNameInputObject == null && durryNameInputField != null)
        {
            durryNameInputObject = durryNameInputField.gameObject;
        }

        if (durryNameInputField != null)
        {
            durryNameInputField.text = "";
            durryNameInputField.onSubmit.RemoveListener(OnNameInputSubmitted);
            durryNameInputField.onSubmit.AddListener(OnNameInputSubmitted);
        }

        SetNameInputVisible(false);
    }

    private void BeginOnboardingFlow()
    {
        onboardingActive = true;
        onboardingState = OnboardingState.IntroSpeech;
        step = 0;
        durryName = string.IsNullOrWhiteSpace(durryName) ? defaultDurryName : durryName;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
        }

        ShowSpeech(introSpeech);
        UpdateBosongText();
    }

    private void AdvanceOnboardingFlow()
    {
        if (Time.time - lastDialogueAdvanceTime < dialogueAdvanceCooldown)
        {
            return;
        }
        lastDialogueAdvanceTime = Time.time;

        if (!onboardingActive)
        {
            return;
        }

        switch (onboardingState)
        {
            case OnboardingState.IntroSpeech:
                StartFirstMissionDescription();
                break;

            case OnboardingState.MissionDescription:
                // 미션 설명 상태에서는 핀치로 대사를 넘기지 않는다.
                // 실제 정리가 끝났다는 입력은 오른손 약지 핀치 또는 B 버튼으로 받는다.
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
        step = 1;

        if (summonDurryOnMissionStart)
        {
            SummonDurryToUser();
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
        }

        ShowDescription(firstMissionDescription);
        UpdateBosongText();
    }

    private void ShowNameQuestionSpeech()
    {
        onboardingState = OnboardingState.NameQuestionSpeech;
        ShowSpeech(nameQuestionSpeech);

        if (showNameInputWithNameQuestion)
        {
            SetNameInputVisible(true);
            if (durryNameInputField != null)
            {
                durryNameInputField.text = "";
                durryNameInputField.ActivateInputField();
            }
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

    // 나중에 무료 STT나 Meta Voice SDK를 붙이면, 인식 결과 문자열을 이 함수로 넘기면 된다.
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
        UpdateBosongText();
    }

    private void ShowFinalSpeech()
    {
        onboardingState = OnboardingState.FinalSpeech;
        ShowSpeech(string.Format(finalSpeechFormat, durryName));
    }

    private void FinishOnboardingFlow()
    {
        onboardingActive = false;
        onboardingState = OnboardingState.Finished;
        SetNameInputVisible(false);
        HideDialoguePanels();
        UpdateUI(
            "오른손 검지 핀치: 더리 부르기\n오른손 중지 핀치: 미션 시작\n오른손 검지로 왼쪽 손목 밴드 터치: 상태창",
            $"보송력 {bosongPower}"
        );
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

        UpdateSpeechBubblePlacement();
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
        else if (questText != null && speechText == null)
        {
            questText.text = message;
        }

        if (descriptionAutoSize != null)
        {
            descriptionAutoSize.SetText(message);
            descriptionAutoSize.ResizeBubble();
        }

        UpdateDescriptionPlacement();
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

    private void UpdateBosongText()
    {
        if (bosongText != null)
        {
            bosongText.text = $"보송력 {bosongPower}";
        }
    }

    private void UpdateHandTrackingInput()
    {
        UpdateWristBandVisual();

        bool rightIndexPinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Index, ref wasRightIndexPinching);
        bool rightMiddlePinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Middle, ref wasRightMiddlePinching);
        bool rightRingPinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Ring, ref wasRightRingPinching);

        if (pinchAdvancesDialogue && onboardingActive && rightIndexPinchDown)
        {
            AdvanceOnboardingFlow();
        }
        else if (rightIndexPinchSummonsDurry && rightIndexPinchDown)
        {
            OnPressTrigger();
        }

        if (rightMiddlePinchStartsMission && rightMiddlePinchDown)
        {
            OnPressA();
        }

        if (rightRingPinchCompletesMission && rightRingPinchDown)
        {
            CompleteMission();
        }

        if (toggleStatusByRightIndexTouchingLeftWristBand)
        {
            UpdateRightIndexTouchWristBandToggle();
        }

        bool leftWristInViewZone = IsLeftWristInViewZone();

        if (requireLeftIndexPinchToToggleStatus &&
            leftWristInViewZone &&
            GetHandPinchDown(leftHand, OVRHand.HandFinger.Index, ref wasLeftIndexPinching))
        {
            ToggleStatusWindow();
        }

        if (toggleStatusWhenLeftWristRaised && !requireLeftIndexPinchToToggleStatus)
        {
            UpdateLeftWristRaiseToggle(leftWristInViewZone);
        }
    }

    private void UpdateRightIndexTouchWristBandToggle()
    {
        bool isTouchingBand = IsRightIndexTouchingLeftWristBand();
        bool pinchConditionOk = true;

        if (requireRightIndexPinchWhileTouchingBand)
        {
            pinchConditionOk = IsHandTracked(rightHand) && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }

        if (!isTouchingBand || !pinchConditionOk)
        {
            wristBandTouchTimer = 0f;
            wristBandTouchLockedUntilRelease = false;
            return;
        }

        if (wristBandTouchLockedUntilRelease)
        {
            return;
        }

        wristBandTouchTimer += Time.deltaTime;

        bool holdEnough = wristBandTouchTimer >= wristBandTouchHoldTime;
        bool cooldownDone = Time.time - lastWristBandTouchToggleTime >= wristBandTouchCooldown;

        if (holdEnough && cooldownDone)
        {
            ToggleStatusWindow();
            lastWristBandTouchToggleTime = Time.time;
            wristBandTouchTimer = 0f;
            wristBandTouchLockedUntilRelease = true;
        }
    }

    private bool IsRightIndexTouchingLeftWristBand()
    {
        if (!IsHandTracked(leftHand) || !IsHandTracked(rightHand))
        {
            return false;
        }

        Transform fingerTip = ResolveRightIndexTipTransform();
        Transform target = ResolveStatusTouchTargetTransform();

        if (fingerTip == null || target == null)
        {
            return false;
        }

        float distance = Vector3.Distance(fingerTip.position, target.position);
        return distance <= wristBandTouchRadius;
    }

    private Transform ResolveRightIndexTipTransform()
    {
        if (rightIndexTipTransform != null)
        {
            return rightIndexTipTransform;
        }

        if (rightHand == null)
        {
            return null;
        }

        OVRSkeleton skeleton = rightHand.GetComponent<OVRSkeleton>();
        if (skeleton == null)
        {
            skeleton = rightHand.GetComponentInChildren<OVRSkeleton>(true);
        }

        if (skeleton != null && skeleton.Bones != null)
        {
            foreach (OVRBone bone in skeleton.Bones)
            {
                if (bone != null && bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
                {
                    rightIndexTipTransform = bone.Transform;
                    return rightIndexTipTransform;
                }
            }
        }

        Transform foundTip = FindChildTransformContainsAll(rightHand.transform, "index", "tip");
        if (foundTip != null)
        {
            rightIndexTipTransform = foundTip;
            return rightIndexTipTransform;
        }

        // 마지막 예비값. 손가락 끝이 아니라 손 기준점이라 정확도는 낮다.
        return rightHand.transform;
    }

    private Transform ResolveStatusTouchTargetTransform()
    {
        if (statusTouchTarget != null)
        {
            return statusTouchTarget;
        }

        if (wristBandObject != null)
        {
            statusTouchTarget = wristBandObject.transform;
            return statusTouchTarget;
        }

        statusTouchTarget = ResolveLeftWristTransform();
        return statusTouchTarget;
    }

    private bool GetHandPinchDown(OVRHand hand, OVRHand.HandFinger finger, ref bool wasPinching)
    {
        bool isPinching = IsHandTracked(hand) && hand.GetFingerIsPinching(finger);
        bool pinchDown = isPinching && !wasPinching;
        wasPinching = isPinching;
        return pinchDown;
    }

    private bool IsHandTracked(OVRHand hand)
    {
        return hand != null && hand.IsTracked && hand.IsDataValid;
    }

    private void UpdateLeftWristRaiseToggle(bool isInViewZone)
    {
        if (!isInViewZone)
        {
            wristRaiseTimer = 0f;
            wristToggleLockedUntilLeave = false;
            return;
        }

        if (wristToggleLockedUntilLeave)
        {
            return;
        }

        wristRaiseTimer += Time.deltaTime;

        bool holdEnough = wristRaiseTimer >= wristRaiseHoldTime;
        bool cooldownDone = Time.time - lastStatusToggleTime >= wristToggleCooldown;

        if (holdEnough && cooldownDone)
        {
            ToggleStatusWindow();
            lastStatusToggleTime = Time.time;
            wristRaiseTimer = 0f;
            wristToggleLockedUntilLeave = true;
        }
    }

    private bool IsLeftWristInViewZone()
    {
        if (!IsHandTracked(leftHand) || centerEyeAnchor == null)
        {
            return false;
        }

        Transform wrist = ResolveLeftWristTransform();
        if (wrist == null)
        {
            return false;
        }

        Vector3 local = centerEyeAnchor.InverseTransformPoint(wrist.position);

        return
            local.x >= wristViewMinX &&
            local.x <= wristViewMaxX &&
            local.y >= wristViewMinY &&
            local.y <= wristViewMaxY &&
            local.z >= wristViewMinZ &&
            local.z <= wristViewMaxZ;
    }

    private Transform ResolveLeftWristTransform()
    {
        if (leftWristTransform != null)
        {
            return leftWristTransform;
        }

        if (leftHand == null)
        {
            return null;
        }

        Transform foundWrist = FindChildTransformContains(leftHand.transform, "wrist");
        if (foundWrist != null)
        {
            leftWristTransform = foundWrist;
            return leftWristTransform;
        }

        return leftHand.transform;
    }

    private Transform FindChildTransformContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrEmpty(keyword))
        {
            return null;
        }

        string lowerKeyword = keyword.ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == null) continue;

            string lowerName = child.name.ToLowerInvariant();
            if (lowerName.Contains(lowerKeyword))
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
            if (child == null) continue;

            string lowerName = child.name.ToLowerInvariant();
            bool containsAll = true;

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrEmpty(keyword)) continue;

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

    private void UpdateWristBandVisual()
    {
        if (wristBandObject == null)
        {
            return;
        }

        bool leftTracked = IsHandTracked(leftHand);
        wristBandObject.SetActive(showWristBandWhenHandTracked ? leftTracked : true);

        if (!leftTracked)
        {
            return;
        }

        Transform wrist = ResolveLeftWristTransform();
        if (wrist == null)
        {
            return;
        }

        if (wristBandObject.transform.parent != wrist)
        {
            wristBandObject.transform.SetParent(wrist, false);
        }

        wristBandObject.transform.localPosition = wristBandLocalPosition;
        wristBandObject.transform.localRotation = Quaternion.Euler(wristBandLocalEuler);
        wristBandObject.transform.localScale = wristBandLocalScale;
    }

    public void ToggleStatusWindow()
    {
        SetStatusWindowVisible(!statusWindowOpen, false);
    }

    public void SetStatusWindowVisible(bool visible)
    {
        SetStatusWindowVisible(visible, false);
    }

    private void SetStatusWindowVisible(bool visible, bool isInitialSetup)
    {
        statusWindowOpen = visible;

        GameObject target = statusWindowObject;

        if (target == null && bosongText != null)
        {
            Transform parent = bosongText.transform.parent;

            // BosongText가 StatusPanel 같은 별도 패널 안에 있으면 패널을 켜고 끈다.
            // 단, 부모가 WorldCanvas나 UIRoot처럼 전체 UI 루트면 텍스트만 켜고 끈다.
            bool parentLooksLikeSmallPanel =
                parent != null &&
                worldCanvas != null &&
                parent != worldCanvas.transform &&
                parent.name.ToLowerInvariant().Contains("status");

            target = parentLooksLikeSmallPanel ? parent.gameObject : bosongText.gameObject;
        }

        if (target != null)
        {
            target.SetActive(visible);
        }

        if (!isInitialSetup)
        {
            Debug.Log(visible ? "Status window opened." : "Status window closed.");
        }
    }

    public void SummonDurryByHand()
    {
        OnPressTrigger();
    }

    public void StartMissionByHand()
    {
        OnPressA();
    }

    public void CompleteMissionByHand()
    {
        CompleteMission();
    }

    private void ResolveUIRoot()
    {
        if (worldCanvas == null)
        {
            resolvedUIRoot = uiRoot;
            return;
        }

        if (uiRoot != null)
        {
            resolvedUIRoot = uiRoot;
            return;
        }

        // WorldCanvas가 UIRoot 아래에 있으면 UIRoot를 움직이는 것이 안전하다.
        if (worldCanvas.transform.parent != null && worldCanvas.transform.parent != centerEyeAnchor)
        {
            resolvedUIRoot = worldCanvas.transform.parent;
        }
        else
        {
            resolvedUIRoot = worldCanvas.transform;
        }
    }

    private void UpdateViewLockedUI(bool force)
    {
        if (!lockUIToUserView) return;
        if (centerEyeAnchor == null) return;
        if (resolvedUIRoot == null) ResolveUIRoot();
        if (resolvedUIRoot == null) return;

        if (forceUIRootUnderCenterEye && resolvedUIRoot.parent != centerEyeAnchor)
        {
            resolvedUIRoot.SetParent(centerEyeAnchor, false);
        }

        resolvedUIRoot.localPosition = uiRootLocalPosition;
        resolvedUIRoot.localRotation = Quaternion.Euler(uiRootLocalEuler);
        resolvedUIRoot.localScale = Vector3.one * Mathf.Max(0.001f, uiRootLocalScale);
    }

    private void SetupDurry()
    {
        if (durryObject == null)
        {
            Debug.LogWarning("Durry Object가 연결되지 않았습니다. DurryRoot를 넣어주세요.");
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            // 더리는 카메라 자식으로 두면 고개를 돌릴 때 같이 따라오므로 월드 루트로 분리한다.
            durryObject.transform.SetParent(null, true);
        }

        durryObject.SetActive(true);

        if (durryVisual == null)
        {
            Transform foundVisual = durryObject.transform.Find("Durry Visual");

            if (foundVisual == null)
            {
                foundVisual = durryObject.transform.Find("DurryVisual");
            }

            if (foundVisual != null)
            {
                durryVisual = foundVisual;
            }
            else
            {
                Debug.LogWarning("Durry Visual을 찾지 못했습니다. Inspector에서 직접 연결해주세요.");
            }
        }

        ApplyDurryScale();

        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);

        if (durryRenderers == null || durryRenderers.Length == 0)
        {
            Debug.LogWarning("Durry Renderer를 찾지 못했습니다.");
            return;
        }

        durryRuntimeMaterials = new Material[durryRenderers.Length];

        for (int i = 0; i < durryRenderers.Length; i++)
        {
            Renderer renderer = durryRenderers[i];

            if (renderer == null) continue;

            if (disableDurryShadows)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Material runtimeMat = renderer.material;

            if (runtimeMat != null)
            {
                SetFloatIfHas(runtimeMat, "_ReceiveShadows", 0f);
            }

            durryRuntimeMaterials[i] = runtimeMat;
        }

        Debug.Log("Durry Renderer Count: " + durryRenderers.Length);
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
        if (durryObject == null) return;

        if (!useCurrentSceneDurryPositionOnStart)
        {
            durryObject.transform.position = durryStartWorldPosition;
            durryObject.transform.rotation = Quaternion.Euler(durryStartWorldEuler);
        }
        else if (faceDurryToUserOnStart && centerEyeAnchor != null)
        {
            durryObject.transform.rotation = GetRotationFacingUser(durryObject.transform.position, durryYawOffset);
        }

        ApplyDurryScale();
    }

    private void SetupScanZone()
    {
        if (scanZoneObject == null)
        {
            Debug.LogWarning("Scan Zone Object가 연결되지 않았습니다.");
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            scanZoneObject.transform.SetParent(null, true);
        }

        scanZoneRenderer = scanZoneObject.GetComponent<Renderer>();
        SetupScanZoneMaterial();

        scanZoneObject.SetActive(true);
        Debug.Log("ScanZone initial active: true");
    }

    private void SetupWorldCanvas()
    {
        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
            worldCanvas.renderMode = RenderMode.WorldSpace;

            Camera centerEyeCamera = centerEyeAnchor != null ? centerEyeAnchor.GetComponent<Camera>() : null;
            if (centerEyeCamera == null)
            {
                centerEyeCamera = Camera.main;
            }
            worldCanvas.worldCamera = centerEyeCamera;

            if (applyTextLayout)
            {
                ApplyTextLayout();
            }

            if (removeTextShadow)
            {
                RemoveTMPShadow(questText);
                RemoveTMPShadow(speechText);
                RemoveTMPShadow(descriptionText);
                RemoveTMPShadow(bosongText);
            }

            if (disableUIShadowComponents)
            {
                DisableUIShadowEffects();
            }

            if (makeCanvasBackgroundTransparent)
            {
                MakeCanvasBackgroundTransparent();
            }
        }
        else
        {
            Debug.LogWarning("World Canvas가 연결되지 않았습니다.");
        }
    }

    private void SummonDurryToUser()
    {
        if (centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        Vector3 forward = GetFlatForward();
        Vector3 right = centerEyeAnchor.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        right.Normalize();

        Vector3 durryPos = headPos + forward * durryDistance + right * durrySideOffset + Vector3.up * durryHeightOffset;

        if (durryObject != null)
        {
            durryObject.transform.position = durryPos;
            durryObject.transform.rotation = GetRotationFacingUser(durryPos, durryYawOffset);
            ApplyDurryScale();
        }

        if (moveScanZoneWhenDurrySummoned)
        {
            PlaceScanZoneInFrontOfUser(forward);
        }
    }

    private void PlaceScanZoneInFrontOfUser(Vector3 forward)
    {
        if (scanZoneObject == null || centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        Vector3 scanZonePos = headPos + forward * scanZoneDistance + Vector3.up * scanZoneHeightOffset;

        scanZoneObject.transform.position = scanZonePos;
        scanZoneObject.transform.rotation = Quaternion.LookRotation(Vector3.up, forward);
        scanZoneObject.transform.localScale = Vector3.one * scanZoneSize;
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

    private void ApplyTextLayout()
    {
        // 말풍선/디스크립션 크기와 내부 텍스트 위치는 SpeechBubbleAutoSize가 담당한다.
        // 여기서는 상태 텍스트처럼 별도 패널에 있는 텍스트만 정리한다.
        ApplyTextRect(bosongText, bosongTextY, bosongTextHeight, bosongFontSize);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
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

        bubble.anchoredPosition = speechBubbleAnchoredPosition;
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

        rect.anchoredPosition = descriptionAnchoredPosition;
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
            if (descriptionRect != null)
            {
                return descriptionRect;
            }
        }

        if (descriptionText == null)
        {
            return null;
        }

        SpeechBubbleAutoSize autoSize = descriptionText.GetComponentInParent<SpeechBubbleAutoSize>(true);
        if (autoSize != null && autoSize.bubbleRect != null)
        {
            descriptionRect = autoSize.bubbleRect;
            return descriptionRect;
        }

        Transform current = descriptionText.transform.parent;
        while (current != null)
        {
            if (worldCanvas != null && current == worldCanvas.transform)
            {
                break;
            }

            RectTransform rect = current as RectTransform;
            if (rect != null)
            {
                string lowerName = current.name.ToLowerInvariant();
                bool looksLikeDescription =
                    lowerName.Contains("description") ||
                    lowerName.Contains("mission") ||
                    lowerName.Contains("bubble") ||
                    lowerName.Contains("panel") ||
                    current.GetComponent<Image>() != null;

                if (looksLikeDescription)
                {
                    descriptionRect = rect;
                    return descriptionRect;
                }
            }

            current = current.parent;
        }

        if (descriptionText.transform.parent is RectTransform parentRect)
        {
            descriptionRect = parentRect;
        }

        return descriptionRect;
    }

    private RectTransform ResolveSpeechBubbleRect()
    {
        if (speechBubbleRect != null)
        {
            return speechBubbleRect;
        }

        TMP_Text targetSpeechText = speechText != null ? speechText : questText;

        if (targetSpeechText == null)
        {
            return null;
        }

        SpeechBubbleAutoSize autoSize = targetSpeechText.GetComponentInParent<SpeechBubbleAutoSize>(true);
        if (autoSize != null && autoSize.bubbleRect != null)
        {
            speechBubbleRect = autoSize.bubbleRect;
            return speechBubbleRect;
        }

        Transform current = targetSpeechText.transform.parent;

        while (current != null)
        {
            if (worldCanvas != null && current == worldCanvas.transform)
            {
                break;
            }

            RectTransform rect = current as RectTransform;
            if (rect != null)
            {
                string lowerName = current.name.ToLowerInvariant();
                bool looksLikeBubble =
                    lowerName.Contains("bubble") ||
                    lowerName.Contains("speech") ||
                    lowerName.Contains("말풍선") ||
                    current.GetComponent<Image>() != null;

                if (looksLikeBubble)
                {
                    speechBubbleRect = rect;
                    return speechBubbleRect;
                }
            }

            current = current.parent;
        }

        if (targetSpeechText.transform.parent is RectTransform parentRect)
        {
            speechBubbleRect = parentRect;
        }

        return speechBubbleRect;
    }

    private void ApplyTextRect(TMP_Text text, float y, float height, float fontSize)
    {
        if (text == null) return;

        RectTransform rect = text.GetComponent<RectTransform>();

        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(textWidth, height);
        }

        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;

        if (fontSize > 0f)
        {
            text.fontSize = fontSize;
        }
    }

    private void RemoveTMPShadow(TMP_Text text)
    {
        if (text == null) return;
        if (text.fontMaterial == null) return;

        Material mat = new Material(text.fontMaterial);
        mat.name = text.name + "_NoShadow_Runtime";

        mat.DisableKeyword("UNDERLAY_ON");
        mat.DisableKeyword("UNDERLAY_INNER");

        SetFloatIfHas(mat, "_OutlineWidth", 0f);
        SetFloatIfHas(mat, "_OutlineSoftness", 0f);
        SetFloatIfHas(mat, "_FaceDilate", 0f);
        SetFloatIfHas(mat, "_UnderlayOffsetX", 0f);
        SetFloatIfHas(mat, "_UnderlayOffsetY", 0f);
        SetFloatIfHas(mat, "_UnderlayDilate", 0f);
        SetFloatIfHas(mat, "_UnderlaySoftness", 0f);
        SetColorIfHas(mat, "_UnderlayColor", new Color(0f, 0f, 0f, 0f));
        SetColorIfHas(mat, "_OutlineColor", new Color(0f, 0f, 0f, 0f));

        text.fontMaterial = mat;
    }

    private void DisableUIShadowEffects()
    {
        if (worldCanvas == null) return;

        Shadow[] shadows = worldCanvas.GetComponentsInChildren<Shadow>(true);

        foreach (Shadow shadow in shadows)
        {
            if (shadow != null)
            {
                shadow.enabled = false;
            }
        }
    }

    private void MakeCanvasBackgroundTransparent()
    {
        if (worldCanvas == null) return;

        Image[] images = worldCanvas.GetComponentsInChildren<Image>(true);

        foreach (Image image in images)
        {
            if (image == null) continue;

            // 네비게이션 바는 항상 보여야 하므로 배경 투명화 대상에서 제외한다.
            if (navigationBarObject != null &&
                (image.transform == navigationBarObject.transform || image.transform.IsChildOf(navigationBarObject.transform)))
            {
                continue;
            }

            string objectName = image.gameObject.name.ToLowerInvariant();

            bool looksLikeBackground =
                objectName.Contains("background") ||
                objectName.Contains("bg") ||
                objectName.Contains("panel") ||
                objectName.Contains("shadow") ||
                objectName.Contains("dim") ||
                objectName.Contains("black");

            if (!looksLikeBackground) continue;

            Color color = image.color;
            color.a = 0f;
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private void SetupScanZoneMaterial()
    {
        if (scanZoneRenderer == null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            Debug.LogWarning("Unlit Shader를 찾지 못했습니다. 기존 Material을 사용합니다.");
            return;
        }

        Material mat = new Material(shader);
        mat.name = "M_ScanZone_SoftBlue_Runtime";

        Color scanColor = new Color(0.45f, 0.85f, 1f, 0.55f);

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", scanColor);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", scanColor);
        }

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
        }

        if (mat.HasProperty("_Blend"))
        {
            mat.SetFloat("_Blend", 0f);
        }

        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (mat.HasProperty("_DstBlend"))
        {
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (mat.HasProperty("_ZWrite"))
        {
            mat.SetFloat("_ZWrite", 0f);
        }

        if (mat.HasProperty("_Cull"))
        {
            mat.SetFloat("_Cull", 0f);
        }

        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        scanZoneRenderer.material = mat;
        scanZoneRenderer.enabled = true;
    }

    private void OnPressA()
    {
        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        Debug.Log("Mission start requested");

        step = 1;

        if (summonDurryOnMissionStart)
        {
            SummonDurryToUser();
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
            Debug.Log("ScanZone SetActive(true)");
        }

        ShowDescription(
            "미션 시작!\n더리 주변의 어질러진 물건 3개를 정리해줘.\n완료 테스트: 오른손 약지 핀치"
        );
        UpdateBosongText();
    }

    private void OnPressTrigger()
    {
        Debug.Log("Summon Durry requested");

        if (recenterOnTrigger)
        {
            SummonDurryToUser();
        }

        if (triggerAlsoCompletesMission)
        {
            CompleteMission();
        }
    }

    private void CompleteMission()
    {
        Debug.Log("CompleteMission called");

        if (step != 1)
        {
            Debug.Log("Mission complete ignored. Mission has not started yet.");
            return;
        }

        step = 2;
        bosongPower += rewardBosongPower;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
            Debug.Log("ScanZone SetActive(false)");
        }

        if (changeDurryColorOnMissionComplete)
        {
            RecoverDurry();
        }

        UpdateBosongText();

        if (onboardingActive && onboardingState == OnboardingState.MissionDescription)
        {
            ShowNameQuestionSpeech();
        }
        else
        {
            ShowDescription($"미션 완료!\n보송력 +{rewardBosongPower}");
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

    private void RecoverDurry()
    {
        if (durryRuntimeMaterials == null || durryRuntimeMaterials.Length == 0)
        {
            return;
        }

        float t = Mathf.Clamp01(bosongPower / 100f);

        Color dustyColor = new Color(0.55f, 0.52f, 0.48f, 1f);
        Color cleanColor = new Color(1f, 0.94f, 0.86f, 1f);

        Color currentColor = Color.Lerp(dustyColor, cleanColor, t);

        foreach (Material mat in durryRuntimeMaterials)
        {
            if (mat == null) continue;

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", currentColor);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", currentColor);
            }
        }
    }

    private void UpdateUI(string questMessage, string bosongMessage)
    {
        if (descriptionText != null)
        {
            descriptionText.text = questMessage;
        }
        else if (questText != null)
        {
            questText.text = questMessage;
        }

        if (bosongText != null)
        {
            bosongText.text = bosongMessage;
        }

        if (descriptionAutoSize != null && descriptionText != null)
        {
            descriptionAutoSize.SetText(questMessage);
        }

        if (applyTextLayout)
        {
            ApplyTextLayout();
        }
    }

    private void FaceCanvasToUser()
    {
        if (worldCanvas == null || centerEyeAnchor == null) return;

        Vector3 direction = worldCanvas.transform.position - centerEyeAnchor.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            worldCanvas.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private void SetFloatIfHas(Material mat, string propertyName, float value)
    {
        if (mat != null && mat.HasProperty(propertyName))
        {
            mat.SetFloat(propertyName, value);
        }
    }

    private void SetColorIfHas(Material mat, string propertyName, Color value)
    {
        if (mat != null && mat.HasProperty(propertyName))
        {
            mat.SetColor(propertyName, value);
        }
    }
}
