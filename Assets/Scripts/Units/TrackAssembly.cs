using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 履带总成（Lux3D 单独生成「履带 + 负重轮」再 part_split 拆出来的）：
///   · wheel_*  每个轮子原点在自己几何中心 → 按 ω = v / R 绕自身轴原地转
///   · belt     履带板几何不动，只滚贴图（UV 是 build_tracks.py 里按车长铺好的）
/// 这样轮子在原地转、履带纹路往前跑，方向和速度一致。
/// </summary>
public class TrackAssembly : MonoBehaviour
{
    [Tooltip("载具物理（取车速）。留空自动往上找")]
    public VehiclePhysics vehicle;

    [Tooltip("履带板节距（米），和生成 UV 时用的一致")]
    public float treadMeters = 0.22f;

    [Tooltip("贴图重复密度倍率：1 = 按 treadMeters 铺；调小让履带板变大、动起来更明显")]
    public float uvDensity = 0.55f;

    [Header("按车体履带槽生成（推荐）")]
    [Tooltip("旧履带模型：只用来量车体上履带槽的包围盒，然后按这个尺寸生成轮子+履带")]
    public GameObject wellReference;
    [Tooltip("每侧负重轮个数（0 = 按长度自动算）")]
    public int wheelsPerSide = 0;

    [Tooltip("轮子转向：1 = 前进时上沿向前（一般就是 1，反了改成 -1）")]
    public float spinSign = 1f;

    readonly List<Transform> _wheels = new List<Transform>();
    readonly List<float> _radii = new List<float>();
    Material _beltMat;
    Material[] _treadMats;
    float _wheelRadius = 0.5f;
    Texture2D _tex;

    void Start()
    {
        // 注意：必须放在 Start 而不是 Awake ——
        // VehicleModelBinder 是「AddComponent 之后再赋 wellReference」，
        // Awake 跑的时候字段还是 null，会走错分支（用模型自带的破履带）。
        if (vehicle == null) vehicle = GetComponentInParent<VehiclePhysics>();
        if (wellReference != null) { BuildFitted(); return; }
        Collect();
        SetupBelt();
    }

    /// <summary>
    /// 按车体履带槽的实际包围盒生成：6 个负重轮 + 一条跑道形履带。
    /// 尺寸全部从 wellReference（原模型履带）量出来，所以不会出现"短一截/轮子比槽大"。
    /// </summary>
    void BuildFitted()
    {
        // 1) 量履带槽
        var probe = Object.Instantiate(wellReference, transform);
        probe.name = "WellProbe";
        var toLocal = transform.parent != null ? transform.parent.worldToLocalMatrix : transform.worldToLocalMatrix;
        bool any = false;
        var wb = new Bounds();
        foreach (var r in probe.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = toLocal.MultiplyPoint3x4(c);
                if (!any) { wb = new Bounds(p, Vector3.zero); any = true; }
                else wb.Encapsulate(p);
            }
        }
        Object.Destroy(probe);
        if (!any) return;

        // 2) 藏掉模型自带的履带，按槽尺寸生成
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        float halfW = wb.size.x * 0.5f;
        float beltW = Mathf.Clamp(wb.size.x * 0.16f, 0.35f, 0.75f);   // 单条履带宽度
        float R = Mathf.Clamp(wb.size.y * 0.40f, 0.25f, 0.95f);        // 负重轮半径
        float axleY = wb.min.y + R;
        float zMin = wb.min.z + R;
        float zMax = wb.max.z - R;
        float sideX = wb.center.x + halfW - beltW * 0.55f;
        _wheelRadius = R;

