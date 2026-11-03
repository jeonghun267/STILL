using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 밤 로비 이야기 ④ 비브의 바시스피어. 1934년 8월 버뮤다 앞바다 — 지름 1.45m 쇠공 안에서 923m 까지 내려간다.
///   손바닥만 한 석영 창 하나와 창밖 탐조등 하나로 본다. 내려갈수록 물빛이 파랑 → 검정이 되고, 바다눈이 위로 흘러간다.
///   수면(글로리아 홀리스터)이 전화로 묻는다 — "창밖에 무엇이 보이나요?" 보이는 것을 골라 알린다.
///   창밖 생물은 프로젝트의 심해 생물 모델: 300m 손도끼고기 무리 · 450m 독사고기 · 580m 다섯 줄 별자리 물고기 · 700m 흡혈오징어 · 923m 초롱아귀.
///   다섯 줄 별자리 물고기(Bathysidus pentagrammus)는 비브가 1934년 8월 11일 약 580m 에서 보고 그 뒤로 아무도 다시 보지 못한 물고기 —
///   나비고기 친척 같은 납작한 몸에 노란 빛점 다섯 줄, 빛점마다 보랏빛 테. 빗해파리를 잘못 본 것이라는 설이 있어 오늘의 신호의 주인과 겹친다.
///   탐조등은 Quest 에서 추가 조명이 꺼져 있어, 관찰기 조명처럼 심해 생물 셰이더 전역 값(_DeviceLight*)으로 비춘다.
/// 쇠공 · 창밖 물(안쪽 면 구)은 다른 무엇도 보이지 않는 먼 곳(빌더)에 두고, 도착하면 쇠공 가운데를 눈높이에 맞춘다(Arrived).
/// </summary>
[DefaultExecutionOrder(200)]                    // 탐조등 전역 값을 관찰기 조명(ObservationLight)보다 나중에 쓴다
public sealed class BathysphereStory : NightStory
{
    [Header("쇠공")]
    public GameObject scene;                    // 쇠공 · 창밖 물 · 생물 — 장면 동안만 켠다
    public Transform sphere;                    // 쇠공 가운데 (+Z 가 창)
    public Renderer water;                      // 창밖 물 — 안쪽 면 구, 깊이에 따라 색
    public ParticleSystem snow;                 // 창밖 바다눈 — 내려가면 위로 흘러간다
    public TMP_Text depthText;
    public Transform needle;                    // 수심계 바늘 (로컬 z 축으로 돈다)
    public float maxDepth = 923f;

    [Header("창밖 생물 (빌더가 모델로 만든다)")]
    public Transform hatchetfish;               // 300m — 은빛 손도끼고기 무리 (배 쪽 발광기)
    public Transform viperfish;                 // 450m — 독사고기
    public Transform constellation;             // 580m — 다섯 줄 별자리 물고기 (Bathysidus pentagrammus)
    public Transform vampireSquid;              // 700m — 흡혈오징어
    public Transform angler;                    // 923m — 초롱아귀

    [Header("탐조등 (창 바깥, 앞을 비춘다)")]
    public Transform searchlight;
    public float lightRange = 4.5f;
    public float lightAngle = 64f;
    public float lightIntensity = 2.4f;
    public Color lightColor = new Color(1f, 0.9f, 0.75f);           // 1930년대 백열 탐조등

    [Header("전화")]
    public CanvasGroup phonePanel;
    public TMP_Text question;
    public TMP_Text[] choiceLabels;
    public AudioSource phone;
    public AudioClip ring;
    public string surfaceSpeaker = "수면 · 글로리아";
    public string surfaceVoice = "Kore";

