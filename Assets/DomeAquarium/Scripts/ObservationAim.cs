using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// 관찰기 — 헤엄치는 생물을 손으로 겨누고 지켜봐서 기록한다. 미스터리 상자를 대신하는 1막의 핵심 행동이다.
///
///  1. 겨누기: 오른손(컨트롤러 또는 손 추적) 정면의 원뿔 안에 들어온 개체를 BoidManager 에서 찾는다.
///     개체에는 콜라이더가 없어서 레이캐스트가 아니라 각도로 판정한다.
///  2. 지켜보기: 같은 종을 계속 겨누면 진행 링이 찬다. 놓쳐도 0으로 떨어지지 않고 천천히 줄어든다.
///     무리 속에서 옆 개체로 옮겨 가도 같은 종이면 진행이 이어진다.
///  3. 기록: 링이 다 차면 관찰 일지에 남기고 정보 카드와 음성 해설을 띄운다. 생물은 내 앞으로 오지 않고 제 갈 길을 간다.
///
/// 이미 기록한 종은 이름과 "기록됨"만 보여 주고 진행시키지 않는다.
/// 이동 기능이 없으므로 플레이어는 제자리에서 몸과 손만 돌려 찾는다.
/// </summary>
public sealed class ObservationAim : MonoBehaviour
{
    [Header("참조")]
    public BoidManager swarm;                // 수중 돔의 무리
    [Tooltip("로비 연습 수조. 켜져 있는 동안(로비)은 이쪽을 겨누고, 로비가 꺼지면 돔 무리로 넘어간다")]
    public PracticeTank practiceTank;
    [Tooltip("연습 중에는 이미 기록한 종도 다시 지켜보고 기록할 수 있다 — 돌아온 관람객이 연습에서 막히지 않게")]
    public bool practiceMode;
    [Tooltip("겨누는 손. 켜져 있는 첫 번째를 쓴다 — 컨트롤러와 손 추적은 XRInputModalityManager 가 번갈아 켠다")]
    public Transform[] aimSources;
    public Transform viewer;                 // 카메라. 비면 Camera.main
    public FishInfoPanel infoPanel;
    public FishEncyclopediaPanel logPanel;   // 관찰 일지가 열려 있는 동안은 겨누지 않는다 (태블릿 뒤에서 기록되면 안 된다)
    public HapticImpulsePlayer haptics;      // 오른손 컨트롤러. 없으면 건너뛴다

    [Header("조준")]
    public float maxDistance = 6f;
    [Tooltip("새 대상을 잡는 원뿔 반각(도)")]
    public float coneDegrees = 5f;
    [Tooltip("이미 잡은 대상을 놓치지 않는 원뿔 반각(도). 무리 속에서 대상이 이웃으로 깜빡이지 않게 넓게 둔다")]
    public float holdConeDegrees = 9f;
    [Tooltip("같은 생물을 이만큼 겨눈 뒤에야 링을 띄우고 지켜보기를 센다. 스쳐 지나가는 물고기마다 링이 깜빡이지 않게 한다 " +
             "(MS 시선 응시 가이드: 피드백 시작 전 150~250ms)")]
    public float feedbackDelay = 0.2f;

    [Header("지켜보는 시간 (초)")]
    public float smallSeconds = 1.5f;        // 체장 smallLength 이하 — 작아서 조준선에서 자주 빠진다
    public float mediumSeconds = 2f;
    public float largeSeconds = 3f;          // 체장 largeLength 이상 — 가끔 지나가서 기다려야 한다
    public float smallLength = 0.12f;
    public float largeLength = 0.8f;
    [Tooltip("놓쳤을 때 줄어드는 속도. 채우는 속도 대비 배수")]
    public float decayMultiplier = 0.5f;
    public float recordCooldown = 1f;

    [Header("정보 카드")]
    public float cardDistance = 1.3f;
    public float cardDrop = 0.05f;
    // 읽는 패널은 눈높이보다 5~15° 아래가 편하고, 10° 넘게 올려다보게 하면 안 된다 (Android XR, MS 편안함 가이드).
    // 생물이 위에 있어도 카드는 눈높이 아래에 둔다.
    public float cardMinPitch = -30f;        // 생물이 이보다 아래에 있어도 카드는 여기까지만 내린다
    public float cardMaxPitch = -5f;
    public float cardMinSeconds = 6f;        // 음성이 일찍 끝나도 이만큼은 보여 준다
    public float cardMaxSeconds = 20f;       // 음성이 길어져도 이 이상 붙잡지 않는다

