// Additive glow for the ability views (the TS MeshBasicMaterial / SpriteMaterial with AdditiveBlending): the puppets'
// strings and rising rings, the koi sigil, the masked heroes' eye glow. Colour = texture x vertex colour (optional) x
// _BaseColor (its alpha = the TS opacity) x the per-instance _Tint (the TS instanceColor). Unlit, two-sided, no depth
// write; instancing on.
Shader "ZU/AbilityAdditive"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        [HDR] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Tint("Instance tint (uninstanced draws)", Color) = (1, 1, 1, 1)
        _VertexColors("Use vertex colours", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AbilityAdditive"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _VertexColors;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                half4 vc = _VertexColors > 0.5 ? i.color : half4(1, 1, 1, 1);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                o.color = half4(vc.rgb * _BaseColor.rgb * tint.rgb, _BaseColor.a);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                return half4(t.rgb * i.color.rgb, t.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
