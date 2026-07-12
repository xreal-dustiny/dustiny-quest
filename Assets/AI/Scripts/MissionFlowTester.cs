using System.Collections;
using System.Collections.Generic;
using System.Linq;

using TMPro;
using UnityEngine;

/// <summary>
/// 실제 QuestGenerator가 연결되기 전,
/// AI 테스트 씬에서 정리 전 스캔 → 임시 미션 → 정리 후 스캔
/// 흐름을 테스트하기 위한 스크립트.
/// </summary>
public class AIMissionFlowTester : MonoBehaviour
{
    public enum AITestPhase
    {
        BeforeScan,
        MissionInProgress,
        AfterScan,
        Result
    }

    [Header("[ 필수 연결 ]")]
    public QuestCameraYoloTester questCameraYoloTester;

    [Header("[ 임시 미션 UI ]")]
    public GameObject missionPanel;
    public TMP_Text missionText;

    [Header("[ 테스트 입력 ]")]
    [Tooltip(
        "현재는 A 버튼으로 진행합니다. " +
        "손 인식 UI 연결 후에는 해제합니다."
    )]
    public bool useDebugAButton = true;

    public AITestPhase CurrentPhase
    {
        get;
        private set;
    } = AITestPhase.BeforeScan;

    private bool waitingForScanResult;
    private bool pendingBeforeScan;

    private void Awake()
    {
        if (questCameraYoloTester == null)
        {
            questCameraYoloTester =
                FindFirstObjectByType<
                    QuestCameraYoloTester
                >();
        }
    }

    private void OnEnable()
    {
        if (questCameraYoloTester != null)
        {
            questCameraYoloTester.OnScanCompleted +=
                HandleScanCompleted;
        }
    }

    private IEnumerator Start()
    {
        // QuestCameraYoloTester의 Start()가 먼저 UI를 초기화하도록
        // 한 프레임 기다린 뒤 안내 문구를 표시
        yield return null;

        ShowBeforeScanPrompt();
    }

    private void OnDisable()
    {
        if (questCameraYoloTester != null)
        {
            questCameraYoloTester.OnScanCompleted -=
                HandleScanCompleted;
        }
    }

    private void Update()
    {
        if (!useDebugAButton ||
            waitingForScanResult ||
            questCameraYoloTester == null ||
            questCameraYoloTester.IsScanning)
        {
            return;
        }

        bool pressedA =
            OVRInput.GetDown(
                OVRInput.Button.One,
                OVRInput.Controller.RTouch
            );

        if (pressedA)
        {
            ConfirmCurrentPhase();
        }
    }

    /// <summary>
    /// 현재 단계에 따라 다음 동작을 실행한다.
    /// 나중에 핀치 확인 UI에서도 이 함수를 호출할 수 있다.
    /// </summary>
    public void ConfirmCurrentPhase()
    {
        if (waitingForScanResult ||
            questCameraYoloTester == null ||
            questCameraYoloTester.IsScanning)
        {
            return;
        }

        switch (CurrentPhase)
        {
            case AITestPhase.BeforeScan:
                StartBeforeScan();
                break;

            case AITestPhase.MissionInProgress:
                StartAfterScan();
                break;

            case AITestPhase.Result:
                ResetMissionFlow();
                break;
        }
    }

    /// <summary>
    /// 정리 전 스캔을 시작한다.
    /// </summary>
    public void StartBeforeScan()
    {
        BeginScan(
            isBeforeScan: true
        );
    }

    /// <summary>
    /// 임시 미션 수행 후 정리 결과 스캔을 시작한다.
    /// </summary>
    public void StartAfterScan()
    {
        BeginScan(
            isBeforeScan: false
        );
    }

    private void BeginScan(
        bool isBeforeScan
    )
    {
        if (questCameraYoloTester == null)
        {
            Debug.LogError(
                "[AI Mission Flow] " +
                "QuestCameraYoloTester가 없습니다."
            );

            return;
        }

        waitingForScanResult = true;
        pendingBeforeScan = isBeforeScan;

        CurrentPhase =
            isBeforeScan
                ? AITestPhase.BeforeScan
                : AITestPhase.AfterScan;

        SetMissionText(
            isBeforeScan
                ? "정리할 물건을 찾고 있어요..."
                : "정리 결과를 확인하고 있어요..."
        );

        questCameraYoloTester.StartScan();

        // 권한이나 컴포넌트 문제로 스캔이 시작되지 않은 경우
        if (!questCameraYoloTester.IsScanning)
        {
            waitingForScanResult = false;

            CurrentPhase =
                isBeforeScan
                    ? AITestPhase.BeforeScan
                    : AITestPhase.MissionInProgress;

            SetMissionText(
                "스캔을 시작하지 못했어요.\n" +
                "카메라 연결 상태를 확인해 주세요."
            );
        }
    }

