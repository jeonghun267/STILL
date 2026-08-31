#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity UI Samples 의 둥근 스프라이트는 9-슬라이스 border 가 0으로 들어와 있다.
/// 그대로 600x400 패널에 늘리면 둥근 모서리가 타원으로 찌그러진다.
/// 모서리 반경만큼 border 를 넣어 Sliced 로 늘어나게 만든다.
/// </summary>
public static class DomeAquariumUiSprites
{
    const string Dir = "Assets/Unity UI Samples/Textures and Sprites/Rounded UI";

    // 파일명 -> border(px). 원본 크기의 모서리 반경에 맞춘 값.
    static readonly (string file, int border)[] Targets =
    {
        ("UIPanel.png", 64),           // 256x256
        ("UIButtonDefault.png", 40),   // 128x128
        ("UIButtonCorner.png", 40),    // 128x128
        ("UIToggleButton.png", 12),    // 32x32
    };

    [MenuItem("DomeAquarium/9. UI 스프라이트 9-슬라이스 설정", priority = 9)]
    public static int ApplyBorders()
    {
        int changed = 0;

        foreach (var (file, b) in Targets)
        {
            string path = $"{Dir}/{file}";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            var border = new Vector4(b, b, b, b);
            if (importer.spriteBorder != border)
            {
                importer.spriteBorder = border;
                dirty = true;
            }

            // VR 에서 가까이 보는 UI라 필터링/압축이 눈에 띈다.
            if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }

            if (!dirty) continue;

            importer.SaveAndReimport();
            changed++;
            Debug.Log($"[DomeAquarium] 9-슬라이스 설정: {file} border={b}px");
        }

        if (changed == 0) Debug.Log("[DomeAquarium] UI 스프라이트 설정이 이미 최신이다.");
        return changed;
    }
}
#endif
