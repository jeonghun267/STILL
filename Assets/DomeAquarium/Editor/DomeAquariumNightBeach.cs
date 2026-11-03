#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 엔딩의 밤 해변 — 파도가 야광 플랑크톤처럼 푸르게 빛난다 (ShoreWaves.nightGlow).
/// 거품 띠와 같은 메시 · 같은 거품 무늬를 가산 재질(푸른빛)로 한 번 더 그린다. 낮에는 꺼져 있다.
/// 해안(19)을 다시 세울 때도 붙고, 해안을 그대로 둔 채 이것만 붙이려면 28.
/// </summary>
public static class DomeAquariumNightBeach
{
    const string MatPath = "Assets/DomeAquarium/Materials/Mat_CoastWaveGlow.mat";

    [MenuItem("DomeAquarium/28. 밤 해변 (푸르게 빛나는 파도)", priority = 28)]
    public static void ApplyMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[NightBeach] 플레이 모드에서는 적용할 수 없다."); return; }
        var waves = Object.FindFirstObjectByType<ShoreWaves>(FindObjectsInactive.Include);
        if (waves == null) { Debug.LogError("[NightBeach] ShoreWaves 가 없다. 먼저 19번 메뉴."); return; }
        AddGlow(waves);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void AddGlow(ShoreWaves waves)
    {
        var old = waves.transform.Find("NightGlow");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var foam = waves.GetComponent<MeshRenderer>().sharedMaterial;
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_CoastWaveGlow", "Universal Render Pipeline/Particles/Unlit", additive: true);
        if (foam != null && foam.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", foam.GetTexture("_BaseMap"));
        mat.SetColor("_BaseColor", new Color(0.22f, 0.70f, 1f, 1f));       // 야광 플랑크톤의 청록빛
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);

        var go = new GameObject("NightGlow");
        go.transform.SetParent(waves.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.01f, 0f);           // 거품 띠 바로 위
        go.AddComponent<MeshFilter>();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;

        waves.nightGlow = r;
        waves.lobby = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        EditorUtility.SetDirty(waves);
        Debug.Log("[NightBeach] 밤 파도가 푸르게 빛난다 (NightGlow).");
    }
}
#endif
