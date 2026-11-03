using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 큰 생물이 정해진 간격으로 돔 가까이 지나간다 (기획 문서 "희귀 출현 이벤트" — 2막에서는 이것이 기본 진행이다).
///
///   예고(5초)  관찰기에 "무언가 다가와요 · 오른쪽 뒤" + 그쪽에서 소나 핑 → 고개를 돌릴 시간
///   통과(20~34초)  돔 벽 근처에서 나타나 내 옆 2.5~4.5m 를 지나 반대편 벽으로 사라진다
///   대기  다음 무리까지 (나타난 때부터 60~90초) — 놓쳐도 벌칙은 없다. 아직 기록하지 않은 종이 먼저 다시 온다.
///
/// 멀리 있을 때는 어두운 그림자로만 보이다가 가까워질수록 제 색이 돌아온다 — "가장 큰 그림자"를 찾는 층이다.
/// 관찰기에는 BoidManager 와 같은 방식(IObservationTargets)으로 답한다. 개체 번호는 미리 만든 몸 묶음의 자리 번호라
/// 생물이 나타나고 사라져도 번호가 바뀌지 않는다.
/// </summary>
public sealed class LargeCreaturePasses : MonoBehaviour, IObservationTargets
{
    [Header("돔")]
    public float tankRadius = 12f;
    public float wallMargin = 1.2f;            // 벽에 몸이 박히지 않게
    public Transform viewer;                   // 비면 Camera.main

    [Header("지나가는 길")]
    public Vector2 closest = new Vector2(2.5f, 4.5f);     // 가장 가까이 올 때의 수평 거리
    public Vector2 heightOffset = new Vector2(-2.2f, 0.8f); // 눈높이 기준 — 조금 아래가 목이 편하다
    public float warnSeconds = 5f;
    public float swayMeters = 0.6f;            // 똑바로 미끄러지지 않게 좌우·위아래로 천천히 흔든다
    [Tooltip("흔들림 때문에 머리가 길에서 벗어나는 최대 각도(°) — 작은 생물(1m) → 큰 생물(7m 이상). " +
             "몸은 움직이는 쪽을 보므로, 느리게 지나가는 고래에 0.6m 를 그대로 흔들면 머리가 좌우로 50° 넘게 돌아 옆으로 걷는 것처럼 보였다.")]
    public Vector2 swayMaxYaw = new Vector2(20f, 5f);

    [Header("그림자 → 제 색")]
    public float shadowFar = 9f;               // 이보다 멀면 거의 검은 그림자
    public float shadowNear = 5.5f;            // 이보다 가까우면 제 색
    [Range(0f, 1f)] public float shadowLevel = 0.1f;

    [Header("소리")]
    public AudioSource cueSource;              // 3D. 예고 때 생물이 올 쪽으로 옮겨 울린다
    public AudioClip cueClip;
    public float cueDistance = 4f;

    /// <summary>예고 순간. 무엇이, 어디서(월드 위치) 오는지 — DiveDirector 가 관찰기에 방향을 띄운다.</summary>
    public event Action<FishData, Vector3> Approaching;

    /// <summary>이미 기록한 종인가 — 아직 기록하지 않은 종을 먼저 보낸다. DiveDirector 가 "이번 잠수" 기준으로 채운다.</summary>
    [NonSerialized] public Predicate<FishData> isRecorded;
    /// <summary>지목 생물. 두 번째 무리로 반드시 오고, 기록하기 전까지는 한 번 걸러 한 번씩 다시 온다.</summary>
    [NonSerialized] public FishData priority;
    /// <summary>참이면 다음 무리를 미룬다 — 1,000m 에서 신호의 주인이 다가오는 동안 (가만히 있어야 하는 때에 고개를 돌리게 하지 않는다).</summary>
    [NonSerialized] public Func<bool> hold;

    sealed class Body
    {
        public Transform t;
        public FishAnimatorSync sync;
        public FishData fish;
        public Renderer[] renderers;
        public Color[][] baseColors;
        public int[][] colorIds;
        public float shade = -1f;
        public bool active;
        public Vector3 pos;
        public Vector3 offset;                 // 무리 안 자리 (지나가는 방향 기준)
        public float lag;                      // 앞 개체보다 늦게 따라오는 시간
        public float phase;
    }

