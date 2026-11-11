using UnityEngine;

/// <summary>
/// 开局前「游戏调整」里的设置，全部走 PlayerPrefs 存档，重开游戏还在。
///
/// 为什么不用 MatchDef 资产存：那是 ScriptableObject，运行时改它会把改动写进 Assets（编辑器里
/// 会变成脏资产、build 里又不持久）。**玩家设置一律走这里**，对局开始时再读。
///
/// 约定：
///   - 人数只算 AI，玩家自己另算一台；
///   - 海/空有解锁时间（开局只玩坦克），飞机另有"一开始就开放"的开关。
/// </summary>
public static class MatchSettings
{
    // ---------------- 对局 ----------------

    /// <summary>初始金币。</summary>
    public static int StartingGold = 200;

    /// <summary>开局就能用飞机（默认关）。关着的时候飞机跟着 unlockMinutes 一起解锁。</summary>
    public static bool AirFromStart = false;

    /// <summary>开局多少分钟后海/空载具才开放。0 = 一开始就全开。</summary>
    public static int UnlockMinutes = 3;

    // ---------------- 敌我人数（只算 AI） ----------------

    public static int AllyShips = 1;
    public static int AllyLight = 0;
    public static int AllyMain = 2;
    public static int AllyHeavy = 1;
    public static int AllyAir = 1;

    public static int EnemyShips = 1;
    public static int EnemyLight = 0;
    public static int EnemyMain = 1;
    public static int EnemyHeavy = 1;
    public static int EnemyAir = 2;

    // ---------------- AI 强度 ----------------

    /// <summary>AI 技能系数：0.55 很菜 / 0.75 偏弱（默认）/ 1.0 原版（几乎枪枪中）。</summary>
    public static float AiSkill = 0.75f;

    /// <summary>三档难度，界面上直接给这三个。</summary>
    public static readonly float[] SkillSteps = { 0.55f, 0.75f, 1.0f };
    public static readonly string[] SkillNames = { "轻松", "普通", "原版（很凶）" };

    // ---------------- 人数上限 ----------------

    public const int MaxPerType = 6;

    // ---------------- 载具 id 映射 ----------------

    public const string IdShip = "sea_gunboat";
    public const string IdLight = "land_light";
    public const string IdMain = "land_main";
    public const string IdHeavy = "land_heavy";
    public const string IdFighter = "air_fighter";
    public const string IdAttacker = "air_attacker";

    /// <summary>某一方某类型几台（只算 AI）。</summary>
    public static int CountOf(int team, string id)
    {
        bool ally = team == 0;
        switch (id)
        {
            case IdShip: return ally ? AllyShips : EnemyShips;
            case IdLight: return ally ? AllyLight : EnemyLight;
            case IdMain: return ally ? AllyMain : EnemyMain;
            case IdHeavy: return ally ? AllyHeavy : EnemyHeavy;
            case IdFighter:
            case IdAttacker: return ally ? AllyAir : EnemyAir;
            default: return 0;
        }
    }

    /// <summary>某一方 AI 总台数。</summary>
    public static int AiCount(int team)
        => CountOf(team, IdShip) + CountOf(team, IdLight) + CountOf(team, IdMain)
         + CountOf(team, IdHeavy) + CountOf(team, IdFighter);

    /// <summary>
    /// 生成这一方 AI 的载具 id 队列（开局按这个顺序出场）。
    /// 顺序刻意打散：先主战、再重装、再海、再空，避免开局全挤在同一类。
    /// </summary>
    public static System.Collections.Generic.List<string> BuildAiRoster(int team)
    {
        var list = new System.Collections.Generic.List<string>();
        void Add(string id, int n) { for (int i = 0; i < n; i++) list.Add(id); }

        // 陆战打头（开局只有陆战能动），海空排后面
        Add(IdMain, CountOf(team, IdMain));
        Add(IdHeavy, CountOf(team, IdHeavy));
        Add(IdLight, CountOf(team, IdLight));
        Add(IdShip, CountOf(team, IdShip));
        Add(IdFighter, CountOf(team, IdFighter));
        return list;
    }

    // ---------------- 解锁 ----------------

    /// <summary>这一局已经打了多少秒（由 MatchManager 每帧写）。</summary>
    public static float MatchElapsed;

    /// <summary>
    /// 某个域的载具现在开不开放。
    /// 2026-11-11 用户澄清：**只要不开空战，海战照常开放**。
    /// 所以陆/海恒开放，只有飞机受开关和倒计时限制。
    /// </summary>
    public static bool DomainUnlocked(Domain d)
    {
        if (d != Domain.Air) return true;          // 陆战、海战：随时可用
        if (AirFromStart) return true;             // 飞机：开局就开放（默认关）
        if (UnlockMinutes <= 0) return true;       // 倒计时设 0 = 立刻开放
        return MatchElapsed >= UnlockMinutes * 60f;
    }

