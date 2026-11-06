using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹药/导弹的可视模型。
///
/// 炮弹原来只有一条曳光、看不见弹体；这里按弹药 id 给飞行体挂一个模型。
/// 模型是 Lux3D 生成（积分），Blender 转成 FBX 放在 `Assets/Resources/Models/Weapons/`，
/// 运行时用 Resources.Load 取 —— 这样不用在预制体/资产里拖引用，程序建的炮弹也能直接长出来。
///
/// 出厂模型尺寸和朝向都不固定，所以这里统一做两件事：
///   ① 按「最长边 = 目标长度」等比缩放（炮弹 0.7 m、空空导弹 3 m、反坦克导弹 1.2 m）
///   ② 把最长的那根轴转到 +Z（Unity 里飞行体是朝 forward 飞的）
/// </summary>
public static class WeaponVisual
{
    static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>();

    /// <summary>
    /// 弹种配色。Lux3D 出的模型是白模（贴图在转 FBX 时丢了），
    /// 这里按弹种给一个纯色，至少能一眼分清穿甲/高爆/破甲/导弹。
    /// </summary>
    static Material MatFor(string ammoId)
    {
        if (_matCache.TryGetValue(ammoId, out var m) && m != null) return m;

        Color c;
        switch (ammoId)
        {
            case "ap": c = new Color(0.22f, 0.23f, 0.25f); break;      // 穿甲弹：钢黑
            case "he": c = new Color(0.34f, 0.31f, 0.19f); break;      // 高爆弹：军绿
            case "heat": c = new Color(0.42f, 0.40f, 0.36f); break;    // 破甲弹：浅灰
            case "missile_aa": c = new Color(0.62f, 0.64f, 0.66f); break;  // 空空导弹：亮灰
            case "missile_at": c = new Color(0.30f, 0.33f, 0.24f); break;  // 反坦克导弹：暗绿
            default: c = new Color(0.45f, 0.45f, 0.45f); break;
        }

        m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.38f);
        m.SetFloat("_Metallic", 0.55f);
        _matCache[ammoId] = m;
        return m;
    }

    /// <summary>弹药 id → Resources 路径。没配的返回 null（保持原样，只有曳光）。</summary>
    static string PathFor(string ammoId)
    {
        switch (ammoId)
        {
            case "ap": return "Models/Weapons/shell_ap";
            case "he": return "Models/Weapons/shell_he";
            case "heat": return "Models/Weapons/shell_heat";
            case "missile_aa": return "Models/Weapons/missile_aam";
            case "missile_at": return "Models/Weapons/missile_atgm";
            default: return null;
        }
    }

    /// <summary>目标长度（米）。</summary>
    static float TargetLength(string ammoId)
    {
        switch (ammoId)
        {
            case "missile_aa": return 3.0f;
            case "missile_at": return 1.2f;
            default: return 0.7f;
        }
    }

    /// <summary>给飞行体挂一个弹药模型。找不到模型就静默跳过（不影响开火）。</summary>
    public static void Attach(Transform parent, string ammoId)
    {
        var path = PathFor(ammoId);
        if (path == null || parent == null) return;

        if (!_cache.TryGetValue(path, out var prefab) || prefab == null)
        {
            prefab = Resources.Load<GameObject>(path);
            _cache[path] = prefab;
        }
        if (prefab == null) return;

        var go = Object.Instantiate(prefab, parent);
        go.name = "Visual_" + ammoId;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // 先在父节点空间量一遍包围盒（还没缩放/旋转）
        if (!TryBoundsInParent(go, parent, out var b)) return;

        float len = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        float k = len > 0.0001f ? TargetLength(ammoId) / len : 1f;
        go.transform.localScale = Vector3.one * k;

        // 最长轴转到 +Z
        Quaternion rot = Quaternion.identity;
        if (b.size.y >= b.size.x && b.size.y >= b.size.z) rot = Quaternion.Euler(90f, 0f, 0f);
        else if (b.size.x > b.size.z) rot = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localRotation = rot;

        // 几何中心挪到原点：不然弹体看起来偏在飞行点一侧
        go.transform.localPosition = -(rot * (b.center * k));

        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = MatFor(ammoId);   // 白模 → 按弹种上色
        }
    }

    /// <summary>把子物体的世界包围盒换算成父节点局部空间（近似，够用）。</summary>
    static bool TryBoundsInParent(GameObject go, Transform parent, out Bounds result)
    {
        result = default;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;

        bool any = false;
        foreach (var r in rs)
        {
            Vector3 c = parent.InverseTransformPoint(r.bounds.center);
            Vector3 s = parent.InverseTransformVector(r.bounds.size);
            var bb = new Bounds(c, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            if (!any) { result = bb; any = true; }
            else result.Encapsulate(bb);
        }
        return any;
    }
}
