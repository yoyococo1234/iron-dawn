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

    VehicleCombat _combat;
    Camera _cam;
    Vector3 _lastAimPoint;
    bool _hasAim;

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
            // 炮塔跟鼠标（受炮塔转速限制）
            float turn0 = _combat.def != null ? _combat.def.turretTurnSpeed : 90f;
            _combat.Physics.AimTurretAt(aimPoint, turn0);
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
            _combat.Physics.AimDirect(wantYawWorld, wantPitch, turn);

            // 切到车顶机枪时，机枪有自己的一套俯仰（能抬到 85° 打飞机），不受炮管 20° 的限制
            var cw = _combat.Weapon;
            if (cw != null && cw.deckMounted && _combat.Physics.HasDeckGun)
                _combat.Physics.SetDeckGunPitch(wantPitch, cw.deckMaxElevation, cw.deckMaxDepression, cw.deckPitchSpeed);
        }

        // 开火
        if (Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space))
            _combat.FireAt(aimPoint, turn);

        // Q 切换武器（右键留给开镜）
        if (Input.GetKeyDown(KeyCode.Q))
            _combat.CycleWeapon();

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
    }

    /// <summary>
    /// 准星打到哪。
    /// 陆战：屏幕正中央，命中点太近就沿视线取远处（避免朝脚下打）。
    /// 空战：**沿机头方向打**（不是屏幕中心）。镜头是压低 18° 俯看机背的，
    /// 用屏幕中心的话子弹会打向地面，和机头指向对不上，准星也就成了没意义的装饰。
    /// 现在准星画在机头指向的屏幕投影上（MatchHUD.DrawCrosshair），子弹也沿机头飞，两边一致。
    /// </summary>
    Vector3 GetAimPoint()
    {
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
