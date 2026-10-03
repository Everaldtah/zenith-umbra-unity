// Dedicated first-person arms: a forearm + hand mesh per hero side (Tripo, rigged on its own small skeleton by
// tools/blender/fp_arm_bind.py - forearm_S, hand_S, five fingers x3, the hero rigs' names - and imported by
// `zu_import_fp_arms` into Resources/ZUFp/arms_<hero>.prefab). The Tripo HERO meshes have mitten hands that read as
// blocks at 0.4 m; these have five jointed fingers. The viewmodel's IK still runs on the hero's own rig; every frame
// each dedicated arm copies the hero forearm's and hand's motion (their rotation away from rest, applied to its own rest
// turned onto the hero's hand frame) and is moved so its wrist sits on the hero's wrist; its fingers curl through their own
// Fingers (the same grips). The hero's own mesh keeps only the upper arm and the elbow half of the forearm.
// No prefab for a hero: nothing changes (the arms-only cut of the hero mesh, as before).
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.FirstPerson
{
    public class FpArms
    {
        class Side
        {
            public string S; public Transform fore, hand, root;
            /// <summary>the dedicated arm's rest turned onto the hero's hand frame (model space), forearm and hand</summary>
            public Quaternion foreRest, handRest;
        }
        readonly List<Side> sides = new List<Side>();
        readonly RigPose rig;
        public readonly GameObject go;
        public readonly Fingers fingers;
        public readonly Renderer[] renderers;

        FpArms(GameObject go, RigPose rig)
        {
            this.go = go; this.rig = rig;
            fingers = Fingers.Build(go.transform);
            renderers = go.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>the hero's dedicated arms (null when none is published, or the rigs can't be matched)</summary>
        public static FpArms Load(string heroId, Transform model, RigPose rig, int layer)
        {
            var pf = Resources.Load<GameObject>("ZUFp/arms_" + heroId);
            if (pf == null) return null;
            var go = Object.Instantiate(pf, model);
            go.name = "fp_arms";
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            var a = new FpArms(go, rig);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            foreach (var S in new[] { "L", "R" })
            {
                if (!byName.TryGetValue("forearm_" + S, out var fore) || !byName.TryGetValue("hand_" + S, out var hand)) continue;
                if (!byName.TryGetValue("index1_" + S, out var idx) || !byName.TryGetValue("pinky1_" + S, out var pky)) continue;
                if (!rig.Has("forearm_" + S) || !rig.Has("hand_" + S) || !rig.rest.ContainsKey("index1_" + S) || !rig.rest.ContainsKey("pinky1_" + S)) continue;
                var root = model;
                Vector3 P(Transform t) => root.InverseTransformPoint(t.position);
                Quaternion Q(Transform t) => Quaternion.Inverse(root.rotation) * t.rotation;
                // the hand frames (model space): along the forearm, the back of the hand
                var hf = Frame(rig.rest["forearm_" + S].p, rig.rest["hand_" + S].p, rig.rest["index1_" + S].p, rig.rest["pinky1_" + S].p, S);
                var df = Frame(P(fore), P(hand), P(idx), P(pky), S);
                var align = hf * Quaternion.Inverse(df);
                // size: the dedicated hand to the hero's hand (wrist -> middle knuckle), the whole side scaled about its root
                if (byName.TryGetValue("middle1_" + S, out var mid) && rig.rest.ContainsKey("middle1_" + S))
                {
                    float want = Vector3.Distance(rig.rest["hand_" + S].p, rig.rest["middle1_" + S].p), have = Vector3.Distance(P(hand), P(mid));
                    var sideRoot = TopOf(fore, go.transform);
                    if (have > 1e-4f && sideRoot != null) sideRoot.localScale *= Mathf.Clamp(want / have, 0.5f, 2f);
                }
                a.sides.Add(new Side { S = S, fore = fore, hand = hand, root = TopOf(fore, go.transform), foreRest = align * Q(fore), handRest = align * Q(hand) });
            }
            if (a.sides.Count == 0) { Object.Destroy(go); return null; }
            foreach (var r in a.renderers) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; }
            return a;
        }

        static Transform TopOf(Transform t, Transform stop) { while (t.parent != null && t.parent != stop) t = t.parent; return t.parent == stop ? t : null; }

        /// <summary>a hand's frame: forward along the forearm, up out of the back of the hand (the same rule for both rigs)</summary>
        static Quaternion Frame(Vector3 fore, Vector3 hand, Vector3 idx, Vector3 pky, string S)
        {
            var along = (hand - fore).normalized;
            var across = pky - idx;
            var back = Vector3.Cross(along, across) * (S == "R" ? -1 : 1);
            back -= along * Vector3.Dot(back, along);
            if (back.sqrMagnitude < 1e-8f) back = Vector3.up;
            return Quaternion.LookRotation(along, back.normalized);
        }

        /// <summary>after the viewmodel's IK: the dedicated arms onto the hero rig's forearms and hands</summary>
        public void Drive()
        {
            var root = rig.root;
            foreach (var s in sides)
            {
                var hf = rig.B("forearm_" + s.S); var hh = rig.B("hand_" + s.S);
                var dFore = (Quaternion.Inverse(root.rotation) * hf.rotation) * Quaternion.Inverse(rig.rest["forearm_" + s.S].q);
                var dHand = (Quaternion.Inverse(root.rotation) * hh.rotation) * Quaternion.Inverse(rig.rest["hand_" + s.S].q);
                s.fore.rotation = root.rotation * (dFore * s.foreRest);
                s.hand.rotation = root.rotation * (dHand * s.handRest);
                // the wrist on the hero's wrist (the whole side moves with it)
                var d = hh.position - s.hand.position;
                if (s.root != null) s.root.position += d; else s.fore.position += d;
            }
        }

        public void Show(bool on) { foreach (var r in renderers) if (r != null) r.enabled = on; }
    }
}
