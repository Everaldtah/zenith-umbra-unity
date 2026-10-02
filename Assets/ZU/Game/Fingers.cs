// Finger curls (port of Fingers.ts). The Tripo rigs carry Mixamo's fingers (thumb / index / middle / ring / pinky, joints
// 1..3 per hand), skinned but never driven by the gaits, so every hand would hang open and flat: a katana held in straight
// fingers, a trigger never pulled. Each hero's hands close into the grip their weapon needs, the way Overwatch 2 poses
// hands: a fist round a hilt or a bow grip, the index along the trigger (squeezed on every shot), the string hand's
// three-finger hook, a caster's open palm, a card pinched between two fingers. Grips blend per joint in about a tenth
// of a second. Curls are rotations about each joint's hinge in its rest frame (the axis across the knuckles, turning the
// finger toward the palm), so they ride on whatever the arm is doing: clips, IK, or the viewmodel. Runs after the Animator.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public enum FingerGrip { Relaxed, Fist, Trigger, Hook, Open, Pinch, Claw, Hammer }

    public class Fingers
    {
        static readonly string[] FINGERS = { "thumb", "index", "middle", "ring", "pinky" };
        /// <summary>radians per joint (1..3), per finger, thumb first</summary>
        static readonly Dictionary<FingerGrip, float[][]> CURL = new Dictionary<FingerGrip, float[][]>
        {
            { FingerGrip.Relaxed, new[] { new[] { 0.12f, 0.2f, 0.12f }, new[] { 0.22f, 0.38f, 0.22f }, new[] { 0.28f, 0.44f, 0.26f }, new[] { 0.34f, 0.5f, 0.3f }, new[] { 0.4f, 0.55f, 0.32f } } },
            { FingerGrip.Fist, new[] { new[] { 0.5f, 0.55f, 0.45f }, new[] { 1.3f, 1.5f, 0.95f }, new[] { 1.35f, 1.5f, 0.95f }, new[] { 1.35f, 1.5f, 0.95f }, new[] { 1.35f, 1.45f, 0.95f } } },
            { FingerGrip.Trigger, new[] { new[] { 0.45f, 0.5f, 0.35f }, new[] { 0.35f, 0.55f, 0.3f }, new[] { 1.3f, 1.5f, 0.95f }, new[] { 1.35f, 1.5f, 0.95f }, new[] { 1.35f, 1.45f, 0.95f } } },
            { FingerGrip.Hook, new[] { new[] { 0.3f, 0.35f, 0.25f }, new[] { 0.35f, 1.15f, 0.75f }, new[] { 0.35f, 1.2f, 0.75f }, new[] { 0.4f, 1.2f, 0.75f }, new[] { 1.15f, 1.35f, 0.9f } } },
            { FingerGrip.Open, new[] { new[] { 0.04f, 0.05f, 0.04f }, new[] { 0.04f, 0.08f, 0.05f }, new[] { 0.04f, 0.08f, 0.05f }, new[] { 0.06f, 0.1f, 0.06f }, new[] { 0.08f, 0.1f, 0.06f } } },
            { FingerGrip.Pinch, new[] { new[] { 0.45f, 0.5f, 0.35f }, new[] { 0.55f, 0.55f, 0.3f }, new[] { 0.6f, 0.6f, 0.35f }, new[] { 1.25f, 1.45f, 0.9f }, new[] { 1.3f, 1.45f, 0.9f } } },
            { FingerGrip.Claw, new[] { new[] { 0.3f, 0.4f, 0.3f }, new[] { 0.55f, 0.85f, 0.6f }, new[] { 0.6f, 0.9f, 0.6f }, new[] { 0.6f, 0.9f, 0.6f }, new[] { 0.65f, 0.9f, 0.6f } } },
            // a two-handed wrap around a thick haft (Reinhardt's hammer): fingers round a ~5 cm handle, the thumb over them
            { FingerGrip.Hammer, new[] { new[] { 0.75f, 0.85f, 0.6f }, new[] { 1.15f, 1.35f, 0.95f }, new[] { 1.2f, 1.4f, 0.95f }, new[] { 1.2f, 1.4f, 0.95f }, new[] { 1.25f, 1.4f, 0.95f } } },
        };
        /// <summary>how far a grip closes the fingers' rest spread (a fist packs them together, an open palm fans them a little)</summary>
        static readonly Dictionary<FingerGrip, float> CLOSE = new Dictionary<FingerGrip, float>
        { { FingerGrip.Relaxed, 0.35f }, { FingerGrip.Fist, 0.9f }, { FingerGrip.Trigger, 0.85f }, { FingerGrip.Hook, 0.7f }, { FingerGrip.Open, -0.15f }, { FingerGrip.Pinch, 0.6f }, { FingerGrip.Claw, 0.2f }, { FingerGrip.Hammer, 0.95f } };

        class Joint { public Transform o; public Quaternion rest; public Vector3 curl; public Vector3? splay; public float spread; public int f, k; }
        class Hand { public List<Joint> joints = new List<Joint>(); public float[] cur = new float[15], tgt = new float[15]; public float close, closeTgt; }
        readonly Hand[] hands = new Hand[2];
        bool first = true;

        /// <summary>null when the model has no finger bones (mechs)</summary>
        public static Fingers Build(Transform root)
        {
            var f = new Fingers(root);
            return f.hands[0] != null || f.hands[1] != null ? f : null;
        }

        Fingers(Transform root)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) { var n = t.name.Replace('.', '_'); if (!byName.ContainsKey(n)) byName[n] = t; }
            Vector3 P(Transform o) => root.InverseTransformPoint(o.position);
            Quaternion Q(Transform o) => Quaternion.Inverse(root.rotation) * o.rotation;
            for (int side = 0; side < 2; side++)
            {
                string S = side == 0 ? "L" : "R";
                var chain = new List<Transform>[5];
                for (int f = 0; f < 5; f++) { chain[f] = new List<Transform>(); for (int k = 1; k <= 3; k++) if (byName.TryGetValue($"{FINGERS[f]}{k}_{S}", out var o)) chain[f].Add(o); }
                var idx = chain[1]; var mid = chain[2]; var pky = chain[4].Count > 0 ? chain[4] : chain[3];
                if (idx.Count < 2 || mid.Count < 2 || pky.Count == 0) continue;
                // the hand frame at rest: fingers' direction, the knuckle line (index -> pinky), the palm normal. Mirrored into
                // Unity's space the right hand's palm is -(along x across) (the TS's +, in its right-handed frame)
                var across = (P(pky[0]) - P(idx[0])).normalized;
                var along = (P(mid[mid.Count - 1]) - P(mid[0])).normalized;
                var palm = (Vector3.Cross(along, across) * (S == "R" ? -1 : 1)).normalized;
                var midDir = (P(mid[1]) - P(mid[0])).normalized;
                var hand = new Hand();
                for (int f = 0; f < 5; f++)
                    for (int k = 0; k < chain[f].Count; k++)
                    {
                        var o = chain[f][k]; var here = P(o);
                        var dir = k + 1 < chain[f].Count ? (P(chain[f][k + 1]) - here) : (here - (k > 0 ? P(chain[f][k - 1]) : here));
                        if (dir.sqrMagnitude < 1e-10f) continue;
                        dir.Normalize();
                        // the thumb closes across the palm toward the little finger, the others straight into it
                        var to = f == 0 ? (palm + across * 0.7f).normalized : palm;
                        var axis = Vector3.Cross(dir, to);
                        if (axis.sqrMagnitude < 1e-6f) continue;
                        var toLocal = Quaternion.Inverse(Q(o));
                        Vector3? splay = null; float spread = 0;
                        if (k == 0 && f != 0 && f != 2)
                        {
                            // the finger's rest spread from the middle finger, about the palm normal
                            spread = Mathf.Atan2(Vector3.Dot(palm, Vector3.Cross(midDir, dir)), Vector3.Dot(midDir, dir));
                            splay = toLocal * palm;
                        }
                        hand.joints.Add(new Joint { o = o, rest = o.localRotation, curl = toLocal * axis.normalized, splay = splay, spread = spread, f = f, k = k });
                    }
                if (hand.joints.Count > 0) hands[side] = hand;
            }
        }

        /// <summary>side 0 = left, 1 = right; `squeeze` curls the index further in (a trigger pull)</summary>
        public void Set(int side, FingerGrip grip, float squeeze = 0)
        {
            var h = hands[side]; if (h == null) return;
            var c = CURL[grip];
            for (int f = 0; f < 5; f++) for (int k = 0; k < 3; k++) h.tgt[f * 3 + k] = c[f][k];
            if (squeeze > 0) { h.tgt[3] += 0.45f * squeeze; h.tgt[4] += 0.35f * squeeze; h.tgt[5] += 0.2f * squeeze; }
            h.closeTgt = CLOSE[grip];
        }

        /// <summary>blend toward the targets (rate: 1/s; ~20 = a snap into a grip) and pose the joints</summary>
        public void Update(float dt, float rate = 20)
        {
            float u = first ? 1 : Mathf.Min(1, dt * rate);
            first = false;
            foreach (var h in hands)
            {
                if (h == null) continue;
                for (int i = 0; i < 15; i++) h.cur[i] += (h.tgt[i] - h.cur[i]) * u;
                h.close += (h.closeTgt - h.close) * u;
                foreach (var j in h.joints)
                {
                    var q = j.rest;
                    if (j.splay.HasValue) q = q * Quaternion.AngleAxis(-j.spread * h.close * Mathf.Rad2Deg, j.splay.Value);
                    j.o.localRotation = q * Quaternion.AngleAxis(h.cur[j.f * 3 + j.k] * Mathf.Rad2Deg, j.curl);
                }
            }
        }

        /// <summary>each hero's hands holding their weapon, [left, right]</summary>
        static readonly Dictionary<string, (FingerGrip, FingerGrip)> BASE = new Dictionary<string, (FingerGrip, FingerGrip)>
        {
            { "raijin", (FingerGrip.Fist, FingerGrip.Fist) }, { "hayate", (FingerGrip.Relaxed, FingerGrip.Fist) }, { "yuzu", (FingerGrip.Fist, FingerGrip.Hook) }, { "seiran", (FingerGrip.Fist, FingerGrip.Hook) },
            { "kaien", (FingerGrip.Open, FingerGrip.Pinch) }, { "mirei", (FingerGrip.Relaxed, FingerGrip.Open) }, { "nocturne", (FingerGrip.Claw, FingerGrip.Claw) }, { "hex", (FingerGrip.Claw, FingerGrip.Claw) },
            { "kagemaru", (FingerGrip.Pinch, FingerGrip.Fist) }, { "enra", (FingerGrip.Fist, FingerGrip.Fist) }, { "haruto", (FingerGrip.Fist, FingerGrip.Trigger) }, { "tenkai", (FingerGrip.Hammer, FingerGrip.Hammer) },
            { "gorgoth", (FingerGrip.Fist, FingerGrip.Trigger) }, { "gantetsu", (FingerGrip.Trigger, FingerGrip.Trigger) }, { "hibiki", (FingerGrip.Relaxed, FingerGrip.Trigger) }, { "tomoe", (FingerGrip.Fist, FingerGrip.Trigger) },
        };
        public static (FingerGrip L, FingerGrip R) BaseGrips(string id) => BASE.TryGetValue(id, out var b) ? b : (FingerGrip.Relaxed, FingerGrip.Relaxed);
        static readonly HashSet<string> CASTERS = new HashSet<string> { "kaien", "mirei", "nocturne", "hex" };

        /// <summary>the grips for this moment: the weapon grip, overridden by what the hands are doing - open palms for casts
        /// and beams, a clawed hand on a wall, the archer's string hand flicking open on the loose and pinching the next arrow
        /// out of the quiver, a trigger squeeze on each shot, a loose hand in death. fp: the viewmodel</summary>
        public static (FingerGrip L, FingerGrip R, float sqL, float sqR, float rate) GripsFor(Actor a, double t, bool fp, float drawW = 1)
        {
            string id = a.def.id;
            var (L, R) = BASE.TryGetValue(id, out var b) ? b : (FingerGrip.Relaxed, FingerGrip.Relaxed);
            float sqL = 0, sqR = 0, rate = 20;
            if (!a.alive) return (FingerGrip.Relaxed, FingerGrip.Relaxed, 0, 0, 4);
            if (a.Has("knockdown", t)) return (FingerGrip.Relaxed, FingerGrip.Relaxed, 0, 0, 12);    // knocked flat: hands fall open
            var an = a.anim; double atk = t - an.attackAt, cast = t - an.castAt;
            if (R == FingerGrip.Trigger) sqR = Mathf.Max(0, 1 - (float)(t - an.fireR) / 0.09f);
            if (L == FingerGrip.Trigger) sqL = Mathf.Max(0, 1 - (float)(t - (id == "gantetsu" ? an.fireL : an.fireR)) / 0.09f);
            // Hayate: the koi shuriken pinched while the nodachi is sheathed
            if (id == "hayate" && !Held.Visible(id, 1, a, t)) R = FingerGrip.Pinch;
            // archers: hook on the string; the loose flicks the fingers open, the reach to the quiver pinches the next arrow
            if (id == "yuzu" || id == "seiran")
            {
                bool shot = an.attackKind == "primary" || an.attackKind == "secondary";
                if (shot && atk < Held.ARROW_GONE.x + 0.12) R = FingerGrip.Open;
                else if (shot && atk < Held.ARROW_GONE.y - 0.12) R = FingerGrip.Relaxed;
                else if (shot && atk < Held.ARROW_GONE.y + 0.05) R = FingerGrip.Pinch;
                else if (!fp && drawW < 0.3f && !a.charging) R = FingerGrip.Relaxed;
            }
            // Mirei calling the fallen back: both palms open to the souls
            if (id == "mirei" && a.sv.TryGetValue("rebirthAt", out var rb) && t - rb < 2.2) { L = FingerGrip.Open; R = FingerGrip.Open; }
            // Tomoe's Fang out of her hand: the throwing hand stays open until the catch
            if (id == "tomoe" && a.Sv("fang", 0) > 0) L = FingerGrip.Open;
            // casts: casters open their palms (both), everyone's free hand opens for the gesture
            if (!string.IsNullOrEmpty(an.castId) && cast < 0.55)
            {
                if (CASTERS.Contains(id)) { L = FingerGrip.Open; R = id == "kaien" ? FingerGrip.Pinch : FingerGrip.Open; }
                else if (L == FingerGrip.Relaxed) L = FingerGrip.Open;
            }
            if (a.beamOn || a.flameOn) { L = FingerGrip.Open; if (CASTERS.Contains(id) || id == "enra") R = FingerGrip.Open; }
            // a reload: the off hand grabs the magazine
            if (a.reloadUntil > t && L != FingerGrip.Trigger) L = FingerGrip.Claw;
            // quick melee jab with the off hand
            if (an.attackKind == "punch" && atk < 0.45) L = FingerGrip.Fist;
            // hands on the wall
            if (a.Has("wallclimb", t)) { L = FingerGrip.Claw; if (R == FingerGrip.Relaxed || R == FingerGrip.Open) R = FingerGrip.Claw; }
            return (L, R, sqL, sqR, rate);
        }

        /// <summary>pose this rig's fingers for the actor this frame</summary>
        public void Drive(Actor a, double t, float dt, bool fp)
        {
            var g = GripsFor(a, t, fp);
            Set(0, g.L, g.sqL); Set(1, g.R, g.sqR);
            Update(dt, g.rate);
        }
    }
}
