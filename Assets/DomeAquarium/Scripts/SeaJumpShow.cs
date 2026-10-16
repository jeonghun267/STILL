using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 로비 창밖 바다에서 돌고래·쥐가오리·가다랑어가 가끔 물 위로 뛰어오른다 — 땅 위에서 바다를 보며 시작하는 1막의 볼거리.
///
/// 어디서 뛰나:
///  - 창밖의 실제 바다 면(SeaSurface) 위. 보는 사람 눈에서 창이 향하는 +X 기준 −40° ~ +40°, 16~38m 앞이다.
///    보는 사람이 창 쪽을 보고 있으면 그 시선 근처에서 뛴다. 부두(테라스) 위로 넘어오지 않게 바깥으로 민다.
///  - 바다 면이 없으면(예전 장면) HDRI 사진 속 물을 수면으로 삼는다 — "눈에서 수평선 아래 4.5~6.5°" 가 만(灣)의 물이다.
///
/// 어떻게 뛰나: 실제 중력(9.8m/s²)으로 포물선을 그리고 몸은 속도 방향을 본다. 몸길이의 절반쯤 물속에서 출발해 물속으로 들어가므로
/// 수면이 몸을 정말로 가린다. 옆모습이 보이게 시선과 거의 직각으로 지나가고, 돌고래는 2~4마리가 차례로, 가다랑어는 떼로,
/// 쥐가오리는 혼자 크게 뛴다. 수면을 뚫고 나올 때와 들어갈 때 물보라·물결 고리가 일고, 물소리는 다시 들어갈 때만 작게 난다.
/// 뛰는 생물을 바라보고 있으면 위에 이름이 잠깐 뜬다.
/// </summary>
public sealed class SeaJumpShow : MonoBehaviour
{
    [Serializable]
    public class Performer
    {
        public FishData fish;
        [Min(0.01f)] public float weight = 1f;
        public Vector2Int pod = new Vector2Int(1, 3);         // 한 번에 뛰는 수
        public Vector2 height = new Vector2(1.5f, 2.5f);      // 물 위로 오르는 높이(m)
        public Vector2 reach = new Vector2(4f, 6f);           // 물에서 나와 다시 들어가기까지 수평 거리(m)
        public float sizeMultiplier = 1f;
        [Range(0f, 1f)] public float pitchFollow = 1f;        // 몸이 속도 방향으로 얼마나 숙이고 드는지 (쥐가오리는 납작하게 떨어진다)
        [Range(0f, 1f)] public float spinChance;              // 공중에서 몸통을 한 바퀴 도는 비율
        public float splashSize = 1f;
    }

    public LobbySequence lobby;
    public SeaSurface sea;                                   // 실제 수면. 없으면 HDRI 사진 속 물을 쓴다
    public DepthWallPreview depthWall;                       // 수심 벽 연출로 로비가 캄캄할 때는 쉰다
    public Performer[] performers;
    public ParticleSystem splash;                            // 물보라 — Emit 으로 위치마다 뿌린다 (월드 시뮬레이션)
    public Material ringMaterial;                            // 물결 고리
    public AudioClip splashSmall;
    public AudioClip splashBig;
    public CanvasGroup label;
    public TMP_Text labelText;

    [Header("때")]
    public float firstDelay = 6f;
    public Vector2 interval = new Vector2(7f, 13f);

    [Header("어디서 — 창이 향하는 +X 기준 (로비 로컬)")]
    public Vector2 azimuth = new Vector2(-40f, 40f);
    public Vector2 distance = new Vector2(16f, 38f);
    public float minOffshore = 24f;                          // 로비 로컬 x — 이보다 가까우면 바깥으로 민다 (부두 끝 13.5m + 물보라 여유)
    [Tooltip("바다 면이 없을 때만: 눈에서 수평선 아래 이 각도의 HDRI 픽셀을 수면으로 삼는다")]
    public Vector2 depression = new Vector2(4.5f, 6.5f);

    [Header("이름표")]
    public float labelSeconds = 2.6f;
    public float labelLookAngle = 40f;                      // 이 각도 안을 보고 있을 때만 이름을 띄운다

    const float Gravity = 9.81f;
    const int RingCount = 4;
    const int VoiceCount = 3;

    sealed class Jumper
    {
        public Transform body;
        public FishAnimatorSync anim;
        public bool busy;
    }

