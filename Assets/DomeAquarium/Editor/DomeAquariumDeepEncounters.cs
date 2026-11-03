#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 깊은 바다의 만남 넷.
///   1,000m 찍을까 바라볼까(FlashOrWatch) — 다가온 신호의 주인 앞에서 B · Y 버튼 플래시.
///   1,000m 고래 낙하(WhaleFall) — 돔 아래 바닥(y −8.1)에 고래 뼈 · 뼈먹는 벌레 · 하얀 게 · 박테리아 매트. 만남 뒤 바닥 조명으로 보여 준다.
///   200m 산갈치(OarfishEncounter) — 드문 만남. FishData · 프리팹(세운 메시)도 만든다.
///   엔딩의 밤 실제 크기 대왕고래(NightBlueWhale) — 로비 창밖 만, 창에서 50m 남짓을 해안과 나란히.
/// 모델: 고래 뼈대(20lux) · 산갈치(TheoBaldeteBRID) — Sketchfab CC-BY, Blender 로 줄였다(Assets/ThirdParty/Sketchfab/DeepSea). 게는 ⑤ 열수구의 걷는 게.
/// 순서: 18(층) · 31(밤 이야기) 뒤. 9(로비) 안에서도 돈다. 따로 다시 세울 때는 34.
/// </summary>
public static class DomeAquariumDeepEncounters
{
    const string MatDir = "Assets/DomeAquarium/Materials";
    const string DeepDir = "Assets/ThirdParty/Sketchfab/DeepSea";
    const float SeabedY = -8.12f;                                         // 돔 아래 바닥 (눈 1.36m 보다 9.5m 아래)
    static readonly Vector3 WhaleFallAt = new Vector3(3.6f, SeabedY, 6.2f);   // 앞 오른쪽 7m — 내려다보면 55°
    const float WhaleFallLength = 10f;                                    // 밍크고래만 한 뼈 (돔 반지름 12m 안에 눕게)

