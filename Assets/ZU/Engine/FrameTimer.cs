// GPU / CPU frame timer (port of zenith-umbra src/engine/GpuTimer.ts, engine core). The TS pooled WebGL timer queries
// and read them back a few frames later; here the source is UnityEngine.FrameTimingManager, which Unity fills the same
// way (results for frames whose GPU work has completed, never a blocking read). Same smoothing: GpuMs is an EMA (0.15)
// of the raw samples, GpuLast the latest sample, -1 = unavailable (the feature is off, or the platform reports 0) - then
// the dynamic resolution falls back to CPU-side frame timing. The CPU figures (main thread, render thread, the main
// thread's wait on present) let Perf compute the frame's own work as cpuMain - presentWait, the TS cpuMs.
// NOTE: in release players the manager only records with Player Settings > "Frame Timing Stats" on
// (ProjectSettings/ProjectSettings.asset: enableFrameTimingStats: 1); the Editor and development builds always record.
// That setting belongs to the project lead (not changed here).
using UnityEngine;

namespace ZU.Engine
{
    public sealed class FrameTimer
    {
        /// <summary>GPU time of a whole frame (ms), smoothed; -1 = unavailable</summary>
        public double GpuMs { get; private set; } = -1;
        /// <summary>the latest raw GPU sample (ms); -1 = unavailable</summary>
        public double GpuLast { get; private set; } = -1;
        /// <summary>main thread time of the latest reported frame (ms), present wait included; -1 = unavailable</summary>
        public double CpuMainMs { get; private set; } = -1;
        /// <summary>render thread time of the latest reported frame (ms); -1 = unavailable</summary>
        public double CpuRenderMs { get; private set; } = -1;
        /// <summary>main thread time spent waiting on present / vsync (ms); -1 = unavailable</summary>
        public double PresentWaitMs { get; private set; } = -1;
        /// <summary>whole CPU frame time (ms, frame start to the next); -1 = unavailable</summary>
        public double CpuFrameMs { get; private set; } = -1;
        /// <summary>FrameTimingManager.IsFeatureEnabled() as of the last capture</summary>
        public bool Supported { get; private set; }

        // one slot, reused: GetLatestTimings fills the caller's array (no per-frame allocation)
        readonly UnityEngine.FrameTiming[] buf = new UnityEngine.FrameTiming[1];

        /// <summary>once per frame (early): capture and read the latest completed frame's timings</summary>
        public void Capture()
        {
            Supported = FrameTimingManager.IsFeatureEnabled();
            if (!Supported) { Clear(); return; }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, buf) == 0) return;   // nothing completed yet: keep the last figures
            ref var t = ref buf[0];
            double g = t.gpuFrameTime;
            // 0 = the platform / driver reports no GPU time (the TS: a disjoint event drops ms to -1 as well)
            if (g > 0) { GpuLast = g; GpuMs = GpuMs < 0 ? g : GpuMs + (g - GpuMs) * 0.15; }
            else { GpuLast = -1; GpuMs = -1; }
            CpuMainMs = t.cpuMainThreadFrameTime > 0 ? t.cpuMainThreadFrameTime : -1;
            CpuRenderMs = t.cpuRenderThreadFrameTime > 0 ? t.cpuRenderThreadFrameTime : -1;
            PresentWaitMs = t.cpuMainThreadPresentWaitTime >= 0 ? t.cpuMainThreadPresentWaitTime : -1;
            CpuFrameMs = t.cpuFrameTime > 0 ? t.cpuFrameTime : -1;
        }

        void Clear() { GpuMs = GpuLast = CpuMainMs = CpuRenderMs = PresentWaitMs = CpuFrameMs = -1; }
    }
}
