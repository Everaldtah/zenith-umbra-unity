# ZU.Dynamics - hair and cloth for the Unity edition

A position-based (Verlet / XPBD-style) bone-chain solver for hair, tufts, sleeves, skirts and capes, ported from the
TypeScript game's `Animator.dynamics()` (same tables, same meaning of every number) and then hardened: fixed 120 Hz
sub-steps on an accumulator, exact segment lengths, a coupled skirt ring (neighbour + diagonal bands), collision that
wins over the cone limit, frame-level teleport detection, a NaN guard, LOD, and one Burst job for every hero on screen.

Staged outside `Assets/` on purpose. To install: move `Runtime/` to `Assets/ZU/Dynamics/` (the `.asmdef` inside it
references `Unity.Burst` and `Unity.Collections`; `Unity.Mathematics` is a built-in module in 6000.6). `tests/` stays out
of Unity.

## Files

| Path | What |
|---|---|
| `Runtime/ZU.Dynamics.asmdef` | assembly: Burst + Collections, unsafe code on |
| `Runtime/Core/DynTypes.cs` | blittable data: `DynKind`, `BodyCol`, `DynParams`, `Capsule`, `Sphere`, `CharFrame`, `ChainDesc`, `LateralPair`, `DynSettings`, `DynBatch` (pointers) |
| `Runtime/Core/DynTables.cs` | the TS tables: `DYN`, `DYN_CLASS`, `HERO_CLASS`, `HERO_DYN`, `DYN_VMAX`, `COLL`, `SKIRT_RING` (+ `SKIRT_DIAG`), `KindOf`, `Resolve(heroId, kind, prefix)` |
| `Runtime/Core/DynMath.cs` | `FromTo`, `ConeLimit`, capsule `PushOut` / `Penetration`, `YawAbout` |
| `Runtime/Core/DynLayout.cs` | packs `CharSpec`s (chains, lengths, bind limits, ring pairs) into the flat batch layout |
| `Runtime/Core/DynCore.cs` | the solver: `SolveCharacter`, `Schedule` (fixed-step accumulator), `Gust` |
| `Runtime/Unity/DynJob.cs` | `[BurstCompile] IJobParallelFor` over characters |
| `Runtime/Unity/ZuDynamicsManager.cs` | static batcher: NativeArrays, one job per frame, LOD, wind, settings |
| `Runtime/Unity/ZuDynamics.cs` | the MonoBehaviour: rig discovery, colliders, gather / apply, `Teleport()` |
| `tests/` | console harness (`run.ps1`): the solver core on synthetic rigs, no Unity |

The core (`Runtime/Core`) depends only on `Unity.Mathematics` and works on raw pointers, so the same code runs inside the
Burst job and in the test harness (pinned `Marshal.AllocHGlobal` memory there, `NativeArray.GetUnsafePtr` in Unity).

## Attaching it to a hero

1. The hero prefab must be in its bind pose when instantiated (chain rest lengths, directions and the collider limits are
   read from the transforms in `Awake`). Bones are found by name under the component: body bones `hips, spine, chest,
   neck, head, upperarm_L/R, forearm_L/R, hand_L/R, thigh_L/R, shin_L/R, foot_L/R`, chains `<prefix>_1..4` with
   prefixes `hair_B, hair_L, hair_R, hair_T, skirt_F, skirt_L, skirt_B, skirt_R, cape_B, sleeve_L, sleeve_R` (the highest
   index is the non-deforming tip marker; a prefix needs at least `_1` and `_2`). `.` in names is read as `_`.
2. Add `ZuDynamics` to the hero's root (the transform the game moves and turns; +Z forward, +Y up, uniform scale) and set
   `heroId` (`kaien`, `tomoe`, `mirei`, ...). The Animator can be in any update mode; the solver runs in `LateUpdate`
   (execution order 500) after the body is posed, and writes the chain bones' world rotations root to tip.