    enum Phase { Off, Waiting, Warning, Passing }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    readonly List<Body> bodies = new List<Body>();
    readonly Dictionary<FishData, List<Body>> pool = new Dictionary<FishData, List<Body>>();
    readonly List<Body> group = new List<Body>();
    DiveMission.PassingCreature[] passers;
    Vector2 interval = new Vector2(60f, 90f);
    MaterialPropertyBlock mpb;
    Transform container;

    Phase phase = Phase.Off;
    float timer;
    int eventCount;
    int lastPick = -1;
    int sincePriority;
    DiveMission.PassingCreature current;
    Vector3 pathStart, pathDir, pathSide;
    float pathLength, pathSeconds, passTime;
    float swaySide, swayUp;                    // 이번 무리의 흔들림 폭(m) — 지나가는 속도 · 몸 크기에 맞춰 줄인다
    const float SwaySideHz = 0.21f, SwayUpHz = 0.13f;

    public bool IsPassing => phase == Phase.Passing;
    public bool IsRunning => phase != Phase.Off;
    public float NextEventIn => phase == Phase.Waiting ? timer : 0f;
    public FishData Current => phase == Phase.Warning || phase == Phase.Passing ? current.fish : null;

    // ─────────────────────────────────────────────────────────────
    // 준비 (화면이 검은 동안 DiveDirector 가 부른다)
    // ─────────────────────────────────────────────────────────────

    /// <summary>이 층의 큰 생물 몸을 미리 만들어 둔다. 비우면 전부 치운다.</summary>
    public void Configure(DiveMission.PassingCreature[] list, Vector2 passInterval)
    {
        Stop();
        Clear();
        passers = list;
        interval = passInterval;
        if (list == null || list.Length == 0) return;

        if (container == null)
        {
            container = new GameObject("PassingBodies").transform;
            container.SetParent(transform, false);
        }
        foreach (var p in list)
        {
            if (p.fish == null || p.fish.prefab == null || pool.ContainsKey(p.fish)) continue;
            var bucket = new List<Body>();
            int count = Mathf.Max(1, p.group.y);
            for (int i = 0; i < count; i++) bucket.Add(Spawn(p.fish, i));
            pool[p.fish] = bucket;
        }
    }

