using UnityEngine;

/// <summary>
/// 애니메이션 없이 뼈대만 있는 생물(Pixit3D 복어 · 갑오징어 · 문어)을 헤엄치는 것처럼 — 몸을 좌우로 살짝 흔들고 위아래로 들썩인다.
/// 몸길이 1 로 맞춘 Model 에 붙는다(DomeAquarium/21). 위치 · 방향은 BoidManager 가 루트에 쓰므로 여기서는 Model 의 로컬만 건드린다.
/// </summary>
public sealed class SwimWiggle : MonoBehaviour
{
    public float yawDegrees = 7f;
    public float frequency = 1.2f;                      // 초당 흔들기
    public float bob = 0.04f;                           // 몸길이 비율

    Quaternion baseRotation;
    Vector3 basePosition;
    float phase;

    void Awake()
    {
        baseRotation = transform.localRotation;
        basePosition = transform.localPosition;
        phase = Random.value * 10f;
    }

    void Update()
    {
        float t = (Time.time + phase) * frequency * Mathf.PI * 2f;
        transform.localRotation = baseRotation * Quaternion.Euler(0f, Mathf.Sin(t) * yawDegrees, 0f);
        transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(t * 0.5f) * bob);
    }
}
