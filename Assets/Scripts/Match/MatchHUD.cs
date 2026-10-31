using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 对局 HUD：时间 / 击杀比分 / 金币 / 资源点状态 / 玩家血量弹药 / 击杀提示 / 结算面板。
///
/// 用虚拟 1920×1080 坐标系绘制（按屏幕高度等比缩放），所以 720p~4K 都不会错位。
/// 只读 MatchManager，不改任何游戏状态。
/// </summary>
public class MatchHUD : MonoBehaviour
{
    const float VH = 1080f;

    float _scale = 1f;
    float _vw = 1920f;

    GUIStyle _s18, _s20, _s24, _s32, _s56;

    static readonly Color CAlly = new Color(0.35f, 0.75f, 1f);
    static readonly Color CEnemy = new Color(1f, 0.42f, 0.35f);
    static readonly Color CNeutral = new Color(0.78f, 0.78f, 0.78f);
    static readonly Color CGold = new Color(1f, 0.84f, 0.35f);
    static readonly Color CPanel = new Color(0.05f, 0.06f, 0.08f, 0.72f);

    void Awake()
    {
        // 注意：GUI.skin 只能在 OnGUI 内部访问，样式必须延迟到第一次 OnGUI 再建，
        // 否则会抛 "You can only call GUI functions from inside OnGUI" 导致整个 HUD 画不出来。
    }

    void EnsureStyles()
    {
        if (_s18 != null) return;
        _s18 = Style(18, TextAnchor.MiddleLeft);
        _s20 = Style(20, TextAnchor.MiddleLeft);
        _s24 = Style(24, TextAnchor.MiddleCenter);
        _s32 = Style(32, TextAnchor.MiddleCenter);
        _s56 = Style(56, TextAnchor.MiddleCenter);
    }

