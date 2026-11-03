using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// 로비 준비 단계 — 바다에 내려가기 전에 할 일을 직접 해 보며 배운다.
///
///  1. 이동:  나레이션이 끝나면 왼쪽 막대 걷기 · 오른쪽 막대 부드러운 회전을 켠다. 순간이동은 없다 — 해변에서 관측소까지 직접 걷는다.
///            끌려가는 자동 걷기 대신 내가 고른 곳으로 간다. 로비 벽에는 충돌체가 없어 걸을 수 있는 곳 밖으로는 못 나가게 막는다.
///  2. 장비:  장비대에서 관찰기를 집는다 → 오른손에 관찰기가 들린다.
///  3. 연습:  연습 수조의 물고기를 겨눠 첫 기록을 한다 (이미 기록한 종이어도 연습에서는 다시 기록된다).
///  4. 탑승:  개찰구·엘리베이터 문이 열리고, 엘리베이터 안으로 이동하면 문이 닫히고 내려간다.
///
/// 처음 쓰는 사람에게 한 번에 동작 하나만 가르치고, 해내야 다음으로 넘어간다 (Meta 온보딩 가이드).
/// 시간 제한은 두지 않는다.
/// </summary>
public sealed class LobbyTutorial : MonoBehaviour
{
    public enum Step { Intro, PickUpDevice, Practice, Board, Riding }

    [Header("참조")]
    public LobbySequence lobby;
    public ObservationAim aim;                     // 비면 찾는다 (관찰기 적용 메뉴가 로비보다 나중에 돌 수 있다)
    public PracticeTank practiceTank;
    public XRSimpleInteractable devicePickup;      // 장비대 위 관찰기 — 쥐기·트리거로 집는다
    public PokeSelector devicePoke;                // 손으로 건드리기·에디터 마우스
    public GameObject deviceOnTable;
    public GameObject deviceInHand;                // 오른손에 붙는 관찰기 모델
    public GameObject locomotionRoot;              // XR 리그의 Locomotion — 걷기·부드러운 회전만 켜 둔 상태로 저장돼 있다
    public TeleportationAnchor cabinAnchor;        // 엘리베이터 안 도착 지점(바닥 빛). 개찰구가 열려야 켜진다
    public Transform cabinCenter;
    [Tooltip("0 장비대 · 1 연습 수조 · 2 엘리베이터 — 지금 할 일 위에만 뜬다")]
    public GameObject[] markers;

    [Header("걸을 수 있는 곳 (로비 로컬 x·z, 빌더가 채운다)")]
    public Rect hallArea;                          // 홀 안쪽. 비어 있으면 막지 않는다
    [Tooltip("개찰구가 열리기 전에는 이 z 를 넘지 못한다")]
    public float gateLineZ = float.PositiveInfinity;
    [Tooltip("개찰구가 열린 뒤 아치 → 엘리베이터 칸 통로")]
    public Rect cabinArea;
    [Tooltip("언제나 걸을 수 있는 곳 — 테라스 문 · 테라스 데크 · 해변 · 계단")]
    public Rect[] openAreas;

    [Header("땅 높이 (해변 · 계단, 빌더가 채운다)")]
    [Tooltip("이 안에서는 지형 높이를 따라 걷는다 (로비 로컬 x·z). 그 밖은 로비 바닥(0)")]
    public Rect[] groundAreas;
    public Terrain ground;
    [Tooltip("계단 — stairTopZ(높이 0)에서 stairBottomZ(높이 stairBottomY)까지 고르게 내려간다")]
    public Rect stairArea;
    public float stairTopZ, stairBottomZ, stairBottomY;

    public const string DefaultMoveLine = "왼쪽 막대로 걷고, 오른쪽 막대로 몸을 돌려요.";

