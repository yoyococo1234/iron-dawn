using UnityEngine;

/// <summary>
/// 战场区域（交战区 / 资源点）。占着持续产金，也是 AI 巡逻的目标。
/// </summary>
[CreateAssetMenu(fileName = "Zone_", menuName = "铁皮黎明/战场区域", order = 5)]
public class ZoneDef : ScriptableObject
{
    [Header("标识")]
    public string id = "zone_urban";
    public string displayName = "城区巷战";

    [Header("归属")]
    public Domain domain = Domain.Land;
    public TerrainType terrain = TerrainType.Land;

    [Header("范围（世界坐标）")]
    [Tooltip("区域矩形：x = 左边界，y = 下边界(z)，width/height = 尺寸")]
    public Rect bounds = new Rect(-26f, -26f, 52f, 40f);

    [Header("占领")]
    public bool captureable = true;
    [Tooltip("占着时每 captureInterval 秒产出多少金币")]
    public int incomePerTick = 15;

    [Header("金币箱刷新点（本区域内）")]
    public Vector2[] coinBoxPoints;

    public bool Contains(Vector3 worldPos)
        => bounds.Contains(new Vector2(worldPos.x, worldPos.z));
}
