#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로비 수심 벽이 쓰는 물속 소리 3개를 코드로 만든다. 외부 음원이 없으니 라이선스 걱정이 없고,
/// 시드를 고정해 다시 만들어도 같은 소리가 나온다.
///
///   24m    산호초    밝은 물소리(잡음 900Hz 아래) + 느린 너울 + 잦은 거품 소리
///   200m   어스름    어두운 물소리(260Hz 아래) + 낮은 화음(C2·G2) + 가끔 거품
///   1,000m 깊은 바다 아주 낮은 웅웅거림(120Hz 아래) + 낮은 화음(G1·D2) + 드문드문 맑은 '띵' (생물 빛)
///
/// 12초 고리 — 끝 1.5초를 처음과 겹쳐 이어 붙여 이음매가 들리지 않는다. 22,050Hz 모노 16비트 WAV.
/// 실제 녹음으로 바꾸고 싶으면 같은 이름의 파일을 덮어쓰면 된다 (이 도구는 파일이 없을 때만 만든다).
/// </summary>
public static class DomeAquariumDepthSounds
{
    public const string Dir = "Assets/DomeAquarium/Audio/Depth";
    const int Rate = 22050;
    const float Seconds = 12f;
    const float Overlap = 1.5f;

    struct Recipe
    {
        public int seed;
        public float cutoff, noiseRms;          // 물소리: 저역 통과 주파수, 세기
        public float swellHz, swellDepth;       // 너울(느린 크기 변화)
        public float[] padHz;
        public float padGain;
        public float bubbleRate, bubbleLo, bubbleHi, bubbleGain;
        public float chimeRate, chimeGain;
    }

    static Recipe RecipeOf(string id)
    {
        switch (id)
        {
            case "Sunlit":
                return new Recipe { seed = 24, cutoff = 900f, noiseRms = 0.11f, swellHz = 0.11f, swellDepth = 0.35f,
                                    padHz = new float[0], padGain = 0f,
                                    bubbleRate = 5f, bubbleLo = 500f, bubbleHi = 1400f, bubbleGain = 0.16f };
            case "Twilight":
                return new Recipe { seed = 200, cutoff = 260f, noiseRms = 0.12f, swellHz = 0.07f, swellDepth = 0.3f,
                                    padHz = new[] { 65.41f, 98.00f }, padGain = 0.09f,
                                    bubbleRate = 0.7f, bubbleLo = 250f, bubbleHi = 600f, bubbleGain = 0.1f };
            default:
                return new Recipe { seed = 1000, cutoff = 120f, noiseRms = 0.11f, swellHz = 0.05f, swellDepth = 0.25f,
                                    padHz = new[] { 49.00f, 73.42f }, padGain = 0.08f,
                                    chimeRate = 0.45f, chimeGain = 0.07f };
        }
    }

    [MenuItem("DomeAquarium/디버그/로비 소리 다시 만들기 (수심 벽 · 물보라)", priority = 215)]
    public static void Regenerate()
    {
        foreach (string id in new[] { "Sunlit", "Twilight", "Midnight" }) Write(id);
        AssetDatabase.Refresh();
        EnsureSplash("Small", force: true);
        EnsureSplash("Big", force: true);
        EnsureSonarPing(force: true);
        Debug.Log($"[DepthSounds] 소리를 다시 만들었다: 수심 벽 3개({Dir}) · 물보라 2개({LobbyDir}) · 소나 핑({DiveDir})");
    }

