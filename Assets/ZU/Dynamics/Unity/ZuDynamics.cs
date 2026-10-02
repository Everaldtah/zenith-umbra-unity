// Hair and cloth on one hero. Attach to the hero's root (the transform the game moves and turns), set the hero id, done:
// at bind it finds the body bones and the dynamic chains (<prefix>_1.._4) by name under its hierarchy, builds the body
// capsules from the rig's proportions, and every frame ZuDynamicsManager solves all heroes in one Burst job after the
// Animator has posed the body; Apply() then aims each chain bone down its solved segment so the skinned mesh follows.
//
// Model space here = the root's local frame without its scale (+Y up, +Z forward, like the TS Animator); `sc` = world
// units per model unit (the root's uniform scale), so a 5.4 m Susanoo works from the same tables.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ZU.Dynamics
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ZU/Dynamics (hair and cloth)")]
    public sealed class ZuDynamics : MonoBehaviour
    {
        [Tooltip("Hero id for the weight class and per-hero overrides (HERO_CLASS / HERO_DYN): kaien, tomoe, mirei, ...")]
        public string heroId = "";
        [Tooltip("Auto follows the manager's LOD distances; the others force a level.")]
        public QualityMode quality = QualityMode.Auto;
        [Tooltip("Set by the game: standing on something (the ground plane is on, chains settle at rest).")]
        public bool grounded = true;
        [Tooltip("Keep chains above the hero's feet while grounded (TS: root y + 1% of the height).")]
        public bool useGround = true;
        [Tooltip("Scale on every body collider radius.")]
        public float colliderScale = 1f;
        [Tooltip("Particle radius as a fraction of the height (TS 0.008).")]
        public float particleRadiusFrac = 0.008f;
        [Tooltip("Collider radii measured from the mesh by the rigger (model units); missing bones use fractions of the height.")]
        public List<ColliderRadius> measuredRadii = new List<ColliderRadius>();
        [Tooltip("Use `velocity` instead of the root's frame-to-frame motion for the rest / moving decision.")]
        public bool overrideVelocity;
        public Vector3 velocity;

        public enum QualityMode { Auto = 0, Full = 1, Half = 2, Off = 3 }
        [System.Serializable] public struct ColliderRadius { public string bone; public float radius; }

        /// <summary>Snap the chains to the animated pose next frame with no velocity (respawn, teleport, cutscene cut).</summary>
        public void Teleport() => PendingReset = true;
        /// <summary>Re-discover the rig (after a bone was renamed or the rest pose changed).</summary>
        public void Rebind() { Bind(); ZuDynamicsManager.Invalidate(); }

        public bool Bound { get; private set; }
        public CharSpec Spec { get; private set; }
        public int ChainCount => chains.Count;
        /// <summary>Character height in model units (head rest height x 1.08, the TS measure).</summary>
        public float Height { get; private set; }

        internal int Slot = -1;
        internal bool PendingReset = true;
        internal bool SkippedThisFrame;

        // ---- rig ---------------------------------------------------------------------------------------------------
        static readonly string[] BODY = { "root", "hips", "spine", "chest", "neck", "head", "shoulder_L", "upperarm_L", "forearm_L", "hand_L",
            "shoulder_R", "upperarm_R", "forearm_R", "hand_R", "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R", "toe_L", "toe_R" };

        sealed class Bone { public Transform t; public float3 pm; public quaternion qm; }   // bind pose in model space
        sealed class Chain
        {
            public string prefix; public DynKind kind; public Transform[] segs; public Transform parent; public Bone parentBone;
            public float3[] dm;                 // per segment: model-space rest direction to the next joint
            public quaternion[] localRest;      // per segment: the bone's local rotation at bind
            public float3[] localDir;           // per segment: direction to its child in the bone's own local space
        }
        sealed class Col { public BodyCol slot; public Bone a, b; public float r; }     // r: model units

        readonly Dictionary<string, Bone> bones = new Dictionary<string, Bone>();
        readonly List<Chain> chains = new List<Chain>();
        readonly List<Col> cols = new List<Col>();
        Bone head, hips, thighL, thighR, shinL, shinR;
        float3 thighDmL, thighDmR;              // rest direction of each thigh (model), for the skirt leg-follow
        float headR;                            // head collider radius, model units
        float3 lastPos; bool hasLastPos;

        void Awake() { if (!Bound) Bind(); }
        void OnEnable() { PendingReset = true; hasLastPos = false; ZuDynamicsManager.Register(this); }
        void OnDisable() { ZuDynamicsManager.Unregister(this); }

        float Scale => math.max(1e-4f, transform.lossyScale.y);

        float3 ToModel(float3 world, float3 rootPos, quaternion rootInv, float sc) => math.mul(rootInv, world - rootPos) / sc;

        /// <summary>Discover bones and chains from the hierarchy as it stands now (the bind / rest pose).</summary>
        void Bind()
        {
            Bound = false; bones.Clear(); chains.Clear(); cols.Clear(); Spec = null;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) { var n = t.name.Replace('.', '_'); if (!byName.ContainsKey(n)) byName[n] = t; }
            float sc = Scale; float3 rootPos = transform.position; quaternion rootRot = transform.rotation, rootInv = math.inverse(rootRot);
            Bone Make(Transform t) => new Bone { t = t, pm = ToModel(t.position, rootPos, rootInv, sc), qm = math.mul(rootInv, t.rotation) };
            foreach (var n in BODY) if (byName.TryGetValue(n, out var t)) bones[n] = Make(t);
            bones.TryGetValue("head", out head); bones.TryGetValue("hips", out hips);
            bones.TryGetValue("thigh_L", out thighL); bones.TryGetValue("thigh_R", out thighR); bones.TryGetValue("shin_L", out shinL); bones.TryGetValue("shin_R", out shinR);
            if (head == null || hips == null) { Debug.LogWarning($"ZuDynamics on {name}: no head / hips bone, nothing to simulate", this); return; }
            Height = head.pm.y * 1.08f;
            if (thighL != null && shinL != null) thighDmL = math.normalizesafe(shinL.pm - thighL.pm, new float3(0, -1, 0));
            if (thighR != null && shinR != null) thighDmR = math.normalizesafe(shinR.pm - thighR.pm, new float3(0, -1, 0));

            // colliders: capsule from a bone head to the next bone head; the head is a sphere above the head joint
            foreach (var (slot, bn, to, frac) in DynTables.COLL)
            {
                if (!bones.TryGetValue(bn, out var a) || !bones.TryGetValue(to, out var b)) continue;
                float def = frac * Height, r = def;
                foreach (var m in measuredRadii) if (m.bone == bn && m.radius > 0) r = math.clamp(m.radius, def * 0.5f, def * 1.45f);
                cols.Add(new Col { slot = slot, a = a, b = b, r = r });
                if (slot == BodyCol.Head) headR = r;
            }
            if (headR <= 0) headR = 0.062f * Height;
            var faceBind = FaceGuard(head.pm, new float3(0, 1, 0), new float3(0, 0, 1), 1f);

            // chains: prefix_1..4, the highest index is a non-deforming tip marker
            Spec = new CharSpec();
            var restJoints = new List<float3[]>();
            foreach (var pf in DynTables.CHAIN_PREFIXES)
            {
                var idx = new List<Transform>();
                for (int i = 1; i <= 4; i++) { if (byName.TryGetValue($"{pf}_{i}", out var t)) idx.Add(t); else break; }
                if (idx.Count < 2) continue;
                var kind = DynTables.KindOf(pf);
                // the body bone the chain hangs from: the nearest known ancestor, else by kind
                Transform p = idx[0].parent; Bone pb = null;
                while (p != null && p != transform) { if (bones.TryGetValue(p.name.Replace('.', '_'), out pb)) break; p = p.parent; }
                if (pb == null)
                {
                    string fb = pf.StartsWith("hair") ? "head" : pf == "sleeve_L" ? "forearm_L" : pf == "sleeve_R" ? "forearm_R" : "hips";
                    if (!bones.TryGetValue(fb, out pb)) continue;
                }
                int n = idx.Count - 1;
                var ch = new Chain { prefix = pf, kind = kind, segs = idx.GetRange(0, n).ToArray(), parent = pb.t, parentBone = pb, dm = new float3[n], localRest = new quaternion[n], localDir = new float3[n] };
                var joints = new float3[n + 1];
                for (int k = 0; k <= n; k++) joints[k] = ToModel(idx[k].position, rootPos, rootInv, sc);
                var spec = new ChainSpec { prefix = pf, kind = kind, P = DynTables.Resolve(heroId, kind, pf), len = new float[n], rmax = new float[n * DynLayout.NC] };
                bool face = pb == head && kind == DynKind.Hair;
                if (face) spec.rface = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float3 d = joints[k + 1] - joints[k];
                    spec.len[k] = math.length(d);
                    ch.dm[k] = math.normalizesafe(d, new float3(0, -1, 0));
                    ch.localRest[k] = idx[k].localRotation;
                    ch.localDir[k] = math.normalizesafe((float3)idx[k + 1].localPosition, new float3(0, 1, 0));
                    // each particle may be pushed out of a collider only as far as it sat from it in the bind pose (less a hair)
                    for (int s = 0; s < DynLayout.NC; s++) spec.rmax[k * DynLayout.NC + s] = float.PositiveInfinity;
                    foreach (var c in cols)
                    {
                        if ((spec.P.colMask & (1 << (int)c.slot)) == 0) continue;
                        float3 A = c.a.pm, B = c.b.pm;
                        if (c.slot == BodyCol.Head) { A += new float3(0, c.r * 0.85f, 0); B = A; }
                        spec.rmax[k * DynLayout.NC + (int)c.slot] = math.distance(joints[k + 1], DynMath.ClosestOnSegment(joints[k + 1], A, B)) * 0.97f;
                    }
                    if (face) spec.rface[k] = math.min(faceBind.r, math.distance(joints[k + 1], faceBind.c) * 0.97f);
                }
                chains.Add(ch); Spec.chains.Add(spec); restJoints.Add(joints);
            }
            DynLayout.AddSkirtRing(Spec, (ci, k) => restJoints[ci][k + 1]);
            Bound = chains.Count > 0;
            if (!Bound) Debug.Log($"ZuDynamics on {name}: no dynamic chains on this rig (a mech?)", this);
        }

        /// <summary>The head's face guard sphere: centre in front of the skull, radius 0.9 x the head collider (scaled by sc).</summary>
        (float3 c, float r) FaceGuard(float3 headPos, float3 up, float3 fwd, float sc)
        {
            float r = headR * sc;
            return (headPos + up * (r * 0.6f) + fwd * (r * 0.35f), r * 0.9f);
        }

        internal DynQuality EffectiveQuality(Transform lodOrigin)
        {
            switch (quality)
            {
                case QualityMode.Full: return DynQuality.Full;
                case QualityMode.Half: return DynQuality.Half;
                case QualityMode.Off: return DynQuality.Off;
            }
            if (lodOrigin == null) return DynQuality.Full;
            float d = Vector3.Distance(lodOrigin.position, transform.position);
            return d > ZuDynamicsManager.LodOffDistance ? DynQuality.Off : d > ZuDynamicsManager.LodHalfDistance ? DynQuality.Half : DynQuality.Full;
        }

        // ---- per frame ---------------------------------------------------------------------------------------------

        /// <summary>Write this frame's inputs (root frame, colliders, chain anchors and animated directions) into the batch.</summary>
        internal unsafe void Gather(ref DynBatch b, int ci, float3 wind, float dt)
        {
            ref CharFrame C = ref b.chars[ci];
            float sc = Scale; float3 rootPos = transform.position; quaternion Rw = transform.rotation, RwInv = math.inverse(Rw);
            C.sc = sc; C.H = Height * sc; C.particleR = particleRadiusFrac * C.H; C.teleportDist = 1.5f * C.H;
            C.wind = wind;
            float3 fwd = math.mul(Rw, new float3(0, 0, 1));
            C.yawCur = math.atan2(fwd.x, fwd.z);
            float3 v = overrideVelocity ? (float3)velocity : (hasLastPos && dt > 0 ? (rootPos - lastPos) / dt : float3.zero);
            lastPos = rootPos; hasLastPos = true;
            C.moving = (math.length(v.xz) > 0.3f || !grounded) ? 1 : 0;
            C.grounded = (grounded && useGround) ? 1 : 0;
            C.groundY = rootPos.y + 0.01f * C.H;
            if (PendingReset) { C.reset = 1; PendingReset = false; }

            // model-space delta of a bone (its rotation now vs the bind pose, in the root's frame)
            quaternion Delta(Bone bn) => math.mul(math.mul(RwInv, bn.t.rotation), math.inverse(bn.qm));
            quaternion dHead = Delta(head);
            float3 headUp = math.mul(Rw, math.mul(dHead, new float3(0, 1, 0))), headFwd = math.mul(Rw, math.mul(dHead, new float3(0, 0, 1)));

            // colliders (world); the skull is a sphere above the head joint
            Capsule* cc = b.capsCur + ci * DynLayout.NC;
            for (int s = 0; s < DynLayout.NC; s++) cc[s].on = 0;
            foreach (var c in cols)
            {
                float3 A = c.a.t.position, B = c.b.t.position;
                float r = c.r * sc * colliderScale;
                if (c.slot == BodyCol.Head) { A += headUp * (r * 0.85f); B = A; }
                cc[(int)c.slot] = new Capsule { a = A, b = B, r = r, on = 1 };
            }
            var fg = FaceGuard(head.t.position, headUp, headFwd, sc * colliderScale);
            C.faceCur = new Sphere { c = fg.c, r = fg.r, on = 1 };

            // chains: anchor + the animated (rigid) directions, the chain riding its parent bone
            quaternion dL = thighL != null ? Delta(thighL) : quaternion.identity, dR = thighR != null ? Delta(thighR) : quaternion.identity;
            float zL = thighL != null ? math.mul(dL, thighDmL).z : 0f, zR = thighR != null ? math.mul(dR, thighDmR).z : 0f;
            for (int c = 0; c < chains.Count; c++)
            {
                var ch = chains[c];
                int chain = C.chainStart + c;
                quaternion baseQ = Delta(ch.parentBone);
                // leg-driven skirts (the way games rig long coats and robes): a side panel follows its own thigh part of the
                // way, the front / back panel the thigh swinging into it, so the legs don't sweep through the cloth; physics
                // rides on top (a full-strength follow made coat tails kick out as stiff flat sheets on every stride)
                if (ch.kind == DynKind.Skirt && thighL != null && thighR != null)
                {
                    quaternion q; float w;
                    if (ch.prefix == "skirt_L") { q = dL; w = 0.35f; }
                    else if (ch.prefix == "skirt_R") { q = dR; w = 0.35f; }
                    else if (ch.prefix == "skirt_F") { q = zL > zR ? dL : dR; w = 0.25f; }
                    else { q = zL < zR ? dL : dR; w = 0.25f; }
                    baseQ = math.slerp(baseQ, q, w);
                }
                b.anchorCur[chain] = ch.segs[0].position;
                int pStart = b.chains[chain].pStart;
                for (int k = 0; k < ch.segs.Length; k++) b.rigid[pStart + k] = math.normalize(math.mul(Rw, math.mul(baseQ, ch.dm[k])));
            }
        }

        /// <summary>Aim each chain bone down its solved segment (root -> tip, twist following the animated rotation).</summary>
        internal unsafe void Apply(ref DynBatch b, int ci)
        {
            ref CharFrame C = ref b.chars[ci];
            for (int c = 0; c < chains.Count; c++)
            {
                var ch = chains[c];
                int chain = C.chainStart + c, pStart = b.chains[chain].pStart;
                float3 headPos = b.anchorCur[chain];
                for (int k = 0; k < ch.segs.Length; k++)
                {
                    var seg = ch.segs[k];
                    float3 target = b.shown[pStart + k];
                    float3 dir = math.normalizesafe(target - headPos, b.rigid[pStart + k]);
                    quaternion animRot = math.mul((quaternion)seg.parent.rotation, ch.localRest[k]);
                    float3 animDir = math.normalize(math.mul(animRot, ch.localDir[k]));
                    seg.rotation = math.mul(DynMath.FromTo(animDir, dir), animRot);
                    headPos = target;
                }
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Bound) return;
            float sc = Scale;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            foreach (var c in cols)
            {
                Vector3 A = c.a.t.position, B = c.b.t.position; float r = c.r * sc * colliderScale;
                if (c.slot == BodyCol.Head) { A += Vector3.up * (r * 0.85f); B = A; }
                Gizmos.DrawWireSphere(A, r); Gizmos.DrawWireSphere(B, r); Gizmos.DrawLine(A, B);
            }
            Gizmos.color = Color.yellow;
            foreach (var ch in chains) for (int k = 0; k + 1 < ch.segs.Length; k++) Gizmos.DrawLine(ch.segs[k].position, ch.segs[k + 1].position);
        }
#endif
    }
}
