using UnityEngine;

/// <summary>
/// 弹药定义。核心是四个"对某类目标的伤害倍率"——这就是克制矩阵。
/// 倍率 × 武器基础伤害 × 目标装甲倍率 = 实际伤害。
/// </summary>
[CreateAssetMenu(fileName = "Ammo_", menuName = "铁皮黎明/弹药定义", order = 1)]
public class AmmoDef : ScriptableObject
{
    [Header("标识")]
    public string id = "ap";
    public string displayName = "穿甲弹";

    [Header("对各类目标的伤害倍率")]
    [Tooltip("对装甲目标（坦克、军舰）")]
    public float vsArmor = 1.2f;
    [Tooltip("对软目标（无装甲单位）")]
    public float vsSoft = 0.4f;
    [Tooltip("对空中目标")]
    public float vsAir = 0.1f;
    [Tooltip("对海上目标")]
    public float vsSea = 0.9f;

    [Header("经济")]
    [Tooltip("每发消耗金币")]
    public int pricePerShot = 5;

    [Header("扩展（暂时不用）")]
    [Tooltip("非伤害弹药（烟幕、干扰弹等），V2 再用")]
    public bool isSupport = false;

    [Header("穿深（mm）—— 0 表示不参与穿透判定（炸弹/鱼雷/防空弹）")]
    [Tooltip("穿甲能力（mm）。和装甲有效厚度比：够就击穿，不够就是未击穿")]
    public float penetration = 0f;
    [Tooltip("每 100 m 穿深衰减（mm）。动能弹用；化学能弹设 0")]
    public float penFalloffPer100m = 0f;
    [Tooltip("化学能弹（HEAT / 反坦克导弹）：不吃距离衰减")]
    public bool isChemical = false;

    public string Describe()
        => $"{displayName}：装甲×{vsArmor} 软×{vsSoft} 空×{vsAir} 海×{vsSea}，{pricePerShot}金/发";
}