    [Header("표시 — 겨눈 생물에 붙는 링")]
    public Transform marker;                 // 월드 공간 캔버스 루트. 부모 스케일 1 인 곳에 둔다
    public RectTransform markerRing;
    public RectTransform markerFillRect;
    public Image markerRingImage;
    public Image markerFill;
    public TMP_Text markerLabel;
    [Tooltip("거리와 상관없이 링이 이 각도(도)로 보이게 크기를 맞춘다 (Meta 손 UI 가이드: 겨눔 대상 2.5~3°)")]
    public float markerAngularSize = 3f;
    public float markerCanvasPixels = 256f;  // 마커 캔버스의 기준 크기(sizeDelta)
    public float markerMaxGrow = 1.6f;       // 큰 생물이라도 링은 기준의 이 배수까지만 키운다
    public Color unknownColor = new Color(0.55f, 0.95f, 1f, 1f);
    public Color knownColor = new Color(1f, 1f, 1f, 0.55f);

    [Header("표시 — 관찰기 화면")]
    public Transform statusRoot;
    public TMP_Text statusText;
    public TMP_Text countText;
    public float statusLift = 0.09f;         // 손 위로 띄우는 높이(m)

    [Header("표시 — 조준선")]
    public LineRenderer aimLine;
    public float aimLineIdleLength = 1.2f;
    public Color aimLineIdleColor = new Color(1f, 1f, 1f, 0.25f);

    [Header("소리")]
    public AudioSource sfx;                  // 3D 소리. 기록 순간 생물 위치로 옮겨 울린다 (Meta: 클릭음은 대상 위치에서)
    public AudioClip recordClip;

    [HideInInspector] public bool armed;     // StartPanel 이 수중 돔 도착 시 켠다. 로비에서는 꺼져 있다

    public event Action<FishData> Recorded;

#if UNITY_EDITOR
    // 헤드셋 없이 플레이 모드에서 기록 흐름을 끝까지 돌려 보기 위한 도구(에디터 메뉴가 켠다).
    // 켜면 아직 기록하지 않은 가장 가까운 생물을 카메라째 바라본다.
    [NonSerialized] public bool debugLockNearest;
#endif

    // --- 조준 상태
    int target = -1;
    FishData progressFish;
    float progress;
    float lastRecordTime = -999f;
    Transform currentSource;
    FishData hoverFish;
    float hoverTime;
    IObservationTargets lastTargets;
    Coroutine hapticRoutine;

    // --- 기록 상태
    int sessionCount;
    string lastRecordedName;
    FishData cardFish;
    float cardShownAt;
    Transform cardAnchor;

    // --- 표시 캐시 (문자열은 상태가 바뀔 때만 만든다)
    FishData labelFish;
    int labelState = -1;
    int recordVersion;
    int shownRecordVersion = -1;
    bool visualsActive = true;
    Transform viewerCache;

    // --- 기록 여부 캐시. IsDiscovered 는 호출마다 람다를 만들어 매 프레임 부르기엔 비싸다.
    readonly Dictionary<FishData, bool> knownCache = new Dictionary<FishData, bool>();
    readonly HashSet<FishData> recordedWithoutStore = new HashSet<FishData>();
    FishCollectionStore subscribedStore;

    const int LabelUnknown = 0, LabelWatching = 1, LabelKnown = 2;

    void OnDisable()
    {
        SetVisualsActive(false);
        if (cardFish != null && infoPanel != null) infoPanel.Hide();
        cardFish = null;
    }

    void OnDestroy()
    {
        if (subscribedStore != null) subscribedStore.Changed -= OnStoreChanged;
        subscribedStore = null;
    }

