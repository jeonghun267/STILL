#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 고래를 "듣고" "알아보는" 체험 두 가지.
///   수중 마이크(HydrophoneCalls): 200m 대왕고래가 지나가면 10배 빠르게 바꾼 노래, 1,000m 향유고래는 딸깍 소리. 누구인지는 말하지 않는다.
///   꼬리 사진 식별(FlukePhotoId): 테라스 쌍안경으로 혹등고래가 꼬리를 드는 순간을 보고 있으면 찰칵 — 관측소 고래 목록과 맞춰 '하얀별'.
/// 꼬리 무늬는 네 가지를 그려 둔다(반달 · 검은물결 · 하얀별 · 점박이). 만의 혹등고래 꼬리 아랫면에는 '하얀별'을 입힌다.
/// 순서: 9(로비) 안에서 돈다 (19 해안 · 20 테라스 · 18 층 하강 뒤). 따로 다시 세울 때는 30.
/// </summary>
public static class DomeAquariumWhaleListening
{
    const string TexDir = "Assets/DomeAquarium/Materials/FlukeId";
    const string MatDir = "Assets/DomeAquarium/Materials";
    const int UiLayer = 5;

    // 관측소 고래 목록 — 사진 · 이름. 만의 혹등고래는 Match 번째(하얀별).
    static readonly (string id, string name)[] Catalog =
    {
        ("HalfMoon", "1번 · 반달"), ("BlackWave", "4번 · 검은물결"), ("WhiteStar", "7번 · 하얀별"), ("Spotted", "12번 · 점박이"),
    };
    const int Match = 2;

    // 혹등고래 꼬리 외곽 — DomeAquariumCoastBuilder.FlukeMesh 와 같은 점 (오른쪽 절반, 아래 끝이 원점)
    static readonly Vector2[] RightHalf =
    {
        new Vector2(0.25f, 0f), new Vector2(0.2f, 1.0f), new Vector2(1.2f, 1.25f), new Vector2(2.2f, 1.8f),
        new Vector2(2.6f, 2.45f), new Vector2(2.0f, 2.45f), new Vector2(1.2f, 2.3f), new Vector2(0.45f, 2.15f),
    };
    const float FlukeHalfWidth = 2.6f, FlukeHeight = 2.45f;

    /// <summary>꼬리 메시 꼭짓점 → 무늬 텍스처 좌표 (폭 5.2m · 높이 2.45m 를 0~1 로).</summary>
    internal static Vector2 FlukeUv(Vector3 p) => new Vector2((p.x + FlukeHalfWidth) / (2f * FlukeHalfWidth), p.y / FlukeHeight);

    [MenuItem("DomeAquarium/30. 고래 소리 · 꼬리 사진 식별", priority = 30)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Whale] 플레이 모드에서는 세울 수 없다."); return; }
        var seq = Object.FindFirstObjectByType<LobbySequence>(FindObjectsInactive.Include);
        if (seq == null) { Debug.LogError("[Whale] LobbySequence 가 없다. 먼저 9번 메뉴."); return; }
        Build(seq);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build(LobbySequence seq)
    {
        BuildHydrophone();
        BuildFlukeId(seq);
    }

    // ─────────────────────────────────────────────────────────────
    // 수중 마이크
    // ─────────────────────────────────────────────────────────────

