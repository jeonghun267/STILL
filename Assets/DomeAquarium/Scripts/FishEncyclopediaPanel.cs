using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손목 메뉴에서 여는 월드 스페이스 수중 태블릿 도감.
/// 고정된 12개 카드 슬롯을 페이지마다 재사용해 VR UI 오브젝트 수를 일정하게 유지한다.
/// </summary>
public sealed class FishEncyclopediaPanel : MonoBehaviour
{
    const string PreviewObjectPrefix = "EncyclopediaPreview_";
    // 상세 영역 부제 자리표시자. 설정 스크립트와 런타임이 같은 문구를 쓴다. 286px 한 줄에 들어가는 길이로.
    public const string DefaultSubtitle = "기록한 생물을 3D로 다시 관찰할 수 있어요";

    [Header("루트")]
    public CanvasGroup canvasGroup;
    public Transform viewer;
    public float openDistance = 1.05f;
    public float openDrop = 0.08f;

    [Header("데이터")]
    public FishData[] allFish;

    [Header("상단")]
    public TMP_Text progressText;
    public TMP_Text profileText;
    public TMP_Text pageText;
    public TMP_Text filterStatusText;
    public Image[] filterChips;              // 보기 칩 (전체 · 기록한 생물 · 아직 못 본 생물)
    public Color chipColor = new Color(0.08f, 0.25f, 0.31f, 1f);
    public Color chipActiveColor = new Color(0.08f, 0.48f, 0.60f, 1f);

    [Header("카드 슬롯")]
    public GameObject[] cardRoots;
    public Button[] cardButtons;
    public Image[] cardFrames;
    public TMP_Text[] cardNames;
    public TMP_Text[] cardRarities;
    public TMP_Text[] cardLockedMarks;

    [Header("상세")]
    public Transform previewAnchor;
    public TMP_Text detailNameText;
    public TMP_Text detailSubtitleText;
    public TMP_Text detailRarityText;
    public TMP_Text detailDescriptionText;
    public TMP_Text detailCountText;
    public GameObject narrationButtonRoot;
    public float previewForwardOffset = 0.11f;

    [Header("내가 찍은 사진 (ObservationPhotos — 기록하는 순간 관찰기 사진)")]
    public GameObject photoRow;                 // 사진이 있을 때만 켠다 — 없으면 설명 칸이 그만큼 넓어진다
    public RawImage photo;
    public TMP_Text photoCaption;

    [Header("관찰 모델 가시성")]
    [Tooltip("관찰창 모델은 낮에도 살짝 또렷하게 — 시간대와 무관하게 항상 더하는 발광(알베도 배수). 작게 유지")]
    [Min(0f)] public float previewBaseGlow = 0.05f;
    [Tooltip("밤에 관찰창 물고기가 검게 보이지 않도록 한밤 기준으로 더하는 발광(알베도 배수). 0.5 안팎이면 음영이 남는다")]
    [Min(0f)] public float previewNightGlow = 0.5f;

    static readonly Color[] RarityColors =
    {
        new Color(0.60f, 0.69f, 0.76f, 1f),
        new Color(0.36f, 0.82f, 0.49f, 1f),
        new Color(0.25f, 0.62f, 1.00f, 1f),
        new Color(0.72f, 0.38f, 0.96f, 1f),
        new Color(1.00f, 0.72f, 0.18f, 1f)
    };
    static readonly string[] RarityNames = ObservationTerms.Frequency;

    readonly List<FishData> filteredFish = new List<FishData>(128);
    FishCollectionStore store;
    FishData selectedFish;
    string selectedDescription;
    GameObject previewHolder;
    GameObject previewModel;
    Renderer[] previewRenderers;
    int page;
    int rarityFilter = -1;
    bool discoveredOnly;
    bool undiscoveredOnly;
    int detailRequestToken;
    bool isOpen;

    public bool IsOpen => isOpen;

    void Awake()
    {
        RemoveAllPreviewObjects();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        SetCanvasVisible(false);
        BindCardSlots();
    }

    void Start()
    {
        store = FishCollectionStore.Instance;
        if (store != null) store.Changed += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (store != null) store.Changed -= Refresh;
        DestroyPreview();
    }

