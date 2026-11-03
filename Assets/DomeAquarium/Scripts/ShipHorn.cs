using UnityEngine;

/// <summary>
/// 먼바다 배의 뱃고동 — 1~2분에 한 번, 들릴 듯 말 듯하게. 소리는 거리만큼 늦게 닿는다(340 m/s, 400m 면 1.2초) —
/// 굴뚝 연기와 함께 보고 있으면 소리가 한 박자 늦게 온다.
/// 크기는 AudioSource 의 3D 거리 감쇠가 정한다 (빌더: 로그 감쇠, 최소 80m — 300~400m 에서 원래의 1/5 남짓).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class ShipHorn : MonoBehaviour
{
    public Vector2 interval = new Vector2(70f, 150f);
    public float firstDelay = 20f;                      // 로비 나레이션 첫 줄과 겹치지 않게
    public float speedOfSound = 340f;

    AudioSource source;
    Transform listener;
    float next;
    float playAt = -1f;

    void Start()
    {
        source = GetComponent<AudioSource>();
        next = Time.time + firstDelay + Random.Range(0f, interval.x * 0.5f);
    }

    void Update()
    {
        if (playAt >= 0f && Time.time >= playAt)
        {
            playAt = -1f;
            if (source != null && source.clip != null) source.Play();
        }
        if (Time.time < next) return;
        next = Time.time + Random.Range(interval.x, interval.y);

        if (listener == null && Camera.main != null) listener = Camera.main.transform;
        float delay = listener != null ? Vector3.Distance(listener.position, transform.position) / speedOfSound : 0f;
        playAt = Time.time + delay;
    }
}
