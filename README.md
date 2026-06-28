# Dustiny Quest

**Dustiny**는 Meta Quest 기반 XR 환경에서 AI Vision을 활용해 사용자의 방/책상 정리를 돕는 XR-AI 클린업 게임 프로젝트입니다.  
이 README는 GitHub 사용이 익숙하지 않은 팀원도 **GitHub Desktop과 Unity를 이용해 안전하게 에셋을 업로드하고 협업할 수 있도록** 정리한 팀 가이드입니다.

---

## Repository

```text
https://github.com/xreal-dustiny/dustiny-quest.git
```

---

## 0. 핵심 원칙

Dustiny는 XR, AI, Unity 씬, UI, 캐릭터, 3D 모델, 사운드가 함께 들어가는 프로젝트입니다.  
Unity 프로젝트는 충돌이 나기 쉬우므로 아무 파일이나 바로 `main`에 올리지 않습니다.

```text
main    = 발표/제출/시연 가능한 안정 버전
develop = 팀 작업을 합치는 통합 버전
개인 branch = 각자 작업하는 공간
```

기본 흐름은 아래와 같습니다.

```text
개인 브랜치 → Pull Request → develop → main
```

### 절대 지켜야 할 규칙

```text
1. main에 직접 push 금지
2. 작업 전 Fetch / Pull 하기
3. 각자 브랜치에서 작업하기
4. Pull Request로 develop에 합치기
5. Unity 에셋은 .meta 파일과 함께 커밋하기
6. 같은 Unity Scene을 여러 명이 동시에 수정하지 않기
7. 에셋은 Assets/Dustiny 안에 정리해서 넣기
8. 파일명은 영어 + 언더바 사용하기
9. 개인정보, 실제 방 사진, API Key는 GitHub에 올리지 않기
10. 발표 전날에는 새 기능 추가보다 안정화 우선
```

---

## 1. 먼저 설치해야 하는 것

### 필수 준비물

1. **GitHub Desktop**  
   <https://github.com/apps/desktop>

2. **Unity Hub / Unity**  
   팀 기준 Unity 버전: `6000.3.9f`

3. **GitHub 계정**

4. **레포 접근 권한**  
   `xreal-dustiny/dustiny-quest` 레포에 접근 권한이 있어야 합니다.

---

## 2. GitHub Desktop 로그인하기

1. GitHub Desktop 실행
2. `Sign in to GitHub.com` 클릭
3. 본인 GitHub 계정으로 로그인
4. 브라우저 인증 완료 후 GitHub Desktop으로 돌아오기

---

## 3. Dustiny 레포를 내 컴퓨터에 가져오기

GitHub Desktop에서 아래 메뉴를 선택합니다.

```text
File > Clone Repository
```

상단 탭에서 **URL**을 선택하고 아래 주소를 입력합니다.

```text
https://github.com/xreal-dustiny/dustiny-quest.git
```

`Local Path`는 본인이 찾기 쉬운 위치로 설정합니다.

추천 예시:

```text
C:\dustiny\dustiny-quest
```

피하는 예시:

```text
바탕화면\Dustiny\dustiny-quest
문서\더스티니\dustiny-quest
```

한글 경로가 무조건 문제를 만드는 것은 아니지만, Unity/빌드/배포 환경에서는 영어 경로가 더 안전합니다.

---

## 4. Unity 프로젝트 열기

Unity Hub를 열고 아래 메뉴를 선택합니다.

```text
Add > Add project from disk
```

아까 Clone한 폴더를 선택합니다.

예시:

```text
C:\dustiny\dustiny-quest
```

선택한 폴더 안에 아래 폴더들이 보이면 정상입니다.

```text
Assets
Packages
ProjectSettings
```

---

## 5. 팀 역할별 작업 범위

### XR 개발 담당

담당 범위:

```text
패스스루
공간 인식
Quest 빌드
XR Interaction
Unity 씬 구성
카메라/컨트롤러/핸드트래킹 입력
```

주로 건드리는 폴더:

```text
Assets/Dustiny/Scripts/XR/
Assets/Dustiny/Scenes/
Assets/Dustiny/Prefabs/XR/
Assets/Dustiny/Prefabs/ScanZone/
```

---

### AI 개발 담당

담당 범위:

```text
이미지 분석
객체 인식
청소 상태 판단
AI 모델 테스트
API 또는 분석 서버
```

