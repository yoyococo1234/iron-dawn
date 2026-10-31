using UnityEngine;

/// <summary>
/// 载具战斗：血量、弹药、开火、受击、死亡。
///
/// 伤害公式（三层）：
///   实际伤害 = 武器基础伤害 × 弹药对目标类型的倍率 × 目标装甲受击倍率
///   装甲受击倍率按命中方向取（正面 0.70 / 侧面 0.90 / 背面 1.10）
///
/// 挂在载具根节点上，和 VehiclePhysics 同一个物体。
/// </summary>
[RequireComponent(typeof(VehiclePhysics))]
public class VehicleCombat : MonoBehaviour
{
    [Header("数据")]
    public VehicleDef def;

    [Header("运行时")]
    public float hp = 1f;
    public float maxHp = 1f;
    public int ammoIndex;
    public bool dead;

    [Header("表现")]
    public float deathExplosionRadius = 8f;

    [Header("阵营")]
    [Tooltip("0 = 我方，1 = 敌方（必须序列化，否则场景里丢失）")]
    public int team;

    [Header("运行时")]
    [Tooltip("最后打中自己的载具（击杀统计用）")]
    public VehicleCombat lastAttacker;

    VehiclePhysics _phys;
    float _nextFireTime;

    // ---- 命中率统计（验收/调参用，静态计数）----
    public static int ShotsFired;
    public static int ShotsHit;
    public static void ResetStats() { ShotsFired = 0; ShotsHit = 0; }
    public static float HitRate => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;

    public bool Alive => !dead && hp > 0f;
    public int Team => team;
    public VehicleDef Def => def;
    public VehiclePhysics Physics => _phys;

    /// <summary>主武器（第一把）。</summary>
    public WeaponDef Weapon => def != null && def.weapons != null && def.weapons.Length > 0 ? def.weapons[0] : null;

    /// <summary>副武器（第二把：炸弹 / 鱼雷）。没有就返回 null。</summary>
    public WeaponDef Secondary => def != null && def.weapons != null && def.weapons.Length > 1 ? def.weapons[1] : null;

    /// <summary>指定武器的当前弹药。</summary>
    public AmmoDef AmmoFor(WeaponDef w)
    {
        if (w == null || w.ammoTypes == null || w.ammoTypes.Length == 0) return null;
        return w.ammoTypes[Mathf.Clamp(ammoIndex, 0, w.ammoTypes.Length - 1)];
    }

    /// <summary>主武器当前弹药。</summary>
    public AmmoDef Ammo => AmmoFor(Weapon);

    public float ReloadProgress => Weapon == null ? 1f : Mathf.Clamp01(1f - (_nextFireTime - Time.time) / Mathf.Max(0.01f, Weapon.reloadTime));

    void Awake()
    {
        _phys = GetComponent<VehiclePhysics>();
        if (def == null) def = _phys != null ? _phys.def : null;
        if (def != null) { maxHp = def.maxHp; hp = maxHp; }
        EnsureHitCollider();
        ApplyTeamTint();
    }

