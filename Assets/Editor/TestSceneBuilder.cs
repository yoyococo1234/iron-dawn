using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 阶段 1 测试场景：40×40 地形（一段 32° 坡 + 一个水塘）+ 三台坦克（轻/主/重）+ 相机。
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
        field.resolution = 81;          // 0.5 米一格，坡面更顺
        // 两段坡：24° 缓坡 + 32° 陡坡
        field.gentleZFoot = -4f;  field.gentleZTop = -9.6f;  field.gentleHeight = 2.5f;
        field.steepZFoot = -9.6f; field.steepZTop = -17.6f;  field.steepHeight = 7.5f;
        field.pondCenter = new Vector2(-9f, 10f);
        field.pondRadius = 7f;
        field.pondDepth = 2.5f;
        // 水面要低于平地（0），只在水塘凹陷处露出来；否则整块地都会被水盖住
        field.waterLevel = -0.4f;
        field.Generate();

        // ---------- 水面（只在水塘上，做成一个圆盘，避免边缘露出来）----------
        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Water";
        water.transform.position = new Vector3(field.pondCenter.x, field.waterLevel, field.pondCenter.y);
        water.transform.localScale = new Vector3(field.pondRadius * 2f, 0.02f, field.pondRadius * 2f);
        var wmat = new Material(Shader.Find("Standard"));
        wmat.color = new Color(0.10f, 0.35f, 0.55f, 0.55f);
        wmat.SetFloat("_Glossiness", 0.85f);
        wmat.SetFloat("_Metallic", 0.1f);
        // Standard 材质设成半透明
        wmat.SetFloat("_Mode", 3f);
        wmat.SetOverrideTag("RenderType", "Transparent");
        wmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wmat.SetInt("_ZWrite", 0);
        wmat.EnableKeyword("_ALPHABLEND_ON");
        wmat.renderQueue = 3000;
        water.GetComponent<Renderer>().sharedMaterial = wmat;
        Object.DestroyImmediate(water.GetComponent<Collider>());

        // ---------- 三台坦克（坡脚前方，车头朝坡 = -z 方向）----------
        SpawnTank("Vehicle_land_light", new Vector3(7f, 0f, -2f), field, 180f);
        SpawnTank("Vehicle_land_main", new Vector3(0f, 0f, -2f), field, 180f);
        SpawnTank("Vehicle_land_heavy", new Vector3(-7f, 0f, -2f), field, 180f);

        // ---------- 相机（侧 3/4 视角，能看清坡和车）----------
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 45f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(24f, 22f, -34f);
        camGo.transform.LookAt(new Vector3(0f, 3f, -10f));

        // ---------- 灯光 ----------
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.42f, 0.46f);

        // ---------- 保存 ----------
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test_Stage1.unity");
        AssetDatabase.SaveAssets();

        Debug.Log("[TestSceneBuilder] 阶段1场景已生成：Assets/Scenes/Test_Stage1.unity");
    }

    static void SpawnTank(string defName, Vector3 pos, TerrainField field, float yaw = 0f)
    {
        var def = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/{defName}.asset");
        if (def == null) { Debug.LogWarning($"[TestSceneBuilder] 找不到载具数据 {defName}"); return; }

        // 按载具类型给不同尺寸和颜色
        float len = def.domain == Domain.Land ? (def.mass > 55f ? 5.2f : def.mass > 30f ? 4.4f : 3.6f) : 4f;
        float wid = len * 0.55f;
        float hei = len * 0.28f;
        Color bodyColor = def.id.Contains("light") ? new Color(0.45f, 0.55f, 0.30f)
                        : def.id.Contains("heavy") ? new Color(0.35f, 0.38f, 0.32f)
                        : new Color(0.42f, 0.48f, 0.34f);

        var root = new GameObject(def.displayName);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(wid, hei, len);
        body.transform.localPosition = new Vector3(0f, hei * 0.5f + 0.15f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial = MakeMat(bodyColor);

        var turret = GameObject.CreatePrimitive(PrimitiveType.Cube);
        turret.name = "Turret";
        turret.transform.SetParent(root.transform, false);
        turret.transform.localScale = new Vector3(wid * 0.7f, hei * 0.6f, len * 0.45f);
        turret.transform.localPosition = new Vector3(0f, hei + 0.3f, -len * 0.1f);
        Object.DestroyImmediate(turret.GetComponent<Collider>());
        turret.GetComponent<Renderer>().sharedMaterial = MakeMat(bodyColor * 1.15f);

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrel.name = "Barrel";
        barrel.transform.SetParent(root.transform, false);
        barrel.transform.localScale = new Vector3(0.22f, 0.22f, len * 0.6f);
        barrel.transform.localPosition = new Vector3(0f, hei + 0.35f, len * 0.42f);
        Object.DestroyImmediate(barrel.GetComponent<Collider>());
        barrel.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.22f, 0.24f, 0.22f));

        var vp = root.AddComponent<VehiclePhysics>();
        vp.def = def;
        vp.terrain = field;
        vp.ApplyDef();
    }

    static Material MakeMat(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.25f);
        return m;
    }
}
