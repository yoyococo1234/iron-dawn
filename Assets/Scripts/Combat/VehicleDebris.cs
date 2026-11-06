using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 载具被击毁时"四分五裂"：把车体/炮塔/履带这些网格**按空间切成若干块**，
/// 每块变成独立碎片飞出去（带初速、自旋、重力），几秒后消失。
///
/// 为什么不用现成的碎块预制体：车体是 Lux3D 生成的整块网格，没有可用的分件；
/// 与其手工建模，不如直接在运行时把三角面按空间分桶，切出来的形状跟着原模型走。
///
/// 成本：只在死亡那一刻跑一次（每个网格几十~几万个三角面），切完就完事。
/// </summary>
public static class VehicleDebris
{
    /// <summary>最多炸出多少块（防止重装坦克那种多网格载具炸出上百个对象）。</summary>
    public const int MaxChunks = 30;

    public static void Explode(VehicleCombat vc, float force = 1f)
    {
        if (vc == null) return;

        Vector3 center = vc.transform.position + Vector3.up * 1.2f;

        var renderers = new List<Renderer>();
        foreach (var r in vc.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled) continue;
            if (r.GetComponent<MeshFilter>() == null) continue;
            renderers.Add(r);
        }
        if (renderers.Count == 0) return;

        int budget = MaxChunks;
        int made = 0;

        foreach (var r in renderers)
        {
            if (budget <= 0) break;
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;

            // 网格越大切得越多；小零件（机枪、瞄具）就不切了，整块飞出去。
            // 注意：FBX 导入的模型默认 **Read/Write 关闭**，这种网格读不到 triangles/vertices，
            // 只能整块当一块碎片（否则会抛 "Not allowed to access triangles"）。
            int want = 1;
            if (mesh.isReadable)
            {
                int tris = mesh.triangles.Length / 3;
                want = tris > 3000 ? Mathf.Min(12, budget) : 1;
            }

            var chunks = want <= 1 ? new List<Mesh> { mesh } : SplitMesh(mesh, r.transform, want);
            foreach (var cm in chunks)
            {
                if (budget-- <= 0) break;
                SpawnChunk(cm, r, center, force);
                made++;
            }
        }

        if (made > 0)
            Debug.Log($"[Debris] {vc.name} 炸成 {made} 块");
    }

    static void SpawnChunk(Mesh mesh, Renderer src, Vector3 center, float force)
    {
        var go = new GameObject("Debris");
        go.transform.position = src.transform.position;
        go.transform.rotation = src.transform.rotation;
        go.transform.localScale = src.transform.lossyScale;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = src.sharedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 碎片初速：从爆炸中心往外推 + 一点向上 + 随机
        Vector3 dir = (go.transform.position - center);
        if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitSphere;
        dir.Normalize();
        float speed = Random.Range(3f, 9f) * force;

        var piece = go.AddComponent<DebrisPiece>();
        piece.velocity = dir * speed + Vector3.up * Random.Range(2f, 6f);
        piece.spin = Random.insideUnitSphere * Random.Range(120f, 400f);
        piece.life = Random.Range(2.6f, 4.2f);
    }

    /// <summary>
    /// 把一个网格按三角面重心所在的空间分桶，切成最多 want 块。
    /// 每块都是独立 Mesh（顶点按三角形拆开，不做合并），保留原 UV / 法线。
    /// </summary>
    static List<Mesh> SplitMesh(Mesh src, Transform xf, int want)
    {
        var verts = src.vertices;
        var uvs = src.uv;
        var normals = src.normals;
        var tris = src.triangles;
        if (verts.Length == 0 || tris.Length < 3) return new List<Mesh> { src };

        // 本地包围盒 → 决定切几刀（按最长两根轴切）
        var lb = src.bounds;
        Vector3 size = lb.size;
        int nx = 3, ny = 2, nz = 2;
        if (size.x >= size.z) { nx = 3; nz = 2; } else { nx = 2; nz = 3; }
        if (size.y < size.x * 0.35f) ny = 2;

        var buckets = new Dictionary<int, List<int>>();   // 桶号 → 三角形序号
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 c = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f;
            Vector3 n = new Vector3(
                Mathf.InverseLerp(lb.min.x, lb.max.x, c.x),
                Mathf.InverseLerp(lb.min.y, lb.max.y, c.y),
                Mathf.InverseLerp(lb.min.z, lb.max.z, c.z));
            int ix = Mathf.Clamp((int)(n.x * nx), 0, nx - 1);
            int iy = Mathf.Clamp((int)(n.y * ny), 0, ny - 1);
            int iz = Mathf.Clamp((int)(n.z * nz), 0, nz - 1);
            int key = (ix * ny + iy) * nz + iz;
            if (!buckets.TryGetValue(key, out var list)) { list = new List<int>(); buckets[key] = list; }
            list.Add(t);
        }

        var result = new List<Mesh>();
        foreach (var kv in buckets)
        {
            var triIdx = kv.Value;
            if (triIdx.Count == 0) continue;

            var nv = new List<Vector3>(triIdx.Count * 3);
            var nuv = new List<Vector2>(triIdx.Count * 3);
            var nn = new List<Vector3>(triIdx.Count * 3);
            var nt = new List<int>(triIdx.Count * 3);
            foreach (int t in triIdx)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t + k];
                    nt.Add(nv.Count);
                    nv.Add(verts[vi]);
                    nuv.Add(uvs.Length > vi ? uvs[vi] : Vector2.zero);
                    nn.Add(normals.Length > vi ? normals[vi] : Vector3.up);
                }
            }

            var m = new Mesh { name = src.name + "_chunk" };
            m.SetVertices(nv);
            m.SetUVs(0, nuv);
            m.SetNormals(nn);
            m.SetTriangles(nt, 0);
            m.RecalculateBounds();
            result.Add(m);

            if (result.Count >= want) break;
        }
        return result.Count > 0 ? result : new List<Mesh> { src };
    }
}

/// <summary>一块碎片：自己飞、自己转，到时间缩没。</summary>
public class DebrisPiece : MonoBehaviour
{
    public Vector3 velocity;
    public Vector3 spin;
    public float life = 3f;

    float _t;
    Vector3 _baseScale;
    bool _init;

    void Update()
    {
        if (!_init) { _baseScale = transform.localScale; _init = true; }

        float dt = Time.deltaTime;
        _t += dt;

        velocity += Vector3.down * 9.8f * dt;
        transform.position += velocity * dt;
        transform.Rotate(spin * dt, Space.World);

        // 落地就慢下来（简单起见只看地形高度）
        float ground = BattlefieldTerrain.SampleHeight(transform.position.x, transform.position.z);
        if (transform.position.y < ground + 0.05f)
        {
            transform.position = new Vector3(transform.position.x, ground + 0.05f, transform.position.z);
            velocity *= 0.55f;
            spin *= 0.5f;
        }

        float left = life - _t;
        if (left < 0.6f) transform.localScale = _baseScale * Mathf.Clamp01(left / 0.6f);
        if (_t >= life) Destroy(gameObject);
    }
}
