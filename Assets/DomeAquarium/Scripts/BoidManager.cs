using System;
using UnityEngine;

// 수조 형태. FishData.cs 의 FishRarity 와 같은 방식으로 파일 최상위에 선언한다.
public enum BoidShape
{
    Cylinder,
    Sphere
}

/// <summary>
/// 돔 수조 안의 물고기 떼 시뮬레이션.
///
/// 성능 설계 (Quest 2 / 72fps / 200개체 예산):
///  1) 위치·속도는 Transform 이 아니라 struct 배열(Agent[])에 들고, 프레임 끝에 Transform 으로 한 번만 밀어 넣는다.
///     Transform 은 읽기도 네이티브 호출이라 회전값도 struct 안에 캐싱한다.
///  2) 이웃 탐색은 O(n^2) 대신 "균일 그리드 + 카운팅 정렬" 을 쓴다.
///     - 왜 spatial hash(딕셔너리)가 아니라 균일 그리드인가:
///       수조가 고정 크기(반지름 12m, 높이 16m)라 셀 개수가 상수(약 6x4x6 = 144)로 미리 정해진다.
///       해시 충돌 처리도, Dictionary 도, List 도 필요 없고 int 배열 4개만 재사용하면 되므로 GC 할당이 0이다.
///       무한 공간이 아니면 해시보다 평평한 배열 인덱싱이 항상 싸다.
///     - 왜 "종별 인덱스 버킷 + 샘플링" 이 아닌가:
///       분리(separation)는 종을 가리지 않고 모든 이웃에 대해 필요하다. 종별 버킷만 두면 다른 종끼리
///       겹쳐 지나가는 걸 못 막는다. 그리드는 한 번 만들면 분리/정렬/응집을 모두 같은 순회로 처리한다.
///     - 매 프레임 그리드 재구성 비용은 O(n + cells) = 200 + 144 회 정수 연산이라 사실상 공짜다.
///  3) updateStride: 이웃 유래 조향(분리/정렬/응집)만 1/stride 개체씩 나눠 갱신하고 캐싱한다.
///     경계 조향·플레이어 회피·적분은 매 프레임 전원 수행한다. 이걸 stride 로 미루면
///     비갱신 프레임에 벽을 뚫고 나가거나 플레이어 얼굴로 파고들기 때문이다.
///  4) Update() 안에서 GetComponent / Find / LINQ / new 를 쓰지 않는다. 배열은 Rebuild() 에서만 할당한다.
///     예외는 무리 교체(5) — 몇 분에 한 번 들어오는 무리를 Instantiate 한다.
///  5) 무리 교체: 마릿수(= 드로우콜 예산)는 그대로 두고, 몇 분마다 rotation 종 무리 하나가 돔 벽(불투명 물 벽) 너머로
///     헤엄쳐 나가고, 아직 안 나온 종이 그 자리(같은 슬롯)를 받아 벽 너머에서 헤엄쳐 들어온다.
///     슬롯 수가 늘지 않으므로 처음 Rebuild 의 마릿수를 넘지 않는다. 작은 무리가 들어오면 남는 슬롯은 비워 둔다.
///
/// 배칭 전제:
///  스폰된 물고기는 매 프레임 움직이므로 static batching 대상이 아니다. 드로우콜을 30 이하로 유지하려면
///  각 종 프리팹의 Renderer 가 "같은 머티리얼 인스턴스를 공유"하고 그 머티리얼의 Enable GPU Instancing 이
///  켜져 있어야 한다. 런타임에 material 프로퍼티를 건드리면(예: renderer.material) 머티리얼이 복제되어
///  인스턴싱 배치가 깨지므로, 색 변화가 필요하면 MaterialPropertyBlock 을 쓸 것.
/// </summary>
public class BoidManager : MonoBehaviour, IObservationTargets
{
    const string ContainerName = "_Agents";

    [Header("수조 (씬 좌표계와 반드시 일치시킬 것)")]
    public BoidShape shape = BoidShape.Cylinder;
    public float radius = 12f;
    public float height = 16f;
    public float floorClearance = 0.8f;
    // 계약상 밴드 상단은 수면(+height/2) 이다. 기본값 0 이면 계약 그대로 동작한다.
    // 물고기 모델이 수면을 뚫는 게 보이면 0.2~0.4 정도만 올린다.
    public float surfaceClearance = 0f;
    public float playerClearance = 1.2f;
    public Transform player;

    [Header("개체")]
    public FishData[] species;
    public int updateStride = 4;
    public int maxAgents = 200;
    public int seed = 7777;

    [Header("무리 교체 — 몇 분마다 한 무리가 떠나고 아직 안 나온 종이 들어온다")]
    // species 중 여기 없는 종(볼거리·지목 생물)은 떠나지 않는다. 비면 교체하지 않는다.
    public FishData[] rotation;
    public Vector2 rotationInterval = new Vector2(100f, 160f);
    [Tooltip("돔 벽 바깥 이만큼에서 사라지고, 같은 거리에서 나타난다 (벽이 불투명 물이라 가려진다)")]
    public float rotationMargin = 2.5f;

    [Header("보이드 가중치")]
    public float neighborRadius = 4f;
    public float separationRadius = 1.2f;
    public float separationWeight = 1.6f;
    public float alignmentWeight = 0.9f;
    public float cohesionWeight = 0.55f;
    // 이 거리 안에서는 응집이 0 이다. 없으면 무리가 한 점으로 수렴해 다시는 흩어지지 않는다.
    public float cohesionInnerRadius = 2.0f;
    public float boundaryWeight = 3.0f;
    public float playerAvoidWeight = 4.0f;
    public float wanderWeight = 0.35f;
    public float wanderFrequency = 0.4f;
    // 한 마리가 한 번에 살펴볼 이웃 상한. 무리가 한 셀에 뭉쳐도 최악 비용이 선형으로 고정된다.
    public int neighborScanBudget = 24;

