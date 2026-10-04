// The in-match HUD - src/client/Hud.ts ported element for element to UI Toolkit (the DOM overlay there, a panel layer
// here; zu.uss carries style.css's HUD rules): health / armor / shields, the hero portrait, the ability bar with the
// player's own key labels, the ult ring, ammo, the flight / Mag-Grind gauge, status chips, the objective bar for the
// mode (control, Mikoshi Rush, the point, Stadium, the campaign's boss), the kill feed, counter callouts, damage
// numbers, health bars over heads, the reticle (custom or per weapon) and hit marker, the death banner, the Tab screen
// (Overwatch 2 style), the performance readout and subtitles.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class HudView
    {
        /// <summary>the default reticle per weapon kind (Overwatch 2's stock look, sized for a 1080p HUD: thin lines, a dark
        /// outline, a dot; a ring the size of the weapon's reach). Melee's ring is the cleave at close range; a beam's the
        /// lock-on cone; zoomed sights (Yuzu's Hawk Eye) collapse to the dot.</summary>
        public static ZuSettings.Reticle ReticleFor(string kind, bool zoom = false)
        {
            var b = new ZuSettings.Reticle { type = "circle+crosshairs", color = "#ffffff", thickness = 2, length = 7, gap = 6, opacity = 0.92, outline = 0.8, dot = 4, dotOpacity = 1, accuracy = false };
            if (zoom) { b.type = "dot"; b.dot = 4; b.gap = 0; b.length = 0; return b; }
            if (kind == "melee") { b.type = "circle"; b.gap = 14; b.length = 0; return b; }
            if (kind == "beam") { b.type = "circle"; b.gap = 26; b.length = 0; b.dot = 3; return b; }
            if (kind == "charge") { b.gap = 8; b.length = 8; return b; }                // the bows: a wider ring for the arc
            if (kind == "projectile") { b.gap = 5; b.length = 8; return b; }
            return b;
        }

        /// <summary>hero-specific lines on the Tab screen (Overwatch 2 shows each hero's own numbers)</summary>
        static readonly Dictionary<string, string> HERO_STAT = new Dictionary<string, string> { ["swoops"] = "Starwing Swoops", ["ignites"] = "Enemies Ignited", ["volatile"] = "Volatile Crits", ["roar"] = "Crowd-Roar Health", ["packs"] = "Health Packs Used", ["healAssists"] = "Healing Assists", ["bassdrop"] = "Allies Bass-Dropped" };
        public static readonly Dictionary<string, string> QNAME = new Dictionary<string, string> { ["quickplay"] = "QUICK PLAY", ["competitive"] = "COMPETITIVE", ["practice"] = "AI QUICK MATCH", ["skirmish"] = "PLAY VS AI", ["spectate"] = "WATCH", ["aitest"] = "AI LAB" };

        static readonly (string id, string label, string color)[] STATUS =
        {
            ("stun", "STUNNED", "#ffee58"), ("root", "ROOTED", "#c77dff"), ("silence", "SILENCED", "#ff4d6d"), ("grounded", "GROUNDED", "#ff4d6d"), ("chained", "TRAPPED", "#ffd76a"),
            ("antiheal", "GRIEVOUS HEX", "#b56dff"), ("brand", "ECLIPSE BRAND", "#ff6a2a"), ("tethered", "STRUNG", "#c77dff"), ("linked", "LINKED", "#bfe8ff"), ("ccimmune", "PURIFIED", "#ffd76a"),
            ("stealth", "VEILED", "#9d7bff"), ("revealed", "REVEALED", "#ffd27a"), ("sealed", "SEALED", "#ffe28a"), ("undying", "SANCTUARY", "#ffe28a"), ("dmgamp", "NOVA +30%", "#bfe8ff"),
            ("vuln", "PUPPETED +30%", "#c77dff"), ("judgment", "RAIJIN'S JUDGMENT", "#8ad8ff"), ("asura", "ASURA", "#ff6a2a"), ("lifesteal", "LIFESTEAL", "#ff2d55"), ("burning", "BURNING", "#ff8a3d"),
            ("tachiai", "UNSTOPPABLE", "#34d1bf"), ("taiko", "TAIKO HEARTBEAT", "#ffb35c"), ("dohyo", "GRAND DOHYO", "#ffe6a8"), ("tempo", "TEMPO RUSH", "#ffd23f"), ("groove", "HEALING GROOVE", "#7dffcf"),
            ("amp", "MAX VOLUME", "#39d6ff"), ("pumped", "PUMPED", "#ffd23f"), ("grinding", "MAG-GRIND", "#9ef6ff"), ("wound", "WOUNDED", "#ff2d55"), ("warcall", "WAR CALL", "#ffd98a"),
            ("tideult", "UNSTOPPABLE", "#5ff2e0"), ("tidemark", "CRESCENT MARK +20%", "#4aa8ff"), ("reborn", "REBORN", "#ffe9a8"), ("tithe", "LIFE TITHE +20/s", "#c77dff"), ("sovereign", "STORM SOVEREIGN", "#8ad8ff"),
        };
        static readonly string[] OB_ICONS = { "stun", "root", "silence", "antiheal", "brand", "tethered", "linked", "sealed" };
        static readonly Dictionary<string, string> OB_ICON_COL = new Dictionary<string, string> { ["stun"] = "#ffee58", ["root"] = "#c77dff", ["tethered"] = "#c77dff", ["silence"] = "#ff4d6d", ["antiheal"] = "#7b2cbf", ["brand"] = "#ff6a2a", ["linked"] = "#bfe8ff", ["sealed"] = "#ffe28a" };

        static readonly Color Z = Grad.C("#5cc8ff"), UM = Grad.C("#ff3b5c"), GOLD = Grad.C("#ffd76a");
        /// <summary>.co: linear-gradient(90deg, transparent, rgba(0,0,0,.75), transparent)</summary>
        static Texture2D CO_BG => Grad.Linear(90, (new Color(0, 0, 0, 0), 0), (new Color(0, 0, 0, 0.75f), 50), (new Color(0, 0, 0, 0), 100));

        public readonly VisualElement root;
        readonly VisualElement hp, abil, ultEl, ammoBox, flight, obj, feed, callout, banner, nums, bars, status, board, portrait, subs;
        readonly Label fps, ammo, hpNum;
        readonly ZU.Engine.FrameGraphElement graph = new ZU.Engine.FrameGraphElement();   // the advanced overlay's frame-time graph (TS FrameGraph)
        readonly HpBar hpBar;
        readonly ReticleEl cross;
        readonly HitMarkEl hitmark;
        readonly Vignette hurt;
        readonly UltRing ring;
        readonly Label ultV, ultN, bannerB, bannerS;
        ZuSettings opt;
        readonly Dictionary<string, (SkewFill box, Label k, Label n, bool counter)> abilEls = new Dictionary<string, (SkewFill, Label, Label, bool)>();
        readonly Dictionary<int, (VisualElement e, Label name, VisualElement bar, VisualElement fill, VisualElement sh)> barEls = new Dictionary<int, (VisualElement, Label, VisualElement, VisualElement, VisualElement)>();
        readonly List<(VisualElement e, double born, Vector3 pos)> numPool = new List<(VisualElement, double, Vector3)>();
        string lastHero = "", lastKind = "", reticleKey = "";
        bool zoomed, ultWasReady;
        float hitUntil, hurtUntil, hurtAt;
        public Action OnUltReady;

        public HudView(VisualElement parent)
        {
            root = U.Div("hud", parent);
            bars = U.Div("bars", root);
            nums = U.Div("nums", root);
            cross = new ReticleEl(); cross.AddToClassList("cross"); root.Add(cross);
            hitmark = new HitMarkEl(); hitmark.AddToClassList("hitmark"); root.Add(hitmark);
            hp = U.Div("hp", root);
            hpBar = new HpBar(); hpBar.AddToClassList("bar"); hp.Add(hpBar);
            hpNum = U.Txt("", "num", hp);
            portrait = U.Div("portrait", root);
            abil = U.Div("abil", root);
            ultEl = U.Div("ult", root);
            ring = new UltRing(); ring.AddToClassList("ring"); ultEl.Add(ring);
            ultV = U.Txt("", "v", ultEl); ultN = U.Txt("", "n", ultEl);
            ammoBox = U.Div("ammo", root); ammo = U.Txt("", "ammo-t", ammoBox);
            flight = U.Div("flight", root);
            obj = U.Div("obj", root);
            feed = U.Div("feed", root);
            callout = U.Div("callout", root);
            banner = U.Div("banner", root); bannerB = U.Txt("", "b", banner); bannerS = U.Txt("", "s", banner);
            status = U.Div("status", root);
            board = U.Div("board", root);
            fps = U.Txt("", "fps", root);
            graph.style.position = Position.Absolute; graph.style.left = 10; graph.style.top = 150; graph.style.display = DisplayStyle.None; root.Add(graph);
            subs = U.Div("subs", root);
            hurt = new Vignette(); hurt.AddToClassList("fill"); hurt.AddToClassList("hurtfx"); root.Add(hurt);
            U.Show(hurt, false); U.Show(board, false); U.Show(banner, false);
            ApplySettings(ZuSettings.Current);
        }

        public void Show(bool v) => U.Show(root, v);

        /// <summary>Settings > Gameplay / Accessibility / Controls (reticle)</summary>
        public void ApplySettings(ZuSettings s)
        {
            opt = s;
            lastHero = "";                  // rebuild the ability bar: its key labels follow the bindings
            var g = s.gameplay;
            // (zoom in the TS: here a scale about the corner each block is anchored to)
            foreach (var e in new[] { hp, portrait, abil, ultEl, ammoBox, flight, obj, feed, status, callout })
            {
                e.style.scale = new Scale(new Vector3((float)g.hudScale, (float)g.hudScale, 1));
                e.style.opacity = (float)g.hudOpacity;
            }
            U.Toggle(root, "nofeed", !g.killFeed); U.Toggle(root, "nonums", !g.damageNumbers);
            U.Toggle(root, "nohit", !g.hitmarkers); U.Toggle(root, "nocounter", !g.counterCallouts);
            reticleKey = "";
            DrawReticle();
        }

        /// <summary>the reticle: the player's custom design (Settings > Controls > Reticle), or the default - an Overwatch-style
        /// circle-and-crosshairs with a centre dot, in the same place in first and third person; per weapon: melee / beam
        /// heroes get the circle alone (a reach, not a point), a zoomed sight a dot</summary>
        void DrawReticle()
        {
            var R = opt?.controls.reticle;
            bool custom = R != null && R.type != "default";
            string kind = string.IsNullOrEmpty(lastKind) ? "hitscan" : lastKind;
            string key = custom ? Newtonsoft.Json.JsonConvert.SerializeObject(R) : kind + ":" + zoomed;
            if (key == reticleKey) return;
            reticleKey = key;
            cross.Set(custom ? R : ReticleFor(kind, zoomed));
        }

        /// <summary>the ability bar shows the keys the player actually bound (their hero's own set first)</summary>
        string KeyLabel(string k, string hero)
        {
            string a = k == "RMB" ? "alt" : k == "SHIFT" ? "a1" : k == "E" ? "a2" : k == "C" ? "melee" : k == "Q" ? "ult" : null;
            if (opt == null || a == null) return k;
            var b = ZuSettings.BindsFor(opt, hero, a);
            return ZuSettings.KeyShort(b.Count > 0 ? b[0] : null);
        }

        /// <summary>the advanced performance overlay (null = off / simple)</summary>
        public void Perf(string[] lines)
        {
            U.Toggle(fps, "adv", lines != null);
            graph.style.display = lines != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (lines != null) U.Set(fps, string.Join("\n", lines));
        }

        /// <summary>a voice line's subtitle (Accessibility > Subtitles filters by category)</summary>
        public void Subtitle(string name, string color, string text, string cat, float secs)
        {
            string lv = opt?.access.subtitles ?? "critical";
            bool show = lv == "all" || (lv == "conversations" && cat != "exert" && cat != "pain") || (lv == "critical" && (cat == "critical" || cat == "announcer"));
            if (!show || string.IsNullOrEmpty(text)) return;
            var d = U.Div("sub", subs);
            d.style.backgroundColor = new Color(0, 0, 0, (float)(opt?.access.subBg ?? 0.5));
            var l = U.Txt((string.IsNullOrEmpty(name) ? "" : $"<color={color}>{name}:</color>  ") + text, "sub-t", d);
            l.style.fontSize = 20 * (float)(opt?.access.subSize ?? 1);
            while (subs.childCount > 3) subs.RemoveAt(0);
            d.schedule.Execute(() => { d.style.opacity = 0; d.schedule.Execute(() => d.RemoveFromHierarchy()).StartingIn(300); }).StartingIn((long)(Mathf.Max(1.6f, secs) * 1000));
        }

        void BuildAbilities(Actor a)
        {
            abil.Clear(); abilEls.Clear();
            var S = a.def.secondary;
            bool sAb = S != null && S.IsAbility;
            var list = new (string key, SlotDef def, string name)[]
            {
                ("RMB", sAb ? S : null, sAb ? S.name : (S != null && S.heal ? "Heal" : "Alt fire")),
                ("SHIFT", a.def.ability1, a.def.ability1?.name), ("E", a.def.ability2, a.def.ability2?.name), ("C", null, "Melee"),
            };
            foreach (var (key, def, name) in list)
            {
                bool counter = !string.IsNullOrEmpty(def?.counter);
                var b = new SkewFill { vertical = true };
                b.AddToClassList("ab"); if (counter) b.AddToClassList("counter");
                abil.Add(b);
                var k = U.Txt(KeyLabel(key, a.def.id), "k", b); var n = U.Txt(name ?? "", "n", b);
                if (counter) b.tooltip = def.counter;
                abilEls[key] = (b, k, n, counter);
            }
            portrait.Clear();
            var col = U.Hex(a.def.color);
            var img = U.Pic("portrait_" + a.def.id, "pimg", portrait);
            img.style.borderTopColor = img.style.borderBottomColor = img.style.borderLeftColor = img.style.borderRightColor = col;
            var txt = U.Div("ptxt", portrait);
            U.Txt(a.def.name, "pname", txt).style.color = col;
            U.Txt(a.def.title + (a.def.pilot != null ? $" · pilot {a.def.pilot.name}" : ""), "ptitle", txt);
            lastKind = a.def.primary?.kind ?? "";
            DrawReticle();
        }

        static void SetBorder(VisualElement e, Color c) { e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c; }

        public void Update(World w, Actor me, Camera cam, double now, float fpsAvg, bool showBoard, string spectating)
        {
            double t = w.time;
            U.Set(fps, fpsAvg > 0 && !fps.ClassListContains("adv") ? $"{fpsAvg:0} FPS" : fps.ClassListContains("adv") ? fps.text : "");
            if (me != null)
            {
                if (lastHero != me.def.id) { lastHero = me.def.id; BuildAbilities(me); }
                double max = me.MaxHp;
                hpBar.Set(me.hp, me.armor, me.ShieldAmt, max);
                U.Set(hpNum, $"{Math.Ceiling(me.Health):0}<size=16><alpha=#99>/{max:0}</alpha></size>{(me.ShieldAmt > 1 ? $" <size=20><color=#7fd3ff>+{Math.Ceiling(me.ShieldAmt):0}</color></size>" : "")}");
                var S = me.def.secondary;
                var cds = new List<(string k, string id)> { ("SHIFT", me.def.ability1?.id), ("E", me.def.ability2?.id) };
                if (S != null && S.IsAbility) cds.Add(("RMB", S.id));
                bool silenced = me.Has("silence", t);
                foreach (var (k, id) in cds)
                {
                    if (id == null || !abilEls.TryGetValue(k, out var e)) continue;
                    double left = me.CdLeft(id, t);
                    var def = k == "SHIFT" ? me.def.ability1 : k == "E" ? me.def.ability2 : S;
                    e.box.SetFrac(left > 0 ? (float)(left / Math.Max(0.1, def.cooldown)) : 0);
                    U.Toggle(e.box, "ready", left <= 0);
                    SetSilenced(e.box, silenced);
                    U.Set(e.k, left > 0 ? U.F(left, left < 3 ? 1 : 0) : KeyLabel(k, me.def.id));
                    // Tomoe: while the Crescent Fang is out, RMB calls it back (no cooldown shown until it's caught)
                    if (id == "crescent" && me.sv.TryGetValue("fang", out var fang) && fang != 0)
                    {
                        bool stuck = fang == 2 || fang == 3;
                        e.box.SetFrac(0);
                        U.Toggle(e.box, "ready", stuck);
                        U.Set(e.k, stuck ? "RECALL" : "···");
                    }
                }
                {
                    // quick melee cooldown
                    var e = abilEls["C"]; double left = Math.Max(0, me.nextMelee - t);
                    e.box.SetFrac((float)(left / Weapons.QUICK_MELEE_COOLDOWN));
                    U.Toggle(e.box, "ready", left <= 0);
                }
                if (me.def.id == "tenkai" && abilEls.TryGetValue("RMB", out var rb) && me.barrier.max > 0)
                {
                    rb.box.SetFrac((float)(1 - me.barrier.hp / me.barrier.max));
                    U.Toggle(rb.box, "ready", me.barrier.hp > 100 && t > me.barrier.brokenUntil);
                }
                double u = me.def.ult != null && me.def.ult.charge > 0 ? me.ult / me.def.ult.charge : 0;
                bool ready = u >= 1;
                double titan = me.Has("titan", t) ? me.St("titan") - t : 0;
                bool pulse = (Time.unscaledTime % 1f) < 0.5f;
                if (titan > 0)
                {
                    // giant form running: the ring drains over the form's length
                    ring.Set((float)(titan / Abilities.TITAN_SECS * 100), Grad.C("#ffb347"), false);
                    U.Set(ultV, $"{Math.Ceiling(titan):0}s"); U.Set(ultN, "GIANT FORM");
                }
                else
                {
                    ring.Set((float)(u * 100), GOLD, ready);
                    U.Set(ultV, ready ? KeyLabel("Q", me.def.id) : Math.Floor(u * 100) + "%"); U.Set(ultN, me.def.ult?.name ?? "");
                }
                // .ult.ready .ring { animation: pulse 1s infinite } (opacity 1 -> .55 at 50%)
                ring.style.opacity = ready && titan <= 0 ? 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI)) : 1f;
                U.Toggle(ultEl, "active", titan > 0);
                U.Toggle(ultEl, "ready", ready);
                if (ready && !ultWasReady) OnUltReady?.Invoke();
                ultWasReady = ready;
                var P = me.def.primary;
                string am;
                if (P.kind == "charge") am = me.charging ? Math.Round(me.charge * 100) + "%" : "DRAW";
                // twin chainguns: left drum | right drum (endless inside the Grand Dohyo)
                else if (me.def.dualGuns) am = me.Has("dohyo", t) ? "∞<size=18><alpha=#99> | </alpha></size>∞" : me.reloadUntil > 0 ? "RELOADING" : $"{me.ammo:0}<size=18><alpha=#99> | </alpha></size>{me.Sv("ammo2"):0}";
                else if (P.ammo.HasValue && P.ammo.Value > 0) am = me.reloadUntil > 0 ? "RELOADING" : $"{me.ammo:0}<size=18><alpha=#99>/{me.MaxAmmo:0}</alpha></size>";
                else am = "∞";
                U.Set(ammo, am);
                Flight(me, t);
                // status chips
                var on = STATUS.Where(x => me.Has(x.id, t)).ToList();
                string sig = string.Join(",", on.Select(x => x.id));
                if ((string)status.userData != sig)
                {
                    status.userData = sig; status.Clear();
                    foreach (var (_, n, c) in on) { var chip = U.Txt(n, "chip", status); var cc = U.Hex(c); chip.style.color = cc; SetBorder(chip, cc); }
                }
                U.Toggle(root, "dead", !me.alive);
                U.Show(banner, !me.alive);
                if (!me.alive) { U.Set(bannerB, "ELIMINATED"); U.Show(bannerB, true); U.Set(bannerS, $"Respawn in {Math.Max(0, me.respawnAt - t):0.0}s"); }
                U.Show(cross, me.alive);
                bool z = me.Sv("zoom") != 0;
                if (zoomed != z) { zoomed = z; DrawReticle(); }
                U.Toggle(root, "spectate", false);
            }
            else
            {
                U.Toggle(root, "spectate", true);
                U.Show(banner, !string.IsNullOrEmpty(spectating));
                U.Show(bannerB, false); U.Set(bannerS, spectating ?? "");
            }
            Objective(w, me, t);
            Overheads(w, me, cam, t);
            Numbers(cam, now);
            // hit marker (120 ms) and the hurt flash (150 ms, with the HUD shake)
            float rt = Time.unscaledTime;
            hitmark.style.opacity = rt < hitUntil ? 1 : 0;
            bool hurting = rt < hurtUntil;
            U.Show(hurt, hurting);
            if (hurting)
            {
                // @keyframes hurtshake: 25% (+3, -2), 75% (-3, +2) times Accessibility > Screen Shake
                float k = (rt - hurtAt) / 0.15f, s = (float)(opt?.access.hudShake ?? 1);
                float dx = k < 0.5f ? Mathf.Lerp(0, 3, Mathf.PingPong(k * 4, 1)) : Mathf.Lerp(0, -3, Mathf.PingPong((k - 0.5f) * 4, 1));
                root.style.translate = new Translate(dx * s, -dx * 2 / 3 * s);
            }
            else root.style.translate = new Translate(0, 0);
            // scoreboard
            bool showB = showBoard || !string.IsNullOrEmpty(w.winner);
            U.Show(board, showB);
            board.AddToClassList("ow2");
            if (showB && (rt >= boardNext || !boardWasShown)) { boardNext = rt + 0.25f; TabScreen(w, me); }
            boardWasShown = showB;
        }
        float boardNext; bool boardWasShown;

        void SetSilenced(VisualElement e, bool s)
        {
            if (e.ClassListContains("silenced") == s) return;
            U.Toggle(e, "silenced", s);
            // .hud .ab.silenced { filter: grayscale(1) brightness(.5) }
            if (s) Filters.Set(e, Filters.Grayscale(1), Filters.Brightness(0.5f)); else Filters.Set(e);
        }

        // ------------------------------------------------------------------ flight / Mag-Grind gauge
        void Flight(Actor me, double t)
        {
            bool flies = me.def.frame == "flyer" || me.def.jets.HasValue;
            bool dj = me.def.id == "hibiki";
            U.Show(flight, flies || dj);
            if (!flies && !dj) return;
            string sig;
            // Hibiki: the Mag-Grind charge (5s of grinding pumps the next Scratch Wave) and the track he's playing
            if (dj)
            {
                bool pumped = me.Has("pumped", t); double g = pumped ? 100 : Math.Min(100, me.Sv("grind") / 5 * 100);
                bool tempo = me.Sv("track") != 0;
                string tr = tempo ? "TEMPO RUSH" : "HEALING GROOVE", tc = tempo ? "#ffd23f" : "#7dffcf";
                double gv = me.Sv("rhythm", 1);
                sig = $"dj|{g:0}|{pumped}|{tr}|{me.Has("amp", t)}|{(gv > 1.15 ? gv.ToString(gv < 10 ? "0.0" : "0") : "")}";
                if ((string)flight.userData == sig) return;
                flight.userData = sig; flight.Clear();
                FlightBar(g, pumped ? "#ffd23f" : "#9ef6ff");
                U.Txt(pumped ? "PUMPED" : "MAG-GRIND", "fl-t", flight);
                Chip(tr + (me.Has("amp", t) ? " · MAX" : ""), tc);
                // the Groove: tap jump in rhythm to build speed - the chip heats from teal to gold to magenta
                if (gv > 1.15)
                {
                    double hue = gv < 8 ? 165 - (gv - 1) / 7 * 120 : 45 - Math.Min(1, (gv - 8) / 12) * 75;
                    Chip($"GROOVE ×{gv.ToString(gv < 10 ? "0.0" : "0")}", U.Css(Color.HSVToRGB((float)(((hue % 360) + 360) % 360 / 360), 1, 1) * 0.65f + Color.white * 0.35f));
                }
                return;
            }
            // Mirei: the swoop's cooldown sits under the flight gauge (F)
            string swoop = me.def.id == "mirei" ? (me.Has("swoop", t) ? "SWOOP" : me.CdLeft("swoop", t) > 0 ? $"F {me.CdLeft("swoop", t):0.0}" : "F SWOOP") : "";
            string lab = me.Has("grounded", t) ? "GROUNDED" : me.def.jets.HasValue ? "THRUSTERS" : "FLIGHT";
            sig = $"f|{me.flight:0}|{lab}|{swoop}|{me.Ready("swoop", t)}";
            if ((string)flight.userData == sig) return;
            flight.userData = sig; flight.Clear();
            FlightBar(me.flight, null);
            U.Txt(lab, "fl-t", flight);
            if (swoop != "") { var s = U.Txt(swoop, "sw", flight); U.Toggle(s, "on", me.Ready("swoop", t)); }
        }
        void FlightBar(double pct, string col)
        {
            var fb = U.Div("fb", flight); var i = U.Div("fbi", fb);
            i.style.height = new Length((float)Math.Max(0, Math.Min(100, pct)), LengthUnit.Percent);
            if (col != null) i.style.backgroundColor = U.Hex(col);
        }
        void Chip(string text, string col)
        {
            var s = U.Txt(text, "sw on", flight); var c = U.Hex(col);
            s.style.color = c; SetBorder(s, c);
        }

        // ------------------------------------------------------------------ objective
        string objKind = "";
        readonly Dictionary<string, VisualElement> o = new Dictionary<string, VisualElement>();
        Label OL(string k) => (Label)o[k];

        void ObjLayout(string kind, Action build)
        {
            if (objKind == kind) return;
            objKind = kind; obj.Clear(); o.Clear();
            build();
        }

        VisualElement Pips(string key, int n)
        {
            var p = U.Div("pips " + key, obj);
            for (int i = 0; i < n; i++) U.Div("pip", p);
            o[key] = p;
            return p;
        }
        void SetPips(string key, int wins, string cls)
        {
            var p = o[key]; int i = 0;
            foreach (var c in p.Children()) { U.Toggle(c, cls, i < wins); i++; }
        }
        void Side(string key, string cls)
        {
            var s = new SkewFill(); s.AddToClassList("side"); s.AddToClassList(cls);
            s.barColor = cls == "us" ? AllyColor : EnemyColor;
            obj.Add(s); o[key] = s;
            o[key + "b"] = U.Txt("", "sb", s);
        }
        void SetSide(string key, double pct)
        {
            var s = (SkewFill)o[key]; s.SetFrac((float)(pct / 100));
            U.Set(OL(key + "b"), $"{pct:0}%");
        }
        void Mid(VisualElement parent = null)
        {
            var m = U.Div("mid", parent ?? obj); o["mid"] = m;
            o["midT"] = U.Txt("", "mid-t", m); o["midS"] = U.Txt("", "mid-s", m);
        }
        void SetMid(string head, string sub, string state)
        {
            U.Set(OL("midT"), head); U.Set(OL("midS"), sub);
            var m = o["mid"];
            U.Toggle(m, "us", state == "us"); U.Toggle(m, "them", state == "them"); U.Toggle(m, "ot", state == "ot");
            if (state == "ot") m.style.opacity = 0.6f + 0.4f * Mathf.PingPong(Time.unscaledTime * 2, 1);   // pulse .5s alternate
            else m.style.opacity = StyleKeyword.Null;
        }

        void Objective(World w, Actor me, double t)
        {
            string my = me?.team ?? "zenith", them = my == "zenith" ? "umbra" : "zenith";
            if (w.mode == "campaign" && w.director is Director dir)
            {
                var b = dir.boss != null && dir.boss.alive ? dir.boss : null;
                if (b != null)
                {
                    ObjLayout("boss:" + b.def.id, () =>
                    {
                        var bb = U.Div("bossbar", obj); var c = U.Hex(b.def.glow);
                        var name = U.Txt(b.def.name, "bb-n", bb); name.style.color = c;
                        U.Txt(b.def.title ?? "", "bb-t", bb);
                        var bar = U.Div("bb-bar", bb); SetBorder(bar, c);
                        var fill = U.Div("bb-fill", bar); Grad.Set(fill, Grad.Linear(90, (c, 0), (Color.white, 100)));
                        o["bbFill"] = fill;
                        U.Txt("Weak point: " + (b.def.weak ?? ""), "bb-w", bb);
                    });
                    o["bbFill"].style.width = new Length((float)(b.Health / b.MaxHp * 100), LengthUnit.Percent);
                }
                else
                {
                    ObjLayout("camp", () => Mid());
                    SetMid(U.Up(dir.level.name), dir.objective, "");
                }
            }
            else if (w.stadium != null)
            {
                // Stadium: round pips (first to 4), the point, the round clock, cash
                var S = w.stadium; var P = w.point;
                ObjLayout("stadium", () => { Pips("pu", Stadium.ROUNDS_TO_WIN); Side("su", "us"); Mid(); Side("st", "them"); Pips("pt", Stadium.ROUNDS_TO_WIN); });
                SetPips("pu", S.wins[my], "z"); SetPips("pt", S.wins[them], "u");
                double unlock = Math.Max(0, P.unlockAt - t);
                string capTxt = S.phase == "armory" ? $"ARMORY {Math.Max(0, Math.Ceiling(S.phaseEnd - t)):0}s" : P.contested ? "CONTESTED" : P.capTeam != null ? $"{(P.capTeam == my ? "CAPTURING" : "LOSING")} {P.capture:0}%" : P.owner != null ? (P.owner == my ? "HOLDING" : "ENEMY HOLDS") : unlock > 0 ? $"POINT OPENS {unlock:0}" : "NEUTRAL";
                SetSide("su", P.progress[my]); SetSide("st", P.progress[them]);
                SetMid($"STADIUM · ROUND {S.round}", capTxt + (S.phase == "fight" ? $" · {U.Clock(Math.Max(0, Stadium.ROUND_SECS - (t - S.roundStart)))}" : "") + (me != null ? $" · ${U.N(me.cash)}" : ""),
                    P.owner != null ? (P.owner == my ? "us" : "them") : "");
            }
            else if (w.rules == "control")
            {
                // Control (best of 3): round pips, the point's state, each team's percentage, overtime
                var P = w.point; var C = w.control;
                ObjLayout("control", () => { Pips("pu", World.ROUNDS_TO_WIN); Side("su", "us"); Mid(); Side("st", "them"); Pips("pt", World.ROUNDS_TO_WIN); });
                SetPips("pu", C.wins[my], "z"); SetPips("pt", C.wins[them], "u");
                double unlock = Math.Max(0, P.unlockAt - t);
                string st = C.phase == "intermission" ? $"ROUND {C.round + 1} IN {Math.Max(0, Math.Ceiling(C.phaseEnd - t)):0}" : C.overtime ? "OVERTIME" : unlock > 0 ? $"POINT OPENS {unlock:0}" : P.contested ? "CONTESTED" : P.capTeam != null ? $"{(P.capTeam == my ? "CAPTURING" : "LOSING")} {P.capture:0}%" : P.owner != null ? (P.owner == my ? "HOLDING" : "ENEMY HOLDS") : "NEUTRAL";
                SetSide("su", P.progress[my]); SetSide("st", P.progress[them]);
                SetMid($"ROUND {C.round}", st, C.overtime ? "ot" : P.owner != null ? (P.owner == my ? "us" : "them") : "");
            }
            else if (w.rules == "push")
            {
                // Mikoshi Rush: the route with the float on it, each team's furthest push, who is moving it, the clock
                var M = w.push;
                ObjLayout("push", () =>
                {
                    var p = U.Div("push", obj); var trk = U.Div("trk", p);
                    U.Div("mk c", trk); o["bu"] = U.Div("mk bu", trk); o["bt"] = U.Div("mk bt", trk); o["fl"] = U.Div("fl", trk);
                    Mid(p);
                });
                double toward(string team) => team == "zenith" ? 1 : -1;         // + = toward the Umbra end
                double x(double d) => 50 + d / Math.Max(1, M.half) * 50 * toward(my);   // our goal on the right
                double unlock = Math.Max(0, M.unlockAt - t);
                string st = unlock > 0 ? $"THE MIKOSHI RISES IN {unlock:0}" : M.overtime ? "OVERTIME" : M.contested ? "CONTESTED" : M.owner != null ? (M.owner == my ? "YOUR TEAM PUSHES" : "ENEMY PUSHES") : "STANDING STILL";
                o["bu"].style.left = new Length((float)x(M.best[my] * toward(my)), LengthUnit.Percent);
                o["bt"].style.left = new Length((float)x(-M.best[them] * toward(my)), LengthUnit.Percent);
                var fl = o["fl"]; fl.style.left = new Length((float)x(M.d), LengthUnit.Percent);
                U.Toggle(fl, "con", M.contested); U.Toggle(fl, "us", !M.contested && M.owner == my); U.Toggle(fl, "them", !M.contested && M.owner != null && M.owner != my);
                SetMid("MIKOSHI RUSH", $"{st} · {U.Clock(Math.Max(0, w.timeLimit - t))} · YOU {Math.Round(M.best[my])}m / THEM {Math.Round(M.best[them])}m", "");
            }
            else if (w.mode != "training")
            {
                var P = w.point;
                ObjLayout("point", () => { Side("su", "us"); Mid(); Side("st", "them"); });
                double unlock = Math.Max(0, P.unlockAt - t);
                string capTxt = P.contested ? "CONTESTED" : P.capTeam != null ? $"{(P.capTeam == my ? "CAPTURING" : "LOSING")} {P.capture:0}%" : P.owner != null ? (P.owner == my ? "HOLDING" : "ENEMY HOLDS") : "NEUTRAL";
                SetSide("su", P.progress[my]); SetSide("st", P.progress[my == "zenith" ? "umbra" : "zenith"]);
                SetMid(unlock > 0 ? $"POINT OPENS {unlock:0}" : capTxt, U.Clock(Math.Max(0, w.timeLimit - t)), P.owner != null ? (P.owner == my ? "us" : "them") : "");
            }
            else
            {
                ObjLayout("training", () => Mid());
                SetMid("TRAINING GROUNDS", $"H: switch hero{(w.full ? " · G: hero range & spar · V: first / third person" : "")} · Esc: menu", "");
            }
        }

        Color AllyColor => U.Hex(opt?.access.allyColor ?? "#5cc8ff");
        Color EnemyColor => U.Hex(opt?.access.enemyColor ?? "#ff3b5c");

        // ------------------------------------------------------------------ overhead bars (enemies + allies), projected
        readonly HashSet<int> seen = new HashSet<int>();
        void Overheads(World w, Actor me, Camera cam, double t)
        {
            seen.Clear();
            var ui = UiRoot.Get();
            if (cam != null)
                foreach (var a in w.actors)
                {
                    if (!a.alive || a == me || a.IsSummon) continue;      // (no bar over each of fifty puppets)
                    if (me != null && a.team != me.team && a.Has("stealth", t) && !a.Has("revealed", t)) continue;
                    var head = Conv.U(a.pos) + Vector3.up * ((float)a.Height + 0.35f);
                    if (!ui.WorldToCanvas(cam, head, out var p)) continue;
                    var vp = cam.WorldToViewportPoint(head);
                    if (Mathf.Abs(vp.x * 2 - 1) > 1.1f || Mathf.Abs(vp.y * 2 - 1) > 1.1f) continue;
                    float d = Vector3.Distance(cam.transform.position, Conv.U(a.pos));
                    if (d > 60) continue;
                    seen.Add(a.id);
                    if (!barEls.TryGetValue(a.id, out var b))
                    {
                        var e = U.Div("ob", bars);
                        var name = U.Txt("", "ob-n", e);
                        var bar = U.Div("ob-bar", e); var fill = U.Div("ob-fill", bar); var sh = U.Div("ob-sh", bar);
                        barEls[a.id] = b = (e, name, bar, fill, sh);
                    }
                    bool enemy = me == null || a.team != me.team;
                    var G = opt?.gameplay;
                    bool showBar = G == null || (enemy ? G.enemyBars : G.allyBars), showName = G == null || G.nameTags;
                    bool ally = a.team == (me?.team ?? "zenith");
                    U.Toggle(b.e, "ally", ally); U.Toggle(b.e, "enemy", !ally);
                    U.Show(b.e, showBar || showName); U.Show(b.bar, showBar); U.Show(b.name, showName);
                    b.e.style.translate = new Translate(p.x, p.y);
                    float sc = Mathf.Max(0.55f, Mathf.Min(1, 14 / d));
                    b.e.style.scale = new Scale(new Vector3(sc, sc, 1));
                    string icons = "";
                    foreach (var s in OB_ICONS) if (a.Has(s, t)) icons += $" <color={OB_ICON_COL[s]}>●</color>";
                    U.Set(b.name, a.def.name + icons);
                    b.fill.style.width = new Length((float)Math.Max(0, a.Health / a.MaxHp * 100), LengthUnit.Percent);
                    b.fill.style.backgroundColor = ally ? AllyColor : EnemyColor;
                    bool shield = a.ShieldAmt > 1;
                    U.Show(b.sh, shield);
                    if (shield) b.sh.style.width = new Length((float)Math.Min(100, a.ShieldAmt / a.MaxHp * 100), LengthUnit.Percent);
                }
            if (barEls.Count > seen.Count)
                foreach (var id in barEls.Keys.ToList())
                    if (!seen.Contains(id)) { barEls[id].e.RemoveFromHierarchy(); barEls.Remove(id); }
        }

        // ------------------------------------------------------------------ floating numbers
        void Numbers(Camera cam, double now)
        {
            var ui = UiRoot.Get();
            for (int i = numPool.Count - 1; i >= 0; i--)
            {
                var n = numPool[i];
                float k = (float)((now - n.born) / 0.9);
                if (k >= 1 || k < 0) { n.e.RemoveFromHierarchy(); numPool.RemoveAt(i); continue; }
                if (!ui.WorldToCanvas(cam, n.pos + Vector3.up * k * 1.2f, out var p)) { U.Show(n.e, false); continue; }
                U.Show(n.e, true);
                n.e.style.translate = new Translate(p.x, p.y);
                n.e.style.opacity = 1 - k * k;
            }
        }

        // ------------------------------------------------------------------ Tab (Overwatch 2 style)
        /// <summary>both teams' E / A / D / DMG / H / MIT, your hero's numbers (accuracy first)</summary>
        void TabScreen(World w, Actor me)
        {
            board.Clear();
            string my = me?.team ?? "zenith", them = my == "zenith" ? "umbra" : "zenith"; double t = w.time;
            if (!string.IsNullOrEmpty(w.winner)) U.Txt(w.winner == my ? "VICTORY" : "DEFEAT", "h2 " + (w.winner == my ? "win" : "loss"), board);
            string score = w.rules == "push" ? $"{Math.Round(w.push.best[my])}m - {Math.Round(w.push.best[them])}m" : w.rules == "control" ? $"{w.control.wins[my]} - {w.control.wins[them]}" : $"{w.point.progress[my]:0}% - {w.point.progress[them]:0}%";
            var hdr = U.Div("hdr", board);
            U.Txt(QNAME.TryGetValue(w.mode ?? "", out var qn) ? qn : U.Up(w.mode), "hdr-b", hdr);
            U.Txt($"{w.map.name} · {(w.rules == "push" ? "MIKOSHI RUSH" : "CONTROL")}{(w.rules == "control" ? $" · ROUND {w.control.round}" : "")} · {score}", "hdr-s", hdr);
            U.Txt(U.Clock(t), "hdr-s", hdr);
            var teams = U.Div("teams", board);
            Table(teams, w, me, my, my == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE", true);
            Table(teams, w, me, them, them == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE", false);
            if (me == null) return;
            string Pct(double a, double b) => b > 0 ? $"{Math.Round(a / b * 100)}%" : "-";
            var tiles = new List<(string, string)>
            {
                ("Weapon Accuracy", Pct(me.hits, me.shots)), ("Critical Hit Accuracy", Pct(me.crits, me.hits)), ("Eliminations", (me.kills + me.assists).ToString()), ("Final Blows", me.kills.ToString()),
                ("Objective Time", U.Clock(me.objTime)), ("Damage Mitigated", U.N(me.mitigated)), ("Best Kill Streak", me.bestStreak.ToString()), ("Ultimates Used", me.ults.ToString()),
            };
            foreach (var kv in me.stats) if (HERO_STAT.TryGetValue(kv.Key, out var label) && kv.Value > 0) tiles.Add((label, U.N(kv.Value)));
            var mine = U.Div("mine", board);
            var who = U.Div("who", mine);
            U.Pic("portrait_" + me.def.id, "who-img", who);
            U.Txt(me.def.name, "who-b", who); U.Txt(me.def.title, "who-s", who);
            var tl = U.Div("tiles", mine);
            foreach (var (k, v) in tiles) { var d = U.Div("tile", tl); U.Txt(k, "tile-k", d); U.Txt(v, "tile-v", d); }
            U.Txt($"{U.N(me.hits)} of {U.N(me.shots)} shots hit", "shots", mine);
        }

        static readonly string[] HEADS = { "", "E", "A", "D", "DMG", "H", "MIT" };
        static void Table(VisualElement parent, World w, Actor me, string team, string title, bool mine)
        {
            var tm = U.Div("tm " + (mine ? "mine" : "enemy"), parent);
            U.Txt(title, "h3", tm);
            // the rows sit in one column: `.board .mine` (the player panel's row layout) also matches the team table "tm mine"
            // - as in the web game, where it puts the team name beside its <table> - and laid the loose rows side by side
            tm = U.Div("tbl", tm);
            var head = U.Div("tr th", tm);
            for (int i = 0; i < HEADS.Length; i++) U.Txt(HEADS[i], i == 0 ? "td h" : "td", head);
            foreach (var a in w.actors.Where(a => a.team == team && !a.isRobot))
            {
                var row = U.Div("tr" + (a == me ? " me" : "") + (a.alive ? "" : " dead"), tm);
                var h = U.Div("td h", row);
                U.Pic("portrait_" + a.def.id, "ti", h);
                U.Txt(a.def.name + (a == me ? " <color=#ffd76a><size=11>YOU</size></color>" : ""), "tn", h);
                double up = a.def.ult != null && a.def.ult.charge > 0 ? a.ult / a.def.ult.charge : 0;
                U.Txt(up >= 1 ? "ULT" : $"{Math.Floor(up * 100)}%", up >= 1 ? "u" : "up", h);
                string F(double v) => U.N(v);
                foreach (var v in new[] { (a.kills + a.assists).ToString(), F(a.stats.TryGetValue("healAssists", out var ha) ? ha : 0), a.deaths.ToString(), F(a.dmgDone), F(a.healDone), F(a.mitigated) })
                    U.Txt(v, "td", row);
            }
        }

        // ------------------------------------------------------------------ events
        public void Event(SimEvent e, Actor me, double now)
        {
            switch (e)
            {
                case DmgEvent d:
                {
                    bool mine = me != null && d.src == me;
                    bool onMe = me != null && d.tgt == me && !d.heal;
                    if (mine || (me == null && d.amt > 30))
                    {
                        var n = U.Txt((d.heal ? "+" : "") + Math.Round(d.amt), "dn" + (d.heal ? " heal" : d.crit ? " crit" : ""), nums);
                        numPool.Add((n, now, Conv.U(d.pos) + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0.4f, 0)));
                        if (numPool.Count > 40) { numPool[0].e.RemoveFromHierarchy(); numPool.RemoveAt(0); }
                        if (mine && !d.heal) { hitUntil = Time.unscaledTime + 0.12f; hitmark.SetColor(d.crit ? new Color(1, 0.32f, 0.32f) : Color.white); }
                    }
                    if (onMe) { hurtAt = Time.unscaledTime; hurtUntil = hurtAt + 0.15f; }
                    break;
                }
                case KillEvent k: Kill(k.src, k.tgt, false, me); break;
                // a destroyed mech shows as the frame's name with a broken-frame marker (the pilot fights on)
                case DemechEvent dm: Kill(dm.src, dm.tgt, true, me); break;
                case CounterEvent c:
                {
                    var co = U.Div("co", callout);
                    Grad.Set(co, CO_BG);
                    var col = U.Hex(c.actor.def.color);
                    co.style.borderTopColor = co.style.borderBottomColor = col;
                    U.Txt("COUNTER", "co-k", co);
                    U.Txt(c.text, "co-b", co).style.color = col;
                    U.Txt($"{c.actor.def.name} vs {c.target.def.name}", "co-s", co);
                    Prepend(callout, co); Pop(co);
                    co.schedule.Execute(() => co.AddToClassList("out")).StartingIn(2600);
                    co.schedule.Execute(() => co.RemoveFromHierarchy()).StartingIn(3200);
                    while (callout.childCount > 3) callout.RemoveAt(callout.childCount - 1);
                    break;
                }
                case MsgEvent m:
                {
                    var l = U.Txt(m.text, "msg", callout);
                    if (!string.IsNullOrEmpty(m.color)) l.style.color = U.Hex(m.color);
                    Prepend(callout, l); Pop(l);
                    l.schedule.Execute(() => l.RemoveFromHierarchy()).StartingIn(3000);
                    break;
                }
            }
        }

        void Kill(Actor src, Actor tgt, bool demech, Actor me)
        {
            var k = U.Div("kf", feed);
            var a = U.Txt(src != null ? src.def.name : "The Void", "kf-b", k); if (src != null) a.style.color = src.team == "zenith" ? Z : UM;
            U.Txt(demech ? "⟶⚙" : src != null ? "⟶" : "↓", "kf-i", k);
            var b = U.Txt(demech ? tgt.baseDef.name : tgt.def.name, "kf-b", k); b.style.color = tgt.team == "zenith" ? Z : UM;
            if (me != null && (src == me || tgt == me)) k.AddToClassList("me");
            Prepend(feed, k);
            k.schedule.Execute(() => k.RemoveFromHierarchy()).StartingIn(6000);
            while (feed.childCount > 6) feed.RemoveAt(feed.childCount - 1);
        }

        static void Prepend(VisualElement parent, VisualElement e) { e.RemoveFromHierarchy(); parent.Insert(0, e); }

        /// <summary>@keyframes pop: from scale 1.3, opacity 0 (.25s)</summary>
        static void Pop(VisualElement e)
        {
            e.style.scale = new Scale(new Vector3(1.3f, 1.3f, 1)); e.style.opacity = 0;
            e.schedule.Execute(() => { e.AddToClassList("popped"); e.style.scale = StyleKeyword.Null; e.style.opacity = StyleKeyword.Null; }).StartingIn(16);
        }

        public void Reset()
        {
            lastHero = ""; objKind = "";
            foreach (var b in barEls.Values) b.e.RemoveFromHierarchy();
            barEls.Clear();
            feed.Clear(); callout.Clear(); nums.Clear(); numPool.Clear(); obj.Clear(); o.Clear();
        }
    }
}
