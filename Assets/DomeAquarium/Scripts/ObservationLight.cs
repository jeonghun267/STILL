using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 관찰기 조명 (기획 문서 "관찰기 업그레이드" — 1막 할 일을 마치면 열린다).
/// 관찰기 앞 약 4~5m 를 비추는 빔. 어두운 200m·1,000m 에서 쓴다.
/// 3막에서는 스스로 꺼야 하는 장비다 — 미확인 생물은 빛을 피한다. 성장과 엔딩 규칙이 이 하나로 이어진다.
///
/// 켜고 끄기: 컨트롤러 A/X 버튼, 관찰기 화면 옆 "조명" 버튼(손으로 찌르기), 에디터에서는 L 키.
/// 빛은 관찰기가 겨누는 방향(ObservationAim 의 조준선)을 따른다 — 손이 안 잡히면 시선 방향.
/// </summary>
public sealed class ObservationLight : MonoBehaviour
{
    public ObservationAim aim;
    public Transform view;                     // 비면 Camera.main
    public Light spot;
    public Renderer beam;                      // 빛줄기 원뿔 (가산 혼합, 꼭짓점 알파로 끝이 사라진다)

    [Header("관찰기 화면 옆 버튼")]
    public GameObject button;                  // 열리기 전에는 숨긴다
    public Image buttonFront;
    public TMP_Text buttonLabel;
    public Color onTint = new Color(0.95f, 0.78f, 0.35f, 1f);
    public Color offTint = new Color(0.40f, 0.50f, 0.58f, 1f);

    [Header("소리")]
    public AudioSource clickSource;
    public AudioClip clickClip;

    [Header("빔 모양")]
    public float beamLength = 3.2f;
    public float beamRadius = 0.8f;
    public Material beamMaterial;

    /// <summary>돔 밖(밤 해변 이야기)에서도 A/X 로 켜고 끈다 — 평소에는 관찰기를 겨누는 동안(aim.armed)만 받는다.</summary>
    [NonSerialized] public bool allowOutside;

    public bool Unlocked { get; private set; }
    public bool On { get; private set; }

    /// <summary>켜고 끌 때마다 (켜졌나).</summary>
    public event Action<bool> Changed;

    InputAction toggle;
    float lastToggle = -99f;

#if UNITY_EDITOR
    // 헤드셋 없이 "조명을 켜고 생물을 겨누기"를 흉내 낸다 (디버그 메뉴). 에디터의 조준선은 마우스를 따라가 방향을 정할 수 없다.
    [NonSerialized] public Transform debugAimAt;
#endif

    void Awake()
    {
        if (aim == null) aim = FindFirstObjectByType<ObservationAim>(FindObjectsInactive.Include);
        if (beam == null && beamMaterial != null) beam = BuildBeam();
        Apply();
    }

    void OnEnable()
    {
        toggle = new InputAction("ObservationLightToggle", InputActionType.Button);
        toggle.AddBinding("<XRController>{RightHand}/primaryButton");   // A
        toggle.AddBinding("<XRController>{LeftHand}/primaryButton");    // X
#if UNITY_EDITOR
        toggle.AddBinding("<Keyboard>/l");
#endif
        toggle.performed += OnToggle;
        toggle.Enable();
    }

    void OnDisable()
    {
        if (lit) { Shader.SetGlobalVector(DevicePosId, Vector4.zero); lit = false; }
        if (toggle == null) return;
        toggle.performed -= OnToggle;
        toggle.Disable();
        toggle.Dispose();
        toggle = null;
    }

    void OnToggle(InputAction.CallbackContext _) => Toggle();

    /// <summary>1막 할 일을 마치면 DiveDirector 가 부른다. 버튼이 나타나고 켤 수 있게 된다.</summary>
    public void Unlock()
    {
        if (Unlocked) return;
        Unlocked = true;
        Apply();
    }

    /// <summary>입력이 부른다. 관찰기를 들고 있을 때(돔 안)만 받는다.</summary>
    public void Toggle()
    {
        if (!Unlocked || (aim != null && !aim.armed && !allowOutside)) return;
        // 손가락으로 찌르면 물리 트리거(PokeSelector)와 UI 찌르기(Button)가 같은 순간에 둘 다 온다 — 두 번 바뀌면 그대로다.
        if (Time.unscaledTime - lastToggle < 0.4f) return;
        lastToggle = Time.unscaledTime;
        SetOn(!On);
        if (clickSource != null && clickClip != null) clickSource.PlayOneShot(clickClip);
    }

    public void SetOn(bool on)
    {
        if (on && !Unlocked) return;
        if (On == on) return;
        On = on;
        Apply();
        Changed?.Invoke(on);
    }

