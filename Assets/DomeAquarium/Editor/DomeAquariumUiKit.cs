#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// STILL 전용 UI 스프라이트 — 판·칩·버튼이 모두 같은 모서리를 쓰도록 직접 그려 만든다.
///
/// 예전에는 VR 템플릿의 "Round Radius 10" 스프라이트(9-슬라이스 테두리 40px)를 그대로 써서
/// 모서리 반경이 판 크기에 비해 크고, 외곽선 스프라이트와 반경이 달라 둥근 듯 만 듯 어중간해 보였다.
/// 여기서는 반경 10px · 외곽선 1.5px 를 한 벌로 그려 배수 없이(PPU 100, 배수 1) 쓴다.
/// 카드(0.0009m/px)에서 반경 9mm, 일지(0.00075m/px)에서 7.5mm — 또렷하되 각지지 않은 모서리.
/// </summary>
public static class DomeAquariumUiKit
{
    public const string Folder = "Assets/DomeAquarium/UI/Kit";
    public const string PanelPath = Folder + "/Kit_Panel.png";
    public const string OutlinePath = Folder + "/Kit_Outline.png";
    public const string DotPath = Folder + "/Kit_Dot.png";
    public const string LinePath = Folder + "/Kit_Line.png";      // 판 테두리 — VR 템플릿 판과 같은 원호, 선만 2px

    const int Size = 48;          // 반경 10 + 테두리 여유. 9-슬라이스 경계 14px
    const float Radius = 10f;
    const int Border = 14;
    const float Stroke = 1.5f;

    [MenuItem("DomeAquarium/9. UI 키트 스프라이트 다시 그리기", priority = 9)]
    public static void Rebuild()
    {
        EnsureAll(force: true);
        Debug.Log($"[UI 키트] 스프라이트를 다시 그렸다: {Folder}");
    }

    /// <summary>스프라이트가 없으면 그린다. 패널 빌더들이 시작할 때 부른다.</summary>
    public static void EnsureAll(bool force = false)
    {
        Directory.CreateDirectory(Path.GetFullPath(Folder));
        Draw(PanelPath, force, Size, (x, y) => Fill(RoundedRectDistance(x, y, Size, Radius)), Border);
        Draw(OutlinePath, force, Size, (x, y) =>
        {
            float d = RoundedRectDistance(x, y, Size, Radius);
            return Fill(d) * Mathf.Clamp01(0.5f + d + Stroke);   // 안쪽으로 Stroke 두께만 남긴다
        }, Border);
        Draw(DotPath, force, 32, (x, y) =>
        {
            float dx = x - 16f, dy = y - 16f;
            return Fill(Mathf.Sqrt(dx * dx + dy * dy) - 15f);
        }, 0);
        // VR 템플릿 외곽선은 모서리 반경의 1/4 굵기라 큰 판에서는 두껍다(반경 24 → 6px). 같은 원호에 선만 2px 로 그린다.
        Draw(LinePath, force, 96, (x, y) =>
        {
            float d = RoundedRectDistance(x, y, 96, 24f);
            return Fill(d) * Mathf.Clamp01(0.5f + d + 2f);
        }, 28);
        DrawIcons(force);
    }

