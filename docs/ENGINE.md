# ZU.Engine - the frame-level performance layer of the Unity edition

The TypeScript game's `src/engine/` (its "engine core": the layer between the Game and three.js that decides when a
frame runs, what moment of the simulation it shows, at what resolution it is drawn and how often each skeleton is
re-posed) ported to Unity as the assembly `ZU.Engine` (`Assets/ZU/Engine/`), plus the parts a browser tab could not have:
a Governor that scales quality by what the frame actually waits on, a hardware profile and a memory watch. It references
only the UnityEngine modules and the URP / Core RP runtime - nothing from `ZU.Sim`, `ZU.Game`, `ZU.Dynamics` or `ZU.Net`
- so it compiles alone (`~/Projects/zu-qa/engine-check/build.ps1`, through `lg`) and the game assemblies reference it,
never the other way round.

Where the web game pulled the engine into its own loop, Unity owns the loop: `PerfDriver` (a hidden MonoBehaviour made
before the first scene loads, execution order -10000) ticks `Perf` once a frame before any script runs, and `Perf` is
the static facade everything else calls.

## Files: TS -> Unity

| TS (`~/Projects/zenith-umbra/src/engine/`) | Unity (`Assets/ZU/Engine/`) | What |
|---|---|---|
| `FramePacer.ts` | `FramePacer.cs` | display refresh from the frame cadence, vsync-quantized deltas, the cap-divides-refresh rule (`VSyncDivisor`) |
| `DynamicResolution.ts` | `DynamicResolution.cs` | render scale follows the GPU's load only (exact port) |
| `GpuTimer.ts` | `FrameTimer.cs` | GPU / CPU frame times from `FrameTimingManager`, same EMA smoothing |
| `FrameGraph.ts` | `FrameGraph.cs` | the 160-frame ring + `FrameGraphElement` (UI Toolkit, Painter2D) |
| `EngineCore.ts` + `Game.ts` applySettings / applyScale / loop | `Perf.cs`, `PerfDriver.cs` | the wiring: `FrameDt`, the cap, URP renderScale = base x dynamic, the upscaler |
| `AnimBudget.ts` | `AnimBudget.cs` | animation LOD: skeleton update rate by screen size |
| `Broadphase.ts` | `Assets/ZU/Sim/Core/Broadphase.cs` | uniform grid under the Level's collision queries (in `ZU.Sim`: the sim must stay Unity-free) |
| `Interpolator.ts` | - | not needed: `MatchRunner` already snapshots and interpolates between fixed steps |
| `FsrPass.ts` | - | URP's own FSR 1.0 / STP upscaling filter, set by `Perf.SetUpscaler` |
| `Game.ts` SharpenShader (:54-61, :276, :325) | `SharpenPass.cs` + `Resources/ZUEngine/Sharpen.shader` | the native-scale image sharpening pass (the Options slider), `Perf.SetSharpen` |
| - | `Governor.cs` | bottleneck-directed quality ladder + memory pressure (no TS original) |
| - | `HardwareProfile.cs` | one-off machine snapshot: tier Low / Mid / High |
| - | `MemoryWatch.cs` | machine free RAM (kernel32) + Unity's memory counters |

Each `.cs` starts with the TS file's reasoning, condensed, and says where it deviates and why.

## Research

What the shipped games do, and what of it this layer takes:

