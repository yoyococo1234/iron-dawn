using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>一键打包：Windows 独立版 / WebGL 版。</summary>
public static class GameBuilder
{
    const string Scene = "Assets/Scenes/Map_Battlefield.unity";

    [MenuItem("Tools/铁皮黎明/④ 打包 Windows 版", false, 50)]
    public static void BuildWindows()
    {
        string outDir = "D:/Builds/Tank3D_Win";
        Directory.CreateDirectory(outDir);

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = $"{outDir}/Tank3D.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[GameBuilder] Windows 打包结果 {s.result}，大小 {s.totalSize / 1048576f:F0} MB，用时 {s.totalTime.TotalSeconds:F0} 秒，错误 {s.totalErrors}");
    }

    [MenuItem("Tools/铁皮黎明/④ 打包 WebGL 版（低配）", false, 51)]
    public static void BuildWebGL()
    {
        string outDir = "D:/Builds/Tank3D_WebGL";
        Directory.CreateDirectory(outDir);

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[GameBuilder] WebGL 打包结果 {s.result}，大小 {s.totalSize / 1048576f:F0} MB，用时 {s.totalTime.TotalSeconds:F0} 秒，错误 {s.totalErrors}");
    }
}
