using UnityEngine;

/// <summary>
/// 载具受伤表现（2026-11-05 用户要求）：
///   · 血量 &lt; 50% → 冒烟
///   · 血量 &lt; 25% → 着火（火焰 + 火光），并且**移速下降**（VehiclePhysics.damageSpeedMul）
///
/// 只挂一个组件，粒子按需懒建（没受过伤的车不建任何粒子系统，省开销）。
/// 恢复机制用户说"之后说"，所以这里只做阈值触发，没做回血自动熄灭以外的逻辑。
/// </summary>
public class VehicleDamageFx : MonoBehaviour
{
    [Tooltip("低于这个血量比例开始冒烟")]
    public float smokeThreshold = 0.5f;
    [Tooltip("低于这个血量比例开始着火")]
    public float fireThreshold = 0.25f;
    [Tooltip("着火后的移速倍率")]
    public float heavySpeedMul = 0.6f;

    VehicleCombat _vc;
    VehiclePhysics _phys;
    ParticleSystem _smoke;
    ParticleSystem _fire;
    Light _fireLight;
    bool _built;
    float _airK = 1f;      // 飞机的特效放大系数（1 = 陆/海）

    void Awake()
    {
        _vc = GetComponent<VehicleCombat>();
        _phys = GetComponent<VehiclePhysics>();
    }

