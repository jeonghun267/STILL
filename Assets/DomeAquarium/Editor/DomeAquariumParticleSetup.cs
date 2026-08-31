#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 파티클이 흰 사각형으로 보이는 문제를 고친다.
///
/// 원인: URP 파티클 머티리얼에 텍스처가 없으면 파티클 쿼드가 그대로 흰 네모로 그려진다.
/// 빌트인 파티클 셰이더를 URP 로 옮길 때 _MainTex 가 비어 있던 것들과,
/// 내가 코드로 만든 BurstFX 머티리얼이 여기 해당한다.
///
/// 해결: 가운데가 밝고 가장자리로 갈수록 투명해지는 원형 텍스처를 만들어 물린다.
/// 물속 반짝임/거품에 쓰기 딱 좋은 모양이고, 텍스처 한 장을 전부 공유하므로 비용도 없다.
/// </summary>
public static class DomeAquariumParticleSetup
{
    const string TexPath = "Assets/DomeAquarium/Materials/SoftDot.png";
    const int Size = 64;

    static readonly string[] MaterialRoots =
    {
        "Assets/DomeAquarium/Materials",
        "Assets/RPG Pack/Materials",
    };

    [MenuItem("DomeAquarium/11. 파티클 텍스처 생성 및 배선", priority = 11)]
    public static void Run()
    {
        var tex = EnsureSoftDotTexture();
        if (tex == null) return;

        int fixedCount = 0;
        var roots = new List<string>();
        foreach (string r in MaterialRoots) if (AssetDatabase.IsValidFolder(r)) roots.Add(r);

        foreach (string guid in AssetDatabase.FindAssets("t:Material", roots.ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (!mat.shader.name.Contains("Particles")) continue;

            bool empty = (!mat.HasProperty("_BaseMap") || mat.GetTexture("_BaseMap") == null)
                      && (!mat.HasProperty("_MainTex") || mat.GetTexture("_MainTex") == null);
            if (!empty) continue;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            // 물속 반짝임이므로 애디티브가 어울린다. Surface=Transparent, Blend=Additive
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
            mat.renderQueue = 3000;

            EditorUtility.SetDirty(mat);
            fixedCount++;
            Debug.Log($"[DomeAquarium] 파티클 텍스처 배선: {path}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DomeAquarium] 파티클 머티리얼 {fixedCount}개에 소프트닷 텍스처를 물렸다.");
    }

    /// <summary>가운데가 밝고 가장자리로 투명해지는 원형 텍스처. 없으면 만든다.</summary>
    public static Texture2D EnsureSoftDotTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (existing != null) return existing;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        float c = (Size - 1) * 0.5f;
        var px = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // 1 -> 0 으로 부드럽게. 제곱해서 중심을 더 또렷하게 남긴다.
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                // ★ RGB 에도 같은 감쇠를 넣는다.
                // 애디티브 블렌딩은 알파를 곱하지 않고 RGB 를 그대로 더한다.
                // RGB 를 흰색으로 두면 알파가 0 인 모서리까지 흰색이 더해져 정사각형으로 보인다.
                px[y * Size + x] = new Color(a, a, a, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        File.WriteAllBytes(Path.GetFullPath(TexPath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Debug.Log($"[DomeAquarium] 소프트닷 텍스처 생성: {TexPath}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }
}
#endif
