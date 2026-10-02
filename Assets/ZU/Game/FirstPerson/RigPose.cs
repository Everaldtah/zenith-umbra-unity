// The rig as the first-person code sees it: the bones by name, their rest pose in MODEL space (the prefab root's local
// space, metres), the arm lengths, and the two-bone IK + aim used to put a hand on a view-space target (port of
// Animator.ts: rest / aimBone / ik). Model space here is Unity's: +X is the character's right, +Z their front.
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.FirstPerson
{
    public class RigPose
    {
        public struct Rest { public Vector3 p; public Quaternion q; public Quaternion local; public Vector3 dir; }
        public readonly Transform root;
        public readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Rest> rest = new Dictionary<string, Rest>();
        /// <summary>the TS Animator's model height: the head bone's rest height x 1.08 (every prop size is a fraction of it)</summary>
        public float height = 1.8f;
        public bool ok;

        static readonly Dictionary<string, string> CHILD = new Dictionary<string, string>
        {
            { "hips", "spine" }, { "spine", "chest" }, { "chest", "neck" }, { "neck", "head" },
            { "shoulder_L", "upperarm_L" }, { "upperarm_L", "forearm_L" }, { "forearm_L", "hand_L" },
            { "shoulder_R", "upperarm_R" }, { "upperarm_R", "forearm_R" }, { "forearm_R", "hand_R" },
            { "thigh_L", "shin_L" }, { "shin_L", "foot_L" }, { "foot_L", "toe_L" }, { "thigh_R", "shin_R" }, { "shin_R", "foot_R" }, { "foot_R", "toe_R" },
        };

        public RigPose(Transform root)
        {
            this.root = root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            ok = bones.ContainsKey("hips") && bones.ContainsKey("head") && bones.ContainsKey("upperarm_L") && bones.ContainsKey("upperarm_R");
            Bind();
        }

        /// <summary>record the current pose as the rest pose (call before anything animates the rig)</summary>
        public void Bind()
        {
            rest.Clear();
            foreach (var kv in bones)
            {
                var t = kv.Value;
                rest[kv.Key] = new Rest { p = root.InverseTransformPoint(t.position), q = Quaternion.Inverse(root.rotation) * t.rotation, local = t.localRotation, dir = Vector3.up };
            }
            foreach (var n in new List<string>(rest.Keys))
            {
                var r = rest[n];
                if (CHILD.TryGetValue(n, out var c) && rest.TryGetValue(c, out var rc)) r.dir = (rc.p - r.p).normalized;
                else if (n.StartsWith("hand") && rest.TryGetValue(n.Replace("hand", "forearm"), out var rf)) r.dir = rf.dir;
                rest[n] = r;
            }
            if (rest.TryGetValue("head", out var head)) height = head.p.y * 1.08f;
        }

        public Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
        public bool Has(string n) => bones.ContainsKey(n);
        public Vector3 Pos(string n) => root.InverseTransformPoint(bones[n].position);

        /// <summary>put a bone back to its rest rotation (local)</summary>
        public void ResetBone(string n) { if (bones.TryGetValue(n, out var t) && rest.TryGetValue(n, out var r)) t.localRotation = r.local; }

        /// <summary>rotate a bone from its REST so its rest direction (toward its child) points along `dir` (model space);
        /// the minimal rotation, as the TS aimBone</summary>
        public void AimBone(string n, Vector3 dir)
        {
            if (!bones.TryGetValue(n, out var t) || !rest.TryGetValue(n, out var r)) return;
            var D = Quaternion.FromToRotation(r.dir, dir.normalized);
            t.rotation = root.rotation * (D * r.q);
        }

        /// <summary>2-bone IK: [upperDir, lowerDir] in model space for a limb from `rootP` to `target`</summary>
        public static void IK(Vector3 rootP, Vector3 target, float l1, float l2, Vector3 pole, out Vector3 upper, out Vector3 lower)
        {
            var d = target - rootP;
            float len = d.magnitude, maxL = (l1 + l2) * 0.999f;
            if (len > maxL) { d *= maxL / len; len = maxL; }
            len = Mathf.Max(len, Mathf.Abs(l1 - l2) + 1e-3f);
            var dir = d.normalized;
            float a = (l1 * l1 - l2 * l2 + len * len) / (2 * len);
            float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - a * a));
            var pd = pole - dir * Vector3.Dot(pole, dir);
            if (pd.sqrMagnitude < 1e-6f) pd = Vector3.forward;
            pd.Normalize();
            var knee = rootP + dir * a + pd * h;
            upper = (knee - rootP).normalized;
            lower = (rootP + d - knee).normalized;
        }

        /// <summary>solve one arm (side 0 = L, 1 = R) onto a model-space hand target and set the hand's rotation to follow the
        /// forearm (its rest offset) with an optional wrist twist in model space; the shoulder goes back to rest first</summary>
        public void SolveArm(int side, Vector3? target, Quaternion? wrist, Vector3? pole = null)
        {
            string S = side == 0 ? "L" : "R"; float sgn = side == 0 ? -1 : 1;     // Unity: the character's left is -X
            string ua = "upperarm_" + S, fa = "forearm_" + S, hn = "hand_" + S, sh = "shoulder_" + S;
            if (!rest.ContainsKey(ua) || !rest.ContainsKey(fa)) return;
            ResetBone(sh);
            var ru = rest[ua]; var rf = rest[fa];
            float l1 = Vector3.Distance(ru.p, rf.p);
            float l2 = rest.TryGetValue(hn, out var rh) ? Vector3.Distance(rf.p, rh.p) : l1;
            if (l2 < 1e-4f) l2 = l1;
            Vector3 u, l;
            if (target.HasValue) IK(ru.p, target.Value, l1, l2, pole ?? new Vector3(sgn * 0.7f, -1, -0.2f), out u, out l);
            else { u = l = new Vector3(sgn * 0.15f, -1, 0).normalized; }
            AimBone(ua, u); AimBone(fa, l);
            if (bones.TryGetValue(hn, out var hand) && rest.ContainsKey(hn))
            {
                // the hand keeps its rest offset from the forearm, twisted by the wrist (model space, premultiplied)
                var fq = Quaternion.Inverse(root.rotation) * bones[fa].rotation;
                var Qh = fq * Quaternion.Inverse(rf.q) * rest[hn].q;
                if (wrist.HasValue) Qh = wrist.Value * Qh;
                hand.rotation = root.rotation * Qh;
            }
        }
    }
}
