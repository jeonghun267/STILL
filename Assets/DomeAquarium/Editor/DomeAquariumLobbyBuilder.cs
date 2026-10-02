#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1막 "육지" — 해 질 녘 해양 관측소 로비를 세우고 LobbySequence 를 배선한다.
///
/// 배치 (로비 로컬 좌표, 바닥 y=0, 정면 +Z):
///   z=0   뒷벽 ─ 시작 자리(z=1.8)
///         왼쪽: 바시스피어 전시(1930년 잠수구)      오른쪽: 바다가 보이는 통유리창 + 소파
///   z=8.4 개찰구와 낮은 유리 칸막이 — 여기부터 "관측소 안"
///   z=12  앞벽: STILL 간판 · 71% / 80% 데이터 월 · 가운데 아치 너머 엘리베이터
///   z=14.5 엘리베이터 칸 중심 — 안쪽 벽에 수심 계기판
///
/// 로비는 수조(원점, 반지름 12m)에서 150m 떨어져 있다. 카메라 원경이 60m 라 서로 보이지 않고,
/// 수중 돔에 도착하면 LobbySet 을 통째로 꺼 비용이 0 이 된다.
/// 여러 번 실행해도 안전하다(기존 LobbyRoot 를 지우고 다시 세움).
/// </summary>
public static class DomeAquariumLobbyBuilder
{
    const string RootName = "LobbyRoot";
    static readonly Vector3 LobbyOrigin = new Vector3(0f, 0f, 150f);

    const string Kit = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs";
    const string Kenney = "Assets/ThirdParty/Kenney_FurnitureKit/Models";
    const string Sketchfab = "Assets/ThirdParty/Sketchfab";
    const string SkyHdr = "Assets/ThirdParty/PolyHaven/secluded_beach_2k.hdr";
    const string SeasideClip = "Assets/ThirdParty/Audio/seaside_seagulls_distant.mp3";
    const string MusicClip = "Assets/ThirdParty/Audio/ExplorationTheme.ogg";
    const string MatDir = "Assets/DomeAquarium/Materials";
    const string UiSamples = "Assets/Unity UI Samples/Textures and Sprites/Rounded UI";

    // 모듈 키트가 3m 격자라 홀 크기도 3의 배수로 맞춘다.
    const float HallHalfWidth = 4.5f;       // x -4.5 ~ 4.5
    const float HallLength = 12f;           // z 0 ~ 12
    const float WallHeight = 3f;
    const float ElevatorScale = 0.75f;      // 원본 4m 정육면체 → 3m, 문은 폭 1.5m · 높이 3m
    const float KenneyScale = 0.2f;         // Kenney 가구는 1/5 축척으로 들어온다
    const float SkyRotation = 0f;           // 창(+X) 쪽에 바다 수평선이 오도록 맞추는 HDRI 회전

    static readonly Vector3 SpawnLocal = new Vector3(0f, 0f, 1.8f);
    static readonly Vector3 GateLocal = new Vector3(0f, 0f, 8.4f);
    static readonly Vector3 WalkStartLocal = new Vector3(0f, 0f, 6.0f);   // 개찰구 2.4m 앞
    static readonly Vector3 CabinLocal = new Vector3(0f, 0f, 14.5f);
    static readonly Vector3 SphereLocal = new Vector3(-2.6f, 0f, 5.0f);

