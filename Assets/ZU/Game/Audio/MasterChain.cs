// The master output chain every sound goes through on its way to the speakers (the TS desktop edition's glue compressor,
// brick-wall limiter and output meter - audio/Sfx.ts unlock() + ZuMeter - rebuilt as sample-accurate DSP for Unity, whose
// default output has no compressor or limiter: a dense fight simply summed past full scale and crackled):
//
//   glue      a gentle stereo-linked bus compressor (soft knee) that holds a dense fight together without pumping; the
//             "night" mix squeezes harder so quiet sounds come up and blasts come down
//   limiter   a look-ahead brick-wall limiter on the TRUE peak (the samples and the points between them): the gain is
//             already down when a peak arrives, so nothing the mix does can push the output past the ceiling (-1 dBTP)
//   safety    a final clamp (never reached while the limiter works; it keeps a NaN or a runaway sum off the device)
//   meter     peak, RMS, momentary loudness (BS.1770 K-weighted, 400 ms), how hard the limiter worked, clipped samples into
//             and out of the chain, and isolated clicks (the TS ZuMeter's curvature rule) - the numbers the audio QA reads
//
// Engine-free (no UnityEngine): MasterBus feeds it from OnAudioFilterRead on the listener; tools/audio/dsptest runs it
// against synthetic worst cases with plain dotnet. Process() runs on the audio thread: no allocation, no locks.
using System;

namespace ZU.Game.Audio
{
    public sealed class MasterChain
    {
        // ------------------------------------------------------------------------------------------------ settings
        /// <summary>output ceiling, dBTP</summary>
        public float CeilingDb = -1f;
        /// <summary>glue compressor: threshold (dBFS), ratio, knee width (dB), attack / release (s)</summary>
        public float GlueThreshDb = -16f, GlueRatio = 2f, GlueKneeDb = 8f, GlueAttack = 0.012f, GlueRelease = 0.18f;
        /// <summary>limiter release (s): fast enough not to hold a dip after a blast, slow enough not to distort bass</summary>
        public float LimitRelease = 0.09f;
        /// <summary>gain after the glue (the master volume rides on AudioListener.volume, before this chain)</summary>
        public float MakeupDb = 0f;

        /// <summary>Settings > Sound > Dynamic Range (ZuSettings.sound.range): "home" = home theater, the widest range (a
        /// light glue: blasts stay big against quiet moments, for good speakers or headphones in a quiet room); "normal" = the
        /// default; "night" = Overwatch's Night Mode (quiet sounds up, blasts down: low volume, or a noisy room). Older
        /// callers pass the mix preset name: anything that isn't "home" or "night" is normal.</summary>
        public void SetPreset(string range)
        {
            switch (range)
            {
                case "home": GlueThreshDb = -10f; GlueRatio = 1.5f; GlueKneeDb = 10f; MakeupDb = 0f; break;
                case "night": GlueThreshDb = -30f; GlueRatio = 4f; GlueKneeDb = 10f; MakeupDb = 6f; break;
                default: GlueThreshDb = -16f; GlueRatio = 2f; GlueKneeDb = 8f; MakeupDb = 0f; break;
            }
        }

