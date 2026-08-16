using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dustiny 스타트 씬 전체를 관리합니다.
///
/// 담당 기능:
/// - 시작 UI 그룹 표시
/// - StartViewRoot를 CenterEyeAnchor 아래에 배치
/// - 시작 화면 배경음악 반복 재생
/// - Start 버튼 선택 시 Yes 효과음 재생
/// - 효과음 재생 후 메인 씬 로드
///
/// 주의:
/// - StartCanvas와 DurryAnchor의 위치 및 크기는 건드리지 않습니다.
/// - 두 오브젝트는 StartViewRoot의 자식으로 직접 배치하세요.
/// </summary>
[DisallowMultipleComponent]
public class StartUIManager : MonoBehaviour
{
    [Header("UI Groups")]
    [Tooltip("처음 표시되는 시작 화면 그룹입니다.")]
    public GameObject startUIGroup;

    [Tooltip("네비게이션 안내 화면입니다.")]
    public GameObject navInformGroup;

    [Tooltip("네비게이션 안내 확인 화면입니다.")]
    public GameObject navInformOKGroup;

    [Header("Center Eye View")]
    [Tooltip("OVRCameraRig/TrackingSpace/CenterEyeAnchor를 연결하세요.")]
    public Transform centerEyeAnchor;

    [Tooltip("CenterEyeAnchor 아래의 StartViewRoot를 연결하세요.")]
    public Transform startViewRoot;

    [Tooltip("StartViewRoot의 부모가 CenterEyeAnchor가 아니면 자동으로 이동합니다.")]
    public bool parentViewRootToCenterEye = true;

    [Tooltip("시작할 때 아래 Local Position과 Rotation을 적용합니다.")]
    public bool applyViewRootPoseOnStart = true;

    [Tooltip("사용자의 눈으로부터 시작 화면까지의 위치입니다.")]
    public Vector3 viewRootLocalPosition = new Vector3(0f, -0.03f, 1.3f);

    public Vector3 viewRootLocalEuler = Vector3.zero;

    [Header("Main Scene")]
    [Tooltip("불러올 메인 씬의 정확한 이름입니다. .unity는 적지 않습니다.")]
    public string mainSceneName = "MainMRScene_ISDK_Test";

    [Header("Start Scene Audio")]
    [Tooltip("스타트 화면에서 반복 재생할 배경음악입니다.")]
    public AudioClip backgroundMusicClip;

    [Tooltip("Start 버튼을 선택했을 때 재생할 Yes 효과음입니다.")]
    public AudioClip yesSfxClip;

    [Tooltip("비워두면 StartManager에 자동 생성합니다.")]
    public AudioSource backgroundMusicSource;

    [Tooltip("비워두면 StartManager에 자동 생성합니다.")]
    public AudioSource sfxSource;

    public bool autoCreateAudioSources = true;
    public bool playBackgroundMusicOnStart = true;

    [Range(0f, 1f)]
    public float backgroundMusicVolume = 0.35f;

    [Range(0f, 1f)]
    public float yesSfxVolume = 1f;

    [Tooltip("Yes 효과음을 기다리는 최대 시간입니다.")]
    [Min(0f)]
    public float maxSceneLoadDelay = 1.2f;

    private bool isStartingGame;

    public bool IsStartingGame => isStartingGame;

    private void Awake()
    {
        ResolveReferences();
        SetupViewRoot();
        SetupAudioSources();
    }

    private void Start()
    {
        ShowStartUI();

        if (playBackgroundMusicOnStart)
        {
            PlayBackgroundMusic();
        }
    }

    private void ResolveReferences()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        if (centerEyeAnchor == null)
        {
            centerEyeAnchor = FindTransformByExactName(
                allTransforms,
                "CenterEyeAnchor"
            );
        }

        if (startViewRoot == null)
        {
            startViewRoot = FindTransformByExactName(
                allTransforms,
                "StartViewRoot"
            );
        }

        if (startUIGroup == null)
        {
            Transform found = FindTransformByExactName(
                allTransforms,
                "StartUI_Group"
            );

            if (found != null)
            {
                startUIGroup = found.gameObject;
            }
        }

        if (navInformGroup == null)
        {
            Transform found = FindTransformByExactName(
                allTransforms,
                "NavInform_Group"
            );

            if (found != null)
            {
                navInformGroup = found.gameObject;
            }
        }

