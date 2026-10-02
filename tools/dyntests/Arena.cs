// Unmanaged buffers for a DynBatch outside Unity (Marshal.AllocHGlobal stands in for NativeArray) and a synthetic rig
// builder: chains hung from analytic anchors, thigh / shin capsules on an analytic gait. Mirrors what ZuDynamics binds
// from a real skeleton (lengths, rmax bind limits, the skirt ring) so the solver is exercised with realistic limits.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace ZU.Dynamics.Tests
{
    public unsafe sealed class Arena : IDisposable
    {
        public DynBatch B;
        public readonly DynLayout L;
        public readonly int nChars, nChains, nParts, nPairs;
        readonly float[] acc;
        readonly List<IntPtr> blocks = new List<IntPtr>();
        public float time;

        T* Alloc<T>(int count) where T : unmanaged
        {
            int bytes = Math.Max(1, count) * sizeof(T);
            IntPtr p = Marshal.AllocHGlobal(bytes);
            new Span<byte>((void*)p, bytes).Clear();
            blocks.Add(p);
            return (T*)p;
        }

        public Arena(DynLayout layout, DynSettings? settings = null)
        {
            L = layout;
            nChars = L.chars.Length; nChains = L.chains.Length; nParts = L.ParticleCount; nPairs = L.pairs.Length;
            acc = new float[nChars];
            int NC = DynLayout.NC;
            B.chars = Alloc<CharFrame>(nChars);
            B.capsCur = Alloc<Capsule>(nChars * NC); B.capsPrev = Alloc<Capsule>(nChars * NC);
            B.chains = Alloc<ChainDesc>(nChains);
            B.anchorCur = Alloc<float3>(nChains); B.anchorPrev = Alloc<float3>(nChains);
            B.rigid = Alloc<float3>(nParts); B.len = Alloc<float>(nParts); B.stiff = Alloc<float>(nParts); B.drag = Alloc<float>(nParts);
            B.rface = Alloc<float>(nParts); B.rmax = Alloc<float>(nParts * NC);
            B.x = Alloc<float3>(nParts); B.prev = Alloc<float3>(nParts); B.shown = Alloc<float3>(nParts);
            B.pairs = Alloc<LateralPair>(nPairs);
            B.S = settings ?? DynSettings.Default;
            for (int i = 0; i < nChars; i++) B.chars[i] = L.chars[i];
            for (int i = 0; i < nChains; i++) B.chains[i] = L.chains[i];
            for (int i = 0; i < nParts; i++) { B.len[i] = L.len[i]; B.stiff[i] = L.stiff[i]; B.drag[i] = L.drag[i]; B.rface[i] = L.rface[i]; }
            for (int i = 0; i < nParts * NC; i++) B.rmax[i] = L.rmax[i];
            for (int i = 0; i < nPairs; i++) B.pairs[i] = L.pairs[i];
        }

        public ref CharFrame Char(int i) => ref B.chars[i];
        public Capsule* Caps(int ci) => B.capsCur + ci * DynLayout.NC;

        /// <summary>Run one frame of `dt` for every character at step length h (inputs must already be written).</summary>
        public void Frame(float dt, float h = 1f / 120f, int maxSteps = 16)
        {
            time += dt;
            for (int i = 0; i < nChars; i++)
            {
                DynCore.Schedule(ref B.chars[i], ref acc[i], dt, h, maxSteps);
                DynCore.SolveCharacter(ref B, i);
            }
        }

        public void Teleport(int ci) { B.chars[ci].reset = 1; acc[ci] = 0; }

        public void Dispose() { foreach (var p in blocks) Marshal.FreeHGlobal(p); blocks.Clear(); }
    }

    /// <summary>
    /// A synthetic humanoid: height H, hips at hipY, thighs / shins as capsules, plus chains defined by their bind-pose joint
    /// positions (model space, +Z forward, +Y up). Model space is mapped to world by (pos, yaw) exactly like the TS toW().
    /// </summary>
    public sealed class SynthRig
    {
        public float H = 1.7f, hipY = 0.95f, kneeY = 0.5f, footY = 0.05f, hipHalfW = 0.1f;
        public string heroId = "kaien";
        public readonly List<(string prefix, string parent, float3[] joints)> chains = new List<(string, string, float3[])>();
        public CharSpec spec;
        public float particleR => 0.008f * H;

        public SynthRig AddChain(string prefix, string parent, params float3[] joints) { chains.Add((prefix, parent, joints)); return this; }

        /// <summary>A hanging chain of `n` segments of `seg` metres from `top`, straight down.</summary>
        public SynthRig AddHanging(string prefix, string parent, float3 top, int n, float seg)
        {
            var j = new float3[n + 1];
            for (int i = 0; i <= n; i++) j[i] = top + new float3(0, -seg * i, 0);
            return AddChain(prefix, parent, j);
        }

        /// <summary>A four-panel skirt ring of radius `ring` at the hips, each panel `n` segments of `seg` hanging down.</summary>
        public SynthRig AddSkirt(float ring, int n, float seg)
        {
            AddHanging("skirt_F", "hips", new float3(0, hipY, ring), n, seg);
            AddHanging("skirt_L", "hips", new float3(ring, hipY, 0), n, seg);
            AddHanging("skirt_B", "hips", new float3(0, hipY, -ring), n, seg);
            AddHanging("skirt_R", "hips", new float3(-ring, hipY, 0), n, seg);
            return this;
        }

        /// <summary>Bind-pose capsules (model space), radii from the TS COLL fractions of the height.</summary>
        public Dictionary<BodyCol, (float3 a, float3 b, float r)> RestCaps()
        {
            var d = new Dictionary<BodyCol, (float3, float3, float)>();
            d[BodyCol.ThighL] = (new float3(hipHalfW, hipY, 0), new float3(hipHalfW, kneeY, 0), 0.052f * H);
            d[BodyCol.ThighR] = (new float3(-hipHalfW, hipY, 0), new float3(-hipHalfW, kneeY, 0), 0.052f * H);
            d[BodyCol.ShinL] = (new float3(hipHalfW, kneeY, 0), new float3(hipHalfW, footY, 0), 0.04f * H);
            d[BodyCol.ShinR] = (new float3(-hipHalfW, kneeY, 0), new float3(-hipHalfW, footY, 0), 0.04f * H);
            d[BodyCol.Hips] = (new float3(0, hipY, 0), new float3(0, hipY + 0.12f, 0), 0.09f * H);
            d[BodyCol.Spine] = (new float3(0, hipY + 0.12f, 0), new float3(0, hipY + 0.3f, 0), 0.085f * H);
            d[BodyCol.Chest] = (new float3(0, hipY + 0.3f, 0), new float3(0, hipY + 0.5f, 0), 0.09f * H);
            d[BodyCol.Neck] = (new float3(0, hipY + 0.5f, 0), new float3(0, hipY + 0.58f, 0), 0.035f * H);
            float hr = 0.062f * H; float3 head = new float3(0, hipY + 0.58f + hr * 0.85f, 0);
            d[BodyCol.Head] = (head, head, hr);
            return d;
        }

        /// <summary>Build the CharSpec the way ZuDynamics does: params from the tables, rmax from the bind pose, the skirt ring.</summary>
        public CharSpec Build()
        {
            var caps = RestCaps();
            spec = new CharSpec();
            foreach (var (prefix, parent, joints) in chains)
            {
                var kind = DynTables.KindOf(prefix);
                var P = DynTables.Resolve(heroId, kind, prefix);
                int n = joints.Length - 1;
                var cs = new ChainSpec { prefix = prefix, kind = kind, P = P, len = new float[n], rmax = new float[n * DynLayout.NC] };
                for (int k = 0; k < n; k++)
                {
                    cs.len[k] = math.distance(joints[k], joints[k + 1]);
                    for (int s = 0; s < DynLayout.NC; s++)
                    {
                        cs.rmax[k * DynLayout.NC + s] = float.PositiveInfinity;
                        if ((P.colMask & (1 << s)) == 0 || !caps.TryGetValue((BodyCol)s, out var c)) continue;
                        cs.rmax[k * DynLayout.NC + s] = math.distance(joints[k + 1], DynMath.ClosestOnSegment(joints[k + 1], c.a, c.b)) * 0.97f;
                    }
                }
                spec.chains.Add(cs);
            }
            DynLayout.AddSkirtRing(spec, (ci, k) => chains[ci].joints[k + 1]);
            return spec;
        }

        public static float3 ToWorld(float3 m, float3 pos, float yaw)
        {
            math.sincos(yaw, out float sy, out float cy);
            return new float3(pos.x + m.x * cy + m.z * sy, pos.y + m.y, pos.z - m.x * sy + m.z * cy);
        }
        public static float3 DirToWorld(float3 m, float yaw)
        {
            math.sincos(yaw, out float sy, out float cy);
            return new float3(m.x * cy + m.z * sy, m.y, -m.x * sy + m.z * cy);
        }

        /// <summary>
        /// Write this character's frame inputs: root at (pos, yaw), the chains rigidly attached (their bind pose turned by
        /// yaw, optionally by an extra rotation of the parent `boneRot(parent)`), capsules from the gait rotations
        /// `legRot(col)` applied about the hip / knee joint.
        /// </summary>
        public unsafe void WriteFrame(Arena A, int ci, float3 pos, float yaw, Func<string, quaternion> boneRot = null, Func<BodyCol, quaternion> legRot = null, bool grounded = true, bool moving = true)
        {
            ref CharFrame C = ref A.Char(ci);
            C.sc = 1f; C.H = H; C.particleR = particleR; C.teleportDist = 1.5f * H;
            C.yawCur = yaw; C.moving = moving ? 1 : 0; C.grounded = grounded ? 1 : 0; C.groundY = pos.y + 0.01f * H;
            C.wind = float3.zero;
            C.faceCur.on = 0;
            var caps = RestCaps();
            var cc = A.Caps(ci);
            for (int s = 0; s < DynLayout.NC; s++)
            {
                if (!caps.TryGetValue((BodyCol)s, out var c)) { cc[s].on = 0; continue; }
                float3 a = c.a, b = c.b;
                if (legRot != null)
                {
                    var q = legRot((BodyCol)s);
                    // thighs swing about the hip joint; shins ride the thigh and bend about the knee by the same rotation
                    if (s == (int)BodyCol.ThighL || s == (int)BodyCol.ThighR) b = a + math.mul(q, b - a);
                    else if (s == (int)BodyCol.ShinL || s == (int)BodyCol.ShinR)
                    {
                        var th = caps[s == (int)BodyCol.ShinL ? BodyCol.ThighL : BodyCol.ThighR];
                        var qt = legRot(s == (int)BodyCol.ShinL ? BodyCol.ThighL : BodyCol.ThighR);
                        float3 knee = th.a + math.mul(qt, th.b - th.a);
                        b = knee + math.mul(math.mul(qt, q), b - a); a = knee;
                    }
                }
                cc[s] = new Capsule { a = ToWorld(a, pos, yaw), b = ToWorld(b, pos, yaw), r = c.r, on = 1 };
            }
            for (int c = 0; c < chains.Count; c++)
            {
                var (prefix, parent, joints) = chains[c];
                var q = boneRot != null ? boneRot(parent) : quaternion.identity;
                int chain = C.chainStart + c;
                float3 pivot = joints[0];
                A.B.anchorCur[chain] = ToWorld(pivot, pos, yaw);
                int pStart = A.B.chains[chain].pStart;
                for (int k = 0; k < joints.Length - 1; k++)
                    A.B.rigid[pStart + k] = DirToWorld(math.normalize(math.mul(q, joints[k + 1] - joints[k])), yaw);
            }
        }
    }
}
