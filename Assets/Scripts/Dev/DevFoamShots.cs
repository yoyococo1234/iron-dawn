using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// 船尾泡沫尾迹的验收截图台（2026-11-06 建）。
///
/// 做什么：在真实地图的开阔海域造一条炮艇，让它自己往前开，等尾迹铺开，
/// 从船尾 / 高空 / 侧面几个机位连拍，再停船拍两张看泡沫消散。
/// 输出：D:/UnityProjects/Tank3D/work/shots/foam/&lt;name&gt;.png + dump.txt
///
/// 用法：DevFoamShots.Begin()   —— Play 模式下由 script-execute 调用。
/// </summary>
public class DevFoamShots : MonoBehaviour
{
    const string Root = "D:/UnityProjects/Tank3D/work/shots/foam";

    public static void Begin()
    {
        var go = new GameObject("DevFoamShots");
        var r = go.AddComponent<DevFoamShots>();
        r.StartCoroutine(r.Run());
    }

    IEnumerator Run()
    {
        var dir = Root;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();

        yield return null;

        // 清场：停掉开局暂停、车库、对局管理，删掉已有载具
        foreach (var v in Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None))
            if (v != null) Object.Destroy(v.gameObject);
        var mm = Object.FindFirstObjectByType<MatchManager>();
        if (mm != null) mm.enabled = false;
        var gs = Object.FindFirstObjectByType<GameStartScreen>();
        if (gs != null) Object.Destroy(gs);
        var garage = Object.FindFirstObjectByType<GarageScreen>();
        if (garage != null) Object.Destroy(garage);
        Time.timeScale = 1f;
        yield return null;
        yield return null;

        // 造炮艇：开阔海域，朝北开
        var def = UnityEditor.AssetDatabase.LoadAssetAtPath<VehicleDef>("Assets/Data/Vehicles/Vehicle_sea_gunboat.asset");
        if (def == null) { Debug.LogError("[DevFoamShots] 找不到炮艇 def"); yield break; }

        var combat = VehicleFactory.Spawn(def, 0, new Vector3(-900f, BattlefieldTerrain.WaterLevel + 0.5f, 280f), 0f, false);
        if (combat == null) { Debug.LogError("[DevFoamShots] 造船失败"); yield break; }
        var ai = combat.GetComponent<VehicleAI>();
        if (ai != null) Object.Destroy(ai);
        var vp = combat.GetComponent<VehiclePhysics>();
        var boat = combat.transform;

        for (int i = 0; i < 5; i++) yield return null;

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[DevFoamShots] 没有主相机"); yield break; }
        var cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.enabled = false;

        // 让它自己往前开
        if (vp != null) { vp.autoDrive = true; vp.autoThrottle = 1f; }

