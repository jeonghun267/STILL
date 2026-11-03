using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// 조수 웅덩이에 손 넣기 — 손(컨트롤러)이 물 아래로 들어가면 물결이 번지고 "찰방" 소리, 손에 가벼운 떨림.
/// 가까운 게는 가장자리 바위 밑으로 숨고 새우는 달아난다(PracticeTank.Scare). 손을 빼고 가만히 있으면 다시 나온다.
/// 처음 한 번, 놀란이 "손은 살짝, 잠깐만" 을 일러 준다 — 웅덩이 생물을 대하는 예절.
/// 물 높이 · 타원은 웅덩이(pool) 로컬 기준.
/// </summary>
public sealed class TidePoolTouch : MonoBehaviour
{
    public Transform pool;                      // 웅덩이 가운데 (로컬 y 0 = 모래 높이)
    public Vector2 radius = new Vector2(0.59f, 0.9f);   // 물 타원 반지름 (로컬 x · z)
    public float waterY = 0.2f;
    public float bedY = 0.02f;
    public Transform[] hands;
    public HapticImpulsePlayer[] haptics;       // hands 와 같은 순서 (없으면 건너뛴다)
    public PracticeTank life;
    public float scareRadius = 0.4f;

    [Header("물결 · 소리")]
    public ParticleSystem ripples;
    public AudioSource sound;
    public AudioClip dip;
    public float rippleEvery = 0.22f;           // 손을 움직이는 동안
    public float rippleMove = 0.03f;

    [Header("놀란")]
    public NpcDialogue nolant;
    [TextArea(2, 4)] public string firstLine =
        "앗, 게가 바위 밑으로 숨었지? 손이 커다란 그림자처럼 보여서 그래. 웅덩이 친구들은 손을 빼고 가만히 기다리면 다시 나와. 만질 땐 살짝, 잠깐만!";
    public string reply = "살짝, 잠깐만!";

    bool[] inside;
    Vector3[] lastRipple;
    float[] rippleAt;
    bool told;

    void Awake()
    {
        int n = hands != null ? hands.Length : 0;
        inside = new bool[n];
        lastRipple = new Vector3[n];
        rippleAt = new float[n];
    }

    void Update()
    {
        if (pool == null || hands == null) return;
        for (int i = 0; i < hands.Length; i++)
        {
            Transform h = hands[i];
            if (h == null || !h.gameObject.activeInHierarchy) { inside[i] = false; continue; }
            bool now = InWater(h.position);
            if (now && !inside[i]) Enter(i, h.position);
            else if (now)
            {
                if (life != null) life.Scare(h.position, scareRadius);              // 손이 물에 있는 동안 숨은 채로
                if (Time.time >= rippleAt[i] && (h.position - lastRipple[i]).sqrMagnitude > rippleMove * rippleMove)
                    Ripple(i, h.position, 0.6f);
            }
            inside[i] = now;
        }
    }

    bool InWater(Vector3 world)
    {
        Vector3 p = pool.InverseTransformPoint(world);
        if (p.y > waterY + 0.01f || p.y < bedY - 0.15f) return false;
        float ex = p.x / Mathf.Max(0.01f, radius.x), ez = p.z / Mathf.Max(0.01f, radius.y);
        return ex * ex + ez * ez < 1f;
    }

    void Enter(int i, Vector3 at)
    {
        Ripple(i, at, 1f);
        if (sound != null && dip != null)
        {
            sound.transform.position = at;
            sound.pitch = Random.Range(0.92f, 1.08f);
            sound.PlayOneShot(dip);
        }
        if (haptics != null && i < haptics.Length && haptics[i] != null) haptics[i].SendHapticImpulse(0.25f, 0.08f);
        int fled = life != null ? life.Scare(at, scareRadius) : 0;
        if (!told && fled > 0 && nolant != null && !nolant.Talking && !nolant.Has("poolTouch"))
        {
            told = true;
            Invoke(nameof(Tell), 1.2f);                                          // 게가 숨는 걸 본 뒤에
        }
    }

    void Tell()
    {
        if (nolant != null && !nolant.Talking) nolant.Remark(firstLine, reply, "poolTouch");
        else told = false;                                                       // 대화 중이었다 — 다음에 넣을 때
    }

    void Ripple(int i, Vector3 at, float strength)
    {
        rippleAt[i] = Time.time + rippleEvery;
        lastRipple[i] = at;
        if (ripples == null) return;
        Vector3 p = pool.InverseTransformPoint(at);
        p.y = waterY + 0.004f;
        var e = new ParticleSystem.EmitParams
        {
            position = pool.TransformPoint(p),
            applyShapeToPosition = false,
            startSize = Mathf.Lerp(0.05f, 0.08f, strength),
            startLifetime = Mathf.Lerp(0.9f, 1.4f, strength),
            startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 0.7f, strength)),
        };
        ripples.Emit(e, 1);
        if (strength >= 1f)                                                      // 처음 들어갈 때는 둘째 물결이 조금 늦게
        {
            e.startLifetime *= 1.25f;
            e.startSize *= 0.6f;
            e.startColor = new Color(1f, 1f, 1f, 0.45f);
            ripples.Emit(e, 1);
        }
    }

#if UNITY_EDITOR
    /// <summary>헤드셋 없이 확인할 때 — 웅덩이 로컬 (x, z) 에 손을 넣었다 뺀다.</summary>
    public void DebugDip(Vector2 local)
    {
        if (pool == null) return;
        Vector3 at = pool.TransformPoint(new Vector3(local.x, waterY - 0.05f, local.y));
        Enter(0, at);
    }
#endif
}
