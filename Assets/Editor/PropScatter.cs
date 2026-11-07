using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 细节物件撒点：城区建筑 / 树 / 岩石 / 灌木 / 路障，按 v2 分区规则自动摆。
/// 菜单：Tools/铁皮黎明/③ 撒细节物件
///
/// 规则（设计坐标）：
///   建筑 → 城区带 z 0~14、|x|<46，避开路面
///   树   → 平原与山脚，坡度 &lt; 20°，避开城区和路面
///   岩石 → 山地 z &lt; −20，坡度 12~50°
///   灌木 → 平原，靠近树
///   路障 → 城区路边 + 山口公路边
/// </summary>
public static class PropScatter
{
    const string ScenePath = "Assets/Scenes/Map_Battlefield.unity";
    const string RootName = "Props";
    const string ModelRoot = "Assets/Models/Props";

    static float S => BattlefieldTerrain.Scale;
    static System.Random rng;

    // ---- 模型清单 ----
    static readonly string[] TreeModels =
    {
        "Nature/tree_detailed", "Nature/tree_detailed_dark",
        "Nature/tree_oak", "Nature/tree_plateau", "Nature/tree_blocks",
        "Nature/tree_pineTallA_detailed", "Nature/tree_pineTallB_detailed",
        "Nature/tree_pineTallC_detailed", "Nature/tree_pineTallD_detailed",
        "Nature/tree_pineRoundA", "Nature/tree_pineRoundC", "Nature/tree_pineRoundE",
        "Nature/tree_pineGroundA", "Nature/tree_fat", "Nature/tree_small",
    };
    static readonly string[] RockModels =
    {
        "Nature/rock_largeA", "Nature/rock_largeC", "Nature/rock_tallA",
        "Nature/rock_smallA", "Nature/rock_smallD", "Nature/stone_largeA",
        "Nature/stone_tallA", "Nature/stone_smallA",
    };
    static readonly string[] BushModels =
    {
        "Nature/plant_bush", "Nature/plant_bushLarge", "Nature/grass",
        "Nature/grass_large", "Nature/flower_redA", "Nature/flower_yellowA",
    };
    static readonly string[] BuildingModels =
    {
        "City/building-a", "City/building-b", "City/building-c", "City/building-d",
        "City/building-e", "City/building-f", "City/building-g",
        "City/low-detail-building-a", "City/low-detail-building-c",
        "City/low-detail-building-e", "City/low-detail-building-g",
        "City/building-skyscraper-a", "City/building-skyscraper-c",
    };
    static readonly string[] PropModels =
    {
        // 原来这里是 Kenney 生存包的木箱/油桶/栅栏，摆在战场上像一堆纸箱子 —— 先清掉
    };

