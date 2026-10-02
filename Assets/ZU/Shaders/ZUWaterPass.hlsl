#ifndef ZU_WATER_PASS_INCLUDED
#define ZU_WATER_PASS_INCLUDED
// ZU/Water forward pass: a stylised, physically plausible water surface for harbours and rivers on flat, horizontal
// (Y up) plane meshes - possibly kilometres across, so everything is driven by world position, never by mesh UVs.
//  - normal: four analytic deep-water waves (exp-sine crests, dispersion-correct speeds) always on, plus two scrolling
//    layers of the optional normal map (_NORMALMAP); all summed as height-field slopes, then flattened with distance so
//    sub-pixel waves don't alias into sparkle far away
//  - depth: the scene's world position is rebuilt from _CameraDepthTexture; the vertical distance below the surface
//    drives the shallow -> deep colour, the transmittance of the refracted _CameraOpaqueTexture, the shoreline foam and
//    the soft intersection. Without the two textures (_ZU_SCENE_OFF, or their TexelSize reads as unbound) the water is
//    opaque deep water with no foam - never black
//  - light: Fresnel-weighted (F0 0.02) split into transmitted (refraction + scattered body colour, Lambert-lit with
//    shadows) and reflected (reflection probes / sky through GlossyEnvironmentReflection, GGX sun and additional lights
//    with a Schlick grazing term); fog on top. No depth write: it is drawn in the transparent queue over the opaque
//    depth it reads, and writing depth would occlude later transparents (particles, splashes) with a flat plane.

#define _SURFACE_TYPE_TRANSPARENT 1         // Shadows.hlsl: sample the shadow map, not the opaque-only screen shadows
#define _SCREENSPACEREFLECTIONS_OFF 1       // GlobalIllumination.hlsl: no SSR on a transparent

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

CBUFFER_START(UnityPerMaterial)
half4 _ShallowColor;
half4 _DeepColor;
half4 _FoamColor;
float _DepthMax;
half _Smoothness;
float _WaveScale;
float _WaveSpeed;
half _WaveStrength;
float _NormalTiling;
half _NormalStrength;
float _FarDistance;
float _FoamWidth;
half _RefractionStrength;
CBUFFER_END

TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float3 positionWS   : TEXCOORD0;
    half3 normalWS      : TEXCOORD1;
    half4 tangentWS     : TEXCOORD2;    // xyz: tangent, w: sign
    half fogFactor      : TEXCOORD3;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD5;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

///////////////////////////////////////////////////////////////////////////////
//                               Helpers                                     //
///////////////////////////////////////////////////////////////////////////////

// Float hash (Dave Hoskins) and value noise with a quintic fade: the foam pattern (same functions as ZUSurface, kept
// local so each shader stays self-contained).
float ZUWaterHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float ZUWaterNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return lerp(lerp(ZUWaterHash(i),                   ZUWaterHash(i + float2(1.0, 0.0)), f.x),
                lerp(ZUWaterHash(i + float2(0.0, 1.0)), ZUWaterHash(i + float2(1.0, 1.0)), f.x), f.y);
}

static const float2 kZUWaveDir[4] = { float2(1.0, 0.0), float2(0.6, 0.8), float2(-0.5, 0.866), float2(0.3, -0.954) };
static const float  kZUWaveLen[4] = { 1.0, 0.57, 0.31, 0.17 };   // of _WaveScale
static const float  kZUWaveAmp[4] = { 1.0, 0.55, 0.3, 0.16 };    // steepness weights

// Analytic waves as a height-field slope (dh/dx, dh/dz) in world xz. Exp-sine crests (sharper tops than a sine),
// deep-water dispersion w = sqrt(g k) so the long swell outruns the chop. The amplitude is expressed as steepness
// (a * k), so the slope never exceeds ~0.14 per wave at strength 1.
float2 ZUWaveSlope(float2 xz, float time)
{
    float2 slope = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float L = kZUWaveLen[i] * max(_WaveScale, 0.05);
        float k = TWO_PI / L;
        float w = sqrt(9.81 * k);
        float ph = dot(kZUWaveDir[i], xz) * k + time * w;
        float s = sin(ph);
        float dh = 0.14 * kZUWaveAmp[i] * cos(ph) * exp(s - 1.0);
        slope += dh * kZUWaveDir[i];
    }
    return slope;
}

