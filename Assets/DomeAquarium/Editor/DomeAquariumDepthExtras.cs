#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 층마다 더하는 작은 볼거리.
///   24m 산호 백화 구역 — 돔 왼쪽 뒤 산호를 하얗게 바랜 재질로. 한동안 머물면 관측소장이 그쪽을 알려 준다(DiveDirector.bleachLine).
///   200m 밤의 대이동 — 돔 둘레(반지름 9~11m)로 작은 빛 알갱이 기둥이 바닥에서 천장 쪽으로 천천히 솟는다(DiveMission.migration).
/// 깊이에 따라 먹먹해지는 물소리 · 큰 고래가 지나갈 때의 진동은 DiveDirector 가 런타임에 한다.
/// 순서: 18(층 하강) 뒤. 9(로비) 안에서도 돈다. 따로 다시 세울 때는 32.
/// </summary>
public static class DomeAquariumDepthExtras
{
    const string MatDir = "Assets/DomeAquarium/Materials";
    // 백화 구역 — 돔 가운데에서 본 각도(앞 +Z 에서 시계 방향, °). 왼쪽 뒤 한 귀퉁이.
    const float BleachFrom = 200f, BleachTo = 265f;

    [MenuItem("DomeAquarium/32. 층 볼거리 (24m 산호 백화 · 200m 밤의 대이동)", priority = 32)]
    public static void BuildMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[DepthExtras] 플레이 모드에서는 세울 수 없다."); return; }
        Build();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build()
    {
        var director = Object.FindFirstObjectByType<DiveDirector>(FindObjectsInactive.Include);
        if (director == null) { Debug.LogWarning("[DepthExtras] DiveDirector 가 없다 (18번 메뉴)."); return; }
        BuildBleaching(director);
        BuildMigration(director);
        var m200 = AssetDatabase.LoadAssetAtPath<DiveMission>("Assets/DomeAquarium/Missions/Mission_200m.asset");
        if (m200 != null && !m200.migration) { m200.migration = true; EditorUtility.SetDirty(m200); AssetDatabase.SaveAssets(); }
        EditorUtility.SetDirty(director);
    }

    static void BuildBleaching(DiveDirector director)
    {
        var reef = GameObject.Find("ReefFloor");
        Transform gen = reef != null ? reef.transform.Find("Generated") : null;
        var glow = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Mat_CoralGlow.mat");
        if (gen == null || glow == null) { Debug.LogWarning("[DepthExtras] 산호초(ReefFloor/Generated · Mat_CoralGlow)가 없어 백화 구역을 건너뛴다."); return; }

        // 하얗게 바랜 산호 — 같은 셰이더 · 모양, 색만 빼고 빛을 거의 끈다
        string path = $"{MatDir}/Mat_CoralBleached.mat";
        var bleached = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (bleached == null) { bleached = new Material(glow); AssetDatabase.CreateAsset(bleached, path); }
        else bleached.CopyPropertiesFromMaterial(glow);
        foreach (var id in new[] { "_BaseColor", "_Color" })
            if (bleached.HasProperty(id)) bleached.SetColor(id, new Color(0.90f, 0.89f, 0.85f));
        foreach (var id in new[] { "_BaseMap", "_MainTex" })                // 산호 색 텍스처를 빼야 하얗게 바랜다 (곱하면 색이 남았다)
            if (bleached.HasProperty(id)) bleached.SetTexture(id, null);
        foreach (var id in new[] { "_EmissionColor", "_GlowColor", "_RimColor" })
            if (bleached.HasProperty(id)) bleached.SetColor(id, new Color(0.08f, 0.08f, 0.075f));
        EditorUtility.SetDirty(bleached);

        int count = 0;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < gen.childCount; i++)
        {
            Transform c = gen.GetChild(i);
            bool coral = c.name.Contains("_Coral_"), weed = c.name.Contains("_Seaweed_");
            if (!coral && !weed) continue;
            float ang = Mathf.Repeat(Mathf.Atan2(c.position.x, c.position.z) * Mathf.Rad2Deg, 360f);
            bool inside = ang >= BleachFrom && ang <= BleachTo;
            foreach (var r in c.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    if (coral && inside && mats[k] == glow) { mats[k] = bleached; changed = true; }
                    else if (coral && !inside && mats[k] == bleached) { mats[k] = glow; changed = true; }
                }
                if (changed) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
            }
            if (weed) c.gameObject.SetActive(!inside);                     // 백화 구역은 해초도 듬성하다
            if (coral && inside) { count++; sum += c.position; }
        }

        var old = director.transform.Find("BleachSpot");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var spot = new GameObject("BleachSpot").transform;
        spot.SetParent(director.transform, false);
        spot.position = count > 0 ? sum / count : new Vector3(-5f, -7f, -5f);
        director.bleachSpot = spot;
        director.bleachLine =
            "{0} 보세요. 산호가 하얗게 변했어요. 바닷물이 너무 따뜻해지면 산호 속에 사는 작은 조류가 떠나고, 산호는 색을 잃고 굶게 돼요. " +
            "이걸 '백화'라고 해요. 오래가면 산호가 죽어요.";
        Debug.Log($"[DepthExtras] 24m 산호 백화 — 산호 {count}개 ({BleachFrom}°~{BleachTo}°), 가운데 {spot.position}.");
    }

    static void BuildMigration(DiveDirector director)
    {
        var old = director.transform.Find("DielMigration");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("DielMigration");
        go.transform.SetParent(director.transform, false);
        go.transform.position = new Vector3(0f, -7.6f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;                                              // 켜자마자 기둥이 이미 솟아 있다
        main.startLifetime = new ParticleSystem.MinMaxCurve(22f, 30f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.95f, 0.9f, 0.8f), new Color(0.75f, 0.85f, 1f, 0.6f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 900;
        var emission = ps.emission;
        emission.rateOverTime = 48f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;                 // 돔 벽 가까이 고리 — 관람객 둘레로 기둥이 선다
        shape.radius = 10f;
        shape.radiusThickness = 0.04f;                                  // 0.4m 폭 — 흩어지지 않고 기둥으로 보인다
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
        shape.arcSpread = 0.0625f;                                        // 고리 위 열여섯 자리에서만 — 흩어지지 않고 기둥이 된다
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.y = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var mat = DomeAquariumLobbyBuilder.AdditiveMaterial("Mat_DielMigration", "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", DomeAquariumParticleSetup.EnsureSoftDotTexture());
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);                                              // 200m 에 내려가면 DiveDirector 가 켠다
        director.migration = go;
        Debug.Log("[DepthExtras] 200m 밤의 대이동 — 돔 둘레 반지름 10m 고리 열여섯 기둥.");
    }
}
#endif
