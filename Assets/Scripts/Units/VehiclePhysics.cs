using UnityEngine;

/// <summary>
/// 载具物理：按 def.domain 分三种行为
///   陆 Land — 贴地、随地形倾斜、按"功率/重量 vs 坡度"判断能不能爬、涉水减速、深水沉没
///   海 Sea  — 浮在水面、只在水里走、水太浅会搁浅（可倒车脱离）
///   空 Air  — 固定高度飞行、无视地形、转向时侧倾
/// 参数全部来自 VehicleDef，可实时改。
/// </summary>
public class VehiclePhysics : MonoBehaviour
{
    [Header("数据")]
    public VehicleDef def;
    [Tooltip("旧测试场的地形源。留空 = 用正式地图的 Unity Terrain（BattlefieldTerrain 分块采样）")]
    public TerrainField terrain;

    [Header("物理（会被 def 覆盖）")]
    public float enginePower = 800f;   // kW
    public float mass = 45f;           // t
    public float wadeDepth = 1.2f;     // 陆：涉水上限；海：吃水深度
    public float resistanceHard = 0.9f;
    public float resistanceSoft = 1.8f;
    public float groundClearance = 0.55f;

    [Header("越障（履带/车轮能直接翻过的小台阶）")]
    [Tooltip("能翻上去的最大台阶高度（米）。取真实坦克的越障能力（约 0.9 m）：" +
             "建筑物地基裙边平地上露 0.22 m、坡地上能露到 0.5~1.5 m，路缘/碎石也在这个量级。" +
             "以前这类矮台阶会把车**直接顶停**（用户 2026-11-12 报「几乎所有的建筑物边缘都这样」）。" +
             "楼体墙面（几米高）不在这个范围内，照样挡得住。")]
    public float stepHeight = 0.9f;

    [Header("手感")]
    public float acceleration = 14f;
    public float brakeDecel = 22f;
    public float tiltSpeed = 6f;

    [Header("空中")]
    [Tooltip("AI 巡逻/保持的飞行高度（离地面）")]
    public float flightAltitude = 220f;
    [Tooltip("转向时的侧倾角度（AI 用）")]
    public float bankAngle = 30f;

    [Header("飞行手感（战争雷霆式：鼠标控姿态 + W/S 油门 + A/D 横滚）")]
    [Tooltip("鼠标灵敏度（虚拟摇杆）")]
    public float airMouseSensitivity = 0.5f;
    [Tooltip("鼠标松手后虚拟摇杆回中速度")]
    public float airRecenterSpeed = 0.9f;
    [Tooltip("最大俯仰角速度（度/秒）")]
    public float airPitchRate = 55f;
    [Tooltip("最大偏航角速度（度/秒）")]
    public float airYawRate = 32f;
    [Tooltip("横滚角速度（度/秒）")]
    public float airRollRate = 120f;
    [Tooltip("靠横滚带出的转弯强度（度/秒，满横滚时）")]
    public float airBankTurnRate = 42f;
    [Tooltip("姿态跟随输入的速度。太小的话模型会明显滞后于飞行方向，看着像横着飘")]
    public float airResponse = 22f;
    [Tooltip("油门变化速度（每秒）")]
    public float airThrottleRate = 0.7f;
    [Tooltip("撞地高度（低于地面这么多米就算坠毁）")]
    public float airCrashHeight = 3f;
    [Tooltip("最高飞行高度")]
    public float airCeiling = 1500f;

    // ---- 飞行姿态（正 = 抬头 / 右横滚）----
    [System.NonSerialized] public float airPitch;
    [System.NonSerialized] public float airYaw;
    [System.NonSerialized] public float airRoll;
    [System.NonSerialized] public float airThrottle = 0.65f;
    float _airAimX, _airAimY;

    /// <summary>飞机准星的屏幕偏移（-1~1），HUD 和瞄准都用它。</summary>
    public Vector2 AirAim => new Vector2(_airAimX, _airAimY);

    [Header("测试用：自动驾驶（不做输入也能跑）")]
    public bool autoDrive = false;
    public float autoThrottle = 1f;

    [Header("AI 驾驶（由 VehicleAI 每帧写入）")]
    [Tooltip("开了就用 aiThrottle/aiSteer，不再读键盘")]
    public bool aiControlled;
    [Range(-1f, 1f)] public float aiThrottle;
    [Range(-1f, 1f)] public float aiSteer;

    /// <summary>油门输入：AI 优先，其次自动驾驶，最后键盘（2026-11-11：键盘改成读 Bindings，支持 WASD 改键）。</summary>
    float ThrottleInput => aiControlled ? aiThrottle : (autoDrive ? autoThrottle : Bindings.AxisV);
    /// <summary>转向输入：AI 优先，其次键盘。</summary>
    float SteerInput => aiControlled ? aiSteer : Bindings.AxisH;

    [Tooltip("进水后多少秒整车报废（会正常爆炸并计入阵亡）")]
    public float drownSeconds = 5f;

    [Header("模型挂点（由 VehicleModelBinder 填入）")]
    [Tooltip("炮塔节点，炮塔转动靠它")]
    public Transform turretBone;
    [Tooltip("炮口节点，开火时子弹从这里出")]
    public Transform muzzleBone;

    [Tooltip("炮塔节点是模型里真实的部件吗？Lux3D 整体网格是逻辑炮塔（不显示旋转）")]
    [System.NonSerialized] public bool turretIsVisual;
    [Tooltip("车体半长（米），逻辑炮塔时用来把出膛点固定在车头")]
    [System.NonSerialized] public float hullHalfLength = 3f;

    [System.NonSerialized] public float turretYaw;

    [Header("炮管俯仰（有独立炮管时生效）")]
    [Tooltip("最大仰角（度）")]
    public float maxElevation = 20f;
    [Tooltip("最大俯角（度，负值）")]
    public float maxDepression = -8f;
    [Tooltip("抬/压炮速度（度/秒）")]
    public float pitchSpeed = 25f;
    [System.NonSerialized] public Transform barrelBone;
    /// <summary>摇摆炮塔：整块上部塔体的俯仰节点（塔体按 elevatingFollow 比例跟随炮管角度）。</summary>
    [System.NonSerialized] public Transform elevatingBone;
    [Tooltip("塔体跟随炮管俯仰的比例（0 = 塔体不动，1 = 整块塔体 1:1 跟随）。" +
             "1:1 时塔体底面扫过量很大，需要大间隙或底座；0.25 左右最自然")]
    public float elevatingFollow = 1f;
    [System.NonSerialized] public float barrelPitch;

