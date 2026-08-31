using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// 수조 바닥 지형(산호/바위/해초/조개) 배치기.
// 에디터에서 컨텍스트 메뉴로 실행하는 것이 기본이지만 런타임 호출도 안전하다.
//
// 좌표 규약: 수조는 반지름 tankRadius(12m), 바닥 y = floorY(-8). 플레이어는 (0,0,0) 고정.
// 생성물은 전부 자식 "Generated" 안에만 들어간다. ClearGenerated 는 그 자식만 지운다.
public class ReefScatter : MonoBehaviour
{
    // Generate 가 만드는 컨테이너 이름. ClearGenerated 가 이 이름만 지운다.
    private const string GeneratedRootName = "Generated";

    [Header("수조 규격 (BoidManager 와 반드시 일치)")]
    public float tankRadius = 12f;
    public float floorY = -8f;

    [Header("계단식 지형")]
    public int terraceCount = 3;
    public float centerClearRadius = 2.5f;   // 플레이어 발밑 — 아무것도 두지 않는다
    public float terraceRise = 0.35f;        // 링 하나당 올라오는 높이 (바깥쪽일수록 높다)
    public float wallMargin = 0.6f;          // 유리벽에 프랍이 박히지 않도록 남기는 여유

    [Header("난수")]
    public int seed = 12345;

    [Header("프랍 (각 배열은 비어 있어도 된다)")]
    public GameObject[] coralPrefabs;
    public GameObject[] rockPrefabs;
    public GameObject[] seaweedPrefabs;
    public GameObject[] shellPrefabs;

    [Header("배치 밀도 / 변형")]
    // 밀도 근거: 유효 배치 면적 = π((tankRadius - wallMargin)^2 - centerClearRadius^2)
    //          = π(11.4^2 - 2.5^2) ≈ π × 123.7 ≈ 388.6 ㎡.
    //          388.6 × 0.31 ≈ 120개.
    // 드로우콜 예산이 30이라 프랍을 무한정 늘릴 수 없다. 120개 정도면
    // 에디터 생성 시 BatchingStatic 플래그가 붙어 정적 배칭으로 묶이므로
    // 머티리얼 종류(산호/바위/해초/조개 4~6종) 수준인 5~8 드로우콜에 들어온다.
    // 이 값을 0.5 이상으로 올리면 200개를 넘어 배칭 후에도 예산을 잡아먹는다.
    public float objectsPerSquareMeter = 0.31f;
    public float scaleMin = 0.8f;
    public float scaleMax = 1.4f;
    public float maxTiltDegrees = 8f;        // 살짝 기울여 인공적인 느낌을 없앤다

    // 카테고리 가중치 — 산호 / 바위 / 해초 / 조개 순. 비어 있는 카테고리는 자동으로 빠진다.
    private static readonly float[] CategoryWeights = { 0.34f, 0.30f, 0.26f, 0.10f };
    private static readonly string[] CategoryNames = { "Coral", "Rock", "Seaweed", "Shell" };

    // 카테고리 배열 캐시 (Generate 시작 시 한 번만 구성)
    private GameObject[][] categories;

    [ContextMenu("지형 생성")]
    public void Generate()
    {
        // 항상 깨끗한 상태에서 시작한다 (Generate 를 두 번 눌러도 겹치지 않도록).
        ClearGenerated();

        if (categories == null || categories.Length != 4) categories = new GameObject[4][];
        categories[0] = coralPrefabs;
        categories[1] = rockPrefabs;
        categories[2] = seaweedPrefabs;
        categories[3] = shellPrefabs;

        if (!AnyPrefabAvailable())
        {
            Debug.LogWarning("[ReefScatter] 프리팹 배열이 모두 비어 있어 아무것도 배치하지 않았다.", this);
            return;
        }

        int rings = Mathf.Max(1, terraceCount);
        float usableRadius = Mathf.Max(0.01f, tankRadius - Mathf.Max(0f, wallMargin));
        float ringWidth = usableRadius / rings;

        if (centerClearRadius >= usableRadius)
        {
            Debug.LogWarning("[ReefScatter] centerClearRadius 가 배치 가능 반지름보다 크다. 배치 없음.", this);
            return;
        }

#if UNITY_EDITOR
        int undoGroup = -1;
        bool editorAuthoring = !Application.isPlaying;
        if (editorAuthoring)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("리프 지형 생성");
            undoGroup = Undo.GetCurrentGroup();
        }
#endif

        // 다른 코드의 난수를 오염시키지 않도록 상태를 저장하고 끝나면 되돌린다.
        Random.State previousState = Random.state;
        Random.InitState(seed);

        int total = 0;
        string perRing = "";

