// ZU/Harbour: port of MapScene.ts's harbour water - the PC game's painted, gently rolling sea (Hanabi Harbor). Unlit and
// opaque like the TS ShaderMaterial: two drifting value-noise layers between the deep and shallow blues, the map's tint
// rolling across it in long bands, white streaks on the crests; the map's linear fog on top (TS fog: true). Driven by
// world position, so the plane's size and UVs don't matter.
Shader "ZU/Harbour"
{
    Properties
    {
        _Deep("Deep", Color) = (0.1137, 0.2471, 0.4510, 1)        // #1d3f73
        _Shallow("Shallow", Color) = (0.2471, 0.5255, 0.7216, 1)  // #3f86b8
        _Glow("Glow (the map's tint)", Color) = (1, 0.6039, 0.2353, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex HarbourVertex
            #pragma fragment HarbourFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep;
            half4 _Shallow;
            half4 _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            // the TS hash and value noise, as written there
            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + float2(1, 1)), f.x), f.y);
            }

            Varyings HarbourVertex(Attributes input)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.fogFactor = ComputeFogFactor(v.positionCS.z);
                return o;
            }

            half4 HarbourFragment(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 w3 = i.positionWS;
                float2 p = w3.xz * 0.12;
                float w = N(p + float2(t * 0.25, t * 0.12)) * 0.6 + N(p * 2.3 - float2(t * 0.3, 0.0)) * 0.4;
                float streak = smoothstep(0.72, 0.8, w);
                float3 c = lerp(_Deep.rgb, _Shallow.rgb, w * 0.8) + _Glow.rgb * 0.12 * sin(w3.x * 0.05 + t) * 0.5 + float3(0.9, 0.95, 1.0) * streak * 0.35;
                c = MixFog(c, InitializeInputDataFog(float4(w3, 1.0), i.fogFactor));
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
