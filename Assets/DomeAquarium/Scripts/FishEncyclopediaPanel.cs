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

    static readonly Color[] RarityColors =
    {
        new Color(0.60f, 0.69f, 0.76f, 1f),
        new Color(0.36f, 0.82f, 0.49f, 1f),
        new Color(0.25f, 0.62f, 1.00f, 1f),
        new Color(0.72f, 0.38f, 0.96f, 1f),
        new Color(1.00f, 0.72f, 0.18f, 1f)
    };
    static readonly string[] RarityNames = { "일반", "고급", "희귀", "영웅", "전설" };

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
        int found = store != null ? store.GetDiscoveredSpeciesCount() : 0;
        if (progressText != null) progressText.text = $"해양생물 도감  ·  {found}/{total} 발견";
        if (profileText != null) profileText.text = store != null ? store.ActiveProfile.displayName : "탐험가 1";
        if (filterStatusText != null)
            filterStatusText.text = discoveredOnly ? "발견한 생물" :
                (rarityFilter < 0 ? "전체 생물" : RarityNames[Mathf.Clamp(rarityFilter, 0, 4)] + " 등급");

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
            if (discoveredOnly && (store == null || !store.IsDiscovered(fish))) continue;
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
            bool found = store != null && store.IsDiscovered(fish);
            int rarity = Mathf.Clamp((int)fish.rarity, 0, 4);

            if (cardNames != null && i < cardNames.Length && cardNames[i] != null)
                cardNames[i].text = found ? fish.DisplayName : "???";
            if (cardRarities != null && i < cardRarities.Length && cardRarities[i] != null)
            {
                cardRarities[i].text = found ? RarityNames[rarity] : "미발견";
                cardRarities[i].color = found ? RarityColors[rarity] : new Color(0.52f, 0.61f, 0.67f);
            }
            if (cardLockedMarks != null && i < cardLockedMarks.Length && cardLockedMarks[i] != null)
            {
                cardLockedMarks[i].text = found ? "◇" : "?";
                cardLockedMarks[i].color = found ? RarityColors[rarity] : new Color(0.28f, 0.38f, 0.45f);
            }
            if (cardFrames != null && i < cardFrames.Length && cardFrames[i] != null)
                cardFrames[i].color = found
                    ? Color.Lerp(new Color(0.025f, 0.12f, 0.17f, 0.97f), RarityColors[rarity], 0.22f)
                    : new Color(0.025f, 0.075f, 0.10f, 0.88f);
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
            detailRarityText.text = "발견 등급  ·  " + RarityNames[rarity];
            detailRarityText.color = RarityColors[rarity];
        }
        if (detailCountText != null) detailCountText.text = $"발견 {store.GetDiscoveryCount(fish)}회";
        if (narrationButtonRoot != null) narrationButtonRoot.SetActive(true);

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
        if (detailNameText != null) detailNameText.text = "미발견 생물";
        if (detailSubtitleText != null) detailSubtitleText.text = "???";
        if (detailRarityText != null) detailRarityText.text = "상자에서 처음 만나면 기록됩니다";
        if (detailDescriptionText != null)
            detailDescriptionText.text = "아직 관찰 기록이 없어요. 새로운 상자를 열어 이 생물의 모습을 확인해 보세요.";
    }

    void ClearDetail()
    {
        selectedFish = null;
        selectedDescription = null;
        detailRequestToken++;
        FishNarrationService.Instance?.Stop();
        DestroyPreview();
        if (detailNameText != null) detailNameText.text = "생물을 선택하세요";
        if (detailSubtitleText != null) detailSubtitleText.text = "발견한 카드에서 3D 생물을 관찰할 수 있어요";
        if (detailRarityText != null) detailRarityText.text = "";
        if (detailDescriptionText != null) detailDescriptionText.text = "카드를 선택하면 생김새와 설명이 여기에 표시됩니다.";
        if (detailCountText != null) detailCountText.text = "";
        if (narrationButtonRoot != null) narrationButtonRoot.SetActive(false);
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
