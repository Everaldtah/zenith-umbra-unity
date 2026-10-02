// Hair / cloth dynamics: the data the solver works on. Everything here is blittable (Burst jobs, NativeArrays) and
// free of UnityEngine so the solver core can be run and tested outside the editor (staging/Dynamics/tests).
//
// Layout (all flat, SoA, built by ZuDynamicsManager once per membership change, refreshed per frame):
//   characters  CharFrame[nChars]                        one per ZuDynamics instance
//   capsules    Capsule[nChars * BodyCol.Count]           body colliders, this frame (cur) and last frame (prev): swept
//   chains      ChainDesc[nChains]                        a character's chains are contiguous (chainStart, chainCount)
//   particles   x / prev / shown / rigid / len / stiff / drag / rface : one entry per particle (the far end of a segment)
//   rmax        float[nParticles * BodyCol.Count]         per particle, per collider: how far out it may be pushed (model units)
//   pairs       LateralPair[nPairs]                       skirt ring bands, a character's pairs are contiguous
using Unity.Mathematics;

namespace ZU.Dynamics
{
    /// <summary>What a chain is: the TS DynKind. Hair hangs; a tuft stands up off the head; cloth kinds collide with the legs.</summary>
    public enum DynKind { Hair = 0, Tuft = 1, Skirt = 2, Cape = 3, Sleeve = 4 }

    /// <summary>Body collider slots, fixed order (a chain's collider set is a bit mask over these).</summary>
    public enum BodyCol { Hips = 0, Spine, Chest, Neck, Head, UpperarmL, UpperarmR, ForearmL, ForearmR, ThighL, ThighR, ShinL, ShinR, Count }

    /// <summary>
    /// One chain's tuned values (DYN x DYN_CLASS x HERO_DYN, resolved by DynTables.Resolve). stiff / drag are fractions per
    /// 1/60 s (root, tip) and are converted exactly to the sub-step length in the solver: keep = (1-drag)^(h*60).
    /// </summary>
    public struct DynParams
    {
        public float stiffRoot, stiffTip;   // pull toward the animated pose per 1/60 s (roots hold the silhouette)
        public float dragRoot, dragTip;     // velocity lost per 1/60 s (air resistance in world space)
        public float grav;                  // g multiplier
        public float maxA;                  // cone limit (radians) off the animated direction, per joint
        public float wind;                  // how much of the wind acceleration the chain feels
        public float inertT, inertR;        // how much of the character's own move / turn the chain feels (1 = fully world space)
        public float simW;                  // final blend: 1 = pure sim, 0 = the animated pose (Blizzard's Ana coat blend)
        public int colMask;                 // which BodyCol slots this chain collides with
    }

    /// <summary>A body capsule in world space (a == b: a sphere). on == 0: the rig has no such bone.</summary>
    public struct Capsule { public float3 a, b; public float r; public int on; }

    /// <summary>The face guard sphere (world): hair anchored on the head can't fold into the eyes.</summary>
    public struct Sphere { public float3 c; public float r; public int on; }

    /// <summary>Per-character frame input (refreshed by the main thread) plus the persistent sim bookkeeping it owns.</summary>
    public struct CharFrame
    {
        public int chainStart, chainCount;  // this character's chains
        public int pairStart, pairCount;    // its lateral (skirt ring) pairs
        // sub-step schedule for this frame: `steps` fixed steps of length h; step i (1-based) samples the frame's inputs at
        // u_i = uEnd - (steps - i) * du, linearly between last frame (u = 0) and this frame (u = 1); alpha = the fraction of a
        // step the frame time is ahead of the sim time (display extrapolation)
        public int steps; public float h, uEnd, du, alpha;
        public float hSim;                  // the step length the stored Verlet velocities were made with (persistent)
        public float sc, H, particleR;      // world units per model unit; character height (world); particle radius (world)
        public float teleportDist;          // an anchor jump beyond this in one frame re-initialises the chain (TS 1.5 H)
        public float3 wind;                 // wind acceleration this frame (world, before the per-kind `wind` scale)
        public float yawPrev, yawCur;       // facing (radians about +Y, atan2(fwd.x, fwd.z)) last frame / this frame
        public float yawSim;                // facing at the sim time (persistent)
        public int moving;                  // 1 = running or airborne; 0 = at rest (drag x restDragMul, no micro-jitter)
        public int grounded; public float groundY;   // optional ground plane under the hero
        public int reset;                   // 1: re-initialise every chain at the animated pose (first frame, Teleport)
        public Sphere faceCur, facePrev;
        public int nanResets;               // diagnostics: chains re-initialised because a value went non-finite
    }

    /// <summary>One chain: n particles starting at pStart; particle k is the far end of segment k (segment 0 starts at the anchor).</summary>
    public struct ChainDesc
    {
        public DynParams P;
        public int kind;                    // DynKind
        public int n, pStart;               // particles
        public int rmaxStart;               // into rmax: n * BodyCol.Count entries
        public int hasFace;                 // uses the face guard (hair anchored on the head)
        public int alive;                   // 0 until the chain has a valid state (first frame / after Teleport)
        public float3 anchorSim;            // the anchor position at the sim time (persistent)
    }

    /// <summary>A distance band between two particles of neighbouring skirt panels: |xb - xa| kept within [lo, hi] x d0.</summary>
    public struct LateralPair { public int a, b; public float d0, lo, hi; }

    /// <summary>Solver constants (one set for the whole batch).</summary>
    public struct DynSettings
    {
        public int iters;                   // constraint iterations per sub-step (lateral bands, collisions, lengths)
        public float vmax;                  // character-relative particle speed cap, m/s (TS DYN_VMAX = 9)
        public float restDragMul;           // drag multiplier when the hero stands still (TS 1.6)
        public float g;                     // gravity, m/s^2 (TS 9.8)
        public int extrapolate;             // 1: display the chain extrapolated to the frame time (hides the sub-step lag)

        public static DynSettings Default => new DynSettings { iters = 3, vmax = 9f, restDragMul = 1.6f, g = 9.8f, extrapolate = 1 };
    }

    /// <summary>
    /// The whole batch as raw pointers (NativeArray.GetUnsafePtr in Unity, pinned memory in the tests). The job hands this
    /// to DynCore.SolveCharacter per character; characters never touch each other's data, so the parallel-for is race free.
    /// </summary>
    public unsafe struct DynBatch
    {
        public CharFrame* chars;
        public Capsule* capsCur; public Capsule* capsPrev;     // [nChars * BodyCol.Count]
        public ChainDesc* chains;
        public float3* anchorCur; public float3* anchorPrev;   // per chain: the first segment bone's world position
        public float3* rigid;                                   // per particle: the animated direction of its segment (world, unit)
        public float* len;                                      // per particle: segment length (model units)
        public float* stiff; public float* drag;                // per particle: the root->tip curve values (per 1/60 s)
        public float* rface;                                    // per particle: face guard push-out radius (model units), <= 0 = none
        public float* rmax;                                     // [nParticles * BodyCol.Count], model units (TS rmax, 0.97 x bind distance)
        public float3* x; public float3* prev; public float3* shown;
        public LateralPair* pairs;
        public DynSettings S;
    }
}
