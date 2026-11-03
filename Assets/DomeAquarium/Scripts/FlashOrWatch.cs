using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 찍을까, 바라볼까 — 1,000m 신호의 주인이 처음 눈앞에 다가오면 관측소장이 "플래시로 찍을 수도 있다"고 일러 준다(B · Y 버튼).
///   플래시를 쓰면: 찰칵, 번쩍 — 생물은 놀라 어둠 속으로 물러나고(실패로 세지 않는다), 관찰 일지 사진은 흐린 빛 얼룩으로 남는다.
///                  다시 조명을 끄고 가만히 있으면 돌아온다 — 기록은 그때 지켜보며 한다.
///   그냥 지켜보면: 놀라지 않은 채 있는 그대로 기록된다 (DiveDirector 가 watchLine 으로 받아 준다).
/// 제안은 잠수마다 한 번. 플래시 빛은 심해 생물 셰이더 전역 값(_DeviceLight*)을 잠깐 아주 밝게 해서 낸다.
/// </summary>
[DefaultExecutionOrder(300)]                    // 관찰기 조명 · 이야기 장면 조명보다 나중에 전역 값을 쓴다
public sealed class FlashOrWatch : MonoBehaviour
{
    public UnknownCreature creature;
    public ObservationAim aim;
    public ObservationLight deviceLight;
    public LobbySequence lobby;
    public AudioSource sound;
    public AudioClip shutter;

    [TextArea(2, 4)] public string offerLine =
        "가까이 왔어요… 관찰기로 플래시 사진을 찍을 수도 있어요 — B 버튼이에요. 아니면, 이대로 가만히 지켜볼 수도 있고요.";
    [TextArea(2, 4)] public string flashLine =
        "찰칵 — 빛에 놀라 멀리 달아났어요. 사진은 흐린 빛 얼룩뿐이에요. 어둠에 사는 눈에 플래시는 한낮의 해보다 눈부셔요. " +
        "다시 불을 끄고 가만히 기다리면 돌아올지도 몰라요.";
    public string notice = "B 버튼 · 플래시로 찍기";
    public float flashSeconds = 0.25f;
    public float flashIntensity = 14f;

    static readonly int DevicePosId = Shader.PropertyToID("_DeviceLightPos");
    static readonly int DeviceDirId = Shader.PropertyToID("_DeviceLightDir");
    static readonly int DeviceParamsId = Shader.PropertyToID("_DeviceLightParams");
    static readonly int DeviceColorId = Shader.PropertyToID("_DeviceLightColor");

    InputAction press;
    bool offered, recorded;
    float flashLeft;
    Vector3 flashFrom, flashDir;

    /// <summary>이번 잠수에서 플래시를 썼나 — DiveDirector 가 기록 뒤 말을 고른다.</summary>
    public bool Used { get; private set; }
    public bool Offered => offered;

    void Awake()
    {
        press = new InputAction("FlashPhoto", InputActionType.Button);
        press.AddBinding("<XRController>{RightHand}/secondaryButton");    // B
        press.AddBinding("<XRController>{LeftHand}/secondaryButton");     // Y
        press.performed += _ => TryFlash();
    }

    void OnEnable()
    {
        press?.Enable();
        if (aim != null) aim.Recorded += OnRecorded;
    }

    void OnDisable()
    {
        press?.Disable();
        if (aim != null) aim.Recorded -= OnRecorded;
    }

    void OnDestroy() => press?.Dispose();

    /// <summary>새 잠수(1,000m 에 내려갈 때) — DiveDirector 가 부른다.</summary>
    public void ResetForDive()
    {
        offered = recorded = Used = false;
        flashLeft = 0f;
    }

    void OnRecorded(FishData fish)
    {
        if (creature != null && fish == creature.fish) recorded = true;
    }

    bool CreatureNear => creature != null && creature.isActiveAndEnabled &&
                         (creature.State == UnknownCreature.Mode.Close || creature.State == UnknownCreature.Mode.Approaching);

    void Update()
    {
        if (creature == null || !creature.isActiveAndEnabled || recorded) return;
        if (!offered && creature.State == UnknownCreature.Mode.Close)
        {
            offered = true;
            StartCoroutine(Offer());
        }
    }

    IEnumerator Offer()
    {
        // 하던 말 · 해설이 끝나면 (오래 기다리지는 않는다)
        float start = Time.time;
        while (FishNarrationService.Instance != null && FishNarrationService.Instance.IsSpeaking && Time.time - start < 6f) yield return null;
        if (recorded || Used) yield break;
        if (aim != null) aim.ShowNotice(notice, 14f);
        if (lobby != null) yield return lobby.SayLine(offerLine);
    }

    public void TryFlash()
    {
        if (!offered || Used || recorded || !CreatureNear) return;
        Used = true;
        if (sound != null && shutter != null) sound.PlayOneShot(shutter);
        Transform eye = Camera.main != null ? Camera.main.transform : null;
        flashFrom = aim != null && aim.HasAim ? aim.AimOrigin : eye != null ? eye.position : transform.position;
        flashDir = (creature.transform.position - flashFrom).normalized;
        flashLeft = flashSeconds;
        var fader = Camera.main != null ? ScreenFader.Ensure(Camera.main) : null;
        if (fader != null) StartCoroutine(fader.Flash(0.75f, 0.45f));
        // 번쩍인 그 순간의 사진 — 흐리게, 그리고 그대로 둔다(뒤에 지켜보며 기록해도 바꾸지 않는다)
        if (creature.fish != null)
            ObservationPhotos.Take(creature.fish, eye != null ? eye.position : flashFrom, creature.transform.position, 1.6f,
                                   "플래시에 놀라 달아나며 흐릿하게 찍혔어요", blur: true, lockIt: true);
        creature.Startle();
        StartCoroutine(SayLater());
    }

    IEnumerator SayLater()
    {
        yield return new WaitForSeconds(0.8f);
        if (lobby != null) yield return lobby.SayLine(flashLine);
    }

    void LateUpdate()
    {
        if (flashLeft <= 0f) return;
        flashLeft -= Time.deltaTime;
        if (flashLeft <= 0f)
        {
            // 관찰기 조명이 꺼져 있으면 전역 값도 끈다 (켜져 있으면 다음 프레임에 관찰기 조명이 다시 쓴다)
            if (deviceLight == null || !deviceLight.On) Shader.SetGlobalVector(DevicePosId, Vector4.zero);
            return;
        }
        float k = flashLeft / flashSeconds;
        Shader.SetGlobalVector(DevicePosId, new Vector4(flashFrom.x, flashFrom.y, flashFrom.z, 1f));
        Shader.SetGlobalVector(DeviceDirId, flashDir);
        Shader.SetGlobalVector(DeviceParamsId, new Vector4(18f, Mathf.Cos(70f * Mathf.Deg2Rad), Mathf.Cos(40f * Mathf.Deg2Rad), flashIntensity * k * k));
        Shader.SetGlobalColor(DeviceColorId, new Color(1f, 0.98f, 0.95f));
    }

#if UNITY_EDITOR
    public void DebugFlash()
    {
        offered = true;
        TryFlash();
    }
#endif
}