    [Header("안내 (자막·음성)")]
    [TextArea(2, 3)] public string moveLine = DefaultMoveLine;
    [TextArea(2, 3)] public string pickUpLine = "먼저 장비대에서 관찰기를 집어 보세요.";
    [TextArea(2, 3)] public string practiceLine = "연습 수조의 물고기에 관찰기를 겨누고, 링이 다 찰 때까지 지켜보세요.";
    [TextArea(2, 3)] public string doneLine = "첫 기록 성공! 개찰구가 열렸어요. 엘리베이터 안으로 이동하세요.";
    [Tooltip("엘리베이터 칸 중심에서 이 거리 안으로 걸어 들어오면 탄 것으로 본다")]
    public float boardRadius = 0.7f;

    public Step Current { get; private set; } = Step.Intro;

    Coroutine prompt;
    Transform viewCache;

    void Start()
    {
        if (aim == null) aim = FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        if (aim != null)
        {
            aim.practiceTank = practiceTank;
            aim.armed = false;                     // 관찰기를 집기 전에는 겨누지 않는다
            aim.Recorded += OnRecorded;
        }

        if (devicePickup != null) devicePickup.selectEntered.AddListener(_ => PickUp());
        if (devicePoke != null) devicePoke.onPoked.AddListener(PickUp);
        if (cabinAnchor != null)
        {
            cabinAnchor.teleporting.AddListener(_ => Board());
            cabinAnchor.gameObject.SetActive(false);
        }
        if (deviceInHand != null) deviceInHand.SetActive(false);
        if (deviceOnTable != null) deviceOnTable.SetActive(true);
        SetLocomotion(false);
        ShowMarker(-1);
    }

    void OnDestroy()
    {
        if (aim != null) aim.Recorded -= OnRecorded;
    }

    /// <summary>로비 나레이션이 끝나면 LobbySequence 가 부른다.</summary>
    public void Begin()
    {
        if (Current != Step.Intro) return;
        Current = Step.PickUpDevice;
        SetLocomotion(true);
        ShowMarker(0);
        Prompt(moveLine, pickUpLine);
    }

    public void PickUp()
    {
        if (Current != Step.PickUpDevice) return;
        Current = Step.Practice;

        if (deviceOnTable != null) deviceOnTable.SetActive(false);
        if (deviceInHand != null) deviceInHand.SetActive(true);
        if (aim != null)
        {
            aim.practiceMode = true;
            aim.armed = true;
        }
        ShowMarker(1);
        Prompt(practiceLine);
    }

    void OnRecorded(FishData fish)
    {
        if (Current != Step.Practice) return;
        Current = Step.Board;

        if (aim != null) aim.practiceMode = false;
        lobby?.OpenGateAndDoors();
        if (cabinAnchor != null) cabinAnchor.gameObject.SetActive(true);
        ShowMarker(2);
        Prompt(doneLine);
    }

    public void Board()
    {
        if (Current != Step.Board) return;
        Current = Step.Riding;

        ShowMarker(-1);
        if (cabinAnchor != null) cabinAnchor.gameObject.SetActive(false);
        if (prompt != null) StopCoroutine(prompt);
        // 텔레포트 요청은 이 프레임 끝에 처리된다 — 이동이 끝난 다음 프레임에 이동 장치를 끈다.
        StartCoroutine(LockLocomotionNextFrame());
        lobby?.Ride();
    }

    IEnumerator LockLocomotionNextFrame()
    {
        yield return null;
        SetLocomotion(false);
    }

    void Update()
    {
        // 걸어서(막대 · 룸스케일) 칸 한가운데에 들어오면 탄 것으로 본다.
        if (Current != Step.Board || cabinCenter == null) return;
        Transform view = ResolveView();
        if (view == null) return;
        Vector3 d = view.position - cabinCenter.position;
        d.y = 0f;
        if (d.sqrMagnitude < boardRadius * boardRadius) Board();
    }

