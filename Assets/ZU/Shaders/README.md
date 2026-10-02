# ZU shaders (URP 17.6, Unity 6000.6)

Two hand-written URP shaders for the map: `ZU/Surface` for every blockout surface (walls, floors, roofs, trims, props)
and `ZU/Water` for harbours and rivers. Both are derived from this exact URP version's own sources in
`Library/PackageCache/com.unity.render-pipelines.universal@b0678dfc9e21/` (Lit.shader and its passes), so every
pass, keyword and macro matches the pipeline; only what the ZU features need was changed.

| File | What |
| --- | --- |
| `ZUSurface.shader` | the shader: Lit's passes with ZU-prefixed forward / GBuffer / DepthNormals includes |
| `ZUSurfaceInput.hlsl` | `UnityPerMaterial` CBUFFER, DOTS props, surface-data read, and the ZU layer functions (bevel, layers, noise) |
| `ZUSurfaceForwardPass.hlsl` | LitForwardPass.hlsl + box data in the varyings, bent normal, albedo layers |
| `ZUSurfaceGBufferPass.hlsl` | LitGBufferPass.hlsl with the same changes (deferred isn't used; kept consistent, it's cheap) |
| `ZUSurfaceDepthNormalsPass.hlsl` | LitDepthNormalsPass.hlsl writing the bent + mapped normal (SSAO sees the bevels) |
| `ZUWater.shader` / `ZUWaterPass.hlsl` | the water: one forward pass |
| `Editor/ZUShaderCheck.cs` (+ asmdef `ZU.Shaders.Editor`) | CLI commands `zu_shader_check` (render test scenes to PNG) and `zu_shader_variants` (compile the keyword variants that matter) |

ShadowCaster, DepthOnly, Meta and MotionVectors include URP's own pass files unchanged (they need no ZU data).
Universal2D and XRMotionVectors are dropped (no 2D renderer, no XR).

## ZU/Surface

### Vertex-data contract (the C# box builder writes it; Unity world space, Y up, boxes world-axis aligned)

| Channel | Content |
| --- | --- |
| `TEXCOORD0` float2 | world-metric UV in metres; the material's `_BaseMap_ST` tiling = 1 / tileSizeMetres (normal map and mask use the same UV and the same ST, as Lit) |
| `TEXCOORD1` float2 | static lightmap UV (as Lit) |
| `TEXCOORD2` float4 | xyz = vertex position relative to its box's centre (m), w = grime weight 0..1 (1 on walls standing on the ground) |
| `TEXCOORD3` float4 | xyz = the box's half extents (m; >= 1e3 on any axis means "not a box": no bevel), w = world Y of the box's foot |
| `NORMAL`, `TANGENT` | as usual; `tangent.w` carries the handedness and is honoured through the bevel (mirrored meshes stay right) |

Non-box geometry (props, ramps, terrain-adjacent meshes): uv2 = 0, uv3.xyz = 1e4 -> no bevel, no grime, everything else
applies. A mesh with no uv2/uv3 at all reads as zeros, which also disables both (radius 0, weight 0).

Because the box data lives in TEXCOORD2, **dynamic (realtime GI / Enlighten) lightmaps are not supported** - Lit reads
them from TEXCOORD2. Static lightmaps, SH, APV (probe volumes) and shadowmasks work as in Lit.

### Properties (Lit's names; a material switches between Lit and ZU/Surface without losing textures)

Lit set kept: `_BaseMap`, `_BaseColor`, `_Cutoff`, `_Smoothness`, `_SmoothnessTextureChannel`, `_Metallic`,
`_MetallicGlossMap`, `_SpecularHighlights`, `_EnvironmentReflections`, `_ScreenSpaceReflections`,
`_ScreenSpaceReflectionsContributeTransparent`, `_BumpScale`, `_BumpMap`, `_OcclusionStrength`, `_OcclusionMap`,
`_EmissionColor`, `_EmissionMap`, and the blend-state set (`_Surface`, `_Blend`, `_Cull`, `_AlphaClip`, `_SrcBlend`,
`_DstBlend`, `_SrcBlendAlpha`, `_DstBlendAlpha`, `_ZWrite`, `_BlendModePreserveSpecular`, `_AlphaToMask`,
`_AddPrecomputedVelocity`, `_ReceiveShadows`, `_QueueOffset`). Dropped (metallic workflow only): parallax, detail maps,
clear coat, specular setup, `_WorkflowMode`.

