#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지시서 2·3장의 계층과 컴포넌트 배선을 그대로 세운다.
/// 스크립트를 새로 만들지 않고 배치·연결만 한다. 여러 번 실행해도 안전하다(기존 것을 지우고 다시 세움).
/// </summary>
public static class DomeAquariumSceneBuilder
{
    const string Root = "Assets/DomeAquarium";
    const string WaterMatPath = Root + "/Materials/Mat_WaterTankInner.mat";
    const string HandTag = "Hand";

    // 수조 규격 — 셋이 어긋나면 물고기가 벽을 뚫는다.
    const float TankRadius = 12f;
    const float TankHeight = 16f;
    const float FloorY = -8f;

    // 시작 전 비활성으로 둘 오브젝트 이름들
    static readonly string[] BuiltRootNames =
    {
        "WaterTank", "ReefFloor", "FishSwarm", "StartPanelRoot",
        "MysteryBox", "RevealStage", "FishRevealManager", "InfoPanelRoot",
        "GeminiService", "AmbientAudio", "WristMenu", "Seabed"
    };

    // VR 템플릿이 기본으로 깔아둔 콘텐츠. 수조 안에 있으면 안 되므로 비활성한다.
    // 삭제가 아니라 비활성 — 되돌리기 쉽게.
    static readonly string[] TemplateRootsToDisable =
    {
        "CoachingCardRoot", "Interactables", "Environment", "Teleportation Area", "Teleport Area",
    };

    // XR Origin 에서 걷어낼 이동 관련 컴포넌트 (멀미 방지 = 이 프로젝트의 핵심 설계)
    static readonly HashSet<string> LocomotionTypes = new HashSet<string>
    {
        "DynamicMoveProvider", "ContinuousMoveProvider", "ActionBasedContinuousMoveProvider",
        "ContinuousTurnProvider", "ActionBasedContinuousTurnProvider",
        "SnapTurnProvider", "ActionBasedSnapTurnProvider",
        "TeleportationProvider", "ClimbProvider", "GrabMoveProvider", "TwoHandedGrabMoveProvider",
        "LocomotionMediator", "LocomotionSystem", "CharacterControllerDriver",
        "CharacterController", "TunnelingVignetteController",
    };

    [MenuItem("DomeAquarium/4. 씬 빌드", priority = 4)]
    public static void Build()
    {
        // 플레이 중에 빌드하면 프리팹 저장/씬 저장이 예외를 던지고 오브젝트가 중복으로 남는다.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DomeAquarium] 플레이 모드에서는 씬을 빌드할 수 없다. Play 를 멈추고 다시 실행할 것.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        Debug.Log($"[DomeAquarium] 씬 빌드 시작: {scene.path}");

        EnsureHandTag();
        Cleanup();
        DisableTemplateContent();
        DisableThrowingTemplateBehaviours();
        EnableEditorMouseInput();
        ConfigureUnderwaterLighting();

        Transform cameraOffset = PrepareXROrigin(out Camera mainCam);
        if (cameraOffset == null)
        {
            Debug.LogError("[DomeAquarium] XR Origin / Camera Offset 을 찾지 못했다. 중단.");
            return;
        }

        BuildWaterTank();
        DomeAquariumSeabed.BuildFloor();
        var reef = BuildReefFloor();
        var boids = BuildFishSwarm(mainCam != null ? mainCam.transform : null);
        BuildGeminiService();
        BuildAmbientAudio();
        var infoPanel = BuildInfoPanel(mainCam != null ? mainCam.transform : null);
        var stage = BuildRevealStage(cameraOffset);
        var reveal = BuildRevealManager(stage, infoPanel, mainCam != null ? mainCam.transform : null);
        var box = BuildMysteryBox(cameraOffset, reveal, stage);
        BuildStartPanel(cameraOffset, box, mainCam != null ? mainCam.transform : null, stage);
        BuildWristMenu(cameraOffset, mainCam != null ? mainCam.transform : null, box);
        AttachGazeFallback(mainCam, box);

        // 산호 배치는 마지막에 — 프리팹 참조가 다 꽂힌 뒤여야 한다.
        if (reef != null) reef.Generate();

        ApplyReefGlow(reef, out var coralMat, out var seaweedMat);
        SetupTimeOfDay(coralMat, seaweedMat);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[DomeAquarium] 씬 빌드 완료 및 저장.");
    }

