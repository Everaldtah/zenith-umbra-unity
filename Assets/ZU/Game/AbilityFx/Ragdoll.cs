// Ragdoll deaths (TS render/Ragdoll.ts, ported), the way Overwatch 2 throws its heroes: the moment a hero dies the
// animated pose becomes a set of Verlet particles on the joints, launched with the hero's own momentum plus the killing
// blow (a bigger hit flings harder, the upper body takes more of it so the body tumbles), then left to gravity, the floor
// and the map's walls. The skeleton is driven back from the particles every frame, so the skinned mesh, its held props
// and the cloth follow the fall.
//
// Particles sit on bone heads (plus a head top, hand tips and toes). Bone lengths are rigid distance constraints; extra
// struts keep the pelvis / chest box from shearing; a few minimum distances stand in for joint limits (elbows, knees and
// the neck can't fold through themselves). Fixed 120 Hz sub-steps; floor contact bleeds speed through friction.
//
// Unity: the solver works on the bone Transforms in Unity space (gravity and contact don't care about the mirror); only
// the map queries cross into sim space (Conv). The Animator keeps running on a dead hero, so Pose re-applies the body
// every frame - asleep or not - after the caller has restored the bones it doesn't drive (CharacterExtras).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class Ragdoll
    {
        sealed class P { public Vector3 x, prev; public float r; }
        enum Kind { Rigid, Min, Band }
        struct Link { public int a, b; public float d; public Kind kind; }
        sealed class Aim { public Transform bone; public int a, b; public Vector3 d0; public Quaternion q0; }
        sealed class Frame { public Transform bone; public int o, up, l, r; public Matrix4x4 B0; public Quaternion q0; public bool pos; }

        const float G = 20;                  // a little floatier than the game's 24: the fall reads, as Overwatch's does
        const float STEP = 1f / 120;
        const float TONE = 0.45f;            // seconds of fading muscle tone after death
        const int MAX_SUB = 3;               // physics sub-steps a frame at most

        readonly List<P> p = new List<P>();
        readonly Dictionary<string, int> idx = new Dictionary<string, int>();
        readonly List<Link> links = new List<Link>();
        float acc, age;
        readonly List<Aim> aims = new List<Aim>();
        readonly List<Frame> frames = new List<Frame>();
        readonly List<Transform> order;
        // muscle tone: each particle's place in the pelvis frame at the moment of death - pulled back toward it with a pull
        // that fades out over TONE seconds, so the body staggers with the blow and then crumples (not an instant plank)
        readonly Vector3[] local;
        readonly Matrix4x4 tone0;
        readonly float H, floorY;
        readonly ILevel level;
        readonly Dictionary<string, Transform> bones;

        public static readonly string[] REQUIRED = { "hips", "chest", "head", "upperarm_L", "forearm_L", "hand_L", "upperarm_R", "forearm_R", "hand_R", "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R" };

        /// <summary>
        /// bones: the rig's bones by the TS names (hips, chest, neck, head, upperarm_L, forearm_L, hand_L, thigh_L, shin_L,
        /// foot_L, toe_L and the _R side); H: the hero's height (m); vel: its velocity at death and fling: the killing blow's
        /// push (Unity world m/s); level: the map (null: a floor at `floorY`); fwd: the body's facing (Unity), for a foot
        /// with no toe bone
        /// </summary>
        public Ragdoll(Dictionary<string, Transform> bones, float H, Vector3 vel, Vector3 fling, ILevel level, float floorY, Vector3 fwd)
        {
            this.bones = bones; this.H = H; this.level = level; this.floorY = floorY;
            Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
            Vector3 wp(string n) => B(n).position;
            void add(string name, Vector3 pos, float r) { idx[name] = p.Count; p.Add(new P { x = pos, prev = pos, r = r * H }); }
            Vector3 hips = wp("hips"), chest = wp("chest"), neck = wp(B("neck") != null ? "neck" : "chest"), head = wp("head");
            var up = (chest - hips).normalized;
            add("hips", hips, 0.07f); add("chest", chest, 0.075f); add("neck", neck, 0.05f); add("head", head, 0.06f);
            add("top", head + up * (0.12f * H), 0.06f);
            foreach (var S in new[] { "L", "R" })
            {
                Vector3 sh = wp($"upperarm_{S}"), el = wp($"forearm_{S}"), wr = wp($"hand_{S}");
                add($"sh{S}", sh, 0.045f); add($"el{S}", el, 0.035f); add($"wr{S}", wr, 0.03f);
                add($"tip{S}", wr + (wr - el).normalized * (0.07f * H), 0.025f);
                Vector3 th = wp($"thigh_{S}"), kn = wp($"shin_{S}"), an = wp($"foot_{S}");
                add($"th{S}", th, 0.055f); add($"kn{S}", kn, 0.045f); add($"an{S}", an, 0.035f);
                // the toe: forward along the ground from the ankle (the rig's toe bone, else the foot's first child)
                var foot = B($"foot_{S}"); var toe = B($"toe_{S}") ?? (foot.childCount > 0 ? foot.GetChild(0) : null);
                var f = toe != null ? toe.position - an : Vector3.zero;
                if (f.sqrMagnitude < 1e-6f) { f = fwd; f.y = 0; }
                add($"toe{S}", an + f.normalized * (0.1f * H), 0.03f);
            }
            int I(string n) => idx[n];
            void link(string a, string b, Kind kind = Kind.Rigid, float k = 1) => links.Add(new Link { a = I(a), b = I(b), d = Vector3.Distance(p[I(a)].x, p[I(b)].x) * k, kind = kind });
            // the spine and head
            link("hips", "chest"); link("chest", "neck"); link("neck", "head"); link("head", "top");
            link("chest", "head", Kind.Band); link("neck", "top", Kind.Band);
            // chest box and pelvis box, tied together
            link("chest", "shL"); link("chest", "shR"); link("shL", "shR"); link("neck", "shL"); link("neck", "shR");
            link("shL", "hips"); link("shR", "hips");
            link("hips", "thL"); link("hips", "thR"); link("thL", "thR"); link("thL", "chest"); link("thR", "chest");
            link("shL", "thL", Kind.Band); link("shR", "thR", Kind.Band);
            foreach (var S in new[] { "L", "R" })
            {
                link($"sh{S}", $"el{S}"); link($"el{S}", $"wr{S}"); link($"wr{S}", $"tip{S}");
                link($"th{S}", $"kn{S}"); link($"kn{S}", $"an{S}"); link($"an{S}", $"toe{S}"); link($"kn{S}", $"toe{S}");
                // joint limits: elbows and knees never fold shut, feet stay off the thighs
                float ua = Vector3.Distance(p[I($"sh{S}")].x, p[I($"el{S}")].x), fa = Vector3.Distance(p[I($"el{S}")].x, p[I($"wr{S}")].x);
                links.Add(new Link { a = I($"sh{S}"), b = I($"wr{S}"), d = (ua + fa) * 0.45f, kind = Kind.Min });
                float tl = Vector3.Distance(p[I($"th{S}")].x, p[I($"kn{S}")].x), sl = Vector3.Distance(p[I($"kn{S}")].x, p[I($"an{S}")].x);
                links.Add(new Link { a = I($"th{S}"), b = I($"an{S}"), d = (tl + sl) * 0.5f, kind = Kind.Min });
                links.Add(new Link { a = I("chest"), b = I($"an{S}"), d = (tl + sl) * 0.6f, kind = Kind.Min });
            }
            // launch: the hero's own momentum, plus the blow - upper body more than the legs, so the body tips and tumbles
            var share = new Dictionary<string, float> { ["hips"] = 0.75f, ["chest"] = 1.1f, ["neck"] = 1.2f, ["head"] = 1.3f, ["top"] = 1.35f };
            foreach (var kv in idx)
            {
                string n = kv.Key;
                float k = share.TryGetValue(n, out var s) ? s : n.StartsWith("sh") || n.StartsWith("el") || n.StartsWith("wr") || n.StartsWith("tip") ? 1.05f : n.StartsWith("th") ? 0.7f : 0.55f;
                var v = vel + fling * k;
                p[kv.Value].prev = p[kv.Value].x - v * STEP;
            }
            // driven bones
            void aim(string bone, string a, string b)
            {
                var o = B(bone); if (o == null) return;
                var A = p[I(a)].x; var Bp = p[I(b)].x;
                aims.Add(new Aim { bone = o, a = I(a), b = I(b), d0 = (Bp - A).normalized, q0 = o.rotation });
            }
            void frame(string bone, string o, string upTo, string l, string r, bool pos)
            {
                var obj = B(bone); if (obj == null) return;
                frames.Add(new Frame { bone = obj, o = I(o), up = I(upTo), l = I(l), r = I(r), B0 = Basis(I(o), I(upTo), I(l), I(r)), q0 = obj.rotation, pos = pos });
            }
            frame("hips", "hips", "chest", "thL", "thR", true);
            frame("chest", "chest", "neck", "shL", "shR", false);
            if (B("neck") != null) aim("neck", "neck", "head");
            aim("head", "head", "top");
            foreach (var S in new[] { "L", "R" })
            {
                aim($"upperarm_{S}", $"sh{S}", $"el{S}"); aim($"forearm_{S}", $"el{S}", $"wr{S}"); aim($"hand_{S}", $"wr{S}", $"tip{S}");
                aim($"thigh_{S}", $"th{S}", $"kn{S}"); aim($"shin_{S}", $"kn{S}", $"an{S}"); aim($"foot_{S}", $"an{S}", $"toe{S}");
            }
            // the pose in the pelvis frame (hips at the origin; up to the chest, across the hip joints)
            tone0 = Basis(I("hips"), I("chest"), I("thL"), I("thR"));
            var inv = tone0.transpose; var o0 = p[I("hips")].x;
            local = p.Select(q => inv.MultiplyVector(q.x - o0)).ToArray();
            // parents before children
            int depth(Transform o) { int d = 0; for (var q = o.parent; q != null; q = q.parent) d++; return d; }
            order = frames.Select(f => f.bone).Concat(aims.Select(a => a.bone)).OrderBy(depth).ToList();
            floor = new float[p.Count];
        }

        /// <summary>the bones this ragdoll poses (the caller restores the others to their death pose each frame)</summary>
        public IEnumerable<Transform> Driven => order;

        Matrix4x4 Basis(int o, int u, int l, int r)
        {
            var up = (p[u].x - p[o].x).normalized;
            var side = p[l].x - p[r].x; side = (side - up * Vector3.Dot(side, up)).normalized;
            var fwd = Vector3.Cross(side, up).normalized;
            var m = Matrix4x4.identity;
            m.SetColumn(0, side); m.SetColumn(1, up); m.SetColumn(2, fwd);
            return m;
        }

        /// <summary>true once the body has come to rest: no more simulation (it just lies there, for free)</summary>
        public bool asleep;
        float still;
        readonly float[] floor;

        /// <summary>advance the simulation and pose the skeleton (posed every frame, asleep or not: the Animator still runs)</summary>
        public void Step(float dt)
        {
            if (!asleep) Simulate(dt);
            Pose();
        }

        void Simulate(float dt)
        {
            age += dt;
            // at most MAX_SUB sub-steps a frame: a slow frame plays the fall in slow motion instead of costing more physics,
            // which would make the next frame slower still
            acc = Mathf.Min(acc + Mathf.Min(dt, 0.1f), STEP * MAX_SUB);
            var L = level;
            // the map is queried once a frame per joint, not inside the solver: every query walks every box of the map
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[i];
                double g = L != null ? L.GroundAt(-q.x.x, q.x.z, q.x.y + H * 0.3, 0) : floorY;
                floor[i] = !double.IsInfinity(g) && !double.IsNaN(g) ? (float)g : floorY - 50;
            }
            float moved = 0;
            while (acc >= STEP)
            {
                acc -= STEP;
                foreach (var q in p)
                {
                    var v = (q.x - q.prev) * 0.996f;
                    q.prev = q.x;
                    q.x += v; q.x.y -= G * STEP * STEP;
                }
                // fading muscle tone (see `local`)
                float tone = Mathf.Max(0, 1 - age / TONE);
                if (tone > 0)
                {
                    var B = Basis(idx["hips"], idx["chest"], idx["thL"], idx["thR"]); var o = p[idx["hips"]].x;
                    float k = 0.22f * tone * tone;
                    for (int i = 0; i < p.Count; i++) { var t = B.MultiplyVector(local[i]) + o; p[i].x = Vector3.Lerp(p[i].x, t, k); }
                }
                for (int it = 0; it < 6; it++)
                {
                    foreach (var c in links)
                    {
                        var A = p[c.a]; var Bq = p[c.b]; var d = Bq.x - A.x; float len = d.magnitude;
                        if (len < 1e-6f) continue;
                        float want = c.d;
                        if (c.kind == Kind.Min) { if (len >= c.d) continue; }
                        else if (c.kind == Kind.Band) { want = Mathf.Min(Mathf.Max(len, c.d * 0.85f), c.d * 1.15f); if (want == len) continue; }
                        float k = (len - want) / len * 0.5f;
                        A.x += d * k; Bq.x -= d * k;
                    }
                    for (int i = 0; i < p.Count; i++) Ground(p[i], i);
                }
                foreach (var q in p) moved = Mathf.Max(moved, (q.x - q.prev).sqrMagnitude);
            }
            // walls: once a frame per joint
            if (L != null)
                foreach (var q in p)
                {
                    var w = new V3(-q.x.x, q.x.y - q.r, q.x.z);
                    if (L.Collide(ref w, q.r, q.r * 2))
                    {
                        q.x.x = (float)-w.x; q.x.z = (float)w.z;
                        q.prev.x += (q.x.x - q.prev.x) * 0.5f; q.prev.z += (q.x.z - q.prev.z) * 0.5f;
                    }
                }
            // at rest (every joint moving under ~0.25 m/s for 0.4 s, after the first second): sleep
            if (age > 1 && moved < (0.25f * STEP) * (0.25f * STEP)) { still += dt; if (still > 0.4f) asleep = true; } else still = 0;
        }

        /// <summary>the body's first hard landings (pelvis, chest): Unity world position and impact speed (m/s), for the thud</summary>
        public Action<Vector3, float> onImpact;
        readonly HashSet<int> landed = new HashSet<int>();

        void Ground(P q, int i)
        {
            float fl = floor[i];
            if (q.x.y < fl + q.r)
            {
                float vy = (q.prev.y - q.x.y) / STEP;
                if (!landed.Contains(i) && vy > 2.5f && (i == idx["hips"] || i == idx["chest"])) { landed.Add(i); onImpact?.Invoke(q.x, vy); }
                q.x.y = fl + q.r;
                // floor friction: the body slides a little, then stops
                q.prev.x += (q.x.x - q.prev.x) * 0.18f; q.prev.z += (q.x.z - q.prev.z) * 0.18f;
            }
        }

        void Pose()
        {
            foreach (var bone in order)
            {
                var f = frames.Find(x => x.bone == bone);
                if (f != null)
                {
                    if (f.pos) bone.position = p[f.o].x;
                    var B = Basis(f.o, f.up, f.l, f.r);
                    var dq = (B * f.B0.transpose).rotation;
                    bone.rotation = dq * f.q0;
                    continue;
                }
                var a = aims.Find(x => x.bone == bone);
                var d = p[a.b].x - p[a.a].x;
                if (d.sqrMagnitude < 1e-10f) continue;
                bone.rotation = Quaternion.FromToRotation(a.d0, d.normalized) * a.q0;
            }
        }

        /// <summary>where the body is now (the pelvis), for the camera / effects</summary>
        public Vector3 Center => p[idx["hips"]].x;
    }
}
