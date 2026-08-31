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
    public string scientificName;

    // 비워 둔다. 해양생물 설명은 지어내지 않는다 — 런타임에 Gemini가 채운다.
    [TextArea(3, 8)] public string description;

    public string sizeTag;
    public string habitatTag;

    [Header("등급")]
    public FishRarity rarity = FishRarity.Common;

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

    public bool HasDescription => !string.IsNullOrWhiteSpace(description);
}
