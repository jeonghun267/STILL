using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 해변 쓰레기 줍기 — 놀란 옆 모래에 버려진 페트병 · 캔 · 엉킨 그물 조각. 손으로 건드리거나(찌르기) 컨트롤러로 집으면
/// 장비대 옆 수거함으로 날아 들어간다. 다 주우면 놀란이 마지막 그물 조각 이야기를 꺼낸다 — "유령 그물".
/// 할 일과 상관없는 덤이고, 24m 의 그물에 걸린 바다거북(EntangledTurtle)과 엔딩의 밤 인사로 이어진다.
/// </summary>
public sealed class BeachCleanup : MonoBehaviour
{
    [System.Serializable]
    public class Item
    {
        public Transform root;
        public XRSimpleInteractable interactable;
        public PokeSelector poke;
        [System.NonSerialized] public bool taken;
    }

    public Item[] items;
    public Transform bin;                      // 수거함 입구
    public TMP_Text counter;                   // 수거함 옆 "0 / 3"
    public NpcDialogue nolant;
    public AudioSource sound;
    public AudioClip dropClip;
    [TextArea(2, 4)] public string doneLine =
        "고마워! 방금 마지막 건 버려진 그물 조각이야. 바다를 떠다니는 그물은 '유령 그물'이라고 해. " +
        "물고기랑 바다거북이 걸려서 빠져나오지 못하거든. 저 아래 바다에서도 보게 될지 몰라.";

    public int Collected { get; private set; }
    public bool Done => items != null && Collected >= items.Length;

    void Start()
    {
        ShowCount();
        if (items == null) return;
        foreach (var it in items)
        {
            var item = it;
            if (item.interactable != null) item.interactable.selectEntered.AddListener(_ => Take(item));
            if (item.poke != null) item.poke.onPoked.AddListener(() => Take(item));
        }
    }

    void Take(Item item)
    {
        if (item.taken || item.root == null) return;
        item.taken = true;
        StartCoroutine(Fly(item));
    }

    IEnumerator Fly(Item item)
    {
        Transform t = item.root;
        Vector3 from = t.position;
        Vector3 to = bin != null ? bin.position : from + Vector3.up;
        Quaternion spin = Quaternion.Euler(Random.Range(-180f, 180f), Random.Range(-180f, 180f), 0f);
        Quaternion start = t.rotation;
        for (float u = 0f; u < 1f; u += Time.deltaTime / 0.7f)
        {
            float k = Mathf.SmoothStep(0f, 1f, u);
            t.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.5f;   // 포물선
            t.rotation = Quaternion.Slerp(start, spin * start, k);
            yield return null;
        }
        if (sound != null && dropClip != null) sound.PlayOneShot(dropClip);
        t.gameObject.SetActive(false);
        Collected++;
        ShowCount();
        if (Done && nolant != null) nolant.Remark(doneLine, "알겠어!", "cleaned");
    }

    void ShowCount()
    {
        if (counter != null) counter.text = $"{Collected} / {(items != null ? items.Length : 0)}";
    }

#if UNITY_EDITOR
    /// <summary>헤드셋 없이 확인할 때 — 아직 남은 첫 쓰레기를 줍는다.</summary>
    public void DebugTakeNext()
    {
        if (items == null) return;
        foreach (var it in items) if (!it.taken) { Take(it); return; }
    }
#endif
}
