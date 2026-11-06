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
    [Tooltip("当前使用第几把武器（Q 键切换）")]
    public int weaponIndex;
    public bool dead;

    [Header("表现")]
    public float deathExplosionRadius = 8f;

    [Header("阵营")]
    [Tooltip("0 = 我方，1 = 敌方（必须序列化，否则场景里丢失）")]
    public int team;

    [Header("出生保护")]
    [Tooltip("出生 / 复活后多少秒内免疫伤害。2026-11-05 用户要求取消（大家都打不到对方），默认 0")]
    public float spawnProtection = 0f;
    [Tooltip("复活后的无敌秒数。2026-11-06 用户要求：开场不要无敌，但死了复活要有 2 秒")]
    public float respawnProtection = 2f;

    /// <summary>无敌截止时间。闪烁提示在 Update 里做。</summary>
    float _invulnerableUntil;
    Renderer[] _blinkRenderers;
    bool[] _blinkWasEnabled;     // 闪烁前每台渲染器原本是否可见（不能一律恢复成 true！）
    bool _blinkHidden;

    [Header("运行时")]
    [Tooltip("最后打中自己的载具（击杀统计用）")]
    public VehicleCombat lastAttacker;

    VehiclePhysics _phys;
    VehicleModule _modules;
    float _nextFireTime;
    float _nextMgFireTime;      // 机枪独立装填计时
    float _nextHitVoice;        // "被击中"语音的冷却
    bool _torpedoLeft;          // 鱼雷左右管交替

    /// <summary>
    /// 血量总倍率（2026-11-05 用户要求"一台车大概 1 万血"）。
    /// 炮艇 1400 × 7.14 ≈ 9996，主战 1200 × 7.14 ≈ 8568，重装 2000 × 7.14 ≈ 14280。
    /// 伤害同步乘同样的倍数，所以**击杀所需时间不变**，只是数字变大、看着更爽。
    /// </summary>
    public const float HpScale = 7.14f;
    /// <summary>伤害总倍率，必须和 HpScale 相等，否则 TTK 会变。</summary>
    public const float DamageScale = 7.14f;

    // ---- 命中率统计（验收/调参用，静态计数）----
    public static int ShotsFired;
    public static int ShotsHit;
    public static void ResetStats() { ShotsFired = 0; ShotsHit = 0; }
    public static float HitRate => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;

    public bool Alive => !dead && hp > 0f;
    public int Team => team;
    public VehicleDef Def => def;
    public VehiclePhysics Physics => _phys;
    /// <summary>这台载具是不是玩家在开（命中反馈只给玩家弹）。</summary>
    public bool IsPlayer => GetComponent<PlayerTankController>() != null;

    /// <summary>当前武器（Q 键切换）。</summary>
    public WeaponDef Weapon
    {
        get
        {
            if (def == null || def.weapons == null || def.weapons.Length == 0) return null;
            return def.weapons[Mathf.Clamp(weaponIndex, 0, def.weapons.Length - 1)];
        }
    }

    /// <summary>
    /// 切到下一把武器（玩家按 Q）。
    /// **跳过独立挂点的机枪**：机枪是单独一套（V 键进入，见 PlayerTankController.MgMode），
    /// 不混在炮塔武器的循环里 —— 用户要求"选炮塔的时候切不到机枪"。
    /// </summary>
    public void CycleWeapon()
    {
        if (def == null || def.weapons == null || def.weapons.Length <= 1) return;
        for (int i = 1; i <= def.weapons.Length; i++)
        {
            int idx = (weaponIndex + i) % def.weapons.Length;
            var w = def.weapons[idx];
            if (w == null || w.deckMounted) continue;
            weaponIndex = idx;
            return;
        }
    }

    /// <summary>独立挂点武器（甲板/车顶机枪）。没有就返回 null。</summary>
    public WeaponDef DeckWeapon
    {
        get
        {
            if (def == null || def.weapons == null) return null;
            foreach (var w in def.weapons)
                if (w != null && w.deckMounted) return w;
            return null;
        }
    }

    /// <summary>
    /// 机枪开火（和当前 weaponIndex 无关，自己一套装填计时）。
    /// 不给瞄准点就沿枪管方向打 —— 机枪的准星就是枪管指向，不需要再反推。
    /// </summary>
    public bool FireDeckGunAt(Vector3? aimPoint = null)
    {
        var w = DeckWeapon;
        if (w == null || !Alive || _phys == null || !_phys.HasDeckGun) return false;
        if (Time.time < _nextMgFireTime) return false;
        var ammo = AmmoFor(w);
        if (ammo == null) return false;
        _nextMgFireTime = Time.time + w.reloadTime;
        return FireDeckGun(w, ammo, aimPoint);
    }

    /// <summary>副武器（第二把：炸弹 / 导弹）。AI 用。</summary>
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
        if (def != null) { maxHp = def.maxHp * HpScale; hp = maxHp; }
        EnsureHitCollider();
        ApplyTeamTint();

        // 出生保护 + 受伤表现（冒烟/着火）
        _invulnerableUntil = Time.time + spawnProtection;
        if (GetComponent<VehicleDamageFx>() == null) gameObject.AddComponent<VehicleDamageFx>();
        // 船：随浪起伏（从 Crest 采样水面高度，只动模型视觉）
        if (def != null && def.domain == Domain.Sea && GetComponent<SeaBuoyancy>() == null)
            gameObject.AddComponent<SeaBuoyancy>();

        // 部位伤害系统（A 档）
        _modules = GetComponent<VehicleModule>();
        if (_modules == null) _modules = gameObject.AddComponent<VehicleModule>();
    }

    /// <summary>直接击毁（弹药架殉爆这类部位效果用）。</summary>
    public void InstantKill(VehicleCombat attacker = null)
    {
        if (!Alive) return;
        if (attacker != null) lastAttacker = attacker;
        hp = 0f;
        Die();
    }

    /// <summary>
    /// 出生保护的闪烁提示：无敌期间慢速闪一下，玩家能看出来"现在打不动"。
    /// 频率别太高 —— 10 Hz 会闪到眼睛疼（用户反馈过），这里用 3 Hz、每次只藏 0.12 秒。
    /// </summary>
    void Update()
    {
        if (Time.time >= _invulnerableUntil)
        {
            if (_blinkHidden) SetRenderersVisible(true);
            return;
        }
        // 每 0.34 秒一个周期，周期里前 0.12 秒隐藏
        float phase = Mathf.Repeat(Time.time, 0.34f);
        bool hide = phase < 0.12f;
        if (hide != _blinkHidden) SetRenderersVisible(!hide);
    }

    /// <summary>
    /// 恢复时**只恢复原本就可见的**渲染器。
    /// 之前一律设成 true，把程序化履带故意隐藏的原模型履带也打开了 ——
    /// 表现就是"每台坦克都多出一套莫名其妙的履带"（用户报的 bug）。
    /// </summary>
    void SetRenderersVisible(bool on)
    {
        if (_blinkRenderers == null || _blinkRenderers.Length == 0)
        {
            _blinkRenderers = GetComponentsInChildren<Renderer>(true);
            _blinkWasEnabled = new bool[_blinkRenderers.Length];
            for (int i = 0; i < _blinkRenderers.Length; i++)
                _blinkWasEnabled[i] = _blinkRenderers[i] != null && _blinkRenderers[i].enabled;
        }

        _blinkHidden = !on;
        for (int i = 0; i < _blinkRenderers.Length; i++)
        {
            var r = _blinkRenderers[i];
            if (r == null) continue;
            r.enabled = on ? _blinkWasEnabled[i] : false;
        }
    }

    /// <summary>阵营色调：我方偏冷、敌方偏暖，一眼能分清敌我（不污染共享材质）。</summary>
    void ApplyTeamTint()
    {
        var tint = team == 0 ? new Color(0.74f, 0.87f, 1.00f) : new Color(1.00f, 0.74f, 0.66f);
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            // 机枪零件（MG_*）保持枪械本色：阵营色调是给车体/船体做敌我识别的，
            // 套到枪械金属上会把黑铁染成浅蓝白（MaterialPropertyBlock 直接替换 _Color，不是相乘）。
            if (r.gameObject.name.StartsWith("MG_")) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", tint);
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>设阵营。注意 Awake 时 team 还是 0，所以这里要重新上色。</summary>
    public void SetTeam(int t)
    {
        team = t;
        ApplyTeamTint();
    }

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

    public bool CanFire => Alive && !gunBroken && Weapon != null && Time.time >= _nextFireTime && HasAmmoFor(Weapon);

    // ---------------- 备弹（飞机挂架上的导弹是有数的） ----------------

    [System.NonSerialized] int[] _ammoLeft;

    void InitAmmo()
    {
        if (def == null || def.weapons == null) return;
        if (_ammoLeft != null && _ammoLeft.Length == def.weapons.Length) return;
        _ammoLeft = new int[def.weapons.Length];
        for (int i = 0; i < def.weapons.Length; i++)
        {
            var w = def.weapons[i];
            _ammoLeft[i] = (w != null && w.ammoCapacity > 0) ? w.ammoCapacity : int.MaxValue;
        }
    }

    bool HasAmmoFor(WeaponDef w)
    {
        if (w == null) return false;
        if (w.ammoCapacity <= 0) return true;          // 无限弹
        InitAmmo();
        if (def == null || def.weapons == null) return true;
        int i = System.Array.IndexOf(def.weapons, w);
        return i < 0 || _ammoLeft[i] > 0;
    }

    /// <summary>当前武器还剩几发（无限弹返回 -1）。HUD 用。</summary>
    public int CurrentAmmoLeft
    {
        get
        {
            var w = Weapon;
            if (w == null || w.ammoCapacity <= 0) return -1;
            InitAmmo();
            int i = def != null && def.weapons != null ? System.Array.IndexOf(def.weapons, w) : -1;
            return (i >= 0 && _ammoLeft != null) ? _ammoLeft[i] : w.ammoCapacity;
        }
    }

    /// <summary>当前武器的总备弹（无限弹返回 0）。</summary>
    public int CurrentAmmoCapacity => Weapon != null ? Weapon.ammoCapacity : 0;

    /// <summary>炮管/炮闩被打坏（部位伤害系统）。</summary>
    [System.NonSerialized] public bool gunBroken;

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
            // 机枪跟着炮塔转但枪口更灵活，容差放宽一点，否则扫射时经常被"没对准"卡住
            float tol = (Weapon != null && Weapon.deckMounted) ? 10f : 4f;
            if (_phys.TurretAngleError(worldPoint) > tol) return false;   // 还没对准
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

        // 扣一发（有备弹上限的武器）
        if (w.ammoCapacity > 0 && def != null && def.weapons != null)
        {
            InitAmmo();
            int wi = System.Array.IndexOf(def.weapons, w);
            if (wi >= 0 && _ammoLeft[wi] > 0) _ammoLeft[wi]--;
            // 机翼上那枚弹跟着消失
            var ws = GetComponent<WingStores>();
            if (ws != null) ws.ConsumeOne();
        }

        // ---------- 鱼雷：水下定深直航，不吃重力 ----------
        if (w.isTorpedo) return FireTorpedo(w, ammo, aimPoint);

        // ---------- 甲板/车顶机枪：独立挂点，仰角不受炮塔限制，瞬时命中 ----------
        if (w.deckMounted && _phys != null && _phys.HasDeckGun) return FireDeckGun(w, ammo, aimPoint);

        Vector3 origin = _phys.GetMuzzlePosition();
        // 炮塔只能水平转，没有俯仰机构 —— 方向按"炮口 → 瞄准点"算，并且走抛物线
        Vector3 dir = aimPoint.HasValue
            ? BallisticDir(origin, aimPoint.Value, w.projectileSpeed)
            : _phys.GetMuzzleForward();

        // 起点推到车体碰撞盒外面，否则射线从自己盒子里出发，命中判定会出怪事
        origin = PushOutsideOwnCollider(origin, dir);
        origin += dir * 1f;

        // 开火音效：按武器威力分主炮 / 机炮；音高随机一点，连射时不至于像复读机
        bool isMainGun = w.baseDamage >= 90f;
        Sfx.PlayAt(isMainGun ? Sfx.CannonFire : Sfx.MgFire, origin,
                   isMainGun ? 1f : 0.75f,
                   isMainGun ? Random.Range(0.94f, 1.06f) : Random.Range(0.92f, 1.10f),
                   isMainGun ? 25f : 8f,
                   isMainGun ? 900f : 300f);

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

        // 弹体模型（Lux3D 生成）：穿甲/高爆/破甲弹、空空/反坦克导弹各有自己的模型；
        // 没配模型的弹药就保持"只有曳光"的老样子。
        if (ammo != null) WeaponVisual.Attach(go.transform, ammo.id);

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

    /// <summary>鱼雷发射：从**可见的发射管口**出去（没有管子的载具退回老逻辑）。</summary>
    bool FireTorpedo(WeaponDef w, AmmoDef ammo, Vector3? aimPoint)
    {
        if (_phys == null) return false;

        float water = _phys.WaterLevel;
        Vector3 origin;
        Vector3 dir;

        if (_phys.HasTorpedoTubes)
        {
            // 左右交替发射，看着像两条管子轮流放雷
            _torpedoLeft = !_torpedoLeft;
            var tube = _torpedoLeft ? _phys.torpedoTubeL : _phys.torpedoTubeR;
            if (tube == null) tube = _phys.torpedoTubeL != null ? _phys.torpedoTubeL : _phys.torpedoTubeR;

            origin = tube.position;
            dir = tube.forward;
            if (aimPoint.HasValue)
            {
                Vector3 to = aimPoint.Value - origin;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f) dir = to.normalized;
            }
            origin += dir * 0.8f;
        }
        else
        {
            origin = _phys.GetMuzzlePosition();
            if (aimPoint.HasValue)
            {
                Vector3 to = aimPoint.Value - origin;
                to.y = 0f;
                dir = to.sqrMagnitude > 0.04f ? to.normalized : transform.forward;
            }
            else dir = transform.forward;

            // 没有发射管的老逻辑：压到水线附近，别从炮塔顶上飞出去
            origin = new Vector3(origin.x, Mathf.Min(origin.y, water - 0.4f), origin.z);
            origin += dir * 2.5f;
        }

        // 管口在水面以上：压到水面下一点点，鱼雷才是"入水"而不是从天上掉下来
        origin.y = Mathf.Min(origin.y, water - 0.15f);

        ShotsFired++;
        var go = new GameObject($"Torpedo_{def.id}");
        var torp = go.AddComponent<Torpedo>();
        torp.Launch(origin, dir, w.projectileSpeed, w.baseDamage, ammo, this,
                    w.range, w.torpedoDepth, water);

        // 出管：低沉的水下闷响
        Sfx.PlayAt(Sfx.CannonFire, origin, 0.7f, Random.Range(0.72f, 0.82f), 18f, 500f);
        ShakeCamera(0.35f);
        return true;
    }

    /// <summary>
    /// 甲板/车顶机枪：从独立挂点射出，瞬时命中（连射 0.12 秒一发，用抛射物会刷一堆 GameObject），
    /// 画一条曳光 + 命中火花。
    /// </summary>
    bool FireDeckGun(WeaponDef w, AmmoDef ammo, Vector3? aimPoint)
    {
        Vector3 origin = _phys.GetDeckGunMuzzle();
        Vector3 dir = aimPoint.HasValue
            ? (aimPoint.Value - origin).normalized
            : _phys.GetDeckGunForward();
        if (dir.sqrMagnitude < 0.001f) dir = _phys.GetDeckGunForward();

        ShotsFired++;
        float maxRange = Mathf.Max(10f, w.range);
        Vector3 endPoint = origin + dir * maxRange;

        var hits = UnityEngine.Physics.RaycastAll(origin, dir, maxRange, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var t = h.collider.GetComponentInParent<VehicleCombat>();
                if (t == this) continue;                       // 不打自己
                endPoint = h.point;
                if (t != null && t.Alive)
                {
                    var res = t.TakeHit(w.baseDamage, ammo, origin, this, h.point, h.normal);
                    if (IsPlayer) MatchHUD.ShowHitFeedback(res);
                    ShotsHit++;
                }
                SpawnImpactSpark(h.point);
                break;
            }
        }

        SpawnTracer(origin, endPoint);

        // 机枪连射：短促、音高随机，不抖镜头（否则连射没法瞄）
        Sfx.PlayAt(Sfx.MgFire, origin, 0.5f, Random.Range(0.88f, 1.14f), 8f, 300f);
        return true;
    }

    /// <summary>曳光弹：一条很快消失的亮线。</summary>
    void SpawnTracer(Vector3 a, Vector3 b)
    {
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startWidth = 0.09f;
        lr.endWidth = 0.03f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = new Color(1f, 0.92f, 0.6f, 0.95f);
        lr.endColor = new Color(1f, 0.7f, 0.3f, 0.15f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go, 0.06f);
    }

    /// <summary>命中火花：一个很小的亮点，一闪就没。</summary>
    void SpawnImpactSpark(Vector3 p)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ImpactSpark";
        go.transform.position = p;
        go.transform.localScale = Vector3.one * 0.22f;
        Object.Destroy(go.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.85f, 0.5f);
        mat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f));
        mat.EnableKeyword("_EMISSION");
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(go, 0.09f);
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
            if (t != null && t.Alive)
            {
                var res = t.TakeHit(w.baseDamage, ammo, origin, this, h.point, h.normal);
                if (IsPlayer) MatchHUD.ShowHitFeedback(res);
            }
            return;
        }
    }

    /// <summary>玩家车开炮时抖镜头（AI 开炮不抖，否则画面一直晃）。按伤害缩放：机炮几乎不抖，主炮才明显。</summary>
    void ShakeCamera(float amount)
    {
        if (GetComponent<PlayerTankController>() == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var cf = cam.GetComponent<CameraFollow>();
        if (cf == null) return;

        // 60 伤害的机炮不该和 300 伤害的主炮一样抖 —— 否则连射时机枪画面根本没法瞄
        // （伤害已经被 DamageScale 放大过，这里先换算回原始量级再算比例）
        float dmg = (Weapon != null ? Weapon.baseDamage : 300f) * DamageScale;
        float scale = Mathf.Lerp(0.06f, 1f, Mathf.InverseLerp(40f * DamageScale, 320f * DamageScale, dmg));
        cf.Shake(amount * scale);
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
    public void TakeDamage(float rawDamage, AmmoDef ammo, Vector3 hitFrom, VehicleCombat attacker = null,
                           Vector3? hitPoint = null)
    {
        TakeHit(rawDamage, ammo, hitFrom, attacker, hitPoint, null);
    }

    /// <summary>
    /// 命中结算（带穿透判定）。返回这一发是击穿 / 未击穿 / 跳弹，给命中反馈用。
    /// </summary>
    public HitResult TakeHit(float rawDamage, AmmoDef ammo, Vector3 hitFrom, VehicleCombat attacker,
                             Vector3? hitPoint, Vector3? hitNormal)
    {
        if (!Alive) return HitResult.None;
        if (Time.time < _invulnerableUntil) return HitResult.None;   // 出生保护

        if (attacker != null && attacker != this && attacker.Team != team) lastAttacker = attacker;

        // 命中方向：从命中者指向自己 = 自己"被从哪个方向打"
        Vector3 toAttacker = hitFrom - transform.position;
        toAttacker.y = 0f;
        float hitAngle = Vector3.Angle(transform.forward, toAttacker.normalized);   // 0 = 正对

        // ---------- 穿透判定 ----------
        // 有穿深数据 + 有命中面法线 → 走"穿深 vs 有效厚度"；否则退回老的方位倍率。
        bool hasPenData = ammo != null && ammo.penetration > 0f && hitNormal.HasValue && hitPoint.HasValue;
        HitResult result = HitResult.Penetrated;
        float penMul = 1f;

        if (hasPenData)
        {
            float baseArmor = ArmorThicknessAt(hitAngle);
            Vector3 shotDir = (hitPoint.Value - hitFrom).normalized;
            // 入射角：命中面法线 与 弹道反向 的夹角（0 = 垂直命中）
            float incidence = Vector3.Angle(hitNormal.Value, -shotDir);
            float cos = Mathf.Max(0.2f, Mathf.Cos(incidence * Mathf.Deg2Rad));
            float effective = baseArmor / cos;

            float pen = ammo.penetration;
            if (!ammo.isChemical && ammo.penFalloffPer100m > 0f)
                pen -= ammo.penFalloffPer100m * (Vector3.Distance(hitFrom, hitPoint.Value) / 100f);
            pen = Mathf.Max(0f, pen);

            if (incidence > 70f && pen < effective)
            {
                result = HitResult.Ricochet;
                penMul = 0f;
            }
            else if (pen < effective)
            {
                result = HitResult.NonPenetrating;
                // 高爆弹：没打穿也能炸掉一点；穿甲弹没打穿基本白搭
                penMul = ammo.penetration < 100f ? 0.35f : 0.10f;
            }
        }
        else
        {
            // 没有穿深数据的武器（炸弹/鱼雷/防空弹）：沿用老的方位减伤
            penMul = def != null ? def.GetArmorMultiplier(hitAngle) : 1f;
        }

        float ammoMul = AmmoMultiplier(ammo);
        float roll = Random.Range(0.9f, 1.1f);          // 伤害浮动 ±10%（WoT 是 ±25%，太看运气）
        float dmg = rawDamage * DamageScale * ammoMul * penMul * roll;

        hp = Mathf.Max(0f, hp - dmg);
        BattleEvents.RaiseVehicleDamaged(this);

        // 部位伤害：**只有击穿才伤内部模块**；未击穿只能打坏履带这类外部件
        if (hitPoint.HasValue && _modules != null)
            _modules.OnHit(hitPoint.Value, rawDamage, ammo, result == HitResult.Penetrated);

        // 命中音效：按穿透结果放不同的声音
        // 机枪/机炮打中装甲是"叮"的金属脆响，不是炮弹的"砰"
        bool smallCal = ammo != null && (ammo.id == "aa" || ammo.id == "cannon");
        string hitClip = smallCal ? Sfx.HitMg
                       : result == HitResult.Ricochet ? Sfx.HitRicochet
                       : result == HitResult.NonPenetrating ? Sfx.HitNonPen
                       : Sfx.HitPen;

        if (hp > 0f)
        {
            if (IsPlayer)
            {
                // **只有"我被打"是永远听得见的**：2D 音源，不吃距离衰减。
                // （飞机在高空扫射时，3D 音源放在命中点会被距离衰减吃掉，用户报过这个问题）
                Sfx.Play2D(hitClip, 1f, Random.Range(0.95f, 1.05f));
            }
            else if (hitPoint.HasValue)
            {
                // 其余一律 3D + 正常距离衰减：我打别人、别人打别人都一样
                Sfx.PlayAt(hitClip, hitPoint.Value, 0.9f, Random.Range(0.9f, 1.1f), 20f, 400f);
            }
        }

        // 语音播报（豆包音频生成，每类随机）：
        //   我开炮打中别人 → 跳弹 / 没打穿 / 打穿了 都响；打死了 → 击杀
        //   我被打 → 被击中（2.5 秒冷却，免得机枪扫射刷屏）
        //   **飞机只播"击杀"和"被击中"**（用户要求：空战不看穿透结果）
        //   **军舰不播"车体受损"**（那是坦克说法）
        bool air = def != null && def.domain == Domain.Air;
        bool sea = def != null && def.domain == Domain.Sea;

        if (attacker != null && attacker.IsPlayer && attacker.Team != team)
        {
            if (hp <= 0f) Sfx.PlayRandomVo(Sfx.VoKill, 1f);
            else if (air) { /* 飞机不播穿透类 */ }
            else if (result == HitResult.Ricochet) Sfx.PlayRandomVo(Sfx.VoRicochet);
            else if (result == HitResult.NonPenetrating) Sfx.PlayRandomVo(Sfx.VoNonPen);
            else Sfx.PlayRandomVo(Sfx.VoPen);
        }
        else if (IsPlayer && Time.time >= _nextHitVoice)
        {
            _nextHitVoice = Time.time + 2.5f;
            // 军舰和飞机都不用"车体受损"（那是坦克的说法），只播另外两句
            Sfx.PlayRandomVo((sea || air) ? Sfx.VoHitNoHull : Sfx.VoHit, 0.95f);
        }

        if (hp <= 0f) Die();
        return result;
    }

    /// <summary>按被击方位取基础装甲厚度（mm）。</summary>
    float ArmorThicknessAt(float hitAngleDeg)
    {
        if (def == null) return 60f;
        float a = Mathf.Repeat(hitAngleDeg, 360f);
        if (a <= 45f) return def.armorThicknessFront;
        if (a <= 135f) return def.armorThicknessSide;
        return def.armorThicknessRear;
    }

    /// <summary>
    /// 这发弹在某个距离上大概能不能打穿目标的正面（AI 用；不算角度）。
    /// </summary>
    public static bool CanPenetrate(AmmoDef ammo, VehicleDef target, float distance = 300f)
    {
        if (ammo == null || target == null) return false;
        if (ammo.penetration <= 0f) return true;          // 不参与判定的武器
        float pen = ammo.penetration;
        if (!ammo.isChemical && ammo.penFalloffPer100m > 0f)
            pen -= ammo.penFalloffPer100m * (distance / 100f);
        return pen >= target.armorThicknessFront * 0.9f;
    }

    /// <summary>
    /// 按目标**当前朝向**估算能不能打穿（AI 用）。
    /// 从侧面/后面打就按侧面/后面厚度算 —— 不然 AI 会以为"打重坦永远打不穿"而干脆不开火。
    /// </summary>
    public static bool CanPenetrateFrom(AmmoDef ammo, VehicleCombat target, Vector3 fromPos, float distance)
    {
        if (ammo == null || target == null || target.Def == null) return false;
        if (ammo.penetration <= 0f) return true;

        float ang = Vector3.Angle(target.transform.forward, fromPos - target.transform.position);
        var d = target.Def;
        float armor = ang <= 45f ? d.armorThicknessFront
                    : ang <= 135f ? d.armorThicknessSide
                                  : d.armorThicknessRear;

        float pen = ammo.penetration;
        if (!ammo.isChemical && ammo.penFalloffPer100m > 0f)
            pen -= ammo.penFalloffPer100m * (distance / 100f);
        return pen >= armor * 0.9f;
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

        // 爆炸音效（车体下面要 SetActive(false)，所以声音要单独在世界上放）
        Sfx.PlayAt(Sfx.Explosion, transform.position + Vector3.up * 1.5f, 1f,
                   Random.Range(0.92f, 1.06f), 30f, 1200f);

        // 爆炸表现：分层粒子（闪光 → 火球 → 黑烟 → 火星 → 冲击波环），见 ExplosionFx
        ExplosionFx.Spawn(transform.position + Vector3.up * 1.2f, deathExplosionRadius);

        // 炸散：把车体/炮塔/履带的网格切成碎块飞出去。
        // 必须在 SetActive(false) 之前做 —— 碎片要读渲染器的网格和材质。
        VehicleDebris.Explode(this);

        // 车体消失
        gameObject.SetActive(false);
        BattleEvents.RaiseVehicleDestroyed(this, lastAttacker);
    }

    public void RespawnAt(Vector3 pos, float yaw)
    {
        dead = false;
        hp = maxHp;
        _nextFireTime = 0f;
        _ammoLeft = null;          // 复活时备弹补满（导弹重新挂上）
        var ws = GetComponent<WingStores>();
        if (ws != null) ws.Reset();
        lastAttacker = null;
        _invulnerableUntil = Time.time + respawnProtection;   // 复活 2 秒无敌
        if (_phys != null) _phys.ResetState();
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        gameObject.SetActive(true);
    }
}

/// <summary>一发命中的结算结果（给命中反馈用）。</summary>
public enum HitResult
{
    None = 0,          // 没做判定（无敌、已死等）
    Penetrated,        // 击穿
    NonPenetrating,    // 未击穿
    Ricochet           // 跳弹
}
