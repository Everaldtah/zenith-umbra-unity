// The team rim light on every hero (the TS CharacterView rimChunk: emissive += rimColor * pow(1 - |N.V|, 2.5) * strength),
// drawn as an extra additive pass over the body (an extra material slot re-renders the mesh), plus a flat _Fill for the
// ghost looks. Colour = _RimColor x (rim x _Rim + _Fill). Per-renderer values come in a MaterialPropertyBlock.
Shader "ZU/HeroRim"
{
    Properties
    {
        [HDR] _RimColor("Rim colour", Color) = (1, 1, 1, 1)
        _Rim("Rim strength", Float) = 0.5
        _Fill("Fill", Float) = 0
        _Power("Rim power", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HeroRim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _Rim, _Fill, _Power;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS), V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(abs(dot(N, V))), _Power);
                return half4(_RimColor.rgb * (rim * _Rim + _Fill), 1);
            }
            ENDHLSL
        }
    }
}
