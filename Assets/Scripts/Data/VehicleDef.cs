using UnityEngine;

/// <summary>
/// 载具定义 = 一台"车"。决定血量、速度、装甲、能去哪、挂什么武器、卖多少钱。
/// 注意：装甲字段是"受击倍率"，数字越小越硬（0.6 = 只受 60% 伤害）。
/// </summary>
[CreateAssetMenu(fileName = "Vehicle_", menuName = "铁皮黎明/载具定义", order = 3)]
public class VehicleDef : ScriptableObject
{
    [Header("标识")]
    public string id = "land_main";
    public string displayName = "主战坦克";
    public Domain domain = Domain.Land;

    [Header("经济")]
    [Tooltip("购买价（换车时按 1.15^换车次数 涨价）")]
    public int price = 200;
    [Tooltip("复活基础费（实际费用 = 该值 × 1.22^已复活次数）")]
    public int respawnBaseCost = 80;

    [Header("生存")]
    public float maxHp = 1200f;
    public ArmorMode armorMode = ArmorMode.Directional;

    [Tooltip("正面受击倍率（Directional 模式用）")]
    public float armorFront = 0.70f;
    [Tooltip("侧面受击倍率")]
    public float armorSide = 0.90f;
    [Tooltip("背面受击倍率")]
    public float armorRear = 1.10f;

    [Tooltip("统一受击倍率（Uniform 模式用，飞机用这个）")]
    public float uniformMultiplier = 1.0f;

    [Header("机动")]
    public float speed = 12f;
    public float turnSpeed = 60f;
    [Tooltip("炮塔转速（度/秒），0 = 无炮塔")]
    public float turretTurnSpeed = 80f;

    [Header("物理（决定能不能爬坡、能不能涉水）")]
    [Tooltip("引擎功率 kW。功率÷质量 决定爬坡能力")]
    public float enginePower = 750f;
    [Tooltip("质量（吨）")]
    public float mass = 45f;
    [Tooltip("硬地阻力系数")]
    public float resistanceHard = 0.9f;
    [Tooltip("软地（沙/泥）阻力系数")]
    public float resistanceSoft = 1.8f;
    [Tooltip("涉水深度（米）。超过就沉；船用吃水深度代替")]
    public float wadeDepth = 1.2f;

    [Header("通行")]
    public TerrainType terrainAllowed = TerrainType.Land | TerrainType.ShallowWater;

    [Header("武器挂点")]
    public WeaponDef[] weapons;

    [Header("外观模型（留空就用方块占位）")]
    [Tooltip("精细模型 Prefab。留空时：会先按 Assets/Models/Vehicles/{id}.prefab / .fbx 自动找，再退化成方块")]
    public GameObject modelPrefab;
    [Tooltip("模型用的材质（带 baseColor 贴图）。运行时动态生成载具要用它")]
    public Material modelMaterial;
    [Tooltip("炮塔模型（Lux3D part_split 拆出来的）。有它炮塔就能真的转")]
    public GameObject modelTurretPrefab;
    [Tooltip("炮塔转轴在车体局部坐标下的位置（= 炮塔本体中心，绕它转才不甩）")]
    public Vector3 modelTurretPivot;
    [Tooltip("炮塔网格自身原点在车体局部坐标下的位置（= Blender 拆件时的切割面中心）。改转轴时用它把炮塔摆回原位")]
    public Vector3 modelTurretMeshOrigin;
    [Tooltip("要不要自动给车体上被炮塔切开的洞补一块甲板（按车体网格实测洞口）")]
    public bool autoTurretWellPatch = true;
    [Tooltip("炮塔节点名（模型里的子物体名），炮塔要能转就靠它")]
    public string turretBoneName = "Turret";
    [Tooltip("炮口节点名（开火时子弹从这里出）")]
    public string muzzleBoneName = "Muzzle";
    [Tooltip("模型朝向修正：模型车头不是 +Z 时，用这个角度转正")]
    public float modelYawOffset = 0f;
    [Tooltip("模型缩放修正（1 = 原尺寸）")]
    public float modelScale = 1f;

    [Header("说明")]
    [TextArea(2, 4)]
    public string description = "";

    /// <summary>按受击方向取装甲倍率。angleDeg = 0 表示正对，180 表示背对。</summary>
    public float GetArmorMultiplier(float angleDeg)
    {
        if (armorMode == ArmorMode.Uniform) return uniformMultiplier;

        float a = Mathf.Abs(Mathf.Repeat(angleDeg + 180f, 360f) - 180f);
        if (a <= 45f) return armorFront;
        if (a <= 135f) return armorSide;
        return armorRear;
    }

    public string Describe()
        => $"{displayName}（{domain}）血量{maxHp} 价{price} 复活{respawnBaseCost} 武器{weapons?.Length ?? 0}把";
}
