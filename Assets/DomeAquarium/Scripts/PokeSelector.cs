using UnityEngine;
using UnityEngine.Events;

// 손으로 찔러서 누르는 주 입력. isTrigger 콜라이더가 필요하다.
// mysteryBox 가 null 이어도 동작한다 — 시작 버튼은 onPoked 만 쓴다.
public class PokeSelector : MonoBehaviour
{
    public MysteryBox mysteryBox;          // null 허용
    public string handTag = "Hand";
    public float inputCooldown = 1.5f;

    // 나중에 컨트롤러 진동/사운드를 붙일 수 있게 항상 public UnityEvent
    public UnityEvent onPoked = new UnityEvent();

    private float lastFireTime = -999f;
    private bool tagCheckEnabled;          // 태그가 프로젝트에 없으면 false → 모든 접촉 허용

    private void Start()
    {
        tagCheckEnabled = IsTagDefined(handTag);

        if (!tagCheckEnabled)
        {
            Debug.LogWarning(
                "[PokeSelector] 태그 '" + handTag + "' 가 프로젝트에 없다. 태그 검사를 건너뛰고 모든 접촉을 입력으로 받는다.",
                this);
        }

        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning("[PokeSelector] Collider 가 없다. OnTriggerEnter 가 호출되지 않는다.", this);
        }
        // isTrigger 가 꺼져 있어도 상관없다 — 손 콜라이더가 트리거 + 키네마틱 리지드바디라
        // "Static Collider vs Kinematic Rigidbody Trigger" 조합으로 OnTriggerEnter 가 양쪽에 온다.
        // 오히려 solid 여야 컨트롤러 레이(Physics.Raycast)가 이 오브젝트를 맞힐 수 있다.
    }

    // 정의되지 않은 태그로 CompareTag 를 부르면 예외가 나므로 미리 안전하게 확인한다.
    private bool IsTagDefined(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;

        try
        {
            gameObject.CompareTag(tag);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 게임뷰에서 마우스로 눌러 볼 수 있게 한다.
    /// 헤드셋에서는 손으로 찌르거나 컨트롤러 레이로 누르지만, 그 둘 다 마우스로는 흉내 낼 수 없어서
    /// 에디터 테스트가 아예 불가능했다. 빌드에는 포함되지 않는다.
    /// </summary>
    private void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Collide)) return;
        if (hit.transform != transform && !hit.transform.IsChildOf(transform)) return;

        Fire();
    }
#endif

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        if (tagCheckEnabled && !other.CompareTag(handTag)) return;

        Fire();
    }

    /// <summary>쿨다운을 지키며 입력을 발화한다. 찌르기와 에디터 마우스가 공유한다.</summary>
    private void Fire()
    {
        // Time.unscaledTime 기준 — 연출 중 타임스케일이 바뀌어도 쿨다운이 흔들리지 않는다.
        float now = Time.unscaledTime;
        if (now - lastFireTime < inputCooldown) return;
        lastFireTime = now;

        if (mysteryBox != null) mysteryBox.Trigger();

        if (onPoked != null) onPoked.Invoke();
    }
}
