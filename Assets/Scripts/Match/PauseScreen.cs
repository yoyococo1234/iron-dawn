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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("PauseScreen");
        go.AddComponent<PauseScreen>();
        Object.DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) Toggle();
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
            Time.timeScale = _savedScale;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.Locked;   // 立刻回到准星
            Cursor.visible = false;
            UiSound.Play(UiSound.Kind.Close);
            UiKit.SwallowInput(0.3f);                   // 免得点「继续」那一下顺手开炮
        }
    }

    static readonly string[] Keys = { "P", "TAB", "G", "R" };
    static readonly string[] Descs = { "继续战斗", "补给商店", "回车库换载具", "重开一局" };

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

        float pw = 460f, ph = 120f + Keys.Length * 46f + 30f;
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

        UiKit.EndScreen();          // 涟漪 + 浮字压在最上层，并复位透明度
        GUI.matrix = oldMatrix;
    }
}
