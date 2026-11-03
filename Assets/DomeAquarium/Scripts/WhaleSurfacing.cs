using System.Collections;
using UnityEngine;

/// <summary>
/// 만 한가운데에 1~2분에 한 번 숨 쉬러 올라오는 혹등고래. 물 위로는 등과 등지느러미, 마지막에 꼬리만 보인다 —
/// 등은 혹등고래 모델(헤엄 애니메이션)을 물속에 깊게 넣어 꼭대기만 내밀고, 잠수할 때 드는 꼬리는 따로 만든 판이다(빌더).
/// 맨눈으로는 멀리 하얀 물기둥이 보이고, 테라스 쌍안경으로 당겨 보면 등과 꼬리가 보인다.
///
/// 한 번 올라오면: 숨(물기둥 + "푸우" 소리) → 등이 솟았다 가라앉음 × 2~3번 (6초 간격, 조금씩 앞으로) → 꼬리를 들며 깊이 잠수.
/// 소리는 거리만큼 늦게 닿는다(340 m/s). 보이는 동안만 쌍안경 대상(BinocularSubject)을 켠다.
/// </summary>
public sealed class WhaleSurfacing : MonoBehaviour
{
    [Header("부품 (빌더가 채운다)")]
    public Transform back;                              // 등 + 등지느러미 — 로컬 y 로 솟았다 가라앉는다
    public Transform fluke;                             // 꼬리 — 잠수할 때만 수직으로 든다
    public ParticleSystem spout;                        // 숨 (한 번에 터지는 물기둥)
    public AudioSource blow;                            // "푸우" (3D)
    public ParticleSystem splash;                       // 꼬리가 물에 들어갈 때 (월드 시뮬레이션, 해안 물보라와 같은 것)
    public BinocularSubject subject;

    [Header("어디서")]
    public Rect area = Rect.MinMaxRect(110f, -70f, 240f, 90f);   // 부모 로컬 x · z — 두 곶 사이 만, 부표보다 먼 곳
    public float seaY = -1.2f;

    [Header("언제")]
    public float firstDelay = 35f;                      // 로비 나레이션이 끝날 즈음
    public Vector2 interval = new Vector2(60f, 110f);
    public Vector2Int breaths = new Vector2Int(2, 3);
    public float breathGap = 6f;
    public float swimSpeed = 1.6f;                      // m/s — 숨 쉬는 동안 천천히 앞으로

    [Header("높이 (등 피벗의 로컬 y)")]
    public float backUp = 0f;                           // 수면 위로 등이 0.7m 쯤 나온다
    public float backDown = -2.6f;                      // 완전히 잠김
    public float flukeUp = 0.2f;
    public float flukeDown = -5f;

    [Header("모델")]
    public float animSpeed = 0.35f;                     // 헤엄 클립은 0.8초에 꼬리 한 번 — 14m 고래는 2~3초에 한 번이 맞다

    Transform view;
    bool visible;
    bool busy;

    // 수중 돔에 내려가면 로비(LobbySet)가 통째로 꺼져 코루틴이 멈춘다 — 엔딩의 밤 로비에서 다시 켜질 때 처음부터 다시 돈다.
    void OnEnable()
    {
        busy = false;
        Hide();
        foreach (var a in GetComponentsInChildren<Animator>(true)) a.speed = animSpeed;
        StartCoroutine(Loop());
    }

    /// <summary>디버그 · 쌍안경 확인용 — 지금 바로 올라온다.</summary>
    public void SurfaceNow()
    {
        if (busy) return;
        StopAllCoroutines();
        Hide();
        StartCoroutine(Loop(0f));
    }

    IEnumerator Loop(float firstWait = -1f)
    {
        yield return new WaitForSeconds(firstWait >= 0f ? firstWait : firstDelay);
        while (true)
        {
            yield return Surfacing();
            yield return new WaitForSeconds(Random.Range(interval.x, interval.y));
        }
    }

    IEnumerator Surfacing()
    {
        busy = true;
        transform.localPosition = new Vector3(Random.Range(area.xMin, area.xMax), seaY, Random.Range(area.yMin, area.yMax));
        transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        SetVisible(true);

        int n = Random.Range(breaths.x, breaths.y + 1);
        for (int i = 0; i < n; i++)
        {
            Breathe();
            yield return Rise(back, backDown, backUp, 1.1f, 0f, -4f);
            yield return Glide(2.4f);
            yield return Rise(back, backUp, backDown, 1.6f, -4f, 12f);   // 머리부터 숙이며 둥글게 가라앉는다
            if (i < n - 1) yield return Glide(Mathf.Max(0f, breathGap - 5.1f));
        }

        // 깊이 잠수 — 꼬리를 물 밖으로 들어 올렸다 미끄러지듯 넣는다
        yield return Glide(1.2f);
        yield return Rise(fluke, flukeDown, flukeUp, 1.8f, 35f, 0f);
        yield return Glide(1.0f);
        yield return Rise(fluke, flukeUp, flukeDown, 2.0f, 0f, -25f);
        Splash(fluke.position);
        SetVisible(false);
        busy = false;
    }

    void Breathe()
    {
        if (spout != null) { spout.Clear(true); spout.Play(true); }
        if (blow != null && blow.clip != null)
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
            float delay = view != null ? Vector3.Distance(view.position, transform.position) / 340f : 0f;
            blow.PlayDelayed(delay);
        }
    }

    /// <summary>part 의 로컬 y 를 from → to 로, 앞으로 헤엄치며 앞뒤로 숙인다(pitch).</summary>
    IEnumerator Rise(Transform part, float from, float to, float seconds, float pitchFrom, float pitchTo)
    {
        if (part == null) yield break;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            Vector3 p = part.localPosition;
            p.y = Mathf.Lerp(from, to, k);
            part.localPosition = p;
            part.localRotation = Quaternion.Euler(Mathf.Lerp(pitchFrom, pitchTo, k), 0f, 0f);
            Swim();
            yield return null;
        }
        Vector3 end = part.localPosition;
        end.y = to;
        part.localPosition = end;
    }

    IEnumerator Glide(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Swim();
            yield return null;
        }
    }

    void Swim() => transform.localPosition += transform.localRotation * Vector3.forward * (swimSpeed * Time.deltaTime);

    void Splash(Vector3 at)
    {
        if (splash == null) return;
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < 18; i++)
        {
            Vector2 d = Random.insideUnitCircle * 1.6f;
            e.position = new Vector3(at.x + d.x, transform.position.y, at.z + d.y);
            e.velocity = new Vector3(d.x, Random.Range(2.5f, 5f), d.y);
            e.startSize = Random.Range(1.2f, 2.6f);
            e.startLifetime = Random.Range(1.0f, 1.6f);
            e.startColor = new Color(1f, 1f, 1f, Random.Range(0.6f, 0.9f));
            splash.Emit(e, 1);
        }
    }

    void Hide()
    {
        if (back != null) back.localPosition = new Vector3(back.localPosition.x, backDown, back.localPosition.z);
        if (fluke != null) fluke.localPosition = new Vector3(fluke.localPosition.x, flukeDown, fluke.localPosition.z);
        SetVisible(false);
    }

    void SetVisible(bool on)
    {
        visible = on;
        if (subject != null && subject.enabled != on) subject.enabled = on;
    }

    public bool Visible => visible;
}
