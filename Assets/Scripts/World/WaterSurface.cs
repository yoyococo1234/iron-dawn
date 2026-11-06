using UnityEngine;

/// <summary>
/// 近处海面：一块**相机跟随的细分网格**，用来做真实的顶点波高。
///
/// 为什么需要它：原来整片海面就是 Unity 内置 Plane（121 个顶点撑 2000 米），
/// 顶点间距 200 米，任何顶点位移都看不出来 —— 只能做法线，看起来就是一块平的蓝布。
/// 现在近处换 256×256 的网格（1.6 米一格），远处保留大平面但把它盖住的那块挖掉。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterSurface : MonoBehaviour
{
    [Tooltip("网格分辨率（每边格子数）")]
    public int segments = 256;
    [Tooltip("近处网格覆盖的边长（米）")]
    public float size = 420f;
    [Tooltip("跟随的目标（一般就是主相机）")]
    public Transform follow;
    [Tooltip("远处的整片水面（会被挖掉近处那块）")]
    public Renderer farWater;

    Mesh _mesh;
    MaterialPropertyBlock _mpb;
    float _cell;

    void Awake()
    {
        Build();
    }

    void OnEnable()
    {
        if (_mesh != null) Apply();
    }

    void Build()
    {
        segments = Mathf.Clamp(segments, 8, 512);
        _cell = size / segments;

        var mf = GetComponent<MeshFilter>();
        _mesh = new Mesh { name = "WaterPatch" };
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        int n = segments + 1;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        float half = size * 0.5f;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                verts[i] = new Vector3(-half + x * _cell, 0f, -half + z * _cell);
                uvs[i] = new Vector2((float)x / segments, (float)z / segments);
            }

        var tris = new int[segments * segments * 6];
        int t = 0;
        for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int i = z * n + x;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }

        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
        mf.sharedMesh = _mesh;
        _mpb = new MaterialPropertyBlock();
        Apply();
    }

    void LateUpdate()
    {
        var cam = follow != null ? follow : (Camera.main != null ? Camera.main.transform : null);
        if (cam == null) return;

        // 吸附到格子：网格整体平移时波面不会"抖"（波函数用的是世界坐标）
        Vector3 p = cam.position;
        float x = Mathf.Round(p.x / _cell) * _cell;
        float z = Mathf.Round(p.z / _cell) * _cell;
        transform.position = new Vector3(x, transform.position.y, z);

        Apply();
    }

    void Apply()
    {
        // 告诉远处的大平面：这块区域别画了
        if (farWater == null) return;
        var mr = farWater.GetComponent<MeshRenderer>();
        if (mr == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(_mpb);
        _mpb.SetVector("_PatchCenter", new Vector4(transform.position.x, 0f, transform.position.z, 0f));
        _mpb.SetFloat("_PatchRadius", size * 0.47f);
        mr.SetPropertyBlock(_mpb);
    }
}
