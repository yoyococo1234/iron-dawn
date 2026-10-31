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
    [Tooltip("视野半径（米）。2 km 图上 450 m 太近视，改成 700 m")]
    public float visionRange = 700f;
    [Tooltip("开火距离（米）。实际取 min(这个, 武器射程)")]
    public float fireRange = 600f;
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
        Vector3 objPoint = objective != null ? objective.position : Vector3.zero;
        Vector3 goal;
        if (Target == null)
        {
            goal = objPoint;
        }
        else if (NavMode == NavGrid.NavMode.Sea && !IsOnWater(Target.transform.position))
        {
            // 炮艇追不上岸上的坦克 —— 去海上的目标点巡逻/守点
            goal = objPoint;
        }
        else
        {
            goal = Target.transform.position;
        }

        // 飞机不用寻路，直接朝目标飞
        if (NavMode == NavGrid.NavMode.Air)
        {
            SteerToward(goal);
            _phys.aiThrottle = 1f;
            return;
        }

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

    /// <summary>这个点是不是够深的可航行水面。</summary>
    bool IsOnWater(Vector3 p)
    {
        float draft = _phys != null ? _phys.wadeDepth : 0.8f;
        return BattlefieldTerrain.WaterDepth(p.x, p.z) >= draft;
    }

    void Repath(Vector3 goal)
    {
        if (_grid == null) { _grid = NavGrid.Instance; if (_grid == null) return; }

        // 按域找路：陆看坡度+涉水，海看吃水（水深必须够），空不用找
        var mode = NavMode;
        if (mode == NavGrid.NavMode.Air) { _path.Clear(); return; }

        float maxSlope = _phys.MaxClimbDeg;
        float water = _phys.wadeDepth;          // 陆 = 涉水上限；海 = 吃水深度
        if (_grid.FindPath(transform.position, goal, mode, maxSlope, water, _path))
        {
            // 第 0 个路点是"自己所在格子的中心"，通常在车后方 —— 跳过，否则会掉头绕圈
            _pathIndex = _path.Count > 1 ? 1 : 0;
        }
        else _path.Clear();
    }

    /// <summary>当前载具属于哪个通行域。</summary>
    public NavGrid.NavMode NavMode
    {
        get
        {
            if (_self == null || _self.Def == null) return NavGrid.NavMode.Land;
            switch (_self.Def.domain)
            {
                case Domain.Sea: return NavGrid.NavMode.Sea;
                case Domain.Air: return NavGrid.NavMode.Air;
                default: return NavGrid.NavMode.Land;
            }
        }
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

    /// <summary>实际开火距离：AI 的设置和武器射程取小。</summary>
    float EffectiveFireRange
    {
        get
        {
            var w = _self != null ? _self.Weapon : null;
            if (w == null || w.range <= 1f) return fireRange;
            return Mathf.Min(fireRange, w.range);
        }
    }

    /// <summary>目标速度（提前量用）。</summary>
    static Vector3 TargetVelocity(VehicleCombat t)
        => t != null && t.Physics != null ? t.Physics.Velocity : Vector3.zero;

    /// <summary>目标的瞄准点：碰撞盒中心（比 transform.position 更接近车体中心）。</summary>
    static Vector3 TargetCenter(VehicleCombat t)
    {
        if (t == null) return Vector3.zero;
        var col = t.GetComponent<Collider>();
        return col != null ? col.bounds.center : t.transform.position + Vector3.up * 1.2f;
    }

    /// <summary>
    /// 带提前量的瞄准点：按"炮弹飞到那儿要多久"把目标外推。
    /// 弹速 780~1600 m/s，600 m 只需 0.4~0.8 秒，但目标 12 m/s 也能跑 5~10 米，
    /// 车体半宽只有 4 米 —— 不提前量就是打空。
    /// </summary>
    Vector3 LeadAimPoint(VehicleCombat target)
    {
        Vector3 center = TargetCenter(target);
        var w = _self != null ? _self.Weapon : null;
        if (w == null) return center;

        float speed = Mathf.Max(1f, w.projectileSpeed);
        Vector3 origin = _phys != null ? _phys.GetMuzzlePosition() : transform.position;
        Vector3 tv = TargetVelocity(target);

        Vector3 aim = center;
        for (int i = 0; i < 3; i++)
        {
            float t = Vector3.Distance(origin, aim) / speed;
            aim = center + tv * t;
        }
        return aim;
    }

    /// <summary>从炮口到瞄准点之间有没有地形/建筑挡住（挡住了就别开火，省得白打）。</summary>
    bool HasLineOfSight(Vector3 origin, Vector3 aim, VehicleCombat target)
    {
        Vector3 to = aim - origin;
        float dist = to.magnitude;
        if (dist < 1f) return true;

        var hits = Physics.RaycastAll(origin, to / dist, dist, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return true;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var vc = h.collider.GetComponentInParent<VehicleCombat>();
            if (vc == _self) continue;                 // 自己的车体
            if (vc == target) return true;             // 打到目标 = 通视
            if (vc != null) continue;                  // 别的载具挡着，不算地形挡住
            return false;                              // 地形/建筑挡住
        }
        return true;
    }

    void AimAndFire()
    {
        if (Target == null || !Target.Alive) return;
        if (_self.Weapon == null || _phys == null) return;

        Vector3 aim = LeadAimPoint(Target);
        float dist = Vector3.Distance(transform.position, Target.transform.position);
        if (dist > EffectiveFireRange) return;

        float turn = _self.Def != null ? _self.Def.turretTurnSpeed : 90f;
        _phys.AimTurretAt(aim, turn);

        if (_phys.TurretAngleError(aim) >= 4f) return;      // 还没对准
        if (!HasLineOfSight(_phys.GetMuzzlePosition(), aim, Target)) return;   // 被地形挡住

        _self.Fire(aim);
    }
}