    [Header("甲板/车顶机枪（独立俯仰，能打飞机）")]
    [Tooltip("机枪挂点（由 VehicleModelBinder 生成；指摇架，绕耳轴俯仰）")]
    [System.NonSerialized] public Transform mgBone;
    [Tooltip("机枪回转座（绕 Y 偏航，独立于炮塔 —— 枪架自己能转，不用拖着炮塔一起甩）")]
    [System.NonSerialized] public Transform mgYawBone;
    [Tooltip("机枪相对炮塔的偏航角（度）")]
    [System.NonSerialized] public float mgYaw;
    [Tooltip("机枪回转限位（±度）。枪架转不到炮塔正后方 —— 免得对着自己船体扫")]
    public float mgYawLimit = 120f;
    [Tooltip("机枪枪口节点（出膛点，由 VehicleModelBinder 生成）")]
    [System.NonSerialized] public Transform mgMuzzle;

    [Tooltip("鱼雷发射管管口（左舷 / 右舷，由 VehicleModelBinder 生成）")]
    [System.NonSerialized] public Transform torpedoTubeL;
    [System.NonSerialized] public Transform torpedoTubeR;
    /// <summary>这艘船有没有可见的鱼雷发射管。</summary>
    public bool HasTorpedoTubes => torpedoTubeL != null || torpedoTubeR != null;
    [Tooltip("机枪当前仰角（度，正 = 抬高）")]
    [System.NonSerialized] public float mgPitch;
    [Tooltip("机枪最大仰角（度）")]
    public float mgMaxElevation = 85f;
    [Tooltip("机枪最大俯角（度，负值）")]
    public float mgMaxDepression = -12f;
    [Tooltip("机枪抬/压速度（度/秒）")]
    public float mgPitchSpeed = 140f;
    /// <summary>模型底部已经贴到车体原点（精细模型会这样），离地间隙就用很小的值。</summary>
    [System.NonSerialized] public bool modelSitsOnGround;

    // ---- 运行时状态（给 HUD 看）----
    [System.NonSerialized] public float speed;
    /// <summary>
    /// 履带视觉用的速度：上不去坡 / 被挡住时，履带照样按油门空转（打滑），
    /// 而不是跟着 speed=0 一起停住（2026-11-06 用户反馈：上坡上不去时履带一动不动，不真实）。
    /// </summary>
    [System.NonSerialized] public float trackSpeed;
    /// <summary>重伤减速倍率（由 VehicleDamageFx 按血量设置：低于 25% 血时降到 0.6）。</summary>
    [System.NonSerialized] public float damageSpeedMul = 1f;
    /// <summary>部位损坏造成的减速（发动机被打坏 0.25、履带断 0.05）。</summary>
    [System.NonSerialized] public float moduleSpeedMul = 1f;
    /// <summary>炮塔转速倍率（炮塔环被打坏 ×0.25）。</summary>
    [System.NonSerialized] public float turretSpeedMul = 1f;
    /// <summary>两个减速倍率合起来用。</summary>
    public float SpeedMul => Mathf.Clamp01(damageSpeedMul * moduleSpeedMul);
    [System.NonSerialized] public float slopeDeg;
    [System.NonSerialized] public float waterDepth;
    [System.NonSerialized] public float maxClimbDeg;
    [System.NonSerialized] public float blockedTime;
    [System.NonSerialized] public string stateText = "正常";
    [System.NonSerialized] public bool alive = true;

    float _sinkTimer;

    public float PowerPerTon => enginePower / Mathf.Max(1f, mass);
    public float MaxClimbDeg => Mathf.Atan(PowerPerTon / 30f) * Mathf.Rad2Deg;

    /// <summary>当前速度向量（给 AI 算提前量用）。</summary>
    public Vector3 Velocity => transform.forward * speed;

    void Start() => ApplyDef();

    public void ApplyDef()
    {
        if (def == null) return;
        enginePower = def.enginePower;
        mass = def.mass;
        resistanceHard = def.resistanceHard;
        resistanceSoft = def.resistanceSoft;
        wadeDepth = def.wadeDepth;
        maxClimbDeg = MaxClimbDeg;
        // 精细模型的底部已经落在车体原点上了 → 离地间隙给 3 cm 就够；
        // 方块占位模型原点在中心，才需要 0.5 m 把它抬起来。
        groundClearance = modelSitsOnGround ? 0.03f : 0.5f;
        // 程序化履带：整车再抬一点。车体原点离地只有 3 cm，履带却长 6.8 m，
        // 稍微有点坡度履带两端就插进地面（2026-11-02 用户反馈）。
        // 注意必须放在这里而不是 VehicleFactory：VehiclePhysics.Start() 会再调一次 ApplyDef 把值覆盖掉。
        if (modelSitsOnGround && def != null && def.trackGroundLift > 0.001f)
            groundClearance = Mathf.Max(groundClearance, def.trackGroundLift);
    }

    void Update()
    {
        if (!alive || def == null) return;

        trackSpeed = speed;          // 默认履带速度 = 实际速度；陆战在 UpdateLand 里会按"打滑"改写

        switch (def.domain)
        {
            case Domain.Sea: UpdateSea(); break;
            case Domain.Air: UpdateAir(); break;
            default: UpdateLand(); break;
        }

        // ---------- 硬保证：任何载具都不允许低于地形 ----------
        // 海战最典型：船搁浅后 y 还锁在水面高度，而沙滩比水面高 1~2 m，
        // 结果整条船埋进沙面以下，只剩栏杆露出来（用户截图里那根"小尖尖"）。
        // 这里统一兜底，不管哪个域、哪段逻辑把车弄到地下，都在这一帧拽回来。
        if (def.domain != Domain.Air)
        {
            var p = transform.position;
            float floorY = GroundY(p.x, p.z) + (def.domain == Domain.Sea ? 0.35f : 0.05f);
            if (p.y < floorY)
                transform.position = new Vector3(p.x, floorY, p.z);
        }
    }

    // ==================== 陆 ====================

