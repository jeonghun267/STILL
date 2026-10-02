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
    // 패널은 정보 카드(DomeAquariumInfoPanelSetup)와 같은 흰 라운드 스프라이트를 쓴다.
    // UIPanel.png 는 회색(약 35%) 스프라이트라 Image.color 에 곱해지면서 아래 청록 색들이 전부
    // 거의 검정으로 나왔고, 밤에는 어두운 물빛에 그대로 묻혀 "도감이 까맣게 안 보이는" 원인이 됐다.
    const string RoundSprite = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png";
    const string OutlineSprite = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10 Outline.png";

    // 밤 배경(안개 sRGB 0.02/0.07/0.15)보다는 확실히 밝게, 그래도 어두운 청록으로.
    static readonly Color Deep = new Color(0.03f, 0.12f, 0.17f, 0.96f);
    static readonly Color Panel = new Color(0.04f, 0.17f, 0.23f, 0.97f);
    static readonly Color Outline = new Color(0.25f, 0.83f, 1f, 0.55f);   // 정보 카드 테두리와 같은 계열
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
        Skin(background.gameObject.AddComponent<Image>(), RoundSprite, Deep);

        // 정보 카드처럼 청록 윤곽선을 두른다. 밤에는 짙은 패널 몸체가 배경에 묻히므로
        // 윤곽선이 태블릿의 실루엣을 지켜 준다.
        RectTransform outlineRt = NewUI("TabletOutline", background, new Vector2(1100f, 700f));
        Image outline = outlineRt.gameObject.AddComponent<Image>();
        Skin(outline, OutlineSprite, Outline);
        outline.raycastTarget = false;

        RectTransform inner = NewUI("InnerPanel", background, new Vector2(1060f, 660f));
        Skin(inner.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.035f, 0.14f, 0.19f, 0.96f));

        // 상단 헤더
        RectTransform header = NewUI("Header", inner, new Vector2(1020f, 82f));
        header.anchoredPosition = new Vector2(0f, 270f);
        Skin(header.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.035f, 0.22f, 0.28f, 0.96f));

        TMP_Text title = NewText("ProgressText", header, new Vector2(520f, 60f), 34f, font);
        title.rectTransform.anchoredPosition = new Vector2(-232f, 0f);
        title.alignment = TextAlignmentOptions.Left;
        title.fontStyle = FontStyles.Bold;
        title.text = ObservationTerms.Progress(0, 98);
        controller.progressText = title;

        TMP_Text marine = NewText("MarineDiscovery", header, new Vector2(220f, 30f), 15f, font);
        marine.rectTransform.anchoredPosition = new Vector2(264f, 20f);
        marine.text = ObservationTerms.LogTitleEnglish;
        marine.color = new Color(0.48f, 0.88f, 0.96f);

        Button close = AddButton("CloseButton", header, new Vector2(68f, 56f), new Vector2(466f, 0f), "×", 38f, font,
            new Color(0.08f, 0.34f, 0.40f, 1f));
        Wire(close, controller.Close);

        // 왼쪽 필터
        // 세 패널은 헤더 아래 232 에서 -252 까지. 그 아래 78 은 프로필 전환·페이지 넘김 전용 줄이다.
        // (예전엔 패널이 -308 까지 내려와 프로필 바와 페이지 버튼이 서로 겹쳤다.)
        RectTransform filterPanel = NewUI("FilterPanel", inner, new Vector2(180f, 484f));
        filterPanel.anchoredPosition = new Vector2(-420f, -10f);
        Skin(filterPanel.gameObject.AddComponent<Image>(), RoundSprite, Panel);

        TMP_Text filterTitle = NewText("FilterTitle", filterPanel, new Vector2(150f, 40f), 23f, font);
        filterTitle.rectTransform.anchoredPosition = new Vector2(0f, 205f);
        filterTitle.text = ObservationTerms.FrequencyLabel;
        filterTitle.fontStyle = FontStyles.Bold;

        string[] filterLabels = { "전체", "기록만", ObservationTerms.Frequency[0], ObservationTerms.Frequency[1],
            ObservationTerms.Frequency[2], ObservationTerms.Frequency[3], ObservationTerms.Frequency[4] };
        UnityEngine.Events.UnityAction[] filterActions =
        {
            controller.FilterAll, controller.FilterDiscovered, controller.FilterCommon, controller.FilterUncommon,
            controller.FilterRare, controller.FilterEpic, controller.FilterLegendary
        };
        for (int i = 0; i < filterLabels.Length; i++)
        {
            Button b = AddButton("Filter_" + i, filterPanel, new Vector2(148f, 48f),
                new Vector2(0f, 150f - i * 54f), filterLabels[i], 21f, font,
                i == 0 ? new Color(0.08f, 0.48f, 0.60f, 1f) : new Color(0.035f, 0.23f, 0.29f, 1f));
            Wire(b, filterActions[i]);
        }

        TMP_Text filterStatus = NewText("FilterStatus", filterPanel, new Vector2(150f, 32f), 17f, font);
        filterStatus.rectTransform.anchoredPosition = new Vector2(0f, -218f);
        filterStatus.text = "전체 생물";
        filterStatus.color = new Color(0.45f, 0.84f, 0.92f);
        controller.filterStatusText = filterStatus;

        // 중앙 카드 그리드
        RectTransform gridPanel = NewUI("CardGridPanel", inner, new Vector2(520f, 484f));
        gridPanel.anchoredPosition = new Vector2(-60f, -10f);
        // 그리드는 바탕보다 살짝 어둡게 파고, 카드는 그보다 밝게 띄워 카드가 도드라지게 한다.
        Skin(gridPanel.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.025f, 0.10f, 0.145f, 0.85f));

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
            Vector2 pos = new Vector2(-168f + col * 168f, 170f - row * 116f);
            RectTransform card = NewUI("Card_" + i, gridPanel, new Vector2(154f, 104f));
            card.anchoredPosition = pos;
            Image frame = card.gameObject.AddComponent<Image>();
            Skin(frame, RoundSprite, new Color(0.06f, 0.22f, 0.29f, 0.97f));

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

            TMP_Text name = NewText("KoreanName", card, new Vector2(140f, 28f), 19f, font);
            name.rectTransform.anchoredPosition = new Vector2(0f, -17f);
            name.fontStyle = FontStyles.Bold;
            name.text = "???";
            // "골드네온피그미고비"처럼 긴 이름이 두 줄로 꺾여 등급 글자를 덮지 않도록 한 줄 고정 + 자동 축소.
            SingleLine(name, 12f, 19f);

            TMP_Text rarity = NewText("Rarity", card, new Vector2(140f, 22f), 15f, font);
            rarity.rectTransform.anchoredPosition = new Vector2(0f, -38f);
            rarity.text = ObservationTerms.Unobserved;
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

        // 페이지 넘김은 그리드 아래 전용 줄(y=-291)에 둔다. 그리드 안에 넣으면 넷째 줄 카드와 붙는다.
        Button prevPage = AddButton("PreviousPage", inner, new Vector2(64f, 42f), new Vector2(-180f, -291f), "‹", 34f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Button nextPage = AddButton("NextPage", inner, new Vector2(64f, 42f), new Vector2(60f, -291f), "›", 34f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Wire(prevPage, controller.PreviousPage);
        Wire(nextPage, controller.NextPage);
        TMP_Text pageText = NewText("PageText", inner, new Vector2(120f, 40f), 19f, font);
        pageText.rectTransform.anchoredPosition = new Vector2(-60f, -291f);
        pageText.text = "1 / 9";
        pageText.color = Muted;
        controller.pageText = pageText;

        // 오른쪽 상세 관찰 영역
        RectTransform detail = NewUI("DetailPanel", inner, new Vector2(320f, 484f));
        detail.anchoredPosition = new Vector2(370f, -10f);
        Skin(detail.gameObject.AddComponent<Image>(), RoundSprite, Panel);

        RectTransform previewWell = NewUI("PreviewWell", detail, new Vector2(286f, 196f));
        previewWell.anchoredPosition = new Vector2(0f, 132f);
        // 검정 구멍처럼 보이지 않도록 패널과 이어지는 밝은 청록 관찰 영역을 사용한다.
        Skin(previewWell.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.06f, 0.25f, 0.32f, 0.90f));
        TMP_Text orbitHint = NewText("OrbitHint", previewWell, new Vector2(250f, 25f), 14f, font);
        orbitHint.rectTransform.anchoredPosition = new Vector2(0f, -80f);
        orbitHint.text = "360° 자동 관찰";
        orbitHint.color = new Color(0.38f, 0.66f, 0.72f);

        RectTransform previewAnchor = NewUI("PreviewAnchor", previewWell, new Vector2(1f, 1f));
        previewAnchor.anchoredPosition = new Vector2(0f, 8f);
        // 화면 중심점은 패널 표면에 유지하고, 사용자 쪽 깊이 이동은 런타임에
        // 관찰점-눈 사이의 직선 방향으로 계산해 원근에 의한 좌우 밀림을 막는다.
        previewAnchor.localPosition = new Vector3(0f, 8f, 0f);
        controller.previewAnchor = previewAnchor;

        TMP_Text detailName = NewText("DetailName", detail, new Vector2(286f, 40f), 29f, font);
        detailName.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        detailName.alignment = TextAlignmentOptions.Left;
        detailName.fontStyle = FontStyles.Bold;
        detailName.text = "생물을 선택하세요";
        SingleLine(detailName, 20f, 29f);
        controller.detailNameText = detailName;

        TMP_Text detailSubtitle = NewText("DetailSubtitle", detail, new Vector2(286f, 40f), 16f, font);
        detailSubtitle.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        detailSubtitle.alignment = TextAlignmentOptions.Left;
        detailSubtitle.fontStyle = FontStyles.Italic;
        detailSubtitle.color = new Color(0.48f, 0.76f, 0.82f);
        detailSubtitle.text = FishEncyclopediaPanel.DefaultSubtitle;
        // 학명이 길어도 한 글자만 다음 줄로 떨어지는 일이 없도록 한 줄로 맞춘다.
        SingleLine(detailSubtitle, 12f, 16f);
        controller.detailSubtitleText = detailSubtitle;

        TMP_Text detailRarity = NewText("DetailRarity", detail, new Vector2(286f, 25f), 16f, font);
        detailRarity.rectTransform.anchoredPosition = new Vector2(0f, -62f);
        detailRarity.alignment = TextAlignmentOptions.Left;
        controller.detailRarityText = detailRarity;

        TMP_Text detailDescription = NewText("DetailDescription", detail, new Vector2(286f, 118f), 19f, font);
        detailDescription.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        detailDescription.alignment = TextAlignmentOptions.TopLeft;
        detailDescription.text = "카드를 선택하면 생김새와 설명이 여기에 표시됩니다.";
        detailDescription.color = new Color(0.86f, 0.93f, 0.96f);
        controller.detailDescriptionText = detailDescription;

        TMP_Text count = NewText("DiscoveryCount", detail, new Vector2(120f, 28f), 16f, font);
        count.rectTransform.anchoredPosition = new Vector2(-78f, -218f);
        count.alignment = TextAlignmentOptions.Left;
        count.color = Muted;
        controller.detailCountText = count;

        Button narrate = AddButton("NarrationButton", detail, new Vector2(150f, 46f), new Vector2(68f, -218f), "▶  음성 해설", 17f, font,
            new Color(0.055f, 0.43f, 0.53f, 1f));
        Wire(narrate, controller.PlaySelectedNarration);
        controller.narrationButtonRoot = narrate.gameObject;
        narrate.gameObject.SetActive(false);

        // 프로필 전환: 텍스트 입력 없이 현장에서 빠르게 사용자만 바꾼다.
        // 필터 패널 바로 아래 줄, 필터 패널 폭 안에만 둔다. 오른쪽으로 삐져나오면 페이지 버튼과 겹친다.
        RectTransform profileBar = NewUI("ProfileBar", inner, new Vector2(180f, 48f));
        profileBar.anchoredPosition = new Vector2(-420f, -291f);
        Button prevProfile = AddButton("PreviousProfile", profileBar, new Vector2(40f, 40f), new Vector2(-68f, 0f), "‹", 26f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Button nextProfile = AddButton("NextProfile", profileBar, new Vector2(40f, 40f), new Vector2(68f, 0f), "›", 26f, font,
            new Color(0.035f, 0.29f, 0.36f, 1f));
        Wire(prevProfile, controller.PreviousProfile);
        Wire(nextProfile, controller.NextProfile);
        TMP_Text profile = NewText("ProfileText", profileBar, new Vector2(92f, 40f), 17f, font);
        SingleLine(profile, 12f, 17f);
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

        // 옆의 "다시 뽑기"(SummonButton)와 같은 스프라이트를 써야 손목 메뉴 안에서 톤이 맞는다.
        Button button = AddButton("EncyclopediaButton", wrist.transform, new Vector2(300f, 128f), new Vector2(0f, -70f),
            ObservationTerms.OpenLog, 36f, font, new Color(0.04f, 0.50f, 0.58f, 0.97f), "UIButtonDefault.png");
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
        float fontSize, TMP_FontAsset font, Color color, string sprite = RoundSprite)
    {
        RectTransform rt = NewUI(name, parent, size);
        rt.anchoredPosition = position;
        Image image = rt.gameObject.AddComponent<Image>();
        Skin(image, sprite, color);
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

    /// <summary>한 줄 고정. 넘치면 글자를 줄이고, 그래도 안 되면 말줄임표.</summary>
    static void SingleLine(TMP_Text text, float minSize, float maxSize)
    {
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
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

    /// <param name="spriteName">"Assets/..." 전체 경로, 또는 UiSamples 폴더 안의 파일 이름</param>
    static void Skin(Image image, string spriteName, Color color)
    {
        image.color = color;
        string path = spriteName.StartsWith("Assets/", System.StringComparison.Ordinal)
            ? spriteName
            : UiSamples + "/" + spriteName;
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
    }
}
#endif
