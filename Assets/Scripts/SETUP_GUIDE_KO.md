# 더스티니 AI 미션 통합 설정 가이드

## 1. 먼저 교체할 스크립트

기존 파일을 **삭제하거나 덮어쓴 뒤**, 같은 클래스의 이전 버전이 프로젝트에 남지 않게 합니다.

반드시 교체:

- `DustinyDemoFlow.cs`
- `QuestCameraYoloTester.cs`
- `QuestData.cs`
- `QuestGenerator.cs`
- `QuestProgressManager.cs`
- `QuestStatusUI.cs`

새로 추가:

- `DustinyMissionController.cs`

동일 기능 유지본:

- `AppManager.cs`
- `AIInferenceTest.cs`
- `DetectionMarkerManager.cs`
- `CleanlinessManager.cs`
- `CreditManager.cs`

> `QuestGenerator(7).cs`, `QuestProgressManager(7).cs` 같은 이전 사본과 새 파일을 동시에 두면 클래스 중복 컴파일 오류가 납니다. 파일명 뒤 숫자가 붙은 이전 사본은 제거하세요.

---

## 2. 권장 Hierarchy

메인 씬의 기존 `OVRCameraRig`는 그대로 사용하고 AI 테스트 씬의 Camera Rig를 다시 넣지 않습니다.

```text
MainMRScene_ISDK_Test
├─ DustinyManager                         기존 유지
│  ├─ AppManager
│  ├─ DustinyDemoFlow
│  ├─ QuestGenerator
│  ├─ QuestProgressManager
│  ├─ CleanlinessManager
│  ├─ CreditManager
│  └─ QuestStatusUI                      항상 활성 상태 권장
│
├─ AIIntegrationRoot                     새 빈 오브젝트
│  ├─ AIInference                        AIInferenceTest
│  ├─ QuestCamera                        QuestCameraYoloTester
│  ├─ DetectionMarkers                   DetectionMarkerManager
│  └─ MissionSystem                      DustinyMissionController
│
├─ PassthroughCamera                     기존 또는 새 빈 오브젝트
│  └─ PassthroughCameraAccess
│
└─ OVRCameraRig                          기존 유지, 중복 생성 금지
   └─ TrackingSpace
      └─ CenterEyeAnchor
         ├─ UIRoot / WorldCanvas         기존 더스티니 UI
         ├─ ScanDimCanvas                스캔 중 반투명 오버레이
         │  └─ ScanDimOverlay
         └─ ScanStatusCanvas             스캔 상태 UI
            └─ ScanBox
               └─ ScanStatusText
```

`MissionSystem`은 `NotePage` 아래에 두면 안 됩니다. 노트가 비활성화될 때 스캔 이벤트를 받지 못하므로 항상 활성인 `AIIntegrationRoot` 아래에 둡니다.

---

## 3. DustinyManager 컴포넌트

### DustinyDemoFlow

기존 연결은 유지하고 아래만 확인합니다.

- `Real AI Mission Integration > Mission Controller`
  - `MissionSystem`의 `DustinyMissionController` 연결
- `Navigation Buttons > Durry Note Button`
  - 하단 네비게이션 바의 더리 노트 `Button` 연결
- `Input > Allow Controller Fallback`
  - 실제 Quest 테스트: **OFF**
  - 에디터에서 A/Trigger로 임시 테스트할 때만 ON

특수 제스처 필드는 제거되었습니다. 약지·중지 핀치로 시작/완료하지 않습니다.

### QuestGenerator

- `Max Generated Quests`: **3**
- `Credit Per Completed Object`: **10**
- `Default Suggested Action`: `organize`

AI가 여러 물체를 찾더라도 탐지 횟수와 평균 confidence가 높은 순서로 최대 3개를 미션으로 사용합니다.

### QuestProgressManager

- `Required Quests For Today`: **3**

세 번째 미션 완료 순간:

- 보송력 `+1`
- 추가 완료 보너스 코인은 없음
- 각 체크된 미션이 이미 `10 CR`씩 지급했으므로 총 지급량은 `체크 개수 × 10 CR`

### QuestStatusUI

`QuestStatusUI`는 비활성화되는 `NotePage`보다 `DustinyManager`처럼 항상 활성인 오브젝트에 붙이는 것을 권장합니다.

요약 UI는 기존 텍스트를 연결합니다.

`Mission Slots`의 Size를 **3**으로 두고 다음처럼 연결합니다.

