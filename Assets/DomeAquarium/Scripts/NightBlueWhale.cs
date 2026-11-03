using System.Collections;
using UnityEngine;

/// <summary>
/// 실제 크기로 보기 — 엔딩의 밤, "오늘 밤의 이야기" 판이 처음 뜬 뒤 로비 통유리창 밖 만에 대왕고래가 실제 크기(25m)로 한 번 지나간다.
/// 200m 에서 만난(수중 마이크로 노래를 들은) 바로 그 종. 등이 수면을 가르며 두 번 숨을 쉬고, 몸 둘레 물이 야광 플랑크톤처럼 푸르게 빛난다
/// — 밤바다에서 큰 몸이 움직이면 와편모류가 빛을 내 몸의 윤곽이 드러난다. 그 빛 덕분에 어두운 밤에도 크기가 보인다.
/// 길은 창에서 50m 남짓, 해안과 나란히(빌더가 정한다). 수심이 4m 남짓이라 몸은 수면 바로 아래.
/// </summary>
public sealed class NightBlueWhale : MonoBehaviour
{
    public Transform whale;                     // 모델 묶음 — 원점이 등 꼭대기, +Z 가 머리
    public Transform from, to;                  // 수면 높이의 길 (등 꼭대기 자리)
    public float seconds = 46f;
    public float dive = 2.5f;                   // 길 앞뒤에서 이만큼 아래에서 떠오르고 가라앉는다
    public ParticleSystem glow;                 // 몸 둘레 수면의 푸른 빛 (월드 시뮬레이션 — 뒤로 꼬리처럼 남는다)
    public ParticleSystem spout;
    public AudioSource blow;
    public float animSpeed = 0.3f;

    [TextArea(2, 4)] public string line =
        "잠깐 — {0} 보세요. 창밖 바다에 대왕고래예요. 오늘 200m 에서 만난 바로 그 고래, 이번엔 실제 크기 그대로예요. " +
        "몸길이 25m — 지금까지 알려진 가장 큰 동물이에요. 테라스로 나가면 더 잘 보여요.";

    public bool Played { get; private set; }
    public bool Swimming { get; private set; }

    void Awake()
    {
        if (whale != null) whale.gameObject.SetActive(false);
    }

    public Vector3 PathMiddle => from != null && to != null ? Vector3.Lerp(from.position, to.position, 0.5f) : transform.position;

    public IEnumerator Play()
    {
        if (Played || whale == null || from == null || to == null) yield break;
        Played = true;
        Swimming = true;
        whale.gameObject.SetActive(true);
        foreach (var a in whale.GetComponentsInChildren<Animator>(true)) a.speed = animSpeed;
        Vector3 a0 = from.position, b0 = to.position;
        Quaternion face = Quaternion.LookRotation(b0 - a0, Vector3.up);
        if (glow != null) glow.Play(true);
        bool blew1 = false, blew2 = false;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float u = t / seconds;
            // 처음 12% 동안 떠오르고, 마지막 12% 동안 머리부터 숙이며 가라앉는다
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, u)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, 1f, u)));
            Vector3 p = Vector3.Lerp(a0, b0, u) + Vector3.down * (dive * (1f - rise)) + Vector3.up * Mathf.Sin(t * 0.5f) * 0.08f;
            float pitch = u > 0.88f ? Mathf.InverseLerp(0.88f, 1f, u) * 12f : u < 0.12f ? -(1f - Mathf.InverseLerp(0f, 0.12f, u)) * 6f : 0f;
            whale.SetPositionAndRotation(p, face * Quaternion.Euler(pitch, 0f, 0f));
            if (!blew1 && rise > 0.9f) { blew1 = true; Breathe(); }         // 숨구멍이 수면을 가르는 순간
            if (!blew2 && u > 0.55f) { blew2 = true; Breathe(); }
            yield return null;
        }
        if (glow != null) glow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        whale.gameObject.SetActive(false);
        Swimming = false;
    }

    void Breathe()
    {
        if (spout != null) { spout.Clear(true); spout.Play(true); }
        if (blow != null && blow.clip != null)
        {
            var cam = Camera.main;
            float delay = cam != null ? Vector3.Distance(cam.transform.position, blow.transform.position) / 340f : 0f;
            blow.PlayDelayed(delay);
        }
    }

#if UNITY_EDITOR
    public void DebugReset() => Played = false;
#endif
}