    void OnDisable()
    {
        if (Application.isPlaying) DestroyPreview();
    }

    void Update()
    {
        if (previewHolder != null && isOpen)
        {
            previewHolder.transform.position = PreviewCenterPosition();
            previewHolder.transform.Rotate(Vector3.up, 24f * Time.deltaTime, Space.World);
            CenterPreviewModel();
        }
    }

    public void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    public void Open()
    {
        ResolveViewer();
        if (viewer != null)
        {
            Vector3 forward = viewer.forward;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            else flatForward.Normalize();

            transform.position = viewer.position + flatForward * openDistance - Vector3.up * openDrop;
            Vector3 away = transform.position - viewer.position;
            transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        FishInfoPanel info = Object.FindFirstObjectByType<FishInfoPanel>();
        if (info != null) info.Hide();

        // 뽑기 전시 도중 도감을 열면 전시용 인스턴스가 태블릿 뒤에서 계속 남을 수 있다.
        // 도감은 독립 관찰 화면이므로 진행 중인 전시를 먼저 완전히 정리한다.
        FishReveal activeReveal = Object.FindFirstObjectByType<FishReveal>();
        if (activeReveal != null) activeReveal.CancelCurrentReveal();

        // 도감은 큰 관찰 화면이므로 기존 상자와 작은 손목 메뉴가 앞을 가리지 않게 정리한다.
        MysteryBox box = Object.FindFirstObjectByType<MysteryBox>();
        if (box != null) box.Dismiss();
        SetWristMenuSuppressed(true);

        isOpen = true;
        SetCanvasVisible(true);
        Refresh();
    }

    public void Close()
    {
        isOpen = false;
        SetCanvasVisible(false);
        FishNarrationService.Instance?.Stop();
        DestroyPreview();
        SetWristMenuSuppressed(false);
    }

    public void PreviousProfile()
    {
        EnsureStore();
        store?.SelectRelativeProfile(-1);
        page = 0;
        ClearDetail();
        Refresh();
    }

    public void NextProfile()
    {
        EnsureStore();
        store?.SelectRelativeProfile(1);
        page = 0;
        ClearDetail();
        Refresh();
    }

    public void PreviousPage()
    {
        int max = MaxPage();
        page = page <= 0 ? max : page - 1;
        RefreshCards();
    }

    public void NextPage()
    {
        int max = MaxPage();
        page = page >= max ? 0 : page + 1;
        RefreshCards();
    }

    public void FilterAll() { SetRarityFilter(-1); }
    public void FilterCommon() { SetRarityFilter((int)FishRarity.Common); }
    public void FilterUncommon() { SetRarityFilter((int)FishRarity.Uncommon); }
    public void FilterRare() { SetRarityFilter((int)FishRarity.Rare); }
    public void FilterEpic() { SetRarityFilter((int)FishRarity.Epic); }
    public void FilterLegendary() { SetRarityFilter((int)FishRarity.Legendary); }
    public void FilterDiscovered()
    {
        discoveredOnly = true;
        undiscoveredOnly = false;
        rarityFilter = -1;
        page = 0;
        ClearDetail();
        Refresh();
    }

    /// <summary>아직 못 본 생물만. 부스 관람객에게는 빈도별 필터보다 "남은 것"이 더 쓸모 있다.</summary>
    public void FilterUndiscovered()
    {
        discoveredOnly = false;
        undiscoveredOnly = true;
        rarityFilter = -1;
        page = 0;
        ClearDetail();
        Refresh();
    }

    public void PlaySelectedNarration()
    {
        if (selectedFish == null || string.IsNullOrWhiteSpace(selectedDescription)) return;
        FishNarrationService.Instance?.Speak(selectedFish.DisplayName, selectedDescription);
    }

    void SetRarityFilter(int value)
    {
        discoveredOnly = false;
        undiscoveredOnly = false;
        rarityFilter = value;
        page = 0;
        ClearDetail();
        Refresh();
    }

    void Refresh()
    {
        EnsureStore();
        RebuildFilter();

        int total = allFish != null ? allFish.Length : 0;
        int found = FoundCount();
        if (progressText != null) progressText.text = ObservationTerms.Progress(found, total);
        if (profileText != null) profileText.text = store != null ? store.ActiveProfile.displayName : "탐험가 1";
        // 지금 고른 보기 칩만 밝힌다 (0 전체 · 1 기록한 생물 · 2 아직 못 본 생물).
        if (filterChips != null)
        {
            int active = discoveredOnly ? 1 : undiscoveredOnly ? 2 : 0;
            for (int c = 0; c < filterChips.Length; c++)
                if (filterChips[c] != null) filterChips[c].color = c == active ? chipActiveColor : chipColor;
        }

        if (filterStatusText != null)
            filterStatusText.text = discoveredOnly ? ObservationTerms.RecordedOnly
                : undiscoveredOnly ? ObservationTerms.NotYetRecorded
                : rarityFilter < 0 ? ObservationTerms.AllCreatures
                : RarityNames[Mathf.Clamp(rarityFilter, 0, 4)];

        page = Mathf.Clamp(page, 0, MaxPage());
        RefreshCards();

        if (selectedFish != null && store != null && !store.IsDiscovered(selectedFish)) ClearDetail();
    }

    void RebuildFilter()
    {
        filteredFish.Clear();
        if (allFish == null) return;

        for (int i = 0; i < allFish.Length; i++)
        {
            FishData fish = allFish[i];
            if (fish == null) continue;
            if (rarityFilter >= 0 && (int)fish.rarity != rarityFilter) continue;
            if (discoveredOnly && !IsFound(fish)) continue;
            if (undiscoveredOnly && IsFound(fish)) continue;
            filteredFish.Add(fish);
        }

        filteredFish.Sort((a, b) =>
        {
            int rarity = ((int)a.rarity).CompareTo((int)b.rarity);
            return rarity != 0 ? rarity : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.CurrentCulture);
        });
    }

