using UnityEngine;

/// <summary>
/// 主菜单 →「游戏调整」面板（2026-11-11）。
///
/// 用户要的三样：**初始金币**、**初始是否开放飞机（默认关）**、**敌我人数**（按类型分别配：
/// 舰艇 / 轻坦 / 主战 / 重装 / 飞机）。顺手加了「海空解锁时间」和「AI 强度」——
/// 前者是"开局只玩坦克"那条需求的开关，后者对应"AI 有点打不过"。
///
/// 所有改动即时写进 <see cref="MatchSettings"/> 并存 PlayerPrefs，**下一局生效**
/// （本局已经在跑的不动，避免中途改人数把场上搞乱）。
/// </summary>
public static class MenuOptions
{
    static string _msg = "";
    static Color _msgColor = UiKit.Desc;
    static float _msgUntil;

    public static void Draw(float vw, Vector2 mouse, float k, System.Action back)
    {
        float pw = 1180f, ph = 860f;
        var panel = UiKit.PopIn(new Rect(vw * 0.5f - pw * 0.5f, 540f - ph * 0.5f, pw, ph), k);
        UiKit.Card(panel);

        UiKit.Label(new Rect(panel.x + 44f, panel.y + 26f, 600f, 48f), "游戏调整", 38, UiKit.Text, TextAnchor.MiddleLeft, true);
        UiKit.Label(new Rect(panel.x + 44f, panel.y + 74f, pw - 88f, 26f),
            "改动立刻存档，<b>下一局开始生效</b>。人数只算 AI，你自己另外算一台。", 15, UiKit.Dim, TextAnchor.MiddleLeft);
        UiKit.HLine(panel.x + 40f, panel.y + 108f, pw - 80f);

        // ================= 左列：全局 =================
        float lx = panel.x + 44f;
        float lw = 500f;
        float y = panel.y + 132f;

        // ---- 初始金币 ----
        y = Stepper(lx, y, lw, "初始金币", MatchSettings.StartingGold.ToString(),
            () => MatchSettings.StartingGold -= 100, () => MatchSettings.StartingGold += 100, mouse, "gold");

        // ---- 飞机开局就开放 ----
        y = Toggle(lx, y, lw, "飞机开局就开放", MatchSettings.AirFromStart,
            "关：开局只玩坦克，海空到点一起开放（默认）", mouse, "airfromstart");

        // ---- 海空解锁时间 ----
        y = Stepper(lx, y, lw, "海空解锁时间（分钟）", MatchSettings.UnlockMinutes == 0 ? "开局全开" : MatchSettings.UnlockMinutes + " 分钟",
            () => MatchSettings.UnlockMinutes -= 1, () => MatchSettings.UnlockMinutes += 1, mouse, "unlock");

        // ---- AI 强度 ----
        UiKit.Label(new Rect(lx, y + 6f, lw, 24f), "AI 强度", 17, UiKit.Desc, TextAnchor.MiddleLeft);
        float bx = lx + 200f;
        for (int i = 0; i < MatchSettings.SkillSteps.Length; i++)
        {
            bool on = Mathf.Abs(MatchSettings.AiSkill - MatchSettings.SkillSteps[i]) < 0.001f;
            var r = new Rect(bx + i * 104f, y + 2f, 98f, 34f);
            var h = UiKit.Button("skill_" + i, r, mouse, true);
            UiKit.Box(r, on ? new Color(1f, 0.84f, 0.35f, 0.28f)
                            : Color.Lerp(new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.10f), h.hover01));
            UiKit.Frame(r, on ? UiKit.Gold : UiKit.Edge);
            UiKit.Label(r, MatchSettings.SkillNames[i], 14, on ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleCenter, on);
            if (h.clicked)
            {
                MatchSettings.AiSkill = MatchSettings.SkillSteps[i];
                Save("AI 强度：" + MatchSettings.SkillNames[i]);
            }
        }
        y += 52f;

        // 提示
        UiKit.Label(new Rect(lx, y, lw, 60f),
            "AI 强度影响视野、开火距离、反应速度和瞄准误差。\n「轻松」下 AI 会打偏很多，适合先熟悉操作。",
            14, UiKit.Dim, TextAnchor.UpperLeft);

        // ================= 右列：敌我人数 =================
        float rx = panel.x + 44f + lw + 60f;
        float rw = pw - 88f - lw - 60f;
        float ry = panel.y + 132f;

