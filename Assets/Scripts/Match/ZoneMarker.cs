using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 资源点的世界表现：贴地圆环 + 中心光柱，颜色随归属变化（蓝=我方 / 红=敌方 / 灰=中立）。
/// 圆环按地形逐点采样高度，所以在山地/海岸也不会埋进土里。
/// 由 MatchManager.Start 调用 BuildAll 自动生成，不需要手动摆。
/// </summary>
public class ZoneMarker : MonoBehaviour
{
    public ZoneState state;

    LineRenderer _ring;
    Transform _beam;
    Material _beamMat;

    const int RingSegments = 96;

    public static void BuildAll(List<ZoneState> zones)
    {
        if (zones == null) return;
        var root = GameObject.Find("ZoneMarkers");
        if (root == null) root = new GameObject("ZoneMarkers");

        foreach (var z in zones)
        {
            if (z == null || z.def == null) continue;
            var go = new GameObject($"Zone_{z.def.id}");
            go.transform.SetParent(root.transform, false);
            var m = go.AddComponent<ZoneMarker>();
            m.state = z;
            m.Build();
        }
    }

    void Build()
    {
        var b = state.def.bounds;
        Vector3 center = new Vector3(b.center.x, 0f, b.center.y);
        float radius = Mathf.Max(12f, Mathf.Min(b.width, b.height) * 0.42f);

        // ---- 贴地圆环 ----
        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(transform, false);
        _ring = ringGo.AddComponent<LineRenderer>();
        _ring.useWorldSpace = true;
        _ring.loop = true;
        _ring.positionCount = RingSegments;
        _ring.widthMultiplier = Mathf.Max(3f, radius * 0.02f);
        _ring.material = new Material(Shader.Find("Sprites/Default"));
        _ring.numCapVertices = 0;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows = false;

        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            float x = center.x + Mathf.Cos(a) * radius;
            float z = center.z + Mathf.Sin(a) * radius;
            float y = Mathf.Max(BattlefieldTerrain.SampleHeight(x, z), BattlefieldTerrain.WaterLevel) + 2.5f;
            _ring.SetPosition(i, new Vector3(x, y, z));
        }

        // ---- 中心光柱 ----
        var beamGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beamGo.name = "Beam";
        beamGo.transform.SetParent(transform, false);
        Object.Destroy(beamGo.GetComponent<Collider>());
        float groundY = Mathf.Max(BattlefieldTerrain.SampleHeight(center.x, center.z), BattlefieldTerrain.WaterLevel);
        beamGo.transform.position = new Vector3(center.x, groundY + 60f, center.z);
        beamGo.transform.localScale = new Vector3(radius * 0.045f, 60f, radius * 0.045f);
        _beamMat = new Material(Shader.Find("Sprites/Default"));
        beamGo.GetComponent<Renderer>().sharedMaterial = _beamMat;
        _beam = beamGo.transform;

        ApplyColor();
    }

    void Update() => ApplyColor();

    void ApplyColor()
    {
        var c = state.OwnerColor;

        if (_ring != null)
        {
            _ring.startColor = new Color(c.r, c.g, c.b, 0.85f);
            _ring.endColor = new Color(c.r, c.g, c.b, 0.85f);
        }

        if (_beamMat != null)
        {
            // 占领中会闪，方便远处看出"正在被抢"
            float pulse = state.progressTeam >= 0 && state.owner != state.progressTeam
                ? 0.22f + 0.16f * Mathf.Abs(Mathf.Sin(Time.time * 3f))
                : 0.16f;
            _beamMat.color = new Color(c.r, c.g, c.b, pulse);
        }
    }
}
