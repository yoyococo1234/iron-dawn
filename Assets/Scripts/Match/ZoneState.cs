using UnityEngine;

/// <summary>
/// 一个资源点的运行时状态：谁占着、正在被谁占、进度多少。
/// 数据来自 ZoneDef（静态），这个类只存会变的东西。
/// </summary>
public class ZoneState
{
    public ZoneDef def;

    /// <summary>当前归属：-1 = 中立，0 = 我方，1 = 敌方。</summary>
    public int owner = -1;

    /// <summary>正在推进占领的队伍（-1 = 没人在占）。</summary>
    public int progressTeam = -1;

    /// <summary>占领进度 0~1。</summary>
    public float progress;

    public string DisplayName => def != null ? def.displayName : "?";
    public string OwnerText => owner < 0 ? "中立" : (owner == 0 ? "我方" : "敌方");

    public bool Contains(Vector3 worldPos) => def != null && def.Contains(worldPos);

    /// <summary>
    /// 这个点能不能被这台载具占。
    /// 2026-11-06 用户要求：**任何载具都能占**（飞机在天上飞过据点也算数），
    /// 不再按 domain 过滤 —— 只要 XZ 在区域矩形内就行（见 ZoneDef.Contains）。
    /// </summary>
    public bool CanBeCapturedBy(VehicleCombat c)
    {
        if (c == null || c.Def == null || def == null) return false;
        return def.captureable;
    }

    public Color OwnerColor
    {
        get
        {
            if (owner == 0) return new Color(0.35f, 0.75f, 1f);      // 我方：蓝
            if (owner == 1) return new Color(1f, 0.42f, 0.35f);      // 敌方：红
            return new Color(0.75f, 0.75f, 0.75f);                   // 中立：灰
        }
    }
}
