using UnityEngine;
using UnityEditor;

/// <summary>
/// 一次性重建 + 打包（给 batchmode 用）。
/// 2026-11-09：Unity 编辑器在普通权限下起不来（许可客户端验签 Code 10），
/// 只能用「管理员权限 + -batchmode -executeMethod」跑，所以把刷新资产 / 重撒物件 / 打包合成一个入口。
///
/// 用法：
///   Unity.exe -batchmode -nographics -projectPath D:\UnityProjects\Tank3D \
///             -executeMethod OneShotRebuild.Run -logFile &lt;log&gt;
/// </summary>
public static class OneShotRebuild
{
    public static void Run()
    {
        Debug.Log("[OneShotRebuild] 1/3 刷新资产（贴图 maxSize 512 会在这里重新导入）");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        Debug.Log("[OneShotRebuild] 2/3 重新撒细节物件（新树/新楼 + 防重叠 + 关阴影）");
        PropScatter.Scatter();

        Debug.Log("[OneShotRebuild] 3/3 打包 Windows 版");
        GameBuilder.BuildWindows();

        Debug.Log("[OneShotRebuild] 全部完成");
    }

    /// <summary>只重撒物件，不打包（调试用）。</summary>
    public static void Rescatter()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        PropScatter.Scatter();
        Debug.Log("[OneShotRebuild] Rescatter 完成");
    }

    /// <summary>
    /// 地形高度/地表规则改过之后用：重建 4 块地形 → 重撒物件 → 打包。
    /// 注意**不要**调 BattlefieldTerrainBuilder.BuildAll()：它内部会 NewScene 把整个场景清空
    ///（Match / 据点 / Props 全没了）。只逐块 BuildTile 是安全的。
    /// </summary>
    public static void RunTerrain()
    {
        Debug.Log("[OneShotRebuild] 1/4 刷新资产");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        for (int t = 0; t < 4; t++)
        {
            Debug.Log($"[OneShotRebuild] 2/4 重建地形 tile {t}");
            var msg = BattlefieldTerrainBuilder.BuildTile(t);
            Debug.Log("[OneShotRebuild]   " + msg);
        }

        Debug.Log("[OneShotRebuild] 3/4 重撒细节物件");
        BattlefieldTerrain.Invalidate();
        PropScatter.Scatter();

        Debug.Log("[OneShotRebuild] 4/4 打包 Windows 版");
        GameBuilder.BuildWindows();

        Debug.Log("[OneShotRebuild] 全部完成");
    }
}
