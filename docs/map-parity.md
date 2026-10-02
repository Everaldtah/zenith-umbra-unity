# Map parity: the PC game's MapScene vs the Unity LevelView / Env

Inventory (2026-10-02, evera-75) of what the TS client draws for a map (`src/render/MapScene.ts`, `Surfaces.ts`,
`EnvLight.ts`, `PostFx.ts`, `Game.ts` post settings) against `Assets/ZU/Game/LevelView.cs` + `Env/**`. The playable
boxes come from the shared map data in both, so collision matches; the gaps are in what is drawn on and around them.

Status: **gap** = missing or different in Unity, port it; **ok** = equivalent; **extra** = Unity-only, flagged.

## Arena geometry and surfaces

| # | Item | PC game (TS) | Unity before | Status |
|---|------|--------------|--------------|--------|
| 1 | Boundary walls | Kagura, Lantern, Starfall, Foundry, Training have 12-14 m perimeter walls (unmaterialled = `wall` texture); drawn | hidden (`IsBoundary`), the outer world shows instead | gap |
| 2 | Floating-island rock skirt (Cloudstep) | 7-sided taper cylinder (r 1 -> 0.35) scaled (max·0.55, min·0.5+3, min·0.55) at y -(min·0.25+1.5), `wall` texture | 9-sided cylinder, other proportions, `rock` | gap |
| 3 | Boxes, decor, ramps merged per material, bevels, grime | MapScene + Surfaces.ts | SurfaceMesh + ZU/Surface | ok |
| 4a | `window` | sun < 2: lit paper (#ffe2a8, emissive #ffc46b x1.1); else day glass (#7d97ad, emissive #ffd49a x0.12, metal .35, rough .15) | amber glow by OuterWorld theme `lit`, dark base | gap |
| 4b | `paint` (road lines) | #f2cf5b, emissive x0.08 | palette tan | gap |
| 4c | `accent` | wall texture tinted tint->white 40%, emissive tint x0.25, rough .4 | flat tint | gap |
| 4d | `glass` | tint, opacity .35, emissive tint x0.3, rough .05 | flat pale blue | gap |
| 4e | ground / wall / roof / wood / trim / rock | map texture + CC0 PBR set (Surfaces manifest) | ZUEnv/<map>_<kind> from the same manifest | ok |
| 5 | Sakura props | the model + a 70-blossom instanced canopy | model only | gap |

## Gameplay objects (missing in Unity)

| # | Item | PC game (TS) | Unity before | Status |
|---|------|--------------|--------------|--------|
| 6 | Capture point | torus r 6 in the owner's colour, a disc shader (capture progress arc + scrolling rings), a 40 m additive beam | a static flat disc in the accent colour | gap |
| 7 | Mikoshi Rush float + route (Kagura, Foundry, Mile, Gulch) | payload prop (prop_kagura_mikoshi / map.payload, payloadYaw) bobbing on push.pos, facing travel, hidden when the camera is inside; a lit tube along map.path | **nothing drawn** | gap |
| 8 | Health packs (8-12 per map) | pedestal, teal rim, floating spinning cross (big: halo), ring that refills while respawning | **nothing drawn** | gap |
| 9 | Jump pads (Training) | ring, disc, bobbing arrow along the launch vector, slow spin | nothing | gap |
| 10 | Ult packs (Training, newer TS data) | gold pedestal, spinning cube | not in the exported data yet | later |
| 11 | Tint light at the point | PointLight(tint, 30, 40 m, decay 1.6) 5 m above it | none | gap |

## Atmosphere, lighting, post

| # | Item | PC game (TS) | Unity before | Status |
|---|------|--------------|--------------|--------|
| 12 | Fog | linear, the map's own near / far (Hanabi 55-180, Kagura 90-260, Foundry 45-170 ...); background = fog colour | near x1.6, far >= 1035 m (pushed out for the outer world): almost no haze in the arena | gap |
| 13 | Sky | painted panorama on a cylinder (R 420, mirrored x2), top cap ambient-sky x0.8, bottom cap fog colour, no fog on it | the panorama re-projected to a Skybox/Panoramic | ok (check the horizon in captures) |
| 14 | Cloud sea (Cloudstep) | fbm plane at y -22, fog colour -> white (rift: violet), fading out by 450 m | OuterWorld.Clouds | check |
| 15 | Harbour water (Hanabi) | 700 m rolling plane, deep #1d3f73 / shallow #3f86b8, tint glints, foam streaks, fogged | ZU/Water sea + far shore | check |
| 16 | Ambient particles | petals / rain / sparks / embers / motes / dust (1400; rain 5000) over the arena | none | gap |
| 17 | Lights | hemisphere (map.ambient) + sun + HDRI env (1.6 x mood x 0.55) | EnvKit models the same | ok |
| 18 | Bloom | strength .55, threshold .82; day maps (sun >= 2.1) .38 / .97; map.bloom overrides | fixed 1.05 / .38 | gap |
| 19 | Colour grade | per-map GRADES: contrast, vibrance, split tone, vignette | one generic grade | gap |
| 20 | Tone mapping | Neutral, exposure .95 x gamma | Neutral, post exposure log2(.95) | ok |

## Unity-only extras (kept, flagged)

| # | Item | Note |
|---|------|------|
| 21 | Far-only depth of field | the TS has none; it never touches the play space |
| 22 | OuterWorld (terrain, skyline rings, landmarks, trees, showpieces, sea shore) | the PC game shows the painted sky above its walls. With the TS fog distances most of it fades out; behind the 5 maps' boundary walls it is hidden. On the open maps (Hanabi, Cloudstep, Mile, Gulch) it adds a skyline the PC game doesn't have. Proposal: off by default for parity, an Options toggle to turn it on - lead's call |
| 23 | Real-time reflection probe | rendering detail, keep |

## Not in scope here

- Training Grounds' Hero Range lane and Spar Arena (TrainingScene.ts) are new web features whose sim (herorange.ts,
  spar.ts) the Unity port doesn't have yet.
