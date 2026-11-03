#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 엘리베이터 칸 뒷벽(계기판 양옆)에 작은 둥근 창과 수압 컵을 단다 (CabinDepthView).
///   [ 둥근 창 ]   [ 수심 계기판 ]   [ 선반 위 스티로폼 컵 · 수압 ]
/// 들어서서 계기판을 보면 양옆으로 함께 보인다. 전부 ElevatorCabin 아래 — 칸이 꺼지면 같이 꺼진다.
/// 순서: 9(로비) 안에서 돈다. 따로 다시 세울 때는 27.
/// </summary>
public static class DomeAquariumCabinExtras
{
    const string MeshDir = "Assets/DomeAquarium/Coast/Meshes";
    const string MatDir = "Assets/DomeAquarium/Materials";
    const float WindowRadius = 0.18f;

    [MenuItem("DomeAquarium/27. 엘리베이터 창 · 수압 컵", priority = 27)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Cabin] 플레이 모드에서는 세울 수 없다."); return; }
        var seq = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (seq == null || seq.cabinSet == null) { Debug.LogError("[Cabin] LobbySequence · 엘리베이터 칸이 없다. 먼저 9번 메뉴."); return; }
        Build(seq);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(LobbySequence seq)
    {
        Transform cabin = seq.cabinSet.transform;
        var panel = cabin.Find("DepthPanel");
        if (panel == null) { Debug.LogError("[Cabin] 수심 계기판(DepthPanel)을 찾지 못했다."); return; }
        var old = cabin.Find("CabinExtras");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("CabinExtras").transform;
        group.SetParent(cabin, false);

        // 계기판 앞면(두께 4cm) 기준 — 뒷벽 면은 모델마다 달라, 그보다 앞에 붙이면 벽 속에 묻혔다
        float wall = panel.localPosition.z - 0.02f;
        float cx = panel.localPosition.x;
        var view = group.gameObject.AddComponent<CabinDepthView>();
        view.lobby = seq;

        // ── 둥근 창 (계기판 왼쪽) ──
        var frameMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_CabinPortholeFrame", new Color(0.46f, 0.38f, 0.22f), 0.6f, 0.75f, false);
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "PortholeFrame";
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.SetParent(group, false);
        ring.transform.localPosition = new Vector3(cx - 0.9f, 1.55f, wall);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = new Vector3((WindowRadius + 0.04f) * 2f, 0.015f, (WindowRadius + 0.04f) * 2f);
        ring.GetComponent<Renderer>().sharedMaterial = frameMat;

        Mesh disc = DiscMesh();
        var waterMat = UnlitMaterial("Mat_CabinWindowWater", new Color(0.32f, 0.66f, 0.80f));
        view.water = Disc("PortholeWater", group, disc, waterMat, new Vector3(cx - 0.9f, 1.55f, wall - 0.018f));
        var snowMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CabinWindowSnow", "Universal Render Pipeline/Particles/Unlit");
        snowMat.SetTexture("_BaseMap", SnowTexture());
        snowMat.SetTextureScale("_BaseMap", new Vector2(1.6f, 1.6f));
        if (snowMat.HasProperty("_Cull")) snowMat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(snowMat);
        view.snow = Disc("PortholeSnow", group, disc, snowMat, new Vector3(cx - 0.9f, 1.55f, wall - 0.022f));
        view.waterByDepth = new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(new Color(0.32f, 0.66f, 0.80f), 0f),      // 0m — 햇빛 드는 옥색
                new GradientColorKey(new Color(0.08f, 0.36f, 0.52f), 0.46f),   // 24m
                new GradientColorKey(new Color(0.02f, 0.08f, 0.20f), 0.77f),   // 200m
                new GradientColorKey(new Color(0.006f, 0.01f, 0.025f), 1f),    // 1,000m — 캄캄
            },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
        };

        // ── 수압 컵 (계기판 오른쪽 선반) ──
        var shelfMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_CabinShelf", new Color(0.30f, 0.26f, 0.22f), 0.35f, 0.1f, false);
        var shelf = DomeAquariumLobbyBuilder.Box("CupShelf", group, new Vector3(cx + 0.9f, 1.18f, wall - 0.07f), new Vector3(0.32f, 0.025f, 0.16f), shelfMat);
        if (shelf.GetComponent<Collider>() is Collider c) Object.DestroyImmediate(c);

        var cupMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_StyrofoamCup", new Color(0.96f, 0.95f, 0.92f), 0.15f, 0f, false);
        if (cupMat.HasProperty("_Cull")) { cupMat.SetFloat("_Cull", 0f); EditorUtility.SetDirty(cupMat); }   // 열린 위로 안쪽이 보인다
        var cup = new GameObject("StyrofoamCup");
        cup.transform.SetParent(group, false);
        cup.transform.localPosition = new Vector3(cx + 0.9f, 1.18f + 0.0125f, wall - 0.07f);   // 바닥이 원점 — 쪼그라들어도 선반에 붙어 있다
        cup.AddComponent<MeshFilter>().sharedMesh = CupMesh();
        cup.AddComponent<MeshRenderer>().sharedMaterial = cupMat;
        view.cup = cup.transform;

        // 글자는 칸 가운데(1.4m)에서 1° 남짓 — 10~15세 기준
        DomeAquariumLobbyBuilder.Label("CupTitle", group, new Vector3(cx + 0.9f, 1.66f, wall - 0.005f), 0f, new Vector2(0.5f, 0.06f),
            "<b>수압 실험 · 스티로폼 컵</b>", 0.034f, new Color(0.85f, 0.95f, 1f), TMPro.TextAlignmentOptions.Center);
        view.pressure = DomeAquariumLobbyBuilder.Label("Pressure", group, new Vector3(cx + 0.9f, 1.58f, wall - 0.005f), 0f, new Vector2(0.5f, 0.08f),
            "수압  1 기압", 0.05f, new Color(0.55f, 0.95f, 1f), TMPro.TextAlignmentOptions.Center);
        DomeAquariumLobbyBuilder.Label("CupNote", group, new Vector3(cx + 0.9f, 1.43f, wall - 0.005f), 0f, new Vector2(0.5f, 0.15f),
            "깊이 내려갈수록 물이 누르는 힘이 커져\n컵이 쪼그라들어요. 1,000m에서는\n손톱만 한 넓이를 약 100kg이 눌러요.", 0.026f, new Color(0.80f, 0.88f, 0.94f), TMPro.TextAlignmentOptions.Center);

        foreach (var r in group.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        EditorUtility.SetDirty(view);
        Debug.Log("[Cabin] 엘리베이터 칸 — 둥근 창(물빛 · 바다눈)과 수압 컵을 달았다.");
    }

    static Renderer Disc(string name, Transform parent, Mesh mesh, Material mat, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return r;
    }

    static Material UnlitMaterial(string name, Color color)
    {
        string path = $"{MatDir}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>앞면이 -z(칸 안쪽)를 보는 둥근 판 — UV 는 판 전체 0~1.</summary>
    static Mesh DiscMesh()
    {
        const int seg = 40;
        var verts = new List<Vector3> { Vector3.zero };
        var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            verts.Add(new Vector3(p.x * WindowRadius, p.y * WindowRadius, 0f));
            uvs.Add(p * 0.5f + Vector2.one * 0.5f);
        }
        var tris = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % seg;
            // Unity 앞면은 cross(b-a, c-a) 쪽 — -z 를 보게
            if (Vector3.Cross(verts[a] - verts[0], verts[b] - verts[0]).z < 0f) tris.AddRange(new[] { 0, a, b });
            else tris.AddRange(new[] { 0, b, a });
        }
        return SaveMesh("CabinWindowDisc", verts, tris, uvs);
    }

    /// <summary>스티로폼 컵 — 바닥이 원점, 위가 넓고 열려 있다 (높이 9.5cm, 위 지름 8.4cm · 아래 6cm).</summary>
    static Mesh CupMesh()
    {
        const int seg = 28;
        const float h = 0.095f, rTop = 0.042f, rBottom = 0.030f;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(d * rBottom);
            verts.Add(d * rTop + Vector3.up * h);
        }
        for (int i = 0; i < seg; i++)
        {
            int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            tris.AddRange(new[] { b0, t0, b1, b1, t0, t1 });
        }
        int center = verts.Count;
        verts.Add(Vector3.zero);
        for (int i = 0; i < seg; i++) tris.AddRange(new[] { center, i * 2, (i + 1) * 2 });
        return SaveMesh("StyrofoamCup", verts, tris, null);
    }

    static Mesh SaveMesh(string name, List<Vector3> verts, List<int> tris, List<Vector2> uvs)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        if (uvs != null) mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = $"{MeshDir}/{name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved != null) { saved.Clear(); EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); return saved; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>바다눈 무늬 — 검은 바탕에 크기가 다른 흰 알갱이 70개, 가장자리가 이어진다(위아래로 흘려도 이음매가 없다).</summary>
    static Texture2D SnowTexture()
    {
        string path = $"{MatDir}/MarineSnowTile.png";
        if (!File.Exists(path))
        {
            const int n = 256;
            var px = new Color[n * n];
            var rng = new System.Random(1000);
            for (int k = 0; k < 70; k++)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float r = 1.2f + (float)rng.NextDouble() * 2.6f;
                float level = 0.45f + (float)rng.NextDouble() * 0.55f;
                for (int dy = -6; dy <= 6; dy++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1.6f) continue;
                        int x = ((int)cx + dx + n) % n, y = ((int)cy + dy + n) % n;
                        float a = Mathf.Clamp01(1.6f - d) * level;
                        var c = px[y * n + x];
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Max(c.a, a));
                    }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
