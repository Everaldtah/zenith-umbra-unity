// ZU/Water: stylised harbour / river water on flat horizontal planes (see ZUWaterPass.hlsl for the model). One forward
// pass in the transparent queue, no depth write, no shadow casting. The orchestrator enables Depth Texture + Opaque
// Texture on the URP asset; without them tick "No Scene Textures" (or the shader detects the unbound textures) and the
// water falls back to opaque deep water with reflections and sun, no refraction or foam.
Shader "ZU/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor("Shallow Colour", Color) = (0.22, 0.55, 0.60, 1)
        _DeepColor("Deep Colour", Color) = (0.02, 0.12, 0.20, 1)
        _DepthMax("Depth Max (m): fully deep colour, 5% transmitted", Float) = 6.0
        _Smoothness("Smoothness (sun lobe)", Range(0.0, 1.0)) = 0.93

        [Header(Waves)]
        _WaveScale("Wave Scale (m, longest wavelength)", Float) = 6.0
        _WaveSpeed("Wave Speed", Float) = 1.0
        _WaveStrength("Wave Strength", Range(0.0, 3.0)) = 1.0
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0.0
        [Normal][NoScaleOffset] _NormalMap("Normal Map (optional detail)", 2D) = "bump" {}
        _NormalTiling("Normal Tiling (repeats per m)", Float) = 0.12
        _NormalStrength("Normal Strength", Range(0.0, 2.0)) = 0.6
        _FarDistance("Far Flattening Distance (m)", Float) = 400.0

        [Header(Foam and Refraction)]
        _FoamColor("Foam Colour", Color) = (0.90, 0.95, 0.95, 1)
        _FoamWidth("Foam Width (m of depth)", Float) = 0.5
        _RefractionStrength("Refraction Strength", Range(0.0, 0.3)) = 0.06
        [Toggle(_ZU_SCENE_OFF)] _SceneOff("No Scene Textures (fallback)", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // -------------------------------------
            // Render State Commands
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ZUWaterVertex
            #pragma fragment ZUWaterFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ZU_SCENE_OFF

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "ZUWaterPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
