#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sketchfab 에서 받은 glTF 9종을 관찰 생물로 만든다 (출처: Assets/ThirdParty/CREDITS.md, 전부 CC BY 4.0).
///  - 1,000m 심해 6종: 심해아귀 · 흡혈오징어 · 덤보문어 · 바이퍼피시 · 망원경고기 · 도끼고기 — 늘 천천히 떠다닌다.
///  - 향유고래(1,000m) · 대왕고래(200m): 큰 생물로 가끔 지나간다. 혹등고래는 로비 앞바다(코스트 빌더 19)가 쓴다 — 일지 종이 아니다.
///  - 24m 산호초: Pixit3D "Fish Free Pack"(에셋스토어 무료) — 복어 · 갑오징어 · 문어는 일지 종(애니메이션이 없어 SwimWiggle 로 몸을 흔든다),
///    게 · 불가사리 · 해삼 · 성게 · 진주조개 · 새우 · 소라껍데기는 산호초 바닥 꾸밈(ReefFloor/Critters, 24m 에서만 켠다).
///
/// 생물마다: 머리가 +Z 를 보도록 돌리고, 몸길이 1 로 맞춘 Model 을 빈 루트 아래 둔 프리팹 → 크기는 FishData.swimScale(=몸길이 m).
/// 재질은 새로 만든다 — glTF 원본 중 셋은 조명을 받지 않는(unlit) 재질이라 빛 없는 1,000m 에서 혼자 환하게 떠 보였고,
/// 투명(BLEND) 재질은 정렬이 어긋난다. 1,000m 생물은 관찰기 조명을 직접 받는 Deep Creature 셰이더, 고래(200m · 로비)는 URP Simple Lit.
/// 발광 텍스처(흡혈오징어·망원경고기·도끼고기의 발광기)는 그대로 살린다.
/// 서식 층과 층 등장은 층별 하강(18)이 이 이름들로 정한다. 순서: 21 → 18.
/// </summary>
public static class DomeAquariumDeepSeaSetup
{
    const string Sketchfab = "Assets/ThirdParty/Sketchfab";
    const string Dir = "Assets/DomeAquarium/Creatures";
    const string FishDir = "Assets/DomeAquarium/FishData";

    internal sealed class Spec
    {
        public string name, glb, clip, korean, english, scientific;
        public Vector3 euler;                   // 머리를 +Z 로
        public float meters, speed, emission;
        public int school;
        public float bandCenter, bandWidth;
        public FishRarity rarity;
        public DiveDepth habitat;
        public bool cutout, twoSided;
        public string source;                   // glb 대신 쓸 원본 경로 (Pixit3D 프리팹)
        public bool keepMaterials;              // 원본이 이미 URP 재질이면 그대로
        public bool wiggle;                     // 애니메이션이 없으면 몸을 살짝 흔들어 헤엄치는 것처럼 (SwimWiggle)
        public string Path => source ?? $"{Sketchfab}/{glb}";
    }

    const string Pixit = "Assets/PixitGames/Pixit3D/PixitUnderseaCollection/LowPolyFishFree/Prefabs";

    // 24m 산호초에 더하는 Pixit3D "Fish Free Pack" (에셋스토어 무료, URP) — 애니메이션은 없고 뼈대만 있다.
    // 헤엄치는 셋은 관찰 일지 종으로, 바닥에 사는 것들은 산호초 바닥 꾸밈(ReefCritters)으로 쓴다.
    // 크기는 실물보다 조금 크게 — 산호초의 다른 소형 어종처럼 눈에 띄게.
    internal static readonly Spec[] Reef =
    {
        new Spec { name = "Pufferfish", source = $"{Pixit}/BabyPutterfish_Rigged_Unity.prefab", korean = "복어", english = "Pufferfish",
                   scientific = "Tetraodontidae", meters = 0.22f, speed = 0.3f, school = 2, bandCenter = 0.45f, bandWidth = 0.3f,
                   rarity = FishRarity.Uncommon, habitat = DiveDepth.Reef24, keepMaterials = true, wiggle = true },
        new Spec { name = "Cuttlefish", source = $"{Pixit}/Cuttlefish_Rigged_Unity.prefab", korean = "갑오징어", english = "Cuttlefish",
                   scientific = "Sepia", meters = 0.3f, speed = 0.35f, school = 3, bandCenter = 0.42f, bandWidth = 0.3f,
                   rarity = FishRarity.Rare, habitat = DiveDepth.Reef24, keepMaterials = true, wiggle = true },
        new Spec { name = "Octopus", source = $"{Pixit}/Octopus_Rigged_Unity.prefab", korean = "문어", english = "Common octopus",
                   scientific = "Octopus vulgaris", meters = 0.5f, speed = 0.25f, school = 1, bandCenter = 0.32f, bandWidth = 0.2f,
                   rarity = FishRarity.Rare, habitat = DiveDepth.Reef24, keepMaterials = true, wiggle = true },
    };

