#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 관찰 일지 태블릿 (v3) — 정보 카드와 같은 청록 판 위에 레이아웃 그룹으로 쌓는다.
///
/// v2 에서 고친 것:
///  - 크기: 0.95m 앞에 폭 1.05m(시야 57°)라 고개를 돌려야 다 보였다. 1.1m 앞에 폭 0.78m(시야 39°)로 줄여
///    시야 중앙 41° 안에 넣는다 (Android XR 공간 UI). 눈높이보다 약 6° 아래 (Android 5°, MS 10~20°).
///  - 배치: 요소마다 좌표를 박아 상세 설명이 오른쪽에서 잘리고 줄마다 여백이 달랐다. 이제 머리줄·본문·아래줄을
///    레이아웃 그룹으로 쌓아 칸이 내용에 맞춰 정렬된다. 필터를 아래줄 칩으로 옮겨 상세 칸을 넓혔다.
///  - 글자: 1.1m 에서 1° ≈ 25.6px. 제목 36px(1.4°), 본문 26px(1.0°), 이름표 22px(0.86°), 최소 19px(0.74°).
///    10~15세 대상이라 MS 권장 0.6~0.75° 보다 크게. 기울임꼴·영문 머리말은 뺐다.
///  - 색: 판과 칸의 가장 어두운 채널을 13/255 위로 (Meta 디스플레이 — 그 아래는 구분되지 않는다).
/// </summary>
public static class DomeAquariumEncyclopediaSetup
{
    const string RootName = "FishEncyclopediaRoot";
    const string HandTag = "Hand";
    // 정보 카드와 같은 STILL 전용 UI 키트(반경 10px, 외곽선 1.5px) — DomeAquariumUiKit 참고.
    const string RoundSprite = DomeAquariumUiKit.PanelPath;
    const string OutlineSprite = DomeAquariumUiKit.OutlinePath;

    const float Width = 1040f;
    const float Height = 680f;
    const float Scale = 0.00075f;             // 0.78m × 0.51m
    const float OpenDistance = 1.1f;
    const float OpenDrop = 0.12f;             // ≈ 6° 아래
    const float CornerMultiplier = 2f;        // 라운드 스프라이트 모서리 40px → 20px, 외곽선도 얇게
    const int SlotCount = 12;                 // 3열 × 4줄

    static readonly Color Plate = new Color(0.06f, 0.15f, 0.20f, 0.97f);
    static readonly Color Well = new Color(0.07f, 0.18f, 0.24f, 0.97f);      // 판보다 한 단계 밝은 칸
    static readonly Color OutlineTint = new Color(0.30f, 0.82f, 1f, 0.18f);
    static readonly Color Chip = DomeAquariumUiKit.SecondaryTint;        // 칩 = Kenney 버튼에 곱하는 색 — 고른 칩은 주 버튼 색
    static readonly Color ChipActive = DomeAquariumUiKit.PrimaryTint;
    static readonly Color Ink = new Color(0.94f, 0.97f, 1f);
    static readonly Color Body = new Color(0.88f, 0.94f, 0.98f);
    static readonly Color Muted = new Color(0.58f, 0.80f, 0.90f);

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

        DomeAquariumUiKit.EnsureAll();
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
        rootRt.sizeDelta = new Vector2(Width, Height);
        rootRt.localScale = Vector3.one * Scale;
        rootRt.localPosition = new Vector3(0f, 1.25f, OpenDistance);

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        FishEncyclopediaPanel controller = root.AddComponent<FishEncyclopediaPanel>();
        controller.canvasGroup = group;
        controller.viewer = camera.transform;
        controller.openDistance = OpenDistance;
        controller.openDrop = OpenDrop;
        controller.allFish = LoadAllFish();

        // ── 판 ─────────────────────────────────────────────
        RectTransform frame = NewUI("TabletFrame", root.transform);
        Stretch(frame);
        Skin(frame.gameObject.AddComponent<Image>(), RoundSprite, Plate);
        Image outline = NewUI("TabletOutline", frame).gameObject.AddComponent<Image>();
        Stretch(outline.rectTransform);
        Ignore(outline.gameObject);
        Skin(outline, OutlineSprite, OutlineTint);

        VerticalLayoutGroup stack = frame.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(28, 28, 24, 24);
        stack.spacing = 16f;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        // ── 머리줄: 진행도 · 현재 보기 · 탐험가 · 닫기 ─────────────
        RectTransform header = NewRow(frame, "Header", 56f, 16f);

