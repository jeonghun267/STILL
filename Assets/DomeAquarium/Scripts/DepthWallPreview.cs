using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 로비 왼쪽 벽 수심 안내판의 띠(24m · 200m · 1,000m)를 누르면, 그 깊이의 빛과 소리가 로비에 잠깐 퍼진다.
///
///  - 빛:   주변광·햇빛·안개·창밖 하늘 밝기를 1초에 걸쳐 그 깊이의 값으로 바꾸고, 5초 머문 뒤 1.6초에 걸쳐 되돌린다.
///          24m 에서는 수중 돔의 햇빛 일렁임(코스틱) 쿠키를 빌려 와 바닥과 벽에 물결 무늬가 진다.
///  - 퍼짐: 벽 밑에서 바닥을 따라 빛 고리가 퍼져 나가고, 그 깊이의 작은 것들이 로비를 채운다
///          (24m 떠오르는 빛 알갱이 · 200m 내려앉는 바다눈 · 1,000m 깜빡이는 생물 빛).
///  - 소리: 깊이마다 만든 물속 소리. 그동안 파도·갈매기와 음악을 줄인다.
///
/// 띠는 손(컨트롤러)으로 건드리거나(PokeSelector), 레이로 가리키고 쥐어(XRSimpleInteractable) 누른다.
/// 레이가 닿으면 띠가 살짝 밝아진다. 에디터에서는 마우스로 누른다(PokeSelector).
/// 다른 띠를 누르면 지금 상태에서 그대로 이어서 바뀌고, 같은 띠를 다시 누르면 조금 더 머문다.
/// 비용: 기존 햇빛·주변광 값만 바꾸고 파티클 3개와 바닥 고리 1장을 더한다 — 라이트·렌더 패스는 늘지 않는다 (Quest).
/// </summary>
public sealed class DepthWallPreview : MonoBehaviour
{
    [Serializable]
    public class Band
    {
        public string id;
        public PokeSelector poke;
        public XRSimpleInteractable ray;
        public Renderer fill;                  // 띠 면 (Unlit) — 가리키거나 켜져 있을 때 밝힌다
        public AudioSource sound;              // 고리 재생, 음량은 여기서 조절
        public ParticleSystem motes;
        public Color rippleColor = Color.white;
        [Range(0f, 1f)] public float volume = 0.6f;

        [Header("이 깊이의 빛")]
        public Color ambientSky;
        public Color ambientEquator;
        public Color ambientGround;
        public Color sunColor = Color.white;
        public float sunIntensity = 1f;
        public bool caustics;
        public Color fogColor;
        public float fogDensity;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        public float skyExposure = 1f;
        [Range(0f, 1f)] public float lampLevel = 1f;   // 천장 네온 밝기 배율
        [Range(0f, 1f)] public float reflectionLevel = 1f;   // 창밖 하늘 반사(바다·테라스) 세기 배율
        public Color scanTint = Color.white;            // 조명을 받지 않는 스캔 모델(바시스피어)에 곱할 색

        [NonSerialized] public Color fillBase;
        [NonSerialized] public int hovers;
        [NonSerialized] public float level;    // 소리·띠 빛의 세기 0..1
    }

    public LobbySequence lobby;
    public Band[] bands;
    public Renderer ripple;                    // 바닥에 눕힌 고리 (Unlit 가산)

    [Header("스스로 빛나거나 조명을 받지 않는 것 — 따로 어둡게 한다")]
    public Renderer[] lamps;                   // 천장 네온 패널 (발광 재질)
    public int[] lampSlots;                    // 각 렌더러에서 네온 재질의 번호
    public Renderer[] unlitScans;              // 사진 스캔 모델 — 조명이 구워져 있어(KHR_materials_unlit) 주변광을 바꿔도 그대로 밝다
    public ReflectionProbe[] skyProbes;        // 창밖 바다·테라스의 하늘 반사 — 미리 구운 밝은 하늘이라 200m·1,000m 에서도 테라스가 환하게 남았다
    public string scanColorProperty = "baseColorFactor";   // glTFast 셰이더의 기본색 속성

    [Header("시간")]
    public float riseSeconds = 1.0f;
    public float holdSeconds = 5f;
    public float fallSeconds = 1.6f;
    public float rippleSeconds = 1.8f;
    public float rippleRadius = 14f;           // 홀 대각선 남짓 — 반대편 창가까지 닿는다

