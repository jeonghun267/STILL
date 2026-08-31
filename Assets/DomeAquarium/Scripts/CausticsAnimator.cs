using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 수면에서 굴절된 햇빛이 바닥에 만드는 일렁이는 무늬(코스틱)를 움직인다.
///
/// 왜 라이트 쿠키인가:
///  - 프로젝터/데칼은 Quest 2 에서 화면 공간 패스가 추가로 붙어 비싸다.
///  - 바닥·산호 셰이더를 전부 고치는 건 Layer Lab 에셋을 침범하는 일이고 종류도 많다.
///  - 디렉셔널 라이트 쿠키는 조명 계산에 텍스처 샘플 한 번이 더해질 뿐이고 드로우콜이 0 개 늘어난다.
///    게다가 바닥뿐 아니라 물고기·바위·산호에도 같은 무늬가 얹혀 실제 수중처럼 보인다.
///
/// 움직임은 offset 을 리사주 곡선으로 흘려서 만든다. 일직선으로 스크롤하면
/// "텍스처가 미끄러진다"는 게 눈에 보이는데, 두 축의 주기를 어긋나게 하면 물결처럼 읽힌다.
/// </summary>
[RequireComponent(typeof(Light))]
public class CausticsAnimator : MonoBehaviour
{
    [Header("흐름")]
    public float speed = 0.035f;          // 낮게. 빠르면 수중이 아니라 디스코가 된다
    public float driftX = 1.0f;           // 두 축의 주기를 다르게 둬야 물결처럼 보인다
    public float driftY = 0.73f;
    public float swirl = 0.6f;            // 리사주 진폭

    [Header("스케일")]
    public Vector2 cookieSize = new Vector2(14f, 14f);   // 월드 단위. 수조 지름 24m 기준

    UniversalAdditionalLightData data;
    Vector2 baseOffset;
    bool ready;

    void Awake()
    {
        data = GetComponent<UniversalAdditionalLightData>();
        if (data == null) data = gameObject.AddComponent<UniversalAdditionalLightData>();

        data.lightCookieSize = cookieSize;
        baseOffset = data.lightCookieOffset;
        cachedLight = GetComponent<Light>();
    }

    Light cachedLight;

    void Update()
    {
        // 쿠키가 나중에 배정될 수도 있으므로 컴포넌트를 영구히 끄지 않고 매 프레임 값싸게 확인한다.
        // (예전에는 Awake 에서 enabled = false 로 꺼 버려, 쿠키를 나중에 넣으면 무늬가 멈춰 있었다)
        if (cachedLight == null || cachedLight.cookie == null) return;
        if (!ready) { data.lightCookieSize = cookieSize; ready = true; }

        float t = Time.time * speed;
        // 주기가 서로 나누어떨어지지 않아 무늬가 같은 자리로 되돌아오지 않는다.
        float x = baseOffset.x + Mathf.Sin(t * driftX * Mathf.PI * 2f) * swirl + t * 0.35f;
        float y = baseOffset.y + Mathf.Cos(t * driftY * Mathf.PI * 2f) * swirl + t * 0.21f;

        data.lightCookieOffset = new Vector2(x, y);
    }
}
