using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// 战场生成器：读 Config → 程序化生成整个场景 → 存成 Assets/Scenes/Main.unity。
/// 改完参数后跑一次 Tools/铁皮黎明/生成战场 即可重建。
/// </summary>
public static class ArenaBuilder
{
    [MenuItem("Tools/铁皮黎明/生成战场")]
    public static void Build()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Materials");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 材质 ----------
        var matGround = MakeMat("M_Ground", new Color(0.22f, 0.26f, 0.22f), 0f);
        var matWater = MakeMat("M_Water", new Color(0.08f, 0.30f, 0.45f), 0.85f);
        var matBrick = MakeMat("M_Brick", new Color(0.55f, 0.30f, 0.15f), 0.1f);
        var matSteel = MakeMat("M_Steel", new Color(0.45f, 0.48f, 0.52f), 0.85f);
        var matHull = MakeMat("M_Hull", new Color(0.30f, 0.40f, 0.22f), 0.3f);
        var matTurret = MakeMat("M_Turret", new Color(0.36f, 0.46f, 0.26f), 0.35f);
        var matBase = MakeMat("M_Base", new Color(0.95f, 0.75f, 0.15f), 0.6f);

        // ---------- 地面 ----------
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(Config.ArenaSize / 10f, 1f, Config.ArenaSize / 10f);
        ground.transform.position = new Vector3(0f, Config.GroundY, 0f);
        ground.GetComponent<Renderer>().sharedMaterial = matGround;
        ground.isStatic = true;

        // ---------- 水域 ----------
        var w = Config.WaterRect;
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Water";
        water.transform.localScale = new Vector3(w.width / 10f, 1f, w.height / 10f);
        water.transform.position = new Vector3(w.x + w.width / 2f, Config.WaterY, w.y + w.height / 2f);
        water.GetComponent<Renderer>().sharedMaterial = matWater;
        water.isStatic = true;

        // ---------- 基地 ----------
        var baseRoot = new GameObject("Base");
        baseRoot.transform.position = new Vector3(0f, 0f, -Config.ArenaSize / 2f + 8f);

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "BasePad";
        pad.transform.SetParent(baseRoot.transform, false);
        pad.transform.localScale = new Vector3(5f, 0.4f, 5f);
        pad.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        pad.GetComponent<Renderer>().sharedMaterial = matSteel;

        var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
        core.name = "BaseCore";
        core.transform.SetParent(baseRoot.transform, false);
        core.transform.localScale = new Vector3(2.4f, 3.2f, 2.4f);
        core.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        core.GetComponent<Renderer>().sharedMaterial = matBase;

        // ---------- 墙体（带随机种子，可复现）----------
        Random.InitState(Config.RandomSeed);
        var wallsRoot = new GameObject("Walls");
        int half = Mathf.RoundToInt(Config.ArenaSize / 2f) - 8;

        for (int c = 0; c < Config.WallClusterCount; c++)
        {
            var center = new Vector3(Random.Range(-half, half), 0f, Random.Range(-half, half));

            // 跳过水域与基地附近
            if (InWater(center)) { c--; continue; }
            if (Vector3.Distance(center, baseRoot.transform.position) < 14f) { c--; continue; }

            bool steelCluster = Random.value < 0.3f;
            int rows = Random.Range(2, 4);
            int cols = Random.Range(2, 4);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (Random.value < 0.15f) continue; // 留缺口，别做成实心块
                    var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    block.name = steelCluster ? "SteelBlock" : "BrickBlock";
                    block.transform.SetParent(wallsRoot.transform, false);
                    block.transform.position = center + new Vector3(j * 4f - (cols - 1) * 2f, 1.5f, i * 4f - (rows - 1) * 2f);
                    block.transform.localScale = new Vector3(3.8f, 3f, 3.8f);
                    block.GetComponent<Renderer>().sharedMaterial = steelCluster ? matSteel : matBrick;
                    block.isStatic = true;
                }
            }
        }

        // ---------- 玩家坦克 ----------
        var tank = BuildTank(matHull, matTurret, matSteel);
        tank.transform.position = new Vector3(0f, 0f, -Config.ArenaSize / 2f + 18f);

        // ---------- 相机 ----------
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = tank.transform.position + Config.CameraOffset;
        camGo.transform.LookAt(tank.transform.position + Vector3.up * 1.5f);
        var follow = camGo.AddComponent<CameraFollow>();
        follow.target = tank.transform;

        // ---------- 灯光 ----------
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.42f);

        // ---------- 保存 ----------
        var path = "Assets/Scenes/Main.unity";
        EditorSceneManager.SaveScene(scene, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ArenaBuilder] 场景已生成：{path}（墙簇 {Config.WallClusterCount}，战场 {Config.ArenaSize}m）");
    }

    // ---------------- 辅助 ----------------

    static bool InWater(Vector3 p)
    {
        var w = Config.WaterRect;
        return p.x >= w.x && p.x <= w.x + w.width && p.z >= w.y && p.z <= w.y + w.height;
    }

    static Material MakeMat(string name, Color color, float metallic)
    {
        var path = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.color = color;
            existing.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", 0.35f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            var parent = Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>用基础几何体拼一台坦克：车体 + 履带 + 炮塔 + 炮管 + 炮口。</summary>
    static GameObject BuildTank(Material matHull, Material matTurret, Material matTrack)
    {
        var root = new GameObject("PlayerTank");
        var ctrl = root.AddComponent<TankController>();

        var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hull.name = "Hull";
        hull.transform.SetParent(root.transform, false);
        hull.transform.localScale = new Vector3(2.4f, 1.0f, 3.8f);
        hull.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        hull.GetComponent<Renderer>().sharedMaterial = matHull;

        for (int side = -1; side <= 1; side += 2)
        {
            var track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = side < 0 ? "TrackL" : "TrackR";
            track.transform.SetParent(root.transform, false);
            track.transform.localScale = new Vector3(0.6f, 0.9f, 4.0f);
            track.transform.localPosition = new Vector3(side * 1.35f, 0.45f, 0f);
            track.GetComponent<Renderer>().sharedMaterial = matTrack;
        }

        var turretPivot = new GameObject("TurretPivot");
        turretPivot.transform.SetParent(root.transform, false);
        turretPivot.transform.localPosition = new Vector3(0f, 1.3f, 0f);

        var turret = GameObject.CreatePrimitive(PrimitiveType.Cube);
        turret.name = "Turret";
        turret.transform.SetParent(turretPivot.transform, false);
        turret.transform.localScale = new Vector3(1.8f, 0.8f, 2.0f);
        turret.GetComponent<Renderer>().sharedMaterial = matTurret;

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrel.name = "Barrel";
        barrel.transform.SetParent(turretPivot.transform, false);
        barrel.transform.localScale = new Vector3(0.3f, 0.3f, 3.0f);
        barrel.transform.localPosition = new Vector3(0f, 0.05f, 2.2f);
        barrel.GetComponent<Renderer>().sharedMaterial = matTrack;

        var tip = new GameObject("BarrelTip");
        tip.transform.SetParent(turretPivot.transform, false);
        tip.transform.localPosition = new Vector3(0f, 0.05f, 3.8f);

        ctrl.turret = turretPivot.transform;
        ctrl.barrelTip = tip.transform;

        return root;
    }
}
