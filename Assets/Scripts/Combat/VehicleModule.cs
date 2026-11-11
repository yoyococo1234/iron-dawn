using System.Collections.Generic;
using UnityEngine;

/// <summary>载具部位。</summary>
public enum ModuleKind { Engine, TurretRing, Gun, AmmoRack, Tracks, Fuel }

/// <summary>
/// 部位伤害系统（A 档：血条还在，额外加一层"打中哪个部位就坏哪个部位"）。
///
/// 为什么不用每个部位一个 Collider：载具是运行时拼出来的（车体 + 炮塔 + 炮管 + 履带 + 机枪），
/// 给每个部位加碰撞体会和命中判定、射线、性能都打架。这里用**数学盒体**在载具本地空间做包含测试，
/// 零物理开销，也不影响现有的单 BoxCollider 命中流程。
///
/// 命中点从 `VehicleCombat.TakeDamage(..., hitPoint)` 传进来（以前只传了"从哪边打来"）。
///
/// 效果（参考战争雷霆：坏的是功能，不是血条）：
///   发动机 → 速度降到 25%，冒浓烟
///   履带   → 速度降到 5%
///   炮塔环 → 炮塔转速 ×0.25
///   炮管   → 不能开火
///   弹药架 → 35% 概率殉爆（直接击毁），否则起火
///   油箱   → 起火，持续掉血
/// </summary>
public class VehicleModule : MonoBehaviour
{
    /// <summary>一个部位的"耐久"（原始伤害单位；120mm 炮一发是 300）。</summary>
    public float moduleHp = 160f;

    [Tooltip("炮管被打坏后多少秒自动修好。0 = 永不自动修复（只能去商店买模块修复）。" +
             "2026-11-09：玩家报「按左键完全没反应」，根因是炮管被一发打坏后永久哑火，HUD 只有一个小红块，玩家根本不知道。")]
    public float gunRepairSeconds = 15f;

    [Tooltip("炮管耐久倍率：别的部位按 moduleHp 算，炮管乘这个数，避免一发就被打哑。")]
    public float gunDurabilityMul = 3f;

    class Mod
    {
        public ModuleKind kind;
        public Vector3 center;     // 本地坐标
        public Vector3 half;       // 半尺寸
        public float integrity = 1f;
        public bool broken;
        public ParticleSystem fx;
        public float repairAt;     // 自动修复的时刻（0 = 不排队）
    }

    readonly List<Mod> _mods = new List<Mod>();
    VehicleCombat _vc;
    VehiclePhysics _phys;
    bool _built;
    bool _fuelFire;
    float _nextBurn;

    /// <summary>HUD 用：各部位是否完好。</summary>
    public bool IsBroken(ModuleKind k)
    {
        if (!_built) Build();
        foreach (var m in _mods) if (m.kind == k) return m.broken;
        return false;
    }

    /// <summary>商店的"模块修复包"：把所有部位修回完好，并熄掉着火。</summary>
    public void RepairAll()
    {
        if (!_built) Build();
        foreach (var m in _mods)
        {
            m.integrity = 1f;
            m.broken = false;
            m.repairAt = 0f;
            if (m.fx != null) m.fx.Stop();
        }
        _fuelFire = false;
        if (_vc != null) _vc.gunBroken = false;
    }

    // ==================== 炮管自动修复 ====================

    /// <summary>炮管是不是正在排队修复。</summary>
    public bool GunRepairing
    {
        get
        {
            if (!_built) Build();
            foreach (var m in _mods) if (m.kind == ModuleKind.Gun) return m.broken && m.repairAt > 0f;
            return false;
        }
    }

    /// <summary>炮管还有几秒修好（不修复时返回 -1）。</summary>
    public float GunRepairRemaining
    {
        get
        {
            if (!_built) Build();
            foreach (var m in _mods)
                if (m.kind == ModuleKind.Gun)
                    return (m.broken && m.repairAt > 0f) ? Mathf.Max(0f, m.repairAt - Time.time) : -1f;
            return -1f;
        }
    }

