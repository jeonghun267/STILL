using UnityEngine;

/// <summary>
/// 오른쪽 곶 끝의 등대. 낮에는 등명기 유리가 은은하게 비치고, 로비에 머물러 해가 저물수록(LobbyDusk) 등명기가 밝아지다가
/// 거의 다 저물면 두 갈래 빛줄기가 돌기 시작한다. 엔딩의 밤 로비에서는 다 켜진다.
/// 실시간 조명을 쓰지 않는다 — Quest 에서 추가 조명은 비싸다. 빛줄기는 끝으로 갈수록 투명해지는 원뿔 메시(가산 혼합)다.
/// </summary>
public sealed class Lighthouse : MonoBehaviour
{
    public LobbySequence lobby;
    public Transform beamPivot;                         // 빛줄기가 붙은 축 (등명기 높이)
    public GameObject beams;                            // 빛줄기 묶음 — 밤(또는 거의 저문 해 질 녘)에만 켠다
    public Renderer lantern;                            // 등명기 유리 (발광 재질)
    [ColorUsage(false, true)] public Color dayGlow = new Color(0.35f, 0.30f, 0.22f);
    [ColorUsage(false, true)] public Color nightGlow = new Color(3.2f, 2.8f, 2.0f);
    public float degreesPerSecond = 30f;                // 12초에 한 바퀴 — 빛줄기가 두 갈래라 6초마다 한 번 이쪽을 비춘다
    [Range(0f, 1f)] public float beamsFromDusk = 0.85f; // 해 질 녘이 이만큼 깊어지면 빛줄기를 켠다

    /// <summary>해 질 녘이 얼마나 깊었는지 0~1 — LobbyDusk 가 채운다.</summary>
    [System.NonSerialized] public float dusk;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;
    float shown = -1f;                                  // 지금 칠해 둔 밝기 (0 낮 ~ 1 밤)

    void Update()
    {
        bool night = lobby != null && lobby.IsNight;
        // 해 질 녘에는 밤 밝기의 절반까지만 — 엔딩의 밤이 더 환해야 대비가 남는다
        float level = night ? 1f : dusk * 0.5f;
        bool turning = night || dusk >= beamsFromDusk;
        if (Mathf.Abs(level - shown) > 0.005f || (beams != null && beams.activeSelf != turning)) Apply(level, turning);
        if (turning && beamPivot != null) beamPivot.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }

    void Apply(float level, bool turning)
    {
        shown = level;
        if (beams != null && beams.activeSelf != turning) beams.SetActive(turning);
        if (lantern == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        lantern.GetPropertyBlock(block);
        block.SetColor(EmissionId, Color.Lerp(dayGlow, nightGlow, level));
        lantern.SetPropertyBlock(block);
    }
}
