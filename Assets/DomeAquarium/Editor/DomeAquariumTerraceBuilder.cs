#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 로비 테라스 — 창 벽에 문을 내고, 데크를 걸어 다니게 하고, 난간 가에 관망 쌍안경을 세운다.
///
///   문: 창 벽(x=6) z 0.8~2.3. 시작 자리(z 2.6) 오른쪽 앞, 화분(z 0.6)과 "창밖 바다" 안내판(z 2.65~) 사이. 미닫이 유리는 열어 둔다.
///   데크: x 6~11.9(유리 난간) · z 0~15. 막대로 걸어 다닌다 (LobbyTutorial.openAreas — 개찰구와 상관없이 언제나).
///   관망 쌍안경: 데크 가운데 난간 앞. 얼굴을 대면 6배로 당겨 보고, 배 · 등대 · 부표 · 고래의 이름이 뜬다 (BinocularViewer).
///   파도 소리: 부두 벽 앞 3D 소리 — 테라스에 나가야 크게 들린다.
/// 전부 LobbySet/Terrace 아래(창 유리만 WindowWall 안에서 나눈다). 여러 번 실행해도 같은 결과.
/// </summary>
public static class DomeAquariumTerraceBuilder
{
    const string Dir = "Assets/DomeAquarium/Coast";
    const float WallX = 6f, HallLength = 15f, CeilingHeight = 4.5f;
    const float DoorZ0 = 0.8f, DoorZ1 = 2.3f, DoorHeight = 2.5f;
    const float RailX = 11.9f;                          // 테라스 유리 난간
    const float Margin = 0.35f;                         // 벽 · 난간에 얼굴을 묻지 않을 만큼 (LobbyBuilder.ConfigureWalkArea 와 같다)
    static readonly Vector3 ViewerLocal = new Vector3(11.2f, 0f, 6.0f);
    const int UiLayer = 5;                              // 확대 화면 — 보조 카메라가 그리지 않는 레이어

