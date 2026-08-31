using UnityEngine;

/// <summary>
/// 물고기 인스턴스 하나에 붙어서 BoidManager 가 넘겨준 속도를 시각 표현으로 바꾼다.
///
///  - Animator 파라미터 이름은 절대 하드코딩하지 않는다.
///    Layer Lab 계열 물고기 프리팹은 idle 클립 하나만 있고 파라미터가 없다. SetFloat("Speed") 같은 걸 부르면
///    "Parameter 'Speed' does not exist" 경고가 매 프레임 쏟아진다. 그래서 Animator.speed 만 조절한다.
///  - Animator 가 없거나 컨트롤러가 비어 있으면 조용히 넘어간다 (에러 로그 금지).
///  - 뱅킹(선회 시 롤)은 BoidManager 가 쓰는 루트 회전과 싸우지 않도록 별도 트랜스폼에 건다.
///    실행 순서: BoidManager.Update() 가 위치/회전(롤 없음)을 쓰고, 이 컴포넌트의 LateUpdate() 가 롤만 얹는다.
/// </summary>
public class FishAnimatorSync : MonoBehaviour
{
    [Header("애니메이션 속도")]
    public float speedToAnimSpeed = 1.2f;
    public float animSpeedMin = 0.35f;
    public float animSpeedMax = 2.5f;

    [Header("뱅킹")]
    public float bankAngle = 35f;
    public float bankSmooth = 4f;
    // 수평 각속도가 이 값(도/초)일 때 bankAngle 만큼 기운다.
    public float turnRateForFullBank = 120f;

    // 롤을 적용할 모델 트랜스폼. 비워 두면 첫 번째 자식을 자동으로 잡는다.
    // 자식이 아예 없으면 루트에 얹되, BoidManager 가 매 프레임 회전을 새로 써 준다는 전제로만 동작한다.
    public Transform modelRoot;

    Animator anim;
    bool hasAnimator;
    bool rollOnSelf;
    Quaternion baseLocalRot = Quaternion.identity;

    Vector3 velocity;
    Vector3 lastFlatDir;
    bool hasLastDir;
    bool fedThisFrame;
    bool everFed;
    float roll;
    float animSpeedNow = -1f;

    void Awake()
    {
        // 캐싱. Update/LateUpdate 에서는 절대 GetComponent 하지 않는다.
        anim = GetComponentInChildren<Animator>(true);
        hasAnimator = anim != null && anim.runtimeAnimatorController != null;

        if (modelRoot == null && transform.childCount > 0) modelRoot = transform.GetChild(0);
        rollOnSelf = (modelRoot == null || modelRoot == transform);
        if (!rollOnSelf) baseLocalRot = modelRoot.localRotation;

        // 조절할 Animator 도 없고 기울일 각도도 없으면 할 일이 없다. 조용히 꺼 둔다.
        if (!hasAnimator && Mathf.Abs(bankAngle) <= 0.01f) enabled = false;
    }

    /// <summary>BoidManager 가 매 틱 호출한다. 계산은 LateUpdate 에서 한 번에 처리.</summary>
    public void SetVelocity(Vector3 worldVelocity)
    {
        velocity = worldVelocity;
        fedThisFrame = true;
        everFed = true;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            fedThisFrame = false;
            return;
        }

        // ---------------- 애니메이션 재생 속도 ----------------
        if (hasAnimator && everFed)
        {
            float target = Mathf.Clamp(velocity.magnitude * speedToAnimSpeed, animSpeedMin, animSpeedMax);

            if (animSpeedNow < 0f) animSpeedNow = target;
            else animSpeedNow = Mathf.Lerp(animSpeedNow, target, 1f - Mathf.Exp(-8f * dt));

            // 네이티브 프로퍼티 쓰기를 줄이려고 유의미한 변화일 때만 대입한다.
            if (Mathf.Abs(anim.speed - animSpeedNow) > 0.01f) anim.speed = animSpeedNow;
        }

        // ---------------- 뱅킹 ----------------
        float targetRoll = 0f;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (flat.sqrMagnitude > 1e-6f)
        {
            flat.Normalize();

            if (hasLastDir)
            {
                // 직전 프레임 대비 수평 방향 변화 -> 각속도(도/초).
                // Vector3.SignedAngle(+Z, +X, +Y) = +90 이므로 "우선회가 +" 다.
                float deltaDeg = Vector3.SignedAngle(lastFlatDir, flat, Vector3.up);
                float angVel = deltaDeg / dt;
                float k = Mathf.Clamp(angVel / Mathf.Max(1f, turnRateForFullBank), -1f, 1f);

                // 로컬 +Z(전방) 축 양수 롤은 up 벡터를 로컬 -X(좌)로 눕힌다.
                // 즉 좌선회(-)일 때 양수 롤이 되어야 선회 "안쪽"으로 기운다. 그래서 부호를 뒤집는다.
                targetRoll = -k * bankAngle;
            }

            lastFlatDir = flat;
            hasLastDir = true;
        }

        // bankSmooth 로 감쇠 보간. 프레임레이트에 흔들리지 않게 exp 감쇠를 쓴다.
        roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-Mathf.Max(0.01f, bankSmooth) * dt));

        if (Mathf.Abs(bankAngle) > 0.01f)
        {
            if (rollOnSelf)
            {
                // 자식 모델이 없는 프리팹용 폴백.
                // BoidManager 가 Update 에서 "롤이 없는" 회전을 매 프레임 새로 쓰기 때문에 여기서 한 번 덧곱해도 누적되지 않는다.
                // 아무도 SetVelocity 를 안 먹여 주는 프레임(예: FishReveal 연출 중)에는 손대지 않아 빙글빙글 도는 사고를 막는다.
                if (fedThisFrame) transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, roll);
            }
            else
            {
                modelRoot.localRotation = baseLocalRot * Quaternion.Euler(0f, 0f, roll);
            }
        }

        fedThisFrame = false;
    }
}
