#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>기존 수중 환경음 위에 Aquatic Downtime BGM과 상황별 오디오 디렉터를 배선한다.</summary>
public static class DomeAquariumAudioSetup
{
    const string MusicPath = "Assets/DomeAquarium/Audio/geoffharvey-aquatic-downtime-363761.mp3";
    const string AmbientPath =
        "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/DemoAssets/Audio/AmbientBackgroundNoise.mp3";

    [MenuItem("DomeAquarium/16. 수중 BGM 적용", priority = 16)]
    public static void ApplyMenu() { Apply(true); }

    public static AquariumAudioDirector Apply(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[오디오] 플레이 모드에서는 BGM 배선을 변경할 수 없습니다.");
            return null;
        }

        AudioClip musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        if (musicClip == null)
        {
            Debug.LogError("[오디오] Aquatic Downtime MP3를 찾지 못했습니다: " + MusicPath);
            return null;
        }

        GameObject root = GameObject.Find("AmbientAudio");
        if (root == null) root = new GameObject("AmbientAudio");

        AquariumAudioDirector director = root.GetComponent<AquariumAudioDirector>();
        if (director == null) director = root.AddComponent<AquariumAudioDirector>();

        AudioSource[] sources = root.GetComponents<AudioSource>();
        AudioSource ambient = director.ambientSource;
        if (ambient == null)
        {
            ambient = sources.FirstOrDefault(source => source != null && source != director.musicSource
                && source.clip != musicClip);
            if (ambient == null) ambient = root.AddComponent<AudioSource>();
        }

        AudioSource music = director.musicSource;
        if (music == null)
        {
            music = sources.FirstOrDefault(source => source != null && source != ambient
                && source.clip == musicClip);
            if (music == null) music = root.AddComponent<AudioSource>();
        }

        if (ambient.clip == null) ambient.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AmbientPath);
        ambient.loop = true;
        ambient.playOnAwake = true;
        ambient.spatialBlend = 0f;
        ambient.priority = 200;
        ambient.volume = 0.09f;

        music.clip = musicClip;
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.priority = 180;
        music.volume = 0f;

        director.ambientSource = ambient;
        director.musicSource = music;
        director.introAmbientVolume = 0.09f;
        director.activeAmbientVolume = 0.05f;
        director.normalMusicVolume = 0.10f;
        director.encyclopediaMusicVolume = 0.06f;
        director.revealMusicVolume = 0.05f;
        director.narrationMusicVolume = 0.025f;
        director.fadeInSeconds = 2f;

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(ambient);
        EditorUtility.SetDirty(music);
        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (saveScene) EditorSceneManager.SaveOpenScenes();

        Debug.Log("[오디오] Aquatic Downtime BGM 적용 완료 — 시작 페이드인, 도감/뽑기/TTS 덕킹 활성.");
        return director;
    }
}
#endif