    [MenuItem("DomeAquarium/9. 육지 로비 빌드", priority = 9)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Lobby] 플레이 모드에서는 로비를 빌드할 수 없다. Play 를 멈추고 다시 실행할 것.");
            return;
        }
        Build(saveScene: true);
    }

    // 씬에 저장 안 한 작업이 있을 때 — 로비만 세우고 저장 여부는 사람이 정한다.
    [MenuItem("DomeAquarium/디버그/로비 빌드 (씬 저장 안 함)", priority = 202)]
    public static void BuildWithoutSaving()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Build(saveScene: false);
    }

    public static void Build(bool saveScene)
    {
        DestroyExisting();

        var root = new GameObject(RootName);
        root.transform.position = LobbyOrigin;
        var set = new GameObject("LobbySet").transform;
        set.SetParent(root.transform, false);

        var mats = new Mats();
        BuildShell(set, mats);
        BuildWindowWall(set, mats);
        BuildFarWall(set, mats, out Animator elevatorAnimator, out AnimationClip elevatorClip, out TMP_Text depthText);
        BuildGateLine(set, mats, out Animator gateAnimator, out AnimationClip gateClip);
        BuildExhibit(set, mats);
        BuildFurniture(set);
        BuildSigns(set);

        Transform spawn = NewPoint("SpawnPoint", root.transform, SpawnLocal);
        Transform cabin = NewPoint("CabinPoint", root.transform, CabinLocal);
        Transform gate = NewPoint("GatePoint", root.transform, GateLocal);
        Transform walkStart = NewPoint("WalkStartPoint", root.transform, WalkStartLocal);

        CanvasGroup caption = BuildCaption(root.transform, out TMP_Text captionText);
        BuildAudio(root.transform, out AudioSource ambience, out AudioSource music);

        var seq = root.AddComponent<LobbySequence>();
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        seq.rig = origin != null ? origin.transform : null;
        seq.viewCamera = FindMainCamera();
        seq.lobbySet = set.gameObject;
        seq.spawnPoint = spawn;
        seq.cabinPoint = cabin;
        seq.gatePoint = gate;
        seq.walkStartPoint = walkStart;
        seq.elevatorAnimator = elevatorAnimator;
        seq.elevatorClip = elevatorClip;
        seq.gateAnimator = gateAnimator;
        seq.gateClip = gateClip;
        seq.depthText = depthText;
        seq.captionGroup = caption;
        seq.captionText = captionText;
        seq.lobbySkybox = SkyMaterial();
        seq.ambience = ambience;
        seq.music = music;

        var startPanel = Object.FindFirstObjectByType<StartPanel>(FindObjectsInactive.Include);
        if (startPanel != null)
        {
            startPanel.lobby = seq;
            seq.startPanel = startPanel;
            EditorUtility.SetDirty(startPanel);
        }
        else Debug.LogWarning("[Lobby] StartPanel 이 없다. 나레이션이 끝나면 바로 엘리베이터로 들어간다.");

        ApplyObservationTerms();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Lobby] 육지 로비 빌드 완료 (리그: {(seq.rig != null ? seq.rig.name : "없음 — 런타임에 찾는다")}).");
    }

    // ─────────────────────────────────────────────────────────────
    // 건물
    // ─────────────────────────────────────────────────────────────

    static void BuildShell(Transform set, Mats mats)
    {
        var shell = Group("Shell", set);
        // Floor_01·Roof_01 은 피벗 기준 x -3~0, z 0~3 을 덮는다.
        for (float x0 = -HallHalfWidth; x0 < HallHalfWidth - 0.01f; x0 += 3f)
            for (float z0 = 0f; z0 < HallLength - 0.01f; z0 += 3f)
            {
                Place($"{Kit}/Floor_01.prefab", shell, new Vector3(x0 + 3f, 0f, z0), 0f);
                Place($"{Kit}/Roof_01.prefab", shell, new Vector3(x0 + 3f, 0f, z0), 0f);
            }

        // Wall_Simple_01 은 두께 0.25(x -0.25~0) · 길이 3(z 0~3). 회전 방향은 안쪽 면이 홀을 보게.
        for (float z0 = 0f; z0 < HallLength - 0.01f; z0 += 3f)
            Place($"{Kit}/Wall_Simple_01.prefab", shell, new Vector3(-HallHalfWidth, 0f, z0), 0f);          // 왼쪽
        for (float x0 = -HallHalfWidth; x0 < HallHalfWidth - 0.01f; x0 += 3f)
            Place($"{Kit}/Wall_Simple_01.prefab", shell, new Vector3(x0 + 3f, 0f, 0f), -90f);                // 뒷벽
        Place($"{Kit}/Wall_Simple_01.prefab", shell, new Vector3(-HallHalfWidth, 0f, HallLength), 90f);      // 앞벽 왼쪽
        Place($"{Kit}/Wall_Simple_01.prefab", shell, new Vector3(1.5f, 0f, HallLength), 90f);                 // 앞벽 오른쪽

        // 키트 천장 타일은 원래 Room 파츠 위에 얹는 용도라 타일 사이와 벽 윗단(3~3.75m)이 비어 하늘이 샌다.
        // 위에 판 하나, 벽 위에 띠를 둘러 막는다.
        const float top = 3.75f, band = top - WallHeight;
        Box("CeilingSlab", shell, new Vector3(0f, top + 0.05f, HallLength * 0.5f), new Vector3(HallHalfWidth * 2f + 0.5f, 0.1f, HallLength + 0.5f), mats.frame);
        Box("BandLeft", shell, new Vector3(-HallHalfWidth - 0.125f, WallHeight + band * 0.5f, HallLength * 0.5f), new Vector3(0.25f, band, HallLength + 0.5f), mats.frame);
        Box("BandRight", shell, new Vector3(HallHalfWidth, WallHeight + band * 0.5f, HallLength * 0.5f), new Vector3(0.22f, band, HallLength + 0.5f), mats.frame);
        Box("BandBack", shell, new Vector3(0f, WallHeight + band * 0.5f, -0.125f), new Vector3(HallHalfWidth * 2f, band, 0.25f), mats.frame);
        foreach (float side in new[] { -3f, 3f })
            Box("BandFar", shell, new Vector3(side, WallHeight + band * 0.5f, HallLength + 0.125f), new Vector3(3f, band, 0.25f), mats.frame);

        // 천장 네온 패널 — 실시간 광원 없이 발광 재질로만 밝힌다 (Quest 예산).
        for (float x = -3f; x <= 3.01f; x += 3f)
            for (float z = 1.5f; z < HallLength; z += 3f)
                Place($"{Kit}/Light_01.prefab", shell, new Vector3(x, WallHeight, z), 0f);

        // 창밖 테라스 — 유리 너머가 허공이면 땅에 서 있다는 느낌이 사라진다.
        for (float z0 = 0f; z0 < HallLength - 0.01f; z0 += 3f)
        {
            Place($"{Kit}/Floor_01.prefab", shell, new Vector3(HallHalfWidth + 3f, 0f, z0), 0f);
            Place($"{Kit}/Floor_01.prefab", shell, new Vector3(HallHalfWidth + 6f, 0f, z0), 0f);
        }
        float railX = HallHalfWidth + 5.9f;
        Box("TerraceRail", shell, new Vector3(railX, 1.0f, HallLength * 0.5f), new Vector3(0.06f, 0.05f, HallLength), mats.frame);
        Box("TerraceGlass", shell, new Vector3(railX, 0.5f, HallLength * 0.5f), new Vector3(0.03f, 1.0f, HallLength), mats.glass);
    }

    static void BuildWindowWall(Transform set, Mats mats)
    {
        var win = Group("WindowWall", set);
        float x = HallHalfWidth;
        Box("Glass", win, new Vector3(x, WallHeight * 0.5f, HallLength * 0.5f), new Vector3(0.03f, WallHeight, HallLength), mats.glass);
        Box("Sill", win, new Vector3(x, 0.04f, HallLength * 0.5f), new Vector3(0.22f, 0.08f, HallLength), mats.frame);
        Box("Header", win, new Vector3(x, WallHeight - 0.06f, HallLength * 0.5f), new Vector3(0.22f, 0.12f, HallLength), mats.frame);
        for (float z = 0f; z <= HallLength + 0.01f; z += 2f)
            Box($"Mullion_{z:0}", win, new Vector3(x, WallHeight * 0.5f, z), new Vector3(0.09f, WallHeight, 0.09f), mats.frame);
    }

    static void BuildFarWall(Transform set, Mats mats, out Animator animator, out AnimationClip clip, out TMP_Text depthText)
    {
        var far = Group("FarWall", set);
        Place($"{Kit}/Door_Arch_01.prefab", far, new Vector3(1.5f, 0f, HallLength), 0f);
        // 아치 깊이(z 12~13)만큼 바닥이 끊겨 땅(하늘 사진)이 보인다 — 1m 짜리 바닥으로 잇는다.
        Place($"{Kit}/Floor_Small_01.prefab", far, new Vector3(1.5f, 0f, HallLength), 0f);

        // 엘리베이터: 문이 원본 +Z 에 있으므로 180° 돌려 홀을 보게 한다. 문 앞면이 z=13 에 온다.
        GameObject elevator = Place($"{Sketchfab}/Elevator_LowPoly.glb", far, CabinLocal, 180f, ElevatorScale);
        animator = elevator != null ? elevator.GetComponentInChildren<Animator>(true) : null;
        clip = AssetDatabase.LoadAllAssetsAtPath($"{Sketchfab}/Elevator_LowPoly.glb").OfType<AnimationClip>().FirstOrDefault();

        // 엘리베이터 승강로 — 칸 옆·뒤가 비어 있으면 아치 틈으로 바깥 하늘이 보인다.
        float cabinFront = CabinLocal.z - 2f * ElevatorScale;
        float shaftBack = CabinLocal.z + 2f * ElevatorScale + 0.3f;
        float shaftMid = (cabinFront + shaftBack) * 0.5f, shaftLen = shaftBack - cabinFront;
        foreach (float side in new[] { -1.65f, 1.65f })
            Box("ShaftSide", far, new Vector3(side, 1.875f, shaftMid), new Vector3(0.1f, 3.75f, shaftLen), mats.frame);
        Box("ShaftBack", far, new Vector3(0f, 1.875f, shaftBack), new Vector3(3.4f, 3.75f, 0.1f), mats.frame);
        Box("ShaftTop", far, new Vector3(0f, 3.8f, shaftMid), new Vector3(3.4f, 0.1f, shaftLen), mats.frame);

        // 아치(3.75m)가 엘리베이터(3m)보다 높아 생기는 틈을 안내판으로 막는다.
        Box("ElevatorHeader", far, new Vector3(0f, 3.375f, cabinFront + 0.1f), new Vector3(3.0f, 0.75f, 0.2f), mats.frame);
        Label("ElevatorSign", far, new Vector3(0f, 3.36f, cabinFront - 0.012f), 0f, new Vector2(2.8f, 0.6f),
              "B1   해저 관측돔", 0.30f, new Color(0.62f, 0.93f, 1f), TextAlignmentOptions.Center);

        // 칸 안쪽 벽(문 맞은편)의 수심 계기판. 들어서면 정면에 보인다.
        float backWall = CabinLocal.z + 2f * ElevatorScale;
        Box("DepthPanel", far, new Vector3(0f, 1.75f, backWall - 0.06f), new Vector3(1.1f, 0.5f, 0.04f), mats.screen);
        depthText = Label("DepthText", far, new Vector3(0f, 1.75f, backWall - 0.085f), 0f, new Vector2(1.0f, 0.42f),
                          "수심  0 m", 0.16f, new Color(0.55f, 0.95f, 1f), TextAlignmentOptions.Center);
    }

    static void BuildGateLine(Transform set, Mats mats, out Animator animator, out AnimationClip clip)
    {
        var line = Group("GateLine", set);
        GameObject gate = Place($"{Sketchfab}/TicketGate.glb", line, GateLocal, 0f);
        animator = gate != null ? gate.GetComponentInChildren<Animator>(true) : null;
        clip = AssetDatabase.LoadAllAssetsAtPath($"{Sketchfab}/TicketGate.glb").OfType<AnimationClip>().FirstOrDefault();

        // 개찰구 양옆을 낮은 유리 칸막이로 막아 "여기부터 관측소 안"이라는 경계를 만든다.
        const float gateHalf = 0.66f;
        float span = HallHalfWidth - gateHalf;
        foreach (float side in new[] { -1f, 1f })
        {
            float cx = side * (gateHalf + span * 0.5f);
            Box(side < 0 ? "PartitionL" : "PartitionR", line, new Vector3(cx, 0.5f, GateLocal.z), new Vector3(span, 1.0f, 0.03f), mats.glass);
            Box(side < 0 ? "RailL" : "RailR", line, new Vector3(cx, 1.01f, GateLocal.z), new Vector3(span, 0.04f, 0.06f), mats.frame);
        }
    }

    static void BuildExhibit(Transform set, Mats mats)
    {
        var ex = Group("BathysphereExhibit", set);
        const float pedestalHeight = 0.3f;
        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Pedestal";
        pedestal.transform.SetParent(ex, false);
        pedestal.transform.localPosition = SphereLocal + Vector3.up * (pedestalHeight * 0.5f);
        pedestal.transform.localScale = new Vector3(2.6f, pedestalHeight * 0.5f, 2.6f);
        Object.DestroyImmediate(pedestal.GetComponent<Collider>());
        pedestal.GetComponent<Renderer>().sharedMaterial = mats.stone;

        // 스캔 모델 밑에 보도블록 조각이 붙어 있다 — 받침대 윗면보다 6cm 낮춰 그 조각을 받침대 속에 묻는다.
        GameObject sphere = PlaceCentered($"{Sketchfab}/Bathysphere.glb", ex, SphereLocal, 200f, 1f);
        if (sphere != null) sphere.transform.position += Vector3.up * (pedestalHeight - 0.06f);

        // 관람 동선(시작 자리)을 향하는 안내판
        Vector3 plaquePos = SphereLocal + new Vector3(1.55f, 0f, -0.9f);
        Vector3 look = Flat(plaquePos - SpawnLocal);
        float yaw = Quaternion.LookRotation(look.normalized, Vector3.up).eulerAngles.y;
        Box("PlaqueStand", ex, plaquePos + Vector3.up * 0.5f, new Vector3(0.06f, 1.0f, 0.06f), mats.frame, yaw);
        Box("PlaqueBoard", ex, plaquePos + Vector3.up * 1.05f, new Vector3(0.62f, 0.38f, 0.03f), mats.screen, yaw);
        Label("PlaqueText", ex, plaquePos + Vector3.up * 1.05f - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.02f, yaw,
              new Vector2(0.56f, 0.34f),
              "<b>바시스피어  BATHYSPHERE</b>\n<size=70%>1930년, 윌리엄 비브와 오티스 바턴은 이 지름 1.45m의 쇠공을 타고 " +
              "사람으로서 처음 깊은 바다를 직접 들여다봤다. 1934년에는 수심 923m까지 내려갔다.</size>",
              0.034f, new Color(0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
    }

    static void BuildFurniture(Transform set)
    {
        var f = Group("Furniture", set);
        // 창가 라운지 — 바다를 보고 앉는 자리
        PlaceCentered($"{Kenney}/rugRound.fbx", f, new Vector3(2.9f, 0f, 4.6f), 0f, 0.3f);
        PlaceCentered($"{Kenney}/loungeDesignSofa.fbx", f, new Vector3(2.5f, 0f, 4.6f), 90f, KenneyScale);
        PlaceCentered($"{Kenney}/tableCoffeeGlass.fbx", f, new Vector3(3.6f, 0f, 4.6f), 90f, KenneyScale);
        PlaceCentered($"{Kenney}/lampRoundFloor.fbx", f, new Vector3(2.3f, 0f, 6.1f), 0f, KenneyScale);

        // 모서리 화분
        foreach (var p in new[] { new Vector3(-3.9f, 0f, 0.6f), new Vector3(3.9f, 0f, 0.6f),
                                  new Vector3(-3.9f, 0f, 11.4f), new Vector3(3.9f, 0f, 11.4f), new Vector3(-3.9f, 0f, 7.8f) })
            PlaceCentered($"{Kenney}/pottedPlant.fbx", f, p, 0f, KenneyScale);
    }

    static void BuildSigns(Transform set)
    {
        var s = Group("Signs", set);
        float z = HallLength - 0.012f;
        Label("Title", s, new Vector3(-3f, 2.05f, z), 0f, new Vector2(2.6f, 0.6f),
              "STILL", 0.62f, new Color(0.92f, 0.97f, 1f), TextAlignmentOptions.Center);
        Label("Subtitle", s, new Vector3(-3f, 1.62f, z), 0f, new Vector2(2.6f, 0.3f),
              "해양 관측소  ·  MARINE OBSERVATORY", 0.11f, new Color(0.62f, 0.86f, 0.95f), TextAlignmentOptions.Center);

        // 나레이션의 숫자를 벽에도 새겨 둔다 — 말을 놓쳐도 눈으로 읽힌다.
        foreach (var (name, x, number, caption, color) in new[]
                 {
                     ("Stat71", 2.25f, "71%", "지구 표면 중 바다", new Color(0.55f, 0.88f, 1f)),
                     ("Stat80", 3.75f, "80%+", "아직 아무도\n보지 못한 바다", new Color(1f, 0.86f, 0.62f)),
                 })
        {
            Label(name, s, new Vector3(x, 2.25f, z), 0f, new Vector2(1.4f, 0.5f),
                  $"<b>{number}</b>", 0.44f, color, TextAlignmentOptions.Center);
            Label(name + "Caption", s, new Vector3(x, 1.72f, z), 0f, new Vector2(1.4f, 0.45f),
                  caption, 0.12f, new Color(0.80f, 0.88f, 0.93f), TextAlignmentOptions.Top);
        }
        Label("StatSource", s, new Vector3(3f, 1.3f, z), 0f, new Vector2(2.6f, 0.16f),
              "출처: NOAA", 0.06f, new Color(0.6f, 0.7f, 0.78f), TextAlignmentOptions.Center);
    }

    // ─────────────────────────────────────────────────────────────
    // 자막·소리
    // ─────────────────────────────────────────────────────────────

    static CanvasGroup BuildCaption(Transform root, out TMP_Text text)
    {
        var go = new GameObject("LobbyCaption", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = FindMainCamera();
        canvas.sortingOrder = 200;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1100f, 210f);
        rt.localScale = Vector3.one * 0.001f;    // 1.1m x 0.21m
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = NewUI("Panel", go.transform, new Vector2(1100f, 210f));
        var img = bg.gameObject.AddComponent<Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSamples}/UIPanel.png");
        img.type = img.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = new Color(0.02f, 0.07f, 0.12f, 0.80f);
        img.raycastTarget = false;

        var t = NewUI("Text", go.transform, new Vector2(1020f, 180f)).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = DomeAquariumSetup.LoadKoreanFont();
        t.fontSize = 40f;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.color = new Color(0.95f, 0.98f, 1f);
        t.raycastTarget = false;
        t.text = "";
        text = t;
        return group;
    }

    static void BuildAudio(Transform root, out AudioSource ambience, out AudioSource music)
    {
        ConfigureStreaming(SeasideClip);
        ConfigureStreaming(MusicClip);
        ambience = NewSource("LobbyAmbience", root, SeasideClip, 200);
        music = NewSource("LobbyMusic", root, MusicClip, 180);
    }

    static AudioSource NewSource(string name, Transform parent, string clipPath, int priority)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (src.clip == null) Debug.LogWarning($"[Lobby] 소리를 찾지 못했다: {clipPath}");
        src.playOnAwake = false;
        src.loop = true;
        src.volume = 0f;
        src.spatialBlend = 0f;
        src.priority = priority;
        return src;
    }

    // 긴 음악·환경음을 메모리에 통째로 풀면 Quest 에서 수십 MB 를 먹는다 (BGM 과 같은 설정).
    static void ConfigureStreaming(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) return;
        var s = imp.defaultSampleSettings;
        if (s.loadType == AudioClipLoadType.Streaming && s.compressionFormat == AudioCompressionFormat.Vorbis) return;
        s.loadType = AudioClipLoadType.Streaming;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.6f;
        imp.defaultSampleSettings = s;
        imp.loadInBackground = true;
        imp.SaveAndReimport();
    }

    // ─────────────────────────────────────────────────────────────
    // 하늘
    // ─────────────────────────────────────────────────────────────

    static Material SkyMaterial()
    {
        if (AssetImporter.GetAtPath(SkyHdr) is TextureImporter imp)
        {
            // 파노라마는 밉맵이 있으면 경도 0° 이음매에 세로줄이 생긴다.
            bool dirty = imp.mipmapEnabled || imp.maxTextureSize != 2048 || imp.wrapModeU != TextureWrapMode.Repeat;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 2048;
            imp.wrapModeU = TextureWrapMode.Repeat;
            imp.wrapModeV = TextureWrapMode.Clamp;
            if (dirty) imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture>(SkyHdr);
        if (tex == null) { Debug.LogWarning("[Lobby] 하늘 HDRI 가 없다. 로비 창밖이 비어 보인다."); return null; }

        string path = $"{MatDir}/Mat_LobbySky.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Skybox/Panoramic"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Skybox/Panoramic");
        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Mapping", 1f);            // Latitude-Longitude
        mat.SetFloat("_ImageType", 0f);          // 360°
        mat.SetFloat("_Exposure", 1.0f);
        mat.SetFloat("_Rotation", SkyRotation);
        mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    // 기존 씬 문구를 "관찰 기록" 용어로 맞춘다 (씬을 처음부터 다시 빌드하지 않아도 되게)
    // ─────────────────────────────────────────────────────────────

    public static void ApplyObservationTerms()
    {
        var f = ObservationTerms.Frequency;
        var map = new Dictionary<string, string>
        {
            { "MARINE DISCOVERY", ObservationTerms.LogTitleEnglish },
            { "다시 뽑기", ObservationTerms.NextDive },
            { "도감 열기", ObservationTerms.OpenLog },
            { "시작하기", ObservationTerms.Enter },
            { "해양생물관", "STILL" },
            { "눈앞의 상자를 컨트롤러로 건드리면\n바다 친구가 찾아옵니다", "아직 아무도 보지 못한 바다를\n기록하러 갑니다" },
            { "발견 등급", ObservationTerms.FrequencyLabel },
            { "발견만", "기록만" },
            { "일반", f[0] }, { "고급", f[1] }, { "희귀", f[2] }, { "영웅", f[3] }, { "전설", f[4] },
            { "미발견", ObservationTerms.Unobserved },
        };

        int changed = 0;
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.text == null || !map.TryGetValue(t.text, out string next)) continue;
            Undo.RecordObject(t, "Observation terms");
            t.text = next;
            EditorUtility.SetDirty(t);
            changed++;
        }
        Debug.Log($"[Lobby] 씬 문구 {changed}곳을 관찰 기록 용어로 바꿨다.");
    }

    // ─────────────────────────────────────────────────────────────
    // 미리보기 — 헤드셋 없이 로비 구도를 확인한다
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/디버그/로비 미리보기 캡처", priority = 201)]
    public static void CapturePreviews()
    {
        var root = GameObject.Find(RootName);
        var seq = root != null ? root.GetComponent<LobbySequence>() : null;
        if (seq == null) { Debug.LogError("[Lobby] LobbyRoot 가 없다. 먼저 9번 메뉴로 빌드할 것."); return; }

        // 씬 조명을 잠시 로비로 바꿨다가 되돌린다 — 씬 파일에는 흔적을 남기지 않는다.
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        var saved = (RenderSettings.skybox, RenderSettings.fog, RenderSettings.ambientMode, RenderSettings.ambientSkyColor,
                     RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
        var savedSun = sun != null ? (sun.color, sun.intensity, sun.transform.rotation, sun.cookie) : default;

        RenderSettings.skybox = seq.lobbySkybox;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = seq.ambientSky;
        RenderSettings.ambientEquatorColor = seq.ambientEquator;
        RenderSettings.ambientGroundColor = seq.ambientGround;
        if (sun != null)
        {
            sun.color = seq.sunColor;
            sun.intensity = seq.sunIntensity;
            sun.transform.rotation = Quaternion.Euler(seq.sunEuler);
            sun.cookie = null;
        }

        var camGo = new GameObject("LobbyPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 90f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        Vector3 o = root.transform.position;
        Vector3 eye = Vector3.up * 1.6f;
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("1_spawn_forward", o + SpawnLocal + eye, Vector3.forward),
            ("2_spawn_window", o + SpawnLocal + eye, new Vector3(1f, -0.05f, 0.6f)),
            ("3_spawn_exhibit", o + SpawnLocal + eye, SphereLocal + Vector3.up * 1.1f - (SpawnLocal + eye)),
            ("4_before_gate", o + GateLocal + new Vector3(0f, 0f, -2.2f) + eye, Vector3.forward),
            ("5_in_cabin", o + CabinLocal + eye, Vector3.forward),
            ("6_overview", o + new Vector3(-4.0f, 2.7f, 0.4f), new Vector3(0.75f, -0.32f, 1f)),
        };

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LobbyPreview"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var shot in shots)
            {
                cam.transform.SetPositionAndRotation(shot.pos, Quaternion.LookRotation(shot.look.normalized, Vector3.up));
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, shot.name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            Debug.Log($"[Lobby] 미리보기 {shots.Length}장 저장: {dir}");
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);
            (RenderSettings.skybox, RenderSettings.fog, RenderSettings.ambientMode, RenderSettings.ambientSkyColor,
             RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) = saved;
            if (sun != null) (sun.color, sun.intensity, sun.transform.rotation, sun.cookie) = savedSun;
        }
    }

    // 헤드셋 없이 플레이 모드에서 흐름을 끝까지 돌려 보기 위한 도구.
    [MenuItem("DomeAquarium/디버그/플레이 중 - 입장하기 누르기", priority = 203)]
    public static void PressEnterInPlayMode()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Lobby] 플레이 모드에서만 쓸 수 있다."); return; }
        var panel = Object.FindFirstObjectByType<StartPanel>();
        if (panel != null) panel.BeginExperience();
        else Debug.LogWarning("[Lobby] StartPanel 이 없다.");
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 진행 상태 출력", priority = 205)]
    public static void LogPlayState()
    {
        var seq = Object.FindFirstObjectByType<LobbySequence>();
        var fader = Object.FindFirstObjectByType<ScreenFader>();
        var cam = FindMainCamera();
        Debug.Log($"[Lobby] time={Time.time:0.00} unscaled={Time.unscaledTime:0.00} scale={Time.timeScale} frame={Time.frameCount} " +
                  $"inLobby={(seq != null && seq.InLobby)} fade={(fader != null ? fader.FadeAlpha : -1f):0.00} " +
                  $"cam={(cam != null ? cam.transform.position.ToString("F2") : "-")}");
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 카메라 화면 캡처", priority = 204)]
    public static void CaptureMainCamera()
    {
        var cam = FindMainCamera();
        if (cam == null) return;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        float prevAspect = cam.aspect;
        try
        {
            cam.targetTexture = rt;
            cam.aspect = 1280f / 720f;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LobbyPreview"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"play_{System.DateTime.Now:HHmmss}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[Lobby] 카메라 화면 저장: {file}");
        }
        finally
        {
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;
            cam.ResetAspect();
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 도우미
    // ─────────────────────────────────────────────────────────────

    sealed class Mats
    {
        public readonly Material glass = LitMaterial("Mat_LobbyGlass", new Color(0.78f, 0.92f, 1f, 0.16f), 0.95f, 0f, true);
        public readonly Material frame = LitMaterial("Mat_LobbyFrame", new Color(0.13f, 0.15f, 0.17f), 0.55f, 0.6f, false);
        public readonly Material stone = LitMaterial("Mat_LobbyStone", new Color(0.80f, 0.79f, 0.76f), 0.25f, 0f, false);
        public readonly Material screen = LitMaterial("Mat_LobbyScreen", new Color(0.03f, 0.08f, 0.12f), 0.7f, 0f, false);
    }

    static Material LitMaterial(string name, Color color, float smoothness, float metallic, bool transparent)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (transparent)
        {
            // URP/Lit 반투명 — SetFloat 만으로는 블렌드 상태·키워드가 따라오지 않는다 (머티리얼 변환기와 같은 이유).
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void DestroyExisting()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) Object.DestroyImmediate(go);
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Transform NewPoint(string name, Transform parent, Vector3 local)
    {
        var t = Group(name, parent);
        t.localPosition = local;
        return t;
    }

    static GameObject Place(string path, Transform parent, Vector3 local, float yaw, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogWarning($"[Lobby] 에셋을 찾지 못했다: {path}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    /// <summary>피벗이 모서리에 있는 모델(Kenney 등)을 렌더러 바운드 기준으로 바닥 중심에 세운다.</summary>
    static GameObject PlaceCentered(string path, Transform parent, Vector3 localCenter, float yaw, float scale)
    {
        var go = Place(path, parent, localCenter, yaw, scale);
        if (go == null) return null;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return go;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 target = parent.TransformPoint(localCenter);
        go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
        return go;
    }

    static GameObject Box(string name, Transform parent, Vector3 local, Vector3 size, Material mat, float yaw = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // 걸어 다니지 않는다 — 물리 비용만 든다
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>벽에 붙는 글자. 월드 캔버스 1장 + TMP. sizeMeters 는 글상자 크기, fontMeters 는 글자 높이.</summary>
    static TMP_Text Label(string name, Transform parent, Vector3 local, float yaw, Vector2 sizeMeters,
                          string text, float fontMeters, Color color, TextAlignmentOptions align)
    {
        const float unitsPerMeter = 1000f;
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = sizeMeters * unitsPerMeter;
        rt.localScale = Vector3.one / unitsPerMeter;

        var t = NewUI("Text", go.transform, sizeMeters * unitsPerMeter).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = DomeAquariumSetup.LoadKoreanFont();
        t.fontSize = fontMeters * unitsPerMeter;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.color = color;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    static RectTransform NewUI(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    static Camera FindMainCamera()
    {
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.CompareTag("MainCamera")) return c;
        return Camera.main;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
#endif
