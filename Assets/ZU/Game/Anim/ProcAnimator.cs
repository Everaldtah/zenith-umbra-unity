// The procedural animator (port of the TS render/Animator.ts, minus the hair / cloth solver - ZU.Dynamics has that - and
// the prop placement HeldRig does from the bones): legs on 2-bone IK with feet locked in the world while planted, the
// lower body facing the move and the spine unwinding it to the aim, per-hero personas (stance, weight, carriage,
// contrapposto, spring aims), squash and stretch, flinches and recoil, every hero-specific arm pose (Tenkai-Oh's hammer
// path, the archers' draw, the blade guard, Hayate's throws, Enra's chain swing and throw, the twin chainguns, Mirei's
// flight, Raijin's giant hurling bolts, Tomoe's Warpath...), the angel's wings, the knockdown sprawl and the whole-body
// tilt the view applies.
// Frames: everything is solved in the TS model space (Y up, +Z forward, +X = the character's LEFT) with the TS numbers;
// the Unity rig's model space (the prefab root's local space, +X = the character's right) is its mirror, so the rest pose
// comes in and the bone rotations go out through M() and nothing else changes. The Animator's pose (Mecanim) underneath
// stands in for the TS clip layer: its local rotations become the clip deltas the TS blends in per body region.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZU.Game.FirstPerson;

