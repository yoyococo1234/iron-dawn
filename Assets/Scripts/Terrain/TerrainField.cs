using UnityEngine;

/// <summary>
/// 程序化地形：一块 40×40 的测试地，含一段 32° 的坡 + 一个水塘。
/// 高度场存在 float[,]，同时生成 Mesh + MeshCollider + 按高度着色的贴图。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainField : MonoBehaviour
{
    [Header("尺寸")]
    public float size = 40f;         // 边长（米）
    public int resolution = 41;      // 顶点数（41×41 → 每格 1 米）

    [Header("形状（两段坡 + 一个水塘）")]
    [Tooltip("缓坡：坡脚 z，高度 0")]
    public float gentleZFoot = -4f;
    [Tooltip("缓坡：坡顶 z")]
    public float gentleZTop = -9.6f;
    [Tooltip("缓坡顶高度（2.5m/5.6m ≈ 24°）")]
    public float gentleHeight = 2.5f;

    [Tooltip("陡坡：坡脚 z（接缓坡顶）")]
    public float steepZFoot = -9.6f;
    [Tooltip("陡坡：坡顶 z")]
    public float steepZTop = -17.6f;
    [Tooltip("陡坡顶高度（5m/8m ≈ 32°）")]
    public float steepHeight = 7.5f;

    public Vector2 pondCenter = new Vector2(-9f, 10f);
    public float pondRadius = 7f;
    public float pondDepth = 2.5f;

    [Header("水面")]
    public float waterLevel = 0f;

    float[,] heights;
    float step;

    void Awake() => Generate();

    [ContextMenu("重新生成地形")]
    public void Generate()
    {
        step = size / (resolution - 1);
        heights = new float[resolution, resolution];

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                float x = -size * 0.5f + i * step;
                float z = -size * 0.5f + j * step;
                heights[i, j] = ShapeHeight(x, z);
            }
        }

        BuildMesh();
        BuildTexture();
    }

    /// <summary>地形形状：平地 → 24° 缓坡 → 32° 陡坡 → 平台，外加一个水塘。</summary>
    float ShapeHeight(float x, float z)
    {
        float h = 0f;

        if (z <= steepZTop) h = steepHeight;                       // 平台
        else if (z < steepZFoot)                                   // 32° 陡坡
            h = Mathf.Lerp(steepHeight, gentleHeight, (z - steepZTop) / (steepZFoot - steepZTop));
        else if (z < gentleZFoot)                                  // 24° 缓坡
            h = Mathf.Lerp(gentleHeight, 0f, (z - gentleZTop) / (gentleZFoot - gentleZTop));

        // 水塘：圆形凹陷，边缘平滑过渡
        float d = Vector2.Distance(new Vector2(x, z), pondCenter);
        if (d < pondRadius)
        {
            float t = 1f - (d / pondRadius);
            h -= pondDepth * Mathf.SmoothStep(0f, 1f, t);
        }

        return h;
    }

    void BuildMesh()
    {
        var verts = new Vector3[resolution * resolution];
        var uvs = new Vector2[resolution * resolution];

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                int idx = j * resolution + i;
                float x = -size * 0.5f + i * step;
                float z = -size * 0.5f + j * step;
                verts[idx] = new Vector3(x, heights[i, j], z);
                uvs[idx] = new Vector2(i / (float)(resolution - 1), j / (float)(resolution - 1));
            }
        }

        var tris = new int[(resolution - 1) * (resolution - 1) * 6];
        int t = 0;
        for (int j = 0; j < resolution - 1; j++)
        {
            for (int i = 0; i < resolution - 1; i++)
            {
                int a = j * resolution + i;
                int b = a + 1;
                int c = a + resolution;
                int d = c + 1;

                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        var mesh = new Mesh { name = "TerrainMesh" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void BuildTexture()
    {
        const int TEX = 256;
        var tex = new Texture2D(TEX, TEX, TextureFormat.RGB24, false);
        var px = new Color[TEX * TEX];

        Color mud = new Color(0.30f, 0.26f, 0.20f);
        Color sand = new Color(0.70f, 0.64f, 0.46f);
        Color grass = new Color(0.30f, 0.44f, 0.23f);
        Color dirt = new Color(0.44f, 0.37f, 0.25f);
        Color rock = new Color(0.50f, 0.50f, 0.52f);

        for (int y = 0; y < TEX; y++)
        {
            for (int x = 0; x < TEX; x++)
            {
                float wx = -size * 0.5f + (x / (float)(TEX - 1)) * size;
                float wz = -size * 0.5f + (y / (float)(TEX - 1)) * size;
                float h = ShapeHeight(wx, wz);

                Color c;
                if (h < -0.3f) c = Color.Lerp(mud, sand, Mathf.InverseLerp(-pondDepth, -0.3f, h));
                else if (h < 0.6f) c = sand;
                else if (h < 2.5f) c = Color.Lerp(sand, grass, Mathf.InverseLerp(0.6f, 1.6f, h));
                else if (h < 6f) c = Color.Lerp(grass, dirt, Mathf.InverseLerp(2.5f, 6f, h));
                else c = Color.Lerp(dirt, rock, Mathf.InverseLerp(6f, steepHeight, h));

                // 一点噪声，避免纯色块
                float n = (Mathf.PerlinNoise(wx * 0.7f + 13f, wz * 0.7f + 7f) - 0.5f) * 0.06f;
                px[y * TEX + x] = new Color(
                    Mathf.Clamp01(c.r + n), Mathf.Clamp01(c.g + n), Mathf.Clamp01(c.b + n));
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        var mat = new Material(Shader.Find("Standard"));
        mat.mainTexture = tex;
        mat.SetFloat("_Glossiness", 0.05f);
        mat.SetFloat("_Metallic", 0f);
        GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ---------------- 对外查询 ----------------

    /// <summary>取世界坐标 (x,z) 处的地面高度（双线性插值）。</summary>
    public float GetHeight(float x, float z)
    {
        if (heights == null) Generate();

        float fx = (x + size * 0.5f) / step;
        float fz = (z + size * 0.5f) / step;
        int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, resolution - 2);
        int j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, resolution - 2);
        float tx = Mathf.Clamp01(fx - i);
        float tz = Mathf.Clamp01(fz - j);

        float h00 = heights[i, j], h10 = heights[i + 1, j];
        float h01 = heights[i, j + 1], h11 = heights[i + 1, j + 1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    /// <summary>水深（水面 - 地面），负数表示在地面以上。</summary>
    public float GetWaterDepth(float x, float z) => waterLevel - GetHeight(x, z);

    /// <summary>该点的坡度（度）。用左右前后 1 米的差分算。</summary>
    public float GetSlopeDeg(float x, float z)
    {
        float d = 1f;
        float hx = GetHeight(x + d, z) - GetHeight(x - d, z);
        float hz = GetHeight(x, z + d) - GetHeight(x, z - d);
        float grad = Mathf.Sqrt(hx * hx + hz * hz) / (2f * d);
        return Mathf.Atan(grad) * Mathf.Rad2Deg;
    }

    /// <summary>该点的地面法线（用于车身倾斜）。</summary>
    public Vector3 GetNormal(float x, float z)
    {
        float d = 0.5f;
        float hx = GetHeight(x + d, z) - GetHeight(x - d, z);
        float hz = GetHeight(x, z + d) - GetHeight(x, z - d);
        return new Vector3(-hx / (2f * d), 1f, -hz / (2f * d)).normalized;
    }
}
