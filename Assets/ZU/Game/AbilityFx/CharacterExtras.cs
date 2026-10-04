// The parts of the TS render/CharacterView.ts this slice owns, hung on each hero's view without touching HeroView:
//  - the blinking lids / masked eye glow (CharacterView: `lids`, `eyeGlow`), from the eye data measured on the model;
//  - the ragdoll death (CharacterView.deathRagdoll / endRagdoll): thrown by the killing blow, sinking before the respawn;
//  - Enra's chain blades (CharacterView.updateChains): ChainBladeView, attached here, runs after HeldRig.
// A hero's view is found by ViewOf (default: the HeroView child of the match named "<def> #<id> (<team>)", as
// ActorViews.Create names it; MatchRunner can hand its own lookup in). Runs in AbilityFx's LateUpdate: after the Animator,
// before HeroView's LateUpdate (held weapons, fingers) and the hair / cloth solver (ZuDynamics, order 500).
using System;
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class CharacterExtras
    {
        /// <summary>a ragdoll's hard landing (TS CharacterView.onBodyFall: the match plays the thud) - actor, Unity position, speed</summary>
        public static event Action<Actor, Vector3, float> BodyFall;
        /// <summary>the TS BRIGHT_SUITS: heroes whose suits already read bright get less self-light on the lids</summary>
        static readonly HashSet<string> BRIGHT_SUITS = new HashSet<string> { "mirei" };

        sealed class Extra
        {
            public GameObject view; public Animator anim; public Renderer body; public HeroView hv;
            public Dictionary<string, Transform> bones;
            public Eyelids lids; public EyeGlow glow;
            public Ragdoll ragdoll; public bool ragdollDone;
            public List<(Transform t, Vector3 p, Quaternion q)> frozen;
            public Transform hips; public Vector3 hipsRest;
        }

        readonly MatchRunner r;
        readonly Func<Actor, GameObject> viewOf;
        readonly Dictionary<int, Extra> extras = new Dictionary<int, Extra>();
        readonly Dictionary<int, GameObject> byId = new Dictionary<int, GameObject>();
        int indexedChildren = -1;

        public CharacterExtras(MatchRunner r, Func<Actor, GameObject> viewOf = null)
        {
            this.r = r; this.viewOf = viewOf ?? DefaultViewOf;
        }

        /// <summary>the HeroView object of an actor among the match's children (named by ActorViews.Create)</summary>
        GameObject DefaultViewOf(Actor a)
        {
            var root = r.transform;
            if (root.childCount != indexedChildren)
            {
                indexedChildren = root.childCount; byId.Clear();
                for (int i = 0; i < root.childCount; i++)
                {
                    var c = root.GetChild(i); var n = c.name; int h = n.IndexOf(" #", StringComparison.Ordinal);
                    if (h < 0 || c.GetComponent<HeroView>() == null) continue;
                    int e = n.IndexOf(' ', h + 2);
                    if (int.TryParse(e > 0 ? n.Substring(h + 2, e - h - 2) : n.Substring(h + 2), out var id)) byId[id] = c.gameObject;
                }
            }
            return byId.TryGetValue(a.id, out var go) ? go : null;
        }

        Extra Bind(Actor a)
        {
            if (extras.TryGetValue(a.id, out var x) && x.view != null) return x;
            var view = viewOf(a);
            if (view == null) return null;
            x = new Extra { view = view, anim = view.GetComponentInChildren<Animator>(true), body = view.GetComponentInChildren<SkinnedMeshRenderer>(true), hv = view.GetComponent<HeroView>() };
            x.bones = new Dictionary<string, Transform>();
            foreach (var t in view.GetComponentsInChildren<Transform>(true)) if (!x.bones.ContainsKey(t.name)) x.bones[t.name] = t;
            // blinking: lids over the painted eyes the rigger found on the face; masked heroes: the sockets glow instead
            var model = x.anim != null ? x.anim.transform : view.transform;
            if (x.bones.TryGetValue("head", out var head))
            {
                var (eyes, glow) = EyeInfo.For(a.def.id);
                if (eyes != null && eyes.Length == 2) x.lids = new Eyelids(model, head, eyes, BRIGHT_SUITS.Contains(a.def.id) ? 0.04f : 0.16f);
                if (glow != null && glow.Length == 2) x.glow = new EyeGlow(model, head, glow, a.def.glow);
            }
            // Enra's Hellfire Chains: the chain, yoke, fire and trail beside HeldRig's blades
            if (Held.For(a.def.id)?.chains == true) ChainBladeView.Attach(view, a, r);
            extras[a.id] = x;
            return x;
        }

        bool Ragdollable(Actor a, Extra x)
        {
            if (a.def.frame == "mech" || a.def.frame == "drone" || a.isBoss || !string.IsNullOrEmpty(a.def.holo)) return false;   // (a hologram fades, it doesn't fall)
            foreach (var n in Ragdoll.REQUIRED) if (!x.bones.ContainsKey(n)) return false;
            return true;
        }

        public void Update(World w, float time, float dt)
        {
            foreach (var live in w.actors)
            {
                // (while the kill cam replays, the views show each hero as it was: so do the lids and the ragdolls)
                var a = KillCam.PastOrLive(r, live);
                if (a.IsSummon && string.IsNullOrEmpty(a.def.model)) continue;      // (the puppets are the swarm's)
                var x = Bind(a);
                if (x == null) continue;
                // the lids and the glow follow the body's renderer and fade with it (a cloaked hero: allies 0.35, enemies 0.25
                // seen / 0.04 not - HeroView.BodyAlpha; fixed in both games)
                bool shown = x.body != null && x.body.enabled;
                float alpha = x.hv != null ? x.hv.BodyAlpha : 1;
                if (!a.alive) DeathRagdoll(w, a, x, time, dt);
                else if (x.ragdoll != null || x.ragdollDone) { x.ragdoll = null; x.ragdollDone = false; x.frozen = null; }   // back to life: the Animator takes the skeleton back
                x.lids?.Update(time, (float)a.anim.hitAt, !a.alive, shown, alpha);
                x.glow?.Update(time, (float)a.anim.castAt, (float)a.anim.hitAt, !a.alive, shown, alpha);
            }
            // views that went away (a hero swapped out, the match rebuilt)
            List<int> gone = null;
            foreach (var kv in extras) if (kv.Value.view == null) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (var id in gone) { extras[id].lids?.Dispose(); extras[id].glow?.Dispose(); extras.Remove(id); }
        }

        /// <summary>runs the ragdoll while the hero is dead (TS CharacterView.deathRagdoll)</summary>
        void DeathRagdoll(World w, Actor a, Extra x, float time, float dt)
        {
            float age = time - (float)a.deathAt;
            if (x.ragdoll == null && !x.ragdollDone)
            {
                x.ragdollDone = true;
                if (!Ragdollable(a, x) || age > 0.5f) return;
                // the killing blow: away from the killer, a little up, harder for a bigger hit (Overwatch's ragdolls fly)
                var fling = Vector3.zero;
                var k = a.lastHitBy != null && time - a.lastHitAt < 1 ? a.lastHitBy : null;
                if (k != null)
                {
                    fling = new Vector3((float)(a.pos.x - k.pos.x), 0, (float)(a.pos.z - k.pos.z));
                    if (fling.sqrMagnitude < 1e-6f) fling = new Vector3(-Mathf.Sin((float)a.yaw), 0, -Mathf.Cos((float)a.yaw));
                    fling = fling.normalized; fling.y = 0.45f;
                    fling = fling.normalized * Mathf.Min(9, 2.5f + (float)a.Sv("lastHitDmg", 30) * 0.03f);
                }
                // TS-PARITY: a.height already includes the scale; the TS multiplies by it again
                float H = (float)(a.Height * a.scale);
                // the bones the Animator writes but the ragdoll doesn't drive (spine, shoulders, toes...) stay in their death pose
                var driven = new HashSet<Transform>();
                x.ragdoll = new Ragdoll(x.bones, H, Conv.U(a.vel), Sp.U(fling), w.level, (float)a.pos.y, x.view.transform.forward);
                foreach (var t in x.ragdoll.Driven) driven.Add(t);
                x.frozen = new List<(Transform, Vector3, Quaternion)>();
                if (x.anim != null && x.anim.isHuman)
                    foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (hb == HumanBodyBones.LastBone) continue;
                        var t = x.anim.GetBoneTransform(hb);
                        if (t != null && !driven.Contains(t)) x.frozen.Add((t, t.localPosition, t.localRotation));
                    }
                x.ragdoll.onImpact = (at, speed) => BodyFall?.Invoke(a, at, speed);
                x.bones.TryGetValue("hips", out x.hips);
            }
            if (x.ragdoll == null) return;
            foreach (var (t, p, q) in x.frozen) if (t != null) { t.localPosition = p; t.localRotation = q; }
            // (the clip layer's Animator has no controller and isn't evaluated for the dead: only a controller-driven one
            // overwrites a sleeping body)
            x.ragdoll.Step(dt, x.anim != null && x.anim.runtimeAnimatorController != null);
            // sinking into the floor before the respawn: the whole body lowers (TS: the model group, from 2.6 s)
            float sink = Mathf.Max(0, age - 2.6f) * 0.6f;
            // (absolute, from the pelvis: a sleeping body isn't re-posed, so the sink can't accumulate frame on frame)
            if (sink > 0 && x.hips != null) x.hips.position = x.ragdoll.Center - new Vector3(0, sink, 0);
        }

        public void Dispose()
        {
            foreach (var x in extras.Values) { x.lids?.Dispose(); x.glow?.Dispose(); }
            extras.Clear();
        }
    }
}
