#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 관측소 남쪽 외벽 벽화 — 관측소 안에서 만날 바다를 해변에서 미리 보여 주는 그림 한 장. 외벽 전체(12.4 × 4.5m)를 채운다.
/// 게임 속 모델(혹등고래 · 쥐가오리 · 바다거북 · 산호초 물고기 · 산호)을 씬에서 멀리 떨어진 빈 곳에 잠깐 세우고,
/// 위에서 내리는 햇빛 · 푸른 안개 · 빛줄기로 물속처럼 찍어 PNG 로 저장한 뒤 무대를 치운다. 난수 씨앗이 고정이라 다시 찍어도 같은 그림.
/// 벽에 거는 일은 DomeAquariumBeachBuilder.BuildMural 이 한다 (그림이 없으면 이 클래스로 먼저 찍는다).
/// </summary>
public static class DomeAquariumMuralBuilder
{
    internal static readonly Vector2 WallSize = new Vector2(12.4f, 4.5f);          // 남쪽 외벽(WallBack) 전체 — 바닥 높이부터 지붕 처마 밑까지
    const string ArtPath = "Assets/DomeAquarium/Coast/ObservatoryMural.png";
    const string FishDir = "Assets/DomeAquarium/FishData/";
    const string CreatureDir = "Assets/DomeAquarium/Creatures/Prefabs/";
    const int Width = 4096, Height = 1488;                     // 벽과 같은 비율 (12.4 : 4.5)
    const int StageLayer = 31;                                  // 무대만 찍는다
    static readonly Vector3 StageOrigin = new Vector3(0f, -3000f, 0f);

    // 카메라는 +z 를 본다. 주 평면(z 30)에서 너비 28.8m 가 그림에 들고, 높이는 벽 비율을 따른다(약 10.5m).
    // 배역 자리(Cast)는 높이 7.2m 틀(위아래 ±3.6)로 적어 두고 찍을 때 높이 비율만큼 늘린다 — 바닥 띠는 아래, 고래 · 돌고래는 위로 퍼진다.
    const float MainDepth = 30f, MainHalfWidth = 14.4f, DesignHalfHeight = 3.6f;
    static readonly float MainHalfHeight = MainHalfWidth * Height / Width;
    static readonly float Stretch = MainHalfHeight / DesignHalfHeight;
    static readonly Color FogColor = new Color(0.07f, 0.30f, 0.44f);

    /// <summary>한 마리 또는 한 무리. at = 무대 기준 (x, y, z), yaw 0 = +x(오른쪽)로 헤엄. length = 몸길이(가오리는 날개 폭).</summary>
    readonly struct Pose
    {
        public readonly string source;                          // FishData 이름 또는 "creature:프리팹 이름"
        public readonly Vector3 at;
        public readonly float yaw, pitch, roll, length;
        public readonly int count;
        public readonly Vector3 spread;
        public Pose(string source, Vector3 at, float yaw, float pitch, float length, int count = 1, Vector3 spread = default, float roll = 0f)
        {
            this.source = source; this.at = at; this.yaw = yaw; this.pitch = pitch; this.length = length;
            this.count = count; this.spread = spread; this.roll = roll;
        }
    }