    // 해변 조수 웅덩이(DomeAquarium/24) — 관찰기 첫 연습. 바닥에 사는 것(bandCenter 0)은 바닥을 기어 다닌다(PracticeTank.crawlBand).
    // 층별 하강의 어느 층에도 넣지 않는다(DomeAquariumDiveSetup) — 관찰 일지에는 연습 웅덩이 종으로 들어간다.
    internal static readonly Spec[] Shore =
    {
        new Spec { name = "Crab", source = $"{Pixit}/Crab_Rigged_Unity.prefab", korean = "게", english = "Shore crab",
                   scientific = "Brachyura", meters = 0.14f, speed = 0.08f, school = 2, bandCenter = 0f, bandWidth = 0f,
                   rarity = FishRarity.Common, habitat = DiveDepth.Reef24, keepMaterials = true, wiggle = true },
        new Spec { name = "Starfish", source = $"{Pixit}/Starfish_Rigged_Unity.prefab", korean = "불가사리", english = "Sea star",
                   scientific = "Asteroidea", meters = 0.16f, speed = 0.02f, school = 1, bandCenter = 0f, bandWidth = 0f,
                   rarity = FishRarity.Common, habitat = DiveDepth.Reef24, keepMaterials = true },
        new Spec { name = "SeaUrchin", source = $"{Pixit}/SeaUrchin_Static_Unity.prefab", korean = "성게", english = "Sea urchin",
                   scientific = "Echinoidea", meters = 0.09f, speed = 0.015f, school = 1, bandCenter = 0f, bandWidth = 0f,
                   rarity = FishRarity.Common, habitat = DiveDepth.Reef24, keepMaterials = true },
        new Spec { name = "Shrimp", source = $"{Pixit}/Shrimp_Rigged_Unity.prefab", korean = "새우", english = "Rockpool shrimp",
                   scientific = "Caridea", meters = 0.07f, speed = 0.1f, school = 1, bandCenter = 0.3f, bandWidth = 0.2f,
                   rarity = FishRarity.Uncommon, habitat = DiveDepth.Reef24, keepMaterials = true, wiggle = true },
    };

    // 산호초 바닥 꾸밈 — (원본, 크기 m, 몇 개). 바닥(y≈-8)은 관람객 눈에서 9m 아래라 관찰기(6m) 대상이 아니라 풍경이다.
    static readonly (string file, float meters, int count)[] Critters =
    {
        ("Crab_Rigged_Unity", 0.3f, 4), ("Starfish_Rigged_Unity", 0.3f, 5), ("SeaCucumber_Rigged_Unity", 0.35f, 3),
        ("SeaUrchin_Static_Unity", 0.2f, 5), ("PearlSeashell_Rigged_Unity", 0.3f, 2), ("Shrimp_Rigged_Unity", 0.16f, 3),
        ("SeaShell_Static_Unity", 0.2f, 3), ("EmptyShell_Static_Unity", 0.2f, 3),
    };