        for (int s = -1; s <= 1; s += 2)
        {
            float x = wb.center.x + s * (halfW - beltW * 0.55f);
            int n = wheelsPerSide > 0 ? wheelsPerSide
                  : Mathf.Clamp(Mathf.RoundToInt((zMax - zMin) / (R * 1.5f)) + 1, 3, 8);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float z = Mathf.Lerp(zMin, zMax, t);
                var wheel = MakeCylinder(new Vector3(x, axleY, z), R * 0.94f, beltW * 0.9f,
                                         new Color(0.36f, 0.36f, 0.35f), $"wheel_{s}_{i}");
                _wheels.Add(wheel);
                var hub = MakeCylinder(new Vector3(x, axleY, z), R * 0.30f, beltW * 1.02f,
                                       new Color(0.52f, 0.52f, 0.50f), $"hub_{s}_{i}");
                _wheels.Add(hub);
            }
            BuildBand(x, axleY, R, zMin, zMax, beltW);
        }

        BuildTexture();
        Debug.Log($"[TrackAssembly] 按槽生成：槽 {wb.size} / 轮半径 {R:F2} / 履带宽 {beltW:F2}");
    }

    /// <summary>跑道形履带：两段直线 + 两端半圆，UV 沿路径铺。</summary>
    void BuildBand(float x, float axleY, float R, float zMin, float zMax, float width)
    {
        const int segStraight = 10;
        const int segArc = 10;
        float hw = width * 0.5f;
        var path = new List<Vector2>();

        for (int i = 0; i <= segStraight; i++)
            path.Add(new Vector2(Mathf.Lerp(zMin, zMax, i / (float)segStraight), -R));
        for (int i = 1; i < segArc; i++)
        {
            float a = -Mathf.PI * 0.5f + Mathf.PI * i / segArc;
            path.Add(new Vector2(zMax + Mathf.Cos(a) * R, Mathf.Sin(a) * R));
        }
        for (int i = 0; i <= segStraight; i++)
            path.Add(new Vector2(Mathf.Lerp(zMax, zMin, i / (float)segStraight), R));
        for (int i = 1; i < segArc; i++)
        {
            float a = Mathf.PI * 0.5f + Mathf.PI * i / segArc;
            path.Add(new Vector2(zMin + Mathf.Cos(a) * R, Mathf.Sin(a) * R));
        }

        int n = path.Count;
        var verts = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        var tris = new List<int>();
        float arc = 0f;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) arc += Vector2.Distance(path[i], path[i - 1]);
            float u = arc / Mathf.Max(0.05f, treadMeters);
            verts[i * 2] = new Vector3(x - hw, axleY + path[i].y, path[i].x);
            verts[i * 2 + 1] = new Vector3(x + hw, axleY + path[i].y, path[i].x);
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = i * 2, b = i * 2 + 1, c = j * 2, d = j * 2 + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        var mesh = new Mesh { name = "Band" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("belt");
        go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        BuildTexture();
        mr.sharedMaterial = BeltMaterial();
    }

    void Collect()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.StartsWith("wheel")) continue;

            // 轮子不一定绕"自己的几何中心"转：FBX 导出时对象位移可能被烘进网格，
            // 结果几个轮子共用同一个原点（表现就是"绕着共同的中心转"）。
            // 所以这里给每个轮子加一个**枢轴**，枢轴放在它自己的几何中心，转枢轴而不是转轮子。
            var mf = t.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Vector3 centerParent;
            if (t.parent != null)
                centerParent = t.parent.InverseTransformPoint(t.TransformPoint(mf.sharedMesh.bounds.center));
            else
                centerParent = mf.sharedMesh.bounds.center;

            var pivot = new GameObject(t.name + "_pivot");
            pivot.transform.SetParent(t.parent, false);
            pivot.transform.localPosition = centerParent;

            Vector3 oldLocal = t.localPosition;
            t.SetParent(pivot.transform, false);
            t.localPosition = oldLocal - centerParent;

            // 半径 = 网格包围盒高度的一半（换到世界尺度）
            float r = Mathf.Max(0.1f, mf.sharedMesh.bounds.size.y * t.lossyScale.y * 0.5f);
            _wheels.Add(pivot.transform);
            _radii.Add(r);
        }
    }

    void SetupBelt()
    {
        BuildTexture();
        _beltMat = new Material(Shader.Find("Standard")) { name = "BeltTreadMat" };
        _beltMat.color = Color.white;
        _beltMat.mainTexture = _tex;
        _beltMat.SetFloat("_Glossiness", 0.05f);
        _beltMat.SetFloat("_Metallic", 0.1f);
        // 贴图重复密度：UV 是按 0.22 m 一块铺的，这里再乘一个倍率把板放大，
        // 否则板太密、跑起来会频闪（看起来就像"没动"）。
        _beltMat.mainTextureScale = new Vector2(Mathf.Max(0.05f, uvDensity), 1f);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform.name.ToLowerInvariant().StartsWith("wheel")) continue;
            r.sharedMaterial = _beltMat;
        }
    }

    Material _treadMat;

    /// <summary>造一个圆柱（轮子/轮毂），轴向 Y，挂到车体上（米制）。</summary>
    Transform MakeCylinder(Vector3 pos, float radius, float width, Color color, string name)
    {
        var mesh = CylinderMesh(radius, width, 14);
        var go = new GameObject(name);
        go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 轴向 Y → X（车宽）
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SimpleMaterial(color);
        return go.transform;
    }

    static Mesh CylinderMesh(float radius, float width, int seg)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        float h = width * 0.5f;

        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.PI * 2f * i / seg;
            float cx = Mathf.Cos(a) * radius, cz = Mathf.Sin(a) * radius;
            verts.Add(new Vector3(cx, -h, cz)); uvs.Add(new Vector2(i / (float)seg, 0f));
            verts.Add(new Vector3(cx, h, cz)); uvs.Add(new Vector2(i / (float)seg, 1f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        for (int s = 0; s < 2; s++)
        {
            float y = s == 0 ? -h : h;
            int cIdx = verts.Count;
            verts.Add(new Vector3(0f, y, 0f)); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                int v0 = cIdx + 1 + i, v1 = cIdx + 1 + i + 1;
                if (s == 0) { tris.Add(cIdx); tris.Add(v0); tris.Add(v1); }
                else { tris.Add(cIdx); tris.Add(v1); tris.Add(v0); }
            }
        }

        var m = new Mesh { name = "Cyl" };
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    Material BeltMaterial()
    {
        if (_treadMat != null) return _treadMat;
        BuildTexture();
        _treadMat = new Material(Shader.Find("Standard")) { name = "BeltTreadMat" };
        _treadMat.color = Color.white;
        _treadMat.mainTexture = _tex;
        _treadMat.SetFloat("_Glossiness", 0.05f);
        _treadMat.SetFloat("_Metallic", 0.1f);
        _treadMat.mainTextureScale = new Vector2(Mathf.Max(0.05f, uvDensity), 1f);
        _beltMat = _treadMat;
        _treadMats = new[] { _treadMat };
        return _treadMat;
    }

    Material SimpleMaterial(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.18f);
        m.SetFloat("_Metallic", 0.35f);
        return m;
    }

    /// <summary>履带板贴图：一块块带齿的钢板 + 深缝。</summary>
    void BuildTexture()
    {
        const int W = 128, H = 16;
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        _tex.name = "BeltTread";
        _tex.wrapMode = TextureWrapMode.Repeat;
        _tex.filterMode = FilterMode.Bilinear;
        var px = new Color[W * H];
        var dark = new Color(0.10f, 0.10f, 0.10f);
        var light = new Color(0.66f, 0.66f, 0.63f);
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)W;
                Color c;
                if (t < 0.15f) c = dark;
                else
                {
                    float u = (t - 0.15f) / 0.85f;
                    c = Color.Lerp(light * 0.6f, light, Mathf.Sin(u * Mathf.PI));
                    float tooth = Mathf.Max(
                        Mathf.SmoothStep(1f, 0f, Mathf.Abs(v - 0.2f) * 6f),
                        Mathf.SmoothStep(1f, 0f, Mathf.Abs(v - 0.8f) * 6f));
                    c = Color.Lerp(c, light * 1.25f, tooth * 0.5f);
                    if (u < 0.1f || u > 0.9f) c *= 0.55f;
                }
                float edge = Mathf.Min(y, H - 1 - y) / (float)H;
                c *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(edge * 6f));
                px[y * W + x] = c;
            }
        }
        _tex.SetPixels(px);
        _tex.Apply(true);
    }

    void Update()
    {
        float speed = vehicle != null ? vehicle.speed : 0f;
        if (Mathf.Abs(speed) < 0.02f) return;
        float dt = Time.deltaTime;

        // 履带板：贴图沿 u 滚（u 是按车长铺的）
        // 注意 mainTextureScale 会缩放 UV，所以位移要按 uvDensity 折算，
        // 保证"贴图位移速度 = 车速"（否则轮子和履带看起来会不同步）。
        if (_beltMat != null)
        {
            float du = speed * dt / Mathf.Max(0.02f, treadMeters) * Mathf.Max(0.05f, uvDensity) * spinSign;
            var o = _beltMat.mainTextureOffset;
            o.x = Mathf.Repeat(o.x - du, 1f);
            _beltMat.mainTextureOffset = o;
        }

        // 负重轮：绕自身轴原地转，ω = v / R。
        // FBX 根节点带 −90°X 旋转，所以子物体的**局部 X 就是车宽方向**（轮轴）。
        for (int i = 0; i < _wheels.Count; i++)
        {
            var w = _wheels[i];
            if (w == null) continue;
            float deg = speed / Mathf.Max(0.05f, _radii[i]) * Mathf.Rad2Deg * dt * spinSign;
            w.Rotate(deg, 0f, 0f, Space.Self);
        }
    }
}
