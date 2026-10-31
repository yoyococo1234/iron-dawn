using UnityEngine;

/// <summary>
/// 第三人称跟随相机：鼠标自由旋转视角（不再锁在车尾），滚轮缩放，开炮抖动。
/// 炮塔跟的是"屏幕中心的准星"，所以转视角就能把炮塔带过去 —— 远处敌人在哪也看得见。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("留空 = 自动跟玩家当前驾驶的载具")]
    public Transform target;

    [Header("视角")]
    public float yaw;
    public float pitch = 8f;
    public float minPitch = -20f;
    public float maxPitch = 72f;
    [Tooltip("鼠标灵敏度")]
    public float sensitivity = 3.2f;

    [Header("距离")]
    public float distance = 18f;
    public float minDistance = 7f;
    public float maxDistance = 55f;
    public float zoomSpeed = 45f;

    [Header("跟随")]
    public float followLerp = 10f;
    public float lookHeight = 2.2f;

    Vector3 _shake;
    float _shakeAmount;
    float _dist = 16f;
    float _curYaw;

    public void Shake(float amount) => _shakeAmount = Mathf.Max(_shakeAmount, amount);

    /// <summary>开炮 / 切车时把视角拉回车尾。</summary>
    public void ResetToBehindTarget()
    {
        if (target != null) yaw = target.eulerAngles.y;
        pitch = 8f;
    }

    void Start() => _dist = distance;

    void Update()
    {
        // 开局封面 / 车库界面要能点鼠标，这时放开光标
        bool uiWantsCursor = !GameStartScreen.Started || (GarageScreen.Instance != null && GarageScreen.Instance.IsOpen);

        if (uiWantsCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked &&
            (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (target == null) AutoFindTarget();
        if (target == null) return;

        // 开飞机时是追尾视角：鼠标用来操控飞机，不能同时转镜头
        var mm = MatchManager.Instance;
        var pl = mm != null ? mm.Player : null;
        if (pl != null && pl.Def != null && pl.Def.domain == Domain.Air)
        {
            ChaseCamera();
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * sensitivity;
            pitch -= Input.GetAxis("Mouse Y") * sensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        _dist = Mathf.Lerp(_dist, distance, 10f * Time.unscaledDeltaTime);

        var pivot = target.position + Vector3.up * lookHeight;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var desired = pivot - rot * Vector3.forward * _dist;

        // 别把相机塞进地里
        float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(desired.x, desired.z), BattlefieldTerrain.WaterLevel) + 2f;
        if (desired.y < ground) desired.y = ground;

        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.unscaledDeltaTime);
        transform.rotation = Quaternion.LookRotation(pivot - transform.position);

        if (_shakeAmount > 0.001f)
        {
            _shake = Random.insideUnitSphere * _shakeAmount;
            _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, Time.deltaTime * 14f);
            transform.rotation *= Quaternion.Euler(_shake.x * 2.5f, _shake.y * 2.5f, _shake.z * 3.5f);
        }
    }

    void AutoFindTarget()
    {
        var mm = MatchManager.Instance;
        if (mm == null) return;
        var p = mm.Player;
        if (p != null) target = p.transform;
    }

    /// <summary>飞机追尾视角：贴着机尾看，随飞机姿态转。</summary>
    void ChaseCamera()
    {
        float dt = Time.unscaledDeltaTime;
        _dist = Mathf.Lerp(_dist, distance, 8f * dt);

        var desired = target.position - target.forward * _dist + target.up * (_dist * 0.22f);
        float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(desired.x, desired.z), BattlefieldTerrain.WaterLevel) + 2f;
        if (desired.y < ground) desired.y = ground;

        transform.position = Vector3.Lerp(transform.position, desired, 9f * dt);

        var lookAt = target.position + target.forward * 80f;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(lookAt - transform.position), 10f * dt);

        if (_shakeAmount > 0.001f)
        {
            _shake = Random.insideUnitSphere * _shakeAmount;
            _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, Time.deltaTime * 14f);
            transform.rotation *= Quaternion.Euler(_shake.x * 2f, _shake.y * 2f, _shake.z * 2f);
        }
    }
}