    // 몸길이는 실제 성체 크기. 고래만 반지름 12m 수조에 맞게 눌렀다(DomeAquariumScaleSetup 규칙과 같은 값).
    // 띠(bandCenter)는 수조 높이 16m 의 비율 — 0.5 가 관람객 눈높이다. 관찰기 조명(5.5m)이 닿는 눈높이 근처에 둔다.
    internal static readonly Spec[] All =
    {
        new Spec { name = "Anglerfish", glb = "DeepSea/Anglerfish.glb", korean = "심해아귀", english = "Humpback anglerfish",
                   scientific = "Melanocetus johnsonii", euler = new Vector3(0f, 180f, 0f), meters = 0.18f, speed = 0.10f,
                   school = 1, bandCenter = 0.42f, bandWidth = 0.20f, rarity = FishRarity.Epic, habitat = DiveDepth.Midnight1000,
                   cutout = true, twoSided = true },
        new Spec { name = "VampireSquid", glb = "DeepSea/VampireSquid.glb", korean = "흡혈오징어", english = "Vampire squid",
                   scientific = "Vampyroteuthis infernalis", euler = new Vector3(80f, 0f, 0f), meters = 0.28f, speed = 0.14f,
                   school = 2, bandCenter = 0.52f, bandWidth = 0.25f, rarity = FishRarity.Epic, habitat = DiveDepth.Midnight1000,
                   emission = 1.2f, twoSided = true },
        new Spec { name = "DumboOctopus", glb = "DeepSea/DumboOctopus.glb", korean = "덤보문어", english = "Dumbo octopus",
                   scientific = "Grimpoteuthis", euler = Vector3.zero, meters = 0.30f, speed = 0.10f,
                   school = 2, bandCenter = 0.38f, bandWidth = 0.20f, rarity = FishRarity.Epic, habitat = DiveDepth.Midnight1000,
                   twoSided = true },
        new Spec { name = "Viperfish", glb = "DeepSea/Viperfish.glb", korean = "바이퍼피시", english = "Pacific viperfish",
                   scientific = "Chauliodus macouni", euler = new Vector3(0f, -90f, 0f), meters = 0.25f, speed = 0.22f,
                   school = 2, bandCenter = 0.56f, bandWidth = 0.30f, rarity = FishRarity.Rare, habitat = DiveDepth.Midnight1000,
                   twoSided = true },
        new Spec { name = "Telescopefish", glb = "DeepSea/Telescopefish.glb", korean = "망원경고기", english = "Telescopefish",
                   scientific = "Gigantura chuni", euler = Vector3.zero, meters = 0.20f, speed = 0.14f,
                   school = 1, bandCenter = 0.48f, bandWidth = 0.25f, rarity = FishRarity.Epic, habitat = DiveDepth.Midnight1000,
                   emission = 1.5f, twoSided = true },
        new Spec { name = "Hatchetfish", glb = "DeepSea/Hatchetfish.glb", korean = "도끼고기", english = "Silver hatchetfish",
                   scientific = "Argyropelecus", euler = new Vector3(-90f, 90f, 0f), meters = 0.08f, speed = 0.16f,   // 모델이 옆으로 누워 있다
                   school = 7, bandCenter = 0.60f, bandWidth = 0.25f, rarity = FishRarity.Uncommon, habitat = DiveDepth.Midnight1000,
                   emission = 0.5f, twoSided = true },
        new Spec { name = "SpermWhale", glb = "Whales/SpermWhale.glb", clip = "SWIM-delphinidae", korean = "향유고래", english = "Sperm whale",
                   scientific = "Physeter macrocephalus", euler = Vector3.zero, meters = 7.0f, speed = 1.0f,
                   school = 1, bandCenter = 0.5f, bandWidth = 0.4f, rarity = FishRarity.Legendary, habitat = DiveDepth.Midnight1000 },
        new Spec { name = "BlueWhale", glb = "Whales/BlueWhale.glb", clip = "SWIM-delphinidae", korean = "대왕고래", english = "Blue whale",
                   scientific = "Balaenoptera musculus", euler = Vector3.zero, meters = 9.0f, speed = 1.0f,
                   school = 1, bandCenter = 0.5f, bandWidth = 0.4f, rarity = FishRarity.Legendary, habitat = DiveDepth.Twilight200 },
    };

    // 로비 앞바다의 혹등고래 — 관찰 일지 종이 아니라 쌍안경으로 보는 풍경이라 FishData 는 만들지 않는다 (코스트 빌더 19 가 쓴다).
    internal static readonly Spec Humpback = new Spec
    {
        name = "HumpbackWhale", glb = "Whales/HumpbackWhale.glb", clip = "SWIM-delphinidae", korean = "혹등고래",
        english = "Humpback whale", scientific = "Megaptera novaeangliae", euler = Vector3.zero, meters = 14f,
    };

