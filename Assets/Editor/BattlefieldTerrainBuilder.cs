using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

/// <summary>
/// 正式战场地形生成器（2000×2000 m，2×2 分块，0.244 m/格）—— 唯一标准是 Docs/战场设计.md。
///
/// 为什么分块：Unity 单块 Terrain 的高度图上限是 4097。2 km 单块最细只有 0.49 m/格，
/// 想更细必须切块。这里切成 2×2，每块 1000 m × 4097 顶点 → 0.244 m/格。
///
/// 坐标系（载具适配时必看）：
///   地图世界范围 x、z ∈ [-1000, 1000]，四块 tile 原点分别是 (-1000,-1000) (-1000,0) (0,-1000) (0,0)
///   高度范围 y ∈ [-100, 300]（BaseY = -100，HeightRange = 400）
///   世界高度 y = -100 + normalizedHeight * 400
///   ⚠ 不要用 Terrain.activeTerrain.SampleHeight：分块后要用 BattlefieldTerrain.SampleHeight(worldPos)
///
/// 缩放：设计坐标（战场设计.md 的 ±60 坐标系）→ 世界坐标 = 设计坐标 × 16.667。
///   高度也按同一比例放大，所以所有坡度角度与 120 m 设计版完全一致，载具爬坡参数不用重调。
///
/// 菜单：Tools/铁皮黎明/① 生成正式战场地形（2000×2000）
/// </summary>
public static class BattlefieldTerrainBuilder
{
    // ================= 规格 =================
    public const float DesignSize = 120f;               // 战场设计.md 的坐标系边长（±60）
    public const float MapSize = 2000f;                 // 世界边长（±1000）
    public const float Scale = MapSize / DesignSize;    // 16.6667：设计坐标 → 世界坐标
    public const int Tiles = 2;                         // 2×2 分块
    public const float TileSize = MapSize / Tiles;      // 1000 m
    public const int HeightRes = 4097;                  // 每块顶点数 → 0.244 m/格
    public const int AlphaRes = 2048;                   // 地表混合图 → 0.49 m/纹素
    public const float DesignBaseY = -6f;               // 设计坐标最低点（海底）
    public const float DesignHeightRange = 24f;         // 设计坐标高度范围（最高 18 m）
    public const float BaseY = DesignBaseY * Scale;             // -100
    public const float HeightRange = DesignHeightRange * Scale; // 400
    public const float WaterLevel = -0.4f * Scale;              // -6.667（海平面）

    // 地表层序号（和 splatmap 通道一一对应）
    public const int L_SAND = 0, L_GRASS = 1, L_DIRT = 2, L_ROCK = 3, L_SNOW = 4;

    const string TerrainFolder = "Assets/Terrain";
    const string TileFolder = "Assets/Terrain/Tiles";
    const string LayerFolder = "Assets/Terrain/Layers";
    const string MatFolder = "Assets/Materials";
    const string SceneFolder = "Assets/Scenes";
    const string WaterMatPath = "Assets/Materials/Water_Sea.mat";
    const string ScenePath = "Assets/Scenes/Map_Battlefield.unity";

    // ================= 入口 =================
    /// <summary>菜单入口：一次性建完（4 块 tile 约 3~6 分钟，编辑器会卡住，属正常）。</summary>
    [MenuItem("Tools/铁皮黎明/① 生成正式战场地形（2000×2000）", false, 10)]
    public static void BuildAll()
    {
        SetupScene();
        for (int t = 0; t < Tiles * Tiles; t++) BuildTile(t);
        FinishScene();
        Debug.Log("[BattlefieldTerrain] 全部完成：" + ScenePath);
    }

    /// <summary>第一步：建数据资产、地表层、4 块地形、水面、灯光、相机，并落盘场景。</summary>
    [MenuItem("Tools/铁皮黎明/①a 建场景骨架（快）", false, 11)]
    public static void SetupScene()
    {
        EnsureFolders();
        var layers = BuildLayers();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        for (int tz = 0; tz < Tiles; tz++)
        {
            for (int tx = 0; tx < Tiles; tx++)
            {
                var data = LoadOrCreateTileData(tx, tz);
                data.terrainLayers = layers;
                EditorUtility.SetDirty(data);
                CreateTerrainObject(data, tx, tz);
            }
        }

        CreateWater();
        CreateLightingAndCameras();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[BattlefieldTerrain] 骨架已建：" + ScenePath);
    }

    /// <summary>第二步：逐块生成高度 + 地表（每次调用约 1 分钟，方便分次调用不超时）。</summary>
    public static string BuildTile(int index)
    {
        int tx = index % Tiles, tz = index / Tiles;
        var data = LoadOrCreateTileData(tx, tz);
        if (data.terrainLayers == null || data.terrainLayers.Length == 0)
            data.terrainLayers = BuildLayers();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        GenerateHeights(data, tx, tz);
        long tHeight = sw.ElapsedMilliseconds;
        ApplySplatmap(data, tx, tz);
        long tSplat = sw.ElapsedMilliseconds - tHeight;

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        string msg = $"[BattlefieldTerrain] tile {index} ({tx},{tz}) 高度 {tHeight} ms 地表 {tSplat} ms | {TileStats(data)}";
        Debug.Log(msg);
        return msg;
    }