| Slot | Root | Mission Text | Mission Check Button | Mission Check Object |
|---|---|---|---|---|
| 0 | `Mission` | 첫 번째 미션 TMP | 첫 번째 체크박스의 Button | 체크 표시 이미지 자식 |
| 1 | `Mission (1)` | 두 번째 미션 TMP | 두 번째 체크박스의 Button | 체크 표시 이미지 자식 |
| 2 | `Mission (2)` | 세 번째 미션 TMP | 세 번째 체크박스의 Button | 체크 표시 이미지 자식 |

중요:

- 체크박스 오브젝트에 `UnityEngine.UI.Button` 컴포넌트를 추가합니다.
- `Mission Check Object`에는 **버튼 전체가 아니라 체크 표시 이미지 자식만** 넣습니다.
- 체크박스 Button의 Inspector `On Click()`은 비워둬도 됩니다. `QuestStatusUI`가 자동 연결합니다.

---

## 4. AIIntegrationRoot 컴포넌트

### AIInferenceTest

`AIInference` 오브젝트에 추가합니다.

- `Model Asset`: 변환된 ONNX ModelAsset
- `Classes File`: 클래스 이름 txt
- `Confidence Threshold`: `0.4`
- `IoU Threshold`: `0.45`

classes 파일의 순서는 모델 학습 클래스 순서와 정확히 같아야 합니다.

### DetectionMarkerManager

`DetectionMarkers` 오브젝트에 추가합니다.

- `Passthrough Camera Access`: 씬의 `PassthroughCameraAccess` 연결
- `Marker Distance`: 우선 `1.0`
- `Marker Scale`: 우선 `0.12`
- `Flip Y`: 기존 테스트 결과가 맞았다면 ON 유지

마커는 이제 `QuestCameraYoloTester`가 자동으로 항상 표시하지 않고 `DustinyMissionController`가 현재 남은 3개 미션만 관리합니다.

### QuestCameraYoloTester

`QuestCamera` 오브젝트에 추가합니다.

필수 연결:

- `Passthrough Camera Access`: `PassthroughCameraAccess`
- `AI Inference Test`: `AIInference`
- `Detection Marker Manager`: `DetectionMarkers`
- `Scan Status Text`: `ScanStatusText`
- `Scan Box`: `ScanBox`
- `Scan Dim Overlay`: `ScanDimOverlay`

반드시 설정:

- `Use Debug A Button`: **OFF**
- `Auto Show Markers For Debug`: **OFF**
- `Compare Tidiness Score`: ON

권장 초기값:

- `Scan Duration`: `2.1`
- `Inference Interval`: `0.3`
- `Minimum Detection Count`: `3`
- `Minimum Average Confidence`: `0.4`
- `Tracking IoU Threshold`: `0.3`
- `Maximum Tracking Gap`: `2`

`ScanDimOverlay`의 Image는 검정색 Alpha 약 `0.2~0.3`, `Raycast Target` OFF로 둡니다. 완전 불투명 검정 이미지나 별도 Camera를 추가하면 패스스루가 다시 가려질 수 있습니다.

### DustinyMissionController

`MissionSystem` 오브젝트에 추가합니다.

필수 시스템 연결:

- `Quest Camera Yolo Tester`: `QuestCamera`
- `Quest Generator`: `DustinyManager`의 `QuestGenerator`
- `Quest Progress Manager`: `DustinyManager`의 `QuestProgressManager`
- `Detection Marker Manager`: `DetectionMarkers`
- `Quest Status UI`: `DustinyManager`의 `QuestStatusUI`
- `Demo Flow`: `DustinyManager`의 `DustinyDemoFlow`

버튼 연결:

- `Durry Note Navigation Button`: 하단바의 더리 노트 Button
- `Rescan Button`: NotePage 안의 재스캔 Button
- `Auto Connect Rescan Button`: ON

실제 규칙:

- `Required Mission Count`: **3**
- `Auto Open Note After Initial Scan`: ON 권장
- `Auto Open Note After Rescan`: ON 권장
- `Show Mission Markers`: ON
- `Matching IoU Threshold`: `0.05`
- `Maximum Center Distance Pixels`: `160`

---

## 5. 더리 노트에 재스캔 버튼 추가

`NotePage` 안에 다음 오브젝트를 추가합니다.

```text
NotePage
├─ Mission
├─ Mission (1)
├─ Mission (2)
└─ RescanButton
   └─ Text (TMP) : "재스캔"
```

