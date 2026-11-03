#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 해변에서 시작하는 구간 — 관측소 남쪽 모래사장에서 눈을 뜨고, 조수 웅덩이에서 관찰기를 처음 써 본 뒤, 나무 계단으로 테라스에 올라
/// 유리문으로 관측소에 들어간다. (예전에는 로비 홀 안에서 시작해 장비대 · 연습 수조로 배웠다 — 연습 수조는 이제 홀의 볼거리다.)
///
///  - 모래사장: 로비 로컬 x 3.6~8.8, z -17~-0.6. 지형 높이를 따라 걷는다(LobbyTutorial.FloorAt — 리그는 평면으로만 움직여
///    중력 · 지면 충돌이 없다). 순간이동은 없다 — 막대로 걷는다.
///  - 계단: 데크 남쪽 끝을 받치는 부두 벽(PierSideA, z -0.3~0) 바로 앞(z -0.3, 높이 0)에서 모래(z -3.55)까지 6칸.
///    걸을 때는 높이가 고르게 바뀐다. 예전에는 z 0.35 에서 시작해 첫 디딤판 · 옆판이 데크 바닥과 부두 벽을 뚫고 지나가 겹쳐 보였다.
///  - 관측소 남쪽 외벽: 벽 전체를 채운 바닷속 벽화(DomeAquariumMuralBuilder 가 게임 속 생물로 그린 그림)와 지붕 끝의 "STILL 해양 관측소" 간판.
///  - 조수 웅덩이: 물가(8.0, -10.5). 바위 턱(높이 0.26m)이 얕은 물을 담고, 게 · 불가사리 · 성게가 바닥을 기고 새우가 헤엄친다 — 연습 수조(PracticeTank)와 같은
///    방식이라 관찰기로 겨눠 첫 기록을 한다. 놀란(선장의 손자)이 옆에서 쓰는 법을 알려 준다(DomeAquariumNpcBuilder).
///  - 장비대 · 시작 자리 · "할 일" 표시도 해변으로 옮긴다. 개찰구 · 엘리베이터는 그대로 — 첫 기록을 하면 열린다.
/// 순서: 9(로비) 안에서 19 · 20 · 22 다음에 돈다. 따로 다시 세울 때는 21(조수 웅덩이 종) → 24.
/// </summary>
public static class DomeAquariumBeachBuilder
{
    internal static readonly Rect SandArea = Rect.MinMaxRect(3.6f, -17f, 8.8f, -0.6f);
    internal static readonly Rect StairArea = Rect.MinMaxRect(6.7f, -3.55f, 7.9f, 0.7f);
    const float StairTopZ = -0.3f, StairBottomZ = -3.55f;        // 부두 벽 앞면에서 시작 — 데크(z 0~)와 겹치지 않는다
    const int StairSteps = 6;
    internal static readonly Vector3 PoolLocal = new Vector3(8.0f, 0f, -10.5f);
    internal static readonly Vector3 PoolInnerHalf = new Vector3(0.38f, 0.09f, 0.58f);      // 생물이 다니는 상자 — 웅덩이 바닥 타원 안에 든다
    // 웅덩이: 바위 턱(위 0.26m)이 물을 두르고, 물은 턱보다 조금 낮게(0.2m) 담긴다. 바닥 0.02m.
    const float PoolRimTop = 0.26f, PoolWater = 0.2f, PoolBed = 0.02f, PoolRimWidth = 0.34f;
    static readonly Vector2 PoolInnerRadius = new Vector2(PoolInnerHalf.x * 1.55f, PoolInnerHalf.z * 1.55f);   // 상자 모서리까지 품는 타원
    internal static readonly Vector3 TableLocal = new Vector3(6.0f, 0f, -12.6f);
    internal static readonly Vector3 NolantLocal = new Vector3(6.75f, 0f, -11.5f);          // 웅덩이 턱 바로 바깥
    internal static readonly Vector3 SpawnLocal = new Vector3(5.2f, 0f, -15.8f);
    internal static readonly Vector3 WalkStartLocal = new Vector3(5.6f, 0f, -14.4f);
    static readonly string[] PoolFish = { "Crab", "Crab", "Starfish", "SeaUrchin", "Shrimp" };

    static readonly string[] IntroLines =
    {
        "1930년, 두 사람이 작은 쇠공을 타고 처음으로 깊은 바다를 들여다봤습니다.",
        "지구 표면의 71%는 바다입니다.",
        "하지만 그 바다의 80% 이상은, 아직 아무도 본 적이 없습니다.",
        "오늘, 당신이 그 다음 관찰자입니다. 관측소에 가기 전에, 바닷가부터 둘러봐요.",
    };

