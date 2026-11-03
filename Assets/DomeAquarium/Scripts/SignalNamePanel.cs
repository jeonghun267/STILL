using TMPro;
using UnityEngine;

/// <summary>
/// 신호의 주인을 기록한 뒤 이름을 붙이는 판 — 후보 셋 중 하나를 고른다(손가락으로 찌르기 · 컨트롤러 레이).
/// 고른 이름은 관측소장 말 · 엔딩 데이터 월 · 선장과 놀란의 밤 대사에 쓰인다 (DiveDirector.SignalName).
/// 눈앞 아래(0.62m, 손 닿는 곳)에 한 번 놓고 따라오지 않는다 — 생물은 눈앞 2.4m 에 머물러 있고, 판은 그 아래에서 짧게 쓴다.
/// </summary>
public sealed class SignalNamePanel : MonoBehaviour
{
    public CanvasGroup group;
    public TMP_Text title;
    public TMP_Text[] labels;                  // 후보 버튼 글자 (3)
    public Transform view;                     // 비면 Camera.main
    public float distance = 0.62f;
    public float drop = 0.24f;                 // 눈높이보다 약 21° 아래 — 생물(정면)을 가리지 않는다

    string[] options;
    float lastPress = -99f;

    public bool Waiting { get; private set; }
    public string Chosen { get; private set; }

    void Awake() => SetVisible(false);

    public void Show(string[] names)
    {
        options = names;
        Chosen = null;
        Waiting = true;
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] != null) labels[i].text = i < names.Length ? names[i] : string.Empty;

        if (view == null && Camera.main != null) view = Camera.main.transform;
        if (view != null)
        {
            Vector3 f = view.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            transform.SetPositionAndRotation(view.position + f * distance + Vector3.down * drop,
                                             Quaternion.LookRotation(f * distance + Vector3.down * drop, Vector3.up));   // 판이 눈을 본다
        }
        SetVisible(true);
    }

    public void Choose0() => Choose(0);
    public void Choose1() => Choose(1);
    public void Choose2() => Choose(2);

    void Choose(int i)
    {
        // 찌르기와 레이가 같은 프레임에 둘 다 부를 수 있다 — 한 번만 받는다
        if (!Waiting || options == null || i < 0 || i >= options.Length || Time.unscaledTime - lastPress < 0.5f) return;
        lastPress = Time.unscaledTime;
        Chosen = options[i];
        Waiting = false;
        SetVisible(false);
    }

    /// <summary>고르지 않고 오래 있으면 DiveDirector 가 첫 후보로 정한다.</summary>
    public void ChooseDefault() => Choose(0);

    void SetVisible(bool on)
    {
        if (group == null) return;
        group.alpha = on ? 1f : 0f;
        group.interactable = on;
        group.blocksRaycasts = on;
    }
}
