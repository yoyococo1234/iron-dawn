using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 履带总成（方案 B：Lux3D 单独生成「履带+负重轮」→ part_split → work/build_tracks3.py 拆干净）。
///
/// 两条运动：
///   1) 负重轮 / 主动轮 / 诱导轮 / 托带轮（名字以 wheel 开头）：绕自己的轴心原地转，ω = v / R。
///      —— 每个轮子在 Blender 里已经把原点搬到自己的轴心上，所以直接转它不会绕着公共中心甩。
///   2) 履带（名字以 belt 开头）：**沿履带环的路径真前进**，不是滚贴图。
///      中心线不是猜的：先按角度把履带顶点分箱取均值得到环的中心线，
///      再按弧长均匀重采样；每个顶点记下自己的弧长参数 s 和相对中心线的偏移。
///      每帧把 s 推到 s + 走过的距离，位置 = 中心线(s+Δ) + 偏移（跟着切线转），法线同样转。
///      这样履带板是真的在往前走，和轮子线速度一致（ω·R = v）。
/// </summary>
public class TrackAssembly : MonoBehaviour
{
    [Tooltip("载具物理（取车速）。留空自动往上找")]
    public VehiclePhysics vehicle;

    [Tooltip("轮子转向：1 = 前进时上沿向前（一般就是 1，反了改成 -1）")]
    public float spinSign = 1f;

    [Tooltip("履带前进方向：1 = 和轮子同向（一般不用改）")]
    public float beltSign = 1f;

    [Tooltip("履带线速度相对车速的倍率，1 = 完全同步")]
    public float beltSpeedScale = 1f;

    [Header("调试")]
    [Tooltip("没有 VehiclePhysics 时，直接按这个速度跑（m/s）")]
    public float debugSpeed = 0f;
    [Tooltip("离相机超过这个距离就不逐帧算履带（0 = 一直算）")]
    public float animateDistance = 150f;
    [Tooltip("GPU 顶点动画：位移交给 Shader，CPU 每帧只改一个 _Scroll（2026-11-01 验证通过，默认开）")]
    public bool useGpuShader = true;
    [Tooltip("清掉 part_split 留下的碎片：static 节点（纯渣子）+ 端部轮子上炸开的尖刺")]
    public bool trimDebris = true;
    [Tooltip("端部主动轮/诱导轮被 part_split 切烂的，换成程序生成的轮盘（半径/圆心从履带环拟合，默认开）")]
    public bool rebuildEndWheels = true;
    [Tooltip("打印诊断信息")]
    public bool verbose = false;

    class Wheel
    {
        public Transform t;
        public float radius;
        public float side;        // -1 = 左，+1 = 右
        public float localZ;      // 在履带总成局部空间里的车长方向位置（认端部轮用）
        public Vector3 localCenter; // 渲染包围盒中心（履带总成局部空间）—— part_split 会把位移烘进网格，transform.position 不可信
        public float medianR;     // 顶点到轮轴的中位半径（网格局部单位）
    }

    class Belt
    {
        public Mesh mesh;
        public Transform tf;
        public Vector3[] basePos;
        public Vector3[] baseNrm;
        public Vector3[] curPos;
        public Vector3[] curNrm;
        public float[] s;         // 每个顶点的弧长参数（网格局部单位）
        public float[] offY;      // 相对中心线的偏移（网格局部 Y = 车长方向）
        public float[] offZ;      // 相对中心线的偏移（网格局部 Z = 高度方向）
        public float[] th0;       // 顶点原始位置的切线角
        public bool[] anim;       // 这个顶点要不要跟着履带走（离环中心线太远的杂物不动）
        public float length;      // 环周长（网格局部单位）
        public float unit = 1f;   // 网格局部单位 → 米 的比例（FBX 导入缩放，通常 100）
        public int N;             // 中心线采样点数
        public Vector2[] cyz;     // 中心线点 (y,z)，按弧长均匀
        public Vector2[] cdir;    // 该处的切线方向（单位向量）
        public float[] ang;       // 该点的切线角（展开，单调递增），angEnd = 绕完一圈的角度
        public Material mat;      // 走 GPU 顶点动画时，这条履带自己的材质实例
        public bool gpu;          // true = 位移交给 Shader（CPU 每帧只改一个 _Scroll）
        public float side;        // -1 = 左，+1 = 右
    }

    readonly List<Wheel> _wheels = new List<Wheel>();
    readonly List<Belt> _belts = new List<Belt>();
    float _scrollL;         // 左履带走过的距离（米）
    float _scrollR;         // 右履带走过的距离（米）
    float _prevYaw;         // 上一帧的车体朝向（算转向角速度用）
    bool _hasPrevYaw;
    float _halfTrack = 1.2f;  // 两条履带中心到车体中线的距离（米）

    void Start()
    {
        if (vehicle == null) vehicle = GetComponentInParent<VehiclePhysics>();
        if (trimDebris) HideDebrisNodes();
        CollectBelts();                    // 先收履带：端部轮盘要用履带环的半径和端点圆心
        CollectWheels();
        if (rebuildEndWheels) RebuildEndWheels();
        _prevYaw = transform.eulerAngles.y;
        _hasPrevYaw = true;
        if (verbose)
            Debug.Log($"[TrackAssembly] {name}: 轮子 {_wheels.Count} 个，履带 {_belts.Count} 条，半轮距 {_halfTrack:F2} m");
    }

