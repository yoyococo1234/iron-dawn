using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 开发用截图工具：在非 Play 模式下也能把任意相机渲染成 PNG。
/// 用途：AI 协作者（或人）改完场景后，用它把画面落盘，再读图验证。
/// 放在 Assets/Editor 下，所以能引用 UnityEditor 命名空间。
/// </summary>
public static class DevCapture
{
    /// <summary>
    /// 渲染指定相机到 PNG 文件。
    /// </summary>
    /// <param name="path">输出绝对路径，例如 "D:/tmp/unity_view.png"</param>
    /// <param name="width">宽</param>
    /// <param name="height">高</param>
    /// <param name="cameraName">相机物体名；留空则用 Camera.main，再退化为场景第一个相机</param>
    public static string Shot(string path = "D:/tmp/unity_view.png", int width = 1280, int height = 720, string cameraName = "")
    {
        Camera cam = null;

        if (!string.IsNullOrEmpty(cameraName))
        {
            var go = GameObject.Find(cameraName);
            if (go != null) cam = go.GetComponent<Camera>();
        }
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            if (cams.Length > 0) cam = cams[0];
        }
        if (cam == null) return "ERROR: 场景里没有相机";

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var rt = new RenderTexture(width, height, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            var png = tex.EncodeToPNG();
            File.WriteAllBytes(path, png);
            Object.DestroyImmediate(tex);

            return $"OK {png.Length} bytes -> {path}  (camera: {cam.name}, {width}x{height})";
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    /// <summary>
    /// 把相机对准某个物体并保持一段距离，常用于"看看这个物体长什么样"。
    /// </summary>
    public static string LookAt(string targetName, float distance = 8f, float height = 4f, float angleY = 0f)
    {
        var target = GameObject.Find(targetName);
        if (target == null) return $"ERROR: 找不到物体 {targetName}";

        var cam = Camera.main;
        if (cam == null)
        {
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            if (cams.Length == 0) return "ERROR: 场景里没有相机";
            cam = cams[0];
        }

        var center = target.transform.position;
        var rot = Quaternion.Euler(0, angleY, 0) * Vector3.back;
        var pos = center + rot * distance + Vector3.up * height;

        cam.transform.position = pos;
        cam.transform.LookAt(center);
        return $"相机已对准 {targetName}";
    }
}
