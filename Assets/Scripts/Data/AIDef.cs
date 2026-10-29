using UnityEngine;

/// <summary>
/// AI 参数。核心约定（2026-10-29 定）：
/// AI 只打同域目标；不做跨域仇恨。
/// 但会"还击"——谁打我，我就打谁（否则玩家能单方面白嫖 AI）。
/// </summary>
[CreateAssetMenu(fileName = "AI_", menuName = "铁皮黎明/AI 参数", order = 6)]
public class AIDef : ScriptableObject
{
    [Header("感知")]
    [Tooltip("能看见多远")]
    public float visionRange = 120f;
    [Tooltip("进入这个距离才开火")]
    public float fireRange = 90f;
    [Tooltip("多久重新思考一次（秒），越大越省性能")]
    public float thinkInterval = 0.5f;

    [Header("战斗")]
    [Tooltip("血量低于该比例就撤退")]
    public float retreatHpPercent = 0.30f;
    [Tooltip("命中率（不百发百中，避免太致命）")]
    public float accuracy = 0.70f;
    [Tooltip("追击倾向 0~1，越大越爱追")]
    public float aggression = 0.60f;

    [Header("跨域（默认关）")]
    [Tooltip("AI 是否会主动去找其他域的敌人。默认关：只打同域。")]
    public bool crossDomainHunting = false;
    [Tooltip("是否还击攻击自己的跨域敌人（建议开，否则玩家能白嫖）")]
    public bool crossDomainRetaliation = true;
    [Tooltip("记住攻击者的时长（秒）")]
    public float retaliationMemorySeconds = 5f;

    [Header("经济决策")]
    [Tooltip("钱包达到 车价×该值 时才考虑换车")]
    public float swapGoldThreshold = 1.2f;
    [Tooltip("血量低于该比例才考虑维修")]
    public float repairHpPercent = 0.40f;
}
