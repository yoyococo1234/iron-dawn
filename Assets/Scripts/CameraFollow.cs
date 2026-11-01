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

    [Header("右键：轻微放大（不换视角）")]
    [Tooltip("按住右键时把视野收窄到这个角度（比 normalFov 小一点即可）")]
    public float rightZoomFov = 38f;

    [Header("开镜（Shift 按住 = 炮镜）")]
    public float normalFov = 50f;
    public float zoomFov = 10f;
    public float scopeSpeed = 12f;
    [Tooltip("开镜时的鼠标灵敏度倍率")]
    public float zoomSensitivity = 0.3f;

    [Header("自由视角（按住 C，战雷那套）")]
    [Tooltip("按住这个键：鼠标只转镜头，炮塔不动")]
    public KeyCode freeLookKey = KeyCode.C;
    [Tooltip("松开后视角回到炮口方向的速度")]
    public float recenterSpeed = 6f;

    /// <summary>当前是否在自由视角（炮塔不跟鼠标）。</summary>
    public bool FreeLook { get; private set; }

    /// <summary>当前是否在炮镜（Shift）。HUD 用它画圆形镜筒和十字分划。</summary>
    public bool IsScoped => _scoped;

    /// <summary>当前是否按着右键（轻微放大）。</summary>
    public bool IsRightZoom => _rightZoom;

    Vector3 _shake;
    float _shakeAmount;
    float _dist = 16f;
    float _curYaw;
    bool _scoped;
    bool _rightZoom;
    bool _wasFreeLook;

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
            float sens = sensitivity * (_scoped ? zoomSensitivity : 1f);
            yaw += Input.GetAxis("Mouse X") * sens;
            pitch -= Input.GetAxis("Mouse Y") * sens;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        // ---------- 自由视角（按住 C）----------
        // 战雷里 C 是「环视」：鼠标只转镜头，炮塔保持不动；松开后视角自己回到炮口方向。
        bool freeLook = Cursor.lockState == CursorLockMode.Locked && Input.GetKey(freeLookKey);
        FreeLook = freeLook;
        if (!freeLook && _wasFreeLook)
        {
            // 松手：把镜头平滑转回炮口指向
            var vp = target.GetComponent<VehiclePhysics>();
            float wantYaw = target.eulerAngles.y + (vp != null ? vp.turretYaw : 0f);
            float k = Mathf.Clamp01(recenterSpeed * Time.unscaledDeltaTime);
            yaw = Mathf.LerpAngle(yaw, wantYaw, k);
            pitch = Mathf.Lerp(pitch, 8f, k);
        }
        _wasFreeLook = freeLook;

        // Shift = 炮镜（圆形镜筒 + 十字分划，HUD 负责画）；右键 = 只是把视野收窄一点。
        // 两者不再叠加（旧版两个一起按会放到更大，手感很怪）。
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        _scoped = locked && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        _rightZoom = locked && Input.GetMouseButton(1) && !_scoped;
        var cam = GetComponent<Camera>();

        // ---------- 炮手视角（右键按住）：相机贴到炮塔上方，炮管就在画面下缘 ----------
        // 战雷里右键就是这个：看到自己的炮管，准星就是炮口指向。按住 Shift 再拉近一档。
        if (_scoped && cam != null)
        {
            var vp = target.GetComponent<VehiclePhysics>();

            // 视线方向 = 玩家自己的 yaw/pitch（**不跟炮管**）。
            // 跟炮管的话，相机→炮塔→相机 会形成反馈，放大后炮塔会自己往左右漂。
            Vector3 lookDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

            // 机位高度按**炮塔实际包围盒顶**算（重装坦克炮塔又高又大，写死高度会被挡死）
            float turretTopY = target.position.y + 2f;
            if (vp != null && vp.turretBone != null)
            {
                bool any = false;
                foreach (var r in vp.turretBone.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any || r.bounds.max.y > turretTopY) { turretTopY = r.bounds.max.y; any = true; }
                }
            }
            float camY = turretTopY + 0.45f;

            Vector3 back = new Vector3(lookDir.x, 0f, lookDir.z);
            back = back.sqrMagnitude > 0.0001f ? back.normalized : target.forward;

            Vector3 camPos = new Vector3(target.position.x, camY, target.position.z) - back * 5.0f;
            Vector3 lookAtS = camPos + lookDir * 300f;

            float groundS = Mathf.Max(BattlefieldTerrain.SampleHeight(camPos.x, camPos.z), BattlefieldTerrain.WaterLevel) + 1.2f;
            if (camPos.y < groundS) camPos.y = groundS;

            transform.position = Vector3.Lerp(transform.position, camPos, 16f * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(lookAtS - transform.position), 16f * Time.unscaledDeltaTime);

            // 炮镜：固定拉到 zoomFov（不再有"两键叠加再放大"）
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, zoomFov, scopeSpeed * Time.unscaledDeltaTime);

            if (_shakeAmount > 0.001f)
            {
                _shake = Random.insideUnitSphere * _shakeAmount;
                _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, Time.deltaTime * 14f);
                transform.rotation *= Quaternion.Euler(_shake.x * 2f, _shake.y * 2f, _shake.z * 2.5f);
            }
            return;
        }

        if (cam != null)
        {
            // 右键：只把视野从 normalFov 收到 rightZoomFov（视角还是第三人称，不换机位）
            float wantFov = _rightZoom ? rightZoomFov : normalFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wantFov, scopeSpeed * Time.unscaledDeltaTime);
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

        var cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, normalFov, scopeSpeed * dt);

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