    [MenuItem("DomeAquarium/34. 깊은 바다의 만남 (찍을까 바라볼까 · 고래 낙하 · 산갈치 · 밤의 대왕고래)", priority = 34)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[DeepEncounters] 플레이 모드에서는 세울 수 없다."); return; }
        Build();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 1,000m: 플래시로 찍기", priority = 222)]
    public static void DebugFlash()
    {
        if (!EditorApplication.isPlaying) return;
        var f = Object.FindFirstObjectByType<FlashOrWatch>();
        if (f != null) f.DebugFlash();
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 200m: 산갈치 바로 부르기", priority = 220)]
    public static void DebugOarfish()
    {
        if (!EditorApplication.isPlaying) return;
        var o = Object.FindFirstObjectByType<OarfishEncounter>(FindObjectsInactive.Include);
        var cam = Camera.main;
        if (o != null && cam != null) o.StartCoroutine(o.Appear(cam.transform));
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 밤: 대왕고래 지나가기", priority = 230)]
    public static void DebugNightWhale()
    {
        if (!EditorApplication.isPlaying) return;
        var w = Object.FindFirstObjectByType<NightBlueWhale>(FindObjectsInactive.Include);
        if (w == null) return;
        w.DebugReset();
        w.StartCoroutine(w.Play());
    }

    public static void Build()
    {
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director == null) { Debug.LogWarning("[DeepEncounters] DiveDirector 가 없다 (18번 메뉴)."); return; }
        BuildFlash(director);
        BuildWhaleFall(director);
        BuildOarfish(director);
        BuildNightWhale(director);
        DomeAquariumEncyclopediaSetup.EnsurePhotoRow();                  // 관찰 일지 — 내가 찍은 사진 줄 · 산갈치까지 종 목록
        EditorUtility.SetDirty(director);
    }

    // ─────────────────────────────────────────────────────────────
    // 찍을까 바라볼까
    // ─────────────────────────────────────────────────────────────

    static void BuildFlash(DiveDirector director)
    {
        var old = director.transform.Find("FlashOrWatch");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("FlashOrWatch");
        go.transform.SetParent(director.transform, false);
        var f = go.AddComponent<FlashOrWatch>();
        f.creature = director.unknown != null ? director.unknown : Object.FindFirstObjectByType<UnknownCreature>(FindObjectsInactive.Include);
        f.aim = director.aim;
        f.deviceLight = director.deviceLight;
        f.lobby = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0.9f;
        f.sound = src;
        f.shutter = DomeAquariumDepthSounds.EnsureShutter();
        director.flash = f;
        EditorUtility.SetDirty(f);
    }

    // ─────────────────────────────────────────────────────────────
    // 고래 낙하
    // ─────────────────────────────────────────────────────────────

    static void BuildWhaleFall(DiveDirector director)
    {
        var old = director.transform.Find("WhaleFall");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("WhaleFall");
        go.transform.SetParent(director.transform, false);
        go.transform.position = WhaleFallAt;
        var fall = go.AddComponent<WhaleFall>();
        var group = new GameObject("Group").transform;
        group.SetParent(go.transform, false);
        fall.group = group.gameObject;
        var center = new GameObject("Center").transform;
        center.SetParent(go.transform, false);
        fall.center = center;

        // 바닥 — 빛이 닿을 때만 보이는 퇴적물 원판 (돔 바닥은 심해 생물 셰이더가 아니라 조명 웅덩이가 보이지 않았다)
        var sediment = DomeAquariumNightStories.VentMaterial("Mat_WhaleFallSediment", null, new Color(0.34f, 0.31f, 0.27f));
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.name = "Sediment";
        disc.transform.SetParent(group, false);
        disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        disc.transform.localScale = new Vector3(16f, 0.01f, 16f);
        disc.GetComponent<Renderer>().sharedMaterial = sediment;

        // 박테리아 매트 — 뼈 둘레 바닥의 희끄무레한 얼룩 (불규칙한 원판)
        var mat = DomeAquariumNightStories.VentMaterial("Mat_WhaleFallMat", null, new Color(0.55f, 0.52f, 0.40f));
        var rng = new System.Random(1000);
        for (int i = 0; i < 7; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 1.2f + (float)rng.NextDouble() * 3.4f;
            var patch = new GameObject($"BacterialMat_{i}", typeof(MeshFilter), typeof(MeshRenderer));
            patch.transform.SetParent(group, false);
            patch.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.035f, Mathf.Sin(a) * r * 0.6f);
            patch.GetComponent<MeshFilter>().sharedMesh = Blob(rng, 0.3f + (float)rng.NextDouble() * 0.45f);
            patch.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 뼈 — 옆으로 누워 바닥에 반쯤 묻혔다
        var bonesHolder = new GameObject("Bones").transform;
        bonesHolder.SetParent(group, false);
        bonesHolder.localRotation = Quaternion.Euler(0f, 28f, 0f);
        var roll = new GameObject("Roll").transform;
        roll.SetParent(bonesHolder, false);
        roll.localRotation = Quaternion.Euler(78f, 0f, 0f);                  // 길이(X) 축으로 굴려 옆으로 눕힌다
        var bones = DomeAquariumNightStories.ModelFrom($"{DeepDir}/WhaleSkeleton.glb", WhaleFallLength, false, roll, DomeAquariumNightStories.VentMaterialSet.Keep);
        if (bones != null)
        {
            // 눕힌 뒤 가장 낮은 곳이 바닥 0.45m 아래로 — 반쯤 묻힌다
            var rs = bones.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            roll.localPosition = new Vector3(-(b.center.x - go.transform.position.x), -(b.min.y - go.transform.position.y) - 0.45f, -(b.center.z - go.transform.position.z));
            roll.localPosition = Quaternion.Inverse(bonesHolder.localRotation) * roll.localPosition;
        }

        // 뼈먹는 벌레(오세닥스) — 뼈 위 붉은 솜털. 뼈 메시의 위쪽 꼭짓점에 작은 붉은 팔면체 덩어리
        if (bones != null)
        {
            var worm = DomeAquariumNightStories.VentMaterial("Mat_OsedaxRed", null, new Color(0.85f, 0.12f, 0.1f));
            var pts = new List<Vector3>();
            foreach (var mf in bones.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var v = mf.sharedMesh.vertices;
                for (int k = 0; k < v.Length; k += 37) pts.Add(go.transform.InverseTransformPoint(mf.transform.TransformPoint(v[k])));
            }
            pts = pts.Where(p => p.y > 0.05f).OrderBy(_ => rng.Next()).Take(220).ToList();
            var tufts = new GameObject("Osedax", typeof(MeshFilter), typeof(MeshRenderer));
            tufts.transform.SetParent(group, false);
            tufts.GetComponent<MeshFilter>().sharedMesh = DomeAquariumNightStories.SaveStoryMesh(Octahedra(pts, 0.045f, rng), "OsedaxTufts");
            tufts.GetComponent<MeshRenderer>().sharedMaterial = worm;
        }

        // 하얀 게 — 뼈 둘레 바닥에
        var walk = DomeAquariumNightStories.LoopedVentClip("CrabWalking", "VentCrabWalk");
        var ctrl = walk != null ? DomeAquariumNightStories.CrabController(walk) : null;
        for (int i = 0; i < 5; i++)
        {
            var holder = new GameObject($"Crab_{i}").transform;
            holder.SetParent(group, false);
            float a = i / 5f * Mathf.PI * 2f + 0.4f;
            holder.localPosition = new Vector3(Mathf.Cos(a) * 2.2f, 0.03f, Mathf.Sin(a) * 1.3f);
            holder.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var crab = DomeAquariumNightStories.VentModel("CrabWalking", 0.45f, false, holder, DomeAquariumNightStories.VentMaterialSet.PaleCrab);
            if (crab == null) continue;
            foreach (var anim in crab.GetComponentsInChildren<Animator>(true)) { anim.runtimeAnimatorController = ctrl; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.speed = 0.25f; }
            foreach (var smr in crab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        }

        var sound = go.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.6f;
        fall.sound = sound;
        fall.switchClip = DomeAquariumDepthSounds.EnsureShutter();          // 굵은 스위치 소리 대신 짧은 딸깍
        group.gameObject.SetActive(false);
        director.whaleFall = fall;
        EditorUtility.SetDirty(fall);
        Debug.Log($"[DeepEncounters] 1,000m 고래 낙하 — {WhaleFallAt}, 뼈 {WhaleFallLength}m.");
    }

    /// <summary>바닥에 눕힌 불규칙한 원판 (박테리아 매트).</summary>
    static Mesh Blob(System.Random rng, float radius)
    {
        const int n = 14;
        var v = new List<Vector3> { Vector3.zero };
        var t = new List<int>();
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f, r = radius * (0.6f + (float)rng.NextDouble() * 0.5f);
            v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < n; i++) { t.Add(0); t.Add(1 + (i + 1) % n); t.Add(1 + i); }
        var m = new Mesh { name = "BacterialMat" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>작은 팔면체 여럿을 한 메시로 — 뼈먹는 벌레 무리 (점마다 크기 · 방향을 조금씩).</summary>
    static Mesh Octahedra(List<Vector3> points, float size, System.Random rng)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        int[] faces = { 2, 0, 4, 2, 4, 1, 2, 1, 5, 2, 5, 0, 3, 4, 0, 3, 1, 4, 3, 5, 1, 3, 0, 5 };
        foreach (var p in points)
        {
            float s = size * (0.6f + (float)rng.NextDouble() * 0.8f);
            int i0 = v.Count;
            foreach (var d in dirs) v.Add(p + d * s * (d == Vector3.up ? 1.6f : 1f));
            foreach (var f in faces) t.Add(i0 + f);
        }
        var m = new Mesh { name = "OsedaxTufts" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ─────────────────────────────────────────────────────────────
    // 산갈치
    // ─────────────────────────────────────────────────────────────

    static void BuildOarfish(DiveDirector director)
    {
        var fish = EnsureOarfishData(out GameObject prefab, out Mesh upright, out Material[] mats);
        var m200 = AssetDatabase.LoadAssetAtPath<DiveMission>("Assets/DomeAquarium/Missions/Mission_200m.asset");
        if (m200 != null && !m200.oarfish) { m200.oarfish = true; EditorUtility.SetDirty(m200); }

        var old = director.transform.Find("Oarfish");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("Oarfish");
        go.transform.SetParent(director.transform, false);
        var enc = go.AddComponent<OarfishEncounter>();
        enc.fish = fish;
        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(go.transform, false);
        body.GetComponent<MeshFilter>().sharedMesh = upright;
        var r = body.GetComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        enc.body = body.transform;
        enc.wave = new[] { body.GetComponent<MeshFilter>() };
        enc.length = fish != null ? fish.bodyLength : 4.5f;
        director.oarfish = enc;
        EditorUtility.SetDirty(enc);
        Debug.Log("[DeepEncounters] 200m 산갈치 — 드문 만남 (Mission_200m.oarfish).");
    }

    /// <summary>
    /// 산갈치 FishData · 세운 메시 · 프리팹. 모델은 머리가 +X — 머리가 위(+Y), 옆구리가 ±Z 를 보게 세워 4.5m 로 굽는다.
    /// 재질은 심해 생물 셰이더 (200m 는 관찰기 조명 · 층 빛으로 보인다).
    /// </summary>
    static FishData EnsureOarfishData(out GameObject prefab, out Mesh upright, out Material[] mats)
    {
        const float length = 4.5f;
        prefab = null; upright = null; mats = new Material[0];
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{DeepDir}/Oarfish.glb");
        if (src == null) { Debug.LogWarning("[DeepEncounters] Oarfish.glb 가 없다."); return null; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        inst.transform.position = Vector3.zero;
        var mf = inst.GetComponentInChildren<MeshFilter>(true);
        var mr = mf.GetComponent<MeshRenderer>();
        var srcMesh = mf.sharedMesh;
        Matrix4x4 toRoot = inst.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        Matrix4x4 rootToWorld = Matrix4x4.Rotate(inst.transform.rotation);
        var stand = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f));           // +X(머리) → +Y
        var verts = srcMesh.vertices;
        var normals = srcMesh.normals;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = stand.MultiplyPoint3x4(rootToWorld.MultiplyPoint3x4(toRoot.MultiplyPoint3x4(verts[i])));
            if (normals != null && normals.Length == verts.Length) normals[i] = stand.MultiplyVector(rootToWorld.MultiplyVector(toRoot.MultiplyVector(normals[i]))).normalized;
        }
        var b = GeometryUtility.CalculateBounds(verts, Matrix4x4.identity);
        float k = length / Mathf.Max(0.001f, b.size.y);
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3((verts[i].x - b.center.x) * k, (verts[i].y - b.min.y) * k, (verts[i].z - b.center.z) * k);   // 꼬리 끝이 원점
        upright = new Mesh { name = "OarfishUpright", indexFormat = srcMesh.indexFormat };
        upright.vertices = verts;
        if (normals != null && normals.Length == verts.Length) upright.normals = normals;
        upright.uv = srcMesh.uv;
        upright.subMeshCount = srcMesh.subMeshCount;
        for (int s = 0; s < srcMesh.subMeshCount; s++) upright.SetTriangles(srcMesh.GetTriangles(s), s);
        upright.RecalculateBounds();
        string meshPath = "Assets/DomeAquarium/Creatures/Meshes/OarfishUpright.asset";
        System.IO.Directory.CreateDirectory("Assets/DomeAquarium/Creatures/Meshes");
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(upright, meshPath);
        mats = mr.sharedMaterials.Select(m => DomeAquariumNightStories.VentMaterialFrom("Oarfish", m, DomeAquariumNightStories.VentMaterialSet.Keep)).ToArray();
        Object.DestroyImmediate(inst);

        // 프리팹 — 관찰 일지 3D 미리보기 · 기록 카드용 (세운 그대로)
        var pgo = new GameObject("Oarfish", typeof(MeshFilter), typeof(MeshRenderer));
        pgo.GetComponent<MeshFilter>().sharedMesh = upright;
        pgo.GetComponent<MeshRenderer>().sharedMaterials = mats;
        string prefabPath = "Assets/DomeAquarium/Creatures/Prefabs/Oarfish.prefab";
        prefab = PrefabUtility.SaveAsPrefabAsset(pgo, prefabPath);
        Object.DestroyImmediate(pgo);

        string dataPath = "Assets/DomeAquarium/FishData/Oarfish.asset";
        var fish = AssetDatabase.LoadAssetAtPath<FishData>(dataPath);
        if (fish == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/Telescopefish.asset");
            fish = template != null ? Object.Instantiate(template) : ScriptableObject.CreateInstance<FishData>();
            AssetDatabase.CreateAsset(fish, dataPath);
        }
        fish.speciesId = "Oarfish";
        fish.koreanName = "산갈치";
        fish.englishName = "Giant oarfish";
        fish.scientificName = "Regalecus glesne";
        fish.description = "뼈 있는 물고기 중 가장 길게 자라는 물고기예요. 몸은 리본처럼 납작하고, 머리 위로 볏처럼 솟은 지느러미부터 꼬리까지 등지느러미가 이어져요. " +
                           "머리를 위로 하고 세로로 선 채 등지느러미를 물결처럼 흔들며 천천히 헤엄쳐요. 깊은 바다에 살아서 살아 있는 모습을 본 사람은 아주 드물어요.";
        fish.sizeTag = "몸길이 수 m";
        fish.habitatTag = "200~1,000m";
        fish.rarity = FishRarity.Legendary;
        fish.prefab = prefab;
        fish.bodyLength = length;
        fish.swimScale = 1f;
        fish.revealScale = 0.2f;
        fish.swimSpeed = 0.2f;
        EditorUtility.SetDirty(fish);
        AssetDatabase.SaveAssets();
        return fish;
    }

    // ─────────────────────────────────────────────────────────────
    // 엔딩의 밤 — 실제 크기 대왕고래
    // ─────────────────────────────────────────────────────────────

    static void BuildNightWhale(DiveDirector director)
    {
        var bay = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        var data = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/BlueWhale.asset");
        if (bay == null || data == null || data.prefab == null) { Debug.LogWarning("[DeepEncounters] 만(19번) · 대왕고래 FishData 가 없어 밤의 대왕고래를 건너뛴다."); return; }
        Transform coast = bay.transform.parent;
        var old = coast.Find("NightBlueWhale");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("NightBlueWhale").transform;
        root.SetParent(coast, false);
        var nw = root.gameObject.AddComponent<NightBlueWhale>();
        float seaY = coast.TransformPoint(new Vector3(0f, bay.seaY, 0f)).y;

        // 길 — 로비 통유리(x≈6)에서 30m 앞(테라스 데크 너머로 수면이 보이기 시작하는 곳 바로 뒤, 수심 3m 남짓), 해안과 나란히 남 → 북 120m.
        // 등은 수면 위 1.3m — 숨 쉬러 올라온 대왕고래는 등이 길게 드러난다. 54m 밖 · 0.3m 로는 낮은 눈높이에서 실처럼 얇아 크기가 느껴지지 않았다
        var from = new GameObject("From").transform; from.SetParent(root, false);
        var to = new GameObject("To").transform; to.SetParent(root, false);
        from.position = new Vector3(36f, seaY + 1.3f, -80f);
        to.position = new Vector3(36f, seaY + 1.3f, 40f);
        nw.from = from;
        nw.to = to;

        // 몸 — 원점이 등 꼭대기, +Z 머리. 25m
        var whale = new GameObject("Whale").transform;
        whale.SetParent(root, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(data.prefab);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;
        foreach (var sync in model.GetComponentsInChildren<FishAnimatorSync>(true)) Object.DestroyImmediate(sync);
        var rs = model.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float k = 25f / Mathf.Max(0.01f, b.size.z);
        model.transform.localScale = Vector3.one * k;
        model.transform.SetParent(whale, false);
        model.transform.localPosition = new Vector3(-b.center.x * k, -b.max.y * k, -b.center.z * k);
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.updateWhenOffscreen = true; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        // 달빛에 젖은 등 — 심해 생물 셰이더는 밤에 거의 검다. 바탕 무늬를 아주 약하게 스스로 빛나게 해 등의 윤곽이 보이게
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string path = $"{MatDir}/Mat_NightBlueWhale_{i}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(mats[i]); AssetDatabase.CreateAsset(m, path); }
                else m.CopyPropertiesFromMaterial(mats[i]);
                if (m.HasProperty("_BaseMap") && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                m.SetColor("_EmissionColor", new Color(0.22f, 0.27f, 0.34f));
                EditorUtility.SetDirty(m);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
        foreach (var anim in model.GetComponentsInChildren<Animator>(true)) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        nw.whale = whale;

        // 몸 둘레 수면의 푸른 빛 — 몸을 따라 움직이며 뒤로 꼬리처럼 남는다 (야광 플랑크톤)
        var glowGo = new GameObject("PlanktonGlow");
        glowGo.transform.SetParent(whale, false);
        glowGo.transform.localPosition = new Vector3(0f, -1.15f, 0f);          // 수면 바로 위 (원점이 등 꼭대기, 수면보다 1.3m 위)
        var ps = glowGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.9f, 1f, 1f), new Color(0.25f, 0.65f, 1f, 0.85f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1400;
        var em = ps.emission; em.rateOverTime = 460f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;   // 몸이 지나간 물이 살짝 일렁이며 퍼진다
        vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0.12f); vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(7.5f, 0.1f, 28f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var glowMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_PlanktonWake", "Universal Render Pipeline/Particles/Unlit");
        glowMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        glowMat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(glowMat);
        var pr = glowGo.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = glowMat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        nw.glow = ps;

        // 숨 — 만의 고래 물보라를 크게 (대왕고래 숨은 9m 까지 곧게 솟는다), 머리 쪽 20%
        if (bay.spout != null)
        {
            var spout = Object.Instantiate(bay.spout.gameObject, whale);
            spout.name = "Spout";
            spout.transform.localPosition = new Vector3(0f, 0.2f, 25f * 0.3f);
            spout.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            spout.transform.localScale = Vector3.one * 2.2f;
            nw.spout = spout.GetComponent<ParticleSystem>();
        }
        var blow = whale.gameObject.AddComponent<AudioSource>();
        blow.playOnAwake = false;
        blow.spatialBlend = 1f;
        blow.minDistance = 20f;
        blow.maxDistance = 400f;
        blow.dopplerLevel = 0f;
        blow.volume = 1f;
        blow.clip = DomeAquariumDepthSounds.EnsureWhaleBlow();
        nw.blow = blow;

        whale.gameObject.SetActive(false);
        director.nightWhale = nw;
        EditorUtility.SetDirty(nw);
        Debug.Log($"[DeepEncounters] 밤의 대왕고래 — 25m, 길 {from.position} → {to.position}.");
    }
}
#endif