Mask layout (`_MetallicGlossMap`, the same texture assigned to `_OcclusionMap`): R = metallic, G = ambient occlusion,
A = smoothness (multiplied by `_Smoothness`). Lit reads exactly these channels; so does this shader.

ZU layers (defaults from the web game's `Surfaces.ts`):

| Property | Default | Meaning |
| --- | --- | --- |
| `_Bevel` | 0.06 m | rounded-edge radius; capped at 35 % of the box's thinnest half extent so thin trims keep a flat middle |
| `_EdgeLift` | 0.16 | albedo brightening across the bevel band, linear in distance into the band (worn / painted highlight) |
| `_Grime` | 0.38 | darkening at the foot of grounded walls, times TEXCOORD2.w |
| `_GrimeHeight` | 1.0 m | the grime fades to nothing this far above TEXCOORD3.w (`1 - smoothstep`, strongest at the foot); side faces only; noise-broken |
| `_Cavity` | 0.25 | the occlusion (mask G, after `_OcclusionStrength`) also darkens the albedo, so crevices read under direct sun |
| `_Macro` | 0.16 | amplitude of the world-space brightness variation (slightly warm where lighter, cool where darker) |
| `_MacroScale` | 18 m | first noise octave; the second is 4.2x finer. Two octaves of hash-based value noise on world xz (+ a little y) - no texture, no swimming, quintic fade so no grid |
| `_RoughMin` | 0.5 | roughness floor: `smoothness = min(smoothness, 1 - _RoughMin)` (the C# sets it per slot, as `Surfaces.ts` does: ground 0.72, wall 0.6, rock 0.75, wood 0.55, trim 0.45, roof 0.4) |

Inspector helpers (not read by the shader): `[Toggle]` floats `_UseNormalMap` (`_NORMALMAP`), `_UseMetallicGlossMap`
(`_METALLICSPECGLOSSMAP`), `_UseOcclusionMap` (`_OCCLUSIONMAP`), `_UseEmission` (`_EMISSION`). Code that enables the
keywords directly (as `EnvImport` does) works regardless; set the matching toggle float too if the material will be
edited in the inspector, since the toggle drawer re-applies its float to the keyword when the shader is (re)assigned.
No `CustomEditor` (Lit's GUI assumes Lit's full property set).

### Keywords

Material: `_NORMALMAP`, `_METALLICSPECGLOSSMAP`, `_OCCLUSIONMAP`, `_EMISSION`, `_ALPHATEST_ON`,
`_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A`, `_SURFACE_TYPE_TRANSPARENT`, `_ALPHAPREMULTIPLY_ON` / `_ALPHAMODULATE_ON`,
`_RECEIVE_SHADOWS_OFF`, `_SPECULARHIGHLIGHTS_OFF`, `_ENVIRONMENTREFLECTIONS_OFF`, `_SCREENSPACEREFLECTIONS_OFF`,
`_SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF` (DepthNormals), `_ADD_PRECOMPUTED_VELOCITY` (motion vectors).
Pipeline (all of Lit's): main light shadows / cascades / screen, additional lights (vertex / pixel) + shadows, soft shadow
tiers, Forward+ (`_CLUSTER_LIGHT_LOOP`), reflection probe blending / box projection / atlas / rotation, SSR, SSAO,
screen-space irradiance, DBuffer decals, light cookies, light layers / rendering layers, lightmaps (static, directional,
shadowmask, bicubic, legacy), APV, fog, LOD crossfade, debug display, GPU instancing, DOTS instancing. Not: dynamic
lightmaps (see above), Meta Quest specials beyond Lit's own `#if`.

### How the layers work

- **Bevel** (`ZUBevelNormal`): the rounded-box normal. With p = TEXCOORD2.xyz, h = TEXCOORD3.xyz, r = radius:
  `q = clamp(|p| - (h - r), 0, r); n = normalize(sign(p) * q)`. On a face interior only the face's own axis is non-zero
  (q = r there), so the result IS the face normal and the band starts with no seam; toward an edge the neighbouring axis
  grows 0 -> r, turning the normal smoothly to 45 degrees at the edge and the diagonal at a corner. The clamp means an
  interpolated p that overshoots h, or any face read on its own axis, never over-rotates, and the own-axis component is
  always r so the length is never 0 (no NaN). Correct on all 6 faces by construction. One rule from the web game is kept:
  a wall standing on the ground (TEXCOORD2.w > 0.5) has no bevel along its foot edge, where the floor continues.
  `edgeT` (0 at the band's start, 1 at the edge, linear in distance) drives the edge lift.
- **Normal map on the bent normal** (`ZUPerturbNormal`): the tangent frame is re-orthogonalised against the bent normal
  (Gram-Schmidt on the tangent, bitangent rebuilt as `tangent.w * cross(n, t)` exactly as Lit does), then the map is
  applied in that frame. Adding the map's world-space deviation instead would pull the band back toward the flat face and
  can tip past 90 degrees at corners; rotating the frame keeps the perturbation relative to the bevelled surface and the
  handedness of the mesh. `inputData.tangentToWorld` (decals) uses the same frame.
- **Same normal in every pass**: forward, GBuffer and DepthNormals all go through `ZUSurfaceNormalWS`, so SSAO / SSR see
  the painted edges (Lit skips the normal map in its octahedral DepthNormals branch; this shader doesn't). The DepthNormals
  smoothness output also applies `_RoughMin`.
- **Albedo layers** (`ZUApplySurfaceLayers`, after the surface data is read, before decals): macro, cavity, grime, edge
  lift, then the roughness floor. The Meta (lightmap bake) pass sees the base albedo only (it has no world data).
- SRP Batcher: every material property sits in `UnityPerMaterial` in `ZUSurfaceInput.hlsl`, identical in all passes;
  instancing as Lit (`multi_compile_instancing`, `renderinglayer`, DOTS props declared).

## ZU/Water

One `UniversalForward` pass, queue Transparent, `Blend SrcAlpha OneMinusSrcAlpha`, **ZWrite Off**, Cull Back; no shadow
casting. No depth write because the plane is drawn after the opaque depth it reads, and writing it would occlude later
transparents (particles, splashes, hero effects) with a flat sheet; nothing needs to be occluded by water that draw order
doesn't already handle. Meshes must be flat horizontal planes (Y up); everything is driven by world position, so a
2 km plane tiles stably and needs no mesh UV.

| Property | Default | Meaning |
| --- | --- | --- |
| `_ShallowColor` | (0.22, 0.55, 0.60) | body colour at zero depth; also the absorption tint of what shows through |
| `_DeepColor` | (0.02, 0.12, 0.20) | body colour at `_DepthMax` |
| `_DepthMax` | 6 m | vertical depth at which the colour is fully deep and 5 % of the bottom still shows (`exp(-3 depth/_DepthMax)`) |
| `_Smoothness` | 0.93 | sun / light lobe sharpness (GGX); eases to 85 % far away as the normals flatten |
| `_WaveScale` | 6 m | longest analytic wavelength (the other three are 0.57x, 0.31x, 0.17x) |
| `_WaveSpeed` | 1 | time multiplier (deep-water dispersion: long waves travel faster) |
| `_WaveStrength` | 1 | analytic wave slope amplitude (<= ~0.14 per wave at 1) |
| `_NormalMap` + `_NORMALMAP` | none | optional tiling normal map, two scrolling layers (1x and 2.37x `_NormalTiling`) added as slopes on top of the analytic waves |
| `_NormalTiling` | 0.12 repeats/m | normal-map tiling in world metres |
| `_NormalStrength` | 0.6 | normal-map layer strength |
| `_FarDistance` | 400 m | the wave normals fade to flat between 20 % and 100 % of this (no far aliasing) |
| `_FoamColor` | (0.9, 0.95, 0.95) | shoreline foam (lit as a white Lambert surface: SH + shadowed sun) |
| `_FoamWidth` | 0.5 m | foam band, in vertical depth; animated two-octave noise |
| `_RefractionStrength` | 0.06 | screen-space displacement of the opaque texture by the normal, scaled by depth (shallow bottoms barely shift) and perspective; a displaced sample that lands on something above the surface falls back to the straight one |
| `_SceneOff` + `_ZU_SCENE_OFF` | off | force the no-scene fallback |

Model: depth = the scene's world position rebuilt from `_CameraDepthTexture` (`ComputeWorldSpacePosition`, URP's own
helper, same screen-UV convention as `SampleSceneDepth`), taken vertically below the surface, so it is view-angle
independent. Fresnel (F0 0.02, Schlick) splits transmitted = refracted scene colour x transmittance x tint + body colour
x (SH ambient + shadowed Lambert sun + additional lights) x (1 - transmittance), from reflected = reflection probes /
sky through `GlossyEnvironmentReflection` (read with a 35 %-flattened normal and with the reflection vector kept above
the horizon) x URP's `EnvironmentBRDFSpecular`, plus GGX sun + additional lights with a Schlick grazing term. Then foam,
then the soft intersection (the surface melts into the bottom over the first 6 cm of depth; foam still covers the edge),
then fog. Main-light shadows: real (shadow map sampled directly, cascades, soft tiers); light layers, cookies, Forward+
and APV all as in Lit. Fallback: when `_ZU_SCENE_OFF` is set, or `_CameraDepthTexture` / `_CameraOpaqueTexture`
TexelSize reads as unbound (<= 8 px, which also covers URP's placeholder textures), the water is opaque deep water
with reflections and sun, no refraction, no foam - never black.

Keywords: `_NORMALMAP`, `_ZU_SCENE_OFF`; pipeline: main light shadows / cascade / screen, additional lights + shadows,
soft shadow tiers, Forward+, reflection probe blending / box projection / atlas / rotation, light cookies, light layers /
rendering layers, APV, SH mixed/vertex, fog, instancing. Not: SSAO on the water (transparent), lightmaps, SSR.

## Verification (Unity 6000.6.4f1, D3D11, URP 17.6, editor CLI pipeline server)

- Import: `ShaderUtil.GetShaderMessages` = 0 messages and `ShaderHasError` = false for both shaders (ZU/Surface: 7
  passes, ZU/Water: 1).
- `unity command zu_shader_variants`: 15 keyword sets compiled synchronously through `ShaderUtil.CompilePass`
  (all ok, 0 messages): ZU/Surface ForwardLit x4 (cascaded soft shadows + Forward+ + additional light shadows + SSAO +
  light layers + cookies + DBuffer MRT3 + LOD fade + rendering layers + fog; vertex lights + APV L2 + shadowmask + probe
  atlas; DEBUG_DISPLAY + screen-space irradiance + screen shadows + alpha test + transparent premultiply; lightmaps +
  directional + SSR + SH mixed), ShadowCaster (punctual + alpha test + LOD fade), GBuffer x2 (octahedral normals, render
  pass, DBuffer, APV L1, rendering layers; lightmaps + shadowmask + SS irradiance), DepthOnly, DepthNormals x2 (write
  smoothness + oct + rendering layers; alpha test + SSR-contribute-transparent off), Meta (editor visualisation),
  MotionVectors (precomputed velocity); ZU/Water x3 (normal map + full lighting set; scene-off + APV + atlas; screen
  shadows + SH vertex).
- `unity command zu_shader_check`: renders `Screenshots/shader_check_*.png` (gitignored) from an isolated
  PreviewRenderUtility scene with procedural brick albedo / normal / mask (R metallic 0, G AO, A smoothness) and meshes
  built with the vertex contract. Looked at and sampled numerically (PIL):
  - `surface_1_overview.png`: seven boxes (floor slab, grounded long wall with a thin trim on top, grounded side wall,
    pillar, floating crate, low block) + a non-box ramp. Every box edge and corner reads rounded with the lifted highlight,
    no seams where the band starts, no black / NaN pixels at corners; the thin trim's bevel is capped; the ramp has no
    bevel; the normal map shows through the bevel.
  - `surface_0_layers_off.png`: same view, all ZU strengths 0 - hard 90-degree edges, lighter mortar (no cavity),
    glossier (no roughness floor). The pair is the before / after.
  - `surface_2_grazing.png`: low view along the long wall: bevel bands on the wall top and the trim at grazing angle.
  - `surface_3_corner.png`: close on the pillar: vertical and top edges bevelled, corner diagonal clean, normal map intact.
  - `surface_4_wallfoot.png`: eye level in front of the long wall's foot: no bevel along the foot (grounded wall rule);
    grime at the defaults is a 10-25 % darkening of the bottom brick row (subtle on purpose, as in the web game).
  - `surface_5_layers_strong.png`: `_Grime` 1 over 2.5 m, `_Macro` 0.8 at 4 m: wall feet darken toward the foot (side wall
    luminance ratio vs overview 0.98 at the top -> 0.68 at the foot), floor mottles +/-20-35 % in 4 m cells, the crate
    (grime weight 0) and the trim stay clean.
  - `water_1_normalmap.png` / `water_2_procedural.png`: a beach sloping into a 60 x 60 m plane with a pier post. The
    preview camera did provide the depth / opaque textures, so these show the real path: bricks refracted and tinted
    through shallow water, deeper water toward the camera, a foam band along the shoreline, sun glitter, wave normals
    (map + analytic / analytic only).
  - `water_3_fallback.png`: `_ZU_SCENE_OFF`: opaque deep water with the sky reflection and sun, no refraction, no foam.
- No play mode, no scene or project-setting changes; the test materials and textures are in-memory only (nothing was
  created under `Assets/ZU/Shaders/_test`).
