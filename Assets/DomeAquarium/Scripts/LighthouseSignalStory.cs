using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 밤 로비 이야기 ① 등대의 신호. 곶 끝 등대 꼭대기 난간에 서서, 등명기 손잡이를 눌러 바다로 빛을 보낸다.
///   1987년 등대지기 일지: "등을 세 번 깜빡였더니, 바다가 세 번 대답했다."
///   왜 1,000m 생물이 바다 위에? — 밤이면 깊은 바다 생물이 먹이를 따라 수면 가까이 올라온다(밤의 대이동, 200m 에서 본 빛 기둥).
///   왜 같은 수로? — 신호의 주인은 빛으로 말한다. 같은 수로 대답하면 "나도 여기 있어" 하는 인사가 되고, 인사할 때마다 한 걸음씩 다가온다.
///   ① 내가 세 번 → 세 번 대답 · 다가옴 ② 그가 두 번 → 나도 두 번 · 다가옴 ③ 그가 네 번 → 나도 네 번
///   → 등대 아래 바닷가로 내려가면(암전) 5m 앞 물 위로 떠오른다 — 1,000m 에서 만난 것과 같은 모습 · 크기(SignalCreaturePuppet).
///     등대 꼭대기(32m 위)에서는 1.4m 몸이 점 하나로만 보여 같은 생물인지 알 수 없었다.
///   → 만 곳곳에 무리의 빛. "혼자가 아니었어요."
/// 손잡이는 찌르기(손) · 레이(컨트롤러) 둘 다. 마지막으로 누르고 gap 초가 지나면 몇 번 보냈는지 센다.
/// 장면 동안 등대의 도는 빛줄기는 끄고(Lighthouse 를 멈춤), 누를 때마다 바다 쪽으로 곧은 빛줄기가 한 번 번쩍인다.
/// </summary>
public sealed class LighthouseSignalStory : NightStory
{
    [Header("등대")]
    public Lighthouse lighthouse;
    public Renderer signalBeam;                 // 바다 쪽으로 곧게 — 누를 때만 켠다
    public float flashSeconds = 0.45f;

    [Header("손잡이")]
    public XRSimpleInteractable leverRay;
    public PokeSelector leverPoke;
    public Transform leverArm;                  // 누르면 아래로 젖혀졌다 돌아온다
    public float gap = 1.8f;                    // 마지막으로 누르고 이만큼 지나면 센다
    public float idleLimit = 40f;               // 이만큼 누르지 않으면 바다가 먼저 이어 간다

    [Header("바다")]
    public Renderer seaLight;                   // 대답하는 푸른 빛 (보는 쪽을 향하는 판) — 멀리서 본 신호의 주인
    public Transform[] approach;                // 먼 곳 → 가까운 곳. 인사를 주고받을 때마다 다음 자리로 다가온다
    public float seaLightSize = 34f;            // approach[0] 에서의 빛 판 크기 — 다가오면 거리만큼 줄인다(보이는 크기는 그대로)
    public SignalCreaturePuppet creature;       // 마지막에 바닷가 5m 앞 물 위로 떠오르는 모습
    public Transform shoreSpot;                 // 등대 아래 바닷가 — 마지막에 여기로 내려간다(암전)
    public Renderer[] chorus;                   // 끝에 만 곳곳에서 켜지는 빛들 — 함께 올라온 무리
    [ColorUsage(false, true)] public Color blue = new Color(0.35f, 0.8f, 1.6f);

    [Header("소리")]
    public AudioSource leverSound;
    public AudioClip leverClip;
    public AudioSource seaSound;
    public AudioClip seaClip;
    public AudioClip chorusClip;

