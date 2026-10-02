// Per-character clip layer (port of ClipLayer.ts): turns gameplay state into a blended clip pose for the procedural
// animator (ProcAnimator, the TS Animator.ts).
//  - base: idle <-> 8-way locomotion blend space (gait by speed, direction by velocity), phase-synced across clips and
//    advanced by DISTANCE travelled (playback rate = body speed / clip speed), so planted feet don't slide
//  - airborne: jump loop; one-shots: jump start, land, rolls / dashes / flips / vaults (full body), melee combos (split
//    hits, advanced per swing), punches, casts, throws, hit reactions (upper body), deaths (full body, held)
// The procedural layer stays on top (aim pitch, recoil, flinch, hammer, IK feet, springs) and falls back to itself wherever
// the library has no clip.
//
// Unity: a PlayableGraph per hero on its Animator (no AnimatorController), evaluated by hand inside Update - the clip times
// and weights are this file's (the TS samplePose / accumulate), Mecanim does the humanoid blend. The base is one mixer of
// every clip in use (a mirrored clip runs through ClipMirror); the running one-shot feeds two masked layers (upper body /
// legs) at their own weights. The pose lands on the bones; ProcAnimator reads it back and hands it to Inertialize, which
// adds the TS inertialized cuts. ProcAnimator also gets the TS LayerOut weights from Out.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Slot = ZU.Game.Anim.ClipLibrary.Slot;
using Clip = ZU.Game.Anim.ClipLibrary.Clip;

namespace ZU.Game.Anim
{
    public sealed class ClipLayer : IDisposable
    {
        /// <summary>ProcAnimator sets this once its SampleClip reads the layer (until then HeroView keeps the Mecanim controller)</summary>
        public static bool Use;

        /// <summary>ability -> the clip slots that sell it (first one the library has wins); everything else casts</summary>
        public static readonly Dictionary<string, Slot[]> CAST_SLOT = new Dictionary<string, Slot[]>
        {
            ["pilotroll"] = new[] { Slot.roll, Slot.dash }, ["sunhop"] = new[] { Slot.flip, Slot.vault, Slot.jump_start }, ["flashstep"] = new[] { Slot.dash, Slot.slide }, ["shadowstep"] = new[] { Slot.dash, Slot.flip },
            ["spiritstep"] = new[] { Slot.flip, Slot.dash }, ["thousandcuts"] = new[] { Slot.dash, Slot.melee }, ["chain"] = new[] { Slot.@throw, Slot.cast }, ["marionette"] = new[] { Slot.cast, Slot.@throw },
            ["reveal"] = new[] { Slot.shoot, Slot.cast }, ["hundredsuns"] = new[] { Slot.cast }, ["parry"] = new[] { Slot.block, Slot.cast }, ["veil"] = new[] { Slot.cast }, ["judgment"] = new[] { Slot.cast }, ["asura"] = new[] { Slot.cast },
            ["crescent"] = new[] { Slot.@throw, Slot.cast }, ["recall"] = new[] { Slot.cast }, ["warcall"] = new[] { Slot.cast },
        };
        // upper-body moves that also own the legs while standing still (the stance of a swing, cast or throw)
        static readonly HashSet<Slot> STANCE = new HashSet<Slot> { Slot.melee, Slot.cast, Slot.@throw, Slot.punch, Slot.block };
        static readonly HashSet<Slot> FULL = new HashSet<Slot> { Slot.roll, Slot.dash, Slot.slide, Slot.vault, Slot.flip, Slot.jump_start, Slot.land, Slot.death, Slot.stun };
        // seconds each one-shot should take in game (its clip is time-scaled into this, within limits)
        static readonly Dictionary<Slot, float> TARGET = new Dictionary<Slot, float>
        {
            [Slot.punch] = 0.42f, [Slot.hit] = 0.4f, [Slot.jump_start] = 0.3f, [Slot.land] = 0.35f, [Slot.roll] = 0.55f, [Slot.dash] = 0.35f, [Slot.slide] = 0.45f,
            [Slot.flip] = 0.7f, [Slot.vault] = 0.6f, [Slot.cast] = 0.6f, [Slot.@throw] = 0.55f, [Slot.shoot] = 0.35f, [Slot.block] = 1.2f,
        };

