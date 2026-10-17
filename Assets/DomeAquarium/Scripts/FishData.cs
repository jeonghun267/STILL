using UnityEngine;

public enum FishRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

[CreateAssetMenu(fileName = "FishData", menuName = "DomeAquarium/Fish Data")]
public class FishData : ScriptableObject
{
    [Header("식별")]
    // 종 고유 ID. 프리팹 이름에서 만들어지며 절대 바뀌지 않는다.
    // Gemini 설명 캐시 키, 저장, 도감 기록이 전부 이 값을 기준으로 한다.
    public string speciesId;

    [Header("표시 정보")]
    public string koreanName;
    // 모델 팩의 프리팹 이름을 교정한 영문 통용명. Gemini 종 식별 보조와 부제에 쓴다.
    public string englishName;
    public string scientificName;

    // 비워 둔다. 해양생물 설명은 지어내지 않는다 — 런타임에 Gemini가 채운다.
    [TextArea(3, 8)] public string description;

    public string sizeTag;
    public string habitatTag;

    [Header("등급")]
    public FishRarity rarity = FishRarity.Common;

    [Header("서식 층")]
    // 어느 층 돔에 나오는가. 층별 등장 생물(DiveMission.species)을 고를 때 쓴다 (DomeAquarium/18).
    public DiveDepth habitat = DiveDepth.Reef24;
    // 강에 사는 종(아마존·인더스강 돌고래 등). 바다 관찰 기록에 나오면 사실 오류라 어느 층에도 넣지 않는다.
    public bool freshwater;
    // 일지와 일치하는 기록이 없는 생물(3막 신호의 주인). 카드는 이름 대신 "일치하는 기록 없음"과 최초 관측자를 쓰고,
    // 설명은 고정 대본(description)만 읽는다 — Gemini 에 묻지 않는다. AI 가 정체를 말해 버리면 미확인이 아니게 된다.
    public bool unnamed;

    [Header("모델")]
    public GameObject prefab;
    public float swimScale = 0.25f;
    public float revealScale = 0.4f;

    [Header("수직 층위 (0 = 바닥, 1 = 수면)")]
    [Range(0f, 1f)] public float bandCenter = 0.5f;
    [Range(0f, 1f)] public float bandWidth = 0.6f;

    [Header("유영")]
    // 실제 체장(m). 분리 반경과 순항 속도를 이 값에서 뽑는다.
    // 크기 보정 도구(DomeAquarium/10)가 채운다.
    public float bodyLength = 0.2f;
    public float swimSpeed = 0.9f;
    public int schoolCount = 6;

    public string DisplayName => string.IsNullOrWhiteSpace(koreanName) ? name : koreanName;

    public string CanonicalEnglishName => string.IsNullOrWhiteSpace(englishName)
        ? GeminiDescriptionService.ToEnglishName(name)
        : englishName;

    public string SubtitleName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(scientificName)) return scientificName;
            return CanonicalEnglishName;
        }
    }

    /// <summary>
    /// 이름 뒤에 받침에 맞는 주제 조사(은/는)를 붙인다. 카드에 "백상아리은"처럼 나오던 것을 바로잡는다.
    /// 마지막 글자가 한글 음절이 아니면(영문 이름 등) "은"을 쓴다.
    /// </summary>
    public static string WithTopicParticle(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        char last = word[word.Length - 1];
        bool hangul = last >= '가' && last <= '힣';
        bool hasFinal = !hangul || (last - '가') % 28 != 0;
        return word + (hasFinal ? "은" : "는");
    }

    // 네트워크가 끊기거나 모델이 정확한 종을 특정하지 못해도 관람이 끊기지 않도록
    // 프리팹 이름에서 확실히 알 수 있는 큰 분류까지만 말한다. 종별 수치나 서식지는 추측하지 않는다.
    public string FallbackDescription
    {
        get
        {
            string key = ((speciesId ?? string.Empty) + " " + CanonicalEnglishName).ToLowerInvariant();
            string subject = WithTopicParticle(DisplayName) + " ";

            if (key.Contains("shark"))
                return subject + "상어류에 속하는 연골어류예요. 몸을 좌우로 움직여 헤엄쳐요. 물속 생태계에서 중요한 역할을 해요.";
            if (key.Contains("turtle"))
                return subject + "바다거북류예요. 폐로 숨을 쉬기 때문에 수면으로 올라와요. 지느러미 모양의 앞다리로 헤엄쳐요.";
            if (key.Contains("dolphin") || key.Contains("dophin"))
                return subject + "돌고래류에 속하는 해양 포유류예요. 폐로 숨을 쉬며 무리를 이루기도 해요. 몸의 움직임과 소리로 소통해요.";
            if (key.Contains("ray"))
                return subject + "가오리류에 속하는 연골어류예요. 넓은 가슴지느러미를 물결치듯 움직여요. 납작한 몸으로 물속을 유영해요.";
            if (key.Contains("tuna"))
                return subject + "다랑어류예요. 유선형 몸과 강한 꼬리로 계속 헤엄쳐요. 먼 거리를 이동하는 종이 많은 무리예요.";
            if (key.Contains("seahorse"))
                return subject + "해마류예요. 몸을 세운 자세로 작은 지느러미를 움직여요. 꼬리로 해초 같은 곳을 붙잡을 수 있어요.";
            if (key.Contains("lobster"))
                return subject + "바닷가재류에 속하는 갑각류예요. 단단한 외골격과 여러 쌍의 다리가 있어요. 바닥을 걸어 다니며 생활해요.";
            if (key.Contains("grouper"))
                return subject + "바리류 물고기예요. 튼튼한 몸과 큰 입이 특징인 무리예요. 산호초 주변에서 만나는 종이 많아요.";
            if (key.Contains("angelfish") || key.Contains("butterfly") || key.Contains("idol"))
                return subject + "산호초 주변에서 만나는 열대어류예요. 옆으로 납작한 몸이 방향을 바꾸는 데 도움을 줘요. 무늬와 색은 종마다 달라요.";
            if (key.Contains("tang"))
                return subject + "양쥐돔류 물고기예요. 옆으로 납작한 몸과 힘센 꼬리로 헤엄쳐요. 여러 종이 산호초 주변에서 생활해요.";
            if (key.Contains("clownfish") || key.Contains("damsel") || key.Contains("chromis"))
                return subject + "자리돔과에 가까운 산호초 물고기예요. 몸집은 작지만 활발하게 헤엄쳐요. 종마다 색과 무늬가 다양해요.";
            if (key.Contains("goby") || key.Contains("dottyback") || key.Contains("basslet") ||
                key.Contains("cardinal") || key.Contains("dartfish") || key.Contains("tilefish"))
                return subject + "작은 산호초 물고기류예요. 바위나 산호 주변을 은신처로 이용하는 종이 많아요. 지느러미로 섬세하게 방향을 바꿔요.";

            return subject + "바다에 사는 생물이에요. 몸과 지느러미를 움직여 물속을 이동해요. 자세한 종 정보는 확인 중이에요.";
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(description);
}
