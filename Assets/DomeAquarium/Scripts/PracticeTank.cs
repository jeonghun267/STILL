using UnityEngine;

/// <summary>
/// 로비의 연습 수조 — 몇 마리가 수조 안을 느긋하게 맴돌고, 관찰기가 돔의 무리처럼 겨눌 수 있게 한다.
///
/// 무리 시뮬레이션(BoidManager)은 월드 원점의 돔을 전제로 짜여 있어 작은 수조에 쓸 수 없다.
/// 여기서는 개체마다 서로 다른 타원 궤도를 돌린다 — 아이들이 처음 겨눠 보는 자리라
/// 예측할 수 있게 천천히, 유리 가까이로 지나가는 편이 낫다.
///
/// 해변의 조수 웅덩이도 이것을 쓴다. 수직 층위(bandCenter)가 crawlBand 이하인 종(게 · 불가사리 · 성게)은
/// 바닥을 기어 다닌다 — 위아래로 들썩이지 않고 몸을 수평으로 둔다.
/// 웅덩이에 손을 넣으면(Scare) 게는 가장자리 바위 밑으로 숨고 새우는 휙 달아난다. 불가사리 · 성게는 그대로 — 손을 빼고
/// 가만히 있으면 하나둘 다시 나온다. 숨은 동안은 관찰기로 겨눌 수 없다.
/// </summary>
public sealed class PracticeTank : MonoBehaviour, IObservationTargets
{
    [Tooltip("수조에 넣을 종. 같은 종을 두 번 넣으면 두 마리가 된다")]
    public FishData[] fish;
    [Tooltip("수조 안쪽 반 크기(m). 로컬 축 기준 — x 폭, y 높이, z 길이")]
    public Vector3 innerHalfSize = new Vector3(0.28f, 0.32f, 0.82f);
    public float speedScale = 0.55f;          // 종별 swimSpeed 대비 — 연습용이라 느리게
    [Range(0f, 1f)] public float crawlBand = 0.05f;

    [Header("손을 넣으면 (조수 웅덩이)")]
    [Tooltip("손이 이만큼 가까이 오면 놀라 달아나는 종 (speciesId)")]
    public string[] skittish = { "Crab", "Shrimp" };
    [Tooltip("숨는 자리 — 수조 반 크기의 몇 배 바깥(바위 턱 밑)까지")]
    public float hideReach = 1.3f;
    [Tooltip("숨을 때 바닥보다 이만큼 낮게 — 바위 턱 그늘 밑으로")]
    public float hideSink = 0.05f;
    public Vector2 hideSeconds = new Vector2(5f, 8f);

    Transform[] bodies;
    FishAnimatorSync[] anims;
    float[] radiusX, radiusZ, height, phase, angularSpeed, bob;
    bool[] crawl, flees;
    float[] hide, hideUntil;
    Vector3[] hideLocal;
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
        crawl = new bool[count];
        flees = new bool[count];
        hide = new float[count];
        hideUntil = new float[count];
        hideLocal = new Vector3[count];
        lastPos = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            FishData f = fish[i];
            if (f == null || f.prefab == null) continue;

