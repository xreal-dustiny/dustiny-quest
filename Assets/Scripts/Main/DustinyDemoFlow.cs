// VERSION: 82_GAME_BUTTON_DOLL_SCENE_2026-08-24
// Baseline: 80_SHORT_MISSION_RESULT_COPY_2026-08-17 (latest cumulative stable flow).
// Tutorial: OnboardingOKNo -> first pinch result -> release -> second pinch -> OnboardingNoDone -> NavInform -> NavInformOK.
// Tutorial overlay: full-camera Screen Space - Camera overlay.
// Navigation: NavInform keeps the bar hidden; it appears only while the player is actually looking down.
// Navigation override: synchronized with DustinyWaistNavFollow and released with an immediate gaze refresh.
// Interaction: left-ring reset removed. Menu Reset asks for confirmation; Menu Exit quits the app.
// Mission start: NOTE only -> friendly prompt -> first index pinch accepts -> second index pinch at scan location starts actual scan.
// Prologue: finishing Durry dialogue only unlocks NOTE; mission prompt appears only when the user presses NOTE.
// Scan result: detected objects are auto-added; legacy "미션 노트에 추가할까?" confirmation is suppressed.
// Reset copy: "초기화 할래? 다시 복구할 수 없어." -> "초기화가 완료되었어!"
// NOTE glow: completely removed by user request.
// Durry interaction: separate DurryLaserInteraction short-touch reactions only; no user grab/move.
// SideTags behavior remains unchanged.
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

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
        Finished
    }

    private enum IntroTutorialState
    {
        OnboardingOKNo,
        OnboardingOK,
        OnboardingNo,
        OnboardingNoDone,
        NavInform,
        NavInformOK,
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

    public static bool ResumeIdleAfterMiniGame;

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

    [Header("First Launch Onboarding / Navigation Intro")]
    [Tooltip("씬 시작 시 OnboardingOKNo → 첫 핀치 결과(OK/No) → OnboardingNoDone → NavInform → NavInformOK 순서의 안내를 표시합니다.")]
    public bool showImageIntroBeforeOpening = true;

    [Tooltip("OnboardingOKNo, OnboardingOK, OnboardingNo, OnboardingNoDone, NavInform, NavInformOK를 묶은 선택적 부모입니다. 비워두어도 자동 탐색합니다.")]
    public GameObject introRootObject;

    [FormerlySerializedAs("onboardingObject")]
    [FormerlySerializedAs("gestureIntroObject")]
    [Tooltip("첫 화면입니다. 검지 또는 중지 핀치를 하나씩 연습합니다.")]
    public GameObject onboardingOKNoObject;

    [FormerlySerializedAs("gestureOKObject")]
    [Tooltip("검지 핀치를 먼저 성공했을 때 표시합니다. 이후 손을 완전히 놓고 중지 핀치를 기다립니다.")]
    public GameObject onboardingOKObject;

    [Tooltip("중지 핀치를 먼저 성공했을 때 표시합니다. 이후 손을 완전히 놓고 검지 핀치를 기다립니다.")]
    public GameObject onboardingNoObject;

    [Tooltip("검지와 중지 핀치를 모두 완료했을 때 표시합니다. 이후 NavInform으로 자동 전환합니다.")]
    public GameObject onboardingNoDoneObject;

    [Tooltip("OnboardingNoDone 이후 표시할 네비게이션 안내 이미지입니다.")]
    public GameObject navInformObject;

    [Tooltip("사용자가 아래를 바라봐 네비게이션을 확인하면 표시할 네비게이션 완료 이미지입니다.")]
    public GameObject navInformOKObject;

    [Header("Fullscreen Tutorial Overlay")]
    [Tooltip("튜토리얼 동안 CenterEye 카메라 전체를 덮는 Screen Space - Camera Canvas입니다. 비워두면 자동 생성합니다.")]
    public Canvas introFullscreenOverlayCanvas;

    [Tooltip("전체 화면 필터 이미지입니다. 기존 오브젝트를 연결해도 실행 시 Fullscreen Canvas 아래로 옮겨 전체 화면으로 Stretch합니다.")]
    public GameObject introPassthroughOverlayObject;
    public Image introPassthroughOverlayImage;

    [Tooltip("켜면 WorldCanvas 크기와 무관하게 CenterEye 카메라 전체를 덮는 Screen Space - Camera 오버레이를 사용합니다.")]
    public bool useFullscreenCameraOverlay = true;
    public bool autoCreateIntroPassthroughOverlay = true;

    [Min(0.1f)] public float introOverlayPlaneDistance = 5f;
    public int introOverlaySortingOrder = -1000;

    [Range(0f, 1f)]
    public float introPassthroughOverlayOpacity = 0.72f;

    [FormerlySerializedAs("gestureSuccessHoldSeconds")]
    [Tooltip("검지와 중지를 모두 완료한 OnboardingNoDone 화면을 유지하는 시간입니다.")]
    [Min(0f)] public float onboardingDoneHoldSeconds = 1.5f;

    [FormerlySerializedAs("gestureSuccessFadeDuration")]
    [Tooltip("OnboardingNoDone에서 NavInform으로 전환할 때 사용하는 페이드 시간입니다.")]
    [Min(0f)] public float onboardingTransitionFadeDuration = 0.25f;

    [Tooltip("네비게이션 튜토리얼 완료로 판단할 고개 숙임의 Forward Y 기준값입니다.")]
    [Range(-1f, 0f)] public float navigationLookDownForwardYThreshold = -0.35f;

    [Tooltip("고개를 아래로 향한 상태를 유지해야 하는 시간입니다.")]
    [Min(0f)] public float navigationLookDownHoldSeconds = 0.35f;

    [Tooltip("NavInformOK가 나타난 뒤 인트로를 종료하기까지 기다리는 시간입니다.")]
    [Min(0f)] public float navInformOKHoldSeconds = 2f;

    [Tooltip("마지막 안내 이미지 대기 후 패스스루 오버레이가 사라지는 페이드 시간입니다.")]
    [Min(0f)] public float introOverlayFadeDuration = 0.30f;

    [Tooltip("이미지 인트로 동안에도 더리를 바로 표시합니다. 켜면 튜토리얼 중에만 숨깁니다.")]
    public bool hideDurryDuringImageIntro = true;

    [Tooltip("이미지 인트로 동안 네비게이션 가시성을 튜토리얼이 직접 제어합니다.")]
    public bool hideNavigationDuringImageIntro = true;

    [Header("Dialogue / Description")]
    public bool startOnboardingFlowOnStart = true;
    public float dialogueAdvanceCooldown = 0.25f;

    public GameObject speechBubbleObject;
    public TMP_Text speechText;
    [Tooltip("말풍선 대사 전용 폰트. 비워두면 KyoboHandwriting TMP를 자동 로드합니다.")]
    public TMP_FontAsset speechBubbleFont;
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
        "네 도움이 필요해!",
        "미션 노트를 눌러서 미션을 수행해줘!\n보송력이 필요해."
    };

    [Header("Opening Complete -> Mission")]
    [TextArea(2, 5)]
    public string missionNoteUnlockedDescription =
        "미션 노트를 눌러서 미션을 수행해줘!\n보송력이 필요해.";

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

    [Header("Dialogue Follow Durry (World Space)")]
    [Tooltip("켜면 말풍선/디스크립션을 시야 고정 Canvas가 아니라 더리 바로 아래 월드 공간에 붙입니다.")]
    public bool pinDialogueBelowDurry = true;

    [Tooltip("더리 바운즈 하단에서 얼마나 아래로 내릴지입니다.")]
    public float dialogueBelowDurryGap = 0.20f;

    [Tooltip("더리보다 사용자 쪽으로 얼마나 당길지입니다. 캐릭터에 파고드는 것을 줄입니다.")]
    public float dialogueTowardUserOffset = 0.36f;

    [Tooltip("대화 전용 World Space Canvas 스케일입니다. 기존 WorldCanvas(0.003)과 맞춥니다.")]
    public float dialogueWorldScale = 0.003f;

    public Vector2 dialogueFollowCanvasSize = new Vector2(1920f, 1080f);

    [Tooltip("대화 UI가 사용자를 바라보도록 회전합니다.")]
    public bool dialogueFaceUser = true;

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

    [Tooltip("고개 숙임에 따라 네비게이션 표시를 제어하는 DustinyWaistNavFollow입니다. 튜토리얼 중 visibility override를 사용합니다.")]
    public DustinyWaistNavFollow waistNavFollow;
    public bool autoFindWaistNavFollow = true;

    [Header("Navigation Buttons")]
    public Button durryNoteButton;
    public Button shopButton;
    public Button myPageButton;
    public Button menuButton;

    [Tooltip("NavigationBar의 Game Button입니다. 비워두면 Game Button/GameButton 이름으로 자동 탐색합니다.")]
    public Button gameButton;

    [Header("Note Button Glow (Dirty Hint)")]
    [Tooltip("보송력(텍스처) 레벨이 0~2일 때 DurryNoteButton 뒤에 반짝이는 Game Blur입니다.")]
    public MissionButtonGlow noteButtonGlow;

    [Tooltip("Assets/Dustiny/Art/UI/Game Blur 스프라이트. 비워두면 자동 로드를 시도합니다.")]
    public Sprite noteButtonGlowSprite;

    [Tooltip("켜면 DurryNoteButton 아래에 NoteButtonGlow를 자동 생성합니다.")]
    public bool autoCreateNoteButtonGlow = true;

    [Tooltip("Game Button을 눌렀을 때 이동할 씬 이름입니다. Build Settings에 동일한 이름의 씬을 등록하세요.")]
    public string dollSceneName = "DollScene";

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

    [Header("Page Focus Mode")]
    [Tooltip("Keep every full page (Note, Shop, MyPage, Menu) centered in front of the player.")]
    public bool placePagesAtPlayerCenter = true;
    [Min(0.2f)] public float pageCenterDistance = 2.2f;

    [Tooltip("Move Durry to the player's left of an open page so equipped-item previews remain visible.")]
    public bool placeDurryBesideOpenPage = true;
    [Min(0.1f)] public float pageDurryLeftOffset = 2.1f;
    public float pageDurryHeightOffset = -0.9f;
    [Range(0.1f, 1f)] public float pageDurryScaleMultiplier = 0.8f;
    [Min(0f)] public float pageDurryScaleSmoothing = 5f;
    [Min(1f)] public float pageIdleRecenterSeconds = 20f;
    [Min(0f)] public float pageOpenFadeDuration = 0.35f;
    public float pageOpenRiseOffset = -180f;

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

    [Header("Menu Action Buttons")]
    [Tooltip("MenuPage의 Reset 버튼입니다. 비워두면 MenuPage 안에서 이름으로 자동 탐색합니다.")]
    public Button menuResetButton;

    [Tooltip("MenuPage의 Exit 버튼입니다. Quest 빌드에서는 Application.Quit(), 에디터에서는 Play Mode 종료로 동작합니다.")]
    public Button menuExitButton;

    public bool autoConnectMenuActionButtons = true;

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

    [Header("Left Hand Tracking")]
    [Tooltip("왼손은 소지 핀치 네비게이션 입력에만 사용합니다. 초기화는 Menu > Reset 버튼에서만 가능합니다.")]
    public OVRHand leftHand;
    public bool autoFindLeftOVRHandIfMissing = true;
    [Min(0f)] public float resetInputCooldown = 1f;


    [Header("Left Pinky Pinch Navigation")]
    [Tooltip("왼손 소지(새끼손가락) 핀치 한 번으로 네비게이션 바를 열거나 닫습니다. OVRSkeleton은 필요하지 않습니다.")]
    public bool leftPinkyPinchTogglesNavigation = false;

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
        "Look",      // 네 도움이 필요해
        "Look"       // 미션 노트를 눌러서 미션을 수행해줘
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

    [Tooltip("온보딩 중 Durry가 사용자의 시선 전방을 부드럽게 따라갑니다.")]
    public bool followDurryDuringOnboarding = true;

    [Min(0f)] public float durryFollowPositionSmoothing = 4f;
    [Min(0f)] public float durryFollowRotationSmoothing = 5f;

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
    private bool completedFirstMainStart;
    private Coroutine introFinishCoroutine;
    private Coroutine onboardingTransitionCoroutine;
    private bool tutorialIndexPinchCompleted;
    private bool tutorialMiddlePinchCompleted;
    private bool tutorialWaitingForReleaseAfterFirstPinch;
    private float navigationLookDownStartedTime = -1f;
    private bool navigationWasVisibleBeforeIntro;
    private bool durryWasActiveBeforeIntro;
    private float lastDialogueAdvanceTime = -999f;
    private float lastConfirmInputTime = -999f;
    private float suppressMissionPinchUntil = -999f;

    private SpeechBubbleAutoSize speechAutoSize;
    
    private RectTransform dialogueFollowRoot;
    private Canvas dialogueFollowCanvas;
    
    private bool dialogueGraphicsPrepared;
