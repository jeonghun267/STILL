using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 쌍안경 꼬리 사진 식별. 혹등고래가 잠수하며 꼬리를 드는 동안 쌍안경으로 그 고래를 보고 있으면(이름표가 뜬 채) "찰칵" 찍힌다.
/// 확대 화면 아래쪽에 내 사진과 관측소 고래 목록의 꼬리 사진이 나란히 떠서, 목록을 한 장씩 넘기며 맞춰 보다가 무늬가 같은 '하얀별'에서 멈춘다.
/// 혹등고래 꼬리 아랫면의 흑백 무늬는 사람 지문처럼 저마다 달라, 실제 연구자도 이렇게 고래를 한 마리씩 알아본다(사진 식별).
/// 한 번 알아보면 이름표가 "혹등고래 '하얀별'"로 바뀌고, 선장이 그 이름으로 받아 준다(NpcDialogue whale_found_id).
/// </summary>
public sealed class FlukePhotoId : MonoBehaviour
{
    public BinocularViewer viewer;
    public WhaleSurfacing whale;
    public LobbySequence lobby;

    [Header("확대 화면 위 (빌더가 채운다)")]
    public GameObject panel;                    // 사진 둘 + 글 — 평소에는 꺼 둔다
    public Renderer photo;                      // 내가 찍은 사진 (확대 화면을 그대로 복사)
    public Renderer catalog;                    // 관측소 목록 사진 — 넘겨 보다 맞는 것에서 멈춘다
    public Renderer catalogFrame;               // 맞으면 초록 테두리
    public Renderer flash;                      // 찍는 순간 하얗게
    public TMP_Text result;
    public Texture2D[] catalogPictures;         // 관측소 고래 목록 — 맞는 것은 match 번째
    public string[] catalogNames;
    public int match;
    public AudioSource sound;
    public AudioClip shutter;
    public AudioClip found;

    [TextArea(2, 4)] public string line =
        "찰칵! 꼬리 사진이 찍혔어요. 혹등고래 꼬리 아랫면 무늬는 사람 지문처럼 고래마다 달라요. " +
        "관측소 목록과 맞춰 보니… '하얀별'이에요! 해마다 이 만에 돌아오는 고래예요.";
    public string identifiedLabel = "혹등고래 '하얀별' · 관측소 고래 목록 7번";
    public float showSeconds = 7f;

    /// <summary>이번 체험에서 꼬리 사진으로 '하얀별'을 알아봤나 — 선장 대사가 받는다.</summary>
    public static bool Identified { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Identified = false;

    RenderTexture shot;
    MaterialPropertyBlock block;
    bool busy;
    bool takenThisDive;

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (flash != null) flash.enabled = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        busy = false;
        if (panel != null) panel.SetActive(false);
        if (flash != null) flash.enabled = false;
    }

    void OnDestroy()
    {
        if (shot != null) shot.Release();
    }

    void LateUpdate()
    {
        if (whale == null || viewer == null) return;
        if (!whale.FlukeUp) { takenThisDive = false; return; }
        if (busy || Identified || takenThisDive || whale.subject == null || viewer.Named != whale.subject) return;
        takenThisDive = true;
        StartCoroutine(Take());
    }

    IEnumerator Take()
    {
        busy = true;
        // 찰칵 — 지금 확대 화면을 그대로 복사해 둔다
        RenderTexture src = viewer.zoomCamera != null ? viewer.zoomCamera.targetTexture : null;
        if (src != null)
        {
            if (shot == null) shot = new RenderTexture(src.width, src.height, 0, src.format);
            Graphics.Blit(src, shot);
            SetTexture(photo, shot);
        }
        if (sound != null && shutter != null) sound.PlayOneShot(shutter);
        if (flash != null) yield return Flash();

        if (panel != null) panel.SetActive(true);
        if (catalogFrame != null) catalogFrame.enabled = false;
        if (result != null) result.text = "꼬리 무늬 대조 중…";
        // 목록을 두 바퀴 넘기고 맞는 사진에서 멈춘다 — 점점 느려진다
        int n = catalogPictures != null ? catalogPictures.Length : 0;
        if (n > 0)
        {
            float gap = 0.1f;
            for (int i = 0; i <= 2 * n + match; i++)
            {
                SetTexture(catalog, catalogPictures[i % n]);
                yield return new WaitForSeconds(gap);
                gap *= 1.1f;
            }
        }
        Identified = true;
        if (catalogFrame != null) catalogFrame.enabled = true;
        string name = catalogNames != null && match < catalogNames.Length ? catalogNames[match] : "";
        if (result != null) result.text = $"일치!  관측소 고래 목록 · {name}";
        if (whale.subject != null) whale.subject.label = identifiedLabel;
        if (sound != null && found != null) sound.PlayOneShot(found);
        if (lobby != null && !string.IsNullOrWhiteSpace(line)) lobby.StartCoroutine(SayWhenQuiet());

        yield return new WaitForSeconds(showSeconds);
        if (panel != null) panel.SetActive(false);
        busy = false;
    }

    /// <summary>처음 이름표가 뜰 때의 관측소장 말과 겹치지 않게, 하던 말이 끝나면 말한다.</summary>
    IEnumerator SayWhenQuiet()
    {
        float start = Time.time;
        var tts = FishNarrationService.Instance;
        while (tts != null && tts.IsSpeaking && Time.time - start < 12f) yield return null;
        yield return lobby.SayLine(line);
    }

    IEnumerator Flash()
    {
        flash.enabled = true;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            SetColor(flash, new Color(1f, 1f, 1f, Mathf.Lerp(0.9f, 0f, t / 0.3f)));
            yield return null;
        }
        flash.enabled = false;
    }

    void SetTexture(Renderer r, Texture tex)
    {
        if (r == null || tex == null) return;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetTexture("_BaseMap", tex);
        r.SetPropertyBlock(block);
    }

    void SetColor(Renderer r, Color c)
    {
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", c);
        r.SetPropertyBlock(block);
    }

#if UNITY_EDITOR
    /// <summary>헤드셋 없이 확인할 때 — 고래가 꼬리를 들었는지와 상관없이 지금 찍는다.</summary>
    public void DebugTake()
    {
        if (!busy) StartCoroutine(Take());
    }

    public static void DebugReset() => Identified = false;
#endif
}
