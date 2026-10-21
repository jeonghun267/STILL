#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 층 단위 하강 (기획 문서 "의뢰와 하강 연결") — 24m 산호초 → 200m 박광층 입구 → 1,000m 심해(3막).
///  1. 98종 FishData 에 서식 층을 적는다. 2막 8종은 200m, 강에 사는 돌고래 3종은 어느 층에도 넣지 않는다.
///  2. 층마다 DiveMission 에셋을 만든다: 할 일 두 줄, 등장 생물, 조명, 관측소장 대사.
///  3. 씬에 DiveDirector 를 두고 관찰기 위 할 일 판, 돔의 "엘리베이터 타기" 판을 만든다.
/// 칸 안 깊이 버튼은 엘리베이터와 같이 로비 빌드(9)가 만든다. 순서: 9 → 18. 18 을 먼저 돌려도 런타임에 서로 찾는다.
/// </summary>
public static class DomeAquariumDiveSetup
{
    const string MissionDir = "Assets/DomeAquarium/Missions";
    const string FishDir = "Assets/DomeAquarium/FishData";
    const string RingSpritePath = "Assets/VRTemplateAssets/Sprites/UI/Circle_60x60 Outline 4.png";
    const int ReefAgentBudget = 150;          // 총 마릿수 상한 (= 드로우콜 예산). 씬 빌드(4)와 같은 값

    // 2막 박광층 입구 — 기획 문서의 "기존 8종": 다랑어 3종, 환도상어, 백상아리, 쥐가오리, 장수거북, 참돌고래붙이.
    // 늘 헤엄치지 않고 60~90초마다 한 무리씩 돔 가까이 지나간다 — 이 층은 "기다리는 법"을 배운다.
    // (이름, 한 번에 몇 마리, 가로지르는 초) — 빠른 다랑어는 떼로 짧게, 쥐가오리·장수거북은 혼자 천천히.
    static readonly (string name, int min, int max, float seconds)[] Twilight =
    {
        ("SkipjackTuna", 4, 6, 22f), ("YellowfinTuna", 3, 4, 24f), ("BluefinTuna", 2, 3, 26f),
        ("CommonThresherShark", 1, 1, 30f), ("GreateWhiteShark", 1, 1, 30f), ("MantaRay", 1, 1, 34f),
        ("LeatherbackTurtle", 1, 1, 32f), ("RightWhaleDolphin", 2, 3, 24f),
    };

    // 강에 사는 종 — 바다 관찰 기록에 나오면 사실 오류다 (분홍돌고래=아마존강, 투쿠시돌고래, 인더스강돌고래).
    static readonly string[] Freshwater = { "PinkDolphin", "TucuxiDophin", "IndusRiverDolphin" };

    // 1막 볼거리. 산호초를 찾는 상어·가오리·거북·돌고래와 관람객이 기대하는 산호초 어종, 지목 생물인 해마류.
    // 이 종들과 해마류는 늘 돔에 남고, 나머지 산호초 종은 몇 분마다 무리 단위로 바뀐다(rotation).
    static readonly string[] ReefMustHave =
    {
        "BlacktipReefShark", "NurseShark", "SpottedEagleRay",
        "GreenTurtle", "HawksbillTurtle", "LoggerheadTurtle", "HectorDolphin",
        "Clownfish", "TomatoClownfish",
        "BlueTang", "YellowTang", "MoorishIdol",
        "GreenChromis", "DominoDamsel",
        "FireGoby", "BlackSeahorse", "RedSeahorse", "BlueLobster",
        "QueenAngelfish", "CopperbandButterflyfish",
    };

    // 관찰기 상태 화면과 같은 색 (가장 어두운 채널도 13/255 위 — Meta 디스플레이 가이드)
    static readonly Color Deep = new Color(0.06f, 0.15f, 0.20f, 0.94f);

    [MenuItem("DomeAquarium/18. 층별 하강 적용 (24m → 200m → 1,000m)", priority = 18)]
    public static void ApplyMenu() { Apply(true); }

    public static DiveDirector Apply(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[층 하강] 플레이 모드에서는 적용할 수 없습니다.");
            return null;
        }

        var all = LoadAllFish();
        if (all.Count == 0) { Debug.LogError($"[층 하강] FishData 가 없습니다: {FishDir}. 먼저 '3. FishData 생성'을 실행하세요."); return null; }
        AssignHabitats(all);

        var lobby = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        var swarm = Object.FindFirstObjectByType<BoidManager>(FindObjectsInactive.Include);
        var aim = Object.FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        var timeOfDay = Object.FindFirstObjectByType<TimeOfDayController>(FindObjectsInactive.Include);
        if (swarm == null || aim == null)
        {
            Debug.LogError("[층 하강] BoidManager 또는 ObservationAim 이 없습니다. '4. 씬 빌드'와 '17. 관찰기 적용'을 먼저 실행하세요.");
            return null;
        }