    /// <summary>
    /// 장면의 버튼을 VR 템플릿식 누르는 버튼으로 바꾼다 — 시작 패널의 "입장하기"는 그 자리에서 겉모습만 바꾸고,
    /// 관찰 일지 태블릿과 손목 "관찰 일지" 버튼은 태블릿 빌더(15번)로 다시 만든 뒤 관찰기의 일지 참조를 다시 잇는다.
    /// </summary>
    [MenuItem("DomeAquarium/16. 버튼 스타일 적용 (VR 템플릿)", priority = 16)]
    public static void ApplyButtonStyle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[UI 키트] 플레이 모드에서는 버튼을 바꿀 수 없다.");
            return;
        }
        EnsureAll();
        var font = DomeAquariumSetup.LoadKoreanFont();

        var startRoot = GameObject.Find("StartPanelRoot");
        var panel = startRoot != null ? startRoot.transform.Find("Panel") as RectTransform : null;
        if (panel != null) TemplatePlate(panel, 24f, PlateFill, PlateLine);   // 버튼과 같은 모서리 — 각진 판에 둥근 버튼이 따로 놀지 않게
        var start = panel != null ? panel.Find("StartButton") as RectTransform : null;
        if (start != null)
            PokeButton(start, new Vector2(320f, 100f), ObservationTerms.Enter, null, Tone.Primary, 34f, font, corner: 18f, lift: 12f);
        else Debug.LogWarning("[UI 키트] 시작 패널의 StartButton 을 찾지 못했다.");

        var log = DomeAquariumEncyclopediaSetup.Apply(saveScene: false);
        if (log != null)
            foreach (var aim in Object.FindObjectsByType<ObservationAim>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                aim.logPanel = log;
                EditorUtility.SetDirty(aim);
            }

        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("[UI 키트] 버튼을 VR 템플릿식으로 바꿨다 (시작 패널 · 관찰 일지 · 손목 버튼).");
    }

    public static Sprite Panel => AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
    public static Sprite Outline => AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);
    public static Sprite Dot => AssetDatabase.LoadAssetAtPath<Sprite>(DotPath);

    // ─────────────────────────────────────────────────────────────

    /// <summary>둥근 사각형 경계까지의 부호 있는 거리(px). 안쪽이 음수.</summary>
    static float RoundedRectDistance(float x, float y, int size, float radius)
    {
        float half = size * 0.5f;
        float qx = Mathf.Abs(x - half) - (half - radius);
        float qy = Mathf.Abs(y - half) - (half - radius);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside - radius;
    }

    /// <summary>경계에서 1px 폭으로 부드럽게 — 계단 없이 또렷한 가장자리.</summary>
    static float Fill(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance);

    static void Draw(string path, bool force, int size, System.Func<float, float, float> alpha, int border, bool mipmaps = false)
    {
        if (!force && File.Exists(Path.GetFullPath(path))) return;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x + 0.5f, y + 0.5f) * 255f));
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.mipmapEnabled = mipmaps;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ─────────────────────────────────────────────────────────────
    // 버튼 — Unity VR 템플릿(Assets/VRTemplateAssets)의 "Text Poke Button" 구성을 따른다
    // ─────────────────────────────────────────────────────────────
    //
    //  버튼 루트   ─ 우물(Well): 눌려 들어갈 자리. 옅은 판 + 레이 판정 영역
    //   └ 앞판(Front) ─ 루트보다 Lift 만큼 떠 있다. 손가락으로 누르면 XRPokeFollowAffordanceFill 이 우물까지 밀어 넣는다
    //       ├ 차오름(Fill)  ─ 누르는 깊이만큼 가운데서 번져 나온다 (앞판 Mask 안)
    //       ├ 내용(Content) ─ 아이콘 + 글자
    //       └ 테두리(Hover) ─ 레이·손가락이 닿으면 밝게 (Button 색 전환)
    //
    //  모서리는 VR 템플릿 "Round Radius 10"(400px, 9-슬라이스 40px)을 배수로 줄여 반경을 맞춘다.
    //  아이콘은 VR 템플릿 아이콘(선 6px / 128px)을 쓰고, 템플릿에 없는 닫기·관찰 일지만 같은 규격으로 그린다.

    public const string TemplateRoundPath = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png";
    public const string TemplateOutlinePath = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10 Outline.png";
    public const string TemplateTopPath = "Assets/VRTemplateAssets/Sprites/UI/Round Radius 10 Top.png";
    const string TemplateIconFolder = "Assets/VRTemplateAssets/Sprites/Icons";
    const float TemplateCorner = 40f;        // Round Radius 10 의 모서리(px)
    public const string CloseIconPath = Folder + "/Kit_Icon_Close.png";
    public const string JournalIconPath = Folder + "/Kit_Icon_Journal.png";

    public static Sprite TemplateIcon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{TemplateIconFolder}/{name}.png");
    public static Sprite CloseIcon => AssetDatabase.LoadAssetAtPath<Sprite>(CloseIconPath);
    public static Sprite JournalIcon => AssetDatabase.LoadAssetAtPath<Sprite>(JournalIconPath);

    public enum Tone { Primary, Secondary }

    public static readonly Color PrimaryFront = new Color(0.05f, 0.42f, 0.52f, 1f);
    public static readonly Color SecondaryFront = new Color(0.10f, 0.21f, 0.27f, 1f);
    public static readonly Color PrimaryInk = Color.white;
    public static readonly Color SecondaryInk = new Color(0.86f, 0.94f, 0.98f, 1f);
    static readonly Color WellColor = new Color(1f, 1f, 1f, 0.07f);
    static readonly Color FillColor = new Color(1f, 1f, 1f, 0.16f);
    static readonly Color HoverColor = new Color(0.50f, 0.90f, 1f, 1f);
    static readonly Color SheenColor = new Color(0.40f, 0.85f, 1f, 0.06f);   // 리니어 색 공간 — 흰색을 얹으면 어두운 청록이 회색으로 바랜다

    public struct ButtonParts
    {
        public UnityEngine.UI.Button button;
        public UnityEngine.UI.Image front;
        public UnityEngine.UI.Image icon;
        public TMPro.TMP_Text label;
    }

    /// <summary>
    /// rect 를 VR 템플릿식 누르는 버튼으로 꾸민다. 이미 있는 Button·onClick·PokeSelector 는 그대로 두고 겉모습만 새로 만든다
    /// (기존 자식은 지운다). label 이 비면 아이콘만, icon 이 없으면 글자만.
    /// </summary>
    public static ButtonParts PokeButton(RectTransform rect, Vector2 size, string label, Sprite icon, Tone tone,
                                         float fontSize, TMPro.TMP_FontAsset font, float iconSize = 0f,
                                         float corner = 14f, float lift = 10f)
    {
        for (int i = rect.childCount - 1; i >= 0; i--) Object.DestroyImmediate(rect.GetChild(i).gameObject);
        rect.sizeDelta = size;
        float multiplier = TemplateCorner / Mathf.Max(1f, corner);
        Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(TemplateRoundPath);
        Sprite outline = AssetDatabase.LoadAssetAtPath<Sprite>(TemplateOutlinePath);
        Color front = tone == Tone.Primary ? PrimaryFront : SecondaryFront;
        Color ink = tone == Tone.Primary ? PrimaryInk : SecondaryInk;

        var well = rect.GetComponent<UnityEngine.UI.Image>();
        if (well == null) well = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        SkinSliced(well, round, WellColor, multiplier);
        well.raycastTarget = true;                         // 앞판이 움직여도 판정 영역은 제자리

        var frontImage = Child("Front", rect, round, front, multiplier);
        frontImage.rectTransform.localPosition = new Vector3(0f, 0f, -lift);
        frontImage.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = true;

        var fill = Child("Fill", frontImage.rectTransform, round, FillColor, multiplier);
        fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        if (tone == Tone.Primary)
        {
            // 윗쪽 절반에 옅은 광택 — VR 템플릿 "Round Radius 10 Top"(위만 둥근 판). 평평한 색 판이 손에 잡히는 물건처럼 보인다.
            var sheen = Child("Sheen", frontImage.rectTransform, AssetDatabase.LoadAssetAtPath<Sprite>(TemplateTopPath), SheenColor, multiplier);
            sheen.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        }

        var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(frontImage.rectTransform, false);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;
        var row = content.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = Mathf.Round(fontSize * 0.35f);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var parts = new ButtonParts { front = frontImage };
        if (icon != null)
        {
            float s = iconSize > 0f ? iconSize : Mathf.Round(size.y * 0.5f);
            var iconImage = new GameObject("Icon", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
            iconImage.rectTransform.SetParent(content, false);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.color = ink;
            iconImage.raycastTarget = false;
            var e = iconImage.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            e.minWidth = e.preferredWidth = s;
            e.minHeight = e.preferredHeight = s;
            parts.icon = iconImage;
        }
        if (!string.IsNullOrEmpty(label))
        {
            var text = new GameObject("Label", typeof(RectTransform)).AddComponent<TMPro.TextMeshProUGUI>();
            text.rectTransform.SetParent(content, false);
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = TMPro.FontStyles.Bold;
            text.color = ink;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = label;
            parts.label = text;
        }

        var hover = Child("Hover", frontImage.rectTransform, outline, HoverColor, multiplier);

        var button = rect.GetComponent<UnityEngine.UI.Button>();
        if (button == null) button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        button.targetGraphic = hover;
        var colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = new Color(1f, 1f, 1f, 0f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var nav = button.navigation;
        nav.mode = UnityEngine.UI.Navigation.Mode.None;     // 레이로 누른 뒤 "선택됨" 상태로 테두리가 남지 않게
        button.navigation = nav;
        parts.button = button;

        var follow = rect.GetComponent<Unity.VRTemplate.XRPokeFollowAffordanceFill>();
        if (follow == null) follow = rect.gameObject.AddComponent<Unity.VRTemplate.XRPokeFollowAffordanceFill>();
        var so = new SerializedObject(follow);
        so.FindProperty("m_PokeFollowTransform").objectReferenceValue = frontImage.transform;
        so.FindProperty("m_PokeFill").objectReferenceValue = fill.rectTransform;
        so.FindProperty("m_PokeFillMaxSizeX").floatValue = size.x;
        so.FindProperty("m_PokeFillMaxSizeY").floatValue = size.y;
        so.FindProperty("m_ClampToMaxDistance").boolValue = true;
        so.FindProperty("m_MaxDistance").floatValue = lift;
        so.ApplyModifiedPropertiesWithoutUndo();
        return parts;
    }

    public static readonly Color PlateFill = new Color(0.05f, 0.11f, 0.15f, 0.94f);
    public static readonly Color PlateLine = new Color(0.45f, 0.88f, 1f, 0.40f);

    /// <summary>
    /// VR 템플릿 "Spatial Panel" 처럼 — 어두운 둥근 판 + 옅은 외곽선. 외곽선은 첫 자식으로 넣어 글자 밑에 깔린다.
    /// </summary>
    public static UnityEngine.UI.Image TemplatePlate(RectTransform rect, float corner, Color fill, Color line)
    {
        var image = rect.GetComponent<UnityEngine.UI.Image>();
        if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        float multiplier = TemplateCorner / Mathf.Max(1f, corner);
        SkinSliced(image, AssetDatabase.LoadAssetAtPath<Sprite>(TemplateRoundPath), fill, multiplier);
        Transform old = rect.Find("Outline");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var outline = Child("Outline", rect, AssetDatabase.LoadAssetAtPath<Sprite>(LinePath), line, 24f / Mathf.Max(1f, corner));
        outline.transform.SetSiblingIndex(0);
        return image;
    }

    static UnityEngine.UI.Image Child(string name, RectTransform parent, Sprite sprite, Color color, float multiplier)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        SkinSliced(image, sprite, color, multiplier);
        image.raycastTarget = false;
        return image;
    }

    static void SkinSliced(UnityEngine.UI.Image image, Sprite sprite, Color color, float multiplier)
    {
        image.sprite = sprite;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = multiplier;
        image.fillCenter = true;
        image.color = color;
    }

    // ── 템플릿에 없는 아이콘 — 128px, 선 6px, 둥근 끝 (VR 템플릿 아이콘과 같은 규격) ──

    const int IconSize = 128;
    const float IconStroke = 3f;             // 선 두께의 절반

    static void DrawIcons(bool force)
    {
        // 닫기: 대각선 두 줄
        Draw(CloseIconPath, force, IconSize, (x, y) =>
            Fill(Mathf.Min(Segment(x, y, 38f, 38f, 90f, 90f), Segment(x, y, 90f, 38f, 38f, 90f)) - IconStroke), 0, mipmaps: true);

        // 관찰 일지: 공책 — 표지 테두리, 왼쪽 묶음 줄, 본문 두 줄
        Draw(JournalIconPath, force, IconSize, (x, y) =>
        {
            float cover = Mathf.Abs(RoundedBox(x, y, 64f, 64f, 32f, 44f, 9f)) - IconStroke;
            float spine = Segment(x, y, 47f, 24f, 47f, 104f) - IconStroke;
            float line1 = Segment(x, y, 60f, 76f, 82f, 76f) - IconStroke;
            float line2 = Segment(x, y, 60f, 60f, 82f, 60f) - IconStroke;
            return Fill(Mathf.Min(Mathf.Min(cover, spine), Mathf.Min(line1, line2)));
        }, 0, mipmaps: true);
    }

    /// <summary>점에서 선분까지의 거리(px).</summary>
    static float Segment(float x, float y, float ax, float ay, float bx, float by)
    {
        var p = new Vector2(x - ax, y - ay);
        var d = new Vector2(bx - ax, by - ay);
        float t = Mathf.Clamp01(Vector2.Dot(p, d) / d.sqrMagnitude);
        return (p - d * t).magnitude;
    }

    /// <summary>중심 (cx, cy), 반폭 (hx, hy), 모서리 반경 r 인 둥근 사각형까지의 부호 있는 거리.</summary>
    static float RoundedBox(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - (hx - r);
        float qy = Mathf.Abs(y - cy) - (hy - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside - r;
    }
}
#endif
