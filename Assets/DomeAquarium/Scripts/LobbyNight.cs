using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 엔딩의 밤 로비 (기획 문서 "상승과 마무리"). 해 질 녘에 내려간 관측소에 밤이 되어 올라온다.
///
///   하늘   창밖 사진 하늘을 어둡고 푸르게(노출 0.1), 별과 달을 띄운다
///   실내   천장 네온은 그대로 켜고 주변광을 따뜻하게 낮춘다 — 창밖은 캄캄하고 안은 불이 켜진 관측소
///   바다   하늘 반사를 줄여 검푸른 밤바다
///   벽     데이터 월(71%·80%)에 오늘 밤의 새 줄 — "+1 처음 관측된 생물 · 최초 관측자"
///
/// 조명 값은 로비 수심 벽(DepthWallPreview)이 이미 쥐고 있는 네온·스캔 모델·반사 프로브를 함께 쓴다.
/// 낮 로비로 되돌리지 않는다 — 엔딩은 이 상태로 크레딧까지 간다.
/// </summary>
public sealed class LobbyNight : MonoBehaviour
{
    public LobbySequence lobby;
    public DepthWallPreview wall;              // 천장 네온·스캔 모델·하늘 반사 프로브 목록을 빌린다

    [Header("밤 빛")]
    public Color ambientSky = new Color(0.20f, 0.22f, 0.32f);
    public Color ambientEquator = new Color(0.26f, 0.22f, 0.18f);   // 네온 아래 따뜻한 실내
    public Color ambientGround = new Color(0.22f, 0.19f, 0.16f);
    public Color moonColor = new Color(0.55f, 0.65f, 0.95f);
    public float moonIntensity = 0.18f;
    public Color skyTint = new Color(0.20f, 0.24f, 0.40f, 0.5f);
    public float skyExposure = 0.10f;
    [Range(0f, 2f)] public float lampLevel = 1.1f;
    [Range(0f, 1f)] public float reflectionLevel = 0.15f;
    public Color scanTint = new Color(0.55f, 0.55f, 0.62f);

    [Header("밤하늘")]
    public GameObject sky;                     // 별·달 묶음. 낮에는 꺼 둔다

    [Header("데이터 월의 새 줄")]
    public CanvasGroup record;
    public TMP_Text recordTitle;
    public TMP_Text recordDetail;
    public AudioSource chime;                  // 새 줄이 생길 때 한 번
    public AudioClip chimeClip;
    public float revealSeconds = 1.6f;

    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    Material nightSky;
    MaterialPropertyBlock block;
    public bool IsNight { get; private set; }

    void Awake()
    {
        if (sky != null) sky.SetActive(false);
        if (record != null) record.alpha = 0f;
    }

    void OnDestroy()
    {
        if (nightSky != null) Destroy(nightSky);
    }

    /// <summary>화면이 검은 동안 LobbySequence 가 부른다. 낮 로비 환경을 먼저 칠한 다음 그 위에 밤을 덮는다.</summary>
    public void Apply()
    {
        IsNight = true;
        if (block == null) block = new MaterialPropertyBlock();

        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;

        Light sun = lobby != null ? lobby.Sun : null;
        if (sun != null)
        {
            sun.color = moonColor;
            sun.intensity = moonIntensity;
            sun.cookie = null;
        }

        if (lobby != null && lobby.lobbySkybox != null)
        {
            if (nightSky == null) nightSky = new Material(lobby.lobbySkybox) { name = "LobbySky (night)", hideFlags = HideFlags.DontSave };
            if (nightSky.HasProperty(TintId)) nightSky.SetColor(TintId, skyTint);
            if (nightSky.HasProperty(ExposureId)) nightSky.SetFloat(ExposureId, skyExposure);
            RenderSettings.skybox = nightSky;
        }

        if (wall != null)
        {
            if (wall.lamps != null)
                for (int i = 0; i < wall.lamps.Length; i++)
                {
                    Renderer r = wall.lamps[i];
                    if (r == null) continue;
                    int slot = wall.lampSlots != null && i < wall.lampSlots.Length ? wall.lampSlots[i] : 0;
                    Material[] mats = r.sharedMaterials;
                    if (slot < 0 || slot >= mats.Length || mats[slot] == null || !mats[slot].HasProperty(EmissionId)) continue;
                    r.GetPropertyBlock(block, slot);
                    block.SetColor(EmissionId, mats[slot].GetColor(EmissionId) * lampLevel);
                    r.SetPropertyBlock(block, slot);
                }
            if (wall.skyProbes != null)
                foreach (var probe in wall.skyProbes)
                    if (probe != null) probe.intensity = reflectionLevel;
            if (wall.unlitScans != null)
            {
                int colorId = Shader.PropertyToID(wall.scanColorProperty);
                foreach (var r in wall.unlitScans)
                {
                    if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(colorId)) continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(colorId, r.sharedMaterial.GetColor(colorId) * scanTint);
                    r.SetPropertyBlock(block);
                }
            }
        }
        if (sky != null) sky.SetActive(true);
    }

    /// <summary>데이터 월에 오늘 밤의 새 줄을 띄운다 — 천천히 밝아지며 한 번 빛난다.</summary>
    public void RevealRecord(string title, string detail)
    {
        if (recordTitle != null) recordTitle.text = title;
        if (recordDetail != null) recordDetail.text = detail;
        if (record != null) StartCoroutine(Reveal());
        if (chime != null && chimeClip != null) chime.PlayOneShot(chimeClip);
    }

    IEnumerator Reveal()
    {
        Color baseColor = recordTitle != null ? recordTitle.color : Color.white;
        for (float t = 0f; t < revealSeconds; t += Time.deltaTime)
        {
            float k = t / revealSeconds;
            record.alpha = Mathf.SmoothStep(0f, 1f, k);
            // 처음에 흰빛으로 번쩍였다가 제 색으로 가라앉는다 — "방금 새겨진" 느낌
            if (recordTitle != null) recordTitle.color = Color.Lerp(Color.white, baseColor, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        record.alpha = 1f;
        if (recordTitle != null) recordTitle.color = baseColor;
    }
}
