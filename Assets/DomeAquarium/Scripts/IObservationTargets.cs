using UnityEngine;

/// <summary>
/// 관찰기(ObservationAim)가 겨눌 수 있는 생물 묶음. 수중 돔의 무리(BoidManager)와 로비 연습 수조(PracticeTank)가 같은 방식으로 답한다.
/// 개체에는 콜라이더가 없어서 레이캐스트가 아니라 "조준선에서 몇 도 떨어져 있나"로 판정한다.
/// </summary>
public interface IObservationTargets
{
    int AgentCount { get; }
    bool TryGetAgent(int index, out Vector3 position, out FishData fish);
    /// <summary>원뿔 안에서 조준선과 각도가 가장 작은 개체. 없으면 -1.</summary>
    int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees);
    /// <summary>이미 잡은 개체가 여전히 원뿔 안에 있는가 — 무리 속에서 대상이 이웃으로 깜빡이지 않게 쓴다.</summary>
    bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees);
}

/// <summary>원뿔 판정 공통식. 큰 생물은 몸 중심이 조준선에서 벗어나도 몸 어딘가를 겨누면 잡히게, 체장 절반이 차지하는 각도만큼 넓힌다.</summary>
public static class ObservationCone
{
    public static bool Contains(Vector3 origin, Vector3 dirNormalized, Vector3 position, FishData fish,
                                float maxDistance, float coneDegrees, out float angle)
    {
        angle = 0f;
        Vector3 to = position - origin;
        float dist = to.magnitude;
        if (dist < 1e-4f || dist > maxDistance) return false;

        float halfBody = fish != null ? Mathf.Max(0f, fish.bodyLength) * 0.5f : 0f;
        float allowance = coneDegrees + Mathf.Atan2(halfBody, dist) * Mathf.Rad2Deg;

        float cos = Vector3.Dot(to / dist, dirNormalized);
        angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
        return angle <= allowance;
    }
}

/// <summary>
/// 두 묶음을 하나처럼 겨눈다 — 늘 헤엄치는 무리(BoidManager) + 가끔 지나가는 큰 생물(LargeCreaturePasses).
/// 개체 번호는 앞 묶음 뒤에 뒷 묶음을 이어 붙인다. 두 묶음 모두 개체 수가 층이 바뀔 때만 달라지므로 번호가 흔들리지 않는다.
/// </summary>
public sealed class CompositeTargets : IObservationTargets
{
    readonly IObservationTargets a, b;

    public CompositeTargets(IObservationTargets first, IObservationTargets second)
    {
        a = first;
        b = second;
    }

    int CountA => a != null ? a.AgentCount : 0;
    public int AgentCount => CountA + (b != null ? b.AgentCount : 0);

    public bool TryGetAgent(int index, out Vector3 position, out FishData fish)
    {
        position = Vector3.zero;
        fish = null;
        int na = CountA;
        if (index < na) return index >= 0 && a.TryGetAgent(index, out position, out fish);
        return b != null && b.TryGetAgent(index - na, out position, out fish);
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        int na = CountA;
        int ia = a != null ? a.FindAgentInCone(origin, direction, maxDistance, coneDegrees) : -1;
        int ib = b != null ? b.FindAgentInCone(origin, direction, maxDistance, coneDegrees) : -1;
        if (ib < 0) return ia;
        if (ia < 0) return na + ib;
        // 둘 다 잡히면 조준선에 더 가까운 쪽
        return Angle(ia, origin, direction) <= Angle(na + ib, origin, direction) ? ia : na + ib;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        int na = CountA;
        if (index < na) return index >= 0 && a.IsAgentInCone(index, origin, direction, maxDistance, coneDegrees);
        return b != null && b.IsAgentInCone(index - na, origin, direction, maxDistance, coneDegrees);
    }

    float Angle(int index, Vector3 origin, Vector3 direction)
    {
        if (!TryGetAgent(index, out Vector3 pos, out _)) return float.MaxValue;
        return Vector3.Angle(direction, pos - origin);
    }
}
