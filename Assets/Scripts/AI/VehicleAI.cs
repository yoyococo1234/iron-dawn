using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 载具 AI：感知 → 决策 → 沿粗网格路径开 → 瞄准开火。
///
/// 感知：找最近的敌对载具（同域优先，跨域只在被打时反击）
/// 决策：能打就打（停在射程内）、打不到就沿路径推进、血少了后撤
/// 寻路：NavGrid 的 A*，每 repathInterval 秒重规划一次
/// 开火：炮塔转到目标 4° 以内才打，避免乱射
/// </summary>
[RequireComponent(typeof(VehicleCombat))]
public class VehicleAI : MonoBehaviour
{
    [Header("感知")]
    [Tooltip("视野半径（米）")]
    public float visionRange = 450f;
    [Tooltip("开火距离（米）")]
    public float fireRange = 320f;
    [Tooltip("同域目标的优先级（越小越优先）")]
    public float sameDomainBonus = 0.6f;

    [Header("决策")]
    [Tooltip("血量低于这个比例就后撤")]
    public float retreatHpRatio = 0.28f;
    [Tooltip("重新规划路径的间隔（秒）")]
    public float repathInterval = 2f;
    [Tooltip("思考间隔（秒）")]
    public float thinkInterval = 0.4f;

    [Header("驾驶")]
    [Tooltip("转向灵敏度（角度误差映射到 -1~1）")]
    public float steerAngle = 45f;
    [Tooltip("角度误差超过这个值就减速")]
    public float slowDownAngle = 55f;

    [Header("目标点")]
    [Tooltip("没有敌人时去哪（世界坐标）。留空则去地图中心")]
    public Transform objective;

    public string StateText { get; private set; } = "待机";
    public VehicleCombat Target { get; private set; }

    VehicleCombat _self;
    VehiclePhysics _phys;
    NavGrid _grid;

    readonly List<Vector3> _path = new List<Vector3>();
    int _pathIndex;
    float _nextRepath;
    float _nextThink;
    Vector3 _lastTargetPos;

    void Awake()
    {
        _self = GetComponent<VehicleCombat>();
        _phys = GetComponent<VehiclePhysics>();
    }

    void Start()
    {
        _grid = NavGrid.Instance;
        if (_phys != null) _phys.aiControlled = true;
    }

    void Update()
    {
        if (_self == null || !_self.Alive) return;

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + thinkInterval;
            Think();
        }

        Drive();
        AimAndFire();
    }

    // ==================== 感知 ====================

    void Think()
    {
        Target = FindTarget();

        bool lowHp = _self.hp / Mathf.Max(1f, _self.maxHp) < retreatHpRatio;
        if (lowHp)
        {
            StateText = "后撤";
            return;
        }

        if (Target == null)
        {
            StateText = "推进";
            return;
        }

        float dist = Vector3.Distance(transform.position, Target.transform.position);
        StateText = dist <= fireRange ? "交战" : "接近";
    }

    VehicleCombat FindTarget()
    {
        VehicleCombat best = null;
        float bestScore = float.MaxValue;

        foreach (var c in Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None))
        {
            if (c == _self || !c.Alive || c.Team == _self.Team) continue;

            float dist = Vector3.Distance(transform.position, c.transform.position);
            if (dist > visionRange) continue;

            // 同域优先；跨域只在很近时才考虑
            bool sameDomain = c.Def != null && _self.Def != null && c.Def.domain == _self.Def.domain;
            float score = dist * (sameDomain ? sameDomainBonus : 1f);
            if (!sameDomain && dist > visionRange * 0.5f) continue;

            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    // ==================== 驾驶 ====================

    void Drive()
    {
        if (_phys == null) return;

        // 后撤：掉头往回开
        if (StateText == "后撤")
        {
            Vector3 away = transform.position - (Target != null ? Target.transform.position : transform.position);
            away.y = 0f;
            if (away.sqrMagnitude > 1f) SteerToward(transform.position + away.normalized * 50f);
            _phys.aiThrottle = 1f;
            return;
        }

        // 交战：停住，只转炮塔
        if (StateText == "交战")
        {
            _phys.aiThrottle = 0f;
            _phys.aiSteer = 0f;
            return;
        }

        // 推进 / 接近：沿路径走
        Vector3 goal = Target != null
            ? Target.transform.position
            : (objective != null ? objective.position : new Vector3(0f, 0f, 0f));

        // 目标移动较多或到时间就重规划
        if (Time.time >= _nextRepath || Vector3.Distance(goal, _lastTargetPos) > 60f)
        {
            _nextRepath = Time.time + repathInterval;
            _lastTargetPos = goal;
            Repath(goal);
        }

        Vector3 wp = CurrentWaypoint();
        SteerToward(wp);
    }

    void Repath(Vector3 goal)
    {
        if (_grid == null) { _grid = NavGrid.Instance; if (_grid == null) return; }

        float maxSlope = _phys.MaxClimbDeg;
        float wade = _phys.wadeDepth;
        if (_grid.FindPath(transform.position, goal, maxSlope, wade, _path))
        {
            // 第 0 个路点是"自己所在格子的中心"，通常在车后方 —— 跳过，否则会掉头绕圈
            _pathIndex = _path.Count > 1 ? 1 : 0;
        }
        else _path.Clear();
    }

    Vector3 CurrentWaypoint()
    {
        if (_path.Count == 0) return _lastTargetPos;

        float reach = _grid.cellSize * 1.1f;

        // 到了就换下一个；已经开过头（路点在身后）也换
        while (_pathIndex < _path.Count - 1)
        {
            Vector3 wp = _path[_pathIndex];
            Vector3 to = wp - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            bool reached = d < reach;
            bool passed = d < reach * 2.5f && Vector3.Dot(transform.forward, to.normalized) < -0.2f;
            if (!reached && !passed) break;
            _pathIndex++;
        }

        return _path[Mathf.Min(_pathIndex, _path.Count - 1)];
    }

    void SteerToward(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;

        float desiredYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float err = Mathf.DeltaAngle(transform.eulerAngles.y, desiredYaw);

        _phys.aiSteer = Mathf.Clamp(err / steerAngle, -1f, 1f);
        // 大幅转向时别停死，留 45% 油门边走边转
        _phys.aiThrottle = Mathf.Abs(err) > slowDownAngle ? 0.45f : 1f;
    }

    // ==================== 开火 ====================

    void AimAndFire()
    {
        if (Target == null || !Target.Alive) return;
        if (_self.Weapon == null) return;

        Vector3 aim = Target.transform.position + Vector3.up * 1.2f;
        float dist = Vector3.Distance(transform.position, aim);
        if (dist > fireRange) return;

        float turn = _self.Def != null ? _self.Def.turretTurnSpeed : 90f;
        _phys.AimTurretAt(aim, turn);

        if (_phys.TurretAngleError(aim) < 4f) _self.Fire(aim);
    }
}