private bool dialogueFollowRootReady;
private SpeechBubbleAutoSize descriptionAutoSize;

    private bool wasTriggerPressed;
    private bool wasRightIndexPinching;
    private bool wasRightMiddlePinching;
    private bool wasRightRingPinching;
    private float lastResetInputTime = -999f;
    private bool awaitingResetConfirmation;
    private bool awaitingMissionScanPlacementConfirmation;
    private bool awaitingMiniGameStartConfirmation;

    private bool creditUIEventSubscribed;

    private bool currentDescriptionUsesDim = true;
    private bool wasLeftPinkyPinching;
    private float lastLeftPinkyNavigationToggleTime = -999f;

    private DustinyPage currentPage = DustinyPage.None;
    private bool pageRootOpen;
    private bool pageCanvasPoseApplied;
    private Transform cachedWorldCanvasParent;
    private Vector3 cachedWorldCanvasLocalPosition;
    private Quaternion cachedWorldCanvasLocalRotation;
    private Vector3 fixedPageCanvasWorldPosition;
    private Quaternion fixedPageCanvasWorldRotation;
    private float lastPageInteractionTime = -999f;
    private CanvasGroup pageCanvasGroup;
    private Coroutine pageOpenAnimationCoroutine;
    private float pageOpenVerticalOffset;

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
        if (IsDuplicateDustinyManager())
        {
            enabled = false;
            return;
        }

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
        ForceDescriptionHiddenAtBoot();
        SetupDescriptionDimOverlay();
        DisableAllKnownDimOverlays();
        ResolveNavigationReferences();
        ResolveWaistNavFollow();
        ResolveNavigationButtons();
        ConnectNavigationButtonEvents();

        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
        ConnectPageButtonEvents();
        ResolveMenuActionButtons();
        ConnectMenuActionButtonEvents();
        ConfigurePageRootGraphics();

        ResolveShopCoinText();
        SubscribeCreditUI();
        RefreshShopCoinUI();

        SetupWorldCanvas();
        SetNavigationBarVisible(navigationBarStartsVisible);
        EnsureNoteButtonGlow();
        RefreshNoteButtonGlow();

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

        RemoveLegacyPageGrabComponent();

        UpdateViewLockedUI();
        KeepIntroTutorialUpright();
        UpdateDialoguePlacement();
        UpdatePageRootPlacement();
        EnterMainSceneFlow();
        EnsureUiPointerModule();
        RestoreWaistNavInteraction();
        if (!introTutorialActive)
        {
            StartCoroutine(EnsureDurryVisibleRoutine());
            StartCoroutine(EnsureNavigationVisibleAfterBootRoutine());
        }

        completedFirstMainStart = true;
    }

    /// <summary>
    /// 재접속(튜토리얼 스킵) 직후 WaistNavFollow.Start의 gaze-hide와 레이스하지 않도록
    /// 몇 프레임 더 네비/메뉴 버튼을 복구합니다.
    /// </summary>
    private IEnumerator EnsureNavigationVisibleAfterBootRoutine()
    {
        for (int i = 0; i < 4; i++)
        {
            yield return null;
            if (introTutorialActive)
            {
                yield break;
            }

            EnsureUiPointerModule();
            RestoreWaistNavInteraction();
        }
    }



    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        UpdateViewLockedUI();
        KeepIntroTutorialUpright();
        UpdateDialoguePlacement();
        UpdatePageRootPlacement();

        if (rightIndexPinchConfirms ||
            rightMiddlePinchRejectsEverything ||
            rightMiddlePinchRetriesMissionScan ||
            rightMiddlePinchClosesOpenPage ||
            rightRingPinchStartsOrRescans ||
            leftPinkyPinchTogglesNavigation)
        {
            UpdateHandPinchInput();
        }

        UpdateNavigationLookDownTutorial();

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
        // Run after other Update-based interactions so they cannot overwrite
        // Durry's head-relative pose in the same frame.
        UpdateDurryFollowDuringOnboarding();
        UpdateDialogueFollowDurry();

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
        // 파괴된 씬 참조가 남아 있으면 재탐색합니다.
        if (navigationBarObject == null)
        {
            // fall through
        }
        else
        {
            return;
        }

        // 먼저 WorldCanvas 아래를 찾습니다.
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        Transform found = FindChildTransformContains(searchRoot, "waistnavcanvas") ??
                          FindChildTransformContainsAll(searchRoot, "navigation", "bar") ??
                          FindChildTransformContains(searchRoot, "navbar") ??
                          FindChildTransformContains(searchRoot, "navigationbar");

        // 네비게이션이 WorldCanvas 밖의 WaistFollowRoot 아래에 있는 현재 씬 구조도 지원합니다.
        if (found == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            Transform best = null;
            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null)
                {
                    continue;
                }

                string lowerName = candidate.name.ToLowerInvariant();
                if (lowerName == "waistnavcanvas")
                {
                    best = candidate;
                    break;
                }

                if (best == null &&
                    (lowerName == "navigationbar" ||
                     lowerName.Contains("waistnav") ||
                     lowerName.Contains("navigationbar")))
                {
                    best = candidate;
                }
            }

            found = best;
        }

        if (found != null)
        {
            // NavigationBar만 잡혀도 실제 루트는 WaistNavCanvas여야 gaze show/hide가 맞습니다.
            Canvas canvas = found.GetComponent<Canvas>() ?? found.GetComponentInParent<Canvas>(true);
            navigationBarObject = canvas != null ? canvas.gameObject : found.gameObject;
        }
        else
        {
            Debug.LogWarning(
                "[DustinyDemoFlow] Navigation Bar Object를 찾지 못했습니다. " +
                "WaistNavCanvas를 Navigation Bar Object에 직접 연결하세요."
            );
        }
    }

    private void ResolveWaistNavFollow()
    {
        if (waistNavFollow != null || !autoFindWaistNavFollow)
        {
            return;
        }

        DustinyWaistNavFollow[] follows = FindObjectsByType<DustinyWaistNavFollow>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        // 버전마다 waistNavRoot / waistNavGrabbableRoot 필드명이 달랐기 때문에
        // 특정 필드에 직접 의존하지 않고 Transform 계층으로 가장 가까운 Follow를 찾습니다.
        foreach (DustinyWaistNavFollow follow in follows)
        {
            if (follow == null)
            {
                continue;
            }

            if (navigationBarObject != null &&
                (navigationBarObject.transform.IsChildOf(follow.transform) ||
                 follow.transform.IsChildOf(navigationBarObject.transform)))
            {
                waistNavFollow = follow;
                return;
            }
        }

        if (follows.Length > 0)
        {
            waistNavFollow = follows[0];
        }
    }

    private void SetTutorialNavigationOverride(bool visible)
    {
        ResolveWaistNavFollow();

        if (waistNavFollow != null)
        {
            // 최신 DustinyWaistNavFollow에는 SetVisibilityOverride(bool)가 있습니다.
            // 이전 버전 스크립트가 프로젝트에 남아 있어도 DemoFlow가 컴파일되도록
            // 리플렉션으로 선택적으로 호출하고, 없으면 SetActive 폴백을 사용합니다.
            System.Reflection.MethodInfo method = waistNavFollow.GetType().GetMethod(
                "SetVisibilityOverride",
                new Type[] { typeof(bool) }
            );

            if (method != null)
            {
                method.Invoke(waistNavFollow, new object[] { visible });
                return;
            }
        }

        SetNavigationBarVisible(visible);
    }

    private void ClearTutorialNavigationOverrideAndRefresh()
    {
        ResolveWaistNavFollow();

        if (waistNavFollow != null)
        {
            System.Reflection.MethodInfo method = waistNavFollow.GetType().GetMethod(
                "ClearVisibilityOverrideAndRefresh",
                Type.EmptyTypes
            );

            if (method != null)
            {
                method.Invoke(waistNavFollow, null);
                return;
            }
        }

        SetNavigationBarVisible(navigationBarStartsVisible || navigationWasVisibleBeforeIntro);
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

        if (gameButton == null)
        {
            gameButton = FindButtonByExactNames(
                searchRoot,
                "Game Button",
                "GameButton"
            ) ?? FindButtonByKeywords(searchRoot, "game");
        }

        EnsureNoteButtonGlow();
        RefreshNoteButtonGlow();
    }

    private void EnsureNoteButtonGlow()
    {
        ResolveNavigationButtonsWithoutGlowRefresh();

        if (noteButtonGlow == null && durryNoteButton != null)
        {
            noteButtonGlow = durryNoteButton.GetComponentInChildren<MissionButtonGlow>(true);
        }

        if (noteButtonGlow == null && autoCreateNoteButtonGlow && durryNoteButton != null)
        {
            GameObject glowObject = new GameObject(
                "NoteButtonGlow",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup)
            );

            glowObject.layer = durryNoteButton.gameObject.layer;
            glowObject.transform.SetParent(durryNoteButton.transform, false);
            glowObject.transform.SetAsFirstSibling();

            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(180f, 180f);
            glowRect.localScale = Vector3.one;
            glowRect.localRotation = Quaternion.identity;

            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.raycastTarget = false;
            glowImage.preserveAspect = true;
            glowImage.color = Color.white;

            noteButtonGlow = glowObject.AddComponent<MissionButtonGlow>();
            noteButtonGlow.glowRect = glowRect;
            noteButtonGlow.glowImage = glowImage;
            noteButtonGlow.canvasGroup = glowObject.GetComponent<CanvasGroup>();
        }

        if (noteButtonGlowSprite == null)
        {
            noteButtonGlowSprite = LoadGameBlurSprite();
        }

        if (noteButtonGlow != null && noteButtonGlowSprite != null)
        {
            noteButtonGlow.ConfigureSprite(noteButtonGlowSprite);
        }
    }

    private void ResolveNavigationButtonsWithoutGlowRefresh()
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
    }

    private static Sprite LoadGameBlurSprite()
    {
        // Quest 빌드용 Resources (Sprite로 import된 경우)
        Sprite resourceSprite = Resources.Load<Sprite>("Dustiny/Art/UI/Game Blur");
        if (resourceSprite != null)
        {
            return resourceSprite;
        }

        Sprite[] sprites = Resources.LoadAll<Sprite>("Dustiny/Art/UI/Game Blur");
        if (sprites != null && sprites.Length > 0)
        {
            return sprites[0];
        }

        // Resources가 Default Texture로 남아 있어도 Texture2D로 받아 스프라이트를 만듭니다.
        Texture2D resourceTexture = Resources.Load<Texture2D>("Dustiny/Art/UI/Game Blur");
        if (resourceTexture != null)
        {
            return Sprite.Create(
                resourceTexture,
                new Rect(0f, 0f, resourceTexture.width, resourceTexture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect
            );
        }

#if UNITY_EDITOR
        Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Dustiny/Art/UI/Game Blur.png");
        if (editorSprite != null)
        {
            return editorSprite;
        }

        Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Dustiny/Art/UI/Game Blur.png");
        if (texture != null)
        {
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect
            );
        }
#endif

        return null;
    }

    /// <summary>
    /// 보송력(텍스처) 레벨이 0/1/2이고 튜토리얼이 아닐 때만 노트 버튼 후광을 켭니다.
    /// </summary>
    public void RefreshNoteButtonGlow()
    {
        EnsureNoteButtonGlow();
        if (noteButtonGlow == null)
        {
            return;
        }

        int score = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 1;

        bool shouldGlow =
            !introTutorialActive &&
            HasCompletedIntroTutorial() &&
            score >= 0 &&
            score <= 2;

        noteButtonGlow.SetGlow(shouldGlow);
    }

private void ConnectNavigationButtonEvents()
    {
        if (!autoConnectNavigationButtons)
        {
            return;
        }

        ResolveNavigationButtons();
        ConnectPageButtonClick(durryNoteButton, OpenNotePage);
        ConnectPageButtonClick(shopButton, OpenShopPage);
        ConnectPageButtonClick(myPageButton, OpenMyPage);
        ConnectPageButtonClick(menuButton, OpenMenuPage);
        ConnectPageButtonClick(gameButton, OpenDollScene);
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

        eventSystem.gameObject.SetActive(true);
        eventSystem.enabled = true;
        eventSystem.sendNavigationEvents = false;

        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                modules[i].enabled = true;
            }
        }
    }


private void RestoreWaistNavInteraction()
    {
        ResolveNavigationReferences();
        ResolveWaistNavFollow();

        // WaistNavCanvas를 우선하고, NavigationBar만 잡혀 있으면 부모 캔버스로 올립니다.
        Transform canvasTransform = null;
        if (waistNavFollow != null && waistNavFollow.waistNavRoot != null)
        {
            canvasTransform = waistNavFollow.waistNavRoot;
        }
        else if (navigationBarObject != null)
        {
            canvasTransform = navigationBarObject.transform;
            Canvas parentCanvas = navigationBarObject.GetComponentInParent<Canvas>(true);
            if (parentCanvas != null)
            {
                canvasTransform = parentCanvas.transform;
                navigationBarObject = parentCanvas.gameObject;
            }
        }

        if (canvasTransform != null)
        {
            Transform current = canvasTransform;
            while (current != null)
            {
                current.gameObject.SetActive(true);
                current = current.parent;
            }

            Canvas canvas = canvasTransform.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                canvas.overrideSorting = false;
                Camera eyeCamera = centerEyeAnchor != null
                    ? centerEyeAnchor.GetComponent<Camera>()
                    : Camera.main;
                if (eyeCamera != null)
                {
                    canvas.worldCamera = eyeCamera;
                }
            }

            GraphicRaycaster raycaster = canvasTransform.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = true;
            }

            MonoBehaviour[] behaviours = canvasTransform.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (typeName == "GraphicRaycaster" ||
                    typeName == "Canvas" ||
                    typeName == "CanvasGroup" ||
                    typeName == "PointableCanvas" ||
                    typeName.Contains("Poke") ||
                    typeName.Contains("Ray") ||
                    typeName.Contains("Pointable"))
                {
                    behaviour.enabled = true;
                }
            }
        }

        ConnectNavigationButtonEvents();

        if (introTutorialActive)
        {
            return;
        }

        if (waistNavFollow != null)
        {
            // 재접속 직후 gaze-hide에 바로 가려지지 않도록 잠시 강제 표시합니다.
            // (미니게임 복귀와 같은 "메뉴가 보이는" 상태를 보장)
            waistNavFollow.ForceShowThenResumeGaze(2.5f);
        }

        SetNavigationBarVisible(true);
    }