    /// <summary>
    /// QuestCameraYoloTester의 스캔 완료 이벤트를 받는다.
    /// </summary>
    private void HandleScanCompleted(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        if (!waitingForScanResult)
        {
            return;
        }

        waitingForScanResult = false;

        if (pendingBeforeScan)
        {
            HandleBeforeScanCompleted(
                confirmedObjects
            );
        }
        else
        {
            HandleAfterScanCompleted(
                confirmedObjects
            );
        }
    }

    // 정리 전 스캔 완료 후 임시 미션을 생성한다.
    private void HandleBeforeScanCompleted(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        int objectCount =
            confirmedObjects?.Count ?? 0;

        if (objectCount == 0)
        {
            CurrentPhase =
                AITestPhase.Result;

            SetMissionText(
                "정리할 물건을 찾지 못했어요!\n\n" +
                $"현재 정돈도: " +
                $"{questCameraYoloTester.BeforeTidinessScore}점\n\n" +
                "A 버튼을 눌러 다시 시작하세요."
            );

            return;
        }

        CurrentPhase =
            AITestPhase.MissionInProgress;

        ShowTemporaryMissions(
            confirmedObjects
        );
    }

    // 정리 후 스캔 완료 결과를 표시한다.
    private void HandleAfterScanCompleted(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        CurrentPhase =
            AITestPhase.Result;

        int beforeScore =
            questCameraYoloTester.BeforeTidinessScore;

        int afterScore =
            questCameraYoloTester.AfterTidinessScore;

        int scoreDifference =
            afterScore -
            beforeScore;

        string differenceText =
            scoreDifference > 0
                ? $"+{scoreDifference}"
                : scoreDifference.ToString();

        int remainingObjectCount =
            confirmedObjects?.Count ?? 0;

        SetMissionText(
            "정리 결과 ✦\n\n" +
            $"정리 전: {beforeScore}점\n" +
            $"정리 후: {afterScore}점\n" +
            $"점수 변화: {differenceText}점\n\n" +
            $"남은 정리 대상: " +
            $"{remainingObjectCount}개\n\n" +
            "A 버튼을 누르면 다시 시작해요!"
        );
    }

    // 확정 객체를 클래스별로 묶어 임시 미션 문구를 만든다.
    private void ShowTemporaryMissions(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        IEnumerable<string> missionLines =
            confirmedObjects
                .GroupBy(item =>
                    item.className
                )
                .OrderByDescending(group =>
                    group.Count()
                )
                .ThenBy(group =>
                    group.Key
                )
                .Select(group =>
                    $"• {GetObjectDisplayName(group.Key)} " +
                    $"{group.Count()}개 정리하기"
                );

        string missionSummary =
            string.Join(
                "\n",
                missionLines
            );

        SetMissionText(
            "오늘의 정리 미션 ✦\n\n" +
            missionSummary +
            "\n\n정리가 끝나면 " +
            "A 버튼을 눌러주세요!"
        );
    }

    // YOLO 클래스 이름을 사용자용 이름으로 변환한다.
    private string GetObjectDisplayName(
        string className
    )
    {
        switch (className)
        {
            case "cup_bottle":
                return "컵·병";

            case "paper_book":
                return "책·종이";

            case "writing_tool":
                return "필기구";

            case "small_device":
                return "소형 전자기기";

            case "cable_charger":
                return "케이블·충전기";

            case "computer_keyboard":
                return "컴퓨터·키보드";

            case "toy_decor":
                return "장난감·장식품";

            case "trash":
                return "쓰레기";

            default:
                return string.IsNullOrEmpty(
                    className
                )
                    ? "물건"
                    : className.Replace(
                        "_",
                        " "
                    );
        }
    }

    /// <summary>
    /// 새로운 테스트를 위해 흐름과 점수를 초기화한다.
    /// </summary>
    public void ResetMissionFlow()
    {
        waitingForScanResult = false;
        pendingBeforeScan = false;

        if (questCameraYoloTester != null)
        {
            questCameraYoloTester
                .ResetScoreComparison();
        }

        CurrentPhase =
            AITestPhase.BeforeScan;

        ShowBeforeScanPrompt();
    }

    private void ShowBeforeScanPrompt()
    {
        SetMissionText(
            "책상을 바라봐 주세요 ✦\n\n" +
            "A 버튼을 누르면\n" +
            "정리할 물건을 찾아볼게요!"
        );
    }

    private void SetMissionText(
        string message
    )
    {
        if (missionPanel != null)
        {
            missionPanel.SetActive(
                true
            );
        }

        if (missionText != null)
        {
            missionText.gameObject.SetActive(
                true
            );

            missionText.text =
                message;
        }
    }
}