using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 밤 로비 이야기 ⑤ 다음 신호 2,500m. 데이터 월 마지막 줄 "새 신호 · 수심 2,500m" — 작은 잠수정을 타고 해저 열수구로.
///   잠수정 안 오른쪽 화면은 밖에 있는 로봇 카메라(ROV). 아래 단추로 카메라를 좌우로 돌려 화면 가운데 십자에 생물을 맞추고
///   잠시 지켜보면 기록된다(쌍안경 · 관찰기와 같은 "가만히 지켜보기"). 검은 굴뚝 · 관벌레 · 열수구 게 셋.
///   다 기록하면 화면에 또 다른 신호가 잡힌다 — 이번엔 더 깊은 곳. 열린 결말.
/// 동태평양 해령(수심 2,500m 남짓)처럼 한 곳에 사는 생물만 둔다 — 대서양 열수구의 눈 없는 새우는 섞지 않는다.
/// 굴뚝 · 관벌레 · 게는 Sketchfab 무료 모델(CC-BY): 마리아나 "세쿼이아" 열수구 스캔 · Riftia 관벌레 무리 · 걷는 게(하얗게).
/// 햇빛이 없는 깊이라 로봇 카메라 조명(rovLight)이 비추는 곳만 보인다 — 심해 생물 셰이더 전역 값(_DeviceLight*)으로 비춘다(Quest 는 추가 조명이 꺼져 있다).
/// </summary>
[DefaultExecutionOrder(200)]                    // 조명 전역 값을 관찰기 조명(ObservationLight)보다 나중에 쓴다
public sealed class NextSignalStory : NightStory
{
    [System.Serializable]
    public class Target
    {
        public string name;
        public Transform point;
        [TextArea(2, 4)] public string line;
        [System.NonSerialized] public bool recorded;
    }

    [Header("잠수정")]
    public GameObject scene;
    public Transform cabin;                     // 잠수정 안 가운데 (도착하면 눈높이에)
    public Transform rovPivot;                  // 로봇 카메라 — 좌우로 돈다
    public Camera rovCamera;
    public float yawLimit = 70f;
    public float yawStep = 20f;
    public float turnSpeed = 45f;               // °/s
    public float aimDegrees = 9f;
    public float dwell = 1.5f;
    public Transform[] propellers;              // 추진기 프로펠러 — 제자리를 지키느라 늘 천천히, 돌 때는 빠르게 돈다
    public float propIdle = 150f;               // °/s

    [Header("로봇 조명")]
    public Transform rovLight;                  // 로봇 카메라 위 — 카메라와 같이 돈다
    public float lightRange = 13f;
    public float lightAngle = 70f;
    public float lightIntensity = 3.2f;
    public Color lightColor = new Color(0.85f, 0.93f, 1f);

    [Header("화면")]
    public TMP_Text monitorTop;
    public TMP_Text monitorStatus;
    public TMP_Text crosshair;
    public Target[] targets;
    public AudioSource ping;
    public AudioClip pingClip;
    public AudioClip recordClip;

    [Header("말")]
    [TextArea(2, 4)] public string introLine =
        "데이터 월 마지막 줄의 새 신호 — 수심 2,500m. 잠수정이 해저 열수구에 내려왔어요. 햇빛은 한 줄기도 닿지 않지만, 땅속에서 뜨거운 물이 뿜어져 나와요.";
    [TextArea(2, 4)] public string howLine =
        "오른쪽 화면은 밖에 있는 로봇 카메라예요. 아래 단추로 카메라를 돌려 화면 가운데 십자에 맞추고, 잠시 지켜보면 기록돼요. 세 가지를 찾아보세요.";
    [TextArea(1, 3)] public string nudgeLine = "단추로 카메라를 왼쪽, 오른쪽으로 돌려 보세요. 굴뚝 둘레에 무언가 살고 있어요.";
    [TextArea(2, 4)] public string signalLine = "…방금, 또 신호가 잡혔어요. 이번엔 훨씬 더 깊은 곳이에요. 바다는 아직, 우리가 모르는 것으로 가득해요.";

    float yaw, targetYaw;
    float watch;
    Target watching;
    int count;
    float nudgeAt;

    public int Recorded => count;

    void Awake()
    {
        if (scene != null) scene.SetActive(false);
    }

    public override void Enter()
    {
        if (scene != null) scene.SetActive(true);
        yaw = targetYaw = -20f;
        count = 0;
        watching = null;
        watch = 0f;
        if (targets != null) foreach (var t in targets) t.recorded = false;
        if (rovCamera != null) rovCamera.enabled = true;
        if (monitorTop != null) monitorTop.text = "로봇 카메라 · 수심 2,500m";
        if (monitorStatus != null) monitorStatus.text = "기록 0 / 3";
        ApplyYaw();
    }

    public override void Arrived(Transform view)
    {
        if (cabin == null || view == null) return;
        Vector3 p = cabin.position;
        float dy = view.position.y - p.y;
        scene.transform.position += Vector3.up * dy;                       // 잠수정과 바깥 열수구를 함께 눈높이에 맞춘다
    }

    public override void Exit()
    {
        StopAllCoroutines();
        talking = false;
        if (rovCamera != null) rovCamera.enabled = false;
        if (scene != null) scene.SetActive(false);
        Shader.SetGlobalVector(DevicePosId, Vector4.zero);                   // 로봇 조명 끄기 — 관찰기 조명이 켜져 있으면 다음 프레임에 다시 쓴다
    }