private IEnumerator EnsureDurryVisibleRoutine()
    {
        if (introTutorialActive)
        {
            yield break;
        }

        // Summon once up front. Continuous SummonDurryToUser every frame fights
        // UpdateDurryFollowDuringOnboarding when a page is open (post mini-game lag).
        if (durryObject != null)
        {
            durryObject.SetActive(true);
            if (durryVisual != null)
            {
                durryVisual.gameObject.SetActive(true);
            }

            if (!pageRootOpen && centerEyeAnchor != null)
            {
                SummonDurryToUser();
            }
            else
            {
                ApplyDurryScale();
            }
        }

        float endTime = Time.unscaledTime + 2.5f;
        while (Time.unscaledTime < endTime)
        {
            if (introTutorialActive)
            {
                yield break;
            }

            if (durryObject != null)
            {
                durryObject.SetActive(true);
                if (durryVisual != null)
                {
                    durryVisual.gameObject.SetActive(true);
                }

                // Keep Durry visible only — do not teleport while a tab is open.
                if (!pageRootOpen)
                {
                    ApplyDurryScale();
                }
            }

            yield return null;
        }
    }







    /// <summary>
    /// NOTE 버튼 뒤에 반투명 Glow를 준비합니다.
    /// 별도 에셋 없이 기존 NOTE 버튼의 targetGraphic Sprite를 복제해 사용할 수 있습니다.
    /// </summary>


    private void ResolvePageReferences()
    {
        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;

        // 파괴된 DDOL 잔여 참조를 비우고 새 씬에서 다시 찾습니다.
        if (bigNoteRoot == null)
        {
            Transform foundRoot = FindChildTransformExact(searchRoot, "BigNoteRoot") ??
                                  FindChildTransformExact(searchRoot, "PageRoot") ??
                                  FindChildTransformContainsAll(searchRoot, "page", "root");

            if (foundRoot != null &&
                !foundRoot.name.Equals("MenuPageRoot", System.StringComparison.OrdinalIgnoreCase) &&
                !foundRoot.name.Equals("NotePageRoot", System.StringComparison.OrdinalIgnoreCase))
            {
                bigNoteRoot = foundRoot.gameObject;
            }
        }

        if (bigNoteRect == null && bigNoteRoot != null)
        {
            bigNoteRect = bigNoteRoot.GetComponent<RectTransform>();
        }

        // 상점/마이페이지처럼 실제 페이지 리프(MenuPage/NotePage)를 우선합니다.
        // MenuPageRoot/NotePageRoot를 잡으면 BigNoteRoot에 가려지거나 내용 갱신이 어긋납니다.
        if (notePageObject == null)
        {
            notePageObject =
                GetGameObjectFromTransform(FindChildTransformExact(searchRoot, "NotePage")) ??
                GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "note"));
        }

        if (shopPageObject == null)
        {
            shopPageObject =
                GetGameObjectFromTransform(FindChildTransformExact(searchRoot, "ShopPage")) ??
                GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "shop"));
        }

        if (myPageObject == null)
        {
            myPageObject =
                GetGameObjectFromTransform(FindChildTransformExact(searchRoot, "MyPage")) ??
                GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "mypage")) ??
                GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "my"));
        }

        if (menuPageObject == null)
        {
            menuPageObject =
                GetGameObjectFromTransform(FindChildTransformExact(searchRoot, "MenuPage")) ??
                GetGameObjectFromTransform(FindBestPageTransform(searchRoot, "menu"));
        }

        if (sideTagsRoot == null)
        {
            Transform tagSearchRoot = bigNoteRoot != null ? bigNoteRoot.transform : searchRoot;
            sideTagsRoot = FindChildTransformContainsAll(tagSearchRoot, "side", "tag") ??
                           FindChildTransformContains(tagSearchRoot, "sidetags");
        }
    }

    /// <summary>
    /// 재접속 후 상점/마이페이지처럼 페이지 오브젝트를 현재 씬 기준으로 다시 붙입니다.
    /// </summary>
    private void RebindPageObjectsFromScene()
    {
        notePageObject = null;
        shopPageObject = null;
        myPageObject = null;
        menuPageObject = null;
        bigNoteRoot = null;
        bigNoteRect = null;
        sideTagsRoot = null;
        noteBackgroundObject = null;
        menuBackgroundObject = null;
        obsoleteSharedBackgroundObject = null;

        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
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

    private void ResolveMenuActionButtons()
    {
        ResolvePageReferences();

        Transform searchRoot = menuPageObject != null
            ? menuPageObject.transform
            : bigNoteRoot != null
                ? bigNoteRoot.transform
                : worldCanvas != null
                    ? worldCanvas.transform
                    : transform;

        if (menuResetButton == null)
        {
            menuResetButton = FindButtonByExactNames(
                searchRoot,
                "Reset",
                "ResetButton",
                "Reset Button",
                "MenuReset",
                "MenuResetButton"
            );

            if (menuResetButton == null)
            {
                menuResetButton = FindButtonByKeywords(searchRoot, "reset");
            }
        }

        if (menuExitButton == null)
        {
            menuExitButton = FindButtonByExactNames(
                searchRoot,
                "Exit",
                "ExitButton",
                "Exit Button",
                "Quit",
                "QuitButton",
                "Quit Button"
            );

            if (menuExitButton == null)
            {
                menuExitButton = FindButtonByKeywords(searchRoot, "exit") ??
                                 FindButtonByKeywords(searchRoot, "quit");
            }
        }

        if (menuResetButton != null && menuResetButton == closePageButton)
        {
            Debug.LogError("[메뉴 버튼 연결 오류] Reset 버튼이 Close 버튼과 같은 오브젝트로 연결되어 있습니다.");
        }

        if (menuExitButton != null && menuExitButton == closePageButton)
        {
            Debug.LogError("[메뉴 버튼 연결 오류] Exit 버튼이 Close 버튼과 같은 오브젝트로 연결되어 있습니다. Close Button을 별도로 연결하세요.");
        }
    }

    private void ConnectMenuActionButtonEvents()
    {
        if (!autoConnectMenuActionButtons)
        {
            return;
        }

        ResolveMenuActionButtons();

        if (menuResetButton != null)
        {
            ConnectPageButtonClick(menuResetButton, RequestResetAllProgress);
        }

        if (menuExitButton != null)
        {
            ConnectPageButtonClick(menuExitButton, ExitGame);
        }
    }

    public void ExitGame()
    {
        SuppressMissionPinchFromPageUI();
        StopDurryVoicePlayback();

        Debug.Log("[Dustiny Menu] Exit 버튼 입력 - 게임을 종료합니다.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OpenNotePage()
    {
        SuppressMissionPinchFromPageUI();
        awaitingMissionScanPlacementConfirmation = false;

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        // 활성 라운드가 없으면 NOTE를 빈 페이지로 열지 않고 시작 질문만 표시합니다.
        if (missionController != null && !missionController.RequestOpenMissionNote())
        {
            // MissionController의 레거시 시스템 문구 대신 사용자 친화적인 문구를 보여줍니다.
            if (missionController.IsAwaitingMissionStart)
            {
                ShowFriendlyMissionStartPrompt();
            }

            return;
        }

        OpenPage(DustinyPage.Note);
    }

    private void ShowFriendlyMissionStartPrompt()
    {
        QuestProgressManager progress = QuestProgressManager.Instance;

        int completed = progress != null ? progress.CompletedRoundsToday : 0;
        int required = progress != null ? progress.RequiredCleaningRoundsPerDay : 3;
        bool rewardLimitReached = progress != null && progress.HasReachedDailyRewardLimit;
        int remainingRewards = Mathf.Max(0, required - completed);

        string remainingRewardText;
        switch (remainingRewards)
        {
            case 1: remainingRewardText = "한 번"; break;
            case 2: remainingRewardText = "두 번"; break;
            case 3: remainingRewardText = "세 번"; break;
            default: remainingRewardText = $"{remainingRewards}번"; break;
        }

        string prompt = rewardLimitReached || remainingRewards <= 0
            ? "미션을 시작할래?\n오늘은 더 이상 보상을 받지 못해."
            : $"미션을 시작할래?\n오늘 {remainingRewardText} 더 보상을 받을 수 있어.";

        ShowDescriptionMessage(
            prompt,
            rewardLimitReached ? concernExpressionState : neutralDialogueExpressionState
        );
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

        // 비활성 상태였던 MenuPage가 실제로 열린 뒤 Reset/Exit를 다시 찾아 연결합니다.
        ResolveMenuActionButtons();
        ConnectMenuActionButtonEvents();
    }

public void OpenDollScene()
    {
        SuppressMissionPinchFromPageUI();
        RequestMiniGameStartConfirmation();
    }

    private void RequestMiniGameStartConfirmation()
    {
        awaitingResetConfirmation = false;
        awaitingMissionScanPlacementConfirmation = false;
        awaitingMiniGameStartConfirmation = true;

        if (pageRootOpen)
        {
            CloseBigNote();
        }

        // 미션/오프닝과 동일하게 SpeechBubble만 사용합니다.
        ShowDescriptionMessage("게임을 시작할래?", successExpressionState);
        UpdateDialogueFollowDurry();
        Debug.Log("[Dustiny Navigation] Game Button → 미니게임 시작 확인 대기 (검지 핀치)");
    }

    private void ConfirmMiniGameStartAndLoad()
    {
        awaitingMiniGameStartConfirmation = false;
        StopDurryVoicePlayback();
        HideDialoguePanels();

        if (string.IsNullOrWhiteSpace(dollSceneName))
        {
            Debug.LogError("[Dustiny Navigation] Doll Scene 이름이 비어 있습니다.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(dollSceneName))
        {
            Debug.LogError(
                $"[Dustiny Navigation] '{dollSceneName}' 씬을 불러올 수 없습니다. " +
                "File > Build Profiles(Build Settings)의 Scene List에 씬이 등록되어 있는지 확인하세요."
            );
            return;
        }

        Debug.Log($"[Dustiny Navigation] 미니게임 시작 확인 → {dollSceneName} 이동");
        SceneManager.LoadScene(dollSceneName);
    }

    private void CancelMiniGameStartConfirmation()
    {
        if (!awaitingMiniGameStartConfirmation)
        {
            return;
        }

        awaitingMiniGameStartConfirmation = false;
        StopDurryVoicePlayback();
        HideDialoguePanels();
        PlayDurryExpression(idleExpressionState);
        Debug.Log("[Dustiny Navigation] 미니게임 시작 취소");
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

    public void NotifyPageInteraction()
    {
        lastPageInteractionTime = Time.unscaledTime;
    }

public void OpenPage(DustinyPage page)
    {
        NotifyPageInteraction();

        // Game 버튼 확인 대기 중 다른 탭(메뉴 등)을 열면 미니게임 시작 확인을 취소합니다.
        if (page != DustinyPage.None && awaitingMiniGameStartConfirmation)
        {
            CancelMiniGameStartConfirmation();
        }

        if (uiRoot != null)
        {
            uiRoot.gameObject.SetActive(true);
        }

        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
        }

        if (durryObject != null)
        {
            durryObject.SetActive(true);
        }

        if (page != currentPage)
        {
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

        // 상점/마이페이지와 동일: 열 때마다 현재 씬 페이지를 다시 붙인 뒤 표시합니다.
        RebindPageObjectsFromScene();
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

        // MenuPageRoot/NotePageRoot는 BigNoteRoot 밖이라, 다른 탭을 열 때 반드시 끕니다.
        SetDedicatedPageRootActive(DustinyPage.Menu, page == DustinyPage.Menu);
        SetDedicatedPageRootActive(DustinyPage.Note, page == DustinyPage.Note);

        if (page == DustinyPage.Shop || page == DustinyPage.MyPage)
        {
            DustinyItemPageController itemPageController =
                requestedPage.GetComponentInChildren<DustinyItemPageController>(true);
            itemPageController?.RefreshPage();
        }

        if (page == DustinyPage.Note)
        {
            // 상점 RefreshPage와 같이 노트 미션 목록을 강제 재바인딩합니다.
            QuestStatusUI statusUI = FindFirstObjectByType<QuestStatusUI>();
            statusUI?.RebindSceneUiReferences();
            statusUI?.RefreshMissionSlots();
        }

        if (page == DustinyPage.Menu)
        {
            ResolveMenuActionButtons();
            ConnectMenuActionButtonEvents();
        }

        bool showSharedBackground = useSharedBackgroundForShopAndMyPage &&
                                    (page == DustinyPage.Shop || page == DustinyPage.MyPage);
        if (obsoleteSharedBackgroundObject != null)
        {
            obsoleteSharedBackgroundObject.SetActive(showSharedBackground);
        }

        SetSeparateBackgroundVisible(noteBackgroundObject, page == DustinyPage.Note, notePageObject);
        SetSeparateBackgroundVisible(menuBackgroundObject, page == DustinyPage.Menu, menuPageObject);

        BringOpenedPageToFront(page);
        SyncDedicatedPageRootWithBigNote(page);

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
        SyncDedicatedPageRootWithBigNote(page);
        Debug.Log($"[페이지 열기] {page}");

    }

    private void BringOpenedPageToFront(DustinyPage page)
    {
        if (page == DustinyPage.Menu || page == DustinyPage.Note)
        {
            RectTransform dedicatedRoot = GetDedicatedPageRootRect(page);
            if (dedicatedRoot != null)
            {
                dedicatedRoot.SetAsLastSibling();
                return;
            }
        }

        if (bringPageRootToFrontWhenOpen && bigNoteRect != null)
        {
            bigNoteRect.SetAsLastSibling();
        }
    }

    private void SetDedicatedPageRootActive(DustinyPage page, bool active)
    {
        RectTransform dedicatedRoot = GetDedicatedPageRootRect(page);
        if (dedicatedRoot == null)
        {
            return;
        }

        dedicatedRoot.gameObject.SetActive(active);
    }

    private RectTransform GetDedicatedPageRootRect(DustinyPage page)
    {
        string exactRootName = page == DustinyPage.Menu
            ? "MenuPageRoot"
            : page == DustinyPage.Note
                ? "NotePageRoot"
                : null;

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        if (!string.IsNullOrEmpty(exactRootName))
        {
            Transform exactRoot = FindChildTransformExact(searchRoot, exactRootName);
            if (exactRoot != null)
            {
                return exactRoot as RectTransform ?? exactRoot.GetComponent<RectTransform>();
            }
        }

        GameObject pageObject = GetPageObject(page);
        if (pageObject == null)
        {
            return null;
        }

        Transform current = pageObject.transform;
        while (current != null)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName == "menupageroot" || lowerName == "notepageroot")
            {
                return current as RectTransform ?? current.GetComponent<RectTransform>();
            }

            if (worldCanvas != null && current == worldCanvas.transform)
            {
                break;
            }

            current = current.parent;
        }

        return pageObject.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Menu/Note는 BigNoteRoot 밖(전용 PageRoot)에 있어서, 상점처럼 보이려면
    /// BigNoteRoot와 같은 위치/순서로 맞춰야 합니다.
    /// </summary>
    private void SyncDedicatedPageRootWithBigNote(DustinyPage page)
    {
        if (page != DustinyPage.Menu && page != DustinyPage.Note)
        {
            return;
        }

        RectTransform dedicatedRoot = GetDedicatedPageRootRect(page);
        if (dedicatedRoot == null)
        {
            return;
        }

        dedicatedRoot.gameObject.SetActive(true);

        if (bigNoteRect == null)
        {
            return;
        }

        dedicatedRoot.anchorMin = bigNoteRect.anchorMin;
        dedicatedRoot.anchorMax = bigNoteRect.anchorMax;
        dedicatedRoot.pivot = bigNoteRect.pivot;
        dedicatedRoot.anchoredPosition = bigNoteRect.anchoredPosition;
        dedicatedRoot.localRotation = bigNoteRect.localRotation;
        dedicatedRoot.localScale = bigNoteRect.localScale;
        dedicatedRoot.SetAsLastSibling();
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

        if (visible)
        {
            NotifyPageInteraction();
        }

        if (!visible)
        {
            RestoreWorldCanvasPoseAfterPage();
        }

        SetDurryVisibleForPage(visible);

        if (bigNoteRoot != null)
        {
            bigNoteRoot.SetActive(visible);
        }

        if (visible)
        {
            StartPageOpenAnimation();
        }
        else
        {
            StopPageOpenAnimation();
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

            // Dedicated roots outside BigNoteRoot must be closed explicitly.
            DeactivateNamedPageRoot("MenuPageRoot");
            DeactivateNamedPageRoot("NotePageRoot");
        }

        if (hideNavigationBarWhenPageOpen)
        {
            SetNavigationBarVisible(!visible && navigationBarStartsVisible);
        }
    }

    private void DeactivateNamedPageRoot(string rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return;
        }

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        Transform found = FindChildTransformExact(searchRoot, rootName);
        if (found != null)
        {
            found.gameObject.SetActive(false);
        }
    }

private void SetPageObjectVisible(GameObject pageObject, bool visible)
    {
        if (pageObject == null)
        {
            return;
        }

        if (visible)
        {
            // Menu/Note live under dedicated *PageRoot objects that start inactive.
            // Activating only the page leaf leaves them invisible in the hierarchy.
            EnsurePageAncestorsActive(pageObject.transform);
            pageObject.SetActive(true);
            return;
        }

        pageObject.SetActive(false);
        DeactivateDedicatedPageRootIfEmpty(pageObject.transform);
    }

    private void EnsurePageAncestorsActive(Transform pageTransform)
    {
        if (pageTransform == null)
        {
            return;
        }

        Transform current = pageTransform;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }

            if ((worldCanvas != null && current == worldCanvas.transform) ||
                (uiRoot != null && current == uiRoot) ||
                (resolvedUIRoot != null && current == resolvedUIRoot))
            {
                break;
            }

            current = current.parent;
        }
    }

    private void DeactivateDedicatedPageRootIfEmpty(Transform pageTransform)
    {
        if (pageTransform == null || pageTransform.parent == null)
        {
            return;
        }

        Transform parent = pageTransform.parent;
        if (bigNoteRoot != null && parent == bigNoteRoot.transform)
        {
            return;
        }

        string lowerName = parent.name.ToLowerInvariant();
        bool isDedicatedRoot =
            lowerName.EndsWith("pageroot") ||
            lowerName == "menupageroot" ||
            lowerName == "notepageroot";

        if (!isDedicatedRoot)
        {
            return;
        }

        parent.gameObject.SetActive(false);
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

    private void StartPageOpenAnimation()
    {
        if (bigNoteRoot == null)
        {
            return;
        }

        pageCanvasGroup = bigNoteRoot.GetComponent<CanvasGroup>() ??
                          bigNoteRoot.AddComponent<CanvasGroup>();
        if (pageOpenAnimationCoroutine != null)
        {
            StopCoroutine(pageOpenAnimationCoroutine);
        }

        pageOpenAnimationCoroutine = StartCoroutine(AnimatePageOpen());
    }

    private void StopPageOpenAnimation()
    {
        if (pageOpenAnimationCoroutine != null)
        {
            StopCoroutine(pageOpenAnimationCoroutine);
            pageOpenAnimationCoroutine = null;
        }

        pageOpenVerticalOffset = 0f;
        if (pageCanvasGroup != null)
        {
            pageCanvasGroup.alpha = 1f;
            pageCanvasGroup.blocksRaycasts = true;
            pageCanvasGroup.interactable = true;
        }
    }

    private IEnumerator AnimatePageOpen()
    {
        pageOpenVerticalOffset = pageOpenRiseOffset;
        pageCanvasGroup.alpha = 0f;
        pageCanvasGroup.blocksRaycasts = false;
        pageCanvasGroup.interactable = false;

        if (pageOpenFadeDuration <= 0f)
        {
            pageOpenVerticalOffset = 0f;
            pageCanvasGroup.alpha = 1f;
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < pageOpenFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / pageOpenFadeDuration);
                float eased = t * t * (3f - 2f * t);
                pageOpenVerticalOffset = Mathf.Lerp(pageOpenRiseOffset, 0f, eased);
                pageCanvasGroup.alpha = eased;
                yield return null;
            }
        }

        pageOpenVerticalOffset = 0f;
        pageCanvasGroup.alpha = 1f;
        pageCanvasGroup.blocksRaycasts = true;
        pageCanvasGroup.interactable = true;
        pageOpenAnimationCoroutine = null;
    }

private void SetDurryVisibleForPage(bool pageIsOpen)
    {
        if (durryObject == null)
        {
            return;
        }

        // 페이지를 열어도 더리는 항상 보입니다. 엽에 둘 배치만 합니다.
        durryObject.SetActive(true);
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
        if (placePagesAtPlayerCenter && pageRootOpen && centerEyeAnchor != null && worldCanvas != null)
        {
            Vector3 forward = centerEyeAnchor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            bool shouldRecenter = !pageCanvasPoseApplied ||
                                Time.unscaledTime - lastPageInteractionTime >= pageIdleRecenterSeconds;
            if (shouldRecenter)
            {
                ApplyWorldCanvasPoseForPage(centerEyeAnchor.position + forward * pageCenterDistance, forward);
                NotifyPageInteraction();
            }
            else
            {
                KeepWorldCanvasFixedForPage();
            }

            bigNoteRect.anchorMin = new Vector2(0.5f, 0.5f);
            bigNoteRect.anchorMax = new Vector2(0.5f, 0.5f);
            bigNoteRect.pivot = new Vector2(0.5f, 0.5f);
            bigNoteRect.anchoredPosition = new Vector2(0f, pageOpenVerticalOffset);
            bigNoteRect.localRotation = Quaternion.identity;
            return;
        }

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

    private void ApplyWorldCanvasPoseForPage(Vector3 position, Vector3 forward)
    {
        Transform canvasTransform = worldCanvas.transform;
        if (!pageCanvasPoseApplied)
        {
            cachedWorldCanvasParent = canvasTransform.parent;
            cachedWorldCanvasLocalPosition = canvasTransform.localPosition;
            cachedWorldCanvasLocalRotation = canvasTransform.localRotation;
            canvasTransform.SetParent(null, true);
            pageCanvasPoseApplied = true;
        }

        canvasTransform.position = position;
        canvasTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        fixedPageCanvasWorldPosition = canvasTransform.position;
        fixedPageCanvasWorldRotation = canvasTransform.rotation;
    }

    private void KeepWorldCanvasFixedForPage()
    {
        if (!pageCanvasPoseApplied || worldCanvas == null)
        {
            return;
        }

        Transform canvasTransform = worldCanvas.transform;
        canvasTransform.position = fixedPageCanvasWorldPosition;
        canvasTransform.rotation = fixedPageCanvasWorldRotation;
    }

    private void RestoreWorldCanvasPoseAfterPage()
    {
        if (!pageCanvasPoseApplied || worldCanvas == null)
        {
            return;
        }

        Transform canvasTransform = worldCanvas.transform;
        canvasTransform.SetParent(cachedWorldCanvasParent, false);
        canvasTransform.localPosition = cachedWorldCanvasLocalPosition;
        canvasTransform.localRotation = cachedWorldCanvasLocalRotation;
        pageCanvasPoseApplied = false;
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

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() =>
        {
            action();
            DustinySfx.PlayYes();
        });
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
        button.onClick.AddListener(() =>
        {
            action();
            DustinySfx.PlayYes();
        });
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
                               FindChildTransformContains(searchRoot, "speechbubble") ??
                               FindChildTransformExact(searchRoot, "SpeechBubble");
            if (speech != null)
            {
                speechBubbleObject = speech.gameObject;
            }
        }

        if (speechBubbleObject == null)
        {
            GameObject foundSpeech = FindSceneGameObjectByName("SpeechBubble");
            if (foundSpeech != null)
            {
                speechBubbleObject = foundSpeech;
            }
        }

        if (descriptionObject == null)
        {
            Transform description = FindChildTransformExact(searchRoot, "Description") ??
                                    FindDialogueDescriptionTransform(searchRoot);
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
        EnsureDialogueAutoSizeComponents();
        EnsureDialogueBackgroundImageSetup(speechBubbleObject, preferSliced: true);
        EnsureDialogueBackgroundImageSetup(descriptionObject, preferSliced: true);
        ApplySpeechBubbleFont();

        // 씬 기본 Active=true여도 Description은 항상 꺼 둡니다 (SpeechBubble과 동시 표시 방지).
        SetDescriptionVisible(false);
        EnsureExclusiveSpeechBubble();
    }

    private void ApplySpeechBubbleFont()
    {
        if (speechText == null)
        {
            return;
        }

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

        TMP_FontAsset resolvedFont = speechBubbleFont;
        if (resolvedFont == null && descriptionText != null && descriptionText.font != null)
        {
            // Kyobo가 로드/렌더에 실패하면 Description에서 검증된 폰트로 폴백합니다.
            resolvedFont = descriptionText.font;
        }

        if (resolvedFont == null)
        {
            return;
        }

        if (speechText.font != resolvedFont)
        {
            speechText.font = resolvedFont;
        }

        if (speechText.fontSharedMaterial == null &&
            speechText.font != null &&
            speechText.font.material != null)
        {
            speechText.fontSharedMaterial = speechText.font.material;
        }
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
                                  FindChildTransformExact(searchRoot, "GuestureIntro") ??
                                  FindChildTransformExact(searchRoot, "GestureIntro") ??
                                  FindChildTransformContainsAll(searchRoot, "intro", "root");
            if (foundRoot != null)
            {
                introRootObject = foundRoot.gameObject;
            }
        }

        // 이름이 비슷하므로 Exact 검색을 우선합니다.
        if (onboardingOKNoObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "OnboardingOKNo") ??
                              FindChildTransformExact(searchRoot, "Onboarding OK No") ??
                              FindChildTransformExact(searchRoot, "Onboarding") ??
                              FindChildTransformExact(searchRoot, "GestureIntro");
            if (found != null) onboardingOKNoObject = found.gameObject;
        }

        if (onboardingOKObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "OnboardingOK") ??
                              FindChildTransformExact(searchRoot, "Onboarding OK") ??
                              FindChildTransformExact(searchRoot, "GestureOK");
            if (found != null) onboardingOKObject = found.gameObject;
        }


        if (onboardingNoObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "OnboardingNo") ??
                              FindChildTransformExact(searchRoot, "Onboarding NO") ??
                              FindChildTransformExact(searchRoot, "Onboarding No");
            if (found != null) onboardingNoObject = found.gameObject;
        }

        if (onboardingNoDoneObject == null)
        {
            Transform found = FindChildTransformExact(searchRoot, "OnboardingNoDone") ??
                              FindChildTransformExact(searchRoot, "Onboarding NO Done") ??
                              FindChildTransformExact(searchRoot, "Onboarding No Done");
            if (found != null) onboardingNoDoneObject = found.gameObject;
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
                              FindChildTransformExact(searchRoot, "NavInform OK") ??
                              FindChildTransformContainsAll(searchRoot, "nav", "inform", "ok");
            if (found != null) navInformOKObject = found.gameObject;
        }
    }

    private void SetupImageIntroOverlay()
    {
        ResolveImageIntroReferences();

        Camera centerEyeCamera = centerEyeAnchor != null
            ? centerEyeAnchor.GetComponent<Camera>()
            : null;

        if (useFullscreenCameraOverlay && centerEyeCamera != null)
        {
            if (introFullscreenOverlayCanvas == null)
            {
                GameObject existingCanvasObject = GameObject.Find("DustinyTutorialFullscreenOverlayCanvas");
                if (existingCanvasObject != null)
                {
                    introFullscreenOverlayCanvas = existingCanvasObject.GetComponent<Canvas>();
                }
            }

            if (introFullscreenOverlayCanvas == null && autoCreateIntroPassthroughOverlay)
            {
                GameObject canvasObject = new GameObject(
                    "DustinyTutorialFullscreenOverlayCanvas",
                    typeof(RectTransform),
                    typeof(Canvas)
                );

                canvasObject.transform.SetParent(transform, false);
                introFullscreenOverlayCanvas = canvasObject.GetComponent<Canvas>();
            }

            if (introFullscreenOverlayCanvas != null)
            {
                introFullscreenOverlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                introFullscreenOverlayCanvas.worldCamera = centerEyeCamera;
                introFullscreenOverlayCanvas.planeDistance = Mathf.Clamp(
                    introOverlayPlaneDistance,
                    centerEyeCamera.nearClipPlane + 0.05f,
                    Mathf.Max(
                        centerEyeCamera.nearClipPlane + 0.1f,
                        centerEyeCamera.farClipPlane - 0.1f
                    )
                );
                introFullscreenOverlayCanvas.overrideSorting = true;
                introFullscreenOverlayCanvas.sortingOrder = introOverlaySortingOrder;
            }

            if (introPassthroughOverlayObject == null && introPassthroughOverlayImage != null)
            {
                introPassthroughOverlayObject = introPassthroughOverlayImage.gameObject;
            }

            if (introPassthroughOverlayObject == null && autoCreateIntroPassthroughOverlay)
            {
                introPassthroughOverlayObject = new GameObject(
                    "IntroPassthroughOverlay",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image)
                );
            }

            if (introPassthroughOverlayObject != null && introFullscreenOverlayCanvas != null)
            {
                RectTransform overlayRect = introPassthroughOverlayObject.GetComponent<RectTransform>();
                if (overlayRect == null)
                {
                    overlayRect = introPassthroughOverlayObject.AddComponent<RectTransform>();
                }

                overlayRect.SetParent(introFullscreenOverlayCanvas.transform, false);
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.pivot = new Vector2(0.5f, 0.5f);
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
                overlayRect.anchoredPosition = Vector2.zero;
                overlayRect.localScale = Vector3.one;
                overlayRect.localRotation = Quaternion.identity;

                introPassthroughOverlayImage = introPassthroughOverlayObject.GetComponent<Image>();
                if (introPassthroughOverlayImage == null)
                {
                    introPassthroughOverlayImage = introPassthroughOverlayObject.AddComponent<Image>();
                }
            }
        }
        else
        {
            // Fullscreen Canvas를 사용할 수 없는 경우에만 WorldCanvas 폴백을 사용합니다.
            if (introPassthroughOverlayObject == null && introPassthroughOverlayImage != null)
            {
                introPassthroughOverlayObject = introPassthroughOverlayImage.gameObject;
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
        }

        if (introPassthroughOverlayImage != null)
        {
            introPassthroughOverlayImage.color = new Color(
                0f,
                0f,
                0f,
                Mathf.Clamp01(introPassthroughOverlayOpacity)
            );
            introPassthroughOverlayImage.raycastTarget = false;
        }
    }

