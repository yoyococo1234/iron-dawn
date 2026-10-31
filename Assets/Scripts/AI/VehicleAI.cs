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
    [Tooltip("没有交战目标时，主动去抢最近的、不属于自己队的资源点")]
    public bool captureZones = true;

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
    Vector3 _goal;                 // 当前要去的世界坐标
    float _losOkTime;              // 最后一次"看得见目标"的时刻
    float _detourUntil;
    float _detourSign = 1f;

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
            Vector3 away = Target != null ? transform.position - Target.transform.position : Vector3.back;
            away.y = 0f;
            _goal = transform.position + (away.sqrMagnitude > 1f ? away.normalized * 150f : Vector3.zero);
            return;
        }

        if (Target != null)
        {
            float dist = Vector3.Distance(transform.position, Target.transform.position);

            // 能打到 + 看得见才算交战。被地形挡住还傻站着会死锁（两辆车隔着山对望，谁也不动），
            // 所以通视一断就改成"接近"，逼它挪位置找射界。留 1.5 秒缓冲避免来回抖。
            bool los = HasLineOfSight(_phys != null ? _phys.GetMuzzlePosition() : transform.position,
                                      LeadAimPoint(Target), Target);
            if (los) _losOkTime = Time.time;

            if (dist <= EffectiveFireRange && Time.time - _losOkTime < 1.5f)
            {
                StateText = "交战";
                return;
            }

            StateText = "接近";
            _goal = Target.transform.position;
            return;
        }

        // 没有敌人 → 去抢点（这是本作的核心玩法，不能只追着人打）
        StateText = captureZones ? "抢点" : "推进";
        _goal = ChooseGoal();
    }

    /// <summary>
    /// 选下一个要去的地方：最近的、本域能占的、还不属于自己队的资源点。
    /// 找不到就退回 objective，再退回地图中心。
    /// </summary>
    Vector3 ChooseGoal()
    {
        var mm = MatchManager.Instance;
        if (captureZones && mm != null && _self != null && _self.Def != null)
        {
            ZoneState best = null;
            float bestD = float.MaxValue;
            foreach (var z in mm.zones)
            {
                if (z == null || z.def == null) continue;
                if (z.def.domain != _self.Def.domain) continue;   // 船不抢陆地点，坦克不抢海点
                if (z.owner == _self.Team) continue;              // 自己队的不用抢
                var c = new Vector3(z.def.bounds.center.x, 0f, z.def.bounds.center.y);
                float d = Vector3.Distance(transform.position, c);
                if (d < bestD) { bestD = d; best = z; }
            }
            if (best != null)
                return new Vector3(best.def.bounds.center.x, 0f, best.def.bounds.center.y);
        }

        return objective != null ? objective.position : Vector3.zero;
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

            // 同域优先；飞机对所有目标一视同仁（本来就能打地面/海面），
            // 地面/海上单位只在近处才考虑跨域
            bool sameDomain = c.Def != null && _self.Def != null && c.Def.domain == _self.Def.domain;
            float score = dist * (sameDomain ? sameDomainBonus : 1f);
            bool isAir = _self.Def != null && _self.Def.domain == Domain.Air;
            if (!sameDomain && !isAir && dist > visionRange * 0.5f) continue;

            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    // ==================== 驾驶 ====================

    void Drive()
    {
        if (_phys == null) return;

        // 交战：地面/海上停住只转炮塔；飞机不能停，绕着目标飞
        if (StateText == "交战")
        {
            if (NavMode == NavGrid.NavMode.Air && Target != null)
            {
                SteerToward(Target.transform.position);
                _phys.aiThrottle = 1f;
                return;
            }
            _phys.aiThrottle = 0f;
            _phys.aiSteer = 0f;
            return;
        }

        // 飞机不用寻路，直接朝目标飞
        if (NavMode == NavGrid.NavMode.Air)
        {
            SteerToward(_goal);
            _phys.aiThrottle = 1f;
            return;
        }

        // 卡在建筑/障碍上超过 1.2 秒 → 侧向绕行（左右交替），并立刻重规划
        if (_phys.blockedTime > 1.2f)
        {
            _detourUntil = Time.time + 2.5f;
            _detourSign = -_detourSign;
            _phys.blockedTime = 0f;
            _nextRepath = 0f;
        }

        Vector3 target = _goal;
        if (Time.time < _detourUntil)
        {
            Vector3 toGoal = _goal - transform.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude > 1f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, toGoal.normalized);
                target = transform.position + toGoal.normalized * 25f + side * (30f * _detourSign);
            }
        }

        // 目标移动较多或到时间就重规划
        if (Time.time >= _nextRepath || Vector3.Distance(target, _lastTargetPos) > 60f)
        {
            _nextRepath = Time.time + repathInterval;
            _lastTargetPos = target;
            Repath(target);
        }

        SteerToward(CurrentWaypoint());
    }

    /// <summary>这个点是不是够深的可航行水面（炮艇判断目标能不能追上时用）。</summary>
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

        // 没有炮塔 / 炮塔转速为 0（飞机）就不做对准门槛，否则永远打不出来
        bool hasTurret = _phys.turretBone != null && _self.Def != null && _self.Def.turretTurnSpeed > 0.1f;
        if (hasTurret)
        {
            _phys.AimTurretAt(aim, _self.Def.turretTurnSpeed);
            if (_phys.TurretAngleError(aim) >= 4f) return;      // 还没对准
        }

        if (!HasLineOfSight(_phys.GetMuzzlePosition(), aim, Target)) return;   // 被地形挡住

        // 飞机对地面/海面目标：副武器（炸弹）在射程内就优先扔
        var sec = _self.Secondary;
        if (sec != null && _self.Def != null && _self.Def.domain == Domain.Air
            && Target.Def != null && Target.Def.domain != Domain.Air
            && dist <= sec.range)
        {
            _self.FireSecondary(aim);
            return;
        }

        if (dist > EffectiveFireRange) return;

        // 机炮打装甲目标几乎没伤害 —— 没副武器可用时干脆别浪费炮弹
        var primAmmo = _self.AmmoFor(_self.Weapon);
        if (VehicleCombat.AmmoMultiplierVs(primAmmo, Target.Def) < 0.3f) return;

        _self.Fire(aim);
    }
}
