#if UNITY_EDITOR
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 유령 그물 이야기 — 해변 쓰레기 줍기(BeachCleanup)와 24m 의 그물에 걸린 바다거북(EntangledTurtle).
///   해변: 놀란 옆 모래 위 페트병 · 캔 · 그물 조각, 장비대 옆 수거함. 다 주우면 놀란이 "유령 그물" 이야기를 한다.
///   24m: 관람객 앞 오른쪽 물속에 그물째 떠다니며 버둥대는 푸른바다거북. 관찰기로 기록하면 관측소장이 구조 요청을 알린다.
///   밤: 놀란이 "무사히 풀려났대!" (NpcBuilder 의 night_rescued).
/// 그물은 마름모 무늬 텍스처(알파 잘라내기, 양면)를 입힌 구겨진 판 메시. 거북 모델 크기에 맞춰 등딱지를 감싸고 뒤로 늘어진다.
/// 순서: 9(로비) 안에서 돈다 (NPC 다음 — 놀란 대화가 있어야 한다). 따로 다시 세울 때는 29.
/// </summary>
public static class DomeAquariumGhostNet
{
    const string MatDir = "Assets/DomeAquarium/Materials";
    const string MeshDir = "Assets/DomeAquarium/Coast/Meshes";
    const string TexPath = MatDir + "/GhostNetTile.png";
    const string FishPath = "Assets/DomeAquarium/Missions/EntangledTurtle.asset";
    const float TileMeters = 0.36f;                                       // 텍스처 한 장 = 마름모 네 칸 (한 칸 9cm)

    // 해변 로컬 (DomeAquariumBeachBuilder 와 같은 좌표) — 시작 자리에서 장비대 · 놀란으로 걸어가는 길 양옆
    static readonly (string name, Vector3 local, float yaw)[] TrashSpots =
    {
        ("Trash_Bottle", new Vector3(4.75f, 0f, -12.4f), 35f),
        ("Trash_Can", new Vector3(5.35f, 0f, -10.1f), -70f),
        ("Trash_Net", new Vector3(7.35f, 0f, -13.5f), 15f),               // 마지막 하나 — 그물 조각
    };
    static readonly Vector3 BinLocal = new Vector3(5.05f, 0f, -13.05f);   // 장비대 왼쪽

    // 24m 돔 — 관람객(돔 가운데, 눈높이 1.36m) 앞 오른쪽 4m 남짓. 관찰기 최대 거리 6m 안.
    static readonly Vector3 TurtleWorld = new Vector3(2.4f, 0.5f, 3.2f);
    const float TurtleYaw = -60f;                                         // 관람객에게 옆모습 — 등딱지 위 그물이 보인다

