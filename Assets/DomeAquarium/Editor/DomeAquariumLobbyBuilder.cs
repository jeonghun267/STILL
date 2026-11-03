#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
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
/// 수조(원점, 반지름 12m · 돔 꼭대기 y=8)가 기준이다. 로비는 그 바로 위 — 엘리베이터 칸이 수조 정중앙 24m 위에 와서,
/// 칸이 곧장 아래로 내려가는 배치다. 로비에서는 바닥과 바다 수면(불투명)이 아래의 수조를 가리고,
/// 수중 돔에 도착하면 LobbySet 을 통째로 꺼 비용이 0 이 된다.
/// 여러 번 실행해도 안전하다(기존 LobbyRoot 를 지우고 다시 세움).
/// </summary>
public static class DomeAquariumLobbyBuilder
{
    const string RootName = "LobbyRoot";

    const string Kit = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs";
    const string Kenney = "Assets/ThirdParty/Kenney_FurnitureKit/Models";
    const string Sketchfab = "Assets/ThirdParty/Sketchfab";
    // 땅을 지운 하늘(Pure Sky). 예전 secluded_beach 는 바위섬 · 절벽이 사진에 박혀 있어 가까이 봐도 입체감이 없었다 —
    // 이제 땅은 DomeAquariumCoastBuilder 가 진짜로 세운다.
    const string SkyHdr = "Assets/ThirdParty/PolyHaven/qwantani_sunset_puresky_2k.hdr";
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
    // HDRI 의 해(u=0.600, 고도 6°)를 로비 해(LobbySequence.sunEuler 32°, -35° — 창 기준 오른쪽 앞)와 같은 방위에 둔다.
    // 그래야 바다에 비친 해 길과 하늘의 해가 한 줄에 선다. (Skybox/Panoramic 의 _Rotation 은 하늘을 -Y 방향으로 돌린다.)
    const float SkyRotation = -18.6f;
    // 새 하늘은 예전 사진보다 열 배 넘게 밝다(위쪽 반구 중앙값 0.53 vs 0.04) — 1.0 이면 로비가 하얗게 바랬다.
    // 수심 벽 띠 · 밤 로비의 하늘 노출은 예전 하늘(1.0) 기준으로 정해 둔 값이라 이 배율을 곱해 쓴다.
    internal const float SkyExposure = 0.3f;

    static readonly Vector3 SpawnLocal = new Vector3(0f, 0f, 2.6f);
    static readonly Vector3 GateLocal = new Vector3(0f, 0f, 11.0f);
    static readonly Vector3 WalkStartLocal = new Vector3(0f, 0f, 8.6f);   // 개찰구 2.4m 앞
    static readonly Vector3 CabinLocal = new Vector3(0f, 0f, HallLength + 2.5f);
    static readonly Vector3 SphereLocal = new Vector3(-3.4f, 0f, 6.2f);