    void UpdateLand()
    {
        var pos = transform.position;
        slopeDeg = GroundSlope(pos.x, pos.z);
        waterDepth = WaterDepthAt(pos.x, pos.z);
        maxClimbDeg = MaxClimbDeg;

        // 进水：不再往地里沉，原地熄火，drownSeconds 秒后整车报废
        // （报废走正常击毁流程 —— 会爆炸、会触发 BattleEvents，玩家才会回到车库）
        if (waterDepth > wadeDepth)
        {
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * 2f * Time.deltaTime);
            _sinkTimer += Time.deltaTime;
            float left = Mathf.Max(0f, drownSeconds - _sinkTimer);
            stateText = $"进水 · {Mathf.CeilToInt(left)} 秒后报废";

            // 只表现"往下压一点"，绝不穿到地形下面
            float floorY = GroundY(pos.x, pos.z) + 0.2f;
            pos.y = Mathf.Lerp(pos.y, Mathf.Max(floorY, pos.y - 0.8f), 2f * Time.deltaTime);
            transform.position = pos;

            if (_sinkTimer >= drownSeconds) Drown();
            return;
        }
        _sinkTimer = 0f;

        float waterFactor = waterDepth > 0f ? 0.5f : 1f;
        bool tooSteep = slopeDeg > MaxClimbDeg;
        float slopeFactor = tooSteep ? 0f : Mathf.Lerp(1f, 0.35f, slopeDeg / Mathf.Max(1f, MaxClimbDeg));

        // 下坡：坡太陡但方向是往下的，允许走（现实里下坡当然能下去）。
        // 之前一律 slopeFactor = 0，上不去下也下不来，只能慢慢滑，很别扭。
        if (tooSteep)
        {
            var g = SlopeGradient(pos.x, pos.z);                 // 上坡方向（水平分量）
            Vector3 up = new Vector3(g.x, 0f, g.y);
            if (up.sqrMagnitude > 1e-6f)
            {
                up.Normalize();
                float downhillDot = Vector3.Dot(transform.forward, -up);
                if (ThrottleInput > 0.05f && downhillDot > 0.25f)
                {
                    tooSteep = false;
                    slopeFactor = 0.55f;                          // 下坡慢一点，但能走
                }
                else if (ThrottleInput < -0.05f && downhillDot < -0.25f)
                {
                    tooSteep = false;
                    slopeFactor = 0.55f;
                }
            }
        }

        if (tooSteep)
        {
            var grad = SlopeGradient(pos.x, pos.z);
            transform.position -= new Vector3(grad.x, 0f, grad.y).normalized * 1.5f * Time.deltaTime;
        }

        float throttle = ThrottleInput;
        float steer = SteerInput;

        transform.Rotate(0f, steer * def.turnSpeed * Time.deltaTime, 0f, Space.World);

