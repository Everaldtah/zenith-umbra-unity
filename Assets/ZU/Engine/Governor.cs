// The Governor (engine core, no TS original): bottleneck-directed quality scaling plus memory-pressure handling, ticked
// by Perf once a frame after the timing capture and the dynamic resolution.
// Why bottleneck-directed: the web game's one lever was pixels, and DynamicResolution already pulls it only when the GPU
// is what the frame waits on. A frame that waits on the main thread (a 10-hero fight: skeletons, procedural animation,
// hair) or the render thread (draw submission) gets no faster with fewer pixels, it just gets blurrier - so the Governor
// first says WHAT the frame waits on (~1 s EMAs of the frame interval, the main thread's own work with its present wait
// taken out, the render thread and the GPU, from FrameTimer) and turns only the knobs that help that case:
//   MainThread    AnimBudget.Shared.tierScale (more heroes on the reduced skeleton rates) + QualitySettings.lodBias
//   RenderThread  lodBias only (fewer, cheaper draws is what helps there; the skeletons aren't the problem)
//   Gpu           nothing here - DynamicResolution owns pixels; reported for the HUD
// One ladder, four levels, one step up per 2 s after 1.5 s sustained over budget, one step down after 4 s well under it
// (hysteresis, so it never oscillates on the edge). Nothing owned by others is touched: render scale goes through Perf,
// and MSAA / shadows / textures / the mipmap limit / vsync stay the settings' (lodBias has no other runtime writer today;
// the PC quality level ships 1.6, and the Governor re-reads its base whenever somebody else changed it).
// Why memory: this PC has hung twice from RAM exhaustion with the game among the tenants. MemoryWatch reads the machine's
// free physical memory; sustained Elevated pressure gets one Resources.UnloadUnusedAssets, Critical an unload + GC and a
// warning, and every change is raised as PressureChanged so the asset owners (FX, dynamics) can shed load themselves.
// The Critical action is adaptive: on a shared PC the pressure usually comes from OTHER processes (Editors, Chrome,
// agent sessions), and an unload + GC that frees nothing of ours would only hitch the match every 20 s. So the process
// size is noted when the action fires and checked at the next real sample ~2 s later: under 128 MB freed doubles the
// interval (20 -> 40 -> 80 -> 160 s cap), 128 MB or more resets it to 20 s, as does the pressure returning to None.
// Off with -zu-governor=0 (and whenever Perf is off); turning it off restores tierScale 1 and the base lodBias.
// No per-frame allocation: the HUD strings are cached, the summaries are built only at start-up and on a warning.
using System;
using UnityEngine;

namespace ZU.Engine
{
    public static class Governor
    {
        /// <summary>what the frame waits on, from the ~1 s averages</summary>
        public enum Bottleneck { None, Gpu, MainThread, RenderThread, Unknown }

        // the ladder: animation LOD threshold scale and lodBias multiplier per level
        static readonly float[] TIER = { 1f, 1.5f, 2.25f, 3f }, LOD = { 1f, 0.85f, 0.72f, 0.6f };
        const int MAX_LEVEL = 3;
        const double OVER = 1.08, UNDER = 0.85, NONE = 1.05;         // frame EMA / budget thresholds
        const double OVER_MS = 1500, STEP_MS = 2000, UNDER_MS = 4000;
        const double ELEVATED_MS = 5000, UNLOAD_EVERY_MS = 120000, CRITICAL_EVERY_MS = 20000, CRITICAL_MAX_MS = 160000, CRITICAL_CHECK_MS = 2000;
        const long FREED_MIN_MB = 128;                                // an unload + GC that freed less than this was not ours to free
        const double EMA_MS = 1000;                                   // the averages' time constant

        static readonly string[] UP_MAIN = { "", "L1: main thread (anim LOD x1.5, lod bias x0.85)", "L2: main thread (anim LOD x2.25, lod bias x0.72)", "L3: main thread (anim LOD x3, lod bias x0.6)" };
        static readonly string[] UP_RENDER = { "", "L1: render thread (lod bias x0.85)", "L2: render thread (lod bias x0.72)", "L3: render thread (lod bias x0.6)" };
        static readonly string[] DOWN = { "L0: recovered", "L1: recovered", "L2: recovered", "" };
        const string START_LOW = "L1: low hardware tier", UNLOAD = "unload unused assets (RAM elevated)", UNLOAD_GC = "unload + GC (RAM critical)", OFF = "off: restored";

