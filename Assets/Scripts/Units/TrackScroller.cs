using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 履带 + 负重轮（程序生成，替换模型里那块"轮子履带焊在一起"的面片）。
///
/// 为什么不用模型自带的：Lux3D 的履带是一整块面片汤，轮子和履带板焊死在一起
/// （用 loose parts 拆会碎成 1000+ 块，最大的碎片才 180 面），没法单独转轮子。
/// 所以这里按模型的尺寸**自己生成**：
///   · 每侧 N 个负重轮（圆柱），按车速绕自己的轴转
///   · 一条"跑道形"履带板（两段直线 + 两端半圆），UV 沿路径铺，按车速滚
/// 模型自带的履带会被隐藏，避免两套重叠。
/// </summary>
public class TrackScroller : MonoBehaviour
{
    [Tooltip("载具物理（取车速）。留空会自动往上找")]
    public VehiclePhysics vehicle;

    [Header("程序生成")]
    [Tooltip("用程序生成的轮子+履带替换模型自带的那块（目前比例对不上，默认关）")]
    public bool replaceWithProcedural = false;
    [Tooltip("履带板节距（米）")]
    public float treadMeters = 0.24f;
    [Tooltip("每侧负重轮数量（0 = 按尺寸自动算）")]
    public int wheelsPerSide = 0;
    [Tooltip("负重轮半径占履带高度的一半的比例")]
    public float wheelRadiusRatio = 0.86f;

    [Header("颜色")]
    public Color treadColor = new Color(0.17f, 0.17f, 0.17f);
    public Color linkColor = new Color(0.55f, 0.55f, 0.53f);
    public Color wheelColor = new Color(0.34f, 0.34f, 0.33f);
    public Color hubColor = new Color(0.48f, 0.48f, 0.46f);

    Material[] _treadMats;
    readonly List<Transform> _wheels = new List<Transform>();
    float _wheelRadius = 0.4f;
    Texture2D _tex;

    void Awake()
    {
        if (vehicle == null) vehicle = GetComponentInParent<VehiclePhysics>();
        if (replaceWithProcedural) Build();
        else BuildTreadMaterialOnly();
    }

