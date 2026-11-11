using UnityEngine;

/// <summary>
/// 一个参战单位（一个人）。5v5 = 双方各 5 个 UnitState。
///
/// 经济是**个人**的：金币归自己，买车花自己的钱，击杀奖励进自己口袋。
/// 唯一共享的是资源点产金 —— 占下来的点每 10 秒给**全队每人**发一份。
/// 每个人最多 playerLives 条命，用光就出局。
/// </summary>
public class UnitState
{
    public int team;
    public int index;
    public bool isHuman;

    public int gold;
    public int lives;

    public VehicleCombat vehicle;
    public VehicleDef lastDef;
    public float respawnAt = -1f;

    /// <summary>
    /// 2026-11-11：开局生成用。海/空载具要等解锁，这类单位先挂"还没第一次出场"，
    /// 到时间由 UpdateRespawns 补上，而且**不扣命**（第一次出场不算复活）。
    /// </summary>
    public bool firstSpawnPending;

    /// <summary>这一号位默认开什么车（MatchSettings 里按类型配的数量决定，空 = 按域随机）。</summary>
    public string prefId;

    public bool HasVehicle => vehicle != null && vehicle.Alive;
    /// <summary>命用光且场上没车 = 出局。</summary>
    public bool Eliminated => lives <= 0 && !HasVehicle;

    public string Label => isHuman ? "你" : $"{(team == 0 ? "我方" : "敌方")}{index + 1}号";
}
