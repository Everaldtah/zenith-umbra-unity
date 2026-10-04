// The scene blur behind the pause and results screens (UiBackdrop.cs): CSS backdrop-filter: blur(Npx) as a separable
// Gaussian, pass 0 horizontal and pass 1 vertical, each a URP full-screen blit of the camera colour.
// _ZuSigma is the standard deviation in SOURCE texels (CSS px scaled from 1080p to the render target's height).
// 25 taps per pass spread over +-3 sigma with bilinear sampling: smooth for the sigmas the UI uses (6-12 px at 1080p).
// The browser blurs display-referred (sRGB-encoded) pixels, so in a linear project the taps are encoded, averaged and
// decoded - a blur of linear values would spread the highlights further than the web page does.
// In Resources so player builds include it (Resources.Load<Shader>("ZUUI/ZUBackdropBlur")).
Shader "Hidden/ZU/BackdropBlur"
{
    HLSLINCLUDE
        #pragma target 3.0
        #pragma editor_sync_compilation
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        float _ZuSigma;

        float3 Enc(float3 c)
        {
        #if !defined(UNITY_COLORSPACE_GAMMA)
            c = LinearToSRGB(max(c, 0.0));
        #endif
            return c;
        }

        float3 Dec(float3 c)
        {
        #if !defined(UNITY_COLORSPACE_GAMMA)
            c = SRGBToLinear(c);
        #endif
            return c;
        }

        float4 Blur(Varyings input, float2 dir)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 texel = _BlitTexture_TexelSize.xy;
            float sigma = max(_ZuSigma, 0.001);
            float stepPx = max(1.0, sigma * 3.0 / 12.0);
            float inv = 1.0 / (2.0 * sigma * sigma);
            float3 sum = 0.0;
            float wsum = 0.0;
            for (int i = -12; i <= 12; i++)
            {
                float x = i * stepPx;
                float w = exp(-x * x * inv);
                sum += Enc(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + dir * texel * x, 0).rgb) * w;
                wsum += w;
            }
            float a = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).a;
            return float4(Dec(sum / wsum), a);
        }

        float4 FragH(Varyings input) : SV_Target { return Blur(input, float2(1.0, 0.0)); }
        float4 FragV(Varyings input) : SV_Target { return Blur(input, float2(0.0, 1.0)); }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ZU Backdrop Blur H"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragH
            ENDHLSL
        }

        Pass
        {
            Name "ZU Backdrop Blur V"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragV
            ENDHLSL
        }
    }

    Fallback Off
}
