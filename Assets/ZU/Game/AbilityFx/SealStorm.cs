// Kaien's Divine Seal Storm (TS render/SealStorm.ts, ported; after Byakuya's Senbonzakura Kageyoshi,
// docs/research/senbonzakura_study.md): a storm of golden paper seals for 15 s. The release - two rows of tall seals
// standing up out of the ground behind him, a beat, then they break into the swarm; the swarm moves as STREAMS, ribbons
// of tumbling cards that curve round him, never a cloud; a shield of seals closes round him in layers a metre out,
// thinning as it's spent and re-forming with a snap; each beat a ribbon flows to every enemy in reach and bursts on it in
// paper scraps, a gold flash and red sigils; the mending seals ring an ally in green-gold; at the end the seals lose
// their light and flutter down as paper.
//
// One instanced draw does every card (prop_kaien_seal, the film's talisman cut to 399 tris; a plain paper card when the
// prop is missing), refilled every frame from the storms' card lists; the per-instance colour carries the glow.
// The maths runs in sim space as in the TS (see Sp); the cards cross into Unity in their matrices.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class SealStorm
    {
        /// <summary>cards drawn at once (a shield is ~60, a ribbon ~22, the release rows 14 tall ones)</summary>
        const int MAX = 900;
        /// <summary>a card's height (m); the release rows' seals; the shield's radius round him</summary>
        const float CARD = 0.42f, TALL = 2.8f, SHIELD_R = 1.05f;
        /// <summary>the shield at full strength: this many seals</summary>
        const int SHIELD_N = 60;
        /// <summary>the release: seals a row, the rows' spacing behind him and to each side</summary>
        const int ROW_N = 7; const float ROW_STEP = 0.95f, ROW_SIDE = 1.7f, ROW_BACK = 1.6f;
        /// <summary>the ambient streams round him, cards a stream, spacing along it (in the loop's 0..1)</summary>
        const int STREAMS = 4, STREAM_N = 18; const float STREAM_GAP = 0.016f;
        /// <summary>a strike ribbon: cards, their spacing, seconds to reach the target, the ribbon's life</summary>
        const int STRIKE_N = 22; const float STRIKE_GAP = 0.035f, STRIKE_SECS = 0.3f, STRIKE_LIFE = 0.62f;
        /// <summary>the fall at the end: seconds a card flutters down for</summary>
        const float FALL_SECS = 1.7f;
        // (three keeps colours linear; so do the per-instance tints)
        static readonly Color GOLD = Sp.Lin(Sp.Hex("#ffe28a")), BRIGHT = Sp.Lin(Sp.Hex("#fff3c8")), DULL = Sp.Lin(Sp.Hex("#6a6250")), MEND = Sp.Lin(Sp.Hex("#9dffb0"));
        // (the colours handed to the particle kit are the TS hex values, as MatchFx passes them)
        static readonly Color GOLD_FX = Sp.Hex("#ffe28a"), BRIGHT_FX = Sp.Hex("#fff3c8"), MEND_FX = Sp.Hex("#9dffb0");

        sealed class Ribbon { public Vector3 from, to; public float born, side, up, seed; }
        sealed class Falling { public Vector3 p, v, spin; public Quaternion q; public float born, s; }
        sealed class Storm
        {
            public Actor actor; public float born, until, yaw; public bool live;
            public float shieldAt;                     // when the shield last (re)formed: it snaps in over 0.25 s
            public float[] phase;                      // each stream's own phase
        }
        sealed class Mend { public Actor actor; public float born, seed; }

        readonly FxKit fx;
        readonly List<InstancedBatch> cards = new List<InstancedBatch>();
        readonly Dictionary<int, Storm> storms = new Dictionary<int, Storm>();
        List<Ribbon> ribbons = new List<Ribbon>();
        List<Falling> falling = new List<Falling>();
        List<Mend> mends = new List<Mend>();
        readonly List<Material> mats = new List<Material>();

        public SealStorm(FxKit fx)
        {
            this.fx = fx;
            Load();
        }

        void Load()
        {
            var p = PropParts.Load("prop_kaien_seal");
            if (p == null)
            {
                // a plain paper card: a tall thin box, gold
                var m = AbilityKit.Lit();
                m.SetVector("_EmissionColor", Sp.Lin(Sp.Hex("#ffd27a")) * 0.4f); m.SetFloat("_Roughness", 0.6f); m.SetFloat("_Metalness", 0.1f);
                mats.Add(m);
                cards.Add(new InstancedBatch(AbilityKit.Box(0.62f, 1, 0.02f).Build("seal card (stand-in)"), m, MAX));
                return;
            }
            // the talisman stood in the card's frame: centred, its height 1 (CARD scales it), its long axis up +Y
            var size = p.bounds.size; float h = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var rot = size.x > size.y ? Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.forward)) : Matrix4x4.identity;
            p.Normalise(Matrix4x4.Scale(Vector3.one / Mathf.Max(1e-4f, h)) * rot * Matrix4x4.Translate(-p.bounds.center));
            foreach (var part in p.parts)
            {
                var m = AbilityKit.Lit();
                var map = AbilityKit.MapOf(part.source) ?? p.map;
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetVector("_EmissionColor", Sp.Lin(Sp.Hex("#ffd27a")) * 0.45f); m.SetFloat("_Metalness", 0); m.SetFloat("_Roughness", 0.6f);
                mats.Add(m);
                cards.Add(new InstancedBatch(part.mesh, m, MAX) { pre = part.pre });
            }
        }

        // ---------------------------------------------------------------------------------------------- events
        static Vector3 U(Vector3 s) => Sp.U(s);
        void Emit(Vector3 p, int n, Color c, FxKit.Opt o) => fx.Emit(U(p), n, c, o);
        void Ring(Vector3 p, float r, string color, float now, float dur = 0.5f) => fx.Ring(U(p), r, Sp.Hex(color), now, dur);
        void Light(Vector3 p, string color, float intensity, float now, float dur = 0.12f) => fx.Light(U(p), Sp.Hex(color), intensity, now, dur);

        /// <summary>the storm's events; true when this was one of them</summary>
        public bool OnEvent(FxEvent e, float now)
        {
            var pos = Sp.V(e.pos);
            switch (e.kind)
            {
                case "sealstorm":
                {
                    if (e.actor == null) return true;
                    var a = e.actor;
                    var ph = new float[STREAMS];
                    for (int k = 0; k < STREAMS; k++) ph[k] = (float)k / STREAMS + Random.value * 0.1f;
                    storms[a.id] = new Storm { actor = a, born = now, until = now + (float)(e.dur ?? 15), yaw = (float)a.yaw, live = false, shieldAt = now, phase = ph };
                    Ring(pos, (float)(e.r ?? 18), "#ffe28a", now, 1.1f); Ring(pos, 3, "#ffffff", now, 0.5f);
                    Light(new Vector3(pos.x, pos.y + 1, pos.z), "#ffe28a", 60, now, 0.5f);
                    Emit(Sp.V(a.pos.x, a.pos.y + 0.3, a.pos.z), 40, GOLD_FX, FxKit.O(speed: 3, life: 1.2f, size: 0.3f, up: 4, spread: 2.5f));
                    return true;
                }
                case "sealshield":
                {
                    if (e.actor != null && storms.TryGetValue(e.actor.id, out var s)) s.shieldAt = now;
                    Ring(pos, 1.6f, "#ffe28a", now, 0.35f); Emit(pos, 14, BRIGHT_FX, FxKit.O(speed: 2, life: 0.4f, size: 0.2f, spread: 1));
                    return true;
                }
                case "sealstrike":
                {
                    if (!e.to.HasValue) return true;
                    ribbons.Add(new Ribbon { from = pos, to = Sp.V(e.to.Value), born = now, side = Random.value < 0.5f ? -1 : 1, up = 0.6f + Random.value * 1.2f, seed = Random.value * 7 });
                    return true;
                }
                case "sealburst":
                {
                    // paper scraps, a small gold flash, red sigil lines
                    Emit(pos, 14, GOLD_FX, FxKit.O(speed: 3.5f, life: 0.5f, size: 0.16f, grav: 5, spread: 0.5f));
                    Emit(pos, 6, Sp.Hex("#ff3a2a"), FxKit.O(speed: 1.5f, life: 0.35f, size: 0.22f, spread: 0.4f));
                    Ring(pos, 0.9f, "#ff5a3a", now, 0.3f); Light(pos, "#ffd27a", 18, now, 0.1f);
                    return true;
                }
                case "sealmend":
                {
                    if (e.actor != null) mends.Add(new Mend { actor = e.actor, born = now, seed = Random.value * 7 });
                    Emit(pos, 8, MEND_FX, FxKit.O(speed: 1.2f, life: 0.7f, size: 0.2f, up: 2, spread: 0.5f));
                    Ring(pos, 1.3f, "#9dffb0", now, 0.4f); Light(pos, "#9dffb0", 14, now, 0.15f);
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------------------------------------- cards
        /// <summary>a card's rotation from its facing (normal) and the direction of its long axis (sim space)</summary>
        static Quaternion Orient(Vector3 normal, Vector3 up)
        {
            var z = normal.normalized; var y = up - z * Vector3.Dot(up, z);
            if (y.sqrMagnitude < 1e-6f) y = new Vector3(0, 1, 0) - z * z.y;
            return Sp.Basis(y.normalized, z);
        }

        /// <summary>a card: position, facing (its normal), up along its long axis, height, colour (linear)</summary>
        void Card(Vector3 p, Vector3 normal, Vector3 up, float h, Color c)
        {
            var m = Sp.TRS(p, Orient(normal, up), Vector3.one * h);
            foreach (var b in cards) b.Add(m, c);
        }
        void Card(Vector3 p, Quaternion q, float s, Color c)
        {
            var m = Sp.TRS(p, q, Vector3.one * s);
            foreach (var b in cards) b.Add(m, c);
        }

        /// <summary>the ambient stream round the caster: a looping path that swells and dips, streams offset in phase</summary>
        static Vector3 StreamAt(Storm s, float u, int k)
        {
            var a = s.actor; float th = u * Mathf.PI * 2 + k * 1.7f, R = 3.2f + 1.3f * Mathf.Sin(u * Mathf.PI * 4 + k), h = (float)a.Height * 0.55f + 1.1f * Mathf.Sin(u * Mathf.PI * 6 + k * 2.1f);
            return new Vector3((float)a.pos.x + Mathf.Cos(th) * R, (float)a.pos.y + h, (float)a.pos.z + Mathf.Sin(th) * R);
        }

        /// <summary>a strike ribbon's path: a bezier from him to the target, bulging out to one side and up</summary>
        static Vector3 StrikeAt(Ribbon r, float u)
        {
            float dx = r.to.x - r.from.x, dz = r.to.z - r.from.z, L = Mathf.Sqrt(dx * dx + dz * dz); if (L == 0) L = 1;
            float sx = -dz / L * r.side, sz = dx / L * r.side;
            float bulge = Mathf.Min(3, L * 0.35f);
            var P0 = r.from; var P3 = r.to;
            var P1 = new Vector3(P0.x + dx * 0.3f + sx * bulge, P0.y + (P3.y - P0.y) * 0.3f + r.up, P0.z + dz * 0.3f + sz * bulge);
            var P2 = new Vector3(P0.x + dx * 0.7f - sx * bulge * 0.5f, P0.y + (P3.y - P0.y) * 0.7f + r.up * 0.4f, P0.z + dz * 0.7f - sz * bulge * 0.5f);
            float t = u, mt = 1 - t, a0 = mt * mt * mt, a1 = 3 * mt * mt * t, a2 = 3 * mt * t * t, a3 = t * t * t;
            return P0 * a0 + P1 * a1 + P2 * a2 + P3 * a3;
        }

        /// <summary>the storm ends: what was in the air falls as paper</summary>
        void Drop(Vector3 p, Quaternion q, float s, float now)
        {
            falling.Add(new Falling
            {
                p = p, v = new Vector3((Random.value - 0.5f) * 1.2f, 0.3f + Random.value * 0.6f, (Random.value - 0.5f) * 1.2f), q = q,
                spin = new Vector3(Random.value * 4 - 2, Random.value * 6 - 3, Random.value * 4 - 2), born = now, s = s,
            });
        }

        readonly List<int> ended = new List<int>();

        public void Update(World w, float now, float dt)
        {
            foreach (var b in cards) b.Clear();
            var up = new Vector3(0, 1, 0);
            ended.Clear();
            foreach (var kv in storms)
            {
                var s = kv.Value; var a = s.actor; float age = now - s.born;
                bool has = a.alive && a.Has("sealstorm", now) && now < s.until + 0.2f;
                if (has) s.live = true;
                // (the TS preloader's sample fires the cast on a hero without the status: one frame of the release, then it's
                // dropped without a fall)
                bool on = has || (!s.live && age < 0.1f);
                if (!on)
                {
                    // over: the shield and the streams fall as dull paper
                    if (s.live)
                    {
                        for (int k = 0; k < SHIELD_N; k++) { ShieldCard(s, k, now, 1, out var p, out var nrm); Drop(p, Orient(nrm, up), CARD, now); }
                        for (int k = 0; k < STREAMS; k++)
                            for (int i = 0; i < STREAM_N; i++)
                            {
                                float u = Wrap(s.phase[k] + now * 0.28f - i * STREAM_GAP);
                                var p = StreamAt(s, u, k); var tng = (StreamAt(s, u + 0.01f, k) - p).normalized;
                                Drop(p, Orient(tng, up), CARD, now);
                            }
                    }
                    ended.Add(kv.Key); continue;
                }
                // the release: two rows of tall seals rising out of the ground behind him, holding a beat, dissolving top-down
                if (age < 1.5f)
                {
                    float fx_ = Mathf.Sin(s.yaw), fz = Mathf.Cos(s.yaw), rx = Mathf.Cos(s.yaw), rz = -Mathf.Sin(s.yaw);
                    for (int side = -1; side <= 1; side += 2)
                        for (int i = 0; i < ROW_N; i++)
                        {
                            float t0 = i * 0.045f + (side > 0 ? 0.02f : 0), rise = Mathf.Min(1, Mathf.Max(0, (age - t0) / 0.35f)), gone = Mathf.Min(1, Mathf.Max(0, (age - 0.95f - i * 0.03f) / 0.4f));
                            if (rise <= 0 || gone >= 1) continue;
                            float k = rise * rise * (3 - 2 * rise) * (1 - gone), h = TALL * k;
                            float d = ROW_BACK + i * ROW_STEP;
                            var p = new Vector3((float)a.pos.x - fx_ * d + rx * side * ROW_SIDE, (float)a.pos.y + h * 0.5f, (float)a.pos.z - fz * d + rz * side * ROW_SIDE);
                            var col = Color.Lerp(GOLD, BRIGHT, 0.5f * Mathf.Max(0, 1 - Mathf.Abs(age - 0.9f) / 0.3f));
                            Card(p, new Vector3(fx_, 0, fz), up, h, col);
                            if (gone > 0 && gone < 1 && Random.value < 0.5f) Emit(new Vector3(p.x, p.y + h * 0.5f * (1 - gone), p.z), 1, GOLD_FX, FxKit.O(speed: 2, life: 0.5f, size: 0.2f, spread: 0.8f));
                        }
                }
                // the shield: a sphere of seals, layered like scales, thinning with what's left of it
                Shield sh = null;
                foreach (var x in a.shields) if (x.kind == "sealshield" && x.amt > 0) { sh = x; break; }
                if (sh != null)
                {
                    float frac = Mathf.Min(1, Mathf.Max(0.25f, (float)sh.amt / 300)), snap = Mathf.Min(1, (now - s.shieldAt) / 0.25f);
                    int N = Mathf.RoundToInt(SHIELD_N * frac);
                    for (int k = 0; k < N; k++)
                    {
                        ShieldCard(s, k, now, snap, out var p, out var nrm);
                        Card(p, nrm, up, CARD * (0.6f + 0.4f * snap), Color.Lerp(GOLD, BRIGHT, 0.3f + 0.3f * Mathf.Sin(now * 5 + k)));
                    }
                }
                // the streams: ribbons of tumbling cards curving round him, the head of each brighter
                float flow = age < 1.2f ? Mathf.Min(1, Mathf.Max(0, (age - 0.8f) / 0.4f)) : 1;
                if (flow > 0)
                    for (int k = 0; k < STREAMS; k++)
                        for (int i = 0; i < STREAM_N; i++)
                        {
                            float u = Wrap(s.phase[k] + now * 0.28f - i * STREAM_GAP);
                            var p = StreamAt(s, u, k); var tng = (StreamAt(s, u + 0.01f, k) - p).normalized;     // the ribbon's tangent
                            float spin = now * 7 + i * 0.6f + k;
                            var nrm = Quaternion.AngleAxis(spin * Mathf.Rad2Deg, tng) * Vector3.Cross(new Vector3(0, 1, 0), tng).normalized;
                            Card(p, nrm, tng, CARD * flow * (1 - i / (STREAM_N * 1.6f)), Color.Lerp(GOLD, BRIGHT, Mathf.Max(0, 1 - i / 5f) * 0.8f));
                        }
            }
            foreach (var id in ended) storms.Remove(id);
            // the strike ribbons: cards flowing along the bezier, arriving over STRIKE_SECS, gone on arrival
            ribbons = ribbons.FindAll(r =>
            {
                float age = now - r.born;
                if (age > STRIKE_LIFE) return false;
                float head = age / STRIKE_SECS;
                for (int i = 0; i < STRIKE_N; i++)
                {
                    float u = head - i * STRIKE_GAP;
                    if (u <= 0 || u >= 1) continue;
                    var p = StrikeAt(r, u); var tng = (StrikeAt(r, Mathf.Min(1, u + 0.02f)) - p).normalized;
                    var nrm = Quaternion.AngleAxis((now * 9 + i * 0.7f + r.seed) * Mathf.Rad2Deg, tng) * Vector3.Cross(new Vector3(0, 1, 0), tng).normalized;
                    Card(p, nrm, tng, CARD * (0.75f + 0.25f * Mathf.Min(1, u * 4)), Color.Lerp(GOLD, BRIGHT, Mathf.Max(0, 1 - i / 4f) * 0.9f));
                }
                return true;
            });
            // the mending seals: a ring of green-gold cards rising round the ally
            mends = mends.FindAll(m =>
            {
                float age = now - m.born; var a = m.actor;
                if (age > 0.7f || !a.alive) return false;
                float k = age / 0.7f;
                for (int i = 0; i < 8; i++)
                {
                    float th = i / 8f * Mathf.PI * 2 + now * 2.5f + m.seed, R = (float)a.Radius + 0.35f;
                    var p = new Vector3((float)a.pos.x + Mathf.Cos(th) * R, (float)a.pos.y + (float)a.Height * (0.25f + 0.55f * k), (float)a.pos.z + Mathf.Sin(th) * R);
                    Card(p, new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)), up, CARD * 0.7f * Mathf.Sin(k * Mathf.PI), Color.Lerp(MEND, GOLD, 0.35f));
                }
                return true;
            });
            // paper falling at the end: drifting down, swaying, losing its light
            falling = falling.FindAll(f =>
            {
                float age = now - f.born;
                if (age > FALL_SECS) return false;
                f.v.y -= 2.6f * dt; f.v *= Mathf.Pow(0.35f, dt);
                f.p += f.v * dt; f.p.x += Mathf.Sin(age * 5 + f.s) * 0.9f * dt;
                f.q *= Sp.EulerXYZ(f.spin.x * dt, f.spin.y * dt, f.spin.z * dt);
                float k = age / FALL_SECS;
                Card(f.p, f.q, f.s * (1 - k * k), Color.Lerp(GOLD, DULL, Mathf.Min(1, k * 1.6f)));
                return true;
            });
            foreach (var b in cards) b.Draw();
        }

        /// <summary>(x % 1 + 1) % 1</summary>
        static float Wrap(float x) => ((x % 1) + 1) % 1;

        /// <summary>the shield's k-th seal: on a fibonacci sphere round him, the sphere turning slowly and breathing; the
        /// position and the outward normal</summary>
        static void ShieldCard(Storm s, int k, float now, float snap, out Vector3 p, out Vector3 nrm)
        {
            var a = s.actor; int N = SHIELD_N; float y = 1 - (k + 0.5f) / N * 2, r = Mathf.Sqrt(1 - y * y), th = k * 2.399963f + now * 0.9f + (k % 2 == 1 ? 0.4f : 0);
            float R = SHIELD_R * (0.4f + 0.6f * snap) + 0.06f * Mathf.Sin(now * 6 + k);
            nrm = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
            p = new Vector3((float)a.pos.x + nrm.x * R, (float)a.pos.y + (float)a.Height * 0.5f + nrm.y * R * 1.15f, (float)a.pos.z + nrm.z * R);
        }

        public void Dispose() { foreach (var m in mats) Object.Destroy(m); }
    }
}
