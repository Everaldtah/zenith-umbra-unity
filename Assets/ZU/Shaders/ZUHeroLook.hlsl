// The hero look the TS CharacterView injects into every body material (addLook): the skin palette recolour (skinChunk),
// the epic / legendary accent glow and flowing energy lines (rimChunk, minus the team rim - ZU/HeroRim draws that as its
// own pass), the legendary metal trims, and the smear frames on fast moves (the skinning_vertex hook). The values are per
// renderer (Looks/HeroLook writes them into the renderer's MaterialPropertyBlock), so they live outside UnityPerMaterial.
// Spaces: _ZuSmear is world space (the TS mesh-space vector, before its matrix inverse); the bind pose rides in TEXCOORD3 as
// MODEL space (the prefab root's local space, metres - the TS GLB's mesh space, X mirrored), baked by HeroLook.
#ifndef ZU_HERO_LOOK_INCLUDED
#define ZU_HERO_LOOK_INCLUDED

float4 _ZuSmear;                                    // xyz: the trailing edge's drag, world space (m)
float _ZuRemap, _ZuHas1, _ZuHas2, _ZuMetal, _ZuKeepSkin, _ZuHeadY, _ZuHeadBand, _ZuGlow, _ZuPattern, _ZuTime;
float4 _ZuSrc1, _ZuSrc2;                            // xy: the costume's measured hue and mean (linear) value
float4 _ZuDst1, _ZuDst2, _ZuNeutral, _ZuPatternColor;   // rgb, linear (as the TS uniforms hold them)

/// smear frames: surfaces facing away from the motion are dragged back along it (streaky, like drawn speed lines)
float3 ZuSmearOS(float3 positionOS, float3 normalOS, float3 bindMS)
{
    float3 s = mul((float3x3)GetWorldToObjectMatrix(), _ZuSmear.xyz);
    float l = length(s);
    if (l > 1e-4)
    {
        float3 d = s / l;
        float k = smoothstep(0.3, 0.95, dot(normalize(normalOS), d));
        // TS: sin(position.y * 6.0 + position.x * 4.0) on the bind pose (the TS x is Unity's -x)
        positionOS += s * k * (0.75 + 0.25 * sin(bindMS.y * 6.0 - bindMS.x * 4.0));
    }
    return positionOS;
}

/// GLSL mod (floored; HLSL fmod truncates toward zero)
float ZuMod(float x, float y) { return x - y * floor(x / y); }

/// skin palette recolour: the costume's measured primary / accent hues -> the skin's colours, neutrals tinted; skin tones
/// (warm, moderately saturated) and near-greys keep the hero's own paint, so faces never change colour. Works on the linear
/// albedo, as the TS does on diffuseColor. accentW: how much of this texel is the recoloured accent (the glow and metal).
float3 ZuSkin(float3 c, float bindY, out float accentW)
{
    accentW = 0.0;
    if (_ZuRemap > 0.5)
    {
        float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b)), d = mx - mn;
        float h = 0.0;
        if (d > 1e-4) { if (mx == c.r) h = ZuMod((c.g - c.b) / d, 6.0); else if (mx == c.g) h = (c.b - c.r) / d + 2.0; else h = (c.r - c.g) / d + 4.0; h /= 6.0; }
        float s = mx > 0.0 ? d / mx : 0.0, v = mx;
        float skinTone = smoothstep(0.0, 0.03, h) * (1.0 - smoothstep(0.09, 0.13, h)) * smoothstep(0.12, 0.2, s) * (1.0 - smoothstep(0.55, 0.7, s)) * smoothstep(0.25, 0.4, v) * _ZuKeepSkin;
        float sat = smoothstep(0.12, 0.3, s);
        float d1 = abs(h - _ZuSrc1.x); d1 = min(d1, 1.0 - d1);
        float d2 = abs(h - _ZuSrc2.x); d2 = min(d2, 1.0 - d2);
        float w1 = exp(-pow(d1 / 0.075, 2.0)) * sat * (1.0 - skinTone);
        float w2 = exp(-pow(d2 / 0.075, 2.0)) * sat * (1.0 - skinTone) * (1.0 - w1);
        float wn = (1.0 - sat) * (1.0 - skinTone);
        // the head (hair, face) keeps its own colours: above the neck in the bind pose
        float head = smoothstep(_ZuHeadY - _ZuHeadBand, _ZuHeadY + _ZuHeadBand, bindY);
        w1 *= 1.0 - head; w2 *= 1.0 - head; wn *= 1.0 - head;
        float3 r1 = _ZuDst1.rgb * clamp(v / max(0.05, _ZuSrc1.y), 0.25, 1.6);
        float3 r2 = _ZuDst2.rgb * clamp(v / max(0.05, _ZuSrc2.y), 0.25, 1.6);
        float3 outc = lerp(c, r1, w1 * _ZuHas1);
        outc = lerp(outc, r2, w2 * _ZuHas2);
        outc = lerp(outc, c * _ZuNeutral.rgb, wn);
        accentW = w2 * _ZuHas2;
        c = outc;
    }
    return c;
}

/// metallic trims on legendary accents (the TS metalnessmap hook; smoothness = 1 - roughness)
void ZuMetalTrim(inout half metallic, inout half smoothness, float accentW)
{
    metallic = lerp(metallic, 1.0, _ZuMetal * accentW);
    smoothness = lerp(smoothness, 1.0 - 0.28, _ZuMetal * accentW);
}

/// the rimChunk's emissive extras: epic / legendary accent trims glow, and the flowing energy lines across the body
/// (world space, in the sim's frame: Unity mirrors X), which also light the rim by the skin's glow
float3 ZuSkinEmission(float3 positionWS, float3 normalWS, float3 viewDirWS, float accentW)
{
    float rim = pow(1.0 - saturate(abs(dot(normalize(normalWS), normalize(viewDirWS)))), 2.5);
    float3 e = _ZuDst2.rgb * accentW * _ZuGlow * 0.55;
    if (_ZuPattern > 0.0)
    {
        float3 w = float3(-positionWS.x, positionWS.y, positionWS.z);
        float band = sin(w.y * 7.0 - _ZuTime * 3.0 + sin(w.x * 3.0 + w.z * 2.0) * 1.5);
        float ln = smoothstep(0.93, 1.0, band) * _ZuPattern;
        e += _ZuPatternColor.rgb * (ln * 1.6 + _ZuGlow * 0.35 * rim);
    }
    return e;
}

#endif
