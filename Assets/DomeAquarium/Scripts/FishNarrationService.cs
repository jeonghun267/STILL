using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 정보 패널·도감의 한국어 이름과 설명을 읽어 주는 해설 음성 서비스.
///
/// 왜 Android 시스템 TTS 가 아닌가:
///  - Quest(Horizon OS)에는 TTS 엔진이 하나도 설치돼 있지 않다. 기기에서 확인한 결과
///    `pm list packages` 에 TTS 패키지 0개, `tts_default_synth` 는 null, TextToSpeech 는
///    "not bound to TTS engine" 만 남기고 아무 소리도 내지 않는다. Play 스토어도 없어 설치할 수도 없다.
///  - 그래서 설명문을 만드는 것과 같은 Gemini API 키로 음성(PCM)을 받아 AudioSource 로 재생한다.
///
/// 동작:
///  - 문장(음성 이름 포함) 해시로 메모리 → 디스크(persistentDataPath/tts/*.wav) 순으로 캐시를 찾는다.
///    같은 물고기를 다시 뽑거나 앱을 다시 켜도 네트워크를 다시 타지 않는다.
///  - 새 문장을 요청하면 진행 중이던 요청/재생은 버린다 (마지막 요청만 살아남는다).
///  - 에디터에서도 그대로 동작하므로 헤드셋 없이 해설을 들어 볼 수 있다.
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class FishNarrationService : MonoBehaviour
{
    public static FishNarrationService Instance { get; private set; }

    const string CacheFolder = "tts";
    const string GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/";
    const int DefaultSampleRate = 24000;   // Gemini TTS 는 16-bit mono 24kHz PCM 을 돌려준다

    /// <summary>
    /// 음성을 받아 오는 중이거나 재생 중이면 true. 배경음악 덕킹이 이 값을 본다.
    /// 요청이 실패하면 반드시 false 로 돌아와야 한다 — 아니면 BGM 이 해설 볼륨에 영구 고정된다.
    /// </summary>
    public bool IsSpeaking => fetching || (source != null && source.isPlaying);

    [Range(0f, 1f)] public float volume = 1f;

    AudioSource source;
    GeminiSettings settings;
    string cacheDir;

    readonly Dictionary<string, AudioClip> memoryCache = new Dictionary<string, AudioClip>();
    int requestToken;          // Speak 마다 증가. 늦게 도착한 옛 응답은 버린다.
    bool fetching;
    Coroutine fetchRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var existing = FindFirstObjectByType<FishNarrationService>();
        if (existing != null) return;

        var host = new GameObject("FishNarrationService");
        DontDestroyOnLoad(host);
        host.AddComponent<FishNarrationService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;      // 해설은 머릿속에서 들리는 2D 사운드
        source.priority = 0;           // 물고기 떼 SFX 가 많아도 해설 채널은 뺏기지 않는다
        source.volume = volume;

        settings = GeminiSettings.Load();
        if (settings == null)
            Debug.LogWarning("[TTS] Resources/GeminiSettings 를 찾지 못했다. 해설 음성이 꺼진다.");
        else if (!settings.IsUsable)
            Debug.LogWarning("[TTS] Gemini API 키가 비어 있거나 기능이 꺼져 있다. 해설 음성 없이 동작한다.");

        cacheDir = Path.Combine(Application.persistentDataPath, CacheFolder);
        try { Directory.CreateDirectory(cacheDir); }
        catch (Exception e) { Debug.LogWarning("[TTS] 캐시 폴더 생성 실패: " + e.Message); cacheDir = null; }
    }

    public void Speak(string koreanName, string description)
    {
        string cleanName = (koreanName ?? string.Empty).Trim();
        string cleanDescription = (description ?? string.Empty).Trim();
        // 새 Gemini 프롬프트와 로컬 폴백은 설명 첫머리에 이름을 포함하므로 두 번 읽지 않는다.
        string text = cleanDescription.StartsWith(cleanName, StringComparison.Ordinal)
            ? cleanDescription
            : (cleanName + ". " + cleanDescription).Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // 새 문장이 오면 이전 것은 재생이든 요청이든 전부 버린다.
        Stop();

        if (settings == null || !settings.IsUsable || !settings.enableNarration) return;

        int token = ++requestToken;
        string key = CacheKey(settings.ttsVoice, text);

        if (memoryCache.TryGetValue(key, out AudioClip cached) && cached != null)
        {
            Play(cached);
            return;
        }

        AudioClip fromDisk = LoadFromDisk(key);
        if (fromDisk != null)
        {
            memoryCache[key] = fromDisk;
            Play(fromDisk);
            return;
        }

        fetchRoutine = StartCoroutine(Fetch(token, key, text));
    }

    public void Stop()
    {
        requestToken++;                 // 진행 중인 Fetch 가 결과를 버리게 한다
        fetching = false;
        if (fetchRoutine != null) { StopCoroutine(fetchRoutine); fetchRoutine = null; }
        if (source != null && source.isPlaying) source.Stop();
    }

    void Play(AudioClip clip)
    {
        if (source == null || clip == null) return;
        source.volume = volume;
        source.clip = clip;
        source.Play();
    }

    IEnumerator Fetch(int token, string key, string text)
    {
        fetching = true;

        string url = GeminiEndpoint + settings.ttsModel + ":generateContent";
        string body = BuildRequestJson(text, settings.ttsVoice);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            // 키는 쿼리스트링이 아니라 헤더로 보낸다 — URL 은 로그·프록시에 그대로 남는다.
            req.SetRequestHeader("x-goog-api-key", settings.apiKey);
            req.timeout = Mathf.Max(5, settings.ttsTimeoutSeconds);

            yield return req.SendWebRequest();

            // 기다리는 동안 다른 문장이 왔거나 Stop 됐으면 조용히 버린다.
            if (token != requestToken) yield break;

            fetching = false;
            fetchRoutine = null;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TTS] 음성 요청 실패: {req.responseCode} {req.error}");
                yield break;
            }

            if (!TryExtractPcm(req.downloadHandler.text, out byte[] pcm, out int sampleRate))
            {
                Debug.LogWarning("[TTS] 응답에 오디오가 없다. 모델 이름(ttsModel)이 TTS 모델인지 확인.");
                yield break;
            }

            AudioClip clip = ClipFromPcm16(pcm, sampleRate, key);
            if (clip == null) yield break;

            memoryCache[key] = clip;
            SaveToDisk(key, pcm, sampleRate);
            Play(clip);
        }
    }

    // ── 요청 본문 ──
    // {"contents":[{"parts":[{"text":"..."}]}],
    //  "generationConfig":{"responseModalities":["AUDIO"],
    //     "speechConfig":{"voiceConfig":{"prebuiltVoiceConfig":{"voiceName":"Kore"}}}}}
    static string BuildRequestJson(string text, string voice)
    {
        var sb = new StringBuilder(text.Length + 240);
        sb.Append("{\"contents\":[{\"parts\":[{\"text\":\"");
        AppendEscaped(sb, text);
        sb.Append("\"}]}],\"generationConfig\":{\"responseModalities\":[\"AUDIO\"],")
          .Append("\"speechConfig\":{\"voiceConfig\":{\"prebuiltVoiceConfig\":{\"voiceName\":\"");
        AppendEscaped(sb, string.IsNullOrWhiteSpace(voice) ? "Kore" : voice.Trim());
        sb.Append("\"}}}}}");
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
    // candidates[0].content.parts[0].inlineData = { mimeType: "audio/L16;codec=pcm;rate=24000", data: base64 }
    [Serializable] class GInline { public string mimeType; public string data; }
    [Serializable] class GPart { public GInline inlineData; }
    [Serializable] class GContent { public GPart[] parts; }
    [Serializable] class GCandidate { public GContent content; }
    [Serializable] class GResponse { public GCandidate[] candidates; }

    static bool TryExtractPcm(string json, out byte[] pcm, out int sampleRate)
    {
        pcm = null;
        sampleRate = DefaultSampleRate;
        if (string.IsNullOrEmpty(json)) return false;

        try
        {
            var res = JsonUtility.FromJson<GResponse>(json);
            var parts = res?.candidates != null && res.candidates.Length > 0 ? res.candidates[0]?.content?.parts : null;
            if (parts == null) return false;

            foreach (var p in parts)
            {
                if (p?.inlineData == null || string.IsNullOrEmpty(p.inlineData.data)) continue;
                pcm = Convert.FromBase64String(p.inlineData.data);
                sampleRate = ParseRate(p.inlineData.mimeType, DefaultSampleRate);
                return pcm.Length > 1;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TTS] 응답 파싱 실패: " + e.Message);
        }
        return false;
    }

    static int ParseRate(string mime, int fallback)
    {
        if (string.IsNullOrEmpty(mime)) return fallback;
        int i = mime.IndexOf("rate=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return fallback;
        i += 5;
        int end = i;
        while (end < mime.Length && char.IsDigit(mime[end])) end++;
        return int.TryParse(mime.Substring(i, end - i), out int r) && r > 0 ? r : fallback;
    }

    // ── PCM16 LE mono → AudioClip ──
    static AudioClip ClipFromPcm16(byte[] pcm, int sampleRate, string name)
    {
        int samples = pcm.Length / 2;
        if (samples <= 0) return null;

        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
            data[i] = s / 32768f;
        }

        var clip = AudioClip.Create("tts-" + name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ── 디스크 캐시 (표준 WAV 라 헤더에 샘플레이트가 들어 있어 따로 기록할 게 없다) ──
    string CachePath(string key) => cacheDir == null ? null : Path.Combine(cacheDir, key + ".wav");

    AudioClip LoadFromDisk(string key)
    {
        string path = CachePath(key);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            byte[] wav = File.ReadAllBytes(path);
            if (wav.Length <= 44) return null;
            int rate = BitConverter.ToInt32(wav, 24);
            var pcm = new byte[wav.Length - 44];
            Buffer.BlockCopy(wav, 44, pcm, 0, pcm.Length);
            return ClipFromPcm16(pcm, rate > 0 ? rate : DefaultSampleRate, key);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TTS] 캐시 읽기 실패: " + e.Message);
            return null;
        }
    }

    void SaveToDisk(string key, byte[] pcm, int sampleRate)
    {
        string path = CachePath(key);
        if (path == null) return;
        try
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                      // fmt 청크 크기
                w.Write((short)1);                // PCM
                w.Write((short)1);                // mono
                w.Write(sampleRate);
                w.Write(sampleRate * 2);          // byte rate
                w.Write((short)2);                // block align
                w.Write((short)16);               // bits per sample
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TTS] 캐시 쓰기 실패: " + e.Message);
        }
    }

    // 문장 + 음성 이름의 FNV-1a 64비트 해시. 파일명으로 쓰기 좋게 16자리 hex.
    static string CacheKey(string voice, string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong h = offset;
        string s = (voice ?? string.Empty) + "\n" + text;
        foreach (char c in s)
        {
            h ^= c;
            h *= prime;
        }
        return h.ToString("x16");
    }

    [ContextMenu("음성 캐시 비우기")]
    public void ClearCache()
    {
        Stop();
        memoryCache.Clear();
        if (cacheDir != null && Directory.Exists(cacheDir))
        {
            try { foreach (var f in Directory.GetFiles(cacheDir, "*.wav")) File.Delete(f); }
            catch (Exception e) { Debug.LogWarning("[TTS] 캐시 삭제 실패: " + e.Message); }
        }
        Debug.Log("[TTS] 음성 캐시를 비웠다.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
