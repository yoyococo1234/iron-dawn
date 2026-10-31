using UnityEngine;

/// <summary>
/// 模型挂点绑定：把"精细模型"接到代码上。
/// 代码只认三个东西：整体、炮塔节点、炮口节点。模型长什么样无所谓。
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
    /// 挂模型 + 补逻辑炮塔。找不到模型返回 false（调用方自己退回方块占位）。
    /// 运行时可调用（用 VehicleDef.modelPrefab，不碰 AssetDatabase）。
    /// </summary>
    public static bool Attach(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        turret = null;
        muzzle = null;
        if (root == null || def == null || def.modelPrefab == null) return false;

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

        // 模型本身不做碰撞（碰撞交给逻辑，别让物理引擎和我们的贴地打架）
        foreach (var c in model.GetComponentsInChildren<Collider>(true))
        {
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }

        turret = FindBone(model, def.turretBoneName);
        muzzle = FindBone(model, def.muzzleBoneName);

        // 模型里有真实的炮塔+炮口节点才算"可视炮塔"，否则用逻辑炮塔（出膛点固定在车头）
        var vp = root.GetComponent<VehiclePhysics>();
        if (vp != null) vp.turretIsVisual = turret != null && muzzle != null;

        if (def.modelMaterial != null)
        {
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = def.modelMaterial;
                r.sharedMaterials = mats;
            }
        }

        // 模型是整体网格时没有炮塔节点 —— 按包围盒补一个逻辑炮塔
        if (turret == null || muzzle == null)
        {
            BuildLogicBones(root, out var lt, out var lm);
            if (turret == null) turret = lt;
            if (muzzle == null) muzzle = lm;
        }
        return true;
    }

    /// <summary>
    /// Lux3D 的模型是整体网格，没有独立炮塔节点。按包围盒补一个"逻辑炮塔 + 炮口"，
    /// 让瞄准方向、出膛位置、弹道起点都是真的（视觉上炮塔暂不旋转）。
    /// </summary>
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
}