    /// <summary>只重建地表贴图（调 splat 规则时用）。</summary>
    [MenuItem("Tools/铁皮黎明/①b 只重建地表贴图（全部）", false, 12)]
    public static void RebuildSplats()
    {
        for (int t = 0; t < Tiles * Tiles; t++)
        {
            int tx = t % Tiles, tz = t / Tiles;
            var data = LoadOrCreateTileData(tx, tz);
            ApplySplatmap(data, tx, tz);
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[BattlefieldTerrain] 地表贴图已重建");
    }

    /// <summary>最后一步：地形接边、保存场景。</summary>
    public static void FinishScene()
    {
        var terrains = new Terrain[Tiles, Tiles];
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            int tx = Mathf.RoundToInt((t.transform.position.x + MapSize * 0.5f) / TileSize);
            int tz = Mathf.RoundToInt((t.transform.position.z + MapSize * 0.5f) / TileSize);
            if (tx >= 0 && tx < Tiles && tz >= 0 && tz < Tiles) terrains[tx, tz] = t;
        }
        for (int tz = 0; tz < Tiles; tz++)
            for (int tx = 0; tx < Tiles; tx++)
            {
                var t = terrains[tx, tz];
                if (t == null) continue;
                t.SetNeighbors(
                    tx > 0 ? terrains[tx - 1, tz] : null,
                    tz < Tiles - 1 ? terrains[tx, tz + 1] : null,
                    tx < Tiles - 1 ? terrains[tx + 1, tz] : null,
                    tz > 0 ? terrains[tx, tz - 1] : null);
            }

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    // ================= 高度场 =================
    static TerrainData LoadOrCreateTileData(int tx, int tz)
    {
        string path = $"{TileFolder}/Battlefield_Tile_{tx}_{tz}.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null)
        {
            data = new TerrainData();
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = AlphaRes;
            data.baseMapResolution = 1024;
            data.size = new Vector3(TileSize, HeightRange, TileSize);
            AssetDatabase.CreateAsset(data, path);
        }
        data.size = new Vector3(TileSize, HeightRange, TileSize);
        return data;
    }

    static void GenerateHeights(TerrainData data, int tx, int tz)
    {
        int res = data.heightmapResolution;
        var heights = new float[res, res];
        float cell = TileSize / (res - 1);
        float ox = -MapSize * 0.5f + tx * TileSize;
        float oz = -MapSize * 0.5f + tz * TileSize;

        for (int j = 0; j < res; j++)
        {
            float z = oz + j * cell;
            for (int i = 0; i < res; i++)
            {
                float x = ox + i * cell;
                float worldY = HeightWorld(x, z);
                heights[j, i] = Mathf.Clamp01((worldY - BaseY) / HeightRange);
            }
        }

        Despike(heights, res);
        SmoothHeightmap(heights, res, 2);
        data.SetHeights(0, 0, heights);
    }

    /// <summary>
    /// 高度图平滑（3×3 均值，边界用夹取索引，保证四块地形的接缝仍然连续）。
    ///
    /// 为什么需要：山体阴影图上能看到一条条虚线状的细脊/细沟（用户反复反馈的"沟壑"），
    /// 那是高频噪声和削填边界留下的 1~2 格窄特征。3×3 平滑两遍刚好抹掉单格特征，
    /// 又不会动到大尺度地形（山脊、山谷还在）。
    /// </summary>
    static void SmoothHeightmap(float[,] h, int res, int passes)
    {
        var tmp = new float[res, res];
        for (int p = 0; p < passes; p++)
        {
            System.Array.Copy(h, tmp, h.Length);
            for (int j = 0; j < res; j++)
            {
                int jm = j > 0 ? j - 1 : 0;
                int jp = j < res - 1 ? j + 1 : res - 1;
                for (int i = 0; i < res; i++)
                {
                    int im = i > 0 ? i - 1 : 0;
                    int ip = i < res - 1 ? i + 1 : res - 1;
                    float sum = tmp[jm, im] + tmp[jm, i] + tmp[jm, ip]
                              + tmp[j, im] + tmp[j, i] + tmp[j, ip]
                              + tmp[jp, im] + tmp[jp, i] + tmp[jp, ip];
                    h[j, i] = sum / 9f;
                }
            }
        }
    }

    /// <summary>
    /// 去尖刺：把"一格之内突然高/低好几米"的像素拉回邻域范围内。
    ///
    /// 为什么需要：道路削填、鞍部压平这些操作在山体上切出近乎垂直的壁，
    /// 在高度图上就是相邻两格落差 2~6 m（≈ 85°+），跑起来看着像地形 bug
    /// （2026-11-06 用户反馈"有些陆地骤然的起伏"）。
    /// 这里只削"比四个邻居都高/低超过 maxStep"的格子，正常山坡（哪怕 50°）不受影响。
    /// </summary>
    static void Despike(float[,] h, int res)
    {
        const int Passes = 12;
        float maxStep = 0.5f / HeightRange;          // 单格最大允许落差 ≈ 0.5 m（约 64°）
        var tmp = new float[res, res];

        for (int pass = 0; pass < Passes; pass++)
        {
            System.Array.Copy(h, tmp, h.Length);
            bool changed = false;
            for (int j = 1; j < res - 1; j++)
            {
                for (int i = 1; i < res - 1; i++)
                {
                    float c = h[j, i];
                    float n0 = h[j, i - 1], n1 = h[j, i + 1], n2 = h[j - 1, i], n3 = h[j + 1, i];
                    float mn = Mathf.Min(Mathf.Min(n0, n1), Mathf.Min(n2, n3));
                    float mx = Mathf.Max(Mathf.Max(n0, n1), Mathf.Max(n2, n3));

                    float target = c;
                    if (c > mx + maxStep) target = mx + maxStep;
                    else if (c < mn - maxStep) target = mn - maxStep;

                    if (target != c) { tmp[j, i] = target; changed = true; }
                }
            }
            System.Array.Copy(tmp, h, h.Length);
            if (!changed) break;
        }
    }

    /// <summary>世界坐标 (x,z) → 世界高度 y。设计坐标算完再等比放大，保证坡度角度不变。</summary>
    public static float HeightWorld(float x, float z)
    {
        float y = HeightDesign(x / Scale, z / Scale) * Scale;
        // 世界尺度的细颗粒（设计尺度放大 16.7 倍后近看偏光，补一层 3 m 级起伏）
        // 公路上要压掉，否则路基会被细噪声顶得坑坑洼洼、纵坡超标
        float road = PassRoadMask(x / Scale, z / Scale);
        y += 0.45f * (Fbm(x * 0.32f + 61f, z * 0.32f + 37f, 2) - 0.5f);
        return y;
    }

    /// <summary>设计坐标（±60）高度函数。v2 版：南北分带——上城区 / 下山区 / 中间与两翼平原。</summary>
    public static float HeightDesign(float x, float z)
    {
        // ---------- 三块陆地场 ----------
        float city = CityField(x, z);        // 中央城区 0.5~2 m
        float plain = PlainField(x, z);      // 平原 0.5~2 m
        float mnt = MountainField(x, z);     // 山地中心 5~18 m
        float lowHill = LowHillField(x, z);  // 两翼低丘 1~4 m

        // ---------- 城区：|x| < 46，z 从 -14 一直到 14（2026-11-09 用户要求"减少中间平原、扩大城区"）----------
        // 原来只占 z 0~14（世界 233 m 深），往南扩到 -14（世界 -233 m），深度变 3 倍，
        // 把山脚和城区之间那条大平原吃掉。
        float cityX = 1f - SStep(40f, 50f, Mathf.Abs(x));
        float cityZ = SStep(-14f, -8f, z) * (1f - SStep(13f, 17f, z));
        float cityW = cityX * cityZ;
        float land = Mathf.Lerp(plain, city, cityW);

        // ---------- 山地：z < -20 接管；中间高、两翼压成低丘（出兵线要能走）----------
        float mntZ = 1f - SStep(-26f, -19f, z);                  // z < -26 → 1
        float center = 1f - SStep(38f, 50f, Mathf.Abs(x));       // |x| < 38 → 1（山地中心）
        float mntH = Mathf.Lerp(lowHill, mnt, center);
        land = Mathf.Lerp(land, mntH, mntZ);

        // ---------- 山脊鞍部 → 再削公路（顺序不能反：先鞍部后公路，否则两段抬升叠加成陡坡）----------
        float roadW;
        land = CarvePass(land, x, z, out roadW);
        land = FlattenSaddle(land, x, z);

        // ---------- 海岸 ----------
        float coast = CoastProfile(x, z);
        float k = SStep(12.5f, 19f, z);
        float h = Mathf.Lerp(land, coast, k);

        // ---------- 微起伏（城区和公路压住）----------
        float micro = (Fbm(x * 0.55f + 4.2f, z * 0.55f + 8.8f, 2) - 0.5f) * 0.16f;
        h += micro * (1f - 0.75f * cityW) * (1f - 0.85f * roadW);

        // ---------- 地图边界：陆侧包一圈山，把"正方形地图"藏起来 ----------
        // 2026-11-06：原来是 2 km 的正方形平地，边上是空的，一眼就能看出地图边界。
        // 常见做法（战地/战术小队这类）是用山脉/海湾把可玩区围起来，玩家看到的是山谷而不是直边。
        // 北侧是海，不抬（留给舰队活动）；滩头带 z>12 也不抬，免得把沙滩顶成山。
        {
            float edge = Mathf.Max(Mathf.Abs(x) - 42f, -z - 44f);   // 0 = 进入边界带
            if (edge > 0f)
            {
                float rimK = SStep(0f, 13f, edge);                   // 平滑抬升
                rimK *= 1f - SStep(5f, 12f, z);                      // 靠海一侧取消
                // 山地据点（鞍部 (0,-45)）正好在南边边界带上，边界山会把它顶成断崖 ——
                // 据点圈半径 168 m，这里在鞍部 12 设计米内不抬边界山。
                float dSaddle = Vector2.Distance(new Vector2(x, z), new Vector2(0f, -45f));
                rimK *= SStep(11f, 20f, dSaddle);
                if (rimK > 0.001f)
                {
                    float rimH = 7f + 15f * Fbm(x * 0.021f + 3.1f, z * 0.021f + 7.7f, 3);
                    h = Mathf.Lerp(h, Mathf.Max(h, rimH), rimK);
                }
            }
        }

        return h;
    }

    // ---- 海岸（含海底）----
    static float CoastProfile(float x, float z)
    {
        // 海岸线前后摆动 ±1.6 m，别是一条直线
        float zz = z + (Fbm(x * 0.055f + 21.3f, 5.5f, 3) - 0.5f) * 3.2f;

        float h;
        if (zz <= 14f) h = 0.3f;                                                  // 滩头靠陆侧
        else if (zz <= 20f) h = Mathf.Lerp(0.3f, -1.2f, SStep(14f, 20f, zz));     // 滩头：浅水→沙地
        else if (zz <= 30f) h = Mathf.Lerp(-1.2f, -4.6f, SStep(20f, 30f, zz));    // 大陆架
        else h = Mathf.Lerp(-4.6f, -6.0f, SStep(30f, 58f, zz));                   // 深水盆地

        if (zz > 18f)   // 海底起伏：沙洲 + 沟槽
            h += (Fbm(x * 0.08f + 7.1f, z * 0.08f + 2.9f, 3) - 0.5f) * 1.1f * SStep(18f, 26f, zz);

        return h;
    }

    // ---- 山地中心（5~18 m）：主脊 + 次脊 + 碎岩 ----
    static float MountainField(float x, float z)
    {
        // 山脚线加扰动，别做成一条笔直的等高线
        float zWobble = z + (Fbm(x * 0.03f + 13f, 7.7f, 2) - 0.5f) * 10f;
        float ramp = SStep(-18f, -40f, zWobble);    // 从平原进山，22 m（世界 367 m）缓过渡
        if (ramp <= 0.001f) return 0f;

        // 域扭曲：把噪声坐标揉一下，山脊就不会是同心圆/穹顶
        float wx = (Fbm(x * 0.011f + 4f, z * 0.011f + 9f, 2) - 0.5f) * 16f;
        float wz = (Fbm(x * 0.011f + 17f, z * 0.011f + 2f, 2) - 0.5f) * 16f;
        float px = x + wx, pz = z + wz;

        float ridge = RidgedFbm(px * 0.026f + 11f, pz * 0.026f + 5f, 4);
        float mid = RidgedFbm(px * 0.11f + 31f, pz * 0.11f + 23f, 3);
        float fine = RidgedFbm(px * 0.32f + 3f, pz * 0.32f + 7f, 3);
        float rock = Fbm(px * 0.22f + 7f, pz * 0.22f + 13f, 3) - 0.5f;

        float h = 5.0f
                + 10.0f * Mathf.Pow(ridge, 1.2f)
                + 2.6f * mid * (0.30f + 0.70f * ridge)              // 次脊：只在主脊上长
                + 1.0f * fine * (0.20f + 0.80f * ridge);            // 碎岩
        h += rock * 1.1f * (0.35f + 0.65f * ridge);
        h += 1.2f * (Fbm(x * 0.018f + 5f, z * 0.018f + 2f, 2) - 0.5f);

        // 山体分组：2~3 座山包，别做成一条等高的墙
        float massif = 0.55f + 0.80f * Fbm(x * 0.013f + 9f, z * 0.013f + 21f, 2);
        return SmoothClamp(h * massif, 0f, 18f, 1.2f) * ramp;
    }

    // ---- 两翼低丘（1~4 m）：坦克能直接翻过去 ----
    static float LowHillField(float x, float z)
    {
        float n = Fbm(x * 0.045f + 29f, z * 0.045f + 7f, 4);
        float h = 0.9f + 2.6f * n;
        h += (Fbm(x * 0.13f + 11f, z * 0.13f + 3f, 3) - 0.5f) * 0.5f;
        return SmoothClamp(h, 0.8f, 4f, 0.5f);
    }

    // ---- 平原（0.5~2 m）：大缓坡 + 中尺度起伏 ----
    static float PlainField(float x, float z)
    {
        float n = Fbm(x * 0.026f + 17f, z * 0.026f + 41f, 4);
        float h = 0.6f + 1.3f * n;
        h += (Fbm(x * 0.07f + 5f, z * 0.07f + 23f, 3) - 0.5f) * 0.5f;
        h += (Fbm(x * 0.2f, z * 0.2f, 2) - 0.5f) * 0.15f;
        return SmoothClamp(h, 0.5f, 2f, 0.4f);
    }

    // ---- 中央城区（0.5~2 m，街道网格压平，能开坦克）----
    static float CityField(float x, float z)
    {
        float n = Fbm(x * 0.021f + 3f, z * 0.021f + 9f, 3);
        float h = 1.1f + (n - 0.5f) * 0.8f;

        float road = RoadMask(x, z);                     // 街道网格压平
        h = Mathf.Lerp(h, 1.0f, road * 0.85f);
        h += (Fbm(x * 0.22f, z * 0.22f, 2) - 0.5f) * 0.12f * (1f - road);
        return SmoothClamp(h, 0.5f, 2f, 0.4f);
    }

    // ---- 街道网格（设计坐标 12 m 一条 → 世界 200 m 一条）----
    // 压平用宽掩码（路基平），上色用窄掩码（不然路面像 50 m 宽的沙带）
    public static float RoadMask(float x, float z)
    {
        return Mathf.Max(RoadLine(x), RoadLine(z));
    }

    static float RoadLine(float v)
    {
        float t = Mathf.Repeat(v + 6f, 12f);
        float d = Mathf.Min(t, 12f - t);
        return 1f - SStep(1.6f, 2.6f, d);
    }

    /// <summary>城区路面上色宽度：约 23~43 m 世界宽度。</summary>
    public static float CityRoadPaintMask(float x, float z)
    {
        return Mathf.Max(RoadPaintLine(x), RoadPaintLine(z));
    }

    static float RoadPaintLine(float v)
    {
        float t = Mathf.Repeat(v + 6f, 12f);
        float d = Mathf.Min(t, 12f - t);
        return 1f - SStep(0.7f, 1.3f, d);
    }

    // ---- 上山盘山路（2026-11-06 用户选了 A 方案：山体保留，上山走盘山路）----
    // 注意：这里只做**地形削填**，不铺任何路面贴图 —— 所以不会出现之前那种像地裂的土路带子。
    // 路基高度从平原 1.0 爬到平台 8.0（设计米），路径长 ~47 设计米 → 平均坡度 ~8.5°。
    static readonly Vector2[][] PassPaths =
    {
        // A 我方侧：盘山路（S 形）
        new[]
        {
            new Vector2(-26f, -16f), new Vector2(-30f, -24f), new Vector2(-26f, -32f),
            new Vector2(-16f, -38f), new Vector2(-6f, -43f),  new Vector2(0f, -45f)
        },
        // B 敌方侧：镜像
        new[]
        {
            new Vector2( 26f, -16f), new Vector2( 30f, -24f), new Vector2( 26f, -32f),
            new Vector2( 16f, -38f), new Vector2( 6f, -43f),  new Vector2(0f, -45f)
        },
    };
    static readonly float[][] PassHeights =
    {
        new[] { 1.0f, 2.2f, 4.0f, 6.5f, 8.0f, 8.0f },
        new[] { 1.0f, 2.2f, 4.0f, 6.5f, 8.0f, 8.0f },
    };

    static float CarvePass(float h, float x, float z, out float roadW)
    {
        var p = new Vector2(x, z);
        float bestD = float.MaxValue;

        // 路基高度按"所有邻近路段反距离加权"混合，而不是只取最近那一段。
        //
        // 为什么（2026-11-06 踩到的坑）：折线路径的"最近点"在两条段的等距线附近会突然从
        // 一段跳到另一段，两段在各自最近点上的路基高度不一样 → 高度函数出现 0.3~0.6 设计米
        // （世界 5~10 m）的**阶跃**，山体上就是一条笔直的断崖，看着像地形 bug。
        // 加权平均后这个跳变被抹平了。
        const float W = 14f;
        float wSum = 0f, hSum = 0f;

        for (int r = 0; r < PassPaths.Length; r++)
        {
            var path = PassPaths[r];
            var hh = PassHeights[r];
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, a + ab * t);
                if (d < bestD) bestD = d;
                if (d >= W) continue;
                float w = (1f / (d * d + 0.05f)) * (1f - SStep(W * 0.6f, W, d));
                wSum += w;
                hSum += w * Mathf.Lerp(hh[i], hh[i + 1], t);
            }
        }