    void TickGunRepair()
    {
        if (_vc == null || !_vc.Alive) return;
        if (!_built) return;
        for (int i = 0; i < _mods.Count; i++)
        {
            var m = _mods[i];
            if (m.kind != ModuleKind.Gun || !m.broken || m.repairAt <= 0f) continue;
            if (Time.time < m.repairAt) continue;

            m.broken = false;
            m.integrity = 0.5f;      // 修好但只有半条命：还能再被打坏一次，比原版更经打
            m.repairAt = 0f;
            if (m.fx != null) m.fx.Stop();
            _vc.gunBroken = false;
            Debug.Log($"[Module] {name} 的 Gun 已修复（自动）");
        }
    }

    void Awake()
    {
        _vc = GetComponent<VehicleCombat>();
        _phys = GetComponent<VehiclePhysics>();
    }

    // ==================== 命中判定 ====================

    /// <summary>按命中点找部位并扣该部位的耐久。</summary>
    public void OnHit(Vector3 worldPoint, float rawDamage, AmmoDef ammo, bool penetrated)
    {
        if (_vc == null || !_vc.Alive) return;
        if (!_built) Build();
        if (_mods.Count == 0) return;

        Vector3 local = transform.InverseTransformPoint(worldPoint);

        Mod hit = null;
        foreach (var m in _mods)
            if (Contains(m, local)) { hit = m; break; }
        if (hit == null) return;              // 打在没部位的地方：只有血条伤害
        if (hit.broken) return;

        // **没打穿就伤不到内部模块** —— 只有履带这种挂在车体外面的才会被崩坏。
        if (!penetrated && hit.kind != ModuleKind.Tracks) return;

        // 穿甲弹打模块更狠一点（用弹药对装甲的倍率近似"穿深")
        float mul = ammo != null ? Mathf.Clamp(ammo.vsArmor, 0.4f, 1.6f) : 1f;
        // 炮管更耐打：原来 120mm 一发（300）就超过 moduleHp=160，炮直接哑火到死
        float hp = Mathf.Max(20f, moduleHp) * (hit.kind == ModuleKind.Gun ? Mathf.Max(1f, gunDurabilityMul) : 1f);
        hit.integrity -= rawDamage * mul / hp;

        if (hit.integrity <= 0f) Break(hit);
    }

    static bool Contains(Mod m, Vector3 p)
    {
        Vector3 d = p - m.center;
        return Mathf.Abs(d.x) <= m.half.x && Mathf.Abs(d.y) <= m.half.y && Mathf.Abs(d.z) <= m.half.z;
    }

    // ==================== 建部位 ====================

    void Build()
    {
        _built = true;
        if (_vc == null) return;

        // 注意：**不能**按 r.enabled 过滤 —— 出生保护期间渲染器是 10 Hz 闪烁的，
        // 正好在这一帧构建就会一个都找不到，然后永久空着（踩过）。
        Bounds b = default;
        bool any = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) { _built = false; return; }   // 没量到就下次再试

        Vector3 c = transform.InverseTransformPoint(b.center);
        Vector3 s = transform.InverseTransformVector(b.size);
        s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        float len = Mathf.Max(1f, s.z), wid = Mathf.Max(1f, s.x), hei = Mathf.Max(0.6f, s.y);

        Domain domain = _vc.Def != null ? _vc.Def.domain : Domain.Land;
        bool land = domain == Domain.Land;

        // 炮塔位置（本地坐标）：炮塔环 / 弹药架 / 炮管都挂在它附近
        Vector3 tp = c + Vector3.up * hei * 0.25f;
        if (_phys != null && _phys.turretBone != null)
            tp = transform.InverseTransformPoint(_phys.turretBone.position);

