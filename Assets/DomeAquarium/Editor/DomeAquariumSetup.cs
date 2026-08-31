#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 씬을 세우기 전에 필요한 에셋을 준비한다.
///  - Layer Lab 물고기 머티리얼을 URP 로 변환 (Built-in Standard 그대로 두면 전부 마젠타)
///  - Noto Sans KR 로 한글 TMP 폰트 에셋 생성 (Multi Atlas Textures 켬)
///  - 프리팹을 훑어 FishData 에셋 생성
/// 코드를 새로 만들지 않고 배치·연결만 하는 도구다.
/// </summary>
public static class DomeAquariumSetup
{
    const string Root = "Assets/DomeAquarium";
    const string FishRoot = "Assets/Layer Lab/3D Characters-Fish/Fish";
    const string FontAssetPath = Root + "/Fonts/NotoSansKR-Medium SDF.asset";
    const string SourceFontPath = Root + "/Fonts/NotoSansKR-Medium.ttf";

    // ─────────────────────────────────────────────────────────────
    // 1. 머티리얼 URP 변환
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// URP 로 바꿔야 할 에셋 팩 폴더들.
    /// 하나라도 빠뜨리면 그 팩의 오브젝트가 통째로 마젠타가 되거나 아예 안 보인다.
    /// (RPG Pack 을 빠뜨려서 미스터리 상자가 안 보였다)
    /// </summary>
    static readonly string[] MaterialRoots =
    {
        "Assets/Layer Lab/3D Characters-Fish/Fish",
        "Assets/RPG Pack",
    };