주로 건드리는 폴더:

```text
AI/
AI/models/
AI/server/
AI/notebooks/
Assets/Dustiny/Scripts/AI/
```

주의:

```text
실제 방 사진
테스트용 개인 이미지
패스스루 캡처 이미지
실험 참여자 데이터
개인정보가 포함된 데이터
```

위 파일들은 GitHub에 올리지 않습니다.

---

### AI 활용 / Unity 연동 담당

담당 범위:

```text
AI 결과를 Unity로 가져오기
청소 우선순위 UI 표시
더리 안내 문구 출력
XR 화면 위에 분석 결과 표시
API 연결
JSON 파싱
```

주로 건드리는 폴더:

```text
Assets/Dustiny/Scripts/AIIntegration/
Assets/Dustiny/Scripts/UI/
Assets/Dustiny/Prefabs/UI/
Assets/Dustiny/Prefabs/Durry/
```

---

### 리서치 / 디자인 / 에셋 담당

담당 범위:

```text
캐릭터 이미지
UI 이미지
아이콘
3D 모델
텍스처
사운드
애니메이션 리소스
```

주로 건드리는 폴더:

```text
Assets/Dustiny/Art/
Assets/Dustiny/Audio/
Assets/Dustiny/Models/
Assets/Dustiny/Textures/
Assets/Dustiny/Prefabs/
```

디자인/에셋 담당자는 가능하면 코드나 씬 파일을 직접 수정하지 않고, 정해진 에셋 폴더에 파일을 정리해서 업로드합니다.

---

## 6. 폴더 구조

프로젝트 전체 구조는 아래처럼 관리합니다.

```text
dustiny-quest/
├─ Assets/
│  └─ Dustiny/
│     ├─ Art/
│     │  ├─ Character/
│     │  ├─ UI/
│     │  ├─ Icons/
│     │  └─ Textures/
│     │
│     ├─ Audio/
│     │  ├─ SFX/
│     │  └─ Voice/
│     │
│     ├─ Materials/
│     ├─ Models/
│     │  ├─ Durry/
│     │  └─ Props/
│     │
│     ├─ Prefabs/
│     │  ├─ Durry/
│     │  ├─ UI/
│     │  └─ ScanZone/
│     │
│     ├─ Scenes/
│     ├─ Scripts/
│     │  ├─ XR/
│     │  ├─ AI/
│     │  ├─ AIIntegration/
│     │  ├─ UI/
│     │  └─ Common/
│     │
│     └─ Textures/
│
├─ AI/
│  ├─ models/
│  ├─ server/
│  ├─ notebooks/
│  └─ datasets/
│
├─ Docs/
│  ├─ GitGuide.md
│  ├─ AssetGuide.md
│  └─ BuildGuide.md
│
├─ ProjectSettings/
├─ Packages/
├─ .gitignore
└─ README.md
```

---

## 7. 에셋은 어디에 넣나요?

에셋은 반드시 `Assets/Dustiny` 안에 넣습니다.

### Art / Models / Prefabs 차이

| 폴더 | 쉽게 말하면 | 넣는 파일 |
| --- | --- | --- |
| `Art` | 그림, 이미지, 텍스처 같은 2D 시각 자료 | `.png`, `.jpg`, `.psd` |
| `Models` | Blender/Maya 등에서 만든 3D 모델 원본 | `.fbx`, `.obj`, `.blend` |
| `Prefabs` | Unity 안에서 바로 배치할 수 있게 조립된 완성 오브젝트 | `.prefab` |
| `Audio` | 효과음, 음성, 알림음, 배경음 | `.wav`, `.mp3` |
| `Materials` | Unity 머티리얼 | `.mat` |

---

### 더리 2D 이미지

예시 파일:

```text
durry_idle.png
durry_clean.png
durry_dirty.png
```

넣는 위치:

```text
Assets/Dustiny/Art/Character/
```

---

### UI 이미지 / 아이콘

예시 파일:

```text
bosong_gauge_bg.png
scan_zone_icon.png
mission_complete_icon.png
```

넣는 위치:

```text
Assets/Dustiny/Art/UI/
Assets/Dustiny/Art/Icons/
```

---

### 더리 3D 모델 원본

예시 파일:

```text
durry_model.fbx
durry_model.blend
```

넣는 위치:

```text
Assets/Dustiny/Models/Durry/
```

---

