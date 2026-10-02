// Grand Dohyo's holographic chains (TS render/ChainCage.ts VERT / FRAG, ported): a semi-opaque body in the ring's colours
// with a bright rim, scanlines and light running along the links. Drawn instanced (one instance a chain unit); the
// per-instance strength _K (the TS instanceColor.r) fades a unit out, 0 discards it. The stake uses it uninstanced (_K = 1).
// Premultiplied alpha, no depth write, two-sided.
Shader "ZU/ChainHolo"
{
    Properties
    {
        _Map("Link texture", 2D) = "white" {}
        _HasMap("Has texture", Float) = 0
        [HDR] _ColA("Colour A", Color) = (0.184, 0.878, 0.784, 1)
        [HDR] _ColB("Colour B", Color) = (1, 0.824, 0.478, 1)
        _T("Time", Float) = 0
        _K("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+6" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ChainHolo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Map); SAMPLER(sampler_Map);
            CBUFFER_START(UnityPerMaterial)
                float4 _Map_ST;
                float _HasMap, _T;
                float4 _ColA, _ColB;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _K)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 world : TEXCOORD1; float3 view : TEXCOORD2; float2 uv : TEXCOORD3; float k : TEXCOORD4; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.world = wp; o.n = TransformObjectToWorldNormal(i.normalOS); o.view = normalize(_WorldSpaceCameraPos - wp); o.uv = i.uv;
                o.k = UNITY_ACCESS_INSTANCED_PROP(Props, _K);
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.k - 0.004);
                float fres = pow(1.0 - abs(dot(normalize(i.n), normalize(i.view))), 2.2);
                float scan = 0.5 + 0.5 * sin(i.world.y * 46.0 - _T * 6.0);                                   // hologram scanlines
                float run = 0.5 + 0.5 * sin(dot(i.world.xz, float2(1.7, 1.3)) * 1.4 - _T * 5.5);             // light running down the links
                float3 tex = lerp(float3(0.8, 0.8, 0.8), SAMPLE_TEXTURE2D(_Map, sampler_Map, i.uv).rgb, _HasMap);
                float lum = dot(tex, float3(0.299, 0.587, 0.114));
                float3 body = lerp(_ColA.rgb, _ColB.rgb, run * 0.75) * (0.35 + 0.9 * lum);
                float a = (0.5 + 0.16 * scan) * (0.92 + 0.08 * sin(_T * 43.0 + i.world.x * 3.0));            // a little flicker
                float3 rim = lerp(_ColB.rgb, float3(1, 1, 1), 0.55) * fres * 1.25;
                return half4((body * a + rim) * i.k, a * i.k);                                                // premultiplied
            }
            ENDHLSL
        }
    }
}