        // 等尾迹铺开（尾迹最长 60 个点 × 1.5 m ≈ 90 m）
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 9f) yield return null;

        log.AppendLine($"[state] speed={(vp != null ? vp.speed : 0f):F2} pos={boat.position.ToString("F1")}");
        log.AppendLine(DumpBounds(boat));
        log.AppendLine(DumpWake(boat));

        // ---- 强度扫描：同一个尾迹，改 peak 看泡沫浓度怎么变（泡沫模拟有时间常数，每次等 3.5 s）----
        var wake = boat.GetComponent<SeaFoamWake>();
        foreach (float pk in new[] { 0.035f })
        {
            if (wake != null) wake.peak = pk;
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 3.5f) yield return null;
            string tag = "peak_" + pk.ToString("F3").Replace('.', '_');
            // 第一次拍的时候相机刚跳过去，Crest 的泡沫模拟还没在新区域累积起来 —— 先拍一张丢掉
            yield return Shot(cam, boat, Path.Combine(dir, "_warm.png"),
                new Vector3(0f, 16f, -60f), new Vector3(0f, 0f, -30f), 50f, 800, 450);
            tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 2f) yield return null;
            yield return Shot(cam, boat, Path.Combine(dir, tag + "_high.png"),
                new Vector3(0f, 16f, -60f), new Vector3(0f, 0f, -30f), 50f, 1600, 900);
            yield return Shot(cam, boat, Path.Combine(dir, tag + "_top.png"),
                new Vector3(0f, 60f, -35f), new Vector3(0f, 0f, -30f), 55f, 1600, 900);
            yield return Shot(cam, boat, Path.Combine(dir, tag + "_third.png"),
                new Vector3(0f, 12f, -40f), new Vector3(0f, 0f, -20f), 55f, 1600, 900);
            log.AppendLine($"peak={pk}: speed={(vp != null ? vp.speed : 0f):F2}");
        }
        if (wake != null) wake.peak = 0.035f;
        { float tw = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - tw < 3.5f) yield return null; }

        var poses = new (string name, Vector3 p, Vector3 look, float fov)[]
        {
            ("01_behind_high", new Vector3(0f, 16f, -60f), new Vector3(0f, 0f, -30f), 50f),
            ("02_behind_low",  new Vector3(0f, 4.5f, -34f), new Vector3(0f, 0f, -18f), 50f),
            ("03_top",         new Vector3(0f, 60f, -35f), new Vector3(0f, 0f, -30f), 55f),
            ("04_side",        new Vector3(38f, 8f, -22f), new Vector3(0f, 0f, -22f), 50f),
            ("05_bow_wake",    new Vector3(22f, 6f, 14f),  new Vector3(0f, 0f, 6f),  50f),
        };
        foreach (var q in poses)
        {
            yield return Shot(cam, boat, Path.Combine(dir, q.name + ".png"), q.p, q.look, q.fov, 1600, 900);
            log.AppendLine($"{q.name}: speed={(vp != null ? vp.speed : 0f):F2} verts={WakeVerts(boat)}");
        }

        // 停船 → 看泡沫消散
        if (vp != null) { vp.autoDrive = false; vp.autoThrottle = 0f; }
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 4f) yield return null;
        yield return Shot(cam, boat, Path.Combine(dir, "10_stopped_4s.png"),
            new Vector3(0f, 18f, -55f), new Vector3(0f, 0f, -28f), 50f, 1600, 900);
        log.AppendLine($"10_stopped_4s: speed={(vp != null ? vp.speed : 0f):F2} verts={WakeVerts(boat)}");

        File.WriteAllText(Path.Combine(dir, "dump.txt"), log.ToString(), Encoding.UTF8);
        Debug.Log("[DevFoamShots] DONE -> " + dir);
        Object.Destroy(gameObject);
    }

    static string DumpBounds(Transform boat)
    {
        var sb = new StringBuilder();
        Bounds b = new Bounds(boat.position, Vector3.one);
        bool any = false;
        foreach (var r in boat.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        sb.AppendLine($"[bounds] size={b.size.ToString("F2")}  -> 船宽≈{b.size.x:F2} 船长≈{b.size.z:F2}");
        return sb.ToString();
    }

    static int WakeVerts(Transform boat)
    {
        var go = GameObject.Find("FoamWake_" + boat.name);
        if (go == null) return -1;
        var mf = go.GetComponent<MeshFilter>();
        return (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : -2;
    }

    static string DumpWake(Transform boat)
    {
        var sb = new StringBuilder();
        var w = boat.GetComponent<SeaFoamWake>();
        if (w == null) { sb.AppendLine("[wake] 没有 SeaFoamWake 组件"); return sb.ToString(); }
        var f = w.GetType();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var samples = f.GetField("_samples", flags).GetValue(w) as System.Collections.IList;
        sb.AppendLine($"[wake] samples={samples.Count} halfWidth={f.GetField("_halfWidth", flags).GetValue(w)} sternOffset={f.GetField("_sternOffset", flags).GetValue(w)} speed={f.GetField("_speed", flags).GetValue(w)}");
        if (samples.Count > 0)
        {
            var first = samples[0]; var last = samples[samples.Count - 1];
            var pf = first.GetType().GetField("pos"); var pb = last.GetType().GetField("birth");
            var p0 = (Vector3)pf.GetValue(first); var p1 = (Vector3)pf.GetValue(last);
            sb.AppendLine($"[wake] 最老样本={p0.ToString("F1")} 最新样本={p1.ToString("F1")} 船={boat.position.ToString("F1")} 差={(p1 - boat.position).magnitude:F1}m");
        }
        var go = GameObject.Find("FoamWake_" + boat.name);
        if (go != null)
        {
            var mr = go.GetComponent<MeshRenderer>();
            var reg = go.GetComponent<Crest.RegisterFoamInput>();
            sb.AppendLine($"[wake] renderer={(mr != null)} sharedMat={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "-")} registerFoam={(reg != null)} enabled={(mr != null && mr.enabled)}");
            var mf = go.GetComponent<MeshFilter>();
            sb.AppendLine($"[wake] meshVerts={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1)}");
        }
        var ocean = Crest.OceanRenderer.Instance;
        if (ocean != null)
        {
            var om = ocean.OceanMaterial;
            sb.AppendLine($"[ocean] createFoamSim={ocean.CreateFoamSim} seaLevel={ocean.SeaLevel:F2} material={(om != null ? om.name : "-")} foamKeyword={(om != null ? om.IsKeywordEnabled("_FOAM_ON") : false)}");
        }
        return sb.ToString();
    }

    IEnumerator Shot(Camera cam, Transform target, string path,
                    Vector3 localPos, Vector3 localLook, float fov, int w, int h)
    {
        Vector3 p = target.TransformPoint(localPos);
        Vector3 l = target.TransformPoint(localLook);
        cam.transform.position = p;
        cam.transform.rotation = Quaternion.LookRotation(l - p);
        cam.fieldOfView = fov;
        yield return null;
        yield return new WaitForEndOfFrame();
        Capture(cam, path, w, h);
    }

    static void Capture(Camera cam, string path, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
#endif
