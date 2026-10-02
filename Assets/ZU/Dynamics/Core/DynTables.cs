// The tuning tables, ported one for one from zenith-umbra/src/render/Animator.ts (DYN, DYN_CLASS, HERO_CLASS, HERO_DYN,
// DYN_VMAX, kindOf, COLL, SKIRT_RING) and docs/research/ow_dynamics.md. Managed code: used at bind time only.
//
// stiff / drag: [root, tip] per 1/60 s along the chain (roots hold the silhouette, tips carry the motion). inertT / inertR:
// how much of the character's own world move / turn the chain feels (1 = fully world space; Kawaii Physics' world damping).
// simW: sim vs the animated pose, a final blend. maxA: angular limit off the animated pose (radians). grav: g multiplier.
using System.Collections.Generic;

namespace ZU.Dynamics
{
    public static class DynTables
    {
        /// <summary>Character-relative particle speed limit (m/s): dashes stay readable and strands can't tunnel through the body.</summary>
        public const float DYN_VMAX = 9f;

        /// <summary>The dynamic chains a rig can carry (skirt panels in ring order: front, left, back, right).</summary>
        public static readonly string[] CHAIN_PREFIXES = { "hair_B", "hair_L", "hair_R", "skirt_F", "skirt_L", "skirt_B", "skirt_R", "cape_B", "hair_T", "sleeve_L", "sleeve_R" };

        /// <summary>Neighbouring skirt panels (the ring) - each pair keeps its rest spacing level by level.</summary>
        public static readonly (string a, string b)[] SKIRT_RING = { ("skirt_F", "skirt_L"), ("skirt_L", "skirt_B"), ("skirt_B", "skirt_R"), ("skirt_R", "skirt_F") };
        /// <summary>Opposite panels: kept apart so a panel can't fold through the ring onto the far side (no panel crossing).</summary>
        public static readonly (string a, string b)[] SKIRT_DIAG = { ("skirt_F", "skirt_B"), ("skirt_L", "skirt_R") };

        /// <summary>Lateral band limits: neighbours within [LAT_LO, LAT_HI] x rest (TS used 0.75..1.25), diagonals never closer than DIAG_LO x rest.</summary>
        public const float LAT_LO = 0.85f, LAT_HI = 1.15f, DIAG_LO = 0.6f, DIAG_HI = 1.6f;

        /// <summary>Capsule colliders: bone head -> `to` bone head; radius as a fraction of the model height when the rig has no measurement.</summary>
        public static readonly (BodyCol col, string bone, string to, float r)[] COLL =
        {
            (BodyCol.Hips, "hips", "spine", 0.09f), (BodyCol.Spine, "spine", "chest", 0.085f), (BodyCol.Chest, "chest", "neck", 0.09f),
            (BodyCol.Neck, "neck", "head", 0.035f), (BodyCol.Head, "head", "head", 0.062f),
            (BodyCol.UpperarmL, "upperarm_L", "forearm_L", 0.035f), (BodyCol.UpperarmR, "upperarm_R", "forearm_R", 0.035f),
            (BodyCol.ForearmL, "forearm_L", "hand_L", 0.03f), (BodyCol.ForearmR, "forearm_R", "hand_R", 0.03f),
            (BodyCol.ThighL, "thigh_L", "shin_L", 0.052f), (BodyCol.ThighR, "thigh_R", "shin_R", 0.052f),
            (BodyCol.ShinL, "shin_L", "foot_L", 0.04f), (BodyCol.ShinR, "shin_R", "foot_R", 0.04f),
        };

        public static readonly Dictionary<string, BodyCol> COL_OF_BONE = new Dictionary<string, BodyCol>
        {
            { "hips", BodyCol.Hips }, { "spine", BodyCol.Spine }, { "chest", BodyCol.Chest }, { "neck", BodyCol.Neck }, { "head", BodyCol.Head },
            { "upperarm_L", BodyCol.UpperarmL }, { "upperarm_R", BodyCol.UpperarmR }, { "forearm_L", BodyCol.ForearmL }, { "forearm_R", BodyCol.ForearmR },
            { "thigh_L", BodyCol.ThighL }, { "thigh_R", BodyCol.ThighR }, { "shin_L", BodyCol.ShinL }, { "shin_R", BodyCol.ShinR },
        };