        roadW = 1f - SStep(3f, 6f, bestD);                     // 给地表/噪声用
        if (bestD > W || wSum <= 0f) return h;

        float target = hSum / wSum;
        float fall = 1f - SStep(2.0f, W, bestD);               // 路基 ~33 m 宽，往外 233 m 渐变
        return Mathf.Lerp(h, target, fall);                    // 削高填低，路基是平的
    }

    /// <summary>
    /// 从平原通往山地据点（鞍部 (0,−45)）的**缓坡走廊**。
    ///
    /// 2026-11-06：用户要求把公路全去掉，但去掉之后山地据点就彻底上不去了
    /// （实测正面坡度 49°，坦克最大爬 22~35°）。这里不是公路 —— 没有路面贴图，
    /// 只是把山体在一条 200 m 宽的走廊里削成 ~11° 的缓坡，看上去就是一道天然山谷。
    /// </summary>
    static float GentleRamp(float h, float x, float z)
    {
        // 三个方向都做平滑过渡，不能有硬边界 ——
        // 之前 z 两端是 if 直接切断，在 z=-18（世界 -300）切出一条 3~5 m 的直坎，
        // 就是用户看到的"很明显的高低不平"。
        float wzIn = 1f - SStep(-22f, -15f, z);        // 从平原进入：-22 → -15 渐入
        float wzOut = 1f - SStep(-47f, -43f, z);       // 进平台前渐出
        float wz = wzIn * wzOut;
        if (wz <= 0.001f) return h;

        float t = Mathf.Clamp01(Mathf.InverseLerp(-18f, -45f, z));
        float target = Mathf.Lerp(1.0f, 8.0f, t);                 // 设计高度：平原 1.0 → 平台 5.0
        float wx = 1f - SStep(7f, 15f, Mathf.Abs(x));             // 走廊半宽 7~15 设计米（117~250 m）
        float w = wz * wx;
        if (w <= 0.001f) return h;
        return Mathf.Lerp(h, target, w);                         // 削高也填低，走廊内是均匀的缓坡
    }

    /// <summary>山脊鞍部：资源点③ 站人的那块高地（设计 (0,−45)），边缘过渡放宽，别做成坑。</summary>
    static float FlattenSaddle(float h, float x, float z)
    {
        float d = Vector2.Distance(new Vector2(x, z), new Vector2(0f, -45f));
        if (d > 14f) return h;
        // 2026-11-06：据点圈半径 168 m（设计 10），原来的 6~28 过渡带太窄，
        // 圈会顺着山体爬到断崖上。现在 11 设计米（183 m）内完全压平，11~24 再平滑过渡。
        float fall = 1f - SStep(11f, 14f, d);
        return Mathf.Lerp(h, 8.0f, fall);
    }

    /// <summary>山口公路的路面掩码（给地表贴图用，0~1）。</summary>
    public static float PassRoadMask(float x, float z)
    {
        float d = float.MaxValue;
        var p = new Vector2(x, z);
        foreach (var path in PassPaths) d = Mathf.Min(d, DistToPath(p, path));
        return 1f - SStep(3f, 6f, d);
    }

    /// <summary>山口公路"上色"宽度（约 33~73 m 世界宽度），比削填范围窄得多。</summary>
    public static float PassRoadPaintMask(float x, float z)
    {
        float d = float.MaxValue;
        var p = new Vector2(x, z);
        foreach (var path in PassPaths) d = Mathf.Min(d, DistToPath(p, path));
        return 1f - SStep(1.0f, 2.2f, d);
    }

    static float DistToPath(Vector2 p, Vector2[] path)
    {
        float best = float.MaxValue;
        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector2 a = path[i], b = path[i + 1];
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best) best = d;
        }
        return best;
    }

    // ================= 地表材质 =================
    static TerrainLayer[] BuildLayers()
    {
        // tileSize 是世界米数：2 km 图上不能还用 8~15 m，否则纹理重复得像地砖。
        // 30~48 m 兼顾远景不重复、近景不糊（1K 贴图 → 每纹素 3~5 cm）。
        return new List<TerrainLayer>
        {
            MakeLayer("L0_Sand",  "Sand",  30f, 0.55f, 0.10f),
            MakeLayer("L1_Grass", "Grass", 34f, 0.45f, 0.15f),
            MakeLayer("L2_Dirt",  "Dirt",  28f, 0.60f, 0.18f),
            MakeLayer("L3_Rock",  "Rock",  48f, 1.00f, 0.22f),
            MakeLayer("L4_Snow",  "Snow",  40f, 0.35f, 0.32f),
        }.ToArray();
    }

    static TerrainLayer MakeLayer(string assetName, string texFolder, float tile, float normalScale, float smoothness)
    {
        string albedo = $"Assets/Textures/Terrain/{texFolder}/Albedo.jpg";
        string normal = $"Assets/Textures/Terrain/{texFolder}/Normal.jpg";

        EnsureNormalImport(normal);

        string path = $"{LayerFolder}/{assetName}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }

        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
        layer.tileSize = new Vector2(tile, tile);
        layer.tileOffset = Vector2.zero;
        layer.normalScale = normalScale;
        layer.smoothness = smoothness;
        layer.metallic = 0f;
        layer.specular = new Color(0.15f, 0.15f, 0.15f);

        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void EnsureNormalImport(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[BattlefieldTerrain] 找不到贴图 " + path); return; }
        if (ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
    }

    /// <summary>按高度 + 坡度 + 区域生成 5 层混合图。优先级：雪 > 沙 > 岩 > 土 > 草。</summary>
    static void ApplySplatmap(TerrainData data, int tx, int tz)
    {
        int res = data.alphamapResolution;
        var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
        var map = new float[res, res, 5];
        float cell = TileSize / (res - 1);
        float ox = -MapSize * 0.5f + tx * TileSize;
        float oz = -MapSize * 0.5f + tz * TileSize;

        for (int j = 0; j < res; j++)
        {
            float z = oz + j * cell;
            float dz = z / Scale;                       // 设计坐标 z
            for (int i = 0; i < res; i++)
            {
                float x = ox + i * cell;
                float dx = x / Scale;                   // 设计坐标 x

                float y = SampleWorldY(heights, x, z, ox, oz);
                float yd = y / Scale;                   // 设计坐标高度
                float slope = SlopeDeg(heights, x, z, ox, oz);

                // 沙滩：滩头带（设计 z 12~28，含浅水海底）+ 靠海的低洼处
                float beachBand = SStep(12f, 16f, dz) * (1f - SStep(28f, 36f, dz));
                float sand = Mathf.Max(SStep(1.0f, -0.6f, yd) * SStep(6f, 12f, dz), beachBand);

                // 岩石：陡坡、山地中高段、以及缓丘上的裸岩露头
                float outcrop = SStep(0.72f, 0.90f, RidgedFbm(dx * 0.09f + 3f, dz * 0.09f + 51f, 3))
                              * SStep(2.0f, 4.2f, yd) * (1f - SStep(26f, 38f, slope));
                float rock = SStep(26f, 40f, slope)
                           + SStep(8.5f, 13f, yd) * (1f - SStep(26f, 36f, slope)) * 0.6f
                           + outcrop * 0.9f;

                // 雪：山顶且不太陡
                float snow = SStep(12.2f, 13.8f, yd) * (1f - SStep(26f, 38f, slope));

                // 土：中段海拔 + 城区路面 + 大尺度裸土斑（打破草地一片绿）
                float cityW = CityMask(dx, dz);
                float patch = SStep(0.55f, 0.78f, Fbm(dx * 0.045f + 71f, dz * 0.045f + 29f, 3));
                float dirt = SStep(1.8f, 5.5f, yd) * (1f - SStep(28f, 40f, slope)) * 0.8f
                           + CityRoadPaintMask(dx, dz) * cityW * 1.4f
                           + patch * 0.75f * (1f - cityW) * (1f - SStep(20f, 32f, slope))
                           + PassRoadPaintMask(dx, dz) * 0.0f;     // 山口公路是土路

                // 噪声打散边界，别让贴图分界线像等高线
                float n = Fbm(dx * 0.18f + 13f, dz * 0.18f + 7f, 3) - 0.5f;
                sand *= Mathf.Clamp01(1f + n * 0.7f);
                dirt *= Mathf.Clamp01(1f + n * 0.9f);
                rock *= Mathf.Clamp01(1f + n * 0.5f);

                sand = Mathf.Clamp01(sand);
                rock = Mathf.Clamp01(rock);
                snow = Mathf.Clamp01(snow);
                dirt = Mathf.Clamp01(dirt);

                // 按优先级逐层扣减，保证 5 个通道和 = 1
                float wSnow = snow;
                float wSand = sand * (1f - wSnow);
                float wRock = rock * (1f - wSnow) * (1f - wSand);
                float wDirt = dirt * (1f - wSnow) * (1f - wSand) * (1f - wRock);
                float wGrass = Mathf.Max(0f, 1f - wSnow - wSand - wRock - wDirt);

                float sum = wSnow + wSand + wRock + wDirt + wGrass;
                if (sum < 1e-4f) { wGrass = 1f; sum = 1f; }

                map[j, i, L_SAND] = wSand / sum;
                map[j, i, L_GRASS] = wGrass / sum;
                map[j, i, L_DIRT] = wDirt / sum;
                map[j, i, L_ROCK] = wRock / sum;
                map[j, i, L_SNOW] = wSnow / sum;
            }
        }

        data.SetAlphamaps(0, 0, map);
        EditorUtility.SetDirty(data);
    }

    /// <summary>城区掩码（2026-11-09：z −14~17、|x| &lt; 46，比原来深 3 倍）。</summary>
    static float CityMask(float x, float z)
    {
        float cityX = 1f - SStep(40f, 50f, Mathf.Abs(x));
        float cityZ = SStep(-14f, -8f, z) * (1f - SStep(13f, 17f, z));
        return cityX * cityZ;
    }

    // ================= 场景物件 =================
    static void CreateTerrainObject(TerrainData data, int tx, int tz)
    {
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = $"Battlefield_Terrain_{tx}_{tz}";
        go.transform.position = new Vector3(-MapSize * 0.5f + tx * TileSize, BaseY, -MapSize * 0.5f + tz * TileSize);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var terrain = go.GetComponent<Terrain>();
        terrain.heightmapPixelError = 5f;                 // 1.5 太细，顶点爆炸；5 在 2 km 图上几乎看不出差别
        terrain.basemapDistance = 600f;                   // 更早切 basemap，省大量三角形
        terrain.drawInstanced = true;
        terrain.shadowCastingMode = ShadowCastingMode.Off; // 2 km 地形投阴影极贵，且几乎看不出来
        terrain.heightmapMaximumLOD = 0;
    }

    static void CreateWater()
    {
        var shader = Shader.Find("铁皮黎明/Water");
        if (shader == null)
        {
            Debug.LogWarning("[BattlefieldTerrain] 找不到 铁皮黎明/Water 着色器，退回 Standard");
            shader = Shader.Find("Standard");
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, WaterMatPath);
        }
        mat.shader = shader;

        // 自定义水面：波浪法线 + 菲涅尔 + 按水深变色 + 近岸泡沫
        mat.SetColor("_ShallowColor", new Color(0.30f, 0.55f, 0.52f, 0.75f));
        mat.SetColor("_DeepColor", new Color(0.03f, 0.12f, 0.24f, 0.95f));
        mat.SetColor("_SkyColor", new Color(0.58f, 0.72f, 0.86f, 1f));
        mat.SetFloat("_WaveStrength", 0.22f);
        mat.SetFloat("_WaveScale", 0.18f);
        mat.SetFloat("_WaveSpeed", 0.7f);
        mat.SetFloat("_SpecPower", 90f);
        mat.SetFloat("_SpecIntensity", 1.3f);
        mat.SetFloat("_DepthFade", 30f);
        mat.SetFloat("_FoamWidth", 6f);
        mat.renderQueue = 3000;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);

        // 水面铺满整张地图：陆地比海面高 5~30 m，整块水会被地形自然遮住，
        // 真正露出来的只有地形低于海平面的地方 —— 岸线由地形决定，是弯的，
        // 不会出现"水面 Plane 边缘一条直线切在沙滩上"的假边。
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Water_Sea";
        water.transform.position = new Vector3(0f, WaterLevel, 0f);
        water.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f);  // Plane 原始 10×10 m
        water.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(water.GetComponent<Collider>());
    }

    static void CreateLightingAndCameras()
    {
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.88f);
        light.intensity = 1.15f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.72f;
        lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

        // 补光：从反方向打一盏弱的，避免背光坡（尤其山地北面）黑成一块
        var fillGo = new GameObject("Sun_Fill");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.62f, 0.72f, 0.85f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;
        fillGo.transform.rotation = Quaternion.Euler(28f, 150f, 0f);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.60f, 0.70f);
        RenderSettings.ambientEquatorColor = new Color(0.40f, 0.42f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.20f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.70f, 0.78f);
        RenderSettings.fogStartDistance = 1400f;
        RenderSettings.fogEndDistance = 4000f;

        QualitySettings.shadowDistance = 250f;                 // 700 太远，2 km 图上没有意义还吃性能
        QualitySettings.shadowCascades = 2;
        QualitySettings.shadows = ShadowQuality.HardOnly;      // 软阴影在大图上性价比低

        // 斜视全景
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 45f;
        cam.farClipPlane = 8000f;
        cam.depthTextureMode = DepthTextureMode.Depth;    // 水面着色器要读 _CameraDepthTexture
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(1500f, 900f, -1500f);
        camGo.transform.LookAt(new Vector3(0f, 40f, 150f));

        // 正上方俯视（对照 Docs/战场平面图_v7.png 检查区域划分）
        var topGo = new GameObject("Camera_TopDown");
        var top = topGo.AddComponent<Camera>();
        top.orthographic = true;
        top.orthographicSize = MapSize * 0.5f;
        top.farClipPlane = 4000f;
        top.depthTextureMode = DepthTextureMode.Depth;
        topGo.transform.position = new Vector3(0f, 1200f, 0f);
        topGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // ================= 工具 =================
    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(TerrainFolder)) AssetDatabase.CreateFolder("Assets", "Terrain");
        if (!AssetDatabase.IsValidFolder(TileFolder)) AssetDatabase.CreateFolder(TerrainFolder, "Tiles");
        if (!AssetDatabase.IsValidFolder(LayerFolder)) AssetDatabase.CreateFolder(TerrainFolder, "Layers");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");  // 目录不存在会静默保存失败
    }

    /// <summary>把世界坐标换成"本块高度图"的插值高度。注意：heights 只是本块（1000 m），必须传块原点。</summary>
    static float SampleWorldY(float[,] heights, float x, float z, float ox, float oz)
    {
        int res = heights.GetLength(0);
        float u = Mathf.Clamp((x - ox) / TileSize * (res - 1), 0f, res - 1.001f);
        float v = Mathf.Clamp((z - oz) / TileSize * (res - 1), 0f, res - 1.001f);
        int i0 = (int)u, j0 = (int)v;
        float fu = u - i0, fv = v - j0;
        float h00 = heights[j0, i0], h10 = heights[j0, i0 + 1];
        float h01 = heights[j0 + 1, i0], h11 = heights[j0 + 1, i0 + 1];
        float h = Mathf.Lerp(Mathf.Lerp(h00, h10, fu), Mathf.Lerp(h01, h11, fu), fv);
        return BaseY + h * HeightRange;
    }

    static float SlopeDeg(float[,] heights, float x, float z, float ox, float oz)
    {
        float e = 1.5f;
        float hx = SampleWorldY(heights, x + e, z, ox, oz) - SampleWorldY(heights, x - e, z, ox, oz);
        float hz = SampleWorldY(heights, x, z + e, ox, oz) - SampleWorldY(heights, x, z - e, ox, oz);
        float grad = Mathf.Sqrt(hx * hx + hz * hz) / (2f * e);
        return Mathf.Atan(grad) * Mathf.Rad2Deg;
    }

    static string TileStats(TerrainData data)
    {
        var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
        int res = data.heightmapResolution;
        float min = float.MaxValue, max = float.MinValue;
        int steep = 0, total = 0;
        float cell = TileSize / (res - 1);
        for (int j = 1; j < res - 1; j += 8)
            for (int i = 1; i < res - 1; i += 8)
            {
                float y = BaseY + heights[j, i] * HeightRange;
                if (y < min) min = y;
                if (y > max) max = y;
                float dx = (heights[j, i + 1] - heights[j, i - 1]) * HeightRange / (2f * cell);
                float dz = (heights[j + 1, i] - heights[j - 1, i]) * HeightRange / (2f * cell);
                if (Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg > 30f) steep++;
                total++;
            }
        return $"高度 {min:F1}~{max:F1} m | 陡坡 {100f * steep / Mathf.Max(1, total):F1}%";
    }

    // ================= 噪声与插值 =================
    /// <summary>
    /// 平滑钳制：把 v 压进 [lo, hi]，但边界处是**渐变**而不是硬折角。
    ///
    /// 为什么需要（2026-11-07 找到的"沟壑"真凶）：原来 PlainField / LowHillField /
    /// MountainField 用 Mathf.Clamp 硬压上下限，被压住的那一片边界是一条**等值线**，
    /// 法线在那里突然折断 —— 山体阴影/光照上就是一条细长的"沟"，
    /// 地图上十几条弧形细线全是它。
    /// </summary>
    static float SmoothClamp(float v, float lo, float hi, float soft)
    {
        if (v < lo) return lo - (lo - v) * soft / (soft + (lo - v));   // 低于下限：渐近贴回 lo
        if (v > hi) return hi + (v - hi) * soft / (soft + (v - hi));   // 高于上限：渐近贴回 hi
        return v;
    }
    static float SStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static float Fbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, z * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / Mathf.Max(1e-5f, norm);   // 0~1
    }

    static float RidgedFbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            float n = Mathf.PerlinNoise(x * freq, z * freq);
            float ridge = 1f - Mathf.Abs(n * 2f - 1f);   // 山脊：0..1 的尖
            sum += amp * ridge * ridge;
            norm += amp;
            amp *= 0.5f;
            freq *= 2.07f;
        }
        return sum / Mathf.Max(1e-5f, norm);
    }
}