        static bool inited, enabled = true, applied;
        static int level, animLevel;                                  // lodBias follows level, tierScale follows animLevel (<= level)
        static float lodBase = 1, lodWritten = float.NaN;
        static double frameEma = -1, mainEma = -1, renderEma = -1, gpuEma = -1;
        static double overSince = -1, underSince = -1, lastStep = double.NegativeInfinity;
        static double elevatedSince = -1, lastUnload = double.NegativeInfinity, lastCritical = double.NegativeInfinity;
        // the adaptive Critical interval: the process size when the last action fired, the check due, what it freed (-1 = unknown)
        static double criticalEveryMs = CRITICAL_EVERY_MS, checkAt = -1;
        static long procBefore = -1, lastFreedMB = -1;
        static MemoryWatch.Pressure pressure = MemoryWatch.Pressure.None;

        // ------------------------------------------------------------------ state for the HUD

        /// <summary>what the frame waits on (the last classification)</summary>
        public static Bottleneck Current { get; private set; } = Bottleneck.Unknown;
        /// <summary>ladder level 0..3</summary>
        public static int Level => level;
        /// <summary>QualitySettings.lodBias as the Governor last applied it (the base when it has not touched it)</summary>
        public static float LodBias => float.IsNaN(lodWritten) ? QualitySettings.lodBias : lodWritten;
        /// <summary>AnimBudget.Shared.tierScale as applied</summary>
        public static float TierScale => TIER[animLevel];
        /// <summary>the machine's memory pressure (MemoryWatch)</summary>
        public static MemoryWatch.Pressure Pressure => pressure;
        /// <summary>the last thing the Governor did (cached strings; "" = nothing yet)</summary>
        public static string LastAction { get; private set; } = "";
        /// <summary>unscaled ms clock of LastAction (-1 = none)</summary>
        public static double LastActionTime { get; private set; } = -1;
        /// <summary>raised on every change of MemoryWatch pressure, for asset owners to shed load of their own</summary>
        public static event Action<MemoryWatch.Pressure> PressureChanged;

        /// <summary>master switch (default true; false with -zu-governor=0). Inactive whenever Perf.Enabled is off.
        /// Turning it off restores tierScale 1 and the base lodBias.</summary>
        public static bool Enabled
        {
            get => enabled;
            set { enabled = value; if (!value && applied) Restore(-1); }
        }

        static bool Active => enabled && Perf.Enabled;

        /// <summary>once, from Perf.Init: the switch, the start-up log line, the Low tier's starting level</summary>
        internal static void Init()
        {
            if (inited) return;
            inited = true;
            enabled = !Perf.FlagOff("-zu-governor");
            MemoryWatch.Sample();
            pressure = MemoryWatch.pressure;
            var hw = HardwareProfile.Current;
            Debug.Log("[ZU.Engine] " + hw.Summary() + " | " + MemoryWatch.Summary() + (enabled ? "" : " | governor off"));
            Application.quitting += OnQuit;
            if (Active && hw.tier == HardwareProfile.Tier.Low) { level = animLevel = 1; Apply(START_LOW, 0); }
        }

