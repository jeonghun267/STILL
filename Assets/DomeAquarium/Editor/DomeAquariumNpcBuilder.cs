#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비의 사람(NPC) 둘 — 테라스 데크의 보리 선장과 손자 놀란.
///  - 보리 선장: Quaternius "Pirate Captain" (Poly Pizza, CC0). 앵무새 어니스트를 데리고 문과 쌍안경 사이에 서 있다. 칼은 숨긴다.
///  - 놀란: Winged Boots "Stylized NPC - Peasant Nolant (DEMO)" (에셋스토어 무료, 휴머노이드). 해변 조수 웅덩이 옆에서 맞아 주는
///    선장의 손자 — 체험이 시작되는 자리에서 관찰기 쓰는 법을 알려 주고, 첫 기록을 하면 계단 위 테라스(할아버지)로 보낸다
///    (DomeAquariumBeachBuilder). 데모에는 서기 · 걷기 동작만 있다 — 손 흔들기 · 끄덕이기 없이 멈춰 서서 돌아본다.
/// 몸짓(LobbyNpc): 다가오면 멈춰 서서 바라보고 고개를 돌린다. 대화(NpcDialogue): 옆에 뜨는 판에서 "말 걸기" → 대사 + 대답 버튼 1~2개.
///
/// 이야기: 40년 배를 몬 선장은 1,000m 아래를 본 적이 없지만, 30년 전 폭풍우 밤에 불을 끄고 숨죽였을 때 배 밑에서 푸른빛을 봤다 —
/// 손전등을 비추자 사라졌다. 3막의 "불을 끄고 가만히"를 미리 귀띔하는 복선이다. 쌍안경으로 혹등고래를 찾아 달라는 작은 부탁도 한다.
/// 엔딩의 밤 로비에서는 그 빛을 만나고 온 관람객을 맞는다.
/// </summary>
public static class DomeAquariumNpcBuilder
{
    const string Glb = "Assets/ThirdParty/PolyPizza/PirateCaptain.glb";
    const string NolantDir = "Assets/Stylized NPC - Peasant Nolant";
    const string NolantFbx = NolantDir + "/Models/Peasant Nolant(Free Version).fbx";
    const string NolantSkin = "Brown";                                    // 밀짚모자에 갈색 웃옷 — 바닷가 견습 선원
    const float NolantHeight = 1.6f;                                      // 열다섯 살 남짓 (모델은 모자까지 2.04)
    const string Dir = "Assets/DomeAquarium/Creatures";
    const float Height = 1.75f;                                   // 모델은 모자까지 1.62m
    static readonly Vector3 CaptainLocal = new Vector3(9.6f, 0f, 3.6f);   // 데크 위 — 문(x 6, z 0.8~2.3)과 쌍안경(11.2, 6) 사이
    static readonly Vector3 CaptainFaces = new Vector3(6.2f, 0f, 1.5f);   // 처음에는 문 쪽을 본다

