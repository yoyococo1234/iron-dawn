using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成开局封面 / 车库的**背景图**（用真实战场渲染，不是渐变）。
///
/// 背景是玩家看到的第一眼，纯渐变太素。这里直接在 Map_Battlefield 里摆临时相机，
/// 渲成 1920×1080 PNG 存进 Resources，运行时按 ScaleAndCrop 铺满。
///
/// 用法：菜单 `Tools → 铁皮黎明 → 生成界面背景`，或调 <see cref="GenerateAll"/>。
/// 候选机位调 <see cref="RenderCandidates"/>（渲到 work/backdrops 供挑选）。
/// </summary>
public static class UiBackdropGen
{
    const string OutDir = "Assets/Resources/UiBackdrops";
    const string CandDir = "D:/UnityProjects/Tank3D/work/backdrops";

    /// <summary>正式出图：封面 + 车库（都用暮色光，和 UI 的深蓝+金配色同调）。</summary>
    [MenuItem("Tools/铁皮黎明/生成界面背景")]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(CandDir);

        var dusk = Dusk();
        try
        {
            // 封面：城区斜上方 —— 天际线 + 远山剪影 + 暮霭，上半留天空给标题
            Shot("cover", new Vector3(560f, 230f, 340f), new Vector3(0f, 40f, -260f), 46f, 1920, 1080, false);

            // 车库：街头仰视高楼。只渲 480×270，靠双线性插值放大出"虚化景深"感
            Shot("garage", new Vector3(-300f, 70f, 60f), new Vector3(180f, 40f, -430f), 55f, 480, 270, false);
        }
        finally { dusk.Restore(); }

        AssetDatabase.Refresh();
        TuneImporter(OutDir + "/cover.png", 2048);
        TuneImporter(OutDir + "/garage.png", 512);
        AssetDatabase.Refresh();
        Debug.Log("[UiBackdropGen] 完成");
    }

    /// <summary>渲一组候选机位到 work/backdrops，挑完再写进 GenerateAll。</summary>
    [MenuItem("Tools/铁皮黎明/生成界面背景（候选机位）")]
    public static void RenderCandidates()
    {
        Directory.CreateDirectory(CandDir);
        var cams = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("A_sea_low",   new Vector3(0f, 120f, 1150f),    new Vector3(0f, 60f, -100f),  38f),
            ("B_high_side", new Vector3(-820f, 420f, 620f),  new Vector3(0f, 30f, -260f),  42f),
            ("C_city_obl",  new Vector3(620f, 260f, 380f),   new Vector3(0f, 40f, -240f),  45f),
            ("D_skim",      new Vector3(300f, 45f, 900f),    new Vector3(-100f, 90f, -400f), 50f),
            ("E_city_in",   new Vector3(-260f, 60f, 120f),   new Vector3(120f, 30f, -420f), 52f),
            ("F_sea_wide",  new Vector3(0f, 240f, 1500f),    new Vector3(0f, 20f, 0f),     34f),
        };
        foreach (var c in cams) Shot(c.name, c.pos, c.look, c.fov, 960, 540, true);
        Debug.Log("[UiBackdropGen] 候选机位已渲到 " + CandDir);
    }

    /// <summary>暮色候选：同样的机位换成黄昏光 + 雾，挑出最有氛围的几张。</summary>
    [MenuItem("Tools/铁皮黎明/生成界面背景（暮色候选）")]
    public static void RenderDuskCandidates()
    {
        Directory.CreateDirectory(CandDir);
        var cams = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("G_dusk_sea",   new Vector3(0f, 150f, 1250f),   new Vector3(0f, 50f, -120f),  36f),
            ("H_dusk_obl",   new Vector3(560f, 230f, 340f),  new Vector3(0f, 40f, -260f),  46f),
            ("I_dusk_high",  new Vector3(-760f, 380f, 560f), new Vector3(0f, 30f, -280f),  42f),
            ("J_dusk_wide",  new Vector3(0f, 260f, 1600f),   new Vector3(0f, 30f, -60f),   30f),
            ("K_dusk_street",new Vector3(-300f, 70f, 60f),   new Vector3(180f, 40f, -430f), 55f),
            ("L_dusk_low",   new Vector3(-120f, 55f, 700f),  new Vector3(60f, 90f, -350f), 44f),
        };

        var dusk = Dusk();
        try { foreach (var c in cams) Shot(c.name, c.pos, c.look, c.fov, 960, 540, true); }
        finally { dusk.Restore(); }
        Debug.Log("[UiBackdropGen] 暮色候选已渲到 " + CandDir);
    }

    /// <summary>临时把场景调成黄昏（低角度暖阳 + 冷环境光 + 雾），返回一个可还原的句柄。</summary>
    static DuskState Dusk()
    {
        var st = new DuskState();
        st.sun = GameObject.Find("Sun");
        if (st.sun != null)
        {
            st.sunLight = st.sun.GetComponent<Light>();
            if (st.sunLight != null)
            {
                st.sunRot = st.sun.transform.rotation;
                st.sunColor = st.sunLight.color;
                st.sunIntensity = st.sunLight.intensity;
                st.sun.transform.rotation = Quaternion.Euler(9f, -128f, 0f);   // 贴着地平线的暖阳
                st.sunLight.color = new Color(1f, 0.62f, 0.36f);
                st.sunLight.intensity = 1.15f;
            }
        }
        var fill = GameObject.Find("Sun_Fill");
        if (fill != null)
        {
            st.fillLight = fill.GetComponent<Light>();
            if (st.fillLight != null) st.fillIntensity = st.fillLight.intensity;
            if (st.fillLight != null) st.fillLight.intensity = 0.18f;
        }

        st.ambient = RenderSettings.ambientLight;
        st.fog = RenderSettings.fog;
        st.fogColor = RenderSettings.fogColor;
        st.fogMode = RenderSettings.fogMode;
        st.fogDensity = RenderSettings.fogDensity;

        RenderSettings.ambientLight = new Color(0.16f, 0.19f, 0.28f);   // 冷蓝环境光，和 UI 配色一致
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.20f, 0.22f, 0.30f);       // 暮霭，和 UI 深蓝底同调
        RenderSettings.fogDensity = 0.0016f;
        return st;
    }

    class DuskState
    {
        public GameObject sun;
        public Light sunLight, fillLight;
        public Quaternion sunRot;
        public Color sunColor;
        public float sunIntensity, fillIntensity;
        public Color ambient, fogColor;
        public bool fog;
        public FogMode fogMode;
        public float fogDensity;

        public void Restore()
        {
            if (sun != null) sun.transform.rotation = sunRot;
            if (sunLight != null) { sunLight.color = sunColor; sunLight.intensity = sunIntensity; }
            if (fillLight != null) fillLight.intensity = fillIntensity;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
        }
    }

    static void Shot(string name, Vector3 pos, Vector3 look, float fov, int w, int h, bool toCand)
    {
        var go = new GameObject("BackdropCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 8000f;
            cam.clearFlags = CameraClearFlags.Skybox;    // 用场景天空盒，氛围才连得上
            cam.transform.position = pos;
            cam.transform.LookAt(look);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var dir = toCand ? CandDir : OutDir;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log($"[UiBackdropGen] {name} -> {dir}");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void TuneImporter(string assetPath, int maxSize)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        imp.textureType = TextureImporterType.Default;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = maxSize;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.filterMode = FilterMode.Bilinear;     // 车库那张靠低分辨率 + 双线性插值出"虚化"感
        imp.SaveAndReimport();
    }
}
