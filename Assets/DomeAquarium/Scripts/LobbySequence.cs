using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 1막 육지 → 2막 하강을 진행한다. 체험은 땅에서 시작한다.
///
///  1. 해 질 녘 아쿠아리움 로비. 창밖으로 바다가 보이고, 가운데에 1930년 잠수구(바시스피어)가 서 있다.
///     나레이션 "지구 표면의 71%는 바다… 80% 이상은 아직 아무도 본 적이 없습니다"가 자막과 함께 나온다.
///  2. 나레이션이 끝나면 "입장하기" 패널이 뜬다. 누르면 엘리베이터 문이 열리고, 개찰구를 지나 안으로 걸어 들어간다.
///  3. 칸 안 깊이 버튼(ElevatorConsole)을 누르면 문이 닫히고 계기판 수심이 내려간다. 도착하면 수중 돔으로 바뀐다.
///  4. 층의 할 일을 마치면 돔에서 "엘리베이터 타기"로 칸에 돌아와 다음 깊이 버튼을 누른다 (DiveDirector 가 진행을 잡는다).
///     칸만 따로 켜고 끈다(cabinSet) — 로비 건물은 엔딩 전까지 다시 켜지 않는다.
///  5. 엔딩: 1,000m 를 마치면 엘리베이터가 해수면까지 올라가고, 밤이 된 로비에서 데이터 월을 보며 선다 (Ascend, LobbyNight).
///
/// 멀미 설계 (이 프로젝트는 이동 기능을 전부 걷어냈다):
///  - 걷기는 일직선·등속이고 시작과 끝만 0.4초씩 부드럽게 가감속한다. 회전은 하지 않는다.
///  - 걷기 전에 화면을 0.3초 가리고 몸 방향을 엘리베이터 쪽으로 맞춘다 — 옆을 보며 앞으로 미끄러지는 게 가장 멀미가 심하다.
///  - 걷는 동안 시야 가장자리를 어둡게 한다(ScreenFader 비네트).
///  - 하강은 닫힌 엘리베이터 안에서만 일어난다. 내 몸과 칸이 같이 움직이니 바깥 흐름(벡션)이 없다.
///  - 장소가 바뀌는 순간은 전부 검은 화면 뒤에서 일어난다.
/// </summary>
[DefaultExecutionOrder(-400)]
public sealed class LobbySequence : MonoBehaviour
{
    enum Phase { Lobby, Entering, Cabin, Riding, Dome, Night }

    [Header("리그 (비면 XR Origin / Main Camera 를 찾는다)")]
    public Transform rig;
    public Camera viewCamera;

    [Header("로비 배치")]
    public GameObject lobbySet;            // 건물·소품 묶음. 수중 돔에 도착하면 통째로 꺼서 비용을 없앤다
    public Transform spawnPoint;           // 처음 서는 자리(바닥 높이). +Z 가 엘리베이터 쪽
    public Transform walkStartPoint;       // "입장하기" 후 화면이 가려진 사이 옮겨 서는 자리 — 자동 걷기를 짧게 유지한다
    public Transform cabinPoint;           // 엘리베이터 안 바닥 중심
    public Transform gatePoint;            // 개찰구 통로 중심 — 지나갈 때 문이 열린다
    public Transform domePoint;            // 수중 돔에서 설 자리. 비면 월드 원점(수조 정중앙)
    public GameObject cabinSet;            // 엘리베이터 칸·승강로·계기판·깊이 버튼. 로비 건물(lobbySet)과 따로 켜고 끈다

    [Header("층 진행 (비면 런타임에 찾는다. 없으면 예전처럼 24m 로 바로 내려간다)")]
    public DiveDirector director;

    [Header("엔딩 — 밤이 된 로비")]
    public LobbyNight night;
    public Transform nightSpot;            // 올라와서 서는 자리 — 데이터 월(앞벽 오른쪽)이 정면 조금 오른쪽에 온다
    [Range(0f, 1f)] public float nightAmbienceLevel = 0.55f;   // 밤 파도 소리는 낮보다 작게

    [Header("문 애니메이션 (glTF 클립)")]
    public Animator elevatorAnimator;
    public AnimationClip elevatorClip;
    public float doorOpenEnd = 1.67f;      // 클립의 0~1.67초가 문 열림
    public float doorCloseStart = 8.62f;   // 8.62초~끝이 문 닫힘
    public Animator gateAnimator;
    public AnimationClip gateClip;
    public float gatePlaySeconds = 1.4f;   // 원본 0.83초는 너무 급하다

    [Header("엘리베이터 계기판")]
    public TMP_Text depthText;
    public float arrivalDepth = 24f;       // 산호초가 사는 햇빛층. 깊은 바다처럼 과장하지 않는다
    public float descentSeconds = 9f;

    [Header("자막")]
    public CanvasGroup captionGroup;
    public TMP_Text captionText;
    public float captionDistance = 1.5f;
    public float captionDrop = 0.34f;
    [Tooltip("엘리베이터 칸 안쪽 반폭(m). 칸이 3m 라 1.5m 앞 자막은 벽 속에 묻힌다 — 칸 안에서는 바라보는 쪽 벽 앞에 세운다")]
    public float cabinHalfSize = 1.4f;
    public float cabinCaptionMargin = 0.25f;   // 벽(과 벽의 수심 계기판)에서 띄우는 거리
    public float cabinCaptionMin = 0.6f;       // 벽에 바짝 붙어 서도 이보다 가까이 세우지 않는다

