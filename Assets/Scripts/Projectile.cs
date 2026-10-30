using UnityEngine;

/// <summary>
/// 炮弹：沿直线飞行，逐帧做扫掠检测（不会因为太快穿模），命中载具就按三层公式结算伤害。
/// 不用 Rigidbody，纯射线扫掠，确定性强、2 km 地图上也不会漏判。
/// </summary>
public class Projectile : MonoBehaviour
{
    Vector3 _dir;
    float _speed = 150f;
    float _damage;
    float _life;
    AmmoDef _ammo;
    VehicleCombat _owner;

    public void Launch(Vector3 dir, float speed, float damage, AmmoDef ammo, VehicleCombat owner)
    {
        _dir = dir.normalized;
        _speed = Mathf.Max(1f, speed);
        _damage = damage;
        _ammo = ammo;
        _owner = owner;
        _life = Mathf.Clamp(3000f / _speed, 1.5f, 12f);   // 大约 3 km 射程
        transform.rotation = Quaternion.LookRotation(_dir);

        // 拖尾，方便看清弹道
        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time = 0.18f;
        tr.startWidth = 0.5f;
        tr.endWidth = 0.05f;
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        tr.endColor = new Color(1f, 0.4f, 0.1f, 0f);
    }

    void Update()
    {
        float step = _speed * Time.deltaTime;
        _life -= Time.deltaTime;
        if (_life <= 0f) { Destroy(gameObject); return; }

        var hits = Physics.RaycastAll(transform.position, _dir, step + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var target = h.collider.GetComponentInParent<VehicleCombat>();
                if (target == _owner) continue;              // 不打自己
                if (target != null)
                {
                    if (target.Alive) target.TakeDamage(_damage, _ammo, transform.position);
                    Destroy(gameObject);
                    return;
                }
                // 打到地形/建筑
                Destroy(gameObject);
                return;
            }
        }

        transform.position += _dir * step;
    }
}