    void RefreshCards()
    {
        int slotCount = cardRoots != null ? cardRoots.Length : 0;
        int start = page * Mathf.Max(1, slotCount);

        for (int i = 0; i < slotCount; i++)
        {
            int dataIndex = start + i;
            bool hasData = dataIndex < filteredFish.Count;
            if (cardRoots[i] != null) cardRoots[i].SetActive(hasData);
            if (!hasData) continue;

            FishData fish = filteredFish[dataIndex];
            bool found = IsFound(fish);
            int rarity = Mathf.Clamp((int)fish.rarity, 0, 4);

            // 기록한 칸: 이름(두 줄까지) + 얼마나 자주 보이는지. 미관찰 칸: 큰 물음표 + "못 봤어요".
            // "?"·"???"·"미관찰"을 한 칸에 겹쳐 쓰던 것을 줄였다.
            if (cardNames != null && i < cardNames.Length && cardNames[i] != null)
            {
                cardNames[i].text = found ? fish.DisplayName : "?";
                cardNames[i].color = found ? Color.white : new Color(0.45f, 0.56f, 0.63f);
            }
            if (cardRarities != null && i < cardRarities.Length && cardRarities[i] != null)
            {
                cardRarities[i].text = found ? RarityNames[rarity] : ObservationTerms.UnobservedShort;
                cardRarities[i].color = found ? RarityColors[rarity] : new Color(0.55f, 0.64f, 0.70f);
            }
            if (cardLockedMarks != null && i < cardLockedMarks.Length && cardLockedMarks[i] != null)
            {
                // 모서리의 작은 색 점 — 기록한 종만. 빈 마름모(◇)는 "아직 비어 있음"으로 읽혀 반대였다.
                cardLockedMarks[i].text = found ? "●" : string.Empty;
                cardLockedMarks[i].color = RarityColors[rarity];
            }
            if (cardFrames != null && i < cardFrames.Length && cardFrames[i] != null)
                // 카드 스프라이트가 흰 라운드(색이 그대로 보임)로 바뀌어 기본색을 설정 스크립트와 맞춘다.
                // 미관찰 칸도 가장 어두운 채널을 13/255 위로 둔다 — Quest 는 그 아래를 구분하지 못해 칸 경계가 사라진다.
                cardFrames[i].color = found
                    ? Color.Lerp(new Color(0.08f, 0.22f, 0.29f, 0.97f), RarityColors[rarity], 0.18f)
                    : new Color(0.07f, 0.15f, 0.20f, 0.92f);
            if (cardButtons != null && i < cardButtons.Length && cardButtons[i] != null)
                cardButtons[i].interactable = true;
        }

        int pages = MaxPage() + 1;
        if (pageText != null) pageText.text = filteredFish.Count == 0 ? "0 / 0" : $"{page + 1} / {pages}";
    }

