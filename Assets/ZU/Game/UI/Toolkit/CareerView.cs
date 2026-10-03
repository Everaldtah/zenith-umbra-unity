// CAREER PROFILE (src/client/CareerUI.ts), laid out after Overwatch 2's: OVERVIEW (time played, time per mode and role,
// Top Heroes with the Hero Comparison dropdown), STATISTICS (Total / Avg per 10 min / Best tables for a mode and a
// hero), HERO RATINGS (Season 18's Hero Skill Rating per hero, per ranked queue), PROGRESSION (hero levels) and
// HISTORY. The numbers are Career/Profile.cs's.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.Career;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class CareerView
    {
        static readonly (string id, string name)[] TABS = { ("overview", "OVERVIEW"), ("stats", "STATISTICS"), ("ratings", "HERO RATINGS"), ("progress", "PROGRESSION"), ("history", "HISTORY") };
        static readonly Dictionary<string, string> ROLE_LABEL = new Dictionary<string, string> { ["tank"] = "Tank", ["dps"] = "Damage", ["support"] = "Support" };
        // the screen's choices (kept between visits, as the TS module state is)
        static string tab = "overview", mode = "all", hero = "all", compare = "time", queue = "online-comp";

        readonly VisualElement el;
        readonly Action back;
        readonly GameData d = ZuData.Get();
        ProfileData p;

        CareerView(VisualElement parent, Action back) { this.back = back; el = U.Div("cp", parent, pick: true); Current = this; Render(); }
        /// <summary>the Career Profile on screen (the UI tour switches its tabs)</summary>
        public static CareerView Current { get; private set; }
        public void SetTab(string t) { tab = t; Render(); }
        public void Close() { el.RemoveFromHierarchy(); if (Current == this) Current = null; back?.Invoke(); }
        public static CareerView Open(VisualElement parent, Action back) => new CareerView(parent, back);

        string Nm(string id) => d.Def(id)?.name ?? id;
        Color Col(string id) => U.Hex(d.Def(id)?.color ?? "#cfd6f5");
        static string Int(double v) => U.N(v);
        static string Pct(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        static string Avg(HeroLine l, double v) { double x = CareerProfile.Per10(l, v); return l.time > 0 ? (x < 10 ? x.ToString("0.0", CultureInfo.InvariantCulture) : Int(x)) : "-"; }
        static string Label_(string k) { var s = Regex.Replace(k, "([A-Z])", " $1"); return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s; }
        static string FmtCompare(string k, double v) => k == "time" ? CareerProfile.FmtHours(v) : k == "winPct" || k == "acc" || k == "crit" ? Pct(v) : k == "epl" ? v.ToString("0.00", CultureInfo.InvariantCulture) : Int(v);
        VisualElement Img(VisualElement parent, string id, float size, string cls = "")
        {
            var e = U.Pic("portrait_" + id, "cimg " + cls, parent);
            e.style.width = e.style.height = size;
            return e;
        }

        void Render()
        {
            p = CareerProfile.Load();
            var c = Ranks.Load();
            var all = CareerProfile.Line(p, "all", "all");
            el.Clear();
            Grad.Fill(Grad.Radial(50, 0, (Grad.C("#1b2140"), 0), (Grad.C("#05060a"), 70)), el);
            var head = U.Div("head", el);
            var medal = U.Div("medal", head);
            Grad.Set(medal, Grad.Radial(35, 30, (Grad.C("#3a4a7a"), 0), (Grad.C("#141a30"), 100)));
            U.Txt(CareerProfile.PlayerLevel(p).ToString(), "md-b", medal); U.Txt("LEVEL", "md-s", medal);
            var who = U.Div("who", head);
            U.Txt(PlayerPrefs.GetString("zu-name", "Vanguard"), "who-h2", who);
            U.Txt($"{CareerProfile.FmtHours(all.time)} played · {CareerProfile.Plural(all.wins, "game")} won · {CareerProfile.Plural(CareerProfile.HeroesPlayed(p, "all").Count, "hero", "heroes")}", "who-s", who);
            var chips = U.Div("chips", head);
            foreach (var r in new[] { "tank", "damage", "support" })
            {
                var rr = c.roles[r];
                var chip = U.Div("chip", chips); chip.tooltip = "Competitive";
                chip.Add(PauseView.Emblem(rr.rating, rr.games, 34));
                U.Txt($"{U.Up(r)}\n{Ranks.RankOf(rr.rating, rr.games).label}", "chip-s", chip);
            }
            var tabs = U.Div("tabs", el);
            foreach (var (t, n) in TABS) U.Btn(n, "tab" + (tab == t ? " on" : ""), () => { tab = t; Render(); }, tabs);
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("body"); el.Add(sv);
            var body = sv.contentContainer;
            switch (tab)
            {
                case "overview": Overview(body); break;
                case "stats": Stats(body); break;
                case "ratings": Ratings(body); break;
                case "progress": Progression(body); break;
                default: History(body); break;
            }
            var bar = U.Div("bar", el, pick: true);
            U.Btn("BACK", null, () => { el.RemoveFromHierarchy(); back(); }, bar);
        }

        // ------------------------------------------------------------------ pieces
        VisualElement Filters_(VisualElement parent) => U.Div("filters", parent);

        void ModeSelect(VisualElement f)
        {
            var ids = new List<string> { "all" }; ids.AddRange(CareerProfile.CAREER_MODES);
            var names = ids.Select(m => m == "all" ? "All Modes" : CareerProfile.MODE_LABEL[m]).ToList();
            var dd = new DropdownField(names, Math.Max(0, ids.IndexOf(mode))); dd.AddToClassList("csel");
            dd.RegisterValueChangedCallback(_ => { mode = ids[Math.Max(0, dd.index)]; Render(); });
            f.Add(dd);
        }

        static VisualElement Bar(VisualElement parent, double frac, Color? col = null)
        {
            var i = U.Div("cbar", parent); var u = U.Div("cbar-u", i);
            u.style.width = new Length((float)Math.Max(0, Math.Min(100, frac * 100)), LengthUnit.Percent);
            if (col.HasValue) u.style.backgroundColor = col.Value;
            return i;
        }

        /// <summary>a table: a header row and rows of cells (first column left-aligned)</summary>
        static void Table(VisualElement parent, string[] head, IEnumerable<string[]> rows, string cls = "")
        {
            var t = U.Div("ctable " + cls, parent);
            var hr = U.Div("ctr th", t);
            for (int i = 0; i < head.Length; i++) U.Txt(head[i], i == 0 ? "ctd first" : "ctd", hr);
            foreach (var r in rows)
            {
                var tr = U.Div("ctr", t);
                for (int i = 0; i < r.Length; i++) U.Txt(r[i], i == 0 ? "ctd first" : "ctd", tr);
            }
        }

        // ------------------------------------------------------------------ OVERVIEW
        void Overview(VisualElement b)
        {
            var l = CareerProfile.Line(p, mode, "all");
            var f = Filters_(b); ModeSelect(f);
            if (l.time <= 0 && l.games <= 0) { U.Txt("No matches recorded yet - play any mode and your career fills in.", "empty", b); return; }
            var modes = CareerProfile.TimeByMode(p); double maxM = modes.Count > 0 ? modes[0].time : 1;
            var roles = CareerProfile.ByRole(p, mode);
            var top = CareerProfile.TopHeroes(p, mode, compare); double max = top.Count > 0 ? top[0].value : 1;
            var strip = U.Div("strip", b);
            foreach (var (k, v) in new[] { ("TIME PLAYED", CareerProfile.FmtTime(l.time)), ("GAMES WON", Int(l.wins)), ("WIN %", Pct(CareerProfile.WinPct(l))), ("ELIMINATIONS", Int(l.elims)), ("DEATHS", Int(l.deaths)), ("WEAPON ACCURACY", Pct(CareerProfile.Accuracy(l))) })
            { var x = U.Div("st", strip); U.Txt(v, "st-b", x); U.Txt(k, "st-s", x); }
            var cols = U.Div("cols", b);
            var left = U.Div("col l", cols);
            U.Txt("TIME PLAYED BY MODE", "h3", left);
            foreach (var (m, t) in modes)
            {
                var r = U.Div("mbar", left);
                U.Txt(CareerProfile.MODE_LABEL[m], "mb-l", r); Bar(r, t / maxM); U.Txt(CareerProfile.FmtTime(t), "mb-v", r);
            }
            U.Txt("ROLES", "h3", left);
            var rc = U.Div("rolecards", left);
            foreach (var r in new[] { "tank", "damage", "support" })
            {
                var x = roles[r]; var card = U.Div("rc", rc);
                var icon = r == "tank" ? Poly.Pct(50, 0, 100, 20, 90, 70, 50, 100, 10, 70, 0, 20) : r == "damage" ? Poly.Pct(40, 0, 60, 0, 60, 60, 80, 60, 50, 100, 20, 60, 40, 60) : Poly.Pct(38, 0, 62, 0, 62, 38, 100, 38, 100, 62, 62, 62, 62, 100, 38, 100, 38, 62, 0, 62, 0, 38, 38, 38);
                icon.AddToClassList("ri"); card.Add(icon);
                U.Txt(U.Up(r), "rc-b", card); U.Txt(CareerProfile.FmtHours(x.time), "rc-s", card); U.Txt($"{Int(x.wins)} won · {Pct(CareerProfile.WinPct(x))}", "rc-s", card);
            }
            var right = U.Div("col r", cols);
            var h3 = U.Div("h3row", right); U.Txt("TOP HEROES", "h3", h3);
            var keys = CareerProfile.COMPARE.Select(x => x.key).ToList();
            var cmp = new DropdownField(CareerProfile.COMPARE.Select(x => x.label).ToList(), Math.Max(0, keys.IndexOf(compare))); cmp.AddToClassList("csel");
            cmp.RegisterValueChangedCallback(_ => { compare = keys[Math.Max(0, cmp.index)]; Render(); });
            h3.Add(cmp);
            if (top.Count == 0) U.Txt($"Nothing to compare yet{(compare == "sr" ? " - heroes place after 5 ranked matches" : "")}.", "empty", right);
            foreach (var (id, value) in top)
            {
                var row = U.Btn(null, "top", () => { hero = id; tab = "stats"; Render(); }, right);
                Img(row, id, 44);
                var mid = U.Div("top-m", row);
                U.Txt(Nm(id), "top-b", mid).style.color = Col(id);
                Bar(mid, value / max, Col(id));
                U.Txt(FmtCompare(compare, value), "top-v", row);
            }
        }

        // ------------------------------------------------------------------ STATISTICS
        void Stats(VisualElement b)
        {
            var heroes = CareerProfile.HeroesPlayed(p, mode);
            if (hero != "all" && !heroes.Contains(hero)) heroes.Add(hero);
            var l = CareerProfile.Line(p, mode, hero);
            var f = Filters_(b); ModeSelect(f);
            var hids = new List<string> { "all" }; hids.AddRange(heroes);
            var hs = new DropdownField(hids.Select(h => h == "all" ? "All Heroes" : Nm(h)).ToList(), Math.Max(0, hids.IndexOf(hero))); hs.AddToClassList("csel");
            hs.RegisterValueChangedCallback(_ => { hero = hids[Math.Max(0, hs.index)]; Render(); });
            f.Add(hs);
            if (hero != "all")
            {
                var lv = CareerProfile.HeroLevel(p.xp.TryGetValue(hero, out var xp) ? xp : 0);
                var hh = U.Div("herohead", b);
                Img(hh, hero, 80, "big");
                var mid = U.Div("hh-m", hh);
                U.Txt(Nm(hero), "hh-h3", mid).style.color = Col(hero);
                var lr = U.Div("hh-row", mid);
                var lvl = U.Txt($"LEVEL {lv.level}", "lvl", lr); var bc = U.Hex(CareerProfile.BADGE_COLOR[lv.badge]);
                lvl.style.borderTopColor = lvl.style.borderBottomColor = lvl.style.borderLeftColor = lvl.style.borderRightColor = bc;
                U.Txt($"{Int(lv.into)} / {Int(lv.need)} XP", "dim", lr);
                var srs = U.Div("srs", hh);
                foreach (var (q, ql) in new[] { ("online-comp", "ONLINE COMPETITIVE"), ("competitive", "COMPETITIVE VS AI") })
                {
                    var row = U.Div("srs-row", srs);
                    U.Txt(ql, "srs-l", row);
                    p.hsr[q].TryGetValue(hero, out var r);
                    var sr = U.Div("sr", row);
                    if (r != null && r.placed >= CareerProfile.HSR_PLACEMENTS) { sr.Add(PauseView.Emblem(CareerProfile.HsrToRating(r.sr), 99, 30)); U.Txt($"SR {Int(r.sr)}", "sr-t", sr); }
                    else { sr.AddToClassList("dim"); U.Txt($"Placements {r?.placed ?? 0}/{CareerProfile.HSR_PLACEMENTS}", "sr-t", sr); }
                }
            }
            if (l.time <= 0 && l.games <= 0) { U.Txt("No numbers for this mode and hero yet.", "empty", b); return; }
            var assists = l.hero.Where(kv => Regex.IsMatch(kv.Key, "assist", RegexOptions.IgnoreCase)).ToList();
            var cards = new List<(string title, string[] head, List<string[]> rows)>
            {
                ("COMBAT", new[] { "", "TOTAL", "AVG / 10 MIN" }, new List<string[]>
                {
                    new[] { "Eliminations", Int(l.elims), Avg(l, l.elims) }, new[] { "Final Blows", Int(l.finalBlows), Avg(l, l.finalBlows) }, new[] { "Assists", Int(l.assists), Avg(l, l.assists) },
                    new[] { "Deaths", Int(l.deaths), Avg(l, l.deaths) }, new[] { "All Damage Done", Int(l.damage), Avg(l, l.damage) }, new[] { "Damage Mitigated", Int(l.mitigated), Avg(l, l.mitigated) },
                    new[] { "Objective Kills", Int(l.objKills), Avg(l, l.objKills) }, new[] { "Objective Time", CareerProfile.FmtTime(l.objTime), l.time > 0 ? CareerProfile.FmtTime(CareerProfile.Per10(l, l.objTime)) : "-" }, new[] { "Ultimates Used", Int(l.ults), Avg(l, l.ults) },
                }),
                ("ASSISTS", new[] { "", "TOTAL", "AVG / 10 MIN" }, new[] { new[] { "Healing Done", Int(l.healing), Avg(l, l.healing) } }.Concat(assists.Select(kv => new[] { Label_(kv.Key), Int(kv.Value), Avg(l, kv.Value) })).ToList()),
                ("BEST", new[] { "", "BEST IN GAME" }, new List<string[]>
                {
                    new[] { "Eliminations - Most in Game", Int(l.bElims) }, new[] { "Final Blows - Most in Game", Int(l.bFinal) }, new[] { "All Damage Done - Most in Game", Int(l.bDamage) },
                    new[] { "Healing Done - Most in Game", Int(l.bHealing) }, new[] { "Damage Mitigated - Most in Game", Int(l.bMitigated) }, new[] { "Objective Time - Most in Game", CareerProfile.FmtTime(l.bObjTime) },
                    new[] { "Kill Streak - Best", Int(l.bStreak) }, new[] { "Multikill - Best", Int(l.bMulti) }, new[] { "Weapon Accuracy - Best in Game", $"{l.bAcc:0}%" },
                }),
                ("GAME", new[] { "", "TOTAL" }, new List<string[]>
                {
                    new[] { "Time Played", CareerProfile.FmtTime(l.time) }, new[] { "Games Played", Int(l.games) }, new[] { "Games Won", Int(l.wins) }, new[] { "Games Lost", Int(l.losses) }, new[] { "Games Tied", Int(l.draws) }, new[] { "Win Percentage", Pct(CareerProfile.WinPct(l)) },
                }),
                ("ACCURACY", new[] { "", "TOTAL" }, new List<string[]>
                {
                    new[] { "Weapon Accuracy", Pct(CareerProfile.Accuracy(l)) }, new[] { "Critical Hits", Int(l.crits) }, new[] { "Critical Hit Accuracy", Pct(CareerProfile.CritAccuracy(l)) }, new[] { "Eliminations per Life", CareerProfile.ElimsPerLife(l).ToString("0.00", CultureInfo.InvariantCulture) },
                }),
            };
            var own = l.hero.Where(kv => !Regex.IsMatch(kv.Key, "assist", RegexOptions.IgnoreCase)).ToList();
            if (hero != "all" && own.Count > 0) cards.Add(("HERO SPECIFIC", new[] { "", "TOTAL", "AVG / 10 MIN" }, own.Select(kv => new[] { Label_(kv.Key), Int(kv.Value), Avg(l, kv.Value) }).ToList()));
            var grid = U.Div("cards", b);
            foreach (var (title, head, rows) in cards) { var sc = U.Div("scard", grid); U.Txt(title, "h4", sc); Table(sc, head, rows); }
        }

        // ------------------------------------------------------------------ HERO RATINGS
        void Ratings(VisualElement b)
        {
            var t = p.hsr[queue];
            var f = Filters_(b);
            U.Btn("ONLINE COMPETITIVE", "fbtn" + (queue == "online-comp" ? " on" : ""), () => { queue = "online-comp"; Render(); }, f);
            U.Btn("COMPETITIVE VS AI", "fbtn" + (queue == "competitive" ? " on" : ""), () => { queue = "competitive"; Render(); }, f);
            U.Txt("Hero Skill Rating (0-5000) is your best-guess skill on each hero. A hero places after 5 ranked matches with at least 3 minutes on it among your 3 most-played heroes. It is never used for matchmaking.", "note", b);
            var roster = MenuState.Roster(d);
            foreach (var role in new[] { "tank", "dps", "support" })
            {
                U.Txt(U.Up(ROLE_LABEL[role]), "h3", b);
                var g = U.Div("rgrid", b);
                foreach (var h in roster.Where(x => x.role == role))
                {
                    t.TryGetValue(h.id, out var r);
                    if (r != null && r.placed >= CareerProfile.HSR_PLACEMENTS)
                    {
                        var c = U.Btn(null, "rcard", () => { hero = h.id; tab = "stats"; Render(); }, g);
                        Img(c, h.id, 64); U.Txt(h.name, "rc-b", c).style.color = U.Hex(h.color);
                        U.Txt(Int(r.sr), "srv", c); c.Add(PauseView.Emblem(CareerProfile.HsrToRating(r.sr), 99, 40));
                        U.Txt(Ranks.RankOf(CareerProfile.HsrToRating(r.sr), 99).label, "rc-s", c); U.Txt($"{r.wins}W - {r.losses}L · Peak {Int(r.peak)}", "rc-s", c);
                    }
                    else if (r != null)
                    {
                        var c = U.Btn(null, "rcard", () => { hero = h.id; tab = "stats"; Render(); }, g);
                        Img(c, h.id, 64); U.Txt(h.name, "rc-b", c).style.color = U.Hex(h.color);
                        U.Txt($"PLACEMENTS {r.placed}/{CareerProfile.HSR_PLACEMENTS}", "rc-s", c);
                        var pips = U.Div("pips", c);
                        for (int i = 0; i < CareerProfile.HSR_PLACEMENTS; i++) U.Div("pip" + (i < r.placed ? " on" : ""), pips);
                    }
                    else { var c = U.Div("rcard dim", g); Img(c, h.id, 64); U.Txt(h.name, "rc-b", c); U.Txt("Unplaced", "rc-s", c); }
                }
            }
        }

        // ------------------------------------------------------------------ PROGRESSION
        void Progression(VisualElement b)
        {
            var roster = MenuState.Roster(d);
            var top = p.xp.OrderByDescending(kv => kv.Value).Take(5).ToList();
            U.Txt("150 XP per minute played. Badge tiers at levels 1 / 25 / 50 / 75 / 100, ascended portraits at 20 / 40 / 60 / 80.", "note", b);
            U.Txt("TOP HEROES BY LEVEL", "h3", b);
            if (top.Count == 0) U.Txt("Play any mode to level your heroes.", "empty", b);
            else
            {
                var big = U.Div("pbig", b);
                foreach (var (id, xp) in top.Select(kv => (kv.Key, kv.Value)))
                {
                    var v = CareerProfile.HeroLevel(xp);
                    var ringCol = v.ascended > 0 ? U.Hex(CareerProfile.ASCEND_COLOR[v.ascended - 1]) : Grad.C("#445");
                    var c = U.Btn(null, "pcard", () => { hero = id; tab = "stats"; Render(); }, big);
                    var ring = U.Div("ring", c); ring.style.backgroundColor = ringCol;
                    Img(ring, id, 96, "round");
                    U.Txt(Nm(id), "pc-b", c).style.color = Col(id);
                    U.Txt($"LEVEL {v.level}", "pc-em", c);
                    Bar(c, v.pct / 100);
                    U.Txt($"{Int(v.into)} / {Int(v.need)} XP", "pc-s", c);
                }
            }
            U.Txt("ALL HEROES", "h3", b);
            var grid = U.Div("pgrid", b);
            foreach (var h in roster)
            {
                var v = CareerProfile.HeroLevel(p.xp.TryGetValue(h.id, out var x) ? x : 0);
                var m = U.Btn(null, "pmini", () => { hero = h.id; tab = "stats"; Render(); }, grid);
                Img(m, h.id, 48);
                var col = U.Div("pm-m", m);
                U.Txt(h.name, "pm-b", col);
                U.Txt($"<color={CareerProfile.BADGE_COLOR[v.badge]}>●</color> Level {v.level}", "pm-s", col);
                Bar(col, v.pct / 100);
            }
        }

        // ------------------------------------------------------------------ HISTORY
        void History(VisualElement b)
        {
            var rows = Enumerable.Reverse(p.matches).Take(40).ToList();
            var t = U.Div("ctable hist2", b);
            var hr = U.Div("ctr th", t);
            foreach (var h in new[] { "Date", "Mode", "Map", "Hero", "Result", "Length", "E / D", "Damage", "Healing", "Acc", "Hero SR" }) U.Txt(h, "ctd", hr);
            if (rows.Count == 0) { U.Txt("No matches yet - queue up.", "empty", b); return; }
            foreach (var m in rows)
            {
                var tr = U.Div("ctr", t);
                var at = DateTimeOffset.FromUnixTimeMilliseconds(m.at).ToLocalTime();
                U.Txt(at.ToString("d", CultureInfo.CurrentCulture) + " " + at.ToString("HH:mm", CultureInfo.InvariantCulture), "ctd", tr);
                U.Txt(CareerProfile.MODE_LABEL.TryGetValue(m.mode ?? "", out var ml) ? ml : m.mode, "ctd", tr);
                U.Txt(d.Map.TryGetValue(m.map ?? "", out var md) ? md.name : m.map, "ctd", tr);
                var hc = U.Div("ctd h", tr); Img(hc, m.hero, 28); U.Txt(Nm(m.hero), "hn", hc);
                U.Txt(m.result == "win" ? "<color=#7dff9a>VICTORY</color>" : m.result == "loss" ? "<color=#ff6b81>DEFEAT</color>" : m.result == "draw" ? "DRAW" : "<alpha=#80>-</alpha>", "ctd", tr);
                U.Txt(CareerProfile.FmtTime(m.secs), "ctd", tr);
                U.Txt($"{m.elims:0} / {m.deaths:0}", "ctd", tr);
                U.Txt(Int(m.damage), "ctd", tr); U.Txt(Int(m.healing), "ctd", tr); U.Txt($"{m.acc:0}%", "ctd", tr);
                U.Txt(m.sr.HasValue ? $"{Int(m.sr.Value)} <color={((m.srDelta ?? 0) >= 0 ? "#7dff9a" : "#ff6b81")}>{((m.srDelta ?? 0) >= 0 ? "+" : "")}{m.srDelta ?? 0:0}</color>" : "-", "ctd", tr);
            }
        }
    }
}