    void Apply()
    {
        if (spot != null) spot.enabled = On;
        if (beam != null) beam.enabled = On;
        if (button != null && button.activeSelf != Unlocked) button.SetActive(Unlocked);
        if (buttonFront != null) buttonFront.color = On ? onTint : offTint;
        if (buttonLabel != null) buttonLabel.text = On ? ObservationTerms.LightOn : ObservationTerms.LightOff;
    }

    void LateUpdate()
    {
        if (!On)
        {
            if (lit) { Shader.SetGlobalVector(DevicePosId, Vector4.zero); lit = false; }
            return;
        }
        Vector3 origin, dir;
        if (aim != null && aim.HasAim) { origin = aim.AimOrigin; dir = aim.AimDirection; }
        else
        {
            Transform v = ResolveView();
            if (v == null) return;
            origin = v.position - v.up * 0.15f;
            dir = v.forward;
        }
#if UNITY_EDITOR
        if (debugAimAt != null) dir = (debugAimAt.position - origin).normalized;
#endif
        if (dir.sqrMagnitude < 1e-6f) return;
        transform.SetPositionAndRotation(origin + dir * 0.06f, Quaternion.LookRotation(dir, Vector3.up));
        PublishToShaders();
    }

    // Quest 용 URP 설정은 추가 조명이 꺼져 있어 이 스포트라이트가 실제로는 아무것도 비추지 않는다.
    // 심해 생물 셰이더(DomeAquarium/Deep Creature)가 이 값으로 직접 빛을 계산한다 — 조명에 비칠 때만 제 색이 보인다.
    static readonly int DevicePosId = Shader.PropertyToID("_DeviceLightPos");
    static readonly int DeviceDirId = Shader.PropertyToID("_DeviceLightDir");
    static readonly int DeviceParamsId = Shader.PropertyToID("_DeviceLightParams");
    static readonly int DeviceColorId = Shader.PropertyToID("_DeviceLightColor");
    bool lit;

    void PublishToShaders()
    {
        float range = spot != null ? spot.range : 5.5f;
        float outer = (spot != null ? spot.spotAngle : 50f) * 0.5f;
        float inner = (spot != null ? spot.innerSpotAngle : 28f) * 0.5f;
        Vector3 p = transform.position;
        Shader.SetGlobalVector(DevicePosId, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalVector(DeviceDirId, transform.forward);
        Shader.SetGlobalVector(DeviceParamsId, new Vector4(range, Mathf.Cos(outer * Mathf.Deg2Rad), Mathf.Cos(inner * Mathf.Deg2Rad),
                                                          spot != null ? spot.intensity : 3f));
        Shader.SetGlobalColor(DeviceColorId, spot != null ? spot.color : Color.white);
        lit = true;
    }

    /// <summary>
    /// 이 점이 빛 안에 있나 — 켜져 있고, 빛 원뿔(spotAngle/2) 안이고, 몸 크기만큼 여유를 준다.
    /// 거리는 따지지 않는다: 미확인 생물은 멀리서도 겨누는 빛을 알아챈다.
    /// </summary>
    public bool IsLighting(Vector3 point, float radius)
    {
        if (!On) return false;
        Vector3 to = point - transform.position;
        float dist = to.magnitude;
        if (dist < 1e-3f) return true;
        float half = spot != null ? spot.spotAngle * 0.5f : 25f;
        float allowance = half + Mathf.Atan2(Mathf.Max(0f, radius), dist) * Mathf.Rad2Deg;
        return Vector3.Angle(transform.forward, to) <= allowance;
    }

    Transform ResolveView()
    {
        if (view != null) return view;
        Camera cam = Camera.main;
        if (cam != null) view = cam.transform;
        return view;
    }

    /// <summary>빛줄기 — 꼭짓점이 관찰기, 열린 원뿔. 끝으로 갈수록 투명해져 빛이 물속에 번지는 것처럼 보인다.</summary>
    Renderer BuildBeam()
    {
        const int segments = 24;
        var mesh = new Mesh { name = "ObservationLightBeam" };
        var vertices = new Vector3[segments * 2];
        var colors = new Color[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            vertices[i * 2] = new Vector3(Mathf.Cos(a) * 0.02f, Mathf.Sin(a) * 0.02f, 0f);
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(a) * beamRadius, Mathf.Sin(a) * beamRadius, beamLength);
            colors[i * 2] = new Color(0.85f, 0.92f, 1f, 0.16f);
            colors[i * 2 + 1] = new Color(0.85f, 0.92f, 1f, 0f);
            int n = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2; triangles[t + 1] = n * 2; triangles[t + 2] = i * 2 + 1;
            triangles[t + 3] = n * 2; triangles[t + 4] = n * 2 + 1; triangles[t + 5] = i * 2 + 1;
        }
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();             // 빛줄기 셰이더가 원뿔 면을 보는 각도로 밝기를 정한다
        mesh.RecalculateBounds();

        var go = new GameObject("Beam");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = beamMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }
}