    [Header("이동")]
    [Range(0.1f, 1.5f)]
    [Tooltip("모든 생물의 종별 유영 속도에 곱하는 전역 배율")]
    public float globalSpeedMultiplier = 0.78f;
    public float boundarySoftness = 2.5f;
    public float wallMargin = 0.35f;
    public float minSpeedMultiplier = 0.45f;
    public float maxSpeedMultiplier = 1.6f;
    public float maxForceMultiplier = 3.0f;
    public float speedJitter = 0.15f;
    public float turnResponse = 8f;

    [Header("기즈모")]
    // 스폰한 물고기의 뱅킹 각도. 0 = 끔.
    // 프리팹마다 롤 축이 달라 요잉/피칭으로 잘못 걸리므로 기본값은 0 이다.
    public float bankAngle = 0f;

    public bool drawGizmos = true;

    // ---------------------------------------------------------------- 내부 상태

    struct Agent
    {
        public Vector3 pos;
        public Vector3 vel;
        public Quaternion rot;
        public Vector3 flockSteer;   // 매 프레임 flockTarget 쪽으로 수렴하는 실제 적용값
        public Vector3 flockTarget;  // updateStride 마다 갱신되는 이웃 유래 조향 목표
        public int species;
        public float minY;
        public float maxY;
        public float cruise;         // 종별 순항 속도 * 개체 지터
        public float sepRadius;      // 체장에 비례하는 개체별 분리 반경
        public float phase;          // wander 위상
        public byte state;           // Swim · Leaving · Entering · Empty
        public Vector3 travel;       // 떠날 때·들어올 때 향하는 수평 방향
    }

    const byte Swim = 0, Leaving = 1, Entering = 2, Empty = 3;
    static readonly Vector3 Parked = new Vector3(0f, -1000f, 0f);   // 빈 슬롯 — 이웃 반경 밖이라 무리 계산에 끼지 않는다

    // 한 종 무리가 쓰는 연속 슬롯. 종이 바뀌어도 슬롯 범위(capacity)는 그대로다.
    struct School
    {
        public int start;
        public int capacity;
        public int kind;
        public int live;             // 아직 돔 안팎에 보이는 개체 수 (떠나는 중이면 줄어든다)
        public float since;          // 들어온 시각 — 가장 오래 머문 무리부터 떠난다
    }

    Agent[] agents;
    Transform[] bodies;
    FishAnimatorSync[] anims;
    int agentCount;

    // 종 목록 = species 뒤에 rotation 의 나머지를 붙인 것. Agent.species 는 여기의 번호다.
    FishData[] kinds;
    bool[] rotates;
    bool[] shown;                    // 이번 탐사에 한 번이라도 들어온 종
    School[] schools;
    int schoolCount;
    int leavingSchool = -1;
    float leaveStarted;
    float nextRotation = float.PositiveInfinity;

    // 균일 그리드 (전부 Rebuild 에서만 할당, 매 프레임 재사용)
    int[] cellOf;
    int[] cellStart;    // 길이 cellTotal + 1. 카운트 -> 누적합 겸용
    int[] cellCursor;   // 길이 cellTotal
    int[] sorted;       // 길이 agentCount
    int gx, gy, gz, cellTotal;
    float invCell;
    float gridMinX, gridMinY, gridMinZ;

    Transform container;
    Vector3 playerPos;

    public int AgentCount { get { return agentCount; } }

    // ---------------------------------------------------------------- 조회 (관찰기 조준용)

    /// <summary>개체 하나의 현재 위치와 종. 개체에는 콜라이더가 없으므로 조준은 레이캐스트 대신 이 값으로 판정한다.</summary>
    public bool TryGetAgent(int index, out Vector3 position, out FishData fish)
    {
        position = Vector3.zero;
        fish = null;
        if (agents == null || index < 0 || index >= agentCount) return false;

        position = agents[index].pos;
        fish = SpeciesOf(index);
        return fish != null;
    }

    /// <summary>
    /// 조준선(origin → direction) 기준 coneDegrees 안에 들어온 개체 중 조준선과 각도가 가장 작은 것. 없으면 -1.
    /// 큰 생물은 몸 중심이 조준선에서 벗어나도 몸 어딘가를 겨누고 있으면 잡히도록, 체장의 절반이 차지하는 각도만큼 원뿔을 넓힌다.
    /// 200개체를 내적 몇 번으로 한 번 훑는 비용이라 그리드를 쓰지 않는다.
    /// </summary>
    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (agents == null || agentCount == 0) return -1;
        if (direction.sqrMagnitude < 1e-8f) return -1;
        direction.Normalize();