    /// <summary>
    /// 隐藏 part_split 漏下来的纯碎片节点（名字叫 static，实测主战履带总成里是 6777 个顶点的渣子，
    /// 渲染出来就是飘在履带周围的一堆尖片）。
    /// </summary>
    void HideDebrisNodes()
    {
        int n = 0;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.ToLowerInvariant() != "static") continue;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled) { mr.enabled = false; n++; }
        }
        if (verbose && n > 0) Debug.Log($"[TrackAssembly] 隐藏碎片节点 {n} 个");
    }

    void CollectWheels()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.ToLowerInvariant().StartsWith("wheel")) continue;
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null) continue;

            // 端部主动轮/诱导轮被 part_split 切出尖刺：按半径截断，只留轮盘本体
            float medianR = 0f;
            if (trimDebris)
            {
                var trimmed = TrimRadial(mf.sharedMesh, out medianR);
                if (trimmed != null) mf.sharedMesh = trimmed;
            }

            // 网格局部空间：X = 车宽（轮轴方向），Y = 车长，Z = 高
            var sz = mf.sharedMesh.bounds.size;
            float scaleY = Mathf.Abs(t.lossyScale.y);
            float r = Mathf.Min(sz.y, sz.z) * 0.5f * scaleY;
            if (r < 0.02f) r = Mathf.Max(sz.y, sz.z) * 0.5f * scaleY;
            if (medianR > 0f) r = Mathf.Min(r, medianR * scaleY);
            float x = transform.InverseTransformPoint(t.position).x;
            var local = mr != null ? transform.InverseTransformPoint(mr.bounds.center) : transform.InverseTransformPoint(t.position);
            _wheels.Add(new Wheel
            {
                t = t,
                radius = Mathf.Max(0.05f, r),
                side = x < 0f ? -1f : 1f,
                localZ = local.z,
                localCenter = local,
                medianR = medianR
            });
        }
    }

    /// <summary>
    /// 端部的主动轮/诱导轮被 part_split 切成了"碎块团"（不是圆盘），截断也救不回来。
    ///
    /// 做法：半径和圆心**不信那个碎块**，改从履带环量 ——
    /// 取履带中心线两端那一段点做最小二乘圆拟合，圆心/半径就是端部轮该在的位置和大小。
    /// 把落在车长方向最外 8% 的轮子，换成按这个半径/圆心生成的轮盘。
    /// </summary>
    void RebuildEndWheels()
    {
        if (_wheels.Count < 4 || _belts.Count == 0) return;

        float zmin = float.MaxValue, zmax = float.MinValue;
        foreach (var w in _wheels)
        {
            if (w.localZ < zmin) zmin = w.localZ;
            if (w.localZ > zmax) zmax = w.localZ;
        }
        float len = zmax - zmin;
        if (len < 0.01f) return;

        // 履带环两端圆心（车体局部空间）与半径：对中心线两端做圆拟合
        var ends = new List<Vector3>();
        var endRs = new List<float>();
        foreach (var b in _belts)
        {
            if (b.mesh == null || b.tf == null || b.cyz == null || b.cyz.Length < 16) continue;
            var bb = b.mesh.bounds;
            var toRoot = transform.worldToLocalMatrix * b.tf.localToWorldMatrix;
            float scale = Mathf.Abs(b.tf.lossyScale.y);

            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in b.cyz) { if (p.x < ymin) ymin = p.x; if (p.x > ymax) ymax = p.x; }
            float span = ymax - ymin;
            if (span < 1e-5f) continue;

            for (int side = 0; side < 2; side++)
            {
                var pts = new List<Vector2>();
                foreach (var p in b.cyz)
                {
                    bool near = side == 0 ? p.x > ymax - span * 0.16f : p.x < ymin + span * 0.16f;
                    if (near) pts.Add(p);
                }
                if (pts.Count < 8) continue;
                if (!FitCircleRefined(pts, out var c, out float r)) continue;
                if (r <= 1e-6f || r * scale > 3f) continue;          // 拟合跑飞就丢掉

                var p3 = new Vector3(bb.center.x, c.x, c.y);
                ends.Add(toRoot.MultiplyPoint3x4(p3));
                endRs.Add(r * scale);
            }
        }
        if (ends.Count == 0) return;

        int replaced = 0;
        foreach (var w in _wheels)
        {
            float tt = (w.localZ - zmin) / len;
            if (tt > 0.08f && tt < 0.92f) continue;               // 中间的负重轮不动
            var mf = w.t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            // 找最近的履带环端点
            var wc = w.localCenter;
            int bestI = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < ends.Count; i++)
            {
                float d = (ends[i] - wc).sqrMagnitude;
                if (d < bestD) { bestD = d; bestI = i; }
            }
            float endR = endRs[bestI];
            Vector3 best = ends[bestI];

            var src = mf.sharedMesh;
            float sy = Mathf.Abs(w.t.lossyScale.y);
            float blobR = Mathf.Min(src.bounds.size.y, src.bounds.size.z) * 0.5f;
            var ownCenter = src.bounds.center;
            float fittedR = endR / Mathf.Max(0.0001f, sy);

            // 拟合的圆心/半径只有和「这个轮子自己的尺寸」对得上时才敢用。
            // 对不上就退回轮子自己的包围盒（碎块形状虽然乱，外廓位置还是准的）——
            // 混用「拟合的圆心 + 碎块的半径」会让圆盘跑到履带环外面去（2026-11-01 实测踩到）。
            bool fitOk = blobR <= 1e-6f ||
                         (fittedR >= blobR * 0.75f && fittedR <= blobR * 1.30f);

            float rLocal = fitOk ? fittedR : blobR;
            if (rLocal <= 1e-6f) continue;
            float widthLocal = Mathf.Max(src.bounds.size.x * 0.9f, rLocal * 0.6f);

            // 圆心换算到轮子自己的网格局部空间
            var world = fitOk
                ? transform.TransformPoint(best)
                : mf.transform.TransformPoint(ownCenter);
            var centerLocal = mf.transform.InverseTransformPoint(world);

            // 网格建在原点 + 把轮子的 Transform 挪到圆心。
            // 关键：TrackAssembly 是让 Transform 自转（Rotate 绕自身原点），
            // 如果网格带偏移、Transform 原点却在别处，轮盘就会"绕着履带环公转"、拖出履带。
            mf.sharedMesh = MakeSprocket(rLocal, widthLocal, Vector3.zero);
            w.t.position = world;
            w.localCenter = transform.InverseTransformPoint(world);
            w.radius = Mathf.Max(0.05f, rLocal * sy);
            replaced++;
        }
        if (verbose && replaced > 0)
            Debug.Log($"[TrackAssembly] 端部轮换成程序轮盘 {replaced} 个（拟合半径 {endRs[0]:F2} m）");
    }

    /// <summary>最小二乘圆拟合（Kåsa 代数法）：x²+y²+Dx+Ey+F=0。点太少或退化返回 false。</summary>
    static bool FitCircle(List<Vector2> pts, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;
        int n = pts.Count;
        if (n < 4) return false;

        double Sxx = 0, Sxy = 0, Syy = 0, Sx = 0, Sy = 0, Sxz = 0, Syz = 0, Sz = 0;
        for (int i = 0; i < n; i++)
        {
            double x = pts[i].x, y = pts[i].y, z = x * x + y * y;
            Sxx += x * x; Sxy += x * y; Syy += y * y;
            Sx += x; Sy += y;
            Sxz += x * z; Syz += y * z; Sz += z;
        }

        double a11 = Sxx, a12 = Sxy, a13 = Sx;
        double a21 = Sxy, a22 = Syy, a23 = Sy;
        double a31 = Sx, a32 = Sy, a33 = n;
        double b1 = -Sxz, b2 = -Syz, b3 = -Sz;

        double det = a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31);
        if (System.Math.Abs(det) < 1e-12) return false;

        double dD = b1 * (a22 * a33 - a23 * a32) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a32 - a22 * b3);
        double dE = a11 * (b2 * a33 - a23 * b3) - b1 * (a21 * a33 - a23 * a31) + a13 * (a21 * b3 - b2 * a31);
        double dF = a11 * (a22 * b3 - b2 * a32) - a12 * (a21 * b3 - b2 * a31) + b1 * (a21 * a32 - a22 * a31);

        double D = dD / det, E = dE / det, F = dF / det;
        double cx = -D * 0.5, cy = -E * 0.5;
        double rr = cx * cx + cy * cy - F;
        if (rr <= 0) return false;

        center = new Vector2((float)cx, (float)cy);
        radius = (float)System.Math.Sqrt(rr);
        return true;
    }

    /// <summary>生成一个轮盘（轴沿网格局部 X）：外圆筒 + 两侧圆盖。center = 原网格包围盒中心（网格局部）。</summary>
    static Mesh MakeSprocket(float radius, float width, Vector3 center, int sides = 28)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        // 局部函数：加一个圆筒（轴沿网格局部 X）
        System.Action<float, float> addCyl = (r, w) =>
        {
            float hx = w * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (float)i / sides * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);

                // 外圆筒
                int b = verts.Count;
                verts.Add(new Vector3(-hx, c0 * r, s0 * r));
                verts.Add(new Vector3(-hx, c1 * r, s1 * r));
                verts.Add(new Vector3(hx, c1 * r, s1 * r));
                verts.Add(new Vector3(hx, c0 * r, s0 * r));
                uvs.Add(new Vector2((float)i / sides, 0f));
                uvs.Add(new Vector2((float)(i + 1) / sides, 0f));
                uvs.Add(new Vector2((float)(i + 1) / sides, 1f));
                uvs.Add(new Vector2((float)i / sides, 1f));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);

                // 两侧圆盖
                int ca = verts.Count;
                verts.Add(new Vector3(-hx, 0f, 0f));
                verts.Add(new Vector3(-hx, c0 * r, s0 * r));
                verts.Add(new Vector3(-hx, c1 * r, s1 * r));
                uvs.Add(new Vector2(0.5f, 0.5f));
                uvs.Add(new Vector2(0.5f + c0 * 0.5f, 0.5f + s0 * 0.5f));
                uvs.Add(new Vector2(0.5f + c1 * 0.5f, 0.5f + s1 * 0.5f));
                tris.Add(ca); tris.Add(ca + 2); tris.Add(ca + 1);

                int cb = verts.Count;
                verts.Add(new Vector3(hx, 0f, 0f));
                verts.Add(new Vector3(hx, c0 * r, s0 * r));
                verts.Add(new Vector3(hx, c1 * r, s1 * r));
                uvs.Add(new Vector2(0.5f, 0.5f));
                uvs.Add(new Vector2(0.5f + c0 * 0.5f, 0.5f + s0 * 0.5f));
                uvs.Add(new Vector2(0.5f + c1 * 0.5f, 0.5f + s1 * 0.5f));
                tris.Add(cb); tris.Add(cb + 1); tris.Add(cb + 2);
            }
        };

        addCyl(radius, width);                        // 轮缘
        addCyl(radius * 0.42f, width * 1.18f);        // 轮毂（两侧各凸出一点，像真轮子）

        var m = new Mesh { name = "Sprocket" };
        for (int i = 0; i < verts.Count; i++) verts[i] += center;   // 挪回原网格所在位置
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>先拟合一次，再剔掉离圆太远的点重拟合（端部窗口里常混进直线段的点，会把半径撑大）。</summary>
    static bool FitCircleRefined(List<Vector2> pts, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;
        if (!FitCircle(pts, out center, out radius)) return false;
        for (int pass = 0; pass < 3; pass++)
        {
            float tol = Mathf.Max(radius * 0.06f, 1e-6f);
            var keep = new List<Vector2>(pts.Count);
            foreach (var p in pts)
                if (Mathf.Abs((p - center).magnitude - radius) <= tol) keep.Add(p);
            if (keep.Count < 6) break;
            if (!FitCircle(keep, out var c2, out float r2)) break;
            bool converged = Mathf.Abs(r2 - radius) < radius * 0.01f;
            center = c2;
            radius = r2;
            if (converged) break;
        }
        return true;
    }

    /// <summary>
    /// 按「离轮轴（网格局部 X 轴）的半径」截断网格：只保留三个顶点半径都不超过中位半径 1.25 倍的三角形，
    /// 再把顶点压紧。这样轮子上的尖刺没了，轮盘本体的细节全留着。
    /// 返回 null 表示没切到东西（不用换 mesh）。
    /// </summary>
    static Mesh TrimRadial(Mesh src, out float medianR)
    {
        medianR = 0f;
        if (src == null || src.subMeshCount != 1) return null;
        var pos = src.vertices;
        if (pos.Length < 32) return null;
        var tris = src.triangles;
        if (tris.Length < 12) return null;

        var bb = src.bounds;
        float cy = bb.center.y, cz = bb.center.z;
        var r = new float[pos.Length];
        for (int i = 0; i < pos.Length; i++)
        {
            float dy = pos[i].y - cy, dz = pos[i].z - cz;
            r[i] = Mathf.Sqrt(dy * dy + dz * dz);
        }
        var sorted = (float[])r.Clone();
        System.Array.Sort(sorted);
        medianR = sorted[sorted.Length / 2];
        if (medianR <= 1e-6f) return null;
        float limit = medianR * 1.25f;

        int triCount = tris.Length / 3;
        var keep = new bool[triCount];
        int kept = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (r[tris[t]] <= limit && r[tris[t + 1]] <= limit && r[tris[t + 2]] <= limit)
            {
                keep[t / 3] = true;
                kept++;
            }
        }
        if (kept == 0 || kept == triCount) return null;

        var map = new int[pos.Length];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        var newTris = new int[kept * 3];
        int nv = 0, k = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (!keep[t / 3]) continue;
            for (int j = 0; j < 3; j++)
            {
                int oi = tris[t + j];
                if (map[oi] < 0) map[oi] = nv++;
                newTris[k++] = map[oi];
            }
        }

        var nrm = src.normals;
        var uv = src.uv;
        var uv2 = src.uv2;
        var col = src.colors;
        var tan = src.tangents;
        bool hasN = nrm != null && nrm.Length == pos.Length;
        bool hasUv = uv != null && uv.Length == pos.Length;
        bool hasUv2 = uv2 != null && uv2.Length == pos.Length;
        bool hasCol = col != null && col.Length == pos.Length;
        bool hasTan = tan != null && tan.Length == pos.Length;

        var vpos = new Vector3[nv];
        var vnrm = new Vector3[nv];
        var vuv = new Vector2[nv];
        var vuv2 = new Vector2[nv];
        var vcol = new Color[nv];
        var vtan = new Vector4[nv];
        for (int oi = 0; oi < map.Length; oi++)
        {
            int ni = map[oi];
            if (ni < 0) continue;
            vpos[ni] = pos[oi];
            if (hasN) vnrm[ni] = nrm[oi];
            if (hasUv) vuv[ni] = uv[oi];
            if (hasUv2) vuv2[ni] = uv2[oi];
            if (hasCol) vcol[ni] = col[oi];
            if (hasTan) vtan[ni] = tan[oi];
        }

        var m = new Mesh { name = src.name + "_trim" };
        m.indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = vpos;
        if (hasN) m.normals = vnrm; else m.RecalculateNormals();
        if (hasUv) m.uv = vuv;
        if (hasUv2) m.uv2 = vuv2;
        if (hasCol) m.colors = vcol;
        if (hasTan) m.tangents = vtan;
        m.triangles = newTris;
        m.RecalculateBounds();
        return m;
    }

    void CollectBelts()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.ToLowerInvariant().StartsWith("belt")) continue;
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) continue;
            var b = BuildBelt(mf.sharedMesh, t);
            if (b == null) continue;
            // 履带中心相对车体中线的横向位置（网格局部 X × 缩放）
            float cx = mf.sharedMesh.bounds.center.x * Mathf.Abs(t.lossyScale.x);
            b.side = cx < 0f ? -1f : 1f;
            if (Mathf.Abs(cx) > 0.2f) _halfTrack = Mathf.Abs(cx);
            if (useGpuShader) SetupGpu(b, mr);
            _belts.Add(b);
        }
    }

    /// <summary>把这条履带的位移交给 Shader：写 UV2（弧长参数）+ 建一张中心线贴图。</summary>
    void SetupGpu(Belt b, MeshRenderer mr)
    {
        var sh = Shader.Find("Tank/TrackBelt");
        if (sh == null)
        {
            if (verbose) Debug.LogWarning("[TrackAssembly] 找不到 Tank/TrackBelt，退回 CPU 逐帧改顶点（很贵）");
            return;
        }

        // UV2.x = 弧长参数（0~1），UV2.y = 是否参与运动（Shader 里靠它跳过碎片顶点）
        int n = b.s.Length;
        var uv2 = new Vector2[n];
        for (int i = 0; i < n; i++) uv2[i] = new Vector2(b.s[i] / b.length, b.anim[i] ? 1f : 0f);
        b.mesh.SetUVs(1, uv2);

        // 中心线数据：xyz = (0, 中心线 y, 中心线 z)，w = 切线角
        var arr = new Vector4[256];
        for (int j = 0; j < 256; j++)
        {
            int k = Mathf.Min(j, b.N - 1);
            arr[j] = new Vector4(0f, b.cyz[k].x, b.cyz[k].y, b.ang[k]);
        }

        var src = mr.sharedMaterial;
        var m = new Material(sh) { name = b.tf.name + "_beltMat" };
        if (src != null)
        {
            m.mainTexture = src.mainTexture;
            m.color = src.color;
            if (src.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", src.GetFloat("_Glossiness"));
            if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        }
        m.SetFloat("_PathN", b.N);
        m.SetVectorArray("_PathArr", arr);
        m.SetFloat("_Scroll", 0f);

        // 所有材质槽都换成 GPU 履带材质。
        // 只设 sharedMaterial（= 第 0 槽）的话，网格里若还有别的 submesh，
        // 那几块仍然用原材质 → 那部分履带板永远不动（实测 belt_R 有 4 个 submesh 就中招了）。
        int slots = Mathf.Max(1, mr.sharedMaterials.Length);
        var mats = new Material[slots];
        for (int i = 0; i < slots; i++) mats[i] = m;
        mr.sharedMaterials = mats;

        b.mat = m;
        b.gpu = true;
        if (verbose) Debug.Log($"[TrackAssembly] 履带 {b.tf.name} 走 GPU 顶点动画（{b.N} 点中心线贴图）");
    }

    /// <summary>从几何本身反推履带环中心线，并算好每个顶点的弧长参数和偏移。</summary>
    Belt BuildBelt(Mesh src, Transform tf)
    {
        Mesh mesh = Object.Instantiate(src);
        mesh.name = src.name + "_runtime";

        // 有些履带网格里混进了跨到车体另一侧的面片（实测重坦履带里有一批 x≈1.9 m 的顶点，
        // 而履带本体只有 ±0.61 m 宽）——这些顶点会被算成"离中心线 2 m"，一推进就炸开。
        // 先按 X 带把它们连面片一起裁掉。
        var bandTrimmed = TrimToXBand(mesh);
        if (bandTrimmed != null)
        {
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
            mesh = bandTrimmed;
        }
        tf.GetComponent<MeshFilter>().sharedMesh = mesh;

        var pos = mesh.vertices;
        var nrm = mesh.normals;
        if (nrm == null || nrm.Length != pos.Length) nrm = new Vector3[pos.Length];
        if (pos.Length < 200) return null;

        float ymin = float.MaxValue, ymax = float.MinValue, zmin = float.MaxValue, zmax = float.MinValue;
        for (int i = 0; i < pos.Length; i++)
        {
            if (pos[i].y < ymin) ymin = pos[i].y;
            if (pos[i].y > ymax) ymax = pos[i].y;
            if (pos[i].z < zmin) zmin = pos[i].z;
            if (pos[i].z > zmax) zmax = pos[i].z;
        }
        float R = (zmax - zmin) * 0.5f;
        if (R < 1e-6f) return null;

        // ---- 1) 中心线：两遍 ----
        //   第一遍：全部顶点参与（碎片已经在 static 节点里被分走了）。
        //   第二遍：只留离第一遍中心线近的顶点，把个别残留杂物排除掉。
        //   旧版用「包围盒跑道形」粗筛，对不是标准跑道的履带环（比如托带轮把上沿顶得更高的
        //   重坦）会误杀大量真实顶点，中心线算歪 → 履带一推进就散架。2026-11-01 改掉。
        var maskAll = new bool[pos.Length];
        for (int i = 0; i < maskAll.Length; i++) maskAll[i] = true;

        if (!ComputeRing(pos, maskAll, out float cy, out float cz,
                         out Vector2[] cyz, out Vector2[] cdir, out float[] ang, out float len))
            return null;

        float nearLimit = 0.45f * R;
        var near = new bool[pos.Length];
        int nNear = 0;
        for (int i = 0; i < pos.Length; i++)
            if (DistanceToRing(pos[i], cyz) <= nearLimit) { near[i] = true; nNear++; }

        if (nNear > 256 && nNear < pos.Length)
        {
            if (ComputeRing(pos, near, out float cy2, out float cz2,
                            out Vector2[] cyz2, out Vector2[] cdir2, out float[] ang2, out float len2))
            {
                cy = cy2; cz = cz2; cyz = cyz2; cdir = cdir2; ang = ang2; len = len2;
            }
        }

        const int N = 256;
        var b = new Belt
        {
            mesh = mesh, tf = tf,
            basePos = pos, baseNrm = nrm,
            curPos = new Vector3[pos.Length], curNrm = new Vector3[pos.Length],
            s = new float[pos.Length], offY = new float[pos.Length], offZ = new float[pos.Length],
            th0 = new float[pos.Length], anim = new bool[pos.Length],
            length = len, unit = Mathf.Max(0.0001f, Mathf.Abs(tf.lossyScale.y)),
            N = N, cyz = cyz, cdir = cdir, ang = ang
        };

        // ---- 2) 每个顶点：角度 → 弧长参数 → 偏移 ----
        float a0 = ang[0];
        float bandLimit = 0.90f * R;
        var mArr = new float[pos.Length];
        int nAnim = 0;
        float maxOff = 0f;
        for (int i = 0; i < pos.Length; i++)
        {
            float a = Mathf.Atan2(pos[i].z - cz, pos[i].y - cy);
            float target = a0 + Mathf.Repeat(a - a0, 2f * Mathf.PI);
            int lo = 0, hi = N - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (ang[mid] <= target) lo = mid; else hi = mid;
            }
            float span = ang[hi] - ang[lo];
            float f = span > 1e-9f ? Mathf.Clamp01((target - ang[lo]) / span) : 0f;
            float s = ((lo + f) / N) * len;
            b.s[i] = Mathf.Repeat(s, len);

            Vector2 c = Sample(b, b.s[i], out float th);
            b.offY[i] = pos[i].y - c.x;
            b.offZ[i] = pos[i].z - c.y;
            b.th0[i] = th;
            mArr[i] = Mathf.Sqrt(b.offY[i] * b.offY[i] + b.offZ[i] * b.offZ[i]);
            b.curPos[i] = pos[i];
            b.curNrm[i] = nrm[i];
        }

        // ---- 3) 连通块判定「是不是履带环本体」----
        //   实测重坦的 belt 网格里混着一堆负重轮（离中心线 0.5~2 m），跟着走就会炸开。
        //   但履带本体自己是「一块块履带板」——很多小连通块，所以不能用"最大块"来判，
        //   改成看每块的**中位偏移**：贴着中心线的块是履带，离得远的块是混进来的轮子。
        var isBeltComp = new bool[pos.Length];
        {
            var parent = new int[pos.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            var triAll = new List<int>();
            for (int sIdx = 0; sIdx < mesh.subMeshCount; sIdx++) triAll.AddRange(mesh.GetTriangles(sIdx));
            for (int t = 0; t + 2 < triAll.Count; t += 3)
            {
                int a = Find(triAll[t]), b2 = Find(triAll[t + 1]), c2 = Find(triAll[t + 2]);
                if (a != b2) parent[b2] = a;
                if (a != c2) parent[c2] = Find(a);
            }

            var compM = new Dictionary<int, List<float>>();
            for (int i = 0; i < pos.Length; i++)
            {
                int r2 = Find(i);
                if (!compM.TryGetValue(r2, out var list)) { list = new List<float>(64); compM[r2] = list; }
                list.Add(mArr[i]);
            }

            float compLimit = 0.30f * R;
            var good = new HashSet<int>();
            foreach (var kv in compM)
            {
                kv.Value.Sort();
                // 用 p90 而不是中位数：履带板自己的顶点全贴着中心线；
                // 而"焊在履带板上的轮子"有一半顶点靠近履带、另一半伸进环里，中位数会漏判。
                int idx = Mathf.Clamp((int)(kv.Value.Count * 0.90f), 0, kv.Value.Count - 1);
                if (kv.Value[idx] <= compLimit) good.Add(kv.Key);
            }
            for (int i = 0; i < pos.Length; i++) isBeltComp[i] = good.Contains(Find(i));
        }

        for (int i = 0; i < pos.Length; i++)
        {
            b.anim[i] = isBeltComp[i] && mArr[i] <= bandLimit;
            if (b.anim[i]) { nAnim++; if (mArr[i] > maxOff) maxOff = mArr[i]; }
        }

        if (verbose)
            Debug.Log($"[TrackAssembly] 履带 {tf.name}: 顶点 {pos.Length}（参与运动 {nAnim}），" +
                      $"环长 {len * b.unit:F2} m，最大偏移 {maxOff * b.unit:F3} m");
        return b;
    }

    /// <summary>
    /// 只保留「落在履带自己那条 X 带里」的三角形（离履带中心超过 1.6 倍半宽的面片全部丢掉），
    /// 顶点压紧。用来清掉混进履带网格的跨侧面片。没裁到东西返回 null。
    /// </summary>
    static Mesh TrimToXBand(Mesh src)
    {
        if (src == null || src.subMeshCount < 1) return null;
        var pos = src.vertices;
        if (pos.Length < 64) return null;

        // 履带网格可能有多个 submesh（导出时按材质拆的），全部并成一份三角形再筛
        var triList = new List<int>();
        for (int s = 0; s < src.subMeshCount; s++) triList.AddRange(src.GetTriangles(s));
        if (triList.Count < 12) return null;
        var tris = triList.ToArray();

        // 包围盒会被这些离群顶点带偏（center 跟着跑），所以用分位数估计「履带本体」的横向中心与半宽
        var xs = new float[pos.Length];
        for (int i = 0; i < pos.Length; i++) xs[i] = pos[i].x;
        System.Array.Sort(xs);
        float cx = xs[pos.Length / 2];
        float lo = xs[Mathf.Clamp((int)(pos.Length * 0.05f), 0, pos.Length - 1)];
        float hi = xs[Mathf.Clamp((int)(pos.Length * 0.95f), 0, pos.Length - 1)];
        float halfW = Mathf.Max((hi - lo) * 0.5f, 1e-5f);
        float limit = halfW * 2.0f;

        int triCount = tris.Length / 3;
        var keep = new bool[triCount];
        int kept = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            bool ok = Mathf.Abs(pos[tris[t]].x - cx) <= limit
                   && Mathf.Abs(pos[tris[t + 1]].x - cx) <= limit
                   && Mathf.Abs(pos[tris[t + 2]].x - cx) <= limit;
            if (ok) { keep[t / 3] = true; kept++; }
        }
        if (kept == 0) return null;
        // 多 submesh 的也一律重建成单 submesh：不然只有第 0 号材质槽会被换掉，
        // 其它 submesh 还挂在旧材质上，那部分履带板就不会跟着动。
        if (kept == triCount && src.subMeshCount == 1) return null;

        // ---- 压紧顶点 ----
        var map = new int[pos.Length];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        var newTris = new int[kept * 3];
        int nv = 0, k = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (!keep[t / 3]) continue;
            for (int j = 0; j < 3; j++)
            {
                int oi = tris[t + j];
                if (map[oi] < 0) map[oi] = nv++;
                newTris[k++] = map[oi];
            }
        }

        var nrm = src.normals;
        var uv = src.uv;
        var uv2 = src.uv2;
        bool hasN = nrm != null && nrm.Length == pos.Length;
        bool hasUv = uv != null && uv.Length == pos.Length;
        bool hasUv2 = uv2 != null && uv2.Length == pos.Length;

        var vpos = new Vector3[nv];
        var vnrm = new Vector3[nv];
        var vuv = new Vector2[nv];
        var vuv2 = new Vector2[nv];
        for (int oi = 0; oi < map.Length; oi++)
        {
            int ni = map[oi];
            if (ni < 0) continue;
            vpos[ni] = pos[oi];
            if (hasN) vnrm[ni] = nrm[oi];
            if (hasUv) vuv[ni] = uv[oi];
            if (hasUv2) vuv2[ni] = uv2[oi];
        }

        var m = new Mesh { name = src.name + "_xband" };
        m.indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = vpos;
        if (hasN) m.normals = vnrm; else m.RecalculateNormals();
        if (hasUv) m.uv = vuv;
        if (hasUv2) m.uv2 = vuv2;
        m.triangles = newTris;
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// 从一组顶点里反推履带环中心线：质心 → 按角度分箱取均值 → 空箱插值 → 平滑 →
    /// 按弧长均匀重采样成 256 个点，并算好切线方向和展开后的角度（单调递增）。
    /// </summary>
    static bool ComputeRing(Vector3[] pos, bool[] mask, out float cy, out float cz,
        out Vector2[] cyz, out Vector2[] cdir, out float[] ang, out float len)
    {
        const int NB = 128;
        const int N = 256;
        cy = 0f; cz = 0f; cyz = null; cdir = null; ang = null; len = 0f;

        double sy = 0, sz = 0;
        int n = 0;
        for (int i = 0; i < pos.Length; i++)
            if (mask[i]) { sy += pos[i].y; sz += pos[i].z; n++; }
        if (n < 100) return false;
        cy = (float)(sy / n);
        cz = (float)(sz / n);

        var by = new float[NB];
        var bz = new float[NB];
        var bc = new int[NB];
        for (int i = 0; i < pos.Length; i++)
        {
            if (!mask[i]) continue;
            float a = Mathf.Atan2(pos[i].z - cz, pos[i].y - cy);
            int k = Mathf.Clamp((int)((a + Mathf.PI) / (2f * Mathf.PI) * NB), 0, NB - 1);
            by[k] += pos[i].y;
            bz[k] += pos[i].z;
            bc[k]++;
        }
        var py = new float[NB];
        var pz = new float[NB];
        for (int k = 0; k < NB; k++)
        {
            if (bc[k] > 0) { py[k] = by[k] / bc[k]; pz[k] = bz[k] / bc[k]; }
            else { py[k] = float.NaN; pz[k] = float.NaN; }
        }
        for (int k = 0; k < NB; k++)
        {
            if (!float.IsNaN(py[k])) continue;
            int ia = k, ib = k;
            for (int t = 1; t <= NB; t++) { ia = (k - t + NB) % NB; if (!float.IsNaN(py[ia])) break; }
            for (int t = 1; t <= NB; t++) { ib = (k + t) % NB; if (!float.IsNaN(py[ib])) break; }
            int da = (k - ia + NB) % NB, db = (ib - k + NB) % NB;
            float f = da + db > 0 ? da / (float)(da + db) : 0.5f;
            py[k] = Mathf.Lerp(py[ia], py[ib], f);
            pz[k] = Mathf.Lerp(pz[ia], pz[ib], f);
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var qy = new float[NB];
            var qz = new float[NB];
            for (int k = 0; k < NB; k++)
            {
                qy[k] = (py[(k + NB - 1) % NB] + py[k] * 2f + py[(k + 1) % NB]) * 0.25f;
                qz[k] = (pz[(k + NB - 1) % NB] + pz[k] * 2f + pz[(k + 1) % NB]) * 0.25f;
            }
            py = qy;
            pz = qz;
        }

        var cum = new float[NB + 1];
        for (int k = 0; k < NB; k++)
        {
            int k2 = (k + 1) % NB;
            float dy = py[k2] - py[k], dz = pz[k2] - pz[k];
            cum[k + 1] = cum[k] + Mathf.Sqrt(dy * dy + dz * dz);
        }
        len = cum[NB];
        if (len < 1e-4f) return false;

        cyz = new Vector2[N];
        cdir = new Vector2[N];
        ang = new float[N];
        for (int j = 0; j < N; j++)
        {
            float target = (float)j / N * len;
            int seg = 0;
            while (seg < NB - 1 && cum[seg + 1] < target) seg++;
            float segLen = cum[seg + 1] - cum[seg];
            float f = segLen > 1e-9f ? (target - cum[seg]) / segLen : 0f;
            int ia = seg, ib = (seg + 1) % NB;
            cyz[j] = new Vector2(Mathf.Lerp(py[ia], py[ib], f), Mathf.Lerp(pz[ia], pz[ib], f));
        }
        for (int j = 0; j < N; j++)
        {
            Vector2 d = cyz[(j + 1) % N] - cyz[(j + N - 1) % N];
            cdir[j] = d.sqrMagnitude > 1e-12f ? d.normalized : Vector2.right;
            float a = Mathf.Atan2(cyz[j].y - cz, cyz[j].x - cy);
            if (j == 0) ang[0] = a;
            else ang[j] = ang[j - 1] + Mathf.Repeat(a - ang[j - 1], 2f * Mathf.PI);
        }
        for (int j = 1; j < N; j++)
            if (ang[j] <= ang[j - 1]) ang[j] = ang[j - 1] + 1e-5f;
        return true;
    }

    /// <summary>顶点到中心线的最短距离（只看 YZ 平面）。</summary>
    static float DistanceToRing(Vector3 p, Vector2[] ring)
    {
        float best = float.MaxValue;
        for (int j = 0; j < ring.Length; j++)
        {
            float dy = p.y - ring[j].x, dz = p.z - ring[j].y;
            float d = dy * dy + dz * dz;
            if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }

    /// <summary>按弧长参数取中心线点和切线角。</summary>
    static Vector2 Sample(Belt b, float s, out float th)
    {
        float u = Mathf.Repeat(s, b.length) / b.length * b.N;
        int i0 = (int)u;
        if (i0 >= b.N) i0 = b.N - 1;
        float f = u - i0;
        int i1 = (i0 + 1) % b.N;
        Vector2 c = Vector2.Lerp(b.cyz[i0], b.cyz[i1], f);
        Vector2 d = Vector2.Lerp(b.cdir[i0], b.cdir[i1], f);
        th = d.sqrMagnitude > 1e-9f ? Mathf.Atan2(d.y, d.x) : Mathf.Atan2(b.cdir[i0].y, b.cdir[i0].x);
        return c;
    }

    // ---------------- 包围盒跑道形（只用来给顶点找初值） ----------------
    static float ParamStadium(float yc, float zc, float R, float hs, float y, float z)
    {
        float sTop = 2f * hs;
        float sArc = Mathf.PI * R;
        if (y > yc + hs)
        {
            float phi = Mathf.Atan2(z - zc, y - (yc + hs));
            return sTop + (Mathf.PI * 0.5f - phi) * R;
        }
        if (y < yc - hs)
        {
            float phi = Mathf.Atan2(z - zc, y - (yc - hs));
            if (phi > 0f) phi -= 2f * Mathf.PI;
            return sTop + sArc + sTop + (-Mathf.PI * 0.5f - phi) * R;
        }
        if (z > zc) return y - (yc - hs);
        return sTop + sArc + (yc + hs - y);
    }

    static Vector2 StadiumPoint(float yc, float zc, float R, float hs, float s, out float th)
    {
        float sTop = 2f * hs;
        float sArc = Mathf.PI * R;
        float len = 4f * hs + 2f * Mathf.PI * R;
        s = Mathf.Repeat(s, len);
        if (s < sTop) { th = 0f; return new Vector2(yc - hs + s, zc + R); }
        s -= sTop;
        if (s < sArc)
        {
            float phi = Mathf.PI * 0.5f - s / R;
            th = Mathf.Atan2(-Mathf.Cos(phi), Mathf.Sin(phi));
            return new Vector2(yc + hs + R * Mathf.Cos(phi), zc + R * Mathf.Sin(phi));
        }
        s -= sArc;
        if (s < sTop) { th = Mathf.PI; return new Vector2(yc + hs - s, zc - R); }
        s -= sTop;
        float ph = -Mathf.PI * 0.5f - s / R;
        th = Mathf.Atan2(-Mathf.Cos(ph), Mathf.Sin(ph));
        return new Vector2(yc - hs + R * Mathf.Cos(ph), zc + R * Mathf.Sin(ph));
    }

    float CurrentSpeed()
    {
        float v = debugSpeed;
        if (vehicle != null) v += vehicle.speed;
        return v;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float v = CurrentSpeed();

        // ---- 转向：左右履带速度不一样（履带车差速转向）----
        // 右转（yaw 增大）时：左履带往前、右履带往后；左转反过来。
        float yaw = transform.eulerAngles.y;
        float yawRate = 0f;
        if (_hasPrevYaw) yawRate = Mathf.DeltaAngle(_prevYaw, yaw) * Mathf.Deg2Rad / dt;
        _prevYaw = yaw;
        _hasPrevYaw = true;

        float vLeft = v + yawRate * _halfTrack;
        float vRight = v - yawRate * _halfTrack;

        // ---- 轮子：绕自身轴心转，ω = 该侧线速度 / R ----
        for (int i = 0; i < _wheels.Count; i++)
        {
            var w = _wheels[i];
            if (w.t == null) continue;
            float sv = w.side < 0f ? vLeft : vRight;
            if (Mathf.Abs(sv) < 0.001f) continue;
            float deg = sv / w.radius * Mathf.Rad2Deg * dt * spinSign;
            w.t.Rotate(deg, 0f, 0f, Space.Self);
        }

        // ---- 履带：沿环路径前进 ----
        if (_belts.Count == 0) return;
        _scrollL += vLeft * dt * beltSpeedScale;
        _scrollR += vRight * dt * beltSpeedScale;
        if (Mathf.Abs(vLeft) < 0.001f && Mathf.Abs(vRight) < 0.001f) return;

        var cam = Camera.main;
        if (cam != null && animateDistance > 0f)
        {
            float d = (cam.transform.position - transform.position).sqrMagnitude;
            if (d > animateDistance * animateDistance) return;
        }

        float dir = spinSign * beltSign;
        for (int bi = 0; bi < _belts.Count; bi++)
        {
            var b = _belts[bi];
            DriveBelt(b, (b.side < 0f ? _scrollL : _scrollR) * dir);
        }
    }

    /// <summary>推一条履带：能走 GPU 就只改一个 float，否则退回逐帧改顶点。</summary>
    void DriveBelt(Belt b, float travel)
    {
        if (useGpuShader && b.gpu && b.mat != null)
        {
            // Shader 里的 s 是 0~1 的弧长参数，所以换算成「绕了几圈的小数部分」
            float t = Mathf.Repeat(travel / b.unit / b.length, 1f);
            b.mat.SetFloat("_Scroll", t);
            return;
        }
        AnimateBelt(b, travel);
    }

    void AnimateBelt(Belt b, float travel)
    {
        float travelLocal = travel / b.unit;      // 米 → 网格局部单位
        var p = b.curPos;
        var n = b.curNrm;
        int count = b.s.Length;
        int N = b.N;
        var cyz = b.cyz;
        var ang = b.ang;
        float scale = N / b.length;
        float ang0 = ang[0];
        float angEnd = ang0 + 2f * Mathf.PI;
        const float TWO_PI = 6.2831853f;

        for (int i = 0; i < count; i++)
        {
            if (!b.anim[i]) { p[i] = b.basePos[i]; n[i] = b.baseNrm[i]; continue; }

            // 弧长参数 → 中心线采样点（不用 atan2，省一半时间）
            float u = (b.s[i] + travelLocal) * scale;
            float q = u / N;
            float loops = Mathf.Floor(q);
            float r = u - loops * N;
            int i0 = (int)r;
            if (i0 >= N) i0 = N - 1;
            else if (i0 < 0) i0 = 0;
            float f = r - i0;
            int i1 = i0 + 1;
            Vector2 c0 = cyz[i0];
            Vector2 c2 = i1 < N ? Vector2.Lerp(c0, cyz[i1], f) : Vector2.Lerp(c0, cyz[0], f);
            float th2 = Mathf.Lerp(ang[i0], i1 < N ? ang[i1] : angEnd, f) + TWO_PI * loops;

            float dth = Mathf.Repeat(th2 - b.th0[i] + Mathf.PI, TWO_PI) - Mathf.PI;
            float cs = Mathf.Cos(dth), sn = Mathf.Sin(dth);
            float oy = b.offY[i], oz = b.offZ[i];
            Vector3 bp = b.basePos[i];
            p[i].x = bp.x;
            p[i].y = c2.x + oy * cs - oz * sn;
            p[i].z = c2.y + oy * sn + oz * cs;

            Vector3 bn = b.baseNrm[i];
            n[i].x = bn.x;
            n[i].y = bn.y * cs - bn.z * sn;
            n[i].z = bn.y * sn + bn.z * cs;
        }
        b.mesh.SetVertices(p);
        b.mesh.SetNormals(n);
    }

    // ---------------- 下面几个只给编辑器里做验证用 ----------------
    /// <summary>编辑器里手动初始化（非运行时 Start 不会自动跑）。</summary>
    public void DebugSetup()
    {
        if (_wheels.Count == 0 && _belts.Count == 0) Start();
    }

    /// <summary>编辑器里手动推进：轮子转 meters/r 弧度，履带前进 meters 米（左右同速）。</summary>
    public void DebugAdvance(float meters)
    {
        DebugAdvanceSides(meters, meters);
    }

    /// <summary>编辑器里手动推进，左右两侧各走各自的米数（测差速转向用）。</summary>
    public void DebugAdvanceSides(float left, float right)
    {
        DebugSetup();
        foreach (var w in _wheels)
        {
            if (w.t == null) continue;
            float m = w.side < 0f ? left : right;
            w.t.Rotate(m / w.radius * Mathf.Rad2Deg * spinSign, 0f, 0f, Space.Self);
        }
        _scrollL += left;
        _scrollR += right;
        foreach (var b in _belts)
            DriveBelt(b, (b.side < 0f ? _scrollL : _scrollR) * spinSign * beltSign);
    }

    /// <summary>编辑器里把履带还原到初始姿态。</summary>
    public void DebugReset()
    {
        foreach (var b in _belts)
        {
            System.Array.Copy(b.basePos, b.curPos, b.basePos.Length);
            System.Array.Copy(b.baseNrm, b.curNrm, b.baseNrm.Length);
            if (!b.gpu)
            {
                b.mesh.SetVertices(b.curPos);
                b.mesh.SetNormals(b.curNrm);
            }
            else if (b.mat != null) b.mat.SetFloat("_Scroll", 0f);
        }
        _scrollL = 0f;
        _scrollR = 0f;
    }
}
