using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 층 단위 하강을 잡는 막 진행 상태 머신 (기획 문서 "의뢰와 하강 연결").
///
///   로비 → 엘리베이터 칸(깊이 버튼) → 하강 → 층 도착(할 일 안내) → 관찰 → 할 일 완료
///        → "엘리베이터 타기" → 칸으로 돌아옴 → 다음 깊이 버튼(완료로 열림) → 하강 → ...
///
/// 엘리베이터 연출(문·계기판·조명·화면 전환)은 LobbySequence 가, 층의 내용(생물·조명·할 일)은 DiveMission 이 갖는다.
/// 여기서는 "지금 어느 층인가, 무엇이 열렸나, 다음에 무엇을 하나"만 정한다.
/// 할 일은 "이번 잠수에서 기록한 종"으로 센다 — 탐험가 프로필의 예전 기록으로 저절로 채워지거나 막히지 않는다.
/// </summary>
public sealed class DiveDirector : MonoBehaviour
{
    public static DiveDirector Instance { get; private set; }

    [Header("층 (얕은 곳부터)")]
    public DiveMission[] missions;

    [Header("연결 (비면 런타임에 찾는다)")]
    public LobbySequence lobby;
    public BoidManager swarm;
    public ObservationAim aim;
    public TimeOfDayController timeOfDay;
    public DiveMissionBoard board;             // 관찰기 위 할 일 두 줄
    public ElevatorCallPanel callPanel;        // 할 일을 마치면 돔에 뜨는 "엘리베이터 타기"
    public LargeCreaturePasses passes;         // 큰 생물이 가끔 지나가는 층(200m)
    public UnknownCreature unknown;            // 3막 신호의 주인 (1,000m)
    public ObservationLight deviceLight;       // 관찰기 조명 — 1막 할 일을 마치면 열린다
    public AudioSource layerAmbience;          // 층 물소리 (12초 고리, 2D)
    public GameObject bioSparks;               // 1,000m 의 반짝이는 발광 알갱이

    [Header("엔딩 (비면 런타임에 찾는다)")]
    public LobbyNight night;                   // 밤이 된 로비 · 데이터 월의 새 줄
    public CreditsRoll credits;
    public ElevatorCallPanel endPanel;         // 크레딧이 끝나면 손 닿는 곳에 "처음부터 다시"
    public LobbyTutorial tutorial;             // 밤 로비에서 다시 걸어 다니게 한다
    public float recordDelay = 1.2f;           // 밝아진 뒤 데이터 월에 새 줄이 생기기까지
    public float narrationDelay = 2.4f;        // 새 줄이 생긴 뒤 마지막 나레이션까지
    public float creditsDelay = 1.5f;

    [Header("층 꾸미기 (비면 런타임에 이름으로 찾는다)")]
    public Transform reefProps;                // ReefFloor/Generated — 산호·바위·해초·조개
    public GameObject reefLights;              // ReefFloor/CoralAccentLights — 산호 군락 조명
    public GameObject rockBed;                 // 암반 층에만 켜는 큰 바위 묶음
    public GameObject marineSnow;              // 바다눈 파티클
    public float noticeSeconds = 8f;           // 큰 생물 예고를 관찰기에 띄워 두는 시간

    [Header("관측소장 — 엘리베이터 칸")]
    [TextArea(1, 3)] public string cabinPrompt = "{0} 버튼을 눌러 주세요.";
    [TextArea(1, 3)] public string cabinFreePrompt = "가고 싶은 층의 버튼을 눌러요.";
    [TextArea(1, 3)] public string revisitLine = "여기는 기록을 마친 층이에요. 더 둘러보고, 준비되면 엘리베이터를 타세요.";

    [Header("시간")]
    public float completeDelay = 1.2f;          // 마지막 기록 카드가 뜬 뒤 축하 말까지
    public float narrationWaitLimit = 14f;      // 생물 해설이 끝나기를 이 이상은 기다리지 않는다

    public enum State { Lobby, Cabin, Riding, Briefing, Observing, Complete, Ending }

