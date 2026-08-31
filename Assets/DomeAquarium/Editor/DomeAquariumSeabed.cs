#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 모래 바닥을 만든다.
///
/// 프로젝트에도 에셋스토어 다운로드 캐시에도 흙/모래 텍스처가 없어서 절차적으로 굽는다.
/// 코스틱과 같은 방식이다 — 외부 에셋에 의존하지 않고, 타일링이 이음매 없이 맞는다.
///
/// 만드는 것:
///  - 알베도: 모래색 + 잔입자 + 물결 무늬(수중 모래 특유의 리플)
///  - 노멀맵: 위 물결의 높이차에서 유도. 코스틱 빛이 물결을 타고 흐르는 느낌을 만든다
///  - Mat_Seabed (URP/Lit) 와 바닥 메시
/// </summary>
public static class DomeAquariumSeabed
{
    const string AlbedoPath = "Assets/DomeAquarium/Materials/Sand_Albedo.png";
    const string NormalPath = "Assets/DomeAquarium/Materials/Sand_Normal.png";
    const string MatPath = "Assets/DomeAquarium/Materials/Mat_Seabed.mat";
    const int Size = 512;

    // 수조 규격과 맞춰야 한다.
    const float TankRadius = 12f;
    const float FloorY = -8f;

    /// <summary>타일링되는 값잡음. 격자점 해시를 스무스스텝으로 보간한다.</summary>
    static float Noise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);

        float H(int a, int b)
        {
            a = ((a % period) + period) % period;      // 주기로 감싸 이음매를 없앤다
            b = ((b % period) + period) % period;
            int h = a * 374761393 + b * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }

        return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), u),
                          Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), u), v);
    }

    /// <summary>0~1 높이. 큰 물결 + 중간 굴곡 + 잔입자.</summary>
    static float Height(float u, float v)
    {
        // 물속 모래 리플 — 한 방향으로 길게 늘어난 물결
        float ripple = Mathf.Sin((u * 18f + Noise(u * 4f, v * 4f, 4, 11) * 2.2f) * Mathf.PI * 2f) * 0.5f + 0.5f;

        float mid = Noise(u * 8f, v * 8f, 8, 3);
        float fine = Noise(u * 32f, v * 32f, 32, 7);

        return Mathf.Clamp01(ripple * 0.45f + mid * 0.40f + fine * 0.15f);
    }

    [MenuItem("DomeAquarium/13. 모래 바닥 텍스처 생성", priority = 13)]
    public static Material EnsureSandMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat != null) return mat;

        // ── 알베도 ──
        var albedo = new Color[Size * Size];
        var heights = new float[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                float h = Height(u, v);
                heights[y * Size + x] = h;

                // 모래색. 밝은 베이지 ~ 살짝 어두운 회갈색.
                Color sand = Color.Lerp(new Color(0.62f, 0.57f, 0.46f),
                                        new Color(0.86f, 0.82f, 0.71f), h);
                // 잔입자로 자글자글한 느낌
                float grain = Noise(u * 128f, v * 128f, 128, 23);
                sand *= 0.94f + grain * 0.12f;

                albedo[y * Size + x] = sand;
            }
        }
        WritePng(AlbedoPath, albedo, false);

        // ── 노멀맵 (높이차에서 유도) ──
        var normal = new Color[Size * Size];
        const float strength = 3.2f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int xm = (x - 1 + Size) % Size, xp = (x + 1) % Size;
                int ym = (y - 1 + Size) % Size, yp = (y + 1) % Size;

                float dx = (heights[y * Size + xp] - heights[y * Size + xm]) * strength;
                float dy = (heights[yp * Size + x] - heights[ym * Size + x]) * strength;

                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                normal[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }
        WritePng(NormalPath, normal, true);

        var sh = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(sh) { name = "Mat_Seabed" };
        var albTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var nrmTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);

        if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", albTex); mat.SetTextureScale("_BaseMap", new Vector2(6f, 6f)); }
        if (mat.HasProperty("_BumpMap") && nrmTex != null)
        {
            mat.SetTexture("_BumpMap", nrmTex);
            mat.SetTextureScale("_BumpMap", new Vector2(6f, 6f));
            mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1.1f);
        }
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);   // 젖은 모래도 거의 무광
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;

        AssetDatabase.CreateAsset(mat, MatPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DomeAquarium] 모래 바닥 머티리얼 생성: {MatPath}");
        return mat;
    }

    static void WritePng(string path, Color[] px, bool isNormal)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
    }

    /// <summary>수조 바닥 판을 세운다. 지금까지는 바닥이 아예 없어서 원통 아랫면이 그대로 보였다.</summary>
    public static GameObject BuildFloor()
    {
        var mat = EnsureSandMaterial();

        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Seabed";
        go.transform.position = new Vector3(0f, FloorY, 0f);
        // Unity Plane 은 10x10 이다. 원통 지름 24m 를 덮으려면 2.6 이면 충분하다.
        go.transform.localScale = Vector3.one * 2.6f;

        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);   // 물고기·레이가 걸리면 안 된다

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Debug.Log("[DomeAquarium] 모래 바닥 배치 완료.");
        return go;
    }
}
#endif
