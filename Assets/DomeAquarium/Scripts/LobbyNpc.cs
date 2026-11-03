using UnityEngine;

/// <summary>
/// 로비의 사람(NPC)의 몸짓. 가까이 오면(greetRadius) 멈춰 서서 몸을 조금 돌려 바라보고 손을 흔든다. 멀어지면 하던 일로 돌아간다.
/// 말은 NpcDialogue 가 한다 — 대사를 시작할 때 Gesture("Talk") 로 끄덕이게 한다. 애니메이터에 그 트리거가 없으면 조용히 넘어간다.
///
/// 거닐기(patrol): 지점이 둘 이상이면 차례로 걸어가고, 닿으면 잠깐(pause) 쉬었다가 다음으로 간다. 걷는 동안 애니메이터 "Walking" 을 켠다.
/// 고개: 애니메이션이 끝난 뒤(LateUpdate) 머리 뼈를 몸 기준 좌우 · 위아래로 더 돌린다 — 뼈 축이 모델마다 달라도 되게
/// 몸의 축으로 만든 회전을 월드에서 얹는다. 휴머노이드는 머리 뼈를 스스로 찾는다.
/// </summary>
public sealed class LobbyNpc : MonoBehaviour
{
    public Animator animator;
    public Transform head;                              // 고개를 돌릴 뼈 (휴머노이드면 비워도 된다)

    [Header("거리")]
    public float greetRadius = 2.8f;
    public float leaveRadius = 4.2f;                    // 이보다 멀어져야 다시 인사한다

    [Header("돌아보기")]
    public float turnSpeed = 80f;                       // °/s
    public float maxBodyYaw = 65f;                      // 서 있던 방향에서 몸을 이만큼까지만 돌린다
    public float headYawMax = 55f;
    public float headPitchMax = 25f;

    [Header("거닐기 (비우면 제자리)")]
    public Vector3[] patrol;                            // 부모 기준 지점
    public float walkSpeed = 0.8f;
    public Vector2 pause = new Vector2(3f, 6f);

    static readonly int WalkingId = Animator.StringToHash("Walking");

    Transform view;
    Quaternion rest;
    bool near;
    float headWeight;
    int leg;
    float waitUntil;
    bool walking;

    public bool Near => near;

    void Start()
    {
        rest = transform.rotation;
        if (head == null && animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void OnDisable()
    {
        near = false;
        headWeight = 0f;
        walking = false;
    }

    /// <summary>애니메이터 트리거 — "Wave" 손 흔들기, "Talk" 말할 때 끄덕이기. 없는 트리거는 건너뛴다.</summary>
    public void Gesture(string trigger)
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        foreach (var p in animator.parameters)
            if (p.name == trigger && p.type == AnimatorControllerParameterType.Trigger) { animator.SetTrigger(trigger); return; }
    }

    void Update()
    {
        if (view == null) { var cam = Camera.main; if (cam == null) return; view = cam.transform; }

        Vector3 to = view.position - transform.position;
        to.y = 0f;
        float d = to.magnitude;
        bool inRange = d < (near ? leaveRadius : greetRadius);
        if (inRange && !near) { near = true; SetWalking(false); Gesture("Wave"); }
        else if (!inRange && near) near = false;

        if (near)
        {
            Quaternion target = d > 0.3f ? Quaternion.RotateTowards(rest, Quaternion.LookRotation(to, Vector3.up), maxBodyYaw) : rest;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }
        else if (patrol != null && patrol.Length > 1) Patrol();
        else transform.rotation = Quaternion.RotateTowards(transform.rotation, rest, turnSpeed * Time.deltaTime);

        headWeight = Mathf.MoveTowards(headWeight, near ? 1f : 0f, Time.deltaTime * 2f);
    }

    void Patrol()
    {
        if (Time.time < waitUntil) { SetWalking(false); return; }
        Vector3 goal = transform.parent != null ? transform.parent.TransformPoint(patrol[leg]) : patrol[leg];
        Vector3 to = goal - transform.position;
        to.y = 0f;
        if (to.magnitude < 0.1f)
        {
            leg = (leg + 1) % patrol.Length;
            waitUntil = Time.time + Random.Range(pause.x, pause.y);
            SetWalking(false);
            return;
        }
        Quaternion face = Quaternion.LookRotation(to, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, face, 160f * Time.deltaTime);
        rest = face;                                    // 멈춰서 돌아볼 때의 기준 방향
        bool facing = Quaternion.Angle(transform.rotation, face) < 25f;
        SetWalking(facing);
        if (facing) transform.position += transform.forward * Mathf.Min(walkSpeed * Time.deltaTime, to.magnitude);
    }

    void SetWalking(bool on)
    {
        if (walking == on || animator == null || !animator.isActiveAndEnabled) { walking = on; return; }
        walking = on;
        foreach (var p in animator.parameters)
            if (p.nameHash == WalkingId) { animator.SetBool(WalkingId, on); return; }
    }

    void LateUpdate()
    {
        if (head == null || view == null || headWeight <= 0f) return;
        Vector3 local = transform.InverseTransformDirection((view.position - head.position).normalized);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -headYawMax, headYawMax);
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -headPitchMax, headPitchMax);
        Quaternion offset = transform.rotation * Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Inverse(transform.rotation);
        head.rotation = Quaternion.Slerp(Quaternion.identity, offset, headWeight) * head.rotation;
    }
}