    void LateUpdate()
    {
        // 이동 제공자가 Update 에서 리그를 옮긴 뒤, 화면에 그리기 전에 걸을 수 있는 곳 안으로 되돌린다.
        // 이동 장치가 꺼진 동안(엘리베이터·수중 돔)은 리그를 스크립트가 옮기므로 건드리지 않는다.
        if (locomotionRoot == null || !locomotionRoot.activeInHierarchy) return;
        if (hallArea.width <= 0f || lobby == null || lobby.rig == null) return;
        Transform view = ResolveView();
        if (view == null) return;

        Vector3 local = transform.InverseTransformPoint(view.position);
        var at = new Vector2(local.x, local.z);
        Vector2 inside = ClampToWalkable(at);
        if (inside != at)
        {
            Vector3 shift = transform.TransformPoint(new Vector3(inside.x, local.y, inside.y)) - view.position;
            shift.y = 0f;
            lobby.rig.position += shift;
        }

        // 리그에는 중력 · 지면 충돌이 없다(평면으로만 움직인다) — 해변 · 계단에서는 발밑 높이를 직접 따라간다.
        float floor = transform.TransformPoint(new Vector3(0f, FloorAt(inside), 0f)).y;
        Vector3 rig = lobby.rig.position;
        if (Mathf.Abs(rig.y - floor) > 0.001f)
        {
            rig.y = Mathf.MoveTowards(rig.y, floor, Time.deltaTime * 4f);
            lobby.rig.position = rig;
        }
    }

    /// <summary>로비 로컬 x·z 의 발밑 높이 (로비 로컬 y). 홀 · 데크는 0, 계단은 고르게, 해변은 지형.</summary>
    public float FloorAt(Vector2 p)
    {
        if (stairArea.width > 0f && stairArea.Contains(p) && !Mathf.Approximately(stairTopZ, stairBottomZ))
            return Mathf.Lerp(0f, stairBottomY, Mathf.InverseLerp(stairTopZ, stairBottomZ, p.y));
        if (ground != null && groundAreas != null)
            foreach (Rect r in groundAreas)
                if (r.Contains(p))
                {
                    Vector3 w = transform.TransformPoint(new Vector3(p.x, 0f, p.y));
                    float h = ground.SampleHeight(w) + ground.transform.position.y;
                    return transform.InverseTransformPoint(new Vector3(w.x, h, w.z)).y;
                }
        return 0f;
    }

    Vector2 ClampToWalkable(Vector2 p)
    {
        bool gateOpen = Current >= Step.Board;       // 엔딩의 밤 로비도 Riding 이라 열린 쪽으로 본다
        Rect hall = hallArea;
        if (!gateOpen) hall.yMax = Mathf.Min(hall.yMax, gateLineZ);
        Vector2 best = Clamp(hall, p);
        if (gateOpen && cabinArea.width > 0f) best = Closer(p, best, Clamp(cabinArea, p));
        if (openAreas != null)
            foreach (Rect r in openAreas)
                if (r.width > 0f) best = Closer(p, best, Clamp(r, p));
        return best;
    }

    static Vector2 Closer(Vector2 p, Vector2 a, Vector2 b) => (a - p).sqrMagnitude <= (b - p).sqrMagnitude ? a : b;

    static Vector2 Clamp(Rect r, Vector2 p) => new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));

    // ─────────────────────────────────────────────────────────────

    /// <summary>엔딩의 밤 로비에서 다시 걸어 다니게 한다 (걷기·부드러운 회전).</summary>
    public void EnableLocomotion(bool on) => SetLocomotion(on);

    void SetLocomotion(bool on)
    {
        if (locomotionRoot != null && locomotionRoot.activeSelf != on) locomotionRoot.SetActive(on);
    }

    void ShowMarker(int index)
    {
        if (markers == null) return;
        for (int i = 0; i < markers.Length; i++)
            if (markers[i] != null) markers[i].SetActive(i == index);
    }

    void Prompt(params string[] lines)
    {
        if (lobby == null) return;
        if (prompt != null) StopCoroutine(prompt);
        prompt = StartCoroutine(PromptRoutine(lines));
    }

    IEnumerator PromptRoutine(string[] lines)
    {
        foreach (string line in lines)
        {
            yield return lobby.SayLine(line);
            yield return new WaitForSeconds(0.3f);
        }
        prompt = null;
    }

    Transform ResolveView()
    {
        if (viewCache != null) return viewCache;
        Camera cam = Camera.main;
        if (cam != null) viewCache = cam.transform;
        return viewCache;
    }
}
