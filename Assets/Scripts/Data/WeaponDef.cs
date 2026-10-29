using UnityEngine;

/// <summary>
/// 武器定义 = 一把"枪"。装在载具的挂点上，决定射程、装填、基础伤害，以及能装哪些弹药。
/// </summary>
[CreateAssetMenu(fileName = "Weapon_", menuName = "铁皮黎明/武器定义", order = 2)]
public class WeaponDef : ScriptableObject
{
    [Header("标识")]
    public string id = "gun_120";
    public string displayName = "120mm 主炮";

    [Header("射击参数")]
    [Tooltip("基础伤害（还要乘弹药倍率和目标装甲倍率）")]
    public float baseDamage = 300f;
    [Tooltip("装填时间（秒）")]
    public float reloadTime = 4.5f;
    [Tooltip("有效射程（米）")]
    public float range = 200f;
    [Tooltip("弹速（米/秒），0 = 瞬时命中")]
    public float projectileSpeed = 150f;

    [Header("弹药")]
    [Tooltip("这把武器能装哪些弹（玩家可以切换）")]
    public AmmoDef[] ammoTypes;

    [Header("挂载")]
    [Tooltip("这把武器属于哪个域（AI 只在本域内选目标）")]
    public Domain mountedDomain = Domain.Land;

    public string Describe()
        => $"{displayName}：伤害{baseDamage} 装填{reloadTime}s 射程{range}m，{ammoTypes?.Length ?? 0} 种弹";
}
