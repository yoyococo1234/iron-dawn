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

        Vector3 aimPoint = GetAimPoint();

        // 炮塔跟鼠标（受炮塔转速限制）
        float turn = _combat.def != null ? _combat.def.turretTurnSpeed : 90f;
        _combat.Physics.AimTurretAt(aimPoint, turn);

        // 开火
        if (Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space))
            _combat.FireAt(aimPoint, turn);

        // Q 切换武器（右键留给开镜）
        if (Input.GetKeyDown(KeyCode.Q))
            _combat.CycleWeapon();

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
    /// 空战（战雷式）：准星是可以用鼠标推着走的圈，不压平高度 —— 俯冲时子弹必须跟着往下走。
    /// </summary>
    Vector3 GetAimPoint()
    {
        bool air = _combat != null && _combat.Def != null && _combat.Def.domain == Domain.Air;

        Vector2 screenPos;
        if (air && _combat.Physics != null)
        {
            Vector2 aim = _combat.Physics.AirAim;
            float r = Screen.height * 0.32f;
            screenPos = new Vector2(Screen.width * 0.5f + aim.x * r, Screen.height * 0.5f - aim.y * r);
        }
        else
        {
            screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        var ray = _cam.ScreenPointToRay(screenPos);

        if (air)
        {
            // 不压平高度：直接沿视线打远，俯冲时炮口才会跟着往下
            if (Physics.Raycast(ray, out var hitAir, aimRayLength, ~0, QueryTriggerInteraction.Ignore)
                && Vector3.Distance(ray.origin, hitAir.point) >= 150f)
                return hitAir.point;
            return ray.origin + ray.direction * 2000f;
        }

        if (Physics.Raycast(ray, out var hit, aimRayLength, ~0, QueryTriggerInteraction.Ignore))
        {
            float d = Vector3.Distance(ray.origin, hit.point);
            if (d >= 120f) return hit.point;          // 远处的命中点直接用
        }

        // 太近 / 没打中：沿视线取远处、并拉回自身高度，避免朝地面或朝天空打
        Vector3 far = ray.origin + ray.direction * 700f;
        far.y = transform.position.y + 1.5f;
        return far;
    }
}
