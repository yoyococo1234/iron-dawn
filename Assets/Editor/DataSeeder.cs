using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 一键生成/刷新全部默认数据资产（幂等：已存在则更新数值，不重复创建）。
/// 菜单：Tools/铁皮黎明/生成默认数据
/// </summary>
public static class DataSeeder
{
    const string Root = "Assets/Data";

    [MenuItem("Tools/铁皮黎明/生成默认数据")]
    public static void SeedAll()
    {
        EnsureFolder("Assets/Data/Ammo");
        EnsureFolder("Assets/Data/Weapons");
        EnsureFolder("Assets/Data/Vehicles");
        EnsureFolder("Assets/Data/Zones");
        EnsureFolder("Assets/Data/Match");
        EnsureFolder("Assets/Data/AI");
        EnsureFolder("Assets/Data/Battlefield");

        // ---------- 弹药 ----------
        var ap    = Ammo("ap",      "穿甲弹",    1.2f, 0.4f, 0.1f, 0.9f, 5);
        var he    = Ammo("he",      "高爆弹",    0.5f, 1.5f, 0.6f, 0.8f, 5);
        var heat  = Ammo("heat",    "破甲弹",    1.0f, 0.9f, 0.1f, 0.9f, 15);
        var aa    = Ammo("aa",      "防空弹",    0.1f, 0.5f, 1.5f, 0.1f, 10);
        var torp  = Ammo("torpedo", "鱼雷",      0.1f, 0.2f, 0.0f, 1.0f, 30);
        var bomb  = Ammo("bomb",    "航空炸弹",  0.8f, 1.2f, 0.2f, 1.2f, 40);

        // ---------- 武器 ----------
        // 弹速 = 真实炮口初速。原来写 140~250 m/s（约等于弹弓），2 km 图上要飞 4~5 秒，
        // 目标早开出 50 米 —— 这就是"炮弹打不中"的真正根因。现在按真炮取值：
        //   75mm 780 / 120mm 1600 / 130mm 950 / 20mm 机炮 1050 / 舰炮 800 m/s
        // 射程也同步放大到 2 km 图该有的交战距离（原来 160 m 在 2000 m 图上等于刺刀）。
        var gun75  = Weapon("gun_75",   "75mm 炮",     180f, 3.0f,  700f,  780f, Domain.Land, ap, he);
        var gun120 = Weapon("gun_120",  "120mm 主炮",  300f, 4.5f, 1200f, 1600f, Domain.Land, ap, he, heat);
        var gun130 = Weapon("gun_130",  "130mm 重炮",  420f, 6.0f,  900f,  950f, Domain.Land, ap, heat);
        var mg12   = Weapon("mg_12",    "12mm 机枪",    25f, 0.12f, 400f,  850f, Domain.Land, ap);
        var naval  = Weapon("naval_gun","舰炮",        260f, 5.0f, 1200f,  800f, Domain.Sea,  he, heat);
        var torpL  = Weapon("torpedo_launcher", "鱼雷管", 850f, 12f, 500f, 40f, Domain.Sea,  torp);
        var gun20  = Weapon("gun_20",   "20mm 机炮",    60f, 0.15f, 800f, 1050f, Domain.Air,  ap, aa);
        var bombR  = Weapon("bomb_rack","航空炸弹挂架", 750f, 8.0f, 300f,   0f, Domain.Air,  bomb);

        // ---------- 载具 ----------
        // 参数顺序：id, 名, 域, 价, 血, 装甲模式, 正/侧/背, 统一, 速度, 转向, 炮塔转速,
        //           功率kW, 质量t, 硬地阻力, 软地阻力, 涉水深度, 通行, 复活基础, 说明, 武器
        Vehicle("land_light", "轻坦", Domain.Land, 160, 800, ArmorMode.Directional,
            0.95f, 1.10f, 1.30f, 0f, 18f, 90f, 120f,
            320f, 15f, 0.8f, 1.6f, 1.0f,
            TerrainType.Land | TerrainType.ShallowWater, 80, "快、皮薄，能爬 35° 陡坡", gun75, mg12);

        Vehicle("land_main", "主战坦克", Domain.Land, 200, 1200, ArmorMode.Directional,
            0.70f, 0.90f, 1.10f, 0f, 12f, 60f, 80f,
            750f, 45f, 0.9f, 1.8f, 1.2f,
            TerrainType.Land | TerrainType.ShallowWater, 80, "均衡，能爬 29°", gun120, mg12);

        var heavy = Vehicle("land_heavy", "重装坦克", Domain.Land, 220, 1600, ArmorMode.Directional,
            0.60f, 0.80f, 1.05f, 0f, 8f, 40f, 50f,
            800f, 65f, 1.0f, 2.0f, 1.4f,
            TerrainType.Land | TerrainType.ShallowWater, 90, "正面极硬，只能爬 22°", gun130, mg12);
        // Lux3D 出的重装坦克长轴沿 +X，其余 5 台沿 +Z —— 这里转正
        heavy.modelYawOffset = 90f;
        EditorUtility.SetDirty(heavy);

        Vehicle("sea_gunboat", "炮艇", Domain.Sea, 180, 1000, ArmorMode.Directional,
            0.85f, 1.00f, 1.15f, 0f, 22f, 70f, 100f,
            1200f, 40f, 1.2f, 1.2f, 0.8f,
            TerrainType.ShallowWater | TerrainType.DeepWater, 80, "海上霸主，吃水 0.8m", naval, torpL);

        Vehicle("air_fighter", "战斗机", Domain.Air, 160, 300, ArmorMode.Uniform,
            1f, 1f, 1f, 1.00f, 45f, 120f, 0f,
            900f, 8f, 0f, 0f, 0f,
            TerrainType.Air, 80, "最快，制空；一击即落", gun20);

        Vehicle("air_attacker", "攻击机", Domain.Air, 200, 450, ArmorMode.Uniform,
            1f, 1f, 1f, 1.10f, 32f, 80f, 0f,
            1100f, 12f, 0f, 0f, 0f,
            TerrainType.Air, 85, "能打地面，越墙；慢、怕防空", gun20, bombR);

        // ---------- 对局与经济 ----------
        var match = GetOrCreate<MatchDef>($"{Root}/Match/Match_Default.asset");
        match.teamSize = 5;

        // 3 条命规则：玩家部署 3 次；敌方 3 台载具，打光即胜
        match.playerLives = 3;
        match.enemyLives = 3;

        // 车库可选载具
        var roster = new List<VehicleDef>();
        foreach (var id in new[] { "land_light", "land_main", "land_heavy", "sea_gunboat", "air_fighter", "air_attacker" })
        {
            var d = GetOrCreate<VehicleDef>($"{Root}/Vehicles/Vehicle_{id}.asset");
            if (d != null) roster.Add(d);
        }
        match.roster = roster.ToArray();

        match.startingGold = 200;
        match.respawnBase = 80;
        match.respawnGrowth = 1.22f;
        match.swapGrowth = 1.15f;
        match.swapGrowthCap = 3f;
        match.killRewardRate = 0.40f;
        match.assistRewardRate = 0.20f;
        match.captureIncome = 15;
        match.captureInterval = 10f;
        match.survivalIncome = 5;
        match.survivalInterval = 10f;
        match.coinBoxValue = 50;
        match.coinBoxInterval = 25f;
        match.moneyBagValue = 25;
        match.repairCostPerPercent = 1;
        match.ammoRefillCost = 20;
        match.matchTimeLimitMinutes = 15f;
        EditorUtility.SetDirty(match);

        // ---------- 区域（v2：2000×2000 世界坐标，设计坐标 ×16.667）----------
        // 中央 3 个点 15 金/10 秒；家门口 2 个点 5 金/10 秒（躺着收，但不够赢）
        var zPort = Zone("zone_port", "港口海战", Domain.Sea, TerrainType.DeepWater,
            new Rect(-350f, 217f, 700f, 700f), 15,
            new[] { new Vector2(-667f, 500f), new Vector2(667f, 500f) });          // 设计 (0,34) r21
        var zUrban = Zone("zone_urban", "城区巷战", Domain.Land, TerrainType.Land,
            new Rect(-417f, -300f, 834f, 834f), 15,
            new[] { new Vector2(-333f, 117f), new Vector2(333f, 117f) });          // 设计 (0,7) r25
        var zHill = Zone("zone_hill", "山地伏击", Domain.Land, TerrainType.Land,
            new Rect(-200f, -950f, 400f, 400f), 15,
            new[] { new Vector2(-500f, -750f), new Vector2(500f, -750f) });        // 设计 (0,-45) r12
        var zFlankL = Zone("zone_flank_left", "左翼平原", Domain.Land, TerrainType.Land,
            new Rect(-950f, -333f, 400f, 400f), 5,
            new[] { new Vector2(-233f, -133f) });                                  // 设计 (-45,-8) r12
        var zFlankR = Zone("zone_flank_right", "右翼平原", Domain.Land, TerrainType.Land,
            new Rect(550f, -333f, 400f, 400f), 5,
            new[] { new Vector2(233f, -133f) });                                   // 设计 (45,-8) r12

        // ---------- AI ----------
        var ai = GetOrCreate<AIDef>($"{Root}/AI/AI_Default.asset");
        ai.visionRange = 120f;
        ai.fireRange = 90f;
        ai.thinkInterval = 0.5f;
        ai.retreatHpPercent = 0.30f;
        ai.accuracy = 0.70f;
        ai.aggression = 0.60f;
        ai.crossDomainHunting = false;
        ai.crossDomainRetaliation = true;
        ai.retaliationMemorySeconds = 5f;
        ai.swapGoldThreshold = 1.2f;
        ai.repairHpPercent = 0.40f;
        EditorUtility.SetDirty(ai);

        // ---------- 战场（v2：2000×2000）----------
        var bf = GetOrCreate<BattlefieldDef>($"{Root}/Battlefield/Battlefield_Default.asset");
        bf.arenaSize = 2000f;
        bf.allySpawnLineX = -933f;
        bf.enemySpawnLineX = 933f;
        bf.spawnLineZMin = -967f;
        bf.spawnLineZMax = 967f;
        bf.waterLevel = -6.667f;
        bf.zones = new[] { zPort, zUrban, zHill, zFlankL, zFlankR };
        bf.coinBoxPoints = new[]
        {
            new Vector2(-667f, 500f), new Vector2(667f, 500f),      // 海域
            new Vector2(-333f, 117f), new Vector2(333f, 117f),      // 城区
            new Vector2(-233f, -133f), new Vector2(233f, -133f),    // 中央平原
            new Vector2(-500f, -750f), new Vector2(500f, -750f),    // 山地
        };
        EditorUtility.SetDirty(bf);

        // 注意：这里刻意不调用 AssetDatabase.Refresh()。
        // Refresh 会触发域重载，而本方法常常是在一次工具调用（MCP）里执行的，
        // 域重载会把那次调用打断，导致调用方超时、连接卡死。
        // CreateAsset / SaveAssets 已经足够让资产落盘。
        AssetDatabase.SaveAssets();

        Debug.Log("[DataSeeder] 默认数据已生成：6 载具 / 8 武器 / 6 弹药 / 5 区域 / 1 对局 / 1 AI / 1 战场（2000×2000）");
    }