    [Header("시작 패널")]
    public StartPanel startPanel;

    [Header("준비 단계 (있으면 자동 걷기 대신 직접 다닌다)")]
    public LobbyTutorial tutorial;

    [Header("로비 환경")]
    public Material lobbySkybox;
    public Color ambientSky = new Color(0.62f, 0.66f, 0.72f);
    public Color ambientEquator = new Color(0.46f, 0.44f, 0.42f);
    public Color ambientGround = new Color(0.20f, 0.19f, 0.18f);
    public Color sunColor = new Color(1.00f, 0.86f, 0.70f);
    public float sunIntensity = 1.15f;
    public Vector3 sunEuler = new Vector3(32f, -35f, 0f);
    // 하강하며 칸 안이 서서히 물빛으로 물든다
    public Color descentAmbient = new Color(0.10f, 0.26f, 0.38f);
    [Tooltip("로비에서만 카메라 원경을 늘린다 — 창밖 바다가 수평선까지 보이게. 수중 돔은 원래 값(60m)으로 돌아간다")]
    public float lobbyFarClip = 1300f;

    [Header("로비 소리")]
    public AudioSource ambience;           // 파도·갈매기
    public AudioSource music;              // 인트로 음악
    [Range(0f, 1f)] public float ambienceVolume = 0.32f;
    [Range(0f, 1f)] public float musicVolume = 0.20f;
    [Range(0f, 1f)] public float musicDuckedVolume = 0.07f;

    [Header("걷기")]
    public float walkSpeed = 1.3f;         // 사람 보행 속도(1.4) 남짓. 느리면 이동 시간이 길어져 오히려 멀미가 쌓인다
    public float walkRamp = 0.4f;

    [Header("대사")]
    [TextArea(2, 4)]
    public string[] introLines =
    {
        "1930년, 두 사람이 이 작은 쇠공을 타고 처음으로 깊은 바다를 들여다봤습니다.",
        "지구 표면의 71%는 바다입니다.",
        "하지만 그 바다의 80% 이상은, 아직 아무도 본 적이 없습니다.",
        "오늘, 당신이 그 다음 관찰자입니다.",
    };
    [TextArea(2, 4)]
    public string descentLine = "햇빛은 수심 200m까지만 닿습니다. 그 아래는, 우리가 거의 모르는 세계입니다.";
    [TextArea(2, 4)]
    public string arrivalLine = "여기가 수심 24m 산호초예요. 헤엄치는 생물에 관찰기를 겨누고 지켜보세요.";

    // --- 상태 ---
    Phase phase = Phase.Lobby;
    ScreenFader fader;
    TimeOfDayController timeOfDay;
    Light sun;
    ClipScrubber elevatorDoors;
    ClipScrubber gateDoors;
    bool captionVisible;
    bool captionSnap;
    Vector3 captionScale;                  // captionDistance 에서의 크기. 가까이 세우면 그만큼 줄여 같은 크기로 보이게 한다
    bool lobbyAudioOn;
    bool doorsOpen;
    bool arrivedOnce;
    float audioFade = 1f;                  // 하강하며 0 으로 — 로비 소리를 줄인다

    // 수중 돔 환경을 적어 두었다가 도착할 때 되돌린다 (로비 조명으로 덮어쓰기 전에).
    struct EnvSnapshot
    {
        public CameraClearFlags clearFlags;
        public Color background;
        public Material skybox;
        public bool fog;
        public FogMode fogMode;
        public Color fogColor;
        public float fogDensity;
        public UnityEngine.Rendering.AmbientMode ambientMode;
        public Color sky, equator, ground;
        public Color sunColor;
        public float sunIntensity;
        public Quaternion sunRotation;
        public Texture sunCookie;
        public bool timeOfDayEnabled;
        public float farClip;
    }
    EnvSnapshot dome;

    public bool InLobby => phase != Phase.Dome;
    public bool IsNight => phase == Phase.Night;

    // --- 로비 활동(수심 벽 등)이 쓰는 것 ---
    /// <summary>로비에서 자유 활동을 받아도 되는지. 엘리베이터에 오른 뒤로는 받지 않는다.</summary>
    public bool AcceptsActivity => phase == Phase.Lobby;
    public Light Sun => sun;
    /// <summary>수중 돔의 햇빛 일렁임(코스틱) 쿠키. 로비에서는 꺼 두고, 수심 벽이 24m 를 보여 줄 때만 빌려 쓴다.</summary>
    public Texture WaterCookie => dome.sunCookie;
    /// <summary>로비 활동이 물속 소리를 낼 때 파도·갈매기와 음악을 줄이는 배율 (1 = 그대로).</summary>
    [System.NonSerialized] public float ambienceDuck = 1f;
    [System.NonSerialized] public float musicDuck = 1f;

