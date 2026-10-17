using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 엔딩 크레딧 — 마지막 나레이션 뒤 밤 로비에 떠서 천천히 올라간다 (기획 문서 "상승과 마무리").
/// CC-BY 에셋은 반드시 여기에 이름을 남긴다 (Assets/ThirdParty/CREDITS.md "앱 크레딧 화면에 반드시 표기").
///
/// 머리에 붙이지 않는다 — 보던 방향(데이터 월)에서 왼쪽 40°, 1.9m 에 세워 두어 데이터 월의 새 줄과 겹치지 않는다.
/// 데이터 월이 3.2m 앞이라 2.2m 에 두면 벽에 거의 붙고, 왼쪽 35° 로는 새 줄 왼쪽 끝과 겹쳤다.
/// 다 올라가면 Finished — DiveDirector 가 손 닿는 곳에 "처음부터 다시"를 띄운다(부스에서 다음 관람객).
/// </summary>
public sealed class CreditsRoll : MonoBehaviour
{
    public CanvasGroup group;
    public RectTransform content;              // 올라가는 글 묶음 (viewport 안, 위쪽 기준)
    public RectTransform viewport;             // 보이는 창 (RectMask2D)
    public TMP_Text text;
    public Transform view;                     // 비면 Camera.main

    [TextArea(10, 60)] public string body;     // 크레딧 본문(리치 텍스트) — 빌더가 채운다. 이름은 여기서 고친다

    [Header("배치·속도")]
    public float distance = 1.9f;
    public float yawOffset = -40f;             // 보던 방향에서 왼쪽으로 — 정면의 데이터 월을 가리지 않게
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
        Transform v = ResolveView();
        if (v != null)
        {
            Vector3 f = new Vector3(v.forward.x, 0f, v.forward.z);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f = Quaternion.Euler(0f, yawOffset, 0f) * f.normalized;
            transform.SetPositionAndRotation(v.position + f * distance + Vector3.down * drop, Quaternion.LookRotation(f, Vector3.up));
        }
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