    // 대답의 next 가 비면 대화를 마친다. set 은 그 마디에 오면 남는 표시 — 어디서 시작할지(NpcDialogue.Entry)를 정한다.
    static NpcDialogue.Node[] Story() => new[]
    {
        N("start", "어이, 젊은 탐험가! 나는 보리 선장일세. 사십 년 동안 배를 몰다가, 지금은 이 테라스에서 바다를 지켜보고 있지. 이 녀석은 내 앵무새 어니스트고.", "met",
          C("오늘 1,000m까지 내려가요", "dive"), C("어니스트, 안녕!", "parrot")),
        N("parrot", "어니스트가 '안녕, 안녕!' 하는구먼. 바닷바람만 불면 저렇게 신이 난다네. 그래, 자네는 오늘 어디까지 내려가나?", null,
          C("1,000m까지요", "dive")),
        N("dive", "1,000m라고? 허허, 나는 평생 바다 위에만 있었지, 그 아래는 한 번도 못 봤다네. 그런데 요즘 관측소가 술렁이더군. 저 아래서 3주째 이상한 신호가 잡힌다지?", null,
          C("그 신호, 뭔지 아세요?", "story"), C("고래도 보셨어요?", "whale_quest")),
        N("story", "딱 한 번 비슷한 걸 본 적이 있지. 삼십 년 전 폭풍우 치던 밤, 엔진이 멈춰서 불을 다 끄고 숨죽인 채 떠 있었다네. 그런데 배 밑에서 푸른빛이 천천히 올라오더군.", null,
          C("그래서요?", "story2")),
        N("story2", "손전등을 비추는 순간, 스르르 사라져 버렸어. 그 뒤로 늘 생각했지. 어떤 녀석들은 빛을 싫어해서, 불을 끄고 가만히 기다려야 만날 수 있는 게 아닐까 하고.", "signalTold",
          C("기억해 둘게요", "farewell"), C("고래 얘기도 해 주세요", "whale_quest")),
        N("farewell", "그래, 조심히 다녀오게. 엘리베이터는 저 안쪽이라네. 어니스트도 응원하고 있다네!", null,
          C("다녀올게요!", null)),
        N("whale_quest", "저기 쌍안경 보이지? 얼굴을 대고 만 한가운데를 살펴보게. 혹등고래가 숨 쉬러 올라오면 하얀 물기둥이 솟는다네. 찾으면 꼭 알려 주게!", "whaleAsked",
          C("찾아볼게요!", null), C("신호 얘기도 해 주세요", "story")),
        N("whale_wait", "고래는 찾았나? 물기둥부터 찾아보게. 숨 쉬러 자주 올라온다네. 바람이 잔잔하면 '푸우' 하는 숨소리도 들리지.", null,
          C("다시 볼게요", null), C("신호 얘기 다시 해 주세요", "story")),
        N("whale_found", "봤구먼! 그 녀석은 이 만에 자주 들르는 혹등고래라네. 꼬리 아랫면 무늬는 고래마다 다 달라서, 그 무늬로 누가 누군지 알아본다네.", "whaleThanked",
          C("사람 지문 같네요!", "whale_found2")),
        N("whale_found2", "허허, 바로 그거야! 자네는 관찰하는 눈이 좋구먼. 그 눈이면 저 깊은 바다에서도 잘 해낼 걸세.", null,
          C("다녀올게요!", null), C("신호 얘기 다시 해 주세요", "story")),
        N("revisit", "또 왔구먼! 바다는 늘 같아 보여도 매일 달라. 오늘은 저 아래서 뭘 보게 될지 나도 궁금하다네.", null,
          C("신호 얘기 다시 해 주세요", "story"), C("고래는 어디서 봐요?", "whale_quest")),
        N("night", "돌아왔군! 관측소에 불이 환하게 켜진 걸 보고 알았지. 그 푸른빛… 자네가 정말 만났구먼.", null,
          C("불을 끄고 가만히 있었더니 다가왔어요", "night2")),
        N("night2", "그렇지, 그렇지! 내가 삼십 년 동안 못 본 걸 자네가 오늘 기록했어. 바다엔 아직 아무도 못 본 게 훨씬 많다네. 또 오게, 탐험가.", null,
          C("또 올게요!", null)),
    };

