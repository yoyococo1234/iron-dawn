using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 反鱼雷拦截弹（2026-11-07 用户重新定义）。
///
/// 原来是"漂在水面的声呐诱饵"，靠吸引鱼雷骗走。用户要的是：
/// **一枚小炮弹，从船边射出去，在水下主动追鱼雷，撞上就把它炸掉** —— 也就是近防拦截弹。
///
/// 现在的行为：
///   1. 从船边（水下 1 m）发射，先朝最近的敌方鱼雷飞
///   2. 每帧转向目标（150°/s），速度 65 m/s
///   3. 进到 6 m 内 → 引爆鱼雷 + 自己
///   4. 附近没有鱼雷就在原地绕一小圈，12 秒后自毁
/// </summary>
public class Decoy : MonoBehaviour
{
    /// <summary>拦截半径（米）：进这个距离就引爆。</summary>
    public float radius = 6f;
    /// <summary>存活时间（秒）。</summary>
    public float life = 12f;
    /// <summary>搜索范围（米）：只追这个距离内的鱼雷。</summary>
    public float seekRange = 320f;
    /// <summary>飞行速度（m/s）。</summary>
    public float speed = 65f;
    /// <summary>转向速率（度/秒）。</summary>
    public float turnRate = 150f;

    /// <summary>发射方阵营（-1 = 未知，谁都拦）。</summary>
    public int team = -1;

    /// <summary>场上所有拦截弹。</summary>
    public static readonly List<Decoy> All = new List<Decoy>();

    float _t;
    Vector3 _dir;
    float _waterLevel;
    LineRenderer _trail;

    public static Decoy Deploy(Vector3 pos, float waterLevel, float radius = 6f, float life = 12f, int team = -1)
    {
        var go = new GameObject("Intercept");
        go.transform.position = new Vector3(pos.x, waterLevel - 1.0f, pos.z);

        var d = go.AddComponent<Decoy>();
        d.radius = radius;
        d.life = life;
        d.team = team;
        d._waterLevel = waterLevel;
        d._dir = Vector3.forward;
        d.Build();
        All.Add(d);
        return d;
    }

    void Build()
    {
        // 弹体：小胶囊，橙红色，水下也能看见
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(0.5f, 1.2f, 0.5f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var col = body.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        var mat = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.45f, 0.2f) };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.9f, 0.35f, 0.1f));
        body.GetComponent<Renderer>().sharedMaterial = mat;

        // 航迹：一条白色拖尾，方便玩家看见"我的拦截弹出去了"
        var trGo = new GameObject("Trail");
        trGo.transform.SetParent(transform, false);
        _trail = trGo.AddComponent<LineRenderer>();
        _trail.useWorldSpace = true;
        _trail.positionCount = 0;
        _trail.startWidth = 0.5f;
        _trail.endWidth = 0.1f;
        _trail.material = new Material(Shader.Find("Sprites/Default"));
        _trail.startColor = new Color(1f, 0.9f, 0.7f, 0.85f);
        _trail.endColor = new Color(1f, 0.9f, 0.7f, 0f);
        _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _t += dt;

        // ---- 找最近的敌方鱼雷 ----
        Torpedo target = null;
        float bestD = seekRange;
        for (int i = Torpedo.All.Count - 1; i >= 0; i--)
        {
            var torp = Torpedo.All[i];
            if (torp == null) { Torpedo.All.RemoveAt(i); continue; }
            if (team >= 0 && torp.Team == team) continue;      // 不拦自己人的
            float d = Vector3.Distance(torp.transform.position, transform.position);
            if (d < bestD) { bestD = d; target = torp; }
        }

        if (target != null)
        {
            Vector3 want = (target.transform.position - transform.position).normalized;
            _dir = Vector3.RotateTowards(_dir, want, turnRate * Mathf.Deg2Rad * dt, 0f);
            if (bestD <= radius)
            {
                target.Intercept();
                Boom();
                return;
            }
        }
        else
        {
            // 没目标就往前飘一点（别原地停着）
            _dir = Vector3.RotateTowards(_dir, Quaternion.Euler(0f, 25f * dt, 0f) * _dir, 30f * Mathf.Deg2Rad * dt, 0f);
        }

        transform.position += _dir * speed * dt;
        transform.rotation = Quaternion.LookRotation(_dir);
        // 贴着水下游，不要飞出水面上天
        var p = transform.position;
        p.y = Mathf.Clamp(p.y, _waterLevel - 3.5f, _waterLevel - 0.5f);
        transform.position = p;

        // 拖尾
        if (_trail != null)
        {
            int n = Mathf.Min(_trail.positionCount + 1, 40);
            var pts = new Vector3[n];
            for (int i = 0; i < n - 1; i++) pts[i] = _trail.GetPosition(i);
            pts[n - 1] = transform.position;
            _trail.positionCount = n;
            _trail.SetPositions(pts);
        }

        if (_t >= life) { All.Remove(this); Destroy(gameObject); }
    }

    void Boom()
    {
        // 水面炸开一朵白花
        ExplosionFx.Spawn(transform.position + Vector3.up * 0.8f, 6f);
        Sfx.PlayAt(Sfx.Explosion, transform.position, 0.7f, Random.Range(1.1f, 1.3f), 15f, 400f);
        All.Remove(this);
        Destroy(gameObject);
    }

    void OnDestroy() => All.Remove(this);

    /// <summary>找离某点最近的、还在生效的拦截弹（鱼雷可以用它做规避参考）。</summary>
    public static Decoy Nearest(Vector3 from, out float dist)
    {
        Decoy best = null;
        dist = float.MaxValue;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var d = All[i];
            if (d == null) { All.RemoveAt(i); continue; }
            float dd = Vector3.Distance(from, d.transform.position);
            if (dd < dist) { dist = dd; best = d; }
        }
        return best;
    }
}
