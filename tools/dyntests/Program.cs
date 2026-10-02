// Behaviour tests for the hair / cloth solver core, single-threaded, no Burst. Each test prints its numbers; the process
// exits 1 if any assertion failed. `DynTests.exe perf` adds a timing run.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace ZU.Dynamics.Tests
{
    static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}"); if (!ok) failures++; }

        static int Main(string[] args)
        {
            Console.WriteLine("ZU.Dynamics solver tests");
            HangingChainSettles();
            LengthsPreserved();
            SkirtVsLegs();
            TeleportAndClamp();
            FrameRateIndependence();
            NoNaN();
            Determinism();
            Perf();
            Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        // ---- helpers -----------------------------------------------------------------------------------------------

        static unsafe float MaxLengthError(Arena A, bool shown)
        {
            float worst = 0;
            for (int c = 0; c < A.nChains; c++)
            {
                var d = A.B.chains[c];
                if (shown && d.P.simW < 1f) continue;    // the shown chain of a simW < 1 kind is a blend by design
                float3 prev = shown ? A.B.anchorCur[c] : d.anchorSim;
                for (int k = 0; k < d.n; k++)
                {
                    int i = d.pStart + k;
                    float3 x = shown ? A.B.shown[i] : A.B.x[i];
                    float L = A.B.len[i] * A.Char(0).sc;
                    worst = math.max(worst, math.abs(math.distance(x, prev) - L) / L);
                    prev = x;
                }
            }
            return worst;
        }

        static unsafe bool AllFinite(Arena A)
        {
            for (int i = 0; i < A.nParts; i++) if (!DynMath.Finite(A.B.x[i]) || !DynMath.Finite(A.B.prev[i]) || !DynMath.Finite(A.B.shown[i])) return false;
            return true;
        }

        /// <summary>Deepest intrusion of any skirt particle (as a sphere of particleR) into the leg capsules, in metres.</summary>
        static unsafe float MaxLegPenetration(Arena A, int ci)
        {
            float worst = 0;
            ref CharFrame C = ref A.Char(ci);
            var cc = A.Caps(ci);
            for (int c = C.chainStart; c < C.chainStart + C.chainCount; c++)
            {
                var d = A.B.chains[c];
                if (d.kind != (int)DynKind.Skirt) continue;
                for (int k = 0; k < d.n; k++)
                {
                    float3 x = A.B.x[d.pStart + k];
                    foreach (var s in new[] { BodyCol.ThighL, BodyCol.ThighR, BodyCol.ShinL, BodyCol.ShinR })
                    {
                        var cap = cc[(int)s];
                        if (cap.on == 0) continue;
                        worst = math.max(worst, DynMath.Penetration(x, cap.a, cap.b, cap.r + C.particleR));
                    }
                }
            }
            return worst;
        }

        /// <summary>A running gait: thighs swing +-swing rad about X at `hz`, opposite phase; shins bend back on the swing-through.</summary>
        static Func<BodyCol, quaternion> Gait(float t, float hz = 3f, float swing = 0.6f)
        {
            float ph = t * hz * 2f * math.PI;
            return col =>
            {
                float side = col == BodyCol.ThighL || col == BodyCol.ShinL ? 1f : -1f;
                float a = math.sin(ph) * swing * side;
                if (col == BodyCol.ThighL || col == BodyCol.ThighR) return quaternion.AxisAngle(new float3(1, 0, 0), -a);   // -a: +Z (forward) swing
                // knee bend, folds back as the leg comes through
                float bend = math.max(0f, math.sin(ph + 1.2f) * side) * 1.1f;
                return quaternion.AxisAngle(new float3(1, 0, 0), bend);
            };
        }

        static SynthRig Kaienish()
        {
            var r = new SynthRig { heroId = "kaien" };
            float headY = r.hipY + 0.58f + 0.062f * r.H * 0.85f;
            r.AddHanging("hair_L", "head", new float3(0.07f, headY + 0.05f, 0.06f), 3, 0.09f);
            r.AddHanging("hair_R", "head", new float3(-0.07f, headY + 0.05f, 0.06f), 3, 0.09f);
            r.AddChain("hair_T", "head", new float3(0, headY + 0.1f, -0.02f), new float3(0, headY + 0.18f, -0.03f), new float3(0, headY + 0.25f, -0.05f), new float3(0, headY + 0.3f, -0.08f));
            r.AddHanging("sleeve_L", "forearm_L", new float3(0.35f, r.hipY + 0.3f, 0), 3, 0.1f);
            r.AddHanging("sleeve_R", "forearm_R", new float3(-0.35f, r.hipY + 0.3f, 0), 3, 0.1f);
            r.AddHanging("cape_B", "chest", new float3(0, r.hipY + 0.45f, -0.14f), 3, 0.3f);
            r.AddSkirt(0.22f, 3, 0.15f);
            return r;
        }

        // ---- tests ---------------------------------------------------------------------------------------------------

        /// <summary>A cape hung from a fixed anchor, started horizontal, must fall to a straight hanging line and stop dead.</summary>
        static unsafe void HangingChainSettles()
        {
            Console.WriteLine("1. hanging chain settles under gravity and stays still");
            var rig = new SynthRig { heroId = "" };
            rig.AddHanging("cape_B", "chest", new float3(0, 1.4f, -0.14f), 3, 0.3f);
            var spec = rig.Build();
            using var A = new Arena(DynLayout.Build(new[] { spec }));
            float3 pos = float3.zero;
            // frame 0: the animated pose points the cape straight back (horizontal) so the chain initialises horizontal
            rig.WriteFrame(A, 0, pos, 0, p => quaternion.AxisAngle(new float3(1, 0, 0), math.PI / 2), null, true, false);
            A.Frame(1f / 60f);
            float3 tip0 = A.B.x[2];
            // then the pose hangs down (gravity + stiffness bring the sim down)
            for (int f = 0; f < 60 * 4; f++) { rig.WriteFrame(A, 0, pos, 0, null, null, true, false); A.Frame(1f / 60f); }
            float3 anchor = A.B.anchorCur[0];
            float hang = math.distance(A.B.x[2], anchor), total = 0.9f;
            float maxV = 0; for (int i = 0; i < 3; i++) maxV = math.max(maxV, math.length(A.B.x[i] - A.B.prev[i]) * 120f);
            Console.WriteLine($"  tip start y={tip0.y:F3} -> y={A.B.x[2].y:F4}; tip distance from anchor {hang:F6} m (rest length {total}); residual speed {maxV:E2} m/s");
            Check(math.abs(hang - total) < 1e-4f, "hangs at full rest length");
            Check(A.B.x[2].y < anchor.y - 0.89f, "tip is straight below the anchor");
            // stays still: another 2 s, no particle moves more than 10 um
            float3[] snap = new float3[3]; for (int i = 0; i < 3; i++) snap[i] = A.B.x[i];
            float drift = 0;
            for (int f = 0; f < 120; f++) { rig.WriteFrame(A, 0, pos, 0, null, null, true, false); A.Frame(1f / 60f); for (int i = 0; i < 3; i++) drift = math.max(drift, math.distance(snap[i], A.B.x[i])); }
            Console.WriteLine($"  drift over 2 s at rest: {drift:E2} m");
            Check(drift < 1e-5f, "no jitter at rest (< 10 um over 2 s)");
        }

        /// <summary>Every segment keeps its length exactly through a violent run (sim state and the displayed chain).</summary>
        static unsafe void LengthsPreserved()
        {
            Console.WriteLine("2. segment lengths preserved");
            var rig = Kaienish();
            using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
            var rnd = new Unity.Mathematics.Random(7);
            float worstSim = 0, worstShown = 0; float3 pos = float3.zero; float yaw = 0;
            for (int f = 0; f < 1200; f++)
            {
                float t = f / 60f;
                pos += new float3(math.sin(t * 2.1f), 0, math.cos(t * 1.3f)) * 7f / 60f;
                if (f % 90 == 0) pos.y = 1.2f; else pos.y = math.max(0, pos.y - 9.8f * 0.5f / 60f);
                yaw += (rnd.NextFloat() - 0.5f) * 0.6f;
                rig.WriteFrame(A, 0, pos, yaw, null, Gait(t));
                A.Frame(1f / 60f);
                worstSim = math.max(worstSim, MaxLengthError(A, false));
                worstShown = math.max(worstShown, MaxLengthError(A, true));
            }
            Console.WriteLine($"  max relative length error: sim {worstSim:E2}, shown (simW = 1 chains) {worstShown:E2}");
            Check(worstSim < 1e-4f, "sim lengths within 1e-4 relative");
            Check(worstShown < 1e-4f, "shown lengths within 1e-4 relative");
        }

        /// <summary>A skirt ring around running / jumping legs: no particle ever intrudes into a thigh or shin capsule.</summary>
        static unsafe void SkirtVsLegs()
        {
            Console.WriteLine("3. skirt ring vs running / jumping legs");
            var rig = new SynthRig { heroId = "kaien" };
            rig.AddSkirt(0.22f, 3, 0.15f);
            using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
            float pr = rig.particleR, worst = 0, worstRing = 0, worstLen = 0; int over = 0;
            float3 pos = float3.zero; float yaw = 0;
            for (int f = 0; f < 2000; f++)
            {
                float t = f / 60f;
                // run forward at 6 m/s, a jump every 2 s, a turn every 5 s
                pos += new float3(math.sin(yaw), 0, math.cos(yaw)) * 6f / 60f;
                float jt = t % 2f; pos.y = jt < 0.7f ? 3.4f * jt - 0.5f * 9.8f * jt * jt : 0f;
                if (f % 300 >= 290) yaw += math.PI / 2 / 10f;    // a 90 degree turn over 10 frames (a fast flick)
                bool grounded = pos.y <= 1e-4f;
                rig.WriteFrame(A, 0, pos, yaw, null, Gait(t), grounded, true);
                A.Frame(1f / 60f);
                float pen = MaxLegPenetration(A, 0);
                worst = math.max(worst, pen);
                if (pen > 0.1f * pr) { over++; if (over <= 8) Console.WriteLine($"    frame {f}: penetration {pen * 1000:F2} mm (steps {A.Char(0).steps}, uEnd {A.Char(0).uEnd:F3}, y {pos.y:F2}, turning {f % 300 >= 290})"); }
                worstLen = math.max(worstLen, MaxLengthError(A, false));
                // the ring holds: neighbouring panels within the band
                for (int p = 0; p < A.nPairs; p++)
                {
                    var P = A.B.pairs[p]; float d = math.distance(A.B.x[P.a], A.B.x[P.b]) / P.d0;
                    worstRing = math.max(worstRing, math.max(P.lo - d, d - P.hi));
                }
            }
            Console.WriteLine($"  particle radius {pr * 1000:F2} mm; max penetration {worst * 1000:F3} mm (limit {0.1f * pr * 1000:F2} mm), frames over limit {over}/2000; ring band excess {worstRing:F3}; length error {worstLen:E2}");
            Check(worst < 0.1f * pr, "skirt never penetrates the leg capsules (max < 0.1 x particle radius)");
            Check(worstRing < 0.05f, "skirt panels stay within their lateral band (+-5% slack)");
        }

        /// <summary>Teleport() resets with zero velocity; a big jump without it is caught by the teleport distance; a jump under it is speed-clamped.</summary>
        static unsafe void TeleportAndClamp()
        {
            Console.WriteLine("4. teleport / respawn and large displacement clamp");
            var rig = Kaienish();
            using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
            float3 pos = float3.zero;
            for (int f = 0; f < 120; f++) { pos.z += 4f / 60f; rig.WriteFrame(A, 0, pos, 0, null, Gait(f / 60f)); A.Frame(1f / 60f); }
            // (a) Teleport(): 5 m jump, the chains restart at the animated pose with no velocity
            A.Teleport(0);
            pos.x += 5f;
            rig.WriteFrame(A, 0, pos, 0, null, Gait(2f));
            A.Frame(1f / 60f);
            float maxV = 0, maxOff = 0;
            for (int c = 0; c < A.nChains; c++)
            {
                var d = A.B.chains[c]; float3 p = A.B.anchorCur[c];
                for (int k = 0; k < d.n; k++)
                {
                    int i = d.pStart + k; p += A.B.rigid[i] * A.B.len[i];
                    maxV = math.max(maxV, math.length(A.B.x[i] - A.B.prev[i]) * 120f);
                    maxOff = math.max(maxOff, math.distance(A.B.shown[i], p));
                }
            }
            Console.WriteLine($"  Teleport(): max particle speed after the jump {maxV:E2} m/s (gravity alone gives {9.8f / 60f:F2} in a frame), max offset from the animated pose {maxOff * 1000:F2} mm");
            Check(maxV < 9.8f / 60f && maxOff < 0.005f, "Teleport(): no velocity beyond one frame of gravity, chain on the animated pose");
            // (b) 5 m jump WITHOUT Teleport(): beyond 1.5 H the chain auto-resets, no whip
            for (int f = 0; f < 30; f++) { pos.z += 4f / 60f; rig.WriteFrame(A, 0, pos, 0, null, Gait(f / 60f)); A.Frame(1f / 60f); }
            pos.x += 5f;
            rig.WriteFrame(A, 0, pos, 0, null, Gait(3f));
            A.Frame(1f / 60f);
            maxV = 0; for (int i = 0; i < A.nParts; i++) maxV = math.max(maxV, math.length(A.B.x[i] - A.B.prev[i]) * 120f);
            Console.WriteLine($"  5 m jump without Teleport(): max particle speed {maxV:E2} m/s (auto-reset past 1.5 H)");
            Check(maxV < 9.8f / 60f, "large displacement past the teleport distance resets instead of whipping");
            // (c) 1.5 m jump in one frame (under 1.5 H): the chains follow, relative speed capped at DYN_VMAX
            for (int f = 0; f < 30; f++) { pos.z += 4f / 60f; rig.WriteFrame(A, 0, pos, 0, null, Gait(f / 60f)); A.Frame(1f / 60f); }
            // (legs held still from here so the measured relative speed is the whip, not the knees pushing the skirt)
            pos.x += 1.5f;
            rig.WriteFrame(A, 0, pos, 0, null, Gait(3.5f, 3f, 0f));
            A.Frame(1f / 60f);
            float jumpV = 0; for (int i = 0; i < A.nParts; i++) jumpV = math.max(jumpV, math.length(A.B.x[i] - A.B.prev[i]) * 120f);
            float maxLen = MaxLengthError(A, false);
            // the frames after: the whip is capped (DYN_VMAX before the constraints, the cone snap on top) and dies within a few frames
            var rel = new float[36];
            for (int f = 0; f < 36; f++)
            {
                pos.z += 4f / 60f;
                rig.WriteFrame(A, 0, pos, 0, null, Gait(3.52f + f / 60f, 3f, 0f));
                A.Frame(1f / 60f);
                for (int c = 0; c < A.nChains; c++)
                {
                    var d = A.B.chains[c];
                    float3 vS = new float3(0, 0, 4f / 60f) * d.P.inertT / 2f;    // felt anchor motion per step (2 steps at 60 fps)
                    for (int k = 0; k < d.n; k++) { int i = d.pStart + k; float rv = math.length(A.B.x[i] - A.B.prev[i] - vS) * 120f; rel[f] = math.max(rel[f], rv); if (rv > 9f && Environment.GetEnvironmentVariable("DYN_VERBOSE") != null) Console.WriteLine($"    f{f} chain {rig.chains[c].prefix} k{k}: {rv:F1} m/s x={A.B.x[i]} anchor={A.B.anchorCur[c]}"); }
                }
            }
            Console.WriteLine($"  1.5 m jump in one frame without Teleport(): anchor at 90 m/s; particle speed in the jump frame {jumpV:F1} m/s (the chain is dragged along, it cannot stretch); character-relative speed over the next 0.6 s (every 3rd frame): {string.Join(" ", Array.ConvertAll(Array.FindAll(rel, (v) => Array.IndexOf(rel, v) % 3 == 0), v => v.ToString("F1")))} m/s (cap {DynTables.DYN_VMAX}); length error {maxLen:E2}, finite {AllFinite(A)}");
            Check(jumpV < 90f, "a sub-teleport jump never moves a particle faster than its anchor");
            float relMax = 0; foreach (var v in rel) relMax = math.max(relMax, v);
            Check(relMax < DynTables.DYN_VMAX * 3f && rel[35] < DynTables.DYN_VMAX, "the swing-back is bounded (< 3 x DYN_VMAX) and under the cap within 0.6 s");
            Check(maxLen < 1e-4f && AllFinite(A), "lengths exact and state finite after the jump");
        }

        /// <summary>The same 6 s of motion at 30 and 144 fps must end in nearly the same place.</summary>
        static unsafe void FrameRateIndependence()
        {
            Console.WriteLine("5. frame-rate independence (30 vs 144 vs 60 fps, same motion, compared mid-motion)");
            float3[] Run(float fps)
            {
                var rig = Kaienish();
                using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
                float dt = 1f / fps; int frames = (int)math.round(2.4f * fps);   // ends mid-run, mid-jump, mid-turn
                for (int f = 0; f <= frames; f++)
                {
                    float t = f * dt;
                    // a smooth analytic path: run in a circle, slow down, a hop and a half turn
                    float speed = t < 3f ? 5f : 5f * math.max(0f, 1f - (t - 3f) * 2f);
                    float yaw = t * 0.8f + math.smoothstep(1.8f, 2.3f, t) * math.PI;
                    float3 pos = new float3(math.sin(t * 0.8f) * 2f, 0, (1 - math.cos(t * 0.8f)) * 2f) * (speed / 5f);
                    float jt = t - 2f; if (jt > 0 && jt < 0.7f) pos.y = 3.4f * jt - 4.9f * jt * jt;
                    rig.WriteFrame(A, 0, pos, yaw, null, Gait(t, 2.5f, 0.5f * math.saturate(speed / 5f)), pos.y <= 1e-4f, speed > 0.3f || pos.y > 1e-4f);
                    A.Frame(dt);
                }
                var outp = new float3[A.nParts]; for (int i = 0; i < A.nParts; i++) outp[i] = A.B.shown[i];
                return outp;
            }
            var a = Run(30f); var b = Run(144f); var c = Run(60f);
            float d30 = 0, d60 = 0;
            for (int i = 0; i < a.Length; i++) { d30 = math.max(d30, math.distance(a[i], b[i])); d60 = math.max(d60, math.distance(c[i], b[i])); }
            Console.WriteLine($"  max displayed particle difference at t = 2.4 s (mid-motion): 30 vs 144 fps {d30 * 1000:F3} mm, 60 vs 144 fps {d60 * 1000:F3} mm");
            Check(d30 < 0.02f, "30 fps and 144 fps agree within 2 cm");
            Check(d60 < 0.02f, "60 fps and 144 fps agree within 2 cm");
            // a 0.1 s hitch at 60 fps (one frame of 6 x 1/60): same motion, the sim catches up in 12 fixed steps
            float3[] RunHitch(bool hitch)
            {
                var rig = Kaienish();
                using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
                float t = 0; float worstLen = 0, maxV = 0;
                while (t < 2.4f - 1e-4f)
                {
                    float dt = hitch && math.abs(t - 1f) < 1e-4f ? 0.1f : 1f / 60f;
                    t += dt;
                    float speed = t < 3f ? 5f : 0f;
                    float yaw = t * 0.8f + math.smoothstep(1.8f, 2.3f, t) * math.PI;
                    float3 pos = new float3(math.sin(t * 0.8f) * 2f, 0, (1 - math.cos(t * 0.8f)) * 2f);
                    float jt = t - 2f; if (jt > 0 && jt < 0.7f) pos.y = 3.4f * jt - 4.9f * jt * jt;
                    rig.WriteFrame(A, 0, pos, yaw, null, Gait(t, 2.5f, 0.5f), pos.y <= 1e-4f, true);
                    A.Frame(dt);
                    worstLen = math.max(worstLen, MaxLengthError(A, false));
                    if (hitch && math.abs(t - 1.1f) < 1e-4f) { for (int i = 0; i < A.nParts; i++) maxV = math.max(maxV, math.length(A.B.x[i] - A.B.prev[i]) * 120f); Console.WriteLine($"  0.1 s hitch frame: {A.Char(0).steps} fixed steps, max particle speed after it {maxV:F2} m/s, length error {worstLen:E2}"); }
                }
                var outp = new float3[A.nParts]; for (int i = 0; i < A.nParts; i++) outp[i] = A.B.shown[i];
                return outp;
            }
            var h0 = RunHitch(false); var h1 = RunHitch(true);
            float dh = 0; for (int i = 0; i < h0.Length; i++) dh = math.max(dh, math.distance(h0[i], h1[i]));
            Console.WriteLine($"  with vs without the hitch, 1.4 s later: {dh * 1000:F3} mm apart");
            Check(dh < 0.02f, "a 0.1 s hitch leaves no lasting difference (< 2 cm after 1.4 s)");
        }

        /// <summary>20000 frames of random violent motion: never a NaN, never an infinite value.</summary>
        static unsafe void NoNaN()
        {
            Console.WriteLine("6. no NaN after 20000 steps of random violent motion");
            var rig = Kaienish();
            using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
            var rnd = new Unity.Mathematics.Random(12345);
            string mode = Environment.GetEnvironmentVariable("DYN_NAN_MODE") ?? "";
            float3 pos = float3.zero; float yaw = 0; bool finite = true; int firstBad = -1, badFrames = 0;
            for (int f = 0; f < 20000; f++)
            {
                // random dt (hitches to 0.25 s), random jumps up to 3 m, random spins, random leg flails, some frames with Teleport
                float dt = rnd.NextFloat() < 0.02f ? rnd.NextFloat(0.1f, 0.25f) : rnd.NextFloat(0.004f, 0.034f);
                pos += rnd.NextFloat3(-3f, 3f) * (rnd.NextFloat() < 0.1f ? 1f : 0.05f);
                yaw += rnd.NextFloat(-math.PI, math.PI) * (rnd.NextFloat() < 0.2f ? 1f : 0.05f);
                float sw = rnd.NextFloat(0f, 1.4f);
                if (rnd.NextFloat() < 0.002f && mode != "noteleport") A.Teleport(0);
                if (mode == "nodt") dt = 1f / 60f;
                if (mode == "noyaw") yaw = 0;
                if (mode == "nojump") pos = new float3(0, 0, f * 0.05f);
                Func<string, quaternion> br = mode == "nobone" ? null : p => quaternion.Euler(rnd.NextFloat3(-1f, 1f));
                rig.WriteFrame(A, 0, pos, yaw, br, Gait(f * 0.01f, 5f, mode == "nogait" ? 0f : sw), rnd.NextBool(), rnd.NextBool());
                A.Char(0).wind = mode == "nowind" ? float3.zero : rnd.NextFloat3(-30f, 30f);
                int resetsBefore = A.Char(0).nanResets;
                float3 anchorBefore = A.B.anchorPrev[0];
                A.Frame(dt);
                if (!AllFinite(A)) { finite = false; if (firstBad < 0) firstBad = f; }
                if (A.Char(0).nanResets != resetsBefore && ++badFrames <= 3)
                    Console.WriteLine($"    NaN guard fired at frame {f}: dt {dt:F4} steps {A.Char(0).steps} anchor jump {math.distance(anchorBefore, A.B.anchorCur[0]):F2} m yaw {yaw:F2} wind {A.Char(0).wind} grounded {A.Char(0).grounded} moving {A.Char(0).moving}");
            }
            Console.WriteLine($"  finite after 20000 frames: {finite} (first bad frame {firstBad}); chains re-initialised by the NaN guard: {A.Char(0).nanResets}; final length error {MaxLengthError(A, false):E2}");
            Check(finite, "all state finite");
            Check(A.Char(0).nanResets == 0, "the NaN guard never had to fire");
        }

        /// <summary>Two identical runs give bit-identical state.</summary>
        static unsafe void Determinism()
        {
            Console.WriteLine("7. determinism");
            float3[] Run()
            {
                var rig = Kaienish();
                using var A = new Arena(DynLayout.Build(new[] { rig.Build() }));
                var rnd = new Unity.Mathematics.Random(99); float3 pos = 0; float yaw = 0;
                for (int f = 0; f < 600; f++) { pos += rnd.NextFloat3(-0.1f, 0.1f); yaw += rnd.NextFloat(-0.2f, 0.2f); rig.WriteFrame(A, 0, pos, yaw, null, Gait(f / 60f)); A.Frame(1f / 60f); }
                var o = new float3[A.nParts]; for (int i = 0; i < A.nParts; i++) o[i] = A.B.x[i]; return o;
            }
            var a = Run(); var b = Run(); bool same = true;
            for (int i = 0; i < a.Length; i++) if (math.any(a[i] != b[i])) same = false;
            Check(same, "identical inputs give bit-identical state");
        }

        /// <summary>Single-threaded, no Burst: 10 kaien-like heroes, 60 fps frames (2 sub-steps each).</summary>
        static unsafe void Perf()
        {
            Console.WriteLine("8. timing (managed, single thread, no Burst; Burst + parallel-for in Unity is far faster)");
            var rigs = new List<SynthRig>(); var specs = new List<CharSpec>();
            for (int i = 0; i < 10; i++) { var r = Kaienish(); specs.Add(r.Build()); rigs.Add(r); }
            using var A = new Arena(DynLayout.Build(specs));
            void Step(int f) { for (int i = 0; i < 10; i++) rigs[i].WriteFrame(A, i, new float3(i * 2f, 0, f * 0.1f), f * 0.01f, null, Gait(f / 60f)); A.Frame(1f / 60f); }
            for (int f = 0; f < 60; f++) Step(f);
            var sw = Stopwatch.StartNew(); int N = 2000;
            for (int f = 0; f < N; f++) Step(f);
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
            Console.WriteLine($"  {A.nChars} heroes, {A.nChains} chains, {A.nParts} particles, {A.nPairs} ring pairs: {us:F1} us per 60 fps frame including input generation");
        }
    }
}