    [Header("관측소장")]
    [TextArea(2, 4)] public string introLine =
        "여기는 곶 끝 등대 꼭대기예요. 1987년 가을, 이 등대를 지키던 사람이 일지에 이상한 걸 적었어요. " +
        "'바다 쪽에 푸른 빛. 등을 세 번 깜빡였더니, 바다가 세 번 대답했다.'";
    [TextArea(2, 4)] public string whyUpLine =
        "1,000m 에 사는 생물이 어떻게 바다 위에 있었을까요? 깊은 바다 생물 중에는 밤이 되면 먹이를 따라 수면 가까이 올라왔다가, " +
        "해가 뜨기 전에 다시 내려가는 녀석들이 많아요. 200m 에서 본, 위로 솟던 빛 알갱이 기둥 — 그게 바로 이 '밤의 대이동'이에요.";
    [TextArea(1, 3)] public string lookLine = "저기, 먼 바다 — 세 번씩 깜빡이는 푸른 빛 보이죠? 오늘 밤에도 올라왔어요.";
    [TextArea(2, 4)] public string goalLine =
        "{name}은 빛으로 말해요. 같은 수만큼 깜빡여 대답하면 '나도 여기 있어' 하는 인사가 돼요. " +
        "인사를 주고받을 때마다 조금씩 다가올 거예요. 등대 빛으로 이 아래까지 불러 봐요.";
    [TextArea(1, 3)] public string sendThreeLine = "앞에 있는 손잡이를 한 번 누를 때마다 등대 빛이 한 번 나가요. 먼저 세 번 보내 보세요.";
    [TextArea(1, 3)] public string retryLine = "몇 번 보냈는지 세면서 다시 해 볼까요? 세 번이에요.";
    [TextArea(1, 3)] public string answeredLine = "대답했어요! 세 번 — 1,000m 에서 {name}이 하던 바로 그 말이에요. 봐요, 조금 다가와요.";
    [TextArea(1, 3)] public string seaFirstLine = "이번엔 {name}이 먼저 보내요. 몇 번 깜빡이는지 세어 보고, 같은 수만큼 대답해 주세요.";
    [TextArea(1, 3)] public string seaRetryLine = "다시 한 번 보낼게요. 잘 세어 보세요.";
    [TextArea(1, 3)] public string goodLine = "맞아요! 또 다가와요.";
    [TextArea(1, 3)] public string goDownLine = "등대 바로 아래까지 왔어요. 바닷가로 내려가 가까이서 만나 봐요.";
    [TextArea(1, 3)] public string nearLine = "바로 앞이에요. 오늘 밤 1,000m 에서 만난 — 바로 그 모습이죠?";
    [TextArea(2, 4)] public string finaleLine =
        "그리고… 보세요, 바다 곳곳에서 푸른 빛이 켜져요. 오늘 밤 함께 올라온 무리예요. 등대지기도, 선장님도, 오늘 밤의 우리도 혼자가 아니었어요.";
    [TextArea(1, 3)] public string outroLine = "해가 뜨기 전에 다시 깊은 바다로 내려가겠죠. 이제 관측소로 돌아가요.";

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    MaterialPropertyBlock block;
    Transform view;
    int presses;
    float lastPressAt = -99f;
    bool listening;
    float[] chorusLevel;
    float seaLevel, seaIdle, seaFade = 1f;

    public int Presses => presses;

    void Awake()
    {
        if (leverRay != null) leverRay.selectEntered.AddListener(_ => Press());
        if (leverPoke != null) leverPoke.onPoked.AddListener(Press);
        HideAll();
    }

    public override void Enter()
    {
        if (lighthouse != null)
        {
            lighthouse.enabled = false;                 // 도는 빛줄기를 멈추고 이 장면의 곧은 빛만 쓴다
            if (lighthouse.beams != null) lighthouse.beams.SetActive(false);
        }
        presses = 0;
        listening = false;
        HideAll();
    }

    public override void Exit()
    {
        StopAllCoroutines();
        listening = false;
        HideAll();
        SetLantern(1f);
        if (lighthouse != null) lighthouse.enabled = true;
    }

    void HideAll()
    {
        if (signalBeam != null) signalBeam.enabled = false;
        seaLevel = seaIdle = 0f;
        seaFade = 1f;
        if (seaLight != null)
        {
            seaLight.enabled = false;
            if (approach != null && approach.Length > 0 && approach[0] != null) seaLight.transform.position = approach[0].position;
        }
        if (creature != null) creature.Show(false);
        if (chorus != null)
        {
            chorusLevel = new float[chorus.Length];
            foreach (var c in chorus) if (c != null) c.enabled = false;
        }
    }

    // ─────────────────────────────────────────────────────────────

