using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 干扰弹（声呐诱饵）：炮艇按 X 放出去，漂在水面上冒白烟，
/// **会吸引附近的鱼雷**——鱼雷进入 radius 以内就掉头追它。
///
/// 现实里鱼雷确实会被拖曳式/悬浮式声呐诱饵骗走，这里把它做成一个能看见的圈。
/// 视觉上刻意做得显眼（白色浮球 + 扩散波纹 + 烟柱），因为它是一个"骗"的机制，
/// 玩家得看得见自己的诱饵、也看得见敌人的诱饵。
/// </summary>
public class Decoy : MonoBehaviour
{
    /// <summary>吸引半径（米）：鱼雷进这个圈就改追诱饵。</summary>
    public float radius = 130f;
    /// <summary>存活时间（秒）。</summary>
    public float life = 18f;

    /// <summary>场上所有诱饵。鱼雷每帧从这里找"最近的诱饵"。</summary>
    public static readonly List<Decoy> All = new List<Decoy>();

    float _t;
    Transform _ring;

    /// <summary>在水面放一个诱饵。</summary>
    public static Decoy Deploy(Vector3 pos, float waterLevel, float radius = 130f, float life = 18f)
    {
        var go = new GameObject("Decoy");
        go.transform.position = new Vector3(pos.x, waterLevel + 0.15f, pos.z);

        var d = go.AddComponent<Decoy>();
        d.radius = radius;
        d.life = life;
        d.Build();
        All.Add(d);
        return d;
    }

    void Build()
    {
        // 浮球：白色球体，水面上下各露一半
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "DecoyBall";
        ball.transform.SetParent(transform, false);
        ball.transform.localScale = Vector3.one * 1.6f;
        var col = ball.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.92f, 0.95f, 1f) };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.35f, 0.55f, 0.7f));
        ball.GetComponent<Renderer>().sharedMaterial = mat;

        // 扩散波纹：一圈白色圆环，一直在扩散，玩家一眼能看见"这里有个诱饵"
        var ringGo = new GameObject("DecoyRing");
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var mf = ringGo.AddComponent<MeshFilter>();
        mf.sharedMesh = RingMesh();
        var mr = ringGo.AddComponent<MeshRenderer>();
        var rm = new Material(Shader.Find("Sprites/Default"));
        rm.color = new Color(0.85f, 0.95f, 1f, 0.5f);
        mr.sharedMaterial = rm;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring = ringGo.transform;
    }

    static Mesh _ringMesh;

    /// <summary>单位圆环（半径 1），用缩放做扩散。</summary>
    static Mesh RingMesh()
    {
        if (_ringMesh != null) return _ringMesh;
        const int seg = 32;
        float inner = 0.94f, outer = 1f;
        var verts = new Vector3[seg * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * inner, s * inner, 0f);
            verts[i * 2 + 1] = new Vector3(c * outer, s * outer, 0f);
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
        var m = new Mesh { name = "DecoyRing" };
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        _ringMesh = m;
        return m;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _t += dt;

        // 波纹每 1.2 秒扩散一次
        if (_ring != null)
        {
            float k = (_t % 1.2f) / 1.2f;
            float s = Mathf.Lerp(2f, radius * 0.55f, k);
            _ring.localScale = new Vector3(s, s, 1f);
            var r = _ring.GetComponent<Renderer>();
            if (r != null)
            {
                var m = r.material;
                var c = m.color;
                c.a = Mathf.Lerp(0.55f, 0f, k);
                m.color = c;
            }
        }

        // 上下轻微浮动，看起来是漂着的
        transform.position = new Vector3(transform.position.x,
                                          transform.position.y + Mathf.Sin(_t * 2.2f) * 0.0012f,
                                          transform.position.z);

        if (_t >= life)
        {
            All.Remove(this);
            Destroy(gameObject);
        }
    }

    void OnDestroy() => All.Remove(this);

    /// <summary>找离某点最近的、还在生效的诱饵（超出 radius 的不算）。</summary>
    public static Decoy Nearest(Vector3 from, out float dist)
    {
        Decoy best = null;
        dist = float.MaxValue;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var d = All[i];
            if (d == null) { All.RemoveAt(i); continue; }
            float dd = Vector3.Distance(new Vector3(from.x, 0f, from.z),
                                        new Vector3(d.transform.position.x, 0f, d.transform.position.z));
            if (dd <= d.radius && dd < dist) { dist = dd; best = d; }
        }
        return best;
    }
}
