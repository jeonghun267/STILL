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
/// </summary>
public static class DomeAquariumInfoPanelSetup
{
    const string RoundPath = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png";
    const string OutlinePath = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10 Outline.png";
    const string CirclePath = "Assets/VRTemplateAssets/Sprites/UI/Circle_60x60 Outline 4.png";

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

        foreach (FishInfoPanel info in Resources.FindObjectsOfTypeAll<FishInfoPanel>())
        {
            if (info == null || EditorUtility.IsPersistent(info) || !info.gameObject.scene.IsValid()) continue;
            bool oldRarityLayout = info.rarityLabel != null
                && !Mathf.Approximately(info.rarityLabel.rectTransform.sizeDelta.x, 280f);
            RectTransform narrationStatus = FindRect(info.transform, "NarrationPill");
            bool oldNarrationLayout = narrationStatus == null
                || !Mathf.Approximately(narrationStatus.sizeDelta.x, 154f);
            if (FindRect(info.transform, "PanelOutline") == null || oldRarityLayout || oldNarrationLayout)
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

        Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath);
        Sprite outline = AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);
        Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);

        RectTransform root = info.GetComponent<RectTransform>();
        RectTransform panel = FindRect(info.transform, "Panel");
        if (root == null || panel == null) return;

        SetRect(root, new Vector2(720f, 440f), Vector2.zero);
        root.localScale = Vector3.one * 0.0009f;
        SetRect(panel, new Vector2(720f, 440f), Vector2.zero);

        Image background = panel.GetComponent<Image>();
        if (background == null) background = panel.gameObject.AddComponent<Image>();
        Skin(background, round, new Color(0.018f, 0.075f, 0.115f, 0.94f));

        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        info.canvasGroup = group;

        Image border = EnsureImage(panel, "PanelOutline", new Vector2(720f, 440f), Vector2.zero);
        Skin(border, outline, new Color(0.25f, 0.83f, 1f, 0.48f));
        border.rectTransform.SetAsFirstSibling();

        Image header = EnsureImage(panel, "HeaderWash", new Vector2(690f, 96f), new Vector2(0f, 158f));
        Skin(header, round, new Color(0.04f, 0.33f, 0.43f, 0.42f));
        header.rectTransform.SetSiblingIndex(1);

        Image accent = EnsureImage(panel, "AccentLine", new Vector2(6f, 278f), new Vector2(-336f, -30f));
        Skin(accent, round, new Color(0.18f, 0.82f, 1f, 0.92f));
        accent.rectTransform.SetSiblingIndex(2);

        TMP_Text collection = EnsureText(panel, "CollectionLabel", info.nameText != null ? info.nameText.font : null);
        SetRect(collection.rectTransform, new Vector2(260f, 28f), new Vector2(200f, 190f));
        collection.text = "MARINE DISCOVERY";
        collection.fontSize = 18f;
        collection.fontStyle = FontStyles.Bold;
        collection.alignment = TextAlignmentOptions.Right;
        collection.color = new Color(0.48f, 0.88f, 1f, 0.78f);

        RectTransform rarityRow = FindRect(panel, "RarityRow");
        if (rarityRow != null) SetRect(rarityRow, new Vector2(650f, 40f), new Vector2(0f, 174f));

        if (info.rarityGlow != null)
        {
            SetRect(info.rarityGlow.rectTransform, new Vector2(150f, 34f), new Vector2(-250f, 0f));
            Skin(info.rarityGlow, round, new Color(1f, 0.78f, 0.25f, 0.24f));
        }
        if (info.rarityBadge != null)
        {
            SetRect(info.rarityBadge.rectTransform, new Vector2(24f, 24f), new Vector2(-308f, 0f));
            Skin(info.rarityBadge, circle, Color.white);
        }
        if (info.rarityLabel != null)
        {
            SetRect(info.rarityLabel.rectTransform, new Vector2(280f, 30f), new Vector2(-155f, 0f));
            info.rarityLabel.fontSize = 21f;
            info.rarityLabel.fontStyle = FontStyles.Bold;
            info.rarityLabel.color = new Color(0.77f, 0.90f, 0.97f);
        }

        if (info.nameText != null)
        {
            SetRect(info.nameText.rectTransform, new Vector2(650f, 56f), new Vector2(0f, 118f));
            info.nameText.fontSize = 47f;
            info.nameText.fontStyle = FontStyles.Bold;
            info.nameText.color = Color.white;
        }
        if (info.scientificText != null)
        {
            SetRect(info.scientificText.rectTransform, new Vector2(650f, 30f), new Vector2(0f, 72f));
            info.scientificText.fontSize = 22f;
            info.scientificText.color = new Color(0.46f, 0.78f, 0.90f);
        }

        RectTransform divider = FindRect(panel, "Divider");
        if (divider != null)
        {
            SetRect(divider, new Vector2(650f, 2f), new Vector2(0f, 46f));
            Image dividerImage = divider.GetComponent<Image>();
            if (dividerImage != null) dividerImage.color = new Color(0.20f, 0.76f, 1f, 0.42f);
        }

        if (info.descriptionText != null)
        {
            SetRect(info.descriptionText.rectTransform, new Vector2(620f, 174f), new Vector2(8f, -54f));
            info.descriptionText.fontSize = 29f;
            info.descriptionText.lineSpacing = 3f;
            info.descriptionText.color = new Color(0.89f, 0.95f, 0.99f);
        }

        RectTransform tagRow = FindRect(panel, "TagRow");
        if (tagRow != null) SetRect(tagRow, new Vector2(620f, 36f), new Vector2(0f, -184f));
        if (info.sizeTagText != null)
        {
            SetRect(info.sizeTagText.rectTransform, new Vector2(255f, 32f), new Vector2(-174f, 0f));
            info.sizeTagText.fontSize = 21f;
            info.sizeTagText.color = new Color(0.58f, 0.82f, 0.92f);
        }
        if (info.habitatTagText != null)
        {
            SetRect(info.habitatTagText.rectTransform, new Vector2(255f, 32f), new Vector2(96f, 0f));
            info.habitatTagText.fontSize = 21f;
            info.habitatTagText.color = new Color(0.58f, 0.82f, 0.92f);
        }

        // 버튼처럼 보이던 긴 문구 대신, 읽기 전용 음성 기능임을 보여 주는 작은 상태 푸터.
        Image narrationPill = EnsureImage(panel, "NarrationPill", new Vector2(154f, 30f), new Vector2(258f, -190f));
        Skin(narrationPill, round, new Color(0.05f, 0.31f, 0.39f, 0.30f));

        Image voiceMark = EnsureImage(narrationPill.rectTransform, "VoiceMark", new Vector2(22f, 22f), new Vector2(-60f, 0f));
        Skin(voiceMark, circle, new Color(0.22f, 0.82f, 1f, 0.76f));
        Image voiceBarLeft = EnsureImage(voiceMark.rectTransform, "VoiceBarLeft", new Vector2(2f, 7f), new Vector2(-4f, 0f));
        Image voiceBarCenter = EnsureImage(voiceMark.rectTransform, "VoiceBarCenter", new Vector2(2f, 12f), Vector2.zero);
        Image voiceBarRight = EnsureImage(voiceMark.rectTransform, "VoiceBarRight", new Vector2(2f, 8f), new Vector2(4f, 0f));
        Skin(voiceBarLeft, round, new Color(0.56f, 0.93f, 1f, 0.94f));
        Skin(voiceBarCenter, round, new Color(0.56f, 0.93f, 1f, 0.94f));
        Skin(voiceBarRight, round, new Color(0.56f, 0.93f, 1f, 0.94f));

        TMP_Text narration = EnsureText(narrationPill.rectTransform, "NarrationLabel", info.nameText != null ? info.nameText.font : null);
        SetRect(narration.rectTransform, new Vector2(108f, 24f), new Vector2(18f, 0f));
        narration.text = "음성 해설";
        narration.fontSize = 17f;
        narration.fontStyle = FontStyles.Bold;
        narration.alignment = TextAlignmentOptions.MidlineLeft;
        narration.color = new Color(0.76f, 0.93f, 1f, 0.92f);

        info.dropBelowFish = 0.04f;
        info.sideOffset = -0.50f;
        info.towardViewerOffset = 0.06f;

        EditorUtility.SetDirty(info);
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(panel);
    }

    static RectTransform FindRect(Transform root, string objectName)
    {
        foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == objectName) return rt;
        return null;
    }

    static Image EnsureImage(Transform parent, string objectName, Vector2 size, Vector2 position)
    {
        RectTransform rt = FindRect(parent, objectName);
        if (rt == null)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }

        SetRect(rt, size, position);
        Image image = rt.GetComponent<Image>();
        if (image == null) image = rt.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text EnsureText(Transform parent, string objectName, TMP_FontAsset font)
    {
        RectTransform rt = FindRect(parent, objectName);
        if (rt == null)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }

        TMP_Text text = rt.GetComponent<TMP_Text>();
        if (text == null) text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.raycastTarget = false;
        return text;
    }

    static void Skin(Image image, Sprite sprite, Color color)
    {
        image.color = color;
        image.sprite = sprite;
        image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
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