    sealed class Ring
    {
        public Transform transform;
        public Renderer renderer;
        public float t = -1f;
        public float size;
    }

    List<Jumper>[] pools;
    readonly Ring[] rings = new Ring[RingCount];
    readonly AudioSource[] voices = new AudioSource[VoiceCount];
    int nextRing, nextVoice;
    float lastSound = -10f;
    MaterialPropertyBlock block;
    Transform viewCache;
    float labelUntil = -1f;
    Vector3 labelAnchor;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Start()
    {
        block = new MaterialPropertyBlock();
        BuildPools();
        BuildRings();
        BuildVoices();
        if (label != null) label.alpha = 0f;
        StartCoroutine(Loop());
    }

    void BuildPools()
    {
        pools = new List<Jumper>[performers != null ? performers.Length : 0];
        for (int p = 0; p < pools.Length; p++)
        {
            pools[p] = new List<Jumper>();
            Performer perf = performers[p];
            if (perf.fish == null || perf.fish.prefab == null) continue;
            for (int i = 0; i < Mathf.Max(1, perf.pod.y); i++)
            {
                GameObject go = Instantiate(perf.fish.prefab, transform);
                go.name = "Jumper_" + perf.fish.DisplayName + "_" + i;
                go.transform.localScale = Vector3.one * Mathf.Max(0.001f, perf.fish.swimScale * perf.sizeMultiplier);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 30m 밖 물 위 — 그림자 받을 곳이 없다
                FishAnimatorSync sync = go.GetComponentInChildren<FishAnimatorSync>(true);
                if (sync == null) sync = go.AddComponent<FishAnimatorSync>();
                sync.bankAngle = 0f;                                                  // 몸통 기울기는 여기서 정한다
                go.SetActive(false);
                pools[p].Add(new Jumper { body = go.transform, anim = sync });
            }
        }
    }

    void BuildRings()
    {
        for (int i = 0; i < RingCount; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SplashRing_" + i;
            quad.transform.SetParent(transform, false);
            Destroy(quad.GetComponent<Collider>());
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = ringMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            rings[i] = new Ring { transform = quad.transform, renderer = r };
        }
    }

