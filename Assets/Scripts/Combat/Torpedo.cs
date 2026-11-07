using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鱼雷：**水下**定深航行的慢速弹（50 m/s，炮弹是 800 m/s）。
///
/// 和 Projectile 的区别：
///   · 不吃重力 —— 入水后保持固定深度水平航行，不会掉到海床上
///   · **会制导** —— 出管一段距离后锁定附近敌方舰艇，按 homingTurnRate 转向追过去；
///     场上只要有干扰弹（Decoy）进了吸引半径，就改追干扰弹（被骗走）
///   · 有水面航迹 —— 白色气泡尾迹 + 水面泡沫条，远远能看见一条线冲过来
///   · 只打水里的东西 —— 撞到岸/浅滩就自毁
///
/// 由 VehicleCombat.FireWeapon 在 WeaponDef.isTorpedo = true 时创建。
/// </summary>
public class Torpedo : MonoBehaviour
{
    /// <summary>入水后下潜到目标深度要多久（秒）。太长会先在水面滑一段，看起来像炮弹。</summary>
    public float diveTime = 0.35f;
    /// <summary>命中判定半径（米）。</summary>
    public float hitRadius = 0.9f;

    // ---- 制导参数 ----
    /// <summary>转向速度（度/秒）。50 m/s 下 45°/s ≈ 64 m 转弯半径，追得上但不会瞬间拐弯。</summary>
    public float homingTurnRate = 45f;
    /// <summary>开始制导前的直线距离（米）——刚出管就拐弯会像被拽着走。</summary>
    public float homingArmDistance = 25f;
    /// <summary>搜索目标的锥角（度）：只锁定正前方这个锥里的敌舰。</summary>
    public float seekConeDeg = 55f;

    Vector3 _dir;
    float _speed;
    float _damage;
    float _range;
    float _depth;
    float _waterLevel;
    float _travelled;
    float _life;
    float _t;
    float _startY;
    float _foamTimer;
    float _lastTime = -1f;

    AmmoDef _ammo;
    VehicleCombat _owner;
    VehicleCombat _target;
    Decoy _decoy;

    TrailRenderer _trail;

    /// <summary>调试用：最后一枚鱼雷是怎么没的（life / beached / hit / miss）。</summary>
    public static string LastKillReason = "";

    /// <summary>场上所有活着的鱼雷（HUD 雷达用）。</summary>
    public static readonly List<Torpedo> All = new List<Torpedo>();

    /// <summary>这枚鱼雷属于哪个阵营（雷达上分辨敌我）。</summary>
    public int Team => _owner != null ? _owner.team : -1;

    void OnDestroy() => All.Remove(this);