    static GUIStyle Style(int size, TextAnchor anchor)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, richText = true };
        st.normal.textColor = Color.white;
        return st;
    }

    void OnGUI()
    {
        var mm = MatchManager.Instance;
        if (mm == null) return;

        EnsureStyles();

        _scale = Mathf.Max(0.35f, Screen.height / VH);
        _vw = Screen.width / _scale;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

        DrawTopBar(mm);
        DrawGold(mm);
        DrawZones(mm);
        DrawPlayerPanel(mm);
        DrawFeed(mm);
        if (mm.matchOver) DrawSettlement(mm);

        GUI.matrix = oldMatrix;
    }

    // ---------------- 顶部：时间 + 比分 ----------------

    void DrawTopBar(MatchManager mm)
    {
        float w = 420f, h = 76f;
        float x = _vw * 0.5f - w * 0.5f;
        Panel(new Rect(x, 12f, w, h));

        Text(new Rect(x, 16f, w, 30f), mm.TimeText, _s32, Color.white, TextAnchor.MiddleCenter);

        Text(new Rect(x + 16f, 46f, w * 0.5f - 16f, 26f), $"我方 <b>{mm.kills[0]}</b>", _s20, CAlly, TextAnchor.MiddleLeft);
        Text(new Rect(x + w * 0.5f, 46f, w * 0.5f - 16f, 26f), $"<b>{mm.kills[1]}</b> 敌方", _s20, CEnemy, TextAnchor.MiddleRight);

        Text(new Rect(x, 88f, w, 22f), $"先到 {mm.killTarget} 击杀获胜", _s18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
    }

    // ---------------- 左上：金币 ----------------

    void DrawGold(MatchManager mm)
    {
        Panel(new Rect(16f, 12f, 240f, 92f));
        Text(new Rect(28f, 16f, 216f, 40f), $"<b>{mm.gold[0]}</b> 金", _s32, CGold, TextAnchor.MiddleLeft);
        Text(new Rect(28f, 58f, 216f, 24f), $"敌方 {mm.gold[1]} 金", _s18, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleLeft);
        Text(new Rect(28f, 80f, 216f, 20f), "资源点每 10 秒产金", _s18, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
    }

    // ---------------- 左侧：资源点 ----------------

    void DrawZones(MatchManager mm)
    {
        if (mm.zones.Count == 0) return;

        float w = 300f, rowH = 46f;
        float h = 34f + mm.zones.Count * rowH;
        float y0 = _vw > 1400f ? 200f : 200f;
        Panel(new Rect(16f, y0, w, h));

        Text(new Rect(28f, y0 + 6f, w - 24f, 26f), "资源点", _s20, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);

        for (int i = 0; i < mm.zones.Count; i++)
        {
            var z = mm.zones[i];
            float y = y0 + 34f + i * rowH;

            Text(new Rect(28f, y, 170f, 22f), z.DisplayName, _s18, Color.white, TextAnchor.MiddleLeft);
            Text(new Rect(w - 78f, y, 62f, 22f), z.OwnerText, _s18, z.OwnerColor, TextAnchor.MiddleRight);

            // 占领进度条
            var bar = new Rect(28f, y + 24f, w - 56f, 10f);
            Fill(bar, new Color(1f, 1f, 1f, 0.12f));
            var teamColor = z.progressTeam == 0 ? CAlly : z.progressTeam == 1 ? CEnemy : CNeutral;
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(z.progress), bar.height), teamColor);
        }
    }

    // ---------------- 底部：玩家状态 ----------------

    void DrawPlayerPanel(MatchManager mm)
    {
        var p = mm.Player;
        if (p == null) return;

        float w = 520f, h = 104f;
        float x = _vw * 0.5f - w * 0.5f, y = VH - h - 16f;
        Panel(new Rect(x, y, w, h));

        if (!p.Alive)
        {
            float eta = mm.RespawnEta(p);
            Text(new Rect(x, y + 16f, w, 40f), "已击毁", _s32, CEnemy, TextAnchor.MiddleCenter);
            Text(new Rect(x, y + 58f, w, 26f), eta > 0f ? $"复活倒计时 {eta:F1} 秒" : "等待复活（队伍金币不足）", _s20, Color.white, TextAnchor.MiddleCenter);
            return;
        }

        var def = p.Def;
        string name = def != null ? def.displayName : p.name;
        Text(new Rect(x + 20f, y + 10f, 300f, 26f), name, _s20, Color.white, TextAnchor.MiddleLeft);

        // 血条
        var hpBar = new Rect(x + 20f, y + 40f, w - 40f, 18f);
        Fill(hpBar, new Color(1f, 1f, 1f, 0.12f));
        float ratio = Mathf.Clamp01(p.hp / Mathf.Max(1f, p.maxHp));
        var hpColor = ratio > 0.55f ? new Color(0.45f, 0.85f, 0.45f)
                    : ratio > 0.25f ? new Color(0.95f, 0.75f, 0.25f)
                                    : new Color(0.95f, 0.35f, 0.3f);
        Fill(new Rect(hpBar.x, hpBar.y, hpBar.width * ratio, hpBar.height), hpColor);
        Text(new Rect(hpBar.x + 8f, hpBar.y - 2f, 260f, 22f), $"{p.hp:F0} / {p.maxHp:F0}", _s18, Color.black, TextAnchor.MiddleLeft);

        // 弹药与装填
        var w2 = p.Weapon;
        string ammo = p.Ammo != null ? p.Ammo.displayName : "—";
        Text(new Rect(x + 20f, y + 66f, w - 40f, 24f), $"弹药：{ammo}　武器：{(w2 != null ? w2.displayName : "—")}", _s18, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);

        var rlBar = new Rect(x + 20f, y + 92f, w - 40f, 8f);
        Fill(rlBar, new Color(1f, 1f, 1f, 0.10f));
        Fill(new Rect(rlBar.x, rlBar.y, rlBar.width * Mathf.Clamp01(p.ReloadProgress), rlBar.height), CGold);

        Text(new Rect(x + w - 240f, y + 66f, 220f, 24f), "左键开火 · 1/2/3 切弹", _s18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleRight);
    }

    // ---------------- 右上：击杀提示 ----------------

    void DrawFeed(MatchManager mm)
    {
        float w = 420f, rowH = 26f;
        float y = 16f;
        float x = _vw - w - 16f;

        int shown = 0;
        for (int i = mm.feed.Count - 1; i >= 0 && shown < 5; i--)
        {
            var e = mm.feed[i];
            float age = Time.time - e.time;
            if (age > 6f) continue;
            float alpha = Mathf.Clamp01(1f - (age - 4f) / 2f);
            Panel(new Rect(x, y, w, rowH - 2f), new Color(0.05f, 0.06f, 0.08f, 0.55f * alpha));
            Text(new Rect(x + 10f, y, w - 20f, rowH - 2f), e.text, _s18,
                new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleLeft);
            y += rowH;
            shown++;
        }
    }

    // ---------------- 结算 ----------------

    void DrawSettlement(MatchManager mm)
    {
        Fill(new Rect(0, 0, _vw, VH), new Color(0f, 0f, 0f, 0.6f));

        float w = 640f, h = 360f;
        float x = _vw * 0.5f - w * 0.5f, y = VH * 0.5f - h * 0.5f;
        Panel(new Rect(x, y, w, h), new Color(0.07f, 0.08f, 0.11f, 0.96f));

        bool win = mm.winnerTeam == 0;
        Text(new Rect(x, y + 30f, w, 70f), win ? "胜利" : "失败", _s56, win ? CAlly : CEnemy, TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 110f, w, 30f), mm.resultText, _s20, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);

        Text(new Rect(x, y + 170f, w, 34f), $"我方 {mm.kills[0]}　:　{mm.kills[1]} 敌方", _s32, Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 216f, w, 28f), $"金币 {mm.gold[0]}　·　剩余时间 {mm.TimeText}", _s20, CGold, TextAnchor.MiddleCenter);

        Text(new Rect(x, y + 280f, w, 40f), "按 <b>R</b> 重新开始", _s24, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter);
    }

    // ---------------- 绘制工具 ----------------

    static void Panel(Rect r, Color? c = null)
        => Fill(r, c ?? CPanel);

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Text(Rect r, string text, GUIStyle style, Color c, TextAnchor anchor)
    {
        var old = style.normal.textColor;
        var oldAnchor = style.alignment;
        style.normal.textColor = c;
        style.alignment = anchor;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
        style.alignment = oldAnchor;
    }
}
