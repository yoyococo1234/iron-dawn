using UnityEngine;

/// <summary>
/// 对局 HUD：剩余时间 / 剩余命数 / 敌方剩余 / 金币 / 资源点 / 玩家血量弹药 / 击杀提示 / 结算面板 / 准星。
/// 用虚拟 1920×1080 坐标系绘制（按屏幕高度等比缩放），720p~4K 都不会错位。
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
        if (!GameStartScreen.Started) return;
        if (GarageScreen.Instance != null && GarageScreen.Instance.IsOpen) return;

        EnsureStyles();

        _scale = Mathf.Max(0.35f, Screen.height / VH);
        _vw = Screen.width / _scale;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

        DrawScope(mm);            // 先画镜筒（盖住画面），HUD 再画在上面
        DrawTopBar(mm);
        DrawGold(mm);
        DrawZones(mm);
        DrawPlayerPanel(mm);
        DrawFeed(mm);
        DrawCrosshair(mm);
        if (mm.matchOver) DrawSettlement(mm);

        GUI.matrix = oldMatrix;
    }

    // ---------------- 顶部：时间 + 击杀数 ----------------

    void DrawTopBar(MatchManager mm)
    {
        float w = 520f, h = 80f;
        float x = _vw * 0.5f - w * 0.5f;
        Panel(new Rect(x, 12f, w, h));

        Text(new Rect(x, 16f, w, 30f), mm.TimeText, _s32, Color.white, TextAnchor.MiddleCenter);

        Text(new Rect(x + 16f, 46f, w * 0.5f - 16f, 26f), $"我方击杀 <b>{mm.kills[0]}</b>", _s20, CAlly, TextAnchor.MiddleLeft);
        Text(new Rect(x + w * 0.5f, 46f, w * 0.5f - 16f, 26f), $"<b>{mm.kills[1]}</b> 敌方击杀", _s20, CEnemy, TextAnchor.MiddleRight);

        Text(new Rect(x, 94f, w, 22f), $"剩余命 我方 {mm.TeamLives(0)} / 敌方 {mm.TeamLives(1)}　·　在场 {mm.TeamAlive(0)} / {mm.TeamAlive(1)}",
             _s18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter);
    }

    // ---------------- 左上：金币（个人） ----------------

    void DrawGold(MatchManager mm)
    {
        Panel(new Rect(16f, 12f, 250f, 78f));
        Text(new Rect(28f, 16f, 226f, 40f), $"<b>￥{mm.Gold}</b>", _s32, CGold, TextAnchor.MiddleLeft);
        Text(new Rect(28f, 56f, 226f, 22f), $"我的命 {mm.LivesText} · 占点全队产金", _s18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
    }

    // ---------------- 左侧：资源点 ----------------

    void DrawZones(MatchManager mm)
    {
        if (mm.zones.Count == 0) return;

        float w = 300f, rowH = 46f;
        float h = 34f + mm.zones.Count * rowH;
        float y0 = 160f;
        Panel(new Rect(16f, y0, w, h));

        Text(new Rect(28f, y0 + 6f, w - 24f, 26f), "资源点", _s20, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);

        for (int i = 0; i < mm.zones.Count; i++)
        {
            var z = mm.zones[i];
            float y = y0 + 34f + i * rowH;

            Text(new Rect(28f, y, 170f, 22f), z.DisplayName, _s18, Color.white, TextAnchor.MiddleLeft);
            Text(new Rect(w - 78f, y, 62f, 22f), z.OwnerText, _s18, z.OwnerColor, TextAnchor.MiddleRight);

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
        float w = 520f, h = 104f;
        float x = _vw * 0.5f - w * 0.5f, y = VH - h - 16f;

        if (p == null || !p.Alive)
        {
            Panel(new Rect(x, y, w, h));
            Text(new Rect(x, y + 20f, w, 36f), "载具已损失", _s32, CEnemy, TextAnchor.MiddleCenter);
            Text(new Rect(x, y + 62f, w, 26f),
                mm.unlimitedLives ? "按 G 回车库换车" : (mm.LivesLeft > 0 ? "正在返回车库…" : "没有剩余部署机会"),
                _s20, Color.white, TextAnchor.MiddleCenter);
            return;
        }

        Panel(new Rect(x, y, w, h));

        var def = p.Def;
        string name = def != null ? def.displayName : p.name;
        Text(new Rect(x + 20f, y + 10f, 300f, 26f), name, _s20, Color.white, TextAnchor.MiddleLeft);

        var hpBar = new Rect(x + 20f, y + 40f, w - 40f, 18f);
        Fill(hpBar, new Color(1f, 1f, 1f, 0.12f));
        float ratio = Mathf.Clamp01(p.hp / Mathf.Max(1f, p.maxHp));
        var hpColor = ratio > 0.55f ? new Color(0.45f, 0.85f, 0.45f)
                    : ratio > 0.25f ? new Color(0.95f, 0.75f, 0.25f)
                                    : new Color(0.95f, 0.35f, 0.3f);
        Fill(new Rect(hpBar.x, hpBar.y, hpBar.width * ratio, hpBar.height), hpColor);
        Text(new Rect(hpBar.x + 8f, hpBar.y - 2f, 260f, 22f), $"{p.hp:F0} / {p.maxHp:F0}", _s18, Color.black, TextAnchor.MiddleLeft);

        var w2 = p.Weapon;
        string ammo = p.Ammo != null ? p.Ammo.displayName : "—";
        Text(new Rect(x + 20f, y + 64f, 300f, 24f), $"弹药 {ammo} · 武器 {(w2 != null ? w2.displayName : "—")}", _s18, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);

        var rlBar = new Rect(x + 20f, y + 92f, w - 40f, 8f);
        Fill(rlBar, new Color(1f, 1f, 1f, 0.10f));
        Fill(new Rect(rlBar.x, rlBar.y, rlBar.width * Mathf.Clamp01(p.ReloadProgress), rlBar.height), CGold);

        Text(new Rect(x + w - 230f, y + 64f, 210f, 24f), "Q 切武器 · 右键瞄准 · Shift 拉近", _s18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight);
    }

    // ---------------- 右上：击杀提示 ----------------

    void DrawFeed(MatchManager mm)
    {
        float w = 460f, rowH = 26f;
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
            Text(new Rect(x + 10f, y, w - 20f, rowH - 2f), e.text, _s18, new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleLeft);
            y += rowH;
            shown++;
        }
    }

    // ---------------- 准星 ----------------

    void DrawCrosshair(MatchManager mm)
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;   // 光标放开时不画准星
        if (IsScoped()) return;                                   // 开镜时用炮镜分划，不画这个

        var p = mm.Player;
        bool air = p != null && p.Def != null && p.Def.domain == Domain.Air;

        float x = _vw * 0.5f, y = VH * 0.5f;

        if (air && p.Physics != null)
        {
            // 空战准星 = **机头指向（炮口方向）**投影到屏幕上的位置，不再钉在屏幕中心。
            // 子弹就是沿机头方向打出去的（见 PlayerTankController.GetAimPoint），
            // 所以准星在哪、子弹就打在哪 —— 平飞时它落在天边（机头指哪打哪），俯冲时跟着压到地面。
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 muzzleDir = p.Physics.transform.forward;
            Vector3 world = p.Physics.transform.position + muzzleDir * 900f;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;                 // 机头指向跑到镜头背后，不画

            x = sp.x / _scale;
            y = (Screen.height - sp.y) / _scale;

            // 夹在画面内，免得机头抬高时准星飞出屏幕找不到
            x = Mathf.Clamp(x, 48f, _vw - 48f);
            y = Mathf.Clamp(y, 48f, VH - 48f);

            // 开镜（Shift）：换成专门的空战分划，画在同一个位置
            if (IsAirScoped()) { DrawAirScope(x, y); return; }

            var c = new Color(1f, 0.88f, 0.35f, 0.95f);
            float r = 16f, t = 2.5f;
            Fill(new Rect(x - r, y - t * 0.5f, r * 0.55f, t), c);   // 左
            Fill(new Rect(x + r * 0.45f, y - t * 0.5f, r * 0.55f, t), c);  // 右
            Fill(new Rect(x - t * 0.5f, y - r, t, r * 0.55f), c);   // 上
            Fill(new Rect(x - t * 0.5f, y + r * 0.45f, t, r * 0.55f), c);  // 下
            Fill(new Rect(x - 2f, y - 2f, 4f, 4f), new Color(1f, 0.32f, 0.26f, 0.95f));
            return;
        }

        // ---------- 陆战 / 海战：只画**炮塔准星** ----------
        // 屏幕中心那个"人视角准星"不画（战雷里它也不是瞄准用的）。
        // 画的是炮口真正指向的屏幕位置 —— 炮塔没转过来时它会停在画面边上，
        // 并且变成箭头指方向；炮塔转过来之后它就回到画面中间。
        var vpG = p != null && p.Physics != null ? p.Physics : null;
        var camG = Camera.main;
        if (vpG == null || camG == null) return;

        Vector3 gunPoint = vpG.GetMuzzlePosition() + vpG.GetMuzzleForward() * 500f;
        Vector3 local = camG.transform.InverseTransformPoint(gunPoint);   // z>0 = 在镜头前方

        // 方向（屏幕坐标系：右 +x、上 +y）；在镜头背后时整体翻转，指回真实方向
        Vector2 dirN = new Vector2(local.x, local.y);
        if (local.z < 0f) dirN = -dirN;
        if (dirN.sqrMagnitude < 1e-6f) dirN = Vector2.up;
        dirN.Normalize();

        float cx0 = _vw * 0.5f, cy0 = VH * 0.5f;
        float halfW = _vw * 0.5f - 52f, halfH = VH * 0.5f - 52f;

        // 先把真实位置投影出来，看是不是在画面内
        Vector3 spG = camG.WorldToScreenPoint(gunPoint);
        float gx = spG.x / _scale;
        float gy = (Screen.height - spG.y) / _scale;
        bool onScreen = spG.z > 0f && gx > 52f && gx < _vw - 52f && gy > 52f && gy < VH - 52f;

        if (onScreen)
        {
            x = gx; y = gy;
        }
        else
        {
            // 炮口指向在画面外：贴到边缘，用箭头表示方向
            float tEdge = Mathf.Min(halfW / Mathf.Max(0.001f, Mathf.Abs(dirN.x)),
                                    halfH / Mathf.Max(0.001f, Mathf.Abs(dirN.y)));
            x = cx0 + dirN.x * tEdge;
            y = cy0 - dirN.y * tEdge;
        }
        var cTank = onScreen ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 0.88f, 0.35f, 0.9f);

        // 十字
        Fill(new Rect(x - 16f, y - 1.5f, 32f, 3f), cTank);
        Fill(new Rect(x - 1.5f, y - 16f, 3f, 32f), cTank);
        Fill(new Rect(x - 2f, y - 2f, 4f, 4f), new Color(1f, 0.32f, 0.26f, 0.95f));

        if (!onScreen)
        {
            // 边缘箭头：指向炮口实际方向
            Vector2 perp = new Vector2(-dirN.y, dirN.x);
            float tipX = x + dirN.x * 16f, tipY = y - dirN.y * 16f;
            float bx = x - dirN.x * 4f, by = y + dirN.y * 4f;
            for (int i = 0; i < 10; i++)
            {
                float f = i / 10f;
                float px = Mathf.Lerp(bx - perp.x * 10f, tipX, f);
                float py = Mathf.Lerp(by - perp.y * 10f, tipY, f);
                Fill(new Rect(px - 3f, py - 3f, 6f, 6f), cTank);
                float qx = Mathf.Lerp(bx + perp.x * 10f, tipX, f);
                float qy = Mathf.Lerp(by + perp.y * 10f, tipY, f);
                Fill(new Rect(qx - 3f, qy - 3f, 6f, 6f), cTank);
            }
        }
    }

    // ---------------- 炮镜（Shift 按住，战雷那套圆形镜筒 + 十字分划） ----------------

    static Texture2D _scopeMask;

    static CameraFollow CamFollow()
    {
        var cam = Camera.main;
        return cam != null ? cam.GetComponent<CameraFollow>() : null;
    }

    static bool IsScoped()
    {
        var cf = CamFollow();
        return cf != null && cf.IsScoped;
    }

    static bool IsAirScoped()
    {
        var cf = CamFollow();
        return cf != null && cf.AirScoped;
    }

    /// <summary>飞机开镜的分划：细圆环 + 四向刻线 + 中心点 + 放大倍率。不压黑边（不是炮镜）。</summary>
    void DrawAirScope(float cx, float cy)
    {
        var cf = CamFollow();
        float zoom = cf != null && cf.airZoomFov > 1f ? cf.normalFov / cf.airZoomFov : 2f;

        float R = Mathf.Min(_vw, VH) * 0.13f;
        var ring = new Color(0.95f, 0.92f, 0.78f, 0.75f);
        var dim = new Color(0.95f, 0.92f, 0.78f, 0.35f);

        // 细圆环（96 个小点近似）
        for (int i = 0; i < 96; i++)
        {
            float a = i / 96f * Mathf.PI * 2f;
            float x = cx + Mathf.Cos(a) * R;
            float y = cy + Mathf.Sin(a) * R;
            Fill(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), ring);
        }

        // 四个方向的刻线（留中心空隙）
        float gap = 12f, len = R * 0.42f, t = 2f;
        Fill(new Rect(cx - R - len, cy - t * 0.5f, len, t), ring);      // 左
        Fill(new Rect(cx + R, cy - t * 0.5f, len, t), ring);            // 右
        Fill(new Rect(cx - t * 0.5f, cy - R - len, t, len), ring);      // 上
        Fill(new Rect(cx - t * 0.5f, cy + R, t, len), ring);            // 下

        // 内圈四点（空战瞄准环常见的"漏斗"标记）
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
            float x = cx + Mathf.Cos(a) * (R * 0.55f);
            float y = cy + Mathf.Sin(a) * (R * 0.55f);
            Fill(new Rect(x - 2f, y - 2f, 4f, 4f), dim);
        }

        // 中心点
        Fill(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), new Color(1f, 0.32f, 0.26f, 1f));

        // 倍率 + 提示
        Text(new Rect(0f, cy + R + len + 10f, _vw, 24f), $"×{zoom:F1}　·　松开 Shift 退出开镜", _s20,
             new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
    }

    /// <summary>圆形镜筒遮罩：圆内透明、圆外纯黑（方形四角也被压黑）。只生成一次。</summary>
    static Texture2D ScopeMask()
    {
        if (_scopeMask != null) return _scopeMask;
        const int S = 512;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = S * 0.5f, r = S * 0.5f - 1f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01((d - 0.955f) / 0.045f);   // 0 = 圆内（透明），1 = 圆外（黑）
                px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255));
            }
        }
        t.SetPixels32(px);
        t.Apply();
        _scopeMask = t;
        return t;
    }

    void DrawScope(MatchManager mm)
    {
        if (!IsScoped()) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float cx = _vw * 0.5f, cy = VH * 0.5f;
        float R = Mathf.Min(_vw, VH) * 0.34f;
        var black = new Color(0f, 0f, 0f, 1f);

        // 圆外压黑：四个矩形 + 方形四角
        Fill(new Rect(0f, 0f, _vw, cy - R), black);
        Fill(new Rect(0f, cy + R, _vw, VH - (cy + R)), black);
        Fill(new Rect(0f, cy - R, cx - R, R * 2f), black);
        Fill(new Rect(cx + R, cy - R, _vw - (cx + R), R * 2f), black);
        GUI.DrawTexture(new Rect(cx - R, cy - R, R * 2f, R * 2f), ScopeMask(), ScaleMode.StretchToFill, true);

        // 镜筒内圈（一圈小方块近似，够圆）
        var ring = new Color(0.06f, 0.06f, 0.06f, 0.85f);
        for (int i = 0; i < 96; i++)
        {
            float a = i / 96f * Mathf.PI * 2f;
            float x = cx + Mathf.Cos(a) * (R - 2f);
            float y = cy + Mathf.Sin(a) * (R - 2f);
            Fill(new Rect(x - 3f, y - 3f, 6f, 6f), ring);
        }

        // ---- 十字分划 ----
        var ret = new Color(0.95f, 0.92f, 0.78f, 0.9f);
        float gap = 10f, thick = 2f;
        Fill(new Rect(cx - R * 0.92f, cy - thick * 0.5f, R * 0.92f - gap, thick), ret);   // 左横
        Fill(new Rect(cx + gap, cy - thick * 0.5f, R * 0.92f - gap, thick), ret);         // 右横
        Fill(new Rect(cx - thick * 0.5f, cy - R * 0.55f, thick, R * 0.55f - gap), ret);   // 上竖
        Fill(new Rect(cx - thick * 0.5f, cy + gap, thick, R * 0.85f - gap), ret);         // 下竖
        Fill(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), new Color(1f, 0.3f, 0.25f, 1f));     // 中心红点

        // 横向密位刻度
        for (int i = 1; i <= 6; i++)
        {
            float d = i * R * 0.13f;
            float h = (i % 2 == 0) ? 11f : 6f;
            Fill(new Rect(cx - d - 1f, cy - h * 0.5f, 2f, h), ret);
            Fill(new Rect(cx + d - 1f, cy - h * 0.5f, 2f, h), ret);
        }
        // 纵向测距刻度
        for (int i = 1; i <= 5; i++)
        {
            float d = i * R * 0.16f;
            float w = (i % 2 == 0) ? 11f : 6f;
            Fill(new Rect(cx - w * 0.5f, cy + d - 1f, w, 2f), ret);
        }

        // 镜筒下方提示
        Text(new Rect(0f, cy + R + 8f, _vw, 26f), "炮镜　·　松开 Shift 退出", _s20,
             new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
    }

    // ---------------- 结算 ----------------

    void DrawSettlement(MatchManager mm)
    {
        Fill(new Rect(0, 0, _vw, VH), new Color(0f, 0f, 0f, 0.6f));

        float w = 660f, h = 360f;
        float x = _vw * 0.5f - w * 0.5f, y = VH * 0.5f - h * 0.5f;
        Panel(new Rect(x, y, w, h), new Color(0.07f, 0.08f, 0.11f, 0.96f));

        bool win = mm.winnerTeam == 0;
        Text(new Rect(x, y + 30f, w, 70f), win ? "胜利" : "失败", _s56, win ? CAlly : CEnemy, TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 110f, w, 30f), mm.resultText, _s20, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);

        Text(new Rect(x, y + 170f, w, 34f), $"我方命 {mm.TeamLives(0)}　:　{mm.TeamLives(1)} 敌方命", _s32, Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 216f, w, 28f), $"我的金币 {mm.Gold}　·　剩余时间 {mm.TimeText}", _s20, CGold, TextAnchor.MiddleCenter);

        Text(new Rect(x, y + 280f, w, 40f), "按 <b>R</b> 重新开始", _s24, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter);
    }

    // ---------------- 绘制工具 ----------------

    static void Panel(Rect r, Color? c = null) => Fill(r, c ?? CPanel);

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
