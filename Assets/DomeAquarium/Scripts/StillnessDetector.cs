using UnityEngine;

/// <summary>
/// "가만히 있음"을 잰다 (기획 문서 3막 — STILL). 머리와 손의 움직임을 부드럽게 평균 내어 두 단계로 판정한다.
///
///   가만히(IsStill)   머리 0.10m/s · 12°/s, 손 0.20m/s 아래 — 이 상태가 이어진 시간이 StillSeconds
///   움직임(IsMoving)  머리 0.22m/s · 30°/s, 손 0.50m/s 를 넘는 상태가 0.4초 이어짐 — 생물이 물러난다
///
/// 두 기준 사이(살짝 꼼지락)는 어느 쪽도 아니다. 관찰기를 생물 쪽으로 천천히 겨누는 손짓까지 "움직임"으로 치면
/// 기록할 수가 없다. 아이들은 완전히 굳어 있지 못하므로 문턱을 넉넉히 잡았다(헤드셋에서 다시 맞출 값).
/// 추적되지 않는 손(리그 원점에 머문다)은 세지 않는다.
/// </summary>
public sealed class StillnessDetector : MonoBehaviour
{
    public Transform head;                     // 비면 Camera.main
    public Transform[] hands;

    [Header("가만히")]
    public float headStillSpeed = 0.10f;
    public float headStillTurn = 12f;
    public float handStillSpeed = 0.20f;

    [Header("움직임")]
    public float headMoveSpeed = 0.22f;
    public float headMoveTurn = 30f;
    public float handMoveSpeed = 0.50f;
    public float moveHold = 0.4f;

    [Header("평균")]
    public float smoothing = 0.25f;            // 초 — 한 프레임 튐(추적 흔들림)에 휘둘리지 않게

    public float StillSeconds { get; private set; }
    public bool IsStill { get; private set; }
    public bool IsMoving { get; private set; }
    public float HeadSpeed => headSpeed;
    public float HeadTurn => headTurn;
    public float HandSpeed => handSpeed;

    Vector3 lastHeadPos;
    Quaternion lastHeadRot;
    Vector3[] lastHandPos;
    float headSpeed, headTurn, handSpeed;
    float overFor;
    bool primed;

#if UNITY_EDITOR
    // 헤드셋 없이 "움직였다"를 흉내 낸다 (디버그 메뉴). 에디터에서는 머리가 늘 멈춰 있다.
    [System.NonSerialized] public float debugMoveUntil;
#endif

    void OnEnable()
    {
        primed = false;
        StillSeconds = 0f;
        IsMoving = false;
    }

    void Update()
    {
        Transform h = ResolveHead();
        if (h == null) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        if (hands != null && (lastHandPos == null || lastHandPos.Length != hands.Length)) { lastHandPos = new Vector3[hands.Length]; primed = false; }

        if (!primed)
        {
            lastHeadPos = h.position;
            lastHeadRot = h.rotation;
            if (hands != null) for (int i = 0; i < hands.Length; i++) if (hands[i] != null) lastHandPos[i] = hands[i].position;
            primed = true;
            return;
        }

        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothing));
        headSpeed = Mathf.Lerp(headSpeed, Vector3.Distance(h.position, lastHeadPos) / dt, k);
        headTurn = Mathf.Lerp(headTurn, Quaternion.Angle(h.rotation, lastHeadRot) / dt, k);
        lastHeadPos = h.position;
        lastHeadRot = h.rotation;

        float fastestHand = 0f;
        if (hands != null)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                Transform t = hands[i];
                if (t == null) continue;
                Vector3 p = t.position;
                bool tracked = t.gameObject.activeInHierarchy && t.localPosition.sqrMagnitude > 1e-4f;
                if (tracked) fastestHand = Mathf.Max(fastestHand, Vector3.Distance(p, lastHandPos[i]) / dt);
                lastHandPos[i] = p;
            }
        }
        handSpeed = Mathf.Lerp(handSpeed, fastestHand, k);

        bool still = headSpeed < headStillSpeed && headTurn < headStillTurn && handSpeed < handStillSpeed;
        bool over = headSpeed > headMoveSpeed || headTurn > headMoveTurn || handSpeed > handMoveSpeed;
#if UNITY_EDITOR
        if (Time.time < debugMoveUntil) { still = false; over = true; }
#endif
        overFor = over ? overFor + dt : 0f;
        IsMoving = overFor >= moveHold;
        IsStill = still;
        StillSeconds = still ? StillSeconds + dt : 0f;
    }

    Transform ResolveHead()
    {
        if (head != null) return head;
        Camera cam = Camera.main;
        if (cam != null) head = cam.transform;
        return head;
    }
}