    // ---------------- 辅助 ----------------

    static AmmoDef Ammo(string id, string name, float vsArmor, float vsSoft, float vsAir, float vsSea, int price)
    {
        var a = GetOrCreate<AmmoDef>($"{Root}/Ammo/Ammo_{id}.asset");
        a.id = id; a.displayName = name;
        a.vsArmor = vsArmor; a.vsSoft = vsSoft; a.vsAir = vsAir; a.vsSea = vsSea;
        a.pricePerShot = price; a.isSupport = false;
        EditorUtility.SetDirty(a);
        return a;
    }

    static WeaponDef Weapon(string id, string name, float dmg, float reload, float range, float speed,
        Domain dom, params AmmoDef[] ammo)
    {
        var w = GetOrCreate<WeaponDef>($"{Root}/Weapons/Weapon_{id}.asset");
        w.id = id; w.displayName = name;
        w.baseDamage = dmg; w.reloadTime = reload; w.range = range; w.projectileSpeed = speed;
        w.mountedDomain = dom; w.ammoTypes = ammo;
        EditorUtility.SetDirty(w);
        return w;
    }

    static VehicleDef Vehicle(string id, string name, Domain dom, int price, float hp, ArmorMode mode,
        float front, float side, float rear, float uniform, float speed, float turn, float turret,
        float power, float mass, float resHard, float resSoft, float wade,
        TerrainType terrain, int respawnBase, string desc, params WeaponDef[] weapons)
    {
        var v = GetOrCreate<VehicleDef>($"{Root}/Vehicles/Vehicle_{id}.asset");
        v.id = id; v.displayName = name; v.domain = dom;
        v.price = price; v.maxHp = hp; v.armorMode = mode;
        v.armorFront = front; v.armorSide = side; v.armorRear = rear; v.uniformMultiplier = uniform;
        v.speed = speed; v.turnSpeed = turn; v.turretTurnSpeed = turret;
        v.enginePower = power; v.mass = mass;
        v.resistanceHard = resHard; v.resistanceSoft = resSoft; v.wadeDepth = wade;
        v.terrainAllowed = terrain; v.respawnBaseCost = respawnBase;
        v.description = desc; v.weapons = weapons;
        EditorUtility.SetDirty(v);
        return v;
    }

    static ZoneDef Zone(string id, string name, Domain dom, TerrainType terrain, Rect bounds,
        int income, Vector2[] coinPoints)
    {
        var z = GetOrCreate<ZoneDef>($"{Root}/Zones/Zone_{id}.asset");
        z.id = id; z.displayName = name; z.domain = dom; z.terrain = terrain;
        z.bounds = bounds; z.captureable = true; z.incomePerTick = income;
        z.coinBoxPoints = coinPoints;
        EditorUtility.SetDirty(z);
        return z;
    }

    static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var created = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(created, path);
        return created;
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
