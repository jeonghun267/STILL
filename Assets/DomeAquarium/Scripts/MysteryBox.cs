using UnityEngine;

// 미스터리 박스 — 희귀도 가중 랜덤으로 FishData 를 뽑아 FishReveal 에 넘긴다.
// 최근 뽑힌 종은 링버퍼로 기억해서 연속 중복을 막는다 (매번 List 할당 금지).
public class MysteryBox : MonoBehaviour
{
    [Header("참조")]
    public Transform boxVisual;
    public ParticleSystem burstFx;
    public FishReveal reveal;

    [Header("뽑기")]
    public int noRepeatWindow = 3;
    public FishData[] pool;

    // FishRarity 순서: Common, Uncommon, Rare, Epic, Legendary
    public float[] rarityWeights = { 60f, 25f, 10f, 4f, 1f };

    [Header("소환 위치")]
    public Transform player;                            // 비면 Camera.main
    public float summonDistance = 0.85f;                // 화면을 과하게 채우지 않는 거리
    public float summonDrop = 0.10f;                    // 눈높이에서 살짝 아래

    // 물고기가 다가와 멈추는 지점. 상자와 **같은 방향**으로 같이 옮겨야 한다.
    // 상자만 눈앞에 있고 물고기가 등 뒤에서 나오면 소용이 없다.
    public Transform revealStage;
    public float stageDistance = 1.35f;
    public float stageDrop = 0.02f;

    [Header("사운드")]
    public AudioSource sfx;                             // 없으면 조용히 건너뛴다
    public AudioClip pokeClip;

    [Header("상자 애니메이션 (RPG Pack / BoxOfPandora)")]
    public Animator boxAnimator;                        // 없으면 전부 건너뛴다
    public RuntimeAnimatorController idleController;    // 평소 — 뚜껑이 들썩이며 별이 샌다
    public RuntimeAnimatorController crashController;   // 건드린 순간 — 상자가 터진다

    // 프리팹에 멀쩡한 상자(Box001)와 부서진 조각 더미(Destroy_box)가 **둘 다** 들어 있고
    // 기본적으로 둘 다 켜져 있다. 그대로 두면 상자가 두 개 겹쳐 보인다.
    public GameObject intactBox;                        // 평소 보이는 쪽
    public GameObject shatteredBox;                     // 터졌을 때만 보이는 조각 더미
    public float shardVisibleTime = 1.4f;               // 조각이 보이는 시간

    [Header("밤 가시성")]
    [Tooltip("밤에 상자가 어두운 물속에 묻히지 않도록 자기 텍스처 색으로 발광(알베도 배수, 한밤 기준). " +
             "판도라 상자 나무 텍스처가 어두워서 1.3 은 줘야 밤 물빛보다 밝게 읽힌다. 0 이면 끈다")]
    [Min(0f)] public float nightGlow = 1.3f;

    [Header("유휴 연출")]
    public float idleSpinSpeed = 25f;      // 초당 회전 각도. 상자 애니메이터가 있으면 자동으로 0 취급
    public float bobAmplitude = 0.06f;     // 위아래 진폭 (m)
    public float bobSpeed = 1.2f;          // 보빙 주기 배속

    // --- 내부 캐시 (Update 에서 할당하지 않기 위해 전부 미리 잡는다) ---
    private FishData[] recentBuffer;       // 고정 크기 링버퍼
    private int recentHead;
    private int recentFilled;

    const int RarityCount = 5;
    private readonly int[] rarityCandidateCounts = new int[RarityCount];

    private Vector3 boxBaseLocalPos;
    private Quaternion boxBaseLocalRot;
    private bool hasVisual;
    private float spinAngle;
    private float bobPhase;

    private bool warnedEmptyPool;

    // RPG Pack 상자 안에는 흰 사각형으로 보이는 구형 별 파티클이 들어 있다.
    // 전용 거품 BurstFX 만 사용하도록 캐시해 두고 렌더링을 막는다.
    private ParticleSystem[] legacyBoxParticles;

    public bool CanTrigger
    {
        get { return reveal != null && !reveal.IsBusy && pool != null && pool.Length > 0; }
    }

    private void Awake()
    {
        CacheVisual();
        DisableLegacyBoxParticles();
        EnsureBuffers();
        SetShattered(false);   // 조각 더미는 터지기 전까지 숨긴다
    }

