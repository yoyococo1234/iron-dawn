using UnityEngine;

/// <summary>
/// 车库：开局和每次阵亡后在这里选载具（花金币买）。
/// 打开时游戏暂停（Time.timeScale = 0）、光标放开；选完自动关闭并恢复。
/// </summary>
public class GarageScreen : MonoBehaviour
{
    public static GarageScreen Instance { get; private set; }

    public bool IsOpen { get; private set; }

    string _title = "选择载具";
    string _subtitle = "";

    GUIStyle _s14, _s18, _s22, _s30, _s46, _center;

    static readonly Color CAlly = new Color(0.35f, 0.75f, 1f);
    static readonly Color CGold = new Color(1f, 0.84f, 0.35f);

    void Awake() => Instance = this;

    public void Open(string title, string subtitle = "")
    {
        _title = title;
        _subtitle = subtitle;
        IsOpen = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
    }

    void EnsureStyles()
    {
        if (_s14 != null) return;
        _s14 = Mk(14, TextAnchor.MiddleLeft);
        _s18 = Mk(18, TextAnchor.MiddleLeft);
        _s22 = Mk(22, TextAnchor.MiddleLeft);
        _s30 = Mk(30, TextAnchor.MiddleCenter);
        _s46 = Mk(46, TextAnchor.MiddleCenter);
        _center = Mk(20, TextAnchor.MiddleCenter);
    }

    static GUIStyle Mk(int size, TextAnchor anchor)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, richText = true };
        st.normal.textColor = Color.white;
        return st;
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        var mm = MatchManager.Instance;
        if (mm == null) return;

        EnsureStyles();

        float scale = Mathf.Max(0.35f, Screen.height / 1080f);
        float vw = Screen.width / scale;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        Fill(new Rect(0, 0, vw, 1080f), new Color(0.03f, 0.04f, 0.06f, 0.94f));

        Text(new Rect(0, 40f, vw, 60f), _title, _s46, Color.white, TextAnchor.MiddleCenter);
        if (!string.IsNullOrEmpty(_subtitle))
            Text(new Rect(0, 100f, vw, 28f), _subtitle, _center, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);

        // 状态行
        Text(new Rect(vw * 0.5f - 480f, 140f, 960f, 32f),
            $"我的金币 <b>{mm.Gold}</b>　　剩余部署 <b>{mm.LivesLeft}</b> 次　　敌方在场 <b>{mm.EnemiesLeft}</b> 台　　我方剩余命 <b>{mm.TeamLives(0)}</b>",
            _s22, CGold, TextAnchor.MiddleCenter);

        // 卡片
        var roster = mm.Roster;
        if (roster != null && roster.Length > 0)
        {
            int cols = 3;
            float cw = 400f, ch = 300f, gap = 26f;
            float totalW = cols * cw + (cols - 1) * gap;
            float x0 = vw * 0.5f - totalW * 0.5f;
            float y0 = 200f;

            var mouse = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);

            for (int i = 0; i < roster.Length; i++)
            {
                var def = roster[i];
                if (def == null) continue;

                int col = i % cols, row = i / cols;
                var r = new Rect(x0 + col * (cw + gap), y0 + row * (ch + gap), cw, ch);

                bool afford = mm.CanAfford(def);
                bool free = afford && mm.Gold < def.price;
                bool hover = r.Contains(mouse) && afford;

                Fill(r, hover ? new Color(0.12f, 0.16f, 0.22f, 0.98f) : new Color(0.07f, 0.09f, 0.12f, 0.95f));
                Fill(new Rect(r.x, r.y, 4f, r.height), afford ? CAlly : new Color(0.4f, 0.4f, 0.4f));

                Text(new Rect(r.x + 20f, r.y + 14f, r.width - 40f, 40f), def.displayName, _s30, afford ? Color.white : new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft);
                Text(new Rect(r.x + 20f, r.y + 56f, r.width - 40f, 24f), DomainText(def.domain), _s18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);

                Text(new Rect(r.x + 20f, r.y + 92f, r.width - 40f, 26f), $"血量 {def.maxHp:F0}　　速度 {def.speed:F0} m/s", _s18, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);
                string weapon = "无武器";
                if (def.weapons != null && def.weapons.Length > 0 && def.weapons[0] != null)
                {
                    weapon = $"{def.weapons[0].displayName}（{def.weapons[0].baseDamage:F0} 伤害 / {def.weapons[0].range:F0} m）";
                    if (def.weapons.Length > 1 && def.weapons[1] != null)
                        weapon += $" + {def.weapons[1].displayName}";
                }
                Text(new Rect(r.x + 20f, r.y + 122f, r.width - 40f, 24f), weapon, _s14, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleLeft);

                Text(new Rect(r.x + 20f, r.y + 158f, r.width - 40f, 60f), def.description, _s14, new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperLeft);

                Text(new Rect(r.x + 20f, r.y + ch - 56f, r.width - 40f, 40f),
                    afford ? (free ? "<b>免费补充</b>　点击出击" : $"<b>￥{def.price}</b>　点击出击") : $"￥{def.price}　金币不足",
                    _s22, afford ? CGold : new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);

                if (afford && Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(mouse))
                {
                    Event.current.Use();
                    mm.DeployPlayer(def);
                }
            }
        }
        else
        {
            Text(new Rect(0, 400f, vw, 40f), "没有可选载具（MatchDef.roster 为空，先跑菜单 ②）", _center, new Color(1f, 0.4f, 0.3f), TextAnchor.MiddleCenter);
        }

        GUI.matrix = oldMatrix;
    }

    static string DomainText(Domain d)
        => d == Domain.Sea ? "海战" : d == Domain.Air ? "空战" : "陆战";

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Text(Rect r, string s, GUIStyle style, Color c, TextAnchor a)
    {
        var oc = style.normal.textColor;
        var oa = style.alignment;
        style.normal.textColor = c;
        style.alignment = a;
        GUI.Label(r, s, style);
        style.normal.textColor = oc;
        style.alignment = oa;
    }
}
