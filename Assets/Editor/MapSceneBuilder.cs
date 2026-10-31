using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 正式地图（Map_Battlefield.unity）的载具测试场景：把 5 台车放到 2000×2000 图上，
/// 用 Unity Terrain 采样（VehiclePhysics.terrain 留空），验证贴地/爬坡/涉水/飞行。
///
/// 菜单：Tools/铁皮黎明/② 在正式地图上放载具（测试用）
/// </summary>
public static class MapSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Map_Battlefield.unity";
    const string RootName = "Test_Vehicles";

    [MenuItem("Tools/铁皮黎明/② 在正式地图上放载具（测试用）", false, 20)]
    public static void Build()
    {
        // 打开正式地图
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BattlefieldTerrain.Invalidate();

        // 清掉上一次放的
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var holder = new GameObject(RootName);

        // 我方出兵线 x = −933，z 从 −967 到 967；按设计坐标摆几台（设计坐标 ×16.667）
        float S = BattlefieldTerrain.Scale;
        // 我方（team 0）
        var player = Spawn(holder, "Vehicle_land_light", new Vector2(-40f, -6f), 90f, S, 0, true);
        Spawn(holder, "Vehicle_land_main", new Vector2(-44f, -12f), 90f, S, 0);
        Spawn(holder, "Vehicle_land_heavy", new Vector2(-44f, 0f), 90f, S, 0);
        Spawn(holder, "Vehicle_sea_gunboat", new Vector2(-10f, 40f), 90f, S, 0);      // 海里
        Spawn(holder, "Vehicle_air_fighter", new Vector2(-20f, -20f), 90f, S, 0);     // 空中

        // 敌方（team 1）：陆海空各一台，用来验证三域 AI
        Spawn(holder, "Vehicle_land_main", new Vector2(-8f, -10f), -90f, S, 1);
        Spawn(holder, "Vehicle_land_heavy", new Vector2(4f, -14f), -90f, S, 1);
        Spawn(holder, "Vehicle_sea_gunboat", new Vector2(12f, 42f), -90f, S, 1);      // 敌方炮艇
        Spawn(holder, "Vehicle_air_fighter", new Vector2(16f, -22f), -90f, S, 1);     // 敌方飞机

        // AI 的目标点
        var objSea = new GameObject("Objective_Sea");
        objSea.transform.position = new Vector3(0f, 0f, 40f * S);          // 设计 (0,40) 海面
        var objCenter = new GameObject("Objective_Center");
        objCenter.transform.position = new Vector3(0f, 0f, 0f);

        // 给 AI 指定巡逻目标：炮艇去海面，其余去地图中心
        foreach (var ai in holder.GetComponentsInChildren<VehicleAI>())
        {
            var def = ai.GetComponent<VehicleCombat>().Def;
            ai.objective = (def != null && def.domain == Domain.Sea) ? objSea.transform : objCenter.transform;
        }

        // 相机：跟玩家车（CameraFollow）
        var camGo = GameObject.Find("Main Camera");
        if (camGo == null) camGo = new GameObject("Main Camera");
        var cam = camGo.GetComponent<Camera>();
        if (cam == null) cam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        cam.fieldOfView = 50f;
        cam.farClipPlane = 8000f;
        cam.depthTextureMode = DepthTextureMode.Depth;
        var follow = camGo.GetComponent<CameraFollow>();
        if (follow == null) follow = camGo.AddComponent<CameraFollow>();
        var target = player != null ? player.transform : holder.transform.GetChild(0);
        follow.target = target;
        camGo.transform.position = target.position + new Vector3(0f, 12f, -22f);

        // 对局主控 + HUD（资源点占领 / 产金 / 复活 / 结算 / 重开）
        var matchGo = GameObject.Find("Match");
        if (matchGo == null) matchGo = new GameObject("Match");
        if (matchGo.GetComponent<MatchManager>() == null) matchGo.AddComponent<MatchManager>();
        if (matchGo.GetComponent<MatchHUD>() == null) matchGo.AddComponent<MatchHUD>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapSceneBuilder] 载具 + 对局主控已放到正式地图：{ScenePath}");
    }

    static GameObject Spawn(GameObject holder, string defAsset, Vector2 designPos, float yaw, float S,
                            int team = 0, bool isPlayer = false)
    {
        var def = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/{defAsset}.asset");
        if (def == null) { Debug.LogWarning($"[MapSceneBuilder] 找不到 {defAsset}"); return null; }

        float x = designPos.x * S, z = designPos.y * S;
        float y = BattlefieldTerrain.SampleHeight(x, z);

        var go = new GameObject(def.displayName);
        go.transform.SetParent(holder.transform, true);
        go.transform.position = new Vector3(x, y + 1f, z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var vp = go.AddComponent<VehiclePhysics>();
        vp.def = def;
        vp.terrain = null;                 // ← 关键：留空 = 用 Unity Terrain
        vp.ApplyDef();

        // 外观：优先用 Lux3D 生成的精细模型；找不到才退回方块占位
        Transform turret, muzzle;
        var modelSource = VehicleModelBinder.AutoFindModel(def.id);
        if (modelSource != null)
        {
            VehicleModelBinder.Attach(go, modelSource, def, out turret, out muzzle);
            ApplyModelMaterial(go, def);

            // Lux3D 的模型是整体网格，没有独立炮塔节点 —— 补一个"逻辑炮塔"，
            // 让瞄准方向 / 出膛点仍然有真实位置（炮塔暂时不能视觉旋转）
            if (turret == null || muzzle == null)
            {
                BuildLogicBones(go, def, out var lt, out var lm);
                if (turret == null) turret = lt;
                if (muzzle == null) muzzle = lm;
            }
        }
        else
        {
            BuildPrimitive(go, def, out turret, out muzzle);
        }

        vp.turretBone = turret;
        vp.muzzleBone = muzzle;

        // 战斗组件
        var combat = go.AddComponent<VehicleCombat>();
        combat.def = def;
        combat.SetTeam(team);

        if (isPlayer)
        {
            var pc = go.AddComponent<PlayerTankController>();
            pc.enabled = true;
        }
        else if (team == 1 || !isPlayer)
        {
            go.AddComponent<VehicleAI>();      // 除玩家外全部由 AI 控制（敌我都是）
        }

        // 飞机给个初始高度
        if (def.domain == Domain.Air)
            go.transform.position = new Vector3(x, Mathf.Max(y, BattlefieldTerrain.WaterLevel) + vp.flightAltitude, z);

        return go;
    }

    static void BuildPrimitive(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        if (def.domain == Domain.Sea)
        {
            // 炮艇按真实尺寸：约 18 m 长、4.6 m 宽
            Prim(root, "Hull", new Vector3(4.6f, 1.6f, 18f), new Vector3(0f, 0.8f, 0f), new Color(0.30f, 0.34f, 0.40f));
            Prim(root, "Cabin", new Vector3(2.8f, 1.8f, 4.5f), new Vector3(0f, 2.4f, -3f), new Color(0.45f, 0.48f, 0.52f));
            var tp = new GameObject("Turret");
            tp.transform.SetParent(root.transform, false);
            tp.transform.localPosition = new Vector3(0f, 2.2f, 1.5f);
            Prim(tp, "NavalGun", new Vector3(0.4f, 0.4f, 5.5f), new Vector3(0f, 0f, 2.6f), new Color(0.20f, 0.22f, 0.24f));
            turret = tp.transform;
            muzzle = Muzzle(tp.transform, new Vector3(0f, 0f, 5.5f));
        }
        else if (def.domain == Domain.Air)
        {
            // 战斗机按真实尺寸：约 11 m 长、10.5 m 翼展
            Prim(root, "Fuselage", new Vector3(1.2f, 1.2f, 11f), Vector3.zero, new Color(0.55f, 0.58f, 0.62f));
            Prim(root, "Wing", new Vector3(10.5f, 0.25f, 2.4f), new Vector3(0f, 0f, 0.6f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Tail", new Vector3(4.0f, 0.2f, 1.4f), new Vector3(0f, 0f, -4.6f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Fin", new Vector3(0.2f, 2.0f, 1.6f), new Vector3(0f, 1.0f, -4.6f), new Color(0.48f, 0.52f, 0.56f));
            turret = null;
            muzzle = Muzzle(root.transform, new Vector3(0f, 0f, 5.5f));
        }
        else
        {
            // 坦克按真实尺寸：轻坦 5.8 m、主战 7.0 m、重装 8.3 m（车体长）
            float len = def.mass > 55f ? 8.3f : def.mass > 30f ? 7.0f : 5.8f;
            float wid = len * 0.45f;
            float hei = len * 0.26f;
            Prim(root, "Body", new Vector3(wid, hei, len), new Vector3(0f, hei * 0.5f + 0.35f, 0f), new Color(0.42f, 0.48f, 0.34f));
            var tp = new GameObject("Turret");
            tp.transform.SetParent(root.transform, false);
            tp.transform.localPosition = new Vector3(0f, hei + 0.55f, -len * 0.1f);
            Prim(tp, "TurretBody", new Vector3(wid * 0.7f, hei * 0.6f, len * 0.45f), Vector3.zero, new Color(0.46f, 0.52f, 0.36f));
            Prim(tp, "Barrel", new Vector3(0.24f, 0.24f, len * 0.62f), new Vector3(0f, 0.05f, len * 0.52f), new Color(0.22f, 0.24f, 0.22f));
            turret = tp.transform;
            muzzle = Muzzle(tp.transform, new Vector3(0f, 0.05f, len * 0.82f));
        }
    }

    static void Prim(GameObject parent, string name, Vector3 scale, Vector3 pos, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var m = new Material(Shader.Find("Standard")) { color = c };
        go.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Transform Muzzle(Transform parent, Vector3 localPos)
    {
        var m = new GameObject("Muzzle");
        m.transform.SetParent(parent, false);
        m.transform.localPosition = localPos;
        return m.transform;
    }

    /// <summary>
    /// 给模型挂上带贴图的材质。贴图是从 Lux3D 的 GLB 里抽出来的 baseColor 图集
    /// （FBX 转换时为了统一上色清掉了材质，这里重新挂回去）。
    /// 阵营色调不写进材质，改由 VehicleCombat 在运行时用 MaterialPropertyBlock 上色。
    /// </summary>
    static void ApplyModelMaterial(GameObject root, VehicleDef def)
    {
        string texPath = $"Assets/Models/Vehicles/Textures/{def.id}_base.png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        EnsureFolder("Assets/Models");
        EnsureFolder("Assets/Models/Vehicles");
        EnsureFolder("Assets/Models/Vehicles/Materials");
        string matPath = $"Assets/Models/Vehicles/Materials/{def.id}.mat";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var std = Shader.Find("Standard");
        if (mat == null)
        {
            mat = new Material(std);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = std;
        if (tex != null) mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Glossiness", 0.18f);
        mat.SetFloat("_Metallic", 0.10f);
        // 场景光照偏亮，贴图本身是 0.43 的中灰，直接白着会过曝成塑料感 —— 压成军用灰绿
        mat.color = new Color(0.58f, 0.61f, 0.55f);
        EditorUtility.SetDirty(mat);

        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// Lux3D 的模型是整体网格，没有独立炮塔节点。按包围盒补一个"逻辑炮塔 + 炮口"，
    /// 让瞄准方向、出膛位置、弹道起点都是真的（视觉上炮塔暂不旋转）。
    /// </summary>
    static void BuildLogicBones(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        var rs = root.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) { turret = root.transform; muzzle = root.transform; return; }

        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        // 世界包围盒换成根节点局部坐标（只取中心，尺寸用最大水平边当车长）
        Vector3 center = root.transform.InverseTransformPoint(b.center);
        float halfLen = Mathf.Max(b.size.x, b.size.z) * 0.5f;
        float height = b.size.y;

        var tp = new GameObject("Turret");
        tp.transform.SetParent(root.transform, false);
        tp.transform.localPosition = new Vector3(0f, Mathf.Max(0.5f, center.y * 0.85f), 0f);
        turret = tp.transform;

        var mz = new GameObject("Muzzle");
        mz.transform.SetParent(tp.transform, false);
        mz.transform.localPosition = new Vector3(0f, height * 0.10f, halfLen * 0.90f);
        muzzle = mz.transform;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
        var leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
