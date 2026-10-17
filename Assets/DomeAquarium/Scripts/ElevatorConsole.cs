using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 엘리베이터 칸 안의 깊이 버튼 (24m · 200m · 1,000m). 층의 할 일을 마쳐야 다음 버튼이 열린다.
/// 갈 차례인 버튼은 숨 쉬듯 밝아졌다 어두워진다. 잠긴 버튼은 눌러도 반응하지 않는다.
/// 버튼은 손가락으로 찌르거나(PokeSelector) 컨트롤러 레이로 누른다(Button) — 둘 다 Press(i) 로 온다.
/// </summary>
public sealed class ElevatorConsole : MonoBehaviour
{
    [Serializable]
    public struct Slot
    {
        public Button button;
        public Image front;                    // 버튼 앞판 (색으로 상태를 보인다)
        public TMP_Text depth;
        public TMP_Text state;
    }

    public Slot[] slots;                       // 얕은 층부터. 층 수보다 많으면 남는 버튼은 잠겨 있다
    public DiveDirector director;              // 비면 런타임에 찾는다 (로비를 다시 빌드해도 끊기지 않게)

    [Header("색")]
    public Color nextTint = new Color(0.16f, 0.55f, 0.78f, 1f);
    public Color openTint = new Color(0.40f, 0.50f, 0.58f, 1f);
    public Color hereTint = new Color(0.22f, 0.62f, 0.50f, 1f);
    public Color lockedTint = new Color(0.20f, 0.23f, 0.26f, 1f);
    public Color lockedText = new Color(1f, 1f, 1f, 0.38f);
    public float pulseSpeed = 1.6f;

    int next = -1;

    DiveDirector Director
    {
        get
        {
            if (director == null) director = DiveDirector.Instance;
            return director;
        }
    }

    void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    // 씬을 열 때 이 칸이 DiveDirector 보다 먼저 켜지면 OnEnable 에서는 아직 Instance 가 없다. Start 는 모든 Awake 뒤다.
    void Start() => Refresh();

    void OnDisable()
    {
        if (subscribed != null) subscribed.Changed -= Refresh;
        subscribed = null;
    }

    DiveDirector subscribed;

    void Subscribe()
    {
        DiveDirector d = Director;
        if (d == subscribed) return;
        if (subscribed != null) subscribed.Changed -= Refresh;
        subscribed = d;
        if (d != null) d.Changed += Refresh;
    }

    public void Press(int index)
    {
        DiveDirector d = Director;
        if (d == null) return;
        d.Select(index);
    }

    public void Refresh()
    {
        Subscribe();
        DiveDirector d = Director;
        if (slots == null) return;
        bool waiting = d != null && d.Current == DiveDirector.State.Cabin;
        next = d != null && waiting ? d.SuggestedIndex : -1;

        for (int i = 0; i < slots.Length; i++)
        {
            Slot s = slots[i];
            bool unlocked = d != null && d.IsUnlocked(i);
            bool moving = d != null && d.Current == DiveDirector.State.Riding;
            bool riding = moving && d.RideTarget == i;
            bool here = d != null && d.LayerIndex == i && !moving;   // 떠나는 중에는 "지금 여기"가 아니다

            string label;
            Color tint;
            if (!unlocked) { label = ObservationTerms.FloorLocked; tint = lockedTint; }
            else if (riding) { label = ObservationTerms.FloorGoing; tint = nextTint; }
            else if (i == next) { label = ObservationTerms.FloorNext; tint = nextTint; }
            else if (here) { label = ObservationTerms.FloorHere; tint = hereTint; }
            else if (d.IsComplete(i)) { label = ObservationTerms.FloorDone; tint = openTint; }
            else { label = string.Empty; tint = openTint; }

            if (s.state != null) s.state.text = label;
            if (s.front != null) s.front.color = tint;
            Color ink = unlocked ? Color.white : lockedText;
            if (s.depth != null) s.depth.color = ink;
            if (s.state != null) s.state.color = ink;
            if (s.button != null) s.button.interactable = unlocked && waiting;
        }
    }

    void Update()
    {
        // 갈 차례인 버튼만 천천히 밝아졌다 어두워진다 — 깜빡임은 VR 에서 눈이 피로하다.
        if (next < 0 || slots == null || next >= slots.Length || slots[next].front == null) return;
        float k = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI);
        slots[next].front.color = Color.Lerp(nextTint, Color.Lerp(nextTint, Color.white, 0.45f), k);
    }
}
