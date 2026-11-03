using TMPro;
using UnityEngine;

/// <summary>
/// 테라스 난간 가의 관망 쌍안경 (관광지 기둥 쌍안경처럼 고정). 접안부에 얼굴을 대면 시야가 6배 남짓 당겨진다 —
/// 손으로 들 필요가 없어 손 추적 · 컨트롤러 · 에디터에서 똑같이 쓴다.
///
///  - 확대 화면: 꺼 둔 보조 카메라가 렌더 텍스처에 그리고, 눈앞 0.6m 의 쌍안경 모양 판(빌더가 만든 두 원 메시)에 띄운다.
///    판은 UI 레이어 — 보조 카메라는 UI 레이어를 그리지 않아 자기 화면을 다시 비추지 않는다. 둘레는 화면 비네트로 어둡게.
///  - 보는 방향은 고개 방향이지만 부드럽게 따라간다 — 7배 확대에서는 머리의 작은 떨림도 크게 흔들려 멀미가 난다.
///    쌍안경 몸도 같은 방향으로 돈다. 기둥 정면에서 좌우 75°, 위아래 -15~20° 까지.
///  - 화면 한가운데에 BinocularSubject 가 dwell 초 머무르면 이름표가 뜨고, line 이 있으면 처음 한 번 관측소장이 말한다.
/// </summary>
public sealed class BinocularViewer : MonoBehaviour
{
    public LobbySequence lobby;
    public Transform eyepiece;                          // 접안부 — 머리가 여기 가까이 오면 들여다보는 것
    public Transform head;                              // 돌아가는 쌍안경 몸 (yaw · pitch)
    public Camera zoomCamera;                           // 꺼 둔 보조 카메라
    public Transform screen;                            // 눈앞 확대 화면 (렌더 텍스처를 입힌 두 원 메시)
    public TMP_Text label;                              // 확대 화면 아래 이름표 (screen 의 자식)

    [Header("들여다보기")]
    public float engageRadius = 0.38f;                  // 머리와 접안부의 수평 거리
    public Vector2 engageHeight = new Vector2(-0.5f, 0.35f);   // 접안부 기준 머리 높이 범위 — 키 작은 아이도 닿게
    public float viewDistance = 0.6f;
    public float viewLift = 3f;                         // 확대 화면을 눈높이보다 이만큼(°) 올린다 — 아래쪽(13°)의 자막 자리를 비운다
    [Range(0f, 1f)] public float vignette = 0.85f;

    [Header("확대")]
    public float zoomFov = 5f;                          // 화면(세로 30°)에 5°를 채운다 — 6배. 더 당기면 작은 떨림도 크게 흔들린다
    public float follow = 6f;                           // 고개를 따라가는 빠르기 (1/s)
    public Vector2 yawLimit = new Vector2(-75f, 75f);
    public Vector2 pitchLimit = new Vector2(-15f, 20f);

    [Header("이름표")]
    public float dwell = 1.0f;
    public float centerDegrees = 1.2f;                  // 확대 화면 한가운데
    public float bodyDegreesMax = 1.3f;                 // 몸 크기로 넓혀 주는 각도의 상한 — 큰 배가 화면 가장자리에서 이름표를 가져가지 않게

    Transform view;
    ScreenFader fader;
    Renderer[] body;                                    // 쌍안경 몸 — 들여다보는 동안은 눈앞을 가리지 않게 숨긴다
    Quaternion look;
    bool engaged;
    bool forced;
    BinocularSubject watching;
    float watchedFor;

    public bool Engaged => engaged;

    void Start()
    {
        if (zoomCamera != null) zoomCamera.enabled = false;
        if (screen != null) screen.gameObject.SetActive(false);
        look = head != null ? head.rotation : transform.rotation;
        body = head != null ? head.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
    }

    // 들여다보는 채로 로비가 꺼지면(엘리베이터 · 장면 전환) 화면 비네트가 남지 않게
    void OnDisable()
    {
        if (engaged) SetEngaged(false);
    }

