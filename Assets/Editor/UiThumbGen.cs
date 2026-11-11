using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成车库卡片用的**载具缩略图**（透明底 PNG，2:1）。
///
/// 为什么要图：车库六张卡片全是文字 + 两根细条，看着像表格而不是游戏界面。
/// 加一张车图就能立刻把观感拉起来，而且不用碰玩法代码。
///
/// 做法：不手搓模型 —— 直接复用 <see cref="VehicleModelBinder.Attach"/>（和游戏里一模一样的那套
/// 车体/炮塔/履带装配 + 材质替换），把它挂在一个临时根节点上，挪到远处（y = -3000，附近什么都没有），
/// 用一台临时相机打两盏灯渲到 RenderTexture，读像素存 PNG。
///
/// 用法：菜单 `Tools → 铁皮黎明 → 生成载具缩略图`，或从命令行调 <see cref="Generate"/>。
/// 输出：`Assets/Resources/UiThumbs/&lt;VehicleDef 资产名&gt;.png`
/// 运行时：`Resources.Load&lt;Texture2D&gt;("UiThumbs/" + def.name)`
/// </summary>
public static class UiThumbGen
{
    // 宽幅比例：车库卡片顶部的图带是 398×120（约 3.3:1），按这个比例出图，
    // ScaleToFit 进去才能填满宽度，车才够大（2:1 的话左右会空掉一半）。
    const int W = 1000;
    const int H = 300;
    const string OutDir = "Assets/Resources/UiThumbs";

    // 相机方向（相对车体，归一化后使用）。先渲一张看是不是"前 3/4"，不对就改这里。
    static readonly Vector3 CamDir = new Vector3(-0.95f, 0.46f, 1.05f);

    [MenuItem("Tools/铁皮黎明/生成载具缩略图")]
    public static void GenerateMenu() => Generate();

    public static void Generate()
    {
        Directory.CreateDirectory(OutDir);

        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:VehicleDef", new[] { "Assets/Data/Vehicles" }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(System.StringComparer.Ordinal);

        // 灯光/环境光会临时改，先存下来
        var oldAmbient = RenderSettings.ambientLight;
        var oldSkybox = RenderSettings.skybox;
        var oldFog = RenderSettings.fog;

        var trash = new List<GameObject>();
        int ok = 0, fail = 0;

        try
        {
            RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.36f);
            RenderSettings.fog = false;

            // 主光 + 补光（都跟着相机，保证每张图的光位一致）。
            // 强度第一版给到 1.25 / 0.55 + 环境光 0.42，渲出来车体几乎全白、细节全丢，这里压下来。
            var keyLight = MakeLight("ThumbKey", new Vector3(38f, 145f, 0f), 0.92f, new Color(1f, 0.95f, 0.88f));
            var fillLight = MakeLight("ThumbFill", new Vector3(14f, -55f, 0f), 0.34f, new Color(0.70f, 0.80f, 1f));
            trash.Add(keyLight);
            trash.Add(fillLight);

            var camGo = new GameObject("ThumbCam");
            trash.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 透明底：卡片底色透出来，像"浮"在面板上
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 4000f;
            cam.allowHDR = false;
            cam.allowMSAA = true;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);

            foreach (var path in paths)
            {
                var def = AssetDatabase.LoadAssetAtPath<VehicleDef>(path);
                if (def == null || def.modelPrefab == null) { fail++; continue; }

                GameObject root = null;
                try
                {
                    root = new GameObject("Thumb_" + def.name);
                    root.transform.position = new Vector3(0f, -3000f, 0f);
                    root.transform.rotation = Quaternion.identity;
                    trash.Add(root);

                    // 和游戏里完全同一套装配（车体 + 炮塔 + 炮管 + 履带 + 材质）
                    if (!VehicleModelBinder.Attach(root, def, out _, out _)) { fail++; continue; }

                    if (!TryFrame(root, cam, out var center, out var dist, out var info))
                    {
                        // 没有可见网格（例如模型还没导入）——跳过，运行时卡片会退回文字占位
                        fail++;
                        continue;
                    }

                    cam.transform.position = center + CamDir.normalized * dist;
                    cam.transform.LookAt(center);
                    cam.targetTexture = rt;
                    cam.Render();

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    cam.targetTexture = null;

                    File.WriteAllBytes(Path.Combine(OutDir, def.name + ".png"), tex.EncodeToPNG());
                    ok++;
                    Debug.Log($"[UiThumbGen] {def.name} ({def.displayName}) 已生成  {info}");
                }
                catch (System.Exception e)
                {
                    fail++;
                    Debug.LogWarning($"[UiThumbGen] {def.name} 失败：{e.Message}");
                }
                finally
                {
                    if (root != null) { trash.Remove(root); Object.DestroyImmediate(root); }
                }
            }

            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        finally
        {
            foreach (var go in trash) if (go != null) Object.DestroyImmediate(go);
            RenderSettings.ambientLight = oldAmbient;
            RenderSettings.skybox = oldSkybox;
            RenderSettings.fog = oldFog;
        }

