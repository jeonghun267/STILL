using System.Collections;
using UnityEngine;

/// <summary>
/// 로비 창밖의 실제 바다 면. 예전에는 HDRI 사진 속 바다뿐이라 수면이 없었다 — 물고기가 물에 들어가도 몸통을 가릴 수 없었고,
/// 로비 안에서 자리를 옮기면 사진과 물체가 따로 놀았다.
///
///  - 수평선까지 이어진 큰 판(불투명)이 수면이다. 그 아래로 들어간 것은 정말로 가려진다.
///  - 물결: URP Lit 노멀맵(큰 물결)과 디테일 노멀맵(잔물결)을 서로 다른 방향으로 흘린다 — 한 장만 흘리면 그림이 미끄러져 보인다.
///  - 하늘 반사: 로비 하늘만 담아 미리 구운 큐브맵을 반사 프로브(창밖에만 영향)로 비춘다 — 수중 돔용 기본 반사를 비추지 않게.
///    skyProbe 에 실시간 프로브를 주면 로비 하늘로 바뀐 뒤 한 번 찍는다.
///    해(디렉셔널 라이트)가 HDRI 의 해와 같은 방향이라 물 위에 반짝이는 길이 생긴다.
/// 비용: 판 1장 + 노멀맵 두 번 샘플. Quest 에서 투명·평면 반사를 쓰지 않는다.
/// </summary>
[RequireComponent(typeof(Renderer))]
public sealed class SeaSurface : MonoBehaviour
{
    public ReflectionProbe skyProbe;
    public Vector2 swell = new Vector2(0.010f, 0.006f);      // 큰 물결 흐름 (타일/초)
    public Vector2 ripple = new Vector2(-0.008f, 0.014f);    // 잔물결 — 다른 방향으로

    public float SeaLevel => transform.position.y;

    static readonly int BaseST = Shader.PropertyToID("_BaseMap_ST");
    static readonly int DetailST = Shader.PropertyToID("_DetailAlbedoMap_ST");   // 디테일 노멀맵도 이 UV 를 쓴다

    Renderer surface;
    MaterialPropertyBlock block;
    Vector4 swellST, rippleST;

    void Start()
    {
        surface = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        Material m = surface.sharedMaterial;
        swellST = m != null && m.HasProperty(BaseST) ? m.GetVector(BaseST) : new Vector4(1f, 1f, 0f, 0f);
        rippleST = m != null && m.HasProperty(DetailST) ? m.GetVector(DetailST) : swellST;
        if (skyProbe != null && skyProbe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) StartCoroutine(CaptureSky());
    }

    IEnumerator CaptureSky()
    {
        // LobbySequence.Start 가 하늘을 로비 HDRI 로 바꾼 뒤에 찍는다.
        yield return null;
        yield return null;
        skyProbe.RenderProbe();
    }

    void Update()
    {
        float t = Time.time;
        surface.GetPropertyBlock(block);
        block.SetVector(BaseST, new Vector4(swellST.x, swellST.y, Mathf.Repeat(swellST.z + swell.x * t, 1f), Mathf.Repeat(swellST.w + swell.y * t, 1f)));
        block.SetVector(DetailST, new Vector4(rippleST.x, rippleST.y, Mathf.Repeat(rippleST.z + ripple.x * t, 1f), Mathf.Repeat(rippleST.w + ripple.y * t, 1f)));
        surface.SetPropertyBlock(block);
    }
}
