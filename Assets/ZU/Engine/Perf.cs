// The engine's public frame-level API (port of zenith-umbra src/engine/EngineCore.ts, engine core): what the web Game
// did with its EngineCore across applySettings / applyScale / the loop, as one static facade every assembly calls.
// Perf OWNS three things the rest of the game used to write directly, so they are set from one place with one rule:
//   the frame delta       FrameDt = FramePacer's vsync-quantized unscaled delta x Time.timeScale (MatchRunner's fixed-step
//                         accumulator reads it instead of Time.deltaTime: no micro-stutter from stamp jitter)
//   the frame cap         Application.targetFrameRate / QualitySettings.vSyncCount (SetCap): a cap that divides the
//                         refresh locks to every k-th vblank (vSyncCount k) - each frame shown for the same time -
//                         instead of a free-running limiter that lands on random vblanks (the TS deadline cap)
//   URP's renderScale     = settings scale x dynamic scale (DynamicResolution follows the GPU's load only), with the
//                         upscaling filter (FSR 1.0 below native, as the TS FsrPass) and its sharpness
//   the sharpen pass      SetSharpen: the Options slider's native-scale image sharpening (SharpenPass, the TS Game.ts
//                         SharpenShader) - a setting, not an optimisation, so it runs even with Enabled == false
// Driven once per frame by PerfDriver (execution order -10000: before anything reads FrameDt), and it ticks the
// Governor (bottleneck-directed quality ladder + memory pressure, no TS original) after the timing capture and the
// dynamic resolution. Off with the command line -zu-engine=0 (the TS ?engine=0 / localStorage zu-engine=0) for A/B
// checks: then the game sees Time.deltaTime, a plain cap, no dynamic resolution and no Governor. URP properties are
// written only when they change (a renderScale write re-allocates the render targets) and, in the Editor, only when
// AllowAssetWrites is set (the pipeline asset is the project's file on disk there - the same rule SettingsApply had).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZU.Engine
{
    /// <summary>a snapshot of the engine's frame state for overlays and tests</summary>
    public readonly struct PerfStats
    {
        public readonly bool Enabled, VSynced, TimingSupported;
        /// <summary>settings render scale, dynamic scale, and the product as applied to URP</summary>
        public readonly float BaseScale, DynScale, RenderScale;
        /// <summary>"off" | "linear" | "fsr" | "stp"</summary>
        public readonly string Upscaler;
        public readonly double RefreshHz, BudgetMs, GpuMs, GpuLast, CpuMs, CpuMainMs, CpuRenderMs, PresentWaitMs, FrameMs;
        /// <summary>Application.targetFrameRate / QualitySettings.vSyncCount as the engine applied them (the live values until SetCap)</summary>
        public readonly int TargetFrameRate, VSyncCount;
        public readonly float FrameDt, UnscaledFrameDt;
        /// <summary>the Governor: what the frame waits on, its ladder level, the animation LOD's last frame, the machine's RAM</summary>
        public readonly Governor.Bottleneck Bottleneck;
        public readonly int GovernorLevel, AnimUpdated, AnimHeld;
        public readonly float MemAvailMB;
        public readonly MemoryWatch.Pressure Pressure;
        /// <summary>the native-scale sharpen pass runs this frame (slider &gt; 0, render scale &gt;= 0.99, shader loaded)</summary>
        public readonly bool SharpenActive;

        public PerfStats(bool enabled, bool vsynced, bool timingSupported, float baseScale, float dynScale, float renderScale, string upscaler,
            double refreshHz, double budgetMs, double gpuMs, double gpuLast, double cpuMs, double cpuMainMs, double cpuRenderMs, double presentWaitMs,
            double frameMs, int targetFrameRate, int vSyncCount, float frameDt, float unscaledFrameDt,
            Governor.Bottleneck bottleneck, int governorLevel, int animUpdated, int animHeld, float memAvailMB, MemoryWatch.Pressure pressure,
            bool sharpenActive)
        {
            SharpenActive = sharpenActive;
            Enabled = enabled; VSynced = vsynced; TimingSupported = timingSupported;
            BaseScale = baseScale; DynScale = dynScale; RenderScale = renderScale; Upscaler = upscaler;
            RefreshHz = refreshHz; BudgetMs = budgetMs; GpuMs = gpuMs; GpuLast = gpuLast; CpuMs = cpuMs; CpuMainMs = cpuMainMs; CpuRenderMs = cpuRenderMs;
            PresentWaitMs = presentWaitMs; FrameMs = frameMs; TargetFrameRate = targetFrameRate; VSyncCount = vSyncCount; FrameDt = frameDt; UnscaledFrameDt = unscaledFrameDt;
            Bottleneck = bottleneck; GovernorLevel = governorLevel; AnimUpdated = animUpdated; AnimHeld = animHeld; MemAvailMB = memAvailMB; Pressure = pressure;
        }
    }

    public static class Perf
    {
        public enum Upscaler { Auto, Off, FSR, STP }

        static readonly FramePacer pacer = new FramePacer();
        static readonly DynamicResolution dynres = new DynamicResolution();
        static readonly FrameTimer timing = new FrameTimer();
        static readonly FrameGraph graph = new FrameGraph();

        // capSet: SetCap was called - only then does the engine own vSyncCount / targetFrameRate (until the settings hook lands,
        // SettingsApply still writes them itself, and a re-apply here would overwrite the player's cap with vsync)
        static bool inited, enabled = true, allowAssetWrites, dynOn, capVsync = true, capSet;
        // dynLive: the dynamic scale can actually reach URP (asset writes allowed and the URP asset exists). Without it the
        // controller would run OPEN-LOOP - lowering its scale never lowers the load, so it walks to the floor (seen in the
        // Editor, where AllowAssetWrites is off): then it is held at 1 and not stepped (the first Editor run, Hanabi)
        static bool dynLive, isEditor;
        static int capFps;
        // the cap actually in effect (QualitySettings.vSyncCount / Application.targetFrameRate, whoever wrote them), re-read
        // once a second and after our own writes: the budget follows it, not only a cap set through SetCap
        static int liveVSync, liveTarget = -1;
        static float baseScale = 1, renderScale = 1, sharp = 0.9f, sharpen;
        static Upscaler upMode = Upscaler.Auto;
        static string upActive = "off";
        static int appliedVSync = -1, appliedTarget = int.MinValue;
        static double appliedRefresh;
        static double frameMs = 1000.0 / 60, lastCapCheck = double.NegativeInfinity;
        // the fallback CPU measurement: the frame's Update start, the end of its scripted work, the end of its render submission
        static double frameT0, lateEnd, renderEnd, fallbackCpuMs;
        static Action<ScriptableRenderContext, List<Camera>> onEndContext;

        // ------------------------------------------------------------------ read-only state

        /// <summary>this frame's delta (s) for the simulation: the quantized unscaled delta, clamped by Time.maximumDeltaTime,
        /// x Time.timeScale (the game pauses with timeScale 0 and slow-mos with 0.35). Time.deltaTime when the engine is off.</summary>
        public static float FrameDt { get; private set; }
        /// <summary>this frame's unscaled delta (s), quantized (Time.unscaledDeltaTime when the engine is off)</summary>
        public static float UnscaledFrameDt { get; private set; }
        /// <summary>the dynamic resolution's scale (1 when off, the engine is off, or the scale cannot reach URP: asset writes
        /// off as in the Editor, or no URP asset - then the controller is held, never run open-loop)</summary>
        public static float DynScale => enabled && dynOn && dynLive ? (float)dynres.Scale : 1;
        /// <summary>the settings render scale (0.5..2)</summary>
        public static float BaseScale => baseScale;
        /// <summary>URP renderScale as applied (= base x dynamic; the asset's own value when asset writes are off)</summary>
        public static float RenderScale => renderScale;
        /// <summary>"off" (no resampling) | "linear" | "fsr" | "stp"</summary>
        public static string UpscalerActive => upActive;
        /// <summary>the Image Sharpening slider as a fraction 0..1 (SetSharpen)</summary>
        public static float Sharpen => sharpen;
        /// <summary>the native-scale sharpen pass runs this frame: Sharpen &gt; 0, RenderScale &gt;= 0.99 (below that FSR's RCAS
        /// sharpens instead) and its shader loaded. Independent of Enabled: a setting, not an optimisation.</summary>
        public static bool SharpenActive => SharpenPass.Active;
        /// <summary>the display's refresh (Hz): reported, then measured from the vsync cadence</summary>
        public static double RefreshHz => pacer.RefreshHz;
        /// <summary>the frame cadence is vsync-locked (deltas are quantized)</summary>
        public static bool VSynced => pacer.VSynced;
        /// <summary>the frame budget (ms) from the cap actually in effect (re-read once a second, whoever set it): vSyncCount k &gt; 0
        /// in a player = refresh / k; else targetFrameRate &gt; 0 = 1000 / min(targetFrameRate, refresh) (the TS updateDynRes);
        /// else the refresh. The Editor ignores vSyncCount, so only its targetFrameRate counts there.</summary>
        public static double BudgetMs
        {
            get
            {
                double hz = pacer.RefreshHz;
                double fps = liveVSync > 0 && !isEditor ? hz / liveVSync : liveTarget > 0 ? Math.Min(liveTarget, hz) : hz;
                return 1000 / Math.Max(1, fps);
            }
        }
        /// <summary>CPU ms of the last frame's own main-thread work (FrameTimer's main thread minus its present wait when
        /// available, else measured from the driver's Update to the end of LateUpdate / render submission)</summary>
        public static double CpuMs { get; private set; }
        public static FrameTimer Timing => timing;
        public static FrameGraph Graph => graph;
        public static FramePacer Pacer => pacer;
        public static DynamicResolution DynRes => dynres;

        /// <summary>everything above (+ the Governor's state) in one struct (no allocation: the strings are cached)</summary>
        public static PerfStats Stats => new PerfStats(enabled, pacer.VSynced, timing.Supported, baseScale, DynScale, renderScale, upActive,
            pacer.RefreshHz, BudgetMs, timing.GpuMs, timing.GpuLast, CpuMs, timing.CpuMainMs, timing.CpuRenderMs, timing.PresentWaitMs,
            frameMs, capSet ? appliedTarget : Application.targetFrameRate, capSet ? appliedVSync : QualitySettings.vSyncCount,
            FrameDt, UnscaledFrameDt,
            Governor.Current, Governor.Level, AnimBudget.Shared.LastUpdated, AnimBudget.Shared.LastHeld, MemoryWatch.AvailMB, MemoryWatch.pressure,
            SharpenPass.Active);

        // ------------------------------------------------------------------ switches

        /// <summary>master switch (default true; false with -zu-engine=0 on the command line). Off = the old behaviour:
        /// FrameDt = Time.deltaTime, a plain cap, no dynamic resolution, no quantizing - for A/B checks.</summary>
        public static bool Enabled
        {
            get { Init(); return enabled; }
            set { Init(); if (enabled == value) return; enabled = value; ApplyCap(); ApplyScale(); }
        }

        /// <summary>write URP asset properties (renderScale, upscaler). Default !Application.isEditor: in the Editor the
        /// pipeline asset is the project's file, and writing it rewrites that file on disk. Tests may opt in.</summary>
        public static bool AllowAssetWrites
        {
            get { Init(); return allowAssetWrites; }
            set { Init(); allowAssetWrites = value; SyncDynLive(); ApplyScale(); }
        }

        /// <summary>the settings render scale (0.5..2). The engine owns URP renderScale = base x dynamic.</summary>
        public static void SetBaseScale(float scale)
        {
            Init();
            baseScale = Mathf.Clamp(scale, 0.5f, 2f);
            ApplyScale();
        }

        /// <summary>the frame cap. fps 0 = display based: vSyncCount 1 (vsync false: uncapped). fps that divides the refresh
        /// (FramePacer.VSyncDivisor) with vsync: vSyncCount k + targetFrameRate -1 - every frame held the same number of
        /// vblanks. Otherwise vSyncCount 0 + targetFrameRate fps. Re-applied when the refresh changes (the window moved
        /// to another monitor), checked once a second.</summary>
        public static void SetCap(int fps, bool vsync = true)
        {
            Init();
            capFps = Math.Max(0, fps); capVsync = vsync; capSet = true;
            ApplyCap();
        }

        /// <summary>dynamic resolution on/off and its floor. Off resets the dynamic scale to 1 (the TS applySettings:
        /// `if (!v.dynamicRes) dynScale = 1; dynres.reset(dynScale)`); on keeps the current scale and restarts the load history.
        /// The default floor is 0.8, not the web's 0.5: that floor suited its lighter three.js renderer, while on the URP PC
        /// pipeline an RTX 3050 missing the 60 Hz budget with the Ultra preset (base 1.25) would otherwise be walked down to
        /// render scale 0.625 - visibly blurry. 0.8 keeps Ultra at native or above (1.25 x 0.8 = 1.0) and the 100 % preset
        /// at &gt;= 80 %, where FSR is close to native. Explicit callers may still ask for 0.25..1.</summary>
        public static void SetDynamicResolution(bool on, float min = 0.8f)
        {
            Init();
            dynOn = on;
            dynres.Min = Mathf.Clamp(min, 0.25f, 1f);
            dynres.Reset(on ? Math.Max(dynres.Min, dynres.Scale) : 1);
            SyncDynLive();
            ApplyScale();
        }

        /// <summary>the upscaling filter. Auto = FSR 1.0 while the applied render scale &lt; 0.99, else bilinear (the TS: FSR
        /// below native, the sharpen pass at native). Off = bilinear. FSR / STP forced. sharpness 0..1 maps to URP's
        /// fsrSharpness, which only applies BELOW native (URP runs RCAS in its final pass only when upscaling); leave the
        /// default 0.9 = the TS RCAS 0.25 stops, exactly like the web: URP computes stops = (1 - fsrSharpness) *
        /// FSRUtils.kMaxSharpnessStops (2.5) - com.unity.render-pipelines.core FSRUtils.cs:109 / URP FinalPostProcessPass.cs:170.
        /// The Options "Image Sharpening" slider does NOT come here: at native it drives the SharpenPass via SetSharpen,
        /// the web's separate native-scale pass.</summary>
        public static void SetUpscaler(Upscaler mode, float sharpness = 0.9f)
        {
            Init();
            upMode = mode; sharp = Mathf.Clamp01(sharpness);
            ApplyUpscaler();
        }

        /// <summary>the Options "Image Sharpening" slider as a fraction 0..1 (= slider / 100). Drives the web's native-scale
        /// sharpen pass (SharpenPass: shader amount = amount01 x 0.6, Game.ts:276), which runs on the resolving Game camera
        /// after URP's post-processing while RenderScale &gt;= 0.99; below that FSR's RCAS sharpens and the pass stays off
        /// (Game.ts:325). 0 = off (nothing enqueued). A setting, not an optimisation: active even with Enabled == false.</summary>
        public static void SetSharpen(float amount01)
        {
            Init();
            sharpen = Mathf.Clamp01(amount01);
            SharpenPass.SetAmount(sharpen);
        }

        // ------------------------------------------------------------------ the frame (PerfDriver)

        internal static void Init()
        {
            if (inited) return;
            inited = true;
            isEditor = Application.isEditor;
            allowAssetWrites = !isEditor;
            enabled = !FlagOff("-zu-engine");
            pacer.SetPrior(Screen.currentResolution.refreshRateRatio.value);
            SyncDivisor();
            SyncBudget();
            SyncDynLive();
            onEndContext = EndRender;
            RenderPipelineManager.endContextRendering += onEndContext;
            Governor.Init();
            SharpenPass.Init();                            // the camera hook only; the shader loads on the first frame the slider is > 0
        }

        /// <summary>true when the command line carries `name=0` (or `name 0`): the TS ?engine=0 switch</summary>
        internal static bool FlagOff(string name)
        {
            try
            {
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length; i++)
                {
                    if (string.Equals(a[i], name + "=0", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < a.Length && a[i + 1] == "0") return true;
                }
            }
            catch (Exception) { /* no command line here: default on */ }
            return false;
        }

        // until SetCap is called the cap is whoever's wrote it last: the pacer's divisor follows the real vSyncCount, so a
        // targetFrameRate cadence (vSyncCount 0) is never measured as the refresh. The Editor ignores vSyncCount (its Game
        // view runs free unless its own VSync toggle is on): never the display's cadence there
        static void SyncDivisor() { pacer.Divisor = isEditor ? 0 : capSet ? appliedVSync : QualitySettings.vSyncCount; }

        /// <summary>snapshot the cap in effect for BudgetMs (two native reads; once a second and after our own cap writes)</summary>
        static void SyncBudget() { liveVSync = QualitySettings.vSyncCount; liveTarget = Application.targetFrameRate; }

        /// <summary>can the dynamic scale reach URP? On the false edge the controller is reset to 1 and held (DynScale reads 1,
        /// nothing drifts); when it becomes true again it resumes from 1, with its normal warm-up (no calls for &gt; 1 s).</summary>
        static void SyncDynLive()
        {
            bool live = allowAssetWrites && Asset != null;
            if (live == dynLive) return;
            dynLive = live;
            if (!live) dynres.Reset(1);
        }

        /// <summary>start of the frame (PerfDriver.Update, order -10000)</summary>
        internal static void Frame()
        {
            Init();
            double tms = Time.unscaledTimeAsDouble * 1000;
            double now = Time.realtimeSinceStartupAsDouble * 1000;
            // the previous frame's own CPU work (the fallback): from its Update start to the later of the end of its scripts
            // and of its render submission
            if (frameT0 > 0) { double e = Math.Max(lateEnd, renderEnd); if (e > frameT0) fallbackCpuMs = e - frameT0; }
            frameT0 = now;
            double q = pacer.Tick(tms);
            if (enabled)
            {
                UnscaledFrameDt = (float)q;
                FrameDt = (float)(Math.Min(q, Time.maximumDeltaTime) * Time.timeScale);
                if (q > 0) frameMs = q * 1000;
            }
            else
            {
                UnscaledFrameDt = Time.unscaledDeltaTime;
                FrameDt = Time.deltaTime;
                frameMs = Time.unscaledDeltaTime * 1000;
            }
            timing.Capture();
            CpuMs = timing.CpuMainMs > 0 ? timing.CpuMainMs - Math.Max(0, timing.PresentWaitMs) : fallbackCpuMs;
            // dynamic render scale: follows the GPU's measured load (DynamicResolution), the budget from the cap in effect;
            // stepped only while the scale can reach URP (dynLive) - never open-loop
            double budget = BudgetMs;
            if (enabled && dynOn && dynLive && dynres.Update(tms, timing.GpuMs, CpuMs, frameMs, budget)) ApplyScale();
            Governor.Tick(tms, frameMs, CpuMs, timing, budget);
            // the graph's "on time" is the frame budget - the cap's interval when capped (the TS pushed the refresh interval,
            // which paints every frame of a 60 cap on a 144 Hz display red)
            graph.Push((float)frameMs, (float)timing.GpuLast, (float)budget);
            // the display may have changed (another monitor, a mode switch): once a second, re-read it and re-apply a cap the
            // engine set; a cap somebody else wrote is left alone (only the pacer's divisor follows it)
            if (tms - lastCapCheck > 1000)
            {
                lastCapCheck = tms;
                pacer.SetPrior(Screen.currentResolution.refreshRateRatio.value);
                if (!capSet) SyncDivisor();
                else if (Math.Abs(pacer.RefreshHz - appliedRefresh) > 0.5) ApplyCap();
                SyncBudget();                              // a cap somebody else wrote (a hand-set targetFrameRate) moves the budget too
                SyncDynLive();                             // the URP asset may have appeared / a test may have opted into asset writes
            }
        }

        /// <summary>end of the frame's scripted work (PerfDriverEnd.LateUpdate, order +10000)</summary>
        internal static void EndScripts() { lateEnd = Time.realtimeSinceStartupAsDouble * 1000; }

        /// <summary>the pipeline finished submitting the frame's cameras (RenderPipelineManager.endContextRendering)</summary>
        static void EndRender(ScriptableRenderContext ctx, List<Camera> cams) { renderEnd = Time.realtimeSinceStartupAsDouble * 1000; }

        // ------------------------------------------------------------------ apply

        static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        static void ApplyCap()
        {
            if (!capSet) return;                           // nobody asked: the cap stays whoever's (see capSet)
            int vs, tfr, k = enabled && capVsync ? pacer.VSyncDivisor(capFps) : 0;
            if (capFps <= 0) { vs = capVsync ? 1 : 0; tfr = -1; }
            else if (k > 0) { vs = k; tfr = -1; }
            else { vs = 0; tfr = capFps; }
            if (QualitySettings.vSyncCount != vs) QualitySettings.vSyncCount = vs;
            if (Application.targetFrameRate != tfr) Application.targetFrameRate = tfr;
            appliedVSync = vs; appliedTarget = tfr; appliedRefresh = pacer.RefreshHz;
            SyncDivisor();
            SyncBudget();
        }

        static void ApplyScale()
        {
            float want = Mathf.Clamp(baseScale * DynScale, 0.1f, 2f);        // URP's own range
            var a = Asset;
            if (a != null && allowAssetWrites)
            {
                if (Mathf.Abs(a.renderScale - want) > 1e-4f) a.renderScale = want;   // (a write re-allocates the targets)
                renderScale = a.renderScale;
            }
            else renderScale = a != null ? a.renderScale : want;
            ApplyUpscaler();
        }

        static void ApplyUpscaler()
        {
            var f = upMode switch
            {
                Upscaler.Off => UpscalingFilterSelection.Linear,
                Upscaler.FSR => UpscalingFilterSelection.FSR,
                Upscaler.STP => UpscalingFilterSelection.STP,
                _ => renderScale < 0.99f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Linear,
            };
            var a = Asset;
            if (a != null && allowAssetWrites)
            {
                if (a.upscalingFilter != f) a.upscalingFilter = f;
                if (!a.fsrOverrideSharpness) a.fsrOverrideSharpness = true;
                if (Mathf.Abs(a.fsrSharpness - sharp) > 1e-4f) a.fsrSharpness = sharp;
            }
            var eff = a != null && !allowAssetWrites ? a.upscalingFilter : f;
            if (eff == UpscalingFilterSelection.Auto) eff = renderScale < 1 ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Linear;   // URP's own Auto rule
            // "off" when nothing is resampled (the TS: fsr.enabled only when the scale is off native)
            upActive = Mathf.Abs(renderScale - 1) <= 0.01f ? "off" : eff == UpscalingFilterSelection.FSR ? "fsr" : eff == UpscalingFilterSelection.STP ? "stp" : "linear";
        }
    }
}
