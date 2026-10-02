// Enra's Hellfire Chains on his third-person body, per frame (TS CharacterView.updateChains, ported; ChainBlades.cs). The
// bracer is read off the real forearm bone (the blade may be out on its chain, so the fist's frame can't give it); the
// chain pays out to wherever the blade is - a slack loop at the hip when it's in the hand, a taut line when it flies -
// and the yoke runs on from each bracer up the arm and over his shoulders to the other, so the two blades are one chain
// end to end. On an attack the swinging blade burns: its flame sheets light, its own glow is driven up, the chain's
// embers flare and its tip leaves a trail.
//
// It rides the hero's view next to HeldRig (which lays the blades from the bones and the animator's orbit, and wears the
// bracer props) and runs after it (order 100 > HeroView's LateUpdate), so the chain ends where the blade is this frame.
// CharacterExtras attaches it to a hero whose held spec has chains.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    [DefaultExecutionOrder(100)]
    public sealed class ChainBladeView : MonoBehaviour
    {
        /// <summary>where each hand's blade is on its chain (0 in the fist .. 1 at full length); null = the third-person timelines
        /// (ChainBlades.Ext, the TS Animator's chainExt). The animator can hand its own in.</summary>
        public System.Func<Actor, float[]> ChainExtSource;

        HeldRig held; MatchRunner runner; Actor actor;
        ChainBlades.Chain[] chains; ChainBlades.Chain yoke;
        ChainBlades.Flame[] flames; ChainBlades.Trail[] trails;
        readonly List<ChainBlades.Heat>[] heats = { new List<ChainBlades.Heat>(), new List<ChainBlades.Heat>() };
        readonly bool[] heated = new bool[2];
        readonly List<Vector3>[] chainPts = { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
        readonly List<Vector3> way = new List<Vector3>();
        readonly float[] ext = new float[2];

        public static ChainBladeView Attach(GameObject view, Actor a, MatchRunner r)
        {
            var held = view.GetComponent<HeldRig>();
            if (held == null || held.spec == null || !held.spec.chains || held.rig == null) return null;
            var v = view.GetComponent<ChainBladeView>() ?? view.AddComponent<ChainBladeView>();
            v.Build(held, a, r);
            return v;
        }

        void Build(HeldRig h, Actor a, MatchRunner r)
        {
            held = h; actor = a; runner = r;
            float L = h.rig.height; var root = h.rig.root;
            var items = new[] { h.spec.L, h.spec.R };
            chains = new ChainBlades.Chain[2]; flames = new ChainBlades.Flame[2]; trails = new ChainBlades.Trail[2];
            for (int i = 0; i < 2; i++)
            {
                chains[i] = ChainBlades.BuildChain(L); chains[i].visible = items[i] != null;
                // the fire on the blade, in its pitched frame (TS: the flame group under the gun with rotation.x = pitch)
                var parent = h.slots[i]?.body != null ? h.slots[i].body : h.slots[i]?.node;
                flames[i] = parent != null ? ChainBlades.BuildFlame(L, (items[i]?.size ?? 0.37f) * L, parent) : null;
                trails[i] = ChainBlades.BuildTrail(root);
            }
            yoke = ChainBlades.BuildChain(L, 72);
        }

        void LateUpdate()
        {
            if (held == null || actor == null || runner == null || runner.World == null) return;
            UpdateChains(Time.deltaTime, (float)runner.World.time);
        }

        void UpdateChains(float dt, float time)
        {
            var a = actor; var rig = held.rig; float L = rig.height; var root = rig.root;
            Vector3? Bone(string n) { var b = rig.B(n); return b != null ? root.InverseTransformPoint(b.position) : (Vector3?)null; }
            float age = time - (float)a.anim.attackAt; string kind = a.anim.attackKind;
            var e = ChainExtSource?.Invoke(a);
            if (e != null && e.Length >= 2) { ext[0] = e[0]; ext[1] = e[1]; } else ChainBlades.Ext(a, time, ext);
            // which blade a light swing is on: the one out on its chain, else the sweep side
            int swingHand = ext[0] > ext[1] ? 0 : ext[1] > ext[0] ? 1 : (a.anim.attackSide > 0 ? 1 : 0);
            Vector3?[] bracerP = new Vector3?[2], elbowP = new Vector3?[2], shoulderP = new Vector3?[2];
            for (int i = 0; i < 2; i++)
            {
                string S = i == 1 ? "R" : "L"; var c = chains[i]; var s = held.slots[i]; var it = s?.item;
                var fa = Bone($"forearm_{S}"); var hn = Bone($"hand_{S}") ?? fa; var up = Bone($"upperarm_{S}");
                bool vis = s != null && it != null && fa.HasValue && hn.HasValue && !s.hidden && s.node.gameObject.activeInHierarchy;
                c.visible = vis;
                if (!vis)
                {
                    c.prev = null;
                    if (flames[i] != null) ChainBlades.UpdateFlame(flames[i], 0, dt, time);
                    ChainBlades.UpdateTrail(trails[i], null, null, false, time);
                    if (heated[i]) { ChainBlades.SetHeat(heats[i], 0); heated[i] = false; }
                    continue;
                }
                float len = it.size * L;
                var br = Vector3.Lerp(fa.Value, hn.Value, 0.62f);
                bracerP[i] = br; elbowP[i] = fa; shoulderP[i] = up ?? fa;
                // (the bracer props themselves: HeldRig.Place puts them on the forearms)
                // the chain: from the bracer to the ring at the pommel, about 1.2 m of it coiled at rest (the study), paid out
                // to the blade when it's flung
                var gp = s.node.localPosition; var gq = s.node.localRotation;
                var pommel = gq * new Vector3(0, 0, -0.12f * len) + gp;
                var tip = gq * new Vector3(0, 0, 0.88f * len) + gp;
                float rest = 0.36f * L;
                ChainBlades.LayChain(c, ChainBlades.SlackCurve(c, br, pommel, Mathf.Max(rest, Vector3.Distance(br, pommel)), dt, chainPts[i]), root);
                // the fire: on the swinging blade for a light swing, the right blade for the throw
                bool on = (kind == "primary" && age < ChainBlades.CB_SWING && i == swingHand) || (kind == "secondary" && age < ChainBlades.CB_THROW && i == 1);
                float k = on ? ChainBlades.FireK(kind, age) : 0;
                if (flames[i] != null) ChainBlades.UpdateFlame(flames[i], k, dt, time);
                // the ribbon is the cut at the chain's reach (not the fling out and the haul back: those span metres in a few
                // frames and would draw as slabs)
                ChainBlades.UpdateTrail(trails[i], pommel, tip, k > 0.3f && ext[i] > 0.6f, time);
                ChainBlades.SetChainHeat(c, k);
                if (heats[i].Count == 0 && s.rends != null) heats[i] = ChainBlades.HeatMaterials(s.rends);
                if (k > 0 || heated[i]) { ChainBlades.SetHeat(heats[i], k); heated[i] = k > 0; }
            }
            // the yoke: bracer -> elbow -> shoulder -> the nape -> shoulder -> elbow -> bracer, hanging just off the body
            var neck = Bone("neck") ?? Bone("chest");
            if (bracerP[0].HasValue && bracerP[1].HasValue && neck.HasValue && chains[0].visible && chains[1].visible)
            {
                // clear of the body: Enra's pauldrons stand a good way off the shoulder joints, so the run over them is lifted
                // and set back (a chain draped over the armour, not threaded through it)
                var back = new Vector3(0, 0, -1); var lift = new Vector3(0, 1, 0);
                way.Clear();
                way.Add(bracerP[0].Value);
                way.Add(elbowP[0].Value + back * (0.1f * L));
                way.Add(shoulderP[0].Value + back * (0.21f * L) + lift * (0.11f * L));
                way.Add(neck.Value + back * (0.25f * L) + lift * (0.04f * L));
                way.Add(shoulderP[1].Value + back * (0.21f * L) + lift * (0.11f * L));
                way.Add(elbowP[1].Value + back * (0.1f * L));
                way.Add(bracerP[1].Value);
                ChainBlades.LayChain(yoke, ChainBlades.YokeCurve(way, chainPts[2]), root);
                yoke.visible = true;
                ChainBlades.SetChainHeat(yoke, Mathf.Max(flames[0]?.k ?? 0, flames[1]?.k ?? 0) * 0.5f);
            }
            else yoke.visible = false;
            foreach (var c in chains) c.Draw();
            yoke.Draw();
        }

        void OnDestroy()
        {
            if (trails != null) foreach (var t in trails) if (t?.go != null) Destroy(t.go);
            if (flames != null) foreach (var f in flames) if (f?.group != null) Destroy(f.group.gameObject);
        }
    }
}
