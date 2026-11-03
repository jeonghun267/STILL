using System.Collections;
using UnityEngine;

/// <summary>
/// 밤 로비 이야기 ② 바다거북의 밤. 밤 해변, 모래 둥지 옆에서 놀란이 기다린다.
///   새끼 바다거북은 달빛이 비친 바다 쪽 밝은 수평선을 보고 기어간다 — 사람 불빛이 있으면 그쪽을 바다로 착각한다.
///   ① 놀란: 관찰기 조명이 켜져 있어. A/X 로 꺼 줄래? → 끄면 둥지에서 새끼들이 하나둘 나온다
///   ② 바다로 기어간다. 다시 켜면 새끼들이 불빛(관찰기) 쪽으로 돌아선다 → 놀란이 말린다
///   ③ 모두 파도에 닿으면 — "언젠가 이 아이들도 이 해변으로 돌아올 거야."
/// "불을 끄고 가만히"(1,000m 의 만남)가 여기서는 "지킴"이 된다. 1,000m 처럼 관찰기 조명을 쓴다(ObservationLight.allowOutside).
/// </summary>
public sealed class TurtleNightStory : NightStory
{
    [Header("놀란")]
    public NpcDialogue nolant;
    public Transform nolantSpot;                // 이 장면에서 놀란이 서는 자리

    [Header("둥지 · 새끼")]
    public ObservationLight deviceLight;
    public Transform nest;
    public GameObject[] shells;                 // 깨어나면 보이는 알껍데기 조각
    public Transform[] hatchlings;              // 모래 밑에 숨겨 둔 새끼들 (빌더가 만든다)
    public Terrain ground;
    public Vector3 seaDirection = Vector3.right;    // 바다 쪽 (월드, 수평)
    public float seaY;                          // 바다 높이 (월드) — 땅이 이보다 낮으면 물
    public float crawlSpeed = 0.17f;            // m/s — 실제 새끼는 1분에 10m 남짓
    public float confusedSpeed = 0.08f;
    public float emergeGap = 0.7f;
    public float giveUpSeconds = 45f;           // 끄지 않고 이만큼 지나면 놀란이 끈다

    [Header("소리")]
    public AudioSource sand;
    public AudioClip sandClip;

    [Header("놀란 대사")]
    [TextArea(2, 4)] public string hushLine = "쉿… 이리 와 봐. 여기 모래 밑에 바다거북 알이 묻혀 있어. 오늘 밤 깨어날 거야!";
    [TextArea(2, 4)] public string lightLine =
        "그런데 네 관찰기 조명이 켜져 있네. 새끼 거북은 달빛이 비친 바다 쪽으로 기어가는데, 불빛이 있으면 그쪽을 바다로 착각해. " +
        "A 버튼이나 X 버튼으로 조명을 꺼 줄래?";
    [TextArea(1, 3)] public string offLine = "고마워. 이제 가만히 지켜보자…";
    [TextArea(1, 3)] public string helpLine = "괜찮아, 내가 꺼 줄게.";
    [TextArea(1, 3)] public string hatchLine = "봐, 모래가 움직여! 나온다!";
    [TextArea(1, 3)] public string strayLine = "앗, 불빛 쪽으로 오잖아! 조명을 꺼 줘!";
    [TextArea(2, 4)] public string doneLine = "다 바다에 닿았어! 고마워. 언젠가 이 아이들도 저 바다를 건너, 어른이 되면 이 해변으로 돌아와 알을 낳을 거야.";
    [TextArea(1, 3)] public string rescuedLine = "오늘 네가 24m 에서 구해 준 그 거북처럼 말이야.";

    enum Phase { Buried, Emerging, Crawling, Swimming, Gone }

    Phase[] phase;
    float[] timer;
    float[] heading;                            // 바다 쪽 기준 좌우로 흔들리는 각
    Animator[] animators;
    Vector3 nolantHome;
    Quaternion nolantHomeRot;
    bool running;
    float lastStrayLine = -99f;

    public int Gone { get; private set; }

    Vector3[] baseScale;

    void Awake()
    {
        baseScale = new Vector3[hatchlings != null ? hatchlings.Length : 0];
        for (int i = 0; i < baseScale.Length; i++) if (hatchlings[i] != null) baseScale[i] = hatchlings[i].localScale;
        HideAll();
    }

    public override void Enter()
    {
        if (nolant != null && nolant.npc != null && nolantSpot != null)
        {
            nolantHome = nolant.npc.transform.position;
            nolantHomeRot = nolant.npc.transform.rotation;
            nolant.npc.Place(nolantSpot.position, nolantSpot.rotation);
        }
        if (deviceLight != null)
        {
            deviceLight.allowOutside = true;
            deviceLight.Unlock();
            deviceLight.SetOn(true);
        }
        HideAll();
    }

    public override void Exit()
    {
        StopAllCoroutines();
        running = false;
        HideAll();
        if (deviceLight != null) { deviceLight.SetOn(false); deviceLight.allowOutside = false; }
        if (nolant != null && nolant.npc != null && nolantSpot != null) nolant.npc.Place(nolantHome, nolantHomeRot);
    }

