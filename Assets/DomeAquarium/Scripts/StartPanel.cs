using System.Collections;
using UnityEngine;

// 시작 안내 패널.
// 누르기 전까지 플레이어 정면(distance, 기본 0.75m)을 부드럽게 따라다닌다 — 고개를 돌려도 잃어버리지 않게.
// 시작 버튼(Button.onClick / PokeSelector.onPoked)이 BeginExperience() 를 호출하면
// 물속으로 가라앉듯 아래로 내려가면서 알파가 0 이 된다.
// 연출이 끝나면 MysteryBox.Summon() 으로 상자를 부르고, 손목 메뉴를 활성화한 뒤 자기 자신을 끈다.
// 육지 로비(LobbySequence)가 있으면 "입장하기" 패널로 쓰인다 — 나레이션 뒤에 나타나고, 누르면 로비가 이어 받는다.
public class StartPanel : MonoBehaviour
{
    [Header("참조")]
    public CanvasGroup canvasGroup;
    public GameObject mysteryBoxRoot;      // 시작 전 비활성, 시작 연출이 끝나면 활성
    public Transform player;               // 비어 있으면 Start 에서 Camera.main 으로 대체

    public Transform revealStage;          // 물고기가 다가와 멈추는 지점. 박스와 같은 방향으로 맞춘다
    public WristMenu wristMenu;            // 시작 연출이 끝난 뒤에야 켠다

    // 관찰기(상자 대신 헤엄치는 생물을 겨눠 기록). 있으면 수중 돔 도착 시 켠다.
    // 관찰기를 쓰는 씬은 mysteryBoxRoot 를 비워 상자를 부르지 않는다.
    public ObservationAim observationAim;

    // 육지 로비에서 시작하는 경우. 비어 있으면 예전처럼 수조 안에서 바로 시작한다.
    // 로비가 있으면: 나레이션이 끝나야 패널이 나타나고(RevealForLobby), "입장하기"를 누르면
    // 걷기·엘리베이터·하강을 로비가 맡은 뒤 도착하면 CompleteFromLobby 로 상자를 부른다.
    public LobbySequence lobby;

    [Header("배치")]
    public float distance = 1.35f;         // 플레이어로부터의 수평 거리
    public float boxDistance = 0.85f;      // MysteryBox 기본 배치와 같은 값(레거시 씬 호환)
    public float boxDrop = 0.10f;
    public float stageDistance = 1.35f;
    public float stageDrop = 0.02f;
    public float followSharpness = 4f;     // 클수록 빨리 따라온다. 너무 크면 헤드락이 되어 멀미가 난다
    public float eyeDrop = 0.18f;          // 눈높이보다 살짝 아래로 내려 배치

    [Header("가라앉는 연출")]
    public float sinkDistance = 2.5f;      // 아래로 내려가는 총 거리
    public float sinkTime = 1.6f;          // 연출 길이
    public float sinkBackAway = 0.35f;     // 가라앉으며 아주 약간 뒤로 멀어지는 양

    // --- 상태 (재진입 방지) ---
    private bool began;                    // BeginExperience 가 이미 호출됐는가
    private bool finished;                 // 마무리 처리가 이미 끝났는가
    private bool revealed;                 // 로비 나레이션이 끝나 패널이 보이는가

    bool UsesLobby => lobby != null && lobby.isActiveAndEnabled;

    // --- 캐시 (Update / 코루틴 안에서 GetComponent·Find 금지) ---
    private Transform tr;
    private Vector3 awayDir = Vector3.forward;   // 플레이어 -> 패널 방향(수평), 패널의 정면 축

    private void Awake()
    {
        tr = transform;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        if (player == null)
        {
            Camera cam = Camera.main;
            if (cam != null) player = cam.transform;
        }

        PlaceInFrontOfPlayer();

        // 로비에서는 나레이션을 듣는 동안 패널이 앞을 가리지 않게 숨겨 둔다.
        revealed = !UsesLobby;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = revealed ? 1f : 0f;
            canvasGroup.interactable = revealed;
            canvasGroup.blocksRaycasts = revealed;
        }

