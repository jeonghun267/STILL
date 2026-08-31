using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 공개된 물고기 아래에 붙어 따라다니는 읽기 전용 정보 패널.
// 설명은 사람 원고 > Gemini > 안전한 상위 분류 폴백 순서로 표시하고 한국어 TTS로 읽는다.
public class FishInfoPanel : MonoBehaviour
{
    [Header("루트")]
    public CanvasGroup canvasGroup;

    [Header("텍스트")]
    public TMP_Text nameText;
    public TMP_Text scientificText;
    public TMP_Text descriptionText;
    public TMP_Text sizeTagText;
    public TMP_Text habitatTagText;
    public TMP_Text rarityLabel;

    [Header("등급 표시")]
    public Image rarityBadge;
    public Image rarityGlow;

    [Header("추종")]
    public Transform player;
    public float dropBelowFish = 0.04f;
    public float sideOffset = -0.50f;
    public float towardViewerOffset = 0.06f;
    public float fadeTime = 0.35f;

    [Header("음성 안내")]
    public bool narrate = true;

    [Header("등급 테이블 (길이 5, FishRarity 순서)")]
    public Color[] rarityColors;
    public string[] rarityNames;

    // 인스펙터 배열이 비었거나 짧을 때의 안전 폴백 (인덱스 예외 금지)
    static readonly Color[] FallbackColors =
    {
        new Color(0.72f, 0.78f, 0.84f, 1f),
        new Color(0.45f, 0.82f, 0.55f, 1f),
        new Color(0.35f, 0.62f, 0.95f, 1f),
        new Color(0.72f, 0.45f, 0.92f, 1f),
        new Color(1.00f, 0.76f, 0.25f, 1f)
    };
    static readonly string[] FallbackNames = { "일반", "고급", "희귀", "영웅", "전설" };

    const int RarityCount = 5;
    const float FollowLambda = 14f;
    const float AlphaEpsilon = 0.001f;

    Transform anchor;
    Transform viewerCache;      // player 가 비었을 때의 Main Camera 캐시 (매 프레임 탐색 금지)
    Coroutine fadeRoutine;
    Coroutine descriptionFallbackRoutine;
    bool snapNextFrame;

    void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    // 지금 화면에 띄우고 있는 종. 늦게 온 Gemini 응답을 버릴지 판단하는 데 쓴다.
    private FishData pendingData;
    private int presentationId;
    private int narratedPresentationId = -1;

    /// <summary>물고기가 무대로 접근하는 동안 설명 요청을 미리 시작한다.</summary>
    public void PreloadDescription(FishData data)
    {
        if (data == null || data.HasDescription) return;
        var svc = GeminiDescriptionService.Instance;
        if (svc != null) svc.Request(data, _ => { });
    }

    public void Show(FishData data, Transform target)
    {
        if (data == null)
        {
            Debug.LogWarning("[FishInfoPanel] FishData 가 null 이라 표시를 건너뜁니다.", this);
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);

        anchor = target;
        snapNextFrame = true;
        pendingData = data;
        int token = ++presentationId;
        StopDescriptionFallback();
        FishNarrationService.Instance?.Stop();

        ApplyText(nameText, data.DisplayName);
        ApplyText(scientificText, data.SubtitleName);
        RequestDescription(data, token);
        ApplyText(sizeTagText, data.sizeTag);
        ApplyText(habitatTagText, data.habitatTag);

        int index = Mathf.Clamp((int)data.rarity, 0, RarityCount - 1);
        Color tint = (rarityColors != null && index < rarityColors.Length)
            ? rarityColors[index]
            : FallbackColors[index];

        if (rarityLabel != null)
        {
            string label = (rarityNames != null && index < rarityNames.Length && !string.IsNullOrEmpty(rarityNames[index]))
                ? rarityNames[index]
                : FallbackNames[index];
            if (!rarityLabel.gameObject.activeSelf) rarityLabel.gameObject.SetActive(true);
            // 현실의 멸종위기 등급이 아니라 뽑기 시스템의 게임 내 발견 등급임을 명확히 한다.
            rarityLabel.text = $"발견 등급  ·  {label}";
            rarityLabel.color = Color.Lerp(Color.white, tint, 0.48f);
        }

        if (rarityBadge != null) rarityBadge.color = tint;

        if (rarityGlow != null)
        {
            bool legendary = index == (int)FishRarity.Legendary;
            rarityGlow.color = tint;
            if (rarityGlow.gameObject.activeSelf != legendary) rarityGlow.gameObject.SetActive(legendary);
        }

        // 위치를 미리 한 번 맞춰서 알파가 오르는 동안 튀지 않게 한다.
        UpdateFollow(true);
        StartFade(1f);
    }

