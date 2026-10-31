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

    // ---- 运行时状态（给 HUD 看）----
    [System.NonSerialized] public float speed;
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
        groundClearance = 0.5f;
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

        float target = throttle * def.speed * slopeFactor * waterFactor;
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
            // 允许倒车脱离
            if (throttle < 0f)
                speed = Mathf.MoveTowards(speed, throttle * def.speed * 0.4f, acceleration * Time.deltaTime);
        }
        else
        {
            stateText = waterDepth > 3f ? "深水航行" : "航行";
            float target = throttle * def.speed;
            float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration * 0.7f : brakeDecel * 0.6f;
            speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);
        }

        // 船转向比车慢一点
        transform.Rotate(0f, steer * def.turnSpeed * 0.7f * Time.deltaTime, 0f, Space.World);
        transform.position += transform.forward * speed * Time.deltaTime;
        ClampToBounds();

        // 浮在水面 + 轻微摇晃
        pos = transform.position;
        float bob = Mathf.Sin(Time.time * 1.6f + transform.position.x * 0.3f) * 0.12f;
        pos.y = Mathf.Lerp(pos.y, WaterLevel + 0.25f + bob, 6f * Time.deltaTime);
        transform.position = pos;

        float yaw = transform.eulerAngles.y;
        float roll = Mathf.Sin(Time.time * 1.2f) * 2.5f - steer * 4f;
        float pitch = Mathf.Cos(Time.time * 1.5f) * 1.5f;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(pitch, yaw, roll), 3f * Time.deltaTime);
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

            _airAimX = Mathf.Clamp(_airAimX + Input.GetAxis("Mouse X") * airMouseSensitivity, -1f, 1f);
            _airAimY = Mathf.Clamp(_airAimY + Input.GetAxis("Mouse Y") * airMouseSensitivity, -1f, 1f);
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
        float targetSpeed = Mathf.Lerp(def.speed * 0.45f, def.speed * 1.15f, airThrottle);
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

    /// <summary>把炮塔转向世界坐标点，受炮塔转速限制。</summary>
    public void AimTurretAt(Vector3 worldPoint, float maxDegPerSec)
    {
        if (turretBone == null) return;
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;

        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(turretYaw, want);
        float limit = Mathf.Min(Mathf.Abs(delta), maxDegPerSec * Time.deltaTime);
        RotateTurret(Mathf.Sign(delta) * limit);
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

    /// <summary>炮口朝向（子弹方向）。</summary>
    public Vector3 GetMuzzleForward()
        => turretBone != null ? turretBone.forward : transform.forward;

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

