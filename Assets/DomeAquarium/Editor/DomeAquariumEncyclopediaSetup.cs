#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>현재 설명 카드와 같은 청록색 디자인으로 월드 스페이스 도감 태블릿을 만든다. (v2)</summary>
public static class DomeAquariumEncyclopediaSetup
{
    const string RootName = "FishEncyclopediaRoot";
    const string HandTag = "Hand";
    const string UiSamples = "Assets/Unity UI Samples/Textures and Sprites/Rounded UI";

    static readonly Color Deep = new Color(0.015f, 0.075f, 0.11f, 0.97f);
    static readonly Color Panel = new Color(0.025f, 0.13f, 0.18f, 0.97f);
    static readonly Color Cyan = new Color(0.08f, 0.72f, 0.88f, 1f);
    static readonly Color Muted = new Color(0.62f, 0.76f, 0.82f, 1f);

    [MenuItem("DomeAquarium/15. 도감 태블릿 적용", priority = 15)]
    public static void ApplyMenu() { Apply(true); }

    public static FishEncyclopediaPanel Apply(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[도감] 플레이 모드에서는 태블릿을 다시 만들 수 없습니다.");
            return null;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[도감] Main Camera를 찾지 못했습니다.");
            return null;
        }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        TMP_FontAsset font = DomeAquariumSetup.LoadKoreanFont();
        Transform parent = camera.transform.parent;

        var root = new GameObject(RootName);
        if (parent != null) root.transform.SetParent(parent, false);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(1100f, 700f);
        // VR에서 작은 글자를 몸을 숙여 읽지 않아도 되도록 기존보다 약 19% 크게 표시한다.
        rootRt.localScale = Vector3.one * 0.00095f;
        rootRt.localPosition = new Vector3(0f, 1.25f, 1.05f);

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        FishEncyclopediaPanel controller = root.AddComponent<FishEncyclopediaPanel>();
        controller.canvasGroup = group;
        controller.viewer = camera.transform;
        controller.openDistance = 0.95f;
        controller.allFish = LoadAllFish();

        RectTransform background = NewUI("TabletFrame", root.transform, new Vector2(1100f, 700f));
        Skin(background.gameObject.AddComponent<Image>(), "UIPanel.png", Deep);

        RectTransform inner = NewUI("InnerPanel", background, new Vector2(1060f, 660f));
        Skin(inner.gameObject.AddComponent<Image>(), "UIPanel.png", new Color(0.02f, 0.105f, 0.145f, 0.96f));

        // 상단 헤더
        RectTransform header = NewUI("Header", inner, new Vector2(1020f, 82f));
        header.anchoredPosition = new Vector2(0f, 270f);
        Skin(header.gameObject.AddComponent<Image>(), "UIPanel.png", new Color(0.035f, 0.22f, 0.28f, 0.96f));

        TMP_Text title = NewText("ProgressText", header, new Vector2(520f, 60f), 34f, font);
        title.rectTransform.anchoredPosition = new Vector2(-232f, 0f);
        title.alignment = TextAlignmentOptions.Left;
        title.fontStyle = FontStyles.Bold;
        title.text = "해양생물 도감  ·  0/98 발견";
        controller.progressText = title;

        TMP_Text marine = NewText("MarineDiscovery", header, new Vector2(220f, 30f), 15f, font);
        marine.rectTransform.anchoredPosition = new Vector2(264f, 20f);
        marine.text = "MARINE DISCOVERY";
        marine.color = new Color(0.48f, 0.88f, 0.96f);

        Button close = AddButton("CloseButton", header, new Vector2(68f, 56f), new Vector2(466f, 0f), "×", 38f, font,
            new Color(0.08f, 0.34f, 0.40f, 1f));
        Wire(close, controller.Close);

        // 왼쪽 필터
        RectTransform filterPanel = NewUI("FilterPanel", inner, new Vector2(180f, 540f));
        filterPanel.anchoredPosition = new Vector2(-420f, -38f);
        Skin(filterPanel.gameObject.AddComponent<Image>(), "UIPanel.png", Panel);

        TMP_Text filterTitle = NewText("FilterTitle", filterPanel, new Vector2(150f, 40f), 23f, font);
        filterTitle.rectTransform.anchoredPosition = new Vector2(0f, 230f);
        filterTitle.text = "발견 등급";
        filterTitle.fontStyle = FontStyles.Bold;

