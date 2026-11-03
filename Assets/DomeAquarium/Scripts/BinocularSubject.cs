using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 관망 쌍안경(BinocularViewer)으로 볼 수 있는 것 — 배 · 등대 · 부표 · 고래. 확대 화면 한가운데에 잠시 머무르면 이름표가 뜨고,
/// line 이 있으면 처음 한 번 관측소장이 한마디 한다. 고래처럼 잠깐만 보이는 것은 그동안만 켠다(enabled).
/// </summary>
public sealed class BinocularSubject : MonoBehaviour
{
    public string label;                                // "화물선 · 컨테이너를 싣고 항구로 가요"
    [TextArea(2, 4)] public string line;                // 처음 보았을 때 관측소장 (비면 이름표만)
    public float radius = 4f;                           // 이만큼 떨어진 곳을 겨눠도 이것으로 본다 (몸 크기)
    public Vector3 offset;                              // 로컬 — 몸 가운데 (모델 바닥이 원점일 때 높이)

    [System.NonSerialized] public bool spoken;

    static readonly List<BinocularSubject> all = new List<BinocularSubject>();
    public static IReadOnlyList<BinocularSubject> All => all;

    public Vector3 Center => transform.TransformPoint(offset);

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);
}