        TMP_Text title = NewText("ProgressText", header, 36f, font, FontStyles.Bold, Ink);
        title.text = ObservationTerms.Progress(0, controller.allFish.Length);
        SingleLine(title, 28f, 36f);
        Fixed(title.gameObject, -1f, 56f);
        Element(title.gameObject).flexibleWidth = 1f;
        controller.progressText = title;

        // 지금 보기는 아래줄 칩 색으로 보여 준다 — 머리줄에 같은 말을 또 쓰지 않는다.
        controller.filterStatusText = null;

        // 프로필 전환: 텍스트 입력 없이 현장에서 빠르게 사용자만 바꾼다.
        Button prevProfile = AddButton("PreviousProfile", header, 52f, 52f, "", 0f, font, DomeAquariumUiKit.Tone.Secondary, DomeAquariumUiKit.MaterialIcon("chevron_left"), 40f).button;
        TMP_Text profile = NewText("ProfileText", header, 24f, font, FontStyles.Bold, new Color(0.70f, 0.92f, 0.96f));
        profile.text = "탐험가 1";
        SingleLine(profile, 19f, 24f);
        Fixed(profile.gameObject, 112f, 52f);
        controller.profileText = profile;
        Button nextProfile = AddButton("NextProfile", header, 52f, 52f, "", 0f, font, DomeAquariumUiKit.Tone.Secondary, DomeAquariumUiKit.MaterialIcon("chevron_right"), 40f).button;
        Wire(prevProfile, controller.PreviousProfile);
        Wire(nextProfile, controller.NextProfile);

        Button close = AddButton("CloseButton", header, 56f, 56f, "", 0f, font, DomeAquariumUiKit.Tone.Secondary, DomeAquariumUiKit.CloseIcon, 36f).button;
        Wire(close, controller.Close);

        // ── 본문: 카드 격자 | 상세 ──────────────────────────────
        RectTransform body = NewUI("Body", frame);
        HorizontalLayoutGroup bodyRow = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyRow.spacing = 20f;
        bodyRow.childControlWidth = true;
        bodyRow.childControlHeight = true;
        bodyRow.childForceExpandWidth = false;
        bodyRow.childForceExpandHeight = true;
        Element(body.gameObject).flexibleHeight = 1f;

