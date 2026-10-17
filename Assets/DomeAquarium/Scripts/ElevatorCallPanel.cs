using TMPro;
using UnityEngine;

/// <summary>
/// 층의 할 일을 마치면 돔 안 눈앞 아래쪽에 뜨는 "엘리베이터 타기" 판. 버튼은 DiveDirector.CallElevator 를 부른다.
/// 머리에 붙여 끌고 다니지 않는다(헤드락은 멀미를 부른다). 제자리에 두었다가 고개를 많이 돌리면 그쪽으로 천천히 옮겨 온다 —
/// 할 일을 마친 뒤에도 마음껏 둘러보다가, 돌아보면 언제나 손 닿는 곳에 있다.
/// 관찰기 화면(avoid)과 시야에서 겹치면 비켜 선다 — 판은 제자리에 있고 관찰기 화면은 손(또는 머리)을 따라 움직이므로,
/// 고개를 조금만 돌려도 둘이 포개질 수 있었다.
/// </summary>
public sealed class ElevatorCallPanel : MonoBehaviour
{
    public CanvasGroup group;
    public TMP_Text title;
    public Transform view;                     // 비면 Camera.main

    [Header("배치")]
    public float distance = 0.62f;             // 팔 길이(0.6~0.7m) 안 — 찔러서 누른다
    public float drop = 0.30f;                 // 눈높이보다 26° 아래 — 자막 줄(13° 아래)보다 낮아 대사와 함께 떠도 가리지 않는다
    public float side = -0.08f;                // 살짝 왼쪽 — 오른손 관찰기 화면(오른쪽 아래)과 자막(정면 13° 아래) 사이 빈자리
    public float followAngle = 40f;            // 판이 시선에서 이 각도보다 멀어지면 옮겨 온다
    public float followSharpness = 2.5f;
    public float fadeSeconds = 0.4f;

    [Header("겹치지 않기")]
    public Transform avoid;                    // 관찰기 화면(ObservationAim.statusRoot). 보이는 동안 시야에서 이만큼 떨어진다
    public Vector2 avoidAngles = new Vector2(27f, 12f);   // 좌우·위아래 (판 반폭 14.5° + 관찰기 화면 반폭 14.3° 남짓)

    bool shown;
    bool moving;
    Vector3 goalPos;
    Quaternion goalRot;

    public bool IsShown => shown;

    public void Show(string text)
    {
        if (title != null && !string.IsNullOrEmpty(text)) title.text = text;
        if (!shown) { shown = true; Retarget(); transform.SetPositionAndRotation(goalPos, goalRot); }
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        SetInteractable(true);
    }

    public void Hide(bool immediate = false)
    {
        shown = false;
        SetInteractable(false);
        if (immediate && group != null) group.alpha = 0f;
    }

    void SetInteractable(bool on)
    {
        if (group == null) return;
        group.interactable = on;
        group.blocksRaycasts = on;
    }

    void LateUpdate()
    {
        if (group != null)
        {
            float target = shown ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));
        }
        if (!shown) return;

        Transform v = ResolveView();
        if (v == null) return;
        Vector3 forward = Flat(v.forward);
        Vector3 toPanel = Flat(transform.position - v.position);
        bool far = forward.sqrMagnitude > 1e-4f && toPanel.sqrMagnitude > 1e-4f && Vector3.Angle(forward, toPanel) > followAngle;
        if (!moving && (far || Overlaps(v.position, transform.position)))
        {
            moving = true;
            Retarget();
        }
        if (!moving) return;

        float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, goalPos, k), Quaternion.Slerp(transform.rotation, goalRot, k));
        if ((transform.position - goalPos).sqrMagnitude < 0.0004f) moving = false;
    }

    void Retarget()
    {
        Transform v = ResolveView();
        if (v == null) return;
        Vector3 forward = Yaw(v);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        // 기본은 살짝 왼쪽. 관찰기 화면이 그 자리에 와 있으면(손을 가운데로 든 경우) 더 왼쪽, 그래도 겹치면 오른쪽.
        goalPos = v.position + forward * distance + right * side + Vector3.down * drop;
        foreach (float alt in new[] { side - 0.24f, side + 0.36f })
        {
            if (!Overlaps(v.position, goalPos)) break;
            goalPos = v.position + forward * distance + right * alt + Vector3.down * drop;
        }
        // 살짝 위를 보게 눕혀 내려다보는 눈과 마주하게 한다. 캔버스 앞면(-Z)이 사람 쪽을 본다.
        goalRot = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(20f, 0f, 0f);
    }

    /// <summary>시야에서 관찰기 화면과 포개지나 — 눈에서 본 좌우·위아래 각도 차이로 잰다.</summary>
    bool Overlaps(Vector3 eye, Vector3 panelPos)
    {
        if (avoid == null || !avoid.gameObject.activeInHierarchy) return false;
        Angles(eye, panelPos, out float yawA, out float pitchA);
        Angles(eye, avoid.position, out float yawB, out float pitchB);
        return Mathf.Abs(Mathf.DeltaAngle(yawA, yawB)) < avoidAngles.x && Mathf.Abs(pitchA - pitchB) < avoidAngles.y;
    }

    static void Angles(Vector3 eye, Vector3 p, out float yaw, out float pitch)
    {
        Vector3 d = p - eye;
        yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 머리가 향한 수평 방향. 똑바로 위·아래를 보면 forward 를 눕힌 값이 0 이 되므로 머리 위쪽 축으로 대신한다
    /// (위를 보면 머리 위쪽이 뒤를, 아래를 보면 앞을 가리킨다). 예전에는 그때 월드 +Z 로 떨어져 엉뚱한 곳에 떴다.
    /// </summary>
    static Vector3 Yaw(Transform v)
    {
        Vector3 f = Flat(v.forward);
        if (f.sqrMagnitude < 0.04f) f = Flat(v.up) * (v.forward.y > 0f ? -1f : 1f);
        return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
    }

    Transform ResolveView()
    {
        if (view != null) return view;
        Camera cam = Camera.main;
        if (cam != null) view = cam.transform;
        return view;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
