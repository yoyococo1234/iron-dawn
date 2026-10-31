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

        // ---------- 炮塔（Lux3D 拆件）----------
        if (def.modelTurretPrefab != null)
        {
            var tp = new GameObject("Turret");
            tp.transform.SetParent(root.transform, false);
            tp.transform.localPosition = def.modelTurretPivot;

            var tm = Object.Instantiate(def.modelTurretPrefab, tp.transform);
            tm.name = "TurretModel";
            // 炮塔网格已经是以转轴为原点的，这里只保留 FBX 自带的上轴修正和缩放
            tm.transform.localPosition = Vector3.zero;
            StripColliders(tm);
            if (def.modelMaterial != null) ApplyMaterial(tm, def.modelMaterial);

            // 炮口放在炮塔最前端（含炮管）
            var lb = LocalBounds(tp.transform);
            var mz = new GameObject("Muzzle");
            mz.transform.SetParent(tp.transform, false);
            mz.transform.localPosition = new Vector3(0f, 0f, lb.max.z * 0.95f);

            // 炮塔井：一个**深色**圆盘压在车体切割面下方。
            // 之前用迷彩贴图的大圆盘 → 看着像"板子叠上去"；改用深灰、缩到刚好盖住洞口，
            // 炮塔转开时看到的就是一个有阴影的炮塔井，而不是一个黑窟窿。
            if (def.showTurretRing)
            {
                float radius = Mathf.Clamp(lb.size.x * 0.46f, 0.7f, 2.2f);
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cap.name = "TurretWell";
                cap.transform.SetParent(root.transform, false);
                cap.transform.localPosition = def.modelTurretPivot + Vector3.down * 0.12f;
                cap.transform.localScale = new Vector3(radius * 2f, 0.06f, radius * 2f);
                var capCol = cap.GetComponent<Collider>();
                if (capCol != null) { if (Application.isPlaying) Object.Destroy(capCol); else Object.DestroyImmediate(capCol); }
                var capR = cap.GetComponent<Renderer>();
                if (capR != null) capR.sharedMaterial = DarkMat();
            }

            turret = tp.transform;
            muzzle = mz.transform;
            if (vp != null) vp.turretIsVisual = true;
            return true;
        }

        // ---------- 没有独立炮塔：补逻辑炮塔 ----------
        BuildLogicBones(root, out var lt, out var lm);
        turret = lt;
        muzzle = lm;
        return true;
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

    /// <summary>炮塔井用的深灰材质（共用一份）。</summary>
    static Material DarkMat()
    {
        if (_darkMat != null) return _darkMat;
        _darkMat = new Material(Shader.Find("Standard"));
        _darkMat.color = new Color(0.09f, 0.09f, 0.10f);
        _darkMat.SetFloat("_Glossiness", 0.05f);
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
}
