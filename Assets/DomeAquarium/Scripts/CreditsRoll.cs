using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 엔딩 크레딧 — 마지막 나레이션 뒤 밤 로비에 떠서 천천히 올라간다 (기획 문서 "상승과 마무리").
/// CC-BY 에셋은 반드시 여기에 이름을 남긴다 (Assets/ThirdParty/CREDITS.md "앱 크레딧 화면에 반드시 표기").
///
/// 머리에 붙이지 않는다 — 데이터 월 새 줄(keepClear, 빛줄)의 왼쪽 끝에서 판 반폭 + clearance 만큼 더 왼쪽, 1.9m 에 세운다.
/// 예전에는 크레딧이 뜨는 순간 보던 방향에서 왼쪽 40° 였다 — 창 쪽을 보고 있으면 판이 새 줄 위에 그대로 얹혔고,
/// 정면을 봐도 판 오른쪽 끝(15° 남짓)이 빛줄 왼쪽 끝을 덮었다. 이제 어디를 보고 있었든 같은 자리(밤 로비 자리에서 약 49° 왼쪽).
/// 데이터 월이 3.2m 앞이라 2.2m 에 두면 벽에 거의 붙는다.
/// 다 올라가면 Finished — DiveDirector 가 손 닿는 곳에 "처음부터 다시"를 띄운다(부스에서 다음 관람객).
/// </summary>
public sealed class CreditsRoll : MonoBehaviour
{
    public CanvasGroup group;
    public RectTransform content;              // 올라가는 글 묶음 (viewport 안, 위쪽 기준)
    public RectTransform viewport;             // 보이는 창 (RectMask2D)
    public TMP_Text text;
    public Transform view;                     // 비면 Camera.main
    public RectTransform keepClear;            // 데이터 월 새 줄의 빛줄(NewRecordLine/Rule) — 제목·아랫줄은 가운데 정렬이라 이보다 좁다

    [TextArea(10, 60)] public string body;     // 크레딧 본문(리치 텍스트) — 빌더가 채운다. 이름은 여기서 고친다

    [Header("배치·속도")]
    public float distance = 1.9f;
    public float clearance = 3f;               // 새 줄 왼쪽 끝과 판 오른쪽 끝 사이 (도)
    public float yawOffset = -40f;             // keepClear 가 없을 때만 — 보던 방향에서 왼쪽으로
    public float drop = 0.05f;
    public float speed = 42f;                  // 캔버스 단위/초 (1 = 1.2mm) — 한 줄(약 48)을 1초 남짓에 넘긴다
    public float fadeSeconds = 1.2f;

    public bool Rolling { get; private set; }
    public event Action Finished;

    float endY;

    void Awake()
    {
        if (group != null) group.alpha = 0f;
    }

    public void Roll()
    {
        Place();
        if (text != null && !string.IsNullOrEmpty(body)) text.text = body;
        if (text != null) text.ForceMeshUpdate();
        float h = text != null ? text.preferredHeight : 0f;
        float vh = viewport != null ? viewport.rect.height : 0f;
        if (content != null)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, h);
            content.anchoredPosition = new Vector2(0f, -vh);    // 창 아래에서 올라오기 시작한다
        }
        endY = h - vh * 0.55f;                                  // 마지막 줄이 창 가운데쯤 올 때 멈춘다
        Rolling = true;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, Rolling || done ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
        if (!Rolling || content == null) return;
        Vector2 p = content.anchoredPosition;
        p.y += speed * Time.deltaTime;
        if (p.y >= endY)
        {
            p.y = endY;
            Rolling = false;
            done = true;
            Finished?.Invoke();
        }
        content.anchoredPosition = p;
    }

    bool done;

    /// <summary>눈높이에 세운다. 방향은 시선이 아니라 새 줄 왼쪽 끝 기준 — 판 오른쪽 끝이 그보다 clearance 만큼 왼쪽에 온다.</summary>
    void Place()
    {
        Transform v = ResolveView();
        if (v == null) return;
        Vector3 f = Flat(v.forward);
        float yaw = yawOffset;
        if (keepClear != null)
        {
            var corners = new Vector3[4];
            keepClear.GetWorldCorners(corners);        // 0 왼쪽 아래, 1 왼쪽 위 — 캔버스 앞면에서 본 왼쪽
            Vector3 toLeftEnd = Flat((corners[0] + corners[1]) * 0.5f - v.position);
            if (toLeftEnd.sqrMagnitude > 1e-4f)
            {
                f = toLeftEnd;
                float halfWidth = ((RectTransform)transform).rect.width * 0.5f * transform.lossyScale.x;
                yaw = -(Mathf.Atan2(halfWidth, distance) * Mathf.Rad2Deg + clearance);
            }
        }
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f = Quaternion.Euler(0f, yaw, 0f) * f.normalized;
        transform.SetPositionAndRotation(v.position + f * distance + Vector3.down * drop, Quaternion.LookRotation(f, Vector3.up));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>처음부터 다시 — 씬을 다시 연다. 부스에서 다음 관람객이 로비부터 시작한다.</summary>
    public void Restart()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    Transform ResolveView()
    {
        if (view != null) return view;
        Camera cam = Camera.main;
        if (cam != null) view = cam.transform;
        return view;
    }

#if UNITY_EDITOR
    /// <summary>디버그: 끝까지 넘긴다.</summary>
    public void DebugSkip()
    {
        if (!Rolling || content == null) return;
        content.anchoredPosition = new Vector2(0f, endY - 1f);
    }
#endif
}
