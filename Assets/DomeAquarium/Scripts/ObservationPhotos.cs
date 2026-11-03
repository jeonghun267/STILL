using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 관찰기 사진 — 생물을 기록하는 순간, 관람객 눈 자리에서 그 생물 쪽을 한 장 찍어 둔다. 관찰 일지(FishEncyclopediaPanel)의 상세에 "내가 찍은 사진"으로 뜬다.
/// 이번 체험 동안만 갖고 있다(저장하지 않는다). 종마다 처음 한 장 — 같은 종을 다시 기록해도 바꾸지 않는다.
/// 1,000m 신호의 주인을 플래시로 찍으면(FlashOrWatch) 흐린 사진이 그대로 남는다 — 지켜보고 기록한 사진으로 바꾸지 않는다.
/// 사진기는 꺼 둔 카메라 하나를 필요할 때만 Render 한다 (UI 층은 빼고). 색만 남긴 작은 렌더 텍스처를 종마다 하나씩.
/// </summary>
public static class ObservationPhotos
{
    public const int Width = 320, Height = 200;

    sealed class Photo { public RenderTexture texture; public string caption; public bool locked; }

    static readonly Dictionary<FishData, Photo> photos = new Dictionary<FishData, Photo>();
    static Camera shooter;
    static RenderTexture scratch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        foreach (var p in photos.Values) if (p.texture != null) p.texture.Release();
        photos.Clear();
        shooter = null;
        scratch = null;
    }

    public static bool TryGet(FishData fish, out Texture texture, out string caption)
    {
        texture = null;
        caption = null;
        if (fish == null || !photos.TryGetValue(fish, out var p) || p.texture == null) return false;
        texture = p.texture;
        caption = p.caption;
        return true;
    }

    /// <summary>
    /// eye 에서 target 쪽을 찍는다. size 는 담을 생물 크기(m) — 화각을 맞춘다. blur 면 아주 작게 찍어 늘린다(흐린 사진).
    /// lockIt 이면 뒤에 같은 종을 다시 찍어도 이 사진을 지키지 않고 바꾸지 않는다.
    /// </summary>
    public static void Take(FishData fish, Vector3 eye, Vector3 target, float size, string caption, bool blur = false, bool lockIt = false)
    {
        if (fish == null) return;
        if (photos.TryGetValue(fish, out var old) && (old.locked || old.texture != null) && !lockIt) return;
        Camera main = Camera.main;
        if (main == null) return;

        if (shooter == null)
        {
            var go = new GameObject("ObservationPhotoCamera") { hideFlags = HideFlags.HideAndDontSave };
            shooter = go.AddComponent<Camera>();
            shooter.enabled = false;
        }
        shooter.CopyFrom(main);
        shooter.enabled = false;
        shooter.stereoTargetEye = StereoTargetEyeMask.None;
        shooter.cullingMask = main.cullingMask & ~(1 << 5);                 // 기록 카드 · 판(UI 층)은 빼고 생물만
        Vector3 to = target - eye;
        float dist = Mathf.Max(0.3f, to.magnitude);
        shooter.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(to, Vector3.up));
        shooter.fieldOfView = Mathf.Clamp(2f * Mathf.Atan2(Mathf.Max(0.15f, size) * 0.9f, dist) * Mathf.Rad2Deg, 14f, 60f);
        shooter.nearClipPlane = Mathf.Clamp(dist * 0.6f, 0.05f, 0.9f);      // 눈앞 관찰기 · 판(UI 층이 아니다)은 사진 밖으로

        int w = blur ? 40 : Width, h = blur ? 25 : Height;
        if (scratch == null || scratch.width != w || scratch.height != h)
        {
            if (scratch != null) scratch.Release();
            scratch = new RenderTexture(w, h, 16) { name = "PhotoScratch" };
        }
        shooter.targetTexture = scratch;
        shooter.Render();
        shooter.targetTexture = null;

        var keep = new RenderTexture(Width, Height, 0) { name = "Photo_" + fish.name, filterMode = FilterMode.Bilinear };
        Graphics.Blit(scratch, keep);                                         // 흐린 사진은 40×25 를 늘려 뿌옇게
        if (old != null && old.texture != null) old.texture.Release();
        photos[fish] = new Photo { texture = keep, caption = caption, locked = lockIt };
    }

    /// <summary>지금 깊이 — 사진 아래 한 줄 ("수심 200m 에서", "해변 조수 웅덩이에서").</summary>
    public static string Where()
    {
        var d = DiveDirector.Instance;
        if (d != null && d.LayerIndex >= 0 && d.Layer != null && d.Current != DiveDirector.State.Ending) return $"수심 {d.Layer.meters:#,0}m 에서";
        return "해변 조수 웅덩이에서";
    }
}
