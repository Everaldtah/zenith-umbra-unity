// Puts the front end's gamma texture on the screen (UiGamma.cs, Graphics.Blit at the end of the frame).
// _MainTex is the UI panel rendered with PanelSettings.forceGammaRendering into a UNORM target: sRGB-ENCODED colour,
// premultiplied by alpha, every layer of the UI already blended on encoded values the way a browser blends CSS.
//
// On an sRGB backbuffer (linear project, the usual case) the GPU blends LINEAR values and the shader cannot read what
// is under it, so the exact CSS result  decode(P + (1 - A) * encode(scene))  is out of reach for see-through pixels.
// What it does instead, with blend One OneMinusSrcAlpha:
//   - opaque UI (A = 1): decode(colour). Exact. Every menu screen is opaque, so the menus match the web page.
//   - dark see-through UI (panel glass, veils): alpha' = 1 - (1 - A)^2.2, which makes the scene term exact for a black
//     overlay - the scene dims by the amount it dims in the browser.
//   - bright see-through UI (text edges, light glass): alpha' = A, the plain linear blend. The dark-glass alpha would
//     take too much of a bright scene away under a white text edge and leave a dark fringe around the glyphs.
//   The two are mixed by the luminance of the UI colour.
// On a backbuffer that takes encoded values (_ZuEncoded = 1) the texture goes out untouched and the blend IS the CSS blend.
// Kept in Resources so player builds include it (Resources.Load<Shader>("ZUUI/ZUUiComposite")).
Shader "Hidden/ZU/UiComposite"
{
    Properties
    {
        _MainTex ("UI (gamma, premultiplied)", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _ZuFlipY;
            float _ZuEncoded;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                if (_ZuFlipY > 0.5) o.uv.y = 1.0 - o.uv.y;
                return o;
            }

            // the sRGB transfer function, exact (not the 2.2 power)
            float3 Decode(float3 c)
            {
                c = saturate(c);
                float3 lo = c / 12.92;
                float3 hi = pow((c + 0.055) / 1.055, 2.4);
                return lerp(hi, lo, step(c, 0.04045));
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 ui = tex2D(_MainTex, i.uv);
                if (_ZuEncoded > 0.5) return ui;

                float a = saturate(ui.a);
                if (a < 0.0005) return float4(0.0, 0.0, 0.0, 0.0);
                float3 c = saturate(ui.rgb / a);                           // the UI's own colour, encoded
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                float aDark = 1.0 - pow(1.0 - a, 2.2);
                float a2 = lerp(aDark, a, smoothstep(0.04, 0.45, lum));
                return float4(Decode(c) * a2, a2);
            }
            ENDCG
        }
    }
    Fallback Off
}
