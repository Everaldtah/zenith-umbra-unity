// Batches every ZuDynamics instance into one Burst job per frame. A hidden runner MonoBehaviour's LateUpdate (after the
// Animators have posed the bodies) gathers each hero's frame inputs into persistent NativeArrays, schedules DynJob over all
// characters, completes it, and lets each instance write its bone rotations. Membership changes (spawn / despawn / enable)
// rebuild the arrays, carrying the surviving instances' state across so nobody's hair pops.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ZU.Dynamics
{
    public enum DynQuality { Full = 0, Half = 1, Off = 2 }

    public static class ZuDynamicsManager
    {
        /// <summary>Global wind (world, m/s^2 of acceleration before each kind's `wind` scale). The TS breeze is added on top.</summary>
        public static float3 Wind = float3.zero;
        /// <summary>Strength of the built-in gusting breeze (1 = the TS breeze, 0 = none).</summary>
        public static float GustScale = 1f;
        /// <summary>Sub-step rate of Full quality (Half quality runs at half this).</summary>
        public static int SubstepHz = 120;
        /// <summary>Longest frame simulated in full (longer hitches drop time instead of exploding): maxSteps x h.</summary>
        public static int MaxStepsPerFrame = 16;
        /// <summary>Solver constants (iterations, speed cap, rest drag, gravity, display extrapolation).</summary>
        public static DynSettings Settings = DynSettings.Default;
        /// <summary>LOD: distances from `LodOrigin` (the camera when null and one exists) beyond which heroes drop to Half / Off.</summary>
        public static Transform LodOrigin;
        public static float LodHalfDistance = 30f, LodOffDistance = 80f;

        public static int InstanceCount => instances.Count;
        public static float SimTime { get; private set; }

        static readonly List<ZuDynamics> instances = new List<ZuDynamics>();
        static readonly List<ZuDynamics> batched = new List<ZuDynamics>();   // the instances in the current arrays, in order
        static bool dirty;
        static Runner runner;

        // the batch
        static NativeArray<CharFrame> chars;
        static NativeArray<Capsule> capsCur, capsPrev;
        static NativeArray<ChainDesc> chains;
        static NativeArray<float3> anchorCur, anchorPrev, rigid, x, prev, shown;
        static NativeArray<float> len, stiff, drag, rface, rmax;
        static NativeArray<LateralPair> pairs;
        static float[] acc;                 // per character: the fixed-step accumulator

        internal static void Register(ZuDynamics d)
        {
            if (instances.Contains(d)) return;
            instances.Add(d); dirty = true;
            if (runner == null)
            {
                var go = new GameObject("ZuDynamicsManager") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<Runner>();
            }
        }

        internal static void Unregister(ZuDynamics d)
        {
            if (instances.Remove(d)) dirty = true;
        }

        /// <summary>Force the arrays to be rebuilt next frame (an instance re-bound its rig).</summary>
        internal static void Invalidate() => dirty = true;

        [DefaultExecutionOrder(500)]
        sealed class Runner : MonoBehaviour
        {
            void LateUpdate() => Step(Time.deltaTime);
            void OnDestroy() => DisposeArrays();
        }

        static void DisposeArrays()
        {
            if (chars.IsCreated) chars.Dispose();
            if (capsCur.IsCreated) capsCur.Dispose(); if (capsPrev.IsCreated) capsPrev.Dispose();
            if (chains.IsCreated) chains.Dispose();
            if (anchorCur.IsCreated) anchorCur.Dispose(); if (anchorPrev.IsCreated) anchorPrev.Dispose();
            if (rigid.IsCreated) rigid.Dispose(); if (x.IsCreated) x.Dispose(); if (prev.IsCreated) prev.Dispose(); if (shown.IsCreated) shown.Dispose();
            if (len.IsCreated) len.Dispose(); if (stiff.IsCreated) stiff.Dispose(); if (drag.IsCreated) drag.Dispose(); if (rface.IsCreated) rface.Dispose(); if (rmax.IsCreated) rmax.Dispose();
            if (pairs.IsCreated) pairs.Dispose();
            batched.Clear();
        }

        /// <summary>Repack the arrays for the current instance set, keeping the state of instances that were already batched.</summary>
        static void Rebuild()
        {
            dirty = false;
            var specs = new List<CharSpec>();
            var keep = new List<ZuDynamics>();
            foreach (var d in instances) if (d.Bound) { specs.Add(d.Spec); keep.Add(d); }
            var L = DynLayout.Build(specs);
            int NC = DynLayout.NC, nP = L.ParticleCount;
            var nChars = new NativeArray<CharFrame>(L.chars, Allocator.Persistent);
            var nCaps = new NativeArray<Capsule>(math.max(1, L.chars.Length * NC), Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nCapsPrev = new NativeArray<Capsule>(nCaps.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nChains = new NativeArray<ChainDesc>(L.chains.Length > 0 ? L.chains : new ChainDesc[1], Allocator.Persistent);
            var nAnchor = new NativeArray<float3>(math.max(1, L.chains.Length), Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nAnchorPrev = new NativeArray<float3>(nAnchor.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nRigid = new NativeArray<float3>(math.max(1, nP), Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nX = new NativeArray<float3>(nRigid.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nPrev = new NativeArray<float3>(nRigid.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nShown = new NativeArray<float3>(nRigid.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var nLen = new NativeArray<float>(nP > 0 ? L.len : new float[1], Allocator.Persistent);
            var nStiff = new NativeArray<float>(nP > 0 ? L.stiff : new float[1], Allocator.Persistent);
            var nDrag = new NativeArray<float>(nP > 0 ? L.drag : new float[1], Allocator.Persistent);
            var nRface = new NativeArray<float>(nP > 0 ? L.rface : new float[1], Allocator.Persistent);
            var nRmax = new NativeArray<float>(nP > 0 ? L.rmax : new float[1], Allocator.Persistent);
            var nPairs = new NativeArray<LateralPair>(L.pairs.Length > 0 ? L.pairs : new LateralPair[1], Allocator.Persistent);
            var nAcc = new float[L.chars.Length];
            // carry the sim state of instances that were already in the batch
            for (int i = 0; i < keep.Count; i++)
            {
                var d = keep[i];
                int old = batched.IndexOf(d);
                if (old < 0 || !chars.IsCreated) continue;
                CharFrame oc = chars[old], nc = nChars[i];
                if (oc.chainCount != nc.chainCount) continue;
                nc.hSim = oc.hSim; nc.yawPrev = oc.yawPrev; nc.yawSim = oc.yawSim; nc.yawCur = oc.yawCur;
                nc.facePrev = oc.facePrev; nc.faceCur = oc.faceCur; nc.reset = oc.reset; nc.nanResets = oc.nanResets;
                nChars[i] = nc;
                nAcc[i] = acc[old];
                for (int s = 0; s < NC; s++) { nCaps[i * NC + s] = capsCur[old * NC + s]; nCapsPrev[i * NC + s] = capsPrev[old * NC + s]; }
                for (int c = 0; c < nc.chainCount; c++)
                {
                    ChainDesc od = chains[oc.chainStart + c], nd = nChains[nc.chainStart + c];
                    if (od.n != nd.n) continue;
                    nd.alive = od.alive; nd.anchorSim = od.anchorSim;
                    nChains[nc.chainStart + c] = nd;
                    nAnchor[nc.chainStart + c] = anchorCur[oc.chainStart + c];
                    nAnchorPrev[nc.chainStart + c] = anchorPrev[oc.chainStart + c];
                    for (int k = 0; k < nd.n; k++)
                    {
                        nX[nd.pStart + k] = x[od.pStart + k]; nPrev[nd.pStart + k] = prev[od.pStart + k];
                        nShown[nd.pStart + k] = shown[od.pStart + k]; nRigid[nd.pStart + k] = rigid[od.pStart + k];
                    }
                }
            }
            DisposeArrays();
            chars = nChars; capsCur = nCaps; capsPrev = nCapsPrev; chains = nChains; anchorCur = nAnchor; anchorPrev = nAnchorPrev;
            rigid = nRigid; x = nX; prev = nPrev; shown = nShown; len = nLen; stiff = nStiff; drag = nDrag; rface = nRface; rmax = nRmax; pairs = nPairs;
            acc = nAcc;
            batched.AddRange(keep);
            for (int i = 0; i < keep.Count; i++) keep[i].Slot = i;
        }

        static unsafe void Step(float dt)
        {
            if (dirty) Rebuild();
            int n = batched.Count;
            if (n == 0) return;
            SimTime += dt;
            float3 wind = Wind + DynCore.Gust(SimTime) * GustScale;
            Transform lod = LodOrigin != null ? LodOrigin : (Camera.main != null ? Camera.main.transform : null);
            // 1. gather (main thread: Transform reads)
            var batch = new DynBatch
            {
                chars = (CharFrame*)NativeArrayUnsafeUtility.GetUnsafePtr(chars),
                capsCur = (Capsule*)NativeArrayUnsafeUtility.GetUnsafePtr(capsCur), capsPrev = (Capsule*)NativeArrayUnsafeUtility.GetUnsafePtr(capsPrev),
                chains = (ChainDesc*)NativeArrayUnsafeUtility.GetUnsafePtr(chains),
                anchorCur = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(anchorCur), anchorPrev = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(anchorPrev),
                rigid = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(rigid),
                len = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(len), stiff = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(stiff), drag = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(drag),
                rface = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(rface), rmax = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(rmax),
                x = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(x), prev = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(prev), shown = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(shown),
                pairs = (LateralPair*)NativeArrayUnsafeUtility.GetUnsafePtr(pairs),
                S = Settings,
            };
            int active = 0;
            for (int i = 0; i < n; i++)
            {
                var d = batched[i];
                ref CharFrame C = ref batch.chars[i];
                var q = d.EffectiveQuality(lod);
                if (q == DynQuality.Off || !d.isActiveAndEnabled) { C.steps = 0; d.SkippedThisFrame = true; d.PendingReset = true; continue; }
                d.SkippedThisFrame = false;
                float h = q == DynQuality.Half ? 2f / SubstepHz : 1f / SubstepHz;
                DynCore.Schedule(ref C, ref acc[i], dt, h, MaxStepsPerFrame);
                d.Gather(ref batch, i, wind, dt);
                active++;
            }
            if (active == 0) return;
            // 2. solve (all characters, one parallel job; skipped characters are no-ops: steps = 0 and no gather means
            //    their inputs are stale, so they are excluded by the per-character `steps < 0` sentinel)
            for (int i = 0; i < n; i++) if (batched[i].SkippedThisFrame) batch.chars[i].steps = -1;
            new DynJob { B = batch }.Schedule(n, 1).Complete();
            // 3. apply (main thread: Transform writes)
            for (int i = 0; i < n; i++) if (!batched[i].SkippedThisFrame) batched[i].Apply(ref batch, i);
        }
    }
}
