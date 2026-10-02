using UnityEngine;

/// <summary>
/// 시작 전 환경음, 체험 BGM, 도감/뽑기/TTS 상황별 음량을 한 곳에서 부드럽게 조절한다.
/// Android TTS는 Unity AudioMixer 밖에서 재생되므로 AudioSource 볼륨을 직접 덕킹한다.
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class AquariumAudioDirector : MonoBehaviour
{
    public static AquariumAudioDirector Instance { get; private set; }

    [Header("소스")]
    public AudioSource ambientSource;
    public AudioSource musicSource;

    [Header("환경음")]
    [Range(0f, 1f)] public float introAmbientVolume = 0.09f;
    [Range(0f, 1f)] public float activeAmbientVolume = 0.05f;

    [Header("BGM 상황별 볼륨")]
    [Range(0f, 1f)] public float normalMusicVolume = 0.10f;
    [Range(0f, 1f)] public float encyclopediaMusicVolume = 0.06f;
    [Range(0f, 1f)] public float revealMusicVolume = 0.05f;
    [Range(0f, 1f)] public float narrationMusicVolume = 0.025f;
    [Min(0.1f)] public float fadeInSeconds = 2f;

    FishEncyclopediaPanel encyclopedia;
    FishReveal reveal;
    bool experienceStarted;
    bool ambientMuted;          // 육지 로비에서는 물속 환경음이 들리면 안 된다
    float eventDuckUntil;
    float musicFadeElapsed;
    bool musicFadeInActive;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveSceneReferences();

        if (ambientSource != null)
        {
            ambientSource.loop = true;
            ambientSource.spatialBlend = 0f;
            ambientSource.volume = introAmbientVolume;
            if (!ambientSource.isPlaying && ambientSource.clip != null) ambientSource.Play();
        }

        if (musicSource != null)
        {
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.playOnAwake = false;
            musicSource.volume = 0f;
            musicSource.Stop();
        }
    }

    void Update()
    {
        if (encyclopedia == null || reveal == null) ResolveSceneReferences();

        bool narration = FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking;
        float musicTarget = 0f;
        if (experienceStarted)
        {
            if (narration) musicTarget = narrationMusicVolume;
            else if (Time.unscaledTime < eventDuckUntil || (reveal != null && reveal.IsBusy))
                musicTarget = revealMusicVolume;
            else if (encyclopedia != null && encyclopedia.IsOpen)
                musicTarget = encyclopediaMusicVolume;
            else musicTarget = normalMusicVolume;
        }

        float ambientTarget = ambientMuted ? 0f : experienceStarted ? activeAmbientVolume : introAmbientVolume;
        if (musicSource != null)
        {
            if (musicFadeInActive)
            {
                musicFadeElapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(musicFadeElapsed / Mathf.Max(0.1f, fadeInSeconds));
                musicSource.volume = musicTarget * Mathf.SmoothStep(0f, 1f, progress);
                if (progress >= 1f) musicFadeInActive = false;
            }
            else
            {
                float response = musicTarget < musicSource.volume ? 7f : 2f;
                musicSource.volume = Damp(musicSource.volume, musicTarget, response);
            }
        }
        if (ambientSource != null)
            ambientSource.volume = Damp(ambientSource.volume, ambientTarget, 2f);
    }

    public void BeginExperience()
    {
        experienceStarted = true;
        if (musicSource == null || musicSource.clip == null) return;
        if (!musicSource.isPlaying)
        {
            musicSource.volume = 0f;
            musicSource.Play();
        }
        musicFadeElapsed = 0f;
        musicFadeInActive = true;
    }

    /// <summary>
    /// 물속 환경음을 끄거나 다시 켠다. 끌 때는 즉시(로비 첫 프레임에 물소리가 새지 않게),
    /// 켤 때는 Update 의 감쇠를 따라 서서히 올라온다.
    /// </summary>
    public void SetAmbientMuted(bool muted)
    {
        ambientMuted = muted;
        if (muted && ambientSource != null) ambientSource.volume = 0f;
    }

    public void PulseEventDuck(float seconds = 1.5f)
    {
        eventDuckUntil = Mathf.Max(eventDuckUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
    }

    float Damp(float current, float target, float response)
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, response) * Time.unscaledDeltaTime);
        return Mathf.Lerp(current, target, t);
    }

    void ResolveSceneReferences()
    {
        if (encyclopedia == null) encyclopedia = FindFirstObjectByType<FishEncyclopediaPanel>();
        if (reveal == null) reveal = FindFirstObjectByType<FishReveal>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
