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
