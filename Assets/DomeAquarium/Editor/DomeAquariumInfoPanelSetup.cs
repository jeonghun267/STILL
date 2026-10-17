#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 이미 임포트된 VRTemplateAssets UI 스프라이트로 물고기 정보 카드를 정돈하고,
/// 현재 열린 씬에도 같은 스타일과 관람 배치를 적용한다.
///
/// 카드 v2 — 레이아웃 그룹으로 위에서 아래로 쌓는다.
/// v1 은 요소마다 좌표를 손으로 박아서, 제목 띠가 이름을 가로지르고 줄마다 왼쪽 여백이 달랐으며
/// 설명이 짧으면 아래가 텅 비었다. 이제 여백은 하나(Pad)로 통일되고 높이는 내용에 맞춰 줄고 는다.
///
/// 수치 근거 (기획 문서 "표시 기준" 표):
///  - 폭 640px × 0.0009 = 0.58m. 1.3m 거리에서 시야 중앙 41° 안에 들어온다 (Android XR 공간 UI).
///  - 이름 46px ≈ 1.9°, 본문 28px ≈ 1.1°. 10~15세 대상이라 MS 권장(0.6~0.75°)보다 크게 잡았다.
///  - 바탕의 가장 어두운 채널도 13/255 위 — Quest 는 그 아래 단계를 구분하지 못한다 (Meta 디스플레이).
///  - 흰 글자 + 불투명한 어두운 바탕, 가는 글꼴 금지 (MS 타이포그래피).
/// </summary>
public static class DomeAquariumInfoPanelSetup
{
    // 판·외곽선·점은 STILL 전용 UI 키트(반경 10px, 외곽선 1.5px)를 쓴다 — DomeAquariumUiKit 참고.
    const string RoundPath = DomeAquariumUiKit.PanelPath;
    const string OutlinePath = DomeAquariumUiKit.OutlinePath;
    const string CirclePath = "Assets/VRTemplateAssets/Sprites/UI/Circle_60x60 Outline 4.png";
    const string DotPath = DomeAquariumUiKit.DotPath;

    const float CardWidth = 640f;
    const float CardScale = 0.0009f;
    const int Pad = 36;                       // 좌우 여백. 모든 줄이 같은 왼쪽 선에서 시작한다
    const float CornerMultiplier = 1f;        // 키트 스프라이트는 반경이 이미 맞춰져 있어 배수를 두지 않는다

    static readonly Color Plate = new Color(0.06f, 0.15f, 0.20f, 0.96f);
    static readonly Color OutlineTint = new Color(0.30f, 0.82f, 1f, 0.18f);
    static readonly Color Ink = new Color(0.94f, 0.97f, 1f);
    static readonly Color Body = new Color(0.88f, 0.94f, 0.98f);
    static readonly Color Muted = new Color(0.58f, 0.80f, 0.90f);
    static readonly Color Rule = new Color(0.30f, 0.80f, 1f, 0.35f);

    // 현재 에디터가 재생 중이거나 창 없이 백그라운드에 남아 있어도,
    // 다음 스크립트 리로드/재생 종료 때 아직 스타일이 없는 씬에 한 번만 적용한다.
    [InitializeOnLoadMethod]
    static void ScheduleMissingStyleApply()
    {
        EditorApplication.delayCall += ApplyIfMissing;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += ApplyIfMissing;
    }

    static void ApplyIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        bool needsLayoutUpdate = false;
        foreach (FishReveal reveal in Resources.FindObjectsOfTypeAll<FishReveal>())
        {
            if (reveal == null || EditorUtility.IsPersistent(reveal) || !reveal.gameObject.scene.IsValid()) continue;
            needsLayoutUpdate |= !Mathf.Approximately(reveal.turntableDegreesPerSecond, 45f)
                              || !Mathf.Approximately(reveal.maxShowcaseSize, 0.34f);
        }
        foreach (MysteryBox box in Resources.FindObjectsOfTypeAll<MysteryBox>())
        {
            if (box == null || EditorUtility.IsPersistent(box) || !box.gameObject.scene.IsValid()) continue;
            needsLayoutUpdate |= !Mathf.Approximately(box.summonDistance, 0.85f)
                              || !Mathf.Approximately(box.summonDrop, 0.10f)
                              || !Mathf.Approximately(box.stageDistance, 1.35f)
                              || !Mathf.Approximately(box.stageDrop, 0.02f);
        }

