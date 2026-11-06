using UnityEngine;

/// <summary>
/// 载具之间的碰撞。
/// 载具是 transform 移动的（不是刚体），不判就会互相穿过去。
///
/// 规则：
///   · 任一方是飞机 → 空中相撞，双方一起爆炸
///   · 都是地面/海上 → **不爆炸**：按重叠深度顶开 + 反向冲量弹开 + 轻微刮蹭伤害
///
/// 2026-11-05 修「卡在队友船体里掉一大截血」：
///   ① 旧版每 0.08 s 只推开 0.12 m —— 两条 18 m 的船叠在一起根本推不开，
///      于是每个 tick 都判定一次撞击，伤害叠着扣（实测几秒能掉掉小半管血）。
///      现在按**真实重叠深度**顶开（每次最多 1.6 m），几帧就分开。
///   ② 伤害加冷却：同一条船 `damageCooldown` 秒内只吃一次撞击伤害。
///   ③ 撞完给两边一个**反向冲量**（VehiclePhysics.ApplyKnockback）+ 前进速度压到 35%，
///      所以是"扣血 → 被弹开"，而不是"贴上去一直掉血"。
///   ④ 一对船只让 InstanceID 小的那边处理，避免两边各推一次、各扣一次。
/// </summary>
[RequireComponent(typeof(VehicleCombat))]
public class VehicleCollision : MonoBehaviour
{
    [Header("刮蹭伤害（地面/海上）")]
    [Tooltip("撞击伤害 = 相对速度 × 这个系数（很小）")]
    public float ramDamagePerSpeed = 1.2f;
    [Tooltip("单次撞击最多扣多少比例的血（防止撞一下就废）")]
    public float ramDamageCapRatio = 0.06f;
    [Tooltip("相对速度低于这个值不算撞击")]
    public float ramMinSpeed = 8f;
    [Tooltip("同一条船两次撞击伤害之间的冷却（秒）—— 防止卡在对方船体里连续掉血")]
    public float damageCooldown = 1.2f;

    [Header("顶开 / 弹开")]
    [Tooltip("每次最多顶开多少米（按重叠深度算，深度大也只顶这么多，避免瞬移）")]
    public float maxDePenetration = 1.6f;
    [Tooltip("弹开冲量 = 相对速度 × 该系数（米/秒）")]
    public float bouncePerSpeed = 0.85f;
    [Tooltip("弹开冲量上限（米/秒）")]
    public float bounceMax = 9f;
    [Tooltip("撞完保留多少前进速度（0.35 = 掉 65%）")]
    public float ramSpeedKeep = 0.35f;

    VehicleCombat _self;
    Collider _col;
    VehiclePhysics _phys;
    float _next;
    float _nextDamage;

    void Awake()
    {
        _self = GetComponent<VehicleCombat>();
        _col = GetComponent<Collider>();
        _phys = GetComponent<VehiclePhysics>();
    }

    void Update()
    {
        if (_self == null || !_self.Alive || _col == null) return;
        if (Time.time < _next) return;
        _next = Time.time + 0.08f;

        var b = _col.bounds;
        var hits = UnityEngine.Physics.OverlapBox(b.center, b.extents * 0.92f, transform.rotation,
                                                  ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h == null || h == _col) continue;
            var other = h.GetComponentInParent<VehicleCombat>();
            if (other == null || other == _self || !other.Alive) continue;
            if (!b.Intersects(h.bounds)) continue;

            bool selfAir = _self.Def != null && _self.Def.domain == Domain.Air;
            bool otherAir = other.Def != null && other.Def.domain == Domain.Air;

            // 空中相撞：一起炸
            if (selfAir || otherAir)
            {
                if (_self.Alive) _self.DestroyByEnvironment();
                if (other.Alive) other.DestroyByEnvironment();
                return;
            }

            // 一对船只让 InstanceID 小的那边处理：两边都跑这段的话，
            // 一次接触会被推两次、扣两次血（旧版就是这么双倍扣的）。
            if (GetInstanceID() > other.GetInstanceID()) continue;

            // ---------- 1. 法线 + 真实重叠深度 ----------
            Vector3 n = transform.position - other.transform.position;
            n.y = 0f;
            if (n.sqrMagnitude < 0.0001f) n = transform.right;
            n.Normalize();

            float depth = RadiusAlong(_col, n) + RadiusAlong(h, n)
                          - Mathf.Abs(Vector3.Dot(transform.position - other.transform.position, n));

            // ---------- 2. 顶开（按深度，不再固定 0.12 m） ----------
            if (depth > 0.001f)
            {
                float move = Mathf.Min(depth * 0.5f + 0.02f, maxDePenetration * 0.5f);
                transform.position += n * move;
                other.transform.position -= n * move;
            }

            // ---------- 3. 相对速度 / 伤害（带冷却） ----------
            var ov = other.GetComponent<VehiclePhysics>();
            float rel = 0f;
            if (_phys != null && ov != null) rel = (_phys.Velocity - ov.Velocity).magnitude;
            if (rel < ramMinSpeed) return;

            // 队友之间只顶开、弹开，**不扣血**（用户 2026-11-05 定：撞队友掉血不合理）
            bool friendly = other.team == _self.team;

            if (!friendly && Time.time >= _nextDamage)
            {
                _nextDamage = Time.time + damageCooldown;
                float dmg = Mathf.Min(rel * ramDamagePerSpeed,
                                      _self.maxHp / VehicleCombat.DamageScale * ramDamageCapRatio);
                other.TakeDamage(dmg, null, transform.position, _self);
                if (_self.Alive) _self.TakeDamage(dmg, null, other.transform.position, other);
            }

            // ---------- 4. 弹开：反向冲量 + 压前进速度 ----------
            // 这一步是用户要的"扣血之后弹开"：不弹的话两条船会一直贴在一起，
            // 顶开速度又慢，看着就是"卡在里面"。
            float bounce = Mathf.Min(rel * bouncePerSpeed, bounceMax);
            if (_phys != null)
            {
                _phys.ApplyKnockback(n * bounce);
                _phys.speed *= ramSpeedKeep;
            }
            if (ov != null)
            {
                ov.ApplyKnockback(-n * bounce);
                ov.speed *= ramSpeedKeep;
            }
            return;
        }
    }

    /// <summary>碰撞盒在某个水平方向上的"半径"（用世界 AABB 近似，够用且便宜）。</summary>
    static float RadiusAlong(Collider c, Vector3 n)
    {
        var s = c.bounds.size;
        return 0.5f * (Mathf.Abs(n.x) * s.x + Mathf.Abs(n.z) * s.z);
    }
}