    void Update()
    {
        UpdateCard();

        Transform view = ResolveViewer();
        bool logOpen = logPanel != null && logPanel.IsOpen;
        IObservationTargets targets = ResolveTargets();
        if (!armed || targets == null || view == null || logOpen)
        {
            target = -1;
            SetVisualsActive(false);
            return;
        }

        Vector3 origin, dir;
        bool fromHand;
        if (!ResolveAim(view, out origin, out dir, out fromHand))
        {
            SetVisualsActive(false);
            return;
        }
        SetVisualsActive(true);

        // 1) 대상 고르기 — 잡고 있던 개체가 넓은 원뿔 안에 남아 있으면 바꾸지 않는다.
        if (target < 0 || !targets.IsAgentInCone(target, origin, dir, maxDistance, holdConeDegrees))
            target = targets.FindAgentInCone(origin, dir, maxDistance, coneDegrees);

        Vector3 targetPos = Vector3.zero;
        FishData fish = null;
        if (target >= 0 && !targets.TryGetAgent(target, out targetPos, out fish)) target = -1;

        // 스쳐 가는 생물은 무시한다 — 같은 종을 feedbackDelay 이상 겨눠야 대상으로 친다.
        if (fish != hoverFish) { hoverFish = fish; hoverTime = 0f; }
        else if (fish != null) hoverTime += Time.deltaTime;
        if (hoverTime < feedbackDelay) fish = null;

        // 2) 지켜보기
        bool known = fish != null && !practiceMode && IsKnown(fish);
        bool cooling = Time.unscaledTime - lastRecordTime < recordCooldown;
        if (fish != null && !known && !cooling)
        {
            // 다른 종으로 옮기면 처음부터. 같은 종의 이웃 개체로 옮기면 이어진다.
            if (fish != progressFish) { progressFish = fish; progress = 0f; }
            progress += Time.deltaTime;
            if (progress >= DwellSeconds(fish))
            {
                Record(fish, targetPos, view);
                progress = 0f;
                progressFish = null;
                known = true;
            }
        }
        else
        {
            progress -= Time.deltaTime * Mathf.Max(0f, decayMultiplier);
            if (progress <= 0f) { progress = 0f; progressFish = null; }
        }

        // 3) 표시
        UpdateMarker(fish, known, targetPos, view);
        UpdateAimLine(origin, dir, fromHand, fish, known, targetPos);
        UpdateStatus(fromHand, view);
    }

    // ---------------------------------------------------------------- 기록

    void Record(FishData fish, Vector3 fishPos, Transform view)
    {
        lastRecordTime = Time.unscaledTime;

        FishCollectionStore store = FishCollectionStore.Instance;
        bool isNew;
        if (store != null) isNew = store.RegisterDiscovery(fish);
        else isNew = recordedWithoutStore.Add(fish);

        if (isNew) sessionCount++;
        lastRecordedName = fish.DisplayName;
        recordVersion++;

        ShowCard(fish, fishPos, view);

        if (sfx != null && recordClip != null)
        {
            // 이 오브젝트 자체에 붙은 소리라면 옮기지 않는다 — 옮기면 카드 앵커 같은 자식이 같이 끌려간다.
            if (sfx.transform != transform) sfx.transform.position = fishPos;
            sfx.PlayOneShot(recordClip);
        }
        if (haptics != null)
        {
            if (hapticRoutine != null) StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(SuccessHaptic());
        }
        AquariumAudioDirector.Instance?.PulseEventDuck(2.2f);

        Recorded?.Invoke(fish);
    }

