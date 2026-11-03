using UnityEngine;

/// <summary>
/// 로비의 시간 흐름 — 로비에 머무는 동안 해 질 녘이 조금씩 깊어진다. 엔딩의 밤 로비(LobbyNight)와 대비가 남도록 "살짝"만:
/// 햇빛이 붉고 약해지고, 하늘이 조금 붉고 어두워지고, 실내 주변광이 따뜻하게 낮아진다. 등대 등명기가 먼저 은은하게 켜지고,
/// 거의 다 저물면 빛줄기가 돌기 시작한다 (Lighthouse.dusk).
///
/// 시간은 로비에 있는 동안만 흐른다 — 엘리베이터 · 돔에 있는 동안은 멈춘다(로비 묶음이 꺼지면 Update 도 멈춘다).
/// 로비의 기준 조명값(LobbySequence.sunColor · ambient*)을 바꿔 두므로 수심 벽 미리보기(DepthWallPreview)와 엘리베이터 칸 조명이
/// 끝나고 돌아올 때도 지금 시각의 빛으로 돌아온다. 하늘 재질은 에셋을 건드리지 않게 실행 중에만 쓰는 복사본으로 바꿔 끼운다.
/// </summary>
public sealed class LobbyDusk : MonoBehaviour
{
    public LobbySequence lobby;
    public DepthWallPreview wall;
    public Lighthouse lighthouse;

    [Header("빠르기")]
    public float minutes = 6f;                           // 로비에 이만큼 있으면 다 저문다 — 보통은 그 전에 엘리베이터를 탄다

    [Header("다 저물었을 때 (처음 값에 곱하거나 섞는다)")]
    public Color sunColor = new Color(1.00f, 0.60f, 0.40f);
    [Range(0f, 1f)] public float sunLevel = 0.65f;
    public Color ambientTint = new Color(1.00f, 0.86f, 0.78f);
    [Range(0f, 1f)] public float ambientLevel = 0.8f;
    public Color skyTint = new Color(0.60f, 0.45f, 0.40f, 0.5f);
    [Range(0f, 1f)] public float skyLevel = 0.7f;

    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int ExposureId = Shader.PropertyToID("_Exposure");

    Color sun0, sky0, equator0, ground0, tint0;
    float sunLevel0, exposure0;
    Material skyCopy;
    float elapsed;
    float shown = -1f;

    public float Progress => minutes > 0f ? Mathf.Clamp01(elapsed / (minutes * 60f)) : 1f;

    void Awake()
    {
        if (lobby == null) { enabled = false; return; }
        sun0 = lobby.sunColor;
        sunLevel0 = lobby.sunIntensity;
        sky0 = lobby.ambientSky;
        equator0 = lobby.ambientEquator;
        ground0 = lobby.ambientGround;
        // 다른 스크립트의 Start 가 하늘을 깔기 전에 실행 중 복사본으로 바꿔 둔다 — 수심 벽 · 밤 로비도 이 복사본에서 시작한다.
        if (lobby.lobbySkybox != null)
        {
            skyCopy = new Material(lobby.lobbySkybox) { name = lobby.lobbySkybox.name + " (dusk)", hideFlags = HideFlags.DontSave };
            tint0 = skyCopy.HasProperty(TintId) ? skyCopy.GetColor(TintId) : Color.gray;
            exposure0 = skyCopy.HasProperty(ExposureId) ? skyCopy.GetFloat(ExposureId) : 1f;
            lobby.lobbySkybox = skyCopy;
        }
    }

    void OnDestroy()
    {
        if (skyCopy != null) Destroy(skyCopy);
    }

    void Update()
    {
        if (lobby == null || lobby.IsNight) return;
        if (lobby.AcceptsActivity) elapsed += Time.deltaTime;

        float k = Mathf.SmoothStep(0f, 1f, Progress);
        if (Mathf.Abs(k - shown) < 0.002f) return;       // 6분에 걸친 변화 — 매 프레임 칠할 필요가 없다
        shown = k;

        lobby.sunColor = Color.Lerp(sun0, sunColor, k);
        lobby.sunIntensity = sunLevel0 * Mathf.Lerp(1f, sunLevel, k);
        float level = Mathf.Lerp(1f, ambientLevel, k);
        Color tint = Color.Lerp(Color.white, ambientTint, k) * level;
        lobby.ambientSky = sky0 * tint;
        lobby.ambientEquator = equator0 * tint;
        lobby.ambientGround = ground0 * tint;
        if (skyCopy != null)
        {
            if (skyCopy.HasProperty(TintId)) skyCopy.SetColor(TintId, Color.Lerp(tint0, skyTint, k));
            if (skyCopy.HasProperty(ExposureId)) skyCopy.SetFloat(ExposureId, exposure0 * Mathf.Lerp(1f, skyLevel, k));
        }
        if (lighthouse != null) lighthouse.dusk = k;

        // 실제 빛은 로비에 있을 때만 칠한다 — 수심 벽 미리보기 중이면 그쪽이 끝날 때 새 기준값으로 돌아온다.
        if (!lobby.AcceptsActivity || (wall != null && wall.Busy)) return;
        RenderSettings.ambientSkyColor = lobby.ambientSky;
        RenderSettings.ambientEquatorColor = lobby.ambientEquator;
        RenderSettings.ambientGroundColor = lobby.ambientGround;
        Light sun = lobby.Sun;
        if (sun != null)
        {
            sun.color = lobby.sunColor;
            sun.intensity = lobby.sunIntensity;
        }
    }

#if UNITY_EDITOR
    /// <summary>헤드셋 없이 확인할 때 — 시계를 0~1 사이 아무 곳으로 옮긴다.</summary>
    public void DebugSet(float progress)
    {
        elapsed = Mathf.Clamp01(progress) * minutes * 60f;
        shown = -1f;
    }
#endif
}