        AssetDatabase.Refresh();
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            TuneImporter(Path.Combine(OutDir, name + ".png").Replace('\\', '/'));
        }
        AssetDatabase.Refresh();

        Debug.Log($"[UiThumbGen] 完成：成功 {ok}，失败 {fail}，输出目录 {OutDir}");
    }

    static GameObject MakeLight(string name, Vector3 euler, float intensity, Color color)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(0f, -3000f, 0f);
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.None;     // 缩略图不要阴影，透明底上出现投影很脏
        return go;
    }

    /// <summary>
    /// 把相机框到模型上，并按**真实顶点**（不是包围盒 8 个角）精确算距离。
    ///
    /// 为什么不能用包围盒角：坦克车体是楔形，包围盒的角是空气。按角点取景的话，
    /// 约束永远由那些"空角"决定（实测 land_main 的 bottom-far-left 角要求 d=16.7，
    /// 而车体实际只占画面的 57%）。改成把所有顶点投到相机空间、取真正的 |x|/|y| 极值。
    ///
    /// 只认 MeshRenderer + MeshFilter（粒子/拖尾/线条的 bounds 经常离模型很远，
    /// 混进来会把中心带歪）；顶点数多时按步长采样，取景不需要每个点都算。
    /// </summary>
    static bool TryFrame(GameObject root, Camera cam, out Vector3 center, out float dist, out string info)
    {
        center = Vector3.zero;
        dist = 10f;
        info = "";

        var meshes = new List<(Mesh mesh, Matrix4x4 m)>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!(r is MeshRenderer)) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            meshes.Add((mf.sharedMesh, mf.transform.localToWorldMatrix));
        }
        if (meshes.Count == 0) return false;

        // ---- 第一遍：用顶点算世界空间包围盒（求中心）----
        var bb = new Bounds();
        bool any = false;
        foreach (var (mesh, mat) in meshes)
        {
            var verts = mesh.vertices;
            int step = Mathf.Max(1, verts.Length / 3000);
            for (int i = 0; i < verts.Length; i += step)
            {
                var w = mat.MultiplyPoint3x4(verts[i]);
                if (!any) { bb = new Bounds(w, Vector3.zero); any = true; }
                else bb.Encapsulate(w);
            }
        }
        if (!any) return false;
        center = bb.center;

        // ---- 第二遍：投到相机空间，求真正需要的距离 ----
        var rot = Quaternion.LookRotation(-CamDir.normalized, Vector3.up);
        var rotInv = Quaternion.Inverse(rot);
        float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * Mathf.Max(0.1f, (float)W / H);

        float need = 0f;

        // 迭代两次：
        //   第 1 次只求"需要多远"，同时记下投影轮廓在相机空间的 x/y 范围；
        //   第 2 次先把中心挪到轮廓中心，再重算距离。
        // 只算一次的话，3/4 视角下投影轮廓的中心和世界包围盒中心不重合，车会偏在一边
        // （实测炮艇左边只剩 20px、右边空了 190px）。
        for (int pass = 0; pass < 2; pass++)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            need = 0f;

            foreach (var (mesh, mat) in meshes)
            {
                var verts = mesh.vertices;
                int step = Mathf.Max(1, verts.Length / 4000);
                for (int i = 0; i < verts.Length; i += step)
                {
                    var l = rotInv * (mat.MultiplyPoint3x4(verts[i]) - center);
                    if (l.x < minX) minX = l.x;
                    if (l.x > maxX) maxX = l.x;
                    if (l.y < minY) minY = l.y;
                    if (l.y > maxY) maxY = l.y;
                    // 屏幕约束 |x| / (d + z) <= tanH  →  d >= |x|/tanH - z
                    need = Mathf.Max(need, Mathf.Abs(l.x) / tanH - l.z);
                    need = Mathf.Max(need, Mathf.Abs(l.y) / tanV - l.z);
                }
            }

            if (pass == 0)
            {
                // 把注视点平移到轮廓中心（相机空间 -> 世界空间）
                center += rot * new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            }
        }

        dist = Mathf.Max(1f, need) * 1.05f;              // 留 5% 边
        info = $"size=({bb.size.x:F1},{bb.size.y:F1},{bb.size.z:F1}) dist={dist:F1}";
        return true;
    }

    /// <summary>缩略图导入设置：不压缩、不生成 mipmap、按透明处理，边缘才干净。</summary>
    static void TuneImporter(string assetPath)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        imp.textureType = TextureImporterType.Default;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = 1024;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }
}