private void BeginImageIntroTutorial()
    {
        ResolveImageIntroReferences();
        SetupImageIntroOverlay();

        if (onboardingOKNoObject == null)
        {
            Debug.LogWarning(
                "[온보딩] OnboardingOKNo 오브젝트를 찾지 못해 이미지 튜토리얼을 건너뜁니다. " +
                "Intro 아래 오브젝트 이름을 OnboardingOKNo로 맞추거나 직접 연결하세요."
            );
            StartOpeningAfterImageIntro();
            return;
        }

        introTutorialActive = true;
        introTutorialState = IntroTutorialState.OnboardingOKNo;
        onboardingActive = false;
        awaitingResetConfirmation = false;
        awaitingMiniGameStartConfirmation = false;
        tutorialIndexPinchCompleted = false;
        tutorialMiddlePinchCompleted = false;
        tutorialWaitingForReleaseAfterFirstPinch = false;
        navigationLookDownStartedTime = -1f;
        RefreshNoteButtonGlow();

        if (introFinishCoroutine != null)
        {
            StopCoroutine(introFinishCoroutine);
            introFinishCoroutine = null;
        }

        if (onboardingTransitionCoroutine != null)
        {
            StopCoroutine(onboardingTransitionCoroutine);
            onboardingTransitionCoroutine = null;
        }

        SetIntroObjectAlpha(onboardingOKNoObject, 1f);
        SetIntroObjectAlpha(onboardingOKObject, 1f);
        SetIntroObjectAlpha(onboardingNoObject, 1f);
        SetIntroObjectAlpha(onboardingNoDoneObject, 1f);
        SetIntroObjectAlpha(navInformObject, 1f);
        SetIntroObjectAlpha(navInformOKObject, 1f);

        navigationWasVisibleBeforeIntro = navigationBarObject != null && navigationBarObject.activeSelf;
        durryWasActiveBeforeIntro = durryObject != null && durryObject.activeSelf;

        HideDialoguePanels();
        SetPageRootVisible(false);
        SetScanZoneVisible(false);

        if (hideNavigationDuringImageIntro)
        {
            SetTutorialNavigationOverride(false);
        }

        if (hideDurryDuringImageIntro)
        {
            SetDurryActive(false);
        }
        else if (durryObject != null)
        {
            SetDurryActive(true);
            ApplyDurryScale();
            SummonDurryToUser();
        }

        if (introRootObject != null)
        {
            introRootObject.SetActive(true);
        }

        if (introFullscreenOverlayCanvas != null)
        {
            introFullscreenOverlayCanvas.gameObject.SetActive(true);
        }

        if (introPassthroughOverlayObject != null)
        {
            introPassthroughOverlayObject.SetActive(true);
        }

        SetImageIntroStage(IntroTutorialState.OnboardingOKNo);
        Debug.Log("[온보딩] OnboardingOKNo 시작 - 검지와 중지 핀치를 각각 1회 연습");
    }



    private void SetImageIntroStage(IntroTutorialState stage)
    {
        introTutorialState = stage;

        SetIntroObjectVisible(onboardingOKNoObject, stage == IntroTutorialState.OnboardingOKNo);
        SetIntroObjectVisible(onboardingOKObject, stage == IntroTutorialState.OnboardingOK);
        SetIntroObjectVisible(onboardingNoObject, stage == IntroTutorialState.OnboardingNo);
        SetIntroObjectVisible(onboardingNoDoneObject, stage == IntroTutorialState.OnboardingNoDone);
        SetIntroObjectVisible(navInformObject, stage == IntroTutorialState.NavInform);
        SetIntroObjectVisible(navInformOKObject, stage == IntroTutorialState.NavInformOK);

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
            case IntroTutorialState.OnboardingOKNo: return onboardingOKNoObject;
            case IntroTutorialState.OnboardingOK: return onboardingOKObject;
            case IntroTutorialState.OnboardingNo: return onboardingNoObject;
            case IntroTutorialState.OnboardingNoDone: return onboardingNoDoneObject;
            case IntroTutorialState.NavInform: return navInformObject;
            case IntroTutorialState.NavInformOK: return navInformOKObject;
            default: return null;
        }
    }

    private bool IsOnboardingPinchTutorialState()
    {
        return introTutorialState == IntroTutorialState.OnboardingOKNo ||
               introTutorialState == IntroTutorialState.OnboardingOK ||
               introTutorialState == IntroTutorialState.OnboardingNo;
    }

    private void ConfirmTutorialIndexPinch()
    {
        if (!introTutorialActive ||
            !IsOnboardingPinchTutorialState() ||
            tutorialIndexPinchCompleted ||
            tutorialWaitingForReleaseAfterFirstPinch)
        {
            return;
        }

        tutorialIndexPinchCompleted = true;
        PlayYesSfx();

        if (tutorialMiddlePinchCompleted)
        {
            CompletePinchTutorial();
            return;
        }

        tutorialWaitingForReleaseAfterFirstPinch = true;
        SetImageIntroStage(IntroTutorialState.OnboardingOK);
        Debug.Log("[온보딩] 검지 핀치 완료 → 손을 완전히 놓은 뒤 중지 핀치 대기");
    }

    private void ConfirmTutorialMiddlePinch()
    {
        if (!introTutorialActive ||
            !IsOnboardingPinchTutorialState() ||
            tutorialMiddlePinchCompleted ||
            tutorialWaitingForReleaseAfterFirstPinch)
        {
            return;
        }

        tutorialMiddlePinchCompleted = true;
        PlayNoSfx();

        if (tutorialIndexPinchCompleted)
        {
            CompletePinchTutorial();
            return;
        }

        tutorialWaitingForReleaseAfterFirstPinch = true;
        SetImageIntroStage(IntroTutorialState.OnboardingNo);
        Debug.Log("[온보딩] 중지 핀치 완료 → 손을 완전히 놓은 뒤 검지 핀치 대기");
    }

    private void CompletePinchTutorial()
    {
        if (!tutorialIndexPinchCompleted || !tutorialMiddlePinchCompleted)
        {
            return;
        }

        tutorialWaitingForReleaseAfterFirstPinch = false;
        SetImageIntroStage(IntroTutorialState.OnboardingNoDone);

        if (onboardingTransitionCoroutine != null)
        {
            StopCoroutine(onboardingTransitionCoroutine);
        }

        onboardingTransitionCoroutine = StartCoroutine(ShowOnboardingDoneThenNavigation());
        Debug.Log("[온보딩] 검지 + 중지 완료 → OnboardingNoDone");
    }

    private IEnumerator ShowOnboardingDoneThenNavigation()
    {
        if (onboardingDoneHoldSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(onboardingDoneHoldSeconds);
        }

        yield return FadeIntroObject(
            onboardingNoDoneObject,
            1f,
            0f,
            onboardingTransitionFadeDuration
        );

        SetIntroObjectAlpha(navInformObject, 0f);
        SetImageIntroStage(IntroTutorialState.NavInform);

        // NavInform만 보일 때는 네비게이션을 아직 보여주지 않습니다.
        // 실제 고개 숙임이 감지되었을 때만 UpdateNavigationLookDownTutorial()에서 표시합니다.
        SetTutorialNavigationOverride(false);

        yield return FadeIntroObject(
            navInformObject,
            0f,
            1f,
            onboardingTransitionFadeDuration
        );

        onboardingTransitionCoroutine = null;
        Debug.Log("[온보딩] NavInform 시작 - 고개 숙임 대기");
    }

    private void UpdateNavigationLookDownTutorial()
    {
        if (!introTutorialActive ||
            introTutorialState != IntroTutorialState.NavInform ||
            centerEyeAnchor == null)
        {
            navigationLookDownStartedTime = -1f;
            return;
        }

        bool isLookingDown = centerEyeAnchor.forward.y <= navigationLookDownForwardYThreshold;

        if (!isLookingDown)
        {
            navigationLookDownStartedTime = -1f;
            SetTutorialNavigationOverride(false);
            return;
        }

        // 실제로 아래를 보는 동안에만 네비게이션을 보여줍니다.
        SetTutorialNavigationOverride(true);

        if (navigationLookDownStartedTime < 0f)
        {
            navigationLookDownStartedTime = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - navigationLookDownStartedTime >= navigationLookDownHoldSeconds)
        {
            ConfirmNavigationLookDownTutorial();
        }
    }

    private void ConfirmNavigationLookDownTutorial()
    {
        if (!introTutorialActive || introTutorialState != IntroTutorialState.NavInform)
        {
            return;
        }

        navigationLookDownStartedTime = -1f;
        SetTutorialNavigationOverride(true);
        PlayYesSfx();
        SetImageIntroStage(IntroTutorialState.NavInformOK);

        if (introFinishCoroutine != null)
        {
            StopCoroutine(introFinishCoroutine);
        }

        introFinishCoroutine = StartCoroutine(FinishImageIntroAfterDelay());
        Debug.Log("[온보딩] Navigation 확인 → NavInformOK");
    }

    private static void SetIntroObjectAlpha(GameObject target, float alpha)
    {
        if (target == null)
        {
            return;
        }

        CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = target.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = Mathf.Clamp01(alpha);
    }

    private static IEnumerator FadeIntroObject(
        GameObject target,
        float fromAlpha,
        float toAlpha,
        float duration)
    {
        if (target == null)
        {
            yield break;
        }

        CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = target.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = Mathf.Clamp01(fromAlpha);
        duration = Mathf.Max(0f, duration);

        if (duration <= 0f)
        {
            canvasGroup.alpha = Mathf.Clamp01(toAlpha);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(
                fromAlpha,
                toAlpha,
                Mathf.Clamp01(elapsed / duration)
            );
            yield return null;
        }

        canvasGroup.alpha = Mathf.Clamp01(toAlpha);
    }

    private IEnumerator FinishImageIntroAfterDelay()
    {
        if (navInformOKHoldSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(navInformOKHoldSeconds);
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
        MarkIntroTutorialCompleted();

        if (onboardingTransitionCoroutine != null)
        {
            StopCoroutine(onboardingTransitionCoroutine);
            onboardingTransitionCoroutine = null;
        }

        SetIntroObjectVisible(onboardingOKNoObject, false);
        SetIntroObjectVisible(onboardingOKObject, false);
        SetIntroObjectVisible(onboardingNoObject, false);
        SetIntroObjectVisible(onboardingNoDoneObject, false);
        SetIntroObjectVisible(navInformObject, false);
        SetIntroObjectVisible(navInformOKObject, false);

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

        if (introFullscreenOverlayCanvas != null)
        {
            introFullscreenOverlayCanvas.gameObject.SetActive(false);
        }

        if (introRootObject != null)
        {
            introRootObject.SetActive(false);
        }

        SetDurryActive(true);
        ApplyDurryScale();
        SummonDurryToUser();
        StartCoroutine(EnsureDurryVisibleRoutine());

        ClearTutorialNavigationOverrideAndRefresh();
        EnsureUiPointerModule();
        RestoreWaistNavInteraction();
        RefreshNoteButtonGlow();


        Debug.Log("[온보딩] 종료 - 전체 화면 오버레이 해제 후 게임 오프닝 시작");
        StartOpeningAfterImageIntro();
    }



private void SetDurryActive(bool active)
    {
        if (durryObject != null)
        {
            durryObject.SetActive(active);
        }

        if (durryVisual != null)
        {
            durryVisual.gameObject.SetActive(active);
        }
    }


    private void StartOpeningAfterImageIntro()
    {
        if (startOnboardingFlowOnStart)
        {
            BeginOnboardingFlow();
        }
        else
        {
            // 프롤로그를 사용하지 않는 경우에도 미션 질문을 자동으로 띄우지 않습니다.
            // NOTE만 사용 가능 상태로 만들고 자유 상태로 진입합니다.
            FinishOpeningWithoutMissionPrompt();
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

            EnsurePostHelpMissionNoteOpeningStep();
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

        EnsurePostHelpMissionNoteOpeningStep();

        Debug.Log(
            "[더리 오프닝] 기존 대사/표정 배열을 " +
            "대사+표정 한 세트 구조로 자동 변환했습니다."
        );
    }

    /// <summary>
    /// "네 도움이 필요해!" 뒤에 미션 노트 안내 대사가 없으면 자동으로 붙입니다.
    /// </summary>
    private void EnsurePostHelpMissionNoteOpeningStep()
    {
        const string helpNeededMarker = "도움이 필요";
        string missionNotePrompt = string.IsNullOrWhiteSpace(missionNoteUnlockedDescription)
            ? "미션 노트를 눌러서 미션을 수행해줘!\n보송력이 필요해."
            : missionNoteUnlockedDescription.Trim();

        if (openingDialogueSteps == null || openingDialogueSteps.Length == 0)
        {
            return;
        }

        for (int i = 0; i < openingDialogueSteps.Length; i++)
        {
            DurryDialogueStep step = openingDialogueSteps[i];
            if (step == null || string.IsNullOrWhiteSpace(step.text))
            {
                continue;
            }

            if (step.text.IndexOf("미션 노트", System.StringComparison.Ordinal) >= 0)
            {
                return;
            }
        }

        int helpIndex = -1;
        for (int i = openingDialogueSteps.Length - 1; i >= 0; i--)
        {
            DurryDialogueStep step = openingDialogueSteps[i];
            if (step != null &&
                !string.IsNullOrWhiteSpace(step.text) &&
                step.text.IndexOf(helpNeededMarker, System.StringComparison.Ordinal) >= 0)
            {
                helpIndex = i;
                break;
            }
        }

        if (helpIndex < 0)
        {
            return;
        }

        // 이미 도움 요청 직후에 미션 노트 안내가 있으면 끝.
        if (helpIndex + 1 < openingDialogueSteps.Length &&
            openingDialogueSteps[helpIndex + 1] != null &&
            !string.IsNullOrWhiteSpace(openingDialogueSteps[helpIndex + 1].text) &&
            openingDialogueSteps[helpIndex + 1].text.IndexOf("미션 노트", System.StringComparison.Ordinal) >= 0)
        {
            return;
        }

        DurryDialogueStep[] expanded = new DurryDialogueStep[openingDialogueSteps.Length + 1];
        for (int i = 0; i <= helpIndex; i++)
        {
            expanded[i] = openingDialogueSteps[i];
        }

        expanded[helpIndex + 1] = new DurryDialogueStep(
            missionNotePrompt,
            NormalizeLegacyExpressionName(missionDescriptionExpressionState)
        );

        for (int i = helpIndex + 1; i < openingDialogueSteps.Length; i++)
        {
            expanded[i + 1] = openingDialogueSteps[i];
        }

        openingDialogueSteps = expanded;
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
            FinishOpeningWithoutMissionPrompt();
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

        ShowSpeech(step.text, openingClip, step.expressionState);
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

        if (onboardingState != OnboardingState.IntroSpeech)
        {
            return;
        }

        if (!TryAdvanceOpeningSpeech())
        {
            // 마지막 프롤로그 대사가 끝나면 별도의 미션 안내를 띄우지 않습니다.
            // 사용자가 NOTE 탭을 직접 눌렀을 때만 시작 질문을 표시합니다.
            FinishOpeningWithoutMissionPrompt();
        }
    }

    private void FinishOpeningWithoutMissionPrompt()
    {
        HideDialoguePanels();
        SetScanZoneVisible(false);
        awaitingMissionScanPlacementConfirmation = false;

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        onboardingActive = false;
        onboardingState = OnboardingState.Finished;

        if (missionController != null)
        {
            // NOTE 버튼만 활성화합니다. 여기서는 미션 질문/스캔을 절대 시작하지 않습니다.
            missionController.UnlockMissionNoteAfterOpening();
        }
        else
        {
            Debug.LogError("[DustinyDemoFlow] 프롤로그는 종료됐지만 DustinyMissionController를 찾지 못했습니다.");
        }

        PlayDurryExpression(idleExpressionState);
        Debug.Log(
            "[더리 오프닝] 프롤로그 종료 - 미션은 시작하지 않습니다. " +
            "사용자가 NOTE 탭을 눌렀을 때만 시작 질문을 표시합니다."
        );
    }

    public void ShowDescriptionMessage(string message)
    {
        message = RewriteMissionSystemCopy(message);
        ShowDescription(message, null, null);
    }

    /// <summary>
    /// 대사와 표정을 명시적으로 한 세트로 표시합니다.
    /// MissionController에서도 키워드 추측 대신 이 오버로드를 사용하면 정확히 고정됩니다.
    /// </summary>
    public void ShowDescriptionMessage(string message, string expressionState)
    {
        message = RewriteMissionSystemCopy(message);
        ShowDescription(message, expressionState, null);
    }

    /// <summary>
    /// MissionController의 레거시 시스템 문구를 짧은 게임 대사로 정리합니다.
    /// 미션 로직/보상 계산은 건드리지 않고 화면 문구만 바꿉니다.
    /// </summary>
    private string RewriteMissionSystemCopy(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (ContainsAny(
                message,
                "청소 미션 1회 완료",
                "오늘의 미션 완료",
                "추가 미션 완료"))
        {
            QuestProgressManager progress = QuestProgressManager.Instance;

            if (progress != null)
            {
                int completed = progress.CompletedRoundsToday;
                int required = progress.RequiredCleaningRoundsPerDay;
                int remaining = Mathf.Max(0, required - completed);
                bool rewardGranted = progress.LastCompletedRoundGrantedReward;
                int reward = progress.LastCompletedRoundRewardCredit;

                if (rewardGranted)
                {
                    if (remaining > 0)
                    {
                        return
                            "청소 미션 완료!\n" +
                            $"{reward}코인을 얻었어. 오늘 앞으로 {remaining}회 더 미션을 할 수 있어!";
                    }

                    return
                        "청소 미션 완료!\n" +
                        $"{reward}코인을 얻었어. 오늘은 더 이상 보상을 받지 못해.";
                }

                return "청소 미션 완료!\n오늘은 더 이상 보상을 받지 못해.";
            }
        }

        if (ContainsAny(
                message,
                "검사 스캔에 실패",
                "재스캔을 시작하지 못",
                "아직 치운 물건을 확인하지 못"))
        {
            return "스캔에 실패했어!\n수동으로 체크하거나 다시 스캔해줘!";
        }

        return message;
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
        // 모든 더리 대사는 Description 패널 없이 SpeechBubble만 사용합니다.
        ShowSpeechBubbleOnly(message, explicitExpressionState, voiceClip);
    }

    private void ShowDescription(string message)
    {
        ShowDescription(message, null, null);
    }

    /// <summary>
    /// 더리 대사 표시의 단일 경로: Description은 숨기고 SpeechBubble만 표시합니다.
    /// ShowSpeech / ShowDescription / ShowDescriptionMessage 모두 여기로 모입니다.
    /// </summary>
    private void ShowSpeechBubbleOnly(
        string message,
        string expressionState,
        AudioClip voiceClip
    )
    {
        ResolveDialogueReferences();

        currentDescriptionUsesDim = false;
        SetDescriptionVisible(false);
        // Follow root 재부모화 전에 Speech를 켜 두어, inactive parent에 붙었다가
        // activeSelf=false로 남는 순서 버그를 방지합니다.
        SetSpeechVisible(true);
        EnsureDialogueFollowRoot();
        AssertSpeechBubbleHierarchyVisible(forceSpeechOn: true);
        EnsureExclusiveSpeechBubble();

        if (speechText != null)
        {
            ApplySpeechBubbleFont();
            speechText.enabled = true;
            Color textColor = speechText.color;
            textColor.a = 1f;
            speechText.color = textColor;
            speechText.gameObject.SetActive(true);
            speechText.text = message;
            speechText.ForceMeshUpdate();
        }

        EnsureSpeechBubbleRectSize();

        if (speechAutoSize != null)
        {
            speechAutoSize.SetText(message);
            speechAutoSize.ResizeBubble();
        }

        UpdateDialoguePlacement();
        UpdateDialogueFollowDurry();
        AssertSpeechBubbleHierarchyVisible(forceSpeechOn: true);
        EnsureDialogueGraphicsVisible();
        RefreshDialogueAutoSize();
        AssertSpeechBubbleHierarchyVisible(forceSpeechOn: true);

        if (!string.IsNullOrWhiteSpace(expressionState))
        {
            PlayDurryExpression(expressionState);
        }
        else
        {
            ApplySituationExpressionForMessage(message);
        }

        AudioClip resolvedVoice = voiceClip != null
            ? voiceClip
            : ResolveVoiceForMessage(message);

        PlayDurryVoice(resolvedVoice, message);
    }

    /// <summary>
    /// SpeechBubble이 켜져 있어야 할 때 follow root / canvas / parent를 강제 활성합니다.
    /// Description은 절대 켜지 않습니다.
    /// </summary>
    private void AssertSpeechBubbleHierarchyVisible(bool forceSpeechOn = true)
    {
        SetDescriptionVisible(false);
        if (forceSpeechOn)
        {
            SetSpeechVisible(true);
        }
        else if (speechBubbleObject == null || !speechBubbleObject.activeSelf)
        {
            return;
        }

        if (dialogueFollowRoot != null)
        {
            dialogueFollowRoot.gameObject.SetActive(true);
        }

        if (dialogueFollowCanvas != null)
        {
            ConfigureDialogueFollowCanvas(dialogueFollowCanvas);
        }

        if (speechBubbleObject != null)
        {
            speechBubbleObject.SetActive(true);
            Transform parent = speechBubbleObject.transform.parent;
            while (parent != null)
            {
                if (!parent.gameObject.activeSelf)
                {
                    parent.gameObject.SetActive(true);
                }

                CanvasGroup parentGroup = parent.GetComponent<CanvasGroup>();
                if (parentGroup != null)
                {
                    parentGroup.alpha = 1f;
                    parentGroup.interactable = true;
                    parentGroup.blocksRaycasts = true;
                }

                parent = parent.parent;
            }

            CanvasGroup speechGroup = speechBubbleObject.GetComponent<CanvasGroup>();
            if (speechGroup != null)
            {
                speechGroup.alpha = 1f;
                speechGroup.interactable = true;
                speechGroup.blocksRaycasts = true;
            }
        }
    }

    private void EnsureSpeechBubbleRectSize()
    {
        if (speechBubbleRect == null && speechBubbleObject != null)
        {
            speechBubbleRect = speechBubbleObject.GetComponent<RectTransform>();
        }

        if (speechBubbleRect == null)
        {
            return;
        }

        Vector2 size = speechBubbleRect.sizeDelta;
        const float minWidth = 600f;
        const float minHeight = 160f;

        if (size.x < 100f || size.y < 40f)
        {
            if (descriptionRect != null &&
                descriptionRect.sizeDelta.x >= 100f &&
                descriptionRect.sizeDelta.y >= 40f)
            {
                size = descriptionRect.sizeDelta;
            }
            else
            {
                size = new Vector2(minWidth, minHeight);
            }
        }

        size.x = Mathf.Max(size.x, minWidth);
        size.y = Mathf.Max(size.y, minHeight);
        speechBubbleRect.sizeDelta = size;
        speechBubbleRect.localScale = Vector3.one;
    }

    /// <summary>
    /// 호환용 API. 내부적으로 SpeechBubble만 표시합니다 (Description 패널 미사용).
    /// </summary>
    private void ShowDescription(
        string message,
        string explicitExpressionState,
        AudioClip explicitVoiceClip
    )
    {
        ShowSpeechBubbleOnly(message, explicitExpressionState, explicitVoiceClip);
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
                "계속 진행할래", "취소할래", "초기화할래", "초기화 할래"))
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
                "잘했어", "CR을 받", "코인을 받", "초기화가 완료",
                "고마워"))
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
        ShowSpeechBubbleOnly(message, expressionState, voiceClip);
    }

    /// <summary>
    /// 상점 구매/착용 성공 시 기쁜 표정+음성만 재생합니다. 말풍선/디스크립션은 띄우지 않습니다.
    /// </summary>
    public void PlayDurryHappyReactionOnly()
    {
        string expression = string.IsNullOrWhiteSpace(successExpressionState)
            ? "Joyful"
            : successExpressionState;

        PlayDurryExpression(expression);

        AudioClip clip = FirstAssignedClip(
            voiceHappy,
            voiceJoy,
            voiceCheerful,
            voiceSpecial,
            voiceGoodJob
        );
        if (clip != null)
        {
            PlayDurryVoice(clip);
        }

        // 구매/착용 핀치가 다른 UI(미션 확인 등)로 넘어가지 않게 짧게 억제합니다.
        SuppressGlobalConfirmInput(0.35f);
    }

    public bool IsDialoguePanelVisible()
    {
        // Description은 더 이상 표시 패널이 아님. SpeechBubble만 가시성 기준으로 사용합니다.
        return speechBubbleObject != null
            ? speechBubbleObject.activeSelf
            : speechText != null && speechText.gameObject.activeSelf;
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
        // Description 패널은 더 이상 표시하지 않습니다. 인자 true여도 SpeechBubble과 동시 노출을 원천 차단합니다.
        _ = visible;

        if (descriptionObject != null && IsDurryDialogueDescriptionObject(descriptionObject))
        {
            descriptionObject.SetActive(false);
        }
        else if (descriptionText != null && IsDurryDialogueDescriptionObject(descriptionText.gameObject))
        {
            descriptionText.gameObject.SetActive(false);
        }

        SetDescriptionDimVisible(false);
    }

    private static bool IsDurryDialogueDescriptionObject(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        string lowerName = target.name.ToLowerInvariant();
        return lowerName == "description" ||
               lowerName == "descriptiontext" ||
               (lowerName.Contains("description") &&
                !lowerName.Contains("itemdescription") &&
                !lowerName.Contains("informationbox"));
    }

    /// <summary>
    /// SpeechBubble이 보이면 Description은 반드시 비활성. 둘 다 activeSelf인 상태를 허용하지 않습니다.
    /// </summary>
    private void EnsureExclusiveSpeechBubble()
    {
        bool speechVisible = speechBubbleObject != null
            ? speechBubbleObject.activeSelf
            : speechText != null && speechText.gameObject.activeSelf;

        bool descriptionVisible = descriptionObject != null
            ? descriptionObject.activeSelf
            : descriptionText != null && descriptionText.gameObject.activeSelf;

        if (speechVisible || descriptionVisible)
        {
            // speech가 켜져 있거나 description이 씬에서 살아 있으면 description을 끈다.
            // (둘 다 true인 상태를 절대 남기지 않음)
            SetDescriptionVisible(false);
        }
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
            if (introTutorialState == IntroTutorialState.NavInform)
            {
                return;
            }

            if (IsOnboardingPinchTutorialState())
            {
                ConfirmTutorialIndexPinch();
            }
            return;
        }

        if (onboardingActive)
        {
            AdvanceOnboardingFlow();
            return;
        }

        // 프롤로그 이후 자유 상태에서는 전역 확인 입력으로 미션을 열지 않습니다.
        // 미션 시작 질문은 반드시 Navigation의 NOTE 버튼(OpenNotePage)을 눌렀을 때만 표시됩니다.
        return;
    }

    /// <summary>
    /// Interaction SDK의 공용 [확인] 버튼 OnClick에 연결합니다.
    /// 오른손 검지 핀치도 이 메서드를 호출하므로 두 입력의 역할이 같습니다.
    /// </summary>
