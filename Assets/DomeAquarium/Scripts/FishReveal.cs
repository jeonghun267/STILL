using System.Collections;
using UnityEngine;

// 미스터리 박스에서 뽑힌 물고기를 플레이어 앞으로 데려와 보여주고 다시 떠나보내는 연출.
// 4단계: (1) 접근 -> (2) 정지/응시 -> (3) 정보 표시 -> (4) 퇴장
public class FishReveal : MonoBehaviour
{
    [Header("참조")]
    public Transform stagePoint;          // 카메라 앞 (0,0,1.2)
    public FishInfoPanel infoPanel;
    public Transform player;

    [Header("타이밍")]
    public float spawnDistance = 14f;     // 플레이어로부터의 수평 등장 거리
    public float approachTime = 2.2f;
    public float holdTime = 8f;
    public float exitTime = 2.0f;

    [Header("관람 배치")]
    public float showcaseSideOffset = 0.20f;
    public float turntableDegreesPerSecond = 45f;
    [Tooltip("참다랑어 같은 대형 어종이 설명 UI를 침범하지 않도록 전시 모델의 최대 외곽 크기를 제한합니다.")]
    public float maxShowcaseSize = 0.34f;

    [Header("밤 가시성")]
    [Tooltip("밤에 뽑힌 물고기가 검은 실루엣으로만 보이지 않도록 자기 텍스처 색으로 발광(알베도 배수, 한밤 기준). " +
             "물고기 팔레트가 밝아서 0.5 면 충분히 보이면서 태양 음영도 남는다. 더 올리면 평면 스티커처럼 보인다")]
    [Min(0f)] public float nightGlow = 0.5f;
    [Tooltip("시간대와 무관하게 항상 더하는 발광. 아주 작게 — 낮 모습이 바뀌면 안 된다")]
    [Min(0f)] public float baseGlow = 0.03f;

    // --- 내부 튜닝 상수 (인스펙터 노출 안 함: 계약 시그니처 유지) ---
    const float SettleTime = 0.6f;        // 도착 후 플레이어를 향해 도는 시간
    const float BobAmplitude = 0.04f;     // 제자리 보빙 진폭 (m)
    const float BobFrequency = 1.15f;     // 보빙 속도
    const float ApproachTurnLambda = 8f;  // 진행 방향 추종 강도
    const float FaceTurnLambda = 7f;      // 전시 회전 추종 강도
    const float MinSqrSpeed = 1e-6f;
    const float MinDelta = 0.0001f;

    bool busy;
    Coroutine routine;
    GameObject instance;

    public bool IsBusy { get { return busy; } }

    public void Show(FishData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[FishReveal] FishData 가 null 이라 연출을 건너뜁니다.", this);
            return;
        }
        if (data.prefab == null)
        {
            Debug.LogWarning("[FishReveal] '" + data.DisplayName + "' 의 prefab 이 비어 있어 연출을 건너뜁니다.", this);
            return;
        }
        if (stagePoint == null)
        {
            Debug.LogWarning("[FishReveal] stagePoint 가 비어 있습니다.", this);
            return;
        }

