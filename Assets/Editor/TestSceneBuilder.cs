using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 阶段 1-2 测试场景：40×40 地形（24°缓坡 + 32°陡坡 + 水塘 + 海湾）+ 三域载具。
/// 载具优先用精细模型（Assets/Models/Vehicles/{id}.prefab 或 .glb），没有就用方块占位。
/// 菜单：Tools/铁皮黎明/生成阶段1测试场景
/// </summary>
public static class TestSceneBuilder
{
    [MenuItem("Tools/铁皮黎明/生成阶段1测试场景")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 地形 ----------
        var terrainGo = new GameObject("Terrain");
        var field = terrainGo.AddComponent<TerrainField>();
        field.size = 40f;
        field.resolution = 81;
        field.gentleZFoot = -4f;  field.gentleZTop = -9.6f;  field.gentleHeight = 2.5f;
        field.steepZFoot = -9.6f; field.steepZTop = -17.6f;  field.steepHeight = 7.5f;
        field.pondCenter = new Vector2(-9f, 10f);
        field.pondRadius = 7f;
        field.pondDepth = 2.5f;
        field.bayRect = new Rect(-19f, 9f, 38f, 7f);
        field.bayDepth = 3.5f;
        field.bayEdgeFade = 3f;
        field.waterLevel = -0.4f;
        field.Generate();

        // ---------- 水面 ----------
        var wmat = MakeWaterMat();
        var pond = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pond.name = "Water_Pond";
        pond.transform.position = new Vector3(field.pondCenter.x, field.waterLevel, field.pondCenter.y);
        pond.transform.localScale = new Vector3(field.pondRadius * 2f, 0.02f, field.pondRadius * 2f);
        pond.GetComponent<Renderer>().sharedMaterial = wmat;
        Object.DestroyImmediate(pond.GetComponent<Collider>());

        var bay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bay.name = "Water_Bay";
        bay.transform.position = new Vector3(field.bayRect.center.x, field.waterLevel, field.bayRect.center.y);
        bay.transform.localScale = new Vector3(field.bayRect.width, 0.08f, field.bayRect.height);
        bay.GetComponent<Renderer>().sharedMaterial = wmat;
        Object.DestroyImmediate(bay.GetComponent<Collider>());

        // ---------- 载具 ----------
        SpawnVehicle("Vehicle_land_light", new Vector3(7f, 0f, -2f), field, 180f);
        SpawnVehicle("Vehicle_land_main", new Vector3(0f, 0f, -2f), field, 180f);
        SpawnVehicle("Vehicle_land_heavy", new Vector3(-7f, 0f, -2f), field, 180f);
        SpawnVehicle("Vehicle_sea_gunboat", new Vector3(-8f, 0f, 17f), field, 90f);
        SpawnVehicle("Vehicle_air_fighter", new Vector3(8f, 20f, 6f), field, 90f);

        // ---------- 相机 ----------
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 50f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(34f, 34f, -30f);
        camGo.transform.LookAt(new Vector3(0f, 4f, 0f));

        // ---------- 灯光 ----------
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.42f, 0.46f);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test_Stage1.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[TestSceneBuilder] 场景已生成：Assets/Scenes/Test_Stage1.unity");
    }

    // ---------------- 载具 ----------------

    static void SpawnVehicle(string defName, Vector3 pos, TerrainField field, float yaw)
    {
        var def = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/{defName}.asset");
        if (def == null) { Debug.LogWarning($"[TestSceneBuilder] 找不到载具数据 {defName}"); return; }

        var root = new GameObject(def.displayName);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var vp = root.AddComponent<VehiclePhysics>();
        vp.def = def;
        vp.terrain = field;
        vp.ApplyDef();

        // 1) 优先用精细模型（VehicleDef 指定的 / 按命名约定自动找的）
        var modelSource = def.modelPrefab != null ? def.modelPrefab : VehicleModelBinder.AutoFindModel(def.id);
        if (modelSource != null)
        {
            VehicleModelBinder.Attach(root, modelSource, def, out var turret, out var muzzle);
            vp.turretBone = turret;
            vp.muzzleBone = muzzle;
            Debug.Log($"[TestSceneBuilder] {def.displayName} 使用精细模型：{modelSource.name}");
        }
        else
        {
            // 2) 没有模型 → 方块占位（缺模型不崩）
            BuildPrimitiveBody(root, def, out var turret, out var muzzle);
            vp.turretBone = turret;
            vp.muzzleBone = muzzle;
        }
    }

    /// <summary>方块占位模型。返回炮塔节点和炮口节点，接口和精细模型一致。</summary>
    static void BuildPrimitiveBody(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        if (def.domain == Domain.Sea)
        {
            var hull = Prim(root, "Hull", new Vector3(2.6f, 0.9f, 7.5f), new Vector3(0f, 0.45f, 0f), new Color(0.30f, 0.34f, 0.40f));
            var cabin = Prim(root, "Cabin", new Vector3(1.6f, 1.0f, 2.2f), new Vector3(0f, 1.35f, -1.2f), new Color(0.45f, 0.48f, 0.52f));

            var turretPivot = new GameObject("Turret");
            turretPivot.transform.SetParent(root.transform, false);
            turretPivot.transform.localPosition = new Vector3(0f, 1.1f, 0.6f);
            Prim(turretPivot, "NavalGun", new Vector3(0.3f, 0.3f, 3.4f), new Vector3(0f, 0f, 1.6f), new Color(0.20f, 0.22f, 0.24f));

            turret = turretPivot.transform;
            muzzle = MakeMuzzle(turretPivot, new Vector3(0f, 0f, 3.4f));
        }
        else if (def.domain == Domain.Air)
        {
            Prim(root, "Fuselage", new Vector3(0.9f, 0.9f, 6.5f), Vector3.zero, new Color(0.55f, 0.58f, 0.62f));
            Prim(root, "Wing", new Vector3(8.5f, 0.18f, 1.8f), new Vector3(0f, 0f, 0.4f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Tail", new Vector3(3.0f, 0.15f, 1.0f), new Vector3(0f, 0f, -2.7f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Fin", new Vector3(0.15f, 1.4f, 1.2f), new Vector3(0f, 0.7f, -2.7f), new Color(0.48f, 0.52f, 0.56f));

            turret = null;   // 飞机没有炮塔
            muzzle = MakeMuzzle(root, new Vector3(0f, 0f, 3.4f));
        }
        else
        {
            float len = def.mass > 55f ? 5.2f : def.mass > 30f ? 4.4f : 3.6f;
            float wid = len * 0.55f;
            float hei = len * 0.28f;
            Color bodyColor = def.id.Contains("light") ? new Color(0.45f, 0.55f, 0.30f)
                            : def.id.Contains("heavy") ? new Color(0.35f, 0.38f, 0.32f)
                            : new Color(0.42f, 0.48f, 0.34f);

            Prim(root, "Body", new Vector3(wid, hei, len), new Vector3(0f, hei * 0.5f + 0.15f, 0f), bodyColor);

            var turretPivot = new GameObject("Turret");
            turretPivot.transform.SetParent(root.transform, false);
            turretPivot.transform.localPosition = new Vector3(0f, hei + 0.3f, -len * 0.1f);
            Prim(turretPivot, "TurretBody", new Vector3(wid * 0.7f, hei * 0.6f, len * 0.45f), Vector3.zero, bodyColor * 1.15f);
            Prim(turretPivot, "Barrel", new Vector3(0.22f, 0.22f, len * 0.6f), new Vector3(0f, 0.05f, len * 0.52f), new Color(0.22f, 0.24f, 0.22f));

            turret = turretPivot.transform;
            muzzle = MakeMuzzle(turretPivot, new Vector3(0f, 0.05f, len * 0.82f));
        }
    }

    static GameObject Prim(GameObject parent, string name, Vector3 scale, Vector3 localPos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localScale = scale;
        go.transform.localPosition = localPos;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = MakeMat(color);
        return go;
    }

    static Transform MakeMuzzle(GameObject parent, Vector3 localPos)
    {
        var m = new GameObject("Muzzle");
        m.transform.SetParent(parent.transform, false);
        m.transform.localPosition = localPos;
        return m.transform;
    }

    // ---------------- 材质 ----------------

    static Material MakeMat(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.25f);
        return m;
    }

    static Material MakeWaterMat()
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.10f, 0.35f, 0.55f, 0.55f);
        m.SetFloat("_Glossiness", 0.85f);
        m.SetFloat("_Metallic", 0.1f);
        m.SetFloat("_Mode", 3f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = 3000;
        return m;
    }
}
