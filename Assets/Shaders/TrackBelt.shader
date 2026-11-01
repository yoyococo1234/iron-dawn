// 履带板：顶点沿「履带环中心线」前进（不是滚 UV）。
// CPU 每帧只改一个 _Scroll，位移全在顶点着色器里算。
//
// 数据来源：
//   · 顶点 UV2.x = 该顶点的弧长参数（0~1，绕一圈）
//   · 顶点 UV2.y = 1 表示这个顶点跟着履带走，0 表示不动（粘进来的碎片）
//   · _PathArr  = 中心线采样数组：xyz = (0, 中心线 y, 中心线 z)，w = 该处切线角（弧度）
//   位置 = 中心线(s + scroll) + 把「相对中心线的偏移」按切线角差旋转
//
// 2026-11-01 实测：Unity 6.3 Built-in 下 surface shader 的顶点阶段，
// material.SetVectorArray 和 material.SetTexture 都能正确读到数据
// （最小复现结果 (0.40,0.60,0.70) 与期望一致）。之前"读出来是 0"是代码问题，不是绑定限制。
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
        #pragma target 3.5

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        float _Scroll;
        float _PathN;
        // 注意：必须是 255 而不是 256。
        // Material.SetVectorArray 的上限是 1023 个 float，256 个 Vector4 = 1024 个 float，
        // 正好超一个 → 整个数组静默设置失败 → 顶点着色器读到未初始化数据 → 履带板甩得满车都是。
        float4 _PathArr[255];

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
            float2 p = v.texcoord1;              // x = 弧长参数(0~1)，y = 是否参与运动
            if (p.y < 0.5) return;               // 不动的顶点原样输出

            float4 c0 = PathAt(p.x);
            float4 c1 = PathAt(p.x + _Scroll);

            float dth = c1.w - c0.w;
            dth = dth - 6.28318530718 * floor(dth / 6.28318530718 + 0.5);   // 最短角差

            float sn, cs;
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
