using UnityEngine;
using System.IO;
using System.Text;

/// <summary>
/// 性能探针：每秒统计帧率，写到 perf.log；带 -benchmark 参数时跑满 N 秒自动退出。
/// 用来在真实 exe 里量性能，而不是靠编辑器里的数字（编辑器有 Scene 视图额外开销）。
/// </summary>
public class PerfProbe : MonoBehaviour
{
    [Tooltip("统计窗口（秒）")]
    public float reportEvery = 1f;
    [Tooltip("带 -benchmark 启动时，跑多少秒后退出")]
    public float benchmarkSeconds = 25f;

    float _acc;
    int _frames;
    float _elapsed;
    bool _benchmark;
    StringBuilder _log = new StringBuilder();

    void Start()
    {
        _benchmark = System.Environment.GetCommandLineArgs().Length > 0 &&
                     System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") >= 0;

        _log.AppendLine($"# PerfProbe {SystemInfo.processorType} / {SystemInfo.graphicsDeviceName}");
        _log.AppendLine($"# screen {Screen.width}x{Screen.height} quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");

        // 屏幕上也能看到
        Application.runInBackground = true;
    }

    void Update()
    {
        _acc += Time.unscaledDeltaTime;
        _frames++;
        _elapsed += Time.unscaledDeltaTime;

        if (_acc >= reportEvery)
        {
            float fps = _frames / _acc;
            _log.AppendLine($"{_elapsed:F1}s\t{fps:F1} FPS\t{_acc / _frames * 1000f:F1} ms");
            _acc = 0f;
            _frames = 0;
        }

        if (_benchmark && _elapsed >= benchmarkSeconds)
        {
            _log.AppendLine("# done");
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "perf.log"), _log.ToString());
            Application.Quit();
        }
    }

    void OnGUI()
    {
        float fps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        GUI.Label(new Rect(10, Screen.height - 30, 300, 24), $"FPS {fps:F0}");
    }
}
