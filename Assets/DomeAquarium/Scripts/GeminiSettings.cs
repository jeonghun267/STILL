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

    public const string DefaultPromptTemplate =
        "너는 아쿠아리움의 한국어 도슨트야. 아래 이름들은 같은 전시 생물을 가리켜.\n\n" +
        "한국어 표시명: {KOREAN}\n" +
        "영문 통용명: {ENGLISH}\n" +
        "학명: {SCIENTIFIC}\n" +
        "데이터 ID: {ID}\n\n" +
        "규칙:\n" +
        "- 한국어 3문장, 총 120자 이내로 써.\n" +
        "- 첫 문장은 반드시 '{KOREAN}'으로 시작해.\n" +
        "- 정확한 종을 알면 그 종의 검증 가능한 특징을 설명해.\n" +
        "- 관상어 품종명·상품명이거나 정확한 종이 불확실하면 속·과·일반 분류 수준까지만 설명해.\n" +
        "- '정보없음'만 답하지 말고, 확실한 상위 분류의 안전한 사실을 설명해.\n" +
        "- 수치와 서식 범위를 추측하지 마. 인사말, 머리말, 목록 기호는 쓰지 마.";

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
    public string promptTemplate = DefaultPromptTemplate;

    [Header("동작")]
    public bool enableGemini = true;
    public string loadingText = "설명을 불러오는 중…";
    public string failureText = "";        // 비우면 실패 시 설명 줄을 그냥 숨긴다

    [Header("음성 해설 (Gemini TTS)")]
    // Quest(Horizon OS)에는 Android 시스템 TTS 엔진이 아예 없다 — `pm list packages` 에 TTS 패키지 0개,
    // TextToSpeech 는 "not bound to TTS engine" 으로 끝난다. 그래서 해설 음성도 Gemini 로 만든다.
    // 같은 API 키를 쓰고, 결과 오디오는 기기에 캐시해 같은 문장은 두 번 호출하지 않는다.
    public bool enableNarration = true;
    public string ttsModel = "gemini-2.5-flash-preview-tts";
    // 사전 정의 음성 이름. Kore(차분한 여성), Puck, Charon, Fenrir, Aoede, Leda 등. 한국어도 같은 음성으로 읽는다.
    public string ttsVoice = "Kore";
    public int ttsTimeoutSeconds = 25;

    static GeminiSettings cached;

    public static GeminiSettings Load()
    {
        if (cached != null) return cached;
        cached = Resources.Load<GeminiSettings>(ResourcePath);
        return cached;
    }

    public bool IsUsable => enableGemini && !string.IsNullOrWhiteSpace(apiKey);
}
