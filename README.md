# ZENITH//UMBRA - Unity edition

A rebuild of [ZENITH//UMBRA](https://github.com/Everaldtah/zenith-umbra), the anime hero shooter, in Unity 6 (URP). It
targets more detailed Overwatch-scale maps, physics and hero models. This is a separate project with its own repo and
its own desktop app, **Zenith Umbra Unity**, so the two versions can be played side by side. The original
TypeScript/Three.js game stays in its own repository and is not changed by this one.

## Status

Work in progress.

- **Simulation:** ported to C# (`Assets/ZU/Sim`, no UnityEngine dependency), field for field from the TypeScript source.
  - Done: movement, combat, projectiles, objectives, all 15 heroes' kits, summons, bot AI, navigation, match setup.
  - Still to port: Stadium, ranks, the Starfall campaign.
  - Headless 10-bot matches pass on all 8 playable maps (`tools/simtest`).
- **Presentation:** heroes, maps, effects, UI and audio in Unity are next.

## Layout

| Path | What |
|---|---|
| `Assets/ZU/Sim` | the game rules (C#), a line-by-line port of `zenith-umbra/src/game`, `src/ai`, `src/engine` |
| `Assets/ZU/Resources/ZUData` | game data exported from the TypeScript source (`tools/export`) |
| `tools/export` | `node tools/export/export_data.mjs <zenith-umbra repo> Assets/ZU/Resources/ZUData` |
| `tools/simtest` | headless build and tests with the .NET SDK bundled in the Unity editor (`build.ps1`, `run.ps1 smoke` / `aimatch`) |
| `tools/blender` | GLB to FBX conversion for Unity's humanoid importer |
| `docs/PORTING.md` | the porting rules |

## Requirements

- Unity 6000.6.4f1 with URP 17.6.
- The editor is driven from the [Unity CLI](https://docs.unity.com) through the `com.unity.pipeline` package (`unity command ...`, `unity mcp`).
- Licence-restricted animation sources (Mixamo, Kevin Iglesias) are built locally and never committed.
