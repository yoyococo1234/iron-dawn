using UnityEngine;

/// <summary>
/// P 键暂停（2026-11-07 用户要求）。
/// 单人游戏，暂停时 Time.timeScale = 0：AI、炮弹、对局计时、据点产出全部停住。
///
/// 注意和另外两个也会压 timeScale 的系统共存：
///   GameStartScreen（开局封面，开局前一直压 0）
///   GarageScreen（车库，打开时压 0）
/// 暂停前如果 timeScale 已经是 0（说明是它们压的），我们只记住"暂停前是 1"，恢复时不会把它们顶掉。
/// 样式走 UiKit，与开局封面 / 车库 / 商店一致。
/// 2026-11-10：面板加开合动画；第一行「P 继续战斗」变成可点按钮。
/// </summary>
public class PauseScreen : MonoBehaviour
{
    public static bool Paused { get; private set; }

    float _savedScale = 1f;
    float _anim;                 // 开合进度（0 = 关 / 1 = 开）
    static bool _volDrag;        // 是否正在拖音量滑条（2026-11-11）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("PauseScreen");
        go.AddComponent<PauseScreen>();
        Object.DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (Bindings.Down(Bindings.Act.Pause)) Toggle();
        // 2026-11-12 用户要求：暂停时按 Esc 也能退出暂停
        else if (Paused && Input.GetKeyDown(KeyCode.Escape)) Toggle();
    }

    /// <summary>暂停 / 继续（P 键和面板上的按钮都走这里）。</summary>
    public void Toggle()
    {
        if (!Paused)
        {
            _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Paused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            UiSound.Play(UiSound.Kind.Open);
        }
        else
        {
            Paused = false;
            _confirmRestart = false;                    // 关面板就把"确认重开"的待确认状态清掉
            Time.timeScale = _savedScale;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.Locked;   // 立刻回到准星
            Cursor.visible = false;
            UiSound.Play(UiSound.Kind.Close);
            UiKit.SwallowInput(0.3f);                   // 免得点「继续」那一下顺手开炮
        }
    }

    static readonly string[] Keys = { "P", "TAB", "G" };
    static readonly string[] Descs = { "继续战斗", "补给商店", "回车库换载具" };

    /// <summary>2026-11-12 用户要求：删掉 R 键，重开改成暂停面板里的一个按钮（点两次确认）。</summary>
    bool _confirmRestart;

    /// <summary>重开一局时把静态暂停状态复位（LoadScene 不清静态字段）。</summary>
    public static void ClearPaused()
    {
        Paused = false;
    }

    void OnGUI()
    {
        // 开合动画：关掉以后还要多画 0.15 秒淡出，所以先推进进度再决定要不要画
        float k = UiKit.Anim(ref _anim, Paused);
        if (!Paused && k <= 0.001f) return;

        var oldMatrix = UiKit.BeginScaled(out float scale, out float vw);
        var mouse = UiKit.Mouse(scale);
        bool live = Paused;

        UiKit.Fade(k);
        // 半透明压暗，身后的战场仍可见
        UiKit.Box(new Rect(0, 0, vw, 1080f), new Color(0.01f, 0.015f, 0.025f, 0.66f));

        float pw = 460f, ph = 120f + Keys.Length * 46f + 54f + 54f + 30f + 56f;   // 键位行 + 换载具 + 重开 + 音量滑条
        var panel = UiKit.PopIn(new Rect(vw * 0.5f - pw * 0.5f, 540f - ph * 0.5f, pw, ph), k);
        UiKit.Card(panel);

        // 标题：呼吸的金色小点 + 已暂停
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
        UiKit.Box(new Rect(panel.x + 30f, panel.y + 40f, 10f, 10f), new Color(1f, 0.84f, 0.35f, pulse));
        UiKit.Label(new Rect(panel.x + 52f, panel.y + 22f, pw - 80f, 46f), "已暂停", 34, UiKit.Text, TextAnchor.MiddleLeft, true);
        UiKit.Label(new Rect(panel.x + 52f, panel.y + 64f, pw - 80f, 24f), "AI、炮弹和对局计时都已停住", 15, UiKit.Dim);

        UiKit.HLine(panel.x + 24f, panel.y + 104f, pw - 48f);

        float y = panel.y + 120f;
        for (int i = 0; i < Keys.Length; i++)
        {
            var rowR = new Rect(panel.x + 22f, y, pw - 44f, 40f);
            // 第一行「P 继续战斗」可以直接点（暂停时鼠标是放开的，用户本来就想去点）
            var hit = UiKit.Button("pause_" + Keys[i], rowR, mouse, i == 0 && live);
            float hv = hit.hover01;

            if (hv > 0.01f) UiKit.Box(rowR, new Color(1f, 0.84f, 0.35f, 0.10f * hv));
            UiKit.KeyCap(new Rect(panel.x + 30f, y + 4f, 86f, 28f), Keys[i], 16);
            UiKit.Label(new Rect(panel.x + 132f, y, pw - 160f, 36f), Descs[i], 18,
                hv > 0.5f ? UiKit.Gold : UiKit.Desc);
            if (i == 0 && hv > 0.5f)
                UiKit.Label(new Rect(panel.x + pw - 150f, y, 120f, 36f), "点击继续 ›", 16, UiKit.Gold, TextAnchor.MiddleRight, true);

            if (hit.clicked) Toggle();

            y += 46f;
        }

        // ---------- 换载具（2026-11-12 用户要求：暂停界面里也要能直接换车）----------
        {
            var mm = MatchManager.Instance;
            var br = new Rect(panel.x + 22f, y, pw - 44f, 44f);
            var hb = UiKit.Button("pause_swap", br, mouse, live);
            bool hotB = hb.hover01 > 0.4f;
            UiKit.Box(br, hotB ? new Color(1f, 0.84f, 0.35f, 0.16f) : new Color(1f, 1f, 1f, 0.03f));
            UiKit.Frame(br, hotB ? UiKit.Gold : UiKit.Edge);
            UiKit.Label(br, "换载具", 18, hotB ? UiKit.Gold : UiKit.Desc, TextAnchor.MiddleCenter, true);
            if (hb.clicked && mm != null)
            {
                UiSound.Play(UiSound.Kind.Open);
                Toggle();                       // 先退出暂停（否则车库关掉后 timeScale 会被抢回 1、状态打架）
                mm.OpenGarageForSwap();
                return;
            }
            y += 54f;
        }

        // ---------- 重新开始本局（2026-11-12 取代原来的 R 键）----------
        // 点第一次变成"再点一次确认"，第二次才真的重开 —— 免得手滑把一局打没。
        {
            var mm = MatchManager.Instance;
            var rr = new Rect(panel.x + 22f, y, pw - 44f, 44f);
            var h = UiKit.Button("pause_restart", rr, mouse, live);
            bool hot = h.hover01 > 0.4f;
            UiKit.Box(rr, _confirmRestart ? new Color(1f, 0.42f, 0.36f, 0.24f)
                                          : new Color(1f, 1f, 1f, 0.03f + 0.10f * h.hover01));
            UiKit.Frame(rr, (_confirmRestart || hot) ? UiKit.Bad : UiKit.Edge);
            UiKit.Label(rr, _confirmRestart ? "再点一次确认重开本局" : "重新开始本局", 18,
                        (_confirmRestart || hot) ? UiKit.Bad : UiKit.Desc, TextAnchor.MiddleCenter, true);
            if (h.clicked)
            {
                if (!_confirmRestart) { _confirmRestart = true; UiSound.Play(UiSound.Kind.Click); }
                else
                {
                    _confirmRestart = false;
                    UiSound.Play(UiSound.Kind.Confirm);
                    if (mm != null) mm.Restart();
                    return;                       // 场景要重载了，这一帧别再往下画
                }
            }
            y += 54f;
        }

        // ---------- 界面音效音量（2026-11-11 补 §7.3）----------
        // UiSound.Volume 之前硬编码 0.55，没有地方能调。这里给一条可拖的滑条。
        // 注意：UiKit.Button 会吃掉鼠标事件，所以滑条自己处理 Event.current，且**不要**和按钮重叠。
        UiKit.HLine(panel.x + 24f, y + 4f, pw - 48f);
        float vy = y + 18f;
        UiKit.Label(new Rect(panel.x + 30f, vy, pw - 60f, 26f), "界面音效音量", 16, UiKit.Desc, TextAnchor.MiddleLeft);
        UiKit.Label(new Rect(panel.x + pw - 110f, vy, 80f, 26f), $"{Mathf.RoundToInt(UiSound.Volume * 100f)}%", 16,
                    UiKit.Gold, TextAnchor.MiddleRight, true);

        var track = new Rect(panel.x + 30f, vy + 30f, pw - 60f, 10f);
        if (live)
        {
            var e = Event.current;
            if (e != null)
            {
                if (e.type == EventType.MouseDown && track.Contains(mouse)) { _volDrag = true; e.Use(); }
                else if (e.type == EventType.MouseUp && _volDrag) { _volDrag = false; e.Use(); }
            }
            if (_volDrag) UiSound.Volume = Mathf.Clamp01((mouse.x - track.x) / Mathf.Max(1f, track.width));
        }
        UiKit.Box(track, new Color(1f, 1f, 1f, 0.14f));
        UiKit.Box(new Rect(track.x, track.y, track.width * Mathf.Clamp01(UiSound.Volume), track.height), UiKit.Gold);
        UiKit.Box(new Rect(track.x + track.width * Mathf.Clamp01(UiSound.Volume) - 3f, track.y - 6f, 6f, track.height + 12f),
                 new Color(1f, 1f, 1f, 0.92f));

        UiKit.EndScreen();          // 涟漪 + 浮字压在最上层，并复位透明度
        GUI.matrix = oldMatrix;
    }
}
