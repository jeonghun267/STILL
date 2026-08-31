#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프로젝트 루트 .env 의 GEMINI_API_KEY 를 Resources/GeminiSettings.asset 으로 옮긴다.
/// .env 는 에디터에서만 읽히고 빌드에 들어가지 않기 때문에, 기기에서 쓰려면 이 단계가 필요하다.
/// 만들어진 .asset 에는 키가 들어 있으므로 .gitignore 로 막아 두었다.
/// </summary>
public static class DomeAquariumGeminiSetup
{
    const string ResourcesDir = "Assets/DomeAquarium/Resources";
    const string AssetPath = ResourcesDir + "/GeminiSettings.asset";

    [MenuItem("DomeAquarium/8. Gemini 설정 생성 / .env 키 동기화", priority = 8)]
    public static GeminiSettings SyncFromEnv()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesDir))
            AssetDatabase.CreateFolder("Assets/DomeAquarium", "Resources");

        var settings = AssetDatabase.LoadAssetAtPath<GeminiSettings>(AssetPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<GeminiSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            Debug.Log($"[DomeAquarium] GeminiSettings 생성: {AssetPath}");
        }

        // 이미 만들어진 에셋에 박혀 있는 죽은 모델 이름을 고친다.
        // (gemini-2.0-flash 는 이 키로 조회되지 않아 전 종이 404 로 실패했다)
        string[] deadModels = { "gemini-2.0-flash", "gemini-1.5-flash", "gemini-pro" };
        foreach (string dead in deadModels)
        {
            if (settings.model != dead) continue;
            settings.model = "gemini-flash-latest";
            EditorUtility.SetDirty(settings);
            Debug.Log($"[DomeAquarium] 사용할 수 없는 모델 '{dead}' 을 'gemini-flash-latest' 로 교체했다.");
            break;
        }

        // 출력 예산이 작으면 thinking 토큰이 다 먹고 답변이 잘린다(finishReason: MAX_TOKENS).
        if (settings.maxOutputTokens < 1024)
        {
            Debug.Log($"[DomeAquarium] maxOutputTokens {settings.maxOutputTokens} → 2048 (thinking 토큰 때문에 답변이 잘린다).");
            settings.maxOutputTokens = 2048;
            EditorUtility.SetDirty(settings);
        }

        string key = ReadKeyFromEnv();
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("[DomeAquarium] 프로젝트 루트 .env 에서 GEMINI_API_KEY 를 찾지 못했다. " +
                             "설정 에셋은 만들었으니 인스펙터에서 직접 넣어도 된다.");
        }
        else if (settings.apiKey != key)
        {
            settings.apiKey = key;
            EditorUtility.SetDirty(settings);
            // 키 자체는 절대 로그에 찍지 않는다.
            Debug.Log($"[DomeAquarium] .env 의 API 키를 GeminiSettings 에 동기화했다 (길이 {key.Length}).");
        }
        else
        {
            Debug.Log("[DomeAquarium] API 키가 이미 최신이다.");
        }

        // Quest 빌드에서 Gemini 를 부르려면 INTERNET 권한이 있어야 한다.
        // Auto 로 두면 Unity 가 코드 분석으로 판단하는데, 확실하게 Require 로 못박는다.
        if (!PlayerSettings.Android.forceInternetPermission)
        {
            PlayerSettings.Android.forceInternetPermission = true;
            Debug.Log("[DomeAquarium] Android INTERNET 권한을 Require 로 설정했다.");
        }

        AssetDatabase.SaveAssets();
        return settings;
    }

    static string ReadKeyFromEnv()
    {
        // Application.dataPath = <project>/Assets
        string envPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
        if (!File.Exists(envPath)) return null;

        foreach (string raw in File.ReadAllLines(envPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string name = line.Substring(0, eq).Trim();
            if (!name.Equals("GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase)) continue;

            string value = line.Substring(eq + 1).Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }
}
#endif
