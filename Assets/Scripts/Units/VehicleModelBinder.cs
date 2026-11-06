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

        // ---------- 鱼雷发射管（只有船有）----------
        if (def.domain == Domain.Sea && vp != null) BuildTorpedoTubes(root.transform, model.transform, def, vp);
        // 攻击机：模型不带弹，机翼下挂 8 枚可消耗的导弹（战斗机没有挂弹，跳过）
        if (def.domain == Domain.Air && HasLimitedAmmoWeapon(def)) BuildWingMissiles(root.transform, model.transform, def);

        // ---------- 炮塔（Lux3D 拆件）----------
        if (def.modelTurretPrefab != null)
        {
            var tp = new GameObject("Turret");
            tp.transform.SetParent(root.transform, false);
            tp.transform.localPosition = def.modelTurretPivot;

            // 摇摆炮塔：上部塔体绕耳轴整体俯仰，下部座圈只转方位（AMX-13 的 FL-10 那种机构）。
            // 层级：Turret(yaw) → Ring(固定) + Elevating(pitch) → 上部塔体 + 炮管（刚性）
            Transform el = tp.transform;
            if (def.oscillatingTurret)
            {
                var elev = new GameObject("Elevating");
                elev.transform.SetParent(tp.transform, false);
                elev.transform.localPosition = def.modelElevatingPivot;
                el = elev.transform;
                // 下部座圈：同一套拆件坐标，直接摆在炮塔节点下，不俯仰
                if (def.modelTurretLowerPrefab != null)
                {
                    var low = Object.Instantiate(def.modelTurretLowerPrefab, tp.transform);
                    low.name = "TurretRing";
                    low.transform.localPosition = def.modelTurretMeshOrigin - def.modelTurretPivot;
                    StripColliders(low);
                    var lm0 = def.modelTurretMaterial != null ? def.modelTurretMaterial : def.modelMaterial;
                    if (lm0 != null) ApplyMaterial(low, lm0);
                }
            }

            var tm = Object.Instantiate(def.modelTurretPrefab, el);
            tm.name = "TurretModel";
            // 炮塔网格的原点是 Blender 拆件时的切割面中心；转轴换成了炮塔本体中心，
            // 所以要把网格反向偏移回去：外观位置不变，只是转起来绕本体中心，不再甩。
            // 摇摆炮塔时还要再减掉耳轴偏移（因为父节点 Elevating 已经挪到耳轴上了）。
            tm.transform.localPosition = def.modelTurretMeshOrigin - def.modelTurretPivot
                                         - (def.oscillatingTurret ? def.modelElevatingPivot : Vector3.zero);
            StripColliders(tm);
            var turMat = def.modelTurretMaterial != null ? def.modelTurretMaterial : def.modelMaterial;
            if (turMat != null) ApplyMaterial(tm, turMat);

            // 炮管（从炮塔上切出来的）
            // 普通炮塔：挂在炮塔下、自己俯仰；摇摆炮塔：刚性固定在上部塔体里，跟着塔体一起俯仰
            Transform barrelNode = null;
            if (def.modelBarrelPrefab != null)
            {
                var bp = new GameObject("Barrel");
                bp.transform.SetParent(el, false);
                bp.transform.localPosition = def.modelBarrelPivot
                                             - (def.oscillatingTurret ? def.modelElevatingPivot : Vector3.zero);
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
                mz.transform.SetParent(el, false);
                var lb = LocalBounds(el);
                mz.transform.localPosition = new Vector3(0f, 0f, lb.max.z * 0.95f);
            }
            if (vp != null)
            {
                // 摇摆炮塔：塔体按 elevatingFollow 比例跟随，炮管转剩下的部分（两者相加 = 目标俯仰角）。
                // elevatingFollow=1 是整块塔体 1:1 跟随（真机构）；0.25 左右是"塔体微动"，
                // 底面扫过只有几厘米，不需要底座也不需要大缝。
                if (def.oscillatingTurret)
                {
                    vp.barrelBone = barrelNode;
                    vp.elevatingBone = el;
                    vp.elevatingFollow = Mathf.Clamp01(def.elevatingFollow);
                }
                else
                {
                    vp.barrelBone = barrelNode;
                }
                if (def.maxElevationOverride > 0f) vp.maxElevation = def.maxElevationOverride;
                if (def.maxDepressionOverride < 0f) vp.maxDepression = def.maxDepressionOverride;
            }

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
            // 摇摆炮塔把 el 传进去：甲板机枪要跟着上部塔体一起俯仰。
            AddTurretExtras(root.transform, el, def, vp);
            return true;
        }

        // ---------- 没有独立炮塔：补逻辑炮塔 ----------
        BuildLogicBones(root, out var lt, out var lm);
        turret = lt;
        muzzle = lm;
        DropToGround(root, model.transform, null, lt, vp);

        // 飞机：机头进气口的涡轮风扇（单网格模型里没有可旋转的叶片，只能程序生成）。
        // DropToGround 已经把模型整体下移了 model.localPosition.y，风扇要跟着下移。
        JetIntakeFan.Attach(root, def, vp, model.transform.localPosition.y);
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
            // 车体模型里已经用 Blender 焊了一块圆形补板的话，这里就不再加方板 ——
            // 方板的四个角会伸到甲板外面，两层补丁叠在一起反而更难看。
            if (!def.bakedTurretWell)
            {
            // 板比炮塔足迹放大 25%：车体被切开时边缘会留下毛刺三角，板小一点就露在外面。
            // 顶面再抬高 5 cm 把这些毛刺压到板下面。
            const float plateH = 0.60f;
            var plate = MakePlate("TurretWellPlate", size.x * 1.25f, size.z * 1.25f, plateH, hullMat);
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(
                turret.localPosition.x, deckY + 0.05f - plateH * 0.5f, turret.localPosition.z);
            }
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

    /// <summary>
    /// 鱼雷发射管：两舷各一根，斜向前方。
    ///
    /// 以前鱼雷是从"主炮炮口位置压到水线"凭空冒出来的，看着像从船肚子里吐出来的。
    /// 现在给两根可见的发射管，鱼雷从管口出去（`VehicleCombat.FireTorpedo` 读这里的管口）。
    /// </summary>
    /// <summary>
    /// 在模型网格里量某个翼展位置的**机翼下表面**，返回模型局部坐标。
    /// Lux3D 的 FBX 轴向：X=翼展、Y=前后（-Y 是机头）、Z=高度（Z-up）。
    /// 起落架/挂架会挂在更低的位置，所以低于 -1.6 的顶点直接忽略。
    /// </summary>
    static Vector3 WingUnderside(Transform model, float localX, float localZCenter)
    {
        var mf = model.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
            return new Vector3(localX, localZCenter, -1.0f);

        float x = Mathf.Abs(localX);
        float bestZ = float.MaxValue, bestY = 0f;
        bool any = false;
        foreach (var v in mf.sharedMesh.vertices)
        {
            if (Mathf.Abs(Mathf.Abs(v.x) - x) > 0.45f) continue;
            if (Mathf.Abs(v.y) > 1.4f) continue;      // 只取机翼弦长范围
            if (v.z < -1.6f) continue;                // 起落架/吊舱
            if (v.z < bestZ) { bestZ = v.z; bestY = v.y; any = true; }
        }
        if (!any) return new Vector3(localX, localZCenter, -1.0f);
        return new Vector3(Mathf.Sign(localX) * x, bestY, bestZ - 0.18f);   // 再往下 18 cm 挂弹
    }

    /// <summary>这把武器有没有备弹上限（= 机翼上要不要挂实弹）。</summary>
    static bool HasLimitedAmmoWeapon(VehicleDef def)
    {
        if (def == null || def.weapons == null) return false;
        foreach (var w in def.weapons)
            if (w != null && w.ammoCapacity > 0) return true;
        return false;
    }

    /// <summary>
    /// 机翼挂架上的 8 枚导弹（左右各 4）。打一枚藏一枚，复活重挂。
    /// 模型本身是"空挂架"版本，弹体全部是运行时拼的简易几何。
    /// </summary>
    static void BuildWingMissiles(Transform root, Transform model, VehicleDef def)
    {
        if (model == null || root == null) return;

        // 这里**只登记**：真正的弹体由 WingStores.Start() 创建。
        // 原因：模型实例化后还要经过 DropToGround 重新定位（localPosition.y = 1.68），
        // 在 Attach 阶段算世界坐标会少掉这段偏移，弹会掉到机身下面一米多。
        var stores = root.gameObject.GetComponent<WingStores>();
        if (stores == null) stores = root.gameObject.AddComponent<WingStores>();
        stores.model = model;
        stores.perSide = 4;
        stores.mat = def.modelMaterial != null ? def.modelMaterial : DarkMat();
    }

    static void BuildTorpedoTubes(Transform root, Transform model, VehicleDef def, VehiclePhysics vp)
    {        if (model == null) return;

        Bounds b = default;
        bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) return;

        var mat = def.modelMaterial != null ? def.modelMaterial : DarkMat();
        Vector3 c = root.InverseTransformPoint(b.center);
        Vector3 size = root.InverseTransformVector(b.size);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        float halfW = size.x * 0.5f;
        // 发射管要贴水线：按**水面**算高度（离水面 1.1 m），不要按船体包围盒顶算 ——
        // 包围盒把上层建筑也算进去了，管子会挂到驾驶舱高度（实测高水面 4 m，看着像朝天放）。
        float deckY = (BattlefieldTerrain.WaterLevel - root.position.y) + 1.1f;
        float zPos = c.z - size.z * 0.05f;             // 船体中后部

        for (int side = -1; side <= 1; side += 2)
        {
            var tube = new GameObject(side < 0 ? "TorpedoTubeL" : "TorpedoTubeR");
            tube.transform.SetParent(root, false);
            tube.transform.localPosition = new Vector3(side * halfW * 0.62f, deckY, zPos);
            tube.transform.localRotation = Quaternion.Euler(0f, side * 9f, 0f);   // 稍微外撇

            // 管体（沿 +Z）
            AddPart(tube.transform, "TubeBody", PrimitiveType.Cylinder,
                    new Vector3(0.58f, 1.55f, 0.58f), new Vector3(0f, 0f, 0.85f), new Vector3(90f, 0f, 0f), mat);
            // 管口加强环
            AddPart(tube.transform, "TubeRim", PrimitiveType.Cylinder,
                    new Vector3(0.68f, 0.10f, 0.68f), new Vector3(0f, 0f, 1.62f), new Vector3(90f, 0f, 0f), mat);
            // 底座支架
            AddPart(tube.transform, "TubeMount", PrimitiveType.Cube,
                    new Vector3(0.42f, 0.34f, 0.85f), new Vector3(0f, -0.30f, 0.85f), Vector3.zero, mat);

            // 管口节点（鱼雷从这里出去）
            var mz = new GameObject("TubeMuzzle");
            mz.transform.SetParent(tube.transform, false);
            mz.transform.localPosition = new Vector3(0f, 0f, 1.72f);

            if (side < 0) vp.torpedoTubeL = mz.transform;
            else vp.torpedoTubeR = mz.transform;
        }
    }

    /// <summary>
    /// 车顶/甲板机枪（12.7mm 高射机枪座）：回转底座 + 立柱 + 摇架 + 枪身 + 散热套筒 + 枪管 + 消焰器
    /// + 弹箱 + 提把 + 环形机械瞄具。
    ///
    /// 两条硬要求（用户 2026-11-05 提的）：
    ///   1. **不能和塔顶有缝**：底座高度不是拿包围盒顶估的，而是给塔体网格临时挂 MeshCollider、
    ///      从正上方往下打射线**实测塔顶曲面**，再压进去 2 cm。塔顶是斜的/有起伏也不会有缝。
    ///   2. **俯仰绕耳轴转**：mgBone 指摇架（yoke），底座和立柱不动。旧版把整个挂点当转轴，
    ///      一转就从底座上"翻"下去。
    ///
    /// 层级：DeckGun（底座，贴塔顶，不俯仰）
    ///         └── MG_Yoke（耳轴，绕 X 俯仰 = mgBone）
    ///               └── 枪身/枪管/弹箱/瞄具 …（MG_Muzzle 在枪口，出膛点用它）
    /// </summary>
    static void BuildDeckGun(Transform turret, VehicleDef def, VehiclePhysics vp,
                             Vector3 center, Vector3 size, float top)
    {
        if (vp == null) return;

        // ---------- 1. 落点：塔顶靠后一点（避开前面的炮盾），有手写 pivot 就用手写的 ----------
        bool manual = def.modelDeckGunPivot != Vector3.zero;
        Vector3 mountXZ = manual
            ? def.modelDeckGunPivot
            : new Vector3(center.x, 0f, center.z - size.z * 0.20f);

        // ---------- 2. 实测塔顶高度（临时 MeshCollider + 竖直向下射线） ----------
        float roofY = manual ? def.modelDeckGunPivot.y : top;
        if (!manual) roofY = MeasureRoofY(turret, mountXZ, top);

        var mg = new GameObject("DeckGun");
        mg.transform.SetParent(turret, false);
        // 压进塔顶 2 cm：底座和塔面重叠一点，任何角度看都没有缝
        mg.transform.localPosition = new Vector3(mountXZ.x, roofY - 0.02f, mountXZ.z);

        // 机枪整体尺寸：每台车在 asset 里指定（炮艇 1.0 = 12.7mm 原尺寸，坦克 0.7）。
        // 别用包围盒自动算 —— 炮塔包围盒把炮管也算进去了，量出来的长度没有可比性。
        float gunScale = Mathf.Clamp(def.deckGunScale, 0.3f, 1.5f);
        mg.transform.localScale = Vector3.one * gunScale;

        vp.mgBone = null;   // 下面指到摇架
        vp.mgMaxElevation = 85f;
        vp.mgMaxDepression = -12f;

        var gun = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.115f, 0.12f, 0.13f)
        };
        gun.SetFloat("_Glossiness", 0.42f);
        gun.SetFloat("_Metallic", 0.72f);

        var steel = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.17f, 0.175f, 0.18f)
        };
        steel.SetFloat("_Glossiness", 0.35f);
        steel.SetFloat("_Metallic", 0.55f);

        // ---------- 3. 底座（不俯仰） ----------
        // 底盘：一大片圆盘，压在塔顶上，盖住塔面的小起伏
        AddPart(mg.transform, "MG_BasePlate", PrimitiveType.Cylinder,
                new Vector3(0.54f, 0.06f, 0.54f), new Vector3(0f, 0.01f, 0f), Vector3.zero, steel);
        // 座圈
        AddPart(mg.transform, "MG_BaseRing", PrimitiveType.Cylinder,
                new Vector3(0.34f, 0.07f, 0.34f), new Vector3(0f, 0.07f, 0f), Vector3.zero, gun);
        // 立柱
        AddPart(mg.transform, "MG_Pedestal", PrimitiveType.Cylinder,
                new Vector3(0.22f, 0.32f, 0.22f), new Vector3(0f, 0.25f, 0f), Vector3.zero, gun);

        // ---------- 4. 回转座 + 摇架 ----------
        // MG_Yaw 绕 Y 转（枪架自己扫射，不用拖着炮塔）；MG_Yoke 绕 X 转（俯仰）= mgBone
        var mgYaw = new GameObject("MG_Yaw");
        mgYaw.transform.SetParent(mg.transform, false);
        mgYaw.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        vp.mgYawBone = mgYaw.transform;

        var yoke = new GameObject("MG_Yoke");
        yoke.transform.SetParent(mgYaw.transform, false);
        yoke.transform.localPosition = Vector3.zero;
        vp.mgBone = yoke.transform;

        // 摇架两块侧板
        AddPart(yoke.transform, "MG_CradleL", PrimitiveType.Cube,
                new Vector3(0.055f, 0.26f, 0.30f), new Vector3(-0.115f, -0.02f, 0.02f), Vector3.zero, gun);
        AddPart(yoke.transform, "MG_CradleR", PrimitiveType.Cube,
                new Vector3(0.055f, 0.26f, 0.30f), new Vector3(0.115f, -0.02f, 0.02f), Vector3.zero, gun);

        // 枪身（机匣）
        AddPart(yoke.transform, "MG_Receiver", PrimitiveType.Cube,
                new Vector3(0.17f, 0.19f, 0.66f), new Vector3(0f, 0f, 0.04f), Vector3.zero, gun);
        // 机匣顶盖（薄一点，做出层次）
        AddPart(yoke.transform, "MG_TopCover", PrimitiveType.Cube,
                new Vector3(0.15f, 0.05f, 0.52f), new Vector3(0f, 0.11f, 0.02f), Vector3.zero, steel);

        // 散热套筒（枪管外面那圈带筋的筒）
        AddPart(yoke.transform, "MG_Jacket", PrimitiveType.Cylinder,
                new Vector3(0.175f, 0.42f, 0.175f), new Vector3(0f, 0f, 0.58f), new Vector3(90f, 0f, 0f), gun);
        // 套筒上的散热环（三圈）
        for (int i = 0; i < 3; i++)
            AddPart(yoke.transform, $"MG_JacketRib{i}", PrimitiveType.Cylinder,
                    new Vector3(0.20f, 0.035f, 0.20f), new Vector3(0f, 0f, 0.44f + i * 0.14f),
                    new Vector3(90f, 0f, 0f), steel);

        // 枪管
        AddPart(yoke.transform, "MG_Barrel", PrimitiveType.Cylinder,
                new Vector3(0.072f, 0.78f, 0.072f), new Vector3(0f, 0f, 1.10f), new Vector3(90f, 0f, 0f), gun);
        // 消焰器 / 制退器
        AddPart(yoke.transform, "MG_Muzzle", PrimitiveType.Cylinder,
                new Vector3(0.125f, 0.16f, 0.125f), new Vector3(0f, 0f, 1.53f), new Vector3(90f, 0f, 0f), steel);

        // 弹箱（挂在机匣左侧，往枪身里喂弹）
        AddPart(yoke.transform, "MG_AmmoBox", PrimitiveType.Cube,
                new Vector3(0.21f, 0.27f, 0.35f), new Vector3(-0.235f, -0.06f, 0.0f), Vector3.zero, steel);
        AddPart(yoke.transform, "MG_AmmoFeed", PrimitiveType.Cube,
                new Vector3(0.14f, 0.09f, 0.12f), new Vector3(-0.13f, 0.06f, 0.10f), Vector3.zero, gun);

        // 双手握把（短粗、贴着机匣尾部往下翘 —— 高射机枪的扳机握把）
        AddPart(yoke.transform, "MG_GripL", PrimitiveType.Cylinder,
                new Vector3(0.085f, 0.10f, 0.085f), new Vector3(-0.085f, -0.15f, -0.28f), new Vector3(100f, 0f, 0f), gun);
        AddPart(yoke.transform, "MG_GripR", PrimitiveType.Cylinder,
                new Vector3(0.085f, 0.10f, 0.085f), new Vector3(0.085f, -0.15f, -0.28f), new Vector3(100f, 0f, 0f), gun);
        // 机匣尾板（把后面的方口堵上，不然像个空盒子）
        AddPart(yoke.transform, "MG_BufferPlate", PrimitiveType.Cube,
                new Vector3(0.15f, 0.17f, 0.04f), new Vector3(0f, 0f, -0.36f), Vector3.zero, steel);

        // 机械瞄具：后环形照门（16 段小方块拼一个环，块放大到互相压住，看着是"环"不是"一串点"）
        AddPart(yoke.transform, "MG_SightPost", PrimitiveType.Cube,
                new Vector3(0.026f, 0.09f, 0.026f), new Vector3(0f, 0.155f, 0.26f), Vector3.zero, steel);
        const float ringR = 0.062f;
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f;
            AddPart(yoke.transform, $"MG_SightRing{i}", PrimitiveType.Cube,
                    new Vector3(0.030f, 0.030f, 0.016f),
                    new Vector3(Mathf.Cos(a) * ringR, 0.215f + Mathf.Sin(a) * ringR, 0.26f),
                    new Vector3(0f, 0f, a * Mathf.Rad2Deg), steel);
        }
        // 前准星（套筒前端一个小立柱）
        AddPart(yoke.transform, "MG_FrontSight", PrimitiveType.Cube,
                new Vector3(0.020f, 0.075f, 0.020f), new Vector3(0f, 0.115f, 0.74f), Vector3.zero, steel);

        // ---------- 5. 枪口节点（出膛点；比几何上的消焰器口再往前 3 cm） ----------
        var muzzle = new GameObject("MG_MuzzlePoint");
        muzzle.transform.SetParent(yoke.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 1.63f);
        vp.mgMuzzle = muzzle.transform;
    }

    /// <summary>
    /// 量炮塔顶面在某个水平位置的真实高度（塔体本地坐标），返回**底座应该坐的高度**。
    ///
    /// 关键：取 9 个采样点（中心 + 半径 0.26 一圈）里的**最低点**，不是最高点。
    /// 塔顶基本都有坡度 —— 用最高点的话，底座在低的一侧会悬空（实测就是这个"缝"）。
    /// 用最低点则上坡侧沉进塔体几厘米（看不出来），下坡侧正好贴住。
    /// 再加个夹子：最多比最高点低 10 cm，免得塔顶有个大台阶时整个机枪陷进去。
    ///
    /// 做法和炮塔井补板一样：临时给塔体网格挂 MeshCollider，从正上方打射线，量完删掉。
    /// </summary>
    static float MeasureRoofY(Transform turret, Vector3 localXZ, float fallback)
    {
        var temp = new List<MeshCollider>();
        foreach (var mf in turret.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc);
        }
        if (temp.Count == 0) return fallback;

        float minY = float.MaxValue, maxY = float.MinValue;
        const float ring = 0.26f;
        for (int i = 0; i < 9; i++)
        {
            float ox = 0f, oz = 0f;
            if (i > 0)
            {
                float a = (i - 1) / 8f * Mathf.PI * 2f;
                ox = Mathf.Cos(a) * ring;
                oz = Mathf.Sin(a) * ring;
            }
            Vector3 from = turret.TransformPoint(new Vector3(localXZ.x + ox, 6f, localXZ.z + oz));
            var hits = Physics.RaycastAll(from, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var mc = h.collider.GetComponent<MeshCollider>();
                if (mc == null || !temp.Contains(mc)) continue;
                float ly = turret.InverseTransformPoint(h.point).y;
                if (ly < minY) minY = ly;
                if (ly > maxY) maxY = ly;
            }
        }

        foreach (var mc in temp) Object.Destroy(mc);
        if (minY == float.MaxValue) return fallback;

        // 最低点坐下，但最多比最高点低 10 cm（塔顶有台阶时不至于陷进去）
        return Mathf.Max(minY, maxY - 0.10f);
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
