using UnityEngine;

/// <summary>
/// 爆炸特效（纯程序生成，不依赖任何贴图素材）。
///
/// 以前死亡/命中只有一个"膨胀的球"，就是用户说的"圆圈的爆炸特效"——难看。
/// 现在的做法照 Unity 官方那篇 A Simple Explosion 的思路分层：
///   · 球状发射器 + **一次性 burst**（不是持续喷）
///   · 尺寸随生命周期变化、颜色先亮后暗（加色材质里"变黑"= 变透明）
///   · Limit Velocity 加阻尼，模拟空气阻力（否则火焰像在真空里直线飞）
///   · 尺寸/旋转随机，避免看出是同一张图重复
/// 再叠上真实的爆炸层次：闪光 → 火球 → 黑烟 → 火星 → 地面冲击波环。
///
/// 贴图也是现算的（径向渐变），所以整个效果零素材依赖。
/// </summary>
public static class ExplosionFx
{
    static Texture2D _softDot;
    static Material _addMat;
    static Material _alphaMat;

    /// <summary>给别的特效（车辆重伤冒烟/着火）复用的加色材质。</summary>
    public static Material FireMat => AddMat();
    /// <summary>给别的特效复用的半透明材质（烟）。</summary>
    public static Material SmokeMat => AlphaMat();
    /// <summary>软圆点贴图。</summary>
    public static Texture2D DotTex => SoftDot();

    /// <summary>爆炸主入口。scale ≈ 半径（米）。</summary>
    public static void Spawn(Vector3 pos, float scale = 6f)
    {
        var root = new GameObject("ExplosionFx");
        root.transform.position = pos;

        float s = Mathf.Clamp(scale, 2f, 14f);

        // 1) 闪光：极短、很亮，先把画面"打白"一下
        MakeFlash(root.transform, s);
        // 2) 火球
        MakeFireball(root.transform, s);
        // 3) 黑烟（慢、久、往上飘）
        MakeSmoke(root.transform, s);
        // 4) 火星（快、带重力、飞得远）
        MakeSparks(root.transform, s);
        // 5) 地面/水面冲击波环
        MakeShockwave(root.transform, s);

        Object.Destroy(root, 4.5f);
    }

    // ---------------- 各层 ----------------

    static void MakeFlash(Transform parent, float s)
    {
        var go = NewSystem(parent, "Flash", AddMat());
        var main = go.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(s * 0.30f, s * 0.55f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 8;

        Burst(go, 2);
        Shape(go, s * 0.2f);

        // 闪光别用纯白：纯白叠上去就是一坨奶油雾，暖黄色才像"火"
        ColorOver(go, Gradient(
            new Color(1f, 0.92f, 0.55f, 1f),
            new Color(1f, 0.72f, 0.28f, 0.55f),
            new Color(1f, 0.45f, 0.10f, 0f)));

        var size = go.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.25f));

