#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 해설 · NPC 대사를 미리 녹음해 넣는다 (VoiceBank). 기기(APK)에는 API 키가 없어 그때그때 받을 수 없다.
///   1. 대사 목록 만들기 — 장면 · 에셋에서 말하는 문장을 모아 Audio/Voice/lines.json 으로 (목소리 · 문장 · 열쇠).
///      말하는 문장 = 이름이 line 이거나 …Line 으로 끝나는 문자열 필드(배열 · 안쪽 묶음 포함), NPC 대화 마디, 물고기 설명(폴백),
///      관측소장의 이름 붙이기 말. {name} 은 이름 후보(ObservationTerms.SignalNames)마다 펼친다.
///      플레이 중 녹음이 없던 문장(Library/VoiceMissing.jsonl, FishNarrationService 가 적는다)도 더한다.
///   2. Tools/bake_voice.py — lines.json 에서 아직 없는 것만 Gemini 로 녹음해 Audio/Voice/{열쇠}.wav 로 (키는 .env, 하루 한도를 넘으면 멈췄다가 다음 날 이어서).
///   3. 녹음 등록 — wav 를 Vorbis · 모노로 가져오게 하고 Resources/VoiceBank 에 묶는다.
/// 목소리: NPC 대화 · NPC 필드(놀란 · 선장)를 가진 구성 요소의 대사는 그 NPC 목소리, 나머지는 관측소장.
/// </summary>
public static class DomeAquariumVoiceBank
{
    public const string VoiceDir = "Assets/DomeAquarium/Audio/Voice";
    const string ListPath = VoiceDir + "/lines.json";
    const string BankPath = "Assets/DomeAquarium/Resources/VoiceBank.asset";
    static readonly HashSet<string> NotSpoken = new HashSet<string> { "taskLine" };   // 판에만 쓰는 한 줄

    [Serializable] public class Line { public string key, voice, text, from; }
    [Serializable] class LineList { public List<Line> lines = new List<Line>(); }
    [Serializable] struct MissingLine { public string voice; public string text; }

