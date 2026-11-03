#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 엔딩의 밤 로비 갈림길 — "오늘 밤의 이야기" 판(NightStoryHub)과 이야기 장면들.
///   판: 크레딧이 끝나면 "처음부터 다시" 대신 뜬다. 장면마다 버튼 하나 + 처음부터 다시 (3줄 × 2칸).
///   ① 등대의 신호(LighthouseSignalStory): 곶 끝 등대 꼭대기 난간 · 등명기 손잡이 · 등대지기 일지 · 바다 위 푸른 빛.
///   ② 바다거북의 밤(TurtleNightStory): 밤 해변 둥지 · 놀란 · 관찰기 조명을 끄면 새끼 거북들이 바다로.
///   ③ 선장의 밤바다(CaptainNightStory): 만 한가운데 연구선 갑판 · 엔진과 갑판 불 끄기 · 빛나는 물 · 향유고래 · 신호의 주인.
///   ④ 비브의 바시스피어(BathysphereStory): 1934년 923m — 쇠공 안 · 창 하나 · 수면과의 전화 · 빛나는 줄무늬 물고기.
///   ⑤ 다음 신호 2,500m(NextSignalStory): 잠수정 · 로봇 카메라 화면 · 열수구의 검은 굴뚝 · 관벌레 · 열수구 게 → 또 다른 신호.
/// 장면이 늘어나면 Build 에 더한다. 순서: 9(로비) 안에서 돈다 (19 해안 · 18 층 하강 뒤). 따로 다시 세울 때는 31.
/// </summary>
public static class DomeAquariumNightStories
{
    const float GalleryTop = 16.8f;                                     // 등대 로컬 — 받침 1.5 + 탑 4·4·4·3 + 난간 바닥 0.3 (CoastBuilder)
    const float GalleryRadius = 2.6f, LanternRadius = 1.4f;

    [MenuItem("DomeAquarium/31. 밤 로비 이야기 판 · 이야기 장면들", priority = 31)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[NightStory] 플레이 모드에서는 세울 수 없다."); return; }
        var seq = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (seq == null) { Debug.LogError("[NightStory] LobbySequence 가 없다. 먼저 9번 메뉴."); return; }
        Build(seq);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(LobbySequence seq)
    {
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director == null) { Debug.LogWarning("[NightStory] DiveDirector 가 없다 (18번 메뉴)."); return; }
        var stories = new List<NightStory>();
        var lighthouse = BuildLighthouseStory(seq);
        if (lighthouse != null) stories.Add(lighthouse);
        var turtles = BuildTurtleStory(seq);
        if (turtles != null) stories.Add(turtles);
        var captain = BuildCaptainStory(seq);
        if (captain != null) stories.Add(captain);
        var bathysphere = BuildBathysphereStory(seq);
        if (bathysphere != null) stories.Add(bathysphere);
        var next = BuildNextSignalStory(seq);
        if (next != null) stories.Add(next);
        var hub = BuildHub(director, seq, stories.ToArray());
        director.hub = hub;
        EditorUtility.SetDirty(director);
        Debug.Log($"[NightStory] 이야기 판 — {string.Join(", ", stories.Select(s => s.title))} + 처음부터 다시");
    }

    // ─────────────────────────────────────────────────────────────
    // 이야기 판
    // ─────────────────────────────────────────────────────────────

    static NightStoryHub BuildHub(DiveDirector director, LobbySequence seq, NightStory[] stories)
    {
        var old = director.transform.Find("NightStoryHub");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var font = DomeAquariumSetup.LoadKoreanFont();

        var go = new GameObject("NightStoryHub", typeof(RectTransform));
        go.transform.SetParent(director.transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var rt = (RectTransform)go.transform;
        var size = new Vector2(500f, 400f);
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * 0.0007f;                          // 35 × 28cm — 0.62m 앞, 손 닿는 곳
        var plate = NewUI("Plate", go.transform, size);
        DomeAquariumUiKit.TemplatePlate(plate, 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var title = NewUI("Title", go.transform, new Vector2(460f, 50f)).gameObject.AddComponent<TextMeshProUGUI>();
        ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 162f);
        title.font = font;
        title.fontSize = 30f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.55f, 0.85f, 1f);
        title.raycastTarget = false;

        var hub = go.AddComponent<NightStoryHub>();
        UnityEngine.Events.UnityAction[] actions = { hub.Press0, hub.Press1, hub.Press2, hub.Press3, hub.Press4, hub.Press5 };
        var slots = new GameObject[6];
        var labels = new TMP_Text[6];
        var buttonSize = new Vector2(224f, 96f);
        for (int i = 0; i < 6; i++)
        {
            var button = NewUI($"Slot{i}", go.transform, buttonSize);
            button.anchoredPosition = new Vector2(i % 2 == 0 ? -118f : 118f, 72f - (i / 2) * 112f);
            bool restart = i == stories.Length;
            var parts = DomeAquariumUiKit.PokeButton(button, buttonSize, restart ? "처음부터 다시" : "이야기", null,
                restart ? DomeAquariumUiKit.Tone.Secondary : DomeAquariumUiKit.Tone.Primary, 26f, font, corner: 18f, lift: 12f);
            parts.label.enableAutoSizing = true;
            parts.label.fontSizeMin = 16f;
            parts.label.fontSizeMax = 26f;
            parts.label.textWrappingMode = TextWrappingModes.Normal;
            UnityEventTools.AddPersistentListener(parts.button.onClick, actions[i]);
            var col = button.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(buttonSize.x + 12f, buttonSize.y + 12f, 240f);
            var rb = button.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var poke = button.gameObject.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 1f;
            UnityEventTools.AddPersistentListener(poke.onPoked, actions[i]);
            slots[i] = button.gameObject;
            labels[i] = parts.label;
        }

        var panel = go.AddComponent<ElevatorCallPanel>();
        panel.group = group;
        panel.title = title;
        panel.side = 0f;
        panel.drop = 0.28f;
        var aim = Object.FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        panel.avoid = aim != null ? aim.statusRoot : null;
        DomeAquariumOverlayUi.Apply(go);

        hub.lobby = seq;
        hub.tutorial = seq.tutorial;
        hub.credits = director.credits;
        hub.panel = panel;
        hub.stories = stories;
        hub.slots = slots;
        hub.labels = labels;
        hub.plate = plate;
        hub.titleRect = (RectTransform)title.transform;
        EditorUtility.SetDirty(hub);
        return hub;
    }

    // ─────────────────────────────────────────────────────────────
    // ① 등대의 신호
    // ─────────────────────────────────────────────────────────────

    static LighthouseSignalStory BuildLighthouseStory(LobbySequence seq)
    {
        var lighthouse = Object.FindFirstObjectByType<Lighthouse>(FindObjectsInactive.Include);
        var whale = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        if (lighthouse == null || whale == null) { Debug.LogWarning("[NightStory] 등대 · 만(19번 메뉴)이 없어 등대 이야기를 건너뛴다."); return null; }
        Transform root = lighthouse.transform;
        foreach (var name in new[] { "SignalStory", "GalleryRail" })
        {
            var old = root.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // 만 한가운데 쪽 — 고래가 올라오는 곳. 바다 높이는 고래가 숨 쉬는 높이.
        Transform bay = whale.transform.parent;
        Vector3 bayCenter = bay.TransformPoint(new Vector3(whale.area.center.x, whale.seaY, whale.area.center.y));
        float seaY = bayCenter.y;
        Vector3 seaDir = bayCenter - root.position;
        seaDir.y = 0f;
        seaDir.Normalize();
        Vector3 floor = root.position + Vector3.up * GalleryTop;
        // 대답하는 빛 — 만 쪽 115m. 서는 자리는 난간을 따라 왼쪽으로 65° 돌아간 곳에서 그 빛을 본다:
        // 등명기(탑 가운데)에서 바다 빛으로 가는 빛줄기를 옆에서 보게 된다 (정면 뒤에서 보면 원뿔이 원판처럼 뭉개졌다).
        Vector3 seaPoint = new Vector3(root.position.x, seaY + 0.8f, root.position.z) + seaDir * 115f;
        Vector3 standAt = floor + Quaternion.Euler(0f, -65f, 0f) * seaDir * ((GalleryRadius + LanternRadius) * 0.5f + 0.05f);
        Vector3 dir = new Vector3(seaPoint.x - standAt.x, 0f, seaPoint.z - standAt.z).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir);

        var iron = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseIron", new Color(0.12f, 0.13f, 0.14f), 0.45f, 0.5f, false);
        var brass = DomeAquariumLobbyBuilder.LitMaterial("Mat_LighthouseBrass", new Color(0.72f, 0.56f, 0.24f), 0.7f, 0.8f, false);
        var knobMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_LighthouseKnob", new Color(0.70f, 0.10f, 0.08f), 0.5f, 0.1f, false);
        var paper = DomeAquariumLobbyBuilder.LitMaterial("Mat_LighthouseLogbook", new Color(0.85f, 0.80f, 0.68f), 0.1f, 0f, false);

        // 난간 — 등대 꼭대기 바깥을 한 바퀴 (낮에도 보인다)
        var rail = new GameObject("GalleryRail").transform;
        rail.SetParent(root, false);
        const int posts = 24;
        float railR = GalleryRadius - 0.1f;
        for (int i = 0; i < posts; i++)
        {
            float a = i * Mathf.PI * 2f / posts, b = (i + 1) * Mathf.PI * 2f / posts;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * railR, q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * railR;
            Prim(PrimitiveType.Cylinder, "Post", rail, new Vector3(p.x, GalleryTop + 0.53f, p.z), Quaternion.identity, new Vector3(0.05f, 0.53f, 0.05f), iron);
            foreach (float h in new[] { 1.06f, 0.56f })
            {
                Vector3 mid = (p + q) * 0.5f;
                Prim(PrimitiveType.Cube, "Rail", rail, new Vector3(mid.x, GalleryTop + h, mid.z),
                     Quaternion.LookRotation(q - p, Vector3.up), new Vector3(0.045f, 0.045f, (q - p).magnitude + 0.02f), iron);
            }
        }

        // 장면 — 서는 자리 · 손잡이 · 일지 · 신호 빛줄기 · 바다 빛
        var storyGo = new GameObject("SignalStory");
        storyGo.transform.SetParent(root, false);
        var story = storyGo.AddComponent<LighthouseSignalStory>();
        story.title = "등대의 신호";
        story.lighthouse = lighthouse;

        var spot = new GameObject("Spot").transform;
        spot.SetParent(storyGo.transform, false);
        spot.SetPositionAndRotation(standAt, Quaternion.LookRotation(dir, Vector3.up));
        story.spot = spot;

        // 손잡이 — 오른손 앞, 허리 높이
        var console = new GameObject("LampLever").transform;
        console.SetParent(storyGo.transform, false);
        console.SetPositionAndRotation(spot.position + dir * 0.42f + right * 0.3f, Quaternion.LookRotation(dir, Vector3.up));
        Prim(PrimitiveType.Cube, "Stand", console, new Vector3(0f, 0.41f, 0f), Quaternion.identity, new Vector3(0.08f, 0.82f, 0.08f), iron);
        Prim(PrimitiveType.Cube, "Box", console, new Vector3(0f, 0.86f, 0f), Quaternion.identity, new Vector3(0.2f, 0.1f, 0.16f), brass);
        var arm = new GameObject("Arm").transform;
        arm.SetParent(console, false);
        arm.localPosition = new Vector3(0f, 0.91f, 0f);
        arm.localRotation = Quaternion.Euler(-30f, 0f, 0f);             // 손잡이가 사람 쪽으로 비스듬히 선다
        Prim(PrimitiveType.Cylinder, "Rod", arm, new Vector3(0f, 0.11f, 0f), Quaternion.identity, new Vector3(0.025f, 0.11f, 0.025f), brass);
        Prim(PrimitiveType.Sphere, "Knob", arm, new Vector3(0f, 0.23f, 0f), Quaternion.identity, Vector3.one * 0.065f, knobMat);
        var pokeVolume = console.gameObject.AddComponent<BoxCollider>();
        pokeVolume.isTrigger = true;
        pokeVolume.center = new Vector3(0f, 1.01f, 0f);
        pokeVolume.size = new Vector3(0.3f, 0.36f, 0.3f);
        var rayVolume = new GameObject("RayVolume").AddComponent<BoxCollider>();
        rayVolume.transform.SetParent(console, false);
        rayVolume.center = new Vector3(0f, 0.99f, 0f);
        rayVolume.size = new Vector3(0.22f, 0.3f, 0.2f);
        story.leverRay = console.gameObject.AddComponent<XRSimpleInteractable>();
        var poke = console.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = "Hand";
        poke.inputCooldown = 0.25f;
        story.leverPoke = poke;
        story.leverArm = arm;
        DomeAquariumLobbyBuilder.Label("LeverSign", console, new Vector3(0f, 0.72f, -0.05f), 0f, new Vector2(0.3f, 0.08f),
            "등명기 손잡이\n<size=70%>눌러서 빛 보내기</size>", 0.03f, new Color(0.95f, 0.9f, 0.75f), TextAlignmentOptions.Center);

        // 등대지기 일지 — 왼쪽 앞 낮은 받침 위에 비스듬히 펼쳐 둔다. 눈높이에 세웠더니 바다 위 대답하는 빛을 가렸다.
        var log = new GameObject("Logbook").transform;
        log.SetParent(storyGo.transform, false);
        log.SetPositionAndRotation(spot.position + dir * 0.35f - right * 0.2f, Quaternion.LookRotation(dir, Vector3.up));
        Prim(PrimitiveType.Cube, "Stand", log, new Vector3(0f, 0.42f, 0f), Quaternion.identity, new Vector3(0.06f, 0.84f, 0.06f), iron);
        Prim(PrimitiveType.Cube, "Book", log, new Vector3(0f, 0.86f, 0f), Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.36f, 0.025f, 0.26f), paper);
        var logText = DomeAquariumLobbyBuilder.Label("LogText", log, new Vector3(0f, 0.885f, -0.01f), 0f, new Vector2(0.34f, 0.22f),
            "<b>등대지기 일지</b>\n1987년 10월 3일\n밤 11시 40분, 바다 쪽에 푸른 빛.\n등을 세 번 깜빡였더니,\n바다가 세 번 대답했다.",
            0.022f, new Color(0.18f, 0.14f, 0.10f), TextAlignmentOptions.Center);
        logText.transform.parent.localRotation = Quaternion.Euler(50f, 0f, 0f);   // 책장 위에 누워 위(눈) 쪽을 본다