    [MenuItem("DomeAquarium/20. 테라스 · 관망 쌍안경", priority = 20)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Terrace] 플레이 모드에서는 세울 수 없다. Play 를 멈추고 다시 실행할 것.");
            return;
        }
        var root = GameObject.Find("LobbyRoot");
        Transform set = root != null ? root.transform.Find("LobbySet") : null;
        var seq = root != null ? root.GetComponent<LobbySequence>() : null;
        var tutorial = root != null ? root.GetComponentInChildren<LobbyTutorial>(true) : null;
        if (set == null || seq == null) { Debug.LogError("[Terrace] LobbyRoot/LobbySet 이 없다. 먼저 9번 메뉴로 로비를 빌드할 것."); return; }
        Build(set, seq, tutorial);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(Transform set, LobbySequence seq, LobbyTutorial tutorial)
    {
        var old = set.Find("Terrace");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("Terrace").transform;
        group.SetParent(set, false);

        CutDoor(set, group);
        if (tutorial != null) Walkable(tutorial);
        BuildViewer(group, seq);
        BuildSurf(group);

        // 확대 카메라에 찍히면 안 되는 글자판 — 창밖 점프 이름표(바다 위 수십 m)와 관측소장 자막. UI 레이어는 본 카메라만 그린다.
        var jumpLabel = set.Find("SeaLife/JumpLabel");
        if (jumpLabel != null) SetLayer(jumpLabel, UiLayer);
        var caption = seq.captionGroup != null ? seq.captionGroup.transform : null;
        if (caption != null) SetLayer(caption, UiLayer);
        AssetDatabase.SaveAssets();
        Debug.Log("[Terrace] 테라스 문 · 데크 걷기 · 관망 쌍안경 · 부두 파도 소리 적용 완료.");
    }

    // ─────────────────────────────────────────────────────────────
    // 문 — 통유리 한 장을 문 양옆 두 장 + 문 위 한 장으로 나눈다
    // ─────────────────────────────────────────────────────────────

    static void CutDoor(Transform set, Transform group)
    {
        var win = set.Find("WindowWall");
        if (win == null) { Debug.LogWarning("[Terrace] WindowWall 이 없어 문을 내지 못했다."); return; }
        Material glass = null, frame = null;
        for (int i = win.childCount - 1; i >= 0; i--)
        {
            var c = win.GetChild(i);
            if (c.name == "Glass" || c.name.StartsWith("Glass_"))
            {
                if (glass == null) glass = c.GetComponent<Renderer>()?.sharedMaterial;
                Object.DestroyImmediate(c.gameObject);
            }
            else if (c.name == "Sill" && frame == null) frame = c.GetComponent<Renderer>()?.sharedMaterial;
        }
        const float t = 0.03f;
        Glass("Glass_A", win, 0f, DoorZ0, 0f, CeilingHeight, glass);
        Glass("Glass_B", win, DoorZ1, HallLength, 0f, CeilingHeight, glass);
        Glass("Glass_Transom", win, DoorZ0, DoorZ1, DoorHeight, CeilingHeight, glass);

        // 문틀과 열어 둔 미닫이 유리 (문 오른쪽 유리 바깥에 겹쳐 놓았다)
        foreach (float z in new[] { DoorZ0, DoorZ1 })
            DomeAquariumLobbyBuilder.Box($"DoorPost_{z:0.0}", group, new Vector3(WallX, DoorHeight * 0.5f, z), new Vector3(0.12f, DoorHeight, 0.07f), frame);
        DomeAquariumLobbyBuilder.Box("DoorHead", group, new Vector3(WallX, DoorHeight, (DoorZ0 + DoorZ1) * 0.5f), new Vector3(0.12f, 0.08f, DoorZ1 - DoorZ0 + 0.07f), frame);
        float w = DoorZ1 - DoorZ0;
        DomeAquariumLobbyBuilder.Box("SlidingDoor", group, new Vector3(WallX + 0.08f, DoorHeight * 0.5f, DoorZ1 + w * 0.5f + 0.05f), new Vector3(t, DoorHeight, w), glass);
        DomeAquariumLobbyBuilder.Box("SlidingDoorFrame", group, new Vector3(WallX + 0.08f, DoorHeight * 0.5f, DoorZ1 + 0.06f), new Vector3(0.05f, DoorHeight, 0.05f), frame);

        // 문 위 안내 (방 안에서 읽힌다 — 월드 캔버스는 +Z 를 등지고 보여 90° 돌린다)
        DomeAquariumLobbyBuilder.Label("TerraceSign", group, new Vector3(WallX - 0.08f, DoorHeight + 0.22f, (DoorZ0 + DoorZ1) * 0.5f), 90f,
            new Vector2(DoorZ1 - DoorZ0 + 0.2f, 0.3f), "테라스  ·  관망 쌍안경", 0.085f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Center);
    }

    static void Glass(string name, Transform parent, float z0, float z1, float y0, float y1, Material mat)
    {
        DomeAquariumLobbyBuilder.Box(name, parent, new Vector3(WallX, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.03f, y1 - y0, z1 - z0), mat);
    }

    // ─────────────────────────────────────────────────────────────
    // 걸을 수 있는 곳 (막대로 걷기만 — 순간이동은 없다)
    // ─────────────────────────────────────────────────────────────

    static void Walkable(LobbyTutorial tutorial)
    {
        Undo.RecordObject(tutorial, "Terrace walk area");
        tutorial.openAreas = new[]
        {
            Rect.MinMaxRect(WallX - 0.7f, DoorZ0 + 0.25f, WallX + 0.7f, DoorZ1 - 0.25f),      // 문 — 홀과 데크를 잇는다
            Rect.MinMaxRect(WallX + Margin, Margin, RailX - Margin, HallLength - Margin),        // 데크
        };
        EditorUtility.SetDirty(tutorial);
    }

    // ─────────────────────────────────────────────────────────────
    // 관망 쌍안경
    // ─────────────────────────────────────────────────────────────

    static void BuildViewer(Transform group, LobbySequence seq)
    {
        var root = new GameObject("BinocularViewer").transform;
        root.SetParent(group, false);
        root.localPosition = ViewerLocal;
        root.localRotation = Quaternion.Euler(0f, 90f, 0f);    // 바다(+X)를 본다

        var paint = DomeAquariumLobbyBuilder.LitMaterial("Mat_TerraceBinocular", new Color(0.10f, 0.33f, 0.42f), 0.5f, 0.4f, false);
        var steel = DomeAquariumLobbyBuilder.LitMaterial("Mat_TerraceSteel", new Color(0.35f, 0.37f, 0.39f), 0.45f, 0.6f, false);
        var lens = DomeAquariumLobbyBuilder.LitMaterial("Mat_TerraceLens", new Color(0.05f, 0.08f, 0.10f), 0.95f, 0f, false);

        Cylinder("Base", root, new Vector3(0f, 0.025f, 0f), 0.4f, 0.05f, steel);
        Cylinder("Post", root, new Vector3(0f, 0.6f, 0f), 0.11f, 1.1f, steel);
        var head = new GameObject("Head").transform;              // 고개를 따라 도는 몸
        head.SetParent(root, false);
        head.localPosition = new Vector3(0f, 1.2f, 0f);
        DomeAquariumLobbyBuilder.Box("Body", head, new Vector3(0f, 0.08f, 0f), new Vector3(0.36f, 0.16f, 0.24f), paint);
        foreach (float x in new[] { -0.09f, 0.09f })
        {
            BarrelZ($"Objective_{x:+0;-0}", head, new Vector3(x, 0.08f, 0.15f), 0.11f, 0.08f, paint);
            BarrelZ($"Lens_{x:+0;-0}", head, new Vector3(x, 0.08f, 0.195f), 0.09f, 0.01f, lens);
            BarrelZ($"Ocular_{x:+0;-0}", head, new Vector3(x, 0.08f, -0.15f), 0.055f, 0.07f, steel);
        }
        var eyepiece = new GameObject("Eyepiece").transform;
        eyepiece.SetParent(head, false);
        eyepiece.localPosition = new Vector3(0f, 0.08f, -0.2f);

        DomeAquariumLobbyBuilder.Label("Hint", root, new Vector3(0f, 0.95f, -0.07f), 0f, new Vector2(0.5f, 0.14f),
            "얼굴을 가까이 대고\n바다를 둘러보세요", 0.035f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Center);

        // 확대 화면: 렌더 텍스처 · 보조 카메라 · 두 원 모양 판
        const float r = 0.16f, cx = 0.13f;                         // 0.6m 앞에서 가로 52° · 세로 30°
        float width = 2f * (cx + r), height = 2f * r;
        var rt = ViewTexture(768, Mathf.RoundToInt(768 * height / width));

        var camGo = new GameObject("ZoomCamera");
        camGo.transform.SetParent(root, false);
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 5f;
        cam.nearClipPlane = 1.5f;                                  // 손 · 쌍안경 몸 · 난간은 그리지 않는다
        cam.farClipPlane = 1300f;
        cam.cullingMask = ~(1 << UiLayer);
        cam.targetTexture = rt;
        cam.depth = -10f;
        cam.allowMSAA = false;
        var urp = cam.GetUniversalAdditionalCameraData();
        urp.allowXRRendering = false;                              // 헤드셋 두 눈이 아니라 렌더 텍스처 한 장에
        urp.renderPostProcessing = false;
        urp.renderShadows = false;
        urp.antialiasing = AntialiasingMode.None;

        var screen = new GameObject("ZoomScreen");
        screen.layer = UiLayer;
        screen.transform.SetParent(root, false);
        screen.AddComponent<MeshFilter>().sharedMesh = ScreenMesh(r, cx);
        var sr = screen.AddComponent<MeshRenderer>();
        sr.sharedMaterial = ScreenMaterial(rt);
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        // 이름표는 확대 화면 위쪽 — 아래쪽은 관측소장 자막(눈높이 13° 아래)이 쓴다
        var label = DomeAquariumLobbyBuilder.Label("Caption", screen.transform, new Vector3(0f, r + 0.03f, 0f), 0f, new Vector2(width, 0.06f),
            string.Empty, 0.026f, Color.white, TextAlignmentOptions.Center);
        SetLayer(screen.transform, UiLayer);                       // 이름표 캔버스까지 — 보조 카메라에 찍히지 않게

        var viewer = root.gameObject.AddComponent<BinocularViewer>();
        viewer.lobby = seq;
        viewer.eyepiece = eyepiece;
        viewer.head = head;
        viewer.zoomCamera = cam;
        viewer.screen = screen.transform;
        viewer.label = label;
        // 두 원 밖을 가리는 검은 공 — 화면 가장자리(0.67m) · 이름표(0.70m)보다 조금 바깥. 보조 카메라가 그리지 않게 UI 레이어
        viewer.blackout = BuildBlackout(root, 0.72f);
    }

    /// <summary>
    /// 안쪽 면만 그리는 검은 공. 다른 불투명 물체보다 먼저(대기열 1950) 깊이를 써서, 공 밖의 땅 · 하늘 · 바다는 깊이 검사에서 떨어져
    /// 그려지지도 않는다. 공 안의 확대 화면 · 이름표 · 손은 보이고, 맨 위에 그리는 자막은 그대로 보인다.
    /// </summary>
    static Renderer BuildBlackout(Transform root, float radius)
    {
        const string path = "Assets/DomeAquarium/Materials/Mat_BinocularBlackout.mat";
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetColor("_BaseColor", Color.black);
        mat.SetFloat("_Surface", 0f);                                              // 불투명 — 깊이를 쓴다
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetFloat("_ZWrite", 1f);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);   // 공 안에서 본다
        if (mat.HasProperty("_QueueOffset")) mat.SetFloat("_QueueOffset", -50f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry - 50;
        EditorUtility.SetDirty(mat);

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Blackout";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.layer = UiLayer;
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * radius * 2f;                      // 기본 공은 반지름 0.5
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return r;
    }

    static RenderTexture ViewTexture(int w, int h)
    {
        string path = $"{Dir}/BinocularView.renderTexture";
        // 알파 없는 형식 — 확대 화면 재질이 반투명 방식이라 하늘 자리 알파(0)를 그대로 쓰면 그 부분이 비쳐 보인다
        const RenderTextureFormat format = RenderTextureFormat.RGB111110Float;
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (rt != null && (rt.width != w || rt.height != h || rt.format != format)) { AssetDatabase.DeleteAsset(path); rt = null; }
        if (rt == null)
        {
            rt = new RenderTexture(w, h, 24, format) { name = "BinocularView", antiAliasing = 1 };
            AssetDatabase.CreateAsset(rt, path);
        }
        return rt;
    }

    /// <summary>두 원이 겹친 쌍안경 모양 판. 카메라 쪽(−Z)에서 보이고, UV 는 판 전체 사각형에 렌더 텍스처를 그대로 편다.</summary>
    static Mesh ScreenMesh(float r, float cx)
    {
        string path = $"{Dir}/Meshes/BinocularScreen.asset";
        AssetDatabase.DeleteAsset(path);
        float w = 2f * (cx + r), h = 2f * r;
        const int seg = 48;
        var v = new System.Collections.Generic.List<Vector3>();
        var uv = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        foreach (float c in new[] { -cx, cx })
        {
            int center = v.Count;
            v.Add(new Vector3(c, 0f, 0f));
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(c + Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
            for (int i = 0; i < seg; i++) tris.AddRange(new[] { center, center + 2 + i, center + 1 + i });   // −Z 에서 보면 시계 방향
        }
        foreach (var p in v) uv.Add(new Vector2((p.x + w * 0.5f) / w, (p.y + h * 0.5f) / h));
        var mesh = new Mesh { name = "BinocularScreen" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>
    /// 확대 화면 재질 — 반투명 방식(알파 1)이라 깊이를 쓰지 않고, 자막(3000)보다 조금 먼저(2990) 그린다. 그래서 관측소장 자막이
    /// 화면 위에 그대로 보인다. 불투명 Unlit 에 깊이 쓰기만 끄면 URP 가 저장할 때 되돌려, 화면이 자막을 가렸다.
    /// </summary>
    static Material ScreenMaterial(RenderTexture rt)
    {
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_BinocularScreen", "Universal Render Pipeline/Unlit", additive: false);
        mat.SetTexture("_BaseMap", rt);
        mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_QueueOffset")) mat.SetFloat("_QueueOffset", -10f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }

    static void Cylinder(string name, Transform parent, Vector3 local, float diameter, float height, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>앞뒤(Z)로 누운 원통 — 대물렌즈 · 접안경.</summary>
    static void BarrelZ(string name, Transform parent, Vector3 local, float diameter, float length, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ─────────────────────────────────────────────────────────────
    // 헤드셋 없이 확인하는 도구
    // ─────────────────────────────────────────────────────────────

    static bool debugLooking;

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 관망 쌍안경 들여다보기 켜기·끄기", priority = 214)]
    public static void DebugLook()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Terrace] 플레이 모드에서만 쓸 수 있다."); return; }
        var viewer = Object.FindFirstObjectByType<BinocularViewer>();
        if (viewer == null) { Debug.LogWarning("[Terrace] 관망 쌍안경이 없다. 20번 메뉴를 먼저."); return; }
        debugLooking = !debugLooking;
        viewer.DebugForce(debugLooking);
        Debug.Log($"[Terrace] 쌍안경 들여다보기: {(debugLooking ? "켬 — 게임뷰에서 고개(카메라) 방향으로 본다" : "끔")}");
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 고래 바로 부르기", priority = 214)]
    public static void DebugWhale()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Terrace] 플레이 모드에서만 쓸 수 있다."); return; }
        var whale = Object.FindFirstObjectByType<WhaleSurfacing>();
        if (whale == null) { Debug.LogWarning("[Terrace] 고래가 없다. 19번 메뉴를 먼저."); return; }
        whale.SurfaceNow();
        Debug.Log($"[Terrace] 고래가 올라온다 — 로비 로컬 {whale.transform.localPosition}");
    }

    static int debugDuskStep;

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 해 질 녘 앞당기기 (0 → ½ → 다 저묾)", priority = 214)]
    public static void DebugDusk()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Terrace] 플레이 모드에서만 쓸 수 있다."); return; }
        var dusk = Object.FindFirstObjectByType<LobbyDusk>();
        if (dusk == null) { Debug.LogWarning("[Terrace] 시간 흐름(LobbyDusk)이 없다. 19번 메뉴를 먼저."); return; }
        debugDuskStep = (debugDuskStep + 1) % 3;
        float p = debugDuskStep * 0.5f;
        dusk.DebugSet(p);
        Debug.Log($"[Terrace] 해 질 녘 {p:0.0} — 로비에 {p * dusk.minutes:0.#}분 머문 빛");
    }

    // ─────────────────────────────────────────────────────────────
    // 부두 벽 파도 소리
    // ─────────────────────────────────────────────────────────────

    static void BuildSurf(Transform group)
    {
        var go = new GameObject("PierSurf");
        go.transform.SetParent(group, false);
        go.transform.localPosition = new Vector3(12.4f, -0.6f, HallLength * 0.5f);
        var src = go.AddComponent<AudioSource>();
        src.clip = DomeAquariumDepthSounds.EnsureSurfLoop();
        src.loop = true;
        src.playOnAwake = true;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;   // 최소 3m — 난간 앞에서 크고, 홀 안쪽(10m 넘게)에서는 3분의 1 아래로
        src.minDistance = 3f;
        src.maxDistance = 60f;
        src.spread = 90f;                                  // 벽을 따라 넓게
        src.volume = 0.55f;
        src.dopplerLevel = 0f;
    }
}
#endif
