using UnityEngine;

/// <summary>
/// 24m 산호초 한쪽 물속에 떠다니는 버려진 그물(유령 그물)에 걸린 바다거북. 빠져나오려고 버둥댄다.
/// 관찰기로 지켜보면 기록 카드와 함께 "구조 요청을 보냈어요" (DiveDirector — 할 일의 종 수에도 든다).
/// 놀란이 해변에서 꺼낸 유령 그물 이야기가 여기서 눈앞의 일이 되고, 엔딩의 밤에 "무사히 풀려났대!"로 닫힌다.
/// 관찰기가 겨눌 수 있게 개체 하나짜리 IObservationTargets 로 답한다 (돔 무리와 CompositeTargets 로 묶는다).
/// </summary>
public sealed class EntangledTurtle : MonoBehaviour, IObservationTargets
{
    public FishData fish;                      // "그물에 걸린 푸른바다거북" — Missions 폴더의 기록 카드용. speciesId 가 푸른바다거북이라 일지에는 그 종으로 남는다
    public FishData species;                   // 푸른바다거북 — 층 할 일은 이 종으로 센다 (돔을 헤엄치는 거북과 두 번 세지 않게)
    [TextArea(2, 4)] public string rescueLine =
        "관측소에서 해양 구조대에 연락했어요. 위치를 보냈으니 곧 그물을 풀어 줄 거예요. " +
        "놀란이 말한 '유령 그물'이 바로 이거예요. 버려진 그물은 바닷속을 떠다니며 이렇게 생물을 붙잡아요.";
    public float hintAfter = 40f;              // 24m 에 내려와 이만큼 지나도 기록하지 않으면 관측소장이 방향을 알려 준다
    [TextArea(2, 4)] public string hintLine =
        "{0} 보세요. 바다거북 한 마리가 그물에 걸려 버둥대고 있어요. 관찰기로 기록하면 관측소가 바로 구조 요청을 보낼게요.";
    public Transform body;                     // 거북 모델 (버둥대는 자세를 여기에)
    public Transform net;                      // 그물 — 거북과 같이 흔들린다
    public float struggle = 14f;               // 버둥대는 각도(°)
    public float drift = 0.25f;                // 물살에 떠밀리는 폭(m)

    Vector3 home;
    Quaternion bodyRest, netRest;
    float seed;

    public bool Rescued { get; private set; }

    void Awake()
    {
        home = transform.localPosition;
        if (body != null) bodyRest = body.localRotation;
        if (net != null) netRest = net.localRotation;
        seed = Random.value * 10f;
    }

    /// <summary>구조 요청을 보낸 뒤 — 버둥대기를 줄인다(구조대가 오는 동안 힘을 아낀다).</summary>
    public void MarkRescued() => Rescued = true;

    void Update()
    {
        float t = Time.time + seed;
        float k = Rescued ? 0.35f : 1f;
        // 그물째 물살에 천천히 떠밀린다
        transform.localPosition = home + new Vector3(Mathf.Sin(t * 0.21f), Mathf.Sin(t * 0.17f) * 0.5f, Mathf.Cos(t * 0.19f)) * drift;
        // 몸을 비틀며 빠져나오려 한다 — 불규칙하게 세졌다 약해진다
        float effort = Mathf.Lerp(0.3f, 1f, Mathf.PerlinNoise(t * 0.6f, 0.4f)) * k;
        if (body != null)
            body.localRotation = bodyRest * Quaternion.Euler(Mathf.Sin(t * 3.1f) * struggle * 0.4f * effort,
                                                            Mathf.Sin(t * 2.3f) * struggle * effort,
                                                            Mathf.Sin(t * 2.7f) * struggle * 0.7f * effort);
        if (net != null)
            net.localRotation = netRest * Quaternion.Euler(Mathf.Sin(t * 1.3f) * 4f * effort, Mathf.Sin(t * 0.9f) * 6f, Mathf.Sin(t * 1.1f) * 3f * effort);
    }

    // ── 관찰기가 겨누는 개체 하나 ──
    public int AgentCount => isActiveAndEnabled && fish != null ? 1 : 0;

    public bool TryGetAgent(int index, out Vector3 position, out FishData f)
    {
        position = body != null ? body.position : transform.position;
        f = fish;
        return index == 0 && AgentCount == 1;
    }

    public int FindAgentInCone(Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees)
    {
        if (AgentCount == 0) return -1;
        TryGetAgent(0, out Vector3 p, out FishData f);
        return ObservationCone.Contains(origin, direction.normalized, p, f, maxDistance, coneDegrees, out _) ? 0 : -1;
    }

    public bool IsAgentInCone(int index, Vector3 origin, Vector3 direction, float maxDistance, float coneDegrees) =>
        index == 0 && FindAgentInCone(origin, direction, maxDistance, coneDegrees) == 0;
}
