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
        // 海湾：给船用的大片水（放在地形内部，别贴边）
        field.bayRect = new Rect(-19f, 9f, 38f, 7f);
        field.bayDepth = 3.5f;
        field.bayEdgeFade = 3f;
        // 水面要低于平地（0），只在水塘凹陷处露出来；否则整块地都会被水盖住
        field.waterLevel = -0.4f;
        field.Generate();

        // ---------- 水面：水塘圆盘 + 海湾长条 ----------
        var wmat = MakeWaterMat();
        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Water_Pond";
        water.transform.position = new Vector3(field.pondCenter.x, field.waterLevel, field.pondCenter.y);
        water.transform.localScale = new Vector3(field.pondRadius * 2f, 0.02f, field.pondRadius * 2f);
        water.GetComponent<Renderer>().sharedMaterial = wmat;
        Object.DestroyImmediate(water.GetComponent<Collider>());

        var bay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bay.name = "Water_Bay";
        bay.transform.position = new Vector3(field.bayRect.center.x, field.waterLevel, field.bayRect.center.y);
        bay.transform.localScale = new Vector3(field.bayRect.width, 0.08f, field.bayRect.height);
        bay.GetComponent<Renderer>().sharedMaterial = wmat;
        Object.DestroyImmediate(bay.GetComponent<Collider>());

        // ---------- 三台坦克（坡脚前方，车头朝坡 = -z 方向）----------
        SpawnTank("Vehicle_land_light", new Vector3(7f, 0f, -2f), field, 180f);
        SpawnTank("Vehicle_land_main", new Vector3(0f, 0f, -2f), field, 180f);
        SpawnTank("Vehicle_land_heavy", new Vector3(-7f, 0f, -2f), field, 180f);

        // ---------- 炮艇（海湾里）+ 战斗机（空中）----------
        SpawnTank("Vehicle_sea_gunboat", new Vector3(-8f, 0f, 17f), field, 90f);
        SpawnTank("Vehicle_air_fighter", new Vector3(8f, 20f, 6f), field, 90f);

        // ---------- 相机（能看到坡、水塘、海湾和飞机）----------
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

        // ---------- 保存 ----------
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test_Stage1.unity");
        AssetDatabase.SaveAssets();

        Debug.Log("[TestSceneBuilder] 阶段1场景已生成：Assets/Scenes/Test_Stage1.unity");
    }

    static void SpawnTank(string defName, Vector3 pos, TerrainField field, float yaw = 0f)
    {
        var def = AssetDatabase.LoadAssetAtPath<VehicleDef>($"Assets/Data/Vehicles/{defName}.asset");
        if (def == null) { Debug.LogWarning($"[TestSceneBuilder] 找不到载具数据 {defName}"); return; }

        var root = new GameObject(def.displayName);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (def.domain == Domain.Sea)
        {
            // 船：扁平船体 + 驾驶舱 + 炮管
            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(root.transform, false);
            hull.transform.localScale = new Vector3(2.6f, 0.9f, 7.5f);
            hull.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            Object.DestroyImmediate(hull.GetComponent<Collider>());
            hull.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.30f, 0.34f, 0.40f));

            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(root.transform, false);
            cabin.transform.localScale = new Vector3(1.6f, 1.0f, 2.2f);
            cabin.transform.localPosition = new Vector3(0f, 1.35f, -1.2f);
            Object.DestroyImmediate(cabin.GetComponent<Collider>());
            cabin.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.45f, 0.48f, 0.52f));

            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "NavalGun";
            gun.transform.SetParent(root.transform, false);
            gun.transform.localScale = new Vector3(0.3f, 0.3f, 3.4f);
            gun.transform.localPosition = new Vector3(0f, 1.1f, 2.2f);
            Object.DestroyImmediate(gun.GetComponent<Collider>());
            gun.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.20f, 0.22f, 0.24f));
        }
        else if (def.domain == Domain.Air)
        {
            // 飞机：机身 + 主翼 + 尾翼
            var fuselage = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fuselage.name = "Fuselage";
            fuselage.transform.SetParent(root.transform, false);
            fuselage.transform.localScale = new Vector3(0.9f, 0.9f, 6.5f);
            Object.DestroyImmediate(fuselage.GetComponent<Collider>());
            fuselage.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.55f, 0.58f, 0.62f));

            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = "Wing";
            wing.transform.SetParent(root.transform, false);
            wing.transform.localScale = new Vector3(8.5f, 0.18f, 1.8f);
            wing.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            Object.DestroyImmediate(wing.GetComponent<Collider>());
            wing.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.48f, 0.52f, 0.56f));

            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tail.name = "Tail";
            tail.transform.SetParent(root.transform, false);
            tail.transform.localScale = new Vector3(3.0f, 0.15f, 1.0f);
            tail.transform.localPosition = new Vector3(0f, 0f, -2.7f);
            Object.DestroyImmediate(tail.GetComponent<Collider>());
            tail.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.48f, 0.52f, 0.56f));

            var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = "Fin";
            fin.transform.SetParent(root.transform, false);
            fin.transform.localScale = new Vector3(0.15f, 1.4f, 1.2f);
            fin.transform.localPosition = new Vector3(0f, 0.7f, -2.7f);
            Object.DestroyImmediate(fin.GetComponent<Collider>());
            fin.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.48f, 0.52f, 0.56f));
        }
        else
        {
            // 坦克：车体 + 履带 + 炮塔 + 炮管
            float len = def.mass > 55f ? 5.2f : def.mass > 30f ? 4.4f : 3.6f;
            float wid = len * 0.55f;
            float hei = len * 0.28f;
            Color bodyColor = def.id.Contains("light") ? new Color(0.45f, 0.55f, 0.30f)
                            : def.id.Contains("heavy") ? new Color(0.35f, 0.38f, 0.32f)
                            : new Color(0.42f, 0.48f, 0.34f);

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
        }

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

    /// <summary>半透明水材质（Standard 透明模式要手动设一堆参数）。</summary>
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
