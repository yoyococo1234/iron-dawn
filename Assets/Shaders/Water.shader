// 战场海面：程序化波浪法线 + 菲涅尔反光 + 按水深变色/透明 + 近岸泡沫
// 不依赖任何贴图，纯算法；需要相机开启 DepthTextureMode.Depth（_CameraDepthTexture）
Shader "铁皮黎明/Water"
{
    Properties
    {
        _ShallowColor ("近岸水色", Color) = (0.16, 0.42, 0.45, 0.72)
        _DeepColor ("深水水色", Color) = (0.02, 0.10, 0.22, 0.95)
        _SkyColor ("天空反光色", Color) = (0.55, 0.70, 0.85, 1)
        _WaveStrength ("波浪强度", Range(0, 1)) = 0.32
        _WaveScale ("波浪密度", Range(0.01, 0.5)) = 0.055
        _WaveSpeed ("波浪速度", Range(0, 3)) = 0.55
        _SpecPower ("高光锐度", Range(4, 400)) = 90
        _SpecIntensity ("高光强度", Range(0, 4)) = 1.3
        _DepthFade ("变色水深(米)", Range(0.5, 60)) = 8
        _FoamWidth ("泡沫水深(米)", Range(0.1, 8)) = 1.4
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
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            fixed4 _SkyColor;
            float _WaveStrength;
            float _WaveScale;
            float _WaveSpeed;
            float _SpecPower;
            float _SpecIntensity;
            float _DepthFade;
            float _FoamWidth;
            sampler2D _CameraDepthTexture;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            // 4 个不同方向的定向波叠加：干涉出自然涟漪，不会像 x/y 独立余弦那样出网格
            float3 WaveNormal (float2 p, float t)
            {
                float s = _WaveScale;
                const float2 d1 = float2( 0.80,  0.60);
                const float2 d2 = float2(-0.40,  0.92);
                const float2 d3 = float2( 0.95, -0.31);
                const float2 d4 = float2(-0.71, -0.70);

                float2 d = float2(0, 0);
                d += d1 * cos(dot(p, d1) * s * 1.0 + t * 1.10) * 0.50;
                d += d2 * cos(dot(p, d2) * s * 1.7 + t * 1.45) * 0.35;
                d += d3 * cos(dot(p, d3) * s * 2.9 + t * 1.90) * 0.25;
                d += d4 * cos(dot(p, d4) * s * 4.7 + t * 2.40) * 0.15;

                d *= _WaveStrength;
                return normalize(float3(-d.x, 1.0, -d.y));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = WaveNormal(i.worldPos.xz, _Time.y * _WaveSpeed);

                // 水有多深：场景深度 - 水面深度
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(tex2D(_CameraDepthTexture, uv).r);
                float depth = max(0.0, sceneZ - i.screenPos.w);

                float deepT = saturate(depth / _DepthFade);
                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, deepT);

                // 菲涅尔：视线越平越像镜子（反天空）
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                waterCol = lerp(waterCol, _SkyColor.rgb, fres * 0.75);

                // 太阳高光
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _SpecPower) * _SpecIntensity;
                waterCol += _LightColor0.rgb * spec;

                // 近岸泡沫 + 浅水渐显（alpha 随水深从 0 涨到不透明，岸线才不是一条硬边）
                float foam = saturate(1.0 - depth / _FoamWidth);
                foam *= foam;
                waterCol = lerp(waterCol, float3(0.82, 0.86, 0.88), foam * 0.55);

                float edgeFade = saturate(depth / 1.2);
                float alpha = lerp(_ShallowColor.a * 0.55, _DeepColor.a, deepT) * edgeFade;
                alpha = saturate(alpha + foam * 0.30);

                return fixed4(waterCol, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
