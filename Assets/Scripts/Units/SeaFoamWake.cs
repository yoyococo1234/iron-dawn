using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Crest;

/// <summary>
/// 船尾泡沫尾迹。
///
/// 为什么不用 SphereWaterInteraction：那个组件只往**动态波浪**模拟里写数据（把水推开），
/// 泡沫是另一张模拟图（LodDataMgrFoam），需要单独的输入。Crest 的泡沫输入要求
/// "一个带网格的渲染器 + Crest/Inputs/Foam/ 下的着色器 + RegisterFoamInput 组件"。
///
/// 所以这里把船尾走过的点连成一条**飘带**（ribbon）：
///   顶点色 R 通道 = 这一点的泡沫强度（着色器就是 return _Strength * col.r）
///   飘带随时间变宽、变淡，模拟尾迹向外扩散
/// 整条飘带在世界空间，父节点挂在原点，船往前走飘带就自然拖在后面。
///
/// 数值怎么定的（重要，别乱调）：
///   泡沫模拟是"每帧往里加、按 _foamFadeRate 衰减"的累加器，
///   稳态浓度 ≈ 每帧写入量 / (衰减率 × 帧时长) ≈ 写入量 × 37.5（SimFoam 默认 fade 0.8 / 30Hz）。
///   所以顶点色要给 **0.01~0.05 量级**，给 0.5 会整条饱和成一条白带。
/// </summary>
public class SeaFoamWake : MonoBehaviour
{
    [Header("形状")]
    [Tooltip("船每前进这么多米记一个点")]
    public float sampleSpacing = 2.2f;
    [Tooltip("最多保留多少个点（决定尾巴最长多长）")]
    public int maxSamples = 70;
    [Tooltip("一个点从产生到消失的时间（秒）")]
    public float lifetime = 6.5f;
    [Tooltip("飘带初始宽度 = 船宽 × 这个系数")]
    public float widthMul = 1.6f;
    [Tooltip("每秒向外扩散多少米（尾迹张角）")]
    public float spread = 1.1f;
    [Tooltip("边缘抖动（0 = 笔直的带子，1 = 毛边）")]
    public float edgeJitter = 0.18f;

    [Header("强度")]
    [Tooltip("船尾最浓处的每帧写入量（0.01~0.06，给大了整条饱和成白带）")]
    public float peak = 0.035f;
    [Tooltip("到这个速度泡沫最浓")]
    public float fullSpeed = 8f;
    [Tooltip("低于这个速度不产泡沫（停船就不该有尾迹）")]
    public float minSpeed = 0.6f;
    [Tooltip("速度平滑（越大越跟手）")]
    public float smooth = 4f;

    class Sample
    {
        public Vector3 pos;
        public Vector3 right;
        public float birth;
        public float halfWidth;
        public float jitter;
    }

    readonly List<Sample> _samples = new List<Sample>();
    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<Color> _cols = new List<Color>();
    readonly List<int> _tris = new List<int>();

    Mesh _mesh;
    MeshRenderer _mr;
    Material _mat;
    GameObject _wakeGo;

    float _halfWidth = 2f;
    float _sternOffset = 6f;
    float _speed;
    float _seaLevel;
    float _bornAt;
    bool _widthLocked;
    Vector3 _lastPos;

    // 横截面 4 列：左外、左内、右内、右外。外侧给 0，让边缘化开
    static readonly float[] k_Profile = { 0f, 1f, 1f, 0f };
    static readonly float[] k_Across = { -1f, -0.45f, 0.45f, 1f };
    const int k_Cols = 4;

    void Start()
    {
        Create();
    }

    void OnEnable()
    {
        // 复活时载具是 SetActive(true) 复用同一个物体，尾迹要重建
        if (_mesh == null) Create();
    }

    void OnDisable()
    {
        // 击沉/阵亡时载具是 SetActive(false)，**不销毁**。
        // 如果这时不把输入撤掉，Crest 会一直按最后那帧的飘带往泡沫模拟里画，
        // 海面上就留下一坨永远散不掉的白泡沫。
        DestroyWake();
    }

    void Create()
    {
        if (_mesh != null) return;

        _bornAt = Time.time;
        _lastPos = transform.position;
        _widthLocked = false;
        _samples.Clear();
        _speed = 0f;

        var ocean = OceanRenderer.Instance;
        _seaLevel = ocean != null ? ocean.SeaLevel : transform.position.y;

        _wakeGo = new GameObject("FoamWake_" + name);
        _wakeGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        _mesh = new Mesh { name = "FoamWake" };
        _mesh.MarkDynamic();
        _mesh.indexFormat = IndexFormat.UInt16;

        var mf = _wakeGo.AddComponent<MeshFilter>();
        mf.sharedMesh = _mesh;

        _mr = _wakeGo.AddComponent<MeshRenderer>();
        _mr.shadowCastingMode = ShadowCastingMode.Off;
        _mr.receiveShadows = false;
        _mr.lightProbeUsage = LightProbeUsage.Off;
        _mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // 材质优先用 Resources 里的资产：打包时 Shader.Find 找到的 shader 有可能被剥离，
        // 而 Resources 里被引用的 shader 一定会进包。
        var baseMat = Resources.Load<Material>("Materials/FoamWake");
        if (baseMat != null)
        {
            _mat = new Material(baseMat);
        }
        else
        {
            var sh = Shader.Find("Crest/Inputs/Foam/Add From Vert Colours");
            if (sh == null)
            {
                Debug.LogWarning("[SeaFoamWake] 找不到泡沫输入着色器，尾迹关闭");
                enabled = false;
                return;
            }
            _mat = new Material(sh);
        }
        _mat.SetFloat("_Strength", 1f);
        _mr.sharedMaterial = _mat;

        // 必须最后加：它 Start 时会去读 renderer.sharedMaterial
        _wakeGo.AddComponent<RegisterFoamInput>();

        // 先用根节点估算，等模型挂上来（VehicleModelBinder 也在 Start）再锁真宽度
        MeasureWidth();
    }

