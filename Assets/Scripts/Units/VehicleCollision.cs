using UnityEngine;

/// <summary>
/// 载具之间的碰撞。
/// 载具是 transform 移动的（不是刚体），不判就会互相穿过去。
///
/// 规则（2026-10-31 修正）：
///   · 任一方是飞机 → 空中相撞，双方一起爆炸
///   · 都是地面/海上 → **不爆炸**，只做"互相推开 + 轻微刮蹭伤害"
///     （之前算成 相对速度 × 26，两车对撞直接互炸，像碰碰车）
/// </summary>
[RequireComponent(typeof(VehicleCombat))]
public class VehicleCollision : MonoBehaviour
{
    [Tooltip("撞击伤害 = 相对速度 × 这个系数（地面/海上，很小）")]
    public float ramDamagePerSpeed = 1.2f;
    [Tooltip("单次撞击最多扣多少比例的血（防止撞一下就废）")]
    public float ramDamageCapRatio = 0.06f;
    [Tooltip("相对速度低于这个值不算撞击")]
    public float ramMinSpeed = 8f;
    [Tooltip("每次检测互相推开的距离（米）")]
    public float separation = 0.12f;

    VehicleCombat _self;
    Collider _col;
    VehiclePhysics _phys;
    float _next;

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

            // 地面/海上：推开，不爆炸
            Vector3 push = transform.position - other.transform.position;
            push.y = 0f;
            if (push.sqrMagnitude > 0.01f)
            {
                push.Normalize();
                transform.position += push * separation;
                other.transform.position -= push * separation;
            }

            // 刮蹭伤害：小，且有上限
            var ov = other.GetComponent<VehiclePhysics>();
            float rel = 0f;
            if (_phys != null && ov != null) rel = (_phys.Velocity - ov.Velocity).magnitude;
            if (rel < ramMinSpeed) return;

            float dmg = Mathf.Min(rel * ramDamagePerSpeed, _self.maxHp * ramDamageCapRatio);
            other.TakeDamage(dmg, null, transform.position, _self);
            if (_self.Alive) _self.TakeDamage(dmg, null, other.transform.position, other);
            return;
        }
    }
}
