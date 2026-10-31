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
    public static bool DebugHits = false;      // 排查用：打印这一发命中了什么

    Vector3 _vel;
    float _damage;
    float _life;
    float _maxRange;      // 射程上限（0 = 不限）
    float _travelled;
    AmmoDef _ammo;
    VehicleCombat _owner;

    public void Launch(Vector3 dir, float speed, float damage, AmmoDef ammo, VehicleCombat owner, float maxRange = 0f)
    {
        _vel = dir.normalized * Mathf.Max(1f, speed);
        _damage = damage;
        _ammo = ammo;
        _owner = owner;
        _maxRange = Mathf.Max(0f, maxRange);
        // 生命 = 飞满射程所需时间 + 余量；没给射程就按 12 秒兜底
        _life = _maxRange > 1f ? _maxRange / Mathf.Max(1f, speed) + 0.5f : 12f;
        _travelled = 0f;
        transform.rotation = Quaternion.LookRotation(_vel);

        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time = 0.35f;
        tr.startWidth = 0.9f;
        tr.endWidth = 0.06f;
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
                if (DebugHits)
                    Debug.Log($"[Shell] {name} 命中 {h.collider.name} 距离 {h.distance:F1} m，目标={(target != null ? target.name : "无")}");
                if (target != null && target.Alive)
                {
                    target.TakeDamage(_damage, _ammo, transform.position, _owner);
                    VehicleCombat.ShotsHit++;
                }
                Destroy(gameObject);
                return;
            }
        }

        transform.position += stepVec;
        _travelled += step;
        transform.rotation = Quaternion.LookRotation(_vel);

        // 飞出射程就消失（否则炮弹会一直飞过整张图）
        if (_maxRange > 1f && _travelled >= _maxRange) { Destroy(gameObject); return; }

        if (DebugHits && Time.frameCount % 12 == 0)
            Debug.Log($"[Shell] {name} pos={transform.position:F1} 距地={transform.position.y - BattlefieldTerrain.SampleHeight(transform.position.x, transform.position.z):F1}");
    }
}
