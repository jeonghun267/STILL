using UnityEngine;

/// <summary>
/// 밤 이야기 ③ 선장의 밤바다에 나오는 신호의 주인 — 1,000m 에서 만난 UnknownCreature 와 같은 모습 · 같은 크기(SignalCreatureLook).
/// 물 위에서는 등만 내밀고(몸 가운데가 수면 아래) 수면에 빛이 번진다(waterGlow) — 다 떠오르면 늘어진 실 두 가닥이 다리처럼 보였다.
/// 상태 기계 없이 이야기가 밝기(glow)와 깜빡임(blinking · Pulse)을 정한다. 제자리에서 천천히 돌며 옆모습을 보이고, 실 두 가닥이 늘어진다.
/// 재질 · 크기는 세울 때 장면의 UnknownCreature 에서 그대로 옮긴다(DomeAquariumNightStories.BuildPuppet).
/// </summary>
public sealed class SignalCreaturePuppet : MonoBehaviour
{
    [Header("모습 (UnknownCreature 와 같게)")]
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

    [Header("움직임")]
    public float blinkPeriod = 4.5f;
    public float turnSpeed = 9f;                // °/s — 가까이서 옆모습을 보이며 천천히 돈다

    [Header("수면 (물 위로 떠오를 때)")]
    public Renderer waterGlow;                  // 수면에 눕힌 빛 번짐 — 세울 때의 높이가 수면. 몸이 수면에 가까울수록 밝다
    public float waterGlowStrength = 2f;        // 가산 판이라 알파도 함께 곱해진다 — 0.8 은 밤바다에서 거의 안 보였다

    [Header("이야기가 정하는 값")]
    [Range(0f, 1.5f)] public float glow;        // 숨 쉬듯 일정한 밝기
    public bool blinking;                       // 세 번씩 깜빡임 — 1,000m 에서 하던 신호

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    SignalCreatureLook look;
    Transform view;
    float current, pulse, spin, waterY;
    bool shown;
    MaterialPropertyBlock block;

    public bool Shown => shown;

    void Awake()
    {
        look = new SignalCreatureLook(transform, new SignalCreatureLook.Style
        {
            body = bodyMaterial, core = coreMaterial, bands = bandMaterial, halo = haloMaterial, filament = filamentMaterial,
            radii = bodyRadii, bandCount = bandCount, filamentLength = filamentLength, haloSize = haloSize, haloColor = haloColor,
        });
        if (waterGlow != null) waterY = waterGlow.transform.position.y;
        spin = Random.Range(0f, 360f);
        Show(false);
    }

    public void Show(bool on)
    {
        shown = on;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        if (!on) { current = 0f; pulse = 0f; }
    }

    /// <summary>한 번 번쩍 — 0.7초.</summary>
    public void Pulse() => pulse = 1f;

    void Update()
    {
        if (!shown) return;
        float dt = Time.deltaTime;
        float target = blinking ? 0.15f + SignalCreatureLook.Blink(Time.time, blinkPeriod) * 0.9f : glow;
        pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.7f);
        current = Mathf.MoveTowards(current, Mathf.Max(target, Mathf.Sin(pulse * Mathf.PI * 0.5f) * 1.1f), dt * 4f);
        spin += turnSpeed * dt;
        transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.3f) * 10f, spin, 0f);
        look.Apply(current, true);
        look.UpdateFilaments();
    }

    void LateUpdate()
    {
        if (!shown) return;
        if (waterGlow != null)
        {
            // 몸은 돌고 기울지만 수면의 빛은 늘 수면에 눕혀 몸 바로 위에 — 깊을수록(떠오르는 중) 약하다
            Vector3 p = transform.position;
            waterGlow.transform.SetPositionAndRotation(new Vector3(p.x, waterY, p.z), Quaternion.Euler(90f, 0f, 0f));
            float near = Mathf.Clamp01(1f - (waterY - p.y - 0.2f) / 2f);
            block ??= new MaterialPropertyBlock();
            waterGlow.GetPropertyBlock(block);
            block.SetColor(BaseColorId, haloColor * (current * near * waterGlowStrength));
            waterGlow.SetPropertyBlock(block);
        }
        if (view == null) { var cam = Camera.main; if (cam == null) return; view = cam.transform; }
        look.FaceHalo(view);
    }
}