    private void Start()
    {
        if ((pool == null || pool.Length == 0) && !warnedEmptyPool)
        {
            warnedEmptyPool = true;
            Debug.LogWarning("[MysteryBox] pool 이 비어 있다. 뽑기가 동작하지 않는다.", this);
        }

        if (reveal == null)
        {
            Debug.LogWarning("[MysteryBox] reveal(FishReveal) 참조가 비어 있다.", this);
        }
    }

    private void CacheVisual()
    {
        hasVisual = boxVisual != null;
        if (hasVisual)
        {
            boxBaseLocalPos = boxVisual.localPosition;
            boxBaseLocalRot = boxVisual.localRotation;
            EnsureShowcaseGlow();
        }
    }

    // 밤에는 태양 0.28 + 앰비언트 0.05 수준이라 URP Lit 상자가 거의 검게 나온다.
    // 씬을 다시 빌드하지 않아도 되도록 없으면 여기서 붙인다. 이미 있으면 인스펙터 값을 존중한다.
    private void EnsureShowcaseGlow()
    {
        if (boxVisual == null || nightGlow <= 0f) return;
        if (boxVisual.GetComponent<ShowcaseGlow>() != null) return;
        ShowcaseGlow.Attach(boxVisual.gameObject, 0f, nightGlow);
    }

