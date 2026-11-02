using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 机头进气口的涡轮风扇：程序生成（不依赖模型里有叶片），按空速转起来。
///
/// 为什么是程序生成：Lux3D 出来的飞机是**单网格**，进气口就是一个黑窟窿，
/// 里面没有任何可以单独旋转的零件。所以这里直接造一片涡轮塞进去。
///
/// 位置由 VehicleDef.intakeFans 给出（车体局部坐标，米；Y 上、Z 前）。
/// 注意 Z 是**车头方向**：模型自己的朝向修正（modelYawOffset）已经由
/// VehicleModelBinder 处理掉了，这里按车体坐标摆就行。
/// </summary>
public class JetIntakeFan : MonoBehaviour
{
    Transform[] _rotors;
    VehiclePhysics _vp;
    VehicleDef _def;

    /// <summary>挂到车体上。yOffset = 模型整体被 DropToGround 下移了多少（风扇跟着下移）。</summary>
    public static JetIntakeFan Attach(GameObject root, VehicleDef def, VehiclePhysics vp, float yOffset)
    {
        if (root == null || def == null || def.intakeFans == null || def.intakeFans.Length == 0) return null;

        var go = new GameObject("JetIntakeFans");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var fan = go.AddComponent<JetIntakeFan>();
        fan._vp = vp;
        fan._def = def;
        fan.Build(def, yOffset);
        return fan;
    }

    void Build(VehicleDef def, float yOffset)
    {
        float r = Mathf.Max(0.15f, def.intakeFanRadius);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.17f, 0.18f, 0.19f);
        mat.SetFloat("_Metallic", 0.72f);
        mat.SetFloat("_Glossiness", 0.55f);

        var list = new List<Transform>();
        foreach (var p in def.intakeFans)
        {
            var rotor = new GameObject("Rotor");
            rotor.transform.SetParent(transform, false);
            rotor.transform.localPosition = new Vector3(p.x, p.y + yOffset, p.z);

            // 整流锥
            Part(rotor.transform, PrimitiveType.Cylinder,
                 new Vector3(r * 0.34f, 0.05f, r * 0.34f), Vector3.zero,
                 new Vector3(90f, 0f, 0f), mat);

            // 叶片：绕 Z 均分，往外推，再给一点扭角
            const int N = 9;
            for (int i = 0; i < N; i++)
            {
                float a = 360f * i / N;
                var q = Quaternion.Euler(0f, 0f, a);
                var blade = Part(rotor.transform, PrimitiveType.Cube,
                                 new Vector3(r * 0.15f, r * 0.78f, 0.022f),
                                 q * new Vector3(0f, r * 0.52f, 0f),
                                 Vector3.zero, mat);
                blade.transform.localRotation = q * Quaternion.Euler(0f, 28f, 0f);
            }
            list.Add(rotor.transform);
        }
        _rotors = list.ToArray();
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 scale,
                           Vector3 pos, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "FanPart";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return go;
    }

    void Update()
    {
        if (_rotors == null || _rotors.Length == 0) return;

        float maxSpd = _def != null ? Mathf.Max(1f, _def.speed) : 40f;
        float spd = _vp != null ? Mathf.Abs(_vp.speed) : 0f;
        float throttle = Mathf.Clamp01(spd / maxSpd);

        // 怠速也转（不然停着像个死模型），满油门大概 7 圈/秒
        float degPerSec = 260f + 2260f * throttle;
        float d = degPerSec * Time.deltaTime;
        for (int i = 0; i < _rotors.Length; i++)
            _rotors[i].Rotate(0f, 0f, d, Space.Self);
    }
}
