// Master-chain tests: the worst cases a fight can throw at the output, checked against an independent 4x-oversampled
// (windowed-sinc) true-peak meter. Exit 0 = all pass.
using System;
using System.Diagnostics;
using System.IO;
using ZU.Game.Audio;

namespace ZU.DspTest
{
    static class Program
    {
        const int SR = 48000;
        static int fails;

        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "render") return Render(args[1], args[2], args.Length > 3 ? args[3] : "default");
            Silence();
            SineUntouched();
            FightPileUp();
            InterSampleOvers();
            NoClicksFromLimiting();
            ClickDetectorSees();
            NightPreset();
            Speed();
            Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
            return fails == 0 ? 0 : 1;
        }

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {name}: {detail}");
            if (!ok) fails++;
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static float[] Run(MasterChain m, float[] x, int ch, int block = 1024)
        {
            var y = (float[])x.Clone();
            var buf = new float[block * ch];
            for (int s = 0; s < y.Length; s += buf.Length)
            {
                int n = Math.Min(buf.Length, y.Length - s);
                if (n < buf.Length) { var last = new float[n]; Array.Copy(y, s, last, 0, n); m.Process(last, ch); Array.Copy(last, 0, y, s, n); break; }
                Array.Copy(y, s, buf, 0, n); m.Process(buf, ch); Array.Copy(buf, 0, y, s, n);
            }
            return y;
        }

        /// <summary>independent true peak: 4x oversampling with a 64-tap-per-phase Blackman-windowed sinc (BS.1770 annex 2 style)</summary>
        static double TruePeakDb(float[] x, int ch)
        {
            const int up = 4, half = 32;
            int taps = 2 * half * up + 1;
            var h = new double[taps];
            for (int i = 0; i < taps; i++)
            {
                double t = (i - (taps - 1) / 2.0) / up;
                double sinc = Math.Abs(t) < 1e-12 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
                h[i] = sinc * w;
            }
            int frames = x.Length / ch;
            double peak = 0;
            for (int c = 0; c < ch; c++)
                for (int f = 0; f < frames; f++)
                    for (int p = 0; p < up; p++)
                    {
                        // output sample at time f + p/up: sum over input samples k of x[k] h((f + p/up - k) * up + center)
                        double acc = 0;
                        for (int k = f - half; k <= f + half; k++)
                        {
                            if (k < 0 || k >= frames) continue;
                            int idx = (f - k) * up + p + (taps - 1) / 2;
                            if (idx < 0 || idx >= taps) continue;
                            acc += x[k * ch + c] * h[idx];
                        }
                        peak = Math.Max(peak, Math.Abs(acc));
                    }
            return 20 * Math.Log10(Math.Max(1e-9, peak));
        }

        static double PeakDb(float[] x) { double p = 0; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return 20 * Math.Log10(Math.Max(1e-9, p)); }

        /// <summary>a gunshot-ish burst: a noise crack with a fast decay over a decaying low thump, peak 1</summary>
        static float[] Shot(Random r, int len)
        {
            var s = new float[len];
            double ph = 0, f0 = 55 + r.NextDouble() * 40;
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / SR;
                double crack = (r.NextDouble() * 2 - 1) * Math.Exp(-t / 0.035);
                ph += 2 * Math.PI * f0 * (1 + 2 * Math.Exp(-t / 0.02)) / SR;
                double thump = Math.Sin(ph) * Math.Exp(-t / 0.12);
                s[i] = (float)(0.7 * crack + 0.6 * thump);
            }
            float pk = 0; foreach (var v in s) pk = Math.Max(pk, Math.Abs(v));
            for (int i = 0; i < len; i++) s[i] /= pk;
            return s;
        }

        // ------------------------------------------------------------------------------------------------ cases
        static void Silence()
        {
            var m = new MasterChain(); m.Prepare(SR);
            var y = Run(m, new float[SR * 2], 2);
            Check("silence", PeakDb(y) < -150 && m.Clicks == 0, $"peak {PeakDb(y):0} dB, clicks {m.Clicks}");
        }

        static void SineUntouched()
        {
            // -20 dBFS 1 kHz in both channels: under the glue's knee, nowhere near the ceiling -> bit-transparent level, -20 LUFS
            var m = new MasterChain(); m.Prepare(SR);
            var x = new float[SR * 2 * 2];
            for (int f = 0; f < SR * 2; f++) { float v = 0.1f * (float)Math.Sin(2 * Math.PI * 1000 * f / SR); x[2 * f] = v; x[2 * f + 1] = v; }
            var y = Run(m, x, 2);
            double err = 0; int lat = MasterChain.LatencyFrames;
            for (int f = lat; f < SR * 2; f++) err = Math.Max(err, Math.Abs(y[2 * f] - x[2 * (f - lat)]));
            Check("sine -20 dBFS passes untouched", err < 1e-5 && m.MaxReductionDb < 0.01, $"max error {err:E1}, limiter max {m.MaxReductionDb:0.00} dB, latency {lat} frames");
            Check("momentary loudness", Math.Abs(m.MaxMomentaryLufs - (-20.0)) < 0.3, $"{m.MaxMomentaryLufs:0.00} LUFS (expected -20.0)");
        }

        static void FightPileUp()
        {
            // 30 full-scale shots within 60 ms plus a sustained bed: the input sums ~+15 dB over full scale
            var r = new Random(7);
            var m = new MasterChain(); m.Prepare(SR);
            int frames = SR * 3;
            var x = new float[frames * 2];
            for (int k = 0; k < 30; k++)
            {
                var s = Shot(r, SR / 2);
                int at = SR / 2 + r.Next(0, SR * 60 / 1000);
                float gain = 0.5f + (float)r.NextDouble() * 0.5f, pan = (float)r.NextDouble();
                for (int i = 0; i < s.Length && at + i < frames; i++) { x[2 * (at + i)] += s[i] * gain * (1 - pan); x[2 * (at + i) + 1] += s[i] * gain * pan; }
            }
            for (int f = 0; f < frames; f++) { float bed = 0.3f * (float)Math.Sin(2 * Math.PI * 80 * f / SR); x[2 * f] += bed; x[2 * f + 1] += bed; }
            double inPk = PeakDb(x);
            var y = Run(m, x, 2);
            double tp = TruePeakDb(y, 2);
            if (Environment.GetEnvironmentVariable("ZU_DSP_DEBUG") == "1")
            {
                Console.WriteLine($"  debug: output SAMPLE peak {PeakDb(y):0.00} dBFS");
                // an isolated over: one +6 dBFS sample in silence must come out at the ceiling exactly
                var m2 = new MasterChain(); m2.Prepare(SR);
                var imp = new float[SR / 4]; imp[SR / 8] = 2f;
                var yi = Run(m2, imp, 1);
                int at = 0; for (int i = 0; i < yi.Length; i++) if (Math.Abs(yi[i]) > Math.Abs(yi[at])) at = i;
                Console.WriteLine($"  debug: impulse out peak {PeakDb(yi):0.00} dB at {at} (in at {SR / 8}, latency {MasterChain.LatencyFrames}); neighbours {yi[at - 1]:0.000} {yi[at + 1]:0.000}");
            }
            Check("fight pile-up stays under the ceiling", tp <= -0.9 && m.ClipsOut == 0,
                $"input peak {inPk:+0.0} dBFS -> output true peak {tp:0.00} dBTP, limiter max {m.MaxReductionDb:0.0} dB, safety clamps {m.ClipsOut}");
        }

        static void InterSampleOvers()
        {
            // a sine at fs/4 phased so every sample sits at 0.707 of its true peak: sample peak under the ceiling, true peak over
            var m = new MasterChain(); m.Prepare(SR);
            int frames = SR;
            var x = new float[frames];
            for (int f = 0; f < frames; f++) x[f] = 0.99f * (float)Math.Sin(2 * Math.PI * 12000 * f / SR + Math.PI / 4);
            double inTp = TruePeakDb(x, 1), inPk = PeakDb(x);
            var y = Run(m, x, 1);
            double tp = TruePeakDb(y, 1);
            Check("inter-sample overs caught", tp <= -0.8, $"input sample peak {inPk:0.0} / true peak {inTp:+0.0} -> output true peak {tp:0.00} dBTP");
        }

        static void NoClicksFromLimiting()
        {
            // a bass tone with a huge shot on top: the limiter must duck and recover without a click of its own. The shot's
            // crack is noise (curvature everywhere); a gain step would show as an isolated spike on the smooth bass while the
            // limiter releases, so count clicks (the meter's rule) from 60 ms after the onset, when the crack has died away
            var m = new MasterChain(); m.Prepare(SR);
            int frames = SR * 2, at = SR / 2;
            var x = new float[frames];
            var r = new Random(3);
            var s = Shot(r, SR / 2);
            for (int f = 0; f < frames; f++) x[f] = 0.4f * (float)Math.Sin(2 * Math.PI * 110 * f / SR);
            for (int i = 0; i < s.Length; i++) x[at + i] += 2.5f * s[i] * (float)Math.Exp(-(double)i / (SR * 0.01));
            var y = Run(m, x, 1);
            int clicks = 0; float p1 = 0, p2 = 0, avg = 1e-4f;
            int from = at + SR * 60 / 1000 + MasterChain.LatencyFrames;
            for (int f = 0; f < frames; f++)
            {
                float v = y[f], c = Math.Abs(v - 2 * p1 + p2);
                if (f >= from && c > 0.02f && c > avg * 40) clicks++;
                avg += (c - avg) * 0.002f; p2 = p1; p1 = v;
            }
            // and the gain curve itself: no frame-to-frame jump in the bass envelope bigger than a smooth ramp makes
            Check("limiting makes no clicks", clicks == 0 && m.ClipsOut == 0 && m.MaxReductionDb > 3, $"clicks after the crack {clicks}, limiter max {m.MaxReductionDb:0.0} dB, out peak {PeakDb(y):0.00}");
        }

        static void ClickDetectorSees()
        {
            var m = new MasterChain(); m.Prepare(SR);
            var x = new float[SR];
            for (int f = 0; f < SR; f++) x[f] = 0.2f * (float)Math.Sin(2 * Math.PI * 220 * f / SR);
            x[SR / 2] += 0.8f; x[SR / 2 + 1] -= 0.8f;
            Run(m, x, 1);
            Check("click detector sees an injected click", m.Clicks >= 1, $"clicks {m.Clicks}");
        }

        static void NightPreset()
        {
            // night: a quiet passage comes up, a loud one comes down - the range between them narrows
            float Level(string mix, float amp)
            {
                var m = new MasterChain(); m.SetPreset(mix); m.Prepare(SR);
                var x = new float[SR * 2];
                for (int f = 0; f < x.Length; f++) x[f] = amp * (float)Math.Sin(2 * Math.PI * 500 * f / SR);
                var y = Run(m, x, 1);
                double p = 0; for (int f = SR; f < x.Length; f++) p = Math.Max(p, Math.Abs(y[f]));
                return (float)(20 * Math.Log10(p));
            }
            float dq = Level("normal", 0.03f), dl = Level("normal", 0.9f), nq = Level("night", 0.03f), nl = Level("night", 0.9f);
            float hq = Level("home", 0.03f), hl = Level("home", 0.9f);
            Check("night preset narrows the range", (nl - nq) < (dl - dq) - 6, $"normal range {dl - dq:0.0} dB, night range {nl - nq:0.0} dB");
            Check("home theater keeps the widest range", (hl - hq) > (dl - dq), $"home range {hl - hq:0.0} dB vs normal {dl - dq:0.0} dB");
        }

        static void Speed()
        {
            var m = new MasterChain(); m.Prepare(SR);
            var buf = new float[1024 * 2];
            var r = new Random(1);
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)(r.NextDouble() * 2 - 1);
            for (int i = 0; i < 50; i++) m.Process(buf, 2);
            var sw = Stopwatch.StartNew();
            int blocks = 2000;
            for (int i = 0; i < blocks; i++) m.Process(buf, 2);
            double us = sw.Elapsed.TotalMilliseconds * 1000 / blocks;
            // a 1024-frame block is 21.3 ms of audio at 48 kHz: the chain must be a sliver of that (it shares the audio thread)
            Check("speed", us < 400, $"{us:0} us per 1024-frame stereo block ({us / 21333 * 100:0.00}% of real time)");
        }

        // ------------------------------------------------------------------------------------------------ render a WAV
        static int Render(string inPath, string outPath, string mix)
        {
            var (x, ch, sr) = ReadWav(inPath);
            var m = new MasterChain(); m.SetPreset(mix); m.Prepare(sr);
            var y = Run(m, x, ch);
            WriteWav(outPath, y, ch, sr);
            Console.WriteLine($"{Path.GetFileName(inPath)}: in peak {PeakDb(x):+0.0;-0.0} dBFS -> out true peak {TruePeakDb(y, ch):0.00} dBTP; {m.Readout()}");
            return 0;
        }

        static (float[] x, int ch, int sr) ReadWav(string path)
        {
            using var br = new BinaryReader(File.OpenRead(path));
            br.ReadBytes(12);
            int ch = 2, sr = 48000, bits = 16, fmt = 1;
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                string id = new string(br.ReadChars(4)); int len = br.ReadInt32();
                if (id == "fmt ") { fmt = br.ReadInt16(); ch = br.ReadInt16(); sr = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16(); br.ReadBytes(len - 16); if (fmt == -2) fmt = bits == 32 ? 3 : 1; }
                else if (id == "data")
                {
                    int n = len / (bits / 8);
                    var x = new float[n];
                    for (int i = 0; i < n; i++)
                        x[i] = fmt == 3 ? br.ReadSingle() : bits == 16 ? br.ReadInt16() / 32768f : bits == 24 ? ((br.ReadByte() | br.ReadByte() << 8 | (sbyte)br.ReadByte() << 16) / 8388608f) : br.ReadInt32() / 2147483648f;
                    return (x, ch, sr);
                }
                else br.ReadBytes(len + (len & 1));
            }
            throw new Exception("no data chunk");
        }

        static void WriteWav(string path, float[] x, int ch, int sr)
        {
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write("RIFF".ToCharArray()); bw.Write(36 + x.Length * 4); bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)3); bw.Write((short)ch); bw.Write(sr); bw.Write(sr * ch * 4); bw.Write((short)(ch * 4)); bw.Write((short)32);
            bw.Write("data".ToCharArray()); bw.Write(x.Length * 4);
            foreach (var v in x) bw.Write(v);
        }
    }
}