public void OnConfirmButtonPressed()
    {
        // 온보딩 핀치 연습에서는 검지 입력만 별도로 기록합니다.
        if (introTutorialActive)
        {
            if (!IsOnboardingPinchTutorialState())
            {
                return;
            }

            if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
            {
                return;
            }

            lastConfirmInputTime = Time.unscaledTime;
            ConfirmTutorialIndexPinch();
            return;
        }

        // 페이지 버튼을 누른 같은 핀치가 전역 미션 확인으로 중복 처리되지 않게 막습니다.
        if (IsMissionPinchSuppressedByPageUI())
        {
            return;
        }

        if (pageRootOpen && !awaitingMiniGameStartConfirmation)
        {
            return;
        }

        if (Time.unscaledTime - lastConfirmInputTime < confirmInputCooldown)
        {
            return;
        }

        lastConfirmInputTime = Time.unscaledTime;
        PlayYesSfx();

        if (awaitingMiniGameStartConfirmation)
        {
            if (pageRootOpen)
            {
                CancelMiniGameStartConfirmation();
                return;
            }

            ConfirmMiniGameStartAndLoad();
            return;
        }

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

        // NOTE 시작 질문에 동의해도 즉시 스캔하지 않습니다.
        // 1차 검지: 시작 의사 확인 -> 스캔 위치 이동 안내
        // 2차 검지: 실제 스캔 시작
        if (missionController != null && missionController.IsAwaitingMissionStart)
        {
            if (!awaitingMissionScanPlacementConfirmation)
            {
                awaitingMissionScanPlacementConfirmation = true;
                ShowDescriptionMessage(
                    "좋아! 정리할 책상이\n잘 보이는 곳으로 이동해줘.\n" +
                    "준비되면 다시 검지 핀치!",
                    scanningExpressionState
                );
                Debug.Log("[DustinyMission] 미션 시작 동의 → 스캔 위치 이동 후 두 번째 검지 핀치 대기");
                return;
            }

            awaitingMissionScanPlacementConfirmation = false;
            HideDialoguePanels();
            missionController.ConfirmMissionStartFromPrompt();
            return;
        }

        if (missionController != null && missionController.IsAwaitingMissionConfirmation)
        {
            missionController.ConfirmPendingMissionScan();
            return;
        }

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

        if (IsMissionPinchSuppressedByPageUI())
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

        if (awaitingMiniGameStartConfirmation)
        {
            CancelMiniGameStartConfirmation();
            return;
        }

        if (awaitingResetConfirmation)
        {
            CancelResetAllProgress();
            return;
        }

        if (awaitingMissionScanPlacementConfirmation)
        {
            awaitingMissionScanPlacementConfirmation = false;
            StopDurryVoicePlayback();

            if (missionController != null && missionController.IsAwaitingMissionStart)
            {
                missionController.HandleMissionRejectPressed();
            }
            else
            {
                HideDialoguePanels();
            }

            Debug.Log("[DustinyMission] 스캔 위치 대기 중 중지 핀치 → 미션 시작 취소");
            return;
        }

        if (pageRootOpen)
        {
            StopDurryVoicePlayback();
            CloseBigNote();
            return;
        }

        if (missionController != null &&
            (missionController.IsAwaitingMissionStart ||
             missionController.IsAwaitingMissionConfirmation))
        {
            StopDurryVoicePlayback();
            missionController.HandleMissionRejectPressed();
            return;
        }

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

        // 초기화 진입점은 Menu > Reset뿐입니다.
        // 버튼을 누른 동일 핀치가 곧바로 확인으로 처리되지 않게 페이지를 먼저 닫습니다.
        awaitingMissionScanPlacementConfirmation = false;
        awaitingMiniGameStartConfirmation = false;
        CloseBigNote();
        awaitingResetConfirmation = true;

        ShowDescriptionMessage(
            "초기화 할래?\n다시 복구할 수 없어.",
            concernExpressionState
        );

        Debug.Log("[Dustiny Reset] 초기화 확인을 기다립니다.");
    }

