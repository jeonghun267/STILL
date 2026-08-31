#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 발광이 "빛나 보이게" 만드는 후처리 설정.
///
/// 발견한 문제: 프로젝트의 DefaultVolumeProfile 에 Bloom 이 들어 있고 active 도 1 인데
/// intensity 가 **0** 이었다. 그래서 산호에 emission 을 아무리 넣어도 그냥 밝은 색일 뿐
/// 번지는 빛으로 읽히지 않는다.
///
/// HDR 도 같이 켠다. HDR 이 꺼져 있으면 색이 1 에서 잘려서 emission 을 2.2 로 올려도
/// 블룸이 잡을 여유분이 사라진다. Quest 2 에서 대역폭 비용이 있으니,
/// 프레임이 부족하면 URP 에셋에서 HDR 을 끄고 Bloom threshold 를 낮추는 쪽으로 타협하면 된다.
/// </summary>
public static class DomeAquariumPostFx
{
    const string ProfilePath = "Assets/DefaultVolumeProfile.asset";

    [MenuItem("DomeAquarium/14. 블룸/HDR 설정", priority = 14)]
    public static void Configure()
    {
        ConfigureBloom();
        EnableHdr();
        AssetDatabase.SaveAssets();
    }

    static void ConfigureBloom()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogWarning($"[DomeAquarium] 볼륨 프로파일을 찾지 못했다: {ProfilePath}");
            return;
        }

        if (!profile.TryGet(out Bloom bloom))
        {
            bloom = profile.Add<Bloom>(true);
            Debug.Log("[DomeAquarium] 볼륨 프로파일에 Bloom 을 추가했다.");
        }

        bloom.active = true;

        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.9f;          // 0 이었다. 이게 0 이면 발광이 전혀 안 번진다

        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.85f;         // 산호 emission 만 걸리고 물색은 안 걸리는 지점

        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.72f;

        // 모바일 예산 — 고품질 필터링은 끄고 반복 횟수를 줄인다.
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = false;
        bloom.maxIterations.overrideState = true;
        bloom.maxIterations.value = 4;
        // downscale 은 URP 버전마다 열거형 이름이 달라 손대지 않는다. 기본값(Half)이 이미 모바일용이다.

        EditorUtility.SetDirty(profile);
        Debug.Log("[DomeAquarium] Bloom 설정 — intensity 0 → 0.9, threshold 0.85, 반복 4회(모바일).");
    }

    static void EnableHdr()
    {
        // Assets/ 안의 것만 건드린다. 패키지 캐시 안의 에셋을 고치면 재임포트 때 되돌아가고,
        // 애초에 우리 프로젝트 소유가 아니다.
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (urp == null) continue;

            var so = new SerializedObject(urp);
            var hdr = so.FindProperty("m_SupportsHDR");
            if (hdr == null || hdr.boolValue) continue;

            hdr.boolValue = true;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            Debug.Log($"[DomeAquarium] HDR 활성화: {path} (블룸이 잡을 여유분을 만든다)");
        }
    }
}
#endif