    [MenuItem("DomeAquarium/음성/1. 대사 목록 만들기", priority = 400)]
    public static void CollectMenu()
    {
        var list = Collect(out int skipped);
        Directory.CreateDirectory(VoiceDir);
        File.WriteAllText(ListPath, JsonUtility.ToJson(new LineList { lines = list }, true), new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(ListPath);
        int have = list.Count(l => File.Exists($"{VoiceDir}/{l.key}.wav"));
        float chars = list.Where(l => !File.Exists($"{VoiceDir}/{l.key}.wav")).Sum(l => l.text.Length);
        Debug.Log($"[Voice] 대사 {list.Count}줄 (목소리별 {string.Join(", ", list.GroupBy(l => l.voice).Select(g => $"{g.Key} {g.Count()}"))}) · " +
                  $"녹음 있음 {have} · 남음 {list.Count - have} (약 {chars / 6.5f / 60f:0}분) · 자리 채움 문장 {skipped}줄은 건너뜀 → {ListPath}");
    }

    [MenuItem("DomeAquarium/음성/2. 녹음 등록 (VoiceBank)", priority = 401)]
    public static void RegisterMenu()
    {
        if (!File.Exists(ListPath)) { Debug.LogError("[Voice] lines.json 이 없다 — 1. 대사 목록 만들기를 먼저."); return; }
        var list = JsonUtility.FromJson<LineList>(File.ReadAllText(ListPath)).lines;
        AssetDatabase.Refresh();
        var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(BankPath);
        if (bank == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BankPath));
            bank = ScriptableObject.CreateInstance<VoiceBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.entries.Clear();
        foreach (var l in list)
        {
            string path = $"{VoiceDir}/{l.key}.wav";
            if (!File.Exists(path)) continue;
            Configure(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) bank.entries.Add(new VoiceBank.Entry { key = l.key, voice = l.voice, text = l.text, clip = clip });
        }
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Voice] VoiceBank — 녹음 {bank.entries.Count} / 대사 {list.Count}줄. 남은 것은 Tools/bake_voice.py 로 녹음한 뒤 다시 등록.");
    }

    /// <summary>모노 · Vorbis · 메모리에 압축해 둔 채로 — 수백 줄이라 Quest 메모리와 APK 크기를 아낀다.</summary>
    static void Configure(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) return;
        var s = ai.defaultSampleSettings;
        bool changed = !ai.forceToMono || s.loadType != AudioClipLoadType.CompressedInMemory || s.compressionFormat != AudioCompressionFormat.Vorbis || Mathf.Abs(s.quality - 0.5f) > 0.01f;
        if (!changed) return;
        ai.forceToMono = true;
        s.loadType = AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.5f;
        ai.defaultSampleSettings = s;
        ai.SaveAndReimport();
    }

    // ─────────────────────────────────────────────────────────────
    // 대사 모으기
    // ─────────────────────────────────────────────────────────────

    public static List<Line> Collect(out int skipped)
    {
        var settings = GeminiSettings.Load();
        string narrator = FishNarrationService.ResolveVoice(null, settings);
        var lines = new Dictionary<string, Line>();
        int skip = 0;
        void Add(string voice, string text, string from)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (text.Contains("{0}") || text.Contains("{1}")) { skip++; return; }   // 실행 중에 채우는 문장 — 플레이 기록(VoiceMissing)이 모은다
            string key = FishNarrationService.KeyFor(voice, text);
            if (!lines.ContainsKey(key)) lines[key] = new Line { key = key, voice = voice, text = text, from = from };
        }
        var names = ObservationTerms.SignalNames;

        // 장면 — 열린 장면의 모든 구성 요소(꺼진 것 포함)
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string from = $"{mb.GetType().Name} ({mb.gameObject.name})";
            if (mb is NpcDialogue npc)
            {
                string v = FishNarrationService.ResolveVoice(npc.voice, settings);
                foreach (var node in npc.nodes ?? new NpcDialogue.Node[0])
                    foreach (var t in Expand(node.line, names, n => NpcDialogue.FillName(node.line, n))) Add(v, t, from);
                continue;
            }
            // NPC 필드(놀란 · 선장)를 가진 구성 요소는 그 NPC 가 말한다. 이름은 이야기가 직접 채우면 '이름', NPC 가 채우면 조사까지
            try
            {
                var speaker = SpeakerOf(mb);
                string voice = speaker != null ? FishNarrationService.ResolveVoice(speaker.voice, settings) : narrator;
                bool storyNames = mb.GetType().GetMethod("NameLine", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, new[] { typeof(string) }, null) != null;
                foreach (string raw in SpokenStrings(mb, 0).ToList())
                    foreach (var t in Expand(raw, names, n => storyNames || speaker == null ? NightStory.NameLine(raw, n) : NpcDialogue.FillName(raw, n)))
                        Add(voice, t, from);
            }
            catch (Exception e) { Debug.LogWarning($"[Voice] {from} 를 읽다 멈췄다: {e.Message}"); }
        }

        // 에셋 — 층 설정 · 물고기 설명(기기에서는 Gemini 설명 대신 폴백 문장을 읽는다). 물고기는 장면이 쓰는 것만 — 팩의 모든 종을 녹음하면 한도만 쓴다
        var roots = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
            .SelectMany(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects()).Cast<UnityEngine.Object>().ToArray();
        var usedFish = new HashSet<FishData>(EditorUtility.CollectDependencies(roots).OfType<FishData>());
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/DomeAquarium" }))
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || so is GeminiSettings || so is VoiceBank) continue;
            if (so is FishData fd)
            {
                if (usedFish.Count == 0 || usedFish.Contains(fd))
                    Add(narrator, FishNarrationService.Compose(fd.unnamed ? string.Empty : fd.DisplayName, fd.FallbackDescription), $"FishData ({fd.name})");
                continue;
            }
            foreach (string raw in SpokenStrings(so, 0))
                foreach (var t in Expand(raw, names, n => NightStory.NameLine(raw, n))) Add(narrator, t, $"{so.GetType().Name} ({so.name})");
        }

        // 코드가 만드는 말 — 이름 붙이기
        Add(narrator, ObservationTerms.NameTitle + ".", "ObservationTerms");
        foreach (string n in names) Add(narrator, ObservationTerms.NamedLine(n), "ObservationTerms");

        // 플레이 중 녹음이 없던 문장
        string missing = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "VoiceMissing.jsonl");
        if (File.Exists(missing))
            foreach (string row in File.ReadAllLines(missing))
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                var m = JsonUtility.FromJson<MissingLine>(row);
                Add(m.voice, m.text, "플레이 중 기록");
            }

        skipped = skip;
        return lines.Values.OrderBy(l => l.from, StringComparer.Ordinal).ThenBy(l => l.text, StringComparer.Ordinal).ToList();
    }

    static IEnumerable<string> Expand(string raw, string[] names, Func<string, string> fill)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        if (!raw.Contains("{name}")) { yield return raw; yield break; }
        foreach (string n in names) yield return fill(n);
    }

    static NpcDialogue SpeakerOf(MonoBehaviour mb)
    {
        foreach (var f in mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (f.FieldType == typeof(NpcDialogue) && (f.IsPublic || f.IsDefined(typeof(SerializeField), false)) && f.GetValue(mb) is NpcDialogue d && d != null)
                return d;
        return null;
    }

    /// <summary>말하는 문자열 — 이름이 line 이거나 …Line(s) 로 끝나는 문자열 · 문자열 배열, 그리고 직렬화되는 안쪽 묶음 속의 같은 이름.</summary>
    static IEnumerable<string> SpokenStrings(object o, int depth)
    {
        if (o == null || depth > 2) yield break;
        for (var type = o.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(ScriptableObject) && type != typeof(object); type = type.BaseType)
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) continue;
                if (f.IsDefined(typeof(NonSerializedAttribute), false) || NotSpoken.Contains(f.Name)) continue;
                bool spoken = f.Name == "line" || f.Name.EndsWith("Line", StringComparison.Ordinal) || f.Name.EndsWith("Lines", StringComparison.Ordinal);
                object v = f.GetValue(o);
                if (v == null) continue;
                if (f.FieldType == typeof(string)) { if (spoken) yield return (string)v; continue; }
                if (v is IEnumerable seq && !(v is UnityEngine.Object))
                {
                    foreach (var item in seq)
                    {
                        if (item is string s) { if (spoken) yield return s; continue; }
                        if (item == null || item is UnityEngine.Object || !item.GetType().IsDefined(typeof(SerializableAttribute), false)) continue;
                        foreach (var inner in SpokenStrings(item, depth + 1)) yield return inner;
                    }
                    continue;
                }
                if (!(v is UnityEngine.Object) && f.FieldType.IsDefined(typeof(SerializableAttribute), false) && !f.FieldType.IsPrimitive)
                    foreach (var inner in SpokenStrings(v, depth + 1)) yield return inner;
            }
    }
}
#endif