private void ConfirmResetAllProgress()
    {
        if (!awaitingResetConfirmation)
        {
            return;
        }

        awaitingResetConfirmation = false;
        awaitingMissionScanPlacementConfirmation = false;
        awaitingMiniGameStartConfirmation = false;
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

        onboardingActive = false;
        onboardingState = OnboardingState.Finished;
        ClearIntroTutorialCompleted();

        ShopInventoryManager.Instance?.ResetAllItemState();
        missionController.ResetMissionCleanlinessAndCreditFromLeftRingPinch();

        StopDurryVoicePlayback();
        ShowDescriptionMessage("초기화가 완료되었어!", successExpressionState);
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
        bool isLeftPinkyPinching = isLeftTracked &&
                                   leftHand.GetFingerIsPinching(OVRHand.HandFinger.Pinky);

        bool indexPinchDown = isIndexPinching && !wasRightIndexPinching;
        bool middlePinchDown = isMiddlePinching && !wasRightMiddlePinching;
        bool ringPinchDown = isRingPinching && !wasRightRingPinching;
        bool leftPinkyPinchDown = isLeftPinkyPinching && !wasLeftPinkyPinching;

        wasRightIndexPinching = isIndexPinching;
        wasRightMiddlePinching = isMiddlePinching;
        wasRightRingPinching = isRingPinching;
        wasLeftPinkyPinching = isLeftPinkyPinching;

        if ((indexPinchDown || middlePinchDown || ringPinchDown ||
             leftPinkyPinchDown) &&
            missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        // 튜토리얼: 검지와 중지를 각각 한 번씩 성공해야 합니다.
        // 첫 핀치 뒤에는 둘 다 완전히 놓을 때까지 다음 입력을 잠급니다.
        if (introTutorialActive)
        {
            if (IsOnboardingPinchTutorialState())
            {
                if (tutorialWaitingForReleaseAfterFirstPinch)
                {
                    if (!isIndexPinching && !isMiddlePinching)
                    {
                        tutorialWaitingForReleaseAfterFirstPinch = false;
                        Debug.Log("[온보딩] 첫 핀치 릴리즈 확인 → 두 번째 손가락 입력 가능");
                    }

                    return;
                }

                if (rightIndexPinchConfirms && indexPinchDown && middlePinchDown)
                {
                    float indexStrength = rightHand != null
                        ? rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index)
                        : 1f;
                    float middleStrength = rightHand != null
                        ? rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle)
                        : 0f;

                    if (!tutorialIndexPinchCompleted && !tutorialMiddlePinchCompleted)
                    {
                        if (middleStrength > indexStrength)
                        {
                            ConfirmTutorialMiddlePinch();
                        }
                        else
                        {
                            ConfirmTutorialIndexPinch();
                        }
                    }
                    else if (!tutorialIndexPinchCompleted)
                    {
                        ConfirmTutorialIndexPinch();
                    }
                    else if (!tutorialMiddlePinchCompleted)
                    {
                        ConfirmTutorialMiddlePinch();
                    }
                }
                else if (rightIndexPinchConfirms && indexPinchDown)
                {
                    ConfirmTutorialIndexPinch();
                }
                else if (middlePinchDown)
                {
                    ConfirmTutorialMiddlePinch();
                }
            }

            // NavInform/NavInformOK에서는 핀치를 받지 않고 고개 숙임만 기다립니다.
            return;
        }

        if (leftPinkyPinchTogglesNavigation && leftPinkyPinchDown)
        {
            OnLeftPinkyPinchToggleNavigation();
            return;
        }

        if (rightMiddlePinchRejectsEverything && middlePinchDown)
        {
            OnRejectButtonPressed();
            return;
        }

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
        DustinySfx.RegisterClips(yesSfxClip, noSfxClip, null, null);
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
        DustinySfx.RegisterClips(yesSfxClip, noSfxClip, null, null);
        DustinySfx.PlayYes();
    }

public void PlayNoSfx()
    {
        DustinySfx.RegisterClips(yesSfxClip, noSfxClip, null, null);
        DustinySfx.PlayNo();
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
        RefreshNoteButtonGlow();
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
            bool hasRenderer = false;
            for (int i = 0; i < durryCleanlinessRenderers.Length; i++)
            {
                if (durryCleanlinessRenderers[i] != null)
                {
                    hasRenderer = true;
                    break;
                }
            }

            if (hasRenderer)
            {
                return;
            }
        }

        if (durryObject == null)
        {
            return;
        }

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
                combined.Contains("body") ||
                combined.Contains("durry_geo") ||
                combined.Contains("character");

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

private void UpdateDurryFollowDuringOnboarding()
    {
        if (introTutorialActive ||
            !followDurryDuringOnboarding ||
            centerEyeAnchor == null ||
            durryObject == null ||
            !durryObject.activeInHierarchy)
        {
            return;
        }

        Vector3 targetPosition;
        if (pageRootOpen && placeDurryBesideOpenPage && worldCanvas != null)
        {
            Vector3 pageLeft = -worldCanvas.transform.right;
            pageLeft.y = 0f;
            if (pageLeft.sqrMagnitude < 0.001f)
            {
                pageLeft = Vector3.left;
            }

            pageLeft.Normalize();
            targetPosition = worldCanvas.transform.position +
                             pageLeft * pageDurryLeftOffset +
                             Vector3.up * pageDurryHeightOffset;
        }
        else
        {
            Vector3 forward = GetFlatForward();
            Vector3 right = centerEyeAnchor.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            right.Normalize();
            targetPosition = centerEyeAnchor.position +
                             forward * durryDistance +
                             right * durrySideOffset +
                             Vector3.up * durryHeightOffset;
        }

        Quaternion targetRotation = GetRotationFacingUser(targetPosition, durryYawOffset);

        float positionT = durryFollowPositionSmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-durryFollowPositionSmoothing * Time.deltaTime);
        float rotationT = durryFollowRotationSmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-durryFollowRotationSmoothing * Time.deltaTime);

        durryObject.transform.position = Vector3.Lerp(
            durryObject.transform.position,
            targetPosition,
            positionT);
        durryObject.transform.rotation = Quaternion.Slerp(
            durryObject.transform.rotation,
            targetRotation,
            rotationT);

        float targetScaleMultiplier = pageRootOpen && placeDurryBesideOpenPage
            ? pageDurryScaleMultiplier
            : 1f;
        durryObject.transform.localScale = Vector3.one * durryRootScale * targetScaleMultiplier;
        if (durryVisual != null)
        {
            durryVisual.localScale = Vector3.one * durryVisualScale;
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

    private void KeepIntroTutorialUpright()
    {
        if (!introTutorialActive || introRootObject == null || centerEyeAnchor == null)
        {
            return;
        }

        Vector3 forward = GetFlatForward();
        introRootObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
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

        EnsureDialogueFollowRoot();
    }

private void EnsureDialogueFollowRoot()
    {
        if (!pinDialogueBelowDurry)
        {
            return;
        }

        if (dialogueFollowRootReady &&
            dialogueFollowRoot != null &&
            dialogueFollowCanvas != null)
        {
            ConfigureDialogueFollowCanvas(dialogueFollowCanvas);
            ReparentDialogueUnderFollowRoot();
            if (speechBubbleObject != null && speechBubbleObject.activeSelf)
            {
                dialogueFollowRoot.gameObject.SetActive(true);
            }

            EnsureDialogueGraphicsVisible();
            return;
        }

        GameObject rootObject = FindSceneGameObjectByName("DurryDialogueFollowRoot");
        if (rootObject == null)
        {
            rootObject = new GameObject(
                "DurryDialogueFollowRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster)
            );
        }

        // WorldCanvas와 동일한 UI 레이어를 써야 Quest MR에서 컬링되지 않습니다.
        if (worldCanvas != null)
        {
            rootObject.layer = worldCanvas.gameObject.layer;
        }
        else
        {
            rootObject.layer = 3;
        }

        dialogueFollowRoot = rootObject.GetComponent<RectTransform>();
        if (dialogueFollowRoot == null)
        {
            dialogueFollowRoot = rootObject.AddComponent<RectTransform>();
        }

        dialogueFollowCanvas = rootObject.GetComponent<Canvas>();
        if (dialogueFollowCanvas == null)
        {
            dialogueFollowCanvas = rootObject.AddComponent<Canvas>();
        }

        CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            Destroy(scaler);
        }

        if (rootObject.GetComponent<GraphicRaycaster>() == null)
        {
            rootObject.AddComponent<GraphicRaycaster>();
        }

        ConfigureDialogueFollowCanvas(dialogueFollowCanvas);

        dialogueFollowRoot.SetParent(null, true);
        dialogueFollowRoot.sizeDelta = dialogueFollowCanvasSize;
        dialogueFollowRoot.pivot = new Vector2(0.5f, 0.5f);
        dialogueFollowRoot.anchorMin = new Vector2(0.5f, 0.5f);
        dialogueFollowRoot.anchorMax = new Vector2(0.5f, 0.5f);
        dialogueFollowRoot.localScale = Vector3.one * Mathf.Max(0.0001f, dialogueWorldScale);

        dialogueGraphicsPrepared = false;
        ReparentDialogueUnderFollowRoot();
        ConfigureDialogueAutoSizeForFollow();
        EnsureDialogueGraphicsVisible();
        dialogueFollowRootReady = true;
        rootObject.SetActive(true);
    }

    private void ConfigureDialogueFollowCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.enabled = true;
        canvas.renderMode = RenderMode.WorldSpace;
        Camera centerEyeCamera = centerEyeAnchor != null
            ? centerEyeAnchor.GetComponent<Camera>()
            : null;
        canvas.worldCamera = centerEyeCamera != null ? centerEyeCamera : Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 200);
        // TMP가 World Space Canvas에서 그려지려면 TexCoord1/Normal/Tangent가 필요합니다.
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;
    }

    private void ReparentDialogueUnderFollowRoot()
    {
        if (dialogueFollowRoot == null)
        {
            return;
        }

        if (speechBubbleObject != null &&
            speechBubbleObject.transform.parent != dialogueFollowRoot)
        {
            bool wasSpeechActive = speechBubbleObject.activeSelf;
            speechBubbleObject.transform.SetParent(dialogueFollowRoot, false);
            RectTransform speechRect = speechBubbleObject.GetComponent<RectTransform>();
            if (speechRect != null)
            {
                speechRect.anchorMin = new Vector2(0.5f, 0.5f);
                speechRect.anchorMax = new Vector2(0.5f, 0.5f);
                speechRect.pivot = new Vector2(0.5f, 0.5f);
                speechRect.anchoredPosition = Vector2.zero;
                speechRect.localRotation = Quaternion.identity;
                speechRect.localScale = Vector3.one;
            }

            speechBubbleObject.SetActive(wasSpeechActive);
        }

        if (descriptionObject != null &&
            descriptionObject.transform.parent != dialogueFollowRoot)
        {
            descriptionObject.transform.SetParent(dialogueFollowRoot, false);
            RectTransform descriptionRectTransform = descriptionObject.GetComponent<RectTransform>();
            if (descriptionRectTransform != null)
            {
                descriptionRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                descriptionRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                descriptionRectTransform.pivot = new Vector2(0.5f, 0.5f);
                descriptionRectTransform.anchoredPosition = Vector2.zero;
                descriptionRectTransform.localRotation = Quaternion.identity;
                descriptionRectTransform.localScale = Vector3.one;
            }
        }

        // Follow root가 켜져도 Description은 씬 Active 상태로 따라오지 않게 강제 비활성.
        SetDescriptionVisible(false);
        if (speechBubbleObject != null && speechBubbleObject.activeSelf && dialogueFollowRoot != null)
        {
            dialogueFollowRoot.gameObject.SetActive(true);
        }
    }

    private void ConfigureDialogueAutoSizeForFollow()
    {
        if (!pinDialogueBelowDurry)
        {
            return;
        }

        if (speechAutoSize != null)
        {
            speechAutoSize.lockBubblePosition = true;
            speechAutoSize.forceCenterAnchorAndPivot = true;
            speechAutoSize.bubbleAnchoredPosition = Vector2.zero;
        }

        if (descriptionAutoSize != null)
        {
            descriptionAutoSize.lockBubblePosition = true;
            descriptionAutoSize.forceCenterAnchorAndPivot = true;
            descriptionAutoSize.bubbleAnchoredPosition = Vector2.zero;
        }
    }