    [Header("로비 소리 줄이기 (1 = 그대로)")]
    [Range(0f, 1f)] public float ambienceDuck = 0.15f;
    [Range(0f, 1f)] public float musicDuck = 0.4f;

    enum State { Idle, Rise, Hold, Fall }

    struct Look
    {
        public Color sky, equator, ground, sun, fog, skyTint, scan;
        public float sunIntensity, fogDensity, skyExposure, lamps, reflection;

        public static Look Lerp(Look a, Look b, float t) => new Look
        {
            sky = Color.Lerp(a.sky, b.sky, t),
            equator = Color.Lerp(a.equator, b.equator, t),
            ground = Color.Lerp(a.ground, b.ground, t),
            sun = Color.Lerp(a.sun, b.sun, t),
            fog = Color.Lerp(a.fog, b.fog, t),
            skyTint = Color.Lerp(a.skyTint, b.skyTint, t),
            sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
            fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
            skyExposure = Mathf.Lerp(a.skyExposure, b.skyExposure, t),
            scan = Color.Lerp(a.scan, b.scan, t),
            lamps = Mathf.Lerp(a.lamps, b.lamps, t),
            reflection = Mathf.Lerp(a.reflection, b.reflection, t),
        };
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    State state = State.Idle;
    int active = -1;
    float holdUntil;
    bool applied;                              // 로비 조명을 바꿔 둔 상태인지
    Look current;
    Color skyTintBase = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    float skyExposureBase = 1f;
    Material skyCopy;                          // 하늘 머티리얼 에셋을 건드리지 않게 런타임 복사본에 값을 넣는다
    Coroutine routine;
    MaterialPropertyBlock blockStore;
    MaterialPropertyBlock block => blockStore ??= new MaterialPropertyBlock();   // 플레이 중 스크립트를 다시 불러와도 비지 않게
    float rippleT = -1f;
    Color rippleTint;
    Color[] lampBase;
    Color[] scanBase;
    float[] probeBase;
    int scanColorId;

    void Awake()
    {
        if (ripple != null) ripple.enabled = false;
        if (bands == null) return;
        for (int i = 0; i < bands.Length; i++)
        {
            int index = i;
            Band b = bands[i];
            if (b.poke != null) b.poke.onPoked.AddListener(() => Press(index));
            if (b.ray != null)
            {
                b.ray.selectEntered.AddListener(_ => Press(index));
                b.ray.hoverEntered.AddListener(_ => b.hovers++);
                b.ray.hoverExited.AddListener(_ => b.hovers = Mathf.Max(0, b.hovers - 1));
            }
            if (b.fill != null && b.fill.sharedMaterial != null && b.fill.sharedMaterial.HasProperty(BaseColorId))
                b.fillBase = b.fill.sharedMaterial.GetColor(BaseColorId);
            if (b.sound != null) b.sound.volume = 0f;
        }

        lampBase = new Color[lamps != null ? lamps.Length : 0];
        for (int i = 0; i < lampBase.Length; i++)
        {
            Material m = SlotMaterial(lamps[i], LampSlot(i));
            lampBase[i] = m != null && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black;
        }
        probeBase = new float[skyProbes != null ? skyProbes.Length : 0];
        for (int i = 0; i < probeBase.Length; i++) probeBase[i] = skyProbes[i] != null ? skyProbes[i].intensity : 1f;
        scanColorId = Shader.PropertyToID(scanColorProperty);
        scanBase = new Color[unlitScans != null ? unlitScans.Length : 0];
        for (int i = 0; i < scanBase.Length; i++)
        {
            Material m = unlitScans[i] != null ? unlitScans[i].sharedMaterial : null;
            scanBase[i] = m != null && m.HasProperty(scanColorId) ? m.GetColor(scanColorId) : Color.white;
        }
    }

    int LampSlot(int i) => lampSlots != null && i < lampSlots.Length ? lampSlots[i] : 0;

    static Material SlotMaterial(Renderer r, int slot)
    {
        if (r == null) return null;
        Material[] mats = r.sharedMaterials;
        return slot >= 0 && slot < mats.Length ? mats[slot] : null;
    }

    void OnDisable()
    {
        // 수중 돔에 도착하면 LobbySet 이 통째로 꺼진다. 그 전에 이미 되돌렸어야 하지만, 혹시 남았으면 여기서 정리한다.
        if (applied) Abort();
    }

    void OnDestroy()
    {
        if (skyCopy != null) Destroy(skyCopy);
    }

