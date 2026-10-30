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

        // 切弹药
        var w = _combat.Weapon;
        if (w != null && w.ammoTypes != null)
        {
            for (int i = 0; i < Mathf.Min(3, w.ammoTypes.Length); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _combat.ammoIndex = i;
        }
    }

    /// <summary>鼠标指到的世界坐标：优先打射线命中点，否则取远处的地平面。</summary>
    Vector3 GetAimPoint()
    {
        var ray = _cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out var hit, aimRayLength, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;

        // 没打中东西 → 和当前高度的水平面求交
        float y = transform.position.y;
        if (Mathf.Abs(ray.direction.y) > 0.0001f)
        {
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t > 0f) return ray.origin + ray.direction * t;
        }
        return ray.origin + ray.direction * 500f;
    }
}
