#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Layer Lab FBX 는 avatarSetup: 0 (No Avatar) 이라 Avatar 서브에셋이 아예 없다.
/// Generic 애니메이션은 아바타 없이도 **커브의 트랜스폼 경로가 계층과 일치하면** 재생된다.
/// 플레이 모드에 들어가지 않고 그 일치 여부를 정적으로 확인한다.
/// </summary>
public static class DomeAquariumAnimatorVerify
{
    const string FishRoot = "Assets/Layer Lab/3D Characters-Fish/Fish";
    const string PrefabDir = FishRoot + "/Prefabs";

    [MenuItem("DomeAquarium/7. 애니메이션 본 경로 검증", priority = 7)]
    public static void Verify()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });

        int checkedCount = 0, perfect = 0, partial = 0, broken = 0, noClip = 0;
        var worst = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            var anim = go.GetComponent<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) continue;

            var clips = anim.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0) { noClip++; continue; }

            checkedCount++;
            int totalPaths = 0, resolved = 0;

            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var seen = new HashSet<string>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!seen.Add(b.path)) continue;      // 같은 본의 여러 커브는 한 번만
                    totalPaths++;
                    // 빈 경로는 루트 자신 — 항상 존재한다.
                    if (string.IsNullOrEmpty(b.path) || go.transform.Find(b.path) != null) resolved++;
                }
            }

            if (totalPaths == 0) { noClip++; continue; }

            float ratio = (float)resolved / totalPaths;
            if (ratio >= 0.999f) perfect++;
            else if (ratio >= 0.5f) { partial++; worst.Add($"{Path.GetFileNameWithoutExtension(path)} {resolved}/{totalPaths}"); }
            else { broken++; worst.Add($"{Path.GetFileNameWithoutExtension(path)} {resolved}/{totalPaths}"); }
        }

        Debug.Log($"[DomeAquarium] 본 경로 검증 — 대상 {checkedCount}개: 완전일치 {perfect}, 부분일치 {partial}, 불일치 {broken}, 클립없음 {noClip}");

        if (worst.Count > 0)
            Debug.LogWarning($"[DomeAquarium] 경로가 안 맞는 프리팹: {string.Join(" | ", worst.GetRange(0, Mathf.Min(25, worst.Count)))}");
        else if (perfect > 0)
            Debug.Log("[DomeAquarium] 모든 프리팹에서 애니메이션 커브가 본에 100% 바인딩된다. 아바타 없이도 재생된다.");
    }
}
#endif
