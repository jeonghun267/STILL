using UnityEngine;

/// <summary>
/// 곶과 물가 바위에 파도가 부딪혀 이는 흰 물보라. 바위 발치 몇 곳 중 하나를 골라 잠깐마다 물방울을 뿌린다.
/// 파티클은 스스로 뿌리지 않고(rate 0) 여기서 Emit 한다 — 창밖 점프 쇼의 물보라와 같은 방식, 같은 재질.
/// </summary>
public sealed class ShoreFoam : MonoBehaviour
{
    public ParticleSystem spray;
    public Transform[] spots;                           // 바위 발치 (수면 높이)
    public Vector2 interval = new Vector2(0.5f, 1.4f);
    public Vector2Int burst = new Vector2Int(8, 16);
    public float spread = 2.5f;                         // 한 번에 퍼지는 폭 (m)
    public Vector2 rise = new Vector2(2.5f, 6f);        // 위로 튀는 속도 (m/s)
    public Vector2 size = new Vector2(1.0f, 2.4f);
    public Vector2 life = new Vector2(1.0f, 1.8f);

    float next;

    void Update()
    {
        if (spray == null || spots == null || spots.Length == 0 || Time.time < next) return;
        next = Time.time + Random.Range(interval.x, interval.y);

        Transform spot = spots[Random.Range(0, spots.Length)];
        if (spot == null) return;
        int count = Random.Range(burst.x, burst.y + 1);
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 d = Random.insideUnitCircle * spread;
            p.position = spot.position + new Vector3(d.x, 0f, d.y);
            p.velocity = new Vector3(d.x * 0.6f, Random.Range(rise.x, rise.y), d.y * 0.6f);
            p.startSize = Random.Range(size.x, size.y);
            p.startLifetime = Random.Range(life.x, life.y);
            p.startColor = new Color(1f, 1f, 1f, Random.Range(0.55f, 0.9f));
            spray.Emit(p, 1);
        }
    }
}
