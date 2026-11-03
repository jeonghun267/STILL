using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 엔딩의 밤 로비 갈림길 — 크레딧이 끝나면 "오늘 밤의 이야기" 판이 손 닿는 곳에 뜬다(시선을 따라온다, ElevatorCallPanel).
/// 이야기 장면(NightStory)마다 버튼 하나, 마지막 칸은 "처음부터 다시". 장면을 고르면 검은 화면 뒤에서 그 자리로 가고,
/// 끝나면 밤 로비(nightSpot)로 돌아와 판이 다시 뜬다. 다녀온 장면에는 "다녀왔어요"가 붙는다.
/// </summary>
public sealed class NightStoryHub : MonoBehaviour
{
    public LobbySequence lobby;
    public LobbyTutorial tutorial;
    public CreditsRoll credits;
    public ElevatorCallPanel panel;             // 판 — 나타나기 · 따라오기 · 숨기
    public NightStory[] stories;
    public GameObject[] slots;                  // 버튼 칸 (빌더가 6개)
    public TMP_Text[] labels;
    public string heading = "오늘 밤의 이야기";
    public string restartLabel = "처음부터 다시";
    public string visitedMark = "<size=62%><color=#9fe6b8>다녀왔어요</color></size>";

    float lastPress = -99f;

    public RectTransform plate;                 // 판 바탕 — 쓰는 줄 수만큼 높이를 맞춘다
    public RectTransform titleRect;

    public bool Running { get; private set; }
    public NightStory Current { get; private set; }

    static int running;
    /// <summary>이야기 장면 안에 있는 동안 — 로비 걷기 범위(LobbyTutorial)가 리그를 로비로 끌어당기지 않게 한다.</summary>
    public static bool AnyRunning => running > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => running = 0;

    // 판 배치 (캔버스 픽셀) — 제목 줄 + 버튼 줄(2칸)
    const float Width = 500f, TitleBand = 84f, RowHeight = 112f, Bottom = 18f, ColumnX = 118f;

    public void Open()
    {
        Layout();
        if (panel != null) panel.Show(heading);
    }

    void Layout()
    {
        int n = stories != null ? stories.Length : 0;
        int rows = Mathf.Max(1, Mathf.CeilToInt((n + 1) / 2f));
        float height = TitleBand + rows * RowHeight + Bottom;
        if (plate != null) plate.sizeDelta = new Vector2(Width, height);
        if (titleRect != null) titleRect.anchoredPosition = new Vector2(0f, height * 0.5f - TitleBand * 0.5f);
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null)
                ((RectTransform)slots[i].transform).anchoredPosition =
                    new Vector2(i % 2 == 0 ? -ColumnX : ColumnX, height * 0.5f - TitleBand - RowHeight * (i / 2 + 0.5f));
        for (int i = 0; i < slots.Length; i++)
        {
            bool story = i < n && stories[i] != null, restart = i == n;
            if (slots[i] != null) slots[i].SetActive(story || restart);
            if (labels == null || i >= labels.Length || labels[i] == null) continue;
            if (story) labels[i].text = stories[i].Visited ? $"{stories[i].title}\n{visitedMark}" : stories[i].title;
            else if (restart) labels[i].text = restartLabel;
        }
    }

    public void Press0() => Press(0);
    public void Press1() => Press(1);
    public void Press2() => Press(2);
    public void Press3() => Press(3);
    public void Press4() => Press(4);
    public void Press5() => Press(5);

    void Press(int i)
    {
        // 찌르기와 레이가 같은 프레임에 둘 다 부를 수 있다 — 한 번만 받는다
        if (Running || Time.unscaledTime - lastPress < 0.6f) return;
        lastPress = Time.unscaledTime;
        int n = stories != null ? stories.Length : 0;
        if (i < n && stories[i] != null) StartCoroutine(Run(stories[i]));
        else if (i == n && credits != null) credits.Restart();
    }

    IEnumerator Run(NightStory story)
    {
        Running = true;
        running++;
        Current = story;
        if (panel != null) panel.Hide();
        if (tutorial != null) tutorial.EnableLocomotion(false);       // 장면 안에서는 걷기 범위가 다르다 — 서서 본다
        ScreenFader fader = lobby != null ? lobby.Fader : null;
        if (fader != null) yield return fader.FadeTo(1f, 0.8f);
        story.Enter();
        if (lobby != null) lobby.MoveRigTo(story.spot);
        yield return null;
        if (Camera.main != null) story.Arrived(Camera.main.transform);
        if (fader != null) yield return fader.FadeTo(0f, 1.2f);

        yield return story.Play();

        if (fader != null) yield return fader.FadeTo(1f, 0.9f);
        story.Exit();
        story.Visited = true;
        if (lobby != null) lobby.MoveRigTo(lobby.nightSpot);
        yield return null;
        Current = null;
        running = Mathf.Max(0, running - 1);
        if (tutorial != null) tutorial.EnableLocomotion(true);
        if (fader != null) yield return fader.FadeTo(0f, 1.2f);
        // 판이 다시 뜬 뒤에야 끝 — 예전에는 밝아지기 전에 Running 을 내려, 그 사이 다음 장면을 고르면 이 판이 다음 장면 한가운데에 떴다
        Running = false;
        Open();
    }

#if UNITY_EDITOR
    public void DebugPress(int i) { lastPress = -99f; Press(i); }
#endif
}
