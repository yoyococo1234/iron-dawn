using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 对局主控。
///
/// 核心规则（2026-10-31 改版）：
///   · 玩家有 playerLives 条命 = 可以部署 playerLives 次，每次阵亡回车库重新选车（花金币买）
///   · 敌方有 enemyLives 台载具，打光就赢；玩家命数用光就输
///   · 资源点占着每 captureInterval 秒产金，金币用来买车
///   · 击杀奖励：击毁敌方载具按目标车价 × killRewardRate 发钱
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
    [Tooltip("阵亡后多久弹出车库")]
    public float garageDelay = 2.5f;

    [Header("运行时状态（只读，给 HUD 用）")]
    public int[] gold = new int[2];
    public int livesLeft;
    public int enemiesLeft;
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

    VehicleCombat _player;
    float _garageAt = -1f;
    float _incomeTimer;
    bool _started;

    public VehicleCombat Player => _player;
    public VehicleDef[] Roster => match != null ? match.roster : null;

    /// <summary>车库里最便宜的一台的价格（兜底免费补充用）。</summary>
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

    /// <summary>这台车现在买得起吗（一穷二白时最低价那台免费，避免卡死在车库）。</summary>
    public bool CanAfford(VehicleDef def)
    {
        if (def == null || livesLeft <= 0) return false;
        if (gold[0] >= def.price) return true;
        return gold[0] < CheapestPrice && def.price == CheapestPrice;
    }

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
        livesLeft = Mathf.Max(1, match.playerLives);
        timeLeft = match.matchTimeLimitMinutes * 60f;

        zones.Clear();
        if (battlefield.zones != null)
            foreach (var z in battlefield.zones)
                if (z != null) zones.Add(new ZoneState { def = z });

        ZoneMarker.BuildAll(zones);
        BattleEvents.VehicleDestroyed += OnVehicleDestroyed;

        CountEnemies();
        PushFeed($"敌方 {enemiesLeft} 台载具 · 你还有 {livesLeft} 条命");

        // 启动自检：打包后最容易出问题的是「数据资产没序列化进场景」
        int rosterCount = Roster != null ? Roster.Length : 0;
        Debug.Log($"[MatchManager] 启动自检 roster={rosterCount} zones={zones.Count} " +
                  $"allyX={battlefield.allySpawnLineX:F0} startingGold={match.startingGold} lives={livesLeft}");
        if (rosterCount == 0) Debug.LogError("[MatchManager] roster 为空 —— MatchDef 没序列化进场景，车库会没有车可选");
        if (zones.Count == 0) Debug.LogError("[MatchManager] zones 为空 —— BattlefieldDef 没序列化进场景");
    }

    void OnDestroy() => BattleEvents.VehicleDestroyed -= OnVehicleDestroyed;

    void CountEnemies()
    {
        enemiesLeft = 0;
        foreach (var c in FindObjectsByType<VehicleCombat>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c != null && c.Team == 1) enemiesLeft++;
    }

    void Update()
    {
        if (!GameStartScreen.Started) return;
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (matchOver) return;

        // 阵亡后延迟弹车库
        if (_garageAt > 0f && Time.unscaledTime >= _garageAt)
        {
            _garageAt = -1f;
            OpenGarageForRespawn();
        }

        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            EndMatch(enemiesLeft <= 0 ? 0 : 1, "时间到");
            return;
        }

        UpdateZones(Time.deltaTime);
        UpdateIncome(Time.deltaTime);
    }

    // ==================== 车库 / 部署 ====================

    /// <summary>开局：进车库选第一台车。</summary>
    public void OpenGarageFirst()
    {
        _started = true;
        OpenGarage("选择你的载具", $"还剩 {livesLeft} 次部署机会 · 金币 {gold[0]}");
    }

    void OpenGarageForRespawn()
    {
        if (matchOver) return;
        if (livesLeft <= 0)
        {
            EndMatch(1, "你的载具全部损失");
            return;
        }
        OpenGarage("载具被击毁 —— 重新选择", $"还剩 {livesLeft} 次部署机会 · 金币 {gold[0]}");
    }

    void OpenGarage(string title, string subtitle)
    {
        var g = GarageScreen.Instance;
        if (g == null) { Debug.LogWarning("[MatchManager] 场景里没有 GarageScreen"); return; }
        g.Open(title, subtitle);
    }

    /// <summary>玩家在车库里点了一台车。</summary>
    public void DeployPlayer(VehicleDef def)
    {
        if (def == null || matchOver) return;
        if (!CanAfford(def)) return;

        int cost = gold[0] >= def.price ? def.price : 0;   // 买不起时最低价那台免费
        gold[0] -= cost;
        livesLeft--;

        Vector3 pos = battlefield.GetSpawnPoint(true);
        pos.y = BattlefieldTerrain.SampleHeight(pos.x, pos.z) + 1f;

        // 清掉上一台残骸
        if (_player != null) Destroy(_player.gameObject);

        _player = VehicleFactory.Spawn(def, 0, pos, 90f, true);
        PushFeed(cost > 0 ? $"部署 {def.displayName}  -{cost}金" : $"部署 {def.displayName}（免费补充）");

        var cam = Camera.main;
        if (cam != null)
        {
            var cf = cam.GetComponent<CameraFollow>();
            if (cf != null) { cf.target = _player.transform; cf.ResetToBehindTarget(); }
        }

        if (GarageScreen.Instance != null) GarageScreen.Instance.Close();
    }

    // ==================== 资源点 ====================

    VehicleCombat[] _cache = new VehicleCombat[0];
    float _nextCache;

    void RefreshCache()
    {
        if (Time.time < _nextCache) return;
        _nextCache = Time.time + 0.4f;
        _cache = FindObjectsByType<VehicleCombat>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    void UpdateZones(float dt)
    {
        RefreshCache();
        foreach (var z in zones)
        {
            int team0 = 0, team1 = 0;
            foreach (var v in _cache)
            {
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

    // ==================== 击杀 / 胜负 ====================

    void OnVehicleDestroyed(VehicleCombat victim, VehicleCombat killer)
    {
        if (victim == null) return;

        if (victim.Team == 1)
        {
            enemiesLeft = Mathf.Max(0, enemiesLeft - 1);
            if (killer != null && killer.Team == 0)
            {
                int reward = Mathf.RoundToInt((victim.Def != null ? victim.Def.price : 100) * match.killRewardRate);
                gold[0] += reward;
                PushFeed($"击毁 {victim.name}  +{reward}金　敌方剩 {enemiesLeft} 台");
            }
            else PushFeed($"{victim.name} 被摧毁　敌方剩 {enemiesLeft} 台");

            if (enemiesLeft <= 0) EndMatch(0, "敌方载具全部击毁");
        }
        else
        {
            // 玩家阵亡
            PushFeed(victim.name + " 被击毁");
            _garageAt = Time.unscaledTime + garageDelay;
            if (_player != null && _player == victim) _player = null;
        }
    }

    // ==================== 结算 / 重开 ====================

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

    // ==================== 工具 ====================

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