        // 检查顺序有讲究：先小后大，先炮塔后车体（不然炮塔上的点会被车体盒子先吃掉）
        // 炮管盒子放在**炮塔到炮口的中点**：原来用 tp + len*0.30，炮艇算出来在船头前方 8.7 m 处，
        // 打船头就判成"炮管被打坏"，而真正打炮管反而判不到。有炮口节点就按真实炮管长度算。
        Vector3 gunCenter, gunHalf;
        if (_phys != null && _phys.muzzleBone != null && _phys.turretBone != null)
        {
            Vector3 tLocal = transform.InverseTransformPoint(_phys.turretBone.position);
            Vector3 mLocal = transform.InverseTransformPoint(_phys.muzzleBone.position);
            gunCenter = (tLocal + mLocal) * 0.5f;
            float halfLen = Mathf.Max(1.2f, Vector3.Distance(tLocal, mLocal) * 0.5f);
            gunHalf = new Vector3(Mathf.Max(0.3f, wid * 0.10f), Mathf.Max(0.25f, hei * 0.10f), halfLen);
        }
        else
        {
            gunCenter = tp + new Vector3(0f, 0f, len * 0.18f);
            gunHalf = new Vector3(wid * 0.10f, hei * 0.12f, Mathf.Max(1.5f, len * 0.14f));
        }
        Add(ModuleKind.Gun, gunCenter, gunHalf);
        Add(ModuleKind.AmmoRack, tp + new Vector3(0f, -hei * 0.05f, -len * 0.12f), new Vector3(wid * 0.22f, hei * 0.20f, len * 0.10f));
        Add(ModuleKind.TurretRing, tp, new Vector3(wid * 0.30f, hei * 0.16f, len * 0.16f));
        Add(ModuleKind.Engine, new Vector3(c.x, c.y + hei * 0.10f, c.z - len * 0.32f), new Vector3(wid * 0.30f, hei * 0.30f, len * 0.16f));
        Add(ModuleKind.Fuel, new Vector3(c.x, c.y - hei * 0.05f, c.z - len * 0.10f), new Vector3(wid * 0.22f, hei * 0.22f, len * 0.09f));
        if (land)
        {
            Add(ModuleKind.Tracks, new Vector3(c.x + wid * 0.36f, c.y - hei * 0.28f, c.z), new Vector3(wid * 0.13f, hei * 0.22f, len * 0.42f));
            Add(ModuleKind.Tracks, new Vector3(c.x - wid * 0.36f, c.y - hei * 0.28f, c.z), new Vector3(wid * 0.13f, hei * 0.22f, len * 0.42f));
        }
    }

    void Add(ModuleKind kind, Vector3 center, Vector3 half)
    {
        _mods.Add(new Mod { kind = kind, center = center, half = half });
    }

    // ==================== 坏掉之后 ====================

    void Break(Mod m)
    {
        m.broken = true;

        switch (m.kind)
        {
            case ModuleKind.Engine:
                if (_phys != null) _phys.moduleSpeedMul = Mathf.Min(_phys.moduleSpeedMul, 0.25f);
                m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, false, 20f);
                break;

            case ModuleKind.Tracks:
                if (_phys != null) _phys.moduleSpeedMul = Mathf.Min(_phys.moduleSpeedMul, 0.05f);
                m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, false, 8f);
                break;

            case ModuleKind.TurretRing:
                if (_phys != null) _phys.turretSpeedMul = Mathf.Min(_phys.turretSpeedMul, 0.25f);
                m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, false, 10f);
                break;

            case ModuleKind.Gun:
                if (_vc != null) _vc.gunBroken = true;
                m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, false, 6f);
                if (gunRepairSeconds > 0f) m.repairAt = Time.time + gunRepairSeconds;
                break;

            case ModuleKind.AmmoRack:
                // 殉爆：直接击毁；没爆就起火
                if (Random.value < 0.35f)
                {
                    if (_vc != null) _vc.InstantKill();
                }
                else m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, true, 14f);
                break;

            case ModuleKind.Fuel:
                _fuelFire = true;
                m.fx = VehicleDamageFx.SpawnModuleFx(transform, m.center, true, 16f);
                break;
        }

        Debug.Log($"[Module] {name} 的 {m.kind} 被打坏");
    }

    void Update()
    {
        TickGunRepair();

        // 油箱起火：持续烧血（不给命中点，避免反复触发部位判定）
        if (!_fuelFire || _vc == null || !_vc.Alive) return;
        if (Time.time < _nextBurn) return;
        _nextBurn = Time.time + 0.5f;
        _vc.TakeDamage(3f, null, transform.position + transform.forward * 3f);
    }
}
