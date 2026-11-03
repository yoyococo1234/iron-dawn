using UnityEngine;

/// <summary>
/// WebGL 性能兜底。
///
/// 浏览器里跑的是同一套桌面画质（阴影 + 泛光 + 全分辨率贴图 + 地形细节），
/// 实测很卡。这里在启动时按平台把最贵的几项压下去 —— 只在 WebGL 上生效，
/// 桌面版一行都不动。
///
/// 想改回去或调松一点，直接改下面这些数字；想彻底关掉，把 Apply 里的 return 去掉一行注释。
/// </summary>
public static class WebGLPerf
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer) return;

        // 阴影最贵：WebGL 直接关掉（场景是白天顶光，关掉后观感损失可接受）
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.shadowDistance = 50f;
        QualitySettings.shadowCascades = 1;

        QualitySettings.antiAliasing = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        QualitySettings.softParticles = false;
        QualitySettings.pixelLightCount = 1;
        QualitySettings.lodBias = 0.7f;

        // 贴图降一半分辨率：显存和带宽都省一大截，远景几乎看不出
        QualitySettings.globalTextureMipmapLimit = 1;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void TuneScene()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer) return;

        // 泛光少迭代一次
        var fx = Object.FindFirstObjectByType<PostFX>();
        if (fx != null)
        {
            fx.blurIterations = 1;
            fx.bloomIntensity *= 0.6f;
            fx.vignette *= 0.7f;
        }

        // 地形细节（草/树）砍一半以上 —— 这是 WebGL 上第二贵的东西
        var t = Terrain.activeTerrain;
        if (t != null)
        {
            t.detailObjectDensity = Mathf.Min(t.detailObjectDensity, 0.35f);
            t.detailObjectDistance *= 0.55f;
            t.treeDistance *= 0.7f;
            t.treeBillboardDistance *= 0.7f;
            t.heightmapPixelError = Mathf.Max(t.heightmapPixelError, 8f);
        }
    }
}
