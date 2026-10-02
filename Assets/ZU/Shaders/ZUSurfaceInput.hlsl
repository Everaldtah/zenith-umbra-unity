#ifndef ZU_SURFACE_INPUT_INCLUDED
#define ZU_SURFACE_INPUT_INCLUDED
// ZU/Surface inputs: URP 17.6's LitInput.hlsl (PackageCache/com.unity.render-pipelines.universal@b0678dfc9e21/Shaders),
// cut to the metallic workflow (no parallax / detail / clear coat / specular setup - map blockouts don't use them) plus
// the ZU surface layers: painted bevel, edge lift, grime, cavity, macro variation, roughness floor. Lit's property
// names and keywords are kept, so a material swaps between Lit and ZU/Surface without losing its textures.
//
// Vertex-data contract (the C# box builder writes it; Unity world space, Y up, boxes are world-axis aligned):
//   TEXCOORD0 float2: world-metric UV in metres (_BaseMap_ST tiles it; normal map and mask share it, as Lit)
//   TEXCOORD2 float4: xyz = vertex position relative to its box's centre (m), w = grime weight 0..1
//   TEXCOORD3 float4: xyz = the box's half extents (m; >= 1e3 on any axis = "not a box": no bevel), w = world Y of its foot
// Non-box geometry carries uv2 = 0 and uv3.xyz = 1e4; a mesh without uv2/uv3 at all reads as zeros, which also
// disables the bevel and the grime (radius 0, weight 0).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"

// NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
half4 _BaseColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _OcclusionStrength;
// ZU layers (defaults and units: ZUSurface.shader Properties)
float _Bevel;
half _EdgeLift;
half _Grime;
float _GrimeHeight;
half _Cavity;
half _Macro;
float _MacroScale;
half _RoughMin;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
// Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
// NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _EmissionColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _BumpScale)
    UNITY_DOTS_INSTANCED_PROP(float , _OcclusionStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _Bevel)
    UNITY_DOTS_INSTANCED_PROP(float , _EdgeLift)
    UNITY_DOTS_INSTANCED_PROP(float , _Grime)
    UNITY_DOTS_INSTANCED_PROP(float , _GrimeHeight)
    UNITY_DOTS_INSTANCED_PROP(float , _Cavity)
    UNITY_DOTS_INSTANCED_PROP(float , _Macro)
    UNITY_DOTS_INSTANCED_PROP(float , _MacroScale)
    UNITY_DOTS_INSTANCED_PROP(float , _RoughMin)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// As Lit: the property loads are cached in statics once, and the names are redirected to them, so the compiler doesn't
// regenerate the load code at every use.
static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_Bevel;
static float  unity_DOTS_Sampled_EdgeLift;
static float  unity_DOTS_Sampled_Grime;
static float  unity_DOTS_Sampled_GrimeHeight;
static float  unity_DOTS_Sampled_Cavity;
static float  unity_DOTS_Sampled_Macro;
static float  unity_DOTS_Sampled_MacroScale;
static float  unity_DOTS_Sampled_RoughMin;

void SetupDOTSZUSurfaceMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor         = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_EmissionColor     = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
    unity_DOTS_Sampled_Cutoff            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_BumpScale         = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
    unity_DOTS_Sampled_OcclusionStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OcclusionStrength);
    unity_DOTS_Sampled_Bevel             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Bevel);
    unity_DOTS_Sampled_EdgeLift          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EdgeLift);
    unity_DOTS_Sampled_Grime             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Grime);
    unity_DOTS_Sampled_GrimeHeight       = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _GrimeHeight);
    unity_DOTS_Sampled_Cavity            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cavity);
    unity_DOTS_Sampled_Macro             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Macro);
    unity_DOTS_Sampled_MacroScale        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _MacroScale);
    unity_DOTS_Sampled_RoughMin          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _RoughMin);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSZUSurfaceMaterialPropertyCaches()

