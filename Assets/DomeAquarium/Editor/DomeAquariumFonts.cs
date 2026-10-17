#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 글꼴을 나눔스퀘어라운드(네이버, SIL OFL 1.1)로 바꾼다 — 둥글고 굵어 아이들에게 친근하고, VR 에서 멀리서도 또렷하다.
/// 예전 Noto Sans KR 은 또렷하지만 딱딱해 체험 부스에는 어울리지 않았다.
///
///  - 기본은 Regular, TMP 굵게(Bold)는 가짜 굵기 대신 ExtraBold 글꼴을 쓴다 (글꼴 굵기 표 700).
///  - 나눔에 없는 기호는 Noto Sans KR 로 대신 그린다 (폴백). TMP 설정 폴백 목록에도 나눔을 맨 앞에 둔다.
///  - 장면의 Noto 글자를 모두 나눔으로 바꾼다. 앞으로 패널 빌더들도 나눔을 쓴다 (DomeAquariumSetup.LoadKoreanFont).
/// 한글 11,172자를 다 구우면 아틀라스 한 장에 안 들어가므로 Noto 와 같이 Dynamic + 여러 장 아틀라스로 만든다.
/// </summary>
public static class DomeAquariumFonts
{
    public const string SourceFolder = "Assets/ThirdParty/Fonts/NanumSquareRound";
    public const string RegularTtf = SourceFolder + "/NanumSquareRoundR.ttf";
    public const string ExtraBoldTtf = SourceFolder + "/NanumSquareRoundEB.ttf";
    public const string RegularAsset = "Assets/DomeAquarium/Fonts/NanumSquareRoundR SDF.asset";
    public const string ExtraBoldAsset = "Assets/DomeAquarium/Fonts/NanumSquareRoundEB SDF.asset";
    const string NotoAsset = "Assets/DomeAquarium/Fonts/NotoSansKR-Medium SDF.asset";

    [MenuItem("DomeAquarium/2-1. 글꼴 바꾸기 (나눔스퀘어라운드)", priority = 3)]
    public static void ApplyMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[글꼴] 플레이 모드에서는 바꿀 수 없다.");
            return;
        }
        var noto = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NotoAsset);
        var bold = Ensure(ExtraBoldTtf, ExtraBoldAsset, noto);
        var regular = Ensure(RegularTtf, RegularAsset, noto);
        if (regular == null || bold == null) return;

        // 굵게 → ExtraBold 글꼴
        var so = new SerializedObject(regular);
        var weights = so.FindProperty("m_FontWeightTable");
        if (weights != null && weights.arraySize > 7)
        {
            weights.GetArrayElementAtIndex(7).FindPropertyRelative("regularTypeface").objectReferenceValue = bold;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(regular);
        RegisterFallback(regular);

        int swapped = SwapSceneFonts(noto, regular, out int custom);
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[글꼴] 나눔스퀘어라운드로 {swapped}곳을 바꿨다" + (custom > 0 ? $" (따로 꾸민 머티리얼 {custom}곳은 기본 머티리얼로)" : "") + ".");
    }

    static TMP_FontAsset Ensure(string ttfPath, string assetPath, TMP_FontAsset fallback)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (asset == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (source == null) { Debug.LogError($"[글꼴] 원본 글꼴이 없다: {ttfPath}"); return null; }
            asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                  AtlasPopulationMode.Dynamic, true);
            if (asset == null) { Debug.LogError($"[글꼴] TMP 글꼴을 만들지 못했다: {ttfPath}"); return null; }
            asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(asset, assetPath);
            // 아틀라스와 머티리얼을 서브에셋으로 붙여야 한 덩어리 에셋이 된다 (DomeAquariumSetup 의 Noto 와 같은 방식)
            if (asset.atlasTextures != null)
                for (int i = 0; i < asset.atlasTextures.Length; i++)
                {
                    var tex = asset.atlasTextures[i];
                    if (tex == null) continue;
                    tex.name = asset.name + " Atlas " + i;
                    AssetDatabase.AddObjectToAsset(tex, asset);
                }
            if (asset.material != null)
            {
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }
        if (fallback != null)
        {
            var list = asset.fallbackFontAssetTable ?? new List<TMP_FontAsset>();
            if (!list.Contains(fallback)) list.Add(fallback);
            asset.fallbackFontAssetTable = list;
            EditorUtility.SetDirty(asset);
        }
        return asset;
    }

    /// <summary>기본 LiberationSans 로 그리는 글자도 한글이 나눔으로 나오도록 TMP 폴백 목록 맨 앞에 둔다.</summary>
    static void RegisterFallback(TMP_FontAsset font)
    {
        var settings = TMP_Settings.instance;
        if (settings == null) return;
        var so = new SerializedObject(settings);
        var fallbacks = so.FindProperty("m_fallbackFontAssets");
        if (fallbacks == null) return;
        for (int i = 0; i < fallbacks.arraySize; i++)
            if (fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == font) return;
        fallbacks.InsertArrayElementAtIndex(0);
        fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = font;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
    }

    static int SwapSceneFonts(TMP_FontAsset from, TMP_FontAsset to, out int customMaterials)
    {
        customMaterials = 0;
        int n = 0;
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || EditorUtility.IsPersistent(t)) continue;
            // Noto 와, 글꼴을 따로 안 정해 기본 LiberationSans(+한글 폴백)로 그리던 글자를 함께 바꾼다
            bool liberation = t.font != null && TMP_Settings.defaultFontAsset != null && t.font == TMP_Settings.defaultFontAsset;
            if (t.font != from && t.font != null && !liberation) continue;
            if (t.fontSharedMaterial != null && t.font != null && t.fontSharedMaterial != t.font.material) customMaterials++;
            Undo.RecordObject(t, "Swap font");
            t.font = to;
            t.fontSharedMaterial = to.material;
            EditorUtility.SetDirty(t);
            n++;
        }
        return n;
    }
}
#endif
