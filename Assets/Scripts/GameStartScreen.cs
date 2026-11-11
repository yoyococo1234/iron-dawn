using UnityEngine;

/// <summary>
/// 开局封面：标题 + 操作说明，点一下（或按空格/回车）才开始。
/// 开始前 Time.timeScale = 0 —— AI、炮弹、对局计时全部停着，不会一进游戏就被打。
/// 样式与 MatchHUD 对齐：深蓝黑底 + 金色强调 + 蓝色描边面板。
/// </summary>
public class GameStartScreen : MonoBehaviour
{
    /// <summary>是否已经开始（HUD 用这个决定要不要画）。</summary>
    public static bool Started { get; private set; }

    [Header("文案")]
    public string title = "铁皮黎明";
    public string subtitle = "2 公里 · 陆海空三域战场";
    public string hint = "点击画面开始";

    bool _paused = true;
    float _fade;          // 入场淡入（从黑到亮），用 unscaledDeltaTime 推进
    GUIStyle _title, _titleGlow, _sub, _key, _desc, _hint, _foot, _head, _tag, _chip;

    // 封面背景：实景暮色渲染图（Assets/Editor/UiBackdropGen.cs 生成），没有就退回纯渐变
    static Texture2D _bg;
    static bool _bgTried;

    static Texture2D Bg()
    {
        if (!_bgTried) { _bgTried = true; _bg = Resources.Load<Texture2D>("UiBackdrops/cover"); }
        return _bg;
    }

    // ---- 配色（与 MatchHUD 一致）----
    static readonly Color CGold = new Color(1f, 0.84f, 0.35f);
    static readonly Color CAlly = new Color(0.35f, 0.75f, 1f);
    static readonly Color CPanel = new Color(0.055f, 0.070f, 0.100f, 0.92f);
    static readonly Color CEdge = new Color(0.35f, 0.75f, 1f, 0.22f);
    static readonly Color CDesc = new Color(0.80f, 0.85f, 0.90f);
    static readonly Color CDim = new Color(0.52f, 0.68f, 0.80f);
    static readonly Color CWhite = new Color(0.95f, 0.96f, 0.98f);

    void Awake()
    {
        Started = false;
        Time.timeScale = 0f;
    }

    void OnDestroy()
    {
        if (_paused) Time.timeScale = 1f;   // 防止退出后编辑器还停着
    }

    void Update()
    {
        // 入场淡入用 unscaledDeltaTime：这时候 timeScale = 0，deltaTime 是 0
        if (_fade < 1f) _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime * 2.4f);

        if (!_paused) return;
        // 自愈：重开场景时旧实例的 OnDestroy 可能把 timeScale 抢回 1，这里按住
        if (!Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = 0f;
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        Begin();
    }

    public void Begin()
    {
        _paused = false;
        Started = true;
        UiSound.Play(UiSound.Kind.Confirm);
        UiKit.SwallowInput(0.35f);      // 点开始这一下别把炮也打出去
        // 不直接开打：先进车库选第一台载具（车库会继续把 timeScale 压成 0）
        var mm = MatchManager.Instance;
        if (mm != null) mm.OpenGarageFirst();
        else Time.timeScale = 1f;
    }

    // GUI.skin 只能在 OnGUI 里访问，样式延迟到第一次绘制再建
    void EnsureStyles()
    {
        if (_title != null) return;
        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 92,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _title.normal.textColor = CWhite;

        // 标题外发光：同一段文字用金色画在下方偏移处，做出暖色轮廓
        _titleGlow = new GUIStyle(_title);
        _titleGlow.normal.textColor = new Color(1f, 0.84f, 0.35f, 0.30f);

        _sub = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter,
        };
        _sub.normal.textColor = CDim;

