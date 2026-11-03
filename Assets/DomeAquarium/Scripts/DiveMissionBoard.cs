using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 관찰기 위에 뜨는 이 층의 할 일 두 줄 (기획 문서의 "의뢰서").
///   수심 200m · 할 일
///   ○ 큰 생물 2종 기록하기        1/2
/// 지목 생물(targets)을 둔 층이면 둘째 줄에 "찾아보기: 단서"가 붙는다 — 지금은 24m · 200m 모두 첫 줄만 쓴다.
/// 관찰기 상태 화면(ObservationAim.statusRoot)의 자식이라 관찰기를 들고 있을 때만 보인다.
/// 지목 생물은 이름을 쓰지 않는다 — 단서만 보고 찾는 것이 할 일이다.
/// </summary>
public sealed class DiveMissionBoard : MonoBehaviour
{
    public GameObject root;
    public TMP_Text title;
    public TMP_Text countLine;
    public TMP_Text countTally;
    public Image countMark;
    public GameObject targetRow;
    public TMP_Text targetLine;
    public Image targetMark;

    [Header("3막 — 신호 세기 (할 일 둘째 줄 자리)")]
    public GameObject signalRow;
    public RectTransform signalFill;           // 막대 — 너비를 신호 세기만큼
    public float signalWidth = 380f;
    /// <summary>0~1. 비면 신호 줄을 숨긴다 (DiveDirector 가 미확인 생물의 신호 세기를 넘긴다).</summary>
    [NonSerialized] public Func<float> signal;

    public Sprite openMark;                    // 아직 — 빈 고리
    public Sprite doneMark;                    // 마침 — 채운 점
    public Color openColor = new Color(0.62f, 0.76f, 0.82f, 1f);
    public Color doneColor = new Color(0.55f, 0.95f, 0.70f, 1f);
    public Color titleColor = new Color(0.55f, 0.95f, 1f, 1f);

    public void Show(DiveMission m, int recorded, bool targetFound, bool complete)
    {
        if (m == null) { Hide(); return; }
        if (root != null && !root.activeSelf) root.SetActive(true);

        if (title != null)
        {
            title.text = complete ? ObservationTerms.LayerDone(m.DepthLabel) : ObservationTerms.LayerTasks(m.DepthLabel);
            title.color = complete ? doneColor : titleColor;
        }

        // 3막처럼 할 일이 한 줄뿐이면 그 문장을 그대로 쓰고 숫자는 숨긴다.
        bool custom = !string.IsNullOrWhiteSpace(m.taskLine);
        bool countDone = custom ? complete : recorded >= m.recordCount;
        if (countLine != null) countLine.text = custom ? m.taskLine : ObservationTerms.CountTask(m.countSubject, m.recordCount);
        if (countTally != null)
        {
            if (countTally.gameObject.activeSelf == custom) countTally.gameObject.SetActive(!custom);
            countTally.text = ObservationTerms.Tally(recorded, m.recordCount);
        }
        Paint(countMark, countLine, countTally, countDone);

        bool showSignal = m.showSignal && !complete && signal != null;
        if (signalRow != null && signalRow.activeSelf != showSignal) signalRow.SetActive(showSignal);

        bool hasTarget = m.HasTarget && !custom;
        if (targetRow != null && targetRow.activeSelf != hasTarget) targetRow.SetActive(hasTarget);
        if (hasTarget && targetLine != null) targetLine.text = ObservationTerms.TargetTask(m.targetClue);
        Paint(targetMark, targetLine, null, hasTarget && targetFound);
    }

    void Update()
    {
        if (signal == null || signalRow == null || !signalRow.activeInHierarchy || signalFill == null) return;
        float w = Mathf.Clamp01(signal()) * signalWidth;
        signalFill.sizeDelta = new Vector2(Mathf.Lerp(signalFill.sizeDelta.x, w, 1f - Mathf.Exp(-10f * Time.deltaTime)), signalFill.sizeDelta.y);
    }

    public void Hide()
    {
        if (root != null && root.activeSelf) root.SetActive(false);
    }

    void Paint(Image mark, TMP_Text line, TMP_Text tally, bool done)
    {
        Color c = done ? doneColor : openColor;
        if (mark != null)
        {
            Sprite s = done ? doneMark : openMark;
            if (s != null) mark.sprite = s;
            mark.color = c;
        }
        if (line != null) line.color = done ? doneColor : Color.white;
        if (tally != null) tally.color = c;
    }
}
