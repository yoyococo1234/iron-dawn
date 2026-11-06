using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 资源点的世界表现：**平贴地面/水面**的圆环 + 中心光柱，颜色随归属变化（蓝=我方 / 红=敌方 / 灰=中立）。
///
/// 为什么不用 LineRenderer：LineRenderer 是一条始终面向相机的缎带，放在地面上从低视角看就是
/// 一条悬空的线（之前就是 +2.5 m 浮在空中，坦克/船看特别别扭）。这里改成**逐点贴合地形的网格圆环**，
/// 相当于贴在地上的一圈涂装，从任何角度看都是"地面上画的圈"。
///
/// 光柱用带顶点渐变的圆柱网格（底部浓、顶部淡），比整根不透明的柱子好看。
/// 由 MatchManager.Start 调用 BuildAll 自动生成，不需要手动摆。
/// </summary>
public class ZoneMarker : MonoBehaviour
{
    public ZoneState state;

    MeshRenderer _ringRenderer;
    Mesh _ringMesh;
    Material _ringMat;

    MeshRenderer _beamRenderer;
    Mesh _beamMesh;
    Material _beamMat;

    const int RingSegments = 128;
    const float RingLift = 0.35f;      // 离地高度（够高避免 Z-fighting，又不显浮空）
    const float BeamHeight = 300f;     // 光柱高度（用户要求再高一点）

    public static void BuildAll(List<ZoneState> zones)
    {
        if (zones == null) return;
        var root = GameObject.Find("ZoneMarkers");
        if (root == null) root = new GameObject("ZoneMarkers");

        foreach (var z in zones)
        {
            if (z == null || z.def == null) continue;
            var go = new GameObject($"Zone_{z.def.id}");
            go.transform.SetParent(root.transform, false);
            var m = go.AddComponent<ZoneMarker>();
            m.state = z;
            m.Build();
        }
    }

    /// <summary>地面高度：陆地取地形，水面（低于海平面）取海面。</summary>
    static float GroundY(float x, float z)
    {
        float h = BattlefieldTerrain.SampleHeight(x, z);
        return Mathf.Max(h, BattlefieldTerrain.WaterLevel);
    }

    void Build()
    {
        var b = state.def.bounds;
        Vector3 center = new Vector3(b.center.x, 0f, b.center.y);
        float radius = Mathf.Max(12f, Mathf.Min(b.width, b.height) * 0.42f);
        float band = Mathf.Max(3f, radius * 0.06f);          // 环的宽度

        BuildRing(center, radius, band);
        BuildBeam(center, radius);
        ApplyColor();
    }

    // ---------------- 贴地圆环 ----------------
    void BuildRing(Vector3 center, float radius, float band)
    {
        var verts = new List<Vector3>(RingSegments * 2 + 2);
        var cols = new List<Color>(RingSegments * 2 + 2);
        var tris = new List<int>(RingSegments * 6);

        float rOut = radius + band * 0.5f;
        float rIn = radius - band * 0.5f;

        for (int i = 0; i <= RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cz = Mathf.Sin(a);

            float xo = center.x + cx * rOut, zo = center.z + cz * rOut;
            float xi = center.x + cx * rIn, zi = center.z + cz * rIn;

            verts.Add(new Vector3(xi, GroundY(xi, zi) + RingLift, zi));
            verts.Add(new Vector3(xo, GroundY(xo, zo) + RingLift, zo));
            cols.Add(Color.white);
            cols.Add(Color.white);
        }

        for (int i = 0; i < RingSegments; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        _ringMesh = new Mesh { name = "ZoneRing" };
        _ringMesh.SetVertices(verts);
        _ringMesh.SetColors(cols);
        _ringMesh.SetTriangles(tris, 0);
        _ringMesh.RecalculateBounds();

        var go = new GameObject("Ring");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _ringMesh;

        _ringMat = new Material(Shader.Find("Sprites/Default"));
        _ringMat.color = Color.white;
        _ringRenderer = go.AddComponent<MeshRenderer>();
        _ringRenderer.sharedMaterial = _ringMat;
        _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ringRenderer.receiveShadows = false;
        _ringRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _ringRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // ---------------- 中心光柱（底部浓、顶部淡）----------------
    void BuildBeam(Vector3 center, float radius)
    {
        const int Sides = 20;
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var tris = new List<int>();

        float groundY = GroundY(center.x, center.z);
        float r = Mathf.Max(0.8f, radius * 0.03f);

        for (int i = 0; i <= Sides; i++)
        {
            float a = i / (float)Sides * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
            verts.Add(new Vector3(center.x + cx * r, groundY, center.z + cz * r));
            verts.Add(new Vector3(center.x + cx * r, groundY + BeamHeight, center.z + cz * r));
            // 底部亮、顶部完全淡出
            cols.Add(new Color(1f, 1f, 1f, 1f));
            cols.Add(new Color(1f, 1f, 1f, 0f));
        }

        for (int i = 0; i < Sides; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        _beamMesh = new Mesh { name = "ZoneBeam" };
        _beamMesh.SetVertices(verts);
        _beamMesh.SetColors(cols);
        _beamMesh.SetTriangles(tris, 0);
        _beamMesh.RecalculateBounds();

        var go = new GameObject("Beam");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _beamMesh;

        _beamMat = new Material(Shader.Find("Sprites/Default"));
        _beamMat.color = Color.white;
        _beamRenderer = go.AddComponent<MeshRenderer>();
        _beamRenderer.sharedMaterial = _beamMat;
        _beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beamRenderer.receiveShadows = false;
        _beamRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _beamRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    void Update() => ApplyColor();

    void ApplyColor()
    {
        var c = state.OwnerColor;

        if (_ringMat != null)
            _ringMat.color = new Color(c.r, c.g, c.b, 0.9f);

        if (_beamMat != null)
        {
            // 占领中会闪，方便远处看出"正在被抢"
            float pulse = state.progressTeam >= 0 && state.owner != state.progressTeam
                ? 0.30f + 0.18f * Mathf.Abs(Mathf.Sin(Time.time * 3f))
                : 0.24f;
            _beamMat.color = new Color(c.r, c.g, c.b, pulse);
        }
    }
}
