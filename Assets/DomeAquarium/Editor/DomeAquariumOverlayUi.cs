#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 눈앞에 띄우는 글자판을 늘 맨 위에 그린다 — 관측소장 자막(LobbyCaption: 로비 · 엘리베이터 칸 · 수중 돔 모두 이 판 하나),
/// 돔의 "엘리베이터 타기" 판, 테라스 선장 대화 판.
/// 자막은 눈앞 1.5m 에 서는데, 그보다 가까운 캐릭터 · 엘리베이터 벽 · 물고기가 있으면 그 뒤에 묻혀 고개를 돌려야 보였다.
/// 바탕(Image)은 DomeAquarium/UI Overlay, 글자(TMP)는 TextMeshPro … Distance Field Overlay — 둘 다 깊이 검사 없이 맨 마지막에 그린다.
/// 재질은 에셋으로 만들어 씬이 참조하게 한다(빌드에 셰이더가 빠지지 않게).
/// </summary>
public static class DomeAquariumOverlayUi
{
    const string Dir = "Assets/DomeAquarium/Materials";

    [MenuItem("DomeAquarium/23. 자막 · 떠 있는 판을 맨 위에 그리기", priority = 23)]
    public static void ApplyMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[자막] 플레이 모드에서는 바꿀 수 없다."); return; }
        int n = 0;
        var lobby = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (lobby != null && lobby.captionGroup != null) { Apply(lobby.captionGroup.gameObject); n++; }
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director != null && director.callPanel != null) { Apply(director.callPanel.gameObject); n++; }
        foreach (var talk in Object.FindObjectsByType<NpcDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (talk.panel != null) { Apply(talk.panel.gameObject); n++; }
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[자막] 맨 위에 그리기 — 판 {n}개 (관측소장 자막 · 엘리베이터 타기 · 선장 대화).");
    }

    /// <summary>root 아래 글자판 전부를 맨 위에 그린다. 빌더들이 판을 만들 때 부른다.</summary>
    internal static void Apply(GameObject root)
    {
        var ui = UiMaterial();
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMP_Text t)
            {
                var m = TextMaterial(t.fontSharedMaterial);
                if (m != null && t.fontSharedMaterial != m) t.fontSharedMaterial = m;
            }
            else if (g.material != ui) g.material = ui;
            EditorUtility.SetDirty(g);
        }
    }

    static Material UiMaterial()
    {
        string path = $"{Dir}/Mat_UIOverlay.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("DomeAquarium/UI Overlay")) { name = "Mat_UIOverlay" };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material TextMaterial(Material src)
    {
        if (src == null) return null;
        if (src.shader != null && src.shader.name.Contains("Overlay")) return src;
        string path = $"{Dir}/{src.name.Replace(" ", "_")}_Overlay.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        bool mobile = src.shader != null && src.shader.name.Contains("Mobile");
        var shader = Shader.Find(mobile ? "TextMeshPro/Mobile/Distance Field Overlay" : "TextMeshPro/Distance Field Overlay");
        if (shader == null) { Debug.LogWarning("[자막] TMP Overlay 셰이더를 찾지 못했다 — TMP Essential Resources 를 확인할 것."); return null; }
        m = new Material(src) { name = src.name + " Overlay", shader = shader };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
#endif
