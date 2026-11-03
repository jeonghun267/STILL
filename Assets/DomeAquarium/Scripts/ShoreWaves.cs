using UnityEngine;

/// <summary>
/// 테라스 앞과 양옆 모래톱으로 밀려오는 파도. 흰 거품 띠가 바다 쪽에서 생겨 해안선까지 밀려와 부서지고 사라진다.
/// 띠는 해안선 모양을 따라 휜다 — 꼭짓점마다 그 z 의 해안선(테라스 앞은 부두 벽)까지 간다.
/// 부두 벽에 닿으면 물보라가 위로 튄다(창 바로 앞이라 크게 보인다), 모래톱에서는 낮게 퍼진다.
/// 바다 판은 평면이라 물마루를 세울 수 없다 — 거품 띠의 움직임으로 파도를 보인다. 메시 하나에 띠 여러 개, 매 프레임 꼭짓점만 고친다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ShoreWaves : MonoBehaviour
{
    [Tooltip("해안선 x (이 오브젝트 로컬). z = zStart + i * zStep — 빌더가 터레인 해안선과 부두 벽에서 채운다")]
    public float[] shoreX;
    public float zStart = -40f;
    public float zStep = 2f;
    public float seaY = -1.2f;
    public Vector2 pierZ = new Vector2(-0.4f, 15.4f);   // 부두 벽 구간 — 물보라가 위로 튄다

    [Header("파도")]
    [Min(1)] public int fronts = 3;                     // 시차를 두고 동시에 밀려오는 띠 수
    public float period = 7.5f;                         // 띠 하나가 생겨서 부서질 때까지 (s)
    public float travel = 28f;                          // 해안선에서 이만큼 바다 쪽에서 생긴다 (m)
    public float width = 1.6f;                          // 띠 두께 — 해안에 다가오며 두 배로 넓어진다
    [Range(0f, 1f)] public float opacity = 0.75f;

    [Header("물보라")]
    public ParticleSystem spray;                        // 월드 시뮬레이션 — ShoreFoam 과 같은 것
    public int sprayPoints = 7;                         // 부서질 때 해안선 위 몇 곳에서 튀나

    Mesh mesh;
    Vector3[] vertices;
    Color[] colors;
    bool[] broke;

    void Start()
    {
        if (shoreX == null || shoreX.Length < 2) { enabled = false; return; }
        int n = shoreX.Length;
        int perFront = n * 2;
        vertices = new Vector3[fronts * perFront];
        colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var tris = new int[fronts * (n - 1) * 6];
        int t = 0;
        for (int f = 0; f < fronts; f++)
        {
            int b = f * perFront;
            for (int i = 0; i < n; i++)
            {
                float z = zStart + i * zStep;
                uv[b + i * 2] = new Vector2(z / 9f + f * 0.37f, 0f);       // 띠마다 거품 무늬를 어긋나게
                uv[b + i * 2 + 1] = new Vector2(z / 9f + f * 0.37f, 1f);
                normals[b + i * 2] = normals[b + i * 2 + 1] = Vector3.up;
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = b + i * 2;
                // 위에서 보았을 때 시계 방향 — 바다 쪽(0) · 해안 쪽(1)
                tris[t++] = a; tris[t++] = a + 1; tris[t++] = a + 2;
                tris[t++] = a + 1; tris[t++] = a + 3; tris[t++] = a + 2;
            }
        }
        mesh = new Mesh { name = "ShoreWaves" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.triangles = tris;
        float minX = float.MaxValue;
        foreach (float x in shoreX) minX = Mathf.Min(minX, x);
        mesh.bounds = new Bounds(new Vector3(minX + travel * 0.5f, seaY, zStart + (n - 1) * zStep * 0.5f),
                                 new Vector3(travel + 10f, 2f, (n - 1) * zStep + 4f));
        GetComponent<MeshFilter>().sharedMesh = mesh;
        broke = new bool[fronts];
    }

    void Update()
    {
        if (mesh == null) return;
        int n = shoreX.Length;
        float time = Time.time;
        for (int f = 0; f < fronts; f++)
        {
            // 0 = 먼바다에서 막 생김, 1 = 해안선에서 부서짐
            float p = Mathf.Repeat(time / period + f / (float)fronts, 1f);
            float ease = 1f - (1f - p) * (1f - p);          // 해안에 가까워질수록 느려진다
            float fade = Mathf.Clamp01(p / 0.25f) * Mathf.Clamp01((1f - p) / 0.12f);
            float w = width * (1f + p);

            if (p > 0.9f && !broke[f]) { broke[f] = true; Break(); }
            else if (p < 0.5f) broke[f] = false;

            int b = f * n * 2;
            for (int i = 0; i < n; i++)
            {
                float z = zStart + i * zStep;
                float x = shoreX[i] + travel * (1f - ease);
                // 띠가 군데군데 끊겨 보이게 — 거품은 고르게 일지 않는다
                float patch = Mathf.PerlinNoise(z * 0.09f + f * 7.3f, time * 0.15f + f);
                float a = opacity * fade * Mathf.Lerp(0.35f, 1f, patch);
                vertices[b + i * 2] = new Vector3(x + w, seaY, z);
                vertices[b + i * 2 + 1] = new Vector3(x, seaY, z);
                colors[b + i * 2] = new Color(1f, 1f, 1f, a * 0.5f);   // 바다 쪽 가장자리는 옅게
                colors[b + i * 2 + 1] = new Color(1f, 1f, 1f, a);
            }
        }
        mesh.vertices = vertices;
        mesh.colors = colors;
    }

    /// <summary>띠가 해안선에 닿았을 때 — 부두 벽에서는 위로 높게, 모래톱에서는 낮게 퍼지는 물보라.</summary>
    void Break()
    {
        if (spray == null) return;
        var e = new ParticleSystem.EmitParams();
        int n = shoreX.Length;
        for (int k = 0; k < sprayPoints; k++)
        {
            int i = Random.Range(0, n);
            float z = zStart + i * zStep + Random.Range(-1f, 1f);
            bool wall = z >= pierZ.x && z <= pierZ.y;
            Vector3 local = new Vector3(shoreX[i] + 0.3f, seaY, z);
            int count = wall ? 6 : 3;
            for (int c = 0; c < count; c++)
            {
                e.position = transform.TransformPoint(local + new Vector3(Random.Range(0f, 0.6f), 0f, Random.Range(-0.8f, 0.8f)));
                // 부두 벽에서는 데크(수면 위 1.2m) 위로 넘어올 만큼 튄다 — 테라스에 서면 발밑에서 물보라가 보인다
                e.velocity = transform.TransformDirection(wall
                    ? new Vector3(Random.Range(-0.4f, 0.6f), Random.Range(4.5f, 7f), Random.Range(-0.6f, 0.6f))
                    : new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.6f, 1.4f), Random.Range(-0.6f, 0.6f)));
                e.startSize = wall ? Random.Range(0.5f, 1.2f) : Random.Range(0.4f, 0.8f);
                e.startLifetime = Random.Range(0.8f, 1.3f);
                e.startColor = new Color(1f, 1f, 1f, Random.Range(0.5f, 0.85f));
                spray.Emit(e, 1);
            }
        }
    }
}