    [MenuItem("DomeAquarium/24. 해변 시작 구간 (모래사장 · 계단 · 조수 웅덩이)", priority = 24)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Beach] 플레이 모드에서는 세울 수 없다."); return; }
        var root = GameObject.Find("LobbyRoot");
        Transform set = root != null ? root.transform.Find("LobbySet") : null;
        var seq = root != null ? root.GetComponent<LobbySequence>() : null;
        if (set == null || seq == null || seq.tutorial == null) { Debug.LogError("[Beach] LobbyRoot · LobbySet · LobbyTutorial 이 없다. 먼저 9번 메뉴."); return; }
        Build(root.transform, set, seq, seq.tutorial);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(Transform root, Transform set, LobbySequence seq, LobbyTutorial tut)
    {
        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        if (terrain == null) { Debug.LogError("[Beach] 해안 지형이 없다. 먼저 19번 메뉴."); return; }
        var old = set.Find("Beach");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("Beach").transform;
        group.SetParent(set, false);

        float stairBottomY = GroundY(root, terrain, new Vector2(7.3f, StairBottomZ));
        BuildStairs(group, stairBottomY);
        var pool = BuildTidePool(group, set, root, terrain);
        BuildSigns(group, root, terrain);

        // 장비대 · 할 일 표시를 해변으로
        var table = set.Find("Prep/EquipmentTable");
        Vector3 markerLift = Vector3.up * 1.6f;
        if (table != null)
        {
            Vector3 before = table.position;
            table.localPosition = OnGround(root, terrain, TableLocal);
            table.localRotation = Quaternion.identity;          // 앞(간판)이 -z — 시작 자리 쪽
            if (tut.markers != null && tut.markers.Length > 0 && tut.markers[0] != null && !tut.markers[0].transform.IsChildOf(table))
                tut.markers[0].transform.position += table.position - before;
        }
        if (tut.markers != null && tut.markers.Length > 1 && tut.markers[1] != null && pool != null)
            tut.markers[1].transform.position = pool.transform.position + Vector3.up * 1.1f;

        // 시작 자리 — 해변 남쪽에서 장비대 · 웅덩이 · 놀란을 바라본다
        Vector3 face = TableLocal - SpawnLocal;
        Quaternion yaw = Quaternion.LookRotation(new Vector3(face.x, 0f, face.z), Vector3.up);
        if (seq.spawnPoint != null) seq.spawnPoint.SetPositionAndRotation(root.TransformPoint(OnGround(root, terrain, SpawnLocal)), root.rotation * yaw);
        if (seq.walkStartPoint != null) seq.walkStartPoint.SetPositionAndRotation(root.TransformPoint(OnGround(root, terrain, WalkStartLocal)), root.rotation * yaw);
        seq.introLines = IntroLines;
        EditorUtility.SetDirty(seq);

        // 걸을 수 있는 곳 · 발밑 높이 · 연습 대상 · 안내
        var open = (tut.openAreas ?? new Rect[0]).Where(r => r != SandArea && r != StairArea).ToList();
        open.Add(SandArea);
        open.Add(StairArea);
        tut.openAreas = open.ToArray();
        tut.groundAreas = new[] { SandArea };
        tut.ground = terrain;
        tut.stairArea = StairArea;
        tut.stairTopZ = StairTopZ;
        tut.stairBottomZ = StairBottomZ;
        tut.stairBottomY = stairBottomY;
        if (pool != null) tut.practiceTank = pool;
        tut.pickUpLine = "놀란 옆 장비대에서 관찰기를 집어 보세요.";
        tut.practiceLine = "조수 웅덩이 속 생물에 관찰기를 겨누고, 링이 다 찰 때까지 지켜보세요.";
        tut.doneLine = "첫 기록 성공! 나무 계단으로 테라스에 올라가, 유리문 안 관측소의 엘리베이터로 가세요.";
        EditorUtility.SetDirty(tut);

        Debug.Log($"[Beach] 해변 시작 구간 — 모래사장 {SandArea}, 계단 {StairSteps}칸(높이 {-stairBottomY:0.00}m), 조수 웅덩이 생물 {PoolFish.Length}마리.");
    }

    // ─────────────────────────────────────────────────────────────
    // 땅
    // ─────────────────────────────────────────────────────────────

