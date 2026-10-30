using UnityEngine;

/// <summary>
/// 模型挂点绑定：把"精细模型"接到代码上。
/// 代码只认三个东西：整体、炮塔节点、炮口节点。模型长什么样无所谓。
/// </summary>
public static class VehicleModelBinder
{
    /// <summary>
    /// 尝试从 AssetDatabase 自动找模型（编辑器用）。
    /// 约定：Assets/Models/Vehicles/{id}.prefab 或 {id}.glb
    /// </summary>
    public static GameObject AutoFindModel(string vehicleId)
    {
#if UNITY_EDITOR
        string[] candidates =
        {
            $"Assets/Models/Vehicles/{vehicleId}.prefab",
            $"Assets/Models/Vehicles/{vehicleId}.glb",
            $"Assets/Models/Vehicles/{vehicleId}.fbx",
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
        {
            if (t.name == boneName) return t;
        }
        // 退一步：忽略大小写
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(t.name, boneName, System.StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }

    /// <summary>
    /// 把模型实例挂到载具根节点下，并修正朝向/缩放。
    /// 返回 (炮塔节点, 炮口节点)。
    /// </summary>
    public static void Attach(GameObject root, GameObject modelSource, VehicleDef def,
        out Transform turret, out Transform muzzle)
    {
        turret = null;
        muzzle = null;
        if (root == null || modelSource == null || def == null) return;

        var model = Object.Instantiate(modelSource, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, def.modelYawOffset, 0f);
        model.transform.localScale = Vector3.one * Mathf.Max(0.001f, def.modelScale);

        // 模型本身不做碰撞（碰撞交给逻辑，别让物理引擎和我们的贴地打架）
        foreach (var c in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);

        turret = FindBone(model, def.turretBoneName);
        muzzle = FindBone(model, def.muzzleBoneName);

        if (turret == null)
            Debug.LogWarning($"[VehicleModelBinder] {def.displayName}: 没找到炮塔节点「{def.turretBoneName}」，炮塔将无法转动。请检查模型节点名，或在 VehicleDef 里改 turretBoneName。");
        if (muzzle == null)
            Debug.LogWarning($"[VehicleModelBinder] {def.displayName}: 没找到炮口节点「{def.muzzleBoneName}」，开火将使用默认位置。");
    }
}