    Body Spawn(FishData fish, int index)
    {
        GameObject go = Instantiate(fish.prefab, container);
        go.name = fish.DisplayName + "_pass_" + index;
        float scale = Mathf.Max(0.001f, fish.swimScale);
        go.transform.localScale = new Vector3(scale, scale, scale);
        var sync = go.GetComponentInChildren<FishAnimatorSync>(true);
        if (sync == null) sync = go.AddComponent<FishAnimatorSync>();
        sync.bankAngle = 0f;                   // BoidManager 와 같은 이유 — 프리팹마다 롤 축이 달라 삐걱댄다

        var body = new Body { t = go.transform, sync = sync, fish = fish };
        body.renderers = go.GetComponentsInChildren<Renderer>(true);
        body.baseColors = new Color[body.renderers.Length][];
        body.colorIds = new int[body.renderers.Length][];
        for (int r = 0; r < body.renderers.Length; r++)
        {
            Material[] mats = body.renderers[r].sharedMaterials;
            body.baseColors[r] = new Color[mats.Length];
            body.colorIds[r] = new int[mats.Length];
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                int id = mat == null ? 0 : mat.HasProperty(BaseColorId) ? BaseColorId : mat.HasProperty(ColorId) ? ColorId : 0;
                body.colorIds[r][m] = id;
                body.baseColors[r][m] = id != 0 ? mat.GetColor(id) : Color.white;
            }
        }
        go.SetActive(false);
        bodies.Add(body);
        return body;
    }

    void Clear()
    {
        foreach (var b in bodies) if (b.t != null) Destroy(b.t.gameObject);
        bodies.Clear();
        pool.Clear();
        group.Clear();
        passers = null;
    }

    /// <summary>도착 안내가 시작될 때. firstDelay 뒤 첫 예고가 온다.</summary>
    public void Begin(float firstDelay)
    {
        if (passers == null || passers.Length == 0 || bodies.Count == 0) { phase = Phase.Off; return; }
        eventCount = 0;
        sincePriority = 0;
        lastPick = -1;
        phase = Phase.Waiting;
        timer = Mathf.Max(0f, firstDelay);
    }

    /// <summary>층을 떠날 때. 지나가던 생물도 바로 치운다.</summary>
    public void Stop()
    {
        phase = Phase.Off;
        foreach (var b in group) Hide(b);
        group.Clear();
    }

    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (phase == Phase.Off) return;
        timer -= Time.deltaTime;

        switch (phase)
        {
            case Phase.Waiting:
                if (timer > 0f) break;
                if (hold != null && hold()) timer = 5f;
                else Warn();
                break;
            case Phase.Warning:
                if (timer <= 0f) StartPass();
                break;
            case Phase.Passing:
                passTime += Time.deltaTime;
                bool any = false;
                foreach (var b in group) any |= Move(b);
                if (!any)
                {
                    foreach (var b in group) Hide(b);
                    group.Clear();
                    phase = Phase.Waiting;     // timer 는 이미 다음 무리까지 남은 시간이다
                }
                break;
        }
    }

    /// <summary>다음 무리를 고르고 길을 정한 뒤 예고한다.</summary>
    void Warn()
    {
        int pick = PickNext();
        if (pick < 0) { phase = Phase.Off; return; }
        current = passers[pick];
        lastPick = pick;
        eventCount++;
        if (priority != null && current.fish == priority) sincePriority = 0; else sincePriority++;

        Transform v = ResolveViewer();
        Vector3 eye = v != null ? v.position : transform.position + Vector3.up * 1.5f;

        // 돔을 가로지르는 직선. 눈에서 closest 만큼 옆으로 비켜 지나간다.
        float yaw = UnityEngine.Random.value * Mathf.PI * 2f;
        pathDir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        pathSide = Vector3.Cross(Vector3.up, pathDir);
        float side = UnityEngine.Random.Range(closest.x, closest.y) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        float lift = UnityEngine.Random.Range(heightOffset.x, heightOffset.y);
        Vector3 near = eye + pathSide * side + Vector3.up * lift;

        Vector3 c = transform.position;
        Vector2 offset = new Vector2(near.x - c.x, near.z - c.z);
        float radius = Mathf.Max(1f, tankRadius - wallMargin);
        // 가장 가까운 점에서 벽까지 (수평). 플레이어가 한가운데가 아니어도 벽 안쪽에서 시작하고 끝난다.
        float along = Vector2.Dot(offset, new Vector2(pathDir.x, pathDir.z));
        float perp2 = offset.sqrMagnitude - along * along;
        float half = Mathf.Sqrt(Mathf.Max(1f, radius * radius - perp2));
        pathStart = near - pathDir * (half + along);
        pathLength = 2f * half;
        pathSeconds = Mathf.Max(5f, current.seconds > 0f ? current.seconds : 30f);

        // 흔들림의 옆 속도가 지나가는 속도에 비해 크면 머리가 좌우로 크게 돈다 — 머리가 길에서 swayMaxYaw 넘게 벗어나지 않을 만큼만 흔든다.
        // 사인 흔들림 A·sin(2πft) 의 최대 옆 속도는 2πfA, 지나가는 속도 v 에서 머리 각은 atan(2πfA / v).
        float speed = pathLength / pathSeconds;
        float maxYaw = Mathf.Lerp(swayMaxYaw.x, swayMaxYaw.y, Mathf.InverseLerp(1f, 7f, current.fish.bodyLength)) * Mathf.Deg2Rad;
        swaySide = Mathf.Min(swayMeters, speed * Mathf.Tan(maxYaw) / (2f * Mathf.PI * SwaySideHz));
        swayUp = Mathf.Min(swayMeters * 0.5f, speed * Mathf.Tan(maxYaw * 0.6f) / (2f * Mathf.PI * SwayUpHz));

        // 무리 짜기 — 앞장선 하나 뒤로 체장의 1.3배 간격, 옆으로 조금씩 흩어진다.
        group.Clear();
        if (pool.TryGetValue(current.fish, out var bucket))
        {
            int count = Mathf.Clamp(UnityEngine.Random.Range(current.group.x, current.group.y + 1), 1, bucket.Count);
            float len = Mathf.Max(0.3f, current.fish.bodyLength);
            for (int i = 0; i < count; i++)
            {
                Body b = bucket[i];
                b.offset = i == 0 ? Vector3.zero
                    : new Vector3(UnityEngine.Random.Range(-1f, 1f) * len * 0.9f, UnityEngine.Random.Range(-0.5f, 0.5f) * len, 0f);
                b.lag = i == 0 ? 0f : i * len * 1.3f / Mathf.Max(0.1f, pathLength / pathSeconds) + UnityEngine.Random.Range(0f, 0.4f);
                b.phase = UnityEngine.Random.value * 10f;
                group.Add(b);
            }
        }

        phase = Phase.Warning;
        timer = warnSeconds;
        Approaching?.Invoke(current.fish, pathStart);

        if (cueSource != null && cueClip != null && v != null)
        {
            Vector3 toward = pathStart - eye;
            toward.y = 0f;
            if (toward.sqrMagnitude > 1e-4f) cueSource.transform.position = eye + toward.normalized * cueDistance;
            cueSource.PlayOneShot(cueClip);
        }
    }

    void StartPass()
    {
        phase = Phase.Passing;
        passTime = 0f;
        // 다음 무리까지: 나타난 때부터 interval. 예고 시간은 이미 지났으니 그만큼 뺀다.
        timer = Mathf.Max(pathSeconds + 4f, UnityEngine.Random.Range(interval.x, interval.y) - warnSeconds);
        foreach (var b in group)
        {
            b.shade = -1f;
            b.active = false;
            Move(b);
        }
    }

    /// <summary>한 개체를 길 위로 옮긴다. 아직 출발 전이거나 이미 지나갔으면 false.</summary>
    bool Move(Body b)
    {
        float t = passTime - b.lag;
        float u = t / pathSeconds;
        if (u < 0f) return true;               // 출발 전 — 무리의 뒤쪽 개체
        if (u > 1f) { Hide(b); return false; }

        if (!b.active)
        {
            b.active = true;
            b.t.gameObject.SetActive(true);
        }

        Vector3 p = PathPoint(u, b);
        Vector3 ahead = PathPoint(Mathf.Min(1f, u + 0.01f), b);
        Vector3 vel = (ahead - p) / Mathf.Max(1e-4f, pathSeconds * 0.01f);
        b.pos = p;
        b.t.position = p;
        if (vel.sqrMagnitude > 1e-6f)
        {
            Quaternion want = Quaternion.LookRotation(vel, Vector3.up);
            b.t.rotation = b.shade < 0f ? want : Quaternion.Slerp(b.t.rotation, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }
        if (b.sync != null) b.sync.SetVelocity(vel);

        Transform v = ResolveViewer();
        float dist = v != null ? Vector3.Distance(v.position, p) : 0f;
        float k = Mathf.Lerp(shadowLevel, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shadowFar, shadowNear, dist)));
        Shade(b, k);
        return true;
    }

    Vector3 PathPoint(float u, Body b)
    {
        float t = u * pathSeconds;
        Vector3 sway = pathSide * (Mathf.Sin((t * SwaySideHz + b.phase) * Mathf.PI * 2f) * swaySide)
                     + Vector3.up * (Mathf.Sin((t * SwayUpHz + b.phase * 0.7f) * Mathf.PI * 2f) * swayUp);
        // 처음과 끝에 흔들림을 줄여 벽 밖으로 나가지 않게 한다.
        float edge = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
        Vector3 local = pathSide * b.offset.x + Vector3.up * b.offset.y;
        return pathStart + pathDir * (pathLength * u) + (sway * edge) + local;
    }

    void Shade(Body b, float k)
    {
        if (b.shade >= 0f && Mathf.Abs(k - b.shade) < 0.02f) return;
        b.shade = k;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        for (int r = 0; r < b.renderers.Length; r++)
        {
            Renderer rend = b.renderers[r];
            if (rend == null) continue;
            for (int m = 0; m < b.colorIds[r].Length; m++)
            {
                int id = b.colorIds[r][m];
                if (id == 0) continue;
                Color c = b.baseColors[r][m];
                rend.GetPropertyBlock(mpb, m);
                mpb.SetColor(id, new Color(c.r * k, c.g * k, c.b * k, c.a));
                rend.SetPropertyBlock(mpb, m);
            }
        }
    }

    void Hide(Body b)
    {
        b.active = false;
        if (b.t != null && b.t.gameObject.activeSelf) b.t.gameObject.SetActive(false);
    }

    /// <summary>
    /// 다음에 보낼 종. 지목 생물은 두 번째 무리로 반드시 오고, 기록 전이면 한 번 걸러 다시 온다.
    /// 나머지는 아직 기록하지 않은 종 중에서 가중치로 고르되, 바로 앞 무리와 같은 종은 피한다.
    /// </summary>
    int PickNext()
    {
        if (passers == null || passers.Length == 0) return -1;
        if (priority != null && !Recorded(priority) && (eventCount == 1 || (eventCount > 1 && sincePriority >= 1)))
            for (int i = 0; i < passers.Length; i++)
                if (passers[i].fish == priority && pool.ContainsKey(priority)) return i;

        bool anyNew = false;
        for (int i = 0; i < passers.Length; i++)
            if (Usable(i) && !Recorded(passers[i].fish) && passers[i].fish != priority) { anyNew = true; break; }

        float total = 0f;
        for (int i = 0; i < passers.Length; i++) total += Weight(i, anyNew);
        if (total <= 0f)
        {
            for (int i = 0; i < passers.Length; i++) if (Usable(i)) return i;
            return -1;
        }
        float pick = UnityEngine.Random.value * total;
        for (int i = 0; i < passers.Length; i++)
        {
            pick -= Weight(i, anyNew);
            if (pick <= 0f && Weight(i, anyNew) > 0f) return i;
        }
        return lastPick >= 0 ? lastPick : 0;
    }

    float Weight(int i, bool onlyNew)
    {
        if (!Usable(i) || (i == lastPick && passers.Length > 1)) return 0f;
        if (onlyNew && (Recorded(passers[i].fish) || passers[i].fish == priority)) return 0f;
        return Mathf.Max(0.01f, passers[i].weight);
    }

    bool Usable(int i) => passers[i].fish != null && pool.ContainsKey(passers[i].fish);
    bool Recorded(FishData f) => isRecorded != null && isRecorded(f);

    Transform ResolveViewer()
    {
        if (viewer != null) return viewer;
        Camera cam = Camera.main;
        if (cam != null) viewer = cam.transform;
        return viewer;
    }

    // ─────────────────────────────────────────────────────────────
    // 관찰기 (IObservationTargets)
    // ─────────────────────────────────────────────────────────────

    public int AgentCount => bodies.Count;

    public bool TryGetAgent(int index, out Vector3 position, out FishData fish)
    {
        position = Vector3.zero;
        fish = null;
        if (index < 0 || index >= bodies.Count || !bodies[index].active) return false;
        position = bodies[index].pos;
        fish = bodies[index].fish;
        return true;
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (phase != Phase.Passing || direction.sqrMagnitude < 1e-8f) return -1;
        direction.Normalize();
        int best = -1;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < bodies.Count; i++)
        {
            Body b = bodies[i];
            if (!b.active) continue;
            if (!ObservationCone.Contains(origin, direction, b.pos, b.fish, maxDistance, coneDegrees, out float angle)) continue;
            if (angle < bestAngle) { bestAngle = angle; best = i; }
        }
        return best;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (index < 0 || index >= bodies.Count || !bodies[index].active || direction.sqrMagnitude < 1e-8f) return false;
        Body b = bodies[index];
        return ObservationCone.Contains(origin, direction.normalized, b.pos, b.fish, maxDistance, coneDegrees, out _);
    }

#if UNITY_EDITOR
    /// <summary>디버그 메뉴: 기다리지 않고 바로 다음 무리를 예고한다.</summary>
    public void DebugNow()
    {
        if (phase == Phase.Off) Begin(0f);
        if (phase == Phase.Waiting) timer = 0f;
    }

    public string DebugState() =>
        $"통과={phase} 다음까지={(phase == Phase.Waiting ? timer : 0f):0.0}s 무리={(Current != null ? Current.DisplayName : "-")} " +
        $"x{group.Count} 지금까지={eventCount}번 몸={bodies.Count}개";
#endif
}
