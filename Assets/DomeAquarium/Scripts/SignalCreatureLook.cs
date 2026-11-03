using UnityEngine;

/// <summary>
/// 신호의 주인의 모습 — 1,000m 의 UnknownCreature 와 밤 이야기의 SignalCreaturePuppet 이 같이 쓴다(같은 메시 · 재질 · 크기).
/// 빗해파리에서 착안한 반투명한 타원 몸 · 속 · 몸을 따라 흐르는 무지갯빛 띠 · 보는 쪽을 향하는 빛무리 · 뒤로 늘어지는 실 두 가닥.
/// 메시는 코드로 만든다(외부 모델 없음). 셰이더: Glow Jelly · Glow Bands.
/// </summary>
public sealed class SignalCreatureLook
{
    public struct Style
    {
        public Material body, core, bands, halo, filament;
        public Vector3 radii;
        public int bandCount;
        public float filamentLength, haloSize;
        public Color haloColor;
    }

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly Transform owner;
    readonly Style style;
    readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    readonly Renderer bodyR, coreR, bandsR, haloR;
    readonly Transform halo;
    readonly LineRenderer[] filaments;

    public SignalCreatureLook(Transform owner, Style style)
    {
        this.owner = owner;
        this.style = style;
        if (style.body == null) return;
        Part("Body", Ellipsoid(style.radii, 28, 18), style.body, out bodyR);
        Part("Core", Ellipsoid(style.radii * 0.38f, 16, 10), style.core != null ? style.core : style.body, out coreR);
        if (style.bands != null) Part("Bands", BandMesh(style.radii, style.bandCount), style.bands, out bandsR);
        if (style.halo != null) halo = Part("Halo", Quad(), style.halo, out haloR);

        if (style.filament != null)
        {
            filaments = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Filament" + i);
                go.transform.SetParent(owner, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 18;
                lr.sharedMaterial = style.filament;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.035f), new Keyframe(1f, 0.004f));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                filaments[i] = lr;
            }
        }
    }

    Transform Part(string name, Mesh mesh, Material mat, out Renderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(owner, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        renderer = r;
        return go.transform;
    }

    /// <summary>g 0~1 밝기. bandsAwake — 가까이서 띠가 또렷하게 흐른다.</summary>
    public void Apply(float g, bool bandsAwake)
    {
        if (bodyR != null) { bodyR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, 0.25f + g * 1.1f); bodyR.SetPropertyBlock(mpb); }
        if (coreR != null) { coreR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, g * 1.4f); coreR.SetPropertyBlock(mpb); }
        if (bandsR != null) { bandsR.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, (bandsAwake ? 0.4f : 0.12f) + g * (bandsAwake ? 1.3f : 0.5f)); bandsR.SetPropertyBlock(mpb); }
        if (haloR != null)
        {
            haloR.GetPropertyBlock(mpb);
            Color c = style.haloColor;
            mpb.SetColor(BaseColorId, new Color(c.r, c.g, c.b, Mathf.Clamp01(0.08f + g * 0.55f)));
            haloR.SetPropertyBlock(mpb);
        }
        if (filaments != null)
            foreach (var lr in filaments)
            {
                if (lr == null) continue;
                lr.startColor = new Color(0.55f, 0.85f, 1f, 0.15f + g * 0.6f);
                lr.endColor = new Color(0.55f, 0.85f, 1f, 0f);
            }
    }

    /// <summary>빛무리는 늘 보는 사람 쪽을 향한다 (LateUpdate 에서).</summary>
    public void FaceHalo(Transform view)
    {
        if (halo == null || view == null) return;
        halo.SetPositionAndRotation(owner.position, Quaternion.LookRotation(owner.position - view.position, Vector3.up));
        halo.localScale = Vector3.one * style.haloSize / Mathf.Max(1e-4f, owner.lossyScale.x);
    }

    public void UpdateFilaments()
    {
        if (filaments == null) return;
        float t = Time.time;
        Vector3 back = -owner.forward;
        for (int k = 0; k < filaments.Length; k++)
        {
            LineRenderer lr = filaments[k];
            if (lr == null) continue;
            float side = k == 0 ? 1f : -1f;
            Vector3 start = owner.TransformPoint(new Vector3(side * style.radii.x * 0.6f, -0.05f, -style.radii.z * 0.55f));
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                Vector3 sway = owner.right * (Mathf.Sin(t * 1.3f + i * 0.45f + k) * 0.18f * u)
                             + owner.up * (Mathf.Sin(t * 0.9f + i * 0.35f + k * 2f) * 0.12f * u - 0.25f * u * u);
                lr.SetPosition(i, start + back * (style.filamentLength * u) + owner.right * side * 0.25f * u + sway);
            }
        }
    }

    /// <summary>blinkPeriod 마다 0.18초씩 세 번 켜진다 — 신호의 주인이 빛으로 하는 말.</summary>
    public static float Blink(float time, float period, int count = 3)
    {
        float t = Mathf.Repeat(time, period);
        for (int i = 0; i < count; i++)
        {
            float start = i * 0.4f;
            if (t >= start && t <= start + 0.18f) return Mathf.Sin((t - start) / 0.18f * Mathf.PI);
        }
        return 0f;
    }

    // ─────────────────────────────────────────────────────────────

    static Mesh Ellipsoid(Vector3 r, int lon, int lat)
    {
        var mesh = new Mesh { name = "UnknownEllipsoid" };
        var v = new Vector3[(lon + 1) * (lat + 1)];
        var n = new Vector3[v.Length];
        var tris = new int[lon * lat * 6];
        int idx = 0;
        for (int y = 0; y <= lat; y++)
        {
            float theta = Mathf.PI * y / lat;                   // 0 = 앞 끝(+Z)
            for (int x = 0; x <= lon; x++)
            {
                float phi = 2f * Mathf.PI * x / lon;
                var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                v[idx] = Vector3.Scale(unit, r);
                n[idx] = new Vector3(unit.x / r.x, unit.y / r.y, unit.z / r.z).normalized;
                idx++;
            }
        }
        int t = 0;
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
            }
        mesh.vertices = v;
        mesh.normals = n;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>앞 끝에서 뒤 끝으로 몸을 따라 흐르는 띠 count 줄. 몸 표면보다 3% 밖, 가운데가 넓고 끝이 가늘다.</summary>
    static Mesh BandMesh(Vector3 radii, int count)
    {
        const int steps = 26;
        int rows = Mathf.Max(1, count);
        var mesh = new Mesh { name = "UnknownBands" };
        var v = new Vector3[rows * (steps + 1) * 2];
        var uv0 = new Vector2[v.Length];
        var uv1 = new Vector2[v.Length];
        var tris = new int[rows * steps * 6];
        int vi = 0, ti = 0;
        for (int k = 0; k < rows; k++)
        {
            float phi = 2f * Mathf.PI * k / rows;
            var east = new Vector3(-Mathf.Sin(phi), Mathf.Cos(phi), 0f);
            int baseIndex = vi;
            for (int s = 0; s <= steps; s++)
            {
                float u = s / (float)steps;
                float theta = Mathf.Lerp(0.1f, 0.9f, u) * Mathf.PI;
                var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                Vector3 p = Vector3.Scale(unit, radii) * 1.03f;
                float w = 0.008f + 0.03f * Mathf.Sin(theta);
                v[vi] = p - east * w; uv0[vi] = new Vector2(k / (float)rows, u); uv1[vi] = new Vector2(0f, 0f); vi++;
                v[vi] = p + east * w; uv0[vi] = new Vector2(k / (float)rows, u); uv1[vi] = new Vector2(1f, 0f); vi++;
            }
            for (int s = 0; s < steps; s++)
            {
                int a = baseIndex + s * 2;
                tris[ti++] = a; tris[ti++] = a + 2; tris[ti++] = a + 1;
                tris[ti++] = a + 1; tris[ti++] = a + 2; tris[ti++] = a + 3;
            }
        }
        mesh.vertices = v;
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh Quad()
    {
        var mesh = new Mesh { name = "UnknownHalo" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