    /// <summary>로비 로컬 x·z 의 지형 높이 (로비 로컬 y).</summary>
    internal static float GroundY(Transform root, Terrain terrain, Vector2 xz)
    {
        Vector3 w = root.TransformPoint(new Vector3(xz.x, 0f, xz.y));
        float h = terrain.SampleHeight(w) + terrain.transform.position.y;
        return root.InverseTransformPoint(new Vector3(w.x, h, w.z)).y;
    }

    internal static Vector3 OnGround(Transform root, Terrain terrain, Vector3 local) =>
        new Vector3(local.x, GroundY(root, terrain, new Vector2(local.x, local.z)), local.z);

    /// <summary>데크 남쪽 끝(높이 0, 부두 벽 앞면)에서 모래까지 나무 계단 — 디딤판 · 양옆 옆판 · 손잡이.</summary>
    static void BuildStairs(Transform group, float bottomY)
    {
        var stairs = new GameObject("Stairs").transform;
        stairs.SetParent(group, false);
        var wood = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachStairWood", new Color(0.46f, 0.33f, 0.22f), 0.25f, 0f, false);
        var rail = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachStairRail", new Color(0.30f, 0.22f, 0.15f), 0.3f, 0f, false);
        float width = StairArea.width - 0.1f, cx = StairArea.center.x;
        float run = (StairTopZ - StairBottomZ) / StairSteps;
        float rise = -bottomY / StairSteps;
        for (int i = 0; i < StairSteps; i++)
        {
            // 위에서 i 번째 디딤판 — 윗면이 -(i+1)·rise
            float top = -(i + 1) * rise;
            float z = StairTopZ - (i + 0.5f) * run;
            DomeAquariumLobbyBuilder.Box($"Tread_{i}", stairs, new Vector3(cx, top - 0.03f, z), new Vector3(width, 0.06f, run + 0.02f), wood);
        }
        float length = Mathf.Sqrt((StairTopZ - StairBottomZ) * (StairTopZ - StairBottomZ) + bottomY * bottomY);
        float pitch = Mathf.Atan2(-bottomY, StairTopZ - StairBottomZ) * Mathf.Rad2Deg;
        Vector3 mid = new Vector3(0f, bottomY * 0.5f - 0.12f, (StairTopZ + StairBottomZ) * 0.5f);
        foreach (float side in new[] { -1f, 1f })
        {
            float x = cx + side * (width * 0.5f + 0.03f);
            DomeAquariumLobbyBuilder.Box(side < 0 ? "StringerL" : "StringerR", stairs, new Vector3(x, mid.y, mid.z), new Vector3(0.05f, 0.22f, length), rail).transform.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
            DomeAquariumLobbyBuilder.Box(side < 0 ? "HandrailL" : "HandrailR", stairs, new Vector3(x, mid.y + 0.95f, mid.z), new Vector3(0.05f, 0.05f, length), rail).transform.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
            for (int k = 0; k <= 2; k++)
            {
                float z = Mathf.Lerp(StairTopZ - 0.1f, StairBottomZ + 0.1f, k / 2f);
                float y = Mathf.Lerp(0f, bottomY, Mathf.InverseLerp(StairTopZ, StairBottomZ, z));
                DomeAquariumLobbyBuilder.Box($"Post_{(side < 0 ? "L" : "R")}{k}", stairs, new Vector3(x, y + 0.45f, z), new Vector3(0.06f, 0.95f, 0.06f), rail);
            }
        }
        foreach (var c in stairs.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    }

    /// <summary>해변에서 관측소를 알아보게 — 남쪽 외벽 간판과 바닷속 벽화, 계단 아래 표지판. 해변(남쪽, -z)에서 읽힌다.</summary>
    static void BuildSigns(Transform group, Transform root, Terrain terrain)
    {
        var signs = new GameObject("Signs").transform;
        signs.SetParent(group, false);
        // 남쪽 외벽(WallBack, 두께 0.2m)은 벽화가 다 채우고, 이름은 지붕 끝에 세운 간판에
        BuildMural(signs);
        BuildRoofSign(signs);
        var wood = DomeAquariumLobbyBuilder.LitMaterial("Mat_BeachStairRail", new Color(0.30f, 0.22f, 0.15f), 0.3f, 0f, false);
        Vector3 at = OnGround(root, terrain, new Vector3(8.35f, 0f, -4.05f));
        var post = DomeAquariumLobbyBuilder.Box("StairSignPost", signs, at + Vector3.up * 0.6f, new Vector3(0.07f, 1.2f, 0.07f), wood);
        if (post.GetComponent<Collider>() is Collider postCol) Object.DestroyImmediate(postCol);
        var board = DomeAquariumLobbyBuilder.Box("StairSignBoard", signs, at + new Vector3(0f, 1.25f, 0f), new Vector3(0.7f, 0.3f, 0.04f), wood);
        if (board.GetComponent<Collider>() is Collider boardCol) Object.DestroyImmediate(boardCol);
        DomeAquariumLobbyBuilder.Label("StairSign", signs, at + new Vector3(0f, 1.25f, -0.03f), 0f, new Vector2(0.66f, 0.28f),
            "<b>↑ 관측소 · 테라스</b>", 0.09f, new Color(0.97f, 0.94f, 0.86f), TMPro.TextAlignmentOptions.Center);
    }

    /// <summary>
    /// 남쪽 외벽을 다 채우는 바닷속 벽화 12.4 × 4.5m (바닥 높이 0 ~ 지붕 처마 밑) — 안에서 만날 생물(혹등고래 · 쥐가오리 · 돌고래 ·
    /// 바다거북 · 산호초 물고기)을 그린 그림. 그림은 DomeAquariumMuralBuilder 가 게임 속 모델로 찍어 둔다(없으면 지금 찍는다).
    /// 햇빛 · 해 질 녘을 받도록 Lit 재질. 벽 아래는 모래가 0.4~0.9m 낮아 틈이 보였다 — 기초 벽으로 땅까지 막는다.
    /// </summary>
    static void BuildMural(Transform signs)
    {
        Texture2D art = DomeAquariumMuralBuilder.LoadOrRender();
        if (art == null) { Debug.LogWarning("[Beach] 벽화 그림을 만들지 못해 벽화를 건너뛴다."); return; }
        var mat = DomeAquariumLobbyBuilder.LitMaterial("Mat_ObservatoryMural", Color.white, 0.12f, 0f, false);
        mat.SetTexture("_BaseMap", art);
        EditorUtility.SetDirty(mat);

        Vector2 size = DomeAquariumMuralBuilder.WallSize;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);   // 앞면이 -z(해변)
        quad.name = "Mural";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(signs, false);
        quad.transform.localPosition = new Vector3(0f, size.y * 0.5f, -0.205f);   // 외벽 앞면(z -0.2) 바로 앞
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 기초 벽 — 벽화 아래 끝(높이 0)에서 모래 속까지, 벽화보다 4cm 내밀어 턱을 만든다
        var stone = DomeAquariumLobbyBuilder.LitMaterial("Mat_ObservatoryPlinth", new Color(0.58f, 0.56f, 0.52f), 0.2f, 0f, false);
        NoCollider(DomeAquariumLobbyBuilder.Box("Plinth", signs, new Vector3(0f, -0.65f, -0.125f), new Vector3(size.x + 0.06f, 1.3f, 0.25f), stone));
    }

