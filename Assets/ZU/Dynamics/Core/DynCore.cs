// The hair / cloth solver: bone chains as position-based particles in WORLD space (the method of VRM SpringBone / Kawaii
// Physics / Dynamic Bone, what hero shooters use for capes, coats, hair and scarves), one character per call, all of a
// character's chains together so the skirt ring can couple them. Pure static code over DynBatch pointers: Burst compiles it
// inside DynJob, the test harness calls it directly.
//
// Per fixed sub-step h (120 Hz), for every chain root -> tip (TS Animator.dynamics):
//   inertia      the chain is carried along with its anchor by (1 - inertT) of the anchor's move and (1 - inertR) of the
//                hero's turn; the rest is "felt" as motion through the air (Kawaii's world damping, Dynamic Bone's Inert)
//   Verlet       v = clamp_rel(x - prev, vmax) * keep, gravity, wind; keep = (1 - drag)^(h*60) (drag is per 1/60 s)
//   stiffness    lerp toward the animated direction of the segment, carried by the simulated bend of the segments above it;
//                pull = 1 - (1 - stiff)^(h*60); roots stiff, tips loose (the t^0.8 curve)
//   collision    body capsules swept through the frame, the face guard, the ground; each particle may be pushed out of a
//                capsule only as far as it sat from it in the bind pose (cloth modelled inside a measured collider)
//   constraints  cone limit maxA about the carried animated direction, then the exact segment length
// then `iters` Gauss-Seidel rounds of [lateral bands (skirt ring + diagonals) -> cone -> collision -> length] so the final
// state has exact lengths, no collider penetration (collision wins over the cone when they conflict) and panels that hold
// together. Output: the sim shifted to the frame's anchor, extrapolated to the frame time, blended toward the animated pose
// by 1 - simW.
using Unity.Mathematics;

namespace ZU.Dynamics
{
    public static unsafe class DynCore
    {
        const int NC = (int)BodyCol.Count;

        /// <summary>
        /// Fixed-step schedule for a frame of `dt` seconds: `acc` carries the unsimulated remainder between frames. The frame
        /// is clamped to maxSteps steps (a long hitch drops time instead of exploding), so the sim never runs more than
        /// maxSteps * h per frame. Sets C.steps / uEnd / du / alpha / h.
        /// </summary>
        public static void Schedule(ref CharFrame C, ref float acc, float dt, float h, int maxSteps)
        {
            dt = math.max(dt, 0f);
            acc += dt;
            // the small bias keeps an exact multiple (60 fps = 2 x 1/120) from flickering between n-1 and n+1 steps
            int steps = (int)math.floor(acc / h + 1e-3f);
            if (steps > maxSteps) { steps = maxSteps; acc = steps * h; }
            acc = math.max(0f, acc - steps * h);
            C.steps = steps; C.h = h;
            C.du = dt > 0f ? h / dt : 0f;
            C.uEnd = dt > 0f ? 1f - acc / dt : 1f;
            C.alpha = acc / h;
        }

        /// <summary>The TS breeze: a light gusting wind (acceleration, m/s^2 before the per-kind `wind` scale) at sim time t.</summary>
        public static float3 Gust(float t)
        {
            float gust = 0.6f + 0.4f * math.sin(t * 0.63f) * math.sin(t * 0.21f + 1.3f);
            return new float3(math.sin(t * 0.37f) * 1.4f, 0f, math.cos(t * 0.29f) * 1.1f) * (gust * 0.9f);
        }

