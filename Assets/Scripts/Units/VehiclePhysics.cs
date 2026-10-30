using UnityEngine;

/// <summary>
/// 载具物理（阶段 1 版）：贴地、随地形倾斜、按"功率/重量 vs 坡度"判断能不能爬、涉水减速、深水沉没。
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
    public float wadeDepth = 1.2f;     // 超过这个水深就沉
    public float resistanceHard = 0.9f;
    public float resistanceSoft = 1.8f;
    public float groundClearance = 0.55f;

    [Header("手感")]
    public float acceleration = 14f;
    public float brakeDecel = 22f;
    public float tiltSpeed = 6f;

    [Header("测试用：自动驾驶（不做输入也能跑）")]
    public bool autoDrive = false;
    public float autoThrottle = 1f;

    // ---- 运行时状态（给 HUD 看）----
    [System.NonSerialized] public float speed;
    [System.NonSerialized] public float slopeDeg;
    [System.NonSerialized] public float waterDepth;
    [System.NonSerialized] public float maxClimbDeg;
    [System.NonSerialized] public string stateText = "正常";
    [System.NonSerialized] public bool alive = true;

    float _sinkTimer;

    /// <summary>功率重量比（kW/t）。轻坦约 21，重装约 12。</summary>
    public float PowerPerTon => enginePower / Mathf.Max(1f, mass);

    /// <summary>能爬的最大坡度（度）。实测：21kW/t → 35°，12kW/t → 22°。</summary>
    public float MaxClimbDeg => Mathf.Atan(PowerPerTon / 30f) * Mathf.Rad2Deg;

    void Start() => ApplyDef();

    /// <summary>把 VehicleDef 里的数值灌进运行时参数。</summary>
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
        if (!alive || terrain == null) return;

        var pos = transform.position;
        slopeDeg = terrain.GetSlopeDeg(pos.x, pos.z);
        waterDepth = terrain.GetWaterDepth(pos.x, pos.z);
        maxClimbDeg = MaxClimbDeg;

        // ---------- 沉没 ----------
        if (def != null && def.domain == Domain.Land && waterDepth > wadeDepth)
        {
            stateText = "沉没中";
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * Time.deltaTime);
            _sinkTimer += Time.deltaTime;
            pos.y -= 1.2f * Time.deltaTime;           // 往下沉
            transform.position = pos;
            if (_sinkTimer >= 3f) { alive = false; stateText = "已沉没"; gameObject.SetActive(false); }
            return;
        }

        // ---------- 涉水减速 ----------
        float waterFactor = 1f;
        if (waterDepth > 0f)
        {
            waterFactor = 0.5f;
            stateText = "涉水中";
        }

        // ---------- 爬坡判定 ----------
        bool tooSteep = slopeDeg > MaxClimbDeg;
        float slopeFactor = tooSteep ? 0f : Mathf.Lerp(1f, 0.35f, slopeDeg / Mathf.Max(1f, MaxClimbDeg));
        if (tooSteep)
        {
            stateText = "打滑（坡太陡）";
            // 打滑：缓慢往坡下滑
            var grad = SlopeGradient(pos.x, pos.z);
            transform.position -= new Vector3(grad.x, 0f, grad.y).normalized * 1.5f * Time.deltaTime;
        }
        else if (stateText == "正常" || stateText == "涉水中") { /* 保持 */ }

        // ---------- 输入 ----------
        float throttle = autoDrive ? autoThrottle : Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");

        // 转向
        float turn = steer * (def != null ? def.turnSpeed : 60f);
        transform.Rotate(0f, turn * Time.deltaTime, 0f, Space.World);

        // 速度
        float target = throttle * (def != null ? def.speed : 12f) * slopeFactor * waterFactor;
        float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration : brakeDecel;
        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);

        // 位移
        transform.position += transform.forward * speed * Time.deltaTime;

        // ---------- 地图边界 ----------
        float half = terrain.size * 0.5f - 1.5f;
        var clamped = transform.position;
        clamped.x = Mathf.Clamp(clamped.x, -half, half);
        clamped.z = Mathf.Clamp(clamped.z, -half, half);
        if (clamped.x != transform.position.x || clamped.z != transform.position.z)
        {
            transform.position = clamped;
            speed = Mathf.MoveTowards(speed, 0f, brakeDecel * Time.deltaTime);
        }

        // ---------- 贴地 + 倾斜 ----------
        pos = transform.position;
        float groundY = terrain.GetHeight(pos.x, pos.z);
        pos.y = Mathf.Lerp(pos.y, groundY + groundClearance, 12f * Time.deltaTime);
        transform.position = pos;

        float yaw = transform.eulerAngles.y;
        var normal = terrain.GetNormal(pos.x, pos.z);
        var targetRot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, tiltSpeed * Time.deltaTime);

        // ---------- 状态文字（每帧重算，避免打滑状态被覆盖）----------
        if (tooSteep) stateText = "打滑（坡太陡）";
        else if (waterDepth > 0f) stateText = "涉水中";
        else stateText = "正常";
    }

    /// <summary>坡度的方向（用于打滑下滑）。</summary>
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

        var style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
        var label = new GUIStyle(GUI.skin.label) { fontSize = 14 };

        GUI.Box(new Rect(12, 12, 300, 172), GUIContent.none, style);
        GUILayout.BeginArea(new Rect(22, 20, 285, 160));
        GUILayout.Label($"<b>{def.displayName}</b>", new GUIStyle(label) { richText = true });
        GUILayout.Label($"状态：{stateText}");
        GUILayout.Label($"速度：{speed:F1} m/s");
        GUILayout.Label($"坡度：{slopeDeg:F1}°　可爬：{maxClimbDeg:F1}°");
        GUILayout.Label($"水深：{Mathf.Max(0f, waterDepth):F2} m　涉水上限：{wadeDepth:F1} m");
        GUILayout.Label($"功率/重量：{PowerPerTon:F1} kW/t　质量：{mass:F0} t");
        GUILayout.Label("W/S 前进后退 · A/D 转向");
        GUILayout.EndArea();
    }
}