#define _BaseColor          unity_DOTS_Sampled_BaseColor
#define _EmissionColor      unity_DOTS_Sampled_EmissionColor
#define _Cutoff             unity_DOTS_Sampled_Cutoff
#define _Smoothness         unity_DOTS_Sampled_Smoothness
#define _Metallic           unity_DOTS_Sampled_Metallic
#define _BumpScale          unity_DOTS_Sampled_BumpScale
#define _OcclusionStrength  unity_DOTS_Sampled_OcclusionStrength
#define _Bevel              unity_DOTS_Sampled_Bevel
#define _EdgeLift           unity_DOTS_Sampled_EdgeLift
#define _Grime              unity_DOTS_Sampled_Grime
#define _GrimeHeight        unity_DOTS_Sampled_GrimeHeight
#define _Cavity             unity_DOTS_Sampled_Cavity
#define _Macro              unity_DOTS_Sampled_Macro
#define _MacroScale         unity_DOTS_Sampled_MacroScale
#define _RoughMin           unity_DOTS_Sampled_RoughMin

#endif

// _BaseMap, _BumpMap and _EmissionMap come from SurfaceInput.hlsl. The mask (Poly Haven ARM repacked by the C#) is
// R = metallic, G = ambient occlusion, A = smoothness; it is assigned to both _MetallicGlossMap and _OcclusionMap.
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv));
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else // _METALLICSPECGLOSSMAP
    specGloss.rgb = _Metallic.rrr;
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

half SampleOcclusion(float2 uv)
{
    #ifdef _OCCLUSIONMAP
        half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #else
        return half(1.0);
    #endif
}

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

    outSurfaceData.clearCoatMask       = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
}

///////////////////////////////////////////////////////////////////////////////
//                           ZU surface layers                               //
///////////////////////////////////////////////////////////////////////////////

// Float hash (Dave Hoskins) - stable at world-scale coordinates; a sin() hash breaks up past a few hundred metres.
float ZUHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise with a quintic fade (C2), so the lattice never shows as a grid.
float ZUNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return lerp(lerp(ZUHash(i),                   ZUHash(i + float2(1.0, 0.0)), f.x),
                lerp(ZUHash(i + float2(0.0, 1.0)), ZUHash(i + float2(1.0, 1.0)), f.x), f.y);
}

bool ZUIsBox(float3 h)
{
    return max(h.x, max(h.y, h.z)) < 1e3;
}

// The bevel radius in use: the property, capped at 35 % of the thinnest half extent so thin trims keep a flat middle
// (as the web game); 0 for non-box geometry or when the mesh carries no box data at all.
float ZUBevelRadius(float3 h)
{
    float r = min(_Bevel, 0.35 * min(h.x, min(h.y, h.z)));
    return ZUIsBox(h) ? max(r, 0.0) : 0.0;
}

// Rounded-box normal. Shrink the box by r; a point's normal points away from the nearest point of the shrunk box:
//   q = clamp(|p| - (h - r), 0, r);  n = sign(p) * q
// On a face interior only the face's own axis is non-zero (q = r there), so the result IS the face normal: the band
// starts with no seam. Toward an edge the neighbouring axis grows from 0 to r, turning the normal smoothly to 45 deg
// at the edge and to the diagonal at a corner. The clamp to [0, r] means an interpolated p that overshoots h, or a
// flat face read on its own axis, can never over-rotate; the own-axis component is always r, so the length is never 0.
// edgeT: how far into the band (0 where it starts, 1 at the edge), linear in distance - drives the edge lift.
float3 ZUBevelNormal(float3 nGeomWS, float3 p, float3 h, float r, float grimeW, out float edgeT)
{
    edgeT = 0.0;
    if (r <= 0.0)
        return nGeomWS;

    float3 q = clamp(abs(p) - (h - r), 0.0, r);
    float3 an = abs(nGeomWS);
    // a wall standing on the ground has no edge at its foot: the floor continues there (a rounded foot reads as floating)
    if (grimeW > 0.5 && an.y < 0.5 && p.y < 0.0)
        q.y = 0.0;

    float3 nLocal = sign(p) * q;
    float len = length(nLocal);

    // band depth from the axes that are NOT the face's own (that one is r everywhere on the face)
    float3 qr = (q / r) * (1.0 - an);
    edgeT = saturate(max(qr.x, max(qr.y, qr.z)));

    return len > 1e-5 ? nLocal / len : nGeomWS;
}

