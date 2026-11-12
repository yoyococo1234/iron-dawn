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
    // 2026-11-09：树和建筑全部换成 Lux3D 生成的模型（Assets/Models/Props/Lux3D）。
    // 老的 Kenney/Polygon 资源包形状太糙、楼体尺寸和格子对不上，一直在穿模。
    // Lux3D 的模型统一归一化到「最长边 = 1.0、原点在几何中心」，所以 scale 直接等于目标高度（米）。
    static readonly string[] TreeModels =
    {
        "Lux3D/tree_pine_tall", "Lux3D/tree_pine_round", "Lux3D/tree_oak_broad",
        "Lux3D/tree_plateau_acacia", "Lux3D/tree_dead", "Lux3D/tree_palm",
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
    // 2026-11-11：建筑全部换成**废土风格**（Lux3D 生成，ArtSource/Props_Wasteland → Blender 转 FBX）。
    // 换风格的同时修掉"下面没生成明白"：每栋楼都会在 Place() 里加一块**地基裙边**（见 AddFoundation），
    // 裙边从地面往上露 0.35 m、往下埋 2 m，所以不管模型底部多脏都不会露缝。
    static readonly string[] BuildingModels =
    {
        "Lux3D/waste_block_low", "Lux3D/waste_block_mid", "Lux3D/waste_tower",
        "Lux3D/waste_shack", "Lux3D/waste_warehouse", "Lux3D/waste_wall",
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
        ResetOccupancy();

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

        // 排查开关：运行时可用命令行参数关掉部分物件（见 PropDebugToggle）
        if (root.GetComponent<PropDebugToggle>() == null) root.AddComponent<PropDebugToggle>();

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

        // 城区：设计 z −13~13.7、|x| < 45（2026-11-09 扩大：原来只有 z 0~13.7）。
        // 随机撒 + 按真实占地互斥；网格方式下格子（28~40 m）和楼体尺寸互相打架，要么重叠要么稀稀拉拉。
        const int target = 320;
        const float xzStretch = 1.45f;   // 用户反馈"建筑的长宽小了" —— 横向拉宽，楼变敦实
        for (int i = 0; i < 40000 && n < target; i++)
        {
            float jx = -45f + (float)rng.NextDouble() * 90f;
            float jz = -13f + (float)rng.NextDouble() * 26.7f;
            if (BattlefieldTerrainBuilder.CityRoadPaintMask(jx, jz) > 0.25f) continue;
            // 离水线太近的楼不要 —— 底座会露在沙滩/水面外，观感很怪
            if (BattlefieldTerrain.WaterDepth(jx * S, jz * S) > -3.0f) continue;
            // 出生线（x = ±640 世界）两侧留 45 m 走廊，免得车生在楼里
            float wx = jx * S;
            if (Mathf.Abs(wx - 640f) < 45f || Mathf.Abs(wx + 640f) < 45f) continue;

            var model = models[rng.Next(models.Length)];
            float scale = BuildingScale(model);
            // 楼体占位：按模型真实水平半径判重，避免两栋楼互相插进去
            if (!TryReserve(jx * S, jz * S, ModelFootprint(model).x * scale * xzStretch * 1.04f)) continue;
            float yaw = rng.Next(2) == 0 ? 0f : 90f;
            Place(holder, model, jx * S, jz * S, scale, yaw, solid: true, xzMul: xzStretch);
            n++;
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
            // Lux3D 的树归一化到「高度 = 1.0」，所以 scale 直接就是米：12~25 m
            float scale = 12f + (float)rng.NextDouble() * 13f;
            // 树冠允许轻微交叠（天然林相就是这样），但树干/主干不许叠
            if (!TryReserve(x, z, ModelFootprint(model).x * scale * 0.55f)) continue;
            // sinkFrac 0.13：把 Lux3D 树自带的土台整圈压进地面以下（用户反馈"树下面的土露出来了"）。
            // 之前用 Blender 删土台面 + 下沉 5%，结果 D3D12 下必崩（改过的树网格触发驱动 bug），
            // 所以退回原始网格，只靠下沉把土埋掉 —— 不动几何最安全。
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f, sinkFrac: 0.13f);
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
            if (!TryReserve(x, z, ModelFootprint(model).x * scale * 0.7f)) continue;
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 260) break;
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
            if (!TryReserve(x, z, ModelFootprint(model).x * scale * 0.8f)) continue;
            Place(holder, model, x, z, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 220) break;
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
            if (!TryReserve(dx * S, dz * S, ModelFootprint(model).x * scale)) continue;
            Place(holder, model, dx * S, dz * S, scale, (float)rng.NextDouble() * 360f);
            n++;
            if (n >= 120) break;
        }
        return n;
    }

    // ================= 工具 =================

    static bool IsCity(float dx, float dz) => dz >= -14f && dz <= 14f && Mathf.Abs(dx) < 46f;

    // ================= 占位索引：不许两个物件叠在一起 =================
    //
    // 2026-11-09 用户反馈「所有树和建筑物都会穿模」。原因：原来树是 9000 次纯随机撒点、
    // 没有任何最小间距；建筑按 1.7 设计格（世界 28 m）撒，但楼体本身远大于格子 —— 必然互相穿插。
    // 这里用一张空间哈希表记录已放置物件的 (x, z, 半径)，每次撒点先查冲突。

    const float OccCell = 24f;
    static readonly List<Vector4> _occ = new List<Vector4>();          // x, z, radius, spare
    static readonly Dictionary<long, List<int>> _occCells = new Dictionary<long, List<int>>();

    static long CellKey(int cx, int cz) => (long)cx * 1000003L + cz;

    static void ResetOccupancy()
    {
        _occ.Clear();
        _occCells.Clear();
    }

    /// <summary>试着占一块圆形地盘；被占就返回 false。</summary>
    static bool TryReserve(float x, float z, float radius)
    {
        if (radius < 0.01f) radius = 0.01f;
        int cx0 = Mathf.FloorToInt((x - radius) / OccCell), cx1 = Mathf.FloorToInt((x + radius) / OccCell);
        int cz0 = Mathf.FloorToInt((z - radius) / OccCell), cz1 = Mathf.FloorToInt((z + radius) / OccCell);

        for (int cx = cx0; cx <= cx1; cx++)
        {
            for (int cz = cz0; cz <= cz1; cz++)
            {
                if (!_occCells.TryGetValue(CellKey(cx, cz), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    var o = _occ[list[k]];
                    float need = o.z + radius;
                    float dx = o.x - x, dz = o.y - z;
                    if (dx * dx + dz * dz < need * need) return false;
                }
            }
        }

        int idx = _occ.Count;
        _occ.Add(new Vector4(x, z, radius, 0f));
        for (int cx = cx0; cx <= cx1; cx++)
        {
            for (int cz = cz0; cz <= cz1; cz++)
            {
                long key = CellKey(cx, cz);
                if (!_occCells.TryGetValue(key, out var lst)) { lst = new List<int>(); _occCells[key] = lst; }
                lst.Add(idx);
            }
        }
        return true;
    }

    static readonly Dictionary<GameObject, Vector2> _modelFootprint = new Dictionary<GameObject, Vector2>();

    /// <summary>
    /// 模型在**自身局部空间**的水平半径（x/z 里较大的那半边）和最低点 y。
    /// 半径用来做占地去重，最低点用来把模型底面贴着地形放（Lux3D 出的模型原点在几何中心，
    /// 直接放在地面高度会半埋进土里）。
    /// </summary>
    static Vector2 ModelFootprint(GameObject model)
    {
        if (_modelFootprint.TryGetValue(model, out var cached) && cached.x > 0.001f) return cached;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue;
        bool any = false;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var b = mesh.bounds;
            Vector3 c = model.transform.InverseTransformPoint(mf.transform.TransformPoint(b.center));
            Vector3 e = mf.transform.TransformVector(b.extents);
            e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
            minX = Mathf.Min(minX, c.x - e.x); maxX = Mathf.Max(maxX, c.x + e.x);
            minZ = Mathf.Min(minZ, c.z - e.z); maxZ = Mathf.Max(maxZ, c.z + e.z);
            minY = Mathf.Min(minY, c.y - e.y);
            any = true;
        }
        if (!any) { var v = new Vector2(1f, 0f); _modelFootprint[model] = v; return v; }

        float radius = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f;
        if (radius < 0.05f) radius = 1f;
        var res = new Vector2(radius, minY);
        _modelFootprint[model] = res;
        return res;
    }

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
        // 2026-11-11：换成废土模型后，按**名字**给目标高度。老逻辑只认 skyscraper / low-detail，
        // 新名字一个都不匹配 → 所有楼（包括 4 米的铁皮棚）都会被拉到 20~40 米。
        string n = model.name.ToLowerInvariant();
        float target;
        if (n.Contains("tower")) target = 30f + (float)rng.NextDouble() * 8f;          // 骨架高楼
        else if (n.Contains("block_mid")) target = 16f + (float)rng.NextDouble() * 5f; // 中层破楼
        else if (n.Contains("block_low")) target = 9f + (float)rng.NextDouble() * 4f;  // 低层废墟
        else if (n.Contains("warehouse")) target = 8f + (float)rng.NextDouble() * 3f;  // 塌陷厂房
        else if (n.Contains("shack")) target = 4f + (float)rng.NextDouble() * 2f;      // 铁皮棚
        else if (n.Contains("wall")) target = 3f + (float)rng.NextDouble() * 1.5f;     // 断墙
        // 老模型（万一还用）
        else if (n.Contains("skyscraper")) target = 60f + (float)rng.NextDouble() * 30f;
        else if (n.Contains("low-detail")) target = 8f + (float)rng.NextDouble() * 8f;
        else target = 20f + (float)rng.NextDouble() * 20f;
        return target / Mathf.Max(0.01f, ModelHeight(model));
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

    static void Place(Transform parent, GameObject model, float x, float z, float scale, float yaw,
                      bool solid = false, float xzMul = 1f, float sinkFrac = 0f)
    {
        var fp = ModelFootprint(model);
        // 底面贴地：减去模型自身最低点。原来直接放在地形高度上，模型原点在几何中心的那种会半埋。
        // sinkFrac：再往下沉"模型高度的百分比"，用来把 Lux3D 树模型自带的土台埋进地里
        //（用户反馈"一些树下面的土露出来了"）。
        float y = BattlefieldTerrain.SampleHeight(x, z) - fp.y * scale - sinkFrac * ModelHeight(model) * scale;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        // Lux3D 导出的 fbx 根节点带一个 270°(=-90°) 的 X 旋转（Blender Z-up → Unity Y-up 的轴向修正）。
        // 以前这里直接 `rotation = Euler(0, yaw, 0)` 把它覆盖掉，模型就全躺地上了 —— 必须乘在基准姿态上。
        var baseRot = go.transform.localRotation;
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(x, y, z);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * baseRot;
        go.transform.localScale = new Vector3(scale * xzMul, scale, scale * xzMul);
        // 2026-11-09：恢复 static 批处理。
        // 中间试过 isStatic=false + GPU Instancing，结果 D3D12 下驱动直接崩
        //（nvwgf2umx → D3D12Core，Unrecoverable GPU device error）；D3D11 下能跑但很卡。
        // 现在模型已经 decimate 到 1/6 面数，静态批处理的顶点缓冲完全吃得下，所以回到 static。
        go.isStatic = true;
        ApplyPalette(go, model.name);
        // Lux3D 的模型带贴图，且要享受 GPU Instancing ——
        // ApplyVariation 会写 MaterialPropertyBlock（每个实例一个 _Color），
        // 那会让 instancing 失效、每个实例变成独立 draw call。所以 Lux 模型不做逐实例调色。
        if (!model.name.StartsWith("tree_") && !model.name.StartsWith("building_"))
            ApplyVariation(go);
        // 2026-11-09：非建筑（树/岩石/灌木）不投影。
        // 1900+ 个物件全部参与阴影 pass 会把 D3D12 的显存预留吃干，实测直接
        // "Unrecoverable GPU device error / AvailableForReservation: 0" 崩掉。
        // 建筑保留投影（城区的立体感主要靠它）。
        if (!solid)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (solid)
        {
            AddSolidCollider(go);
            // 2026-11-12：**不再加地基裙边**（用户要求"把建筑的所有的裙边藏起来"）。
            // 11-11 加它本来是为了遮住 Lux3D 楼底的开缝，但上沿露出的那 0.22 m（坡地上更多）
            // 会把车顶停，且看起来像"看不见的矮墙" —— 用户明确要求去掉。
            // 场景里已有的 320 块地基已用 `objects | where name == "Foundation" | set active = false`
            // 整批隐藏（可撤销：把 active 设回 true 就回来）。AddFoundation 方法保留，别删。
            // AddFoundation(go, parent, BattlefieldTerrain.SampleHeight(x, z));
        }
        // 树也要有碰撞：以前树只有 Transform+Mesh，坦克开过去直接穿过、炮弹也打不中
        if (model.name.StartsWith("tree_"))
            AddTreeCollider(parent, x, z, BattlefieldTerrain.SampleHeight(x, z),
                            ModelHeight(model) * scale, fp.x * scale);
    }

    /// <summary>
    /// 树干碰撞：一根细胶囊。
    /// ⚠️ 必须挂在一个**没有旋转/缩放**的独立物体上：Lux3D 的 fbx 根节点带 270° 的 X 轴修正旋转，
    /// 胶囊挂在根上会跟着躺倒（变成横着的碰撞体，斜插在地里，撞不到东西）。
    /// 树冠不做碰撞（代价太高、而且会挡视线）。
    /// </summary>
    static void AddTreeCollider(Transform parent, float x, float z, float baseY, float worldHeight, float worldFootRadius)
    {
        // 2026-11-11 用户反馈"明明空的却走不过去"：原来半径按**树冠**算（最大 1.4 m = 2.8 m 见方），
        // 树干其实只有 0.3 m 粗，于是每棵树周围一圈空气墙。改成按树高估树干粗细。
        // 2026-11-12 用户要求"碰撞箱做精细"：再收一档（12 m 的树 → 0.53 m 见方，贴近真实树干）。
        float r = Mathf.Clamp(worldHeight * 0.022f, 0.16f, 0.34f);
        float h = Mathf.Max(2f, worldHeight * 0.55f);      // 树干到主枝分叉那一截

        var go = new GameObject("TreeCollider");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, baseY, z);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // 用 BoxCollider 而不是 Capsule：方柱更简单、PhysX 里最稳，
        // 而且树干碰撞本来就不需要圆角（实测胶囊在大量实例 + 频繁查询的组合下不稳）。
        var c = go.AddComponent<BoxCollider>();
        c.size = new Vector3(r * 2f, h, r * 2f);
        c.center = new Vector3(0f, h * 0.5f, 0f);
    }

    static Material _foundationMat;

    /// <summary>地基裙边的材质（灰色混凝土），做成资源存盘，否则场景引用会丢。</summary>
    static Material FoundationMat()
    {
        if (_foundationMat != null) return _foundationMat;

        const string path = "Assets/Materials/Props/foundation_concrete.mat";
        _foundationMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (_foundationMat == null)
        {
            var sh = Shader.Find("Standard");
            _foundationMat = new Material(sh) { color = new Color(0.33f, 0.315f, 0.29f) };
            _foundationMat.SetFloat("_Glossiness", 0.08f);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(_foundationMat, path);
        }
        return _foundationMat;
    }

    /// <summary>
    /// 建筑地基：一块比楼体略大的实心混凝土台，中心在地面下 1 m，
    /// 总高 3 m → 上沿露 0.35 m 遮住楼底接缝、往下埋 2.65 m 保证任何坡地都不露空。
    /// 带 BoxCollider（实心，不是 trigger），所以车也撞得住。
    /// </summary>
    static void AddFoundation(GameObject building, Transform parent, float groundY)
    {
        // 量这栋楼渲染体的世界包围盒（含旋转/缩放），只取水平占地
        bool has = false;
        var b = new Bounds();
        foreach (var r in building.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return;

        // 1.06 倍：只比楼体略大一点，正好把底边接缝盖住，不铺大灰板
        float w = Mathf.Max(1.5f, b.size.x * 1.06f);
        float d = Mathf.Max(1.5f, b.size.z * 1.06f);

        // 上沿露 0.22 m、总高 3.0 m → 中心在地面下 1.28 m，埋深 2.78 m（斜坡上也不会露空）
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Foundation";
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(b.center.x, groundY - 1.28f, b.center.z);
        go.transform.localScale = new Vector3(w, 3.0f, d);
        go.transform.localRotation = Quaternion.identity;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = FoundationMat();
        go.isStatic = true;
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

        // 2026-11-11 用户反馈"表面建模和碰撞区域完全不相干"：整栋楼一个实心盒会把
        // 窗户凹陷、镂空、雨棚底下、楼间缝隙全填实 —— 看不见的墙。
        // 改成**每个网格一个 MeshCollider**（碰撞 = 渲染），盒子留着但设成 trigger：
        // `NavGrid.MarkBuildings()` 还靠它读 bounds 标障碍格，删了 AI 会撞楼。
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            if (mf.GetComponent<MeshCollider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }

        var box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
        box.isTrigger = true;
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

    static void ApplyPalette(GameObject go, string modelName)
    {
        var lux = LuxMaterialFor(modelName);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                // Lux3D 出来的模型材质不带贴图（glb 的纹理被抽到 Textures/<模型名>/BaseColor.png），
                // 这里换成我们自己的带贴图材质
                if (lux != null && m.name.StartsWith("Designer_Logic"))
                {
                    mats[i] = lux;
                    changed = true;
                    continue;
                }
                if (!Palette.TryGetValue(m.name, out var c)) continue;
                mats[i] = GetPaletteMat(m.name, c);
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>Lux3D 模型的共用材质（Standard + 提取出来的 BaseColor 贴图）。不是 Lux3D 模型返回 null。</summary>
    static Material LuxMaterialFor(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return null;
        // 2026-11-11：加了废土建筑（waste_ 前缀）。漏了这一条 → 新楼全部渲染成白模，
        // 因为下面 Palette 里查不到 "Designer_Logic_PBR"，材质就保持原样（无贴图 + 纯白）。
        if (!modelName.StartsWith("tree_") && !modelName.StartsWith("building_")
            && !modelName.StartsWith("waste_")) return null;

        string key = "LUX_" + modelName;
        if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;

        string matPath = $"Assets/Materials/Props/{key}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Props")) AssetDatabase.CreateFolder("Assets/Materials", "Props");

            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");
            mat = new Material(sh) { color = Color.white };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.18f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Models/Props/Lux3D/Textures/{modelName}/BaseColor.png");
            if (tex != null)
            {
                mat.mainTexture = tex;
                mat.SetFloat("_Glossiness", 0.15f);
            }
            else
            {
                // 没贴图就退成灰绿，别留白模
                mat.color = new Color(0.42f, 0.46f, 0.38f);
            }
            mat.enableInstancing = false;   // D3D12 下 instancing 会触发驱动崩溃，用静态批处理代替
            AssetDatabase.CreateAsset(mat, matPath);
        }
        MatCache[key] = mat;
        if (mat.enableInstancing)
        {
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
        }
        return mat;
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