        UiKit.Label(new Rect(rx, ry, rw, 28f), "敌我人数（按类型）", 20, UiKit.Ally, TextAnchor.MiddleLeft, true);
        UiKit.Label(new Rect(rx + 186f, ry + 6f, 130f, 24f), "我方 AI", 15, UiKit.Ally, TextAnchor.MiddleCenter);
        UiKit.Label(new Rect(rx + 340f, ry + 6f, 130f, 24f), "敌方 AI", 15, UiKit.Bad, TextAnchor.MiddleCenter);
        ry += 34f;

        string[] names = { "舰艇", "轻型坦克", "主战坦克", "重装坦克", "飞机" };
        for (int i = 0; i < names.Length; i++)
        {
            var row = new Rect(rx, ry + i * 50f, rw, 44f);
            UiKit.Box(row, new Color(1f, 1f, 1f, 0.025f));
            UiKit.Label(new Rect(row.x + 12f, row.y, 170f, row.height), names[i], 16, UiKit.Desc, TextAnchor.MiddleLeft);

            // 两组各 130 宽，中间留 24 px 空档，否则「− 1 + − 1 +」会连成一片看不出哪列是哪列
            CountBox(row.x + 186f, row, 0, i, mouse);
            CountBox(row.x + 340f, row, 1, i, mouse);
        }
        ry += names.Length * 50f + 8f;

        // 合计
        UiKit.HLine(rx, ry, rw);
        UiKit.Label(new Rect(rx, ry + 8f, rw, 26f),
            $"合计：我方 {MatchSettings.AiCount(0)} 台 AI + 你 = {MatchSettings.AiCount(0) + 1} 台　·　敌方 {MatchSettings.AiCount(1)} 台",
            15, UiKit.Gold, TextAnchor.MiddleLeft);

        // ---- 消息 / 底部按钮 ----
        if (Time.unscaledTime < _msgUntil && !string.IsNullOrEmpty(_msg))
            UiKit.Label(new Rect(panel.x + 44f, panel.y + ph - 132f, pw - 88f, 30f), _msg, 16, _msgColor, TextAnchor.MiddleLeft);

        float by = panel.y + ph - 88f;

        var reset = new Rect(panel.x + 44f, by, 240f, 56f);
        var h1 = UiKit.Button("opt_reset", reset, mouse, true);
        var r1 = UiKit.Lift(reset, h1);
        UiKit.Box(r1, h1.hover01 > 0.02f ? new Color(1f, 0.42f, 0.36f, 0.16f) : new Color(1f, 1f, 1f, 0.04f));
        UiKit.Frame(r1, h1.hover01 > 0.4f ? UiKit.Bad : UiKit.Edge);
        UiKit.Label(r1, "恢复默认设置", 18, h1.hover01 > 0.4f ? UiKit.Bad : UiKit.Desc, TextAnchor.MiddleCenter, true);
        if (h1.clicked)
        {
            MatchSettings.ResetAll();
            Say("已恢复默认设置", UiKit.Good);
            UiSound.Play(UiSound.Kind.Confirm);
        }

