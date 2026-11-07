using UnityEngine;

/// <summary>
/// 补给商店（2026-11-07 用户要求，按 **TAB** 开关）。
///
/// 单人游戏，打开时全场暂停（Time.timeScale = 0），买完按 TAB 关掉继续。
/// 商品分两类：
///   通用 —— 维修包（回 30% 血）/ 模块修复包（修好所有部位）/ 弹药补给（补满备弹，含飞机导弹）
///   按载具域 —— 飞机买导弹，炮艇买鱼雷，坦克买炮弹（都走"弹药补给"这一条，按载具自动定价）
/// </summary>
public class ShopScreen : MonoBehaviour
{
    public static bool Open { get; private set; }

    float _savedScale = 1f;
    string _toast = "";
    float _toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ShopScreen");
        go.AddComponent<ShopScreen>();
        Object.DontDestroyOnLoad(go);
    }

    MatchManager MM => MatchManager.Instance;
    VehicleCombat Player => MM != null ? MM.Player : null;

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Tab)) return;

        // 开局封面 / 车库 / 暂停界面里不抢 TAB
        if (PauseScreen.Paused) return;
        var player = Player;
        if (player == null || !player.Alive) return;

        if (!Open) { Open = true; _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f; }
        else { Open = false; Time.timeScale = _savedScale; }
    }

    void OnDisable()
    {
        if (Open) { Open = false; Time.timeScale = 1f; }
    }

    struct Item
    {
        public string name;
        public string desc;
        public int price;
        public System.Action<VehicleCombat> buy;
        public System.Func<VehicleCombat, bool> canBuy;
    }

    Item[] BuildItems(VehicleCombat p)
    {
        string dom = p != null && p.Def != null ? p.Def.domain.ToString() : "?";
        int ammoPrice = dom == "Air" ? 90 : dom == "Sea" ? 70 : 60;
        string ammoName = dom == "Air" ? "导弹 / 机炮补给" : dom == "Sea" ? "炮弹 / 鱼雷补给" : "炮弹补给";

        return new[]
        {
            new Item {
                name = "维修包", desc = "回复 30% 血量（按 TAB 前买的直接生效）", price = 120,
                buy = p2 => p2.BuyHeal(0.30f),
                canBuy = p2 => p2.hp < p2.maxHp * 0.999f,
            },
            new Item {
                name = "模块修复包", desc = "修好发动机 / 履带 / 炮塔环 / 炮管 / 弹药架 / 油箱", price = 100,
                buy = p2 => p2.BuyModuleRepair(),
                canBuy = p2 => true,
            },
            new Item {
                name = ammoName, desc = "所有有限备弹的武器补满", price = ammoPrice,
                buy = p2 => p2.BuyAmmoRefill(),
                canBuy = p2 => true,
            },
        };
    }

    void OnGUI()
    {
        if (!Open) return;

        var p = Player;
        var mm = MM;
        if (p == null || mm == null) { Open = false; Time.timeScale = _savedScale; return; }

        float w = Screen.width, h = Screen.height;
        float panelW = Mathf.Min(760f, w * 0.78f);
        float rowH = 74f;
        var items = BuildItems(p);
        float panelH = 150f + items.Length * rowH + 40f;
        var panel = new Rect((w - panelW) * 0.5f, (h - panelH) * 0.5f, panelW, panelH);

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.color = new Color(0.06f, 0.08f, 0.11f, 0.97f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(panel.x + 24, panel.y + 18, panel.width - 48, 36), "补给商店", title);

        var small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(new Rect(panel.x + 24, panel.y + 56, panel.width - 48, 24),
            $"金币 {mm.Gold}　·　HP {p.hp:F0} / {p.maxHp:F0}　·　按 TAB 关闭继续战斗", small);

        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var row = new Rect(panel.x + 24, panel.y + 96 + i * rowH, panel.width - 48, rowH - 10);
            bool affordable = mm.Gold >= it.price;
            bool useful = it.canBuy == null || it.canBuy(p);
            bool canClick = affordable && useful;

            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool hover = row.Contains(mouse) && canClick;

            GUI.color = hover ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(0.10f, 0.13f, 0.18f, 1f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            nameStyle.normal.textColor = canClick ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            GUI.Label(new Rect(row.x + 16, row.y + 8, row.width * 0.55f, 26), it.name, nameStyle);

            var descStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            descStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(row.x + 16, row.y + 34, row.width * 0.62f, 22), it.desc, descStyle);

            var priceStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            priceStyle.normal.textColor = affordable ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.4f, 0.35f);
            string priceTxt = useful ? $"{it.price} 金币" : "不需要";
            GUI.Label(new Rect(row.x + row.width - 190, row.y + 20, 170, 28), priceTxt, priceStyle);

            if (hover && Input.GetMouseButtonDown(0))
            {
                if (mm.TrySpend(it.price))
                {
                    it.buy(p);
                    _toast = "已购买：" + it.name;
                    _toastUntil = Time.unscaledTime + 2f;
                }
                else
                {
                    _toast = "金币不够";
                    _toastUntil = Time.unscaledTime + 2f;
                }
            }
        }

        if (Time.unscaledTime < _toastUntil)
        {
            var toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            toastStyle.normal.textColor = new Color(0.6f, 1f, 0.6f);
            GUI.Label(new Rect(panel.x, panel.y + panelH - 34, panel.width, 26), _toast, toastStyle);
        }
    }
}
