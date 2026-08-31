using UnityEngine;

/// <summary>
/// 정보 패널의 한국어 이름과 설명을 읽는 Quest용 TTS 서비스.
/// Android 기기의 기본 TTS 엔진을 사용하므로 별도 음성 서버 호출이나 API 키가 필요 없다.
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class FishNarrationService : MonoBehaviour
{
    public static FishNarrationService Instance { get; private set; }

    public bool IsSpeaking
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // TTS 생성 자체가 실패한 경우 pendingText만으로 발화 중이라 판단하면
            // BGM이 해설 볼륨에 영구 고정된다.
            if (textToSpeech == null || initResult != 0 && initResult != int.MinValue) return false;
            if (!string.IsNullOrWhiteSpace(pendingText)) return true;
            try { return textToSpeech.Call<bool>("isSpeaking"); }
            catch (System.Exception) { return false; }
#else
            return false;
#endif
        }
    }

    [Range(0.5f, 1.5f)] public float speechRate = 0.88f;
    [Range(0.5f, 1.5f)] public float pitch = 1f;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject textToSpeech;
    volatile int initResult = int.MinValue;
    bool configured;
    string pendingText;

    sealed class InitListener : AndroidJavaProxy
    {
        readonly FishNarrationService owner;

        public InitListener(FishNarrationService owner)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            this.owner = owner;
        }

        public void onInit(int status)
        {
            owner.initResult = status;
        }
    }
#endif

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

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                textToSpeech = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    activity,
                    new InitListener(this));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[TTS] Android TTS 초기화 실패: " + e.Message);
        }
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (configured || initResult == int.MinValue) return;
        configured = true;

        // TextToSpeech.SUCCESS == 0
        if (initResult != 0 || textToSpeech == null)
        {
            Debug.LogWarning("[TTS] 기기의 기본 음성 엔진을 시작하지 못했습니다.");
            pendingText = null;
            return;
        }

        try
        {
            using (var localeClass = new AndroidJavaClass("java.util.Locale"))
            using (var korean = localeClass.GetStatic<AndroidJavaObject>("KOREAN"))
            {
                int languageResult = textToSpeech.Call<int>("setLanguage", korean);
                if (languageResult < 0)
                    Debug.LogWarning("[TTS] 기기에 한국어 음성 데이터가 없어 기본 음성으로 재생될 수 있습니다.");
            }
            textToSpeech.Call<int>("setSpeechRate", Mathf.Clamp(speechRate, 0.5f, 1.5f));
            textToSpeech.Call<int>("setPitch", Mathf.Clamp(pitch, 0.5f, 1.5f));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[TTS] 한국어 음성 설정 실패: " + e.Message);
        }

        if (!string.IsNullOrWhiteSpace(pendingText))
        {
            string text = pendingText;
            pendingText = null;
            SpeakNow(text);
        }
#endif
    }

    public void Speak(string koreanName, string description)
    {
        string cleanName = (koreanName ?? string.Empty).Trim();
        string cleanDescription = (description ?? string.Empty).Trim();
        // 새 Gemini 프롬프트와 로컬 폴백은 설명 첫머리에 이름을 포함하므로 두 번 읽지 않는다.
        string text = cleanDescription.StartsWith(cleanName, System.StringComparison.Ordinal)
            ? cleanDescription
            : (cleanName + ". " + cleanDescription).Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!configured)
        {
            pendingText = text;
            return;
        }
        SpeakNow(text);
#else
        // 에디터에서는 실제 Android 엔진 대신 발화할 문장을 로그로 검증한다.
        Debug.Log("[TTS 미리보기] " + text, this);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    void SpeakNow(string text)
    {
        if (textToSpeech == null || initResult != 0) return;
        try
        {
            // QUEUE_FLUSH(0): 이전 물고기 설명을 끊고 지금 보고 있는 물고기를 바로 읽는다.
            textToSpeech.Call<int>("speak", text, 0, null, "fish-info");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[TTS] 발화 실패: " + e.Message);
        }
    }
#endif

    public void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        pendingText = null;
        if (textToSpeech == null) return;
        try { textToSpeech.Call<int>("stop"); }
        catch (System.Exception) { }
#endif
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (textToSpeech != null)
        {
            try
            {
                textToSpeech.Call("stop");
                textToSpeech.Call("shutdown");
            }
            catch (System.Exception) { }
            textToSpeech.Dispose();
            textToSpeech = null;
        }
#endif
    }
}