        /// <summary>what the procedural animator passes (TS ClipInput): speed in leg lengths / s, angle in the canonical frame
        /// (0 = forward, +pi/2 = the character's left), its own smoothed move / air blends, and whether clips may drive</summary>
        public struct Input { public float speed, angle, moveBlend, airBlend; public bool eligible; }
        /// <summary>TS LayerOut, without the pose (the pose is on the bones)</summary>
        public sealed class Out
        {
            public bool ok;
            public float legs, torso, armsLoco, armsAction, loco;
            public readonly float[] contact = new float[2];
            public string action = "", clipName = "";
        }

        sealed class Action { public Clip clip; public Slot slot; public float t, rate, w; public bool full, hold; }

        /// <summary>inertialized transitions: the half-life (s) the pose jump of a cut decays with (critically damped, from rest)</summary>
        public const float INERT_HALFLIFE = 0.1f;
        static readonly float INERT_Y = 2 * Mathf.Log(2) / INERT_HALFLIFE;
        /// <summary>the share of a pose jump still left t seconds after the cut: x(t) = (1 + y t) e^(-y t) (a critically damped
        /// spring released from rest - Bollo's inertialization, GDC 2018, in D. Holden's spring form)</summary>
        public static float InertDecay(float t) => (1 + INERT_Y * t) * Mathf.Exp(-INERT_Y * t);

        public readonly ClipLibrary lib;
        readonly int seed; readonly string hero;
        public float phase, idleT;
        Action action;
        public int combo;
        float lastMelee = -9;
        float pAttack = 9, pCast = 9, pHit = 9, pJump = 9, pLand = 9, pReload;
        Clip death;
        readonly Out o = new Out();
        public Out Result => o;
        // inertialization: the pose handed out last frame, and the jump a cut opened
        readonly Dictionary<string, Quaternion> lastQ = new Dictionary<string, Quaternion>(), offQ = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> lastP = new Dictionary<string, Vector3>(), offP = new Dictionary<string, Vector3>();
        bool lastOk, cut; float offT = 9;

        // ---- the graph
        PlayableGraph graph;
        AnimationLayerMixerPlayable layers;
        AnimationMixerPlayable baseMix;
        sealed class Src { public Clip c; public AnimationClipPlayable cp; public Playable top; public bool mirrored; public ClipMirrorJob job; public int input = -1; }
        readonly Dictionary<Clip, Src> baseSrc = new Dictionary<Clip, Src>();
        readonly List<Src> baseList = new List<Src>();
        Src actUp, actLo;
        static AvatarMask upperMask, lowerMask;

        public ClipLayer(Animator anim, ClipLibrary lib, int seed = 0, string hero = "")
        {
            this.lib = lib; this.seed = seed; this.hero = hero ?? "";
            graph = PlayableGraph.Create("zu clips " + hero);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "clips", anim);
            layers = AnimationLayerMixerPlayable.Create(graph, 3);
            baseMix = AnimationMixerPlayable.Create(graph, 0);
            graph.Connect(baseMix, 0, layers, 0);
            layers.SetInputWeight(0, 1);
            Masks();
            layers.SetLayerMaskFromAvatarMask(1, upperMask);
            layers.SetLayerMaskFromAvatarMask(2, lowerMask);
            output.SetSourcePlayable(layers);
        }