### 청소 소품 3D 모델

예시 파일:

```text
broom.fbx
trash_bag.fbx
desk_prop_01.fbx
```

넣는 위치:

```text
Assets/Dustiny/Models/Props/
```

---

### Unity에서 완성된 더리 오브젝트

더리 모델에 Material, Animator, Collider, Script 등을 연결해서 Unity에서 바로 사용할 수 있는 형태로 만든 파일은 Prefab입니다.

예시 파일:

```text
Durry.prefab
Durry_Base.prefab
```

넣는 위치:

```text
Assets/Dustiny/Prefabs/Durry/
```

쉽게 말하면 아래와 같습니다.

```text
Models = 3D 모델 재료
Prefabs = Unity에서 바로 꺼내 쓸 수 있는 완성품
```

---

### 사운드 파일

예시 파일:

```text
mission_complete_sfx.wav
scan_start.wav
durry_voice_01.wav
```

넣는 위치:

```text
Assets/Dustiny/Audio/SFX/
Assets/Dustiny/Audio/Voice/
```

---

## 8. 파일 이름 규칙

파일명은 영어로 작성하고, 띄어쓰기 대신 `_`를 사용합니다.

좋은 예시:

```text
durry_idle.png
durry_clean_01.png
bosong_gauge_bg.png
mission_complete_sfx.wav
scan_zone_icon.png
durry_model.fbx
cleaning_panel.prefab
```

피해야 할 예시:

```text
더리 최종 진짜.png
청소구역 아이콘 수정본.png
새 폴더/최종/진짜최종.png
button final final.png
```

한글 파일명이 무조건 안 되는 것은 아니지만, Unity/Git 환경에서는 영어 파일명이 더 안전합니다.

---

## 9. Unity 에셋 추가 규칙

파일 탐색기에서 바로 넣어도 되지만, 가능하면 **Unity Project 창에서 원하는 폴더에 드래그해서 넣는 것**을 추천합니다.

이유는 Unity가 `.meta` 파일을 자동으로 생성하기 때문입니다.

예시:

```text
durry_idle.png
durry_idle.png.meta
```

`.meta` 파일도 반드시 같이 올라가야 합니다.

`.meta` 파일이 빠지면 다른 팀원 컴퓨터에서 이미지, 머티리얼, 프리팹 연결이 깨질 수 있습니다.

### 파일 이동 / 이름 변경도 Unity 안에서 하기

좋은 방법:

```text
Unity Project 창에서 이동 또는 이름 변경
```

위험한 방법:

```text
파일 탐색기에서 직접 이동 또는 이름 변경
```

Unity 밖에서 옮기면 `.meta` 연결이 꼬일 수 있습니다.

---

## 10. 작업 시작 전: Fetch / Pull 하기

작업을 시작하기 전에는 항상 최신 상태를 받아옵니다.

GitHub Desktop에서:

```text
Fetch origin
```

을 누릅니다.

만약 버튼이 아래처럼 바뀌면:

```text
Pull origin
```

한 번 더 눌러서 최신 파일을 받아옵니다.

작업 전 Pull을 하지 않으면 다른 사람이 만든 파일과 충돌날 수 있습니다.

---

## 11. 브랜치 만들기

`main`이나 `develop`에 바로 작업하지 않고, 본인 작업용 브랜치를 만듭니다.

GitHub Desktop 상단에서:

```text
Current Branch > New Branch
```

를 누릅니다.

### 브랜치 이름 규칙

브랜치 이름은 영어 소문자로 작성합니다.

#### 개발 기능 브랜치

```text
feature/작업내용
```

예시:

```text
feature/xr-passthrough
feature/ai-object-detection
feature/ai-result-panel
feature/durry-feedback
```

#### 에셋 브랜치

```text
asset/에셋내용
```

예시:

```text
asset/durry-character
asset/ui-icons
asset/sound-effects
asset/yunjin-durry
asset/sojung
asset/minjeong
```

#### 버그 수정 브랜치

```text
fix/문제내용
```

예시:

```text
fix/passthrough-build-error
fix/ui-position-bug
fix/ai-json-error
```

---

## 12. 에셋 넣기

Unity에서 원하는 폴더에 에셋을 넣습니다.

예시:

```text
Assets/Dustiny/Art/Character/durry_idle.png
Assets/Dustiny/Art/Character/durry_clean.png
Assets/Dustiny/Art/UI/bosong_gauge.png
```