    [MenuItem("DomeAquarium/전체 실행 (에셋 준비 + FishData + 씬 빌드)", priority = 20)]
    public static void BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DomeAquarium] 플레이 모드에서는 실행할 수 없다. Play 를 멈추고 다시 실행할 것.");
            return;
        }

        DomeAquariumSetup.ConvertMaterialsToUrp();
        DomeAquariumSetup.CreateKoreanFontAsset();
        DomeAquariumGeminiSetup.SyncFromEnv();          // .env 키 -> Resources/GeminiSettings
        DomeAquariumUiSprites.ApplyBorders();           // 둥근 UI 스프라이트 9-슬라이스
        DomeAquariumParticleSetup.Run();                // 파티클이 흰 사각형으로 나오는 것 방지
        DomeAquariumPostFx.Configure();                 // 블룸 intensity 가 0 이라 발광이 안 보였다
        DomeAquariumAnimatorSetup.AttachAnimators();   // 프리팹에 Animator 가 하나도 없다 — 먼저 붙인다
        DomeAquariumSetup.GenerateFishData();
        DomeAquariumScaleSetup.ApplyScales();           // 어종별 실제 크기 반영
        AssetDatabase.Refresh();
        Build();
        DomeAquariumAnimatorSetup.ReportAnimatorState();
    }

    // ─────────────────────────────────────────────────────────────
    // 준비
    // ─────────────────────────────────────────────────────────────

    static void EnsureHandTag()
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (asset == null || asset.Length == 0) return;

        var so = new SerializedObject(asset[0]);
        var tags = so.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == HandTag) return;

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = HandTag;
        so.ApplyModifiedProperties();
        Debug.Log($"[DomeAquarium] '{HandTag}' 태그를 추가했다.");
    }

    /// <summary>
    /// 이전 빌드가 만든 오브젝트를 지운다.
    /// GameObject.Find 는 **비활성 오브젝트를 못 찾는다**. MysteryBox 는 비활성으로 만들어지므로
    /// Find 로 지우면 재실행할 때마다 중복이 쌓인다. 그래서 씬 루트부터 직접 순회한다.
    /// </summary>
    static void Cleanup()
    {
        var targets = new HashSet<string>(BuiltRootNames);
        var doomed = new List<GameObject>();

        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (targets.Contains(t.name)) doomed.Add(t.gameObject);
            }
        }

        // 부모가 먼저 지워지면 자식 참조가 죽으므로 null 검사를 하며 지운다.
        foreach (var go in doomed)
        {
            if (go == null) continue;
            Object.DestroyImmediate(go);
        }

        if (doomed.Count > 0) Debug.Log($"[DomeAquarium] 이전 빌드 오브젝트 {doomed.Count}개 정리.");
    }

    static void DisableTemplateContent()
    {
        var targets = new HashSet<string>(TemplateRootsToDisable);
        int n = 0, already = 0;

        // 여기서도 GameObject.Find 를 쓰면 안 된다 — 이미 껐던 것을 못 찾아 "없음"으로 잘못 보고한다.
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !targets.Contains(t.name)) continue;
                if (!t.gameObject.activeSelf) { already++; continue; }
                t.gameObject.SetActive(false);
                n++;
                Debug.Log($"[DomeAquarium] VR 템플릿 오브젝트 비활성: {t.name}");
            }
        }
        Debug.Log($"[DomeAquarium] VR 템플릿 정리 — 이번에 {n}개 비활성, 이미 꺼져 있던 것 {already}개.");
    }

    /// <summary>
    /// VR 템플릿이 남긴 컴포넌트들이 참조 대상을 잃고 매 프레임 NullReferenceException 을 던진다.
    /// 플레이 중 로그를 보면 프레임당 수십 건이 쌓이는데, Unity 에서 예외는 매우 비싸서
    /// 그것만으로 프레임이 튄다("삐걱대는 느낌"의 실제 원인).
    /// 우리 씬에서 쓰지 않는 것들이므로 컴포넌트를 비활성한다(삭제가 아니라 enabled=false).
    /// </summary>
    static void DisableThrowingTemplateBehaviours()
    {
        // 실제 스택 트레이스에서 확인된 것들 + 같은 계열
        var kill = new HashSet<string>
        {
            "LazyFollow",
            "AnchorVisuals",
            "Callout",
            "CalloutGazeController",
            "StepManager",
            "TutorialVideoPlayer",
            "VideoPlayerRenderTexture",
            "VideoTimeScrubControl",
            "UIComponentToggler",
            "ResetVideoOnLoop",
            "XRGazeAssistance",
        };

        int n = 0;
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;

                var type = mb.GetType();
                string tn = type.Name;
                string ns = type.Namespace ?? string.Empty;

                // 우리 스크립트는 절대 건드리지 않는다.
                if (type.Assembly.GetName().Name == "Assembly-CSharp" && ns.Length == 0 &&
                    !kill.Contains(tn)) continue;

                bool isAffordance = ns.Contains("AffordanceSystem");
                if (!kill.Contains(tn) && !isAffordance) continue;

                if (!mb.enabled) continue;
                mb.enabled = false;
                n++;
            }
        }
        Debug.Log($"[DomeAquarium] 예외를 던지던 VR 템플릿 컴포넌트 {n}개 비활성.");
    }

    /// <summary>
    /// VR 템플릿의 EventSystem 은 XRUIInputModule 의 마우스 입력을 꺼 둔다(m_EnableMouseInput: false).
    /// 그러면 에디터 게임뷰에서 UI 를 마우스로 클릭할 수 없다 —
    /// 캔버스에 GraphicRaycaster 를 아무리 붙여도 클릭 이벤트 자체가 들어오지 않는다.
    /// 헤드셋 빌드에서는 XR 입력만 쓰므로 켜 둬도 손해가 없고, 에디터 테스트가 훨씬 빨라진다.
    /// </summary>
    static void EnableEditorMouseInput()
    {
        var modules = Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (modules == null || modules.Length == 0)
        {
            Debug.LogWarning("[DomeAquarium] XRUIInputModule 을 찾지 못했다. 마우스 클릭 설정을 건너뛴다.");
            return;
        }

        foreach (var m in modules)
        {
            if (m.enableMouseInput && m.enableTouchInput) continue;
            m.enableMouseInput = true;
            m.enableTouchInput = true;
            EditorUtility.SetDirty(m);
            Debug.Log($"[DomeAquarium] {m.gameObject.name} 의 XRUIInputModule 에 마우스/터치 입력을 켰다.");
        }
    }

    /// <summary>수조 안이라는 느낌을 내는 최소한의 조명·안개 설정.</summary>
    static void ConfigureUnderwaterLighting()
    {
        // 수면에서 내려오는 빛 하나만 쓴다 — Quest 2 라 실시간 광원을 늘리지 않는다.
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        Light sun = lights.FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null)
        {
            var go = new GameObject("Directional Light");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.transform.rotation = Quaternion.Euler(78f, 20f, 0f);   // 거의 수직으로 내리쬐게
        sun.color = new Color(0.72f, 0.90f, 1.00f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.None;                            // 모바일 예산

        // 수면에서 굴절된 햇빛이 바닥에 만드는 그물 무늬(코스틱)를 라이트 쿠키로 넣는다.
        // 바닥뿐 아니라 물고기·바위에도 얹혀 수중 느낌을 크게 올려준다. 드로우콜은 0 개 늘어난다.
        var caustics = DomeAquariumCaustics.Generate();
        if (caustics != null)
        {
            sun.cookie = caustics;
            var anim = sun.GetComponent<CausticsAnimator>();
            if (anim == null) anim = sun.gameObject.AddComponent<CausticsAnimator>();
            anim.cookieSize = new Vector2(14f, 14f);
            Debug.Log("[DomeAquarium] 수중 코스틱 라이트 쿠키 배선 완료.");
        }

        EditorUtility.SetDirty(sun);

        // 나머지 광원은 꺼서 드로우콜/셰이딩 비용을 아낀다.
        foreach (var l in lights)
            if (l != sun && l.type != LightType.Directional) l.enabled = false;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.18f, 0.42f, 0.58f);
        RenderSettings.ambientEquatorColor = new Color(0.08f, 0.24f, 0.38f);
        RenderSettings.ambientGroundColor = new Color(0.02f, 0.08f, 0.14f);

        // 안개로 깊이감을 준다. 수조 반지름이 12m 이므로 그 너머가 서서히 흐려지게.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.05f, 0.20f, 0.32f);
        RenderSettings.fogDensity = 0.035f;

        Debug.Log("[DomeAquarium] 수중 조명·안개 설정 완료.");
    }

    static Transform PrepareXROrigin(out Camera mainCam)
    {
        mainCam = Object.FindFirstObjectByType<Camera>();
        var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var c in cams) if (c.CompareTag("MainCamera")) { mainCam = c; break; }

        if (mainCam == null) { Debug.LogError("[DomeAquarium] Main Camera 없음."); return null; }

        // (6) Clipping Far >= 40 — 수조 반대편 벽이 24m 밖이다.
        mainCam.farClipPlane = Mathf.Max(mainCam.farClipPlane, 60f);
        mainCam.nearClipPlane = Mathf.Min(mainCam.nearClipPlane, 0.05f);

        // 수조가 시야를 다 덮지만, 틈이 생겼을 때 하늘색이 새면 몰입이 깨진다.
        // 스카이박스 대신 단색으로 지워 모바일 GPU의 스카이박스 픽셀 비용도 없앤다.
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.02f, 0.09f, 0.16f, 1f);
        EditorUtility.SetDirty(mainCam);

        // XR Origin 루트 찾기
        Transform origin = mainCam.transform;
        while (origin.parent != null) origin = origin.parent;

        // ★ 리그를 월드 원점으로 되돌린다.
        // 이 앱의 설계 전제가 "플레이어는 원통 수조 정중앙에 고정"인데,
        // 리그가 (-1.169, 0, 0.841) 같은 곳에 놓여 있으면 그 전제가 깨진다.
        // 수조·산호·물고기 경계는 전부 월드 원점 기준이라 플레이어만 1.4m 옆에 서 있게 되고,
        // 눈앞에 배치하는 것들(시작 패널·상자)도 전부 어긋난다.
        // 눈높이는 Camera Offset 의 로컬 y 가 담당하므로 그대로 두면 된다.
        if (origin.position != Vector3.zero || origin.rotation != Quaternion.identity)
        {
            Debug.Log($"[DomeAquarium] XR Origin 을 원점으로 리셋 (이전 위치 {origin.position}, 회전 {origin.rotation.eulerAngles}).");
            origin.position = Vector3.zero;
            origin.rotation = Quaternion.identity;
        }
        origin.localScale = Vector3.one;

        // (5) 이동 컴포넌트 전부 제거
        int removed = 0;
        var all = origin.GetComponentsInChildren<Component>(true);
        foreach (var c in all)
        {
            if (c == null) continue;
            if (!LocomotionTypes.Contains(c.GetType().Name)) continue;
            Debug.Log($"[DomeAquarium] 이동 컴포넌트 제거: {c.GetType().Name} on {c.gameObject.name}");
            Object.DestroyImmediate(c);
            removed++;
        }

        // Locomotion 전용 오브젝트가 통째로 있으면 비활성
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            if (n == "locomotion" || n.Contains("teleport")) { t.gameObject.SetActive(false); removed++; }
        }
        Debug.Log($"[DomeAquarium] 이동 관련 {removed}건 정리.");

        // Camera Offset 확보
        Transform offset = mainCam.transform.parent != null ? mainCam.transform.parent : origin;

        // 손으로 인정할 오브젝트는 정확히 이 4개뿐이다.
        // 이름 부분일치로 잡으면 "XR Origin Hands (XR Rig)" 나 각종 Attach/Stabilized 자식까지
        // 11개가 딸려 들어와 한 번 찌를 때마다 트리거가 여러 번 겹친다.
        var handNames = new HashSet<string> { "Left Controller", "Right Controller", "Left Hand", "Right Hand" };

        // 이전 빌드가 잘못 붙여둔 태그/콜라이더를 전부 되돌린다.
        int reverted = 0;
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || handNames.Contains(t.name)) continue;
            bool touched = false;
            if (t.gameObject.CompareTag(HandTag)) { t.gameObject.tag = "Untagged"; touched = true; }
            var badCol = t.GetComponent<SphereCollider>();
            if (badCol != null && badCol.isTrigger && Mathf.Approximately(badCol.radius, 0.06f))
            { Object.DestroyImmediate(badCol); touched = true; }
            if (touched)
            {
                var badRb = t.GetComponent<Rigidbody>();
                if (badRb != null && badRb.isKinematic) Object.DestroyImmediate(badRb);
                Debug.Log($"[DomeAquarium] 손 오인 부착 되돌림: {t.name}");
                reverted++;
            }
        }

        // 컨트롤러/손에 Hand 태그 + 트리거 콜라이더 + 키네마틱 리지드바디
        int hands = 0;
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !handNames.Contains(t.name)) continue;

            t.gameObject.tag = HandTag;

            var col = t.GetComponent<SphereCollider>();
            if (col == null) col = t.gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.06f;

            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            hands++;
        }
        Debug.Log($"[DomeAquarium] 손 {hands}개에 '{HandTag}' 태그 + 트리거 콜라이더 부착 (오인 {reverted}개 되돌림).");
        if (hands == 0)
            Debug.LogWarning("[DomeAquarium] 손으로 인정된 오브젝트가 없다. XR Origin 자식 이름을 확인할 것 " +
                             "(기대: Left Controller / Right Controller / Left Hand / Right Hand).");

        return offset;
    }

    // ─────────────────────────────────────────────────────────────
    // 수조
    // ─────────────────────────────────────────────────────────────

    static void BuildWaterTank()
    {
        var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tank.name = "WaterTank";
        tank.transform.position = Vector3.zero;
        tank.transform.localScale = new Vector3(TankRadius * 2f, TankHeight / 2f, TankRadius * 2f);

        // (1) Collider 반드시 삭제 — 없으면 컨트롤러/레이가 벽에 먼저 막힌다.
        var col = tank.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
        if (mat == null)
        {
            var sh = Shader.Find("DomeAquarium/WaterTankInner");
            if (sh == null)
            {
                Debug.LogWarning("[DomeAquarium] WaterTankInner 셰이더를 찾지 못했다. URP/Lit 로 임시 대체.");
                sh = Shader.Find("Universal Render Pipeline/Lit");
            }
            mat = new Material(sh) { name = "Mat_WaterTankInner" };
            AssetDatabase.CreateAsset(mat, WaterMatPath);
            AssetDatabase.SaveAssets();
        }
        tank.GetComponent<MeshRenderer>().sharedMaterial = mat;
        tank.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tank.GetComponent<MeshRenderer>().receiveShadows = false;
    }

    const string CoralGlowMatPath = Root + "/Materials/Mat_CoralGlow.mat";
    const string SeaweedGlowMatPath = Root + "/Materials/Mat_SeaweedGlow.mat";

    /// <summary>
    /// 산호·해초에 은은한 자체 발광을 준다. 밤에 강해지도록 TimeOfDayController 가 강도를 조절한다.
    /// 공유 머티리얼 하나만 쓰므로 배칭도 유지되고, 런타임에 바꿀 대상도 하나뿐이다.
    /// </summary>
    static Material MakeGlowMaterial(string path, string name, Color tint)
    {
        var glow = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (glow == null)
        {
            // Layer Lab 프랍이 쓰는 팔레트 머티리얼을 복제해 텍스처를 그대로 이어받는다.
            var src = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Layer Lab/3D Characters-Fish/Fish/Materials/Color.mat");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            glow = src != null ? new Material(src) : new Material(sh);
            glow.name = name;
            AssetDatabase.CreateAsset(glow, path);
        }

        glow.shader = Shader.Find("Universal Render Pipeline/Lit");
        glow.EnableKeyword("_EMISSION");
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (glow.HasProperty("_EmissionColor")) glow.SetColor("_EmissionColor", tint * 0.6f);
        glow.enableInstancing = true;
        EditorUtility.SetDirty(glow);
        return glow;
    }

    /// <summary>
    /// 산호는 따뜻한 금빛, 해초는 차가운 청록으로 빛나게 한다 (사용자가 준 레퍼런스 톤).
    /// 종류별 공유 머티리얼 하나씩만 쓰므로 배칭이 유지되고, 시간대 컨트롤러가 강도만 바꾼다.
    /// </summary>
    static void ApplyReefGlow(ReefScatter reef, out Material coralMat, out Material seaweedMat)
    {
        coralMat = null; seaweedMat = null;
        if (reef == null) return;

        var generated = reef.transform.Find("Generated");
        if (generated == null)
        {
            Debug.LogWarning("[DomeAquarium] Generated 가 없어 발광을 건너뛴다.");
            return;
        }

        coralMat = MakeGlowMaterial(CoralGlowMatPath, "Mat_CoralGlow", new Color(1.00f, 0.72f, 0.30f));
        seaweedMat = MakeGlowMaterial(SeaweedGlowMatPath, "Mat_SeaweedGlow", new Color(0.25f, 0.75f, 1.00f));
        AssetDatabase.SaveAssets();

        // 바위·조개는 제외한다. 빛나면 어색하다.
        int coral = 0, weed = 0;
        foreach (var t in generated.GetComponentsInChildren<Transform>(true))
        {
            // ReefScatter 가 "R0_Coral_01" 처럼 링 번호를 붙이므로 StartsWith 로는 못 잡는다.
            string nm = t.name.ToLowerInvariant();
            Material use = null;
            if (nm.Contains("coral")) { use = coralMat; coral++; }
            else if (nm.Contains("seaweed")) { use = seaweedMat; weed++; }
            if (use == null) continue;

            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = use;
                r.sharedMaterials = mats;
            }
        }

        Debug.Log($"[DomeAquarium] 발광 적용 — 산호(금빛) {coral}개, 해초(청록) {weed}개.");
    }

    /// <summary>실제 시계에 맞춰 조명·안개·물색·산호 발광을 바꾸는 컨트롤러를 붙인다.</summary>
    static void SetupTimeOfDay(Material coralGlow, Material seaweedGlow)
    {
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                        .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) { Debug.LogWarning("[DomeAquarium] 디렉셔널 라이트가 없어 시간대 조명을 건너뛴다."); return; }

        var tod = sun.GetComponent<TimeOfDayController>();
        if (tod == null) tod = sun.gameObject.AddComponent<TimeOfDayController>();

        tod.sun = sun;
        tod.waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
        tod.coralGlowMaterial = coralGlow;
        tod.seaweedGlowMaterial = seaweedGlow;
        tod.useSystemClock = true;
        tod.keys = TimeOfDayController.DefaultKeys();
        EditorUtility.SetDirty(tod);

        Debug.Log($"[DomeAquarium] 시간대 조명 배선 완료 (현재 {tod.CurrentHour():0.0}시 기준으로 적용된다).");
    }

    const string AmbientClipPath =
        "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/DemoAssets/Audio/AmbientBackgroundNoise.mp3";
    const string PokeClipPath = "Assets/VRTemplateAssets/Audio/Button_22_click.wav";

    /// <summary>수조 배경음. 2D 로 깔아 어디를 봐도 같은 크기로 들리게 한다.</summary>
    static void BuildAmbientAudio()
    {
        var go = new GameObject("AmbientAudio");
        var src = go.AddComponent<AudioSource>();

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AmbientClipPath);
        if (clip == null)
        {
            Debug.LogWarning("[DomeAquarium] 배경음 클립을 찾지 못했다. 무음으로 둔다.");
            src.playOnAwake = false;
            return;
        }

        src.clip = clip;
        src.loop = true;
        src.playOnAwake = true;
        src.volume = 0.18f;          // 물속 웅웅거림 정도. 해설을 덮으면 안 된다
        src.spatialBlend = 0f;       // 2D — 머리를 돌려도 일정하게
        src.priority = 200;
        Debug.Log("[DomeAquarium] 배경음 배치 완료.");
    }

    /// <summary>어종 설명을 Gemini 로 받아 오는 서비스. 씬에 하나만 있으면 된다.</summary>
    static void BuildGeminiService()
    {
        var go = new GameObject("GeminiService");
        go.AddComponent<GeminiDescriptionService>();

        var cfg = Resources.Load<GeminiSettings>(GeminiSettings.ResourcePath);
        if (cfg == null)
            Debug.LogWarning("[DomeAquarium] GeminiSettings 가 없다. 메뉴 8번을 먼저 실행할 것. 지금은 설명 없이 동작한다.");
        else if (!cfg.IsUsable)
            Debug.LogWarning("[DomeAquarium] GeminiSettings 의 API 키가 비어 있다. 설명 없이 동작한다.");
        else
            Debug.Log("[DomeAquarium] Gemini 설명 서비스 배치 완료.");
    }

    static ReefScatter BuildReefFloor()
    {
        var go = new GameObject("ReefFloor");
        go.transform.position = Vector3.zero;
        var reef = go.AddComponent<ReefScatter>();
        reef.tankRadius = TankRadius;
        reef.floorY = FloorY;
        reef.terraceCount = 3;
        reef.centerClearRadius = 2.5f;
        reef.coralPrefabs = DomeAquariumSetup.LoadPropPrefabs("coral").ToArray();
        reef.rockPrefabs = DomeAquariumSetup.LoadPropPrefabs("rock").ToArray();
        reef.seaweedPrefabs = DomeAquariumSetup.LoadPropPrefabs("seaweed").ToArray();
        reef.shellPrefabs = DomeAquariumSetup.LoadPropPrefabs("shell")
                              .Concat(DomeAquariumSetup.LoadPropPrefabs("starfish")).ToArray();
        Debug.Log($"[DomeAquarium] ReefScatter 프리팹 — 산호 {reef.coralPrefabs.Length}, 바위 {reef.rockPrefabs.Length}, 해초 {reef.seaweedPrefabs.Length}, 조개 {reef.shellPrefabs.Length}");
        return reef;
    }

    static BoidManager BuildFishSwarm(Transform player)
    {
        var go = new GameObject("FishSwarm");
        var boids = go.AddComponent<BoidManager>();
        boids.shape = BoidShape.Cylinder;
        boids.radius = TankRadius;
        boids.height = TankHeight;
        boids.floorClearance = 0.8f;
        boids.playerClearance = 1.2f;
        boids.updateStride = 4;
        boids.maxAgents = 160;   // 종별 배분 합계보다 살짝 위 — 비례 축소가 걸리지 않게
        // 상단 캡을 뚫지 않도록 위쪽에도 여유를 준다 (가장 큰 모델의 반높이 기준).
        boids.surfaceClearance = 0.5f;
        boids.player = player;

        // 같은 종끼리 한 덩어리로 뭉치던 문제 — 응집을 낮추고 분리를 올린다.
        // 좁은 수직 밴드 안에서는 같은 종 전원이 서로의 이웃이라 응집이 항상 최대로 걸린다.
        boids.cohesionWeight = 0.18f;
        boids.cohesionInnerRadius = 2.0f;   // 이 안에서는 응집 0 — 한 점으로 수렴하는 걸 막는다
        boids.separationWeight = 2.2f;
        boids.separationRadius = 1.2f;      // 폴백값. 실제로는 개체별 체장 x3 이 쓰인다
        boids.alignmentWeight = 0.55f;      // 정렬이 응집보다 세면 무리가 강체처럼 통째로 움직인다
        boids.turnResponse = 5f;            // 낮출수록 방향 전환이 부드럽다

        // 이웃 스캔 절단이 셀 순회 순서(y 오름차순) 때문에 아래쪽 이웃만 남겨
        // 무리 전체가 밴드 바닥으로 가라앉는 편향이 있었다. 예산을 올려 절단 자체를 드물게 만든다.
        boids.neighborScanBudget = 48;

        var all = AssetDatabase.FindAssets("t:FishData", new[] { Root + "/FishData" })
            .Select(g => AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null && d.prefab != null)
            .OrderBy(d => d.bandCenter).ThenBy(d => d.name)
            .ToList();

        // ★ 앞선 판단을 정정한다.
        // 물고기 한 마리 = SkinnedMeshRenderer 하나 = 드로우콜 하나다. 배칭도 인스턴싱도 안 된다.
        // 즉 드로우콜은 "종 수"가 아니라 "총 마릿수"에만 비례한다. 종을 12개로 줄인 건
        // 성능에 아무 도움이 안 되면서 볼거리만 없앤 잘못된 최적화였다.
        //
        // 그래서 반대로 간다: 종은 많이(볼거리), 총 마릿수는 줄여서(성능) 잡는다.
        // 그리고 어종 습성에 맞게 종별 마릿수를 다르게 준다 — 상어 16마리가 떼로 다니면 이상하다.
        var species = PickSpeciesForExhibit(all, out int totalAgents);
        boids.species = species;

        Debug.Log($"[DomeAquarium] BoidManager — 유영 {species.Length}종 / 총 {totalAgents}마리 " +
                  $"(드로우콜은 마릿수에 비례하므로 이전 192 → {totalAgents}). 뽑기 풀은 전체 {all.Count}종 유지.");
        return boids;
    }

    /// <summary>습성 그룹별 목표 마릿수. 무리 어종은 많이, 대형 단독 어종은 적게.</summary>
    static int SchoolSizeFor(string name)
    {
        string s = name.ToLowerInvariant();

        // 단독 대형 — 한 마리씩만
        if (s.Contains("shark") || s.Contains("dolphin") || s.Contains("dophin") ||
            s.Contains("turtle") || s.Contains("mantaray") || s.Contains("eagleray")) return 1;
        if (s.Contains("ray") || s.Contains("grouper")) return 1;

        // 대형 회유 — 소규모 무리
        if (s.Contains("tuna")) return 3;

        // 중형 — 몇 마리씩
        if (s.Contains("tang") || s.Contains("angelfish") || s.Contains("butterfly") ||
            s.Contains("idol") || s.Contains("lobster") || s.Contains("tilefish")) return 3;

        // 소형 무리 어종
        if (s.Contains("chromis") || s.Contains("damsel")) return 8;
        if (s.Contains("clownfish") || s.Contains("cardinal") || s.Contains("goby") ||
            s.Contains("basslet") || s.Contains("dottyback") || s.Contains("dartfish") ||
            s.Contains("seahorse")) return 5;

        return 4;
    }

    /// <summary>
    /// 전시용 종 선정. 관람객이 기대하는 대형 어종을 반드시 넣고,
    /// 나머지는 수직 층위가 고르게 퍼지도록 채운다.
    /// </summary>
    static FishData[] PickSpeciesForExhibit(List<FishData> sortedByBand, out int totalAgents)
    {
        const int AgentBudget = 150;      // 총 마릿수 상한 (= 드로우콜 예산)

        // 반드시 들어가야 하는 "볼거리" 종. 이게 빠져서 상어가 사라졌었다.
        string[] mustHave =
        {
            "GreateWhiteShark", "BlacktipReefShark", "NurseShark",
            "MantaRay", "SpottedEagleRay",
            "GreenTurtle", "HawksbillTurtle",
            "HectorDolphin", "PinkDolphin",
            "BluefinTuna",
            "Clownfish", "TomatoClownfish",
            "BlueTang", "YellowTang", "MoorishIdol",
            "GreenChromis", "DominoDamsel",
            "FireGoby", "BlackSeahorse", "BlueLobster",
            "QueenAngelfish", "CopperbandButterflyfish",
        };

        var byName = new Dictionary<string, FishData>();
        foreach (var d in sortedByBand) byName[d.name] = d;

        var picked = new List<FishData>();
        foreach (string n in mustHave)
            if (byName.TryGetValue(n, out var d) && !picked.Contains(d)) picked.Add(d);

        // 남은 예산만큼 수직 층위가 고르게 퍼지도록 더 채운다.
        int used = 0;
        foreach (var d in picked) used += SchoolSizeFor(d.name);

        var rest = sortedByBand.Where(d => !picked.Contains(d)).ToList();
        int stride = Mathf.Max(1, rest.Count / 24);       // 밴드 순으로 훑으며 고르게
        for (int i = 0; i < rest.Count; i += stride)
        {
            int cost = SchoolSizeFor(rest[i].name);
            if (used + cost > AgentBudget) continue;
            picked.Add(rest[i]);
            used += cost;
        }

        // 종별 마릿수를 실제로 기록한다.
        foreach (var d in picked)
        {
            d.schoolCount = SchoolSizeFor(d.name);
            EditorUtility.SetDirty(d);
        }
        AssetDatabase.SaveAssets();

        totalAgents = used;
        return picked.ToArray();
    }

    // ─────────────────────────────────────────────────────────────
    // 연출 / UI
    // ─────────────────────────────────────────────────────────────

    static Transform BuildRevealStage(Transform cameraOffset)
    {
        // 씬 루트에 둔다. 시작 연출이 끝날 때 StartPanel 이 "사용자가 실제로 보는 방향" 앞으로 옮긴다.
        // 카메라 오프셋 자식으로 두면 리그의 +Z 에 고정되는데, 그건 사용자 정면이 아니다.
        var go = new GameObject("RevealStage");
        go.transform.position = new Vector3(0f, 1.2f, 1.2f);
        return go.transform;
    }

    static FishReveal BuildRevealManager(Transform stage, FishInfoPanel panel, Transform player)
    {
        var go = new GameObject("FishRevealManager");
        var rev = go.AddComponent<FishReveal>();
        rev.stagePoint = stage;
        rev.infoPanel = panel;
        rev.player = player;
        // 수조 반지름이 12m 다. 14m 로 두면 물고기가 유리벽 바깥에서 스며 나온다.
        rev.spawnDistance = 9f;
        rev.approachTime = 2.2f;
        rev.holdTime = 6f;
        rev.exitTime = 2.0f;
        return rev;
    }

    static MysteryBox BuildMysteryBox(Transform cameraOffset, FishReveal reveal, Transform stage)
    {
        // 씬 루트에 둔다. StartPanel 이 시작 연출 끝에 사용자 정면으로 옮긴 뒤 켠다.
        var root = new GameObject("MysteryBox");
        root.transform.position = new Vector3(0f, 1.1f, 0.55f);   // 임시값 — 실제 배치는 StartPanel 이 한다

        // RPG Pack 의 판도라 상자를 쓴다. 자체 Animator + 별 파티클이 들어 있어
        // 흰 큐브보다 "만져 보고 싶은" 물건이 된다. 없으면 큐브로 폴백.
        GameObject visual = null;
        var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Pack/Prefabs/BoxOfPandora.prefab");
        if (boxPrefab != null)
        {
            visual = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 0.25f;
            visual.transform.localPosition = new Vector3(0f, -0.1f, 0f);   // 트리거 중심에 맞춤
        }
        else
        {
            Debug.LogWarning("[DomeAquarium] BoxOfPandora 프리팹이 없다. 임시 큐브로 대체.");
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 0.2f;
        }
        visual.name = "BoxVisual";

        // 상자 자체의 콜라이더는 전부 끈다 — 판정은 부모의 트리거 하나가 담당한다.
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        var fx = new GameObject("BurstFX");
        fx.transform.SetParent(root.transform, false);
        var ps = fx.AddComponent<ParticleSystem>();
        ConfigureBurst(ps);

        var box = root.AddComponent<MysteryBox>();
        box.boxVisual = visual.transform;
        box.burstFx = ps;
        box.reveal = reveal;
        box.noRepeatWindow = 3;

        // 프리팹 안에 멀쩡한 상자(Box001)와 부서진 조각 더미(Destroy_box)가 둘 다 들어 있고
        // 기본적으로 둘 다 켜져 있다. 그대로 두면 상자가 두 개 겹쳐 보인다
        // (하나는 애니메이션으로 들썩이고 하나는 가만히 있는 조각 더미).
        foreach (var t in visual.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Box001") box.intactBox = t.gameObject;
            else if (t.name == "Destroy_box") box.shatteredBox = t.gameObject;
        }
        if (box.shatteredBox != null) box.shatteredBox.SetActive(false);
        Debug.Log($"[DomeAquarium] 상자 파츠 — 멀쩡={(box.intactBox != null)}, 조각더미={(box.shatteredBox != null)} (조각은 숨김)");

        // 판도라 상자의 유휴/터짐 애니메이션 배선
        box.boxAnimator = visual.GetComponentInChildren<Animator>(true);
        box.idleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/RPG Pack/Animations/BoxIdleAC.controller");
        box.crashController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/RPG Pack/Animations/BoxCrashAC.controller");
        if (box.boxAnimator != null && box.idleController != null)
            box.boxAnimator.runtimeAnimatorController = box.idleController;
        Debug.Log($"[DomeAquarium] 미스터리 박스 — Animator={(box.boxAnimator != null)}, idle={(box.idleController != null)}, crash={(box.crashController != null)}");
        box.player = cameraOffset != null ? cameraOffset.GetComponentInChildren<Camera>()?.transform : null;
        box.summonDistance = 0.55f;
        box.summonDrop = 0.25f;
        // 상자와 연출 무대를 같은 방향으로 함께 옮기게 한다.
        // 이 배선이 빠지면 물고기가 월드 +Z 쪽 — 최악의 경우 사용자 등 뒤 — 에서 나타난다.
        box.revealStage = stage;
        box.stageDistance = 1.2f;
        box.stageDrop = 0.15f;

        box.pool = AssetDatabase.FindAssets("t:FishData", new[] { Root + "/FishData" })
            .Select(g => AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null && d.prefab != null)
            .ToArray();

        // 상자 효과음 — 3D 로 두어 상자 쪽에서 나게 한다.
        var sfx = root.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;
        sfx.minDistance = 0.3f;
        sfx.maxDistance = 6f;
        sfx.volume = 0.7f;
        box.sfx = sfx;
        box.pokeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(PokeClipPath);
        if (box.pokeClip == null) Debug.LogWarning("[DomeAquarium] 상자 효과음 클립을 찾지 못했다.");

        // isTrigger = false 인 이유:
        //  - 컨트롤러 레이(Physics.Raycast)는 기본적으로 트리거를 무시한다. 레이로 누르려면 solid 여야 한다.
        //  - 그래도 포크는 여전히 동작한다. 손 콜라이더가 트리거 + 키네마틱 리지드바디라
        //    "Static Collider vs Kinematic Rigidbody Trigger" 조합으로 OnTriggerEnter 가 양쪽에 온다.
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = false;
        trigger.size = Vector3.one * 0.3f;

        var poke = root.AddComponent<PokeSelector>();
        poke.mysteryBox = box;
        poke.handTag = HandTag;
        poke.inputCooldown = 1.5f;

        // 손을 뻗어 찌르는 것 외에, 가리키고 트리거를 당기는 경로도 열어 둔다.
        // 팔이 짧거나 앉아 있으면 포크가 안 닿을 수 있다.
        var interactable = root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        UnityEventTools.AddVoidPersistentListener(interactable.selectEntered, box.Trigger);

        root.SetActive(false);   // StartPanel 이 켠다
        return box;
    }

    static void ConfigureBurst(ParticleSystem ps)
    {
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.9f;
        main.startSpeed = 1.6f;
        main.startSize = 0.03f;
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            const string matPath = Root + "/Materials/Mat_BurstFX.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null)
                {
                    mat = new Material(sh) { name = "Mat_BurstFX" };
                    AssetDatabase.CreateAsset(mat, matPath);
                }
            }
            if (mat != null)
            {
                // 텍스처가 없으면 파티클이 흰 사각형으로 그려진다.
                var dot = DomeAquariumParticleSetup.EnsureSoftDotTexture();
                if (dot != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", dot);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", dot);
                }
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);   // Additive
                mat.renderQueue = 3000;
                EditorUtility.SetDirty(mat);
                renderer.sharedMaterial = mat;
            }
        }
    }

    static FishInfoPanel BuildInfoPanel(Transform player)
    {
        var root = new GameObject("InfoPanelRoot");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = null;                        // (3) World Space 에서는 비워둔다
        root.AddComponent<CanvasScaler>();
        var raycaster = root.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;                        // 읽기 전용 패널

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(600f, 400f);
        rootRt.localScale = Vector3.one * 0.001f;         // (2) 안 하면 패널이 수조만큼 커진다

        var panel = NewUI("Panel", root.transform, new Vector2(600f, 400f));
        var bg = panel.gameObject.AddComponent<Image>();
        SkinImage(bg, LoadUiSprite("UIPanel.png"), new Color(0.03f, 0.09f, 0.16f, 0.75f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();   // (4) 페이드용
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var font = DomeAquariumSetup.LoadKoreanFont();

        // 희귀도 줄
        var rarityRow = NewUI("RarityRow", panel, new Vector2(560f, 44f));
        rarityRow.anchoredPosition = new Vector2(0f, 158f);

        var glow = NewUI("RarityGlow", rarityRow, new Vector2(220f, 44f));
        glow.anchoredPosition = new Vector2(-170f, 0f);
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.color = new Color(1f, 0.85f, 0.35f, 0.35f);
        glow.gameObject.SetActive(false);

        var badge = NewUI("RarityBadge", rarityRow, new Vector2(18f, 18f));
        badge.anchoredPosition = new Vector2(-258f, 0f);
        var badgeImg = badge.gameObject.AddComponent<Image>();
        badgeImg.color = Color.white;

        var rarityLabel = NewText("RarityLabel", rarityRow, new Vector2(240f, 32f), 26f, font);
        rarityLabel.rectTransform.anchoredPosition = new Vector2(-110f, 0f);
        rarityLabel.alignment = TextAlignmentOptions.Left;
        rarityLabel.color = new Color(0.75f, 0.85f, 0.95f);

        var nameText = NewText("NameText", panel, new Vector2(560f, 64f), 48f, font);
        nameText.rectTransform.anchoredPosition = new Vector2(0f, 100f);
        nameText.alignment = TextAlignmentOptions.Left;
        nameText.color = Color.white;

        var sciText = NewText("ScientificText", panel, new Vector2(560f, 34f), 24f, font);
        sciText.rectTransform.anchoredPosition = new Vector2(0f, 58f);
        sciText.alignment = TextAlignmentOptions.Left;
        sciText.fontStyle = FontStyles.Italic;
        sciText.color = new Color(0.62f, 0.70f, 0.78f);

        var divider = NewUI("Divider", panel, new Vector2(540f, 2f));
        divider.anchoredPosition = new Vector2(0f, 34f);
        divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.3f);

        // ★ 30 미만으로 내리지 말 것 — VR 최소 가독 크기
        var desc = NewText("DescriptionText", panel, new Vector2(540f, 150f), 30f, font);
        desc.rectTransform.anchoredPosition = new Vector2(0f, -48f);
        desc.alignment = TextAlignmentOptions.TopLeft;
        desc.color = new Color(0.90f, 0.94f, 0.98f);
        // 워드랩은 TMP 기본값이 켜짐이다. enableWordWrapping 은 버전에 따라 Obsolete 라 건드리지 않는다.

        var tagRow = NewUI("TagRow", panel, new Vector2(540f, 34f));
        tagRow.anchoredPosition = new Vector2(0f, -158f);

        var sizeTag = NewText("SizeTag", tagRow, new Vector2(260f, 32f), 24f, font);
        sizeTag.rectTransform.anchoredPosition = new Vector2(-138f, 0f);
        sizeTag.alignment = TextAlignmentOptions.Left;
        sizeTag.color = new Color(0.66f, 0.78f, 0.88f);

        var habitatTag = NewText("HabitatTag", tagRow, new Vector2(260f, 32f), 24f, font);
        habitatTag.rectTransform.anchoredPosition = new Vector2(138f, 0f);
        habitatTag.alignment = TextAlignmentOptions.Left;
        habitatTag.color = new Color(0.66f, 0.78f, 0.88f);

        var info = root.AddComponent<FishInfoPanel>();
        info.canvasGroup = group;
        info.nameText = nameText;
        info.scientificText = sciText;
        info.descriptionText = desc;
        info.sizeTagText = sizeTag;
        info.habitatTagText = habitatTag;
        info.rarityLabel = rarityLabel;
        info.rarityBadge = badgeImg;
        info.rarityGlow = glowImg;
        info.player = player;
        info.dropBelowFish = 0.3f;
        info.rarityColors = new[]
        {
            new Color(0.78f, 0.82f, 0.85f),   // Common
            new Color(0.45f, 0.85f, 0.55f),   // Uncommon
            new Color(0.35f, 0.65f, 0.95f),   // Rare
            new Color(0.72f, 0.45f, 0.95f),   // Epic
            new Color(1.00f, 0.78f, 0.25f),   // Legendary
        };
        info.rarityNames = new[] { "일반", "고급", "희귀", "영웅", "전설" };

        return info;
    }

    static void BuildStartPanel(Transform cameraOffset, MysteryBox box, Transform player, Transform stage)
    {
        // ★ 손이 닿아야 한다. 지시서의 1.35m 는 팔 길이(약 0.6~0.7m)를 넘어서 찌를 수가 없다.
        // 패널을 0.6m 로 당기고, 그만큼 크기를 줄여 시야를 안 덮게 한다.
        var root = new GameObject("StartPanelRoot");
        root.transform.SetParent(cameraOffset, false);
        root.transform.localPosition = new Vector3(0f, 1.25f, 0.6f);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // 지시서는 "Render Camera 는 비워둔다"라고 하지만, XRI 의 TrackedDeviceGraphicRaycaster 를
        // 쓸 때는 명시해야 한다. 비워 두면 eventCamera 가 Camera.main 폴백이 되는데,
        // 그 값이 등록/해제 시점에 달라져 OnDisable 에서 KeyNotFoundException 이 난다.
        canvas.worldCamera = player != null ? player.GetComponent<Camera>() : null;
        root.AddComponent<CanvasScaler>();

        // 컨트롤러 레이용. GraphicRaycaster 를 같이 붙이면 안 된다 —
        // 한 캔버스에 레이캐스터가 둘이면 XRI 내부 딕셔너리가 깨져 KeyNotFoundException 이 쏟아진다.
        // 에디터 마우스 클릭은 PokeSelector 의 에디터 전용 레이캐스트가 담당한다.
        root.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(600f, 400f);
        // 0.6m 로 당겼으므로 조금 줄인다 (0.48 x 0.32m). 안 줄이면 시야를 다 덮는다.
        rootRt.localScale = Vector3.one * 0.0008f;

        var panel = NewUI("Panel", root.transform, new Vector2(600f, 400f));
        SkinImage(panel.gameObject.AddComponent<Image>(), LoadUiSprite("UIPanel.png"),
                  new Color(0.03f, 0.10f, 0.18f, 0.85f));
        var group = panel.gameObject.AddComponent<CanvasGroup>();

        var font = DomeAquariumSetup.LoadKoreanFont();

        var title = NewText("TitleText", panel, new Vector2(560f, 80f), 56f, font);
        title.rectTransform.anchoredPosition = new Vector2(0f, 110f);
        title.text = "해양생물관";
        title.color = Color.white;

        var sub = NewText("SubText", panel, new Vector2(560f, 90f), 28f, font);
        sub.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        sub.text = "눈앞의 상자를 컨트롤러로 건드리면\n바다 친구가 찾아옵니다";
        sub.color = new Color(0.78f, 0.86f, 0.93f);

        var button = NewUI("StartButton", panel, new Vector2(320f, 100f));
        button.anchoredPosition = new Vector2(0f, -110f);
        var btnImg = button.gameObject.AddComponent<Image>();
        SkinImage(btnImg, LoadUiSprite("UIButtonDefault.png"), new Color(0.13f, 0.45f, 0.80f, 0.95f));

        var label = NewText("Label", button, new Vector2(320f, 100f), 34f, font);
        label.text = "시작하기";
        label.color = Color.white;

        // 컨트롤러 레이로 누르는 정식 경로. 이게 없으면 레이가 맞아도 반응할 대상이 없다.
        var btn = button.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.30f, 0.62f, 0.95f, 1f);   // 레이가 올라가면 밝아진다
        colors.pressedColor = new Color(0.08f, 0.30f, 0.58f, 1f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;

        // World Space Canvas 안의 버튼도 컨트롤러로 "찔러서" 누른다.
        // Canvas 스케일이 0.001 이므로 콜라이더는 로컬 단위(260x80)로 잡아야 실제 0.26m x 0.08m 가 된다.
        // 콜라이더는 캔버스 로컬 단위(1 = 0.001m). 깊이를 넉넉히 줘서 어느 각도로 찔러도 걸리게 한다.
        // 얇게(40 = 4cm) 두면 빠르게 통과할 때 트리거를 놓친다.
        var btnCol = button.gameObject.AddComponent<BoxCollider>();
        btnCol.isTrigger = true;
        btnCol.size = new Vector3(340f, 150f, 300f);   // 0.34 x 0.15 x 0.30 m

        var btnRb = button.gameObject.AddComponent<Rigidbody>();
        btnRb.isKinematic = true;
        btnRb.useGravity = false;

        var startPanel = root.AddComponent<StartPanel>();
        startPanel.canvasGroup = group;
        startPanel.mysteryBoxRoot = box != null ? box.gameObject : null;
        startPanel.player = player;
        startPanel.revealStage = stage;
        // 읽기 좋으면서 손도 닿는 절충점. 지시서의 1.35m 는 팔 길이를 넘어 포크가 불가능하다.
        // 컨트롤러 레이는 거리와 무관하게 되므로, 이 값은 "찔러서도 눌러지는가" 기준으로 잡는다.
        startPanel.distance = 0.75f;

        // 시작 버튼은 MysteryBox 가 아니라 onPoked 이벤트만 쓴다.
        var poke = button.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = HandTag;
        poke.inputCooldown = 1.5f;
        UnityEventTools.AddPersistentListener(poke.onPoked, startPanel.BeginExperience);

        // 컨트롤러 레이 클릭 경로 — 손을 뻗지 않아도 가리키고 트리거를 당기면 시작된다.
        UnityEventTools.AddPersistentListener(btn.onClick, startPanel.BeginExperience);

        // 안전망 — 버튼을 정확히 못 맞춰도 패널 아무 데나 건드리면 시작된다.
        // 시작 말고는 할 수 있는 게 없는 화면이라 오작동해도 손해가 없다.
        var panelRb = panel.gameObject.AddComponent<Rigidbody>();
        panelRb.isKinematic = true;
        panelRb.useGravity = false;

        var panelCol = panel.gameObject.AddComponent<BoxCollider>();
        panelCol.isTrigger = true;
        panelCol.size = new Vector3(620f, 420f, 260f);

        var panelPoke = panel.gameObject.AddComponent<PokeSelector>();
        panelPoke.mysteryBox = null;
        panelPoke.handTag = HandTag;
        panelPoke.inputCooldown = 1.5f;
        UnityEventTools.AddPersistentListener(panelPoke.onPoked, startPanel.BeginExperience);
    }

    /// <summary>
    /// 손목 메뉴 — 손목을 돌려 얼굴 쪽을 보면 나타나는 "다시 뽑기" 버튼.
    /// 상자를 계속 띄워 두지 않고 필요할 때만 부르기 위한 입구다.
    /// </summary>
    static void BuildWristMenu(Transform cameraOffset, Transform player, MysteryBox box)
    {
        if (cameraOffset == null) return;

        var old = GameObject.Find("WristMenu");
        if (old != null) Object.DestroyImmediate(old);

        // 왼손을 찾는다. 없으면 오른손, 그것도 없으면 WristMenu 가 시야 아래 폴백으로 붙는다.
        Transform hand = null;
        foreach (var t in cameraOffset.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Left Controller") { hand = t; break; }
            if (t.name == "Left Hand" && hand == null) hand = t;
        }
        if (hand == null)
        {
            foreach (var t in cameraOffset.GetComponentsInChildren<Transform>(true))
                if (t.name == "Right Controller") { hand = t; break; }
        }

        var root = new GameObject("WristMenu");
        root.transform.SetParent(cameraOffset, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // StartPanelRoot 과 같은 이유로 카메라를 명시한다 (OnDisable 의 KeyNotFoundException 방지).
        canvas.worldCamera = player != null ? player.GetComponent<Camera>() : null;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300f, 130f);

        // ★ 스케일과 초기 위치를 빌드 시점에 맞춰 둔다.
        // WristMenu 는 LateUpdate 에서 자세를 잡는데 그건 플레이 중에만 돈다.
        // 안 맞춰 두면 에디트 모드 씬 뷰에서 300m x 130m 짜리 판이 하늘을 덮는다.
        const float WristScale = 0.0007f;               // 300 x 0.0007 = 0.21m, 손목에 얹기 적당
        rt.localScale = Vector3.one * WristScale;
        rt.localPosition = new Vector3(-0.25f, 0.95f, 0.25f);   // 왼손이 있을 법한 자리

        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;                               // 손목을 돌리기 전에는 보이지 않는다
        group.interactable = false;
        group.blocksRaycasts = false;

        var font = DomeAquariumSetup.LoadKoreanFont();

        var btn = NewUI("SummonButton", root.transform, new Vector2(300f, 130f));
        var img = btn.gameObject.AddComponent<Image>();
        SkinImage(img, LoadUiSprite("UIButtonDefault.png"), new Color(0.10f, 0.42f, 0.72f, 0.95f));

        var label = NewText("Label", btn, new Vector2(300f, 130f), 40f, font);
        label.text = "다시 뽑기";
        label.color = Color.white;

        var uiBtn = btn.gameObject.AddComponent<Button>();
        uiBtn.targetGraphic = img;
        var colors = uiBtn.colors;
        colors.highlightedColor = new Color(0.28f, 0.60f, 0.92f, 1f);
        colors.pressedColor = new Color(0.06f, 0.28f, 0.52f, 1f);
        colors.fadeDuration = 0.08f;
        uiBtn.colors = colors;

        // 찌르기 / 에디터 마우스용
        var col = btn.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(320f, 150f, 260f);

        var rb = btn.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var poke = btn.gameObject.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = HandTag;
        poke.inputCooldown = 1.0f;

        if (box != null)
        {
            UnityEventTools.AddPersistentListener(uiBtn.onClick, box.Summon);
            UnityEventTools.AddPersistentListener(poke.onPoked, box.Summon);
        }

        var menu = root.AddComponent<WristMenu>();
        menu.anchor = hand;
        menu.viewer = player;
        menu.group = group;
        menu.scale = WristScale;   // 런타임에도 같은 크기를 유지한다
        menu.armed = false;        // 시작 연출이 끝나야 켜진다

        // 시작 패널이 끝날 때 손목 메뉴를 열도록 연결한다.
        // 이 배선이 없으면 메뉴가 시작 화면 위에 겹쳐 그려지고 클릭까지 가로챈다.
        var startPanel = Object.FindFirstObjectByType<StartPanel>();
        if (startPanel != null) startPanel.wristMenu = menu;
        else Debug.LogWarning("[DomeAquarium] StartPanel 을 찾지 못해 손목 메뉴 활성화를 연결하지 못했다.");

        Debug.Log($"[DomeAquarium] 손목 메뉴 배치 완료 (앵커: {(hand != null ? hand.name : "없음 — 시야 하단 폴백")}).");
    }

    static void AttachGazeFallback(Camera cam, MysteryBox box)
    {
        if (cam == null) return;

        // 폴백용 레티클. 시작 시 비활성이며 GazeSelector 를 켤 때만 쓰인다.
        var old = cam.transform.Find("ReticleCanvas");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var canvasGo = new GameObject("ReticleCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(cam.transform, false);
        canvasGo.transform.localPosition = new Vector3(0f, 0f, 1.5f);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = null;
        canvasGo.AddComponent<CanvasScaler>();

        var crt = canvasGo.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(120f, 120f);
        crt.localScale = Vector3.one * 0.001f;

        var ring = NewUI("ReticleRing", canvasGo.transform, new Vector2(96f, 96f));
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.color = new Color(1f, 1f, 1f, 0.25f);
        ringImg.raycastTarget = false;

        var fill = NewUI("ReticleFill", canvasGo.transform, new Vector2(96f, 96f));
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color(0.45f, 0.85f, 1f, 0.9f);
        fillImg.raycastTarget = false;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = (int)Image.Origin360.Top;
        fillImg.fillAmount = 0f;

        canvasGo.SetActive(false);   // 시작 시 비활성

        var gaze = cam.GetComponent<GazeSelector>();
        if (gaze == null) gaze = cam.gameObject.AddComponent<GazeSelector>();
        gaze.mysteryBox = box;
        gaze.dwellTime = 2f;
        gaze.maxDistance = 6f;
        gaze.reticleFill = fillImg;
        gaze.reticleRoot = canvasGo;
        gaze.enabled = false;    // 폴백 — 붙이되 비활성
    }

    // ─────────────────────────────────────────────────────────────
    // UI 헬퍼
    // ─────────────────────────────────────────────────────────────

    const string UiSamples = "Assets/Unity UI Samples/Textures and Sprites/Rounded UI";

    /// <summary>Unity UI Samples 의 둥근 스프라이트. 없으면 null 이라 기본 사각형으로 남는다.</summary>
    static Sprite LoadUiSprite(string fileName)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSamples}/{fileName}");
    }

    /// <summary>Image 에 9-슬라이스 스프라이트를 입힌다. 스프라이트가 없으면 색만 남긴다.</summary>
    static void SkinImage(Image img, Sprite sprite, Color color)
    {
        img.color = color;
        if (sprite == null) return;
        img.sprite = sprite;
        // 9-슬라이스로 늘려야 모서리 곡률이 유지된다.
        img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
    }

    static RectTransform NewUI(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, Vector2 size, float fontSize, TMP_FontAsset font)
    {
        var rt = NewUI(name, parent, size);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        if (font != null) t.font = font;
        return t;
    }
}
#endif
