// ZU/Hero's DepthOnly and DepthNormals passes: URP's own, fed a smeared position so the depth / normals textures (SSAO,
// soft particles) match what the forward pass draws. The shadow caster is URP's unchanged: the TS smears the colour pass
// only (three draws its shadow maps with the plain depth material).
#ifndef ZU_HERO_DEPTH_PASS_INCLUDED
#define ZU_HERO_DEPTH_PASS_INCLUDED

#if defined(ZU_DEPTH_NORMALS)
    #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
#else
    #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
#endif
#include "ZUHeroLook.hlsl"

struct ZuDepthAttributes
{
    float4 positionOS   : POSITION;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float3 normalOS     : NORMAL;
    float3 bindMS       : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings ZuDepthVertex(ZuDepthAttributes input)
{
    Attributes a = (Attributes)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, a);
    float3 p = ZuSmearOS(input.positionOS.xyz, input.normalOS, input.bindMS);
    a.texcoord = input.texcoord;
#if defined(ZU_DEPTH_NORMALS)
    a.positionOS = float4(p, 1.0);
    a.tangentOS = input.tangentOS;
    a.normal = input.normalOS;
    return DepthNormalsVertex(a);
#else
    a.position = float4(p, 1.0);
    return DepthOnlyVertex(a);
#endif
}

#endif
