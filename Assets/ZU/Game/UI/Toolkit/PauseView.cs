// The screens over a paused or finished match (Menu.ts pause / results / queueResults): PAUSED with resume, settings
// and quit; the result - VICTORY / DEFEAT with the score line, your numbers and, in Competitive, the rank change
// (emblems, the progress bar, the modifiers, PROMOTED / DEMOTED) - with play again, change hero and main menu. The
// pause state itself (Esc, the cursor, the career record) is PauseMenu in Front.cs.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.UI;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class PauseView
    {
        readonly VisualElement host;
        VisualElement screen;
        OptionsView options;
        MenuView swapMenu;
        string shown = "";              // "" | pause | results | options | swap

        public PauseView(VisualElement parent) { host = parent; }

        public bool OptionsOpen => shown == "options";

        public void Update(MatchRunner r)
        {
            var w = r.World; if (w == null) return;
            bool over = !string.IsNullOrEmpty(w.winner);
            if (!PauseMenu.Paused) { if (shown != "") Close(); return; }
            if (shown == "options") { options?.Update(); return; }
            if (shown == "swap")
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { swapMenu?.Close(); swapMenu = null; Pause(r); }
                return;
            }
            if (over) { if (shown != "results") Results(r); }
            else if (shown != "pause") Pause(r);
        }

        void Close()
        {
            options?.Close(); options = null;
            swapMenu?.Close(); swapMenu = null;
            screen?.RemoveFromHierarchy(); screen = null; shown = "";
        }

        VisualElement Screen(string cls)
        {
            screen?.RemoveFromHierarchy();
            screen = U.Div(cls, host, pick: true);
            // backdrop-filter: blur(6px) over the frozen frame
            screen.style.backdropFilter = new StyleList<FilterFunction>(new System.Collections.Generic.List<FilterFunction> { Filters.Blur(6) });
            return screen;
        }

        void Pause(MatchRunner r)
        {
            shown = "pause";
            var s = Screen("pause");
            U.Txt("PAUSED", "p-h2", s);
            var b = U.Div("btns", s);
            U.Btn("RESUME", "primary", PauseMenu.Resume, b);
            if (r.World?.mode == "training") U.Btn("SWITCH HERO", null, () => Swap(r), b);
            U.Btn("SETTINGS", null, () => Settings(r), b);
            U.Btn("QUIT TO MENU", null, () => { MatchUi.Current?.RecordCareer("none"); ToMenu("title"); }, b);
            U.Txt("Click the game to capture the mouse · Esc pauses", "tips", s);
        }

        void Settings(MatchRunner r)
        {
            shown = "options";
            screen?.RemoveFromHierarchy(); screen = null;
            options = OptionsView.Open(host, () => { options = null; Pause(r); });
        }

        /// <summary>Esc while a screen over the pause is open goes back to the pause (or cancels a rebind)</summary>
        public bool TakeEsc() => shown == "options" || shown == "swap";

        /// <summary>the Training Grounds' hero select over the paused match (Menu.ts heroSelect(swap)): SWITCH swaps the
        /// hero in place and resumes</summary>
        public void Swap(MatchRunner r)
        {
            shown = "swap";
            screen?.RemoveFromHierarchy(); screen = null;
            if (r.Player != null) MatchSettings.Hero = r.Player.baseDef.id;
            swapMenu = MenuView.OpenSwap(host, id =>
            {
                swapMenu?.Close(); swapMenu = null; shown = "";
                if (r.SwapHero(id)) MatchSettings.Hero = id;
                PauseMenu.Resume();
            }, () => { swapMenu?.Close(); swapMenu = null; Pause(r); });
        }

        static void ToMenu(string where)
        {
            MenuState.ReturnTo = where;
            PauseMenu.Reset();
            MatchSettings.BackToMenu();
        }

        /// <summary>a won level plays its outro (the last level the epilogue too) before the campaign menu</summary>
        void CampaignOutro(MatchRunner r)
        {
            var me = r.Player; var w = r.World;
            bool won = me != null && w.winner == me.team;
            var d = ZuData.Get();
            var ids = d.Campaign.levels.Select(j => (string)j["id"]).ToList();
            CampaignLevel.All(d).TryGetValue(r.mapId, out var L);
            var beats = won ? StoryView.Beats(L?.outro) : new System.Collections.Generic.List<(string, string)>();
            if (won && ids.IndexOf(r.mapId) == ids.Count - 1)
                beats.Add(("img/key_qelvaris.webp", "\"You have not ended the Eclipse,\" whispers a voice from the static. \"You have only made it curious.\" - To be continued."));
            screen?.RemoveFromHierarchy(); screen = null;
            StoryView.Play(beats, () => ToMenu("campaign"));
        }

        static void Again(MatchRunner r)
        {
            PauseMenu.Reset();
            string map = r.mapId;
            if (MenuState.Queue == "practice" && MenuState.MapChoice == "random") map = MenuState.RandomMap(ZuData.Get()).id;
            MatchSettings.Map = map; MatchSettings.Hero = r.playerHero; MatchSettings.Mode = r.mode; MatchSettings.Skill = r.botSkill; MatchSettings.Third = r.thirdPerson;
            MatchSettings.Autopilot = false; MatchSettings.Pending = true;
            LoadingView.LoadMatch();
        }

        // ------------------------------------------------------------------ results
        void Results(MatchRunner r)
        {
            shown = "results";
            var w = r.World; var me = r.Player;
            string q = MenuState.Queue;
            if (me != null && q != null && q == r.mode) { QueueResults(r, q); return; }
            var s = Screen("pause results");
            var h = U.Txt($"{(w.winner == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE")} WINS", "p-h2 " + w.winner, s);
            // Stadium: the round score and what you built
            var S = w.stadium;
            if (S != null) U.Txt($"STADIUM · {S.wins["zenith"]} - {S.wins["umbra"]} in rounds{(me != null ? $" · {me.items.Count} items, {me.powers.Count} powers" : "")}", "sres", s);
            ProgressBlock(s);
            // the campaign: a won level opens the next; back to the campaign menu
            if (r.mode == "campaign" && me != null && w.winner == me.team)
            {
                var ids = ZuData.Get().Campaign.levels.Select(j => (string)j["id"]).ToList();
                int i = ids.IndexOf(r.mapId); if (i >= 0) MenuView.Unlock(i + 1);
            }
            var b = U.Div("btns", s);
            if (r.mode == "campaign") { U.Btn("CONTINUE", "primary", () => CampaignOutro(r), b); U.Btn("MAIN MENU", null, () => ToMenu("title"), b); return; }
            U.Btn("PLAY AGAIN", "primary", () => Again(r), b);
            U.Btn("CHANGE HERO", null, () => ToMenu("heroes"), b);      // keeps Normal / Stadium
            U.Btn("MAIN MENU", null, () => ToMenu("title"), b);
        }

        /// <summary>Quick Play / Competitive / AI Quick Match: the result, the score, your numbers, (Competitive) the rank change</summary>
        void QueueResults(MatchRunner r, string q)
        {
            var w = r.World; var me = r.Player;
            string us = me.team, them = us == "zenith" ? "umbra" : "zenith";
            bool won = w.winner == us;
            string score = w.rules == "push" ? $"{Math.Round(w.push.best[us])}m - {Math.Round(w.push.best[them])}m" : $"{w.control.wins[us]} - {w.control.wins[them]}";
            var s = Screen("pause results queue-res");
            U.Txt(won ? "VICTORY" : "DEFEAT", "p-h2 " + (won ? "win" : "loss"), s);
            U.Txt($"{MenuState.QUEUE_NAME[q]} · {w.map.name} · {(w.rules == "push" ? "MIKOSHI RUSH" : "CONTROL")} · {score}", "sres", s);
            var ms = U.Div("mystats", s);
            void Stat(string v, string k) { var d = U.Div("ms", ms); U.Txt(v, "ms-b", d); U.Txt(k, "ms-s", d); }
            Stat((me.kills + me.assists).ToString(), "ELIMINATIONS"); Stat(me.deaths.ToString(), "DEATHS"); Stat(U.N(me.dmgDone), "DAMAGE");
            Stat(U.N(me.healDone), "HEALING"); Stat(U.N(me.mitigated), "MITIGATED"); Stat((me.shots > 0 ? Math.Round((double)me.hits / me.shots * 100) : 0) + "%", "ACCURACY");
            var ch = PauseMenu.LastChange;
            if (q == "competitive" && ch != null) RankUp(s, ch);
            else if (q == "quickplay")
            {
                var c = Career.Ranks.Load();
                U.Txt($"Quick Play record {c.qp.wins}W - {c.qp.games - c.qp.wins}L", "qp", s);
            }
            ProgressBlock(s);
            var b = U.Div("btns", s);
            U.Btn(q == "practice" ? "PLAY AGAIN" : "QUEUE AGAIN", "primary", () => { if (q == "practice") Again(r); else ToMenu("find"); }, b);
            U.Btn("CHANGE HERO", null, () => ToMenu("heroes"), b);
            U.Btn("MAIN MENU", null, () => ToMenu("title"), b);
        }

        /// <summary>hero level-ups and Hero Skill Rating changes (OnlineUI.progressHtml)</summary>
        static void ProgressBlock(VisualElement s)
        {
            var rec = MatchUi.LastRecord; if (rec == null) return;
            var d = ZuData.Get(); var p = Career.CareerProfile.Load();
            var lv = rec.levels.Where(l => l.xp > 0).ToList();
            var sr = rec.hsr.Where(h => h.qualified || h.after.placed >= Career.CareerProfile.HSR_PLACEMENTS).ToList();
            if (lv.Count == 0 && sr.Count == 0) return;
            var box = U.Div("progress", s);
            if (sr.Count > 0)
            {
                var srs = U.Div("srs", box);
                foreach (var h in sr)
                {
                    var def = d.Def(h.hero); bool placed = h.after.placed >= Career.CareerProfile.HSR_PLACEMENTS;
                    var row = U.Div("srch", srs);
                    if (placed) row.Add(Emblem(Career.CareerProfile.HsrToRating(h.after.sr), 99, 34));
                    U.Txt(def?.name ?? h.hero, "pg-b", row).style.color = U.Hex(def?.color ?? "#ffffff");
                    string sign = h.delta >= 0 ? "+" : "", dc = h.delta >= 0 ? "#5cffa7" : "#ff5d7a";
                    U.Txt(placed ? $"HERO SR {U.N(h.after.sr)} <color={dc}>{sign}{h.delta:0}</color>{(h.placedNow ? " \u00b7 PLACED" : "")}" : $"HERO PLACEMENT {h.after.placed}/{Career.CareerProfile.HSR_PLACEMENTS}", "pg-s", row);
                }
            }
            if (lv.Count > 0)
            {
                var lvs = U.Div("lvs", box);
                foreach (var l in lv)
                {
                    var def = d.Def(l.hero); var v = Career.CareerProfile.HeroLevel(p.xp.TryGetValue(l.hero, out var x) ? x : 0);
                    var row = U.Div("lvup", lvs);
                    U.Pic("portrait_" + l.hero, "lv-img", row);
                    U.Txt(def?.name ?? l.hero, "pg-b", row).style.color = U.Hex(def?.color ?? "#ffffff");
                    U.Txt((l.to > l.from ? $"LEVEL UP {l.from} \u2192 {l.to}" : $"Level {l.to}") + $" \u00b7 +{l.xp:0} XP", "pg-s", row);
                    var bar = U.Div("lv-bar", row); var fill = U.Div("lv-fill", bar); fill.style.width = new Length((float)v.pct, LengthUnit.Percent);
                }
            }
        }

        /// <summary>a rank emblem (Menu.ts emblem): the tier colour once placed, grey with a ? before</summary>
        public static EmblemEl Emblem(double rating, int games, float size)
        {
            var v = Career.Ranks.RankOf(rating, games);
            return new EmblemEl(v.placed ? v.color : Grad.C("#7a8199"), v.placed ? v.division.ToString() : "?", size);
        }

        static void RankUp(VisualElement s, Career.RankChange ch)
        {
            var b = Career.Ranks.RankOf(ch.before.rating, ch.before.games);
            var a = Career.Ranks.RankOf(ch.after.rating, ch.after.games);
            double d = ch.delta;
            var ru = U.Div("rankup" + (ch.promoted ? " up" : ch.demoted ? " down" : ""), s);
            var from = U.Div("from", ru); from.Add(Emblem(ch.before.rating, ch.before.games, 64)); U.Txt(b.label, "ru-l", from);
            var mid = U.Div("mid", ru);
            var bar = U.Div("pbar big", mid);
            var old = U.Div("old", bar);
            old.style.width = new Length((float)(a.placed ? (ch.promoted ? 0 : Math.Min(a.pct, b.pct)) : 0), LengthUnit.Percent);
            var nw = U.Div("new " + (d >= 0 ? "gain" : "loss"), bar);
            nw.style.left = new Length((float)(a.placed ? Math.Min(a.pct, ch.promoted ? 0 : b.pct) : 0), LengthUnit.Percent);
            nw.style.width = new Length((float)(a.placed ? Math.Abs(ch.promoted || ch.demoted ? a.pct : a.pct - b.pct) : 0), LengthUnit.Percent);
            U.Txt(ch.placedNow ? "RANK REVEALED" : a.placed ? $"{(d >= 0 ? "+" : "")}{d:0}%" : $"PLACEMENT {ch.after.games}/{Career.Ranks.PLACEMENTS}", "dl " + (d >= 0 ? "gain" : "loss"), mid);
            var mods = U.Div("mods", mid);
            foreach (var m in ch.mods) U.Txt(m, "mod", mods);
            if (ch.promoted) U.Txt($"PROMOTED TO {U.Up(a.label)}", "bn", mid);
            else if (ch.demoted) U.Txt($"DEMOTED TO {U.Up(a.label)}", "bn", mid);
            var to = U.Div("to", ru); to.Add(Emblem(ch.after.rating, ch.after.games, 80));
            var tl = U.Txt(a.label, "ru-l", to); tl.style.color = a.placed ? Grad.C(Career.Ranks.TIER_COLOR[a.tier]) : Grad.C("#aab");
        }
    }
}