        /// <summary>once a frame (Perf.Frame): classify, step the ladder, watch the memory</summary>
        internal static void Tick(double tms, double frameMs, double cpuMs, FrameTimer t, double budget)
        {
            if (!Active) { if (applied) Restore(tms); Current = Bottleneck.Unknown; return; }
            // ---- the averages (time-constant EMA: a frame weighs its own length, so the window is ~1 s at any frame rate)
            double k = Math.Min(1, frameMs / EMA_MS);
            frameEma = Ema(frameEma, frameMs, k);
            mainEma = cpuMs > 0 ? Ema(mainEma, cpuMs, k) : mainEma;
            renderEma = t.CpuRenderMs > 0 ? Ema(renderEma, t.CpuRenderMs, k) : renderEma;
            gpuEma = t.GpuLast > 0 ? Ema(gpuEma, t.GpuLast, k) : t.GpuMs < 0 ? -1 : gpuEma;
            // ---- what the frame waits on
            bool timed = t.CpuMainMs > 0;
            if (frameEma <= budget * NONE || (timed && t.PresentWaitMs > frameMs * 0.5)) Current = Bottleneck.None;
            else if (!timed && gpuEma < 0) Current = Bottleneck.Unknown;
            else if (gpuEma >= mainEma && gpuEma >= renderEma) Current = Bottleneck.Gpu;
            else if (renderEma > mainEma) Current = Bottleneck.RenderThread;
            else Current = Bottleneck.MainThread;
            // ---- the ladder: up after 1.5 s over budget on a CPU-side bottleneck, one step per 2 s; down after 4 s well under
            bool cpuBound = Current == Bottleneck.MainThread || Current == Bottleneck.RenderThread;
            if (cpuBound && frameEma > budget * OVER) { if (overSince < 0) overSince = tms; } else overSince = -1;
            if (frameEma < budget * UNDER) { if (underSince < 0) underSince = tms; } else underSince = -1;
            if (overSince >= 0 && tms - overSince >= OVER_MS && tms - lastStep >= STEP_MS && level < MAX_LEVEL)
            {
                level++;
                if (Current == Bottleneck.MainThread) animLevel = level;
                lastStep = tms; overSince = -1; underSince = -1;
                Apply(Current == Bottleneck.MainThread ? UP_MAIN[level] : UP_RENDER[level], tms);
            }
            else if (underSince >= 0 && tms - underSince >= UNDER_MS && level > 0)
            {
                level--;
                if (animLevel > level) animLevel = level;
                lastStep = tms; underSince = -1;
                Apply(DOWN[level], tms);
            }
            else if (applied && Math.Abs(QualitySettings.lodBias - lodWritten) > 1e-4f) Apply(null, tms);   // somebody set a new base
            // ---- memory
            bool sampled = MemoryWatch.Sample();
            var p = MemoryWatch.pressure;
            if (p != pressure)
            {
                pressure = p;
                if (p == MemoryWatch.Pressure.None) criticalEveryMs = CRITICAL_EVERY_MS;   // the squeeze is over: next time, act promptly again
                PressureChanged?.Invoke(p);
            }
            // did the last Critical action free anything of ours? (the first real sample ~2 s after it)
            if (sampled && checkAt >= 0 && tms >= checkAt)
            {
                checkAt = -1;
                long now = ProcMB();
                if (procBefore >= 0 && now >= 0)
                {
                    lastFreedMB = procBefore - now;
                    criticalEveryMs = lastFreedMB < FREED_MIN_MB ? Math.Min(CRITICAL_MAX_MS, criticalEveryMs * 2) : CRITICAL_EVERY_MS;
                }
            }
            if (p == MemoryWatch.Pressure.Critical)
            {
                elevatedSince = -1;
                if (tms - lastCritical >= criticalEveryMs)
                {
                    lastCritical = lastUnload = tms;
                    procBefore = ProcMB(); checkAt = tms + CRITICAL_CHECK_MS;
                    Resources.UnloadUnusedAssets();
                    GC.Collect();
                    Act(UNLOAD_GC, tms);
                    // (strings only here, on the warning path; "next in" is the interval as adapted by the previous action's check)
                    Debug.LogWarning("[ZU.Engine] memory critical: " + MemoryWatch.Summary() + " (next in " + (criticalEveryMs / 1000).ToString("0") + " s"
                        + (lastFreedMB >= 0 ? "; last unload freed " + lastFreedMB + " MB)" : ")"));
                }
            }
            else if (p == MemoryWatch.Pressure.Elevated)
            {
                if (elevatedSince < 0) elevatedSince = tms;
                if (tms - elevatedSince >= ELEVATED_MS && tms - lastUnload >= UNLOAD_EVERY_MS)
                {
                    lastUnload = tms;
                    Resources.UnloadUnusedAssets();
                    Act(UNLOAD, tms);
                }
            }
            else elevatedSince = -1;
        }

        static double Ema(double ema, double x, double k) => ema < 0 ? x : ema + (x - ema) * k;

        // the process' size as the OS counts it, else as Unity tracks it; -1 when neither counter is valid in this player
        static long ProcMB() => MemoryWatch.SystemUsedMB >= 0 ? MemoryWatch.SystemUsedMB : MemoryWatch.TotalUsedMB;

        // the knobs for the current levels. The base lodBias is whatever is set when the Governor isn't the last writer (a
        // quality level / settings change becomes the new base); nothing else writes it at runtime today
        static void Apply(string action, double tms)
        {
            float cur = QualitySettings.lodBias;
            if (float.IsNaN(lodWritten) || Math.Abs(cur - lodWritten) > 1e-4f) lodBase = cur;
            float want = lodBase * LOD[level];
            if (Math.Abs(cur - want) > 1e-4f) QualitySettings.lodBias = want;
            lodWritten = want;
            AnimBudget.Shared.tierScale = TIER[animLevel];
            applied = true;
            if (action != null) Act(action, tms);
        }

        static void Restore(double tms)
        {
            level = animLevel = 0;
            float cur = QualitySettings.lodBias;
            if (!float.IsNaN(lodWritten) && Math.Abs(cur - lodWritten) <= 1e-4f) QualitySettings.lodBias = lodBase;   // (else somebody else's: leave it)
            lodWritten = float.NaN;
            AnimBudget.Shared.tierScale = 1;
            applied = false;
            overSince = underSince = -1;
            Act(OFF, tms);
        }

        static void Act(string action, double tms) { LastAction = action; LastActionTime = tms; }

        static void OnQuit() { if (applied) Restore(-1); }
    }
}
