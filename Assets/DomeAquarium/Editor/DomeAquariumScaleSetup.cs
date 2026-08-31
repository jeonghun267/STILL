#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 모든 FishData 의 swimScale/revealScale 이 0.25 / 0.4 로 똑같아서
/// 백상아리와 고비가 같은 크기로 보이는 문제를 고친다.
///
/// 방식: 프리팹의 실제 메시 길이를 측정한 뒤, 어종별 목표 길이(m)에 맞는 배율을 역산한다.
/// 팩이 모델을 어떤 크기로 만들었든 결과 크기가 일정해진다.
///
/// 목표 길이는 실제 성체 크기를 기준으로 하되, 반지름 12m 수조 안에서 다루기 쉽도록
/// 큰 쪽을 조금 눌렀다 (백상아리 실물 4.5m -> 2.8m). 순서와 비율감은 유지된다.
/// </summary>
public static class DomeAquariumScaleSetup
{
    const string FishRoot = "Assets/Layer Lab/3D Characters-Fish/Fish";
    const string DataDir = "Assets/DomeAquarium/FishData";

    // 눈앞 연출에서의 길이 한계. 만타가 실물 3m 로 얼굴 1.2m 앞에 오면 아무것도 안 보인다.
    const float RevealMin = 0.22f;
    const float RevealMax = 0.85f;

    struct SizeRule { public string keyword; public float meters; }

    // 위에서부터 먼저 맞는 규칙을 쓴다. 구체적인 것을 앞에 둔다.
    static readonly SizeRule[] Rules =
    {
        // 대형 ─────────────────────────────
        new SizeRule { keyword = "greatewhiteshark",  meters = 2.8f },
        new SizeRule { keyword = "commonthresher",    meters = 2.4f },
        new SizeRule { keyword = "mantaray",          meters = 2.6f },
        new SizeRule { keyword = "nurseshark",        meters = 2.0f },
        new SizeRule { keyword = "blacktipreefshark", meters = 1.5f },
        new SizeRule { keyword = "angelshark",        meters = 1.2f },
        new SizeRule { keyword = "shark",             meters = 1.8f },
        new SizeRule { keyword = "dolphin",           meters = 1.8f },
        new SizeRule { keyword = "dophin",            meters = 1.8f },   // 팩 오타 (TucuxiDophin)
        new SizeRule { keyword = "spottedeagleray",   meters = 1.6f },
        new SizeRule { keyword = "ray",               meters = 1.4f },
        new SizeRule { keyword = "leatherbackturtle", meters = 1.5f },
        new SizeRule { keyword = "turtle",            meters = 1.0f },
        new SizeRule { keyword = "bluefintuna",       meters = 1.8f },
        new SizeRule { keyword = "yellowfintuna",     meters = 1.4f },
        new SizeRule { keyword = "tuna",              meters = 1.0f },

        // 중형 ─────────────────────────────
        new SizeRule { keyword = "grouper",           meters = 0.70f },
        new SizeRule { keyword = "tilefish",          meters = 0.35f },
        new SizeRule { keyword = "unicorntang",       meters = 0.45f },
        new SizeRule { keyword = "sailfintang",       meters = 0.35f },
        new SizeRule { keyword = "angelfish",         meters = 0.30f },
        new SizeRule { keyword = "tang",              meters = 0.26f },
        new SizeRule { keyword = "moorishidol",       meters = 0.22f },
        new SizeRule { keyword = "butterfly",         meters = 0.18f },
        new SizeRule { keyword = "lobster",           meters = 0.35f },

        // 소형 ─────────────────────────────
        new SizeRule { keyword = "clownfish",         meters = 0.11f },
        new SizeRule { keyword = "damsel",            meters = 0.09f },
        new SizeRule { keyword = "chromis",           meters = 0.09f },
        new SizeRule { keyword = "cardinal",          meters = 0.08f },
        new SizeRule { keyword = "basslet",           meters = 0.06f },
        new SizeRule { keyword = "dottyback",         meters = 0.08f },
        new SizeRule { keyword = "dartfish",          meters = 0.10f },
        new SizeRule { keyword = "goby",              meters = 0.05f },
        new SizeRule { keyword = "seahorse",          meters = 0.09f },
    };

