using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// 验收用截图台（2026-11-01 深夜建）。
///
/// 为什么要有它：前一个人用 DevCapture 拍单张、自己挑角度和光，用户看到的是游戏内第三人称
/// （远、暗、有阵营色调 + 后处理），两边对不上，来回三次都没修好。
///
/// 这里做的是：在**真实地图**上、用**游戏内第三人称的实际机位与 FOV**、跑在 **Play 模式**里
/// （TrackAssembly 已经跑完 Start，履带材质/顶点数据都是运行时真实状态）连拍多角度 + 运动序列，
/// 并把 TrackAssembly 的运行时数据 dump 出来当证据。
///
/// 用法（编辑器里执行）：
///   DevTankShots.Begin(def, "light", pos, yaw);
/// 输出：D:/UnityProjects/Tank3D/work/shots/&lt;tag&gt;/*.png + dump.txt
/// </summary>
public class DevTankShots : MonoBehaviour
{
    const string Root = "D:/UnityProjects/Tank3D/work/shots";

    public static void Begin(VehicleDef def, string tag, Vector3 pos, float yaw)
    {
        var go = new GameObject("DevTankShots");
        var r = go.AddComponent<DevTankShots>();
        r.StartCoroutine(r.Run(def, tag, pos, yaw));
    }

    IEnumerator Run(VehicleDef def, string tag, Vector3 pos, float yaw)
    {
        var dir = Path.Combine(Root, tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();

        yield return null;

        foreach (var v in Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None))
            if (v != null) Object.Destroy(v.gameObject);
        var mm = Object.FindFirstObjectByType<MatchManager>();
        if (mm != null) mm.enabled = false;
        // GameStartScreen 每帧把 Time.timeScale 按回 0（"没开局就暂停"），不干掉它车根本不动
        var gs = Object.FindFirstObjectByType<GameStartScreen>();
        if (gs != null) Object.Destroy(gs);
        var garage = Object.FindFirstObjectByType<GarageScreen>();
        if (garage != null) Object.Destroy(garage);
        Time.timeScale = 1f;
        yield return null;
        yield return null;

        var combat = VehicleFactory.Spawn(def, 0, pos, yaw, false);
        if (combat == null) { Debug.LogError("[DevTankShots] 造车失败"); yield break; }
        var ai = combat.GetComponent<VehicleAI>();
        if (ai != null) Object.Destroy(ai);
        var vp = combat.GetComponent<VehiclePhysics>();
        var tank = combat.transform;

        for (int i = 0; i < 4; i++) yield return null;

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[DevTankShots] 没有主相机"); yield break; }
        var cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.enabled = false;

        log.AppendLine(Dump(tank));

        // ---- 整车多角度（游戏内第三人称 + 侧视 + 近景）----
        var poses = new (string name, Vector3 p, Vector3 look, float fov)[]
        {
            ("01_behind",       new Vector3(0f, 4.2f, -14f),    new Vector3(0f, 2.2f, 0f),    50f),
            ("02_side_L",       new Vector3(-13f, 3.2f, 0f),    new Vector3(0f, 1.6f, 0f),    50f),
            ("03_side_R",       new Vector3(13f, 3.2f, 0f),     new Vector3(0f, 1.6f, 0f),    50f),
            ("04_front34",      new Vector3(10f, 3.5f, 9f),     new Vector3(0f, 1.8f, 0f),    50f),
            ("05_close_L_mid",  new Vector3(-6.5f, 1.3f, 0f),   new Vector3(0f, 0.75f, 0f),   40f),
            ("06_close_L_front",new Vector3(-5.0f, 1.2f, 5.6f), new Vector3(0f, 0.7f, 3.0f),  38f),
            ("07_close_L_rear", new Vector3(-5.0f, 1.2f, -5.6f),new Vector3(0f, 0.7f, -3.0f), 38f),
            ("08_close_R_front",new Vector3(5.0f, 1.2f, 5.6f),  new Vector3(0f, 0.7f, 3.0f),  38f),
            ("09_close_R_rear", new Vector3(5.0f, 1.2f, -5.6f), new Vector3(0f, 0.7f, -3.0f), 38f),
            ("10_low_front34",  new Vector3(-6f, 0.45f, 7f),    new Vector3(0f, 0.8f, 1f),    45f),
        };
        foreach (var q in poses)
        {
            yield return Shot(cam, tank, Path.Combine(dir, q.name + ".png"), q.p, q.look, q.fov, 1600, 900);
            log.AppendLine($"{q.name}: cam={cam.transform.position.ToString("F2")} fov={q.fov}");
        }

        // ---- 只看履带总成（把车体/炮塔藏起来）----
        var hullTf = tank.Find("Model");
        var turretTf = tank.Find("Turret");
        SetRenderersEnabled(hullTf, false);
        SetRenderersEnabled(turretTf, false);
        yield return null;
        yield return Shot(cam, tank, Path.Combine(dir, "30_trackonly_L.png"), new Vector3(-9f, 1.6f, 0f), new Vector3(0f, 0.7f, 0f), 45f, 1600, 900);
        yield return Shot(cam, tank, Path.Combine(dir, "31_trackonly_R.png"), new Vector3(9f, 1.6f, 0f), new Vector3(0f, 0.7f, 0f), 45f, 1600, 900);
        yield return Shot(cam, tank, Path.Combine(dir, "32_trackonly_f34.png"), new Vector3(-7f, 2.4f, 8f), new Vector3(0f, 0.7f, 1f), 45f, 1600, 900);
        yield return Shot(cam, tank, Path.Combine(dir, "33_trackonly_top.png"), new Vector3(-2f, 6.5f, -2f), new Vector3(0f, 0.7f, 0f), 45f, 1600, 900);
        SetRenderersEnabled(hullTf, true);
        SetRenderersEnabled(turretTf, true);

        // ---- 运动：慢动作 + 侧视跟随，看履带会不会撕/抖/卡 ----
        if (vp != null) { vp.autoDrive = true; vp.autoThrottle = 1f; }
        float prevScale = Time.timeScale;
        Time.timeScale = 0.35f;
        for (int i = 0; i < 6; i++)
        {
            yield return Shot(cam, tank, Path.Combine(dir, $"10_move_{i}.png"),
                new Vector3(-13f, 3.2f, 0f), new Vector3(0f, 1.6f, 0f), 50f, 1600, 900);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.45f) yield return null;
        }
        yield return Shot(cam, tank, Path.Combine(dir, "20_move_behind.png"),
            new Vector3(0f, 4.2f, -14f), new Vector3(0f, 2.2f, 0f), 50f, 1600, 900);
        Time.timeScale = prevScale;
        if (vp != null) { vp.autoDrive = false; vp.autoThrottle = 0f; }

