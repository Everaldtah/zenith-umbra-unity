// Frame timing for the render loop (port of zenith-umbra src/engine/FramePacer.ts, engine core).
//  - measures the display's refresh interval from the frame cadence (the TS read the requestAnimationFrame cadence;
//    here Unity's frame start times, which sit on vblanks once QualitySettings.vSyncCount > 0)
//  - hands out vsync-quantized frame deltas: frame stamps jitter by a fraction of a millisecond even when every frame
//    lands on its vblank, and animating with that raw jitter is the micro-stutter Croteam traced in "The Elusive Frame
//    Timing" (GDC 2018). A delta within 12% of a whole number of refresh intervals is snapped to it; the snapping error
//    is carried as a debt and paid back gradually, so game time never drifts from the wall clock (co-op stays in sync).
//  - the TS also paced a frame cap by deadline (run on the vblank nearest each deadline). Unity owns frame submission,
//    so there is no tick to skip: a cap that divides the refresh (60 on 120/240, 72 on 144) goes to vSyncCount = k
//    (each frame held for the same number of vblanks, see Perf.SetCap) and VSyncDivisor is the TS capInterval rule.
// Unity differences: with vSyncCount = k the cadence is k vblanks, so samples are divided by `Divisor` to get the
// refresh; with vSyncCount 0 (cap by targetFrameRate, or uncapped) the cadence says nothing about the display, so the
// refresh stays at the display's reported rate (SetPrior) and deltas are not quantized (the TS: only when vsynced).
// The measured refresh replaces the reported one only from a steady cadence no faster than the reported rate (see
// Sample): unlike rAF, a Unity cadence can be free-running even with vsync asked for.
using System;

namespace ZU.Engine
{
    public sealed class FramePacer
    {
        const int RING = 120;

        /// <summary>display refresh interval (ms): the display's reported rate until measured from the cadence</summary>
        public double RefreshMs { get; private set; } = 1000.0 / 60;
        /// <summary>display refresh rate (Hz)</summary>
        public double RefreshHz => 1000 / RefreshMs;
        /// <summary>true once the cadence looks vsync-locked (steady intervals) - only then are deltas quantized</summary>
        public bool VSynced { get; private set; }
        /// <summary>true once the refresh was measured from a vsync-locked cadence (not just the display's reported rate)</summary>
        public bool Measured { get; private set; }
        /// <summary>the refresh is known - measured, or the display reported a sane rate (the cap rule needs one)</summary>
        public bool RefreshKnown => Measured || prior > 0;
        /// <summary>the last raw (unquantized) frame interval (ms)</summary>
        public double LastRawMs { get; private set; }
        /// <summary>wall clock minus game time (ms): the quantization error still to be paid back</summary>
        public double Debt => debt;

        readonly double[] ring = new double[RING], sorted = new double[RING];
        int n, divisor = 1;
        double lastT, lastFrame, debt, prior;
        bool hasT, started;

        /// <summary>vblanks per frame as applied (QualitySettings.vSyncCount): samples are divided by it; 0 = free-running,
        /// the cadence is the cap's (or none) and tells nothing about the display, so no measurement and no quantizing</summary>
        public int Divisor
        {
            get => divisor;
            // the ring restarts (its intervals were k vblanks); the refresh estimate stays - it was divided by k, so it is
            // still the display's, and dropping it for the prior would let a prior that disagrees with the measurement
            // flip the cap mode back and forth
            set { if (value == divisor) return; divisor = value; Restart(); }
        }

        /// <summary>the display's reported refresh (Screen.currentResolution.refreshRateRatio): the prior until measured;
        /// a change (the window moved to another monitor) restarts the measurement</summary>
        public void SetPrior(double hz)
        {
            if (!(hz >= 20 && hz <= 1000)) return;      // nonsense from a headless / virtual display: keep what we have
            if (Math.Abs(hz - prior) < 0.05) return;
            prior = hz;
            Reset();
        }

        /// <summary>forget the measured refresh (the display changed): it falls back to the prior until re-measured</summary>
        public void Reset()
        {
            Restart(); Measured = false;
            if (prior > 0) RefreshMs = 1000 / prior;
        }

        /// <summary>restart the cadence measurement (the cap mode changed), keeping the refresh estimate</summary>
        public void Restart() { n = 0; VSynced = false; }