        /// <summary>A kind's full tuning (the TS Dyn interface) before the class / hero multipliers.</summary>
        public sealed class Dyn
        {
            public float[] stiff, drag; public float grav, maxA, wind, inertT, inertR, simW; public string[] cols;
            public Dyn Clone() => new Dyn { stiff = (float[])stiff.Clone(), drag = (float[])drag.Clone(), grav = grav, maxA = maxA, wind = wind, inertT = inertT, inertR = inertR, simW = simW, cols = cols };
        }
        /// <summary>A per-hero partial override (null = keep the kind's value).</summary>
        public sealed class DynOverride { public float[] stiff, drag; public float? grav, maxA, wind, inertT, inertR, simW; }

        public static readonly Dictionary<DynKind, Dyn> DYN = new Dictionary<DynKind, Dyn>
        {
            { DynKind.Hair, new Dyn { stiff = new[] { 0.26f, 0.08f }, drag = new[] { 0.08f, 0.11f }, grav = 0.6f, maxA = 0.75f, wind = 0.35f, inertT = 0.55f, inertR = 0.45f, simW = 1f,
                cols = new[] { "head", "neck", "chest", "spine", "upperarm_L", "upperarm_R" } } },
            // hair standing up off the head (topknot, buns, dreadlocks): holds its shape against gravity, bounces with the head
            { DynKind.Tuft, new Dyn { stiff = new[] { 0.5f, 0.3f }, drag = new[] { 0.12f, 0.14f }, grav = 0.12f, maxA = 0.26f, wind = 0.15f, inertT = 0.8f, inertR = 0.7f, simW = 1f,
                cols = new[] { "head" } } },
            // wide sleeves: hang and swing off the forearm, can't pass through the torso, the thighs or the arm itself
            { DynKind.Sleeve, new Dyn { stiff = new[] { 0.3f, 0.09f }, drag = new[] { 0.08f, 0.1f }, grav = 0.95f, maxA = 0.8f, wind = 0.4f, inertT = 0.6f, inertR = 0.5f, simW = 0.85f,
                cols = new[] { "spine", "chest", "hips", "thigh_L", "thigh_R", "forearm_L", "forearm_R" } } },
            { DynKind.Skirt, new Dyn { stiff = new[] { 0.34f, 0.12f }, drag = new[] { 0.08f, 0.1f }, grav = 0.9f, maxA = 0.55f, wind = 0.3f, inertT = 0.5f, inertR = 0.4f, simW = 0.7f,
                cols = new[] { "hips", "spine", "thigh_L", "thigh_R", "shin_L", "shin_R" } } },
            { DynKind.Cape, new Dyn { stiff = new[] { 0.24f, 0.07f }, drag = new[] { 0.06f, 0.09f }, grav = 1f, maxA = 0.8f, wind = 0.6f, inertT = 0.4f, inertR = 0.35f, simW = 0.75f,
                cols = new[] { "spine", "chest", "hips", "thigh_L", "thigh_R", "upperarm_L", "upperarm_R" } } },
        };

        public struct ClassMul { public float stiff, drag, maxA, inertT; }
        /// <summary>Per weight class: heavy cloth swings slower and less; silk gowns and flyers' hair stay livelier.</summary>
        public static readonly Dictionary<string, ClassMul> DYN_CLASS = new Dictionary<string, ClassMul>
        {
            { "heavy", new ClassMul { stiff = 1.25f, drag = 1.2f, maxA = 0.85f, inertT = 0.9f } },
            { "light", new ClassMul { stiff = 0.85f, drag = 0.9f, maxA = 1.1f, inertT = 1.05f } },
        };
        public static readonly Dictionary<string, string> HERO_CLASS = new Dictionary<string, string>
        {
            { "gantetsu", "heavy" }, { "tomoe", "heavy" }, { "enra", "heavy" }, { "gorgoth", "heavy" }, { "vorn", "heavy" }, { "qelvaris", "heavy" },
            { "mirei", "light" }, { "nocturne", "light" }, { "yuzu", "light" }, { "susanoo", "heavy" }, { "enra_effigy", "heavy" },
        };
        public static readonly Dictionary<string, Dictionary<DynKind, DynOverride>> HERO_DYN = new Dictionary<string, Dictionary<DynKind, DynOverride>>
        {
            // dreads at groove speed
            { "hibiki", new Dictionary<DynKind, DynOverride> { { DynKind.Tuft, new DynOverride { inertT = 0.5f, inertR = 0.5f } } } },
            { "hibiki_armor", new Dictionary<DynKind, DynOverride> { { DynKind.Tuft, new DynOverride { inertT = 0.5f, inertR = 0.5f } } } },
            // the ragged gown hem reads better with less sim
            { "nocturne", new Dictionary<DynKind, DynOverride> { { DynKind.Skirt, new DynOverride { simW = 0.6f } } } },
            // wide sleeves: don't wall off the view in third person
            { "kaien", new Dictionary<DynKind, DynOverride> { { DynKind.Sleeve, new DynOverride { maxA = 0.65f } } } },
            { "seiran", new Dictionary<DynKind, DynOverride> { { DynKind.Sleeve, new DynOverride { maxA = 0.65f } } } },
            // Raijin's Susanoo (5.4 m): the robe hangs from the chest as the cape chain - slow and damped at that scale, held
            // near its hanging line, so it never swings up past the shoulders when the giant raises its arms
            { "susanoo", new Dictionary<DynKind, DynOverride> {
                { DynKind.Cape, new DynOverride { drag = new[] { 0.12f, 0.15f }, maxA = 0.55f, inertT = 0.3f, inertR = 0.3f, simW = 0.7f } },
                { DynKind.Skirt, new DynOverride { drag = new[] { 0.11f, 0.13f }, maxA = 0.45f, inertT = 0.35f } } } },
        };