    /// <summary>
    /// 설명 줄을 채운다. 우선순위: 사람이 쓴 원고 > Gemini 응답 > 안전한 상위 분류 설명.
    /// 응답이 늦게 도착했는데 그 사이 다른 종으로 바뀌었으면 버린다.
    /// </summary>
    private void RequestDescription(FishData data, int token)
    {
        if (data.HasDescription) { ApplyDescriptionAndNarrate(data, data.description, token); return; }

        var svc = GeminiDescriptionService.Instance;
        if (svc == null) { ApplyDescriptionAndNarrate(data, data.FallbackDescription, token); return; }

        var cfg = GeminiSettings.Load();
        string loading = cfg != null ? cfg.loadingText : "설명을 불러오는 중…";

        // 캐시 적중이면 Request 가 그 자리에서 콜백하므로, 로딩 문구는 그 뒤에 덮인다.
        ApplyText(descriptionText, loading);

        // 네트워크가 느리거나 끊겨도 패널 표시 시간 안에 이름과 설명을 반드시 읽는다.
        // Gemini가 먼저 오면 이 폴백은 취소되고, 늦게 오면 화면 문구만 더 정확한 설명으로 갱신한다.
        if (isActiveAndEnabled)
            descriptionFallbackRoutine = StartCoroutine(UseFallbackAfterDelay(data, token));

        svc.Request(data, text =>
        {
            if (this == null) return;                 // 패널이 파괴됨
            if (pendingData != data) return;          // 이미 다른 종을 보고 있다
            if (presentationId != token) return;       // 이미 숨었거나 같은 종을 다시 열었다

            StopDescriptionFallback();
            string resolved = string.IsNullOrWhiteSpace(text) ? data.FallbackDescription : text;
            ApplyDescriptionAndNarrate(data, resolved, token);
        });
    }

    IEnumerator UseFallbackAfterDelay(FishData data, int token)
    {
        yield return new WaitForSecondsRealtime(1.4f);
        descriptionFallbackRoutine = null;

        if (pendingData != data || presentationId != token) yield break;
        ApplyDescriptionAndNarrate(data, data.FallbackDescription, token);
    }

    void StopDescriptionFallback()
    {
        if (descriptionFallbackRoutine == null) return;
        StopCoroutine(descriptionFallbackRoutine);
        descriptionFallbackRoutine = null;
    }

    void ApplyDescriptionAndNarrate(FishData data, string text, int token)
    {
        if (pendingData != data || presentationId != token) return;
        if (string.IsNullOrWhiteSpace(text)) text = data.FallbackDescription;

        ApplyText(descriptionText, text);
        if (narrate && narratedPresentationId != token)
        {
            narratedPresentationId = token;
            FishNarrationService.Instance?.Speak(data.DisplayName, text);
        }
    }

    public void Hide()
    {
        pendingData = null;
        presentationId++;
        StopDescriptionFallback();
        FishNarrationService.Instance?.Stop();
        StartFade(0f);
    }

    void ApplyText(TMP_Text field, string value)
    {
        if (field == null) return;

        bool has = !string.IsNullOrWhiteSpace(value);
        if (field.gameObject.activeSelf != has) field.gameObject.SetActive(has);
        if (has) field.text = value;
    }

    void StartFade(float targetAlpha)
    {
        if (canvasGroup == null)
        {
            if (targetAlpha <= 0f) anchor = null;
            return;
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        // 비활성 상태에서는 코루틴을 돌릴 수 없으니 즉시 반영한다.
        if (!isActiveAndEnabled || fadeTime <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            if (targetAlpha <= 0f) anchor = null;
            return;
        }

        fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha));
    }

    IEnumerator FadeRoutine(float targetAlpha)
    {
        float start = canvasGroup.alpha;
        float duration = Mathf.Max(0.01f, fadeTime);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, k);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        if (targetAlpha <= 0f) anchor = null;   // 다 사라진 뒤에 참조를 놓는다.

        fadeRoutine = null;
    }

    void LateUpdate()
    {
        // 알파 0이면 계산 자체를 건너뛴다 (성능).
        if (canvasGroup == null || canvasGroup.alpha <= AlphaEpsilon) return;
        UpdateFollow(false);
    }

    void UpdateFollow(bool snap)
    {
        if (anchor == null) return;

        Transform viewer = ResolveViewer();

        Vector3 desired = anchor.position - Vector3.up * dropBelowFish;

        // 물고기 바로 아래에 붙이면 카드와 모델이 겹쳐 형태를 볼 수 없다.
        // 사용자의 좌우 축을 기준으로 카드는 왼쪽에, 물고기는 오른쪽에 분리한다.
        if (viewer != null)
        {
            Vector3 right = viewer.right;
            right.y = 0f;
            if (right.sqrMagnitude > 1e-6f) right.Normalize();
            else right = Vector3.right;

            Vector3 towardViewer = viewer.position - anchor.position;
            if (towardViewer.sqrMagnitude > 1e-6f) towardViewer.Normalize();
            else towardViewer = -viewer.forward;

            desired += right * sideOffset + towardViewer * towardViewerOffset;
        }

        if (snap || snapNextFrame)
        {
            transform.position = desired;
            snapNextFrame = false;
        }
        else
        {
            float t = 1f - Mathf.Exp(-FollowLambda * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
        }

        if (viewer == null) return;

        // 빌보드: 플레이어를 향하되 롤(Z)은 0으로 고정 (기울어진 패널은 VR 멀미 유발)
        Vector3 away = transform.position - viewer.position;
        if (away.sqrMagnitude < 1e-6f) return;

        Quaternion look = Quaternion.LookRotation(away.normalized, Vector3.up);
        Vector3 euler = look.eulerAngles;
        transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
    }

    Transform ResolveViewer()
    {
        if (player != null) return player;
        if (viewerCache != null) return viewerCache;

        Camera cam = Camera.main;
        if (cam != null) viewerCache = cam.transform;
        return viewerCache;
    }

    void OnDisable()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        anchor = null;
        pendingData = null;
        presentationId++;
        StopDescriptionFallback();
        FishNarrationService.Instance?.Stop();
    }
}