Unity에서 에셋을 넣은 뒤 잠깐 기다리면 Unity가 자동으로 `.meta` 파일을 생성합니다.

---

## 13. GitHub Desktop에서 변경사항 확인하기

GitHub Desktop으로 돌아오면 왼쪽 `Changes` 탭에 변경된 파일들이 보입니다.

정상적으로 보이는 예시:

```text
Assets/Dustiny/Art/Character/durry_idle.png
Assets/Dustiny/Art/Character/durry_idle.png.meta
Assets/Dustiny/Art/Character/durry_clean.png
Assets/Dustiny/Art/Character/durry_clean.png.meta
```

에셋 파일과 `.meta` 파일이 같이 보이는지 확인합니다.

---

## 14. 커밋하기

반드시 본인 브랜치에서 작업했는지 확인한 뒤 커밋합니다.

GitHub Desktop 왼쪽 아래 `Summary` 칸에 커밋 메시지를 씁니다.

### 커밋 메시지 규칙

커밋 메시지는 아래 형식을 사용합니다.

```text
[파트] 작업 내용
```

파트 이름:

```text
[XR]
[AI]
[AI-USE]
[ASSET]
[UI]
[FIX]
[DOCS]
```

예시:

```text
[ASSET] Add Durry character assets
[ASSET] Add UI icon assets
[ASSET] Add mission complete sound
[XR] Add passthrough test scene
[AI] Add object detection API test
[AI-USE] Connect AI result to UI panel
[FIX] Fix Quest build error
[DOCS] Update asset upload guide
```

한국어로 작성해도 괜찮지만, 앞의 태그는 통일합니다.

```text
[ASSET] 더리 기본 표정 이미지 추가
[XR] 패스스루 테스트 씬 추가
[AI-USE] 청소 우선순위 UI 연결
```

커밋 메시지를 작성한 뒤 아래 버튼을 누릅니다.

```text
Commit to 브랜치명
```

---

## 15. Push 하기

커밋을 했으면 GitHub Desktop 상단의 아래 버튼을 누릅니다.

```text
Push origin
```

이걸 눌러야 내 컴퓨터에 있는 변경사항이 팀 GitHub 레포에 올라갑니다.

---

## 16. Pull Request 만들기

Push 후 GitHub Desktop에 아래 버튼이 보이면 클릭합니다.

```text
Create Pull Request
```

브라우저에서 GitHub Pull Request 페이지가 열립니다.

### PR 제목 형식

```text
[파트] 작업 요약
```

예시:

```text
[ASSET] 더리 캐릭터 에셋 추가
[UI] 보송 게이지 이미지 추가
[XR] 패스스루 기본 씬 추가
```

### PR 내용에 적을 것

```md
## 작업 내용
- 무엇을 추가/수정했는지 작성

## 확인 방법
- Unity에서 어떤 씬 또는 파일을 확인하면 되는지
- 실행했을 때 무엇이 보여야 하는지

## 관련 파일
- 주요 파일 경로 작성

## 주의사항
- 아직 안 되는 부분
- 다른 팀원이 확인해야 할 부분
```

예시:

```md
## 작업 내용
- 더리 기본 상태 이미지를 추가했습니다.
- 더리 보송 상태 이미지를 추가했습니다.
- 캐릭터 테스트용 png 파일을 추가했습니다.

## 확인 방법
- Unity에서 `Assets/Dustiny/Art/Character/` 폴더 확인

## 관련 파일
- `Assets/Dustiny/Art/Character/durry_idle.png`
- `Assets/Dustiny/Art/Character/durry_clean.png`

## 주의사항
- 아직 애니메이션은 적용되지 않았습니다.
```

---

## 17. 작업 완료 후 팀 채팅에 공유하기

Pull Request를 만든 뒤 팀 채팅에 아래처럼 공유합니다.

예시:

```text
더리 캐릭터 에셋 업로드했습니다.
브랜치: asset/yunjin-durry
PR: 링크
추가 위치: Assets/Dustiny/Art/Character/
```

---

## 18. 다른 사람 작업 가져오기

다른 사람이 올린 파일을 내 Unity 프로젝트에 반영하려면 GitHub Desktop에서 아래 순서로 진행합니다.

```text
1. Fetch origin
2. Pull origin
3. Unity로 돌아가기
4. 새 에셋이 들어왔는지 확인
```

