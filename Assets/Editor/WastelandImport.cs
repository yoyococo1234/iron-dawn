using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// 废土建筑模型的导入设置（2026-11-11）。
///
/// Lux3D 出的 glb 经 Blender 转成 fbx 后，导入设置必须和原来那批 Lux3D 模型一致，
/// 否则缩放/朝向/材质会对不上（原来的设置：globalScale=1、useFileScale=1、isReadable=1、
/// materialImportMode=2、animationType=None）。
///
/// 菜单：Tools → 铁皮黎明 → ④d 废土建筑导入设置
/// </summary>
public static class WastelandImport
{
    const string Dir = "Assets/Models/Props/Lux3D";

    [MenuItem("Tools/铁皮黎明/④d 废土建筑导入设置", false, 47)]
    public static void Apply()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(Dir, "waste_*.fbx"))
        {
            string p = path.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null) { Debug.LogWarning("[WastelandImport] 不是模型：" + p); continue; }

            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.isReadable = true;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.addCollider = false;
            imp.SaveAndReimport();
            n++;
            Debug.Log("[WastelandImport] 已设置 " + p);
        }
        AssetDatabase.Refresh();

        // ---- 贴图：glb 抽出来的 BaseColor 是 4K 原图（5~6 MB 一张），不压会让包涨 50 MB+ ----
        int tex = 0;
        foreach (var dir in Directory.GetDirectories("Assets/Models/Props/Lux3D/Textures"))
        {
            if (!Path.GetFileName(dir).StartsWith("waste_")) continue;
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                string p = f.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 1024;                 // 1024 够用：楼在 2 km 图上不会贴脸看
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                // Unity 6 已移除 crunchCompression / crunchCompressionQuality，别再写（会 CS1061）
                ti.SaveAndReimport();
                tex++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[WastelandImport] 处理 {n} 个废土建筑模型、{tex} 张贴图");
    }
}
