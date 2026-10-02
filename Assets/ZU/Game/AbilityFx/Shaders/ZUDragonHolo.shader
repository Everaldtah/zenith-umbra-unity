// The spirit koi-dragons' holographic body (TS render/SpiritDragon.ts VERT / FRAG, ported). The mesh is the dragon baked
// into its straight rest pose (spine along +Z, head toward +Z); every vertex is skinned here by up to four joints whose
// matrices (_Bones, set per dragon each frame by SpiritDragons) carry that slice of the body straight to the world - the
// TS's identity-bind trick, so a joint's skinning matrix is simply "where its slice of the body goes".
// Bone indices ride in UV2, weights in UV3. Premultiplied alpha (dst * (1 - a) + body + rim), no depth write, two-sided.
Shader "ZU/DragonHolo"
{
    Properties
    {
        _BaseMap("Painted scales", 2D) = "white" {}
        [HDR] _Col("Colour", Color) = (0.3, 0.6, 1, 1)
        [HDR] _Glow("Glow", Color) = (0.6, 0.8, 1, 1)
        _T("Time", Float) = 0
        _Alpha("Materialise", Float) = 0
        _Head("Head z", Float) = 5
        _Len("Length", Float) = 10
        _Seed("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "DragonHolo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Col, _Glow;
                float _T, _Alpha, _Head, _Len, _Seed;
            CBUFFER_END
            // the joints (SpiritDragons.MAX_JOINTS): set per renderer, so not part of the per-material buffer
            float4x4 _Bones[48];

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 bi : TEXCOORD2; float4 bw : TEXCOORD3; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 view : TEXCOORD2;
                float along : TEXCOORD3; float3 world : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float4 p = float4(i.positionOS.xyz, 1);
                float3 wp = 0, wn = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float w = i.bw[k];
                    if (w <= 0) continue;
                    float4x4 m = _Bones[(int)i.bi[k]];
                    wp += mul(m, p).xyz * w;
                    wn += mul((float3x3)m, i.normalOS) * w;      // (the TS skins the normal with the same matrix)
                }
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.along = i.positionOS.z;
                o.n = normalize(wn + 1e-6);
                o.view = _WorldSpaceCameraPos - wp;
                o.world = wp;
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(i.view);
                float fr = pow(1.0 - abs(dot(n, v)), 1.8);
                float back = saturate((_Head - i.along) / _Len);                          // 0 at the snout, 1 at the tail tip
                float3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float lum = dot(tex, float3(0.3, 0.59, 0.11));
                float vein = smoothstep(0.55, 0.9, lum);                                   // the concept's glowing scale lines
                // scale lattice: offset rows of arcs along the body
                float2 sc = float2(i.along * 3.2, i.uv.y * 26.0); sc.x += step(1.0, fmod(sc.y, 2.0)) * 0.5;
                float2 cell = frac(sc) - float2(0.5, 0.0);
                float scale = smoothstep(0.08, 0.0, abs(length(cell) - 0.55)) * 0.6;
                float pulse = pow(0.5 + 0.5 * sin(i.along * 1.3 + _T * 10.0), 8.0);              // energy racing to the tail
                float scan = 0.5 + 0.5 * sin(i.world.y * 38.0 - _T * 7.0);                      // hologram scanlines
                float glitch = step(0.94, noise(float2(floor(i.world.y * 6.0), floor(_T * 14.0) + _Seed)));
                float flicker = 0.9 + 0.1 * noise(float2(_T * 25.0, _Seed));
                float3 irid = lerp(_Col.rgb, _Glow.rgb, fr) + float3(0.12, -0.05, 0.18) * sin(fr * 6.0 + _T * 2.0);
                // the holographic body (semi-opaque, so it still reads over a bright white arena) ...
                float3 body = irid * (0.8 + 0.8 * lum) + _Glow.rgb * (vein * 1.1 + scale * 0.5 + pulse * 0.7);
                body *= (0.85 + 0.25 * scan) * flicker;
                float bodyA = (0.55 + 0.35 * fr + 0.2 * vein) * (1.0 - smoothstep(0.6, 1.0, back) * 0.85);
                // ... plus light that only adds: the fresnel rim, a white-hot edge and glitch bands
                float3 rim = _Glow.rgb * fr * 1.7 + float3(1, 1, 1) * pow(fr, 5.0) * 0.4 + _Glow.rgb * glitch * 0.8;
                // dissolve: _Alpha 0..1 sweeps a noise threshold; the burning edge glows white-hot
                float dn = noise(i.uv * 18.0 + float2(_Seed, _Seed)) * 0.7 + noise(float2(i.along * 0.9, _Seed)) * 0.3;
                float edge = _Alpha * 1.15 - dn;
                clip(edge);
                rim += lerp(_Glow.rgb, float3(1, 1, 1), 0.6) * smoothstep(0.12, 0.0, edge) * 2.5;
                float k = min(1.0, _Alpha * 1.5);
                return half4(body * bodyA * k + rim * k, bodyA * k);                          // premultiplied
            }
            ENDHLSL
        }
    }
}
