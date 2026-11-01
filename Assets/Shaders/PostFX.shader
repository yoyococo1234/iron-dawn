// 铁皮黎明 · 后处理：亮部泛光 + 电影色调映射 + 对比度/饱和度 + 暗角
// 用在 Built-in 管线的相机上（PostFX.cs 的 OnRenderImage 驱动）。
// 目标：把「Unity 默认直出」那种平、灰、没有重量感的画面，压成有对比、有暖调、边缘略暗的战场感。
Shader "铁皮黎明/PostFX"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;

    float _Threshold;
    float _SoftKnee;
    float _BloomIntensity;
    float _Saturation;
    float _Contrast;
    float _Vignette;
    float _Exposure;
    float _Lift;
    float4 _Tint;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert (appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    // ---- Pass 0：提取亮部（带软膝，避免生硬边界）----
    fixed4 fragBright (v2f i) : SV_Target
    {
        float3 c = tex2D(_MainTex, i.uv).rgb;
        float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
        float knee = max(0.0001, _SoftKnee);
        float soft = clamp(lum - _Threshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee);
        float contrib = max(soft, lum - _Threshold) / max(lum, 0.0001);
        return fixed4(c * contrib, 1.0);
    }

    // ---- Pass 1/2：可分离高斯模糊（9 tap）----
    static const float Weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };

    fixed4 fragBlurH (v2f i) : SV_Target
    {
        float2 texel = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv).rgb * Weights[0];
        [unroll] for (int k = 1; k < 5; k++)
        {
            c += tex2D(_MainTex, i.uv + float2(texel.x * k * 1.6, 0)).rgb * Weights[k];
            c += tex2D(_MainTex, i.uv - float2(texel.x * k * 1.6, 0)).rgb * Weights[k];
        }
        return fixed4(c, 1.0);
    }

    fixed4 fragBlurV (v2f i) : SV_Target
    {
        float2 texel = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv).rgb * Weights[0];
        [unroll] for (int k = 1; k < 5; k++)
        {
            c += tex2D(_MainTex, i.uv + float2(0, texel.y * k * 1.6)).rgb * Weights[k];
            c += tex2D(_MainTex, i.uv - float2(0, texel.y * k * 1.6)).rgb * Weights[k];
        }
        return fixed4(c, 1.0);
    }

    // ---- Pass 3：合成（泛光 + 曝光 + 色调映射 + 对比/饱和 + 暗角 + 轻微暖调）----
    float3 ACESFilmic (float3 x)
    {
        // Narkowicz 2015 近似，便宜且足够"像电影"
        const float a = 2.51; const float b = 0.03;
        const float c = 2.43; const float d = 0.59; const float e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    fixed4 fragComposite (v2f i) : SV_Target
    {
        float3 col = tex2D(_MainTex, i.uv).rgb;
        float3 bloom = tex2D(_BloomTex, i.uv).rgb;

        col += bloom * _BloomIntensity;
        col *= _Exposure;

        // 色调映射
        col = ACESFilmic(col);

        // 对比度（绕 0.5 灰点）
        col = (col - 0.5) * _Contrast + 0.5;

        // 饱和度
        float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
        col = lerp(lum.xxx, col, _Saturation);

        // 暖调 + 抬黑（lift 让暗部不死黑，像胶片）
        col = lerp(col, col * _Tint.rgb, 0.55);
        col = col * (1.0 - _Lift) + _Lift;

        // 暗角
        float2 d = i.uv - 0.5;
        float vig = 1.0 - dot(d, d) * _Vignette * 2.4;
        col *= saturate(vig);

        return fixed4(saturate(col), 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBright
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlurH
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlurV
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }
    }
    FallBack Off
}
