#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    const string BubbleMaterialPath = "Assets/DomeAquarium/Materials/Mat_BurstFX.mat";
    const string BubbleShaderName = "DomeAquarium/Bubble Particle";
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

        int bubbleCount = 0;
        var touchedScenes = new HashSet<Scene>();
        foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
        {
            if (ps == null || ps.gameObject.name != "BurstFX") continue;
            if (EditorUtility.IsPersistent(ps) || !ps.gameObject.scene.IsValid()) continue;

            ConfigureBubbleBurst(ps);
            EditorUtility.SetDirty(ps);
            EditorUtility.SetDirty(ps.GetComponent<ParticleSystemRenderer>());
            touchedScenes.Add(ps.gameObject.scene);
            bubbleCount++;
        }

        foreach (Scene scene in touchedScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DomeAquarium] 파티클 머티리얼 {fixedCount}개 배선, 상자 거품 효과 {bubbleCount}개 갱신.");
    }

    /// <summary>상자 등장/뽑기 효과를 위로 떠오르는 반투명 거품으로 구성한다.</summary>
    public static void ConfigureBubbleBurst(ParticleSystem ps)
    {
        if (ps == null) return;

        var main = ps.main;
        main.duration = 1.8f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.15f, 1.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.085f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.52f, 0.86f, 1f, 0.42f),
            new Color(0.86f, 0.97f, 1f, 0.72f));
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;
        shape.radiusThickness = 1f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.52f, 0.95f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.28f, 0.9f),
            new Keyframe(1f, 1.28f)));

        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.65f, 0.9f, 1f), 0f),
                new GradientColorKey(new Color(0.88f, 0.97f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.8f, 0.12f),
                new GradientAlphaKey(0.62f, 0.72f),
                new GradientAlphaKey(0f, 1f)
            });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        var noise = ps.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
        noise.frequency = 0.7f;
        noise.scrollSpeed = 0.2f;
        noise.damping = true;
        noise.octaveCount = 1;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.enableGPUInstancing = true;
        renderer.sharedMaterial = EnsureBubbleMaterial();
    }

    static Material EnsureBubbleMaterial()
    {
        var shader = Shader.Find(BubbleShaderName);
        if (shader == null)
        {
            Debug.LogError($"[DomeAquarium] 거품 셰이더를 찾지 못했다: {BubbleShaderName}");
            return AssetDatabase.LoadAssetAtPath<Material>(BubbleMaterialPath);
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(BubbleMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Mat_BurstFX" };
            AssetDatabase.CreateAsset(material, BubbleMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetColor("_BaseColor", new Color(0.65f, 0.92f, 1f, 0.72f));
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
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
