using UnityEngine;

/// <summary>
/// 开局主菜单（2026-11-11 改版）。
///
/// 用户要求：四个按钮 —— **开始游戏 / 按键设置 / 游戏调整 / 退出游戏**。
/// 旧版是"点一下画面就开始"，现在改成标准主菜单，两个设置面板就地切换（不换场景）。
///
/// 开始前 Time.timeScale = 0 —— AI、炮弹、对局计时全停着，不会一进游戏就被打。
/// 样式统一走 UiKit（和车库/商店/暂停一套皮肤）。
/// </summary>
public class GameStartScreen : MonoBehaviour
{
    /// <summary>是否已经开始（HUD / MatchManager 用这个决定要不要跑）。</summary>
    public static bool Started { get; private set; }

    enum Page { Main, Keys, Options }

    [Header("文案")]
    public string title = "铁皮黎明";
    public string subtitle = "2 公里 · 陆海空三域战场";

    Page _page = Page.Main;
    float _fade;          // 入场淡入（从黑到亮），用 unscaledDeltaTime 推进
    float _pageAnim;      // 子面板开合动画

    // 封面背景：实景暮色渲染图（Assets/Editor/UiBackdropGen.cs 生成），没有就退回纯渐变
    static Texture2D _bg;
    static bool _bgTried;

    static Texture2D Bg()
    {
        if (!_bgTried) { _bgTried = true; _bg = Resources.Load<Texture2D>("UiBackdrops/cover"); }
        return _bg;
    }

    void Awake()
    {
        Started = false;
        _page = Page.Main;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 玩家设置/键位从存档里读一次
        MatchSettings.Load();
    }

    void OnDestroy()
    {
        // 防止退出后编辑器还停着
        if (!Started) Time.timeScale = 1f;
    }

    void Update()
    {
        // 入场淡入用 unscaledDeltaTime：这时候 timeScale = 0，deltaTime 是 0
        if (_fade < 1f) _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime * 2.4f);
        _pageAnim = Mathf.MoveTowards(_pageAnim, _page == Page.Main ? 0f : 1f, Time.unscaledDeltaTime * 6f);

        if (Started) return;