        /// <summary>Solve one character for this frame (all its sub-steps), then write its display positions.</summary>
        public static void SolveCharacter(ref DynBatch b, int ci)
        {
            ref CharFrame C = ref b.chars[ci];
            if (C.steps < 0) return;    // not gathered this frame (LOD off / disabled): leave its state alone
            Capsule* cc = b.capsCur + ci * NC;
            Capsule* cp = b.capsPrev + ci * NC;
            int c0 = C.chainStart, c1 = C.chainStart + C.chainCount;

            // first frame / Teleport: nothing to sweep from, everything starts at the animated pose
            if (C.reset != 0)
            {
                for (int s = 0; s < NC; s++) cp[s] = cc[s];
                C.facePrev = C.faceCur;
                C.yawPrev = C.yawCur; C.yawSim = C.yawCur;
                for (int c = c0; c < c1; c++) { b.anchorPrev[c] = b.anchorCur[c]; b.chains[c].alive = 0; }
            }
            // a respawn / teleport the game didn't announce: an anchor that jumped further than a hero can move in a frame
            // restarts its chain at the new pose instead of being swept 5 m through the level (checked per frame here, and
            // per sub-step in StepChain as a backstop)
            for (int c = c0; c < c1; c++)
                if (b.chains[c].alive != 0 && math.distance(b.anchorPrev[c], b.anchorCur[c]) > C.teleportDist) { b.chains[c].alive = 0; b.anchorPrev[c] = b.anchorCur[c]; }
            // a quality (LOD) switch changed the step length: the Verlet velocities are per step, rescale them
            if (C.hSim > 0f && C.h != C.hSim)
            {
                float k = C.h / C.hSim;
                for (int c = c0; c < c1; c++)
                {
                    ref ChainDesc d = ref b.chains[c];
                    for (int p = d.pStart; p < d.pStart + d.n; p++) b.prev[p] = b.x[p] - (b.x[p] - b.prev[p]) * k;
                }
            }
            C.hSim = C.h;

            float h = C.h, f = h * 60f, dYaw = DynMath.WrapAngle(C.yawCur - C.yawPrev);
            for (int st = 1; st <= C.steps; st++)
            {
                float u = math.saturate(C.uEnd - (C.steps - st) * C.du);
                float yawU = C.yawPrev + dYaw * u;
                for (int c = c0; c < c1; c++) StepChain(ref b, ref C, ref b.chains[c], c, cc, cp, u, yawU, h, f);
                C.yawSim = yawU;
                for (int it = 0; it < b.S.iters; it++)
                {
                    Lateral(ref b, ref C);
                    for (int c = c0; c < c1; c++) Constrain(ref b, ref C, ref b.chains[c], c, cc, cp, u, it == 0);
                }
            }

            // display + the NaN guard, then roll the frame's inputs into "previous"
            for (int c = c0; c < c1; c++) Output(ref b, ref C, ref b.chains[c], c);
            for (int s = 0; s < NC; s++) cp[s] = cc[s];
            C.facePrev = C.faceCur;
            C.yawPrev = C.yawCur;
            for (int c = c0; c < c1; c++) b.anchorPrev[c] = b.anchorCur[c];
            C.reset = 0;
        }

        /// <summary>Start a chain at the animated pose with no velocity (first frame, respawn, teleport, non-finite state).</summary>
        static void Init(ref DynBatch b, ref CharFrame C, ref ChainDesc d, float3 anchor)
        {
            float3 p = anchor;
            for (int k = 0; k < d.n; k++)
            {
                int i = d.pStart + k;
                p += b.rigid[i] * (b.len[i] * C.sc);
                b.x[i] = p; b.prev[i] = p;
            }
            d.anchorSim = anchor;
            d.alive = 1;
        }