    static void BuildHydrophone()
    {
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director == null) { Debug.LogWarning("[Whale] DiveDirector 가 없어 수중 마이크를 건너뛴다 (18번 메뉴)."); return; }
        var old = director.transform.Find("Hydrophone");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("Hydrophone");
        go.transform.SetParent(director.transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;                                             // 수중 마이크 스피커 — 어디서 오는지보다 "들린다"
        src.volume = 0.8f;
        var hydro = go.AddComponent<HydrophoneCalls>();
        hydro.speaker = src;
        hydro.calls = new[]
        {
            new HydrophoneCalls.Call
            {
                fish = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/BlueWhale.asset"),
                clip = DomeAquariumDepthSounds.EnsureBlueWhaleSong(),
                notice = "수중 마이크 · 아주 낮은 노래 (10배 빠르게)",
                line = "들려요? 수중 마이크에 잡힌 노래예요. 너무 낮아서 원래는 사람 귀에 들리지 않아, 10배 빠르게 바꿔 들려주고 있어요. 누구의 노래일까요?",
            },
            new HydrophoneCalls.Call
            {
                fish = AssetDatabase.LoadAssetAtPath<FishData>("Assets/DomeAquarium/FishData/SpermWhale.asset"),
                clip = DomeAquariumDepthSounds.EnsureSpermWhaleClicks(),
                notice = "수중 마이크 · 딸깍딸깍 소리",
                line = "딸깍, 딸깍… 수중 마이크에 잡힌 소리예요. 깜깜한 바다에서 소리로 먹이를 찾는 누군가가 있어요. 소리가 빨라지면 먹이를 찾았다는 뜻이에요.",
            },
        };
        director.hydrophone = hydro;
        EditorUtility.SetDirty(hydro);
        EditorUtility.SetDirty(director);
        Debug.Log($"[Whale] 수중 마이크 — {string.Join(", ", hydro.calls.Where(c => c.fish != null).Select(c => c.fish.DisplayName))}");
    }

    // ─────────────────────────────────────────────────────────────
    // 꼬리 사진 식별
    // ─────────────────────────────────────────────────────────────

    static void BuildFlukeId(LobbySequence seq)
    {
        var whale = Object.FindFirstObjectByType<WhaleSurfacing>(FindObjectsInactive.Include);
        var viewer = Object.FindFirstObjectByType<BinocularViewer>(FindObjectsInactive.Include);
        if (whale == null || viewer == null || viewer.screen == null) { Debug.LogWarning("[Whale] 만의 고래(19) · 테라스 쌍안경(20)이 없어 꼬리 사진을 건너뛴다."); return; }

        // 목록 사진 넷 + 고래 꼬리 아랫면
        Directory.CreateDirectory(TexDir);
        var cards = Catalog.Select((c, i) => SaveTexture($"{TexDir}/FlukeCard_{c.id}.png", Card(i), false)).ToArray();
        var under = SaveTexture($"{TexDir}/FlukeUnder_{Catalog[Match].id}.png", Underside(Match), true);
        PatchFluke(whale, under);

        // 확대 화면 위 — 내 사진 · 목록 사진 · 결과 한 줄 · 찰칵 빛
        Transform screen = viewer.screen;
        var oldPanel = screen.Find("FlukeId");
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);
        var oldFlash = screen.Find("FlukeFlash");
        if (oldFlash != null) Object.DestroyImmediate(oldFlash.gameObject);
        var panel = new GameObject("FlukeId").transform;
        panel.SetParent(screen, false);

        var white = OverlayMaterial("Mat_FlukeIdWhite", new Color(0.95f, 0.97f, 1f, 1f), 4);
        var green = OverlayMaterial("Mat_FlukeIdMatch", new Color(0.35f, 0.95f, 0.55f, 1f), 5);
        var dark = OverlayMaterial("Mat_FlukeIdBar", new Color(0f, 0.02f, 0.04f, 0.7f), 4);
        var picture = OverlayMaterial("Mat_FlukeIdPicture", Color.white, 6);
        var flashMat = OverlayMaterial("Mat_FlukeIdFlash", new Color(1f, 1f, 1f, 0f), 8);

