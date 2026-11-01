using UnityEngine;

/// <summary>
/// 画面质感后处理（Built-in 管线，挂在 Main Camera 上）。
///
/// 做四件事：
///   1. 亮部泛光 —— 阳光/爆炸/炮口焰会有光晕，而不是一块死白
///   2. 电影色调映射（ACES 近似）—— 高光滚降，不再一过曝就糊成白片
///   3. 对比度 / 饱和度 / 轻微暖调 / 抬黑 —— 去掉 Unity 默认直出那种"平、灰、塑料"
///   4. 暗角 —— 视线收拢到画面中心
///
/// 全部参数可在 Inspector 里实时调；enabled = false 就是原样输出，方便 A/B。
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class PostFX : MonoBehaviour
{
    [Header("开关")]
    [Tooltip("关掉就是 Unity 默认直出，用来 A/B 对比")]
    public bool enablePostFX = true;

    [Header("泛光")]
    [Tooltip("亮度阈值：高于它的部分才发光（HDR 下 1.0 = 纯白）")]
    public float threshold = 1.05f;
    [Tooltip("软膝：阈值附近的过渡宽度，避免生硬边界")]
    public float softKnee = 0.5f;
    [Range(0f, 2f)] public float bloomIntensity = 0.42f;
    [Range(1, 4)] public int blurIterations = 2;

    [Header("色调")]
    [Range(0.5f, 2f)] public float exposure = 1.06f;
    [Range(0.5f, 2f)] public float contrast = 1.07f;
    [Range(0f, 2f)] public float saturation = 1.12f;
    [Tooltip("暗部抬升：让阴影不死黑，像胶片")]
    [Range(0f, 0.15f)] public float lift = 0.018f;
    [Tooltip("整体色调偏移（默认略暖）")]
    public Color tint = new Color(1.04f, 1.0f, 0.955f, 1f);

    [Header("暗角")]
    [Range(0f, 1f)] public float vignette = 0.28f;

    Material _mat;
    Shader _shader;

    void OnEnable()
    {
        _shader = Shader.Find("铁皮黎明/PostFX");
        if (_shader == null)
        {
            Debug.LogWarning("[PostFX] 找不到 Shader 铁皮黎明/PostFX，后处理关闭");
            enablePostFX = false;
            return;
        }
        if (_mat == null) _mat = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnDisable()
    {
        if (_mat != null)
        {
            if (Application.isPlaying) Destroy(_mat);
            else DestroyImmediate(_mat);
            _mat = null;
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!enablePostFX || _mat == null)
        {
            Graphics.Blit(src, dst);
            return;
        }

        _mat.SetFloat("_Threshold", threshold);
        _mat.SetFloat("_SoftKnee", softKnee);
        _mat.SetFloat("_BloomIntensity", bloomIntensity);
        _mat.SetFloat("_Exposure", exposure);
        _mat.SetFloat("_Contrast", contrast);
        _mat.SetFloat("_Saturation", saturation);
        _mat.SetFloat("_Lift", lift);
        _mat.SetFloat("_Vignette", vignette);
        _mat.SetColor("_Tint", tint);

        int w = Mathf.Max(1, src.width / 2);
        int h = Mathf.Max(1, src.height / 2);

        // 亮部
        var bright = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
        bright.filterMode = FilterMode.Bilinear;
        Graphics.Blit(src, bright, _mat, 0);

        // 可分离模糊（来回两遍，得到更宽的光晕）
        var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
        tmp.filterMode = FilterMode.Bilinear;
        RenderTexture a = bright, b = tmp;
        int iters = Mathf.Clamp(blurIterations, 1, 4);
        for (int i = 0; i < iters; i++)
        {
            Graphics.Blit(a, b, _mat, 1);   // 横向
            Graphics.Blit(b, a, _mat, 2);   // 纵向
        }

        // 合成
        _mat.SetTexture("_BloomTex", a);
        Graphics.Blit(src, dst, _mat, 3);

        RenderTexture.ReleaseTemporary(bright);
        RenderTexture.ReleaseTemporary(tmp);
    }
}