    // ------------------------------------------------------------------
    //  主流程：量尺寸 → 生成轮子和履带 → 隐藏原模型
    // ------------------------------------------------------------------
    void Build()
    {
        // 1) 用**渲染包围盒**量（世界 AABB 的 8 个角换算到车体局部）。
        // 不能直接拿 mesh.vertices：FBX 根节点带 100 倍缩放，手算容易差 100 倍。
        var space = transform.parent != null ? transform.parent : transform;
        var toLocal = space.worldToLocalMatrix;

        bool any = false;
        var b = new Bounds();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = wb.center + Vector3.Scale(wb.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = toLocal.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        if (!any) return;

        float midX = b.center.x;
        BuildSide(b, midX, -1f);
        BuildSide(b, midX, +1f);

        // 3) 隐藏模型自带的履带
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    void BuildSide(Bounds b, float midX, float sign)
    {
        // 每侧宽度 = 总宽的一半；高度/长度用整体
        float halfW = b.size.x * 0.25f;
        float x = midX + sign * halfW;

        float height = b.size.y;
        float R = Mathf.Max(0.15f, height * 0.5f * Mathf.Clamp01(wheelRadiusRatio));
        _wheelRadius = R;
        float axleY = b.center.y;
        float zMin = b.min.z + R;
        float zMax = b.max.z - R;
        if (zMax <= zMin) { zMin = b.min.z; zMax = b.max.z; }
        float width = Mathf.Max(0.2f, b.size.x * 0.30f);   // 履带宽度

        int n = wheelsPerSide > 0 ? wheelsPerSide
              : Mathf.Clamp(Mathf.RoundToInt((zMax - zMin) / (R * 1.55f)) + 1, 3, 9);

        // ---- 负重轮 ----
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0.5f : i / (float)(n - 1);
            float z = Mathf.Lerp(zMin, zMax, t);
            var wheel = MakeCylinder(new Vector3(x, axleY, z), R * 0.92f, width * 0.92f, wheelColor, $"Wheel_{sign}_{i}");
            _wheels.Add(wheel);
            // 轮毂（小一圈、亮一点），转动时看得更明显
            var hub = MakeCylinder(new Vector3(x, axleY, z), R * 0.34f, width * 1.02f, hubColor, $"Hub_{sign}_{i}");
            _wheels.Add(hub);
        }

        // ---- 履带板：跑道形闭合带 ----
        BuildBand(new Vector3(x, axleY, 0f), R, zMin, zMax, width);
    }

    /// <summary>跑道形履带：两段直线（上下）+ 两端半圆，沿路径铺 UV。</summary>
    void BuildBand(Vector3 center, float R, float zMin, float zMax, float width)
    {
        const int segStraight = 8;      // 每段直线的采样数
        const int segArc = 10;          // 每个半圆的采样数
        float hw = width * 0.5f;
        var path = new List<Vector2>(); // (z, y) 相对 axleY

        // 下沿：从 zMin 到 zMax
        for (int i = 0; i <= segStraight; i++)
            path.Add(new Vector2(Mathf.Lerp(zMin, zMax, i / (float)segStraight), -R));
        // 前弧：从下到上（z = zMax 处，绕 +90°）
        for (int i = 1; i < segArc; i++)
        {
            float a = -Mathf.PI * 0.5f + Mathf.PI * i / segArc;
            path.Add(new Vector2(zMax + Mathf.Cos(a) * R, Mathf.Sin(a) * R));
        }
        // 上沿：从 zMax 回 zMin
        for (int i = 0; i <= segStraight; i++)
            path.Add(new Vector2(Mathf.Lerp(zMax, zMin, i / (float)segStraight), R));
        // 后弧：从上到下
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
            float u = arc / Mathf.Max(0.02f, treadMeters);
            verts[i * 2 + 0] = new Vector3(center.x - hw, center.y + path[i].y, path[i].x);
            verts[i * 2 + 1] = new Vector3(center.x + hw, center.y + path[i].y, path[i].x);
            uvs[i * 2 + 0] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = i * 2, b = i * 2 + 1, c = j * 2, d = j * 2 + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        var mesh = new Mesh { name = "ProceduralTrack" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("TrackBand");
        // 注意：履带物体的 transform 带 100 倍缩放（FBX 根节点），
        // 生成的网格是按米做的，必须挂到它的父节点（车体）上，否则会被放大 100 倍。
        go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TreadMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    Transform MakeCylinder(Vector3 pos, float radius, float width, Color color, string name)
    {
        var mesh = CylinderMesh(radius, width, 14);
        var go = new GameObject(name);
        // 同上：挂到车体上（米制），不要挂在带 100 倍缩放的履带物体下面
        go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 圆柱轴向 Y → X（车宽方向）
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

        // 侧面
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
        // 两端圆盘
        int capStart = verts.Count;
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

    Material _treadMat, _simpleMat;

    Material TreadMaterial()
    {
        if (_treadMat != null) return _treadMat;
        BuildTexture();
        _treadMat = new Material(Shader.Find("Standard")) { name = "TrackTreadMat" };
        _treadMat.color = Color.white;
        _treadMat.mainTexture = _tex;
        _treadMat.SetFloat("_Glossiness", 0.05f);
        _treadMat.SetFloat("_Metallic", 0.1f);
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
        if (_tex != null) return;
        const int W = 128, H = 16;
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        _tex.name = "TrackTread";
        _tex.wrapMode = TextureWrapMode.Repeat;
        _tex.filterMode = FilterMode.Bilinear;

        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)W;
                Color c;
                if (t < 0.14f) c = treadColor;
                else
                {
                    float u = (t - 0.14f) / 0.86f;
                    float face = Mathf.Sin(u * Mathf.PI);
                    c = Color.Lerp(linkColor * 0.6f, linkColor, Mathf.Clamp01(face));
                    float tooth = Mathf.Max(
                        Mathf.SmoothStep(1f, 0f, Mathf.Abs(v - 0.20f) * 6f),
                        Mathf.SmoothStep(1f, 0f, Mathf.Abs(v - 0.80f) * 6f));
                    c = Color.Lerp(c, linkColor * 1.3f, tooth * 0.5f);
                    if (u < 0.10f || u > 0.90f) c *= 0.55f;
                }
                float edge = Mathf.Min(y, H - 1 - y) / (float)H;
                c *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(edge * 6f));
                px[y * W + x] = c;
            }
        }
        _tex.SetPixels(px);
        _tex.Apply(true);
    }

    // ------------------------------------------------------------------
    //  旧方案：滚模型自带履带的 UV（不生成轮子）
    // ------------------------------------------------------------------
    void BuildTreadMaterialOnly()
    {
        BuildTexture();
        var mat = TreadMaterial();
        var list = new List<Material>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterials = new[] { mat, SimpleMaterial(new Color(0.3f, 0.3f, 0.3f)) };
            list.Add(mat);
        }
        _treadMats = list.ToArray();
    }

    void Update()
    {
        float speed = vehicle != null ? vehicle.speed : 0f;
        if (Mathf.Abs(speed) < 0.02f) return;

        // 履带贴图滚动
        if (_treadMats != null)
        {
            float du = speed * Time.deltaTime / Mathf.Max(0.02f, treadMeters);
            for (int i = 0; i < _treadMats.Length; i++)
            {
                var m = _treadMats[i];
                if (m == null) continue;
                var o = m.mainTextureOffset;
                o.x = Mathf.Repeat(o.x - du, 1f);
                m.mainTextureOffset = o;
            }
        }

        // 轮子转：角速度 = 车速 / 轮半径（弧度/秒）
        if (_wheelRadius > 0.01f)
        {
            float deg = speed / _wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (w == null) continue;
                // 轮子轴向 = 自身 Y（生成时已经绕 Z 转了 90°，Y 指向车宽方向）
                w.Rotate(0f, -deg, 0f, Space.Self);
            }
        }
    }
}
