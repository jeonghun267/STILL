#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로비를 배치하기 전에 외부 에셋의 실제 크기(렌더러 바운드)를 콘솔에 찍는다.
/// 프리팹 피벗과 단위가 제각각이라 눈대중으로 놓으면 벽 사이가 뜨거나 겹친다.
/// </summary>
public static class DomeAquariumLobbyProbe
{
    static readonly string[] Folders =
    {
        "Assets/Barking_Dog/3D Free Modular Kit/Prefabs",
        "Assets/ThirdParty/Sketchfab",
    };

    static readonly string[] KenneyPicks =
    {
        "bench", "benchCushion", "loungeDesignSofa", "loungeSofaLong", "pottedPlant", "plantSmall1",
        "lampRoundFloor", "rugRectangle", "rugRound", "tableCoffeeGlass", "sideTable", "trashcan",
    };

    [MenuItem("DomeAquarium/디버그/로비 에셋 크기 출력", priority = 200)]
    public static void Dump()
    {
        var sb = new StringBuilder("[LobbyProbe]\n");
        foreach (string folder in Folders)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
                Append(sb, AssetDatabase.GUIDToAssetPath(guid));
        }
        foreach (string name in KenneyPicks)
            Append(sb, $"Assets/ThirdParty/Kenney_FurnitureKit/Models/{name}.fbx");
        Debug.Log(sb.ToString());
    }

    static void Append(StringBuilder sb, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { sb.AppendLine($"  (없음) {path}"); return; }

        var go = Object.Instantiate(prefab);
        try
        {
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { sb.AppendLine($"  {System.IO.Path.GetFileName(path)}: 렌더러 없음"); return; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var anim = go.GetComponentInChildren<Animation>(true);
            var animator = go.GetComponentInChildren<Animator>(true);
            sb.AppendLine($"  {System.IO.Path.GetFileName(path)}: min={b.min:F2} max={b.max:F2} size={b.size:F2}" +
                          (anim != null ? $" Animation({anim.GetClipCount()})" : "") +
                          (animator != null ? " Animator" : ""));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
#endif