    public void Launch(Vector3 origin, Vector3 dir, float speed, float damage, AmmoDef ammo,
                       VehicleCombat owner, float range, float depth, float waterLevel)
    {
        _dir = dir.normalized;
        _dir.y = 0f;                       // 鱼雷只走水平方向（不跟着炮管仰角飞）
        if (_dir.sqrMagnitude < 0.01f) _dir = owner != null ? owner.transform.forward : Vector3.forward;
        _dir.Normalize();

        _speed = Mathf.Max(1f, speed);
        _damage = damage;
        _ammo = ammo;
        _owner = owner;
        _range = Mathf.Max(1f, range);
        _waterLevel = waterLevel;
        // 定深要跟实际水深走：近岸只有 2~3 m 深，硬压 1.6 m 会贴着海床一路"蹭"，
        // 稍微一波动就被判成搁浅。这里最多吃掉水深的一半。
        float seabed = BattlefieldTerrain.SampleHeight(origin.x, origin.z);
        float depthAvail = Mathf.Max(0.4f, waterLevel - seabed);
        _depth = Mathf.Clamp(depth, 0.4f, Mathf.Max(0.5f, depthAvail * 0.5f));

        _startY = origin.y;
        // 射程兜底：武器定义里的 range 万一没配好（0 或几十米），鱼雷会在出管两秒内自己没了。
        // 这里给个下限，保证至少跑 200 m 才可能耗尽。
        _range = Mathf.Max(200f, _range);
        _life = _range / _speed + 3f;

        transform.position = origin;
        transform.rotation = Quaternion.LookRotation(_dir);
        if (!All.Contains(this)) All.Add(this);

        // 水下气泡尾迹：细而半透明，远处看是一道淡淡的白色水痕（太粗会像一块白板）
        _trail = gameObject.AddComponent<TrailRenderer>();
        _trail.time = 2.2f;
        _trail.startWidth = 0.20f;
        _trail.endWidth = 0.02f;
        _trail.material = new Material(Shader.Find("Sprites/Default"));
        _trail.startColor = new Color(0.90f, 0.96f, 1f, 0.42f);
        _trail.endColor = new Color(0.78f, 0.92f, 1f, 0f);
        _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        _trail.minVertexDistance = 0.4f;

        // 鱼雷本体：细长圆柱，水下能看见一个影子
        var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        vis.name = "TorpedoBody";
        vis.transform.SetParent(transform, false);
        vis.transform.localScale = new Vector3(0.16f, 0.5f, 0.16f);
        vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // 必须**先禁用**再销毁：Destroy 是延迟到帧末的，这一帧的 SphereCast 会先撞上它自己
        var vcol = vis.GetComponent<Collider>();
        if (vcol != null) { vcol.enabled = false; Object.Destroy(vcol); }
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.28f, 0.30f) };
        vis.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void Update()
    {
        // 步长用 Time.deltaTime（受 timeScale 控制，慢动作/暂停时跟着走），但夹住上限：
        // 编辑器卡一帧时 Time.deltaTime 可能报到十几秒，step = 40 * 15 = 600 m，
        // 鱼雷会在一帧里"瞬移"完整个射程，看起来就是刚发射就没了。
        float dt = Mathf.Clamp(Time.deltaTime, 0.0005f, 0.05f);

        _life -= dt;
        if (_life <= 0f) { LastKillReason = "life"; Destroy(gameObject); return; }

        _t += dt;

        // ---------- 高度：先下潜到定深，之后锁死 ----------
        float targetY = _waterLevel - _depth;
        if (_t < diveTime)
        {
            float k = Mathf.Clamp01(_t / diveTime);
            float y = Mathf.Lerp(_startY, targetY, k * k);      // 二次曲线：入水快、后面稳
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        }
        else
        {
            transform.position = new Vector3(transform.position.x, targetY, transform.position.z);
        }

        // ---------- 制导：先看有没有干扰弹，再找敌舰 ----------
        if (_travelled > homingArmDistance)
        {
            Vector3 aim = Vector3.zero;
            bool hasAim = false;

            float decDist;
            var dec = Decoy.Nearest(transform.position, out decDist);
            if (dec != null)
            {
                // 诱饵优先：鱼雷被骗走，掉头追它
                _decoy = dec;
                _target = null;
                aim = dec.transform.position;
                hasAim = true;
            }
            else
            {
                if (_target == null || !_target.Alive) _target = AcquireTarget();
                if (_target != null)
                {
                    aim = _target.transform.position;
                    hasAim = true;
                }
            }

            if (hasAim)
            {
                Vector3 to = aim - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f)
                {
                    // 追到诱饵头上就算被骗掉：诱饵没有碰撞体，不判定的话鱼雷会绕着它转圈
                    if (_decoy != null && to.magnitude < 5f)
                    {
                        LastKillReason = "decoyed";
                        Explode(false);
                        return;
                    }

                    Vector3 want = to.normalized;
                    _dir = Vector3.RotateTowards(_dir, want, homingTurnRate * Mathf.Deg2Rad * dt, 0f);
                    _dir.y = 0f;
                    if (_dir.sqrMagnitude > 0.0001f) _dir.Normalize();
                    transform.rotation = Quaternion.LookRotation(_dir);
                }
            }
        }

        // ---------- 前进 + 扫掠命中 ----------
        Vector3 stepVec = _dir * _speed * dt;
        float step = Mathf.Min(stepVec.magnitude, 3f);      // 硬上限：任何情况下单帧不超过 3 m
        stepVec = _dir * step;

        var hits = Physics.SphereCastAll(transform.position, hitRadius, _dir, step + 0.4f, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                // 海床不算障碍 —— 鱼雷本来就在水下，贴着海床走是正常的
                if (h.collider is TerrainCollider) continue;
                // 自己身上的东西（船体/鱼雷本体）不算
                if (h.collider.transform.IsChildOf(transform)) continue;

                var target = h.collider.GetComponentInParent<VehicleCombat>();
                if (target == _owner) continue;
                if (target != null && target.Alive)
                {
                    LastKillReason = "hit:" + target.name;
                    target.TakeDamage(_damage, _ammo, transform.position, _owner);
                    VehicleCombat.ShotsHit++;
                    Explode(true);
                    return;
                }
                // 撞到别的东西（建筑/残骸）也算终止
                LastKillReason = "miss:" + h.collider.name + "@" + h.collider.GetType().Name;
                Explode(false);
                return;
            }
        }

        transform.position += stepVec;
        _travelled += step;

        // ---------- 近炸命中（2026-11-07）----------
        // 只靠 SphereCast 打不中船：鱼雷在 1.6 m 深，而船的 BoxCollider 底面通常只到 -0.5 m 左右，
        // 鱼雷从船底下穿过去了 —— 用户反馈"发了这么多次鱼雷，一次都没炸到"。
        // 这里加一层水平面（XZ）近炸判定：只要和某条敌船的水平距离进了它的船体半径，就算命中。
        foreach (var other in VehicleCombat.AllAlive())
        {
            if (other == null || other == _owner || !other.Alive) continue;
            if (other.Def == null || other.Def.domain != Domain.Sea) continue;
            if (_owner != null && other.team == _owner.team) continue;

            Vector3 d = other.transform.position - transform.position;
            d.y = 0f;
            float hullRadius = 7f;
            var col = other.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 b = col.bounds.extents;
                hullRadius = Mathf.Max(4f, Mathf.Max(b.x, b.z));
            }
            if (d.sqrMagnitude <= (hullRadius + hitRadius) * (hullRadius + hitRadius))
            {
                LastKillReason = "hit:" + other.name;
                other.TakeDamage(_damage, _ammo, transform.position, _owner);
                VehicleCombat.ShotsHit++;
                Explode(true);
                return;
            }
        }

        // ---------- 水面泡沫航迹（每 0.12 秒留一个白点，慢慢淡掉）----------
        _foamTimer -= dt;
        if (_foamTimer <= 0f && _t > diveTime)
        {
            _foamTimer = 0.12f;
            SpawnFoam();
        }

        // 打上岸才叫搁浅。
        // 注意：地图边缘的分块地形可能没有数据，SampleHeight 会返回 0（远高于水面），
        // 用它做判定会让鱼雷一进边界就自爆 —— 所以只认「水深数据明确为 0 且地形确实高于水面」。
        float depthHere = BattlefieldTerrain.WaterDepth(transform.position.x, transform.position.z);
        if (_t > 1.2f && depthHere < 0.05f)
        {
            float ground = BattlefieldTerrain.SampleHeight(transform.position.x, transform.position.z);
            if (ground > _waterLevel - 0.05f) { Beached(); return; }
        }

        if (_travelled >= _range)
        {
            LastKillReason = $"range({_travelled:F0}/{_range:F0})";
            Explode(false);
            return;
        }
    }

    /// <summary>
    /// 找一个可以锁的敌舰：敌方阵营、还活着、在海面上、在射程内、并且落在正前方的搜索锥里。
    /// 已经锁上的目标不重新挑（否则两艘船一交错就会来回改主意，鱼雷走成 S 形）。
    /// </summary>
    VehicleCombat AcquireTarget()
    {
        if (_owner == null) return null;

        VehicleCombat best = null;
        float bestScore = float.MaxValue;
        var all = Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None);
        foreach (var vc in all)
        {
            if (vc == null || vc == _owner || !vc.Alive) continue;
            if (vc.team == _owner.team) continue;
            if (vc.Def == null || vc.Def.domain != Domain.Sea) continue;

            Vector3 to = vc.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > _range || dist < 3f) continue;

            float ang = Vector3.Angle(_dir, to);
            if (ang > seekConeDeg) continue;

            // 角度优先、同角度取近的
            float score = ang * 4f + dist * 0.02f;
            if (score < bestScore) { bestScore = score; best = vc; }
        }
        return best;
    }

    void SpawnFoam()
    {
        var go = new GameObject("TorpedoFoam");
        go.transform.position = new Vector3(transform.position.x, _waterLevel + 0.05f, transform.position.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = FoamDisc();
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(1f, 1f, 1f, 0.32f);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var fx = go.AddComponent<FoamFade>();
        fx.life = 2.6f;
        fx.size = 0.85f;
    }

    static Mesh _foamMesh;

    /// <summary>圆形泡沫片（16 边扇形）。用四边形会看成一块白方块。</summary>
    static Mesh FoamDisc()
    {
        if (_foamMesh != null) return _foamMesh;
        const int seg = 16;
        var verts = new Vector3[seg + 1];
        var uvs = new Vector2[seg + 1];
        var tris = new int[seg * 3];
        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
            uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
        }
        for (int i = 0; i < seg; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % seg;
            tris[i * 3 + 2] = 1 + i;
        }
        var m = new Mesh { name = "FoamDisc" };
        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        _foamMesh = m;
        return m;
    }

    /// <summary>搁浅（撞岸/浅滩）：冒个白花就没了。</summary>
    void Beached()
    {
        LastKillReason = "beached";
        Explode(false);
    }

    void Explode(bool hit)
    {
        if (string.IsNullOrEmpty(LastKillReason)) LastKillReason = hit ? "hit" : "miss";
        float r = hit ? 7f : 3.5f;

        // 和载具死亡用同一套分层爆炸（闪光/火球/烟/火星/冲击波），只是小一号
        Vector3 pos = new Vector3(transform.position.x, _waterLevel + 0.4f, transform.position.z);
        ExplosionFx.Spawn(pos, hit ? r * 0.8f : r * 0.45f);

        Sfx.PlayAt(Sfx.Explosion, transform.position, hit ? 0.9f : 0.6f,
                   Random.Range(0.9f, 1.05f), 25f, 900f);

        Destroy(gameObject);
    }
}

/// <summary>水面泡沫点：放大 + 淡出，播完自销毁。</summary>
public class FoamFade : MonoBehaviour
{
    public float life = 2.2f;
    public float size = 1.1f;
    float _t;
    Renderer _r;

    void Start() => _r = GetComponent<Renderer>();

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / life);
        transform.localScale = Vector3.one * Mathf.Lerp(size * 0.5f, size * 1.8f, k);
        if (_r != null)
        {
            var m = _r.material;
            var c = m.color;
            c.a = Mathf.Lerp(0.55f, 0f, k);
            m.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}
