using UnityEngine;

/// <summary>
/// Gemini 호출 설정. Resources 에 두어 빌드에 포함시킨다.
///
/// 주의: 프로젝트 루트의 .env 는 에디터에서만 읽히고 Quest 빌드에는 들어가지 않는다.
/// 그래서 에디터 메뉴(DomeAquarium/8)가 .env 의 키를 이 에셋에 복사한다.
/// 이 에셋은 APK 안에 들어가므로 키가 기기에 실려 나간다 — 추출이 가능하다.
/// 학내 시연 수준에서는 흔한 방식이지만, 외부 배포를 한다면 중계 서버를 두고
/// 이 필드는 비워 두는 편이 맞다.
/// </summary>
[CreateAssetMenu(fileName = "GeminiSettings", menuName = "DomeAquarium/Gemini Settings")]
public class GeminiSettings : ScriptableObject
{
    public const string ResourcePath = "GeminiSettings";

    [Header("인증")]
    public string apiKey = "";

    [Header("모델")]
    // 별칭을 쓴다. 버전을 박아두면 그 모델이 내려갈 때 404 가 나는데,
    // 실제로 gemini-2.0-flash 로 두었다가 전 종이 404 로 실패했다.
    public string model = "gemini-flash-latest";
    // ★ 넉넉히 줘야 한다.
    // 최신 Gemini 는 "생각" 토큰을 이 예산에서 먼저 쓴다. 220 으로 뒀더니
    // thinking 에 489 토큰을 쓰고 답변은 19 토큰만 남아 문장이 중간에 잘렸다(MAX_TOKENS).
    // 실측: thinking 725 + 답변 67 = 약 800. 2048 이면 안전하다.
    // 답변 길이는 프롬프트의 "3문장 120자" 규칙이 잡으므로 이 값을 키워도 길어지지 않는다.
    public int maxOutputTokens = 2048;
    [Range(0f, 1f)] public float temperature = 0.25f;   // 낮게 — 사실 진술이라 창작을 억제한다
    public int timeoutSeconds = 12;

    [Header("프롬프트")]
    [TextArea(6, 14)]
    public string promptTemplate =
        "너는 아쿠아리움 도슨트야. 아래 해양생물을 관람객에게 소개해 줘.\n\n" +
        "종 이름: {KOREAN}\n" +
        "학명/영문명: {SCIENTIFIC}\n\n" +
        "규칙:\n" +
        "- 한국어 3문장, 총 120자 이내.\n" +
        "- 확실히 아는 사실만 말해. 크기·서식지·특징 중 확실한 것만 골라 써.\n" +
        "- 이 종을 정확히 특정할 수 없으면 첫 줄에 정확히 '정보없음' 이라고만 답해.\n" +
        "- 수치를 지어내지 마. 애매하면 수치를 빼고 서술해.\n" +
        "- 인사말, 머리말, 목록 기호 없이 설명 문장만 출력해.";

    [Header("동작")]
    public bool enableGemini = true;
    public string loadingText = "설명을 불러오는 중…";
    public string failureText = "";        // 비우면 실패 시 설명 줄을 그냥 숨긴다

    static GeminiSettings cached;

    public static GeminiSettings Load()
    {
        if (cached != null) return cached;
        cached = Resources.Load<GeminiSettings>(ResourcePath);
        return cached;
    }

    public bool IsUsable => enableGemini && !string.IsNullOrWhiteSpace(apiKey);
}
