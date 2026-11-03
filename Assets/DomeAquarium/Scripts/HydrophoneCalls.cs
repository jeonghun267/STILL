using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 돔의 수중 마이크. 큰 고래가 지나가면(LargeCreaturePasses.Approaching) 그 고래의 소리를 들려준다 —
///   200m 대왕고래: 너무 낮아 사람 귀에 들리지 않는 노래를 10배 빠르게, 1,000m 향유고래: 소리로 먹이를 찾는 딸깍 소리.
/// 누구의 소리인지는 말하지 않는다 — 관찰기 위 알림과 관측소장 말은 "누구일까요?"까지만. 지켜보고 기록해서 알아낸다.
/// 소리는 2D(수중 마이크 스피커)라 어디서 오는지보다 "들린다"가 먼저다. 나는 동안 음악을 낮춘다.
/// </summary>
public sealed class HydrophoneCalls : MonoBehaviour
{
    [Serializable]
    public class Call
    {
        public FishData fish;
        public AudioClip clip;
        public string notice;                       // 관찰기 위 한 줄 — "수중 마이크 · 낮은 노래 (10배 빠르게)"
        [TextArea(2, 4)] public string line;        // 처음 한 번 관측소장
        [NonSerialized] public bool told;
    }

    public Call[] calls;
    public AudioSource speaker;
    public float delay = 3f;                        // 예고(무언가 다가와요) 뒤 이만큼 — 몸이 보일 즈음 들린다

    Coroutine playing;

    /// <summary>fish 의 소리가 있으면 틀고 그 항목을 돌려준다. 이미 틀고 있으면 null.</summary>
    public Call Play(FishData fish)
    {
        if (fish == null || calls == null || speaker == null || playing != null) return null;
        Call call = Array.Find(calls, c => c != null && c.fish != null && c.clip != null &&
                                          (c.fish == fish || c.fish.speciesId == fish.speciesId));
        if (call == null) return null;
        playing = StartCoroutine(PlayRoutine(call));
        return call;
    }

    IEnumerator PlayRoutine(Call call)
    {
        yield return new WaitForSeconds(delay);
        speaker.clip = call.clip;
        speaker.Play();
        AquariumAudioDirector.Instance?.PulseEventDuck(call.clip.length);
        yield return new WaitForSeconds(call.clip.length);
        playing = null;
    }

    public void Stop()
    {
        if (playing != null) StopCoroutine(playing);
        playing = null;
        if (speaker != null) speaker.Stop();
    }

    void OnDisable() => Stop();
}
