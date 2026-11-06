using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// 海面观感对比截图（2026-11-06 建）：固定机位拍几张海面，用来对比换波形前后的差别。
/// 用法：DevSeaShots.Begin("gerstner") / DevSeaShots.Begin("fft")
/// 输出：D:/UnityProjects/Tank3D/work/shots/sea_&lt;tag&gt;/*.png
/// </summary>
public class DevSeaShots : MonoBehaviour
{
    public static void Begin(string tag)
    {
        var go = new GameObject("DevSeaShots");
        var r = go.AddComponent<DevSeaShots>();
        r.StartCoroutine(r.Run(tag));
    }

    /// <summary>围着一条船拍，用来看船的倒影（平面反射）。</summary>
    public static void BeginAroundBoat(string tag)
    {
        var go = new GameObject("DevSeaShots");
        var r = go.AddComponent<DevSeaShots>();
        r.StartCoroutine(r.RunAroundBoat(tag));
    }

    IEnumerator RunAroundBoat(string tag)
    {
        var dir = Path.Combine("D:/UnityProjects/Tank3D/work/shots", "sea_" + tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        yield return null;
        var gs = Object.FindFirstObjectByType<GameStartScreen>();
        if (gs != null) Object.Destroy(gs);
        var garage = Object.FindFirstObjectByType<GarageScreen>();
        if (garage != null) Object.Destroy(garage);
        Time.timeScale = 1f;
        for (int i = 0; i < 10; i++) yield return null;

        Transform boat = null;
        foreach (var vc in Object.FindObjectsByType<VehicleCombat>(FindObjectsSortMode.None))
            if (vc.Def != null && vc.Def.domain == Domain.Sea) { boat = vc.transform; break; }
        if (boat == null) { Debug.LogError("[DevSeaShots] 没有船"); yield break; }

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[DevSeaShots] 没有主相机"); yield break; }
        var cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.enabled = false;

        var poses = new (string name, Vector3 p, Vector3 look, float fov)[]
        {
            ("01_behind", new Vector3(0f, 3.0f, -26f), new Vector3(0f, 1.0f, 0f), 45f),
            ("02_side",   new Vector3(24f, 2.6f, -8f), new Vector3(0f, 0.8f, 0f), 45f),
            ("03_high",   new Vector3(0f, 12f, -30f),  new Vector3(0f, 0f, 0f),   50f),
        };

        foreach (var q in poses)
        {
            Vector3 p = boat.TransformPoint(q.p);
            Vector3 l = boat.TransformPoint(q.look);
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(l - p);
            cam.fieldOfView = q.fov;
            // 等几帧：Crest 的 LOD 跟着相机走，机位一换必须等它重建，否则拍到的是旧 LOD
            for (int i = 0; i < 12; i++) yield return null;
            yield return new WaitForEndOfFrame();
            Capture(cam, Path.Combine(dir, q.name + ".png"), 1600, 900);
        }

        Debug.Log("[DevSeaShots] DONE -> " + dir);
        Object.Destroy(gameObject);
    }

    IEnumerator Run(string tag)
    {
        var dir = Path.Combine("D:/UnityProjects/Tank3D/work/shots", "sea_" + tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        yield return null;
        var gs = Object.FindFirstObjectByType<GameStartScreen>();
        if (gs != null) Object.Destroy(gs);
        var garage = Object.FindFirstObjectByType<GarageScreen>();
        if (garage != null) Object.Destroy(garage);
        Time.timeScale = 1f;
        yield return null;

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[DevSeaShots] 没有主相机"); yield break; }
        var cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.enabled = false;

        // 开阔海域：远离岸边，看纯海面
        var poses = new (string name, Vector3 p, Vector3 look, float fov)[]
        {
            ("01_horizon", new Vector3(-900f, 2.5f, 300f),  new Vector3(-900f, 2.0f, 400f),  50f),
            ("02_high",    new Vector3(-900f, 35f, 260f),   new Vector3(-900f, 0f, 360f),    55f),
            ("03_low",     new Vector3(-880f, 1.2f, 320f),  new Vector3(-900f, 1.0f, 400f),  45f),
        };

        foreach (var q in poses)
        {
            cam.transform.position = q.p;
            cam.transform.rotation = Quaternion.LookRotation(q.look - q.p);
            cam.fieldOfView = q.fov;
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture(cam, Path.Combine(dir, q.name + ".png"), 1600, 900);
            // 机位跳过去以后浪要跑几帧才稳定，再拍一张覆盖
            for (int i = 0; i < 20; i++) yield return null;
            Capture(cam, Path.Combine(dir, q.name + ".png"), 1600, 900);
        }

        Debug.Log("[DevSeaShots] DONE -> " + dir);
        Object.Destroy(gameObject);
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
