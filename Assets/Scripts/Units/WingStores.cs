using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 机翼挂架上的外挂物（导弹）。打一发少一枚，复活时重新挂满。
///
/// 为什么需要这个：攻击机的导弹原本是**烘在模型网格里**的（单网格单材质），
/// 没法单独隐藏，所以改成"模型本身不带弹 + 运行时挂 8 个简易弹体"。
///
/// ⚠️ 为什么在 Start 里建而不是在 VehicleModelBinder 里当场建：
/// 模型实例化后还要经过 DropToGround 重新定位（localPosition.y = 1.68），
/// 在 Attach 阶段算世界坐标会少掉这段偏移，弹会掉到机身下面一米多（踩过）。
/// Start 在所有初始化之后跑，这时模型的最终变换才是确定的。
/// </summary>
public class WingStores : MonoBehaviour
{
    public readonly List<GameObject> all = new List<GameObject>();

    [Tooltip("挂弹用的模型节点（由 VehicleModelBinder 填）")]
    public Transform model;
    [Tooltip("每侧挂几枚")]
    public int perSide = 4;
    public Material mat;

    int _fired;
    bool _built;

    /// <summary>还剩几枚（视觉）。</summary>
    public int Remaining => Mathf.Max(0, all.Count - _fired);

    void Start()
    {
        if (!_built && model != null) BuildProps();
    }

    /// <summary>按网格实测的机翼下表面位置挂弹。</summary>
    public void BuildProps()
    {
        _built = true;
        var mf = model.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        var mb = mf.sharedMesh.bounds;
        float halfSpan = mb.extents.x;
        float midZ = mb.center.z;
        var useMat = mat != null ? mat : MakeMat();

        int idx = 0;
        // 按「左1 右1 左2 右2…」交替登记，发射时 ConsumeOne 顺序取，
        // 这样打出去的是左右交替的，不会先把一侧打空（原来 side 在外层，左翼 4 枚打完才轮到右翼）。
        for (int i = 0; i < perSide; i++)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float t = 0.34f + i * 0.14f;
                float mx = side * halfSpan * t;
                Vector3 wingMesh = WingUnderside(mf, mx, midZ);

                var m = new GameObject($"Missile_{idx}");
                m.transform.SetParent(transform, false);
                // 模型此刻的变换已经是最终值，直接换算成世界坐标挂上去
                m.transform.position = mf.transform.TransformPoint(wingMesh);
                m.transform.localRotation = Quaternion.identity;

                Part(m.transform, PrimitiveType.Cylinder, new Vector3(0.16f, 0.55f, 0.16f),
                     new Vector3(0f, 0f, 0.55f), new Vector3(90f, 0f, 0f), useMat);
                Part(m.transform, PrimitiveType.Sphere, new Vector3(0.16f, 0.22f, 0.16f),
                     new Vector3(0f, 0f, 1.12f), Vector3.zero, useMat);
                Part(m.transform, PrimitiveType.Cube, new Vector3(0.42f, 0.02f, 0.20f),
                     new Vector3(0f, 0f, 0.10f), Vector3.zero, useMat);
                Part(m.transform, PrimitiveType.Cube, new Vector3(0.02f, 0.38f, 0.20f),
                     new Vector3(0f, 0f, 0.10f), Vector3.zero, useMat);
                Part(m.transform, PrimitiveType.Cube, new Vector3(0.08f, 0.30f, 0.40f),
                     new Vector3(0f, 0.30f, 0.40f), Vector3.zero, useMat);

                all.Add(m);
                idx++;
            }
        }
    }

    /// <summary>网格坐标里某翼展位置的机翼下表面（X=翼展、Y=前后、Z=高度）。</summary>
    static Vector3 WingUnderside(MeshFilter mf, float localX, float midZ)
    {
        float x = Mathf.Abs(localX);
        float bestZ = float.MaxValue, bestY = 0f;
        bool any = false;
        foreach (var v in mf.sharedMesh.vertices)
        {
            if (Mathf.Abs(Mathf.Abs(v.x) - x) > 0.45f) continue;
            if (Mathf.Abs(v.y) > 1.4f) continue;      // 只取机翼弦长范围
            if (v.z < -1.6f) continue;                // 起落架/吊舱
            if (v.z < bestZ) { bestZ = v.z; bestY = v.y; any = true; }
        }
        if (!any) return new Vector3(localX, 0f, midZ);
        return new Vector3(Mathf.Sign(localX) * x, bestY, bestZ - 0.16f);
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 scale, Vector3 pos, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "MissilePart";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }

    static Material MakeMat()
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.62f, 0.63f, 0.64f);
        m.SetFloat("_Metallic", 0.35f);
        m.SetFloat("_Glossiness", 0.4f);
        return m;
    }

    /// <summary>打掉一枚（隐藏最上面那枚）。</summary>
    public void ConsumeOne()
    {
        if (_fired >= all.Count) return;
        var g = all[_fired];
        _fired++;
        if (g != null) g.SetActive(false);
    }

    /// <summary>复活：全部重新挂上。</summary>
    public void Reset()
    {
        _fired = 0;
        foreach (var g in all)
            if (g != null) g.SetActive(true);
    }
}
