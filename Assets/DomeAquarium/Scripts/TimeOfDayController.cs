using System;
using UnityEngine;

/// <summary>
/// 실제 시계에 맞춰 수조 안 분위기를 바꾼다.
/// 아침엔 안쪽까지 환하고, 저녁엔 가라앉고, 밤엔 어두워지면서 산호가 은은히 빛난다.
///
/// 비용: 매 프레임이 아니라 updateInterval(기본 2초)마다 전역 조명/안개/머티리얼 프로퍼티
/// 몇 개만 갱신한다. 드로우콜도, 셰이더 패스도 늘지 않는다.
/// </summary>
public class TimeOfDayController : MonoBehaviour
{
    [Serializable]
    public struct Key
    {
        public float hour;              // 0~24
        public Color sunColor;
        public float sunIntensity;
        public Color ambientSky;
        public Color ambientEquator;
        public Color ambientGround;
        public Color fogColor;
        public float fogDensity;
        public Color waterShallow;
        public Color waterDeep;
        public float shaftStrength;     // 빛기둥 세기
        public float coralGlow;         // 산호 자체 발광 (밤에 강해진다)
    }

    [Header("참조")]
    public Light sun;
    public Material waterMaterial;      // Mat_WaterTankInner
    // 공유 머티리얼만 건드린다. 레퍼런스처럼 산호는 따뜻한 금빛, 해초는 차가운 청록으로 나눈다.
    public Material coralGlowMaterial;
    public Color coralGlowTint = new Color(1.00f, 0.72f, 0.30f);
    public Material seaweedGlowMaterial;
    public Color seaweedGlowTint = new Color(0.25f, 0.75f, 1.00f);

    [Header("동작")]
    public bool useSystemClock = true;
    [Range(0f, 24f)] public float overrideHour = 13f;   // useSystemClock 이 꺼졌을 때 쓸 시각
    public float updateInterval = 2f;

    [Header("시간대")]
    public Key[] keys;

    static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
    static readonly int DeepId = Shader.PropertyToID("_DeepColor");
    static readonly int ShaftId = Shader.PropertyToID("_ShaftStrength");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    float timer;

    // waterMaterial / coralGlowMaterial / seaweedGlowMaterial 은 인스턴스가 아니라 **프로젝트 에셋**이다.
    // 런타임에 SetColor 하면 .mat 파일이 그대로 바뀌어, 밤에 한 번 플레이하면
    // 에디터로 돌아와도 물색이 밤 그대로 남고 소스 관리에도 변경으로 잡힌다.
    // 그래서 시작할 때 원래 값을 적어 두고 끝날 때 되돌린다.
    struct MatSnapshot
    {
        public Material mat;
        public Color shallow, deep, emission;
        public float shaft;
        public bool hasShallow, hasDeep, hasShaft, hasEmission;
    }
    MatSnapshot[] snapshots;

    void Reset() { keys = DefaultKeys(); }

    void Awake()
    {
        if (keys == null || keys.Length < 2) keys = DefaultKeys();
        if (sun == null) sun = GetComponent<Light>();
        TakeSnapshots();
        Apply(CurrentHour());
    }

    void OnDisable() { RestoreSnapshots(); }

    void TakeSnapshots()
    {
        var list = new System.Collections.Generic.List<MatSnapshot>(3);
        foreach (var m in new[] { waterMaterial, coralGlowMaterial, seaweedGlowMaterial })
        {
            if (m == null) continue;
            var s = new MatSnapshot { mat = m };
            if (m.HasProperty(ShallowId)) { s.hasShallow = true; s.shallow = m.GetColor(ShallowId); }
            if (m.HasProperty(DeepId)) { s.hasDeep = true; s.deep = m.GetColor(DeepId); }
            if (m.HasProperty(ShaftId)) { s.hasShaft = true; s.shaft = m.GetFloat(ShaftId); }
            if (m.HasProperty(EmissionId)) { s.hasEmission = true; s.emission = m.GetColor(EmissionId); }
            list.Add(s);
        }
        snapshots = list.ToArray();
    }