    [MenuItem("DomeAquarium/29. 유령 그물 (해변 쓰레기 · 그물에 걸린 거북)", priority = 29)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[GhostNet] 플레이 모드에서는 세울 수 없다."); return; }
        var seq = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (seq == null) { Debug.LogError("[GhostNet] LobbySequence 가 없다. 먼저 9번 메뉴."); return; }
        Build(seq);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 해변: 쓰레기 하나 줍기", priority = 214)]
    public static void DebugTakeTrash()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GhostNet] 플레이 모드에서만 쓸 수 있다."); return; }
        var c = Object.FindFirstObjectByType<BeachCleanup>();
        if (c == null) return;
        c.DebugTakeNext();
        Debug.Log($"[GhostNet] 쓰레기 줍기 — {c.Collected + 1}/{c.items.Length}째 수거함으로");
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 24m: 그물에 걸린 거북 기록하기", priority = 221)]
    public static void DebugRecordTurtle()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GhostNet] 플레이 모드에서만 쓸 수 있다."); return; }
        var d = DiveDirector.Instance;
        if (d == null || d.entangled == null || d.aim == null || !d.entangled.isActiveAndEnabled) { Debug.LogWarning("[GhostNet] 24m 에 내려가 있지 않다."); return; }
        d.aim.DebugRecord(d.entangled.fish, d.entangled.body != null ? d.entangled.body.position : d.entangled.transform.position);
        Debug.Log($"[GhostNet] 그물에 걸린 거북 기록 — 구조 요청 {(d.TurtleRescued ? "보냄" : "안 보냄")} | {d.DebugState()}");
    }

    public static void Build(LobbySequence seq)
    {
        var net = NetMaterial();
        BuildCleanup(seq, net);
        BuildTurtle(net);
    }

    // ─────────────────────────────────────────────────────────────
    // 해변 쓰레기 줍기
    // ─────────────────────────────────────────────────────────────

    static void BuildCleanup(LobbySequence seq, Material net)
    {
        Transform root = seq.transform;
        Transform beach = root.Find("LobbySet/Beach");
        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        if (beach == null || terrain == null) { Debug.LogWarning("[GhostNet] 해변(24번 메뉴)이 없어 쓰레기 줍기를 건너뛴다."); return; }
        var old = beach.Find("Cleanup");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("Cleanup").transform;
        group.SetParent(beach, false);

        var cleanup = group.gameObject.AddComponent<BeachCleanup>();
        cleanup.nolant = Object.FindObjectsByType<NpcDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.speaker == "놀란");
        if (cleanup.nolant == null) Debug.LogWarning("[GhostNet] 놀란 대화가 없다 — 22번 메뉴 뒤에 다시 돌릴 것.");

        // ── 수거함 ──
        var binMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachBin", new Color(0.12f, 0.42f, 0.48f), 0.35f, 0f, false);
        var rimMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachBinRim", new Color(0.86f, 0.84f, 0.76f), 0.3f, 0f, false);
        var bin = new GameObject("Bin").transform;
        bin.SetParent(group, false);
        bin.localPosition = DomeAquariumBeachBuilder.OnGround(root, terrain, BinLocal);
        Cylinder("Body", bin, new Vector3(0f, 0.33f, 0f), new Vector3(0.5f, 0.33f, 0.5f), binMat);
        Cylinder("Rim", bin, new Vector3(0f, 0.665f, 0f), new Vector3(0.54f, 0.02f, 0.54f), rimMat);
        var holeMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachBinHole", new Color(0.03f, 0.05f, 0.06f), 0.1f, 0f, false);
        Cylinder("Opening", bin, new Vector3(0f, 0.688f, 0f), new Vector3(0.44f, 0.006f, 0.44f), holeMat);   // 뚫린 입구 — 뚜껑처럼 보이지 않게
        var mouth = new GameObject("Mouth").transform;
        mouth.SetParent(bin, false);
        mouth.localPosition = new Vector3(0f, 0.6f, 0f);
        cleanup.bin = mouth;
        DomeAquariumLobbyBuilder.Label("BinSign", bin, new Vector3(0f, 0.46f, -0.262f), 0f, new Vector2(0.44f, 0.12f),
              "<b>수거함</b>", 0.075f, new Color(0.96f, 0.93f, 0.86f), TextAlignmentOptions.Center);
        cleanup.counter = DomeAquariumLobbyBuilder.Label("BinCount", bin, new Vector3(0f, 0.3f, -0.262f), 0f, new Vector2(0.44f, 0.1f),
              "", 0.055f, new Color(0.96f, 0.93f, 0.86f), TextAlignmentOptions.Center);

        var drop = bin.gameObject.AddComponent<AudioSource>();
        drop.playOnAwake = false;
        drop.spatialBlend = 1f;
        drop.minDistance = 1.5f;
        drop.maxDistance = 10f;
        drop.volume = 0.8f;
        cleanup.sound = drop;
        cleanup.dropClip = DomeAquariumDepthSounds.EnsureBinDrop();

        // ── 쓰레기 셋 ──
        var bottleMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_TrashBottle", new Color(0.72f, 0.88f, 0.93f, 0.55f), 0.85f, 0f, true);
        var capMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_TrashCap", new Color(0.12f, 0.35f, 0.80f), 0.4f, 0f, false);
        var canMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_TrashCan", new Color(0.72f, 0.12f, 0.10f), 0.55f, 0.6f, false);
        var floatMat = FloatMaterial();
        var items = new BeachCleanup.Item[TrashSpots.Length];
        for (int i = 0; i < TrashSpots.Length; i++)
        {
            var (name, local, yaw) = TrashSpots[i];
            var t = new GameObject(name).transform;
            t.SetParent(group, false);
            t.localPosition = DomeAquariumBeachBuilder.OnGround(root, terrain, local) + Vector3.up * 0.03f;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var model = new GameObject("Model").transform;
            model.SetParent(t, false);
            model.localScale = Vector3.one * 1.25f;                         // 모래 위에서 눈에 띄게 실제보다 조금 크게
            switch (i)
            {
                case 0:   // 누운 페트병
                    Cylinder("Bottle", model, new Vector3(0f, 0.035f, 0f), new Vector3(0.07f, 0.11f, 0.07f), bottleMat, new Vector3(0f, 0f, 90f));
                    Cylinder("Cap", model, new Vector3(0.122f, 0.035f, 0f), new Vector3(0.034f, 0.012f, 0.034f), capMat, new Vector3(0f, 0f, 90f));
                    break;
                case 1:   // 찌그러진 캔
                    Cylinder("Can", model, new Vector3(0f, 0.03f, 0f), new Vector3(0.066f, 0.06f, 0.05f), canMat, new Vector3(8f, 0f, 90f));
                    break;
                default:  // 엉킨 그물 조각 + 부표 하나
                    var piece = new GameObject("NetPiece", typeof(MeshFilter), typeof(MeshRenderer));
                    piece.transform.SetParent(model, false);
                    piece.GetComponent<MeshFilter>().sharedMesh = SaveMesh(CrumpleMesh(0.55f, 0.1f, 314), "BeachNetPiece");
                    piece.GetComponent<MeshRenderer>().sharedMaterial = net;
                    Sphere("Float", model, new Vector3(0.22f, 0.05f, 0.12f), 0.1f, floatMat);
                    Sphere("Float2", model, new Vector3(-0.18f, 0.04f, -0.16f), 0.08f, floatMat);
                    break;
            }

            // 손 찌르기 — 바닥까지 몸을 숙이지 않아도 무릎 높이에서 닿게 위로 긴 트리거 기둥. 레이는 안쪽 단단한 상자를 맞힌다.
            var pokeVolume = t.gameObject.AddComponent<BoxCollider>();
            pokeVolume.isTrigger = true;
            pokeVolume.center = new Vector3(0f, 0.35f, 0f);
            pokeVolume.size = new Vector3(0.42f, 0.8f, 0.42f);
            var grab = new GameObject("RayVolume").AddComponent<BoxCollider>();
            grab.transform.SetParent(t, false);
            grab.center = new Vector3(0f, 0.08f, 0f);
            grab.size = new Vector3(0.34f, 0.18f, 0.34f);
            var ray = t.gameObject.AddComponent<XRSimpleInteractable>();
            var poke = t.gameObject.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 0.5f;
            items[i] = new BeachCleanup.Item { root = t, interactable = ray, poke = poke };
        }
        cleanup.items = items;
        EditorUtility.SetDirty(cleanup);
        Debug.Log($"[GhostNet] 해변 쓰레기 {items.Length}개 · 수거함 — 놀란 {(cleanup.nolant != null ? "연결" : "없음")}.");
    }

    // ─────────────────────────────────────────────────────────────
    // 24m — 그물에 걸린 푸른바다거북
    // ─────────────────────────────────────────────────────────────

    static void BuildTurtle(Material net)
    {
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        var reef = GameObject.Find("ReefFloor");
        var species = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/GreenTurtle.asset");
        if (director == null || reef == null || species == null || species.prefab == null)
        { Debug.LogWarning("[GhostNet] DiveDirector · ReefFloor · 푸른바다거북 데이터가 없어 거북을 건너뛴다."); return; }

        var old = Object.FindFirstObjectByType<EntangledTurtle>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 수중 돔 묶음(ReefFloor) 아래 — 해변에 있는 동안 돔과 같이 꺼진다. 층마다 켜고 끄는 건 DiveDirector.
        var go = new GameObject("EntangledTurtle");
        go.transform.SetParent(reef.transform, false);
        go.transform.position = TurtleWorld;
        var turtle = go.AddComponent<EntangledTurtle>();
        turtle.fish = TurtleData(species);
        turtle.species = species;

        var body = (GameObject)PrefabUtility.InstantiatePrefab(species.prefab);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one * Mathf.Max(0.001f, species.swimScale);
        foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        turtle.body = body.transform;

        // 모델 크기에 맞춘 그물 (돌리기 전, 로컬 = 월드 축에서 잰다)
        var rends = body.GetComponentsInChildren<Renderer>(true);
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        b.center -= go.transform.position;

        var netGo = new GameObject("Net", typeof(MeshFilter), typeof(MeshRenderer));
        netGo.transform.SetParent(go.transform, false);
        netGo.GetComponent<MeshFilter>().sharedMesh = SaveMesh(WrapMesh(b), "GhostNetWrap");
        netGo.GetComponent<MeshRenderer>().sharedMaterial = net;
        netGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        turtle.net = netGo.transform;
        // 그물 끝에 매달린 부표 둘
        var floatMat = FloatMaterial();
        float tailZ = b.center.z - b.extents.z * 0.3f - 1.5f;
        Sphere("Float_L", netGo.transform, new Vector3(-0.22f, b.center.y - 0.75f, tailZ), 0.13f, floatMat);
        Sphere("Float_R", netGo.transform, new Vector3(0.3f, b.center.y - 0.55f, tailZ + 0.35f), 0.11f, floatMat);

        go.transform.rotation = Quaternion.Euler(0f, TurtleYaw, 0f);
        go.SetActive(false);                                               // 24m 에 내려가면 DiveDirector 가 켠다

        director.entangled = turtle;
        EditorUtility.SetDirty(turtle);
        EditorUtility.SetDirty(director);
        Debug.Log($"[GhostNet] 그물에 걸린 거북 — {TurtleWorld}, 모델 크기 {b.size}, 기록 데이터 {FishPath}.");
    }

    /// <summary>기록 카드용 — 일지에는 푸른바다거북으로 남는다(speciesId 같음). 이름 · 설명만 이 장면에 맞춘다.</summary>
    static FishData TurtleData(FishData species)
    {
        var data = AssetDatabase.LoadAssetAtPath<FishData>(FishPath);
        if (data == null)
        {
            data = Object.Instantiate(species);
            AssetDatabase.CreateAsset(data, FishPath);
        }
        else EditorUtility.CopySerialized(species, data);
        data.name = "EntangledTurtle";
        data.speciesId = species.speciesId;
        data.koreanName = "그물에 걸린 푸른바다거북";
        data.description = "버려진 고기잡이 그물에 앞지느러미와 등딱지가 감겨 빠져나오지 못하고 있어요. " +
                           "바다거북은 숨을 쉬러 물 위로 올라가야 하는데, 그물에 묶이면 그럴 수가 없어요. " +
                           "이렇게 바다를 떠다니며 생물을 붙잡는 그물을 '유령 그물'이라고 불러요.";
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return data;
    }

    // ─────────────────────────────────────────────────────────────
    // 그물 메시 · 재질
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 등딱지를 위에서 감싸 양옆 아래로 말려 들어가는 판 + 꼬리 쪽으로 1.5m 늘어지며 처지는 자락.
    /// 가로는 각도(±112°)로 타원을 따라 돌고, 꼬리 자락에서는 좁아지며 아래로 처지고 물결친다. 구김은 작은 흔들림.
    /// </summary>
    static Mesh WrapMesh(Bounds b)
    {
        const int nu = 15, nv = 24, bodyRows = 10;
        float halfW = b.extents.x * 0.82f;
        float zFront = b.center.z + b.extents.z * 0.5f, zBody = b.center.z - b.extents.z * 0.3f, zEnd = zBody - 1.5f;
        float cy = b.center.y, ry = b.max.y + 0.03f - cy;
        var rng = new System.Random(2718);
        float J() => (float)(rng.NextDouble() * 2.0 - 1.0);

        var verts = new Vector3[nu * nv];
        var uvs = new Vector2[nu * nv];
        for (int v = 0; v < nv; v++)
        {
            float z, width, trail;
            if (v < bodyRows) { float k = v / (float)(bodyRows - 1); z = Mathf.Lerp(zFront, zBody, k); width = halfW; trail = 0f; }
            else { float k = (v - bodyRows + 1) / (float)(nv - bodyRows); z = Mathf.Lerp(zBody, zEnd, k); width = halfW * Mathf.Lerp(1f, 0.35f, k); trail = k; }
            for (int u = 0; u < nu; u++)
            {
                float a = u / (float)(nu - 1) * 2f - 1f;
                float ang = a * Mathf.PI * 0.62f;
                float x = Mathf.Sin(ang) * width * (1f - 0.55f * trail);
                float y = cy + Mathf.Cos(ang) * ry * (1f - 0.7f * trail) - trail * trail * 0.9f + Mathf.Sin(a * 3f + trail * 6f) * 0.07f * trail;
                float crumple = 0.02f + 0.03f * trail;
                verts[v * nu + u] = new Vector3(x + J() * crumple, y + J() * crumple, z + J() * crumple);
                uvs[v * nu + u] = new Vector2(ang * width / TileMeters, (zFront - z) / TileMeters);
            }
        }
        return Grid(verts, uvs, nu, nv, "GhostNetWrap");
    }

    /// <summary>모래 위에 뭉쳐 놓인 그물 조각 — 작은 판을 마구 구긴다.</summary>
    static Mesh CrumpleMesh(float size, float height, int seed)
    {
        const int n = 9;
        var rng = new System.Random(seed);
        float J() => (float)(rng.NextDouble() * 2.0 - 1.0);
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int v = 0; v < n; v++)
            for (int u = 0; u < n; u++)
            {
                float x = (u / (float)(n - 1) - 0.5f) * size, z = (v / (float)(n - 1) - 0.5f) * size;
                float r = Mathf.Sqrt(x * x + z * z) / (size * 0.5f);
                float pull = 1f - 0.45f * r;                                 // 가장자리를 가운데로 끌어모아 뭉친 모양
                float y = height * Mathf.Clamp01(1f - r) + Mathf.Abs(J()) * height * 0.6f;
                verts[v * n + u] = new Vector3(x * pull + J() * 0.03f, y, z * pull + J() * 0.03f);
                uvs[v * n + u] = new Vector2(x / TileMeters, z / TileMeters);
            }
        return Grid(verts, uvs, n, n, "BeachNetPiece");
    }

    static Mesh Grid(Vector3[] verts, Vector2[] uvs, int nu, int nv, string name)
    {
        var tris = new int[(nu - 1) * (nv - 1) * 6];
        int k = 0;
        for (int v = 0; v < nv - 1; v++)
            for (int u = 0; u < nu - 1; u++)
            {
                int i = v * nu + u;
                tris[k++] = i; tris[k++] = i + nu; tris[k++] = i + 1;
                tris[k++] = i + 1; tris[k++] = i + nu; tris[k++] = i + nu + 1;
            }
        var mesh = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshDir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        existing.vertices = mesh.vertices;
        existing.uv = mesh.uv;
        existing.triangles = mesh.triangles;
        existing.RecalculateNormals();
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    /// <summary>바랜 초록빛 밧줄 그물 — 마름모 무늬 텍스처, 알파 잘라내기, 양면.</summary>
    static Material NetMaterial()
    {
        string path = $"{MatDir}/Mat_GhostNet.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", NetTexture());
        mat.SetColor("_BaseColor", new Color(0.92f, 0.96f, 0.86f));
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.45f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f);
        mat.doubleSidedGI = true;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D NetTexture()
    {
        if (!File.Exists(TexPath))
        {
            const int size = 128, cells = 4;
            float cell = size / (float)cells;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(99);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 대각선 두 방향 줄 — 마름모 그물코. 줄까지의 거리(픽셀)로 굵기 2px 남짓, 가장자리는 부드럽게.
                    float p = (x + y) / cell, q = (x - y + size) / cell;
                    float d1 = Mathf.Min(p - Mathf.Floor(p), Mathf.Ceil(p) - p) * cell;
                    float d2 = Mathf.Min(q - Mathf.Floor(q), Mathf.Ceil(q) - q) * cell;
                    float d = Mathf.Min(d1, d2);
                    float knot = d1 < 2.4f && d2 < 2.4f ? 0.9f : 0f;                // 매듭은 조금 굵다
                    float a = Mathf.Clamp01(2.7f + knot - d);
                    float shade = 0.85f + 0.15f * Mathf.Sin((x - y) * 0.9f) + (float)rng.NextDouble() * 0.06f;   // 꼰 밧줄 결
                    tex.SetPixel(x, y, new Color(0.74f * shade, 0.84f * shade, 0.70f * shade, a));   // 볕에 바랜 초록 밧줄
                }
            Directory.CreateDirectory(MatDir);
            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(TexPath) is TextureImporter imp && (!imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Repeat || !imp.mipMapsPreserveCoverage))
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.mipMapsPreserveCoverage = true;                            // 멀리서도 그물코가 사라지지 않게
            imp.alphaTestReferenceValue = 0.45f;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }

    static Material FloatMaterial() =>
        DomeAquariumLobbyBuilder.LitMaterial("Mat_GhostNetFloat", new Color(0.95f, 0.45f, 0.12f), 0.5f, 0f, false);

    static void Cylinder(string name, Transform parent, Vector3 local, Vector3 scale, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void Sphere(string name, Transform parent, Vector3 local, float diameter, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(diameter, diameter * 0.85f, diameter);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
#endif
