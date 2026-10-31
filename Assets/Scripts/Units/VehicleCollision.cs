using UnityEngine;

/// <summary>
/// 载具之间的碰撞。载具是 transform 移动的（不是刚体），不判就会互相穿过去 —— 两架飞机对着飞会直接穿过。
///
/// 规则：
///   · 任一方是飞机 → 空中相撞，双方一起爆炸
///   · 都是地面/海上 → 按相对速度互相撞击掉血（不做硬阻挡，避免 AI 顶牛卡死）
/// </summary>
[RequireComponent(typeof(VehicleCombat))]
public class VehicleCollision : MonoBehaviour
{
    [Tooltip("撞击伤害 = 相对速度 × 这个系数")]
    public float ramDamagePerSpeed = 26f;
    [Tooltip("相对速度低于这个值不算撞击")]
    public float ramMinSpeed = 6f;

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

            // 地面/海上相撞：按相对速度掉血
            var ov = other.GetComponent<VehiclePhysics>();
            float rel = 0f;
            if (_phys != null && ov != null) rel = (_phys.Velocity - ov.Velocity).magnitude;
            if (rel < ramMinSpeed) continue;

            float dmg = rel * ramDamagePerSpeed;
            other.TakeDamage(dmg, null, transform.position, _self);
            if (_self.Alive) _self.TakeDamage(dmg, null, other.transform.position, other);
            return;
        }
    }
}
