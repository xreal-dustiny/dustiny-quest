using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DustinyDemoFlow : MonoBehaviour
{
    public enum DustinyPage { None, Note, Shop, MyPage, Menu }

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

    [Header("Texts")]
    [Tooltip("기존 호환용입니다. 가능하면 Speech Text / Description Text를 따로 연결해주세요.")]
    public TMP_Text questText;
    public TMP_Text bosongText;

    [Header("Dialogue / Description")]
    public bool startOnboardingFlowOnStart = true;
    public bool pinchAdvancesDialogue = true;
    public float dialogueAdvanceCooldown = 0.25f;

    public GameObject speechBubbleObject;
    public TMP_Text speechText;
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
    public RectTransform speechBubbleRect;
    public bool keepSpeechBubbleBelowView = false;
    public Vector2 speechBubbleAnchoredPosition = new Vector2(0f, -620f);
    public bool forceSpeechBubbleCenterAnchor = true;

    [Header("Description Placement")]
    public RectTransform descriptionRect;
    public bool keepDescriptionBelowView = true;
    public Vector2 descriptionAnchoredPosition = new Vector2(0f, -620f);
    public bool forceDescriptionCenterAnchor = true;

    [Header("Dialogue Safe Bounds")]
    public bool clampDialogueAboveWaistNav = true;
    public float speechBubbleMinAnchoredY = -620f;
    public float descriptionMinAnchoredY = -620f;

    [Header("Navigation Bar - ISDK")]
    [Tooltip("WaistNavCanvas 아래의 NavigationBar 오브젝트를 연결하세요. 위치/회전/잡기는 DustinyWaistNavFollow와 Interaction SDK가 담당합니다.")]
    public GameObject navigationBarObject;
    public RectTransform navigationBarRect;
    public bool navigationBarStartsVisible = true;
    public bool keepNavigationBarRectCentered = true;
    public bool applyNavigationBarSize = false;
    public Vector2 navigationBarSize = new Vector2(900f, 170f);

    [Header("Navigation Buttons")]
    public Button durryNoteButton;
    public Button shopButton;
    public Button myPageButton;
    public Button menuButton;
    public bool autoConnectNavigationButtons = true;

    [Header("Big Note / Page Panels")]
    public GameObject bigNoteRoot;
    public RectTransform bigNoteRect;
    public bool bigNoteStartsOpen = false;
    public DustinyPage startBigNotePage = DustinyPage.Note;
    public bool keepBigNoteInView = true;
    public Vector2 bigNoteAnchoredPosition = new Vector2(0f, -40f);
    public bool forceBigNoteCenterAnchor = true;
    public bool applyBigNoteSize = true;
    public Vector2 bigNoteSize = new Vector2(1900f, 1150f);

    public GameObject viewpointBackgroundObject;
    public bool forceBigNoteChildrenToFillRoot = true;
    public bool bringBigNoteToFrontWhenOpen = true;
    public bool hideDialogueWhenBigNoteOpens = true;
    public bool hideNavigationBarWhenBigNoteOpen = false;

    public GameObject notePageObject;
    public GameObject shopPageObject;
    public GameObject myPageObject;
    public GameObject menuPageObject;

    [Header("Big Note Side Tag Buttons")]
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
    public float activeTagOffsetX = 14f;
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

    [Header("Hand Tracking Gestures")]
    [Tooltip("하단바 UI 입력은 Interaction SDK가 담당합니다. 이 값은 더리 대사/미션 제스처만 담당합니다.")]
    public bool useHandTrackingGestures = true;
    public OVRHand rightHand;
    public bool autoFindRightOVRHandIfMissing = true;

    [Tooltip("검지 핀치는 온보딩 대사를 넘기는 데 사용합니다. 하단바 선택도 같은 검지 핀치라서, 온보딩이 끝난 뒤 더리 소환은 기본적으로 꺼둡니다.")]
    public bool rightIndexPinchSummonsDurry = false;
    public bool rightMiddlePinchStartsMission = true;
    public bool rightRingPinchCompletesMission = true;

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
    public float durryRootScale = 1.0f;
    public float durryVisualScale = 10f;

    [Header("Text Layout")]
    public bool applyTextLayout = true;
    public float bosongTextY = -360f;
    public float textWidth = 1500f;
    public float bosongTextHeight = 140f;
    public float bosongFontSize = 38f;

    [Header("Visual Cleanup")]
    public bool removeTextShadow = true;
    public bool disableUIShadowComponents = true;
    public bool disableDurryShadows = true;

    [Tooltip("필요할 때만 켜세요. NOTE/SHOP 배경 이미지까지 투명해질 수 있어서 기본값은 꺼둡니다.")]
    public bool makeOnlyBlackDimUITransparent = false;

    [Header("Scan Zone Placement")]
    public bool moveScanZoneWhenDurrySummoned = true;
    public float scanZoneDistance = 1.7f;
    public float scanZoneHeightOffset = -0.55f;
    public float scanZoneSize = 0.8f;

    [Header("Reward")]
    public int rewardBosongPower = 1;
    public bool changeDurryColorOnMissionComplete = false;

    private int bosongPower = 0;
    private int step = 0;

    private enum OnboardingState { IntroSpeech, MissionDescription, NameQuestionSpeech, NamedDescription, FinalSpeech, Finished }

    private OnboardingState onboardingState = OnboardingState.IntroSpeech;
    private bool onboardingActive = false;
    private float lastDialogueAdvanceTime = -999f;

    private SpeechBubbleAutoSize speechAutoSize;
    private SpeechBubbleAutoSize descriptionAutoSize;

    private bool wasTriggerPressed = false;
    private bool wasRightIndexPinching = false;
    private bool wasRightMiddlePinching = false;
    private bool wasRightRingPinching = false;

    private DustinyPage currentBigNotePage = DustinyPage.None;
    private bool bigNoteOpen = false;
    private bool bigNoteTagBasePositionsSaved = false;
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
        rightIndexPinchSummonsDurry = false;
    }

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start - ISDK optimized");

        ResolveRightHandIfNeeded();
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

        SetNavigationBarVisible(navigationBarStartsVisible);
        if (bigNoteStartsOpen) OpenBigNotePage(startBigNotePage);
        else SetBigNoteVisible(false);

        PlaceDurryAtStartWorldPosition();

        if (summonDurryOnStart) SummonDurryToUser();

        UpdateViewLockedUI(true);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
        UpdateNavigationBarRect();
        UpdateBigNotePlacement();

        if (startOnboardingFlowOnStart) BeginOnboardingFlow();
        else
        {
            HideDialoguePanels();
            UpdateUI("하단바: Interaction SDK로 터치/레이 핀치 선택\n오른손 중지 핀치: 미션 시작\n오른손 약지 핀치: 미션 완료", "보송력 0");
        }
    }

    private void Update()
    {
        if (centerEyeAnchor == null) return;

        UpdateViewLockedUI(false);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
        UpdateNavigationBarRect();
        UpdateBigNotePlacement();

        if (useHandTrackingGestures) UpdateHandGestureInput();

        if (allowControllerFallback)
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) OnPressA();
            if (bButtonCompletesMission && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) CompleteMission();

            if (GetRightTriggerDown())
            {
                if (onboardingActive && pinchAdvancesDialogue) AdvanceOnboardingFlow();
                else OnPressTrigger();
            }
        }

        if (!lockUIToUserView) FaceCanvasToUser();
    }

    private void ResolveRightHandIfNeeded()
    {
        if (!autoFindRightOVRHandIfMissing || rightHand != null) return;

        OVRHand[] hands = FindObjectsOfType<OVRHand>(true);
        foreach (OVRHand hand in hands)
        {
            if (hand == null) continue;
            string name = hand.gameObject.name.ToLowerInvariant();
            string parent = hand.transform.parent != null ? hand.transform.parent.name.ToLowerInvariant() : "";
            if ((name + " " + parent).Contains("right"))
            {
                rightHand = hand;
                return;
            }
        }
    }

    private void UpdateHandGestureInput()
    {
        ResolveRightHandIfNeeded();
        if (!IsHandTracked(rightHand)) return;

        bool rightIndexPinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Index, ref wasRightIndexPinching);
        bool rightMiddlePinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Middle, ref wasRightMiddlePinching);
        bool rightRingPinchDown = GetHandPinchDown(rightHand, OVRHand.HandFinger.Ring, ref wasRightRingPinching);

        if (rightIndexPinchDown)
        {
            if (pinchAdvancesDialogue && onboardingActive) AdvanceOnboardingFlow();
            else if (rightIndexPinchSummonsDurry) OnPressTrigger();
        }

        if (rightMiddlePinchStartsMission && rightMiddlePinchDown) OnPressA();
        if (rightRingPinchCompletesMission && rightRingPinchDown) CompleteMission();
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

    private void ResolveNavigationReferences()
    {
        if (navigationBarObject == null)
        {
            Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
            Transform found = FindChildTransformContainsAll(searchRoot, "navigation", "bar") ??
                              FindChildTransformContains(searchRoot, "navbar") ??
                              FindChildTransformContains(searchRoot, "nav");
            if (found != null) navigationBarObject = found.gameObject;
        }

        if (navigationBarRect == null && navigationBarObject != null) navigationBarRect = navigationBarObject.GetComponent<RectTransform>();
    }

    private void UpdateNavigationBarRect()
    {
        ResolveNavigationReferences();
        if (navigationBarRect == null) return;

        if (keepNavigationBarRectCentered)
        {
            navigationBarRect.anchorMin = new Vector2(0.5f, 0.5f);
            navigationBarRect.anchorMax = new Vector2(0.5f, 0.5f);
            navigationBarRect.pivot = new Vector2(0.5f, 0.5f);
            navigationBarRect.anchoredPosition = Vector2.zero;
            navigationBarRect.localEulerAngles = Vector3.zero;
        }

        if (applyNavigationBarSize && navigationBarSize.x > 0f && navigationBarSize.y > 0f) navigationBarRect.sizeDelta = navigationBarSize;
    }

    public void SetNavigationBarVisible(bool visible)
    {
        ResolveNavigationReferences();
        if (navigationBarObject != null) navigationBarObject.SetActive(visible);
    }

    public void ShowNavigationBar() => SetNavigationBarVisible(true);
    public void HideNavigationBar() => SetNavigationBarVisible(false);

    public void ToggleNavigationBar()
    {
        ResolveNavigationReferences();
        if (navigationBarObject != null) navigationBarObject.SetActive(!navigationBarObject.activeSelf);
    }

    private void ResolveNavigationButtons()
    {
        Transform searchRoot = navigationBarObject != null ? navigationBarObject.transform : (worldCanvas != null ? worldCanvas.transform : transform);
        if (durryNoteButton == null) durryNoteButton = FindButtonByKeywords(searchRoot, "durry", "note") ?? FindButtonByKeywords(searchRoot, "dury", "note");
        if (shopButton == null) shopButton = FindButtonByKeywords(searchRoot, "shop");
        if (myPageButton == null) myPageButton = FindButtonByKeywords(searchRoot, "my", "page") ?? FindButtonByKeywords(searchRoot, "mypage");
        if (menuButton == null) menuButton = FindButtonByKeywords(searchRoot, "menu");
    }

    private void ConnectNavigationButtonEvents()
    {
        if (!autoConnectNavigationButtons) return;
        ResolveNavigationButtons();
        ConnectButtonClick(durryNoteButton, OnDurryNoteButtonClicked);
        ConnectButtonClick(shopButton, OnShopButtonClicked);
        ConnectButtonClick(myPageButton, OnMyPageButtonClicked);
        ConnectButtonClick(menuButton, OnMenuButtonClicked);
    }

    public void OnDurryNoteButtonClicked() => OpenNotePage();
    public void OnShopButtonClicked() => OpenShopPage();
    public void OnMyPageButtonClicked() => OpenMyPage();
    public void OnMenuButtonClicked() => OpenMenuPage();

    public void OpenNotePage() => OpenBigNotePage(DustinyPage.Note);
    public void OpenShopPage() => OpenBigNotePage(DustinyPage.Shop);
    public void OpenMyPage() => OpenBigNotePage(DustinyPage.MyPage);
    public void OpenMenuPage() => OpenBigNotePage(menuPageObject != null ? DustinyPage.Menu : DustinyPage.Note);

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
            Transform found = FindChildTransformContainsAll(searchRoot, "big", "note") ??
                              FindChildTransformContains(searchRoot, "viewpoint") ??
                              FindChildTransformContainsAll(searchRoot, "page", "root");
            if (found != null) bigNoteRoot = found.gameObject;
        }

        if (bigNoteRect == null && bigNoteRoot != null) bigNoteRect = bigNoteRoot.GetComponent<RectTransform>();

        Transform pageRoot = bigNoteRoot != null ? bigNoteRoot.transform : searchRoot;

        if (viewpointBackgroundObject == null && bigNoteRoot != null)
        {
            viewpointBackgroundObject = GetGameObjectFromTransform(FindChildTransformContainsAll(bigNoteRoot.transform, "viewpoint", "background")) ??
                                       GetGameObjectFromTransform(FindChildTransformContainsAll(bigNoteRoot.transform, "note", "background")) ??
                                       GetGameObjectFromTransform(FindChildTransformContains(bigNoteRoot.transform, "background"));
        }

        if (notePageObject == null) notePageObject = GetPageObject(pageRoot, "note");
        if (shopPageObject == null) shopPageObject = GetPageObject(pageRoot, "shop");
        if (myPageObject == null) myPageObject = GetGameObjectFromTransform(FindChildTransformContainsAll(pageRoot, "my", "page")) ?? GetGameObjectFromTransform(FindChildTransformContains(pageRoot, "mypagepanel"));
        if (menuPageObject == null)
        {
            GameObject foundMenu = GetPageObject(pageRoot, "menu");
            if (foundMenu != navigationBarObject) menuPageObject = foundMenu;
        }
    }

    private GameObject GetPageObject(Transform root, string pageName)
    {
        Transform found = FindChildTransformContainsAll(root, pageName, "page") ?? FindChildTransformContains(root, pageName + "panel");
        return GetGameObjectFromTransform(found);
    }

    private GameObject GetGameObjectFromTransform(Transform target)
    {
        if (target == null) return null;
        if (bigNoteRoot != null && target == bigNoteRoot.transform) return null;
        return target.gameObject;
    }

    private void ResolveBigNoteButtons()
    {
        ResolveBigNoteReferences();
        Transform pageRoot = bigNoteRoot != null ? bigNoteRoot.transform : (worldCanvas != null ? worldCanvas.transform : transform);

        if (closePageButton == null) closePageButton = FindButtonByKeywords(pageRoot, "close") ?? FindButtonByKeywords(pageRoot, "x");
        if (noteTagButton == null) noteTagButton = FindButtonByKeywords(pageRoot, "note");
        if (shopTagButton == null) shopTagButton = FindButtonByKeywords(pageRoot, "shop");
        if (myPageTagButton == null) myPageTagButton = FindButtonByKeywords(pageRoot, "my", "page") ?? FindButtonByKeywords(pageRoot, "mypage");
        if (menuTagButton == null) menuTagButton = FindButtonByKeywords(pageRoot, "menu");

        if (closeTagRect == null && closePageButton != null) closeTagRect = closePageButton.GetComponent<RectTransform>();
        if (noteTagRect == null && noteTagButton != null) noteTagRect = noteTagButton.GetComponent<RectTransform>();
        if (shopTagRect == null && shopTagButton != null) shopTagRect = shopTagButton.GetComponent<RectTransform>();
        if (myPageTagRect == null && myPageTagButton != null) myPageTagRect = myPageTagButton.GetComponent<RectTransform>();
        if (menuTagRect == null && menuTagButton != null) menuTagRect = menuTagButton.GetComponent<RectTransform>();

        SaveBigNoteTagBasePositions();
    }

    private void ConnectBigNoteButtonEvents()
    {
        if (!autoConnectBigNoteButtons) return;
        ResolveBigNoteButtons();
        ConnectButtonClick(closePageButton, CloseBigNote);
        ConnectButtonClick(noteTagButton, OpenNotePage);
        ConnectButtonClick(shopTagButton, OpenShopPage);
        ConnectButtonClick(myPageTagButton, OpenMyPage);
        ConnectButtonClick(menuTagButton, OpenMenuPage);
    }

    private void ConnectButtonClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null) return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private Button FindButtonByKeywords(Transform root, params string[] keywords)
    {
        Transform found = FindChildTransformContainsAll(root, keywords);
        if (found == null) return null;
        Button directButton = found.GetComponent<Button>();
        return directButton != null ? directButton : found.GetComponentInChildren<Button>(true);
    }

    private void UpdateBigNotePlacement()
    {
        if (!keepBigNoteInView) return;
        ResolveBigNoteReferences();
        if (bigNoteRect == null) return;

        if (forceBigNoteCenterAnchor)
        {
            bigNoteRect.anchorMin = new Vector2(0.5f, 0.5f);
            bigNoteRect.anchorMax = new Vector2(0.5f, 0.5f);
            bigNoteRect.pivot = new Vector2(0.5f, 0.5f);
        }

        bigNoteRect.anchoredPosition = bigNoteAnchoredPosition;
        if (applyBigNoteSize && bigNoteSize.x > 0f && bigNoteSize.y > 0f) bigNoteRect.sizeDelta = bigNoteSize;

        if (forceBigNoteChildrenToFillRoot)
        {
            StretchChildToFill(viewpointBackgroundObject);
            StretchChildToFill(notePageObject);
            StretchChildToFill(shopPageObject);
            StretchChildToFill(myPageObject);
            StretchChildToFill(menuPageObject);
        }
    }

    private void StretchChildToFill(GameObject target)
    {
        if (target == null) return;
        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localEulerAngles = Vector3.zero;
    }

    private void SetBigNoteVisible(bool visible)
    {
        ResolveBigNoteReferences();
        bigNoteOpen = visible;
        if (bigNoteRoot != null) bigNoteRoot.SetActive(visible);
        if (hideNavigationBarWhenBigNoteOpen) SetNavigationBarVisible(!visible && navigationBarStartsVisible);
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

        if (bringBigNoteToFrontWhenOpen && bigNoteRect != null) bigNoteRect.SetAsLastSibling();
        if (hideDialogueWhenBigNoteOpens) HideDialoguePanels();

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
        if (pageObject != null) pageObject.SetActive(visible);
    }

    private void SaveBigNoteTagBasePositions()
    {
        if (bigNoteTagBasePositionsSaved) return;
        if (closeTagRect != null) closeTagBasePosition = closeTagRect.anchoredPosition;
        if (noteTagRect != null) noteTagBasePosition = noteTagRect.anchoredPosition;
        if (shopTagRect != null) shopTagBasePosition = shopTagRect.anchoredPosition;
        if (myPageTagRect != null) myPageTagBasePosition = myPageTagRect.anchoredPosition;
        if (menuTagRect != null) menuTagBasePosition = menuTagRect.anchoredPosition;
        bigNoteTagBasePositionsSaved = true;
    }

    private void UpdateBigNoteTags()
    {
        if (!popOutActiveTag) return;
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
        if (tagRect == null) return;
        float offset = popped ? activeTagOffsetX : inactiveTagOffsetX;
        tagRect.anchoredPosition = basePosition + new Vector2(offset, 0f);
    }

    private void ResolveDialogueReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (speechBubbleObject == null)
        {
            Transform foundSpeech = FindChildTransformContainsAll(searchRoot, "speech", "bubble") ?? FindChildTransformContains(searchRoot, "speechbubble");
            if (foundSpeech != null) speechBubbleObject = foundSpeech.gameObject;
        }

        if (descriptionObject == null)
        {
            Transform foundDescription = FindChildTransformContains(searchRoot, "description");
            if (foundDescription != null) descriptionObject = foundDescription.gameObject;
        }

        if (speechText == null && speechBubbleObject != null) speechText = speechBubbleObject.GetComponentInChildren<TMP_Text>(true);
        if (descriptionText == null && descriptionObject != null) descriptionText = descriptionObject.GetComponentInChildren<TMP_Text>(true);
        if (speechText == null && questText != null) speechText = questText;
        if (questText == null && speechText != null) questText = speechText;
        if (speechBubbleRect == null && speechBubbleObject != null) speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
        if (descriptionRect == null && descriptionObject != null) descriptionRect = descriptionObject.GetComponent<RectTransform>();

        speechAutoSize = speechBubbleObject != null ? speechBubbleObject.GetComponent<SpeechBubbleAutoSize>() : null;
        descriptionAutoSize = descriptionObject != null ? descriptionObject.GetComponent<SpeechBubbleAutoSize>() : null;
        ConfigureAutoSize(speechAutoSize, speechText, speechBubbleRect);
        ConfigureAutoSize(descriptionAutoSize, descriptionText, descriptionRect);

        if (durryNameInputObject == null && durryNameInputField != null) durryNameInputObject = durryNameInputField.gameObject;

        if (durryNameInputField != null)
        {
            durryNameInputField.text = "";
            durryNameInputField.onSubmit.RemoveListener(OnNameInputSubmitted);
            durryNameInputField.onSubmit.AddListener(OnNameInputSubmitted);
        }

        SetNameInputVisible(false);
    }

    private void ConfigureAutoSize(SpeechBubbleAutoSize autoSize, TMP_Text text, RectTransform bubbleRect)
    {
        if (autoSize == null || text == null) return;
        autoSize.questText = text;
        if (autoSize.textRect == null) autoSize.textRect = text.GetComponent<RectTransform>();
        if (autoSize.bubbleRect == null && bubbleRect != null) autoSize.bubbleRect = bubbleRect;
    }

    private void BeginOnboardingFlow()
    {
        onboardingActive = true;
        onboardingState = OnboardingState.IntroSpeech;
        step = 0;
        durryName = string.IsNullOrWhiteSpace(durryName) ? defaultDurryName : durryName;
        if (scanZoneObject != null) scanZoneObject.SetActive(false);
        ShowSpeech(introSpeech);
        UpdateBosongText();
    }

    private void AdvanceOnboardingFlow()
    {
        if (Time.time - lastDialogueAdvanceTime < dialogueAdvanceCooldown) return;
        lastDialogueAdvanceTime = Time.time;
        if (!onboardingActive) return;

        switch (onboardingState)
        {
            case OnboardingState.IntroSpeech:
                StartFirstMissionDescription();
                break;
            case OnboardingState.MissionDescription:
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
        if (summonDurryOnMissionStart) SummonDurryToUser();
        if (scanZoneObject != null) scanZoneObject.SetActive(true);
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
        if (!onboardingActive || onboardingState != OnboardingState.NameQuestionSpeech) return;
        ConfirmDurryNameAndShowDescription(submittedName);
    }

    public void SetDurryNameFromVoice(string recognizedName)
    {
        if (!onboardingActive || onboardingState != OnboardingState.NameQuestionSpeech) return;
        ConfirmDurryNameAndShowDescription(recognizedName);
    }

    private void ConfirmDurryNameAndShowDescription(string inputName = null)
    {
        string rawName = inputName;
        if (string.IsNullOrWhiteSpace(rawName) && durryNameInputField != null) rawName = durryNameInputField.text;
        if (string.IsNullOrWhiteSpace(rawName)) rawName = defaultDurryName;
        durryName = rawName.Trim();
        if (string.IsNullOrWhiteSpace(durryName)) durryName = defaultDurryName;

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
        UpdateUI("하단바에서 NOTE / SHOP / MY PAGE를 선택할 수 있어.", $"보송력 {bosongPower}");
    }

    private void ShowSpeech(string message)
    {
        ResolveDialogueReferences();
        SetSpeechVisible(true);
        SetDescriptionVisible(false);
        if (speechText != null) speechText.text = message;
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
        if (descriptionText != null) descriptionText.text = message;
        else if (questText != null && speechText == null) questText.text = message;
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
        if (speechBubbleObject != null) speechBubbleObject.SetActive(visible);
        else if (speechText != null) speechText.gameObject.SetActive(visible);
    }

    private void SetDescriptionVisible(bool visible)
    {
        if (descriptionObject != null) descriptionObject.SetActive(visible);
        else if (descriptionText != null) descriptionText.gameObject.SetActive(visible);
    }

    private void SetNameInputVisible(bool visible)
    {
        if (durryNameInputObject != null) durryNameInputObject.SetActive(visible);
        else if (durryNameInputField != null) durryNameInputField.gameObject.SetActive(visible);
    }

    private void UpdateBosongText()
    {
        if (bosongText != null) bosongText.text = $"보송력 {bosongPower}";
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

        resolvedUIRoot = worldCanvas.transform.parent != null && worldCanvas.transform.parent != centerEyeAnchor ? worldCanvas.transform.parent : worldCanvas.transform;
    }

    private void UpdateViewLockedUI(bool force)
    {
        if (!lockUIToUserView || centerEyeAnchor == null) return;
        if (resolvedUIRoot == null) ResolveUIRoot();
        if (resolvedUIRoot == null) return;
        if (forceUIRootUnderCenterEye && resolvedUIRoot.parent != centerEyeAnchor) resolvedUIRoot.SetParent(centerEyeAnchor, false);

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

        if (detachDurryAndScanZoneFromParent) durryObject.transform.SetParent(null, true);
        durryObject.SetActive(true);

        if (durryVisual == null) durryVisual = durryObject.transform.Find("Durry Visual") ?? durryObject.transform.Find("DurryVisual");
        ApplyDurryScale();

        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);
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
            if (runtimeMat != null) SetFloatIfHas(runtimeMat, "_ReceiveShadows", 0f);
            durryRuntimeMaterials[i] = runtimeMat;
        }
    }

    private void ApplyDurryScale()
    {
        if (durryObject != null) durryObject.transform.localScale = Vector3.one * durryRootScale;
        if (durryVisual != null) durryVisual.localScale = Vector3.one * durryVisualScale;
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
        if (scanZoneObject == null) return;
        if (detachDurryAndScanZoneFromParent) scanZoneObject.transform.SetParent(null, true);
        scanZoneRenderer = scanZoneObject.GetComponent<Renderer>();
        SetupScanZoneMaterial();
        scanZoneObject.SetActive(true);
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
        Camera centerEyeCamera = centerEyeAnchor != null ? centerEyeAnchor.GetComponent<Camera>() : null;
        worldCanvas.worldCamera = centerEyeCamera != null ? centerEyeCamera : Camera.main;

        if (applyTextLayout) ApplyTextLayout();
        if (removeTextShadow)
        {
            RemoveTMPShadow(questText);
            RemoveTMPShadow(speechText);
            RemoveTMPShadow(descriptionText);
            RemoveTMPShadow(bosongText);
        }
        if (disableUIShadowComponents) DisableUIShadowEffects();
        if (makeOnlyBlackDimUITransparent) MakeOnlyBlackDimUITransparent();
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

        if (moveScanZoneWhenDurrySummoned) PlaceScanZoneInFrontOfUser(forward);
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
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        return forward.normalized;
    }

    private Quaternion GetRotationFacingUser(Vector3 objectPosition, float yawOffset)
    {
        Vector3 toUser = centerEyeAnchor.position - objectPosition;
        toUser.y = 0f;
        if (toUser.sqrMagnitude < 0.001f) toUser = -GetFlatForward();
        Quaternion lookAtUser = Quaternion.LookRotation(toUser.normalized, Vector3.up);
        return lookAtUser * Quaternion.Euler(0f, yawOffset, 0f);
    }

    private void ApplyTextLayout()
    {
        ApplyTextRect(bosongText, bosongTextY, bosongTextHeight, bosongFontSize);
        UpdateSpeechBubblePlacement();
        UpdateDescriptionPlacement();
    }

    private void UpdateSpeechBubblePlacement()
    {
        if (!keepSpeechBubbleBelowView) return;
        RectTransform bubble = ResolveSpeechBubbleRect();
        if (bubble == null) return;

        if (forceSpeechBubbleCenterAnchor)
        {
            bubble.anchorMin = new Vector2(0.5f, 0.5f);
            bubble.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.pivot = new Vector2(0.5f, 0.5f);
        }

        Vector2 targetPosition = speechBubbleAnchoredPosition;
        if (clampDialogueAboveWaistNav) targetPosition.y = Mathf.Max(targetPosition.y, speechBubbleMinAnchoredY);
        bubble.anchoredPosition = targetPosition;
    }

    private void UpdateDescriptionPlacement()
    {
        if (!keepDescriptionBelowView) return;
        RectTransform rect = ResolveDescriptionRect();
        if (rect == null) return;

        if (forceDescriptionCenterAnchor)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        Vector2 targetPosition = descriptionAnchoredPosition;
        if (clampDialogueAboveWaistNav) targetPosition.y = Mathf.Max(targetPosition.y, descriptionMinAnchoredY);
        rect.anchoredPosition = targetPosition;
    }

    private RectTransform ResolveDescriptionRect()
    {
        if (descriptionRect != null) return descriptionRect;
        if (descriptionObject != null) return descriptionRect = descriptionObject.GetComponent<RectTransform>();
        return descriptionText != null ? descriptionText.GetComponentInParent<RectTransform>(true) : null;
    }

    private RectTransform ResolveSpeechBubbleRect()
    {
        if (speechBubbleRect != null) return speechBubbleRect;
        if (speechBubbleObject != null) return speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
        TMP_Text targetSpeechText = speechText != null ? speechText : questText;
        return targetSpeechText != null ? targetSpeechText.GetComponentInParent<RectTransform>(true) : null;
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
        text.textWrappingMode = TextWrappingModes.Normal;
        if (fontSize > 0f) text.fontSize = fontSize;
    }

    private void RemoveTMPShadow(TMP_Text text)
    {
        if (text == null || text.fontMaterial == null) return;
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
            if (shadow != null) shadow.enabled = false;
        }
    }

    private void MakeOnlyBlackDimUITransparent()
    {
        if (worldCanvas == null) return;
        Image[] images = worldCanvas.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image == null) continue;
            string objectName = image.gameObject.name.ToLowerInvariant();
            bool looksLikeBlackDim = objectName.Contains("shadow") || objectName.Contains("dim") || objectName.Contains("black");
            if (!looksLikeBlackDim) continue;
            Color color = image.color;
            color.a = 0f;
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private void SetupScanZoneMaterial()
    {
        if (scanZoneRenderer == null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) return;

        Material mat = new Material(shader);
        mat.name = "M_ScanZone_SoftBlue_Runtime";
        Color scanColor = new Color(0.45f, 0.85f, 1f, 0.55f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", scanColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", scanColor);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
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

        step = 1;
        if (summonDurryOnMissionStart) SummonDurryToUser();
        if (scanZoneObject != null) scanZoneObject.SetActive(true);
        ShowDescription("미션 시작!\n더리 주변의 어질러진 물건 3개를 정리해줘.\n완료 테스트: 오른손 약지 핀치");
        UpdateBosongText();
    }

    private void OnPressTrigger()
    {
        if (recenterOnTrigger) SummonDurryToUser();
        if (triggerAlsoCompletesMission) CompleteMission();
    }

    private void CompleteMission()
    {
        if (step != 1)
        {
            Debug.Log("Mission complete ignored. Mission has not started yet.");
            return;
        }

        step = 2;
        bosongPower += rewardBosongPower;
        if (scanZoneObject != null) scanZoneObject.SetActive(false);
        if (changeDurryColorOnMissionComplete) RecoverDurry();
        UpdateBosongText();

        if (onboardingActive && onboardingState == OnboardingState.MissionDescription) ShowNameQuestionSpeech();
        else ShowDescription($"미션 완료!\n보송력 +{rewardBosongPower}");
    }

    private bool GetRightTriggerDown()
    {
        float triggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool isPressed = triggerValue > 0.8f;
        bool pressedThisFrame = isPressed && !wasTriggerPressed;
        wasTriggerPressed = isPressed;
        return pressedThisFrame;
    }

    private void RecoverDurry()
    {
        if (durryRuntimeMaterials == null || durryRuntimeMaterials.Length == 0) return;

        float t = Mathf.Clamp01(bosongPower / 100f);
        Color currentColor = Color.Lerp(new Color(0.55f, 0.52f, 0.48f, 1f), new Color(1f, 0.94f, 0.86f, 1f), t);

        foreach (Material mat in durryRuntimeMaterials)
        {
            if (mat == null) continue;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", currentColor);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", currentColor);
        }
    }

    private void UpdateUI(string questMessage, string bosongMessage)
    {
        if (descriptionText != null) descriptionText.text = questMessage;
        else if (questText != null) questText.text = questMessage;
        if (bosongText != null) bosongText.text = bosongMessage;
        if (descriptionAutoSize != null && descriptionText != null) descriptionAutoSize.SetText(questMessage);
        if (applyTextLayout) ApplyTextLayout();
    }

    private void FaceCanvasToUser()
    {
        if (worldCanvas == null || centerEyeAnchor == null) return;
        Vector3 direction = worldCanvas.transform.position - centerEyeAnchor.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f) worldCanvas.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private Transform FindChildTransformContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrEmpty(keyword)) return null;
        string lowerKeyword = keyword.ToLowerInvariant();
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name.ToLowerInvariant().Contains(lowerKeyword)) return child;
        }
        return null;
    }

    private Transform FindChildTransformContainsAll(Transform root, params string[] keywords)
    {
        if (root == null || keywords == null || keywords.Length == 0) return null;
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
            if (containsAll) return child;
        }
        return null;
    }

    private void SetFloatIfHas(Material mat, string propertyName, float value)
    {
        if (mat != null && mat.HasProperty(propertyName)) mat.SetFloat(propertyName, value);
    }

    private void SetColorIfHas(Material mat, string propertyName, Color value)
    {
        if (mat != null && mat.HasProperty(propertyName)) mat.SetColor(propertyName, value);
    }
}