    /// <summary>헤드셋 없이 확인할 때 — 머리 자리와 상관없이 들여다본다.</summary>
    public void DebugForce(bool on) => forced = on;

    void LateUpdate()
    {
        if (view == null) { var cam = Camera.main; if (cam != null) view = cam.transform; else return; }

        bool want = forced || Near();
        if (want != engaged) SetEngaged(want);
        if (!engaged) return;

        // 고개 방향 → 기둥 정면 기준 각도 제한 → 부드럽게
        Vector3 local = transform.InverseTransformDirection(view.forward);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, yawLimit.x, yawLimit.y);
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -pitchLimit.y, -pitchLimit.x);
        Quaternion target = transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
        look = Quaternion.Slerp(look, target, 1f - Mathf.Exp(-follow * Time.deltaTime));
        if (head != null) head.rotation = look;

        if (zoomCamera != null) zoomCamera.transform.SetPositionAndRotation(view.position, look);
        if (screen != null)
        {
            Vector3 lift = view.up * (viewDistance * Mathf.Tan(viewLift * Mathf.Deg2Rad));
            screen.SetPositionAndRotation(view.position + view.forward * viewDistance + lift, Quaternion.LookRotation(view.forward, view.up));
        }

        Watch();
    }

    bool Near()
    {
        if (eyepiece == null) return false;
        Vector3 d = view.position - eyepiece.position;
        float h = d.y;
        d.y = 0f;
        if (d.magnitude > engageRadius || h < engageHeight.x || h > engageHeight.y) return false;
        // 쌍안경 쪽(기둥 정면)을 보고 있어야 한다 — 등지고 서 있으면 들여다보는 게 아니다
        return Vector3.Dot(Flat(view.forward), Flat(transform.forward)) > 0.3f;
    }

    void SetEngaged(bool on)
    {
        engaged = on;
        if (zoomCamera != null) zoomCamera.enabled = on;
        if (screen != null) screen.gameObject.SetActive(on);
        if (body != null) foreach (var r in body) if (r != null) r.enabled = !on;
        if (fader == null) fader = view != null ? view.GetComponentInChildren<ScreenFader>(true) : null;
        if (fader == null && Camera.main != null) fader = ScreenFader.Ensure(Camera.main);
        if (fader != null) fader.SetVignette(on ? vignette : 0f);
        if (on) look = Quaternion.LookRotation(Flat(view.forward).sqrMagnitude > 1e-4f ? Flat(view.forward) : transform.forward, Vector3.up);
        watching = null;
        watchedFor = 0f;
        if (label != null) label.text = string.Empty;
    }

    /// <summary>확대 화면 한가운데에 머무르는 것의 이름을 띄운다.</summary>
    void Watch()
    {
        Vector3 eye = zoomCamera != null ? zoomCamera.transform.position : view.position;
        Vector3 dir = look * Vector3.forward;
        BinocularSubject best = null;
        float bestAngle = float.MaxValue;
        foreach (var s in BinocularSubject.All)
        {
            Vector3 to = s.Center - eye;
            float dist = to.magnitude;
            if (dist < 1f) continue;
            float angle = Vector3.Angle(dir, to);
            float allowance = centerDegrees + Mathf.Min(Mathf.Atan2(s.radius, dist) * Mathf.Rad2Deg, bodyDegreesMax);
            if (angle <= allowance && angle < bestAngle) { best = s; bestAngle = angle; }
        }

        if (best != watching) { watching = best; watchedFor = 0f; if (label != null) label.text = string.Empty; }
        if (watching == null) return;
        watchedFor += Time.deltaTime;
        if (watchedFor < dwell) return;
        if (label != null && label.text != watching.label) label.text = watching.label;
        if (!watching.spoken && !string.IsNullOrWhiteSpace(watching.line) && lobby != null)
        {
            watching.spoken = true;
            lobby.StartCoroutine(lobby.SayLine(watching.line));
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
