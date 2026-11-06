// 港口海面（平湖版）：平静水面 + 阳光反射带 + 平面反射（把港口/城市映进水里）
// 不再做顶点位移和浪顶白沫 —— 用户要求"风平浪静"，白纹太难看。
// 反射贴图由 PlanarReflection.cs 提供（镜像相机渲染）。
Shader "铁皮黎明/Water"
{
    Properties
    {
        _DeepColor ("深水色", Color) = (0.035, 0.10, 0.20, 1)
        _HorizonColor ("远处水色", Color) = (0.16, 0.28, 0.40, 1)
        _SunColor ("阳光色", Color) = (1.0, 0.95, 0.82, 1)

        _RippleStrength ("水面细纹", Range(0, 1)) = 0.12
        _RippleScale ("细纹密度", Range(0.01, 2)) = 0.9
        _RippleSpeed ("细纹速度", Range(0, 3)) = 0.35

        _Reflectivity ("反射强度", Range(0, 1)) = 0.85
        _SunPath ("阳光反射带强度", Range(0, 6)) = 2.2
        _SunSharp ("阳光锐度", Range(8, 4000)) = 260
        _SunWidth ("阳光带宽度", Range(0.05, 1)) = 0.35

        _ReflectionTex ("反射贴图", 2D) = "black" {}
        _Distortion ("反射扰动", Range(0, 0.05)) = 0.012
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 200

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _DeepColor, _HorizonColor, _SunColor;
            float _RippleStrength, _RippleScale, _RippleSpeed;
            float _Reflectivity, _SunPath, _SunSharp, _SunWidth, _Distortion;
            sampler2D _ReflectionTex;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = wp;
                o.pos = UnityWorldToClipPos(wp);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            // 很轻的细纹：只为了让反射和阳光带不呆板，不做起伏
            float2 RippleNormal (float2 p, float t)
            {
                float s = _RippleScale;
                float2 d = float2(0, 0);
                d += float2( 0.80,  0.60) * cos(dot(p, float2( 0.80,  0.60)) * s * 1.0 + t * 1.00) * 0.50;
                d += float2(-0.40,  0.92) * cos(dot(p, float2(-0.40,  0.92)) * s * 2.3 + t * 1.45) * 0.30;
                d += float2( 0.95, -0.31) * cos(dot(p, float2( 0.95, -0.31)) * s * 4.1 + t * 2.10) * 0.18;
                d *= _RippleStrength;
                return d;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float camDist = distance(_WorldSpaceCameraPos, i.worldPos);

                // 远处细纹淡出（否则出摩尔纹）
                float rf = saturate(1.0 - camDist / 300.0);
                float2 g = RippleNormal(i.worldPos.xz, _Time.y * _RippleSpeed) * rf;
                float3 N = normalize(float3(-g.x, 1.0, -g.y));

                // ---------- 底色：近处深、远处偏亮（大气透视） ----------
                float horizonT = saturate(camDist / 900.0);
                float3 waterCol = lerp(_DeepColor.rgb, _HorizonColor.rgb, horizonT * 0.85);

                // ---------- 反射：暂时不做平面反射 ----------
                // 平面反射的镜像相机矩阵没算对，转视角时倒影会跟着转（用户实测反馈），
                // 先用"天空色 + 菲涅尔"顶上，看起来是平静深水，不会有错误倒影。
                float3 refl = _HorizonColor.rgb * 1.15 + _SunColor.rgb * 0.05;
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                fres = saturate(fres * _Reflectivity * 0.55);
                float3 col = lerp(waterCol, refl, fres);

                // ---------- 阳光反射带 ----------
                // 真实海面：太阳的倒影是一条**窄而长**的亮带，不是一大片白。
                // 之前指数太低（pow 12），一看向太阳整个水面都糊成白的 —— 收紧指数。
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 R = reflect(-V, N);
                float cosSun = saturate(dot(R, L));

                float3 Rflat = normalize(float3(R.x, 0.0, R.z) + 1e-5);
                float3 Lflat = normalize(float3(L.x, 0.0, L.z) + 1e-5);
                float horiz = saturate(dot(Rflat, Lflat));          // 方位对齐度

                float sunCore = pow(cosSun, 2600.0) * 2.5;                       // 核心亮点（很紧）
                float sunBand = pow(horiz, 26.0) * pow(cosSun, 260.0) * _SunPath; // 长带（窄）
                float glitter = pow(cosSun, 900.0) * 0.18;                        // 一点点碎光，别做成斑块

                float3 sunAdd = _SunColor.rgb * _LightColor0.rgb * (sunCore + sunBand + glitter);
                // 上限保护：再亮也不超过"纯白"，避免整片水面糊成一块
                sunAdd = min(sunAdd, float3(1.6, 1.6, 1.6));
                col += sunAdd;

                // ---------- 透明度：整体接近不透明，只在极近处稍透 ----------
                float alpha = saturate(0.94 + fres * 0.06);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