    [MenuItem("DomeAquarium/1. 에셋 준비 - 머티리얼 URP 변환", priority = 1)]
    public static int ConvertMaterialsToUrp()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[DomeAquarium] URP/Lit 셰이더를 찾을 수 없다. URP 패키지를 확인할 것.");
            return 0;
        }
        Shader particleUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        var roots = new List<string>();
        foreach (string r in MaterialRoots) if (AssetDatabase.IsValidFolder(r)) roots.Add(r);
        if (roots.Count == 0) { Debug.LogWarning("[DomeAquarium] 변환할 머티리얼 폴더가 없다."); return 0; }

        string[] guids = AssetDatabase.FindAssets("t:Material", roots.ToArray());
        int converted = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            // 이미 URP 계열이면 건너뛴다.
            if (mat.shader.name.StartsWith("Universal Render Pipeline/")) continue;

            // 파티클 계열은 Lit 으로 바꾸면 안 된다 — 알파/애디티브가 다 깨진다.
            // Built-in "Particles/..." 는 URP/Particles/Unlit 으로 보낸다.
            if (mat.shader.name.StartsWith("Particles/") || mat.shader.name.StartsWith("Legacy Shaders/Particles/"))
            {
                if (particleUnlit == null)
                {
                    Debug.LogWarning($"[DomeAquarium] URP 파티클 셰이더를 못 찾아 건너뜀: {path}");
                    continue;
                }

                Texture pTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color pCol = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor")
                           : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                bool additive = mat.shader.name.Contains("Additive");

                mat.shader = particleUnlit;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", pTex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", pTex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", pCol);
                // Surface=Transparent(1), Blend: Additive(1) / Alpha(0)
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 1f : 0f);
                mat.renderQueue = 3000;

                EditorUtility.SetDirty(mat);
                converted++;
                Debug.Log($"[DomeAquarium] URP 파티클 변환: {path}");
                continue;
            }

            // Built-in Standard 의 값을 먼저 읽어둔다. 셰이더를 바꾸면 프로퍼티가 날아간다.
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 mainScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 mainOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.25f;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;

            mat.shader = lit;

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", mainScale);
                mat.SetTextureOffset("_BaseMap", mainOffset);
            }
            // URP/Lit 은 _MainTex 도 별칭으로 갖고 있어 같이 채워두면 안전하다.
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (bump != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bump);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            // 드로우콜 30 예산 — 물고기가 같은 머티리얼을 공유하므로 인스턴싱을 켠다.
            mat.enableInstancing = true;

            EditorUtility.SetDirty(mat);
            converted++;
            Debug.Log($"[DomeAquarium] URP 변환: {path}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DomeAquarium] 머티리얼 {converted}개를 URP/Lit 으로 변환했다.");
        return converted;
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 한글 TMP 폰트 에셋
    // ─────────────────────────────────────────────────────────────

    [MenuItem("DomeAquarium/2. 에셋 준비 - 한글 TMP 폰트 생성", priority = 2)]
    public static TMP_FontAsset CreateKoreanFontAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null)
        {
            Debug.Log($"[DomeAquarium] 한글 폰트 에셋이 이미 있다: {FontAssetPath}");
            RegisterFallback(existing);
            return existing;
        }

        var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (source == null)
        {
            Debug.LogError($"[DomeAquarium] 원본 폰트를 찾을 수 없다: {SourceFontPath}");
            return null;
        }

        // 한글 음절은 11,172자다. Static 으로 전부 구우면 1024 아틀라스 한 장에 들어가지 않아 두부가 된다.
        // Dynamic + Multi Atlas Textures 조합이 유일하게 안전한 설정이다.
        var fontAsset = TMP_FontAsset.CreateFontAsset(
            source,
            90,                                  // samplingPointSize
            9,                                   // atlasPadding
            GlyphRenderMode.SDFAA,
            1024, 1024,
            AtlasPopulationMode.Dynamic,
            true                                 // enableMultiAtlasSupport ← 이게 핵심
        );

        if (fontAsset == null)
        {
            Debug.LogError("[DomeAquarium] TMP_FontAsset 생성 실패.");
            return null;
        }

        fontAsset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

        // 아틀라스 텍스처와 머티리얼을 서브에셋으로 붙여야 폰트가 온전한 하나의 에셋이 된다.
        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                var tex = fontAsset.atlasTextures[i];
                if (tex == null) continue;
                tex.name = fontAsset.name + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
        }
        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);

        RegisterFallback(fontAsset);
        Debug.Log($"[DomeAquarium] 한글 TMP 폰트 생성: {FontAssetPath} (Dynamic + Multi Atlas)");
        return fontAsset;
    }

    /// <summary>기본 LiberationSans 로 렌더되는 텍스트도 한글이 나오도록 폴백에 등록한다.</summary>
    static void RegisterFallback(TMP_FontAsset korean)
    {
        var settings = TMP_Settings.instance;
        if (settings == null) return;

        var so = new SerializedObject(settings);
        var fallbacks = so.FindProperty("m_fallbackFontAssets");
        if (fallbacks == null) return;

        for (int i = 0; i < fallbacks.arraySize; i++)
        {
            if (fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == korean) return;
        }

        fallbacks.InsertArrayElementAtIndex(fallbacks.arraySize);
        fallbacks.GetArrayElementAtIndex(fallbacks.arraySize - 1).objectReferenceValue = korean;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        Debug.Log("[DomeAquarium] TMP Settings 폴백 목록에 한글 폰트를 등록했다.");
    }

    public static TMP_FontAsset LoadKoreanFont()
    {
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. FishData 생성
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 프리팹 이름 -> 한국어 통용명. 어종 통용명은 확인 가능한 사실이라 매핑하지만,
    /// 여기에 없는 이름은 프리팹 이름을 그대로 두고 사용자 확인을 받는다. 설명문은 절대 만들지 않는다.
    /// </summary>
    static readonly Dictionary<string, string> KoreanNames = new Dictionary<string, string>
    {
        { "Clownfish", "흰동가리" },
        { "TomatoClownfish", "토마토흰동가리" },
        { "PinkSkunkClownfish", "핑크스컹크흰동가리" },
        { "BlueTang", "블루탱" },
        { "YellowTang", "옐로우탱" },
        { "PowderBlueTang", "파우더블루탱" },
        { "SailfinTang", "돛대탱" },
        { "UnicornTang", "유니콘탱" },
        { "MoorishIdol", "깃대돔" },
        { "QueenAngelfish", "퀸엔젤피쉬" },
        { "FrenchAngelfish", "프렌치엔젤피쉬" },
        { "FlameAngelfish", "플레임엔젤피쉬" },
        { "KoranAngelfish", "코란엔젤피쉬" },
        { "CopperbandButterflyfish", "카퍼밴드나비고기" },
        { "RacoonButterflyfish", "너구리나비고기" },
        { "FoureyeButterflyfish", "네눈나비고기" },
        { "LongnoseButterflyfish", "긴코나비고기" },
        { "SaddleButterflyfish", "새들나비고기" },
        { "GreenChromis", "그린크로미스" },
        { "BarrierReefChromis", "배리어리프크로미스" },
        { "DominoDamsel", "도미노담셀" },
        { "AzureDamsel", "애저담셀" },
        { "JewelDamsel", "주얼담셀" },
        { "ThreeStripeDamsel", "세줄담셀" },
        { "FireGoby", "파이어고비" },
        { "BluebandedGoby", "블루밴드고비" },
        { "GreenbandedGoby", "그린밴드고비" },
        { "WheelersShrimpGoby", "휠러새우고비" },
        { "ScissortailDartfish", "가위꼬리다트피쉬" },
        { "StrawberryDottyback", "스트로베리도티백" },
        { "GoldenDottyback", "골든도티백" },
        { "BlackSeahorse", "검은해마" },
        { "GreenSeahorse", "초록해마" },
        { "RedSeahorse", "붉은해마" },
        { "ZebraSeahorse", "얼룩말해마" },
        { "PygmeSeahorse", "피그미해마" },
        { "BluefinTuna", "참다랑어" },
        { "YellowfinTuna", "황다랑어" },
        { "SkipjackTuna", "가다랑어" },
        { "MantaRay", "쥐가오리" },
        { "SpottedEagleRay", "점박이매가오리" },
        { "GreateWhiteShark", "백상아리" },
        { "BlacktipReefShark", "흑기흉상어" },
        { "NurseShark", "간호상어" },
        { "AngelShark", "전자리상어" },
        { "CommonThresherShark", "환도상어" },
        { "GreenTurtle", "푸른바다거북" },
        { "HawksbillTurtle", "매부리바다거북" },
        { "LoggerheadTurtle", "붉은바다거북" },
        { "LeatherbackTurtle", "장수거북" },
        { "HectorDolphin", "헥터돌고래" },
        { "PinkDolphin", "분홍돌고래" },
        { "IndusRiverDolphin", "인더스강돌고래" },
        { "TucuxiDophin", "투쿠시돌고래" },
        { "RightWhaleDolphin", "참돌고래붙이" },
        { "BlueLobster", "블루랍스터" },
        { "Lobster", "바닷가재" },
    };

    struct Band { public float center; public float width; }

    /// <summary>지시서 5장의 어종군별 수직 층위표.</summary>
    static Band BandFor(string n)
    {
        string s = n.ToLowerInvariant();

        // 대형 어종은 눈높이로 내린다.
        // 실측: floorY=-7.2, ceilY=+7.5, span=14.7 이므로 bandCenter 0.85 는 y=+5.3 (밴드 2.7~7.5)이다.
        // 플레이어 눈은 y≈1.36 이라 상어가 머리 위로만 다녀 "어디 갔는지 모르겠다"가 된다.
        // 0.60 = y≈1.62 로 눈앞을 가로지른다. 폭도 넓혀 위아래로 오간다.
        if (s.Contains("shark") || s.Contains("dolphin") || s.Contains("dophin") || s.Contains("turtle"))
            return new Band { center = 0.60f, width = 0.50f };
        if (s.Contains("tuna") || s.Contains("ray"))
            return new Band { center = 0.55f, width = 0.45f };
        if (s.Contains("butterfly") || s.Contains("tang") || s.Contains("angelfish") || s.Contains("idol"))
            return new Band { center = 0.50f, width = 0.45f };
        if (s.Contains("clownfish") || s.Contains("damsel") || s.Contains("chromis"))
            return new Band { center = 0.30f, width = 0.35f };
        if (s.Contains("goby") || s.Contains("seahorse") || s.Contains("dottyback") ||
            s.Contains("lobster") || s.Contains("basslet") || s.Contains("cardinal") ||
            s.Contains("dartfish") || s.Contains("tilefish") || s.Contains("grouper"))
            return new Band { center = 0.15f, width = 0.25f };

        // 판별 불확실 — 지시서대로 중앙 넓은 밴드로 두고 보고한다.
        return new Band { center = 0.5f, width = 0.6f };
    }

    static bool IsProp(string n)
    {
        string s = n.ToLowerInvariant();
        return s.StartsWith("coral") || s.StartsWith("rock") || s.StartsWith("shell") ||
               s.StartsWith("seaweed") || s.StartsWith("starfish") ||
               s == "ground" || s == "land" || s == "sea";
    }

    [MenuItem("DomeAquarium/3. FishData 생성", priority = 3)]
    public static List<FishData> GenerateFishData()
    {
        string outDir = Root + "/FishData";
        if (!AssetDatabase.IsValidFolder(outDir)) AssetDatabase.CreateFolder(Root, "FishData");

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { FishRoot + "/Prefabs" });
        var created = new List<FishData>();
        var unmapped = new List<string>();
        var uncertainBand = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path);
            if (IsProp(n)) continue;                       // 산호/바위/조개는 ReefScatter 몫

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string assetPath = $"{outDir}/{n}.asset";
            var data = AssetDatabase.LoadAssetAtPath<FishData>(assetPath);
            bool isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<FishData>();

            data.prefab = prefab;

            // 종 고유 ID. 프리팹 이름 기반이라 안정적이다 — 캐시 키/도감 기록이 이걸 쓴다.
            if (string.IsNullOrWhiteSpace(data.speciesId)) data.speciesId = n;

            if (KoreanNames.TryGetValue(n, out string kr)) data.koreanName = kr;
            else { data.koreanName = n; unmapped.Add(n); }

            // 설명은 지어내지 않는다. 런타임에 Gemini 가 채운다.
            data.description = string.Empty;

            var band = BandFor(n);
            data.bandCenter = band.center;
            data.bandWidth = band.width;
            if (Mathf.Approximately(band.center, 0.5f) && Mathf.Approximately(band.width, 0.6f))
                uncertainBand.Add(n);

            if (isNew)
            {
                data.rarity = FishRarity.Common;           // 배분은 사용자가 조정
                data.swimScale = 0.25f;
                data.revealScale = 0.4f;
                data.schoolCount = 6;
                data.swimSpeed = 0.9f;
                AssetDatabase.CreateAsset(data, assetPath);
            }
            else EditorUtility.SetDirty(data);

            created.Add(data);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[DomeAquarium] FishData {created.Count}개 생성/갱신.");
        if (unmapped.Count > 0)
            Debug.LogWarning($"[DomeAquarium] 한국어명 미확인 {unmapped.Count}종 — 프리팹 이름 그대로 둠: {string.Join(", ", unmapped)}");
        if (uncertainBand.Count > 0)
            Debug.LogWarning($"[DomeAquarium] 어종군 판별 불확실 {uncertainBand.Count}종 — bandCenter 0.5 / bandWidth 0.6: {string.Join(", ", uncertainBand)}");

        return created;
    }

    public static List<GameObject> LoadPropPrefabs(string prefix)
    {
        var list = new List<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { FishRoot + "/Prefabs" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path);
            if (!n.ToLowerInvariant().StartsWith(prefix)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) list.Add(go);
        }
        return list;
    }
}
#endif
