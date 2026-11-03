using System.Collections;
using UnityEngine;

/// <summary>
/// 밤 로비 이야기 ③ 선장의 밤바다. 보리 선장이 "이제 내 차례일세" — 연구선을 몰고 만 바깥 밤바다로 나간다.
///   ① 갑판 뒤 단추 둘: 엔진 끄기 · 갑판 불 끄기 → ② 가만히 있으면(StillnessDetector) 배 둘레 물이 푸르게 빛난다
///   ③ 향유고래가 숨을 쉬러 올라와 물을 뿜고 → ④ 선장이 "밤이면 깊은 바다 생물이 수면 가까이 올라온다(밤의 대이동)"를 들려주고,
///   신호의 주인이 배 옆 물 위로 떠올라 세 번 깜빡인다. "삼십 년 만이군… 고맙네, 탐험가."
///   신호의 주인은 1,000m 에서 만난 것과 같은 모습 · 크기(SignalCreaturePuppet) — 예전에는 둥근 공이라 다른 생물처럼 보였다.
/// "불을 끄고 가만히"가 1,000m(만남) → 바다거북의 밤(지킴) → 여기(재회)에서 세 번째로 되풀이된다.
/// 정박해 있던 연구선은 장면 동안 숨기고, 만 한가운데에 둔 이야기용 연구선(boat)을 켠다 — 흔들리지 않게(멀미) 고정.
/// </summary>
public sealed class CaptainNightStory : NightStory
{
    [Header("배 · 선장")]
    public GameObject boat;                     // 이야기용 연구선 (평소 꺼 둔다)
    public GameObject mooredBoat;               // 테라스 옆에 정박한 연구선 — 장면 동안 숨긴다 ("배를 몰고 나갔다")
    public NpcDialogue captain;
    public Transform captainSpot;

    [Header("단추 · 엔진 · 갑판 불 (단추는 빌더가 PressEngine · PressLamp 에 잇는다 — 찌르기 · 레이 둘 다)")]
    public TMPro.TMP_Text engineLabel, lampLabel;
    public AudioSource engine;                  // 엔진 소리 (고리)
    public Renderer lampBulb;
    public Renderer lampHalo;
    public AudioSource click;
    public AudioClip clickClip;

    [Header("바다")]
    public StillnessDetector stillness;
    public Renderer[] seaGlow;                  // 배 둘레 물 위에 눕힌 빛 판
    [ColorUsage(false, true)] public Color glowColor = new Color(0.22f, 0.70f, 1f, 1f);
    public Transform whale;                     // 향유고래 (평소 꺼 둔다)
    public ParticleSystem whaleSpout;
    public AudioSource whaleBlow;
    public Transform whaleStart, whaleEnd;
    public SignalCreaturePuppet owner;          // 신호의 주인 — 배 옆 물 위로 떠오른다
    public float ownerDepth = 4f;

    [Header("선장")]
    [TextArea(2, 4)] public string openLine = "이제 내 차례일세. 삼십 년 전, 폭풍우가 지나간 밤… 바로 이 바다에서 그 푸른빛을 봤지.";
    [TextArea(1, 3)] public string switchLine = "엔진을 끄고, 갑판 불도 꺼 주게. 앞쪽 난간의 단추 두 개라네.";
    [TextArea(1, 3)] public string stillLine = "좋아… 이제 가만히 있어 보게. 그날 밤도 엔진이 멎어 이렇게 조용했지. 깊은 바다 녀석들은 불빛과 엔진 소리를 피하거든.";
    [TextArea(1, 3)] public string glowLine = "보게, 물이 빛나기 시작하는구먼. 야광 플랑크톤이라네. 배가 멈추니 바다가 깨어나는 게야.";
    [TextArea(1, 3)] public string whaleLine = "저기! 향유고래가 숨을 쉬러 올라왔어. 깊은 바다에서 사냥하고 돌아온 걸세.";
    [TextArea(2, 4)] public string whyLine =
        "그거 아나? 밤이 되면 깊은 바다의 많은 생물이 먹이를 따라 수면 가까이 올라온다네. 해가 뜨기 전에 다시 내려가고. " +
        "자네가 200m 에서 본, 위로 솟던 빛 알갱이 기둥 — 그게 바로 그 '밤의 대이동'일세. 그날 밤 내가 본 푸른빛도 그렇게 올라왔던 게야.";
    [TextArea(1, 3)] public string ownerLine = "…저기, 배 옆이야. 저 빛… 삼십 년 만이군.";
    [TextArea(2, 4)] public string thanksLine = "세 번 깜빡였어. 자네가 1,000m 에서 만난 바로 그 생물이지? {name} — 고맙네, 탐험가. 자네 덕분에 다시 만났어.";
    public float switchWaitLimit = 45f;
    public float stillWaitLimit = 20f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    MaterialPropertyBlock block;
    Transform view;
    bool engineOn, lampOn;
    float glow;
    Vector3 captainHome;
    Quaternion captainHomeRot;
    float lastPress = -99f;

