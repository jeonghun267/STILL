using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 눈앞에 두고 보는 물건(미스터리 박스, 뽑힌 물고기, 도감 관찰 모델)이 밤에 배경과 함께 까맣게
/// 묻히지 않도록, 자기 텍스처 색으로 은은히 발광시킨다. TimeOfDayController.NightFactor 에 비례해
/// 세지고 한낮에는 거의 0 이라 낮 모습은 그대로다.
///
/// 왜 라이트가 아니라 발광인가: Android(Low) 품질이 쓰는 Performance URP 설정은 추가 라이트가
/// 꺼져 있어 포인트 라이트를 붙여도 아무것도 비추지 않는다. 발광은 파이프라인 설정과 무관하고
/// 드로우콜도 늘지 않는다.
///
/// 공유 머티리얼(프로젝트 에셋)은 절대 건드리지 않는다 — 렌더러마다 인스턴스를 만들어 _EMISSION 을
/// 켜고, 파괴될 때 원본을 되돌린 뒤 인스턴스를 지운다. (TimeOfDayController 의 에셋 오염 교훈과 같은 이유)
/// </summary>
public class ShowcaseGlow : MonoBehaviour
{
    [Tooltip("시간대와 무관하게 항상 더하는 발광(알베도 배수). 도감 관찰창처럼 늘 또렷해야 할 때만 0 보다 크게")]
    [Min(0f)] public float baseGlow = 0f;
    [Tooltip("한밤(NightFactor=1)에 더해지는 발광(알베도 배수). 밝은 텍스처(물고기)는 0.5 안팎이면 음영이 남고, " +
             "어두운 나무 텍스처(상자)는 1.3 안팎이어야 밤 물빛보다 밝게 읽힌다")]
    [Min(0f)] public float nightGlow = 0.8f;
    [Min(0.1f)] public float updateInterval = 0.5f;

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    const string EmissionKeyword = "_EMISSION";

    // Linear 프로젝트에서 Material.SetColor 는 sRGB 값을 받아 업로드 때 선형화한다(GetColor 도 sRGB 를 돌려준다).
    // 그래서 sRGB 값에 k 를 곱하면 GPU 에서는 k^2.2 배가 된다 — 0.15 를 주면 실제로는 0.015.
    // 선형에서 곱하고 sRGB 로 되돌려 넘겨야 "발광 = 알베도 × k" 가 그대로 성립한다.
    static readonly bool LinearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;

    struct Entry
    {
        public Material mat;
        public Color tintLinear;    // _BaseColor(선형) — 발광이 원래 색조를 그대로 따르게 한다
    }

    struct Original
    {
        public Renderer renderer;
        public Material[] shared;   // 인스턴스로 바꾸기 전의 에셋 참조 — Release 때 되돌린다
    }

    readonly List<Entry> entries = new List<Entry>();
    readonly List<Original> originals = new List<Original>();
    readonly List<Material> owned = new List<Material>();   // 우리가 만든 인스턴스 전부 (파괴용)
    float timer;
    float lastApplied = -1f;
    bool built;

    /// <summary>런타임에 생성한 모델에 붙일 때 쓴다. 붙이는 즉시 값이 반영된다.</summary>
    public static ShowcaseGlow Attach(GameObject target, float baseGlow, float nightGlow)
    {
        if (target == null) return null;
        ShowcaseGlow glow = target.GetComponent<ShowcaseGlow>();
        if (glow == null) glow = target.AddComponent<ShowcaseGlow>();
        glow.Configure(baseGlow, nightGlow);
        return glow;
    }

    public void Configure(float baseGlow, float nightGlow)
    {
        this.baseGlow = Mathf.Max(0f, baseGlow);
        this.nightGlow = Mathf.Max(0f, nightGlow);
        lastApplied = -1f;
        if (isActiveAndEnabled) Tick();
    }

    void OnEnable()
    {
        if (!built) Build();
        lastApplied = -1f;      // 비활성 사이에 시간대가 바뀌었을 수 있다
        timer = 0f;
        Tick();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;
        Tick();
    }

    void OnDestroy() { Release(); }

    /// <summary>자식 렌더러가 바뀌었을 때(파츠 교체 등) 다시 훑는다.</summary>
    public void Rebuild()
    {
        Release();
        Build();
        lastApplied = -1f;
        Tick();
    }

    void Build()
    {
        built = true;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            // 파티클/트레일은 제외 — 메시만 다룬다. 부서진 조각처럼 비활성인 파츠도 포함한다.
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

            Material[] shared = renderer.sharedMaterials;
            bool any = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (Supports(shared[i])) { any = true; break; }
            }
            if (!any) continue;

            originals.Add(new Original { renderer = renderer, shared = shared });

            // renderer.materials 는 슬롯 전체를 인스턴스로 바꾼다. 지원 여부와 상관없이 전부 owned 에 넣어
            // 파괴 시 하나도 새지 않게 한다.
            Material[] mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                owned.Add(m);
                if (!Supports(m)) continue;

                // 자기 베이스맵을 발광맵으로 물린다 → "자기 색으로 빛나는" 효과. 색조도 _BaseColor 를 따른다.
                m.EnableKeyword(EmissionKeyword);
                // URP Lit 인스펙터는 키워드가 아니라 GI 플래그를 기준으로 _EMISSION 을 다시 맞춘다.
                // 물고기 머티리얼은 EmissiveIsBlack 이라, 플레이 중 인스펙터에서 물고기를 클릭하면 키워드가
                // 벗겨져 도로 까매진다. 발광 체크박스가 켜졌을 때와 같은 플래그로 맞춰 둔다.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetTexture(EmissionMapId, m.GetTexture(BaseMapId));
                Color tint = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                tint.a = 1f;
                entries.Add(new Entry { mat = m, tintLinear = LinearSpace ? tint.linear : tint });
            }
        }
    }

    static bool Supports(Material m)
    {
        return m != null
            && m.HasProperty(EmissionColorId)
            && m.HasProperty(EmissionMapId)
            && m.HasProperty(BaseMapId);
    }

    void Release()
    {
        entries.Clear();

        // 렌더러가 아직 살아 있으면(컴포넌트만 떼거나 Rebuild) 인스턴스를 지우기 전에 원본부터 돌려준다.
        // 안 그러면 렌더러가 파괴된 머티리얼을 물고 분홍색으로 남는다.
        for (int i = 0; i < originals.Count; i++)
        {
            Original o = originals[i];
            if (o.renderer != null) o.renderer.sharedMaterials = o.shared;
        }
        originals.Clear();

        for (int i = 0; i < owned.Count; i++)
        {
            Material m = owned[i];
            if (m == null) continue;
            if (Application.isPlaying) Destroy(m);
            else DestroyImmediate(m);
        }
        owned.Clear();
        built = false;
    }

    void Tick()
    {
        TimeOfDayController tod = TimeOfDayController.Instance;
        float night = tod != null ? tod.NightFactor : 0f;
        float k = baseGlow + nightGlow * night;
        if (Mathf.Abs(k - lastApplied) < 0.002f) return;
        lastApplied = k;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.mat == null) continue;
            Color c = e.tintLinear * k;
            e.mat.SetColor(EmissionColorId, LinearSpace ? c.gamma : c);
        }
    }
}
