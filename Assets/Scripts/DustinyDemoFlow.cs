// VERSION: 66_GRAB_FIX_2026-07-16
// Includes DurryLaserGrabInteraction bridge methods and hides all SideTags on NOTE/MENU.
using System;
using System.Collections;
using System.Collections.Generic;
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
        Finished
    }

    private enum IntroTutorialState
    {
        GestureIntro,
        GestureOK,
        NavInform,
        NavInformOK,
        Extra,
        Finishing,
        Finished
    }

    /// <summary>
    /// 일반 대사에서 표정과 음성을 같은 기준으로 선택하기 위한 감정 분류입니다.
    /// 표정용 키워드와 음성용 키워드가 서로 달라 어긋나는 문제를 막습니다.
    /// </summary>
    private enum DurryDialogueMood
    {
        Neutral,
        Scanning,
        Detected,
        CleaningRequest,
        Retry,
        Failure,
        Success,
        PowerRecover,
        Concern
    }

    /// <summary>
    /// 대사, 표정, 음성을 하나의 항목으로 묶습니다.
    /// Inspector에서 한 Element 안에 세 값이 함께 보이므로 인덱스가 어긋나지 않습니다.
    /// </summary>
    [Serializable]
    public class DurryDialogueStep
    {
        [TextArea(2, 5)]
        public string text;

        [Tooltip("이 대사와 동시에 실행할 Animator State 또는 Trigger 이름입니다.")]
        public string expressionState = "Look";

        [Tooltip("이 대사 전용 음성입니다. 비어 있으면 표정/상황에 맞는 공용 음성을 사용합니다.")]
        public AudioClip voiceClip;

        public DurryDialogueStep()
        {
        }

        public DurryDialogueStep(string text, string expressionState)
        {
            this.text = text;
            this.expressionState = expressionState;
        }
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

    [Header("First Launch Gesture / Navigation Intro")]
    [Tooltip("씬 시작 시 제스처 → 제스처 OK → 네비 안내 → 네비 OK 순서의 이미지 튜토리얼을 먼저 표시합니다.")]
    public bool showImageIntroBeforeOpening = true;

    [Tooltip("GestureIntro, GestureOK, NavInform, NavInformOK, Extra를 묶은 선택적 부모입니다. 비워두어도 자동 탐색합니다.")]
    public GameObject introRootObject;

    [Tooltip("첫 화면: 검지 핀치 제스처 안내 이미지입니다.")]
    public GameObject gestureIntroObject;

    [Tooltip("첫 번째 검지 핀치 뒤 표시할 제스처 확인 이미지입니다.")]
    public GameObject gestureOKObject;

    [Tooltip("두 번째 검지 핀치 뒤 표시할 네비게이션 안내 이미지입니다.")]
    public GameObject navInformObject;

    [Tooltip("세 번째 검지 핀치 뒤 표시할 네비게이션 확인 이미지입니다.")]
    public GameObject navInformOKObject;

    [Tooltip("NavInformOK 다음에 표시할 추가 제스처 안내 이미지입니다. 비워두면 이름이 Extra인 오브젝트를 자동 탐색합니다.")]
    public GameObject extraIntroObject;

    [Tooltip("패스스루 위를 어둡게 덮는 전용 인트로 오버레이입니다. 비워두면 WorldCanvas에 자동 생성합니다.")]
    public GameObject introPassthroughOverlayObject;
    public Image introPassthroughOverlayImage;
    public bool autoCreateIntroPassthroughOverlay = true;

    [Range(0f, 1f)]
    public float introPassthroughOverlayOpacity = 0.72f;

    [Tooltip("NavInformOK가 나타난 뒤 Extra로 넘어가기까지 기다리는 시간입니다. Extra가 없으면 이 시간이 지난 뒤 종료합니다.")]
    [Min(0f)] public float navInformOKHoldSeconds = 2f;

    [Tooltip("Extra가 나타난 뒤 자동으로 인트로를 닫기까지 기다리는 시간입니다.")]
    [Min(0f)] public float extraIntroHoldSeconds = 2f;

    [Tooltip("마지막 안내 이미지 대기 후 패스스루 오버레이가 사라지는 페이드 시간입니다.")]
    [Min(0f)] public float introOverlayFadeDuration = 0.30f;

    [Tooltip("이미지 인트로 동안 더리를 숨기고, 종료 시 다시 표시합니다.")]
    public bool hideDurryDuringImageIntro = true;

    [Tooltip("이미지 인트로 동안 허리 네비게이션을 숨기고, 종료 시 시작 설정값으로 복원합니다.")]
    public bool hideNavigationDuringImageIntro = true;

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

    [Tooltip("항상 켜두세요. 오프닝, 미션 안내, 스캔 중을 포함한 모든 검정 디밍 오버레이를 완전히 사용하지 않습니다.")]
    public bool disableAllBlackDimOverlays = true;

    [Tooltip("레거시 호환용입니다. Disable All Black Dim Overlays가 켜져 있으면 이 값과 관계없이 디밍은 표시되지 않습니다.")]
    public bool missionMessagesUseDescriptionDim = false;

    [Header("Onboarding Dialogue + Expression Pairs")]
    [Tooltip("각 Element 안에서 대사, 표정, 음성을 한 세트로 설정합니다. 검지 핀치마다 다음 Element로 넘어갑니다.")]
    public DurryDialogueStep[] openingDialogueSteps;

    // 기존 씬에 저장된 값을 새 한-세트 구조로 자동 이전하기 위한 레거시 필드입니다.
    // Inspector에는 표시하지 않고, openingDialogueSteps가 비어 있을 때만 사용합니다.
    [HideInInspector]
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

    [Header("Opening Complete -> Mission")]
    [TextArea(2, 5)]
    public string missionNoteUnlockedDescription =
        "오늘의 미션 안내가 준비됐어!\n하단 NOTE를 눌러 확인해줘.";

    [Header("Confirm / Reject SFX + Background Music")]
    [Tooltip("검지 확인 입력에서 Yes 효과음을 재생하는 2D AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource interactionSfxSource;

    [Tooltip("항상 반복되는 배경음악 전용 2D AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource backgroundMusicSource;

    [Tooltip("검지 핀치로 확인/Yes 입력을 했을 때 재생할 효과음입니다.")]
    public AudioClip yesSfxClip;

    [Tooltip("중지 핀치로 거절/No 입력을 했을 때 재생할 효과음입니다.")]
    public AudioClip noSfxClip;

    [Tooltip("게임이 실행되는 동안 반복 재생할 배경음악입니다.")]
    public AudioClip backgroundMusicClip;

    public bool autoCreateInteractionAudioSources = true;
    public bool playBackgroundMusicOnStart = true;

    [Range(0f, 1f)] public float interactionSfxVolume = 1f;
    [Range(0f, 1f)] public float backgroundMusicVolume = 0.35f;

    [Header("Durry Voice / WAV")]
    [Tooltip("더리 위치에서 소리를 재생할 AudioSource입니다. 비워두면 Durry Object에 자동 생성합니다.")]
    public AudioSource durryVoiceSource;
    public bool autoCreateDurryVoiceSource = true;
    [Range(0f, 1f)] public float durryVoiceVolume = 1f;
    [Range(0f, 1f)] public float durryVoiceSpatialBlend = 1f;
    public bool stopPreviousVoiceWhenNewTextAppears = true;

    [Tooltip("Opening Speech Steps와 같은 순서입니다. Size를 대사 개수와 같게 맞춘 뒤 원하는 WAV를 넣으세요. 비어 있으면 아래 상황별 음성을 사용합니다.")]
    public AudioClip[] openingVoiceClips = new AudioClip[10];

    [Header("Durry Voice Clips - Drag Matching WAV Files")]
    public AudioClip voiceAngry;
    public AudioClip voiceClear;
    public AudioClip voiceCheerful;
    public AudioClip voiceDizzy;
    public AudioClip voiceJoy;
    public AudioClip voiceSpecial;
    public AudioClip voiceTearful;
    public AudioClip voiceFindDust;
    public AudioClip voiceGoodJob;
    public AudioClip voiceHappy;
    public AudioClip voiceLevelUp;
    public AudioClip voiceRequestClean;
    public AudioClip voiceReward;
    public AudioClip voiceSad;
    public AudioClip voiceStart;
    public AudioClip voiceWaiting;

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

    [Header("Page Placement Beside Durry - No Grab")]
    [Tooltip("켜면 BigNoteRoot를 카메라 고정 위치가 아니라 더리 옆의 월드 위치에 배치합니다.")]
    public bool placePageBesideDurry = true;

    [Tooltip("더리와 페이지가 붙거나 겹치지 않도록 두 외곽 사이에 남길 실제 월드 간격입니다.")]
    [Min(0f)] public float pageGapFromDurry = 0.12f;

    [Tooltip("자동 크기 계산 결과가 작더라도 확보할 더리 중심 기준 최소 옆 거리입니다.")]
    [Min(0f)] public float pageMinimumSideOffset = 0.75f;

    [Tooltip("페이지가 지나치게 멀어지는 것을 막는 최대 옆 거리입니다.")]
    [Min(0.1f)] public float pageMaximumSideOffset = 1.40f;

    [Tooltip("더리 기준 페이지의 높이입니다.")]
    public float pageHeightOffsetFromDurry = 0.28f;

    [Tooltip("양수면 더리보다 사용자 쪽, 음수면 더리 뒤쪽으로 이동합니다.")]
    public float pageDepthOffsetFromDurry = 0.02f;

    [Tooltip("페이지가 열려 있는 동안 더리가 움직이면 페이지도 계속 함께 움직입니다.")]
    public bool followDurryWhilePageOpen = true;

    [Tooltip("페이지가 사용자를 바라보도록 회전합니다.")]
    public bool faceDurryPageTowardUser = true;

    [Tooltip("페이지 앞뒤가 반대로 보일 때 180으로 바꿉니다.")]
    public float durryPageFacingYawOffset = 0f;

    [Tooltip("이전 버전의 DustinyDurryPageFollowGrab 컴포넌트가 남아 있으면 실행 중 자동으로 제거합니다.")]
    public bool removeLegacyPageGrabComponent = true;

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

    [Header("Shop Coin UI")]
    [Tooltip("ShopPage > ShopHeader > MyCoinDisplay > MyCoin의 TMP Text를 연결하세요. 비워두면 이름으로 자동 탐색합니다.")]
    public TMP_Text shopCoinText;
    [Tooltip("코인 아이콘이 따로 있으므로 기본값은 숫자만 표시합니다.")]
    public string shopCoinTextFormat = "{0}";
    public bool autoFindShopCoinText = true;

    [Header("Page Side Tag Buttons")]
    [Tooltip("SideTags 루트를 연결하면 자동 탐색이 다른 페이지 버튼을 잘못 잡는 일을 막을 수 있습니다.")]
    public Transform sideTagsRoot;
    public Button closePageButton;
    public Button noteTagButton;
    public Button shopTagButton;
    public Button myPageTagButton;
    public Button menuTagButton;
    public bool autoConnectPageButtons = true;

    [Tooltip("켜면 NOTE와 MENU 페이지에서는 X 닫기 버튼을 포함한 모든 사이드 태그를 숨깁니다.")]
    public bool hideSideTabsOnNoteAndMenu = true;

    [Header("Page Tag Pop Out")]
    public RectTransform closeTagRect;
    public RectTransform noteTagRect;
    public RectTransform shopTagRect;
    public RectTransform myPageTagRect;
    public RectTransform menuTagRect;
    public bool popOutActiveTag = true;
    public bool closeTagAlwaysPopped = true;

    [Tooltip("상점/마이페이지의 활성 태그가 옆으로 빠지는 거리입니다. 오른쪽 이동은 양수, 왼쪽 이동은 음수입니다.")]
    public float activeTagOffsetX = 24f;

    public float inactiveTagOffsetX = 0f;

    [Tooltip("Layout Group이 태그 위치를 되돌려도 LateUpdate에서 활성 태그 위치를 다시 적용합니다.")]
    public bool keepActiveTagPoppedAfterLayout = true;

    [Header("Real AI Mission Integration")]
    [Tooltip("AI 스캔과 더리 노트를 연결하는 DustinyMissionController입니다.")]
    public DustinyMissionController missionController;

    [Header("Start / Summon")]
    public bool summonDurryOnStart = true;
    public bool detachDurryAndScanZoneFromParent = false;

    [Header("Input")]
    [Tooltip("에디터 테스트용입니다. 실제 Quest 빌드에서는 UI 버튼과 Interaction SDK 핀치를 사용합니다.")]
    public bool allowControllerFallback = false;

    [Header("Hand Tracking Confirm / Reject")]
    [Tooltip("오른손 검지 핀치를 공용 확인(O) 입력으로 사용합니다.")]
    public bool rightIndexPinchConfirms = true;

    [Tooltip("오른손 중지 핀치를 모든 선택 상황의 거절(X) 입력으로 사용합니다. 미션 시작 질문은 취소하고, 탐지 결과는 거절 후 다시 스캔하며, 열린 창과 일반 안내는 닫습니다.")]
    public bool rightMiddlePinchRejectsEverything = true;

    [Tooltip("레거시 옵션입니다. 전체 거절 옵션이 꺼졌을 때만 탐지 결과 확인 화면에서 중지 핀치로 다시 스캔합니다.")]
    public bool rightMiddlePinchRetriesMissionScan = true;

    [Tooltip("레거시 옵션입니다. 전체 거절 옵션이 꺼졌을 때만 열린 페이지를 중지 핀치로 닫습니다.")]
    public bool rightMiddlePinchClosesOpenPage = true;
    [Tooltip("오른손 약지 핀치로 활성 미션의 검사 재스캔을 시작합니다.")]
    public bool rightRingPinchStartsOrRescans = true;
    public OVRHand rightHand;
    public bool autoFindRightOVRHandIfMissing = true;

    [Header("Left Hand Debug Reset")]
    [Tooltip("왼손 약지 핀치를 한 번 하면 오늘 미션 진행도, 보송력, 크레딧, 구매/장착 아이템을 모두 초기값으로 되돌립니다.")]
    public bool leftRingPinchResetsMissionCleanlinessAndCredit = true;
    public OVRHand leftHand;
    public bool autoFindLeftOVRHandIfMissing = true;
    [Min(0f)] public float resetInputCooldown = 1f;

    [Header("Left Pinky Pinch Navigation")]
    [Tooltip("왼손 소지(새끼손가락) 핀치 한 번으로 네비게이션 바를 열거나 닫습니다. OVRSkeleton은 필요하지 않습니다.")]
    public bool leftPinkyPinchTogglesNavigation = true;

    [Tooltip("소지 핀치가 연속으로 두 번 처리되는 것을 막는 대기 시간입니다.")]
    [Min(0f)] public float leftPinkyPinchToggleCooldown = 0.55f;

    [Min(0f)] public float confirmInputCooldown = 0.20f;

    // 페이지 버튼에 사용한 검지 핀치가 같은 프레임의 전역 미션 확인으로
    // 중복 처리되지 않도록 하는 고정 차단 시간입니다. 인스펙터 설정은 필요 없습니다.
    private const float PageButtonPinchSuppressionSeconds = 0.35f;

    [Header("Durry Expression Animation")]
    [Tooltip("더리 캐릭터에 붙은 Animator입니다. 비워두면 Durry Object 아래에서 자동 탐색합니다.")]
    public Animator durryAnimator;
    public bool autoFindDurryAnimator = true;

    [Tooltip("Animator에 같은 이름의 Trigger가 있으면 Trigger를 사용할 수 있습니다.")]
    public bool preferAnimatorTriggers = true;

    [Tooltip("켜면 대사가 바뀌는 즉시 해당 상태로 CrossFade합니다. Any State 전환의 Exit Time 때문에 표정이 한 박자 늦는 현상을 막습니다.")]
    public bool forceImmediateExpressionSwitch = true;

    [Min(0f)] public float expressionCrossFadeDuration = 0.12f;
    public bool logMissingExpressionWarnings = true;

    [HideInInspector]
    public string[] openingExpressionStates =
    {
        "Surprised", // 으앙... 엣취! 여긴 어디야?
        "Confused",  // 나는 누구지...?
        "Joyful",    // 청결요정이었던 것 같아!
        "Sad",       // 몸이 무겁고 먼지가 붙어 있어
        "Confused",  // 뭔가를 얻으면 힘이 날 것 같아
        "Surprised", // 생각났어! 보송력!
        "Joyful",    // 보송력으로 힘을 되찾을 수 있어
        "Surprised", // 저쪽에서 보송력의 기운이 느껴져
        "Worried",   // 정확히 어디 있는지는 모르겠어
        "Look"       // 네 도움이 필요해
    };

    [Tooltip("오프닝 마지막 미션 안내에 사용할 표정입니다. 자동 상황 표정이 꺼져 있을 때만 사용합니다.")]
    public string missionDescriptionExpressionState = "Look";
    public string idleExpressionState = "DurryIdleAni";

    [Header("Situation Based Durry Expressions")]
    [Tooltip("말풍선/디스크립션 내용에 따라 더리 표정을 자동으로 바꿉니다.")]
    public bool autoSelectExpressionFromDialogue = true;

    [Tooltip("말풍선/디스크립션이 닫히면 기본 Idle 표정으로 돌아갑니다.")]
    public bool returnToIdleWhenDialogueHidden = true;

    [Tooltip("일반 안내나 미션 시작 질문에서 사용할 중립 표정입니다.")]
    public string neutralDialogueExpressionState = "Look";

    [Tooltip("스캔/검사 중 사용할 집중 표정입니다.")]
    public string scanningExpressionState = "Focused";

    [Tooltip("물건 탐지가 완료됐을 때 사용할 표정입니다.")]
    public string detectedExpressionState = "Surprised";

    [Tooltip("정리를 요청할 때 사용할 표정입니다. 부탁하는 대사는 집중 표정보다 Look이 자연스럽습니다.")]
    public string cleaningRequestExpressionState = "Look";

    [Tooltip("재스캔이나 재시도를 안내할 때 사용할 표정입니다.")]
    public string retryExpressionState = "Confused";

    [Tooltip("실패/오류 안내에서 사용할 표정입니다.")]
    public string failureExpressionState = "Sad";

    [Tooltip("미션 완료/보상에서 사용할 표정입니다.")]
    public string successExpressionState = "Joyful";

    [Tooltip("보송력 회복이나 레벨 상승에서 사용할 표정입니다.")]
    public string powerRecoverExpressionState = "PowerRecover";

    [Tooltip("초기화 확인, 보상 없이 계속하기 같은 조심스러운 질문에서 사용할 표정입니다.")]
    public string concernExpressionState = "Worried";

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

    [Header("Durry Cleanliness Materials - L0 to L4")]
    [Tooltip("보송력 머터리얼이 바뀌어야 하는 더리 바디 Renderer만 연결하세요. 비워두면 이름에 body/durry_geo/main이 포함된 Renderer를 자동 탐색합니다.")]
    public Renderer[] durryCleanlinessRenderers;

    [Tooltip("Element 0=L0, 1=L1, 2=L2, 3=L3, 4=L4 머터리얼입니다.")]
    public Material[] durryCleanlinessMaterials = new Material[5];

    [Tooltip("바디 Renderer에서 교체할 머터리얼 슬롯 번호입니다. 일반적으로 0입니다.")]
    [Min(0)] public int durryCleanlinessMaterialSlot = 0;

    [Tooltip("체크하면 바디 Renderer의 모든 머터리얼 슬롯을 현재 보송력 머터리얼로 바꿉니다.")]
    public bool replaceAllDurryBodyMaterialSlots = false;

    public bool autoFindDurryCleanlinessRenderers = true;

    [Tooltip("SkinnedMeshRenderer와 같은 오브젝트에 실수로 추가된 MeshRenderer를 런타임에 끕니다.")]
    public bool disableDuplicateBodyMeshRenderer = true;

    public bool logCleanlinessMaterialChanges = true;

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
    private IntroTutorialState introTutorialState = IntroTutorialState.Finished;
    private int openingSpeechIndex;
    private bool onboardingActive;
    private bool introTutorialActive;
    private Coroutine introFinishCoroutine;
    private bool navigationWasVisibleBeforeIntro;
    private bool durryWasActiveBeforeIntro;
    private float lastDialogueAdvanceTime = -999f;
    private float lastConfirmInputTime = -999f;
    private float suppressMissionPinchUntil = -999f;

    private SpeechBubbleAutoSize speechAutoSize;
    private SpeechBubbleAutoSize descriptionAutoSize;

    private bool wasTriggerPressed;
    private bool wasRightIndexPinching;
    private bool wasRightMiddlePinching;
    private bool wasRightRingPinching;
    private bool wasLeftRingPinching;
    private float lastResetInputTime = -999f;
    private bool awaitingResetConfirmation;
    private bool creditUIEventSubscribed;

    private bool currentDescriptionUsesDim = true;
    private bool wasLeftPinkyPinching;
    private float lastLeftPinkyNavigationToggleTime = -999f;

    private DustinyPage currentPage = DustinyPage.None;
    private bool pageRootOpen;

    public DustinyPage CurrentPage => currentPage;
    public bool IsAnyPageOpen => pageRootOpen;
    public bool IsNotePageOpen => pageRootOpen && currentPage == DustinyPage.Note;
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

    private bool cleanlinessMaterialEventSubscribed;
    private int lastAppliedCleanlinessLevel = -1;
    private string lastVoiceMessage = string.Empty;
    private float lastVoiceMessageTime = -999f;

    private void OnValidate()
    {
        EnsureOpeningDialogueSteps();

        if (synchronizeDialoguePosition)
        {
            // 인스펙터에서도 두 레거시 위치가 서로 달라 보이지 않도록 공통 위치로 맞춥니다.
            speechBubbleAnchoredPosition = sharedDialogueAnchoredPosition;
            descriptionAnchoredPosition = sharedDialogueAnchoredPosition;
        }

        if (openingDialogueSteps != null)
        {
            foreach (DurryDialogueStep step in openingDialogueSteps)
            {
                if (step != null)
                {
                    step.expressionState =
                        NormalizeLegacyExpressionName(step.expressionState);
                }
            }
        }

        if (openingExpressionStates != null)
        {
            for (int index = 0; index < openingExpressionStates.Length; index++)
            {
                openingExpressionStates[index] =
                    NormalizeLegacyExpressionName(openingExpressionStates[index]);
            }
        }

        // 더리 바디 SkinnedMeshRenderer의 실제 머터리얼 슬롯은 0번입니다.
        durryCleanlinessMaterialSlot = 0;
        interactionSfxVolume = Mathf.Clamp01(interactionSfxVolume);
        backgroundMusicVolume = Mathf.Clamp01(backgroundMusicVolume);
        durryVoiceVolume = Mathf.Clamp01(durryVoiceVolume);
        durryVoiceSpatialBlend = Mathf.Clamp01(durryVoiceSpatialBlend);

        pageGapFromDurry = Mathf.Max(0f, pageGapFromDurry);
        pageMinimumSideOffset = Mathf.Max(0f, pageMinimumSideOffset);
        pageMaximumSideOffset = Mathf.Max(
            Mathf.Max(0.1f, pageMinimumSideOffset),
            pageMaximumSideOffset
        );
    }

    private void OnEnable()
    {
        SubscribeCreditUI();
        SubscribeCleanlinessMaterialUpdates();
        ResolveShopCoinText();
        RefreshShopCoinUI();

        if (Application.isPlaying)
        {
            SetupInteractionAudio();
        }
    }

    private void OnDisable()
    {
        UnsubscribeCreditUI();
        UnsubscribeCleanlinessMaterialUpdates();

        if (durryVoiceSource != null)
        {
            durryVoiceSource.Stop();
        }
    }

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start - modular manager integration");
        EnsureOpeningDialogueSteps();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        ResolveRightHandIfNeeded();
        ResolveLeftHandIfNeeded();
        ResolveUIRoot();
        SetupDurry();
        SetupDurryVoice();
        SetupInteractionAudio();
        SubscribeCleanlinessMaterialUpdates();
        ApplyCurrentCleanlinessMaterial(force: true);
        SetupScanZone();

        ResolveDialogueReferences();
        SetupDescriptionDimOverlay();
        DisableAllKnownDimOverlays();
        ResolveNavigationReferences();
        ResolveNavigationButtons();
        ConnectNavigationButtonEvents();

        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
        ConnectPageButtonEvents();
        ConfigurePageRootGraphics();

        ResolveShopCoinText();
        SubscribeCreditUI();
        RefreshShopCoinUI();

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

        // 이전 버전의 별도 그랩 컴포넌트는 사용하지 않습니다.
        // 페이지 위치 계산과 더리 추적은 모두 이 DustinyDemoFlow가 담당합니다.
        RemoveLegacyPageGrabComponent();

        UpdateViewLockedUI();
        UpdateDialoguePlacement();
        UpdatePageRootPlacement();

        if (showImageIntroBeforeOpening)
        {
            BeginImageIntroTutorial();
        }
        else
        {
            StartOpeningAfterImageIntro();
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

        if (rightIndexPinchConfirms ||
            rightMiddlePinchRejectsEverything ||
            rightMiddlePinchRetriesMissionScan ||
            rightMiddlePinchClosesOpenPage ||
            rightRingPinchStartsOrRescans ||
            leftRingPinchResetsMissionCleanlinessAndCredit ||
            leftPinkyPinchTogglesNavigation)
        {
            UpdateHandPinchInput();
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

    private void LateUpdate()
    {
        if (!pageRootOpen ||
            !popOutActiveTag ||
            !keepActiveTagPoppedAfterLayout)
        {
            return;
        }

        // UI Layout Group이 anchoredPosition을 되돌린 뒤 마지막에 다시 적용합니다.
        if (sideTagsRoot is RectTransform sideTagsRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(sideTagsRect);
        }

        UpdatePageTags();
    }

    #region Navigation And Pages

    private void ResolveNavigationReferences()
    {
        if (navigationBarObject != null)
        {
            return;
        }

        // 먼저 WorldCanvas 아래를 찾습니다.
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        Transform found = FindChildTransformContainsAll(searchRoot, "navigation", "bar") ??
                          FindChildTransformContains(searchRoot, "navbar") ??
                          FindChildTransformContains(searchRoot, "navigationbar") ??
                          FindChildTransformContains(searchRoot, "waistnavcanvas");

        // 네비게이션이 WorldCanvas 밖의 WaistFollowRoot 아래에 있는 현재 씬 구조도 지원합니다.
        if (found == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null)
                {
                    continue;
                }

                string lowerName = candidate.name.ToLowerInvariant();
                bool isNavigationRoot =
                    lowerName == "waistnavcanvas" ||
                    lowerName == "navigationbar" ||
                    lowerName.Contains("waistnav") ||
                    lowerName.Contains("navigationbar");

                if (!isNavigationRoot)
                {
                    continue;
                }

                found = candidate;
                break;
            }
        }

        if (found != null)
        {
            navigationBarObject = found.gameObject;
        }
        else
        {
            Debug.LogWarning(
                "[DustinyDemoFlow] Navigation Bar Object를 찾지 못했습니다. " +
                "WaistNavCanvas를 Navigation Bar Object에 직접 연결하세요."
            );
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

    private void RemoveLegacyPageGrabComponent()
    {
        if (!removeLegacyPageGrabComponent)
        {
            return;
        }

        ResolvePageReferences();
        if (bigNoteRoot == null)
        {
            return;
        }

        Component[] components = bigNoteRoot.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null ||
                component == this ||
                component.GetType().Name != "DustinyDurryPageFollowGrab")
            {
                continue;
            }

            // Component 자체에는 enabled 속성이 없습니다.
            // MonoBehaviour, Renderer 등 enabled를 가진 Behaviour인 경우에만 먼저 끕니다.
            if (component is Behaviour legacyBehaviour)
            {
                legacyBehaviour.enabled = false;
            }

            Destroy(component);
            Debug.Log(
                "[더리 페이지] 이전 DustinyDurryPageFollowGrab을 제거했습니다. " +
                "이제 위치 추적은 DustinyDemoFlow가 직접 담당합니다."
            );
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
        ConnectPageButtonClick(closePageButton, CloseBigNote);
        ConnectPageButtonClick(noteTagButton, OpenNotePage);
        ConnectPageButtonClick(shopTagButton, OpenShopPage);
        ConnectPageButtonClick(myPageTagButton, OpenMyPage);
        ConnectPageButtonClick(menuTagButton, OpenMenuPage);
    }

    public void OpenNotePage()
    {
        SuppressMissionPinchFromPageUI();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        // With no active round, NOTE shows a description prompt instead of an empty page.
        if (missionController != null && !missionController.RequestOpenMissionNote())
        {
            return;
        }

        OpenPage(DustinyPage.Note);
    }

    public void OpenShopPage()
    {
        SuppressMissionPinchFromPageUI();
        OpenPage(DustinyPage.Shop);
    }

    public void OpenMyPage()
    {
        SuppressMissionPinchFromPageUI();
        OpenPage(DustinyPage.MyPage);
    }

    public void OpenMenuPage()
    {
        SuppressMissionPinchFromPageUI();
        OpenPage(DustinyPage.Menu);
    }

    public void CloseBigNote()
    {
        // The same index pinch that clicks X must end here. It must not fall through
        // to the global mission-confirm input and reopen NOTE.
        SuppressMissionPinchFromPageUI();
        ShopInventoryManager.Instance?.ClearAllPreviews();
        currentPage = DustinyPage.None;
        SetPageRootVisible(false);
        UpdatePageTags();
    }

    public void OpenPage(DustinyPage page)
    {
        if (page != currentPage)
        {
            // Leaving Shop/MyPage must always cancel temporary try-on state.
            ShopInventoryManager.Instance?.ClearAllPreviews();
        }

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

        // Other page objects may disable after Shop/MyPage enables and clear previews.
        // Refresh the final active item page once more after all visibility changes.
        if (page == DustinyPage.Shop || page == DustinyPage.MyPage)
        {
            DustinyItemPageController itemPageController =
                requestedPage.GetComponentInChildren<DustinyItemPageController>(true);
            itemPageController?.RefreshPage();
        }

        if (page == DustinyPage.Note)
        {
            QuestStatusUI statusUI = FindFirstObjectByType<QuestStatusUI>();
            statusUI?.RefreshMissionSlots();
        }

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

        if (page == DustinyPage.Shop)
        {
            RefreshShopCoinUI();
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
        ResolvePageReferences();

        if (bigNoteRect == null)
        {
            return;
        }

        // 새 방식: BigNoteRoot가 카메라 위치를 따르지 않고 더리 옆에 떨어져서 생성됩니다.
        // 별도 그랩 스크립트 없이 DustinyDemoFlow가 매 프레임 월드 위치를 직접 유지합니다.
        if (placePageBesideDurry)
        {
            if (!pageRootOpen && followDurryWhilePageOpen)
            {
                return;
            }

            UpdatePageWorldPoseBesideDurry();
            return;
        }

        // placePageBesideDurry를 끈 경우에만 기존 인스펙터 기반 UI 위치 방식을 사용합니다.
        if (preservePageRootInspectorLayout || !keepPageRootInView)
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

    [ContextMenu("Page/Apply Higher And Further Back Position")]
    private void ApplyRecommendedHigherBackPagePosition()
    {
        pageHeightOffsetFromDurry = 0.45f;
        pageDepthOffsetFromDurry = -0.14f;
        pageGapFromDurry = 0.14f;
        UpdatePageRootPlacement();

        Debug.Log(
            "[더리 페이지 위치] 더 위쪽/뒤쪽 추천값을 적용했습니다. " +
            "Height=0.45, Depth=-0.14, Gap=0.14"
        );
    }

    private void UpdatePageWorldPoseBesideDurry()
    {
        if (durryObject == null || bigNoteRect == null)
        {
            return;
        }

        Transform durryRoot = durryObject.transform;

        Vector3 sideDirection =
            centerEyeAnchor != null
                ? centerEyeAnchor.right
                : durryRoot.right;

        sideDirection.y = 0f;
        if (sideDirection.sqrMagnitude < 0.001f)
        {
            sideDirection = Vector3.right;
        }
        sideDirection.Normalize();

        Vector3 towardViewer =
            centerEyeAnchor != null
                ? centerEyeAnchor.position - durryRoot.position
                : -durryRoot.forward;

        towardViewer.y = 0f;
        if (towardViewer.sqrMagnitude < 0.001f)
        {
            towardViewer = -durryRoot.forward;
        }
        towardViewer.Normalize();

        float resolvedSideOffset = CalculateDurryPageSideOffset(sideDirection);

        Vector3 targetPosition =
            durryRoot.position +
            sideDirection * resolvedSideOffset +
            Vector3.up * pageHeightOffsetFromDurry +
            towardViewer * pageDepthOffsetFromDurry;

        bigNoteRect.position = targetPosition;

        if (faceDurryPageTowardUser && centerEyeAnchor != null)
        {
            Vector3 facingDirection = targetPosition - centerEyeAnchor.position;
            if (facingDirection.sqrMagnitude > 0.001f)
            {
                bigNoteRect.rotation =
                    Quaternion.LookRotation(facingDirection.normalized, Vector3.up) *
                    Quaternion.Euler(0f, durryPageFacingYawOffset, 0f);
            }
        }
    }

    private float CalculateDurryPageSideOffset(Vector3 sideDirection)
    {
        float minimumOffset = Mathf.Max(0f, pageMinimumSideOffset);
        float maximumOffset = Mathf.Max(minimumOffset, pageMaximumSideOffset);

        float durryExtent = CalculateDurryVisualExtent(sideDirection);
        float pageHalfWidth = CalculatePageWorldHalfWidth();

        float requiredOffset =
            durryExtent +
            pageHalfWidth +
            Mathf.Max(0f, pageGapFromDurry);

        return Mathf.Clamp(
            Mathf.Max(minimumOffset, requiredOffset),
            minimumOffset,
            maximumOffset
        );
    }

    private float CalculateDurryVisualExtent(Vector3 direction)
    {
        if (durryObject == null)
        {
            return 0.22f;
        }

        Renderer[] renderers =
            durryObject.GetComponentsInChildren<Renderer>(true);

        float maximumProjection = 0f;
        bool foundVisibleRenderer = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 centerOffset =
                bounds.center - durryObject.transform.position;

            float centerProjection =
                Mathf.Abs(Vector3.Dot(centerOffset, direction));

            Vector3 extents = bounds.extents;
            float extentProjection =
                Mathf.Abs(direction.x) * extents.x +
                Mathf.Abs(direction.y) * extents.y +
                Mathf.Abs(direction.z) * extents.z;

            maximumProjection = Mathf.Max(
                maximumProjection,
                centerProjection + extentProjection
            );
            foundVisibleRenderer = true;
        }

        return foundVisibleRenderer
            ? maximumProjection
            : 0.22f;
    }

    private float CalculatePageWorldHalfWidth()
    {
        if (bigNoteRect == null)
        {
            return 0.32f;
        }

        float localHalfWidth =
            Mathf.Abs(bigNoteRect.rect.width) * 0.5f;

        float worldHalfWidth =
            bigNoteRect.TransformVector(
                Vector3.right * localHalfWidth
            ).magnitude;

        return worldHalfWidth > 0.001f
            ? worldHalfWidth
            : 0.32f;
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
        ResolvePageButtons();

        bool hideAllSideTags = pageRootOpen &&
                               hideSideTabsOnNoteAndMenu &&
                               (currentPage == DustinyPage.Note ||
                                currentPage == DustinyPage.Menu);

        // NOTE와 MENU는 독립 페이지이므로 X 버튼까지 포함한 SideTags 전체를 숨깁니다.
        // SHOP과 MYPAGE로 이동하면 같은 루트를 다시 활성화합니다.
        if (sideTagsRoot != null)
        {
            sideTagsRoot.gameObject.SetActive(!hideAllSideTags);
        }
        else
        {
            // SideTags Root가 연결되지 않은 씬을 위한 안전한 폴백입니다.
            SetPageTagButtonVisible(closePageButton, !hideAllSideTags);
            SetPageTagButtonVisible(noteTagButton, !hideAllSideTags);
            SetPageTagButtonVisible(shopTagButton, !hideAllSideTags);
            SetPageTagButtonVisible(myPageTagButton, !hideAllSideTags);
            SetPageTagButtonVisible(menuTagButton, !hideAllSideTags);
        }

        if (!popOutActiveTag || hideAllSideTags)
        {
            return;
        }

        SavePageTagBasePositions();

        bool isOpen = pageRootOpen;
        SetTagPopped(closeTagRect, closeTagBasePosition, isOpen && closeTagAlwaysPopped);

        // NOTE와 MENU에는 사이드 태그 자체가 나타나지 않습니다.
        SetTagPopped(noteTagRect, noteTagBasePosition, false);
        SetTagPopped(menuTagRect, menuTagBasePosition, false);

        // SHOP과 MYPAGE에서 선택된 태그만 옆으로 빠집니다.
        SetTagPopped(
            shopTagRect,
            shopTagBasePosition,
            isOpen && currentPage == DustinyPage.Shop
        );
        SetTagPopped(
            myPageTagRect,
            myPageTagBasePosition,
            isOpen && currentPage == DustinyPage.MyPage
        );
    }

    private static void SetPageTagButtonVisible(Button button, bool visible)
    {
        if (button != null)
        {
            button.gameObject.SetActive(visible);
        }
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

    private void ConnectPageButtonClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        // RemoveAllListeners()는 Inspector에 저장된 Persistent Listener를 제거하지 못합니다.
        // 새 UnityEvent로 교체해야 X 버튼에 남은 OpenNotePage 같은 잘못된 연결까지
        // 런타임에서 완전히 무시하고 이 컨트롤러가 지정한 동작 하나만 실행할 수 있습니다.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
    }

    private void SuppressMissionPinchFromPageUI()
    {
        float duration = Mathf.Max(confirmInputCooldown, PageButtonPinchSuppressionSeconds);
        suppressMissionPinchUntil = Mathf.Max(
            suppressMissionPinchUntil,
            Time.unscaledTime + duration
        );
        lastConfirmInputTime = Time.unscaledTime;
    }

    private bool IsMissionPinchSuppressedByPageUI()
    {
        return Time.unscaledTime < suppressMissionPinchUntil;
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

    private void ResolveShopCoinText()
    {
        if (shopCoinText != null || !autoFindShopCoinText)
        {
            return;
        }

        ResolvePageReferences();

        Transform searchRoot = shopPageObject != null
            ? shopPageObject.transform
            : bigNoteRoot != null
                ? bigNoteRoot.transform
                : worldCanvas != null
                    ? worldCanvas.transform
                    : transform;

        Transform coinTextTransform =
            FindChildTransformExact(searchRoot, "MyCoin") ??
            FindChildTransformExact(searchRoot, "MyCoinText") ??
            FindChildTransformContainsAll(searchRoot, "my", "coin");

        if (coinTextTransform == null)
        {
            return;
        }

        shopCoinText = coinTextTransform.GetComponent<TMP_Text>() ??
                       coinTextTransform.GetComponentInChildren<TMP_Text>(true);
    }

    private void SubscribeCreditUI()
    {
        if (creditUIEventSubscribed)
        {
            return;
        }

        CreditManager.OnCreditChanged += HandleCreditChanged;
        creditUIEventSubscribed = true;
    }

    private void UnsubscribeCreditUI()
    {
        if (!creditUIEventSubscribed)
        {
            return;
        }

        CreditManager.OnCreditChanged -= HandleCreditChanged;
        creditUIEventSubscribed = false;
    }

    private void HandleCreditChanged(int currentCredit)
    {
        RefreshShopCoinUI(currentCredit);
    }

    private void RefreshShopCoinUI()
    {
        int currentCredit = CreditManager.Instance != null
            ? CreditManager.Instance.CurrentCredit
            : 0;

        RefreshShopCoinUI(currentCredit);
    }

    private void RefreshShopCoinUI(int currentCredit)
    {
        ResolveShopCoinText();

        if (shopCoinText == null)
        {
            return;
        }

        string format = string.IsNullOrWhiteSpace(shopCoinTextFormat)
            ? "{0}"
            : shopCoinTextFormat;

        shopCoinText.text = string.Format(format, Mathf.Max(0, currentCredit));
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

    private void ResolveImageIntroReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        if (introRootObject == null)
        {
            Transform foundRoot = FindChildTransformExact(searchRoot, "Intro") ??
                                  FindChildTransformExact(searchRoot, "ImageIntro") ??
                                  FindChildTransformContainsAll(searchRoot, "intro", "root");
            if (foundRoot != null)
            {
                introRootObject = foundRoot.gameObject;
            }
        }

        if (gestureIntroObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "GestureIntro") ??
                              FindChildTransformContainsAll(searchRoot, "gesture", "intro");
            if (found != null) gestureIntroObject = found.gameObject;
        }

        if (gestureOKObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "GestureOK") ??
                              FindChildTransformContainsAll(searchRoot, "gesture", "ok");
            if (found != null) gestureOKObject = found.gameObject;
        }

        if (navInformObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "NavInform") ??
                              FindChildTransformContainsAll(searchRoot, "nav", "inform");
            if (found != null) navInformObject = found.gameObject;
        }

        if (navInformOKObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "NavInformOK") ??
                              FindChildTransformContainsAll(searchRoot, "nav", "inform", "ok");
            if (found != null) navInformOKObject = found.gameObject;
        }

        if (extraIntroObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "Extra") ??
                              FindChildTransformExact(searchRoot, "ExtraIntro") ??
                              FindChildTransformContainsAll(searchRoot, "extra", "intro") ??
                              FindChildTransformContains(searchRoot, "extra");
            if (found != null) extraIntroObject = found.gameObject;
        }
    }

    private void SetupImageIntroOverlay()
    {
        ResolveImageIntroReferences();

        if (introPassthroughOverlayObject == null && introPassthroughOverlayImage != null)
        {
            introPassthroughOverlayObject = introPassthroughOverlayImage.gameObject;
        }

        if (introPassthroughOverlayImage == null && introPassthroughOverlayObject != null)
        {
            introPassthroughOverlayImage = introPassthroughOverlayObject.GetComponent<Image>();
        }

        if (introPassthroughOverlayObject == null &&
            autoCreateIntroPassthroughOverlay &&
            worldCanvas != null)
        {
            introPassthroughOverlayObject = new GameObject(
                "IntroPassthroughOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );

            RectTransform overlayRect = introPassthroughOverlayObject.GetComponent<RectTransform>();
            overlayRect.SetParent(worldCanvas.transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;

            introPassthroughOverlayImage = introPassthroughOverlayObject.GetComponent<Image>();
        }

        if (introPassthroughOverlayImage != null)
        {
            Color overlayColor = introPassthroughOverlayImage.color;
            overlayColor.r = 0f;
            overlayColor.g = 0f;
            overlayColor.b = 0f;
            overlayColor.a = Mathf.Clamp01(introPassthroughOverlayOpacity);
            introPassthroughOverlayImage.color = overlayColor;
            introPassthroughOverlayImage.raycastTarget = false;
        }
    }

    private void BeginImageIntroTutorial()
    {
        ResolveImageIntroReferences();
        SetupImageIntroOverlay();

        if (gestureIntroObject == null)
        {
            Debug.LogWarning(
                "[이미지 인트로] GestureIntro를 찾지 못해 이미지 인트로를 건너뜁니다. " +
                "WorldCanvas 아래 오브젝트 이름을 GestureIntro로 맞추거나 직접 연결하세요."
            );
            StartOpeningAfterImageIntro();
            return;
        }

        introTutorialActive = true;
        introTutorialState = IntroTutorialState.GestureIntro;
        onboardingActive = false;
        awaitingResetConfirmation = false;

        if (introFinishCoroutine != null)
        {
            StopCoroutine(introFinishCoroutine);
            introFinishCoroutine = null;
        }

        navigationWasVisibleBeforeIntro = navigationBarObject != null && navigationBarObject.activeSelf;
        durryWasActiveBeforeIntro = durryObject != null && durryObject.activeSelf;

        HideDialoguePanels();
        SetPageRootVisible(false);
        SetScanZoneVisible(false);

        if (hideNavigationDuringImageIntro)
        {
            SetNavigationBarVisible(false);
        }

        if (hideDurryDuringImageIntro && durryObject != null)
        {
            durryObject.SetActive(false);
        }

        if (introRootObject != null)
        {
            introRootObject.SetActive(true);
        }

        if (introPassthroughOverlayObject != null)
        {
            introPassthroughOverlayObject.SetActive(true);
            introPassthroughOverlayObject.transform.SetAsLastSibling();
        }

        SetImageIntroStage(IntroTutorialState.GestureIntro);
        Debug.Log("[이미지 인트로] GestureIntro 시작");
    }

    private void SetImageIntroStage(IntroTutorialState stage)
    {
        introTutorialState = stage;

        SetIntroObjectVisible(gestureIntroObject, stage == IntroTutorialState.GestureIntro);
        SetIntroObjectVisible(gestureOKObject, stage == IntroTutorialState.GestureOK);
        SetIntroObjectVisible(navInformObject, stage == IntroTutorialState.NavInform);
        SetIntroObjectVisible(navInformOKObject, stage == IntroTutorialState.NavInformOK);
        SetIntroObjectVisible(extraIntroObject, stage == IntroTutorialState.Extra);

        GameObject visibleObject = GetImageIntroObject(stage);
        BringImageIntroAboveOverlay(visibleObject);
    }

    private void BringImageIntroAboveOverlay(GameObject visibleObject)
    {
        if (visibleObject == null)
        {
            return;
        }

        visibleObject.transform.SetAsLastSibling();

        if (worldCanvas == null)
        {
            return;
        }

        Transform canvasTransform = worldCanvas.transform;
        Transform topLayer = introRootObject != null
            ? introRootObject.transform
            : visibleObject.transform;

        while (topLayer.parent != null && topLayer.parent != canvasTransform)
        {
            topLayer = topLayer.parent;
        }

        if (topLayer.parent == canvasTransform)
        {
            topLayer.SetAsLastSibling();
        }
    }

    private static void SetIntroObjectVisible(GameObject target, bool visible)
    {
        if (target != null)
        {
            target.SetActive(visible);
        }
    }

    private GameObject GetImageIntroObject(IntroTutorialState stage)
    {
        switch (stage)
        {
            case IntroTutorialState.GestureIntro: return gestureIntroObject;
            case IntroTutorialState.GestureOK: return gestureOKObject;
            case IntroTutorialState.NavInform: return navInformObject;
            case IntroTutorialState.NavInformOK: return navInformOKObject;
            case IntroTutorialState.Extra: return extraIntroObject;
            default: return null;
        }
    }

    private void AdvanceImageIntroTutorial()
    {
        if (!introTutorialActive || introTutorialState == IntroTutorialState.Finishing)
        {
            return;
        }

        switch (introTutorialState)
        {
            case IntroTutorialState.GestureIntro:
                SetImageIntroStage(IntroTutorialState.GestureOK);
                Debug.Log("[이미지 인트로] GestureOK");
                break;

            case IntroTutorialState.GestureOK:
                SetImageIntroStage(IntroTutorialState.NavInform);
                Debug.Log("[이미지 인트로] NavInform");
                break;

            case IntroTutorialState.NavInform:
                SetImageIntroStage(IntroTutorialState.NavInformOK);
                Debug.Log(
                    extraIntroObject != null
                        ? "[이미지 인트로] NavInformOK - 대기 후 Extra 표시"
                        : "[이미지 인트로] NavInformOK - Extra가 없어 대기 후 자동 종료"
                );

                if (introFinishCoroutine != null)
                {
                    StopCoroutine(introFinishCoroutine);
                }

                introFinishCoroutine = StartCoroutine(FinishImageIntroAfterDelay());
                break;
        }
    }

    private IEnumerator FinishImageIntroAfterDelay()
    {
        if (navInformOKHoldSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(navInformOKHoldSeconds);
        }

        // 새로 추가된 Extra 안내를 NavInformOK 다음에 표시합니다.
        // Extra는 별도의 확인 입력 없이 지정 시간 동안 보여준 뒤 기존처럼 자동 종료합니다.
        if (extraIntroObject != null)
        {
            SetImageIntroStage(IntroTutorialState.Extra);
            Debug.Log("[이미지 인트로] Extra 표시");

            if (extraIntroHoldSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(extraIntroHoldSeconds);
            }
        }

        introTutorialState = IntroTutorialState.Finishing;

        float startAlpha = introPassthroughOverlayImage != null
            ? introPassthroughOverlayImage.color.a
            : 0f;

        float duration = Mathf.Max(0f, introOverlayFadeDuration);
        if (duration > 0f && introPassthroughOverlayImage != null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Color color = introPassthroughOverlayImage.color;
                color.a = Mathf.Lerp(startAlpha, 0f, t);
                introPassthroughOverlayImage.color = color;
                yield return null;
            }
        }

        CompleteImageIntroTutorial();
    }

    private void CompleteImageIntroTutorial()
    {
        introFinishCoroutine = null;
        introTutorialActive = false;
        introTutorialState = IntroTutorialState.Finished;

        SetIntroObjectVisible(gestureIntroObject, false);
        SetIntroObjectVisible(gestureOKObject, false);
        SetIntroObjectVisible(navInformObject, false);
        SetIntroObjectVisible(navInformOKObject, false);
        SetIntroObjectVisible(extraIntroObject, false);

        if (introPassthroughOverlayImage != null)
        {
            Color color = introPassthroughOverlayImage.color;
            color.a = 0f;
            introPassthroughOverlayImage.color = color;
            introPassthroughOverlayImage.raycastTarget = false;
        }

        if (introPassthroughOverlayObject != null)
        {
            introPassthroughOverlayObject.SetActive(false);
        }

        if (introRootObject != null)
        {
            introRootObject.SetActive(false);
        }

        if (hideDurryDuringImageIntro && durryObject != null)
        {
            durryObject.SetActive(durryWasActiveBeforeIntro || summonDurryOnStart);
        }

        if (hideNavigationDuringImageIntro)
        {
            SetNavigationBarVisible(navigationBarStartsVisible || navigationWasVisibleBeforeIntro);
        }

        Debug.Log("[이미지 인트로] 종료 - 패스스루 복원 후 게임 오프닝 시작");
        StartOpeningAfterImageIntro();
    }

    private void StartOpeningAfterImageIntro()
    {
        if (startOnboardingFlowOnStart)
        {
            BeginOnboardingFlow();
        }
        else
        {
            missionController?.UnlockMissionNoteAfterOpening();
            ShowDescription(missionNoteUnlockedDescription, missionDescriptionExpressionState, null);
        }
    }

    private void EnsureOpeningDialogueSteps()
    {
        if (openingDialogueSteps != null && openingDialogueSteps.Length > 0)
        {
            for (int index = 0; index < openingDialogueSteps.Length; index++)
            {
                if (openingDialogueSteps[index] == null)
                {
                    openingDialogueSteps[index] = new DurryDialogueStep();
                }

                openingDialogueSteps[index].expressionState =
                    NormalizeLegacyExpressionName(
                        openingDialogueSteps[index].expressionState
                    );
            }

            return;
        }

        int textCount = openingSpeechSteps != null
            ? openingSpeechSteps.Length
            : 0;
        int expressionCount = openingExpressionStates != null
            ? openingExpressionStates.Length
            : 0;

        int count = Mathf.Max(textCount, expressionCount);
        if (count <= 0)
        {
            return;
        }

        openingDialogueSteps = new DurryDialogueStep[count];

        for (int index = 0; index < count; index++)
        {
            string text = index < textCount
                ? openingSpeechSteps[index]
                : string.Empty;

            string expression = index < expressionCount
                ? openingExpressionStates[index]
                : neutralDialogueExpressionState;

            AudioClip legacyVoice = openingVoiceClips != null &&
                                    index < openingVoiceClips.Length
                ? openingVoiceClips[index]
                : null;

            openingDialogueSteps[index] = new DurryDialogueStep(
                text,
                NormalizeLegacyExpressionName(expression)
            )
            {
                voiceClip = legacyVoice
            };
        }

        Debug.Log(
            "[더리 오프닝] 기존 대사/표정 배열을 " +
            "대사+표정 한 세트 구조로 자동 변환했습니다."
        );
    }

    private void BeginOnboardingFlow()
    {
        onboardingActive = true;
        onboardingState = OnboardingState.IntroSpeech;
        openingSpeechIndex = 0;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
        }

        ShowCurrentOpeningSpeech();
    }

    private void ShowCurrentOpeningSpeech()
    {
        EnsureOpeningDialogueSteps();

        if (openingDialogueSteps == null || openingDialogueSteps.Length == 0)
        {
            StartFirstMissionDescription();
            return;
        }

        openingSpeechIndex = Mathf.Clamp(
            openingSpeechIndex,
            0,
            openingDialogueSteps.Length - 1
        );

        DurryDialogueStep step = openingDialogueSteps[openingSpeechIndex];
        if (step == null)
        {
            Debug.LogWarning($"[더리 오프닝] Element {openingSpeechIndex}가 비어 있습니다.");
            return;
        }

        AudioClip openingClip = step.voiceClip != null
            ? step.voiceClip
            : GetOpeningVoiceClip(openingSpeechIndex);

        // 대사와 표정을 같은 메서드 호출에서 동시에 적용합니다.
        // 별도 배열 인덱스를 참조하지 않으므로 서로 어긋날 수 없습니다.
        ShowSpeech(
            step.text,
            openingClip,
            step.expressionState
        );
    }

    private bool TryAdvanceOpeningSpeech()
    {
        EnsureOpeningDialogueSteps();

        if (openingDialogueSteps == null || openingDialogueSteps.Length == 0)
        {
            return false;
        }

        int nextIndex = openingSpeechIndex + 1;
        if (nextIndex >= openingDialogueSteps.Length)
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
        }
    }

    private void StartFirstMissionDescription()
    {
        onboardingState = OnboardingState.MissionDescription;
        SetScanZoneVisible(false);
        ShowDescription(missionNoteUnlockedDescription, missionDescriptionExpressionState, null);

        // 자동 상황 표정을 끈 경우에만 인스펙터의 수동 표정을 사용합니다.
        // 자동 상황 표정이 켜져 있으면 ShowDescription()이 문구에 맞는 표정을 선택합니다.
        if (!autoSelectExpressionFromDialogue)
        {
            PlayDurryExpression(missionDescriptionExpressionState);
        }
    }

    private void ConfirmFirstMissionDescription()
    {
        HideDialoguePanels();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController == null)
        {
            ShowDescription("AI 미션 컨트롤러가 연결되지 않았어!", failureExpressionState, null);
            Debug.LogError("[DustinyDemoFlow] DustinyMissionController가 연결되지 않았습니다.");
            return;
        }

        onboardingActive = false;
        onboardingState = OnboardingState.Finished;
        missionController.UnlockMissionNoteAfterOpening();
        PlayDurryExpression(idleExpressionState);
    }

    public void ShowDescriptionMessage(string message)
    {
        ShowDescription(message, null, null);
    }

    /// <summary>
    /// 대사와 표정을 명시적으로 한 세트로 표시합니다.
    /// MissionController에서도 키워드 추측 대신 이 오버로드를 사용하면 정확히 고정됩니다.
    /// </summary>
    public void ShowDescriptionMessage(string message, string expressionState)
    {
        ShowDescription(message, expressionState, null);
    }

    public void ShowSpeechMessage(string message)
    {
        ShowSpeech(message, ResolveVoiceForMessage(message), null);
    }

    /// <summary>
    /// 대사와 표정을 명시적으로 한 세트로 표시합니다.
    /// </summary>
    public void ShowSpeechMessage(string message, string expressionState)
    {
        ShowSpeech(message, ResolveVoiceForMessage(message), expressionState);
    }

    private void ShowSpeech(string message)
    {
        ShowSpeech(message, ResolveVoiceForMessage(message), null);
    }

    private void ShowSpeech(string message, AudioClip voiceClip)
    {
        ShowSpeech(message, voiceClip, null);
    }

    private void ShowSpeech(
        string message,
        AudioClip voiceClip,
        string explicitExpressionState
    )
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

        if (!string.IsNullOrWhiteSpace(explicitExpressionState))
        {
            PlayDurryExpression(explicitExpressionState);
        }
        else
        {
            ApplySituationExpressionForMessage(message);
        }

        PlayDurryVoice(voiceClip, message);
    }

    private void ShowDescription(string message)
    {
        ShowDescription(message, null, null);
    }

    private void ShowDescription(
        string message,
        string explicitExpressionState,
        AudioClip explicitVoiceClip
    )
    {
        ResolveDialogueReferences();

        // 검정 디밍은 모든 상황에서 사용하지 않습니다.
        currentDescriptionUsesDim = false;
        SetSpeechVisible(false);
        SetDescriptionVisible(true);

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

        if (!string.IsNullOrWhiteSpace(explicitExpressionState))
        {
            PlayDurryExpression(explicitExpressionState);
        }
        else
        {
            ApplySituationExpressionForMessage(message);
        }

        AudioClip resolvedVoice = explicitVoiceClip != null
            ? explicitVoiceClip
            : ResolveVoiceForMessage(message);

        PlayDurryVoice(resolvedVoice, message);
    }

    public void HideDialoguePanels()
    {
        SetSpeechVisible(false);
        SetDescriptionVisible(false);

        // 안내가 끝난 뒤 Focused 같은 표정이 계속 남지 않도록 기본 Idle로 복귀합니다.
        // 오프닝 중에는 다음 대사의 표정이 바로 재생되므로 여기서 강제로 덮어쓰지 않습니다.
        if (returnToIdleWhenDialogueHidden && !onboardingActive)
        {
            PlayDurryExpression(idleExpressionState);
        }
    }

    private void ApplySituationExpressionForMessage(string message)
    {
        if (!autoSelectExpressionFromDialogue)
        {
            return;
        }

        // 오프닝 대사는 openingExpressionStates 배열이 정확한 표정을 담당합니다.
        if (onboardingActive && onboardingState == OnboardingState.IntroSpeech)
        {
            return;
        }

        PlayDurryExpression(ResolveExpressionForMessage(message));
    }

    private string ResolveExpressionForMessage(string message)
    {
        switch (ResolveDialogueMood(message))
        {
            case DurryDialogueMood.Scanning:
                return scanningExpressionState;

            case DurryDialogueMood.Detected:
                return detectedExpressionState;

            case DurryDialogueMood.CleaningRequest:
                return cleaningRequestExpressionState;

            case DurryDialogueMood.Retry:
                return retryExpressionState;

            case DurryDialogueMood.Failure:
                return failureExpressionState;

            case DurryDialogueMood.Success:
                return successExpressionState;

            case DurryDialogueMood.PowerRecover:
                return powerRecoverExpressionState;

            case DurryDialogueMood.Concern:
                return concernExpressionState;

            default:
                // 미션 시작 질문, NOTE 안내, 일반 설명은 Focused가 아니라
                // 사용자를 바라보는 중립적인 Look을 사용합니다.
                return neutralDialogueExpressionState;
        }
    }

    /// <summary>
    /// 대사 한 문장을 하나의 감정으로 분류합니다.
    /// 표정과 음성이 모두 이 결과를 사용하므로 서로 다른 분위기로 재생되지 않습니다.
    /// </summary>
    private DurryDialogueMood ResolveDialogueMood(string message)
    {
        string normalized = message ?? string.Empty;

        // 초기화, 보상 없는 추가 진행처럼 사용자의 결정을 조심스럽게 묻는 대사입니다.
        if (ContainsAny(normalized,
                "초기화하시겠습니까", "정말 초기화", "보상 없이", "계속할래",
                "계속 진행할래", "취소할래", "초기화할래"))
        {
            return DurryDialogueMood.Concern;
        }

        // 탐지 실패는 치명적인 오류가 아니라 다시 해보자는 의미이므로 Confused가 자연스럽습니다.
        if (ContainsAny(normalized,
                "다시 스캔", "재스캔", "다시 시도", "다시 검사", "한 번 더",
                "탐지되지 않았", "인식되지 않았", "물건을 찾지 못",
                "정리 대상을 찾지 못", "0개"))
        {
            return DurryDialogueMood.Retry;
        }

        // 시스템 연결 문제와 실제 오류만 Sad로 처리합니다.
        if (ContainsAny(normalized,
                "연결되지", "연결을 확인", "권한", "오류", "에러",
                "실패했습니다", "컨트롤러를 찾지 못", "카메라를 찾지 못"))
        {
            return DurryDialogueMood.Failure;
        }

        // 보송력 회복은 일반 성공보다 먼저 판정하여 PowerRecover를 확실히 사용합니다.
        if (ContainsAny(normalized, "보송력") &&
            ContainsAny(normalized,
                "회복", "올라", "+1", "증가", "개선", "레벨 업", "레벨업"))
        {
            return DurryDialogueMood.PowerRecover;
        }

        if (ContainsAny(normalized,
                "오늘의 미션 완료", "추가 미션 완료", "청소 미션 1회 완료",
                "미션 완료", "보상 획득", "보상 지급", "축하해",
                "잘했어", "CR을 받", "코인을 받"))
        {
            return DurryDialogueMood.Success;
        }

        // "탐지 완료! 미션 노트에 추가할까?"는 정리 요청보다 탐지의 놀람을 우선합니다.
        if (ContainsAny(normalized,
                "탐지 완료", "물건이 탐지", "찾았어", "인식된",
                "감지 완료", "개 찾았", "발견했어"))
        {
            return DurryDialogueMood.Detected;
        }

        if (ContainsAny(normalized,
                "책상 스캔 중", "스캔 중", "확인하는 중", "검사하는 중",
                "잠시만", "카메라 준비", "분석 중", "인식 중", "탐색 중"))
        {
            return DurryDialogueMood.Scanning;
        }

        if (ContainsAny(normalized,
                "미션 노트에 추가", "정리한 뒤", "정리 대상", "정리하고",
                "정리해줘", "검사받아줘", "치워줘", "청소해줘"))
        {
            return DurryDialogueMood.CleaningRequest;
        }

        return DurryDialogueMood.Neutral;
    }

    /// <summary>
    /// 별도 DurryLaserInteraction 컴포넌트가 현재 더리 자유 상호작용을
    /// 받아도 되는 상태인지 확인합니다. 직렬화 필드는 추가하지 않습니다.
    /// </summary>
    public bool CanReceiveFreeDurryInteraction()
    {
        if (introTutorialActive ||
            onboardingActive ||
            pageRootOpen ||
            awaitingResetConfirmation ||
            IsDialoguePanelVisible())
        {
            return false;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        return missionController == null ||
               (!missionController.IsAwaitingMissionStart &&
                !missionController.IsAwaitingMissionConfirmation);
    }

    /// <summary>
    /// 더리를 누른 검지 핀치가 같은 프레임의 전역 확인 입력으로 처리되지 않게 합니다.
    /// </summary>
    public void SuppressGlobalConfirmInput(float seconds = 0.25f)
    {
        float duration = Mathf.Max(confirmInputCooldown, seconds);
        suppressMissionPinchUntil = Mathf.Max(
            suppressMissionPinchUntil,
            Time.unscaledTime + duration
        );
        lastConfirmInputTime = Time.unscaledTime;
    }

    /// <summary>
    /// 별도 더리 상호작용 컴포넌트에서 대사, 표정, 음성을 한 세트로 표시합니다.
    /// </summary>
    public void ShowDurryInteractionMessage(
        string message,
        string expressionState,
        AudioClip voiceClip
    )
    {
        ShowSpeech(message, voiceClip, expressionState);
    }

    public bool IsDialoguePanelVisible()
    {
        bool speechVisible = speechBubbleObject != null
            ? speechBubbleObject.activeSelf
            : speechText != null && speechText.gameObject.activeSelf;

        bool descriptionVisible = descriptionObject != null
            ? descriptionObject.activeSelf
            : descriptionText != null && descriptionText.gameObject.activeSelf;

        return speechVisible || descriptionVisible;
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

        SetDescriptionDimVisible(visible && currentDescriptionUsesDim);
    }

    private void SetupDescriptionDimOverlay()
    {
        // 사용자가 모든 검정 오버레이 제거를 요청했으므로 새 오버레이를 절대 만들지 않습니다.
        autoCreateDescriptionDimOverlay = false;
        currentDescriptionUsesDim = false;

        if (descriptionDimOverlayObject == null && descriptionDimOverlayImage != null)
        {
            descriptionDimOverlayObject = descriptionDimOverlayImage.gameObject;
        }

        if (descriptionDimOverlayImage == null && descriptionDimOverlayObject != null)
        {
            descriptionDimOverlayImage = descriptionDimOverlayObject.GetComponent<Image>();
        }

        if (descriptionDimOverlayImage != null)
        {
            Color color = descriptionDimOverlayImage.color;
            color.a = 0f;
            descriptionDimOverlayImage.color = color;
            descriptionDimOverlayImage.raycastTarget = false;
        }

        if (descriptionDimOverlayObject != null)
        {
            descriptionDimOverlayObject.SetActive(false);
        }
    }

    private void SetDescriptionDimVisible(bool visible)
    {
        // visible 값과 관계없이 항상 꺼 둡니다.
        if (descriptionDimOverlayImage != null)
        {
            Color color = descriptionDimOverlayImage.color;
            color.a = 0f;
            descriptionDimOverlayImage.color = color;
            descriptionDimOverlayImage.raycastTarget = false;
        }

        if (descriptionDimOverlayObject != null)
        {
            descriptionDimOverlayObject.SetActive(false);
        }
    }

    private void DisableAllKnownDimOverlays()
    {
        disableAllBlackDimOverlays = true;
        SetupDescriptionDimOverlay();

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        Transform[] children = searchRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == null)
            {
                continue;
            }

            string lowerName = child.name.ToLowerInvariant();
            bool isKnownDim =
                lowerName.Contains("descriptiondim") ||
                lowerName.Contains("scandimoverlay") ||
                lowerName == "dimoverlay" ||
                lowerName == "blackdim";

            if (!isKnownDim)
            {
                continue;
            }

            Image image = child.GetComponent<Image>();
            if (image != null)
            {
                Color color = image.color;
                color.a = 0f;
                image.color = color;
                image.raycastTarget = false;
            }

            child.gameObject.SetActive(false);
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

    #endregion

    #region Mission Flow

    private void OnPressA()
    {
        if (introTutorialActive)
        {
            AdvanceImageIntroTutorial();
            return;
        }

        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        missionController?.RequestOpenMissionNote();
    }

    /// <summary>
    /// Interaction SDK의 공용 [확인] 버튼 OnClick에 연결합니다.
    /// 오른손 검지 핀치도 이 메서드를 호출하므로 두 입력의 역할이 같습니다.
    /// </summary>
    public void OnConfirmButtonPressed()
    {
        // 이미지 인트로에서는 검지 핀치만 받아 4장의 안내 이미지를 순서대로 진행합니다.
        if (introTutorialActive)
        {
            if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
            {
                return;
            }

            lastConfirmInputTime = Time.unscaledTime;
            PlayYesSfx();
            AdvanceImageIntroTutorial();
            return;
        }

        // A page button (especially X) uses the same physical index pinch.
        // Never let that click also become a global mission confirmation.
        if (IsMissionPinchSuppressedByPageUI() || pageRootOpen)
        {
            return;
        }

        // 검지 핀치와 UI [확인] 버튼이 같은 프레임에 함께 들어와도 한 번만 처리합니다.
        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;
        PlayYesSfx();

        if (awaitingResetConfirmation)
        {
            ConfirmResetAllProgress();
            return;
        }

        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        // NOTE를 눌러 나타난 "오늘의 미션을 진행할래?" 화면에서는
        // 검지 핀치가 최초 스캔을 시작합니다.
        if (missionController != null && missionController.IsAwaitingMissionStart)
        {
            missionController.ConfirmMissionStartFromPrompt();
            return;
        }

        // 탐지 결과 확인 화면에서는 현재 결과를 미션 노트에 추가합니다.
        if (missionController != null && missionController.IsAwaitingMissionConfirmation)
        {
            missionController.ConfirmPendingMissionScan();
            return;
        }

        // 그 외의 말풍선/디스크립션은 검지 핀치 한 번으로 닫습니다.
        if (IsDialoguePanelVisible())
        {
            HideDialoguePanels();
            return;
        }

        missionController?.HandleMissionConfirmPressed();
    }

    /// <summary>
    /// 오른손 중지 핀치의 공용 거절(X) 입력입니다.
    /// 우선순위:
    /// 1) 열린 NOTE/SHOP/MYPAGE/MENU 닫기
    /// 2) "오늘의 미션을 실행할래?" 질문 취소
    /// 3) 탐지 결과 거절 후 다시 스캔
    /// 4) 그 외 말풍선/디스크립션 닫기
    /// </summary>
    public void OnRejectButtonPressed()
    {
        if (introTutorialActive)
        {
            return;
        }

        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;
        PlayNoSfx();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (awaitingResetConfirmation)
        {
            CancelResetAllProgress();
            return;
        }

        // 열린 페이지는 어떤 종류든 중지 핀치로 닫습니다.
        if (pageRootOpen)
        {
            StopDurryVoicePlayback();
            CloseBigNote();
            return;
        }

        // 미션 시작 질문과 탐지 결과 질문은 컨트롤러가 상태를 안전하게 되돌립니다.
        if (missionController != null &&
            (missionController.IsAwaitingMissionStart ||
             missionController.IsAwaitingMissionConfirmation))
        {
            StopDurryVoicePlayback();
            missionController.HandleMissionRejectPressed();
            return;
        }

        // 일반 안내 문구에서도 중지는 항상 X이므로 화면과 음성을 함께 닫습니다.
        if (IsDialoguePanelVisible())
        {
            StopDurryVoicePlayback();
            HideDialoguePanels();
        }
    }

    /// <summary>
    /// 레거시 호환용입니다. 탐지 결과 확인 화면에서 오른손 중지 핀치로
    /// 현재 결과를 거절하고 미션 생성용 스캔을 다시 시작합니다.
    /// </summary>
    public void OnRetryMissionScanPinchPressed()
    {
        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
        {
            return;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController == null || !missionController.IsAwaitingMissionConfirmation)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;
        PlayNoSfx();
        missionController.HandleMissionRetryPinchPressed();
    }

    /// <summary>
    /// 오른손 약지 핀치의 검사 재스캔 입력입니다.
    /// 활성 미션에서 물건 정리 결과를 다시 검사합니다.
    /// </summary>
    public void OnMissionScanRingPinchPressed()
    {
        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown || onboardingActive)
        {
            return;
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (!IsNotePageOpen ||
            missionController == null ||
            !missionController.CanHandleRingPinchScan)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;
        missionController.HandleMissionScanRingPinchPressed();
    }

    /// <summary>
    /// 왼손 약지 핀치 디버그 입력입니다.
    /// 오늘 미션 진행도, 현재 미션 카드, 보송력, 크레딧,
    /// 구매 아이템과 장착 아이템을 즉시 초기화합니다.
    /// </summary>
    public void OnResetAllProgressLeftRingPinchPressed()
    {
        if (!leftRingPinchResetsMissionCleanlinessAndCredit)
        {
            return;
        }

        RequestResetAllProgress();
    }

    /// <summary>
    /// 메뉴의 초기화 버튼 OnClick에 연결하는 메서드입니다.
    /// 즉시 데이터를 지우지 않고 확인 문구를 먼저 표시합니다.
    /// </summary>
    public void RequestResetAllProgress()
    {
        if (awaitingResetConfirmation ||
            Time.unscaledTime - lastResetInputTime < resetInputCooldown)
        {
            return;
        }

        lastResetInputTime = Time.unscaledTime;
        lastConfirmInputTime = Time.unscaledTime;

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController == null)
        {
            ShowDescriptionMessage("초기화할 미션 컨트롤러를 찾지 못했어!", failureExpressionState);
            Debug.LogError("[DustinyDemoFlow] 초기화 요청 실패: DustinyMissionController가 없습니다.");
            return;
        }

        // 메뉴의 초기화 버튼을 누른 동일한 검지 핀치가 즉시 확인으로도 처리되지 않도록
        // 페이지를 닫고 확인 대기 상태로 전환합니다.
        CloseBigNote();
        awaitingResetConfirmation = true;

        ShowDescriptionMessage(
            "모든 진행 상황을 초기화하시겠습니까?\n" +
            "오늘의 미션, 보송력, 구매/착용 아이템이 초기화되고\n" +
            "보유 코인은 기본값 100 CR로 돌아갑니다.\n" +
            "검지 핀치: 초기화 · 중지 핀치: 취소",
            concernExpressionState
        );

        // 위 문구는 ResolveDialogueMood()에서 Concern으로 분류되어
        // Worried 표정과 조심스러운 음성이 함께 선택됩니다.
        Debug.Log("[Dustiny Reset] 초기화 확인을 기다립니다.");
    }

    private void ConfirmResetAllProgress()
    {
        if (!awaitingResetConfirmation)
        {
            return;
        }

        awaitingResetConfirmation = false;
        StopDurryVoicePlayback();
        HideDialoguePanels();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController == null)
        {
            ShowDescriptionMessage("초기화할 미션 컨트롤러를 찾지 못했어!", failureExpressionState);
            return;
        }

        // 확인을 받은 뒤에만 오프닝과 저장 데이터를 실제로 초기화합니다.
        onboardingActive = false;
        onboardingState = OnboardingState.Finished;

        ShopInventoryManager.Instance?.ResetAllItemState();
        missionController.ResetMissionCleanlinessAndCreditFromLeftRingPinch();
    }

    private void CancelResetAllProgress()
    {
        if (!awaitingResetConfirmation)
        {
            return;
        }

        awaitingResetConfirmation = false;
        StopDurryVoicePlayback();
        HideDialoguePanels();
        PlayDurryExpression(idleExpressionState);
        Debug.Log("[Dustiny Reset] 사용자가 초기화를 취소했습니다.");
    }

    public void NotifyMissionCompleted()
    {
        SetScanZoneVisible(false);
        ApplyCurrentCleanlinessMaterial(force: true);
        PlayDurryExpression(successExpressionState);

        if (changeDurryColorOnMissionComplete)
        {
            RecoverDurryByCleanliness();
        }
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

    private void ResolveLeftHandIfNeeded()
    {
        if (!autoFindLeftOVRHandIfMissing || leftHand != null)
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

            if ((objectName + " " + parentName).Contains("left"))
            {
                leftHand = hand;
                return;
            }
        }
    }


    public void OnLeftPinkyPinchToggleNavigation()
    {
        if (!leftPinkyPinchTogglesNavigation ||
            Time.unscaledTime - lastLeftPinkyNavigationToggleTime < leftPinkyPinchToggleCooldown)
        {
            return;
        }

        ResolveNavigationReferences();
        if (navigationBarObject == null)
        {
            Debug.LogWarning(
                "[네비게이션 소지 핀치] Navigation Bar Object가 연결되지 않았습니다. " +
                "WaistNavCanvas를 직접 연결하세요."
            );
            return;
        }

        lastLeftPinkyNavigationToggleTime = Time.unscaledTime;
        bool willShow = !navigationBarObject.activeSelf;
        SetNavigationBarVisible(willShow);

        Debug.Log(
            willShow
                ? "[네비게이션 소지 핀치] 네비게이션 바를 열었습니다."
                : "[네비게이션 소지 핀치] 네비게이션 바를 닫았습니다."
        );
    }

    private void UpdateHandPinchInput()
    {
        ResolveRightHandIfNeeded();
        ResolveLeftHandIfNeeded();

        bool isRightTracked = rightHand != null && rightHand.IsTracked && rightHand.IsDataValid;
        bool isIndexPinching = isRightTracked &&
                               rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool isMiddlePinching = isRightTracked &&
                                rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
        bool isRingPinching = isRightTracked &&
                              rightHand.GetFingerIsPinching(OVRHand.HandFinger.Ring);

        bool isLeftTracked = leftHand != null && leftHand.IsTracked && leftHand.IsDataValid;
        bool isLeftRingPinching = isLeftTracked &&
                                  leftHand.GetFingerIsPinching(OVRHand.HandFinger.Ring);
        bool isLeftPinkyPinching = isLeftTracked &&
                                   leftHand.GetFingerIsPinching(OVRHand.HandFinger.Pinky);

        bool indexPinchDown = isIndexPinching && !wasRightIndexPinching;
        bool middlePinchDown = isMiddlePinching && !wasRightMiddlePinching;
        bool ringPinchDown = isRingPinching && !wasRightRingPinching;
        bool leftRingPinchDown = isLeftRingPinching && !wasLeftRingPinching;
        bool leftPinkyPinchDown = isLeftPinkyPinching && !wasLeftPinkyPinching;

        wasRightIndexPinching = isIndexPinching;
        wasRightMiddlePinching = isMiddlePinching;
        wasRightRingPinching = isRingPinching;
        wasLeftRingPinching = isLeftRingPinching;
        wasLeftPinkyPinching = isLeftPinkyPinching;

        if ((indexPinchDown || middlePinchDown || ringPinchDown ||
             leftRingPinchDown || leftPinkyPinchDown) &&
            missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        // 최초 이미지 인트로 중에는 오른손 검지 핀치만 허용합니다.
        // 중지/약지/왼손 초기화/네비 토글이 튜토리얼을 방해하지 않게 모두 막습니다.
        if (introTutorialActive)
        {
            if (rightIndexPinchConfirms && indexPinchDown)
            {
                OnConfirmButtonPressed();
            }

            return;
        }

        // 왼손 약지 핀치는 디버그 전체 초기화 입력이며 다른 입력보다 먼저 처리합니다.
        if (leftRingPinchResetsMissionCleanlinessAndCredit && leftRingPinchDown)
        {
            OnResetAllProgressLeftRingPinchPressed();
            return;
        }

        // 왼손 소지 핀치는 스켈레톤 없이 OVRHand 핀치 상태만으로 네비게이션을 토글합니다.
        if (leftPinkyPinchTogglesNavigation && leftPinkyPinchDown)
        {
            OnLeftPinkyPinchToggleNavigation();
            return;
        }

        // 오른손 중지는 모든 상황에서 거절(X)입니다.
        // 시작 질문 취소, 탐지 결과 거절/재스캔, 열린 창 닫기, 일반 안내 닫기를
        // 하나의 입력으로 통일합니다.
        if (rightMiddlePinchRejectsEverything && middlePinchDown)
        {
            OnRejectButtonPressed();
            return;
        }

        // 아래 두 분기는 기존 인스펙터 설정과의 호환을 위한 레거시 폴백입니다.
        if (!rightMiddlePinchRejectsEverything &&
            rightMiddlePinchClosesOpenPage &&
            middlePinchDown &&
            pageRootOpen)
        {
            PlayNoSfx();
            CloseBigNote();
            return;
        }

        if (!rightMiddlePinchRejectsEverything &&
            rightMiddlePinchRetriesMissionScan &&
            middlePinchDown &&
            missionController != null &&
            missionController.IsAwaitingMissionConfirmation)
        {
            OnRetryMissionScanPinchPressed();
            return;
        }

        // 약지 핀치는 노트 버튼의 대체 입력입니다.
        // Ready이면 최초 스캔, Active이면 검사 재스캔을 실행합니다.
        if (rightRingPinchStartsOrRescans &&
            ringPinchDown &&
            IsNotePageOpen &&
            missionController != null &&
            missionController.CanHandleRingPinchScan)
        {
            OnMissionScanRingPinchPressed();
            return;
        }

        if (rightIndexPinchConfirms && indexPinchDown)
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

    private void SetupInteractionAudio()
    {
        if (interactionSfxSource == null && autoCreateInteractionAudioSources)
        {
            interactionSfxSource = CreateChildAudioSource("Dustiny Interaction SFX Source");
        }

        if (backgroundMusicSource == null && autoCreateInteractionAudioSources)
        {
            backgroundMusicSource = CreateChildAudioSource("Dustiny Background Music Source");
        }

        // 실수로 같은 AudioSource를 두 칸에 연결한 경우 SFX가 BGM을 끊지 않도록 분리합니다.
        if (interactionSfxSource != null &&
            backgroundMusicSource == interactionSfxSource &&
            autoCreateInteractionAudioSources)
        {
            backgroundMusicSource = CreateChildAudioSource("Dustiny Background Music Source");
        }

        if (interactionSfxSource != null)
        {
            interactionSfxSource.playOnAwake = false;
            interactionSfxSource.loop = false;
            interactionSfxSource.spatialBlend = 0f;
            interactionSfxSource.volume = Mathf.Clamp01(interactionSfxVolume);
        }

        if (backgroundMusicSource == null)
        {
            return;
        }

        backgroundMusicSource.playOnAwake = false;
        backgroundMusicSource.loop = true;
        backgroundMusicSource.spatialBlend = 0f;
        backgroundMusicSource.volume = Mathf.Clamp01(backgroundMusicVolume);

        AudioClip resolvedBackgroundClip = backgroundMusicClip != null
            ? backgroundMusicClip
            : backgroundMusicSource.clip;

        if (resolvedBackgroundClip == null)
        {
            return;
        }

        if (backgroundMusicSource.clip != resolvedBackgroundClip)
        {
            backgroundMusicSource.Stop();
            backgroundMusicSource.clip = resolvedBackgroundClip;
        }

        if (playBackgroundMusicOnStart && !backgroundMusicSource.isPlaying)
        {
            backgroundMusicSource.Play();
        }
    }

    private AudioSource CreateChildAudioSource(string objectName)
    {
        Transform existingChild = transform.Find(objectName);
        if (existingChild != null)
        {
            AudioSource existingSource = existingChild.GetComponent<AudioSource>();
            if (existingSource != null)
            {
                return existingSource;
            }
        }

        GameObject audioObject = new GameObject(objectName);
        audioObject.transform.SetParent(transform, false);
        return audioObject.AddComponent<AudioSource>();
    }

    public void PlayYesSfx()
    {
        PlayInteractionSfx(yesSfxClip);
    }

    public void PlayNoSfx()
    {
        PlayInteractionSfx(noSfxClip);
    }

    private void PlayInteractionSfx(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        SetupInteractionAudio();
        if (interactionSfxSource == null)
        {
            return;
        }

        interactionSfxSource.volume = Mathf.Clamp01(interactionSfxVolume);
        interactionSfxSource.PlayOneShot(clip);
    }

    private void SetupDurryVoice()
    {
        if (durryVoiceSource == null && durryObject != null)
        {
            durryVoiceSource = durryObject.GetComponentInChildren<AudioSource>(true);
        }

        if (durryVoiceSource == null && autoCreateDurryVoiceSource && durryObject != null)
        {
            durryVoiceSource = durryObject.AddComponent<AudioSource>();
        }

        if (durryVoiceSource == null)
        {
            return;
        }

        durryVoiceSource.playOnAwake = false;
        durryVoiceSource.loop = false;
        durryVoiceSource.volume = Mathf.Clamp01(durryVoiceVolume);
        durryVoiceSource.spatialBlend = Mathf.Clamp01(durryVoiceSpatialBlend);
        durryVoiceSource.minDistance = 0.35f;
        durryVoiceSource.maxDistance = 6f;
        durryVoiceSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    public void StopDurryVoicePlayback()
    {
        if (durryVoiceSource == null)
        {
            return;
        }

        durryVoiceSource.Stop();
        durryVoiceSource.clip = null;
        lastVoiceMessage = string.Empty;
        lastVoiceMessageTime = -999f;
    }

    public void PlayDurryVoice(AudioClip clip)
    {
        PlayDurryVoice(clip, string.Empty);
    }

    private void PlayDurryVoice(AudioClip clip, string messageKey)
    {
        if (clip == null)
        {
            return;
        }

        SetupDurryVoice();
        if (durryVoiceSource == null)
        {
            return;
        }

        // 같은 텍스트가 한 프레임에 중복 호출될 때 소리가 겹치지 않게 합니다.
        if (!string.IsNullOrWhiteSpace(messageKey) &&
            messageKey == lastVoiceMessage &&
            Time.unscaledTime - lastVoiceMessageTime < 0.15f)
        {
            return;
        }

        lastVoiceMessage = messageKey ?? string.Empty;
        lastVoiceMessageTime = Time.unscaledTime;

        durryVoiceSource.volume = Mathf.Clamp01(durryVoiceVolume);
        durryVoiceSource.spatialBlend = Mathf.Clamp01(durryVoiceSpatialBlend);

        if (stopPreviousVoiceWhenNewTextAppears)
        {
            durryVoiceSource.Stop();
        }

        durryVoiceSource.clip = clip;
        durryVoiceSource.Play();
    }

    private AudioClip GetOpeningVoiceClip(int index)
    {
        if (openingVoiceClips != null &&
            index >= 0 &&
            index < openingVoiceClips.Length &&
            openingVoiceClips[index] != null)
        {
            return openingVoiceClips[index];
        }

        string expression = string.Empty;

        if (openingDialogueSteps != null &&
            index >= 0 &&
            index < openingDialogueSteps.Length &&
            openingDialogueSteps[index] != null)
        {
            expression = NormalizeLegacyExpressionName(
                openingDialogueSteps[index].expressionState
            );
        }
        else if (openingExpressionStates != null &&
                 index >= 0 &&
                 index < openingExpressionStates.Length)
        {
            expression = NormalizeLegacyExpressionName(
                openingExpressionStates[index]
            );
        }

        switch ((expression ?? string.Empty).ToLowerInvariant())
        {
            case "surprised":
                return FirstAssignedClip(voiceSpecial, voiceCheerful, voiceHappy);
            case "confused":
                return FirstAssignedClip(voiceDizzy, voiceSpecial, voiceSad);
            case "sad":
                return FirstAssignedClip(voiceTearful, voiceSad, voiceDizzy);
            case "joyful":
                return FirstAssignedClip(voiceJoy, voiceHappy, voiceCheerful);
            case "worried":
                return FirstAssignedClip(voiceTearful, voiceSad, voiceWaiting);
            case "focused":
                return FirstAssignedClip(voiceStart, voiceRequestClean, voiceWaiting);
            case "look":
                return FirstAssignedClip(voiceFindDust, voiceCheerful, voiceSpecial);
            default:
                return FirstAssignedClip(voiceCheerful, voiceHappy, voiceStart);
        }
    }

    private AudioClip ResolveVoiceForMessage(string message)
    {
        string normalized = message ?? string.Empty;

        switch (ResolveDialogueMood(normalized))
        {
            case DurryDialogueMood.Failure:
                return FirstAssignedClip(voiceSad, voiceTearful, voiceDizzy);

            case DurryDialogueMood.Retry:
                return FirstAssignedClip(voiceDizzy, voiceWaiting, voiceSad);

            case DurryDialogueMood.Scanning:
                return FirstAssignedClip(voiceWaiting, voiceDizzy, voiceStart);

            case DurryDialogueMood.Detected:
                return FirstAssignedClip(voiceFindDust, voiceCheerful, voiceSpecial);

            case DurryDialogueMood.CleaningRequest:
                return FirstAssignedClip(voiceRequestClean, voiceStart, voiceFindDust);

            case DurryDialogueMood.PowerRecover:
                return FirstAssignedClip(voiceLevelUp, voiceHappy, voiceJoy);

            case DurryDialogueMood.Success:
                if (ContainsAny(normalized, "보상", "CR", "코인"))
                {
                    return FirstAssignedClip(voiceReward, voiceGoodJob, voiceHappy);
                }

                return FirstAssignedClip(voiceGoodJob, voiceClear, voiceHappy);

            case DurryDialogueMood.Concern:
                return FirstAssignedClip(voiceWaiting, voiceSad, voiceDizzy);

            default:
                if (ContainsAny(normalized,
                        "진행할래", "미션을 시작", "NOTE를 눌러",
                        "시작해줘", "준비됐어", "확인해줘", "도와줘"))
                {
                    return FirstAssignedClip(voiceStart, voiceCheerful, voiceRequestClean);
                }

                return FirstAssignedClip(voiceCheerful, voiceHappy, voiceSpecial);
        }
    }

    private static bool ContainsAny(string source, params string[] keywords)
    {
        if (string.IsNullOrEmpty(source) || keywords == null)
        {
            return false;
        }

        foreach (string keyword in keywords)
        {
            if (!string.IsNullOrEmpty(keyword) &&
                source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static AudioClip FirstAssignedClip(params AudioClip[] clips)
    {
        if (clips == null)
        {
            return null;
        }

        foreach (AudioClip clip in clips)
        {
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private void SubscribeCleanlinessMaterialUpdates()
    {
        if (cleanlinessMaterialEventSubscribed)
        {
            return;
        }

        CleanlinessManager.OnStateChanged += HandleCleanlinessLevelChanged;
        cleanlinessMaterialEventSubscribed = true;
    }

    private void UnsubscribeCleanlinessMaterialUpdates()
    {
        if (!cleanlinessMaterialEventSubscribed)
        {
            return;
        }

        CleanlinessManager.OnStateChanged -= HandleCleanlinessLevelChanged;
        cleanlinessMaterialEventSubscribed = false;
    }

    private void HandleCleanlinessLevelChanged(int level)
    {
        ApplyDurryCleanlinessMaterial(level, force: true);
    }

    private void ApplyCurrentCleanlinessMaterial(bool force)
    {
        int level = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 1;

        ApplyDurryCleanlinessMaterial(level, force);
    }

    private static bool IsWearableTransform(Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            string lowerName = current.name.ToLowerInvariant();

            if (lowerName == "back_nametag_01" ||
                lowerName == "body_sleepwear" ||
                lowerName == "head_sleepmask" ||
                lowerName == "head_hat_hard" ||
                lowerName == "head_cone" ||
                lowerName == "head_cap_baseball")
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ResolveDurryCleanlinessRenderers()
    {
        if (durryCleanlinessRenderers != null && durryCleanlinessRenderers.Length > 0)
        {
            return;
        }

        if (!autoFindDurryCleanlinessRenderers || durryObject == null)
        {
            return;
        }

        // 애니메이션 바디는 SkinnedMeshRenderer가 실제로 그립니다.
        // 같은 오브젝트에 실수로 추가된 일반 MeshRenderer는 보송력 대상에서 제외합니다.
        SkinnedMeshRenderer[] allRenderers =
            durryObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        List<Renderer> bodyRenderers = new List<Renderer>();

        foreach (SkinnedMeshRenderer renderer in allRenderers)
        {
            if (renderer == null || IsWearableTransform(renderer.transform))
            {
                continue;
            }

            string lowerName = renderer.name.ToLowerInvariant();
            string parentName = renderer.transform.parent != null
                ? renderer.transform.parent.name.ToLowerInvariant()
                : string.Empty;
            string combined = lowerName + " " + parentName;

            bool looksLikeBody =
                combined.Contains("body_geo") ||
                combined.Contains("durry_body") ||
                combined.Contains("durrybody") ||
                combined.Contains("body");

            bool looksLikeFacePart =
                combined.Contains("eye") ||
                combined.Contains("mouth") ||
                combined.Contains("joint") ||
                combined.Contains("glasses") ||
                combined.Contains("mask") ||
                combined.Contains("hat") ||
                combined.Contains("cap");

            if (looksLikeBody && !looksLikeFacePart)
            {
                bodyRenderers.Add(renderer);
            }
        }

        if (bodyRenderers.Count == 0 && allRenderers.Length > 0)
        {
            bodyRenderers.Add(allRenderers[0]);
        }

        durryCleanlinessRenderers = bodyRenderers.ToArray();
    }

    private void DisableDuplicateBodyMeshRenderers()
    {
        if (!disableDuplicateBodyMeshRenderer || durryObject == null)
        {
            return;
        }

        MeshRenderer[] meshRenderers =
            durryObject.GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer meshRenderer in meshRenderers)
        {
            if (meshRenderer == null)
            {
                continue;
            }

            SkinnedMeshRenderer skinnedRenderer =
                meshRenderer.GetComponent<SkinnedMeshRenderer>();

            if (skinnedRenderer == null)
            {
                continue;
            }

            meshRenderer.enabled = false;

            if (logCleanlinessMaterialChanges)
            {
                Debug.LogWarning(
                    $"[더리 보송력 머터리얼] '{meshRenderer.name}'에 SkinnedMeshRenderer와 " +
                    "MeshRenderer가 동시에 있어 일반 MeshRenderer를 비활성화했습니다."
                );
            }
        }
    }

    private void ApplyDurryCleanlinessMaterial(int level, bool force)
    {
        level = Mathf.Clamp(level, 0, 4);

        if (!force && level == lastAppliedCleanlinessLevel)
        {
            return;
        }

        ResolveDurryCleanlinessRenderers();

        if (durryCleanlinessMaterials == null ||
            level >= durryCleanlinessMaterials.Length ||
            durryCleanlinessMaterials[level] == null)
        {
            lastAppliedCleanlinessLevel = level;
            if (logCleanlinessMaterialChanges)
            {
                Debug.LogWarning(
                    $"[더리 보송력 머터리얼] L{level} 머터리얼이 비어 있습니다. " +
                    "DustinyDemoFlow > Durry Cleanliness Materials의 Element 0~4를 연결하세요."
                );
            }
            return;
        }

        Material targetMaterial = durryCleanlinessMaterials[level];
        bool changedAny = false;

        if (durryCleanlinessRenderers != null)
        {
            foreach (Renderer renderer in durryCleanlinessRenderers)
            {
                if (renderer == null || IsWearableTransform(renderer.transform))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = targetMaterial;
                    changedAny = true;
                    continue;
                }

                if (replaceAllDurryBodyMaterialSlots)
                {
                    for (int index = 0; index < materials.Length; index++)
                    {
                        materials[index] = targetMaterial;
                    }
                }
                else
                {
                    int slot = Mathf.Clamp(
                        durryCleanlinessMaterialSlot,
                        0,
                        materials.Length - 1
                    );
                    materials[slot] = targetMaterial;
                }

                renderer.sharedMaterials = materials;
                changedAny = true;
            }
        }

        lastAppliedCleanlinessLevel = level;

        if (changedAny && logCleanlinessMaterialChanges)
        {
            Debug.Log($"[더리 보송력 머터리얼] L{level} → {targetMaterial.name}");
        }
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
        DisableDuplicateBodyMeshRenderers();

        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);
        durryRuntimeMaterials = new Material[durryRenderers.Length];

        for (int index = 0; index < durryRenderers.Length; index++)
        {
            Renderer renderer = durryRenderers[index];
            if (renderer == null || IsWearableTransform(renderer.transform))
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
        EnsureOpeningDialogueSteps();

        if (openingDialogueSteps == null ||
            index < 0 ||
            index >= openingDialogueSteps.Length ||
            openingDialogueSteps[index] == null)
        {
            return;
        }

        PlayDurryExpression(openingDialogueSteps[index].expressionState);
    }

    public void PlayDurryExpression(string expressionName)
    {
        expressionName = NormalizeLegacyExpressionName(expressionName);

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

        // 이전 프레임에 남은 Focused 등의 Trigger가 뒤늦게 실행되어
        // 현재 표정을 덮어쓰지 않도록 표정 Trigger를 먼저 정리합니다.
        ResetKnownExpressionTriggers();

        int shortStateHash = Animator.StringToHash(expressionName);
        int fullStateHash = Animator.StringToHash("Base Layer." + expressionName);

        // 대사와 표정이 한 박자 어긋나는 가장 흔한 원인은
        // Any State 전환에 남아 있는 Exit Time입니다.
        // 이 옵션이 켜져 있으면 전환 대기를 거치지 않고 상태를 즉시 재생합니다.
        if (forceImmediateExpressionSwitch)
        {
            if (durryAnimator.HasState(0, fullStateHash))
            {
                durryAnimator.CrossFadeInFixedTime(
                    fullStateHash,
                    expressionCrossFadeDuration,
                    0
                );
                return;
            }

            if (durryAnimator.HasState(0, shortStateHash))
            {
                durryAnimator.CrossFadeInFixedTime(
                    shortStateHash,
                    expressionCrossFadeDuration,
                    0
                );
                return;
            }
        }

        int parameterHash = FindAnimatorTriggerHash(expressionName);
        if (preferAnimatorTriggers && parameterHash != 0)
        {
            durryAnimator.SetTrigger(parameterHash);
            return;
        }

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

        // 같은 이름의 Trigger가 존재하는 경우의 마지막 폴백입니다.
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

    private void ResetKnownExpressionTriggers()
    {
        if (durryAnimator == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in durryAnimator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Trigger ||
                !IsKnownExpressionName(parameter.name))
            {
                continue;
            }

            durryAnimator.ResetTrigger(parameter.nameHash);
        }
    }

    private bool IsKnownExpressionName(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string normalizedCandidate = NormalizeLegacyExpressionName(candidate);

        if (StringEqualsIgnoreCase(normalizedCandidate, idleExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, missionDescriptionExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, neutralDialogueExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, scanningExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, detectedExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, cleaningRequestExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, retryExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, failureExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, successExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, powerRecoverExpressionState) ||
            StringEqualsIgnoreCase(normalizedCandidate, concernExpressionState))
        {
            return true;
        }

        if (openingDialogueSteps != null)
        {
            foreach (DurryDialogueStep step in openingDialogueSteps)
            {
                if (step != null &&
                    StringEqualsIgnoreCase(
                        normalizedCandidate,
                        step.expressionState
                    ))
                {
                    return true;
                }
            }
        }

        return StringEqualsIgnoreCase(normalizedCandidate, "Greet_Short") ||
               StringEqualsIgnoreCase(normalizedCandidate, "PowerRecover") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Sad") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Look") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Joyful") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Surprised") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Worried") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Confused") ||
               StringEqualsIgnoreCase(normalizedCandidate, "Focused") ||
               StringEqualsIgnoreCase(normalizedCandidate, "DurryIdleAni");
    }

    private static bool StringEqualsIgnoreCase(string left, string right)
    {
        return string.Equals(
            NormalizeLegacyExpressionName(left),
            NormalizeLegacyExpressionName(right),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string NormalizeLegacyExpressionName(string expressionName)
    {
        return string.Equals(
            expressionName,
            "Woried",
            System.StringComparison.OrdinalIgnoreCase
        )
            ? "Worried"
            : expressionName;
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

    public void SetScanZoneVisible(bool visible)
    {
        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(visible);
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
        scanZoneObject.SetActive(false);
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

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        string path = target.name;
        Transform parent = target.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
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