private void EnsureDialogueGraphicsVisible()
    {
        // SpeechBubble만 그래픽 복원. Description은 숨김 유지(그래픽 복원으로 다시 보이지 않게).
        // 매 프레임 follow-root 준비 경로에서도 호출되므로, 이미 켜진 Speech만 계층을 보정합니다.
        AssertSpeechBubbleHierarchyVisible(forceSpeechOn: false);
        if (speechBubbleObject != null && speechBubbleObject.activeSelf)
        {
            EnsureSpeechBubbleRectSize();
        }

        RestoreDialogueBackgroundGraphics(speechBubbleObject);
        EnsureDialogueBackgroundImageSetup(speechBubbleObject, preferSliced: true);
        RestoreDefaultDialogueMaterials(speechBubbleObject);
        AlignSpeechBubbleVisualsWithDescriptionFallback();
        ApplySpeechBubbleFont();

        if (speechText != null && speechBubbleObject != null && speechBubbleObject.activeSelf)
        {
            speechText.enabled = true;
            speechText.gameObject.SetActive(true);
            Color textColor = speechText.color;
            textColor.a = 1f;
            speechText.color = textColor;
            if (speechText.fontSharedMaterial == null &&
                speechText.font != null &&
                speechText.font.material != null)
            {
                speechText.fontSharedMaterial = speechText.font.material;
            }
        }

        if (dialogueFollowCanvas != null)
        {
            ConfigureDialogueFollowCanvas(dialogueFollowCanvas);
        }

        dialogueGraphicsPrepared = true;
        SetDescriptionVisible(false);
        EnsureExclusiveSpeechBubble();
    }

    /// <summary>
    /// SpeechBubble 배경/텍스트가 비정상일 때 Description의 검증된 Image/TMP 설정을 빌려 옵니다.
    /// Description 패널 자체는 절대 표시하지 않습니다.
    /// </summary>
    private void AlignSpeechBubbleVisualsWithDescriptionFallback()
    {
        if (speechBubbleObject == null)
        {
            return;
        }

        Image speechImage = speechBubbleObject.GetComponent<Image>();
        Image descriptionImage = descriptionObject != null
            ? descriptionObject.GetComponent<Image>()
            : null;

        if (speechImage != null)
        {
            speechImage.enabled = true;
            speechImage.material = null;
            Color color = speechImage.color;
            color.a = 1f;
            speechImage.color = color;

            if (speechImage.sprite == null && descriptionImage != null && descriptionImage.sprite != null)
            {
                speechImage.sprite = descriptionImage.sprite;
                speechImage.type = Image.Type.Sliced;
                speechImage.fillCenter = true;
            }
        }

        if (speechText != null && descriptionText != null)
        {
            if (speechText.font == null && descriptionText.font != null)
            {
                speechText.font = descriptionText.font;
            }

            if (speechText.fontSize < 8f && descriptionText.fontSize >= 8f)
            {
                speechText.fontSize = descriptionText.fontSize;
            }
        }
    }

    private static void RestoreDefaultDialogueMaterials(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
            {
                continue;
            }

            // Image 배경은 항상 UI 기본 머티리얼을 쓰게 강제합니다.
            // (이전 DialogueInFront 복제 머티리얼이 Quest에서 배경을 지웠습니다.)
            if (graphic is Image)
            {
                graphic.material = null;
            }
            else if (graphic.material != null &&
                     graphic.material.name.Contains("_DialogueInFront"))
            {
                graphic.material = null;
            }

            TMP_Text tmp = graphic as TMP_Text;
            if (tmp == null)
            {
                continue;
            }

            Material fontMat = tmp.fontMaterial;
            if (fontMat != null && fontMat.name.Contains("_DialogueInFront"))
            {
                if (tmp.font != null && tmp.font.material != null)
                {
                    tmp.fontSharedMaterial = tmp.font.material;
                }
            }
        }
    }

    private static void EnsureDialogueBackgroundImageSetup(GameObject root, bool preferSliced)
    {
        if (root == null)
        {
            return;
        }

        Image image = root.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        image.enabled = true;
        image.raycastTarget = true;
        image.material = null;

        Color color = image.color;
        color.a = 1f;
        image.color = color;

        if (image.sprite == null)
        {
            Debug.LogWarning(
                $"[Dustiny Dialogue] '{root.name}' 배경 Image에 sprite가 없습니다. " +
                "bubble/description 스프라이트를 다시 연결하세요."
            );
            return;
        }

        if (!preferSliced)
        {
            return;
        }

        // 말풍선/디스크립션 배경이 텍스트 크기에 맞게 늘어나도록 9-slice를 켭니다.
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        image.preserveAspect = false;
    }

    /// <summary>
    /// 이전 ZTest Always 머티리얼 핵은 Quest에서 배경을 안 보이게 만들어 비활성화했습니다.
    /// </summary>
    private static void ForceDialogueGraphicDrawInFront(Graphic graphic)
    {
        // no-op: keep Default UI Material
    }

    private void EnsureDialogueAutoSizeComponents()
    {
        if (speechBubbleObject != null)
        {
            if (speechAutoSize == null)
            {
                speechAutoSize = speechBubbleObject.GetComponent<SpeechBubbleAutoSize>();
            }

            if (speechAutoSize == null)
            {
                speechAutoSize = speechBubbleObject.AddComponent<SpeechBubbleAutoSize>();
            }

            ConfigureAutoSize(speechAutoSize, speechText, speechBubbleRect);
            ConfigureAutoSizeDefaults(speechAutoSize, isSpeech: true);
        }

        if (descriptionObject != null)
        {
            if (descriptionAutoSize == null)
            {
                descriptionAutoSize = descriptionObject.GetComponent<SpeechBubbleAutoSize>();
            }

            if (descriptionAutoSize == null)
            {
                descriptionAutoSize = descriptionObject.AddComponent<SpeechBubbleAutoSize>();
            }

            ConfigureAutoSize(descriptionAutoSize, descriptionText, descriptionRect);
            ConfigureAutoSizeDefaults(descriptionAutoSize, isSpeech: false);
        }

        ConfigureDialogueAutoSizeForFollow();
    }

    private static void ConfigureAutoSizeDefaults(SpeechBubbleAutoSize autoSize, bool isSpeech)
    {
        if (autoSize == null)
        {
            return;
        }

        if (autoSize.maxTextWidth < 100f)
        {
            autoSize.maxTextWidth = isSpeech ? 560f : 720f;
        }

        if (autoSize.padding.sqrMagnitude < 1f)
        {
            autoSize.padding = isSpeech
                ? new Vector2(160f, 90f)
                : new Vector2(180f, 70f);
        }

        if (autoSize.minBubbleSize.sqrMagnitude < 1f)
        {
            autoSize.minBubbleSize = isSpeech
                ? new Vector2(600f, 160f)
                : new Vector2(640f, 120f);
        }
        else if (isSpeech)
        {
            autoSize.minBubbleSize = new Vector2(
                Mathf.Max(autoSize.minBubbleSize.x, 600f),
                Mathf.Max(autoSize.minBubbleSize.y, 160f));
        }
    }

    private void RefreshDialogueAutoSize()
    {
        if (speechAutoSize != null &&
            speechBubbleObject != null &&
            speechBubbleObject.activeInHierarchy)
        {
            speechAutoSize.ResizeBubble();
        }

        if (descriptionAutoSize != null &&
            descriptionObject != null &&
            descriptionObject.activeInHierarchy)
        {
            descriptionAutoSize.ResizeBubble();
        }
    }

    private static void RestoreDialogueBackgroundGraphics(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null)
            {
                continue;
            }

            string lowerName = image.gameObject.name.ToLowerInvariant();
            bool isDimOrShadow =
                lowerName.Contains("shadow") ||
                lowerName.Contains("dim") ||
                lowerName.Contains("black");
            if (isDimOrShadow)
            {
                continue;
            }

            image.enabled = true;
            Color color = image.color;
            if (color.a < 0.99f)
            {
                color.a = 1f;
                image.color = color;
            }
        }

        CanvasGroup[] groups = root.GetComponentsInChildren<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] == null)
            {
                continue;
            }

            groups[i].alpha = 1f;
            groups[i].interactable = true;
            groups[i].blocksRaycasts = true;
        }
    }


private void UpdateDialogueFollowDurry()
    {
        if (!pinDialogueBelowDurry || !Application.isPlaying)
        {
            return;
        }

        EnsureDialogueFollowRoot();
        if (dialogueFollowRoot == null)
        {
            return;
        }

        bool dialogueVisible = IsDialoguePanelVisible();
        if (dialogueFollowRoot.gameObject.activeSelf != dialogueVisible)
        {
            dialogueFollowRoot.gameObject.SetActive(dialogueVisible);
        }

        if (!dialogueVisible)
        {
            return;
        }

        EnsureExclusiveSpeechBubble();

        Vector3 anchor = ResolveDialogueWorldAnchor();
        dialogueFollowRoot.position = anchor;
        dialogueFollowRoot.localScale = Vector3.one * Mathf.Max(0.0001f, dialogueWorldScale);

        if (dialogueFaceUser && centerEyeAnchor != null)
        {
            // WorldCanvas FaceCanvasToUser와 동일한 규약:
            // Canvas +Z가 카메라에서 멀어지는 쪽을 향해야 글자/배경이 좌우반전되지 않습니다.
            Vector3 awayFromCamera = dialogueFollowRoot.position - centerEyeAnchor.position;
            awayFromCamera.y = 0f;
            if (awayFromCamera.sqrMagnitude < 0.0001f)
            {
                awayFromCamera = GetFlatForward();
            }

            dialogueFollowRoot.rotation = Quaternion.LookRotation(awayFromCamera.normalized, Vector3.up);
        }

        EnsureDialogueGraphicsVisible();
        EnsureDialogueAutoSizeComponents();
        RefreshDialogueAutoSize();
    }

private Vector3 ResolveDialogueWorldAnchor()
    {
        Transform durryTransform = durryObject != null ? durryObject.transform : null;
        bool durryVisible =
            durryTransform != null &&
            durryTransform.gameObject.activeInHierarchy &&
            (durryVisual == null || durryVisual.gameObject.activeInHierarchy);

        if (durryVisible)
        {
            Bounds bounds;
            if (TryGetDurryVisualBounds(out bounds))
            {
                // 더리 바운즈 하단에서 20cm 아래 + 사용자 쪽으로 살짝 당겨 몸과 겹치지 않게 합니다.
                float gap = Mathf.Max(0.20f, dialogueBelowDurryGap);
                Vector3 anchor = new Vector3(
                    bounds.center.x,
                    bounds.min.y - gap,
                    bounds.center.z);
                if (centerEyeAnchor != null)
                {
                    Vector3 towardUser = centerEyeAnchor.position - anchor;
                    towardUser.y = 0f;
                    if (towardUser.sqrMagnitude > 0.0001f)
                    {
                        anchor += towardUser.normalized * Mathf.Max(0.25f, dialogueTowardUserOffset);
                    }
                }

                return anchor;
            }

            Vector3 fallback = durryTransform.position +
                               Vector3.down * (0.35f + dialogueBelowDurryGap);
            if (centerEyeAnchor != null)
            {
                Vector3 towardUser = centerEyeAnchor.position - fallback;
                towardUser.y = 0f;
                if (towardUser.sqrMagnitude > 0.0001f)
                {
                    fallback += towardUser.normalized * dialogueTowardUserOffset;
                }
            }

            return fallback;
        }

        if (centerEyeAnchor != null)
        {
            Vector3 forward = GetFlatForward();
            return centerEyeAnchor.position + forward * 1.2f + Vector3.up * -0.35f;
        }

        return dialogueFollowRoot != null ? dialogueFollowRoot.position : Vector3.zero;
    }

    private bool TryGetDurryVisualBounds(out Bounds bounds)
    {
        bounds = default;
        Transform searchRoot = durryVisual != null
            ? durryVisual
            : (durryObject != null ? durryObject.transform : null);
        if (searchRoot == null)
        {
            return false;
        }

        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(false);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            string lowerName = renderer.name.ToLowerInvariant();
            if (lowerName.Contains("shadow") || lowerName.Contains("bubble"))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }


