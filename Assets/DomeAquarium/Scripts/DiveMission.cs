using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>관측 층. 엘리베이터 깊이 버튼과 생물의 서식 층이 같은 값을 쓴다.</summary>
public enum DiveDepth
{
    Reef24 = 0,        // 1막 산호초 — 햇빛이 충분히 닿는 곳
    Twilight200 = 1,   // 2막 박광층 입구 — 햇빛이 거의 끝나는 곳
    Midnight1000 = 2,  // 3막 심해
}

/// <summary>층마다 돔 바닥을 어떻게 꾸미나. 같은 돔을 다시 쓰므로 소품을 켜고 끈다.</summary>
public enum SeabedDressing
{
    CoralReef = 0,     // 산호·해초·조개·바위 + 산호 군락 조명 (1막)
    RockBed = 1,       // 산호 없는 암반 — 바위만 남기고 큰 바위를 더 깐다 (2막)
    Bare = 2,          // 거의 비운다 (3막)
}

/// <summary>
/// 층 하나의 할 일·생물·조명. DiveDirector 가 얕은 층부터 순서대로 읽는다 (24m → 200m → 1,000m).
/// 할 일은 기획 문서의 의뢰서 두 줄이다: "아무 생물이나 N종 기록" + "지목 생물 찾기"(이름 없이 단서만).
/// 같은 돔을 층마다 다시 꾸민다 — 돔 지오메트리는 그대로 두고 생물과 조명만 바꾼다.
/// </summary>
[CreateAssetMenu(fileName = "DiveMission", menuName = "DomeAquarium/Dive Mission")]
public sealed class DiveMission : ScriptableObject
{
    [Header("층")]
    public DiveDepth depth;
    public float meters = 24f;
    public string layerName = "산호초";
    public float rideSeconds = 9f;             // 바로 위 층과 이 층 사이를 오가는 시간

    [Header("할 일 1 — N종 기록")]
    public string countSubject = "아무 생물이나";
    [Min(0)] public int recordCount = 4;

    [Header("할 일 2 — 지목 생물 (이 중 하나를 기록하면 된다)")]
    // 해마류처럼 여러 종을 한 무리로 묶는다. 비면 이 줄은 없다.
    public FishData[] targets;
    [TextArea(1, 3)] public string targetClue;

    [Header("등장 생물 — 늘 돔 안을 헤엄치는 무리 (BoidManager)")]
    public FishData[] species;

    [Header("무리 교체 — 몇 분마다 한 무리가 떠나고 아직 안 나온 종이 들어온다 (BoidManager)")]
    // 동시에 헤엄치는 마릿수(= Quest 드로우콜 예산)는 그대로 두고, 한 번의 탐사에서 만나는 종을 늘린다.
    // species 중 여기 없는 종(볼거리·지목 생물)은 떠나지 않는다. 비면 교체하지 않는다.
    public FishData[] rotation;
    public Vector2 rotationInterval = new Vector2(100f, 160f);

    [Header("가끔 지나가는 큰 생물 — 한 번에 한 무리, passInterval 마다 (LargeCreaturePasses)")]
    // 2막의 기본 진행: 큰 생물이 늘 돌아다니지 않고 정해진 간격으로 돔 가까이 지나간다. 기다리는 법을 배운다.
    public PassingCreature[] passers;
    public Vector2 passInterval = new Vector2(60f, 90f);   // 한 무리가 나타난 때부터 다음 무리까지
    public float firstPassDelay = 14f;                     // 도착 안내가 끝날 즈음 첫 무리 — 90초 안에 반드시 한 번은 지나간다

    [Serializable]
    public struct PassingCreature
    {
        public FishData fish;
        public Vector2Int group;                // 한 번에 몇 마리 (다랑어는 떼, 돌고래는 작은 무리, 나머지는 혼자)
        [Min(5f)] public float seconds;         // 돔을 가로지르는 시간 — 빠른 다랑어는 짧게, 쥐가오리는 길게
        [Min(0f)] public float weight;
    }

    [Header("바닥·물")]
    public SeabedDressing seabed = SeabedDressing.CoralReef;
    public bool marineSnow;                    // 위에서 천천히 가라앉는 바다눈 (햇빛이 끝나는 200m 부터)
    public bool bioluminescence;               // 어둠 속에서 잠깐씩 반짝이는 푸른 빛 알갱이 (1,000m)

    [Header("3막 — 신호의 주인 (UnknownCreature)")]
    public bool encounter;                     // 이 층에서 미확인 생물을 깨운다
    [TextArea(1, 2)] public string taskLine;   // 할 일 한 줄을 직접 쓴다 ("신호의 정체 확인하기"). 비면 "N종 기록하기"
    public bool showSignal;                    // 할 일 판에 신호 세기 막대
    public bool lightOnArrival;                // 도착할 때 관찰기 조명을 켜 둔다 (어둠 — 그리고 꺼야 할 빛)
    [TextArea(1, 3)] public string hintLine;   // 조명을 켠 채 두 번 놓치면 관측소장이

    [Header("엔딩 — 이 층을 마치면 엘리베이터가 저절로 올라간다")]
    public bool finale;
    [TextArea(1, 3)] public string ascentLine; // 해수면까지 올라가는 엘리베이터 안에서
    [TextArea(2, 4)] public string finalLine;  // 밤이 된 로비, 데이터 월에 새 줄이 생긴 뒤 — 그다음 크레딧

    [Header("소리")]
    public AudioClip ambience;                 // 층 물소리 (12초 고리)
    [Range(0f, 1f)] public float ambienceVolume = 0.25f;
    [Range(0f, 1f)] public float musicLevel = 1f;   // 3막은 음악을 끄고 낮은 물소리만

    [Header("조명")]
    // 실제 시계를 따르는 것은 1막뿐이다. 나머지 층은 lighting 으로 고정한다.
    public bool useClock = true;
    public TimeOfDayController.Key lighting;
    public bool caustics = true;               // 바닥의 물결 무늬 빛 — 햇빛이 넉넉히 닿는 층에서만

    [Header("관측소장")]
    [TextArea(2, 4)] public string descentLine;    // 이 층으로 내려오는 엘리베이터 안에서
    [TextArea(2, 4)] public string arrivalLine;    // 도착해서 할 일을 알려 줄 때
    [TextArea(2, 4)] public string completeLine;   // 할 일을 다 했을 때

    public string DepthLabel => FormatMeters(meters);
    public bool HasPassers => passers != null && passers.Length > 0;

    /// <summary>이 층에서 기록할 수 있는 모든 종 — 늘 헤엄치는 무리 + 가끔 지나가는 큰 생물.</summary>
    public IEnumerable<FishData> AllSpecies
    {
        get
        {
            if (species != null) foreach (var f in species) if (f != null) yield return f;
            if (passers != null) foreach (var p in passers) if (p.fish != null) yield return p.fish;
        }
    }

    public bool HasTarget => targets != null && targets.Length > 0;

    public bool IsTarget(FishData fish)
    {
        if (fish == null || targets == null) return false;
        foreach (var t in targets) if (t == fish) return true;
        return false;
    }

    /// <summary>"24m", "1,000m" — 계기판·버튼·할 일 제목이 같은 표기를 쓴다.</summary>
    public static string FormatMeters(float m) => $"{m:#,0}m";
}
