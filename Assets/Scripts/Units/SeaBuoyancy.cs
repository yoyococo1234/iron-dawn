using UnityEngine;
using Crest;

/// <summary>
/// 船随浪起伏：从 Crest 采样船头/船尾/左右四点的水面高度，
/// 算出升沉（heave）、俯仰（pitch）、横滚（roll），只作用在**模型节点**上。
///
/// 为什么不直接动根节点：载具是 transform 驱动的（没有 Rigidbody），
/// 根节点一歪，炮塔瞄准、碰撞盒、相机全跟着歪。只动模型视觉最安全。
/// </summary>
public class SeaBuoyancy : MonoBehaviour
{
    [Tooltip("采样点离船中心的距离（米）")]
    public float sampleRadius = 7f;
    [Tooltip("升沉幅度倍率")]
    public float heaveScale = 1f;
    [Tooltip("俯仰/横滚角度倍率")]
    public float tiltScale = 1f;
    [Tooltip("平滑速度（越大越跟手）")]
    public float smooth = 5f;

    Transform _model;
    Vector3 _baseLocalPos;
    Quaternion _baseLocalRot;
    readonly SampleHeightHelper _probe = new SampleHeightHelper();

    float _heave;
    float _pitch;
    float _roll;

    bool _rockReady;

    void Start()
    {
        _model = transform.Find("Model");
        if (_model == null) _model = transform;
        _baseLocalPos = _model.localPosition;
        _baseLocalRot = _model.localRotation;
        CreateWaterInteraction();

        // 船尾泡沫尾迹（Crest 泡沫模拟的输入，和水变形是两套数据）
        if (GetComponent<SeaFoamWake>() == null) gameObject.AddComponent<SeaFoamWake>();
    }

    /// <summary>
    /// 把「会跟着船体一起摇」的部件收进一个视觉根：船体模型 + 炮塔 + 炮塔井 + 鱼雷发射管。
    ///
    /// 为什么要有这一步：炮塔、鱼雷管是 VehicleModelBinder 挂在**根节点**上的，
    /// 而船体摇摆只动 Model 节点 —— 换成真实波形之后浪一大，就变成
    /// 「船体在摇、炮塔和鱼雷管纹丝不动」，看着像三个东西各自动各自的。
    /// 收进同一个父节点后一起摇，世界坐标不变（父节点是单位变换，SetParent(..., false) 保留局部变换）。
    ///
    /// 晚一帧再做：VehicleModelBinder 也在 Start 里建这些节点。
    /// </summary>
    void BuildRock()
    {
        _rockReady = true;
        var vr = transform.Find("VisualRock");
        if (vr == null)
        {
            var parts = new System.Collections.Generic.List<Transform>();
            var model = transform.Find("Model");
            if (model != null) parts.Add(model);
            foreach (var nm in new[] { "Turret", "TurretWell" })
            {
                var t = transform.Find(nm);
                if (t != null) parts.Add(t);
            }
            foreach (Transform c in transform)
                if (c.name.StartsWith("TorpedoTube")) parts.Add(c);

            if (parts.Count > 0)
            {
                var go = new GameObject("VisualRock");
                go.transform.SetParent(transform, false);      // 单位变换，世界位置不变
                vr = go.transform;
                foreach (var p in parts) p.SetParent(vr, false);
            }
        }

        _model = vr != null ? vr : transform.Find("Model");
        if (_model == null) _model = transform;
        _baseLocalPos = _model.localPosition;
        _baseLocalRot = _model.localRotation;
    }

    /// <summary>
    /// 船体划过水面时的**局部水变形**：给 Crest 的动态波浪系统喂作用球。
    /// 船头 / 船中 / 船尾各一个，球跟着船走，Crest 按球的速度把水推开、留下尾迹。
    /// </summary>
    void CreateWaterInteraction()
    {
        float halfLen = 8f, radius = 3.2f;
        var b = new Bounds(transform.position, Vector3.one);
        bool any = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any)
        {
            halfLen = Mathf.Clamp(b.size.z * 0.35f, 4f, 14f);
            radius = Mathf.Clamp(b.size.x * 0.35f, 1.8f, 6f);
        }

        // 按 Crest 官方建议：球的 Radius **大致匹配船体**，Weight 只控制强度。
        // 之前给的是"船宽×0.47 + Weight 2.6 × 3 个球"，叠加起来把水顶成一个圆丘 —— 明显不对。
        // 现在：半径 ≈ 船宽的一半，Weight 1 左右，垂直分量压到 0.25（船不该把水顶起来，只是推开）。
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject("WaterInteraction" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.5f, Mathf.Lerp(halfLen * 0.7f, -halfLen * 0.7f, i / 2f));

            var s = go.AddComponent<SphereWaterInteraction>();
            s._radius = Mathf.Clamp(radius * 0.55f, 1.2f, 3.0f);
            s._weight = i == 0 ? 1.0f : 0.7f;      // 船头稍微强一点
            s._weightUpDownMul = 0.25f;            // 垂直分量要小，否则水被顶起来
            s._velocityOffset = 0.4f;
            s._compensateForWaveMotion = 0.3f;
        }
    }

    bool SampleAt(Vector3 worldPos, out float height)
    {
        _probe.Init(worldPos, 0f, true);      // allowMultipleCallsPerFrame
        return _probe.Sample(out height);
    }

    void LateUpdate()
    {
        if (!_rockReady) BuildRock();
        if (_model == null) return;

        Vector3 p = transform.position;
        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;

        float hBow, hStern, hPort, hStar;
        if (!SampleAt(p + fwd * sampleRadius, out hBow)) return;   // 没有海面数据就跳过
        if (!SampleAt(p - fwd * sampleRadius, out hStern)) return;
        if (!SampleAt(p - right * sampleRadius, out hPort)) return;
        if (!SampleAt(p + right * sampleRadius, out hStar)) return;

        float waterLevel = p.y;                                   // 船当前就在水面上
        float targetHeave = ((hBow + hStern + hPort + hStar) * 0.25f - waterLevel) * heaveScale;

        float span = Mathf.Max(0.01f, sampleRadius * 2f);
        float targetPitch = Mathf.Atan2(hBow - hStern, span) * Mathf.Rad2Deg * tiltScale;
        float targetRoll = Mathf.Atan2(hStar - hPort, span) * Mathf.Rad2Deg * tiltScale;

        // 限幅：别让船翻过去
        targetPitch = Mathf.Clamp(targetPitch, -12f, 12f);
        targetRoll = Mathf.Clamp(targetRoll, -12f, 12f);

        float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        _heave = Mathf.Lerp(_heave, targetHeave, k);
        _pitch = Mathf.Lerp(_pitch, targetPitch, k);
        _roll = Mathf.Lerp(_roll, targetRoll, k);

        _model.localPosition = _baseLocalPos + Vector3.up * _heave;
        // 船头抬高 = 绕 X 轴负向转（Unity 里 +X 是低头）
        _model.localRotation = _baseLocalRot * Quaternion.Euler(-_pitch, 0f, _roll);
    }
}