    static readonly int DevicePosId = Shader.PropertyToID("_DeviceLightPos");
    static readonly int DeviceDirId = Shader.PropertyToID("_DeviceLightDir");
    static readonly int DeviceParamsId = Shader.PropertyToID("_DeviceLightParams");
    static readonly int DeviceColorId = Shader.PropertyToID("_DeviceLightColor");

    void LateUpdate()
    {
        if (scene == null || !scene.activeInHierarchy || rovLight == null) return;
        Vector3 p = rovLight.position;
        float outer = lightAngle * 0.5f, inner = outer * 0.55f;
        Shader.SetGlobalVector(DevicePosId, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalVector(DeviceDirId, rovLight.forward);
        Shader.SetGlobalVector(DeviceParamsId, new Vector4(lightRange, Mathf.Cos(outer * Mathf.Deg2Rad), Mathf.Cos(inner * Mathf.Deg2Rad), lightIntensity));
        Shader.SetGlobalColor(DeviceColorId, lightColor);
    }

    public override IEnumerator Play()
    {
        yield return Narrate(introLine);
        yield return Narrate(howLine);
        nudgeAt = Time.time + 25f;
        while (count < (targets != null ? targets.Length : 0))
        {
            if (Time.time > nudgeAt && !talking) { nudgeAt = Time.time + 40f; StartCoroutine(Narrate(nudgeLine)); }
            yield return null;
        }
        while (talking) yield return null;                                  // 마지막 기록 해설이 끝난 뒤
        yield return new WaitForSeconds(1.5f);

        // 또 다른 신호
        for (int i = 0; i < 3; i++)
        {
            if (ping != null && pingClip != null) ping.PlayOneShot(pingClip);
            if (monitorTop != null) monitorTop.text = i % 2 == 0 ? "<color=#7FDBFF>새 신호 · 수심 ????m</color>" : "로봇 카메라 · 수심 2,500m";
            yield return new WaitForSeconds(0.9f);
        }
        if (monitorTop != null) monitorTop.text = "<color=#7FDBFF>새 신호 · 수심 ????m</color>";
        yield return Narrate(signalLine);
        yield return new WaitForSeconds(2f);
    }

    void Update()
    {
        if (scene == null || !scene.activeInHierarchy) return;
        float before = yaw;
        yaw = Mathf.MoveTowards(yaw, targetYaw, turnSpeed * Time.deltaTime);
        ApplyYaw();
        SpinPropellers(yaw != before);
        if (rovCamera == null || targets == null) return;

        // 화면 가운데 십자에 가장 가까운 아직 기록하지 않은 대상
        Target best = null;
        float bestAngle = aimDegrees;
        foreach (var t in targets)
        {
            if (t == null || t.recorded || t.point == null) continue;
            // 로봇 카메라는 좌우로만 돈다 — 좌우 각도로만 맞췄는지 본다 (굴뚝 꼭대기 · 바닥의 관벌레는 높이가 달라 위아래까지 따지면 영영 안 맞았다)
            Vector3 f = rovCamera.transform.forward, to = t.point.position - rovCamera.transform.position;
            f.y = 0f;
            to.y = 0f;
            float a = Vector3.Angle(f, to);
            if (a < bestAngle) { best = t; bestAngle = a; }
        }
        if (best != watching) { watching = best; watch = 0f; }
        if (watching == null)
        {
            if (crosshair != null) crosshair.color = new Color(1f, 1f, 1f, 0.6f);
            return;
        }
        watch += Time.deltaTime;
        if (crosshair != null) crosshair.color = Color.Lerp(Color.white, new Color(0.45f, 1f, 0.6f), watch / dwell);
        if (monitorStatus != null) monitorStatus.text = $"지켜보는 중 · {watching.name}  {Mathf.RoundToInt(Mathf.Clamp01(watch / dwell) * 100f)}%";
        if (watch >= dwell) Record(watching);
    }

    void Record(Target t)
    {
        t.recorded = true;
        count++;
        watching = null;
        watch = 0f;
        if (ping != null && recordClip != null) ping.PlayOneShot(recordClip);
        if (monitorStatus != null) monitorStatus.text = $"기록 {count} / {targets.Length} · {t.name}";
        StartCoroutine(Narrate(t.line));
    }

    void ApplyYaw()
    {
        if (rovPivot != null) rovPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void SpinPropellers(bool turning)
    {
        if (propellers == null) return;
        float step = propIdle * (turning ? 5f : 1f) * Time.deltaTime;
        for (int i = 0; i < propellers.Length; i++)
            if (propellers[i] != null) propellers[i].Rotate(0f, 0f, i % 2 == 0 ? step : -step, Space.Self);
    }

    public void TurnLeft() => targetYaw = Mathf.Clamp(targetYaw - yawStep, -yawLimit, yawLimit);
    public void TurnRight() => targetYaw = Mathf.Clamp(targetYaw + yawStep, -yawLimit, yawLimit);

    bool talking;

    /// <summary>관측소장 말 — 기록이 빨리 이어져도 자막이 겹치지 않게 앞 말이 끝나기를 기다린다.</summary>
    IEnumerator Narrate(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        while (talking) yield return null;
        talking = true;
        var lobby = FindFirstObjectByType<LobbySequence>();
        if (lobby != null) yield return lobby.SayLine(line);
        talking = false;
    }

#if UNITY_EDITOR
    public float DebugYaw => yaw;
    public void DebugAimAt(int i)
    {
        if (targets == null || i < 0 || i >= targets.Length || rovPivot == null || targets[i].point == null) return;
        Vector3 to = rovPivot.parent.InverseTransformPoint(targets[i].point.position) - rovPivot.localPosition;
        targetYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
    }
#endif
}
