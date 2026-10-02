// Everything in flight and the things the fallen leave behind (port of the TS render/Fx.ts syncProjectiles, syncSouls and
// syncFangs, same order, same numbers):
// - projectiles: a hot core in a soft additive glow sized by kind (a fist, a hex bomb, a heal, a splash round, a bullet),
//   stretched into a streak for the sun / reveal / shadow / hex / bolt rounds; the sonic round with its pulsing ring; a
//   thrown prop (Hayate's shuriken) flies itself, flat along the flight and spinning, and a ricochet star swims in koi
//   water (two counter-turning rings, spray off the rim, a water streak behind it); Tomoe's Crescent Fang tumbles end over
//   end; chains and Gantetsu's fist drag a line back to the thrower; every round sheds sparks.
// - souls: a golden orb and a faint pillar where a teammate fell, while a living Mirei could still call them back.
// - the Crescent Fang out of her hand: buried in a wall, riding an enemy (blade in, grip out) or whirling home.
// Frames: the TS group's +Z is the flight direction; Unity's is too (Conv mirrors X), so a TS rotation about X keeps its
// sign and one about Y or Z flips.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.Game
{
    public class ProjectileViews
    {
        /// <summary>TS FXCOL: a projectile's colour by its fx kind (white when unlisted)</summary>
        static readonly Dictionary<string, string> FXCOL = new Dictionary<string, string>
        {
            { "sun", "#ffd76a" }, { "star", "#bfe8ff" }, { "talisman", "#ffe28a" }, { "bolt", "#8ad8ff" }, { "void", "#ff2244" }, { "blood", "#ff2d55" }, { "hex", "#c77dff" },
            { "shadow", "#9d7bff" }, { "flame", "#ff6a2a" }, { "fist", "#ffd76a" }, { "reveal", "#fff2b0" }, { "hexbomb", "#c77dff" }, { "chain", "#ff6a2a" }, { "sonic", "#7dfcff" },
            { "tide", "#5ff2e0" }, { "crescent", "#5ff2e0" },
        };
        /// <summary>TS HeldProps THROWN_ALT: the prop flown until its second generation is in the build</summary>
        static readonly Dictionary<string, string> THROWN_ALT = new Dictionary<string, string> { { "prop_hayate_shuriken_v2", "prop_hayate_shuriken" } };
        const string REBIRTH_COLOR = "#ffe9a8";
        const string FANG_GLOW = "#5ff2e0";
        static readonly string[] STREAK = { "sun", "reveal", "shadow", "hex", "bolt" };

        static Color Col(string fx) => Conv.Hex(fx != null && FXCOL.TryGetValue(fx, out var h) ? h : null, Color.white);

        readonly Transform root;
        readonly Dictionary<int, Transform> projMeshes = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> beamMeshes = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> fangMeshes = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> soulMeshes = new Dictionary<int, Transform>();
        readonly Dictionary<Color, Material> cores = new Dictionary<Color, Material>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> gone = new List<int>();
        Material additive;
        static Mesh sphere, torusA, torusB;

        public ProjectileViews(Transform parent) { root = new GameObject("Projectiles").transform; root.SetParent(parent, false); }

        // ------------------------------------------------------------------------------------------------ building blocks
        /// <summary>the TS sphere (radius 1, 12 x 8) with white vertex colours, which ZU/FxAdditive multiplies in</summary>
        internal static Mesh Sphere()
        {
            if (sphere != null) return sphere;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
            const int W = 12, Hh = 8;
            for (int y = 0; y <= Hh; y++)
                for (int x = 0; x <= W; x++)
                {
                    float th = Mathf.PI * y / Hh, ph = 2 * Mathf.PI * x / W;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d); n.Add(d); c.Add(Color.white);
                    if (x < W && y < Hh) { int i = y * (W + 1) + x; tri.AddRange(new[] { i, i + W + 1, i + 1, i + 1, i + W + 1, i + W + 2 }); }
                }
            sphere = new Mesh { name = "fx sphere" };
            sphere.SetVertices(v); sphere.SetNormals(n); sphere.SetColors(c); sphere.SetTriangles(tri, 0); sphere.RecalculateBounds();
            return sphere;
        }

        /// <summary>a torus of radius R, tube r, lying flat in the XZ plane (the TS TorusGeometry turned by PI/2 about X)</summary>
        static Mesh Torus(float R, float r, int radial, int tubular)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
            for (int j = 0; j <= radial; j++)
                for (int i = 0; i <= tubular; i++)
                {
                    float u = i * 2 * Mathf.PI / tubular, w = j * 2 * Mathf.PI / radial;
                    var ring = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u));
                    var d = ring * Mathf.Cos(w) + Vector3.up * Mathf.Sin(w);
                    v.Add(ring * R + d * r); n.Add(d); c.Add(Color.white);
                    if (i < tubular && j < radial)
                    {
                        int a = j * (tubular + 1) + i, b = a + tubular + 1;
                        tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                }
            var m = new Mesh { name = "fx torus" };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>TS MeshBasicMaterial: an opaque core that ignores the light (Lit + emission, the way the build keeps it)</summary>
        Material Core(Color c)
        {
            if (cores.TryGetValue(c, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "fx core" };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c);
            cores[c] = m;
            return m;
        }

        Transform Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>an additive part tinted c at opacity a (TS: AdditiveBlending, opacity a; HDR x1.4 like FxKit so the bloom takes it)</summary>
        Transform Glow(string name, Transform parent, Mesh mesh, Color c, float a)
        {
            var t = Part(name, parent, mesh, additive);
            Tint(t, c, a);
            return t;
        }
        void Tint(Transform t, Color c, float a)
        {
            var mr = t.GetComponent<MeshRenderer>(); var k = c * 1.4f; k.a = a;
            mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", k); mr.SetPropertyBlock(mpb);
        }

        Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(root, false); return g; }
        static void Drop(Transform t) { if (t != null) Object.Destroy(t.gameObject); }

        /// <summary>TS thrownProp: the fitted Tripo prop lying flat (normal +Y), SHURIKEN_R x the thrower's height in radius;
        /// the procedural star while the build has neither generation</summary>
        static Transform ThrownProp(string id, float L)
        {
            THROWN_ALT.TryGetValue(id, out var alt);
            var m = HeldRig.Load(id, alt);
            if (m != null) m.transform.localScale = Vector3.one * 2 * Held.SHURIKEN_R * L;     // the flat fit is unit diameter
            else m = ProcProps.Shuriken(L, null);                                               // (already SHURIKEN_R x L)
            foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            return m.transform;
        }

        /// <summary>TS buildFang(1.25): the Crescent Fang with its grip at the origin and the blade along +Z, 0.26 x 1.25 m long
        /// (the Tripo knife, its centre 8.75% of the height forward so the grip is the back third)</summary>
        static Transform Fang()
        {
            const float L = 1.25f, len = 0.26f * L;
            var g = new GameObject("fang").transform;
            var m = HeldRig.Load("prop_tomoe_blade", null);
            if (m != null) { m.transform.SetParent(g, false); m.transform.localScale = Vector3.one * len; m.transform.localPosition = new Vector3(0, 0, 0.0875f * L); }
            else ProcProps.Blade(len, L, new HeldItem("prop_tomoe_blade", HeldKind.Blade, 0.26f, "#f3f1ec", FANG_GLOW)).transform.SetParent(g, false);
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        // ------------------------------------------------------------------------------------------------ per frame
        public void Sync(World w, float alpha)
        {
            var fx = MatchFx.Current;
            if (fx == null) return;
            additive ??= fx.Additive;
            float now = Time.time;
            SyncProjectiles(w, fx, now, alpha);
            SyncSouls(w, fx, now);
            SyncFangs(w, fx, now);
        }

        void SyncProjectiles(World w, FxKit fx, float now, float alpha)
        {
            seen.Clear();
            foreach (var p in w.projs)
            {
                seen.Add(p.id);
                projMeshes.TryGetValue(p.id, out var m);
                var col = Col(p.fx);
                if (m == null && p.fx == "crescent")
                {
                    m = Group("proj crescent");
                    var blade = Fang(); blade.name = "spin"; blade.SetParent(m, false);
                    projMeshes[p.id] = m;
                }
                // a thrown prop (Hayate's shuriken): the prop itself flies, flat along the flight, spinning about its own axis
                if (m == null && p.mesh != null)
                {
                    m = Group("proj " + p.mesh);
                    var spin = new GameObject("spin").transform; spin.SetParent(m, false);
                    ThrownProp(p.mesh, (float)p.owner.Height).SetParent(spin, false);
                    // a ricochet shuriken swims in koi water: the rings turn against the star, swell while it hunts
                    if (p.bounce.HasValue)
                    {
                        var water = new GameObject("water").transform; water.SetParent(m, false);
                        var wc = Conv.Hex(p.fx != null && FXCOL.TryGetValue(p.fx, out var h) ? h : "#5ff2e0");
                        torusA ??= Torus(0.2f, 0.028f, 6, 28); torusB ??= Torus(0.27f, 0.016f, 6, 28);
                        Glow("ring a", water, torusA, wc, 0.55f);
                        Glow("ring b", water, torusB, wc, 0.55f).localRotation = Quaternion.AngleAxis(0.5f * Mathf.Rad2Deg, Vector3.right);
                    }
                    projMeshes[p.id] = m;
                }
                if (m == null)
                {
                    m = Group("proj " + p.fx);
                    var core = Part("core", m, Sphere(), Core(Color.Lerp(col, Color.white, 0.5f)));
                    var glow = Glow("glow", m, Sphere(), col, 0.35f);
                    float r = p.fx == "fist" ? 0.45f : p.fx == "hexbomb" ? 0.3f : p.heal ? 0.22f : p.splash > 0 ? 0.2f : 0.09f;
                    core.localScale = Vector3.one * r; glow.localScale = Vector3.one * r * 2.6f;
                    if (System.Array.IndexOf(STREAK, p.fx) >= 0 && !(p.splash > 0)) core.localScale = new Vector3(r * 0.8f, r * 0.8f, r * 5);
                    if (p.fx == "sonic")
                    {
                        core.localScale = new Vector3(0.07f, 0.07f, 0.2f); glow.localScale = Vector3.one * 0.2f;
                        // the TS ring faces along the flight (its plane XY): the annulus (plane XZ) stood up by +90 about X
                        var ring = Glow("ring", m, FxKit.RingMesh, col, 0.8f);
                        ring.localRotation = Quaternion.Euler(90, 0, 0); ring.localScale = Vector3.one * 0.22f;
                    }
                    projMeshes[p.id] = m;
                }
                // drawn ahead of the last step by the time not yet simulated (velocity is constant within a step)
                var pos = Conv.U(p.pos) + Conv.U(p.vel) * (float)(alpha * MatchRunner.DT);
                m.position = pos;
                if (p.fx == "sonic") { var rg = m.Find("ring"); if (rg != null) rg.localScale = Vector3.one * (0.2f + 0.08f * Mathf.Sin(now * 60 + p.id)); }
                if (p.fx == "crescent") { var b = m.Find("spin"); if (b != null) b.localRotation = Quaternion.AngleAxis(-now * 26 * Mathf.Rad2Deg, Vector3.right); }
                else if (p.mesh != null)
                {
                    var b = m.Find("spin"); if (b != null) b.localRotation = Quaternion.AngleAxis(-now * (float)(p.spin ?? 30) * Mathf.Rad2Deg, Vector3.up);
                    var wtr = m.Find("water");
                    if (wtr != null)
                    {
                        wtr.localRotation = Quaternion.AngleAxis(now * 11 * Mathf.Rad2Deg, Vector3.up);
                        wtr.GetChild(1).localRotation = Quaternion.AngleAxis(0.5f * Mathf.Sin(now * 7 + p.id) * Mathf.Rad2Deg, Vector3.right);
                        float sc = p.seekTgt.HasValue ? 1.4f : 1 + 0.15f * (float)(p.bounced ?? 0);
                        wtr.localScale = Vector3.one * sc;
                        // the spray: droplets flung off the rim that fall away behind it, more while it hunts and right after a bounce
                        fx.Emit(pos, p.seekTgt.HasValue ? 3 : 2, Color.Lerp(col, Color.white, 0.35f), FxKit.O(speed: 1.4f, life: 0.4f, size: 0.1f, grav: 6, spread: 0.3f));
                        // ...and a streak of water behind it, so the star reads from across the arena (the chain / cable line, reused)
                        int key = -p.id; var v = Conv.U(p.vel); float sp = v.magnitude; if (sp < 1e-6f) sp = 1;
                        if (!beamMeshes.TryGetValue(key, out var line)) beamMeshes[key] = line = Glow("water streak", root, FxKit.BeamMesh, col, 0.55f);
                        float L = p.seekTgt.HasValue ? 2.2f : 1.5f;
                        FxKit.Orient(line, pos - v / sp * L, pos, 0.09f);
                    }
                }
                var vel = Conv.U(p.vel);
                if (vel.sqrMagnitude > 0.01f) m.rotation = Quaternion.FromToRotation(Vector3.forward, vel.normalized);
                if (Random.value < 0.6f) fx.Emit(pos, 1, col, FxKit.O(speed: 0.5f, life: 0.25f, size: p.splash > 0 ? 0.3f : 0.14f));
                if (p.fx == "chain" || p.fx == "fist")
                {
                    // draw the chain / cable back to the thrower
                    int key = -p.id;
                    if (!beamMeshes.TryGetValue(key, out var line)) beamMeshes[key] = line = Glow("chain", root, FxKit.BeamMesh, col, 0.8f);
                    FxKit.Orient(line, Conv.U(p.owner.Center), pos, 0.05f);
                }
            }
            gone.Clear();
            foreach (var id in projMeshes.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Drop(projMeshes[id]); projMeshes.Remove(id);
                if (beamMeshes.TryGetValue(-id, out var line)) { Drop(line); beamMeshes.Remove(-id); }
            }
        }

        /// <summary>the souls of the fallen (Overwatch's launch-era Resurrect): a soft golden orb where a teammate fell with a
        /// faint pillar above it, for as long as a living Mirei could still call them back; everyone sees them</summary>
        void SyncSouls(World w, FxKit fx, float now)
        {
            seen.Clear();
            var col = Conv.Hex(REBIRTH_COLOR);
            foreach (var a in w.actors)
            {
                if (!Rebirth.SoulLingers(w, a)) continue;
                seen.Add(a.id);
                if (!soulMeshes.TryGetValue(a.id, out var m))
                {
                    m = Group("soul " + a.def.id);
                    Part("orb", m, Sphere(), Core(Color.Lerp(col, Color.white, 0.5f))).localScale = Vector3.one * 0.22f;
                    Glow("glow", m, Sphere(), col, 0.35f).localScale = Vector3.one * 0.6f;
                    // the pillar: the beam tube (along +Z) stood up (-90 about X), 0.35 wide, 6 m tall
                    var pillar = Glow("pillar", m, FxKit.BeamMesh, col, 0.18f);
                    pillar.localRotation = Quaternion.Euler(-90, 0, 0); pillar.localScale = new Vector3(0.35f, 0.35f, 6);
                    soulMeshes[a.id] = m;
                }
                m.position = Conv.U(a.pos.x, a.pos.y + 0.55 + Mathf.Sin(now * 2 + a.id) * 0.06, a.pos.z);
                m.Find("glow").localScale = Vector3.one * (0.55f + 0.1f * Mathf.Sin(now * 4 + a.id));
                if (Random.value < 0.25f) fx.Emit(m.position, 1, col, FxKit.O(speed: 0.3f, life: 0.9f, size: 0.14f, up: 0.8f, spread: 0.3f));
            }
            gone.Clear();
            foreach (var id in soulMeshes.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { Drop(soulMeshes[id]); soulMeshes.Remove(id); }
        }

        /// <summary>the Crescent Fang out of her hand: buried in a wall, riding an enemy (blade in, grip out), or whirling home</summary>
        void SyncFangs(World w, FxKit fx, float now)
        {
            seen.Clear();
            var glow = Conv.Hex(FANG_GLOW);
            foreach (var a in w.actors)
            {
                double st = a.def.id == "tomoe" ? a.Sv("fang", 0) : 0;
                if (st < 2) continue;
                var at = Abilities.FangPos(w, a);
                if (!at.HasValue) continue;
                seen.Add(a.id);
                if (!fangMeshes.TryGetValue(a.id, out var m)) { m = Group("fang " + a.id); var f = Fang(); f.name = "spin"; f.SetParent(m, false); fangMeshes[a.id] = m; }
                var b = m.Find("spin");
                var atU = Conv.U(at.Value);
                if (st == 4)
                {
                    // whirling home, edge first
                    m.position = atU;
                    var v = Conv.U(a.pos.x, a.pos.y + a.Height * 0.6, a.pos.z) - atU;
                    if (v.sqrMagnitude > 1e-4f) m.rotation = Quaternion.FromToRotation(Vector3.forward, v.normalized);
                    b.localRotation = Quaternion.AngleAxis(-now * 30 * Mathf.Rad2Deg, Vector3.right);
                    if (Random.value < 0.8f) fx.Emit(atU, 1, glow, FxKit.O(speed: 0.4f, life: 0.25f, size: 0.18f));
                }
                else
                {
                    // buried: the blade points back the way it came in (from its owner), the grip sticking out
                    var tgt = st == 3 ? w.actors.Find(o => o.id == (int)a.Sv("fangTgt", 0)) : null;
                    var from = Conv.U(a.pos.x, a.pos.y + a.Height * 0.6, a.pos.z);
                    var d = atU - from; d.y = 0; if (d.sqrMagnitude < 1e-4f) d = Vector3.forward; d.Normalize();
                    m.position = atU + d * (tgt != null ? -(float)(tgt.Radius + 0.05) : -0.12f);
                    m.rotation = Quaternion.FromToRotation(Vector3.forward, d);
                    b.localRotation = Quaternion.AngleAxis(0.35f * Mathf.Rad2Deg, Vector3.right);
                    if (Random.value < 0.15f) fx.Emit(m.position, 1, glow, FxKit.O(speed: 0.2f, life: 0.5f, size: 0.14f, up: 0.6f));
                }
            }
            gone.Clear();
            foreach (var id in fangMeshes.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { Drop(fangMeshes[id]); fangMeshes.Remove(id); }
        }
    }
}