`RescanButton`에 필요한 것:

- `Image`
- `Button`
- 현재 World Canvas에서 사용하는 Interaction SDK Ray/Poke 입력 설정

`On Click()`은 비워둬도 됩니다. `DustinyMissionController`의 `Auto Connect Rescan Button`이 연결합니다.

---

## 6. 공용 [확인] 버튼 연결

Opening 대사를 넘기고 최초 책상 스캔을 시작하는 실제 버튼을 선택합니다.

버튼의 `On Click()`에 다음을 연결합니다.

```text
DustinyManager
→ DustinyDemoFlow
→ OnConfirmButtonPressed()
```

이 버튼은 Interaction SDK의 일반 검지 선택으로 누릅니다. 중지·약지 전용 감지 코드는 사용하지 않습니다.

Opening 마지막 설명에서 [확인]을 누르면 자동으로 다음이 실행됩니다.

```text
DustinyDemoFlow.OnConfirmButtonPressed()
→ DustinyMissionController.BeginMissionScan()
→ QuestCameraYoloTester.TryStartBeforeScan()
```

---

## 7. 실제 런타임 흐름

```text
앱 시작
→ 더리 노트 Button.interactable = false
→ Opening 진행
→ 마지막 [확인]
→ 정리 전 AI 스캔
→ 확정 물체 중 3개 미션 생성
→ 노트 버튼 활성화
→ 노트에 3개 미션 표시
→ 사용자가 체크박스 직접 선택 또는 재스캔
→ 완료된 미션마다 10 CR
→ 3개 완료
→ 보송력 +1
→ 노트 자동 닫힘
→ 더리 노트 버튼 다시 비활성화
→ 마커 제거
```

재스캔은 정리 전 바운딩박스와 정리 후 바운딩박스의 클래스·위치를 비교합니다. 원래 위치에서 사라진 물체를 완료로 처리합니다. 동일 클래스가 여러 개여도 위치가 가장 가까운 결과끼리 먼저 대응합니다.

---

## 8. Play Mode 테스트 순서

1. `QuestProgressManager` 컴포넌트 메뉴에서 `Debug/Reset Today's Quest` 실행
2. Play 시작
3. 시작 직후 더리 노트 버튼이 회색/비활성인지 확인
4. Opening 마지막 [확인] 클릭
5. `스캔 중...` UI가 나타나는지 확인
6. Console에서 확정 물체 3개 이상인지 확인
7. 스캔 후 노트 버튼 활성화 및 노트 자동 열림 확인
8. 첫 체크박스 클릭 → `+10 CR`
9. 물건 하나 치우고 재스캔 → 해당 미션 자동 체크 확인
10. 총 3개 완료 → 보송력 `+1`, 총 `30 CR`, 노트 잠금 확인

---

## 9. 자주 발생하는 문제

### 노트가 시작부터 열림

- `DustinyDemoFlow > Big Note Starts Open`: OFF
- `DustinyMissionController > Durry Note Navigation Button` 연결 확인
- Play 중 Button의 `Interactable`이 false인지 확인

### 스캔 결과가 와도 미션이 안 뜸

- 새 `QuestCameraYoloTester.cs`로 교체했는지 확인
- `OnScanResultReady` 이벤트가 `FinishScan()`에서 호출되는 버전인지 확인
- `MissionSystem`이 활성 상태인지 확인
- 확정 물체가 3개 미만이면 미션을 생성하지 않으므로 Console의 탐지 개수 확인

### 재스캔을 두 번째로 못 함

- 새 버전은 정리 전 기준을 퀘스트 완료까지 유지합니다.
- 이전 `QuestCameraYoloTester`가 남아 있으면 첫 재스캔 후 비교 상태가 초기화될 수 있습니다.

### 체크박스가 눌리지만 체크 이미지가 안 뜸

- `Mission Check Object`에 체크 이미지 자식을 연결했는지 확인
- 버튼 전체 오브젝트를 `Mission Check Object`에 넣지 않았는지 확인

### 패스스루가 검정으로 가려짐

- AI 테스트 씬의 Camera Rig를 메인 씬에 중복 추가하지 않기
- ScanDimOverlay Alpha를 1로 두지 않기
- ScanDimOverlay의 Raycast Target OFF
- World Canvas 루트에 불투명 전체 화면 Image가 없는지 확인
