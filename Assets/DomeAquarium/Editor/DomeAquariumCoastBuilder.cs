#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 로비 창밖의 해안 — 하늘 사진(HDRI) 속 바위섬 대신 진짜 땅과 소품을 세운다.
///
/// 배치 (로비 로컬, 창이 +X, 수면 y=-1.2):
///   가운데: 테라스 앞 모래톱. 창에서 14~28m(x 20~34)는 돌고래가 뛰는 곳이라 비워 둔다.
///   왼쪽(+Z) 곶: z≈185 에서 x≈244 까지 뻗은 바위 곶. 오른쪽(-Z) 곶: z≈-140 에서 x≈194 까지, 끝에 등대.
///   두 곶 사이로 열린 바다 끝(수평선)에 섬 셋. 바다 판(600m) 양옆 가장자리 너머는 전부 땅이라 판 끝이 안 보인다.
///   부표 넷(x 60~150m), 테라스 양옆에 정박한 연구선 · 예인선 · 나룻배, 먼바다를 지나가는 화물선 · 여객선(연기 · 뱃고동)과
///   닻 내린 돛단배, 만 위를 도는 갈매기, 테라스 앞으로 밀려와 부서지는 파도, 바위에 이는 물보라.
///
/// 땅은 Unity 터레인(높이맵 257 · 레이어 셋 — 모래 · 풀 · 절벽)이다. 바위 · 부표는 Poly Haven 스캔 모델(CC0)에
/// 메시 LOD 를 만들어 두고 쓴다 — 멀어지면 삼각형이 줄어든다. 배는 Kenney Watercraft Kit(CC0).
/// 전부 LobbySet 아래라 수중 돔에 도착하면 로비와 같이 꺼진다. 실시간 조명 · 그림자 · 충돌체는 쓰지 않는다.
/// </summary>
public static class DomeAquariumCoastBuilder
{
    const string Dir = "Assets/DomeAquarium/Coast";
    const string PolyHaven = "Assets/ThirdParty/PolyHaven";
    const string Kenney = "Assets/ThirdParty/Kenney_WatercraftKit";
    const float SeaY = DomeAquariumLobbyBuilder.SeaLevel;

    // 터레인: x -250~650, z -450~450, 높이 -10~60 (로비 로컬). 로비 바닥이 월드 y=24 라 터레인 바닥(월드 14)도 수조 돔 꼭대기(8)보다 위다.
    static readonly Vector3 TerrainOrigin = new Vector3(-250f, -10f, -450f);
    static readonly Vector3 TerrainSize = new Vector3(900f, 70f, 900f);
    const int HeightRes = 257;                          // 한 칸 3.5m — 가까이 볼 땅은 테라스 옆 모래톱뿐이다
    const int SplatRes = 256;