    private void DisableLegacyBoxParticles()
    {
        if (boxVisual == null) return;

        if (legacyBoxParticles == null)
            legacyBoxParticles = boxVisual.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem ps in legacyBoxParticles)
        {
            if (ps == null) continue;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emission = ps.emission;
            emission.enabled = false;

            ParticleSystemRenderer particleRenderer = ps.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null) particleRenderer.enabled = false;
        }
    }

    private void PlayBubbleBurst()
    {
        if (burstFx == null) return;
        burstFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        burstFx.Play(true);
    }

    // 최근 중복 방지용 링버퍼 크기 보정. 인스펙터에서 값이 바뀌어도 안전하게 재할당.
    private void EnsureBuffers()
    {
        int window = noRepeatWindow;
        if (window < 0) window = 0;

        if (recentBuffer == null || recentBuffer.Length != window)
        {
            recentBuffer = new FishData[window];
            recentHead = 0;
            recentFilled = 0;
        }
    }

    private void Update()
    {
        // boxVisual 이 Awake 이후에 배선되는 경우가 있어 한 번 더 잡아준다.
        if (!hasVisual)
        {
            if (boxVisual == null) return;
            CacheVisual();
        }

        float dt = Time.deltaTime;

        // 상자 모델이 자체 애니메이션을 갖고 있으면(BoxOfPandora) 회전은 그쪽에 맡기고
        // 코드 스핀은 끈다. 둘 다 돌면 어지럽다. 보빙만 남겨 "떠 있는" 느낌을 준다.
        float spin = boxAnimator != null ? 0f : idleSpinSpeed;

        spinAngle += spin * dt;
        if (spinAngle > 360f) spinAngle -= 360f;

        bobPhase += bobSpeed * dt;
        if (bobPhase > 6.2831853f) bobPhase -= 6.2831853f;

        boxVisual.localRotation = boxBaseLocalRot * Quaternion.Euler(0f, spinAngle, 0f);

        Vector3 p = boxBaseLocalPos;
        p.y += Mathf.Sin(bobPhase) * bobAmplitude;
        boxVisual.localPosition = p;
    }

    /// <summary>
    /// 상자를 플레이어 정면으로 불러온다.
    /// 상자가 계속 떠 있으면 시야를 가리고 어수선하므로, 뽑을 때만 나타났다가 연출이 끝나면 사라진다.
    /// 비활성 상태에서도 호출할 수 있어야 하므로 코루틴을 쓰지 않는다.
    /// </summary>
    public void Summon()
    {
        // 연출 중에 다시 부르면 물고기 앞에 상자가 끼어든다.
        if (reveal != null && reveal.IsBusy) return;

        // 도감이 열려 있는 동안 손목 메뉴는 숨겨지므로 정상 경로로는 여기 올 수 없다.
        // 그래도 숨겨진 버튼 콜라이더를 손이 스치는 등 헛입력이 들어오면 도감 위에 상자가 겹친다.
        FishEncyclopediaPanel encyclopedia = FindFirstObjectByType<FishEncyclopediaPanel>();
        if (encyclopedia != null && encyclopedia.IsOpen) return;

        // 다시 뽑기 화면에는 도감에서 생성한 관찰용 복제 모델이 절대 남지 않게 한다.
        FishEncyclopediaPanel.RemoveAllPreviewObjects();

        Transform p = player;
        if (p == null)
        {
            Camera cam = Camera.main;
            if (cam != null) p = cam.transform;
        }

        if (p != null)
        {
            Vector3 flat = p.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();

            transform.position = p.position + flat * summonDistance + Vector3.up * -summonDrop;
            transform.rotation = Quaternion.LookRotation(-flat, Vector3.up);

            // 연출 무대도 같은 방향으로 옮긴다. 이걸 빠뜨리면 물고기가 월드 +Z 쪽,
            // 최악의 경우 사용자 등 뒤에서 나타난다.
            if (revealStage != null)
                revealStage.position = p.position + flat * stageDistance + Vector3.up * -stageDrop;
        }

        SetShattered(false);
        if (boxAnimator != null && idleController != null)
        {
            boxAnimator.runtimeAnimatorController = idleController;
            boxAnimator.Rebind();
        }

        gameObject.SetActive(true);

        // 프리팹의 사각 별 파티클은 숨기고 상자 등장 순간부터 거품을 보여 준다.
        DisableLegacyBoxParticles();
        PlayBubbleBurst();
    }

    /// <summary>상자를 치운다. 물고기 연출이 끝난 뒤 호출된다.</summary>
    public void Dismiss()
    {
        if (burstFx != null)
            burstFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
    }

    // 뽑기 실행. reveal 이 연출 중이면 무시.
    public void Trigger()
    {
        if (reveal == null) return;
        if (reveal.IsBusy) return;

        AquariumAudioDirector.Instance?.PulseEventDuck(2.2f);

        if (pool == null || pool.Length == 0)
        {
            if (!warnedEmptyPool)
            {
                warnedEmptyPool = true;
                Debug.LogWarning("[MysteryBox] pool 이 비어 있다. 뽑기가 동작하지 않는다.", this);
            }
            return;
        }

        EnsureBuffers();

        // 1차: 최근 목록 제외하고 가중 랜덤
        int index = PickIndex(true);

        // 2차: 전부 제외됐으면 제약을 풀고 다시
        if (index < 0) index = PickIndex(false);

        // 3차: 가중치가 전부 0인 극단 케이스 — 균등 랜덤
        if (index < 0) index = PickUniform();

        if (index < 0) return;

        FishData data = pool[index];
        if (data == null) return;

        PushRecent(data);

        PlayBubbleBurst();
        if (sfx != null && pokeClip != null) sfx.PlayOneShot(pokeClip);
        PlayCrash();

        reveal.Show(data);
    }

    // 에디터 마우스 클릭은 같은 오브젝트의 PokeSelector 가 처리한다 (중복 발화 방지).

    private void SetShattered(bool shattered)
    {
        if (intactBox != null && intactBox.activeSelf == shattered) intactBox.SetActive(!shattered);
        if (shatteredBox != null && shatteredBox.activeSelf != shattered) shatteredBox.SetActive(shattered);
    }

    /// <summary>상자를 터뜨린다. 연출이 끝나면 조각을 치우고 상자를 물린다.</summary>
    private void PlayCrash()
    {
        SetShattered(true);

        // 애니메이터가 없어도(프리팹 폴백 큐브) 복구 코루틴은 반드시 돌아야 한다.
        // 예전에는 여기서 early return 하는 바람에 조각이 영원히 남고 Dismiss 도 안 됐다.
        if (boxAnimator != null && crashController != null)
        {
            boxAnimator.runtimeAnimatorController = crashController;
            boxAnimator.Rebind();
            boxAnimator.Update(0f);      // 첫 프레임부터 즉시 재생
        }

        StopCoroutine(nameof(RestoreIdleWhenDone));
        StartCoroutine(nameof(RestoreIdleWhenDone));
    }

    private System.Collections.IEnumerator RestoreIdleWhenDone()
    {
        yield return null;

        // 조각은 터지는 순간만 보여주고 금방 치운다.
        // 물고기 연출이 끝날 때까지(10초 남짓) 두면 부서진 조각이 바닥에 널브러진 채로 남는다.
        // 이때 멀쩡한 상자는 아직 되살리지 않는다 — 물고기를 보는 동안 상자가 도로 나타나면 어수선하다.
        yield return new WaitForSeconds(shardVisibleTime);
        if (shatteredBox != null) shatteredBox.SetActive(false);

        while (reveal != null && reveal.IsBusy) yield return null;

        // 연출이 끝나면 상자를 아예 치운다.
        // 계속 눈앞에 떠 있으면 시야를 가리고 어수선하다 — 다시 뽑고 싶으면 손목 메뉴에서 부른다.
        SetShattered(false);
        Dismiss();
    }

    // 등급을 설정 확률로 먼저 뽑은 뒤, 그 등급 안에서 종을 균등 선택한다.
    // 종마다 등급 가중치를 더하면 해당 등급의 종 수에 따라 실제 확률이 왜곡된다.
    private int PickIndex(bool respectRecent)
    {
        int len = pool.Length;
        System.Array.Clear(rarityCandidateCounts, 0, rarityCandidateCounts.Length);

        for (int i = 0; i < len; i++)
        {
            FishData d = pool[i];
            if (d == null || (respectRecent && IsRecent(d))) continue;
            rarityCandidateCounts[RarityIndex(d)]++;
        }

        float total = 0f;
        for (int r = 0; r < RarityCount; r++)
        {
            if (rarityCandidateCounts[r] > 0) total += WeightOfRarity(r);
        }
        if (total <= 0f) return -1;

        float roll = Random.value * total;
        float acc = 0f;
        int selectedRarity = -1;
        for (int r = 0; r < RarityCount; r++)
        {
            if (rarityCandidateCounts[r] <= 0) continue;
            float w = WeightOfRarity(r);
            acc += w;
            if (roll <= acc)
            {
                selectedRarity = r;
                break;
            }
        }

        if (selectedRarity < 0)
        {
            for (int r = RarityCount - 1; r >= 0; r--)
                if (rarityCandidateCounts[r] > 0 && WeightOfRarity(r) > 0f) { selectedRarity = r; break; }
        }
        if (selectedRarity < 0) return -1;

        int pick = Random.Range(0, rarityCandidateCounts[selectedRarity]);
        for (int i = 0; i < len; i++)
        {
            FishData d = pool[i];
            if (d == null || (respectRecent && IsRecent(d)) || RarityIndex(d) != selectedRarity) continue;
            if (pick == 0) return i;
            pick--;
        }
        return -1;
    }

    // 가중치가 모두 0으로 설정된 경우의 최후 폴백
    private int PickUniform()
    {
        int len = pool.Length;
        int valid = 0;
        for (int i = 0; i < len; i++)
        {
            if (pool[i] != null) valid++;
        }
        if (valid == 0) return -1;

        int pick = Random.Range(0, valid);
        for (int i = 0; i < len; i++)
        {
            if (pool[i] == null) continue;
            if (pick == 0) return i;
            pick--;
        }
        return -1;
    }

    private static int RarityIndex(FishData d)
    {
        return Mathf.Clamp((int)d.rarity, 0, RarityCount - 1);
    }

    private float WeightOfRarity(int rarity)
    {
        if (rarityWeights == null || rarityWeights.Length == 0) return 1f;
        int index = Mathf.Clamp(rarity, 0, rarityWeights.Length - 1);
        float w = rarityWeights[index];
        return w > 0f ? w : 0f;
    }

    private bool IsRecent(FishData d)
    {
        if (recentBuffer == null || recentFilled == 0) return false;
        for (int i = 0; i < recentFilled; i++)
        {
            if (recentBuffer[i] == d) return true;
        }
        return false;
    }

    private void PushRecent(FishData d)
    {
        if (recentBuffer == null || recentBuffer.Length == 0) return;

        recentBuffer[recentHead] = d;
        recentHead++;
        if (recentHead >= recentBuffer.Length) recentHead = 0;
        if (recentFilled < recentBuffer.Length) recentFilled++;
    }
}