        Directory.CreateDirectory(MissionDir);
        DiveMission reef = BuildReefMission(all, lobby);
        DiveMission twilight = BuildTwilightMission(all);
        FishData unknownFish = BuildUnknownFish();
        DiveMission midnight = BuildMidnightMission(unknownFish);
        AssetDatabase.SaveAssets();

        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director == null) director = new GameObject("DiveDirector").AddComponent<DiveDirector>();
        director.missions = new[] { reef, twilight, midnight };
        director.lobby = lobby;
        director.swarm = swarm;
        director.aim = aim;
        director.timeOfDay = timeOfDay;

        TMP_FontAsset font = DomeAquariumSetup.LoadKoreanFont();
        DomeAquariumUiKit.EnsureAll();
        director.board = BuildBoard(aim, font);
        director.callPanel = BuildCallPanel(director, font);
        director.passes = BuildPasses(director, swarm);
        Transform reefFloor = GameObject.Find("ReefFloor")?.transform;
        director.reefProps = reefFloor != null ? reefFloor.Find("Generated") : null;
        director.reefLights = reefFloor != null ? reefFloor.Find("CoralAccentLights")?.gameObject : null;
        director.rockBed = BuildRockBed(reefFloor != null ? reefFloor.GetComponent<ReefScatter>() : null);
        director.marineSnow = BuildMarineSnow();
        director.deviceLight = BuildDeviceLight(director, aim, font);
        BuildStillness(director);
        director.unknown = BuildUnknown(director, unknownFish, aim);
        director.bioSparks = BuildBioSparks();
        director.layerAmbience = BuildLayerAmbience(director);
        director.night = Object.FindFirstObjectByType<LobbyNight>(FindObjectsInactive.Include);
        director.credits = BuildCredits(director, director.night, font);
        // "처음부터 다시"는 크레딧과 달리 시선을 따라온다 — 손 닿는 곳(0.62m, 눈높이 26° 아래)이라야 누른다.
        // 크레딧을 다 읽은 눈 앞 아래에 뜨고, 데이터 월을 돌아봐도 새 줄(3.2m, 눈높이 3~15° 아래)보다 낮다.
        director.endPanel = BuildFloatingPanel(director.transform, "EndPanel", ObservationTerms.EndTitle, ObservationTerms.Restart,
                                               director.credits.Restart, font);
        director.tutorial = Object.FindFirstObjectByType<LobbyTutorial>(FindObjectsInactive.Include);
        if (director.night == null)
            Debug.LogWarning("[층 하강] 밤 로비(LobbyNight)가 없습니다. '9. 육지 로비 빌드'를 다시 실행하세요 — 엔딩이 로비로 올라와도 낮 그대로입니다.");
        EditorUtility.SetDirty(director);

        // 돔은 1막 층으로 시작한다 — 강돌고래와 200m 종이 섞여 있던 예전 전시 목록을 바꾼다.
        Undo.RecordObject(swarm, "층별 하강 적용");
        swarm.species = reef.species;
        swarm.rotation = reef.rotation;
        swarm.rotationInterval = reef.rotationInterval;
        EditorUtility.SetDirty(swarm);

        // 관찰 일지는 실제로 만날 수 있는 종만 센다 — 어느 층에도 안 나오는 종(강돌고래)이 있으면 일지를 다 채울 수 없다.
        var journal = Object.FindFirstObjectByType<FishEncyclopediaPanel>(FindObjectsInactive.Include);
        if (journal != null)
        {
            Undo.RecordObject(journal, "층별 하강 적용");
            journal.allFish = DomeAquariumEncyclopediaSetup.LoadAllFish();
            EditorUtility.SetDirty(journal);
        }

        if (lobby != null)
        {
            Undo.RecordObject(lobby, "층별 하강 적용");
            lobby.director = director;
            EditorUtility.SetDirty(lobby);
            if (lobby.cabinSet == null)
                Debug.LogWarning("[층 하강] 로비에 엘리베이터 칸 묶음(cabinSet)이 없습니다. '9. 육지 로비 빌드'를 다시 실행하세요 — 깊이 버튼도 그때 생깁니다.");
        }
        else Debug.LogWarning("[층 하강] LobbySequence 가 없습니다. 엘리베이터 없이 돔만 있는 씬에서는 층을 옮길 수 없습니다.");

        var scene = director.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);

        Debug.Log($"[층 하강] 적용 완료 — 24m {reef.species.Length}종 {reef.species.Sum(f => f.schoolCount)}마리 " +
                  $"(교체 후보 {reef.rotation.Length}종, {reef.rotationInterval.x:0}~{reef.rotationInterval.y:0}초마다 한 무리), " +
                  $"관찰 일지 {journal?.allFish?.Length ?? 0}종 " +
                  $"(지목: {string.Join(", ", reef.targets.Select(f => f.DisplayName))}), " +
                  $"200m 큰 생물 {twilight.passers.Length}종이 {twilight.passInterval.x:0}~{twilight.passInterval.y:0}초마다 한 무리씩 " +
                  $"(지목: {twilight.targets[0].DisplayName}), 바닥 {twilight.seabed}, 바다눈 {(twilight.marineSnow ? "켬" : "끔")}. " +
                  $"1,000m: 신호의 주인({unknownFish.DisplayName}) — 조명을 끄고 {director.unknown?.stillNeeded ?? 5f:0}초 가만히.");
        return director;
    }

    // ─────────────────────────────────────────────────────────────
    // 서식 층
    // ─────────────────────────────────────────────────────────────

    static List<FishData> LoadAllFish()
    {
        return AssetDatabase.FindAssets("t:FishData", new[] { FishDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(f => f != null)
            .OrderBy(f => f.bandCenter)
            .ToList();
    }

    static void AssignHabitats(List<FishData> all)
    {
        var twilight = new HashSet<string>(Twilight.Select(t => t.name));
        var fresh = new HashSet<string>(Freshwater);
        foreach (var f in all)
        {
            DiveDepth habitat = twilight.Contains(f.name) ? DiveDepth.Twilight200 : DiveDepth.Reef24;
            bool freshwater = fresh.Contains(f.name);
            if (f.habitat == habitat && f.freshwater == freshwater) continue;
            f.habitat = habitat;
            f.freshwater = freshwater;
            EditorUtility.SetDirty(f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 층 에셋
    // ─────────────────────────────────────────────────────────────

    static DiveMission LoadOrCreate(string file)
    {
        string path = $"{MissionDir}/{file}.asset";
        var m = AssetDatabase.LoadAssetAtPath<DiveMission>(path);
        if (m == null)
        {
            m = ScriptableObject.CreateInstance<DiveMission>();
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    /// <summary>
    /// 층별 하강에서 실제로 만날 수 있는 종 — 늘 헤엄치는 무리 · 교체 무리 · 지나가는 큰 생물 · 지목 생물.
    /// 관찰 일지의 총 종 수는 이것만 센다. 층 데이터가 아직 없으면 null (그때는 강에 사는 종만 뺀다).
    /// </summary>
    internal static HashSet<FishData> MeetableFish()
    {
        if (!AssetDatabase.IsValidFolder(MissionDir)) return null;
        var missions = AssetDatabase.FindAssets("t:DiveMission", new[] { MissionDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<DiveMission>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null)
            .ToList();
        if (missions.Count == 0) return null;

        var meetable = new HashSet<FishData>();
        foreach (var m in missions)
        {
            if (m.species != null) meetable.UnionWith(m.species);
            if (m.rotation != null) meetable.UnionWith(m.rotation);
            if (m.targets != null) meetable.UnionWith(m.targets);
            if (m.passers != null) meetable.UnionWith(m.passers.Select(p => p.fish));
        }
        meetable.Remove(null);
        return meetable;
    }

    static DiveMission BuildReefMission(List<FishData> all, LobbySequence lobby)
    {
        var reef = all.Where(f => f.habitat == DiveDepth.Reef24 && !f.freshwater && f.prefab != null).ToList();
        var byName = reef.ToDictionary(f => f.name);

        // 씬 빌드(4)의 전시 종 선정과 같은 방식: 볼거리를 먼저 넣고, 남은 예산을 수직 층위 순으로 고르게 채운다.
        // 해마류는 지목 생물이라 모두 늘 남는다 — 교체로 들어온 해마를 기록해도 할 일이 안 채워지면 안 된다.
        var picked = new List<FishData>();
        foreach (string n in ReefMustHave)
            if (byName.TryGetValue(n, out var f) && !picked.Contains(f)) picked.Add(f);
        foreach (var f in reef.Where(f => f.name.Contains("Seahorse")))
            if (!picked.Contains(f)) picked.Add(f);
        var stay = new HashSet<FishData>(picked);
        int used = picked.Sum(f => DomeAquariumSceneBuilder.SchoolSizeFor(f.name));
        var rest = reef.Where(f => !picked.Contains(f)).ToList();
        int stride = Mathf.Max(1, rest.Count / 24);
        for (int i = 0; i < rest.Count; i += stride)
        {
            int cost = DomeAquariumSceneBuilder.SchoolSizeFor(rest[i].name);
            if (used + cost > ReefAgentBudget) continue;
            picked.Add(rest[i]);
            used += cost;
        }
        // 교체로 들어오는 종도 같은 무리 크기를 쓴다 — 떠난 무리의 슬롯 수에 맞는 종을 고를 때 본다.
        foreach (var f in reef)
        {
            int count = DomeAquariumSceneBuilder.SchoolSizeFor(f.name);
            if (f.schoolCount != count) { f.schoolCount = count; EditorUtility.SetDirty(f); }
        }

        var m = LoadOrCreate("Mission_24m");
        m.depth = DiveDepth.Reef24;
        m.meters = 24f;
        m.layerName = "산호초";
        m.rideSeconds = lobby != null ? lobby.descentSeconds : 9f;
        m.countSubject = "아무 생물이나";
        m.recordCount = 4;
        m.targets = picked.Where(f => f.name.Contains("Seahorse")).ToArray();
        m.targetClue = "산호 가지를 꼬리로 붙잡고 거의 움직이지 않는 생물";
        m.species = picked.ToArray();
        // 처음부터 헤엄치는 종 중 볼거리가 아닌 것 + 예산에 못 든 종. 150마리 예산 안에서 무리째 바뀐다.
        m.rotation = reef.Where(f => !stay.Contains(f)).ToArray();
        m.rotationInterval = new Vector2(100f, 160f);
        m.useClock = true;                      // 1막만 실제 시계 — 밤에 오면 산호가 빛나는 밤 바다
        m.caustics = true;
        m.seabed = SeabedDressing.CoralReef;
        m.ambience = null;
        m.musicLevel = 1f;
        m.descentLine = lobby != null && !string.IsNullOrWhiteSpace(lobby.descentLine)
            ? lobby.descentLine
            : "햇빛은 수심 200m까지만 닿습니다. 그 아래는, 우리가 거의 모르는 세계입니다.";
        m.arrivalLine = "여기가 수심 24m 산호초예요. 관찰기를 생물에 겨누고 지켜보면 기록돼요. " +
                        "4종을 기록하고, 꼬리로 산호를 붙잡는 생물도 찾아 주세요.";
        m.completeLine = "24m 기록 완료! 이제 200m로 내려갈 수 있어요. 준비되면 엘리베이터를 타세요.";
        EditorUtility.SetDirty(m);
        return m;
    }

    static DiveMission BuildTwilightMission(List<FishData> all)
    {
        var byName = all.ToDictionary(f => f.name);
        var passers = new List<DiveMission.PassingCreature>();
        foreach (var (name, min, max, seconds) in Twilight)
        {
            if (!byName.TryGetValue(name, out var f)) { Debug.LogWarning($"[층 하강] 200m 종을 찾지 못했습니다: {name}"); continue; }
            passers.Add(new DiveMission.PassingCreature { fish = f, group = new Vector2Int(min, max), seconds = seconds, weight = 1f });
        }

        var m = LoadOrCreate("Mission_200m");
        m.depth = DiveDepth.Twilight200;
        m.meters = 200f;
        m.layerName = "박광층 입구";
        m.rideSeconds = 12f;
        m.countSubject = "큰 생물";
        m.recordCount = 2;
        m.targets = byName.TryGetValue("MantaRay", out var manta) ? new[] { manta } : new FishData[0];
        m.targetClue = "가장 큰 그림자";
        m.species = new FishData[0];             // 늘 헤엄치는 무리는 없다 — 바다눈 속에서 기다린다
        m.passers = passers.ToArray();
        m.passInterval = new Vector2(60f, 90f);
        m.firstPassDelay = 14f;                 // 도착 안내(약 10초)가 끝날 즈음 첫 예고 → 20초 안에 첫 무리
        m.seabed = SeabedDressing.RockBed;
        m.marineSnow = true;
        m.ambience = DomeAquariumDepthSounds.Ensure("Twilight");   // 로비 수심 벽과 같은 200m 물소리
        m.ambienceVolume = 0.22f;
        m.musicLevel = 0.7f;
        m.useClock = false;
        m.caustics = false;                     // 물결 무늬는 햇빛이 넉넉한 얕은 바다에서만 생긴다
        m.lighting = TwilightLighting();
        m.descentLine = "200m부터는 햇빛이 거의 닿지 않아요. 여기서는 생물이 가끔씩만 지나가요. 기다리는 것도 관찰이에요.";
        m.arrivalLine = "수심 200m예요. 여기서는 큰 생물이 가끔씩만 지나가요. 관찰기에 '다가와요'가 뜨면 그쪽을 보세요. " +
                        "큰 생물 2종, 그리고 가장 큰 그림자를 찾아 주세요.";
        m.completeLine = "200m 기록 완료! 끝까지 기다려 줬네요. 신호가 더 강해졌어요. 이제 1,000m로 내려갈 수 있어요.";
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>
    /// 200m: 짙은 청색, 위에서 희미한 빛. 24m 의 밤(23시 키)보다 푸르고 산호 발광이 없다.
    /// 안개·물색의 가장 어두운 채널도 13/255(0.051) 위 — 그 아래는 Quest 화면에서 검은 덩어리로 뭉개진다.
    /// </summary>
    static TimeOfDayController.Key TwilightLighting() => new TimeOfDayController.Key
    {
        hour = 0f,
        sunColor = new Color(0.32f, 0.50f, 0.85f), sunIntensity = 0.34f,
        ambientSky = new Color(0.06f, 0.13f, 0.26f),
        ambientEquator = new Color(0.04f, 0.09f, 0.18f),
        ambientGround = new Color(0.02f, 0.04f, 0.09f),
        fogColor = new Color(0.051f, 0.09f, 0.17f), fogDensity = 0.06f,
        waterShallow = new Color(0.06f, 0.15f, 0.29f), waterDeep = new Color(0.051f, 0.075f, 0.15f),
        shaftStrength = 0.18f, coralGlow = 0f,
    };

    // ─────────────────────────────────────────────────────────────
    // 3막 — 1,000m 심해, 신호의 주인
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 신호의 주인 기록용 FishData. 관찰 일지(98종 목록)에는 넣지 않는다 — FishData 폴더가 아니라 Missions 에 둔다.
    /// 설명은 고정 대본이다. Gemini 에 묻지 않는다(정체를 말해 버리면 미확인이 아니다).
    /// </summary>
    static FishData BuildUnknownFish()
    {
        string path = $"{MissionDir}/Unknown_SignalOwner.asset";
        var f = AssetDatabase.LoadAssetAtPath<FishData>(path);
        if (f == null)
        {
            f = ScriptableObject.CreateInstance<FishData>();
            AssetDatabase.CreateAsset(f, path);
        }
        f.speciesId = "unknown-signal-1000";
        f.koreanName = "이름 없는 생물";
        f.englishName = "";
        f.scientificName = "";
        f.description = "관찰기가 관찰 일지와 대조했지만 일치하는 기록이 없어요. 반투명한 몸을 따라 무지갯빛 띠가 흐르고, " +
                        "스스로 푸른빛을 내요. 이 기록이 첫 번째 관측이에요.";
        f.sizeTag = "몸길이 약 1.4m";
        f.habitatTag = "수심 1,000m";
        f.rarity = FishRarity.Legendary;
        f.prefab = null;
        f.bodyLength = 1.4f;
        f.habitat = DiveDepth.Midnight1000;
        f.freshwater = false;
        f.unnamed = true;
        EditorUtility.SetDirty(f);
        return f;
    }

    static DiveMission BuildMidnightMission(FishData unknownFish)
    {
        var m = LoadOrCreate("Mission_1000m");
        m.depth = DiveDepth.Midnight1000;
        m.meters = 1000f;
        m.layerName = "심해";
        m.rideSeconds = 15f;
        m.countSubject = "";
        m.recordCount = 1;
        m.targets = new[] { unknownFish };
        m.targetClue = "";
        m.taskLine = "신호의 정체 확인하기";
        m.species = new FishData[0];
        m.passers = new DiveMission.PassingCreature[0];
        m.seabed = SeabedDressing.Bare;         // 3막은 거의 비운다 — 모래 바닥과 어둠뿐
        m.marineSnow = false;
        m.bioluminescence = true;
        m.encounter = true;
        m.showSignal = true;
        m.lightOnArrival = true;
        m.useClock = false;
        m.caustics = false;
        m.lighting = MidnightLighting();
        m.ambience = DomeAquariumDepthSounds.Ensure("Midnight");  // 아주 낮은 웅웅거림 + 드문 '띵'
        m.ambienceVolume = 0.32f;
        m.musicLevel = 0f;                      // 3막은 음악을 끄고 낮은 물소리만
        m.descentLine = "1,000m까지 내려갑니다. 여기부터는 햇빛이 전혀 닿지 않아요. 신호가 점점 강해지고 있어요.";
        m.arrivalLine = "수심 1,000m예요. 빛이 하나도 없어서 관찰기 조명을 켜 두었어요. 저 멀리 깜빡이는 푸른 빛, 보이나요? " +
                        "3주 동안 잡히던 신호예요. 관찰기를 그쪽으로 겨눠 보세요.";
        m.hintLine = "불을 꺼 봐요. 그리고 움직이지 말고.";
        m.completeLine = "신호의 주인을 처음으로 기록했어요. 이제 올라갈게요.";
        m.finale = true;
        m.ascentLine = "1,000m에서 해수면까지 올라갑니다. 신호는 다시 조용해졌어요.";
        m.finalLine = "바다의 80%는 여전히 아무도 본 적이 없습니다. 하지만 오늘 밤, 그중 한 칸에 당신의 이름이 적혔습니다.";
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>
    /// 1,000m: 거의 완전한 어둠. 햇빛·물결 무늬·빛기둥 없음. 다만 안개·물빛의 가장 어두운 채널도 13/255(0.051) 위 —
    /// Quest 화면에서 그 아래는 검은 덩어리로 번진다(Meta 디스플레이 가이드). 생물 발광만 또렷하게 보인다.
    /// </summary>
    static TimeOfDayController.Key MidnightLighting() => new TimeOfDayController.Key
    {
        hour = 0f,
        sunColor = new Color(0.20f, 0.26f, 0.40f), sunIntensity = 0.02f,
        ambientSky = new Color(0.030f, 0.036f, 0.060f),
        ambientEquator = new Color(0.024f, 0.028f, 0.046f),
        ambientGround = new Color(0.016f, 0.018f, 0.030f),
        fogColor = new Color(0.051f, 0.055f, 0.078f), fogDensity = 0.05f,
        waterShallow = new Color(0.056f, 0.062f, 0.090f), waterDeep = new Color(0.051f, 0.053f, 0.072f),
        shaftStrength = 0f, coralGlow = 0f,
    };

    /// <summary>
    /// 관찰기 조명 — 관찰기가 겨누는 쪽 앞 5.5m 를 비추는 스포트라이트 + 옅은 빛줄기. 1막을 마치면 열린다.
    /// 관찰기 화면 왼쪽에 "조명" 버튼(손으로 찌르기)을 붙인다 — 컨트롤러면 A/X 버튼으로도 된다.
    /// </summary>
    static ObservationLight BuildDeviceLight(DiveDirector director, ObservationAim aim, TMP_FontAsset font)
    {
        var old = Object.FindFirstObjectByType<ObservationLight>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("ObservationLight");
        go.transform.SetParent(director.transform, false);
        var spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 5.5f;
        spot.spotAngle = 50f;
        spot.innerSpotAngle = 28f;
        spot.intensity = 3f;
        spot.color = new Color(0.85f, 0.93f, 1f);
        spot.shadows = LightShadows.None;
        spot.enabled = false;

        // URP 파티클 셰이더는 일반 메시의 꼭짓점 알파를 쓰지 않아 원뿔이 불투명한 회색 통으로 보였다 — 전용 셰이더를 쓴다.
        var beamMat = ShaderMaterial("Mat_ObservationBeam", "DomeAquarium/Light Beam");
        beamMat.SetColor("_Color", new Color(0.85f, 0.92f, 1f));
        beamMat.SetFloat("_Intensity", 1f);
        beamMat.SetFloat("_FacingPower", 1.6f);
        EditorUtility.SetDirty(beamMat);

        var light = go.AddComponent<ObservationLight>();
        light.aim = aim;
        light.spot = spot;
        light.beamMaterial = beamMat;
        light.view = Camera.main != null ? Camera.main.transform : null;

        // 관찰기 화면(560×150, 0.5mm/단위) 오른쪽 옆 버튼 — 7.5 × 5.5cm. 왼쪽 옆에 두었더니 가운데 아래의 엘리베이터 판과 맞닿았다.
        // 관찰기를 든 오른손 위에 떠 있으니, 왼손 검지로 오른쪽 끝을 찌르기도 편하다.
        if (aim.statusRoot != null)
        {
            // VR 템플릿 누르는 버튼(XRPokeFollowAffordanceFill)은 부모 캔버스의 TrackedDeviceGraphicRaycaster 에서 찌르기 상태를 받는다.
            // 관찰기 화면 캔버스에는 레이캐스터가 없어 버튼이 켜지는 순간 NullReferenceException 이 났다. 컨트롤러 레이로도 누를 수 있게 된다.
            var statusCanvas = aim.statusRoot.GetComponent<Canvas>();
            if (statusCanvas != null) statusCanvas.worldCamera = Camera.main;
            if (aim.statusRoot.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>() == null)
                aim.statusRoot.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            var oldButton = aim.statusRoot.Find("LightButton");
            if (oldButton != null) Object.DestroyImmediate(oldButton.gameObject);
            var size = new Vector2(150f, 110f);
            var rect = NewUI("LightButton", aim.statusRoot, size);
            rect.anchoredPosition = new Vector2(280f + 12f + size.x * 0.5f, 0f);
            var parts = DomeAquariumUiKit.PokeButton(rect, size, ObservationTerms.LightOff, null,
                DomeAquariumUiKit.Tone.Secondary, 26f, font, corner: 14f, lift: 10f);
            parts.label.textWrappingMode = TextWrappingModes.Normal;
            var col = rect.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(160f, 120f, 160f);          // 0.08 × 0.06 × 0.08 m
            var rb = rect.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var poke = rect.gameObject.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 0.8f;
            // 관찰기를 든 오른손은 제 화면 버튼을 누르지 않는다 (손목 메뉴와 같은 이유 — 손 콜라이더가 늘 겹쳐 있다).
            poke.ignoreRoots = aim.aimSources != null ? aim.aimSources.Where(t => t != null).ToArray() : new Transform[0];
            UnityEventTools.AddPersistentListener(poke.onPoked, light.Toggle);
            UnityEventTools.AddPersistentListener(parts.button.onClick, light.Toggle);   // 컨트롤러 레이로 누를 때
            light.button = rect.gameObject;
            light.buttonFront = parts.front;
            light.buttonLabel = parts.label;
            rect.gameObject.SetActive(false);
        }
        EditorUtility.SetDirty(light);
        return light;
    }

    /// <summary>머리·양손 움직임으로 "가만히 있음"을 잰다. 컨트롤러와 손 추적 둘 다 넣고, 추적되는 것만 센다.</summary>
    static StillnessDetector BuildStillness(DiveDirector director)
    {
        var old = Object.FindFirstObjectByType<StillnessDetector>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("StillnessDetector");
        go.transform.SetParent(director.transform, false);
        var still = go.AddComponent<StillnessDetector>();
        var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
        Transform offset = origin != null ? origin.transform.Find("Camera Offset") : null;
        still.head = Camera.main != null ? Camera.main.transform : null;
        still.hands = offset == null ? new Transform[0]
            : new[] { "Right Controller", "Left Controller", "Right Hand", "Left Hand" }
                .Select(n => offset.Find(n)).Where(t => t != null).ToArray();
        EditorUtility.SetDirty(still);
        return still;
    }

    static UnknownCreature BuildUnknown(DiveDirector director, FishData fish, ObservationAim aim)
    {
        var old = Object.FindFirstObjectByType<UnknownCreature>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("UnknownCreature");
        go.transform.SetParent(director.transform, false);
        var c = go.AddComponent<UnknownCreature>();
        c.fish = fish;
        c.aim = aim;
        c.view = Camera.main != null ? Camera.main.transform : null;
        c.bodyMaterial = GlowMaterial("Mat_UnknownBody", new Color(0.04f, 0.10f, 0.20f), new Color(0.35f, 0.75f, 1f), 2.0f);
        c.coreMaterial = GlowMaterial("Mat_UnknownCore", new Color(0.20f, 0.10f, 0.40f), new Color(0.55f, 0.40f, 1f), 1.2f);
        c.bandMaterial = ShaderMaterial("Mat_UnknownBands", "DomeAquarium/Glow Bands");
        var halo = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_UnknownHalo", "Universal Render Pipeline/Particles/Unlit");
        halo.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        if (halo.HasProperty("_Cull")) halo.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(halo);
        c.haloMaterial = halo;
        var filament = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_UnknownFilament", "Universal Render Pipeline/Particles/Unlit");
        filament.SetTexture("_BaseMap", null);
        EditorUtility.SetDirty(filament);
        c.filamentMaterial = filament;

        // 신호 탐지 삑 — 생물 쪽 3m 에서 울린다
        var beep = new GameObject("SignalBeep");
        beep.transform.SetParent(go.transform, false);
        var src = beep.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = 14f;
        src.dopplerLevel = 0f;
        src.volume = 0.6f;
        c.beepSource = src;
        c.beepClip = DomeAquariumDepthSounds.EnsureSonarPing();

        go.SetActive(false);
        EditorUtility.SetDirty(c);
        return c;
    }

    static Material GlowMaterial(string name, Color inner, Color rim, float rimPower)
    {
        var mat = ShaderMaterial(name, "DomeAquarium/Glow Jelly");
        mat.SetColor("_Color", inner);
        mat.SetColor("_RimColor", rim);
        mat.SetFloat("_RimPower", rimPower);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material ShaderMaterial(string name, string shaderName)
    {
        string path = $"Assets/DomeAquarium/Materials/{name}.mat";
        var shader = Shader.Find(shaderName);
        if (shader == null) { Debug.LogError($"[층 하강] 셰이더를 찾지 못했습니다: {shaderName}"); return null; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.shader != shader) mat.shader = shader;
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>1,000m 의 발광 알갱이 — 어둠 속에서 푸른 점이 잠깐 켜졌다 꺼진다. 최대 160개.</summary>
    static GameObject BuildBioSparks()
    {
        foreach (var go0 in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go0.name == "BioSparks") Object.DestroyImmediate(go0);

        var go = new GameObject("BioSparks");
        go.transform.position = new Vector3(0f, 1.5f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        main.duration = 6f;
        main.maxParticles = 160;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.55f, 1f, 0.9f), new Color(0.45f, 0.95f, 1f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 40f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(20f, 14f, 20f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.03f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        var twinkle = new Gradient();
        twinkle.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.1f, 0.35f),
                    new GradientAlphaKey(0.8f, 0.55f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = twinkle;
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_BioSpark", "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        EditorUtility.SetDirty(mat);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    static AudioSource BuildLayerAmbience(DiveDirector director)
    {
        var old = director.transform.Find("LayerAmbience");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("LayerAmbience");
        go.transform.SetParent(director.transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 0.25f;
        return src;
    }

    // ─────────────────────────────────────────────────────────────
    // 200m 환경: 큰 생물 통과 · 암반 · 바다눈
    // ─────────────────────────────────────────────────────────────

    static LargeCreaturePasses BuildPasses(DiveDirector director, BoidManager swarm)
    {
        var old = Object.FindFirstObjectByType<LargeCreaturePasses>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("LargeCreaturePasses");
        go.transform.SetParent(director.transform, false);
        var passes = go.AddComponent<LargeCreaturePasses>();
        passes.tankRadius = swarm != null ? swarm.radius : 12f;
        passes.viewer = Camera.main != null ? Camera.main.transform : null;

        // 예고 핑 — 생물이 올 쪽 4m 에서 울린다. 3D 라 고개를 어디로 돌릴지 귀가 먼저 안다.
        var cue = new GameObject("ApproachPing");
        cue.transform.SetParent(go.transform, false);
        var src = cue.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = 16f;
        src.volume = 0.55f;
        src.dopplerLevel = 0f;
        passes.cueSource = src;
        passes.cueClip = DomeAquariumDepthSounds.EnsureSonarPing();
        EditorUtility.SetDirty(passes);
        return passes;
    }

    /// <summary>
    /// 산호 없는 암반 — 1막의 바위는 그대로 두고, 바위를 성기게 더 깐다 (ReefScatter, 바위 프리팹만).
    /// 키트의 바위는 원래 크기가 제각각이라(가장 큰 것은 바닥에서 눈높이 가까이 솟는다) 배율을 실제 높이로 정한다 —
    /// 처음에 1.3~2.4 배로 깔았더니 바위가 눈높이 위로 솟아 시야를 통째로 가렸다.
    /// 가장 큰 바위도 꼭대기가 바닥에서 RockTopMax 를 넘지 않게(눈보다 약 3m 아래), 가운데 4m 는 비운다. 200m 에서만 켠다.
    /// </summary>
    const float RockTopMax = 6.5f;
    static GameObject BuildRockBed(ReefScatter reef)
    {
        foreach (var go0 in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go0.name == "RockBed") Object.DestroyImmediate(go0);
        if (reef == null || reef.rockPrefabs == null || reef.rockPrefabs.Length == 0)
        {
            Debug.LogWarning("[층 하강] ReefFloor 의 바위 프리팹이 없어 암반을 만들지 못했습니다. 200m 는 1막 바위만 남습니다.");
            return null;
        }

        var go = new GameObject("RockBed");
        go.transform.position = reef.transform.position;
        var bed = go.AddComponent<ReefScatter>();
        bed.tankRadius = reef.tankRadius;
        bed.floorY = reef.floorY;
        bed.terraceCount = 3;
        bed.terraceRise = 0f;
        bed.groundEmbedDepth = 0.05f;           // 큰 바위는 조금 더 묻어 바닥에 앉힌다
        bed.centerClearRadius = 4f;
        bed.wallMargin = 0.9f;
        bed.seed = 200;
        bed.coralPrefabs = new GameObject[0];
        bed.rockPrefabs = reef.rockPrefabs;
        bed.seaweedPrefabs = new GameObject[0];
        bed.shellPrefabs = new GameObject[0];
        float tallest = TallestPrefab(reef.rockPrefabs);
        bed.objectsPerSquareMeter = 0.08f;
        bed.scaleMax = Mathf.Clamp(RockTopMax / Mathf.Max(0.1f, tallest), 0.15f, 1.2f);
        bed.scaleMin = bed.scaleMax * 0.4f;
        bed.maxTiltDegrees = 18f;
        Debug.Log($"[층 하강] 암반 바위: 가장 큰 프리팹 높이 {tallest:0.0}m → 배율 {bed.scaleMin:0.00}~{bed.scaleMax:0.00} (꼭대기 ≤ {RockTopMax}m)");
        bed.Generate();
        go.SetActive(false);
        return go;
    }

    /// <summary>프리팹을 잠깐 꺼내 렌더러 높이를 잰다 (배율 1).</summary>
    static float TallestPrefab(GameObject[] prefabs)
    {
        float tallest = 0f;
        foreach (var prefab in prefabs)
        {
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.localScale = Vector3.one;
            if (ReefScatter.TryGetVisualBounds(go, out Bounds b)) tallest = Mathf.Max(tallest, b.size.y);
            Object.DestroyImmediate(go);
        }
        return tallest;
    }

    /// <summary>
    /// 바다눈 — 위에서 천천히 가라앉는 작은 알갱이(생물의 부스러기). 햇빛이 끝나는 200m 의 첫인상.
    /// 플레이어는 돔 한가운데에서만 있으므로 발밑 기준 18×14×18m 상자에서 뿌린다. 미리 채워(prewarm) 도착하자마자 내리고 있다.
    /// Quest 예산: 가산 혼합 점 1,000개 이하, 그림자·충돌 없음.
    /// </summary>
    static GameObject BuildMarineSnow()
    {
        foreach (var go0 in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go0.name == "MarineSnow") Object.DestroyImmediate(go0);

        var go = new GameObject("MarineSnow");
        go.transform.position = new Vector3(0f, 1.5f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        main.duration = 10f;
        main.maxParticles = 1000;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        main.startSpeed = 0f;
        // 0.012~0.034m 는 헤드셋 픽셀 몇 개라 거의 안 보였다. 2~5cm, 더 밝게.
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.74f, 0.86f, 0.75f), new Color(0.88f, 0.92f, 0.96f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 55f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(18f, 14f, 18f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.12f, -0.04f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_MarineSnow", "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        EditorUtility.SetDirty(mat);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 0.08f;

        go.SetActive(false);
        return go;
    }

    // ─────────────────────────────────────────────────────────────
    // 관찰기 위 할 일 판
    // ─────────────────────────────────────────────────────────────

    static DiveMissionBoard BuildBoard(ObservationAim aim, TMP_FontAsset font)
    {
        Transform status = aim.statusRoot;
        if (status == null) { Debug.LogWarning("[층 하강] 관찰기 상태 화면이 없어 할 일 판을 붙이지 못했습니다. '17. 관찰기 적용'을 다시 실행하세요."); return null; }
        var old = status.Find("MissionBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 상태 화면(560×150, 0.5mm/단위) 바로 위. 손 위에 같이 떠서 관찰기를 볼 때 함께 읽힌다.
        var rt = NewUI("MissionBoard", status, new Vector2(560f, 196f));
        rt.anchoredPosition = new Vector2(0f, 75f + 12f + 98f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = DomeAquariumUiKit.Panel;
        bg.type = bg.sprite != null && bg.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        bg.color = Deep;
        bg.raycastTarget = false;

        var board = rt.gameObject.AddComponent<DiveMissionBoard>();
        board.root = rt.gameObject;
        board.openMark = AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
        board.doneMark = DomeAquariumUiKit.Dot;

        board.title = Text("Title", rt, new Vector2(-260f, 64f), new Vector2(520f, 40f), 26f, font, TextAlignmentOptions.Left);
        board.title.fontStyle = FontStyles.Bold;

        board.countMark = Mark("CountMark", rt, new Vector2(-250f, 20f));
        board.countLine = Text("CountLine", rt, new Vector2(-228f, 20f), new Vector2(380f, 40f), 26f, font, TextAlignmentOptions.Left);
        board.countTally = Text("CountTally", rt, new Vector2(160f, 20f), new Vector2(100f, 40f), 26f, font, TextAlignmentOptions.Right);
        board.countTally.fontStyle = FontStyles.Bold;

        var targetRow = NewUI("TargetRow", rt, new Vector2(560f, 80f));
        targetRow.anchoredPosition = new Vector2(0f, -46f);
        board.targetRow = targetRow.gameObject;
        board.targetMark = Mark("TargetMark", targetRow, new Vector2(-250f, 16f));
        board.targetLine = Text("TargetLine", targetRow, new Vector2(-228f, 0f), new Vector2(488f, 76f), 24f, font, TextAlignmentOptions.TopLeft);
        board.targetLine.textWrappingMode = TextWrappingModes.Normal;
        board.targetLine.lineSpacing = -8f;

        // 3막: 둘째 줄 자리에 신호 세기 막대 ("신호 ▬▬▬▭▭"). 관찰기를 신호 쪽으로 겨눌수록 찬다.
        var signalRow = NewUI("SignalRow", rt, new Vector2(560f, 60f));
        signalRow.anchoredPosition = new Vector2(0f, -36f);
        var signalLabel = Text("SignalLabel", signalRow, new Vector2(-262f, 0f), new Vector2(80f, 40f), 24f, font, TextAlignmentOptions.Left);
        signalLabel.text = ObservationTerms.SignalLabel;
        signalLabel.color = new Color(0.62f, 0.76f, 0.82f, 1f);
        var track = NewUI("Track", signalRow, new Vector2(380f, 18f));
        track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = new Vector2(-170f, 0f);
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.06f);       // 빈 막대가 이미 찬 것처럼 보이지 않게 옅게
        trackImg.raycastTarget = false;
        var fill = NewUI("Fill", track, new Vector2(0f, 18f));
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color(0.45f, 0.85f, 1f, 1f);
        fillImg.raycastTarget = false;
        board.signalRow = signalRow.gameObject;
        board.signalFill = fill;
        board.signalWidth = 380f;
        signalRow.gameObject.SetActive(false);

        rt.gameObject.SetActive(false);
        EditorUtility.SetDirty(board);
        return board;
    }

    // ─────────────────────────────────────────────────────────────
    // 돔의 "엘리베이터 타기" 판
    // ─────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────
    // 엔딩 크레딧
    // ─────────────────────────────────────────────────────────────

    // CC-BY 세 줄은 반드시 들어가야 한다 (Assets/ThirdParty/CREDITS.md). 이름(만든 사람)은 씬의 CreditsRoll.body 에서 고친다.
    const string CreditsBody =
        "<size=72><b>STILL</b></size>\n" +
        "<size=30><color=#8FE6FF>해양 관측소 STILL · 1,000m 의 신호</color></size>\n\n\n" +
        "<size=26><color=#8FB0C0>만든 사람</color></size>\nSTILL 제작팀\n\n" +
        "<size=26><color=#8FB0C0>관측소장의 목소리 · 생물 설명</color></size>\nGoogle Gemini\n(일지에 없는 생물은 고정 대본)\n\n" +
        "<size=26><color=#8FB0C0>바다 생물 · 산호 모델</color></size>\nLayer Lab · 3D Characters - Fish (Unity Asset Store)\n\n" +
        "<size=26><color=#8FB0C0>로비</color></size>\n" +
        "\"Elevator - Low Poly Animated\" by JeffK, CC BY 4.0\n" +
        "\"Animated Ticket Gate\" by €r!c, CC BY 4.0\n" +
        "\"Bathysphere | New York Aquarium\" by sabster, CC BY 4.0\n" +
        "<size=24>(modified: reduced polygons and texture size)</size>\n" +
        "Furniture Kit · UI Pack · Kenney (CC0)\n" +
        "Secluded Beach · Poly Haven (CC0)\n" +
        "3D Free Modular Kit · Barking Dog (Unity Asset Store)\n\n" +
        "<size=26><color=#8FB0C0>소리</color></size>\n" +
        "\"Exploration Theme\" · Cleyton Kauffman (CC0)\n" +
        "바닷가 갈매기 · selcukartut (Pixabay)\n" +
        "물속 하강 · DRAGON-STUDIO (Pixabay)\n" +
        "물소리 · 물보라 · 소나 · 종소리 · 코드로 만든 소리\n\n" +
        "<size=26><color=#8FB0C0>글꼴 · 아이콘</color></size>\n" +
        "나눔스퀘어라운드 · NAVER (SIL OFL 1.1)\n" +
        "Material Icons · Google (Apache 2.0)\n\n" +
        "<size=26><color=#8FB0C0>바다 숫자</color></size>\n71% · 80% · NOAA\n\n\n" +
        "<size=40><b>오늘 밤, 관측해 줘서 고마워요.</b></size>";

    /// <summary>
    /// 밤 로비에 뜨는 크레딧 판 — 1.03 × 0.91m, 1.9m 앞, 데이터 월 새 줄(빛줄)의 왼쪽 끝보다 더 왼쪽. 글은 창(RectMask2D) 안에서 아래에서 위로 올라간다.
    /// 본문 30(약 3.6cm, 1.9m 에서 1.09°) — 10~15세 기준 1° 이상.
    /// </summary>
    static CreditsRoll BuildCredits(DiveDirector director, LobbyNight night, TMP_FontAsset font)
    {
        var old = director.transform.Find("Credits");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("Credits", typeof(RectTransform));
        go.transform.SetParent(director.transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(860f, 760f);
        rt.localScale = Vector3.one * 0.0012f;
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var plate = NewUI("Plate", go.transform, new Vector2(860f, 760f));
        // 뒤로 밤 로비(열린 엘리베이터·네온)가 비치면 글이 묻힌다 — 0.95 로는 데이터 월 글자와 빛줄이 비쳤다. 불투명하게
        DomeAquariumUiKit.TemplatePlate(plate, 28f, new Color(0.03f, 0.06f, 0.09f, 1f), DomeAquariumUiKit.PlateLine);

        var viewport = NewUI("Viewport", go.transform, new Vector2(800f, 700f));
        viewport.gameObject.AddComponent<RectMask2D>();

        var text = NewUI("Text", viewport, new Vector2(770f, 2000f)).gameObject.AddComponent<TextMeshProUGUI>();
        var trt = text.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = Vector2.zero;
        if (font != null) text.font = font;
        text.fontSize = 30f;
        text.lineSpacing = 6f;
        text.alignment = TextAlignmentOptions.Top;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        text.color = new Color(0.92f, 0.95f, 0.98f);
        text.raycastTarget = false;
        text.text = CreditsBody;

        var roll = go.AddComponent<CreditsRoll>();
        roll.group = group;
        roll.viewport = viewport;
        roll.content = trt;
        roll.text = text;
        roll.body = CreditsBody;
        roll.view = Camera.main != null ? Camera.main.transform : null;
        Transform record = night != null && night.record != null ? night.record.transform : null;
        var rule = record != null ? record.Find("Rule") as RectTransform : null;
        roll.keepClear = rule != null ? rule : record as RectTransform;
        if (roll.keepClear == null)
            Debug.LogWarning("[층 하강] 데이터 월 새 줄(NewRecordLine)이 없어 크레딧이 보던 방향 왼쪽에 뜹니다. '9. 육지 로비 빌드'를 다시 실행하세요.");
        EditorUtility.SetDirty(roll);
        return roll;
    }

    static ElevatorCallPanel BuildCallPanel(DiveDirector director, TMP_FontAsset font) =>
        BuildFloatingPanel(director.transform, "ElevatorCallPanel", ObservationTerms.LayerDone("24m"), ObservationTerms.RideElevator,
                           director.CallElevator, font);

    /// <summary>
    /// 눈앞 아래쪽에 떠서 손 닿는 곳에 머무는 판 — 제목 한 줄 + 누르는 버튼 하나 (ElevatorCallPanel).
    /// 찌르기(PokeSelector)와 컨트롤러 레이(Button) 둘 다 action 을 부른다 — action 은 두 번 불려도 안전해야 한다.
    /// </summary>
    static ElevatorCallPanel BuildFloatingPanel(Transform parent, string name, string titleText, string buttonText,
                                                UnityEngine.Events.UnityAction action, TMP_FontAsset font)
    {
        var old = parent.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;         // 비워 두면 레이캐스터 등록·해제 때 카메라가 달라져 예외가 난다
        go.AddComponent<CanvasScaler>();
        go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(460f, 250f);
        rt.localScale = Vector3.one * 0.0007f;    // 약 32 × 18cm — 0.62m 앞에서 시야 29° 폭. 글자는 2.1cm(약 2°)

        var plate = NewUI("Plate", go.transform, new Vector2(460f, 250f));
        DomeAquariumUiKit.TemplatePlate(plate, 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var title = Text("Title", go.transform, new Vector2(-210f, 72f), new Vector2(420f, 50f), 30f, font, TextAlignmentOptions.Center);
        title.color = new Color(0.55f, 0.95f, 0.70f);
        title.fontStyle = FontStyles.Bold;
        title.text = titleText;

        var buttonSize = new Vector2(340f, 100f);
        var button = NewUI("Button", go.transform, buttonSize);
        button.anchoredPosition = new Vector2(0f, -36f);
        var parts = DomeAquariumUiKit.PokeButton(button, buttonSize, buttonText, null,
            DomeAquariumUiKit.Tone.Primary, 34f, font, corner: 18f, lift: 12f);
        UnityEventTools.AddPersistentListener(parts.button.onClick, action);

        var col = button.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(360f, 130f, 260f);
        var rb = button.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var poke = button.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = "Hand";
        poke.inputCooldown = 1.5f;
        UnityEventTools.AddPersistentListener(poke.onPoked, action);

        var panel = go.AddComponent<ElevatorCallPanel>();
        panel.group = group;
        panel.title = title;
        panel.side = -0.08f;
        var aimForAvoid = Object.FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        panel.avoid = aimForAvoid != null ? aimForAvoid.statusRoot : null;
        EditorUtility.SetDirty(panel);
        return panel;
    }

    // ─────────────────────────────────────────────────────────────
    // 헤드셋 없이 층 흐름을 끝까지 돌려 보는 도구
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 지금 층 할 일 채우기", priority = 216)]
    public static void DebugFill()
    {
        var d = FindDirector();
        if (d == null) return;
        d.DebugFillLayer();
        Debug.Log("[층 하강] " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 엘리베이터 타기 누르기", priority = 217)]
    public static void DebugCall()
    {
        var d = FindDirector();
        if (d == null) return;
        var panel = d.callPanel;
        var button = panel != null ? panel.GetComponentInChildren<Button>(true) : null;
        if (button != null) button.onClick.Invoke();   // 실제 버튼의 연결까지 같이 확인한다
        else d.CallElevator();
        Debug.Log("[층 하강] " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 갈 차례 깊이 버튼 누르기", priority = 218)]
    public static void DebugPressNext()
    {
        var d = FindDirector();
        if (d == null) return;
        int next = d.SuggestedIndex;
        if (next < 0) { Debug.Log("[층 하강] 갈 차례인 층이 없습니다 — " + d.DebugState()); return; }
        var console = Object.FindFirstObjectByType<ElevatorConsole>();
        if (console != null && console.slots != null && next < console.slots.Length && console.slots[next].button != null)
            console.slots[next].button.onClick.Invoke();
        else d.Select(next);
        Debug.Log("[층 하강] " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 큰 생물 바로 부르기", priority = 221)]
    public static void DebugPassNow()
    {
        var d = FindDirector();
        if (d == null || d.passes == null) return;
        d.passes.DebugNow();
        Debug.Log("[층 하강] " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 24m 버튼 누르기 (돌아가기)", priority = 219)]
    public static void DebugPressReef()
    {
        var d = FindDirector();
        if (d == null) return;
        var console = Object.FindFirstObjectByType<ElevatorConsole>();
        if (console != null && console.slots != null && console.slots.Length > 0 && console.slots[0].button != null)
            console.slots[0].button.onClick.Invoke();
        else d.Select(0);
        Debug.Log("[층 하강] " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 3막: 조명 켜기·끄기", priority = 222)]
    public static void DebugToggleLight()
    {
        var d = FindDirector();
        if (d == null || d.deviceLight == null) return;
        d.deviceLight.Unlock();
        d.deviceLight.SetOn(!d.deviceLight.On);
        Debug.Log("[층 하강] 조명 " + (d.deviceLight.On ? "켬" : "끔") + " | " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 3막: 조명을 켜서 생물에게 비추기", priority = 224)]
    public static void DebugShineAtCreature()
    {
        var d = FindDirector();
        if (d == null || d.deviceLight == null || d.unknown == null) return;
        d.deviceLight.Unlock();
        d.deviceLight.debugAimAt = d.unknown.transform;
        d.deviceLight.SetOn(true);
        Debug.Log("[층 하강] 조명을 켜서 생물에게 비춘다 | " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 엔딩: 크레딧 끝으로 넘기기", priority = 225)]
    public static void DebugSkipCredits()
    {
        var d = FindDirector();
        if (d == null || d.credits == null) return;
        d.credits.DebugSkip();
        Debug.Log("[층 하강] 크레딧을 끝으로 넘긴다 | " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 엔딩: 처음부터 다시 누르기", priority = 226)]
    public static void DebugRestart()
    {
        var d = FindDirector();
        var button = d != null && d.endPanel != null ? d.endPanel.GetComponentInChildren<Button>(true) : null;
        if (button == null) return;
        Debug.Log("[층 하강] 처음부터 다시 — 씬을 다시 연다");
        button.onClick.Invoke();                       // 실제 버튼 연결까지 확인한다
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 3막: 2초 동안 움직이기", priority = 223)]
    public static void DebugMove()
    {
        var d = FindDirector();
        var still = Object.FindFirstObjectByType<StillnessDetector>();
        if (d == null || still == null) return;
        still.debugMoveUntil = Time.time + 2f;
        Debug.Log("[층 하강] 2초 동안 움직인 것으로 친다 | " + d.DebugState());
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 층: 진행 상태 출력", priority = 220)]
    public static void DebugLog()
    {
        var d = FindDirector();
        if (d != null) Debug.Log("[층 하강] " + d.DebugState());
    }

    static DiveDirector FindDirector()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[층 하강] 플레이 모드에서만 쓸 수 있다."); return null; }
        var d = DiveDirector.Instance;
        if (d == null) Debug.LogWarning("[층 하강] 씬에 DiveDirector 가 없다. '18. 층별 하강 적용'을 먼저 실행하세요.");
        return d;
    }

    // ─────────────────────────────────────────────────────────────
    // UI 헬퍼
    // ─────────────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>왼쪽 가장자리가 leftCenter.x 에 오는 글상자 (가운데 정렬이면 leftCenter 가 곧 중심 — 그때는 x 에 -폭/2 를 넘긴다).</summary>
    static TMP_Text Text(string name, Transform parent, Vector2 leftCenter, Vector2 size, float fontSize, TMP_FontAsset font,
                         TextAlignmentOptions align)
    {
        var rt = NewUI(name, parent, size);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = leftCenter;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        t.color = Color.white;
        return t;
    }

    static Image Mark(string name, Transform parent, Vector2 center)
    {
        var rt = NewUI(name, parent, new Vector2(26f, 26f));
        rt.anchoredPosition = center;
        var img = rt.gameObject.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }
}
#endif