        // 시작 전에는 미스터리 박스를 숨겨 둔다.
        if (mysteryBoxRoot != null) mysteryBoxRoot.SetActive(false);
    }

    // 시작 패널은 누르기 전까지 **계속 플레이어를 따라다닌다**.
    //
    // 한 번만 놓으면 절대 안 된다:
    //  - Quest 는 세션 시작 직후 몇 프레임 동안 헤드 포즈를 원점/항등으로 보고한다 → 발밑에 깔린다.
    //  - 그 뒤 사용자가 고개를 돌리거나 자리를 옮기면 패널은 옆/뒤에 남아 영영 못 찾는다.
    //    (실측: 카메라가 x=-1.17 로 옮겨갔는데 패널은 x=0 에 남아 있었다)
    // 이 앱은 이동이 없어서 걸어가 볼 수도 없으므로, 따라오게 만드는 것이 유일하게 안전하다.
    private void Update()
    {
        if (began) return;

        if (player == null)
        {
            Camera cam = Camera.main;
            if (cam != null) player = cam.transform;
            if (player == null) return;
        }

        ComputeTarget(out Vector3 targetPos, out Quaternion targetRot);

        // 부드럽게 따라간다. 딱 붙여 놓으면 헤드락이 되어 멀미가 나고,
        // 너무 느리면 "안 따라온다"고 느낀다. 이 정도가 UI 패널로 흔히 쓰는 값이다.
        float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        tr.position = Vector3.Lerp(tr.position, targetPos, k);
        tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, k);
    }

    private void ComputeTarget(out Vector3 pos, out Quaternion rot)
    {
        Vector3 origin = player.position;
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        else forward.Normalize();

        awayDir = forward;
        pos = origin + forward * distance + Vector3.down * eyeDrop;
        rot = Quaternion.LookRotation(forward, Vector3.up);
    }

    // 플레이어 정면(수평) distance 만큼 앞, 눈높이보다 eyeDrop 만큼 아래.
    private void PlaceInFrontOfPlayer()
    {
        Vector3 origin = Vector3.zero;
        Vector3 forward = Vector3.forward;

        if (player != null)
        {
            origin = player.position;
            forward = player.forward;
        }

        // 위/아래를 쳐다보는 상태에서도 패널이 기울지 않도록 수평 성분만 사용한다.
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        else forward.Normalize();

        awayDir = forward;

        tr.position = origin + forward * distance + Vector3.down * eyeDrop;
        // 패널의 +Z 가 플레이어 반대쪽을 향해야 캔버스 앞면이 플레이어를 본다.
        tr.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    /// <summary>로비 나레이션이 끝나면 LobbySequence 가 부른다. "입장하기" 패널을 서서히 띄운다.</summary>
    public void RevealForLobby()
    {
        if (revealed || began) return;
        revealed = true;
        if (isActiveAndEnabled) StartCoroutine(FadeCanvas(1f, 0.6f, true));
        else if (canvasGroup != null) { canvasGroup.alpha = 1f; canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }
    }

    /// <summary>엘리베이터가 수중 돔에 도착하면 LobbySequence 가 부른다. 배경음악·상자·손목 메뉴를 켠다.</summary>
    public void CompleteFromLobby()
    {
        AquariumAudioDirector.Instance?.BeginExperience();
        FinishSequence();
    }

    // 시작 버튼이 호출한다. 두 번 눌려도 안전.
    public void BeginExperience()
    {
        // 찌르기(PokeSelector)는 캔버스가 숨어 있어도 트리거로 들어온다 — 나레이션 중에는 무시한다.
        if (!revealed) return;
        if (began) return;
        began = true;

        if (UsesLobby)
        {
            // 로비에서는 물거품도, 배경음악도 아직 아니다. 패널만 조용히 사라지고 로비가 이어 받는다.
            if (canvasGroup != null) { canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
            if (isActiveAndEnabled) StartCoroutine(FadeCanvas(0f, 0.4f, false));
            else if (canvasGroup != null) canvasGroup.alpha = 0f;
            lobby.BeginEntry();
            return;
        }

        PlayStartBubbleBurst();
        AquariumAudioDirector.Instance?.BeginExperience();

        if (canvasGroup != null)
        {
            // 연출 중에는 더 이상 입력을 받지 않는다.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // 비활성 상태에서는 코루틴을 돌릴 수 없으므로 즉시 마무리한다.
        if (!isActiveAndEnabled)
        {
            FinishSequence();
            return;
        }

        StartCoroutine(SinkRoutine());
    }

    void PlayStartBubbleBurst()
    {
        if (mysteryBoxRoot == null) return;

        MysteryBox box = mysteryBoxRoot.GetComponent<MysteryBox>();
        if (box == null || box.burstFx == null) return;

        // 비활성 상태인 상자 안의 거품 파티클을 월드 루트로 복제한다.
        // 시작 패널이 곧 꺼져도 거품은 남아 자연스럽게 위로 떠오른다.
        Vector3 origin = tr != null ? tr.position - Vector3.up * 0.06f : transform.position;
        GameObject fxObject = Instantiate(box.burstFx.gameObject, origin, Quaternion.identity);
        fxObject.name = "StartBubbleBurst_Runtime";
        fxObject.transform.SetParent(null, true);
        fxObject.transform.localScale = Vector3.one;
        fxObject.SetActive(true);

        ParticleSystem particles = fxObject.GetComponent<ParticleSystem>();
        if (particles == null)
        {
            Destroy(fxObject);
            return;
        }

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.Play(true);

        ParticleSystem.MainModule main = particles.main;
        float longestLifetime = main.startLifetime.constantMax;
        Destroy(fxObject, main.duration + longestLifetime + 0.25f);
    }

    private IEnumerator SinkRoutine()
    {
        float duration = Mathf.Max(0.01f, sinkTime);

        Vector3 from = tr.position;
        Vector3 to = from + Vector3.down * sinkDistance + awayDir * sinkBackAway;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.Clamp01(t);

            // ease-out: 처음에 쑥 내려가고 점점 느려진다.
            // 물의 저항을 받으며 감속해 가라앉는 느낌. (ease-in 이면 공중에서 떨어지는 느낌이 난다)
            float ease = 1f - (1f - k) * (1f - k);

            tr.position = Vector3.LerpUnclamped(from, to, ease);

            // 알파는 SmoothStep 으로 — 초반에 잠깐 읽히다가 중반부터 빠르게 사라진다.
            if (canvasGroup != null) canvasGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, k);

            yield return null;
        }

        FinishSequence();
    }

    private IEnumerator FadeCanvas(float target, float seconds, bool interactiveAtEnd)
    {
        if (canvasGroup == null) yield break;
        float from = canvasGroup.alpha;
        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, seconds);
            canvasGroup.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        canvasGroup.alpha = target;
        canvasGroup.interactable = interactiveAtEnd;
        canvasGroup.blocksRaycasts = interactiveAtEnd;
    }

    // 미스터리 박스를 켜고 패널을 끈다. 여러 번 불려도 한 번만 동작.
    private void FinishSequence()
    {
        if (finished) return;
        finished = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (mysteryBoxRoot != null)
        {
            // 배치와 활성화는 MysteryBox.Summon() 이 담당한다.
            // 손목 메뉴에서 다시 부를 때도 같은 경로를 타야 위치가 어긋나지 않는다.
            var box = mysteryBoxRoot.GetComponent<MysteryBox>();
            if (box != null) box.Summon();
            else mysteryBoxRoot.SetActive(true);
        }

        // 이제부터 손목 메뉴를 쓸 수 있다. 시작 전에 켜 두면 시작 패널과 겹치고 클릭까지 가로챈다.
        if (wristMenu != null) wristMenu.armed = true;
        if (observationAim != null) observationAim.armed = true;

        // 마지막에 자기 자신을 끈다 (이 시점에 코루틴도 함께 멈춘다).
        gameObject.SetActive(false);
    }
}
