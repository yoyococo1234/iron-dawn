using UnityEngine;

/// <summary>
/// 主菜单 →「按键设置」面板（2026-11-11）。
///
/// 用户要求：开始界面里能改键；给的算初始键位；**改重复了就不让**。
/// 做法：点一行 → 该行变成"请按新键…" → 捕获 Event.current.KeyDown →
/// <see cref="Bindings.TrySet"/> 撞到别的动作就拒绝并把被占用的动作名报出来。
///
/// 只有键盘能改；鼠标左右键（开火/开镜）写死，界面上标注清楚。
/// </summary>
public static class MenuKeys
{
    static Bindings.Act? _waiting;     // 正在等玩家按键的那个动作

    /// <summary>是不是正在等玩家按键。主菜单要问这个 —— 否则按 Esc 会连面板一起关掉。</summary>
    public static bool IsWaiting => _waiting.HasValue;
    static string _msg = "";
    static Color _msgColor = UiKit.Desc;
    static float _msgUntil;

    public static void Draw(float vw, Vector2 mouse, float k, System.Action back)
    {
        // ---- 捕获按键 ----
        if (_waiting.HasValue)
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    _waiting = null;
                    Say("已取消", UiKit.Dim);
                    e.Use();
                }
                else if (e.keyCode != KeyCode.None)
                {
                    var act = _waiting.Value;
                    if (Bindings.TrySet(act, e.keyCode, out var conflict))
                    {
                        Say($"「{Bindings.LabelOf(act)}」改成 {e.keyCode}", UiKit.Good);
                        UiSound.Play(UiSound.Kind.Confirm);
                    }
                    else
                    {
                        // 用户明确要求：重复的键不让设
                        Say($"{e.keyCode} 已经被「{Bindings.LabelOf(conflict)}」占用，换一个", UiKit.Bad);
                        UiSound.Play(UiSound.Kind.Deny);
                    }
                    _waiting = null;
                    e.Use();
                }
            }
        }

        // ---- 面板 ----
        float pw = 1180f, ph = 820f;
        var panel = UiKit.PopIn(new Rect(vw * 0.5f - pw * 0.5f, 540f - ph * 0.5f, pw, ph), k);
        UiKit.Card(panel);

        UiKit.Label(new Rect(panel.x + 44f, panel.y + 26f, 600f, 48f), "按键设置", 38, UiKit.Text, TextAnchor.MiddleLeft, true);
        UiKit.Label(new Rect(panel.x + 44f, panel.y + 74f, pw - 88f, 26f),
            "点一行，然后按下你想用的键。重复的键会被拦下。鼠标左右键（开火 / 开镜）固定不可改。",
            15, UiKit.Dim, TextAnchor.MiddleLeft);

        UiKit.HLine(panel.x + 40f, panel.y + 108f, pw - 80f);

        // ---- 两列 ----
        var table = Bindings.Table;
        int half = (table.Length + 1) / 2;
        float colW = (pw - 120f) * 0.5f;
        float rowH = 46f;
        float y0 = panel.y + 128f;

        for (int i = 0; i < table.Length; i++)
        {
            int col = i / half;
            int row = i % half;
            var slot = new Rect(panel.x + 48f + col * (colW + 24f), y0 + row * rowH, colW, rowH - 6f);

            var e0 = table[i];
            bool isWaiting = _waiting.HasValue && _waiting.Value == e0.act;

            var hit = UiKit.Button("key_" + e0.act, slot, mouse, !_waiting.HasValue || isWaiting);
            var r = UiKit.Lift(slot, hit);
            float hv = hit.hover01;

            UiKit.Box(r, isWaiting
                ? new Color(1f, 0.84f, 0.35f, 0.20f)
                : Color.Lerp(new Color(1f, 1f, 1f, 0.03f), new Color(1f, 0.84f, 0.35f, 0.10f), hv));

            UiKit.Label(new Rect(r.x + 14f, r.y, colW - 200f, r.height), e0.label, 17,
                        hv > 0.4f ? UiKit.Text : UiKit.Desc, TextAnchor.MiddleLeft);

            // 右侧键帽
            string keyTxt = isWaiting ? "按新键…" : Bindings.Get(e0.act).ToString();
            var cap = new Rect(r.xMax - 168f, r.y + 5f, 154f, r.height - 10f);
            UiKit.Box(cap, isWaiting ? new Color(1f, 0.84f, 0.35f, 0.28f) : new Color(0.10f, 0.13f, 0.18f, 0.95f));
            UiKit.Frame(cap, isWaiting ? UiKit.Gold : UiKit.Edge);
            UiKit.Label(cap, keyTxt, 16, isWaiting ? UiKit.Gold : UiKit.Text, TextAnchor.MiddleCenter, true);

            if (hit.clicked)
            {
                _waiting = e0.act;
                Say($"请按下「{e0.label}」的新按键（Esc 取消）", UiKit.Gold);
                UiSound.Play(UiSound.Kind.Click);
            }
        }

        // ---- 消息条 ----
        if (Time.unscaledTime < _msgUntil && !string.IsNullOrEmpty(_msg))
        {
            UiKit.Label(new Rect(panel.x + 44f, panel.y + ph - 132f, pw - 88f, 30f), _msg, 17, _msgColor, TextAnchor.MiddleLeft);
        }
        else if (Bindings.IsDefault())
        {
            UiKit.Label(new Rect(panel.x + 44f, panel.y + ph - 132f, pw - 88f, 30f), "当前是默认键位", 16, UiKit.Dim, TextAnchor.MiddleLeft);
        }

        // ---- 底部按钮 ----
        float by = panel.y + ph - 88f;
        var reset = new Rect(panel.x + 44f, by, 220f, 56f);
        var h1 = UiKit.Button("key_reset", reset, mouse, true);
        var r1 = UiKit.Lift(reset, h1);
        UiKit.Box(r1, h1.hover01 > 0.02f ? new Color(1f, 0.42f, 0.36f, 0.16f) : new Color(1f, 1f, 1f, 0.04f));
        UiKit.Frame(r1, h1.hover01 > 0.4f ? UiKit.Bad : UiKit.Edge);
        UiKit.Label(r1, "恢复默认键位", 18, h1.hover01 > 0.4f ? UiKit.Bad : UiKit.Desc, TextAnchor.MiddleCenter, true);
        if (h1.clicked)
        {
            Bindings.ResetAll();
            _waiting = null;
            Say("已恢复默认键位", UiKit.Good);
            UiSound.Play(UiSound.Kind.Confirm);
        }

        var backR = new Rect(panel.xMax - 44f - 220f, by, 220f, 56f);
        var h2 = UiKit.Button("key_back", backR, mouse, true);
        var r2 = UiKit.Lift(backR, h2);
        UiKit.Box(r2, h2.hover01 > 0.02f ? new Color(1f, 0.84f, 0.35f, 0.18f) : new Color(1f, 1f, 1f, 0.04f));
        UiKit.Frame(r2, h2.hover01 > 0.4f ? UiKit.Gold : UiKit.Edge);
        UiKit.Label(r2, "返回", 18, h2.hover01 > 0.4f ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleCenter, true);
        if (h2.clicked)
        {
            _waiting = null;
            UiSound.Play(UiSound.Kind.Close);
            back();
        }
    }

    static void Say(string s, Color c)
    {
        _msg = s;
        _msgColor = c;
        _msgUntil = Time.unscaledTime + 2.2f;
    }
}