// GGX sun / point-light lobe with Schlick on L.H: URP's direct term skips the grazing boost, and on water that boost
// is the glitter path toward the sun.
half3 ZUWaterDirectSpecular(BRDFData brdf, half3 N, half3 L, half3 V)
{
    half3 H = normalize(L + V + half3(0, 1e-4, 0));
    half LoH = saturate(dot(L, H));
    half x = 1.0 - LoH;
    half x5 = x * x * x * x * x;
    half3 F = lerp(brdf.specular, half3(1.0, 1.0, 1.0), x5);
    return DirectBRDFSpecular(brdf, N, L, V) * F;
}

void ZUWaterAccumulate(Light light, BRDFData brdf, half3 N, half3 V, inout half3 diffuse, inout half3 specular)
{
    half NoL = saturate(dot(N, light.direction));
    half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation * NoL);
    diffuse += radiance;
    specular += radiance * ZUWaterDirectSpecular(brdf, N, light.direction, V);
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

Varyings ZUWaterVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;
    output.normalWS = half3(normalInput.normalWS);
    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif
    output.fogFactor = fogFactor;

    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

    return output;
}

void ZUWaterFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 positionWS = input.positionWS;
    float3 toCam = GetCameraPositionWS() - positionWS;
    float dist = length(toCam);
    half3 V = half3(toCam / max(dist, 1e-4));
    float time = _Time.y * _WaveSpeed;

    // far away the waves fade to a flat plane and the lobe widens a touch instead
    float farFade = 1.0 - smoothstep(_FarDistance * 0.2, _FarDistance, dist);

    // ---- normal: analytic waves + map layers, summed as slopes in world xz
    float2 slope = ZUWaveSlope(positionWS.xz, time) * _WaveStrength;
#if defined(_NORMALMAP)
    float2 uvA = positionWS.xz * _NormalTiling + time * float2(0.031, 0.017);
    float2 uvB = positionWS.xz * (_NormalTiling * 2.37) + time * float2(-0.023, 0.041);
    half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), _NormalStrength);
    half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), _NormalStrength * 0.6);
    // tangent-space normal -> slope along the mesh tangent / bitangent, then into world xz
    float2 sMap = -nA.xy / max(nA.z, 0.2) - nB.xy / max(nB.z, 0.2);
    float3 T = normalize(float3(input.tangentWS.xyz));
    float3 B = input.tangentWS.w * cross(normalize(float3(input.normalWS)), T);
    slope += sMap.x * T.xz + sMap.y * B.xz;
#endif
    slope *= farFade;
    half3 N = half3(normalize(float3(-slope.x, 1.0, -slope.y)));
    // the sky is read through a flatter normal: full wave normals at low roughness mirror a noisy, broken sky
    half3 Nflat = half3(normalize(float3(-slope.x * 0.35, 1.0, -slope.y * 0.35)));

    // ---- scene depth and refraction
    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
#if defined(_ZU_SCENE_OFF)
    bool hasScene = false;
#else
    // an unbound texture reads a zero TexelSize; URP's own placeholder textures are 4x4 - both mean "no scene"
    bool hasScene = _CameraDepthTexture_TexelSize.z > 8.0 && _CameraOpaqueTexture_TexelSize.z > 8.0;
