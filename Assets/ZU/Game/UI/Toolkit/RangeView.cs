// Training Grounds UI (desktop edition) - src/client/RangeUI.ts ported to UI Toolkit (range.css as Resources/ZUUI/range.uss,
// the same rg-* class names): the console (G, or the consoles by the Hero Range and the Spar Arena; the pause menu's
// HERO RANGE · SPAR ARENA) with its two stations, the Hero Range damage meter, and the spar scoreboard / countdown. The
// console pauses the match while it is open (PauseMenu), and the pause screen steps aside for it (PauseView).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ZU.Game.UI;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class RangeView : MonoBehaviour
    {
        static readonly Dictionary<string, string> DIFF_NAME = new Dictionary<string, string> { ["easy"] = "EASY", ["medium"] = "MEDIUM", ["hard"] = "HARD" };
        /// <summary>how close you must stand to a console for the prompt</summary>
        const double NEAR = 3.2;
        static readonly Color Z = Grad.C("#5cc8ff");

        public static RangeView Current { get; private set; }

        MatchRunner r;
        HeroRange range;
        Spar spar;
        VisualElement meter, sparHud, count, prompt, panel;
        Label countMain, countSub;
        /// <summary>ConsoleTab: "range" | "spar"</summary>
        public string tab = "range";
        float at = -1;
        RangeOpts draftR;
        SparOpts draftS;

        public bool Open => panel != null;

        public static RangeView Attach(MatchRunner runner)
        {
            var m = runner.Match;
            if (m?.range == null) return null;
            var v = Current = runner.gameObject.AddComponent<RangeView>();
            v.r = runner; v.range = m.range; v.spar = m.spar;
            v.draftR = m.range.opts.Clone();
            v.draftS = (m.spar?.opts ?? Spar.DEFAULT_SPAR).Clone();
            v.Build();
            return v;
        }

        void Build()
        {
            var ui = UiRoot.Get();
            var sheet = Resources.Load<StyleSheet>("ZUUI/range");
            if (sheet != null && !ui.Root.styleSheets.Contains(sheet)) ui.Root.styleSheets.Add(sheet);
            meter = U.Div("rg-meter", ui.HudLayer);
            sparHud = U.Div("rg-spar", ui.HudLayer);
            count = U.Div("rg-count", ui.HudLayer);
            countMain = U.Txt("", "rg-cm", count); countSub = U.Txt("", "rg-cs", count);
            prompt = U.Div("rg-prompt", ui.HudLayer);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            panel?.RemoveFromHierarchy(); panel = null;
            foreach (var e in new[] { meter, sparHud, count, prompt }) e?.RemoveFromHierarchy();
        }

        /// <summary>which station you're standing at (the console opens on that tab)</summary>
        public string Nearest(Actor me)
        {
            if (me == null) return null;
            if (M.Hypot(me.pos.x - HeroRange.LANE.console.x, me.pos.z - HeroRange.LANE.console.z) < NEAR) return "range";
            if (M.Hypot(me.pos.x - Spar.SPAR_CONSOLE.x, me.pos.z - Spar.SPAR_CONSOLE.z) < NEAR) return "spar";
            return null;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            var w = r != null ? r.World : null; if (w == null) return;
            var me = r.Player; var s = ZuSettings.Current;
            var keys = ZuSettings.BindsFor(s, me?.def.id, "range");
            var kb = Keyboard.current;
            // G (or its rebinding) in the Training Grounds opens the console - at a console, on that station's tab; Esc or G closes it
            if (Open) { if (Keys.Pressed(keys) || (kb != null && kb.escapeKey.wasPressedThisFrame)) Close(); }
            else if (!PauseMenu.Paused && me != null && Keys.Pressed(keys)) Show(Nearest(me));
            Frame(me, w.time);
        }

        void Big(string main, string sub, string cls)
        {
            U.Set(countMain, main); U.Set(countSub, sub ?? ""); U.Show(countSub, !string.IsNullOrEmpty(sub));
            foreach (var c in new[] { "on", "fight", "msg", "win", "lose" }) U.Toggle(count, c, c == "on" || (" " + cls + " ").Contains(" " + c + " "));
        }

        void Frame(Actor me, double time)
        {
            // the countdown is big and centred - every frame; the panels ~8 times a second
            var s = spar; string foe = U.Up(s?.foe?.baseDef.name ?? "");
            if (s != null && s.phase == "countdown") Big(Math.Ceiling(s.Left).ToString(), null, "");
            else if (s != null && s.phase == "fight" && time - s.phaseAt < 0.8) Big("FIGHT!", null, "fight");
            else if (s != null && s.phase == "roundover" && time - s.phaseAt < 2.2)
                Big($"ROUND {s.round}", s.roundWinner == "draw" ? "DRAW" : s.roundWinner == "you" ? "YOU WIN" : $"{foe} WINS", "msg " + (s.roundWinner == "you" ? "win" : s.roundWinner == "draw" ? "" : "lose"));
            else if (s != null && s.phase == "done")
            {
                bool won = s.wins["you"] > s.wins["them"];
                Big(won ? "VICTORY" : "DEFEAT", $"{(won ? "YOU WIN" : foe + " WINS")} THE SPAR {Math.Max(s.wins["you"], s.wins["them"])} - {Math.Min(s.wins["you"], s.wins["them"])}", "msg " + (won ? "win" : "lose"));
            }
            else foreach (var c in new[] { "on", "fight", "msg", "win", "lose" }) count.RemoveFromClassList(c);
            if (Time.unscaledTime - at < 0.12f) return;
            at = Time.unscaledTime;
            DrawMeter(me, time);
            DrawSpar(me);
            var near = Nearest(me);
            bool sp = s != null && s.phase == "waiting" && s.foe != null && me != null && Math.Abs(me.pos.z - (Spar.ARENA.z - Spar.ARENA.hz)) < 6 && Math.Abs(me.pos.x - Spar.ARENA.x) < Spar.ARENA.hx + 2 && !s.Inside(me.pos);
            string key = null, txt = "";
            if (!Open)
            {
                if (near == "range") { key = KeyLabel(me); txt = "HERO RANGE CONSOLE"; }
                else if (near == "spar") { key = KeyLabel(me); txt = "SPAR CONSOLE"; }
                else if (sp) txt = $"SPAR ARMED · {U.Up(s.foe.baseDef.name)} ({DIFF_NAME[s.opts.diff]}) · walk in to start - the box seals until someone wins";
                else if (s != null && s.phase == "waiting" && s.needExit && me != null && s.Inside(me.pos)) txt = "step out and back in for a rematch";
            }
            prompt.Clear();
            if (key != null) U.Txt(key, "kbd", prompt);
            if (txt != "") U.Txt(txt, "rg-pt", prompt);
            U.Toggle(prompt, "on", txt != "");
        }

        /// <summary>the key the console is bound to (G by default)</summary>
        static string KeyLabel(Actor me)
        {
            var b = ZuSettings.BindsFor(ZuSettings.Current, me?.def.id, "range");
            return ZuSettings.KeyName(b.Count > 0 ? b[0] : null);
        }

        static string N0(double x) => U.N(x);
        static string Secs(double? x) => x == null ? "—" : $"{U.F(x.Value, 2)} s";

        /// <summary>a table row of label / value pairs (the TS &lt;tr&gt;&lt;th&gt;..&lt;td&gt;..)</summary>
        static void Row(VisualElement table, params (string th, string td, string cls)[] cells)
        {
            var tr = U.Div("rg-tr", table);
            foreach (var (th, td, cls) in cells) { U.Txt(th, "rg-th", tr); U.Txt(td, "rg-td " + (cls ?? ""), tr); }
        }

        static void TallyRows(VisualElement table, Tally t, bool live)
        {
            var dps = t.Dps();
            Row(table, ("DAMAGE", N0(t.total), null), ("DPS", dps == null ? "—" : N0(dps.Value), live ? "live" : null));
            Row(table, ("HITS", t.hits.ToString(), null), ("CRITS", t.crits + (t.hits > 0 ? $" <size=11><alpha=#99>{JsMath.Round((double)t.crits / t.hits * 100)}%</alpha></size>" : ""), null));
            // (◆ marks a critical hit: the TS's ✦ is in none of the game's fonts)
            Row(table, ("LAST HIT", t.hits > 0 ? N0(t.last) + (t.lastCrit ? " ◆" : "") : "—", t.lastCrit ? "crit" : null), ("BIGGEST", t.hits > 0 ? N0(t.max) : "—", null));
        }

        void DrawMeter(Actor me, double time)
        {
            var a = range.bot;
            if (a == null || (spar?.Sealed ?? false)) { meter.RemoveFromClassList("on"); return; }
            var s = range.stats; var o = range.opts; var d = s.dealt;
            double? acc = me != null && me.shots - s.shots0 > 0 ? JsMath.Round((double)(me.hits - s.hits0) / (me.shots - s.shots0) * 100) : (double?)null;
            meter.AddToClassList("on");
            var col = U.Hex(a.baseDef.color);
            meter.style.borderLeftColor = col;
            meter.Clear();
            var h4 = U.Div("rg-h4", meter);
            var pic = U.Pic("portrait_" + a.baseDef.id, "rg-pimg", h4);
            pic.style.borderTopColor = pic.style.borderBottomColor = pic.style.borderLeftColor = pic.style.borderRightColor = col;
            var nm = U.Div("rg-nm", h4);
            U.Txt(a.baseDef.name, "rg-b", nm).style.color = col;
            var sub = U.Div("rg-sub", nm);
            U.Txt(U.Up(o.mode), "rg-small " + o.mode, sub);
            string skill = HeroRange.SKILLS.FirstOrDefault(k => k.skill == o.skill).name ?? "";
            U.Txt($"{o.dist} m · ABILITIES {(o.abilities ? "ON" : "OFF")}{(o.mode == "defense" ? $" · {U.Up(o.move)}" : $" · {skill}")}", "rg-small", sub);
            // health / armor / shields, the numbers under it
            double sh = a.ShieldAmt, max = a.MaxHp + Math.Max(0, sh);
            var bar = new HpBar(); bar.AddToClassList("rg-hpb"); meter.Add(bar);
            bar.Set(Math.Max(0, a.hp), Math.Max(0, a.armor), Math.Max(0, sh), max);
            U.Txt(a.alive ? $"{N0(a.Health + sh)} <size=12><alpha=#B3>/ {N0(a.MaxHp)}{(a.def.armor > 0 ? $" · {N0(a.armor)} armor" : "")}{(sh > 0.5 ? $" · {N0(sh)} shield" : "")}{(a.barrier.up ? $" · barrier {N0(a.barrier.hp)}" : "")}</alpha></size>"
                : "<color=#ff5d6d>DOWN</color> <size=12><alpha=#B3>back in a moment</alpha></size>", "rg-hpn", meter);
            U.Txt($"YOUR DAMAGE <size=11><alpha=#B3>as {me?.def.name ?? "-"}{(s.lastDist > 0 ? $" · from {U.F(s.lastDist, 1)} m" : "")}</alpha></size>", "rg-h5", meter);
            var t = U.Div("rg-table", meter);
            TallyRows(t, d, d.Active(time));
            Row(t, ("WEAPON", N0(d.by["weapon"]), null), ("ABILITIES", N0(d.by["ability"] + d.by["dot"]), null));
            Row(t, ("TIME TO KILL", Secs(s.lastTtk), null), ("BEST", Secs(s.bestTtk), null));
            Row(t, ("KILLS", s.kills.ToString(), null), ("ACCURACY", acc == null ? "—" : acc + "%", null));
            if (o.mode == "attack")
            {
                U.Txt($"{U.Up(a.baseDef.name)}'S DAMAGE TO YOU", "rg-h5", meter);
                var t2 = U.Div("rg-table", meter);
                TallyRows(t2, s.taken, s.taken.Active(time));
                Row(t2, ("YOUR DEATHS", s.deaths.ToString(), null), ("", "", null));
            }
            var keys = U.Div("rg-keys", meter);
            U.Txt(KeyLabel(me), "kbd", keys); U.Txt("console", "rg-kt", keys);
        }

        void DrawSpar(Actor me)
        {
            var s = spar;
            if (s == null || s.foe == null || !s.Sealed) { sparHud.RemoveFromClassList("on"); return; }
            var f = s.foe; var fc = U.Hex(f.baseDef.color);
            sparHud.AddToClassList("on");
            sparHud.Clear();
            void Pips(VisualElement side, int n, Color c)
            {
                var p = U.Div("rg-pips", side);
                for (int i = 0; i < s.opts.firstTo; i++)
                {
                    var pip = U.Div("rg-pip", p);
                    if (i < n) { pip.style.backgroundColor = c; pip.style.borderTopColor = pip.style.borderBottomColor = pip.style.borderLeftColor = pip.style.borderRightColor = c; }
                }
            }
            string fn = U.Up(f.baseDef.name);
            string st = s.phase == "countdown" ? $"ROUND {s.round} · GET READY" : s.phase == "fight" ? $"ROUND {s.round}" : s.phase == "roundover"
                ? (s.roundWinner == "draw" ? $"ROUND {s.round} · DRAW" : $"ROUND {s.round} · {(s.roundWinner == "you" ? "YOU WIN" : fn + " WINS")}")
                : s.wins["you"] > s.wins["them"] ? "YOU WIN THE SPAR" : $"{fn} WINS THE SPAR";
            var you = new Shape(); you.AddToClassList("rg-side"); you.AddToClassList("you"); you.strokeOverride = Z; sparHud.Add(you);
            U.Txt(me?.def.name ?? "YOU", "rg-sb", you); Pips(you, s.wins["you"], Z); U.Txt(s.wins["you"].ToString(), "rg-em", you).style.color = Z;
            var mid = U.Div("rg-mid " + s.phase, sparHud);
            U.Txt($"SPAR · FIRST TO {s.opts.firstTo} · {DIFF_NAME[s.opts.diff]}", "rg-ms", mid);
            U.Txt(st, "rg-mt", mid);
            var them = new Shape(); them.AddToClassList("rg-side"); them.AddToClassList("them"); them.strokeOverride = fc; sparHud.Add(them);
            U.Txt(s.wins["them"].ToString(), "rg-em", them).style.color = fc; Pips(them, s.wins["them"], fc); U.Txt(f.baseDef.name, "rg-sb", them);
        }

        // ------------------------------------------------------------------ the console
        /// <summary>open the console (null: the tab it was last on); the match pauses while it is open</summary>
        public void Show(string t = null)
        {
            tab = t ?? tab;
            if (!PauseMenu.Paused) PauseMenu.Pause();
            panel?.RemoveFromHierarchy();
            var p = panel = U.Div("rg-console", UiRoot.Get().MenuLayer, pick: true);
            // backdrop-filter: blur(5px) over the frozen frame
            if (Filters.Enabled) p.style.backdropFilter = new StyleList<FilterFunction>(new List<FilterFunction> { Filters.Blur(5) });
            var box = U.Div("rg-box", p, pick: true);
            Grad.Fill(Grad.Radial(20, 0, (Grad.C("#142a48"), 0), (Grad.C("#070b14"), 70)), box, "rg-boxbg");
            var tabs = U.Div("rg-tabs", box);
            U.Btn("HERO RANGE", "rg-tab" + (tab == "range" ? " on" : ""), () => Show("range"), tabs);
            U.Btn("SPAR ARENA", "rg-tab" + (tab == "spar" ? " on" : ""), () => Show("spar"), tabs);
            U.Txt($"Esc / {KeyLabel(r.Player)} close", "rg-hint", tabs);
            U.Btn("✕", "rg-x", Close, tabs);
            var body = U.Div("rg-body " + tab, box);
            var left = U.Div("rg-left", body);
            var grid = new ScrollView(ScrollViewMode.Vertical); grid.AddToClassList("rg-grid"); left.Add(grid);
            var opts = new ScrollView(ScrollViewMode.Vertical); opts.AddToClassList("rg-opts"); body.Add(opts);
            var log = new ScrollView(ScrollViewMode.Vertical); log.AddToClassList("rg-log"); left.Add(log);
            var R = draftR; var S = draftS; var s = spar;
            Pick(grid, tab == "range" ? R.hero : S.hero, id => { if (tab == "range") R.hero = id; else S.hero = id; Show(); });
            if (tab == "range")
            {
                Lbl(opts, "MODE"); Chips(opts, new[] { ("ATTACK", "attack"), ("DEFENSE", "defense") }, R.mode, v => R.mode = v);
                Desc(opts, R.mode == "attack" ? "It fights back with its whole kit, leashed to its lane - measure what you deal under fire and what that hero deals to you."
                    : "It holds its post facing you and never fires; its passives still apply (armor, damage reduction). Back to full health 3 s after your last hit - every burst gives a clean time-to-kill.");
                Lbl(opts, "ABILITIES"); Chips(opts, new[] { ("ON", "on"), ("OFF", "off") }, R.abilities ? "on" : "off", v => R.abilities = v == "on");
                Desc(opts, R.mode == "attack" ? "Off: weapon only - no abilities, no ultimate." : "On: it guards itself the way a player would - barrier, temporary health, deflect or parry, a dodge when low. Off: a pure target, like Overwatch's Hero Bot.");
                Lbl(opts, "MOVEMENT"); Chips(opts, new[] { ("HOLD", "hold"), ("STRAFE", "strafe") }, R.move, v => R.move = v, R.mode == "attack");
                Lbl(opts, "DISTANCE"); Chips(opts, HeroRange.LANE.dists.Select(d => ($"{d} M", d.ToString())).ToArray(), R.dist.ToString(), v => R.dist = double.Parse(v));
                Lbl(opts, "AI AIM"); Chips(opts, HeroRange.SKILLS.Select(k => (k.name, k.skill.ToString("R", System.Globalization.CultureInfo.InvariantCulture))).ToArray(), R.skill.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    v => R.skill = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture), R.mode == "defense");
                var go = U.Div("rg-go", opts);
                U.Btn($"DEPLOY {U.Up(GameData.Current.Hero[R.hero].name)}", "primary rg-deploy", () => { range.Deploy(R.hero, R.mode, R.abilities, R.move, R.dist, R.skill); Close(); }, go);
                if (range.bot != null)
                {
                    U.Btn("REMOVE TARGET", "rg-remove", () => { range.Clear(); Show(); }, go);
                    U.Btn("RESET STATS", "rg-reset", () => { range.ResetStats(); Show(); }, go);
                }
                U.Txt("RESULTS <size=12><alpha=#99>each target you drop</alpha></size>", "rg-h5", log);
                var L = range.stats.log;
                if (L.Count > 0)
                {
                    var tbl = Table(log, "YOU", "TARGET", "MODE", "DIST", "TIME TO KILL", "DAMAGE", "HITS", "CRITS", "DPS");
                    foreach (var x in L)
                        Tr(tbl, null, x.you, x.target, x.mode + (x.abilities ? "+ab" : ""), $"{x.dist} m",
                            Secs(x.ttk) + (x.frameDown != null ? $" <size=12><alpha=#99>(frame {U.F(x.frameDown.Value, 2)})</alpha></size>" : ""),
                            N0(x.dmg), x.hits.ToString(), x.crits.ToString(), x.dps == null ? "—" : N0(x.dps.Value));
                }
                else U.Txt("Deploy a hero and drop it: every kill is logged here with your hero, the time to kill and the damage.", "rg-empty", log);
            }
            else
            {
                bool sealedNow = s?.Sealed ?? false;
                Lbl(opts, "DIFFICULTY"); Chips(opts, new[] { ("EASY", "easy"), ("MEDIUM", "medium"), ("HARD", "hard") }, S.diff, v => S.diff = v);
                Desc(opts, (S.diff == "easy" ? "Slow aim and slow reactions." : S.diff == "medium" ? "A fair fight." : "Sharp aim, quick abilities and ultimates - it punishes mistakes.")
                    + $" The opponent plays its full kit at every level (AI skill {Spar.SPAR_SKILL[S.diff].ToString(System.Globalization.CultureInfo.InvariantCulture)}).");
                Lbl(opts, "ROUNDS"); Chips(opts, new[] { ("FIRST TO 1", "1"), ("FIRST TO 2", "2"), ("FIRST TO 3", "3") }, S.firstTo.ToString(), v => S.firstTo = int.Parse(v));
                Desc(opts, "Walk into the arena north of the spawn: a holographic box seals around the two of you and stays shut until someone has won the spar. Every round starts from full health at opposite ends after a 3 s countdown and ends on a kill.");
                var go = U.Div("rg-go", opts);
                if (sealedNow) U.Btn("FORFEIT SPAR", "rg-end", () => { s.Cancel(); Show(); }, go);
                else
                {
                    U.Btn($"{(s?.foe != null ? "RE-ARM" : "ARM")} SPAR · {U.Up(GameData.Current.Hero[S.hero].name)}", "primary rg-arm", () => { s?.Arm(S.hero, S.diff, S.firstTo); Close(); }, go);
                    if (s?.foe != null) U.Btn("REMOVE OPPONENT", "rg-end", () => { s.Cancel(); Show(); }, go);
                }
                U.Txt("SPAR HISTORY", "rg-h5", log);
                if (s != null && s.history.Count > 0)
                {
                    var tbl = Table(log, "YOU", "OPPONENT", "DIFFICULTY", "SCORE", "RESULT");
                    foreach (var x in s.history) Tr(tbl, x.won ? "won" : "lost", x.you, x.foe, DIFF_NAME[x.diff], $"{x.score[0]} - {x.score[1]}", x.won ? "WIN" : "LOSS");
                }
                else U.Txt("No spars yet.", "rg-empty", log);
            }
        }

        /// <summary>the hero grid: Zenith on one row, Umbra on the next (the desktop roster)</summary>
        void Pick(VisualElement grid, string cur, Action<string> pick)
        {
            var heroes = Setup.RosterFor(true);
            foreach (var team in new[] { "zenith", "umbra" })
            {
                var row = U.Div("rg-row", grid);
                foreach (var h in heroes.Where(x => x.team == team))
                {
                    var id = h.id;
                    var c = U.Btn(null, $"rg-card {h.team}{(h.id == cur ? " sel" : "")}", () => pick(id), row);
                    var col = U.Hex(h.color);
                    U.Pic("portrait_" + h.id, "rg-cimg", c);
                    var glow = U.Div("rg-cglow", c);
                    Grad.Set(glow, Grad.Linear(180, (new Color(0, 0, 0, 0), 0), (U.A(Grad.C(h.team == "zenith" ? "#5cc8ff" : "#ff3b5c"), 0.35f), 100)));
                    U.Txt(h.name, "rg-cb", c);
                    U.Txt(U.Up(h.role), "rg-crole", c);
                    if (h.id == cur) c.style.borderTopColor = c.style.borderBottomColor = c.style.borderLeftColor = c.style.borderRightColor = col;
                    // aspect-ratio 3/4
                    c.RegisterCallback<GeometryChangedEvent>(e => { float hgt = e.newRect.width * 4 / 3; if (Mathf.Abs(c.resolvedStyle.height - hgt) > 0.5f) c.style.height = hgt; });
                }
            }
        }

        static void Lbl(VisualElement parent, string text) => U.Txt(text, "rg-label", parent);
        static void Desc(VisualElement parent, string text) => U.Txt(text, "rg-desc", parent);

        /// <summary>a row of option chips; `dis` greys the group out (it doesn't apply to the mode)</summary>
        void Chips(VisualElement parent, (string label, string v)[] list, string cur, Action<string> set, bool dis = false)
        {
            var row = U.Div("rg-chips" + (dis ? " dis" : ""), parent);
            foreach (var (label, v) in list)
            {
                var b = U.Btn(label, "rg-chip" + (v == cur ? " on" : ""), () => { set(v); Show(); }, row);
                if (dis) b.SetEnabled(false);
            }
        }

        static VisualElement Table(VisualElement parent, params string[] head)
        {
            var t = U.Div("rg-ltable", parent);
            var tr = U.Div("rg-ltr rg-lth", t);
            foreach (var h in head) U.Txt(h, "rg-ltd", tr);
            return t;
        }
        static void Tr(VisualElement t, string lastCls, params string[] cells)
        {
            var tr = U.Div("rg-ltr", t);
            for (int i = 0; i < cells.Length; i++) U.Txt(cells[i], "rg-ltd" + (i == cells.Length - 1 && lastCls != null ? " " + lastCls : ""), tr);
        }

        public void Close()
        {
            if (panel == null) return;
            panel.RemoveFromHierarchy(); panel = null;
            PauseMenu.Resume();
        }
    }
}
