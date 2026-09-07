#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 이미 만들어진 수조 씬의 바닥 깊이와 산호 군락 조명을 보정한다.
/// 전체 씬을 다시 빌드하지 않기 때문에 사용자가 조정한 UI/물고기 배치는 보존된다.
/// </summary>
public static class DomeAquariumEnvironmentSetup
{
    const string ScenePath = "Assets/Scenes/STILL.unity";
    const string AccentRootName = "CoralAccentLights";
    const string SeabedMeshPath = "Assets/DomeAquarium/Materials/SeabedDisc.asset";
    const string TankWallMeshPath = "Assets/DomeAquarium/Materials/WaterTankWall.asset";
    const int AccentLightCount = 5;
    const int CircleSegments = 96;
    const float SeabedRadius = 11.88f;

    [InitializeOnLoadMethod]
    static void ScheduleApply()
    {
        EditorApplication.delayCall += ApplyIfNeeded;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // 플레이 중에는 씬 에셋을 건드리지 않는다. 편집 모드로 돌아온 직후 기존 배치도 접지한다.
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += ApplyIfNeeded;
    }

    static void ApplyIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;

        GameObject seabed = GameObject.Find("Seabed");
        GameObject tank = GameObject.Find("WaterTank");
        var tod = Object.FindFirstObjectByType<TimeOfDayController>();
        Transform reef = GameObject.Find("ReefFloor")?.transform;
        ReefScatter scatter = reef != null ? reef.GetComponent<ReefScatter>() : null;
        Transform generated = reef != null ? reef.Find("Generated") : null;
        Transform accentRoot = reef != null ? reef.Find(AccentRootName) : null;

        bool floorReady = seabed != null &&
                          Mathf.Abs(seabed.transform.position.y - DomeAquariumSeabed.SeabedSurfaceY) < 0.001f &&
                          seabed.transform.localScale == Vector3.one &&
                          seabed.GetComponent<MeshFilter>()?.sharedMesh?.name == "SeabedDisc";
        bool wallReady = tank != null &&
                         tank.GetComponent<MeshFilter>()?.sharedMesh?.name == "WaterTankWallWithSurface";
        bool lightsReady = tod != null && accentRoot != null &&
                           tod.coralAccentLights != null &&
                           tod.coralAccentLights.Length == accentRoot.childCount &&
                           accentRoot.childCount > 0;
        bool reefReady = scatter != null && generated != null &&
                         Mathf.Abs(scatter.floorY - DomeAquariumSeabed.SeabedSurfaceY) < 0.001f &&
                         Mathf.Abs(scatter.terraceRise) < 0.001f &&
                         AreGeneratedPropsGrounded(generated, DomeAquariumSeabed.SeabedSurfaceY,
                             scatter.groundEmbedDepth);
        if (floorReady && wallReady && lightsReady && reefReady) return;

