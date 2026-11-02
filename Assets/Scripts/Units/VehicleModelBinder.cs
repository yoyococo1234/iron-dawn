using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 模型挂点绑定：把"精细模型"接到代码上。
/// 代码只认三个东西：整体、炮塔节点、炮口节点。
///
/// 两种模型形态都支持：
///   · Lux3D part_split 拆件（车体 + 独立炮塔）→ 炮塔真的会转
///   · 整体网格（没有炮塔部件）→ 补一个逻辑炮塔，出膛点固定在车头
/// </summary>
public static class VehicleModelBinder
{
    /// <summary>编辑器里按约定路径自动找模型。</summary>
    public static GameObject AutoFindModel(string vehicleId)
    {
#if UNITY_EDITOR
        string[] candidates =
        {
            $"Assets/Models/Vehicles/{vehicleId}.prefab",
            $"Assets/Models/Vehicles/{vehicleId}.fbx",
            $"Assets/Models/Vehicles/{vehicleId}.glb",
        };
        foreach (var p in candidates)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) return go;
        }
#endif
        return null;
    }

    /// <summary>按名字在子物体里找节点（找不到返回 null，不报错）。</summary>
    public static Transform FindBone(GameObject root, string boneName)
    {
        if (root == null || string.IsNullOrEmpty(boneName)) return null;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, boneName, System.StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    /// <summary>
    /// 挂模型 + 炮塔。找不到模型返回 false（调用方自己退回方块占位）。
    /// 运行时可调用（用 VehicleDef.modelPrefab，不碰 AssetDatabase）。
    /// </summary>
    public static bool Attach(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        turret = null;
        muzzle = null;
        if (root == null || def == null || def.modelPrefab == null) return false;

        var vp = root.GetComponent<VehiclePhysics>();

        // ---------- 车体 ----------
        var model = Object.Instantiate(def.modelPrefab, root.transform);
        model.name = "Model";

        // FBX 导入出来的根节点自带「导入缩放」和「上轴修正旋转」，
        // 直接覆盖 localScale/localRotation 会把模型缩成 1/100 并且歪掉。
        // 所以要在原值基础上叠加，而不是替换。
        var baseScale = model.transform.localScale;
        var baseRot = model.transform.localRotation;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, def.modelYawOffset, 0f) * baseRot;
        model.transform.localScale = baseScale * Mathf.Max(0.001f, def.modelScale);

        StripColliders(model);
        if (def.modelMaterial != null) ApplyMaterial(model, def.modelMaterial);
        var hullFilter = model.GetComponentInChildren<MeshFilter>();

        // ---------- 履带（单独一块，按车速滚 UV 让它"转"起来）----------
        Transform tracksRoot = null;
        if (def.modelTracksPrefab != null)
        {
            var tracks = Object.Instantiate(def.modelTracksPrefab, root.transform);
            tracks.name = "Tracks";
            tracks.transform.localPosition = Vector3.zero;
            StripColliders(tracks);
            if (def.modelMaterial != null || def.modelTracksMaterial != null)
                ApplyMaterial(tracks, def.modelTracksMaterial != null ? def.modelTracksMaterial : def.modelMaterial);

            // 履带对齐：Lux3D 每批生成的履带总成高度都不一样，直接塞进履带槽会出现
            // 「履带比车体还高、从侧裙上面冒出来」或者「悬空」。这里按 VehicleDef 给的目标高度
            // 等比缩放，再让底面贴到车体底部。
            AlignTracks(tracks.transform, model.transform, def);

            // 注意：**不要**整体关掉履带总成里的 "static" 网格。
            // 它虽然混着一些离群碎面，但主动轮/诱导轮也在里面 —— 一刀切关掉之后，
            // 履带前端会变成空壳（只剩履带板环，没有轮子）。
            // 碎面已经在 Blender 里清理过（work/clean_track_static.py → light_tracks3_clean.fbx）。

            // 拆件版履带总成（belt + wheel_*）用 TrackAssembly；老的整体履带用 TrackScroller
            bool isAssembly = false;
            foreach (var t in tracks.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().StartsWith("wheel")) { isAssembly = true; break; }
            if (isAssembly)
            {
                var asm = tracks.AddComponent<TrackAssembly>();
                asm.vehicle = vp;
            }
            else
            {
                var scroller = tracks.AddComponent<TrackScroller>();
                scroller.vehicle = vp;
            }
            tracksRoot = tracks.transform;
        }

        // ---------- 炮塔（Lux3D 拆件）----------
        if (def.modelTurretPrefab != null)
        {
            var tp = new GameObject("Turret");
            tp.transform.SetParent(root.transform, false);
            tp.transform.localPosition = def.modelTurretPivot;

            var tm = Object.Instantiate(def.modelTurretPrefab, tp.transform);
            tm.name = "TurretModel";
            // 炮塔网格的原点是 Blender 拆件时的切割面中心；转轴换成了炮塔本体中心，
            // 所以要把网格反向偏移回去：外观位置不变，只是转起来绕本体中心，不再甩。
            tm.transform.localPosition = def.modelTurretMeshOrigin - def.modelTurretPivot;
            StripColliders(tm);
            var turMat = def.modelTurretMaterial != null ? def.modelTurretMaterial : def.modelMaterial;
            if (turMat != null) ApplyMaterial(tm, turMat);

            // 炮管（从炮塔上切出来的，单独做俯仰）
            Transform barrelNode = null;
            if (def.modelBarrelPrefab != null)
            {
                var bp = new GameObject("Barrel");
                bp.transform.SetParent(tp.transform, false);
                bp.transform.localPosition = def.modelBarrelPivot;
                var bm = Object.Instantiate(def.modelBarrelPrefab, bp.transform);
                bm.name = "BarrelModel";
                bm.transform.localPosition = Vector3.zero;
                StripColliders(bm);
                if (turMat != null) ApplyMaterial(bm, turMat);
                barrelNode = bp.transform;
            }

            // 炮口：有炮管就挂在炮管前端（抬炮时跟着动），否则挂在炮塔前端
            var mz = new GameObject("Muzzle");
            if (barrelNode != null)
            {
                mz.transform.SetParent(barrelNode, false);
                var bl = LocalBounds(barrelNode);
                mz.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0.3f, bl.max.z * 0.98f));
            }
            else
            {
                mz.transform.SetParent(tp.transform, false);
                var lb = LocalBounds(tp.transform);
                mz.transform.localPosition = new Vector3(0f, 0f, lb.max.z * 0.95f);
            }
            if (vp != null) vp.barrelBone = barrelNode;

            // 炮塔井：车体被炮塔切开的破口，实测后拍平 + 补板（见 WellFixup）
            if (def.autoTurretWellPatch)
            {
                var fix = WellFixup.GetOrBuild(def, root, model, tp.transform);
                if (fix.hull != null && hullFilter != null) hullFilter.sharedMesh = fix.hull;
                if (fix.plate != null)
                {
                    var well = new GameObject("TurretWell");
                    well.transform.SetParent(root.transform, false);
                    well.AddComponent<MeshFilter>().sharedMesh = fix.plate;
                    var wr = well.AddComponent<MeshRenderer>();
                    // 用和车体同一套材质（UV 全取 0.5 → 采到迷彩贴图上的一个色，颜色和车体同族）
                    wr.sharedMaterial = def.modelMaterial != null ? def.modelMaterial : DarkMat();
                    wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    wr.receiveShadows = true;
                }
            }

            turret = tp.transform;
            muzzle = mz.transform;
            if (vp != null) vp.turretIsVisual = true;

            // 让模型底部正好落在车体原点（= 地面高度），否则整台车会悬空
            DropToGround(root, model.transform, tracksRoot, tp.transform, vp);

            // 炮塔是从车体/船体里「挖」出来的：底部没有面、甲板上留了个切口。
            // 不补的话炮塔一转就看见内部。这里补封底盘 + 炮座板，再挂上车顶机枪。
            AddTurretExtras(root.transform, tp.transform, def, vp);
            return true;
        }

        // ---------- 没有独立炮塔：补逻辑炮塔 ----------
        BuildLogicBones(root, out var lt, out var lm);
        turret = lt;
        muzzle = lm;
        DropToGround(root, model.transform, null, lt, vp);
        return true;
    }

    /// <summary>
    /// 把履带总成对进履带槽：等比缩放到目标高度，再让底面贴到车体底部。
    /// Lux3D 每批生成的履带高度都不一样（实测重坦那批 3.02 m，比整个车体 2.79 m 还高，
    /// 会从侧裙上面冒出来），所以在接车的时候统一校正一次。
    /// </summary>
    static void AlignTracks(Transform tracks, Transform hull, VehicleDef def)
    {
        if (tracks == null || def == null) return;
        var rs = tracks.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;

        Bounds tb = rs[0].bounds;
        foreach (var r in rs) tb.Encapsulate(r.bounds);
        if (tb.size.y < 0.01f) return;

        // ---- 按车体的「长 / 宽 / 高」分别对齐 ----
        // 以前是「按车体高度 × 88% 等比缩放」：轻坦履带本来 7.02 m 长、和车体 7.10 m 正好，
        // 被等比放大 27% 后变成 8.96 m，前后都戳出车体；宽度又只有 2.66 m（车体 4.01 m），
        // 看起来就是一条细铁丝绕在轮子上。
        //
        // 分开缩放才对：
        //   X（宽）—— 拉伸到车体宽，履带板变厚实，而且 X 方向拉伸**不会**把轮子压扁
        //   Z（长）—— 对齐车体长，但夹住范围，避免把负重轮拉成椭圆
        //   Y（高）—— 轻微调整
        if (hull != null)
        {
            var hr = hull.GetComponentsInChildren<Renderer>(true);
            if (hr.Length > 0)
            {
                var hb = hr[0].bounds;
                foreach (var r in hr) hb.Encapsulate(r.bounds);

                float kx = Mathf.Clamp(hb.size.x * 0.98f / Mathf.Max(0.01f, tb.size.x), 0.60f, 2.20f);
                float kz = Mathf.Clamp(hb.size.z * 0.98f / Mathf.Max(0.01f, tb.size.z), 0.85f, 1.20f);
                // Y 只允许**缩小**，绝不放大：轻坦履带本来 1.42 m，放大 15% 后顶到 1.63 m，
                // 直接高出侧裙上缘，看起来就是"履带把整个车体包起来了"。
                float ky = def.tracksTargetHeight > 0.01f
                    ? Mathf.Clamp(def.tracksTargetHeight / Mathf.Max(0.01f, tb.size.y), 0.75f, 1.00f)
                    : Mathf.Clamp(hb.size.y * 0.72f / Mathf.Max(0.01f, tb.size.y), 0.75f, 1.00f);

                if (Mathf.Abs(kx - 1f) > 0.02f || Mathf.Abs(ky - 1f) > 0.02f || Mathf.Abs(kz - 1f) > 0.02f)
                {
                    tracks.localScale = Vector3.Scale(tracks.localScale, new Vector3(kx, ky, kz));
                    tb = rs[0].bounds;
                    foreach (var r in rs) tb.Encapsulate(r.bounds);
                }
            }
        }

        // 底面贴到车体底部 + 前后居中（长度对齐后可能整体偏前/偏后）
        if (hull != null)
        {
            var hr = hull.GetComponentsInChildren<Renderer>(true);
            if (hr.Length > 0)
            {
                var hb = hr[0].bounds;
                foreach (var r in hr) hb.Encapsulate(r.bounds);

                float dy = hb.min.y - tb.min.y;
                float dz = hb.center.z - tb.center.z;
                var shift = Vector3.zero;
                if (Mathf.Abs(dy) > 0.005f) shift.y = dy;
                if (Mathf.Abs(dz) > 0.02f) shift.z = dz;
                if (shift != Vector3.zero) tracks.position += shift;
            }
        }
    }

    /// <summary>
    /// 把挂好的模型往下挪，让**最低点正好落在车体原点**。
    /// VehiclePhysics 会把车体原点放在「地面 + groundClearance」，模型原点如果在包围盒中心，
    /// 车就会浮在半空（截图里那种悬空）。这里按实际渲染包围盒量出最低点，整体下移。
    /// </summary>
    static void DropToGround(GameObject root, Transform model, Transform tracks, Transform turret, VehiclePhysics vp)
    {
        bool any = false;
        float minY = float.MaxValue;
        var toRoot = root.transform.worldToLocalMatrix;

        var list = new List<Transform>(3);
        if (model != null) list.Add(model);
        if (tracks != null) list.Add(tracks);
        if (turret != null) list.Add(turret);

        foreach (var t in list)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = toRoot.MultiplyPoint3x4(c);
                    if (!any || p.y < minY) { minY = p.y; any = true; }
                }
            }
        }
        if (!any) return;

        float dy = -minY;
        if (Mathf.Abs(dy) > 0.001f)
            foreach (var t in list) t.localPosition += new Vector3(0f, dy, 0f);

        // 模型底部已经落在车体原点上了，那就把「离地间隙」压到接近 0，
        // 否则整台车会浮在 0.55 m 的空中（截图里那种悬空）。
        if (vp != null)
        {
            vp.modelSitsOnGround = true;
            vp.groundClearance = 0.03f;
        }
    }

    /// <summary>整体网格时，按包围盒补一个"逻辑炮塔 + 炮口"（出膛点固定在车头）。</summary>
    public static void BuildLogicBones(GameObject root, out Transform turret, out Transform muzzle)
    {
        var rs = root.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) { turret = root.transform; muzzle = root.transform; return; }

        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        Vector3 center = root.transform.InverseTransformPoint(b.center);
        float halfLen = Mathf.Max(b.size.x, b.size.z) * 0.5f;
        float height = b.size.y;

        var vp = root.GetComponent<VehiclePhysics>();
        if (vp != null)
        {
            vp.turretIsVisual = false;
            vp.hullHalfLength = halfLen;
        }

        var tp = new GameObject("Turret");
        tp.transform.SetParent(root.transform, false);
        tp.transform.localPosition = new Vector3(0f, Mathf.Max(0.5f, center.y * 0.85f), 0f);
        turret = tp.transform;

        var mz = new GameObject("Muzzle");
        mz.transform.SetParent(tp.transform, false);
        mz.transform.localPosition = new Vector3(0f, height * 0.10f, halfLen * 0.90f);
        muzzle = mz.transform;
    }

    /// <summary>清掉炮塔井修补的缓存（改了模型/转轴后调用）。</summary>
    public static void ClearWellPatchCache() => WellFixup.Clear();

    // ==================== 拆件炮塔的补充件 ====================

    /// <summary>
    /// 拆件炮塔的收尾工作：
    ///   1. 炮塔封底盘 —— 炮塔是从车体里挖出来的，底部本来贴在甲板上、没有面，一转就看见内部
    ///   2. 甲板炮座板 —— 盖住车体上被挖走的那个圆口，炮塔转到任何角度都不露洞
    ///   3. 车顶/甲板机枪 —— 独立俯仰，能抬到 85° 打飞机
    /// </summary>
    static void AddTurretExtras(Transform root, Transform turret, VehicleDef def, VehiclePhysics vp)
    {
        if (root == null || turret == null || def == null) return;

        var rs = turret.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        // 炮管也在炮塔下面（一起转），但量炮塔座圈尺寸时不能把它算进去 ——
        // 炮管往前伸 3 m，算出来的"半对角线"会变成 3 m 以上，座板直接盖到履带上。
        Bounds wb = default;
        bool any = false;
        foreach (var r in rs)
        {
            if (IsUnderBarrel(r.transform, turret)) continue;
            if (!any) { wb = r.bounds; any = true; }
            else wb.Encapsulate(r.bounds);
        }
        if (!any) return;

        Vector3 center = turret.InverseTransformPoint(wb.center);
        Vector3 size = turret.InverseTransformVector(wb.size);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        float halfDiag = Mathf.Sqrt(size.x * size.x + size.z * size.z) * 0.5f;
        float bottom = center.y - size.y * 0.5f;
        float top = center.y + size.y * 0.5f;

        var turMat = def.modelTurretMaterial != null ? def.modelTurretMaterial : def.modelMaterial;
        var hullMat = def.modelMaterial;

        if (def.autoTurretSkirt && halfDiag > 0.25f)
        {
            // 封底盘：贴着炮塔底面（用短边的一半，不是对角线）。
            // 用对角线的话，重坦那种又长又窄的炮塔底下会伸出一圈大裙边，像个盘子。
            float capR = Mathf.Min(size.x, size.z) * 0.55f;
            var cap = MakeDisc("TurretBaseCap", capR, 0.08f, 32, turMat);
            cap.transform.SetParent(turret, false);
            cap.transform.localPosition = new Vector3(center.x, bottom + 0.03f, center.z);

            // 炮塔井盖：炮塔是从车体里"挖"走的，车体上留下一个**方形坑**（坑壁就是原来炮塔的侧面）。
            // 圆盘只能盖住外接圆、盖不住四角，所以用和坑同尺寸的方形板把坑口封平。
            // UV 全取 (0.5,0.5)：采样到车体贴图上的同一个色，才不会出现一块"不同色的补丁"。
            //
            // 2026-11-02：板加厚到 0.55 m（从井口一直填到井底），**顶面贴实测的甲板高度**。
            // 为什么不再用"炮塔转轴高度"：转轴在甲板下面 20 cm 左右，板沉在井底，
            // 炮塔一转就看见一个深坑（用户："下面空出来了" / "前面漏风"）。
            // 甲板高度现在用**临时 MeshCollider + 从上往下射线**实测（车体网格已开 Read/Write）。
            float deckY = turret.localPosition.y;
            {
                var hullTf2 = root.Find("Model");
                var hmf = hullTf2 != null ? hullTf2.GetComponentInChildren<MeshFilter>() : null;
                if (hmf != null && hmf.sharedMesh != null && hmf.sharedMesh.isReadable)
                {
                    var colGo = hmf.gameObject;
                    var col = colGo.GetComponent<MeshCollider>();
                    bool added = col == null;
                    if (added) col = colGo.AddComponent<MeshCollider>();
                    col.sharedMesh = hmf.sharedMesh;

                    Vector3 wt = root.TransformPoint(turret.localPosition);
                    float best = float.MinValue;
                    for (int i = 0; i < 9; i++)
                    {
                        float ox = ((i % 3) - 1) * 0.45f, oz = ((i / 3) - 1) * 0.45f;
                        var from = new Vector3(wt.x + ox, wt.y + 8f, wt.z + oz);
                        var hits = Physics.RaycastAll(from, Vector3.down, 16f, ~0, QueryTriggerInteraction.Ignore);
                        foreach (var h in hits)
                        {
                            if (h.collider != col) continue;      // 只认车体网格，别被命中盒/炮塔挡住
                            if (h.point.y > best) best = h.point.y;
                        }
                    }
                    if (best > float.MinValue)
                    {
                        float ly = root.InverseTransformPoint(new Vector3(wt.x, best, wt.z)).y;
                        deckY = Mathf.Max(deckY, ly + 0.02f);
                    }
                    if (added) Object.Destroy(col);
                    else col.sharedMesh = null;
                }
            }
            // 板比炮塔足迹放大 25%：车体被切开时边缘会留下毛刺三角，板小一点就露在外面。
            // 顶面再抬高 5 cm 把这些毛刺压到板下面。
            const float plateH = 0.60f;
            var plate = MakePlate("TurretWellPlate", size.x * 1.25f, size.z * 1.25f, plateH, hullMat);
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(
                turret.localPosition.x, deckY + 0.05f - plateH * 0.5f, turret.localPosition.z);
        }

        BuildDeckGun(turret, def, vp, center, size, top);
    }

    /// <summary>一块方板（六面体），UV 全取贴图中心色 —— 用来补炮塔井，颜色和车体同族，不会像块补丁。</summary>
    static GameObject MakePlate(string name, float width, float depth, float height, Material mat)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = PlateMesh(width, height, depth);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat != null ? mat : DarkMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        return go;
    }

    static readonly Dictionary<string, Mesh> _plateCache = new Dictionary<string, Mesh>();

    static Mesh PlateMesh(float width, float height, float depth)
    {
        string key = $"{width:F2}_{height:F2}_{depth:F2}";
        if (_plateCache.TryGetValue(key, out var cached) && cached != null) return cached;

        float x = Mathf.Max(0.01f, width) * 0.5f;
        float y = Mathf.Max(0.01f, height) * 0.5f;
        float z = Mathf.Max(0.01f, depth) * 0.5f;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++) uvs.Add(new Vector2(0.5f, 0.5f));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        Vector3 p000 = new Vector3(-x, -y, -z), p100 = new Vector3(x, -y, -z);
        Vector3 p110 = new Vector3(x, y, -z), p010 = new Vector3(-x, y, -z);
        Vector3 p001 = new Vector3(-x, -y, z), p101 = new Vector3(x, -y, z);
        Vector3 p111 = new Vector3(x, y, z), p011 = new Vector3(-x, y, z);

        Quad(p010, p110, p111, p011);   // 顶
        Quad(p000, p001, p101, p100);   // 底
        Quad(p000, p010, p011, p001);   // 左
        Quad(p100, p101, p111, p110);   // 右
        Quad(p000, p100, p110, p010);   // 前
        Quad(p001, p011, p111, p101);   // 后

        var m = new Mesh { name = "Plate" };
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        _plateCache[key] = m;
        return m;
    }

    /// <summary>这个节点是不是挂在 Barrel 底下（炮管单独俯仰，不算炮塔主体尺寸）。</summary>
    static bool IsUnderBarrel(Transform t, Transform turret)
    {
        while (t != null && t != turret)
        {
            if (t.name == "Barrel") return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>车顶/甲板机枪：支架 + 枪身 + 枪管 + 弹箱，挂在炮塔顶上（跟着炮塔转，自己只做俯仰）。</summary>
    static void BuildDeckGun(Transform turret, VehicleDef def, VehiclePhysics vp,
                             Vector3 center, Vector3 size, float top)
    {
        if (vp == null) return;

        var mg = new GameObject("DeckGun");
        mg.transform.SetParent(turret, false);
        mg.transform.localPosition = def.modelDeckGunPivot != Vector3.zero
            ? def.modelDeckGunPivot
            : new Vector3(center.x, top - 0.04f, center.z - size.z * 0.20f);

        vp.mgBone = mg.transform;
        vp.mgMaxElevation = 85f;
        vp.mgMaxDepression = -12f;

        var dark = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.13f, 0.14f, 0.15f)
        };
        dark.SetFloat("_Glossiness", 0.45f);
        dark.SetFloat("_Metallic", 0.65f);

        AddPart(mg.transform, "MG_Post", PrimitiveType.Cylinder,
                new Vector3(0.09f, 0.19f, 0.09f), new Vector3(0f, 0.19f, 0f), Vector3.zero, dark);

        AddPart(mg.transform, "MG_Body", PrimitiveType.Cube,
                new Vector3(0.17f, 0.19f, 0.62f), new Vector3(0f, 0.46f, 0.06f), Vector3.zero, dark);

        AddPart(mg.transform, "MG_Barrel", PrimitiveType.Cylinder,
                new Vector3(0.05f, 0.46f, 0.05f), new Vector3(0f, 0.47f, 0.72f), new Vector3(90f, 0f, 0f), dark);

        AddPart(mg.transform, "MG_Ammo", PrimitiveType.Cube,
                new Vector3(0.13f, 0.2f, 0.26f), new Vector3(0.17f, 0.4f, 0.02f), Vector3.zero, dark);
    }

    static void AddPart(Transform parent, string name, PrimitiveType type,
                        Vector3 scale, Vector3 pos, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat != null ? mat : DarkMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    /// <summary>一个扁圆盘（圆柱）—— 炮塔封底盘 / 炮座板用。</summary>
    static GameObject MakeDisc(string name, float radius, float height, int segments, Material mat)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = DiscMesh(radius, height, segments);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat != null ? mat : DarkMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        return go;
    }

    static readonly Dictionary<string, Mesh> _discCache = new Dictionary<string, Mesh>();

    /// <summary>圆盘网格：上下两个圆面 + 一圈侧面。UV 全给 (0.5,0.5)，采样到贴图上同一个色，颜色和车体同族。</summary>
    static Mesh DiscMesh(float radius, float height, int segments)
    {
        string key = $"{radius:F3}_{height:F3}_{segments}";
        if (_discCache.TryGetValue(key, out var cached) && cached != null) return cached;

        int seg = Mathf.Clamp(segments, 8, 96);
        float h = Mathf.Max(0.01f, height) * 0.5f;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // 顶面中心 + 边缘
        verts.Add(new Vector3(0f, h, 0f)); uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            verts.Add(new Vector3(Mathf.Cos(a) * radius, h, Mathf.Sin(a) * radius));
            uvs.Add(new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % seg;
            tris.Add(0); tris.Add(b); tris.Add(a);
        }

        // 底面中心 + 边缘
        int bottomCenter = verts.Count;
        verts.Add(new Vector3(0f, -h, 0f)); uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            verts.Add(new Vector3(Mathf.Cos(a) * radius, -h, Mathf.Sin(a) * radius));
            uvs.Add(new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = bottomCenter + 1 + i, b = bottomCenter + 1 + (i + 1) % seg;
            tris.Add(bottomCenter); tris.Add(a); tris.Add(b);
        }

        // 侧面
        int sideStart = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            float x = Mathf.Cos(a) * radius, z = Mathf.Sin(a) * radius;
            verts.Add(new Vector3(x, h, z)); uvs.Add(new Vector2(0.5f, 0.5f));
            verts.Add(new Vector3(x, -h, z)); uvs.Add(new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;
            int t0 = sideStart + i * 2, b0 = sideStart + i * 2 + 1;
            int t1 = sideStart + n * 2, b1 = sideStart + n * 2 + 1;
            tris.Add(t0); tris.Add(b0); tris.Add(t1);
            tris.Add(t1); tris.Add(b0); tris.Add(b1);
        }

        var m = new Mesh { name = $"Disc_{seg}" };
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        _discCache[key] = m;
        return m;
    }

    // ---------------- 工具 ----------------

    /// <summary>某个节点下的所有网格，在它自己局部坐标下的包围盒。</summary>
    static Bounds LocalBounds(Transform root)
    {
        bool has = false;
        var local = new Bounds(Vector3.zero, Vector3.zero);
        var toRoot = root.worldToLocalMatrix;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var e = mb.extents;
            var m = toRoot * mf.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    mb.center.x + ((i & 1) == 0 ? -e.x : e.x),
                    mb.center.y + ((i & 2) == 0 ? -e.y : e.y),
                    mb.center.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = m.MultiplyPoint3x4(c);
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        return local;
    }

    static Material _darkMat;

    /// <summary>补板兜底材质（没有车体材质时用）。中灰，像一块装甲盖板，不是黑洞。</summary>
    static Material DarkMat()
    {
        if (_darkMat != null) return _darkMat;
        _darkMat = new Material(Shader.Find("Standard"));
        _darkMat.color = new Color(0.45f, 0.45f, 0.44f);
        _darkMat.SetFloat("_Glossiness", 0.08f);
        return _darkMat;
    }

    static void StripColliders(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
        {
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }
    }

    static void ApplyMaterial(GameObject go, Material mat)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    // ==================================================================
    //  炮塔井修补
    // ==================================================================

    /// <summary>车体网格的修补结果。</summary>
    public struct HullFix
    {
        public Mesh hull;      // 拍平后的车体网格（null = 没改）
        public Mesh plate;     // 补板网格（null = 不需要补）
    }

    /// <summary>
    /// 把车体上被炮塔切开的破口收拾干净。
    ///
    /// 为什么必须"实测"而不是写死数字：
    ///   Lux3D 出来的模型是面片汤（一台车 6 万个独立三角面，没有语义部件），
    ///   part_split 把炮塔那块拿走之后，车体甲板上留下的破口形状不规则，
    ///   而且车体是空心壳 —— 从破口看到的是内壁，也就是那块"黑洞"。
    ///   圆盘盖不住（洞比炮塔宽）、Blender 补面补不准（洞不是干净边界环）。
    ///
    /// 做法两步：
    ///   ① 拍平：炮塔本体足迹内、落在甲板高度附近的顶点，全部按到甲板平面。
    ///      破口那些翘起/塌下的碎面片被压平，甲板就连续了（这一步最关键）。
    ///   ② 补板：拍平之后还低于甲板 0.12 m 以上的格（真正的深洞），
    ///      按实测格生成一块平面盖住。
    /// </summary>
    static class WellFixup
    {
        static readonly Dictionary<string, HullFix> _cache = new Dictionary<string, HullFix>();

        public static HullFix GetOrBuild(VehicleDef def, GameObject root, GameObject modelRoot, Transform turretNode)
        {
            string key = def != null && !string.IsNullOrEmpty(def.id) ? def.id : "?";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var fix = new HullFix();
            try { fix = Build(def, root, modelRoot, turretNode); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WellFixup] 炮塔井修补失败（{key}）：{e.Message}");
            }
            _cache[key] = fix;
            return fix;
        }

        public static void Clear() => _cache.Clear();

        static HullFix Build(VehicleDef def, GameObject root, GameObject modelRoot, Transform turretNode)
        {
            var fix = new HullFix();
            if (root == null || modelRoot == null || turretNode == null) return fix;

            var hmf = modelRoot.GetComponentInChildren<MeshFilter>();
            if (hmf == null || hmf.sharedMesh == null) return fix;
            var tmf = turretNode.GetComponentInChildren<MeshFilter>();
            if (tmf == null || tmf.sharedMesh == null) return fix;

            var rootToLocal = root.transform.worldToLocalMatrix;

            // ---- 1. 炮塔「本体」足迹（车体局部，米）----
            // 炮塔那块零件里带着长炮管，直接用整体包围盒会把车头斜坡也算进来；
            // 所以先按 0.05 m 打点、腐蚀 0.15 m（细炮管被腐蚀掉），取最大连通块的包围盒。
            var toRootT = rootToLocal * tmf.transform.localToWorldMatrix;
            var tv = tmf.sharedMesh.vertices;
            var tt = tmf.sharedMesh.triangles;
            var TP = new Vector3[tv.Length];
            var fmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var fmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < tv.Length; i++)
            {
                TP[i] = toRootT.MultiplyPoint3x4(tv[i]);
                fmin = Vector3.Min(fmin, TP[i]);
                fmax = Vector3.Max(fmax, TP[i]);
            }

            const float tcell = 0.05f;
            const int erode = 3;
            int tnx = Mathf.CeilToInt((fmax.x - fmin.x) / tcell) + 1;
            int tnz = Mathf.CeilToInt((fmax.z - fmin.z) / tcell) + 1;
            if (tnx < 2 * erode + 4 || tnz < 2 * erode + 4 || (long)tnx * tnz > 300000) return fix;

            var fmask = new bool[tnx * tnz];
            for (int t = 0; t < tt.Length; t += 3)
            {
                Vector3 a = TP[tt[t]], b = TP[tt[t + 1]], c = TP[tt[t + 2]];
                float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                int i0 = Mathf.Clamp(Mathf.FloorToInt((minx - fmin.x) / tcell), 0, tnx - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((maxx - fmin.x) / tcell), 0, tnx - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt((minz - fmin.z) / tcell), 0, tnz - 1);
                int j1 = Mathf.Clamp(Mathf.CeilToInt((maxz - fmin.z) / tcell), 0, tnz - 1);
                for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    float px = fmin.x + (i + 0.5f) * tcell, pz = fmin.z + (j + 0.5f) * tcell;
                    if (!InsideTriXY(px, pz, a, b, c)) continue;
                    fmask[j * tnx + i] = true;
                }
            }

            var emask = new bool[tnx * tnz];
            for (int i = erode; i < tnx - erode; i++)
            for (int j = erode; j < tnz - erode; j++)
            {
                bool all = true;
                for (int di = -erode; di <= erode && all; di++)
                for (int dj = -erode; dj <= erode && all; dj++)
                    if (!fmask[(j + dj) * tnx + (i + di)]) all = false;
                emask[j * tnx + i] = all;
            }

            if (!LargestComponentBounds(emask, tnx, tnz, out int bi0, out int bi1, out int bj0, out int bj1)) return fix;

            float rx0 = fmin.x + (bi0 - erode) * tcell - 0.05f;
            float rx1 = fmin.x + (bi1 + 1 + erode) * tcell + 0.05f;
            float rz0 = fmin.z + (bj0 - erode) * tcell - 0.05f;
            float rz1 = fmin.z + (bj1 + 1 + erode) * tcell + 0.05f;

            // ---- 2. 车体网格 → 车体局部 ----
            var toRootH = rootToLocal * hmf.transform.localToWorldMatrix;
            var hv = hmf.sharedMesh.vertices;
            var ht = hmf.sharedMesh.triangles;
            var P = new Vector3[hv.Length];
            for (int i = 0; i < hv.Length; i++) P[i] = toRootH.MultiplyPoint3x4(hv[i]);

            const float cell = 0.08f;
            int nx = Mathf.CeilToInt((rx1 - rx0) / cell);
            int nz = Mathf.CeilToInt((rz1 - rz0) / cell);
            if (nx < 4 || nz < 4 || (long)nx * nz > 200000) return fix;

            var top = new float[nx * nz];
            var uvTop = new Vector2[nx * nz];
            var hasUv = new bool[nx * nz];
            var srcUv = hmf.sharedMesh.uv;
            bool uvOk = srcUv != null && srcUv.Length == hv.Length;

            System.Action rasterize = () =>
            {
                for (int k = 0; k < top.Length; k++) { top[k] = float.NegativeInfinity; hasUv[k] = false; }
                for (int t = 0; t < ht.Length; t += 3)
                {
                    Vector3 a = P[ht[t]], b = P[ht[t + 1]], c = P[ht[t + 2]];
                    float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    if (maxx < rx0 || minx > rx1 || maxz < rz0 || minz > rz1) continue;

                    int i0 = Mathf.Clamp(Mathf.FloorToInt((minx - rx0) / cell), 0, nx - 1);
                    int i1 = Mathf.Clamp(Mathf.CeilToInt((maxx - rx0) / cell), 0, nx - 1);
                    int j0 = Mathf.Clamp(Mathf.FloorToInt((minz - rz0) / cell), 0, nz - 1);
                    int j1 = Mathf.Clamp(Mathf.CeilToInt((maxz - rz0) / cell), 0, nz - 1);
                    float my = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

                    for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        float px = rx0 + (i + 0.5f) * cell, pz = rz0 + (j + 0.5f) * cell;
                        if (!InsideTriXY(px, pz, a, b, c)) continue;
                        int k = j * nx + i;
                        if (my <= top[k]) continue;
                        top[k] = my;
                        if (!uvOk) continue;
                        // 顺手把这个格在车体贴图上的 UV 记下来，补板直接用，颜色才能和甲板连上
                        var v0 = new Vector2(b.x - a.x, b.z - a.z);
                        var v1 = new Vector2(c.x - a.x, c.z - a.z);
                        var v2 = new Vector2(px - a.x, pz - a.z);
                        float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d11 = Vector2.Dot(v1, v1);
                        float d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
                        float den = d00 * d11 - d01 * d01;
                        if (Mathf.Abs(den) < 1e-9f) continue;
                        float wb = (d11 * d20 - d01 * d21) / den;
                        float wc = (d00 * d21 - d01 * d20) / den;
                        float wa = 1f - wb - wc;
                        uvTop[k] = srcUv[ht[t]] * wa + srcUv[ht[t + 1]] * wb + srcUv[ht[t + 2]] * wc;
                        hasUv[k] = true;
                    }
                }
            };

            rasterize();

            // 甲板高度 = 高度直方图众数（5 cm 一格）：这个范围里最多的那个高度就是甲板面
            var heights = new List<float>(nx * nz);
            for (int k = 0; k < top.Length; k++)
                if (!float.IsNegativeInfinity(top[k])) heights.Add(top[k]);
            if (heights.Count < 40) return fix;

            var hist = new Dictionary<int, int>();
            for (int i = 0; i < heights.Count; i++)
            {
                int key = Mathf.RoundToInt(heights[i] * 20f);
                hist[key] = hist.TryGetValue(key, out var c0) ? c0 + 1 : 1;
            }
            int bestKey = 0, bestCount = -1;
            foreach (var kv in hist) if (kv.Value > bestCount) { bestCount = kv.Value; bestKey = kv.Key; }
            float deck = bestKey / 20f;

            // ---- 3. 补板：炮塔足迹内所有低于甲板平面的格，整片盖平 ----
            // 补板比甲板高 2 cm（低了会被翘起的碎面片钻出来，高了会有台阶），
            // UV 用最近一圈甲板的 —— 迷彩纹路接得上，远看就是一片连续甲板。
            float plateY = deck + 0.02f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            var isHole = new bool[nx * nz];
            for (int k = 0; k < top.Length; k++)
            {
                if (float.IsNegativeInfinity(top[k])) continue;
                isHole[k] = top[k] < plateY - 0.005f;
            }

            var seen = new bool[nx * nz];
            var keep = new bool[nx * nz];
            var stack = new Stack<int>();
            var comp = new List<int>();
            int found = 0;
            for (int start = 0; start < isHole.Length; start++)
            {
                if (!isHole[start] || seen[start]) continue;

                comp.Clear();
                stack.Clear();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    comp.Add(k);
                    int i = k % nx, j = k / nx;
                    if (i > 0) { int n = k - 1; if (isHole[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (i < nx - 1) { int n = k + 1; if (isHole[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (j > 0) { int n = k - nx; if (isHole[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (j < nz - 1) { int n = k + nx; if (isHole[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                }
                if (comp.Count * cell * cell < 0.12f) continue;   // 太碎的不管
                foreach (int k in comp) keep[k] = true;
                found++;
            }

            for (int k = 0; k < keep.Length; k++)
            {
                if (!keep[k]) continue;
                int i = k % nx, j = k / nx;
                float x0 = rx0 + i * cell, x1 = x0 + cell;
                float z0 = rz0 + j * cell, z1 = z0 + cell;
                bool left = i == 0 || !keep[k - 1];
                bool right = i == nx - 1 || !keep[k + 1];
                bool down = j == 0 || !keep[k - nx];
                bool up = j == nz - 1 || !keep[k + nx];
                if (left) x0 -= cell;
                if (right) x1 += cell;
                if (down) z0 -= cell;
                if (up) z1 += cell;

                int b = verts.Count;
                verts.Add(new Vector3(x0, plateY, z0));
                verts.Add(new Vector3(x1, plateY, z0));
                verts.Add(new Vector3(x1, plateY, z1));
                verts.Add(new Vector3(x0, plateY, z1));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);

                // 补板 UV：取最近一圈甲板的 UV，让迷彩纹路接得上（接不上就退回单色）
                uvs.Add(NearestUv(uvTop, hasUv, nx, nz, i, j));
                uvs.Add(NearestUv(uvTop, hasUv, nx, nz, i + 1, j));
                uvs.Add(NearestUv(uvTop, hasUv, nx, nz, i + 1, j + 1));
                uvs.Add(NearestUv(uvTop, hasUv, nx, nz, i, j + 1));
            }

            if (verts.Count > 0)
            {
                var plate = new Mesh { name = "TurretWellPatch" };
                plate.SetVertices(verts);
                plate.SetTriangles(tris, 0);
                plate.uv = uvs.ToArray();
                plate.RecalculateNormals();
                plate.RecalculateBounds();
                fix.plate = plate;
            }

            Debug.Log($"[WellFixup] {def.id}: 甲板={deck:F2} 补板 {verts.Count / 4} 格 / {found} 块");
            return fix;
        }

        /// <summary>
        /// 找离 (i,j) 最近的一个「有 UV 记录的甲板格」，把它的 UV 拿来用。
        /// 补板这样就能采到车体迷彩贴图上同一块花纹，不会变成一块纯色板。
        /// 找不到（半径 8 格内都没有）就退回贴图中点。
        /// </summary>
        static Vector2 NearestUv(Vector2[] uvTop, bool[] hasUv, int nx, int nz, int i, int j)
        {
            const int maxR = 8;
            for (int r = 0; r <= maxR; r++)
            {
                float bestD = float.MaxValue;
                Vector2 best = default;
                bool any = false;
                for (int di = -r; di <= r; di++)
                for (int dj = -r; dj <= r; dj++)
                {
                    if (Mathf.Abs(di) != r && Mathf.Abs(dj) != r) continue;   // 只看这一圈的边
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                    int k = nj * nx + ni;
                    if (!hasUv[k]) continue;
                    float d = di * di + dj * dj;
                    if (d < bestD) { bestD = d; best = uvTop[k]; any = true; }
                }
                if (any) return best;
            }
            return new Vector2(0.5f, 0.5f);
        }

        /// <summary>格心是否落在三角形的 XY 投影里。</summary>
        static bool InsideTriXY(float px, float pz, Vector3 a, Vector3 b, Vector3 c)        {
            float d1 = (px - b.x) * (a.z - b.z) - (a.x - b.x) * (pz - b.z);
            float d2 = (px - c.x) * (b.z - c.z) - (b.x - c.x) * (pz - c.z);
            float d3 = (px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z);
            bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(neg && pos);
        }

        /// <summary>最大 4 连通块的包围盒（栅格坐标）。</summary>
        static bool LargestComponentBounds(bool[] mask, int nx, int nz,
            out int bi0, out int bi1, out int bj0, out int bj1)
        {
            bi0 = 0; bi1 = -1; bj0 = 0; bj1 = -1;
            var seen = new bool[mask.Length];
            var stack = new Stack<int>();
            int best = 0;
            for (int s = 0; s < mask.Length; s++)
            {
                if (!mask[s] || seen[s]) continue;
                stack.Clear();
                stack.Push(s);
                seen[s] = true;
                int ci0 = int.MaxValue, ci1 = -1, cj0 = int.MaxValue, cj1 = -1;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    int i = k % nx, j = k / nx;
                    if (i < ci0) ci0 = i;
                    if (i > ci1) ci1 = i;
                    if (j < cj0) cj0 = j;
                    if (j > cj1) cj1 = j;
                    if (i > 0) { int n = k - 1; if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (i < nx - 1) { int n = k + 1; if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (j > 0) { int n = k - nx; if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                    if (j < nz - 1) { int n = k + nx; if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); } }
                }
                int area = (ci1 - ci0 + 1) * (cj1 - cj0 + 1);
                if (area > best)
                {
                    best = area;
                    bi0 = ci0; bi1 = ci1; bj0 = cj0; bj1 = cj1;
                }
            }
            return best > 0;
        }
    }
}
