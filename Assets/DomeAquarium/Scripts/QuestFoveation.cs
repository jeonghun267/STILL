using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Quest 고정 포비에이티드 렌더링 — 렌즈 가장자리를 낮은 해상도로 그려 GPU 부담을 던다.
/// OpenXR "Foveated Rendering" 기능 + SRP Foveation 방식(Vulkan)이 켜져 있어야 한다 (DomeAquarium/26. Quest 성능 설정).
/// 렌즈 가장자리는 원래 흐려 차이가 거의 보이지 않는다. 시선 20° 아래 자막과 손 위 관찰기 화면이 흐려지지 않게 중간 세기로 둔다.
/// 표시 장치가 늦게 뜰 수 있어 몇 초 동안 다시 시도한다. 에디터에서 헤드셋 없이 돌리면 표시 장치가 없어 아무것도 하지 않는다.
/// </summary>
public sealed class QuestFoveation : MonoBehaviour
{
    [Range(0f, 1f)] public float level = 0.5f;
    public float retrySeconds = 10f;

    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    IEnumerator Start()
    {
        for (float t = 0f; t < retrySeconds; t += 0.5f)
        {
            if (Apply()) yield break;
            yield return new WaitForSeconds(0.5f);
        }
    }

    bool Apply()
    {
        SubsystemManager.GetSubsystems(displays);
        bool applied = false;
        foreach (var d in displays)
        {
            if (d == null || !d.running) continue;
            d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;   // 고정 — 시선 추적 없이
            d.foveatedRenderingLevel = level;
            applied = true;
        }
        return applied;
    }
}
