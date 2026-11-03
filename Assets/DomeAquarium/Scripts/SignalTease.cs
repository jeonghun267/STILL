using System.Collections;
using UnityEngine;

/// <summary>
/// 신호 예고 — 200m 에서 한 번, 먼 어둠 속에서 푸른 빛이 세 번 깜빡이고 사라진다. 1,000m 에서 만날 신호의 주인과 같은 깜빡임이라,
/// 내려가는 동안 "그게 저 아래에 있다"는 기대가 쌓인다 (DiveMission.signalTeaseAfter, DiveDirector 가 부른다).
/// 빛은 보던 방향에서 왼쪽, 돔 벽 가까이 · 눈높이보다 아래 — 고개를 돌려야 보이는 자리. 탐지 삑 소리가 그쪽에서 한 번 울린다.
/// </summary>
public sealed class SignalTease : MonoBehaviour
{
    public Renderer halo;                               // 얼굴을 향하는 판 (가산 · 부드러운 점)
    public AudioSource ping;
    public AudioClip pingClip;
    public Color color = new Color(0.35f, 0.65f, 1f, 1f);
    public float distance = 9f;                         // 눈에서 — 반지름 12m 수조의 벽 안쪽
    public float yaw = -55f;                            // 보던 방향에서 왼쪽으로
    public float drop = 1.2f;                           // 눈높이보다 조금 아래 — 더 내리면 200m 바위 더미에 가려진다
    public float blink = 0.55f;                         // 한 번 켜졌다 꺼지는 시간
    public float gap = 0.4f;
    public int count = 3;                               // 신호의 주인과 같은 세 번

    /// <summary>깜빡임이 다 끝나기까지(초).</summary>
    public float Duration => count * (blink + gap);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock mpb;
    Transform view;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (halo != null) halo.enabled = false;
    }

    public void Play(Transform viewer)
    {
        if (viewer == null || halo == null) return;
        view = viewer;
        StopAllCoroutines();
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        Vector3 f = view.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * f.normalized;
        transform.position = view.position + dir * distance + Vector3.down * drop;
        if (ping != null && pingClip != null) ping.PlayOneShot(pingClip);

        halo.enabled = true;
        for (int i = 0; i < count; i++)
        {
            for (float t = 0f; t < blink; t += Time.deltaTime)
            {
                Set(Mathf.Sin(t / blink * Mathf.PI));
                yield return null;
            }
            Set(0f);
            yield return new WaitForSeconds(gap);
        }
        halo.enabled = false;
    }

    void LateUpdate()
    {
        if (halo != null && halo.enabled && view != null)
            halo.transform.rotation = Quaternion.LookRotation(halo.transform.position - view.position, Vector3.up);   // 쿼드 앞면(-z)이 눈을 본다
    }

    void Set(float k)
    {
        halo.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, new Color(color.r * k, color.g * k, color.b * k, k));
        halo.SetPropertyBlock(mpb);
    }
}
