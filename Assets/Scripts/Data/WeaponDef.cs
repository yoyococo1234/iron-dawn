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
    [Tooltip("总备弹数（发）。0 = 无限。挂架上 8 枚导弹就是 8")]
    public int ammoCapacity = 0;

    [Header("挂载")]
    [Tooltip("这把武器属于哪个域（AI 只在本域内选目标）")]
    public Domain mountedDomain = Domain.Land;

    [Header("甲板/车顶机枪")]
    [Tooltip("独立挂点的机枪：不吃炮塔的俯仰限制，可以抬到 80° 以上打飞机；走瞬时命中（连射不刷 GameObject）")]
    public bool deckMounted = false;
    [Tooltip("独立挂点的最大仰角（度）")]
    public float deckMaxElevation = 85f;
    [Tooltip("独立挂点的最大俯角（度，负值）")]
    public float deckMaxDepression = -12f;
    [Tooltip("独立挂点的抬枪速度（度/秒）")]
    public float deckPitchSpeed = 140f;

    [Header("鱼雷")]
    [Tooltip("鱼雷：水下航行（定深直航），只对水面目标有效，速度远低于炮弹")]
    public bool isTorpedo = false;
    [Tooltip("鱼雷航行深度（米，水面以下）")]
    public float torpedoDepth = 1.6f;

    public string Describe()
        => $"{displayName}：伤害{baseDamage} 装填{reloadTime}s 射程{range}m，{ammoTypes?.Length ?? 0} 种弹";
}
