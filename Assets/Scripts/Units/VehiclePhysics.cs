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
    [Tooltip("固定飞行高度（离地面/水面）")]
    public float flightAltitude = 25f;
    [Tooltip("转向时的侧倾角度")]
    public float bankAngle = 30f;

    [Header("测试用：自动驾驶（不做输入也能跑）")]
    public bool autoDrive = false;
    public float autoThrottle = 1f;

    [Tooltip("从开始下沉到彻底消失要几秒（方便观察，默认 3 秒）")]
    public float sinkDuration = 3f;
    [Tooltip("下沉速度 m/s")]
    public float sinkSpeed = 1.2f;

    [Header("模型挂点（由 VehicleModelBinder 填入）")]
    [Tooltip("炮塔节点，炮塔转动靠它")]
    public Transform turretBone;
    [Tooltip("炮口节点，开火时子弹从这里出")]
    public Transform muzzleBone;

    [System.NonSerialized] public float turretYaw;

    // ---- 运行时状态（给 HUD 看）----
    [System.NonSerialized] public float speed;
    [System.NonSerialized] public float slopeDeg;
    [System.NonSerialized] public float waterDepth;
    [System.NonSerialized] public float maxClimbDeg;
    [System.NonSerialized] public string stateText = "正常";
    [System.NonSerialized] public bool alive = true;

    float _sinkTimer;

    public float PowerPerTon => enginePower / Mathf.Max(1f, mass);
    public float MaxClimbDeg => Mathf.Atan(PowerPerTon / 30f) * Mathf.Rad2Deg;

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
        if (!alive || terrain == null || def == null) return;

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
        slopeDeg = terrain.GetSlopeDeg(pos.x, pos.z);
        waterDepth = terrain.GetWaterDepth(pos.x, pos.z);
        maxClimbDeg = MaxClimbDeg;

        // 沉没
        if (waterDepth > wadeDepth)
        {
            stateText = "沉没中";
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * Time.deltaTime);
            _sinkTimer += Time.deltaTime;
            pos.y -= sinkSpeed * Time.deltaTime;
            transform.position = pos;
            if (_sinkTimer >= sinkDuration) { alive = false; stateText = "已沉没"; gameObject.SetActive(false); }
            return;
        }

        float waterFactor = waterDepth > 0f ? 0.5f : 1f;
        bool tooSteep = slopeDeg > MaxClimbDeg;
        float slopeFactor = tooSteep ? 0f : Mathf.Lerp(1f, 0.35f, slopeDeg / Mathf.Max(1f, MaxClimbDeg));

        if (tooSteep)
        {
            var grad = SlopeGradient(pos.x, pos.z);
            transform.position -= new Vector3(grad.x, 0f, grad.y).normalized * 1.5f * Time.deltaTime;
        }

        float throttle = autoDrive ? autoThrottle : Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");

        transform.Rotate(0f, steer * def.turnSpeed * Time.deltaTime, 0f, Space.World);

        float target = throttle * def.speed * slopeFactor * waterFactor;
        float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration : brakeDecel;
        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);

        transform.position += transform.forward * speed * Time.deltaTime;
        ClampToBounds();

        // 贴地 + 倾斜
        pos = transform.position;
        float groundY = terrain.GetHeight(pos.x, pos.z);
        pos.y = Mathf.Lerp(pos.y, groundY + groundClearance, 12f * Time.deltaTime);
        transform.position = pos;

        float yaw = transform.eulerAngles.y;
        var normal = terrain.GetNormal(pos.x, pos.z);
        var targetRot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, tiltSpeed * Time.deltaTime);

        stateText = tooSteep ? "打滑（坡太陡）" : (waterDepth > 0f ? "涉水中" : "正常");
    }

    // ==================== 海 ====================

    void UpdateSea()
    {
        var pos = transform.position;
        waterDepth = terrain.GetWaterDepth(pos.x, pos.z);
        slopeDeg = 0f;
        maxClimbDeg = 0f;

        float throttle = autoDrive ? autoThrottle : Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");

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
        pos.y = Mathf.Lerp(pos.y, terrain.waterLevel + 0.25f + bob, 6f * Time.deltaTime);
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
        float groundY = Mathf.Max(terrain.GetHeight(pos.x, pos.z), terrain.waterLevel);
        waterDepth = Mathf.Max(0f, terrain.waterLevel - terrain.GetHeight(pos.x, pos.z));
        slopeDeg = 0f;
        maxClimbDeg = 0f;

        float throttle = autoDrive ? autoThrottle : Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");

        speed = Mathf.MoveTowards(speed, throttle * def.speed, acceleration * 2f * Time.deltaTime);

        transform.Rotate(0f, steer * def.turnSpeed * Time.deltaTime, 0f, Space.World);
        transform.position += transform.forward * speed * Time.deltaTime;
        ClampToBounds();

        // 固定高度
        pos = transform.position;
        pos.y = Mathf.Lerp(pos.y, groundY + flightAltitude, 3f * Time.deltaTime);
        transform.position = pos;

        // 转向时侧倾
        float yaw = transform.eulerAngles.y;
        float bank = -steer * bankAngle;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(0f, yaw, bank), 2.5f * Time.deltaTime);

        stateText = "飞行";
    }

    // ==================== 挂点接口（武器阶段用）====================

    /// <summary>转炮塔（度）。没有炮塔节点的载具（飞机）会自动忽略。</summary>
    public void RotateTurret(float deltaDeg)
    {
        turretYaw += deltaDeg;
        if (turretBone != null)
            turretBone.localRotation = Quaternion.Euler(0f, turretYaw, 0f);
    }

    /// <summary>炮口世界坐标（开火起点）。没有炮口节点时退化到车头前方。</summary>
    public Vector3 GetMuzzlePosition()
    {
        if (muzzleBone != null) return muzzleBone.position;
        return transform.position + transform.forward * 3f + Vector3.up * 1.2f;
    }

    /// <summary>炮口朝向（子弹方向）。</summary>
    public Vector3 GetMuzzleForward()
        => turretBone != null ? turretBone.forward : transform.forward;

    // ==================== 公共 ====================

    void ClampToBounds()
    {
        float half = terrain.size * 0.5f - 1.5f;
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
            terrain.GetHeight(x + d, z) - terrain.GetHeight(x - d, z),
            terrain.GetHeight(x, z + d) - terrain.GetHeight(x, z - d));
    }

    // ---------------- 调试 HUD ----------------

    void OnGUI()
    {
        if (def == null) return;

        var box = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
        var label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        var rich = new GUIStyle(label) { richText = true };

        GUI.Box(new Rect(12, 12, 310, 150), GUIContent.none, box);
        GUILayout.BeginArea(new Rect(22, 20, 295, 140));
        GUILayout.Label($"<b>{def.displayName}</b>（{def.domain}）", rich);
        GUILayout.Label($"状态：{stateText}　速度：{speed:F1} m/s");
        if (def.domain == Domain.Land)
            GUILayout.Label($"坡度：{slopeDeg:F1}°　可爬：{maxClimbDeg:F1}°");
        if (def.domain != Domain.Air)
            GUILayout.Label($"水深：{Mathf.Max(0f, waterDepth):F2} m　上限：{wadeDepth:F1} m");
        GUILayout.Label($"功率/重量：{PowerPerTon:F1} kW/t　质量：{mass:F0} t");
        GUILayout.Label("W/S 前后 · A/D 转向");
        GUILayout.EndArea();
    }
}