    public override IEnumerator Play()
    {
        yield return Say(introLine);
        yield return Say(whyUpLine);
        // 먼 바다에서 세 번씩 — 어디를 보면 되는지 먼저 보여 준다
        seaIdle = 0.3f;
        if (seaLight != null) seaLight.enabled = true;
        yield return SeaFlashes(3);
        yield return Say(lookLine);
        yield return SeaFlashes(3);
        yield return Say(NameLine(goalLine));
        yield return Say(sendThreeLine);

        // ① 내가 먼저 세 번
        while (true)
        {
            int got = -1;
            yield return Listen(n => got = n);
            if (got == 3 || got < 0) break;
            yield return Say(retryLine);
        }
        yield return new WaitForSeconds(0.8f);
        yield return SeaFlashes(3);
        StartCoroutine(MoveSea(1, 6f));
        yield return Say(NameLine(answeredLine));

        // ②③ 그가 먼저 — 두 번, 네 번. 맞히면 한 걸음씩 다가온다
        yield return Say(NameLine(seaFirstLine));
        foreach (int count in new[] { 2, 4 })
        {
            while (true)
            {
                yield return SeaFlashes(count);
                int got = -1;
                yield return Listen(n => got = n);
                if (got == count || got < 0) break;
                yield return Say(seaRetryLine);
            }
            yield return new WaitForSeconds(0.5f);
            if (count == 2) { StartCoroutine(MoveSea(2, 6f)); yield return Say(goodLine); }
        }

        // 등대 바로 아래로 — 바닷가로 내려가면 눈앞 물 위로 1,000m 에서 본 모습이 떠오른다
        yield return Say(goDownLine);
        yield return GoDown();
        yield return Surface();
        yield return Say(nearLine);
        yield return CreatureBlinks(3);

        // 끝 — 만 곳곳에 무리의 빛
        StartCoroutine(Chorus());
        yield return new WaitForSeconds(2.5f);
        yield return Say(finaleLine);
        yield return new WaitForSeconds(3f);
        yield return Say(outroLine);
    }

    /// <summary>먼 빛이 approach[index] 로 미끄러지듯 다가온다.</summary>
    IEnumerator MoveSea(int index, float seconds)
    {
        if (seaLight == null || approach == null || index >= approach.Length || approach[index] == null) yield break;
        Vector3 from = seaLight.transform.position, to = approach[index].position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            seaLight.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        seaLight.transform.position = to;
    }

