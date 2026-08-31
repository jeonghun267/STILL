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
    public float summonDistance = 0.55f;                // 팔 뻗으면 닿는 거리
    public float summonDrop = 0.25f;                    // 눈높이보다 이만큼 아래

    // 물고기가 다가와 멈추는 지점. 상자와 **같은 방향**으로 같이 옮겨야 한다.
    // 상자만 눈앞에 있고 물고기가 등 뒤에서 나오면 소용이 없다.
    public Transform revealStage;
    public float stageDistance = 1.2f;
    public float stageDrop = 0.15f;

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

    [Header("유휴 연출")]
    public float idleSpinSpeed = 25f;      // 초당 회전 각도. 상자 애니메이터가 있으면 자동으로 0 취급
    public float bobAmplitude = 0.06f;     // 위아래 진폭 (m)
    public float bobSpeed = 1.2f;          // 보빙 주기 배속

    // --- 내부 캐시 (Update 에서 할당하지 않기 위해 전부 미리 잡는다) ---
    private FishData[] recentBuffer;       // 고정 크기 링버퍼
    private int recentHead;
    private int recentFilled;

    private float[] weightBuffer;          // 후보 가중치 재사용 버퍼

    private Vector3 boxBaseLocalPos;
    private Quaternion boxBaseLocalRot;
    private bool hasVisual;
    private float spinAngle;
    private float bobPhase;

    private bool warnedEmptyPool;

    public bool CanTrigger
    {
        get { return reveal != null && !reveal.IsBusy && pool != null && pool.Length > 0; }
    }

    private void Awake()
    {
        CacheVisual();
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
        }
    }

    // 링버퍼 / 가중치 버퍼 크기 보정. 인스펙터에서 값이 바뀌어도 안전하게 재할당.
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

        int poolLen = (pool != null) ? pool.Length : 0;
        if (weightBuffer == null || weightBuffer.Length < poolLen)
        {
            weightBuffer = new float[poolLen > 0 ? poolLen : 1];
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
    }

    /// <summary>상자를 치운다. 물고기 연출이 끝난 뒤 호출된다.</summary>
    public void Dismiss()
    {
        gameObject.SetActive(false);
    }

    // 뽑기 실행. reveal 이 연출 중이면 무시.
    public void Trigger()
    {
        if (reveal == null) return;
        if (reveal.IsBusy) return;

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

        if (burstFx != null) burstFx.Play();
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

    // respectRecent 가 true 면 최근 목록에 있는 종의 가중치를 0으로 만든다.
    private int PickIndex(bool respectRecent)
    {
        int len = pool.Length;
        float total = 0f;

        for (int i = 0; i < len; i++)
        {
            FishData d = pool[i];
            float w = WeightOf(d);
            if (w > 0f && respectRecent && IsRecent(d)) w = 0f;
            weightBuffer[i] = w;
            total += w;
        }

        if (total <= 0f) return -1;

        float roll = Random.value * total;
        float acc = 0f;

        for (int i = 0; i < len; i++)
        {
            float w = weightBuffer[i];
            if (w <= 0f) continue;
            acc += w;
            if (roll <= acc) return i;
        }

        // 부동소수 오차로 끝까지 못 골랐을 때: 유효한 마지막 후보
        for (int i = len - 1; i >= 0; i--)
        {
            if (weightBuffer[i] > 0f) return i;
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

    private float WeightOf(FishData d)
    {
        if (d == null) return 0f;
        if (rarityWeights == null || rarityWeights.Length == 0) return 1f;

        int r = (int)d.rarity;
        if (r < 0) r = 0;
        if (r >= rarityWeights.Length) r = rarityWeights.Length - 1;

        float w = rarityWeights[r];
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
