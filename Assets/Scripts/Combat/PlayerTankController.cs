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

        // 副武器（炸弹 / 鱼雷）：右键。只在光标锁定（战斗中）时生效
        if (Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButtonDown(1))
            _combat.FireSecondary(aimPoint);

        // 切弹药
        var w = _combat.Weapon;
        if (w != null && w.ammoTypes != null)
        {
            for (int i = 0; i < Mathf.Min(3, w.ammoTypes.Length); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _combat.ammoIndex = i;
        }
    }

    /// <summary>
    /// 屏幕正中央（准星）打到哪。
    /// 注意：相机有俯角，中心射线往往 20~30 m 就打到脚下了 —— 那样炮口会朝下打地面。
    /// 所以命中点太近时，改成"沿视线方向、在车体高度上取远处一点"，让炮口水平指出去。
    /// </summary>
    Vector3 GetAimPoint()
    {
        var ray = _cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

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
