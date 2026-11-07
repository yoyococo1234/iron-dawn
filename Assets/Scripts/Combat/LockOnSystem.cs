using UnityEngine;

/// <summary>
/// Ctrl 锁定瞄准（2026-11-07 用户要求）。
///
/// 按住 Ctrl：自动吸附到"屏幕里能打到、最近的那台敌车"
///   · 炮塔/机头自动对准目标（PlayerTankController 会直接拿锁定点当瞄准点）
///   · HUD 画出锁定框 + 距离 + 是否进入射程（射程内绿色，超射程橙色）
///   · 这时候按左键，炮弹带轻微制导，保证打得中
///
/// 松开 Ctrl 立刻解除。
/// </summary>
public class LockOnSystem : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static VehicleCombat Target { get; private set; }
    public static bool InRange { get; private set; }
    public static float Distance { get; private set; }

    /// <summary>锁定搜索半径（米）。</summary>
    public float maxRange = 1600f;

    static LockOnSystem _inst;
    float _lockTime;
    float _flash;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("LockOnSystem");
        _inst = go.AddComponent<LockOnSystem>();
        Object.DontDestroyOnLoad(go);
    }

    static void Clear()
    {
        Active = false;
        Target = null;
        InRange = false;
    }

    void Update()
    {
        var mm = MatchManager.Instance;
        var player = mm != null ? mm.Player : null;

        bool want = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!want || player == null || !player.Alive || PauseScreen.Paused || ShopScreen.Open)
        {
            Clear();
            return;
        }

        Active = true;

        // 目标没了 / 死了 / 跑太远 → 重新挑一个
        if (Target == null || !Target.Alive ||
            Vector3.Distance(player.transform.position, Target.transform.position) > maxRange)
        {
            var next = PickTarget(player);
            if (next != Target) { Target = next; _lockTime = 0f; _flash = 1f; }
        }

        if (Target != null)
        {
            _lockTime += Time.deltaTime;
            Distance = Vector3.Distance(player.transform.position, Target.transform.position);
            float range = player.Weapon != null ? player.Weapon.range : 800f;
            InRange = Distance <= range;
        }
        else
        {
            Distance = 0f;
            InRange = false;
        }

        _flash = Mathf.Max(0f, _flash - Time.deltaTime * 3f);
    }

    /// <summary>挑"屏幕里、有视线、最近"的敌人。</summary>
    VehicleCombat PickTarget(VehicleCombat player)
    {
        var cam = Camera.main;
        if (cam == null) return null;

        VehicleCombat best = null;
        float bestScore = float.MaxValue;
        Vector3 from = player.transform.position + Vector3.up * 2f;

        foreach (var v in VehicleCombat.AllAlive())
        {
            if (v == null || !v.Alive || v == player) continue;
            if (v.team == player.team) continue;

            Vector3 aim = v.transform.position + Vector3.up * 1.2f;
            float d = Vector3.Distance(from, aim);
            if (d > maxRange) continue;

            // 必须在屏幕中间区域（避免锁到视野外的东西）
            Vector3 sp = cam.WorldToScreenPoint(aim);
            if (sp.z <= 0f) continue;
            float sx = Mathf.Abs(sp.x / Screen.width - 0.5f);
            float sy = Mathf.Abs(sp.y / Screen.height - 0.5f);
            if (sx > 0.45f || sy > 0.45f) continue;

            // 有视线（被山挡住的不锁）
            if (Physics.Linecast(from, aim, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                var hitVc = hit.collider.GetComponentInParent<VehicleCombat>();
                if (hitVc != v) continue;
            }

            float score = d * 0.01f + (sx + sy) * 6f;
            if (score < bestScore) { bestScore = score; best = v; }
        }
        return best;
    }

    void OnGUI()
    {
        if (!Active) return;
        var cam = Camera.main;
        if (cam == null) return;

        float w = Screen.width, h = Screen.height;

        // 锁定中：屏幕四角淡青色渐变（"进入锁定模式"的氛围）
        var col = InRange ? new Color(0.35f, 1f, 0.55f, 0.10f) : new Color(1f, 0.65f, 0.25f, 0.10f);
        if (Target != null) DrawVignette(col);

        if (Target == null)
        {
            var noT = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            noT.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(0, h * 0.60f, w, 24f), "锁定中 · 视野内没有可锁定的目标", noT);
            return;
        }

        Vector3 aim = Target.transform.position + Vector3.up * 1.2f;
        Vector3 sp2 = cam.WorldToScreenPoint(aim);
        if (sp2.z <= 0f) return;
        float px = sp2.x;
        float py = h - sp2.y;

        // 锁定框大小：随距离缩放，锁上后有一个"收紧"的动画
        float dist = Distance;
        float size = Mathf.Clamp(22000f / Mathf.Max(60f, dist), 26f, 120f);
        float tighten = Mathf.Clamp01(_lockTime / 0.35f);
        size *= Mathf.Lerp(1.6f, 1f, tighten);

        Color c = InRange ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.7f, 0.3f);
        c.a = 0.9f;
        DrawBrackets(px, py, size, c);
        if (_flash > 0.01f) DrawBrackets(px, py, size * (1f + _flash * 0.35f), new Color(c.r, c.g, c.b, _flash * 0.7f));

        // 中心十字到目标的连线
        DrawLine(new Vector2(w * 0.5f, h * 0.5f), new Vector2(px, py), new Color(c.r, c.g, c.b, 0.25f), 1.5f);

        // 距离 + 射程状态
        var st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        st.normal.textColor = c;
        string rangeTxt = InRange ? $"{dist:F0} m · 射程内 · 左键发射" : $"{dist:F0} m · 超出射程";
        GUI.Label(new Rect(px - 140, py + size * 0.6f + 6f, 280, 22f), rangeTxt, st);
    }

    void DrawVignette(Color c)
    {
        float w = Screen.width, h = Screen.height;
        float band = Mathf.Min(w, h) * 0.12f;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0, 0, w, band), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, h - band, w, band), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, 0, band, h), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(w - band, 0, band, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void DrawBrackets(float cx, float cy, float r, Color c)
    {
        float len = r * 0.42f;
        var t = Texture2D.whiteTexture;
        GUI.color = c;
        // 四个角
        GUI.DrawTexture(new Rect(cx - r, cy - r, len, 2.5f), t);
        GUI.DrawTexture(new Rect(cx - r, cy - r, 2.5f, len), t);
        GUI.DrawTexture(new Rect(cx + r - len, cy - r, len, 2.5f), t);
        GUI.DrawTexture(new Rect(cx + r - 2.5f, cy - r, 2.5f, len), t);
        GUI.DrawTexture(new Rect(cx - r, cy + r - 2.5f, len, 2.5f), t);
        GUI.DrawTexture(new Rect(cx - r, cy + r - len, 2.5f, len), t);
        GUI.DrawTexture(new Rect(cx + r - len, cy + r - 2.5f, len, 2.5f), t);
        GUI.DrawTexture(new Rect(cx + r - 2.5f, cy + r - len, 2.5f, len), t);
        GUI.color = Color.white;
    }

    void DrawLine(Vector2 a, Vector2 b, Color c, float width)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        var m = GUI.matrix;
        GUI.color = c;
        GUIUtility.RotateAroundPivot(ang, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
        GUI.matrix = m;
        GUI.color = Color.white;
    }
}