    void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (rig == null)
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin != null) rig = origin.transform;
            else if (viewCamera != null) rig = viewCamera.transform.root;
        }

        // 다른 스크립트의 Start(시작 패널 배치 등)보다 먼저 로비로 옮겨 둔다.
        if (rig != null && spawnPoint != null)
        {
            rig.position = spawnPoint.position;
            rig.rotation = Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f);
        }

        if (captionGroup != null) { captionGroup.alpha = 0f; captionScale = captionGroup.transform.localScale; }
        if (depthText != null) depthText.text = FormatDepth(0f);
    }

    void Start()
    {
        // TimeOfDayController.Awake 가 수중 안개·조명을 이미 적용했다. 그 상태를 적어 둔 뒤 로비 조명으로 바꾼다.
        timeOfDay = TimeOfDayController.Instance != null ? TimeOfDayController.Instance : FindFirstObjectByType<TimeOfDayController>();
        sun = timeOfDay != null && timeOfDay.sun != null ? timeOfDay.sun : FindDirectionalLight();
        dome = CaptureEnvironment();
        ApplyLobbyEnvironment();

        AquariumAudioDirector.Instance?.SetAmbientMuted(true);

        fader = ScreenFader.Ensure(viewCamera);
        fader?.SetFadeImmediate(1f);        // 첫 몇 프레임은 헤드 포즈가 원점이라 화면이 튄다 — 검게 시작

        if (elevatorAnimator != null && elevatorClip != null)
        {
            elevatorDoors = new ClipScrubber(elevatorAnimator, elevatorClip, "ElevatorDoors");
            elevatorDoors.Sample(0f);
        }
        if (gateAnimator != null && gateClip != null)
        {
            gateDoors = new ClipScrubber(gateAnimator, gateClip, "TicketGate");
            gateDoors.Sample(0f);
        }

        if (director == null) director = DiveDirector.Instance != null ? DiveDirector.Instance : FindFirstObjectByType<DiveDirector>();

        StartCoroutine(IntroRoutine());
    }

    void OnDestroy()
    {
        elevatorDoors?.Dispose();
        gateDoors?.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // 1막: 로비 나레이션
    // ─────────────────────────────────────────────────────────────

    IEnumerator IntroRoutine()
    {
        yield return null;
        yield return new WaitForSeconds(0.6f);
        StartLobbyAudio();
        if (fader != null) yield return fader.FadeTo(0f, 1.6f);
        yield return new WaitForSeconds(1.4f);   // 둘러볼 틈

        foreach (string line in introLines)
        {
            yield return Say(line);
            yield return new WaitForSeconds(0.5f);
        }
        HideCaption();
        yield return new WaitForSeconds(0.4f);

        // 준비 단계가 있으면 직접 다니며 관찰기를 집고 연습 수조에서 첫 기록을 한다 — 개찰구는 그 뒤에 열린다.
        if (tutorial != null && tutorial.isActiveAndEnabled) tutorial.Begin();
        else if (startPanel != null) startPanel.RevealForLobby();
        else BeginEntry();                       // 시작 패널이 없는 씬에서도 멈추지 않게
    }

    /// <summary>"입장하기"를 누르면 StartPanel 이 부른다. (준비 단계가 없는 예전 흐름 — 자동 걷기)</summary>
    public void BeginEntry()
    {
        if (phase != Phase.Lobby) return;
        phase = Phase.Entering;
        StartCoroutine(EntryRoutine());
    }

    // ─────────────────────────────────────────────────────────────
    // 준비 단계(LobbyTutorial)가 부르는 것들
    // ─────────────────────────────────────────────────────────────

    /// <summary>안내 한 줄을 자막(과 음성)으로 띄우고, 읽을 시간이 지나면 끝난다.</summary>
    public IEnumerator SayLine(string line)
    {
        yield return Say(line);
        HideCaption();
    }

    /// <summary>첫 기록을 마치면 개찰구와 엘리베이터 문을 연다. 걸어 들어가는 건 관람객이 직접 한다.</summary>
    public void OpenGateAndDoors()
    {
        if (gateDoors != null && gateClip != null) StartCoroutine(Scrub(gateDoors, 0f, gateClip.length, gatePlaySeconds));
        if (elevatorDoors != null) { StartCoroutine(Scrub(elevatorDoors, 0f, doorOpenEnd, doorOpenEnd)); doorsOpen = true; }
    }

    /// <summary>관람객이 엘리베이터 칸에 들어서면 준비 단계가 부른다. 문이 닫히고 내려간다.</summary>
    public void Ride()
    {
        if (phase != Phase.Lobby) return;
        phase = Phase.Entering;
        StartCoroutine(RideRoutine());
    }

    IEnumerator RideRoutine()
    {
        HideCaption();
        FishNarrationService.Instance?.Stop();

        // 텔레포트로 들어온 자리·방향이 제각각이다. 화면을 잠깐 가린 채 칸 한가운데에서 계기판(+Z)을 보게 맞춘다.
        if (fader != null) yield return fader.FadeTo(1f, 0.25f);
        if (cabinPoint != null) AlignRig(cabinPoint.position, Vector3.forward);
        yield return new WaitForSeconds(0.1f);
        if (fader != null) yield return fader.FadeTo(0f, 0.35f);
        yield return new WaitForSeconds(0.4f);

        yield return BoardedOrDescend();
    }

    /// <summary>칸에 들어선 뒤. 층 진행이 있으면 깊이 버튼을 기다리고, 없으면(예전 씬) 바로 24m 로 내려간다.</summary>
    IEnumerator BoardedOrDescend()
    {
        if (director != null)
        {
            phase = Phase.Cabin;
            director.EnterCabin();
            yield break;
        }
        yield return CloseAndDescend();
    }

    // ─────────────────────────────────────────────────────────────
    // 걷기 → 엘리베이터 → 하강 → 도착
    // ─────────────────────────────────────────────────────────────

    IEnumerator EntryRoutine()
    {
        HideCaption();
        FishNarrationService.Instance?.Stop();

        // 1) 엘리베이터를 정면으로. 화면을 잠깐 가린 채 몸 방향과 서는 자리를 맞춘다.
        //    시작 자리에서 엘리베이터까지 12m 를 다 걸으면 자동 이동이 10초를 넘는다 — 개찰구 앞에서 시작한다.
        if (fader != null) yield return fader.FadeTo(1f, 0.3f);
        Transform start = walkStartPoint != null ? walkStartPoint : spawnPoint;
        if (start != null && cabinPoint != null)
            AlignRig(start.position, cabinPoint.position - start.position);
        yield return new WaitForSeconds(0.1f);
        if (fader != null) yield return fader.FadeTo(0f, 0.35f);

        // 2) 문이 열린다
        if (elevatorDoors != null) { yield return Scrub(elevatorDoors, 0f, doorOpenEnd, doorOpenEnd); doorsOpen = true; }

        // 3) 걸어 들어간다
        fader?.SetVignette(0.9f);
        yield return Walk();
        fader?.SetVignette(0f);
        yield return new WaitForSeconds(0.5f);

        yield return BoardedOrDescend();
    }

    /// <summary>4) 문이 닫히고 5) 내려가 6) 수중 돔에 도착한다. 자동 걷기와 직접 타기가 같이 쓴다.</summary>
    IEnumerator CloseAndDescend()
    {
        // 4) 문이 닫힌다
        yield return CloseDoors();

        // 5) 하강
        yield return Descend(0f, arrivalDepth, descentSeconds, descentLine);

        // 6) 도착 — 장소 전환은 검은 화면 뒤에서
        if (fader != null) yield return fader.FadeTo(1f, 0.9f);
        SwitchToDome();
        yield return new WaitForSeconds(0.4f);
        if (fader != null) yield return fader.FadeTo(0f, 1.4f);

        // STILL — 아무 말 없이 물고기만 헤엄치는 순간
        yield return new WaitForSeconds(2.5f);

        phase = Phase.Dome;
        if (startPanel != null) startPanel.CompleteFromLobby();
        yield return Say(arrivalLine);
        HideCaption();
    }

    IEnumerator Walk()
    {
        if (rig == null || viewCamera == null || cabinPoint == null) yield break;

        Vector3 from = Flat(viewCamera.transform.position);
        Vector3 to = Flat(cabinPoint.position);
        Vector3 path = to - from;
        float distance = path.magnitude;
        if (distance < 0.05f) yield break;
        Vector3 dir = path / distance;

        float speed = Mathf.Max(0.2f, walkSpeed);
        float ramp = Mathf.Clamp(walkRamp, 0.01f, distance / speed);
        float accel = speed / ramp;
        float duration = distance / speed + ramp;   // 가감속 구간이 더하는 시간
        float gateAlong = gatePoint != null ? Vector3.Dot(Flat(gatePoint.position) - from, dir) - 0.9f : float.MaxValue;
        bool gateOpened = gateDoors == null;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            // 사다리꼴 속도: 0.4초 가속 → 등속 → 0.4초 감속
            float s;
            if (t < ramp) s = 0.5f * accel * t * t;
            else if (t < duration - ramp) s = 0.5f * accel * ramp * ramp + speed * (t - ramp);
            else { float r = duration - t; s = distance - 0.5f * accel * r * r; }

            MoveCameraTo(from + dir * Mathf.Clamp(s, 0f, distance));

            if (!gateOpened && s >= gateAlong)
            {
                gateOpened = true;
                StartCoroutine(Scrub(gateDoors, 0f, gateClip.length, gatePlaySeconds));
            }
            yield return null;
        }
        MoveCameraTo(to);
    }

    IEnumerator CloseDoors()
    {
        if (!doorsOpen || elevatorDoors == null || elevatorClip == null) yield break;
        yield return Scrub(elevatorDoors, doorCloseStart, elevatorClip.length, elevatorClip.length - doorCloseStart);
        doorsOpen = false;
    }

    /// <summary>
    /// 닫힌 칸 안에서 계기판 숫자가 fromMeters → toMeters 로 흐르고, 칸 안 빛이 그 깊이만큼 가라앉는다(올라가면 밝아진다).
    /// 몸과 칸이 같이 움직이므로 바깥 흐름이 없다 — 이동하는 느낌은 숫자와 빛, 소리로만 준다.
    /// </summary>
    IEnumerator Descend(float fromMeters, float toMeters, float seconds, string line)
    {
        Coroutine say = string.IsNullOrWhiteSpace(line) ? null : StartCoroutine(Say(line));
        Color sky0 = RenderSettings.ambientSkyColor;
        Color eq0 = RenderSettings.ambientEquatorColor;
        Color gr0 = RenderSettings.ambientGroundColor;
        float sun0 = sun != null ? sun.intensity : 0f;
        CabinLook(toMeters, out Color sky1, out Color eq1, out Color gr1, out float sun1);
        float duration = Mathf.Max(1f, seconds);

        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            if (depthText != null) depthText.text = FormatDepth(Mathf.Lerp(fromMeters, toMeters, k));
            RenderSettings.ambientSkyColor = Color.Lerp(sky0, sky1, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(eq0, eq1, k);
            RenderSettings.ambientGroundColor = Color.Lerp(gr0, gr1, k);
            if (sun != null) sun.intensity = Mathf.Lerp(sun0, sun1, k);
            audioFade = 1f - k;
            yield return null;
        }
        if (say != null) yield return say;       // 대사가 계기판보다 길면 마저 듣고 내린다
    }

    /// <summary>
    /// 깊이에 따른 칸 안 빛. 0m 는 로비 실내광, 24m 는 descentAmbient(처음 하강의 도착값), 그 아래는 더 어둡게.
    /// 가장 어두운 1,000m 에서도 채널이 13/255 아래로 떨어지지 않게 한다 (Quest 디스플레이가 그 아래를 구분하지 못한다).
    /// </summary>
    void CabinLook(float meters, out Color sky, out Color equator, out Color ground, out float sunLevel)
    {
        Color d = descentAmbient;
        if (meters <= arrivalDepth)
        {
            float k = arrivalDepth > 0f ? Mathf.Clamp01(meters / arrivalDepth) : 1f;
            sky = Color.Lerp(ambientSky, d, k);
            equator = Color.Lerp(ambientEquator, d * 0.7f, k);
            ground = Color.Lerp(ambientGround, d * 0.35f, k);
            sunLevel = sunIntensity * Mathf.Lerp(1f, 0.25f, k);
            return;
        }
        // 24m → 200m → 1,000m: 1 → 0.55 → 0.3 배
        float dim = meters <= 200f
            ? Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(arrivalDepth, 200f, meters))
            : Mathf.Lerp(0.55f, 0.3f, Mathf.InverseLerp(200f, 1000f, meters));
        Color floor = new Color(0.05f, 0.05f, 0.06f);
        sky = Max(d * dim, floor);
        equator = Max(d * (0.7f * dim), floor * 0.8f);
        ground = Max(d * (0.35f * dim), floor * 0.6f);
        sunLevel = sunIntensity * 0.25f * dim;
    }

    static Color Max(Color a, Color b) => new Color(Mathf.Max(a.r, b.r), Mathf.Max(a.g, b.g), Mathf.Max(a.b, b.b), 1f);

    /// <summary>
    /// 칸 안 깊이 버튼을 누르면 DiveDirector 가 부른다. 문을 닫고 fromMeters → toMeters 로 오간 뒤,
    /// 검은 화면 뒤에서 돔으로 바꾸고 atBlack(층 꾸미기) → 화면이 밝아진 뒤 arrived(할 일 안내)를 부른다.
    /// 같은 층을 다시 누르면 오가지 않고 잠깐 기다렸다 내린다.
    /// </summary>
    public void RideTo(float fromMeters, float toMeters, float seconds, string line, System.Action atBlack, System.Action arrived)
    {
        if (phase != Phase.Cabin) return;
        phase = Phase.Riding;
        StartCoroutine(RideToRoutine(fromMeters, toMeters, seconds, line, atBlack, arrived));
    }

    IEnumerator RideToRoutine(float fromMeters, float toMeters, float seconds, string line, System.Action atBlack, System.Action arrived)
    {
        HideCaption();
        yield return CloseDoors();
        if (Mathf.Abs(toMeters - fromMeters) > 0.5f) yield return Descend(fromMeters, toMeters, seconds, line);
        else yield return new WaitForSeconds(0.8f);

        if (fader != null) yield return fader.FadeTo(1f, 0.9f);
        SwitchToDome();
        atBlack?.Invoke();
        yield return new WaitForSeconds(0.4f);
        if (fader != null) yield return fader.FadeTo(0f, 1.4f);

        // STILL — 아무 말 없이 물고기만 헤엄치는 순간. 층이 바뀔 때마다 먼저 바라볼 틈을 준다.
        yield return new WaitForSeconds(2.5f);
        phase = Phase.Dome;
        if (!arrivedOnce)
        {
            arrivedOnce = true;
            if (startPanel != null) startPanel.CompleteFromLobby();
        }
        arrived?.Invoke();
    }

    /// <summary>돔의 "엘리베이터 타기"를 누르면 DiveDirector 가 부른다. 검은 화면 뒤에서 칸으로 옮겨 서고 문은 닫혀 있다.</summary>
    public void ReturnToCabin(float meters, System.Action arrived)
    {
        if (phase != Phase.Dome) return;
        phase = Phase.Riding;
        StartCoroutine(ReturnRoutine(meters, arrived));
    }

    IEnumerator ReturnRoutine(float meters, System.Action arrived)
    {
        HideCaption();
        FishNarrationService.Instance?.Stop();
        if (fader != null) yield return fader.FadeTo(1f, 0.6f);

        AquariumAudioDirector.Instance?.SetAmbientMuted(true);
        if (cabinSet != null) cabinSet.SetActive(true);
        ApplyLobbyEnvironment();                 // 안개를 걷고 수중 조명을 끈다 — 칸 안은 실내다
        CabinLook(meters, out Color sky, out Color eq, out Color gr, out float sunLevel);
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = eq;
        RenderSettings.ambientGroundColor = gr;
        if (sun != null) sun.intensity = sunLevel;
        if (elevatorDoors != null) elevatorDoors.Sample(0f);
        doorsOpen = false;
        if (depthText != null) depthText.text = FormatDepth(meters);
        if (cabinPoint != null) AlignRig(cabinPoint.position, Vector3.forward);

        yield return new WaitForSeconds(0.3f);
        if (fader != null) yield return fader.FadeTo(0f, 0.6f);
        phase = Phase.Cabin;
        arrived?.Invoke();
    }

    /// <summary>
    /// 엔딩 — 돔에서 칸으로 옮겨 서고, 계기판이 fromMeters → 0m 로 오르며 칸 안이 밝아진다.
    /// 해수면에 닿으면 검은 화면 뒤에서 로비 건물을 다시 켜고 밤을 칠한 뒤, 데이터 월이 보이는 자리에 세운다.
    /// 문은 열어 둔다 — 방금 타고 올라온 엘리베이터가 뒤에 있다.
    /// </summary>
    public void Ascend(float fromMeters, float seconds, string line, System.Action atLobby)
    {
        if (phase != Phase.Dome) return;
        phase = Phase.Riding;
        StartCoroutine(AscendRoutine(fromMeters, seconds, line, atLobby));
    }

    IEnumerator AscendRoutine(float fromMeters, float seconds, string line, System.Action atLobby)
    {
        HideCaption();
        FishNarrationService.Instance?.Stop();
        if (fader != null) yield return fader.FadeTo(1f, 0.9f);

        var audio = AquariumAudioDirector.Instance;
        if (audio != null) { audio.SetAmbientMuted(true); audio.layerMusicLevel = 0f; }
        if (cabinSet != null) cabinSet.SetActive(true);
        ApplyLobbyEnvironment();
        CabinLook(fromMeters, out Color sky, out Color eq, out Color gr, out float sunLevel);
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = eq;
        RenderSettings.ambientGroundColor = gr;
        if (sun != null) sun.intensity = sunLevel;
        if (elevatorDoors != null) elevatorDoors.Sample(0f);
        doorsOpen = false;
        if (depthText != null) depthText.text = FormatDepth(fromMeters);
        if (cabinPoint != null) AlignRig(cabinPoint.position, Vector3.forward);
        yield return new WaitForSeconds(0.3f);
        if (fader != null) yield return fader.FadeTo(0f, 0.9f);

        yield return Descend(fromMeters, 0f, seconds, line);   // 이름은 Descend 지만 거꾸로도 오른다 — 숫자가 줄고 칸이 밝아진다
        yield return new WaitForSeconds(0.8f);

        // 해수면 — 검은 화면 뒤에서 로비에 밤을 칠하고 데이터 월 앞으로 옮겨 선다.
        if (fader != null) yield return fader.FadeTo(1f, 1.0f);
        if (lobbySet != null) lobbySet.SetActive(true);
        ApplyLobbyEnvironment();
        if (night != null) night.Apply();
        if (elevatorDoors != null) { elevatorDoors.Sample(doorOpenEnd); doorsOpen = true; }
        Transform spot = nightSpot != null ? nightSpot : walkStartPoint;
        if (spot != null) AlignRig(spot.position, spot.forward);
        StartLobbyAudio();
        phase = Phase.Night;
        yield return new WaitForSeconds(0.5f);
        if (fader != null) yield return fader.FadeTo(0f, 1.8f);
        atLobby?.Invoke();
    }

    void SwitchToDome()
    {
        HideCaption(true);
        lobbyAudioOn = false;
        if (ambience != null) ambience.Stop();
        if (music != null) music.Stop();
        if (lobbySet != null) lobbySet.SetActive(false);
        if (cabinSet != null) cabinSet.SetActive(false);

        RestoreEnvironment(dome);
        if (timeOfDay != null)
        {
            timeOfDay.enabled = dome.timeOfDayEnabled;
            if (timeOfDay.enabled) timeOfDay.ApplyNow();
        }
        AquariumAudioDirector.Instance?.SetAmbientMuted(false);

        if (rig != null && viewCamera != null)
        {
            // 걷기 전에 돌려 둔 몸 방향을 원래(정면 +Z)로 되돌리고, 머리가 수조 정중앙에 오게 한다.
            rig.RotateAround(viewCamera.transform.position, Vector3.up, -rig.eulerAngles.y);
            Vector3 target = domePoint != null ? domePoint.position : Vector3.zero;
            PlaceCameraOver(target);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 리그 이동
    // ─────────────────────────────────────────────────────────────

    /// <summary>머리(카메라)가 floorPoint 위에 오고, 정면이 forward 를 보도록 리그를 돌리고 옮긴다.</summary>
    void AlignRig(Vector3 floorPoint, Vector3 forward)
    {
        if (rig == null || viewCamera == null) return;
        Vector3 camForward = Flat(viewCamera.transform.forward);
        Vector3 want = Flat(forward);
        if (camForward.sqrMagnitude > 0.0001f && want.sqrMagnitude > 0.0001f)
            rig.RotateAround(viewCamera.transform.position, Vector3.up, Vector3.SignedAngle(camForward, want, Vector3.up));
        PlaceCameraOver(floorPoint);
    }

    void PlaceCameraOver(Vector3 floorPoint)
    {
        Vector3 headOffset = Flat(viewCamera.transform.position - rig.position);
        rig.position = new Vector3(floorPoint.x - headOffset.x, floorPoint.y, floorPoint.z - headOffset.z);
    }

    void MoveCameraTo(Vector3 flatTarget)
    {
        Vector3 delta = flatTarget - Flat(viewCamera.transform.position);
        rig.position += delta;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ─────────────────────────────────────────────────────────────
    // 나레이션·자막
    // ─────────────────────────────────────────────────────────────

    IEnumerator Say(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        ShowCaption(line);

        var tts = FishNarrationService.Instance;
        if (tts != null) tts.Speak(string.Empty, line);

        // 음성은 처음 한 번은 네트워크로 받아 오므로 늦게 시작할 수 있다.
        // 음성이 없거나(키 없음·오프라인) 늦으면 글자 수로 읽을 시간을 잡고, 무한정 기다리지는 않는다.
        float start = Time.time;
        float readTime = 1.6f + line.Length * 0.085f;
        float timeout = readTime + 10f;
        yield return null;
        while (Time.time - start < timeout)
        {
            bool speaking = tts != null && tts.IsSpeaking;
            if (!speaking && Time.time - start >= readTime) break;
            yield return null;
        }
    }

    void ShowCaption(string line)
    {
        if (captionText != null) captionText.text = line;
        if (!captionVisible) captionSnap = true;
        captionVisible = true;
    }

    void HideCaption(bool immediate = false)
    {
        captionVisible = false;
        if (immediate && captionGroup != null) captionGroup.alpha = 0f;
    }

    /// <summary>다른 말로 넘어갈 때(DiveDirector). 자막은 천천히 사라지고 다음 줄이 이어 받는다.</summary>
    public void HideCaptionNow() => HideCaption();

    void Update()
    {
        if (!lobbyAudioOn) return;
        bool speaking = FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking;
        float k = 1f - Mathf.Exp(-2.5f * Time.deltaTime);
        float nightLevel = phase == Phase.Night ? nightAmbienceLevel : 1f;
        if (ambience != null) ambience.volume = Mathf.Lerp(ambience.volume, ambienceVolume * audioFade * ambienceDuck * nightLevel, k);
        if (music != null)
            music.volume = Mathf.Lerp(music.volume, (speaking ? musicDuckedVolume : musicVolume) * audioFade * musicDuck, k);
    }

    void LateUpdate()
    {
        if (captionGroup == null) return;
        captionGroup.alpha = Mathf.MoveTowards(captionGroup.alpha, captionVisible ? 1f : 0f, Time.deltaTime * 2.5f);
        if (captionGroup.alpha <= 0.001f || viewCamera == null) return;

        // 시작 패널과 같은 방식으로 정면 아래를 천천히 따라온다 — 고개를 돌려도 자막을 놓치지 않는다.
        Transform cam = viewCamera.transform;
        Vector3 forward = Flat(cam.forward);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward; else forward.Normalize();
        // 가까이 세울 때는 내리는 높이와 크기를 같은 비율로 줄인다 — 시야에서는 같은 자리, 같은 크기다.
        float near = CaptionDistance(cam.position, forward) / captionDistance;
        Vector3 pos = cam.position + forward * (captionDistance * near) + Vector3.down * (captionDrop * near);
        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);

        Transform tr = captionGroup.transform;
        float k = captionSnap ? 1f : 1f - Mathf.Exp(-3f * Time.deltaTime);
        captionSnap = false;
        tr.SetPositionAndRotation(Vector3.Lerp(tr.position, pos, k), Quaternion.Slerp(tr.rotation, rot, k));
        tr.localScale = Vector3.Lerp(tr.localScale, captionScale * near, k);
    }

    /// <summary>
    /// 자막을 세울 거리. 엘리베이터 칸은 안쪽이 3m 라 칸 가운데에서 1.5m 앞이 곧 벽이다 — 깊이 버튼을 누르면 뜨는 하강 대사가
    /// 계기판 벽 뒤에 묻혀, 고개를 옆으로 돌려야 겨우 보였다. 칸 안에서는 바라보는 쪽 벽보다 cabinCaptionMargin 앞에 세운다.
    /// </summary>
    float CaptionDistance(Vector3 eye, Vector3 forward)
    {
        if (cabinPoint == null || cabinSet == null || !cabinSet.activeInHierarchy) return captionDistance;
        Vector3 p = cabinPoint.InverseTransformPoint(eye);
        float h = cabinHalfSize;
        if (Mathf.Abs(p.x) > h || Mathf.Abs(p.z) > h) return captionDistance;   // 칸 밖 (로비 홀)

        Vector3 d = cabinPoint.InverseTransformDirection(forward);
        float wall = float.PositiveInfinity;
        if (Mathf.Abs(d.x) > 1e-4f) wall = Mathf.Min(wall, ((d.x > 0f ? h : -h) - p.x) / d.x);
        if (Mathf.Abs(d.z) > 1e-4f) wall = Mathf.Min(wall, ((d.z > 0f ? h : -h) - p.z) / d.z);
        return Mathf.Clamp(wall - cabinCaptionMargin, cabinCaptionMin, captionDistance);
    }

    static string FormatDepth(float meters) => $"수심  {meters:0} m";

    // ─────────────────────────────────────────────────────────────
    // 환경 (조명·하늘·안개)
    // ─────────────────────────────────────────────────────────────

    EnvSnapshot CaptureEnvironment()
    {
        var s = new EnvSnapshot
        {
            skybox = RenderSettings.skybox,
            fog = RenderSettings.fog,
            fogMode = RenderSettings.fogMode,
            fogColor = RenderSettings.fogColor,
            fogDensity = RenderSettings.fogDensity,
            ambientMode = RenderSettings.ambientMode,
            sky = RenderSettings.ambientSkyColor,
            equator = RenderSettings.ambientEquatorColor,
            ground = RenderSettings.ambientGroundColor,
            timeOfDayEnabled = timeOfDay != null && timeOfDay.enabled,
        };
        if (viewCamera != null) { s.clearFlags = viewCamera.clearFlags; s.background = viewCamera.backgroundColor; s.farClip = viewCamera.farClipPlane; }
        if (sun != null)
        {
            s.sunColor = sun.color;
            s.sunIntensity = sun.intensity;
            s.sunRotation = sun.transform.rotation;
            s.sunCookie = sun.cookie;
        }
        return s;
    }

    void ApplyLobbyEnvironment()
    {
        // 시간대 컨트롤러는 2초마다 수중 안개를 다시 덮어쓴다 — 로비에 있는 동안은 끈다.
        if (timeOfDay != null) timeOfDay.enabled = false;

        if (viewCamera != null && lobbySkybox != null) viewCamera.clearFlags = CameraClearFlags.Skybox;
        if (viewCamera != null) viewCamera.farClipPlane = Mathf.Max(viewCamera.farClipPlane, lobbyFarClip);
        if (lobbySkybox != null) RenderSettings.skybox = lobbySkybox;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;

        if (sun != null)
        {
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.transform.rotation = Quaternion.Euler(sunEuler);
            sun.cookie = null;                   // 물결 무늬는 물속에서만
        }
    }

    void RestoreEnvironment(EnvSnapshot s)
    {
        if (viewCamera != null)
        {
            viewCamera.clearFlags = s.clearFlags;
            viewCamera.backgroundColor = s.background;
            if (s.farClip > 0f) viewCamera.farClipPlane = s.farClip;
        }
        RenderSettings.skybox = s.skybox;
        RenderSettings.fog = s.fog;
        RenderSettings.fogMode = s.fogMode;
        RenderSettings.fogColor = s.fogColor;
        RenderSettings.fogDensity = s.fogDensity;
        RenderSettings.ambientMode = s.ambientMode;
        RenderSettings.ambientSkyColor = s.sky;
        RenderSettings.ambientEquatorColor = s.equator;
        RenderSettings.ambientGroundColor = s.ground;
        if (sun != null)
        {
            sun.color = s.sunColor;
            sun.intensity = s.sunIntensity;
            sun.transform.rotation = s.sunRotation;
            sun.cookie = s.sunCookie;
        }
    }

    static Light FindDirectionalLight()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    void StartLobbyAudio()
    {
        lobbyAudioOn = true;
        audioFade = 1f;
        foreach (var src in new[] { ambience, music })
        {
            if (src == null || src.clip == null) continue;
            src.volume = 0f;
            src.loop = true;
            src.spatialBlend = 0f;
            if (!src.isPlaying) src.Play();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // glTF 문 애니메이션 — 컨트롤러 없이 클립 시간을 직접 지정한다(열기·닫기를 한 클립의 구간으로 쓴다).
    // ─────────────────────────────────────────────────────────────

    IEnumerator Scrub(ClipScrubber scrubber, float from, float to, float seconds)
    {
        if (scrubber == null) yield break;
        float duration = Mathf.Max(0.01f, seconds);
        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / duration;
            scrubber.Sample(Mathf.Lerp(from, to, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    sealed class ClipScrubber : System.IDisposable
    {
        PlayableGraph graph;
        AnimationClipPlayable playable;

        public ClipScrubber(Animator animator, AnimationClip clip, string name)
        {
            graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, name, animator);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            output.SetSourcePlayable(playable);
            graph.Play();
        }

        public void Sample(float time)
        {
            if (!graph.IsValid()) return;
            playable.SetTime(time);
            graph.Evaluate(0f);
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
