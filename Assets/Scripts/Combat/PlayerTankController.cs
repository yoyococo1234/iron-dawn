using UnityEngine;

/// <summary>
/// 玩家操作：鼠标瞄准炮塔、左键开火、1/2/3 切弹药。
/// 车体移动仍由 VehiclePhysics 读 Input.GetAxis("Vertical"/"Horizontal") 负责。
/// </summary>
[RequireComponent(typeof(VehicleCombat))]
public class PlayerTankController : MonoBehaviour
{
    [Header("瞄准")]
    [Tooltip("瞄准用的射线最大长度（米）")]
    public float aimRayLength = 3000f;
    [Tooltip("炮塔对准目标多少度以内才允许开火")]
    public float fireAngleTolerance = 4f;

    [Header("机枪模式（V）")]
    [Tooltip("机枪回转座转速（度/秒）。枪架是独立回转座，比炮塔快得多")]
    public float mgTurnSpeed = 200f;

    [Tooltip("出生后多少秒内不接受开火输入。开局点鼠标/按空格开始游戏时，这一下会被当成 Fire1，" +
             "载具一生成就打一炮（用户报的 bug）")]
    public float fireLockAfterSpawn = 0.4f;

    float _fireLockUntil;
    float _lockPitch;          // 锁定时的俯仰低通状态

    void OnEnable() => _fireLockUntil = Time.time + fireLockAfterSpawn;

    VehicleCombat _combat;
    Camera _cam;
    Vector3 _lastAimPoint;
    bool _hasAim;
    bool _mgMode;

    /// <summary>是否在「甲板/车顶机枪」模式（V 键切换）。机枪是独立一套：不占炮塔的 Q 循环，
    /// 有自己的俯仰（能抬到 85° 打飞机）和自己的瞄准镜。</summary>
    public bool MgMode => _mgMode;

