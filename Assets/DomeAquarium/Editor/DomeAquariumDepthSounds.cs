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

    /// <summary>
    /// 먼바다 배의 뱃고동 — 낮은 두 음(G2 98Hz · D3 147Hz)을 겹친 3초 남짓의 긴 한 번.
    /// 배음 많은 톱니파를 낮게 거르고(먼 소리는 높은 소리가 먼저 사라진다) 곶에 부딪혀 돌아오는 메아리 두 번을 얹는다.
    /// ShipHorn 이 3D 소리로 아주 작게 튼다.
    /// </summary>
    public static AudioClip EnsureShipHorn(bool force = false)
    {
        string path = $"{LobbyDir}/ShipHorn.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(LobbyDir);
            File.WriteAllBytes(path, ToWav(SynthesizeHorn()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is AudioImporter imp && imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static float[] SynthesizeHorn()
    {
        const float seconds = 6f, blast = 3.2f;
        int n = (int)(seconds * Rate);
        var dry = new float[n];
        float[] tones = { 98f, 146.8f };
        var phase = new double[tones.Length];
        float lp1 = 0f, lp2 = 0f, k = OnePole(650f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = t < blast ? Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((blast - t) / 0.6f) : 0f;
            float vib = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 4.5f * t);
            float s = 0f;
            for (int j = 0; j < tones.Length; j++)
            {
                phase[j] += 2.0 * Math.PI * tones[j] * vib / Rate;
                for (int h = 1; h <= 8; h++) s += Mathf.Sin((float)(phase[j] * h)) / h;   // 톱니파 배음
            }
            lp1 += (s * env - lp1) * k;
            lp2 += (lp1 - lp2) * k;
            dry[i] = lp2;
        }
        var buf = new float[n];
        int e1 = (int)(0.55f * Rate), e2 = (int)(1.3f * Rate);
        float peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float s = dry[i] + (i >= e1 ? 0.28f * dry[i - e1] : 0f) + (i >= e2 ? 0.15f * dry[i - e2] : 0f);
            buf[i] = s;
            peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.8f / peak;
        return buf;
    }

    /// <summary>혹등고래 숨소리 — "푸우". 넓은 잡음이 확 터졌다가 낮아지며 잦아들고, 그 밑에 낮은 울림이 한 번.</summary>
    public static AudioClip EnsureWhaleBlow(bool force = false) => EnsureEffect("WhaleBlow", SynthesizeBlow, force);

    /// <summary>
    /// 200m 수중 마이크 — 대왕고래 노래를 10배 빠르게. 실제 노래는 15~20Hz 라 사람 귀에 들리지 않는다(녹음도 빠르게 돌려 듣는다).
    /// 북태평양 대왕고래처럼 A(웅웅 떨리는 소리) → B(길게 내려가는 낮은 신음, 셋째 배음이 가장 크다)를 두 번. 물속 메아리와 낮은 물소리를 깐다. 14초.
    /// </summary>
    public static AudioClip EnsureBlueWhaleSong(bool force = false) => EnsureEffect("Hydrophone_BlueWhale", SynthesizeBlueWhale, force);

    /// <summary>
    /// 1,000m 수중 마이크 — 향유고래 딸깍 소리. 날카로운 딸깍(한 번에 4ms 간격 메아리 셋)이 0.6~0.9초마다 이어지다가
    /// 먹이를 찾으면 점점 빨라져 "드르륵"(크릭)이 된다. 12초.
    /// </summary>
    public static AudioClip EnsureSpermWhaleClicks(bool force = false) => EnsureEffect("Hydrophone_SpermWhale", SynthesizeSpermWhale, force);

    /// <summary>
    /// 밤바다 연구선 엔진 — 2초 고리. 낮은 "통통통" (38Hz 와 배음, 초당 8번 털털거림)에 거른 잡음.
    /// 주파수가 모두 2초에 딱 맞아떨어지게 골라 고리 이음매가 튀지 않고, 잡음은 끝 0.2초를 처음과 겹쳐 잇는다.
    /// </summary>
    public static AudioClip EnsureEngineIdle(bool force = false) => EnsureEffect("BoatEngineIdle", SynthesizeEngine, force);

    static float[] SynthesizeEngine()
    {
        const float loop = 2f, over = 0.2f;
        int n = (int)(loop * Rate), o = (int)(over * Rate);
        var raw = new float[n + o];
        var rng = new System.Random(3838);
        float lp = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            float chug = 0.55f + 0.45f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 8f * t), 3f);
            float tone = Mathf.Sin(2f * Mathf.PI * 38f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 76f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 114f * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * OnePole(300f);
            raw[i] = (tone * 0.45f + lp * 1.2f) * chug;
        }
        var buf = new float[n];
        float peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            buf[i] = raw[i];
            if (i < o) { float k = i / (float)o; buf[i] = raw[i] * k + raw[n + i] * (1f - k); }
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.6f / peak;
        return buf;
    }

    /// <summary>바시스피어 전화 — 옛날 전화벨 "따르릉" 한 번 (두 음이 초당 20번 떨리며 0.8초).</summary>
    public static AudioClip EnsurePhoneRing(bool force = false) => EnsureEffect("PhoneRing1934", SynthesizeRing, force);

    static float[] SynthesizeRing()
    {
        const float seconds = 0.9f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        float peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float gate = Mathf.Sin(2f * Mathf.PI * 20f * t) > 0f ? 1f : 0.25f;   // 종을 때리는 망치가 떨린다
            float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((0.8f - t) / 0.08f);
            buf[i] = (Mathf.Sin(2f * Mathf.PI * 1100f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 1370f * t)) * gate * env;
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.45f / peak;
        return buf;
    }

    /// <summary>쌍안경 꼬리 사진 — 카메라 셔터 "찰칵" (짧은 딸깍 두 번).</summary>
    public static AudioClip EnsureShutter(bool force = false) => EnsureEffect("Shutter", SynthesizeShutter, force);

    static float[] SynthesizeBlueWhale()
    {
        const float seconds = 14f;
        int n = (int)(seconds * Rate);
        var dry = new float[n];
        var rng = new System.Random(1515);
        // (시작 초, 종류, 길이) — A: 떨림, B: 내려가는 신음
        var calls = new[] { (0.4f, 'A', 1.7f), (2.6f, 'B', 2.4f), (6.2f, 'A', 1.6f), (8.3f, 'B', 2.6f) };
        foreach (var (start, kind, len) in calls)
        {
            int i0 = (int)(start * Rate), i1 = Mathf.Min(n, (int)((start + len) * Rate));
            float phase = 0f;
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0) / (float)Rate, k = t / len;
                float env = Mathf.Clamp01(t / 0.18f) * Mathf.Clamp01((len - t) / 0.45f);
                float s;
                if (kind == 'A')
                {
                    float f = 330f + 6f * Mathf.Sin(2f * Mathf.PI * 0.7f * t);
                    phase += 2f * Mathf.PI * f / Rate;
                    float pulse = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 17f * t);                 // 웅웅 떨린다
                    s = (Mathf.Sin(phase) + 0.4f * Mathf.Sin(2f * phase) + 0.2f * Mathf.Sin(3f * phase)) * pulse * 0.5f;
                }
                else
                {
                    float f = Mathf.Lerp(164f, 148f, k) * (1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 3f * t));
                    phase += 2f * Mathf.PI * f / Rate;
                    s = 0.45f * Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase) + 0.9f * Mathf.Sin(3f * phase) + 0.15f * Mathf.Sin(4f * phase);
                    s *= 0.5f;
                }
                dry[i] += s * env;
            }
        }
        // 물속 메아리 + 낮은 물소리
        var buf = new float[n];
        int[] taps = { (int)(0.11f * Rate), (int)(0.23f * Rate), (int)(0.41f * Rate), (int)(0.67f * Rate) };
        float[] gains = { 0.35f, 0.22f, 0.13f, 0.07f };
        float lp = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float s = dry[i];
            for (int j = 0; j < taps.Length; j++) if (i >= taps[j]) s += dry[i - taps[j]] * gains[j];
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * OnePole(260f);
            buf[i] = s + lp * 0.35f;
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        float fade = 0.6f * Rate;
        for (int i = 0; i < n; i++)
        {
            float g = Mathf.Clamp01(i / fade) * Mathf.Clamp01((n - i) / fade);
            buf[i] *= 0.75f / peak * g;
        }
        return buf;
    }

    static float[] SynthesizeSpermWhale()
    {
        const float seconds = 12f;
        int n = (int)(seconds * Rate);
        var dry = new float[n];
        var rng = new System.Random(808);
        // 딸깍 시각 — 0.6~0.9초 간격으로 8초, 그다음 점점 빨라지는 크릭 2.4초
        var times = new System.Collections.Generic.List<float>();
        for (float t = 0.5f; t < 8.5f; t += 0.6f + (float)rng.NextDouble() * 0.3f) times.Add(t);
        for (float t = 8.9f, gap = 0.12f; t < 11.3f; t += gap, gap = Mathf.Max(0.022f, gap * 0.9f)) times.Add(t);
        foreach (float at in times)
        {
            float amp = at > 8.8f ? 0.55f : 0.9f + (float)rng.NextDouble() * 0.1f;
            for (int p = 0; p < 3; p++)                                        // 머릿속에서 되울리는 메아리 셋 (4ms 간격)
            {
                int i0 = (int)((at + p * 0.004f) * Rate);
                float a = amp * (p == 0 ? 1f : p == 1 ? 0.45f : 0.2f);
                for (int i = i0; i < Mathf.Min(n, i0 + (int)(0.004f * Rate)); i++)
                {
                    float t = (i - i0) / (float)Rate;
                    dry[i] += a * (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t / 0.0005f);
                }
            }
        }
        var buf = new float[n];
        int echo = (int)(0.018f * Rate);
        float lpLow = 0f, lpHi = 0f, rumble = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float x = dry[i] + (i >= echo ? dry[i - echo] * 0.25f : 0f);
            lpHi += (x - lpHi) * OnePole(6000f);                                // 너무 쨍하지 않게
            lpLow += (lpHi - lpLow) * OnePole(900f);
            float band = lpHi - lpLow * 0.7f;                                   // 낮은 쪽을 덜어 "딸깍"을 또렷하게
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            rumble += (noise - rumble) * OnePole(180f);
            buf[i] = band + rumble * 0.25f;
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.8f / peak;
        return buf;
    }

    static float[] SynthesizeShutter()
    {
        const float seconds = 0.25f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        var rng = new System.Random(77);
        float lp = 0f, peak = 1e-4f;
        foreach (float at in new[] { 0f, 0.075f })
        {
            int i0 = (int)(at * Rate);
            for (int i = i0; i < n; i++)
            {
                float t = (i - i0) / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * OnePole(3500f);
                buf[i] += (lp * 1.2f + 0.4f * Mathf.Sin(2f * Mathf.PI * 1800f * t)) * Mathf.Exp(-t / 0.012f);
            }
        }
        foreach (float v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
        for (int i = 0; i < n; i++) buf[i] *= 0.7f / peak;
        return buf;
    }

    /// <summary>조수 웅덩이에 손을 넣는 소리 — 얕은 물 "찰방". 물을 가르는 짧은 잡음 위에 공기 방울 "뽁" 두 개(올라가는 사인)와 물방울 몇 개.</summary>
    public static AudioClip EnsureWaterDip(bool force = false) => EnsureEffect("WaterDip", SynthesizeWaterDip, force);

    static float[] SynthesizeWaterDip()
    {
        const float seconds = 0.7f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        var rng = new System.Random(2626);
        float lpA = 0f, lpB = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpA += (noise - lpA) * OnePole(3200f);
            lpB += (lpA - lpB) * OnePole(500f);
            buf[i] = (lpA - lpB) * Mathf.Exp(-t * 18f) * Mathf.Clamp01(t / 0.004f) * 0.9f;     // 물을 가르는 "촤"
        }
        // 공기 방울 — 짧게 올라가는 사인 (크기가 작을수록 높다)
        foreach (var (at, f0, len, amp) in new[] { (0.03f, 520f, 0.07f, 0.8f), (0.09f, 760f, 0.05f, 0.55f), (0.22f, 1150f, 0.035f, 0.35f), (0.31f, 900f, 0.04f, 0.3f), (0.44f, 1400f, 0.03f, 0.22f) })
        {
            int i0 = (int)(at * Rate), i1 = Mathf.Min(n, i0 + (int)(len * Rate));
            float phase = 0f;
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0) / (float)Rate, k = t / len;
                phase += 2f * Mathf.PI * f0 * (1f + 0.9f * k) / Rate;
                buf[i] += amp * Mathf.Sin(phase) * Mathf.Exp(-k * 3.5f) * Mathf.Clamp01(t / 0.002f);
            }
        }
        foreach (float v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
        for (int i = 0; i < n; i++) buf[i] *= 0.7f / peak;
        return buf;
    }

    /// <summary>해변 수거함에 쓰레기가 들어가는 소리 — 속 빈 통에 "통". 낮은 울림 두 개와 짧은 부딪힘 잡음.</summary>
    public static AudioClip EnsureBinDrop(bool force = false) => EnsureEffect("BinDrop", SynthesizeBinDrop, force);

    static float[] SynthesizeBinDrop()
    {
        const float seconds = 0.6f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        var rng = new System.Random(4242);
        float lp = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * OnePole(2400f);
            float knock = lp * Mathf.Exp(-t * 60f);                                         // 부딪히는 순간
            float body = (Mathf.Sin(2f * Mathf.PI * 190f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 310f * t)) * Mathf.Exp(-t * 11f);
            buf[i] = (knock * 0.9f + body * 0.7f) * Mathf.Clamp01(t / 0.002f);
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.7f / peak;
        return buf;
    }

    /// <summary>
    /// 테라스 부두 벽에 부딪히는 파도 — 12초 고리(끝 1.5초를 처음과 겹침). 낮게 거른 물소리가 4초 남짓 간격으로 밀려왔다 빠지고,
    /// 밀려올 때마다 벽에 부딪히는 철썩 소리. 부두 벽 앞 3D 소리라 테라스에 나가야 크게 들린다.
    /// </summary>
    public static AudioClip EnsureSurfLoop(bool force = false)
    {
        string path = $"{LobbyDir}/TerraceSurf.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(LobbyDir);
            File.WriteAllBytes(path, ToWav(SynthesizeSurf()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        Configure(path);   // 12초 고리 — 수심 벽 물소리와 같은 설정
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static AudioClip EnsureEffect(string id, Func<float[]> synth, bool force)
    {
        string path = $"{LobbyDir}/{id}.wav";
        if (force || !File.Exists(path))
        {
            Directory.CreateDirectory(LobbyDir);
            File.WriteAllBytes(path, ToWav(synth()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is AudioImporter imp && imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static float[] SynthesizeBlow()
    {
        const float seconds = 2.2f;
        int n = (int)(seconds * Rate);
        var buf = new float[n];
        var rng = new System.Random(731);
        float lp = 0f, hp = 0f, prev = 0f, peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-t * 1.9f);
            float cutoff = Mathf.Lerp(1600f, 380f, Mathf.Clamp01(t / 1.4f));   // 확 터졌다가 낮아진다
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * OnePole(cutoff);
            hp = lp - prev * 0.97f;                                             // 웅웅거림을 덜어 숨결처럼
            prev = lp;
            float thump = Mathf.Sin(2f * Mathf.PI * 68f * t) * Mathf.Exp(-t * 7f) * 0.6f;
            buf[i] = hp * env * 1.4f + thump * Mathf.Clamp01(t / 0.02f);
            peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
        }
        for (int i = 0; i < n; i++) buf[i] *= 0.8f / peak;
        return buf;
    }

    static float[] SynthesizeSurf()
    {
        int loop = (int)(Seconds * Rate), over = (int)(Overlap * Rate), n = loop + over;
        var raw = new float[n];
        var rng = new System.Random(5150);
        float lp = 0f, lp2 = 0f;
        // 4.2초 남짓 간격으로 밀려온다 — 철썩 시각을 미리 뽑아 둔다
        var hits = new System.Collections.Generic.List<float>();
        for (float h = 0.8f; h < Seconds + Overlap; h += 3.6f + (float)rng.NextDouble() * 1.4f) hits.Add(h);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * OnePole(700f);
            lp2 += (noise - lp2) * OnePole(2400f);
            float wash = 0.35f, crash = 0f;
            foreach (float h in hits)
            {
                float d = t - h;
                if (d < -1.6f || d > 2.5f) continue;
                wash += d < 0f ? 0.65f * Mathf.SmoothStep(0f, 1f, (d + 1.6f) / 1.6f)   // 밀려오며 커지고
                                : 0.65f * Mathf.Exp(-d * 1.4f);                       // 빠지며 잦아든다
                if (d >= 0f) crash += Mathf.Exp(-d * 9f);                              // 벽에 부딪히는 순간
            }
            raw[i] = lp * wash * 1.6f + lp2 * crash * 0.9f;
        }
        // 끝 Overlap 을 처음과 겹쳐 이음매 없는 고리로
        var buf = new float[loop];
        for (int i = 0; i < loop; i++) buf[i] = raw[i];
        for (int i = 0; i < over; i++)
        {
            float k = i / (float)over;
            buf[i] = raw[i] * k + raw[loop + i] * (1f - k);
        }
        float peak = 1e-4f;
        foreach (float s in buf) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < loop; i++) buf[i] *= 0.7f / peak;
        return buf;
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