        Transform viewer = ResolveViewer();
        if (viewer == null)
        {
            Debug.LogWarning("[FishReveal] player 도 Main Camera 도 찾지 못했습니다.", this);
            return;
        }
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("[FishReveal] 컴포넌트가 비활성이라 코루틴을 시작할 수 없습니다.", this);
            return;
        }
        if (busy)
        {
            // 진행 중이면 새 연출을 겹치지 않는다.
            Debug.LogWarning("[FishReveal] 이미 연출 중이라 요청을 무시합니다.", this);
            return;
        }

        // 도감 미리보기 복제본이 참조 유실로 남아 있어도 새 뽑기 연출과 섞이지 않게 정리한다.
        FishEncyclopediaPanel.RemoveAllPreviewObjects();

        // 정상적으로 공개를 시작할 수 있을 때만 현재 탐험가의 도감에 기록한다.
        // speciesId 기반 저장이라 같은 생물을 다시 뽑으면 발견 횟수만 증가한다.
        FishCollectionStore.Instance?.RegisterDiscovery(data);

        // 접근 연출 동안 설명을 미리 받아 패널이 열린 뒤 기다리는 시간을 줄인다.
        if (infoPanel != null) infoPanel.PreloadDescription(data);

        busy = true;
        routine = StartCoroutine(RevealRoutine(data, viewer));
    }

    Transform ResolveViewer()
    {
        if (player != null) return player;
        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    IEnumerator RevealRoutine(FishData data, Transform viewer)
    {
        Vector3 stagePos = stagePoint.position;

        // 플레이어 정면을 피한 임의 수평 방향 (뒤 / 옆)
        Vector3 forward = viewer.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < MinSqrSpeed) forward = Vector3.forward;
        else forward.Normalize();

        float yaw = Random.Range(70f, 290f);
        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * forward;

        Vector3 viewerFlat = new Vector3(viewer.position.x, stagePos.y, viewer.position.z);
        Vector3 spawnPos = viewerFlat + dir * spawnDistance;
        spawnPos.y = stagePos.y + Random.Range(-0.6f, 1.2f);

        Vector3 toStage = stagePos - spawnPos;
        Quaternion spawnRot = toStage.sqrMagnitude > MinSqrSpeed
            ? Quaternion.LookRotation(toStage.normalized, Vector3.up)
            : Quaternion.identity;

        instance = Instantiate(data.prefab, spawnPos, spawnRot);
        Transform fish = instance.transform;
        fish.localScale = Vector3.one * Mathf.Max(0.01f, data.revealScale);
        LimitShowcaseSize(fish);

        // 연출 중 컨트롤러/손과 충돌하면 안 된다.
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;

        // 물리에 끌려가지 않도록
        Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].useGravity = false;
            bodies[i].isKinematic = true;
        }

        FishAnimatorSync sync = instance.GetComponentInChildren<FishAnimatorSync>(true);

        // 전시 모델은 우리가 만든 복제본이라 머티리얼 인스턴스를 만들어도 원본 프리팹/에셋은 안전하다.
        if (nightGlow > 0f || baseGlow > 0f)
            ShowcaseGlow.Attach(instance, baseGlow, nightGlow);

        Vector3 prevPos = spawnPos;

        // ---------- (1) 접근: ease-out 으로 감속하며 무대에 도착 ----------
        float duration = Mathf.Max(0.01f, approachTime);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            float k = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic

            Vector3 target = stagePoint != null ? ShowcasePosition(stagePoint.position, viewer) : stagePos;
            Vector3 next = Vector3.Lerp(spawnPos, target, eased);
            Vector3 velocity = (next - prevPos) / Mathf.Max(dt, MinDelta);

            fish.position = next;
            if (velocity.sqrMagnitude > MinSqrSpeed)
            {
                Quaternion want = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                fish.rotation = Quaternion.Slerp(fish.rotation, want, 1f - Mathf.Exp(-ApproachTurnLambda * dt));
            }
            if (sync != null) sync.SetVelocity(velocity);

            prevPos = next;
            yield return null;
        }

        // ---------- (2) 정지: 플레이어를 향해 부드럽게 회전 + 미세 보빙 ----------
        float phase = 0f;
        float settle = 0f;
        while (settle < SettleTime)
        {
            float dt = Time.deltaTime;
            settle += dt;
            phase += dt;
            Idle(fish, viewer, sync, phase, dt, ref prevPos);
            yield return null;
        }

        // ---------- (3) 표시 ----------
        if (infoPanel != null) infoPanel.Show(data, fish);

        float hold = 0f;
        float holdDuration = Mathf.Max(0f, holdTime);
        while (hold < holdDuration)
        {
            float dt = Time.deltaTime;
            hold += dt;
            phase += dt;
            Idle(fish, viewer, sync, phase, dt, ref prevPos);
            yield return null;
        }

        // ---------- (4) 퇴장: 처음 온 방향의 반대쪽으로 멀어진다 ----------
        if (infoPanel != null) infoPanel.Hide();

        Vector3 exitStart = fish.position;
        Vector3 travel = exitStart - spawnPos;
        travel.y = 0f;
        if (travel.sqrMagnitude < MinSqrSpeed) travel = forward;
        travel.Normalize();
        Vector3 exitTarget = exitStart + travel * spawnDistance + Vector3.up * 0.5f;

        float exitDuration = Mathf.Max(0.01f, exitTime);
        float leaving = 0f;
        while (leaving < exitDuration)
        {
            float dt = Time.deltaTime;
            leaving += dt;

            float k = Mathf.Clamp01(leaving / exitDuration);
            float eased = k * k;                         // ease-in: 서서히 가속하며 멀어진다
            Vector3 next = Vector3.Lerp(exitStart, exitTarget, eased);
            Vector3 velocity = (next - prevPos) / Mathf.Max(dt, MinDelta);

            fish.position = next;
            if (velocity.sqrMagnitude > MinSqrSpeed)
            {
                Quaternion want = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                fish.rotation = Quaternion.Slerp(fish.rotation, want, 1f - Mathf.Exp(-ApproachTurnLambda * dt));
            }
            if (sync != null) sync.SetVelocity(velocity);

            prevPos = next;
            yield return null;
        }

        if (instance != null)
        {
            Destroy(instance);
            instance = null;
        }

        routine = null;
        busy = false;
    }

    void LimitShowcaseSize(Transform fish)
    {
        if (fish == null || maxShowcaseSize <= 0f) return;

        Renderer[] renderers = fish.GetComponentsInChildren<Renderer>(false);
        Bounds bounds = default;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer
                || renderer is TrailRenderer || renderer is LineRenderer)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (!found) return;
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest > maxShowcaseSize && largest > 0.0001f)
            fish.localScale *= maxShowcaseSize / largest;
    }

    // 무대 위 제자리 유영: 미세한 보빙 + 360도 전시 회전
    void Idle(Transform fish, Transform viewer, FishAnimatorSync sync, float phase, float dt, ref Vector3 prevPos)
    {
        Vector3 stagePos = stagePoint != null ? ShowcasePosition(stagePoint.position, viewer) : fish.position;

        Vector3 bob = new Vector3(
            Mathf.Sin(phase * BobFrequency * 0.55f) * BobAmplitude * 0.5f,
            Mathf.Sin(phase * BobFrequency) * BobAmplitude,
            0f);

        Vector3 next = stagePos + bob;
        Vector3 velocity = (next - prevPos) / Mathf.Max(dt, MinDelta);
        fish.position = next;

        Vector3 profile = -viewer.right;
        profile.y = 0f;
        if (profile.sqrMagnitude > MinSqrSpeed)
        {
            profile.Normalize();
            // 8초 동안 한 바퀴 돌아 어느 축으로 제작된 모델도 몸 전체를 확인할 수 있다.
            profile = Quaternion.AngleAxis(phase * turntableDegreesPerSecond, Vector3.up) * profile;
            Quaternion want = Quaternion.LookRotation(profile, Vector3.up);
            fish.rotation = Quaternion.Slerp(fish.rotation, want, 1f - Mathf.Exp(-FaceTurnLambda * dt));
        }

        if (sync != null) sync.SetVelocity(velocity);
        prevPos = next;
    }

    Vector3 ShowcasePosition(Vector3 center, Transform viewer)
    {
        Vector3 right = viewer.right;
        right.y = 0f;
        if (right.sqrMagnitude < MinSqrSpeed) right = Vector3.right;
        else right.Normalize();
        return center + right * showcaseSideOffset;
    }

    /// <summary>도감 진입이나 씬 비활성화 때 진행 중인 전시 모델과 UI를 즉시 정리한다.</summary>
    public void CancelCurrentReveal()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        if (infoPanel != null) infoPanel.Hide();
        if (instance != null)
        {
            // Destroy는 프레임 끝에 실행되므로 먼저 숨겨 한 프레임 잔상을 막는다.
            instance.SetActive(false);
            Destroy(instance);
            instance = null;
        }
        busy = false;
    }

    void OnDisable()
    {
        // 씬 전환/비활성 시 코루틴과 인스턴스를 남기지 않는다.
        CancelCurrentReveal();
    }
}
