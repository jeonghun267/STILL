using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 NPC 와 주고받는 대화. 가까이 가면 NPC 옆에 "말 걸기" 판이 뜨고, 누르면(손가락 찌르기 · 컨트롤러 레이) NPC 가 한 마디 하고
/// 대답 1~2개가 버튼으로 나온다. 대답에 따라 다음 마디로 가거나 대화를 마친다.
///
/// 대화는 마디(Node) 목록 — 마디마다 대사 · 대답(다음 마디 id, 비우면 끝) · 남길 표시(set, 쉼표로 여러 개).
/// 어디서 시작할지는 지금까지의 표시와 상황으로 정한다(Entry): 처음 → start, 엔딩의 밤 로비 → night.
/// 밤에 24m 의 그물에 걸린 거북 구조 요청을 보냈으면 → night_rescued (한 번).
/// 그 마디가 있으면: 관찰기 첫 기록을 한 뒤 처음 → practiced, 첫 기록 전 → revisit_pre,
/// 고래를 찾아 달라고 한 뒤 쌍안경으로 고래를 봤으면 → whale_found, 아직이면 → whale_wait. 그 밖에는 → revisit.
///
/// 대사는 판 위 글자 + NPC 목소리(관측소장과 다른 Gemini 음성)로 낸다. 관측소장 자막 자리는 쓰지 않는다 — 판이 NPC 바로 옆이라 누가 말하는지 보인다.
/// 대화하는 동안(AnyTalking)은 관측소장 자막 · 목소리가 기다린다 — 한 번에 한 사람만 말해 글자 판이 겹치지 않는다(LobbySequence.Say).
/// 판은 NPC 와 관람객 사이, 관람객 기준 오른쪽에 떠서 NPC 얼굴을 가리지 않고, 늘 관람객을 향한다.
/// 말을 걸기 전에는 이름과 "말 걸기" 버튼만 담긴 작은 판(위쪽 절반)이라 옆의 장비대 · 표시를 덜 가린다.
/// </summary>
public sealed class NpcDialogue : MonoBehaviour
{
    [Serializable]
    public class Choice
    {
        public string label;
        public string next;                             // 비우면 대화를 마친다
    }

    [Serializable]
    public class Node
    {
        public string id;
        [TextArea(2, 5)] public string line;
        public string set;                              // 이 마디에 오면 남길 표시 (쉼표로 여러 개)
        public Choice[] choices;
    }

    public LobbyNpc npc;
    public LobbySequence lobby;
    public WhaleSurfacing whale;                        // 쌍안경으로 고래를 봤는지 (whale.subject.spoken)
    public LobbyTutorial tutorial;                      // 관찰기 첫 기록을 했는지 (조수 웅덩이 안내 — practiced · revisit_pre)
    public string speaker = "보리 선장";
    public string voice = "Charon";
    public Node[] nodes;

    [Header("판 (빌더가 채운다)")]
    public RectTransform panel;
    public RectTransform plate;                         // 판 바탕 — 말 걸기 전에는 위쪽만 작게
    public CanvasGroup group;
    public TMP_Text title;
    public TMP_Text body;
    public Button talkButton;
    public Button[] choiceButtons;
    public TMP_Text[] choiceLabels;

    [Header("자리")]
    public float showRadius = 3.0f;                     // 이 안에 들어오면 "말 걸기"
    public float endRadius = 5.0f;                      // 대화 중 이보다 멀어지면 끝낸다
    public float toward = 0.6f;                         // NPC 에서 관람객 쪽으로
    public float side = 0.45f;                          // 관람객 기준 오른쪽으로
    public float height = 1.3f;
    public float follow = 4f;

    static readonly HashSet<NpcDialogue> talkingNow = new HashSet<NpcDialogue>();

    /// <summary>지금 누군가 NPC 와 대화 중인가 — 관측소장 자막이 기다린다.</summary>
    public static bool AnyTalking => talkingNow.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => talkingNow.Clear();     // 도메인 다시 읽기를 끈 에디터 플레이에서도 비운다

    readonly HashSet<string> flags = new HashSet<string>();
    Node current;
    bool talking;
    float choicesAt;
    float lastPress = -999f;
    Transform view;

    public bool Talking => talking;
    public bool Has(string flag) => flags.Contains(flag);

    void Start()
    {
        if (group != null) { group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false; }
        ShowIdle();
    }

    void OnDisable()
    {
        // 엘리베이터를 타면 로비가 꺼진다 — 하던 대화는 끝낸다(표시는 남긴다)
        if (talking) EndTalk();
        talkingNow.Remove(this);
        if (group != null) { group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false; }
    }