    // 엘리베이터 칸이 수조 정중앙(원점) 바로 위, 바닥이 24m 위에 오게 로비를 놓는다 (예전에는 수조에서 z 로 150m 떨어져 있었다).
    // 바다 수면(로비 y=-1.2)과 부두 받침(y=-3.2)도 돔 꼭대기(y=8)보다 한참 위다. CabinLocal 보다 아래에 두어야 초기화 순서가 맞는다.
    const float LobbyHeight = 24f;
    static readonly Vector3 LobbyOrigin = new Vector3(0f, LobbyHeight, 0f) - CabinLocal;

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
        // 엘리베이터 칸은 로비 건물(LobbySet)과 따로 둔다. 수중 돔에서 다음 층으로 갈 때 로비는 끈 채 칸만 다시 켠다.
        var cabinSet = Group("ElevatorCabin", root.transform);
        BuildFarWall(set, cabinSet, mats, out Animator elevatorAnimator, out AnimationClip elevatorClip, out TMP_Text depthText);
        BuildElevatorConsole(cabinSet, mats);
        BuildGateLine(set, mats, out Animator gateAnimator, out AnimationClip gateClip);
        BuildExhibit(set, mats);
        BuildFurniture(set);
        DepthWallPreview depthWall = BuildSigns(set, mats);

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
        seq.cabinSet = cabinSet.gameObject;
        seq.director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
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
        if (depthWall != null)
        {
            depthWall.lobby = seq;
            WireDepthWallDimming(depthWall, set);
        }
        SeaSurface sea = BuildSea(set, mats);
        BuildSeaLife(set, seq, depthWall, sea);
        DomeAquariumCoastBuilder.Build(set, seq);           // 창밖 땅 · 바위 · 부표 · 배 · 등대 · 갈매기 · 고래
        DomeAquariumTerraceBuilder.Build(set, seq, seq.tutorial);   // 테라스 문 · 데크 걷기 · 관망 쌍안경
        DomeAquariumNpcBuilder.Build(set, seq);                     // 테라스 데크의 선장 · 해변의 놀란
        DomeAquariumQuestPerformance.AssignDomeSet(seq);             // 물 위에 있는 동안 수중 돔을 끈다
        DomeAquariumCabinExtras.Build(seq);                          // 엘리베이터 칸 둥근 창 · 수압 컵
        DomeAquariumGhostNet.Build(seq);                             // 해변 쓰레기 줍기 · 24m 그물에 걸린 거북 (놀란 대화 뒤)
        DomeAquariumWhaleListening.Build(seq);                       // 수중 마이크 고래 소리 · 쌍안경 꼬리 사진 식별
        DomeAquariumNightStories.Build(seq);                         // 엔딩의 밤 "오늘 밤의 이야기" 판 · 다섯 이야기 장면
        DomeAquariumDepthExtras.Build();                             // 24m 산호 백화 · 200m 밤의 대이동
        DomeAquariumTidePoolTouch.Build();                           // 조수 웅덩이에 손 넣기 (놀란 다음)
        DomeAquariumDeepEncounters.Build();                          // 찍을까 바라볼까 · 고래 낙하 · 산갈치 · 밤의 대왕고래
        DomeAquariumBeachBuilder.Build(root.transform, set, seq, seq.tutorial);   // 해변 시작 — 모래사장 · 계단 · 조수 웅덩이 · 장비대
        if (depthWall != null) depthWall.skyProbes = set.GetComponentsInChildren<ReflectionProbe>(true);   // 창밖 하늘 반사도 깊이에 맞춰 어둡게
        BuildNight(root.transform, set, seq, depthWall);

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
        // 테라스 끝 난간은 손잡이 봉 없는 유리판만 — 봉(높이 1m)이 아이 눈높이에서 수평선 바로 아래를 가로질러
        // 돌고래가 뛰는 바다를 가렸다.
        float railX = W + 5.9f;
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
        // 창살은 양 끝에만 — 2.5m 마다 선 세로 창살이 바다를 보는 자리마다 정면에 걸렸다. 통유리(구조용 유리) 벽처럼.
        foreach (float z in new[] { 0f, L })
            Box($"Mullion_{z:0.0}", win, new Vector3(x, H * 0.5f, z), new Vector3(0.09f, H, 0.09f), mats.frame);
    }

    static void BuildFarWall(Transform set, Transform cabin, Mats mats, out Animator animator, out AnimationClip clip, out TMP_Text depthText)
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
        GameObject elevator = Place($"{Sketchfab}/Elevator_LowPoly.glb", cabin, CabinLocal, 180f, ElevatorScale);
        animator = elevator != null ? elevator.GetComponentInChildren<Animator>(true) : null;
        clip = AssetDatabase.LoadAllAssetsAtPath($"{Sketchfab}/Elevator_LowPoly.glb").OfType<AnimationClip>().FirstOrDefault();

        // 엘리베이터 승강로 — 칸 옆·뒤가 비어 있으면 아치 틈으로 바깥 하늘이 보인다.
        float cabinFront = CabinLocal.z - 2f * ElevatorScale;
        float shaftBack = CabinLocal.z + 2f * ElevatorScale + 0.3f;
        float shaftMid = (cabinFront + shaftBack) * 0.5f, shaftLen = shaftBack - cabinFront;
        foreach (float side in new[] { -1.65f, 1.65f })
            Box("ShaftSide", cabin, new Vector3(side, 1.875f, shaftMid), new Vector3(0.1f, 3.75f, shaftLen), mats.frame);
        Box("ShaftBack", cabin, new Vector3(0f, 1.875f, shaftBack), new Vector3(3.4f, 3.75f, 0.1f), mats.frame);
        Box("ShaftTop", cabin, new Vector3(0f, 3.8f, shaftMid), new Vector3(3.4f, 0.1f, shaftLen), mats.frame);

        // 아치(3.75m)가 엘리베이터(3m)보다 높아 생기는 틈을 안내판으로 막는다.
        Box("ElevatorHeader", far, new Vector3(0f, 3.375f, cabinFront + 0.1f), new Vector3(3.0f, 0.75f, 0.2f), mats.frame);
        Label("ElevatorSign", far, new Vector3(0f, 3.36f, cabinFront - 0.012f), 0f, new Vector2(2.8f, 0.6f),
              "B1   해저 관측돔", 0.30f, new Color(0.62f, 0.93f, 1f), TextAlignmentOptions.Center);

        // 칸 안쪽 벽(문 맞은편)의 수심 계기판. 들어서면 정면에 보인다.
        float backWall = CabinLocal.z + 2f * ElevatorScale;
        Box("DepthPanel", cabin, new Vector3(0f, 1.75f, backWall - 0.06f), new Vector3(1.1f, 0.5f, 0.04f), mats.screen);
        depthText = Label("DepthText", cabin, new Vector3(0f, 1.75f, backWall - 0.085f), 0f, new Vector2(1.0f, 0.42f),
                          "수심  0 m", 0.16f, new Color(0.55f, 0.95f, 1f), TextAlignmentOptions.Center);
    }

    // 깊이 버튼 콘솔: 칸 한가운데(서는 자리) 0.55m 앞의 낮은 기둥 위 비스듬한 판. 칸이 3m 라 벽 버튼은 팔이 닿지 않는다.
    // 버튼은 위에서부터 얕은 층 — 24m · 200m · 1,000m. 층 할 일을 마쳐야 다음 버튼이 열린다(ElevatorConsole).
    static readonly string[] ConsoleDepths = { "24m", "200m", "1,000m" };
    const float ConsoleAhead = 0.55f;
    const float ConsoleHeight = 0.98f;
    const float ConsoleTilt = 45f;           // 판이 위로 45° 누워 내려다보는 눈과 마주한다
    const float ConsoleScale = 0.0008f;      // 캔버스 1 = 0.8mm

    static void BuildElevatorConsole(Transform cabin, Mats mats)
    {
        var group = Group("DepthConsole", cabin);
        group.localPosition = CabinLocal + new Vector3(0f, 0f, ConsoleAhead);

        Box("ConsolePost", group, new Vector3(0f, (ConsoleHeight - 0.06f) * 0.5f, 0.04f), new Vector3(0.14f, ConsoleHeight - 0.06f, 0.10f), mats.frame);
        var top = Box("ConsoleTop", group, new Vector3(0f, ConsoleHeight, 0f), new Vector3(0.34f, 0.025f, 0.36f), mats.frame);
        top.transform.localRotation = Quaternion.Euler(-ConsoleTilt, 0f, 0f);

        var canvasGo = new GameObject("ConsoleCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(group, false);
        // 판 윗면에서 1.7cm 띄운다 — 버튼 앞판이 눌려 들어갈 깊이(lift)가 판에 묻히지 않게.
        Vector3 normal = Quaternion.Euler(-ConsoleTilt, 0f, 0f) * Vector3.up;
        canvasGo.transform.localPosition = new Vector3(0f, ConsoleHeight, 0f) + normal * 0.03f;
        canvasGo.transform.localRotation = Quaternion.Euler(90f - ConsoleTilt, 0f, 0f);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = FindMainCamera();   // 비워 두면 레이캐스터 등록·해제 때 카메라가 달라져 예외가 난다 (시작 패널과 같은 이유)
        canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGo.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = new Vector2(380f, 400f);
        crt.localScale = Vector3.one * ConsoleScale;

        DomeAquariumUiKit.EnsureAll();
        var font = DomeAquariumSetup.LoadKoreanFont();
        var plate = NewUI("Plate", canvasGo.transform, new Vector2(380f, 400f));
        DomeAquariumUiKit.TemplatePlate(plate, 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);

        var title = NewUI("Title", canvasGo.transform, new Vector2(340f, 50f)).gameObject.AddComponent<TextMeshProUGUI>();
        title.rectTransform.anchoredPosition = new Vector2(0f, 160f);
        title.font = font;
        title.fontSize = 28f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.55f, 0.95f, 1f);
        title.raycastTarget = false;
        title.text = "내려갈 깊이";

        var console = canvasGo.AddComponent<ElevatorConsole>();
        var slots = new ElevatorConsole.Slot[ConsoleDepths.Length];
        var buttonSize = new Vector2(320f, 84f);
        for (int i = 0; i < ConsoleDepths.Length; i++)
        {
            var rect = NewUI($"Depth_{ConsoleDepths[i]}", canvasGo.transform, buttonSize);
            rect.anchoredPosition = new Vector2(0f, 82f - i * 96f);
            var parts = DomeAquariumUiKit.PokeButton(rect, buttonSize, ConsoleDepths[i], null,
                DomeAquariumUiKit.Tone.Secondary, 40f, font, corner: 16f, lift: 12f);

            // 버튼 오른쪽 작은 글씨 — 갈 차례 / 지금 여기 / 기록 완료 / 잠김
            var state = new GameObject("State", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            state.rectTransform.SetParent(parts.label.rectTransform.parent, false);
            state.font = font;
            state.fontSize = 24f;
            state.alignment = TextAlignmentOptions.Center;
            state.textWrappingMode = TextWrappingModes.NoWrap;
            state.raycastTarget = false;
            state.text = ObservationTerms.FloorLocked;

            // 손가락으로 찌르기 — 버튼 간격(96)보다 낮은 상자라 이웃 버튼까지 같이 눌리지 않는다.
            var col = rect.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(330f, 88f, 200f);
            var rb = rect.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var poke = rect.gameObject.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 1.2f;
            UnityEventTools.AddIntPersistentListener(poke.onPoked, console.Press, i);
            UnityEventTools.AddIntPersistentListener(parts.button.onClick, console.Press, i);

            slots[i] = new ElevatorConsole.Slot { button = parts.button, front = parts.front, depth = parts.label, state = state };
        }
        console.slots = slots;
        console.nextTint = DomeAquariumUiKit.PrimaryTint;
        console.openTint = DomeAquariumUiKit.SecondaryTint;
    }

    // ─────────────────────────────────────────────────────────────
    // 엔딩 — 밤이 된 로비
    // ─────────────────────────────────────────────────────────────

    // 올라와서 서는 자리: 개찰구를 지나 엘리베이터 앞(방금 타고 올라온 칸이 왼쪽 뒤로 열려 있다).
    // 데이터 월(오른쪽 나무색 벽)이 3.2m 앞 정면 — 새 줄 제목 약 2.7°, 아랫줄 약 1.9°.
    // 처음에는 개찰구 앞(z 9.2)에 세웠더니 개찰구 기계와 칸막이 난간이 시야 아래를 가로질렀다.
    static readonly Vector3 NightSpotLocal = new Vector3(1.6f, 0f, 12.6f);
    const float NightSpotYaw = 42f;

    // 수심 벽 띠와 밤 로비의 하늘 노출 — 예전 하늘(노출 1.0) 기준 값에 SkyExposure 를 곱한다.
    static readonly Dictionary<string, float> BandSkyExposure = new Dictionary<string, float>
    {
        { "Sunlit", 0.55f * SkyExposure }, { "Twilight", 0.12f * SkyExposure }, { "Midnight", 0.02f * SkyExposure },
    };
    const float NightSkyExposure = 0.10f * SkyExposure;

    /// <summary>하늘을 바꾼 씬에 수심 벽 띠 · 밤 로비의 하늘 노출을 새 하늘 밝기로 다시 맞춘다 (로비를 다시 세우지 않을 때).</summary>
    internal static void ApplySkyExposure(DepthWallPreview wall, LobbyNight night)
    {
        if (wall != null && wall.bands != null)
        {
            for (int i = 0; i < wall.bands.Length; i++)
                if (wall.bands[i] != null && BandSkyExposure.TryGetValue(wall.bands[i].id, out float e)) wall.bands[i].skyExposure = e;
            EditorUtility.SetDirty(wall);
        }
        if (night != null)
        {
            night.skyExposure = NightSkyExposure;
            EditorUtility.SetDirty(night);
        }
    }

    static void BuildNight(Transform root, Transform set, LobbySequence seq, DepthWallPreview wall)
    {
        Transform spot = NewPoint("NightSpot", root, NightSpotLocal);
        spot.localRotation = Quaternion.Euler(0f, NightSpotYaw, 0f);
        seq.nightSpot = spot;

        var night = root.gameObject.AddComponent<LobbyNight>();
        night.lobby = seq;
        night.wall = wall;
        night.skyExposure = NightSkyExposure;
        night.sky = BuildNightSky(set);
        BuildNewRecordLine(set, night);
        seq.night = night;
    }

    /// <summary>별 500개(위쪽 반구, 800m)와 바다 쪽 하늘의 달(빛무리 + 밝은 속). 낮에는 꺼 둔다.</summary>
    static GameObject BuildNightSky(Transform set)
    {
        var sky = Group("NightSky", set);
        sky.localPosition = new Vector3(0f, 0f, HallLength * 0.5f);

        var starsGo = new GameObject("Stars");
        starsGo.transform.SetParent(sky, false);
        var ps = starsGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = false;
        main.duration = 1f;
        main.maxParticles = 500;
        main.startLifetime = 1000000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 4.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.82f, 1f, 0.55f), new Color(1f, 1f, 1f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)500) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 800f;
        shape.radiusThickness = 0f;            // 껍질에만 — 별이 전부 같은 먼 거리에
        shape.rotation = new Vector3(-90f, 0f, 0f);   // 반구가 위를 보게
        var starMat = AdditiveMaterial("Mat_LobbyStar", "Universal Render Pipeline/Particles/Unlit");
        starMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        EditorUtility.SetDirty(starMat);
        var r = starsGo.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = starMat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.maxParticleSize = 0.05f;

        // 달 — 바다(창, +X) 쪽 하늘 18° 높이. 창가에서 고개를 돌리면 보인다.
        var moonMat = AdditiveMaterial("Mat_LobbyMoon", "Universal Render Pipeline/Particles/Unlit");
        moonMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        if (moonMat.HasProperty("_Cull")) moonMat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(moonMat);
        Vector3 dir = Quaternion.Euler(-18f, 70f, 0f) * Vector3.forward;
        foreach (var (name, size, color) in new[]
                 {
                     ("MoonGlow", 90f, new Color(0.45f, 0.55f, 0.85f, 0.35f)),
                     ("Moon", 26f, new Color(0.95f, 0.96f, 1f, 1f)),
                 })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(sky, false);
            q.transform.localPosition = dir * 700f;
            q.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            q.transform.localScale = Vector3.one * size;
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = moonMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }
        sky.gameObject.SetActive(false);
        return sky.gameObject;
    }

    /// <summary>
    /// 데이터 월(앞벽 오른쪽 나무색 벽 — 71% · 80%+ 아래)의 새 줄. 평소에는 투명하고 엔딩에서 새겨진다.
    /// "+1 처음 관측된 생물" / "수심 1,000m · 최초 관측자 탐험가 N · 날짜". 6m 밖에서 제목 약 1.4°, 아랫줄 약 1°.
    /// </summary>
    static void BuildNewRecordLine(Transform set, LobbyNight night)
    {
        Transform signs = set.Find("Signs");
        Transform parent = signs != null ? signs : set;
        float rightCenter = 1.5f + (HallHalfWidth - 1.5f) * 0.5f;
        const float unitsPerMeter = 1000f;

        var go = new GameObject("NewRecordLine", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(rightCenter, 0.92f, HallLength - 0.035f);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(4200f, 640f);
        rt.localScale = Vector3.one / unitsPerMeter;
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // 표의 새 행처럼 — 위에 가는 빛줄
        var rule = NewUI("Rule", go.transform, new Vector2(3400f, 8f));
        rule.anchoredPosition = new Vector2(0f, 250f);
        var ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.color = new Color(0.55f, 0.95f, 1f, 0.55f);
        ruleImg.raycastTarget = false;

        var font = DomeAquariumSetup.LoadKoreanFont();
        var title = NewUI("Title", go.transform, new Vector2(4000f, 200f)).gameObject.AddComponent<TextMeshProUGUI>();
        title.rectTransform.anchoredPosition = new Vector2(0f, 110f);
        title.font = font;
        title.fontSize = 150f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.55f, 0.95f, 1f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.raycastTarget = false;
        title.text = ObservationTerms.NewRecordTitle;

        var detail = NewUI("Detail", go.transform, new Vector2(4000f, 160f)).gameObject.AddComponent<TextMeshProUGUI>();
        detail.rectTransform.anchoredPosition = new Vector2(0f, -110f);
        detail.font = font;
        detail.fontSize = 108f;
        detail.alignment = TextAlignmentOptions.Center;
        detail.color = new Color(0.95f, 0.92f, 0.86f);
        detail.textWrappingMode = TextWrappingModes.NoWrap;
        detail.raycastTarget = false;
        detail.text = ObservationTerms.NewRecordDetail("1,000m", "탐험가", System.DateTime.Now);

        var chimeGo = new GameObject("Chime");
        chimeGo.transform.SetParent(go.transform, false);
        var chime = chimeGo.AddComponent<AudioSource>();
        chime.playOnAwake = false;
        chime.spatialBlend = 0.6f;             // 벽 쪽에서 들리되 멀어도 잘 들리게 반쯤만 3D
        chime.minDistance = 3f;
        chime.maxDistance = 20f;
        chime.volume = 0.7f;

        night.record = group;
        night.recordTitle = title;
        night.recordDetail = detail;
        night.chime = chime;
        night.chimeClip = DomeAquariumDepthSounds.EnsureChime();
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
              "사람으로서 처음 깊은 바다를 직접 들여다봤다. 1934년에는 수심 923m까지 내려갔다. " +
              "그날 비브는 다섯 줄로 빛나는 물고기를 보고 그림으로 남겼지만, 그 뒤로 아무도 다시 보지 못했다.</size>",
              0.03f, new Color(0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
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

    static DepthWallPreview BuildSigns(Transform set, Mats mats)
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

        return BuildDepthWall(s, mats);
    }

    /// <summary>
    /// 왼쪽 벽의 수심 안내판 — 3막 하강(24m → 200m → 1,000m)을 미리 보여 주고, 띠를 누르면 그 깊이의 빛과 소리가
    /// 로비에 잠깐 퍼진다 (DepthWallPreview). 위에서 아래로 갈수록 어두워지는 띠 세 칸.
    /// 시작 자리(z 2.6)에서 왼쪽으로 고개를 돌리면 정면에 온다.
    /// 띠는 아이 손이 닿는 높이(0.66~2.1m)에 둔다 — v1 은 맨 위 띠가 2.1~2.6m 라 손으로 누를 수 없었다.
    /// v1 은 바시스피어(z 6.2) 바로 뒤에 있어서, 띠를 낮추자 잠수구가 가운데를 가렸다 — 시작 자리 쪽으로 당기고 폭을 줄였다.
    /// </summary>
    static DepthWallPreview BuildDepthWall(Transform s, Mats mats)
    {
        const float x = -HallHalfWidth + 0.03f, zCenter = 3.6f, width = 4.8f;
        const float yaw = -90f;                    // 월드 캔버스는 +Z 를 등지고 보인다 — 글이 +X(홀 안쪽)에서 읽히게
        const float bandH = 0.44f, gap = 0.06f, topY = 1.88f;

        var wall = Group("DepthWall", s);
        Box("DepthBoard", wall, new Vector3(x - 0.01f, 1.6f, zCenter), new Vector3(0.02f, 2.1f, width + 0.2f), mats.frame);
        Label("DepthTitle", wall, new Vector3(x + 0.02f, 2.47f, zCenter), yaw, new Vector2(width - 0.3f, 0.3f),
              "바다는 깊어질수록 어두워져요", 0.17f, new Color(0.95f, 0.98f, 1f), TextAlignmentOptions.MidlineLeft);
        Label("DepthHint", wall, new Vector3(x + 0.02f, 2.22f, zCenter), yaw, new Vector2(width - 0.3f, 0.15f),
              "띠를 눌러 보세요. 그 깊이의 바다가 로비로 찾아와요", 0.09f, new Color(0.62f, 0.88f, 1f), TextAlignmentOptions.MidlineLeft);

        var preview = wall.gameObject.AddComponent<DepthWallPreview>();
        Material mote = MoteMaterial();
        var specs = new[]
        {
            new DepthWallPreview.Band
            {
                id = "Sunlit", volume = 0.55f, rippleColor = new Color(0.55f, 0.92f, 1f, 0.8f),
                // 햇빛이 물에 걸러져 청록빛 — 수면의 물결 무늬가 바닥에 일렁인다
                ambientSky = new Color(0.30f, 0.62f, 0.78f), ambientEquator = new Color(0.20f, 0.48f, 0.62f), ambientGround = new Color(0.12f, 0.32f, 0.42f),
                sunColor = new Color(0.70f, 0.93f, 1f), sunIntensity = 1.0f, caustics = true,
                fogColor = new Color(0.16f, 0.45f, 0.58f), fogDensity = 0.035f,
                skyTint = new Color(0.30f, 0.55f, 0.62f, 0.5f), skyExposure = BandSkyExposure["Sunlit"],
                lampLevel = 0.55f, scanTint = new Color(0.55f, 0.85f, 1f), reflectionLevel = 0.6f,
            },
            new DepthWallPreview.Band
            {
                id = "Twilight", volume = 0.6f, rippleColor = new Color(0.30f, 0.55f, 1f, 0.7f),
                // 빨간빛은 다 사라지고 옅은 파란빛만 — 바다눈이 천천히 내려앉는다
                ambientSky = new Color(0.07f, 0.16f, 0.30f), ambientEquator = new Color(0.04f, 0.11f, 0.22f), ambientGround = new Color(0.02f, 0.06f, 0.12f),
                sunColor = new Color(0.35f, 0.55f, 1f), sunIntensity = 0.18f,
                fogColor = new Color(0.03f, 0.10f, 0.20f), fogDensity = 0.07f,
                skyTint = new Color(0.20f, 0.35f, 0.50f, 0.5f), skyExposure = BandSkyExposure["Twilight"],
                lampLevel = 0.15f, scanTint = new Color(0.16f, 0.26f, 0.42f), reflectionLevel = 0.1f,
            },
            new DepthWallPreview.Band
            {
                id = "Midnight", volume = 0.65f, rippleColor = new Color(0.25f, 1f, 0.85f, 0.7f),
                // 햇빛이 전혀 없다. 보이는 빛은 생물이 내는 빛뿐 — 그래도 완전한 검정(0)은 피한다 (Meta: 가장 어두운 값 13/255 이상)
                ambientSky = new Color(0.03f, 0.05f, 0.09f), ambientEquator = new Color(0.02f, 0.035f, 0.065f), ambientGround = new Color(0.012f, 0.02f, 0.04f),
                sunColor = new Color(0.2f, 0.3f, 0.6f), sunIntensity = 0f,
                fogColor = new Color(0.02f, 0.04f, 0.07f), fogDensity = 0.12f,
                skyTint = new Color(0.15f, 0.25f, 0.4f, 0.5f), skyExposure = BandSkyExposure["Midnight"],
                lampLevel = 0.03f, scanTint = new Color(0.05f, 0.08f, 0.13f), reflectionLevel = 0.02f,
            },
        };
        var bands = new (string label, string note, Color fill)[]
        {
            ("24m  산호초", "햇빛이 닿아 알록달록해요", new Color(0.16f, 0.52f, 0.66f)),
            ("200m  어스름 바다", "빛이 거의 닿지 않아요", new Color(0.07f, 0.24f, 0.38f)),
            ("1,000m  깊은 바다", "스스로 빛을 내는 생물이 살아요", new Color(0.03f, 0.07f, 0.14f)),
        };

        for (int i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            var b = bands[i];
            float y = topY - i * (bandH + gap);
            // 띠 면은 Unlit — 1,000m 처럼 로비가 캄캄해져도 띠가 보여야 다른 띠를 누를 수 있다.
            var band = Box("Depth_" + spec.id, wall, new Vector3(x, y, zCenter), new Vector3(0.02f, bandH, width - 0.3f),
                           UnlitMaterial("Mat_LobbyDepth_" + spec.id, b.fill));
            // 누르는 상자 — 벽에서 방 쪽으로 11cm 내밀어 손끝이 벽에 닿기 전에 눌린다. 컨트롤러 레이도 이 상자를 맞힌다.
            var press = band.AddComponent<BoxCollider>();
            press.center = new Vector3(2.5f, 0f, 0f);              // 로컬 x 1 = 2cm
            press.size = new Vector3(6f, 1f, 1f);
            var poke = band.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 0.6f;
            spec.poke = poke;
            spec.ray = band.AddComponent<XRSimpleInteractable>();
            spec.fill = band.GetComponent<Renderer>();

            Label("DepthLabel_" + spec.id, wall, new Vector3(x + 0.02f, y + 0.06f, zCenter), yaw, new Vector2(width - 0.6f, 0.22f),
                  $"<b>{b.label}</b>", 0.15f, Color.white, TextAlignmentOptions.MidlineLeft);
            Label("DepthNote_" + spec.id, wall, new Vector3(x + 0.02f, y - 0.12f, zCenter), yaw, new Vector2(width - 0.6f, 0.14f),
                  b.note, 0.095f, new Color(0.85f, 0.93f, 0.98f), TextAlignmentOptions.MidlineLeft);

            var sound = new GameObject("Sound_" + spec.id).AddComponent<AudioSource>();
            sound.transform.SetParent(wall, false);
            sound.clip = DomeAquariumDepthSounds.Ensure(spec.id);
            sound.playOnAwake = false;
            sound.loop = true;
            sound.volume = 0f;
            sound.spatialBlend = 0f;                               // 로비 전체에 퍼지는 소리 — 방향 없이
            sound.priority = 150;
            spec.sound = sound;
        }

        specs[0].motes = DepthMotes("Motes_Sunlit", wall, mote, max: 140, rate: 22f, life: new Vector2(4f, 6f), size: new Vector2(0.02f, 0.05f),
                                    velMin: new Vector3(-0.03f, 0.08f, -0.03f), velMax: new Vector3(0.03f, 0.2f, 0.03f),
                                    a: new Color(0.75f, 0.95f, 1f, 0.5f), b: new Color(1f, 1f, 0.9f, 0.35f), twinkle: false);
        specs[1].motes = DepthMotes("Motes_Twilight", wall, mote, max: 260, rate: 40f, life: new Vector2(6f, 8f), size: new Vector2(0.012f, 0.026f),
                                    velMin: new Vector3(-0.02f, -0.1f, -0.02f), velMax: new Vector3(0.02f, -0.04f, 0.02f),
                                    a: new Color(0.8f, 0.85f, 0.9f, 0.55f), b: new Color(0.95f, 0.95f, 1f, 0.4f), twinkle: false);
        specs[2].motes = DepthMotes("Motes_Midnight", wall, mote, max: 110, rate: 32f, life: new Vector2(1.6f, 3.2f), size: new Vector2(0.03f, 0.06f),
                                    velMin: new Vector3(-0.03f, -0.03f, -0.03f), velMax: new Vector3(0.03f, 0.03f, 0.03f),
                                    a: new Color(0.2f, 0.85f, 1f, 1f), b: new Color(0.35f, 1f, 0.6f, 1f), twinkle: true);
        preview.bands = specs;

        // 벽 밑에서 바닥을 따라 퍼지는 빛 고리. 반은 벽 속이라 보이지 않는다.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ring.name = "Ripple";
        ring.transform.SetParent(wall, false);
        ring.transform.localPosition = new Vector3(-HallHalfWidth, 0.012f, zCenter);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);  // 위를 본다
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        var ringRenderer = ring.GetComponent<Renderer>();
        ringRenderer.sharedMaterial = RippleMaterial();
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;
        ringRenderer.enabled = false;
        preview.ripple = ringRenderer;
        return preview;
    }

    /// <summary>
    /// 주변광을 바꿔도 밝기가 그대로인 것들을 수심 벽에 알려 준다 — 천장 네온(발광 재질)과
    /// 바시스피어 스캔 모델(조명이 구워진 Unlit). 이걸 빼면 1,000m 에서도 천장과 잠수구만 환하게 떠 보인다.
    /// </summary>
    static void WireDepthWallDimming(DepthWallPreview wall, Transform set)
    {
        var lamps = new List<Renderer>();
        var slots = new List<int>();
        foreach (var r in set.GetComponentsInChildren<Renderer>(true))
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null || shared[i].name != "Neon_01") continue;
                lamps.Add(r);
                slots.Add(i);
                break;
            }
        }
        wall.lamps = lamps.ToArray();
        wall.lampSlots = slots.ToArray();

        Transform sphere = set.Find("BathysphereExhibit/Bathysphere");
        wall.unlitScans = sphere != null ? sphere.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        Debug.Log($"[Lobby] 수심 벽: 천장 네온 {wall.lamps.Length}개, 스캔 모델 {wall.unlitScans.Length}개를 함께 어둡게 한다.");
    }

    /// <summary>그 깊이의 작은 것들 — 홀 전체 상자에서 생겨 천천히 떠다닌다. 누르는 순간 1/3 이 한꺼번에 생겨 "퍼져 들어온" 느낌을 준다.</summary>
    static ParticleSystem DepthMotes(string name, Transform parent, Material mat, int max, float rate, Vector2 life, Vector2 size,
                                     Vector3 velMin, Vector3 velMax, Color a, Color b, bool twinkle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, CeilingHeight * 0.5f, HallLength * 0.5f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = max;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(max / 3)) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(HallHalfWidth * 2f - 0.4f, CeilingHeight - 0.4f, HallLength - 0.4f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(velMin.x, velMax.x);
        velocity.y = new ParticleSystem.MinMaxCurve(velMin.y, velMax.y);
        velocity.z = new ParticleSystem.MinMaxCurve(velMin.z, velMax.z);

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            twinkle
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.15f, 0.5f),
                          new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) }      // 두 번 깜빡이고 꺼진다
                : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static Material MoteMaterial()
    {
        var mat = AdditiveMaterial("Mat_LobbyDepthMote", "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material RippleMaterial()
    {
        var mat = AdditiveMaterial("Mat_LobbyDepthRipple", "Universal Render Pipeline/Unlit");
        mat.SetTexture("_BaseMap", RingTexture());
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// URP 반투명 파티클·고리용. additive 면 가산(어두운 곳에서 빛나 보이고 겹쳐도 정렬 문제가 없다),
    /// 아니면 보통 알파 섞기(밝은 하늘·물 위의 흰 물보라처럼 빛나면 안 되는 것).
    /// </summary>
    internal static Material AdditiveMaterial(string name, string shaderName, bool additive = true)
    {
        string path = $"{MatDir}/{name}.mat";
        var shader = Shader.Find(shaderName);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.shader != shader) mat.shader = shader;
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);                   // Additive / Alpha
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha"))
            mat.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>가장자리가 부드러운 빛 고리 + 안쪽 옅은 잔광. 지름 대비 두께가 얇아 14m 로 퍼져도 1m 남짓이다.</summary>
    static Texture2D RingTexture()
    {
        string path = $"{MatDir}/SoftRing.png";
        if (!File.Exists(path))
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int py = 0; py < size; py++)
            for (int pxX = 0; pxX < size; pxX++)
            {
                float u = (pxX + 0.5f) / size * 2f - 1f, v = (py + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float edge = (r - 0.93f) / 0.035f;
                float wake = (r - 0.82f) / 0.1f;
                float alpha = r > 1f ? 0f : Mathf.Clamp01(Mathf.Exp(-edge * edge) + 0.18f * Mathf.Exp(-wake * wake));
                px[py * size + pxX] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
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

    // ─────────────────────────────────────────────────────────────
    // 창밖의 실제 바다 — 수평선까지 이어진 수면 + 하늘 반사 + 부두 받침벽 (SeaSurface)
    // ─────────────────────────────────────────────────────────────

    internal const float SeaLevel = -1.2f;  // 테라스(바닥 0)보다 1.2m 아래 — 부두에 선 높이
    // 창(x=6)에서 600m. 눈높이(수면 위 약 2.8m)에서 끝이 수평선 0.3° 아래라 하늘(HDRI)의 수평선과 붙어 보인다 —
    // 1.2km 와 창밖 모습이 같다. 400m 부터는 끝과 수평선 사이에 HDRI 의 물이 띠로 보인다.
    const float SeaSize = 600f;
    const float SeaSwellTile = 9f;          // 큰 물결 노멀맵 한 장이 덮는 크기(m)
    const float SeaRippleTile = 4.3f;       // 잔물결 — 큰 물결과 나누어떨어지지 않아 반복 무늬가 안 보인다

    static SeaSurface BuildSea(Transform set, Mats mats)
    {
        var group = Group("Sea", set);       // LobbySet 아래 — 수중 돔에 도착하면 통째로 꺼진다

        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10m 판
        plane.name = "SeaSurface";
        plane.transform.SetParent(group, false);
        plane.transform.localPosition = new Vector3(HallHalfWidth + SeaSize * 0.5f, SeaLevel, HallLength * 0.5f);
        plane.transform.localScale = new Vector3(SeaSize / 10f, 1f, SeaSize / 10f);
        Object.DestroyImmediate(plane.GetComponent<Collider>());
        var renderer = plane.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = SeaMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        var surface = plane.AddComponent<SeaSurface>();

        // 하늘 반사 — 창밖(x>6)에만 영향. 로비 실내는 지금처럼 기본 반사를 쓴다.
        // 장면 기본 반사는 수중 돔 하늘로 구워져 있어 바다가 어둡게 비친다. 로비 하늘만 담은 큐브맵을 미리 구워 둔다
        // (거칠기별로 흐려진 밉까지 — 런타임에 찍는 비용이 없다).
        var probeGo = new GameObject("SkyProbe");
        probeGo.transform.SetParent(group, false);
        probeGo.transform.localPosition = new Vector3(HallHalfWidth + SeaSize * 0.5f, SeaLevel + 3f, HallLength * 0.5f);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.resolution = 128;
        probe.hdr = true;
        probe.cullingMask = 0;                                         // 하늘만
        probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.Skybox;
        probe.boxProjection = false;
        probe.importance = 2;
        probe.size = new Vector3(SeaSize, 400f, SeaSize);
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
        probe.customBakedTexture = LobbySkyReflection(probe);
        surface.skyProbe = null;

        BuildPier(set, mats);
        return surface;
    }

    /// <summary>테라스 둘레 받침벽 — 테라스 아래가 비어 있으면 부두가 허공에 뜬다. 테라스 타일 크기에서 둘레를 잰다.</summary>
    static void BuildPier(Transform set, Mats mats)
    {
        var shell = set.Find("Shell");
        if (shell == null) return;
        bool any = false;
        Bounds deck = default;
        foreach (var r in shell.GetComponentsInChildren<Renderer>())
        {
            Vector3 c = set.InverseTransformPoint(r.bounds.center);
            if (c.x <= HallHalfWidth + 0.2f || r.bounds.size.y > 0.6f) continue;   // 창 밖의 바닥 타일만
            Bounds local = new Bounds(c, r.bounds.size);
            if (!any) { deck = local; any = true; } else deck.Encapsulate(local);
        }
        if (!any) { Debug.LogWarning("[Lobby] 테라스 바닥을 찾지 못해 부두 받침벽을 세우지 못했다."); return; }

        var pier = Group("Pier", set);
        const float top = -0.02f, bottom = SeaLevel - 2f, t = 0.3f;
        float h = top - bottom, y = (top + bottom) * 0.5f;
        float x0 = HallHalfWidth, x1 = deck.max.x, z0 = deck.min.z, z1 = deck.max.z;
        Box("PierFront", pier, new Vector3(x1 + t * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0 + 2f * t), mats.stone);
        Box("PierSideA", pier, new Vector3((x0 + x1) * 0.5f, y, z0 - t * 0.5f), new Vector3(x1 - x0, h, t), mats.stone);
        Box("PierSideB", pier, new Vector3((x0 + x1) * 0.5f, y, z1 + t * 0.5f), new Vector3(x1 - x0, h, t), mats.stone);
    }

    /// <summary>로비 하늘(HDRI)만 담은 반사 큐브맵을 굽는다. 장면 하늘은 수중 돔 것이라 잠시 로비 하늘로 바꿔 찍고 되돌린다.</summary>
    internal static Texture LobbySkyReflection(ReflectionProbe probe)
    {
        string path = $"{MatDir}/LobbySkyReflection.exr";
        Material sky = SkyMaterial();
        if (sky != null)
        {
            Material saved = RenderSettings.skybox;
            RenderSettings.skybox = sky;
            try { UnityEditor.Lightmapping.BakeReflectionProbe(probe, path); }
            finally { RenderSettings.skybox = saved; }
            AssetDatabase.ImportAsset(path);
        }
        var cube = AssetDatabase.LoadAssetAtPath<Texture>(path);
        if (cube == null) Debug.LogWarning("[Lobby] 바다 하늘 반사를 굽지 못했다 — 바다가 어둡게 비친다.");
        return cube;
    }

    static Material SeaMaterial()
    {
        var mat = LitMaterial("Mat_LobbySea", new Color(0.035f, 0.16f, 0.22f), 0.88f, 0f, false);
        var normal = SeaNormalTexture();
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 0.7f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTextureScale("_BaseMap", Vector2.one * (SeaSize / SeaSwellTile));
        // 잔물결 — 디테일 노멀맵. _DETAIL_SCALED + 배율 0 이면 색은 그대로 두고 노멀만 섞는다.
        mat.SetTexture("_DetailNormalMap", normal);
        mat.SetFloat("_DetailNormalMapScale", 0.45f);
        mat.SetFloat("_DetailAlbedoMapScale", 0f);
        mat.SetTextureScale("_DetailAlbedoMap", Vector2.one * (SeaSize / SeaRippleTile));
        mat.EnableKeyword("_DETAIL_SCALED");
        mat.DisableKeyword("_DETAIL_MULX2");
        mat.SetFloat("_EnvironmentReflections", 1f);
        mat.SetFloat("_SpecularHighlights", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 이어 붙여도 이음매가 없는 물결 노멀맵(256px). 정수 파수의 사인파 28개를 겹친 높이의 기울기 —
    /// 바람(+X) 쪽으로 기운 방향, 긴 물결일수록 크게. 가장 가파른 곳이 약 22° 기울도록 맞춘다.
    /// </summary>
    static Texture2D SeaNormalTexture()
    {
        string path = $"{MatDir}/SeaNormal.png";
        if (!File.Exists(path))
        {
            const int N = 256, waves = 28;
            var rng = new System.Random(71);
            var kx = new int[waves];
            var ky = new int[waves];
            var amp = new float[waves];
            var phase = new float[waves];
            for (int i = 0; i < waves; i++)
            {
                float k = 2f + (float)rng.NextDouble() * 14f;
                float a = ((float)rng.NextDouble() * 2f - 1f) * 1.1f;
                kx[i] = Mathf.RoundToInt(Mathf.Cos(a) * k);
                ky[i] = Mathf.RoundToInt(Mathf.Sin(a) * k);
                if (kx[i] == 0 && ky[i] == 0) kx[i] = 1;
                amp[i] = 1f / Mathf.Pow(Mathf.Sqrt(kx[i] * kx[i] + ky[i] * ky[i]), 1.3f);
                phase[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
            }
            var sx = new float[N * N];
            var sy = new float[N * N];
            float steepest = 1e-6f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = 0f, gy = 0f;
                    for (int i = 0; i < waves; i++)
                    {
                        float c = Mathf.Cos(2f * Mathf.PI * (kx[i] * x + ky[i] * y) / N + phase[i]) * amp[i];
                        gx += c * kx[i];
                        gy += c * ky[i];
                    }
                    sx[y * N + x] = gx;
                    sy[y * N + x] = gy;
                    steepest = Mathf.Max(steepest, Mathf.Sqrt(gx * gx + gy * gy));
                }
            float scale = 0.4f / steepest;         // tan(22°) ≈ 0.4
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int i = 0; i < N * N; i++)
            {
                Vector3 n = new Vector3(-sx[i] * scale, -sy[i] * scale, 1f).normalized;
                px[i] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                                    (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 4;                   // 멀리 비스듬히 보이는 수면이 뭉개지지 않게
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ─────────────────────────────────────────────────────────────
    // 창밖 바다 — 돌고래·쥐가오리·가다랑어가 가끔 뛰어오른다 (SeaJumpShow)
    // ─────────────────────────────────────────────────────────────

    static void BuildSeaLife(Transform set, LobbySequence seq, DepthWallPreview depthWall, SeaSurface surface)
    {
        var sea = Group("SeaLife", set);                     // LobbySet 아래 — 수중 돔에 도착하면 통째로 꺼진다
        var show = sea.gameObject.AddComponent<SeaJumpShow>();
        show.lobby = seq;
        show.sea = surface;
        show.depthWall = depthWall;
        // 1.8m 돌고래가 20m 앞에서 약 5° — 아이가 창가에서 바로 알아본다. 부두 끝(13.5m)과 물보라 고리가 겹치지 않게 20m 밖.
        show.distance = new Vector2(14f, 28f);
        show.minOffshore = 20f;

        FishData Load(string id) => AssetDatabase.LoadAssetAtPath<FishData>($"Assets/DomeAquarium/FishData/{id}.asset");
        show.performers = new[]
        {
            // 돌고래는 숨을 쉬러, 또 빨리 헤엄치려고 물 위로 뛴다 — 여럿이 차례로
            new SeaJumpShow.Performer { fish = Load("HectorDolphin"), weight = 1.6f, pod = new Vector2Int(2, 3),
                height = new Vector2(1.6f, 2.6f), reach = new Vector2(4.5f, 6.5f), spinChance = 0.2f, splashSize = 1f },
            new SeaJumpShow.Performer { fish = Load("RightWhaleDolphin"), weight = 1.2f, pod = new Vector2Int(2, 4),
                height = new Vector2(1.4f, 2.2f), reach = new Vector2(4f, 6f), spinChance = 0.1f, splashSize = 0.9f },
            // 쥐가오리는 혼자 높이 솟았다가 납작하게 떨어진다 — 물보라가 크다
            new SeaJumpShow.Performer { fish = Load("MantaRay"), weight = 0.9f, pod = new Vector2Int(1, 1),
                height = new Vector2(1.8f, 3f), reach = new Vector2(1.6f, 2.6f), sizeMultiplier = 1.4f,
                pitchFollow = 0.45f, spinChance = 0.35f, splashSize = 1.6f },
            // 가다랑어는 먹이를 쫓다 떼로 짧게 튀어 오른다
            new SeaJumpShow.Performer { fish = Load("SkipjackTuna"), weight = 1.1f, pod = new Vector2Int(3, 5),
                height = new Vector2(0.6f, 1.1f), reach = new Vector2(1.8f, 2.8f), sizeMultiplier = 1.2f, splashSize = 0.55f },
        }.Where(p => p.fish != null && p.fish.prefab != null).ToArray();

        show.splash = SplashParticles(sea);
        show.ringMaterial = RippleMaterial();
        show.splashSmall = DomeAquariumDepthSounds.EnsureSplash("Small");
        show.splashBig = DomeAquariumDepthSounds.EnsureSplash("Big");
        show.label = BuildJumpLabel(sea, out TMP_Text labelText);
        show.labelText = labelText;

        BuildSeaSign(set);
    }

    /// <summary>물보라 — 뿌리는 위치가 매번 달라 스크립트가 Emit 한다. 흰 물방울이 솟았다가 중력으로 떨어진다.</summary>
    internal static ParticleSystem SplashParticles(Transform parent)
    {
        var go = new GameObject("Splash");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = true;                 // 스스로는 뿌리지 않고(rate 0) 돌기만 한다 — Emit 한 물방울이 움직이게
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 400;
        main.startSpeed = 0f;
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;

        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1.6f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var mat = AdditiveMaterial("Mat_LobbySplash", "Universal Render Pipeline/Particles/Unlit", additive: false);
        mat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        EditorUtility.SetDirty(mat);
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    /// <summary>뛰는 생물 위에 잠깐 뜨는 이름 — 30m 앞에서 글자 0.7m ≈ 1.3° (아이 기준 본문 1° 이상).</summary>
    static CanvasGroup BuildJumpLabel(Transform parent, out TMP_Text text)
    {
        var go = new GameObject("JumpLabel", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(800f, 160f);
        rt.localScale = Vector3.one * 0.01f;                 // 1 = 1cm
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var pill = NewUI("Pill", go.transform, Vector2.zero);
        var bg = pill.gameObject.AddComponent<Image>();
        bg.sprite = DomeAquariumUiKitSprite();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 0.25f;                  // 키트 모서리 10px → 40cm, 알약 모양
        bg.color = new Color(0.04f, 0.13f, 0.18f, 0.82f);
        bg.raycastTarget = false;
        var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(36, 36, 14, 14);
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var t = NewUI("Name", pill, Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = DomeAquariumSetup.LoadKoreanFont();
        t.fontSize = 70f;
        t.fontStyle = FontStyles.Bold;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = "헥터돌고래";
        text = t;
        return group;
    }

    /// <summary>창유리 위 안내 — 눈높이보다 위(2.6m)라 바다를 가리지 않는다. 시작 자리에서 오른쪽 앞.</summary>
    static void BuildSeaSign(Transform set)
    {
        var go = new GameObject("SeaSign", typeof(RectTransform));
        go.transform.SetParent(set, false);
        go.transform.localPosition = new Vector3(HallHalfWidth - 0.07f, 2.6f, 3.75f);   // 유리 바로 안쪽
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // 월드 캔버스는 +Z 를 등지고 보인다 — 방 안(−X)에서 읽히게
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(2200f, 440f);
        rt.localScale = Vector3.one * 0.001f;

        var panel = NewUI("Panel", go.transform, new Vector2(2200f, 440f));
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = DomeAquariumUiKitSprite();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 0.4f;                   // 모서리 25mm
        bg.color = new Color(0.04f, 0.13f, 0.18f, 0.86f);
        bg.raycastTarget = false;
        var stack = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(60, 60, 44, 44);
        stack.spacing = 18f;
        stack.childAlignment = TextAnchor.MiddleCenter;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandHeight = false;

        TMP_Text Line(string name, string value, float size, FontStyles style, Color color)
        {
            var t = NewUI(name, panel, Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = DomeAquariumSetup.LoadKoreanFont();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = value;
            return t;
        }
        Line("Title", "창밖 바다를 지켜보세요", 120f, FontStyles.Bold, Color.white);
        Line("Body", "돌고래 · 쥐가오리 · 가다랑어가 가끔 물 위로 뛰어올라요", 72f, FontStyles.Normal, new Color(0.72f, 0.92f, 1f));
    }

    // ─────────────────────────────────────────────────────────────
    // 준비 단계 — 장비대 · 연습 수조 · 안내 표시 · 걷기 · 엘리베이터 도착 지점
    // ─────────────────────────────────────────────────────────────

    static readonly Vector3 TableLocal = new Vector3(2.0f, 0f, 4.4f);                     // 시작 자리 오른쪽 앞 — 몇 걸음이면 닿는다 (해변 시작에서는 해변으로 옮긴다)
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
        // 안내판은 없다 — 관찰기 연습은 해변 조수 웅덩이에서 하고(DomeAquariumBeachBuilder), 이 수조는 홀의 볼거리다.

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

        // ── 이동: 막대로 걷기만 (순간이동 없음) + 엘리베이터 안 도착 지점 ──
        // 도착 지점은 걸어 들어오면 탄 것으로 보는 자리(LobbyTutorial.cabinCenter)와 "여기로 오세요" 바닥 빛이다.
        tutorial.locomotionRoot = ConfigureLocomotion(origin, out TeleportationProvider provider);
        ConfigureWalkArea(tutorial);
        InteractionLayerMask teleportLayer = InteractionLayerMask.GetMask("Teleport");
        if (teleportLayer.value == 0) teleportLayer = -1;

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

    const float MoveSpeed = 1.5f;       // m/s — 박물관 걸음. XRI 기본 1, 스타터 리그 2.5 는 실내에서 빠르다
    const float TurnSpeed = 60f;        // °/s — XRI 기본값

    /// <summary>
    /// 리그의 이동 장치를 "왼쪽 막대 걷기 + 오른쪽 막대 부드러운 회전"으로 맞춘다. 순간이동은 없다 — 해변에서 관측소까지 직접 걷는다.
    /// 스냅 회전·잡아 끌기·점프·중력·텔레포트는 끈다 (중력은 바닥 충돌체가 없는 수중 돔에서 리그를 떨어뜨린다).
    /// 텔레포트 제공자는 엘리베이터 도착 지점(TeleportationAnchor)이 가리키므로 지우지 않고 꺼 둔다.
    /// 이동 장치 묶음은 꺼 둔 채 저장하고 준비 단계가 켠다.
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

        // 씬 빌더가 리그의 이동 제공자를 모두 걷어 냈다 — Move·Turn·Teleportation 자리에 필요한 것만 다시 붙인다.
        teleport = rootGo.GetComponentInChildren<TeleportationProvider>(true);
        if (teleport == null) teleport = Undo.AddComponent<TeleportationProvider>(ChildOf(rootGo.transform, "Teleportation"));
        var move = rootGo.GetComponentInChildren<ContinuousMoveProvider>(true);
        if (move == null) move = Undo.AddComponent<ContinuousMoveProvider>(ChildOf(rootGo.transform, "Move"));
        var turn = rootGo.GetComponentInChildren<ContinuousTurnProvider>(true);
        if (turn == null) turn = Undo.AddComponent<ContinuousTurnProvider>(ChildOf(rootGo.transform, "Turn"));
        WireSmoothInput(move, turn);
        SetControllerModes(origin);

        var kept = new List<string>();
        foreach (var p in rootGo.GetComponentsInChildren<LocomotionProvider>(true))
        {
            bool keep = p is ContinuousMoveProvider || p is ContinuousTurnProvider;
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
        if (teleport.gameObject != rootGo && teleport.gameObject.activeSelf)
        {
            Undo.RecordObject(teleport.gameObject, "Lobby locomotion");
            teleport.gameObject.SetActive(false);
        }
        Undo.RecordObject(rootGo, "Lobby locomotion");
        rootGo.SetActive(false);
        Debug.Log($"[Lobby] 이동 장치: {string.Join(", ", kept)} 만 켰다 (준비 단계에서만 활성).");
        return rootGo;
    }

    /// <summary>
    /// 막대로 걸을 수 있는 곳 (로비 로컬 x·z). 로비 벽에는 충돌체가 없어서 이 밖으로는 머리가 못 나가게 막는다.
    /// 개찰구가 열리기 전에는 개찰구 앞까지, 열린 뒤에는 홀 전체와 아치 → 엘리베이터 칸 통로까지.
    /// </summary>
    static void ConfigureWalkArea(LobbyTutorial tutorial)
    {
        const float wall = 0.35f;                  // 벽에 얼굴을 묻지 않을 만큼
        const float doorHalf = 0.55f;              // 엘리베이터 문 폭 1.5m 안쪽
        Undo.RecordObject(tutorial, "Lobby walk area");
        tutorial.hallArea = Rect.MinMaxRect(-HallHalfWidth + wall, wall, HallHalfWidth - wall, HallLength - wall);
        tutorial.gateLineZ = GateLocal.z - wall;
        tutorial.cabinArea = Rect.MinMaxRect(-doorHalf, HallLength - wall, doorHalf, CabinLocal.z + 0.9f);
        EditorUtility.SetDirty(tutorial);
    }

    /// <summary>
    /// 로비를 다시 세우지 않고 이동 방식·걸을 수 있는 곳·이동 안내만 지금 씬에 다시 적용한다.
    /// 로비를 손으로 고친 뒤라 "9. 육지 로비 빌드" 로 덮어쓰고 싶지 않을 때 쓴다.
    /// </summary>
    [MenuItem("DomeAquarium/디버그/로비 이동 방식 다시 적용 (걷기·부드러운 회전)", priority = 216)]
    public static void ReapplyLocomotion()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var tutorial = Object.FindFirstObjectByType<LobbyTutorial>(FindObjectsInactive.Include);
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (tutorial == null || origin == null)
        {
            Debug.LogError("[Lobby] LobbyTutorial 또는 XR Origin 이 없다. 먼저 \"9. 육지 로비 빌드\" 를 실행할 것.");
            return;
        }
        Undo.RecordObject(tutorial, "Lobby locomotion");
        tutorial.locomotionRoot = ConfigureLocomotion(origin, out _);
        tutorial.moveLine = LobbyTutorial.DefaultMoveLine;
        ConfigureWalkArea(tutorial);
        // 예전 빌드가 깔아 둔 순간이동 바닥(홀 · 테라스 데크 · 해변)을 걷어 낸다
        int removed = 0;
        foreach (var area in tutorial.GetComponentsInChildren<TeleportationArea>(true))
        {
            Undo.DestroyObjectImmediate(area.gameObject);
            removed++;
        }
        EditorUtility.SetDirty(tutorial);
        EditorSceneManager.MarkSceneDirty(tutorial.gameObject.scene);   // 저장은 사람이 한다 — 다른 손작업이 섞여 있을 수 있다
        Debug.Log($"[Lobby] 이동 방식 다시 적용: 왼쪽 막대 걷기 · 오른쪽 막대 부드러운 회전 (순간이동 없음, 순간이동 바닥 {removed}개 걷어 냄).");
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

    /// <summary>
    /// 걷기·회전을 XRI 기본 입력의 "XRI Left/Right Locomotion / Move · Turn" 에 잇는다.
    /// 어느 손 막대가 실제로 쓰이는지는 컨트롤러의 ControllerInputActionManager 가 액션을 켜고 꺼서 정한다.
    /// </summary>
    static void WireSmoothInput(ContinuousMoveProvider move, ContinuousTurnProvider turn)
    {
        const string actionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        var refs = AssetDatabase.LoadAllAssetsAtPath(actionsPath).OfType<UnityEngine.InputSystem.InputActionReference>().ToArray();
        UnityEngine.InputSystem.InputActionReference Find(string map, string action) =>
            refs.FirstOrDefault(r => r.action != null && r.action.name == action && r.action.actionMap != null && r.action.actionMap.name == map);

        var leftMove = Find("XRI Left Locomotion", "Move");
        var rightMove = Find("XRI Right Locomotion", "Move");
        var leftTurn = Find("XRI Left Locomotion", "Turn");
        var rightTurn = Find("XRI Right Locomotion", "Turn");
        if (leftMove == null || rightMove == null || leftTurn == null || rightTurn == null)
        {
            Debug.LogWarning($"[Lobby] 걷기·회전 입력을 찾지 못했다: {actionsPath}");
            return;
        }

        Undo.RecordObject(move, "Lobby locomotion");
        move.leftHandMoveInput = new XRInputValueReader<Vector2>("Left Hand Move") { inputActionReference = leftMove };
        move.rightHandMoveInput = new XRInputValueReader<Vector2>("Right Hand Move") { inputActionReference = rightMove };
        move.moveSpeed = MoveSpeed;
        move.enableStrafe = true;
        move.enableFly = false;
        EditorUtility.SetDirty(move);

        Undo.RecordObject(turn, "Lobby locomotion");
        turn.leftHandTurnInput = new XRInputValueReader<Vector2>("Left Hand Turn") { inputActionReference = leftTurn };
        turn.rightHandTurnInput = new XRInputValueReader<Vector2>("Right Hand Turn") { inputActionReference = rightTurn };
        turn.turnSpeed = TurnSpeed;
        EditorUtility.SetDirty(turn);
    }

    /// <summary>
    /// 왼손은 걷기, 오른손은 부드러운 회전. 스타터 리그는 걷지 않는 손의 막대를 앞으로 밀면 텔레포트 레이를 켠다 —
    /// 그 손의 "텔레포트 모드" 입력을 비워 막대를 밀어도 아무 일이 없게 한다 (회전 입력은 그대로).
    /// 샘플 어셈블리를 참조하지 않으려고 이름으로 찾고 직렬화 필드로 고친다.
    /// </summary>
    static void SetControllerModes(XROrigin origin)
    {
        int found = 0;
        foreach (var m in origin.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (m == null || m.GetType().Name != "ControllerInputActionManager") continue;
            bool left = m.gameObject.name.Contains("Left");
            var so = new SerializedObject(m);
            so.FindProperty("m_SmoothMotionEnabled").boolValue = left;
            so.FindProperty("m_SmoothTurnEnabled").boolValue = !left;
            foreach (string teleportInput in new[] { "m_TeleportMode", "m_TeleportModeCancel" })
            {
                var prop = so.FindProperty(teleportInput);
                if (prop != null) prop.objectReferenceValue = null;
            }
            so.ApplyModifiedProperties();
            found++;
        }
        if (found == 0) Debug.LogWarning("[Lobby] ControllerInputActionManager 가 없어 막대 역할(걷기·회전)을 나누지 못했다.");
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
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(unlit);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.shader != unlit) mat.shader = unlit;     // 예전에 Lit 으로 만든 같은 이름의 머티리얼도 바꾼다 (수심 띠)
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

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 수심 벽 24m 누르기", priority = 210)]
    public static void DebugDepth24() => PressDepthBand(0);

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 수심 벽 200m 누르기", priority = 211)]
    public static void DebugDepth200() => PressDepthBand(1);

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 수심 벽 1,000m 누르기", priority = 212)]
    public static void DebugDepth1000() => PressDepthBand(2);

    static int nextJumper;

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 창밖 점프 보기 (차례로)", priority = 213)]
    public static void DebugSeaJump()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Lobby] 플레이 모드에서만 쓸 수 있다."); return; }
        var show = Object.FindFirstObjectByType<SeaJumpShow>();
        if (show == null || show.performers == null || show.performers.Length == 0) { Debug.LogWarning("[Lobby] SeaJumpShow 가 없다. 9번 메뉴로 로비를 다시 빌드할 것."); return; }
        int index = nextJumper++ % show.performers.Length;
        show.JumpNow(index);
        Debug.Log($"[Lobby] 창밖 점프: {show.performers[index].fish.DisplayName}");
    }

    static void PressDepthBand(int index)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Lobby] 플레이 모드에서만 쓸 수 있다."); return; }
        var wall = Object.FindFirstObjectByType<DepthWallPreview>();
        if (wall == null) { Debug.LogWarning("[Lobby] DepthWallPreview 가 없다. 9번 메뉴로 로비를 다시 빌드할 것."); return; }
        wall.Press(index);
        Debug.Log($"[Lobby] 수심 벽: {index} 번 띠를 눌렀다 (켜진 띠 {wall.ActiveBand}).");
    }

    // ─────────────────────────────────────────────────────────────
    // 자막·소리
    // ─────────────────────────────────────────────────────────────

    internal const float CaptionPanelAlpha = 0.94f;

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
        rt.localScale = Vector3.one * 0.0006f;   // 66 x 12.6cm — 0.9m 앞(LobbySequence.captionDistance)에서 예전 1.5m 의 1.1m 판과 같은 크기로 보인다
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = NewUI("Panel", go.transform, new Vector2(1100f, 210f));
        var img = bg.gameObject.AddComponent<Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSamples}/UIPanel.png");
        img.type = img.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = new Color(0.02f, 0.07f, 0.12f, CaptionPanelAlpha);   // 맨 위에 그려지므로 뒤의 간판 글씨가 비치지 않게 거의 불투명
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
        DomeAquariumOverlayUi.Apply(go);   // 가까운 캐릭터 · 엘리베이터 벽 · 물고기에 가려지지 않게 맨 위에 그린다
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

    internal static Material SkyMaterial()
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
        mat.SetFloat("_Exposure", SkyExposure);
        mat.SetFloat("_Rotation", Mathf.Repeat(SkyRotation, 360f));
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
        cam.farClipPlane = 1300f;            // 창밖 바다가 수평선까지
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
            ("9_depth_wall", o + SpawnLocal + new Vector3(0.5f, 0f, 0.6f) + eye, new Vector3(-1f, -0.08f, 0.1f)),
            // 준비 단계 — 장비대 앞, 연습 수조 앞(아이 눈높이 1.35m)
            ("10_prep_table", o + TableLocal + new Vector3(-0.4f, 0f, -1.3f) + Vector3.up * 1.35f, new Vector3(0.3f, -0.55f, 1f)),
            ("11_practice_tank", o + TankLocal + new Vector3(-2.3f, 0f, 0f) + Vector3.up * 1.35f, new Vector3(1f, -0.12f, 0f)),
            // 창밖 바다 — 점프가 일어나는 만(灣)과 창유리 안내판
            ("12_window_sea", o + new Vector3(3.2f, 0f, 2.4f) + eye, new Vector3(1f, 0.02f, 0.18f)),
        };

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LobbyPreview"));
        Directory.CreateDirectory(dir);
        // 빌드 직후 같은 틱에 찍으면 다른 아틀라스 장에 든 글자(보조 메시)가 아직 없어 "STI"처럼 빠진다.
        // 주변광 프로브도 바꾼 하늘로 다시 구워야 해를 등진 면이 수중 씬 주변광으로 어둡게 찍히지 않는다.
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        DynamicGI.UpdateEnvironment();
        foreach (var probe in root.GetComponentsInChildren<ReflectionProbe>())                        // 실시간 프로브만 — 구운 하늘 반사는 그대로
            if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) probe.RenderProbe();
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

    internal static Material LitMaterial(string name, Color color, float smoothness, float metallic, bool transparent)
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

    internal static GameObject Box(string name, Transform parent, Vector3 local, Vector3 size, Material mat, float yaw = 0f)
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
    internal static TMP_Text Label(string name, Transform parent, Vector3 local, float yaw, Vector2 sizeMeters,
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
