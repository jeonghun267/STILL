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

    static void Draw(string path, bool force, int size, System.Func<float, float, float> alpha, int border)
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
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
#endif