    /// <summary>띠 하나를 누른다. 0 = 24m, 1 = 200m, 2 = 1,000m.</summary>
    public void Press(int index)
    {
        if (bands == null || index < 0 || index >= bands.Length) return;
        if (lobby == null || !lobby.AcceptsActivity) return;

        if (index == active && (state == State.Rise || state == State.Hold))
        {
            holdUntil = Time.time + holdSeconds;   // 같은 띠를 또 누르면 조금 더 머문다
            StartRipple(bands[index].rippleColor);
            return;
        }
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Show(index));
    }

    public int ActiveBand => active;

    IEnumerator Show(int index)
    {
        Band band = bands[index];
        if (active >= 0 && active != index) StopMotes(bands[active], false);
        active = index;

        if (!applied) Begin(band.fogColor);
        if (band.caustics) SetCaustics(true);
        if (band.motes != null) band.motes.Play();
        if (band.sound != null && !band.sound.isPlaying) band.sound.Play();
        StartRipple(band.rippleColor);

        state = State.Rise;
        yield return Blend(current, LookOf(band), riseSeconds);
        if (!band.caustics) SetCaustics(false);

        state = State.Hold;
        holdUntil = Time.time + holdSeconds;
        while (Time.time < holdUntil) yield return null;

        state = State.Fall;
        StopMotes(band, false);
        yield return Blend(current, Baseline(current.fog), fallSeconds);

        End();
        active = -1;
        state = State.Idle;
        routine = null;
    }

