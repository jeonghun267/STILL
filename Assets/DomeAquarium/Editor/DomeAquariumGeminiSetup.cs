#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Resources/GeminiSettings.asset 을 만들고 고친다. API 키는 에셋에 넣지 않는다 — 에디터는 프로젝트 루트 .env 의
/// GEMINI_API_KEY 를 직접 읽고(GeminiSettings.ApiKey), 빌드(APK)에는 키가 실리지 않는다(아래 빌드 전 검사).
/// 기기에서 들리는 해설 · NPC 목소리는 에디터에서 미리 녹음한 것(VoiceBank, DomeAquariumVoiceBank)이다.
/// </summary>
public static class DomeAquariumGeminiSetup
{
    const string ResourcesDir = "Assets/DomeAquarium/Resources";
    const string AssetPath = ResourcesDir + "/GeminiSettings.asset";

    [MenuItem("DomeAquarium/8. Gemini 설정 생성 (키는 .env 에만)", priority = 8)]
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

        // 이전 프롬프트는 종을 정확히 특정하지 못하면 '정보없음'만 반환했다.
        // 교정된 영문명과 ID까지 함께 보내고, 불확실할 때는 안전한 상위 분류를 설명하도록 갱신한다.
        if (string.IsNullOrWhiteSpace(settings.promptTemplate) ||
            settings.promptTemplate.Contains("정보없음' 이라고만") ||
            !settings.promptTemplate.Contains("{ENGLISH}") ||
            !settings.promptTemplate.Contains("{ID}"))
        {
            settings.promptTemplate = GeminiSettings.DefaultPromptTemplate;
            EditorUtility.SetDirty(settings);
            Debug.Log("[DomeAquarium] Gemini 종 식별 프롬프트를 한국어명+영문명+ID 방식으로 갱신했다.");
        }

        // 키는 에셋에 두지 않는다 — 에디터는 .env 를 직접 읽고(GeminiSettings.ApiKey), 빌드(APK)에는 실리지 않는다.
        // 예전에는 여기서 .env 의 키를 에셋에 복사했고, 그 키가 APK 와 다른 공개 저장소 기록에 실려 유출 판정(403)을 받았다.
        if (!string.IsNullOrEmpty(settings.apiKey))
        {
            settings.apiKey = string.Empty;
            EditorUtility.SetDirty(settings);
            Debug.Log("[DomeAquarium] GeminiSettings 에 남아 있던 API 키를 비웠다 — 에디터는 .env 를 읽는다.");
        }
        if (string.IsNullOrEmpty(GeminiSettings.EnvKey()))
            Debug.LogWarning("[DomeAquarium] 프로젝트 루트 .env 에서 GEMINI_API_KEY 를 찾지 못했다. 미리 녹음한 목소리(VoiceBank)만 쓴다.");

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
}

/// <summary>빌드 전 검사 — GeminiSettings 에 API 키가 남아 있으면 빌드를 멈춘다(APK 안의 키는 꺼내 볼 수 있다).</summary>
public sealed class DomeAquariumGeminiKeyGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        var settings = AssetDatabase.LoadAssetAtPath<GeminiSettings>("Assets/DomeAquarium/Resources/GeminiSettings.asset");
        if (settings != null && !string.IsNullOrWhiteSpace(settings.apiKey))
            throw new BuildFailedException("GeminiSettings 에 API 키가 들어 있어 빌드를 멈췄다. " +
                                           "DomeAquarium/8 메뉴로 비우고(키는 .env 에만) 다시 빌드하세요.");
    }
}
#endif