        Vector2 frame = new Vector2(0.128f, 0.075f), pic = new Vector2(0.12f, 0.067f);
        const float x = 0.075f, y = -0.055f;
        Quad("PhotoFrame", panel, new Vector3(-x, y, -0.002f), frame, white);
        var photo = Quad("Photo", panel, new Vector3(-x, y, -0.003f), pic, picture);
        Quad("CatalogFrame", panel, new Vector3(x, y, -0.002f), frame, white);
        var matchFrame = Quad("CatalogMatch", panel, new Vector3(x, y, -0.0025f), frame + new Vector2(0.006f, 0.006f), green);
        var catalog = Quad("Catalog", panel, new Vector3(x, y, -0.003f), pic, picture);
        DomeAquariumLobbyBuilder.Label("PhotoTitle", panel, new Vector3(-x, y + 0.046f, -0.003f), 0f, new Vector2(0.13f, 0.016f),
            "내 사진", 0.011f, Color.white, TextAlignmentOptions.Center);
        DomeAquariumLobbyBuilder.Label("CatalogTitle", panel, new Vector3(x, y + 0.046f, -0.003f), 0f, new Vector2(0.13f, 0.016f),
            "관측소 고래 목록", 0.011f, Color.white, TextAlignmentOptions.Center);
        Quad("ResultBar", panel, new Vector3(0f, -0.112f, -0.002f), new Vector2(0.34f, 0.026f), dark);
        var result = DomeAquariumLobbyBuilder.Label("Result", panel, new Vector3(0f, -0.112f, -0.003f), 0f, new Vector2(0.34f, 0.024f),
            "", 0.0145f, Color.white, TextAlignmentOptions.Center);
        // 찰칵 빛은 확대 화면과 같은 두 원 모양 — 네모로 하면 원 밖 깜깜한 곳까지 번졌다
        var flash = Quad("FlukeFlash", screen, new Vector3(0f, 0f, -0.004f), Vector2.one, flashMat);
        flash.transform.localScale = Vector3.one;
        flash.GetComponent<MeshFilter>().sharedMesh = screen.GetComponent<MeshFilter>().sharedMesh;
        SetLayer(panel, UiLayer);
        SetLayer(flash.transform, UiLayer);
        panel.gameObject.SetActive(false);

