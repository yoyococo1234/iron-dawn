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
    [Tooltip("GPU 顶点动画（实验：中心线数据传不进 shader，暂时别开）")]
    public bool useGpuShader = false;
    [Tooltip("打印诊断信息")]
    public bool verbose = false;

    class Wheel
    {
        public Transform t;
        public float radius;
        public float side;        // -1 = 左，+1 = 右
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
        CollectWheels();
        CollectBelts();
        _prevYaw = transform.eulerAngles.y;
        _hasPrevYaw = true;
        if (verbose)
            Debug.Log($"[TrackAssembly] {name}: 轮子 {_wheels.Count} 个，履带 {_belts.Count} 条，半轮距 {_halfTrack:F2} m");
    }

    void CollectWheels()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.ToLowerInvariant().StartsWith("wheel")) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // 网格局部空间：X = 车宽（轮轴方向），Y = 车长，Z = 高
            var sz = mf.sharedMesh.bounds.size;
            float r = Mathf.Min(sz.y, sz.z) * 0.5f * Mathf.Abs(t.lossyScale.y);
            if (r < 0.02f) r = Mathf.Max(sz.y, sz.z) * 0.5f * Mathf.Abs(t.lossyScale.y);
            float x = transform.InverseTransformPoint(t.position).x;
            _wheels.Add(new Wheel { t = t, radius = Mathf.Max(0.05f, r), side = x < 0f ? -1f : 1f });
        }
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

        // UV2.x = 弧长参数（0~1）
        int n = b.s.Length;
        var uv2 = new Vector2[n];
        for (int i = 0; i < n; i++) uv2[i] = new Vector2(b.s[i] / b.length, 0f);
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
        mr.sharedMaterial = m;

        b.mat = m;
        b.gpu = true;
        if (verbose) Debug.Log($"[TrackAssembly] 履带 {b.tf.name} 走 GPU 顶点动画（{b.N} 点中心线贴图）");
    }

    /// <summary>从几何本身反推履带环中心线，并算好每个顶点的弧长参数和偏移。</summary>
    Belt BuildBelt(Mesh src, Transform tf)
    {
        Mesh mesh = Object.Instantiate(src);
        mesh.name = src.name + "_runtime";
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
        float zc = (zmin + zmax) * 0.5f;
        float yc = (ymin + ymax) * 0.5f;
        float hs = Mathf.Max(0.02f, (ymax - ymin) * 0.5f - R);

        // ---- 1) 用包围盒跑道形粗筛：只留「大概率属于履带板本体」的顶点 ----
        //      （粘进来的碎片、零件残渣离环很远，不能参与求中心线）
        var rough = new bool[pos.Length];
        float roughLimit = 0.55f * R;
        for (int i = 0; i < pos.Length; i++)
        {
            float s0 = ParamStadium(yc, zc, R, hs, pos[i].y, pos[i].z);
            Vector2 c0 = StadiumPoint(yc, zc, R, hs, s0, out _);
            float dy = pos[i].y - c0.x, dz = pos[i].z - c0.y;
            rough[i] = dy * dy + dz * dz <= roughLimit * roughLimit;
        }

        // ---- 2) 质心 + 按角度分箱取均值 → 环的中心线（环是凸的，角度对弧长单调）----
        double sy = 0, sz = 0;
        int nRough = 0;
        for (int i = 0; i < pos.Length; i++)
            if (rough[i]) { sy += pos[i].y; sz += pos[i].z; nRough++; }
        if (nRough < 100) return null;
        float cy = (float)(sy / nRough), cz = (float)(sz / nRough);

        const int NB = 128;
        var by = new float[NB];
        var bz = new float[NB];
        var bc = new int[NB];
        for (int i = 0; i < pos.Length; i++)
        {
            if (!rough[i]) continue;
            float a = Mathf.Atan2(pos[i].z - cz, pos[i].y - cy);
            int k = Mathf.Clamp((int)((a + Mathf.PI) / (2f * Mathf.PI) * NB), 0, NB - 1);
            by[k] += pos[i].y;
            bz[k] += pos[i].z;
            bc[k]++;
        }
        // 空箱用左右邻插值补上
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
        // 轻微平滑
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

        // ---- 3) 按弧长均匀重采样 ----
        var cum = new float[NB + 1];
        for (int k = 0; k < NB; k++)
        {
            int k2 = (k + 1) % NB;
            cum[k + 1] = cum[k] + Mathf.Sqrt((py[k2] - py[k]) * (py[k2] - py[k]) + (pz[k2] - pz[k]) * (pz[k2] - pz[k]));
        }
        float len = cum[NB];
        if (len < 1e-4f) return null;

        const int N = 256;
        var cyz = new Vector2[N];
        var cdir = new Vector2[N];
        var ang = new float[N];          // 展开后的角度，单调递增
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
            else
            {
                float prev = ang[j - 1];
                float d2 = Mathf.Repeat(a - prev, 2f * Mathf.PI);
                ang[j] = prev + d2;
            }
        }
        // 保证 ang 严格递增（个别点重合时）
        for (int j = 1; j < N; j++)
            if (ang[j] <= ang[j - 1]) ang[j] = ang[j - 1] + 1e-5f;

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

        // ---- 4) 每个顶点：角度 → 弧长参数 → 偏移 ----
        float a0 = ang[0];
        float bandLimit = 0.90f * R;
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
            float m = Mathf.Sqrt(b.offY[i] * b.offY[i] + b.offZ[i] * b.offZ[i]);
            b.anim[i] = m <= bandLimit;
            if (b.anim[i]) { nAnim++; if (m > maxOff) maxOff = m; }
            b.curPos[i] = pos[i];
            b.curNrm[i] = nrm[i];
        }

        if (verbose)
            Debug.Log($"[TrackAssembly] 履带 {tf.name}: 顶点 {pos.Length}（参与运动 {nAnim}），" +
                      $"环长 {len * b.unit:F2} m，最大偏移 {maxOff * b.unit:F3} m");
        return b;
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