    /// <summary>
    /// 기록한 생물 쪽으로 눈앞에 카드를 세운다. 생물을 따라가면 카드가 같이 떠다녀 읽을 수 없다.
    /// 바닥 근처 생물을 내려다보며 기록했는데 카드가 눈높이 정면에 뜨면 시야 밖이라 못 본다 —
    /// 생물 쪽 높낮이를 따르되, 목이 꺾이지 않게 위아래 각도만 제한한다.
    /// </summary>
    void ShowCard(FishData fish, Vector3 fishPos, Transform view)
    {
        if (infoPanel == null) return;

        if (cardAnchor == null)
        {
            cardAnchor = new GameObject("ObservationCardAnchor").transform;
            cardAnchor.SetParent(transform, false);
        }

        Vector3 eye = view.position;
        Vector3 toFish = fishPos - eye;
        Vector3 flat = new Vector3(toFish.x, 0f, toFish.z);
        if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(view.forward.x, 0f, view.forward.z);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        flat.Normalize();

        float pitch = Mathf.Atan2(toFish.y, new Vector2(toFish.x, toFish.z).magnitude) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, cardMinPitch, cardMaxPitch) * Mathf.Deg2Rad;
        Vector3 dir = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);

        cardAnchor.position = eye + dir * cardDistance - Vector3.up * cardDrop;
        infoPanel.Show(fish, cardAnchor);
        cardFish = fish;
        cardShownAt = Time.unscaledTime;
    }

    void UpdateCard()
    {
        if (cardFish == null || infoPanel == null) return;

        float shown = Time.unscaledTime - cardShownAt;
        bool speaking = FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking;
        if (shown >= cardMaxSeconds || (shown >= cardMinSeconds && !speaking))
        {
            infoPanel.Hide();
            cardFish = null;
        }
    }

    /// <summary>Meta 햅틱 가이드의 "성공" 패턴 — 175ms 동안 점점 세지는 두 번의 진동.</summary>
    System.Collections.IEnumerator SuccessHaptic()
    {
        haptics.SendHapticImpulse(0.3f, 0.05f);
        yield return new WaitForSecondsRealtime(0.1f);
        haptics.SendHapticImpulse(0.65f, 0.075f);
        hapticRoutine = null;
    }

    float DwellSeconds(FishData fish)
    {
        float len = fish.bodyLength;
        if (len <= smallLength) return Mathf.Max(0.1f, smallSeconds);
        if (len >= largeLength) return Mathf.Max(0.1f, largeSeconds);
        return Mathf.Max(0.1f, mediumSeconds);
    }

    bool IsKnown(FishData fish)
    {
        FishCollectionStore store = FishCollectionStore.Instance;
        if (store == null) return recordedWithoutStore.Contains(fish);

        if (subscribedStore != store)
        {
            if (subscribedStore != null) subscribedStore.Changed -= OnStoreChanged;
            subscribedStore = store;
            store.Changed += OnStoreChanged;   // 기록·탐험가 전환 때 캐시를 비운다
            knownCache.Clear();
        }

        bool known;
        if (!knownCache.TryGetValue(fish, out known))
        {
            known = store.IsDiscovered(fish);
            knownCache[fish] = known;
        }
        return known;
    }

    void OnStoreChanged() => knownCache.Clear();

    // ---------------------------------------------------------------- 조준 입력

    bool ResolveAim(Transform view, out Vector3 origin, out Vector3 dir, out bool fromHand)
    {
        fromHand = false;
        currentSource = null;

#if UNITY_EDITOR
        if (debugLockNearest && TryDebugLock(view, out origin, out dir)) return true;
        // 헤드셋이 없으면 컨트롤러는 켜져 있어도 추적되지 않아 바닥에 박혀 있다. 게임뷰 마우스로 겨눈다.
        if (!UnityEngine.XR.XRSettings.isDeviceActive && TryEditorMouse(view, out origin, out dir)) return true;
#endif

        if (aimSources != null)
        {
            for (int i = 0; i < aimSources.Length; i++)
            {
                Transform s = aimSources[i];
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                origin = s.position;
                dir = s.forward;
                fromHand = true;
                currentSource = s;
                return true;
            }
        }

        // 손이 둘 다 잡히지 않으면 시선으로 겨눈다.
        origin = view.position;
        dir = view.forward;
        return true;
    }