        // ---- 履带滚动自检：车停住，只让履带按 debugSpeed 走，拍两张看履带板有没有位移 ----
        {
            var asm = tank.GetComponentInChildren<TrackAssembly>(true);
            if (asm != null && vp != null)
            {
                vp.speed = 0f;
                asm.debugSpeed = 4f;
                yield return Shot(cam, tank, Path.Combine(dir, "40_scroll_a.png"),
                    new Vector3(-5.0f, 1.2f, 1.5f), new Vector3(0f, 0.7f, 0f), 38f, 1600, 900);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
                yield return Shot(cam, tank, Path.Combine(dir, "41_scroll_b.png"),
                    new Vector3(-5.0f, 1.2f, 1.5f), new Vector3(0f, 0.7f, 0f), 38f, 1600, 900);
                asm.debugSpeed = 0f;
                log.AppendLine($"scroll test: debugSpeed=4 m/s，两张间隔 0.6 s");
            }
        }

        log.AppendLine($"end: tankPos={tank.position.ToString("F2")} speed={(vp != null ? vp.speed : 0f):F2}");
        File.WriteAllText(Path.Combine(dir, "dump.txt"), log.ToString(), Encoding.UTF8);
        Debug.Log($"[DevTankShots] DONE -> {dir}");
        if (combat != null) Object.Destroy(combat.gameObject);
        Object.Destroy(gameObject);
    }

    static void SetRenderersEnabled(Transform t, bool on)
    {
        if (t == null) return;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    /// <summary>用反射把 TrackAssembly 的运行时数据读出来（不改生产代码）。</summary>
    static string Dump(Transform tank)
    {
        var sbh = new StringBuilder();
        {
            // 贴地高度：车体原点 vs 地形高度，履带/车体的最低点
            float ground = BattlefieldTerrain.SampleHeight(tank.position.x, tank.position.z);
            sbh.AppendLine($"[height] rootY={tank.position.y:F3} terrainY={ground:F3} 离地={tank.position.y - ground:F3}");
            var vp0 = tank.GetComponent<VehiclePhysics>();
            if (vp0 != null) sbh.AppendLine($"[height] groundClearance={vp0.groundClearance:F3} modelSitsOnGround={vp0.modelSitsOnGround}");
            foreach (var name in new[] { "Tracks", "Model" })
            {
                var t0 = tank.Find(name);
                if (t0 == null) continue;
                Bounds b = default; bool any = false;
                foreach (var r in t0.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any) sbh.AppendLine($"[height] {name} 世界最低点={b.min.y:F3}（相对车体原点 {b.min.y - tank.position.y:F3}），最高={b.max.y:F3}");
            }
        }
        return sbh.ToString() + DumpTrackAssembly(tank);
    }

    static string DumpTrackAssembly(Transform tank)
    {
        var sb = new StringBuilder();
        var asm = tank.GetComponentInChildren<TrackAssembly>(true);
        if (asm == null) { sb.AppendLine("[dump] 找不到 TrackAssembly"); return sb.ToString(); }

        var t = asm.GetType();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var fW = t.GetField("_wheels", flags);
        var fB = t.GetField("_belts", flags);
        sb.AppendLine($"[dump] TrackAssembly on {asm.gameObject.name}  useGpu={asm.useGpuShader} trim={asm.trimDebris} rebuild={asm.rebuildEndWheels}");
        sb.AppendLine($"[dump] halfTrack={(float)t.GetField("_halfTrack", flags).GetValue(asm):F3}");

        var wheels = fW.GetValue(asm) as System.Collections.IList;
        sb.AppendLine($"[dump] wheels={wheels.Count}");
        foreach (var w in wheels)
        {
            var wt = w.GetType();
            var tf = (Transform)wt.GetField("t").GetValue(w);
            float rad = (float)wt.GetField("radius").GetValue(w);
            float side = (float)wt.GetField("side").GetValue(w);
            float lz = (float)wt.GetField("localZ").GetValue(w);
            var lc = (Vector3)wt.GetField("localCenter").GetValue(w);
            var mf = tf.GetComponent<MeshFilter>();
            sb.AppendLine($"   wheel {tf.name,-9} side={side,2:F0} r={rad:F3} localZ={lz:F2} center=({lc.x:F2},{lc.y:F2},{lc.z:F2}) mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "-")} v={(mf && mf.sharedMesh ? mf.sharedMesh.vertexCount : 0)}");
        }

        var belts = fB.GetValue(asm) as System.Collections.IList;
        sb.AppendLine($"[dump] belts={belts.Count}");
        foreach (var b in belts)
        {
            var bt = b.GetType();
            var tf = (Transform)bt.GetField("tf").GetValue(b);
            float len = (float)bt.GetField("length").GetValue(b);
            float unit = (float)bt.GetField("unit").GetValue(b);
            float side = (float)bt.GetField("side").GetValue(b);
            bool gpu = (bool)bt.GetField("gpu").GetValue(b);
            var anim = (bool[])bt.GetField("anim").GetValue(b);
            var s = (float[])bt.GetField("s").GetValue(b);
            int nAnim = 0; foreach (var a in anim) if (a) nAnim++;
            var mesh = (Mesh)bt.GetField("mesh").GetValue(b);
            var cyz = (Vector2[])bt.GetField("cyz").GetValue(b);
            float ymin = 1e9f, ymax = -1e9f, zmin = 1e9f, zmax = -1e9f;
            foreach (var p in cyz) { if (p.x < ymin) ymin = p.x; if (p.x > ymax) ymax = p.x; if (p.y < zmin) zmin = p.y; if (p.y > zmax) zmax = p.y; }
            sb.AppendLine($"   belt {tf.name,-8} side={side,2:F0} gpu={gpu} v={s.Length} anim={nAnim} loopLen={len * unit:F2}m " +
                          $"centerline(local) y {ymin:F3}..{ymax:F3} z {zmin:F3}..{zmax:F3} -> (m) y {ymin * unit:F2}..{ymax * unit:F2} z {zmin * unit:F2}..{zmax * unit:F2}");
            sb.AppendLine($"        mesh={mesh.name} v={mesh.vertexCount} sub={mesh.subMeshCount} boundsSize(m)=({mesh.bounds.size.x * unit:F2},{mesh.bounds.size.y * unit:F2},{mesh.bounds.size.z * unit:F2})");
            var pct = (float[])bt.GetField("dbgPct").GetValue(b);
            if (pct != null)
                sb.AppendLine($"        偏移分位(m) p50={pct[0] * unit:F3} p75={pct[1] * unit:F3} p90={pct[2] * unit:F3} p97={pct[3] * unit:F3} p99={pct[4] * unit:F3}  band={((float)bt.GetField("dbgBand").GetValue(b)) * unit:F3}");
            var hist = (int[])bt.GetField("dbgHist").GetValue(b);
            if (hist != null)
            {
                var h = new StringBuilder("        偏移直方图(2cm/格): ");
                for (int k = 0; k < hist.Length; k++)
                    if (hist[k] > 0) h.Append($"{k * 2}cm:{hist[k]} ");
                sb.AppendLine(h.ToString());
            }
        }
        return sb.ToString();
    }

    IEnumerator Shot(Camera cam, Transform tank, string path,
                    Vector3 localPos, Vector3 localLook, float fov, int w, int h)
    {
        Vector3 p = tank.TransformPoint(localPos);
        Vector3 l = tank.TransformPoint(localLook);
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
