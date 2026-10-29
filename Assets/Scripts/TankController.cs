using UnityEngine;

/// <summary>
/// 坦克控制器：A/D 转车体，W/S 前进后退，鼠标转炮塔，左键开火。
/// M0 阶段只做"开得动、打得出去"，不做碰撞与伤害。
/// </summary>
public class TankController : MonoBehaviour
{
    public Transform turret;        // 炮塔（只绕 Y 转）
    public Transform barrelTip;     // 炮口（子弹从这里出）

    float _turretYaw;
    float _cooldown;

    void Update()
    {
        // --- 车体 ---
        float turn = Input.GetAxis("Horizontal");
        float move = Input.GetAxis("Vertical");

        transform.Rotate(0f, turn * Config.TankTurnSpeed * Time.deltaTime, 0f, Space.World);
        transform.position += transform.forward * (move * Config.TankSpeed * Time.deltaTime);

        // --- 炮塔（鼠标水平）---
        _turretYaw += Input.GetAxis("Mouse X") * Config.TurretSensitivity;
        if (turret != null)
            turret.localRotation = Quaternion.Euler(0f, _turretYaw, 0f);

        // --- 开火 ---
        _cooldown -= Time.deltaTime;
        if (Input.GetMouseButton(0) && _cooldown <= 0f)
        {
            Fire();
            _cooldown = Config.FireCooldown;
        }
    }

    void Fire()
    {
        if (barrelTip == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Projectile";
        go.transform.position = barrelTip.position;
        go.transform.localScale = Vector3.one * 0.35f;

        var r = go.GetComponent<Renderer>();
        r.material.color = new Color(1f, 0.85f, 0.3f);
        Destroy(go.GetComponent<Collider>()); // M0 不做碰撞

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearVelocity = barrelTip.forward * Config.ProjectileSpeed;

        go.AddComponent<Projectile>();
    }
}
