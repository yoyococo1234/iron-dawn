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

    [Tooltip("发动机循环音片段名（Assets/Resources/Audio/<名字>.wav）。" +
             "留空 = 按域自动选：陆/海用 engine_loop，空用 engine_loop_jet。" +
             "螺旋桨飞机填 engine_loop_prop")]
    public string engineLoopName = "";

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

    [Header("装甲厚度（mm）—— 穿透判定用")]
    [Tooltip("正面装甲厚度（mm）。摆角度会让有效厚度变厚")]
    public float armorThicknessFront = 100f;
    [Tooltip("侧面装甲厚度（mm）")]
    public float armorThicknessSide = 60f;
    [Tooltip("后面装甲厚度（mm）")]
    public float armorThicknessRear = 40f;

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
    [Tooltip("要不要用拆件炮塔。关掉 = 用整体模型（外观和原模型一模一样，但炮塔不会转）")]
    public bool useSplitTurret = false;
    [Tooltip("履带模型（可选）。给了就会按车速滚动 UV，看起来履带在转")]
    public GameObject modelTracksPrefab;
    [Tooltip("原模型履带（只用来量车体履带槽尺寸，程序生成轮子+履带时按它对齐）")]
    public GameObject modelTrackWellRef;
    [Tooltip("炮塔单独生成时，它自己的材质（贴图和车体不是同一张）")]
    public Material modelTurretMaterial;
    [Tooltip("履带总成自己的材质（Lux3D 履带模型的 baseColor 贴图）。留空 = 用 modelMaterial")]
    public Material modelTracksMaterial;
    [Tooltip("履带总成的目标高度（米）。0 = 自动按车体高度的 88% 算。" +
             "用来把履带塞进侧裙后面，避免「履带比车体还高」")]
    public float tracksTargetHeight = 0f;

    [Header("程序化履带（Lux3D 履带网格烂掉时用）")]
    [Tooltip("勾上 = 不加载 Lux3D 那套履带网格，直接按下面的尺寸生成履带（环带+主动轮+诱导轮+负重轮+托带轮）")]
    public bool proceduralTracks = false;
    [Tooltip("履带中心线到车体中线的横向距离（米）")]
    public float trackHalfSpan = 0f;
    [Tooltip("履带板宽度（米）")]
    public float trackBandWidth = 0f;
    [Tooltip("履带环外廓总长（车长方向，米）")]
    public float trackLoopLength = 0f;
    [Tooltip("履带环外廓总高（米）")]
    public float trackLoopHeight = 0f;
    [Tooltip("负重轮半径（米）")]
    public float trackRoadWheelR = 0f;
    [Tooltip("每侧负重轮个数")]
    public int trackRoadWheelCount = 0;
    [Tooltip("程序化履带时整车抬高多少（米）。" +
             "车体原点只离地 3 cm，履带又长 6.8 m，稍微有点坡度履带两端就插进地里 —— 抬一点留余量")]
    public float trackGroundLift = 0.10f;
    [Tooltip("程序化履带整体下沉多少（米）。" +
             "车体模型剥掉自带履带以后，车体最低点（车底）比原来履带底面高一截，" +
             "而程序化履带是按「底面 = 车体原点」生成的，会整体浮在车底那一层。" +
             "把车底到履带底面的高度差填在这里，履带才落得回地面（主战 0，重装 0.948）")]
    public float trackDrop = 0f;
    [Tooltip("车体模型里已经烘进去一块炮塔井补板（用 Blender 焊进 hull 网格了）。" +
             "勾上就不再在运行时生成方形补板，免得两层补丁打架")]
    public bool bakedTurretWell = false;
    [Tooltip("炮管模型（从炮塔上切出来的，单独做俯仰）。留空 = 炮塔整体不会抬炮")]
    public GameObject modelBarrelPrefab;
    [Tooltip("炮管转轴在炮塔局部坐标下的位置（炮盾中心）")]
    public Vector3 modelBarrelPivot;
    [Tooltip("炮塔转轴在车体局部坐标下的位置（= 炮塔本体中心，绕它转才不甩）")]
    public Vector3 modelTurretPivot;
    [Tooltip("炮塔网格自身原点在车体局部坐标下的位置（= Blender 拆件时的切割面中心）。改转轴时用它把炮塔摆回原位")]
    public Vector3 modelTurretMeshOrigin;
    [Tooltip("要不要自动给车体上被炮塔切开的洞补一块甲板（按车体网格实测洞口）")]
    public bool autoTurretWellPatch = true;
    [Tooltip("拆件炮塔时自动补「炮塔封底盘 + 甲板炮座板」。炮塔是从船体/车体里挖出来的，" +
             "底部本来没有面，不补的话一转就看见内部、甲板上还会露切口")]
    public bool autoTurretSkirt = true;
    [Tooltip("甲板/车顶机枪挂点（车体局部坐标）。零向量 = 自动放在炮塔顶后部")]
    public Vector3 modelDeckGunPivot = Vector3.zero;
    [Tooltip("甲板/车顶机枪整体缩放。炮艇用 1.0（12.7mm 高射机枪原尺寸），" +
             "坦克炮塔小，用 0.65~0.75 才不会显得像顶着一门比炮塔还宽的机枪")]
    public float deckGunScale = 1f;

    [Header("摇摆炮塔 / 整座俯仰（真实机构，见 AMX-13 的 FL-10）")]
    [Tooltip("开启后炮塔分成两块：下部座圈只转方位，**上部塔体整体绕耳轴俯仰**，" +
             "炮管刚性固定在上部塔体里（不再单独俯仰）。像 AMX-13 / AMX-50 / 潘哈德 EBR")]
    public bool oscillatingTurret = false;
    [Tooltip("固定下座圈的网格（拆件）。开启摇摆炮塔时必填：它不俯仰，只跟着炮塔偏航")]
    public GameObject modelTurretLowerPrefab;
    [Tooltip("耳轴位置（炮塔本地坐标，Y 上 / Z 前）。上部塔体绕这个点俯仰。" +
             "放在塔体中高处：底面边缘主要做前后平移，不会从座圈上撬开")]
    public Vector3 modelElevatingPivot = Vector3.zero;
    [Tooltip("塔体跟随炮管俯仰的比例：1 = 整块塔体 1:1 跟随（真摇摆炮塔），" +
             "0.25 左右 = 塔体微动（底面扫过小，不用底座也不用大缝）")]
    [Range(0f, 1f)] public float elevatingFollow = 1f;
    [Tooltip("炮塔俯仰上限（度，正=抬）。0 = 用 VehiclePhysics 的默认值 20")]
    public float maxElevationOverride = 0f;
    [Tooltip("炮塔俯仰下限（度，负=压）。0 = 用 VehiclePhysics 的默认值 -8")]
    public float maxDepressionOverride = 0f;

    [Tooltip("炮塔左右可转的角度上限（度，相对车头方向；180 = 不限，能转一整圈）。" +
             "炮艇主炮在甲板中央、正后方就是船桥（z -1.0~+1.5，高 3.93 m），" +
             "3.5 m 长的炮管转到后方会插进驾驶室 —— 所以限到 150（±150° 之外是死角）")]
    public float turretYawLimit = 180f;
    [Tooltip("炮塔节点名（模型里的子物体名），炮塔要能转就靠它")]
    public string turretBoneName = "Turret";
    [Tooltip("炮口节点名（开火时子弹从这里出）")]
    public string muzzleBoneName = "Muzzle";
    [Tooltip("模型朝向修正：模型车头不是 +Z 时，用这个角度转正")]
    public float modelYawOffset = 0f;
    [Tooltip("模型缩放修正（1 = 原尺寸）")]
    public float modelScale = 1f;

    [Header("机头进气口涡轮风扇（飞机用）")]
    [Tooltip("每个风扇在**车体局部坐标**下的位置（米；Y 上、Z 前）。留空 = 不加风扇。" +
             "Lux3D 出的飞机是单网格、进气口是个窟窿，叶片只能程序生成")]
    public Vector3[] intakeFans;
    [Tooltip("风扇半径（米）")]
    public float intakeFanRadius = 0.5f;

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
