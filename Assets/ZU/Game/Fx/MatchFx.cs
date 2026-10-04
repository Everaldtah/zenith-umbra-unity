// A match's visual effects (the TS render/Fx.ts, ported): every simulation fx event drawn from the FxKit primitives the
// way the web game draws it (hits, impacts, tracers, slashes, slams, shockwaves, ult flashes, parries, auras, bolts from
// the sky...), and per frame the ability zones on the ground, heal beams and flame cones, and the status particles on
// heroes (burning, bleeding, stunned, rooted, silenced...). The hero-specific showpieces (Kaien's seal storm, the koi
// dragons, Enra's chains) fall back to the generic burst until their own views are ported.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public class MatchFx : MonoBehaviour
    {
        MatchRunner r;
        FxKit fx;
        WeaponFx wfx;
        /// <summary>the match's gunfire effects (tracers, flashes, impacts, brass)</summary>
        public static WeaponFx Weapons { get; private set; }
        public static FxKit Current { get; private set; }
        const string REBIRTH = "#ffd98a";

        public static MatchFx Attach(MatchRunner runner)
        {
            var m = runner.gameObject.AddComponent<MatchFx>();
            m.r = runner;
            m.fx = Current = new FxKit(runner.transform);
            // the desktop edition's gunfire (TS WeaponFx): travelling tracers, star flashes, sparks, smoke, scorch marks, brass
            var alpha = Resources.Load<Material>("ZUFx/alpha");
            m.wfx = Weapons = new WeaponFx(runner.transform, m.fx.Additive, alpha != null ? alpha : new Material(Shader.Find("ZU/FxAlpha")));
            var lvl = runner.World.level;
            m.wfx.ground = (x, z, y) => (float)lvl.GroundAt(-x, z, y);      // (Unity x is the sim's -x)
            return m;
        }

        void Start() { EventSink.OnEvent += OnEvent; }

        int hand = 1;
        /// <summary>TS Game.muzzleFor: the local player's rounds in first person leave the viewmodel's gun (just right of and below
        /// the view, the twin guns taking turns); everyone else's from where the sim fired them</summary>
        Vector3 MuzzleFor(Actor a, Vector3 from)
        {
            var c = Camera.main;
            if (a == null || a != r.Player || r.thirdPerson || c == null) return from;
            var t = c.transform; var f = t.forward; var rt = Vector3.Cross(Vector3.up, f).normalized; var u = Vector3.Cross(f, rt);
            if (a.def.dualGuns) hand = -hand;
            float side = a.def.dualGuns ? hand * 0.42f : 0.24f;
            return t.position + f * 1.45f + rt * (side * 1.15f) + u * -0.3f;
        }
        /// <summary>draw an effect the views raise themselves (not a sim event: a heavy footfall's dust, TS Game's onStep)</summary>
        public void Fire(FxEvent e) => OnEvent(r, e);
        void OnDestroy() { EventSink.OnEvent -= OnEvent; if (Current == fx) Current = null; if (Weapons == wfx) Weapons = null; }

        static Color C(string hex, string fallback = "#ffffff") => Conv.Hex(hex ?? fallback, Color.white);
        static Vector3 U(V3 v) => Conv.U(v);
        static Vector3 Up(Vector3 p, float y) => new Vector3(p.x, p.y + y, p.z);
        float Now => (float)r.World.time;

        void OnEvent(MatchRunner runner, SimEvent ev)
        {
            if (runner != r || !(ev is FxEvent e)) return;
            var cam = Camera.main;
            var p = U(e.pos);
            float near = cam != null ? Vector3.Distance(cam.transform.position, p) : 20;
            bool dmg = e.kind == "hit" || e.kind == "impact" || e.kind == "slash" || e.kind == "wound" || e.kind == "burst";
            float lod = near > 60 ? 0.35f : near > 30 ? 0.7f : 1;
            int N(int k) => Mathf.Max(1, Mathf.RoundToInt(k * lod));
            var c = C(e.color);
            string col = e.color ?? "#ffffff";
            float now = Now, R = (float)(e.r ?? 6);
            Vector3? to = e.to.HasValue ? U(e.to.Value) : (Vector3?)null;
            var a = e.actor;
            void Shake(float k) => fx.Shake = Mathf.Max(fx.Shake, k);
            switch (e.kind)
            {
                case "hit": fx.Emit(p, N(8), c, FxKit.O(5, 0.25f, 0.18f)); wfx.Impact(p, null, c, now); break;
                case "impact": wfx.Impact(p, e.n.HasValue ? U(e.n.Value) : (Vector3?)null, c, now); break;
                case "healhit": fx.Emit(p, N(10), C("#9dffb0"), FxKit.O(2, 0.6f, 0.2f, up: 2)); break;
                case "burst": fx.Emit(p, N(30), c, FxKit.O(7, 0.45f, 0.35f)); fx.Ring(p, (float)(e.r ?? 2) * 1.2f, c, now, 0.35f, false); fx.Light(p, c, 25, now); Shake(0.12f / (1 + near / 10)); break;
                case "tracer":
                    if (to.HasValue)
                    {
                        // heavy rotary rounds: fat orange-white tracers, big flashes, brass; everything else a lean rail round
                        bool heavy = a != null && a.def.dualGuns;
                        p = MuzzleFor(a, p);
                        if (heavy) wfx.Tracer(p, to.Value, c, now, speed: 150, w: 0.16f, len: 5.5f); else wfx.Tracer(p, to.Value, c, now, speed: 220, w: 0.08f, len: 3.5f);
                        wfx.Muzzle(p, c, now, heavy ? 0.85f : 0.5f, heavy);
                        if (heavy)
                        {
                            var f = a.Forward(); double hh = a.Height;
                            wfx.Casing(Conv.U(a.pos.x + f.x * 0.6 - f.z * 0.5, a.pos.y + hh * 0.45, a.pos.z + f.z * 0.6 + f.x * 0.5), Conv.U(-f.z, 0, f.x), now);
                        }
                        if (near < 25) fx.Light(p, c, heavy ? 10 : 6, now, 0.04f);
                    }
                    break;
                case "slash": case "swing":
                    if (e.kind == "slash") fx.Emit(p, N(12), c, FxKit.O(6, 0.3f, 0.22f));
                    else if (a != null)
                    {
                        float s = (float)a.scale;
                        fx.Arc(Up(U(a.pos), (float)a.Height * 0.55f), Conv.Yaw(a.yaw) * Quaternion.Euler(-17, 0, 0), 1.4f * s, 2.6f * s, -0.9f, 0.9f, c, now, 0.18f);
                    }
                    break;
                case "hammer":
                    if (a != null)
                    {
                        float side = (float)(e.side ?? 1), rr = (float)(e.r ?? 5);
                        fx.Arc(Up(U(a.pos), (float)a.Height * 0.5f), Conv.Yaw(a.yaw) * Quaternion.Euler(-7 * side, 0, 0), rr * 0.3f, rr, -1.5f, 1.5f, c, now, 0.22f, 0.55f);
                        var f = U(a.Forward());
                        fx.Emit(Up(U(a.pos) + f * rr * 0.8f, (float)a.Height * 0.45f), N(18), c, FxKit.O(7, 0.35f, 0.25f));
                        Shake(0.05f / (1 + near / 8));
                    }
                    break;
                case "shatter":
                    if (to.HasValue)
                    {
                        fx.Wedge(p, to.Value, 0.42f, c, now, 0.55f);
                        for (int k = 1; k <= 8; k++)
                        {
                            var q = Vector3.Lerp(p, to.Value, k / 8f) + Vector3.up * 0.2f;
                            fx.Emit(q, N(10), c, FxKit.O(5 + 4 * k / 8f, 0.6f, 0.35f, grav: 12, up: 4));
                            fx.Emit(q, N(6), C("#6b5b4a"), FxKit.O(4, 0.8f, 0.45f, grav: 14, up: 5));
                        }
                        fx.Ring(p, 3, C(e.color, "#ffd76a"), now, 0.4f); fx.Light(p, C(e.color, "#ffd76a"), 40, now); Shake(0.3f / (1 + near / 12));
                    }
                    break;
                case "lightning": if (to.HasValue) fx.Bolt(p, to.Value, C("#8ad8ff"), now, 0.04f, 0.18f, 0.5f, 7); break;
                // the koi dragons draw these themselves (AbilityFx SpiritDragon), as Fx.ts hands them to its dragons
                case "twinkoi": case "dragoncut": case "dragoncoil": break;
                case "parry": fx.Ring(p, 1.8f, C("#8ad8ff"), now, 0.3f, false); fx.Emit(p, N(16), c, FxKit.O(8, 0.2f, 0.15f)); fx.Light(p, C("#8ad8ff"), 30, now); break;
                case "deflect": fx.Emit(p, N(14), c, FxKit.O(9, 0.22f, 0.12f, spread: 0.35f, dir: e.n.HasValue ? U(e.n.Value) : (Vector3?)null)); fx.Emit(p, N(6), Color.white, FxKit.O(3, 0.12f, 0.2f)); fx.Light(p, C("#8ad8ff"), 22, now); break;
                case "decoy": fx.Emit(p, N(40), c, FxKit.O(4, 0.8f, 0.3f)); fx.Ring(p, 2, C(e.color, "#c77dff"), now, 0.5f, false); break;
                case "undying": fx.Emit(p, N(20), C("#ffe28a"), FxKit.O(3, 0.6f, 0.3f, up: 2)); break;
                case "death": fx.Emit(p, N(50), c, FxKit.O(5, 1.2f, 0.3f, up: 2)); break;
                case "eject": fx.Emit(p, N(40), C("#ffb040"), FxKit.O(8, 0.6f, 0.35f)); fx.Light(p, C("#ffb040"), 60, now, 0.25f); Shake(0.35f); break;
                case "dust": fx.Emit(p, N(24), C("#b8a58a"), FxKit.O(3, 0.8f, 0.6f, spread: (float)(e.r ?? 1.5), up: 0.5f)); Shake(0.2f / (1 + near / 8)); break;
                case "step": fx.Emit(p, N(6), C("#9c8f7c"), FxKit.O(1.2f, 0.5f, 0.35f, spread: 0.6f, up: 0.4f)); break;
                case "doublejump": case "sunhop": fx.Ring(p, 1.5f, c, now, 0.3f); fx.Emit(p, N(14), c, FxKit.O(3, 0.4f, 0.2f)); break;
                case "pad": fx.Ring(p, 2, c, now, 0.4f); fx.Emit(p, N(20), c, FxKit.O(2, 0.6f, 0.25f, up: 6, dir: Vector3.up)); break;
                case "spawn": fx.Emit(Up(p, 1), N(30), c, FxKit.O(2, 0.8f, 0.3f, spread: 1, up: 3)); fx.Ring(p, 1.6f, c, now, 0.6f); break;
                case "barrierhit": fx.Emit(p, N(5), c, FxKit.O(3, 0.2f, 0.2f)); break;
                case "barrierbreak": fx.Emit(p, N(60), c, FxKit.O(9, 0.7f, 0.35f)); fx.Light(p, c, 40, now); break;
                case "ultflash": fx.Ring(p, 5, c, now, 0.6f, false); fx.Emit(p, N(50), c, FxKit.O(6, 0.8f, 0.4f)); fx.Light(p, c, 70, now, 0.3f); break;
                case "sunburst": case "nova": case "requiem": case "theater": case "sanctuarycast": case "sealcast": case "revealburst": case "hexburst": case "implode": case "slam": case "arrowsmark":
                {
                    bool big = e.kind == "slam" || e.kind == "implode";
                    fx.Ring(p, R, c, now, big ? 0.5f : 0.8f);
                    if (e.kind != "arrowsmark") fx.Ring(Up(p, 0.5f), R * 0.7f, c, now, 0.6f, false);
                    fx.Emit(p, N(big ? 90 : 60), c, FxKit.O(R * 0.9f, 0.7f, 0.4f, spread: 1));
                    fx.Light(p, c, e.kind == "slam" ? 120 : 50, now, 0.25f);
                    if (big) Shake(0.6f / (1 + near / 15));
                    break;
                }
                case "link": case "strings": case "chainline":
                    if (a != null && e.target != null)
                    {
                        var src = a; var tgt = e.target;
                        fx.Beam(U(src.Center), U(tgt.Center), c, now, (float)(e.dur ?? 0.5), e.kind == "chainline" ? 0.08f : 0.03f, () => (U(src.Center), U(tgt.Center), src.alive && tgt.alive));
                    }
                    break;
                case "wish": case "voidshield": case "bloodpact": case "parrystance": fx.Emit(p, N(25), c, FxKit.O(2.5f, 0.6f, 0.3f)); break;
                case "papers": fx.Emit(Up(p, 1), N(40), C("#fff6d8"), FxKit.O(4, 0.8f, 0.28f, grav: -1, spread: 1)); break;
                case "smoke": fx.Emit(Up(p, 1), N(40), C("#3a2a5a"), FxKit.O(2.5f, 1, 0.8f, spread: 1.2f, up: 0.5f)); break;
                case "flash": case "chargetrail":
                    if (a != null) { var src = a; fx.During(now, (float)(e.dur ?? 0.3), _ => fx.Emit(Up(U(src.pos), (float)src.Height * 0.42f), 2, c, FxKit.O(0.8f, 0.3f, 0.24f, spread: 0.35f))); }
                    break;
                case "lance": case "soundcone": case "brandcone":
                    if (to.HasValue) { fx.Cone(p, to.Value, e.kind == "lance" ? 1 : e.kind == "soundcone" ? 5 : 4, c, now, 0.35f); fx.Emit(to.Value, N(20), c, FxKit.O(4, 0.4f, 0.3f)); }
                    break;
                case "cut": if (to.HasValue) { fx.Beam(p, to.Value, C("#9d7bff"), now, 0.2f, 0.08f); fx.Emit(to.Value, N(20), c, FxKit.O(6, 0.3f, 0.25f)); } break;
                case "arrowhit": fx.Beam(Up(p, 14), p, C("#fff2b0"), now, 0.12f, 0.05f); fx.Emit(p, N(8), c, FxKit.O(4, 0.35f, 0.25f)); break;
                case "immune": case "blocked": case "interrupt": fx.Emit(p, N(16), c, FxKit.O(3, 0.4f, 0.22f)); fx.Ring(p, 1.2f, c, now, 0.3f, false); break;
                case "zonebreak": fx.Ring(p, R, C(e.color, "#9d7bff"), now, 0.5f); fx.Emit(p, N(50), c, FxKit.O(6, 0.6f, 0.3f, spread: (float)(e.r ?? 4))); break;
                case "singularity": fx.Emit(p, N(30), c, FxKit.O(3, 0.6f, 0.4f, spread: 3)); break;
                case "fall": break;
                case "swoop":
                    if (a != null && e.target != null) fx.Beam(U(a.Center), U(e.target.Center), C(e.color, "#bfe8ff"), now, 0.22f, 0.025f);
                    fx.Ring(p, 1.4f, C(e.color, "#bfe8ff"), now, 0.3f, false); fx.Emit(p, N(24), C("#fff4d6"), FxKit.O(4, 0.5f, 0.28f, spread: 0.6f));
                    break;
                case "swoopburst": fx.Ring(p, 2.2f, C(e.color, "#bfe8ff"), now, 0.35f); fx.Emit(p, N(34), C("#fff4d6"), FxKit.O(6, 0.55f, 0.3f, spread: 0.6f)); fx.Light(p, C(e.color, "#bfe8ff"), 25, now); break;
                // ultimate charge pack (Training Grounds): a gold ring, sparks rising, a pulse of light
                case "ultpack": fx.Ring(Up(p, -0.45f), 1.8f, C("#ffd23f"), now, 0.5f); fx.Emit(p, N(30), C("#fff1a8"), FxKit.O(3f, 0.8f, 0.3f, spread: 0.6f, up: 4)); fx.Light(p, C("#ffc83a"), 22, now); break;
                case "healthpack": fx.Ring(Up(p, -0.45f), (float)(e.r ?? 1) * 1.6f, C("#29f0a0"), now, 0.45f); fx.Emit(p, N(26), C("#7dffb0"), FxKit.O(2.5f, 0.7f, 0.28f, spread: 0.6f, up: 3)); fx.Light(p, C("#29f0a0"), 18, now); break;
                case "wound": fx.Emit(p, N(10), C("#ff2d55"), FxKit.O(2.5f, 0.5f, 0.16f, grav: 7)); fx.Emit(p, N(4), C("#ffd0d8"), FxKit.O(4, 0.18f, 0.12f)); break;
                case "warcall":
                {
                    var g = Up(p, -(a != null ? (float)a.Height * 0.5f : 1));
                    fx.Ring(g, (float)(e.r ?? 15), C("#ffd98a"), now, 0.8f); fx.Ring(g, (float)(e.r ?? 15) * 0.6f, C(e.color, "#5ff2e0"), now, 0.6f);
                    fx.Ring(p, 2.2f, C("#ffd98a"), now, 0.45f, false);
                    fx.Emit(p, N(40), C("#ffe7a8"), FxKit.O(4, 0.7f, 0.3f, spread: 0.6f, up: 3)); fx.Emit(p, N(20), c, FxKit.O(6, 0.5f, 0.24f));
                    fx.Light(p, C("#ffd98a"), 40, now, 0.25f);
                    break;
                }
                case "warcallally": fx.Ring(Up(p, -(a != null ? (float)a.Height * 0.5f : 1)), 1.3f, C("#ffd98a"), now, 0.45f); fx.Emit(p, N(14), C("#ffe7a8"), FxKit.O(1.5f, 0.6f, 0.24f, spread: 0.5f, up: 1.8f)); break;
                case "reaping":
                    if (a != null)
                    {
                        float RR = (float)(e.r ?? 5.5) * 0.8f;
                        fx.Arc(Up(U(a.pos), (float)a.Height * 0.5f), Conv.Yaw(a.yaw) * Quaternion.Euler(-26, 0, 20), RR * 0.55f, RR, -1.15f, 1.15f, c, now, 0.28f, 0.85f);
                        Shake(0.1f / (1 + near / 10));
                    }
                    break;
                case "crossmix": fx.Ring(p, (float)(e.r ?? 12), C(e.color, "#7dffcf"), now, 0.55f); fx.Emit(p, N(24), c, FxKit.O(4, 0.5f, 0.25f, up: 1)); break;
                case "amp": for (int i = 0; i < 3; i++) fx.Ring(Up(p, -0.8f + i * 0.5f), (float)(e.r ?? 12) * (0.5f + i * 0.25f), C(e.color, "#7dffcf"), now, 0.5f + i * 0.12f); fx.Emit(p, N(40), c, FxKit.O(6, 0.6f, 0.3f)); fx.Light(p, C(e.color, "#7dffcf"), 30, now); break;
                case "scratchwave":
                    if (a != null)
                    {
                        var f = U(a.Forward()); bool big = e.side == 1;
                        for (int i = 0; i < 4; i++) fx.Ring(Up(p + f * (1.2f + i * 1.7f), -0.3f), (0.8f + i * 0.9f) * (big ? 1.3f : 1), C(e.color, "#9ef6ff"), now + i * 0.03f, 0.32f, false);
                        fx.Emit(Up(p + f * 3, -0.3f), N(big ? 40 : 24), c, FxKit.O(7, 0.35f, 0.25f, dir: new Vector3(f.x, 0.05f, f.z)));
                        Shake(big ? 0.12f : 0.06f);
                    }
                    break;
                case "bassdrop": fx.Ring(p, (float)(e.r ?? 30), C(e.color, "#7dffcf"), now, 0.9f); fx.Ring(p, (float)(e.r ?? 30) * 0.5f, Color.white, now, 0.6f); fx.Emit(p, N(120), c, FxKit.O(12, 0.8f, 0.4f, spread: 1.5f, up: 1)); fx.Light(p, C(e.color, "#7dffcf"), 140, now, 0.3f); Shake(0.5f / (1 + near / 20)); break;
                case "bassshield": fx.Emit(p, N(18), C("#bffcff"), FxKit.O(2, 0.6f, 0.3f, spread: 0.8f, up: 1.5f)); break;
                case "ringhit": fx.Emit(p, N(8), C("#ffe6a8"), FxKit.O(3, 0.3f, 0.2f)); fx.Emit(p, N(3), Color.white, FxKit.O(1.5f, 0.8f, 0.22f, grav: 3)); break;
                case "ignite": fx.Ring(p, 1.1f, C("#ff8a3d"), now, 0.3f, false); fx.Emit(p, N(26), C("#ff8a3d"), FxKit.O(3, 0.6f, 0.35f, spread: 0.5f, up: 2.5f)); break;
                case "taiko": case "taikopulse":
                {
                    bool big = e.kind == "taiko"; float rr = (float)(e.r ?? 12) * (big ? 1 : 0.85f);
                    fx.Ring(Up(p, -(a != null ? (float)a.Height * 0.5f : 1)), rr, C(e.color, "#ffb35c"), now, big ? 0.7f : 0.55f);
                    if (big) { fx.Ring(p, 2.4f, C(e.color, "#ffb35c"), now, 0.4f, false); fx.Light(p, C(e.color, "#ffb35c"), 30, now); }
                    fx.Emit(p, N(big ? 30 : 10), c, FxKit.O(big ? 5 : 3, 0.5f, 0.3f, spread: 0.8f));
                    break;
                }
                case "stomp":
                {
                    float rr = (float)(e.r ?? 7);
                    fx.Ring(p, rr, C(e.color, "#ffb35c"), now, 0.5f); fx.Ring(p, rr * 0.62f, C("#ff6a2a"), now, 0.4f); fx.Ring(p, rr * 0.36f, Color.white, now, 0.28f);
                    fx.Light(p, C("#ff8a3d"), 40, now);
                    fx.Emit(Up(p, 0.2f), N(36), C("#6b5b4a"), FxKit.O(7, 0.9f, 0.5f, grav: 16, spread: 3, up: 7));
                    fx.Emit(Up(p, 0.3f), N(28), c, FxKit.O(9, 0.55f, 0.3f, spread: 2.5f, up: 3));
                    Shake(0.7f / (1 + near / 12));
                    break;
                }
                case "susanoocast":
                {
                    var top = Up(p, 40);
                    fx.Beam(top, p, Color.white, now, 0.6f, 0.35f); fx.Beam(top, p, C("#8ad8ff"), now, 1.6f, 0.5f);
                    fx.Ring(p, (float)(e.r ?? 12), C("#8ad8ff"), now, 0.7f); fx.Ring(p, (float)(e.r ?? 12) * 0.5f, Color.white, now, 0.4f);
                    fx.Light(p, C("#bfe8ff"), 90, now, 0.35f);
                    fx.Emit(Up(p, 1), N(60), C("#bfe8ff"), FxKit.O(8, 0.9f, 0.4f, spread: 3, up: 6));
                    Shake(0.5f / (1 + near / 15));
                    break;
                }
                case "skybolt":
                {
                    var top = p + new Vector3((Random.value - 0.5f) * 3, 36, (Random.value - 0.5f) * 3);
                    fx.Bolt(top, p, Color.white, now, 0.14f, 0.22f); fx.Bolt(top, p, C("#8ad8ff"), now, 0.4f, 0.5f);
                    fx.Ring(p, 2.6f, Color.white, now, 0.3f); fx.Ring(p, 4, C("#8ad8ff"), now, 0.5f);
                    fx.Light(Up(p, 2), C("#dff4ff"), 80, now, 0.5f);
                    fx.Emit(Up(p, 0.3f), N(30), C("#bfe8ff"), FxKit.O(7, 0.5f, 0.28f, spread: 1.2f, up: 4));
                    Shake(0.35f / (1 + near / 10));
                    break;
                }
                case "stormcall":
                {
                    var b0 = a != null ? U(a.pos) : p; float h = a != null ? (float)(a.def.height * a.scale) : 5.4f;
                    var tip = Up(b0, h * 1.22f); var top = tip + new Vector3((Random.value - 0.5f) * 2, 30, (Random.value - 0.5f) * 2);
                    fx.Bolt(top, tip, Color.white, now, 0.12f, 0.2f); fx.Bolt(top, tip, C("#8ad8ff"), now, 0.35f, 0.45f);
                    fx.Light(tip, C("#dff4ff"), 60, now, 0.4f); fx.Emit(tip, N(18), C("#bfe8ff"), FxKit.O(4, 0.4f, 0.35f, spread: 0.8f));
                    break;
                }
                case "susanooslash": { float rr = (float)(e.r ?? 5.5); fx.Ring(p, rr, C("#8ad8ff"), now, 0.35f); fx.Ring(p, rr * 0.6f, Color.white, now, 0.22f); fx.Light(p, C("#8ad8ff"), 30, now); fx.Emit(p, N(24), C("#bfe8ff"), FxKit.O(9, 0.4f, 0.3f, spread: 2)); break; }
                case "susanoofade": case "effigyfade": fx.Emit(Up(p, 2.5f), N(50), c, FxKit.O(2.5f, 1.1f, 0.45f, spread: 2.2f, up: 2.5f)); fx.Light(p, C(e.color, "#8ad8ff"), 40, now, 0.4f); break;
                case "rebirthcast":
                {
                    fx.Ring(p, R, C(REBIRTH), now, 1); fx.Ring(p, R * 0.4f, Color.white, now, 0.5f); fx.Light(Up(p, 1.5f), C(REBIRTH), 60, now, 0.6f);
                    if (a != null) { var src = a; float RR = R; fx.During(now, 1, _ => { for (int k = 0; k < 6; k++) { float an = Random.value * Mathf.PI * 2, d = Mathf.Sqrt(Random.value) * RR * 0.8f; fx.Emit(U(src.pos) + new Vector3(Mathf.Cos(an) * d, 0.3f + Random.value * 1.5f, Mathf.Sin(an) * d), 1, C(REBIRTH), FxKit.O(0.4f, 1.3f, 0.3f, spread: 0.2f, up: 2.4f)); } }); }
                    break;
                }
                case "rebirth": fx.Ring(p, 2.2f, Color.white, now, 0.4f); fx.Ring(p, 3.2f, C(REBIRTH), now, 0.7f); fx.Beam(Up(p, -1), Up(p, 9), C(REBIRTH), now, 1.4f, 0.5f); fx.Light(p, C(REBIRTH), 50, now, 0.5f); fx.Emit(p, N(40), C(REBIRTH), FxKit.O(2.5f, 1.2f, 0.35f, spread: 1, up: 3)); break;
                case "rebirthring": fx.Ring(p, 2.4f, C(REBIRTH), now, 0.45f); fx.Emit(p, N(16), Color.white, FxKit.O(4, 0.4f, 0.25f, spread: 0.8f)); break;
                case "bossbeam": if (to.HasValue) { fx.Beam(p, to.Value, c, now, 0.07f, 0.45f); fx.Beam(p, to.Value, Color.white, now, 0.07f, 0.15f); fx.Emit(to.Value, N(3), c, FxKit.O(4, 0.3f, 0.5f)); } break;
                // Kaien's seals: AbilityFx SealStorm draws its own bursts, rings and lights
                case "sealstorm": case "sealshield": case "sealstrike": case "sealburst": case "sealmend": break;
                default: fx.Emit(p, N(10), c, FxKit.O(3, 0.4f, 0.25f)); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ per frame
        sealed class ZoneView { public GameObject go; public Transform disc, edge, dome; public MeshRenderer discR, edgeR, domeR; }
        readonly Dictionary<int, ZoneView> zones = new Dictionary<int, ZoneView>();
        readonly Dictionary<int, GameObject> beams = new Dictionary<int, GameObject>(), flames = new Dictionary<int, GameObject>();
        MaterialPropertyBlock mpb;          // (created on first use: not allowed in a MonoBehaviour's field initialiser)
        static Mesh disc, edge, dome, tube;
        Material addMat;

        void LateUpdate()
        {
            var w = r.World;
            if (w == null) return;
            float now = Now;
            fx.Update(now, Time.deltaTime);
            wfx.Update(now, Time.deltaTime);
            SyncZones(w, now);
            SyncBeams(w, now);
            Status(w, now, Time.deltaTime);
        }

        void Tint(MeshRenderer mr, Color c, float a) { mpb ??= new MaterialPropertyBlock(); c.a = Mathf.Clamp01(a); mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); mr.SetPropertyBlock(mpb); }

        MeshRenderer Child(Transform parent, string name, Mesh mesh)
        {
            addMat ??= Resources.Load<Material>("ZUFx/additive") ?? new Material(Shader.Find("ZU/FxAdditive"));
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = addMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        /// <summary>the ability zones on the ground: a translucent disc with a bright rim in the zone's colour (a dome over a
        /// sanctuary, a dark core in a singularity, a wall of light round the Grand Dohyo, a filling warning for boss attacks)</summary>
        void SyncZones(World w, float now)
        {
            disc ??= Disc(); edge ??= Ring(0.94f, 1f); dome ??= Dome(); tube ??= Wall();
            var seen = new HashSet<int>();
            foreach (var z in w.zones)
            {
                if (z.kind == "tether") continue;
                seen.Add(z.id);
                if (!zones.TryGetValue(z.id, out var v))
                {
                    v = new ZoneView { go = new GameObject("zone " + z.kind) };
                    v.go.transform.SetParent(transform, false);
                    v.discR = Child(v.go.transform, "disc", disc); v.disc = v.discR.transform;
                    v.edgeR = Child(v.go.transform, "edge", edge); v.edge = v.edgeR.transform;
                    if (z.kind == "sanctuary" || z.kind == "dohyo") { v.domeR = Child(v.go.transform, "dome", z.kind == "dohyo" ? tube : dome); v.dome = v.domeR.transform; }
                    zones[z.id] = v;
                }
                var col = z.kind == "seal" || z.kind == "sanctuary" ? C("#ffe28a") : z.kind == "grievous" ? C("#c77dff") : z.kind == "singularity" ? C("#ff2244") : z.kind == "tele" ? C("#ff3355") : z.kind == "dohyo" ? C("#ffe6a8") : C("#ffd27a");
                float life = (float)((z.until - now) / System.Math.Max(0.01, z.until - z.born));
                float grow = Mathf.Min(1, (float)(now - z.born) * 4) * (life < 0.1f ? Mathf.Max(0, life * 10) : 1);
                float rr = (float)z.r;
                v.go.transform.position = U(new V3(z.x, z.y, z.z)) + Vector3.up * 0.08f;
                if (z.kind == "tele")
                {
                    // the boss's warning: the fill grows until the hit lands
                    double fireAt = z.data != null && z.data.TryGetValue("fireAt", out var fa) ? System.Convert.ToDouble(fa) : z.until;
                    float k = Mathf.Clamp01((float)((now - z.born) / System.Math.Max(0.01, fireAt - z.born)));
                    v.disc.localScale = Vector3.one * rr * k; v.edge.localScale = Vector3.one * rr;
                    Tint(v.discR, col, 0.35f); Tint(v.edgeR, col, 0.9f);
                    continue;
                }
                v.disc.localScale = Vector3.one * rr * grow; v.edge.localScale = Vector3.one * rr * grow;
                Tint(v.discR, col, 0.14f + 0.04f * Mathf.Sin(now * 3)); Tint(v.edgeR, col, 0.85f);
                if (v.dome != null)
                {
                    if (z.kind == "dohyo") { v.dome.localScale = new Vector3(rr, (float)World.RING_H * Mathf.Min(1, (float)(now - z.born) * 3), rr); Tint(v.domeR, col, 0.22f * (life < 0.06f ? (Mathf.Sin(now * 60) > 0 ? 1 : 0.2f) : 1)); }
                    else { v.dome.localScale = Vector3.one * rr * grow; Tint(v.domeR, col, 0.1f); }
                }
                var zp = v.go.transform.position;
                if (z.kind == "singularity") fx.Emit(zp + new Vector3((Random.value - 0.5f) * rr * 2, 0.5f, (Random.value - 0.5f) * rr * 2), 2, col, FxKit.O(0.5f, 0.5f, 0.3f));
                if (z.kind == "seal" && Random.value < 0.5f) fx.Emit(zp + new Vector3((Random.value - 0.5f) * rr * 1.6f, 0.2f, (Random.value - 0.5f) * rr * 1.6f), 1, col, FxKit.O(0.3f, 1, 0.25f, up: 1.5f));
                if (z.kind == "grievous" && Random.value < 0.6f) fx.Emit(zp + new Vector3((Random.value - 0.5f) * rr * 1.6f, 0.2f, (Random.value - 0.5f) * rr * 1.6f), 1, col, FxKit.O(0.3f, 1.2f, 0.35f, up: 1));
            }
            var gone = new List<int>();
            foreach (var kv in zones) if (!seen.Contains(kv.Key)) { Destroy(kv.Value.go); gone.Add(kv.Key); }
            foreach (var id in gone) zones.Remove(id);
        }

        /// <summary>heal beams (a pulsing line to the target) and Enra's hellflame cone, per actor</summary>
        void SyncBeams(World w, float now)
        {
            tube ??= Wall();
            foreach (var a in w.actors)
            {
                beams.TryGetValue(a.id, out var bm);
                if (a.alive && a.beamOn && a.beamTarget != null)
                {
                    if (bm == null) { bm = Child(transform, "beam " + a.def.id, Beam()).gameObject; beams[a.id] = bm; }
                    bm.SetActive(true);
                    var from = U(w.Muzzle(a)); var to = U(a.beamTarget.Center);
                    var d = to - from; float l = Mathf.Max(0.01f, d.magnitude), wd = 0.05f + Mathf.Sin(now * 25) * 0.01f;
                    bm.transform.SetPositionAndRotation(from, d.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(d / l) : bm.transform.rotation); bm.transform.localScale = new Vector3(wd, wd, l);
                    Tint(bm.GetComponent<MeshRenderer>(), C(a.def.glow) * 1.4f, 0.85f);
                    if (Random.value < 0.5f) fx.Emit(to, 1, C("#9dffb0"), FxKit.O(1, 0.5f, 0.2f, up: 1.5f));
                }
                else if (bm != null) bm.SetActive(false);
                flames.TryGetValue(a.id, out var fl);
                if (a.alive && a.flameOn)
                {
                    if (fl == null) { fl = Child(transform, "flame " + a.def.id, Cone()).gameObject; flames[a.id] = fl; }
                    fl.SetActive(true);
                    var from = U(w.Muzzle(a)); var dir = U(a.AimDir());
                    float range = (float)(a.def.primary.range * (a.Has("asura", now) ? 1.5 : 1) * a.scale);
                    fl.transform.SetPositionAndRotation(from, dir.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(dir) : fl.transform.rotation); fl.transform.localScale = new Vector3(range * 0.27f, range * 0.27f, range);
                    Tint(fl.GetComponent<MeshRenderer>(), C("#ff6a2a"), 0.07f + Random.value * 0.05f);
                    for (int i = 0; i < 3; i++) fx.Emit(from, 1, Random.value < 0.5f ? C("#ff6a2a") : C("#b026ff"), FxKit.O(range * 1.6f, 0.5f, 0.45f, dir: dir));
                }
                else if (fl != null) fl.SetActive(false);
            }
        }

        /// <summary>the status particles on heroes (TS Fx.statusFx)</summary>
        void Status(World w, float now, float dt)
        {
            float k = Mathf.Min(1, dt * 60);
            var me = r.Player;
            foreach (var a in w.actors)
            {
                if (!a.alive || (a == me && !r.thirdPerson)) continue;
                var c = U(a.Center); var pos = U(a.pos); float rad = (float)a.Radius, h = (float)a.Height;
                bool P(float p) => Random.value < p * k;
                if (a.Has("brand", now) && P(0.3f)) fx.Emit(c, 1, C("#ff6a2a"), FxKit.O(0.8f, 0.6f, 0.25f, spread: rad, up: 1.5f));
                if (a.Has("bleed", now) && P(0.3f)) fx.Emit(c, 1, C("#ff1744"), FxKit.O(0.8f, 0.6f, 0.2f, grav: 4, spread: rad));
                if (a.Has("stun", now) && P(0.4f)) { float t = now * 6; fx.Emit(pos + new Vector3(Mathf.Cos(t) * 0.5f, h + 0.2f, Mathf.Sin(t) * 0.5f), 1, C("#ffee58"), FxKit.O(0.1f, 0.3f, 0.2f)); }
                if (a.Has("root", now) && P(0.4f)) { float t = Random.value * 6.28f; fx.Emit(pos + new Vector3(Mathf.Cos(t) * rad, 0.1f, Mathf.Sin(t) * rad), 1, C("#c77dff"), FxKit.O(0.2f, 0.4f, 0.2f)); }
                if (a.Has("silence", now) && P(0.3f)) fx.Emit(pos + Vector3.up * (h + 0.3f), 1, C("#ff4d6d"), FxKit.O(0.3f, 0.4f, 0.3f));
                if (a.Has("antiheal", now) && P(0.2f)) fx.Emit(c, 1, C("#7b2cbf"), FxKit.O(0.5f, 0.6f, 0.25f, spread: rad, up: 0.8f));
                if (a.Has("hot", now) && P(0.3f)) fx.Emit(c, 1, C("#9dffb0"), FxKit.O(0.5f, 0.7f, 0.2f, spread: rad, up: 1.5f));
                if (a.Has("tithe", now) && P(0.45f + 0.4f * Mathf.Max(0, Mathf.Sin(now * 4)))) { float th = now * 5 + a.id, rr = rad * 0.9f; fx.Emit(pos + new Vector3(Mathf.Cos(th) * rr, h * (0.15f + 0.5f * Random.value), Mathf.Sin(th) * rr), 1, Random.value < 0.7f ? C("#c77dff") : C("#f0d8ff"), FxKit.O(0.3f, 0.8f, 0.22f, spread: 0.15f, up: 1.6f)); }
                if (a.Has("judgment", now) && P(0.5f)) fx.Emit(c, 1, C("#8ad8ff"), FxKit.O(3, 0.2f, 0.18f, spread: rad * 2));
                if (a.Has("asura", now) && P(0.6f)) fx.Emit(c, 1, Random.value < 0.5f ? C("#ff6a2a") : C("#b026ff"), FxKit.O(1, 0.5f, 0.4f, spread: rad * 1.5f, up: 2.5f));
                if (a.flying && P(0.5f)) fx.Emit(pos + Vector3.up * h * 0.6f, 1, C(a.def.glow), FxKit.O(0.6f, 0.6f, 0.22f, grav: 1, spread: 0.8f));
                if (a.Has("burning", now) && P(0.7f)) fx.Emit(c, 1, Random.value < 0.6f ? C("#ff8a3d") : C("#ffd27a"), FxKit.O(0.8f, 0.5f, 0.32f, spread: rad * 1.2f, up: 2.4f));
                if (a.Has("tachiai", now) && a.grounded && P(0.9f)) fx.Emit(pos + Vector3.up * 0.2f, 2, C("#9c8f7c"), FxKit.O(1.5f, 0.6f, 0.5f, spread: rad * 1.5f, up: 1));
                if (a.Has("taiko", now) && P(0.4f)) fx.Emit(c, 1, C("#ffb35c"), FxKit.O(0.6f, 0.7f, 0.28f, spread: rad * 1.8f, up: 1.2f));
                if ((a.Has("groove", now) || a.Has("tempo", now)) && P(0.35f)) { float t0 = Random.value * 6.28f; fx.Emit(pos + new Vector3(Mathf.Cos(t0) * rad * 1.2f, 0.12f, Mathf.Sin(t0) * rad * 1.2f), 1, a.Has("tempo", now) ? C("#ffd23f") : C("#7dffcf"), FxKit.O(0.3f, 0.6f, 0.22f, up: 1.2f)); }
                if (a.wounds.Count > 0 && P(Mathf.Min(0.9f, 0.25f * a.wounds.Count))) fx.Emit(c + Vector3.up * 0.2f, 1, Random.value < 0.7f ? C("#ff2d55") : C("#9e0f2b"), FxKit.O(0.4f, 0.7f, 0.16f, grav: 6, spread: rad * 0.9f));
                if (a.Has("warcall", now) && P(0.35f)) fx.Emit(pos + Vector3.up * 0.15f, 1, C("#ffd98a"), FxKit.O(0.3f, 0.6f, 0.22f, spread: rad * 1.4f, up: 1.6f));
                if (a.Has("pumped", now) && P(0.25f)) fx.Emit(c, 1, C("#ffd23f"), FxKit.O(0.5f, 0.5f, 0.2f, spread: rad, up: 1));
            }
        }

        // meshes (unit sized; scaled per zone / beam)
        static Mesh Disc()
        {
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>(); var c = new List<Color> { Color.white }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            for (int i = 0; i <= 64; i++) { float a = i * Mathf.PI * 2 / 64; v.Add(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a))); c.Add(Color.white); uv.Add(new Vector2(0.5f, 0.5f)); if (i > 0) { t.Add(0); t.Add(i); t.Add(i + 1); } }
            var m = new Mesh { name = "zone disc" }; m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        static Mesh Ring(float r0, float r1)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>(); var uv = new List<Vector2>();
            for (int i = 0; i <= 64; i++) { float a = i * Mathf.PI * 2 / 64; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); v.Add(d * r0); v.Add(d * r1); c.Add(Color.white); c.Add(Color.white); uv.Add(Vector2.zero); uv.Add(Vector2.one); if (i < 64) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); } }
            var m = new Mesh { name = "zone edge" }; m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        static Mesh Dome()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>(); var uv = new List<Vector2>();
            int S = 32, Rn = 10;
            for (int j = 0; j <= Rn; j++) for (int i = 0; i <= S; i++) { float el = j * Mathf.PI * 0.5f / Rn, a = i * Mathf.PI * 2 / S; v.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(a) * Mathf.Cos(el))); c.Add(Color.white); uv.Add(Vector2.zero); }
            for (int j = 0; j < Rn; j++) for (int i = 0; i < S; i++) { int a = j * (S + 1) + i, b = a + S + 1; t.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 }); }
            var m = new Mesh { name = "zone dome" }; m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        /// <summary>an open cylinder wall, radius 1, height 1 (the Dohyo's wall of light)</summary>
        static Mesh Wall()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>(); var uv = new List<Vector2>();
            for (int i = 0; i <= 72; i++) { float a = i * Mathf.PI * 2 / 72; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); v.Add(d); v.Add(d + Vector3.up); c.Add(Color.white); c.Add(new Color(1, 1, 1, 0.2f)); uv.Add(Vector2.zero); uv.Add(Vector2.one); if (i < 72) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); } }
            var m = new Mesh { name = "zone wall" }; m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        static Mesh Beam() => TubeZ(1, 1, 8);
        static Mesh Cone() => TubeZ(0, 1, 24);
        static Mesh TubeZ(float r0, float r1, int sides)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>(); var uv = new List<Vector2>();
            for (int i = 0; i <= sides; i++) { float a = i * Mathf.PI * 2 / sides; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); v.Add(d * r0); v.Add(d * r1 + Vector3.forward); c.Add(Color.white); c.Add(Color.white); uv.Add(Vector2.zero); uv.Add(Vector2.one); if (i < sides) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); } }
            var m = new Mesh { name = "tube" }; m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }
}
