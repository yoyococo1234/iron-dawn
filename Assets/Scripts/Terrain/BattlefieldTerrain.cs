using UnityEngine;

/// <summary>
/// 分块战场地形的统一查询入口（2 km 地图由 2×2 块 Terrain 拼成）。
///
/// 载具适配（交接文档第 6 节 Step 7）要用这里，不要直接用 Terrain.activeTerrain：
///   贴地：float y = BattlefieldTerrain.SampleHeight(worldPos);
///   坡度：float deg = BattlefieldTerrain.SampleSteepness(worldPos);
///   碰撞：TerrainCollider 自动生成，不用管。
///
/// 世界坐标：x、z ∈ [-1000, 1000]；高度 y ∈ [-100, 300]；海平面 y = -2.667（2026-11-12 抬高 4 m）。
/// </summary>
public static class BattlefieldTerrain
{
    public const float MapSize = 2000f;
    public const float TileSize = 1000f;
    public const int Tiles = 2;
    public const float BaseY = -100f;            // 地形最低点
    public const float HeightRange = 400f;       // 高度范围
    // 2026-11-12 用户要求："海平面有点低，先稍微升高一点，让船的舰炮可以打击（岸上目标）"。
    // 原来是 -6.6667（地形生成时的设计值 -0.4×Scale）。抬到 -2.6667 = **+4 m**：
    // 实测水域占比只从 35.4% 涨到 35.6%（海岸线很陡，不会淹掉地图），但船整体高了 4 m，
    // 舰炮能越过滩头看到/打到岸上的目标。要再调就改这一个常量 + 场景里 Water_Sea 的 Y。
    public const float WaterLevel = -2.6667f;    // 海平面（2026-11-12 由 -6.6667 抬高 4 m）
    public const float Scale = MapSize / 120f;   // 设计坐标 → 世界坐标（16.667）

    static Terrain[] cache;

    /// <summary>清缓存：场景切换或地形重建后调用。</summary>
    public static void Invalidate() => cache = null;

    static Terrain[] Tiles_()
    {
        if (cache == null || cache.Length == 0 || cache[0] == null)
            cache = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        return cache;
    }

    /// <summary>取某个世界坐标所在的地形块；越界返回 null。</summary>
    public static Terrain TileAt(float x, float z)
    {
        foreach (var t in Tiles_())
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position;
            Vector3 s = t.terrainData.size;
            if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z) return t;
        }
        return null;
    }

    /// <summary>世界坐标处的地面高度（世界 y）。越界时返回海平面，方便直接判断落水。</summary>
    public static float SampleHeight(Vector3 worldPos)
    {
        var t = TileAt(worldPos.x, worldPos.z);
        if (t == null) return WaterLevel;
        return t.SampleHeight(worldPos) + t.transform.position.y;
    }

    public static float SampleHeight(float x, float z) => SampleHeight(new Vector3(x, 0f, z));

    /// <summary>世界坐标处的坡度（度）。用 1.5 m 步长采样，避免 0.24 m 网格的毛刺。</summary>
    public static float SampleSteepness(Vector3 worldPos)
    {
        var t = TileAt(worldPos.x, worldPos.z);
        if (t == null) return 0f;
        Vector3 local = worldPos - t.transform.position;
        Vector3 size = t.terrainData.size;
        float nx = Mathf.Clamp01(local.x / size.x);
        float nz = Mathf.Clamp01(local.z / size.z);
        return t.terrainData.GetSteepness(nx, nz);
    }

    public static float SampleSteepness(float x, float z) => SampleSteepness(new Vector3(x, 0f, z));

    /// <summary>水深（米）：海平面 − 地面高度。负数表示地面在海平面之上（干地）。</summary>
    public static float WaterDepth(float x, float z) => WaterLevel - SampleHeight(x, z);

    /// <summary>是否在地图范围内（含一点余量）。</summary>
    public static bool InBounds(float x, float z, float margin = 0f)
    {
        float h = MapSize * 0.5f + margin;
        return x >= -h && x <= h && z >= -h && z <= h;
    }
}