    void Awake() => _combat = GetComponent<VehicleCombat>();

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null) _cam = Object.FindFirstObjectByType<Camera>();
    }

    void Update()
    {
        if (_combat == null || !_combat.Alive) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        // 有界面挡在战斗前面（开局封面 / 车库 / 商店 / 暂停，或刚关掉界面的 0.3 秒）时，
        // 整段战斗输入直接跳过：以前在商店里点「购买」，鼠标一动炮塔就跟着拧；
        // 点完「出击」那一下左键还按着，车一出来立刻开一炮（2026-11-10）。
        if (UiKit.Blocking) return;

        // 按住 C = 自由视角（战雷那套）：镜头随便转，炮塔保持不动。
        // 这时继续用「按下 C 之前的瞄准点」，所以开火还是打在炮口指的方向上。
        bool freeLook = Input.GetKey(KeyCode.C);

        Vector3 aimPoint;
        if (freeLook && _hasAim)
        {
            aimPoint = _lastAimPoint;
        }
        else
        {
            aimPoint = GetAimPoint();
            _lastAimPoint = aimPoint;
            _hasAim = true;
            // 这里**不再**调 AimTurretAt(aimPoint)：
            // 瞄准点现在就是沿炮口方向取的，再"把炮塔朝这个点转"等于自己追自己，
            // 会一点点把炮塔拧走。炮塔的朝向统一由下面的 AimDirect（只认鼠标）负责。
        }

        float turn = _combat.def != null ? _combat.def.turretTurnSpeed : 90f;

        // ---------- 直接瞄准（战雷那套）----------
        // 鼠标直接控「炮塔偏航 + 炮管俯仰」，两个轴各自限速、同时运动，可以斜着走。
        // 不再靠"屏幕中心射线打点再反推"——那样相机和炮塔会互相追，放大时就会往左右漂。
        //
        // 注意：按住 C 自由视角时**整段都要跳过**。以前这里没判 freeLook，于是上面虽然
        // 保住了 aimPoint，炮塔还是被 camFollow.yaw 拖着一起转 —— 用户反馈"环绕查看时炮塔跟着转"。
        var camFollow = _cam.GetComponent<CameraFollow>();
        if (!freeLook && camFollow != null && _combat.Physics != null && _combat.Physics.turretBone != null)
        {
            float wantYawWorld = camFollow.yaw;
            // 炮管俯仰 = **镜头"应该"的俯仰的反向**（用 camFollow 的鼠标俯仰算，不用相机的实际朝向）。
            //
            // 千万别读 _cam.transform.eulerAngles：开镜时相机是**跟着炮口**走的，
            // 读相机就等于"炮口 → 相机 → 炮口"闭环 —— 瞄准会自己漂、鼠标推不动（用户报的 bug）。
            // 用 camFollow.pitch（只由鼠标改）就断开了这个环。
            float wantPitch = -(camFollow.tankLookDownDeg + (camFollow.pitch - 8f));
            var dw = _combat.DeckWeapon;

            // ---------- Ctrl 锁定：炮塔/枪架**直接对准锁定目标**（2026-11-07）----------
            // 之前只把 GetAimPoint 换成了目标位置，但那只是"开火点"，炮塔还是跟着鼠标走，
            // 所以看着像"不会瞄准"。这里直接算目标的世界偏航 + 弹道俯仰，喂给 AimDirect。
            if (LockOnSystem.Active && LockOnSystem.Target != null && LockOnSystem.Target.Alive)
            {
                Vector3 lockAim = LockOnSystem.AimPointFor(_combat);
                // 用**船体/车体根部**当基准，不用炮口节点：炮口节点挂在 Model 上，
                // 船随浪起伏时它会上下动，解出来的俯仰角跟着抖 —— 就是"锁定后炮管上下晃"的来源。
                Vector3 muzzle = _combat.transform.position + Vector3.up * 2.5f;
                // 只有"正常弹速的炮弹"才做弹道解算。鱼雷/导弹这种慢速或制导武器直接指向，
                // 否则解出来的抛射角是几十度的高抛，镜头会直接飘到天上（用户报的 bug）。
                var lw = _combat.Weapon;
                bool ballistic = lw != null && !lw.isTorpedo && lw.projectileSpeed >= 300f;
                float bspd = lw != null ? Mathf.Max(150f, lw.projectileSpeed) : 900f;
                Vector3 bdir = ballistic
                    ? VehicleCombat.BallisticDir(muzzle, lockAim, bspd)
                    : (lockAim - muzzle).normalized;

                wantYawWorld = Mathf.Atan2(bdir.x, bdir.z) * Mathf.Rad2Deg;
                float rawPitch = Mathf.Asin(Mathf.Clamp(bdir.y, -1f, 1f)) * Mathf.Rad2Deg;
                // 低通：残余的小抖动直接抹掉（时间常数 ~0.12 s）
                _lockPitch = Mathf.Lerp(_lockPitch, rawPitch, 1f - Mathf.Exp(-8f * Time.deltaTime));
                wantPitch = _lockPitch;

                // 镜头也跟过去（用户要求"镜头不晃、正好瞄到他身上"）：
                // 用鼠标那套 pitch 换算反过来推 camFollow.pitch，这样镜头和炮管是同一套解算。
                camFollow.yaw = Mathf.MoveTowardsAngle(camFollow.yaw, wantYawWorld, 150f * Time.deltaTime);
                float wantCamPitch = -wantPitch - camFollow.tankLookDownDeg + 8f;
                camFollow.pitch = Mathf.MoveTowards(camFollow.pitch, wantCamPitch, 110f * Time.deltaTime);
            }

            if (_mgMode && dw != null && _combat.Physics.HasDeckGun)
            {
                // ---------- 机枪模式：炮塔不动，只转枪架 ----------
                // 枪架是独立回转座（MG_Yaw），200°/s —— 炮塔只有 50°/s，用炮塔带着机枪转，
                // 画面会明显跟不上鼠标（用户："移动鼠标视角转换很不自然"）。
                float turretWorldYaw = _combat.transform.eulerAngles.y + _combat.Physics.turretYaw;
                float relYaw = Mathf.DeltaAngle(turretWorldYaw, wantYawWorld);
                _combat.Physics.SetDeckGunYaw(relYaw, mgTurnSpeed);
                _combat.Physics.SetDeckGunPitch(wantPitch, dw.deckMaxElevation, dw.deckMaxDepression, dw.deckPitchSpeed);
            }
            else
            {
                _combat.Physics.AimDirect(wantYawWorld, wantPitch, turn);
                // 不在机枪模式：枪架归位（对准炮塔正前方、水平），免得它歪着挂在塔顶
                _combat.Physics.SetDeckGunYaw(0f, 120f);
                _combat.Physics.SetDeckGunPitch(0f, 85f, -12f, 90f);
            }
        }

        // 开火：机枪模式打机枪，否则打当前炮塔武器
        if ((Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space)) && Time.time >= _fireLockUntil)
        {
            if (_mgMode)
            {
                // 2026-11-07：机枪**永远按准星方向打**。
                // 锁定目标时打锁定点；没有锁定就直接用屏幕中心（准星）射线打 ——
                // 不能再用炮口方向的 aimPoint，那样准星和弹道是两条线，永远打不中。
                Vector3 mgAim = (LockOnSystem.Active && LockOnSystem.Target != null && LockOnSystem.Target.Alive)
                    ? LockOnSystem.AimPointFor(_combat)
                    : CameraAimPoint();
                _combat.FireDeckGunAt(mgAim);
            }
            else _combat.FireAt(aimPoint, turn);
        }

        // Q 切换炮塔武器（舰炮/鱼雷…，**永远切不到机枪**）
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (_mgMode) _mgMode = false;       // 机枪模式下按 Q = 退回炮塔武器
            else _combat.CycleWeapon();
        }

        // V 进/出机枪模式（只有装了独立挂点机枪的载具才有）
        if (Input.GetKeyDown(KeyCode.V))
        {
            var dw2 = _combat.DeckWeapon;
            if (dw2 != null && _combat.Physics != null && _combat.Physics.HasDeckGun)
            {
                _mgMode = !_mgMode;
                // 万一当前武器正好是机枪（旧存档/旧逻辑留下的），退回一把炮塔武器
                if (_mgMode && _combat.Weapon != null && _combat.Weapon.deckMounted) _combat.CycleWeapon();
            }
        }

        // G = 随时换载具（测试用：不用等阵亡，直接回车库重选）
        if (Input.GetKeyDown(KeyCode.G))
        {
            var mm = MatchManager.Instance;
            if (mm != null) mm.OpenGarageForSwap();
        }

        // 切弹药
        var w = _combat.Weapon;
        if (w != null && w.ammoTypes != null)
        {
            for (int i = 0; i < Mathf.Min(3, w.ammoTypes.Length); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _combat.ammoIndex = i;
        }

        // X = 放干扰弹（只有炮艇有）：骗走追过来的鱼雷
        TickDecoy();
    }

    // ---------------- 干扰弹 ----------------

    [Header("干扰弹（炮艇专用）")]
    [Tooltip("一共带几发")]
    public int decoyCharges = 3;
    [Tooltip("两发之间的冷却（秒）")]
    public float decoyCooldown = 12f;
    [Tooltip("诱饵的吸引半径（米）")]
    public float decoyRadius = 130f;
    [Tooltip("诱饵存活时间（秒）")]
    public float decoyLife = 18f;

    /// <summary>当前还剩几发（HUD 显示用）。</summary>
    public int DecoyCharges => decoyCharges;
    float _decoyReadyAt;
    bool _decoyInited;

    void TickDecoy()
    {
        if (!_decoyInited)
        {
            // 换车后重新装满
            _decoyInited = true;
            decoyCharges = Mathf.Max(decoyCharges, 3);
        }

        bool sea = _combat != null && _combat.Def != null && _combat.Def.domain == Domain.Sea;
        if (!sea) return;
        if (!Input.GetKeyDown(KeyCode.X)) return;
        if (decoyCharges <= 0) return;
        if (Time.time < _decoyReadyAt) return;

        decoyCharges--;
        _decoyReadyAt = Time.time + decoyCooldown;

        // 放在船尾后面一点：往前丢的话自己会先撞上自己的诱饵
        Vector3 pos = _combat.transform.position - _combat.transform.forward * 6f;
        Decoy.Deploy(pos, BattlefieldTerrain.WaterLevel, 6f, 12f, _combat != null ? _combat.team : -1);
        Sfx.PlayAt(Sfx.MgFire, pos, 0.5f, 1.6f, 20f, 300f);   // 先拿机炮声顶一下，后面再换专门音效
    }

    /// <summary>
    /// 准星打到哪。
    /// 陆战：屏幕正中央，命中点太近就沿视线取远处（避免朝脚下打）。
    /// 空战：**沿机头方向打**（不是屏幕中心）。镜头是压低 18° 俯看机背的，
    /// 用屏幕中心的话子弹会打向地面，和机头指向对不上，准星也就成了没意义的装饰。
    /// 现在准星画在机头指向的屏幕投影上（MatchHUD.DrawCrosshair），子弹也沿机头飞，两边一致。
    /// </summary>
    /// <summary>
    /// 屏幕中心（准星）射线打到哪 —— 机枪不开锁定时就打这个点。
    /// 这样"准星指哪、弹打哪"，不会再出现准星和弹道两条线的情况。
    /// </summary>
    Vector3 CameraAimPoint()
    {
        if (_cam == null) return transform.position + transform.forward * 500f;

        var ray = _cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        var hits = Physics.RaycastAll(ray, 1600f, ~0, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var vc = h.collider.GetComponentInParent<VehicleCombat>();
                if (vc == _combat) continue;         // 自己身上的碰撞盒跳过
                return h.point;
            }
        }
        return ray.origin + ray.direction * 1600f;
    }

    Vector3 GetAimPoint()
    {
        // Ctrl 锁定：直接把锁定目标当瞄准点（炮塔/机头会自己转过去）
        if (LockOnSystem.Active && LockOnSystem.Target != null && LockOnSystem.Target.Alive)
            return LockOnSystem.AimPointFor(_combat);
        bool air = _combat != null && _combat.Def != null && _combat.Def.domain == Domain.Air;

        if (air)
        {
            // 炮口起点取机身中心稍微抬高，方向就是机头
            Vector3 origin = _combat.transform.position + Vector3.up * 0.5f;
            Vector3 dir = _combat.transform.forward;

            Vector3 basePoint;
            if (Physics.Raycast(origin, dir, out var hitAir, aimRayLength, ~0, QueryTriggerInteraction.Ignore)
                && Vector3.Distance(origin, hitAir.point) >= 150f)
                basePoint = hitAir.point;
            else
                basePoint = origin + dir * 2000f;

            // 辅助瞄准：机头方向附近有敌人就吸过去（导弹吸得宽，机炮吸得窄）
            var w = _combat.Weapon;
            bool missile = w != null && w.projectileSpeed <= 600f && w.baseDamage >= 300f;
            float cone = missile ? 20f : 6f;
            return AirAimAssist(basePoint, origin, dir, cone);
        }

        // ---------- 陆战 / 海战：瞄准点沿**炮口方向** ----------
        // HUD 画的准星就是炮口方向的屏幕投影（MatchHUD.DrawCrosshair），
        // 所以弹也沿炮口飞 —— 准星在哪、弹就打在哪，炮塔没转过来时准星停在画面边上（箭头）。
        // 不能再用"相机中心射线"：开镜时相机挂在炮塔后方会产生视差，炮塔会追着视差转。
        if (_combat.Physics != null)
        {
            Vector3 mp = _combat.Physics.GetMuzzlePosition();
            Vector3 md = _combat.Physics.GetMuzzleForward();
            Vector3 start = mp + md * 4f;
            if (Physics.Raycast(start, md, out var mh, aimRayLength, ~0, QueryTriggerInteraction.Ignore))
                return mh.point;
            return start + md * 700f;
        }

        Vector2 screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var ray = _cam.ScreenPointToRay(screenPos);

        // 陆战 / 海战：瞄准点就是**屏幕中心那条射线**打到的位置。
        // 准星画在屏幕中心（MatchHUD），炮塔按 turretTurnSpeed 转过来对准它 —— 战雷那套：
        // 没追上从炮塔模型看得出来，FireAt 里还有 4° 的对准门槛兜底。
        if (Physics.Raycast(ray, out var hit, aimRayLength, ~0, QueryTriggerInteraction.Ignore))
        {
            float d = Vector3.Distance(ray.origin, hit.point);
            if (d >= 120f) return hit.point;          // 远处的命中点直接用
        }

        // 太近 / 没打中：沿视线取远处。
        // 注意**不要**把高度拉平 —— 拉平的话炮管永远是 0°，抬炮/压炮就形同虚设。
        // 往天上瞄就该抬炮，往脚下瞄就该压炮（角度由 maxElevation/maxDepression 限制）。
        return ray.origin + ray.direction * 700f;
    }

    /// <summary>
    /// 空中辅助瞄准：在准星附近（coneDeg 度以内）找最近的敌人，把瞄准点吸到它身上，
    /// 并按弹速算提前量。导弹给 20° 的大锥角（相当于半自动锁定），机炮只给 6°。
    /// 找不到就返回原来的瞄准点。
    /// </summary>
    Vector3 AirAimAssist(Vector3 fallback, Vector3 rayOrigin, Vector3 rayDir, float coneDeg)
    {
        VehicleCombat best = null;
        float bestAngle = coneDeg;
        Vector3 bestPoint = fallback;
        float bestDist = float.MaxValue;

        var all = Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None);
        foreach (var vc in all)
        {
            if (vc == null || vc == _combat || !vc.Alive) continue;
            if (vc.team == _combat.team) continue;

            Vector3 c = vc.transform.position + Vector3.up * 1.0f;
            Vector3 to = c - rayOrigin;
            float dist = to.magnitude;
            if (dist < 25f || dist > 2400f) continue;

            float ang = Vector3.Angle(rayDir, to);
            if (ang > bestAngle) continue;
            // 角度优先，同角度取近的
            if (ang < bestAngle - 0.5f || dist < bestDist)
            {
                bestAngle = ang;
                bestDist = dist;
                best = vc;
                bestPoint = c;
            }
        }

        if (best == null) return fallback;

        // 提前量：按当前武器的弹速估飞行时间，打到目标"将要在的位置"
        float speed = _combat.Weapon != null ? Mathf.Max(120f, _combat.Weapon.projectileSpeed) : 800f;
        var vp = best.GetComponent<VehiclePhysics>();
        Vector3 vel = vp != null ? vp.Velocity : Vector3.zero;
        float tof = Vector3.Distance(rayOrigin, bestPoint) / speed;
        return bestPoint + vel * tof;
    }
}