    /// <summary>몸길이 1 로 맞춘 혹등고래 프리팹. 모델이 없으면 null — 코스트 빌더는 그때 예전의 타원체 등을 쓴다.</summary>
    internal static GameObject HumpbackPrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Sketchfab}/{Humpback.glb}");
        if (source == null) return null;
        Folders();
        return BuildPrefab(Humpback, source);
    }

    [MenuItem("DomeAquarium/21. 심해 생물 · 고래 만들기 (1,000m · 200m)", priority = 21)]
    public static void BuildMenu()
    {
        var made = Build();
        Debug.Log($"[심해 생물] {made.Count}종 — " + string.Join(", ", made.Select(f => $"{f.koreanName} {f.bodyLength:0.##}m")) +
                  ". 층에 넣으려면 '18. 층별 하강 적용'을 다시 실행하세요.");
    }

    internal static List<FishData> Build()
    {
        Folders();
        var made = new List<FishData>();
        foreach (var s in All.Concat(Reef).Concat(Shore))
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(s.Path);
            if (source == null) { Debug.LogWarning($"[심해 생물] 모델이 없습니다: {s.Path}"); continue; }
            var prefab = BuildPrefab(s, source);
            made.Add(BuildData(s, prefab));
        }
        BuildReefCritters();
        AssetDatabase.SaveAssets();
        return made;
    }

    // ─────────────────────────────────────────────────────────────
    // 프리팹
    // ─────────────────────────────────────────────────────────────

    static GameObject BuildPrefab(Spec s, GameObject source)
    {
        var root = new GameObject(s.name);
        try
        {
            var model = Object.Instantiate(source, root.transform, false);
            model.name = "Model";
            model.transform.localRotation = Quaternion.Euler(s.euler);

            // 고래 모델에 딸린 반투명 물보라 판(transp) — 점프 연출용이라 헤엄칠 때는 넓은 판으로만 보인다.
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.sharedMaterials.Any(m => m != null && m.name.StartsWith("transp"))) continue;
                var mf = r.GetComponent<MeshFilter>();
                Object.DestroyImmediate(r);
                if (mf != null) Object.DestroyImmediate(mf);
            }

            var converted = new Dictionary<Material, Material>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && !s.keepMaterials; i++)
                {
                    if (mats[i] == null) continue;
                    if (!converted.TryGetValue(mats[i], out var m))
                        converted[mats[i]] = m = LitMaterial(s, mats[i], converted.Count);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }

            // 헤엄 애니메이션 — glTFast 는 Animator 만 붙이고 컨트롤러는 비워 둔다. 도끼고기는 애니메이션이 없다.
            var anim = model.GetComponentInChildren<Animator>(true);
            var clip = AssetDatabase.LoadAllAssetsAtPath(s.Path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                .OrderBy(c => s.clip != null && c.name == s.clip ? 0 : 1).FirstOrDefault();
            if (clip != null && anim != null)
            {
                anim.runtimeAnimatorController = Controller(s, clip);
                anim.applyRootMotion = false;
                anim.updateMode = AnimatorUpdateMode.Normal;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;     // 화면 밖 개체는 건너뛴다 (BoidManager 와 같은 규칙)
            }
            else if (anim != null) Object.DestroyImmediate(anim);

            // 고래는 같은 속도에도 꼬리를 훨씬 느리게 친다 — 클립(0.8초에 한 번)을 물고기 기준으로 돌리면 파닥거린다.
            // BoidManager · LargeCreaturePasses 는 프리팹에 이미 있는 FishAnimatorSync 를 그대로 쓴다.
            if (s.meters >= 5f && anim != null && clip != null)
            {
                var sync = root.AddComponent<FishAnimatorSync>();
                sync.speedToAnimSpeed = 0.4f;
                sync.animSpeedMin = 0.25f;
                sync.animSpeedMax = 0.7f;
                sync.bankAngle = 0f;
            }

            // 몸길이 1, 몸 한가운데가 루트. 뼈대 모델은 메시 원본 바운즈가 실제 크기와 100배씩 어긋나서(뼈에 0.01 배율)
            // 크기 보정(10)의 측정법을 쓰지 않고, 뼈대를 적용해 구운 꼭짓점으로 잰다. 그래서 10 은 이 종들을 건너뛴다.
            Bounds b = BakedBounds(root);
            float native = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (native > 1e-6f) model.transform.localScale = Vector3.one / native;
            b = BakedBounds(root);
            model.transform.localPosition = -b.center;
            FitSkinBounds(root);
            if (s.wiggle && clip == null) model.AddComponent<SwimWiggle>();

            string path = $"{Dir}/Prefabs/{s.name}.prefab";
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>루트 기준으로 렌더러 전체를 감싸는 박스 — 스킨 메시는 지금 자세로 구운 꼭짓점을 쓴다.</summary>
    internal static Bounds BakedBounds(GameObject root)
    {
        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        bool has = false;
        Bounds total = default;
        var baked = new Mesh();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            Vector3[] verts;
            Matrix4x4 m;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                verts = SkinnedWorld(smr, baked);
                m = toRoot;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                verts = mf.sharedMesh.vertices;
                m = toRoot * r.transform.localToWorldMatrix;
            }
            foreach (var v in verts)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (!has) { total = new Bounds(p, Vector3.zero); has = true; }
                else total.Encapsulate(p);
            }
        }
        Object.DestroyImmediate(baked);
        return total;
    }

    /// <summary>
    /// 스킨 메시의 지금 자세 꼭짓점(월드). BakeMesh(useScale: false) 결과에 위치 · 회전만 곱해야 배율을 바꿔도 크기가 비례한다
    /// — 실험으로 확인했다 (useScale: true 에 위치 · 회전만 곱하면 Model 을 줄여도 크기가 그대로 나온다).
    /// </summary>
    static Vector3[] SkinnedWorld(SkinnedMeshRenderer smr, Mesh baked)
    {
        smr.BakeMesh(baked, false);
        var verts = baked.vertices;
        Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        for (int i = 0; i < verts.Length; i++) verts[i] = m.MultiplyPoint3x4(verts[i]);
        return verts;
    }

    /// <summary>
    /// glTF 에서 온 스킨 메시의 경계 상자는 뼈 배율(0.01 등)과 어긋나 실제 몸과 크기 · 위치가 다르다 — 그대로 두면 화면 안에 있어도
    /// 컬링돼 사라진다. 구운 꼭짓점으로 다시 잡고, 헤엄치며 휘는 만큼 25% 넉넉히 둔다.
    /// </summary>
    static void FitSkinBounds(GameObject root)
    {
        var baked = new Mesh();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            Transform space = smr.rootBone != null ? smr.rootBone : smr.transform;
            bool has = false;
            Bounds b = default;
            foreach (var w in SkinnedWorld(smr, baked))
            {
                Vector3 p = space.InverseTransformPoint(w);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
            if (!has) continue;
            b.Expand(b.size * 0.25f);
            smr.localBounds = b;
        }
        Object.DestroyImmediate(baked);
    }

    static RuntimeAnimatorController Controller(Spec s, AnimationClip clip)
    {
        string path = $"{Dir}/Animations/{s.name}.controller";
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c == null) return AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
        var state = c.layers[0].stateMachine.defaultState;
        if (state != null && state.motion != clip) { state.motion = clip; EditorUtility.SetDirty(c); }
        return c;
    }

    /// <summary>glTF 재질(glTFast 셰이더 그래프)의 색 · 발광 텍스처를 URP Simple Lit 으로 옮긴다.</summary>
    static Material LitMaterial(Spec s, Material src, int index)
    {
        string path = $"{Dir}/Materials/Mat_{s.name}_{index}.mat";
        // 1,000m 생물은 관찰기 조명을 직접 계산하는 셰이더 — Quest 용 URP 설정은 추가 조명이 꺼져 있어 Simple Lit 으로는
        // 스포트라이트를 비춰도 검은 그림자로만 보인다. 200m 대왕고래 · 로비 혹등고래는 해를 받으니 Simple Lit 그대로.
        var shader = Shader.Find(s.habitat == DiveDepth.Midnight1000 ? "DomeAquarium/Deep Creature" : "Universal Render Pipeline/Simple Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;

        Texture baseMap = Tex(src, "baseColorTexture") ?? src.mainTexture;
        Color baseColor = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : Color.white;
        baseColor.a = 1f;
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", 0.35f);
        m.SetColor("_SpecColor", new Color(0.18f, 0.18f, 0.18f));
        m.EnableKeyword("_SPECULAR_COLOR");

        // 지느러미 · 이빨은 투명(BLEND) 대신 잘라내기 — 정렬이 어긋나지 않고 Quest 에서 싸다.
        m.SetFloat("_AlphaClip", s.cutout ? 1f : 0f);
        m.SetFloat("_Cutoff", 0.4f);
        if (s.cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", s.twoSided ? 0f : 2f);       // 얇은 지느러미 · 막은 양면

        Texture emissive = Tex(src, "emissiveTexture");
        if (emissive != null && s.emission > 0f)
        {
            m.SetTexture("_EmissionMap", emissive);
            m.SetColor("_EmissionColor", Color.white * s.emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
        }
        m.renderQueue = s.cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture Tex(Material m, string property) => m.HasProperty(property) ? m.GetTexture(property) : null;

    // ─────────────────────────────────────────────────────────────
    // 산호초 바닥 꾸밈 — ReefFloor/Critters. 24m(산호초)에서만 켠다 (DiveDirector.reefCritters)
    // ─────────────────────────────────────────────────────────────

    static void BuildReefCritters()
    {
        var reef = GameObject.Find("ReefFloor");
        var props = reef != null ? reef.transform.Find("Generated") : null;
        if (props == null) { Debug.LogWarning("[심해 생물] ReefFloor/Generated 가 없어 바닥 생물을 놓지 않았다."); return; }
        var old = reef.transform.Find("Critters");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("Critters").transform;
        group.SetParent(reef.transform, false);

        // 산호 · 바위 옆에 둔다 — 바닥 높이는 그 소품의 높이를 쓴다(바닥에 콜라이더가 없다)
        var anchors = Enumerable.Range(0, props.childCount).Select(i => props.GetChild(i))
            .Where(t => t.name.Contains("_Coral_") || t.name.Contains("_Rock_")).ToList();
        if (anchors.Count == 0) { Debug.LogWarning("[심해 생물] 산호 · 바위 소품이 없어 바닥 생물을 놓지 않았다."); return; }
        var rng = new System.Random(2411);
        int placed = 0;
        foreach (var (file, meters, count) in Critters)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pixit}/{file}.prefab");
            if (prefab == null) continue;
            for (int i = 0; i < count; i++)
            {
                var anchor = anchors[rng.Next(anchors.Count)];
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 0.5f + (float)rng.NextDouble() * 0.6f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.name = $"{file.Split('_')[0]}_{i:00}";
                go.transform.localScale = Vector3.one;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim != null) anim.enabled = false;           // 애니메이션 없는 원본 — 매 프레임 비용만 든다
                Bounds b = BakedBounds(go);
                float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (size > 1e-4f) go.transform.localScale = Vector3.one * (meters / size);
                Vector3 at = anchor.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                go.transform.position = at;
                go.transform.position += Vector3.up * (anchor.position.y - WorldMinY(go));   // 몸 아래 끝을 바닥에
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                placed++;
            }
        }

        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director != null) { director.reefCritters = group.gameObject; EditorUtility.SetDirty(director); }
        group.gameObject.SetActive(true);
        EditorSceneManager.MarkSceneDirty(reef.scene);
        Debug.Log($"[심해 생물] 산호초 바닥 생물 {placed}개 — 게 · 불가사리 · 해삼 · 성게 · 진주조개 · 새우 · 소라껍데기");
    }

    static float WorldMinY(GameObject go)
    {
        float min = float.MaxValue;
        var baked = new Mesh();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                foreach (var v in SkinnedWorld(smr, baked)) min = Mathf.Min(min, v.y);
            else if (r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null)
                foreach (var v in mf.sharedMesh.vertices) min = Mathf.Min(min, r.transform.TransformPoint(v).y);
        }
        Object.DestroyImmediate(baked);
        return min == float.MaxValue ? go.transform.position.y : min;
    }

    // ─────────────────────────────────────────────────────────────
    // 관찰 일지
    // ─────────────────────────────────────────────────────────────

    static FishData BuildData(Spec s, GameObject prefab)
    {
        string path = $"{FishDir}/{s.name}.asset";
        var f = AssetDatabase.LoadAssetAtPath<FishData>(path);
        if (f == null)
        {
            f = ScriptableObject.CreateInstance<FishData>();
            AssetDatabase.CreateAsset(f, path);
        }
        f.speciesId = s.name;
        f.koreanName = s.korean;
        f.englishName = s.english;
        f.scientificName = s.scientific;
        f.description = "";                     // 다른 종과 같이 런타임에 Gemini 가 채운다
        f.rarity = s.rarity;
        f.habitat = s.habitat;
        f.freshwater = false;
        f.unnamed = false;
        f.prefab = prefab;
        f.bodyLength = s.meters;
        f.swimScale = s.meters;                  // 프리팹 몸길이가 1 이다
        f.revealScale = Mathf.Clamp(s.meters, 0.22f, 0.85f);
        f.swimSpeed = s.speed;
        f.schoolCount = s.school;
        f.bandCenter = s.bandCenter;
        f.bandWidth = s.bandWidth;
        EditorUtility.SetDirty(f);
        return f;
    }

    static void Folders()
    {
        Folder($"{Dir}");
        Folder($"{Dir}/Prefabs");
        Folder($"{Dir}/Materials");
        Folder($"{Dir}/Animations");
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