// The normal map on top of the bent normal: the tangent frame is re-orthogonalised against the bent normal
// (Gram-Schmidt), so the map perturbs relative to the bevelled surface. Adding the map's world-space deviation instead
// would pull the band back toward the flat face and can tip past 90 deg at a corner. The bitangent is rebuilt with the
// sign tangentWS.w carries, exactly as Lit does, so mirrored / odd-scaled meshes stay right. |t| after the projection
// is >= 0.57 (the tangent lies in the flat face, the bent normal is within 54.7 deg of the face normal): safe to normalise.
half3 ZUPerturbNormal(float3 nBentWS, half4 tangentWS, half3 normalTS, out half3x3 tangentToWorld)
{
    float3 t = tangentWS.xyz - nBentWS * dot(tangentWS.xyz, nBentWS);
    t = normalize(t);
    float3 b = tangentWS.w * cross(nBentWS, t);
    tangentToWorld = half3x3(half3(t), half3(b), half3(nBentWS));
    return TransformTangentToWorld(normalTS, tangentToWorld);
}

// The world normal every pass lights / writes: bevel first, then the normal map (when _NORMALMAP). The forward,
// GBuffer and DepthNormals passes all go through here, so SSAO / SSR see exactly the lit normal.
half3 ZUSurfaceNormalWS(float3 nGeomWS, half4 tangentWS, half3 normalTS, float4 boxLocal, float4 boxHalf,
                        out float edgeT, out half3x3 tangentToWorld)
{
    nGeomWS = normalize(nGeomWS);
    float r = ZUBevelRadius(boxHalf.xyz);
    float3 nBent = ZUBevelNormal(nGeomWS, boxLocal.xyz, boxHalf.xyz, r, boxLocal.w, edgeT);
#if defined(_NORMALMAP)
    return ZUPerturbNormal(nBent, tangentWS, normalTS, tangentToWorld);
#else
    tangentToWorld = half3x3(half3(1, 0, 0), half3(0, 0, 1), half3(nBent));
    return half3(nBent);
#endif
}

// The albedo layers, in place on the surface data (call after InitializeStandardLitSurfaceData, before the decals):
// macro variation, cavity, grime, edge lift; and the roughness floor. Everything here keys off world position and the
// box data, never the UV, so it is independent of the material's tiling.
void ZUApplySurfaceLayers(inout SurfaceData s, float3 positionWS, float4 boxLocal, float4 boxHalf, float3 nGeomWS, float edgeT)
{
    // macro: two octaves of world value noise (1x and 4.2x the scale, a little of Y so stacked floors differ); a touch
    // warmer where it lightens and cooler where it darkens, so it reads as paint batches rather than a brightness wobble
    float invScale = 1.0 / max(_MacroScale, 0.01);
    float mv = ZUNoise(positionWS.xz * invScale + positionWS.y * (0.4 * invScale)) * 0.65
             + ZUNoise(positionWS.zx * (4.2 * invScale) + 7.3) * 0.35;
    half d = half(mv * 2.0 - 1.0);
    half3 macroMul = half3(1.0, 1.0, 1.0) + (_Macro * d) * half3(1.08, 1.0, 0.9);
    s.albedo = s.albedo * macroMul;

    // cavity: the occlusion (mask G, already _OcclusionStrength-scaled) darkens the albedo, so crevices read under sun
    half cavityMul = LerpWhiteTo(s.occlusion, _Cavity);
    s.albedo = s.albedo * cavityMul;

    // grime: the first _GrimeHeight metres above the box's foot, side faces only, broken up by noise, times the weight
    if (ZUIsBox(boxHalf.xyz) && boxLocal.w > 0.001)
    {
        float fromFoot = positionWS.y - boxHalf.w;
        float g = 1.0 - smoothstep(0.0, max(_GrimeHeight, 0.01), fromFoot);
        g *= 1.0 - smoothstep(0.4, 0.7, abs(nGeomWS.y));
        g *= 0.75 + 0.25 * ZUNoise(positionWS.xz * 1.3 + positionWS.y);
        half grimeMul = half(1.0) - half(g * boxLocal.w) * _Grime;
        s.albedo = s.albedo * grimeMul;
    }

    // edge lift: the worn / painted highlight along the bevel band
    half liftMul = half(1.0) + half(edgeT) * _EdgeLift;
    s.albedo = s.albedo * liftMul;

    // roughness floor: big flat surfaces don't mirror the sky (photo-scanned sets are glossier than a stylised street)
    s.smoothness = min(s.smoothness, half(1.0) - _RoughMin);
}

#endif // ZU_SURFACE_INPUT_INCLUDED