    /// <summary>
    /// 지붕 남쪽 끝에 세운 가게 간판 — 짙은 남색 판에 밝은 글씨, 위아래 옥색 테. 글씨는 빛을 받지 않는 TMP 라 해 진 뒤에도 불 켠 간판처럼 보인다.
    /// 지붕(Ceiling)은 높이 4.5~4.6, 외벽보다 0.2m 앞(z -0.4)까지 나와 있다 — 간판 앞면을 그 끝에 맞춘다.
    /// </summary>
    static void BuildRoofSign(Transform signs)
    {
        var board = DomeAquariumLobbyBuilder.LitMaterial("Mat_RoofSignBoard", new Color(0.05f, 0.12f, 0.19f), 0.4f, 0.1f, false);
        var trim = DomeAquariumLobbyBuilder.LitMaterial("Mat_RoofSignTrim", new Color(0.20f, 0.62f, 0.72f), 0.5f, 0.3f, false);
        const float bottom = 4.6f, height = 1.25f, width = 8.6f, depth = 0.22f, front = -0.40f;
        float cz = front + depth * 0.5f;
        NoCollider(DomeAquariumLobbyBuilder.Box("RoofSignBoard", signs, new Vector3(0f, bottom + height * 0.5f, cz), new Vector3(width, height, depth), board));
        NoCollider(DomeAquariumLobbyBuilder.Box("RoofSignTrimTop", signs, new Vector3(0f, bottom + height + 0.03f, cz), new Vector3(width + 0.1f, 0.06f, depth + 0.04f), trim));
        NoCollider(DomeAquariumLobbyBuilder.Box("RoofSignTrimBottom", signs, new Vector3(0f, bottom + 0.03f, cz), new Vector3(width + 0.1f, 0.06f, depth + 0.04f), trim));
        DomeAquariumLobbyBuilder.Label("ObservatorySign", signs, new Vector3(0f, bottom + height * 0.6f, front - 0.012f), 0f, new Vector2(width - 0.4f, 0.8f),
            "<b>STILL 해양 관측소</b>", 0.66f, new Color(0.97f, 0.96f, 0.90f), TMPro.TextAlignmentOptions.Center);
        DomeAquariumLobbyBuilder.Label("ObservatorySignSub", signs, new Vector3(0f, bottom + 0.24f, front - 0.012f), 0f, new Vector2(width - 0.4f, 0.24f),
            "STILL  OCEAN  OBSERVATORY", 0.16f, new Color(0.55f, 0.88f, 0.95f), TMPro.TextAlignmentOptions.Center);
    }

