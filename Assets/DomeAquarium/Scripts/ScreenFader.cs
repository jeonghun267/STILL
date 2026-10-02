using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 카메라 바로 앞에 붙는 가림막. 두 가지를 한다.
///  - Fade: 순간이동(로비 → 수중 돔)을 검은 화면으로 가린다. 눈앞이 갑자기 바뀌는 게 멀미의 가장 큰 원인이다.
///  - Vignette: 자동으로 걸어가는 동안 시야 가장자리를 어둡게 해 주변시 흐름(벡션)을 줄인다.
///
/// 왜 포스트프로세싱이 아닌가: XR 카메라는 포스트프로세싱이 꺼져 있다(블룸도 안 보였던 이유).
/// 그래서 카메라 자식 월드 캔버스 한 장으로 처리한다 — 드로우콜 2개, 셰이더 패스 추가 없음.
/// </summary>
public sealed class ScreenFader : MonoBehaviour
{
    const float Distance = 0.25f;          // 근평면(0.05)보다 멀고, 손·UI 보다 가깝게
    const float QuadMeters = 1.3f;         // 0.25m 앞 1.3m 판 = 시야각 약 138°, Quest 시야를 다 덮는다
    const float VignetteResponse = 5f;

    Image black;
    Image vignette;
    float vignetteTarget;
    float vignetteValue;

    public float FadeAlpha => black != null ? black.color.a : 0f;

    /// <summary>카메라에 가림막을 하나만 붙인다. 이미 있으면 그것을 돌려준다.</summary>
    public static ScreenFader Ensure(Camera cam)
    {
        if (cam == null) return null;
        var existing = cam.GetComponentInChildren<ScreenFader>(true);
        if (existing != null) return existing;

        var go = new GameObject("ScreenFader");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, Distance);
        go.transform.localRotation = Quaternion.identity;
        var fader = go.AddComponent<ScreenFader>();
        fader.Build(cam);
        return fader;
    }

    void Build(Camera cam)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        // 다른 월드 캔버스(시작 패널·정보 카드)보다 나중에 그려야 위를 덮는다.
        canvas.sortingOrder = 32000;

        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(1000f, 1000f);
        rt.localScale = Vector3.one * (QuadMeters / 1000f);

        vignette = NewImage("Vignette", MakeVignetteSprite());
        black = NewImage("Black", null);
        SetAlpha(black, 0f);
        SetAlpha(vignette, 0f);
    }

    Image NewImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = Color.black;
        img.raycastTarget = false;     // 가림막이 버튼 클릭을 가로채면 안 된다
        return img;
    }

    // 가운데는 투명, 가장자리로 갈수록 검게. 텍스처 파일 없이 런타임에 128px 로 만든다.
    static Sprite MakeVignetteSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "ScreenFaderVignette"
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                // 시야 중심 약 ±35° 는 그대로 두고 그 바깥부터 어둡게 — 걸어가는 방향은 계속 보인다.
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.80f, r));
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    void Update()
    {
        if (vignette == null) return;
        float k = 1f - Mathf.Exp(-VignetteResponse * Time.unscaledDeltaTime);
        vignetteValue = Mathf.Lerp(vignetteValue, vignetteTarget, k);
        SetAlpha(vignette, vignetteValue);
    }

    /// <summary>0 = 없음, 1 = 가장자리 완전히 어둡게.</summary>
    public void SetVignette(float strength) => vignetteTarget = Mathf.Clamp01(strength);

    public void SetFadeImmediate(float alpha) => SetAlpha(black, Mathf.Clamp01(alpha));

    public IEnumerator FadeTo(float target, float seconds)
    {
        if (black == null) yield break;
        float from = black.color.a;
        float duration = Mathf.Max(0.01f, seconds);
        for (float t = 0f; t < 1f;)
        {
            t += Time.unscaledDeltaTime / duration;
            SetAlpha(black, Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))));
            yield return null;
        }
        SetAlpha(black, target);
    }

    static void SetAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
        // 완전히 투명하면 아예 그리지 않는다 — 평소에는 비용 0.
        bool on = a > 0.001f;
        if (img.enabled != on) img.enabled = on;
    }
}