    IEnumerator Blend(Look from, Look to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            Apply(Look.Lerp(from, to, k));
            yield return null;
        }
        Apply(to);
    }

    void Update()
    {
        // 엘리베이터에 오르면 바로 로비 조명으로 되돌린다 — 하강 연출이 로비 값에서 시작해야 한다.
        if (applied && (lobby == null || !lobby.AcceptsActivity)) Abort();

        if (bands == null) return;
        float loudest = 0f;
        for (int i = 0; i < bands.Length; i++)
        {
            Band b = bands[i];
            bool on = i == active && state != State.Fall && state != State.Idle;
            float seconds = i != active ? 0.5f : state == State.Fall ? fallSeconds : riseSeconds;
            b.level = Mathf.MoveTowards(b.level, on ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, seconds));
            loudest = Mathf.Max(loudest, b.level);

            if (b.sound != null)
            {
                b.sound.volume = b.level * b.level * b.volume;
                if (b.level <= 0f && b.sound.isPlaying) b.sound.Stop();
            }
            if (b.fill != null)
            {
                float glow = Mathf.Max(b.level, b.hovers > 0 ? 0.35f : 0f);
                Color lit = Color.Lerp(b.fillBase, Color.white, 0.35f);
                lit.a = 1f;
                b.fill.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(b.fillBase, lit, glow));
                b.fill.SetPropertyBlock(block);
            }
        }
        if (lobby != null)
        {
            lobby.ambienceDuck = Mathf.Lerp(1f, ambienceDuck, loudest);
            lobby.musicDuck = Mathf.Lerp(1f, musicDuck, loudest);
        }

        UpdateRipple();
    }

    // ─────────────────────────────────────────────────────────────
    // 조명
    // ─────────────────────────────────────────────────────────────

    void Begin(Color fog)
    {
        applied = true;
        if (lobby.lobbySkybox != null && RenderSettings.skybox == lobby.lobbySkybox)
        {
            if (skyCopy == null) skyCopy = new Material(lobby.lobbySkybox) { name = "LobbySky (depth preview)", hideFlags = HideFlags.DontSave };
            if (lobby.lobbySkybox.HasProperty(TintId)) skyTintBase = lobby.lobbySkybox.GetColor(TintId);
            if (lobby.lobbySkybox.HasProperty(ExposureId)) skyExposureBase = lobby.lobbySkybox.GetFloat(ExposureId);
            RenderSettings.skybox = skyCopy;
        }
        RenderSettings.fogMode = FogMode.Exponential;
        current = Baseline(fog);
    }

    void End()
    {
        Apply(Baseline(current.fog));
        RenderSettings.fog = false;
        SetCaustics(false);
        if (skyCopy != null && RenderSettings.skybox == skyCopy) RenderSettings.skybox = lobby.lobbySkybox;
        applied = false;
    }

    /// <summary>엘리베이터 탑승·비활성화 때 — 연출을 기다리지 않고 곧장 로비 조명으로 되돌린다.</summary>
    void Abort()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (bands != null) foreach (Band b in bands) StopMotes(b, true);
        if (lobby != null) End();
        else applied = false;
        if (ripple != null) ripple.enabled = false;
        rippleT = -1f;
        active = -1;
        state = State.Idle;
    }

    Look Baseline(Color fog) => new Look
    {
        sky = lobby.ambientSky,
        equator = lobby.ambientEquator,
        ground = lobby.ambientGround,
        sun = lobby.sunColor,
        sunIntensity = lobby.sunIntensity,
        fog = fog,                               // 안개 색은 그대로 두고 짙기만 0 으로 — 검은 안개를 거쳐 가지 않게
        fogDensity = 0f,
        skyTint = skyTintBase,
        skyExposure = skyExposureBase,
        scan = Color.white,
        lamps = 1f,
        reflection = 1f,
    };

    static Look LookOf(Band b) => new Look
    {
        sky = b.ambientSky,
        equator = b.ambientEquator,
        ground = b.ambientGround,
        sun = b.sunColor,
        sunIntensity = b.sunIntensity,
        fog = b.fogColor,
        fogDensity = b.fogDensity,
        skyTint = b.skyTint,
        skyExposure = b.skyExposure,
        scan = b.scanTint,
        lamps = b.lampLevel,
        reflection = b.reflectionLevel,
    };

    void Apply(Look look)
    {
        current = look;
        RenderSettings.ambientSkyColor = look.sky;
        RenderSettings.ambientEquatorColor = look.equator;
        RenderSettings.ambientGroundColor = look.ground;
        RenderSettings.fog = look.fogDensity > 0.0005f;
        RenderSettings.fogColor = look.fog;
        RenderSettings.fogDensity = look.fogDensity;

        Light sun = lobby != null ? lobby.Sun : null;
        if (sun != null)
        {
            sun.color = look.sun;
            sun.intensity = look.sunIntensity;
        }
        if (skyCopy != null)
        {
            if (skyCopy.HasProperty(TintId)) skyCopy.SetColor(TintId, look.skyTint);
            if (skyCopy.HasProperty(ExposureId)) skyCopy.SetFloat(ExposureId, look.skyExposure);
        }
        for (int i = 0; i < lampBase.Length; i++)
        {
            Renderer r = lamps[i];
            if (r == null) continue;
            int slot = LampSlot(i);
            r.GetPropertyBlock(block, slot);
            block.SetColor(EmissionId, lampBase[i] * look.lamps);
            r.SetPropertyBlock(block, slot);
        }
        for (int i = 0; i < probeBase.Length; i++)
            if (skyProbes[i] != null) skyProbes[i].intensity = probeBase[i] * look.reflection;
        for (int i = 0; i < scanBase.Length; i++)
        {
            Renderer r = unlitScans[i];
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(scanColorId, scanBase[i] * look.scan);
            r.SetPropertyBlock(block);
        }
    }

    void SetCaustics(bool on)
    {
        Light sun = lobby != null ? lobby.Sun : null;
        if (sun == null) return;
        sun.cookie = on ? lobby.WaterCookie : null;   // 쿠키가 있으면 해의 CausticsAnimator 가 무늬를 흘린다
    }

    // ─────────────────────────────────────────────────────────────
    // 퍼지는 것들
    // ─────────────────────────────────────────────────────────────

    static void StopMotes(Band b, bool clear)
    {
        if (b.motes == null) return;
        b.motes.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
    }

    void StartRipple(Color color)
    {
        if (ripple == null) return;
        rippleT = 0f;
        rippleTint = color;
        ripple.enabled = true;
    }

    void UpdateRipple()
    {
        if (ripple == null || rippleT < 0f) return;
        rippleT += Time.deltaTime / Mathf.Max(0.1f, rippleSeconds);
        if (rippleT >= 1f)
        {
            rippleT = -1f;
            ripple.enabled = false;
            return;
        }
        float grow = 1f - (1f - rippleT) * (1f - rippleT);          // 처음엔 빠르게, 끝에서 느리게
        float diameter = Mathf.Lerp(0.4f, rippleRadius * 2f, grow);
        ripple.transform.localScale = new Vector3(diameter, diameter, 1f);
        Color c = rippleTint;
        c.a = rippleTint.a * Mathf.Pow(1f - rippleT, 1.5f);
        ripple.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        ripple.SetPropertyBlock(block);
    }
}