3. Each frame the game sets `grounded` (and optionally `overrideVelocity` + `velocity` instead of the root's own
   frame-to-frame motion: it only decides "at rest" vs "moving" for the rest drag).
4. Call `Teleport()` on respawn / teleport. A jump of more than 1.5 x the height in one frame is caught anyway.

```csharp
var dyn = hero.AddComponent<ZU.Dynamics.ZuDynamics>();
dyn.heroId = "kaien";
dyn.grounded = actor.Grounded;        // per frame
dyn.Teleport();                        // on respawn
dyn.quality = ZuDynamics.QualityMode.Auto;
dyn.enabled = false;                   // chains freeze on the animated pose; re-enabling resets them

ZU.Dynamics.ZuDynamicsManager.Wind = new float3(3, 0, 0);   // map wind (acceleration, before each kind's `wind` scale)
ZU.Dynamics.ZuDynamicsManager.GustScale = 1f;               // the TS breeze (0 = off)
ZU.Dynamics.ZuDynamicsManager.LodOrigin = cam.transform;    // default: Camera.main
ZU.Dynamics.ZuDynamicsManager.LodHalfDistance = 30f;         // 60 Hz sub-steps beyond this
ZU.Dynamics.ZuDynamicsManager.LodOffDistance = 80f;          // animated pose only beyond this
ZU.Dynamics.ZuDynamicsManager.Settings.iters = 3;            // constraint rounds per sub-step
```

### Public API

`ZuDynamics` (component): `heroId`, `quality` (Auto / Full / Half / Off), `grounded`, `useGround`, `colliderScale`,
`particleRadiusFrac`, `measuredRadii` (bone name -> radius in model units, clamped to 0.5..1.45 x the proportional
default like the TS), `overrideVelocity` + `velocity`, `Teleport()`, `Rebind()`, `Bound`, `ChainCount`, `Height`.
Enabling / disabling registers / unregisters it with the manager.

`ZuDynamicsManager` (static): `Wind`, `GustScale`, `SubstepHz` (120), `MaxStepsPerFrame` (16: a frame longer than 16
sub-steps drops time), `Settings` (`iters`, `vmax`, `restDragMul`, `g`, `extrapolate`), `LodOrigin`, `LodHalfDistance`,
`LodOffDistance`, `InstanceCount`, `SimTime`. It creates a hidden runner object on first use and batches every instance
into one `DynJob` (parallel over characters) per frame; spawns and despawns repack the arrays while carrying every
surviving hero's state across (no pops).

## How a sub-step works (per chain, root to tip)

