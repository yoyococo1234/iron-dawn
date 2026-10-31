using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 对局主控：把「打一局」需要的东西串起来。
///
///   · 资源点占领  —— 一方独占某个点 captureSeconds 秒就算占下（双方同时在场 = 僵持，进度回退）
///   · 产金        —— 每 captureInterval 秒，每个占着的点给所属队伍 incomePerTick 金
///   · 击杀奖励    —— 击毁敌方载具按目标车价 × killRewardRate 给击杀方发钱
///   · 复活        —— 被击毁 respawnDelay 秒后，队伍付得起复活费就在出兵线复活（费用按次数递增）
///   · 胜负        —— 先到 killTarget 击杀的一方获胜；时间到则击杀多的一方获胜
///   · 重开        —— R 键重载场景
///
/// 挂在场景里的一个空物体上（MapSceneBuilder 会自动放）。
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
    [Tooltip("先打到多少击杀获胜")]
    public int killTarget = 12;
    [Tooltip("被击毁后几秒复活")]
    public float respawnDelay = 6f;

    [Header("运行时状态（只读，给 HUD 用）")]
    public int[] gold = new int[2];
    public int[] kills = new int[2];
    public float timeLeft;
    public bool matchOver;
    public int winnerTeam = -1;
    public string resultText = "";

    public readonly List<ZoneState> zones = new List<ZoneState>();
    public readonly List<FeedEntry> feed = new List<FeedEntry>();

    public class FeedEntry
    {
        public string text;
        public float time;
    }

    VehicleCombat[] _vehicles = new VehicleCombat[0];
    readonly Dictionary<VehicleCombat, float> _deadAt = new Dictionary<VehicleCombat, float>();
    readonly Dictionary<VehicleCombat, int> _respawnCount = new Dictionary<VehicleCombat, int>();
    readonly Dictionary<ZoneState, int> _lastProgressTeam = new Dictionary<ZoneState, int>();

    float _incomeTimer;
    float _nextVehicleRefresh;

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
        gold[0] = gold[1] = match.startingGold;
        timeLeft = match.matchTimeLimitMinutes * 60f;

        zones.Clear();
        if (battlefield.zones != null)
            foreach (var z in battlefield.zones)
                if (z != null) zones.Add(new ZoneState { def = z });

        RefreshVehicles();
        BattleEvents.VehicleDestroyed += OnVehicleDestroyed;

        ZoneMarker.BuildAll(zones);

        PushFeed($"对局开始 · {zones.Count} 个资源点 · 先到 {killTarget} 击杀获胜");
    }

    void OnDestroy() => BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;

    void RefreshVehicles()
    {
        _vehicles = FindObjectsByType<VehicleCombat>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (matchOver) return;

        if (Time.time >= _nextVehicleRefresh) { _nextVehicleRefresh = Time.time + 2f; RefreshVehicles(); }

        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f) { timeLeft = 0f; EndMatch(kills[0] >= kills[1] ? 0 : 1, "时间到"); return; }

        UpdateZones(Time.deltaTime);
        UpdateIncome(Time.deltaTime);
        UpdateRespawns();
    }

    // ==================== 资源点 ====================

    void UpdateZones(float dt)
    {
        foreach (var z in zones)
        {
            int team0 = 0, team1 = 0;
            foreach (var v in _vehicles)
            {
                if (v == null || !v.Alive || !z.CanBeCapturedBy(v)) continue;
                if (!z.Contains(v.transform.position)) continue;
                if (v.Team == 0) team0++; else if (v.Team == 1) team1++;
            }

            // 只有一方独占才算推进；双方都在 = 僵持
            int capping = (team0 > 0 && team1 == 0) ? 0 : (team1 > 0 && team0 == 0) ? 1 : -1;

            if (capping < 0)
            {
                // 没人占 / 僵持 → 进度往回退
                z.progress = Mathf.MoveTowards(z.progress, 0f, dt / Mathf.Max(0.01f, captureSeconds));
                if (z.progress <= 0f) z.progressTeam = -1;
                continue;
            }

            if (z.owner == capping) { z.progress = 1f; z.progressTeam = capping; continue; }

            // 换了一方来占 → 进度清零重来
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

    // ==================== 收入 ====================

    void UpdateIncome(float dt)
    {
        _incomeTimer += dt;
        if (_incomeTimer < match.captureInterval) return;
        _incomeTimer -= match.captureInterval;

        foreach (var z in zones)
        {
            if (z.owner < 0 || z.def == null) continue;
            gold[z.owner] += z.def.incomePerTick;
        }
    }

    // ==================== 击杀 / 复活 ====================

    void OnVehicleDestroyed(VehicleCombat victim, VehicleCombat killer)
    {
        if (victim == null) return;
        _deadAt[victim] = Time.time;

        if (killer != null && killer != victim)
        {
            int t = Mathf.Clamp(killer.Team, 0, 1);
            kills[t]++;
            int reward = Mathf.RoundToInt((victim.Def != null ? victim.Def.price : 100) * match.killRewardRate);
            gold[t] += reward;
            PushFeed($"{(t == 0 ? "我方" : "敌方")} {killer.name} 击毁 {victim.name}  +{reward}金");

            if (kills[t] >= killTarget) EndMatch(t, $"{killTarget} 击杀达成");
        }
        else
        {
            PushFeed($"{victim.name} 被摧毁");
        }
    }

    void UpdateRespawns()
    {
        if (_deadAt.Count == 0) return;

        List<VehicleCombat> ready = null;
        foreach (var kv in _deadAt)
            if (Time.time - kv.Value >= respawnDelay) (ready ??= new List<VehicleCombat>()).Add(kv.Key);
        if (ready == null) return;

        foreach (var v in ready)
        {
            _deadAt.Remove(v);
            if (v == null) continue;

            int team = Mathf.Clamp(v.Team, 0, 1);
            int times = _respawnCount.TryGetValue(v, out var n) ? n : 0;
            int cost = match.RespawnCost(times, v.Def != null ? v.Def.respawnBaseCost : match.respawnBase);

            // 队伍钱不够就继续等（HUD 会显示"等待复活"）
            if (gold[team] < cost) { _deadAt[v] = Time.time; continue; }

            gold[team] -= cost;
            _respawnCount[v] = times + 1;

            Vector3 pos = battlefield.GetSpawnPoint(team == 0);
            pos.y = BattlefieldTerrain.SampleHeight(pos.x, pos.z) + 1f;

            if (v.Def != null && v.Def.domain == Domain.Air)
            {
                float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(pos.x, pos.z), BattlefieldTerrain.WaterLevel);
                pos.y = ground + (v.Physics != null ? v.Physics.flightAltitude : 420f);
            }

            v.RespawnAt(pos, team == 0 ? 90f : -90f);
            PushFeed($"{v.name} 复活  -{cost}金");
        }
    }

    // ==================== 结算 / 重开 ====================

    public void EndMatch(int winner, string reason)
    {
        if (matchOver) return;
        matchOver = true;
        winnerTeam = winner;
        resultText = $"{reason} · {(winner == 0 ? "我方胜利" : "敌方胜利")}";
        PushFeed(resultText);
    }

    public void Restart()
    {
        matchOver = false;
        BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ==================== 工具 ====================

    /// <summary>距离复活还有几秒（HUD 用）。0 = 已可复活 / 没死。</summary>
    public float RespawnEta(VehicleCombat v)
    {
        if (v == null || v.Alive) return 0f;
        if (_deadAt.TryGetValue(v, out var t)) return Mathf.Max(0f, respawnDelay - (Time.time - t));
        return 0f;
    }

    public void PushFeed(string text)
    {
        feed.Add(new FeedEntry { text = text, time = Time.time });
        while (feed.Count > 6) feed.RemoveAt(0);
    }

    /// <summary>玩家控制的载具（HUD 用）。</summary>
    public VehicleCombat Player
    {
        get
        {
            foreach (var v in _vehicles)
                if (v != null && v.GetComponent<PlayerTankController>() != null) return v;
            return null;
        }
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
