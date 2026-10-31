using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 正式地图（Map_Battlefield.unity）的场景搭建：敌方载具 + 相机 + 对局主控 + 车库。
///
/// 玩家不再预先摆在场景里 —— 开局进车库选车，由 VehicleFactory 在运行时生成。
///
/// 菜单：Tools/铁皮黎明/② 搭建对局场景
/// </summary>
public static class MapSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Map_Battlefield.unity";
    const string RootName = "Test_Vehicles";

    [MenuItem("Tools/铁皮黎明/② 搭建对局场景（敌方+相机+车库）", false, 20)]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BattlefieldTerrain.Invalidate();

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var holder = new GameObject(RootName);

        float S = BattlefieldTerrain.Scale;

        // 先把每台车的模型 Prefab / 材质绑到 VehicleDef 上（运行时动态生成要用）
        foreach (var def in LoadRoster())
            EnsureModelAssets(def);

        // 敌方 3 台：陆 / 海 / 空各一 —— 打光就赢
        SpawnEnemy(holder, "Vehicle_land_main", new Vector2(-8f, -10f), -90f, S);
        SpawnEnemy(holder, "Vehicle_sea_gunboat", new Vector2(12f, 42f), -90f, S);
        SpawnEnemy(holder, "Vehicle_air_fighter", new Vector2(16f, -22f), -90f, S);

        // AI 的兜底目标点
        var objSea = new GameObject("Objective_Sea");
        objSea.transform.position = new Vector3(0f, 0f, 40f * S);
        var objCenter = new GameObject("Objective_Center");
        objCenter.transform.position = Vector3.zero;

        foreach (var ai in holder.GetComponentsInChildren<VehicleAI>())
        {
            var def = ai.GetComponent<VehicleCombat>().Def;
            ai.objective = (def != null && def.domain == Domain.Sea) ? objSea.transform : objCenter.transform;
        }

        // 相机
        var camGo = GameObject.Find("Main Camera");
        if (camGo == null) camGo = new GameObject("Main Camera");
        var cam = camGo.GetComponent<Camera>();
        if (cam == null) cam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        cam.fieldOfView = 50f;
        cam.farClipPlane = 8000f;
        cam.depthTextureMode = DepthTextureMode.Depth;
        if (camGo.GetComponent<CameraFollow>() == null) camGo.AddComponent<CameraFollow>();

        // 清掉调试用的静态相机（它们和主相机同为 depth 0，会盖掉跟随视角）
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other == null || other.gameObject == camGo) continue;
            Object.DestroyImmediate(other.gameObject);
        }

        // 对局主控 + HUD + 车库 + 开局封面
        var matchGo = GameObject.Find("Match");
        if (matchGo == null) matchGo = new GameObject("Match");
        if (matchGo.GetComponent<MatchManager>() == null) matchGo.AddComponent<MatchManager>();
        if (matchGo.GetComponent<MatchHUD>() == null) matchGo.AddComponent<MatchHUD>();
        if (matchGo.GetComponent<GarageScreen>() == null) matchGo.AddComponent<GarageScreen>();
        if (matchGo.GetComponent<GameStartScreen>() == null) matchGo.AddComponent<GameStartScreen>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapSceneBuilder] 对局场景已搭好：{ScenePath}（敌方 3 台 + 车库 + HUD）");
    }

    static VehicleDef[] LoadRoster()
    {
        var ids = new[] { "land_light", "land_main", "land_heavy", "sea_gunboat", "air_fighter", "air_attacker" };
        var list = new System.Collections.Generic.List<VehicleDef>();
        foreach (var id in ids)
        {
            var d = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/Vehicle_{id}.asset");
            if (d != null) list.Add(d);
        }
        return list.ToArray();
    }

    /// <summary>把模型 Prefab 和材质绑到 VehicleDef 上，运行时动态生成载具要用。</summary>
    static void EnsureModelAssets(VehicleDef def)
    {
        if (def == null) return;
        if (def.modelPrefab == null) def.modelPrefab = VehicleModelBinder.AutoFindModel(def.id);
        if (def.modelMaterial == null) def.modelMaterial = EnsureMaterial(def);
        EditorUtility.SetDirty(def);
    }

    static Material EnsureMaterial(VehicleDef def)
    {
        EnsureFolder("Assets/Models");
        EnsureFolder("Assets/Models/Vehicles");
        EnsureFolder("Assets/Models/Vehicles/Materials");
        string matPath = $"Assets/Models/Vehicles/Materials/{def.id}.mat";

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Models/Vehicles/Textures/{def.id}_base.png");
        var std = Shader.Find("Standard");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
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
        return mat;
    }

    static GameObject SpawnEnemy(GameObject holder, string defAsset, Vector2 designPos, float yaw, float S)
    {
        var def = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/{defAsset}.asset");
        if (def == null) { Debug.LogWarning($"[MapSceneBuilder] 找不到 {defAsset}"); return null; }

        float x = designPos.x * S, z = designPos.y * S;
        float y = BattlefieldTerrain.SampleHeight(x, z);

        var combat = VehicleFactory.Spawn(def, 1, new Vector3(x, y + 1f, z), yaw, false);
        if (combat == null) return null;
        combat.transform.SetParent(holder.transform, true);
        return combat.gameObject;
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