    // 놀란 — 선장의 손자. 해변 조수 웅덩이에서 처음 만나 관찰기 쓰는 법을 알려 준다. 첫 기록을 하면 할아버지(테라스)에게 보낸다.
    // 할아버지의 푸른빛 이야기를 믿고, 바다 쓰레기(유령 그물)를 줍는 일을 한다.
    static NpcDialogue.Node[] NolantStory() => new[]
    {
        N("start", "안녕! 나는 놀란이야. 저 관측소에 가는 길이지? 그 전에 이 조수 웅덩이 좀 봐. 바닷물이 빠지면 바위 사이에 이렇게 작은 바다가 남아.", "met",
          C("조수 웅덩이가 뭐야?", "pool"), C("관찰기는 어떻게 써?", "device")),
        N("pool", "밀물 때 들어온 바닷물이 썰물 때 바위 사이에 고인 거야. 게, 불가사리, 성게 같은 친구들이 여기서 다음 밀물을 기다리지.", null,
          C("관찰기로 보고 싶어", "device")),
        N("device", "옆 장비대 위에 관찰기가 있어. 집어서 웅덩이 속 생물에 겨누고, 링이 다 찰 때까지 가만히 지켜보면 기록돼. 게가 느려서 연습하기 딱 좋아.", null,
          C("해 볼게!", null)),
        N("revisit_pre", "관찰기는 집었어? 웅덩이 속 게한테 겨눠 봐. 천천히 기어 다녀서 따라가기 쉬워.", null,
          C("해 볼게!", null), C("조수 웅덩이가 뭐야?", "pool")),
        N("practiced_new", "와, 벌써 첫 기록을 했구나! 나는 놀란이야. 우리 할아버지가 저 나무 계단 위 테라스에 계셔. 바다 이야기를 엄청 많이 아시거든.", "met,practicedSeen",
          C("할아버지가 누구야?", "grandpa"), C("관측소는 어떻게 가?", "way")),
        N("practiced", "와, 첫 기록이다! 이제 진짜 관찰자네. 우리 할아버지가 저 나무 계단 위 테라스에 계셔. 바다 이야기를 엄청 많이 아시거든.", "practicedSeen",
          C("할아버지가 누구야?", "grandpa"), C("관측소는 어떻게 가?", "way")),
        N("grandpa", "보리 선장님! 사십 년 동안 배를 모셨어. 푸른빛을 봤다는 얘기는… 직접 들어 봐. 나는 백 번은 들었지만 그래도 믿어.", null,
          C("가 볼게!", "way")),
        N("way", "저 나무 계단으로 테라스에 올라가서 유리문으로 들어가면 관측소 안이야. 엘리베이터가 열려 있을 거야. 잘 다녀와!", null,
          C("다녀올게!", null), C("너는 여기서 뭐 해?", "job")),
        N("job", "바닷가 쓰레기를 주워. 버려진 그물이 제일 무서워. '유령 그물'이라고 하는데, 바닷속을 떠다니면서 물고기랑 바다거북, 고래까지 걸리게 하거든.", "netTold",
          C("나도 도울게!", null)),
        N("revisit", "또 왔네! 할아버지는 만나 봤어? 계단 위 테라스에 계셔. 쌍안경으로 고래도 볼 수 있어.", null,
          C("유령 그물 얘기 해 줘", "job"), C("할아버지 얘기 해 줘", "grandpa")),
        N("night", "돌아왔구나! 할아버지가 아까부터 계속 싱글벙글이셔. 네가 그 빛을 만났다고!", null,
          C("진짜 만났어!", "night2")),
        N("night2", "대단하다… 나도 언젠가 꼭 내려가 볼 거야. 그때까지 여기 바닷가를 깨끗하게 지키고 있을게!", null,
          C("또 올게!", null)),
    };

    static NpcDialogue.Node N(string id, string line, string set, params NpcDialogue.Choice[] choices) =>
        new NpcDialogue.Node { id = id, line = line, set = set ?? "", choices = choices };

    static NpcDialogue.Choice C(string label, string next) => new NpcDialogue.Choice { label = label, next = next ?? "" };

    [MenuItem("DomeAquarium/22. 로비 NPC (테라스 선장 · 놀란)", priority = 22)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[NPC] 플레이 모드에서는 세울 수 없다."); return; }
        var root = GameObject.Find("LobbyRoot");
        Transform set = root != null ? root.transform.Find("LobbySet") : null;
        var seq = root != null ? root.GetComponent<LobbySequence>() : null;
        if (set == null || seq == null) { Debug.LogError("[NPC] LobbyRoot/LobbySet 이 없다. 먼저 9번 메뉴로 로비를 빌드할 것."); return; }
        Build(set, seq);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(Transform set, LobbySequence seq)
    {
        var old = set.Find("Npcs");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Glb);
        if (source == null) { Debug.LogWarning($"[NPC] 모델이 없다: {Glb}"); return; }

        var group = new GameObject("Npcs").transform;
        group.SetParent(set, false);

        var captain = new GameObject("Captain").transform;
        captain.SetParent(group, false);
        captain.localPosition = CaptainLocal;
        Vector3 face = CaptainFaces - CaptainLocal;
        captain.localRotation = Quaternion.LookRotation(new Vector3(face.x, 0f, face.z), Vector3.up);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, captain);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * (Height / 1.62f);