        string[] filterLabels = { "전체", "발견만", "일반", "고급", "희귀", "영웅", "전설" };
        UnityEngine.Events.UnityAction[] filterActions =
        {
            controller.FilterAll, controller.FilterDiscovered, controller.FilterCommon, controller.FilterUncommon,
            controller.FilterRare, controller.FilterEpic, controller.FilterLegendary
        };
        for (int i = 0; i < filterLabels.Length; i++)
        {
            Button b = AddButton("Filter_" + i, filterPanel, new Vector2(148f, 50f),
                new Vector2(0f, 170f - i * 56f), filterLabels[i], 21f, font,
                i == 0 ? new Color(0.08f, 0.48f, 0.60f, 1f) : new Color(0.035f, 0.23f, 0.29f, 1f));
            Wire(b, filterActions[i]);
        }

        TMP_Text filterStatus = NewText("FilterStatus", filterPanel, new Vector2(150f, 32f), 17f, font);
        filterStatus.rectTransform.anchoredPosition = new Vector2(0f, -224f);
        filterStatus.text = "전체 생물";
        filterStatus.color = new Color(0.45f, 0.84f, 0.92f);
        controller.filterStatusText = filterStatus;

        // 중앙 카드 그리드
        RectTransform gridPanel = NewUI("CardGridPanel", inner, new Vector2(520f, 540f));
        gridPanel.anchoredPosition = new Vector2(-60f, -38f);
        Skin(gridPanel.gameObject.AddComponent<Image>(), "UIPanel.png", new Color(0.015f, 0.085f, 0.12f, 0.72f));

        const int SlotCount = 12;
        var cardRoots = new GameObject[SlotCount];
        var cardButtons = new Button[SlotCount];
        var cardFrames = new Image[SlotCount];
        var cardNames = new TMP_Text[SlotCount];
        var cardRarities = new TMP_Text[SlotCount];
        var cardMarks = new TMP_Text[SlotCount];

        for (int i = 0; i < SlotCount; i++)
        {
            int col = i % 3;
            int row = i / 3;
            Vector2 pos = new Vector2(-168f + col * 168f, 188f - row * 120f);
            RectTransform card = NewUI("Card_" + i, gridPanel, new Vector2(154f, 106f));
            card.anchoredPosition = pos;
            Image frame = card.gameObject.AddComponent<Image>();
            Skin(frame, "UIButtonDefault.png", new Color(0.025f, 0.12f, 0.17f, 0.97f));

            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.08f, 0.36f, 0.44f, 1f);
            colors.pressedColor = new Color(0.04f, 0.52f, 0.62f, 1f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;
            AddPoke(card.gameObject, new Vector3(160f, 112f, 180f));

            TMP_Text mark = NewText("Silhouette", card, new Vector2(48f, 48f), 38f, font);
            mark.rectTransform.anchoredPosition = new Vector2(0f, 19f);
            mark.text = "?";
            mark.color = new Color(0.28f, 0.38f, 0.45f);

            TMP_Text name = NewText("KoreanName", card, new Vector2(140f, 30f), 19f, font);
            name.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            name.fontStyle = FontStyles.Bold;
            name.text = "???";

            TMP_Text rarity = NewText("Rarity", card, new Vector2(140f, 23f), 15f, font);
            rarity.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            rarity.text = "미발견";
            rarity.color = Muted;

            cardRoots[i] = card.gameObject;
            cardButtons[i] = button;
            cardFrames[i] = frame;
            cardNames[i] = name;
            cardRarities[i] = rarity;
            cardMarks[i] = mark;
        }

        controller.cardRoots = cardRoots;
        controller.cardButtons = cardButtons;
        controller.cardFrames = cardFrames;
        controller.cardNames = cardNames;
        controller.cardRarities = cardRarities;
        controller.cardLockedMarks = cardMarks;