    public State Current { get; private set; } = State.Lobby;
    /// <summary>지금 있는(칸에 있다면 방금 떠나온) 층. 로비 = -1.</summary>
    public int LayerIndex { get; private set; } = -1;
    /// <summary>칸에서 고른 층. 내려가는 동안 버튼을 켜 둔다.</summary>
    public int RideTarget { get; private set; } = -1;
    public int Count => missions != null ? missions.Length : 0;
    public DiveMission Layer => MissionAt(LayerIndex);
    public float CurrentMeters => LayerIndex >= 0 && Layer != null ? Layer.meters : 0f;

    /// <summary>상태·진행이 바뀔 때마다. 엘리베이터 콘솔이 버튼을 다시 칠한다.</summary>
    public event Action Changed;

    sealed class Progress
    {
        public readonly HashSet<FishData> recorded = new HashSet<FishData>();
        public bool targetFound;
        public bool complete;
    }

    Progress[] progress;
    Coroutine talk;
    Light sun;
    Texture waterCookie;
    Camera viewCamera;

    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        progress = new Progress[Count];
        for (int i = 0; i < progress.Length; i++) progress[i] = new Progress();

        if (lobby == null) lobby = FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (swarm == null) swarm = FindFirstObjectByType<BoidManager>(FindObjectsInactive.Include);
        if (aim == null) aim = FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        if (timeOfDay == null) timeOfDay = FindFirstObjectByType<TimeOfDayController>(FindObjectsInactive.Include);
        if (passes == null) passes = FindFirstObjectByType<LargeCreaturePasses>(FindObjectsInactive.Include);
        if (unknown == null) unknown = FindFirstObjectByType<UnknownCreature>(FindObjectsInactive.Include);
        if (deviceLight == null) deviceLight = FindFirstObjectByType<ObservationLight>(FindObjectsInactive.Include);
        if (night == null) night = FindFirstObjectByType<LobbyNight>(FindObjectsInactive.Include);
        if (credits == null) credits = FindFirstObjectByType<CreditsRoll>(FindObjectsInactive.Include);
        if (tutorial == null) tutorial = FindFirstObjectByType<LobbyTutorial>(FindObjectsInactive.Include);
        if (endPanel != null) endPanel.Hide(true);
        Transform reef = GameObject.Find("ReefFloor")?.transform;
        if (reefProps == null && reef != null) reefProps = reef.Find("Generated");
        if (reefLights == null && reef != null) reefLights = reef.Find("CoralAccentLights")?.gameObject;
        if (board != null) board.Hide();
        if (callPanel != null) callPanel.Hide(true);
        if (callPanel != null && callPanel.avoid == null && aim != null) callPanel.avoid = aim.statusRoot;   // 관찰기 화면과 겹치면 비킨다
    }

    void OnEnable()
    {
        if (aim != null) aim.Recorded += OnRecorded;
        if (passes != null) passes.Approaching += OnApproaching;
        if (unknown != null) { unknown.Failed += OnUnknownFailed; unknown.HintDue += GiveHint; }
        if (credits != null) credits.Finished += OnCreditsFinished;
    }

    void OnDisable()
    {
        if (aim != null) aim.Recorded -= OnRecorded;
        if (passes != null) passes.Approaching -= OnApproaching;
        if (unknown != null) { unknown.Failed -= OnUnknownFailed; unknown.HintDue -= GiveHint; }
        if (credits != null) credits.Finished -= OnCreditsFinished;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 조회 (엘리베이터 콘솔·할 일 표시·디버그 메뉴)
    // ─────────────────────────────────────────────────────────────

    public DiveMission MissionAt(int i) => missions != null && i >= 0 && i < missions.Length ? missions[i] : null;
    public bool IsComplete(int i) => progress != null && i >= 0 && i < progress.Length && progress[i].complete;
    /// <summary>첫 층은 처음부터, 그다음 층은 바로 위 층의 할 일을 마쳐야 열린다 (관측 규정: 층마다 기준 기록).</summary>
    public bool IsUnlocked(int i) => MissionAt(i) != null && (i == 0 || IsComplete(i - 1));
    public int RecordedCount(int i) => progress != null && i >= 0 && i < progress.Length ? progress[i].recorded.Count : 0;
    public bool TargetFound(int i) => progress != null && i >= 0 && i < progress.Length && progress[i].targetFound;

    /// <summary>할 일이 남은 가장 얕은 열린 층. 다 마쳤으면 -1.</summary>
    public int SuggestedIndex
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (IsUnlocked(i) && !IsComplete(i)) return i;
            return -1;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 엘리베이터
    // ─────────────────────────────────────────────────────────────

    /// <summary>LobbySequence 가 부른다 — 로비에서 칸에 탔을 때, 또는 돔에서 칸으로 돌아왔을 때.</summary>
    public void EnterCabin()
    {
        Current = State.Cabin;
        RideTarget = -1;
        if (aim != null) aim.armed = false;
        if (board != null) board.Hide();
        if (callPanel != null) callPanel.Hide();
        Notify();

        int next = SuggestedIndex;
        string line = next >= 0 ? string.Format(cabinPrompt, MissionAt(next).DepthLabel) : cabinFreePrompt;
        Say(line);
    }

    /// <summary>칸 안 깊이 버튼. 열린 층만 받는다. 지금 층을 다시 누르면 내려가지 않고 문만 다시 연다.</summary>
    public void Select(int index)
    {
        if (Current != State.Cabin || !IsUnlocked(index) || lobby == null) return;

        DiveMission m = MissionAt(index);
        RideTarget = index;
        Current = State.Riding;
        StopTalk();
        Notify();

        // 오가는 시간은 두 층 중 깊은 쪽의 rideSeconds. 거슬러 올라갈 때는 내려가는 말을 하지 않는다.
        DiveMission deeper = MissionAt(Mathf.Max(index, LayerIndex));
        float seconds = deeper != null ? deeper.rideSeconds : m.rideSeconds;
        string line = index > LayerIndex ? m.descentLine : null;
        lobby.RideTo(CurrentMeters, m.meters, seconds, line, () => PrepareLayer(index), () => BeginLayer(index));
    }

    /// <summary>돔의 "엘리베이터 타기" 버튼. 할 일을 마친 층에서만 받는다.</summary>
    public void CallElevator()
    {
        if (Current != State.Complete || lobby == null) return;
        Current = State.Riding;
        StopTalk();
        if (callPanel != null) callPanel.Hide();
        if (board != null) board.Hide();
        if (aim != null) aim.armed = false;
        if (passes != null) passes.Stop();
        if (deviceLight != null) deviceLight.SetOn(false);
        if (layerAmbience != null) StartCoroutine(FadeAmbience(0.6f));
        Notify();
        lobby.ReturnToCabin(CurrentMeters, EnterCabin);
    }

    // ─────────────────────────────────────────────────────────────
    // 층
    // ─────────────────────────────────────────────────────────────

    /// <summary>화면이 검은 동안(LobbySequence.SwitchToDome 직후) 돔을 이 층으로 다시 꾸민다.</summary>
    void PrepareLayer(int index)
    {
        LayerIndex = index;
        DiveMission m = MissionAt(index);
        if (m == null) return;

        // 늘 헤엄치는 무리. 200m 처럼 큰 생물만 가끔 지나가는 층은 비운다.
        if (swarm != null)
        {
            swarm.species = m.species ?? new FishData[0];
            if (swarm.species.Length > 0) swarm.Rebuild();
            else swarm.Clear();
        }
        Progress p = progress[index];
        if (passes != null)
        {
            passes.Configure(m.passers, m.passInterval);
            passes.isRecorded = fish => p.recorded.Contains(fish);
            passes.priority = m.HasTarget && m.passers != null && m.passers.Any(c => c.fish == m.targets[0]) ? m.targets[0] : null;
        }
        ApplyDressing(m.seabed);
        if (marineSnow != null) marineSnow.SetActive(m.marineSnow);
        if (bioSparks != null) bioSparks.SetActive(m.bioluminescence);
        if (unknown != null)
        {
            unknown.gameObject.SetActive(m.encounter);
            if (m.encounter) unknown.Prepare();
        }
        if (board != null) board.signal = m.showSignal && unknown != null ? () => unknown.SignalStrength : (Func<float>)null;
        hintGiven = false;

        // 소리 — 층 물소리를 갈아 끼우고, 3막은 음악을 끈다.
        var audio = AquariumAudioDirector.Instance;
        if (audio != null) audio.layerMusicLevel = m.musicLevel;
        if (layerAmbience != null)
        {
            layerAmbience.Stop();
            layerAmbience.clip = m.ambience;
            layerAmbience.volume = m.ambienceVolume;
            if (m.ambience != null) layerAmbience.Play();
        }

        if (timeOfDay != null)
        {
            timeOfDay.enabled = true;
            if (m.useClock) timeOfDay.UseClock();
            else timeOfDay.SetFixedLighting(m.lighting);
        }
        // 물결 무늬 텍스처는 로비가 처음에 적어 둔 돔 환경에서 받는다 — 200m 처럼 무늬를 끈 층에서 24m 로 돌아올 때 되살린다.
        if (sun == null) sun = timeOfDay != null && timeOfDay.sun != null ? timeOfDay.sun : (lobby != null ? lobby.Sun : null);
        if (waterCookie == null && lobby != null) waterCookie = lobby.WaterCookie;
        if (sun != null) sun.cookie = m.caustics ? waterCookie : null;
        if (viewCamera == null) viewCamera = Camera.main;
        if (!m.useClock && viewCamera != null && viewCamera.clearFlags == CameraClearFlags.SolidColor)
            viewCamera.backgroundColor = m.lighting.fogColor;

        if (aim != null)
        {
            aim.knownOverride = fish => p.recorded.Contains(fish);
            aim.layerTargets = m.encounter && unknown != null ? unknown
                : passes != null && m.HasPassers ? new CompositeTargets(swarm, passes) : (IObservationTargets)null;
            aim.ResetAim();
        }
    }

    /// <summary>같은 돔을 층에 맞게 꾸민다. 산호초는 전부, 암반은 바위만 + 큰 바위 묶음, 빈 바닥은 아무것도.</summary>
    void ApplyDressing(SeabedDressing dressing)
    {
        if (reefProps != null)
        {
            for (int i = 0; i < reefProps.childCount; i++)
            {
                Transform c = reefProps.GetChild(i);
                // ReefScatter 가 "R1_Rock_07" 처럼 이름을 붙인다.
                bool show = dressing == SeabedDressing.CoralReef || (dressing == SeabedDressing.RockBed && c.name.Contains("_Rock_"));
                if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            }
        }
        if (reefLights != null) reefLights.SetActive(dressing == SeabedDressing.CoralReef);
        if (rockBed != null) rockBed.SetActive(dressing == SeabedDressing.RockBed);
    }

    /// <summary>큰 생물 예고 — 무엇인지는 말하지 않고 어느 쪽인지만 관찰기에 띄운다.</summary>
    void OnApproaching(FishData fish, Vector3 from)
    {
        if (aim == null || Current == State.Riding || Current == State.Cabin) return;
        if (viewCamera == null) viewCamera = Camera.main;
        string where = ObservationTerms.Direction(0f);
        if (viewCamera != null)
        {
            Vector3 f = viewCamera.transform.forward;
            Vector3 to = from - viewCamera.transform.position;
            f.y = 0f;
            to.y = 0f;
            if (f.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f)
                where = ObservationTerms.Direction(Vector3.SignedAngle(f, to, Vector3.up));
        }
        aim.ShowNotice(ObservationTerms.Approaching(where), noticeSeconds);
    }

    /// <summary>화면이 밝아진 뒤. 할 일을 띄우고 관측소장이 알려 준다.</summary>
    void BeginLayer(int index)
    {
        DiveMission m = MissionAt(index);
        if (m == null) return;
        RideTarget = -1;
        if (aim != null) aim.armed = true;

        bool done = IsComplete(index);
        Current = done ? State.Complete : State.Briefing;
        RefreshBoard();
        Notify();
        if (passes != null && m.HasPassers) passes.Begin(m.firstPassDelay);
        if (m.lightOnArrival && deviceLight != null) { deviceLight.Unlock(); deviceLight.SetOn(true); }
        if (!done && m.encounter && unknown != null) unknown.Begin();

        if (done)
        {
            Say(revisitLine);
            ShowCallPanel(m);
            return;
        }
        talk = StartCoroutine(BriefRoutine(m));
    }

    IEnumerator BriefRoutine(DiveMission m)
    {
        if (lobby != null) yield return lobby.SayLine(m.arrivalLine);
        talk = null;
        if (Current == State.Briefing) { Current = State.Observing; Notify(); }
    }

    void OnRecorded(FishData fish)
    {
        if (fish == null || LayerIndex < 0) return;
        if (Current != State.Briefing && Current != State.Observing && Current != State.Complete) return;

        DiveMission m = Layer;
        Progress p = progress[LayerIndex];
        p.recorded.Add(fish);
        if (m.IsTarget(fish)) p.targetFound = true;
        RefreshBoard();
        Notify();

        if (!p.complete && Meets(m, p))
        {
            p.complete = true;
            StopTalk();
            // 1막을 마치면 관찰기에 조명이 생긴다 — 어두운 200m·1,000m 에서 쓰고, 3막에서는 스스로 꺼야 한다.
            if (LayerIndex == 0 && deviceLight != null && !deviceLight.Unlocked)
            {
                deviceLight.Unlock();
                if (aim != null) aim.ShowNotice(ObservationTerms.LightUnlocked, noticeSeconds);
            }
            talk = StartCoroutine(CompleteRoutine(m));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 3막 — 신호의 주인
    // ─────────────────────────────────────────────────────────────

    bool hintGiven;

    /// <summary>조명을 켠 채 겨눠 두 번 놓치면 관측소장이 알려 준다 (기획 문서 그대로: "불을 꺼 봐요. 그리고 움직이지 말고.").</summary>
    void OnUnknownFailed(int count)
    {
        if (count >= 2) GiveHint();
    }

    void GiveHint()
    {
        DiveMission m = Layer;
        if (hintGiven || m == null || !m.encounter || string.IsNullOrWhiteSpace(m.hintLine)) return;
        if (Current != State.Briefing && Current != State.Observing) return;
        hintGiven = true;
        Say(m.hintLine);
        if (aim != null) aim.ShowNotice(ObservationTerms.LightHint, noticeSeconds + 4f);
    }

    IEnumerator FadeAmbience(float seconds)
    {
        if (layerAmbience == null) yield break;
        float from = layerAmbience.volume;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            layerAmbience.volume = Mathf.Lerp(from, 0f, t / seconds);
            yield return null;
        }
        layerAmbience.Stop();
    }

    static bool Meets(DiveMission m, Progress p) =>
        p.recorded.Count >= m.recordCount && (!m.HasTarget || p.targetFound);

    IEnumerator CompleteRoutine(DiveMission m)
    {
        Current = State.Complete;
        RefreshBoard();
        Notify();

        // 마지막 기록의 해설과 카드를 끊지 않는다. 카드가 떠 있는 동안 자막을 띄우면 눈앞에서 겹친다
        // (음성이 막혔을 때 — 해설이 없으면 카드만 6초 남는다 — 예전에는 그 위로 완료 자막이 바로 떴다).
        yield return new WaitForSeconds(completeDelay);
        float start = Time.time;
        while (((FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking) ||
                (aim != null && aim.IsCardShowing)) &&
               Time.time - start < narrationWaitLimit)
            yield return null;

        // 신호의 주인은 마지막 말과 함께 어둠 속으로 사라진다.
        if (m.encounter && unknown != null) unknown.Depart();
        if (lobby != null) yield return lobby.SayLine(m.completeLine);
        talk = null;
        if (m.finale && Current == State.Complete) { BeginEnding(m); yield break; }
        if (Current == State.Complete) ShowCallPanel(m);
    }

    // ─────────────────────────────────────────────────────────────
    // 엔딩 — 상승, 밤이 된 로비, 데이터 월의 새 줄, 마지막 나레이션, 크레딧
    // ─────────────────────────────────────────────────────────────

    /// <summary>마지막 층을 마치면 버튼을 기다리지 않고 엘리베이터가 해수면까지 올라간다.</summary>
    void BeginEnding(DiveMission m)
    {
        Current = State.Ending;
        if (aim != null) aim.armed = false;
        if (deviceLight != null) deviceLight.SetOn(false);
        if (board != null) board.Hide();
        if (callPanel != null) callPanel.Hide();
        if (passes != null) passes.Stop();
        if (layerAmbience != null) StartCoroutine(FadeAmbience(0.9f));
        Notify();
        if (lobby == null) return;
        lobby.Ascend(CurrentMeters, m.rideSeconds + 3f, m.ascentLine, () => talk = StartCoroutine(NightRoutine(m)));
    }

    IEnumerator NightRoutine(DiveMission m)
    {
        if (unknown != null) unknown.gameObject.SetActive(false);
        if (bioSparks != null) bioSparks.SetActive(false);
        yield return new WaitForSeconds(recordDelay);

        // 데이터 월에 오늘 밤의 새 줄 — 최초 관측자는 지금 탐험가
        var store = FishCollectionStore.Instance;
        string observer = store != null && store.ActiveProfile != null ? store.ActiveProfile.displayName : ObservationTerms.YouObserver;
        if (night != null) night.RevealRecord(ObservationTerms.NewRecordTitle, ObservationTerms.NewRecordDetail(m.DepthLabel, observer, DateTime.Now));
        yield return new WaitForSeconds(narrationDelay);

        if (lobby != null) yield return lobby.SayLine(m.finalLine);
        yield return new WaitForSeconds(creditsDelay);

        // 밤 로비는 걸어 다녀도 된다 — 창밖 밤바다, 수심 벽, 새 줄을 가까이 보러 간다.
        if (tutorial != null) tutorial.EnableLocomotion(true);
        if (credits != null) credits.Roll();
        talk = null;
    }

    void OnCreditsFinished()
    {
        if (endPanel != null) endPanel.Show(ObservationTerms.EndTitle);
    }

    /// <summary>"엘리베이터 타기" 판을 띄우고 관찰기 위 할 일 판은 접는다 — 같은 "기록 완료"를 두 번 띄우면 눈앞에서 겹친다.</summary>
    void ShowCallPanel(DiveMission m)
    {
        if (callPanel == null) return;
        callPanel.Show(ObservationTerms.LayerDone(m.DepthLabel));
        if (board != null) board.Hide();
    }

    // ─────────────────────────────────────────────────────────────

    void RefreshBoard()
    {
        if (board == null) return;
        DiveMission m = Layer;
        if (m == null || (callPanel != null && callPanel.IsShown)) { board.Hide(); return; }
        board.Show(m, RecordedCount(LayerIndex), TargetFound(LayerIndex), IsComplete(LayerIndex));
    }

    void Say(string line)
    {
        StopTalk();
        if (lobby == null || string.IsNullOrWhiteSpace(line)) return;
        talk = StartCoroutine(SayRoutine(line));
    }

    IEnumerator SayRoutine(string line)
    {
        yield return lobby.SayLine(line);
        talk = null;
    }

    void StopTalk()
    {
        if (talk != null) StopCoroutine(talk);
        talk = null;
        if (lobby != null) lobby.HideCaptionNow();
    }

    void Notify() => Changed?.Invoke();

#if UNITY_EDITOR
    // ─────────────────────────────────────────────────────────────
    // 헤드셋 없이 흐름을 끝까지 돌려 보는 도구 (DomeAquarium/디버그 메뉴)
    // ─────────────────────────────────────────────────────────────

    /// <summary>지금 층의 할 일을 실제 기록처럼 채운다 — 지목 생물 하나와 모자란 수만큼의 종.</summary>
    public void DebugFillLayer()
    {
        DiveMission m = Layer;
        if (m == null || IsComplete(LayerIndex)) return;
        if (m.HasTarget && !TargetFound(LayerIndex)) Record(m.targets[0]);
        foreach (var fish in m.AllSpecies.ToList())
        {
            if (IsComplete(LayerIndex)) break;
            if (fish != null && !progress[LayerIndex].recorded.Contains(fish)) Record(fish);
        }
    }

    void Record(FishData fish)
    {
        FishCollectionStore.Instance?.RegisterDiscovery(fish);
        OnRecorded(fish);
    }

    public string DebugState()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"상태={Current} 층={(Layer != null ? Layer.DepthLabel : "로비")} 고른 층={RideTarget}");
        if (passes != null && passes.IsRunning) sb.Append(" | " + passes.DebugState());
        if (unknown != null && unknown.isActiveAndEnabled) sb.Append(" | " + unknown.DebugState());
        if (credits != null && credits.Rolling) sb.Append(" | 크레딧 올라가는 중");
        for (int i = 0; i < Count; i++)
        {
            DiveMission m = MissionAt(i);
            if (m == null) continue;
            sb.Append($" | {m.DepthLabel}: {(IsUnlocked(i) ? "열림" : "잠김")} {RecordedCount(i)}/{m.recordCount}종" +
                      $"{(m.HasTarget ? (TargetFound(i) ? " 지목O" : " 지목X") : "")}{(IsComplete(i) ? " 완료" : "")}");
        }
        return sb.ToString();
    }
#endif
}