    void Update()
    {
        if (_vc == null) return;
        if (!_vc.Alive)
        {
            if (_smoke != null) SetRate(_smoke, 0f);
            if (_fire != null) SetRate(_fire, 0f);
            if (_fireLight != null) _fireLight.enabled = false;
            if (_phys != null) _phys.damageSpeedMul = 1f;
            return;
        }

        float ratio = _vc.maxHp > 0.01f ? Mathf.Clamp01(_vc.hp / _vc.maxHp) : 1f;
        bool wantSmoke = ratio < smokeThreshold;
        bool wantFire = ratio < fireThreshold;

        if ((wantSmoke || wantFire) && !_built) Build();

        if (_smoke != null)
        {
            float k = Mathf.InverseLerp(smokeThreshold, 0f, ratio);      // 0=刚到 50%，1=快死
            // 飞机烟要更浓：天空背景亮、距离远，稀薄的灰烟基本看不见
            float boost = _airK > 1f ? 1.7f : 1f;
            SetRate(_smoke, wantSmoke ? Mathf.Lerp(20f, 58f, k) * boost : 0f);
        }
        if (_fire != null)
        {
            float k = Mathf.InverseLerp(fireThreshold, 0f, ratio);
            SetRate(_fire, wantFire ? Mathf.Lerp(30f, 72f, k) : 0f);
        }
        if (_fireLight != null)
        {
            _fireLight.enabled = wantFire;
            if (wantFire) _fireLight.intensity = Mathf.Lerp(1.6f, 4.5f, Mathf.InverseLerp(fireThreshold, 0f, ratio))
                                                 * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 6f, 0.3f));
        }

        if (_phys != null) _phys.damageSpeedMul = wantFire ? heavySpeedMul : 1f;
    }

    static void SetRate(ParticleSystem ps, float rate)
    {
        var em = ps.emission;
        em.rateOverTime = rate;
        if (rate > 0.01f && !ps.isPlaying) ps.Play();
    }

    /// <summary>
    /// 冒烟/着火的挂点：**发动机舱**，不是车顶中心。
    ///
    /// 参考战争雷霆：坦克的烟和火从车体尾部（发动机甲板）冒出来，不是炮塔顶；
    /// 飞机从机头/发动机短舱；船从机舱（船体中后部）。
    /// 以前取的是包围盒正上方中心（正好落在炮塔上），看着像炮塔在烧 —— 不合理。
    /// </summary>
    Vector3 EngineAnchor()
    {
        Bounds b = default;
        bool any = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) return new Vector3(0f, 1.5f, -1.5f);

        Vector3 c = transform.InverseTransformPoint(b.center);
        Vector3 s = transform.InverseTransformVector(b.size);
        s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        Domain d = _vc != null && _vc.Def != null ? _vc.Def.domain : Domain.Land;

        // 沿车体长度方向偏到发动机那一头：
        //   陆：车尾（坦克发动机在后部）
        //   海：船尾机舱（偏多一点，舰炮/驾驶室在前半部）
        //   空：机尾喷口
        float along = d == Domain.Air ? -0.42f : (d == Domain.Sea ? -0.30f : -0.32f);

        // 高度：
        //   陆：包围盒顶往下 12%（发动机甲板，别顶到炮塔上）
        //   海：**不能取包围盒顶** —— 船的包围盒把驾驶室、桅杆、炮塔全算进去了，
        //       取顶会得到"离甲板 4 米"的悬空点，火就飘在船上方（用户看到的问题）。
        //       取船体中线略上一点 = 主甲板高度。
        //   空：机身中线（发动机在机身里）
        float y = d == Domain.Air ? c.y
                : d == Domain.Sea ? c.y + s.y * 0.06f
                                  : c.y + s.y * 0.5f - s.y * 0.12f;

        return new Vector3(c.x, y, c.z + along * s.z);
    }

    /// <summary>
    /// 给某个部位挂一个持续冒烟 / 着火的特效（部位被打坏时用）。
    /// 和整车的"重伤冒烟"分开：这个是挂在**具体模块**上的，打哪儿冒哪儿。
    /// </summary>
    public static ParticleSystem SpawnModuleFx(Transform parent, Vector3 localPos, bool fire, float rate)
    {
        var go = new GameObject(fire ? "ModuleFire" : "ModuleSmoke");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = fire ? new ParticleSystem.MinMaxCurve(0.5f, 0.9f)
                                  : new ParticleSystem.MinMaxCurve(1.6f, 3.0f);
        main.startSpeed = fire ? new ParticleSystem.MinMaxCurve(1.2f, 3.0f)
                               : new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize = fire ? new ParticleSystem.MinMaxCurve(1.0f, 2.0f)
                              : new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = fire ? -0.15f : 0f;
        main.maxParticles = 120;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = rate;

        var sh = ps.shape;
        sh.enabled = true;
        if (fire)
        {
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 20f;
            sh.radius = 0.5f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }
        else
        {
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.5f;
        }

        var g = new Gradient();
        if (fire)
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.78f, 0.28f), 0f),
                        new GradientColorKey(new Color(1f, 0.42f, 0.08f), 0.45f),
                        new GradientColorKey(new Color(0.45f, 0.10f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        else
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.30f, 0.29f, 0.28f), 0f),
                        new GradientColorKey(new Color(0.18f, 0.18f, 0.18f), 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.28f, 0.5f), new GradientAlphaKey(0f, 1f) });

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ExplosionFx.SmokeMat;    // 半透明：加色在亮背景上看不见
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        return ps;
    }

    /// <summary>第一次受伤才建粒子系统。</summary>
    void Build()
    {
        _built = true;

        Vector3 anchor = EngineAnchor();
        Vector3 local = anchor;

        // 飞机在天上、离镜头远，烟和火要放大一号才看得见
        float k = (_vc != null && _vc.Def != null && _vc.Def.domain == Domain.Air) ? 1.7f : 1f;
        _airK = k;

        _smoke = MakeSystem("DamageSmoke", local + Vector3.up * 0.15f, ExplosionFx.SmokeMat,
                            2.4f * k, 4.6f * k, 1.6f, 3.0f, 1.9f * k, 0f, true, false);
        // 火焰用**半透明**材质 + 饱和橙红：加色材质叠在亮背景上几乎看不见（实测只有烟没有火）。
        // 颗粒别太大 —— 这是"车在烧"，不是爆炸；要明显小于死亡时的火球（用户要求）。
        _fire = MakeSystem("DamageFire", local + Vector3.up * 0.10f, ExplosionFx.SmokeMat,
                           1.9f * k, 3.4f * k, 0.6f, 1.1f, 3.4f * k, -0.2f, false, true);

        // 火光
        var lightGo = new GameObject("DamageFireLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = local + Vector3.up * 0.5f;
        _fireLight = lightGo.AddComponent<Light>();
        _fireLight.type = LightType.Point;
        _fireLight.range = 14f;
        _fireLight.color = new Color(1f, 0.55f, 0.2f);
        _fireLight.intensity = 0f;
        _fireLight.enabled = false;
    }

    ParticleSystem MakeSystem(string name, Vector3 localPos, Material mat,
                              float sizeMin, float sizeMax, float lifeMin, float lifeMax,
                              float speed, float gravity, bool alphaBlend, bool upward)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // 跟着车走
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.maxParticles = 120;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.enabled = true;
        if (upward)
        {
            // 火焰：锥形朝上喷，才有"往上烧"的感觉
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 22f;
            sh.radius = 0.7f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }
        else
        {
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.6f;
        }

        // 颜色：烟由深到透明；火由亮黄到暗红
        var g = new Gradient();
        if (alphaBlend)
        {
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.34f, 0.32f, 0.30f), 0f),
                        new GradientColorKey(new Color(0.22f, 0.21f, 0.20f), 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.30f, 0.5f), new GradientAlphaKey(0f, 1f) });
        }
        else
        {
            // 火：饱和橙红，用半透明混合，保证在亮背景上也能看见
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.78f, 0.28f), 0f),
                        new GradientColorKey(new Color(1f, 0.42f, 0.08f), 0.45f),
                        new GradientColorKey(new Color(0.45f, 0.10f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.75f, 0.5f), new GradientAlphaKey(0f, 1f) });
        }
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.2f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        return ps;
    }
}