        Button prevPage = AddButton("PreviousPage", gridPanel, new Vector2(70f, 46f), new Vector2(-128f, -238f), "‹", 34f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Button nextPage = AddButton("NextPage", gridPanel, new Vector2(70f, 46f), new Vector2(128f, -238f), "›", 34f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Wire(prevPage, controller.PreviousPage);
        Wire(nextPage, controller.NextPage);
        TMP_Text pageText = NewText("PageText", gridPanel, new Vector2(160f, 40f), 19f, font);
        pageText.rectTransform.anchoredPosition = new Vector2(0f, -238f);
        pageText.text = "1 / 9";
        pageText.color = Muted;
        controller.pageText = pageText;

        // 오른쪽 상세 관찰 영역
        RectTransform detail = NewUI("DetailPanel", inner, new Vector2(320f, 540f));
        detail.anchoredPosition = new Vector2(370f, -38f);
        Skin(detail.gameObject.AddComponent<Image>(), "UIPanel.png", Panel);

        RectTransform previewWell = NewUI("PreviewWell", detail, new Vector2(286f, 202f));
        previewWell.anchoredPosition = new Vector2(0f, 154f);
        // 검정 구멍처럼 보이지 않도록 패널과 이어지는 밝은 청록 관찰 영역을 사용한다.
        Skin(previewWell.gameObject.AddComponent<Image>(), "UIPanel.png", new Color(0.025f, 0.16f, 0.20f, 0.90f));
        TMP_Text orbitHint = NewText("OrbitHint", previewWell, new Vector2(250f, 25f), 14f, font);
        orbitHint.rectTransform.anchoredPosition = new Vector2(0f, -82f);
        orbitHint.text = "360° 자동 관찰";
        orbitHint.color = new Color(0.38f, 0.66f, 0.72f);

        RectTransform previewAnchor = NewUI("PreviewAnchor", previewWell, new Vector2(1f, 1f));
        previewAnchor.anchoredPosition = new Vector2(0f, 8f);
        // 화면 중심점은 패널 표면에 유지하고, 사용자 쪽 깊이 이동은 런타임에
        // 관찰점-눈 사이의 직선 방향으로 계산해 원근에 의한 좌우 밀림을 막는다.
        previewAnchor.localPosition = new Vector3(0f, 8f, 0f);
        controller.previewAnchor = previewAnchor;

        TMP_Text detailName = NewText("DetailName", detail, new Vector2(286f, 40f), 29f, font);
        detailName.rectTransform.anchoredPosition = new Vector2(0f, 30f);
        detailName.alignment = TextAlignmentOptions.Left;
        detailName.fontStyle = FontStyles.Bold;
        detailName.text = "생물을 선택하세요";
        controller.detailNameText = detailName;

        TMP_Text detailSubtitle = NewText("DetailSubtitle", detail, new Vector2(286f, 42f), 16f, font);
        detailSubtitle.rectTransform.anchoredPosition = new Vector2(0f, -10f);
        detailSubtitle.alignment = TextAlignmentOptions.Left;
        detailSubtitle.fontStyle = FontStyles.Italic;
        detailSubtitle.color = new Color(0.48f, 0.76f, 0.82f);
        detailSubtitle.text = "발견한 카드에서 3D 생물을 관찰할 수 있어요";
        controller.detailSubtitleText = detailSubtitle;

        TMP_Text detailRarity = NewText("DetailRarity", detail, new Vector2(286f, 25f), 16f, font);
        detailRarity.rectTransform.anchoredPosition = new Vector2(0f, -45f);
        detailRarity.alignment = TextAlignmentOptions.Left;
        controller.detailRarityText = detailRarity;

        TMP_Text detailDescription = NewText("DetailDescription", detail, new Vector2(286f, 132f), 19f, font);
        detailDescription.rectTransform.anchoredPosition = new Vector2(0f, -126f);
        detailDescription.alignment = TextAlignmentOptions.TopLeft;
        detailDescription.text = "카드를 선택하면 생김새와 설명이 여기에 표시됩니다.";
        detailDescription.color = new Color(0.86f, 0.93f, 0.96f);
        controller.detailDescriptionText = detailDescription;

        TMP_Text count = NewText("DiscoveryCount", detail, new Vector2(120f, 28f), 16f, font);
        count.rectTransform.anchoredPosition = new Vector2(-78f, -230f);
        count.alignment = TextAlignmentOptions.Left;
        count.color = Muted;
        controller.detailCountText = count;

        Button narrate = AddButton("NarrationButton", detail, new Vector2(150f, 46f), new Vector2(68f, -230f), "▶  음성 해설", 17f, font,
            new Color(0.055f, 0.43f, 0.53f, 1f));
        Wire(narrate, controller.PlaySelectedNarration);
        controller.narrationButtonRoot = narrate.gameObject;
        narrate.gameObject.SetActive(false);

        // 프로필 전환: 텍스트 입력 없이 현장에서 빠르게 사용자만 바꾼다.
        RectTransform profileBar = NewUI("ProfileBar", inner, new Vector2(320f, 48f));
        profileBar.anchoredPosition = new Vector2(-350f, -306f);
        Button prevProfile = AddButton("PreviousProfile", profileBar, new Vector2(48f, 42f), new Vector2(-132f, 0f), "‹", 28f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Button nextProfile = AddButton("NextProfile", profileBar, new Vector2(48f, 42f), new Vector2(132f, 0f), "›", 28f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Wire(prevProfile, controller.PreviousProfile);
        Wire(nextProfile, controller.NextProfile);
        TMP_Text profile = NewText("ProfileText", profileBar, new Vector2(200f, 40f), 20f, font);
        profile.text = "탐험가 1";
        profile.fontStyle = FontStyles.Bold;
        profile.color = new Color(0.66f, 0.90f, 0.94f);
        controller.profileText = profile;

        AddWristButton(controller, font);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (saveScene) EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[도감] 수중 탐사 태블릿 적용 완료 ({controller.allFish.Length}종, 로컬 탐험가 3명).");
        return controller;
    }

    static void AddWristButton(FishEncyclopediaPanel controller, TMP_FontAsset font)
    {
        GameObject wrist = GameObject.Find("WristMenu");
        if (wrist == null)
        {
            Debug.LogWarning("[도감] WristMenu를 찾지 못해 도감 열기 버튼을 연결하지 못했습니다.");
            return;
        }

        Transform old = wrist.transform.Find("EncyclopediaButton");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        RectTransform wristRt = wrist.GetComponent<RectTransform>();
        if (wristRt != null) wristRt.sizeDelta = new Vector2(300f, 270f);
        RectTransform summon = wrist.transform.Find("SummonButton") as RectTransform;
        if (summon != null)
        {
            summon.sizeDelta = new Vector2(300f, 128f);
            summon.anchoredPosition = new Vector2(0f, 70f);
            TMP_Text summonLabel = summon.GetComponentInChildren<TMP_Text>(true);
            if (summonLabel != null) summonLabel.rectTransform.sizeDelta = new Vector2(300f, 128f);
        }

        Button button = AddButton("EncyclopediaButton", wrist.transform, new Vector2(300f, 128f), new Vector2(0f, -70f),
            "도감 열기", 36f, font, new Color(0.04f, 0.50f, 0.58f, 0.97f));
        Wire(button, controller.Toggle);
    }

    static FishData[] LoadAllFish()
    {
        return AssetDatabase.FindAssets("t:FishData", new[] { "Assets/DomeAquarium/FishData" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(fish => fish != null)
            .OrderBy(fish => (int)fish.rarity)
            .ThenBy(fish => fish.DisplayName)
            .ToArray();
    }

    static Button AddButton(string name, Transform parent, Vector2 size, Vector2 position, string label,
        float fontSize, TMP_FontAsset font, Color color)
    {
        RectTransform rt = NewUI(name, parent, size);
        rt.anchoredPosition = position;
        Image image = rt.gameObject.AddComponent<Image>();
        Skin(image, "UIButtonDefault.png", color);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.20f);
        colors.fadeDuration = 0.07f;
        button.colors = colors;
        TMP_Text text = NewText("Label", rt, size, fontSize, font);
        text.text = label;
        text.color = Color.white;
        AddPoke(rt.gameObject, new Vector3(size.x + 12f, size.y + 12f, 180f));
        return button;
    }

    static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null) return;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        PokeSelector poke = button.GetComponent<PokeSelector>();
        if (poke != null) UnityEventTools.AddPersistentListener(poke.onPoked, action);
    }

    static void AddPoke(GameObject target, Vector3 size)
    {
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = size;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        PokeSelector poke = target.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = HandTag;
        poke.inputCooldown = 0.45f;
    }

    static RectTransform NewUI(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, Vector2 size, float fontSize, TMP_FontAsset font)
    {
        RectTransform rt = NewUI(name, parent, size);
        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        if (font != null) text.font = font;
        return text;
    }

    static void Skin(Image image, string spriteName, Color color)
    {
        image.color = color;
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSamples + "/" + spriteName);
        if (sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
    }
}
#endif