        if (needsLayoutUpdate)
        {
            ApplyToOpenScenes();
            return;
        }

        // v2 카드는 Panel 에 VerticalLayoutGroup 이 있다. 없으면 아직 v1(좌표 배치) 카드다.
        foreach (FishInfoPanel info in Resources.FindObjectsOfTypeAll<FishInfoPanel>())
        {
            if (info == null || EditorUtility.IsPersistent(info) || !info.gameObject.scene.IsValid()) continue;
            RectTransform panel = FindRect(info.transform, "Panel");
            if (panel == null || panel.GetComponent<VerticalLayoutGroup>() == null)
            {
                ApplyToOpenScenes();
                return;
            }
        }
    }

    [MenuItem("DomeAquarium/12. 정보 패널 스타일 및 관람 배치 적용")]
    public static void ApplyToOpenScenes()
    {
        int rarityAssetCount = DomeAquariumSetup.ApplyDiscoveryRarities();
        int panelCount = 0;
        int revealCount = 0;
        int boxCount = 0;
        var touchedScenes = new HashSet<Scene>();

        foreach (FishInfoPanel info in Resources.FindObjectsOfTypeAll<FishInfoPanel>())
        {
            if (info == null || EditorUtility.IsPersistent(info) || !info.gameObject.scene.IsValid()) continue;
            StylePanel(info);
            touchedScenes.Add(info.gameObject.scene);
            panelCount++;
        }

        foreach (FishReveal reveal in Resources.FindObjectsOfTypeAll<FishReveal>())
        {
            if (reveal == null || EditorUtility.IsPersistent(reveal) || !reveal.gameObject.scene.IsValid()) continue;
            reveal.showcaseSideOffset = 0.20f;
            reveal.turntableDegreesPerSecond = 45f;
            reveal.maxShowcaseSize = 0.34f;
            reveal.holdTime = Mathf.Max(8f, reveal.holdTime);
            EditorUtility.SetDirty(reveal);
            touchedScenes.Add(reveal.gameObject.scene);
            revealCount++;
        }

        foreach (MysteryBox box in Resources.FindObjectsOfTypeAll<MysteryBox>())
        {
            if (box == null || EditorUtility.IsPersistent(box) || !box.gameObject.scene.IsValid()) continue;
            box.summonDistance = 0.85f;
            box.summonDrop = 0.10f;
            box.stageDistance = 1.35f;
            box.stageDrop = 0.02f;
            EditorUtility.SetDirty(box);
            touchedScenes.Add(box.gameObject.scene);
            boxCount++;
        }

        foreach (Scene scene in touchedScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DomeAquarium] 발견 등급 {rarityAssetCount}개 갱신, 정보 카드 {panelCount}개, 360도 관람 연출 {revealCount}개, 상자 배치 {boxCount}개 적용.");
    }

    public static void StylePanel(FishInfoPanel info)
    {
        if (info == null) return;

        DomeAquariumUiKit.EnsureAll();
        Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath);
        Sprite outline = AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);
        Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        Sprite dot = AssetDatabase.LoadAssetAtPath<Sprite>(DotPath);
        TMP_FontAsset font = info.nameText != null && info.nameText.font != null
            ? info.nameText.font
            : DomeAquariumSetup.LoadKoreanFont();

        RectTransform root = info.GetComponent<RectTransform>();
        if (root == null) return;
        RectTransform panel = FindRect(info.transform, "Panel");
        if (panel == null)
        {
            panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root, false);
        }

        // v1 의 좌표 배치 자식은 전부 지우고 새로 쌓는다. 참조는 아래에서 다시 건다.
        for (int i = panel.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(panel.GetChild(i).gameObject);

        SetRect(root, new Vector2(CardWidth, 400f), Vector2.zero);
        root.localScale = Vector3.one * CardScale;
        SetRect(panel, new Vector2(CardWidth, 0f), Vector2.zero);

        Image background = panel.GetComponent<Image>();
        if (background == null) background = panel.gameObject.AddComponent<Image>();
        Skin(background, round, Plate);

        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        info.canvasGroup = group;

        var stack = panel.GetComponent<VerticalLayoutGroup>();
        if (stack == null) stack = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(Pad, Pad, 30, 26);
        stack.spacing = 2f;
        stack.childAlignment = TextAnchor.UpperLeft;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        var fit = panel.GetComponent<ContentSizeFitter>();
        if (fit == null) fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 외곽선은 레이아웃에서 빼고 바탕 전체에 겹친다. 바탕과 같은 모서리 배수로 둬야 곡률이 맞는다.
        Image border = NewImage(panel, "PanelOutline");
        Stretch(border.rectTransform);
        Ignore(border.gameObject);
        Skin(border, outline, OutlineTint);

        // 1) 관측 빈도 · 일지 기록 표시
        RectTransform status = NewRow(panel, "StatusRow", 32f, 10f);
        Image badge = NewImage(status, "RarityBadge");
        Size(badge.gameObject, 14f, 14f);
        Skin(badge, dot != null ? dot : circle, Color.white);
        TMP_Text rarity = NewText(status, "RarityLabel", font, 21f, FontStyles.Bold, Muted);
        rarity.textWrappingMode = TextWrappingModes.NoWrap;
        Flexible(rarity.gameObject);
        TMP_Text logged = NewText(status, "LoggedLabel", font, 19f, FontStyles.Normal, Muted);
        logged.text = ObservationTerms.LoggedToJournal;
        logged.alignment = TextAlignmentOptions.MidlineRight;
        logged.textWrappingMode = TextWrappingModes.NoWrap;

        Spacer(panel, 6f);

        // 2) 이름 — 긴 이름(와이오밍화이트흰동가리)도 한 줄에 들어가게 자동으로 줄인다
        TMP_Text name = NewText(panel, "NameText", font, 46f, FontStyles.Bold, Ink);
        name.enableAutoSizing = true;
        name.fontSizeMin = 34f;
        name.fontSizeMax = 46f;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.characterSpacing = -1.5f;           // 합성 굵기로 벌어진 자간을 조금 당긴다
        name.alignment = TextAlignmentOptions.BottomLeft;
        // Noto Sans KR 은 줄 높이가 글자 크기의 약 1.45배다. 칸이 그보다 낮으면 TMP 가 줄을 넘친 것으로 보고
        // 자동 크기를 줄이거나(이름) 줄을 통째로 숨긴다(학명) — 높이는 반드시 글자 크기 × 1.45 이상.
        Height(name.gameObject, 68f);

        // 3) 학명 / 영문명
        TMP_Text sci = NewText(panel, "ScientificText", font, 22f, FontStyles.Normal, Muted);
        sci.textWrappingMode = TextWrappingModes.NoWrap;
        sci.overflowMode = TextOverflowModes.Ellipsis;
        sci.alignment = TextAlignmentOptions.TopLeft;
        Height(sci.gameObject, 34f);

        // 4) 구분선 — 위아래 여백을 품은 줄 안에 2px 선을 세운다
        RectTransform ruleRow = NewUI(panel, "Divider");
        Height(ruleRow.gameObject, 26f);
        Image line = NewImage(ruleRow, "Line");
        line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        line.rectTransform.sizeDelta = new Vector2(0f, 2f);
        line.rectTransform.anchoredPosition = Vector2.zero;
        line.color = Rule;

        // 5) 설명 — 높이는 글 길이에 맞춘다
        TMP_Text desc = NewText(panel, "DescriptionText", font, 28f, FontStyles.Normal, Body);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.lineSpacing = 8f;
        desc.alignment = TextAlignmentOptions.TopLeft;

        Spacer(panel, 14f);

        // 6) 아래 줄 — 음성 해설 표시와 태그(비어 있으면 줄에서 빠진다)
        RectTransform footer = NewRow(panel, "FooterRow", 30f, 8f);
        Image voice = NewImage(footer, "VoiceMark");
        Size(voice.gameObject, 26f, 26f);
        voice.sprite = DomeAquariumUiKit.MaterialIcon("volume_up");   // 관찰 일지 "음성 해설" 버튼과 같은 아이콘 (Material Icons)
        voice.preserveAspect = true;
        voice.color = new Color(0.45f, 0.88f, 1f, 1f);
        TMP_Text narration = NewText(footer, "NarrationLabel", font, 19f, FontStyles.Normal, Muted);
        narration.text = ObservationTerms.Narration;
        narration.textWrappingMode = TextWrappingModes.NoWrap;
        Flexible(narration.gameObject);
        TMP_Text sizeTag = NewText(footer, "SizeTag", font, 19f, FontStyles.Normal, Muted);
        sizeTag.alignment = TextAlignmentOptions.MidlineRight;
        TMP_Text habitatTag = NewText(footer, "HabitatTag", font, 19f, FontStyles.Normal, Muted);
        habitatTag.alignment = TextAlignmentOptions.MidlineRight;

        info.rarityBadge = badge;
        info.rarityLabel = rarity;
        info.rarityGlow = null;                  // v1 의 "극히 드묾" 후광 — 작은 점 하나로 충분해 뺐다
        info.nameText = name;
        info.scientificText = sci;
        info.descriptionText = desc;
        info.sizeTagText = sizeTag;
        info.habitatTagText = habitatTag;

        info.dropBelowFish = 0.04f;
        info.sideOffset = -0.50f;
        info.towardViewerOffset = 0.06f;

        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        EditorUtility.SetDirty(info);
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(panel);
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    static RectTransform NewUI(Transform parent, string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return rt;
    }

    static Image NewImage(Transform parent, string objectName)
    {
        RectTransform rt = NewUI(parent, objectName);
        Image image = rt.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text NewText(Transform parent, string objectName, TMP_FontAsset font, float size, FontStyles style, Color color)
    {
        RectTransform rt = NewUI(parent, objectName);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    static RectTransform NewRow(Transform parent, string objectName, float height, float spacing)
    {
        RectTransform rt = NewUI(parent, objectName);
        var row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        Height(rt.gameObject, height);
        return rt;
    }

    static void Spacer(Transform parent, float height) => Height(NewUI(parent, "Spacer").gameObject, height);

    static LayoutElement Element(GameObject go)
    {
        var le = go.GetComponent<LayoutElement>();
        return le != null ? le : go.AddComponent<LayoutElement>();
    }

    static void Height(GameObject go, float h) { var le = Element(go); le.minHeight = h; le.preferredHeight = h; }
    static void Size(GameObject go, float w, float h) { var le = Element(go); le.minWidth = le.preferredWidth = w; le.minHeight = le.preferredHeight = h; }
    static void Flexible(GameObject go) => Element(go).flexibleWidth = 1f;
    static void Ignore(GameObject go) => Element(go).ignoreLayout = true;

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static RectTransform FindRect(Transform root, string objectName)
    {
        foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == objectName) return rt;
        return null;
    }

    static void Skin(Image image, Sprite sprite, Color color)
    {
        image.color = color;
        image.sprite = sprite;
        bool sliced = sprite != null && sprite.border.sqrMagnitude > 0f;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        if (sliced) image.pixelsPerUnitMultiplier = CornerMultiplier;
        image.raycastTarget = false;
    }

    static void SetRect(RectTransform rt, Vector2 size, Vector2 position)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        rt.localScale = Vector3.one;
    }
}
#endif
