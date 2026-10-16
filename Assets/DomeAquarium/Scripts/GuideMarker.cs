using UnityEngine;

/// <summary>
/// 지금 할 일 위에 떠 있는 안내 표시 — 이름표와 아래를 가리키는 화살표가 천천히 오르내린다.
/// 보는 사람 쪽으로 몸(yaw)만 돌리고 기울이지는 않는다 (롤 0 — Meta·MS 가이드).
/// </summary>
public sealed class GuideMarker : MonoBehaviour
{
    public float bobAmplitude = 0.06f;
    public float bobSpeed = 1.6f;

    Vector3 basePosition;
    Transform viewCache;

    void Awake() => basePosition = transform.localPosition;   // 꺼졌다 켜져도 오르내린 높이가 쌓이지 않게 처음 한 번만

    void LateUpdate()
    {
        transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);

        if (viewCache == null && Camera.main != null) viewCache = Camera.main.transform;
        if (viewCache == null) return;
        Vector3 away = transform.position - viewCache.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }
}