    [Header("말")]
    [TextArea(2, 4)] public string introLine =
        "1934년 8월, 버뮤다 앞바다. 윌리엄 비브와 오티스 바턴은 지름 1.45m 쇠공 '바시스피어'에 들어가, 줄 하나에 매달려 내려갔어요. 창은 손바닥만 한 석영 창 하나, 그 바깥에 탐조등 하나뿐이에요.";
    [TextArea(2, 4)] public string callLine = "바시스피어, 들려요? 여기는 수면이에요. 지금부터 내립니다. 창밖에 보이는 걸 전화로 알려 주세요.";
    [TextArea(1, 3)] public string blueLine = "물빛이 점점 짙은 파랑으로 바뀌어요. 빨간빛부터 먼저 사라져요.";
    [TextArea(1, 3)] public string ask300 = "지금 300m예요. 창밖에 무엇이 보이나요?";
    public string[] choices300 = { "작은 빛들이 반짝여요", "아무것도 안 보여요" };
    [TextArea(2, 4)] public string reply300Seen =
        "스스로 빛을 내는 물고기예요! 은빛 손도끼고기 — 배 쪽 발광기로 위에서 내려오는 희미한 빛에 그림자를 지워서, 아래에서 올려다보는 사냥꾼 눈에 잘 띄지 않아요.";
    [TextArea(1, 3)] public string reply300None = "조금만 더 지켜보세요. 깜깜해질수록 빛이 더 잘 보여요.";
    [TextArea(1, 3)] public string viperLine = "독사고기예요. 입에 다 들어가지도 않을 만큼 긴 이빨로, 어둠 속에서 먹이를 붙잡아요.";
    [TextArea(1, 3)] public string ask580 = "지금 580m예요. 창밖에 무엇이 보이나요?";
    public string[] choices580 = { "빛줄 다섯 개가 있는 물고기!", "푸른빛 생물!" };
    [TextArea(2, 4)] public string reply580 =
        "받아 적었어요! 1934년 8월 11일, 비브도 바로 이 깊이에서 노란 빛점이 다섯 줄로 늘어선 물고기를 보고 '다섯 줄 별자리 물고기'라고 이름 붙였어요. 그 뒤로 아무도 다시 보지 못했어요.";
    [TextArea(2, 4)] public string combJellyLine =
        "어떤 과학자들은 비브가 본 게 물고기가 아니라 빗해파리였을 거라고 해요. 오늘 밤 1,000m 에서 만난 {name}처럼, 몸을 따라 빛줄이 흐르는 생물이요.";
    [TextArea(2, 4)] public string darkLine = "이제 햇빛이 거의 닿지 않아요. 비브는 이 어둠을, 지금까지 본 어떤 검정보다 더 검다고 적었어요.";
    [TextArea(2, 4)] public string vampireLine = "흡혈오징어예요. 이름과 달리 피는 먹지 않고, 떠다니는 바다눈을 모아 먹어요. 산소가 아주 적은 이 깊이에서 살아요.";
    [TextArea(1, 3)] public string arriveLine = "8월 15일, 923m — 그때까지 사람이 내려간 가장 깊은 곳이에요! 창밖을 잘 보세요.";
    [TextArea(1, 3)] public string anglerLine = "머리에 등불을 단 아귀예요. 이 어둠에서는 빛이 먹이를 부르는 미끼가 돼요.";
    [TextArea(2, 4)] public string outroLine =
        "90년 전 비브가 그랬던 것처럼, 오늘 당신도 아무도 본 적 없는 생물을 기록했어요. 바다는 아직 그런 이야기로 가득해요.";
    public float waitChoice = 25f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Color[] Gradient =
    {
        new Color(0.10f, 0.45f, 0.55f), new Color(0.04f, 0.22f, 0.40f), new Color(0.015f, 0.07f, 0.18f),
        new Color(0.004f, 0.012f, 0.035f), new Color(0f, 0.002f, 0.008f),
    };
    static readonly float[] GradientDepth = { 0f, 100f, 300f, 600f, 923f };

    MaterialPropertyBlock block;
    float depth;
    int chosen = -1;
    float lastChoice = -99f;

    void Awake()
    {
        if (scene != null) scene.SetActive(false);
        SetPhone(false);
    }

    public override void Enter()
    {
        if (scene != null) scene.SetActive(true);
        foreach (var t in new[] { hatchetfish, viperfish, constellation, vampireSquid, angler }) if (t != null) t.gameObject.SetActive(false);
        SetDepth(0f);
        SetPhone(false);
    }

    /// <summary>쇠공 가운데를 눈높이에 — 키가 달라도 창이 눈앞에 온다.</summary>
    public override void Arrived(Transform view)
    {
        if (sphere == null || view == null) return;
        Vector3 p = sphere.position;
        p.y = view.position.y;
        sphere.position = p;
    }