Everything is solved in world space on the particle at the far end of each segment; segment 0 starts at the anchor
(`<prefix>_1`'s position, swept linearly from last frame to this one).

1. **Inertia carry** - the chain is translated by `(1 - inertT)` of the anchor's move and turned about the anchor by
   `(1 - inertR)` of the hero's yaw change this step; `x` and `prev` move together so the particle keeps its velocity.
   The remaining `inertT` fraction is the anchor motion the chain *feels* (Kawaii Physics' world damping).
2. **Verlet**: `v = clamp(x - prev - vFelt, DYN_VMAX * h) + vFelt`, `v *= keep`, `keep = (1 - drag)^(h * 60)`;
   gravity `g * grav * h^2`; wind `wind * kind.wind * h^2`. `drag` is `x 1.6` when the hero stands still (TS rest drag).
3. **Stiffness**: `x = lerp(x, target, 1 - (1 - stiff)^(h * 60))` where `target = parentParticle + L * rigidW` and
   `rigidW` is the segment's animated direction *rotated by the bend of the segments above it*, so a bent root carries
   the tip (a chain, not a rod). `stiff` / `drag` are per particle on the `t^0.8` root->tip curve.
4. **Collision**: each capsule in the kind's set, swept to this sub-step, with radius `min(r + particleR, rmax)` where
   `rmax` is 0.97 x how far the particle sat from that capsule in the bind pose (cloth modelled inside a wide hip capsule
   isn't shoved out every frame); the face guard sphere for head-anchored hair; the ground plane.
5. **Cone + length**: the segment direction is pulled into the `maxA` cone about `rigidW`, then the particle is placed at
   exactly `L` from its parent.

Then `iters` (3) Gauss-Seidel rounds over the whole character: lateral bands (skirt ring neighbours within
0.85..1.15 x bind spacing, opposite panels never closer than 0.6 x) -> cone (first round only) -> collision -> length. The
last operation is always the length projection, so lengths are exact; collision is applied after the cone, so when a leg
and the cone disagree the leg wins (cloth never through the legs, the cone is cosmetic).

Output: the sim chain shifted onto the frame's anchor (only directions reach the bones, so this is exact), extrapolated
by the accumulator remainder (`alpha` of a step) so there is no visible sub-step lag at 144 fps, lengths re-projected,
then blended toward the animated pose by `1 - simW`. The bones are aimed with a from-to rotation on top of their
animated rotation (minimal twist), root to tip.

## Parameter mapping from the TS tables

All of `Animator.ts` lines 36-70 live in `DynTables.cs` with the same values:

| TS | here | meaning |
|---|---|---|
| `DYN[kind].stiff: [root, tip]` | `DynParams.stiffRoot / stiffTip` | pull toward the animated pose per 1/60 s; converted with `1 - (1 - s)^(h*60)` |
| `drag: [root, tip]` | `dragRoot / dragTip` | velocity lost per 1/60 s; `(1 - d)^(h*60)` |
| `grav` | `grav` | g multiplier (g = 9.8, `DynSettings.g`) |
| `maxA` | `maxA` | cone limit (rad) per joint about the carried animated direction |
| `wind` | `wind` | scale on the wind acceleration |
| `inertT`, `inertR` | `inertT`, `inertR` | fraction of the hero's move / turn the chain feels |
| `simW` | `simW` | final blend toward the animated pose |
| `cols` | `colMask` | bit mask over `BodyCol` |
| `DYN_CLASS` x `HERO_CLASS` | `Resolve()` | stiff x, drag x (capped 0.95 / 0.9), maxA x, inertT x (capped 1) |
| `HERO_DYN` | `Resolve()` | per-hero overrides (hibiki tuft inertia, nocturne skirt simW, kaien / seiran sleeve maxA, susanoo cape + skirt) |
| hair_L / hair_R maxA 0.45 | `Resolve()` | front locks beside the face, unless the hero overrides maxA |
| `DYN_VMAX = 9` | `DynSettings.vmax` | character-relative speed cap (m/s), applied to the Verlet velocity |
| `COLL` fractions | `DynTables.COLL` | capsule radii as fractions of the height (= head rest y x 1.08); head sphere 0.85 r above the joint |
| face guard | `ZuDynamics.FaceGuard` | centre head + 0.6 r up + 0.35 r forward, radius 0.9 r; per-particle limit `min(r, 0.97 x bind distance)` |
| `SKIRT_RING` +-25 % | `LAT_LO / LAT_HI` = 0.85 / 1.15 | tightened so the ring reads as a sheet; plus `SKIRT_DIAG` (F-B, L-R) >= 0.6 x so panels can't fold through the ring |
| rest drag x 1.6 below 0.3 m/s | `DynSettings.restDragMul`, `CharFrame.moving` | same |
| 120 Hz, <= 5 sub-steps of dt/steps | fixed h = 1/120 on an accumulator, <= 16 steps | frame-rate independent; a 0.1 s hitch = 12 steps |
| 1.5 H anchor jump -> restart | `CharFrame.teleportDist` | checked per frame and per sub-step |
| 0.008 H particle radius | `particleRadiusFrac` | same |
| leg-driven skirt targets (0.35 side / 0.25 F-B slerp toward the thigh) | `ZuDynamics.Gather` | same |

One deliberate deviation: in the TS, `dynFor` spread the `HERO_DYN` override in and then overwrote `stiff` / `drag` with
the base table, so Susanoo's `drag: [0.12, 0.15]` / `[0.11, 0.13]` never applied. `Resolve()` honours them (it is clearly
what the comment intends). Everything else resolves to the same numbers.

## Tests (`tests/run.ps1`, .NET 8 SDK bundled with the editor; links the real `UnityEngine.MathematicsModule.dll`)

Single-threaded, no Burst, synthetic rigs (a kaien-like hero: hair_L/R/T, sleeves, cape, four skirt panels, 3 segments
each; TS-proportioned capsules; an analytic 3 Hz running gait with knee bend). Latest run:

| test | result |
|---|---|
| hanging chain settles | tip at 0.900000 m from the anchor (rest 0.9), residual speed 1e-13 m/s, drift over 2 s at rest 6e-15 m |
| lengths preserved (1200 frames, run + jumps + random yaw) | max relative error 3.4e-6 (sim and shown) |
| skirt ring vs running / jumping / turning legs, 2000 frames | max penetration 0.178 mm vs limit 1.36 mm (0.1 x particle radius 13.6 mm), 0 frames over; ring band excess 0.6 % |
| Teleport() over 5 m | particle speed after 8.9e-3 m/s (gravity's first step alone is 0.16 m/s per frame), 0.12 mm off the pose |
| 5 m jump without Teleport() | auto-reset: 8.9e-3 m/s |
| 1.5 m jump in one frame (90 m/s, under 1.5 H) | the chain is dragged along at 72 m/s (it cannot stretch), swing-back peaks at 15.8 m/s relative and is under the 9 m/s cap within 0.6 s; lengths 4e-6, finite |
| 30 vs 144 fps, 60 vs 144 fps, compared mid-motion at 2.4 s | 8.1 mm / 7.7 mm max particle difference |
| 0.1 s hitch at 60 fps | 12 fixed steps, 1.28 m/s after it, 0.009 mm difference 1.4 s later |
| 20000 frames of random violent motion (hitches to 0.25 s, 3 m jumps, random spins, 30 m/s^2 wind, random Teleports) | all finite, NaN guard fired 0 times |
| determinism | bit-identical state on repeated runs |
| timing, managed single thread | 10 heroes / 100 chains / 300 particles / 180 ring pairs: ~380 us per 60 fps frame including input generation |

Burst compiles the same code with the parallel-for over characters; expect well under 100 us for 10 heroes on a Ryzen 5
5500 at 120 fps (1 sub-step per frame there).

## Known limits / notes

- Bind pose: the rig is read in `Awake` from the prefab's transforms. If a prefab is saved mid-animation, call
  `Rebind()` after posing it. The humanoid Animator's own retarget pose does not matter (chains ride their parent bone).
- Uniform root scale only (`lossyScale.y` is the model scale, like the TS `scale`).
- The yaw inertia (`inertR`) is about the world up axis, as in the TS; pitch / roll turns of the root are felt fully.
- Capsules are swept linearly through the frame per sub-step; a leg moving further than its radius plus the particle
  radius in one sub-step (> 12 m/s knee speed at 120 Hz) could in principle push a particle out of the wrong side.
- Chains are bone chains (3 particles each); the skirt is four coupled panels, not a mesh cloth. Fabric detail between
  panels comes from skinning.
- `measuredRadii` is where the rigger's per-mesh collider measurements go (the TS `ModelInfo.colliders`); none are
  exported for Unity yet, so radii are proportional to the height.
- Transform reads / writes are on the main thread (about 30 reads + 33 writes per hero). A `TransformAccessArray`
  version would move them into jobs if ever needed; at 10 heroes it is not worth the complexity.
- Not yet done: a Unity-side smoke scene (the editor is owned by another session), and hooking `grounded` / `Teleport()`
  into `ActorView`.
