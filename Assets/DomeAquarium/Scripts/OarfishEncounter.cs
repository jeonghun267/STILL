using System.Collections;
using UnityEngine;

/// <summary>
/// 200m 아주 드문 만남 — 산갈치(Regalecus glesne). 잠수마다 정해진 확률로 한 번, 돔 가까이 4~5m 에 머리를 위로 하고 세로로 선 채
/// 등지느러미를 물결처럼 흔들며 천천히 떠오르다가, 다시 어둠 속으로 가라앉는다. 관찰기로 기록할 수 있다(200m 할 일에도 센다).
/// 실제 산갈치는 수 m 까지 자라는, 뼈 있는 물고기 중 가장 긴 물고기다 — 여기서는 4.5m.
/// 몸의 물결은 메시 꼭짓점을 코드로 흔든다(머리 쪽은 거의 그대로, 꼬리 쪽일수록 크게). 메시는 빌더가 세워서(+Y 가 머리, 물결은 로컬 X) 굽는다.
/// </summary>
public sealed class OarfishEncounter : MonoBehaviour, IObservationTargets
{
    public FishData fish;
    public Transform body;                      // 모델 — 원점이 꼬리 끝, +Y 가 머리, 옆구리가 ±Z (빌더가 세워 굽는다)
    public MeshFilter[] wave;                   // 물결칠 메시
    public float length = 4.5f;
    public Vector2 distance = new Vector2(3.8f, 4.6f);
    public float stay = 22f;                    // 눈앞에 머무는 시간
    public float waveAmplitude = 0.12f;
    public float waveLength = 1.6f;             // m
    public float waveSpeed = 0.9f;              // 물결이 몸을 타고 내려가는 빠르기 (m/s)

    Mesh[] meshes;
    Vector3[][] baseVerts, work;
    float[] minY, maxY;
    Vector3 from, to;
    bool showing, recordable;
    float shown;

    public bool Showing => showing;

    void Awake()
    {
        if (wave != null)
        {
            meshes = new Mesh[wave.Length];
            baseVerts = new Vector3[wave.Length][];
            work = new Vector3[wave.Length][];
            minY = new float[wave.Length];
            maxY = new float[wave.Length];
            for (int i = 0; i < wave.Length; i++)
            {
                if (wave[i] == null || wave[i].sharedMesh == null) continue;
                meshes[i] = Instantiate(wave[i].sharedMesh);
                meshes[i].MarkDynamic();
                wave[i].sharedMesh = meshes[i];
                baseVerts[i] = meshes[i].vertices;
                work[i] = new Vector3[baseVerts[i].Length];
                minY[i] = float.MaxValue; maxY[i] = float.MinValue;
                foreach (var v in baseVerts[i]) { minY[i] = Mathf.Min(minY[i], v.y); maxY[i] = Mathf.Max(maxY[i], v.y); }
            }
        }
        Hide();
    }

    void Hide()
    {
        showing = recordable = false;
        if (body != null) body.gameObject.SetActive(false);
    }

    /// <summary>눈(view) 앞 4~5m, 보던 쪽에서 오른쪽으로 비켜 아래에서 떠오른다.</summary>
    public IEnumerator Appear(Transform view)
    {
        if (body == null || view == null || showing) yield break;
        Vector3 f = view.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        Vector3 dir = Quaternion.Euler(0f, 35f, 0f) * f.normalized;
        float d = Random.Range(distance.x, distance.y);
        Vector3 c = view.position + dir * d;
        from = c + Vector3.down * (length * 1.4f);                           // 원점이 꼬리 끝 — 몸 전체가 아래 어둠 속
        to = c + Vector3.down * (length * 0.58f);                            // 머리가 눈높이보다 2m 남짓 위
        body.position = from;
        body.rotation = Quaternion.LookRotation(-dir, Vector3.up);            // 옆구리를 보이게 (Y 가 머리)
        body.gameObject.SetActive(true);
        showing = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 6f)                 // 천천히 떠오른다
        {
            body.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            recordable = t > 0.4f;
            yield return null;
        }
        recordable = true;
        for (shown = 0f; shown < stay; shown += Time.deltaTime)
        {
            body.position = to + Vector3.up * Mathf.Sin(shown * 0.35f) * 0.15f;
            yield return null;
        }
        recordable = false;
        Vector3 at = body.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 7f)                 // 꼬리부터 다시 어둠 속으로
        {
            body.position = Vector3.Lerp(at, from + Vector3.down * 2f, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        Hide();
    }

    public void Stop()
    {
        StopAllCoroutines();
        Hide();
    }

    void Update()
    {
        if (!showing || meshes == null) return;
        float t = Time.time;
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] == null) continue;
            var src = baseVerts[i];
            var dst = work[i];
            float span = Mathf.Max(0.001f, maxY[i] - minY[i]);
            float scale = body.lossyScale.y;
            for (int k = 0; k < src.Length; k++)
            {
                float u = (maxY[i] - src[k].y) / span;                         // 0 = 머리, 1 = 꼬리
                float s = (maxY[i] - src[k].y) * scale;                        // 머리에서 m
                float w = Mathf.Sin((s - t * waveSpeed) / waveLength * Mathf.PI * 2f) * waveAmplitude * Mathf.Lerp(0.15f, 1f, u) / Mathf.Max(0.001f, scale);
                dst[k] = src[k] + new Vector3(0f, 0f, w);                      // 납작한 옆구리(±Z) 쪽으로 물결
            }
            meshes[i].vertices = dst;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 관찰기 (IObservationTargets) — 몸 가운데를 겨눈다

    Vector3 Center => body != null ? body.position + Vector3.up * (length * 0.5f) : transform.position;

    public int AgentCount => 1;

    public bool TryGetAgent(int index, out Vector3 position, out FishData data)
    {
        position = Center;
        data = null;
        if (index != 0 || !recordable || fish == null) return false;
        data = fish;
        return true;
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (!recordable || fish == null || direction.sqrMagnitude < 1e-8f) return -1;
        return ObservationCone.Contains(origin, direction.normalized, Center, fish, maxDistance, coneDegrees, out _) ? 0 : -1;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (index != 0 || !recordable || fish == null || direction.sqrMagnitude < 1e-8f) return false;
        return ObservationCone.Contains(origin, direction.normalized, Center, fish, maxDistance, coneDegrees, out _);
    }
}