    void RestoreSnapshots()
    {
        if (snapshots == null) return;
        foreach (var s in snapshots)
        {
            if (s.mat == null) continue;
            if (s.hasShallow) s.mat.SetColor(ShallowId, s.shallow);
            if (s.hasDeep) s.mat.SetColor(DeepId, s.deep);
            if (s.hasShaft) s.mat.SetFloat(ShaftId, s.shaft);
            if (s.hasEmission) s.mat.SetColor(EmissionId, s.emission);
        }
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < Mathf.Max(0.25f, updateInterval)) return;
        timer = 0f;
        Apply(CurrentHour());
    }

    public float CurrentHour()
    {
        if (!useSystemClock) return Mathf.Repeat(overrideHour, 24f);
        DateTime n = DateTime.Now;
        return n.Hour + n.Minute / 60f + n.Second / 3600f;
    }

    /// <summary>지금 시각을 감싸는 두 키를 찾아 보간한다. 24시를 넘어 0시로 이어진다.</summary>
    void Apply(float hour)
    {
        if (keys == null || keys.Length == 0) return;

        int a = keys.Length - 1;
        int b = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].hour > hour) { b = i; a = (i - 1 + keys.Length) % keys.Length; break; }
            if (i == keys.Length - 1) { a = i; b = 0; }
        }

        float ha = keys[a].hour;
        float hb = keys[b].hour;
        float span = hb - ha;
        if (span <= 0f) span += 24f;            // 자정을 넘어가는 구간
        float pos = hour - ha;
        if (pos < 0f) pos += 24f;
        float t = span > 0.0001f ? Mathf.Clamp01(pos / span) : 0f;
        t = Mathf.SmoothStep(0f, 1f, t);        // 급격한 전환을 막는다

        Key k = keys[a], m = keys[b];

        if (sun != null)
        {
            sun.color = Color.Lerp(k.sunColor, m.sunColor, t);
            sun.intensity = Mathf.Lerp(k.sunIntensity, m.sunIntensity, t);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(k.ambientSky, m.ambientSky, t);
        RenderSettings.ambientEquatorColor = Color.Lerp(k.ambientEquator, m.ambientEquator, t);
        RenderSettings.ambientGroundColor = Color.Lerp(k.ambientGround, m.ambientGround, t);

        RenderSettings.fog = true;
        RenderSettings.fogColor = Color.Lerp(k.fogColor, m.fogColor, t);
        RenderSettings.fogDensity = Mathf.Lerp(k.fogDensity, m.fogDensity, t);

        if (waterMaterial != null)
        {
            if (waterMaterial.HasProperty(ShallowId))
                waterMaterial.SetColor(ShallowId, Color.Lerp(k.waterShallow, m.waterShallow, t));
            if (waterMaterial.HasProperty(DeepId))
                waterMaterial.SetColor(DeepId, Color.Lerp(k.waterDeep, m.waterDeep, t));
            if (waterMaterial.HasProperty(ShaftId))
                waterMaterial.SetFloat(ShaftId, Mathf.Lerp(k.shaftStrength, m.shaftStrength, t));
        }

        float glow = Mathf.Lerp(k.coralGlow, m.coralGlow, t);
        // 발광은 HDR 이라 1을 넘겨야 블룸이 걸려 "빛난다"고 읽힌다.
        if (coralGlowMaterial != null && coralGlowMaterial.HasProperty(EmissionId))
            coralGlowMaterial.SetColor(EmissionId, coralGlowTint * (glow * 2.2f));
        if (seaweedGlowMaterial != null && seaweedGlowMaterial.HasProperty(EmissionId))
            seaweedGlowMaterial.SetColor(EmissionId, seaweedGlowTint * (glow * 1.8f));
    }

    /// <summary>기본 시간대 프리셋. 수조 안이라 한낮에도 완전히 밝지는 않다.</summary>
    public static Key[] DefaultKeys()
    {
        return new[]
        {
            // 새벽 — 아직 푸르고 어둡다
            new Key { hour = 5f,
                sunColor = new Color(0.45f, 0.62f, 0.85f), sunIntensity = 0.45f,
                ambientSky = new Color(0.07f, 0.16f, 0.26f), ambientEquator = new Color(0.04f, 0.10f, 0.18f), ambientGround = new Color(0.01f, 0.04f, 0.07f),
                fogColor = new Color(0.03f, 0.11f, 0.20f), fogDensity = 0.055f,
                waterShallow = new Color(0.07f, 0.26f, 0.40f), waterDeep = new Color(0.01f, 0.07f, 0.15f),
                shaftStrength = 0.35f, coralGlow = 0.55f },

            // 아침 — 안쪽까지 환해진다
            new Key { hour = 8f,
                sunColor = new Color(0.85f, 0.92f, 1.00f), sunIntensity = 1.25f,
                ambientSky = new Color(0.20f, 0.46f, 0.62f), ambientEquator = new Color(0.10f, 0.28f, 0.42f), ambientGround = new Color(0.03f, 0.10f, 0.17f),
                fogColor = new Color(0.06f, 0.24f, 0.38f), fogDensity = 0.028f,
                waterShallow = new Color(0.16f, 0.52f, 0.66f), waterDeep = new Color(0.02f, 0.16f, 0.30f),
                shaftStrength = 1.15f, coralGlow = 0.10f },

            // 한낮 — 가장 밝고 빛기둥이 선명하다
            new Key { hour = 13f,
                sunColor = new Color(0.92f, 0.97f, 1.00f), sunIntensity = 1.45f,
                ambientSky = new Color(0.24f, 0.52f, 0.68f), ambientEquator = new Color(0.12f, 0.32f, 0.47f), ambientGround = new Color(0.04f, 0.12f, 0.19f),
                fogColor = new Color(0.07f, 0.27f, 0.42f), fogDensity = 0.022f,
                waterShallow = new Color(0.20f, 0.58f, 0.72f), waterDeep = new Color(0.02f, 0.18f, 0.34f),
                shaftStrength = 1.35f, coralGlow = 0.05f },

            // 오후 — 따뜻하게 기운다
            new Key { hour = 17f,
                sunColor = new Color(1.00f, 0.87f, 0.72f), sunIntensity = 1.00f,
                ambientSky = new Color(0.20f, 0.42f, 0.55f), ambientEquator = new Color(0.11f, 0.26f, 0.38f), ambientGround = new Color(0.03f, 0.10f, 0.16f),
                fogColor = new Color(0.07f, 0.23f, 0.36f), fogDensity = 0.030f,
                waterShallow = new Color(0.22f, 0.50f, 0.62f), waterDeep = new Color(0.02f, 0.14f, 0.27f),
                shaftStrength = 1.00f, coralGlow = 0.18f },

            // 저녁 — 가라앉고 산호가 살아난다
            new Key { hour = 20f,
                sunColor = new Color(0.72f, 0.60f, 0.72f), sunIntensity = 0.55f,
                ambientSky = new Color(0.10f, 0.20f, 0.32f), ambientEquator = new Color(0.05f, 0.13f, 0.22f), ambientGround = new Color(0.02f, 0.05f, 0.10f),
                fogColor = new Color(0.04f, 0.13f, 0.24f), fogDensity = 0.045f,
                waterShallow = new Color(0.10f, 0.30f, 0.44f), waterDeep = new Color(0.01f, 0.08f, 0.18f),
                shaftStrength = 0.50f, coralGlow = 0.65f },

            // 밤 — 가장 어둡고 산호 발광이 주광원처럼 보인다
            new Key { hour = 23f,
                sunColor = new Color(0.35f, 0.48f, 0.75f), sunIntensity = 0.28f,
                ambientSky = new Color(0.04f, 0.10f, 0.20f), ambientEquator = new Color(0.02f, 0.06f, 0.13f), ambientGround = new Color(0.01f, 0.02f, 0.05f),
                fogColor = new Color(0.02f, 0.07f, 0.15f), fogDensity = 0.065f,
                waterShallow = new Color(0.04f, 0.17f, 0.30f), waterDeep = new Color(0.00f, 0.04f, 0.11f),
                shaftStrength = 0.22f, coralGlow = 1.00f },
        };
    }
}
