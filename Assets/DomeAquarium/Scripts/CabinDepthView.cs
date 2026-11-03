using TMPro;
using UnityEngine;

/// <summary>
/// 엘리베이터 칸에서 "내려가고 있다"를 숫자 말고도 느끼게 하는 두 가지 (계기판 수심 LobbySequence.CabinMeters 를 따라간다).
///
///  - 작은 둥근 창: 창 너머 물빛이 깊이만큼 어두워지고(0m 옥색 → 24m 청록 → 200m 남색 → 1,000m 검정), 바다눈 알갱이가
///    내려갈 때는 위로, 올라갈 때는 아래로 흘러간다. 실제로 몸이 움직이지 않는 칸이라 창은 작게 — 시야 바깥의 흐름이 멀미를 부르지 않게.
///  - 수압 컵: 스티로폼 컵은 공기 방울이 눌려 깊이 내려갈수록 쪼그라들고, 올라와도 작아진 채로 남는다 — 지금까지 가장 깊이 간 곳만큼.
///    옆 표에 지금 수압(1 + 수심/10 기압)을 적는다.
/// </summary>
public sealed class CabinDepthView : MonoBehaviour
{
    public LobbySequence lobby;

    [Header("창")]
    public Renderer water;                              // 창 너머 물빛 (불투명)
    public Renderer snow;                               // 바다눈 알갱이 (가산, 이음매 없는 무늬)
    public Gradient waterByDepth;                       // 0~1 = 로그 눈금 0m ~ 1,000m
    public float snowScroll = 0.012f;                   // 1 m/s 에 무늬가 흐르는 빠르기 (타일/초)

    [Header("수압 컵")]
    public Transform cup;
    public float cupSmallest = 0.55f;                   // 1,000m 에서 남는 크기
    public float cupDepthScale = 300f;                  // 이 깊이(m)쯤에서 크게 줄어든다 — 그 아래는 조금씩
    public TMP_Text pressure;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseStId = Shader.PropertyToID("_BaseMap_ST");
    MaterialPropertyBlock waterBlock, snowBlock;
    Vector4 snowSt = new Vector4(1f, 1f, 0f, 0f);
    Vector3 cupScale;
    float lastMeters = -1f, deepest;
    int shownBar = -1;

    void Awake()
    {
        waterBlock = new MaterialPropertyBlock();
        snowBlock = new MaterialPropertyBlock();
        if (snow != null && snow.sharedMaterial != null && snow.sharedMaterial.HasProperty(BaseStId)) snowSt = snow.sharedMaterial.GetVector(BaseStId);
        if (cup != null) cupScale = cup.localScale;
    }

    void Update()
    {
        if (lobby == null) return;
        float meters = Mathf.Max(0f, lobby.CabinMeters);
        float speed = lastMeters < 0f || Time.deltaTime <= 0f ? 0f : (meters - lastMeters) / Time.deltaTime;   // 내려가면 +
        lastMeters = meters;

        float k = Mathf.Log10(1f + meters) / 3f;          // 0m 0 · 24m 0.46 · 200m 0.77 · 1,000m 1
        if (water != null && waterByDepth != null)
        {
            water.GetPropertyBlock(waterBlock);
            waterBlock.SetColor(BaseColorId, waterByDepth.Evaluate(Mathf.Clamp01(k)));
            water.SetPropertyBlock(waterBlock);
        }
        if (snow != null)
        {
            // 내려갈 때 알갱이는 위로 지나간다 — 무늬를 아래로 흘려 보낸다(UV 를 위로)
            snowSt.w = Mathf.Repeat(snowSt.w - speed * snowScroll * Time.deltaTime, 1f);
            snow.GetPropertyBlock(snowBlock);
            snowBlock.SetVector(BaseStId, snowSt);
            float density = Mathf.Lerp(0.25f, 0.9f, Mathf.Clamp01(k * 1.4f));   // 빛이 사라지는 깊이에서 바다눈이 또렷하다
            snowBlock.SetColor(BaseColorId, new Color(0.75f * density, 0.85f * density, 0.9f * density, density));
            snow.SetPropertyBlock(snowBlock);
        }

        deepest = Mathf.Max(deepest, meters);
        if (cup != null)
        {
            float s = Mathf.Lerp(1f, cupSmallest, 1f - Mathf.Exp(-deepest / cupDepthScale));
            cup.localScale = cupScale * s;
        }
        if (pressure != null)
        {
            int bar = Mathf.RoundToInt(1f + meters / 10f);
            if (bar != shownBar)
            {
                shownBar = bar;
                pressure.text = $"수압  {bar:#,0} 기압";
            }
        }
    }
}
