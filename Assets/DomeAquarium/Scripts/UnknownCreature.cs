using System;
using UnityEngine;

/// <summary>
/// 3막의 신호의 주인 — 일지에 없는 생물 (기획 문서 "엔딩: 가만히 있어야 만날 수 있다").
///
///   신호      어둠 속 9.5m 밖에서 푸른 빛이 세 번씩 깜빡인다. 관찰기를 그쪽으로 겨누면 신호 세기가 오르고 삑 소리가 잦아진다.
///   물러남    조명을 켠 채 겨누면 빛을 피해 11m 밖으로 물러나 흐려진다 (실패 한 번). 두 번이면 관측소장이 알려 준다.
///   다가옴    조명을 끄고 5초 가만히 있으면 천천히 다가와 눈앞 2.4m 에 멈춘다. 움직이면 다시 물러나지만 벌칙은 없다.
///   가까이    스스로 빛을 낸다. 관찰기로 지켜보면 기록된다 — "일치하는 기록 없음".
///   떠남      기록이 끝나면 어둠 속으로 사라진다.
///
/// 모습: 실존 종을 흉내 내지 않은 창작 생물. 빗해파리에서 착안한 반투명한 타원 몸에 무지갯빛 띠 8줄이 흐르고,
/// 뒤로 가는 실 두 가닥이 늘어진다. 메시는 코드로 만든다(외부 모델 없음). 셰이더: Glow Jelly · Glow Bands.
/// </summary>
public sealed class UnknownCreature : MonoBehaviour, IObservationTargets
{
    [Header("연결 (비면 런타임에 찾는다)")]
    public FishData fish;                      // 기록용 — 이름 칸이 빈 생물
    public ObservationLight deviceLight;
    public StillnessDetector stillness;
    public ObservationAim aim;
    public Transform view;

    [Header("모습")]
    public Material bodyMaterial;
    public Material coreMaterial;
    public Material bandMaterial;
    public Material haloMaterial;
    public Material filamentMaterial;
    public Vector3 bodyRadii = new Vector3(0.42f, 0.42f, 0.70f);
    public int bandCount = 8;
    public float filamentLength = 2.6f;
    public float haloSize = 2.8f;
    public Color haloColor = new Color(0.35f, 0.65f, 1f, 1f);

    [Header("거리 (눈 기준)")]
    public float signalDistance = 9.5f;
    public float retreatDistance = 11.2f;
    public float meetDistance = 2.4f;
    public float signalYaw = -40f;             // 도착할 때 보던 방향에서 왼쪽으로 — 둘러보게 한다
    public float heightOffset = -0.4f;
    public float approachSpeed = 0.55f;        // 9.5m → 2.4m 약 13초
    public float backOffSpeed = 0.9f;
    public float retreatSpeed = 1.6f;
    public float returnSpeed = 0.6f;
    public float leaveSeconds = 7f;

    [Header("가만히 있기")]
    public float stillNeeded = 5f;             // 처음 다가오기까지
    public float resumeNeeded = 2.5f;          // 한 번 다가왔다 물러난 뒤 다시 다가오기까지
    public float lightForgetSeconds = 2.5f;    // 빛이 사라지고 이만큼 지나야 신호 자리로 돌아온다
    public float hintAfterSeconds = 45f;       // 조명을 켠 채 이만큼 아무 일도 없으면 알려 준다

    [Header("신호")]
    public float blinkPeriod = 4.5f;
    public float signalCone = 35f;             // 이 각도 안으로 겨누면 신호가 잡힌다
    public AudioSource beepSource;
    public AudioClip beepClip;

    public enum Mode { Hidden, Signal, Retreating, Approaching, Close, Leaving, Gone }

    public Mode State { get; private set; } = Mode.Hidden;
    /// <summary>0~1. 관찰기 할 일 판의 신호 막대와 삑 소리 간격.</summary>
    public float SignalStrength { get; private set; }
    public int Failures { get; private set; }
    public float Distance => distance;

    /// <summary>조명을 켠 채 겨눠 물러날 때마다 (몇 번째인가).</summary>
    public event Action<int> Failed;
    /// <summary>조명을 켠 채 한참 아무 일도 없을 때 한 번.</summary>
    public event Action HintDue;

