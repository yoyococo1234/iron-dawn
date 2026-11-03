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

    [Header("陆/海机位（镜头抬高，准星在屏幕中心，炮塔自己转过来对准它）")]
    [Tooltip("机位抬高量 = 跟车距离 × 该值。越大越俯视，车身在画面里越靠下")]
    public float tankHeightFactor = 0.37f;
    [Tooltip("视线相对水平压低多少度。压低后车身落到画面下方；炮管会跟着压到这个角度，" +
             "所以别超过炮管的 maxDepression（默认 8°），否则瞄不到准星")]
    public float tankLookDownDeg = 7f;

    [Header("右键：轻微放大（不换视角）")]
    [Tooltip("按住右键时把视野收窄到这个角度（比 normalFov 小一点即可）")]
    public float rightZoomFov = 30f;

    [Header("开镜（Shift 按住 = 炮镜）")]
    public float normalFov = 50f;
    public float zoomFov = 10f;
    public float scopeSpeed = 12f;
    [Tooltip("开镜时的鼠标灵敏度倍率")]
    public float zoomSensitivity = 0.3f;

    [Header("飞机追尾机位（抬高机位 + 视线压向地面，不开镜也能对地瞄准）")]
    [Tooltip("跟机距离 = 按机身体积算出的距离 × 该值")]
    public float airDistanceFactor = 1.0f;
    [Tooltip("机位抬高量 = 跟机距离 × 该值。0.5 ≈ 从斜上方俯看机背")]
    public float airHeightFactor = 0.5f;
    [Tooltip("视线相对机头压低多少度。平飞时视野 50° 压 18° = 地平线在画面上方 22%")]
    public float airLookDownDeg = 18f;
    [Tooltip("跟机距离下限（米）。太近时飞机一抬头，机身会横满屏幕 —— 这是硬下限")]
    public float airMinDistance = 17f;
    [Tooltip("跟机距离上限（米）")]
    public float airMaxDistance = 44f;
    [Tooltip("镜头跟随飞机滚转的比例：1 = 完全跟随（压杆时地平线跟着斜），0 = 永远水平")]
    [Range(0f, 1f)] public float airRollFollow = 0.85f;

    [Header("飞机开镜（按住 Shift）")]
    [Tooltip("开镜后收窄到的视野角")]
    public float airZoomFov = 22f;
    [Tooltip("开镜时跟机距离乘多少。要 >1：视野收窄 2.3 倍的同时把机位退远，" +
             "飞机在画面里大小不变，被放大的是地面 —— 不然一开镜满屏都是机背")]
    public float airZoomDistFactor = 2.2f;
    [Tooltip("按住右键的轻微放大（不是开镜，机位不动，只把视野收窄一点）")]
    public float airRightZoomFov = 38f;
    [Tooltip("开镜时视线压低多少度。0 = 正对机头（准星在正中间，像真正的机炮瞄准具）；" +
             "视野只有 22°，压低 18° 的话机头准星会飞出画面")]
    public float airScopeLookDownDeg = 0f;

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
    [Tooltip("强制飞机开镜（截图/验收用，正常游戏别勾）")]
    public bool forceAirScopeForTest = false;

    /// <summary>当前是否在自由视角（炮塔不跟鼠标）。</summary>
    public bool FreeLook { get; private set; }

    /// <summary>飞机是否在开镜（按住右键/Shift）。HUD 用它画空战分划。</summary>
    public bool AirScoped => _airScoped;
    bool _airScoped;
    bool _playerAlive;

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
        // 玩家死了到复活之间 mm.Player 是 null。这段必须放在最前面 ——
        // 以前这里直接落到下面的坦克分支，按 Shift 会套上坦克那圈黑色炮镜（用户报的 bug）。
        // 放最前面还有个原因：车被销毁后 target 会变成 null、下面提前 return，
        // 那些"清状态"的代码就永远跑不到，开镜状态会一直留在画面上。
        var mm0 = MatchManager.Instance;
        var pl0 = mm0 != null ? mm0.Player : null;
        _playerAlive = pl0 != null && pl0.Alive;
        if (!_playerAlive)
        {
            _scoped = false;
            _airScoped = false;
            _rightZoom = false;
        }

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

        if (_playerAlive && pl.Def != null && pl.Def.domain == Domain.Air)
        {
            // 飞机用的是自己那套开镜（AirScoped），坦克的炮镜状态必须清掉，
            // 否则从坦克换到飞机时那圈黑色镜筒会一直留在画面上。
            _scoped = false;
            _rightZoom = false;

            bool lockedAir = Cursor.lockState == CursorLockMode.Locked;
            bool freeLookAir = (lockedAir && Input.GetKey(freeLookKey)) || forceFreeLookForTest;
            FreeLook = freeLookAir;
            FreeLookActive = freeLookAir;

            // 滚轮调距离：飞机以前走不到下面那段滚轮代码，所以滚轮完全没反应。
            // 追尾和环视都吃这个 distance。
            float scrollAir = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scrollAir) > 0.0001f)
                distance = Mathf.Clamp(distance - scrollAir * zoomSpeed, 10f, 70f);

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
        _airScoped = false;

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
        _scoped = (_playerAlive && locked && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                  || forceScopeForTest;
        _rightZoom = _playerAlive && locked && Input.GetMouseButton(1) && !_scoped;
        var cam = GetComponent<Camera>();

        // ---------- 炮手视角（右键按住）：相机贴到炮塔上方，炮管就在画面下缘 ----------
        // 战雷里右键就是这个：看到自己的炮管，准星就是炮口指向。按住 Shift 再拉近一档。
        if (_scoped && cam != null)
        {
            var vp = target.GetComponent<VehiclePhysics>();

            // 视线方向 = **炮口真实朝向**，不是鼠标的 yaw/pitch。
            // 炮塔转速有限（turretTurnSpeed），所以开镜后视野是跟着炮塔慢慢转过去的 ——
            // 准星永远在炮口指的方向上，这就是"十字跟着炮塔转"（战雷那套）。
            //
            // 以前这里用的是 yaw/pitch，理由是"跟炮管会形成 相机→炮塔→相机 的反馈"。
            // 其实不会：炮塔的目标角来自 camFollow.yaw（只由鼠标驱动），
            // 相机只是**读**炮口朝向、不回写，所以没有闭环。
            Vector3 lookDirS = vp != null && vp.muzzleBone != null
                ? vp.GetMuzzleForward()
                : Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

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

            Vector3 back = new Vector3(lookDirS.x, 0f, lookDirS.z);
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
            camPos = LiftCameraOutOfSelf(camPos, lookDirS, target);

            float groundS = Mathf.Max(BattlefieldTerrain.SampleHeight(camPos.x, camPos.z), BattlefieldTerrain.WaterLevel) + 1.2f;
            if (camPos.y < groundS) camPos.y = groundS;

            transform.position = Vector3.Lerp(transform.position, camPos, 16f * Time.unscaledDeltaTime);

            // 看点必须从**实际的相机位置**沿 lookDir 推出去（不能用抬升前的 camPos）。
            // 用 camPos 的话，机位被地面/自身碰撞盒抬高之后，视线会比炮口歪两三度 —— 镜心就不是炮口了。
            Vector3 lookAtS = transform.position + lookDirS * 300f;
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

        // ---- 陆/海：和飞机同一个思路 ----
        // 机位放到车"正后方 + 上方"，视线固定压低 tankLookDownDeg —— 不再"盯着车看"。
        // 盯着车看的时候屏幕中心就是车身，炮口准星只能贴在炮塔上（离车身只有 10° 的夹角）。
        // 抬高 + 压低之后：车身落到画面下方、炮口准星升到中上部，两者拉开距离。
        Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        var desired = pivot - flat * _dist + Vector3.up * (_dist * tankHeightFactor);

        // 别把相机塞进地里
        float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(desired.x, desired.z), BattlefieldTerrain.WaterLevel) + 2f;
        if (desired.y < ground) desired.y = ground;

        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.unscaledDeltaTime);

        // 视线压低量 = 基础值 + 鼠标俯仰的偏移（pitch 默认 8° 当"水平"，往上看就减小）
        float lookDownL = tankLookDownDeg + (pitch - 8f);
        Vector3 lookDirL = Quaternion.Euler(lookDownL, yaw, 0f) * Vector3.forward;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(lookDirL, Vector3.up), 14f * Time.unscaledDeltaTime);

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

    /// <summary>
    /// 飞机追尾视角（战雷那套：镜头**跟随机身姿态**）。
    ///
    /// 关键点：机位和视线都建在飞机自己的坐标系里 ——
    ///   · 飞机抬头，镜头跟着抬头，看到的是天空，不是"从上面俯视一架正在爬升的飞机"
    ///   · 飞机滚转，地平线跟着倾斜（airRollFollow 控制跟多少）
    ///   · 视线相对机头固定压低 airLookDownDeg，所以平飞时地面占大半屏、准星落在地面上
    /// 距离有上下限（airMinDistance / airMaxDistance），滚轮拉到底也不会贴到机背上。
    /// 按住右键/Shift 开镜：视野收到 airZoomFov，机位按 airZoomDistFactor 退远，飞机大小不变。
    /// </summary>
    void ChaseCamera()
    {
        float dt = Time.unscaledDeltaTime;
        var cam = GetComponent<Camera>();

        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // Shift = 开镜（大倍率 + 空战分划）；右键 = 只是把视野收窄一点，机位不动。
        // 和坦克那套保持一致：右键是"轻微放大"，Shift 才是开镜。
        _airScoped = (_playerAlive && locked &&
                      (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                     || forceAirScopeForTest;
        _rightZoom = _playerAlive && locked && Input.GetMouseButton(1) && !_airScoped;

        // 距离：先在飞机自己的区间里夹一次，再叠加开镜倍数
        float baseDist = Mathf.Clamp(distance * airDistanceFactor, airMinDistance, airMaxDistance);
        float wantDist = Mathf.Clamp(baseDist * (_airScoped ? airZoomDistFactor : 1f),
                                     airMinDistance, airMaxDistance * airZoomDistFactor);
        _dist = Mathf.Lerp(_dist, wantDist, 8f * dt);

        if (cam != null)
        {
            float wantFov = _airScoped ? airZoomFov : (_rightZoom ? airRightZoomFov : normalFov);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wantFov, scopeSpeed * dt);
        }

        // ---- 机位：在**飞机坐标系**里往后上方放 ----
        // 用机头（含俯仰）而不是机头的水平投影：飞机爬升时机位跟着一起爬，
        // 不会出现"镜头还在地面方向、飞机已经竖着往上飞"的怪画面。
        Vector3 back = target.forward;
        Vector3 upPlane = target.up;

        var desired = target.position - back * _dist + upPlane * (_dist * airHeightFactor);
        float ground = Mathf.Max(BattlefieldTerrain.SampleHeight(desired.x, desired.z), BattlefieldTerrain.WaterLevel) + 2f;
        if (desired.y < ground) desired.y = ground;

        transform.position = Vector3.Lerp(transform.position, desired, 9f * dt);

        // ---- 视线：机头方向再压低（在飞机坐标系里压，所以跟着俯仰走）----
        // 开镜时几乎不压：视野只剩 22°，压 18° 的话机头准星会飞出画面。
        float lookDown = _airScoped ? airScopeLookDownDeg : airLookDownDeg;
        Vector3 lookDir = target.rotation * (Quaternion.AngleAxis(lookDown, Vector3.right) * Vector3.forward);

        // ---- 滚转跟随：airRollFollow = 1 完全跟随（地平线随滚转倾斜），0 = 永远水平 ----
        Vector3 camUp = Vector3.Slerp(Vector3.up, upPlane, Mathf.Clamp01(airRollFollow));
        if (camUp.sqrMagnitude < 0.0001f) camUp = Vector3.up;

        var wantRot = Quaternion.LookRotation(lookDir, camUp);
        transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, 10f * dt);

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

        // 环视距离用 distance（滚轮可调），下限 10 m 保证飞机 11 m 长也看得全
        float want = Mathf.Max(10f, distance);
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