    void BuildVoices()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var go = new GameObject("SplashVoice_" + i);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;                     // 소리 나는 쪽을 보면 물고기가 있다
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = 55f;
            src.dopplerLevel = 0f;
            src.volume = 0.5f;                         // 창 너머 20m 밖의 물소리 — 파도·음악보다 튀지 않게
            voices[i] = src;
        }
    }

    IEnumerator Loop()
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            if (CanShow()) StartCoroutine(Show(null));
            yield return new WaitForSeconds(Random.Range(interval.x, interval.y));
        }
    }

    bool CanShow()
    {
        if (lobby != null && !lobby.AcceptsActivity) return false;          // 엘리베이터에 오른 뒤에는 창이 안 보인다
        if (depthWall != null && depthWall.ActiveBand >= 0) return false;   // 1,000m 처럼 캄캄할 때 바다에서 뛰면 어색하다
        return ResolveView() != null;
    }

    /// <summary>지금 시선 근처에서 바로 한 번 뛴다 (에디터 확인용 · 다른 연출이 부를 수 있다).</summary>
    public void JumpNow(int performerIndex = -1)
    {
        Performer p = performers != null && performerIndex >= 0 && performerIndex < performers.Length ? performers[performerIndex] : null;
        StartCoroutine(Show(p));
    }

    IEnumerator Show(Performer forced)
    {
        Transform view = ResolveView();
        if (view == null || pools == null) yield break;
        int pi = forced != null ? Array.IndexOf(performers, forced) : PickPerformer();
        if (pi < 0) yield break;
        Performer perf = performers[pi];

        // 방향: 창 쪽을 보고 있으면 그 시선 근처, 아니면 만의 물 어디든
        Vector3 gaze = transform.InverseTransformDirection(view.forward);
        float gazeAz = Mathf.Atan2(gaze.z, gaze.x) * Mathf.Rad2Deg;
        bool looking = gaze.x > 0.2f && gazeAz > azimuth.x - 25f && gazeAz < azimuth.y + 25f;
        float az = looking ? Mathf.Clamp(gazeAz + Random.Range(-8f, 8f), azimuth.x, azimuth.y)
                           : Random.Range(azimuth.x, azimuth.y);
        float dist = Random.Range(distance.x, distance.y);
        float dep = Random.Range(depression.x, depression.y);

        Vector3 dir = transform.TransformDirection(new Vector3(Mathf.Cos(az * Mathf.Deg2Rad), 0f, Mathf.Sin(az * Mathf.Deg2Rad)));
        Vector3 eye = view.position;
        Vector3 center = eye + dir * dist;
        float seaY;
        if (sea != null)
        {
            seaY = sea.SeaLevel;
            Vector3 local = transform.InverseTransformPoint(center);
            if (local.x < minOffshore) center += transform.right * (minOffshore - local.x);
        }
        else seaY = eye.y - dist * Mathf.Tan(dep * Mathf.Deg2Rad);
        center.y = seaY;

        // 옆모습이 보이게 시선과 거의 직각으로 지나간다
        Vector3 heading = Quaternion.AngleAxis((Random.value < 0.5f ? 90f : -90f) + Random.Range(-25f, 25f), Vector3.up) * dir;
        Vector3 side = Vector3.Cross(Vector3.up, heading).normalized;
        int count = Random.Range(perf.pod.x, perf.pod.y + 1);
        float height = Random.Range(perf.height.x, perf.height.y);
        float reach = Random.Range(perf.reach.x, perf.reach.y);
        bool spin = Random.value < perf.spinChance;

        float delay = 0f;
        for (int i = 0; i < count; i++)
        {
            Jumper j = Take(pi);
            if (j == null) break;
            Vector3 start = center - heading * (reach * 0.5f + i * Random.Range(1.2f, 2.2f));
            if (i > 0) start += side * Random.Range(-1.4f, 1.4f);
            start.y = seaY;
            StartCoroutine(Jump(j, perf, start, heading, reach * Random.Range(0.9f, 1.1f),
                                height * Random.Range(0.85f, 1.1f), seaY, spin && i == 0, delay));
            delay += Random.Range(0.25f, 0.5f);
        }

        if (perf.fish != null && Vector3.Angle(view.forward, center - eye) < labelLookAngle)
            ShowLabel(perf.fish.DisplayName, center + Vector3.up * (height + 1.6f));
    }

    IEnumerator Jump(Jumper j, Performer perf, Vector3 start, Vector3 heading, float reach, float height, float sea, bool spin, float delay)
    {
        j.busy = true;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 실제 수면이 있으면 몸길이의 절반쯤 물속에서 출발해 물속으로 들어간다 — 수면이 몸을 가린다.
        float sink = this.sea != null && perf.fish != null ? perf.fish.bodyLength * perf.sizeMultiplier * 0.5f : 0f;
        start.y = sea - sink;
        float vy = Mathf.Sqrt(2f * Gravity * (height + sink));
        float duration = 2f * vy / Gravity;
        float surfaced = (vy - Mathf.Sqrt(Mathf.Max(0f, vy * vy - 2f * Gravity * sink))) / Gravity;   // 수면을 뚫는 순간
        Vector3 vh = heading * (reach / duration);
        Quaternion flat = Quaternion.LookRotation(heading, Vector3.up);

        bool burst = false, dove = false;
        j.body.gameObject.SetActive(true);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            Vector3 vel = vh + Vector3.up * (vy - Gravity * t);
            Vector3 pos = start + vh * t + Vector3.up * (vy * t - 0.5f * Gravity * t * t);
            Quaternion rot = Quaternion.Slerp(flat, Quaternion.LookRotation(vel, Vector3.up), perf.pitchFollow);
            if (spin) rot *= Quaternion.AngleAxis(360f * Mathf.SmoothStep(0f, 1f, t / duration), Vector3.forward);
            j.body.SetPositionAndRotation(pos, rot);
            if (j.anim != null) j.anim.SetVelocity(vel);
            // 물에서 나올 때는 물보라만, 다시 들어갈 때만 소리 — 여러 마리가 뛰면 소리가 연달아 터져 시끄러웠다
            if (!burst && t >= surfaced) { burst = true; Splash(new Vector3(pos.x, sea, pos.z), perf.splashSize * 0.7f, false); }
            if (!dove && t >= duration - surfaced) { dove = true; Splash(new Vector3(pos.x, sea, pos.z), perf.splashSize, true); }
            yield return null;
        }

        if (!dove)
        {
            Vector3 end = start + vh * duration;
            Splash(new Vector3(end.x, sea, end.z), perf.splashSize, true);
        }
        j.body.gameObject.SetActive(false);
        j.busy = false;
    }

    // ─────────────────────────────────────────────────────────────
    // 물보라 · 물결 고리 · 물소리
    // ─────────────────────────────────────────────────────────────

    void Splash(Vector3 at, float size, bool sound)
    {
        if (splash != null)
        {
            int n = Mathf.RoundToInt(16f + 26f * size);
            var e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector2 spot = Random.insideUnitCircle * 0.5f * size;
                Vector2 spray = Random.insideUnitCircle * 1.3f * size;
                e.position = at + new Vector3(spot.x, 0f, spot.y);
                e.velocity = new Vector3(spray.x, Random.Range(2.2f, 4.6f) * Mathf.Sqrt(size), spray.y);
                e.startSize = Random.Range(0.18f, 0.45f) * size;
                e.startLifetime = Random.Range(0.6f, 1.1f);
                e.startColor = new Color(1f, 1f, 1f, Random.Range(0.7f, 0.95f));
                splash.Emit(e, 1);
            }
        }

        Ring ring = rings[nextRing];
        nextRing = (nextRing + 1) % RingCount;
        if (ring != null && ring.renderer != null)
        {
            ring.transform.SetPositionAndRotation(at + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
            ring.size = size;
            ring.t = 0f;
            ring.renderer.enabled = true;
        }

        // 바로 앞 물소리와 0.35초 안이면 겹쳐 내지 않는다
        if (!sound || Time.time - lastSound < 0.35f) return;
        AudioSource voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % VoiceCount;
        AudioClip clip = size >= 1.2f ? splashBig : splashSmall;
        if (voice != null && clip != null)
        {
            lastSound = Time.time;
            voice.transform.position = at;
            voice.pitch = Random.Range(0.9f, 1.1f);
            voice.PlayOneShot(clip, Mathf.Clamp01(0.35f + 0.2f * size));
        }
    }

    void Update()
    {
        for (int i = 0; i < RingCount; i++)
        {
            Ring ring = rings[i];
            if (ring == null || ring.t < 0f) continue;
            ring.t += Time.deltaTime / 1.6f;
            if (ring.t >= 1f)
            {
                ring.t = -1f;
                ring.renderer.enabled = false;
                continue;
            }
            float grow = 1f - (1f - ring.t) * (1f - ring.t);
            float d = Mathf.Lerp(0.6f, 5f * ring.size, grow);
            ring.transform.localScale = new Vector3(d, d, 1f);
            ring.renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(1f, 1f, 1f, 0.55f * Mathf.Pow(1f - ring.t, 1.5f)));
            ring.renderer.SetPropertyBlock(block);
        }
        UpdateLabel();
    }

    // ─────────────────────────────────────────────────────────────
    // 이름표 — 뛰는 생물 위에 잠깐
    // ─────────────────────────────────────────────────────────────

    void ShowLabel(string text, Vector3 anchor)
    {
        if (label == null || labelText == null) return;
        labelText.text = text;
        labelAnchor = anchor;
        labelUntil = Time.time + labelSeconds;
    }

    void UpdateLabel()
    {
        if (label == null) return;
        bool on = Time.time < labelUntil;
        label.alpha = Mathf.MoveTowards(label.alpha, on ? 1f : 0f, Time.deltaTime * 3f);
        if (label.alpha <= 0.001f) return;
        Transform view = ResolveView();
        label.transform.position = labelAnchor;
        if (view == null) return;
        Vector3 away = labelAnchor - view.position;
        away.y = 0f;                                   // 몸만 돌리고 기울이지 않는다 (롤 0)
        if (away.sqrMagnitude > 1e-4f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    // ─────────────────────────────────────────────────────────────

    int PickPerformer()
    {
        float total = 0f;
        for (int i = 0; i < performers.Length; i++)
            if (pools[i].Count > 0) total += performers[i].weight;
        float r = Random.value * total;
        for (int i = 0; i < performers.Length; i++)
        {
            if (pools[i].Count == 0) continue;
            r -= performers[i].weight;
            if (r <= 0f) return i;
        }
        return -1;
    }

    Jumper Take(int performer)
    {
        foreach (Jumper j in pools[performer])
            if (!j.busy) return j;
        return null;
    }

    Transform ResolveView()
    {
        if (viewCache != null) return viewCache;
        Camera cam = Camera.main;
        if (cam != null) viewCache = cam.transform;
        return viewCache;
    }
}
