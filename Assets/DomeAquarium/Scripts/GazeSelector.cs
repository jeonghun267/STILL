using UnityEngine;
using UnityEngine.UI;

// 시선 응시 폴백 입력. Main Camera 에 붙되 기본은 비활성 상태로 씬에 들어간다.
// 손 추적이 안 될 때만 켠다.
public class GazeSelector : MonoBehaviour
{
    public MysteryBox mysteryBox;
    public float dwellTime = 2f;
    public float maxDistance = 6f;
    public Image reticleFill;
    public GameObject reticleRoot;
    public LayerMask hitMask = ~0;

    [Header("되감기 / 재입력")]
    public float rewindMultiplier = 3f;    // 시선이 벗어나면 이 배속으로 되감는다
    public float retriggerDelay = 1.5f;

    private Camera cam;
    private Transform camTf;
    private Collider[] boxColliders;       // MysteryBox 콜라이더 캐시 (Update 에서 GetComponent 금지)
    private float dwell;
    private float lastTriggerTime = -999f;

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        camTf = (cam != null) ? cam.transform : transform;

        boxColliders = (mysteryBox != null)
            ? mysteryBox.GetComponentsInChildren<Collider>(true)
            : null;

        dwell = 0f;

        if (reticleRoot != null) reticleRoot.SetActive(true);
        ApplyProgress(0f);
    }

    private void OnDisable()
    {
        dwell = 0f;
        ApplyProgress(0f);
        if (reticleRoot != null) reticleRoot.SetActive(false);
    }

    private void Update()
    {
        if (mysteryBox == null || camTf == null)
        {
            Decay(Time.deltaTime);
            ApplyProgress(dwell / Mathf.Max(0.01f, dwellTime));
            return;
        }

        float dt = Time.deltaTime;
        float duration = Mathf.Max(0.01f, dwellTime);

        bool onTarget = false;

        RaycastHit hit;
        if (Physics.Raycast(camTf.position, camTf.forward, out hit, maxDistance, hitMask, QueryTriggerInteraction.Collide))
        {
            onTarget = IsBoxCollider(hit.collider);
        }

        bool ready = mysteryBox.CanTrigger && (Time.unscaledTime - lastTriggerTime) >= retriggerDelay;

        if (onTarget && ready)
        {
            dwell += dt;
            if (dwell >= duration)
            {
                dwell = 0f;
                lastTriggerTime = Time.unscaledTime;
                mysteryBox.Trigger();
                ApplyProgress(0f);
                return;
            }
        }
        else
        {
            Decay(dt);
        }

        ApplyProgress(dwell / duration);
    }

    private void Decay(float dt)
    {
        if (dwell <= 0f)
        {
            dwell = 0f;
            return;
        }

        dwell -= dt * Mathf.Max(1f, rewindMultiplier);
        if (dwell < 0f) dwell = 0f;
    }

    private bool IsBoxCollider(Collider c)
    {
        if (c == null || boxColliders == null) return false;

        for (int i = 0; i < boxColliders.Length; i++)
        {
            if (boxColliders[i] == c) return true;
        }
        return false;
    }

    private void ApplyProgress(float t)
    {
        if (reticleFill == null) return;
        reticleFill.fillAmount = Mathf.Clamp01(t);
    }
}
