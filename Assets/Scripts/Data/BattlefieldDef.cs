using UnityEngine;

/// <summary>
/// 战场总定义：两条出兵线 + 区域列表 + 金币箱刷新点。
/// 没有"基地"——胜负靠歼灭，兵从出兵线进入战场。
/// </summary>
[CreateAssetMenu(fileName = "Battlefield_", menuName = "铁皮黎明/战场定义", order = 7)]
public class BattlefieldDef : ScriptableObject
{
    [Header("尺寸")]
    public float arenaSize = 120f;

    [Header("出兵线")]
    [Tooltip("我方出兵线：x 固定，z 从 zMin 到 zMax")]
    public float allySpawnLineX = -56f;
    [Tooltip("敌方出兵线")]
    public float enemySpawnLineX = 56f;
    public float spawnLineZMin = -58f;
    public float spawnLineZMax = 58f;

    [Header("区域")]
    public ZoneDef[] zones;

    [Header("金币箱全局刷新点（世界坐标 x,z）")]
    public Vector2[] coinBoxPoints;

    [Header("海平面")]
    public float waterLevel = 0.05f;

    /// <summary>在出兵线上随机取一个出生点。</summary>
    public Vector3 GetSpawnPoint(bool ally, System.Random rng = null)
    {
        float x = ally ? allySpawnLineX : enemySpawnLineX;
        float t = rng != null ? (float)rng.NextDouble() : Random.value;
        float z = Mathf.Lerp(spawnLineZMin, spawnLineZMax, t);
        return new Vector3(x, 0f, z);
    }
}