- **Overwatch** runs its simulation on fixed 16 ms command frames separate from rendering and renders between ticks;
  effects are queued and processed once per frame under budgets. ZU already runs a fixed 120 Hz sim with interpolated
  rendering (`MatchRunner`); `Perf.FrameDt` feeds that accumulator a vsync-quantized delta so the shown instant never
  jitters by the frame stamps' noise.
  [Overwatch Gameplay Architecture and Netcode (GDC 2017)](https://gdcvault.com/play/1024001/-Overwatch-Gameplay-Architecture-and),
  [Edgegap: Overwatch 2016 netcode architecture](https://edgegap.com/blog/game-backend-deep-dive-overwatch-2016-netcode-architecture-rollback)
- **Rainbow Six Siege** renders fewer pixels and reconstructs over time (checkerboard rendering, up to ~50 % faster).
  In Unity that maps to URP's FSR 1.0 / STP upscaling filter plus a resolution scale that only drops when the GPU is the
  bottleneck (`DynamicResolution`).
  [Rendering Rainbow Six Siege (GDC 2016)](https://gdcvault.com/play/1022990/Rendering-Rainbow-Six-Siege),
  [slides](https://archive.org/download/GDC2016Mansouri/GDC2016-Mansouri.pdf),
  [Intel: checkerboard rendering for real-time upscaling](https://intel.com/content/dam/develop/external/us/en/documents/checkerboard-rendering-for-real-time-upscaling-on-intel-integrated-graphics.pdf),
  [Game Developer: how Rainbow Six Siege was rendered](https://gamedeveloper.com/programming/how-i-rainbow-six-siege-i-was-rendered)
- **Animation LOD**: small or off-screen characters re-pose less often (Unreal's Update Rate Optimisation / Animation
  Budget Allocator, as Fortnite runs it). `AnimBudget` does this per hero view; the Governor widens its thresholds
  when the main thread is the bottleneck.
- **Unity 6 extras**: `FrameTimingManager` tells CPU-bound from GPU-bound frames (what the Governor classifies on);
  batched GPU skinning (`MeshDeformation.GPUBatched`); GPU Resident Drawer occlusion culling (currently off);
  memory-pressure handling (`MemoryWatch` + Governor).
  [Frame Timing Manager](https://docs.unity3d.com/2022.3/Documentation/Manual/frame-timing-manager.html),
  [GPU Resident Drawer compatibility](https://docs.unity3d.com/Manual/urp/make-object-compatible-gpu-rendering.html),
  [Resolution scaling in Unity 6](https://docs.unity3d.com/6/Documentation/Manual/resolution-scale-introduction.html),
  [MeshDeformation.GPUBatched](https://docs.unity3d.com/ScriptReference/MeshDeformation.GPUBatched.html)

## APIs

### `Perf` (static; the facade)

```csharp
public enum Perf.Upscaler { Auto, Off, FSR, STP }

float  Perf.FrameDt            // this frame's sim delta: quantized unscaled dt, clamped by Time.maximumDeltaTime, x Time.timeScale (Time.deltaTime when off)
float  Perf.UnscaledFrameDt
float  Perf.BaseScale          // the settings render scale (0.5..2)
float  Perf.DynScale           // DynamicResolution's scale (1 when off)
float  Perf.RenderScale        // URP renderScale as applied = base x dynamic
string Perf.UpscalerActive     // "off" (no resampling) | "linear" | "fsr" | "stp"
float  Perf.Sharpen            // the Image Sharpening slider as a fraction 0..1
bool   Perf.SharpenActive      // the sharpen pass runs this frame: Sharpen > 0, RenderScale >= 0.99, shader loaded (independent of Enabled)
double Perf.RefreshHz          // the display's refresh: reported, then measured from the vsync cadence
bool   Perf.VSynced            // the cadence is vblank-locked (deltas are quantized)
double Perf.BudgetMs           // 1000 / (cap > 0 ? min(cap, refresh) : refresh)  (the TS updateDynRes)
double Perf.CpuMs              // the last frame's own main-thread work (FrameTimer main - present wait, else measured)
FrameTimer Perf.Timing;  FrameGraph Perf.Graph;  FramePacer Perf.Pacer;  DynamicResolution Perf.DynRes
PerfStats Perf.Stats           // readonly struct snapshot of all of the above + Governor / AnimBudget / MemoryWatch state

bool Perf.Enabled              // default true; false with -zu-engine=0. Off = Time.deltaTime, a plain cap, no dynres, no Governor
bool Perf.AllowAssetWrites     // default !Application.isEditor: URP asset properties are written only when true
void Perf.SetBaseScale(float scale)                       // 0.5..2; URP renderScale = base x dynamic
void Perf.SetCap(int fps, bool vsync = true)              // 0 = display based (vSyncCount 1); fps dividing the refresh + vsync = vSyncCount k; else vSyncCount 0 + targetFrameRate
void Perf.SetDynamicResolution(bool on, float min = 0.5f) // off resets the dynamic scale to 1 (TS applySettings)
void Perf.SetUpscaler(Perf.Upscaler mode, float sharpness = 0.9f)   // Auto = FSR below 0.99 scale, else bilinear; 0.9 = the web RCAS 0.25 stops (applies below native only)
void Perf.SetSharpen(float amount01)                      // the Image Sharpening slider / 100: the web's native-scale sharpen pass (shader amount = x 0.6), off below 0.99 scale
```

`Perf` owns `QualitySettings.vSyncCount` / `Application.targetFrameRate` only once `SetCap` has been called (until
then the settings code's own writes stand), and the URP asset's `renderScale` / `upscalingFilter` / `fsrSharpness`,
written only when they change (a renderScale write re-allocates the render targets).

`PerfStats` fields: `Enabled, VSynced, TimingSupported, BaseScale, DynScale, RenderScale, Upscaler, RefreshHz,
BudgetMs, GpuMs, GpuLast, CpuMs, CpuMainMs, CpuRenderMs, PresentWaitMs, FrameMs, TargetFrameRate, VSyncCount, FrameDt,
UnscaledFrameDt, Bottleneck, GovernorLevel, AnimUpdated, AnimHeld, MemAvailMB, Pressure, SharpenActive`.

#### The sharpen pass (`SharpenPass`, `Resources/ZUEngine/Sharpen.shader`)

The web ran its 5-tap unsharp mask (`out = clamp(c + (4c - neighbours) * slider/100 * 0.6, 0, 1)`) as the LAST pass of
its chain - after OutputPass (tone mapping + sRGB), the grade and SMAA - and only at render scale >= 0.99: below native
FSR's RCAS sharpens the upscaled picture. URP runs RCAS only below scale 1 too, so the slider used to do nothing at
native. The port:

- **Injection without a renderer asset** (renderer assets belong to other people): `Perf.Init` subscribes
  `RenderPipelineManager.beginCameraRendering`; for Game cameras with the slider > 0 and `RenderScale >= 0.99` it calls
  `camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass)`. URP empties its pass queue only at
  the end of each camera (`ScriptableRenderer.cs:1227`), so the enqueue survives into `AddRenderPasses`.
- **Pass order in URP 17.6** (`UniversalRendererRenderGraph.OnAfterRendering`): custom passes in
  `[AfterRenderingPostProcessing, AfterRendering)` are recorded after `PostProcess.RenderPostProcessing` (SMAA, DoF,
  TAA / STP, motion blur, bloom, UberPost = tone mapping + grade; all at `AfterRenderingPostProcessing - 1`) and before
  `RenderFinalPostProcessing` (FXAA / FSR RCAS / TAA sharpening, guaranteed to run after user passes) and the FinalBlit.
  `AfterRendering` is past the final blit (active target = backbuffer), so `AfterRenderingPostProcessing + 50` is the
  latest safe point, sorted after other owners' full-screen features (`FullScreenPassRendererFeature` defaults to +0).
  Like the web: after SMAA / TAA and the grade. Unlike the web: FXAA, if the project picks it, runs after the sharpen.
- **Gamma handling**: the camera colour after post-processing is LINEAR (the final blit / sRGB backbuffer encodes); the
  web sharpened the display-referred sRGB picture. In a linear project the shader converts the 5 taps `LinearToSRGB`,
  masks and clamps there, and returns `SRGBToLinear` - same numbers as the web. Gamma projects (`UNITY_COLORSPACE_GAMMA`)
  convert nothing.
- **Once per stack**: enqueued on every Game camera, recorded only where `UniversalCameraData.resolveFinalTarget` is
  true (the last camera of the stack) and the camera's `renderScale >= 0.99` (URP's `imageScalingMode` is internal; its
  `renderScale` is already snapped to 1 within 0.05). The blit goes camera colour -> a temp texture
  of the same descriptor (single-sampled, as URP's own post targets) and `resourceData.cameraColor = temp`, the URP 17
  pattern; the final post / final blit then read the sharpened picture. `requiresIntermediateTexture = true` keeps URP
  off the direct-to-backbuffer path while the pass is enqueued.
- **Not reproduced**: the web drew the first-person viewmodel after the sharpen at native (arms never sharpened); URP
  draws it inside the stack before post-processing, so it is sharpened too.
- Shader load: `Resources.Load<Shader>("ZUEngine/Sharpen")` on the first frame the slider is > 0 (the Resources folder
  keeps it in players; not `Shader.Find`). A failed load warns once and the pass stays off (`SharpenActive` false).
- Cost: one full-screen pass + one extra colour target at native (and, with FXAA off, the final blit URP adds once a
  user pass sits after post-processing). Nothing when the slider is 0 (nothing enqueued) or below 0.99 scale.

### `AnimBudget` (per-view animation LOD)

```csharp
AnimBudget AnimBudget.Shared
bool  enabled;  float tierScale                 // tierScale: the Governor's knob (>1 = more views on the reduced rates)
int   Updated, Held, LastUpdated, LastHeld      // this frame so far / the previous frame
AnimBudget.Slot NewSlot()                       // one per view, staggered phases
void  Begin(Camera cam)                         // once a frame (Step calls it with Camera.main on the first view)
float Step(Slot s, Vector3 feet, float height, float dt, bool always)   // the dt to pose with, or -1 = hold the pose
float Interval(Vector3 feet, float height)      // 0 every frame; 1/45 mid-sized; 1/30 far; 1/24 off screen
```

### `Governor` (static)

```csharp
public enum Governor.Bottleneck { None, Gpu, MainThread, RenderThread, Unknown }

Governor.Bottleneck Governor.Current   // what the frame waits on (~1 s EMAs; None = frame EMA <= budget x 1.05 or mostly present wait)
int    Governor.Level                  // ladder 0..3: lodBias = base x {1, .85, .72, .6}
float  Governor.LodBias                // QualitySettings.lodBias as applied
float  Governor.TierScale              // AnimBudget.Shared.tierScale as applied: {1, 1.5, 2.25, 3} (MainThread steps only)
MemoryWatch.Pressure Governor.Pressure
string Governor.LastAction;  double Governor.LastActionTime
event Action<MemoryWatch.Pressure> Governor.PressureChanged   // asset owners (FX, dynamics) shed load themselves
bool   Governor.Enabled                // default true; false with -zu-governor=0; inactive while Perf.Enabled is off. Off restores the knobs.
```

Rules: MainThread sustained 1.5 s with the frame EMA > budget x 1.08 steps the level up (tierScale + lodBias), at most
one step per 2 s; RenderThread the same but lodBias only; Gpu steps nothing (DynamicResolution owns pixels); one step
down when the frame EMA < budget x 0.85 for 4 s. The lodBias base is re-read whenever somebody else wrote the value, so a
quality change becomes the new base. Memory: `MemoryWatch.Sample()` every frame; Elevated for 5 s = one
`Resources.UnloadUnusedAssets()` (at most every 120 s); Critical = unload + `GC.Collect()` and a
`[ZU.Engine] memory critical: ... (next in 40 s; last unload freed 12 MB)` warning. The Critical interval is adaptive,
because on this shared PC the pressure usually comes from other processes (Editors, Chrome, agent sessions) and an
unload + GC that frees nothing of ours would only hitch the match: the process size (`SystemUsedMB`, else
`TotalUsedMB`) is noted when the action fires and checked at the next real sample ~2 s later; under 128 MB freed doubles
the interval (20 -> 40 -> 80 -> 160 s cap), 128 MB or more resets it to 20 s, as does the pressure returning to None. At
start-up it logs `HardwareProfile.Summary()` + `MemoryWatch.Summary()` once, and a Low hardware tier starts at L1.

### `HardwareProfile`

```csharp
HardwareProfile HardwareProfile.Current        // taken on first access (main thread)
enum Tier { Low, Mid, High }; Tier tier        // Low: VRAM < 3 GB or RAM < 8 GB or < 4 cores; High: >= 8 GB VRAM, >= 16 GB RAM, >= 8 cores
string gpuName, gpuVendor, cpuType, os;  GraphicsDeviceType api;  int vramMB, shaderLevel, cores, cpuMHz, ramMB;  bool compute, frameTiming;  float refreshHz
string Summary()                               // one log line
```

### `MemoryWatch` (static)

```csharp
enum Pressure { None, Elevated, Critical }     // Elevated: < 3 GB free or > 85 % in use; Critical: < 1.5 GB or > 93 %
bool  MemoryWatch.Sample()                     // at most twice a second (true when it sampled)
float AvailMB, TotalMB, LoadPct;  Pressure pressure
long  SystemUsedMB, TotalUsedMB, GcReservedMB, GfxUsedMB   // Unity's counters (-1 when invalid in this player)
string Summary()
```

### `FrameTimer`, `FramePacer`, `DynamicResolution`, `FrameGraph`

```csharp
FrameTimer:  double GpuMs (EMA .15), GpuLast, CpuMainMs, CpuRenderMs, PresentWaitMs, CpuFrameMs (-1 = unknown); bool Supported; void Capture()
FramePacer:  double RefreshMs, RefreshHz; bool VSynced, Measured, RefreshKnown; int Divisor; double Tick(double tms); int VSyncDivisor(int capFps)
DynamicResolution: double Scale, Min, Max, Headroom (.85), Warmup (3000); void Reset(double scale); bool Update(now, gpuMs, cpuMs, frameMs, budget)
FrameGraph:  const N = 160; void Push(float dtMs, float gpuMs, float budgetMs); float Dt(int k), Gpu(int k); float BudgetMs; event Action Pushed
```

### `FrameGraphElement : VisualElement`

`new FrameGraphElement()` (optionally `(FrameGraph)`): 280x72, rgba(0,0,0,.45), pickingMode Ignore. Bars of the last
160 frame intervals against the frame budget (green on time, amber > 1.15x, red > 1.9x, 0..3x scale), white lines at 1x
and 2x, the GPU time as a cyan line broken where unknown, and "xx.x ms" at the 1x line. It repaints itself on every
`Perf.Graph.Push` while displayed; the HUD only positions it and toggles `style.display`.

## Hooks the owners add

**MatchRunner.Update** (`Assets/ZU/Game/MatchRunner.cs`; `ZU.Game.asmdef` references `ZU.Engine`):

```csharp
acc += ZU.Engine.Perf.FrameDt;            // was: acc += Time.deltaTime;
```

All five hooks as `git apply`-able patches (against unity-94): `~/Projects/zu-qa/engine-check/hooks/*.patch`, each
compile-checked together with ZU.Engine (a full unitycheck with the patched files swapped in).

**HeroView animation LOD** (`hooks/HeroView.patch`): a
`AnimBudget.Slot budget` field per view, `v.budget = AnimBudget.Shared.NewSlot()` where the view is made, and in the
per-frame pose code `float adt = AnimBudget.Shared.Step(budget, drawPos, (float)a.Height, Time.deltaTime, always)`:
only when `adt >= 0` does the view build its `AnimState` with `adt` and run `proc.Update(state, anim, clips)`; the
root pose below (tilt / knockdown at the current draw position) runs every frame from the last procedural values, so
the body never lags its hitbox. `always` (never held) = the own hero, the gallery / Hero Viewer, bosses, holograms,
forced moves, Susanoo, the knocked-down (sim or pose) and views without a clip layer. The impact camera kick fires only
on an updated frame (`proc.impact` is a per-update flag).

**SettingsApply.Apply** (`Assets/ZU/Game/UI/Toolkit/SettingsApply.cs`), in place of the `vSyncCount` /
`targetFrameRate` / `renderScale` writes:

```csharp
using ZU.Engine;
...
Perf.SetCap((int)v.fpsCap);                                   // 0 = display based; a cap that divides the refresh locks to every k-th vblank
Perf.SetBaseScale(Mathf.Clamp((float)v.renderScale / 100f, 0.5f, 2f));
Perf.SetDynamicResolution(v.dynamicRes);                      // off resets the dynamic scale to 1 (TS applySettings)
Perf.SetUpscaler(Perf.Upscaler.Auto);                 // FSR below native with the web's RCAS 0.25 stops
Perf.SetSharpen(Mathf.Clamp01((float)v.sharpen / 100f)); // the web's native-scale sharpen pass
```

The `UniversalRenderPipelineAsset` block stays for `msaaSampleCount` / `shadowDistance` only.

**MatchUi** advanced stats (`Assets/ZU/Game/UI/Toolkit/MatchUi.cs`, the web `Game.ts` perf lines):

```csharp
var ps = ZU.Engine.Perf.Stats;
hud.Perf(new[]
{
    $"{fpsAvg:0} FPS  {1000 / Mathf.Max(1, fpsAvg):0.0} ms",
    $"render {Mathf.RoundToInt(Screen.width * ps.RenderScale)}x{Mathf.RoundToInt(Screen.height * ps.RenderScale)} ({Mathf.RoundToInt(ps.RenderScale * 100)}%){(ps.Upscaler != "off" ? " " + ps.Upscaler.ToUpperInvariant() : "")}",
    $"sim 120 Hz  heroes {w.actors.Count}  projectiles {w.projs.Count}",
    $"CPU {ps.CpuMs:0.0} ms  GPU {(ps.GpuMs >= 0 ? ps.GpuMs.ToString("0.0") + " ms" : "n/a")}",
    $"display {ps.RefreshHz:0} Hz{(ps.VSynced ? " vsync" : "")}  cap {(ps.TargetFrameRate > 0 ? ps.TargetFrameRate + " fps" : ps.VSyncCount > 0 ? "vsync/" + ps.VSyncCount : "off")}  dyn {ps.DynScale:0.00}",
    $"gov {ps.Bottleneck} L{ps.GovernorLevel}  anim LOD {ps.AnimUpdated} full / {ps.AnimHeld} held  RAM {ps.MemAvailMB:0} MB {ps.Pressure}",
});
```

**HudView** (`Assets/ZU/Game/UI/Toolkit/HudView.cs`), the graph next to the `fps` label (the web: absolute, left 10,
top 150):

```csharp
readonly ZU.Engine.FrameGraphElement graph = new ZU.Engine.FrameGraphElement();
// constructor, after the root is built:
graph.style.position = Position.Absolute; graph.style.left = 10; graph.style.top = 150; root.Add(graph);
// Perf(string[] lines):
graph.style.display = lines != null ? DisplayStyle.Flex : DisplayStyle.None;
```

**ProjectSettings** (`ProjectSettings/ProjectSettings.asset`, the lead): `enableFrameTimingStats: 1`, so release
players record `FrameTimingManager` (the Editor and development builds always do). Without it `FrameTimer` reports -1,
the dynamic resolution falls back to frame-interval timing and the Governor classifies most frames as Unknown.

## Broadphase

`Assets/ZU/Sim/Core/Broadphase.cs` (port of the TS `Broadphase.ts`) puts a uniform XZ grid under the Level's collision
queries - `groundAt`, `collide`, the rays (bot sight checks, camera pull-in, hitscan, ragdoll bones) - which used to
walk every box of the map per hero per 120 Hz step. Candidates come back as item indices in ascending order, so the
original loops run over them in the original order and give bit-identical answers (ties, sequential pushes).

Verified on Kaggle (harness `~/Projects/zu-qa/engine-bptest`): 14 maps, 37.8 M queries, 0 mismatches against the
pre-grid loops. Per query: ground 4-12x, collide 3-7x, ray up to 2x faster. `simtest aimatch` output identical; 8 maps
24.7 s -> 17.6 s.

## Prop LOD convention

Agreed with the props owner, for every imported map prop: 3 LODs at 100 / 40 / 12 % of the triangles, `LODGroup` screen
heights 0.25 / 0.10 / 0.02, culled below 0.02 except landmarks taller than 6 m (they stay at LOD2), `fadeMode None`.
The Governor's lodBias ladder (1 -> 0.6) moves these thresholds together; it never edits the groups.

## Switches

| Switch | Effect |
|---|---|
| `-zu-engine=0` (command line) | `Perf.Enabled = false`: `FrameDt = Time.deltaTime`, no quantizing, a plain vsync-or-target cap, no dynamic resolution, no Governor. The web `?engine=0`, for A/B checks. |
| `-zu-governor=0` | `Governor.Enabled = false`: no ladder, no memory actions; the readouts still run. |
| `Perf.AllowAssetWrites` | default `!Application.isEditor`. In the Editor the URP asset is the project's file on disk, so renderScale / upscaler writes are skipped unless a test opts in. |

## Editor verification checklist

Nothing here has run inside the Editor yet (the compile check is the asmdef-boundary build only). To tick off:

- [ ] `RenderPipelineManager.endContextRendering` fires once per frame on the main thread in this URP setup (the fallback `CpuMs` end mark); with multithreaded rendering + vsync the main thread may block inside the submit, inflating the fallback (safe direction: the Governor / dynres then see "CPU bound" and do not downscale).
- [ ] `FrameTimingManager` reports non-zero `gpuFrameTime` on this machine's API (D3D12 / D3D11); if 0, `GpuMs` = -1 and the frame-interval fallback is in use (check `Perf.Stats.TimingSupported` and `GpuMs` on the HUD).
- [ ] The cap rule: 60 cap on the 144 Hz display stays on `vSyncCount 0 + targetFrameRate 60` (not a divisor); 72 -> `vSyncCount 2`; 144 -> `vSyncCount 1`; display based -> `vSyncCount 1`. Measured `RefreshHz` settles near 144 with `VSynced` true in a player (never in the Editor: it ignores vSyncCount, the pacer's divisor is forced to 0 there).
- [ ] Moving the window to another monitor: `Screen.currentResolution.refreshRateRatio` follows it and the cap is re-applied within a second; on a non-primary monitor with a higher rate the measurement is rejected by design (never above the reported rate).
- [ ] `FrameGraphElement` paints in the HUD; the "xx.x ms" label sits just above the 1x line (bottom = H/3 + 1 was derived from the web baseline, not seen).
- [ ] Dynamic resolution: `DynScale` steps in 5 % with the warm-up hold at match start; `UpscalerActive` flips to "fsr" below 0.99 and the picture shows RCAS sharpening.
- [ ] `Sharpen.shader` compiles (the asmdef check cannot compile HLSL): no errors on import, `Hidden/ZU/Sharpen` shows one pass "ZU Sharpen", `Resources.Load<Shader>("ZUEngine/Sharpen")` returns it in a player build (the Resources folder, no renderer / Always Included entry needed).
- [ ] Sharpen pass order (Frame Debugger / Render Graph Viewer): "ZU Sharpen" sits after "UberPost" (and SMAA / TAA when on) and before "FinalPost" / "FinalBlit", on the resolving camera only - one per frame with the viewmodel overlay camera in the stack, none on the Hero Viewer / gallery cameras unless they are Game cameras resolving their own stack.
- [ ] Sharpen gamma: with the slider at 100 a mid-grey / highlight edge sharpens like the web (the mask runs in sRGB, no stronger haloing on highlights than the web shows); the picture is not darkened or double-encoded (a wrong colour space would show as a gamma shift of the whole frame while the slider is > 0).
- [ ] No double sharpening below native: drop the render scale to 0.9 - `SharpenActive` false, `UpscalerActive` "fsr", no "ZU Sharpen" pass in the frame; back to 1.0 - `SharpenActive` true, no RCAS. Slider 0: no pass enqueued at all (URP goes back to its direct-to-backbuffer path when nothing else needs the intermediate).
- [ ] The cameraColor swap: after the pass the final blit reads the sharpened temp (the picture changes with the slider), no "trying to access frameData outside of the current frame" / untracked-texture errors from the blit helper, no MSAA mismatch warnings with MSAA on in the quality asset.
- [ ] Sharpen cost at 1440p native: one full-screen blit (~0.1-0.2 ms on the RTX) plus, with FXAA off, URP's extra final blit; `GpuMs` on the HUD moves by about that when the slider goes 0 -> 50.
- [ ] `QualitySettings.lodBias` writes in play mode do not persist into the quality asset after exiting play mode (the Governor restores the base on quit, but confirm the Editor leaves `QualitySettings` as it was).
- [ ] Governor ladder: in a 10-hero fight with the frame over budget, `Bottleneck` reads MainThread, L1 after ~1.5 s, L2 2 s later; recovery to L0 after 4 s under 85 % of budget; `tierScale` shows in `AnimHeld` rising.
- [ ] AnimBudget: a held frame's bones keep the last pose with the manual PlayableGraph (the clip layer is only evaluated inside `proc.Update`) - no T-pose or snap on held frames of far heroes.
- [ ] `Screen.dpi` returns 0 on some set-ups: the AnimBudget thresholds then use device px (a 1440p screen counts as 1440 CSS px, so fewer views are reduced); check the HUD's updated / held counts look sane.
- [ ] `Camera.main` is the match camera (AnimBudget's frustum and the pixel-per-metre figure come from it; the Hero Viewer / gallery views are `always` anyway).
- [ ] `ProfilerRecorder` memory counters may be invalid in release players (`MemoryWatch` then shows -1 for the Unity figures; the kernel32 machine figures still drive the pressure).
- [ ] `MemoryWatch.Pressure` thresholds on this 32 GB PC: Elevated at < 3 GB free; verify one `Resources.UnloadUnusedAssets` after 5 s (log `[ZU.Engine]` LastAction on the HUD) and no hitch worth noticing.
- [ ] Critical back-off: with the RAM squeezed by other processes (open a second Editor / Chrome tabs until < 1.5 GB free), the warnings read "next in 20 s", then 40, 80, 160 s with "last unload freed <128 MB"; back to 20 s once the pressure drops to None. `SystemUsedMB` must be valid for the check (else `TotalUsedMB`; both -1 = the interval never adapts).
- [ ] Start-up log line `[ZU.Engine] Mid | NVIDIA GeForce RTX ... | ... | RAM ...` appears once per play session (no domain reload: statics persist - check a second Play does not log twice or double-boot `PerfDriver`).

## Open items

- **Overlay cameras.** URP's `renderScale` also scales stacked overlay cameras - the first-person viewmodel among them -
  while the web drew the viewmodel at native over the scaled scene. Either accept the slightly softer arms below native
  or render the viewmodel on its own camera stack at scale 1.
- **Refresh on a non-primary monitor.** The pacer trusts `Screen.currentResolution.refreshRateRatio` as the ceiling of
  what the measurement may report; if Unity reports the primary display's rate for a window on a faster secondary
  monitor, the game keeps the primary's rate (no divisor lock, budget from the lower rate). To verify on a dual-monitor
  set-up; `Display.displays` may be the better prior.
