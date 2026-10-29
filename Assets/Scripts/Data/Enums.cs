using UnityEngine;

/// <summary>载具属于哪个域。</summary>
public enum Domain
{
    Land = 0,   // 陆
    Sea = 1,    // 海
    Air = 2,    // 空
}

/// <summary>载具能通行的地形（可组合）。</summary>
[System.Flags]
public enum TerrainType
{
    None = 0,
    Land = 1 << 0,          // 陆地
    ShallowWater = 1 << 1,  // 浅水（坦克能涉水）
    DeepWater = 1 << 2,     // 深水（只有船）
    Air = 1 << 3,           // 空域
}

/// <summary>
/// 防御模型：
/// Directional = 分正/侧/背（坦克、军舰，现实里确实有装甲带）
/// Uniform     = 不分方向（飞机是薄壳，靠速度和机动活命，没有"背面装甲"这回事）
/// </summary>
public enum ArmorMode
{
    Directional = 0,
    Uniform = 1,
}
