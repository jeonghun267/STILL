using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 어종 설명을 Gemini 로 받아 온다.
///
/// 설계 원칙:
///  - 모델에는 한국어명·교정된 영문명·학명·ID를 함께 보내 종 식별 가능성을 높인다.
///  - speciesId 로 캐시한다. 같은 종을 다시 뽑아도 두 번 호출하지 않는다 (할당량·지연 절약).
///  - 캐시는 PlayerPrefs 에 남겨 앱을 다시 켜도 유지된다.
///  - FishData.description 이 이미 채워져 있으면 그쪽이 항상 우선이다 (사람이 쓴 원고가 우선).
/// </summary>
public class GeminiDescriptionService : MonoBehaviour
{
    const string PrefsPrefix = "domeaq.desc.";

    public static GeminiDescriptionService Instance { get; private set; }

    readonly Dictionary<string, string> memoryCache = new Dictionary<string, string>();
    readonly HashSet<string> inFlight = new HashSet<string>();
    readonly Dictionary<string, List<Action<string>>> waiters = new Dictionary<string, List<Action<string>>>();

    GeminiSettings settings;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        settings = GeminiSettings.Load();
        if (settings == null)
            Debug.LogWarning("[Gemini] Resources/GeminiSettings 를 찾지 못했다. 설명 생성이 꺼진다.");
        else if (!settings.IsUsable)
            Debug.LogWarning("[Gemini] API 키가 비어 있거나 기능이 꺼져 있다. 설명 없이 동작한다.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 설명을 요청한다. 캐시가 있으면 그 자리에서 콜백이 불린다.
    /// 실패하거나 모델이 특정하지 못하면 빈 문자열로 콜백한다 — 호출부는 안전한 상위 분류 설명을 쓴다.
    /// </summary>
    public void Request(FishData data, Action<string> onDone)
    {
        if (onDone == null) return;
        if (data == null) { onDone(string.Empty); return; }

        // 사람이 쓴 원고가 최우선.
        if (data.HasDescription) { onDone(data.description); return; }

        string id = KeyFor(data);

        if (memoryCache.TryGetValue(id, out string hit)) { onDone(hit); return; }

        string saved = PlayerPrefs.GetString(PrefsPrefix + id, null);
        if (!string.IsNullOrEmpty(saved))
        {
            memoryCache[id] = saved;
            onDone(saved);
            return;
        }

        if (settings == null || !settings.IsUsable) { onDone(string.Empty); return; }

        // 같은 종을 동시에 여러 번 요청하면 한 번만 보내고 결과를 나눠 준다.
        if (!waiters.TryGetValue(id, out var list))
        {
            list = new List<Action<string>>();
            waiters[id] = list;
        }
        list.Add(onDone);

        if (inFlight.Contains(id)) return;
        inFlight.Add(id);
        StartCoroutine(Fetch(id, data));
    }

    static string KeyFor(FishData d)
    {
        if (!string.IsNullOrWhiteSpace(d.speciesId)) return d.speciesId;
        return string.IsNullOrWhiteSpace(d.name) ? d.DisplayName : d.name;
    }

    /// <summary>
    /// 프리팹 이름을 모델이 알아볼 수 있는 영어 종명으로 다듬는다.
    /// Layer Lab 팩은 "SpeckeldButterfly"(Speckled 오타), "GreateWhiteShark"(Great 오타) 처럼
    /// 붙여쓰기 + 철자 오류가 섞여 있어서 그대로 넘기면 모델이 종을 특정하지 못한다.
    /// </summary>
    public static string ToEnglishName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // 팩에 실제로 들어 있는 오타들
        string s = raw
            .Replace("Speckeld", "Speckled")
            .Replace("Greate", "Great")
            .Replace("Dophin", "Dolphin")
            .Replace("Jennnifer", "Jennifer")
            .Replace("Pygme", "Pygmy")
            .Replace("Kauderns", "Kaudern's")
            .Replace("Bleekers", "Bleeker's")
            .Replace("Wheelers", "Wheeler's")
            .Replace("Achiles", "Achilles")
            .Replace("Goldneon", "Gold Neon")
            .Replace("Pazama", "Pajama")
            .Replace("JamTang", "GemTang");

        // 붙어 있는 낱말을 띄운다: "PowderBlueTang" -> "Powder Blue Tang"
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ' && s[i - 1] != '\'')
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    IEnumerator Fetch(string id, FishData data)
    {
        string english = data.CanonicalEnglishName;
        string korean = data.DisplayName;
        string scientific = string.IsNullOrWhiteSpace(data.scientificName) ? "미확인" : data.scientificName;
        string speciesId = string.IsNullOrWhiteSpace(data.speciesId) ? data.name : data.speciesId;

        string prompt = settings.promptTemplate
            .Replace("{KOREAN}", korean)
            .Replace("{ENGLISH}", english)
            .Replace("{SCIENTIFIC}", scientific)
            .Replace("{ID}", speciesId);

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{settings.model}:generateContent";
        string body = BuildRequestJson(prompt, settings.maxOutputTokens, settings.temperature);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            // 키는 쿼리스트링이 아니라 헤더로 보낸다 — URL 은 로그·프록시에 그대로 남는다.
            req.SetRequestHeader("x-goog-api-key", settings.apiKey);
            req.timeout = Mathf.Max(3, settings.timeoutSeconds);

            yield return req.SendWebRequest();

            string result = string.Empty;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Gemini] '{id}' 요청 실패: {req.responseCode} {req.error}");
            }
            else
            {
                result = ExtractText(req.downloadHandler.text);
                if (IsUnknown(result))
                {
                    // 호출부가 프리팹 이름에서 확실한 상위 분류 설명으로 대체한다.
                    Debug.Log($"[Gemini] '{id}' — 정확한 종 설명이 없어 안전한 범주 설명으로 대체한다.");
                    result = string.Empty;
                }
            }

            // 성공했을 때만 캐시한다. 실패는 다음에 다시 시도할 수 있어야 한다.
            if (!string.IsNullOrEmpty(result))
            {
                memoryCache[id] = result;
                PlayerPrefs.SetString(PrefsPrefix + id, result);
                PlayerPrefs.Save();
            }

            inFlight.Remove(id);
            if (waiters.TryGetValue(id, out var list))
            {
                waiters.Remove(id);
                for (int i = 0; i < list.Count; i++)
                {
                    try { list[i]?.Invoke(result); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }
    }

    static bool IsUnknown(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim();
        return t.StartsWith("정보없음") || t.StartsWith("정보 없음");
    }

    // JsonUtility 는 중첩 배열에 약해서 요청 본문은 직접 만든다.
    static string BuildRequestJson(string prompt, int maxTokens, float temperature)
    {
        var sb = new StringBuilder(prompt.Length + 200);
        sb.Append("{\"contents\":[{\"parts\":[{\"text\":\"");
        AppendEscaped(sb, prompt);
        sb.Append("\"}]}],\"generationConfig\":{\"maxOutputTokens\":");
        sb.Append(maxTokens);
        sb.Append(",\"temperature\":");
        sb.Append(temperature.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append("}}");
        return sb.ToString();
    }

    static void AppendEscaped(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
    }

    // ── 응답 파싱 (JsonUtility 용 최소 스키마) ──
    [Serializable] class GPart { public string text; }
    [Serializable] class GContent { public GPart[] parts; }
    [Serializable] class GCandidate { public GContent content; public string finishReason; }
    [Serializable] class GResponse { public GCandidate[] candidates; }

    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        try
        {
            var res = JsonUtility.FromJson<GResponse>(json);
            if (res?.candidates == null || res.candidates.Length == 0) return string.Empty;
            var c = res.candidates[0];
            if (c?.content?.parts == null || c.content.parts.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var p in c.content.parts)
                if (p != null && !string.IsNullOrEmpty(p.text)) sb.Append(p.text);

            return sb.ToString().Trim();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Gemini] 응답 파싱 실패: " + e.Message);
            return string.Empty;
        }
    }

    [ContextMenu("설명 캐시 비우기")]
    public void ClearCache()
    {
        foreach (var k in memoryCache.Keys) PlayerPrefs.DeleteKey(PrefsPrefix + k);
        memoryCache.Clear();
        PlayerPrefs.Save();
        Debug.Log("[Gemini] 설명 캐시를 비웠다.");
    }
}
