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
    float _anim;                 // 开合进度（0 = 关 / 1 = 开）

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

        if (!Open) { Open = true; _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f; UiSound.Play(UiSound.Kind.Open); }
        else { Open = false; Time.timeScale = _savedScale; RelockCursor(); UiSound.Play(UiSound.Kind.Close); UiKit.SwallowInput(0.3f); }
    }

    void OnDisable()
    {
        if (Open) { Open = false; Time.timeScale = 1f; RelockCursor(); }
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

    /// <summary>关掉商店立刻把光标锁回准星（用户要求：不要还得点一下才恢复）。</summary>
    static void RelockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnGUI()
    {
        // 开合动画：关掉以后还要多画 0.15 秒淡出，所以先推进进度再决定要不要画
        float k = UiKit.Anim(ref _anim, Open);
        if (!Open && k <= 0.001f) return;

        var p = Player;
        var mm = MM;
        if (p == null || mm == null) { Open = false; Time.timeScale = _savedScale; return; }

        var oldMatrix = UiKit.BeginScaled(out float scale, out float vw);
        var mouse = UiKit.Mouse(scale);
        bool live = Open;                     // 淡出过程中不再响应点击
        var items = BuildItems(p);

        UiKit.Fade(k);
        // 半透明压暗，能看到身后的战场
        UiKit.Box(new Rect(0, 0, vw, 1080f), new Color(0.01f, 0.015f, 0.025f, 0.62f));

        float panelW = 760f;
        float rowH = 92f;
        // 140 = 标题/金币血量/分隔线占的高度；46 = 底部 toast 的位置。
        // 原来写成 168 + … + 56，面板底下空出 80 px（2026-11-10 截图里很明显）
        float panelH = 140f + items.Length * rowH + 46f;
        var panel = UiKit.PopIn(new Rect(vw * 0.5f - panelW * 0.5f, 540f - panelH * 0.5f, panelW, panelH), k);
        UiKit.Card(panel);

        // 标题 + 右上角 TAB 提示
        UiKit.Label(new Rect(panel.x + 30f, panel.y + 20f, 300f, 40f), "补给商店", 30, UiKit.Text, TextAnchor.MiddleLeft, true);
        UiKit.KeyCap(new Rect(panel.xMax - 30f - 64f, panel.y + 27f, 64f, 26f), "TAB", 15);
        UiKit.Label(new Rect(panel.xMax - 30f - 64f - 110f, panel.y + 27f, 100f, 26f), "关闭", 15, UiKit.Dim, TextAnchor.MiddleRight);

        // 金币 + 血量
        float iy = panel.y + 76f;
        UiKit.Label(new Rect(panel.x + 30f, iy, 60f, 28f), "金币", 16, UiKit.Dim);
        UiKit.Label(new Rect(panel.x + 76f, iy, 160f, 28f), mm.Gold.ToString(), 24, UiKit.Gold, TextAnchor.MiddleLeft, true);
        float hpT = p.maxHp > 0f ? p.hp / p.maxHp : 0f;
        UiKit.Label(new Rect(panel.x + 260f, iy, 50f, 28f), "血量", 16, UiKit.Dim);
        UiKit.Bar(new Rect(panel.x + 310f, iy + 11f, 260f, 7f), hpT, hpT > 0.35f ? UiKit.Good : UiKit.Bad);
        UiKit.Label(new Rect(panel.x + 580f, iy, 150f, 28f), $"{p.hp:F0} / {p.maxHp:F0}", 16, UiKit.Text, TextAnchor.MiddleRight, true);

        UiKit.HLine(panel.x + 24f, panel.y + 124f, panel.width - 48f);

        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var row = new Rect(panel.x + 24f, panel.y + 140f + i * rowH, panel.width - 48f, rowH - 14f);
            bool affordable = mm.Gold >= it.price;
            bool useful = it.canBuy == null || it.canBuy(p);
            bool canClick = affordable && useful;

            // 交互交给 UiKit：悬停平滑、按下位移、点不了时抖动 + 拒绝音、点成功出涟漪
            var hit = UiKit.Button("shop_" + it.name, row, mouse, canClick && live);
            row = UiKit.Lift(row, hit);
            bool hover = hit.hover01 > 0.5f;

            UiKit.Glow(row, hit.hover01, UiKit.Gold);
            UiKit.Box(row, hover ? new Color(1f, 0.84f, 0.35f, 0.10f) : new Color(1f, 1f, 1f, 0.03f));
            UiKit.Frame(row, hover ? new Color(1f, 0.84f, 0.35f, 0.45f) : new Color(0.35f, 0.75f, 1f, 0.14f));
            UiKit.Box(new Rect(row.x, row.y, 3f, row.height), canClick ? UiKit.Ally : new Color(1f, 1f, 1f, 0.15f));

            UiKit.Label(new Rect(row.x + 20f, row.y + 8f, row.width * 0.6f, 30f), it.name, 21,
                canClick ? UiKit.Text : UiKit.Mute, TextAnchor.MiddleLeft, true);
            UiKit.Label(new Rect(row.x + 20f, row.y + 40f, row.width * 0.68f, 24f), it.desc, 15,
                canClick ? UiKit.Dim : UiKit.Mute);

            string priceTxt = !useful ? "不需要" : $"{it.price} 金币";
            Color priceC = !useful ? UiKit.Mute : affordable ? UiKit.Gold : UiKit.Bad;
            UiKit.Label(new Rect(row.xMax - 200f, row.y + 6f, 180f, 30f), priceTxt, 20, priceC, TextAnchor.MiddleRight, true);
            string act = !useful ? "" : !affordable ? "金币不足" : hover ? "点击购买 ›" : "购买";
            UiKit.Label(new Rect(row.xMax - 200f, row.y + 38f, 180f, 24f), act, 15,
                !affordable ? UiKit.Bad : hover ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleRight);

            if (hit.clicked)
            {
                if (mm.TrySpend(it.price))
                {
                    it.buy(p);
                    _toast = "已购买：" + it.name;
                    UiSound.Play(UiSound.Kind.Coin);
                    UiKit.Float("-" + it.price, new Vector2(row.center.x, row.y + 20f), UiKit.Gold);
                }
                else _toast = "金币不够";
                _toastUntil = Time.unscaledTime + 2f;
            }
            else if (hit.denied)
            {
                UiKit.Float(useful ? "金币不够" : "不需要", new Vector2(row.center.x, row.y + 20f), UiKit.Bad);
            }
        }

        if (Time.unscaledTime < _toastUntil)
        {
            bool ok = _toast.StartsWith("已购买");
            UiKit.Chip(new Rect(panel.x + panel.width * 0.5f - 130f, panel.yMax - 46f, 260f, 30f), _toast, ok ? UiKit.Good : UiKit.Bad, 16);
        }

        UiKit.EndScreen();          // 涟漪 + 浮字压在最上层，并复位透明度
        GUI.matrix = oldMatrix;
    }
}
