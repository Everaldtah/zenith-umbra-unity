// The lit surface the ability views share where the TS used a MeshStandardMaterial on an InstancedMesh: Kaien's paper
// seals, Hex's puppets, the eyelids. Albedo = texture x vertex colour (optional) x _BaseColor x the per-instance _Tint
// (the TS instanceColor, which multiplies the diffuse colour only); emission = _EmissionColor, times the albedo texture when
// _EmitFromBase is 1 (the lids' emissiveMap = map). Lighting: the main light with its shadows plus the ambient probe, a
// soft roughness-driven highlight; two-sided (back faces flip their normal). _OffsetFactor / _OffsetUnits = the TS
// polygonOffset (the lids sit on the face without z-fighting). Instancing on; casts shadows.
Shader "ZU/AbilityLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Tint("Instance tint (uninstanced draws)", Color) = (1, 1, 1, 1)
        _VertexColors("Use vertex colours", Float) = 0
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        _EmitFromBase("Emission x albedo", Float) = 0
        _Roughness("Roughness", Range(0, 1)) = 0.7
        _Metalness("Metalness", Range(0, 1)) = 0
        _OffsetFactor("Offset factor", Float) = 0
        _OffsetUnits("Offset units", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor, _EmissionColor;
            float _VertexColors, _EmitFromBase, _Roughness, _Metalness, _OffsetFactor, _OffsetUnits, _Cull;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)
        ENDHLSL

        Pass
        {
            Name "AbilityLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            Offset [_OffsetFactor], [_OffsetUnits]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 world : TEXCOORD2;
                half4 color : COLOR; float fog : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.world = wp; o.n = TransformObjectToWorldNormal(i.normalOS); o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                half4 vc = _VertexColors > 0.5 ? i.color : half4(1, 1, 1, 1);
                o.color = vc * _BaseColor * UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                o.positionCS = TransformWorldToHClip(wp);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 albedo = tex.rgb * i.color.rgb;
                float3 n = normalize(i.n) * (front ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.world));
                float ndl = saturate(dot(n, L.direction));
                float3 lit = L.color * (ndl * L.shadowAttenuation * L.distanceAttenuation);
                float3 diffuse = albedo * (1 - _Metalness) * (lit + SampleSH(n));
                // a soft highlight: wider and dimmer the rougher the surface
                float3 h = normalize(L.direction + v);
                float gloss = exp2(10 * (1 - _Roughness) + 1);
                float3 spec = lerp(float3(0.04, 0.04, 0.04), albedo, _Metalness) * pow(saturate(dot(n, h)), gloss) * (gloss + 2) / 8 * lit;
                float3 emit = _EmissionColor.rgb * lerp(float3(1, 1, 1), tex.rgb, _EmitFromBase);
                float3 c = diffuse + spec + emit;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - wp);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