        float target = throttle * def.speed * slopeFactor * waterFactor * SpeedMul;
        float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration : brakeDecel;
        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);

        // 撞墙判定：载具是 transform 移动的（不是刚体），不判就会直接穿过建筑
        bool blocked = false;
        Vector3 delta = transform.forward * speed * Time.deltaTime;
        if (delta.sqrMagnitude > 1e-8f && IsBlockedByObstacle(delta))
        {
            // ---------- 蹭墙滑行（2026-11-12）----------
            // 以前一撞就 speed=0：贴着楼开的时候稍微带一点角度就"直接停下"，很别扭。
            // 现在把位移投到**墙面切向**再试一次 —— 角度不对就沿墙蹭过去，实在没路才停。
            Vector3 n = _lastBlockNormal;
            n.y = 0f;
            bool slid = false;
            if (n.sqrMagnitude > 0.01f)
            {
                n.Normalize();
                Vector3 tangent = delta - n * Vector3.Dot(delta, n);
                if (tangent.sqrMagnitude > 1e-7f && !IsBlockedByObstacle(tangent))
                {
                    transform.position += tangent;
                    // 蹭墙掉速要按**秒**算，不能按帧（0.94/帧在 90 fps 下等于 0.4%/秒，一秒就停死）
                    speed *= Mathf.Max(0f, 1f - 1.5f * Time.deltaTime);
                    slid = true;
                }
            }
            if (!slid)
            {
                speed = 0f;
                blocked = true;
            }
        }
        else
        {
            transform.position += delta;
        }
        blockedTime = blocked ? blockedTime + Time.deltaTime : 0f;

        // 履带视觉：卡住 / 坡太陡时按油门空转，别停着
        trackSpeed = (blocked || tooSteep) ? throttle * def.speed * 0.55f : speed;
        ClampToBounds();

        // 贴地 + 倾斜
        pos = transform.position;
        float groundY = DriveSurfaceY(pos);
        // 往**上**翻台阶时慢一点（7/s）：一步弹上去看着像瞬移；往下掉/平地走还是 12/s
        float lerpK = groundY > pos.y - groundClearance + 0.05f ? 7f : 12f;
        pos.y = Mathf.Lerp(pos.y, groundY + groundClearance, lerpK * Time.deltaTime);
        transform.position = pos;

        float yaw = transform.eulerAngles.y;
        var normal = GroundNormal(pos.x, pos.z);
        var targetRot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, tiltSpeed * Time.deltaTime);

        stateText = tooSteep ? "打滑（坡太陡）"
                  : blocked ? "撞到障碍"
                  : waterDepth > 0f ? "涉水中"
                  : "正常";
    }

    // ==================== 海 ====================

    void UpdateSea()
    {
        var pos = transform.position;
        waterDepth = WaterDepthAt(pos.x, pos.z);
        slopeDeg = 0f;
        maxClimbDeg = 0f;

        float throttle = ThrottleInput;
        float steer = SteerInput;

        // ---------- 船头前探（2026-11-07）----------
        // 用户要的是"快碰到浅滩就先自己拐出去"，而不是撞进沙子里再退出来。
        // 所以在船头前方 16 m 探一次水深：不够就**提前转向深水**（不刹车），船头一偏就绕开了。
        if (waterDepth >= wadeDepth)
        {
            Vector3 ahead = pos + transform.forward * 16f;
            float aheadDepth = WaterDepthAt(ahead.x, ahead.z);
            if (aheadDepth < wadeDepth + 0.25f)
            {
                Vector3 deeper = DeeperDirection(ahead, 9f, out float deepDepth);
                if (deeper.sqrMagnitude > 0.01f && deepDepth > aheadDepth)
                {
                    float wantYaw = Mathf.Atan2(deeper.x, deeper.z) * Mathf.Rad2Deg;
                    float curYaw = transform.eulerAngles.y;
                    float newYaw = Mathf.MoveTowardsAngle(curYaw, wantYaw, 55f * Time.deltaTime);
                    transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
                    stateText = "避让浅滩";
                    // 只压一点速度，保证"拐出去"而不是"怼进去"
                    speed = Mathf.Min(speed, def.speed * 0.6f);
                }
            }
        }

        // 吃水不足 → 搁浅
        if (waterDepth < wadeDepth)
        {
            stateText = "搁浅";
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * Time.deltaTime);

            // ---------- 沿"更深"的方向滑回水里 ----------
            // 抄的是 World of Warships 12.3 的做法：船碰到浅水不是卡死，而是"滑"。
            // 采样周围 8 个方向的水深，朝最深的那边走；搁浅越深推得越快。
            Vector3 slideDir = DeeperDirection(pos, 7f, out float slideDepth);
            if (slideDepth > waterDepth + 0.05f)
            {
                float howStuck = Mathf.Clamp01((wadeDepth - waterDepth) / Mathf.Max(0.25f, wadeDepth));
                float push = Mathf.Lerp(0.8f, 3.2f, howStuck);
                transform.position += slideDir * push * Time.deltaTime;

                // 滑的时候船头慢慢转向深水，免得"横着蹭"看着别扭
                if (slideDir.sqrMagnitude > 0.01f)
                {
                    float wantYaw = Mathf.Atan2(slideDir.x, slideDir.z) * Mathf.Rad2Deg;
                    float curYaw = transform.eulerAngles.y;
                    float newYaw = Mathf.MoveTowardsAngle(curYaw, wantYaw, 35f * Time.deltaTime);
                    transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
                }
            }

            // 倒车给满速（原来只有 40%，玩家经常退不出来）
            if (throttle < 0f)
                speed = Mathf.MoveTowards(speed, throttle * def.speed * SpeedMul, acceleration * Time.deltaTime);
        }
        else
        {
            stateText = waterDepth > 3f ? "深水航行" : "航行";
            float target = throttle * def.speed * SpeedMul;
            float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration * 0.7f : brakeDecel * 0.6f;
            speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);
        }

        // 船转向比车慢一点
        transform.Rotate(0f, steer * def.turnSpeed * 0.7f * Time.deltaTime, 0f, Space.World);
        transform.position += transform.forward * speed * Time.deltaTime;
        ClampToBounds();

        // 浮在水面 + 轻微摇晃。
        // 注意要跟地形取 max：搁浅时船得"坐在沙面上"，不能钉在水面高度 ——
        // 否则地形一高就把整条船埋进沙里（只剩栏杆露出来）。
        pos = transform.position;
        float bob = Mathf.Sin(Time.time * 1.6f + transform.position.x * 0.3f) * 0.12f;
        float waterY = Mathf.Lerp(pos.y, WaterLevel + 0.25f + bob, 6f * Time.deltaTime);
        float floorY = GroundY(pos.x, pos.z) + 0.35f;
        pos.y = Mathf.Max(waterY, floorY);
        transform.position = pos;

        float yaw = transform.eulerAngles.y;
        float roll = Mathf.Sin(Time.time * 1.2f) * 2.5f - steer * 4f;
        float pitch = Mathf.Cos(Time.time * 1.5f) * 1.5f;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(pitch, yaw, roll), 3f * Time.deltaTime);
    }

    /// <summary>
    /// 采样周围 8 个方向的水深，返回"最深"的那个方向（水平单位向量）和那个方向的水深。
    /// 搁浅时用它决定往哪边滑；都不比脚下深就返回零向量（说明四面都是浅滩，滑不动）。
    /// </summary>
    Vector3 DeeperDirection(Vector3 pos, float radius, out float bestDepth)
    {
        bestDepth = WaterDepthAt(pos.x, pos.z);
        Vector3 best = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float x = pos.x + Mathf.Sin(a) * radius;
            float z = pos.z + Mathf.Cos(a) * radius;
            float d = WaterDepthAt(x, z);
            if (d > bestDepth + 0.02f)
            {
                bestDepth = d;
                best = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
        }
        return best;
    }

    // ==================== 空 ====================

    void UpdateAir()
    {
        var pos = transform.position;
        float groundY = Mathf.Max(GroundY(pos.x, pos.z), WaterLevel);
        waterDepth = Mathf.Max(0f, WaterLevel - GroundY(pos.x, pos.z));
        slopeDeg = 0f;
        maxClimbDeg = 0f;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (airYaw == 0f && Mathf.Abs(transform.eulerAngles.y) > 0.01f) airYaw = transform.eulerAngles.y;

        // ================= 输入 =================
        if (aiControlled)
        {
            // AI：朝 aiSteer 偏航，保持 flightAltitude 高度
            airYaw += aiSteer * def.turnSpeed * 0.8f * dt;
            airRoll = Mathf.Lerp(airRoll, -aiSteer * bankAngle, 3f * dt);
            float wantY = groundY + flightAltitude;
            airPitch = Mathf.Lerp(airPitch, Mathf.Clamp((wantY - pos.y) * 0.12f, -18f, 18f), 2f * dt);
            airThrottle = Mathf.Lerp(airThrottle, Mathf.Abs(aiThrottle) < 0.05f ? 0.65f : 0.85f, 2f * dt);
        }
        else
        {
            // 玩家：W/S 油门，A/D 横滚，鼠标控俯仰偏航（虚拟摇杆，松手自动回中）
            airThrottle = Mathf.Clamp01(airThrottle + Bindings.AxisV * airThrottleRate * dt);

            float rollIn = Bindings.AxisH;
            airRoll = Mathf.Clamp(airRoll + rollIn * airRollRate * dt, -85f, 85f);
            if (Mathf.Abs(rollIn) < 0.05f) airRoll = Mathf.MoveTowards(airRoll, 0f, 45f * dt);

            // 按住 C 环视时，鼠标只转镜头 —— 不要再把飞机拐走（不然一边看一边侧翻）
            if (!CameraFollow.FreeLookActive)
            {
                _airAimX = Mathf.Clamp(_airAimX + Input.GetAxis("Mouse X") * airMouseSensitivity, -1f, 1f);
                _airAimY = Mathf.Clamp(_airAimY + Input.GetAxis("Mouse Y") * airMouseSensitivity, -1f, 1f);
            }
            _airAimX = Mathf.MoveTowards(_airAimX, 0f, airRecenterSpeed * dt);
            _airAimY = Mathf.MoveTowards(_airAimY, 0f, airRecenterSpeed * dt);

            airPitch += _airAimY * airPitchRate * dt;     // 鼠标上推 = 抬头
            airYaw += _airAimX * airYawRate * dt;
        }

        // 横滚带出转弯（压杆转弯）
        airYaw += Mathf.Sin(airRoll * Mathf.Deg2Rad) * airBankTurnRate * dt;
        airPitch = Mathf.Clamp(airPitch, -70f, 70f);
        airYaw = Mathf.Repeat(airYaw, 360f);

        // ================= 姿态 =================
        var targetRot = Quaternion.Euler(-airPitch, airYaw, -airRoll);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, airResponse * dt);

        // ================= 速度 =================
        float targetSpeed = Mathf.Lerp(def.speed * 0.45f, def.speed * 1.15f, airThrottle) * SpeedMul;
        speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * 1.5f * dt);

        // ================= 位移 =================
        transform.position += transform.forward * speed * dt;
        ClampToBounds();

        pos = transform.position;
        if (pos.y > airCeiling)
        {
            pos.y = airCeiling;
            transform.position = pos;
            airPitch = Mathf.MoveTowards(airPitch, -20f, 40f * dt);
        }

        // 撞地 = 坠毁
        if (pos.y < groundY + airCrashHeight)
        {
            stateText = "坠毁";
            Crash();
            return;
        }

        stateText = $"飞行 · 油门 {airThrottle * 100f:F0}% · {speed:F0} m/s";
    }

    /// <summary>撞地坠毁：走正常击毁流程（爆炸 / 事件 / 玩家回车库）。</summary>
    void Crash()
    {
        if (!alive) return;
        alive = false;
        var combat = GetComponent<VehicleCombat>();
        if (combat != null) combat.DestroyByEnvironment();
        else gameObject.SetActive(false);
    }

    // ==================== 挂点接口（武器阶段用）====================

    /// <summary>转炮塔（度）。没有炮塔节点的载具（飞机）会自动忽略。</summary>
    public void RotateTurret(float deltaDeg)
    {
        turretYaw += deltaDeg;
        // 偏航限位：炮艇主炮后方是船桥，炮管转过去会插进驾驶室（见 VehicleDef.turretYawLimit）。
        // turretYaw 是累积值，先归一化到 -180~180 再夹，夹完写回去，避免累积溢出。
        float limit = def != null ? def.turretYawLimit : 180f;
        if (limit > 0f && limit < 180f)
        {
            float norm = Mathf.DeltaAngle(0f, turretYaw);
            if (norm > limit || norm < -limit)
                turretYaw = Mathf.Clamp(norm, -limit, limit);
        }
        if (turretBone != null)
            turretBone.localRotation = Quaternion.Euler(0f, turretYaw, 0f);
    }

    /// <summary>炮塔当前朝向与"指向目标"的夹角（度）。没有炮塔返回 0。</summary>
    public float TurretAngleError(Vector3 worldPoint)
    {
        if (turretBone == null) return 0f;
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return 0f;
        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - transform.eulerAngles.y;
        return Mathf.Abs(Mathf.DeltaAngle(turretYaw, want));
    }

    /// <summary>把炮塔转向世界坐标点，受炮塔转速限制；有独立炮管时顺带抬/压炮。</summary>
    public void AimTurretAt(Vector3 worldPoint, float maxDegPerSec)
    {
        if (turretBone == null) return;
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;

        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(turretYaw, want);
        float limit = Mathf.Min(Mathf.Abs(delta), maxDegPerSec * turretSpeedMul * Time.deltaTime);
        RotateTurret(Mathf.Sign(delta) * limit);

        AimBarrelAt(worldPoint);
    }

    /// <summary>直接设置炮管俯仰（度，正=抬炮）。玩家用鼠标上下控的就是这个。</summary>
    public void SetBarrelPitch(float deg, bool smooth = true)
    {
        if (barrelBone == null) return;
        float want = Mathf.Clamp(deg, maxDepression, maxElevation);
        if (smooth)
        {
            // 用 SmoothDamp 而不是 MoveTowards：MoveTowards 是匀速直冲，停下来很硬；
            // SmoothDamp 有加速和收尾，手感接近战雷那种"炮管有重量"。
            barrelPitch = Mathf.SmoothDamp(barrelPitch, want, ref _pitchVel, Mathf.Max(0.02f, 1f / Mathf.Max(1f, pitchSpeed)));
        }
        else barrelPitch = want;
        ApplyPitch();
    }

    /// <summary>
    /// 把当前 barrelPitch 分摊到「塔体」和「炮管」两个节点上。
    /// 普通炮塔：只有炮管转（elevatingBone 为空）；摇摆炮塔：塔体按 elevatingFollow 比例跟随，
    /// 炮管转剩下的部分，两者相加正好等于目标俯仰角。
    /// </summary>
    void ApplyPitch()
    {
        float follow = elevatingBone != null ? Mathf.Clamp01(elevatingFollow) : 0f;
        if (elevatingBone != null)
            elevatingBone.localRotation = Quaternion.Euler(-barrelPitch * follow, 0f, 0f);
        if (barrelBone != null)
            barrelBone.localRotation = Quaternion.Euler(-barrelPitch * (1f - follow), 0f, 0f);
    }

    float _pitchVel;

    /// <summary>
    /// 玩家直接瞄准：偏航 + 俯仰各自限速、**同时**运动（可以左上、右下斜着走）。
    /// 不像 AimTurretAt 那样要先算出目标点，所以不会有相机→炮塔→相机的反馈漂移。
    /// </summary>
    public void AimDirect(float targetYawWorld, float targetPitch, float yawDegPerSec)
    {
        if (turretBone != null)
        {
            float want = Mathf.DeltaAngle(transform.eulerAngles.y, targetYawWorld);
            float delta = Mathf.DeltaAngle(turretYaw, want);
            float limit = Mathf.Min(Mathf.Abs(delta), yawDegPerSec * turretSpeedMul * Time.deltaTime);
            if (limit > 0f) RotateTurret(Mathf.Sign(delta) * limit);
        }
        SetBarrelPitch(targetPitch);
    }

    /// <summary>
    /// 直接按**车体/船体本地角**瞄准（2026-11-12，锁定用）。
    ///
    /// 和 AimDirect 的唯一区别：这里收的是"相对车体的偏航/俯仰"。
    /// 旧写法把**世界偏航**直接喂给炮塔（`DeltaAngle(船体yaw, 世界yaw)`），漏掉了船随浪的
    /// 俯仰和横滚 —— 炮艇在浪里横滚 2° 时炮管就固定歪 2°：第三人称 FOV 50° 看不出来，
    /// 开镜 14° 视野里就是几十像素、400 m 外打偏十几米（用户报的"开镜后瞄不准"）。
    /// 把世界方向先转进船体坐标系再拆角，炮管就真的指向目标，浪再大也稳。
    /// </summary>
    public void AimDirectLocal(float relYaw, float pitch, float yawDegPerSec)
    {
        if (turretBone != null)
        {
            float delta = Mathf.DeltaAngle(turretYaw, relYaw);
            float limit = Mathf.Min(Mathf.Abs(delta), yawDegPerSec * turretSpeedMul * Time.deltaTime);
            if (limit > 0f) RotateTurret(Mathf.Sign(delta) * limit);
        }
        SetBarrelPitch(pitch);
    }

    /// <summary>炮塔当前**世界**偏航（度）。取炮塔节点的真实朝向，含船体姿态。</summary>
    public float TurretWorldYaw
    {
        get
        {
            if (turretBone != null)
            {
                Vector3 f = turretBone.forward;
                if (f.sqrMagnitude > 1e-6f) return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            return transform.eulerAngles.y + turretYaw;
        }
    }

    /// <summary>
    /// 只转炮塔（不动炮管俯仰），朝一个世界偏航角。
    /// 机枪模式用它让炮塔**慢慢跟着视角**走：枪架只有 ±120° 回转，炮塔不跟的话
    /// 视角转到船尾方向枪架就卡死（用户报的"机枪索敌瞄不到人"）。
    /// </summary>
    public void RotateTurretToward(float worldYaw, float degPerSec)
    {
        if (turretBone == null) return;
        float want = Mathf.DeltaAngle(transform.eulerAngles.y, worldYaw);
        float delta = Mathf.DeltaAngle(turretYaw, want);
        float limit = Mathf.Min(Mathf.Abs(delta), degPerSec * turretSpeedMul * Time.deltaTime);
        if (limit > 0f) RotateTurret(Mathf.Sign(delta) * limit);
    }

    /// <summary>
    /// 机枪按**世界方向**瞄准：先把方向转进机枪回转座的父坐标系（炮塔，含船体姿态），
    /// 再拆成回转角 + 仰角。旧写法以"船体偏航 + 炮塔角"为基准，同样漏掉船体俯仰/横滚，固定歪 1~2°。
    /// </summary>
    public void AimDeckGunWorld(Vector3 worldDir, float maxElev, float maxDepress, float degPerSec)
    {
        if (mgBone == null || mgYawBone == null || worldDir.sqrMagnitude < 1e-6f) return;
        var baseTf = mgYawBone.parent != null ? mgYawBone.parent : transform;
        Vector3 local = baseTf.InverseTransformDirection(worldDir.normalized);
        float relYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        SetDeckGunYaw(relYaw, degPerSec);
        SetDeckGunPitch(pitch, maxElev, maxDepress, degPerSec);
    }

    /// <summary>炮管俯仰：按目标高度算角度，受 maxElevation/maxDepression 和转速限制（AI 用）。</summary>
    void AimBarrelAt(Vector3 worldPoint)
    {
        if (barrelBone == null) return;

        Vector3 pivot = barrelBone.position;
        Vector3 to = worldPoint - pivot;
        float horiz = new Vector2(to.x, to.z).magnitude;
        if (horiz < 1f) return;

        float want = Mathf.Atan2(to.y, horiz) * Mathf.Rad2Deg;
        want = Mathf.Clamp(want, maxDepression, maxElevation);
        barrelPitch = Mathf.MoveTowards(barrelPitch, want, pitchSpeed * Time.deltaTime);
        ApplyPitch();
    }

    /// <summary>炮口世界坐标（开火起点）。没有炮口节点时退化到车头前方。</summary>
    public Vector3 GetMuzzlePosition()
    {
        // 逻辑炮塔（模型整体网格、炮塔不视觉旋转）时，出膛点固定在车头正前方。
        // 否则玩家往侧后方瞄准时，出膛点会绕炮塔转到车尾，看起来就像"子弹向后飞"。
        if (!turretIsVisual)
            return transform.position + transform.forward * (hullHalfLength + 0.6f) + transform.up * 0.8f;

        if (muzzleBone != null) return muzzleBone.position;
        return transform.position + transform.forward * 3f + Vector3.up * 1.2f;
    }

    /// <summary>炮口朝向（子弹方向）。有炮口节点就跟着炮口（含俯仰）。</summary>
    public Vector3 GetMuzzleForward()
    {
        if (muzzleBone != null) return muzzleBone.forward;
        return turretBone != null ? turretBone.forward : transform.forward;
    }

    // ==================== 甲板/车顶机枪 ====================

    /// <summary>有没有独立机枪挂点。</summary>
    public bool HasDeckGun => mgBone != null;

    /// <summary>机枪当前仰角（度）。</summary>
    public float DeckGunPitch => mgPitch;

    /// <summary>直接设机枪仰角（正 = 抬高）。玩家用鼠标上下控的就是这个。</summary>
    public void SetDeckGunPitch(float deg, float maxElev, float maxDepress, float degPerSec, bool smooth = true)
    {
        if (mgBone == null) return;
        float want = Mathf.Clamp(deg, maxDepress, maxElev);
        if (smooth) mgPitch = Mathf.MoveTowards(mgPitch, want, Mathf.Max(1f, degPerSec) * Time.deltaTime);
        else mgPitch = want;
        // 和炮管同一套符号约定：局部 -X 旋转 = 抬高
        mgBone.localRotation = Quaternion.Euler(-mgPitch, 0f, 0f);
    }

    /// <summary>
    /// 机枪回转（相对炮塔的偏航角，度）。
    /// 枪架是独立回转座：转机枪不用拖着整座炮塔一起甩 —— 炮塔转速只有 50°/s，
    /// 用它带着机枪转，画面会明显跟不上鼠标。
    /// </summary>
    public void SetDeckGunYaw(float deg, float degPerSec, bool smooth = true)
    {
        if (mgYawBone == null) return;
        deg = Mathf.Repeat(deg + 180f, 360f) - 180f;      // 折到 -180..180
        deg = Mathf.Clamp(deg, -mgYawLimit, mgYawLimit);  // 回转限位（默认 ±120°）
        if (smooth) mgYaw = Mathf.MoveTowardsAngle(mgYaw, deg, Mathf.Max(1f, degPerSec) * Time.deltaTime);
        else mgYaw = deg;
        mgYawBone.localRotation = Quaternion.Euler(0f, mgYaw, 0f);
    }

    // ==================== 外部冲量（撞击弹开） ====================

    /// <summary>
    /// 外部冲量（米/秒）。载具本来是"沿车头方向按 speed 前进"，没有横向速度，
    /// 所以撞击弹开要单独存一个矢量，在 LateUpdate 里叠加位移并自己衰减。
    /// </summary>
    [System.NonSerialized] public Vector3 knockback;

    /// <summary>撞一下：给一个反向冲量（世界坐标），车会真的被弹开一段。</summary>
    public void ApplyKnockback(Vector3 v)
    {
        v.y = 0f;                       // 只弹水平方向，不然车会被顶上天
        knockback += v;
        float max = 14f;                // 别叠加到离谱
        if (knockback.magnitude > max) knockback = knockback.normalized * max;
    }

    void LateUpdate()
    {
        if (knockback.sqrMagnitude < 0.0001f) return;
        transform.position += knockback * Time.deltaTime;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 20f * Time.deltaTime);
        if (knockback.sqrMagnitude < 0.0001f) knockback = Vector3.zero;
    }

    /// <summary>按世界坐标点算机枪仰角（AI 用）。</summary>
    public void AimDeckGunAt(Vector3 worldPoint, float maxElev, float maxDepress, float degPerSec)
    {
        if (mgBone == null) return;
        Vector3 to = worldPoint - mgBone.position;
        float horiz = new Vector2(to.x, to.z).magnitude;
        float want = Mathf.Atan2(to.y, Mathf.Max(0.2f, horiz)) * Mathf.Rad2Deg;
        SetDeckGunPitch(want, maxElev, maxDepress, degPerSec);
    }

    /// <summary>机枪枪口世界坐标（开火起点）。</summary>
    public Vector3 GetDeckGunMuzzle()
    {
        if (mgMuzzle != null) return mgMuzzle.position;      // 模型自带枪口节点，最准
        if (mgBone == null) return GetMuzzlePosition();
        return mgBone.position + mgBone.forward * 1.15f;
    }

    /// <summary>机枪枪管朝向（含仰角）。</summary>
    public Vector3 GetDeckGunForward()
    {
        if (mgBone == null) return GetMuzzleForward();
        return mgBone.forward;
    }

    // ==================== 公共 ====================

    void ClampToBounds()
    {
        float half = MapHalfSize - 1.5f;
        var p = transform.position;
        float cx = Mathf.Clamp(p.x, -half, half);
        float cz = Mathf.Clamp(p.z, -half, half);
        if (cx != p.x || cz != p.z)
        {
            p.x = cx; p.z = cz;
            transform.position = p;
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * Time.deltaTime);
        }
    }

    Vector2 SlopeGradient(float x, float z)
    {
        float d = 1f;
        return new Vector2(
            GroundY(x + d, z) - GroundY(x - d, z),
            GroundY(x, z + d) - GroundY(x, z - d));
    }

    /// <summary>
    /// 前方这一步会不会撞到实体障碍（建筑/路障）。
    /// 地形和别的载具不算 —— 地形靠贴地处理，车与车之间先不管。
    /// </summary>
    bool IsBlockedByObstacle(Vector3 delta)
    {
        var col = GetComponent<Collider>();
        if (col == null) return false;

        // 一律用世界包围盒算，别手算 half：手算要乘 lossyScale，缩放里一旦有 0/NaN
        // 就会给 PhysX 喂非法尺寸 —— 实测那是会直接原生崩溃的（连 Mono 异常都没有）。
        var bb = col.bounds;
        Vector3 center = bb.center;
        Vector3 half = bb.extents * 0.55f;
        if (float.IsNaN(half.x) || float.IsNaN(half.y) || float.IsNaN(half.z)) return false;
        if (half.x <= 0.01f || half.y <= 0.01f || half.z <= 0.01f) return false;
        if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || float.IsNaN(delta.z)) return false;

        float step = delta.magnitude;
        float terrainY = GroundY(transform.position.x, transform.position.z);

        // ---------- ① 台阶试探（2026-11-12）----------
        // 把探测盒**抬到"台阶高度"之上**再往前打一次：
        //   · 抬起来之后畅通 → 前面只是个矮台阶（地基裙边 / 路缘 / 碎石），履带压上去就行，不算挡；
        //   · 抬起来之后还撞东西 → 那是墙（或者矮台阶后面紧贴着一堵墙），挡住。
        // 为什么要这么做：楼体是**整栋一个 MeshCollider**，它的包围盒顶在楼顶（十几米），
        // 所以"看包围盒高度"永远判不出楼底那一小块矮东西；必须用"抬高试探"这种方式才分得清
        // "矮台阶"和"墙"。用户 2026-11-12 报的「几乎所有的建筑物边缘都把车顶停」就是这条。
        if (step > 0.01f)
        {
            float lift = (terrainY + stepHeight) - (center.y - half.y);
            if (lift > 0.02f)
            {
                Vector3 raised = center + Vector3.up * lift;
                var stepHits = UnityEngine.Physics.BoxCastAll(raised, half, delta / step, Quaternion.identity,
                                                             step + 0.2f, ~0, QueryTriggerInteraction.Ignore);
                bool clearAhead = true;
                foreach (var h in stepHits)
                {
                    var c = h.collider;
                    if (c == null || c == col) continue;
                    if (c is TerrainCollider) continue;
                    if (c.GetComponentInParent<VehicleCombat>() != null) continue;
                    clearAhead = false;
                    break;
                }
                if (clearAhead) return false;    // 抬高就畅通 → 矮台阶，放行（贴地那步会把车抬上去）
            }
        }

        // 已经卡在障碍里了 → **挡住这一帧**（车会停住，玩家可以倒车出来）。
        // 2026-11-09 修：原来这里是 `return false`（当作没挡），本意是防止被建筑夹住出不来，
        // 但建筑的 BoxCollider 是从地面到楼顶的整块实心盒 —— 车一开到楼底就落进盒子里，
        // 于是被判定成"已经卡住"，直接穿楼而过（用户报的"车能开进楼里"）。
        // 注意：不要在这里调 Physics.ComputePenetration 去把车挤出去 —— 实测会在打包版/Play 里
        // 直接原生崩溃（PhysX 内部崩，连 Mono 异常都没有）。
        var inside = UnityEngine.Physics.OverlapBox(center, half, Quaternion.identity,
                                                    ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in inside)
        {
            if (h == null || h == col) continue;
            if (h is TerrainCollider) continue;
            if (h.GetComponentInParent<VehicleCombat>() != null) continue;
            if (IsLowLedge(h)) continue;      // 矮台阶（地基裙边等）不算卡住，履带能压过去
            LogBlocked(h);                    // 打包版也会写进 Player.log，方便定位"这里为什么过不去"
            _lastBlockNormal = (transform.position - h.bounds.center).normalized;
            return true;      // 挡住，别往前走
        }
        if (step > 0.01f)
        {
            var hits = UnityEngine.Physics.BoxCastAll(center, half, delta / step, Quaternion.identity,
                                                      step + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider == col) continue;
                if (h.collider is TerrainCollider) continue;                       // 地形不挡
                if (h.collider.GetComponentInParent<VehicleCombat>() != null) continue;  // 载具不挡
                if (IsLowLedge(h.collider)) continue;   // 矮台阶：不挡，让车翻上去
                LogBlocked(h.collider);
                _lastBlockNormal = h.normal;            // 真正的墙面法线（蹭墙滑行用这个最准）
                return true;
            }
        }
        return false;
    }

    float _lastBlockLog = -9f;
    Vector3 _lastBlockNormal;      // 上次被挡时的墙面法线（蹭墙滑行用）

    /// <summary>
    /// 被挡住时记一条日志（限流 1 秒）。打包版会写进
    /// `%USERPROFILE%\AppData\LocalLow\<公司>\<产品>\Player.log`，
    /// 用户说"这里过不去"时直接读日志就能知道挡他的是哪个物件、多高 —— 不用猜。
    /// </summary>
    void LogBlocked(Collider c)
    {
        if (Time.time - _lastBlockLog < 1f) return;
        _lastBlockLog = Time.time;
        float terrainY = GroundY(transform.position.x, transform.position.z);
        Debug.Log($"[越障] 被挡住 obj={c.name} root={c.transform.root.name} " +
                  $"顶面-地形={c.bounds.max.y - terrainY:F2}m 车={name} 位置={transform.position.ToString("F1")} " +
                  $"yaw={transform.eulerAngles.y:F0} stepHeight={stepHeight:F2} 坡度={slopeDeg:F0}°");
    }

    /// <summary>
    /// 这个碰撞体是不是"矮台阶"——顶面只比车底高一点点（≤ stepHeight）。
    ///
    /// 2026-11-12 用户反馈：**几乎所有的建筑物边缘都会把车直接顶停**。
    /// 根因是 11-11 给每栋楼加的地基裙边（`PropScatter.AddFoundation`）上沿露出 0.22 m，
    /// 而 `IsBlockedByObstacle` 对任何非地形碰撞体一律"挡住"，于是 320 栋楼周边全成了不可通行的矮墙；
    /// 车一旦开上去还会被判成"已经卡在障碍里"，连倒车都费劲。
    /// 履带车本来就该能翻过 20~50 cm 的台阶（路缘、水泥板、碎石），所以这类矮台阶直接放行。
    /// 楼体本身的 MeshCollider 顶面在十几米高 → 不在这个范围内，照样挡得住。
    /// </summary>
    bool IsLowLedge(Collider c)
    {
        if (c == null) return false;
        // 高度差要跟**地形**比，不能跟车体碰撞盒底比 —— 车体碰撞盒是个大盒子，
        // 盒底通常比地面低一两米，用它算"台阶高度"会把 0.22 m 的地基算成 1.6 m，永远判不成矮台阶。
        float groundY = GroundY(transform.position.x, transform.position.z);
        float rise = c.bounds.max.y - groundY;
        return rise > 0.01f && rise <= stepHeight;
    }

    /// <summary>
    /// 车底实际该贴的面高度：地形高度，或者**只比地形高一点点的台阶顶面**（地基裙边 / 路缘 / 碎石）。
    ///
    /// 只放行 IsLowLedge 还不够：车虽然能往前开，但贴地用的是地形高度，
    /// 视觉上会"陷在水泥板里"。这里从车顶上方往下打一条射线，把"矮台阶的顶面"也当成可行驶地面。
    /// </summary>
    float DriveSurfaceY(Vector3 pos)
    {
        float terrainY = GroundY(pos.x, pos.z);
        var own = GetComponent<Collider>();

        Vector3 from = pos + Vector3.up * (stepHeight + 3f);
        float len = stepHeight + 6f;
        var hits = UnityEngine.Physics.RaycastAll(from, Vector3.down, len, ~0, QueryTriggerInteraction.Ignore);
        float best = terrainY;
        foreach (var h in hits)
        {
            var c = h.collider;
            if (c == null || c == own) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;   // 自己身上的碰撞盒
            if (c is TerrainCollider) continue;
            if (c.GetComponentInParent<VehicleCombat>() != null) continue;               // 别的载具不算地面
            float y = h.point.y;
            if (y <= terrainY + 0.02f) continue;          // 和地形一样高，用地形就行
            if (y > terrainY + stepHeight) continue;      // 比台阶高得多 → 那是墙，不是地面
            if (y > best) best = y;
        }
        return best;
    }

    // ---------------- 地形源抽象 ----------------
    // terrain 有值 → 旧测试场（TerrainField）；留空 → 正式地图（Unity Terrain 分块）
    // 这样同一套载具代码既能在 Test_Stage1 回归测试，也能跑 2 km 正式图。

    public float WaterLevel => terrain != null ? terrain.waterLevel : BattlefieldTerrain.WaterLevel;
    public float MapHalfSize => terrain != null ? terrain.size * 0.5f : BattlefieldTerrain.MapSize * 0.5f;

    public float GroundY(float x, float z)
        => terrain != null ? terrain.GetHeight(x, z) : BattlefieldTerrain.SampleHeight(x, z);

    public float GroundSlope(float x, float z)
        => terrain != null ? terrain.GetSlopeDeg(x, z) : BattlefieldTerrain.SampleSteepness(x, z);

    public float WaterDepthAt(float x, float z)
        => terrain != null ? terrain.GetWaterDepth(x, z) : BattlefieldTerrain.WaterDepth(x, z);

    public Vector3 GroundNormal(float x, float z)
    {
        if (terrain != null) return terrain.GetNormal(x, z);
        float d = 1.5f;
        return new Vector3(
            GroundY(x - d, z) - GroundY(x + d, z),
            2f * d,
            GroundY(x, z - d) - GroundY(x, z + d)).normalized;
    }

    // ---------------- 复活重置 ----------------

    /// <summary>进水报废：走正常击毁流程，这样会爆炸、会计入阵亡、玩家会回车库。</summary>
    void Drown()
    {
        if (!alive) return;
        alive = false;
        stateText = "已报废";
        var combat = GetComponent<VehicleCombat>();
        if (combat != null) combat.DestroyByEnvironment();
        else gameObject.SetActive(false);
    }

    /// <summary>复活时把运行时状态清干净（沉没计时、速度、存活标记）。</summary>
    public void ResetState()
    {
        alive = true;
        speed = 0f;
        _sinkTimer = 0f;
        stateText = "正常";
        aiThrottle = 0f;
        aiSteer = 0f;
    }
}

