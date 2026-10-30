using UnityEngine;

/// <summary>
/// 炮弹：抛物线弹道（受重力），逐帧扫掠检测防穿模，命中载具按三层公式结算伤害。
///
/// 为什么不做直线：2 km 地图地形有起伏，直线弹道贴着地面飞 200 m 就会被土包挡住；
/// 抛物线能越过去，也更像坦克炮。
/// </summary>
public class Projectile : MonoBehaviour
{
    public const float Gravity = 9.8f;

    Vector3 _vel;
    float _damage;
    float _life;
    AmmoDef _ammo;
    VehicleCombat _owner;

    public void Launch(Vector3 dir, float speed, float damage, AmmoDef ammo, VehicleCombat owner)
    {
        _vel = dir.normalized * Mathf.Max(1f, speed);
        _damage = damage;
        _ammo = ammo;
        _owner = owner;
        _life = 12f;
        transform.rotation = Quaternion.LookRotation(_vel);

        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time = 0.22f;
        tr.startWidth = 0.6f;
        tr.endWidth = 0.05f;
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        tr.endColor = new Color(1f, 0.4f, 0.1f, 0f);
    }

    void Update()
    {
        _life -= Time.deltaTime;
        if (_life <= 0f) { Destroy(gameObject); return; }

        _vel += Vector3.down * Gravity * Time.deltaTime;

        Vector3 stepVec = _vel * Time.deltaTime;
        float step = stepVec.magnitude;
        if (step < 0.0001f) return;

        var hits = Physics.RaycastAll(transform.position, stepVec / step, step + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var target = h.collider.GetComponentInParent<VehicleCombat>();
                if (target == _owner) continue;                 // 不打自己
                if (target != null && target.Alive)
                    target.TakeDamage(_damage, _ammo, transform.position);
                Destroy(gameObject);
                return;
            }
        }

        transform.position += stepVec;
        transform.rotation = Quaternion.LookRotation(_vel);
    }
}