#if UNITY_EDITOR
    bool TryEditorMouse(Transform view, out Vector3 origin, out Vector3 dir)
    {
        origin = Vector3.zero;
        dir = Vector3.forward;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        Camera cam = view.GetComponent<Camera>();
        if (mouse == null || cam == null) return false;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        origin = ray.origin;
        dir = ray.direction;
        return true;
    }

    bool TryDebugLock(Transform view, out Vector3 origin, out Vector3 dir)
    {
        origin = view.position;
        dir = view.forward;

        IObservationTargets targets = ResolveTargets();
        if (targets == null) return false;

        // 잡고 있는 대상이 아직 기록 전이면 그대로 따라간다.
        Vector3 pos;
        FishData fish;
        if (!(target >= 0 && targets.TryGetAgent(target, out pos, out fish) && (practiceMode || !IsKnown(fish))))
        {
            int best = -1;
            float bestDist = maxDistance * 0.9f;
            for (int i = 0; i < targets.AgentCount; i++)
            {
                if (!targets.TryGetAgent(i, out pos, out fish) || (!practiceMode && IsKnown(fish))) continue;
                float d = Vector3.Distance(pos, origin);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best < 0) return false;
            target = best;
            targets.TryGetAgent(best, out pos, out fish);
        }

        dir = (pos - origin).normalized;
        view.rotation = Quaternion.LookRotation(dir, Vector3.up);
        return true;
    }

    /// <summary>에디터 메뉴가 콘솔에 찍는 한 줄 상태.</summary>
    public string DebugState()
    {
        FishData fish = null;
        Vector3 pos;
        IObservationTargets targets = ResolveTargets();
        if (target >= 0 && targets != null) targets.TryGetAgent(target, out pos, out fish);
        return $"armed={armed} source={(targets == (IObservationTargets)practiceTank && practiceTank != null ? "연습 수조" : "돔")} " +
               $"target={(fish != null ? fish.DisplayName : "-")} " +
               $"progress={progress:0.00}/{(progressFish != null ? DwellSeconds(progressFish) : 0f):0.0}s " +
               $"session={sessionCount} card={(cardFish != null ? cardFish.DisplayName : "-")}";
    }
