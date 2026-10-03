// ZU/CloudSea: port of MapScene.ts's cloud sea under the floating maps (Cloudstep) - one plane of drifting fbm cloud
// between the map's fog colour and white (violet on the Rift), fading out with distance from the centre (gone by 450 m).
// Unlit, transparent, no depth write, not fogged - all as the TS ShaderMaterial.
Shader "ZU/CloudSea"
{
    Properties
    {
        _C1("Shade (the map's fog colour)", Color) = (0.839, 0.902, 0.957, 1)
        _C2("Light", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CloudVertex
            #pragma fragment CloudFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _C1;
            half4 _C2;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            // the TS hash, value noise and five-octave fbm, as written there
            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + float2(1, 1)), f.x), f.y);
            }
            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { v += a * N(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            Varyings CloudVertex(Attributes input)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                return o;
            }

            half4 CloudFragment(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.positionWS.xz * 0.018;
                float f = Fbm(p + float2(t * 0.02, t * 0.013));
                float f2 = Fbm(p * 2.3 - float2(t * 0.03, 0.0));
                float d = length(i.positionWS.xz) / 450.0;
                float3 col = lerp(_C1.rgb, _C2.rgb, smoothstep(0.35, 0.8, f * 0.7 + f2 * 0.4));
                return half4(col, (1.0 - smoothstep(0.6, 1.0, d)) * 0.95);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
