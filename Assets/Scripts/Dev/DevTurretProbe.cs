#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 炮塔装配探针（仅编辑器用，不参与打包）。
///
/// 为什么要有它：主战坦克「炮塔不在中心」这件事，之前一直靠进 Play 模式跑 DevTankShots
/// 连拍 23 张来验证，一轮 3~5 分钟，根本没法快速试参数。
/// 这个台子只做一件事：按 VehicleDef 的 pivot 把「车体 + 炮塔 + 炮管」摆好，
/// 直接从正上方 / 正后方各渲染一张正交图，1 秒出结果。
///
/// 用法（编辑器里执行）：
///   DevTurretProbe.Render(def, "work/probe/assemble", pivot, meshOrigin, barrelPivot);
/// 传 Vector3.zero 给 pivot 表示"读 def 里的值"。
/// </summary>
public static class DevTurretProbe
{
    const string Root = "D:/UnityProjects/Tank3D/work/probe";

    /// <summary>摆一台车并渲染若干正交视角。返回渲染出的文件路径。</summary>
    public static string Render(VehicleDef def, string tag,
                                Vector3 pivotOverride = default,
                                Vector3 meshOriginOverride = default,
                                Vector3 barrelPivotOverride = default)
    {
        if (def == null || def.modelPrefab == null) return "no def";

        Vector3 pivot = pivotOverride == default ? def.modelTurretPivot : pivotOverride;
        Vector3 meshOrigin = meshOriginOverride == default ? def.modelTurretMeshOrigin : meshOriginOverride;
        Vector3 barrelPivot = barrelPivotOverride == default ? def.modelBarrelPivot : barrelPivotOverride;

        Directory.CreateDirectory(Root);

        var old = GameObject.Find("DevTurretProbeRoot");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("DevTurretProbeRoot");
        // 抬到高空，避开场景地形/其他物体（正交俯视会把地形也拍进来）
        root.transform.position = new Vector3(0f, 400f, 0f);

        // ---- 车体 ----
        var hull = (GameObject)Object.Instantiate(def.modelPrefab, root.transform);
        hull.name = "Model";
        var baseScale = hull.transform.localScale;
        var baseRot = hull.transform.localRotation;
        hull.transform.localPosition = Vector3.zero;
        hull.transform.localRotation = Quaternion.Euler(0f, def.modelYawOffset, 0f) * baseRot;
        hull.transform.localScale = baseScale * Mathf.Max(0.001f, def.modelScale);
        if (def.modelMaterial != null) Paint(hull, def.modelMaterial);

        // ---- 炮塔 ----
        var tp = new GameObject("Turret");
        tp.transform.SetParent(root.transform, false);
        tp.transform.localPosition = pivot;

        var tm = (GameObject)Object.Instantiate(def.modelTurretPrefab, tp.transform);
        tm.name = "TurretModel";
        tm.transform.localPosition = meshOrigin - pivot;
        var turMat = def.modelTurretMaterial != null ? def.modelTurretMaterial : def.modelMaterial;
        if (turMat != null) Paint(tm, turMat);

        // ---- 炮管 ----
        if (def.modelBarrelPrefab != null)
        {
            var bp = new GameObject("Barrel");
            bp.transform.SetParent(tp.transform, false);
            bp.transform.localPosition = barrelPivot;
            var bm = (GameObject)Object.Instantiate(def.modelBarrelPrefab, bp.transform);
            bm.name = "BarrelModel";
            bm.transform.localPosition = Vector3.zero;
            if (turMat != null) Paint(bm, turMat);
        }

        // ---- 参考标记：车体中轴线（红）+ 炮管轴线（黄），放在整车顶上方，俯视时才看得见 ----
        var bAll = Bounds(root.transform, root.transform);     // 世界空间
        var b0 = Bounds(root.transform, hull.transform);       // 世界空间
        float markY = bAll.max.y + 0.35f;
        var mid = root.transform.InverseTransformPoint(new Vector3(0f, 0f, b0.center.z));
        float barLen = b0.size.z * 1.04f;
        MakeBar(root.transform, "AxisCenterline", new Vector3(mid.x, markY, mid.z),
                new Vector3(0.04f, 0.02f, barLen), new Color(1f, 0.15f, 0.15f));
        MakeBar(root.transform, "AxisGun", new Vector3(pivot.x, markY, mid.z),
                new Vector3(0.04f, 0.02f, barLen), new Color(1f, 0.9f, 0.2f));

        // ---- 灯光 ----
        var lightGo = new GameObject("ProbeLight");
        lightGo.transform.SetParent(root.transform, false);
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.05f;
        lt.color = new Color(1f, 0.98f, 0.94f);
        lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

        // ---- 相机 ----
        var camGo = new GameObject("ProbeCam");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.11f, 0.12f, 0.13f);
        cam.orthographic = true;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;

        // 车体包围盒（root 空间）
        var b = Bounds(root.transform, hull.transform);
        float size = Mathf.Max(b.size.x, b.size.z);
        string outPath = $"{Root}/{tag}_top.png";

        // 正上方俯视：图像上方 = +Z（车头朝上）
        cam.orthographicSize = size * 0.58f;
        cam.transform.position = new Vector3(b.center.x, b.center.y + 40f, b.center.z);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        Shot(cam, outPath, 1000, 1000);

        // 正后方（游戏内第三人称视角）
        cam.orthographic = false;
        cam.fieldOfView = 50f;
        cam.transform.position = root.transform.TransformPoint(new Vector3(0f, 4.2f, -14f));
        cam.transform.rotation = Quaternion.LookRotation(
            root.transform.TransformPoint(new Vector3(0f, 2.2f, 0f)) - cam.transform.position);
        Shot(cam, $"{Root}/{tag}_behind.png", 1000, 640);

        // 正前方
        cam.transform.position = root.transform.TransformPoint(new Vector3(0f, 3.4f, 13f));
        cam.transform.rotation = Quaternion.LookRotation(
            root.transform.TransformPoint(new Vector3(0f, 2.0f, 0f)) - cam.transform.position);
        Shot(cam, $"{Root}/{tag}_front.png", 1000, 640);

        // 侧面近景：看炮塔和甲板的接缝（有没有悬空 / 陷进去）
        cam.fieldOfView = 40f;
        cam.transform.position = root.transform.TransformPoint(new Vector3(-7.5f, 2.6f, 0f));
        cam.transform.rotation = Quaternion.LookRotation(
            root.transform.TransformPoint(new Vector3(0f, 2.3f, 0f)) - cam.transform.position);
        Shot(cam, $"{Root}/{tag}_side.png", 1000, 640);

        Object.DestroyImmediate(root);
        return outPath;
    }

    /// <summary>世界空间的渲染包围盒（相机取景用）。</summary>
    static Bounds Bounds(Transform root, Transform hull)
    {
        bool any = false;
        var bb = new Bounds();
        foreach (var r in hull.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bb = r.bounds; any = true; }
            else bb.Encapsulate(r.bounds);
        }
        return bb;
    }

    /// <summary>一根细长条，当作参考线。</summary>
    static void MakeBar(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        var shader = Shader.Find("Unlit/Color");
        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        mat.color = color;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void Paint(GameObject go, Material mat)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    static void Shot(Camera cam, string path, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
#endif