#endif

    /// <summary>로비에 있는 동안은 연습 수조, 로비가 꺼지면(수중 돔 도착) 돔 무리. 바뀌는 순간 잡고 있던 대상을 놓는다.</summary>
    IObservationTargets ResolveTargets()
    {
        // Unity 오브젝트의 null 비교는 인터페이스로 바꾸면 깨지므로 구체 타입에서 먼저 가린다.
        IObservationTargets current = null;
        if (practiceTank != null && practiceTank.isActiveAndEnabled) current = practiceTank;
        else if (swarm != null) current = swarm;

        if (!ReferenceEquals(current, lastTargets))
        {
            lastTargets = current;
            target = -1;
            progress = 0f;
            progressFish = null;
        }
        return current;
    }

    Transform ResolveViewer()
    {
        if (viewer != null) return viewer;
        if (viewerCache != null) return viewerCache;
        Camera cam = Camera.main;
        if (cam != null) viewerCache = cam.transform;
        return viewerCache;
    }

    // ---------------------------------------------------------------- 표시

    void SetVisualsActive(bool active)
    {
        if (visualsActive == active) return;
        visualsActive = active;
        if (!active && marker != null) marker.gameObject.SetActive(false);
        if (statusRoot != null) statusRoot.gameObject.SetActive(active);
        if (!active && aimLine != null) aimLine.enabled = false;
    }

    /// <summary>
    /// 보는 사람을 향하되 롤은 0 (기울어진 글자는 멀미를 부른다 — Meta·MS 가이드).
    /// 다만 거의 수직으로 내려다보거나 올려다보면 월드 위쪽 기준이 무너져 글자가 뒤집히므로, 그때만 머리 위쪽을 쓴다.
    /// </summary>
    static Quaternion Billboard(Vector3 awayNormalized, Transform view)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(awayNormalized, Vector3.up)) > 0.94f ? view.up : Vector3.up;
        return Quaternion.LookRotation(awayNormalized, up);
    }

    void UpdateMarker(FishData fish, bool known, Vector3 fishPos, Transform view)
    {
        if (marker == null) return;

        bool show = fish != null;
        if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        if (!show) return;

        Vector3 toFish = fishPos - view.position;
        float dist = toFish.magnitude;
        if (dist < 1e-3f) return;
        Vector3 away = toFish / dist;

        // 링을 몸 중심에 두면 물고기 몸통이 링 절반을 가린다. 몸 앞쪽으로 당겨 둔다.
        float halfBody = Mathf.Max(0f, fish.bodyLength) * 0.5f;
        float markerDist = Mathf.Max(0.3f, dist - halfBody - 0.05f);
        marker.position = view.position + away * markerDist;
        marker.rotation = Billboard(away, view);

        // 거리와 상관없이 같은 크기로 보이게 한다. 큰 생물은 링을 조금 키우되,
        // 링 스프라이트는 테두리 두께도 같이 커지므로 markerMaxGrow 배까지만 — 바다거북에서 링이 도넛처럼 두꺼워졌다.
        float worldPerPixel = 2f * markerDist * Mathf.Tan(markerAngularSize * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, markerCanvasPixels);
        marker.localScale = new Vector3(worldPerPixel, worldPerPixel, worldPerPixel);
        float ringPixels = Mathf.Clamp(fish.bodyLength * 1.1f / Mathf.Max(1e-6f, worldPerPixel),
            markerCanvasPixels, markerCanvasPixels * Mathf.Max(1f, markerMaxGrow));
        Vector2 ringSize = new Vector2(ringPixels, ringPixels);
        if (markerRing != null) markerRing.sizeDelta = ringSize;
        if (markerFillRect != null) markerFillRect.sizeDelta = ringSize;
        if (markerLabel != null) markerLabel.rectTransform.anchoredPosition = new Vector2(0f, -ringPixels * 0.5f - 72f);

        Color tint = known ? knownColor : unknownColor;
        if (markerRingImage != null) markerRingImage.color = new Color(tint.r, tint.g, tint.b, tint.a * 0.45f);
        if (markerFill != null)
        {
            markerFill.color = tint;
            markerFill.fillAmount = known ? 1f
                : (progressFish == fish ? Mathf.Clamp01(progress / DwellSeconds(fish)) : 0f);
        }

        int state = known ? LabelKnown : (progressFish == fish && progress > 0f ? LabelWatching : LabelUnknown);
        if (markerLabel != null && (state != labelState || fish != labelFish))
        {
            labelState = state;
            labelFish = fish;
            markerLabel.text = state == LabelKnown
                ? fish.DisplayName + "  ·  " + ObservationTerms.Recorded
                : state == LabelWatching
                    ? ObservationTerms.Unidentified + "  ·  " + ObservationTerms.Watching
                    : ObservationTerms.Unidentified;
            markerLabel.color = tint;
        }
    }

    void UpdateAimLine(Vector3 origin, Vector3 dir, bool fromHand, FishData fish, bool known, Vector3 fishPos)
    {
        if (aimLine == null) return;

        // 시선·마우스로 겨눌 때는 눈에서 나오는 선이 점으로만 보여 의미가 없다.
        if (aimLine.enabled != fromHand) aimLine.enabled = fromHand;
        if (!fromHand) return;

        Vector3 start = origin + dir * 0.03f;
        Vector3 end = fish != null ? fishPos : origin + dir * aimLineIdleLength;
        aimLine.SetPosition(0, start);
        aimLine.SetPosition(1, end);

        Color c = fish == null ? aimLineIdleColor : (known ? knownColor : unknownColor);
        aimLine.startColor = new Color(c.r, c.g, c.b, c.a * 0.6f);
        aimLine.endColor = new Color(c.r, c.g, c.b, 0f);
    }

    void UpdateStatus(bool fromHand, Transform view)
    {
        if (statusRoot == null) return;

        // 손 위에 띄운 작은 화면. 손이 안 잡히면 시야 아래쪽에 둔다.
        if (fromHand && currentSource != null)
            statusRoot.position = currentSource.position + Vector3.up * statusLift;
        else
            statusRoot.position = view.position + view.forward * 0.7f - view.up * 0.3f;

        Vector3 away = statusRoot.position - view.position;
        if (away.sqrMagnitude > 1e-6f) statusRoot.rotation = Billboard(away.normalized, view);

        // 기록이 하나라도 일어나면 다시 쓴다 — 연습 수조에서 이미 기록한 종을 다시 기록해도(새 종 수는 그대로) 화면이 바뀐다.
        if (shownRecordVersion == recordVersion) return;
        shownRecordVersion = recordVersion;

        // 첫 기록 전에는 무엇을 하라는지만 알려 준다. 그 뒤로는 방금 기록한 생물을 남긴다.
        if (statusText != null)
            statusText.text = string.IsNullOrEmpty(lastRecordedName)
                ? ObservationTerms.AimHint
                : ObservationTerms.Recorded + "  ·  " + lastRecordedName;
        if (countText != null) countText.text = ObservationTerms.SessionCount(sessionCount);
    }
}
