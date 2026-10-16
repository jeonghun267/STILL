#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 정보 카드와 관찰 일지를 따로 찍어 보는 도구. 헤드셋 없이 글자 정렬·칸 배치를 눈으로 확인하려고 만들었다.
/// 씬에서 멀리 떨어진 빈 하늘(y=500)로 패널을 잠깐 옮겨 수중 배경색으로 찍고, 모든 값을 되돌린다 — 씬을 저장하지 않는다.
/// </summary>
public static class DomeAquariumCardPreview
{
    static readonly string[] Samples = { "WyomingWhiteClownfish", "BlueTang", "GreateWhiteShark" };
    static readonly Vector3 Eye = new Vector3(0f, 500f, 0f);

    [MenuItem("DomeAquarium/디버그/정보 카드 미리보기 캡처", priority = 211)]
    public static void CaptureMenu()
    {
        foreach (string id in Samples) Capture(id);
    }

    [MenuItem("DomeAquarium/디버그/관찰 일지 미리보기 캡처", priority = 212)]
    public static void CaptureLogMenu() => CaptureLog();

    public static string Capture(string speciesId)
    {
        if (!CanRun()) return null;

        var info = Object.FindFirstObjectByType<FishInfoPanel>(FindObjectsInactive.Include);
        FishData fish = LoadFish(speciesId);
        if (info == null || fish == null)
        {
            Debug.LogWarning($"[미리보기] FishInfoPanel 또는 '{speciesId}' FishData 를 찾지 못했다.");
            return null;
        }

        var state = new PanelState(info.transform, info.canvasGroup);
        try
        {
            info.gameObject.SetActive(true);
            if (info.canvasGroup != null) info.canvasGroup.alpha = 1f;
            info.PreviewInEditor(fish, fish.FallbackDescription);

            // 실제 관람 거리(1.3m)에서, 카드가 화면 높이의 약 70%를 차지하도록 좁은 화각으로 찍는다.
            info.transform.SetPositionAndRotation(Eye + Vector3.forward * 1.3f, Quaternion.identity);
            return Render(info.gameObject, 26f, $"card_{speciesId}.png");
        }
        finally
        {
            state.Restore();
        }
    }

    public static string CaptureLog()
    {
        if (!CanRun()) return null;

        var log = Object.FindFirstObjectByType<FishEncyclopediaPanel>(FindObjectsInactive.Include);
        if (log == null)
        {
            Debug.LogWarning("[미리보기] FishEncyclopediaPanel 을 찾지 못했다. '15. 도감 태블릿 적용'을 먼저 실행하세요.");
            return null;
        }

        var state = new PanelState(log.transform, log.canvasGroup);
        try
        {
            log.gameObject.SetActive(true);
            if (log.canvasGroup != null) log.canvasGroup.alpha = 1f;
            log.PreviewInEditor(20, LoadFish("BlueTang"));

            // 실제로 펼쳐지는 거리에서 찍는다. 화각 34° 는 태블릿 전체와 약간의 여백이 들어오는 값.
            log.transform.SetPositionAndRotation(Eye + Vector3.forward * log.openDistance, Quaternion.identity);
            return Render(log.gameObject, 34f, "log.png");
        }
        finally
        {
            log.EndPreview();
            state.Restore();
        }
    }

    // ─────────────────────────────────────────────────────────────

    static bool CanRun()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
        Debug.LogWarning("[미리보기] 편집 모드에서만 쓸 수 있다.");
        return false;
    }

    static FishData LoadFish(string speciesId) =>
        AssetDatabase.LoadAssetAtPath<FishData>($"Assets/DomeAquarium/FishData/{speciesId}.asset");

    static string Render(GameObject panelRoot, float fieldOfView, string fileName)
    {
        var camGo = new GameObject("PanelPreviewCamera");
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        try
        {
            var cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(Eye, Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.08f, 0.13f);   // 낮 수조 물빛과 비슷한 어두운 청록
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.targetTexture = rt;

            Canvas.ForceUpdateCanvases();
            foreach (var layout in panelRoot.GetComponentsInChildren<RectTransform>(true))
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
            foreach (TMP_Text t in panelRoot.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CardPreview"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, fileName);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Debug.Log($"[미리보기] 저장: {file}");
            return file;
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }

    /// <summary>찍는 동안 바꾼 위치·알파·글자·활성 상태를 되돌린다.</summary>
    sealed class PanelState
    {
        readonly Transform tr;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly bool active;
        readonly CanvasGroup group;
        readonly float alpha;
        readonly TMP_Text[] texts;
        readonly string[] strings;
        readonly bool[] textActive;

        public PanelState(Transform tr, CanvasGroup group)
        {
            this.tr = tr;
            position = tr.position;
            rotation = tr.rotation;
            active = tr.gameObject.activeSelf;
            this.group = group;
            alpha = group != null ? group.alpha : 1f;
            texts = tr.GetComponentsInChildren<TMP_Text>(true);
            strings = new string[texts.Length];
            textActive = new bool[texts.Length];
            for (int i = 0; i < texts.Length; i++) { strings[i] = texts[i].text; textActive[i] = texts[i].gameObject.activeSelf; }
        }

        public void Restore()
        {
            if (tr == null) return;
            tr.SetPositionAndRotation(position, rotation);
            if (group != null) group.alpha = alpha;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) continue;
                texts[i].text = strings[i];
                texts[i].gameObject.SetActive(textActive[i]);
            }
            tr.gameObject.SetActive(active);
        }
    }
}
#endif
