// Tenkai-Oh's Solar Bulwark (the TS CharacterView barrier ShaderMaterial): a curved wall of hexes that breathes, its
// edges bright, shading from the hero's glow toward red as it takes damage (_Hp 1 -> 0). Additive, double-sided.
Shader "ZU/Barrier"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1, 0.84, 0.42, 1)
        _Hp("Health 0..1", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Barrier"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Hp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }

            float hex(float2 p)
            {
                p.x *= 1.1547; p.y += fmod(floor(p.x), 2.0) * 0.5;
                p = abs(frac(p) - 0.5);
                return abs(max(p.x * 1.5 + p.y, p.y * 2.0) - 1.0);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * float2(14.0, 8.0);
                float h = smoothstep(0.0, 0.12, hex(p));
                float edge = smoothstep(0.35, 0.5, abs(i.uv.x - 0.5)) + smoothstep(0.85, 1.0, i.uv.y) + smoothstep(0.1, 0.0, i.uv.y);
                float a = (0.18 + (1.0 - h) * 0.55 + edge * 0.5) * (0.6 + 0.4 * sin(_Time.y * 3.0 + i.uv.y * 10.0));
                return half4(lerp(float3(1.0, 0.3, 0.2), _Color.rgb, _Hp) * a, a * 0.9);
            }
            ENDHLSL
        }
    }
}
