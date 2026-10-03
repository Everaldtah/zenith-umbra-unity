// Dynamic render scale (port of zenith-umbra src/engine/DynamicResolution.ts, engine core), after Unreal's dynamic
// resolution as Fortnite ships it: the scene's resolution follows the GPU's measured frame time against the frame
// budget, with headroom, a panic drop and slow recovery. What it fixes in the old controller: that one lowered the
// resolution whenever the frame rate was under target - but a CPU-bound frame (the common case in a 10-hero fight)
// doesn't get faster with fewer pixels, it just gets blurrier. This one only trades pixels when the GPU is the
// bottleneck. Scale moves in 5% steps and changes rarely (each change re-allocates URP's render targets): down at most
// every 0.5 s (immediately on a panic), up a step at most every second while the load predicted for the next step
// still fits the target. For the first 3 s of a match (or after any pause in the calls) it only watches: the first
// frames' uploads and warm-up are not GPU load. The ms clock comes from the caller (Perf: Time.unscaledTimeAsDouble).
using System;

namespace ZU.Engine
{
    public sealed class DynamicResolution
    {
        /// <summary>the current dynamic scale (a multiplier on the settings render scale)</summary>
        public double Scale = 1;
        public double Min = 0.5;
        public double Max = 1;
        /// <summary>fraction of the frame budget the GPU should use (the rest absorbs spikes)</summary>
        public double Headroom = 0.85;
        /// <summary>ms after a (re)start during which the scale is held</summary>
        public double Warmup = 3000;
        double ema = -1;
        double lastChange;
        int over;
        double lastCall = double.NegativeInfinity;
        double holdUntil;

        /// <summary>settings applied: start over from this scale (the load history described another resolution)</summary>
        public void Reset(double scale) { Scale = scale; ema = -1; over = 0; }

        /// <param name="now">ms clock</param>
        /// <param name="gpuMs">measured GPU time of the last frames (-1 = unknown)</param>
        /// <param name="cpuMs">CPU time the frame's own work took (main thread)</param>
        /// <param name="frameMs">the frame's wall interval</param>
        /// <param name="budget">target frame time (ms)</param>
        /// <returns>true when <see cref="Scale"/> changed</returns>
        public bool Update(double now, double gpuMs, double cpuMs, double frameMs, double budget)
        {
            if (now - lastCall > 1000) { holdUntil = now + Warmup; ema = -1; over = 0; }   // a new match / resumed
            lastCall = now;
            if (now < holdUntil) return false;
            // without a GPU timer: infer GPU load from the frame interval, but only when the CPU isn't what's slow
            double load = gpuMs > 0 ? gpuMs : cpuMs < budget * 0.7 ? frameMs * 0.9 : -1;
            if (load <= 0) { over = 0; return false; }
            ema = ema < 0 ? load : ema + (load - ema) * 0.1;
            double want = budget * Headroom;
            over = load > budget * 1.5 ? over + 1 : 0;
            bool panic = over >= 3;
            double since = now - lastChange;
            double next = Scale;
            if (panic || (ema > budget * 0.95 && since > 500))
            {
                // pixel cost ~ scale^2: the scale that would bring the load down to the target, at least one step, at most four
                double fit = Scale * Math.Sqrt(want / (panic ? load : ema));
                next = Math.Max(Min, Math.Min(Scale - 0.05, Math.Max(Scale - 0.2, fit)));
            }
            else if (since > 1000 && Scale < Max)
            {
                // one step up if the load it would bring (pixels ~ scale^2) stays under the target, with room to spare
                double up = Math.Min(Max, Scale + 0.05), k = up / Scale;
                if (ema * k * k < want * 0.92) next = up;
            }
            next = Math.Min(Max, Math.Max(Min, Math.Round(next * 20) / 20));
            if (Math.Abs(next - Scale) < 1e-6) return false;
            Scale = next; lastChange = now; over = 0;
            ema = -1;                                        // the old load no longer describes the new resolution
            return true;
        }
    }
}
