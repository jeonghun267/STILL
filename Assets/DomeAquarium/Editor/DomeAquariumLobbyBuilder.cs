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
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// 1막 "육지" — 해 질 녘 해양 관측소 로비를 세우고 LobbySequence 를 배선한다.
///
/// 배치 (로비 로컬 좌표, 바닥 y=0, 정면 +Z, 폭 12m · 천장 4.5m):
///   z=0    뒷벽 ─ 시작 자리(z=2.6)
///          왼쪽: 바시스피어 전시(1930년 잠수구) + 벽의 수심 안내판     오른쪽: 바닥~천장 통유리창 + 소파
///   z=11   개찰구와 낮은 유리 칸막이 — 여기부터 "관측소 안"
///   z=15   앞벽: STILL 간판 · 71% / 80% 데이터 월(나무색 강조 벽) · 가운데 아치 너머 엘리베이터
///   z=17.5 엘리베이터 칸 중심 — 안쪽 벽에 수심 계기판
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

    // 바닥 타일(모듈 키트)이 3m 격자라 홀 크기도 3의 배수로 맞춘다.
    // v1(폭 9m · 길이 12m · 천장 3m)은 VR 에서 답답했고, 시작 자리가 뒷벽 1.8m 앞이었다.
    // 폭 12m · 길이 15m · 천장 4.5m 로 넓히고, 개찰구 앞 공간을 3m 늘렸다. 자동 걷기 거리(8.5m → 8.9m)는 거의 그대로.
    const float HallHalfWidth = 6f;         // x -6 ~ 6
    const float HallLength = 15f;           // z 0 ~ 15
    const float CeilingHeight = 4.5f;
    const float WallHeight = 3f;            // 키트 문(아치)·엘리베이터 기준 높이. 벽면의 가로 줄눈도 이 높이에 둔다
    const float ArchHeight = 3.75f;
    const float ElevatorScale = 0.75f;      // 원본 4m 정육면체 → 3m, 문은 폭 1.5m · 높이 3m
    const float KenneyScale = 0.2f;         // Kenney 가구는 1/5 축척으로 들어온다
    const float SkyRotation = 0f;           // 창(+X) 쪽에 바다 수평선이 오도록 맞추는 HDRI 회전

    static readonly Vector3 SpawnLocal = new Vector3(0f, 0f, 2.6f);
    static readonly Vector3 GateLocal = new Vector3(0f, 0f, 11.0f);
    static readonly Vector3 WalkStartLocal = new Vector3(0f, 0f, 8.6f);   // 개찰구 2.4m 앞
    static readonly Vector3 CabinLocal = new Vector3(0f, 0f, HallLength + 2.5f);
    static readonly Vector3 SphereLocal = new Vector3(-3.4f, 0f, 6.2f);

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
        // 실내 주변광. 실시간 보조광·GI 를 쓰지 않으니(Quest 예산) 바닥 반사광을 주변광 아랫색으로 넣는다.
        // v1 의 아랫색 0.20 은 야외 흙바닥 값이라, 아래를 보는 천장과 해를 등진 벽이 짙은 회색으로 가라앉았다.
        seq.ambientSky = new Color(0.66f, 0.70f, 0.76f);
        seq.ambientEquator = new Color(0.58f, 0.56f, 0.53f);
        seq.ambientGround = new Color(0.50f, 0.48f, 0.45f);   // 밝은 바닥 타일에서 올라오는 빛
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

        seq.tutorial = BuildPrep(root.transform, set, mats, seq, origin);

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
        const float W = HallHalfWidth, L = HallLength, H = CeilingHeight;

        // 바닥만 모듈 키트 타일(Floor_01 은 피벗 기준 x -3~0, z 0~3)을 쓴다.
        for (float x0 = -W; x0 < W - 0.01f; x0 += 3f)
            for (float z0 = 0f; z0 < L - 0.01f; z0 += 3f)
                Place($"{Kit}/Floor_01.prefab", shell, new Vector3(x0 + 3f, 0f, z0), 0f);

        // 벽과 천장은 이음새 없는 판 한 장씩. v1 은 키트 벽·천장 타일 사이를 띠와 판으로 덧대서
        // 타일 틈으로 검은 천장판이 구멍처럼 비치고, 모서리마다 바깥 배경이 샜다.
        // 판끼리는 모서리에서 서로 0.2m 씩 겹쳐 어느 각도에서도 틈이 생기지 않게 한다.
        Box("Ceiling", shell, new Vector3(0f, H + 0.05f, L * 0.5f), new Vector3(W * 2f + 0.8f, 0.1f, L + 0.8f), mats.ceiling);
        Box("WallLeft", shell, new Vector3(-W - 0.1f, H * 0.5f, L * 0.5f), new Vector3(0.2f, H + 0.1f, L + 0.4f), mats.stone);
        Box("WallBack", shell, new Vector3(0f, H * 0.5f, -0.1f), new Vector3(W * 2f + 0.4f, H + 0.1f, 0.2f), mats.stone);

        // 걸레받이와 3m 높이의 가로 줄눈 — 큰 흰 벽이 밋밋하지 않게, 문·엘리베이터 높이와 선을 맞춘다.
        Box("BaseLeft", shell, new Vector3(-W + 0.01f, 0.06f, L * 0.5f), new Vector3(0.02f, 0.12f, L), mats.frame);
        Box("BaseBack", shell, new Vector3(0f, 0.06f, 0.01f), new Vector3(W * 2f, 0.12f, 0.02f), mats.frame);
        Box("RevealLeft", shell, new Vector3(-W + 0.005f, WallHeight, L * 0.5f), new Vector3(0.01f, 0.03f, L), mats.frame);
        Box("RevealBack", shell, new Vector3(0f, WallHeight, 0.005f), new Vector3(W * 2f, 0.03f, 0.01f), mats.frame);

        // 천장 네온 패널 — 실시간 광원 없이 발광 재질로만 밝힌다 (Quest 예산).
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
            foreach (float z in new[] { 2f, 5.5f, 9f, 12.5f })
                Place($"{Kit}/Light_01.prefab", shell, new Vector3(x, H, z), 0f);

        // 창밖 테라스 — 유리 너머가 허공이면 땅에 서 있다는 느낌이 사라진다.
        for (float z0 = 0f; z0 < L - 0.01f; z0 += 3f)
        {
            Place($"{Kit}/Floor_01.prefab", shell, new Vector3(W + 3f, 0f, z0), 0f);
            Place($"{Kit}/Floor_01.prefab", shell, new Vector3(W + 6f, 0f, z0), 0f);
        }
        float railX = W + 5.9f;
        Box("TerraceRail", shell, new Vector3(railX, 1.0f, L * 0.5f), new Vector3(0.06f, 0.05f, L), mats.frame);
        Box("TerraceGlass", shell, new Vector3(railX, 0.5f, L * 0.5f), new Vector3(0.03f, 1.0f, L), mats.glass);
    }

    static void BuildWindowWall(Transform set, Mats mats)
    {
        var win = Group("WindowWall", set);
        const float x = HallHalfWidth, L = HallLength, H = CeilingHeight;
        // 바닥부터 천장까지 통유리 — 창 위가 막혀 있으면 천장이 더 낮아 보인다.
        Box("Glass", win, new Vector3(x, H * 0.5f, L * 0.5f), new Vector3(0.03f, H, L), mats.glass);
        Box("Sill", win, new Vector3(x, 0.04f, L * 0.5f), new Vector3(0.22f, 0.08f, L), mats.frame);
        Box("Header", win, new Vector3(x, H - 0.06f, L * 0.5f), new Vector3(0.22f, 0.12f, L), mats.frame);
        for (float z = 0f; z <= L + 0.01f; z += 2.5f)
            Box($"Mullion_{z:0.0}", win, new Vector3(x, H * 0.5f, z), new Vector3(0.09f, H, 0.09f), mats.frame);
    }

    static void BuildFarWall(Transform set, Mats mats, out Animator animator, out AnimationClip clip, out TMP_Text depthText)
    {
        var far = Group("FarWall", set);
        const float W = HallHalfWidth, L = HallLength, H = CeilingHeight;

        // 앞벽: 엘리베이터 아치(폭 3m, x -1.5~1.5) 자리만 뚫린 판 세 장. 아랫단은 나무색 강조 벽으로 간판을 받친다.
        float leftW = W + 0.2f - 1.5f, rightW = W + 0.2f - 1.5f;
        Box("FarWallLeft", far, new Vector3(-1.5f - leftW * 0.5f, H * 0.5f, L + 0.1f), new Vector3(leftW, H + 0.1f, 0.2f), mats.stone);
        Box("FarWallRight", far, new Vector3(1.5f + rightW * 0.5f, H * 0.5f, L + 0.1f), new Vector3(rightW, H + 0.1f, 0.2f), mats.stone);
        Box("FarWallOverArch", far, new Vector3(0f, (ArchHeight + H) * 0.5f, L + 0.1f), new Vector3(3.2f, H - ArchHeight + 0.1f, 0.2f), mats.stone);
        foreach (float side in new[] { -1f, 1f })
        {
            float cx = side * (1.5f + (W - 1.5f) * 0.5f);
            Box(side < 0 ? "AccentLeft" : "AccentRight", far, new Vector3(cx, WallHeight * 0.5f, L - 0.01f), new Vector3(W - 1.5f, WallHeight, 0.02f), mats.wood);
            Box(side < 0 ? "BaseFarL" : "BaseFarR", far, new Vector3(cx, 0.06f, L - 0.03f), new Vector3(W - 1.5f, 0.12f, 0.02f), mats.frame);
        }

        Place($"{Kit}/Door_Arch_01.prefab", far, new Vector3(1.5f, 0f, L), 0f);
        // 아치 깊이(z L~L+1)만큼 바닥이 끊겨 땅(하늘 사진)이 보인다 — 1m 짜리 바닥으로 잇는다.
        Place($"{Kit}/Floor_Small_01.prefab", far, new Vector3(1.5f, 0f, L), 0f);

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
        // 창가 라운지 — 바다를 보고 앉는 자리. 통로(x -1.5~1.5)와 넉넉히 떨어뜨린다.
        PlaceCentered($"{Kenney}/rugRound.fbx", f, new Vector3(4.0f, 0f, 6.2f), 0f, 0.3f);
        PlaceCentered($"{Kenney}/loungeDesignSofa.fbx", f, new Vector3(3.6f, 0f, 6.2f), 90f, KenneyScale);
        PlaceCentered($"{Kenney}/tableCoffeeGlass.fbx", f, new Vector3(4.7f, 0f, 6.2f), 90f, KenneyScale);
        PlaceCentered($"{Kenney}/lampRoundFloor.fbx", f, new Vector3(4.9f, 0f, 4.9f), 0f, KenneyScale);   // 연습 수조 앞을 비워 둔다

        // 모서리 화분
        float c = HallHalfWidth - 0.6f;
        foreach (var p in new[] { new Vector3(-c, 0f, 0.6f), new Vector3(c, 0f, 0.6f),
                                  new Vector3(-c, 0f, HallLength - 0.6f), new Vector3(c, 0f, HallLength - 0.6f),
                                  new Vector3(-c, 0f, 10.2f) })
            PlaceCentered($"{Kenney}/pottedPlant.fbx", f, p, 0f, KenneyScale);
    }

    static void BuildSigns(Transform set)
    {
        var s = Group("Signs", set);
        float z = HallLength - 0.035f;          // 나무색 강조 벽(두께 2cm) 바로 앞
        float leftCenter = -(1.5f + (HallHalfWidth - 1.5f) * 0.5f);
        float rightCenter = -leftCenter;
        Label("Title", s, new Vector3(leftCenter, 2.15f, z), 0f, new Vector2(3.2f, 0.7f),
              "STILL", 0.72f, new Color(0.96f, 0.98f, 1f), TextAlignmentOptions.Center);
        Label("Subtitle", s, new Vector3(leftCenter, 1.66f, z), 0f, new Vector2(3.2f, 0.3f),
              "해양 관측소  ·  MARINE OBSERVATORY", 0.12f, new Color(0.92f, 0.88f, 0.80f), TextAlignmentOptions.Center);

        // 나레이션의 숫자를 벽에도 새겨 둔다 — 말을 놓쳐도 눈으로 읽힌다.
        foreach (var (name, x, number, caption, color) in new[]
                 {
                     ("Stat71", rightCenter - 0.8f, "71%", "지구 표면 중 바다", new Color(0.70f, 0.92f, 1f)),
                     ("Stat80", rightCenter + 0.8f, "80%+", "아직 아무도\n보지 못한 바다", new Color(1f, 0.88f, 0.66f)),
                 })
        {
            Label(name, s, new Vector3(x, 2.3f, z), 0f, new Vector2(1.5f, 0.55f),
                  $"<b>{number}</b>", 0.48f, color, TextAlignmentOptions.Center);
            Label(name + "Caption", s, new Vector3(x, 1.76f, z), 0f, new Vector2(1.5f, 0.45f),
                  caption, 0.13f, new Color(0.95f, 0.92f, 0.86f), TextAlignmentOptions.Top);
        }
        Label("StatSource", s, new Vector3(rightCenter, 1.3f, z), 0f, new Vector2(2.6f, 0.16f),
              "출처: NOAA", 0.065f, new Color(0.85f, 0.80f, 0.72f), TextAlignmentOptions.Center);

        BuildDepthWall(s);
    }

    /// <summary>
    /// 왼쪽 벽의 수심 안내판 — 넓어진 벽이 휑하지 않게, 그리고 3막 하강(24m → 200m → 1,000m)을 미리 보여 준다.
    /// 위에서 아래로 갈수록 어두워지는 띠 세 칸. 시작 자리에서 왼쪽으로 고개를 돌리면 정면에 온다.
    /// </summary>
    static void BuildDepthWall(Transform s)
    {
        const float x = -HallHalfWidth + 0.03f, zCenter = 6.2f, width = 5.4f;
        const float yaw = -90f;                    // 월드 캔버스는 +Z 를 등지고 보인다 — 글이 +X(홀 안쪽)에서 읽히게
        var bands = new (string id, float depth, string label, string note, Color fill)[]
        {
            ("Sunlit", 24f, "24m  산호초", "햇빛이 닿아 알록달록해요", new Color(0.16f, 0.52f, 0.66f)),
            ("Twilight", 200f, "200m  어스름 바다", "빛이 거의 닿지 않아요", new Color(0.07f, 0.24f, 0.38f)),
            ("Midnight", 1000f, "1,000m  깊은 바다", "스스로 빛을 내는 생물이 살아요", new Color(0.03f, 0.07f, 0.14f)),
        };
        var mats = new Mats();
        Box("DepthBoard", s, new Vector3(x - 0.01f, 1.85f, zCenter), new Vector3(0.02f, 2.3f, width + 0.2f), mats.frame);
        Label("DepthTitle", s, new Vector3(x + 0.02f, 2.78f, zCenter), yaw, new Vector2(width - 0.3f, 0.32f),
              "바다는 깊어질수록 어두워져요", 0.17f, new Color(0.95f, 0.98f, 1f), TextAlignmentOptions.MidlineLeft);
        float bandH = 0.52f;
        for (int i = 0; i < bands.Length; i++)
        {
            float y = 2.36f - i * (bandH + 0.06f);
            var b = bands[i];
            var fill = LitMaterial("Mat_LobbyDepth_" + b.id, b.fill, 0.2f, 0f, false);
            Box("Depth_" + b.id, s, new Vector3(x, y, zCenter), new Vector3(0.02f, bandH, width - 0.3f), fill);
            Label("DepthLabel_" + b.id, s, new Vector3(x + 0.02f, y + 0.07f, zCenter), yaw, new Vector2(width - 0.6f, 0.26f),
                  $"<b>{b.label}</b>", 0.15f, Color.white, TextAlignmentOptions.MidlineLeft);
            Label("DepthNote_" + b.id, s, new Vector3(x + 0.02f, y - 0.13f, zCenter), yaw, new Vector2(width - 0.6f, 0.2f),
                  b.note, 0.095f, new Color(0.85f, 0.93f, 0.98f), TextAlignmentOptions.MidlineLeft);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 준비 단계 — 장비대 · 연습 수조 · 안내 표시 · 텔레포트 바닥 · 엘리베이터 도착 지점
    // ─────────────────────────────────────────────────────────────

    static readonly Vector3 TableLocal = new Vector3(2.0f, 0f, 4.4f);                     // 시작 자리 오른쪽 앞 — 첫 텔레포트 한 번이면 닿는다
    static readonly Vector3 TankLocal = new Vector3(HallHalfWidth - 0.75f, 0f, 9.3f);     // 창가 — 바다를 배경으로 물고기를 본다

    static LobbyTutorial BuildPrep(Transform root, Transform set, Mats mats, LobbySequence seq, XROrigin origin)
    {
        var prep = Group("Prep", set);       // LobbySet 아래 — 수중 돔에 도착하면 통째로 꺼진다
        var tutorial = root.gameObject.AddComponent<LobbyTutorial>();
        tutorial.lobby = seq;
        tutorial.aim = Object.FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);

        // ── 장비대 ──
        var table = Group("EquipmentTable", prep);
        table.localPosition = TableLocal;
        Box("Counter", table, new Vector3(0f, 0.41f, 0f), new Vector3(1.1f, 0.82f, 0.55f), mats.wood);
        Box("CounterTop", table, new Vector3(0f, 0.84f, 0f), new Vector3(1.16f, 0.04f, 0.6f), mats.frame);
        Label("TableSign", table, new Vector3(0f, 0.52f, -0.29f), 0f, new Vector2(1.0f, 0.22f),
              "<b>장비대</b>", 0.13f, new Color(0.96f, 0.93f, 0.86f), TextAlignmentOptions.Center);

        // 장비대 위 관찰기 — 쥐기·트리거(멀리서도 레이로), 손으로 건드리기, 에디터 마우스로 집는다.
        // 바깥 트리거 상자는 손 찌르기(PokeSelector), 안쪽 단단한 상자는 XRI 레이가 잡는다 (레이는 트리거를 무시한다).
        GameObject onTable = BuildDeviceModel(table, mats, "ObservationDevice_Table");
        onTable.transform.localPosition = new Vector3(0f, 0.93f, -0.05f);
        onTable.transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);      // 화면이 위·관람객 쪽을 보게 비스듬히 기댄다
        var pokeVolume = onTable.AddComponent<BoxCollider>();
        pokeVolume.isTrigger = true;
        pokeVolume.size = new Vector3(0.3f, 0.22f, 0.18f);
        var grabVolume = new GameObject("GrabVolume").AddComponent<BoxCollider>();
        grabVolume.transform.SetParent(onTable.transform, false);
        grabVolume.size = new Vector3(0.22f, 0.16f, 0.08f);
        var pickup = onTable.AddComponent<XRSimpleInteractable>();
        var poke = onTable.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = "Hand";
        poke.inputCooldown = 0.5f;
        tutorial.deviceOnTable = onTable;
        tutorial.devicePickup = pickup;
        tutorial.devicePoke = poke;

        // 오른손에 들리는 관찰기 — 집기 전에는 꺼져 있다. 로비를 다시 빌드할 때 겹치지 않게 이전 것을 지운다.
        Transform right = origin != null ? origin.transform.Find("Camera Offset/Right Controller") : null;
        if (right != null)
        {
            Transform old = right.Find("ObservationDevice_Hand");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            GameObject inHand = BuildDeviceModel(right, mats, "ObservationDevice_Hand");
            inHand.transform.localPosition = new Vector3(0f, 0.03f, 0.04f);
            inHand.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);     // 화면(-Z)이 손등 위로 들어 올린 눈을 보게
            inHand.transform.localScale = Vector3.one * 0.85f;
            inHand.SetActive(false);
            tutorial.deviceInHand = inHand;
        }
        else Debug.LogWarning("[Lobby] Right Controller 를 찾지 못해 손에 드는 관찰기를 붙이지 못했다.");

        // ── 연습 수조 ──
        const float standH = 0.8f, tankH = 0.8f, tankW = 0.72f, tankL = 1.9f;
        var tank = Group("PracticeTank", prep);
        tank.localPosition = TankLocal;
        Box("Stand", tank, new Vector3(0f, standH * 0.5f, 0f), new Vector3(tankW + 0.06f, standH, tankL + 0.06f), mats.frame);
        Box("Sand", tank, new Vector3(0f, standH + 0.02f, 0f), new Vector3(tankW - 0.02f, 0.04f, tankL - 0.02f), mats.stone);
        Box("Water", tank, new Vector3(0f, standH + tankH * 0.5f, 0f), new Vector3(tankW - 0.02f, tankH - 0.06f, tankL - 0.02f), WaterMaterial());
        Box("Glass", tank, new Vector3(0f, standH + tankH * 0.5f, 0f), new Vector3(tankW, tankH, tankL), mats.glass);
        Box("Rim", tank, new Vector3(0f, standH + tankH + 0.015f, 0f), new Vector3(tankW + 0.04f, 0.03f, tankL + 0.04f), mats.frame);
        Label("TankSign", tank, new Vector3(-(tankW + 0.06f) * 0.5f - 0.01f, 0.5f, 0f), 90f, new Vector2(1.6f, 0.3f),
              "<b>연습 수조</b>  ·  관찰기로 물고기를 겨눠 보세요", 0.075f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Center);

        var fishRoot = Group("Fish", tank);
        fishRoot.localPosition = new Vector3(0f, standH + tankH * 0.5f, 0f);
        var practice = fishRoot.gameObject.AddComponent<PracticeTank>();
        practice.fish = new[] { "Clownfish", "Clownfish", "BlueTang", "YellowTang" }
            .Select(id => AssetDatabase.LoadAssetAtPath<FishData>($"Assets/DomeAquarium/FishData/{id}.asset"))
            .Where(f => f != null).ToArray();
        practice.innerHalfSize = new Vector3(tankW * 0.5f - 0.06f, tankH * 0.5f - 0.1f, tankL * 0.5f - 0.08f);
        tutorial.practiceTank = practice;

        // ── 지금 할 일 표시 ──
        tutorial.markers = new[]
        {
            Marker("Marker_Table", prep, TableLocal + new Vector3(0f, 1.75f, 0f), "관찰기"),
            Marker("Marker_Tank", prep, TankLocal + new Vector3(-0.2f, 2.15f, 0f), "연습 수조"),
            Marker("Marker_Elevator", prep, new Vector3(0f, 2.55f, HallLength - 0.6f), "엘리베이터"),
        };

        // ── 텔레포트: 개찰구 앞 홀 바닥 전체 + 엘리베이터 안 도착 지점 ──
        tutorial.locomotionRoot = ConfigureLocomotion(origin, out TeleportationProvider provider);
        InteractionLayerMask teleportLayer = InteractionLayerMask.GetMask("Teleport");
        if (teleportLayer.value == 0) teleportLayer = -1;

        var floor = new GameObject("TeleportFloor");
        floor.transform.SetParent(prep, false);
        floor.transform.localPosition = new Vector3(0f, -0.05f, GateLocal.z * 0.5f);
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(HallHalfWidth * 2f - 0.6f, 0.1f, GateLocal.z - 0.8f);
        var area = floor.AddComponent<TeleportationArea>();
        area.interactionLayers = teleportLayer;
        area.teleportationProvider = provider;
        area.matchOrientation = MatchOrientation.WorldSpaceUp;

        var anchorGo = new GameObject("CabinAnchor");
        anchorGo.transform.SetParent(prep, false);
        anchorGo.transform.localPosition = CabinLocal;
        var anchorCollider = anchorGo.AddComponent<BoxCollider>();
        anchorCollider.size = new Vector3(1.3f, 0.1f, 1.3f);
        anchorCollider.center = new Vector3(0f, -0.05f, 0f);
        var anchor = anchorGo.AddComponent<TeleportationAnchor>();
        anchor.teleportAnchorTransform = anchorGo.transform;   // 계기판(+Z)을 보고 선다
        anchor.matchOrientation = MatchOrientation.TargetUpAndForward;
        anchor.interactionLayers = teleportLayer;
        anchor.teleportationProvider = provider;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);        // "여기로 오세요" 바닥 빛
        disc.name = "AnchorGlow";
        disc.transform.SetParent(anchorGo.transform, false);
        disc.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        disc.transform.localScale = new Vector3(1.0f, 0.004f, 1.0f);
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.GetComponent<Renderer>().sharedMaterial = UnlitMaterial("Mat_LobbyAnchorGlow", new Color(0.12f, 0.55f, 0.68f));
        tutorial.cabinAnchor = anchor;
        tutorial.cabinCenter = anchorGo.transform;
        return tutorial;
    }

    /// <summary>
    /// 리그의 이동 장치를 "텔레포트 + 스냅 회전"만 남기고 끈다. 연속 이동·연속 회전·잡아 끌기·점프·중력은 끈다
    /// (중력은 바닥 충돌체가 없는 수중 돔에서 리그를 떨어뜨린다). 이동 장치 묶음은 꺼 둔 채 저장하고 준비 단계가 켠다.
    /// </summary>
    static GameObject ConfigureLocomotion(XROrigin origin, out TeleportationProvider teleport)
    {
        teleport = null;
        if (origin == null) { Debug.LogWarning("[Lobby] XR Origin 이 없어 이동 장치를 설정하지 못했다."); return null; }
        var body = origin.GetComponentInChildren<XRBodyTransformer>(true);
        if (body == null) { Debug.LogWarning("[Lobby] XRBodyTransformer 가 없어 텔레포트를 켤 수 없다."); return null; }

        GameObject rootGo = body.gameObject;
        if (origin.GetComponentInChildren<LocomotionMediator>(true) == null)
        {
            Undo.AddComponent<LocomotionMediator>(rootGo);
            Debug.Log("[Lobby] LocomotionMediator 가 없어 붙였다.");
        }

        // 이 리그(손 추적 데모)는 Turn·Teleportation 자리만 있고 제공자 컴포넌트가 빠져 있다 — 없으면 붙인다.
        teleport = rootGo.GetComponentInChildren<TeleportationProvider>(true);
        if (teleport == null) teleport = Undo.AddComponent<TeleportationProvider>(ChildOf(rootGo.transform, "Teleportation"));
        var snap = rootGo.GetComponentInChildren<SnapTurnProvider>(true);
        if (snap == null) snap = Undo.AddComponent<SnapTurnProvider>(ChildOf(rootGo.transform, "Turn"));
        WireSnapTurnInput(snap);

        var kept = new List<string>();
        foreach (var p in rootGo.GetComponentsInChildren<LocomotionProvider>(true))
        {
            bool keep = p is TeleportationProvider || p is SnapTurnProvider;
            Undo.RecordObject(p, "Lobby locomotion");
            p.enabled = keep;
            if (keep)
            {
                kept.Add(p.GetType().Name);
                if (!p.gameObject.activeSelf)
                {
                    Undo.RecordObject(p.gameObject, "Lobby locomotion");
                    p.gameObject.SetActive(true);
                }
            }
            EditorUtility.SetDirty(p);
        }
        Undo.RecordObject(rootGo, "Lobby locomotion");
        rootGo.SetActive(false);
        Debug.Log($"[Lobby] 이동 장치: {string.Join(", ", kept)} 만 켰다 (준비 단계에서만 활성).");
        return rootGo;
    }

    static GameObject ChildOf(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Lobby locomotion");
            child.SetParent(parent, false);
        }
        return child.gameObject;
    }

    /// <summary>스냅 회전을 XRI 기본 입력의 "XRI Left/Right Locomotion / Snap Turn" 에 잇는다 (엄지 막대 좌우).</summary>
    static void WireSnapTurnInput(SnapTurnProvider snap)
    {
        const string actionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        var refs = AssetDatabase.LoadAllAssetsAtPath(actionsPath).OfType<UnityEngine.InputSystem.InputActionReference>().ToArray();
        UnityEngine.InputSystem.InputActionReference Find(string map) =>
            refs.FirstOrDefault(r => r.action != null && r.action.name == "Snap Turn" && r.action.actionMap != null && r.action.actionMap.name == map);

        var left = Find("XRI Left Locomotion");
        var right = Find("XRI Right Locomotion");
        if (left == null || right == null)
        {
            Debug.LogWarning($"[Lobby] 스냅 회전 입력을 찾지 못했다: {actionsPath}");
            return;
        }
        Undo.RecordObject(snap, "Lobby locomotion");
        snap.leftHandTurnInput = new UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputValueReader<Vector2>("Left Hand Snap Turn") { inputActionReference = left };
        snap.rightHandTurnInput = new UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputValueReader<Vector2>("Right Hand Snap Turn") { inputActionReference = right };
        EditorUtility.SetDirty(snap);
    }

    /// <summary>손바닥만 한 관찰기 — 몸통, 뒤쪽(-Z) 화면, 앞쪽(+Z) 렌즈, 아래 손잡이.</summary>
    static GameObject BuildDeviceModel(Transform parent, Mats mats, string name)
    {
        var device = new GameObject(name);
        device.transform.SetParent(parent, false);
        Box("Body", device.transform, Vector3.zero, new Vector3(0.15f, 0.095f, 0.032f), mats.frame);
        Box("Screen", device.transform, new Vector3(0f, 0.004f, -0.0165f), new Vector3(0.125f, 0.07f, 0.002f),
            UnlitMaterial("Mat_DeviceScreen", new Color(0.22f, 0.78f, 0.92f)));
        Box("Grip", device.transform, new Vector3(0f, -0.07f, 0.004f), new Vector3(0.04f, 0.06f, 0.024f), mats.frame);
        var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lens.name = "Lens";
        lens.transform.SetParent(device.transform, false);
        lens.transform.localPosition = new Vector3(0.045f, 0.02f, 0.02f);
        lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        lens.transform.localScale = new Vector3(0.03f, 0.008f, 0.03f);
        Object.DestroyImmediate(lens.GetComponent<Collider>());
        lens.GetComponent<Renderer>().sharedMaterial = mats.screen;
        return device;
    }

    /// <summary>지금 할 일 위에 뜨는 이름표 + 아래를 가리키는 마름모. GuideMarker 가 오르내리며 보는 사람 쪽으로 돈다.</summary>
    static GameObject Marker(string name, Transform parent, Vector3 local, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<GuideMarker>();

        const float unitsPerMeter = 1000f;
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(go.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)canvasGo.transform;
        rt.sizeDelta = new Vector2(900f, 400f);
        rt.localScale = Vector3.one / unitsPerMeter;

        var pill = NewUI("Pill", canvasGo.transform, new Vector2(Mathf.Max(420f, text.Length * 150f + 120f), 210f));
        pill.anchoredPosition = new Vector2(0f, 60f);
        var pillImage = pill.gameObject.AddComponent<Image>();
        pillImage.sprite = DomeAquariumUiKitSprite();
        pillImage.type = Image.Type.Sliced;
        pillImage.color = new Color(0.06f, 0.15f, 0.20f, 0.92f);
        pillImage.raycastTarget = false;

        var label = NewUI("Text", pill, pill.sizeDelta).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = DomeAquariumSetup.LoadKoreanFont();
        label.fontSize = 130f;                    // 13cm — 6m 거리에서 약 1.2°
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = text;

        var pointer = NewUI("Pointer", canvasGo.transform, new Vector2(80f, 80f));
        pointer.anchoredPosition = new Vector2(0f, -95f);
        pointer.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var pointerImage = pointer.gameObject.AddComponent<Image>();
        pointerImage.color = new Color(0.30f, 0.84f, 1f, 1f);
        pointerImage.raycastTarget = false;

        go.SetActive(false);
        return go;
    }

    static Sprite DomeAquariumUiKitSprite()
    {
        DomeAquariumUiKit.EnsureAll();
        return DomeAquariumUiKit.Panel;
    }

    static Material WaterMaterial()
    {
        // 너무 옅으면 빈 유리 상자로 보인다 — 물빛이 확실히 돌되 물고기는 비치는 정도.
        var mat = LitMaterial("Mat_LobbyTankWater", new Color(0.16f, 0.50f, 0.68f, 0.38f), 0.9f, 0f, true);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        return mat;
    }

    static Material UnlitMaterial(string name, Color color)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 헤드셋 없이 준비 단계를 끝까지 돌려 보는 도구
    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 관찰기 집기", priority = 208)]
    public static void DebugPickUp() => WithTutorial(t => t.PickUp());

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 엘리베이터 타기", priority = 209)]
    public static void DebugBoard() => WithTutorial(t => t.Board());

    static void WithTutorial(System.Action<LobbyTutorial> action)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Lobby] 플레이 모드에서만 쓸 수 있다."); return; }
        var t = Object.FindFirstObjectByType<LobbyTutorial>();
        if (t == null) { Debug.LogWarning("[Lobby] LobbyTutorial 이 없다."); return; }
        action(t);
        Debug.Log($"[Lobby] 준비 단계: {t.Current}");
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
            ("6_overview", o + new Vector3(-HallHalfWidth + 0.5f, 3.6f, 0.4f), new Vector3(0.75f, -0.32f, 1f)),
            // 빈틈 점검 — 뒤돌아본 모습, 천장 모서리, 왼쪽 벽 안내판
            ("7_look_back", o + GateLocal + new Vector3(0f, 0f, -0.8f) + eye, new Vector3(0.15f, 0.05f, -1f)),
            ("8_ceiling_corner", o + SpawnLocal + eye, new Vector3(-0.6f, 0.9f, -0.4f)),
            ("9_depth_wall", o + SpawnLocal + new Vector3(0.5f, 0f, 1.6f) + eye, new Vector3(-1f, 0f, 0.35f)),
            // 준비 단계 — 장비대 앞, 연습 수조 앞(아이 눈높이 1.35m)
            ("10_prep_table", o + TableLocal + new Vector3(-0.4f, 0f, -1.3f) + Vector3.up * 1.35f, new Vector3(0.3f, -0.55f, 1f)),
            ("11_practice_tank", o + TankLocal + new Vector3(-2.3f, 0f, 0f) + Vector3.up * 1.35f, new Vector3(1f, -0.12f, 0f)),
        };

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LobbyPreview"));
        Directory.CreateDirectory(dir);
        // 빌드 직후 같은 틱에 찍으면 다른 아틀라스 장에 든 글자(보조 메시)가 아직 없어 "STI"처럼 빠진다.
        // 주변광 프로브도 바꾼 하늘로 다시 구워야 해를 등진 면이 수중 씬 주변광으로 어둡게 찍히지 않는다.
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        DynamicGI.UpdateEnvironment();
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
            DynamicGI.UpdateEnvironment();
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
        // 천장은 벽보다 한 톤 밝게 — v1 은 짙은 프레임 재질이라 타일 틈이 검은 구멍처럼 보였다.
        public readonly Material ceiling = LitMaterial("Mat_LobbyCeiling", new Color(0.90f, 0.90f, 0.88f), 0.1f, 0f, false);
        // 앞벽 아랫단 강조 — 흰 벽만 있으면 VR 에서 거리감이 흐려진다.
        public readonly Material wood = LitMaterial("Mat_LobbyWood", new Color(0.50f, 0.36f, 0.25f), 0.3f, 0f, false);
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
        mat.DisableKeyword("_EMISSION");                 // 로비 재질은 발광하지 않는다 (밝기는 주변광으로)
        mat.SetColor("_EmissionColor", Color.black);
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