Unity가 바로 갱신되지 않으면 Unity를 한 번 껐다 켜도 됩니다.

---

## 19. 충돌을 줄이기 위한 규칙

### MainMRScene은 담당자만 수정하기

아래 파일은 충돌이 잘 나므로 담당자만 수정합니다.

```text
Assets/Dustiny/Scenes/MainMRScene.unity
```

에셋 업로드 담당자는 씬을 저장하지 않는 것을 추천합니다.

---

### 에셋 업로드 목적이라면 에셋만 변경하기

에셋 업로드 목적이라면 가능하면 아래 파일만 변경되도록 합니다.

```text
Assets/Dustiny/Art/...
Assets/Dustiny/Audio/...
Assets/Dustiny/Models/...
Assets/Dustiny/Prefabs/...
```

---

### Unity에서 Save Scene 누르지 않기

에셋만 넣는 작업이라면 씬을 수정할 필요가 없습니다.

실수로 씬이 변경되면 GitHub Desktop Changes에 `.unity` 파일이 뜰 수 있습니다.

주의해야 할 예시:

```text
Assets/Dustiny/Scenes/MainMRScene.unity
```

이 파일이 뜨면 커밋 전에 담당자에게 확인합니다.

---

### 같은 Scene을 여러 명이 동시에 수정하지 않기

Unity 씬 파일은 충돌이 나면 해결하기 어렵습니다.

조심할 파일 예시:

```text
MainMRScene.unity
MainScene.unity
DemoScene.unity
XRScene.unity
```

규칙:

```text
한 씬은 한 명만 수정한다.
여러 명이 테스트할 때는 개인 테스트 씬을 사용한다.
```

추천 구조:

```text
Assets/Dustiny/Scenes/
├─ MainMRScene.unity
└─ Sandbox/
   ├─ XR_Yurim_Test.unity
   ├─ AI_Minjeong_Test.unity
   └─ UI_Test.unity
```

---

### Prefab 단위로 작업하기

씬에 직접 이것저것 넣기보다, 기능별로 Prefab을 만들어서 작업합니다.

예시:

```text
Durry.prefab
ScanZone.prefab
CleaningResultPanel.prefab
TrashHighlight.prefab
ObjectMarker.prefab
```

이렇게 하면 씬 충돌이 줄어듭니다.

---

## 20. GitHub Desktop에 이런 파일이 뜨면 확인하기

### 정상적으로 올려도 되는 파일

```text
.png
.jpg
.psd
.fbx
.obj
.blend
.wav
.mp3
.mp4
.onnx
.sentis
.prefab
.mat
.meta
.cs
```

### 확인이 필요한 파일

```text
MainMRScene.unity
ProjectSettings/
Packages/
Packages/manifest.json
InputActions 파일
공통 Manager 스크립트
공통 Prefab
```

이 파일들은 프로젝트 전체에 영향을 줄 수 있으므로 커밋 전에 팀에 공유합니다.

### 올리면 안 되는 파일

```text
Library/
Temp/
Obj/
Build/
Builds/
Logs/
.vs/
.idea/
UserSettings/
```

보통 `.gitignore`가 막아주지만, GitHub Desktop에 이런 파일이 많이 뜨면 커밋하지 말고 담당자에게 문의합니다.

---

## 21. 개인정보 / AI 데이터 관리 규칙

방청소 AI 프로젝트 특성상 실제 이미지 데이터 관리에 주의해야 합니다.

GitHub에 올리면 안 되는 파일:

```text
실제 방 사진
패스스루 캡처 이미지
개인정보가 포함된 이미지/영상
실험 참여자 데이터
개인 API Key
AI 서버 비밀번호
.env 파일
```

AI 개발에 필요한 테스트 이미지는 GitHub에 직접 올리지 않고, 팀 공유 드라이브나 개인 로컬 폴더에서 관리합니다.

데이터 설명이 필요하다면 아래 파일에 설명만 작성합니다.

```text
AI/datasets/README.md
```

예시:

```md
# Dataset Guide

실제 테스트 이미지는 개인정보 보호를 위해 GitHub에 업로드하지 않습니다.

사용 데이터:
- 정리된 방 이미지
- 어질러진 방 이미지
- 바닥 장애물 이미지
- 책상 위 물건 이미지

보관 위치:
- 팀 공유 드라이브
- 개인 로컬 폴더
```