    /// <summary>암전 뒤 등대 아래 바닷가로 — 먼 빛은 끄고, 바다를 보고 선다.</summary>
    IEnumerator GoDown()
    {
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (shoreSpot == null || lobby == null) yield break;
        ScreenFader fader = lobby.Fader;
        if (fader != null) yield return fader.FadeTo(1f, 0.8f);
        seaIdle = 0f;
        if (seaLight != null) seaLight.enabled = false;
        lobby.MoveRigTo(shoreSpot);
        yield return null;
        if (fader != null) yield return fader.FadeTo(0f, 1.2f);
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator Surface()
    {
        if (creature == null) yield break;
        Transform c = creature.transform;
        Vector3 top = c.position, bottom = top - Vector3.up * 3f;
        Vector3 farFrom = seaLight != null ? seaLight.transform.position : top;
        c.position = bottom;
        creature.blinking = false;
        creature.glow = 0.2f;
        creature.Show(true);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 6f)
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            if (seaLight != null) seaLight.transform.position = Vector3.Lerp(farFrom, top, k);
            seaFade = 1f - k;                                              // 먼 빛은 줄어들며 사라지고
            c.position = Vector3.Lerp(bottom, top, k);                     // 몸이 떠오른다
            creature.glow = 0.2f + 0.7f * k;
            yield return null;
        }
        c.position = top;
        seaFade = 0f;
        seaIdle = 0f;
        if (seaLight != null) seaLight.enabled = false;
    }

    IEnumerator CreatureBlinks(int times)
    {
        if (creature == null) yield break;
        for (int i = 0; i < times; i++)
        {
            if (seaSound != null && seaClip != null) seaSound.PlayOneShot(seaClip, 0.6f);
            creature.Pulse();
            yield return new WaitForSeconds(0.75f);
        }
    }

    /// <summary>누르기를 기다렸다가, 마지막으로 누르고 gap 초가 지나면 몇 번인지 돌려준다. 오래 누르지 않으면 -1.</summary>
    IEnumerator Listen(System.Action<int> done)
    {
        presses = 0;
        listening = true;
        float start = Time.time;
        while (presses == 0 || Time.time - lastPressAt < gap)
        {
            if (presses == 0 && Time.time - start > idleLimit) { listening = false; done(-1); yield break; }
            yield return null;
        }
        listening = false;
        done(presses);
    }

    /// <summary>등명기 손잡이 — 한 번 누를 때마다 바다 쪽으로 빛이 한 번.</summary>
    public void Press()
    {
        // 찌르기와 레이가 같은 프레임에 둘 다 부를 수 있다 — 한 번만 받는다. Listen 과 같은 시계(Time.time)로 잰다.
        if (Time.time - lastPressAt < 0.3f) return;
        lastPressAt = Time.time;
        if (listening) presses++;
        if (leverSound != null && leverClip != null) leverSound.PlayOneShot(leverClip);
        StartCoroutine(LampFlash());
        if (leverArm != null) StartCoroutine(LeverDip());
    }

    IEnumerator LampFlash()
    {
        if (signalBeam != null) signalBeam.enabled = true;
        for (float t = 0f; t < flashSeconds; t += Time.deltaTime)
        {
            float k = Mathf.Sin(Mathf.Clamp01(t / flashSeconds) * Mathf.PI);
            SetLantern(1f + 2.5f * k);
            if (signalBeam != null) SetColor(signalBeam, new Color(1f, 0.95f, 0.8f, 0.5f * k));
            yield return null;
        }
        SetLantern(1f);
        if (signalBeam != null) signalBeam.enabled = false;
    }

    IEnumerator LeverDip()
    {
        Quaternion rest = leverArm.localRotation;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            leverArm.localRotation = rest * Quaternion.Euler(Mathf.Sin(t / 0.3f * Mathf.PI) * 35f, 0f, 0f);
            yield return null;
        }
        leverArm.localRotation = rest;
    }

    /// <summary>바다 위 푸른 빛이 count 번 깜빡인다 — 켜지며 짧게 삑.</summary>
    IEnumerator SeaFlashes(int count)
    {
        if (seaLight == null) yield break;
        seaLight.enabled = true;
        if (seaIdle <= 0f) seaIdle = 0.3f;
        for (int i = 0; i < count; i++)
        {
            if (seaSound != null && seaClip != null) seaSound.PlayOneShot(seaClip, 0.6f);
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                seaLevel = t < 0.15f ? t / 0.15f : t < 0.4f ? 1f : 1f - (t - 0.4f) / 0.3f;
                yield return null;
            }
            seaLevel = 0f;
            yield return new WaitForSeconds(0.35f);
        }
    }

    IEnumerator Chorus()
    {
        if (chorus == null) yield break;
        if (seaSound != null && chorusClip != null) seaSound.PlayOneShot(chorusClip, 0.8f);
        for (int i = 0; i < chorus.Length; i++)
        {
            if (chorus[i] != null) chorus[i].enabled = true;
            int k = i;
            StartCoroutine(Rise(k));
            yield return new WaitForSeconds(0.22f);
        }
    }

    IEnumerator Rise(int i)
    {
        for (float t = 0f; t < 1.2f; t += Time.deltaTime) { chorusLevel[i] = t / 1.2f; yield return null; }
        chorusLevel[i] = 1f;
    }

    void LateUpdate()
    {
        if (view == null) { var cam = Camera.main; if (cam == null) return; view = cam.transform; }
        if (seaLight != null && seaLight.enabled)
        {
            Face(seaLight.transform);
            // 다가올수록 판을 줄여 보이는 크기는 그대로 — 가까이 와서 빛 덩어리가 커지지 않게
            if (approach != null && approach.Length > 0 && approach[0] != null)
            {
                float d0 = Vector3.Distance(view.position, approach[0].position), d = Vector3.Distance(view.position, seaLight.transform.position);
                seaLight.transform.localScale = Vector3.one * Mathf.Max(3f, seaLightSize * d / Mathf.Max(1f, d0)) * Mathf.Lerp(0.3f, 1f, seaFade);
            }
            SetColor(seaLight, blue * (Mathf.Max(seaLevel, seaIdle) * seaFade));
        }
        if (chorus == null || chorusLevel == null) return;
        for (int i = 0; i < chorus.Length; i++)
        {
            var c = chorus[i];
            if (c == null || !c.enabled) continue;
            Face(c.transform);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (0.8f + 0.13f * i) + i * 1.7f);   // 저마다 다른 숨
            SetColor(c, blue * (chorusLevel[i] * pulse));
        }
    }

    void Face(Transform t)
    {
        Vector3 to = t.position - view.position;
        if (to.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(to, Vector3.up);
    }

    void SetColor(Renderer r, Color c)
    {
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        r.SetPropertyBlock(block);
    }

    void SetLantern(float boost)
    {
        if (lighthouse == null || lighthouse.lantern == null) return;
        block ??= new MaterialPropertyBlock();
        lighthouse.lantern.GetPropertyBlock(block);
        block.SetColor(EmissionId, lighthouse.nightGlow * boost);
        lighthouse.lantern.SetPropertyBlock(block);
    }

    IEnumerator Say(string line)
    {
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (lobby != null) yield return lobby.SayLine(line);
    }

    static string NameLine(string line)
    {
        string name = DiveDirector.Instance != null ? DiveDirector.Instance.SignalName : string.Empty;
        return line.Replace("{name}", string.IsNullOrWhiteSpace(name) ? "신호의 주인" : $"'{name}'");
    }

#if UNITY_EDITOR
    public bool DebugListening => listening;
    public bool DebugSurfaced => creature != null && creature.Shown;
#endif
}
