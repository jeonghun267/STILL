using UnityEngine;

/// <summary>
/// 만 위를 도는 갈매기 몇 마리. 로비에는 갈매기 소리(파도 · 갈매기 고리)만 있고 하늘에 새가 없었다.
/// 모델 없이 몸통과 두 날개를 코드로 만든다 (한 마리 삼각형 16개). 큰 원을 따라 돌며 날갯짓과 활공을 번갈아 한다.
/// 원점(이 오브젝트)은 만 한가운데 수면 위 — 돌고래가 뛰는 곳(창에서 14~28m)보다 한참 높이 돈다.
/// 엔딩의 밤 로비에서는 보이지 않는다.
/// </summary>
public sealed class SeagullFlock : MonoBehaviour
{
    public LobbySequence lobby;                         // 밤이면 숨긴다
    public Material material;
    [Min(1)] public int count = 5;
    public Vector2 radius = new Vector2(24f, 46f);      // 도는 원의 반지름
    public Vector2 height = new Vector2(11f, 24f);      // 원점 기준 높이
    public Vector2 speed = new Vector2(6f, 9f);         // m/s
    public float wingSpan = 1.3f;                       // 재갈매기 날개 편 길이 1.3~1.5m
    public float flapsPerSecond = 2.6f;

    sealed class Gull
    {
        public Transform body, left, right;
        public float radius, height, angle, direction, speed, phase;
        public float glide;                             // 0 = 날갯짓, 1 = 활공
        public bool gliding;
        public float switchAt;
    }

    Gull[] gulls;
    GameObject flockRoot;
    bool hidden;

    void Start()
    {
        Mesh bodyMesh = BuildBody();
        Mesh wingMesh = BuildWing(wingSpan * 0.5f);
        flockRoot = new GameObject("Gulls");
        flockRoot.transform.SetParent(transform, false);

        gulls = new Gull[count];
        for (int i = 0; i < count; i++)
        {
            var g = new Gull
            {
                radius = Random.Range(radius.x, radius.y),
                height = Random.Range(height.x, height.y),
                angle = Random.value * Mathf.PI * 2f,
                direction = i % 3 == 2 ? -1f : 1f,     // 몇 마리는 반대로 돈다
                speed = Random.Range(speed.x, speed.y),
                phase = Random.value * 10f,
                switchAt = Random.Range(1f, 4f),
            };
            g.body = Part("Gull", flockRoot.transform, bodyMesh, Vector3.zero);
            g.left = Part("WingL", g.body, wingMesh, new Vector3(-0.05f, 0.01f, 0f));
            g.right = Part("WingR", g.body, wingMesh, new Vector3(0.05f, 0.01f, 0f));
            g.left.localScale = new Vector3(-1f, 1f, 1f);   // 같은 날개를 거울로 — 날개는 양면이라 감기 방향이 바뀌어도 보인다
            gulls[i] = g;
        }
    }

    Transform Part(string name, Transform parent, Mesh mesh, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    void Update()
    {
        if (gulls == null) return;
        bool night = lobby != null && lobby.IsNight;
        if (night != hidden) { hidden = night; flockRoot.SetActive(!night); }
        if (hidden) return;

        float dt = Time.deltaTime;
        float t = Time.time;
        foreach (var g in gulls)
        {
            // 원 둘레를 일정한 속도로 — 반지름이 숨 쉬듯 조금씩 변해 같은 원을 그리는 티가 덜 난다
            float r = g.radius + Mathf.Sin(t * 0.07f + g.phase) * 5f;
            g.angle += g.direction * g.speed / Mathf.Max(5f, r) * dt;
            float y = g.height + Mathf.Sin(t * 0.21f + g.phase) * 2.5f;
            g.body.localPosition = new Vector3(Mathf.Cos(g.angle) * r, y, Mathf.Sin(g.angle) * r);

            Vector3 tangent = new Vector3(-Mathf.Sin(g.angle), 0f, Mathf.Cos(g.angle)) * g.direction;
            float bank = 18f * g.direction;             // 원 안쪽으로 기운다
            g.body.localRotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, bank);

            // 날갯짓 몇 번 → 활공 몇 초를 번갈아
            if (t >= g.switchAt)
            {
                g.gliding = !g.gliding;
                g.switchAt = t + (g.gliding ? Random.Range(2.5f, 6f) : Random.Range(1.2f, 3f));
            }
            g.glide = Mathf.MoveTowards(g.glide, g.gliding ? 1f : 0f, dt * 2f);
            float flap = Mathf.Sin((t + g.phase) * flapsPerSecond * 2f * Mathf.PI) * 32f + 6f;
            float wing = Mathf.Lerp(flap, 7f, g.glide);   // 활공할 때는 날개를 살짝 위로 든 채
            g.right.localRotation = Quaternion.Euler(0f, 0f, wing);
            g.left.localRotation = Quaternion.Euler(0f, 0f, -wing);
        }
    }

    /// <summary>몸통 — 앞이 +Z 인 길쭉한 마름모 (머리 쪽이 굵고 꼬리 쪽이 가늘다).</summary>
    static Mesh BuildBody()
    {
        var v = new[]
        {
            new Vector3(0f, 0f, 0.24f),     // 0 부리
            new Vector3(0f, 0f, -0.30f),    // 1 꼬리
            new Vector3(-0.07f, 0f, 0.02f), // 2 왼쪽
            new Vector3(0.07f, 0f, 0.02f),  // 3 오른쪽
            new Vector3(0f, 0.06f, 0.02f),  // 4 등
            new Vector3(0f, -0.06f, 0.02f), // 5 배
        };
        var tris = new[] { 0, 3, 4, 0, 4, 2, 0, 5, 3, 0, 2, 5, 1, 4, 3, 1, 2, 4, 1, 3, 5, 1, 5, 2 };
        var m = new Mesh { name = "GullBody", vertices = v, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>오른쪽 날개 한 장 (+X 로 뻗고 끝이 뒤로 젖혀진다). 앞뒷면을 따로 만들어 양쪽에서 보인다.</summary>
    static Mesh BuildWing(float half)
    {
        var top = new[]
        {
            new Vector3(0f, 0f, 0.10f),
            new Vector3(0f, 0f, -0.09f),
            new Vector3(half * 0.55f, 0f, 0.05f),
            new Vector3(half, 0f, -0.13f),
        };
        var v = new Vector3[8];
        var n = new Vector3[8];
        for (int i = 0; i < 4; i++)
        {
            v[i] = top[i]; n[i] = Vector3.up;
            v[i + 4] = top[i]; n[i + 4] = Vector3.down;
        }
        var tris = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6 };
        var m = new Mesh { name = "GullWing", vertices = v, normals = n, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