        var id = viewer.GetComponent<FlukePhotoId>();
        if (id == null) id = viewer.gameObject.AddComponent<FlukePhotoId>();
        id.viewer = viewer;
        id.whale = whale;
        id.lobby = seq;
        id.panel = panel.gameObject;
        id.photo = photo;
        id.catalog = catalog;
        id.catalogFrame = matchFrame;
        id.flash = flash;
        id.result = result;
        id.catalogPictures = cards;
        id.catalogNames = Catalog.Select(c => c.name).ToArray();
        id.match = Match;
        var src = viewer.GetComponent<AudioSource>();
        if (src == null) src = viewer.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0.8f;
        id.sound = src;
        id.shutter = DomeAquariumDepthSounds.EnsureShutter();
        id.found = DomeAquariumDepthSounds.EnsureChime();
        EditorUtility.SetDirty(id);
        Debug.Log($"[Whale] 꼬리 사진 식별 — 목록 {cards.Length}장, 만의 고래는 '{Catalog[Match].name}'.");
    }

    /// <summary>꼬리 메시에 텍스처 좌표를 넣고(예전 메시에는 없다) 아랫면 재질에 하얀별 무늬를 입힌다.</summary>
    static void PatchFluke(WhaleSurfacing whale, Texture2D under)
    {
        var mf = whale.fluke != null ? whale.fluke.GetComponentInChildren<MeshFilter>(true) : null;
        var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
        if (mf == null || mr == null || mf.sharedMesh == null) { Debug.LogWarning("[Whale] 고래 꼬리 메시를 찾지 못했다."); return; }
        var mesh = mf.sharedMesh;
        mesh.SetUVs(0, mesh.vertices.Select(FlukeUv).ToList());
        EditorUtility.SetDirty(mesh);
        var mats = mr.sharedMaterials;
        if (mats.Length > 1 && mats[1] != null)
        {
            mats[1].SetTexture("_BaseMap", under);
            mats[1].SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f));
            EditorUtility.SetDirty(mats[1]);
        }
        AssetDatabase.SaveAssets();
    }

    // ─────────────────────────────────────────────────────────────
    // 꼬리 무늬 그리기
    // ─────────────────────────────────────────────────────────────

    /// <summary>꼬리 좌표(x: -2.6~2.6, y: 0~2.45)의 무늬 — 1 하양, 0 검정. 테두리는 모두 검다.</summary>
    static float Pattern(int kind, Vector2 p, float edge)
    {
        if (edge < 0.1f) return 0f;                                         // 검은 테두리 (꼬리 끝 가장자리)
        float ax = Mathf.Abs(p.x);
        switch (kind)
        {
            case 0:   // 반달 — 하얀 바탕, 가운데 오목한 곳에서 내려온 검은 반달
                return Vector2.Distance(p, new Vector2(0f, 2.25f)) < 0.85f ? 0f : 1f;
            case 1:   // 검은물결 — 검은 바탕, 하얀 물결 띠 하나
                return Mathf.Abs(p.y - (1.55f + 0.22f * Mathf.Sin(p.x * 2.4f))) < 0.18f ? 1f : 0f;
            case 2:   // 하얀별 — 검은 바탕, 양쪽 날개 가운데 하얀 별 + 가운데 하얀 줄
            {
                Vector2 c = new Vector2(1.45f, 1.85f);
                Vector2 d = new Vector2(ax, p.y) - c;
                float a = Mathf.Atan2(d.y, d.x) - Mathf.PI * 0.5f;
                float r = 0.5f * (0.45f + 0.55f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(5f * a), 3f));
                if (d.magnitude < r) return 1f;
                return ax < 0.18f && p.y > 1.25f && p.y < 1.95f ? 1f : 0f;
            }
            default:  // 점박이 — 하얀 바탕, 검은 점 열넷
            {
                var rng = new System.Random(12);
                for (int i = 0; i < 14; i++)
                {
                    var s = new Vector2((float)rng.NextDouble() * 4.6f - 2.3f, 1.2f + (float)rng.NextDouble() * 1.1f);
                    float rad = 0.1f + (float)rng.NextDouble() * 0.12f;
                    if (Vector2.Distance(p, s) < rad) return 0f;
                }
                return 1f;
            }
        }
    }

    static List<Vector2> Outline()
    {
        var o = new List<Vector2>(RightHalf) { new Vector2(0f, 1.95f) };
        for (int i = RightHalf.Length - 1; i >= 0; i--) o.Add(new Vector2(-RightHalf[i].x, RightHalf[i].y));
        return o;
    }

    static bool Inside(List<Vector2> poly, Vector2 p)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
        return c;
    }

    static float EdgeDistance(List<Vector2> poly, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    static readonly Color Black = new Color(0.07f, 0.08f, 0.10f), White = new Color(0.93f, 0.94f, 0.93f);

    /// <summary>목록 사진 — 옅은 바다색 바탕 가운데 꼬리 아랫면.</summary>
    static Texture2D Card(int kind)
    {
        const int w = 256, h = 144;
        var poly = Outline();
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        float scale = 200f / (2f * FlukeHalfWidth), ox = w * 0.5f, oy = (h - FlukeHeight * scale) * 0.5f;
        var bg = new Color(0.70f, 0.80f, 0.86f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2((x + 0.5f - ox) / scale, (y + 0.5f - oy) / scale);
                Color c = bg * (0.92f + 0.08f * y / h);
                if (Inside(poly, p)) c = Pattern(kind, p, EdgeDistance(poly, p)) > 0.5f ? White : Black;
                tex.SetPixel(x, y, c);
            }
        return tex;
    }

    /// <summary>고래 꼬리 아랫면 — 메시 텍스처 좌표(FlukeUv) 그대로. 외곽 밖은 상관없다.</summary>
    static Texture2D Underside(int kind)
    {
        const int w = 512, h = 256;
        var poly = Outline();
        var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2((x + 0.5f) / w * 2f * FlukeHalfWidth - FlukeHalfWidth, (y + 0.5f) / h * FlukeHeight);
                float edge = Inside(poly, p) ? EdgeDistance(poly, p) : 0f;
                tex.SetPixel(x, y, Pattern(kind, p, edge) > 0.5f ? White : Black);
            }
        return tex;
    }

    static Texture2D SaveTexture(string path, Texture2D tex, bool mips)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.mipmapEnabled = mips;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>확대 화면(투명 대기열 −10) 위에 그리는 판 — 빛을 받지 않고, 대기열을 조금씩 뒤로 미뤄 겹치는 순서를 정한다.</summary>
    static Material OverlayMaterial(string name, Color color, int queueAfterScreen)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10 + queueAfterScreen;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Renderer Quad(string name, Transform parent, Vector3 local, Vector2 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }

    // ─────────────────────────────────────────────────────────────
    // 디버그
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/디버그/플레이 중 - 로비: 꼬리 사진 바로 찍기", priority = 214)]
    public static void DebugTakePhoto()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Whale] 플레이 모드에서만 쓸 수 있다."); return; }
        var id = Object.FindFirstObjectByType<FlukePhotoId>();
        if (id == null) { Debug.LogWarning("[Whale] 꼬리 사진(30번 메뉴)이 없다."); return; }
        id.DebugTake();
        Debug.Log("[Whale] 찰칵 — 꼬리 무늬 대조");
    }
}
#endif