    [MenuItem("DomeAquarium/19. 로비 바깥 해안 세우기 (지형·하늘·소품)", priority = 19)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Coast] 플레이 모드에서는 세울 수 없다. Play 를 멈추고 다시 실행할 것.");
            return;
        }
        var root = GameObject.Find("LobbyRoot");
        Transform set = root != null ? root.transform.Find("LobbySet") : null;
        var seq = root != null ? root.GetComponent<LobbySequence>() : null;
        if (set == null || seq == null) { Debug.LogError("[Coast] LobbyRoot/LobbySet 이 없다. 먼저 9번 메뉴로 로비를 빌드할 것."); return; }

        // 하늘을 땅 없는 HDRI 로 바꾸고, 바다에 비치는 하늘 반사도 새 하늘로 다시 굽는다.
        seq.lobbySkybox = DomeAquariumLobbyBuilder.SkyMaterial();
        var probe = set.Find("Sea/SkyProbe")?.GetComponent<ReflectionProbe>();
        if (probe != null) probe.customBakedTexture = DomeAquariumLobbyBuilder.LobbySkyReflection(probe);
        DomeAquariumLobbyBuilder.ApplySkyExposure(set.GetComponentInChildren<DepthWallPreview>(true), root.GetComponent<LobbyNight>());
        EditorUtility.SetDirty(seq);

        Build(set, seq);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(Transform set, LobbySequence seq)
    {
        var old = set.Find("Coast");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EnsureFolder(Dir);
        EnsureFolder($"{Dir}/Meshes");

        var coast = new GameObject("Coast").transform;
        coast.SetParent(set, false);

        Terrain terrain = BuildTerrain(coast);
        List<Transform> spots = BuildRocks(coast);
        BuildBuoys(coast);
        BuildBoats(coast);
        Lighthouse lighthouse = BuildLighthouse(coast, seq, terrain);
        BuildGulls(coast, seq);
        ParticleSystem spray = BuildFoam(coast, spots);
        BuildWaves(coast, set, spray);
        BuildWhale(coast, spray);

        // 시간 흐름 — 로비에 머무는 동안 해 질 녘이 조금씩 깊어지고 등대가 켜진다
        var dusk = coast.gameObject.AddComponent<LobbyDusk>();
        dusk.lobby = seq;
        dusk.wall = Object.FindFirstObjectByType<DepthWallPreview>(FindObjectsInactive.Include);
        dusk.lighthouse = lighthouse;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Coast] 해안 세우기 완료 — 터레인 {HeightRes}², 바위 {spots.Count}개, 부표 4 · 정박한 배 3 · 지나가는 배 2 · 등대 · 갈매기 · 파도.");
    }

    // ─────────────────────────────────────────────────────────────
    // 땅 모양
    // ─────────────────────────────────────────────────────────────

    static float Bump(float z, float center, float width)
    {
        float d = (z - center) / width;
        return Mathf.Exp(-d * d * 2.2f);
    }

    // 곶은 건물에서 충분히 떨어져 시작한다 — 가까이 붙이면 창 끝에 거대한 절벽 벽이 섰다.
    static float LeftHead(float z) => Bump(z, 185f, 85f);
    static float RightHead(float z) => Bump(z, -140f, 70f);
    /// <summary>바다 판(z ±300) 가장자리 너머 — 전부 땅으로 덮어 판 끝과 하늘 아랫부분이 안 보이게 한다.</summary>
    static float FarLand(float z) => Smooth01((Mathf.Abs(z - 7.5f) - 270f) / 70f);

    /// <summary>해안선의 x (이보다 작으면 뭍). 테라스 앞(x≈9)은 곧고, 양옆으로 갈수록 곶이 바다로 뻗는다.</summary>
    static float CoastX(float z) => 9f + Mathf.Max(LeftHead(z) * 235f, RightHead(z) * 185f, FarLand(z) * 700f);

    static float Height(float x, float z)
    {
        float s = CoastX(z) - x;                        // 해안선에서 뭍 쪽으로 들어간 거리 (바다 쪽은 음수)
        float rocky = Mathf.Max(LeftHead(z), RightHead(z), FarLand(z));   // 곶일수록 모래톱 없이 절벽
        float n = Mathf.PerlinNoise(x * 0.018f + 3.1f, z * 0.018f + 7.7f);
        float n2 = Mathf.PerlinNoise(x * 0.07f + 11.3f, z * 0.07f + 5.9f);
        float y;
        if (s <= 0f)
            y = SeaY + s * Mathf.Lerp(0.12f, 0.6f, rocky);   // 물속으로 — 절벽 앞은 금방 깊어진다
        else
        {
            // 터레인은 위에서 텍스처를 입혀 가파른 면이 세로로 늘어난다 — 곶도 40° 남짓으로 오르게 하고, 발치는 바위 모델로 가린다.
            float beach = Mathf.Lerp(16f, 3f, rocky);
            float rise = Mathf.Lerp(45f, 24f, rocky);
            float top = Mathf.Lerp(6f, 20f, rocky) + (n - 0.5f) * Mathf.Lerp(6f, 12f, rocky) + (n2 - 0.5f) * 2.5f;
            float shore = SeaY + 1.6f;                  // 모래톱 끝 — 수면보다 0.4m 위
            y = s < beach ? SeaY + s / beach * 1.6f : shore + (top - shore) * Smooth01((s - beach) / rise);
        }
        y = Mathf.Max(y, Island(x, z, 470f, -70f, 60f, 17f), Island(x, z, 545f, 95f, 36f, 10f), Island(x, z, 600f, 10f, 45f, 6f));

        // 건물(홀 · 테라스 · 엘리베이터 칸) 아래는 바닥(0)보다 낮게 — 바닥을 뚫고 올라오지 않게
        float under = RectMask(x, z, -8f, 13f, -2.5f, 21.5f, 4f);
        y = Mathf.Lerp(y, Mathf.Min(y, -0.4f), under);
        return Mathf.Clamp(y, TerrainOrigin.y, TerrainOrigin.y + TerrainSize.y - 1f);
    }

    static float Island(float x, float z, float cx, float cz, float radius, float height)
    {
        float dx = (x - cx) / radius, dz = (z - cz) / (radius * 0.7f);   // 수평선 방향으로 길쭉하게
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float n = Mathf.PerlinNoise(x * 0.05f, z * 0.05f);
        // 둥근 언덕 섬 — 뾰족하면 회색 피라미드처럼 보였다
        return SeaY - 4f + (height + 4f) * Mathf.Sqrt(Smooth01((1.05f - d) * 1.4f)) * (0.8f + 0.4f * n);
    }

    /// <summary>사각형 안이면 1, 바깥 margin 만큼에서 0 으로 부드럽게.</summary>
    static float RectMask(float x, float z, float x0, float x1, float z0, float z1, float margin)
    {
        float dx = Mathf.Max(x0 - x, 0f, x - x1);
        float dz = Mathf.Max(z0 - z, 0f, z - z1);
        return 1f - Smooth01(Mathf.Sqrt(dx * dx + dz * dz) / margin);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ─────────────────────────────────────────────────────────────
    // 터레인
    // ─────────────────────────────────────────────────────────────

    static Terrain BuildTerrain(Transform coast)
    {
        var data = new TerrainData { heightmapResolution = HeightRes };
        data.size = TerrainSize;
        int n = data.heightmapResolution;
        var heights = new float[n, n];
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = TerrainOrigin.x + ix / (float)(n - 1) * TerrainSize.x;
                float z = TerrainOrigin.z + iz / (float)(n - 1) * TerrainSize.z;
                heights[iz, ix] = Mathf.Clamp01((Height(x, z) - TerrainOrigin.y) / TerrainSize.y);
            }
        data.SetHeights(0, 0, heights);

        data.terrainLayers = new[]
        {
            Layer("Sand", "coast_sand_01", 4f),
            Layer("Grass", "aerial_grass_rock", 16f),
            Layer("Cliff", "cliff_side", 10f),
        };
        data.alphamapResolution = SplatRes;
        data.baseMapResolution = 256;
        Paint(data);

        string path = $"{Dir}/CoastTerrain.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        Object.DestroyImmediate(go.GetComponent<TerrainCollider>());   // 걸어 들어가지 않는다
        go.transform.SetParent(coast, false);
        go.transform.localPosition = TerrainOrigin;
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = TerrainMaterial();
        terrain.heightmapPixelError = 6f;
        terrain.basemapDistance = DomeAquariumQuestPerformance.TerrainBasemapDistance;   // 그 밖은 미리 구운 한 장(베이스맵)으로
        terrain.drawInstanced = true;
        terrain.drawTreesAndFoliage = false;
        terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        terrain.allowAutoConnect = false;
        return terrain;
    }

    /// <summary>가파른 곳은 절벽, 물가에서 1~3m 위까지는 모래, 그 위는 풀밭.</summary>
    static void Paint(TerrainData data)
    {
        int res = data.alphamapResolution;
        var map = new float[res, res, 3];
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float u = (ix + 0.5f) / res, v = (iz + 0.5f) / res;
                float y = data.GetInterpolatedHeight(u, v) + TerrainOrigin.y;
                float rock = Smooth01((data.GetSteepness(u, v) - 28f) / 14f);
                float sand = 1f - Smooth01((y - (SeaY + 1.4f)) / 2.4f);
                map[iz, ix, 0] = (1f - rock) * sand;
                map[iz, ix, 1] = (1f - rock) * (1f - sand);
                map[iz, ix, 2] = rock;
            }
        data.SetAlphamaps(0, 0, map);
    }

    static TerrainLayer Layer(string name, string texture, float tile)
    {
        string diffuse = $"{PolyHaven}/Textures/{texture}/{texture}_diff_1k.jpg";
        string normal = $"{PolyHaven}/Textures/{texture}/{texture}_nor_gl_1k.jpg";
        if (AssetImporter.GetAtPath(normal) is TextureImporter imp && imp.textureType != TextureImporterType.NormalMap)
        {
            imp.textureType = TextureImporterType.NormalMap;   // Poly Haven nor_gl = OpenGL 방식(Y+) — Unity 와 같다
            imp.SaveAndReimport();
        }
        string path = $"{Dir}/TL_{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse);
        // 노멀맵은 쓰지 않는다 — 층 셋 × (색 + 노멀) 에 지형 노멀까지 픽셀마다 8번 읽어 해변 장면의 GPU 를 잡아먹었다 (DomeAquariumQuestPerformance).
        layer.normalMapTexture = null;
        layer.normalScale = 1f;
        layer.tileSize = new Vector2(tile, tile);
        layer.metallic = 0f;
        // 매끄러움은 고정값으로. 기본값(디퓨즈 알파)은 알파 없는 JPG 를 1(거울)로 읽어 땅이 하늘을 비춰 눈밭처럼 하얬다.
        layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
        layer.smoothness = 0.05f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static Material TerrainMaterial()
    {
        string path = $"{Dir}/Mat_CoastTerrain.mat";
        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        DomeAquariumQuestPerformance.LightenTerrainMaterial(mat);   // 노멀맵 없이 — Quest 에서 해변을 가득 채우는 지형의 픽셀 비용
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    // 모델 (glTF) — 메시 LOD 를 만든 사본을 쓴다
    // ─────────────────────────────────────────────────────────────

    struct Piece
    {
        public Mesh mesh;
        public Material[] materials;
        public Matrix4x4 local;                         // 모델 루트 기준 노드 변환
    }

    static readonly Dictionary<string, List<Piece>> models = new Dictionary<string, List<Piece>>();

    static List<Piece> Model(string path, bool lods)
    {
        if (models.TryGetValue(path, out var cached) && cached.TrueForAll(p => p.mesh != null)) return cached;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var list = new List<Piece>();
        if (prefab == null) { Debug.LogWarning($"[Coast] 모델이 없다: {path}"); return list; }
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;
            list.Add(new Piece
            {
                mesh = lods ? LodMesh(mf.sharedMesh, $"{prefab.name}_{mf.sharedMesh.name}") : mf.sharedMesh,
                materials = mr.sharedMaterials,
                local = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix,
            });
        }
        models[path] = list;
        return list;
    }

    /// <summary>스캔 바위(1.2~2만 삼각형)에 메시 LOD 를 붙인 사본. 이미 만든 것이 있으면 그대로 쓴다 (지우면 다시 만든다).</summary>
    static Mesh LodMesh(Mesh source, string name)
    {
        string path = $"{Dir}/Meshes/{name}_LOD.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        var copy = Object.Instantiate(source);
        copy.name = name + "_LOD";
        MeshLodUtility.GenerateMeshLods(copy);
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    static Transform Place(string name, Transform parent, List<Piece> model, Vector3 local, float yaw, float scale, float tilt = 0f)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = local;
        root.localRotation = Quaternion.Euler(tilt, yaw, tilt * 0.6f);
        root.localScale = Vector3.one * scale;
        foreach (var piece in model)
        {
            var go = new GameObject(piece.mesh.name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = piece.local.GetColumn(3);
            go.transform.localRotation = piece.local.rotation;
            go.transform.localScale = piece.local.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = piece.materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return root;
    }

    // ─────────────────────────────────────────────────────────────
    // 바위 · 부표 · 배
    // ─────────────────────────────────────────────────────────────

    const string RockFace = PolyHaven + "/Models/rock_face_01/rock_face_01_1k.gltf";   // 5m 바위벽
    const string Rock07 = PolyHaven + "/Models/rock_07/rock_07_1k.gltf";              // 0.32m 돌 — 크게 키운다
    const string Rock09 = PolyHaven + "/Models/rock_09/rock_09_1k.gltf";              // 0.14m 납작 돌 — 넓은 너럭바위로

    /// <summary>곶 둘레와 테라스 양옆 물가의 바위. 돌아오는 값은 물보라가 이는 바위 발치(수면 높이).</summary>
    static List<Transform> BuildRocks(Transform coast)
    {
        var group = new GameObject("Rocks").transform;
        group.SetParent(coast, false);
        var spots = new List<Transform>();
        // (모델, 해안선 z, 해안선에서 바다 쪽으로 x, 배율, 돌린 각도, 모델 높이(m, 배율 1))
        var rocks = new (string model, float z, float off, float scale, float yaw, float height)[]
        {
            // 오른쪽 곶 (-Z) — 끝에 등대
            (RockFace, -85f, 2f, 3.0f, 30f, 3.56f),
            (Rock07, -105f, 2f, 40f, 110f, 0.14f),
            (Rock09, -125f, 3f, 90f, 200f, 0.03f),
            (RockFace, -140f, 3f, 3.5f, 80f, 3.56f),
            (Rock07, -160f, 2f, 35f, 250f, 0.14f),
            // 왼쪽 곶 (+Z)
            (Rock07, 115f, 2f, 45f, 20f, 0.14f),
            (RockFace, 145f, 3f, 3.2f, 160f, 3.56f),
            (Rock09, 170f, 3f, 100f, 70f, 0.03f),
            (RockFace, 200f, 3f, 4.0f, 300f, 3.56f),
            // 테라스 양옆 물가 — 창 끝에서 가까이 보인다 (돌고래가 뛰는 x 20~34 · z -18~33 밖)
            (Rock07, 40f, 3.5f, 18f, 140f, 0.14f),
            (RockFace, 58f, 4f, 1.6f, 210f, 3.56f),
            (Rock09, -28f, 3f, 45f, 15f, 0.03f),
            (Rock07, -40f, 3f, 25f, 330f, 0.14f),
        };
        for (int i = 0; i < rocks.Length; i++)
        {
            var r = rocks[i];
            float x = CoastX(r.z) + r.off;
            float sink = r.height * r.scale * 0.25f;     // 아래 1/4 은 물에 잠긴다
            var t = Place($"Rock_{i:00}", group, Model(r.model, lods: true), new Vector3(x, SeaY - sink, r.z), r.yaw, r.scale, tilt: (i % 3 - 1) * 4f);
            var spot = new GameObject("FoamSpot").transform;
            spot.SetParent(t, false);
            spot.position = new Vector3(t.position.x, coast.TransformPoint(new Vector3(0f, SeaY, 0f)).y, t.position.z);
            spots.Add(spot);
        }
        return spots;
    }

    static void BuildBuoys(Transform coast)
    {
        var group = new GameObject("Buoys").transform;
        group.SetParent(coast, false);
        var buoy = Model(PolyHaven + "/Models/ocean_buoy/ocean_buoy_1k.gltf", lods: true);
        var marker = Model(PolyHaven + "/Models/lateral_sea_marker/lateral_sea_marker_1k.gltf", lods: true);
        // 돌고래가 뛰는 곳(x 20~34)보다 먼 바다에. 관측 부표 둘 + 항로 표지 둘.
        const string buoyLabel = "관측 부표 · 수온과 파도 높이를 재서 관측소로 보내요";
        const string markerLabel = "항로 표지 · 배가 다니는 길을 알려 줘요";
        Subject(Bob(Place("ObservationBuoy_A", group, buoy, new Vector3(78f, SeaY, -42f), 20f, 1.3f), 0.18f, 5f, 4.2f), buoyLabel, 2f, new Vector3(0f, 1f, 0f));
        Subject(Bob(Place("ObservationBuoy_B", group, buoy, new Vector3(150f, SeaY, 62f), 200f, 1.3f), 0.18f, 5f, 4.8f), buoyLabel, 2f, new Vector3(0f, 1f, 0f));
        Subject(Bob(Place("SeaMarker_A", group, marker, new Vector3(62f, SeaY, 52f), 0f, 1f), 0.12f, 3f, 5.5f), markerLabel, 2.5f, new Vector3(0f, 2.5f, 0f));
        Subject(Bob(Place("SeaMarker_B", group, marker, new Vector3(96f, SeaY, -30f), 90f, 1f), 0.12f, 3f, 6.1f), markerLabel, 2.5f, new Vector3(0f, 2.5f, 0f));
    }

    /// <summary>
    /// 건물 가까이 있는 배는 정박해 들썩이기만 하고, 멀리 보이는 배만 지나간다.
    ///   정박: 연구선(테라스 오른쪽), 예인선(테라스 왼쪽 — 굴뚝에서 옅은 연기), 나룻배(왼쪽 모래톱 가).
    ///   먼바다: 돛단배(닻을 내림), 화물선 · 여객선이 곶 뒤에서 나와 만 건너편을 가로질러 반대편 곶 뒤로 들어간다.
    ///   지나가는 배는 굴뚝에서 연기를 뿜고, 1~2분에 한 번 들릴 듯 말 듯한 뱃고동을 낸다.
    /// Kenney 배는 뱃머리가 +Z, 바닥이 y=0 이다. 흘수는 모델 단위 (0.35 는 떠 보였다).
    /// </summary>
    static void BuildBoats(Transform coast)
    {
        var group = new GameObject("Boats").transform;
        group.SetParent(coast, false);

        // 정박 — 돌고래가 뛰는 곳(x 20~34)보다 테라스 쪽
        var fishing = Model(Kenney + "/boat-fishing-small.glb", lods: false);
        Subject(Bob(Place("ResearchBoat", group, fishing, new Vector3(15.8f, SeaY - 0.55f * 2.2f, -13f), 0f, 2.2f), 0.10f, 2.5f, 5.2f),
                "관측소 연구선 · 바다를 조사하러 나가는 배예요", 2.5f, new Vector3(0f, 1.2f, 0f));
        var tug = Bob(Place("Tug", group, Model(Kenney + "/boat-tug-a.glb", lods: false),
                            new Vector3(15.6f, SeaY - 0.5f * 3f, 27f), 180f, 3f), 0.10f, 2f, 5.8f);
        Subject(tug, "예인선 · 큰 배를 밀고 끌어 항구에 대 줘요", 2f, new Vector3(0f, 1.2f, 0f));
        // 예인선 자체가 3배로 커져 있다 — 굴뚝 자리는 모델 단위 그대로 (파티클 크기는 부모 배율을 따르지 않는다)
        Smoke("FunnelSmoke", tug, new Vector3(0f, 2.3f, -0.85f), size: 1.0f, rate: 3f, scaleParent: 1f);
        float rowZ = 47f;
        Subject(Bob(Place("Rowboat", group, Model(Kenney + "/boat-row-small.glb", lods: false),
                          new Vector3(CoastX(rowZ) + 2.5f, SeaY - 0.2f * 1.8f, rowZ), 65f, 1.8f), 0.06f, 4f, 3.6f),
                "나룻배 · 노를 저어 가까운 바다를 다녀요", 1.5f, new Vector3(0f, 0.4f, 0f));

        // 먼바다 돛단배 — 수평선 쪽 섬 사이에 닻을 내렸다
        var sail = Model(Kenney + "/boat-sail-a.glb", lods: false);
        Subject(Bob(Place("Sailboat", group, sail, new Vector3(330f, SeaY - 0.55f * 3f, -55f), 70f, 3f), 0.2f, 4f, 6f),
                "돛단배 · 섬 사이에 닻을 내렸어요", 2f, new Vector3(0f, 1.5f, 0f));

        // 지나가는 배 — 두 끝(z ±330)은 곶 뒤라 창에서 안 보인다. 섬(x 410~)보다 앞 항로 둘.
        var horn = DomeAquariumDepthSounds.EnsureShipHorn();
        Subject(Ship("CargoShip", group, Kenney + "/ship-cargo-a.glb", scale: 6f, draft: 0.5f,
                     from: new Vector3(380f, SeaY, -330f), to: new Vector3(380f, SeaY, 330f), speed: 5.5f, startAt: 0.42f,
                     funnels: new[] { new Vector3(-0.6f, 3.4f, 3.0f), new Vector3(0.35f, 3.4f, 2.8f) }, horn),
                "화물선 · 컨테이너를 가득 싣고 항구로 가요", 30f, new Vector3(0f, 6f, 0f));
        Subject(Ship("Liner", group, Kenney + "/ship-ocean-liner-small.glb", scale: 5f, draft: 0.9f,
                     from: new Vector3(300f, SeaY, 330f), to: new Vector3(300f, SeaY, -330f), speed: 6.5f, startAt: 0.62f,
                     funnels: new[] { new Vector3(0f, 8.5f, 1f), new Vector3(0f, 8.5f, 5f) }, horn),
                "여객선 · 섬과 육지를 오가며 사람을 실어 날라요", 35f, new Vector3(0f, 10f, 15f));
    }

    /// <summary>관망 쌍안경으로 볼 수 있게 이름표를 단다. offset 은 t 의 로컬 (배율이 있으면 모델 단위).</summary>
    static BinocularSubject Subject(Transform t, string label, float radius, Vector3 offset, string line = null)
    {
        var s = t.gameObject.AddComponent<BinocularSubject>();
        s.label = label;
        s.radius = radius;
        s.offset = offset;
        s.line = line;
        return s;
    }

    static Transform Ship(string name, Transform parent, string model, float scale, float draft, Vector3 from, Vector3 to,
                          float speed, float startAt, Vector3[] funnels, AudioClip horn)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        var route = root.gameObject.AddComponent<ShipRoute>();
        route.from = from;
        route.to = to;
        route.speed = speed;
        route.startAt = startAt;
        root.localPosition = Vector3.Lerp(from, to, startAt);
        root.localRotation = Quaternion.LookRotation(to - from, Vector3.up);

        var body = new GameObject("Body").transform;                    // 물결에 들썩이는 몸 — 항로는 부모가 정한다
        body.SetParent(root, false);
        Bob(body, 0.25f, 1.5f, 7f);
        Place("Model", body, Model(model, lods: false), new Vector3(0f, -draft * scale, 0f), 0f, scale);
        foreach (var f in funnels)
            Smoke("FunnelSmoke", body, new Vector3(f.x, f.y - draft, f.z), size: 0.55f * scale, rate: 3f, scaleParent: scale);

        var src = root.gameObject.AddComponent<AudioSource>();
        src.clip = horn;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;   // 최소 80m — 300~400m 에서 원래의 1/5 남짓 (들릴 듯 말 듯)
        src.minDistance = 80f;
        src.maxDistance = 1200f;
        src.volume = 0.55f;
        src.dopplerLevel = 0f;
        src.priority = 200;
        root.gameObject.AddComponent<ShipHorn>();
        return root;
    }

    /// <summary>
    /// 굴뚝 연기. 월드 시뮬레이션이라 배가 지나간 자리에 연기 줄이 남고, 바닷바람(+X, 창 반대쪽)에 천천히 밀린다.
    /// Simple Lit 이라 밤에는 어둡게 가라앉는다. position 은 모델 단위(배율 1) — scaleParent 를 곱해 놓는다.
    /// </summary>
    static ParticleSystem Smoke(string name, Transform parent, Vector3 position, float size, float rate, float scaleParent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(position.x * scaleParent, position.y * scaleParent, position.z * scaleParent);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 원뿔(+Z)이 위(+Y)를 향하게
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f * size, 1.5f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f * size, 1.4f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.15f * size;

        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.08f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.2f));
        var wind = ps.forceOverLifetime;
        wind.enabled = true;
        wind.space = ParticleSystemSimulationSpace.World;
        wind.x = new ParticleSystem.MinMaxCurve(0.12f * size);
        wind.y = new ParticleSystem.MinMaxCurve(0f);
        wind.z = new ParticleSystem.MinMaxCurve(0.04f * size);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SmokeMaterial();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static Material SmokeMaterial()
    {
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CoastSmoke", "Universal Render Pipeline/Particles/Simple Lit", additive: false);
        mat.SetTexture("_BaseMap", SmokeTexture());
        mat.SetColor("_BaseColor", new Color(0.42f, 0.42f, 0.41f, 1f));   // 저녁 하늘(밝은 회청색)에 묻히지 않게 조금 짙게
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 연기 한 뭉치 — 색은 흰색 그대로 두고 알파만 구름처럼 흐리게. 물속 반짝임용 점 텍스처(SoftDot)는 가산 혼합에 맞춰
    /// 가장자리 색까지 검게 줄여 두어, 알파 섞기 연기에 쓰면 검은 고리가 줄줄이 보였다.
    /// </summary>
    static Texture2D SmokeTexture()
    {
        string path = $"{Dir}/SmokePuff.png";
        if (!System.IO.File.Exists(path))
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float cloud = 0.55f + 0.45f * Mathf.PerlinNoise(x * 0.06f + 2.3f, y * 0.06f + 5.1f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a) * cloud;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Transform Bob(Transform t, float heave, float roll, float period)
    {
        var bob = t.gameObject.AddComponent<SeaBob>();
        bob.heave = heave;
        bob.roll = roll;
        bob.pitch = roll * 0.6f;
        bob.period = period;
        return t;
    }

    // ─────────────────────────────────────────────────────────────
    // 등대 — 오른쪽 곶 끝. 프리미티브로 쌓고, 밤에는 빛줄기가 돈다 (Lighthouse)
    // ─────────────────────────────────────────────────────────────

    static Lighthouse BuildLighthouse(Transform coast, LobbySequence seq, Terrain terrain)
    {
        float z = -140f;
        float x = CoastX(z) - 20f;
        Vector3 world = coast.TransformPoint(new Vector3(x, 0f, z));
        world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
        Vector3 local = coast.InverseTransformPoint(world);

        var root = new GameObject("Lighthouse").transform;
        root.SetParent(coast, false);
        root.localPosition = local - Vector3.up * 0.5f;   // 비탈에 묻히게 조금 내린다

        var white = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseWhite", new Color(0.92f, 0.91f, 0.88f), 0.35f, 0f, false);
        var red = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseRed", new Color(0.68f, 0.12f, 0.10f), 0.35f, 0f, false);
        var dark = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseIron", new Color(0.12f, 0.13f, 0.14f), 0.45f, 0.5f, false);
        var stone = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseStone", new Color(0.52f, 0.50f, 0.47f), 0.15f, 0f, false);
        var glass = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseLantern", new Color(1f, 0.95f, 0.82f), 0.9f, 0f, false);
        glass.EnableKeyword("_EMISSION");
        glass.SetColor("_EmissionColor", new Color(0.35f, 0.30f, 0.22f));
        glass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(glass);

        float y = 0f;
        Cylinder("Plinth", root, ref y, 3.2f, 1.5f, stone);
        Cylinder("Tower_1", root, ref y, 2.4f, 4f, white);
        Cylinder("Tower_2", root, ref y, 2.2f, 4f, red);
        Cylinder("Tower_3", root, ref y, 2.0f, 4f, white);
        Cylinder("Tower_4", root, ref y, 1.8f, 3f, red);
        Cylinder("Gallery", root, ref y, 2.6f, 0.3f, dark);
        float lanternY = y + 1.1f;
        var lantern = Cylinder("Lantern", root, ref y, 1.4f, 2.2f, glass);
        Cylinder("Roof", root, ref y, 1.7f, 0.4f, red);
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Dome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(root, false);
        dome.transform.localPosition = new Vector3(0f, y, 0f);
        dome.transform.localScale = new Vector3(2.4f, 1.8f, 2.4f);
        Quiet(dome.GetComponent<MeshRenderer>(), red);

        var pivot = new GameObject("BeamPivot").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0f, lanternY, 0f);
        var beams = new GameObject("Beams");
        beams.transform.SetParent(pivot, false);
        Mesh cone = BeamMesh();
        Material beamMat = BeamMaterial();
        foreach (float yaw in new[] { 0f, 180f })
        {
            var b = new GameObject("Beam");
            b.transform.SetParent(beams.transform, false);
            b.transform.localRotation = Quaternion.Euler(2f, yaw, 0f);   // 수평선 쪽으로 살짝 숙인다
            b.AddComponent<MeshFilter>().sharedMesh = cone;
            Quiet(b.AddComponent<MeshRenderer>(), beamMat);
        }
        beams.SetActive(false);

        var lh = root.gameObject.AddComponent<Lighthouse>();
        lh.lobby = seq;
        lh.beamPivot = pivot;
        lh.beams = beams;
        lh.lantern = lantern;
        Subject(root, "등대 · 밤이 되면 12초마다 한 바퀴 빛을 돌려 배에 길을 알려요", 6f, new Vector3(0f, 10f, 0f));
        return lh;
    }

    static Renderer Cylinder(string name, Transform parent, ref float y, float radius, float height, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // 지름 1 · 높이 2
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y + height * 0.5f, 0f);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        y += height;
        var r = go.GetComponent<MeshRenderer>();
        Quiet(r, mat);
        return r;
    }

    static void Quiet(Renderer r, Material mat)
    {
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    /// <summary>빛줄기 원뿔 (+Z 로 110m, 끝 반지름 6m). 꼭짓점 색 알파가 등명기에서 0.5 → 끝에서 0 — 가산 혼합이라 밤하늘에 번진다.</summary>
    static Mesh BeamMesh()
    {
        string path = $"{Dir}/Meshes/LighthouseBeam.asset";
        AssetDatabase.DeleteAsset(path);
        const int seg = 16;
        const float length = 110f, radius = 6f, start = 0.6f;
        var v = new List<Vector3>();
        var c = new List<Color>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v.Add(dir * start); c.Add(new Color(1f, 1f, 1f, 0.5f));
            v.Add(dir * radius + Vector3.forward * length); c.Add(new Color(1f, 1f, 1f, 0f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = a + 1, d = a + 2, e = a + 3;
            tris.AddRange(new[] { a, b, d, d, b, e });
        }
        var m = new Mesh { name = "LighthouseBeam" };
        m.SetVertices(v);
        m.SetColors(c);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material BeamMaterial()
    {
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CoastLighthouseBeam", "Universal Render Pipeline/Particles/Unlit");
        mat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.75f, 1f));
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);   // 원뿔 안팎 모두
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    // 갈매기 · 물보라
    // ─────────────────────────────────────────────────────────────

    static void BuildGulls(Transform coast, LobbySequence seq)
    {
        var go = new GameObject("Seagulls");
        go.transform.SetParent(coast, false);
        go.transform.localPosition = new Vector3(50f, SeaY, 6f);   // 만 한가운데 수면
        var flock = go.AddComponent<SeagullFlock>();
        flock.lobby = seq;
        flock.material = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastGull", new Color(0.93f, 0.94f, 0.95f), 0.2f, 0f, false);
    }

    static ParticleSystem BuildFoam(Transform coast, List<Transform> spots)
    {
        var ps = DomeAquariumLobbyBuilder.SplashParticles(coast);
        ps.gameObject.name = "ShoreFoamSpray";
        var foam = ps.gameObject.AddComponent<ShoreFoam>();
        foam.spray = ps;
        foam.spots = spots.ToArray();
        return ps;
    }

    // ─────────────────────────────────────────────────────────────
    // 밀려오는 파도 — 테라스 앞(부두 벽)과 양옆 모래톱
    // ─────────────────────────────────────────────────────────────

    static void BuildWaves(Transform coast, Transform set, ParticleSystem spray)
    {
        // 부두 앞벽(로비 빌더 BuildPier) — 테라스 앞에서는 파도가 벽에 부딪힌다
        float pierX = 12.3f;
        Vector2 pierZ = new Vector2(-0.4f, 15.4f);
        var front = set.Find("Pier/PierFront");
        if (front != null)
        {
            pierX = front.localPosition.x + front.localScale.x * 0.5f;
            pierZ = new Vector2(front.localPosition.z - front.localScale.z * 0.5f, front.localPosition.z + front.localScale.z * 0.5f);
        }

        const float zStart = -40f, zEnd = 58f, step = 2f;
        int n = Mathf.RoundToInt((zEnd - zStart) / step) + 1;
        var shore = new float[n];
        for (int i = 0; i < n; i++)
        {
            float z = zStart + i * step;
            shore[i] = z >= pierZ.x && z <= pierZ.y ? pierX : CoastX(z);
        }

        var go = new GameObject("ShoreWaves");
        go.transform.SetParent(coast, false);
        go.AddComponent<MeshFilter>();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = FoamMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var waves = go.AddComponent<ShoreWaves>();
        waves.shoreX = shore;
        waves.zStart = zStart;
        waves.zStep = step;
        waves.seaY = SeaY + 0.04f;                     // 바다 판 바로 위 — 겹쳐 깜빡이지 않게
        waves.pierZ = pierZ;
        waves.spray = spray;
        // 창(눈높이 수면 위 2.6m)에서는 수면을 비스듬히 보아 띠가 납작해진다 — 1.6m 띠는 가는 선 하나로만 보였다.
        waves.fronts = 4;
        waves.period = 8f;
        waves.travel = 34f;
        waves.width = 3.5f;
        waves.opacity = 0.9f;
        DomeAquariumNightBeach.AddGlow(waves);         // 엔딩의 밤 — 푸르게 빛나는 파도
    }

    // ─────────────────────────────────────────────────────────────
    // 혹등고래 — 물 위로 솟는 등(모델)과 잠수할 때 드는 꼬리 (WhaleSurfacing)
    // ─────────────────────────────────────────────────────────────

    static void BuildWhale(Transform coast, ParticleSystem splash)
    {
        var root = new GameObject("Whale").transform;
        root.SetParent(coast, false);
        root.localPosition = new Vector3(170f, SeaY, 10f);
        var skin = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastWhale", new Color(0.10f, 0.12f, 0.15f), 0.6f, 0f, false);   // 젖어 번들거리는 검푸른 등
        var belly = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastWhaleFlukeUnder", new Color(0.78f, 0.80f, 0.80f), 0.5f, 0f, false);

        // 등: 몸길이 14m 혹등고래 모델(Sketchfab, DomeAquarium/21)의 등 꼭대기가 0.7m 남짓 물 위로 — 나머지는 물속이라 안 보인다.
        // 모델이 없으면 예전처럼 길이 11m 타원체 + 등지느러미 혹.
        var back = new GameObject("Back").transform;
        back.SetParent(root, false);
        var humpback = DomeAquariumDeepSeaSetup.HumpbackPrefab();
        if (humpback != null)
        {
            const float length = 14f;
            var body = (GameObject)PrefabUtility.InstantiatePrefab(humpback, back);
            body.name = "Body";
            body.transform.localScale = Vector3.one * length;
            float top = DomeAquariumDeepSeaSetup.BakedBounds(body).max.y * length;
            body.transform.localPosition = new Vector3(0f, 0.7f - top, 0f);
        }
        else
        {
            Ellipsoid("Body", back, Vector3.zero, new Vector3(2.6f, 1.4f, 11f), skin);
            Ellipsoid("Dorsal", back, new Vector3(0f, 0.62f, -2.8f), new Vector3(0.35f, 0.55f, 1.3f), skin);
        }

        // 꼬리: 폭 5.2m. 잠수할 때 몸 뒤쪽(−Z)에서 수직으로 들어 올린다.
        var fluke = new GameObject("Fluke").transform;
        fluke.SetParent(root, false);
        fluke.localPosition = new Vector3(0f, -5f, -5.5f);
        var flukeMesh = new GameObject("FlukeMesh");
        flukeMesh.transform.SetParent(fluke, false);
        flukeMesh.AddComponent<MeshFilter>().sharedMesh = FlukeMesh();
        var fr = flukeMesh.AddComponent<MeshRenderer>();
        fr.sharedMaterials = new[] { skin, belly };   // 앞(머리 쪽)은 검고, 뒤에서 보이는 아랫면은 희다
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fr.receiveShadows = false;

        // 숨 — 물기둥 (한 번에 터진다)
        var spoutGo = new GameObject("Spout");
        spoutGo.transform.SetParent(root, false);
        spoutGo.transform.localPosition = new Vector3(0f, 0.3f, 3.6f);
        spoutGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var spout = spoutGo.AddComponent<ParticleSystem>();
        spout.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = spout.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.55f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var emission = spout.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30), new ParticleSystem.Burst(0.15f, 15) });
        var shape = spout.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 0.2f;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        var color = spout.colorOverLifetime;
        color.enabled = true;
        color.color = fade;
        var grow = spout.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 3f));
        var sr = spoutGo.GetComponent<ParticleSystemRenderer>();
        var mist = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CoastSpout", "Universal Render Pipeline/Particles/Simple Lit", additive: false);
        mist.SetTexture("_BaseMap", SmokeTexture());
        mist.SetColor("_BaseColor", new Color(0.95f, 0.96f, 0.97f, 1f));
        EditorUtility.SetDirty(mist);
        sr.sharedMaterial = mist;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;

        var blow = root.gameObject.AddComponent<AudioSource>();
        blow.clip = DomeAquariumDepthSounds.EnsureWhaleBlow();
        blow.playOnAwake = false;
        blow.spatialBlend = 1f;
        blow.rolloffMode = AudioRolloffMode.Logarithmic;   // 최소 40m — 150~250m 에서 원래의 1/5 안팎
        blow.minDistance = 40f;
        blow.maxDistance = 900f;
        blow.volume = 1f;
        blow.dopplerLevel = 0f;

        var subject = Subject(root, "혹등고래 · 숨을 쉬러 물 위로 올라왔어요", 7f, new Vector3(0f, 0.5f, 0f),
            "혹등고래예요! 고래는 물고기가 아니라 우리처럼 공기로 숨을 쉬어요. 물 위로 올라와 숨을 내쉬면, 그 김이 저렇게 물기둥처럼 솟아요.");
        subject.enabled = false;                       // 올라와 있는 동안만 켠다

        var whale = root.gameObject.AddComponent<WhaleSurfacing>();
        whale.back = back;
        whale.fluke = fluke;
        whale.spout = spout;
        whale.blow = blow;
        whale.splash = splash;
        whale.subject = subject;
        whale.seaY = SeaY;
    }

    static void Ellipsoid(string name, Transform parent, Vector3 local, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        Quiet(go.GetComponent<MeshRenderer>(), mat);
    }

    /// <summary>
    /// 혹등고래 꼬리 — 꼬리자루(아래)에서 좌우로 벌어진 두 날개, 가운데가 오목하다. XY 평면, 아래 끝이 원점.
    /// 앞면(+Z 를 보는 쪽, 머리 쪽)은 0번 재질, 뒷면은 1번 재질 — 두 면을 따로 만든다.
    /// </summary>
    static Mesh FlukeMesh()
    {
        string path = $"{Dir}/Meshes/WhaleFluke.asset";
        AssetDatabase.DeleteAsset(path);
        var right = new[]
        {
            new Vector2(0.25f, 0f), new Vector2(0.2f, 1.0f), new Vector2(1.2f, 1.25f), new Vector2(2.2f, 1.8f),
            new Vector2(2.6f, 2.45f), new Vector2(2.0f, 2.45f), new Vector2(1.2f, 2.3f), new Vector2(0.45f, 2.15f),
        };
        var outline = new System.Collections.Generic.List<Vector2>(right);
        outline.Add(new Vector2(0f, 1.95f));                                        // 가운데 오목한 곳
        for (int i = right.Length - 1; i >= 0; i--) outline.Add(new Vector2(-right[i].x, right[i].y));
        var center = new Vector2(0f, 1.5f);

        var v = new System.Collections.Generic.List<Vector3> { center };
        foreach (var p in outline) v.Add(p);
        int n = outline.Count;
        var front = new System.Collections.Generic.List<int>();
        var rear = new System.Collections.Generic.List<int>();
        for (int i = 0; i < n; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % n;
            front.AddRange(new[] { 0, a, b });          // 외곽선이 반시계라 (가운데, a, b) 가 +Z 쪽에서 보면 시계 방향
            rear.AddRange(new[] { 0, b, a });
        }
        var mesh = new Mesh { name = "WhaleFluke" };
        // 앞뒤 면에 각자 노멀이 필요해 꼭짓점을 두 벌로
        var verts = new System.Collections.Generic.List<Vector3>(v);
        verts.AddRange(v);
        var normals = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < v.Count; i++) normals.Add(Vector3.forward);
        for (int i = 0; i < v.Count; i++) normals.Add(Vector3.back);
        for (int i = 0; i < rear.Count; i++) rear[i] += v.Count;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, verts.ConvertAll(p => DomeAquariumWhaleListening.FlukeUv(p)));   // 아랫면 무늬(꼬리 사진 식별) — 30번 메뉴
        mesh.subMeshCount = 2;
        mesh.SetTriangles(front, 0);
        mesh.SetTriangles(rear, 1);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Material FoamMaterial()
    {
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CoastWaveFoam", "Universal Render Pipeline/Particles/Simple Lit", additive: false);
        mat.SetTexture("_BaseMap", FoamTexture());
        mat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f, 1f));
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 거품 띠 텍스처 — 거품 덩어리와 그 사이 구멍이 얼룩덜룩한 무늬. 가로(띠 길이 9m)와 세로(띠 두께 3.5~7m)의 픽셀 크기가 비슷해
    /// 같은 빈도의 잡음을 쓴다. 가로로만 이어 붙인다(양 끝을 섞는다). 예전 무늬는 가로로만 변해 위에서 보면 빛줄기처럼 보였다.
    /// </summary>
    static Texture2D FoamTexture()
    {
        string path = $"{Dir}/WaveFoam.png";
        if (!System.IO.File.Exists(path))
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            float Tiled(float x, float y, float f, float seed)
            {
                float a = Mathf.PerlinNoise(seed + x * f, seed + y * f);
                float b = Mathf.PerlinNoise(seed + (x - w) * f, seed + y * f);
                return Mathf.Lerp(a, b, x / w);                  // 오른쪽 끝이 왼쪽 처음과 같아진다
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h;
                    float blobs = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.66f, Tiled(x, y, 0.045f, 3.7f)));
                    float lace = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.7f, Tiled(x, y, 0.13f, 11.3f)));
                    float across = Mathf.Exp(-Mathf.Pow((v - 0.45f) / 0.3f, 2f));   // 띠 가운데가 짙고 양 가장자리로 흐려진다
                    float alpha = Mathf.Clamp01(across * (0.25f + 0.75f * Mathf.Max(blobs, lace * 0.7f)));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.alphaIsTransparency = true;
                imp.wrapModeU = TextureWrapMode.Repeat;
                imp.wrapModeV = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
#endif
