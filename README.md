# ZENITH//UMBRA - Unity edition

A rebuild of [ZENITH//UMBRA](https://github.com/Everaldtah/zenith-umbra), the anime hero shooter, in Unity 6 (URP). It
targets more detailed Overwatch-scale maps, physics and hero models. This is a separate project with its own repo and
its own desktop app, **Zenith Umbra Unity**, so the two versions can be played side by side. The original
TypeScript/Three.js game stays in its own repository and is not changed by this one.

## What is in it

- **Simulation** (`Assets/ZU/Sim`): the game rules ported to C# field for field from the TypeScript source, with no
  UnityEngine dependency.
  - Movement, combat, projectiles, objectives (control, push, point), all 15 heroes' kits, summons, bot AI, navigation.
  - **Stadium** (rounds, the Armory, hero powers) and the **Operation Starfall** campaign (encounters, waves, five
    bosses, Qel'Varis).
  - Headless tests (`tools/simtest`): 10-bot matches on every map, a full Stadium match, all five campaign levels.
- **Heroes:**
  - All 16 (15 + Haruto) with the HD Tripo meshes on the game rig and humanoid avatars.
  - 180 retargeted animation clips and a locomotion / combat Animator driven by the simulation.
  - Hair and cloth dynamics (`Assets/ZU/Dynamics`: Burst-compiled Verlet chains, body colliders, skirt rings).
- **Maps:**
  - The TS game's CC0 PBR surface sets and painted skies, with HDRI lighting, on the ZU/Surface shader (painted bevels,
    grime, cavity, macro variation).
  - A world beyond the walls (`Env/OuterWorld`): terrain into mountains, procedural skylines in each map's style,
    landmarks, the Tripo props, a harbour sea with ZU/Water, a cloud sea, and space for the campaign.
- **Feel:**
  - Combat effects (the TS Fx.ts port) and the recorded sound bank, mixed like the desktop edition (rolloff, air
    absorption, occlusion, footsteps by surface, voice lines).
  - An Overwatch-style HUD and the main menu: hero showcase, modes, competitive ranks per role.

Still to come: online play (the TS co-op / PvP netcode), the hero-specific ultimate showpieces (seal storm, koi dragons,
chain cage, puppet swarm), ragdolls.

## Play it

The desktop app installs to `%LOCALAPPDATA%\Programs\ZenithUmbraUnity` with **Zenith Umbra Unity** shortcuts on the
Desktop and in the Start menu. To build and install it from the running editor:

```bash
tools/build_app.sh 0.1.0     # zu_app_setup -> async Win64 build -> tools/install_app.ps1
```

## Layout

| Path | What |
|---|---|
| `Assets/ZU/Sim` | the game rules (C#), a line-by-line port of `zenith-umbra/src/game`, `src/ai`, `src/engine`, `src/campaign` |
| `Assets/ZU/Game` | the Unity side: match runner, views, environment (`Env/`), effects (`Fx/`), audio (`Audio/`), UI (`UI/`), career |
| `Assets/ZU/Dynamics` | hair and cloth (Burst / Jobs) |
| `Assets/ZU/Shaders` | ZU/Surface, ZU/Water, ZU/FxAdditive |
| `Assets/ZU/Editor` | the editor commands the CLI drives (`zu_*`: import heroes / anims / props / env, previews, captures, scenes, the app) |
| `Assets/ZU/Resources/ZUData` | game data exported from the TypeScript source (`tools/export`) |
| `tools/export` | data, environment (`export_env.py`), audio (`export_audio.py`), glTF factors |
| `tools/blender` | GLB to FBX conversion (`glb2fbx.py`) and texture fixing (`fix_textures.py`) |
| `tools/simtest`, `tools/dyntests`, `tools/unitycheck` | headless builds and tests with the .NET SDK bundled in the editor |
| `tools/mapshots.sh` | review captures of maps from the running editor |
| `docs/PORTING.md`, `docs/DYNAMICS.md` | the porting rules, the dynamics design |

## Requirements

- Unity 6000.6.4f1 with URP 17.6 (the Windows Mono player; IL2CPP is not installed).
- The editor is driven from the [Unity CLI](https://docs.unity.com) through the `com.unity.pipeline` package
  (`unity command ...`, `unity mcp`).
- Blender 5 for the model conversions.
- Licence-restricted animation sources (Mixamo, Kevin Iglesias) are built locally from a zenith-umbra checkout and
  never committed (`Assets/ZU/Art/Anim/Mixamo|Kevin`).