        // 칼은 숨긴다 — 관측소 테라스의 선장이다
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.Contains("Cutlass") || r.name.Contains("Weapon")) { r.gameObject.SetActive(false); continue; }
            r.sharedMaterial = Material(r.sharedMaterial);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
        }

        var anim = model.GetComponentInChildren<Animator>(true);
        if (anim == null) anim = model.AddComponent<Animator>();
        anim.runtimeAnimatorController = Controller();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var npc = captain.gameObject.AddComponent<LobbyNpc>();
        npc.animator = anim;
        npc.head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");

        var talk = BuildPanel(group, npc, seq, "CaptainTalk", "보리 선장", "Charon", Story());
        talk.whale = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        Debug.Log($"[NPC] 보리 선장 — 로비 로컬 {CaptainLocal}, 대화 마디 {talk.nodes.Length}개, 머리 뼈 {(npc.head != null ? "찾음" : "없음")}.");

        var nolant = BuildNolant(group);
        if (nolant != null)
        {
            var talk2 = BuildPanel(group, nolant, seq, "NolantTalk", "놀란", "Puck", NolantStory());
            talk2.tutorial = seq.tutorial;
            Debug.Log($"[NPC] 놀란 — 해변 조수 웅덩이 옆, 대화 마디 {talk2.nodes.Length}개.");
        }
    }

    /// <summary>놀란 — 휴머노이드라 머리 뼈는 LobbyNpc 가 스스로 찾는다. 기본 렌더 파이프라인 재질이라 URP Simple Lit 으로 바꾼다.</summary>
    static LobbyNpc BuildNolant(Transform group)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NolantDir}/Prefabs/Peasant Nolant {NolantSkin}(Free Version).prefab");
        if (prefab == null) { Debug.LogWarning("[NPC] 놀란 모델이 없다 — 에셋스토어에서 'Stylized NPC - Peasant Nolant (DEMO)' 를 임포트할 것."); return null; }

        // 해변 조수 웅덩이 옆 — 시작 자리를 바라본다 (높이는 지형)
        var root = new GameObject("Nolant").transform;
        root.SetParent(group, false);
        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        var lobbyRoot = group.parent != null ? group.parent.parent : null;
        Vector3 local = DomeAquariumBeachBuilder.NolantLocal;
        if (terrain != null && lobbyRoot != null) local = DomeAquariumBeachBuilder.OnGround(lobbyRoot, terrain, local);
        root.localPosition = local;
        Vector3 d = DomeAquariumBeachBuilder.SpawnLocal - DomeAquariumBeachBuilder.NolantLocal;
        root.localRotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z), Vector3.up);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * (NolantHeight / 2.04f);

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{NolantDir}/Textures/Peasant Nolant {NolantSkin}.png");
        var mat = SimpleLit($"{Dir}/Materials/Mat_Nolant.mat", tex);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        var anim = model.GetComponentInChildren<Animator>(true);
        if (anim == null) anim = model.AddComponent<Animator>();
        anim.runtimeAnimatorController = NolantController();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var npc = root.gameObject.AddComponent<LobbyNpc>();
        npc.animator = anim;
        return npc;
    }

    /// <summary>서기(기본) ↔ 걷기 — bool Walking.</summary>
    static RuntimeAnimatorController NolantController()
    {
        string path = $"{Dir}/Animations/Nolant.controller";
        var clips = AssetDatabase.LoadAllAssetsAtPath(NolantFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
        AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name.EndsWith("|" + n));
        var c0 = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c0 != null) return c0;
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("Walking", AnimatorControllerParameterType.Bool);
        var sm = c.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = Clip("Idle");
        var walk = sm.AddState("Walk");
        walk.motion = Clip("Walk");
        sm.defaultState = idle;
        var go = idle.AddTransition(walk);
        go.AddCondition(AnimatorConditionMode.If, 0f, "Walking");
        go.hasExitTime = false;
        go.duration = 0.25f;
        var back = walk.AddTransition(idle);
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walking");
        back.hasExitTime = false;
        back.duration = 0.25f;
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        return c;
    }

    static Material SimpleLit(string path, Texture tex)
    {
        var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ─────────────────────────────────────────────────────────────
    // 대화 판 — 52 × 47cm. 이름 · 대사(6줄까지) · 대답 버튼 두 개, 대화 전에는 위쪽 52 × 23cm 에 이름 · "말 걸기" 하나(NpcDialogue.ShowIdle).
    // 글자 27px = 2.7cm — 판은 관람객에게서 1.4m 남짓이라 1.1° (10~15세 기준 1° 이상)
    // ─────────────────────────────────────────────────────────────

    static NpcDialogue BuildPanel(Transform group, LobbyNpc npc, LobbySequence seq, string name, string speaker, string voice,
                                  NpcDialogue.Node[] nodes)
    {
        TMP_FontAsset font = DomeAquariumSetup.LoadKoreanFont();
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(group, false);
        go.layer = 5;                                             // UI — 쌍안경 확대 카메라에 찍히지 않게
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(520f, 470f);
        rt.localScale = Vector3.one * 0.001f;
        rt.position = npc.transform.position + Vector3.up * 1.3f;
        var cg = go.AddComponent<CanvasGroup>();

        var plate = UI("Plate", go.transform, new Vector2(520f, 470f), Vector2.zero);
        DomeAquariumUiKit.TemplatePlate(plate, 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);

        var title = Label("Title", go.transform, new Vector2(470f, 44f), new Vector2(0f, 200f), 28f, font, TextAlignmentOptions.Left);
        title.color = new Color(1f, 0.80f, 0.45f);
        title.fontStyle = FontStyles.Bold;

        var body = Label("Line", go.transform, new Vector2(470f, 240f), new Vector2(0f, 55f), 27f, font, TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        body.lineSpacing = 6f;

        var talk = go.AddComponent<NpcDialogue>();
        var talkButton = Button(go.transform, "Talk", "말 걸기", new Vector2(360f, 110f), new Vector2(0f, 20f), DomeAquariumUiKit.Tone.Primary, font, talk.PressTalk);
        // 대답 글자는 대화할 때 채운다 — 비워 두면 키트가 글자 칸을 만들지 않는다.
        // 둘 다 진한 파랑 — 회색(Secondary) 버튼은 글자가 흐려 판 위에서 잘 안 읽혔다
        var c0 = Button(go.transform, "Choice0", "대답", new Vector2(470f, 74f), new Vector2(0f, -110f), DomeAquariumUiKit.Tone.Primary, font, talk.PressChoice0);
        var c1 = Button(go.transform, "Choice1", "대답", new Vector2(470f, 74f), new Vector2(0f, -190f), DomeAquariumUiKit.Tone.Primary, font, talk.PressChoice1);

        talk.npc = npc;
        talk.lobby = seq;
        talk.speaker = speaker;
        talk.voice = voice;
        talk.nodes = nodes;
        talk.panel = rt;
        talk.plate = plate;
        talk.group = cg;
        talk.title = title;
        talk.body = body;
        talk.talkButton = talkButton.button;
        talk.choiceButtons = new[] { c0.button, c1.button };
        talk.choiceLabels = new[] { c0.label, c1.label };
        title.text = talk.speaker;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;   // 키트가 만든 버튼 속까지 UI 레이어로
        DomeAquariumOverlayUi.Apply(go);                                                          // NPC 의 팔 · 앵무새에 가려지지 않게
        EditorUtility.SetDirty(talk);
        return talk;
    }

    /// <summary>찌르기(PokeSelector)와 컨트롤러 레이(Button) 둘 다 action 을 부른다 — NpcDialogue 가 한 번만 받는다.</summary>
    static DomeAquariumUiKit.ButtonParts Button(Transform parent, string name, string text, Vector2 size, Vector2 pos,
                                                DomeAquariumUiKit.Tone tone, TMP_FontAsset font, UnityEngine.Events.UnityAction action)
    {
        var rect = UI(name, parent, size, pos);
        var parts = DomeAquariumUiKit.PokeButton(rect, size, text, null, tone, size.y > 90f ? 36f : 26f, font, corner: 16f, lift: 10f);
        UnityEventTools.AddPersistentListener(parts.button.onClick, action);
        var col = rect.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(size.x + 20f, size.y + 20f, 200f);
        var rb = rect.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var poke = rect.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = "Hand";
        poke.inputCooldown = 0.6f;
        UnityEventTools.AddPersistentListener(poke.onPoked, action);
        if (parts.label != null)
        {
            parts.label.textWrappingMode = TextWrappingModes.NoWrap;
            parts.label.overflowMode = TextOverflowModes.Ellipsis;
        }
        return parts;
    }

    static RectTransform UI(string name, Transform parent, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    static TMP_Text Label(string name, Transform parent, Vector2 size, Vector2 pos, float fontSize, TMP_FontAsset font, TextAlignmentOptions align)
    {
        var rt = UI(name, parent, size, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.raycastTarget = false;
        t.color = Color.white;
        return t;
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: NPC 대화 한 걸음 (첫 대답)", priority = 214)]
    public static void DebugStep0() => DebugStep(0);

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: NPC 대화 한 걸음 (둘째 대답)", priority = 214)]
    public static void DebugStep1() => DebugStep(1);

    static void DebugStep(int choice)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[NPC] 플레이 모드에서만 쓸 수 있다."); return; }
        // 선장 · 놀란 중 지금 대화 중이거나 가까이 있는 쪽
        var all = Object.FindObjectsByType<NpcDialogue>(FindObjectsSortMode.None);
        var talk = all.FirstOrDefault(t => t.Talking) ?? all.FirstOrDefault(t => t.npc != null && t.npc.Near) ?? all.FirstOrDefault();
        if (talk == null) { Debug.LogWarning("[NPC] 선장 대화가 없다. 22번 메뉴를 먼저."); return; }
        talk.DebugStep(choice);
        Debug.Log("[NPC] " + talk.DebugState());
    }

    static Material Material(Material src)
    {
        string path = $"{Dir}/Materials/Mat_Captain.mat";
        var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        if (src != null && src != m)
        {
            Texture tex = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
            if (tex != null) m.SetTexture("_BaseMap", tex);
        }
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>서기(기본) → 손 흔들기 / 끄덕이기(말할 때) → 다시 서기. 트리거 Wave · Talk.</summary>
    static RuntimeAnimatorController Controller()
    {
        string path = $"{Dir}/Animations/Captain.controller";
        var clips = AssetDatabase.LoadAllAssetsAtPath(Glb).OfType<AnimationClip>().ToArray();
        AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name.Contains($"|{n}|"));

        // 이미 있으면 상태 구성은 그대로 두고 클립만 다시 꽂는다
        var c0 = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c0 != null)
        {
            foreach (var st in c0.layers[0].stateMachine.states)
                st.state.motion = st.state.name == "Idle" ? Clip("Idle") : st.state.name == "Wave" ? Clip("Wave") : Clip("Yes");
            EditorUtility.SetDirty(c0);
            return c0;
        }
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Talk", AnimatorControllerParameterType.Trigger);
        var sm = c.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = Clip("Idle");
        sm.defaultState = idle;
        AddGesture(sm, idle, "Wave", Clip("Wave"));
        AddGesture(sm, idle, "Talk", Clip("Yes"));
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        return c;
    }

    static void AddGesture(AnimatorStateMachine sm, AnimatorState idle, string trigger, AnimationClip clip)
    {
        var s = sm.AddState(trigger);
        s.motion = clip;
        var go = idle.AddTransition(s);
        go.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        go.hasExitTime = false;
        go.duration = 0.2f;
        var back = s.AddTransition(idle);
        back.hasExitTime = true;
        back.exitTime = 0.92f;
        back.duration = 0.25f;
    }
}
#endif