    [MenuItem("Tools/铁皮黎明/③ 撒细节物件（建筑/树/石）", false, 40)]
    public static void Scatter()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BattlefieldTerrain.Invalidate();

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);

        rng = new System.Random(20261030);

        var trees = LoadAll(TreeModels);
        var rocks = LoadAll(RockModels);
        var bushes = LoadAll(BushModels);
        var builds = LoadAll(BuildingModels);
        var props = LoadAll(PropModels);

        int nb = ScatterBuildings(root.transform, builds);
        int nt = ScatterTrees(root.transform, trees);
        int nr = ScatterRocks(root.transform, rocks);
        int nbu = ScatterBushes(root.transform, bushes);
        int np = ScatterProps(root.transform, props);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PropScatter] 建筑 {nb} / 树 {nt} / 岩石 {nr} / 灌木 {nbu} / 路障 {np}，共 {nb + nt + nr + nbu + np} 个");
    }

    // ================= 各类撒点 =================

    static int ScatterBuildings(Transform parent, GameObject[] models)
    {
        if (models.Length == 0) return 0;
        var holder = new GameObject("Buildings").transform;
        holder.SetParent(parent, false);
        int n = 0;

        // 城区：设计 z 0~14、|x|<46；按 1.7 m 设计网格撒（世界 28 m），避开路面
        for (float dz = 1.0f; dz < 13.7f; dz += 1.7f)
        {
            for (float dx = -45f; dx < 45f; dx += 1.7f)
            {
                float jx = dx + (float)(rng.NextDouble() - 0.5) * 0.9f;
                float jz = dz + (float)(rng.NextDouble() - 0.5) * 0.9f;
                if (Mathf.Abs(jx) > 45f || jz < 0.8f || jz > 13.7f) continue;
                if (BattlefieldTerrainBuilder.CityRoadPaintMask(jx, jz) > 0.25f) continue;
                // 2026-11-07：离水线太近的楼不要 —— 底座会露在沙滩/水面外，观感很怪
                if (BattlefieldTerrain.WaterDepth(jx * S, jz * S) > -3.0f) continue;
                if (rng.NextDouble() > 0.55) continue;

                var model = models[rng.Next(models.Length)];
                float scale = BuildingScale(model);
                float yaw = rng.Next(2) == 0 ? 0f : 90f;
                Place(holder, model, jx * S, jz * S, scale, yaw, solid: true);
                n++;
            }
        }
        return n;
    }

    static int ScatterTrees(Transform parent, GameObject[] models)
    {
        if (models.Length == 0) return 0;
        var holder = new GameObject("Trees").transform;
        holder.SetParent(parent, false);
        int n = 0;

        // 平原 + 山脚；用噪声做树丛密度
        for (int i = 0; i < 9000; i++)
        {
            float dx = -59f + (float)rng.NextDouble() * 118f;
            float dz = -58f + (float)rng.NextDouble() * 74f;      // z −58 ~ 16
            if (dz > 14f) continue;
            if (IsCity(dx, dz)) continue;

            float x = dx * S, z = dz * S;
            float slope = BattlefieldTerrain.SampleSteepness(x, z);
            if (slope > 20f) continue;
            if (BattlefieldTerrain.SampleHeight(x, z) < BattlefieldTerrain.WaterLevel + 1.5f) continue;
            if (BattlefieldTerrainBuilder.PassRoadPaintMask(dx, dz) > 0.2f) continue;

            // 树丛密度：噪声 > 阈值才种
            float clump = Mathf.PerlinNoise(dx * 0.06f + 11f, dz * 0.06f + 3f);
            if (clump < 0.48f) continue;
            if (rng.NextDouble() > 0.32) continue;

            var model = models[rng.Next(models.Length)];
            float scale = 9f + (float)rng.NextDouble() * 7f;      // 树高 13~27 m（原来 8~17 m 太矮）
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 650) break;
        }
        return n;
    }

    static int ScatterRocks(Transform parent, GameObject[] models)
    {
        if (models.Length == 0) return 0;
        var holder = new GameObject("Rocks").transform;
        holder.SetParent(parent, false);
        int n = 0;

        for (int i = 0; i < 12000; i++)
        {
            float dx = -59f + (float)rng.NextDouble() * 118f;
            float dz = -59f + (float)rng.NextDouble() * 39f;      // z −59 ~ −20（山地）
            if (dz > -19f) continue;

            float x = dx * S, z = dz * S;
            float slope = BattlefieldTerrain.SampleSteepness(x, z);
            if (slope < 12f || slope > 52f) continue;
            if (BattlefieldTerrainBuilder.PassRoadPaintMask(dx, dz) > 0.2f) continue;

            var model = models[rng.Next(models.Length)];
            float scale = 4f + (float)rng.NextDouble() * 7f;
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 600) break;
        }
        return n;
    }

    static int ScatterBushes(Transform parent, GameObject[] models)
    {
        if (models.Length == 0) return 0;
        var holder = new GameObject("Bushes").transform;
        holder.SetParent(parent, false);
        int n = 0;

        for (int i = 0; i < 6000; i++)
        {
            float dx = -58f + (float)rng.NextDouble() * 116f;
            float dz = -30f + (float)rng.NextDouble() * 44f;
            if (IsCity(dx, dz)) continue;
            float x = dx * S, z = dz * S;
            if (BattlefieldTerrain.SampleSteepness(x, z) > 24f) continue;
            if (BattlefieldTerrain.SampleHeight(x, z) < BattlefieldTerrain.WaterLevel + 1f) continue;
            if (rng.NextDouble() > 0.3) continue;

            var model = models[rng.Next(models.Length)];
            float scale = 3f + (float)rng.NextDouble() * 4f;
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 700) break;
        }
        return n;
    }

    static int ScatterProps(Transform parent, GameObject[] models)
    {
        if (models.Length == 0) return 0;
        var holder = new GameObject("Barricades").transform;
        holder.SetParent(parent, false);
        int n = 0;

        for (int i = 0; i < 4000; i++)
        {
            float dx = -45f + (float)rng.NextDouble() * 90f;
            float dz = -20f + (float)rng.NextDouble() * 33f;
            bool inCity = IsCity(dx, dz);
            bool nearPass = BattlefieldTerrainBuilder.PassRoadPaintMask(dx, dz) > 0.35f;
            if (!inCity && !nearPass) continue;
            if (BattlefieldTerrain.SampleSteepness(dx * S, dz * S) > 18f) continue;
            if (rng.NextDouble() > 0.12) continue;

            var model = models[rng.Next(models.Length)];
            float scale = 2.4f + (float)rng.NextDouble() * 1.6f;
            Place(holder, model, dx * S, dz * S, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 120) break;
        }
        return n;
    }

    // ================= 工具 =================

    static bool IsCity(float dx, float dz) => dz >= 0f && dz <= 14f && Mathf.Abs(dx) < 46f;

    static readonly Dictionary<GameObject, float> _modelHeight = new Dictionary<GameObject, float>();

    static float ModelHeight(GameObject model)
    {
        if (_modelHeight.TryGetValue(model, out var h) && h > 0.01f) return h;
        var r = model.GetComponentInChildren<MeshRenderer>();
        h = r != null ? r.bounds.size.y : 1f;
        if (h < 0.01f) h = 1f;
        _modelHeight[model] = h;
        return h;
    }

    /// <summary>按"想要多少米高"反算缩放。高度对齐真实城市：小楼 8~16 m、普通楼 20~40 m、地标 60~90 m。</summary>
    static float BuildingScale(GameObject model)
    {
        string n = model.name.ToLowerInvariant();
        float target = n.Contains("skyscraper") ? 60f + (float)rng.NextDouble() * 30f
                     : n.Contains("low-detail") ? 8f + (float)rng.NextDouble() * 8f
                     : 20f + (float)rng.NextDouble() * 20f;
        return target / ModelHeight(model);
    }

    static GameObject[] LoadAll(string[] paths)
    {
        var list = new List<GameObject>();
        foreach (var p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{p}.fbx");
            if (go != null) list.Add(go);
        }
        if (list.Count == 0) Debug.LogWarning("[PropScatter] 模型一个都没加载到，检查 Assets/Models/Props");
        return list.ToArray();
    }

    static void Place(Transform parent, GameObject model, float x, float z, float scale, float yaw, bool solid = false)
    {
        float y = BattlefieldTerrain.SampleHeight(x, z);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(x, y, z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        go.isStatic = true;
        ApplyPalette(go);
        ApplyVariation(go);
        if (solid) AddSolidCollider(go);
    }

    /// <summary>
    /// 给建筑加实体碰撞盒（子弹打不穿、车开不过去）。
    /// 按网格局部包围盒的 8 个角换算到根节点局部空间，这样 yaw 转 90° 也不会把盒子横过来。
    /// </summary>
    static void AddSolidCollider(GameObject go)
    {
        bool has = false;
        var local = new Bounds(Vector3.zero, Vector3.zero);
        var toRoot = go.transform.worldToLocalMatrix;

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var e = mb.extents;
            var m = toRoot * mf.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    mb.center.x + ((i & 1) == 0 ? -e.x : e.x),
                    mb.center.y + ((i & 2) == 0 ? -e.y : e.y),
                    mb.center.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = m.MultiplyPoint3x4(corner);
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }

        if (!has) return;
        var box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
    }

    // ---- 每棵/每栋轻微色差，避免整片一个颜色 ----
    static MaterialPropertyBlock _mpb;

    static void ApplyVariation(GameObject go)
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        float r = 0.88f + (float)rng.NextDouble() * 0.24f;
        float g = 0.88f + (float)rng.NextDouble() * 0.24f;
        float b = 0.88f + (float)rng.NextDouble() * 0.24f;

        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mat = mr.sharedMaterial;
            if (mat == null || !mat.HasProperty("_Color")) continue;
            Color baseC = mat.GetColor("_Color");
            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", new Color(baseC.r * r, baseC.g * g, baseC.b * b, baseC.a));
            mr.SetPropertyBlock(_mpb);
        }
    }

    // ================= 材质调色 =================
    // Kenney 原包是青绿卡通配色，放战场上太扎眼。按材质名统一换成自然色；
    // 不在表里的（如城区 colormap 贴图材质）保留原样。

    static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        { "_defaultMat",     new Color(0.20f, 0.32f, 0.14f) },
        { "woodBark",        new Color(0.30f, 0.21f, 0.14f) },
        { "woodBarkDark",    new Color(0.24f, 0.17f, 0.12f) },
        { "woodBirch",       new Color(0.62f, 0.58f, 0.50f) },
        { "woodDark",        new Color(0.26f, 0.19f, 0.13f) },
        { "woodInner",       new Color(0.48f, 0.36f, 0.22f) },
        { "wood",            new Color(0.33f, 0.24f, 0.16f) },
        { "bark",            new Color(0.30f, 0.21f, 0.14f) },
        { "leafsGreen",      new Color(0.17f, 0.30f, 0.13f) },
        { "leafsDark",       new Color(0.12f, 0.22f, 0.10f) },
        { "leafsDarkGreen",  new Color(0.12f, 0.22f, 0.10f) },
        { "leafsGreenLight", new Color(0.26f, 0.38f, 0.16f) },
        { "leafsFall",       new Color(0.42f, 0.30f, 0.12f) },
        { "leafs",           new Color(0.17f, 0.30f, 0.13f) },
        { "grass",           new Color(0.28f, 0.36f, 0.16f) },
        { "grassLight",      new Color(0.36f, 0.44f, 0.20f) },
        { "corn",            new Color(0.50f, 0.46f, 0.20f) },
        { "dirt",            new Color(0.36f, 0.29f, 0.21f) },
        { "dirtDark",        new Color(0.28f, 0.23f, 0.17f) },
        { "stone",           new Color(0.40f, 0.40f, 0.38f) },
        { "stoneDark",       new Color(0.30f, 0.30f, 0.29f) },
        { "rock",            new Color(0.40f, 0.40f, 0.38f) },
        { "sand",            new Color(0.62f, 0.55f, 0.40f) },
        { "colorTan",        new Color(0.58f, 0.50f, 0.36f) },
        { "water",           new Color(0.20f, 0.34f, 0.40f) },
    };

    static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

    static void ApplyPalette(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (!Palette.TryGetValue(m.name, out var c)) continue;
                mats[i] = GetPaletteMat(m.name, c);
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static Material GetPaletteMat(string key, Color c)
    {
        if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;

        string path = $"Assets/Materials/Props/{key}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Props")) AssetDatabase.CreateFolder("Assets/Materials", "Props");
            mat = new Material(Shader.Find("Standard")) { color = c };
            mat.SetFloat("_Glossiness", 0.12f);
            AssetDatabase.CreateAsset(mat, path);
        }
        MatCache[key] = mat;
        return mat;
    }
}
