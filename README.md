# STILL

> 지구 표면의 71%는 바다지만, 그 바다의 80% 이상은 아직 아무도 본 적이 없습니다.
> STILL 은 생물을 잡아 모으는 대신, 아직 모르는 바다를 한 칸씩 기록하는 VR 관찰 체험입니다.

![Unity](https://img.shields.io/badge/Unity-6000.3.16f1-222?logo=unity)
![Meta Quest](https://img.shields.io/badge/Meta%20Quest-OpenXR-0467DF?logo=meta)
![URP](https://img.shields.io/badge/Render-URP-555)

해변의 해양 관측소에서 출발해 엘리베이터를 타고 수심 **24m → 200m → 1,000m** 로 내려갑니다. 오른손의 관찰기로 헤엄치는 생물을 겨눠 관찰 일지에 기록하고, 가장 깊은 곳에서는 불을 끄고 가만히 기다려야만 다가오는 "신호의 주인"을 만납니다. 밤이 된 관측소로 돌아오면 그날의 이야기를 골라 이어 갈 수 있습니다.

- **대상** 10\~15세 · 한 사람 15\~20분
- **생물** 관찰 일지에 약 110종 (한국어 이름 · 학명 · 설명 · 음성 해설)
- **언어** 해설 · NPC 대사 모두 한국어 음성과 자막

<br>

## 체험 흐름

| 장소 | 하는 일 |
|---|---|
| **해변** | 놀란과 조수 웅덩이에서 관찰기 쓰는 법을 익히고 첫 기록. 바닷가 쓰레기 줍기 |
| **관측소 로비** | 수심 벽 만져 보기, 테라스 쌍안경으로 혹등고래 찾기, 보리 선장의 "푸른빛" 이야기 |
| **24m 산호초** | 아무 생물이나 4종 기록. 몇 분마다 바뀌는 물고기 무리, 유령 그물에 걸린 바다거북 |
| **200m** | 큰 생물 2종 기록. 어둠을 가로지르는 대형 생물, 멀리서 깜빡이는 푸른 신호, 드물게 산갈치 |
| **1,000m** | 조명을 끄고 가만히 기다리면 신호의 주인이 다가온다. 이름 붙이기, 찍을까 바라볼까, 고래 낙하 |
| **밤의 관측소** | 데이터 월에 새겨지는 기록, 크레딧, 그리고 "오늘 밤의 이야기" |

### 오늘 밤의 이야기

크레딧이 끝나면 밤 로비에서 다섯 장면 중 하나를 골라 들어가고, 끝나면 로비로 돌아옵니다.

| 장면 | 내용 |
|---|---|
| 등대의 신호 | 등대 꼭대기에서 신호의 주인과 같은 수로 빛을 깜빡여 인사한다 |
| 바다거북의 밤 | 불을 끄면 새끼 바다거북 열두 마리가 바다로 기어간다 |
| 선장의 밤바다 | 연구선 갑판에서 불을 끄고 기다리면 신호의 주인이 다시 떠오른다 |
| 비브의 바시스피어 | 1934년, 지름 1.45m 쇠공을 타고 923m 까지 내려간다 |
| 다음 신호 2,500m | 로봇 카메라로 해저 열수구를 기록하면, 더 깊은 곳에서 새 신호가 잡힌다 |

<br>

## 조작 (Meta Quest)

| 입력 | 동작 |
|---|---|
| 왼쪽 막대 | 걷기 (순간이동 없음) |
| 오른쪽 막대 좌우 | 부드럽게 돌기 |
| 오른손으로 생물 겨누기 | 링이 다 찰 때까지 지켜보면 기록 |
| A / X 버튼 | 관찰기 조명 켜기 · 끄기 |
| B 버튼 | 1,000m 에서 플래시 사진 찍기 |
| 왼손목 메뉴 | 관찰 일지 열기 |
| 손가락으로 누르기 · 레이 | 버튼 누르기, NPC 에게 말 걸기 |

<br>

## 시작하기

### 준비물

- Unity **6000.3.16f1** (Unity 6.3 LTS) + Android Build Support
- Meta Quest 헤드셋 (기기에서 실행할 때)
- 비공개 저장소 [`BUY_Asset`](https://github.com/jeonghun267/BUY_Asset) 접근 권한

### 1. 저장소 받기

```bash
git clone https://github.com/jeonghun267/STILL.git
```

### 2. 구매한 에셋 넣기

24m 산호초 · 200m 물고기 모델(Layer Lab "3D Characters - Fish")은 Unity Asset Store 구매 에셋이라 재배포할 수 없어 이 저장소에 넣지 않습니다. 비공개 저장소에서 받아 `Assets/` 아래에 그대로 복사하세요.

```bash
git clone https://github.com/jeonghun267/BUY_Asset.git
cp -r "BUY_Asset/Layer Lab" "BUY_Asset/Layer Lab.meta" "BUY_Asset/3D Characters - Fish" "BUY_Asset/3D Characters - Fish.meta" STILL/Assets/
```

- `.meta` 파일까지 같이 복사해야 생물 데이터와 씬이 같은 GUID 로 모델을 찾습니다.
- 이 폴더들은 `.gitignore` 에 있어 커밋에 섞이지 않습니다.
- 없으면 수중 돔에 물고기가 나오지 않습니다.

### 3. Gemini API 키 (선택)

에디터에서 Gemini 로 생물 설명을 받거나 목소리를 새로 녹음할 때만 필요합니다. 프로젝트 루트에 `.env` 를 만드세요.

```
GEMINI_API_KEY=여기에_키
```

키가 없어도 체험은 그대로 돌아갑니다. 설명은 미리 써 둔 원고를, 목소리는 미리 녹음해 둔 [`VoiceBank`](#목소리-미리-녹음하기) 를 씁니다.

### 4. 열고 실행하기

1. Unity Hub 에서 프로젝트 폴더를 열고 `Assets/Scenes/STILL.unity` 를 엽니다. 체험 전체가 이 씬 하나에 들어 있습니다.
2. **에디터에서 확인** — Play 를 누릅니다. 헤드셋이 없으면 `DomeAquarium/디버그/` 메뉴로 각 장면을 바로 불러 볼 수 있습니다.
3. **Quest 에서 실행** — `File > Build Profiles` 에서 Android 를 고르고, Quest 를 연결한 뒤 Build And Run 합니다.

<br>

## 프로젝트 구조

```
Assets/
├─ Scenes/STILL.unity        체험 전체가 들어 있는 씬
├─ DomeAquarium/
│  ├─ Scripts/               런타임 코드 (흐름 · 관찰 · 층 · NPC · 밤 이야기)
│  ├─ Editor/                씬을 세우는 빌더와 DomeAquarium 메뉴
│  ├─ FishData/              생물 데이터 (이름 · 학명 · 서식 층 · 설명)
│  ├─ Missions/              층별 할 일 (DiveMission)
│  ├─ Shaders/               심해 생물 · 발광 · 빛줄기 · 맨 위에 그리는 UI
│  ├─ Audio/                 배경음 · 효과음 · 미리 녹음한 목소리
│  └─ Coast/ Creatures/ Materials/ Prefabs/ UI/ Fonts/
└─ ThirdParty/               외부 무료 에셋 (출처는 CREDITS.md)
Docs/                        개발 기록 · 확장 로드맵
Tools/bake_voice.py          목소리 미리 녹음 스크립트
```

### 주요 스크립트

| 영역 | 스크립트 |
|---|---|
| 전체 흐름 | `LobbySequence` 나레이션 · 장소 이동 · 자막 / `LobbyTutorial` 해변 · 로비 걷기 / `DiveDirector` · `DiveMission` 층별 하강 |
| 관찰 | `ObservationAim` 겨눠서 기록 / `ObservationLight` 관찰기 조명 / `FishCollectionStore` 탐험가별 관찰 일지 / `FishEncyclopediaPanel` 일지 화면 |
| 바닷속 | `BoidManager` 물고기 무리 · 교체 / `LargeCreaturePasses` 200m 큰 생물 / `UnknownCreature` · `StillnessDetector` 신호의 주인 |
| 사람과 목소리 | `NpcDialogue` · `LobbyNpc` 보리 선장 · 놀란 / `FishNarrationService` · `VoiceBank` 음성 해설 |
| 엔딩 | `LobbyNight` · `CreditsRoll` 밤 로비와 크레딧 / `NightStoryHub` 이야기 판과 다섯 장면 |

<br>

## 에디터 메뉴로 씬 다시 세우기

씬의 대부분은 손으로 배치하지 않고 `DomeAquarium` 메뉴의 빌더가 코드로 세웁니다. 다 세운 씬이 커밋되어 있으니 평소에는 씬을 열기만 하면 됩니다.

| 메뉴 | 하는 일 |
|---|---|
| `DomeAquarium/9. 육지 로비 빌드` | 해변 · 관측소 · 해안 · NPC · 밤 이야기까지 차례로 다시 세움 |
| `DomeAquarium/18. 층별 하강 적용` | 24m · 200m · 1,000m 층 구성 |
| `DomeAquarium/26. Quest 성능 설정 (FPS)` | 포비에이티드 렌더링 · HDR 끄기 등 Quest 설정 |
| `DomeAquarium/디버그/…` | 헤드셋 없이 플레이 중에 장면 · 생물 · 대화 바로 부르기 |

<details>
<summary>번호 메뉴 전체 보기</summary>

| 번호 | 메뉴 |
|---|---|
| 1 \~ 8 | 에셋 준비 (URP 변환 · 한글 폰트 · FishData · 씬 빌드 · 애니메이터 · Gemini 설정) |
| 9 | 육지 로비 빌드 · UI 키트 스프라이트 |
| 10 \~ 16 | 크기 보정 · 파티클 · 코스틱 · 정보 패널 · 관찰 일지 · 버튼 스타일 · 수중 BGM |
| 17 | 관찰기 적용 |
| 18 | 층별 하강 적용 (24m → 200m → 1,000m) |
| 19 | 로비 바깥 해안 (지형 · 하늘 · 소품) |
| 20 | 테라스 · 관망 쌍안경 |
| 21 | 심해 생물 · 고래 |
| 22 | 로비 NPC (보리 선장 · 놀란) |
| 23 | 자막 · 떠 있는 판을 맨 위에 그리기 |
| 24 | 해변 시작 구간 (모래사장 · 계단 · 조수 웅덩이) |
| 25 | 관측소 벽화 다시 그리기 |
| 26 | Quest 성능 설정 |
| 27 | 엘리베이터 창 · 수압 컵 |
| 28 | 밤 해변 (푸르게 빛나는 파도) |
| 29 | 유령 그물 (쓰레기 줍기 · 그물에 걸린 거북) |
| 30 | 고래 소리 · 꼬리 사진 식별 |
| 31 | 밤 로비 이야기 판 · 이야기 장면 |
| 32 | 층 볼거리 (산호 백화 · 밤의 대이동) |
| 33 | 조수 웅덩이에 손 넣기 |
| 34 | 깊은 바다의 만남 (찍을까 바라볼까 · 고래 낙하 · 산갈치 · 밤의 대왕고래) |

</details>

<br>

## 목소리 미리 녹음하기

해설과 NPC 목소리는 Gemini 음성으로 미리 녹음해 앱에 넣습니다(`VoiceBank`). 기기에서는 Gemini 를 부르지 않습니다.

1. `DomeAquarium/음성/1. 대사 목록 만들기` — 장면과 에셋의 대사를 `Assets/DomeAquarium/Audio/Voice/lines.json` 으로 모읍니다.
2. `python Tools/bake_voice.py` — 아직 녹음이 없는 대사만 녹음해 같은 폴더에 `{열쇠}.wav` 로 저장합니다. 하루 한도에 걸리면 멈추니, 다음 날 다시 실행하면 이어집니다.
3. `DomeAquarium/음성/2. 녹음 등록 (VoiceBank)` — wav 를 모노 · Vorbis 로 가져와 `Resources/VoiceBank` 에 묶습니다.

<br>

## 개발 흐름

- `jeonghun` 브랜치에서 작업하고, 에디터 실행과 Quest 빌드로 에러가 없는지 확인한 뒤 `main` 에 머지합니다.
- `main` 은 항상 빌드할 수 있는 최신 상태이고, Quest 빌드는 `main` 에서 합니다.
- 바뀐 내용은 [개발 기록](Docs/STILL_개발_기록.md) 에 날짜별로 적습니다.

## 보안

- Gemini API 키는 프로젝트 루트 `.env` 에만 두고 커밋하지 않습니다. `GeminiSettings.asset` 의 키 칸은 비워 두고, 에디터가 `.env` 를 직접 읽습니다.
- 앱(APK)에는 키가 들어가지 않습니다. 에셋에 키가 남아 있으면 빌드 전 검사(`DomeAquariumGeminiKeyGuard`)가 빌드를 멈춥니다.

## 에셋 출처

- 외부 무료 에셋의 출처와 라이선스는 [`Assets/ThirdParty/CREDITS.md`](Assets/ThirdParty/CREDITS.md) 에 있습니다. CC-BY 모델은 엔딩 크레딧에도 표기합니다.
- 구매 에셋은 저장소에 없습니다. [구매한 에셋 넣기](#2-구매한-에셋-넣기) 를 보세요.

## 문서

| 문서 | 내용 |
|---|---|
| [개발 기록](Docs/STILL_개발_기록.md) | 기능이 쌓인 순서(커밋별)와 날짜별 변경 기록 |
| [확장 로드맵](Docs/STILL_확장_로드맵.md) | 이야기의 다섯 가닥과 밤 장면 계획 |