        var backR = new Rect(panel.xMax - 44f - 220f, by, 220f, 56f);
        var h2 = UiKit.Button("opt_back", backR, mouse, true);
        var r2 = UiKit.Lift(backR, h2);
        UiKit.Box(r2, h2.hover01 > 0.02f ? new Color(1f, 0.84f, 0.35f, 0.18f) : new Color(1f, 1f, 1f, 0.04f));
        UiKit.Frame(r2, h2.hover01 > 0.4f ? UiKit.Gold : UiKit.Edge);
        UiKit.Label(r2, "返回", 18, h2.hover01 > 0.4f ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleCenter, true);
        if (h2.clicked)
        {
            UiSound.Play(UiSound.Kind.Close);
            back();
        }
    }

    // ---------------- 控件 ----------------

    static float Stepper(float x, float y, float w, string label, string value,
        System.Action minus, System.Action plus, Vector2 mouse, string id)
    {
        UiKit.Label(new Rect(x, y + 6f, w - 220f, 24f), label, 17, UiKit.Desc, TextAnchor.MiddleLeft);

        var rMinus = new Rect(x + w - 220f, y + 2f, 40f, 34f);
        var rVal = new Rect(x + w - 174f, y + 2f, 124f, 34f);
        var rPlus = new Rect(x + w - 44f, y + 2f, 40f, 34f);

        if (SmallButton(rMinus, "−", mouse, id + "_m")) { minus(); Save(label + "：" + value); }
        UiKit.Box(rVal, new Color(0.10f, 0.13f, 0.18f, 0.95f));
        UiKit.Frame(rVal, UiKit.Edge);
        UiKit.Label(rVal, value, 17, UiKit.Gold, TextAnchor.MiddleCenter, true);
        if (SmallButton(rPlus, "+", mouse, id + "_p")) { plus(); Save(label + "：" + value); }

        return y + 46f;
    }

    static float Toggle(float x, float y, float w, string label, bool on, string note,
        Vector2 mouse, string id)
    {
        UiKit.Label(new Rect(x, y + 6f, w - 200f, 24f), label, 17, UiKit.Desc, TextAnchor.MiddleLeft);

        var r = new Rect(x + w - 120f, y + 2f, 120f, 34f);
        var h = UiKit.Button(id, r, mouse, true);
        UiKit.Box(r, on ? new Color(0.35f, 0.75f, 1f, 0.28f) : new Color(1f, 1f, 1f, 0.05f));
        UiKit.Frame(r, on ? UiKit.Ally : UiKit.Edge);
        UiKit.Label(r, on ? "开放" : "关闭", 15, on ? UiKit.Ally : UiKit.Mute, TextAnchor.MiddleCenter, true);
        if (h.clicked)
        {
            bool v = !MatchSettings.AirFromStart;
            MatchSettings.AirFromStart = v;
            Save(label + "：" + (v ? "开放" : "关闭"));
        }

        UiKit.Label(new Rect(x, y + 30f, w, 20f), note, 13, UiKit.Dim, TextAnchor.MiddleLeft);
        return y + 62f;
    }

    static void CountBox(float x, Rect row, int team, int typeIdx, Vector2 mouse)
    {
        var rMinus = new Rect(x, row.y + 5f, 34f, 34f);
        var rVal = new Rect(x + 38f, row.y + 5f, 54f, 34f);
        var rPlus = new Rect(x + 96f, row.y + 5f, 34f, 34f);

        int v = GetCount(team, typeIdx);
        string id = (team == 0 ? "a" : "e") + typeIdx;

        if (SmallButton(rMinus, "−", mouse, id + "_m")) { SetCount(team, typeIdx, v - 1); Save("人数已更新"); }
        UiKit.Box(rVal, new Color(0.10f, 0.13f, 0.18f, 0.95f));
        UiKit.Frame(rVal, UiKit.Edge);
        UiKit.Label(rVal, v.ToString(), 16, team == 0 ? UiKit.Ally : UiKit.Bad, TextAnchor.MiddleCenter, true);
        if (SmallButton(rPlus, "+", mouse, id + "_p")) { SetCount(team, typeIdx, v + 1); Save("人数已更新"); }
    }

    static bool SmallButton(Rect r, string txt, Vector2 mouse, string id)
    {
        var h = UiKit.Button(id, r, mouse, true);
        UiKit.Box(r, h.hover01 > 0.02f ? new Color(1f, 0.84f, 0.35f, 0.20f) : new Color(1f, 1f, 1f, 0.05f));
        UiKit.Frame(r, h.hover01 > 0.4f ? UiKit.Gold : UiKit.Edge);
        UiKit.Label(r, txt, 20, h.hover01 > 0.4f ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleCenter, true);
        return h.clicked;
    }

    static int GetCount(int team, int i)
    {
        bool ally = team == 0;
        switch (i)
        {
            case 0: return ally ? MatchSettings.AllyShips : MatchSettings.EnemyShips;
            case 1: return ally ? MatchSettings.AllyLight : MatchSettings.EnemyLight;
            case 2: return ally ? MatchSettings.AllyMain : MatchSettings.EnemyMain;
            case 3: return ally ? MatchSettings.AllyHeavy : MatchSettings.EnemyHeavy;
            default: return ally ? MatchSettings.AllyAir : MatchSettings.EnemyAir;
        }
    }

    static void SetCount(int team, int i, int v)
    {
        v = Mathf.Clamp(v, 0, MatchSettings.MaxPerType);
        bool ally = team == 0;
        switch (i)
        {
            case 0: if (ally) MatchSettings.AllyShips = v; else MatchSettings.EnemyShips = v; break;
            case 1: if (ally) MatchSettings.AllyLight = v; else MatchSettings.EnemyLight = v; break;
            case 2: if (ally) MatchSettings.AllyMain = v; else MatchSettings.EnemyMain = v; break;
            case 3: if (ally) MatchSettings.AllyHeavy = v; else MatchSettings.EnemyHeavy = v; break;
            default: if (ally) MatchSettings.AllyAir = v; else MatchSettings.EnemyAir = v; break;
        }
    }

    static void Save(string what)
    {
        MatchSettings.Save();
        Say(what + "（已存档，下一局生效）", UiKit.Good);
        UiSound.Play(UiSound.Kind.Click);
    }

    static void Say(string s, Color c)
    {
        _msg = s;
        _msgColor = c;
        _msgUntil = Time.unscaledTime + 2.2f;
    }
}
