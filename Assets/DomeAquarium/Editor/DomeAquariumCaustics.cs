#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 수중 코스틱(수면 굴절로 바닥에 생기는 하얀 그물 무늬) 텍스처를 절차적으로 만든다.
///
/// 만드는 법: 토러스 위에 점을 흩뿌리고 각 픽셀에서 가장 가까운 두 점까지의 거리 차(F2-F1)를 본다.
/// 셀 경계에서 이 값이 0 에 가까워지므로, 그 부근만 밝게 하면 실제 코스틱과 같은
/// "가는 밝은 그물" 이 나온다. 토러스 거리라 상하좌우로 이어 붙여도 이음매가 없다.
///
/// 밝기 범위를 0.55~1.0 으로 잡은 이유: 이 텍스처는 라이트 쿠키로 쓰이므로 곧 조명의 곱셈 항이다.
/// 0 에 가까운 값이 있으면 그 부분이 캄캄해진다. 어둡게 만드는 게 아니라 밝은 부분만 더 밝게 해야 한다.
/// </summary>
public static class DomeAquariumCaustics
{
    const string TexPath = "Assets/DomeAquarium/Materials/Caustics.png";
    const int Size = 512;
    const int Points = 42;
    const int Seed = 20260828;

    [MenuItem("DomeAquarium/12. 수중 코스틱 텍스처 생성", priority = 12)]
    public static Texture2D Generate()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (existing != null)
        {
            Debug.Log($"[DomeAquarium] 코스틱 텍스처가 이미 있다: {TexPath}");
            return existing;
        }

        var rng = new System.Random(Seed);
        var px = new Vector2[Points];
        for (int i = 0; i < Points; i++)
            px[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());

        var pixels = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            float v = (y + 0.5f) / Size;
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size;

                float f1 = 999f, f2 = 999f;
                for (int i = 0; i < Points; i++)
                {
                    // 토러스 거리 — 가장자리를 넘어가는 쪽이 더 가까우면 그쪽을 쓴다.
                    float dx = Mathf.Abs(u - px[i].x); if (dx > 0.5f) dx = 1f - dx;
                    float dy = Mathf.Abs(v - px[i].y); if (dy > 0.5f) dy = 1f - dy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) { f2 = d; }
                }

                // 셀 경계(F2-F1 ≈ 0)가 밝은 선이 된다.
                float edge = 1f - Mathf.Clamp01((f2 - f1) * 9f);
                edge = edge * edge * edge;                       // 선을 가늘고 또렷하게

                // 잔물결 하나를 얹어 무늬가 기계적으로 보이지 않게 한다.
                float ripple = 0.5f + 0.5f * Mathf.Sin((u * 7.3f + v * 5.1f) * Mathf.PI * 2f);

                float lum = 0.55f + 0.45f * Mathf.Clamp01(edge * 0.85f + edge * ripple * 0.35f);
                pixels[y * Size + x] = new Color(lum, lum, lum, 1f);
            }
        }

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TexPath)));
        File.WriteAllBytes(Path.GetFullPath(TexPath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;   // 쿠키가 수조 전체에 반복돼야 한다
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        Debug.Log($"[DomeAquarium] 수중 코스틱 텍스처 생성: {TexPath}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }
}
#endif
