using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>
/// 命令行打包入口（2026-11-12 加）。
///
/// 为什么需要它：MCP 编辑器插件在长时间会话里会反复掉线，`script-execute` 调不动。
/// 这条路不依赖编辑器连接 —— 关掉 GUI 编辑器后直接：
///
///   set TANK3D_BUILD_DIR=D:\Builds\Tank3D_Win_20261112g
///   Unity.exe -batchmode -quit -projectPath D:\UnityProjects\Tank3D ^
///             -executeMethod BuildCli.BuildWindows -logFile D:\Builds\build.log
///
/// 输出目录从环境变量 `TANK3D_BUILD_DIR` 取，没设就用 GameBuilder 的默认目录。
/// </summary>
public static class BuildCli
{
    const string Scene = "Assets/Scenes/Map_Battlefield.unity";

    public static void BuildWindows()
    {
        string outDir = System.Environment.GetEnvironmentVariable("TANK3D_BUILD_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = "D:/Builds/Tank3D_Win";
        Directory.CreateDirectory(outDir);

        // 先打印一份"海平面 / 水域"体检，方便确认抬升效果（读日志即可）
        LogWaterStats();

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = outDir + "/Tank3D.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        string msg = $"[BuildCli] {s.result} 大小={s.totalSize / 1048576f:F0}MB 用时={s.totalTime.TotalSeconds:F0}s 错误={s.totalErrors} 输出={outDir}";
        Debug.Log(msg);

        // batchmode 的退出码：失败时返回非 0，方便外面判断
        if (Application.isBatchMode)
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>海平面体检：水域占比、海岸剖面、船出生深度（只读，写日志）。</summary>
    public static void LogWaterStats()
    {
        // batchmode 下要自己把场景打开
        var cur = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(cur.path))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);

        float wl = BattlefieldTerrain.WaterLevel;
        int N = 120, wet = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float x = -1000f + (i + 0.5f) * (2000f / N);
                float z = -1000f + (j + 0.5f) * (2000f / N);
                if (BattlefieldTerrain.SampleHeight(x, z) < wl) wet++;
            }
        Debug.Log($"[Water] 海平面={wl:F2}  水域占比={wet / (float)(N * N) * 100f:F1}%");

        // 港口附近：水面到岸线多远、岸上目标比海平面高多少
        float shoreX = -1000f;
        for (float x = -1000f; x <= -200f; x += 5f)
            if (BattlefieldTerrain.SampleHeight(x, 280f) >= wl) { shoreX = x; break; }
        float inlandH = BattlefieldTerrain.SampleHeight(shoreX + 120f, 280f);
        Debug.Log($"[Water] 港口岸线 x≈{shoreX:F0}（z=280）；岸内 120 m 处地形高度={inlandH:F2}（比海平面高 {inlandH - wl:F1} m）");

        // 船出生点：沿 z=280 找第一个够 3 m 深的点
        for (float x = -1000f; x <= 0f; x += 10f)
        {
            float d = BattlefieldTerrain.WaterDepth(x, 280f);
            if (d >= 3f) { Debug.Log($"[Water] 首个 ≥3 m 深水域 x≈{x:F0}（距岸线约 {shoreX - x:F0} m）"); break; }
        }
    }
}
