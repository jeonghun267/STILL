#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>
/// Quest 프레임 — "조금 느린 느낌"을 듣고 해변 장면에서 잰 것과 고친 것.
///
///  1. 물 위에 있는 동안 수중 돔을 끈다 (LobbySequence.domeSet). 로비가 돔 24m 위라 보이지도 않는데 물고기 142마리의 애니메이션 ·
///     무리 계산과 산호초 · 수조 렌더러 300개가 해변을 걷는 내내 같이 돌았다. 가장 큰 몫.
///  2. Quest 용 URP 설정(Performance URP Config)의 HDR 을 끈다. 후처리를 쓰지 않아 화면은 같고, 화면 버퍼가 절반이 된다.
///  3. 고정 포비에이티드 렌더링 — OpenXR "Foveated Rendering" 기능 + SRP Foveation(Vulkan), 실행 중 세기 0.5 (QuestFoveation).
///  4. 해변 지형: 층(모래 · 풀 · 절벽)마다 노멀맵과 지형 노멀까지 픽셀마다 텍스처를 8번 읽었다 — 노멀맵을 빼고(4번),
///     120m 밖은 미리 구운 한 장(베이스맵)으로 그린다. 화면을 가득 채우는 땅이라 픽셀 비용이 그대로 프레임이다.
/// 메뉴 한 번으로 지금 프로젝트 · 씬에 적용한다. 로비(9) · 해안(19) 빌더도 같은 값을 쓴다.
/// </summary>
public static class DomeAquariumQuestPerformance
{
    internal const float TerrainBasemapDistance = 120f;
    const float FoveationLevel = 0.5f;
    static readonly string[] DomeRoots = { "FishSwarm", "ReefFloor", "WaterTank", "Seabed" };

    [MenuItem("DomeAquarium/26. Quest 성능 설정 (FPS)", priority = 26)]
    public static void ApplyMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[성능] 플레이 모드에서는 적용할 수 없다."); return; }
        var report = new List<string>();

        var seq = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (seq != null) report.Add($"물 위에서 끄는 돔 물체 {AssignDomeSet(seq)}개");

        foreach (string guid in AssetDatabase.FindAssets("Performance URP Config t:UniversalRenderPipelineAsset"))
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (rp == null || !rp.supportsHDR) continue;
            rp.supportsHDR = false;
            EditorUtility.SetDirty(rp);
            report.Add($"{rp.name} HDR 끔");
        }

        report.Add(EnableFoveation());
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin != null) AddFoveation(origin);

        var terrain = Object.FindFirstObjectByType<Terrain>(FindObjectsInactive.Include);
        if (terrain != null)
        {
            terrain.basemapDistance = TerrainBasemapDistance;
            EditorUtility.SetDirty(terrain);
            if (terrain.materialTemplate != null) LightenTerrainMaterial(terrain.materialTemplate);
            int cleared = 0;
            foreach (var layer in terrain.terrainData.terrainLayers)
            {
                if (layer == null || layer.normalMapTexture == null) continue;
                layer.normalMapTexture = null;
                EditorUtility.SetDirty(layer);
                cleared++;
            }
            report.Add($"지형 층 노멀맵 {cleared}개 뺌, 베이스맵 거리 {TerrainBasemapDistance}m");
        }

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[성능] Quest 성능 설정 — " + string.Join(" · ", report));
    }

    /// <summary>LobbySequence 가 물 위에 있는 동안 끌 수중 돔 물체(씬 맨 위의 이름으로 찾는다).</summary>
    public static int AssignDomeSet(LobbySequence seq)
    {
        var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        seq.domeSet = DomeRoots.Select(n => roots.FirstOrDefault(r => r.name == n)).Where(g => g != null).ToArray();
        EditorUtility.SetDirty(seq);
        return seq.domeSet.Length;
    }

    internal static void AddFoveation(XROrigin origin)
    {
        var fov = origin.GetComponent<QuestFoveation>();
        if (fov == null) fov = Undo.AddComponent<QuestFoveation>(origin.gameObject);
        fov.level = FoveationLevel;
        EditorUtility.SetDirty(fov);
    }

    /// <summary>지형 재질 — 층 노멀맵 · 픽셀마다 지형 노멀을 끈다 (기울기 빛은 정점 노멀로 충분하다).</summary>
    internal static void LightenTerrainMaterial(Material mat)
    {
        mat.DisableKeyword("_NORMALMAP");
        if (mat.HasProperty("_EnableInstancedPerPixelNormal")) mat.SetFloat("_EnableInstancedPerPixelNormal", 0f);
        mat.DisableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        EditorUtility.SetDirty(mat);
    }

    static string EnableFoveation()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null) return "OpenXR Android 설정 없음";
        var feature = settings.GetFeature<FoveatedRenderingFeature>();
        if (feature == null) return "Foveated Rendering 기능 없음";
        settings.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.SRPFoveation;
        feature.enabled = true;
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(feature);
        return $"고정 포비에이티드 렌더링 켬 (SRP, 세기 {FoveationLevel})";
    }
}
#endif