private void UpdateDialoguePlacement()
    {
        if (pinDialogueBelowDurry)
        {
            RectTransform bubble = ResolveSpeechBubbleRect();
            RectTransform description = ResolveDescriptionRect();
            ApplyDialogueRectPlacement(bubble, Vector2.zero, forceSpeechBubbleCenterAnchor);
            ApplyDialogueRectPlacement(description, Vector2.zero, forceDescriptionCenterAnchor);

            if (forceSameDialogueWorldPosition &&
                bubble != null &&
                description != null &&
                bubble.parent != description.parent)
            {
                description.position = bubble.position;
                description.rotation = bubble.rotation;
            }

            ConfigureDialogueAutoSizeForFollow();
            return;
        }

        if (!synchronizeDialoguePosition)
        {
            UpdateSpeechBubblePlacement();
            UpdateDescriptionPlacement();
            return;
        }

        RectTransform syncedBubble = ResolveSpeechBubbleRect();
        RectTransform syncedDescription = ResolveDescriptionRect();

        Vector2 targetPosition = sharedDialogueAnchoredPosition;
        if (clampDialogueAboveWaistNav)
        {
            targetPosition.y = Mathf.Max(targetPosition.y, sharedDialogueMinAnchoredY);
        }

        ApplyDialogueRectPlacement(
            syncedBubble,
            targetPosition,
            forceSpeechBubbleCenterAnchor
        );

        ApplyDialogueRectPlacement(
            syncedDescription,
            targetPosition,
            forceDescriptionCenterAnchor
        );

        if (forceSameDialogueWorldPosition &&
            syncedBubble != null &&
            syncedDescription != null &&
            syncedBubble.parent != syncedDescription.parent)
        {
            syncedDescription.position = syncedBubble.position;
            syncedDescription.rotation = syncedBubble.rotation;
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

    private Button FindButtonByExactNames(Transform root, params string[] exactNames)
    {
        if (root == null || exactNames == null || exactNames.Length == 0)
        {
            return null;
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            foreach (string exactName in exactNames)
            {
                if (!string.IsNullOrWhiteSpace(exactName) &&
                    string.Equals(button.gameObject.name, exactName, StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }
        }

        return null;
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
            // 실제 페이지 리프(MenuPage/NotePage)를 Root보다 우선합니다.
            // Root를 잡으면 BigNoteRoot에 가려지거나 상점/마이페이지와 다른 경로가 됩니다.
            if (lowerName == normalizedKeyword + "page") score += 20;
            if (lowerName == "my" + "page" && normalizedKeyword == "mypage") score += 20;
            if (lowerName.EndsWith("pageroot")) score -= 10;
            if (lowerName.EndsWith("root") && !lowerName.EndsWith("pageroot")) score -= 5;
            if (candidate.GetComponent<RectTransform>() != null) score += 1;
            if (candidate.GetComponentInChildren<DustinyItemPageController>(true) != null) score += 8;

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

    private Transform FindDialogueDescriptionTransform(Transform root)
    {
        if (root == null)
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
            if (!lowerName.Contains("description"))
            {
                continue;
            }

            // 상점/마이페이지 아이템 설명 UI는 더리 대화 Description과 분리합니다.
            if (lowerName.Contains("itemdescription") ||
                lowerName.StartsWith("itemname") ||
                lowerName.Contains("informationbox"))
            {
                continue;
            }

            return child;
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


private void EnterPostTutorialMainState()
    {
        bool returnedFromMiniGame = ConsumeResumeIdleAfterMiniGame();

        introTutorialActive = false;
        introTutorialState = IntroTutorialState.Finished;
        onboardingActive = false;
        onboardingState = OnboardingState.Finished;
        awaitingResetConfirmation = false;
        awaitingMiniGameStartConfirmation = false;
        followDurryDuringOnboarding = true;

        wasRightIndexPinching = true;
        wasRightMiddlePinching = true;
        wasRightRingPinching = true;
        wasLeftPinkyPinching = true;
        wasTriggerPressed = true;
        SuppressGlobalConfirmInput(1f);

        RebindMainSceneReferences();
        ResolveImageIntroReferences();
        ResolveDialogueReferences();
        HideAllModalUiAfterMiniGame();

        if (uiRoot != null)
        {
            uiRoot.gameObject.SetActive(true);
        }

        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
        }

        SetDurryActive(true);
        ApplyDurryScale();
        ApplyCurrentCleanlinessMaterial(force: true);
        EnsureUiPointerModule();
        RestoreWaistNavInteraction();
        SummonDurryToUser();

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<DustinyMissionController>();
        }

        if (missionController != null)
        {
            missionController.UnlockMissionNoteAfterOpening();
        }

        QuestStatusUI questStatusUI = FindFirstObjectByType<QuestStatusUI>();
        questStatusUI?.RebindSceneUiReferences();
        questStatusUI?.RefreshMissionSlots();

        HideDialoguePanels();
        StartCoroutine(EnsureDurryVisibleRoutine());
        UpdateDialoguePlacement();
        RefreshNoteButtonGlow();

        if (returnedFromMiniGame)
        {
            StartCoroutine(ShowThankYouSpeechNextFrame());
        }
        else
        {
            StartCoroutine(ShowCleanlinessGreetingNextFrame());
        }
    }

private void EnterMainSceneFlow()
    {
        if (HasCompletedIntroTutorial())
        {
            EnterPostTutorialMainState();
            return;
        }

        if (showImageIntroBeforeOpening)
        {
            BeginImageIntroTutorial();
            return;
        }

        StartOpeningAfterImageIntro();
    }

    private static bool HasCompletedIntroTutorial()
    {
        return PlayerPrefs.GetInt("Dustiny_HasCompletedIntroTutorial", 0) == 1;
    }

    private static void MarkIntroTutorialCompleted()
    {
        PlayerPrefs.SetInt("Dustiny_HasCompletedIntroTutorial", 1);
        PlayerPrefs.Save();
    }

    private static void ClearIntroTutorialCompleted()
    {
        if (!PlayerPrefs.HasKey("Dustiny_HasCompletedIntroTutorial"))
        {
            return;
        }

        PlayerPrefs.DeleteKey("Dustiny_HasCompletedIntroTutorial");
        PlayerPrefs.Save();
    }






    private void ShowThankYouSpeechAfterMiniGame()
    {
        const string thankYouMessage = "내 인형을 깨끗하게\n세탁해줘서 고마워!";
        string joyfulState = string.IsNullOrWhiteSpace(successExpressionState)
            ? "Joyful"
            : successExpressionState;

        // 감사 대사는 Description이 아니라 SpeechBubble만 사용
        ShowSpeechBubbleOnly(thankYouMessage, joyfulState, ResolveVoiceForMessage(thankYouMessage));
        UpdateDialogueFollowDurry();
    }

    private IEnumerator ShowThankYouSpeechNextFrame()
    {
        // 씬 복귀 직후 참조/바운즈가 잡힐 때까지 잠시 대기 (접속 인사와 동일)
        yield return null;
        yield return null;
        wasRightIndexPinching = true;
        wasRightMiddlePinching = true;
        wasTriggerPressed = true;
        SuppressGlobalConfirmInput(1.5f);
        SummonDurryToUser();
        ShowThankYouSpeechAfterMiniGame();
    }

    private IEnumerator ShowCleanlinessGreetingNextFrame()
    {
        // 더리 소환/바운즈가 잡힌 뒤 말풍선을 띄워, 첫 프레임에 위치가 어긋나지 않게 합니다.
        yield return null;
        yield return null;
        wasRightIndexPinching = true;
        wasRightMiddlePinching = true;
        wasTriggerPressed = true;
        SuppressGlobalConfirmInput(1.25f);
        SummonDurryToUser();
        ShowCleanlinessSessionGreeting();
    }

    /// <summary>
    /// 앱/메인 씬에 들어올 때마다 보송력(0~4)에 맞는 인사와 표정을 보여 줍니다.
    /// </summary>
    private void ShowCleanlinessSessionGreeting()
    {
        int score = CleanlinessManager.Instance != null
            ? CleanlinessManager.Instance.CleanlinessScore
            : 1;

        string message;
        string expression;
        AudioClip voice;

        if (score <= 1)
        {
            message = score <= 0
                ? "몸이 너무 더러워서 우울해... \n우리 청소하는 거 어때?"
                : "보송력이 떨어져서 슬퍼... \n청소 부탁해도 될까?";
            expression = string.IsNullOrWhiteSpace(failureExpressionState)
                ? "Sad"
                : failureExpressionState;
            voice = FirstAssignedClip(voiceSad, voiceTearful, voiceDizzy, voiceWaiting);
        }
        else if (score == 2)
        {
            message = "오늘은 보송력이 보통이야. \n가끔 청소해 주면 좋겠어.";
            expression = string.IsNullOrWhiteSpace(concernExpressionState)
                ? "Worried"
                : concernExpressionState;
            voice = FirstAssignedClip(voiceWaiting, voiceDizzy, voiceStart, voiceRequestClean);
        }
        else
        {
            message = score >= 4
                ? "또 왔네? \n나 완전 깨끗해서 기분 최고야!"
                : "또 왔네? \n반가워! 오늘도 잘 부탁해.";
            expression = string.IsNullOrWhiteSpace(successExpressionState)
                ? "Joyful"
                : successExpressionState;
            voice = FirstAssignedClip(voiceJoy, voiceHappy, voiceCheerful, voiceStart);
        }

        // ShowDescription → ShowSpeechBubbleOnly (말풍선 전용)
        ShowDescription(message, expression, voice);
        UpdateDialogueFollowDurry();
        Debug.Log($"[Dustiny Greeting] 보송력={score} → {message}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionFlags()
    {
        ResumeIdleAfterMiniGame = false;
    }


public static void MarkResumeIdleAfterMiniGame()
    {
        ResumeIdleAfterMiniGame = true;
    }

public static void ClearResumeIdleAfterMiniGame()
    {
        ResumeIdleAfterMiniGame = false;
        if (PlayerPrefs.HasKey("Dustiny_ResumeIdleAfterMiniGame"))
        {
            PlayerPrefs.DeleteKey("Dustiny_ResumeIdleAfterMiniGame");
            PlayerPrefs.Save();
        }
    }


private static bool PeekResumeIdleAfterMiniGame()
    {
        return ResumeIdleAfterMiniGame;
    }

private static bool ConsumeResumeIdleAfterMiniGame()
    {
        bool resume = ResumeIdleAfterMiniGame;
        ResumeIdleAfterMiniGame = false;
        if (PlayerPrefs.HasKey("Dustiny_ResumeIdleAfterMiniGame"))
        {
            PlayerPrefs.DeleteKey("Dustiny_ResumeIdleAfterMiniGame");
            PlayerPrefs.Save();
        }

        return resume;
    }

private void HideAllModalUiAfterMiniGame()
    {
        HideDialoguePanels();
        SetPageRootVisible(false);
        SetScanZoneVisible(false);

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        DisableNamedTransform(searchRoot, "GuestureIntro");
        DisableNamedTransform(searchRoot, "GestureIntro");
        DisableNamedTransform(searchRoot, "Intro");
        DisableNamedTransform(searchRoot, "ImageIntro");
        DisableNamedTransform(searchRoot, "NameInputPanel");

        SetIntroObjectVisible(onboardingOKNoObject, false);
        SetIntroObjectVisible(onboardingOKObject, false);
        SetIntroObjectVisible(onboardingNoObject, false);
        SetIntroObjectVisible(onboardingNoDoneObject, false);
        SetIntroObjectVisible(navInformObject, false);
        SetIntroObjectVisible(navInformOKObject, false);

        if (introRootObject != null)
        {
            introRootObject.SetActive(false);
        }

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

        if (introFullscreenOverlayCanvas != null)
        {
            introFullscreenOverlayCanvas.gameObject.SetActive(false);
        }

        HideScreenSpaceModalCanvases();
    }

private void DisableNamedTransform(Transform searchRoot, string childName)
    {
        Transform found = FindChildTransformExact(searchRoot, childName);
        if (found != null)
        {
            found.gameObject.SetActive(false);
        }
    }

private void HideScreenSpaceModalCanvases()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            string canvasName = canvas.gameObject.name;
            if (canvasName.IndexOf("WaistNav", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            if (worldCanvas != null && (canvas == worldCanvas || canvas.transform.IsChildOf(worldCanvas.transform)))
            {
                continue;
            }

            bool isIntroOverlay =
                canvasName.IndexOf("Intro", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                canvasName.IndexOf("Onboarding", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                canvasName.IndexOf("Overlay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                canvasName.IndexOf("Fullscreen", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isIntroOverlay)
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay ||
                canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvas.gameObject.SetActive(false);
            }
        }
    }








private void Awake()
    {
        if (IsDuplicateDustinyManager())
        {
            enabled = false;
            return;
        }

        SceneManager.sceneLoaded -= HandleMainSceneLoaded;
        SceneManager.sceneLoaded += HandleMainSceneLoaded;

        // 튜토리얼 여부와 무관하게 Description은 항상 강제 OFF (씬 기본 Active 이중 표시 방지).
        ForceDescriptionHiddenAtBoot();

        if (!HasCompletedIntroTutorial() && !PeekResumeIdleAfterMiniGame())
        {
            return;
        }

        if (bigNoteRoot != null)
        {
            bigNoteRoot.SetActive(false);
        }

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        DisableNamedTransform(searchRoot, "GuestureIntro");
        DisableNamedTransform(searchRoot, "GestureIntro");
        DisableNamedTransform(searchRoot, "Description");
        DisableNamedTransform(searchRoot, "BigNoteRoot");
        DisableNamedTransform(searchRoot, "NameInputPanel");
    }

    private void ForceDescriptionHiddenAtBoot()
    {
        if (descriptionObject != null)
        {
            descriptionObject.SetActive(false);
        }

        Transform searchRoot = worldCanvas != null ? worldCanvas.transform : transform;
        DisableNamedTransform(searchRoot, "Description");
        SetDescriptionVisible(false);
    }



private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleMainSceneLoaded;
    }

private bool IsDuplicateDustinyManager()
    {
        return AppManager.Instance != null &&
               AppManager.Instance.gameObject != gameObject;
    }


    private void HandleMainSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying ||
            !completedFirstMainStart ||
            IsDuplicateDustinyManager())
        {
            return;
        }

        string sceneName = scene.name ?? string.Empty;
        if (sceneName.IndexOf("MainMR", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        StartCoroutine(RebindMainSceneAfterLoadRoutine());
    }

private IEnumerator RebindMainSceneAfterLoadRoutine()
    {
        yield return null;
        RebindMainSceneReferences();
        EnterMainSceneFlow();
        EnsureUiPointerModule();
        RestoreWaistNavInteraction();
    }



    private void ResetMainSceneBindingCache()
    {
        worldCanvas = null;
        uiRoot = null;
        durryObject = null;
        durryVisual = null;

        speechBubbleObject = null;
        speechText = null;
        speechBubbleRect = null;
        speechAutoSize = null;

        descriptionObject = null;
        descriptionText = null;
        descriptionRect = null;
        descriptionAutoSize = null;

        dialogueFollowRoot = null;
        dialogueFollowCanvas = null;
        dialogueFollowRootReady = false;
        dialogueGraphicsPrepared = false;

        bigNoteRoot = null;
        bigNoteRect = null;
        notePageObject = null;
        shopPageObject = null;
        myPageObject = null;
        menuPageObject = null;
        sideTagsRoot = null;
        introRootObject = null;

        navigationBarObject = null;
        waistNavFollow = null;
        durryNoteButton = null;
        shopButton = null;
        myPageButton = null;
        menuButton = null;
        gameButton = null;
        noteButtonGlow = null;
        rightHand = null;
    }

    private static GameObject FindSceneGameObjectByName(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Transform candidate in transforms)
        {
            if (candidate != null && candidate.name == exactName)
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    private void RebindMainSceneReferences()
    {
        ResetMainSceneBindingCache();

        GameObject eye = GameObject.Find("CenterEyeAnchor");
        if (eye != null)
        {
            centerEyeAnchor = eye.transform;
        }

        durryObject = GameObject.Find("DurryRoot");
        if (durryObject != null)
        {
            durryVisual = durryObject.transform.Find("Durry Visual") ??
                          durryObject.transform.Find("DurryVisual");
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null)
            {
                continue;
            }

            string canvasName = canvases[i].name.ToLowerInvariant();
            if (canvasName.Contains("worldcanvas") || canvasName == "world canvas")
            {
                worldCanvas = canvases[i];
                break;
            }
        }

        GameObject foundUiRoot = GameObject.Find("UIRoot") ?? GameObject.Find("UI Root");
        if (foundUiRoot != null)
        {
            uiRoot = foundUiRoot.transform;
        }

        missionController = FindFirstObjectByType<DustinyMissionController>();
        ResolveRightHandIfNeeded();
        ResolveLeftHandIfNeeded();
        ResolveUIRoot();
        ResolveDialogueReferences();
        ResolveNavigationReferences();
        ResolveWaistNavFollow();
        ResolveNavigationButtons();
        ConnectNavigationButtonEvents();
        ResolvePageReferences();
        ResolvePageBackgroundReferences();
        ResolvePageButtons();
        ConnectPageButtonEvents();
        ResolveMenuActionButtons();
        ConnectMenuActionButtonEvents();
        SetupWorldCanvas();
        SetupDurry();
        ApplyCurrentCleanlinessMaterial(force: true);
        EnsureUiPointerModule();
        EnsureNoteButtonGlow();
        RefreshNoteButtonGlow();

        QuestStatusUI statusUI = FindFirstObjectByType<QuestStatusUI>();
        statusUI?.RebindSceneUiReferences();
        statusUI?.RefreshMissionSlots();
    }


}