    void BindCardSlots()
    {
        if (cardButtons == null) return;
        for (int i = 0; i < cardButtons.Length; i++)
        {
            int slot = i;
            Button button = cardButtons[i];
            if (button != null) button.onClick.AddListener(() => SelectSlot(slot));

            if (cardRoots != null && i < cardRoots.Length && cardRoots[i] != null)
            {
                PokeSelector poke = cardRoots[i].GetComponent<PokeSelector>();
                if (poke != null) poke.onPoked.AddListener(() => SelectSlot(slot));
            }
        }
    }

    void SelectSlot(int slot)
    {
        int slotCount = cardRoots != null ? cardRoots.Length : 0;
        int index = page * Mathf.Max(1, slotCount) + slot;
        if (index < 0 || index >= filteredFish.Count) return;

        FishData fish = filteredFish[index];
        bool found = store != null && store.IsDiscovered(fish);
        if (!found)
        {
            ShowLockedDetail();
            return;
        }

        selectedFish = fish;
        int rarity = Mathf.Clamp((int)fish.rarity, 0, 4);
        if (detailNameText != null) detailNameText.text = fish.DisplayName;
        if (detailSubtitleText != null) detailSubtitleText.text = fish.SubtitleName;
        if (detailRarityText != null)
        {
            detailRarityText.text = ObservationTerms.FrequencyLine(rarity);
            detailRarityText.color = RarityColors[rarity];
        }
        if (detailCountText != null) detailCountText.text = $"관찰 {store.GetDiscoveryCount(fish)}회";
        if (narrationButtonRoot != null) narrationButtonRoot.SetActive(true);
        ShowPhoto(fish);

        int token = ++detailRequestToken;
        selectedDescription = fish.HasDescription ? fish.description : fish.FallbackDescription;
        if (detailDescriptionText != null) detailDescriptionText.text = selectedDescription;
        BuildPreview(fish);

        if (!fish.HasDescription && GeminiDescriptionService.Instance != null)
        {
            GeminiDescriptionService.Instance.Request(fish, resolved =>
            {
                if (this == null || selectedFish != fish || detailRequestToken != token) return;
                if (!string.IsNullOrWhiteSpace(resolved)) selectedDescription = resolved;
                if (detailDescriptionText != null) detailDescriptionText.text = selectedDescription;
            });
        }
    }

    void ShowLockedDetail()
    {
        ClearDetail();
        if (detailNameText != null) detailNameText.text = ObservationTerms.UnobservedCreature;
        if (detailSubtitleText != null) detailSubtitleText.text = "???";
        if (detailRarityText != null) detailRarityText.text = "탐사에서 처음 만나면 기록됩니다";
        if (detailDescriptionText != null)
            detailDescriptionText.text = "아직 아무도 기록하지 않았어요. 다음 탐사에서 이 생물을 만날지도 몰라요.";
    }

    void ClearDetail()
    {
        selectedFish = null;
        selectedDescription = null;
        detailRequestToken++;
        FishNarrationService.Instance?.Stop();
        DestroyPreview();
        if (detailNameText != null) detailNameText.text = "생물을 선택하세요";
        if (detailSubtitleText != null) detailSubtitleText.text = DefaultSubtitle;
        if (detailRarityText != null) detailRarityText.text = "";
        if (detailDescriptionText != null) detailDescriptionText.text = "카드를 선택하면 생김새와 설명이 여기에 표시됩니다.";
        if (detailCountText != null) detailCountText.text = "";
        if (narrationButtonRoot != null) narrationButtonRoot.SetActive(false);
        ShowPhoto(null);
    }