        Apply(saveScene: true);
    }

    [MenuItem("DomeAquarium/15. 바닥 높이 및 산호 조명 보정", priority = 15)]
    public static void ApplyFromMenu()
    {
        Apply(saveScene: true);
    }

    public static void Apply(bool saveScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[DomeAquarium] 플레이 모드에서는 환경 보정을 적용하지 않는다.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        int changed = 0;
        GameObject seabed = GameObject.Find("Seabed");
        if (seabed != null)
        {
            Vector3 p = seabed.transform.position;
            if (!Mathf.Approximately(p.y, DomeAquariumSeabed.SeabedSurfaceY))
            {
                p.y = DomeAquariumSeabed.SeabedSurfaceY;
                seabed.transform.position = p;
                EditorUtility.SetDirty(seabed.transform);
                changed++;
            }

            if (seabed.transform.localScale != Vector3.one)
            {
                seabed.transform.localScale = Vector3.one;
                EditorUtility.SetDirty(seabed.transform);
                changed++;
            }

            var filter = seabed.GetComponent<MeshFilter>();
            if (filter == null) filter = seabed.AddComponent<MeshFilter>();
            Mesh disc = EnsureSeabedDiscMesh();
            if (filter.sharedMesh != disc)
            {
                filter.sharedMesh = disc;
                EditorUtility.SetDirty(filter);
                changed++;
            }
        }

        GameObject tank = GameObject.Find("WaterTank");
        if (tank != null)
        {
            var filter = tank.GetComponent<MeshFilter>();
            Mesh wall = EnsureOpenTankWallMesh();
            if (filter != null && filter.sharedMesh != wall)
            {
                filter.sharedMesh = wall;
                EditorUtility.SetDirty(filter);
                changed++;
            }
        }

        Transform reef = GameObject.Find("ReefFloor")?.transform;
        Transform generated = reef != null ? reef.Find("Generated") : null;
        ReefScatter scatter = reef != null ? reef.GetComponent<ReefScatter>() : null;
        if (scatter != null)
        {
            scatter.floorY = DomeAquariumSeabed.SeabedSurfaceY;
            scatter.terraceRise = 0f;
            scatter.groundEmbedDepth = 0.015f;
            EditorUtility.SetDirty(scatter);

            if (generated != null)
            {
                for (int i = 0; i < generated.childCount; i++)
                {
                    GameObject prop = generated.GetChild(i).gameObject;
                    if (!ReefScatter.AlignBottomToSurface(prop, DomeAquariumSeabed.SeabedSurfaceY,
                            scatter.groundEmbedDepth))
                        continue;
                    EditorUtility.SetDirty(prop.transform);
                    changed++;
                }
            }
        }

        var tod = Object.FindFirstObjectByType<TimeOfDayController>();
        if (reef != null && generated != null && tod != null)
        {
            Light[] lights = RebuildAccentLights(reef, generated);
            tod.coralAccentLights = lights;
            tod.coralLightDayFloor = 0.14f;
            tod.coralLightMaxIntensity = 1.6f;
            tod.coralLightRange = 2.8f;
            EditorUtility.SetDirty(tod);
            changed += lights.Length;
        }

        if (changed <= 0) return;
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        Debug.Log($"[DomeAquarium] 바닥 겹침 보정 및 산호 군락 조명 {Mathf.Max(0, changed - 1)}개 적용.");
    }

    static bool AreGeneratedPropsGrounded(Transform generated, float surfaceY, float embedDepth)
    {
        if (generated == null) return false;
        float expectedBottom = surfaceY - Mathf.Max(0f, embedDepth);
        for (int i = 0; i < generated.childCount; i++)
        {
            GameObject prop = generated.GetChild(i).gameObject;
            if (!ReefScatter.TryGetVisualBounds(prop, out Bounds bounds)) continue;
            if (Mathf.Abs(bounds.min.y - expectedBottom) > 0.01f) return false;
        }
        return true;
    }

    /// <summary>수조 밖으로 모서리가 나오지 않는 반지름 11.88m 원형 바닥.</summary>
    public static Mesh EnsureSeabedDiscMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SeabedMeshPath);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = "SeabedDisc" };
        else mesh.Clear();

        var vertices = new Vector3[CircleSegments + 1];
        var normals = new Vector3[vertices.Length];
        var tangents = new Vector4[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[CircleSegments * 3];

        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        tangents[0] = new Vector4(1f, 0f, 0f, 1f);
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int v = i + 1;
            vertices[v] = new Vector3(x * SeabedRadius, 0f, z * SeabedRadius);
            normals[v] = Vector3.up;
            tangents[v] = new Vector4(1f, 0f, 0f, 1f);
            uv[v] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);

            int next = ((i + 1) % CircleSegments) + 1;
            int t = i * 3;
            triangles[t] = 0;
            triangles[t + 1] = next;
            triangles[t + 2] = v;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, SeabedMeshPath);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// 아래는 열어 흙 바닥이 보이게 하고, 위는 수면으로 막은 수조 메시.
    /// 기존 24/8/24 스케일과 호환된다.
    /// </summary>
    public static Mesh EnsureOpenTankWallMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TankWallMeshPath);
        bool create = mesh == null;
        if (create) mesh = new Mesh();
        else mesh.Clear();
        mesh.name = "WaterTankWallWithSurface";

        int stride = CircleSegments + 1;
        int sideVertexCount = stride * 2;
        int surfaceCenter = sideVertexCount;
        int surfaceRim = surfaceCenter + 1;
        int sideTriangleIndexCount = CircleSegments * 6;
        var vertices = new Vector3[sideVertexCount + 1 + stride];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[sideTriangleIndexCount + CircleSegments * 3];

        vertices[surfaceCenter] = Vector3.up;
        normals[surfaceCenter] = Vector3.up;
        uv[surfaceCenter] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i <= CircleSegments; i++)
        {
            float u = i / (float)CircleSegments;
            float angle = u * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int bottom = i * 2;
            int top = bottom + 1;
            vertices[bottom] = radial * 0.5f + Vector3.down;
            vertices[top] = radial * 0.5f + Vector3.up;
            normals[bottom] = normals[top] = radial;
            uv[bottom] = new Vector2(u, 0f);
            uv[top] = new Vector2(u, 1f);

            // 옆벽과 수면은 모서리 노멀이 달라 별도 정점을 쓴다.
            int rim = surfaceRim + i;
            vertices[rim] = radial * 0.5f + Vector3.up;
            normals[rim] = Vector3.up;
            uv[rim] = new Vector2(radial.x + 0.5f, radial.z + 0.5f);

            if (i == CircleSegments) continue;
            int nextBottom = bottom + 2;
            int nextTop = top + 2;
            int t = i * 6;
            triangles[t] = bottom;
            triangles[t + 1] = top;
            triangles[t + 2] = nextBottom;
            triangles[t + 3] = top;
            triangles[t + 4] = nextTop;
            triangles[t + 5] = nextBottom;

            // 위를 향하는 앞면. 플레이어는 아래에서 뒷면을 보며 셰이더의 Cull Front와 맞는다.
            int cap = sideTriangleIndexCount + i * 3;
            triangles[cap] = surfaceCenter;
            triangles[cap + 1] = surfaceRim + i + 1;
            triangles[cap + 2] = surfaceRim + i;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, TankWallMeshPath);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Light[] RebuildAccentLights(Transform reef, Transform generated)
    {
        Transform old = reef.Find(AccentRootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(AccentRootName).transform;
        root.SetParent(reef, false);

        var candidates = generated.Cast<Transform>()
            .Where(t => t != null && t.name.IndexOf("_Coral_", System.StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(t => t.GetComponentInChildren<MeshRenderer>(true) != null)
            .ToList();

        List<Transform> anchors = PickDistributed(candidates, AccentLightCount);
        var result = new List<Light>(anchors.Count);
        for (int i = 0; i < anchors.Count; i++)
        {
            Transform anchor = anchors[i];
            var renderers = anchor.GetComponentsInChildren<MeshRenderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);

            var go = new GameObject($"CoralGlow_{i + 1:00}");
            go.transform.SetParent(root, false);
            Vector3 p = bounds.center;
            p.y = anchor.position.y + Mathf.Clamp(bounds.extents.y * 0.45f, 0.28f, 0.65f);
            go.transform.position = p;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.00f, 0.72f, 0.30f);
            light.intensity = 0.22f;
            light.range = 2.8f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            result.Add(light);
        }

        return result.ToArray();
    }

    // 첫 군락 이후에는 기존 선택점에서 가장 멀리 떨어진 산호를 고른다.
    // 조명이 한쪽에 몰리거나 여러 라이트 범위가 겹치는 것을 줄여 모바일 비용을 제한한다.
    static List<Transform> PickDistributed(List<Transform> candidates, int count)
    {
        var picked = new List<Transform>();
        if (candidates == null || candidates.Count == 0) return picked;

        Transform first = candidates.OrderBy(t => t.position.sqrMagnitude).First();
        picked.Add(first);

        while (picked.Count < count && picked.Count < candidates.Count)
        {
            Transform best = null;
            float bestDistance = -1f;
            foreach (Transform candidate in candidates)
            {
                if (picked.Contains(candidate)) continue;
                float nearest = float.MaxValue;
                foreach (Transform existing in picked)
                {
                    Vector2 a = new Vector2(candidate.position.x, candidate.position.z);
                    Vector2 b = new Vector2(existing.position.x, existing.position.z);
                    nearest = Mathf.Min(nearest, (a - b).sqrMagnitude);
                }
                if (nearest <= bestDistance) continue;
                bestDistance = nearest;
                best = candidate;
            }
            if (best == null) break;
            picked.Add(best);
        }

        return picked;
    }
}
#endif
