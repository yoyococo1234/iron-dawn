using UnityEngine;

/// <summary>
/// 开局封面：标题 + 操作说明，点一下（或按空格/回车）才开始。
/// 开始前 Time.timeScale = 0 —— AI、炮弹、对局计时全部停着，不会一进游戏就被打。
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
    GUIStyle _title, _sub, _body, _hint, _center;

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
        // 不直接开打：先进车库选第一台载具（车库会继续把 timeScale 压成 0）
        var mm = MatchManager.Instance;
        if (mm != null) mm.OpenGarageFirst();
        else Time.timeScale = 1f;
    }

    // GUI.skin 只能在 OnGUI 里访问，样式延迟到第一次绘制再建
    void EnsureStyles()
    {
        if (_title != null) return;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 76, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        _title.normal.textColor = Color.white;
        _sub = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        _sub.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        _body = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        _body.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        _hint = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
        _hint.normal.textColor = new Color(1f, 0.84f, 0.35f);
        _center = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        _center.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
    }

    void OnGUI()
    {
        if (!_paused) return;
        EnsureStyles();

        float scale = Mathf.Max(0.35f, Screen.height / 1080f);
        float vw = Screen.width / scale;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // 压暗背景
        var old = GUI.color;
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        GUI.DrawTexture(new Rect(0, 0, vw, 1080f), Texture2D.whiteTexture);
        GUI.color = old;

        GUI.Label(new Rect(0, 240f, vw, 100f), title, _title);
        GUI.Label(new Rect(0, 340f, vw, 40f), subtitle, _sub);

        // 操作说明（两列）
        float bx = vw * 0.5f - 330f;
        string[] left =
        {
            "W / S　　　前进 / 后退",
            "A / D　　　车体转向",
            "鼠标　　　 炮塔瞄准",
            "左键 / 空格　开炮",
        };
        string[] right =
        {
            "1 / 2 / 3　切换弹药",
            "R　　　　　重新开始",
            "占资源点　　每 10 秒产金",
            "先到 12 击杀　获胜",
        };
        for (int i = 0; i < left.Length; i++)
        {
            GUI.Label(new Rect(bx, 440f + i * 38f, 300f, 30f), left[i], _body);
            GUI.Label(new Rect(bx + 320f, 440f + i * 38f, 320f, 30f), right[i], _body);
        }

        GUI.Label(new Rect(0, 700f, vw, 40f), hint, _hint);
        GUI.Label(new Rect(0, 750f, vw, 30f), "你驾驶的是轻坦 —— 快、皮薄，别正面硬冲", _center);

        GUI.matrix = oldMatrix;
    }
}