        try
        {
            Transform root = CreateGeneratedRoot();

            // 수조 중심의 수평 위치. y 는 floorY(월드 절대값)를 그대로 쓴다.
            Vector3 center = transform.position;

            for (int ring = 0; ring < rings; ring++)
            {
                // 링 안쪽/바깥쪽 반지름. 가장 안쪽 링은 centerClearRadius 부터 시작한다.
                float inner = Mathf.Max(ring * ringWidth, centerClearRadius);
                float outer = (ring + 1) * ringWidth;
                if (outer <= inner) { perRing += (ring == 0 ? "" : "/") + "0"; continue; }

                // 바깥 링일수록 바닥에서 계단처럼 올라온다. 링 높이를 명시적으로 계산.
                float terraceY = floorY + ring * terraceRise;

                // 면적 비례 개수 — 안쪽 링에 몰리지 않게 한다.
                float ringArea = Mathf.PI * (outer * outer - inner * inner);
                int count = Mathf.RoundToInt(ringArea * Mathf.Max(0f, objectsPerSquareMeter));

                for (int i = 0; i < count; i++)
                {
                    GameObject[] category = PickCategory(out int categoryIndex);
                    if (category == null) break;

                    GameObject prefab = PickPrefab(category);
                    if (prefab == null) continue;

                    // 링 안에서 면적 균등 샘플링 (그냥 Range(inner, outer) 를 쓰면 안쪽에 몰린다).
                    float r = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, Random.value));
                    float theta = Random.value * Mathf.PI * 2f;

                    Vector3 pos = new Vector3(
                        center.x + Mathf.Cos(theta) * r,
                        terraceY + Random.Range(-0.04f, 0.04f),   // 완전한 평면을 피하는 미세 지터
                        center.z + Mathf.Sin(theta) * r);

                    float yaw = Random.value * 360f;
                    Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

                    // 최대 maxTiltDegrees 만큼 임의 방향으로 기울인다.
                    float tilt = Random.value * Mathf.Max(0f, maxTiltDegrees);
                    if (tilt > 0.01f)
                    {
                        float tiltDir = Random.value * Mathf.PI * 2f;
                        Vector3 axis = new Vector3(Mathf.Cos(tiltDir), 0f, Mathf.Sin(tiltDir));
                        rot = Quaternion.AngleAxis(tilt, axis) * rot;
                    }

                    GameObject go = SpawnPrefab(prefab, root);
                    if (go == null) continue;

                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one * Random.Range(scaleMin, scaleMax);
                    go.name = "R" + ring + "_" + CategoryNames[categoryIndex] + "_" + i.ToString("00");

                    total++;
                }

                perRing += (ring == 0 ? "" : "/") + count;
            }
        }
        finally
        {
            // 어떤 경로로 빠져나가도 난수 상태는 복원한다.
            Random.state = previousState;
        }

#if UNITY_EDITOR
        if (editorAuthoring)
        {
            if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        Debug.Log("[ReefScatter] 지형 생성 완료 — 총 " + total + "개 (링별 " + perRing + "), seed=" + seed, this);
    }

    [ContextMenu("지형 삭제")]
    public void ClearGenerated()
    {
        // 이름이 같은 자식이 여러 개 남아 있어도 전부 지운다. 다른 자식은 건드리지 않는다.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || child.name != GeneratedRootName) continue;

            GameObject go = child.gameObject;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(go);
                continue;
            }
#endif
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    // 생성물 컨테이너. 부모의 스케일이 1이 아니면 프랍도 같이 늘어나니 수조 루트는 스케일 1로 둘 것.
    private Transform CreateGeneratedRoot()
    {
        GameObject root = new GameObject(GeneratedRootName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(root, "리프 지형 루트 생성");
#endif
        return root.transform;
    }

    // 에디터에서는 프리팹 연결을 유지해야 하므로 PrefabUtility 를 쓴다. 런타임은 일반 Instantiate.
    private GameObject SpawnPrefab(GameObject prefab, Transform parent)
    {
        GameObject go = null;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 프리팹 에셋이 아니면 null 이 돌아온다 -> 아래 Instantiate 로 폴백.
            go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (go != null)
            {
                Undo.RegisterCreatedObjectUndo(go, "리프 프랍 생성");
                // 정적 배칭으로 묶어 드로우콜을 아낀다 (예산 30).
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }
#endif
        if (go == null) go = Instantiate(prefab, parent);
        return go;
    }

    private bool AnyPrefabAvailable()
    {
        for (int i = 0; i < categories.Length; i++)
        {
            if (HasPrefab(categories[i])) return true;
        }
        return false;
    }

    private static bool HasPrefab(GameObject[] array)
    {
        if (array == null) return false;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] != null) return true;
        }
        return false;
    }

    // 비어 있는 카테고리는 가중치에서 빠진다 (배열이 하나만 채워져 있어도 정상 동작).
    private GameObject[] PickCategory(out int categoryIndex)
    {
        categoryIndex = -1;

        float totalWeight = 0f;
        for (int i = 0; i < categories.Length; i++)
        {
            if (HasPrefab(categories[i])) totalWeight += CategoryWeights[i];
        }
        if (totalWeight <= 0f) return null;

        float pick = Random.value * totalWeight;
        for (int i = 0; i < categories.Length; i++)
        {
            if (!HasPrefab(categories[i])) continue;
            pick -= CategoryWeights[i];
            if (pick <= 0f)
            {
                categoryIndex = i;
                return categories[i];
            }
        }

        // 부동소수 오차로 여기까지 오면 마지막 유효 카테고리를 쓴다.
        for (int i = categories.Length - 1; i >= 0; i--)
        {
            if (HasPrefab(categories[i]))
            {
                categoryIndex = i;
                return categories[i];
            }
        }
        return null;
    }

    // 배열 안에 null 이 섞여 있어도 안전하게 하나 고른다.
    private static GameObject PickPrefab(GameObject[] array)
    {
        int valid = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] != null) valid++;
        }
        if (valid == 0) return null;

        int target = Random.Range(0, valid);
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == null) continue;
            if (target == 0) return array[i];
            target--;
        }
        return null;
    }
}