    void ShowPhoto(FishData fish)
    {
        bool has = ObservationPhotos.TryGet(fish, out Texture tex, out string caption);
        if (photoRow != null) photoRow.SetActive(has);
        if (!has) return;
        if (photo != null) photo.texture = tex;
        if (photoCaption != null) photoCaption.text = "<b>내가 찍은 사진</b>\n" + caption;
    }

    void BuildPreview(FishData fish)
    {
        DestroyPreview();
        if (fish == null || fish.prefab == null || previewAnchor == null) return;

        previewHolder = new GameObject(PreviewObjectPrefix + FishCollectionStore.StableId(fish));
        previewHolder.transform.position = PreviewCenterPosition();
        previewHolder.transform.rotation = previewAnchor.rotation;
        // 월드 루트에 두면 닫기 이벤트나 참조가 유실될 때 수조에 모델이 남는다.
        // 관찰 앵커 아래에 귀속시켜 패널을 닫는 즉시 계층 전체가 숨겨지게 한다.
        previewHolder.transform.SetParent(previewAnchor, true);

        previewModel = Instantiate(fish.prefab, previewHolder.transform.position, previewAnchor.rotation, previewHolder.transform);
        previewModel.name = PreviewObjectPrefix + "Model_" + FishCollectionStore.StableId(fish);
        Collider[] colliders = previewModel.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        Rigidbody[] bodies = previewModel.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
        }
        MonoBehaviour[] scripts = previewModel.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++) scripts[i].enabled = false;

        // 프리팹의 수영 애니메이션이 모델 루트를 옮기면 관찰창 중심에서 벗어나므로
        // 도감 미리보기에서는 정지 포즈로 두고 태블릿 자체 회전만 사용한다.
        Animator[] animators = previewModel.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++) animators[i].enabled = false;
        Animation[] animations = previewModel.GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < animations.Length; i++) animations[i].enabled = false;

        // 관찰창은 늘 또렷해야 한다 — 낮에도 약간, 밤에는 충분히 자기 색으로 발광시킨다.
        // (위에서 프리팹의 스크립트를 전부 껐으므로 반드시 그 뒤에 붙인다.)
        if (previewNightGlow > 0f || previewBaseGlow > 0f)
            ShowcaseGlow.Attach(previewModel, previewBaseGlow, previewNightGlow);

        previewModel.transform.localScale = Vector3.one * Mathf.Max(0.01f, fish.revealScale);
        previewRenderers = previewModel.GetComponentsInChildren<Renderer>(false);
        if (!TryGetPreviewBounds(out Bounds bounds)) return;

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        // 회전 중 긴 꼬리나 지느러미가 관찰 영역 밖으로 튀어나오지 않도록 여백을 확보한다.
        if (largest > 0.0001f)
            previewModel.transform.localScale *= 0.16f / largest;

        CenterPreviewModel();
    }

    void CenterPreviewModel()
    {
        if (previewModel == null || previewAnchor == null || !TryGetPreviewBounds(out Bounds bounds)) return;
        previewModel.transform.position += PreviewCenterPosition() - bounds.center;
    }

    Vector3 PreviewCenterPosition()
    {
        if (previewAnchor == null) return transform.position;
        ResolveViewer();
        Vector3 surfacePoint = previewAnchor.position;
        Vector3 towardViewer = viewer != null
            ? viewer.position - surfacePoint
            : -previewAnchor.forward;
        if (towardViewer.sqrMagnitude < 0.0001f) towardViewer = -previewAnchor.forward;
        return surfacePoint + towardViewer.normalized * previewForwardOffset;
    }

    bool TryGetPreviewBounds(out Bounds bounds)
    {
        bounds = default;
        if (previewRenderers == null) return false;

        bool found = false;
        for (int i = 0; i < previewRenderers.Length; i++)
        {
            Renderer renderer = previewRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    void DestroyPreview()
    {
        // Destroy는 프레임 끝까지 화면에 남을 수 있으므로 먼저 즉시 숨긴다.
        if (previewHolder != null) previewHolder.SetActive(false);
        previewHolder = null;
        previewModel = null;
        previewRenderers = null;
        RemoveAllPreviewObjects();
    }

    /// <summary>
    /// 스크립트 리컴파일이나 도메인 재로드로 참조가 끊긴 미리보기 복제본까지 정리한다.
    /// 실제 수조 생물은 이 전용 접두사를 사용하지 않아 영향을 받지 않는다.
    /// </summary>
    public static void RemoveAllPreviewObjects()
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || !candidate.name.StartsWith(PreviewObjectPrefix, System.StringComparison.Ordinal))
                continue;

            GameObject preview = candidate.gameObject;
            preview.SetActive(false);
            if (Application.isPlaying) Object.Destroy(preview);
            else Object.DestroyImmediate(preview);
        }
    }

    void EnsureStore()
    {
        if (store == null) store = FishCollectionStore.Instance;
    }

    bool IsFound(FishData fish)
    {
#if UNITY_EDITOR
        if (store == null && previewFound != null) return previewFound.Contains(fish);
#endif
        return store != null && store.IsDiscovered(fish);
    }

    /// <summary>손목 메뉴의 진행 막대용 — 일지를 한 번도 열지 않았어도 지금 탐험가의 기록 수를 센다.</summary>
    public int RecordedCount()
    {
        EnsureStore();
        return FoundCount();
    }

    public int TotalCount => allFish != null ? allFish.Length : 0;

    int FoundCount()
    {
#if UNITY_EDITOR
        if (store == null && previewFound != null) return previewFound.Count;
#endif
        return store != null ? store.GetDiscoveredSpeciesCount() : 0;
    }

