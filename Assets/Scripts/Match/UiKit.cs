using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 所有 OnGUI 界面（开局封面 / 车库 / 商店 / 暂停）共用的配色和绘制工具。
/// 风格与 MatchHUD 一致：深蓝黑底 + 蓝色描边面板 + 金色强调。
/// 所有坐标都按 1080 高的虚拟画布写，BeginScaled 负责缩放到实际分辨率。
/// </summary>
public static class UiKit
{
    public static readonly Color Gold = new Color(1f, 0.84f, 0.35f);
    public static readonly Color Ally = new Color(0.35f, 0.75f, 1f);
    public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);
    public static readonly Color Good = new Color(0.55f, 0.95f, 0.60f);
    public static readonly Color PanelBg = new Color(0.055f, 0.070f, 0.100f, 0.94f);
    public static readonly Color PanelHover = new Color(0.085f, 0.115f, 0.160f, 0.97f);
    public static readonly Color Edge = new Color(0.35f, 0.75f, 1f, 0.22f);
    public static readonly Color Text = new Color(0.95f, 0.96f, 0.98f);
    public static readonly Color Desc = new Color(0.80f, 0.85f, 0.90f);
    public static readonly Color Dim = new Color(0.52f, 0.68f, 0.80f);
    public static readonly Color Mute = new Color(0.55f, 0.60f, 0.66f);

    // ---------------- 整体透明度（2026-11-10） ----------------

    /// <summary>
    /// 整块界面的透明度乘子，默认 1。
    /// 面板淡入淡出时设成 0..1：Box / Frame / Label 等所有绘制都会把它乘进自己的颜色，
    /// 这样不用把每个颜色都手算一遍 alpha。**每个界面画完必须调 EndScreen() 复位**，
    /// 否则下一个界面（HUD）会跟着一起变透明。
    /// </summary>
    public static float Alpha = 1f;

    static Color A(Color c) => new Color(c.r, c.g, c.b, c.a * Alpha);

    /// <summary>界面画完的收尾：复位透明度 + 画涟漪和浮字（同一帧只画一次）。</summary>
    public static void EndScreen()
    {
        Alpha = 1f;
        DrawRipples();
        DrawFloaters();
    }

    // ---------------- 缩放 ----------------

    /// <summary>切到 1080 高的虚拟画布；返回旧矩阵，画完记得还原。</summary>
    public static Matrix4x4 BeginScaled(out float scale, out float vw)
    {
        scale = Mathf.Max(0.35f, Screen.height / 1080f);
        vw = Screen.width / scale;
        var old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        return old;
    }

    /// <summary>鼠标在虚拟画布上的坐标。</summary>
    public static Vector2 Mouse(float scale)
        => new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);

    // ---------------- 样式（按字号/对齐/粗细缓存，不每帧 new） ----------------

    static readonly Dictionary<int, GUIStyle> _styles = new Dictionary<int, GUIStyle>();

    public static GUIStyle Style(int size, TextAnchor anchor, bool bold = false)
    {
        int key = size * 100 + (int)anchor * 2 + (bold ? 1 : 0);
        if (!_styles.TryGetValue(key, out var st) || st == null)
        {
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                richText = true,
                wordWrap = true,
            };
            _styles[key] = st;
        }
        return st;
    }

    public static void Label(Rect r, string s, int size, Color c,
    TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
    {
        var st = Style(size, a, bold);
        st.normal.textColor = A(c);
        GUI.Label(r, s, st);
    }

    // ---------------- 基础图形 ----------------

    public static void Box(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static void Frame(Rect r, Color c, float t = 1f)
    {
        Box(new Rect(r.x, r.y, r.width, t), c);
        Box(new Rect(r.x, r.yMax - t, r.width, t), c);
        Box(new Rect(r.x, r.y, t, r.height), c);
        Box(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>
    /// 垂直渐变（色条坐标取整、首尾相接）。**只用于两端颜色不同**的场合（如 Backdrop 的整屏底色）。
    /// ⚠️ 用它叠在实景照片上会出水平条纹：GUI.matrix 缩放在非整数倍（1440/1080 = 1.333）时，
    /// 每条半透明色条的边缘落在非整数设备像素上，采样把相邻条混出周期明暗线。
    /// 单纯"同色变透明"的压暗/暗角请用 <see cref="FadeDown"/> / <see cref="FadeUp"/>（一张贴图一次画，无接缝）。
    /// </summary>
    public static void VGradient(Rect r, Color top, Color bottom, int steps = 0)
    {
        if (steps <= 0) steps = Mathf.Clamp(Mathf.RoundToInt(r.height / 4f), 2, 220);
        float h = r.height / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = steps <= 1 ? 0f : i / (float)(steps - 1);
            float y0 = Mathf.Round(r.y + i * h), y1 = Mathf.Round(r.y + (i + 1) * h);
            Box(new Rect(r.x, y0, r.width, y1 - y0), Color.Lerp(top, bottom, t));
        }
    }

    // ---------------- 单张贴图渐变（无接缝压暗，2026-11-10） ----------------

    static Texture2D _fadeV, _fadeH;

    /// <summary>1×256 白色贴图，alpha「上端 1 → 下端 0」。用 GUI.color 染色即为顶部压暗。</summary>
    static Texture2D FadeV()
    {
        if (_fadeV != null) return _fadeV;
        const int N = 256;
        var t = new Texture2D(1, N, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N];
        for (int i = 0; i < N; i++)
        {
            // 纹理坐标原点在左下：i = N-1 是顶部
            float a = i / (float)(N - 1);
            px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        t.SetPixels32(px);
        t.Apply();
        _fadeV = t;
        return t;
    }

    /// <summary>256×1 白色贴图，alpha「左端 1 → 右端 0」。</summary>
    static Texture2D FadeH()
    {
        if (_fadeH != null) return _fadeH;
        const int N = 256;
        var t = new Texture2D(N, 1, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N];
        for (int i = 0; i < N; i++)
        {
            float a = 1f - i / (float)(N - 1);
            px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        t.SetPixels32(px);
        t.Apply();
        _fadeH = t;
        return t;
    }

    /// <summary>区域顶部不透明、向下淡出。一次 DrawTexture，任何缩放下都不会有接缝。</summary>
    public static void FadeDown(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTexture(r, FadeV(), ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    /// <summary>区域底部不透明、向上淡出（垂直翻转 UV）。</summary>
    public static void FadeUp(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTextureWithTexCoords(r, FadeV(), new Rect(0f, 1f, 1f, -1f));
        GUI.color = old;
    }

    /// <summary>区域左侧不透明、向右淡出。</summary>
    public static void FadeRight(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTexture(r, FadeH(), ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    /// <summary>区域右侧不透明、向左淡出（水平翻转 UV）。</summary>
    public static void FadeLeft(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTextureWithTexCoords(r, FadeH(), new Rect(1f, 0f, -1f, 1f));
        GUI.color = old;
    }

    /// <summary>整屏底：上深下浅的冷色渐变。alpha &lt; 1 时能透出后面的战场。</summary>
    public static void Backdrop(float vw, float alpha)
    {
        VGradient(new Rect(0, 0, vw, 1080f),
        new Color(0.015f, 0.022f, 0.035f, alpha),
        new Color(0.040f, 0.062f, 0.088f, alpha), 24);
        FadeDown(new Rect(0, 0, vw, 160f), new Color(0.35f, 0.75f, 1f, 0.06f * alpha));
    }

    // ---------------- 实景背景（2026-11-10） ----------------

    /// <summary>水平渐变（左 → 右）。两端不同色时用这个；单纯同色变淡请用 FadeLeft/FadeRight。</summary>
    public static void HGradient(Rect r, Color left, Color right, int steps = 0)
    {
        if (steps <= 0) steps = Mathf.Clamp(Mathf.RoundToInt(r.width / 4f), 2, 220);
        float w = r.width / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = steps <= 1 ? 0f : i / (float)(steps - 1);
            float x0 = Mathf.Round(r.x + i * w), x1 = Mathf.Round(r.x + (i + 1) * w);
            Box(new Rect(x0, r.y, x1 - x0, r.height), Color.Lerp(left, right, t));
        }
    }

    /// <summary>四边压暗（暗角）。上/下满宽、左/右只画中段，四角由上下那两条负责，避免叠太重。</summary>
    public static void Vignette(float vw, float strength = 0.34f, float band = 220f)
    {
        float midH = 1080f - band * 1.2f;
        FadeDown(new Rect(0, 0, vw, band), new Color(0f, 0f, 0f, strength));
        FadeUp(new Rect(0, 1080f - band, vw, band), new Color(0f, 0f, 0f, strength));
        FadeRight(new Rect(0, band * 0.6f, band * 1.1f, midH), new Color(0f, 0f, 0f, strength * 0.8f));
        FadeLeft(new Rect(vw - band * 1.1f, band * 0.6f, band * 1.1f, midH), new Color(0f, 0f, 0f, strength * 0.8f));
    }

    /// <summary>
    /// 实景背景（开局封面 / 车库用）：铺满图片 → 整体压暗 → 冷色调 → 上下渐变 → 暗角。
    ///
    /// 为什么要这么叠：直接放一张渲染图会太亮、太花，UI 压不住；
    /// 压暗 + 冷调让它退到"背景"位置，上下渐变保证标题和底部文字始终读得清。
    /// tex 为 null 时退回原来的纯渐变（没跑生成器也不会崩）。
    /// </summary>
    public static void PhotoBackdrop(float vw, Texture2D tex, float darken = 0.50f,
        float topFade = 0.74f, float bottomFade = 0.88f, float vignette = 0.34f)
    {
        if (tex != null)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Alpha);
            GUI.DrawTexture(new Rect(0, 0, vw, 1080f), tex, ScaleMode.ScaleAndCrop, false);
            GUI.color = old;
        }
        else Backdrop(vw, 0.96f);

        // 整体压暗 + 冷色调（往 UI 的深蓝黑靠）
        Box(new Rect(0, 0, vw, 1080f), new Color(0.020f, 0.030f, 0.052f, darken));
        Box(new Rect(0, 0, vw, 1080f), new Color(0.12f, 0.24f, 0.44f, 0.10f));

        FadeDown(new Rect(0, 0, vw, 200f), new Color(0f, 0f, 0f, topFade));
        FadeUp(new Rect(0, 1080f - 240f, vw, 240f), new Color(0f, 0f, 0f, bottomFade));
        Vignette(vw, vignette);
    }

    /// <summary>标准面板：底色 + 顶部高光条 + 细描边。hover 时描边变金。</summary>
    public static void Card(Rect r, bool hover = false)
    {
        Box(r, hover ? PanelHover : PanelBg);
        Box(new Rect(r.x, r.y, r.width, 2f), hover ? new Color(1f, 0.84f, 0.35f, 0.70f)
            : new Color(0.35f, 0.75f, 1f, 0.35f));
        Frame(r, hover ? new Color(1f, 0.84f, 0.35f, 0.45f) : Edge);
    }

    public static void HLine(float x, float y, float w, float a = 0.13f)
        => Box(new Rect(x, y, w, 1f), new Color(0.35f, 0.75f, 1f, a));

    /// <summary>小标签：淡底 + 同色描边 + 居中文字。</summary>
    public static void Chip(Rect r, string s, Color c, int size = 16)
    {
        Box(r, new Color(c.r, c.g, c.b, 0.10f));
        Frame(r, new Color(c.r, c.g, c.b, 0.38f));
        Label(r, s, size, c, TextAnchor.MiddleCenter, true);
    }

    /// <summary>金色键帽（按键提示用）。</summary>
    public static void KeyCap(Rect r, string key, int size = 18)
    {
        Box(r, new Color(1f, 0.84f, 0.35f, 0.10f));
        Box(new Rect(r.x, r.y, 2f, r.height), new Color(1f, 0.84f, 0.35f, 0.55f));
        Label(r, key, size, Gold, TextAnchor.MiddleCenter, true);
    }

    /// <summary>横向数值条（底槽 + 填充）。</summary>
    public static void Bar(Rect r, float t, Color c)
    {
        Box(r, new Color(1f, 1f, 1f, 0.07f));
        Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), c);
    }

    // ---------------- 径向光晕（2026-11-10，给缩略图当背光） ----------------

    static Texture2D _blob;

    /// <summary>64² 的径向渐变（中间不透明、边缘透明，二次衰减）。第一次用时生成，之后复用。</summary>
    static Texture2D BlobTex()
    {
        if (_blob != null) return _blob;
        const int S = 64;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                  // 二次衰减：中间亮、边缘化得干净
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply();
        _blob = t;
        return t;
    }

    /// <summary>画一团径向光晕（用 GUI.color 染色，会乘上 Alpha）。给卡片里的车做背光。</summary>
    public static void Blob(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = A(c);
        GUI.DrawTexture(r, BlobTex(), ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    /// <summary>页面大标题 + 金色短横 + 副标题（居中）。返回占用到的底部 y。</summary>
    public static float Heading(float vw, float y, string title, string sub, int size = 46)
    {
        Label(new Rect(0, y, vw, size + 14f), title, size, Text, TextAnchor.MiddleCenter, true);
        float ly = y + size + 22f;
        Box(new Rect(vw * 0.5f - 70f, ly, 140f, 2f), new Color(1f, 0.84f, 0.35f, 0.75f));
        if (string.IsNullOrEmpty(sub)) return ly + 8f;
        Label(new Rect(0, ly + 12f, vw, 28f), sub, 20, Dim, TextAnchor.MiddleCenter);
        return ly + 44f;
    }

    // ================= 交互层（悬停 / 按下 / 点击反馈）=================
    // 全部用 unscaledTime：这些界面打开时 timeScale = 0。

    /// <summary>有没有任何界面挡在战斗前面（开火、换弹、锁定都要先问这个）。</summary>
    public static bool Blocking =>
        !GameStartScreen.Started
        || (GarageScreen.Instance != null && GarageScreen.Instance.IsOpen)
        || PauseScreen.Paused
        || ShopScreen.Open
        || Time.unscaledTime < _swallowUntil;

    static float _swallowUntil;

    /// <summary>界面刚关掉的这一小段时间里继续吞掉鼠标：
    /// 否则点完「出击」那一下左键还按着，车一出来就开一炮。</summary>
    public static void SwallowInput(float seconds = 0.3f)
        => _swallowUntil = Mathf.Max(_swallowUntil, Time.unscaledTime + seconds);

    class HoverState { public float t; public bool was; public float shakeAt = -9f; }
    static readonly Dictionary<string, HoverState> _hover = new Dictionary<string, HoverState>();

    struct Ripple { public Vector2 p; public float at; public Color c; }
    static readonly List<Ripple> _ripples = new List<Ripple>();

    static string _pressedId;       // 鼠标按下时落在哪个控件上（松开时还在上面才算点击）

    /// <summary>
    /// 一个可点击区域这一帧的交互状态：
    ///   hover01 —— 悬停程度 0..1（平滑过渡，画高亮用）
    ///   pressed —— 正按着
    ///   clicked —— 本帧完成一次点击（按下和松开都在区域内）
    ///   denied  —— 点了但不可用（会自动抖动 + 拒绝音）
    /// </summary>
    public struct Hit { public float hover01, shake; public bool hover, pressed, clicked, denied; }

    public static Hit Button(string id, Rect r, Vector2 mouse, bool enabled = true)
    {
        if (!_hover.TryGetValue(id, out var st)) { st = new HoverState(); _hover[id] = st; }
        var e = Event.current;
        bool inside = r.Contains(mouse);
        var h = new Hit { hover = inside && enabled };

        // 悬停淡入淡出（只在 Repaint 推进，一帧多个事件不会推得太快）
        if (e.type == EventType.Repaint)
        {
            st.t = Mathf.MoveTowards(st.t, h.hover ? 1f : 0f, Time.unscaledDeltaTime * 9f);
            if (h.hover && !st.was) UiSound.Play(UiSound.Kind.Hover);
            st.was = h.hover;
        }
        h.hover01 = Smooth(st.t);

        if (inside && e.type == EventType.MouseDown && e.button == 0)
        {
            _pressedId = id;
            if (enabled) UiSound.Play(UiSound.Kind.Click);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _pressedId == id)
        {
            _pressedId = null;
            if (inside)
            {
                e.Use();
                if (enabled)
                {
                    h.clicked = true;
                    AddRipple(mouse, Gold);
                }
                else
                {
                    h.denied = true;
                    st.shakeAt = Time.unscaledTime;
                    UiSound.Play(UiSound.Kind.Deny);
                    AddRipple(mouse, Bad);
                }
            }
        }
        h.pressed = _pressedId == id && inside && Input.GetMouseButton(0);

        float since = Time.unscaledTime - st.shakeAt;
        if (since < 0.32f) h.shake = Mathf.Sin(since * 70f) * 7f * (1f - since / 0.32f);
        return h;
    }

    /// <summary>悬停上浮一点、按下沉一点、拒绝时左右抖。</summary>
    public static Rect Lift(Rect r, Hit h)
    {
        float dy = h.pressed ? 2f : -3f * h.hover01;
        return new Rect(r.x + h.shake, r.y + dy, r.width, r.height);
    }

    /// <summary>悬停外发光（画在卡片之前）。</summary>
    public static void Glow(Rect r, float k, Color c)
    {
        if (k <= 0.01f) return;
        for (int i = 1; i <= 4; i++)
        {
            float g = i * 3f;
            Frame(new Rect(r.x - g, r.y - g, r.width + g * 2f, r.height + g * 2f),
            new Color(c.r, c.g, c.b, 0.12f * k * (1f - i / 5f)), 3f);
        }
    }

    static void AddRipple(Vector2 p, Color c)
    {
        _ripples.Add(new Ripple { p = p, at = Time.unscaledTime, c = c });
        if (_ripples.Count > 12) _ripples.RemoveAt(0);
    }

    /// <summary>画点击涟漪（界面最后调一次，压在最上层）。</summary>
    public static void DrawRipples()
    {
        if (!FxOnce(ref _rippleFrame)) return;      // 一帧只画一遍，别被两个界面重复画
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var rp = _ripples[i];
            float t = (Time.unscaledTime - rp.at) / 0.38f;
            if (t >= 1f)
            {
                _ripples.RemoveAt(i);
                continue;
            }
            float rad = 8f + 46f * Smooth(t);
            Frame(new Rect(rp.p.x - rad, rp.p.y - rad, rad * 2f, rad * 2f),
            new Color(rp.c.r, rp.c.g, rp.c.b, (1f - t) * 0.65f), 2f);
        }
    }

    // 一帧里 OnGUI 会跑好几遍（Layout / Repaint / 事件），涟漪和浮字都只在 Repaint 画、且每帧一次。
    static int _rippleFrame = -1, _floaterFrame = -1;

    static bool FxOnce(ref int frame)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return false;
        if (frame == Time.frameCount) return false;
        frame = Time.frameCount;
        return true;
    }

    // ---------------- 面板开合 ----------------

    /// <summary>开合进度：open 时 0→1，关时 1→0（约 0.15 s），状态存在调用方的 float 里。</summary>
    public static float Anim(ref float state, bool open, float speed = 6.5f)
    {
        if (Event.current.type == EventType.Repaint)
            state = Mathf.MoveTowards(state, open ? 1f : 0f, Time.unscaledDeltaTime * speed);
        return Smooth(state);
    }

    /// <summary>按开合进度把面板从略小、略低处弹出来。</summary>
    public static Rect PopIn(Rect r, float k)
    {
        float s = Mathf.Lerp(0.94f, 1f, k);
        float w = r.width * s, h = r.height * s;
        return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f + (1f - k) * 18f, w, h);
    }

    /// <summary>整体透明度（开合淡入）。画完用 EndScreen() 复位。</summary>
    public static void Fade(float k) => Alpha = Mathf.Clamp01(k);

    public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // ---------------- 浮动提示（-120 金币 / 已购买）----------------

    struct Floater { public string s; public Vector2 p; public float at; public Color c; }
    static readonly List<Floater> _floaters = new List<Floater>();

    public static void Float(string s, Vector2 p, Color c)
    {
        _floaters.Add(new Floater { s = s, p = p, at = Time.unscaledTime, c = c });
        if (_floaters.Count > 8) _floaters.RemoveAt(0);
    }

    public static void DrawFloaters()
    {
        if (!FxOnce(ref _floaterFrame)) return;
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            float t = (Time.unscaledTime - f.at) / 0.9f;
            if (t >= 1f)
            {
                _floaters.RemoveAt(i);
                continue;
            }
            float y = f.p.y - 40f * Smooth(t);
            Label(new Rect(f.p.x - 150f, y - 16f, 300f, 32f), f.s, 22,
            new Color(f.c.r, f.c.g, f.c.b, 1f - t * t), TextAnchor.MiddleCenter, true);
        }
    }
}
