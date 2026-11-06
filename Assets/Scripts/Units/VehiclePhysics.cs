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

    [Header("手感")]
    public float acceleration = 14f;
    public float brakeDecel = 22f;
    public float tiltSpeed = 6f;

    [Header("空中")]
    [Tooltip("AI 巡逻/保持的飞行高度（离地面）")]
    public float flightAltitude = 420f;
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

    /// <summary>油门输入：AI 优先，其次自动驾驶，最后键盘。</summary>
    float ThrottleInput => aiControlled ? aiThrottle : (autoDrive ? autoThrottle : Input.GetAxis("Vertical"));
    /// <summary>转向输入：AI 优先，其次键盘。</summary>
    float SteerInput => aiControlled ? aiSteer : Input.GetAxis("Horizontal");

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
            speed = 0f;
            blocked = true;
        }
        else
        {
            transform.position += delta;
        }
        blockedTime = blocked ? blockedTime + Time.deltaTime : 0f;
        ClampToBounds();

        // 贴地 + 倾斜
        pos = transform.position;
        float groundY = GroundY(pos.x, pos.z);
        pos.y = Mathf.Lerp(pos.y, groundY + groundClearance, 12f * Time.deltaTime);
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
            airThrottle = Mathf.Clamp01(airThrottle + Input.GetAxis("Vertical") * airThrottleRate * dt);

            float rollIn = Input.GetAxis("Horizontal");
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

        Vector3 center;
        Vector3 half;
        if (col is BoxCollider box)
        {
            var s = transform.lossyScale;
            center = transform.TransformPoint(box.center);
            half = Vector3.Scale(box.size, s) * 0.5f;
            half = new Vector3(half.x * 0.85f, half.y * 0.6f, half.z * 0.85f);
        }
        else
        {
            var b = col.bounds;
            center = b.center;
            half = b.extents * 0.8f;
        }

        // 已经卡在障碍里了 → 不再阻挡，否则永远出不来（被建筑夹住的元凶）
        var inside = UnityEngine.Physics.OverlapBox(center, half * 0.88f, transform.rotation,
                                                    ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in inside)
        {
            if (h == null || h == col) continue;
            if (h is TerrainCollider) continue;
            if (h.GetComponentInParent<VehicleCombat>() != null) continue;
            return false;
        }

        var hits = UnityEngine.Physics.BoxCastAll(center, half, delta.normalized, transform.rotation,
                                                  delta.magnitude + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider == col) continue;
            if (h.collider is TerrainCollider) continue;                       // 地形不挡
            if (h.collider.GetComponentInParent<VehicleCombat>() != null) continue;  // 载具不挡
            return true;
        }
        return false;
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