        /// <summary>One chain, one sub-step: inertia carry, Verlet, stiffness, collisions, cone + length.</summary>
        static void StepChain(ref DynBatch b, ref CharFrame C, ref ChainDesc d, int c, Capsule* cc, Capsule* cp, float u, float yawU, float h, float f)
        {
            float3 aNew = math.lerp(b.anchorPrev[c], b.anchorCur[c], u);
            float3 vS = float3.zero;    // the anchor motion this step that the chain feels (per step)
            if (d.alive == 0 || math.distance(d.anchorSim, aNew) > C.teleportDist)
            {
                Init(ref b, ref C, ref d, aNew);
            }
            else
            {
                // inertia: carry the chain along with the anchor by the part of its move (and turn) the chain doesn't feel;
                // moving x and prev together keeps each particle's own velocity
                float3 dA = aNew - d.anchorSim;
                vS = dA * d.P.inertT;
                float3 carry = dA * (1f - d.P.inertT);
                float turn = (1f - d.P.inertR) * DynMath.WrapAngle(yawU - C.yawSim);
                for (int k = 0; k < d.n; k++)
                {
                    int i = d.pStart + k;
                    b.x[i] += carry; b.prev[i] += carry;
                    if (turn != 0f) { b.x[i] = DynMath.YawAbout(b.x[i], aNew, turn); b.prev[i] = DynMath.YawAbout(b.prev[i], aNew, turn); }
                }
                d.anchorSim = aNew;
            }

            float vmax = b.S.vmax * h, hh = h * h;
            float dragMul = C.moving != 0 ? 1f : b.S.restDragMul;
            float3 grav = new float3(0, -b.S.g * d.P.grav * hh, 0), wind = C.wind * (d.P.wind * hh);
            float3 prevP = aNew;
            quaternion rot = quaternion.identity;
            for (int k = 0; k < d.n; k++)
            {
                int i = d.pStart + k;
                float L = b.len[i] * C.sc;
                float keep = math.pow(1f - math.min(0.9f, b.drag[i] * dragMul), f), pull = 1f - math.pow(1f - b.stiff[i], f);
                // the animated direction of this segment, carried by the simulated rotation of the segments above it
                float3 rigidW = math.normalize(math.mul(rot, b.rigid[i]));
                float3 target = prevP + rigidW * L;
                // Verlet with drag (air resistance in world space): velocity relative to the anchor's felt motion, clamped
                float3 x = b.x[i], v = x - b.prev[i] - vS;
                float vl2 = math.lengthsq(v);
                if (vl2 > vmax * vmax) v *= vmax * math.rsqrt(vl2);
                v = (v + vS) * keep;
                b.prev[i] = x;
                x += v + grav + wind;
                // stiffness: back toward the animated pose
                x = math.lerp(x, target, pull);
                Collide(ref b, ref C, ref d, k, ref x, cc, cp, u);
                // hard segment length and the cone limit off the animated pose
                float3 dir = Direction(x - prevP, rigidW);
                dir = DynMath.ConeLimit(dir, rigidW, d.P.maxA);
                x = prevP + dir * L;
                b.x[i] = x;
                rot = math.mul(DynMath.FromTo(rigidW, dir), rot);
                prevP = x;
            }
        }

        /// <summary>
        /// Constraint round for one chain, root -> tip: cone limit (first round only - it is a soft aesthetic limit and must
        /// not fight the colliders), collision, exact length. Ends on the length projection so lengths are exact.
        /// </summary>
        static void Constrain(ref DynBatch b, ref CharFrame C, ref ChainDesc d, int c, Capsule* cc, Capsule* cp, float u, bool cone)
        {
            float3 prevP = d.anchorSim;
            quaternion rot = quaternion.identity;
            for (int k = 0; k < d.n; k++)
            {
                int i = d.pStart + k;
                float L = b.len[i] * C.sc;
                float3 rigidW = math.normalize(math.mul(rot, b.rigid[i]));
                float3 x = b.x[i];
                if (cone)
                {
                    float3 d0 = DynMath.ConeLimit(Direction(x - prevP, rigidW), rigidW, d.P.maxA);
                    x = prevP + d0 * L;
                }
                Collide(ref b, ref C, ref d, k, ref x, cc, cp, u);
                float3 dir = Direction(x - prevP, rigidW);
                x = prevP + dir * L;
                b.x[i] = x;
                rot = math.mul(DynMath.FromTo(rigidW, dir), rot);
                prevP = x;
            }
        }

        /// <summary>Unit direction of `v`, or the fallback when it is degenerate.</summary>
        static float3 Direction(float3 v, float3 fallback)
        {
            float l2 = math.lengthsq(v);
            return l2 > 1e-12f ? v * math.rsqrt(l2) : fallback;
        }