            float margin = Mathf.Max(0.06f, f.bodyLength * 0.6f);
            radiusX[i] = Mathf.Max(0.05f, innerHalfSize.x - margin) * Random.Range(0.55f, 1f);
            radiusZ[i] = Mathf.Max(0.1f, innerHalfSize.z - margin) * Random.Range(0.6f, 1f);
            crawl[i] = f.bandCenter <= crawlBand;
            flees[i] = skittish != null && System.Array.IndexOf(skittish, f.speciesId) >= 0;
            height[i] = crawl[i] ? -innerHalfSize.y + f.bodyLength * 0.25f : Random.Range(-0.35f, 0.35f) * innerHalfSize.y;
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
        float lift = crawl[i] ? 0f : Mathf.Sin(a * bob[i] * 1.3f) * innerHalfSize.y * 0.18f;
        return new Vector3(Mathf.Cos(a) * radiusX[i], height[i] + lift, Mathf.Sin(a) * radiusZ[i]);
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
            Vector3 local = LocalOrbit(i, t);
            if (hideUntil[i] > 0f)
            {
                // 숨는 건 재빠르게(게 0.6초), 다시 나오는 건 천천히(3초)
                bool scared = t < hideUntil[i];
                hide[i] = Mathf.MoveTowards(hide[i], scared ? 1f : 0f, dt / (scared ? 0.6f : 3f));
                if (!scared && hide[i] <= 0f) hideUntil[i] = 0f;
                local = Vector3.Lerp(local, hideLocal[i], Mathf.SmoothStep(0f, 1f, hide[i]));
            }
            Vector3 pos = transform.TransformPoint(local);
            Vector3 vel = (pos - lastPos[i]) / dt;
            lastPos[i] = pos;
            Vector3 heading = crawl[i] ? Vector3.ProjectOnPlane(vel, transform.up) : vel;
            Quaternion rot = heading.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(heading, transform.up) : body.rotation;
            body.SetPositionAndRotation(pos, Quaternion.Slerp(body.rotation, rot, 1f - Mathf.Exp(-8f * dt)));
            if (anims[i] != null) anims[i].SetVelocity(vel);
        }
    }

    /// <summary>
    /// 손(worldPoint)이 물에 들어왔다 — radius 안의 겁 많은 종이 손에서 먼 쪽 가장자리로 달아난다.
    /// 손이 물에 있는 동안 계속 부르면 숨은 채로 있는다. 달아나기 시작한 마릿수를 돌려준다.
    /// </summary>
    public int Scare(Vector3 worldPoint, float radius)
    {
        int started = 0;
        float now = Time.time;
        Vector3 hand = transform.InverseTransformPoint(worldPoint);
        for (int i = 0; i < count; i++)
        {
            if (bodies == null || bodies[i] == null || !flees[i]) continue;
            Vector3 at = transform.InverseTransformPoint(bodies[i].position);
            Vector2 away = new Vector2(at.x - hand.x, at.z - hand.z);
            if (away.magnitude > radius) continue;
            bool fresh = hideUntil[i] <= 0f || now >= hideUntil[i];
            hideUntil[i] = now + Random.Range(hideSeconds.x, hideSeconds.y) * (crawl[i] ? 1f : 0.4f);
            if (!fresh && hide[i] > 0.05f) continue;                         // 이미 숨은 곳에 그대로
            // 손에서 먼 쪽 — 타원 둘레로 정규화한 방향
            Vector2 n = new Vector2(away.x / Mathf.Max(0.01f, innerHalfSize.x), away.y / Mathf.Max(0.01f, innerHalfSize.z));
            if (n.sqrMagnitude < 1e-4f) n = Random.insideUnitCircle;
            n.Normalize();
            float reach = crawl[i] ? hideReach : 0.85f;                        // 새우는 반대편 물속으로 휙
            float y = crawl[i] ? height[i] - hideSink : height[i];              // 게는 턱 밑 그늘로 살짝 낮게
            hideLocal[i] = new Vector3(n.x * innerHalfSize.x * reach, y, n.y * innerHalfSize.z * reach);
            started++;
        }
        return started;
    }

    /// <summary>바위 밑에 숨어 있는 동안은 겨눌 수 없다.</summary>
    bool Hidden(int i) => hide != null && hide[i] > 0.6f && crawl[i];

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
            if (bodies == null || bodies[i] == null || Hidden(i)) continue;
            if (!ObservationCone.Contains(origin, direction, bodies[i].position, fish[i], maxDistance, coneDegrees, out float angle)) continue;
            if (angle < bestAngle) { bestAngle = angle; best = i; }
        }
        return best;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (bodies == null || index < 0 || index >= count || bodies[index] == null || Hidden(index)) return false;
        if (direction.sqrMagnitude < 1e-8f) return false;
        return ObservationCone.Contains(origin, direction.normalized, bodies[index].position, fish[index], maxDistance, coneDegrees, out _);
    }
}