    /// <summary>소리 파일이 없으면 만들고, 가져오기 설정을 맞춘 뒤 돌려준다.</summary>
    public static AudioClip Ensure(string id)
    {
        string path = PathOf(id);
        if (!File.Exists(path))
        {
            Write(id);
            AssetDatabase.ImportAsset(path);
        }
        Configure(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static string PathOf(string id) => $"{Dir}/Depth_{id}.wav";

    static void Configure(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) return;
        var s = imp.defaultSampleSettings;
        if (s.loadType == AudioClipLoadType.CompressedInMemory && s.compressionFormat == AudioCompressionFormat.Vorbis) return;
        s.loadType = AudioClipLoadType.CompressedInMemory;   // 12초 모노 — 스트리밍할 만큼 길지 않다
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.5f;
        imp.defaultSampleSettings = s;
        imp.forceToMono = true;
        imp.SaveAndReimport();
    }

    static void Write(string id)
    {
        Directory.CreateDirectory(Dir);
        float[] samples = Synthesize(RecipeOf(id));
        File.WriteAllBytes(PathOf(id), ToWav(samples));
    }

    // ─────────────────────────────────────────────────────────────
    // 합성
    // ─────────────────────────────────────────────────────────────

    static float[] Synthesize(Recipe r)
    {
        int n = (int)(Seconds * Rate);
        int x = (int)(Overlap * Rate);
        int total = n + x;
        var rng = new System.Random(r.seed);

        // 물소리 — 흰 잡음을 두 번 저역 통과해 부드럽게 하고, 아주 낮은 웅웅거림(35Hz 아래)은 뺀다.
        var water = new float[total];
        float aLow = OnePole(r.cutoff), aRumble = OnePole(35f);
        float l1 = 0f, l2 = 0f, rumble = 0f;
        double sumSq = 0;
        for (int i = 0; i < total; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            l1 += aLow * (white - l1);
            l2 += aLow * (l1 - l2);
            rumble += aRumble * (l2 - rumble);
            water[i] = l2 - rumble;
            sumSq += water[i] * water[i];
        }
        float gain = r.noiseRms / Mathf.Max(1e-6f, (float)Math.Sqrt(sumSq / total));

        var buf = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)Rate;
            // 주기가 어긋난 두 너울을 겹쳐 규칙적으로 들리지 않게 한다.
            float swell = 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * r.swellHz * t))
                        * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * r.swellHz * 0.61f * t + 1.3f));
            buf[i] = water[i] * gain * (1f - r.swellDepth * swell);

            for (int k = 0; k < r.padHz.Length; k++)
            {
                float f = r.padHz[k];
                float wobble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.06f * t + k * 2.1f);
                // 살짝 어긋난 두 음을 겹쳐 맥놀이 — 깊은 곳의 느린 울림
                buf[i] += r.padGain * wobble * 0.5f * (Mathf.Sin(2f * Mathf.PI * f * t) + Mathf.Sin(2f * Mathf.PI * f * 1.004f * t + k));
            }
        }

        if (r.bubbleRate > 0f) AddBubbles(buf, rng, r);
        if (r.chimeRate > 0f) AddChimes(buf, rng, r);

        // 고리 이음 — 끝 x 샘플을 처음 x 샘플과 같은 힘으로 겹친다.
        var output = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (i < x)
            {
                float a = i / (float)x;
                output[i] = buf[i] * Mathf.Sqrt(a) + buf[n + i] * Mathf.Sqrt(1f - a);
            }
            else output[i] = buf[i];
        }

        float peak = 0f;
        foreach (float s in output) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak > 0.85f) for (int i = 0; i < n; i++) output[i] *= 0.85f / peak;
        return output;
    }

    /// <summary>거품 — 짧게 올라가는 사인파. 물속 거품이 터질 때의 '뽁'.</summary>
    static void AddBubbles(float[] buf, System.Random rng, Recipe r)
    {
        double t = Exp(rng, r.bubbleRate);
        while (true)
        {
            int start = (int)(t * Rate);
            if (start >= buf.Length) break;
            float dur = Mathf.Lerp(0.03f, 0.07f, (float)rng.NextDouble());
            float f0 = Mathf.Lerp(r.bubbleLo, r.bubbleHi, (float)rng.NextDouble());
            float amp = r.bubbleGain * (0.4f + 0.6f * (float)rng.NextDouble());
            int len = (int)(dur * 2.5f * Rate);
            double phase = 0;
            for (int k = 0; k < len && start + k < buf.Length; k++)
            {
                float tt = k / (float)Rate;
                float f = f0 * (1f + 0.8f * Mathf.Min(1f, tt / dur));
                phase += 2.0 * Math.PI * f / Rate;
                float env = Mathf.Min(1f, tt / 0.002f) * Mathf.Exp(-tt / (dur * 0.35f));
                buf[start + k] += (float)Math.Sin(phase) * env * amp;
            }
            t += Exp(rng, r.bubbleRate);
        }
    }

    /// <summary>생물 빛 — 맑고 길게 사라지는 '띵'. 5음 음계에서 골라 서로 부딪히지 않는다.</summary>
    static void AddChimes(float[] buf, System.Random rng, Recipe r)
    {
        float[] notes = { 1046.5f, 1174.7f, 1318.5f, 1568.0f, 1760.0f, 2093.0f };
        double t = 0.6 + Exp(rng, r.chimeRate);
        while (true)
        {
            int start = (int)(t * Rate);
            if (start >= buf.Length) break;
            float f = notes[rng.Next(notes.Length)];
            float amp = r.chimeGain * (0.5f + 0.5f * (float)rng.NextDouble());
            int len = (int)(2.2f * Rate);
            for (int k = 0; k < len && start + k < buf.Length; k++)
            {
                float tt = k / (float)Rate;
                float env = (1f - Mathf.Exp(-tt / 0.004f)) * Mathf.Exp(-tt / 0.55f);
                float tone = Mathf.Sin(2f * Mathf.PI * f * tt) + 0.35f * Mathf.Exp(-tt / 0.2f) * Mathf.Sin(2f * Mathf.PI * f * 2.76f * tt);
                buf[start + k] += tone * env * amp;
            }
            t += Exp(rng, r.chimeRate);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 물보라 — 로비 창밖에서 물고기가 물에 들고 날 때 (SeaJumpShow)
    // ─────────────────────────────────────────────────────────────
    //   Small (1.1초): 가벼운 '첨벙' + 물방울 18개 — 돌고래·가다랑어
    //   Big   (1.8초): 묵직한 '첨벙' + 오래 남는 물소리 + 물방울 40개 — 쥐가오리

    public const string LobbyDir = "Assets/DomeAquarium/Audio/Lobby";

    public static AudioClip EnsureSplash(string id, bool force = false)
    {
        string path = $"{LobbyDir}/Splash_{id}.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(LobbyDir);
            File.WriteAllBytes(path, ToWav(SynthesizeSplash(id == "Big")));   // 덮어써도 .meta(GUID)는 그대로 — 장면 연결이 끊기지 않는다
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is AudioImporter imp && imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;     // 짧은 효과음 — 바로 재생되게
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static float[] SynthesizeSplash(bool big)
    {
        float seconds = big ? 1.8f : 1.1f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        var rng = new System.Random(big ? 7 : 3);

        // v1 은 3ms 만에 솟는 넓은 대역 잡음이라 멀리서 들으면 '탕' 하는 총소리처럼 들렸다.
        // 지금은 30ms 에 걸쳐 차오르는 낮은 물 덩어리('첨벙') + 그 뒤에 천천히 차오르는 쏴 소리 — 날카로운 처음이 없다.
        float aBody = OnePole(big ? 900f : 1300f), aWash = OnePole(big ? 1800f : 2200f), aLow = OnePole(90f);
        float b1 = 0f, b2 = 0f, w1 = 0f, w2 = 0f, low = 0f;
        float bodyDecay = big ? 0.35f : 0.2f, washDecay = big ? 0.55f : 0.32f;
        double thump = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            b1 += aBody * (white - b1); b2 += aBody * (b1 - b2);
            w1 += aWash * (white - w1); w2 += aWash * (w1 - w2);
            float body = b2 * Mathf.SmoothStep(0f, 1f, t / 0.03f) * Mathf.Exp(-t / bodyDecay) * 2.2f;
            float wash = w2 * Mathf.SmoothStep(0f, 1f, t / 0.08f) * Mathf.Exp(-t / washDecay) * 0.55f;
            // 물이 몸을 받아 내는 낮은 '퐁' — 180Hz 에서 90Hz 로 내려가며 사라진다
            thump += 2.0 * Math.PI * Mathf.Lerp(big ? 160f : 220f, big ? 80f : 120f, Mathf.Clamp01(t / 0.2f)) / Rate;
            float plunk = (float)Math.Sin(thump) * Mathf.SmoothStep(0f, 1f, t / 0.025f) * Mathf.Exp(-t / 0.12f) * (big ? 0.35f : 0.22f);
            float sample = body + wash + plunk;
            low += aLow * (sample - low);
            buf[i] = sample - low;
        }

        // 물방울 — 위로 짧게 올라가는 사인파, 뒤로 갈수록 작아진다
        int drops = big ? 40 : 18;
        float spread = big ? 1.2f : 0.7f;
        for (int d = 0; d < drops; d++)
        {
            float t0 = 0.05f + (float)rng.NextDouble() * spread;
            int start = (int)(t0 * Rate);
            float dur = 0.015f + (float)rng.NextDouble() * 0.025f;
            float f0 = 1200f + (float)rng.NextDouble() * (big ? 2300f : 2800f);
            float amp = 0.07f * (0.4f + 0.6f * (float)rng.NextDouble()) * Mathf.Exp(-t0 / (spread * 0.6f));
            int len = (int)(dur * 3f * Rate);
            double phase = 0;
            for (int k = 0; k < len && start + k < n; k++)
            {
                float tt = k / (float)Rate;
                phase += 2.0 * Math.PI * f0 * (1f + 0.6f * Mathf.Min(1f, tt / dur)) / Rate;
                buf[start + k] += (float)Math.Sin(phase) * Mathf.Min(1f, tt / 0.002f) * Mathf.Exp(-tt / (dur * 0.4f)) * amp;
            }
        }

        // 끝 50ms 는 0 으로 — 잘린 소리가 '틱' 하지 않게
        int tail = (int)(0.05f * Rate);
        for (int i = 0; i < tail; i++) buf[n - 1 - i] *= i / (float)tail;

        float peak = 0f;
        foreach (float s in buf) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak > 1e-4f) for (int i = 0; i < n; i++) buf[i] *= 0.7f / peak;
        return buf;
    }

    // ─────────────────────────────────────────────────────────────
    // 큰 생물 예고 — 부드러운 소나 핑 (2막)
    // ─────────────────────────────────────────────────────────────

    public const string DiveDir = "Assets/DomeAquarium/Audio/Dive";

    /// <summary>
    /// "띵—" 하고 퍼지는 낮은 핑 한 번과 두 번의 메아리. 경보처럼 들리지 않게 960Hz 사인에 8ms 로 부드럽게 시작하고,
    /// 0.42초·0.84초 뒤 메아리는 고역을 깎아 멀리서 되돌아오는 소리처럼 만든다. 3D 로 생물이 올 쪽에서 울린다.
    /// </summary>
    public static AudioClip EnsureSonarPing(bool force = false)
    {
        string path = $"{DiveDir}/Sonar_Ping.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(DiveDir);
            File.WriteAllBytes(path, ToWav(SynthesizePing()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is AudioImporter imp && imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    /// <summary>
    /// 엔딩 — 밤 로비 데이터 월에 새 줄이 새겨질 때 한 번. 맑은 두 음(도·솔, 1,047·1,568Hz)이 0.12초 어긋나
    /// 부드럽게 울리고 1.6초에 걸쳐 사라진다. 경보가 아니라 "기록되었다"는 작은 종소리.
    /// </summary>
    public static AudioClip EnsureChime(bool force = false)
    {
        string path = $"{DiveDir}/Record_Chime.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(DiveDir);
            const float seconds = 2.2f;
            int n = (int)(seconds * Rate);
            var buf = new float[n];
            foreach (var (f, delay, gain) in new[] { (1046.5f, 0f, 0.55f), (1568f, 0.12f, 0.4f), (2093f, 0.12f, 0.08f) })
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate - delay;
                    if (t < 0f) continue;
                    float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t / 0.55f);
                    buf[i] += gain * env * Mathf.Sin(2f * Mathf.PI * f * t);
                }
            float peak = 0f;
            foreach (float v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 1e-4f) for (int i = 0; i < n; i++) buf[i] *= 0.5f / peak;
            File.WriteAllBytes(path, ToWav(buf));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is AudioImporter imp && imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static float[] SynthesizePing()
    {
        const float seconds = 2.2f;
        int n = (int)(seconds * Rate);
        var dry = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float attack = Mathf.Clamp01(t / 0.008f);
            float f = Mathf.Lerp(960f, 900f, Mathf.Clamp01(t / 0.6f));          // 아주 살짝 내려가며 퍼진다
            float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / 0.32f);
            float shimmer = 0.12f * Mathf.Sin(2f * Mathf.PI * f * 2.01f * t) * Mathf.Exp(-t / 0.12f);
            dry[i] = attack * (body + shimmer);
        }

        var buf = new float[n];
        float a = OnePole(1400f), lp1 = 0f, lp2 = 0f;
        int e1 = (int)(0.42f * Rate), e2 = (int)(0.84f * Rate);
        for (int i = 0; i < n; i++)
        {
            float echo1 = i >= e1 ? dry[i - e1] : 0f;
            float echo2 = i >= e2 ? dry[i - e2] : 0f;
            lp1 += a * (echo1 * 0.32f - lp1);                                  // 메아리는 둔하게
            lp2 += a * (echo2 * 0.12f - lp2);
            buf[i] = dry[i] + lp1 + lp2;
        }

        float peak = 0f;
        foreach (float v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
        int fade = (int)(0.3f * Rate);
        for (int i = 0; i < n; i++)
        {
            if (peak > 1e-4f) buf[i] *= 0.6f / peak;
            if (i > n - fade) buf[i] *= (n - i) / (float)fade;
        }
        return buf;
    }

    static double Exp(System.Random rng, float rate) => -Math.Log(1.0 - rng.NextDouble()) / rate;

    static float OnePole(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);

    static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataBytes);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // 모노
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataBytes);
        foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        w.Flush();
        return ms.ToArray();
    }
}
#endif
