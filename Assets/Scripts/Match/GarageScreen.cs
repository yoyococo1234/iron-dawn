using UnityEngine;

/// <summary>
/// 车库：开局和每次阵亡后在这里选载具（花金币买）。
/// 打开时游戏暂停（Time.timeScale = 0）、光标放开；选完自动关闭并恢复。
/// 样式走 UiKit：深蓝底 + 蓝描边卡片 + 金色强调，与开局封面一致。
/// </summary>
public class GarageScreen : MonoBehaviour
{
    public static GarageScreen Instance { get; private set; }

    public bool IsOpen { get; private set; }

    string _title = "选择载具";
    string _subtitle = "";
    float _anim;                 // 开合进度（0 = 关 / 1 = 开），给淡入淡出和弹出用

    void Awake() => Instance = this;

    public void Open(string title, string subtitle = "")
    {
        _title = title;
        _subtitle = subtitle;
        IsOpen = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UiSound.Play(UiSound.Kind.Open);
    }

    public void Close()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        if (GameStartScreen.Started)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        // 关掉之后这一小段时间继续吞鼠标：否则点「出击」那一下左键还按着，车一出来就开一炮
        UiKit.SwallowInput(0.35f);
    }

    // 卡片里数值条的满格参考（只用于画条，不影响玩法）
    const float HpRef = 12000f;
    const float SpeedRef = 80f;

    // 车库背景：实景暮色图（低分辨率渲的，靠双线性放大出虚化景深），没有就退回渐变
    static Texture2D _bg;
    static bool _bgTried;

    static Texture2D Bg()
    {
        if (!_bgTried) { _bgTried = true; _bg = Resources.Load<Texture2D>("UiBackdrops/garage"); }
        return _bg;
    }

    void OnGUI()
    {
        // 开合动画：关掉以后还要多画 0.15 秒淡出，所以先推进进度再决定要不要画
        float k = UiKit.Anim(ref _anim, IsOpen);
        if (!IsOpen && k <= 0.001f) return;

        var mm = MatchManager.Instance;
        if (mm == null) return;

        var oldMatrix = UiKit.BeginScaled(out float scale, out float vw);
        var mouse = UiKit.Mouse(scale);
        bool live = IsOpen;                  // 淡出过程中不再响应点击

        UiKit.Fade(k);
        // 实景暮色背景（虚化）：压暗得多一些，卡片才压得住
        UiKit.PhotoBackdrop(vw, Bg(), 0.62f, 0.58f, 0.88f, 0.40f);

        float y = UiKit.Heading(vw, 34f, _title, _subtitle);

        // ---- 左上角「退出」（2026-11-11 用户要求）----
        // 还没出击（场上没车）= 退出这一局，回主菜单（重载场景，一切重置）；
        // 已经有车（阵亡后重选 / G 中途换车）= 取消，关掉车库继续打。
        {
            var r0 = new Rect(24f, 26f, 168f, 52f);
            var h0 = UiKit.Button("garage_exit", r0, mouse, live);
            var rr = UiKit.Lift(r0, h0);
            bool hot = h0.hover01 > 0.4f;
            UiKit.Box(rr, hot ? new Color(1f, 0.42f, 0.36f, 0.20f) : new Color(0.055f, 0.070f, 0.100f, 0.88f));
            UiKit.Frame(rr, hot ? UiKit.Bad : UiKit.Edge);
            UiKit.Label(rr, "← 退出", 20, hot ? UiKit.Bad : UiKit.Desc, TextAnchor.MiddleCenter, true);
            if (h0.clicked)
            {
                UiSound.Play(UiSound.Kind.Close);
                if (mm.Player == null) mm.Restart();      // 回主菜单
                else Close();                             // 取消换车
            }
        }

        // ---- 海空解锁倒计时（开局只玩坦克）----
        {
            // 2026-11-11：海战常开，只有空战要等
            float airIn = MatchSettings.UnlockIn(Domain.Air);
            string note = null;
            if (airIn > 0f) note = $"空战载具 {Mathf.CeilToInt(airIn / 60f)} 分钟后开放";
            else if (MatchSettings.UnlockMinutes > 0) note = "空战载具已开放";
            if (note != null)
            {
                var nr = new Rect(vw - 24f - 360f, 34f, 360f, 40f);
                UiKit.Box(nr, new Color(0.055f, 0.070f, 0.100f, 0.88f));
                UiKit.Frame(nr, airIn > 0f ? UiKit.Gold : UiKit.Ally);
                UiKit.Label(nr, note, 17, airIn > 0f ? UiKit.Gold : UiKit.Ally, TextAnchor.MiddleCenter, true);
            }
        }

        // ---- 状态条：四个小格 ----
        string[] statK = { "金币", "剩余部署", "敌方在场", "我方剩余命" };
        string[] statV = { mm.Gold.ToString(), $"{mm.LivesLeft} 次", $"{mm.EnemiesLeft} 台", mm.TeamLives(0).ToString() };
        Color[] statC = { UiKit.Gold, UiKit.Text, UiKit.Bad, UiKit.Ally };
        float sw = 200f, sg = 14f;
        float sx = vw * 0.5f - (sw * 4f + sg * 3f) * 0.5f;
        float sy = y + 6f;
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect(sx + i * (sw + sg), sy, sw, 54f);
            UiKit.Box(r, new Color(0.055f, 0.070f, 0.100f, 0.80f));
            UiKit.Frame(r, UiKit.Edge);
            UiKit.Label(new Rect(r.x + 14f, r.y + 4f, r.width - 28f, 22f), statK[i], 15, UiKit.Dim);
            UiKit.Label(new Rect(r.x + 14f, r.y + 22f, r.width - 28f, 30f), statV[i], 22, statC[i], TextAnchor.MiddleLeft, true);
        }

        // ---- 载具卡片 ----
        var roster = mm.Roster;
        if (roster != null && roster.Length > 0)
        {
            int cols = 3;
            // 卡片高度 330 → 370：顶部让出 120 给载具缩略图（2026-11-10）
            float cw = 400f, ch = 370f, gap = 24f;
            float totalW = cols * cw + (cols - 1) * gap;
            float x0 = vw * 0.5f - totalW * 0.5f;
            float y0 = sy + 80f;

            for (int i = 0; i < roster.Length; i++)
            {
                var def = roster[i];
                if (def == null) continue;

                int col = i % cols, row = i / cols;
                // slot 是命中区（不随动画移动），r 是实际画出来的位置（悬停上浮 / 按下下沉 / 拒绝抖动）
                var slot = new Rect(x0 + col * (cw + gap), y0 + row * (ch + gap), cw, ch);

                bool unlocked = MatchSettings.DomainUnlocked(def.domain);   // 2026-11-11：开局只玩坦克
                bool afford = unlocked && mm.CanAfford(def);
                bool free = afford && mm.Gold < def.price;

                var hit = UiKit.Button("garage_" + def.displayName, slot, mouse, afford && live);
                var r = UiKit.Lift(slot, hit);
                float hv = hit.hover01;
                bool hover = hv > 0.5f;

                UiKit.Glow(r, hv, UiKit.Gold);        // 悬停外发光，画在卡片下面
                UiKit.Card(r, hover);
                if (!afford) UiKit.Box(r, new Color(0f, 0f, 0f, 0.35f));

                float ix = r.x + 22f, iw = r.width - 44f;
                Color nameC = afford ? UiKit.Text : UiKit.Mute;

                // ---- 载具缩略图（2026-11-10）----
                // 六张纯文字卡片看着像表格，加一张车图立刻变成"游戏界面"。
                // 图是 Assets/Editor/UiThumbGen.cs 渲出来的透明底 PNG，底部压一层渐变融进卡片底色。
                var band = new Rect(r.x + 1f, r.y + 1f, r.width - 2f, 120f);
                DrawThumb(band, def, afford, hover);

                // 名称 + 域标签
                UiKit.Label(new Rect(ix, r.y + 126f, iw - 90f, 36f), def.displayName, 26, nameC, TextAnchor.MiddleLeft, true);
                UiKit.Chip(new Rect(r.xMax - 22f - 76f, r.y + 130f, 76f, 26f), DomainText(def.domain), DomainColor(def.domain), 15);

                UiKit.HLine(ix, r.y + 168f, iw);

                // 两条数值
                StatRow(ix, r.y + 178f, iw, "血量", $"{def.maxHp:F0}", def.maxHp / HpRef, afford);
                StatRow(ix, r.y + 208f, iw, "速度", $"{def.speed:F0} m/s", def.speed / SpeedRef, afford);

                // 武器
                string weapon = "无武器";
                if (def.weapons != null && def.weapons.Length > 0 && def.weapons[0] != null)
                {
                    weapon = $"{def.weapons[0].displayName}（{def.weapons[0].baseDamage:F0} 伤害 / {def.weapons[0].range:F0} m）";
                    if (def.weapons.Length > 1 && def.weapons[1] != null)
                    weapon += $" + {def.weapons[1].displayName}";
                }
                // 武器（最多两行：主炮 + 同轴机枪这种长串会换行，所以给它 36 px 高）
                UiKit.Label(new Rect(ix, r.y + 236f, iw, 36f), weapon, 15, afford ? UiKit.Desc : UiKit.Mute);

                // 简介
                UiKit.Label(new Rect(ix, r.y + 274f, iw, 34f), def.description, 15,
                    afford ? UiKit.Dim : UiKit.Mute, TextAnchor.UpperLeft);

                // 底部价格栏
                var foot = new Rect(r.x + 1f, r.yMax - 58f, r.width - 2f, 57f);
                UiKit.Box(foot, hover ? new Color(1f, 0.84f, 0.35f, 0.14f) : new Color(1f, 1f, 1f, 0.03f));
                UiKit.HLine(r.x + 1f, foot.y, r.width - 2f, hover ? 0.0f : 0.13f);
                if (hover) UiKit.Box(new Rect(r.x + 1f, foot.y, r.width - 2f, 1f), new Color(1f, 0.84f, 0.35f, 0.45f));

                string priceTxt = !unlocked ? "未开放"
                                : free ? "免费补充" : $"￥ {def.price}";
                UiKit.Label(new Rect(ix, foot.y, iw * 0.5f, foot.height), priceTxt, 24,
                    unlocked && afford ? UiKit.Gold : UiKit.Mute, TextAnchor.MiddleLeft, true);

                string rightTxt;
                Color rightC;
                if (!unlocked)
                {
                    int mins = Mathf.CeilToInt(MatchSettings.UnlockIn(def.domain) / 60f);
                    rightTxt = $"{mins} 分钟后开放";
                    rightC = UiKit.Gold;
                }
                else if (afford) { rightTxt = hover ? "点击出击 ›" : "出击"; rightC = hover ? UiKit.Gold : UiKit.Desc; }
                else { rightTxt = "金币不足"; rightC = UiKit.Bad; }

                UiKit.Label(new Rect(ix + iw * 0.5f, foot.y, iw * 0.5f, foot.height), rightTxt, 18,
                    rightC, TextAnchor.MiddleRight, hover && afford);

                if (hit.clicked)
                {
                    mm.DeployPlayer(def);
                }
                else if (hit.denied)
                {
                    // 点不起 / 没开放：卡片已经抖过了，再补一句浮字说明原因
                    UiKit.Float(unlocked ? "金币不足" : "该域还没开放", new Vector2(r.center.x, r.y + 46f),
                        unlocked ? UiKit.Bad : UiKit.Gold);
                }
            }
        }
        else
        {
            UiKit.Label(new Rect(0, 420f, vw, 40f), "没有可选载具（MatchDef.roster 为空，先跑菜单 ②）", 20, UiKit.Bad, TextAnchor.MiddleCenter);
        }

        UiKit.EndScreen();          // 涟漪 + 浮字压在最上层，并复位透明度
        GUI.matrix = oldMatrix;
    }

    static void StatRow(float x, float y, float w, string k, string v, float t, bool on)
    {
        UiKit.Label(new Rect(x, y, 52f, 24f), k, 15, on ? UiKit.Dim : UiKit.Mute);
        UiKit.Bar(new Rect(x + 56f, y + 9f, w - 56f - 110f, 6f), t,
            on ? new Color(0.35f, 0.75f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.18f));
        UiKit.Label(new Rect(x + w - 104f, y, 104f, 24f), v, 16, on ? UiKit.Text : UiKit.Mute, TextAnchor.MiddleRight, true);
    }

    // ---------------- 载具缩略图（2026-11-10） ----------------

    static readonly System.Collections.Generic.Dictionary<string, Texture2D> _thumbs =
        new System.Collections.Generic.Dictionary<string, Texture2D>();

    /// <summary>
    /// 按 VehicleDef 的资产名取缩略图（`Assets/Resources/UiThumbs/&lt;资产名&gt;.png`）。
    /// 找不到也缓存 null —— 否则每帧 OnGUI 都会去 Resources.Load 一次。
    /// </summary>
    static Texture2D Thumb(VehicleDef def)
    {
        if (def == null) return null;
        if (_thumbs.TryGetValue(def.name, out var t)) return t;
        t = Resources.Load<Texture2D>("UiThumbs/" + def.name);
        _thumbs[def.name] = t;
        return t;
    }

    /// <summary>卡片顶部那块车图：背光 + ScaleToFit 保比例 + 底边渐变让它"站"在卡片上。</summary>
    static void DrawThumb(Rect band, VehicleDef def, bool afford, bool hover)
    {
        // 背光：按域染色的径向光晕，车才从深色底上"浮"出来（2026-11-10）
        var dc = DomainColor(def.domain);
        UiKit.Blob(new Rect(band.center.x - band.width * 0.52f, band.center.y - band.height * 0.72f,
            band.width * 1.04f, band.height * 1.50f), new Color(dc.r, dc.g, dc.b, 0.30f));

        var tex = Thumb(def);
        if (tex != null)
        {
            var old = GUI.color;
            // 买不起的车把图压暗一点（配合卡片上那层黑罩，一眼能看出"不可选"）
            GUI.color = new Color(1f, 1f, 1f, (afford ? 1f : 0.55f) * UiKit.Alpha);
            GUI.DrawTexture(band, tex, ScaleMode.ScaleToFit, true);
            GUI.color = old;

            // 底边渐变收到卡片底色里。**底色要跟着 hover 变**，否则悬停时卡片变亮、
            // 这条渐变还是暗的，会出现一道可见的横带。
            // 用 FadeUp（单张贴图一次画）而不是逐条 VGradient —— 后者在 1.333 缩放下有接缝条纹。
            var baseC = hover ? UiKit.PanelHover : UiKit.PanelBg;
            UiKit.FadeUp(new Rect(band.x, band.yMax - 30f, band.width, 30f),
                new Color(baseC.r, baseC.g, baseC.b, 0.95f));
        }
        else
        {
            // 还没跑 `Tools → 铁皮黎明 → 生成载具缩略图` 时的占位，别让卡片塌掉
            UiKit.Label(new Rect(band.x, band.y, band.width, band.height), "（无预览图）", 15,
                new Color(1f, 1f, 1f, 0.28f), TextAnchor.MiddleCenter);
        }
    }

    static string DomainText(Domain d)
        => d == Domain.Sea ? "海战" : d == Domain.Air ? "空战" : "陆战";

    static Color DomainColor(Domain d)
        => d == Domain.Sea ? new Color(0.35f, 0.75f, 1f) : d == Domain.Air ? new Color(0.70f, 0.62f, 1f) : new Color(0.62f, 0.85f, 0.45f);
}