        // 自愈：重开场景时旧实例的 OnDestroy 可能把 timeScale 抢回 1，这里按住
        if (!Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = 0f;

        // Esc 的两层语义（2026-11-11 用户反馈：按 Esc 应该只退出"选择新按键"这一步，
        // 不是退出整个按键设置）。**只留这一个入口**，按顺序判定 ——
        // 以前是"面板自己吞一次、主菜单又读一次 Input.GetKeyDown"，两个组件抢同一个按键，
        // 顺序一变就整块面板退出。
        if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    /// <summary>
    /// Esc 两层语义的**唯一判定点**（2026-11-12 从 Update 里抽出来，方便脱离键盘直接验证）。
    /// 第一层：正在等新键 → 只取消这次改键，面板留着；
    /// 第二层：其余情况 → 返回主菜单。
    /// </summary>
    public void HandleEscape()
    {
        if (_page == Page.Main) return;
        if (MenuKeys.IsWaiting) MenuKeys.CancelWaiting();
        else _page = Page.Main;
    }

    /// <summary>当前在哪个面板（0=主菜单 1=按键设置 2=游戏调整）。验收脚本用。</summary>
    public int PageIndex => (int)_page;

    /// <summary>开始游戏：进车库选第一台车。</summary>
    public void Begin()
    {
        if (Started) return;
        Started = true;
        UiSound.Play(UiSound.Kind.Confirm);
        UiKit.SwallowInput(0.35f);      // 点开始这一下别把炮也打出去
        var mm = MatchManager.Instance;
        if (mm != null) mm.OpenGarageFirst();
        else Time.timeScale = 1f;
    }

    /// <summary>退出游戏（编辑器里停 Play，打包后真退出）。</summary>
    public void QuitGame()
    {
        UiSound.Play(UiSound.Kind.Close);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnGUI()
    {
        if (Started) return;

        var oldMatrix = UiKit.BeginScaled(out float scale, out float vw);
        var mouse = UiKit.Mouse(scale);

        UiKit.Alpha = 1f;                                   // 不参与别的界面的淡入淡出
        UiKit.PhotoBackdrop(vw, Bg(), 0.50f, 0.74f, 0.88f, 0.34f);

        if (_page == Page.Main) DrawMain(vw, mouse);
        else
        {
            // 子面板：主菜单内容压暗一点做底，再盖面板
            UiKit.Box(new Rect(0, 0, vw, 1080f), new Color(0.01f, 0.015f, 0.025f, 0.55f * _pageAnim));
            if (_page == Page.Keys) MenuKeys.Draw(vw, mouse, _pageAnim, () => _page = Page.Main);
            else MenuOptions.Draw(vw, mouse, _pageAnim, () => _page = Page.Main);
        }

        // 入场黑幕
        if (_fade < 1f) UiKit.Box(new Rect(0, 0, vw, 1080f), new Color(0f, 0f, 0f, 1f - _fade));

        UiKit.EndScreen();
        GUI.matrix = oldMatrix;
    }

    void DrawMain(float vw, Vector2 mouse)
    {
        // ---- 标题 ----
        float pulse = 0.16f + 0.16f * Mathf.Sin(Time.unscaledTime * 2.1f);
        var titleRect = new Rect(0f, 176f, vw, 120f);
        // 金色光晕：同字下移 3px 画一层
        UiKit.Label(new Rect(0f, 179f, vw, 120f), title, 96, new Color(1f, 0.84f, 0.35f, pulse), TextAnchor.MiddleCenter, true);
        UiKit.Label(titleRect, title, 96, UiKit.Text, TextAnchor.MiddleCenter, true);
        UiKit.Label(new Rect(0f, 292f, vw, 34f), subtitle, 22, UiKit.Dim, TextAnchor.MiddleCenter);

        // 标题下的金色短横
        UiKit.Box(new Rect(vw * 0.5f - 90f, 340f, 180f, 2f), new Color(1f, 0.84f, 0.35f, 0.55f));

        // ---- 四个按钮 ----
        float bw = 420f, bh = 72f, gap = 14f;
        float bx = vw * 0.5f - bw * 0.5f;
        float by = 408f;

        string[] labels = { "开始游戏", "按键设置", "游戏调整", "退出游戏" };
        string[] hints = { "选载具，出击", "改键位 · 冲突会拦下", "金币 / 飞机 / 敌我人数 / AI 强度", "关掉游戏" };
        Color[] accent = { UiKit.Gold, UiKit.Ally, UiKit.Ally, UiKit.Bad };

        for (int i = 0; i < labels.Length; i++)
        {
            var slot = new Rect(bx, by + i * (bh + gap), bw, bh);
            var hit = UiKit.Button("menu_" + i, slot, mouse, true);
            var r = UiKit.Lift(slot, hit);
            float hv = hit.hover01;

            UiKit.Glow(r, hv, accent[i]);
            UiKit.Box(r, hv > 0.02f
                ? Color.Lerp(UiKit.PanelBg, UiKit.PanelHover, hv)
                : UiKit.PanelBg);
            UiKit.Frame(r, Color.Lerp(UiKit.Edge, accent[i], hv * 0.85f));

            // 左侧竖条：悬停时亮起来，一眼看出当前项
            UiKit.Box(new Rect(r.x + 1f, r.y + 1f, 4f, r.height - 2f),
                      new Color(accent[i].r, accent[i].g, accent[i].b, 0.35f + 0.55f * hv));

            // 标题 26px 的行高约 31px，原来塞进 30px 的框会向下溢出 1px，
            // 肉眼就是"副标题贴着标题底边"（用户 2026-11-11 反馈字挤）。
            UiKit.Label(new Rect(r.x + 28f, r.y + 4f, bw - 40f, 34f), labels[i], 26,
                        hv > 0.4f ? accent[i] : UiKit.Text, TextAnchor.MiddleLeft, true);
            UiKit.Label(new Rect(r.x + 28f, r.y + 42f, bw - 40f, 18f), hints[i], 13,
                        hv > 0.4f ? UiKit.Desc : UiKit.Dim, TextAnchor.MiddleLeft);

            if (hit.clicked)
            {
                UiSound.Play(UiSound.Kind.Click);
                if (i == 0) Begin();
                else if (i == 1) { _page = Page.Keys; UiSound.Play(UiSound.Kind.Open); }
                else if (i == 2) { _page = Page.Options; UiSound.Play(UiSound.Kind.Open); }
                else QuitGame();
            }
        }

        // ---- 底部提示 ----
        UiKit.Label(new Rect(0f, 1000f, vw, 26f),
            "鼠标点选　·　进游戏后 P 暂停（可重开）/ TAB 商店 / G 换车 / V 机枪", 17,
            new Color(0.62f, 0.72f, 0.82f, 0.8f), TextAnchor.MiddleCenter);
        // 注意：UnlockText() 自己已经带"空战"前缀（"空战 3 分钟后开放" / "开局就开放"），
        // 这里**不要**再写"海空"——以前就是多拼了一层，显示成「海空空战 3 分钟后开放」。
        UiKit.Label(new Rect(0f, 1028f, vw, 24f),
            $"当前设置：初始金币 {MatchSettings.StartingGold}　·　AI {SkillName()}　·　{UnlockText()}", 16,
            new Color(0.52f, 0.68f, 0.80f, 0.75f), TextAnchor.MiddleCenter);
    }

    static string SkillName()
    {
        int best = 0;
        float bd = 9f;
        for (int i = 0; i < MatchSettings.SkillSteps.Length; i++)
        {
            float d = Mathf.Abs(MatchSettings.SkillSteps[i] - MatchSettings.AiSkill);
            if (d < bd) { bd = d; best = i; }
        }
        return MatchSettings.SkillNames[best];
    }

    static string UnlockText()
        => MatchSettings.AirFromStart ? "开局就开放"
         : MatchSettings.UnlockMinutes <= 0 ? "开局就开放"
         : $"空战 {MatchSettings.UnlockMinutes} 分钟后开放";
}