    const float DefaultMeters = 0.20f;   // 규칙에 안 걸리는 일반 어류

    static float TargetLength(string name, out bool matched)
    {
        string s = name.ToLowerInvariant();
        foreach (var r in Rules)
        {
            if (s.Contains(r.keyword)) { matched = true; return r.meters; }
        }
        matched = false;
        return DefaultMeters;
    }

    /// <summary>프리팹의 렌더러 전체를 감싸는 박스의 가장 긴 축 길이(로컬 스케일 반영).</summary>
    static float MeasureLength(GameObject prefabRoot)
    {
        var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return 0f;

        bool has = false;
        Bounds total = default;

        foreach (var r in renderers)
        {
            Bounds b;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                // SkinnedMeshRenderer.bounds 는 프리팹 에셋에서 신뢰하기 어렵다.
                // 메시 자신의 바운즈를 루트 기준으로 옮겨서 쓴다.
                var m = prefabRoot.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var mb = smr.sharedMesh.bounds;
                b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
                b.Encapsulate(m.MultiplyPoint3x4(mb.center + mb.extents));
                b.Encapsulate(m.MultiplyPoint3x4(mb.center - mb.extents));
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = prefabRoot.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
                b.Encapsulate(m.MultiplyPoint3x4(mb.center + mb.extents));
                b.Encapsulate(m.MultiplyPoint3x4(mb.center - mb.extents));
            }

            if (!has) { total = b; has = true; }
            else total.Encapsulate(b);
        }

        if (!has) return 0f;
        Vector3 size = total.size;
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
    }

    [MenuItem("DomeAquarium/10. 어종별 크기 보정", priority = 10)]
    public static void ApplyScales()
    {
        string[] guids = AssetDatabase.FindAssets("t:FishData", new[] { DataDir });
        int done = 0, noMesh = 0;
        var unmatched = new List<string>();
        var report = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<FishData>(path);
            if (data == null || data.prefab == null) continue;

            string n = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(data.prefab));

            var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(data.prefab));
            float native;
            try { native = MeasureLength(contents); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (native <= 0.0001f) { noMesh++; continue; }

            float target = TargetLength(n, out bool matched);
            if (!matched) unmatched.Add(n);

            data.swimScale = target / native;

            // 체장을 기록해 두면 BoidManager 가 개체별 분리 반경을 뽑아 쓴다.
            data.bodyLength = target;
            // 실제 어류 순항속도는 대략 2 체장/초. 전 종이 0.9 로 같으면
            // 고비는 18 체장/초로 발작하듯 움직이고 참다랑어는 0.5 체장/초로 미끄러진다.
            data.swimSpeed = Mathf.Clamp(target * 2.0f, 0.35f, 2.2f);

            // 연출용은 얼굴 앞이라 별도 상한을 둔다.
            float revealLen = Mathf.Clamp(target, RevealMin, RevealMax);
            data.revealScale = revealLen / native;

            EditorUtility.SetDirty(data);
            done++;

            if (report.Count < 12)
                report.Add($"{n}: 모델 {native:0.00} -> 유영 {target:0.00}m (x{data.swimScale:0.000}), 연출 {revealLen:0.00}m");
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[DomeAquarium] 크기 보정 {done}종 완료. 예시:\n  " + string.Join("\n  ", report));
        if (noMesh > 0) Debug.LogWarning($"[DomeAquarium] 메시를 측정하지 못한 종 {noMesh}개 — 크기 그대로 둠.");
        if (unmatched.Count > 0)
            Debug.LogWarning($"[DomeAquarium] 크기 규칙에 안 걸려 기본 {DefaultMeters}m 를 쓴 종 {unmatched.Count}개: " +
                             string.Join(", ", unmatched.GetRange(0, Mathf.Min(25, unmatched.Count))));
    }
}
#endif
