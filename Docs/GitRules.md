# Git 작업 규칙

브랜치, 커밋, PR을 같은 형식으로 맞추면 팀 작업이 훨씬 수월합니다.

- **브랜치** = 작업 위치
- **커밋** = 작업 기록
- **PR** = develop에 합치기 전 설명서

---

## Branch Naming Rule

브랜치는 아래 형식으로 만듭니다.

```
작업종류/파트-작업내용
```

### 작업 종류

| 종류      | 설명                |
| --------- | ------------------- |
| `feature` | 새로운 기능 개발    |
| `asset`   | 에셋 추가 또는 수정 |
| `fix`     | 버그 수정           |
| `docs`    | 문서 수정           |
| `test`    | 테스트 작업         |

### 브랜치 이름 예시

#### XR

- `feature/xr-passthrough`
- `feature/xr-scan-zone`
- `feature/xr-quest-build`

#### AI

- `feature/ai-object-detection`
- `feature/ai-room-analysis`
- `feature/ai-api-server`

#### AI 활용 / Unity 연동

- `feature/ai-integration`
- `feature/ai-result-ui`
- `feature/ai-json-parser`

#### 에셋

- `asset/durry-character`
- `asset/ui-icons`
- `asset/cleaning-props`
- `asset/durry-voice`

#### 버그 수정

- `fix/quest-build-error`
- `fix/ui-position-bug`
- `fix/ai-json-error`

#### 문서

- `docs/github-guide`
- `docs/asset-upload-guide`

### 브랜치 이름 규칙

- 영어 소문자 사용
- 띄어쓰기 금지 (대신 `-` 사용)
- 너무 길게 쓰지 않기
- `main`, `develop`에서 직접 작업하지 않기

---

## Commit Message Rule

커밋 메시지 형식:

```
[파트] 작업 내용
```

### 파트 태그

| 태그       | 용도                                  |
| ---------- | ------------------------------------- |
| `[XR]`     | XR / 패스스루 / Quest / 공간 인식     |
| `[AI]`     | AI 모델 / 분석 코드 / 서버            |
| `[AI-USE]` | AI 결과 활용 / Unity 연동 / JSON 파싱 |
| `[UI]`     | 화면 UI / 버튼 / 패널                 |
| `[ASSET]`  | 이미지 / 모델 / 사운드 / 프리팹 에셋  |
| `[FIX]`    | 버그 수정                             |
| `[DOCS]`   | 문서 수정                             |

### 예시

```
[XR] 패스스루 테스트 씬 추가
[ASSET] 더리 캐릭터 이미지 추가
[AI-USE] AI 결과 UI 연결
```

자세한 내용은 [.github/commit_template.md](../.github/commit_template.md)를 참고하세요.

---

## 7. Pull Request 규칙

작업을 `develop`에 합치기 전에는 **Pull Request**를 만듭니다.

### PR 제목 형식

```
[파트] 작업 요약
```

예시:

```
[XR] 패스스루 기본 씬 추가
[AI] 방 이미지 분석 API 추가
[ASSET] 더리 캐릭터 에셋 추가
```

### PR 대상 브랜치

```
내 작업 브랜치 → develop
```

예시:

- `feature/xr-passthrough` → `develop`
- `asset/durry-character` → `develop`
- `fix/quest-build-error` → `develop`

### PR 내용에 꼭 적을 것

```
## 작업 내용
- 무엇을 추가/수정했는지 작성

## 확인 방법
- Unity에서 어떤 씬을 열면 되는지
- 실행했을 때 무엇이 보여야 하는지

## 관련 파일
- 주요 파일 경로 작성

## 주의사항
- 아직 안 되는 부분
- 다른 팀원이 확인해야 할 부분
```

예시:

```
## 작업 내용
- 더리 캐릭터 기본 모델을 추가했습니다.
- Prefabs/Durry 폴더에 기본 프리팹을 만들었습니다.

## 확인 방법
- Unity에서 Assets/Dustiny/Prefabs/Durry/Durry_Base.prefab 확인

## 관련 파일
- Assets/Dustiny/Models/Durry/
- Assets/Dustiny/Prefabs/Durry/

## 주의사항
- 아직 애니메이션은 적용되지 않았습니다.
```

PR 본문 양식은 GitHub에서 자동으로 채워집니다. ([.github/pull_request_template.md](../.github/pull_request_template.md))

---

## 작업 시작 전 체크

### 1. 내 작업 종류 확인

- XR 개발
- AI 개발
- AI 활용 / Unity 연동
- UI
- 에셋
- 문서
- 버그 수정

### 2. 브랜치 만들기

위 **Branch Naming Rule**에 맞게 브랜치를 생성합니다.

### 3. 작업 전 Pull (GitHub Desktop)

1. Current Branch가 내 브랜치인지 확인
2. **Fetch origin** 클릭
3. **Pull origin** 클릭
4. Unity 열고 작업 시작

### 4. 커밋 메시지 작성

형식: `[파트] 작업 내용`

---

## 로컬 Git에서 커밋 양식 쓰기 (선택)

커밋할 때 `.github/commit_template.md` 내용이 자동으로 뜨게 하려면, 한 번만 설정합니다.

```bash
git config commit.template .github/commit_template.md
```

GitHub Desktop은 Commit Summary에 한 줄 형식(`[파트] 작업 내용`)만 적으면 됩니다.