        /// <summary>the TS UPPER bones (spine .. head, shoulders, arms, hands) and the rest (hips, legs, feet)</summary>
        static void Masks()
        {
            if (upperMask != null) return;
            upperMask = new AvatarMask(); lowerMask = new AvatarMask();
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++) { upperMask.SetHumanoidBodyPartActive(p, false); lowerMask.SetHumanoidBodyPartActive(p, false); }
            foreach (var p in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                upperMask.SetHumanoidBodyPartActive(p, true);
            foreach (var p in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg, AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                lowerMask.SetHumanoidBodyPartActive(p, true);
        }

        Src MakeSrc(Clip c)
        {
            var s = new Src { c = c, cp = AnimationClipPlayable.Create(graph, c.clip) };
            s.cp.SetApplyFootIK(false); s.cp.SetApplyPlayableIK(false);
            if (c.mirror) { s.mirrored = true; s.top = ClipMirror.Create(graph, s.cp, out s.job); }
            else s.top = s.cp;
            return s;
        }
        void FreeSrc(Src s)
        {
            if (s == null) return;
            if (s.top.IsValid() && s.mirrored) s.top.Destroy();
            if (s.cp.IsValid()) s.cp.Destroy();
            if (s.mirrored) ClipMirror.Dispose(s.job);
        }
        Src BaseSrc(Clip c)
        {
            if (baseSrc.TryGetValue(c, out var s)) return s;
            s = MakeSrc(c);
            s.input = baseMix.GetInputCount();
            baseMix.SetInputCount(s.input + 1);
            graph.Connect(s.top, 0, baseMix, s.input);
            baseSrc[c] = s; baseList.Add(s);
            return s;
        }

        Clip Pick(Slot slot) => Pick(slot, seed);
        Clip Pick(Slot slot, int i) { var l = lib.Get(slot, hero); return l.Count > 0 ? l[((i % l.Count) + l.Count) % l.Count] : null; }

        void Start(Slot slot, Clip clip, float? target = null, bool hold = false)
        {
            if (clip == null) return;
            // a one-shot cutting into another that still shows: the pose would jump - inertialize it
            if (action != null && action.w > 0.05f) cut = true;
            float want = target ?? (TARGET.TryGetValue(slot, out var tg) ? tg : clip.duration);
            float rate = Mathf.Min(2.2f, Mathf.Max(0.6f, clip.duration / Mathf.Max(0.05f, want)));
            action = new Action { clip = clip, slot = slot, t = 0, rate = rate, full = FULL.Contains(slot), w = 0, hold = hold };
            FreeSrc(actUp); FreeSrc(actLo);
            actUp = MakeSrc(clip); actLo = MakeSrc(clip);
            layers.DisconnectInput(1); layers.DisconnectInput(2);
            graph.Connect(actUp.top, 0, layers, 1); graph.Connect(actLo.top, 0, layers, 2);
        }
        void EndAction()
        {
            action = null;
            layers.DisconnectInput(1); layers.DisconnectInput(2);
            layers.SetInputWeight(1, 0); layers.SetInputWeight(2, 0);
            FreeSrc(actUp); FreeSrc(actLo); actUp = actLo = null;
        }

        readonly List<(Src s, float w)> wBase = new List<(Src, float)>();
        float cL, cR;
        void Acc(Clip c, float t, float w)
        {
            if (w <= 0) return;
            var s = BaseSrc(c);
            s.cp.SetTime(c.SourceTime(t));
            wBase.Add((s, w));
            int k = c.ContactAt(t);
            cL += (k & 1) * w; cR += ((k >> 1) & 1) * w;
        }
        void ApplyBase()
        {
            float total = 0; foreach (var e in wBase) total += e.w;
            foreach (var s in baseList) baseMix.SetInputWeight(s.input, 0);
            if (total > 1e-6f) foreach (var e in wBase) baseMix.SetInputWeight(e.s.input, baseMix.GetInputWeight(e.s.input) + e.w / total);
        }

        /// <summary>
        /// The layer for this frame (TS ClipLayer.update): the clip pose is evaluated onto the bones, and Result carries the
        /// region weights and the feet planted. Result.ok false: no clip pose this frame (the procedural layer alone).
        /// </summary>
        public Out Update(AnimState s, Input k)
        {
            // real elapsed time: the gait phase must keep up with the distance the body really covered, or locked feet fall
            // behind the clip on slow frames
            float dt = Mathf.Min(0.25f, Mathf.Max(0, s.dt));
            o.action = ""; o.armsAction = 0; o.clipName = ""; o.ok = false;
            wBase.Clear(); cL = cR = 0;
            // ---- death: full body, held on the last frame
            if (s.dead)
            {
                if (death == null) { death = Pick(Slot.death); cut = true; }
                if (death == null) return o;
                if (action != null) EndAction();
                Acc(death, Mathf.Min(death.duration, s.deathAge * Mathf.Max(1, death.duration / 1.6f)), 1);
                ApplyBase();
                o.contact[0] = cL; o.contact[1] = cR;
                o.legs = o.torso = o.armsAction = 1; o.armsLoco = 0; o.loco = 0; o.action = "death"; o.clipName = death.name;
                graph.Evaluate(0);
                return Ok();
            }
            if (death != null) { death = null; lastOk = false; offT = 9; }        // (back from the dead: no flow from the corpse)
            // ---- one-shot triggers (ages drop to ~0 when a new event happens)
            if (s.attackAge < pAttack - 1e-6f)
            {
                if (s.attackKind == "punch") Start(Slot.punch, Pick(Slot.punch, 0) ?? Pick(Slot.melee, 0));
                else if (s.melee && !s.hammer && lib.Has(Slot.melee, hero))
                {
                    // melee combos: each swing plays the next hit of the combo, reset after a pause
                    combo = s.time - lastMelee < 1.1f ? combo + 1 : 0;
                    lastMelee = s.time;
                    var c = Pick(Slot.melee, combo);
                    Start(Slot.melee, c, Mathf.Max(0.3f, (s.attackTime > 0 ? s.attackTime : c?.duration ?? 0.6f) * 1.05f));
                }
            }
            if (s.castAge < pCast - 1e-6f && !string.IsNullOrEmpty(s.castId))
            {
                if (lib.casts.TryGetValue(s.castId, out var own)) Start(Slot.cast, own.clip, own.target);
                else
                {
                    var list = CAST_SLOT.TryGetValue(s.castId, out var sl) ? sl : new[] { Slot.cast };
                    foreach (var slot in list)
                        if (lib.Has(slot, hero)) { Start(slot, Pick(slot, slot == Slot.melee ? 0 : seed + Mathf.FloorToInt(s.time))); break; }
                }
            }
            if (s.hitAge < pHit - 1e-6f && (action == null || action.slot == Slot.hit)) Start(Slot.hit, Pick(Slot.hit, Mathf.FloorToInt(s.time * 7)));
            if (s.jumpAge < pJump - 1e-6f && (action == null || !action.full)) Start(Slot.jump_start, Pick(Slot.jump_start, 0));
            if (s.landAge < pLand - 1e-6f && s.jumpAge > 0.25f && (action == null || action.slot == Slot.jump_start)) Start(Slot.land, Pick(Slot.land, 0));
            // reload: upper body only, time-scaled to the weapon's reload (the rising edge of reloadLeft)
            float rl = s.reloadLeft;
            if (rl > pReload + 1e-3f && (action == null || action.slot == Slot.shoot)) Start(Slot.reload, Pick(Slot.reload, 0), Mathf.Max(0.4f, (s.reloadDur ?? rl) * 0.95f));
            pReload = rl;
            pAttack = s.attackAge; pCast = s.castAge; pHit = s.hitAge; pJump = s.jumpAge; pLand = s.landAge;
            if (!k.eligible) { if (action != null) EndAction(); lastOk = false; offT = 9; return o; }
            // ---- base: idle / locomotion / airborne / stunned
            float air = k.airBlend;
            var idleClip = s.stunned ? Pick(Slot.stun, 0) ?? Pick(Slot.idle, 0) : Pick(Slot.idle, 0);
            idleT += dt;
            var locoList = lib.Blend(k.angle, k.speed);
            float wLoco = locoList.Count > 0 ? k.moveBlend : 0;
            float wIdle = idleClip != null ? 1 - k.moveBlend : 0;
            float ground = (wLoco + wIdle) * (1 - air);
            if (wLoco > 0 && air < 1)
            {
                // distance-driven phase: one cycle = the blended stride
                float D = 0, W = 0;
                foreach (var e in locoList) { D += e.w * e.clip.speed * e.clip.duration; W += e.w; }
                D /= Mathf.Max(1e-6f, W);
                if (D > 1e-4f) phase = (phase + k.speed * dt / D) % 1;
                foreach (var e in locoList) Acc(e.clip, ((phase + e.clip.phase0) % 1) * e.clip.duration, e.w * wLoco * (1 - air));
            }
            if (wIdle > 0 && air < 1 && idleClip != null) Acc(idleClip, idleT, wIdle * (1 - air));
            var jl = air > 0.01f ? Pick(Slot.jump_loop, 0) : null;
            if (jl != null) Acc(jl, idleT, air);
            ApplyBase();
            float baseW = Mathf.Min(1, ground + (jl != null ? air : 0));
            float cw = (wLoco + wIdle) > 0 ? 1 / Mathf.Max(1e-6f, wLoco + wIdle) : 0;
            if (air > 0.5f) cL = cR = 0;
            else { float n = cw / (1 - air); cL *= n; cR *= n; }
            o.loco = (wLoco * (1 - air)) / Mathf.Max(1e-6f, baseW);
            o.legs = o.torso = o.armsLoco = baseW;
            // ---- one-shot on top
            var a = action;
            float upW = 0, legW = 0;
            if (a != null)
            {
                a.t += dt * a.rate;
                float real = a.clip.duration / a.rate, tr = a.t / a.rate;
                if (a.t >= a.clip.duration && !a.hold) { if (a.w > 0.3f) cut = true; EndAction(); }
                else
                {
                    // fast in (the pose answers the button within a few frames), eased out: trimmed gestures end on their key pose
                    // and blend back to the base over ~0.2 s instead of the clip's own walk back to idle
                    a.w = Mathf.Min(1, Mathf.Min(tr / 0.06f, a.clip.loop || a.hold ? 1 : Mathf.Max(0, (real - tr) / 0.2f)));
                    float st = a.clip.SourceTime(a.t);
                    actUp.cp.SetTime(st); actLo.cp.SetTime(st);
                    // full-body moves own the legs; melee owns them only while standing (you can swing on the run)
                    legW = a.full ? a.w : STANCE.Contains(a.slot) ? a.w * (1 - k.moveBlend * 0.85f) : 0;
                    upW = a.w * (a.slot == Slot.hit ? 0.65f : 1);
                    if (legW > 0)
                    {
                        int ac = a.clip.ContactAt(a.t);
                        cL = cL * (1 - legW) + (ac & 1) * legW; cR = cR * (1 - legW) + ((ac >> 1) & 1) * legW;
                        o.legs = o.legs + (1 - o.legs) * legW; o.loco *= 1 - legW;
                    }
                    o.torso = o.torso + (1 - o.torso) * upW;
                    o.armsAction = upW;
                    o.action = a.slot.ToString(); o.clipName = a.clip.name;
                }
            }
            layers.SetInputWeight(1, upW); layers.SetInputWeight(2, legW);
            o.contact[0] = cL; o.contact[1] = cR;
            if (wBase.Count == 0 && action == null) return o;           // no clip at all: the procedural layer alone
            graph.Evaluate(0);
            return Ok();
        }
        Out Ok() { o.ok = true; return o; }

        /// <summary>
        /// The TS inertialize, on the pose read back from the bones (ProcAnimator's model-space rotations and positions, by TS
        /// bone name): a cut this frame opens an offset from last frame's pose to this one; every frame the remaining offset
        /// (decayed by InertDecay) rides on top, so the body flows from where it was into the new motion instead of snapping.
        /// </summary>
        public void Inertialize(Dictionary<string, Quaternion> q, Dictionary<string, Vector3> p, float dt)
        {
            if (cut && lastOk)
            {
                offQ.Clear(); offP.Clear();
                foreach (var kv in q)
                {
                    if (!lastQ.TryGetValue(kv.Key, out var lq)) continue;
                    var f = lq * Quaternion.Inverse(kv.Value);
                    if (f.w < 0) f = new Quaternion(-f.x, -f.y, -f.z, -f.w);       // the short way round
                    offQ[kv.Key] = f;
                    if (p.TryGetValue(kv.Key, out var pp) && lastP.TryGetValue(kv.Key, out var lp)) offP[kv.Key] = lp - pp;
                }
                offT = 0;
            }
            else offT += Mathf.Max(0, dt);
            cut = false;
            if (offT < 6 * INERT_HALFLIFE)
            {
                float k = InertDecay(offT);
                foreach (var kv in offQ) if (q.TryGetValue(kv.Key, out var cur)) q[kv.Key] = Quaternion.Slerp(Quaternion.identity, kv.Value, k) * cur;
                foreach (var kv in offP) if (p.TryGetValue(kv.Key, out var cur)) p[kv.Key] = cur + kv.Value * k;
            }
            lastQ.Clear(); foreach (var kv in q) lastQ[kv.Key] = kv.Value;
            lastP.Clear(); foreach (var kv in p) lastP[kv.Key] = kv.Value;
            lastOk = true;
        }

        public void Dispose()
        {
            if (action != null) EndAction();
            foreach (var s in baseList) if (s.mirrored) ClipMirror.Dispose(s.job);
            baseList.Clear(); baseSrc.Clear();
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
