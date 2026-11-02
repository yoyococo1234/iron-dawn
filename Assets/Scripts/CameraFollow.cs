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
    [Tooltip("最大抬头角。抬到 85° 才能用机枪打高处的飞机")]
    public float maxPitch = 85f;
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
    public float rightZoomFov = 30f;

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

    [Header("调试")]
    [Tooltip("强制处于炮镜状态（截图/验收用，正常游戏别勾）")]
    public bool forceScopeForTest = false;
    [Tooltip("强制处于自由环视（截图/验收用，正常游戏别勾）")]
    public bool forceFreeLookForTest = false;

    /// <summary>当前是否在自由视角（炮塔不跟鼠标）。</summary>
    public bool FreeLook { get; private set; }

    /// <summary>
    /// 全局：这一帧是否按着 C 环视。飞机用它屏蔽鼠标操控
    /// （不然环视时鼠标一边转镜头一边把飞机拐走）。
    /// 由 LateUpdate 每帧写，VehiclePhysics.UpdateAir 读 —— 差一帧，看不出来。
    /// </summary>
    public static bool FreeLookActive;

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
    Transform _lastTarget;

    /// <summary>按载具实际尺寸调跟车距离（坦克 ~18 m，18 m 长的炮艇 ~30 m）。</summary>
    void FitDistanceToTarget()
    {
        if (target == null) return;
        var col = target.GetComponent<Collider>();
        if (col == null) return;

        float len = Mathf.Max(col.bounds.size.x, col.bounds.size.z);
        float want = Mathf.Clamp(len * 1.7f, 14f, 46f);
        distance = want;
        minDistance = Mathf.Max(7f, want * 0.45f);
        maxDistance = Mathf.Max(minDistance + 10f, want * 2.4f);
        _dist = want;
    }

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

        // 换车（车 → 船 → 飞机）时按载具实际尺寸重设跟车距离：
        // 18 m 长的炮艇用坦克的 18 m 距离会顶在船尾上，整屏都是船。
        if (target != _lastTarget)
        {
            _lastTarget = target;
            FitDistanceToTarget();
        }

        // 开飞机时是追尾视角：鼠标用来操控飞机，不能同时转镜头。
        // 但按住 C 要能环视 —— 这时候鼠标只转镜头，飞机那边会忽略鼠标（见 FreeLookActive）。
        var mm = MatchManager.Instance;
        var pl = mm != null ? mm.Player : null;
        if (pl != null && pl.Def != null && pl.Def.domain == Domain.Air)
        {
            bool lockedAir = Cursor.lockState == CursorLockMode.Locked;
            bool freeLookAir = (lockedAir && Input.GetKey(freeLookKey)) || forceFreeLookForTest;
            FreeLook = freeLookAir;
            FreeLookActive = freeLookAir;

            if (!freeLookAir)
            {
                _wasFreeLook = false;
                ChaseCamera();
                return;
            }

            // 刚按下 C：先把 yaw/pitch 对齐到当前镜头方向，免得第一帧跳一下
            if (!_wasFreeLook)
            {
                var e = transform.eulerAngles;
                yaw = e.y;
                pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), minPitch, maxPitch);
                _wasFreeLook = true;
            }

            // 按住 C：鼠标绕飞机转镜头，飞机照常飞
            float sensAir = sensitivity;
            yaw += Input.GetAxis("Mouse X") * sensAir;
            pitch -= Input.GetAxis("Mouse Y") * sensAir;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            OrbitAroundTarget();
            return;
        }
        FreeLookActive = false;

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
        _scoped = locked && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) || forceScopeForTest;
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

            // 机位基点用**炮塔实际位置**，不是车体中心。
            // 用中心的话，炮塔靠船头的船会退到驾驶舱里 —— 那正是"放大到脑袋上、看不见前面"的原因。
            Vector3 turretPivot = target.position + Vector3.up * 2f;
            if (vp != null && vp.turretBone != null) turretPivot = vp.turretBone.position;

            // 机位高度按**炮塔实际包围盒顶**算（重装坦克炮塔又高又大，写死高度会被挡死）
            float turretTopY = turretPivot.y + 1f;
            if (vp != null && vp.turretBone != null)
            {
                bool any = false;
                foreach (var r in vp.turretBone.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any || r.bounds.max.y > turretTopY) { turretTopY = r.bounds.max.y; any = true; }
                }
            }

            Vector3 back = new Vector3(lookDir.x, 0f, lookDir.z);
            back = back.sqrMagnitude > 0.0001f ? back.normalized : target.forward;

            // 后退距离按炮塔实际尺寸走：小船/坦克 1.5~2 m 刚好，18 m 的炮艇退 1.5 m 会贴到枪口上。
            float turretSpan = 2f;
            if (vp != null && vp.turretBone != null)
            {
                bool anyR = false;
                Bounds tb = new Bounds(vp.turretBone.position, Vector3.zero);
                foreach (var r in vp.turretBone.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!anyR) { tb = r.bounds; anyR = true; }
                    else tb.Encapsulate(r.bounds);
                }
                if (anyR) turretSpan = Mathf.Max(1f, Mathf.Max(tb.size.x, tb.size.z));
            }
            float backDist = Mathf.Clamp(turretSpan * 0.8f, 1.6f, 3.6f);

            Vector3 camPos = turretPivot - back * backDist;
            camPos.y = turretTopY + 0.42f;

            // 防穿模：机位如果落在自己车体里，就往上抬；再检查前方 3 m 会不会被自己挡住。
            camPos = LiftCameraOutOfSelf(camPos, lookDir, target);

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

    /// <summary>
    /// 把炮手机位从自己车体里"抬"出来。
    /// 船的炮塔在船头、驾驶舱在中部，机位退 5 m 就钻进驾驶舱了 —— 表现就是"放大到脑袋上、看不见前面"。
    /// 这里做两件事：① 机位落在自身包围盒内就抬到盒顶之上；② 前方 3 m 被自己挡住也抬。
    /// </summary>
    Vector3 LiftCameraOutOfSelf(Vector3 camPos, Vector3 lookDir, Transform self)
    {
        var col = self.GetComponent<Collider>();
        if (col == null) return camPos;

        var b = col.bounds;
        for (int i = 0; i < 14; i++)
        {
            bool inside = b.Contains(camPos);
            bool blocked = false;

            if (!inside)
            {
                var hits = Physics.RaycastAll(camPos, lookDir, 3f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider == col) { blocked = true; break; }
                }
            }

            if (!inside && !blocked) return camPos;
            camPos += Vector3.up * 0.55f;
        }
        // 抬不出去就干脆站到车顶之上
        camPos.y = Mathf.Max(camPos.y, b.max.y + 0.5f);
        return camPos;
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

    /// <summary>
    /// 按住 C 时的环视机位（飞机用）：绕着飞机转，看哪边由 yaw/pitch 决定。
    /// 和坦克那套不一样 —— 飞机不跟着镜头走，镜头只是绕它转一圈。
    /// </summary>
    void OrbitAroundTarget()
    {
        float dt = Time.unscaledDeltaTime;

        // 环视距离按飞机尺寸走：战斗机 11 m 长，退 16 m 才看得全
        float want = Mathf.Max(12f, distance);
        _dist = Mathf.Lerp(_dist, want, 8f * dt);

        var cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, normalFov, scopeSpeed * dt);

        Vector3 look = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        Vector3 pivot = target.position + Vector3.up * 1.2f;
        Vector3 desired = pivot - look * _dist;

        float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(desired.x, desired.z), BattlefieldTerrain.WaterLevel) + 2f;
        if (desired.y < ground) desired.y = ground;

        transform.position = Vector3.Lerp(transform.position, desired, 12f * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(pivot - transform.position), 14f * dt);

        if (_shakeAmount > 0.001f)
        {
            _shake = Random.insideUnitSphere * _shakeAmount;
            _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, Time.deltaTime * 14f);
            transform.rotation *= Quaternion.Euler(_shake.x * 2f, _shake.y * 2f, _shake.z * 2f);
        }
    }
}