    /// <summary>还要多久解锁（秒），已经开放返回 0。</summary>
    public static float UnlockIn(Domain d)
    {
        if (DomainUnlocked(d)) return 0f;
        return Mathf.Max(0f, UnlockMinutes * 60f - MatchElapsed);
    }

    // ---------------- 存读 ----------------

    const string K = "Tank3D.Settings.";

    public static void Load()
    {
        StartingGold = PlayerPrefs.GetInt(K + "gold", 200);
        AirFromStart = PlayerPrefs.GetInt(K + "airFromStart", 0) != 0;
        UnlockMinutes = PlayerPrefs.GetInt(K + "unlockMin", 3);

        AllyShips = PlayerPrefs.GetInt(K + "aShip", 1);
        AllyLight = PlayerPrefs.GetInt(K + "aLight", 0);
        AllyMain = PlayerPrefs.GetInt(K + "aMain", 2);
        AllyHeavy = PlayerPrefs.GetInt(K + "aHeavy", 1);
        AllyAir = PlayerPrefs.GetInt(K + "aAir", 1);

        EnemyShips = PlayerPrefs.GetInt(K + "eShip", 1);
        EnemyLight = PlayerPrefs.GetInt(K + "eLight", 0);
        EnemyMain = PlayerPrefs.GetInt(K + "eMain", 1);
        EnemyHeavy = PlayerPrefs.GetInt(K + "eHeavy", 1);
        EnemyAir = PlayerPrefs.GetInt(K + "eAir", 2);

        AiSkill = PlayerPrefs.GetFloat(K + "ai", 0.75f);
        Clamp();
    }

    public static void Save()
    {
        Clamp();
        PlayerPrefs.SetInt(K + "gold", StartingGold);
        PlayerPrefs.SetInt(K + "airFromStart", AirFromStart ? 1 : 0);
        PlayerPrefs.SetInt(K + "unlockMin", UnlockMinutes);

        PlayerPrefs.SetInt(K + "aShip", AllyShips);
        PlayerPrefs.SetInt(K + "aLight", AllyLight);
        PlayerPrefs.SetInt(K + "aMain", AllyMain);
        PlayerPrefs.SetInt(K + "aHeavy", AllyHeavy);
        PlayerPrefs.SetInt(K + "aAir", AllyAir);

        PlayerPrefs.SetInt(K + "eShip", EnemyShips);
        PlayerPrefs.SetInt(K + "eLight", EnemyLight);
        PlayerPrefs.SetInt(K + "eMain", EnemyMain);
        PlayerPrefs.SetInt(K + "eHeavy", EnemyHeavy);
        PlayerPrefs.SetInt(K + "eAir", EnemyAir);

        PlayerPrefs.SetFloat(K + "ai", AiSkill);
        PlayerPrefs.Save();
    }

    public static void ResetAll()
    {
        StartingGold = 200; AirFromStart = false; UnlockMinutes = 3;
        AllyShips = 1; AllyLight = 0; AllyMain = 2; AllyHeavy = 1; AllyAir = 1;
        EnemyShips = 1; EnemyLight = 0; EnemyMain = 1; EnemyHeavy = 1; EnemyAir = 2;
        AiSkill = 0.75f;
        Save();
    }

    static void Clamp()
    {
        StartingGold = Mathf.Clamp(StartingGold, 0, 20000);
        UnlockMinutes = Mathf.Clamp(UnlockMinutes, 0, 15);
        AllyShips = Mathf.Clamp(AllyShips, 0, MaxPerType);
        AllyLight = Mathf.Clamp(AllyLight, 0, MaxPerType);
        AllyMain = Mathf.Clamp(AllyMain, 0, MaxPerType);
        AllyHeavy = Mathf.Clamp(AllyHeavy, 0, MaxPerType);
        AllyAir = Mathf.Clamp(AllyAir, 0, MaxPerType);
        EnemyShips = Mathf.Clamp(EnemyShips, 0, MaxPerType);
        EnemyLight = Mathf.Clamp(EnemyLight, 0, MaxPerType);
        EnemyMain = Mathf.Clamp(EnemyMain, 0, MaxPerType);
        EnemyHeavy = Mathf.Clamp(EnemyHeavy, 0, MaxPerType);
        EnemyAir = Mathf.Clamp(EnemyAir, 0, MaxPerType);
        AiSkill = Mathf.Clamp(AiSkill, 0.3f, 1f);
    }
}
