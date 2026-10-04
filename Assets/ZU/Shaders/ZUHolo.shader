// The Spar Arena's holographic walls and roof (the TS TrainingScene holoMaterial ShaderMaterial): a blue tint you see the
// world through, a 1 m grid, a scan band sweeping up, bright edges. Normal alpha blending - additive light vanished
// against the Proving Grounds' bright white walls. Double-sided, no depth write. The quad's UVs run 0..1 over the wall;
// _Size is the wall in metres (the grid is 1 m), _On fades it, _HoloTime is the match time (seconds).
Shader "ZU/Holo"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (0.247, 0.663, 1, 1)
        _Size("Size (m)", Vector) = (1, 1, 0, 0)
        _On("On 0..1", Float) = 1
        _HoloTime("Time (s)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Holo"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Size;
                float _On;
                float _HoloTime;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * _Size.xy;
                float2 g = abs(frac(p) - 0.5);
                float grid = smoothstep(0.455, 0.5, max(g.x, g.y));
                float2 e = min(i.uv, 1.0 - i.uv) * _Size.xy;
                float edge = 1.0 - smoothstep(0.0, 0.18, min(e.x, e.y));
                float scan = smoothstep(0.9, 1.0, 1.0 - abs(frac(i.uv.y * 0.5 - _HoloTime * 0.35) - 0.5) * 2.0);
                float shimmer = 0.85 + 0.15 * sin(_HoloTime * 3.0 + p.x * 0.7 + p.y * 1.3);
                float a = clamp((0.16 + grid * 0.42 + edge * 0.7 + scan * 0.25) * shimmer, 0.0, 0.92) * _On;
                float3 col = lerp(_Color.rgb * 0.85, float3(0.85, 0.95, 1.0), grid * 0.35 + edge * 0.5 + scan * 0.3);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