        var light = go.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = s * 6f;
        light.intensity = 9f;
        light.color = new Color(1f, 0.78f, 0.45f);
        go.gameObject.AddComponent<ExplosionLightFade>().life = 0.16f;
    }

    static void MakeFireball(Transform parent, float s)
    {
        var go = NewSystem(parent, "Fireball", AddMat());
        var main = go.main;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s * 0.9f, s * 2.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(s * 0.55f, s * 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.06f;
        main.maxParticles = 180;

        Burst(go, 84);
        Shape(go, s * 0.25f);

        ColorOver(go, Gradient(
            new Color(1f, 0.96f, 0.80f, 1f),
            new Color(1f, 0.72f, 0.25f, 1f),
            new Color(0.85f, 0.30f, 0.06f, 1f),
            new Color(0.25f, 0.08f, 0.03f, 0f)));

        var size = go.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 0.25f, 1f));

        Dampen(go, s * 0.35f, 0.55f);
    }

    static void MakeSmoke(Transform parent, float s)
    {
        var go = NewSystem(parent, "Smoke", AlphaMat());
        var main = go.main;
        main.duration = 1.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        main.startDelay = new ParticleSystem.MinMaxCurve(0.18f, 0.40f);   // 烟要比火晚一点冒，不然第一帧就是一团黑
        main.startSpeed = new ParticleSystem.MinMaxCurve(s * 0.35f, s * 1.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(s * 0.55f, s * 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 90;

        Burst(go, 26);
        Shape(go, s * 0.35f);

        ColorOver(go, Gradient(
            new Color(0.18f, 0.16f, 0.15f, 0.75f),
            new Color(0.12f, 0.11f, 0.11f, 0.45f),
            new Color(0.10f, 0.10f, 0.10f, 0f)));

        var size = go.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.6f));

        Dampen(go, s * 0.2f, 0.5f);
    }

    static void MakeSparks(Transform parent, float s)
    {
        var go = NewSystem(parent, "Sparks", AddMat());
        var main = go.main;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s * 2.2f, s * 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
        main.gravityModifier = 1.1f;
        main.maxParticles = 160;

        Burst(go, 64);
        Shape(go, s * 0.15f);

        ColorOver(go, Gradient(
            new Color(1f, 0.95f, 0.7f, 1f),
            new Color(1f, 0.6f, 0.2f, 1f),
            new Color(0.6f, 0.15f, 0.05f, 0f)));

        var size = go.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

        Dampen(go, s * 0.6f, 0.25f);
    }

    static void MakeShockwave(Transform parent, float s)
    {
        var go = new GameObject("Shockwave");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 平铺在地面/水面
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = RingMesh();
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(1f, 0.85f, 0.55f, 0.55f);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var fx = go.AddComponent<ShockwaveFx>();
        fx.maxRadius = s * 2.6f;
        fx.life = 0.45f;
    }

    // ---------------- 工具 ----------------

    static ParticleSystem NewSystem(Transform parent, string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var r = go.GetComponent<ParticleSystemRenderer>();
        if (mat != null) r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingFudge = -10f;

        ps.Play();
        return ps;
    }

    static void Burst(ParticleSystem ps, short count)
    {
        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
    }

    static void Shape(ParticleSystem ps, float radius)
    {
        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = Mathf.Max(0.05f, radius);
        sh.radiusThickness = 0.6f;
    }

    static void ColorOver(ParticleSystem ps, Gradient g)
    {
        var c = ps.colorOverLifetime;
        c.enabled = true;
        c.color = new ParticleSystem.MinMaxGradient(g);
    }

    /// <summary>空气阻尼：飞出去之后被空气拖住（官方教程里那个 Limit Velocity + Dampen）。</summary>
    static void Dampen(ParticleSystem ps, float limit, float dampen)
    {
        var l = ps.limitVelocityOverLifetime;
        l.enabled = true;
        l.limit = new ParticleSystem.MinMaxCurve(Mathf.Max(0.5f, limit));
        l.dampen = dampen;
    }

    static Gradient Gradient(params Color[] colors)
    {
        var g = new Gradient();
        var keys = new GradientColorKey[colors.Length];
        var alphas = new GradientAlphaKey[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            float t = colors.Length > 1 ? i / (float)(colors.Length - 1) : 0f;
            keys[i] = new GradientColorKey(new Color(colors[i].r, colors[i].g, colors[i].b), t);
            alphas[i] = new GradientAlphaKey(colors[i].a, t);
        }
        g.SetKeys(keys, alphas);
        return g;
    }

    /// <summary>径向渐变的软圆点（火球/烟/火星共用一张图）。</summary>
    static Texture2D SoftDot()
    {
        if (_softDot != null) return _softDot;
        const int S = 128;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply();
        _softDot = t;
        return t;
    }

    static Mesh _ringMesh;

    /// <summary>单位圆环（半径 1），用缩放做冲击波扩散。</summary>
    static Mesh RingMesh()
    {
        if (_ringMesh != null) return _ringMesh;
        const int seg = 48;
        float inner = 0.86f, outer = 1f;
        var verts = new Vector3[seg * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            verts[i * 2] = new Vector3(cs * inner, sn * inner, 0f);
            verts[i * 2 + 1] = new Vector3(cs * outer, sn * outer, 0f);
        }
        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;
            tris[i * 6 + 0] = i * 2;
            tris[i * 6 + 1] = n * 2;
            tris[i * 6 + 2] = i * 2 + 1;
            tris[i * 6 + 3] = i * 2 + 1;
            tris[i * 6 + 4] = n * 2;
            tris[i * 6 + 5] = n * 2 + 1;
        }
        var m = new Mesh { name = "ShockwaveRing" };
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        _ringMesh = m;
        return m;
    }

    static Material AddMat()
    {
        if (_addMat != null) return _addMat;
        // Unity 6 里老的 "Particles/Additive" 已经没有了（会退成粉红），
        // 必须用 Legacy Shaders 前缀那套。
        var sh = Shader.Find("Legacy Shaders/Particles/Additive (Soft)");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _addMat = new Material(sh);
        _addMat.mainTexture = SoftDot();
        return _addMat;
    }

    static Material AlphaMat()
    {
        if (_alphaMat != null) return _alphaMat;
        var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _alphaMat = new Material(sh);
        _alphaMat.mainTexture = SoftDot();
        return _alphaMat;
    }
}

/// <summary>冲击波环：快速扩散 + 淡出。</summary>
public class ShockwaveFx : MonoBehaviour
{
    public float maxRadius = 14f;
    public float life = 0.45f;
    float _t;

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / life);
        float r = Mathf.Lerp(0.6f, maxRadius, 1f - (1f - k) * (1f - k));   // 先快后慢
        transform.localScale = new Vector3(r, r, 1f);

        var mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var m = mr.material;
            var c = m.color;
            c.a = Mathf.Lerp(0.55f, 0f, k);
            m.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>爆炸闪光灯：一亮就灭。</summary>
public class ExplosionLightFade : MonoBehaviour
{
    public float life = 0.16f;
    float _t, _start;

    void Start()
    {
        var l = GetComponent<Light>();
        if (l != null) _start = l.intensity;
    }

    void Update()
    {
        _t += Time.deltaTime;
        var l = GetComponent<Light>();
        if (l != null) l.intensity = Mathf.Lerp(_start, 0f, Mathf.Clamp01(_t / life));
        if (_t >= life) Destroy(this);
    }
}
