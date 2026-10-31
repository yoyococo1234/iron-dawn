using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 履带滚动：按车速平移履带材质的 UV，看起来就是履带在跑。
///
/// 为什么用 UV 滚动而不是转轮子：Lux3D 出来的履带是一整块面片汤，
/// 没有"轮子/链节"这种可单独旋转的部件；给履带单独一份材质、
/// 让它按车速滚动贴图，是这套素材上最省又最像的做法。
/// 履带 prefab 由 work/build_turret2.py 从 part_split 的结果里单独导出。
/// </summary>
public class TrackScroller : MonoBehaviour
{
    [Tooltip("载具物理（取车速）。留空会自动往上找")]
    public VehiclePhysics vehicle;

    [Tooltip("每跑 1 米，UV 平移多少（履带贴图一格大约 1/uvPerMeter 米长）")]
    public float uvPerMeter = 0.35f;

    [Tooltip("滚动轴：true = 沿 U，false = 沿 V（不同模型 UV 方向不一样）")]
    public bool scrollU = true;

    [Tooltip("倒车时反向滚")]
    public bool reverseOnBackward = true;

    Material[] _mats;
    float _lastSpeed;

    void Awake()
    {
        if (vehicle == null) vehicle = GetComponentInParent<VehiclePhysics>();
        CacheMaterials();
    }

    void CacheMaterials()
    {
        var list = new List<Material>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            // .materials 是实例化出来的副本，改它不会影响别的车
            var ms = r.materials;
            if (ms != null) list.AddRange(ms);
        }
        _mats = list.ToArray();
    }

    void Update()
    {
        if (_mats == null || _mats.Length == 0)
        {
            CacheMaterials();
            if (_mats == null || _mats.Length == 0) return;
        }

        float v = vehicle != null ? vehicle.speed : 0f;
        _lastSpeed = v;
        if (Mathf.Abs(v) < 0.02f) return;

        float du = v * uvPerMeter * Time.deltaTime;
        for (int i = 0; i < _mats.Length; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            var o = m.mainTextureOffset;
            if (scrollU) o.x = Mathf.Repeat(o.x + du, 1f);
            else o.y = Mathf.Repeat(o.y + du, 1f);
            m.mainTextureOffset = o;
        }
    }
}
