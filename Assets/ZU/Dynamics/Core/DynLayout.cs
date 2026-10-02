// Builds the flat batch layout (characters -> chains -> particles, lateral pairs) from per-character descriptions. Managed,
// bind-time only; the result is copied into NativeArrays by ZuDynamicsManager and into unmanaged buffers by the tests.
using System.Collections.Generic;

namespace ZU.Dynamics
{
    /// <summary>One chain as the binder found it: its kind, tuning, segment lengths and bind-pose collision limits.</summary>
    public sealed class ChainSpec
    {
        public string prefix;
        public DynKind kind;
        public DynParams P;
        public float[] len;             // per particle (segment), model units
        public float[] rmax;            // per particle x BodyCol.Count, model units: the most it may be pushed out of that collider
        public float[] rface;           // per particle, model units (0 = none); null when the chain has no face guard
        public int N => len.Length;
    }

    /// <summary>A lateral band between level `level` of two chains of the same character.</summary>
    public struct PairSpec { public int chainA, chainB, level; public float d0, lo, hi; }

    public sealed class CharSpec
    {
        public List<ChainSpec> chains = new List<ChainSpec>();
        public List<PairSpec> pairs = new List<PairSpec>();
        public int ParticleCount { get { int n = 0; foreach (var c in chains) n += c.N; return n; } }
    }

    /// <summary>The packed static data of a batch (everything the solver reads but never writes, plus the initial descriptors).</summary>
    public sealed class DynLayout
    {
        public const int NC = (int)BodyCol.Count;
        public CharFrame[] chars;
        public ChainDesc[] chains;
        public LateralPair[] pairs;
        public float[] len, stiff, drag, rface, rmax;
        public int ParticleCount => len.Length;
        /// <summary>Per character: index of its first particle (for copying state across rebuilds).</summary>
        public int[] charParticleStart;

        public static DynLayout Build(IList<CharSpec> specs)
        {
            var L = new DynLayout();
            int nChains = 0, nParts = 0, nPairs = 0;
            foreach (var s in specs) { nChains += s.chains.Count; nParts += s.ParticleCount; nPairs += s.pairs.Count; }
            L.chars = new CharFrame[specs.Count];
            L.chains = new ChainDesc[nChains];
            L.pairs = new LateralPair[nPairs];
            L.len = new float[nParts]; L.stiff = new float[nParts]; L.drag = new float[nParts]; L.rface = new float[nParts];
            L.rmax = new float[nParts * NC];
            L.charParticleStart = new int[specs.Count];
            int ci = 0, pi = 0, pri = 0;
            for (int s = 0; s < specs.Count; s++)
            {
                var S = specs[s];
                L.charParticleStart[s] = pi;
                var C = new CharFrame { chainStart = ci, chainCount = S.chains.Count, pairStart = pri, pairCount = S.pairs.Count, reset = 1 };
                int chainBase = ci;
                foreach (var c in S.chains)
                {
                    var d = new ChainDesc { P = c.P, kind = (int)c.kind, n = c.N, pStart = pi, rmaxStart = pi * NC, hasFace = c.rface != null ? 1 : 0, alive = 0 };
                    for (int k = 0; k < c.N; k++)
                    {
                        float t = DynTables.CurveT(k, c.N);
                        L.len[pi + k] = c.len[k];
                        L.stiff[pi + k] = c.P.stiffRoot + (c.P.stiffTip - c.P.stiffRoot) * t;
                        L.drag[pi + k] = c.P.dragRoot + (c.P.dragTip - c.P.dragRoot) * t;
                        L.rface[pi + k] = c.rface != null ? c.rface[k] : 0f;
                        for (int j = 0; j < NC; j++) L.rmax[(pi + k) * NC + j] = c.rmax != null ? c.rmax[k * NC + j] : float.PositiveInfinity;
                    }
                    L.chains[ci++] = d;
                    pi += c.N;
                }
                foreach (var p in S.pairs)
                {
                    var A = L.chains[chainBase + p.chainA]; var B = L.chains[chainBase + p.chainB];
                    L.pairs[pri++] = new LateralPair { a = A.pStart + p.level, b = B.pStart + p.level, d0 = p.d0, lo = p.lo, hi = p.hi };
                }
                L.chars[s] = C;
            }
            return L;
        }

        /// <summary>
        /// The skirt ring bands for a character: neighbours (SKIRT_RING) within [LAT_LO, LAT_HI] of their bind spacing, opposite
        /// panels (SKIRT_DIAG) never closer than DIAG_LO. `restPos(chain, level)` returns the bind position of a particle.
        /// </summary>
        public static void AddSkirtRing(CharSpec S, System.Func<int, int, Unity.Mathematics.float3> restPos)
        {
            int Find(string pf) => S.chains.FindIndex(c => c.prefix == pf);
            void Add((string a, string b)[] list, float lo, float hi)
            {
                foreach (var (pa, pb) in list)
                {
                    int A = Find(pa), B = Find(pb);
                    if (A < 0 || B < 0) continue;
                    int n = System.Math.Min(S.chains[A].N, S.chains[B].N);
                    for (int k = 0; k < n; k++)
                    {
                        float d0 = Unity.Mathematics.math.distance(restPos(A, k), restPos(B, k));
                        if (d0 > 1e-5f) S.pairs.Add(new PairSpec { chainA = A, chainB = B, level = k, d0 = d0, lo = lo, hi = hi });
                    }
                }
            }
            Add(DynTables.SKIRT_RING, DynTables.LAT_LO, DynTables.LAT_HI);
            Add(DynTables.SKIRT_DIAG, DynTables.DIAG_LO, DynTables.DIAG_HI);
        }
    }
}