    static readonly Pose[] Cast =
    {
        // 수면 가까이 — 돌고래 · 가다랑어 떼
        new Pose("TucuxiDophin", new Vector3(5.4f, 3.0f, 36f), 196f, 4f, 2.3f, 2, new Vector3(1.6f, 0.35f, 1.5f)),
        new Pose("SkipjackTuna", new Vector3(-9.5f, 2.9f, 38f), 6f, 0f, 0.9f, 9, new Vector3(2.4f, 0.5f, 2.5f)),
        // 멀리 — 안개 속 실루엣
        new Pose("SpottedEagleRay", new Vector3(-13.5f, 2.2f, 48f), 8f, -4f, 3.2f, roll: 6f),
        new Pose("BlacktipReefShark", new Vector3(13f, 0.4f, 46f), 180f, 2f, 3.0f),
        // 주인공 — 혹등고래가 위쪽을 가로지르고, 오른쪽 위에 쥐가오리
        new Pose("creature:HumpbackWhale", new Vector3(-3.5f, 1.4f, 38f), 4f, -7f, 13f),
        new Pose("MantaRay", new Vector3(8.8f, 1.9f, 31f), 200f, -6f, 4.4f, roll: -10f),
        // 가운데 — 바다거북, 물고기 떼
        new Pose("GreenTurtle", new Vector3(-9.2f, -0.4f, 27f), 20f, -12f, 1.9f),
        new Pose("BlueTang", new Vector3(3.2f, -0.2f, 27f), 192f, 0f, 0.55f, 14, new Vector3(3.2f, 1.1f, 2.5f)),
        new Pose("YellowTang", new Vector3(-2.6f, -1.3f, 24f), 12f, 0f, 0.48f, 11, new Vector3(2.6f, 0.8f, 2f)),
        new Pose("BarrierReefChromis", new Vector3(10.4f, -0.9f, 26f), 170f, 0f, 0.32f, 16, new Vector3(2.2f, 1f, 2f)),
        // 산호 가까이
        new Pose("MoorishIdol", new Vector3(5.6f, -2.0f, 23f), 185f, 0f, 0.5f, 3, new Vector3(1.2f, 0.4f, 1f)),
        new Pose("QueenAngelfish", new Vector3(-5.6f, -2.1f, 22.5f), 15f, 0f, 0.55f, 2, new Vector3(1f, 0.3f, 0.6f)),
        new Pose("Clownfish", new Vector3(-0.8f, -2.5f, 21.5f), 160f, 0f, 0.32f, 3, new Vector3(0.7f, 0.25f, 0.5f)),
        new Pose("FlameAngelfish", new Vector3(8.6f, -2.4f, 22f), 200f, 0f, 0.36f, 2, new Vector3(0.8f, 0.3f, 0.5f)),
    };