#if UNITY_EDITOR
    // 에디터 미리보기(DomeAquariumCardPreview)용. 플레이 전에는 기록 저장소가 없어서 모든 칸이 "???"로만 보인다.
    HashSet<FishData> previewFound;

    /// <summary>기록 저장소 없이 일부 종을 기록한 것으로 보고 화면을 채운다. 한 종은 상세 칸에 띄운다.</summary>
    public void PreviewInEditor(int foundCount, FishData detailFish)
    {
        previewFound = new HashSet<FishData>();
        // 한 칸씩 건너 채워서 기록한 카드와 미관찰 카드가 섞인 모습을 본다.
        for (int i = 0; allFish != null && i < allFish.Length && previewFound.Count < foundCount; i += 2)
            if (allFish[i] != null) previewFound.Add(allFish[i]);
        if (detailFish != null) previewFound.Add(detailFish);

        store = null;
        page = 0;
        rarityFilter = -1;
        discoveredOnly = false;
        undiscoveredOnly = false;
        Refresh();

        if (detailFish == null) return;
        int rarity = Mathf.Clamp((int)detailFish.rarity, 0, 4);
        if (detailNameText != null) detailNameText.text = detailFish.DisplayName;
        if (detailSubtitleText != null) detailSubtitleText.text = detailFish.SubtitleName;
        if (detailRarityText != null)
        {
            detailRarityText.text = ObservationTerms.FrequencyLine(rarity);
            detailRarityText.color = RarityColors[rarity];
        }
        if (detailDescriptionText != null) detailDescriptionText.text = detailFish.FallbackDescription;
        if (detailCountText != null) detailCountText.text = "관찰 1회";
        if (narrationButtonRoot != null) narrationButtonRoot.SetActive(true);
        ShowPhoto(detailFish);
    }

    public void EndPreview()
    {
        previewFound = null;
        ClearDetail();
    }
#endif

    void ResolveViewer()
    {
        if (viewer != null) return;
        Camera cam = Camera.main;
        if (cam != null) viewer = cam.transform;
    }

    int MaxPage()
    {
        int perPage = cardRoots != null && cardRoots.Length > 0 ? cardRoots.Length : 1;
        return Mathf.Max(0, Mathf.CeilToInt(filteredFish.Count / (float)perPage) - 1);
    }

    void SetCanvasVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        // CanvasGroup 알파는 MeshRenderer에는 적용되지 않는다. 3D 미리보기의 부모를
        // 직접 비활성화해야 삭제가 프레임 끝까지 지연되어도 밖에 보이지 않는다.
        if (previewAnchor != null) previewAnchor.gameObject.SetActive(visible);
    }

    void SetWristMenuSuppressed(bool value)
    {
        WristMenu menu = Object.FindFirstObjectByType<WristMenu>();
        if (menu != null) menu.suppressed = value;
    }
}
