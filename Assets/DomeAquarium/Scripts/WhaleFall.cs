using System.Collections;
using UnityEngine;

/// <summary>
/// 1,000m 고래 낙하 — 돔 아래 바닥(눈보다 9m 아래)에 가라앉은 고래 뼈와 그 위에 모인 생물.
/// 심해 생물 셰이더라 빛이 닿을 때만 보인다(관찰기 조명은 5.5m 까지라 닿지 않는다). 신호의 주인을 만나고 이름을 붙인 뒤,
/// 올라가기 전에 관측소장이 "바닥 조명"을 잠깐 켜서 보여 준다(Reveal) — "불을 끄고 가만히" 를 깨지 않게 만남이 끝난 다음에.
///   뼈 — 고래 뼈대 모델(Sketchfab CC-BY) · 붉은 솜털 — 뼈먹는 벌레(오세닥스) · 하얀 게 · 바닥의 박테리아 매트.
/// 바닥 조명은 심해 생물 셰이더 전역 값(_DeviceLight*)을 위에서 아래로 넓게 비추는 것으로 낸다.
/// </summary>
[DefaultExecutionOrder(300)]
public sealed class WhaleFall : MonoBehaviour
{
    public GameObject group;                    // 1,000m 에서만 켠다 (얕은 층에서는 햇빛에 보인다)
    public Transform center;                    // 뼈 가운데 (바닥)
    public float lightHeight = 7f;
    public float lightRange = 16f;
    public float lightAngle = 110f;
    public float lightIntensity = 2.6f;
    public Color lightColor = new Color(0.9f, 0.95f, 1f);
    public AudioSource sound;
    public AudioClip switchClip;

    [TextArea(1, 3)] public string lightOnLine = "올라가기 전에 하나만 더 보여 줄게요. 관측소 바닥 조명을 잠깐 켤게요 — 발밑, {0} 보세요.";
    [TextArea(2, 4)] public string boneLine =
        "고래 뼈예요. 죽은 고래가 깊은 바다 바닥까지 가라앉은 '고래 낙하'예요. 먹이가 귀한 심해에서 고래 한 마리는 수십 년 동안 수많은 생물의 잔치가 돼요.";
    [TextArea(2, 4)] public string wormLine =
        "뼈 위의 붉은 솜털 보이죠? 뼈만 먹고 사는 '뼈먹는 벌레', 오세닥스예요. 하얀 게들도 남은 것을 찾아 모였어요. 이제 조명을 끌게요.";

    float level;

    public bool Revealing => level > 0f;

    void Awake()
    {
        if (group != null) group.SetActive(false);
    }

    /// <summary>바닥 조명을 켜고 관측소장이 들려준 뒤 끈다. where 는 "오른쪽 앞을" 같은 방향.</summary>
    public IEnumerator Reveal(LobbySequence lobby, string where)
    {
        if (group == null || center == null) yield break;
        group.SetActive(true);
        if (lobby != null) StartCoroutine(lobby.SayLine(string.Format(lightOnLine, where)));
        yield return new WaitForSeconds(2.2f);
        if (sound != null && switchClip != null) sound.PlayOneShot(switchClip);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 2.5f) { level = Mathf.SmoothStep(0f, 1f, t); yield return null; }   // 천천히 밝아진다
        level = 1f;
        if (lobby != null)
        {
            while (FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking) yield return null;
            yield return lobby.SayLine(boneLine);
            yield return lobby.SayLine(wormLine);
        }
        else yield return new WaitForSeconds(12f);
        if (sound != null && switchClip != null) sound.PlayOneShot(switchClip);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.5f) { level = 1f - t; yield return null; }
        level = 0f;
        Shader.SetGlobalVector(DevicePosId, Vector4.zero);
    }

    static readonly int DevicePosId = Shader.PropertyToID("_DeviceLightPos");
    static readonly int DeviceDirId = Shader.PropertyToID("_DeviceLightDir");
    static readonly int DeviceParamsId = Shader.PropertyToID("_DeviceLightParams");
    static readonly int DeviceColorId = Shader.PropertyToID("_DeviceLightColor");

    void LateUpdate()
    {
        if (level <= 0f || center == null) return;
        Vector3 p = center.position + Vector3.up * lightHeight;
        float outer = lightAngle * 0.5f;
        Shader.SetGlobalVector(DevicePosId, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalVector(DeviceDirId, Vector3.down);
        Shader.SetGlobalVector(DeviceParamsId, new Vector4(lightRange, Mathf.Cos(outer * Mathf.Deg2Rad), Mathf.Cos(outer * 0.6f * Mathf.Deg2Rad), lightIntensity * level));
        Shader.SetGlobalColor(DeviceColorId, lightColor);
    }

    void OnDisable()
    {
        if (level > 0f) Shader.SetGlobalVector(DevicePosId, Vector4.zero);
        level = 0f;
    }

#if UNITY_EDITOR
    public float DebugLevel => level;
#endif
}