        // ------------------------------------------------------------------------------------------------ state
        // The look-ahead limiter, the classic construction: per frame the gain r[n] that keeps its true peak under the
        // ceiling; the running MINIMUM m of r over the last L frames; the running MEAN of m over L frames; the audio
        // delayed by L - 1 frames. For a peak entering at frame n, every m[n .. n+L-1] <= r[n], so the mean at frame
        // n+L-1 - exactly when that peak leaves the delay - is <= r[n]: no overshoot, and the gain got there along a smooth
        // L-frame ramp (a step in gain is itself a click).
        const int L = 96;                           // 2 ms at 48 kHz
        const int CH = 8;                           // most channels handled (7.1)
        // true peak = the samples and the 3 points between each pair, by 4x polyphase windowed-sinc interpolation (the
        // BS.1770 method); the interpolator looks H frames ahead, so the requirement for a frame is known H frames late
        // and the audio waits that much longer: delay D = L - 1 + H
        const int H = 16, TAPS = 2 * H;
        const int D = L - 1 + H;
        readonly float[] delay = new float[(D + 1) * CH];
        readonly float[] tpHist = new float[TAPS * CH];   // ring: the last TAPS input frames per channel
        readonly float[] tpCoef = new float[3 * TAPS];    // phases 1/4, 2/4, 3/4
        const float TP_MARGIN = 0.977f;                    // -0.2 dB: what a 32-tap interpolator can still miss
        const float TP_QUIET = 0.35f;                      // -9 dB under the ceiling: no point between samples gets over
        long tpLoudUntil = -1;                             // interpolate only while a frame this loud is in the taps' reach
        readonly float[] req = new float[L];        // ring: required gain per frame
        readonly long[] dq = new long[L + 1];       // monotonic deque of frame numbers: the running minimum of req
        int dqHead, dqTail;
        readonly float[] box = new float[L];        // ring: the running minimums, for their running mean
        double boxSum = L;
        long n;                                     // frames processed
        float limGain = 1f, glueEnv;
        int sr = 48000;
        float aGA, aGR, aLR;                        // smoothing coefficients for the current rate
        readonly double[] kz = new double[4 * CH];  // K-weighting (BS.1770) biquad state per channel
        double kb0, kb1, kb2, ka1, ka2, ra1, ra2;
        float p1, p2, cAvg = 1e-4f;                 // the click detector (channel 0, as the TS)
        readonly double[] mBlocks = new double[4];  // momentary loudness: 4 x 100 ms blocks of K-weighted power
        double mAcc; int mCount, mBlock, mBlocksFilled;

        public MasterChain() { for (int i = 0; i < L; i++) { req[i] = 1; box[i] = 1; } Prepare(48000); }

        // ------------------------------------------------------------------------------------------------ meter
        /// <summary>readouts since the last ResetMeter (written by the audio thread, read anywhere)</summary>
        public volatile float Peak, PeakIn, Rms, MomentaryLufs = -70f, MaxMomentaryLufs = -70f, MaxReductionDb, ReductionDb;
        public volatile int ClipsIn, ClipsOut, Clicks, OverCeiling;
        public long Frames;
        double rmsAcc; long rmsN;
        /// <summary>the latency the chain adds (frames)</summary>
        public static int LatencyFrames => D;

        public void ResetMeter()
        {
            Peak = 0; PeakIn = 0; Rms = 0; MaxMomentaryLufs = -70f; MaxReductionDb = 0; ClipsIn = 0; ClipsOut = 0; Clicks = 0;
            OverCeiling = 0; Frames = 0; rmsAcc = 0; rmsN = 0;
        }

        public string Readout() =>
            $"master: peak {Db(Peak):0.0} dBFS (in {Db(PeakIn):0.0}), rms {Db(Rms):0.0}, M {MomentaryLufs:0.0} LUFS (max {MaxMomentaryLufs:0.0}), " +
            $"limiter {ReductionDb:0.0} dB (max {MaxReductionDb:0.0}, {OverCeiling} frames over), clips in {ClipsIn} out {ClipsOut}, clicks {Clicks}, frames {Frames}";

        static float Db(float x) => 20f * (float)Math.Log10(Math.Max(1e-6f, x));
        static float FromDb(float db) => (float)Math.Pow(10, db / 20.0);