        int best = -1;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < agentCount; i++)
        {
            float angle;
            if (agents[i].state == Empty) continue;
            if (!AngleInCone(i, origin, direction, maxDistance, coneDegrees, out angle)) continue;
            if (angle < bestAngle) { bestAngle = angle; best = i; }
        }
        return best;
    }

    /// <summary>이미 잡고 있는 개체가 여전히 원뿔 안에 있는가. 무리 속에서 대상이 이웃으로 깜빡이며 바뀌지 않게 쓴다.</summary>
    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (agents == null || index < 0 || index >= agentCount) return false;
        if (direction.sqrMagnitude < 1e-8f || agents[index].state == Empty) return false;
        float angle;
        return AngleInCone(index, origin, direction.normalized, maxDistance, coneDegrees, out angle);
    }

    bool AngleInCone(int i, Vector3 origin, Vector3 dirNormalized, float maxDistance, float coneDegrees, out float angle)
        => ObservationCone.Contains(origin, dirNormalized, agents[i].pos, SpeciesOf(i), maxDistance, coneDegrees, out angle);

    FishData SpeciesOf(int index)
    {
        if (agents[index].state == Empty) return null;
        int s = agents[index].species;
        return (kinds != null && s >= 0 && s < kinds.Length) ? kinds[s] : null;
    }

    // ---------------------------------------------------------------- 라이프사이클

    void Awake()
    {
        if (player == null)
        {
            Camera cam = Camera.main;
            if (cam != null) player = cam.transform;
        }
    }

    void Start()
    {
        // struct 배열과 Transform 참조는 직렬화되지 않는다. 에디터에서 미리 뽑아 뒀더라도 런타임엔 반드시 재생성.
        // 로비에 있는 동안 꺼 두었다가(LobbySequence.domeSet) 처음 내려갈 때 켜면, 층 꾸미기(DiveDirector)가 이미 Rebuild 했다 — 두 번 만들지 않는다.
        if (agents == null || agentCount == 0) Rebuild();
    }

    void OnDestroy()
    {
        agents = null;
        bodies = null;
        anims = null;
        agentCount = 0;
    }

    // ---------------------------------------------------------------- 생성 / 파괴

    [ContextMenu("보이드 재생성")]
    public void Rebuild()
    {
        ClearAgents();

        if (species == null || species.Length == 0)
        {
            Debug.LogWarning("[BoidManager] species 배열이 비어 있어 생성할 개체가 없다.", this);
            return;
        }

        Vector3 ls = transform.lossyScale;
        if (Mathf.Abs(ls.x - 1f) > 0.01f || Mathf.Abs(ls.y - 1f) > 0.01f || Mathf.Abs(ls.z - 1f) > 0.01f)
        {
            Debug.LogWarning("[BoidManager] 이 오브젝트의 월드 스케일이 1이 아니다. 시뮬레이션은 월드 좌표로 도므로 스케일 1, 위치 원점 권장.", this);
        }

        int cap = Mathf.Max(0, maxAgents);
        int[] counts = new int[species.Length];   // Rebuild 에서만 할당한다 (매 프레임 아님)

        int wanted = 0;
        for (int i = 0; i < species.Length; i++)
        {
            FishData s = species[i];
            if (s == null)
            {
                Debug.LogWarning("[BoidManager] species[" + i + "] 가 null 이라 건너뛴다.", this);
                continue;
            }
            if (s.prefab == null)
            {
                Debug.LogWarning("[BoidManager] '" + s.DisplayName + "' 의 prefab 이 null 이라 건너뛴다.", this);
                continue;
            }
            counts[i] = Mathf.Max(0, s.schoolCount);
            wanted += counts[i];
        }

        if (wanted == 0)
        {
            Debug.LogWarning("[BoidManager] 생성 가능한 종이 없다 (prefab 누락이거나 schoolCount 가 0).", this);
            return;
        }

        int total = wanted;
        if (wanted > cap)
        {
            // 종별 비례 축소. 프리팹이 있는 종은 최소 1마리는 남긴다.
            float k = cap / (float)wanted;
            total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                counts[i] = Mathf.Max(1, Mathf.FloorToInt(counts[i] * k));
                total += counts[i];
            }
            // 반올림 잔여분은 가장 많은 종부터 1마리씩 깎는다.
            while (total > cap)
            {
                int big = -1;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] <= 0) continue;
                    if (big < 0 || counts[i] > counts[big]) big = i;
                }
                if (big < 0) break;
                counts[big]--;
                total--;
            }
            Debug.Log("[BoidManager] 요청 " + wanted + "마리가 maxAgents(" + cap + ")를 넘어 종별로 비례 축소했다. 최종 " + total + "마리.", this);
        }

        if (total <= 0) return;

        agents = new Agent[total];
        bodies = new Transform[total];
        anims = new FishAnimatorSync[total];
        cellOf = new int[total];
        sorted = new int[total];
        BuildKinds();
        schools = new School[species.Length];
        schoolCount = 0;

        BuildGridArrays();

        GameObject holder = new GameObject(ContainerName);
        container = holder.transform;
        container.SetParent(transform, false);
        container.localPosition = Vector3.zero;
        container.localRotation = Quaternion.identity;
        container.localScale = Vector3.one;

        UnityEngine.Random.State prevState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed);

        int n = 0;
        for (int i = 0; i < species.Length; i++)
        {
            int c = counts[i];
            if (c <= 0) continue;

            FishData s = species[i];
            float minY, maxY;
            ComputeBand(s, out minY, out maxY);

            schools[schoolCount++] = new School { start = n, capacity = Mathf.Min(c, total - n), kind = i, live = Mathf.Min(c, total - n), since = 0f };
            shown[i] = true;

            for (int m = 0; m < c && n < total; m++, n++)
            {
                Vector3 pos = RandomPointInBand(minY, maxY);
                Vector3 dir = RandomHorizontalDirection();
                Spawn(n, i, m, pos, dir, minY, maxY, Swim);
            }
        }

        UnityEngine.Random.state = prevState;
        agentCount = n;
        leavingSchool = -1;
        ScheduleRotation();
    }

    /// <summary>species 뒤에 rotation 중 아직 없는 종을 붙인다. species 번호는 그대로라 Agent.species 가 바로 쓴다.</summary>
    void BuildKinds()
    {
        int extra = rotation != null ? rotation.Length : 0;
        var list = new FishData[species.Length + extra];
        int k = 0;
        for (int i = 0; i < species.Length; i++) list[k++] = species[i];
        for (int i = 0; i < extra; i++)
        {
            FishData f = rotation[i];
            if (f == null || f.prefab == null || Array.IndexOf(list, f, 0, k) >= 0) continue;
            list[k++] = f;
        }
        kinds = new FishData[k];
        Array.Copy(list, kinds, k);
        rotates = new bool[k];
        shown = new bool[k];
        for (int i = 0; i < k; i++)
            rotates[i] = kinds[i] != null && rotation != null && Array.IndexOf(rotation, kinds[i]) >= 0;
    }

    /// <summary>슬롯 n 에 kind 종 한 마리를 낸다. 처음 채울 때와 무리가 바뀔 때 같이 쓴다.</summary>
    void Spawn(int n, int kind, int m, Vector3 pos, Vector3 dir, float minY, float maxY, byte state)
    {
        FishData s = kinds[kind];
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        float baseSpeed = Mathf.Max(0.05f, s.swimSpeed * Mathf.Clamp(globalSpeedMultiplier, 0.1f, 1.5f));
        float scale = Mathf.Max(0.001f, s.swimScale);

        GameObject go = Instantiate(s.prefab, container);
        go.name = s.DisplayName + "_" + m.ToString("000");
        // 부모 스케일에 휘둘리지 않게 월드 기준으로 다시 앉힌다.
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(scale, scale, scale);

        // 스폰 시 한 번만 캐싱한다 (Update 에서는 절대 GetComponent 하지 않는다).
        // 프리팹에 없으면 붙여 준다 — 빌더가 프리팹마다 수동으로 달 필요가 없게.
        FishAnimatorSync sync = go.GetComponentInChildren<FishAnimatorSync>(true);
        if (sync == null) sync = go.AddComponent<FishAnimatorSync>();

        // 뱅킹(롤)을 끈다.
        // FishAnimatorSync 는 transform.GetChild(0) 을 롤 축으로 삼는데, Layer Lab 프리팹마다
        // 그 자식이 다르다 — 어떤 종은 SkinnedMeshRenderer(효과 없음), 어떤 종은 Rx(-90) 보정
        // 노드라 로컬 Z 롤이 실제로는 ±35° 요잉이 되고, 돌고래는 ±35° 피칭이 된다.
        // 그래서 "삐걱대는" 움직임이 났다. 헤엄 애니메이션은 Animator 가 따로 돌리므로
        // 뱅킹 없이도 자연스럽다.
        sync.bankAngle = bankAngle;

        float jitter = 1f + UnityEngine.Random.Range(-speedJitter, speedJitter);
        float cruise = baseSpeed * Mathf.Max(0.1f, jitter);

        agents[n].pos = pos;
        agents[n].rot = rot;
        agents[n].vel = dir * cruise;
        agents[n].flockSteer = Vector3.zero;
        agents[n].flockTarget = Vector3.zero;
        agents[n].species = kind;
        agents[n].minY = minY;
        agents[n].maxY = maxY;
        agents[n].cruise = cruise;
        agents[n].phase = UnityEngine.Random.Range(0f, 100f);
        // 어류 무리의 최근접 간격은 통상 1~2 체장이다. 분리력이 감쇠형이라
        // 그 2배 지점까지 미쳐야 실제 간격이 1~2 체장으로 수렴한다.
        agents[n].sepRadius = Mathf.Clamp(Mathf.Max(0.02f, s.bodyLength) * 3f, 0.35f, 3.5f);
        agents[n].state = state;
        agents[n].travel = dir;

        bodies[n] = go.transform;
        anims[n] = sync;
    }

    /// <summary>모든 개체를 치운다. 늘 헤엄치는 무리 없이 큰 생물만 가끔 지나가는 층(200m)에서 쓴다.</summary>
    public void Clear()
    {
        ClearAgents();
    }

    void ClearAgents()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c == null || c.name != ContainerName) continue;

            // 플레이 모드의 Destroy 는 지연 파괴라, 이름을 바꿔 새 컨테이너와 헷갈리지 않게 한다.
            c.name = ContainerName + "_dead";
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }

        container = null;
        agents = null;
        bodies = null;
        anims = null;
        cellOf = null;
        sorted = null;
        agentCount = 0;
        schools = null;
        schoolCount = 0;
        leavingSchool = -1;
        nextRotation = float.PositiveInfinity;
    }

    void ComputeBand(FishData s, out float minY, out float maxY)
    {
        float floorY = -height * 0.5f + Mathf.Max(0f, floorClearance);
        float ceilY = height * 0.5f - Mathf.Max(0f, surfaceClearance);
        if (ceilY < floorY)
        {
            float mid = (ceilY + floorY) * 0.5f;
            floorY = mid;
            ceilY = mid;
        }

        float span = ceilY - floorY;
        float center = floorY + Mathf.Clamp01(s.bandCenter) * span;
        float half = 0.5f * Mathf.Clamp01(s.bandWidth) * span;

        minY = Mathf.Max(floorY, center - half);
        maxY = Mathf.Min(ceilY, center + half);

        if (maxY - minY < 0.3f)
        {
            float mid = (minY + maxY) * 0.5f;
            minY = Mathf.Max(floorY, mid - 0.15f);
            maxY = Mathf.Min(ceilY, mid + 0.15f);
        }
    }

    Vector3 RandomPointInBand(float minY, float maxY)
    {
        float y = UnityEngine.Random.Range(minY, maxY);
        float rLimit = Mathf.Max(0.5f, radius - wallMargin - 0.5f);

        for (int tries = 0; tries < 8; tries++)
        {
            float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            // sqrt 로 균일 분포
            float r = Mathf.Sqrt(UnityEngine.Random.value) * rLimit;
            Vector3 p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);

            if (shape == BoidShape.Sphere)
            {
                float lim = Mathf.Max(0.5f, radius - wallMargin);
                if (p.sqrMagnitude > lim * lim) continue;
            }
            float clear = Mathf.Max(0f, playerClearance) + 0.6f;
            if (p.sqrMagnitude < clear * clear) continue;
            return p;
        }
        return new Vector3(rLimit * 0.6f, y, 0f);
    }

    Vector3 RandomHorizontalDirection()
    {
        float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    // ---------------------------------------------------------------- 무리 교체

    const float LeaveTimeout = 60f;   // 이 안에 다 못 나가면(벽에 끼는 등) 남은 개체를 그 자리에서 비운다

    void ScheduleRotation()
    {
        bool any = false;
        for (int k = 0; rotates != null && k < rotates.Length && !any; k++) any = rotates[k];
        nextRotation = any && Application.isPlaying
            ? Time.time + UnityEngine.Random.Range(Mathf.Max(5f, rotationInterval.x), Mathf.Max(5f, rotationInterval.y))
            : float.PositiveInfinity;
    }

    void UpdateRotation()
    {
        if (schools == null) return;
        if (leavingSchool >= 0)
        {
            ref School s = ref schools[leavingSchool];
            if (s.live > 0 && Time.time - leaveStarted < LeaveTimeout) return;
            ArriveNext(leavingSchool);
            leavingSchool = -1;
            ScheduleRotation();
            return;
        }
        if (Time.time >= nextRotation && !BeginLeaving()) ScheduleRotation();
    }

    /// <summary>가장 오래 머문 교체 대상 무리를 벽 너머로 내보낸다. 들어올 종이 없으면 false.</summary>
    bool BeginLeaving()
    {
        if (PickIncoming(int.MaxValue, -1) < 0) return false;

        int pick = -1;
        for (int i = 0; i < schoolCount; i++)
        {
            School s = schools[i];
            if (s.live <= 0 || !rotates[s.kind]) continue;
            if (pick < 0 || s.since < schools[pick].since ||
                (s.since == schools[pick].since && UnityEngine.Random.value < 0.5f)) pick = i;
        }
        if (pick < 0) return false;

        // 무리 한가운데에서 가장 가까운 벽 쪽으로 — 관람객 얼굴 앞을 가로지르지 않는다.
        School leaving = schools[pick];
        Vector3 c = Vector3.zero;
        for (int n = leaving.start; n < leaving.start + leaving.capacity; n++)
            if (agents[n].state != Empty) c += agents[n].pos;
        c.y = 0f;
        Vector3 outward = c.sqrMagnitude > 1e-4f ? c.normalized : RandomHorizontalDirection();
        for (int n = leaving.start; n < leaving.start + leaving.capacity; n++)
        {
            if (agents[n].state == Empty) continue;
            agents[n].state = Leaving;
            agents[n].travel = outward;
        }
        leavingSchool = pick;
        leaveStarted = Time.time;
        return true;
    }

    /// <summary>
    /// 지금 돔에 없는 교체 종 하나. 이번 탐사에 아직 안 나온 종을 먼저, 그다음 무리 크기가 슬롯 수에 가까운 종을 고른다.
    /// slots 가 int.MaxValue 면 크기는 보지 않는다(들어올 종이 있는지만 확인). exclude 는 방금 떠난 종. 없으면 -1.
    /// </summary>
    int PickIncoming(int slots, int exclude)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int k = 0; k < kinds.Length; k++)
        {
            if (!rotates[k] || k == exclude || IsPresent(k)) continue;
            float score = (shown[k] ? 1000f : 0f) + UnityEngine.Random.value;
            if (slots != int.MaxValue) score += Mathf.Abs(Mathf.Max(1, kinds[k].schoolCount) - slots) * 10f;
            if (score < bestScore) { bestScore = score; best = k; }
        }
        return best;
    }

    bool IsPresent(int kind)
    {
        for (int i = 0; i < schoolCount; i++)
            if (schools[i].kind == kind && schools[i].live > 0) return true;
        return false;
    }

    /// <summary>비운 슬롯에 새 종 무리를 벽 너머 한 곳에 모아 내고, 돔 안쪽으로 헤엄쳐 들어오게 한다.</summary>
    void ArriveNext(int school)
    {
        ref School s = ref schools[school];
        int left = s.kind;
        for (int n = s.start; n < s.start + s.capacity; n++)
        {
            if (bodies[n] != null) Destroy(bodies[n].gameObject);
            bodies[n] = null;
            anims[n] = null;
            agents[n].state = Empty;
            agents[n].pos = Parked;
        }
        s.live = 0;

        int kind = PickIncoming(s.capacity, left);
        if (kind < 0) return;
        FishData f = kinds[kind];
        int count = Mathf.Clamp(f.schoolCount, 1, s.capacity);
        float minY, maxY;
        ComputeBand(f, out minY, out maxY);

        Vector3 from = RandomHorizontalDirection();
        Vector3 gate = from * (radius + Mathf.Max(0.5f, rotationMargin) - 0.3f);
        for (int m = 0; m < count; m++)
        {
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * 1.2f;
            Vector3 pos = gate + new Vector3(jitter.x, 0f, jitter.y);
            pos.y = UnityEngine.Random.Range(minY, maxY);
            Spawn(s.start + m, kind, m, pos, -from, minY, maxY, Entering);
        }
        s.kind = kind;
        s.live = count;
        s.since = Time.time;
        shown[kind] = true;
    }

    int SchoolOf(int agent)
    {
        for (int i = 0; i < schoolCount; i++)
            if (agent >= schools[i].start && agent < schools[i].start + schools[i].capacity) return i;
        return 0;
    }

    // ---------------------------------------------------------------- 시뮬레이션

    void Update()
    {
        if (agentCount == 0 || agents == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (dt > 0.05f) dt = 0.05f;   // 히치 났을 때 물고기가 순간이동하지 않도록

        playerPos = player != null ? player.position : Vector3.zero;

        UpdateRotation();
        BuildGrid();

        int stride = Mathf.Max(1, updateStride);
        int phase = Time.frameCount % stride;
        float time = Time.time;
        float lerp = 1f - Mathf.Exp(-Mathf.Max(0.01f, turnResponse) * dt);
        float clearSq = Mathf.Max(0f, playerClearance) * Mathf.Max(0f, playerClearance);

        float wallSq = radius * radius;
        float goneSq = (radius + Mathf.Max(0.5f, rotationMargin)) * (radius + Mathf.Max(0.5f, rotationMargin));
        float insideR = Mathf.Max(0.1f, radius - Mathf.Max(0.2f, boundarySoftness));

        for (int i = 0; i < agentCount; i++)
        {
            byte state = agents[i].state;
            if (state == Empty) continue;

            // 1/stride 만 이웃 탐색을 갱신한다. 다만 결과를 **그대로 갈아끼우면**
            // 4프레임마다 조향이 계단식으로 튀어 유영이 삐걱거린다.
            // 새 값은 목표로만 두고, 매 프레임 그쪽으로 부드럽게 수렴시킨다.
            if (i % stride == phase) agents[i].flockTarget = ComputeFlockSteer(i);
            agents[i].flockSteer = Vector3.Lerp(agents[i].flockSteer, agents[i].flockTarget, lerp);

            Vector3 pos = agents[i].pos;
            Vector3 vel = agents[i].vel;
            float cruise = agents[i].cruise;

            Vector3 steer = agents[i].flockSteer;
            steer += BoundarySteer(pos, agents[i].minY, agents[i].maxY, cruise, state == Swim);
            // 떠나는 무리는 벽 바깥으로, 들어오는 무리는 돔 안쪽으로 곧게 헤엄친다 (수평 경계는 잠시 무시).
            if (state != Swim) steer += agents[i].travel * (cruise * boundaryWeight);
            steer += PlayerSteer(pos, cruise, clearSq);
            steer += WanderSteer(time, agents[i].phase, cruise);

            steer = Vector3.ClampMagnitude(steer, cruise * Mathf.Max(0.1f, maxForceMultiplier));

            vel += steer * dt;

            float sp = vel.magnitude;
            float maxS = cruise * Mathf.Max(0.2f, maxSpeedMultiplier);
            float minS = cruise * Mathf.Clamp(minSpeedMultiplier, 0.05f, 1f);
            if (sp > maxS) vel *= maxS / sp;
            else if (sp < minS)
            {
                if (sp > 1e-4f) vel *= minS / sp;
                else vel = new Vector3(0f, 0f, minS);
            }

            pos += vel * dt;
            ClampInside(ref pos, ref vel, agents[i].minY, agents[i].maxY, state == Swim);
            PushOutOfPlayer(ref pos);

            float h2 = pos.x * pos.x + pos.z * pos.z;
            if (state == Leaving && h2 > goneSq)
            {
                // 벽 너머 물 속으로 사라졌다 — 슬롯을 비운다. 마지막 한 마리가 나가면 UpdateRotation 이 새 무리를 들인다.
                agents[i].state = Empty;
                agents[i].pos = Parked;
                if (bodies[i] != null) bodies[i].gameObject.SetActive(false);
                schools[SchoolOf(i)].live--;
                continue;
            }
            if (state == Entering)
            {
                if (h2 < insideR * insideR) agents[i].state = Swim;
                else if (h2 > 1e-4f) agents[i].travel = new Vector3(-pos.x, 0f, -pos.z) * (1f / Mathf.Sqrt(h2));   // 분리력에 밀려도 중심을 향한다
            }

            // 회전은 struct 에 캐싱해 Transform 읽기를 없앤다.
            Quaternion rot = agents[i].rot;
            if (vel.sqrMagnitude > 1e-6f)
            {
                Quaternion target = Quaternion.LookRotation(vel, Vector3.up);
                rot = Quaternion.Slerp(rot, target, lerp);
            }

            agents[i].pos = pos;
            agents[i].vel = vel;
            agents[i].rot = rot;

            Transform t = bodies[i];
            if (t != null) t.SetPositionAndRotation(pos, rot);

            FishAnimatorSync a = anims[i];
            if (a != null) a.SetVelocity(vel);
        }
    }

    // 이웃 유래 조향: 분리(모든 종) + 정렬/응집(같은 종만)
    Vector3 ComputeFlockSteer(int i)
    {
        Vector3 p = agents[i].pos;
        Vector3 v = agents[i].vel;
        int myS = agents[i].species;
        float cruise = agents[i].cruise;

        float nr = Mathf.Max(0.1f, neighborRadius);
        // 분리 반경은 개체 크기에 비례해야 한다.
        // 전역 1.2m 하나로 쓰면 몸길이 1.8m 인 참다랑어는 서로 관통해 한 덩어리로 보이고,
        // 몸길이 5cm 인 고비는 1.2m 씩 벌어져 무리로 보이지 않는다 (체장 차이가 36배다).
        float sr = Mathf.Max(0.05f, agents[i].sepRadius > 0f ? agents[i].sepRadius : separationRadius);
        float nr2 = nr * nr;
        float sr2 = sr * sr;

        Vector3 sep = Vector3.zero;
        Vector3 aliSum = Vector3.zero;
        Vector3 cohSum = Vector3.zero;
        int sameCount = 0;
        int scanned = 0;
        int budget = Mathf.Max(4, neighborScanBudget);

        int cx = CellAxis(p.x - gridMinX, gx);
        int cy = CellAxis(p.y - gridMinY, gy);
        int cz = CellAxis(p.z - gridMinZ, gz);

        int x0 = cx > 0 ? cx - 1 : 0;
        int x1 = cx < gx - 1 ? cx + 1 : gx - 1;
        int y0 = cy > 0 ? cy - 1 : 0;
        int y1 = cy < gy - 1 ? cy + 1 : gy - 1;
        int z0 = cz > 0 ? cz - 1 : 0;
        int z1 = cz < gz - 1 ? cz + 1 : gz - 1;

        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                int rowBase = (y * gz + z) * gx;
                for (int x = x0; x <= x1; x++)
                {
                    int cell = rowBase + x;
                    int end = cellStart[cell + 1];
                    for (int k = cellStart[cell]; k < end; k++)
                    {
                        int j = sorted[k];
                        if (j == i) continue;

                        Vector3 d = p - agents[j].pos;
                        float d2 = d.sqrMagnitude;
                        if (d2 > nr2) continue;

                        scanned++;
                        if (scanned > budget) goto Done;

                        if (d2 < sr2 && d2 > 1e-6f) sep += d / d2;

                        if (agents[j].species == myS)
                        {
                            aliSum += agents[j].vel;
                            cohSum += agents[j].pos;
                            sameCount++;
                        }
                    }
                }
            }
        }

    Done:
        ;   // 라벨 뒤 빈 문장 (스캔 예산 초과 시 중첩 루프를 한 번에 빠져나온다)

        Vector3 steer = Vector3.zero;

        if (sep.sqrMagnitude > 1e-8f)
        {
            steer += sep.normalized * (cruise * separationWeight);
        }

        if (sameCount > 0)
        {
            float inv = 1f / sameCount;

            Vector3 avgVel = aliSum * inv;
            if (avgVel.sqrMagnitude > 1e-8f)
            {
                steer += (avgVel.normalized * cruise - v) * alignmentWeight;
            }

            // ★ 응집에 거리 감쇠를 넣는다.
            // 예전엔 toCenter.normalized * cruise 였는데, 그러면 중심에서 3.9m 든 0.1m 든
            // 똑같은 최대 세기로 당긴다. 되밀 수 있는 힘은 separationRadius 안에서만 작동하므로
            // 그 사이 구간에는 안쪽으로 당기는 힘만 존재한다 = 한 번 이웃이 되면 절대 못 빠져나온다.
            // 그래서 무리가 지름 3m대 공으로 영구히 뭉쳐 버렸다.
            // 안쪽 반경에서 0, neighborRadius 에서 1 로 감쇠시키면 무리가 늘었다 줄었다 호흡한다.
            Vector3 toCenter = cohSum * inv - p;
            float cd2 = toCenter.sqrMagnitude;
            if (cd2 > 1e-6f)
            {
                float cd = Mathf.Sqrt(cd2);
                float ct = Mathf.InverseLerp(cohesionInnerRadius, neighborRadius, cd);
                if (ct > 0f)
                    steer += (toCenter * (cruise * ct / cd) - v) * cohesionWeight;
            }
        }

        return steer;
    }

    Vector3 BoundarySteer(Vector3 p, float minY, float maxY, float cruise, bool walls)
    {
        Vector3 steer = Vector3.zero;
        float soft = Mathf.Max(0.2f, boundarySoftness);
        float w = cruise * boundaryWeight;

        // walls 가 꺼지면(떠나는·들어오는 무리) 수직 밴드만 지킨다.
        if (walls && shape == BoidShape.Cylinder)
        {
            float h2 = p.x * p.x + p.z * p.z;
            float start = Mathf.Max(0.1f, radius - soft);
            if (h2 > start * start)
            {
                float h = Mathf.Sqrt(h2);
                float t = Mathf.Clamp01((h - start) / soft);
                float invH = 1f / h;
                steer.x += -p.x * invH * t * w;
                steer.z += -p.z * invH * t * w;
            }
        }
        else if (walls)
        {
            float d2 = p.sqrMagnitude;
            float start = Mathf.Max(0.1f, radius - soft);
            if (d2 > start * start)
            {
                float d = Mathf.Sqrt(d2);
                float t = Mathf.Clamp01((d - start) / soft);
                steer += (-p / d) * (t * w);
            }
        }

        // 수직 밴드
        float vsoft = Mathf.Min(soft, Mathf.Max(0.2f, (maxY - minY) * 0.35f));
        if (p.y > maxY - vsoft)
        {
            float t = Mathf.Clamp01((p.y - (maxY - vsoft)) / vsoft);
            steer.y -= t * w;
        }
        else if (p.y < minY + vsoft)
        {
            float t = Mathf.Clamp01(((minY + vsoft) - p.y) / vsoft);
            steer.y += t * w;
        }

        return steer;
    }

    Vector3 PlayerSteer(Vector3 p, float cruise, float clearSq)
    {
        if (clearSq <= 0f) return Vector3.zero;

        Vector3 d = p - playerPos;
        float d2 = d.sqrMagnitude;
        float outer = clearSq * 4f;   // 반경의 2배부터 부드럽게 비켜간다
        if (d2 > outer) return Vector3.zero;

        float dist = Mathf.Sqrt(d2);
        if (dist < 1e-4f) return new Vector3(0f, 0f, cruise * playerAvoidWeight);

        float clear = Mathf.Sqrt(clearSq);
        float t = Mathf.Clamp01(1f - (dist - clear) / Mathf.Max(0.01f, clear));
        return (d / dist) * (t * cruise * playerAvoidWeight);
    }

    Vector3 WanderSteer(float time, float phase, float cruise)
    {
        float t = time * Mathf.Max(0f, wanderFrequency) + phase;
        float w = cruise * wanderWeight;
        return new Vector3(
            Mathf.Sin(t) * w,
            Mathf.Sin(t * 0.73f) * w * 0.35f,
            Mathf.Cos(t * 1.31f) * w);
    }

    /// <summary>
    /// 안전망 클램프. 위치만 되돌리고 속도를 그대로 두면 다음 프레임에도 같은 방향으로 밀어붙여
    /// 경계에 "갈리는" 떨림이 생긴다. 그래서 바깥으로 향하던 속도 성분을 함께 죽인다.
    /// </summary>
    void ClampInside(ref Vector3 p, ref Vector3 v, float minY, float maxY, bool walls)
    {
        float lim = Mathf.Max(0.1f, radius - wallMargin);

        if (!walls)
        {
            if (p.y < minY) { p.y = minY; if (v.y < 0f) v.y = 0f; }
            else if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = 0f; }
        }
        else if (shape == BoidShape.Cylinder)
        {
            float h2 = p.x * p.x + p.z * p.z;
            if (h2 > lim * lim)
            {
                float inv = 1f / Mathf.Sqrt(h2);
                float nx = p.x * inv, nz = p.z * inv;      // 바깥 방향 단위벡터(수평)
                p.x = nx * lim;
                p.z = nz * lim;

                float outward = v.x * nx + v.z * nz;
                if (outward > 0f) { v.x -= outward * nx; v.z -= outward * nz; }
            }
            if (p.y < minY) { p.y = minY; if (v.y < 0f) v.y = 0f; }
            else if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = 0f; }
        }
        else
        {
            if (p.y < minY) { p.y = minY; if (v.y < 0f) v.y = 0f; }
            else if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = 0f; }

            float d2 = p.sqrMagnitude;
            if (d2 > lim * lim)
            {
                float inv = 1f / Mathf.Sqrt(d2);
                Vector3 n = p * inv;
                p = n * lim;

                float outward = Vector3.Dot(v, n);
                if (outward > 0f) v -= outward * n;

                if (p.y < minY) { p.y = minY; if (v.y < 0f) v.y = 0f; }
                else if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = 0f; }
            }
        }
    }

    void PushOutOfPlayer(ref Vector3 p)
    {
        float clear = Mathf.Max(0f, playerClearance);
        if (clear <= 0f) return;

        Vector3 d = p - playerPos;
        float d2 = d.sqrMagnitude;
        if (d2 >= clear * clear) return;

        if (d2 < 1e-6f)
        {
            p = playerPos + new Vector3(0f, 0f, clear);
            return;
        }
        p = playerPos + d * (clear / Mathf.Sqrt(d2));
    }

    // ---------------------------------------------------------------- 균일 그리드

    void BuildGridArrays()
    {
        // 셀 한 변 = 탐색 반경. 그래야 3x3x3 만 보고도 neighborRadius 안의 이웃을 전부 잡는다.
        // 개체별 sepRadius 는 최대 3.5m 까지 커지므로 그것도 덮어야 한다.
        float cellSize = Mathf.Max(0.5f, Mathf.Max(neighborRadius, Mathf.Max(separationRadius, 3.5f)));
        invCell = 1f / cellSize;

        float yExtent = (shape == BoidShape.Sphere) ? radius * 2f : height;

        gridMinX = -radius;
        gridMinZ = -radius;
        gridMinY = -yExtent * 0.5f;

        gx = Mathf.Max(1, Mathf.CeilToInt(radius * 2f * invCell));
        gz = gx;
        gy = Mathf.Max(1, Mathf.CeilToInt(yExtent * invCell));
        cellTotal = gx * gy * gz;

        if (cellStart == null || cellStart.Length != cellTotal + 1) cellStart = new int[cellTotal + 1];
        if (cellCursor == null || cellCursor.Length != cellTotal) cellCursor = new int[cellTotal];
    }

    int CellAxis(float local, int count)
    {
        int v = (int)(local * invCell);
        if (v < 0) v = 0;
        else if (v >= count) v = count - 1;
        return v;
    }

    int CellIndex(Vector3 p)
    {
        int x = CellAxis(p.x - gridMinX, gx);
        int y = CellAxis(p.y - gridMinY, gy);
        int z = CellAxis(p.z - gridMinZ, gz);
        return (y * gz + z) * gx + x;
    }

    // 카운팅 정렬로 셀별 연속 구간을 만든다. 할당 0, O(n + cells).
    void BuildGrid()
    {
        if (cellStart == null || cellStart.Length != cellTotal + 1) BuildGridArrays();

        Array.Clear(cellStart, 0, cellStart.Length);

        for (int i = 0; i < agentCount; i++)
        {
            int c = CellIndex(agents[i].pos);
            cellOf[i] = c;
            cellStart[c + 1]++;
        }

        for (int c = 0; c < cellTotal; c++) cellStart[c + 1] += cellStart[c];

        Array.Copy(cellStart, cellCursor, cellTotal);

        for (int i = 0; i < agentCount; i++)
        {
            int c = cellOf[i];
            sorted[cellCursor[c]] = i;
            cellCursor[c]++;
        }
    }

    // ---------------------------------------------------------------- 기즈모

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);

        if (shape == BoidShape.Sphere)
        {
            Gizmos.DrawWireSphere(Vector3.zero, radius);
        }
        else
        {
            float top = height * 0.5f;
            float bottom = -height * 0.5f;
            const int seg = 24;
            Vector3 prevT = new Vector3(radius, top, 0f);
            Vector3 prevB = new Vector3(radius, bottom, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 curT = new Vector3(Mathf.Cos(a) * radius, top, Mathf.Sin(a) * radius);
                Vector3 curB = new Vector3(Mathf.Cos(a) * radius, bottom, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prevT, curT);
                Gizmos.DrawLine(prevB, curB);
                if (i % 6 == 0) Gizmos.DrawLine(curB, curT);
                prevT = curT;
                prevB = curB;
            }
        }

        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.7f);
        Gizmos.DrawWireSphere(player != null ? player.position : Vector3.zero, playerClearance);
    }
}
