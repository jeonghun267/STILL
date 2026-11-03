using UnityEngine;

/// <summary>
/// 물에 뜬 것(부표 · 배)을 물결에 맞춰 천천히 들썩이고 기울인다. 처음 놓인 자리(로컬)를 기준으로 흔들 뿐 떠내려가지 않는다.
/// 위아래 · 좌우 · 앞뒤 흔들림의 주기를 조금씩 어긋나게 두어 같은 동작이 되풀이되는 티가 덜 나게 한다.
/// </summary>
public sealed class SeaBob : MonoBehaviour
{
    public float heave = 0.15f;        // 위아래 (m)
    public float roll = 4f;            // 좌우로 기우는 각도 (°)
    public float pitch = 2.5f;         // 앞뒤로 기우는 각도 (°)
    public float period = 4.5f;        // 한 번 들썩이는 시간 (s)

    Vector3 home;
    Quaternion rest;
    float phase;

    void Start()
    {
        home = transform.localPosition;
        rest = transform.localRotation;
        phase = Random.value * 20f;    // 여러 개가 같이 움직이지 않게
    }

    void Update()
    {
        float t = (Time.time + phase) * (2f * Mathf.PI / Mathf.Max(0.5f, period));
        transform.localPosition = home + Vector3.up * (Mathf.Sin(t) * heave);
        transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 0.83f + 1.3f) * pitch, 0f, Mathf.Sin(t * 0.61f) * roll);
    }
}