        // ------------------------------------------------------------------------------------------------ setup
        public void Prepare(int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            sr = sampleRate;
            aGA = 1f - (float)Math.Exp(-1.0 / (GlueAttack * sr));
            aGR = 1f - (float)Math.Exp(-1.0 / (GlueRelease * sr));
            aLR = 1f - (float)Math.Exp(-1.0 / (LimitRelease * sr));
            // K-weighting at this rate (the same derivation as tools/audio/meter.py)
            double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
            double K = Math.Tan(Math.PI * f0 / sr), Vh = Math.Pow(10, G / 20), Vb = Math.Pow(Vh, 0.4996667741545416), a0 = 1 + K / Q + K * K;
            kb0 = (Vh + Vb * K / Q + K * K) / a0; kb1 = 2 * (K * K - Vh) / a0; kb2 = (Vh - Vb * K / Q + K * K) / a0;
            ka1 = 2 * (K * K - 1) / a0; ka2 = (1 - K / Q + K * K) / a0;
            f0 = 38.13547087602444; Q = 0.5003270373238773; K = Math.Tan(Math.PI * f0 / sr); a0 = 1 + K / Q + K * K;
            ra1 = 2 * (K * K - 1) / a0; ra2 = (1 - K / Q + K * K) / a0;
            // the interpolator: y(k + p/4) = sum_j x[k - H + 1 + j] * c_p[j], a Blackman-windowed sinc, each phase normalised
            for (int p = 1; p <= 3; p++)
            {
                double sum = 0;
                for (int j = 0; j < TAPS; j++)
                {
                    double t = (j - (H - 1)) - p / 4.0;        // tap j sits at x[k - H + 1 + j]: offset from the point k + p/4
                    double sinc = Math.Abs(t) < 1e-12 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                    double u = (j + 1 - p / 4.0) / (TAPS + 1);   // window position 0..1
                    double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * u) + 0.08 * Math.Cos(4 * Math.PI * u);
                    tpCoef[(p - 1) * TAPS + j] = (float)(sinc * w); sum += sinc * w;
                }
                for (int j = 0; j < TAPS; j++) tpCoef[(p - 1) * TAPS + j] /= (float)sum;
            }
        }

