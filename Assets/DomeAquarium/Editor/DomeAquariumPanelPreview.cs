#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 손목 메뉴와 시작 패널을 정면에서 찍는다 — 헤드셋 없이 버튼·글꼴 모습을 확인하는 도구 (Temp/CardPreview).
/// 손목 메뉴는 평소 숨겨져(CanvasGroup 알파 0) 있어 찍는 동안만 보이게 하고 되돌린다.
/// </summary>
public static class DomeAquariumPanelPreview
{
    [MenuItem("DomeAquarium/디버그/손목 메뉴·시작 패널 미리보기 캡처", priority = 213)]
    public static void Capture()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CardPreview"));
        Directory.CreateDirectory(dir);
        foreach (var (name, file) in new[] { ("WristMenu", "wrist.png"), ("StartPanelRoot", "start.png") })
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.LogWarning($"[미리보기] {name} 이(가) 없다."); continue; }
            var groups = go.GetComponentsInChildren<CanvasGroup>(true);
            var saved = new float[groups.Length];
            for (int i = 0; i < groups.Length; i++) { saved[i] = groups[i].alpha; groups[i].alpha = 1f; }
            try { Shoot(go.transform, Path.Combine(dir, file)); }
            finally { for (int i = 0; i < groups.Length; i++) groups[i].alpha = saved[i]; }
        }
        Debug.Log($"[미리보기] 손목 메뉴·시작 패널 저장: {dir}");
    }

    static void Shoot(Transform target, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var t in target.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.ForceMeshUpdate(true, true);
        var bounds = new Bounds(target.position, Vector3.zero);
        var corners = new Vector3[4];
        foreach (var r in target.GetComponentsInChildren<RectTransform>())
        {
            r.GetWorldCorners(corners);
            foreach (var c in corners) bounds.Encapsulate(c);
        }
        var camGo = new GameObject("PanelPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.15f + 0.005f;
        cam.nearClipPlane = 0.001f;
        cam.farClipPlane = 10f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.12f, 0.16f);      // 수중 돔과 비슷한 어두운 바다색 위에서 본다
        // 살짝 비스듬히 — 앞판이 떠 있는 입체감이 보이게
        Vector3 forward = Quaternion.AngleAxis(-12f, target.up) * Quaternion.AngleAxis(8f, target.right) * target.forward;
        cam.transform.SetPositionAndRotation(bounds.center - forward * 1.5f, Quaternion.LookRotation(forward, target.up));
        var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}
#endif
