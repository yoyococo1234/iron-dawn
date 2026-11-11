using System.Collections.Generic;
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
        DrawDamageVignette();     // 2026-11-10 受伤红边：画在 HUD 之前，别把数字盖住
        DrawTopBar(mm);
        DrawGold(mm);
        DrawZones(mm);
        DrawPlayerPanel(mm);
        DrawFeed(mm);
        DrawRadar(mm);
        DrawNamePlates(mm);       // 2026-11-11：名字牌（画在准星之前）
        DrawDamageDirection();    // 2026-11-11：挨打时的受击方向弧
        DrawCrosshair(mm);
        // 2026-11-10：命中标记复活成准星处的四角斜线（击穿金色 / 未击穿·跳弹红色）。
        // 语音播报照旧（Sfx.VoRicochet / VoNonPen / VoPen），两者不冲突：语音给"听"，标记给"看"。
        DrawHitFeedback();
        DrawGunBrokenAlert(mm);
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
        DrawGoldPops();     // 2026-11-10 金币变化浮字（差值在 Update 里算，这里只画）
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
        // 2026-11-11：弹药类型取消（只有武器有区别），这行只显示武器名 + 备弹数。
        string ammoTxt = $"武器 {(w2 != null ? w2.displayName : "—")}";
        int left = p.CurrentAmmoLeft;
        int cap = p.CurrentAmmoCapacity;
        if (cap > 0) ammoTxt += $"  备弹 {left}/{cap}";
        // 宽度从 420 收到 280：右边那行操作提示是从 x+w-230 起画的，420 会压到提示文字上（2026-11-10）
        Text(new Rect(x + 20f, y + 64f, 280f, 24f), ammoTxt, _s18, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);

        // 装填条：只在真装填时画。ReloadProgress 平时就是 1，以前这里常亮一条金色横杠，
        // 看着像 UI 出错的装饰线（2026-11-10）
        var rlBar = new Rect(x + 20f, y + 92f, w - 40f, 8f);
        float rl = p.ReloadProgress;
        if (rl < 0.999f)
        {
            Fill(rlBar, new Color(1f, 1f, 1f, 0.10f));
            Fill(new Rect(rlBar.x, rlBar.y, rlBar.width * Mathf.Clamp01(rl), rlBar.height), CGold);
        }

        Text(new Rect(x + w - 230f, y + 64f, 210f, 24f), "Q 切武器 · 右键瞄准", _s18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight);

        // 炮艇：显示还剩几发干扰弹
        if (def != null && def.domain == Domain.Sea)
        {
            var pc = p.GetComponent<PlayerTankController>();
            int n = pc != null ? pc.DecoyCharges : 0;
            Text(new Rect(x + w - 230f, y + 92f, 210f, 22f), $"干扰弹 ×{n}　X 释放", _s18,
                 n > 0 ? CGold : new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleRight);
        }

        // 部位状态（部位伤害系统）：一排小方块，打坏的标红
        var vmod = p.GetComponent<VehicleModule>();
        if (vmod != null) DrawModuleStatus(vmod, def != null ? def.domain : Domain.Land, x, y - 32f, w);
    }

    void DrawModuleStatus(VehicleModule mod, Domain domain, float x, float y, float w)
    {
        var kinds = new List<ModuleKind> { ModuleKind.Engine, ModuleKind.TurretRing, ModuleKind.Gun, ModuleKind.AmmoRack, ModuleKind.Fuel };
        var names = new List<string> { "发动机", "炮塔环", "炮管", "弹药架", "油箱" };
        if (domain == Domain.Land) { kinds.Insert(4, ModuleKind.Tracks); names.Insert(4, "履带"); }

        float cw = 88f, gap = 6f;
        float total = kinds.Count * cw + (kinds.Count - 1) * gap;
        float sx = x + (w - total) * 0.5f;

        for (int i = 0; i < kinds.Count; i++)
        {
            bool broken = mod.IsBroken(kinds[i]);
            string label = (broken ? "✕ " : "✓ ") + names[i];
            // 炮管坏了会哑火，玩家必须知道多久修好（2026-11-09 修：以前只有一个红块，玩家以为是鼠标失灵）
            if (kinds[i] == ModuleKind.Gun && broken)
            {
                float rem = mod.GunRepairRemaining;
                if (rem > 0f) label = "修理中 " + Mathf.CeilToInt(rem) + "s";
            }
            var r = new Rect(sx + i * (cw + gap), y, cw, 26f);
            Fill(r, broken ? new Color(0.55f, 0.12f, 0.10f, 0.9f) : new Color(0.10f, 0.22f, 0.14f, 0.75f));
            Text(r, label, _s18,
                 broken ? CEnemy : new Color(0.65f, 0.9f, 0.7f), TextAnchor.MiddleCenter);
        }
    }

    /// <summary>炮管损坏时在准星下方给一条醒目提示：玩家点左键没反应，必须先知道是炮坏了。</summary>
    void DrawGunBrokenAlert(MatchManager mm)
    {
        var p = mm.Player;
        if (p == null || !p.Alive) return;
        var vmod = p.GetComponent<VehicleModule>();
        if (vmod == null || !vmod.IsBroken(ModuleKind.Gun)) return;

        float rem = vmod.GunRepairRemaining;
        string txt = rem > 0f
            ? "炮管损坏 · 修理中 " + Mathf.CeilToInt(rem) + "s"
            : "炮管损坏 · 左键无法开火（TAB 商店可修）";
        var r = new Rect(0f, VH * 0.5f + 74f, _vw, 34f);
        Fill(new Rect(_vw * 0.5f - 190f, r.y - 2f, 380f, 34f), new Color(0.35f, 0.05f, 0.05f, 0.78f));
        Text(r, txt, _s20, new Color(1f, 0.55f, 0.5f, 1f), TextAnchor.MiddleCenter);
    }

    // ---------------- 右上：击杀提示 ----------------

    readonly int[] _feedPick = new int[5];     // 本帧要画哪几条（按"新→旧"存，最多 5 条；feed 本身上限 6）

    void DrawFeed(MatchManager mm)
    {
        float w = 460f, rowH = 26f;
        float y = 16f;
        float x = _vw - w - 16f;

        // 先按"新→旧"挑出最多 5 条（和原来的取法一致），再反过来画 ——
        // 2026-11-10：新条目要从右边滑进来，画在旧条目**上面**才不会被旧底板压住。
        int n = 0;
        for (int i = mm.feed.Count - 1; i >= 0 && n < 5; i--)
        {
            if (Time.time - mm.feed[i].time > 6f) continue;
            _feedPick[n] = i;
            n++;
        }
        if (n == 0) return;

        // 2026-11-10 入场动画：
        // 最新那条 0.18 s 内从 +40 px 滑到位并淡入；它一出现，下面整摞要让出一格 ——
        // 让的过程同样按 age 缓动，否则会"啪"地跳一格，看着像掉帧。
        // 位移量只取 0.6 格：滑入的那条本身是半透明、还在从右边进场，
        // 让满一格的话两条会在 0.18 s 里正面叠在一起，文字糊成一团。
        float topAge = Time.time - mm.feed[_feedPick[0]].time;
        float pushK = Smooth01(Mathf.Clamp01(topAge / 0.18f));

        for (int s = n - 1; s >= 0; s--)          // s = 0 是最新的一条（最上面）
        {
            var e = mm.feed[_feedPick[s]];
            float age = Time.time - e.time;

            float inK = Smooth01(Mathf.Clamp01(age / 0.18f));            // 0 = 刚出现
            float alpha = Mathf.Clamp01(1f - (age - 4f) / 2f) * inK;     // 老条目照旧 4 s 后 2 s 淡出
            if (alpha <= 0.002f) continue;

            float dy = s == 0 ? 0f : (1f - pushK) * rowH * 0.6f;         // 还没让完：先画在上一格
            float dx = (1f - inK) * 40f;                                 // 从屏幕右边缘滑入
            float ry = y + s * rowH - dy;

            Panel(new Rect(x + dx, ry, w, rowH - 2f), new Color(0.05f, 0.06f, 0.08f, 0.55f * alpha));
            Text(new Rect(x + dx + 10f, ry, w - 20f, rowH - 2f), e.text, _s18, new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleLeft);
        }
    }

    // ---------------- 准星 ----------------

    Vector2 _lockCrossPos;
    bool _lockCrossInit;

    // 本帧准星画在虚拟画布的哪个位置（2026-11-10）。
    // 命中标记要跟着准星走：陆战/海战的准星是"炮口指向的投影"，炮塔没转过来时会停在画面边上，
    // 这时候如果把命中标记画在屏幕正中，玩家会以为打偏了。
    static Vector2 _crossPos = new Vector2(-1f, -1f);
    static int _crossFrame = -1;

    static void MarkCross(float x, float y)
    {
        _crossPos = new Vector2(x, y);
        _crossFrame = Time.frameCount;
    }

    /// <summary>锁定准星：画在目标身上（绿色），带一圈断开的锁定弧。</summary>
    void DrawLockCrosshair(Vector2 pos)
    {
        var c = new Color(0.45f, 1f, 0.6f, 0.95f);
        float x = pos.x, y = pos.y;
        float r = 20f, t = 2.5f;

        Fill(new Rect(x - r, y - t * 0.5f, r * 0.6f, t), c);
        Fill(new Rect(x + r * 0.4f, y - t * 0.5f, r * 0.6f, t), c);
        Fill(new Rect(x - t * 0.5f, y - r, t, r * 0.6f), c);
        Fill(new Rect(x - t * 0.5f, y + r * 0.4f, t, r * 0.6f), c);
        Fill(new Rect(x - 2f, y - 2f, 4f, 4f), new Color(1f, 1f, 1f, 0.95f));

        float R = 34f;
        for (int i = 0; i < 32; i++)
        {
            if (i % 8 < 4) continue;                     // 断成 4 段弧
            float a0 = i / 32f * Mathf.PI * 2f;
            float a1 = (i + 1) / 32f * Mathf.PI * 2f;
            DrawSeg(new Vector2(x + Mathf.Cos(a0) * R, y + Mathf.Sin(a0) * R),
                    new Vector2(x + Mathf.Cos(a1) * R, y + Mathf.Sin(a1) * R), c, 2f);
        }
    }

    void DrawSeg(Vector2 a, Vector2 b, Color c, float w)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.5f) return;

        // 2026-11-10：原来这里用 GUIUtility.RotateAroundPivot(angle, a)。
        // 它内部会拿 pivot 去做 GUIClip.Unclip（当成**屏幕像素**），可此时 GUI.matrix 已经
        // 按屏幕高度缩放过了（scale = Screen.height / 1080），于是旋转中心落到了另一个点，
        // 整条线段被甩偏出画面 —— 表现就是"命中标记只有中心点、四条斜线不见了"。
        // 改成在虚拟画布坐标系里自己做「平移到 a + 绕 a 旋转」：左乘已有的 GUI.matrix 即可。
        var oldM = GUI.matrix;
        var oldC = GUI.color;
        GUI.matrix = oldM * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f),
            Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), Vector3.one);
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, -w * 0.5f, len, w), Texture2D.whiteTexture);
        GUI.color = oldC;
        GUI.matrix = oldM;
    }

    void DrawCrosshair(MatchManager mm)
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;   // 光标放开时不画准星
        if (IsScoped()) return;                                   // 开镜时用炮镜分划，不画这个

        // ---------- Ctrl 锁定且目标进入射程 → 准星平滑移到目标身上（2026-11-07）----------
        if (LockOnSystem.Active && LockOnSystem.Target != null && LockOnSystem.Target.Alive && LockOnSystem.InRange)
        {
            var camL = Camera.main;
            if (camL != null)
            {
                Vector3 spL = camL.WorldToScreenPoint(LockOnSystem.Target.transform.position + Vector3.up * 1.2f);
                if (spL.z > 0f)
                {
                    Vector2 want = new Vector2(spL.x / _scale, (Screen.height - spL.y) / _scale);
                    if (!_lockCrossInit) { _lockCrossPos = want; _lockCrossInit = true; }
                    float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
                    _lockCrossPos = Vector2.Lerp(_lockCrossPos, want, k);
                    DrawLockCrosshair(_lockCrossPos);
                    MarkCross(_lockCrossPos.x, _lockCrossPos.y);
                    return;
                }
            }
        }
        else _lockCrossInit = false;

        var p = mm.Player;

        // ---------- 机枪模式（V）：机枪有自己一套准星/镜，不走炮塔那一套 ----------
        var ptcHud = p != null ? p.GetComponent<PlayerTankController>() : null;
        if (ptcHud != null && ptcHud.MgMode) { DrawMgReticle(p); return; }

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
            if (IsAirScoped()) { DrawAirScope(x, y); MarkCross(x, y); return; }

            MarkCross(x, y);
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

        // 2026-11-07：炮口节点挂在会随浪摇摆的 Model 上，用它画准星会跟着上下抖（用户反复反馈）。
        // 改成"车体/船体根部 + 炮塔偏航 + 炮管俯仰"算一个**稳定**的指向，准星就不晃了。
        float yawStable = vpG.transform.eulerAngles.y + vpG.turretYaw;
        Vector3 dirStable = Quaternion.Euler(-vpG.barrelPitch, yawStable, 0f) * Vector3.forward;
        Vector3 gunPoint = vpG.transform.position + Vector3.up * 2.5f + dirStable * 500f;
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
        MarkCross(x, y);

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

    static bool IsMgScoped()
    {
        var cf = CamFollow();
        return cf != null && cf.MgScoped;
    }

    /// <summary>
    /// 机枪准星（V 进入机枪模式）。
    /// 不开镜：画**枪管指向**的屏幕投影（机枪挂在炮塔上，炮塔没转过来时会贴边，和炮塔准星一个道理）。
    /// 开镜（Shift）：画机枪分划，样式和飞机开镜那套一样（细圆环 + 刻线 + 中心红点 + 倍率）。
    /// </summary>
    void DrawMgReticle(VehicleCombat p)
    {
        if (p == null || p.Physics == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (IsMgScoped())
        {
            DrawMgScope(_vw * 0.5f, VH * 0.5f);
            return;
        }

        // 2026-11-07：机枪弹道已经改成"按准星方向打"（FireDeckGunAt(aimPoint)），
        // 准星也必须画在**准星方向**上。原来准星跟着枪管走、子弹跟着准星走，两边对不上，
        // 玩家按准星瞄却打不中 —— 这就是"机枪打不中"的观感来源。
        Vector3 world = cam.transform.position + cam.transform.forward * 400f;
        Vector3 sp = cam.WorldToScreenPoint(world);

        float x = sp.x / _scale, y = (Screen.height - sp.y) / _scale;
        bool onScreen = sp.z > 0f && x > 52f && x < _vw - 52f && y > 52f && y < VH - 52f;

        if (!onScreen)
        {
            Vector3 local = cam.transform.InverseTransformPoint(world);
            Vector2 d = new Vector2(local.x, local.y);
            if (local.z < 0f) d = -d;
            if (d.sqrMagnitude < 1e-6f) d = Vector2.up;
            d.Normalize();
            float halfW = _vw * 0.5f - 52f, halfH = VH * 0.5f - 52f;
            float t = Mathf.Min(halfW / Mathf.Max(0.001f, Mathf.Abs(d.x)),
                                halfH / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            x = _vw * 0.5f + d.x * t;
            y = VH * 0.5f - d.y * t;
        }

        var c = onScreen ? new Color(1f, 0.95f, 0.75f, 0.9f) : new Color(1f, 0.88f, 0.35f, 0.9f);
        // 散布圈（机枪连射的视觉提示）
        var dimC = new Color(c.r, c.g, c.b, 0.26f);
        for (int i = 0; i < 40; i++)
        {
            float a = i / 40f * Mathf.PI * 2f;
            Fill(new Rect(x + Mathf.Cos(a) * 26f - 1.5f, y + Mathf.Sin(a) * 26f - 1.5f, 3f, 3f), dimC);
        }
        // 小十字
        Fill(new Rect(x - 13f, y - 1.5f, 26f, 3f), c);
        Fill(new Rect(x - 1.5f, y - 13f, 3f, 26f), c);
        Fill(new Rect(x - 2f, y - 2f, 4f, 4f), new Color(1f, 0.32f, 0.26f, 0.95f));

        Text(new Rect(0f, y + 34f, _vw, 22f), "机枪　·　Shift 开镜　V 退出", _s20,
             new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter);
    }

    /// <summary>机枪开镜分划：细圆环 + 四向刻线 + 密位刻度 + 中心红点 + 倍率。样式对齐飞机开镜那套。</summary>
    void DrawMgScope(float cx, float cy)
    {
        var cf = CamFollow();
        float zoom = cf != null && cf.mgScopeFov > 1f ? cf.normalFov / cf.mgScopeFov : 2f;

        float R = Mathf.Min(_vw, VH) * 0.17f;
        var ring = new Color(0.95f, 0.92f, 0.78f, 0.8f);
        var dim = new Color(0.95f, 0.92f, 0.78f, 0.38f);

        // 细圆环
        for (int i = 0; i < 96; i++)
        {
            float a = i / 96f * Mathf.PI * 2f;
            Fill(new Rect(cx + Mathf.Cos(a) * R - 1.5f, cy + Mathf.Sin(a) * R - 1.5f, 3f, 3f), ring);
        }

        // 四向刻线（留中心空隙）
        float gap = 10f, len = R * 0.45f, t = 2f;
        Fill(new Rect(cx - R - len, cy - t * 0.5f, len, t), ring);
        Fill(new Rect(cx + R, cy - t * 0.5f, len, t), ring);
        Fill(new Rect(cx - t * 0.5f, cy - R - len, t, len), ring);
        Fill(new Rect(cx - t * 0.5f, cy + R, t, len), ring);

        // 横向密位刻度
        for (int i = 1; i <= 5; i++)
        {
            float d = i * R * 0.16f;
            float h = (i % 2 == 0) ? 9f : 5f;
            Fill(new Rect(cx - d - 1f, cy - h * 0.5f, 2f, h), dim);
            Fill(new Rect(cx + d - 1f, cy - h * 0.5f, 2f, h), dim);
        }
        // 纵向测距刻度（往下，机枪打远目标要抬枪）
        for (int i = 1; i <= 4; i++)
        {
            float d = i * R * 0.18f;
            float w = (i % 2 == 0) ? 9f : 5f;
            Fill(new Rect(cx - w * 0.5f, cy + d - 1f, w, 2f), dim);
        }

        // 中心红点 + 内圈四点
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
            Fill(new Rect(cx + Mathf.Cos(a) * (R * 0.52f) - 2f, cy + Mathf.Sin(a) * (R * 0.52f) - 2f, 4f, 4f), dim);
        }
        Fill(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), new Color(1f, 0.32f, 0.26f, 1f));

        Text(new Rect(0f, cy + R + len + 8f, _vw, 24f), $"机枪 ×{zoom:F1}　·　松开 Shift 退出开镜", _s20,
             new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
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

    float _settleAt = -9f;      // 结算开始时刻（unscaledTime，在 Update 里只取一次）
    bool _settleWasOver;        // 上一帧 matchOver 的值，用来抓"刚结束"那一帧

    void DrawSettlement(MatchManager mm)
    {
        // 2026-11-10 入场：0.4 s 内从 0.94 缩放 + 淡入弹到位。
        // 时刻在 Update 里记 —— OnGUI 一帧会跑 Layout/Repaint/事件好几遍，在这里推进状态动画会快 3~4 倍。
        float k = Smooth01(Mathf.Clamp01((Time.unscaledTime - _settleAt) / 0.4f));

        // 全屏压黑单独画、不跟着缩放：跟着缩会在屏幕四边漏出一圈战场，很难看
        Fill(new Rect(0, 0, _vw, VH), new Color(0f, 0f, 0f, 0.6f * k));

        float w = 660f, h = 360f;
        float x = _vw * 0.5f - w * 0.5f, y = VH * 0.5f - h * 0.5f;

        // 面板绕自己的中心缩放：不位移，手感比"从下面升上来"更利落
        var oldM = GUI.matrix;
        float pop = Mathf.Lerp(0.94f, 1f, k);
        GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(_vw * 0.5f, VH * 0.5f));

        Panel(new Rect(x, y, w, h), WithA(new Color(0.07f, 0.08f, 0.11f, 0.96f), k));

        bool win = mm.winnerTeam == 0;
        // 标题「胜利/失败」额外弹一下：1.0 → 1.08 → 1.0（正弦半周，0.42 s，延迟 0.22 s 等面板先到位）
        float tp = Mathf.Clamp01((Time.unscaledTime - _settleAt - 0.22f) / 0.42f);
        float tScale = 1f + 0.08f * Mathf.Sin(tp * Mathf.PI);
        var oldM2 = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(tScale, tScale), new Vector2(_vw * 0.5f, y + 65f));
        Text(new Rect(x, y + 30f, w, 70f), win ? "胜利" : "失败", _s56, WithA(win ? CAlly : CEnemy, k), TextAnchor.MiddleCenter);
        GUI.matrix = oldM2;

        Text(new Rect(x, y + 110f, w, 30f), mm.resultText, _s20, WithA(new Color(1f, 1f, 1f, 0.8f), k), TextAnchor.MiddleCenter);

        Text(new Rect(x, y + 170f, w, 34f), $"我方命 {mm.TeamLives(0)}　:　{mm.TeamLives(1)} 敌方命", _s32, WithA(Color.white, k), TextAnchor.MiddleCenter);
        Text(new Rect(x, y + 216f, w, 28f), $"我的金币 {mm.Gold}　·　剩余时间 {mm.TimeText}", _s20, WithA(CGold, k), TextAnchor.MiddleCenter);

        // 2026-11-11：把「按 R 重新开始」那行文字换成**可点按钮**（R 键照旧有效）。
        // 坑：这一层 GUI.matrix 既缩放过（_scale）又套了一层 ScaleAroundPivot(pop)，
        // 命中判断必须把鼠标坐标**先还原 pop**，否则缩放 0.94 的那 0.4 s 里按钮点不中。
        var btn = new Rect(x + w * 0.5f - 160f, y + 272f, 320f, 58f);
        Vector2 vp = new Vector2(Input.mousePosition.x / _scale,
                                 (Screen.height - Input.mousePosition.y) / _scale);
        float pcx = _vw * 0.5f, pcy = VH * 0.5f;
        Vector2 vpUnpop = new Vector2(pcx + (vp.x - pcx) / Mathf.Max(0.01f, pop),
                                      pcy + (vp.y - pcy) / Mathf.Max(0.01f, pop));
        bool over = k > 0.6f && btn.Contains(vpUnpop);

        Fill(btn, WithA(over ? new Color(1f, 0.84f, 0.35f, 0.22f) : new Color(0.12f, 0.14f, 0.18f, 0.95f), k));
        Fill(new Rect(btn.x, btn.y, btn.width, 2f), WithA(CGold, 0.85f * k));
        Fill(new Rect(btn.x, btn.yMax - 2f, btn.width, 2f), WithA(CGold, 0.85f * k));
        Fill(new Rect(btn.x, btn.y, 2f, btn.height), WithA(CGold, 0.85f * k));
        Fill(new Rect(btn.xMax - 2f, btn.y, 2f, btn.height), WithA(CGold, 0.85f * k));
        Text(btn, over ? "<b>再来一局</b>（R）" : "再来一局（R）", _s24,
             WithA(over ? CGold : new Color(1f, 1f, 1f, 0.9f), k), TextAnchor.MiddleCenter);

        if (over && Event.current != null && Event.current.type == EventType.MouseUp)
        {
            Event.current.Use();
            _wantRestart = true;      // 不在 OnGUI 里 LoadScene：等下一帧 Update 再重启
        }

        GUI.matrix = oldM;
    }

    // ================= 受击方向指示（2026-11-11） =================
    //
    // 用户原话："显示别人距离的那个箭头不要了，被打的时候才显示一下，不过也不能用这种水平的箭头了，
    // 因为现在的箭头我都看不出来敌人在哪，很废物。"
    //
    // 所以改成：**只在挨打时**在屏幕中心外一圈画一段红色圆弧，弧心正对**打你那个人的方位**，
    // 1.6 秒内淡出。弧比箭头好认 —— 一眼就知道"敌人在我左后方"。

    struct DmgMark { public Vector3 from; public float at; }
    static readonly List<DmgMark> _dmgMarks = new List<DmgMark>(8);

    /// <summary>被打了：记一下是谁从哪个方向打的。由 VehicleCombat 调用。</summary>
    public static void ShowDamageFrom(Vector3 worldPos)
    {
        if (_dmgMarks.Count >= 6) _dmgMarks.RemoveAt(0);
        _dmgMarks.Add(new DmgMark { from = worldPos, at = Time.unscaledTime });
    }

    void DrawDamageDirection()
    {
        if (_dmgMarks.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;

        // 清掉过期的
        for (int i = _dmgMarks.Count - 1; i >= 0; i--)
            if (Time.unscaledTime - _dmgMarks[i].at > 1.6f) _dmgMarks.RemoveAt(i);
        if (_dmgMarks.Count == 0) return;

        Vector2 center = new Vector2(_vw * 0.5f, VH * 0.5f);
        float R = Mathf.Min(_vw, VH) * 0.30f;

        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        for (int m = 0; m < _dmgMarks.Count; m++)
        {
            var mk = _dmgMarks[m];
            float age = Time.unscaledTime - mk.at;
            float a = 1f - Mathf.Clamp01(age / 1.6f);
            a = a * a;                                   // 快速衰减，前 0.4 秒最亮

            Vector3 to = mk.from - cam.transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.01f) continue;
            to.Normalize();

            // 有符号夹角：右正左负（和屏幕上"往右转"一致）
            float ang = Vector3.SignedAngle(fwd, to, Vector3.up) * Mathf.Deg2Rad;

            // 弧心角在屏幕上的方向：ang=0 → 正上方
            float half = 20f * Mathf.Deg2Rad;            // 弧的总张角 40°
            int seg = 18;
            Color c = new Color(1f, 0.30f, 0.26f, 0.85f * a);

            for (int i = 0; i < seg; i++)
            {
                float t0 = ang - half + (2f * half) * (i / (float)seg);
                float t1 = ang - half + (2f * half) * ((i + 1) / (float)seg);
                Vector2 p0 = center + new Vector2(Mathf.Sin(t0), -Mathf.Cos(t0)) * R;
                Vector2 p1 = center + new Vector2(Mathf.Sin(t1), -Mathf.Cos(t1)) * R;
                DrawSeg(p0, p1, c, 6f);
            }

            // 弧的两端再各点一个小三角，强化"这里"的感觉
            for (int s = -1; s <= 1; s += 2)
            {
                float t = ang + half * s;
                Vector2 outer = center + new Vector2(Mathf.Sin(t), -Mathf.Cos(t)) * (R + 10f);
                Vector2 inner = center + new Vector2(Mathf.Sin(t), -Mathf.Cos(t)) * (R - 10f);
                Vector2 side = center + new Vector2(Mathf.Sin(t + 0.10f * s), -Mathf.Cos(t + 0.10f * s)) * R;
                DrawSeg(outer, inner, c, 3f);
                DrawSeg(outer, side, c, 3f);
            }
        }
    }

    // ---------------- 绘制工具 ----------------

    // ================= 敌人/队友名字牌 + 屏幕外威胁提示（2026-11-11） =================
    //
    // 需求来自 2026-11-10 交接文档 §7.3 第一条「敌人/队友名字牌、威胁提示」。
    //
    // 两条原则：
    //   1. 每帧 OnGUI 会跑 3~4 遍，**绝对不能在里面 FindObjects / Linecast**。
    //      目标收集 + 视线判定统一放在 Update 里 0.15 s 轮询一次（和雷达同一个节拍），
    //      OnGUI 里只做投影和画。
    //   2. 名字牌画在**虚拟画布坐标**里（除以 _scale），不是屏幕像素，
    //      否则 1440p / 4K 下会整体偏右下（和 §5 第 5 条是同一类坐标错误）。

    struct NamePlate
    {
        public VehicleCombat v;
        public float dist;
        public bool los;        // 从相机到车体中心的连线没被地形/建筑挡住
    }

    static readonly List<NamePlate> _plates = new List<NamePlate>();

    [Tooltip("名字牌显示距离（米）：陆/海目标")]
    public float plateRangeGround = 520f;
    [Tooltip("名字牌显示距离（米）：空中目标（飞机看得远）")]
    public float plateRangeAir = 1100f;
    [Tooltip("屏幕外威胁箭头的距离上限（米）。比名字牌近得多 —— 远处的飞机一直闪箭头只是噪音")]
    public float threatArrowRange = 700f;

    static Color TeamColor(int team) => team == 0 ? CAlly : CEnemy;

    /// <summary>
    /// 收集射程内的目标 + 判定视线。由 Update() 按 0.15 s 节拍调用（不要放 OnGUI）。
    /// </summary>
    void PollNamePlates()
    {
        _plates.Clear();

        var mm = MatchManager.Instance;
        var me = mm != null ? mm.Player : null;
        var cam = Camera.main;
        if (me == null || cam == null || me.dead) return;

        Vector3 eye = cam.transform.position;
        var arr = _radarVehicles;
        for (int i = 0; i < arr.Length; i++)
        {
            var v = arr[i];
            if (v == null || v == me || v.dead) continue;

            bool air = v.Def != null && v.Def.domain == Domain.Air;
            float range = air ? plateRangeAir : plateRangeGround;
            float d = Vector3.Distance(me.transform.position, v.transform.position);
            if (d > range) continue;

            // 视线：只认"这条线上第一个碰撞体是不是它自己"。
            // 打到自己车体（相机在车顶）也算被挡住 —— 那确实看不见。
            Vector3 tgt = v.transform.position + Vector3.up * (air ? 2.5f : 1.6f);
            bool los = true;
            if (Physics.Linecast(eye, tgt, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                los = hit.collider.GetComponentInParent<VehicleCombat>() == v;

            _plates.Add(new NamePlate { v = v, dist = d, los = los });
        }
    }

    /// <summary>名字牌 + 屏幕外威胁箭头。画在准星之前，让准星压在它上面。</summary>
    void DrawNamePlates(MatchManager mm)
    {
        var me = mm.Player;
        if (me == null || me.dead) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;   // 光标放开（面板/暂停）不画
        if (IsScoped()) return;                                   // 炮镜里不画，免得糊住分划
        if (_plates.Count == 0) return;

        var cam = Camera.main;
        if (cam == null) return;

        float sw = Screen.width, sh = Screen.height;

        for (int i = 0; i < _plates.Count; i++)
        {
            var np = _plates[i];
            var v = np.v;
            if (v == null || v.dead) continue;

            bool enemy = v.team != me.team;
            bool air = v.Def != null && v.Def.domain == Domain.Air;
            Color col = TeamColor(v.team);

            Vector3 head = v.transform.position + Vector3.up * (air ? 3.6f : 4.3f);
            Vector3 sp = cam.WorldToScreenPoint(head);

            bool onScreen = sp.z > 0.01f && sp.x >= 0f && sp.x <= sw && sp.y >= 0f && sp.y <= sh;

            // 2026-11-11 用户反馈：常显的"距离箭头"又乱又看不出敌人在哪，删掉。
            // 改成**只在挨打时**弹一次受击方向指示（见 DrawDamageDirection）。
            if (!onScreen) continue;

            // 被地形挡住：不画名字（不让你"透视"）
            if (!np.los) continue;

            float px = sp.x / _scale;
            float py = (sh - sp.y) / _scale;

            // 远 → 淡，近 → 实
            float far = air ? plateRangeAir : plateRangeGround;
            float a = Mathf.Lerp(1f, 0.42f, Mathf.Clamp01((np.dist - 60f) / (far - 60f)));

            DrawOnePlate(v, new Vector2(px, py), np.dist, col, enemy, a);
        }
    }

    void DrawOnePlate(VehicleCombat v, Vector2 p, float dist, Color col, bool enemy, float alpha)
    {
        const float W = 138f, H = 56f;
        var box = new Rect(p.x - W * 0.5f, p.y - H, W, H);

        // 底（半透明，够看字就行，别糊住战场）
        Fill(box, new Color(0.02f, 0.03f, 0.05f, 0.40f * alpha));

        // 描边：四边各一条细线，比整框描边省事，远看也分得清敌我
        Color edge = WithA(col, 0.42f * alpha);
        Fill(new Rect(box.x, box.y, W, 1.5f), edge);
        Fill(new Rect(box.x, box.yMax - 1.5f, W, 1.5f), edge);
        Fill(new Rect(box.x, box.y, 1.5f, H), edge);
        Fill(new Rect(box.xMax - 1.5f, box.y, 1.5f, H), edge);

        // 顶部一条队伍色（比整框描边省 draw call，一眼分敌我）
        Fill(new Rect(box.x, box.y, W, 3f), WithA(col, 0.95f * alpha));

        // 名字
        string name = v.Def != null && !string.IsNullOrEmpty(v.Def.displayName) ? v.Def.displayName : "未知";
        Text(new Rect(box.x + 4f, box.y + 4f, W - 8f, 22f), name, _s18,
             WithA(Color.white, 0.95f * alpha), TextAnchor.MiddleCenter);

        // 血条（自己的车看自己，敌人的车也看得到剩余血量——同《战雷》街机）
        float hp01 = v.maxHp > 0.01f ? Mathf.Clamp01(v.hp / v.maxHp) : 0f;
        var bar = new Rect(box.x + 12f, box.y + 28f, W - 24f, 8f);
        Fill(bar, new Color(0f, 0f, 0f, 0.45f * alpha));
        Fill(new Rect(bar.x, bar.y, bar.width * hp01, bar.height),
             WithA(hp01 > 0.5f ? (enemy ? CEnemy : CAlly) : (hp01 > 0.22f ? CGold : CEnemy), alpha));

        // 距离
        Text(new Rect(box.x + 4f, box.y + 37f, W - 8f, 18f), $"{Mathf.RoundToInt(dist)} m", _s18,
             WithA(Color.white, 0.62f * alpha), TextAnchor.MiddleCenter);

        // 指向目标的小三角（牌底到车顶的引导线），让人一眼对上是哪台车
        DrawSeg(new Vector2(p.x, p.y + 2f), new Vector2(p.x, p.y + 10f), WithA(col, 0.8f * alpha), 2.4f);
    }

    /// <summary>屏幕边缘的威胁方位箭头：指向"屏幕外/背后"的敌人。</summary>
    void DrawThreatArrow(Vector2 screenPos, bool behind, float dist, Color col)
    {
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;

        // 屏幕像素 → 以中心为原点、y 向上（Unity 屏幕坐标 y 向下，所以取负）
        Vector2 d = new Vector2(screenPos.x - cx, -(screenPos.y - cy));
        if (behind) d = -d;                       // 背后的点在屏幕上是镜像的，反一下才是真实方位
        if (d.sqrMagnitude < 0.0001f) d = Vector2.up;
        d.Normalize();

        // 落在虚拟画布内缩一圈的椭圆上（留够箭头 + 文字的余量）
        // 下半部分再缩一点：底部有模块状态 + 车辆信息面板，箭头别压上去
        float vcx = _vw * 0.5f, vcy = VH * 0.5f;
        float rx = Mathf.Max(140f, _vw * 0.5f - 132f);
        float ry = VH * 0.5f - (d.y < 0f ? 200f : 118f);
        float ang = Mathf.Atan2(d.y, d.x);
        var tip = new Vector2(vcx + Mathf.Cos(ang) * rx, vcy - Mathf.Sin(ang) * ry);

        // 呼吸闪烁（unscaledTime：暂停/界面时也别冻住）
        float pulse = 0.66f + 0.34f * Mathf.Sin(Time.unscaledTime * 5.2f);
        Color c = WithA(col, pulse);

        // 虚拟画布 y 向下：back = 指向画面中心的单位向量
        Vector2 back = new Vector2(-Mathf.Cos(ang), Mathf.Sin(ang));
        Vector2 perp = new Vector2(-back.y, back.x);
        const float L = 34f, Wd = 18f;

        // 箭头根部一小段杆，让它在任何背景上都看得出方向
        DrawSeg(tip + back * (L + 14f), tip, WithA(col, 0.55f * pulse), 2.4f);
        DrawSeg(tip, tip + back * L + perp * Wd, c, 4.4f);
        DrawSeg(tip, tip + back * L - perp * Wd, c, 4.4f);

        // 距离：贴在箭头内侧，带一块暗底（纯文字压在亮天空上看不清）
        var lp = tip + back * 56f;
        var tb = new Rect(lp.x - 42f, lp.y - 13f, 84f, 26f);
        Fill(tb, new Color(0.02f, 0.03f, 0.05f, 0.42f * pulse));
        Text(new Rect(tb.x, tb.y + 1f, tb.width, tb.height), $"{Mathf.RoundToInt(dist)}m", _s20,
             WithA(col, 0.95f * pulse), TextAnchor.MiddleCenter);
    }

    // ---------------- 舰载雷达（只有船有） ----------------

    /// <summary>雷达探测半径（米）。</summary>
    public float radarRange = 700f;

    static VehicleCombat[] _radarVehicles = new VehicleCombat[0];
    static float _radarNextScan;
    static Texture2D _radarDisc;

    static bool _wantRestart;    // 结算面板「再来一局」按钮按下后，延到下一帧 Update 再重启

    void Update()
    {
        // 2026-11-11：结算面板的「再来一局」按钮在这里落地。
        // 不要在 OnGUI 里直接 SceneManager.LoadScene —— 会在 GUI 遍历中销毁对象。
        if (_wantRestart)
        {
            _wantRestart = false;
            var m0 = MatchManager.Instance;
            if (m0 != null) m0.Restart();
        }

        // 2026-11-10：受伤/金币的差值必须每帧算一次，所以在下面那个 0.15 s 的提前 return 之前
        PollCombatFx();

        // OnGUI 一帧会跑好几遍，别在里面 FindObjects
        if (Time.time < _radarNextScan) return;
        _radarNextScan = Time.time + 0.15f;
        _radarVehicles = Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None);

        // 2026-11-11：名字牌的目标收集 + 视线判定跟雷达同一个节拍（0.15 s），
        // 千万别搬进 OnGUI —— 那里一帧跑 3~4 遍，Linecast 会把帧率吃掉。
        PollNamePlates();
    }

    /// <summary>雷达圆盘贴图：圆内不透明、圆外透明（用 GUI.color 染色）。</summary>
    static Texture2D RadarDisc()
    {
        if (_radarDisc != null) return _radarDisc;
        const int S = 256;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        float c = S * 0.5f, r = S * 0.5f - 1f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01((0.99f - d) / 0.05f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply();
        _radarDisc = t;
        return t;
    }

    /// <summary>
    /// 小地图（2026-11-11 用户要求：所有载具都要有，原来只有船有）。
    /// 以自己为圆心、**北朝上**（不跟车头转，所以"上方永远是北"），扫掠线转圈。
    /// 红点 = 敌人，蓝点 = 队友，橙点 = 鱼雷（只有船显示）。
    /// </summary>
    void DrawRadar(MatchManager mm)
    {
        var p = mm.Player;
        if (p == null || p.Def == null) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        bool isSea = p.Def.domain == Domain.Sea;

        const float R = 112f;
        float cx = _vw - R - 42f;
        float cy = VH - R - 42f;

        // 底盘
        var oldC = GUI.color;
        GUI.color = new Color(0.02f, 0.08f, 0.09f, 0.78f);
        GUI.DrawTexture(new Rect(cx - R, cy - R, R * 2f, R * 2f), RadarDisc());
        GUI.color = oldC;

        // 外圈 + 距离环 + 十字
        var ring = new Color(0.35f, 0.85f, 0.75f, 0.55f);
        for (int i = 0; i < 72; i++)
        {
            float a = i / 72f * Mathf.PI * 2f;
            Fill(new Rect(cx + Mathf.Cos(a) * R - 1.5f, cy + Mathf.Sin(a) * R - 1.5f, 3f, 3f), ring);
        }
        var dim = new Color(0.35f, 0.85f, 0.75f, 0.22f);
        for (int i = 0; i < 48; i++)
        {
            float a = i / 48f * Mathf.PI * 2f;
            Fill(new Rect(cx + Mathf.Cos(a) * R * 0.66f - 1f, cy + Mathf.Sin(a) * R * 0.66f - 1f, 2f, 2f), dim);
            Fill(new Rect(cx + Mathf.Cos(a) * R * 0.33f - 1f, cy + Mathf.Sin(a) * R * 0.33f - 1f, 2f, 2f), dim);
        }
        Fill(new Rect(cx - R, cy - 0.5f, R * 2f, 1f), dim);
        Fill(new Rect(cx - 0.5f, cy - R, 1f, R * 2f), dim);

        // 扫掠线（转一圈 3 秒）
        float sweep = (Time.time * 120f) % 360f;
        for (int i = 0; i < 26; i++)
        {
            float f = i / 26f;
            float a = (sweep - f * 22f) * Mathf.Deg2Rad;
            var c = new Color(0.45f, 1f, 0.85f, 0.30f * (1f - f));
            Fill(new Rect(cx + Mathf.Sin(a) * R * f * 0.98f - 1.5f,
                          cy - Mathf.Cos(a) * R * f * 0.98f - 1.5f, 3f, 3f), c);
        }

        // 本船：中心的小箭头，**指向船头方向**（雷达是北朝上的，不跟船转）
        float shipYaw = p.transform.eulerAngles.y;
        var me = new Color(0.55f, 0.95f, 1f, 0.95f);
        for (int i = 1; i <= 9; i++)
        {
            float f = i / 9f;
            float a = shipYaw * Mathf.Deg2Rad;
            float px = cx + Mathf.Sin(a) * 10f * f;
            float py = cy - Mathf.Cos(a) * 10f * f;
            float w = 4.2f * (1f - f * 0.55f);
            Fill(new Rect(px - w * 0.5f, py - w * 0.5f, w, w), me);
        }
        Fill(new Rect(cx - 2f, cy - 2f, 4f, 4f), new Color(1f, 1f, 1f, 0.95f));

        // 北向标记
        Text(new Rect(cx - 12f, cy - R - 22f, 24f, 20f), "北", _s18,
             new Color(0.55f, 0.95f, 0.85f, 0.8f), TextAnchor.MiddleCenter);

        // 目标点：世界方向直接换算（北朝上，所以参考角恒为 0）
        const float yaw = 0f;
        var pos = p.transform.position;

        // 鱼雷先画（在船下面，别被船点盖住）；坦克/飞机不显示鱼雷
        for (int i = isSea ? Torpedo.All.Count - 1 : -1; i >= 0; i--)
        {
            var t = Torpedo.All[i];
            if (t == null) continue;
            Vector3 d = t.transform.position - pos;
            float dist = new Vector2(d.x, d.z).magnitude;
            if (dist > radarRange) continue;
            float rel = Mathf.DeltaAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float k = dist / radarRange;
            float px = cx + Mathf.Sin(rel * Mathf.Deg2Rad) * k * R;
            float py = cy - Mathf.Cos(rel * Mathf.Deg2Rad) * k * R;
            bool hostile = t.Team != p.Team;
            var tc = hostile ? new Color(1f, 0.62f, 0.18f, 0.95f) : new Color(0.75f, 0.95f, 0.75f, 0.6f);
            Fill(new Rect(px - 2.5f, py - 2.5f, 5f, 5f), tc);
            // 来向短线：鱼雷的航向
            Vector3 tv = t.transform.forward;
            float tAng = Mathf.DeltaAngle(yaw, Mathf.Atan2(tv.x, tv.z) * Mathf.Rad2Deg);
            Fill(new Rect(px + Mathf.Sin(tAng * Mathf.Deg2Rad) * 7f - 1f,
                          py - Mathf.Cos(tAng * Mathf.Deg2Rad) * 7f - 1f, 2f, 2f), tc);
        }

        // 场上所有载具
        foreach (var v in _radarVehicles)
        {
            if (v == null || v == p || !v.Alive) continue;
            Vector3 d = v.transform.position - pos;
            float dist = new Vector2(d.x, d.z).magnitude;
            if (dist > radarRange) continue;
            float rel = Mathf.DeltaAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float k = dist / radarRange;
            float px = cx + Mathf.Sin(rel * Mathf.Deg2Rad) * k * R;
            float py = cy - Mathf.Cos(rel * Mathf.Deg2Rad) * k * R;
            bool enemy = v.team != p.team;
            var c = enemy ? new Color(1f, 0.35f, 0.30f, 0.95f) : new Color(0.55f, 0.85f, 1f, 0.75f);
            float s = enemy ? 5f : 4f;
            Fill(new Rect(px - s * 0.5f, py - s * 0.5f, s, s), c);
            if (enemy)
                Fill(new Rect(px - 1f, py - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.9f));
        }

        Text(new Rect(cx - R, cy + R + 4f, R * 2f, 22f),
             $"小地图 {Mathf.RoundToInt(radarRange)} m", _s18,
             new Color(0.55f, 0.95f, 0.85f, 0.65f), TextAnchor.MiddleCenter);
    }

    // ---------------- 命中反馈（击穿 / 未击穿 / 跳弹） ----------------

    // 2026-11-10：从"屏幕文字 + Time.time 计时"改成"准星处的四角斜线 + unscaledTime 计时"。
    // 用 unscaledTime 是因为暂停/慢动作时 timeScale 会变，Time.time 一卡住，标记就会冻在屏幕上。
    static HitResult _hitKind = HitResult.None;   // 上一次命中的结果（None = 不画）
    static float _hitAt = -9f;                    // 上一次命中时刻（unscaledTime）

    const float HIT_FX_DUR = 0.28f;               // 标记存活时长

    /// <summary>玩家打中敌人时由 Projectile / 机枪调用（签名与语义保持不变）。</summary>
    public static void ShowHitFeedback(HitResult r)
    {
        switch (r)
        {
            case HitResult.Penetrated:
            case HitResult.NonPenetrating:
            case HitResult.Ricochet:
                _hitKind = r;
                _hitAt = Time.unscaledTime;
                break;
            default:
                return;      // None：没做判定，不画
        }
    }

    /// <summary>准星处的命中标记：四角斜线 + 中心小点，击穿金色、未击穿/跳弹红色。</summary>
    void DrawHitFeedback()
    {
        if (_hitKind == HitResult.None) return;
        float age = Time.unscaledTime - _hitAt;
        if (age < 0f || age > HIT_FX_DUR) return;

        float k = Mathf.Clamp01(age / HIT_FX_DUR);
        float s = Mathf.Lerp(1.35f, 1f, Smooth01(k));    // 0.28 s 内缩放 1.35 → 1.0
        float a = 1f - k;                                 // 同时透明度 1 → 0

        var baseC = _hitKind == HitResult.Penetrated ? CGold : CEnemy;
        var c = new Color(baseC.r, baseC.g, baseC.b, a);

        // 位置跟着准星走（炮塔没转过来时准星在画面边上，标记也该在边上）；
        // 本帧没画准星（开镜 / 光标放开）就退回屏幕中心。
        float cx = _vw * 0.5f, cy = VH * 0.5f;
        if (_crossFrame == Time.frameCount) { cx = _crossPos.x; cy = _crossPos.y; }
        float inner = 6f * s, len = 14f * s;              // 每条斜线 14 px（缩放前）
        for (int i = 0; i < 4; i++)
        {
            float ang = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;      // 四个 45° 方向
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 p0 = new Vector2(cx, cy) + dir * inner;
            DrawSeg(p0, p0 + dir * len, c, 3f * s);
        }
        Fill(new Rect(cx - 2f * s, cy - 2f * s, 4f * s, 4f * s), c);  // 中心小点
    }

    // ---------------- 战斗内 UI 特效：状态 + 轮询（2026-11-10） ----------------
    // 所有"前后帧差值"都在 Update 里算一次：OnGUI 一帧会跑 Layout/Repaint/事件好几遍，
    // 在里面轮询会把同一段掉血算成好几次，暗角/浮字也会快 3~4 倍。

    VehicleCombat _fxHpVehicle;     // 血量基准属于哪辆车（换车/复活就重新取基准）
    float _fxHpPrev = -1f;          // 上一帧血量
    float _fxHpRatio = 1f;          // 当前血量比例（< 0.3 时常驻暗角）
    float _fxHurtAt = -9f;          // 上次掉血时刻（unscaledTime）

    int _fxGoldPrev = int.MinValue; // 上一帧金币（int.MinValue = 还没取过基准）

    struct GoldPop { public int delta; public float at; public int slot; }   // slot = 固定显示槽位（0..5）
    static readonly List<GoldPop> _goldPops = new List<GoldPop>();   // 最多 6 条

    /// <summary>每帧一次：轮询血量差 / 金币差，顺带清到期的浮字。只在 Update 里调。</summary>
    void PollCombatFx()
    {
        var mm = MatchManager.Instance;

        // 到期的浮字在这里清（放 OnGUI 里会一帧删好几次，条数对不上）
        for (int i = _goldPops.Count - 1; i >= 0; i--)
            if (Time.unscaledTime - _goldPops[i].at > 0.9f) _goldPops.RemoveAt(i);

        if (mm == null)
        {
            _fxHpVehicle = null; _fxHpPrev = -1f; _fxHpRatio = 1f;
            _fxGoldPrev = int.MinValue;
            return;
        }

        // ---- 结算面板入场时刻：只在 matchOver 由假变真的那一帧取一次 ----
        if (mm.matchOver && !_settleWasOver) _settleAt = Time.unscaledTime;
        _settleWasOver = mm.matchOver;

        // ---- 血量：只跟"当前这辆车"比，换车/复活时重新取基准 ----
        var p = mm.Player;
        if (p == null || !p.Alive)
        {
            _fxHpVehicle = null;     // 车没了：下次上车重新取基准，别拿新车的满血去减旧车的残血
            _fxHpPrev = -1f;
            _fxHpRatio = 1f;         // 没车不画常驻暗角
        }
        else
        {
            if (p != _fxHpVehicle) { _fxHpVehicle = p; _fxHpPrev = p.hp; }
            else if (p.hp < _fxHpPrev - 0.01f) _fxHurtAt = Time.unscaledTime;   // 掉血：触发一次脉冲
            _fxHpPrev = p.hp;
            _fxHpRatio = Mathf.Clamp01(p.hp / Mathf.Max(1f, p.maxHp));
        }

        // ---- 金币 ----
        int g = mm.Gold;
        if (mm.matchOver)
        {
            // 结算期间不浮字；顺便把基准清掉，重开对局第一帧只会重新取基准，不会跳一个假数字
            _fxGoldPrev = int.MinValue;
            _goldPops.Clear();
        }
        else if (_fxGoldPrev == int.MinValue) _fxGoldPrev = g;
        else if (g != _fxGoldPrev)
        {
            // 2026-11-11 修 §7.4：原来按 List 下标错开，前面的到期被删掉后，后面的位置会整体跳 28 px。
            // 改成**固定槽位**：新条目占一个当前没被占用的最小槽；6 个全满就顶掉最老的那条并复用它的槽。
            int slot = 0;
            while (slot < 6 && _goldPops.Exists(x => x.slot == slot)) slot++;
            if (slot >= 6)
            {
                int oldest = 0;
                for (int i = 1; i < _goldPops.Count; i++)
                    if (_goldPops[i].at < _goldPops[oldest].at) oldest = i;
                slot = _goldPops[oldest].slot;
                _goldPops.RemoveAt(oldest);
            }
            _goldPops.Add(new GoldPop { delta = g - _fxGoldPrev, at = Time.unscaledTime, slot = slot });
            _fxGoldPrev = g;
        }
    }

    /// <summary>
    /// 受伤反馈：屏幕四边红色暗角。
    /// 掉血时 0.5 s 衰减的脉冲；血量 &lt; 30% 时暗角常驻、按 Sin(unscaledTime*2.2) 低频脉动。
    /// </summary>
    void DrawDamageVignette()
    {
        float hurt = Mathf.Clamp01(1f - (Time.unscaledTime - _fxHurtAt) / 0.5f);

        float low = 0f;
        if (_fxHpVehicle != null && _fxHpRatio < 0.3f)
        {
            // 越接近 0 越浓；再叠一个低频脉动，玩家不用盯血量条也知道快没了
            float deep = 1f - _fxHpRatio / 0.3f;
            low = (0.22f + 0.10f * Mathf.Sin(Time.unscaledTime * 2.2f)) * deep;
        }

        // 两者取大而不是相加：脉冲和常驻叠在一起会在边角闪成一片（本项目踩过的坑）
        float k = Mathf.Clamp01(Mathf.Max(hurt, low));
        if (k <= 0.01f) return;

        const float W = 150f;      // 暗角厚度（虚拟像素）
        const int Steps = 14;      // 细分条数：太少看得出横纹
        if (_vw <= W * 2f) return;

        var edge = new Color(0.85f, 0.06f, 0.05f, 1f);
        float peak = 0.62f * k;    // 最浓处的不透明度
        float strip = W / Steps;

        for (int i = 0; i < Steps; i++)
        {
            // 从屏幕边往里按平方衰减：线性衰减在 1080 高上看得出分层
            float f = 1f - i / (float)Steps;
            float a = peak * f * f;
            if (a <= 0.002f) continue;
            var c = new Color(edge.r, edge.g, edge.b, a);

            // 坐标取整 + 首尾相接，否则细分条之间会露出一条缝
            float d0 = Mathf.Round(i * strip);
            float d1 = Mathf.Round((i + 1) * strip);
            float th = d1 - d0;

            // 上、下两条画满整宽；左、右两条只画中间那段 —— 四条绝不重叠。
            // 半透明矩形一叠加就会出现明显的横/纵亮纹，这是本项目踩过的坑。
            Fill(new Rect(0f, d0, _vw, th), c);                        // 上
            Fill(new Rect(0f, VH - d1, _vw, th), c);                   // 下
            Fill(new Rect(d0, W, th, VH - W * 2f), c);                 // 左
            Fill(new Rect(_vw - d1, W, th, VH - W * 2f), c);           // 右
        }
    }

    /// <summary>金币变化浮字：在左上金币面板右侧浮出 +N / -N，0.9 s 上浮 40 px 并淡出。</summary>
    void DrawGoldPops()
    {
        for (int i = 0; i < _goldPops.Count; i++)
        {
            var gp = _goldPops[i];
            float age = Time.unscaledTime - gp.at;
            if (age < 0f || age > 0.9f) continue;

            float t = Mathf.Clamp01(age / 0.9f);
            var c = gp.delta >= 0 ? CGold : CEnemy;
            string s = (gp.delta >= 0 ? "+" : "") + gp.delta;

            // 同时出现多条时按**固定槽位**错开一行（2026-11-11 改：原来用 List 下标，
            // 前面的到期删掉后后面的会跳 28 px）。起点 50 而不是 18：上浮 40 px 之后还要留在屏幕内。
            float y = 50f + gp.slot * 28f - 40f * Smooth01(t);
            Text(new Rect(280f, y, 240f, 32f), $"<b>{s}</b>", _s24, new Color(c.r, c.g, c.b, 1f - t * t), TextAnchor.MiddleLeft);
        }
    }

    static void Panel(Rect r, Color? c = null) => Fill(r, c ?? CPanel);

    /// <summary>0..1 的三次 S 曲线缓动（动画统一用它，别各写一份）。</summary>
    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    /// <summary>只改 alpha、保留原色（结算面板整体淡入用）。</summary>
    static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

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