        // ------------------------------------------------------------------------------------------------ the audio thread
        /// <summary>process one interleaved block in place</summary>
        public void Process(float[] data, int channels)
        {
            if (data == null || channels <= 0) return;
            int ch = Math.Min(channels, CH);
            float ceiling = FromDb(CeilingDb), makeup = FromDb(MakeupDb);
            float thr = GlueThreshDb, knee = Math.Max(0.01f, GlueKneeDb), slope = 1f - 1f / Math.Max(1f, GlueRatio);
            int frames = data.Length / channels;
            float peak = Peak, peakIn = PeakIn, maxRed = MaxReductionDb;
            int clipsIn = ClipsIn, clipsOut = ClipsOut, clicks = Clicks, over = OverCeiling;
            for (int f = 0; f < frames; f++)
            {
                int b = f * channels;
                // --- glue: a stereo-linked peak-ish detector on the loudest channel, soft-knee gain computer
                float lvl = 0;
                for (int c = 0; c < ch; c++)
                {
                    float x = data[b + c];
                    if (float.IsNaN(x) || float.IsInfinity(x)) { x = 0; data[b + c] = 0; }
                    float ax = Math.Abs(x);
                    if (ax >= 0.999f) clipsIn++;
                    if (ax > peakIn) peakIn = ax;
                    if (ax > lvl) lvl = ax;
                }
                glueEnv += (lvl - glueEnv) * (lvl > glueEnv ? aGA : aGR);
                float o = Db(glueEnv) - thr, red;
                if (2 * o < -knee) red = 0;
                else if (2 * Math.Abs(o) <= knee) { float k = o + knee / 2; red = slope * k * k / (2 * knee); }
                else red = slope * o;
                float glueGain = FromDb(-red) * makeup;

                // --- this frame after the glue: into the delay and the interpolator's history; the true peak measured is
                // that of frame n - H (the points between it and the next frame need H frames of future); out of the delay
                // comes frame n - D
                int slot = (int)(n % L), dIn = (int)(n % (D + 1)), dOut = (int)((n + 1) % (D + 1)), hs = (int)(n % TAPS);
                float framePeak = 0;
                for (int c = 0; c < ch; c++)
                {
                    float x = data[b + c] * glueGain;
                    delay[dIn * CH + c] = x;
                    tpHist[c * TAPS + hs] = x;
                    if (Math.Abs(x) > TP_QUIET * ceiling) tpLoudUntil = n + TAPS;
                }
                bool interp = n < tpLoudUntil;
                for (int c = 0; c < ch; c++)
                {
                    // frame k = n - H sits at ring position (n - H) % TAPS; its sample, then the 3 fractional points after it
                    float s0 = Math.Abs(tpHist[c * TAPS + (int)((n - H + TAPS) % TAPS)]);
                    if (s0 > framePeak) framePeak = s0;
                    for (int p = 0; interp && p < 3; p++)
                    {
                        float acc = 0; int cb = p * TAPS;
                        // tap j = x[k - H + 1 + j] = x[n - 2H + 1 + j]
                        for (int j = 0; j < TAPS; j++) acc += tpHist[c * TAPS + (int)((n - 2 * H + 1 + j + 2 * TAPS) % TAPS)] * tpCoef[cb + j];
                        float a = Math.Abs(acc); if (a > framePeak) framePeak = a;
                    }
                }
                framePeak /= TP_MARGIN;

                // --- required gain -> running minimum (deque) -> running mean -> release
                float r = framePeak > ceiling ? ceiling / framePeak : 1f;
                if (framePeak > ceiling) over++;
                req[slot] = r;
                while (dqHead != dqTail && n - dq[dqHead] >= L) dqHead = (dqHead + 1) % (L + 1);   // stale first: room for n
                while (dqHead != dqTail && req[(int)(dq[(dqTail + L) % (L + 1)] % L)] >= r) dqTail = (dqTail + L) % (L + 1);
                dq[dqTail] = n; dqTail = (dqTail + 1) % (L + 1);
                float mn = req[(int)(dq[dqHead] % L)];
                boxSum += mn - box[slot]; box[slot] = mn;
                float gBox = (float)(boxSum / L);
                limGain = gBox <= limGain ? gBox : limGain + (gBox - limGain) * aLR;      // up slower than down: always safe
                float g = limGain, redDb = -Db(g);
                if (redDb > maxRed) maxRed = redDb;
                n++;

                // --- out: the delayed frame x the limiter gain, the safety clamp, the meter
                for (int c = 0; c < ch; c++)
                {
                    float y = delay[dOut * CH + c] * g;
                    if (y > 0.999f) { y = 0.999f; clipsOut++; } else if (y < -0.999f) { y = -0.999f; clipsOut++; }
                    data[b + c] = y;
                    float ay = Math.Abs(y);
                    if (ay > peak) peak = ay;
                    rmsAcc += y * y; rmsN++;
                    int z = c * 4;                              // K-weighted power (transposed direct form II, two stages)
                    double s1 = kb0 * y + kz[z]; kz[z] = kb1 * y - ka1 * s1 + kz[z + 1]; kz[z + 1] = kb2 * y - ka2 * s1;
                    double s2 = s1 + kz[z + 2]; kz[z + 2] = -2 * s1 - ra1 * s2 + kz[z + 3]; kz[z + 3] = s1 - ra2 * s2;
                    mAcc += s2 * s2;
                }
                for (int c = ch; c < channels; c++) data[b + c] = 0;     // channels past 8 (never on PC) stay silent
                float v = data[b];                                       // a click: curvature far above its running level
                float cv = Math.Abs(v - 2 * p1 + p2);
                if (cv > 0.35f && cv > cAvg * 40) clicks++;
                cAvg += (cv - cAvg) * 0.002f;
                p2 = p1; p1 = v;
                if (++mCount >= sr / 10)                                  // momentary loudness, 100 ms blocks
                {
                    mBlocks[mBlock] = mAcc / mCount; mBlock = (mBlock + 1) % 4; if (mBlocksFilled < 4) mBlocksFilled++;
                    mAcc = 0; mCount = 0;
                    double sum = 0; for (int i = 0; i < mBlocksFilled; i++) sum += mBlocks[i];
                    float m = (float)(-0.691 + 10 * Math.Log10(Math.Max(1e-12, sum / 4)));
                    MomentaryLufs = m; if (m > MaxMomentaryLufs) MaxMomentaryLufs = m;
                }
                ReductionDb = redDb;
            }
            Peak = peak; PeakIn = peakIn; MaxReductionDb = maxRed;
            ClipsIn = clipsIn; ClipsOut = clipsOut; Clicks = clicks; OverCeiling = over;
            Frames += frames;
            if (rmsN > 0) Rms = (float)Math.Sqrt(rmsAcc / rmsN);
        }
    }
}
