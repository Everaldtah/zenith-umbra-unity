// Yuzu's Hundred Suns, the Unity edition - a deliberate divergence from the TS (the user's rework of 2026-10-03; the web
// game keeps its 3 s arrow rain): five giant sword-like arrows of sunlight fall into a ring round the aim point and stand
// there burning, then shatter into a thousand small arrows that stream out and hunt every enemy within reach for 15 s - a
// swirling knot of arrows round each target, a quarter of them diving through it at any moment - and at the end the swarm
// gutters out arrow by arrow.
//
// Everything comes from the replicated "sunswarm" zone (its id, centre, born / until; Abilities.SUNS_* for the timeline),
// so a client draws what the host simulates. The small arrows fly on a cheap per-arrow spring toward a moving goal (no
// physics: ~1000 vector updates a frame). Draws: the small arrows (prop yuzu_ult_sunarrow, else a built arrow) and the
// giant ones (prop yuzu_ult_greatarrow, else a built sword-arrow), instanced, plus additive glow trails behind both.
// Sounds (evera-eb's ids; the old rain's while they don't exist): yuzuult_land / yuzuult_split / yuzuult_hit and the
// yuzuult_swarm loop. Maths in sim space (Sp); the matrices cross into Unity.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.Audio;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class SunSwarm
    {
        /// <summary>small arrows a swarm; their length and the giant ones' (m)</summary>
        const int N = 1000;
        const float SMALL = 0.7f, GIANT = 9f;
        /// <summary>a giant arrow's fall (s), how high and how far out it starts, how deep its tip goes in (m)</summary>
        const float FALL = 0.45f, FALL_UP = 28, FALL_OUT = 5, DEPTH = 1.0f;
        /// <summary>the swarm's flight: spring stiffness, damping, top speed (m/s)</summary>
        const float K = 30, DAMP = 8.5f, VMAX = 28;
        /// <summary>a strike cycle (s): each arrow dives through its target for the first DIVE of it</summary>
        const float CYCLE = 1.0f, DIVE = 0.25f;
        /// <summary>the swarm gutters out over the last FADE seconds</summary>
        const float FADE = 2;
        /// <summary>targets that flash on a tick, and that sound on one (2 a tick, 4 a second)</summary>
        const int FLASH_MAX = 12, HIT_SOUNDS = 2;

        static readonly Color ORANGE = Sp.Lin(Sp.Hex("#ffa94d")), GOLD = Sp.Lin(Sp.Hex("#ffd27a")), WHITE = Sp.Lin(Sp.Hex("#fff3d6")), EMBER = Sp.Lin(Sp.Hex("#ff7a1a"));
        static readonly Color ORANGE_FX = Sp.Hex("#ffa94d"), GOLD_FX = Sp.Hex("#ffd27a"), WHITE_FX = Sp.Hex("#fff3d6"), DUST_FX = Sp.Hex("#8a7a66");

        // per-arrow constants (the same on every machine): where it sits in its knot, how it turns, when it dives and dies
        static readonly float[] H1 = Hash(1), H2 = Hash(2), H3 = Hash(3), H4 = Hash(4);
        static float[] Hash(int salt)
        {
            var h = new float[N];
            for (int k = 0; k < N; k++) { float x = Mathf.Sin((k + 1) * 12.9898f + salt * 78.233f) * 43758.5453f; h[k] = x - Mathf.Floor(x); }
            return h;
        }

        sealed class Swarm
        {
            public int id; public Actor owner; public Vector3 c; public float born, until; public bool seen;
            public readonly Vector3[] land = new Vector3[Abilities.SUNS_N], dir = new Vector3[Abilities.SUNS_N];
            public int landed, tick; public bool split;
            public Vector3[] p, v, d; public bool[] dead;
        }

        /// <summary>one mesh drawn at up to 4 x 1023 matrices (InstancedBatch's limit is one call's 1023)</summary>
        sealed class Pool
        {
            readonly Mesh mesh; readonly Material mat; readonly Matrix4x4 pre;
            readonly List<InstancedBatch> b = new List<InstancedBatch>(); int cur;
            public Pool(Mesh mesh, Material mat, Matrix4x4 pre) { this.mesh = mesh; this.mat = mat; this.pre = pre; }
            public void Clear() { foreach (var x in b) x.Clear(); cur = 0; }
            public void Add(Matrix4x4 m, Color c)
            {
                if (cur < b.Count && b[cur].Count >= b[cur].Max) cur++;
                if (cur >= b.Count) { if (b.Count >= 4) return; b.Add(new InstancedBatch(mesh, mat, 1023) { pre = pre }); }
                b[cur].Add(m, c);
            }
            public void Draw() { foreach (var x in b) x.Draw(); }
        }

        readonly FxKit fx;
        readonly List<Pool> small = new List<Pool>(), giant = new List<Pool>();
        readonly Pool trails;
        readonly List<Material> mats = new List<Material>();
        readonly Dictionary<int, Swarm> swarms = new Dictionary<int, Swarm>();
        readonly List<int> gone = new List<int>();
        readonly List<Actor> targets = new List<Actor>();

        public SunSwarm(FxKit fx)
        {
            this.fx = fx;
            small.AddRange(Load("yuzu_ult_sunarrow", SmallArrow, 0.8f));
            giant.AddRange(Load("yuzu_ult_greatarrow", GreatArrow, 0.9f));
            var tm = AbilityKit.Additive();
            tm.SetFloat("_VertexColors", 1); tm.SetColor("_BaseColor", Color.white);
            mats.Add(tm);
            trails = new Pool(Trail(), tm, Matrix4x4.identity);
        }

        // ---------------------------------------------------------------------------------------------- meshes
        /// <summary>a prop's parts stood on its long axis: +Y, length 1, centred (fb's pipeline already stands them tip-up);
        /// a built stand-in when the prop isn't there</summary>
        List<Pool> Load(string id, System.Func<Mesh> standIn, float glow)
        {
            var list = new List<Pool>();
            var p = PropParts.Load(id);
            if (p == null)
            {
                var m = AbilityKit.Lit();
                m.SetVector("_EmissionColor", ORANGE * glow); m.SetFloat("_Roughness", 0.35f); m.SetFloat("_Metalness", 0.2f);
                mats.Add(m);
                list.Add(new Pool(standIn(), m, Matrix4x4.identity));
                return list;
            }
            var size = p.bounds.size; float h = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var rot = size.y >= size.x && size.y >= size.z ? Matrix4x4.identity
                : size.x >= size.z ? Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.forward)) : Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.right));
            p.Normalise(Matrix4x4.Scale(Vector3.one / Mathf.Max(1e-4f, h)) * rot * Matrix4x4.Translate(-p.bounds.center));
            // tip up: the Tripo exports don't agree on which end is up (the giant arrow came tip-down), so the narrower end
            // of the long axis - the point - is turned to +Y
            if (TipDown(p)) p.Normalise(Matrix4x4.Rotate(Quaternion.AngleAxis(180, Vector3.right)));
            foreach (var part in p.parts)
            {
                var m = AbilityKit.Lit();
                var map = AbilityKit.MapOf(part.source) ?? p.map;
                if (map != null) { m.SetTexture("_BaseMap", map); m.SetFloat("_EmitFromBase", 1); m.SetVector("_EmissionColor", Color.white * glow); }
                else m.SetVector("_EmissionColor", ORANGE * glow);
                m.SetFloat("_Roughness", 0.35f); m.SetFloat("_Metalness", 0.2f);
                mats.Add(m);
                list.Add(new Pool(part.mesh, m, part.pre));
            }
            return list;
        }

        /// <summary>true when the stood-up prop (long axis +Y, centred, length 1) is wider at its top 6% than at its bottom 6%:
        /// the point is at the bottom. Unreadable meshes (a player build without Read/Write) are left as they are.</summary>
        static bool TipDown(PropParts p)
        {
            float top = 0, bottom = 0;
            foreach (var part in p.parts)
            {
                if (part.mesh == null || !part.mesh.isReadable) continue;
                foreach (var v in part.mesh.vertices)
                {
                    var q = part.pre.MultiplyPoint3x4(v); float w = Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z));
                    if (q.y > 0.44f) top = Mathf.Max(top, w); else if (q.y < -0.44f) bottom = Mathf.Max(bottom, w);
                }
            }
            return top > bottom * 1.15f;
        }

        /// <summary>a small arrow, 1 long along +Y, centred: a flat leaf head, a thin shaft, two crossed fins (~50 tris)</summary>
        static Mesh SmallArrow()
        {
            var b = new AbilityKit.MeshBuilder();
            b.Append(AbilityKit.Cylinder(0.012f, 0.012f, 0.78f, 4), Matrix4x4.Translate(new Vector3(0, -0.06f, 0)));
            b.Append(AbilityKit.Cone(0.05f, 0.2f, 4), Matrix4x4.TRS(new Vector3(0, 0.4f, 0), Quaternion.identity, new Vector3(1, 1, 0.35f)));
            b.Append(AbilityKit.Box(0.11f, 0.16f, 0.004f), Matrix4x4.Translate(new Vector3(0, -0.38f, 0)));
            b.Append(AbilityKit.Box(0.004f, 0.16f, 0.11f), Matrix4x4.Translate(new Vector3(0, -0.38f, 0)));
            return b.Build("sun arrow (stand-in)");
        }

        /// <summary>a giant sword-arrow, 1 long along +Y, centred: a broad flat blade over a guard, a banded shaft, three fins</summary>
        static Mesh GreatArrow()
        {
            var b = new AbilityKit.MeshBuilder();
            var n = Matrix4x4.Scale(Vector3.one / 6f) * Matrix4x4.Translate(new Vector3(0, -3, 0));      // built 6 m tall from the tail
            b.Append(AbilityKit.Cylinder(0.15f, 0.15f, 4.4f, 6), n * Matrix4x4.Translate(new Vector3(0, 2.2f, 0)));
            b.Append(AbilityKit.Cone(0.7f, 1.5f, 4), n * Matrix4x4.TRS(new Vector3(0, 5.25f, 0), Quaternion.identity, new Vector3(1, 1, 0.22f)));
            b.Append(AbilityKit.Cone(0.7f, 0.35f, 4), n * Matrix4x4.TRS(new Vector3(0, 4.32f, 0), Quaternion.AngleAxis(180, Vector3.right), new Vector3(1, 1, 0.22f)));
            b.Append(AbilityKit.Box(1.6f, 0.18f, 0.36f), n * Matrix4x4.Translate(new Vector3(0, 4.1f, 0)));
            for (int i = 0; i < 3; i++) b.Append(AbilityKit.Cylinder(0.2f, 0.2f, 0.14f, 6), n * Matrix4x4.Translate(new Vector3(0, 1.6f + i * 0.9f, 0)));
            for (int i = 0; i < 3; i++)
            {
                var q = Quaternion.AngleAxis(i * 120, Vector3.up);
                b.Append(AbilityKit.Box(0.05f, 1.3f, 0.9f), n * Matrix4x4.TRS(q * new Vector3(0, 0, 0.5f) + new Vector3(0, 0.8f, 0), q, Vector3.one));
            }
            return b.Build("sun great arrow (stand-in)", true);
        }

        /// <summary>a glow trail: two crossed quads hanging from the origin down -Y to -1, 1 wide, bright at the top and fading to
        /// black (nothing, added) at the end</summary>
        static Mesh Trail()
        {
            var b = new AbilityKit.MeshBuilder();
            for (int q = 0; q < 2; q++)
            {
                var side = q == 0 ? new Vector3(0.5f, 0, 0) : new Vector3(0, 0, 0.5f);
                var nrm = q == 0 ? Vector3.forward : Vector3.right;
                int o = b.Count;
                b.Add(-side, nrm, new Vector2(0, 1), Color.white); b.Add(side, nrm, new Vector2(1, 1), Color.white);
                b.Add(-side + Vector3.down, nrm, new Vector2(0, 0), Color.black); b.Add(side + Vector3.down, nrm, new Vector2(1, 0), Color.black);
                b.Tri(o, o + 2, o + 1); b.Tri(o + 1, o + 2, o + 3);
            }
            return b.Build("sun trail");
        }

        // ---------------------------------------------------------------------------------------------- drawing
        static Vector3 U(Vector3 s) => Sp.U(s);
        static Quaternion Along(Vector3 d) => Quaternion.FromToRotation(Vector3.up, d);
        void Small(Vector3 p, Quaternion q, float s, Color c) { var m = Sp.TRS(p, q, Vector3.one * s); foreach (var b in small) b.Add(m, c); }
        void Giant(Vector3 p, Quaternion q, float s, Color c) { var m = Sp.TRS(p, q, Vector3.one * s); foreach (var b in giant) b.Add(m, c); }
        /// <summary>a trail hanging back from `from` against the direction of flight `d`</summary>
        void TrailAt(Vector3 from, Quaternion q, float width, float len, Color c) => trails.Add(Sp.TRS(from, q, new Vector3(width, len, width)), c);

        void Sound(string id, string fallback, Vector3 at, Swarm s, Actor me, float vol = 1)
        {
            try
            {
                string pick = AudioKit.Has(id) ? id : fallback;
                if (pick != null) AudioKit.Play(pick, U(at), vol, PlayOpts.Of(s.owner, RelOf(s.owner, me)));
            }
            catch (System.Exception) { /* no audio bank (tools, tests) */ }
        }
        static Rel RelOf(Actor a, Actor me) => a == null || me == null ? Rel.None : a == me ? Rel.Self : a.team == me.team ? Rel.Ally : Rel.Enemy;

        // ---------------------------------------------------------------------------------------------- the swarms
        Swarm Begin(World w, Zone z, float now)
        {
            var s = new Swarm { id = z.id, owner = z.owner, c = Sp.V(z.x, z.y, z.z), born = (float)z.born, until = (float)z.until };
            for (int i = 0; i < Abilities.SUNS_N; i++)
            {
                var l = Sp.V(Abilities.SunsLanding(w, z, i));
                var o = new Vector3(l.x - s.c.x, 0, l.z - s.c.z).normalized;
                s.land[i] = l;
                s.dir[i] = (l - (l + o * FALL_OUT + Vector3.up * FALL_UP)).normalized;      // tip-first, from high up and outside
            }
            // joined late (a client connecting, a view rebuilt): what already happened isn't replayed
            float age = now - s.born;
            while (s.landed < Abilities.SUNS_N && age >= LandAt(s.landed) + 0.1f) s.landed++;
            if (age < 0.3f)
            {
                fx.Ring(U(s.c), (float)Abilities.SUNS_RING, GOLD_FX, now, 1.4f);
                fx.Light(U(s.c + Vector3.up * 2), ORANGE_FX, 30, now, 0.3f);
            }
            return s;
        }

        static float LandAt(int i) => (float)(Abilities.SUNS_LAND_AT + i * Abilities.SUNS_LAND_STEP);

        public void Update(World w, Actor me, float now, float dt)
        {
            foreach (var p in small) p.Clear();
            foreach (var p in giant) p.Clear();
            trails.Clear();
            foreach (var s in swarms.Values) s.seen = false;
            foreach (var z in w.zones)
            {
                if (z.kind != "sunswarm") continue;
                if (!swarms.TryGetValue(z.id, out var s)) swarms[z.id] = s = Begin(w, z, now);
                s.seen = true; s.until = (float)z.until;
                Step(w, s, me, now, Mathf.Min(dt, 0.05f));
            }
            gone.Clear();
            foreach (var kv in swarms) if (!kv.Value.seen) gone.Add(kv.Key);
            foreach (var id in gone) swarms.Remove(id);
            foreach (var p in small) p.Draw();
            foreach (var p in giant) p.Draw();
            trails.Draw();
        }

        void Step(World w, Swarm s, Actor me, float now, float dt)
        {
            float age = now - s.born, split = (float)Abilities.SUNS_SPLIT;
            var up = Vector3.up;
            // ---- the giant arrows: falling tip-first, slamming in, quivering, then cracking with light before the split
            if (age < split)
            {
                float crack = Mathf.Clamp01((age - (split - 0.3f)) / 0.3f);
                for (int i = 0; i < Abilities.SUNS_N; i++)
                {
                    float L = LandAt(i);
                    if (age < L - FALL) continue;
                    var d = s.dir[i]; var tipEnd = s.land[i] + d * DEPTH;
                    Vector3 tip; var q = Along(d);
                    if (age < L)
                    {
                        float u = (age - (L - FALL)) / FALL;
                        tip = tipEnd - d * (Mathf.Sqrt(FALL_UP * FALL_UP + FALL_OUT * FALL_OUT) * (1 - u * u));
                        TrailAt(tip - d * GIANT, q, 1.6f, 12, EMBER * 1.2f);
                    }
                    else
                    {
                        tip = tipEnd;
                        float tau = age - L, quiver = 4 * Mathf.Exp(-7 * tau) * Mathf.Sin(tau * 45);
                        q = Quaternion.AngleAxis(quiver, Vector3.Cross(d, up).normalized) * q;
                    }
                    var axis = q * up;
                    float sc = 1 + 0.07f * crack * Mathf.Sin(age * 70 + i * 1.7f);
                    Giant(tip - axis * (GIANT * 0.5f * sc), q, GIANT * sc, Color.Lerp(Color.Lerp(EMBER, ORANGE, 0.6f), WHITE, crack));
                    if (crack > 0 && Random.value < 0.35f) fx.Emit(U(tip - axis * (GIANT * Random.value)), 1, WHITE_FX, FxKit.O(speed: 2.5f, life: 0.4f, size: 0.25f, spread: 0.6f));
                }
            }
            // ---- each landing: a flash, a ring, sparks and dust thrown up
            while (s.landed < Abilities.SUNS_N && age >= LandAt(s.landed))
            {
                var l = s.land[s.landed];
                fx.Ring(U(l + up * 0.1f), (float)Abilities.SUNS_LAND_R, ORANGE_FX, now, 0.5f);
                fx.Light(U(l + up), ORANGE_FX, 50, now, 0.15f);
                fx.Emit(U(l + up * 0.3f), 30, GOLD_FX, FxKit.O(speed: 7, life: 0.6f, size: 0.3f, grav: 10, spread: 1.5f, up: 3));
                fx.Emit(U(l + up * 0.2f), 14, DUST_FX, FxKit.O(speed: 3, life: 0.9f, size: 0.6f, grav: 1, spread: 2, up: 1.5f));
                Sound("yuzuult_land", "arrowhit", l, s, me);
                s.landed++;
            }
            // ---- the split: the giant arrows burst into the swarm, the reach flashes out on the ground
            if (!s.split && age >= split)
            {
                s.split = true;
                fx.Ring(U(s.c + up * 0.1f), (float)Abilities.SUNS_REACH, ORANGE_FX, now, 1.2f);
                fx.Light(U(s.c + up * 3), WHITE_FX, 80, now, 0.25f);
                s.p = new Vector3[N]; s.v = new Vector3[N]; s.d = new Vector3[N]; s.dead = new bool[N];
                for (int i = 0; i < Abilities.SUNS_N; i++)
                {
                    var tip = s.land[i] + s.dir[i] * DEPTH;
                    fx.Emit(U(tip - s.dir[i] * (GIANT * 0.5f)), 30, WHITE_FX, FxKit.O(speed: 6, life: 0.6f, size: 0.35f, spread: 2.5f));
                    fx.Emit(U(tip - s.dir[i] * (GIANT * 0.5f)), 20, ORANGE_FX, FxKit.O(speed: 9, life: 0.8f, size: 0.3f, spread: 3, up: 3));
                }
                for (int k = 0; k < N; k++)
                {
                    int g = k % Abilities.SUNS_N;
                    var tip = s.land[g] + s.dir[g] * DEPTH;
                    s.p[k] = tip - s.dir[g] * (GIANT * (0.1f + 0.85f * H1[k]));
                    var o = new Vector3(s.p[k].x - s.c.x, 0, s.p[k].z - s.c.z).normalized;
                    s.v[k] = o * (4 + 6 * H2[k]) + up * (6 + 8 * H3[k]) + new Vector3(H4[k] - 0.5f, H2[k] - 0.5f, H1[k] - 0.5f) * 4;
                    s.d[k] = s.v[k].normalized;
                }
                Sound("yuzuult_split", "arrowrain", s.c + up * 2, s, me);
            }
            if (s.p == null) return;
            // ---- the swarm
            targets.Clear();
            if (s.owner != null)
                foreach (var x in w.actors)
                    if (x.alive && x.team != s.owner.team && Mathf.Sqrt((float)((x.pos.x - s.c.x) * (x.pos.x - s.c.x) + (x.pos.z - s.c.z) * (x.pos.z - s.c.z))) < Abilities.SUNS_REACH)
                        targets.Add(x);
            float sw = age - split, left = s.until - now;
            int nT = targets.Count;
            for (int k = 0; k < N; k++)
            {
                if (s.dead[k]) continue;
                float life = Mathf.Clamp01((left - FADE * (1 - H4[k])) / 0.25f);
                if (life <= 0)
                {
                    s.dead[k] = true;
                    if (k % 20 == 0) fx.Emit(U(s.p[k]), 3, GOLD_FX, FxKit.O(speed: 1.5f, life: 0.5f, size: 0.2f, grav: 2, spread: 0.5f));
                    continue;
                }
                Vector3 goal;
                if (nT > 0)
                {
                    var t = targets[k % nT];
                    var tc = Sp.V(t.pos.x, t.pos.y + t.Height * 0.55, t.pos.z);
                    float th = k * 2.399963f + sw * (1.8f + 1.8f * H1[k]) * (k % 2 == 0 ? 1 : -1);
                    float ph = (H2[k] - 0.5f) * 1.5f + 0.4f * Mathf.Sin(sw * 1.3f + k);
                    float c = sw / CYCLE + H4[k]; c -= Mathf.Floor(c);
                    float dive = c < DIVE ? Mathf.Sin(c / DIVE * Mathf.PI) : 0;
                    float R = (1.1f + 1.4f * H3[k]) * (1 - 0.95f * dive);
                    goal = tc + new Vector3(Mathf.Cos(th) * Mathf.Cos(ph), Mathf.Sin(ph) * 0.8f, Mathf.Sin(th) * Mathf.Cos(ph)) * R;
                }
                else
                {
                    // nobody in reach: a slow halo of arrows over the ring, waiting
                    float th = k * 2.399963f + sw * 0.9f * (k % 2 == 0 ? 1 : -1), R = 4.5f + 3.5f * H3[k];
                    goal = s.c + new Vector3(Mathf.Cos(th) * R, 3.5f + 1.5f * Mathf.Sin(sw * 0.9f + k), Mathf.Sin(th) * R);
                }
                var v = s.v[k] + ((goal - s.p[k]) * K - s.v[k] * DAMP) * dt;
                float sp = v.magnitude;
                if (sp > VMAX) { v *= VMAX / sp; sp = VMAX; }
                s.v[k] = v; s.p[k] += v * dt;
                if (sp > 0.5f) s.d[k] = v / sp;
                var q = Along(s.d[k]);
                Small(s.p[k], q, SMALL * life, Color.Lerp(EMBER, ORANGE, H3[k]));
                TrailAt(s.p[k] - s.d[k] * (SMALL * 0.45f), q, 0.09f * life, Mathf.Clamp(sp * 0.05f, 0.15f, 1.6f), EMBER * (0.9f * life));
            }
            // ---- the hits, on the sim's beat (a tick at split + j * SUNS_TICK): a flash on each target, a few of them heard
            while (s.split && s.born + split + s.tick * (float)Abilities.SUNS_TICK <= now && s.born + split + s.tick * (float)Abilities.SUNS_TICK < s.until)
            {
                s.tick++;
                for (int i = 0; i < nT && i < FLASH_MAX; i++)
                {
                    var t = targets[i]; var tc = Sp.V(t.pos.x, t.pos.y + t.Height * 0.55, t.pos.z);
                    fx.Emit(U(tc), 6, GOLD_FX, FxKit.O(speed: 3, life: 0.35f, size: 0.2f, spread: 0.6f));
                    fx.Light(U(tc), ORANGE_FX, 10, now, 0.08f);
                    if (i < HIT_SOUNDS) Sound("yuzuult_hit", "arrowhit", tc, s, me, 0.7f);
                }
            }
            // ---- the swarm's hum, fading with it
            if (left > 0)
                try { AudioKit.Loop($"yzsw{s.owner?.id ?? s.id}", "yuzuult_swarm", U(s.c + up * 3), 0.9f * Mathf.Clamp01(left / FADE), PlayOpts.Of(s.owner, RelOf(s.owner, me))); }
                catch (System.Exception) { /* no audio bank */ }
        }

        public void Dispose() { foreach (var m in mats) Object.Destroy(m); }
    }
}