    public override void Exit()
    {
        StopAllCoroutines();
        SetPhone(false);
        if (scene != null) scene.SetActive(false);
        Shader.SetGlobalVector(DevicePosId, Vector4.zero);                   // 탐조등 끄기 — 관찰기 조명이 켜져 있으면 다음 프레임에 다시 쓴다
    }

    static readonly int DevicePosId = Shader.PropertyToID("_DeviceLightPos");
    static readonly int DeviceDirId = Shader.PropertyToID("_DeviceLightDir");
    static readonly int DeviceParamsId = Shader.PropertyToID("_DeviceLightParams");
    static readonly int DeviceColorId = Shader.PropertyToID("_DeviceLightColor");

    /// <summary>창 바깥 탐조등 — 심해 생물 셰이더가 이 값으로 비춘다 (관찰기 조명과 같은 자리).</summary>
    void LateUpdate()
    {
        if (scene == null || !scene.activeInHierarchy || searchlight == null) return;
        Vector3 p = searchlight.position;
        float outer = lightAngle * 0.5f, inner = outer * 0.6f;
        Shader.SetGlobalVector(DevicePosId, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalVector(DeviceDirId, searchlight.forward);
        Shader.SetGlobalVector(DeviceParamsId, new Vector4(lightRange, Mathf.Cos(outer * Mathf.Deg2Rad), Mathf.Cos(inner * Mathf.Deg2Rad), lightIntensity));
        Shader.SetGlobalColor(DeviceColorId, lightColor);
    }

    // ─────────────────────────────────────────────────────────────

    public override IEnumerator Play()
    {
        yield return Narrate(introLine);
        yield return Ring();
        yield return Surface(callLine);

        yield return Descend(0f, 150f, 14f);
        StartCoroutine(Narrate(blueLine));
        yield return Descend(150f, 300f, 14f);

        // 300m — 손도끼고기 무리가 지나가고, 수면이 묻는다
        StartCoroutine(Pass(hatchetfish, 1.6f, 0.9f, 9f));
        yield return new WaitForSeconds(3f);
        yield return Ring();
        yield return Ask(ask300, choices300);
        if (chosen == 1) { yield return Surface(reply300None); StartCoroutine(Pass(hatchetfish, 1.4f, 0.8f, 8f)); yield return new WaitForSeconds(4f); }
        else yield return Surface(reply300Seen);

        // 450m — 독사고기
        yield return Descend(300f, 450f, 12f);
        StartCoroutine(Pass(viperfish, 1.5f, 0.9f, 11f));
        yield return new WaitForSeconds(2.5f);
        yield return Narrate(viperLine);

        // 580m — 다섯 줄 별자리 물고기 (비브가 1934년 8월 11일 이 깊이에서 본 것)
        yield return Descend(450f, 580f, 11f);
        StartCoroutine(Pass(constellation, 1.3f, 0.8f, 12f));               // 창 바로 앞 — 손바닥만 한 창으로도 빛점 다섯 줄이 보이게
        yield return new WaitForSeconds(4f);
        yield return Ring();
        yield return Ask(ask580, choices580);
        yield return Surface(reply580);
        yield return Narrate(NameLine(combJellyLine));

        // 700m — 흡혈오징어
        yield return Descend(580f, 700f, 10f);
        StartCoroutine(Narrate(darkLine));
        yield return new WaitForSeconds(3f);
        StartCoroutine(Pass(vampireSquid, 1.4f, 0.8f, 12f));
        yield return new WaitForSeconds(5f);
        yield return Narrate(vampireLine);

        // 923m — 그때까지 사람이 내려간 가장 깊은 곳
        yield return Descend(700f, maxDepth, 16f);
        yield return Ring();
        yield return Surface(arriveLine);
        StartCoroutine(Pass(angler, 1.2f, 0.7f, 13f));
        yield return new WaitForSeconds(3f);
        yield return Narrate(anglerLine);
        yield return new WaitForSeconds(1.5f);
        yield return Narrate(outroLine);
        yield return new WaitForSeconds(1.5f);
    }

    static string NameLine(string line)
    {
        string name = DiveDirector.Instance != null ? DiveDirector.Instance.SignalName : string.Empty;
        return line.Replace("{name}", string.IsNullOrWhiteSpace(name) ? "신호의 주인" : $"'{name}'");
    }

    IEnumerator Descend(float from, float to, float seconds)
    {
        var emission = snow != null ? snow.emission : default;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetDepth(Mathf.Lerp(from, to, t / seconds));
            if (snow != null)
            {
                var main = snow.main;
                main.startSpeed = 0f;
                var vel = snow.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);           // 쇠공이 내려가니 바다눈은 위로 흘러간다
                emission.rateOverTime = Mathf.Lerp(8f, 26f, depth / maxDepth);
            }
            yield return null;
        }
        SetDepth(to);
        if (snow != null)
        {
            var vel = snow.velocityOverLifetime;
            vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);            // 멈추면 천천히 떠다닌다
        }
    }

    /// <summary>창 앞 distance m 를 왼쪽에서 오른쪽으로 지나간다 (창 시야 폭에 맞춰 width m).</summary>
    IEnumerator Pass(Transform t, float distance, float width, float seconds)
    {
        if (t == null || sphere == null) yield break;
        t.gameObject.SetActive(true);
        Vector3 c = sphere.position + sphere.forward * (0.72f + distance);
        Vector3 a = c - sphere.right * width * 1.8f, b = c + sphere.right * width * 1.8f;
        t.rotation = Quaternion.LookRotation(sphere.right, Vector3.up);
        for (float u = 0f; u < 1f; u += Time.deltaTime / seconds)
        {
            t.position = Vector3.Lerp(a, b, u) + Vector3.up * Mathf.Sin(u * Mathf.PI * 2f) * 0.12f;
            yield return null;
        }
        t.gameObject.SetActive(false);
    }

    void SetDepth(float d)
    {
        depth = d;
        if (depthText != null) depthText.text = $"수심 {Mathf.RoundToInt(d)}m";
        if (needle != null) needle.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Lerp(-135f, 135f, d / 1000f));
        if (water == null) return;
        int i = 0;
        while (i < GradientDepth.Length - 2 && d > GradientDepth[i + 1]) i++;
        Color c = Color.Lerp(Gradient[i], Gradient[i + 1], Mathf.InverseLerp(GradientDepth[i], GradientDepth[i + 1], d));
        block ??= new MaterialPropertyBlock();
        water.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        water.SetPropertyBlock(block);
    }

    // ─────────────────────────────────────────────────────────────
    // 전화
    // ─────────────────────────────────────────────────────────────

    IEnumerator Ring()
    {
        if (phone != null && ring != null) { phone.PlayOneShot(ring); yield return new WaitForSeconds(0.9f); phone.PlayOneShot(ring); }
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Ask(string q, string[] options)
    {
        yield return Surface(q);
        chosen = -1;
        if (question != null) question.text = q;
        for (int i = 0; i < choiceLabels.Length; i++) if (choiceLabels[i] != null) choiceLabels[i].text = i < options.Length ? options[i] : "";
        SetPhone(true);
        float start = Time.time;
        while (chosen < 0 && Time.time - start < waitChoice) yield return null;
        if (chosen < 0) chosen = 0;
        SetPhone(false);
    }

    public void Choose0() => Choose(0);
    public void Choose1() => Choose(1);

    void Choose(int i)
    {
        // 찌르기와 레이가 같은 프레임에 둘 다 부를 수 있다 — 한 번만 받는다
        if (phonePanel == null || !phonePanel.interactable || Time.unscaledTime - lastChoice < 0.5f) return;
        lastChoice = Time.unscaledTime;
        chosen = i;
    }

    void SetPhone(bool on)
    {
        if (phonePanel == null) return;
        phonePanel.alpha = on ? 1f : 0f;
        phonePanel.interactable = on;
        phonePanel.blocksRaycasts = on;
    }

    IEnumerator Surface(string line)
    {
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (lobby != null) yield return lobby.SayLine(line, surfaceVoice, surfaceSpeaker);
    }

    IEnumerator Narrate(string line)
    {
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (lobby != null) yield return lobby.SayLine(line);
    }

#if UNITY_EDITOR
    public float DebugDepth => depth;
    public bool DebugAsking => phonePanel != null && phonePanel.interactable;
    public void DebugChoose(int i) { lastChoice = -99f; Choose(i); }
#endif
}
