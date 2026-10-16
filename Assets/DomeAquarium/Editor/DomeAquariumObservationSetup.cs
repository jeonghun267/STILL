#if UNITY_EDITOR
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// 1막 관찰 프로토타입 — 미스터리 상자를 끄고, 헤엄치는 생물을 오른손으로 겨눠 기록하는 관찰기(ObservationAim)를 씬에 놓는다.
/// 상자 쪽 코드는 지우지 않는다. "되돌리기" 메뉴로 예전 뽑기 흐름과 번갈아 플레이테스트할 수 있다.
/// </summary>
public static class DomeAquariumObservationSetup
{
    const string RootName = "ObservationAim";
    const string CirclePath = "Assets/VRTemplateAssets/Sprites/UI/Circle_60x60 Outline 4.png";
    const string RoundPath = DomeAquariumUiKit.PanelPath;   // 카드·일지와 같은 UI 키트

    // 도감 태블릿·정보 카드와 같은 청록 계열. Quest 디스플레이는 13/255 아래 단계를 구분하지 못해서(Meta 디스플레이 가이드)
    // 가장 어두운 채널도 그 위로 둔다 — 아래로 내려가면 어두운 물빛과 뭉개져 패널 경계가 사라진다.
    static readonly Color Deep = new Color(0.06f, 0.15f, 0.20f, 0.94f);
    static readonly Color Muted = new Color(0.62f, 0.76f, 0.82f, 1f);

    [MenuItem("DomeAquarium/17. 관찰기 적용 (상자 대신 직접 관찰)", priority = 17)]
    public static void ApplyMenu() { Apply(true); }

    [MenuItem("DomeAquarium/디버그/관찰기 끄고 상자 되돌리기", priority = 210)]
    public static void RevertMenu() { Revert(true); }

    public static ObservationAim Apply(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[관찰기] 플레이 모드에서는 적용할 수 없습니다.");
            return null;
        }

        var swarm = Object.FindFirstObjectByType<BoidManager>(FindObjectsInactive.Include);
        var info = Object.FindFirstObjectByType<FishInfoPanel>(FindObjectsInactive.Include);
        var log = Object.FindFirstObjectByType<FishEncyclopediaPanel>(FindObjectsInactive.Include);
        var start = Object.FindFirstObjectByType<StartPanel>(FindObjectsInactive.Include);
        var box = Object.FindFirstObjectByType<MysteryBox>(FindObjectsInactive.Include);
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        Camera cam = origin != null && origin.Camera != null ? origin.Camera : Camera.main;

        if (swarm == null || cam == null)
        {
            Debug.LogError("[관찰기] BoidManager 또는 Main Camera 를 찾지 못했습니다. 먼저 '4. 씬 빌드'를 실행하세요.");
            return null;
        }

        Transform offset = origin != null ? origin.transform.Find("Camera Offset") : null;
        Transform rightController = offset != null ? offset.Find("Right Controller") : null;
        Transform rightHand = offset != null ? offset.Find("Right Hand") : null;
        if (rightController == null) Debug.LogWarning("[관찰기] Right Controller 를 찾지 못했습니다. 손 추적·시선으로만 겨눕니다.");

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        var aim = root.AddComponent<ObservationAim>();
        aim.swarm = swarm;
        aim.viewer = cam.transform;
        aim.infoPanel = info;
        aim.logPanel = log;
        aim.aimSources = rightHand != null
            ? new[] { rightController, rightHand }
            : new[] { rightController };
        aim.haptics = rightController != null ? rightController.GetComponent<HapticImpulsePlayer>() : null;

