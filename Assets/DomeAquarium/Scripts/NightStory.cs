using System.Collections;
using UnityEngine;

/// <summary>
/// 엔딩의 밤 로비에서 골라 들어가는 이야기 장면 하나 (NightStoryHub). 검은 화면 뒤에서 Enter → 리그를 spot 으로 →
/// 밝아지면 Play → 끝나면 다시 검은 화면 뒤에서 Exit → 밤 로비로 돌아온다. 장면 동안 걷기는 꺼 둔다.
/// </summary>
public abstract class NightStory : MonoBehaviour
{
    public string title;                        // 이야기 판 버튼 글자
    public Transform spot;                      // 서는 자리 (정면 = 보는 쪽)

    /// <summary>이번 밤에 다녀왔나 — 판 버튼에 표시.</summary>
    public bool Visited { get; set; }

    /// <summary>검은 화면 뒤 — 장면을 꾸민다.</summary>
    public virtual void Enter() { }

    /// <summary>검은 화면 뒤, 리그를 spot 으로 옮긴 다음 — 머리 높이에 맞춰야 하는 장면(쇠공 안)이 여기서 맞춘다.</summary>
    public virtual void Arrived(Transform view) { }

    /// <summary>밝아진 뒤 — 장면이 끝날 때까지 돈다.</summary>
    public abstract IEnumerator Play();

    /// <summary>다시 검은 화면 뒤 — 꾸민 것을 걷는다.</summary>
    public virtual void Exit() { }
}