    Transform body, core, bands, halo;
    Renderer bodyR, coreR, bandsR, haloR;
    LineRenderer[] filaments;
    MaterialPropertyBlock mpb;
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    Vector3 anchor;                            // 눈 위치(도착 때). 돔 안에서는 걸어 다니지 않는다
    Vector3 dir;                               // 눈 → 생물 수평 방향
    float distance;
    float glow;                                // 0~1 지금 밝기
    float lastLitTime = -99f, lastFailTime = -99f;
    float idleTime, beepTimer, leaveTimer, spin;
    bool approachedOnce, hintGiven;

    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (deviceLight == null) deviceLight = FindFirstObjectByType<ObservationLight>(FindObjectsInactive.Include);
        if (stillness == null) stillness = FindFirstObjectByType<StillnessDetector>(FindObjectsInactive.Include);
        if (aim == null) aim = FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        mpb = new MaterialPropertyBlock();
        BuildVisuals();
        SetVisible(false);
    }

    /// <summary>화면이 검은 동안(층 준비). 숨겨 두고 처음 상태로.</summary>
    public void Prepare()
    {
        State = Mode.Hidden;
        Failures = 0;
        approachedOnce = false;
        hintGiven = false;
        idleTime = 0f;
        SignalStrength = 0f;
        glow = 0f;
        SetVisible(false);
    }

    /// <summary>도착 안내와 함께. 보던 방향에서 왼쪽 먼 곳에 신호가 켜진다.</summary>
    public void Begin()
    {
        Transform v = ResolveView();
        anchor = v != null ? v.position : transform.position + Vector3.up * 1.5f;
        Vector3 f = v != null ? new Vector3(v.forward.x, 0f, v.forward.z) : Vector3.forward;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        dir = (Quaternion.Euler(0f, signalYaw, 0f) * f.normalized).normalized;
        distance = retreatDistance;
        lastLitTime = lastFailTime = -99f;
        State = Mode.Signal;
        SetVisible(true);
        Place(0f);
    }

    /// <summary>기록이 끝나면 DiveDirector 가 부른다 — 어둠 속으로 사라진다.</summary>
    public void Depart()
    {
        if (State == Mode.Hidden || State == Mode.Gone) return;
        State = Mode.Leaving;
        leaveTimer = 0f;
    }

    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (State == Mode.Hidden || State == Mode.Gone) return;
        float dt = Time.deltaTime;
        Vector3 pos = CurrentPosition();
        bool lit = deviceLight != null && deviceLight.IsLighting(pos, bodyRadii.z);
        if (lit) lastLitTime = Time.time;
        bool still = stillness != null ? stillness.IsStill : true;
        bool moving = stillness != null && stillness.IsMoving;
        float stillFor = stillness != null ? stillness.StillSeconds : 999f;
        bool lightOn = deviceLight != null && deviceLight.On;

        switch (State)
        {
            case Mode.Signal:
                distance = Mathf.MoveTowards(distance, signalDistance, returnSpeed * dt);
                if (lit) { StartRetreat(); break; }
                if (!lightOn && stillFor >= (approachedOnce ? resumeNeeded : stillNeeded)) { State = Mode.Approaching; approachedOnce = true; }
                break;

            case Mode.Retreating:
                distance = Mathf.MoveTowards(distance, retreatDistance, retreatSpeed * dt);
                if (Time.time - lastLitTime > lightForgetSeconds) State = Mode.Signal;
                break;

            case Mode.Approaching:
            case Mode.Close:
                if (lit) { StartRetreat(); break; }
                if (lightOn) { distance = Mathf.MoveTowards(distance, signalDistance, backOffSpeed * dt); if (distance >= signalDistance - 0.01f) State = Mode.Signal; break; }
                if (moving)
                {
                    // 움직이면 물러난다 — 벌칙은 없다. 다시 가만히 있으면 더 짧게 기다려도 돌아온다.
                    distance = Mathf.MoveTowards(distance, signalDistance, backOffSpeed * dt);
                    State = distance >= signalDistance - 0.01f ? Mode.Signal : Mode.Approaching;
                }
                else if (still)
                {
                    distance = Mathf.MoveTowards(distance, meetDistance, approachSpeed * dt);
                    if (distance <= meetDistance + 0.01f) State = Mode.Close;
                }
                break;

            case Mode.Leaving:
                leaveTimer += dt;
                distance += dt * 1.1f;
                if (leaveTimer >= leaveSeconds) { State = Mode.Gone; SetVisible(false); return; }
                break;
        }

        // 조명을 켠 채 한참 신호만 보고 있으면 — 겨누지 않아 실패도 쌓이지 않는 경우 — 한 번 알려 준다.
        if (State == Mode.Signal && lightOn) idleTime += dt;
        if (!hintGiven && idleTime >= hintAfterSeconds) { hintGiven = true; HintDue?.Invoke(); }

        Place(dt);
        UpdateSignal(dt);
    }

    void StartRetreat()
    {
        State = Mode.Retreating;
        if (Time.time - lastFailTime < 3f) return;
        lastFailTime = Time.time;
        Failures++;
        Failed?.Invoke(Failures);
    }

    Vector3 CurrentPosition()
    {
        float bob = Mathf.Sin(Time.time * 0.4f) * 0.15f;
        return anchor + dir * distance + Vector3.up * (heightOffset + bob);
    }

    /// <summary>위치·자세·밝기·실 가닥.</summary>
    void Place(float dt)
    {
        transform.position = CurrentPosition();

        // 멀리서는 천천히 돌고, 다가올 때는 입 쪽(+Z)을 사람에게, 가까이서는 옆모습을 보이며 천천히 돈다.
        spin += dt * (State == Mode.Close ? 9f : 14f);
        Quaternion want;
        if (State == Mode.Approaching) want = Quaternion.LookRotation(-dir, Vector3.up);
        else want = Quaternion.LookRotation(Quaternion.Euler(0f, spin, 0f) * Vector3.Cross(Vector3.up, dir), Vector3.up)
                    * Quaternion.Euler(Mathf.Sin(Time.time * 0.3f) * 10f, 0f, 0f);
        transform.rotation = dt <= 0f ? want : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-1.5f * dt));

        // 밝기: 신호일 때는 세 번 깜빡임, 가까이서는 숨 쉬듯 일정하게, 빛을 피할 때와 떠날 때는 흐리게.
        float target;
        float blink = Blink(Time.time);
        switch (State)
        {
            case Mode.Signal: target = 0.18f + blink * 0.9f; break;
            case Mode.Retreating: target = 0.1f + blink * 0.3f; break;
            case Mode.Approaching: target = Mathf.Lerp(0.85f, 0.35f, Mathf.InverseLerp(meetDistance, signalDistance, distance)) + blink * 0.3f; break;
            case Mode.Close: target = 0.9f + Mathf.Sin(Time.time * 1.2f) * 0.1f; break;
            case Mode.Leaving: target = Mathf.Clamp01(1f - leaveTimer / Mathf.Max(0.1f, leaveSeconds)) * 0.8f; break;
            default: target = 0f; break;
        }
        glow = dt <= 0f ? target : Mathf.MoveTowards(glow, target, dt * 4f);
        ApplyGlow(glow, State == Mode.Close || State == Mode.Approaching || State == Mode.Leaving);
        UpdateFilaments();
    }

    /// <summary>blinkPeriod 마다 0.18초씩 세 번 켜진다.</summary>
    float Blink(float time)
    {
        float t = Mathf.Repeat(time, blinkPeriod);
        for (int i = 0; i < 3; i++)
        {
            float start = i * 0.4f;
            if (t >= start && t <= start + 0.18f) return Mathf.Sin((t - start) / 0.18f * Mathf.PI);
        }
        return 0f;
    }

    void UpdateSignal(float dt)
    {
        float target = 0f;
        Transform v = ResolveView();
        if (State != Mode.Leaving && v != null)
        {
            Vector3 origin = aim != null && aim.HasAim ? aim.AimOrigin : v.position;
            Vector3 forward = aim != null && aim.HasAim ? aim.AimDirection : v.forward;
            float angle = Vector3.Angle(forward, transform.position - origin);
            float near = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(retreatDistance, meetDistance, distance));
            target = Mathf.Clamp01(1f - angle / signalCone) * near;
        }
        SignalStrength = Mathf.MoveTowards(SignalStrength, target, dt * 2.5f);

        // 신호 탐지기처럼 겨눌수록 삑 소리가 잦아진다. 다가오기 시작하면 그친다 — 이제는 눈으로 본다.
        bool beeping = SignalStrength > 0.05f && (State == Mode.Signal || State == Mode.Retreating);
        if (!beeping || v == null || beepSource == null || beepClip == null) { beepTimer = 0.3f; return; }
        beepTimer -= dt;
        if (beepTimer > 0f) return;
        beepTimer = Mathf.Lerp(2.4f, 0.5f, SignalStrength * SignalStrength);
        Vector3 to = transform.position - v.position;
        beepSource.transform.position = v.position + to.normalized * 3f;
        beepSource.pitch = 1.45f + SignalStrength * 0.25f;
        beepSource.PlayOneShot(beepClip, 0.15f + SignalStrength * 0.3f);
    }

    // ─────────────────────────────────────────────────────────────
    // 관찰기 (IObservationTargets) — 가까이 와서 스스로 빛날 때만 겨눌 수 있다
    // ─────────────────────────────────────────────────────────────

    bool Recordable => fish != null && (State == Mode.Close || (State == Mode.Approaching && distance < meetDistance + 0.8f));

    public int AgentCount => 1;

    public bool TryGetAgent(int index, out Vector3 position, out FishData data)
    {
        position = transform.position;
        data = null;
        if (index != 0 || !Recordable) return false;
        data = fish;
        return true;
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (!Recordable || direction.sqrMagnitude < 1e-8f) return -1;
        return ObservationCone.Contains(origin, direction.normalized, transform.position, fish, maxDistance, coneDegrees, out _) ? 0 : -1;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (index != 0 || !Recordable || direction.sqrMagnitude < 1e-8f) return false;
        return ObservationCone.Contains(origin, direction.normalized, transform.position, fish, maxDistance, coneDegrees, out _);
    }

    // ─────────────────────────────────────────────────────────────
    // 모습 만들기 (코드로 만든 메시)
    // ─────────────────────────────────────────────────────────────

    void BuildVisuals()
    {
        if (bodyMaterial == null) return;
        body = Part("Body", Ellipsoid(bodyRadii, 28, 18), bodyMaterial, out bodyR);
        core = Part("Core", Ellipsoid(bodyRadii * 0.38f, 16, 10), coreMaterial != null ? coreMaterial : bodyMaterial, out coreR);
        if (bandMaterial != null) bands = Part("Bands", BandMesh(), bandMaterial, out bandsR);
        if (haloMaterial != null) halo = Part("Halo", Quad(), haloMaterial, out haloR);

        if (filamentMaterial != null)
        {
            filaments = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Filament" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 18;
                lr.sharedMaterial = filamentMaterial;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.035f), new Keyframe(1f, 0.004f));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                filaments[i] = lr;
            }
        }
    }

    Transform Part(string name, Mesh mesh, Material mat, out Renderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        renderer = r;
        return go.transform;
    }

    void SetVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    void ApplyGlow(float g, bool bandsAwake)
    {
        if (bodyR != null) { bodyR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, 0.25f + g * 1.1f); bodyR.SetPropertyBlock(mpb); }
        if (coreR != null) { coreR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, g * 1.4f); coreR.SetPropertyBlock(mpb); }
        if (bandsR != null) { bandsR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, (bandsAwake ? 0.4f : 0.12f) + g * (bandsAwake ? 1.3f : 0.5f)); bandsR.SetPropertyBlock(mpb); }
        if (haloR != null)
        {
            haloR.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(haloColor.r, haloColor.g, haloColor.b, Mathf.Clamp01(0.08f + g * 0.55f)));
            haloR.SetPropertyBlock(mpb);
        }
        if (filaments != null)
            foreach (var lr in filaments)
            {
                if (lr == null) continue;
                lr.startColor = new Color(0.55f, 0.85f, 1f, 0.15f + g * 0.6f);
                lr.endColor = new Color(0.55f, 0.85f, 1f, 0f);
            }
    }

    void LateUpdate()
    {
        if (halo == null || State == Mode.Hidden || State == Mode.Gone) return;
        Transform v = ResolveView();
        if (v == null) return;
        halo.SetPositionAndRotation(transform.position, Quaternion.LookRotation(transform.position - v.position, Vector3.up));
        halo.localScale = Vector3.one * haloSize;
    }

    void UpdateFilaments()
    {
        if (filaments == null) return;
        float t = Time.time;
        Vector3 back = -transform.forward;
        for (int k = 0; k < filaments.Length; k++)
        {
            LineRenderer lr = filaments[k];
            if (lr == null) continue;
            float side = k == 0 ? 1f : -1f;
            Vector3 start = transform.TransformPoint(new Vector3(side * bodyRadii.x * 0.6f, -0.05f, -bodyRadii.z * 0.55f));
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                Vector3 sway = transform.right * (Mathf.Sin(t * 1.3f + i * 0.45f + k) * 0.18f * u)
                             + transform.up * (Mathf.Sin(t * 0.9f + i * 0.35f + k * 2f) * 0.12f * u - 0.25f * u * u);
                lr.SetPosition(i, start + back * (filamentLength * u) + transform.right * side * 0.25f * u + sway);
            }
        }
    }

    static Mesh Ellipsoid(Vector3 r, int lon, int lat)
    {
        var mesh = new Mesh { name = "UnknownEllipsoid" };
        var v = new Vector3[(lon + 1) * (lat + 1)];
        var n = new Vector3[v.Length];
        var tris = new int[lon * lat * 6];
        int idx = 0;
        for (int y = 0; y <= lat; y++)
        {
            float theta = Mathf.PI * y / lat;                   // 0 = 앞 끝(+Z)
            for (int x = 0; x <= lon; x++)
            {
                float phi = 2f * Mathf.PI * x / lon;
                var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                v[idx] = Vector3.Scale(unit, r);
                n[idx] = new Vector3(unit.x / r.x, unit.y / r.y, unit.z / r.z).normalized;
                idx++;
            }
        }
        int t = 0;
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
            }
        mesh.vertices = v;
        mesh.normals = n;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>앞 끝에서 뒤 끝으로 몸을 따라 흐르는 띠 bandCount 줄. 몸 표면보다 3% 밖, 가운데가 넓고 끝이 가늘다.</summary>
    Mesh BandMesh()
    {
        const int steps = 26;
        int rows = Mathf.Max(1, bandCount);
        var mesh = new Mesh { name = "UnknownBands" };
        var v = new Vector3[rows * (steps + 1) * 2];
        var uv0 = new Vector2[v.Length];
        var uv1 = new Vector2[v.Length];
        var tris = new int[rows * steps * 6];
        int vi = 0, ti = 0;
        for (int k = 0; k < rows; k++)
        {
            float phi = 2f * Mathf.PI * k / rows;
            var east = new Vector3(-Mathf.Sin(phi), Mathf.Cos(phi), 0f);
            int baseIndex = vi;
            for (int s = 0; s <= steps; s++)
            {
                float u = s / (float)steps;
                float theta = Mathf.Lerp(0.1f, 0.9f, u) * Mathf.PI;
                var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                Vector3 p = Vector3.Scale(unit, bodyRadii) * 1.03f;
                float w = 0.008f + 0.03f * Mathf.Sin(theta);
                v[vi] = p - east * w; uv0[vi] = new Vector2(k / (float)rows, u); uv1[vi] = new Vector2(0f, 0f); vi++;
                v[vi] = p + east * w; uv0[vi] = new Vector2(k / (float)rows, u); uv1[vi] = new Vector2(1f, 0f); vi++;
            }
            for (int s = 0; s < steps; s++)
            {
                int a = baseIndex + s * 2;
                tris[ti++] = a; tris[ti++] = a + 2; tris[ti++] = a + 1;
                tris[ti++] = a + 1; tris[ti++] = a + 2; tris[ti++] = a + 3;
            }
        }
        mesh.vertices = v;
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh Quad()
    {
        var mesh = new Mesh { name = "UnknownHalo" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    Transform ResolveView()
    {
        if (view != null) return view;
        Camera cam = Camera.main;
        if (cam != null) view = cam.transform;
        return view;
    }

#if UNITY_EDITOR
    public string DebugState() =>
        $"미확인 생물={State} 거리={distance:0.0}m 밝기={glow:0.00} 신호={SignalStrength:0.00} 실패={Failures} " +
        $"가만히={(stillness != null ? stillness.StillSeconds : 0f):0.0}s 움직임={(stillness != null && stillness.IsMoving)} 조명={(deviceLight != null && deviceLight.On)}";
#endif
}
