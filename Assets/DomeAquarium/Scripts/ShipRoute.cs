using UnityEngine;

/// <summary>
/// 먼바다를 가로지르는 배. from 에서 to 까지 일정한 속도로 가고, 끝에 닿으면 from 으로 돌아가 다시 간다.
/// 두 끝은 곶 뒤(창에서 안 보이는 곳)에 두어 되돌아가는 순간이 보이지 않게 한다.
/// 배 몸은 자식에서 물결 따라 들썩인다(SeaBob) — 여기서는 자리와 방향만 정한다. 뱃머리는 모델의 +Z.
/// </summary>
public sealed class ShipRoute : MonoBehaviour
{
    public Vector3 from;                                // 부모 로컬
    public Vector3 to;
    public float speed = 5f;                            // m/s — 연안 화물선 10노트 남짓
    [Range(0f, 1f)] public float startAt;               // 처음 자리 (경로 비율) — 시작하자마자 창밖에 보이게

    float t;

    void Start()
    {
        t = startAt;
        Place();
    }

    void Update()
    {
        float length = Mathf.Max(1f, Vector3.Distance(from, to));
        t += speed * Time.deltaTime / length;
        if (t >= 1f) t -= 1f;
        Place();
    }

    void Place()
    {
        transform.localPosition = Vector3.Lerp(from, to, t);
        Vector3 d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
    }
}
