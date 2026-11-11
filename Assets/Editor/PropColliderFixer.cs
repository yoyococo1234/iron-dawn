using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 细节物件碰撞修复（2026-11-11）。
///
/// 用户反馈："表面建模和实际碰撞区域完全不相干，明明是空的却走不过去，整个地图都有这种问题。"
///
/// 根因两条：
///   1. **建筑**用的是 `PropScatter.AddSolidCollider` —— 把整栋楼的所有网格包围盒合成**一个实心 BoxCollider**。
///      楼体的窗户凹陷、镂空、雨棚下面、楼与楼之间的缝，全被这个盒子填实了 → 看不见的墙。
///   2. **树**的碰撞柱半径按**树冠**算（`footRadius*0.30`，最大 1.4 m），树干其实只有 0.3 m 粗
///      → 树干周围 2.8 m 见方全是空气墙。地图上树又最多，所以"整个地图都有问题"。
///
/// 修法：
///   - 建筑：给**每个 MeshFilter 加一个 MeshCollider**（碰撞 = 渲染），原来那个 BoxCollider
///     **保留但改成 isTrigger** —— `NavGrid.MarkBuildings()` 还要读它的 bounds 标障碍格，删了 AI 会撞楼。
///     trigger 不参与物理碰撞，所以车和炮弹只跟真网格碰撞。
///   - 树：碰撞柱按树高收细到树干粗细（0.30~0.75 m 半径）。
///
/// 另外把这些模型的 Read/Write 打开（MeshCollider 在打包后需要可读网格）。
/// </summary>
public static class PropColliderFixer
{
    [MenuItem("Tools/铁皮黎明/④b 修细节物件碰撞（网格对网格）", false, 45)]
    public static void FixAll() => FixAll(false);

    [MenuItem("Tools/铁皮黎明/④c 物件网格置为可读（打包需要）", false, 46)]
    public static void FixReadable() => FixAll(true);

    public static void FixAll(bool alsoMakeReadable)
    {
        var props = GameObject.Find("Props");
        if (props == null) { Debug.LogError("[PropColliderFixer] 场景里找不到 Props"); return; }

        int buildingRoots = 0, meshColliders = 0, trunks = 0;
        var touchedMeshes = new HashSet<Mesh>();

        // ---------- 1. 建筑：BoxCollider → trigger + 每网格一个 MeshCollider ----------
        foreach (var bc in props.GetComponentsInChildren<BoxCollider>(true))
        {
            if (bc == null || bc.isTrigger) continue;

            var root = bc.gameObject;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) continue;          // 树干碰撞柱没有网格，留给第 2 步

            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                touchedMeshes.Add(mf.sharedMesh);
                if (mf.GetComponent<MeshCollider>() != null) continue;

                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                meshColliders++;
            }

            bc.isTrigger = true;    // 只留给 NavGrid 算障碍格，不参与物理
            buildingRoots++;
        }

        // ---------- 2. 树干：按树高收细 ----------
        foreach (var t in props.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.name != "TreeCollider") continue;
            var bc = t.GetComponent<BoxCollider>();
            if (bc == null) continue;

            float h = bc.size.y;
            float r = Mathf.Clamp(h * 0.05f, 0.30f, 0.75f);
            bc.size = new Vector3(r * 2f, h, r * 2f);
            bc.center = new Vector3(0f, h * 0.5f, 0f);
            trunks++;
        }

        // ---------- 3. 这些网格要可读（打包后 MeshCollider 需要） ----------
        // 单独一个菜单项做（462 个模型重新导入很慢），这里只在需要时开。
        int readable = 0;
        if (alsoMakeReadable) readable = MakeReadable(touchedMeshes);

        EditorUtility.SetDirty(props);          // 标记场景已改，Ctrl+S 才会存下新加的碰撞体
        Debug.Log($"[PropColliderFixer] 建筑 {buildingRoots} 栋 → MeshCollider {meshColliders} 个；" +
                  $"树干收细 {trunks} 棵；网格置为可读 {readable} 个。**记得 Ctrl+S 存场景**");
    }

    /// <summary>把用到网格的模型资源改成 Read/Write 可用（只动 Assets/Models/Props 下的）。</summary>
    static int MakeReadable(HashSet<Mesh> meshes)
    {
        // 第二次跑的时候建筑盒已经是 trigger，上面那个循环会跳过它们 → 这里补一次全量扫描
        if (meshes.Count == 0)
        {
            var root = GameObject.Find("Props");
            if (root != null)
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    if (mc != null && mc.sharedMesh != null) meshes.Add(mc.sharedMesh);
        }

        var paths = new HashSet<string>();
        foreach (var m in meshes)
        {
            if (m == null) continue;
            string p = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(p)) continue;
            if (!p.StartsWith("Assets/Models/Props")) continue;
            paths.Add(p);
        }

        int n = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var p in paths)
            {
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null || imp.isReadable) continue;
                imp.isReadable = true;
                imp.SaveAndReimport();
                n++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
        return n;
    }
}
// (2026-11-11) 强制重编译以清掉控制台里那条过期的 CS0103 缓存
