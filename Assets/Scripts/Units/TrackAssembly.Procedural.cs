using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化履带（2026-11-01 深夜）。
///
/// 为什么换方案：Lux3D 给轻坦生成的那套履带网格，是「一圈带子 + 一大团碎面」焊在一起的东西。
/// 实测顶点到中心线的偏移是**连续分布**的（p50=0.11 / p75=0.22 / p90=0.34 / p97=0.50 / p99=0.56 m），
/// 没有哪个阈值能把碎面和履带板分开 —— 任何"按距离筛"的启发式都会误伤。
///
/// 前一个人在这块加了 5 层补丁（TrimToXBand / p97 自适应带宽 / 按轮轴排除面片 / 压平碎片 / 端部轮重建），
/// 补丁之间互相打架，最后的结果是：
///   · 端部轮重建按「轮子列表里 z 最外 8%」判，但轻坦真正的主动轮在 static_clean 里、不在列表里，
///     于是把最前面两个**负重轮**换成了大圆盘；
///   · 按半径截断轮子尖刺，把 0.43 m 的负重轮切成了 0.35 m；
///   · 履带环高 1.41 m 而负重轮只有 0.88 m，顶上那段履带悬空 0.5 m，没有托带轮撑着。
///
/// 所以这里不再"修"烂网格，直接**按尺寸把履带生成出来**：
///   环带（stadium 环，带厚度、带一节一节的履带板）+ 主动轮（带齿）+ 诱导轮 + 负重轮 + 托带轮。
/// 全部可参数化（VehicleDef 上），全部干净、可控、不再依赖任何阈值。
/// </summary>
public partial class TrackAssembly
{
    [Header("程序化履带（VehicleDef.proceduralTracks = true 时生效）")]
    [Tooltip("生成完把原 Lux3D 履带的渲染关掉（只关渲染，不动资产）")]
    public bool hideSourceTracks = true;