        if (navInformOKGroup == null)
        {
            Transform found = FindTransformByExactName(
                allTransforms,
                "NavInformOK_Group"
            );

            if (found != null)
            {
                navInformOKGroup = found.gameObject;
            }
        }
    }

    private static Transform FindTransformByExactName(
        Transform[] transforms,
        string exactName
    )
    {
        if (transforms == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        foreach (Transform candidate in transforms)
        {
            if (candidate != null && candidate.name == exactName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void SetupViewRoot()
    {
        if (centerEyeAnchor == null)
        {
            Debug.LogError(
                "[StartUIManager] CenterEyeAnchor를 찾지 못했습니다."
            );
            return;
        }

        if (startViewRoot == null)
        {
            Debug.LogError(
                "[StartUIManager] StartViewRoot를 찾지 못했습니다."
            );
            return;
        }

        if (parentViewRootToCenterEye &&
            startViewRoot.parent != centerEyeAnchor)
        {
            startViewRoot.SetParent(centerEyeAnchor, false);
        }

        if (applyViewRootPoseOnStart)
        {
            startViewRoot.localPosition = viewRootLocalPosition;
            startViewRoot.localRotation = Quaternion.Euler(viewRootLocalEuler);
            startViewRoot.localScale = Vector3.one;
        }
    }

    private void SetupAudioSources()
    {
        if (backgroundMusicSource == null &&
            autoCreateAudioSources)
        {
            backgroundMusicSource = gameObject.AddComponent<AudioSource>();
            backgroundMusicSource.name = "Start Background Music Source";
        }

        if (sfxSource == null &&
            autoCreateAudioSources)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.name = "Start SFX Source";
        }

        if (backgroundMusicSource != null)
        {
            backgroundMusicSource.playOnAwake = false;
            backgroundMusicSource.loop = true;
            backgroundMusicSource.spatialBlend = 0f;
            backgroundMusicSource.volume =
                Mathf.Clamp01(backgroundMusicVolume);
        }

        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.volume = Mathf.Clamp01(yesSfxVolume);
        }
    }

    public void PlayBackgroundMusic()
    {
        SetupAudioSources();

        if (backgroundMusicSource == null ||
            backgroundMusicClip == null)
        {
            return;
        }

        backgroundMusicSource.clip = backgroundMusicClip;
        backgroundMusicSource.loop = true;
        backgroundMusicSource.volume =
            Mathf.Clamp01(backgroundMusicVolume);

        if (!backgroundMusicSource.isPlaying)
        {
            backgroundMusicSource.Play();
        }
    }

    public void StopBackgroundMusic()
    {
        if (backgroundMusicSource != null)
        {
            backgroundMusicSource.Stop();
        }
    }

    public void PlayYesSfx()
    {
        SetupAudioSources();

        if (sfxSource == null || yesSfxClip == null)
        {
            return;
        }

        sfxSource.Stop();
        sfxSource.volume = Mathf.Clamp01(yesSfxVolume);
        sfxSource.PlayOneShot(yesSfxClip);
    }

    public void ShowStartUI()
    {
        SetGroupActive(startUIGroup, true);
        SetGroupActive(navInformGroup, false);
        SetGroupActive(navInformOKGroup, false);
    }

    public void ToNavInform()
    {
        if (isStartingGame)
        {
            return;
        }

        SetGroupActive(startUIGroup, false);
        SetGroupActive(navInformGroup, true);
        SetGroupActive(navInformOKGroup, false);
    }

    public void ToNavInformOK()
    {
        if (isStartingGame)
        {
            return;
        }

        SetGroupActive(startUIGroup, false);
        SetGroupActive(navInformGroup, false);
        SetGroupActive(navInformOKGroup, true);
    }

    public void ReturnToStartUI()
    {
        if (isStartingGame)
        {
            return;
        }

        ShowStartUI();
    }

    /// <summary>
    /// Start 버튼이 호출하는 게임 시작 함수입니다.
    /// </summary>
    public void StartGame()
    {
        if (isStartingGame)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mainSceneName))
        {
            Debug.LogError(
                "[StartUIManager] Main Scene Name이 비어 있습니다."
            );
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(mainSceneName))
        {
            Debug.LogError(
                $"[StartUIManager] '{mainSceneName}' 씬을 불러올 수 없습니다.\n" +
                "File > Build Profiles > Scene List에 메인 씬을 추가하고 " +
                "씬 이름을 정확하게 입력하세요."
            );
            return;
        }

        StartCoroutine(LoadMainSceneRoutine());
    }

    /// <summary>
    /// 안내 화면의 마지막 확인 버튼에서 연결할 수 있습니다.
    /// </summary>
    public void ConfirmNavAndStartGame()
    {
        StartGame();
    }

    private IEnumerator LoadMainSceneRoutine()
    {
        isStartingGame = true;

        PlayYesSfx();

        float delay = 0f;

        if (yesSfxClip != null)
        {
            delay = Mathf.Min(
                yesSfxClip.length,
                Mathf.Max(0f, maxSceneLoadDelay)
            );
        }

        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        SceneManager.LoadScene(
            mainSceneName,
            LoadSceneMode.Single
        );
    }

    private static void SetGroupActive(
        GameObject target,
        bool active
    )
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }
}