    void MeasureWidth()
    {
        Bounds b = new Bounds(transform.position, Vector3.one);
        bool any = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any)
        {
            _halfWidth = Mathf.Clamp(b.size.x * 0.5f * widthMul, 3f, 9f);
            _sternOffset = Mathf.Clamp(b.size.z * 0.5f * 0.75f, 3f, 20f);
        }
    }

    void LateUpdate()
    {
        if (_mesh == null) return;

        // 模型挂好之后再量一次真实船宽（Start 时可能还没 Attach）
        if (!_widthLocked && Time.time - _bornAt > 0.4f)
        {
            MeasureWidth();
            _widthLocked = true;
        }

        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float now = Time.time;

        // 实际水平速度（载具是 transform 驱动的，没有 Rigidbody）
        Vector3 delta = transform.position - _lastPos;
        delta.y = 0f;
        float instSpeed = delta.magnitude / dt;
        _lastPos = transform.position;
        _speed = Mathf.Lerp(_speed, instSpeed, 1f - Mathf.Exp(-smooth * dt));

        var ocean = OceanRenderer.Instance;
        if (ocean != null) _seaLevel = ocean.SeaLevel;

        // 注意：飘带顶点是**世界坐标**，所以承载网格的物体必须留在原点（单位变换）。
        // 一旦把这个物体挪到船身上，顶点会被再乘一次物体的变换矩阵，整条飘带直接飘到几百米外。

        // 够远才记一个点（停着不动不会堆点）
        if (_speed >= minSpeed)
        {
            // 记的是**船尾**的位置，不是船中心，否则泡沫和船体之间会空一段
            Vector3 p = transform.position - transform.forward * _sternOffset;
            p.y = _seaLevel;
            if (_samples.Count == 0 || Vector3.Distance(p, _samples[_samples.Count - 1].pos) >= sampleSpacing)
            {
                _samples.Add(new Sample
                {
                    pos = p,
                    right = transform.right,
                    birth = now,
                    halfWidth = _halfWidth,
                    jitter = Random.value,
                });
                while (_samples.Count > maxSamples) _samples.RemoveAt(0);
            }
        }

        // 清掉过期点
        for (int i = _samples.Count - 1; i >= 0; i--)
        {
            if (now - _samples[i].birth > lifetime) _samples.RemoveAt(i);
        }

        // 速度 → 浓度（停船尾迹自然淡掉）
        float speedK = Mathf.Clamp01((_speed - minSpeed) / Mathf.Max(0.01f, fullSpeed - minSpeed));

        RebuildMesh(now, speedK);
    }

    void RebuildMesh(float now, float speedK)
    {
        _verts.Clear();
        _cols.Clear();
        _tris.Clear();

        int n = _samples.Count;
        if (n >= 2 && speedK > 0.001f)
        {
            for (int i = 0; i < n; i++)
            {
                var s = _samples[i];
                float age = now - s.birth;
                float t = Mathf.Clamp01(age / lifetime);

                // 越老越宽（向外扩散）、越淡
                float half = s.halfWidth + spread * age;
                float fade = 1f - t;
                fade *= fade;                        // 平方衰减，尾巴更快消失

                float v = peak * fade * speedK;
                float jitter = 1f + (s.jitter - 0.5f) * 2f * edgeJitter;

                for (int c = 0; c < k_Cols; c++)
                {
                    _verts.Add(s.pos + s.right * (half * k_Across[c] * jitter));
                    float a = v * k_Profile[c];
                    _cols.Add(new Color(a, a, a, 1f));
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                for (int c = 0; c < k_Cols - 1; c++)
                {
                    int a = i * k_Cols + c;
                    int b = a + 1;
                    int cc = a + k_Cols;
                    int d = cc + 1;
                    _tris.Add(a); _tris.Add(cc); _tris.Add(b);
                    _tris.Add(b); _tris.Add(cc); _tris.Add(d);
                }
            }
        }

        _mesh.Clear();
        if (_verts.Count > 0)
        {
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
        else
        {
            _mesh.bounds = new Bounds(new Vector3(0f, _seaLevel, 0f), Vector3.one);
        }
    }

    void OnDestroy()
    {
        DestroyWake();
    }

    void DestroyWake()
    {
        // 先把网格清空再销毁物体：万一销毁延迟到帧末，这一帧也不会再往泡沫模拟里画东西
        if (_mesh != null) { _mesh.Clear(); }
        _samples.Clear();

        if (_wakeGo != null) Destroy(_wakeGo);
        if (_mesh != null) Destroy(_mesh);
        if (_mat != null) Destroy(_mat);

        _wakeGo = null;
        _mesh = null;
        _mr = null;
        _mat = null;
    }
}
