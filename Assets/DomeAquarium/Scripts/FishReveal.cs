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
    public float holdTime = 6f;
    public float exitTime = 2.0f;

    // --- 내부 튜닝 상수 (인스펙터 노출 안 함: 계약 시그니처 유지) ---
    const float SettleTime = 0.6f;        // 도착 후 플레이어를 향해 도는 시간
    const float BobAmplitude = 0.04f;     // 제자리 보빙 진폭 (m)
    const float BobFrequency = 1.15f;     // 보빙 속도
    const float SwayAngle = 3.5f;         // 제자리 요동 각도
    const float SwayFrequency = 0.75f;
    const float ApproachTurnLambda = 8f;  // 진행 방향 추종 강도
    const float FaceTurnLambda = 3.5f;    // 플레이어 응시 회전 강도
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

            Vector3 target = stagePoint != null ? stagePoint.position : stagePos;
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

    // 무대 위 제자리 유영: 아주 미세한 보빙 + 요동, 플레이어 응시
    void Idle(Transform fish, Transform viewer, FishAnimatorSync sync, float phase, float dt, ref Vector3 prevPos)
    {
        Vector3 stagePos = stagePoint != null ? stagePoint.position : fish.position;

        Vector3 bob = new Vector3(
            Mathf.Sin(phase * BobFrequency * 0.55f) * BobAmplitude * 0.5f,
            Mathf.Sin(phase * BobFrequency) * BobAmplitude,
            0f);

        Vector3 next = stagePos + bob;
        Vector3 velocity = (next - prevPos) / Mathf.Max(dt, MinDelta);
        fish.position = next;

        Vector3 look = viewer.position - next;
        if (look.sqrMagnitude > MinSqrSpeed)
        {
            Quaternion want = Quaternion.LookRotation(look.normalized, Vector3.up)
                              * Quaternion.Euler(0f, Mathf.Sin(phase * SwayFrequency) * SwayAngle, 0f);
            fish.rotation = Quaternion.Slerp(fish.rotation, want, 1f - Mathf.Exp(-FaceTurnLambda * dt));
        }

        if (sync != null) sync.SetVelocity(velocity);
        prevPos = next;
    }

    void OnDisable()
    {
        // 씬 전환/비활성 시 코루틴과 인스턴스를 남기지 않는다.
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        if (infoPanel != null) infoPanel.Hide();
        if (instance != null)
        {
            Destroy(instance);
            instance = null;
        }
        busy = false;
    }
}
