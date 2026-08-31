#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Layer Lab 물고기 프리팹 153개에는 Animator 가 하나도 붙어 있지 않다.
/// 팩은 체형별 컨트롤러 7개(fish/shark/dolphin/turtle/ray/seahorse/lobster)만 따로 제공한다.
/// 모든 모델이 Generic 리그에 동일한 본 이름(IDLE/SPINE/SPINE1/HEAD/TAIL)을 쓰므로
/// 체형군만 맞춰 붙이면 idle 유영 애니메이션이 그대로 재생된다.
/// </summary>
public static class DomeAquariumAnimatorSetup
{
    const string FishRoot = "Assets/Layer Lab/3D Characters-Fish/Fish";
    const string AniDir = FishRoot + "/Ani";
    const string PrefabDir = FishRoot + "/Prefabs";

    /// <summary>이름 조각 -> 컨트롤러 파일명. 위에서부터 먼저 맞는 것을 쓴다.</summary>
    static readonly (string keyword, string controller)[] GroupMap =
    {
        ("shark",    "shark"),
        ("dolphin",  "dolphin"),
        ("dophin",   "dolphin"),    // 팩에 TucuxiDophin 오타가 있다
        ("turtle",   "turtle"),
        ("seahorse", "seahorse"),
        ("lobster",  "lobster"),
        ("ray",      "ray"),        // MantaRay / SpottedEagleRay
    };

    static readonly string[] PropPrefixes =
    { "coral", "rock", "shell", "seaweed", "starfish", "ground", "land", "sea" };

    static bool IsProp(string n)
    {
        string s = n.ToLowerInvariant();
        foreach (string p in PropPrefixes) if (s.StartsWith(p)) return true;
        return false;
    }

    static string ControllerNameFor(string prefabName)
    {
        string s = prefabName.ToLowerInvariant();
        foreach (var (keyword, controller) in GroupMap)
            if (s.Contains(keyword)) return controller;
        return "fish";              // 일반 어류 전부
    }

    [MenuItem("DomeAquarium/5. 물고기 애니메이터 일괄 부착", priority = 5)]
    public static void AttachAnimators()
    {
        // 컨트롤러를 미리 로드해 둔다.
        var controllers = new Dictionary<string, RuntimeAnimatorController>();
        foreach (var (_, name) in GroupMap)
        {
            if (controllers.ContainsKey(name)) continue;
            var c = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{AniDir}/{name}.controller");
            if (c != null) controllers[name] = c;
        }
        var fishCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{AniDir}/fish.controller");
        if (fishCtrl != null) controllers["fish"] = fishCtrl;

        if (controllers.Count == 0)
        {
            Debug.LogError($"[DomeAquarium] {AniDir} 에서 컨트롤러를 하나도 찾지 못했다.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
        int touched = 0, skipped = 0, noAvatar = 0;
        var missingController = new HashSet<string>();
        var perGroup = new Dictionary<string, int>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path);
            if (IsProp(n)) { skipped++; continue; }

            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            try
            {
                // 리깅된 모델만 대상 — SkinnedMeshRenderer 가 없으면 애니메이션할 것이 없다.
                if (root.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) { skipped++; continue; }

                string ctrlName = ControllerNameFor(n);
                if (!controllers.TryGetValue(ctrlName, out var ctrl))
                {
                    missingController.Add(ctrlName);
                    continue;
                }

                var anim = root.GetComponent<Animator>();
                if (anim == null) anim = root.AddComponent<Animator>();

                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;                       // 이동은 BoidManager 가 한다
                anim.updateMode = AnimatorUpdateMode.Normal;
                // 화면 밖 개체의 애니메이션을 통째로 건너뛴다 — Quest 2 에서 200마리를 돌리려면 필수.
                anim.cullingMode = AnimatorCullingMode.CullCompletely;

                // Generic 리그는 아바타가 있어야 클립이 본에 바인딩된다. 모델 자신의 아바타를 쓴다.
                if (anim.avatar == null)
                {
                    var avatar = FindAvatarFor(n);
                    if (avatar != null) anim.avatar = avatar;
                    else noAvatar++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                touched++;
                perGroup[ctrlName] = perGroup.TryGetValue(ctrlName, out int c0) ? c0 + 1 : 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var summary = new List<string>();
        foreach (var kv in perGroup) summary.Add($"{kv.Key}={kv.Value}");
        Debug.Log($"[DomeAquarium] Animator 부착 완료 — {touched}개 프리팹 ({string.Join(", ", summary)}), 프랍/비리깅 {skipped}개 건너뜀.");
        if (noAvatar > 0)
            Debug.LogWarning($"[DomeAquarium] 아바타를 찾지 못한 프리팹 {noAvatar}개 — Generic 클립이 안 붙을 수 있다. FBX 임포터의 Rig 탭을 확인할 것.");
        if (missingController.Count > 0)
            Debug.LogWarning($"[DomeAquarium] 없는 컨트롤러: {string.Join(", ", missingController)}");
    }

    /// <summary>같은 이름의 FBX 안에 들어 있는 Avatar 서브에셋을 찾는다.</summary>
    static Avatar FindAvatarFor(string prefabName)
    {
        foreach (string ext in new[] { ".fbx", ".FBX" })
        {
            string fbx = $"{FishRoot}/FBX/{prefabName}{ext}";
            var subs = AssetDatabase.LoadAllAssetsAtPath(fbx);
            if (subs == null) continue;
            foreach (var s in subs) if (s is Avatar a) return a;
        }
        return null;
    }

    [MenuItem("DomeAquarium/6. 애니메이터 부착 상태 점검", priority = 6)]
    public static void ReportAnimatorState()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
        int total = 0, withAnim = 0, withCtrl = 0, withAvatar = 0;
        var missing = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path);
            if (IsProp(n)) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            total++;

            var anim = go.GetComponent<Animator>();
            if (anim == null) { missing.Add(n); continue; }
            withAnim++;
            if (anim.runtimeAnimatorController != null) withCtrl++;
            if (anim.avatar != null) withAvatar++;
        }

        Debug.Log($"[DomeAquarium] 애니메이터 점검 — 리깅 프리팹 {total}개 중 Animator {withAnim}, 컨트롤러 {withCtrl}, 아바타 {withAvatar}.");
        if (missing.Count > 0)
            Debug.LogWarning($"[DomeAquarium] Animator 없는 프리팹 {missing.Count}개: {string.Join(", ", missing.GetRange(0, Mathf.Min(20, missing.Count)))}");
    }
}
#endif
