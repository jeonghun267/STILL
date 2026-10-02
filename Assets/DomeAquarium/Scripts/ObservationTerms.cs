/// <summary>
/// 화면에 보이는 "관찰 기록" 용어를 한곳에 모은다.
///
/// 이 체험은 생물을 잡아 모으는 게 아니라, 아직 아무도 보지 못한 바다를 한 칸씩 기록하는 것이다.
/// 그래서 "도감·발견·뽑기·등급" 같은 수집 게임 말투 대신 "관찰 일지·기록·탐사·관측 빈도"를 쓴다.
/// 정보 카드·도감·손목 메뉴가 같은 말을 쓰도록 문자열은 여기서만 고친다.
/// </summary>
public static class ObservationTerms
{
    /// <summary>
    /// 희귀도는 게임 아이템 등급이 아니라 "사람이 얼마나 드물게 관측했나"로 읽는다.
    /// FishRarity 순서(Common → Legendary)와 같은 길이 5.
    /// </summary>
    public static readonly string[] Frequency = { "흔함", "보통", "드묾", "매우 드묾", "극히 드묾" };

    public const string FrequencyLabel = "관측 빈도";
    public const string LogTitle = "관찰 일지";
    public const string LogTitleEnglish = "OBSERVATION LOG";
    public const string Unobserved = "미관찰";
    public const string UnobservedCreature = "아직 기록되지 않은 생물";
    public const string RecordedOnly = "기록한 생물";
    public const string NextDive = "다음 탐사";
    public const string OpenLog = "관찰 일지";
    public const string Enter = "입장하기";

    public static string FrequencyName(int index)
    {
        if (index < 0) index = 0;
        if (index >= Frequency.Length) index = Frequency.Length - 1;
        return Frequency[index];
    }

    /// <summary>"관측 빈도 · 드묾" — 정보 카드와 도감 상세가 같은 형식을 쓴다.</summary>
    public static string FrequencyLine(int index) => FrequencyLabel + "  ·  " + FrequencyName(index);

    public static string Progress(int recorded, int total) => $"{LogTitle}  ·  {recorded}/{total}종 기록";
}