        /// <summary>TS kindOf: the kind a chain prefix is solved as.</summary>
        public static DynKind KindOf(string prefix)
        {
            if (prefix == "hair_T") return DynKind.Tuft;
            if (prefix.StartsWith("hair")) return DynKind.Hair;
            if (prefix.StartsWith("cape")) return DynKind.Cape;
            if (prefix.StartsWith("sleeve")) return DynKind.Sleeve;
            return DynKind.Skirt;
        }

        /// <summary>The collider mask a kind uses (the TS `cols` list).</summary>
        public static int ColMask(DynKind kind)
        {
            int m = 0;
            foreach (var n in DYN[kind].cols) if (COL_OF_BONE.TryGetValue(n, out var c)) m |= 1 << (int)c;
            return m;
        }

        /// <summary>
        /// TS dynFor: DYN[kind] with the hero's class multipliers and per-hero overrides. Front locks (hair_L / hair_R) hang
        /// beside the face and get a tighter cone (0.45 rad) unless the hero overrides maxA. Deviation from the TS: HERO_DYN
        /// stiff / drag overrides are honoured here (the TS spread them in and then overwrote them with the base table, so
        /// Susanoo's drag override never took effect there).
        /// </summary>
        public static DynParams Resolve(string heroId, DynKind kind, string prefix)
        {
            var b = DYN[kind];
            ClassMul m = new ClassMul { stiff = 1, drag = 1, maxA = 1, inertT = 1 };
            if (heroId != null && HERO_CLASS.TryGetValue(heroId, out var cls) && DYN_CLASS.TryGetValue(cls, out var cm)) m = cm;
            DynOverride o = null;
            if (heroId != null && HERO_DYN.TryGetValue(heroId, out var per)) per.TryGetValue(kind, out o);
            float[] stiff = o?.stiff ?? b.stiff, drag = o?.drag ?? b.drag;
            float maxA = o?.maxA ?? (kind == DynKind.Hair && (prefix == "hair_L" || prefix == "hair_R") ? 0.45f : b.maxA);
            return new DynParams
            {
                stiffRoot = System.Math.Min(0.95f, stiff[0] * m.stiff), stiffTip = System.Math.Min(0.95f, stiff[1] * m.stiff),
                dragRoot = System.Math.Min(0.9f, drag[0] * m.drag), dragTip = System.Math.Min(0.9f, drag[1] * m.drag),
                grav = o?.grav ?? b.grav,
                maxA = maxA * m.maxA,
                wind = o?.wind ?? b.wind,
                inertT = System.Math.Min(1f, (o?.inertT ?? b.inertT) * m.inertT),
                inertR = o?.inertR ?? b.inertR,
                simW = o?.simW ?? b.simW,
                colMask = ColMask(kind),
            };
        }

        /// <summary>TS root->tip curve: t^0.8 with t = k / (n - 1) (a single-particle chain uses the tip value).</summary>
        public static float CurveT(int k, int n) => n > 1 ? (float)System.Math.Pow((double)k / (n - 1), 0.8) : 1f;
    }
}