---

## 22. 큰 파일과 Git LFS

우리 레포는 Unity 에셋 관리를 위해 Git LFS를 사용합니다.

Git LFS는 큰 파일을 GitHub에 올릴 때 사용하는 방식입니다.

예시:

```text
.png
.jpg
.fbx
.wav
.mp4
.onnx
.sentis
.unitypackage
```

디자인/에셋 담당자는 따로 복잡하게 신경 쓰지 않아도 되지만, 아래 문제가 생기면 담당자에게 알려주세요.

```text
큰 파일이 제대로 받아지지 않음
이미지가 깨져 보임
모델 파일이 열리지 않음
LFS 관련 에러가 뜸
```

---

## 23. 리뷰 규칙

PR은 최소 1명 이상 확인 후 merge합니다.

### 개발 PR

```text
개발자 1명 이상 확인
Unity 실행 가능 여부 확인
Console 에러 확인
Quest 빌드 영향 확인
```

### 에셋 PR

```text
에셋 폴더 위치 확인
파일명 규칙 확인
Unity에서 깨지지 않는지 확인
.meta 파일 포함 여부 확인
```

### main으로 merge할 때

`develop`에서 충분히 테스트한 뒤, 팀장이 `main`으로 합칩니다.

---

## 24. 하루 작업 루틴

팀원들은 작업할 때 아래 순서를 따릅니다.

```text
1. GitHub Desktop 열기
2. 본인 브랜치 선택
3. Fetch origin
4. Pull origin
5. Unity 작업
6. Unity에서 에러 없는지 확인
7. GitHub Desktop에서 변경 파일 확인
8. Commit
9. Push origin
10. Pull Request 생성
11. 팀 채팅에 공유
```

---

## 25. 발표 전 안정화 규칙

### 발표 2일 전

```text
기능 추가 마감
```

### 발표 1일 전

```text
버그 수정만 허용
에셋 교체는 최소화
main 빌드 테스트
Quest 실행 테스트
```

### 발표 당일

```text
main 수정 금지
발표용 빌드만 사용
```

---

## 26. 전체 순서 요약: 에셋 업로드

```text
1. GitHub Desktop 설치
2. GitHub 로그인
3. dustiny-quest 레포 Clone
4. Unity Hub에서 프로젝트 열기
5. Fetch / Pull로 최신 상태 받기
6. 새 브랜치 만들기
7. Assets/Dustiny 폴더 안에 에셋 넣기
8. Unity가 .meta 파일을 생성했는지 확인
9. GitHub Desktop에서 변경사항 확인
10. Summary 작성
11. Commit
12. Push origin
13. Pull Request 만들기
14. 팀 채팅에 공유
```

---

## 27. 문제가 생겼을 때

### Clone이 안 될 때

확인할 것:

```text
GitHub 로그인 여부
xreal-dustiny/dustiny-quest 접근 권한
레포 주소 오타 여부
```

레포 주소:

```text
https://github.com/xreal-dustiny/dustiny-quest.git
```

---

### Unity에서 에셋이 안 보일 때

확인할 것:

```text
올바른 폴더를 열었는지 확인
Assets, Packages, ProjectSettings가 있는 폴더인지 확인
Unity를 한 번 껐다 켜기
```

---

### GitHub Desktop에 너무 많은 파일이 뜰 때

확인할 것:

```text
Library/
Temp/
Logs/
UserSettings/
Build/
```

이런 폴더가 뜬다면 커밋하지 말고 담당자에게 문의합니다.

---

### Pull 받을 때 충돌이 뜰 때

혼자 해결하지 말고 담당자에게 캡처해서 공유합니다.

특히 아래 파일 충돌은 임의로 해결하지 않습니다.

```text
.unity
.prefab
.meta
ProjectSettings/
Packages/manifest.json
```

---

## 28. 최종 주의사항

에셋 업로드 담당자는 기본적으로 아래 작업만 하면 됩니다.

```text
Assets/Dustiny 폴더 안에 에셋 추가
.meta 파일과 함께 커밋
본인 브랜치에 Push
Pull Request 생성
팀 채팅에 공유
```

아래 파일은 프로젝트 전체에 영향을 줄 수 있으므로 수정 전 반드시 팀에 공유합니다.

```text
MainMRScene
ProjectSettings
Packages
공통 Prefab
공통 Manager Script
```