namespace ZU.Game.Anim
{
    public sealed partial class ProcAnimator
    {
        // ------------------------------------------------------------------------------------------------ frames
        /// <summary>Unity model space &lt;-&gt; TS model space (mirror X; its own inverse)</summary>
        public static Vector3 M(Vector3 v) => new Vector3(-v.x, v.y, v.z);
        public static Quaternion M(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        static readonly Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
        static Quaternion Rot(Vector3 axis, float a) => Quaternion.AngleAxis(a * Mathf.Rad2Deg, axis);
        static Vector3 Lerp(Vector3 a, Vector3 b, float t) => Vector3.LerpUnclamped(a, b, t);
        static float ClampA(float v, float m) => Mathf.Max(-m, Mathf.Min(m, v));
        static float Smooth(float u) => u * u * (3 - 2 * u);
        static float Hypot(float a, float b) => Mathf.Sqrt(a * a + b * b);
        static float Mod1(float x) => ((x % 1) + 1) % 1;

        /// <summary>damped spring toward a target, sub-stepped so stiff springs stay stable on slow frames</summary>
        sealed class Spring { public float x, v; public Spring(float x = 0) { this.x = x; } }
        static float Step(Spring s, float target, float k, float d, float dt)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f)); float h = dt / n;
            for (int i = 0; i < n; i++) { s.v += ((target - s.x) * k - s.v * d) * h; s.x += s.v * h; }
            return s.x;
        }

        // ------------------------------------------------------------------------------------------------ personas
        public struct Persona
        {
            public float weight, bounce, lean, stance, width, chest, hip, sway, aimK, aimD, lag, squash;
            public Persona(float weight, float bounce, float lean, float stance, float width, float chest, float hip, float sway, float aimK, float aimD, float lag, float squash)
            { this.weight = weight; this.bounce = bounce; this.lean = lean; this.stance = stance; this.width = width; this.chest = chest; this.hip = hip; this.sway = sway; this.aimK = aimK; this.aimD = aimD; this.lag = lag; this.squash = squash; }
        }
        static readonly Dictionary<string, Persona> PERSONA = new Dictionary<string, Persona>
        {
            { "raijin", new Persona(0.2f, 0.6f, 0.9f, 0.8f, 1.12f, -0.04f, 0.15f, 0.5f, 240, 21, 0.5f, 1.0f) },      // fast duellist
            { "yuzu", new Persona(0.25f, 0.45f, 0.55f, 0.35f, 1.02f, 0.08f, 0.5f, 0.4f, 200, 19, 0.6f, 0.85f) },     // archer
            { "kaien", new Persona(0.3f, 0.2f, 0.3f, 0.45f, 1.18f, 0.06f, 0.1f, 0.3f, 120, 13, 0.9f, 0.5f) },        // warding monk
            { "tomoe", new Persona(0.62f, 0.35f, 0.62f, 0.72f, 1.25f, 0.07f, 0.3f, 0.5f, 115, 12, 1.05f, 0.7f) },
            { "hibiki", new Persona(0.1f, 0.45f, 1.0f, 0.9f, 1.1f, -0.02f, 0.35f, 0.9f, 190, 17, 0.7f, 1.1f) },     // street skater
            { "mirei", new Persona(0.08f, 0.35f, 0.35f, 0.12f, 0.8f, 0.12f, 0.95f, 0.7f, 105, 11, 1.0f, 0.75f) },    // angelic medic
            { "nocturne", new Persona(0.12f, 0.25f, 0.3f, 0.06f, 0.85f, 0.14f, 1.0f, 0.6f, 115, 12, 0.9f, 0.6f) },   // diva
            { "hex", new Persona(0.3f, 0.15f, 0.2f, 0.15f, 0.92f, -0.14f, 0.3f, 0.85f, 85, 9, 1.1f, 0.4f) },         // puppeteer
            { "kagemaru", new Persona(0.15f, 0.3f, 1.0f, 1.0f, 1.22f, -0.12f, 0.0f, 0.3f, 300, 24, 0.4f, 1.1f) },    // shinobi
            { "enra", new Persona(0.75f, 0.5f, 0.8f, 0.9f, 1.35f, -0.08f, 0.0f, 0.5f, 95, 9, 1.2f, 0.65f) },         // oni brute
            { "gantetsu", new Persona(0.92f, 0.7f, 0.55f, 0.85f, 1.5f, 0.05f, 0.0f, 0.6f, 80, 9, 1.3f, 0.85f) },     // festival heavyweight
            { "haruto", new Persona(0.3f, 0.55f, 0.75f, 0.6f, 1.1f, 0.05f, 0.2f, 0.4f, 220, 20, 0.6f, 1.0f) },      // pilot on foot
        };
        static readonly Persona PERSONA_DEFAULT = new Persona(0.4f, 0.4f, 0.5f, 0.5f, 1.05f, 0, 0.2f, 0.5f, 150, 15, 0.8f, 0.7f);
        static readonly Persona PERSONA_MECH = new Persona(1, 0.3f, 0.3f, 0.5f, 1.3f, 0, 0, 0.2f, 70, 10, 1.4f, 0.15f);
        static readonly Persona PERSONA_LEGACY = new Persona(0, 0.5f, 0.714f, 0.5f, 1, 0, 0, 0.5f, 150, 15, 0, 0);
        public static Persona PersonaOf(string hero, bool mech = false) => PERSONA.TryGetValue(hero ?? "", out var p) ? p : mech ? PERSONA_MECH : PERSONA_DEFAULT;
        /// <summary>the performance layer ships with the desktop edition (FULL): the Unity edition is FULL</summary>
        public static bool PERF = true;

        // ------------------------------------------------------------------------------------------------ hammer path
        // Tenkai-Oh's rocket hammer, choreographed after a heavyweight hammer tank: swings alternate sides - wind up behind
        // the shoulder, sweep flat through the front with the weight rolling onto the lead foot, follow through past the
        // other shoulder, settle back into the guard (hammer upright in front, head by the right shoulder).
        public const float SWING_TIME = 0.96f;          // Reinhardt: 0.96s per swing
        public const float REAP_SECS = 0.75f, REAP_HIT = 0.58f, REAP_STOP = 0.05f;
        const float TIDE_TURNS = 5;
        public static float ReapPhase(float castAge) { float cp = castAge / REAP_SECS; return cp < REAP_HIT ? cp : Mathf.Max(REAP_HIT, cp - REAP_STOP / REAP_SECS); }
        /// <summary>th: yaw of the haft round the body (0 ahead, + toward the left), ph: haft elevation, d: grip distance from
        /// the shoulder centre in arm lengths, gy: grip height above the shoulders in body heights</summary>
        struct HPose { public float th, ph, d, gy; public HPose(float th, float ph, float d, float gy) { this.th = th; this.ph = ph; this.d = d; this.gy = gy; } }
        sealed class HState { public float th, ph, d, gy, imp, w, side, lean; }
        static readonly HPose GUARD = new HPose(-0.45f, 1.05f, 0.8f, -0.2f);
        static HPose Keyed((float p, HPose q)[] K, float p)
        {
            int k = 0;
            while (k < K.Length - 2 && p > K[k + 1].p) k++;
            var (p0, a) = K[k]; var (p1, b) = K[k + 1];
            float u = Smooth(Mathf.Min(1, Mathf.Max(0, (p - p0) / (p1 - p0))));
            float L(float x, float y) => x + (y - x) * u;
            return new HPose(L(a.th, b.th), L(a.ph, b.ph), L(a.d, b.d), L(a.gy, b.gy));
        }
        static HState H(HPose q, float imp, float w, float side, float lean) => new HState { th = q.th, ph = q.ph, d = q.d, gy = q.gy, imp = imp, w = w, side = side, lean = lean };
        static readonly (float, HPose)[] SHATTER =
        {
            (0, GUARD), (0.32f, new HPose(0, 1.35f, 0.35f, 0.3f)), (0.55f, new HPose(0, 1.1f, 0.45f, 0.36f)),
            (0.72f, new HPose(0, -0.95f, 0.95f, -0.12f)), (0.9f, new HPose(0, -0.9f, 0.9f, -0.14f)), (1, GUARD),
        };
        static readonly (float, HPose)[] REAPING =
        {
            (0, new HPose(-1.2f, 1.25f, 0.3f, 0.2f)), (0.3f, new HPose(-0.85f, 1.45f, 0.42f, 0.36f)), (0.5f, new HPose(-0.15f, 0.35f, 0.95f, 0.02f)),
            (0.62f, new HPose(0.75f, -0.35f, 1.0f, -0.12f)), (0.8f, new HPose(1.45f, -0.6f, 0.85f, -0.22f)), (1, new HPose(1.1f, -0.2f, 0.7f, -0.18f)),
        };
        static HState HammerPose(float p, float side, bool shield, bool casting, string mode, float cp)
        {
            if (mode == "dawncharge") return new HState { th = -2.3f, ph = -0.35f, d = 0.85f, gy = -0.25f, imp = 0, w = 1, side = 1, lean = 0.2f };   // trailing low behind the right hip
            if (mode == "shatter" && cp < 1)
            {
                // overhead wind-up, then the head is driven down into the ground in front; rear back, crunch into the slam
                var q = Keyed(SHATTER, cp);
                float lean = cp < 0.55f ? -0.2f * Mathf.Sin(Mathf.Min(1, cp / 0.55f) * Mathf.PI * 0.5f) : cp < 0.9f ? -0.2f + 0.62f * Smooth(Mathf.Min(1, (cp - 0.55f) / 0.2f)) : 0.42f * (1 - (cp - 0.9f) / 0.1f);
                return H(q, Mathf.Max(0, 1 - Mathf.Abs(cp - 0.74f) / 0.12f) * 1.4f, 1, 1, lean);
            }
            if (mode == "reaping" && cp < 1)
            {
                // Tomoe's Crescent Reaping: off her back high over the right shoulder, heaved round and down through the front on
                // a diagonal, through low past the left hip, then shouldered again
                var q = Keyed(REAPING, cp);
                float lean = cp < 0.3f ? -0.14f * Mathf.Sin(cp / 0.3f * Mathf.PI * 0.5f) : cp < 0.8f ? -0.14f + 0.5f * Smooth(Mathf.Min(1, (cp - 0.3f) / 0.32f)) : 0.36f * (1 - (cp - 0.8f) / 0.2f);
                return H(q, Mathf.Max(0, 1 - Mathf.Abs(cp - 0.56f) / 0.12f) * 1.2f, 1, -1, lean);
            }
            // Crescent Warpath: the axe out wide in the right hand at shoulder height, trailing the turn (it leaves her hand: the orbit)
            if (mode == "tide") return new HState { th = -1.85f, ph = 0.08f, d = 0.92f, gy = 0, imp = 0, w = 0, side = 1, lean = 0.1f };
            if (shield) return new HState { th = -0.75f, ph = -1.15f, d = 0.6f, gy = -0.36f, imp = 0, w = 0, side = side, lean = 0 };   // lowered while the shield is up
            if (p >= 1 || p < 0) return new HState { th = GUARD.th + (casting ? -0.25f : 0), ph = GUARD.ph, d = GUARD.d, gy = GUARD.gy, imp = 0, w = 0, side = side, lean = 0 };
            // Reinhardt's sweep: anticipation loading the start side, a fast flat strike through the front at shoulder height,
            // a long follow-through past the other shoulder (~220 deg), a held end pose, back to the guard
            var K = new (float, HPose)[]
            {
                (0, GUARD),
                (0.16f, new HPose(-side * 1.95f, 0.3f, 0.86f, 0.02f)),
                (0.33f, new HPose(-side * 0.2f, 0.02f, 1.0f, -0.02f)),
                (0.46f, new HPose(side * 1.5f, -0.04f, 0.98f, -0.03f)),
                (0.6f, new HPose(side * 2.0f, 0.08f, 0.9f, -0.05f)),
                (0.78f, new HPose(side * 1.85f, 0.22f, 0.84f, -0.07f)),
                (1, GUARD),
            };
            var r = Keyed(K, p);
            return H(r, Mathf.Max(0, 1 - Mathf.Abs(p - 0.35f) / 0.16f), Mathf.Min(1, Mathf.Min(p / 0.08f, (1 - p) / 0.22f)), side,
                -0.08f * Mathf.Max(0, 1 - Mathf.Abs(p - 0.16f) / 0.12f) + 0.1f * Mathf.Max(0, 1 - Mathf.Abs(p - 0.4f) / 0.2f));
        }

        // Enra's Hellfire Chains (TS ChainBlades.ts timelines)
        public const float CB_WIND = 0.15f, CB_ARC = 0.25f, CB_SWING = 0.62f, CB_THROW = 0.6f, CB_REACH = 5, CB_THROW_REACH = 7.5f;
        const float CB_THROW_OUT = 0.08f, CB_THROW_HIT = 0.25f, CB_THROW_BACK = 0.34f;
        static float Ez(float u) { u = Mathf.Min(1, Mathf.Max(0, u)); return u * u * (3 - 2 * u); }
        public static float SwingExt(float t)
        {
            if (t < 0) return 0;
            if (t < CB_WIND) return 0.12f * Ez(t / CB_WIND);
            if (t < CB_WIND + CB_ARC) { float u = (t - CB_WIND) / CB_ARC; return 0.12f + 0.88f * Ez(u / 0.45f); }
            if (t < CB_SWING) return 1 - Ez((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC));
            return 0;
        }
        static float SwingArc(float t) => t < CB_WIND ? 0 : t < CB_WIND + CB_ARC ? Ez((t - CB_WIND) / CB_ARC) : 1;
        static float SwingPhi(float t, float side)
        {
            float phi0 = side * 1.9f, phi1 = -side * 1.25f, a = SwingArc(t);
            float lead = t < CB_WIND + CB_ARC ? -side * 0.3f * Mathf.Sin(a * Mathf.PI) : 0;
            if (t >= CB_WIND + CB_ARC) { float u = Ez((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC)); return phi1 + (-side * 0.5f - phi1) * u; }
            return phi0 + (phi1 - phi0) * a + lead;
        }
        public static float ThrowExt(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return Ez((t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT));
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 - Ez((t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK));
            return 0;
        }
        static float ThrowSpin(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return (t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT);
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 + (t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK);
            return 0;
        }

        // ------------------------------------------------------------------------------------------------ rig
        sealed class Rest { public Quaternion q; public Vector3 p, dir; }
        static readonly string[] BONES =
        {
            "hips", "spine", "chest", "neck", "head", "shoulder_L", "upperarm_L", "forearm_L", "hand_L", "shoulder_R", "upperarm_R", "forearm_R", "hand_R",
            "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R", "wing_L", "wing_R",
        };
        static readonly Dictionary<string, string> CHILD = new Dictionary<string, string>
        {
            { "hips", "spine" }, { "spine", "chest" }, { "chest", "neck" }, { "neck", "head" }, { "shoulder_L", "upperarm_L" }, { "upperarm_L", "forearm_L" }, { "forearm_L", "hand_L" },
            { "shoulder_R", "upperarm_R" }, { "upperarm_R", "forearm_R" }, { "forearm_R", "hand_R" }, { "thigh_L", "shin_L" }, { "shin_L", "foot_L" }, { "thigh_R", "shin_R" }, { "shin_R", "foot_R" },
        };

        readonly Transform root;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<string, Rest> rest = new Dictionary<string, Rest>();
        readonly Dictionary<string, Quaternion> modelQ = new Dictionary<string, Quaternion>();
        public readonly bool ok;
        public float legLen = 1, thigh = 0.5f, shin = 0.5f, hipW = 0.1f, hipH = 1, footY = 0.05f, armLen = 0.6f, height = 1.8f;
        bool Has(string n) => bones.ContainsKey(n) && rest.ContainsKey(n);

        /// <summary>binds to the rig's rest pose as RigPose recorded it (before the Animator's first frame)</summary>
        public ProcAnimator(RigPose rig)
        {
            root = rig.root;
            foreach (var n in BONES)
                if (rig.bones.TryGetValue(n, out var t) && rig.rest.TryGetValue(n, out var r)) { bones[n] = t; rest[n] = new Rest { q = M(r.q), p = M(r.p), dir = Vector3.up }; }
            ok = new[] { "hips", "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R", "chest", "head" }.All(n => Has(n));
            if (!ok) return;
            foreach (var n in BONES)
            {
                if (!rest.TryGetValue(n, out var r)) continue;
                if (CHILD.TryGetValue(n, out var c) && rest.TryGetValue(c, out var rc)) r.dir = (rc.p - r.p).normalized;
                else if (n.StartsWith("foot")) r.dir = new Vector3(0, -0.2f, 1).normalized;
                else if (n.StartsWith("hand")) r.dir = rest.TryGetValue(n.Replace("hand", "forearm"), out var rf) ? rf.dir : Y;
                else if (n.StartsWith("wing")) r.dir = new Vector3(n.EndsWith("L") ? 1 : -1, 0.3f, -0.3f).normalized;
                else if (n == "head") r.dir = Y;
            }
            var R = rest;
            thigh = Vector3.Distance(R["thigh_L"].p, R["shin_L"].p);
            shin = Vector3.Distance(R["shin_L"].p, R["foot_L"].p);
            legLen = thigh + shin;
            hipW = Mathf.Abs(R["thigh_L"].p.x - R["thigh_R"].p.x) / 2;
            hipH = R["hips"].p.y;
            footY = (R["foot_L"].p.y + R["foot_R"].p.y) / 2;
            height = R["head"].p.y * 1.08f;
            if (Has("upperarm_L") && Has("hand_L") && Has("forearm_L")) armLen = Vector3.Distance(R["upperarm_L"].p, R["forearm_L"].p) + Vector3.Distance(R["forearm_L"].p, R["hand_L"].p);
            foot[0] = new Vector3(R["foot_L"].p.x, footY, R["foot_L"].p.z);
            foot[1] = new Vector3(R["foot_R"].p.x, footY, R["foot_R"].p.z);
        }

        /// <summary>model-space rotation (TS frame) for a bone this frame: written to the Unity bone through the mirror</summary>
        void SetModelQ(string n, Quaternion Qm)
        {
            if (!bones.TryGetValue(n, out var b)) return;
            b.rotation = root.rotation * M(Qm);
            modelQ[n] = Qm;
        }
        /// <summary>delta D (model space) applied on top of the rest orientation</summary>
        void ApplyDelta(string n, Quaternion D) { if (rest.TryGetValue(n, out var r)) SetModelQ(n, D * r.q); }
        /// <summary>rotate a bone so its rest direction points along `dir` (model space), keeping twist minimal</summary>
        Quaternion AimBone(string n, Vector3 dir)
        {
            if (!rest.TryGetValue(n, out var r)) return Quaternion.identity;
            var D = Quaternion.FromToRotation(r.dir.normalized, dir.normalized);
            ApplyDelta(n, D);
            return D;
        }
        Quaternion CurQ(string n) => modelQ.TryGetValue(n, out var q) ? q : Quaternion.identity;

        /// <summary>2-bone IK: [upperDir, lowerDir] in model space</summary>
        static (Vector3, Vector3) Ik(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole)
        {
            var d = target - root;
            float len = d.magnitude, maxL = (l1 + l2) * 0.999f;
            if (len > maxL) { d *= maxL / len; len = maxL; }
            len = Mathf.Max(len, Mathf.Abs(l1 - l2) + 1e-3f);
            var dir = d.normalized;
            float a = (l1 * l1 - l2 * l2 + len * len) / (2 * len);
            float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - a * a));
            var pd = pole - dir * Vector3.Dot(pole, dir);
            if (pd.sqrMagnitude < 1e-6f) pd = Z; pd.Normalize();
            var knee = root + dir * a + pd * h;
            var end = root + d;
            return ((knee - root).normalized, (end - knee).normalized);
        }

        // ------------------------------------------------------------------------------------------------ state
        float phase;                        // gait phase in cycles
        readonly Vector3[] foot = new Vector3[2];
        Vector2 lean, leanV;
        float bob, landDip, flap, moveBlend, airBlend, flyBlend, atk, cast;
        /// <summary>the archer's draw (0..1)</summary>
        public float drawW;
        Vector3 bowHandM;
        /// <summary>a blade guard held (0..1)</summary>
        public float guardW;
        readonly bool[] lastStance = { true, true };
        readonly Vector3?[] pplant = new Vector3?[2], cplant = new Vector3?[2], swingFrom = new Vector3?[2];
        readonly Vector3[] cerr = new Vector3[2];
        float clipW, cLegs;
        readonly float[] restepT = new float[2];
        float hipYaw, hipYawV, lastYaw, turnRoll, flinch, flinchV, lastHitAge = 9, flinchDir = 1;
        float recoil, recoilV, lastAtkAge = 9, readyW, punchExt, punchW;
        /// <summary>the held hammer's haft length (model units; HeldRig.hammerLen)</summary>
        public float hammerLen = 1;
        /// <summary>a held prop taken out of the hand (TS gunOrbit, model space TS frame): position, blade direction, up, weight</summary>
        public readonly (Vector3 p, Vector3 z, Vector3 y, float w)?[] gunOrbit = new (Vector3, Vector3, Vector3, float)?[2];
        /// <summary>Enra's chain blades: how far out on its chain each blade is, 0 (in the fist) .. 1 (full length)</summary>
        public readonly float[] chainExt = new float[2];
        float orbitW, skW, tuck;
        // ---- performance layer outputs, applied by the view: whole-body tilt about the hips, squash & stretch
        public float tiltPitch, tiltRoll, sqY = 1, sqXZ = 1;
        readonly Spring tiltP = new Spring(), tiltR = new Spring(), sq = new Spring(), lagY = new Spring(), lagP = new Spring(), headF = new Spring();
        float lastJump = 9, lastLand = 9, lastPitch, yawRate, headStab, lastCastAge = -1;
        /// <summary>1 on the frame a heavy strike lands (Crescent Reaping's cleave): the view kicks the camera</summary>
        public float impact;
        readonly Spring kick = new Spring();
        float tumble, hitX, hitZ = 1, leapW, slamDip; bool lastLeap;
        /// <summary>knocked flat: 0 standing .. 1 lying; downDir = the way the body fell (model space, TS frame)</summary>
        public float down; public Vector3 downDir = new Vector3(0, 0, -1);
        readonly Spring shift = new Spring(), wLift = new Spring(), wSweep = new Spring(0.5f);
        float aSwoop, aSup, aGlide, aFlare, aHover, aSling;          // angel state weights (smoothed)
        float wFidget, stepFlutter;
        Vector3 prevVel, acc; bool isAngel;
        Vector3 gripG, gripH = Y, gripT = X;
        /// <summary>the hammer-frame prop this frame (TS frame): pommel position, haft direction, striking side; null = no prop pose</summary>
        public (Vector3 pos, Vector3 haft, Vector3 side)? prop;

        /// <summary>a foot lands: the heavy chest / hips punctuation and a flutter through an angel's wings</summary>
        void Footfall(int i, bool heavy, Persona P)
        {
            kick.v += (heavy ? 1.6f : 2.4f) * P.weight * moveBlend;
            if (isAngel) stepFlutter += 0.035f;
        }

        /// <summary>Mirei's arms per flight state, in the body frame (null when the arm is busy or no state applies)</summary>
        (Vector3 hand, float w, Vector3? pole)? AngelArm(int i, float side, Vector3 sh, float Lr, AnimState s, float idleW, float run)
        {
            if (!PERF || cast > 0.05f || (i == 1 && (s.beam || atk > 0.05f)) || (i == 0 && atk > 0.3f)) return null;
            float t = s.time; bool lead = i == 1;
            Vector3 V(float x, float y, float z) => new Vector3(side * x, y, z) * Lr;
            var targets = new (Vector3 v, float w)[]
            {
                (V(0.78f, -0.5f + Mathf.Sin(t * 1.5f + side) * 0.06f, 0.08f), aGlide),
                (lead ? V(-0.05f, -0.3f, 0.9f) : V(0.28f, -0.88f, -0.32f), aSwoop + aSling),
                (V(0.2f, -0.96f, -0.18f), aSup),
                (V(0.62f, -0.1f, 0.55f), aFlare),
                (V(0.55f + Mathf.Sin(t * 1.1f + i) * 0.04f, -0.72f, 0.22f), aHover),
                (lead ? V(0.12f, -0.6f, 0.42f) : V(0.3f, -0.9f, 0.1f), idleW * (1 - 0.6f * run) * (s.grounded ? 1 : 0)),
            };
            float tw = 0; var hand = Vector3.zero;
            foreach (var (v, w) in targets) if (w > 1e-3f) { hand += v * w; tw += w; }
            if (tw < 0.05f) return null;
            hand = hand / tw + sh;
            return (hand, Mathf.Min(1, tw) * (lead ? 1 : 0.9f), new Vector3(side * 0.7f, -0.4f, -0.6f));
        }

        /// <summary>knocked flat (Gantetsu's Shiko slam): the limbs sprawl over whatever the body was doing</summary>
        void Sprawl(float w)
        {
            Quaternion Cur(string n) => (modelQ.TryGetValue(n, out var q) ? q : rest[n].q) * Quaternion.Inverse(rest[n].q);
            void ToQ(string n, Quaternion D, float k = 1) { if (Has(n)) ApplyDelta(n, Quaternion.Slerp(Cur(n), D, w * k)); }
            void ToDir(string n, float x, float y, float z) { if (!Has(n)) return; ToQ(n, Quaternion.FromToRotation(rest[n].dir, new Vector3(x, y, z).normalized)); }
            var I = Quaternion.identity;
            foreach (var n in new[] { "hips", "spine", "chest", "neck" }) ToQ(n, I, 0.85f);
            ToQ("head", Rot(X, -0.22f));
            foreach (var (S, side) in new[] { ("L", 1f), ("R", -1f) })
            {
                ToQ("shoulder_" + S, I, 0.85f);
                ToDir("upperarm_" + S, side, 0.3f, 0.12f);
                ToDir("forearm_" + S, side * 0.7f, 0.7f, 0.3f);
                ToDir("thigh_" + S, side * 0.24f, -1, 0.26f);
                ToDir("shin_" + S, side * 0.1f, -1, -0.22f);
                if (Has("shin_" + S) && bones.ContainsKey("foot_" + S)) ToQ("foot_" + S, Cur("shin_" + S));
            }
        }
    }
}