        /// <summary>Push particle k of a chain out of its colliders (swept to u), the face guard and the ground.</summary>
        static void Collide(ref DynBatch b, ref CharFrame C, ref ChainDesc d, int k, ref float3 x, Capsule* cc, Capsule* cp, float u)
        {
            int mask = d.P.colMask;
            float* rmax = b.rmax + d.rmaxStart + k * NC;
            for (int s = 0; s < NC; s++)
            {
                if ((mask & (1 << s)) == 0 || cc[s].on == 0) continue;
                // the push-out radius: the capsule plus the particle's own radius, but never past where the particle sat in the
                // bind pose (cloth modelled inside a wide hip capsule must not be shoved out of it every frame)
                float r = math.min(cc[s].r + C.particleR, rmax[s] * C.sc);
                if (r <= 0f) continue;
                float3 a = math.lerp(cp[s].a, cc[s].a, u), bb = math.lerp(cp[s].b, cc[s].b, u);
                DynMath.PushOut(ref x, a, bb, r);
            }
            if (d.hasFace != 0 && C.faceCur.on != 0)
            {
                float r = b.rface[d.pStart + k] * C.sc;
                if (r > 0f) { float3 fc = math.lerp(C.facePrev.c, C.faceCur.c, u); DynMath.PushOut(ref x, fc, fc, r); }
            }
            if (C.grounded != 0 && x.y < C.groundY) x.y = C.groundY;
        }

        /// <summary>Skirt ring: a PBD distance band between matching levels of neighbouring (and opposite) panels.</summary>
        static void Lateral(ref DynBatch b, ref CharFrame C)
        {
            for (int p = C.pairStart; p < C.pairStart + C.pairCount; p++)
            {
                LateralPair P = b.pairs[p];
                float d0 = P.d0 * C.sc;
                float3 dv = b.x[P.b] - b.x[P.a];
                float d = math.length(dv);
                if (d < 1e-6f) continue;
                float want = math.clamp(d, d0 * P.lo, d0 * P.hi);
                if (want == d) continue;
                float3 corr = dv * ((d - want) / d * 0.5f);
                b.x[P.a] += corr; b.x[P.b] -= corr;
            }
        }

        /// <summary>
        /// The shown chain: the sim moved onto the frame's anchor (only directions reach the bones, so a rigid shift is
        /// exact), extrapolated by the fraction of a step the frame is ahead of the sim, lengths re-projected, then blended
        /// toward the animated pose by 1 - simW (the sim itself keeps its own state). Non-finite state resets the chain.
        /// </summary>
        static void Output(ref DynBatch b, ref CharFrame C, ref ChainDesc d, int c)
        {
            float3 aCur = b.anchorCur[c];
            if (d.alive == 0) Init(ref b, ref C, ref d, aCur);    // reset in a frame too short for a sub-step: start at the pose
            else
            {
                bool ok = true;
                for (int k = 0; ok && k < d.n; k++) { int i = d.pStart + k; ok = DynMath.Finite(b.x[i]) && DynMath.Finite(b.prev[i]); }
                if (!ok) { Init(ref b, ref C, ref d, aCur); C.nanResets++; }
            }

            float alpha = b.S.extrapolate != 0 ? math.saturate(C.alpha) : 0f;
            float3 shift = aCur - d.anchorSim;
            float3 prevS = aCur, prevA = aCur;
            float w = 1f - d.P.simW;
            for (int k = 0; k < d.n; k++)
            {
                int i = d.pStart + k;
                float L = b.len[i] * C.sc;
                float3 xe = b.x[i] + (b.x[i] - b.prev[i]) * alpha + shift;
                float3 dir = Direction(xe - prevS, b.rigid[i]);
                float3 sim = prevS + dir * L;
                float3 anim = prevA + b.rigid[i] * L;
                b.shown[i] = w > 0f ? math.lerp(sim, anim, w) : sim;
                prevS = sim; prevA = anim;
            }
        }
    }
}