    void HideAll()
    {
        int n = hatchlings != null ? hatchlings.Length : 0;
        phase = new Phase[n];
        timer = new float[n];
        heading = new float[n];
        animators = new Animator[n];
        Gone = 0;
        for (int i = 0; i < n; i++)
        {
            if (hatchlings[i] == null) continue;
            hatchlings[i].gameObject.SetActive(false);
            if (baseScale != null && i < baseScale.Length) hatchlings[i].localScale = baseScale[i];   // 물속으로 사라지며 줄었던 크기를 되돌린다
            animators[i] = hatchlings[i].GetComponentInChildren<Animator>(true);
        }
        if (shells != null) foreach (var s in shells) if (s != null) s.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────

    public override IEnumerator Play()
    {
        yield return Say(hushLine);
        yield return Say(lightLine);

        // ① 조명을 끌 때까지
        float asked = Time.time;
        while (deviceLight != null && deviceLight.On)
        {
            if (Time.time - asked > giveUpSeconds) { yield return Say(helpLine); deviceLight.SetOn(false); break; }
            yield return null;
        }
        yield return Say(offLine);
        yield return new WaitForSeconds(1.5f);

        // ② 깨어나 기어간다
        if (sand != null && sandClip != null) sand.PlayOneShot(sandClip);
        if (shells != null) foreach (var s in shells) if (s != null) s.SetActive(true);
        StartCoroutine(Say(hatchLine));
        running = true;
        for (int i = 0; i < hatchlings.Length; i++)
        {
            Emerge(i);
            yield return new WaitForSeconds(emergeGap * Random.Range(0.6f, 1.4f));
        }
        float since = Time.time;
        while (Gone < hatchlings.Length)
        {
            // 너무 오래 걸리면(불을 계속 켜 두면) 놀란이 대신 꺼 준다
            if (deviceLight != null && deviceLight.On && Time.time - since > giveUpSeconds)
            {
                yield return Say(helpLine);
                deviceLight.SetOn(false);
                since = Time.time;
            }
            yield return null;
        }
        running = false;

        // ③ 모두 바다로
        yield return new WaitForSeconds(1f);
        yield return Say(doneLine);
        if (DiveDirector.Instance != null && DiveDirector.Instance.TurtleRescued) yield return Say(rescuedLine);
        yield return new WaitForSeconds(1.5f);
    }

    void Emerge(int i)
    {
        Transform t = hatchlings[i];
        if (t == null) { phase[i] = Phase.Gone; Gone++; return; }
        Vector2 jitter = Random.insideUnitCircle * 0.12f;
        Vector3 p = nest.position + new Vector3(jitter.x, 0f, jitter.y);
        p.y = Ground(p) - 0.06f;
        t.position = p;
        heading[i] = Random.Range(-35f, 35f);
        t.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, heading[i], 0f) * seaDirection, Vector3.up);
        t.gameObject.SetActive(true);
        phase[i] = Phase.Emerging;
        timer[i] = 0f;
    }

    void Update()
    {
        if (!running || hatchlings == null) return;
        bool lit = deviceLight != null && deviceLight.On;
        Vector3 lamp = deviceLight != null ? deviceLight.transform.position : Vector3.zero;
        bool anyStray = false;
        float dt = Time.deltaTime;
        for (int i = 0; i < hatchlings.Length; i++)
        {
            Transform t = hatchlings[i];
            if (t == null || phase[i] == Phase.Buried || phase[i] == Phase.Gone) continue;
            timer[i] += dt;
            Vector3 p = t.position;
            switch (phase[i])
            {
                case Phase.Emerging:                                       // 모래 밖으로 고개를 내민다
                    p.y = Mathf.Lerp(Ground(p) - 0.06f, Ground(p) + 0.005f, timer[i] / 0.6f);
                    t.position = p;
                    if (timer[i] >= 0.6f) { phase[i] = Phase.Crawling; timer[i] = 0f; }
                    SetAnim(i, 0.6f);
                    break;

                case Phase.Crawling:
                {
                    // 바다 쪽(조금씩 좌우로) — 불이 켜져 있으면 불빛 아래로 돌아선다
                    Vector3 want = Quaternion.Euler(0f, heading[i] * 0.4f, 0f) * seaDirection;
                    float speed = crawlSpeed;
                    if (lit)
                    {
                        Vector3 toLamp = lamp - p;
                        toLamp.y = 0f;
                        if (toLamp.magnitude > 0.35f) { want = toLamp.normalized; speed = confusedSpeed; anyStray = true; }
                        else speed = 0f;                                    // 불빛 바로 아래에서 맴돈다
                    }
                    want = Quaternion.Euler(0f, Mathf.Sin(Time.time * 6f + i) * 18f, 0f) * want;    // 뒤뚱뒤뚱
                    Quaternion face = Quaternion.LookRotation(want, Vector3.up);
                    t.rotation = Quaternion.RotateTowards(t.rotation, face, 160f * dt);
                    p += t.forward * speed * dt;
                    p.y = Ground(p) + 0.005f;
                    t.position = p;
                    SetAnim(i, speed > 0f ? 2.4f : 0.8f);
                    if (Ground(p) < seaY - 0.02f) { phase[i] = Phase.Swimming; timer[i] = 0f; }
                    break;
                }

                case Phase.Swimming:                                       // 파도에 닿으면 물속으로 사라진다
                    p += t.forward * crawlSpeed * 1.5f * dt;
                    p.y = Mathf.Lerp(seaY, seaY - 0.15f, timer[i] / 1.2f);
                    t.position = p;
                    t.localScale = t.localScale * (1f - 0.6f * dt);
                    if (timer[i] >= 1.2f) { phase[i] = Phase.Gone; Gone++; t.gameObject.SetActive(false); }
                    break;
            }
        }
        if (anyStray && Time.time - lastStrayLine > 10f && nolant != null && !nolant.Talking)
        {
            lastStrayLine = Time.time;
            StartCoroutine(Say(strayLine));
        }
    }

    void SetAnim(int i, float speed)
    {
        if (animators[i] != null) animators[i].speed = speed;
    }

    float Ground(Vector3 p) => ground != null ? ground.SampleHeight(p) + ground.transform.position.y : p.y;

    IEnumerator Say(string line)
    {
        if (nolant != null) yield return nolant.SayLine(line);
    }
}