        // 신호 빛줄기 — 등명기 유리 바로 바깥에서 바다 빛 쪽으로 곧게
        var beamMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/DomeAquarium/Coast/Meshes/LighthouseBeam.asset");
        var beamMat = FindMaterial("Mat_CoastLighthouseBeam");
        Vector3 lamp = lighthouse.beamPivot != null ? lighthouse.beamPivot.position : root.position + Vector3.up * (GalleryTop + 1.1f);
        Vector3 toSea = seaPoint - lamp;
        Vector3 flat = new Vector3(toSea.x, 0f, toSea.z).normalized;
        Vector3 beamStart = lamp + flat * (LanternRadius + 0.2f);
        var beam = new GameObject("SignalBeam", typeof(MeshFilter), typeof(MeshRenderer));
        beam.transform.SetParent(storyGo.transform, false);
        beam.transform.SetPositionAndRotation(beamStart, Quaternion.LookRotation(seaPoint - beamStart, Vector3.up));
        beam.transform.localScale = new Vector3(0.4f, 0.4f, 1f);           // 도는 빛줄기보다 가늘게 — 2m 옆에서 보면 굵은 원뿔이 벽처럼 보였다
        beam.GetComponent<MeshFilter>().sharedMesh = beamMesh;
        var br = beam.GetComponent<MeshRenderer>();
        br.sharedMaterial = beamMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        br.receiveShadows = false;
        br.enabled = false;
        story.signalBeam = br;

        // 바다 위 푸른 빛 — 대답하는 하나 + 끝에 켜지는 무리
        var halo = FindMaterial("Mat_UnknownHalo");
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        story.seaLight = Glow("SeaLight", storyGo.transform, seaPoint, 34f, halo, quad);       // 115m 앞 — 작으면 점 하나로 보였다
        story.seaLightSize = 34f;
        // 인사를 주고받을 때마다 다가오는 자리 — 115m → 70m → 45m. 마지막엔 등대 앞 40m 물 위로 떠오른다(난간 너머로 내려다보인다)
        var approach = new List<Transform>();
        foreach (float d in new[] { 115f, 70f, 45f })
        {
            var a = new GameObject($"Approach_{d:0}m").transform;
            a.SetParent(storyGo.transform, false);
            a.position = new Vector3(root.position.x, seaY + 0.8f, root.position.z) + seaDir * d;
            approach.Add(a);
        }
        story.approach = approach.ToArray();

        // 마지막 — 등대 아래 바닷가. 바다 쪽으로 땅이 물 높이(+0.5m)까지 내려오는 곳 반걸음 앞에 서고, 5m 앞 물 위로 떠오른다
        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        float edge = 15f;
        if (terrain != null)
            for (float d = 4f; d < 60f; d += 0.5f)
            {
                Vector3 q = root.position + seaDir * d;
                if (terrain.SampleHeight(q) + terrain.transform.position.y <= seaY + 0.5f) { edge = d; break; }
            }
        Vector3 shoreAt = root.position + seaDir * (edge - 0.6f);
        shoreAt.y = terrain != null ? terrain.SampleHeight(shoreAt) + terrain.transform.position.y : seaY + 0.5f;
        var shore = new GameObject("ShoreSpot").transform;
        shore.SetParent(storyGo.transform, false);
        shore.SetPositionAndRotation(shoreAt, Quaternion.LookRotation(seaDir, Vector3.up));
        story.shoreSpot = shore;
        Vector3 rise = shoreAt + seaDir * 5f;
        story.creature = BuildPuppet("SignalOwner", storyGo.transform, new Vector3(rise.x, seaY + 0.3f, rise.z));
        var rng = new System.Random(1987);
        var chorus = new List<Renderer>();
        for (int i = 0; i < 26; i++)
        {
            float ang = Mathf.Lerp(-55f, 55f, (float)rng.NextDouble());
            float dist = Mathf.Lerp(60f, 300f, (float)rng.NextDouble());
            Vector3 p = new Vector3(root.position.x, seaY + 0.5f, root.position.z) + Quaternion.Euler(0f, ang, 0f) * seaDir * dist;
            chorus.Add(Glow($"Chorus_{i:00}", storyGo.transform, p, Mathf.Lerp(8f, 20f, dist / 300f), halo, quad));
        }
        story.chorus = chorus.ToArray();

        // 소리
        var leverSrc = console.gameObject.AddComponent<AudioSource>();
        leverSrc.playOnAwake = false;
        leverSrc.spatialBlend = 1f;
        leverSrc.minDistance = 1f;
        leverSrc.maxDistance = 8f;
        story.leverSound = leverSrc;
        story.leverClip = DomeAquariumDepthSounds.EnsureShutter();
        var seaSrc = storyGo.AddComponent<AudioSource>();
        seaSrc.playOnAwake = false;
        seaSrc.spatialBlend = 0f;
        seaSrc.volume = 0.7f;
        story.seaSound = seaSrc;
        story.seaClip = DomeAquariumDepthSounds.EnsureSonarPing();
        story.chorusClip = DomeAquariumDepthSounds.EnsureChime();