#endif
    float depthV = _DepthMax;           // vertical water depth under this pixel, metres
    half3 sceneColor = half3(0, 0, 0);
    if (hasScene)
    {
        float raw0 = SampleSceneDepth(screenUV);
        float3 scenePos0 = ComputeWorldSpacePosition(screenUV, raw0, UNITY_MATRIX_I_VP);
        float depth0 = max(positionWS.y - scenePos0.y, 0.0);
        // a world-constant displacement scaled by depth (a shallow bottom barely shifts) and perspective
        float2 refrUV = screenUV + N.xz * (_RefractionStrength * saturate(depth0 * 0.5) / max(input.positionCS.w, 1.0));
        float rawR = SampleSceneDepth(refrUV);
        // the displaced sample may land on something above the water (a quay, a hero): keep the straight sample then
        bool ok = LinearEyeDepth(rawR, _ZBufferParams) >= input.positionCS.w;
        float3 scenePosR = ComputeWorldSpacePosition(refrUV, rawR, UNITY_MATRIX_I_VP);
        float2 sceneUV = ok ? refrUV : screenUV;
        depthV = ok ? max(positionWS.y - scenePosR.y, 0.0) : depth0;
        sceneColor = half3(SampleSceneColor(sceneUV));
    }

    float depth01 = saturate(depthV / max(_DepthMax, 0.01));
    half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, half(depth01));
    // Beer-Lambert-ish transmittance of what lies under (5 % left at _DepthMax), tinted toward the shallow colour
    half transmit = hasScene ? half(exp(-3.0 * depth01)) : half(0.0);
    half3 tint = lerp(half3(1, 1, 1), _ShallowColor.rgb, half(saturate(depth01 * 2.0)));

    // ---- lighting
    half smoothness = lerp(_Smoothness * 0.85, _Smoothness, half(farFade));
    BRDFData brdf;
    half alpha = 1.0;
    InitializeBRDFData(body, half(0.0), half3(0, 0, 0), smoothness, alpha, brdf);

    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = N;
    inputData.viewDirectionWS = V;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
    inputData.normalizedScreenSpaceUV = screenUV;
    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactor);
    inputData.shadowMask = half4(1, 1, 1, 1);
#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(positionWS), N, V, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, N);
#endif

    uint meshRenderingLayers = GetMeshRenderingLayer();
    half3 directDiffuse = half3(0, 0, 0);
    half3 directSpec = half3(0, 0, 0);

    Light mainLight = GetMainLight(inputData.shadowCoord, positionWS, inputData.shadowMask);
    half3 sunRadiance = half3(0, 0, 0);     // for the foam (lit like a white Lambert surface)
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        ZUWaterAccumulate(mainLight, brdf, N, V, directDiffuse, directSpec);
        sunRadiance = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * saturate(dot(N, mainLight.direction)));
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, positionWS, inputData.shadowMask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            ZUWaterAccumulate(light, brdf, N, V, directDiffuse, directSpec);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, inputData.shadowMask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            ZUWaterAccumulate(light, brdf, N, V, directDiffuse, directSpec);
        }
    LIGHT_LOOP_END
#endif

    // ---- Fresnel split: what passes into the water vs what the surface reflects
    half NoV = saturate(dot(Nflat, V));
    half fresnelTerm = Pow4(1.0 - NoV);
    half f1 = 1.0 - NoV;
    half F = 0.02 + 0.98 * (f1 * f1 * f1 * f1 * f1);

    half3 R = reflect(-V, Nflat);
    R.y = max(R.y, 0.03);               // never read the probe's floor through a wave: the horizon stays sky
    half3 env = GlossyEnvironmentReflection(R, positionWS, brdf.perceptualRoughness, half(1.0), screenUV);
    half3 envSpec = env * EnvironmentBRDFSpecular(brdf, fresnelTerm);

    half3 under = sceneColor * tint * transmit + brdf.diffuse * (inputData.bakedGI + directDiffuse) * (1.0 - transmit);
    half3 color = (1.0 - F) * under + envSpec + directSpec;

    // ---- shoreline foam and soft intersection
    half foam = 0.0;
    if (hasScene)
    {
        float fm = 1.0 - saturate(depthV / max(_FoamWidth, 0.01));
        float fn = ZUWaterNoise(positionWS.xz * 1.9 + float2(time * 0.35, -time * 0.2)) * 0.6
                 + ZUWaterNoise(positionWS.xz * 6.1 + float2(-time * 0.5, time * 0.3)) * 0.4;
        foam = half(fm * (0.35 + 0.65 * smoothstep(0.3, 0.7, fn)));
    }
    half3 foamLit = _FoamColor.rgb * (inputData.bakedGI + sunRadiance);
    color = lerp(color, foamLit, foam);
    if (hasScene)
    {
        // the surface melts into the bottom where it meets it (the foam still covers the very edge)
        half edge = half(saturate(depthV / 0.06));
        color = lerp(sceneColor, color, max(edge, foam));
    }

    color = MixFog(color, inputData.fogCoord);
    outColor = half4(color, 1.0);

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
