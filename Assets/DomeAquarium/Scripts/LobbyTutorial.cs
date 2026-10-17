using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// 로비 준비 단계 — 바다에 내려가기 전에 할 일을 직접 해 보며 배운다.
///
///  1. 이동:  나레이션이 끝나면 텔레포트(엄지 막대 앞)와 스냅 회전(좌우)을 켠다. 끌려가는 자동 걷기 대신 내가 고른 곳으로 간다.
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
    public GameObject locomotionRoot;              // XR 리그의 Locomotion — 텔레포트·스냅 회전만 켜 둔 상태로 저장돼 있다
    public TeleportationAnchor cabinAnchor;        // 엘리베이터 안 도착 지점. 개찰구가 열려야 켜진다
    public Transform cabinCenter;
    [Tooltip("0 장비대 · 1 연습 수조 · 2 엘리베이터 — 지금 할 일 위에만 뜬다")]
    public GameObject[] markers;

    [Header("안내 (자막·음성)")]
    [TextArea(2, 3)] public string moveLine = "엄지 막대를 앞으로 밀었다 놓으면, 가리킨 곳으로 이동해요.";
    [TextArea(2, 3)] public string pickUpLine = "먼저 장비대에서 관찰기를 집어 보세요.";
    [TextArea(2, 3)] public string practiceLine = "연습 수조의 물고기에 관찰기를 겨누고, 링이 다 찰 때까지 지켜보세요.";
    [TextArea(2, 3)] public string doneLine = "첫 기록 성공! 개찰구가 열렸어요. 엘리베이터 안으로 이동하세요.";
    [Tooltip("엘리베이터 칸 중심에서 이 거리 안으로 들어오면 탄 것으로 본다(텔레포트가 아닌 방법으로 들어온 경우)")]
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
        // 텔레포트 지점이 아니라 그냥 걸어 들어온 경우(룸스케일)도 탄 것으로 본다.
        if (Current != Step.Board || cabinCenter == null) return;
        Transform view = ResolveView();
        if (view == null) return;
        Vector3 d = view.position - cabinCenter.position;
        d.y = 0f;
        if (d.sqrMagnitude < boardRadius * boardRadius) Board();
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>엔딩의 밤 로비에서 다시 걸어 다니게 한다 (텔레포트·스냅 회전).</summary>
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