        EditorUtility.SetDirty(story);
        Debug.Log($"[NightStory] ① 등대의 신호 — 서는 자리 {spot.position}, 바다 빛 {Vector3.Distance(spot.position, seaPoint):0}m 앞, 무리 {chorus.Count}개.");
        return story;
    }

    // ─────────────────────────────────────────────────────────────
    // ② 바다거북의 밤
    // ─────────────────────────────────────────────────────────────

    // 해변 로컬 (DomeAquariumBeachBuilder 와 같은 좌표, +x 가 바다 — 물가는 x≈9)
    static readonly Vector3 NestLocal = new Vector3(5.6f, 0f, -9.0f);
    static readonly Vector3 TurtleSpotLocal = new Vector3(4.7f, 0f, -9.9f);          // 둥지에서 1.2m — 8cm 새끼가 점으로 보이지 않게
    static readonly Vector3 TurtleLookLocal = new Vector3(8.5f, 0f, -7.8f);
    static readonly Vector3 NolantNightLocal = new Vector3(5.0f, 0f, -7.5f);
    const int Hatchlings = 12;
    const float HatchlingScale = 0.13f;                                  // 거북 모델(약 1.2m) × 0.13 ≈ 15cm — 실제(5cm)보다 크게, 밤 모래 위 1~2m 앞에서 보이게

    static TurtleNightStory BuildTurtleStory(LobbySequence seq)
    {
        Transform root = seq.transform;
        Transform set = root.Find("LobbySet");
        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        var whale = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        var species = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/GreenTurtle.asset");
        var nolant = Object.FindObjectsByType<NpcDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.speaker == "놀란");
        if (set == null || terrain == null || whale == null || species == null || species.prefab == null || nolant == null)
        { Debug.LogWarning("[NightStory] 해변 · 놀란 · 거북 모델이 없어 바다거북의 밤을 건너뛴다 (19 · 22 · 24번 메뉴)."); return null; }

        var old = set.Find("TurtleNightStory");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("TurtleNightStory");
        go.transform.SetParent(set, false);
        var story = go.AddComponent<TurtleNightStory>();
        story.title = "바다거북의 밤";
        story.nolant = nolant;
        story.deviceLight = Object.FindFirstObjectByType<ObservationLight>(FindObjectsInactive.Include);
        story.ground = terrain;
        story.seaDirection = root.right;
        story.seaY = whale.transform.parent.TransformPoint(new Vector3(0f, whale.seaY, 0f)).y;

        Vector3 W(Vector3 local) => root.TransformPoint(DomeAquariumBeachBuilder.OnGround(root, terrain, local));
        var spot = new GameObject("Spot").transform;
        spot.SetParent(go.transform, false);
        Vector3 look = W(TurtleLookLocal) - W(TurtleSpotLocal);
        look.y = 0f;
        spot.SetPositionAndRotation(W(TurtleSpotLocal), Quaternion.LookRotation(look, Vector3.up));
        story.spot = spot;

        var nolantSpot = new GameObject("NolantSpot").transform;
        nolantSpot.SetParent(go.transform, false);
        Vector3 toPlayer = W(TurtleSpotLocal) - W(NolantNightLocal);
        toPlayer.y = 0f;
        nolantSpot.SetPositionAndRotation(W(NolantNightLocal), Quaternion.LookRotation(toPlayer, Vector3.up));
        story.nolantSpot = nolantSpot;

        // 둥지 — 살짝 솟은 모래 더미, 깨어나면 알껍데기 조각
        var sandMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_TurtleNestSand", new Color(0.42f, 0.37f, 0.29f), 0.05f, 0f, false);   // 파헤쳐진 젖은 모래 — 주변보다 조금 어둡게
        var shellMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_TurtleEggshell", new Color(0.93f, 0.91f, 0.86f), 0.3f, 0f, false);
        var nest = new GameObject("Nest").transform;
        nest.SetParent(go.transform, false);
        nest.position = W(NestLocal);
        story.nest = nest;
        Prim(PrimitiveType.Sphere, "Mound", nest, new Vector3(0f, -0.02f, 0f), Quaternion.identity, new Vector3(0.6f, 0.1f, 0.6f), sandMat);
        var rng = new System.Random(77);
        var shells = new List<GameObject>();
        for (int i = 0; i < 7; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.05f + (float)rng.NextDouble() * 0.2f;
            shells.Add(Prim(PrimitiveType.Sphere, $"Shell_{i}", nest, new Vector3(Mathf.Cos(a) * r, 0.025f, Mathf.Sin(a) * r),
                Quaternion.Euler((float)rng.NextDouble() * 180f, 0f, 0f), new Vector3(0.035f, 0.018f, 0.035f), shellMat));
        }
        story.shells = shells.ToArray();

        // 새끼 거북 — 바다거북 모델을 작게 (헤엄 애니메이션이 모래 위에서는 앞지느러미 젓기)
        var hatchlings = new Transform[Hatchlings];
        for (int i = 0; i < Hatchlings; i++)
        {
            var t = (GameObject)PrefabUtility.InstantiatePrefab(species.prefab);
            t.name = $"Hatchling_{i:00}";
            t.transform.SetParent(go.transform, false);
            t.transform.localScale = Vector3.one * HatchlingScale;
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 8cm 남짓한 몸이라 화면 밖 판정이 잦다 — 뼈 갱신이 멈추면 몸이 사라졌다. 열두 마리뿐이라 늘 갱신해도 싸다.
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            foreach (var a in t.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            t.SetActive(false);
            hatchlings[i] = t.transform;
        }
        story.hatchlings = hatchlings;
        EditorUtility.SetDirty(story);
        Debug.Log($"[NightStory] ② 바다거북의 밤 — 둥지 {nest.position}, 새끼 {Hatchlings}마리, 놀란 자리 {nolantSpot.position}.");
        return story;
    }

    // ─────────────────────────────────────────────────────────────
    // ③ 선장의 밤바다
    // ─────────────────────────────────────────────────────────────

    const float BoatScale = 2.2f, BoatDraft = 0.55f;                     // CoastBuilder 의 연구선과 같은 배율 · 흘수(모델 단위)

    static CaptainNightStory BuildCaptainStory(LobbySequence seq)
    {
        Transform set = seq.transform.Find("LobbySet");
        var moored = GameObject.Find("ResearchBoat");
        var bayWhale = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        var sperm = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/SpermWhale.asset");
        var captain = Object.FindObjectsByType<NpcDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.speaker == "보리 선장");
        if (set == null || moored == null || moored.transform.childCount == 0 || bayWhale == null || sperm == null || sperm.prefab == null || captain == null)
        { Debug.LogWarning("[NightStory] 연구선 · 만 · 향유고래 · 선장이 없어 선장의 밤바다를 건너뛴다 (19 · 21 · 22번 메뉴)."); return null; }

        var old = set.Find("CaptainNightStory");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("CaptainNightStory");
        go.transform.SetParent(set, false);
        var story = go.AddComponent<CaptainNightStory>();
        story.title = "선장의 밤바다";
        story.captain = captain;
        story.mooredBoat = moored;
        story.stillness = Object.FindFirstObjectByType<StillnessDetector>(FindObjectsInactive.Include);

        // 만 한가운데 — 뱃머리는 바다(+x) 쪽. 정박한 연구선과 같은 모델 · 배율 · 흘수.
        Transform bay = bayWhale.transform.parent;
        Vector3 bayCenter = bay.TransformPoint(new Vector3(bayWhale.area.center.x - 30f, bayWhale.seaY, bayWhale.area.center.y));
        float seaY = bayCenter.y;
        // 배 · 단추 · 갑판 불 · 빛나는 물 · 고래 · 신호의 주인은 모두 Scene 아래 — 장면 동안만 켠다 (밤 로비 만에 떠 있지 않게)
        var scene = new GameObject("Scene").transform;
        scene.SetParent(go.transform, false);
        var boat = new GameObject("StoryBoat").transform;
        boat.SetParent(scene, false);
        boat.SetPositionAndRotation(new Vector3(bayCenter.x, seaY - BoatDraft * BoatScale, bayCenter.z), Quaternion.LookRotation(Vector3.right, Vector3.up));
        boat.localScale = Vector3.one * BoatScale;
        var model = Object.Instantiate(moored.transform.GetChild(0).gameObject, boat);
        model.name = "Hull";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        story.boat = scene.gameObject;

        // 갑판 높이 — 뒤쪽 가운데 줄의 가장 높은 꼭짓점 (바닥 · 갑판만 지난다)
        float deck = 0.7f;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            float best = float.MinValue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 p = boat.InverseTransformPoint(mf.transform.TransformPoint(v));
                if (Mathf.Abs(p.x) < 0.3f && p.z < -0.6f && p.z > -1.6f) best = Mathf.Max(best, p.y);
            }
            if (best > float.MinValue) deck = best;
        }
        Vector3 L(float x, float y, float z) => boat.TransformPoint(new Vector3(x, y, z));

        var spot = new GameObject("Spot").transform;
        spot.SetParent(go.transform, false);
        spot.SetPositionAndRotation(L(0f, deck, -1.35f), Quaternion.LookRotation(boat.right, Vector3.up));  // 오른쪽 뱃전 너머 바다를 본다 (돛대에서 한 걸음 뒤)
        story.spot = spot;
        var captainSpot = new GameObject("CaptainSpot").transform;
        captainSpot.SetParent(go.transform, false);
        Vector3 cs = L(-0.3f, deck, -0.75f);                              // 왼쪽 뒤 — 바다 쪽 시야를 가리지 않게
        Vector3 toPlayer = spot.position - cs;
        toPlayer.y = 0f;
        captainSpot.SetPositionAndRotation(cs, Quaternion.LookRotation(toPlayer, Vector3.up));
        story.captainSpot = captainSpot;

        // 단추 둘 — 오른쪽 뱃전 앞 받침 위, 허리 높이에 비스듬히
        var font = DomeAquariumSetup.LoadKoreanFont();
        var iron = DomeAquariumLobbyBuilder.LitMaterial("Mat_CoastLighthouseIron", new Color(0.12f, 0.13f, 0.14f), 0.45f, 0.5f, false);
        Vector3 consoleAt = spot.position + boat.right * 0.75f;
        Prim(PrimitiveType.Cube, "ConsolePost", scene, Vector3.zero, Quaternion.identity, new Vector3(0.08f, 0.9f, 0.08f), iron)
            .transform.SetPositionAndRotation(consoleAt + Vector3.up * 0.45f, Quaternion.LookRotation(boat.right, Vector3.up));
        var panel = new GameObject("BoatPanel", typeof(RectTransform));
        panel.transform.SetParent(scene, false);
        panel.transform.SetPositionAndRotation(consoleAt + Vector3.up * 0.98f, Quaternion.LookRotation(boat.right, Vector3.up) * Quaternion.Euler(30f, 0f, 0f));
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        panel.AddComponent<CanvasScaler>();
        panel.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var prt = (RectTransform)panel.transform;
        prt.sizeDelta = new Vector2(500f, 150f);
        prt.localScale = Vector3.one * 0.0007f;
        DomeAquariumUiKit.TemplatePlate(NewUI("Plate", panel.transform, new Vector2(500f, 150f)), 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
        story.engineLabel = BoatButton(panel.transform, "엔진 끄기", -120f, story.PressEngine, font);
        story.lampLabel = BoatButton(panel.transform, "갑판 불 끄기", 120f, story.PressLamp, font);

        // 갑판 불 — 선실 뒷벽 위 전등 + 따뜻한 빛무리
        var bulbMat = DomeAquariumLobbyBuilder.LitMaterial("Mat_BoatLampBulb", new Color(1f, 0.92f, 0.75f), 0.6f, 0f, false);
        bulbMat.EnableKeyword("_EMISSION");
        bulbMat.SetColor("_EmissionColor", new Color(2.2f, 1.7f, 1.0f));
        EditorUtility.SetDirty(bulbMat);
        Vector3 lampAt = L(0f, deck + 1.0f, -0.25f);
        story.lampBulb = Prim(PrimitiveType.Sphere, "LampBulb", scene, Vector3.zero, Quaternion.identity, Vector3.one * 0.16f, bulbMat).GetComponent<Renderer>();
        story.lampBulb.transform.position = lampAt;
        var haloMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_BoatLampHalo", "Universal Render Pipeline/Particles/Unlit");
        haloMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        haloMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.45f, 0.8f));
        EditorUtility.SetDirty(haloMat);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        story.lampHalo = Glow("LampHalo", scene, lampAt, 1.6f, haloMat, quad);
        story.lampHalo.enabled = true;

        // 소리 — 엔진(고리) · 단추 딸깍
        var engine = boat.gameObject.AddComponent<AudioSource>();
        engine.clip = DomeAquariumDepthSounds.EnsureEngineIdle();
        engine.loop = true;
        engine.playOnAwake = false;
        engine.spatialBlend = 0.6f;
        engine.minDistance = 3f;
        engine.maxDistance = 30f;
        story.engine = engine;
        var click = go.AddComponent<AudioSource>();
        click.playOnAwake = false;
        click.spatialBlend = 0f;
        story.click = click;
        story.clickClip = DomeAquariumDepthSounds.EnsureShutter();

        // 빛나는 물 — 배 둘레 물 위에 눕힌 둥근 빛무리 (가장자리가 흐린 점 텍스처 — 파도 거품 텍스처는 네모 판 모서리가 보였다)
        var glowMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_SeaBioGlow", "Universal Render Pipeline/Particles/Unlit");
        glowMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        glowMat.SetColor("_BaseColor", Color.white);
        if (glowMat.HasProperty("_Cull")) glowMat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(glowMat);
        var rng = new System.Random(1994);
        var glows = new List<Renderer>();
        for (int i = 0; i < 34; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Lerp(3f, 16f, (float)rng.NextDouble());
            Vector3 p = new Vector3(boat.position.x + Mathf.Cos(a) * r, seaY + 0.06f, boat.position.z + Mathf.Sin(a) * r);
            var g = Glow($"SeaGlow_{i:00}", scene, p, Mathf.Lerp(6f, 11f, (float)rng.NextDouble()), glowMat, quad);
            g.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 360f, 0f);
            glows.Add(g);
        }

        // 향유고래 — 오른쪽 30m 앞을 뒤에서 앞으로 지나며 두 번 숨을 쉰다
        var whale = (GameObject)PrefabUtility.InstantiatePrefab(sperm.prefab);
        whale.name = "SpermWhale";
        whale.transform.SetParent(scene, false);
        whale.transform.localScale = Vector3.one * Mathf.Max(0.001f, sperm.swimScale);
        foreach (var c in whale.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in whale.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
        foreach (var an in whale.GetComponentsInChildren<Animator>(true)) { an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.speed = 0.4f; }
        whale.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var wr = whale.GetComponentsInChildren<Renderer>(true);
        Bounds wb = wr.Length > 0 ? wr[0].bounds : new Bounds(Vector3.zero, Vector3.one * 10f);
        foreach (var r in wr) wb.Encapsulate(r.bounds);
        var start = new GameObject("WhaleStart").transform;
        start.SetParent(go.transform, false);
        Vector3 s0 = boat.position + boat.right * 22f - boat.forward * 20f;
        start.position = new Vector3(s0.x, seaY - wb.max.y + 0.6f, s0.z);    // 등이 물 위로 0.6m
        var end = new GameObject("WhaleEnd").transform;
        end.SetParent(go.transform, false);
        end.position = start.position + boat.forward * 40f;
        // 고래 둘레 물도 빛난다 — 밤바다에서 검은 등만으로는 잘 안 보였다. 몸을 따라 물 위에 둥근 빛무리 여섯.
        for (int i = 0; i < 6; i++)
        {
            float along = Mathf.Lerp(wb.min.z, wb.max.z, i / 5f);
            float side = (i % 2 == 0 ? 1f : -1f) * wb.extents.x * 1.3f;
            var g = Glow($"WakeGlow_{i}", whale.transform, Vector3.zero, Mathf.Lerp(5f, 9f, i / 5f), glowMat, quad);
            g.transform.SetPositionAndRotation(new Vector3(side, wb.max.y - 0.6f + 0.06f, along), Quaternion.Euler(90f, i * 47f, 0f));
            g.transform.localScale = g.transform.localScale / Mathf.Max(0.001f, sperm.swimScale);
            g.transform.SetParent(whale.transform, true);
            glows.Add(g);
        }
        story.whaleStart = start;
        story.whaleEnd = end;
        var spout = Object.Instantiate(bayWhale.spout.gameObject, whale.transform);
        spout.name = "Spout";
        spout.transform.position = new Vector3(-wb.extents.x * 0.35f, wb.max.y, wb.center.z + wb.extents.z * 0.92f);
        spout.transform.rotation = Quaternion.LookRotation(new Vector3(-0.45f, 1f, 0.75f).normalized, Vector3.forward);   // 향유고래 숨은 앞 왼쪽으로 비스듬히
        story.whaleSpout = spout.GetComponent<ParticleSystem>();
        var blow = whale.AddComponent<AudioSource>();
        blow.clip = DomeAquariumDepthSounds.EnsureWhaleBlow();
        blow.playOnAwake = false;
        blow.spatialBlend = 1f;
        blow.minDistance = 20f;
        blow.maxDistance = 400f;
        blow.dopplerLevel = 0f;
        story.whaleBlow = blow;
        story.whale = whale.transform;
        story.seaGlow = glows.ToArray();
        whale.SetActive(false);

        // 신호의 주인 — 서는 자리 오른쪽 앞 6m, 물 위로 몸 위쪽 절반을 내민다. 1,000m 에서 만난 것과 같은 모습 · 크기
        // (예전에는 1.8m 둥근 공 + 8m 빛무리라 다른 생물처럼 보였다)
        Vector3 toOwner = (boat.right * 8f + boat.forward * 1.5f).normalized;
        Vector3 o = spot.position + new Vector3(toOwner.x, 0f, toOwner.z).normalized * 6f;
        story.owner = BuildPuppet("SignalOwner", scene, new Vector3(o.x, seaY + 0.3f, o.z));          // 몸이 거의 다 물 위로 — 반쯤 잠기면 반구처럼 보였다
        scene.gameObject.SetActive(false);                                 // 장면 동안만 켠다 (런타임에는 Awake 도 끈다)

        EditorUtility.SetDirty(story);
        Debug.Log($"[NightStory] ③ 선장의 밤바다 — 배 {boat.position} 갑판 높이(모델) {deck:0.00}, 서는 자리 {spot.position}.");
        return story;
    }

    /// <summary>연구선 단추 — 찌르기(PokeSelector)와 레이(UI 버튼) 둘 다 action 을 부른다. 받는 쪽이 한 번만 받는다.</summary>
    static TMP_Text BoatButton(Transform panel, string text, float x, UnityEngine.Events.UnityAction action, TMP_FontAsset font)
    {
        var size = new Vector2(220f, 100f);
        var b = NewUI(text, panel, size);
        b.anchoredPosition = new Vector2(x, 0f);
        var parts = DomeAquariumUiKit.PokeButton(b, size, text, null, DomeAquariumUiKit.Tone.Primary, 26f, font, corner: 18f, lift: 12f);
        UnityEventTools.AddPersistentListener(parts.button.onClick, action);
        var col = b.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(size.x + 12f, size.y + 12f, 240f);
        var rb = b.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var poke = b.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = "Hand";
        poke.inputCooldown = 0.6f;
        UnityEventTools.AddPersistentListener(poke.onPoked, action);
        return parts.label;
    }

    // ─────────────────────────────────────────────────────────────
    // ④ 비브의 바시스피어
    // ─────────────────────────────────────────────────────────────

    static readonly Vector3 BathysphereAt = new Vector3(-400f, -150f, -400f);   // 아무것도 보이지 않는 먼 곳 — 창밖 물(안쪽 면 구)이 바깥을 가린다
    const float SphereRadius = 0.75f, WindowRadius = 0.15f;

    static BathysphereStory BuildBathysphereStory(LobbySequence seq)
    {
        Transform set = seq.transform.Find("LobbySet");
        if (set == null) return null;
        var old = set.Find("BathysphereStory");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("BathysphereStory");
        go.transform.SetParent(set, false);
        var story = go.AddComponent<BathysphereStory>();
        story.title = "비브의 바시스피어";
        var font = DomeAquariumSetup.LoadKoreanFont();

        var scene = new GameObject("Scene").transform;
        scene.SetParent(go.transform, false);
        scene.position = BathysphereAt;
        story.scene = scene.gameObject;
        var sphere = new GameObject("Sphere").transform;
        sphere.SetParent(scene, false);
        sphere.localPosition = Vector3.zero;
        story.sphere = sphere;

        var spot = new GameObject("Spot").transform;
        spot.SetParent(go.transform, false);
        spot.SetPositionAndRotation(BathysphereAt - Vector3.up * 1.55f, Quaternion.identity);   // 머리가 쇠공 가운데쯤 (도착하면 눈높이로 맞춘다)
        story.spot = spot;

        // 쇠공 안쪽 — 창(+Z) 자리에 구멍을 낸 구, 양면. 어두운 쇠 빛깔에 아주 약한 발광(빛이 없어도 벽이 보이게).
        var iron = DomeAquariumLobbyBuilder.LitMaterial("Mat_BathysphereInside", new Color(0.24f, 0.27f, 0.25f), 0.35f, 0.6f, false);
        iron.EnableKeyword("_EMISSION");
        iron.SetColor("_EmissionColor", new Color(0.06f, 0.065f, 0.06f));
        iron.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(iron);
        var shell = new GameObject("Inside", typeof(MeshFilter), typeof(MeshRenderer));
        shell.transform.SetParent(sphere, false);
        shell.GetComponent<MeshFilter>().sharedMesh = SaveStoryMesh(InsideSphere(SphereRadius, Mathf.Asin(WindowRadius / SphereRadius) * Mathf.Rad2Deg), "BathysphereInside");
        shell.GetComponent<MeshRenderer>().sharedMaterial = iron;
        var brass = DomeAquariumLobbyBuilder.LitMaterial("Mat_LighthouseBrass", new Color(0.72f, 0.56f, 0.24f), 0.7f, 0.8f, false);
        float boltAngle = (Mathf.Asin(WindowRadius / SphereRadius) + 0.04f);
        for (int i = 0; i < 12; i++)                                       // 창 둘레 볼트
        {
            float a = i * Mathf.PI * 2f / 12f;
            Vector3 p = new Vector3(Mathf.Sin(boltAngle) * Mathf.Cos(a), Mathf.Sin(boltAngle) * Mathf.Sin(a), Mathf.Cos(boltAngle)) * (SphereRadius - 0.01f);
            Prim(PrimitiveType.Sphere, $"Bolt_{i}", sphere, p, Quaternion.identity, Vector3.one * 0.025f, brass);
        }

        // 창밖 물 — 반지름 12m 구의 안쪽 면만 그린다(앞면 컬링 — 음수 배율은 Unity 가 감기 방향을 되돌려 안쪽이 안 보였다).
        // 빛을 받지 않는 색. 바다눈 · 생물만 그 안에 있다.
        var waterMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        string waterPath = "Assets/DomeAquarium/Materials/Mat_BathysphereWater.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(waterPath) == null) AssetDatabase.CreateAsset(waterMat, waterPath);
        waterMat = AssetDatabase.LoadAssetAtPath<Material>(waterPath);
        waterMat.SetColor("_BaseColor", new Color(0.10f, 0.45f, 0.55f));
        waterMat.SetFloat("_Cull", 1f);
        EditorUtility.SetDirty(waterMat);
        var water = Prim(PrimitiveType.Sphere, "Water", sphere, Vector3.zero, Quaternion.identity, Vector3.one * 24f, waterMat);
        story.water = water.GetComponent<Renderer>();

        // 바다눈 — 창 앞 상자 안에서 위로 흘러간다
        var snowGo = new GameObject("MarineSnow");
        snowGo.transform.SetParent(sphere, false);
        snowGo.transform.localPosition = new Vector3(0f, 0f, 3.2f);
        var snow = snowGo.AddComponent<ParticleSystem>();
        snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = snow.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new Color(0.85f, 0.9f, 0.85f, 0.7f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 220;
        var emission = snow.emission;
        emission.rateOverTime = 8f;
        var shape = snow.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(3f, 4f, 4f);
        var vel = snow.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        var snowMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_BathysphereSnow", "Universal Render Pipeline/Particles/Unlit");
        snowMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        snowMat.SetColor("_BaseColor", new Color(0.75f, 0.8f, 0.75f, 0.8f));
        EditorUtility.SetDirty(snowMat);
        var sr = snowGo.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = snowMat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        story.snow = snow;

        // 수심계 — 오른쪽 앞 벽 (바늘 · 글자)
        var white = DomeAquariumLobbyBuilder.LitMaterial("Mat_BathysphereGaugeFace", new Color(0.92f, 0.9f, 0.82f), 0.3f, 0f, false);
        white.EnableKeyword("_EMISSION");
        white.SetColor("_EmissionColor", new Color(0.25f, 0.24f, 0.2f));
        EditorUtility.SetDirty(white);
        var black = DomeAquariumLobbyBuilder.LitMaterial("Mat_BathysphereNeedle", new Color(0.08f, 0.06f, 0.05f), 0.3f, 0f, false);
        Vector3 gaugeDir = new Vector3(0.55f, -0.12f, 0.6f).normalized;
        var gauge = new GameObject("Gauge").transform;
        gauge.SetParent(sphere, false);
        gauge.SetLocalPositionAndRotation(gaugeDir * (SphereRadius - 0.03f), Quaternion.LookRotation(gaugeDir, Vector3.up));   // +Z = 바깥 — 글자는 안쪽에서 읽힌다
        Prim(PrimitiveType.Cylinder, "Face", gauge, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.16f, 0.01f, 0.16f), white);
        var needlePivot = new GameObject("Needle").transform;
        needlePivot.SetParent(gauge, false);
        needlePivot.localPosition = new Vector3(0f, 0f, -0.012f);
        Prim(PrimitiveType.Cube, "Hand", needlePivot, new Vector3(0f, 0.03f, 0f), Quaternion.identity, new Vector3(0.006f, 0.06f, 0.004f), black);
        story.needle = needlePivot;
        story.depthText = DomeAquariumLobbyBuilder.Label("DepthText", gauge, new Vector3(0f, -0.11f, -0.045f), 0f, new Vector2(0.24f, 0.05f),
            "수심 0m", 0.035f, new Color(1f, 0.92f, 0.7f), TextAlignmentOptions.Center);

        // 전화 — 왼쪽 벽 수화기 + 앞 왼쪽 아래 고르는 판
        var bakelite = DomeAquariumLobbyBuilder.LitMaterial("Mat_BathyspherePhone", new Color(0.05f, 0.05f, 0.05f), 0.6f, 0f, false);
        Vector3 phoneDir = new Vector3(-0.75f, -0.05f, 0.35f).normalized;
        var handset = new GameObject("Handset").transform;
        handset.SetParent(sphere, false);
        handset.SetLocalPositionAndRotation(phoneDir * (SphereRadius - 0.06f), Quaternion.LookRotation(phoneDir, Vector3.up));
        Prim(PrimitiveType.Capsule, "Grip", handset, Vector3.zero, Quaternion.identity, new Vector3(0.04f, 0.09f, 0.04f), bakelite);
        Prim(PrimitiveType.Cylinder, "Ear", handset, new Vector3(0f, 0.09f, -0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, 0.02f, 0.06f), bakelite);
        Prim(PrimitiveType.Cylinder, "Mouth", handset, new Vector3(0f, -0.09f, -0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, 0.02f, 0.06f), bakelite);
        var ringSrc = handset.gameObject.AddComponent<AudioSource>();
        ringSrc.playOnAwake = false;
        ringSrc.spatialBlend = 0.7f;
        ringSrc.minDistance = 0.5f;
        story.phone = ringSrc;
        story.ring = DomeAquariumDepthSounds.EnsurePhoneRing();

        Vector3 panelDir = new Vector3(-0.38f, -0.42f, 0.82f).normalized;
        var panel = new GameObject("PhonePanel", typeof(RectTransform));
        panel.transform.SetParent(sphere, false);
        panel.transform.SetLocalPositionAndRotation(panelDir * 0.5f, Quaternion.LookRotation(panelDir, Vector3.up));
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        panel.AddComponent<CanvasScaler>();
        panel.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var prt = (RectTransform)panel.transform;
        prt.sizeDelta = new Vector2(440f, 250f);
        prt.localScale = Vector3.one * 0.0006f;
        DomeAquariumUiKit.TemplatePlate(NewUI("Plate", panel.transform, new Vector2(440f, 250f)), 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
        var group = panel.AddComponent<CanvasGroup>();
        var q = NewUI("Question", panel.transform, new Vector2(400f, 60f)).gameObject.AddComponent<TextMeshProUGUI>();
        ((RectTransform)q.transform).anchoredPosition = new Vector2(0f, 82f);
        q.font = font;
        q.fontSize = 26f;
        q.alignment = TextAlignmentOptions.Center;
        q.color = new Color(1f, 0.85f, 0.55f);
        q.raycastTarget = false;
        story.question = q;
        story.phonePanel = group;
        UnityEngine.Events.UnityAction[] actions = { story.Choose0, story.Choose1 };
        var labels = new TMP_Text[2];
        for (int i = 0; i < 2; i++)
        {
            var size = new Vector2(380f, 70f);
            var b = NewUI($"Choice{i}", panel.transform, size);
            b.anchoredPosition = new Vector2(0f, 10f - i * 84f);
            var parts = DomeAquariumUiKit.PokeButton(b, size, "보기", null, DomeAquariumUiKit.Tone.Primary, 26f, font, corner: 18f, lift: 10f);   // 글자가 비면 아이콘 버튼이 되어 글자 칸이 없다
            UnityEventTools.AddPersistentListener(parts.button.onClick, actions[i]);
            var col = b.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(size.x + 12f, size.y + 12f, 200f);
            var rb = b.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var poke = b.gameObject.AddComponent<PokeSelector>();
            poke.mysteryBox = null;
            poke.handTag = "Hand";
            poke.inputCooldown = 0.6f;
            UnityEventTools.AddPersistentListener(poke.onPoked, actions[i]);
            labels[i] = parts.label;
        }
        story.choiceLabels = labels;
        DomeAquariumOverlayUi.Apply(panel);

        // 창밖 생물 — 프로젝트의 심해 생물 모델(Sketchfab · Layer Lab)을 실제 크기에 가깝게. 예전에는 공 · 원기둥을 이어 붙인 모양이었다.
        //   손도끼고기 · 독사고기 · 흡혈오징어 · 초롱아귀는 심해 생물 셰이더 — 창밖 탐조등(_DeviceLight*)에 비칠 때 제 색이 보이고, 발광기는 스스로 빛난다.
        // 탐조등 — 창 바로 바깥 위에서 앞을 비춘다 (비브의 바시스피어도 창밖에 탐조등을 달았다)
        var searchlight = new GameObject("Searchlight").transform;
        searchlight.SetParent(sphere, false);
        searchlight.localPosition = new Vector3(0f, 0.18f, SphereRadius + 0.12f);
        searchlight.localRotation = Quaternion.Euler(6f, 0f, 0f);
        story.searchlight = searchlight;

        // 300m 손도끼고기 무리 — 실제 5~8cm, 창 너머 1.6m 에서 보이게 12cm
        var school = new GameObject("Hatchetfish").transform;
        school.SetParent(sphere, false);
        var rng = new System.Random(1934);
        for (int f = 0; f < 7; f++)
        {
            var fish = DeepModel("Hatchetfish", $"Hatchetfish_{f}", 0.12f, school);
            if (fish == null) break;
            fish.localPosition = new Vector3((float)rng.NextDouble() * 0.5f - 0.25f, (float)rng.NextDouble() * 0.45f - 0.22f, (float)rng.NextDouble() * 0.9f - 0.45f);
            fish.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 16f - 8f, 0f);
        }
        school.gameObject.SetActive(false);
        story.hatchetfish = school;

        // 450m 독사고기 — 실제 25~35cm
        story.viperfish = Holder("Viperfish", sphere, DeepModel("Viperfish", "Model", 0.32f, null));
        // 700m 흡혈오징어 — 실제 약 30cm (팔 포함)
        story.vampireSquid = Holder("VampireSquid", sphere, DeepModel("VampireSquid", "Model", 0.3f, null));
        // 923m 초롱아귀 — 머리 위 등불(발광 세균)
        story.angler = Holder("Angler", sphere, DeepModel("Anglerfish", "Model", 0.24f, null));

        // 580m 다섯 줄 별자리 물고기 — 비브의 기록 그대로: 나비고기 친척 같은 납작한 몸, 옆구리에 노란 빛점 다섯 줄(활처럼 휜 줄), 빛점마다 보랏빛 테.
        // 몸은 나비고기 모델을 어둡게(심해 생물 셰이더 — 탐조등에 비칠 때만 윤곽), 빛점은 한 메시로 묶은 가산 판.
        story.constellation = BuildConstellationFish(sphere);

        scene.gameObject.SetActive(false);
        EditorUtility.SetDirty(story);
        Debug.Log($"[NightStory] ④ 비브의 바시스피어 — 쇠공 {BathysphereAt}, 창 반지름 {WindowRadius}m.");
        return story;
    }

    /// <summary>
    /// 심해 생물 모델 하나 — FishData 의 프리팹을 length m 길이(+Z)로. 무리 연출(FishAnimatorSync)은 떼어 내 헤엄 애니메이션이 제 속도로 돈다.
    /// 작은 몸이라 화면 밖 판정으로 애니메이션이 멈추지 않게 한다.
    /// </summary>
    static Transform DeepModel(string fishId, string name, float length, Transform parent)
    {
        var fd = AssetDatabase.LoadAssetAtPath<FishData>($"Assets/DomeAquarium/FishData/{fishId}.asset");
        if (fd == null || fd.prefab == null) { Debug.LogWarning($"[NightStory] {fishId} 모델이 없다 (21번 메뉴)."); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fd.prefab);
        go.name = name;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        go.transform.localScale = Vector3.one * (length / Mathf.Max(0.01f, b.size.z));
        foreach (var sync in go.GetComponentsInChildren<FishAnimatorSync>(true)) Object.DestroyImmediate(sync);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.updateWhenOffscreen = true; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        foreach (var anim in go.GetComponentsInChildren<Animator>(true)) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (parent != null) go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>Pass 가 옮기는 빈 부모 — 모델은 그 안 가운데. 장면이 시작될 때까지 꺼 둔다.</summary>
    static Transform Holder(string name, Transform sphere, Transform model)
    {
        var h = new GameObject(name).transform;
        h.SetParent(sphere, false);
        if (model != null) { model.SetParent(h, false); model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity; }
        h.gameObject.SetActive(false);
        return h;
    }

    static Transform BuildConstellationFish(Transform sphere)
    {
        var holder = new GameObject("ConstellationFish").transform;
        holder.SetParent(sphere, false);
        const float length = 0.24f;                                        // 비브: 약 15cm — 손바닥만 한 창 너머 1.3m 에서 빛점 줄이 보이게 조금 크게
        var body = DeepModel("EightbandedButterflyfish", "Body", length, null);
        if (body == null) body = DeepModel("CopperbandButterflyfish", "Body", length, null);
        if (body != null)
        {
            string path = "Assets/DomeAquarium/Materials/Mat_ConstellationBody.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("DomeAquarium/Deep Creature")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", new Color(0.09f, 0.085f, 0.11f));
            mat.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(mat);
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray();
        }

        // 빛점 — 몸 양옆, 활처럼 휜 다섯 줄. 노란 점 둘레에 작은 보랏빛 점 여섯 (가산이라 겹치면 하얗게 바래 — 테를 겹치지 않게 둘렀다)
        Bounds b = new Bounds(Vector3.zero, new Vector3(0.035f, 0.19f, length));
        if (body != null)
        {
            var rs = body.GetComponentsInChildren<Renderer>(true);
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
        }
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();
        void Dot(Vector3 c, Vector3 normal, float size, Color col)
        {
            Vector3 u = Vector3.Cross(normal, Vector3.up).normalized * size, v = Vector3.up * size;
            int i0 = verts.Count;
            verts.Add(c - u - v); verts.Add(c + u - v); verts.Add(c - u + v); verts.Add(c + u + v);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));
            for (int k = 0; k < 4; k++) colors.Add(col);
            bool flip = normal.x < 0f;
            if (!flip) { tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1); tris.Add(i0 + 1); tris.Add(i0 + 2); tris.Add(i0 + 3); }
            else { tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2); tris.Add(i0 + 1); tris.Add(i0 + 3); tris.Add(i0 + 2); }
        }
        var yellow = new Color(1.25f, 0.85f, 0.18f);
        var purple = new Color(0.55f, 0.12f, 1.1f);
        float hz = b.extents.z * 0.62f, hy = b.extents.y * 0.62f;
        for (int row = 0; row < 5; row++)
        {
            float y0 = Mathf.Lerp(-hy, hy, row / 4f) * 0.8f;
            for (int k = 0; k < 7; k++)
            {
                float z = Mathf.Lerp(-hz, hz * 0.9f, k / 6f);
                float y = y0 + (1f - (z / hz) * (z / hz)) * 0.012f * (row - 2);  // 가운데가 부푼 활 모양 — 몸 윤곽을 따라
                float edge = 1f - Mathf.Clamp01(Mathf.Pow(z / b.extents.z, 2f) + Mathf.Pow(y / b.extents.y, 2f));
                if (edge < 0.08f) continue;                                     // 몸 밖으로 나가는 점은 뺀다
                foreach (float side in new[] { 1f, -1f })
                {
                    float x = side * (b.extents.x * Mathf.Sqrt(edge) + 0.002f);
                    var n = new Vector3(side, 0f, 0f);
                    Vector3 at = new Vector3(x, y, z) + b.center;
                    Dot(at + n * 0.0015f, n, 0.0075f, yellow);                                // 노란 빛점
                    for (int r = 0; r < 6; r++)                                             // 둘레의 작은 보랏빛 발광기 여섯 (비브: "빛마다 보랏빛 발광기가 둘렀다")
                    {
                        float ang = r * Mathf.PI / 3f;
                        Dot(at + n * 0.001f + new Vector3(0f, Mathf.Sin(ang), Mathf.Cos(ang)) * 0.0085f, n, 0.0032f, purple);
                    }
                }
            }
        }
        var mesh = new Mesh { name = "ConstellationLights" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        var lights = new GameObject("Lights", typeof(MeshFilter), typeof(MeshRenderer));
        lights.GetComponent<MeshFilter>().sharedMesh = SaveStoryMesh(mesh, "ConstellationLights");
        var lmat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_PhotophoreDots", "Universal Render Pipeline/Particles/Unlit");
        lmat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        lmat.SetColor("_BaseColor", new Color(1.15f, 1.15f, 1.15f, 1f));
        EditorUtility.SetDirty(lmat);
        var lr = lights.GetComponent<MeshRenderer>();
        lr.sharedMaterial = lmat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        var fishRoot = new GameObject("Fish").transform;
        fishRoot.SetParent(holder, false);
        if (body != null) body.SetParent(fishRoot, false);
        // 빛점은 몸통 뼈(SPINE)에 붙여 헤엄치며 휘는 몸을 따라가게
        Transform spine = body != null ? body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SPINE") : null;
        lights.transform.SetParent(fishRoot, false);
        if (spine != null) lights.transform.SetParent(spine, true);
        holder.gameObject.SetActive(false);
        return holder;
    }

    internal enum VentMaterialSet { Keep, PaleCrab }

    /// <summary>
    /// 열수구 모델 하나 — Assets/ThirdParty/Sketchfab/Vent/{file}.glb 를 size m 로(높이 기준 byHeight, 아니면 가로 큰 쪽) 맞춰
    /// 바닥 가운데가 parent 원점에 오게 놓는다. 재질은 심해 생물 셰이더로 바꾼다(로봇 조명에 비칠 때 보인다).
    /// </summary>
    internal static Transform VentModel(string file, float size, bool byHeight, Transform parent, VentMaterialSet look) =>
        ModelFrom($"Assets/ThirdParty/Sketchfab/Vent/{file}.glb", size, byHeight, parent, look);

    /// <summary>VentModel 과 같되 GLB 경로를 직접 — 1,000m 고래 낙하처럼 다른 폴더 모델에도 쓴다(DomeAquariumDeepEncounters).</summary>
    internal static Transform ModelFrom(string path, float size, bool byHeight, Transform parent, VentMaterialSet look)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Debug.LogWarning($"[NightStory] {path} 가 없다."); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = file;
        Quaternion upright = go.transform.localRotation;                    // glTF 가 세워 둔 회전은 그대로 — 지우면 굴뚝이 누워 카메라 쪽으로 13m 뻗었다
        go.transform.position = Vector3.zero;
        go.transform.localScale = Vector3.one;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float k = size / Mathf.Max(0.001f, byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z));
        go.transform.localScale = Vector3.one * k;
        go.transform.SetParent(parent, false);
        go.transform.localRotation = upright;
        go.transform.localPosition = new Vector3(-b.center.x * k, -b.min.y * k, -b.center.z * k);   // 바닥 가운데를 원점에
        foreach (var r in rs)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = VentMaterialFrom(file, mats[i], look);
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return go.transform;
    }

    /// <summary>스캔 꼭대기 — 연기를 내뿜는 자리 (굴뚝 부모 기준, 가장 높은 곳 25cm 안 꼭짓점들의 가운데).</summary>
    static Vector3 VentTop(Transform model)
    {
        float topY = float.MinValue;
        var pts = new List<Vector3>();
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 w = model.parent.InverseTransformPoint(mf.transform.TransformPoint(v));
                pts.Add(w);
                if (w.y > topY) topY = w.y;
            }
        }
        var near = pts.Where(p => p.y > topY - 0.25f).ToList();
        if (near.Count == 0) return Vector3.zero;
        Vector3 c = Vector3.zero;
        foreach (var p in near) c += p;
        c /= near.Count;
        return new Vector3(c.x, topY, c.z);
    }

    static readonly Dictionary<string, Material> ventMats = new Dictionary<string, Material>();

    /// <summary>glTF 재질 → 심해 생물 셰이더 (바탕색 텍스처 · 색만 옮긴다, 양면). 게는 조금 더 밝게.</summary>
    internal static Material VentMaterialFrom(string file, Material src, VentMaterialSet look)
    {
        string key = $"{file}_{(src != null ? src.name : "none")}_{look}";
        if (ventMats.TryGetValue(key, out var cached) && cached != null) return cached;
        string safe = new string(key.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        Texture tex = null;
        Color color = Color.white;
        if (src != null)
        {
            if (src.HasProperty("baseColorTexture")) tex = src.GetTexture("baseColorTexture");
            else if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
            if (src.HasProperty("baseColorFactor")) color = src.GetColor("baseColorFactor");
        }
        if (look == VentMaterialSet.PaleCrab) color = new Color(1.1f, 1.1f, 1.05f);   // 텍스처는 Blender 로 희게 바꿔 두었다(무늬는 남는다)
        var m = VentMaterial($"Vent/Mat_{safe}", tex, color);
        ventMats[key] = m;
        return m;
    }

    internal static Material VentMaterial(string name, Texture tex, Color color)
    {
        string path = $"Assets/DomeAquarium/Materials/{name}.mat";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var shader = Shader.Find("DomeAquarium/Deep Creature");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        if (m.shader != shader) m.shader = shader;
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", color);
        m.SetColor("_EmissionColor", Color.black);
        m.SetFloat("_Cull", 0f);                                            // 스캔 메시는 구멍 · 뒷면이 보일 수 있다 — 양면
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>GLB 안의 걷기 클립을 고리로 도는 복사본으로 (가져온 클립은 고칠 수 없다).</summary>
    internal static AnimationClip LoopedVentClip(string file, string name)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath($"Assets/ThirdParty/Sketchfab/Vent/{file}.glb").OfType<AnimationClip>().FirstOrDefault();
        if (src == null) return null;
        string path = $"Assets/DomeAquarium/Creatures/Animations/{name}.anim";
        System.IO.Directory.CreateDirectory("Assets/DomeAquarium/Creatures/Animations");
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (old != null) AssetDatabase.DeleteAsset(path);
        var clip = Object.Instantiate(src);
        clip.name = name;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    internal static RuntimeAnimatorController CrabController(AnimationClip walk)
    {
        string path = "Assets/DomeAquarium/Creatures/Animations/VentCrab.controller";
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(path, walk);
        return ctrl;
    }

    /// <summary>안쪽에서 보는 구 — +Z 극 둘레 holeDeg 안쪽을 비워 창을 낸다. 노멀은 안쪽.</summary>
    static Mesh InsideSphere(float r, float holeDeg)
    {
        const int lat = 48, lon = 48;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        for (int i = 0; i <= lat; i++)
        {
            float th = i * Mathf.PI / lat;
            for (int j = 0; j <= lon; j++)
            {
                float ph = j * Mathf.PI * 2f / lon;
                var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th));
                verts.Add(p * r);
                normals.Add(-p);
            }
        }
        var tris = new List<int>();
        for (int i = 0; i < lat; i++)
        {
            if (i * 180f / lat < holeDeg) continue;                         // 창 구멍
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, b = a + lon + 1;
                tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        }
        var mesh = new Mesh { name = "BathysphereInside" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    internal static Mesh SaveStoryMesh(Mesh mesh, string name)
    {
        string path = $"Assets/DomeAquarium/Coast/Meshes/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ─────────────────────────────────────────────────────────────
    // ⑤ 다음 신호 2,500m
    // ─────────────────────────────────────────────────────────────

    static readonly Vector3 SubmersibleAt = new Vector3(400f, -200f, -400f);
    const float CabinRadius = 1.0f, ViewportRadius = 0.2f;

    static NextSignalStory BuildNextSignalStory(LobbySequence seq)
    {
        Transform set = seq.transform.Find("LobbySet");
        if (set == null) return null;
        var old = set.Find("NextSignalStory");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("NextSignalStory");
        go.transform.SetParent(set, false);
        var story = go.AddComponent<NextSignalStory>();
        story.title = "다음 신호 2,500m";
        var font = DomeAquariumSetup.LoadKoreanFont();

        var scene = new GameObject("Scene").transform;
        scene.SetParent(go.transform, false);
        scene.position = SubmersibleAt;
        story.scene = scene.gameObject;
        var cabin = new GameObject("Cabin").transform;
        cabin.SetParent(scene, false);
        story.cabin = cabin;
        var spot = new GameObject("Spot").transform;
        spot.SetParent(go.transform, false);
        spot.SetPositionAndRotation(SubmersibleAt - Vector3.up * 1.55f, Quaternion.identity);
        story.spot = spot;

        Material Flat(string name, Color c, float cull = 2f)
        {
            string path = $"Assets/DomeAquarium/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", cull);
            EditorUtility.SetDirty(m);
            return m;
        }

        // 잠수정 안 — 바시스피어보다 조금 큰 쇠공, 앞 아래로 둥근 창 하나
        var hull = DomeAquariumLobbyBuilder.LitMaterial("Mat_SubmersibleInside", new Color(0.30f, 0.32f, 0.33f), 0.4f, 0.5f, false);
        hull.EnableKeyword("_EMISSION");
        hull.SetColor("_EmissionColor", new Color(0.07f, 0.075f, 0.08f));
        hull.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(hull);
        var inside = new GameObject("Inside", typeof(MeshFilter), typeof(MeshRenderer));
        inside.transform.SetParent(cabin, false);
        inside.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);    // 창(+Z)이 앞 아래 열수구를 본다
        inside.GetComponent<MeshFilter>().sharedMesh = SaveStoryMesh(InsideSphere(CabinRadius, Mathf.Asin(ViewportRadius / CabinRadius) * Mathf.Rad2Deg), "SubmersibleInside");
        inside.GetComponent<MeshRenderer>().sharedMaterial = hull;

        // 바깥 — 검푸른 물(안쪽 면), 바닥, 현무암, 굴뚝(실제 스캔), 관벌레 무리, 열수구 게 무리.
        // 모델은 Sketchfab 무료 모델(CC-BY, Assets/ThirdParty/Sketchfab/Vent). 재질은 심해 생물 셰이더로 바꿔 로봇 카메라 조명(_DeviceLight*)에 비칠 때 제 색이 보인다
        // — 2,500m 에는 햇빛이 없으니 실제 ROV 영상처럼 로봇의 빛이 닿는 곳만 보인다.
        Prim(PrimitiveType.Sphere, "Water", scene, Vector3.zero, Quaternion.identity, Vector3.one * 60f, Flat("Mat_VentWater", new Color(0.004f, 0.012f, 0.02f), 1f));
        var sediment = VentMaterial("Mat_VentFloor", null, new Color(0.30f, 0.27f, 0.24f));
        var floor = Prim(PrimitiveType.Cylinder, "Floor", scene, new Vector3(0f, -6.2f, 6f), Quaternion.identity, new Vector3(40f, 0.1f, 40f), sediment);
        var basalt = VentMaterial("Mat_VentRock", null, new Color(0.20f, 0.19f, 0.19f));
        // Poly Haven 바위(CC0)를 Blender 로 1,500 삼각형까지 줄인 것 — 해안의 1k 바위(개당 1.5만)를 그대로 쓰면 바위만 25만이었다
        var rockMeshes = new[] { "rock_07_low", "rock_09_low" }
            .Select(n => AssetDatabase.LoadAllAssetsAtPath($"Assets/ThirdParty/PolyHaven/Models/VentRocks/{n}.glb").OfType<Mesh>().FirstOrDefault()).Where(m => m != null).ToArray();
        var rng = new System.Random(2500);
        for (int i = 0; i < 18; i++)                                       // 바닥의 현무암 덩어리 (해안 바위 모델을 검게)
        {
            float x = (float)rng.NextDouble() * 24f - 12f, z = 3f + (float)rng.NextDouble() * 14f, sz = 0.5f + (float)rng.NextDouble() * 1.4f;
            if (Mathf.Abs(x) < 1.6f && z > 7f && z < 11f) continue;           // 굴뚝 자리는 비운다
            GameObject r;
            if (rockMeshes.Length > 0)
            {
                var mesh = rockMeshes[i % rockMeshes.Length];
                r = new GameObject($"Rock_{i}", typeof(MeshFilter), typeof(MeshRenderer));
                r.GetComponent<MeshFilter>().sharedMesh = mesh;
                r.GetComponent<MeshRenderer>().sharedMaterial = basalt;
                r.transform.SetParent(scene, false);
                float m = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
                r.transform.localScale = new Vector3(sz / m, sz * 0.55f / Mathf.Max(0.01f, mesh.bounds.size.y), sz / m);
                r.transform.localPosition = new Vector3(x, -6.2f, z);
                r.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }
            else r = Prim(PrimitiveType.Sphere, $"Rock_{i}", scene, new Vector3(x, -6.2f + sz * 0.2f, z), Quaternion.identity, new Vector3(sz, sz * 0.5f, sz * 0.8f), basalt);
            r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        var smokeMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_VentSmoke", "Universal Render Pipeline/Particles/Unlit", additive: false);
        smokeMat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        smokeMat.SetColor("_BaseColor", new Color(0.06f, 0.055f, 0.05f, 0.9f));
        EditorUtility.SetDirty(smokeMat);
        // 검은 굴뚝 — 마리아나 "세쿼이아" 열수구 스캔(DeepSeaBEE). 꼭대기에서 검은 연기(광물 알갱이)를 뿜는다
        Transform Chimney(string name, Vector3 at, float height, float yaw)
        {
            var c = new GameObject(name).transform;
            c.SetParent(scene, false);
            c.localPosition = at;
            c.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var model = VentModel("SequoiaVent", height, true, c, VentMaterialSet.Keep);
            Vector3 top = model != null ? VentTop(model) : new Vector3(0f, height, 0f);
            var smoke = new GameObject("Smoke").AddComponent<ParticleSystem>();
            smoke.transform.SetParent(c, false);
            smoke.transform.localPosition = top;
            smoke.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = smoke.main;
            m.loop = true; m.playOnAwake = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 120;
            var e = smoke.emission; e.rateOverTime = 14f;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.12f;
            var grow = smoke.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var r = smoke.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var focus = new GameObject("Top").transform;
            focus.SetParent(c, false);
            focus.localPosition = new Vector3(top.x, height * 0.6f, top.z);
            return focus;
        }
        var chimneyTop = Chimney("BlackSmoker", new Vector3(0f, -6.2f, 9f), 5.5f, 0f);
        Chimney("BlackSmoker2", new Vector3(6.5f, -6.2f, 13f), 3.6f, 130f);   // 같은 스캔을 작게 돌려 세운 둘째 굴뚝

        // 관벌레 — 바위 위 관벌레 무리 스캔(Scientart, Riftia pachyptila). 굴뚝 왼쪽 아래에 세 덩이
        var worms = new GameObject("TubeWorms").transform;
        worms.SetParent(scene, false);
        worms.localPosition = new Vector3(-3.4f, -6.2f, 6.5f);
        // Riftia 관은 2m 넘게 자란다 — 무리 하나 2m, 둘레에 작은 무리 둘
        var wormSpots = new[] { (new Vector3(0f, 0f, 0f), 2.0f, 0f), (new Vector3(-1.3f, 0f, 1.0f), 1.4f, 140f), (new Vector3(1.1f, 0f, 1.3f), 1.5f, 250f) };
        foreach (var (at, size, yaw) in wormSpots)
        {
            var holder = new GameObject("Cluster").transform;
            holder.SetParent(worms, false);
            holder.localPosition = at;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            VentModel("Riftia", size, false, holder, VentMaterialSet.Keep);
        }
        var wormFocus = new GameObject("Focus").transform;
        wormFocus.SetParent(worms, false);
        wormFocus.localPosition = new Vector3(0f, 0.8f, 0.6f);

        // 열수구 게 — 걷는 게 모델(majidakbarimoganjogi)을 하얗게. 열수구 게(Bythograea)처럼 희고 납작하다. 굴뚝 오른쪽 바위 위
        var crabs = new GameObject("VentCrabs").transform;
        crabs.SetParent(scene, false);
        crabs.localPosition = new Vector3(3.6f, -6.2f, 6.6f);
        var walk = LoopedVentClip("CrabWalking", "VentCrabWalk");
        var ctrl = walk != null ? CrabController(walk) : null;
        for (int i = 0; i < 6; i++)
        {
            var holder = new GameObject($"Crab_{i}").transform;
            holder.SetParent(crabs, false);
            holder.localPosition = new Vector3((float)rng.NextDouble() * 1.8f - 0.9f, 0f, (float)rng.NextDouble() * 1.4f - 0.7f);
            holder.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var crab = VentModel("CrabWalking", 0.45f, false, holder, VentMaterialSet.PaleCrab);  // 실제(다리 포함 약 20cm)보다 크게 — 6m 밖 로봇 카메라 화면에서 보이게
            if (crab == null) continue;
            foreach (var anim in crab.GetComponentsInChildren<Animator>(true))
            {
                anim.runtimeAnimatorController = ctrl;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.speed = 0.2f + (float)rng.NextDouble() * 0.2f;            // 천천히 — 열수구 둘레에서 거의 제자리
            }
            foreach (var smr in crab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        }
        var crabFocus = new GameObject("Focus").transform;
        crabFocus.SetParent(crabs, false);
        crabFocus.localPosition = new Vector3(0f, 0.2f, 0f);

        // 로봇 카메라 — 잠수정 앞 아래 · 좌우로 돈다(몸은 BuildRovBody). 화면은 저해상도 렌더 텍스처 (장면 동안만 켠다)
        var rov = new GameObject("ROV").transform;
        rov.SetParent(scene, false);
        rov.localPosition = new Vector3(0f, -3.2f, 2.2f);                   // 바닥 3m 위 — 관벌레 · 게가 화면 가운데쯤 들어오게
        BuildRovBody(rov, scene, story);
        var camGo = new GameObject("ROVCamera");
        camGo.transform.SetParent(rov, false);
        camGo.transform.localPosition = new Vector3(0f, 0.05f, 0.32f);
        camGo.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 48f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 40f;
        cam.cullingMask = ~(1 << 5);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.004f, 0.012f, 0.02f);
        var rt = new RenderTexture(512, 320, 16) { name = "ROVFeed" };
        string rtPath = "Assets/DomeAquarium/Materials/ROVFeed.renderTexture";
        var oldRt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (oldRt != null) AssetDatabase.DeleteAsset(rtPath);
        AssetDatabase.CreateAsset(rt, rtPath);
        cam.targetTexture = rt;
        var urp = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
        urp.renderPostProcessing = false;
        urp.renderShadows = false;
        urp.allowXRRendering = false;
        story.rovPivot = rov;
        story.rovCamera = cam;
        var rovLight = new GameObject("ROVLight").transform;                 // 로봇 카메라 바로 위의 조명 — 카메라와 같이 돈다
        rovLight.SetParent(rov, false);
        rovLight.localPosition = new Vector3(0f, 0.25f, 0.3f);
        rovLight.localRotation = Quaternion.Euler(18f, 0f, 0f);
        story.rovLight = rovLight;

        // 화면 — 앞 오른쪽 벽 (렌더 텍스처 판 + 위아래 글자 + 가운데 십자)
        Vector3 monitorDir = new Vector3(0.55f, -0.12f, 0.8f).normalized;
        var monitor = new GameObject("Monitor").transform;
        monitor.SetParent(cabin, false);
        monitor.SetLocalPositionAndRotation(monitorDir * (CabinRadius - 0.32f), Quaternion.LookRotation(monitorDir, Vector3.up));
        var screenMat = Flat("Mat_ROVScreen", Color.white);
        screenMat.SetTexture("_BaseMap", rt);
        EditorUtility.SetDirty(screenMat);
        var screen = Prim(PrimitiveType.Quad, "Screen", monitor, Vector3.zero, Quaternion.identity, new Vector3(0.48f, 0.3f, 1f), screenMat);
        Prim(PrimitiveType.Cube, "Bezel", monitor, new Vector3(0f, 0f, 0.012f), Quaternion.identity, new Vector3(0.52f, 0.34f, 0.02f), Flat("Mat_ROVBezel", new Color(0.05f, 0.05f, 0.06f)));
        story.monitorTop = DomeAquariumLobbyBuilder.Label("Top", monitor, new Vector3(0f, 0.13f, -0.003f), 0f, new Vector2(0.46f, 0.04f),
            "로봇 카메라 · 수심 2,500m", 0.022f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Center);
        story.monitorStatus = DomeAquariumLobbyBuilder.Label("Status", monitor, new Vector3(0f, -0.13f, -0.003f), 0f, new Vector2(0.46f, 0.04f),
            "기록 0 / 3", 0.02f, new Color(0.7f, 1f, 0.8f), TextAlignmentOptions.Center);
        story.crosshair = DomeAquariumLobbyBuilder.Label("Crosshair", monitor, new Vector3(0f, 0f, -0.003f), 0f, new Vector2(0.08f, 0.08f),
            "+", 0.06f, Color.white, TextAlignmentOptions.Center);

        // 좌우 단추 — 화면 아래
        var panel = new GameObject("TurnPanel", typeof(RectTransform));
        panel.transform.SetParent(cabin, false);
        Vector3 panelDir = new Vector3(0.42f, -0.45f, 0.78f).normalized;
        panel.transform.SetLocalPositionAndRotation(panelDir * (CabinRadius - 0.42f), Quaternion.LookRotation(panelDir, Vector3.up));
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        panel.AddComponent<CanvasScaler>();
        panel.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var prt = (RectTransform)panel.transform;
        prt.sizeDelta = new Vector2(440f, 130f);
        prt.localScale = Vector3.one * 0.0006f;
        DomeAquariumUiKit.TemplatePlate(NewUI("Plate", panel.transform, new Vector2(440f, 130f)), 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
        BoatButton(panel.transform, "왼쪽으로", -105f, story.TurnLeft, font);   // 화살표 글리프는 한글 글꼴에 없을 수 있다
        BoatButton(panel.transform, "오른쪽으로", 105f, story.TurnRight, font);
        DomeAquariumOverlayUi.Apply(panel);

        // 기록할 셋
        story.targets = new[]
        {
            new NextSignalStory.Target { name = "검은 굴뚝", point = chimneyTop,
                line = "검은 굴뚝 — 땅속에서 350도가 넘는 물이 광물을 녹여 뿜어내요. 검은 연기처럼 보이는 건 아주 작은 광물 알갱이예요." },
            new NextSignalStory.Target { name = "관벌레", point = wormFocus,
                line = "관벌레 — 입도 위도 없어요. 몸속 세균이 열수구의 황화수소로 양분을 만들어 줘요. 햇빛 없이 사는 생물이에요." },
            new NextSignalStory.Target { name = "열수구 게", point = crabFocus,
                line = "열수구 게 — 몸이 하얗고, 다 자라면 눈이 거의 보이지 않아요. 깜깜한 곳이라 눈 대신 냄새와 감각으로 먹이를 찾아요." },
        };
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        story.ping = src;
        story.pingClip = DomeAquariumDepthSounds.EnsureSonarPing();
        story.recordClip = DomeAquariumDepthSounds.EnsureChime();

        scene.gameObject.SetActive(false);
        EditorUtility.SetDirty(story);
        Debug.Log($"[NightStory] ⑤ 다음 신호 2,500m — 잠수정 {SubmersibleAt}, 굴뚝 스캔 2 · 관벌레 무리 3 · 게 6 · 로봇 카메라(추진기 {story.propellers.Length}).");
        return story;
    }

    /// <summary>
    /// 로봇 카메라(ROV) 몸 — 쓸 수 있는 무료 모델(CC-BY · CC0)을 찾지 못해 기본 도형으로 짠다. 실제 관측용 ROV 처럼:
    /// 위는 노란 부력재(무거운 장비를 물속에서 뜨게 하는 발포재), 아래는 검은 틀 · 전자 장비 원통 · 앞 가운데 카메라 · 양옆 조명 둘,
    /// 덕트 추진기 넷(뒤에 비스듬히 둘 · 부력재를 뚫고 위로 둘), 카메라 아래 접어 둔 팔, 위 가운데에서 잠수정 배 밑으로 이어지는 주황 줄(테더).
    /// 원점 = 도는 축, +Z = 카메라가 보는 쪽. 카메라(z 0.32) 앞으로는 아무것도 내밀지 않는다 — 화면에 제 몸이 비치지 않게.
    /// 잠수정 창에 몸을 기울여 내려다보면 앞 아래로 보인다.
    /// </summary>
    static void BuildRovBody(Transform rov, Transform scene, NextSignalStory story)
    {
        // 심해 생물 셰이더 — 다른 열수구 모델과 같이 비친다. Lit 은 해 조명의 물결 무늬(쿠키)까지 받아 2,500m 에서 어색했다
        Material Mat(string name, Color c, Color glow)
        {
            var m = VentMaterial(name, null, c);
            m.SetColor("_EmissionColor", glow);
            EditorUtility.SetDirty(m);
            return m;
        }
        var foam = Mat("Mat_ROVBody", new Color(1f, 0.62f, 0.1f), new Color(0.14f, 0.08f, 0.01f));   // 푸른 주변광에서 연두로 보이지 않게 주황 쪽으로
        var frame = Mat("Mat_ROVFrame", new Color(0.13f, 0.14f, 0.15f), new Color(0.01f, 0.01f, 0.012f));
        var metal = Mat("Mat_ROVMetal", new Color(0.55f, 0.57f, 0.6f), new Color(0.03f, 0.03f, 0.035f));
        var black = Mat("Mat_ROVThruster", new Color(0.05f, 0.05f, 0.055f), Color.black);
        var glass = Mat("Mat_ROVLens", new Color(0.05f, 0.09f, 0.14f), new Color(0.02f, 0.05f, 0.09f));
        var lamp = Mat("Mat_ROVLamp", Color.white, new Color(2.6f, 2.8f, 3.0f));
        var tetherMat = Mat("Mat_ROVTether", new Color(1f, 0.42f, 0.08f), new Color(0.12f, 0.04f, 0f));
        var R = Quaternion.identity;
        var along = Quaternion.Euler(90f, 0f, 0f);                          // 원기둥(위아래 축)을 앞뒤로 눕힌다
        GameObject Bar(string name, Vector3 a, Vector3 b, float thick, Material m) =>
            Prim(PrimitiveType.Cube, name, rov, (a + b) * 0.5f, Quaternion.LookRotation(b - a), new Vector3(thick, thick, (b - a).magnitude + thick), m);

        // 부력재 — 위 절반, 옆으로 검은 띠
        Prim(PrimitiveType.Cube, "Foam", rov, new Vector3(0f, 0.17f, -0.06f), R, new Vector3(0.64f, 0.2f, 0.72f), foam);
        Prim(PrimitiveType.Cube, "FoamBand", rov, new Vector3(0f, 0.17f, -0.1f), R, new Vector3(0.648f, 0.035f, 0.5f), black);
        // 틀 — 네 기둥 · 바닥 썰매 · 가로대
        foreach (float x in new[] { -0.29f, 0.29f })
        {
            foreach (float z in new[] { 0.24f, -0.38f })
                Prim(PrimitiveType.Cube, "Post", rov, new Vector3(x, -0.07f, z), R, new Vector3(0.035f, 0.29f, 0.035f), frame);
            Prim(PrimitiveType.Cube, "Skid", rov, new Vector3(x, -0.2f, -0.07f), R, new Vector3(0.04f, 0.03f, 0.74f), frame);
            Prim(PrimitiveType.Cube, "Rail", rov, new Vector3(x, -0.03f, -0.07f), R, new Vector3(0.025f, 0.025f, 0.66f), frame);
        }
        foreach (float z in new[] { 0.24f, -0.38f })
            Prim(PrimitiveType.Cube, "CrossBar", rov, new Vector3(0f, -0.2f, z), R, new Vector3(0.62f, 0.03f, 0.03f), frame);
        // 전자 장비 원통(내압 용기)
        Prim(PrimitiveType.Cylinder, "Pod", rov, new Vector3(0f, -0.07f, -0.1f), along, new Vector3(0.16f, 0.2f, 0.16f), metal);
        foreach (float z in new[] { 0.105f, -0.305f })
            Prim(PrimitiveType.Cylinder, "PodCap", rov, new Vector3(0f, -0.07f, z), along, new Vector3(0.17f, 0.012f, 0.17f), black);
        // 카메라 — 앞 가운데, 둥근 유리
        Prim(PrimitiveType.Cylinder, "CameraHousing", rov, new Vector3(0f, 0.05f, 0.2f), along, new Vector3(0.1f, 0.1f, 0.1f), metal);
        Prim(PrimitiveType.Sphere, "CameraLens", rov, new Vector3(0f, 0.05f, 0.3f), R, new Vector3(0.09f, 0.09f, 0.045f), glass);
        Prim(PrimitiveType.Cube, "CameraMount", rov, new Vector3(0f, -0.01f, 0.2f), R, new Vector3(0.05f, 0.06f, 0.08f), frame);

        // 조명 둘 — 카메라 양옆, 로봇 조명(rovLight)과 같이 18° 아래로. 빛줄기는 UI 레이어(5) — 로봇 카메라는 UI 를 그리지 않아
        // 화면에는 빛줄기가 끼지 않고, 잠수정 창으로만 물속에 뻗는 빛이 보인다
        var beamMesh = SaveStoryMesh(BeamMesh(2.6f, 0.03f, 0.75f, 0.08f), "ROVBeam");
        var beamMat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_ROVBeam", "Universal Render Pipeline/Particles/Unlit");
        beamMat.SetColor("_BaseColor", new Color(0.75f, 0.88f, 1f, 1f));
        beamMat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(beamMat);
        foreach (float x in new[] { -0.2f, 0.2f })
        {
            var head = new GameObject("Lamp").transform;
            head.SetParent(rov, false);
            head.SetLocalPositionAndRotation(new Vector3(x, 0.01f, 0.25f), Quaternion.Euler(18f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, "Body", head, Vector3.zero, along, new Vector3(0.07f, 0.04f, 0.07f), metal);
            Prim(PrimitiveType.Cylinder, "Lens", head, new Vector3(0f, 0f, 0.04f), along, new Vector3(0.06f, 0.004f, 0.06f), lamp);
            Prim(PrimitiveType.Cube, "Bracket", rov, new Vector3(x, 0.05f, 0.23f), R, new Vector3(0.03f, 0.05f, 0.03f), frame);
            var beam = new GameObject("Beam", typeof(MeshFilter), typeof(MeshRenderer));
            beam.layer = 5;
            beam.transform.SetParent(head, false);
            beam.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            beam.GetComponent<MeshFilter>().sharedMesh = beamMesh;
            var br = beam.GetComponent<MeshRenderer>();
            br.sharedMaterial = beamMat;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            br.receiveShadows = false;
        }

        // 추진기 — 덕트(둘레 고리) 안에 허브와 날개 셋. 뒤 둘은 비스듬히 수평, 둘은 부력재를 뚫고 위를 향한다(구멍은 검은 원판)
        var duct = SaveStoryMesh(DuctMesh(20), "ROVDuct");
        var props = new List<Transform>();
        void Thruster(string name, Vector3 at, Quaternion facing, float size)
        {
            var t = new GameObject(name).transform;
            t.SetParent(rov, false);
            t.SetLocalPositionAndRotation(at, facing);
            var d = new GameObject("Duct", typeof(MeshFilter), typeof(MeshRenderer));
            d.transform.SetParent(t, false);
            d.transform.localScale = new Vector3(size, size, size * 0.85f);
            d.GetComponent<MeshFilter>().sharedMesh = duct;
            var dr = d.GetComponent<MeshRenderer>();
            dr.sharedMaterial = black;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Prim(PrimitiveType.Cylinder, "Hub", t, Vector3.zero, along, new Vector3(size * 0.28f, size * 0.35f, size * 0.28f), metal);
            var prop = new GameObject("Propeller").transform;
            prop.SetParent(t, false);
            for (int b = 0; b < 3; b++)
            {
                var spoke = Quaternion.Euler(0f, 0f, b * 120f);
                Prim(PrimitiveType.Cube, "Blade", prop, spoke * new Vector3(0f, size * 0.24f, 0f), spoke * Quaternion.Euler(0f, 30f, 0f),
                    new Vector3(size * 0.16f, size * 0.32f, size * 0.03f), metal);
            }
            props.Add(prop);
        }
        foreach (float x in new[] { -0.18f, 0.18f })
        {
            Thruster("RearThruster", new Vector3(x, -0.09f, -0.44f), Quaternion.Euler(0f, x < 0f ? 25f : -25f, 0f), 0.15f);
            Thruster("VerticalThruster", new Vector3(x * 0.95f, 0.29f, -0.2f), Quaternion.Euler(-90f, 0f, 0f), 0.15f);
            Prim(PrimitiveType.Cylinder, "ThrusterHole", rov, new Vector3(x * 0.95f, 0.272f, -0.2f), R, new Vector3(0.13f, 0.002f, 0.13f), black);
        }
        story.propellers = props.ToArray();

        // 팔 — 카메라 아래에 접어 둔 집게 팔
        Vector3 shoulder = new Vector3(0.09f, -0.1f, 0.15f), elbow = new Vector3(0.09f, -0.14f, 0.27f), wrist = new Vector3(-0.01f, -0.15f, 0.28f);
        Prim(PrimitiveType.Cube, "ArmBase", rov, shoulder, R, new Vector3(0.06f, 0.05f, 0.05f), frame);
        Bar("ArmUpper", shoulder, elbow, 0.035f, metal);
        Bar("ArmFore", elbow, wrist, 0.03f, metal);
        Bar("JawTop", wrist, wrist + new Vector3(-0.05f, 0.012f, 0f), 0.012f, frame);
        Bar("JawBottom", wrist, wrist + new Vector3(-0.05f, -0.012f, 0f), 0.012f, frame);

        // 테더 — 위 가운데(도는 축)에 매달아 로봇이 돌아도 줄은 그대로. 느슨하게 휘어 잠수정 배 밑으로
        Prim(PrimitiveType.Cylinder, "TetherBoot", rov, new Vector3(0f, 0.31f, 0f), R, new Vector3(0.045f, 0.05f, 0.045f), tetherMat);
        Vector3 a = rov.localPosition + new Vector3(0f, 0.36f, 0f), e = new Vector3(0f, -1.12f, 0.4f);   // 잠수정(반지름 1m) 바로 밑
        Vector3 p1 = a + new Vector3(0.12f, 0.8f, -0.15f), p2 = e + new Vector3(0.08f, -0.55f, 0.45f);
        var pts = new List<Vector3>();
        for (int i = 0; i <= 32; i++)
        {
            float u = i / 32f, w = 1f - u;
            pts.Add(w * w * w * a + 3f * w * w * u * p1 + 3f * w * u * u * p2 + u * u * u * e);
        }
        var tether = new GameObject("Tether", typeof(MeshFilter), typeof(MeshRenderer));
        tether.transform.SetParent(scene, false);
        tether.GetComponent<MeshFilter>().sharedMesh = SaveStoryMesh(TubeMesh(pts, 0.014f, 8), "ROVTether");
        var tr = tether.GetComponent<MeshRenderer>();
        tr.sharedMaterial = tetherMat;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>덕트 — 두께 있는 열린 원통(지름 1 · 길이 1, +Z 축). 바깥 벽 · 안 벽 · 앞뒤 테.</summary>
    static Mesh DuctMesh(int sides)
    {
        const float ro = 0.5f, ri = 0.42f, h = 0.5f;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        void Band(float r0, float z0, float r1, float z1, float facing)        // facing: +1 바깥 벽 · -1 안 벽 · 0 앞뒤 테
        {
            int start = v.Count;
            for (int s = 0; s <= sides; s++)
            {
                float ang = s / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                Vector3 normal = facing != 0f ? dir * facing : new Vector3(0f, 0f, Mathf.Sign(z0));
                v.Add(dir * r0 + Vector3.forward * z0); n.Add(normal);
                v.Add(dir * r1 + Vector3.forward * z1); n.Add(normal);
            }
            for (int s = 0; s < sides; s++)
            {
                int i = start + s * 2;
                t.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
            }
        }
        Band(ro, -h, ro, h, 1f);
        Band(ri, h, ri, -h, -1f);
        Band(ri, h, ro, h, 0f);
        Band(ro, -h, ri, -h, 0f);
        var mesh = new Mesh { name = "ROVDuct" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>빛줄기 — +Z 로 뻗는 열린 원뿔 셋을 겹쳐(가운데가 진하고 가장자리가 옅게), 꼭짓점 알파로 끝으로 갈수록 사라진다.</summary>
    static Mesh BeamMesh(float length, float r0, float r1, float alpha)
    {
        const int sides = 16, rings = 8;
        var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
        foreach (float shrink in new[] { 1f, 0.7f, 0.4f })
        {
            int start = v.Count;
            for (int i = 0; i <= rings; i++)
            {
                float u = i / (float)rings, r = Mathf.Lerp(r0, r1 * shrink, u);
                float a = alpha / 3f * Mathf.Pow(1f - u, 1.6f);
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, u * length));
                    c.Add(new Color(1f, 1f, 1f, a));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int s = 0; s < sides; s++)
                {
                    int p = start + i * (sides + 1) + s, q = p + sides + 1;
                    t.AddRange(new[] { p, q, p + 1, p + 1, q, q + 1 });
                }
        }
        var mesh = new Mesh { name = "ROVBeam" };
        mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>점들을 따라가는 관(테더 같은 줄). 줄이 거의 위아래 · 앞뒤로만 휘므로 옆(X)을 기준 축으로.</summary>
    static Mesh TubeMesh(IList<Vector3> pts, float radius, int sides)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 fwd = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(fwd, Vector3.right).normalized, up = Vector3.Cross(side, fwd);
            for (int s = 0; s <= sides; s++)
            {
                float ang = s / (float)sides * Mathf.PI * 2f;
                Vector3 d = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                v.Add(pts[i] + d * radius); n.Add(d);
            }
        }
        for (int i = 0; i < pts.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int p = i * (sides + 1) + s, q = p + sides + 1;
                t.AddRange(new[] { p, q, p + 1, p + 1, q, q + 1 });
            }
        var mesh = new Mesh { name = "ROVTether" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 밤 이야기의 신호의 주인 — 장면의 UnknownCreature(1,000m)에서 재질 · 크기를 그대로 옮겨 같은 모습으로 (SignalCreaturePuppet).
    /// </summary>
    static SignalCreaturePuppet BuildPuppet(string name, Transform parent, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var p = go.AddComponent<SignalCreaturePuppet>();
        var real = Object.FindFirstObjectByType<UnknownCreature>(FindObjectsInactive.Include);
        if (real != null)
        {
            p.bodyMaterial = real.bodyMaterial;
            p.coreMaterial = real.coreMaterial;
            p.bandMaterial = real.bandMaterial;
            p.haloMaterial = real.haloMaterial;
            p.filamentMaterial = real.filamentMaterial;
            p.bodyRadii = real.bodyRadii;
            p.bandCount = real.bandCount;
            p.filamentLength = real.filamentLength;
            p.haloSize = real.haloSize;
            p.haloColor = real.haloColor;
            p.blinkPeriod = real.blinkPeriod;
        }
        else
        {
            Debug.LogWarning("[NightStory] 1,000m 의 UnknownCreature 가 없어 신호의 주인 재질을 이름으로 찾는다 (18번 메뉴를 먼저).");
            p.bodyMaterial = FindMaterial("Mat_UnknownBody");
            p.coreMaterial = FindMaterial("Mat_UnknownCore");
            p.bandMaterial = FindMaterial("Mat_UnknownBands");
            p.haloMaterial = FindMaterial("Mat_UnknownHalo");
        }
        EditorUtility.SetDirty(p);
        return p;
    }

    static Renderer Glow(string name, Transform parent, Vector3 at, float size, Material mat, Mesh quad)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.localScale = Vector3.one * size;
        go.GetComponent<MeshFilter>().sharedMesh = quad;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return r;
    }

    static Material FindMaterial(string name)
    {
        string guid = AssetDatabase.FindAssets($"{name} t:Material").FirstOrDefault(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
        return guid != null ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)) : null;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 local, Quaternion rot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

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

    // ─────────────────────────────────────────────────────────────
    // 디버그
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/디버그/플레이 중 - 밤: 이야기 판 열기", priority = 227)]
    public static void DebugOpenHub()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[NightStory] 플레이 모드에서만 쓸 수 있다."); return; }
        var d = DiveDirector.Instance;
        if (d == null || d.hub == null) { Debug.LogWarning("[NightStory] 이야기 판(31번 메뉴)이 없다."); return; }
        d.hub.Open();
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 밤: 첫째 이야기 들어가기", priority = 228)]
    public static void DebugFirstStory()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[NightStory] 플레이 모드에서만 쓸 수 있다."); return; }
        var d = DiveDirector.Instance;
        if (d == null || d.hub == null) return;
        d.hub.DebugPress(0);
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 밤: 등대 손잡이 누르기", priority = 229)]
    public static void DebugLever()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[NightStory] 플레이 모드에서만 쓸 수 있다."); return; }
        var s = Object.FindFirstObjectByType<LighthouseSignalStory>();
        if (s == null) return;
        s.Press();
        Debug.Log($"[NightStory] 등대 손잡이 — 이번에 {s.Presses}번");
    }
}
#endif
