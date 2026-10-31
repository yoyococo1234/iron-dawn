using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 履带滚动：让履带看起来在跑。
///
/// 为什么不用模型自带的 UV：Lux3D 出来的履带 UV 是乱的（UV 和车长方向基本不相关），
/// 直接平移贴图会变成一片噪点在滑。所以这里：
///   1. 给履带网格**重算一套 UV**：u 沿车长方向按米数铺（一格 = treadMeters），v 用高度
///   2. 贴一张程序生成的「横向履带板」贴图（不用额外美术资源）
///   3. 按车速平移 u —— 看上去就是履带板在往前跑
/// 履带 prefab 由 work/assemble_tank.py 从车体零件里单独拆出来。
/// </summary>
public class TrackScroller : MonoBehaviour
{
    [Tooltip("载具物理（取车速）。留空会自动往上找")]
    public VehiclePhysics vehicle;

    [Tooltip("履带板节距（米）：每多少米一块履带板")]
    public float treadMeters = 0.28f;

    [Tooltip("履带底色")]
    public Color treadColor = new Color(0.30f, 0.30f, 0.29f);

    [Tooltip("履带板亮色")]
    public Color linkColor = new Color(0.46f, 0.46f, 0.44f);

    Material[] _mats;
    Texture2D _tex;

    void Awake()
    {
        if (vehicle == null) vehicle = GetComponentInParent<VehiclePhysics>();
        RebuildUvs();
        BuildTreadMaterial();
    }

    /// <summary>把履带 UV 换成「沿车长按米铺」的平面映射。</summary>
    void RebuildUvs()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mesh = Object.Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + "_tread";
            var v = mesh.vertices;
            var uv = new Vector2[v.Length];

            float zmin = float.MaxValue, zmax = float.MinValue, ymin = float.MaxValue, ymax = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].z < zmin) zmin = v[i].z;
                if (v[i].z > zmax) zmax = v[i].z;
                if (v[i].y < ymin) ymin = v[i].y;
                if (v[i].y > ymax) ymax = v[i].y;
            }
            float len = Mathf.Max(0.01f, zmax - zmin);
            float hgt = Mathf.Max(0.01f, ymax - ymin);
            float tileLen = Mathf.Max(1, Mathf.Round(len / Mathf.Max(0.02f, treadMeters)));

            for (int i = 0; i < v.Length; i++)
            {
                float u = (v[i].z - zmin) / len * tileLen;
                float vv = (v[i].y - ymin) / hgt;
                uv[i] = new Vector2(u, vv);
            }
            mesh.uv = uv;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }
    }

    /// <summary>程序生成一张「横向履带板」贴图：一条暗底 + 一条亮板 + 一点阴影。</summary>
    void BuildTreadMaterial()
    {
        const int W = 32, H = 8;
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        _tex.name = "TrackTread";
        _tex.wrapMode = TextureWrapMode.Repeat;
        _tex.filterMode = FilterMode.Bilinear;

        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)W;                       // 沿履带方向
                Color c = treadColor;
                if (t < 0.55f) c = linkColor;                 // 履带板
                else if (t < 0.62f) c = treadColor * 0.7f;    // 板缝阴影
                // 上下边缘压暗一点，像履带侧边
                float edge = Mathf.Min(y, H - 1 - y) / (float)H;
                c *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(edge * 4f));
                px[y * W + x] = c;
            }
        }
        _tex.SetPixels(px);
        _tex.Apply(true);

        var shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = "TrackTreadMat" };
        mat.color = Color.white;
        mat.mainTexture = _tex;
        mat.SetFloat("_Glossiness", 0.05f);
        mat.SetFloat("_Metallic", 0.0f);
        mat.mainTextureScale = Vector2.one;

        var list = new List<Material>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            list.Add(mat);
        }
        _mats = list.ToArray();
    }

    void Update()
    {
        if (_mats == null || _mats.Length == 0) return;
        float speed = vehicle != null ? vehicle.speed : 0f;
        if (Mathf.Abs(speed) < 0.02f) return;

        float du = speed * Time.deltaTime / Mathf.Max(0.02f, treadMeters);
        for (int i = 0; i < _mats.Length; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            var o = m.mainTextureOffset;
            o.x = Mathf.Repeat(o.x - du, 1f);
            m.mainTextureOffset = o;
        }
    }
}
