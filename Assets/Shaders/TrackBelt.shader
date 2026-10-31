// 履带板：顶点沿「履带环中心线」前进（不是滚 UV）。
// CPU 每帧只改一个 _Scroll，位移全在顶点着色器里算。
//
// 数据来源：
//   · 顶点 UV2.x = 该顶点的弧长参数（0~1，绕一圈）
//   · _PathArr  = 中心线采样数组：xyz = (0, 中心线 y, 中心线 z)，w = 该处切线角（弧度）
//   位置 = 中心线(s + scroll) + 把「相对中心线的偏移」按切线角差旋转
Shader "Tank/TrackBelt"
{
    Properties
    {
        _Color ("Color", Color) = (0.46,0.46,0.44,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.18
        _Metallic ("Metallic", Range(0,1)) = 0.55
        _Scroll ("Scroll", Float) = 0
        _PathN ("Path Count", Float) = 256
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        float _Scroll;
        float _PathN;
        float4 _PathArr[256];

        struct Input
        {
            float2 uv_MainTex;
        };

        // 在中心线数组里按 0~1 的弧长参数取值（线性插值，角度走最短角差）
        float4 PathAt(float t)
        {
            float u = frac(t) * _PathN;
            float i0 = floor(u);
            float f = u - i0;
            int a = (int)i0;
            int b = a + 1;
            if (b >= (int)_PathN) b = 0;
            float4 pa = _PathArr[a];
            float4 pb = _PathArr[b];
            float d = pb.w - pa.w;
            d = d - 6.28318530718 * floor(d / 6.28318530718 + 0.5);   // 最短角差
            return float4(0.0, lerp(pa.y, pb.y, f), lerp(pa.z, pb.z, f), pa.w + d * f);
        }

        void vert(inout appdata_full v)
        {
            float s = v.texcoord1.x;              // 0~1 弧长参数
            float4 c0 = PathAt(s);
            float4 c1 = PathAt(s + _Scroll);

            float dth = c1.w - c0.w;
            dth = dth - 6.28318530718 * floor(dth / 6.28318530718 + 0.5);   // 最短角差

            float sn, cs;
            sincos(dth, sn, cs);

            // DEBUG5: uv2 与数组绑定检查（验证完删掉）
            v.vertex.y += v.texcoord1.x * 1.0;
            v.vertex.x += _PathArr[3].y * 30.0;
            sincos(dth, sn, cs);

            // 网格局部：X = 车宽，Y = 车长，Z = 高；履带环在 YZ 平面
            float oy = v.vertex.y - c0.y;
            float oz = v.vertex.z - c0.z;
            v.vertex.y = c1.y + oy * cs - oz * sn;
            v.vertex.z = c1.z + oy * sn + oz * cs;

            float ny = v.normal.y, nz = v.normal.z;
            v.normal.y = ny * cs - nz * sn;
            v.normal.z = ny * sn + nz * cs;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
