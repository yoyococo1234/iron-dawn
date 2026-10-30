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

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapSceneBuilder] 载具已放到正式地图：{ScenePath}");
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

        // 方块占位（还没有正式模型）
        BuildPrimitive(go, def, out var turret, out var muzzle);
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
}
