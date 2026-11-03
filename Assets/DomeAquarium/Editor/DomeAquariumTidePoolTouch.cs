#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// 조수 웅덩이에 손 넣기(TidePoolTouch) — 손이 물에 들면 물결 고리 · "찰방" · 손 떨림, 게는 바위 밑으로 숨는다.
/// 물결은 수평 빌보드 파티클(고리 텍스처)을 손 자리에서 하나씩 내보내 크게 번지며 흐려지게.
/// 순서: 24(해변) · 25(NPC) 뒤. 9(로비) 안에서도 돈다. 따로 다시 세울 때는 33.
/// </summary>
public static class DomeAquariumTidePoolTouch
{
    const string MatDir = "Assets/DomeAquarium/Materials";
    const string RingPath = MatDir + "/RippleRing.png";

    [MenuItem("DomeAquarium/33. 조수 웅덩이에 손 넣기", priority = 33)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[PoolTouch] 플레이 모드에서는 세울 수 없다."); return; }
        Build();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("DomeAquarium/디버그/플레이 중 - 해변: 웅덩이에 손 넣기", priority = 214)]
    public static void DebugDip()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[PoolTouch] 플레이 모드에서만 쓸 수 있다."); return; }
        var touch = Object.FindFirstObjectByType<TidePoolTouch>();
        if (touch == null) return;
        // 게가 있는 쪽에 — 가장 가까운 게 옆
        Vector2 at = Vector2.zero;
        if (touch.life != null)
            for (int i = 0; i < touch.life.AgentCount; i++)
                if (touch.life.TryGetAgent(i, out Vector3 p, out FishData f) && f.speciesId == "Crab")
                {
                    Vector3 l = touch.pool.InverseTransformPoint(p);
                    at = new Vector2(l.x, l.z) * 0.8f;
                    break;
                }
        touch.DebugDip(at);
        Debug.Log($"[PoolTouch] 웅덩이 로컬 {at} 에 손을 넣었다.");
    }

    public static void Build()
    {
        var tank = Object.FindObjectsByType<PracticeTank>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.transform.parent != null && t.transform.parent.name == "TidePool");
        if (tank == null) { Debug.LogWarning("[PoolTouch] 조수 웅덩이(TidePool/Life)가 없다 — 24번 메뉴를 먼저."); return; }
        Transform pool = tank.transform.parent;

        var old = pool.Find("Touch");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("Touch");
        go.transform.SetParent(pool, false);
        var touch = go.AddComponent<TidePoolTouch>();
        touch.pool = pool;
        touch.radius = DomeAquariumBeachBuilder.PoolInnerRadius;
        touch.waterY = DomeAquariumBeachBuilder.PoolWater;
        touch.bedY = DomeAquariumBeachBuilder.PoolBed;
        touch.life = tank;
        // 게가 숨는 자리 — 안쪽 벽(수조 반 크기 × 1.55) 바로 안, 바닥보다 5cm 낮게: 몸 절반이 바위 턱 속으로 들어가 "바위 밑에 숨은" 것처럼
        tank.hideReach = 1.5f;
        tank.hideSink = 0.05f;
        EditorUtility.SetDirty(tank);

        var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
        Transform offset = origin != null ? origin.transform.Find("Camera Offset") : null;
        var hands = offset == null ? new Transform[0]
            : new[] { "Right Controller", "Left Controller", "Right Hand", "Left Hand" }.Select(n => offset.Find(n)).Where(t => t != null).ToArray();
        touch.hands = hands;
        touch.haptics = hands.Select(h => h.GetComponent<HapticImpulsePlayer>()).ToArray();
        if (hands.Length == 0) Debug.LogWarning("[PoolTouch] XR Origin 의 손(컨트롤러)을 찾지 못했다.");

        touch.nolant = Object.FindObjectsByType<NpcDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.speaker == "놀란");

        // 물결 — 손 자리에서 하나씩 내보내는 수평 고리
        var rgo = new GameObject("Ripples");
        rgo.transform.SetParent(go.transform, false);
        var ps = rgo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startSpeed = 0f;
        main.startLifetime = 1.2f;
        main.startSize = 0.06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(6f, new AnimationCurve(new Keyframe(0f, 1f / 6f, 0f, 2.2f), new Keyframe(1f, 1f, 0.3f, 0f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_PoolRipple", "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", EnsureRingTexture());
        mat.SetColor("_BaseColor", new Color(0.85f, 0.95f, 1f, 1f));
        EditorUtility.SetDirty(mat);
        var pr = rgo.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        pr.sharedMaterial = mat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        touch.ripples = ps;

        // 찰방
        var sgo = new GameObject("Dip");
        sgo.transform.SetParent(go.transform, false);
        sgo.transform.localPosition = new Vector3(0f, DomeAquariumBeachBuilder.PoolWater, 0f);
        var src = sgo.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.minDistance = 0.6f;
        src.maxDistance = 8f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.volume = 0.8f;
        touch.sound = src;
        touch.dip = DomeAquariumDepthSounds.EnsureWaterDip();

        EditorUtility.SetDirty(touch);
        Debug.Log($"[PoolTouch] 조수 웅덩이 손 넣기 — 손 {hands.Length}개, 놀란 {(touch.nolant != null ? "있음" : "없음")}.");
    }

    /// <summary>물결 고리 — 가는 밝은 고리와 안쪽으로 번지는 옅은 띠. 애디티브라 RGB 에도 같은 감쇠.</summary>
    static Texture2D EnsureRingTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(RingPath);
        if (existing != null) return existing;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.05f, 2f));
                float inner = 0.35f * Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.08f, 2f));
                float a = Mathf.Clamp01(ring + inner) * Mathf.Clamp01((1f - d) / 0.06f);
                px[y * size + x] = new Color(a, a, a, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(Path.GetFullPath(RingPath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RingPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(RingPath) is TextureImporter imp)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RingPath);
    }
}
#endif
