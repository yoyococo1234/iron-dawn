using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 地形形状预览器：直接采样 BattlefieldTerrainBuilder.HeightWorld，输出一张晕渲图（PNG）。
/// 用途：调山形/坡度时秒出结果，不用每次重建 4 块 4097 地形（那要 6 分钟）。
/// 菜单：Tools/铁皮黎明/地形预览
/// </summary>
public static class TerrainPreview
{
    [MenuItem("Tools/铁皮黎明/地形预览（晕渲图，秒出）", false, 30)]
    public static void Preview()
    {
        Render("D:/UnityProjects/Tank3D/work/shots/preview_shaded.png", 1024);
    }

    public static string Render(string path, int size, float centerX = 0f, float centerZ = 0f, float extent = 0f)
    {
        if (extent <= 0f) extent = BattlefieldTerrainBuilder.MapSize;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var px = new Color[size * size];
        float step = extent / (size - 1);
        float sunAz = -35f * Mathf.Deg2Rad, sunEl = 48f * Mathf.Deg2Rad;

        float minH = 9999f, maxH = -9999f;

        for (int j = 0; j < size; j++)
        {
            float z = centerZ - extent * 0.5f + j * step;
            for (int i = 0; i < size; i++)
            {
                float x = centerX - extent * 0.5f + i * step;

                float h = BattlefieldTerrainBuilder.HeightWorld(x, z);
                float hx = BattlefieldTerrainBuilder.HeightWorld(x + step, z) - BattlefieldTerrainBuilder.HeightWorld(x - step, z);
                float hz = BattlefieldTerrainBuilder.HeightWorld(x, z + step) - BattlefieldTerrainBuilder.HeightWorld(x, z - step);
                Vector3 n = new Vector3(-hx / (2f * step), 1f, -hz / (2f * step)).normalized;

                Vector3 sun = new Vector3(Mathf.Cos(sunEl) * Mathf.Sin(sunAz), Mathf.Sin(sunEl), Mathf.Cos(sunEl) * Mathf.Cos(sunAz)).normalized;
                float lam = Mathf.Clamp01(Vector3.Dot(n, sun));

                float slope = Mathf.Atan(Mathf.Sqrt(n.x * n.x + n.z * n.z) / Mathf.Max(1e-4f, n.y)) * Mathf.Rad2Deg;

                // 高度着色：海底深蓝 → 沙 → 草 → 岩 → 雪
                Color c;
                if (h < BattlefieldTerrainBuilder.WaterLevel) c = Color.Lerp(new Color(0.05f, 0.16f, 0.28f), new Color(0.12f, 0.35f, 0.45f), Mathf.InverseLerp(-100f, BattlefieldTerrainBuilder.WaterLevel, h));
                else if (h < 30f) c = Color.Lerp(new Color(0.76f, 0.70f, 0.48f), new Color(0.42f, 0.52f, 0.26f), Mathf.InverseLerp(0f, 30f, h));
                else if (h < 150f) c = Color.Lerp(new Color(0.42f, 0.52f, 0.26f), new Color(0.46f, 0.42f, 0.34f), Mathf.InverseLerp(30f, 150f, h));
                else c = Color.Lerp(new Color(0.46f, 0.42f, 0.34f), new Color(0.92f, 0.93f, 0.95f), Mathf.InverseLerp(150f, 270f, h));

                if (slope > 32f) c = Color.Lerp(c, new Color(0.35f, 0.30f, 0.26f), 0.65f);   // 陡坡标成岩色

                float shade = 0.30f + 0.85f * lam;
                px[j * size + i] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);

                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        var png = tex.EncodeToPNG();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, png);
        Object.DestroyImmediate(tex);

        return $"OK {path} | 高度 {minH:F1}~{maxH:F1} m";
    }

    /// <summary>坡度分布统计：哪部分地形各档载具能开。</summary>
    [MenuItem("Tools/铁皮黎明/地形坡度统计", false, 31)]
    public static void Stats()
    {
        int n1 = 0, n2 = 0, n3 = 0, wet = 0, total = 0;
        float step = 5f;
        for (float z = -1000f; z <= 1000f; z += step)
            for (float x = -1000f; x <= 1000f; x += step)
            {
                float h = BattlefieldTerrainBuilder.HeightWorld(x, z);
                total++;
                if (h < BattlefieldTerrainBuilder.WaterLevel) { wet++; continue; }
                float hx = BattlefieldTerrainBuilder.HeightWorld(x + 2f, z) - BattlefieldTerrainBuilder.HeightWorld(x - 2f, z);
                float hz = BattlefieldTerrainBuilder.HeightWorld(x, z + 2f) - BattlefieldTerrainBuilder.HeightWorld(x, z - 2f);
                float slope = Mathf.Atan(Mathf.Sqrt(hx * hx + hz * hz) / 4f) * Mathf.Rad2Deg;
                if (slope < 22.3f) n1++; else if (slope < 30f) n2++; else n3++;
            }
        Debug.Log($"[地形坡度] 水域 {100f * wet / total:F1}% | <22.3°(全车可走) {100f * n1 / total:F1}% | 22.3~30°(轻/中坦) {100f * n2 / total:F1}% | >30°(仅轻坦) {100f * n3 / total:F1}%");
    }
}