    /// <summary>阵营色调：我方偏冷、敌方偏暖，一眼能分清敌我（不污染共享材质）。</summary>
    void ApplyTeamTint()
    {
        var tint = team == 0 ? new Color(0.74f, 0.87f, 1.00f) : new Color(1.00f, 0.74f, 0.66f);
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", tint);
            r.SetPropertyBlock(mpb);
        }
    }

    public void SetTeam(int t) => team = t;

    /// <summary>
    /// 给载具加一个可被击中的碰撞体。
    /// 必须按「网格局部包围盒」算，不能拿世界 AABB —— 车一转 yaw，世界 AABB 的长宽就互换，
    /// 命中盒会横过来，炮弹从车头穿过去都判定不到。
    /// </summary>
    void EnsureHitCollider()
    {
        if (GetComponent<Collider>() != null) return;

        bool has = false;
        Bounds local = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var e = mb.extents;
            var toRoot = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    mb.center.x + ((i & 1) == 0 ? -e.x : e.x),
                    mb.center.y + ((i & 2) == 0 ? -e.y : e.y),
                    mb.center.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = toRoot.MultiplyPoint3x4(corner);
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }

        if (!has) return;
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
    }

    // ==================== 开火 ====================

    public bool CanFire => Alive && Weapon != null && Time.time >= _nextFireTime;

    /// <summary>
    /// 朝世界坐标点开火（先转炮塔，对准了才打）。
    /// 没有炮塔 / 炮塔转速为 0 的载具（飞机、机炮）不做"对准"门槛 —— 否则永远打不出来。
    /// </summary>
    public bool FireAt(Vector3 worldPoint, float maxTurnDegPerSec = 999f)
    {
        if (!CanFire || _phys == null) return false;

        bool hasTurret = _phys.turretBone != null && def != null && def.turretTurnSpeed > 0.1f;
        if (hasTurret)
        {
            _phys.AimTurretAt(worldPoint, maxTurnDegPerSec);
            if (_phys.TurretAngleError(worldPoint) > 4f) return false;   // 还没对准
        }
        return Fire(worldPoint);
    }

    /// <summary>开火（主武器）。给了瞄准点就打向那个点（带仰角），否则朝炮口水平朝向。</summary>
    public bool Fire(Vector3? aimPoint = null) => FireWeapon(Weapon, aimPoint);

    /// <summary>打副武器（炸弹 / 鱼雷）。没有副武器返回 false。</summary>
    public bool FireSecondary(Vector3? aimPoint = null) => FireWeapon(Secondary, aimPoint);

    bool FireWeapon(WeaponDef w, Vector3? aimPoint)
    {
        var ammo = AmmoFor(w);
        if (w == null || ammo == null || !CanFire) return false;

        _nextFireTime = Time.time + w.reloadTime;

        Vector3 origin = _phys.GetMuzzlePosition();
        // 炮塔只能水平转，没有俯仰机构 —— 方向按"炮口 → 瞄准点"算，并且走抛物线
        Vector3 dir = aimPoint.HasValue
            ? BallisticDir(origin, aimPoint.Value, w.projectileSpeed)
            : _phys.GetMuzzleForward();

        // 起点推到车体碰撞盒外面，否则射线从自己盒子里出发，命中判定会出怪事
        origin = PushOutsideOwnCollider(origin, dir);
        origin += dir * 1f;

        // 弹速为 0 的武器（炸弹架）走瞬时命中，否则会变成 1 m/s 的爬行弹
        if (w.projectileSpeed <= 1f)
        {
            ShotsFired++;
            FireHitscan(origin, dir, w, ammo);
            return true;
        }

        var go = new GameObject($"Shell_{def.id}");
        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(dir);

        ShotsFired++;
        var shell = go.AddComponent<Projectile>();
        shell.Launch(dir, w.projectileSpeed, w.baseDamage, ammo, this, w.range);

        // 玩家开炮：镜头抖一下 + 炮口闪光
        ShakeCamera(0.55f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 25f;
        light.intensity = 6f;
        light.color = new Color(1f, 0.75f, 0.35f);
        Object.Destroy(light, 0.08f);

        return true;
    }

    /// <summary>瞬时命中（弹速 0 的武器用）：沿射线找第一个不是自己的目标。</summary>
    void FireHitscan(Vector3 origin, Vector3 dir, WeaponDef w, AmmoDef ammo)
    {
        float maxRange = Mathf.Max(1f, w.range);
        // 注意：本类有同名属性 Physics（VehiclePhysics），所以必须写全 UnityEngine.Physics
        var hits = UnityEngine.Physics.RaycastAll(origin, dir, maxRange, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var t = h.collider.GetComponentInParent<VehicleCombat>();
            if (t == this) continue;
            if (t != null && t.Alive) t.TakeDamage(w.baseDamage, ammo, origin, this);
            return;
        }
    }

    /// <summary>玩家车开炮时抖镜头（AI 开炮不抖，否则画面一直晃）。</summary>
    void ShakeCamera(float amount)
    {
        if (GetComponent<PlayerTankController>() == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.Shake(amount);
    }

    /// <summary>把出膛点推到自身碰撞盒外面（沿发射方向），最多推 20 m。</summary>
    Vector3 PushOutsideOwnCollider(Vector3 origin, Vector3 dir)
    {
        var col = GetComponent<Collider>();
        if (col == null) return origin;

        var b = col.bounds;
        for (int i = 0; i < 40; i++)
        {
            if (!b.Contains(origin)) return origin;
            origin += dir * 0.5f;
        }
        return origin;
    }

    /// <summary>
    /// 抛物线发射方向（低伸弹道）：给定初速和重力，解出让弹丸正好落到目标的仰角。
    /// 超出射程（无解）时退回直线，保证不会因为算不出来就哑火。
    /// </summary>
    public static Vector3 BallisticDir(Vector3 origin, Vector3 target, float speed)
    {
        Vector3 to = target - origin;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        float x = flat.magnitude;
        if (x < 0.5f) return (to).normalized;

        float y = to.y;
        float g = Projectile.Gravity;
        float v2 = speed * speed;

        float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
        if (disc < 0f) return to.normalized;            // 打不到，直线凑合

        float tanTheta = (v2 - Mathf.Sqrt(disc)) / (g * x);
        float angle = Mathf.Atan(tanTheta);

        return (flat.normalized * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
    }

    // ==================== 受击 ====================

    /// <summary>按方向计算装甲倍率并扣血。attacker 用于击杀统计。</summary>
    public void TakeDamage(float rawDamage, AmmoDef ammo, Vector3 hitFrom, VehicleCombat attacker = null)
    {
        if (!Alive) return;

        if (attacker != null && attacker != this && attacker.Team != team) lastAttacker = attacker;

        // 命中方向：从命中者指向自己 = 自己"被从哪个方向打"
        Vector3 toAttacker = hitFrom - transform.position;
        toAttacker.y = 0f;
        float hitAngle = Vector3.Angle(transform.forward, toAttacker.normalized);   // 0 = 正对

        float armorMul = def != null ? def.GetArmorMultiplier(hitAngle) : 1f;
        float ammoMul = AmmoMultiplier(ammo);
        float dmg = rawDamage * ammoMul * armorMul;

        hp = Mathf.Max(0f, hp - dmg);
        BattleEvents.RaiseVehicleDamaged(this);

        if (hp <= 0f) Die();
    }

    /// <summary>弹药对"这台载具"的倍率：按目标所在域取。</summary>
    float AmmoMultiplier(AmmoDef ammo) => AmmoMultiplierVs(ammo, def);

    /// <summary>某弹药打某种载具的倍率（AI 判断"这炮有没有用"用）。</summary>
    public static float AmmoMultiplierVs(AmmoDef ammo, VehicleDef targetDef)
    {
        if (ammo == null || targetDef == null) return 1f;
        switch (targetDef.domain)
        {
            case Domain.Sea: return ammo.vsSea;
            case Domain.Air: return ammo.vsAir;
            default: return targetDef.armorMode == ArmorMode.Uniform ? ammo.vsSoft : ammo.vsArmor;
        }
    }

    /// <summary>环境致死（进水报废、掉出地图等），没有击杀者。</summary>
    public void DestroyByEnvironment()
    {
        if (!Alive) return;
        lastAttacker = null;
        hp = 0f;
        Die();
    }

    void Die()
    {
        dead = true;
        hp = 0f;

        // 爆炸表现：一圈快速膨胀的球 + 火花
        var boom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boom.name = "Explosion";
        boom.transform.position = transform.position + Vector3.up * 1.5f;
        boom.transform.localScale = Vector3.one * 0.5f;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.55f, 0.15f);
        mat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f));
        mat.EnableKeyword("_EMISSION");
        boom.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(boom.GetComponent<Collider>());

        var l = boom.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = deathExplosionRadius * 4f;
        l.intensity = 12f;
        l.color = new Color(1f, 0.6f, 0.25f);

        var fx = boom.AddComponent<ExplosionFx>();
        fx.duration = 0.9f;
        fx.maxScale = deathExplosionRadius;

        // 车体消失
        gameObject.SetActive(false);
        BattleEvents.RaiseVehicleDestroyed(this, lastAttacker);
    }

    public void RespawnAt(Vector3 pos, float yaw)
    {
        dead = false;
        hp = maxHp;
        _nextFireTime = 0f;
        lastAttacker = null;
        if (_phys != null) _phys.ResetState();
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        gameObject.SetActive(true);
    }
}

/// <summary>爆炸膨胀动画，播完自销毁。</summary>
public class ExplosionFx : MonoBehaviour
{
    public float duration = 0.9f;
    public float maxScale = 8f;
    float _t;
    Light _light;

    void Start() => _light = GetComponent<Light>();

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / duration);
        transform.localScale = Vector3.one * Mathf.Lerp(0.5f, maxScale, k);
        if (_light != null) _light.intensity = Mathf.Lerp(12f, 0f, k);
        var r = GetComponent<Renderer>();
        if (r != null)
        {
            var m = r.material;
            var c = m.color;
            c.a = 1f - k;
            m.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}
