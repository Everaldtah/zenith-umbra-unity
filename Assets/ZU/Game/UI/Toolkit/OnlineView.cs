// PLAY ONLINE (src/client/OnlineUI.ts): the online lobby (who's online, the connection test, Quick Play / Competitive
// role queues, custom games), matchmaking, the one-minute assemble screen (hero pick while the match stays open for
// anyone else who queues), the launch through ZU.Net's NetMatch, the end-of-match screen for online matches (online
// ranks, Hero Skill Rating), the in-match network readout, and the Starfall campaign's co-op panel. Everything goes
// through evera-b1's ZU.Net (NetDriver.Session / Coop); the node is NetConfig.NodeUrl - this file never names one.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.Career;
using ZU.Net;
using ZU.Net.Unity;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public static class OnlineView
    {
        static readonly Dictionary<string, string> ROLE_NAME = MenuState.ROLE_NAME;
        static MenuView host;
        static string notice = "", testing = "";
        static (string q, string role) lastQ = ("qp", "flex");
        static IVisualElementScheduledItem timer;
        static bool wired;

        /// <summary>a new play session starts clean (TS: a page load): the Editor keeps statics across play sessions (Enter
        /// Play Mode Options, no domain reload) while NetDriver closes its sessions on exit - a stale `wired` left the next
        /// session without OnChange/OnStart (no assemble screen, the match never loaded)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; notice = ""; testing = ""; timer = null; wired = false; coopWired = false; }

        static GameData D => ZuData.Get();
        static List<MapDef> Maps => MenuState.PlayMaps(D);
        static string Mmss(double s) => U.Clock(Math.Max(0, s));
        static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        static bool InMatch => MatchUi.Current != null;
        static void Sfx(string id) { try { if (Audio.AudioKit.Has(id)) Audio.AudioKit.Play(id, null); } catch (Exception) { /* no bank */ } }

        /// <summary>the session (created on first use with the player's name and profile card) and its events</summary>
        static OnlineSession Sess()
        {
            if (!wired)
            {
                NetDriver.PlayerName = PlayerName;
                NetDriver.Profile = ProfileCard;
            }
            var s = NetDriver.Session;
            if (!wired)
            {
                wired = true;
                s.OnChange = Refresh;
                s.OnNotice = OnNotice;
                s.OnStart = Launch;
                if (s.Speed == null) Test();
            }
            return s;
        }

        /// <summary>what other players see: the profile card, level and best online rank (TS profileCard)</summary>
        static ProfileInfo ProfileCard()
        {
            var c = Ranks.Load(); var p = CareerProfile.Load();
            var ranks = new JObject();
            foreach (var r in new[] { "tank", "damage", "support" }) ranks[r] = Ranks.RankOf(c.online[r].rating, c.online[r].games).label;
            var all = CareerProfile.Line(p, "all", "all");
            var top = new JArray(CareerProfile.TopHeroes(p, "all", "time").Take(3).Select(t =>
            {
                var o = new JObject { ["hero"] = t.hero, ["time"] = Math.Round(t.value) };
                if (p.hsr["online-comp"].TryGetValue(t.hero, out var sr) && sr.placed >= CareerProfile.HSR_PLACEMENTS) o["sr"] = sr.sr;
                return o;
            }));
            var card = new JObject { ["level"] = CareerProfile.PlayerLevel(p), ["hours"] = Math.Round(all.time / 360) / 10, ["top"] = top, ["ranks"] = ranks };
            var best = new[] { "tank", "damage", "support" }.Select(r => c.online[r]).Where(r => r.games >= Ranks.PLACEMENTS).OrderByDescending(r => r.rating).FirstOrDefault();
            return new ProfileInfo { card = card, lvl = CareerProfile.PlayerLevel(p), rank = best != null ? Ranks.RankOf(best.rating).label : "" };
        }

        static void OnNotice(string t)
        {
            notice = t; Sfx("announce");
            // a client that loses its host mid-match goes back to the lobby (once there's a winner the result screen has it)
            var s = NetDriver.Session; var mu = MatchUi.Current;
            if (mu != null && NetMatch.Current != null && s.Role == null && string.IsNullOrEmpty(mu.WorldWinner))
            {
                mu.RecordCareer("none");
                MenuState.ReturnTo = "online";
                PauseMenu.Reset();
                MatchSettings.BackToMenu();
                return;
            }
            Refresh();
        }

        static void Refresh()
        {
            var s = NetDriver.Session;
            if (InMatch || host == null || !host.IsOpen) return;
            if (s.Phase == Phase.Queue) QueueScreen();
            else if (s.Phase == Phase.Assemble) Assemble();
            else if (s.Phase == Phase.Idle && host.Showing("online")) Lobby();
        }

        static async void Test()
        {
            var s = NetDriver.Session;
            testing = "ping"; Refresh();
            try { await s.Test(step => { testing = step; Refresh(); }); }
            catch (Exception e) { Debug.LogWarning("[ZU] connection test failed: " + e.Message); }
            finally { testing = ""; Refresh(); }
        }

        // ================================================================ the online lobby
        public static void Open(MenuView menu) { host = menu; Sess(); notice = ""; Lobby(); }

        static void Lobby()
        {
            var s = Sess(); var c = Ranks.Load(); var sp = s.Speed;
            timer?.Pause(); timer = null;
            var sc = host.Screen("modes online");
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mgrid-scroll"); sc.Add(sv);
            var root = sv.contentContainer;
            U.Txt("PLAY ONLINE\n<size=16><alpha=#B3>Real players who have ZENITH//UMBRA installed, linked peer-to-peer through the online node. Two players are enough for a match - AI fills every empty seat, and every extra player replaces a bot.<alpha=#FF></size>", "m-h2", root);
            var others = s.Players.Where(p => p.platform == "desktop" || p.status != "squad").ToList();
            var st = U.Div("ostat", root, pick: true);
            U.Txt("● " + (s.Online ? "ONLINE" : "CONNECTING..."), s.Online ? "ok" : "bad", st);
            U.Txt($"{others.Count + 1} player{(others.Count > 0 ? "s" : "")} online", null, st);
            var nl = U.Div("nm-l", st); U.Txt("NAME", null, nl);
            var nm = new TextField { value = NetDriver.PlayerName, maxLength = 20 }; nm.AddToClassList("nm"); nl.Add(nm);
            nm.RegisterCallback<FocusOutEvent>(_ =>
            {
                string v = nm.value.Trim(); if (v.Length == 0) v = "Vanguard"; if (v.Length > 20) v = v.Substring(0, 20);
                // (the session renames your seat and a hosted game's line too; the co-op squad only if it is already online)
                NetDriver.PlayerName = v; s.SetName(v); if (coopWired) NetDriver.Coop.SetName(v);
                PlayerPrefs.SetString("zu-name", v); PlayerPrefs.Save();
            });
            var conn = U.Div("conn", st);
            if (sp != null && Quality.TIER.TryGetValue(sp.tier ?? "", out var t))
            {
                U.Txt($"<color={t.color}>{t.label}</color>", "cb", conn);
                U.Txt($"{sp.rtt} ms ping · {sp.jitter} ms jitter", null, conn);
                U.Txt($"{sp.downKbps / 1000.0:0.0} Mbps down · {sp.upKbps / 1000.0:0.0} Mbps up", null, conn);
                U.Txt($"up to {t.snapHz} updates/s · host score {sp.score}", null, conn);
            }
            else U.Txt("Connection not tested yet", null, conn);
            U.Btn(testing != "" ? $"TESTING {U.Up(testing)}..." : "TEST CONNECTION", null, () => { if (testing == "") Test(); }, st);
            if (notice != "") U.Txt(notice, "onotice", root);
            var grid = U.Div("ogrid", root);
            // Online Quick Play: a role queue or flex
            var qp = U.Div("ocard", grid, pick: true);
            U.Txt("ONLINE QUICK PLAY", "o-h3", qp); U.Txt("Unranked, role queue. Pick a role or flex.", "op", qp);
            var qr = U.Div("oroles", qp);
            foreach (var r in new[] { "tank", "damage", "support", "flex" })
            {
                var b = U.Btn(null, null, () => Queue("qp", r), qr);
                b.Add(RoleIcon(r)); U.Txt(ROLE_NAME[r], "zb-t", b);
            }
            U.Txt($"Quick Play record {c.oqp.wins}W - {c.oqp.games - c.oqp.wins}L", "os", qp);
            // Online Competitive: one rank per role
            var cp = U.Div("ocard", grid, pick: true);
            U.Txt("ONLINE COMPETITIVE", "o-h3", cp); U.Txt($"Ranked, one rank per role ({Ranks.PLACEMENTS} placement matches) and a Hero Skill Rating for every hero.", "op", cp);
            var cr = U.Div("oroles", cp);
            foreach (var r in new[] { "tank", "damage", "support" })
            {
                var rr = c.online[r];
                var b = U.Btn(null, "orole", () => Queue("comp", r), cr);
                b.Add(RoleIcon(r)); U.Txt(ROLE_NAME[r], "zb-t", b);
                b.Add(PauseView.Emblem(rr.rating, rr.games, 46));
                U.Txt(Ranks.RankOf(rr.rating, rr.games).label, "ors", b);
            }
            // Custom game: host on any map, or join
            var cu = U.Div("ocard", grid, pick: true);
            U.Txt("CUSTOM GAME", "o-h3", cu); U.Txt("Host a lobby on any map, or join one.", "op", cu);
            var row = U.Div("row2", cu);
            var maps = Maps;
            var dd = new DropdownField(maps.Select(m => m.name).ToList(), 0); dd.AddToClassList("bsel"); row.Add(dd);
            U.Btn("HOST", null, () => s.HostCustom(maps[Math.Max(0, dd.index)].id), row);
            var cl = U.Div("clist", cu);
            var customs = s.CustomGames().ToList();
            if (customs.Count == 0) U.Txt("No open custom games.", "dim", cl);
            foreach (var p in customs) { var li = U.Div("cli", cl); U.Txt(p.info ?? p.name, null, li); string id = p.id; U.Btn("JOIN", null, () => s.JoinCustom(id), li); }
            // who's online
            U.Txt("PLAYERS ONLINE", "o-h3", root);
            var pl = U.Div("plist", root);
            var mine = U.Div("pli me", pl); U.Txt($"<b>{NetDriver.PlayerName}</b>", null, mine); U.Txt("you", "ps", mine);
            var status = new Dictionary<string, string> { ["lobby"] = "in menus", ["squad"] = "campaign squad", ["playing"] = "in a match", ["queue"] = "searching", ["custom"] = "hosting a game", ["online"] = "in a match" };
            foreach (var p in others.Take(40))
            {
                var li = U.Div("pli", pl);
                U.Txt($"<b>{p.name}</b>", null, li);
                U.Txt($"{(p.lvl > 0 ? $"Lv {p.lvl}" : "")}{(!string.IsNullOrEmpty(p.rank) ? $" · {p.rank}" : "")}", "ps", li);
                U.Txt((status.TryGetValue(p.status ?? "", out var stt) ? stt : "") + (p.ping > 0 ? $" · {p.ping} ms" : ""), "st", li);
            }
            var bar = U.Div("bar", sc, pick: true);
            U.Btn("BACK", null, () => { s.Leave(true); host.Title(); }, bar);
        }

        static VisualElement RoleIcon(string r)
        {
            Shape p = r == "tank" ? Poly.Pct(50, 0, 100, 20, 90, 70, 50, 100, 10, 70, 0, 20) : r == "damage" ? Poly.Pct(40, 0, 60, 0, 60, 60, 80, 60, 50, 100, 20, 60, 40, 60)
                : r == "support" ? Poly.Pct(38, 0, 62, 0, 62, 38, 100, 38, 100, 62, 62, 62, 62, 100, 38, 100, 38, 62, 0, 62, 0, 38, 38, 38) : new Shape();
            p.AddToClassList("ri"); p.AddToClassList(r);
            return p;
        }

        public static void Queue(string q, string role)
        {
            var s = Sess(); var c = Ranks.Load();
            lastQ = (q, role);
            double mmr = q == "comp" && c.online.TryGetValue(role, out var rr) ? rr.mmr : c.oqp.mmr;
            notice = "";
            s.Queue(q, role, mmr);
        }

        static void QueueScreen()
        {
            var s = NetDriver.Session;
            timer?.Pause();
            var sc = host.Screen("loading find oq");
            U.Txt($"{(s.Q == "comp" ? "ONLINE COMPETITIVE" : "ONLINE QUICK PLAY")} · {ROLE_NAME[s.QueueRole]}", "ld-h2", sc);
            var stl = U.Txt("", "st", sc);
            var tips = U.Txt("", "tips", sc);
            if (notice != "") U.Txt(notice, "onotice", sc);
            var bar = U.Div("bar find-bar", sc, pick: true);
            U.Btn("CANCEL", null, () => { s.CancelQueue(); Lobby(); }, bar);
            void Draw()
            {
                stl.text = $"SEARCHING FOR PLAYERS  <color=#ffd76a>{Mmss((NowMs - s.QueueSinceMs) / 1000.0)}</color>";
                tips.text = $"<b>{Math.Max(1, s.Queued)}</b> in this queue · a match starts the moment one more player queues; for the next minute anyone else who queues joins it, and AI heroes fill every empty seat.";
            }
            Draw();
            timer = sc.schedule.Execute(() => { if (s.Phase != Phase.Queue || InMatch) { timer?.Pause(); return; } Draw(); }).Every(250);
        }

        // ================================================================ assemble (hero select)
        static void Assemble()
        {
            var s = NetDriver.Session;
            timer?.Pause();
            var me = s.MySeat;
            var m = D.Map.TryGetValue(string.IsNullOrEmpty(s.Map) ? Maps[0].id : s.Map, out var mm) ? mm : Maps[0];
            var sc = host.Screen("modes online asm");
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mgrid-scroll"); sc.Add(sv);
            var root = sv.contentContainer;
            var head = U.Div("asmhead", root);
            U.Bg(head, "map_" + m.id);
            Grad.Fill(Grad.Linear(90, (new Color(5 / 255f, 6 / 255f, 10 / 255f, 0.95f), 0), (new Color(5 / 255f, 6 / 255f, 10 / 255f, 0.55f), 100)), head);
            var ht = U.Div("ah-t", head);
            U.Txt($"{(s.Q == "custom" ? "CUSTOM GAME" : s.Q == "comp" ? "ONLINE COMPETITIVE" : "ONLINE QUICK PLAY")} <size=16><alpha=#B3>{m.name} · {(m.objective == "push" ? "MIKOSHI RUSH" : "CONTROL")}<alpha=#FF></size>", "m-h2", ht);
            U.Txt(s.Q == "custom" ? (s.Role == "host" ? "You host: start whenever you like - AI heroes fill the empty seats." : "Waiting for the host to start the game.")
                : "The match stays open while the clock runs: anyone who queues now joins it. AI heroes take every seat still empty at the start.", "tips", ht);
            var clock = U.Div("clock", head);
            Label cd = null;
            if (s.StartAtMs > 0) { U.Txt("STARTS IN", "ck-s", clock); cd = U.Txt(Mmss((s.StartAtMs - NowMs) / 1000.0), "cd", clock); }
            else if (s.Role != "host") U.Txt("WAITING", "ck-s", clock);
            if (notice != "") U.Txt(notice, "onotice", root);
            var teams = U.Div("teams", root);
            foreach (var team in new[] { "zenith", "umbra" })
            {
                var xs = s.Seats.Where(x => x.team == team).ToList(); int ai = Math.Max(0, 5 - xs.Count);
                var ot = U.Div("oteam " + team, teams);
                U.Txt($"{(team == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE")} <size=14><alpha=#99>{xs.Count} player{(xs.Count == 1 ? "" : "s")} · {ai} AI<alpha=#FF></size>", "o-h3 " + team, ot);
                foreach (var x in xs) SeatRow(ot, s, x);
                for (int i = 0; i < ai; i++)
                {
                    var li = U.Div("oli ai", ot); U.Txt("AI", "q", li);
                    var tx = U.Div("oli-t", li); U.Txt("<b>AI hero</b>", null, tx); U.Txt("fills the seat at the start", "oli-s", tx);
                }
            }
            if (me != null)
            {
                U.Txt($"CHOOSE YOUR HERO <size=14><alpha=#99>{ROLE_NAME[me.role ?? "flex"]} · {(me.team == "zenith" ? "Zenith Vanguard" : "Umbra Syndicate")}<alpha=#FF></size>", "o-h3", root);
                var pick = U.Div("row opick", root);
                var taken = new HashSet<string>(s.Seats.Where(x => x.team == me.team && x.id != s.Me).Select(x => x.hero));
                foreach (var d in OnlineSession.HeroPool(me.team, me.role))
                {
                    bool isTaken = taken.Contains(d.id);
                    var card = U.Btn(null, $"hc {d.team}{(me.hero == d.id ? " sel" : "")}{(isTaken ? " taken" : "")}", () => { if (!isTaken) { s.Pick(d.id); Assemble(); } }, pick);
                    U.Pic("portrait_" + d.id, "hc-img", card);
                    U.Txt(d.name, "hc-b", card); U.Txt(U.Up(d.role), "hc-s", card);
                    if (me.hero == d.id) card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = U.Hex(d.color);
                    if (isTaken) Filters.Set(card, Filters.Grayscale(1));
                    card.RegisterCallback<GeometryChangedEvent>(e => { float h = e.newRect.width * 4 / 3; if (Mathf.Abs(card.resolvedStyle.height - h) > 0.5f) card.style.height = h; });
                }
            }
            else U.Txt("Linking to the host...", "tips", root);
            var bar = U.Div("bar", sc, pick: true);
            if (s.Role == "host" && s.Q == "custom")
            {
                var ml = U.Div("bar-label", bar); U.Txt("MAP", "bl", ml);
                var maps = Maps;
                var dd = new DropdownField(maps.Select(x => x.name).ToList(), Math.Max(0, maps.FindIndex(x => x.id == s.Map))); dd.AddToClassList("bsel"); ml.Add(dd);
                dd.RegisterValueChangedCallback(_ => s.SetMap(maps[Math.Max(0, dd.index)].id));
                U.Btn("START GAME", "primary go", () => s.Start(), bar);
            }
            U.Btn("LEAVE", null, () => { s.Leave(); notice = ""; Lobby(); }, bar);
            if (cd != null)
                timer = sc.schedule.Execute(() => { if (s.Phase != Phase.Assemble || InMatch) { timer?.Pause(); return; } cd.text = Mmss((s.StartAtMs - NowMs) / 1000.0); }).Every(250);
        }

        static void SeatRow(VisualElement ot, OnlineSession s, Seat x)
        {
            var d = string.IsNullOrEmpty(x.hero) ? null : D.Def(x.hero);
            var li = U.Div("oli" + (x.id == s.Me ? " me" : ""), ot);
            li.style.borderLeftColor = d != null ? U.Hex(d.color) : Grad.C("#556");
            if (d != null) U.Pic("portrait_" + d.id, "oli-img", li); else U.Txt("?", "q", li);
            var tx = U.Div("oli-t", li);
            U.Txt($"<b>{x.name}</b>{(x.id == s.HostId ? "  <color=#ffd76a><size=11>HOST</size></color>" : "")}", null, tx);
            var sm = U.Div("oli-s", tx); sm.Add(RoleIcon(x.role ?? "flex")); U.Txt($"{ROLE_NAME[x.role ?? "flex"]} · {(d != null ? d.name : "choosing...")}", null, sm);
            string lk = x.id == s.HostId ? "" : x.id == s.Me ? "you" : x.link == "p2p" ? $"{(x.ping > 0 ? x.ping.ToString() : "-")} ms · P2P" : x.link == "relay" ? $"{(x.ping > 0 ? x.ping.ToString() : "-")} ms · relay" : "linking...";
            U.Txt(lk, "lk" + (x.id == s.Me || x.id == s.HostId ? "" : " " + (x.link ?? "connecting")), li);
            if (s.Role == "host" && s.Q == "custom" && x.id != s.Me) { string id = x.id; U.Btn("⇄", "sw", () => s.SwapTeam(id), li); }
        }

        // ================================================================ the match
        static void Launch(OnlineStart st)
        {
            var s = NetDriver.Session;
            timer?.Pause();
            var nm = NetMatch.Prepare(st, s);
            if (nm == null) { notice = "The match started without you (your link to the host never came up)."; if (host != null && host.IsOpen) Lobby(); return; }
            Sfx("announce");
            MenuState.Queue = null;
            LoadingView.NextTips = $"{st.seats.Count} player{(st.seats.Count == 1 ? "" : "s")} · {10 - st.seats.Count} AI · {(nm.IsHost ? "you are hosting the match" : "linked to the host")}";
            host?.Close();
            // Quick Play and Competitive are first person (TS FIXED_VIEW); custom games follow Options > Camera
            bool third = nm.Mode != "quickplay" && nm.Mode != "competitive" && ZuSettings.Current.view == "third";
            MatchSettings.Start(st.map, nm.Hero, nm.Mode, (float)nm.Skill, third);
        }

        /// <summary>the end of an online match (TS OnlineUI.results): result, your numbers, the rank / Hero Skill Rating
        /// changes; then the session finishes (a host keeps the links up 4 s so every client gets the final state)</summary>
        public static void Results(VisualElement s0, MatchRunner r, Action<string> toMenu)
        {
            var nm = NetMatch.Current; var s = NetDriver.Session;
            var w = r.World; var me = r.Player;
            bool won = me != null && w.winner == me.team;
            bool close = w.rules == "push" ? Math.Abs(w.push.best["zenith"] - w.push.best["umbra"]) < 10 : w.control.round >= 3 || w.control.overtime;
            var c = Ranks.Load();
            var sc = s0;
            U.Txt(won ? "VICTORY" : "DEFEAT", "p-h2 " + (won ? "win" : "loss"), sc);
            U.Txt($"{U.Up(CareerProfile.MODE_LABEL.TryGetValue(nm.Career, out var ml) ? ml : nm.Career)} · {w.map.name} · {nm.Start?.seats.Count ?? 0} players + {10 - (nm.Start?.seats.Count ?? 0)} AI", "sres", sc);
            if (me != null)
            {
                var ms = U.Div("mystats", sc);
                void Stat(string v, string k) { var d = U.Div("ms", ms); U.Txt(v, "ms-b", d); U.Txt(k, "ms-s", d); }
                Stat((me.kills + me.assists).ToString(), "ELIMINATIONS"); Stat(me.deaths.ToString(), "DEATHS"); Stat(U.N(me.dmgDone), "DAMAGE");
                Stat(U.N(me.healDone), "HEALING"); Stat(U.N(me.mitigated), "MITIGATED"); Stat((me.shots > 0 ? Math.Round((double)me.hits / me.shots * 100) : 0) + "%", "ACCURACY");
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nm.Career == "online-comp" && nm.MyRole != "flex" && c.online.TryGetValue(nm.MyRole ?? "", out var prev))
            {
                var ch = Ranks.ApplyCompetitive(prev, won, nm.Opp, close);
                c.online[nm.MyRole] = ch.after; c.obest[nm.MyRole] = Math.Max(c.obest.TryGetValue(nm.MyRole, out var b) ? b : 0, ch.after.rating);
                var a = Ranks.RankOf(ch.after.rating, ch.after.games);
                var ru = U.Div("rankup" + (ch.promoted ? " up" : ch.demoted ? " down" : ""), sc);
                var to = U.Div("to", ru); to.Add(PauseView.Emblem(ch.after.rating, ch.after.games, 80));
                U.Txt(a.label, "ru-l", to).style.color = a.placed ? Grad.C(Ranks.TIER_COLOR[a.tier]) : Grad.C("#aab");
                var mid = U.Div("mid", ru);
                U.Txt(ch.placedNow ? "RANK REVEALED" : a.placed ? $"{(ch.delta >= 0 ? "+" : "")}{ch.delta:0}%" : $"PLACEMENT {ch.after.games}/{Ranks.PLACEMENTS}", "dl " + (ch.delta >= 0 ? "gain" : "loss"), mid);
                var mods = U.Div("mods", mid); foreach (var x in ch.mods) U.Txt(x, "mod", mods);
                c.history.Add(new MatchLog { at = now, mode = "online-comp", role = nm.MyRole, map = w.map.id, hero = me?.baseDef.id ?? "", won = won, delta = ch.delta, mods = ch.mods, score = "" });
            }
            else if (nm.Career == "online-qp")
            {
                Ranks.ApplyQuickPlay(c, won, nm.Opp, online: true);
                c.history.Add(new MatchLog { at = now, mode = "online-qp", map = w.map.id, hero = me?.baseDef.id ?? "", won = won, score = "" });
                U.Txt($"Online Quick Play record {c.oqp.wins}W - {c.oqp.games - c.oqp.wins}L", "qp", sc);
            }
            if (nm.Career != "custom") Ranks.Save(c);
            MatchUi.Current?.RecordCareer(won ? "win" : "loss", role => c.online.TryGetValue(role, out var rr) ? rr.rating : 1800);
            PauseView.ProgressBlock(sc);
            // the host keeps the links up a moment so every client gets the final state before they close
            if (s.Role == "host") { var keep = s; sc.schedule.Execute(() => { if (keep.Phase == Phase.Playing) keep.Finish(); }).StartingIn(4000); }
            else s.Finish();
            var bb = U.Div("btns", sc);
            string career = nm.Career, role = nm.MyRole;
            if (career != "custom") U.Btn("QUEUE AGAIN", "primary", () => { lastQ = (career == "online-comp" ? "comp" : "qp", role ?? "flex"); toMenu("online-queue"); }, bb);
            U.Btn("ONLINE LOBBY", null, () => { notice = ""; toMenu("online"); }, bb);
            U.Btn("MAIN MENU", null, () => { s.Leave(true); toMenu("title"); }, bb);
        }

        /// <summary>the menu came back from an online match: the lobby, or straight back into the queue</summary>
        public static void Return(MenuView menu, string where)
        {
            host = menu; Sess();
            if (where == "online-queue") Queue(lastQ.q, lastQ.role);
            Lobby();
            Refresh();
        }

        // ================================================================ the in-match network readout (TS Game.netReadout)
        public static void Readout(Label el)
        {
            var nm = NetMatch.Current;
            if (nm == null) { U.Show(el, false); return; }
            U.Show(el, true);
            if (!nm.IsHost)
            {
                if (nm.Session.Role == null) { U.Set(el, "<color=#ff5d6d><b>● HOST LOST</b></color>"); return; }
                var t = Quality.TIER.TryGetValue(nm.Tier ?? "", out var ts) ? ts : Quality.TIER["medium"];
                string path = nm.Path == "node" ? "RELAY" : nm.Path == "turn" ? "TURN" : nm.Path == "lan" ? "LAN" : "P2P";
                U.Set(el, $"<color={t.color}><b>● {nm.Rtt} ms</b></color> · {nm.SnapHz:0} Hz {t.label} · {nm.Loss * 100:0.0}% loss · {path}\n<alpha=#BF>interp {nm.InterpMs:0} ms<alpha=#FF>");
            }
            else
            {
                var info = nm.PeerLinks();
                int worst = info.Count > 0 ? info.Max(x => x.rtt) : 0; double outK = info.Sum(x => x.kbps);
                string links = string.Join(" · ", info.Select(x => $"{(Quality.TIER.TryGetValue(x.tier ?? "", out var t) ? t.label : x.tier)} {x.rtt}ms"));
                U.Set(el, $"<color=#58ffb0><b>● HOSTING</b></color> · {info.Count} player{(info.Count == 1 ? "" : "s")} · worst ping {worst} ms\n<alpha=#BF>{links} · {outK:0} kbps out<alpha=#FF>");
            }
        }

        // ================================================================ Starfall co-op (the campaign menu's panel, TS Menu.campaign)
        static bool coopWired;
        /// <summary>the ONLINE CO-OP box: GO ONLINE, then host a squad or join an open one; the squad and its links</summary>
        public static void CoopPanel(VisualElement box, MenuView menu, string level, string hero, Action rerender)
        {
            host = menu;
            bool connected = coopWired;
            var h4 = U.Div("row2", box);
            if (!connected)
            {
                // the web's "Your name" input before GO ONLINE (CoopUI.ts): the name the squad sees
                var nm = new TextField { value = PlayerName, maxLength = 20 }; nm.AddToClassList("cnm"); h4.Add(nm);
                nm.RegisterCallback<FocusOutEvent>(_ =>
                {
                    string v = nm.value.Trim(); if (v.Length == 0) v = "Vanguard"; if (v.Length > 20) v = v.Substring(0, 20);
                    NetDriver.PlayerName = v; PlayerPrefs.SetString("zu-name", v); PlayerPrefs.Save();
                });
                U.Btn("GO ONLINE", null, () => { GoOnline(rerender); rerender(); }, h4);
                return;
            }
            var co = NetDriver.Coop;
            U.Txt(co.lobby.Brokers > 0 ? "<color=#7dff9a>● online</color>" : "<color=#ff6b81>● connecting</color>", "st", h4);
            if (notice != "") U.Txt(notice, "onotice", box);
            var row = U.Div("row2", box);
            bool inSquad = co.Role != null;
            if (inSquad) U.Txt($"<b>{(co.Role == "host" ? "Your squad" : "Joined squad")}</b>", null, row);
            else U.Btn("HOST A SQUAD", null, () => { co.Host(hero, level); rerender(); }, row);
            if (inSquad) U.Btn("LEAVE", null, () => { co.Leave(); rerender(); }, row);
            var members = U.Div("clist", box);
            foreach (var m in co.Squad)
            {
                var li = U.Div("cli", members);
                U.Txt($"{m.name}{(m.id == co.Me ? " (you)" : "")} · {D.Def(m.hero)?.name ?? m.hero}", null, li);
                U.Txt(m.id == co.HostId ? "host" : m.link ?? "", "st", li);
            }
            if (!inSquad)
            {
                U.Txt("OPEN SQUADS", "coop-h4", box);
                var list = U.Div("clist", box);
                var sq = co.Squads().ToList();
                if (sq.Count == 0) U.Txt("No open squads yet - host one and share the game with a friend.", "st", list);
                var levels = CampaignLevel.All(D);
                foreach (var p in sq)
                {
                    var li = U.Div("cli", list);
                    U.Txt($"{p.name} · {(levels.TryGetValue(p.mission ?? "", out var lv) ? lv.name : "")}", null, li);
                    string id = p.id; U.Btn("JOIN", null, () => { co.Join(id, hero); rerender(); }, li);
                }
            }
        }
        static void GoOnline(Action rerender)
        {
            if (coopWired) return;
            NetDriver.PlayerName = PlayerName;
            var c = NetDriver.Coop;
            coopWired = true;
            c.OnPlayers = _ => rerender();
            c.OnSquad = _ => rerender();
            c.OnStatus = _ => rerender();
            c.OnLeft = reason => { notice = reason; rerender(); };
            c.OnStart = (lvl, squad) => LaunchCoop(lvl, squad);
        }

        /// <summary>the player's name: the Zenith.net launcher's name#tag when started from it (TS Menu.name), else the saved one</summary>
        public static string PlayerName => Zenith.User ?? PlayerPrefs.GetString("zu-name", "Vanguard");

        /// <summary>started from a Zenith.net launcher party ("Play together", TS Menu.partyCoop, d2f85ac): straight to Starfall
        /// co-op online - the party leader hosts a squad, everyone else joins the squad that carries the same party id</summary>
        public static void PartyCoop(MenuView menu, string hero, string level, Action rerender)
        {
            if (Zenith.Party == null) return;
            host = menu;
            GoOnline(rerender);
            var c = NetDriver.Coop;
            if (Zenith.Host) { c.Host(hero, level); rerender(); return; }
            string party = Zenith.Party;
            void TryJoin()
            {
                if (!coopWired || c != NetDriver.Coop || c.Role != null) return;
                var sq = c.Squads().FirstOrDefault(p => p.party == party);
                if (sq != null) { c.Join(sq.id, hero); rerender(); }
            }
            var prev = c.OnPlayers;
            c.OnPlayers = p => { prev?.Invoke(p); TryJoin(); };
            TryJoin();
            rerender();
        }

        public static bool CoopHost => coopWired && NetDriver.Coop.Role == "host";
        public static bool CoopClient => coopWired && NetDriver.Coop.Role == "client";
        public static string CoopLevel => coopWired ? NetDriver.Coop.Level : null;
        public static void CoopSetHero(string hero) { if (coopWired) NetDriver.Coop.SetHero(hero); }
        public static void CoopSetLevel(string level) { if (coopWired) NetDriver.Coop.SetLevel(level); }
        public static void CoopStart() { if (coopWired) NetDriver.Coop.Start(); }
        public static void CoopLeave() { if (coopWired) NetDriver.Coop.Leave(); }

        static void LaunchCoop(string level, List<Member> squad)
        {
            var nm = NetMatch.PrepareCoop(level, squad, NetDriver.Coop, 0.7);
            MatchSettings.Level = level;
            host?.Close();
            MatchSettings.Start(level, nm.Hero, "campaign", (float)nm.Skill, true);
        }
    }
}
