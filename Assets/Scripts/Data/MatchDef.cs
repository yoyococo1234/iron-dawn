using UnityEngine;

/// <summary>
/// 对局与经济参数。整局手感主要由 respawnGrowth 和 swapGrowth 两个系数决定。
/// </summary>
[CreateAssetMenu(fileName = "Match_", menuName = "铁皮黎明/对局与经济", order = 4)]
public class MatchDef : ScriptableObject
{
    [Header("规模")]
    [Tooltip("每队台数（含玩家）")]
    public int teamSize = 5;

    [Header("开局")]
    public int startingGold = 200;

    [Header("复活（递增）")]
    [Tooltip("复活基础费")]
    public int respawnBase = 80;
    [Tooltip("每复活一次，费用乘这个数。1.22 ≈ 9分钟一局，1.12 ≈ 15分钟")]
    public float respawnGrowth = 1.22f;

    [Header("换车（递增）")]
    [Tooltip("每换一次车，该玩家的车价乘这个数")]
    public float swapGrowth = 1.15f;
    [Tooltip("车价最多涨到几倍")]
    public float swapGrowthCap = 3f;

    [Header("收入（固定，不随通胀）")]
    [Tooltip("击杀奖励 = 目标载具基础价 × 该比例")]
    public float killRewardRate = 0.40f;
    public float assistRewardRate = 0.20f;

    [Header("资源点")]
    public int captureIncome = 15;
    [Tooltip("每多少秒结算一次")]
    public float captureInterval = 10f;

    [Header("存活收入")]
    public int survivalIncome = 5;
    public float survivalInterval = 10f;

    [Header("金币道具")]
    public int coinBoxValue = 50;
    public float coinBoxInterval = 25f;
    public int moneyBagValue = 25;

    [Header("维修与弹药")]
    [Tooltip("每恢复 1% 血量的金币")]
    public int repairCostPerPercent = 1;
    [Tooltip("补满弹药的金币")]
    public int ammoRefillCost = 20;

    [Header("对局")]
    public float matchTimeLimitMinutes = 15f;

    /// <summary>第 n 次复活的费用（n 从 0 开始）。</summary>
    public int RespawnCost(int timesRespawned, int vehicleRespawnBase)
        => Mathf.RoundToInt(vehicleRespawnBase * Mathf.Pow(respawnGrowth, timesRespawned));

    /// <summary>换车时的涨价倍率。</summary>
    public float SwapPriceMultiplier(int timesSwapped)
        => Mathf.Min(Mathf.Pow(swapGrowth, timesSwapped), swapGrowthCap);
}