    public bool EngineOn => engineOn;
    public bool LampOn => lampOn;

    void Awake()
    {
        if (boat != null) boat.SetActive(false);
    }

    public override void Enter()
    {
        if (boat != null) boat.SetActive(true);
        if (mooredBoat != null) mooredBoat.SetActive(false);
        if (captain != null && captain.npc != null && captainSpot != null)
        {
            captainHome = captain.npc.transform.position;
            captainHomeRot = captain.npc.transform.rotation;
            captain.npc.Place(captainSpot.position, captainSpot.rotation);
        }
        engineOn = lampOn = true;
        glow = 0f;
        if (engine != null) { engine.volume = 0.55f; engine.Play(); }
        if (whale != null) whale.gameObject.SetActive(false);
        if (owner != null) owner.Show(false);
        Refresh();
    }

    public override void Exit()
    {
        StopAllCoroutines();
        if (engine != null) engine.Stop();
        if (boat != null) boat.SetActive(false);
        if (mooredBoat != null) mooredBoat.SetActive(true);
        if (captain != null && captain.npc != null && captainSpot != null) captain.npc.Place(captainHome, captainHomeRot);
    }

    // ─────────────────────────────────────────────────────────────

    public override IEnumerator Play()
    {
        yield return Say(openLine);
        yield return Say(switchLine);
        float asked = Time.time;
        while (engineOn || lampOn)
        {
            if (Time.time - asked > switchWaitLimit) { if (engineOn) PressEngine(); else if (lampOn) PressLamp(); asked = Time.time - switchWaitLimit + 1.5f; }
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        yield return Say(stillLine);

        // 가만히 — 오래 기다리지는 않는다
        float since = Time.time;
        while (Time.time - since < stillWaitLimit && (stillness == null || stillness.StillSeconds < 3f)) yield return null;

        // 물이 빛난다
        for (float t = 0f; t < 4f; t += Time.deltaTime) { glow = t / 4f; yield return null; }
        glow = 1f;
        yield return Say(glowLine);

        // 향유고래
        if (whale != null) StartCoroutine(Whale());
        yield return new WaitForSeconds(2.5f);
        yield return Say(whaleLine);
        yield return new WaitForSeconds(4f);
        yield return Say(whyLine);

        // 신호의 주인 — 깊은 곳에서 천천히 떠오른다
        if (owner != null)
        {
            Transform o = owner.transform;
            Vector3 top = o.position;
            Vector3 bottom = top - Vector3.up * ownerDepth;
            o.position = bottom;
            owner.blinking = false;
            owner.glow = 0.3f;
            owner.Show(true);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 6f)
            {
                o.position = Vector3.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, t));
                owner.glow = 0.3f + 0.5f * t;
                yield return null;
            }
            o.position = top;
            yield return Say(ownerLine);
            yield return Blink(3);
            yield return Say(NameLine(thanksLine));
            yield return Blink(3);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 5f)
            {
                o.position = Vector3.Lerp(top, bottom, Mathf.SmoothStep(0f, 1f, t));
                owner.glow = Mathf.Lerp(0.8f, 0f, t);
                yield return null;
            }
            owner.Show(false);
            o.position = top;
        }
        yield return new WaitForSeconds(2f);
    }

    /// <summary>1,000m 에서처럼 세 번 깜빡인다.</summary>
    IEnumerator Blink(int times)
    {
        for (int i = 0; i < times; i++)
        {
            owner.Pulse();
            yield return new WaitForSeconds(0.75f);
        }
        yield return new WaitForSeconds(0.6f);
    }

    IEnumerator Whale()
    {
        whale.gameObject.SetActive(true);
        Vector3 a = whaleStart.position, b = whaleEnd.position;
        Quaternion face = Quaternion.LookRotation(b - a, Vector3.up);
        whale.SetPositionAndRotation(a - Vector3.up * 5f, face);
        bool blown = false;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 3f)                      // 떠오른다 — 머리가 물을 가르는 순간 첫 숨
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            whale.position = Vector3.Lerp(a - Vector3.up * 5f, a, k);
            if (!blown && k >= 0.85f) { blown = true; Breathe(); }
            yield return null;
        }
        if (!blown) Breathe();
        float glide = 14f;
        for (float t = 0f; t < glide; t += Time.deltaTime)
        {
            whale.position = Vector3.Lerp(a, b, t / glide) + Vector3.up * Mathf.Sin(t * 0.8f) * 0.12f;
            if (t < 6f && t + Time.deltaTime >= 6f) Breathe();                     // 수면에서 한 번 더
            yield return null;
        }
        Vector3 end = whale.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 3.5f)                    // 머리부터 숙이며 들어간다
        {
            whale.SetPositionAndRotation(end + whale.forward * t * 4f - Vector3.up * t * 6f, face * Quaternion.Euler(t * 25f, 0f, 0f));
            yield return null;
        }
        whale.gameObject.SetActive(false);
    }

    void Breathe()
    {
        if (whaleSpout != null) { whaleSpout.Clear(true); whaleSpout.Play(true); }
        if (whaleBlow != null && whaleBlow.clip != null)
        {
            float delay = view != null ? Vector3.Distance(view.position, whale.position) / 340f : 0f;
            whaleBlow.PlayDelayed(delay);
        }
    }

    // ─────────────────────────────────────────────────────────────

    public void PressEngine()
    {
        if (!engineOn || Time.unscaledTime - lastPress < 0.4f) return;
        lastPress = Time.unscaledTime;
        engineOn = false;
        if (click != null && clickClip != null) click.PlayOneShot(clickClip);
        if (engine != null) StartCoroutine(FadeEngine());
        Refresh();
    }

    public void PressLamp()
    {
        if (!lampOn || Time.unscaledTime - lastPress < 0.4f) return;
        lastPress = Time.unscaledTime;
        lampOn = false;
        if (click != null && clickClip != null) click.PlayOneShot(clickClip);
        Refresh();
    }

    IEnumerator FadeEngine()
    {
        float from = engine.volume;
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)                       // 털털거리다 멎는다
        {
            engine.volume = Mathf.Lerp(from, 0f, t / 2.5f);
            engine.pitch = Mathf.Lerp(1f, 0.6f, t / 2.5f);
            yield return null;
        }
        engine.Stop();
        engine.pitch = 1f;
    }

    void Refresh()
    {
        if (engineLabel != null) engineLabel.text = engineOn ? "엔진 끄기" : "엔진 꺼짐";
        if (lampLabel != null) lampLabel.text = lampOn ? "갑판 불 끄기" : "갑판 불 꺼짐";
        if (lampHalo != null) lampHalo.enabled = lampOn;
        if (lampBulb != null)
        {
            block ??= new MaterialPropertyBlock();
            lampBulb.GetPropertyBlock(block);
            block.SetColor(EmissionId, lampOn ? new Color(2.2f, 1.7f, 1.0f) : Color.black);
            lampBulb.SetPropertyBlock(block);
        }
    }

    void LateUpdate()
    {
        if (view == null) { var cam = Camera.main; if (cam == null) return; view = cam.transform; }
        if (seaGlow != null)
            for (int i = 0; i < seaGlow.Length; i++)
            {
                var r = seaGlow[i];
                if (r == null) continue;
                bool on = glow > 0.01f;
                if (r.enabled != on) r.enabled = on;
                if (!on) continue;
                float wave = 0.55f + 0.45f * Mathf.Sin(Time.time * (0.7f + 0.11f * i) + i * 2.3f);   // 물결 따라 일렁인다
                SetColor(r, BaseColorId, glowColor * (glow * wave));
            }
        if (lampHalo != null && lampHalo.enabled) Face(lampHalo.transform);
    }

    void Face(Transform t)
    {
        Vector3 to = t.position - view.position;
        if (to.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(to, Vector3.up);
    }

    void SetColor(Renderer r, int id, Color c)
    {
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(id, c);
        r.SetPropertyBlock(block);
    }

    /// <summary>선장 말은 시선을 따라오는 자막에 선장 목소리로 — 바다를 보며 듣는다 (선장 옆 판은 신호의 주인 쪽 시야를 가렸다).</summary>
    IEnumerator Say(string line)
    {
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (lobby != null && captain != null) yield return lobby.SayLine(line, captain.voice, captain.speaker);
        else if (captain != null) yield return captain.SayLine(line);
    }

    static string NameLine(string line)
    {
        string name = DiveDirector.Instance != null ? DiveDirector.Instance.SignalName : string.Empty;
        return line.Replace("{name}", string.IsNullOrWhiteSpace(name) ? "그 푸른빛" : $"'{name}'");
    }

#if UNITY_EDITOR
    public float DebugGlow => glow;
#endif
}
