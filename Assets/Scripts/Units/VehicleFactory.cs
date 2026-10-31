using UnityEngine;

/// <summary>
/// 运行时造一台载具（车库选完车之后调用）。
/// 编辑器里 MapSceneBuilder 摆场景也走这里，保证「场上摆的」和「动态生成的」长得一样。
/// </summary>
public static class VehicleFactory
{
    /// <summary>在 pos 处造一台载具。返回它的 VehicleCombat。</summary>
    public static VehicleCombat Spawn(VehicleDef def, int team, Vector3 pos, float yaw, bool isPlayer)
    {
        if (def == null) { Debug.LogWarning("[VehicleFactory] def 为空"); return null; }

        var go = new GameObject(def.displayName);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var vp = go.AddComponent<VehiclePhysics>();
        vp.def = def;
        vp.terrain = null;                 // 留空 = 用正式地图的 Unity Terrain 分块采样
        vp.ApplyDef();

        // 外观：优先精细模型，没有才用方块占位
        Transform turret, muzzle;
        if (!VehicleModelBinder.Attach(go, def, out turret, out muzzle))
            BuildPrimitive(go, def, out turret, out muzzle);

        vp.turretBone = turret;
        vp.muzzleBone = muzzle;

        // 战斗组件要在模型挂好之后再 AddComponent —— Awake 里要按模型包围盒建命中盒
        var combat = go.AddComponent<VehicleCombat>();
        combat.def = def;
        combat.SetTeam(team);

        if (isPlayer) go.AddComponent<PlayerTankController>();
        else go.AddComponent<VehicleAI>();

        // 飞机给初始高度
        if (def.domain == Domain.Air)
        {
            float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(pos.x, pos.z), BattlefieldTerrain.WaterLevel);
            go.transform.position = new Vector3(pos.x, ground + vp.flightAltitude, pos.z);
        }

        return combat;
    }

    /// <summary>没有精细模型时的方块占位。</summary>
    public static void BuildPrimitive(GameObject root, VehicleDef def, out Transform turret, out Transform muzzle)
    {
        if (def.domain == Domain.Sea)
        {
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
            Prim(root, "Fuselage", new Vector3(1.2f, 1.2f, 11f), Vector3.zero, new Color(0.55f, 0.58f, 0.62f));
            Prim(root, "Wing", new Vector3(10.5f, 0.25f, 2.4f), new Vector3(0f, 0f, 0.6f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Tail", new Vector3(4.0f, 0.2f, 1.4f), new Vector3(0f, 0f, -4.6f), new Color(0.48f, 0.52f, 0.56f));
            Prim(root, "Fin", new Vector3(0.2f, 2.0f, 1.6f), new Vector3(0f, 1.0f, -4.6f), new Color(0.48f, 0.52f, 0.56f));
            turret = null;
            muzzle = Muzzle(root.transform, new Vector3(0f, 0f, 5.5f));
        }
        else
        {
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
        Object.Destroy(go.GetComponent<Collider>());
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
