using UnityEngine;

/// <summary>
/// 손목에 붙는 작은 메뉴. 손목을 돌려 얼굴 쪽을 향하면 나타나고, 내리면 사라진다.
///
/// 왜 이 방식인가:
///  - 상자를 계속 눈앞에 띄워 두면 시야를 가리고 어수선하다.
///  - 그렇다고 허공에 버튼을 고정해 두면 그것대로 거슬리고, 고개를 돌리면 못 찾는다.
///  - 손목 메뉴는 필요할 때만 보이고 항상 손에 있어서 못 찾을 일이 없다.
///    VR 관람형 콘텐츠에서 흔히 쓰는 방식이다.
/// </summary>
public class WristMenu : MonoBehaviour
{
    [Header("참조")]
    public Transform anchor;              // 왼손 컨트롤러
    public Transform viewer;              // 카메라. 비면 Camera.main
    public CanvasGroup group;

    [Header("배치")]
    public Vector3 localOffset = new Vector3(0f, 0.06f, -0.04f);
    public float scale = 0.0006f;

    [Header("표시 조건")]
    [Range(10f, 90f)] public float showAngle = 60f;   // 손목이 얼굴 쪽을 향한 것으로 볼 각도
    public float fadeSpeed = 8f;

    // 시작 연출이 끝나기 전에는 뜨지 않는다.
    // 안 그러면 시작 패널과 같은 자리에 겹쳐 그려지고, 더 가까이 있어서 클릭까지 가로챈다.
    public bool armed;
    [HideInInspector] public bool suppressed;   // 큰 도감/설정 패널이 열려 있을 때 잠시 숨김

    float alpha;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (viewer == null)
        {
            Camera cam = Camera.main;
            if (cam != null) viewer = cam.transform;
        }
        if (group != null) { group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false; }
        alpha = 0f;
    }

    void Start()
    {
        RegisterOwnHandAsIgnored();
    }

    /// <summary>
    /// 메뉴가 붙어 있는 손(왼손)은 자기 손목 버튼을 누를 수 없게 한다.
    /// 손 콜라이더(반지름 6cm)와 손목 버튼 콜라이더가 항상 겹쳐 있어서, 메뉴가 사용자를 향해
    /// 회전하거나 추적이 잠깐 튈 때마다 OnTriggerEnter 가 다시 들어와 "다시 뽑기" 가 헛발화했다.
    /// 컨트롤러와 핸드 트래킹이 같이 있을 수 있으니 같은 쪽("Left "/"Right ") 형제도 함께 무시한다.
    /// </summary>
    void RegisterOwnHandAsIgnored()
    {
        if (anchor == null) return;

        var roots = new System.Collections.Generic.List<Transform> { anchor };
        string side = anchor.name.StartsWith("Left") ? "Left" : anchor.name.StartsWith("Right") ? "Right" : null;
        if (side != null && anchor.parent != null)
        {
            foreach (Transform sibling in anchor.parent)
                if (sibling != anchor && sibling.name.StartsWith(side)) roots.Add(sibling);
        }

        Transform[] arr = roots.ToArray();
        foreach (var poke in GetComponentsInChildren<PokeSelector>(true))
            poke.ignoreRoots = arr;
    }

    void LateUpdate()
    {
        if (group == null) return;

        bool visible = false;

        if (!armed || suppressed)
        {
            // 아직 시작 전. 완전히 숨기고 입력도 막는다.
            alpha = Mathf.MoveTowards(alpha, 0f, fadeSpeed * Time.deltaTime);
            group.alpha = alpha;
            if (group.interactable) group.interactable = false;
            if (group.blocksRaycasts) group.blocksRaycasts = false;
            return;
        }

        // 컨트롤러가 추적되지 않으면(에디터 게임뷰에 헤드셋이 없을 때) 손목에 붙일 수가 없다.
        // TrackedPoseDriver 는 추적이 없으면 리그 원점에 그대로 머무르므로 그것으로 판별한다.
        bool anchorTracked = anchor != null && anchor.localPosition.sqrMagnitude > 0.0001f;

        if (anchorTracked && viewer != null)
        {
            // 손목 위에 붙이고 사용자를 바라보게 한다.
            transform.position = anchor.TransformPoint(localOffset);
            Vector3 toViewer = viewer.position - transform.position;
            if (toViewer.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
            SetWorldScale(scale);

            // 컨트롤러 윗면이 얼굴을 향할 때만 보인다. 팔을 내리고 있으면 안 보인다.
            float dot = Vector3.Dot(anchor.up, toViewer.normalized);
            visible = dot > Mathf.Cos(showAngle * Mathf.Deg2Rad);
        }
        else if (viewer != null)
        {
            // 폴백 — 컨트롤러가 없거나 추적이 안 될 때. 에디터 게임뷰 테스트가 이 경로를 탄다.
            // 시야 정중앙과 왼쪽 정보 카드를 피해 오른쪽 아래에 둔다.
            Vector3 right = viewer.right; right.y = 0f;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right; else right.Normalize();

            transform.position = viewer.position + viewer.forward * 0.90f
                               + Vector3.down * 0.24f + right * 0.38f;
            transform.rotation = Quaternion.LookRotation(transform.position - viewer.position, Vector3.up);
            SetWorldScale(scale);
            visible = true;
        }

        SetVisible(visible);
    }

    /// <summary>
    /// 원하는 **월드** 크기가 나오도록 로컬 스케일을 정한다.
    /// 이 메뉴는 Camera Offset 자식이라, 리그 스케일이 1이 아니면
    /// localScale 을 그대로 대입할 때 실제 크기가 그 배율만큼 달라진다.
    /// </summary>
    void SetWorldScale(float world)
    {
        Transform parent = transform.parent;
        float parentScale = parent != null ? parent.lossyScale.x : 1f;
        if (Mathf.Abs(parentScale) < 0.0001f) parentScale = 1f;
        transform.localScale = Vector3.one * (world / parentScale);
    }

    void SetVisible(bool visible)
    {
        float target = visible ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
        group.alpha = alpha;

        bool on = alpha > 0.5f;
        if (group.interactable != on) group.interactable = on;
        if (group.blocksRaycasts != on) group.blocksRaycasts = on;
    }
}