        DomeAquariumUiKit.EnsureAll();
        TMP_FontAsset font = DomeAquariumSetup.LoadKoreanFont();
        Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath);
        if (circle == null) Debug.LogWarning($"[관찰기] 링 스프라이트가 없어 사각형으로 보입니다: {CirclePath}");

        BuildMarker(aim, root.transform, circle, font);
        BuildStatus(aim, root.transform, round, font);
        BuildAimLine(aim, root.transform, rightController);

        // 기록 클릭음은 생물 위치에서 울린다 (Meta 레이캐스트 가이드). 6m 거리에서도 들리도록 선형 감쇠로 둔다.
        var soundGo = new GameObject("RecordSound");
        soundGo.transform.SetParent(root.transform, false);
        var sfx = soundGo.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;
        sfx.rolloffMode = AudioRolloffMode.Linear;
        sfx.minDistance = 2f;
        sfx.maxDistance = 20f;
        sfx.volume = 0.8f;
        aim.sfx = sfx;
        aim.recordClip = box != null ? box.pokeClip : null;   // 상자를 찌를 때 쓰던 효과음을 셔터음으로 쓴다

        // 상자 대신 관찰기 — 수중 돔에 도착하면 상자를 부르지 않고 관찰기를 켠다.
        if (start != null)
        {
            Undo.RecordObject(start, "관찰기 적용");
            start.observationAim = aim;
            start.mysteryBoxRoot = null;
            EditorUtility.SetDirty(start);
        }
        else Debug.LogWarning("[관찰기] StartPanel 이 없어 관찰기를 켤 곳이 없습니다. armed 를 직접 켜야 합니다.");

        if (box != null) box.gameObject.SetActive(false);
        LayoutWristMenu(false);

        EditorUtility.SetDirty(aim);
        var scene = root.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);

        Debug.Log($"[관찰기] 적용 완료 — 유영 {swarm.species?.Length ?? 0}종을 오른손으로 겨눠 기록합니다. 상자는 꺼 두었습니다.");
        return aim;
    }

    public static void Revert(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[관찰기] 플레이 모드에서는 되돌릴 수 없습니다.");
            return;
        }

        var start = Object.FindFirstObjectByType<StartPanel>(FindObjectsInactive.Include);
        var box = Object.FindFirstObjectByType<MysteryBox>(FindObjectsInactive.Include);

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        if (start != null)
        {
            Undo.RecordObject(start, "관찰기 되돌리기");
            start.observationAim = null;
            start.mysteryBoxRoot = box != null ? box.gameObject : null;
            EditorUtility.SetDirty(start);
        }
        LayoutWristMenu(true);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        Debug.Log("[관찰기] 되돌림 — 수중 돔 도착 시 다시 상자를 부릅니다.");
    }

    // ─────────────────────────────────────────────────────────────
    // 겨눈 생물에 붙는 링: 테두리 + 진행 호 + 이름 줄
    // ─────────────────────────────────────────────────────────────

    static void BuildMarker(ObservationAim aim, Transform parent, Sprite circle, TMP_FontAsset font)
    {
        const float px = 256f;
        var go = new GameObject("ObservationMarker", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = go.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(px, px);
        crt.localScale = Vector3.one * 0.001f;   // 런타임에 거리에 맞춰 다시 잡는다

        var ring = NewUI("Ring", go.transform, new Vector2(px, px));
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = circle;
        ringImg.preserveAspect = true;
        ringImg.raycastTarget = false;
        ringImg.color = new Color(0.55f, 0.95f, 1f, 0.45f);

        var fill = NewUI("Fill", go.transform, new Vector2(px, px));
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = circle;
        fillImg.preserveAspect = true;
        fillImg.raycastTarget = false;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = (int)Image.Origin360.Top;
        fillImg.fillClockwise = true;
        fillImg.fillAmount = 0f;

        // 링이 시야각 3.5° 라 글자 96px ≈ 1.3° — VR 에서 편하게 읽히는 크기다.
        var label = NewText("Label", go.transform, new Vector2(1400f, 130f), 96f, font);
        label.rectTransform.anchoredPosition = new Vector2(0f, -px * 0.5f - 72f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.fontStyle = FontStyles.Bold;

        aim.marker = go.transform;
        aim.markerRing = ring;
        aim.markerFillRect = fill;
        aim.markerRingImage = ringImg;
        aim.markerFill = fillImg;
        aim.markerLabel = label;
        aim.markerCanvasPixels = px;
        go.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // 손 위에 뜨는 작은 관찰기 화면: 안내·방금 기록 + 이번 탐사 기록 수
    // ─────────────────────────────────────────────────────────────

    static void BuildStatus(ObservationAim aim, Transform parent, Sprite round, TMP_FontAsset font)
    {
        var go = new GameObject("ObservationStatus", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = go.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(560f, 150f);
        crt.localScale = Vector3.one * 0.0005f;   // 약 28 × 7.5cm

        var bg = NewUI("Background", go.transform, new Vector2(560f, 150f));
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = Deep;
        bgImg.raycastTarget = false;
        if (round != null)
        {
            bgImg.sprite = round;
            bgImg.type = round.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        }

        var status = NewText("Status", go.transform, new Vector2(520f, 64f), 32f, font);
        status.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        status.textWrappingMode = TextWrappingModes.NoWrap;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.text = ObservationTerms.AimHint;

        var count = NewText("Count", go.transform, new Vector2(520f, 50f), 26f, font);
        count.rectTransform.anchoredPosition = new Vector2(0f, -32f);
        count.color = Muted;
        count.text = ObservationTerms.SessionCount(0);

        aim.statusRoot = go.transform;
        aim.statusText = status;
        aim.countText = count;
        go.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // 조준선: 손에서 겨눈 생물까지 옅은 선. 5° 원뿔을 손짐작만으로 맞추기는 어렵다.
    // ─────────────────────────────────────────────────────────────

    static void BuildAimLine(ObservationAim aim, Transform parent, Transform rightController)
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = 0.006f;
        lr.endWidth = 0.002f;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = FindLineMaterial(rightController);
        lr.enabled = false;
        aim.aimLine = lr;
    }

    /// <summary>XRI 컨트롤러가 쓰는 선 머티리얼(정점 색 지원)을 빌려 쓴다. 씬 저장에 남으려면 에셋 머티리얼이어야 한다.</summary>
    static Material FindLineMaterial(Transform rightController)
    {
        if (rightController != null)
        {
            foreach (var lr in rightController.GetComponentsInChildren<LineRenderer>(true))
            {
                Material m = lr.sharedMaterial;
                if (m != null && AssetDatabase.Contains(m)) return m;
            }
        }
        return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
    }

    // ─────────────────────────────────────────────────────────────
    // 손목 메뉴: 상자를 부르는 버튼을 숨기고 "관찰 일지"만 가운데 둔다.
    // ─────────────────────────────────────────────────────────────

    static void LayoutWristMenu(bool withSummon)
    {
        var wrist = Object.FindFirstObjectByType<WristMenu>(FindObjectsInactive.Include);
        if (wrist == null) return;

        var wristRt = wrist.GetComponent<RectTransform>();
        var summon = wrist.transform.Find("SummonButton") as RectTransform;
        var log = wrist.transform.Find("EncyclopediaButton") as RectTransform;

        // 위치 값은 DomeAquariumEncyclopediaSetup.AddWristButton 과 같다.
        // 판·진행 막대는 관찰기 흐름에서만 쓴다 — 상자 흐름으로 되돌리면 숨기고, 다시 오면 15번(도감 태블릿)이 새로 만든다.
        if (wristRt != null) wristRt.sizeDelta = withSummon ? new Vector2(300f, 270f) : DomeAquariumEncyclopediaSetup.WristSize;
        if (summon != null) summon.gameObject.SetActive(withSummon);
        if (log != null) log.anchoredPosition = withSummon ? new Vector2(0f, -70f) : DomeAquariumEncyclopediaSetup.WristButtonPosition;
        foreach (string child in new[] { "WristPlate", "WristProgress" })
        {
            Transform t = wrist.transform.Find(child);
            if (t != null) t.gameObject.SetActive(!withSummon);
        }
        EditorUtility.SetDirty(wrist.gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    // 헤드셋 없이 플레이 모드에서 확인하는 도구
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/디버그/플레이 중 - 관찰기 자동 겨누기 켜기·끄기", priority = 206)]
    public static void ToggleDebugLock()
    {
        var aim = FindAimInPlayMode();
        if (aim == null) return;
        aim.debugLockNearest = !aim.debugLockNearest;
        Debug.Log($"[관찰기] 자동 겨누기 {(aim.debugLockNearest ? "켬 — 가장 가까운 미기록 생물을 바라본다" : "끔")}");
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 관찰기 상태 출력", priority = 207)]
    public static void LogState()
    {
        var aim = FindAimInPlayMode();
        if (aim != null) Debug.Log("[관찰기] " + aim.DebugState());
    }

    static ObservationAim FindAimInPlayMode()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[관찰기] 플레이 모드에서만 쓸 수 있다."); return null; }
        var aim = Object.FindFirstObjectByType<ObservationAim>();
        if (aim == null) Debug.LogWarning("[관찰기] 씬에 ObservationAim 이 없다. '17. 관찰기 적용'을 먼저 실행하세요.");
        return aim;
    }

    // ─────────────────────────────────────────────────────────────
    // UI 헬퍼
    // ─────────────────────────────────────────────────────────────

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
        t.color = Color.white;
        t.raycastTarget = false;
        if (font != null) t.font = font;
        return t;
    }
}
#endif