        RectTransform gridPanel = NewUI("CardGridPanel", body);
        Skin(gridPanel.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.05f, 0.13f, 0.18f, 0.9f));
        Fixed(gridPanel.gameObject, 500f, -1f);
        GridLayoutGroup grid = gridPanel.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.cellSize = new Vector2(152f, 104f);
        grid.spacing = new Vector2(10f, 10f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;

        var cardRoots = new GameObject[SlotCount];
        var cardButtons = new Button[SlotCount];
        var cardFrames = new Image[SlotCount];
        var cardNames = new TMP_Text[SlotCount];
        var cardRarities = new TMP_Text[SlotCount];
        var cardMarks = new TMP_Text[SlotCount];

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform card = NewUI("Card_" + i, gridPanel);
            Image cardFrame = card.gameObject.AddComponent<Image>();
            Skin(cardFrame, RoundSprite, Well);

            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = cardFrame;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);   // 칸 색(관측 빈도 색)을 유지한 채 밝게
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;
            AddPoke(card.gameObject, new Vector3(164f, 116f, 180f));

            VerticalLayoutGroup cardStack = card.gameObject.AddComponent<VerticalLayoutGroup>();
            cardStack.padding = new RectOffset(8, 8, 10, 8);
            cardStack.spacing = 0f;
            cardStack.childAlignment = TextAnchor.MiddleCenter;
            cardStack.childControlWidth = true;
            cardStack.childControlHeight = true;
            cardStack.childForceExpandWidth = true;
            cardStack.childForceExpandHeight = false;

            // 이름은 한 줄. 한국어는 글자 단위로 줄이 꺾여 "가위꼬리다트피 / 쉬"처럼 끊기므로 감싸지 않는다.
            // 98종 중 92종이 8글자 이하라 칸 안쪽 136px 에 17px 이상으로 들어간다. 10글자 넘는 3종만 말줄임표 —
            // 전체 이름은 상세 칸에 나온다. 칸 높이는 글자 크기 × 1.45 이상 (낮으면 TMP 가 줄을 숨긴다).
            TMP_Text name = NewText("KoreanName", card, 22f, font, FontStyles.Bold, Ink);
            name.text = "?";
            name.alignment = TextAlignmentOptions.Center;
            SingleLine(name, 14f, 22f);
            Fixed(name.gameObject, -1f, 36f);

            TMP_Text rarity = NewText("Rarity", card, 19f, font, FontStyles.Normal, Muted);
            rarity.alignment = TextAlignmentOptions.Center;
            SingleLine(rarity, 16f, 19f);
            Fixed(rarity.gameObject, -1f, 28f);

            cardRoots[i] = card.gameObject;
            cardButtons[i] = button;
            cardFrames[i] = cardFrame;
            cardNames[i] = name;
            cardRarities[i] = rarity;
            cardMarks[i] = null;   // 모서리 색 점은 뺐다 — 두 줄 이름을 가리고, 색은 빈도 문구가 이미 전한다
        }

        controller.cardRoots = cardRoots;
        controller.cardButtons = cardButtons;
        controller.cardFrames = cardFrames;
        controller.cardNames = cardNames;
        controller.cardRarities = cardRarities;
        controller.cardLockedMarks = cardMarks;

        // 상세: [3D 관찰 칸 | 이름·학명·관측 빈도·횟수] / 설명 / 음성 해설 버튼
        RectTransform detail = NewUI("DetailPanel", body);
        Skin(detail.gameObject.AddComponent<Image>(), RoundSprite, Well);
        Element(detail.gameObject).flexibleWidth = 1f;
        VerticalLayoutGroup detailStack = detail.gameObject.AddComponent<VerticalLayoutGroup>();
        detailStack.padding = new RectOffset(18, 18, 18, 16);
        detailStack.spacing = 12f;
        detailStack.childControlWidth = true;
        detailStack.childControlHeight = true;
        detailStack.childForceExpandWidth = true;
        detailStack.childForceExpandHeight = false;

        RectTransform top = NewRow(detail, "DetailTop", 200f, 16f);
        top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

        RectTransform previewWell = NewUI("PreviewWell", top);
        // 검정 구멍처럼 보이지 않도록 패널과 이어지는 밝은 청록 관찰 영역을 사용한다.
        Skin(previewWell.gameObject.AddComponent<Image>(), RoundSprite, new Color(0.10f, 0.27f, 0.34f, 0.95f));
        Fixed(previewWell.gameObject, 200f, 200f);
        TMP_Text orbitHint = NewText("OrbitHint", previewWell, 19f, font, FontStyles.Normal, new Color(0.55f, 0.78f, 0.84f));
        orbitHint.text = "360° 자동 관찰";
        orbitHint.alignment = TextAlignmentOptions.Bottom;
        Stretch(orbitHint.rectTransform);
        orbitHint.rectTransform.offsetMin = new Vector2(0f, 8f);
        RectTransform previewAnchor = NewUI("PreviewAnchor", previewWell);
        previewAnchor.anchorMin = previewAnchor.anchorMax = new Vector2(0.5f, 0.5f);
        previewAnchor.sizeDelta = Vector2.one;
        // 화면 중심점은 패널 표면에 유지하고, 사용자 쪽 깊이 이동은 런타임에
        // 관찰점-눈 사이의 직선 방향으로 계산해 원근에 의한 좌우 밀림을 막는다.
        previewAnchor.localPosition = new Vector3(0f, 10f, 0f);
        controller.previewAnchor = previewAnchor;

        RectTransform info = NewUI("DetailInfo", top);
        Element(info.gameObject).flexibleWidth = 1f;
        VerticalLayoutGroup infoStack = info.gameObject.AddComponent<VerticalLayoutGroup>();
        infoStack.spacing = 4f;
        infoStack.childControlWidth = true;
        infoStack.childControlHeight = true;
        infoStack.childForceExpandWidth = true;
        infoStack.childForceExpandHeight = false;

        TMP_Text detailName = NewText("DetailName", info, 34f, font, FontStyles.Bold, Ink);
        detailName.text = "생물을 선택하세요";
        SingleLine(detailName, 24f, 34f);
        Fixed(detailName.gameObject, -1f, 50f);
        controller.detailNameText = detailName;

        TMP_Text detailSubtitle = NewText("DetailSubtitle", info, 22f, font, FontStyles.Normal, Muted);
        detailSubtitle.text = FishEncyclopediaPanel.DefaultSubtitle;
        detailSubtitle.textWrappingMode = TextWrappingModes.Normal;
        Fixed(detailSubtitle.gameObject, -1f, 64f);
        detailSubtitle.alignment = TextAlignmentOptions.TopLeft;
        controller.detailSubtitleText = detailSubtitle;

        TMP_Text detailRarity = NewText("DetailRarity", info, 22f, font, FontStyles.Bold, Muted);
        SingleLine(detailRarity, 19f, 22f);
        Fixed(detailRarity.gameObject, -1f, 34f);
        controller.detailRarityText = detailRarity;

        TMP_Text count = NewText("DiscoveryCount", info, 20f, font, FontStyles.Normal, Muted);
        SingleLine(count, 19f, 20f);
        Fixed(count.gameObject, -1f, 32f);
        controller.detailCountText = count;

        TMP_Text detailDescription = NewText("DetailDescription", detail, 26f, font, FontStyles.Normal, Body);
        detailDescription.text = "카드를 선택하면 생김새와 설명이 여기에 표시됩니다.";
        detailDescription.textWrappingMode = TextWrappingModes.Normal;
        detailDescription.overflowMode = TextOverflowModes.Ellipsis;
        detailDescription.lineSpacing = 6f;
        detailDescription.alignment = TextAlignmentOptions.TopLeft;
        Element(detailDescription.gameObject).flexibleHeight = 1f;
        controller.detailDescriptionText = detailDescription;

        RectTransform detailBottom = NewRow(detail, "DetailBottom", 52f, 0f);
        detailBottom.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
        Element(NewUI("Spacer", detailBottom).gameObject).flexibleWidth = 1f;
        Button narrate = AddButton("NarrationButton", detailBottom, 176f, 52f, ObservationTerms.Narration, 22f, font,
            DomeAquariumUiKit.Tone.Primary, DomeAquariumUiKit.MaterialIcon("volume_up"), 28f).button;
        Wire(narrate, controller.PlaySelectedNarration);
        controller.narrationButtonRoot = narrate.gameObject;
        narrate.gameObject.SetActive(false);

        // ── 아래줄: 보기 필터 칩 · 페이지 ────────────────────────
        // 빈도별 필터 다섯 개(흔함~극히 드묾)는 아이들에게 어렵고 쓸 일도 드물다. "남은 생물"을 찾는 세 가지만 둔다.
        RectTransform footer = NewRow(frame, "Footer", 52f, 8f);

        string[] filterLabels = { "전체", ObservationTerms.RecordedOnly, ObservationTerms.NotYetRecorded };
        float[] filterWidths = { 88f, 160f, 196f };
        UnityEngine.Events.UnityAction[] filterActions =
        {
            controller.FilterAll, controller.FilterDiscovered, controller.FilterUndiscovered
        };
        var chips = new Image[filterLabels.Length];
        for (int i = 0; i < filterLabels.Length; i++)
        {
            var chip = AddButton("Filter_" + i, footer, filterWidths[i], 48f, filterLabels[i], 22f, font, DomeAquariumUiKit.Tone.Secondary);
            chip.front.color = i == 0 ? ChipActive : Chip;   // 고른 칩은 앞판 색으로 — FishEncyclopediaPanel 이 바꾼다
            Wire(chip.button, filterActions[i]);
            chips[i] = chip.front;
        }
        controller.filterChips = chips;
        controller.chipColor = Chip;
        controller.chipActiveColor = ChipActive;

        Element(NewUI("Spacer", footer).gameObject).flexibleWidth = 1f;

        Button prevPage = AddButton("PreviousPage", footer, 56f, 48f, "", 0f, font, DomeAquariumUiKit.Tone.Secondary, DomeAquariumUiKit.MaterialIcon("chevron_left"), 36f).button;
        TMP_Text pageText = NewText("PageText", footer, 24f, font, FontStyles.Normal, Muted);
        pageText.text = "1 / 9";
        pageText.alignment = TextAlignmentOptions.Center;
        Fixed(pageText.gameObject, 84f, 48f);
        controller.pageText = pageText;
        Button nextPage = AddButton("NextPage", footer, 56f, 48f, "", 0f, font, DomeAquariumUiKit.Tone.Secondary, DomeAquariumUiKit.MaterialIcon("chevron_right"), 36f).button;
        Wire(prevPage, controller.PreviousPage);
        Wire(nextPage, controller.NextPage);

        LayoutRebuilder.ForceRebuildLayoutImmediate(frame);

        AddWristButton(controller, font);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (saveScene) EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[도감] 관찰 일지 태블릿 적용 완료 ({controller.allFish.Length}종, 로컬 탐험가 3명).");
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

        foreach (string child in new[] { "EncyclopediaButton", "WristPlate", "WristProgress" })
        {
            Transform old = wrist.transform.Find(child);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // 관찰기 흐름(상자 없음): 손목시계처럼 작은 판 위에 "관찰 일지" 버튼과 기록 진행 막대를 둔다.
        // 예전 상자 흐름에서는 "다시 뽑기" 버튼과 위아래로 나눈다.
        RectTransform wristRt = wrist.GetComponent<RectTransform>();
        RectTransform summon = wrist.transform.Find("SummonButton") as RectTransform;
        bool withSummon = summon != null && summon.gameObject.activeSelf;
        if (wristRt != null) wristRt.sizeDelta = withSummon ? new Vector2(300f, 270f) : WristSize;
        if (withSummon)
        {
            summon.sizeDelta = new Vector2(300f, 128f);
            summon.anchoredPosition = new Vector2(0f, 70f);
            TMP_Text summonLabel = summon.GetComponentInChildren<TMP_Text>(true);
            if (summonLabel != null) summonLabel.rectTransform.sizeDelta = new Vector2(300f, 128f);
        }
        else
        {
            RectTransform plate = NewUI("WristPlate", wrist.transform);
            Stretch(plate);
            DomeAquariumUiKit.TemplatePlate(plate, 24f, DomeAquariumUiKit.PlateFill, DomeAquariumUiKit.PlateLine);
            plate.GetComponent<Image>().raycastTarget = false;
            plate.SetAsFirstSibling();
        }

        // 손목 버튼은 손에 붙어 작게 보인다 — 아이콘을 키우고 모서리·들뜸도 크게(0.0006m/px: 반경 12mm, 들뜸 8mm).
        Vector2 buttonSize = withSummon ? new Vector2(300f, 128f) : WristButtonSize;
        Button button = AddButton("EncyclopediaButton", wrist.transform, buttonSize.x, buttonSize.y, ObservationTerms.OpenLog, 36f, font,
            DomeAquariumUiKit.Tone.Primary, DomeAquariumUiKit.JournalIcon, 60f, corner: 20f, lift: 14f).button;
        RectTransform buttonRt = button.GetComponent<RectTransform>();
        buttonRt.anchoredPosition = withSummon ? new Vector2(0f, -70f) : WristButtonPosition;
        Wire(button, controller.Toggle);

        WristMenu menu = wrist.GetComponent<WristMenu>();
        if (menu == null) return;
        menu.log = controller;
        menu.progressText = null;
        menu.progressFill = null;
        if (withSummon) { EditorUtility.SetDirty(menu); return; }

        // 아래줄: "21 / 98종 기록" + 진행 막대 (WristMenu 가 보이는 동안 0.5초마다 다시 센다)
        RectTransform progress = NewUI("WristProgress", wrist.transform);
        progress.sizeDelta = new Vector2(WristButtonSize.x, 44f);
        progress.anchoredPosition = new Vector2(0f, -66f);

        TMP_Text count = NewText("Count", progress, 22f, font, FontStyles.Bold, Muted);
        count.rectTransform.anchorMin = new Vector2(0f, 1f);
        count.rectTransform.anchorMax = new Vector2(1f, 1f);
        count.rectTransform.pivot = new Vector2(0.5f, 1f);
        count.rectTransform.sizeDelta = new Vector2(0f, 32f);
        count.rectTransform.anchoredPosition = Vector2.zero;
        count.alignment = TextAlignmentOptions.MidlineLeft;
        count.text = ObservationTerms.WristProgress(0, controller.allFish.Length);

        RectTransform track = NewUI("Track", progress);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(1f, 0f);
        track.pivot = new Vector2(0.5f, 0f);
        track.sizeDelta = new Vector2(0f, 10f);
        track.anchoredPosition = Vector2.zero;
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DomeAquariumUiKit.TemplateRoundPath);
        trackImage.type = Image.Type.Sliced;
        trackImage.pixelsPerUnitMultiplier = 8f;          // 모서리 40px → 5px, 막대 높이의 절반
        trackImage.color = new Color(0.45f, 0.88f, 1f, 0.14f);
        trackImage.raycastTarget = false;

        RectTransform fill = NewUI("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.sizeDelta = Vector2.zero;
        fill.anchoredPosition = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.sprite = trackImage.sprite;
        fillImage.type = Image.Type.Sliced;
        fillImage.pixelsPerUnitMultiplier = 8f;
        fillImage.color = new Color(0.45f, 0.88f, 1f, 1f);
        fillImage.raycastTarget = false;

        menu.progressText = count;
        menu.progressFill = fill;
        menu.progressWidth = WristButtonSize.x;
        EditorUtility.SetDirty(menu);
    }

    // 손목 메뉴 (0.0006m/px): 판 19 × 12cm, 버튼 17 × 6cm — Meta 손목 메뉴 예시보다 약간 크게(장갑 낀 듯한 아이 손가락 기준)
    public static readonly Vector2 WristSize = new Vector2(320f, 200f);
    public static readonly Vector2 WristButtonSize = new Vector2(288f, 104f);
    public static readonly Vector2 WristButtonPosition = new Vector2(0f, 30f);

    /// <summary>관찰 일지에 담을 종 — 층별 하강에서 실제로 만날 수 있는 종만 (18 전이면 강에 사는 종만 뺀다).</summary>
    internal static FishData[] LoadAllFish()
    {
        var meetable = DomeAquariumDiveSetup.MeetableFish();
        return AssetDatabase.FindAssets("t:FishData", new[] { "Assets/DomeAquarium/FishData" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(fish => fish != null && (meetable != null ? meetable.Contains(fish) : !fish.freshwater))
            .OrderBy(fish => (int)fish.rarity)
            .ThenBy(fish => fish.DisplayName)
            .ToArray();
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// VR 템플릿식 누르는 버튼 (DomeAquariumUiKit.PokeButton) — 손가락으로 누르면 앞판이 들어가고, 레이가 닿으면 테두리가 밝아진다.
    /// 화살표·재생은 VR 템플릿 아이콘, 닫기·관찰 일지는 같은 규격으로 그린 아이콘을 쓴다 (예전에는 "‹ › × ▶" 글자라 줄 높이가 어긋났다).
    /// 레이아웃 그룹 안에서는 크기를 LayoutElement 로 정한다.
    /// </summary>
    static DomeAquariumUiKit.ButtonParts AddButton(string name, Transform parent, float width, float height, string label,
        float fontSize, TMP_FontAsset font, DomeAquariumUiKit.Tone tone, Sprite icon = null, float iconSize = 0f,
        float corner = 12f, float lift = 10f)
    {
        RectTransform rt = NewUI(name, parent);
        Fixed(rt.gameObject, width, height);
        var parts = DomeAquariumUiKit.PokeButton(rt, new Vector2(width, height), label, icon, tone, fontSize, font, iconSize, corner, lift);
        AddPoke(rt.gameObject, new Vector3(width + 12f, height + 12f, 180f));
        return parts;
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
        Rigidbody rb = target.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        PokeSelector poke = target.AddComponent<PokeSelector>();
        poke.mysteryBox = null;
        poke.handTag = HandTag;
        poke.inputCooldown = 0.45f;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    static RectTransform NewRow(Transform parent, string name, float height, float spacing)
    {
        RectTransform rt = NewUI(name, parent);
        HorizontalLayoutGroup row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        Fixed(rt.gameObject, -1f, height);
        return rt;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, float fontSize, TMP_FontAsset font, FontStyles style, Color color)
    {
        RectTransform rt = NewUI(name, parent);
        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        if (font != null) text.font = font;
        return text;
    }

    /// <summary>한 줄 고정. 넘치면 글자를 줄이고, 그래도 안 되면 말줄임표. 칸 높이는 글자 크기의 1.45배 이상으로 둔다.</summary>
    static void SingleLine(TMP_Text text, float minSize, float maxSize)
    {
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
    }

    static LayoutElement Element(GameObject go)
    {
        var le = go.GetComponent<LayoutElement>();
        return le != null ? le : go.AddComponent<LayoutElement>();
    }

    /// <summary>음수는 "정하지 않음".</summary>
    static void Fixed(GameObject go, float width, float height)
    {
        var le = Element(go);
        if (width >= 0f) le.minWidth = le.preferredWidth = width;
        if (height >= 0f) le.minHeight = le.preferredHeight = height;
    }

    static void Ignore(GameObject go) => Element(go).ignoreLayout = true;

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Skin(Image image, string spritePath, Color color)
    {
        image.color = color;
        image.raycastTarget = true;
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null) return;
        image.sprite = sprite;
        bool sliced = sprite.border.sqrMagnitude > 0f;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        if (sliced && spritePath.StartsWith("Assets/VRTemplateAssets", System.StringComparison.Ordinal))
            image.pixelsPerUnitMultiplier = CornerMultiplier;
    }
}
#endif
