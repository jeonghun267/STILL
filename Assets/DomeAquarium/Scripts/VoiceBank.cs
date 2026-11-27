using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미리 녹음한 해설 · NPC 대사 (Resources/VoiceBank). FishNarrationService 가 Gemini 에 묻기 전에 먼저 찾는다.
/// 문장 + 목소리(FishNarrationService.KeyFor)로 찾고, 없으면 같은 문장의 다른 목소리라도 쓴다 — 말 없는 것보다 낫다.
/// 왜 미리 녹음하나: 그때그때 받으려면 APK 에 API 키가 실려야 하고(키가 새어 Google 이 막았다, 403),
/// 무료 한도(429)에 걸리면 목소리가 끊겼다. 녹음은 에디터에서 한다 — DomeAquarium/음성 메뉴와 Tools/bake_voice.py.
/// </summary>
public sealed class VoiceBank : ScriptableObject
{
    public const string ResourcePath = "VoiceBank";

    [System.Serializable]
    public struct Entry
    {
        public string key;
        public string voice;
        [TextArea(1, 3)] public string text;
        public AudioClip clip;
    }

    public List<Entry> entries = new List<Entry>();

    Dictionary<string, AudioClip> byKey, byText;

    public static VoiceBank Load() => Resources.Load<VoiceBank>(ResourcePath);

    public AudioClip Find(string key, string text)
    {
        if (byKey == null) Index();
        if (byKey.TryGetValue(key, out var clip)) return clip;
        return byText.TryGetValue(text, out clip) ? clip : null;
    }

    void Index()
    {
        byKey = new Dictionary<string, AudioClip>();
        byText = new Dictionary<string, AudioClip>();
        foreach (var e in entries)
        {
            if (e.clip == null) continue;
            byKey[e.key] = e.clip;
            if (!byText.ContainsKey(e.text)) byText[e.text] = e.clip;
        }
    }

    void OnEnable() => byKey = byText = null;
}
