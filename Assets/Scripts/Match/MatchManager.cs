using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 对局主控（5v5 · 每人 3 条命 · 个人经济）。
///
/// 规则：
///   · 双方各 5 个单位；你是我方 0 号，其余 4 个是我方 AI，敌方 5 个全是 AI
///   · 每个单位最多 playerLives 条命；命用光就出局，不会再有车
///   · 经济独立：每人有自己的金币。买车花自己的钱，击毁敌人 +40% 车价进自己口袋
///   · 唯一共享的收入是资源点：占下来的点每 10 秒给**全队每人**发一份 incomePerTick
///   · 一方 5 个人全部出局 → 对方获胜；时间到则比剩余命数
/// </summary>
public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    [Header("数据（留空则自动从 Assets/Data 找默认资产）")]
    public MatchDef match;
    public BattlefieldDef battlefield;

    [Header("规则")]
    [Tooltip("无争夺时占下一个点要几秒")]
    public float captureSeconds = 6f;
    [Tooltip("你阵亡后多久弹出车库")]
    public float garageDelay = 2.5f;
    [Tooltip("AI 阵亡后多久重新出击")]
    public float aiRespawnDelay = 6f;
    [Tooltip("每队人数")]
    public int teamSize = 5;

    [Header("测试开关")]
    [Tooltip("不限命数：玩家可以无限次换车，对局也不会因为你没命而结束（调参/验收时用）")]
    public bool unlimitedLives = true;
    [Tooltip("无限金币：玩家金币恒定，随便换车（调参/验收时用）")]
    public bool unlimitedGold = true;
    [Tooltip("无限金币时的金币数")]
    public int debugGold = 999999;

    [Header("运行时状态（只读，给 HUD 用）")]
    public float timeLeft;
    public bool matchOver;
    public int winnerTeam = -1;
    public string resultText = "";

    /// <summary>双方击杀数（HUD 顶部显示）。</summary>
    public readonly int[] kills = new int[2];

    public readonly List<UnitState> units = new List<UnitState>();
    public readonly List<ZoneState> zones = new List<ZoneState>();
    public readonly List<FeedEntry> feed = new List<FeedEntry>();

    public class FeedEntry
    {
        public string text;
        public float time;
    }

    UnitState _human;
    readonly Dictionary<VehicleCombat, UnitState> _map = new Dictionary<VehicleCombat, UnitState>();
    readonly Dictionary<VehicleCombat, int> _cache = new Dictionary<VehicleCombat, int>();
    float _garageAt = -1f;
    float _incomeTimer;
    float _nextCache;

    // ---------------- 对外只读接口 ----------------

    public UnitState Human => _human;
    public int Gold => _human != null ? _human.gold : 0;

    /// <summary>商店买东西：够钱就扣，返回是否成功。</summary>
    public bool TrySpend(int amount)
    {
        if (_human == null) return false;
        if (amount <= 0) return true;
        if (_human.gold < amount) return false;
        _human.gold -= amount;
        return true;
    }
    public int LivesLeft => _human != null ? _human.lives : 0;
    public VehicleCombat Player => _human != null ? _human.vehicle : null;
    public VehicleDef[] Roster => match != null ? match.roster : null;

    public int TeamLives(int team)
    {
        int n = 0;
        foreach (var u in units) if (u.team == team) n += Mathf.Max(0, u.lives);
        return n;
    }

    public int TeamAlive(int team)
    {
        int n = 0;
        foreach (var u in units) if (u.team == team && u.HasVehicle) n++;
        return n;
    }

    public int EnemiesLeft => TeamAlive(1);

    public int CheapestPrice
    {
        get
        {
            int min = int.MaxValue;
            var r = Roster;
            if (r != null) foreach (var d in r) if (d != null) min = Mathf.Min(min, d.price);
            return min == int.MaxValue ? 0 : min;
        }
    }

    public bool CanAfford(VehicleDef def)
    {
        if (def == null || _human == null) return false;
        if (!unlimitedLives && _human.lives <= 0) return false;
        if (_human.gold >= def.price) return true;
        return _human.gold < CheapestPrice && def.price == CheapestPrice;   // 一穷二白时最低价免费
    }

    /// <summary>命数显示文案（不限命数时显示 ∞）。</summary>
    public string LivesText => unlimitedLives ? "∞" : LivesLeft.ToString();

    // ---------------- 生命周期 ----------------

    void Awake()
    {
        Instance = this;
        LoadDefaults();
    }

    void LoadDefaults()
    {
#if UNITY_EDITOR
        if (match == null)
            match = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchDef>("Assets/Data/Match/Match_Default.asset");
        if (battlefield == null)
            battlefield = UnityEditor.AssetDatabase.LoadAssetAtPath<BattlefieldDef>("Assets/Data/Battlefield/Battlefield_Default.asset");
#endif
        if (match == null) match = ScriptableObject.CreateInstance<MatchDef>();
        if (battlefield == null) battlefield = ScriptableObject.CreateInstance<BattlefieldDef>();
    }

    void Start()
    {
        timeLeft = match.matchTimeLimitMinutes * 60f;
        MatchSettings.MatchElapsed = 0f;                 // 2026-11-11：海/空解锁计时从 0 开始
        VehicleAI.Skill = MatchSettings.AiSkill;         // AI 难度（游戏调整里选）

        zones.Clear();
        if (battlefield.zones != null)
            foreach (var z in battlefield.zones)
                if (z != null) zones.Add(new ZoneState { def = z });
        ZoneMarker.BuildAll(zones);

        BuildUnits();
        BattleEvents.VehicleDestroyed += OnVehicleDestroyed;

        // AI 立刻进场（你不用等他们）；海/空的单位要等解锁时间到了才出场
        foreach (var u in units)
        {
            if (u.isHuman) continue;

            var dom = DomainOf(u);
            float wait = MatchSettings.UnlockIn(dom);
            if (wait > 0f)
            {
                // 还没解锁：挂成"待出场"，由 UpdateRespawns 每拍检查解锁，到点补上且不扣命
                u.firstSpawnPending = true;
                u.respawnAt = -1f;
                continue;
            }
            SpawnUnit(u, PickDef(u));
        }

        int rosterCount = Roster != null ? Roster.Length : 0;
        Debug.Log($"[MatchManager] 自检 roster={rosterCount} zones={zones.Count} allyX={battlefield.allySpawnLineX:F0} " +
                  $"units={units.Count} lives={match.playerLives} gold={MatchSettings.StartingGold}");
        if (rosterCount == 0) Debug.LogError("[MatchManager] roster 为空 —— MatchDef 没序列化进场景");
        if (zones.Count == 0) Debug.LogError("[MatchManager] zones 为空 —— BattlefieldDef 没序列化进场景");

        int a = TeamCount(0), e = TeamCount(1);
        string unlockNote = MatchSettings.UnlockMinutes > 0
            ? $" · 海空 {MatchSettings.UnlockMinutes} 分钟后开放"
            : "";
        PushFeed($"我方 {a} 台 vs 敌方 {e} 台 · 每人 {match.playerLives} 条命{unlockNote}");
    }

    void OnDestroy() => BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;

    void BuildUnits()
    {
        units.Clear();
        _map.Clear();
        _human = null;

        // 2026-11-11：人数不再写死 5v5，改成「游戏调整」里按类型配（只算 AI，玩家另算一台）。
        for (int team = 0; team < 2; team++)
        {
            var ids = MatchSettings.BuildAiRoster(team);

            // 玩家自己占 team0 的 0 号位，车由车库选
            if (team == 0)
            {
                var me = new UnitState
                {
                    team = 0,
                    index = 0,
                    isHuman = true,
                    gold = MatchSettings.StartingGold,
                    lives = Mathf.Max(1, match.playerLives),
                };
                units.Add(me);
                _human = me;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                var u = new UnitState
                {
                    team = team,
                    index = i + (team == 0 ? 1 : 0),   // team0 的 AI 从 1 号开始编号，别和"你"撞号
                    isHuman = false,
                    gold = MatchSettings.StartingGold,
                    lives = Mathf.Max(1, match.playerLives),
                    prefId = ids[i],
                };
                units.Add(u);
            }
        }

        teamSize = Mathf.Max(TeamCount(0), TeamCount(1));
        Debug.Log($"[MatchManager] 人数配置：我方 {TeamCount(0)} 台 / 敌方 {TeamCount(1)} 台（含你）");
    }

    /// <summary>某一方总台数（含玩家）。</summary>
    int TeamCount(int team)
    {
        int n = 0;
        foreach (var u in units) if (u.team == team) n++;
        return n;
    }

    // ---------------- 每帧 ----------------

    void Update()
    {
        if (!GameStartScreen.Started) return;

        if (matchOver)
        {
            if (Bindings.Down(Bindings.Act.Restart)) Restart();
            return;
        }

        if (_garageAt > 0f && Time.unscaledTime >= _garageAt)
        {
            _garageAt = -1f;
            OpenGarageForRespawn();
        }

        timeLeft -= Time.deltaTime;
        // 2026-11-11：海/空解锁计时（开局只玩坦克，到点才放海空）
        MatchSettings.MatchElapsed = Mathf.Max(0f, match.matchTimeLimitMinutes * 60f - timeLeft);
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            EndByLives("时间到");
            return;
        }

        UpdateZones(Time.deltaTime);
        UpdateIncome(Time.deltaTime);
        UpdateRespawns();
        CheckTeamWipe();

        // 无限金币（测试用）：每帧顶满，换车扣了也立刻补回来
        if (unlimitedGold && _human != null) _human.gold = debugGold;
    }

    void RefreshCache()
    {
        if (Time.time < _nextCache) return;
        _nextCache = Time.time + 0.4f;
        _cache.Clear();
        foreach (var v in FindObjectsByType<VehicleCombat>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (v != null) _cache[v] = v.Team;
    }

    // ---------------- 出击 / 复活 ----------------

    VehicleDef PickDef(UnitState u)
    {
        // 2026-11-11：单位开什么车由「游戏调整」里的人数配置决定（BuildUnits 写进 prefId）。
        // 只有没配的情况才退回旧的平均分配逻辑。
        string id = u.prefId;
        if (string.IsNullOrEmpty(id))
        {
            string[] ids = u.team == 0
                ? new[] { "land_main", "land_heavy", "sea_gunboat", "air_fighter" }
                : new[] { "land_main", "land_heavy", "sea_gunboat", "air_fighter", "air_attacker" };
            id = ids[u.index % ids.Length];
        }

        var r = Roster;
        if (r != null) foreach (var d in r) if (d != null && d.id == id) return d;
        return r != null && r.Length > 0 ? r[0] : null;
    }

    /// <summary>这个单位的车属于哪个域（用来判断海/空有没有解锁）。</summary>
    static Domain DomainOf(UnitState u)
    {
        switch (u.prefId)
        {
            case MatchSettings.IdShip: return Domain.Sea;
            case MatchSettings.IdFighter:
            case MatchSettings.IdAttacker: return Domain.Air;
            default: return Domain.Land;
        }
    }

    VehicleCombat SpawnUnit(UnitState u, VehicleDef def)
    {
        if (u == null || def == null || u.lives <= 0) return null;

        Vector3 pos = FindSpawnPoint(u.team == 0, def.domain, u.index);
        float yaw = u.team == 0 ? 90f : -90f;

        var v = VehicleFactory.Spawn(def, u.team, pos, yaw, u.isHuman);
        if (v == null) return null;

        u.vehicle = v;
        u.lastDef = def;
        u.respawnAt = -1f;
        _map[v] = u;

        if (u.isHuman)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var cf = cam.GetComponent<CameraFollow>();
                if (cf != null) { cf.target = v.transform; cf.ResetToBehindTarget(); }
            }
        }
        return v;
    }

    /// <summary>开局：进车库选第一台车。</summary>
    public void OpenGarageFirst()
    {
        OpenGarage("选择你的载具", $"命数 {LivesText} · 你的金币 {Gold}");
    }

    void OpenGarageForRespawn()
    {
        if (matchOver) return;
        if (!unlimitedLives && LivesLeft <= 0)
        {
            EndMatch(1, "你的 3 条命用完了");
            return;
        }
        OpenGarage("载具被击毁 —— 重新选择", $"还剩 {LivesText} 次部署 · 你的金币 {Gold}");
    }

    /// <summary>测试用：按 G 随时回车库换车，不用等阵亡。</summary>
    public void OpenGarageForSwap()
    {
        if (matchOver) return;
        var g = GarageScreen.Instance;
        if (g == null) return;
        if (g.IsOpen) return;                      // 已经在车库里就别重复开
        OpenGarage("换载具", $"金币 {Gold} · 随时按 G 换车 · 命数 {LivesText}");
    }

    void OpenGarage(string title, string subtitle)
    {
        var g = GarageScreen.Instance;
        if (g == null) { Debug.LogWarning("[MatchManager] 场景里没有 GarageScreen"); return; }
        g.Open(title, subtitle);
    }

    /// <summary>你在车库里点了一台车（花你自己的钱）。</summary>
    public void DeployPlayer(VehicleDef def)
    {
        if (def == null || matchOver || _human == null) return;
        if (!CanAfford(def)) return;

        int cost = _human.gold >= def.price ? def.price : 0;
        _human.gold -= cost;
        if (!unlimitedLives) _human.lives--;

        if (_human.vehicle != null)
        {
            _map.Remove(_human.vehicle);
            Destroy(_human.vehicle.gameObject);
            _human.vehicle = null;
        }

        SpawnUnit(_human, def);
        PushFeed(cost > 0 ? $"你 部署 {def.displayName}  -{cost}金" : $"你 部署 {def.displayName}（免费补充）");

        if (GarageScreen.Instance != null) GarageScreen.Instance.Close();
    }

    void UpdateRespawns()
    {
        foreach (var u in units)
        {
            if (u.isHuman || u.lives <= 0 || u.vehicle != null) continue;

            // 2026-11-11：开局时海/空还没解锁的那批 —— 每拍检查一次解锁状态，
            // 到点就出场，而且**不扣命、不扣钱**（第一次出场不算复活）。
            // 注意这里必须在下面 respawnAt 判断之前：待出场单位的 respawnAt 是 -1。
            if (u.firstSpawnPending)
            {
                var d0 = u.lastDef != null ? u.lastDef : PickDef(u);
                if (d0 != null && !MatchSettings.DomainUnlocked(d0.domain)) continue;
                u.firstSpawnPending = false;
                u.respawnAt = -1f;
                if (SpawnUnit(u, d0) != null)
                    PushFeed($"{u.Label} 进入战场（{(d0 != null ? d0.displayName : "载具")}）");
                continue;
            }

            if (u.respawnAt < 0f || Time.time < u.respawnAt) continue;

            var def = u.lastDef != null ? u.lastDef : PickDef(u);

            // 复活时海/空还没解锁就先等（不会发生：解锁是单向的，这里只是兜底）
            if (def != null && !MatchSettings.DomainUnlocked(def.domain))
            {
                u.respawnAt = Time.time + 2f;
                continue;
            }

            int cost = def != null ? def.price : 0;

            if (u.gold < cost)
            {
                // AI 钱不够：攒钱，过几秒再试
                u.respawnAt = Time.time + 2f;
                continue;
            }

            u.gold -= cost;
            u.lives--;
            if (SpawnUnit(u, def) != null)
                PushFeed($"{u.Label} 重新出击  -{cost}金");
        }
    }

    // ---------------- 出生点 ----------------

    Vector3 FindSpawnPoint(bool ally, Domain domain, int slot)
    {
        float x = ally ? battlefield.allySpawnLineX : battlefield.enemySpawnLineX;
        float zMin = battlefield.spawnLineZMin, zMax = battlefield.spawnLineZMax;

        // 每个单位从不同的 z 起点扫，避免全挤在同一个点上
        float start = 0.5f + (slot - 2) * 0.08f;

        // ---------- 船：必须整条船都在够深的水里 ----------
        // 旧版只检查一个点，而且 80 次扫不到就直接取出生线中点（**完全不查水深**），
        // 于是舰艇重生经常一头扎在沙滩上，动都动不了。
        if (domain == Domain.Sea) return FindSeaSpawn(x, start, zMin, zMax, slot);

        float bestZ = Mathf.Lerp(zMin, zMax, 0.5f);
        bool found = false;

        // 陆/空也要避开已经站着的车，不然开局几台车会叠在一起
        float minDist = domain == Domain.Air ? 60f : 16f;
        for (int i = 0; i < 80; i++)
        {
            float t = start + (i % 2 == 0 ? 1f : -1f) * (i * 0.006f);
            if (t < 0.02f || t > 0.98f) continue;
            float z = Mathf.Lerp(zMin, zMax, t);
            if (!FitsDomain(x, z, domain)) continue;
            // 别生在陡坡上（2026-11-06：边界加了山脉，出生线原来在 ±933 正好落进山里）
            if (domain != Domain.Air && BattlefieldTerrain.SampleSteepness(x, z) > 12f) continue;
            if (!SpawnClear(new Vector3(x, 0f, z), minDist)) continue;
            bestZ = z; found = true; break;
        }
        if (!found)
        {
            // 实在找不到空位：按槽位横向错开，至少别完全重叠
            bestZ = Mathf.Lerp(zMin, zMax, 0.5f) + (slot - 2) * (domain == Domain.Air ? 80f : 18f);
            bestZ = Mathf.Clamp(bestZ, zMin, zMax);
        }

        float ground = BattlefieldTerrain.SampleHeight(x, bestZ);
        float y;
        switch (domain)
        {
            case Domain.Air: y = Mathf.Max(ground, BattlefieldTerrain.WaterLevel) + 220f; break;
            default: y = ground + 1.2f; break;
        }
        return new Vector3(x, y, bestZ);
    }

    /// <summary>
    /// 船的出生点：**整条船**（船体中心 ±10 m 五个采样点）都要 ≥3 m 深。
    /// 出生线整段是浅滩时，退到全图找最深的水域，绝不再往沙滩上放。
    /// </summary>
    Vector3 FindSeaSpawn(float lineX, float start, float zMin, float zMax, int slot)
    {
        float wy = BattlefieldTerrain.WaterLevel + 0.5f;

        // 1) 先沿出生线找（要求够深 **且** 附近没有别的船）
        for (int i = 0; i < 120; i++)
        {
            float t = start + (i % 2 == 0 ? 1f : -1f) * (i * 0.004f);
            if (t < 0.02f || t > 0.98f) continue;
            float z = Mathf.Lerp(zMin, zMax, t);
            if (!SeaFits(lineX, z)) continue;
            if (!SpawnClear(new Vector3(lineX, 0f, z), 26f)) continue;
            return new Vector3(lineX, wy, z);
        }

        // 2) 出生线不行：全图扫一遍找够深、且离别的船最远的水域。
        //    ⚠️ 旧版这里直接返回"全局最深的那个点"——那是个**固定坐标**，
        //    所有船都会拿到同一个点，于是开局两条船叠在一起（用户报的问题）。
        Vector3 best = new Vector3(lineX, wy, Mathf.Lerp(zMin, zMax, 0.5f));
        float bestScore = -999f;
        const float half = 900f;
        for (float gx = -half; gx <= half; gx += 25f)
            for (float gz = -half; gz <= half; gz += 25f)
            {
                float d = BattlefieldTerrain.WaterDepth(gx, gz);
                if (d <= 0f) continue;
                if (!SeaFits(gx, gz)) continue;
                // 得分 = 水深 + 与已有载具的最近距离（距离优先，避免叠船）
                float near = NearestVehicleDist(new Vector3(gx, 0f, gz));
                float score = Mathf.Min(near, 120f) * 10f + d;
                if (score <= bestScore) continue;
                bestScore = score;
                best = new Vector3(gx, wy, gz);
            }

        // 3) 兜底：真的全图都挤，按槽位错开
        if (NearestVehicleDist(best) < 26f)
            best += Vector3.forward * ((slot - 2) * 30f);
        return best;
    }

    /// <summary>离最近的活着的载具有多远（出生点避让用）。</summary>
    static float NearestVehicleDist(Vector3 p)
    {
        float best = 9999f;
        var all = Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None);
        foreach (var v in all)
        {
            if (v == null || !v.Alive) continue;
            float d = Vector3.Distance(new Vector3(v.transform.position.x, 0f, v.transform.position.z),
                                       new Vector3(p.x, 0f, p.z));
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>这个点周围有没有别的载具（出生避让）。</summary>
    static bool SpawnClear(Vector3 p, float minDist) => NearestVehicleDist(p) >= minDist;

    /// <summary>这个点能不能放船：船体中心 + 前后左右各 10 m 都要 ≥3 m 深。</summary>
    static bool SeaFits(float x, float z)
    {
        const float halfLen = 10f;
        if (BattlefieldTerrain.WaterDepth(x, z) < 3.0f) return false;
        if (BattlefieldTerrain.WaterDepth(x + halfLen, z) < 3.0f) return false;
        if (BattlefieldTerrain.WaterDepth(x - halfLen, z) < 3.0f) return false;
        if (BattlefieldTerrain.WaterDepth(x, z + halfLen) < 3.0f) return false;
        if (BattlefieldTerrain.WaterDepth(x, z - halfLen) < 3.0f) return false;
        return true;
    }

    static bool FitsDomain(float x, float z, Domain domain)
    {
        if (domain == Domain.Air) return true;
        float ground = BattlefieldTerrain.SampleHeight(x, z);
        float depth = BattlefieldTerrain.WaterDepth(x, z);
        if (domain == Domain.Sea) return depth >= 2.5f;
        return depth <= 0f && BattlefieldTerrain.SampleSteepness(x, z) < 18f;
    }

    // ---------------- 资源点 ----------------

    void UpdateZones(float dt)
    {
        RefreshCache();
        foreach (var z in zones)
        {
            int team0 = 0, team1 = 0;
            foreach (var kv in _cache)
            {
                var v = kv.Key;
                if (v == null || !v.Alive || !z.CanBeCapturedBy(v)) continue;
                if (!z.Contains(v.transform.position)) continue;
                if (v.Team == 0) team0++; else if (v.Team == 1) team1++;
            }

            int capping = (team0 > 0 && team1 == 0) ? 0 : (team1 > 0 && team0 == 0) ? 1 : -1;

            if (capping < 0)
            {
                z.progress = Mathf.MoveTowards(z.progress, 0f, dt / Mathf.Max(0.01f, captureSeconds));
                if (z.progress <= 0f) z.progressTeam = -1;
                continue;
            }

            if (z.owner == capping) { z.progress = 1f; z.progressTeam = capping; continue; }
            if (z.progressTeam != capping) { z.progressTeam = capping; z.progress = 0f; }

            z.progress += dt / Mathf.Max(0.01f, captureSeconds);
            if (z.progress >= 1f)
            {
                z.progress = 1f;
                int old = z.owner;
                z.owner = capping;
                BattleEvents.RaiseZoneCaptured(z);
                if (old != capping)
                    PushFeed($"{z.DisplayName} 被{(capping == 0 ? "我方" : "敌方")}占领");
            }
        }
    }

    /// <summary>资源点产金是**全队加成**：占着的点每 tick 给该队每个活着的单位各发一份。</summary>
    void UpdateIncome(float dt)
    {
        _incomeTimer += dt;
        if (_incomeTimer < match.captureInterval) return;
        _incomeTimer -= match.captureInterval;

        int income0 = 0, income1 = 0;
        foreach (var z in zones)
        {
            if (z.owner < 0 || z.def == null) continue;
            if (z.owner == 0) income0 += z.def.incomePerTick;
            else income1 += z.def.incomePerTick;
        }

        if (income0 > 0) foreach (var u in units) if (u.team == 0 && !u.Eliminated) u.gold += income0;
        if (income1 > 0) foreach (var u in units) if (u.team == 1 && !u.Eliminated) u.gold += income1;
    }

    // ---------------- 击杀 / 胜负 ----------------

    void OnVehicleDestroyed(VehicleCombat victim, VehicleCombat killer)
    {
        if (victim == null) return;

        if (_map.TryGetValue(victim, out var victimUnit)) _map.Remove(victim);
        else victimUnit = null;

        if (victimUnit != null)
        {
            victimUnit.vehicle = null;

            if (victimUnit.isHuman)
            {
                PushFeed("你的载具被击毁");
                _garageAt = Time.unscaledTime + garageDelay;
            }
            else if (victimUnit.lives > 0)
            {
                victimUnit.respawnAt = Time.time + aiRespawnDelay;
            }
        }

        // 击杀奖励归**击杀者个人**
        if (killer != null && _map.TryGetValue(killer, out var killerUnit) && killerUnit != victimUnit)
        {
            int reward = Mathf.RoundToInt((victim.Def != null ? victim.Def.price : 100) * match.killRewardRate);
            killerUnit.gold += reward;
            kills[Mathf.Clamp(killerUnit.team, 0, 1)]++;
            PushFeed($"{killerUnit.Label} 击毁 {victim.name}  +{reward}");
        }
        else
        {
            PushFeed($"{victim.name} 被摧毁");
        }

        CheckTeamWipe();
    }

    void CheckTeamWipe()
    {
        if (matchOver) return;
        if (TeamLives(1) <= 0 && TeamAlive(1) == 0) { EndMatch(0, "敌方全部出局"); return; }
        if (TeamLives(0) <= 0 && TeamAlive(0) == 0) { EndMatch(1, "我方全部出局"); return; }
        if (!unlimitedLives && _human != null && _human.lives <= 0 && _human.vehicle == null)
            EndMatch(1, "你的 3 条命用完了");
    }

    void EndByLives(string reason)
    {
        int a = TeamLives(0), e = TeamLives(1);
        if (a == e) EndMatch(0, reason + " · 平局判我方胜");
        else EndMatch(a > e ? 0 : 1, reason + " · 比剩余命数");
    }

    public void EndMatch(int winner, string reason)
    {
        if (matchOver) return;
        matchOver = true;
        winnerTeam = winner;
        resultText = $"{reason} · {(winner == 0 ? "我方胜利" : "我方失败")}";
        PushFeed(resultText);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Restart()
    {
        matchOver = false;
        BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------------- 工具 ----------------

    public void PushFeed(string text)
    {
        feed.Add(new FeedEntry { text = text, time = Time.time });
        while (feed.Count > 6) feed.RemoveAt(0);
    }

    public string TimeText
    {
        get
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(timeLeft));
            return $"{t / 60:00}:{t % 60:00}";
        }
    }
}