    /// <summary>生成本车的履带总成。跑完 _belts / _wheels 就是完整状态，Update 照常驱动。</summary>
    void BuildProcedural()
    {
        var def = vehicle != null ? vehicle.def : null;

        // 车体包围盒（VehicleModelBinder 挂好的 "Model"）
        Bounds hb = new Bounds(Vector3.zero, Vector3.zero);
        bool hasHull = false;
        var root = vehicle != null ? vehicle.transform : transform.parent;
        if (root != null)
        {
            var model = root.Find("Model");
            if (model != null)
            {
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled) continue;
                    if (!hasHull) { hb = r.bounds; hasHull = true; }
                    else hb.Encapsulate(r.bounds);
                }
            }
        }

        // ---- 规格：VehicleDef 上可调，没填就按车体推 ----
        float bandW = Pick(def != null ? def.trackBandWidth : 0f, 0.62f);
        float spanX = Pick(def != null ? def.trackHalfSpan : 0f, hasHull ? hb.size.x * 0.35f : 1.40f);
        float loopLen = Pick(def != null ? def.trackLoopLength : 0f, hasHull ? hb.size.z * 0.93f : 6.60f);
        float loopH = Pick(def != null ? def.trackLoopHeight : 0f, 1.34f);
        float wheelR = Pick(def != null ? def.trackRoadWheelR : 0f, 0.40f);
        int wheelN = def != null && def.trackRoadWheelCount > 0 ? def.trackRoadWheelCount : 5;

        // ---- 把履带总成归零：程序化生成不继承 AlignTracks 的缩放/位移 ----
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (hideSourceTracks)
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        var baseMat = MakeSteelMat();
        var wheelMat = MakeSteelMat(0.40f, 0.30f, 0.22f);   // 轮体：中灰（太黑会显得"轮子上一块黑一块白"）
        // 辐条/螺栓用**无光照**材质：只靠颜色区分，不受法线/阴影影响（之前用 Standard 会在轮面糊出黑三角）
        var markMat = MakeFlatMat(new Color(0.94f, 0.94f, 0.95f, 1f));   // 标记：纯白
        var starMat = MakeFlatMat(new Color(0.72f, 0.10f, 0.10f, 1f));   // 轮心五角星：红

        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1f : 1f;
            BuildOneSide(side, side * spanX, bandW, loopLen, loopH, wheelR, wheelN, baseMat, wheelMat, markMat, starMat);
        }

        _halfTrack = spanX;
        if (verbose)
            Debug.Log($"[TrackAssembly] 程序化履带：span={spanX:F2} band={bandW:F2} loop={loopLen:F2}x{loopH:F2} " +
                      $"负重轮={wheelN} 侧数={_belts.Count}");
    }

    static float Pick(float v, float fallback) => v > 0.01f ? v : fallback;

    void BuildOneSide(float side, float x, float bandW, float loopLen, float loopH,
                      float wheelR, int wheelN, Material baseMat, Material wheelMat, Material markMat, Material starMat)
    {
        const float t = 0.150f;                 // 履带板厚（侧影就靠它，太薄会像一根铁丝）
        const float ridge = 0.028f;             // 履带板凸起（做出"一节一节"的轮廓）
        float R = loopH * 0.5f - t * 0.5f;      // 中心线端部半径
        float hs = Mathf.Max(0.2f, loopLen * 0.5f - loopH * 0.5f);   // 直段半长
        float zc = t * 0.5f + R;                // 中心线高度（履带底面 = 0）
        float P = 4f * hs + 2f * Mathf.PI * R;  // 环周长

        int linkCount = Mathf.Max(8, Mathf.RoundToInt(P / 0.18f));
        int ringsPerLink = 3;
        int RING = linkCount * ringsPerLink;

        // ================= 1) 履带环 =================
        var bandGo = new GameObject(side < 0f ? "beltProc_L" : "beltProc_R");
        bandGo.transform.SetParent(transform, false);
        // 网格局部约定和 Lux3D 那套一致：x=宽, y=车长, z=高（节点转 270°，和原 belt 节点相同）
        bandGo.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        bandGo.transform.localPosition = new Vector3(x, 0f, 0f);

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var arc = new List<float>();

        float halfW = bandW * 0.5f;
        // 4 条面：0 外 / 1 右 / 2 内 / 3 左。每条面独立顶点，棱角才硬。
        for (int face = 0; face < 4; face++)
        {
            int start = verts.Count;
            for (int i = 0; i <= RING; i++)
            {
                int ring = i % RING;
                bool raised = (ring % ringsPerLink) != (ringsPerLink - 1);
                float offOut = t * 0.5f + (raised ? ridge : 0f);
                float sArc = (float)i / RING * P;

                Vector2 c, tan;
                Stadium(sArc, hs, R, zc, out c, out tan);
                Vector2 nOut = new Vector2(tan.y, -tan.x);

                float oA, oB, xA, xB;
                Vector2 nUse = nOut;
                switch (face)
                {
                    case 0:  // 外表面
                        oA = offOut; oB = offOut; xA = -halfW; xB = halfW; break;
                    case 1:  // 右侧面（x = +halfW）
                        oA = offOut; oB = -t * 0.5f; xA = halfW; xB = halfW; break;
                    case 2:  // 内表面
                        nUse = -nOut; oA = t * 0.5f; oB = t * 0.5f; xA = halfW; xB = -halfW; break;
                    default: // 左侧面（x = -halfW）
                        oA = -t * 0.5f; oB = offOut; xA = -halfW; xB = -halfW; break;
                }

                verts.Add(new Vector3(xA, c.x + nUse.x * oA, c.y + nUse.y * oA));
                verts.Add(new Vector3(xB, c.x + nUse.x * oB, c.y + nUse.y * oB));
                arc.Add(sArc); arc.Add(sArc);
            }
            bool flip = (face == 1 || face == 3);
            for (int i = 0; i < RING; i++)
            {
                int a = start + i * 2, b = a + 1, c2 = a + 2, d = a + 3;
                if (!flip)
                {
                    tris.Add(a); tris.Add(c2); tris.Add(b);
                    tris.Add(b); tris.Add(c2); tris.Add(d);
                }
                else
                {
                    tris.Add(a); tris.Add(b); tris.Add(c2);
                    tris.Add(b); tris.Add(d); tris.Add(c2);
                }
            }
        }

        var mesh = new Mesh { name = bandGo.name };
        mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mf = bandGo.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = bandGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = baseMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        var belt = new Belt
        {
            mesh = mesh,
            tf = bandGo.transform,
            basePos = mesh.vertices,
            baseNrm = mesh.normals,
            curPos = new Vector3[verts.Count],
            curNrm = new Vector3[verts.Count],
            s = arc.ToArray(),
            anim = new bool[verts.Count],
            length = P,
            unit = 1f,
            N = 256,
            side = side,
            th0 = new float[verts.Count],
            offY = new float[verts.Count],
            offZ = new float[verts.Count],
        };
        for (int i = 0; i < belt.anim.Length; i++) belt.anim[i] = true;
        System.Array.Copy(belt.basePos, belt.curPos, belt.basePos.Length);
        System.Array.Copy(belt.baseNrm, belt.curNrm, belt.baseNrm.Length);

        // 中心线采样（和 Lux3D 那套同格式：cyz=(y,z)，ang=切线角展开、单调）
        var cyz = new Vector2[256];
        var cdir = new Vector2[256];
        var ang = new float[256];
        for (int j = 0; j < 256; j++)
        {
            Vector2 c, tan;
            Stadium((float)j / 256f * P, hs, R, zc, out c, out tan);
            cyz[j] = c;
            cdir[j] = tan;
        }
        ang[0] = 0f;
        for (int j = 1; j < 256; j++)
        {
            Vector2 d = cyz[j] - cyz[j - 1];
            ang[j] = ang[j - 1] + d.magnitude / R * 0.0f;   // 占位，下面按累计转角重算
        }
        // 按累计切线角重建（单调递增，绕完正好 2π）
        ang[0] = 0f;
        for (int j = 1; j < 256; j++)
        {
            float a0 = Mathf.Atan2(cdir[j - 1].y, cdir[j - 1].x);
            float a1 = Mathf.Atan2(cdir[j].y, cdir[j].x);
            float da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            ang[j] = ang[j - 1] + Mathf.Abs(da);
        }
        // 归一：最后一点 + 补到起点，凑成正好 2π
        float total = ang[255] + Mathf.Abs(Mathf.DeltaAngle(
            Mathf.Atan2(cdir[255].y, cdir[255].x) * Mathf.Rad2Deg,
            Mathf.Atan2(cdir[0].y, cdir[0].x) * Mathf.Rad2Deg) * Mathf.Deg2Rad);
        if (total > 1e-4f)
            for (int j = 0; j < 256; j++) ang[j] = ang[j] / total * 2f * Mathf.PI;

        belt.cyz = cyz;
        belt.cdir = cdir;
        belt.ang = ang;
        for (int i = 0; i < belt.s.Length; i++)
        {
            Vector2 c, tan;
            Stadium(belt.s[i], hs, R, zc, out c, out tan);
            belt.th0[i] = Mathf.Atan2(tan.y, tan.x);
            belt.offY[i] = belt.basePos[i].y - c.x;
            belt.offZ[i] = belt.basePos[i].z - c.y;
        }

        if (useGpuShader) SetupGpu(belt, mr);
        _belts.Add(belt);

        // ================= 2) 轮子 =================
        float wheelY = t + wheelR;                          // 压在履带内表面上
        float axleW = Mathf.Max(0.20f, bandW * 0.62f);

        float spanZ = Mathf.Max(0.5f, hs - R - wheelR * 0.1f);   // 别顶到端部轮
        for (int i = 0; i < wheelN; i++)
        {
            float f = wheelN == 1 ? 0.5f : (float)i / (wheelN - 1);
            float z = Mathf.Lerp(-spanZ, spanZ, f);
            AddWheel($"wheelProc_{sideTag(side)}_{i}", side, x, wheelY, z,
                     wheelR, axleW, 24, wheelR * 0.42f, wheelMat, 0, markMat, starMat);
        }

        float sprocketR = R * 0.96f;
        AddWheel($"sprocketProc_{sideTag(side)}", side, x, zc, hs + 0.02f,
                 sprocketR, axleW * 1.12f, 20, sprocketR * 0.34f, wheelMat, 12, markMat, starMat);
        AddWheel($"idlerProc_{sideTag(side)}", side, x, zc, -(hs + 0.02f),
                 sprocketR * 0.94f, axleW * 1.02f, 20, sprocketR * 0.32f, wheelMat, 0, markMat, starMat);

        // 托带轮：撑着上面那段履带（不然顶上悬空一大截）
        float rollerR = Mathf.Clamp((zc + R - (wheelY + wheelR)) * 0.55f, 0.07f, 0.13f);
        float rollerY = zc + R - rollerR - t * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            float z = Mathf.Lerp(-hs * 0.55f, hs * 0.55f, i);
            AddWheel($"rollerProc_{sideTag(side)}_{i}", side, x, rollerY, z,
                     rollerR, axleW * 0.55f, 16, rollerR * 0.42f, wheelMat, 0);
        }
    }

    static string sideTag(float side) => side < 0f ? "L" : "R";

    /// <summary>
    /// 环中心线：stadium（两段直 + 两段半圆）。坐标是**网格局部** (y=车长, z=高)。
    /// 绕向：底面从 -hs 走到 +hs（+y 方向）→ 前端半圆自下而上 → 顶面往回 → 后端半圆自上而下。
    /// </summary>
    static void Stadium(float s, float hs, float R, float zc, out Vector2 pos, out Vector2 tan)
    {
        float L1 = 2f * hs;
        float L2 = Mathf.PI * R;
        s = Mathf.Repeat(s, 4f * hs + 2f * Mathf.PI * R);

        if (s < L1)                                    // 底面：-hs → +hs
        {
            pos = new Vector2(-hs + s, zc - R);
            tan = new Vector2(1f, 0f);
            return;
        }
        s -= L1;
        if (s < L2)                                    // 前端半圆：φ 0→π
        {
            float phi = s / R;
            pos = new Vector2(hs + R * Mathf.Sin(phi), zc - R * Mathf.Cos(phi));
            tan = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
            return;
        }
        s -= L2;
        if (s < L1)                                    // 顶面：+hs → -hs
        {
            pos = new Vector2(hs - s, zc + R);
            tan = new Vector2(-1f, 0f);
            return;
        }
        s -= L1;                                       // 后端半圆：φ 0→π
        {
            float phi = s / R;
            pos = new Vector2(-hs - R * Mathf.Sin(phi), zc + R * Mathf.Cos(phi));
            tan = new Vector2(-Mathf.Cos(phi), -Mathf.Sin(phi));
        }
    }

    void AddWheel(string name, float side, float x, float y, float z,
                  float radius, float width, int sides, float hubR, Material mat, int teeth,
                  Material markMat = null, Material starMat = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localRotation = Quaternion.identity;

        var marks = new List<int>();
        var star = new List<int>();
        var m = MakeWheelMesh(radius, width, sides, hubR, teeth, marks, star);
        m.name = name;
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>();
        // 槽位：0 = 轮体，1 = 辐条/螺栓（白），2 = 轮心五角星（红）
        if (star.Count > 0 && starMat != null) mr.sharedMaterials = new[] { mat, markMat != null ? markMat : mat, starMat };
        else if (marks.Count > 0 && markMat != null) mr.sharedMaterials = new[] { mat, markMat };
        else mr.sharedMaterial = mat;
        // 轮子不投影：辐条/螺栓只有几毫米凸起，自己给自己投影会在轮面糊上一圈黑三角
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _wheels.Add(new Wheel
        {
            t = go.transform,
            radius = radius,
            side = side,
            localZ = z,
            localCenter = new Vector3(x, y, z),
            medianR = 0f,
        });
    }

    /// <summary>轮子网格：轴沿 X。外圆筒 + 两侧盖 + 轮毂（+ 可选齿）。</summary>
    static Mesh MakeWheelMesh(float radius, float width, int sides, float hubR, int teeth,
                              List<int> trMarks, List<int> trStar)
    {
        var v = new List<Vector3>();
        var tr = new List<int>();
        float hx = width * 0.5f;

        // 外圆筒
        for (int i = 0; i < sides; i++)
        {
            float a0 = (float)i / sides * Mathf.PI * 2f;
            float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
            int b = v.Count;
            v.Add(new Vector3(-hx, Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius));
            v.Add(new Vector3(-hx, Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius));
            v.Add(new Vector3(hx, Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius));
            v.Add(new Vector3(hx, Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius));
            tr.Add(b); tr.Add(b + 1); tr.Add(b + 2);
            tr.Add(b); tr.Add(b + 2); tr.Add(b + 3);
        }

        // 两侧盖
        for (int sgn = 0; sgn < 2; sgn++)
        {
            float xx = sgn == 0 ? -hx : hx;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (float)i / sides * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
                int b = v.Count;
                v.Add(new Vector3(xx, 0f, 0f));
                v.Add(new Vector3(xx, Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius));
                v.Add(new Vector3(xx, Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius));
                if (sgn == 0) { tr.Add(b); tr.Add(b + 2); tr.Add(b + 1); }
                else { tr.Add(b); tr.Add(b + 1); tr.Add(b + 2); }
            }
        }

        // 轮缘（两侧各凸出一圈，侧视能看见一个明显的圆盘台阶）—— 没有它轮子就是一块黑板
        float rimW = width * 0.09f;
        for (int sgn = 0; sgn < 2; sgn++)
        {
            float xIn = sgn == 0 ? -hx : hx;
            float xOut = sgn == 0 ? -hx - rimW : hx + rimW;
            float x0 = Mathf.Min(xIn, xOut), x1 = Mathf.Max(xIn, xOut);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (float)i / sides * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
                int b = v.Count;
                v.Add(new Vector3(x0, Mathf.Cos(a0) * radius * 0.82f, Mathf.Sin(a0) * radius * 0.82f));
                v.Add(new Vector3(x0, Mathf.Cos(a1) * radius * 0.82f, Mathf.Sin(a1) * radius * 0.82f));
                v.Add(new Vector3(x1, Mathf.Cos(a1) * radius * 0.82f, Mathf.Sin(a1) * radius * 0.82f));
                v.Add(new Vector3(x1, Mathf.Cos(a0) * radius * 0.82f, Mathf.Sin(a0) * radius * 0.82f));
                tr.Add(b); tr.Add(b + 1); tr.Add(b + 2);
                tr.Add(b); tr.Add(b + 2); tr.Add(b + 3);
                // 外端盖（朝外的一面）
                int c = v.Count;
                v.Add(new Vector3(xOut, 0f, 0f));
                v.Add(new Vector3(xOut, Mathf.Cos(a0) * radius * 0.82f, Mathf.Sin(a0) * radius * 0.82f));
                v.Add(new Vector3(xOut, Mathf.Cos(a1) * radius * 0.82f, Mathf.Sin(a1) * radius * 0.82f));
                if (sgn == 0) { tr.Add(c); tr.Add(c + 2); tr.Add(c + 1); }
                else { tr.Add(c); tr.Add(c + 1); tr.Add(c + 2); }
            }
        }

        // 轮毂（要凸到轮缘外面，不然被轮缘盖住、侧视看不见）
        float hw = hx + rimW + width * 0.05f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = (float)i / sides * Mathf.PI * 2f;
            float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
            int b = v.Count;
            v.Add(new Vector3(-hw, Mathf.Cos(a0) * hubR, Mathf.Sin(a0) * hubR));
            v.Add(new Vector3(-hw, Mathf.Cos(a1) * hubR, Mathf.Sin(a1) * hubR));
            v.Add(new Vector3(hw, Mathf.Cos(a1) * hubR, Mathf.Sin(a1) * hubR));
            v.Add(new Vector3(hw, Mathf.Cos(a0) * hubR, Mathf.Sin(a0) * hubR));
            tr.Add(b); tr.Add(b + 1); tr.Add(b + 2);
            tr.Add(b); tr.Add(b + 2); tr.Add(b + 3);
        }
        for (int sgn = 0; sgn < 2; sgn++)
        {
            float xx = sgn == 0 ? -hw : hw;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (float)i / sides * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
                int b = v.Count;
                v.Add(new Vector3(xx, 0f, 0f));
                v.Add(new Vector3(xx, Mathf.Cos(a0) * hubR, Mathf.Sin(a0) * hubR));
                v.Add(new Vector3(xx, Mathf.Cos(a1) * hubR, Mathf.Sin(a1) * hubR));
                if (sgn == 0) { tr.Add(b); tr.Add(b + 2); tr.Add(b + 1); }
                else { tr.Add(b); tr.Add(b + 1); tr.Add(b + 2); }
            }
        }

        // 辐条 + 螺栓（2026-11-01 深夜加）：用户反馈"看不出轮子在转"。
        // 原因：轮面是一个均匀的圆盘，转起来没有参照物。加 5 根不对称辐条 + 5 颗螺栓，
        // 侧视就能明显看出轮面在动（5 是奇数，避免出现"转 72° 和没转一样"的对称感）。
        // 2026-11-02：辐条起点往外让开，给轮心的红色五角星腾位置。
        float starR = Mathf.Min(hubR * 1.10f, radius * 0.46f);
        {
            float xFace = hx + rimW;
            float spokeTh = 0.006f;   // 薄一点：太厚会自己给自己投影，轮面上出现一圈黑三角
            for (int sgn = 0; sgn < 2; sgn++)
            {
                // 起点比轮缘外表面再凸 2 mm，否则和轮缘端盖共面 → z-fighting，辐条只剩一条头发丝
                float xa = sgn == 0 ? -(xFace + spokeTh + 0.002f) : xFace + 0.002f;
                float xb = sgn == 0 ? -(xFace + 0.002f) : xFace + spokeTh + 0.002f;
                for (int k = 0; k < 5; k++)
                {
                    float ang = (float)k / 5f * Mathf.PI * 2f + 0.35f;
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    // 辐条：从五角星外沿到轮缘内沿的一条细条
                    AddRadialBar(v, trMarks, xa, xb, starR * 1.12f, radius * 0.78f, radius * 0.09f, ca, sa);
                    // 螺栓：落在轮缘端盖平面上（半径 0.66R），不要飘在空中
                    float rb = radius * 0.66f;
                    float xb0 = sgn == 0 ? xa : xb;
                    AddStud(v, trMarks, xb0, sgn, ca * rb, sa * rb, radius * 0.075f, 0.006f, 10);
                }
            }
        }

        // 轮心红色五角星（2026-11-02 用户要求）。
        // 贴在轮毂端盖平面上（比轮毂再凸 3 mm），半径略大于轮毂，正好把轮毂端面盖住。
        if (trStar != null && radius > 0.25f)   // 托带轮太小，不画星
        {
            float xStar = hw + 0.003f;
            for (int sgn = 0; sgn < 2; sgn++)
            {
                float x = sgn == 0 ? -xStar : xStar;
                var ring = new Vector3[10];
                for (int i = 0; i < 10; i++)
                {
                    float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;     // 从正上方那个尖开始
                    float r = (i % 2 == 0) ? starR : starR * 0.382f;   // 外尖 / 内凹
                    ring[i] = new Vector3(x, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                }
                var center = new Vector3(x, 0f, 0f);
                for (int i = 0; i < 10; i++)
                    AddTri2(v, trStar, center, ring[i], ring[(i + 1) % 10]);
            }
        }

        // 齿（主动轮）
        for (int k = 0; k < teeth; k++)
        {
            float a = (float)k / teeth * Mathf.PI * 2f;
            float half = Mathf.PI / teeth * 0.5f;
            float rIn = radius * 0.90f, rOut = radius * 1.10f;
            float w2 = width * 0.28f;
            int b = v.Count;
            for (int aa = 0; aa < 2; aa++)
                for (int bb = 0; bb < 2; bb++)
                    for (int cc = 0; cc < 2; cc++)
                    {
                        float ang = a + (aa == 0 ? -half : half);
                        float rr = bb == 0 ? rOut : rIn;
                        float xx = cc == 0 ? -w2 : w2;
                        v.Add(new Vector3(xx, Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr));
                    }
            // 角点索引：aa*4 + bb*2 + cc
            AddQuad(tr, b + 0, b + 4, b + 6, b + 2);   // 外弧面
            AddQuad(tr, b + 1, b + 3, b + 7, b + 5);   // 内弧面
            AddQuad(tr, b + 0, b + 1, b + 5, b + 4);   // 端面 A
            AddQuad(tr, b + 2, b + 6, b + 7, b + 3);   // 端面 B
            AddQuad(tr, b + 0, b + 2, b + 3, b + 1);   // 侧面 A
            AddQuad(tr, b + 4, b + 5, b + 7, b + 6);   // 侧面 B
        }

        var mesh = new Mesh();
        mesh.indexFormat = v.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        // 注意：SetTriangles(list, 1) 不会自己把 subMeshCount 撑到 2（实测 Unity 6 里直接静默丢弃），
        // 必须先手动把 subMeshCount 设成 2，否则第 1 槽永远是空的、辐条根本不渲染。
        bool hasMarks = trMarks != null && trMarks.Count > 0;
        bool hasStar = trStar != null && trStar.Count > 0;
        mesh.subMeshCount = hasStar ? 3 : (hasMarks ? 2 : 1);
        mesh.SetTriangles(tr, 0);
        if (hasMarks) mesh.SetTriangles(trMarks, 1);
        if (hasStar) mesh.SetTriangles(trStar, 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddQuad(List<int> tr, int a, int b, int c, int d)
    {
        tr.Add(a); tr.Add(b); tr.Add(c);
        tr.Add(a); tr.Add(c); tr.Add(d);
    }

    /// <summary>
    /// 双面四边形。**两个绕向各用一套顶点**：共用顶点的话 RecalculateNormals 会把两边的法线
    /// 平均掉（互相抵消成 0），结果是纯黑 —— 2026-11-01 实测踩到。
    /// </summary>
    static void AddQuad2(List<Vector3> v, List<int> tr, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        tr.Add(i); tr.Add(i + 1); tr.Add(i + 2);
        tr.Add(i); tr.Add(i + 2); tr.Add(i + 3);

        int j = v.Count;
        v.Add(a); v.Add(c); v.Add(b); v.Add(d);
        tr.Add(j); tr.Add(j + 1); tr.Add(j + 2);
        tr.Add(j + 1); tr.Add(j + 3); tr.Add(j + 2);
    }

    /// <summary>双面三角形（两个绕向各用一套顶点，避免法线被平均成 0 → 纯黑）。</summary>
    static void AddTri2(List<Vector3> v, List<int> tr, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        tr.Add(i); tr.Add(i + 1); tr.Add(i + 2);
        int j = v.Count;
        v.Add(a); v.Add(c); v.Add(b);
        tr.Add(j); tr.Add(j + 1); tr.Add(j + 2);
    }

    /// <summary>轮面上的一条辐条：沿半径 rA→rB、切向半宽 halfW、沿轴 xa→xb 的细长块。</summary>
    static void AddRadialBar(List<Vector3> v, List<int> tr,
                             float xa, float xb, float rA, float rB, float halfW, float ca, float sa)
    {
        var p = new Vector3[8];
        int n = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++)
                {
                    float r = i == 0 ? rA : rB;
                    float t = j == 0 ? -halfW : halfW;
                    float x = k == 0 ? xa : xb;
                    p[n++] = new Vector3(x, r * ca - t * sa, r * sa + t * ca);
                }
        // idx = i*4 + j*2 + k
        AddQuad2(v, tr, p[0], p[1], p[3], p[2]);   // 内端面 (i=0)
        AddQuad2(v, tr, p[4], p[6], p[7], p[5]);   // 外端面 (i=1)
        AddQuad2(v, tr, p[0], p[4], p[5], p[1]);   // 切向负侧 (j=0)
        AddQuad2(v, tr, p[2], p[3], p[7], p[6]);   // 切向正侧 (j=1)
        AddQuad2(v, tr, p[0], p[2], p[6], p[4]);   // x 负侧 (k=0)
        AddQuad2(v, tr, p[1], p[5], p[7], p[3]);   // x 正侧 (k=1)
    }

    /// <summary>轮面上的一颗螺栓（小圆柱，轴沿 X）。</summary>
    static void AddStud(List<Vector3> v, List<int> tr, float x, int sgn,
                        float cy, float cz, float r, float len, int sides)
    {
        float x0 = sgn == 0 ? x : x - len;
        float x1 = sgn == 0 ? x + len : x;
        float xo = sgn == 0 ? x0 : x1;
        for (int i = 0; i < sides; i++)
        {
            float a0 = (float)i / sides * Mathf.PI * 2f;
            float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
            Vector3 p0 = new Vector3(x0, cy + Mathf.Cos(a0) * r, cz + Mathf.Sin(a0) * r);
            Vector3 p1 = new Vector3(x0, cy + Mathf.Cos(a1) * r, cz + Mathf.Sin(a1) * r);
            Vector3 p2 = new Vector3(x1, cy + Mathf.Cos(a1) * r, cz + Mathf.Sin(a1) * r);
            Vector3 p3 = new Vector3(x1, cy + Mathf.Cos(a0) * r, cz + Mathf.Sin(a0) * r);
            AddQuad2(v, tr, p0, p1, p2, p3);

            // 外端盖（两个绕向各用一套顶点，共用会被法线平均成 0 → 纯黑）
            int c = v.Count;
            v.Add(new Vector3(xo, cy, cz));
            v.Add(new Vector3(xo, cy + Mathf.Cos(a0) * r, cz + Mathf.Sin(a0) * r));
            v.Add(new Vector3(xo, cy + Mathf.Cos(a1) * r, cz + Mathf.Sin(a1) * r));
            tr.Add(c); tr.Add(c + 1); tr.Add(c + 2);
            int c2 = v.Count;
            v.Add(new Vector3(xo, cy, cz));
            v.Add(new Vector3(xo, cy + Mathf.Cos(a1) * r, cz + Mathf.Sin(a1) * r));
            v.Add(new Vector3(xo, cy + Mathf.Cos(a0) * r, cz + Mathf.Sin(a0) * r));
            tr.Add(c2); tr.Add(c2 + 1); tr.Add(c2 + 2);
        }
    }

    /// <summary>履带用的深色钢材质（不依赖 Lux3D 那张图集，UV 对不上）。</summary>
    static Material MakeFlatMat(Color c)
    {
        var sh = Shader.Find("Unlit/Color");
        var m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.name = "WheelMark";
        if (m.HasProperty("_Color")) m.color = c;
        return m;
    }

    static Material MakeSteelMat(float grey = 0.20f, float metallic = 0.65f, float gloss = 0.30f)
    {
        var sh = Shader.Find("Standard");
        var m = new Material(sh != null ? sh : Shader.Find("Diffuse"));
        m.name = "TrackSteel";
        m.color = new Color(grey, grey * 1.02f, grey * 1.06f, 1f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
        return m;
    }
}