        /// <summary>a new run (the Editor's play mode without a domain reload: the clock restarts at 0 while this object
        /// survives): forget the stamps and the debt, restart the cadence; the refresh estimate stays (same display)</summary>
        internal void ResetRun() { hasT = false; started = false; lastT = lastFrame = 0; debt = 0; LastRawMs = 0; Restart(); }

        /// <summary>called once per frame with the frame's start time (ms, Time.unscaledTimeAsDouble * 1000): the
        /// unscaled delta (s) for this frame, quantized when vsynced, clamped to 0..0.1 s (a stall is not a 2 s step, and a
        /// clock that went backwards - a new play without a domain reload - is a new run: 0 for that frame, never negative)</summary>
        public double Tick(double tms)
        {
            if (hasT && tms < lastT) ResetRun();
            if (hasT) Sample(tms - lastT);
            lastT = tms; hasT = true;
            if (!started) { started = true; lastFrame = tms; return 0; }
            double raw = tms - lastFrame;
            lastFrame = tms;
            // the clock ran backwards: Unity restarted it under us (a new play session in the Editor with domain reload off
            // keeps this object while Time.unscaledTime starts again from 0). A negative delta here went straight into the
            // match's step accumulator and froze every match for as long as the previous session had run - start over
            if (raw < 0) { debt = 0; Restart(); LastRawMs = 0; return 0; }
            // (the tms < lastT check above already restarted the run for that case - lastFrame == lastT once started, so
            // this branch is the lead's belt and braces and never fires on top of it)
            LastRawMs = raw;
            return Math.Max(0, Math.Min(0.1, Quantize(raw) / 1000));
        }

        /// <summary>the TS capInterval rule: the cap as a whole number k of vblanks when it divides the refresh closely
        /// enough (|refresh / cap - k| &lt; 0.06), else 0. k is capped at 4 (QualitySettings.vSyncCount's range).</summary>
        public int VSyncDivisor(int capFps)
        {
            if (capFps <= 0 || !RefreshKnown) return 0;
            double ratio = RefreshHz / capFps;
            int k = (int)Math.Round(ratio);
            return k >= 1 && k <= 4 && Math.Abs(ratio - k) < 0.06 ? k : 0;
        }

        double Quantize(double raw)
        {
            if (!VSynced || raw <= 0) return raw;
            double k = Math.Round(raw / RefreshMs);
            double q = k >= 1 && Math.Abs(raw - k * RefreshMs) <= RefreshMs * 0.12 ? k * RefreshMs : raw;
            // the debt is the wall clock minus game time: the stamps' jitter (it telescopes - bounded) plus any error in the
            // refresh estimate (it builds up). Paid back 5% a frame, like a phase-locked loop: a fraction of a percent of
            // speed, never a visible jump, and game time stays within a frame of the clock
            debt += raw - q;
            double pay = debt * 0.05;
            debt -= pay;
            return q + pay;
        }

        void Sample(double d)
        {
            if (divisor <= 0) { VSynced = false; return; }
            d /= divisor;                                  // k vblanks per frame: the refresh is a k-th of the interval
            if (d <= 1 || d > 100) return;
            ring[n % RING] = d; n++;
            if (n < 30 || n % 30 != 0) return;
            // the refresh is the low median of the recent intervals (dropped frames sit at 2x and above)
            int m = Math.Min(n, RING);
            Array.Copy(ring, sorted, m);
            Array.Sort(sorted, 0, m);                      // (no comparer: no allocation)
            double med = sorted[(int)Math.Floor(m * 0.4)];
            int close = 0; double sum = 0;
            for (int i = 0; i < m; i++) { double x = sorted[i]; if (Math.Abs(x - med) < med * 0.08) { close++; sum += x; } }
            // steady when most intervals sit on one value; the refresh is their mean (finer than any one frame stamp)
            VSynced = close > m * 0.6 && med > 3.5;
            if (close == 0) return;
            double ms = sum / close;
            // Unity: a cadence faster than the display's reported rate is not vblank-locked (the driver's control panel
            // forced vsync off, a compositor ignoring it): taking it as the refresh would starve the frame budget and
            // the dynamic resolution would chase it. Keep the prior and treat the cadence as free-running. (The TS took
            // any mean, its rAF cadence being the display's by definition.) An unsteady cadence keeps the last estimate.
            if (prior > 0 && 1000 / ms > prior * 1.05) { VSynced = false; return; }
            if (VSynced) { RefreshMs = ms; Measured = true; }
        }
    }
}
