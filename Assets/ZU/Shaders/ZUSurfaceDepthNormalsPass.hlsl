#ifndef ZU_SURFACE_DEPTH_NORMALS_PASS_INCLUDED
#define ZU_SURFACE_DEPTH_NORMALS_PASS_INCLUDED
// URP 17.6 LitDepthNormalsPass.hlsl writing the ZU normal: the bevelled + normal-mapped normal the forward pass lights
// with, so SSAO (and SSR) see the painted edges instead of the flat box. Parallax / detail are gone.

#include "ZUSurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_ALPHATEST_ON) || defined(_NORMALMAP) || defined(_WRITE_SMOOTHNESS)
#define REQUIRES_UV_INTERPOLATOR
#endif

struct Attributes
{
    float4 positionOS   : POSITION;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float3 normal       : NORMAL;
    float4 boxLocal     : TEXCOORD2;    // ZU
    float4 boxHalf      : TEXCOORD3;    // ZU
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS  : SV_POSITION;
    #if defined(REQUIRES_UV_INTERPOLATOR)
    float2 uv          : TEXCOORD1;
    #endif
    half3 normalWS     : TEXCOORD2;

    #if defined(_NORMALMAP)
    half4 tangentWS    : TEXCOORD4;    // xyz: tangent, w: sign
    #endif

    float4 boxLocal    : TEXCOORD6;    // ZU
    float4 boxHalf     : TEXCOORD7;    // ZU

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};


Varyings ZUSurfaceDepthNormalsVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    #if defined(REQUIRES_UV_INTERPOLATOR)
        output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    #endif
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normal, input.tangentOS);

    output.normalWS = half3(normalInput.normalWS);
    #if defined(_NORMALMAP)
        float sign = input.tangentOS.w * float(GetOddNegativeScale());
        output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
    #endif

    output.boxLocal = input.boxLocal;
    output.boxHalf = input.boxHalf;

    return output;
}

void ZUSurfaceDepthNormalsFragment(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    #if defined(_ALPHATEST_ON) || defined(_WRITE_SMOOTHNESS)
        float alpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a;
    #endif

    #if defined(_ALPHATEST_ON)
        Alpha(alpha, _BaseColor, _Cutoff);
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    #if _SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF_KEYWORD_DECLARED
        if (_SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF)
            discard;
    #endif

    // ZU: the same normal the forward pass lights with (Lit skips the normal map in the octahedral branch; we don't,
    // the painted edges are the point of this pass)
    #if defined(_NORMALMAP)
        half3 normalTS = SampleNormal(input.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
        half4 tangentWS = input.tangentWS;
    #else
        half3 normalTS = half3(0.0, 0.0, 1.0);
        half4 tangentWS = half4(1, 0, 0, 1);
    #endif
    float edgeT;
    half3x3 tangentToWorld;
    float3 normalWS = ZUSurfaceNormalWS(float3(input.normalWS), tangentWS, normalTS, input.boxLocal, input.boxHalf, edgeT, tangentToWorld);

    #if defined(_GBUFFER_NORMALS_OCT)
        normalWS = normalize(normalWS);
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
        half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
        outNormalWS = half4(packedNormalWS, 0.0);
    #else
        outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
    #endif

    #if defined(_WRITE_SMOOTHNESS) && !defined(_SCREENSPACEREFLECTIONS_OFF)
        // the roughness floor applies here too, so SSR sees the smoothness the forward pass shades with
        outNormalWS.a = min(SampleMetallicSpecGloss(input.uv, alpha).a, half(1.0) - _RoughMin);
    #endif

    #ifdef _WRITE_RENDERING_LAYERS
        outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
