/// <summary>
/// 화면에 보이는 "관찰 기록" 용어를 한곳에 모은다.
///
/// 이 체험은 생물을 잡아 모으는 게 아니라, 아직 아무도 보지 못한 바다를 한 칸씩 기록하는 것이다.
/// 그래서 "도감·발견·뽑기·등급" 같은 수집 게임 말투 대신 "관찰 일지·기록·탐사"를 쓴다.
/// 주 관람객이 10~15세라 한자어(관측 빈도·극히 드묾·미관찰·미확인)도 쓰지 않고, 읽자마자 뜻이 오는 말로 쓴다.
/// 정보 카드·도감·손목 메뉴가 같은 말을 쓰도록 문자열은 여기서만 고친다.
/// </summary>
public static class ObservationTerms
{
    /// <summary>
    /// 희귀도는 게임 아이템 등급이 아니라 "바다에서 얼마나 자주 보이나"로 읽는다.
    /// FishRarity 순서(Common → Legendary)와 같은 길이 5. 도감 칸(폭 116px)에 들어가도록 다섯 글자 안팎으로.
    /// </summary>
    public static readonly string[] Frequency = { "자주 보여요", "가끔 보여요", "잘 안 보여요", "아주 귀해요", "만나면 행운" };

    // 예전 씬 문구를 옮기는 데만 쓴다 (DomeAquariumLobbyBuilder.ApplyObservationTerms). 화면에는 더 이상 붙이지 않는다.
    public const string FrequencyLabel = "얼마나 자주 보이나";
    public const string LogTitle = "관찰 일지";
    public const string LogTitleEnglish = "OBSERVATION LOG";
    public const string Unobserved = "아직 못 봤어요";
    public const string UnobservedShort = "못 봤어요";       // 도감 칸처럼 좁은 곳
    public const string UnobservedCreature = "아직 기록하지 않은 생물";
    public const string AllCreatures = "모든 생물";
    public const string RecordedOnly = "기록한 생물";
    public const string NotYetRecorded = "아직 못 본 생물";
    public const string NextDive = "다음 탐사";
    public const string OpenLog = "관찰 일지";
    public const string Enter = "입장하기";

    // 관찰기(손에 든 기록 장치) 문구
    public const string AimHint = "헤엄치는 생물에 관찰기를 겨누세요";
    public const string Unidentified = "처음 보는 생물";      // "누구"는 사람에게 쓰는 말이라 관찰 장비 화면에는 맞지 않는다
    public const string Watching = "지켜보는 중";
    public const string Recorded = "기록됨";
    public const string LoggedToJournal = "관찰 일지에 기록됨";
    public const string Narration = "음성 해설";

    public static string SessionCount(int recorded) => $"이번 탐사 {recorded}종 기록";

    // 층마다 할 일 (기획 문서의 "의뢰서"). 관찰기 위에 두 줄로 뜬다. "의뢰·임무"보다 바로 읽히는 "할 일"로 쓴다.
    public static string LayerTasks(string depth) => $"수심 {depth}  ·  할 일";
    public static string LayerDone(string depth) => $"수심 {depth}  ·  기록 완료";
    public static string CountTask(string subject, int count) => $"{subject} {count}종 기록하기";
    public static string TargetTask(string clue) => $"찾아보기: {clue}";
    public static string Tally(int recorded, int count) => $"{System.Math.Min(recorded, count)}/{count}";
    public const string RideElevator = "엘리베이터 타기";

    // 엘리베이터 깊이 버튼 아래 작은 글씨
    public const string FloorHere = "지금 여기";
    public const string FloorNext = "갈 차례";
    public const string FloorGoing = "가는 중";
    public const string FloorDone = "기록 완료";
    public const string FloorLocked = "잠김";

    // 관찰기 조명 (1막을 마치면 생긴다)
    public const string LightOn = "조명 켜짐";
    public const string LightOff = "조명 꺼짐";
    // 관찰기 화면 한 줄(약 16자)에 들어가게 짧게. 손 추적이면 바로 옆 "조명" 버튼이 보인다.
    public const string LightUnlocked = "조명이 생겼어요 · A 버튼";
    public const string LightHint = "조명 끄기 · A/X 버튼 · 가만히";

    // 3막 — 신호의 주인. "미확인" 대신 기획 문서의 관찰기 표시 그대로 "일치하는 기록 없음".
    public const string SignalLabel = "신호";
    public const string NoMatch = "일치하는 기록 없음";
    public const string FirstSighting = "처음 관측된 생물";
    public const string YouObserver = "나";
    public static string FirstObserver(string name, System.DateTime when) => $"최초 관측자 · {name} · {when:yyyy.MM.dd}";

    // 엔딩 — 밤 로비 데이터 월의 새 줄, 크레딧 뒤 다시 하기
    public const string NewRecordTitle = "+1  처음 관측된 생물";
    public static string NewRecordDetail(string depth, string observer, System.DateTime when) =>
        $"수심 {depth}  ·  최초 관측자 {observer}  ·  {when:yyyy.MM.dd}";
    public const string EndTitle = "오늘 밤의 관측 끝";
    public const string Restart = "처음부터 다시";

    // 큰 생물 예고 (2막). 무엇인지는 말하지 않는다 — 어느 쪽인지만 알려 주고, 누구인지는 지켜봐서 알아낸다.
    public static string Approaching(string where) => $"무언가 다가와요 · {where}";

    /// <summary>머리 방향 기준 여덟 방향. 0 = 앞, 시계 방향(오른쪽이 +).</summary>
    public static readonly string[] Directions = { "앞", "오른쪽 앞", "오른쪽", "오른쪽 뒤", "뒤", "왼쪽 뒤", "왼쪽", "왼쪽 앞" };

    /// <summary>signedYaw: 머리 정면에서 몇 도 돌아가 있나 (-180~180, 오른쪽이 +).</summary>
    public static string Direction(float signedYaw)
    {
        int i = (int)System.Math.Round(signedYaw / 45.0);
        i = ((i % 8) + 8) % 8;
        return Directions[i];
    }

    public static string FrequencyName(int index)
    {
        if (index < 0) index = 0;
        if (index >= Frequency.Length) index = Frequency.Length - 1;
        return Frequency[index];
    }

    /// <summary>정보 카드와 도감 상세에 쓰는 한 줄. 문장 자체가 뜻을 말하므로 "관측 빈도 ·" 같은 머리말을 붙이지 않는다.</summary>
    public static string FrequencyLine(int index) => FrequencyName(index);

    public static string Progress(int recorded, int total) => $"{LogTitle}  ·  {recorded}/{total}종 기록";

    /// <summary>손목 메뉴 아래줄 — 버튼에 "관찰 일지"가 이미 있으니 숫자만.</summary>
    public static string WristProgress(int recorded, int total) => $"{recorded} / {total}종 기록";
}