    static void NoCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() is Collider c) Object.DestroyImmediate(c);
    }

    // ─────────────────────────────────────────────────────────────
    // 조수 웅덩이
    // ─────────────────────────────────────────────────────────────

    static PracticeTank BuildTidePool(Transform group, Transform set, Transform root, Terrain terrain)
    {
        var pool = new GameObject("TidePool").transform;
        pool.SetParent(group, false);
        Vector3 at = OnGround(root, terrain, PoolLocal);
        pool.localPosition = at;

        // 지형을 파지 않고 바위 턱으로 물을 담는다 — 예전에는 모래 위 0.2m 에 얇은 물 원판만 있어 유리판이 떠 있는 것처럼 보였다.
        // 턱 안쪽 벽이 물 가장자리를 가리고, 바닥은 젖은 모래, 물은 턱보다 조금 낮다.
        var wet = DomeAquariumLobbyBuilder.LitMaterial("Mat_TidePoolBed", new Color(0.36f, 0.32f, 0.25f), 0.55f, 0f, false);
        var water = DomeAquariumLobbyBuilder.LitMaterial("Mat_TidePoolWater", new Color(0.24f, 0.50f, 0.56f, 0.38f), 0.92f, 0f, true);
        var stone = DomeAquariumLobbyBuilder.LitMaterial("Mat_TidePoolRock", new Color(0.31f, 0.28f, 0.25f), 0.35f, 0f, false);
        BuildPoolRim(pool, root, terrain, at, stone);
        Disc("Bed", pool, PoolBed, PoolInnerRadius.x * 0.95f, PoolInnerRadius.y * 0.95f, wet);
        Disc("Water", pool, PoolWater, PoolInnerRadius.x * 1.02f, PoolInnerRadius.y * 1.02f, water);   // 가장자리가 안쪽 벽 속에 묻히게

        // 턱 위 바위 — 해안 바위 모델을 작게
        // 둥근 바위(rock_07 · rock_09) — 절벽 단면(rock_face)은 작게 줄이면 얇은 막대처럼 보였다
        var rockSource = set.Find("Coast/Rocks")?.GetComponentsInChildren<MeshFilter>(true)
            .FirstOrDefault(m => m.sharedMesh != null && (m.sharedMesh.name.Contains("rock_07") || m.sharedMesh.name.Contains("rock_09")));
        var rng = new System.Random(77);
        int n = 8;
        Vector2 rimMid = PoolInnerRadius + Vector2.one * (PoolRimWidth * 0.5f);
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
            var p = new Vector3(Mathf.Cos(a) * rimMid.x, PoolRimTop - 0.02f, Mathf.Sin(a) * rimMid.y);
            float size = 0.24f + (float)rng.NextDouble() * 0.22f;
            GameObject r;
            if (rockSource != null)
            {
                r = new GameObject($"Rock_{i:00}");
                r.AddComponent<MeshFilter>().sharedMesh = rockSource.sharedMesh;
                r.AddComponent<MeshRenderer>().sharedMaterials = rockSource.GetComponent<MeshRenderer>().sharedMaterials;
                float m = Mathf.Max(rockSource.sharedMesh.bounds.size.x, rockSource.sharedMesh.bounds.size.z);
                r.transform.SetParent(pool, false);
                r.transform.localScale = new Vector3(size / m, size * 0.7f / Mathf.Max(0.01f, rockSource.sharedMesh.bounds.size.y), size / m);
            }
            else
            {
                r = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(r.GetComponent<Collider>());
                r.name = $"Rock_{i:00}";
                r.transform.SetParent(pool, false);
                r.transform.localScale = new Vector3(size, size * 0.6f, size);
                r.GetComponent<Renderer>().sharedMaterial = wet;
            }
            r.transform.localPosition = p + Vector3.down * 0.04f;
            r.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 생물 — 연습 수조와 같은 방식(PracticeTank). 바닥 종은 기어 다닌다
        var life = new GameObject("Life");
        life.transform.SetParent(pool, false);
        life.transform.localPosition = new Vector3(0f, PoolInnerHalf.y + 0.02f, 0f);
        var tank = life.AddComponent<PracticeTank>();
        tank.fish = PoolFish.Select(name => AssetDatabase.LoadAssetAtPath<FishData>($"Assets/DomeAquarium/FishData/{name}.asset"))
                            .Where(f => f != null).ToArray();
        tank.innerHalfSize = PoolInnerHalf;
        tank.speedScale = 1f;
        tank.crawlBand = 0.05f;
        if (tank.fish.Length < PoolFish.Length)
            Debug.LogWarning("[Beach] 조수 웅덩이 종 FishData 가 일부 없다 — 먼저 '21. 심해 생물 · 고래 만들기' 를 돌릴 것.");
        return tank;
    }

    /// <summary>
    /// 웅덩이를 두르는 바위 턱 — 바깥 벽(모래 속까지) · 윗면 · 안쪽 벽(바닥까지)을 각진 면으로. 바깥 둘레 · 높이만 조금씩 들쭉날쭉하고,
    /// 안쪽 벽은 물 높이에서 물 원판보다 살짝 안쪽이라 물 가장자리가 벽 속에 묻힌다. 바깥 벽 아래 끝은 그 자리 모래 높이를 따른다.
    /// </summary>
    static void BuildPoolRim(Transform pool, Transform root, Terrain terrain, Vector3 at, Material mat)
    {
        const int seg = 28;
        var rng = new System.Random(31);
        Vector2 ri = PoolInnerRadius, ro = PoolInnerRadius + Vector2.one * PoolRimWidth;
        var outerBottom = new Vector3[seg];
        var outerTop = new Vector3[seg];
        var innerTop = new Vector3[seg];
        var innerBottom = new Vector3[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float jo = 1f + ((float)rng.NextDouble() - 0.5f) * 0.16f;
            float jh = ((float)rng.NextDouble() - 0.5f) * 0.06f;
            var o = new Vector2(u.x * ro.x, u.y * ro.y) * jo;
            float ground = GroundY(root, terrain, new Vector2(at.x + o.x * 1.08f, at.z + o.y * 1.08f)) - at.y;
            outerBottom[i] = new Vector3(o.x * 1.08f, ground - 0.1f, o.y * 1.08f);
            outerTop[i] = new Vector3(o.x, PoolRimTop + jh - 0.03f, o.y);
            innerTop[i] = new Vector3(u.x * ri.x * 1.03f, PoolRimTop + jh, u.y * ri.y * 1.03f);
            innerBottom[i] = new Vector3(u.x * ri.x * 0.94f, PoolBed - 0.01f, u.y * ri.y * 0.94f);
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            // 앞면(Unity 는 cross(b-a, c-a) 쪽)이 facing 을 보게 감는 방향을 고른다. 면마다 꼭짓점을 따로 둬 각지게 보인다
            int k = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) >= 0f) tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            else tris.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
        }
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            Vector3 radial = outerTop[i] + outerTop[j];
            radial.y = 0f;
            radial.Normalize();
            Quad(outerBottom[i], outerTop[i], outerTop[j], outerBottom[j], radial);    // 바깥 벽
            Quad(outerTop[i], innerTop[i], innerTop[j], outerTop[j], Vector3.up);       // 윗면
            Quad(innerTop[i], innerBottom[i], innerBottom[j], innerTop[j], -radial);   // 안쪽 벽 — 웅덩이 가운데를 본다
        }
        var mesh = new Mesh { name = "TidePoolRim" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        const string path = "Assets/DomeAquarium/Coast/Meshes/TidePoolRim.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved != null) { saved.Clear(); EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); mesh = saved; }
        else AssetDatabase.CreateAsset(mesh, path);

        var go = new GameObject("Rim");
        go.transform.SetParent(pool, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void Disc(string name, Transform parent, float y, float rx, float rz, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.transform.localScale = new Vector3(rx * 2f, 0.005f, rz * 2f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
#endif
