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

        zones.Clear();
        if (battlefield.zones != null)
            foreach (var z in battlefield.zones)
                if (z != null) zones.Add(new ZoneState { def = z });
        ZoneMarker.BuildAll(zones);

        BuildUnits();
        BattleEvents.VehicleDestroyed += OnVehicleDestroyed;

        // AI 立刻进场（你不用等他们）
        foreach (var u in units)
            if (!u.isHuman) SpawnUnit(u, PickDef(u));

        int rosterCount = Roster != null ? Roster.Length : 0;
        Debug.Log($"[MatchManager] 自检 roster={rosterCount} zones={zones.Count} allyX={battlefield.allySpawnLineX:F0} " +
                  $"units={units.Count} lives={match.playerLives} gold={match.startingGold}");
        if (rosterCount == 0) Debug.LogError("[MatchManager] roster 为空 —— MatchDef 没序列化进场景");
        if (zones.Count == 0) Debug.LogError("[MatchManager] zones 为空 —— BattlefieldDef 没序列化进场景");

        PushFeed($"5v5 · 每人 {match.playerLives} 条命 · 打光对方全部命数获胜");
    }

    void OnDestroy() => BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;

    void BuildUnits()
    {
        units.Clear();
        _map.Clear();
        _human = null;

        for (int team = 0; team < 2; team++)
        {
            for (int i = 0; i < teamSize; i++)
            {
                var u = new UnitState
                {
                    team = team,
                    index = i,
                    isHuman = team == 0 && i == 0,
                    gold = match.startingGold,
                    lives = Mathf.Max(1, match.playerLives),
                };
                units.Add(u);
                if (u.isHuman) _human = u;
            }
        }
    }

    // ---------------- 每帧 ----------------

    void Update()
    {
        if (!GameStartScreen.Started) return;

        if (matchOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }

        if (_garageAt > 0f && Time.unscaledTime >= _garageAt)
        {
            _garageAt = -1f;
            OpenGarageForRespawn();
        }

        timeLeft -= Time.deltaTime;
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
        string[] ids = u.team == 0
            ? new[] { "land_main", "land_heavy", "sea_gunboat", "air_fighter" }
            : new[] { "land_main", "land_heavy", "sea_gunboat", "air_fighter", "air_attacker" };
        string id = ids[u.index % ids.Length];

        var r = Roster;
        if (r != null) foreach (var d in r) if (d != null && d.id == id) return d;
        return r != null && r.Length > 0 ? r[0] : null;
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
            if (u.respawnAt < 0f || Time.time < u.respawnAt) continue;

            var def = u.lastDef != null ? u.lastDef : PickDef(u);
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
        float bestZ = Mathf.Lerp(zMin, zMax, 0.5f);
        bool found = false;

        for (int i = 0; i < 80; i++)
        {
            float t = start + (i % 2 == 0 ? 1f : -1f) * (i * 0.006f);
            if (t < 0.02f || t > 0.98f) continue;
            float z = Mathf.Lerp(zMin, zMax, t);
            if (FitsDomain(x, z, domain)) { bestZ = z; found = true; break; }
        }
        if (!found) bestZ = Mathf.Lerp(zMin, zMax, 0.5f);

        float ground = BattlefieldTerrain.SampleHeight(x, bestZ);
        float y;
        switch (domain)
        {
            case Domain.Air: y = Mathf.Max(ground, BattlefieldTerrain.WaterLevel) + 420f; break;
            case Domain.Sea: y = BattlefieldTerrain.WaterLevel + 0.5f; break;
            default: y = ground + 1.2f; break;
        }
        return new Vector3(x, y, bestZ);
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