    [MenuItem("DomeAquarium/25. 관측소 벽화 다시 그리기", priority = 25)]
    public static void RenderMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Mural] 플레이 모드에서는 그릴 수 없다."); return; }
        Render();
    }

    /// <summary>저장해 둔 그림을 불러오고, 없으면 지금 찍는다.</summary>
    internal static Texture2D LoadOrRender()
    {
        var art = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath);
        return art != null ? art : Render();
    }

    internal static Texture2D Render()
    {
        // 씬 조명 · 환경을 적어 두었다가 되돌린다 — 무대만 무대 빛으로 찍는다
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l.enabled).ToList();
        var env = (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
                   RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
        var stage = new GameObject("__MuralStage") { hideFlags = HideFlags.DontSave };
        stage.transform.position = StageOrigin;
        var temps = new List<Object>();
        RenderTexture rt = null;
        try
        {
            foreach (var l in lights) l.enabled = false;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 80f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.66f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.42f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.20f, 0.27f);
            DynamicGI.UpdateEnvironment();

            BuildBackdrop(stage.transform, temps);
            BuildFloor(stage.transform, temps);
            var rng = new System.Random(2026);
            BuildReef(stage.transform, rng);
            foreach (var pose in Cast) Place(stage.transform, pose, rng);
            BuildRays(stage.transform, temps);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(62f, -18f, 0f);
            sun.color = new Color(0.92f, 0.98f, 1f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.None;
            sun.cullingMask = 1 << StageLayer;

            foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StageLayer;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform, false);
            cam.cullingMask = 1 << StageLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.fieldOfView = 2f * Mathf.Atan(MainHalfHeight / MainDepth) * Mathf.Rad2Deg;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 140f;
            cam.aspect = Width / (float)Height;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.GetFullPath(ArtPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            Object.DestroyImmediate(stage);
            foreach (var o in temps) if (o != null) Object.DestroyImmediate(o);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            foreach (var l in lights) if (l != null) l.enabled = true;
            (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
             RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) = env;
            DynamicGI.UpdateEnvironment();
        }

        AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(ArtPath) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;                      // Quest — 12.4m 벽에 1m 당 165px
            importer.npotScale = TextureImporterNPOTScale.None;  // 2의 거듭제곱으로 늘리면 세로가 512 로 줄어 흐려진다 (2048 × 744 그대로)
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        Debug.Log($"[Mural] 관측소 벽화를 그렸다: {ArtPath} ({Width}×{Height})");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath);
    }

    // ─────────────────────────────────────────────────────────────
    // 무대
    // ─────────────────────────────────────────────────────────────

    /// <summary>뒤 배경 — 위는 햇빛 받은 옥색, 아래는 깊은 남색. 안개가 섞여도 위아래 차이가 남게 진하게.</summary>
    static void BuildBackdrop(Transform stage, List<Object> temps)
    {
        var tex = new Texture2D(4, 256, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
        var top = new Color(0.45f, 0.86f, 0.95f);
        var mid = new Color(0.08f, 0.40f, 0.60f);
        var bottom = new Color(0.01f, 0.08f, 0.20f);
        for (int y = 0; y < 256; y++)
        {
            float v = y / 255f;
            Color c = v > 0.55f ? Color.Lerp(mid, top, Mathf.SmoothStep(0f, 1f, (v - 0.55f) / 0.45f))
                                : Color.Lerp(bottom, mid, Mathf.SmoothStep(0f, 1f, v / 0.55f));
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        temps.Add(tex);
        temps.Add(mat);

        const float depth = 60f;
        float halfH = depth * MainHalfHeight / MainDepth;
        Quad("Backdrop", stage, new Vector3(0f, 0f, depth), new Vector2(halfH * 2f * 4.6f, halfH * 2.2f), mat);   // 쿼드 앞면이 -z — 카메라 쪽
    }

    /// <summary>모래 바닥 — 주 평면 아래 끝(y -3.6) 조금 아래에서 멀리까지. 물빛이 도는 모래색이라야 먼 곳이 물속으로 녹아든다
    /// (밝은 베이지는 수평선이 탁자 모서리처럼 또렷하게 그어졌다).</summary>
    static void BuildFloor(Transform stage, List<Object> temps)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
        mat.SetColor("_BaseColor", new Color(0.50f, 0.58f, 0.56f));
        temps.Add(mat);
        var floor = Quad("Sand", stage, new Vector3(0f, -MainHalfHeight + 0.2f, 45f), new Vector2(90f, 60f), mat);
        floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>산호 · 바위 · 해초를 바닥 띠에 흩어 놓는다 — 앞쪽은 크고 또렷하게, 뒤쪽은 안개 속으로.</summary>
    static void BuildReef(Transform stage, System.Random rng)
    {
        var corals = DomeAquariumSetup.LoadPropPrefabs("coral");
        var rocks = DomeAquariumSetup.LoadPropPrefabs("rock");
        var weeds = DomeAquariumSetup.LoadPropPrefabs("seaweed");
        float floorY = -MainHalfHeight + 0.2f;
        void Scatter(List<GameObject> source, int count, float zMin, float zMax, float hMin, float hMax)
        {
            if (source.Count == 0) return;
            for (int i = 0; i < count; i++)
            {
                float z = Mathf.Lerp(zMin, zMax, (float)rng.NextDouble());
                float halfW = z * MainHalfWidth / MainDepth + 1f;                  // 그 깊이에서 화면 반폭
                float x = Mathf.Lerp(-halfW, halfW, (float)rng.NextDouble());
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source[rng.Next(source.Count)]);
                go.transform.SetParent(stage, false);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.one;
                Bounds b = WorldBounds(go);
                float h = Mathf.Lerp(hMin, hMax, (float)rng.NextDouble()) * Mathf.Sqrt(Stretch);
                float s = b.size.y > 1e-3f ? h / b.size.y : 1f;
                go.transform.localScale = Vector3.one * s;
                b = WorldBounds(go);
                go.transform.position += stage.TransformPoint(new Vector3(x, floorY, z)) - new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            }
        }
        Scatter(rocks, 6, 26f, 44f, 0.5f, 1.0f);
        Scatter(corals, 46, 20f, 42f, 0.7f, 2.0f);
        Scatter(weeds, 18, 20f, 36f, 1.2f, 2.6f);
    }

    /// <summary>위에서 비스듬히 내리는 빛줄기 — 부드러운 띠 스프라이트를 옅게 겹친다.</summary>
    static void BuildRays(Transform stage, List<Object> temps)
    {
        const int w = 64, h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Abs(x / (w - 1f) * 2f - 1f);
                float v = y / (h - 1f);
                float a = Mathf.Pow(1f - u, 2f) * Mathf.SmoothStep(0f, 1f, v);     // 위가 진하고 아래로 사라진다
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 16f);
        temps.Add(tex);
        temps.Add(sprite);
        var rays = new (float x, float width, float alpha, float tilt)[]
        {
            (-11f, 2.6f, 0.10f, -14f), (-6.5f, 1.6f, 0.13f, -12f), (-2f, 3.2f, 0.08f, -15f),
            (2.5f, 1.4f, 0.12f, -11f), (6.5f, 2.4f, 0.10f, -13f), (11.5f, 1.8f, 0.11f, -12f),
        };
        foreach (var r in rays)
        {
            var go = new GameObject("Ray");
            go.transform.SetParent(stage, false);
            go.transform.localPosition = new Vector3(r.x, MainHalfHeight * 1.5f, 40f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, r.tilt);
            go.transform.localScale = new Vector3(r.width / (w / 16f), 9f * Stretch / (h / 16f), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(0.85f, 1f, 1f, r.alpha);
        }
    }

    static void Place(Transform stage, Pose pose, System.Random rng)
    {
        GameObject prefab = Prefab(pose.source);
        if (prefab == null) { Debug.LogWarning($"[Mural] {pose.source} 모델이 없어 건너뛴다."); return; }
        for (int i = 0; i < pose.count; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(stage, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            SamplePose(go, (float)rng.NextDouble());
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;   // 경계 상자가 어긋나 잘리지 않게
            // 스킨 모델의 경계 상자는 믿을 수 없다 — 지금 자세의 꼭짓점으로 잰다 (모델 기준, 배율 · 회전과 상관없다)
            Bounds b = DomeAquariumDeepSeaSetup.BakedBounds(go);
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float jitter = pose.count > 1 ? Mathf.Lerp(0.85f, 1.12f, (float)rng.NextDouble()) : 1f;
            float length = pose.length * Mathf.Sqrt(Stretch) * jitter;                     // 틀이 커진 만큼 조금 키운다
            if (size > 1e-4f) go.transform.localScale = Vector3.one * (length / size);

            Vector3 offset = Vector3.zero;
            Vector3 at = new Vector3(pose.at.x, pose.at.y * Stretch, pose.at.z);
            Vector3 spread = new Vector3(pose.spread.x, pose.spread.y * Stretch, pose.spread.z);
            float yaw = pose.yaw, pitch = pose.pitch;
            if (pose.count > 1)
            {
                offset = Vector3.Scale(spread, new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f));
                yaw += ((float)rng.NextDouble() * 2f - 1f) * 12f;
                pitch += ((float)rng.NextDouble() * 2f - 1f) * 8f;
            }
            // yaw 0 = 오른쪽(+x). 모델은 +z 가 앞이라 90° 돌려 둔다.
            go.transform.localRotation = Quaternion.Euler(pitch, 90f - yaw, pose.roll);
            go.transform.position += stage.TransformPoint(at + offset) - go.transform.TransformPoint(b.center);
        }
    }

    static GameObject Prefab(string source)
    {
        if (source.StartsWith("creature:"))
            return AssetDatabase.LoadAssetAtPath<GameObject>(CreatureDir + source.Substring("creature:".Length) + ".prefab");
        var data = AssetDatabase.LoadAssetAtPath<FishData>(FishDir + source + ".asset");
        return data != null ? data.prefab : null;
    }

    /// <summary>헤엄치는 동작의 한 순간으로 — 꼬리 · 지느러미가 곧게 뻗은 기본 자세보다 살아 보인다.</summary>
    static void SamplePose(GameObject go, float phase)
    {
        foreach (var anim in go.GetComponentsInChildren<Animator>(true))
        {
            var clips = anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.animationClips : null;
            if (clips == null || clips.Length == 0 || clips[0] == null) continue;
            clips[0].SampleAnimation(anim.gameObject, clips[0].length * phase);
        }
    }

    /// <summary>움직이지 않는 소품(산호 · 바위 · 해초)의 월드 경계.</summary>
    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds all = rs[0].bounds;
        foreach (var r in rs) all.Encapsulate(r.bounds);
        return all;
    }

    static GameObject Quad(string name, Transform parent, Vector3 local, Vector2 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
#endif
