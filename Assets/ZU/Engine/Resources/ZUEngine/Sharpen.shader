// The web game's native-scale image sharpening (port of zenith-umbra src/client/Game.ts:54-61 SharpenShader, engine
// core), as a URP full-screen blit. The web ran it as the LAST pass of its chain - after OutputPass (tone mapping +
// sRGB encoding), the colour grade and SMAA - and only while the render scale was >= 0.99: below native AMD FSR's RCAS
// sharpened the upscaled picture instead, and this pass would only have sharpened the aliasing it upscales (Game.ts:325).
// Exact maths of the original: a 5-tap unsharp mask, out = clamp(c + (4c - sum(neighbours)) * amount, 0, 1),
// amount = slider/100 x 0.6, texel = 1 / render size (exact texels: point sampling, clamped at the edges like the web's
// ClampToEdge render target).
// WHY the sRGB round trip: the web sharpened the DISPLAY-REFERRED picture - the canvas texture after OutputPass, i.e.
// sRGB-encoded 8-bit values. URP's camera colour after post-processing holds LINEAR values (the final blit / final post
// pass does the sRGB encoding, or the sRGB backbuffer does), so the same formula on linear values would weight the
// highlights ~2.2x more than the web and clip them differently. So, in a linear colour-space project
// (!UNITY_COLORSPACE_GAMMA, a built-in platform define), the 5 taps are converted LinearToSRGB, the mask and clamp run in
// that encoding, and the result goes back with SRGBToLinear - same curve, same numbers as the web, same clamp. In a
// gamma project the buffer already holds encoded values and nothing is converted.
// NOTE: the web drew the first-person viewmodel AFTER this pass at native, so the arms were never sharpened. URP draws
// the viewmodel inside the camera stack before post-processing; it is sharpened here too. Not reproducible, accepted.
// Loaded with Resources.Load<Shader>("ZUEngine/Sharpen") (SharpenPass.cs): the Resources folder keeps it in player
// builds without touching any renderer asset; Shader.Find would need it referenced from a scene or "Always Included".
Shader "Hidden/ZU/Sharpen"
{
    HLSLINCLUDE
        #pragma target 2.0
        #pragma editor_sync_compilation
        // URP Core.hlsl first: the stereo / instancing cbuffers that Blit.hlsl's XR macros rely on (as URP's CoreBlit.shader)
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        // LinearToSRGB / SRGBToLinear (Blit.hlsl pulls it in already; explicit for the reader)
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        // slider / 100 x 0.6 (SharpenPass sets it; 0 = pass not enqueued at all)
        float _Amount;

        // one tap in the web's encoding (sRGB in a linear project; as stored in a gamma project)
        float3 Tap(float2 uv)
        {
            float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
        #if !defined(UNITY_COLORSPACE_GAMMA)
            c = LinearToSRGB(c);
        #endif
            return c;
        }

        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 texel = _BlitTexture_TexelSize.xy;                         // 1 / source size, filled by Unity for _BlitTexture
            float4 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            float3 c = src.rgb;
        #if !defined(UNITY_COLORSPACE_GAMMA)
            c = LinearToSRGB(c);
        #endif
            // the web: n = right + left + up + down (texture2D at uv +- texel)
            float3 n = Tap(uv + float2(texel.x, 0.0)) + Tap(uv - float2(texel.x, 0.0))
                     + Tap(uv + float2(0.0, texel.y)) + Tap(uv - float2(0.0, texel.y));
            // gl_FragColor = vec4(clamp(c + (c * 4 - n) * amount, 0, 1), c.a)
            float3 o = saturate(c + (c * 4.0 - n) * _Amount);
        #if !defined(UNITY_COLORSPACE_GAMMA)
            o = SRGBToLinear(o);
        #endif
            return float4(o, src.a);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        // 0: the one and only pass (SharpenPass blits with shaderPass 0, a procedural full-screen triangle: Vert)
        Pass
        {
            Name "ZU Sharpen"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