    void Update()
    {
        if (view == null) { var cam = Camera.main; if (cam == null) return; view = cam.transform; }
        Vector3 anchor = npc != null ? npc.transform.position : transform.position;
        Vector3 d = view.position - anchor;
        d.y = 0f;
        float dist = d.magnitude;
        if (talking && dist > endRadius) EndTalk();
        // 쌍안경을 들여다보는 동안은 숨는다 — 판은 맨 위에 그려져 깜깜한 쌍안경 화면 위에 떴다
        // 밤 이야기 장면 안에서는 "말 걸기" 판을 띄우지 않는다 — 장면이 시키는 말(SayLine)만 보인다
        bool visible = (talking || (dist < showRadius && !NightStoryHub.AnyRunning)) && !BinocularViewer.AnyEngaged;

        if (group != null)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.deltaTime * 3f);
            bool live = visible && group.alpha > 0.6f;
            group.interactable = live;
            group.blocksRaycasts = live;
        }
        if (talking) UpdateChoices();

        if (panel != null && dist > 0.05f)
        {
            Vector3 dir = d / dist;
            Vector3 right = Vector3.Cross(Vector3.up, -dir);                // 관람객이 NPC 를 볼 때의 오른쪽
            Vector3 want = anchor + dir * Mathf.Min(toward, dist * 0.5f) + right * side + Vector3.up * height;
            float k = 1f - Mathf.Exp(-follow * Time.deltaTime);
            panel.position = group != null && group.alpha <= 0.01f ? want : Vector3.Lerp(panel.position, want, k);
            Vector3 look = panel.position - view.position;                  // 월드 캔버스는 뒤(-Z)에서 읽힌다
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 버튼 (찌르기와 레이가 같은 프레임에 둘 다 부를 수 있다 — 한 번만 받는다)
    // ─────────────────────────────────────────────────────────────

    public void PressTalk()
    {
        if (!Debounce() || talking) return;
        talking = true;
        talkingNow.Add(this);
        ShowFull();
        Go(Entry());
    }

    public void PressChoice0() => Choose(0);
    public void PressChoice1() => Choose(1);

    bool Debounce()
    {
        if (Time.unscaledTime - lastPress < 0.4f) return false;
        lastPress = Time.unscaledTime;
        return true;
    }

    void Choose(int i)
    {
        if (!talking || current == null || Time.time < choicesAt) return;
        if (current.choices == null || i < 0 || i >= current.choices.Length) return;
        if (!Debounce()) return;
        string next = current.choices[i].next;
        if (string.IsNullOrWhiteSpace(next)) EndTalk();
        else Go(next);
    }

    // ─────────────────────────────────────────────────────────────
    // 흐름
    // ─────────────────────────────────────────────────────────────

    string Entry()
    {
        if (lobby != null && lobby.IsNight)
        {
            // 24m 에서 그물에 걸린 거북의 구조 요청을 보냈으면 그 소식부터 (놀란)
            bool rescued = DiveDirector.Instance != null && DiveDirector.Instance.TurtleRescued;
            if (rescued && !Has("rescueTold") && Find("night_rescued") != null) return "night_rescued";
            if (Find("night") != null) return "night";
        }
        bool practiced = tutorial != null && tutorial.Current >= LobbyTutorial.Step.Board;
        if (!Has("met")) return practiced && Find("practiced_new") != null ? "practiced_new" : "start";
        if (practiced && !Has("practicedSeen") && Find("practiced") != null) return "practiced";
        if (!practiced && tutorial != null && Find("revisit_pre") != null) return "revisit_pre";
        if (Has("whaleAsked") && !Has("whaleThanked"))
            return !WhaleSeen ? "whale_wait" : FlukePhotoId.Identified && Find("whale_found_id") != null ? "whale_found_id" : "whale_found";
        return "revisit";
    }

    bool WhaleSeen => whale != null && whale.subject != null && whale.subject.spoken;

    Node Find(string id)
    {
        if (nodes == null) return null;
        foreach (var n in nodes) if (n != null && n.id == id) return n;
        return null;
    }

    /// <summary>
    /// 말을 걸지 않았는데 NPC 가 먼저 하는 한 마디 (해변 쓰레기를 다 주웠을 때 등). 대화 중이면 넘긴다.
    /// 대답 버튼 하나로 끝나고, set 표시를 남겨 나중 대사가 받아 줄 수 있다.
    /// </summary>
    public void Remark(string line, string reply = "알겠어요", string set = null)
    {
        if (talking || string.IsNullOrWhiteSpace(line)) return;
        talking = true;
        talkingNow.Add(this);
        ShowFull();
        Show(new Node { id = "remark", line = line, set = set, choices = new[] { new Choice { label = reply } } });
    }

    /// <summary>
    /// 밤 이야기 장면에서 NPC 가 혼자 하는 말 — 대답 버튼 없이 판에 대사를 띄우고, 목소리가 끝나면(없으면 읽을 시간 뒤) 내린다.
    /// 그동안 관측소장 자막은 기다린다(AnyTalking).
    /// </summary>
    public IEnumerator SayLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        while (talking) yield return null;
        talking = true;
        talkingNow.Add(this);
        ShowFull();
        Show(new Node { id = "say", line = line, choices = new Choice[0] });
        var tts = FishNarrationService.Instance;
        float start = Time.time, read = 1.6f + line.Length * 0.085f;
        yield return null;
        while (talking && Time.time - start < read + 10f)
        {
            bool speaking = tts != null && tts.IsSpeaking;
            if (!speaking && Time.time - start >= read) break;
            yield return null;
        }
        if (talking) EndTalk();
    }

    void Go(string id) => Show(Find(id));

    void Show(Node node)
    {
        current = node;
        if (current == null) { EndTalk(); return; }
        if (!string.IsNullOrWhiteSpace(current.set))
            foreach (var f in current.set.Split(',')) if (!string.IsNullOrWhiteSpace(f)) flags.Add(f.Trim());

        string line = Fill(current.line);
        if (title != null) title.text = speaker;
        if (body != null) { body.gameObject.SetActive(true); body.text = line; }
        if (talkButton != null) talkButton.gameObject.SetActive(false);
        // 대답은 대사를 조금 들은 뒤에 — 바로 누르고 넘겨 버리지 않게
        choicesAt = Time.time + Mathf.Min(2.5f, 0.8f + line.Length * 0.025f);
        foreach (var b in choiceButtons) if (b != null) b.gameObject.SetActive(false);

        if (npc != null) npc.Gesture("Talk");
        var tts = FishNarrationService.Instance;
        if (tts != null) tts.Speak(string.Empty, line, voice);
    }

    /// <summary>대사 속 {name} 을 체험자가 1,000m 에서 붙인 신호의 주인 이름으로 — 아직 없으면 "그 생물". {이라고} · {이에요} 는 받침에 맞춘다.</summary>
    static string Fill(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains("{name}")) return line;
        string name = DiveDirector.Instance != null ? DiveDirector.Instance.SignalName : string.Empty;
        if (string.IsNullOrWhiteSpace(name)) name = "그 생물";
        return line.Replace("{name}", name)
                   .Replace("{이라고}", ObservationTerms.Particle(name, "이라고", "라고"))
                   .Replace("{이에요}", ObservationTerms.Particle(name, "이에요", "예요"));
    }

    void UpdateChoices()
    {
        if (current == null || Time.time < choicesAt) return;
        int n = current.choices != null ? current.choices.Length : 0;
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            bool on = i < n;
            if (choiceButtons[i] != null && choiceButtons[i].gameObject.activeSelf != on) choiceButtons[i].gameObject.SetActive(on);
            if (on && choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null && choiceLabels[i].text != current.choices[i].label)
                choiceLabels[i].text = current.choices[i].label;
        }
    }

    void EndTalk()
    {
        talking = false;
        talkingNow.Remove(this);
        current = null;
        ShowIdle();
    }

    // 판 배치(캔버스 픽셀, 가운데 기준) — 대화 중에는 520 × 470 전체, 말 걸기 전에는 이름 · 버튼만 담은 위쪽 520 × 230
    static readonly Vector2 FullPlateSize = new Vector2(520f, 470f), IdlePlateSize = new Vector2(520f, 230f);
    static readonly Vector2 IdlePlateCenter = new Vector2(0f, 120f), IdleTalkButton = new Vector2(0f, 85f);

    void ShowIdle()
    {
        if (title != null) title.text = speaker;
        if (body != null) body.gameObject.SetActive(false);
        if (choiceButtons != null) foreach (var b in choiceButtons) if (b != null) b.gameObject.SetActive(false);
        if (talkButton != null)
        {
            talkButton.gameObject.SetActive(true);
            if (plate != null) ((RectTransform)talkButton.transform).anchoredPosition = IdleTalkButton;
        }
        if (plate != null) { plate.sizeDelta = IdlePlateSize; plate.anchoredPosition = IdlePlateCenter; }
    }

    void ShowFull()
    {
        if (plate != null) { plate.sizeDelta = FullPlateSize; plate.anchoredPosition = Vector2.zero; }
    }

#if UNITY_EDITOR
    /// <summary>헤드셋 없이 확인할 때 — 대화를 시작하거나, 이미 하고 있으면 i 번째 대답을 고른다.</summary>
    public void DebugStep(int choice)
    {
        lastPress = -999f;
        if (!talking) { PressTalk(); return; }
        choicesAt = 0f;
        Choose(choice);
    }

    public void DebugMarkWhaleSeen()
    {
        if (whale != null && whale.subject != null) whale.subject.spoken = true;
    }

    public string DebugState() =>
        $"대화 {(talking ? "중" : "아님")} 마디={(current != null ? current.id : "-")} 표시=[{string.Join(",", flags)}] 고래봄={WhaleSeen}";
#endif
}