        _head = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
        };
        _head.normal.textColor = CAlly;

        _key = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _key.normal.textColor = CGold;

        _desc = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleLeft,
        };
        _desc.normal.textColor = CDesc;

        _tag = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleRight,
        };
        _tag.normal.textColor = new Color(1f, 0.84f, 0.35f, 0.55f);

        _hint = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _hint.normal.textColor = CGold;

        _foot = new GUIStyle(GUI.skin.label)
        {
            fontSize = 19,
            alignment = TextAnchor.MiddleCenter,
        };
        _foot.normal.textColor = new Color(0.62f, 0.72f, 0.82f, 0.85f);

        _chip = new GUIStyle(_foot) { fontSize = 18 };
        _chip.normal.textColor = new Color(0.62f, 0.78f, 0.90f, 0.9f);
    }

    // ---------------- 绘制辅助 ----------------

    static void Box(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Frame(Rect r, Color c, float t = 1f)
    {
        Box(new Rect(r.x, r.y, r.width, t), c);
        Box(new Rect(r.x, r.yMax - t, r.width, t), c);
        Box(new Rect(r.x, r.y, t, r.height), c);
        Box(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>一条带中间亮点的横向分隔线。</summary>
    static void Divider(float x, float y, float w)
    {
        Box(new Rect(x, y, w, 1f), new Color(0.35f, 0.75f, 1f, 0.16f));
        Box(new Rect(x + w * 0.5f - 60f, y - 0.5f, 120f, 2f), new Color(0.35f, 0.75f, 1f, 0.42f));
    }

    /// <summary>一行按键说明：金色键帽 + 灰色描述。</summary>
    void KeyRow(float x, float y, float w, string key, string desc)
    {
        const float keyW = 132f;
        // 键帽底板
        Box(new Rect(x, y + 3f, keyW, 26f), new Color(1f, 0.84f, 0.35f, 0.10f));
        Box(new Rect(x, y + 3f, 2f, 26f), new Color(1f, 0.84f, 0.35f, 0.55f));
        GUI.Label(new Rect(x + 2f, y, keyW - 2f, 30f), key, _key);
        GUI.Label(new Rect(x + keyW + 14f, y, w - keyW - 14f, 30f), desc, _desc);
    }

    /// <summary>一块说明面板：标题栏 + 分隔线 + 若干行。</summary>
    void Panel(Rect r, string head, string tag, string[] keys, string[] descs)
    {
        // 底板 + 顶部高光条
        Box(r, CPanel);
        Box(new Rect(r.x, r.y, r.width, 2f), new Color(0.35f, 0.75f, 1f, 0.35f));
        Frame(r, CEdge);

        GUI.Label(new Rect(r.x + 22f, r.y + 12f, r.width - 160f, 30f), head, _head);
        if (!string.IsNullOrEmpty(tag))
        GUI.Label(new Rect(r.x + r.width - 150f, r.y + 15f, 128f, 24f), tag, _tag);

        Box(new Rect(r.x + 18f, r.y + 48f, r.width - 36f, 1f), new Color(0.35f, 0.75f, 1f, 0.13f));

        float y = r.y + 64f;
        for (int i = 0; i < keys.Length; i++)
        {
            KeyRow(r.x + 20f, y, r.width - 40f, keys[i], descs[i]);
            y += 42f;
        }
    }

    void OnGUI()
    {
        if (!_paused) return;
        EnsureStyles();

        float scale = Mathf.Max(0.35f, Screen.height / 1080f);
        float vw = Screen.width / scale;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // ---- 背景：实景暮色图 + 压暗/冷调/上下渐变/暗角（2026-11-10）----
        // 统一走 UiKit.PhotoBackdrop：渐变的条数在那边按每 4px 自动算，
        // 这里原来手写 16/18 条，叠在照片上会出现明显的水平色带。
        UiKit.Alpha = 1f;                       // 封面不参与淡入淡出，保证不被别的界面残留污染
        UiKit.PhotoBackdrop(vw, Bg(), 0.50f, 0.74f, 0.88f, 0.34f);

        // 顶部一道冷光，压住标题区（用单张贴图渐变，避免条带接缝）
        UiKit.FadeDown(new Rect(0, 0, vw, 180f), new Color(0.35f, 0.75f, 1f, 0.07f));

        // 缓慢下移的扫描光带（2026-11-10）：改成两张贴图渐变拼成一条软光带，
        // 原来用 14 条 5px 半透明横条拼，接缝在缩放后会被放大成可见的横线。
        float scanY = Mathf.Repeat(Time.unscaledTime * 95f, 1240f) - 90f;
        var scanC = new Color(0.35f, 0.75f, 1f, 0.030f);
        UiKit.FadeUp(new Rect(0, scanY, vw, 45f), scanC);        // 上半：向上淡出，底边最亮
        UiKit.FadeDown(new Rect(0, scanY + 45f, vw, 45f), scanC); // 下半：向下淡出，顶边最亮

        // ---- 标题区 ----
        var titleRect = new Rect(0, 118f, vw, 120f);
        // 金色光晕呼吸（2026-11-10）：静态光晕看着太"贴纸"，加一层低频明暗就有金属反光感
        _titleGlow.normal.textColor = new Color(1f, 0.84f, 0.35f,
            0.16f + 0.16f * Mathf.Sin(Time.unscaledTime * 2.1f));
        GUI.Label(new Rect(titleRect.x + 3f, titleRect.y + 4f, titleRect.width, titleRect.height), title, _titleGlow);
        GUI.Label(titleRect, title, _title);

        // 标题下的金色短横
        Box(new Rect(vw * 0.5f - 90f, 244f, 180f, 2f), new Color(1f, 0.84f, 0.35f, 0.75f));
        Box(new Rect(vw * 0.5f - 26f, 241f, 52f, 8f), new Color(1f, 0.84f, 0.35f, 0.16f));

        GUI.Label(new Rect(0, 262f, vw, 36f), subtitle, _sub);

        // ---- 两块操作面板 ----
        float panelW = 452f;
        float gap = 28f;
        float px = vw * 0.5f - panelW - gap * 0.5f;
        float py = 344f;
        float ph = 448f;   // 9 行 × 42 + 表头 64 + 底部留白

        Panel(new Rect(px, py, panelW, ph), "驾驶操作", "陆地 · 海上", new[]
            {
                "W / S", "A / D", "鼠标", "左键 / 空格",
                "右键按住", "C 按住", "V / Shift", "Ctrl", "X",
            }, new[]
            {
                "前进 / 后退（飞机=油门）", "转向（飞机=横滚）", "转动视角 / 操控飞机", "开火",
                "开镜瞄准", "自由视角", "机枪模式 / 机枪开镜", "锁定目标", "干扰弹（炮艇）",
            });

        Panel(new Rect(px + panelW + gap, py, panelW, ph), "系统", "空中 · 全局", new[]
            {
                "Q", "1 / 2 / 3", "TAB", "G", "P", "Esc", "R",
            }, new[]
            {
                "切换武器", "切换弹药类型", "补给商店", "回车库换载具", "暂停 / 继续", "释放鼠标", "重开一局",
            });

        // ---- 底部 ----
        Divider(vw * 0.5f - 300f, 806f, 600f);

        // 呼吸提示（timeScale=0，必须用 unscaledTime）
        float pulse = 0.62f + 0.38f * Mathf.Sin(Time.unscaledTime * 3.0f);
        var hintCol = new Color(CGold.r, CGold.g, CGold.b, pulse);
        var oldC = GUI.color;
        GUI.color = hintCol;
        GUI.Label(new Rect(0, 830f, vw, 44f), hint, _hint);
        GUI.color = oldC;

        // 规则说明
        GUI.Label(new Rect(0, 892f, vw, 28f),
            "每人 3 条命 · 占点和击杀赚金币 · 全队命用完即输", _foot);

        // 三个小标签，横向排开
        float tagY = 936f;
        float tagW = 210f;
        float tagGap = 16f;
        float tagStart = vw * 0.5f - (tagW * 3f + tagGap * 2f) * 0.5f;
        string[] tags = { "陆海空三域", "5 v 5 对抗", "15 分钟一局" };
        for (int i = 0; i < tags.Length; i++)
        {
            var r = new Rect(tagStart + i * (tagW + tagGap), tagY, tagW, 30f);
            Box(r, new Color(0.35f, 0.75f, 1f, 0.06f));
            Frame(r, new Color(0.35f, 0.75f, 1f, 0.20f));
            GUI.Label(r, tags[i], _chip);
        }

        // 入场淡入：整块压一层黑，alpha 从 1 收到 0（0.4 秒左右）
        if (_fade < 1f) Box(new Rect(0, 0, vw, 1080f), new Color(0f, 0f, 0f, 1f - _fade));

        GUI.matrix = oldMatrix;
    }
}
