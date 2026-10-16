using UnityEngine;

/// <summary>
/// 로비의 연습 수조 — 몇 마리가 수조 안을 느긋하게 맴돌고, 관찰기가 돔의 무리처럼 겨눌 수 있게 한다.
///
/// 무리 시뮬레이션(BoidManager)은 월드 원점의 돔을 전제로 짜여 있어 작은 수조에 쓸 수 없다.
/// 여기서는 개체마다 서로 다른 타원 궤도를 돌린다 — 아이들이 처음 겨눠 보는 자리라
/// 예측할 수 있게 천천히, 유리 가까이로 지나가는 편이 낫다.
/// </summary>
public sealed class PracticeTank : MonoBehaviour, IObservationTargets
{
    [Tooltip("수조에 넣을 종. 같은 종을 두 번 넣으면 두 마리가 된다")]
    public FishData[] fish;
    [Tooltip("수조 안쪽 반 크기(m). 로컬 축 기준 — x 폭, y 높이, z 길이")]
    public Vector3 innerHalfSize = new Vector3(0.28f, 0.32f, 0.82f);
    public float speedScale = 0.55f;          // 종별 swimSpeed 대비 — 연습용이라 느리게

    Transform[] bodies;
    FishAnimatorSync[] anims;
    float[] radiusX, radiusZ, height, phase, angularSpeed, bob;
    Vector3[] lastPos;
    int count;

    public int AgentCount => count;

    void Start()
    {
        if (fish == null) return;
        count = fish.Length;
        bodies = new Transform[count];
        anims = new FishAnimatorSync[count];
        radiusX = new float[count];
        radiusZ = new float[count];
        height = new float[count];
        phase = new float[count];
        angularSpeed = new float[count];
        bob = new float[count];
        lastPos = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            FishData f = fish[i];
            if (f == null || f.prefab == null) continue;

            float margin = Mathf.Max(0.06f, f.bodyLength * 0.6f);
            radiusX[i] = Mathf.Max(0.05f, innerHalfSize.x - margin) * Random.Range(0.55f, 1f);
            radiusZ[i] = Mathf.Max(0.1f, innerHalfSize.z - margin) * Random.Range(0.6f, 1f);
            height[i] = Random.Range(-0.35f, 0.35f) * innerHalfSize.y;
            phase[i] = i * 2.4f + Random.Range(0f, 1f);
            bob[i] = Random.Range(0.4f, 0.9f);
            float speed = Mathf.Max(0.08f, f.swimSpeed * speedScale);
            float meanRadius = 0.5f * (radiusX[i] + radiusZ[i]);
            angularSpeed[i] = speed / Mathf.Max(0.05f, meanRadius) * (i % 2 == 0 ? 1f : -1f);   // 서로 엇갈려 돈다

            GameObject go = Instantiate(f.prefab, transform);
            go.name = "Practice_" + f.DisplayName + "_" + i;
            go.transform.localScale = Vector3.one * Mathf.Max(0.001f, f.swimScale);
            FishAnimatorSync sync = go.GetComponentInChildren<FishAnimatorSync>(true);
            if (sync == null) sync = go.AddComponent<FishAnimatorSync>();
            sync.bankAngle = 0f;
            bodies[i] = go.transform;
            anims[i] = sync;

            Vector3 p = LocalOrbit(i, 0f);
            go.transform.position = transform.TransformPoint(p);
            lastPos[i] = go.transform.position;
        }
    }

    Vector3 LocalOrbit(int i, float time)
    {
        float a = phase[i] + angularSpeed[i] * time;
        return new Vector3(Mathf.Cos(a) * radiusX[i],
                           height[i] + Mathf.Sin(a * bob[i] * 1.3f) * innerHalfSize.y * 0.18f,
                           Mathf.Sin(a) * radiusZ[i]);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float t = Time.time;
        for (int i = 0; i < count; i++)
        {
            Transform body = bodies[i];
            if (body == null) continue;
            Vector3 pos = transform.TransformPoint(LocalOrbit(i, t));
            Vector3 vel = (pos - lastPos[i]) / dt;
            lastPos[i] = pos;
            Quaternion rot = vel.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(vel, Vector3.up) : body.rotation;
            body.SetPositionAndRotation(pos, Quaternion.Slerp(body.rotation, rot, 1f - Mathf.Exp(-8f * dt)));
            if (anims[i] != null) anims[i].SetVelocity(vel);
        }
    }

    public bool TryGetAgent(int index, out Vector3 position, out FishData f)
    {
        position = Vector3.zero;
        f = null;
        if (bodies == null || index < 0 || index >= count || bodies[index] == null) return false;
        position = bodies[index].position;
        f = fish[index];
        return f != null;
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (direction.sqrMagnitude < 1e-8f) return -1;
        direction.Normalize();
        int best = -1;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (bodies == null || bodies[i] == null) continue;
            if (!ObservationCone.Contains(origin, direction, bodies[i].position, fish[i], maxDistance, coneDegrees, out float angle)) continue;
            if (angle < bestAngle) { bestAngle = angle; best = i; }
        }
        return best;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (bodies == null || index < 0 || index >= count || bodies[index] == null) return false;
        if (direction.sqrMagnitude < 1e-8f) return false;
        return ObservationCone.Contains(origin, direction.normalized, bodies[index].position, fish[index], maxDistance, coneDegrees, out _);
    }
}
