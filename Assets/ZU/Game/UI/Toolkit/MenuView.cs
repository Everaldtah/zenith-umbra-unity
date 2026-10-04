// The front-end screens (src/client/Menu.ts): the title, the queues (Quick Play / Competitive role select, AI Quick
// Match difficulty and map), matchmaking (SEARCHING -> MATCH FOUND), hero select with the hero's whole kit, rival and
// lore, map select (Watch AI vs AI, AI Test Lab), the Starfall campaign menu, and the ways into Options, the Hero
// Viewer and the Career Profile. A match hands back to the screen MenuState.ReturnTo names.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.UI;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class MenuView
    {
        static readonly (string name, double v, string desc)[] DIFFS = { ("RECRUIT", 0.4, "Relaxed bots: slower aim, slower reactions."), ("VETERAN", 0.62, "A fair fight."), ("ELITE", 0.8, "Sharp aim, quick ability use."), ("LEGEND", 0.95, "Top-tier bots that punish every mistake.") };

        /// <summary>modes whose view is fixed (TS Game FIXED_VIEW); the rest follow Options > Camera and V</summary>
        public static readonly Dictionary<string, string> FIXED_VIEW = new Dictionary<string, string> { ["skirmish"] = "first", ["stadium"] = "third", ["quickplay"] = "first", ["competitive"] = "first", ["practice"] = "first" };

        static MenuView current;
        /// <summary>the menu on screen (the UI tour drives it)</summary>
        public static MenuView Current => current;
        /// <summary>the mode the next hero select / launch is for (TS Menu.mode)</summary>
        public string ModeName { get => mode; set => mode = value; }
        public void OpenCareer() => Career_();
        public void OpenSettings() => Settings(Title);
        readonly VisualElement root;
        readonly GameData d;
        readonly List<HeroDef> heroes;
        readonly List<MapDef> playMaps;
        string mode = "quickplay";
        IVisualElementScheduledItem timer;
        Action<string> swapPick; Action swapBack;         // hero select over a Training Grounds match (SWITCH HERO)

        MenuView(VisualElement parent)
        {
            root = U.Div("menu", parent, pick: true);
            d = ZuData.Get();
            heroes = MenuState.Roster(d);
            playMaps = MenuState.PlayMaps(d);
            if (d.Def(MatchSettings.Hero) == null) MatchSettings.Hero = "raijin";
            if (!d.Map.ContainsKey(MatchSettings.Map) || d.Map[MatchSettings.Map].retired) MatchSettings.Map = playMaps[0].id;
            mode = MatchSettings.Mode;
        }

        /// <summary>open the menu on the screen a match handed back to (or the title)</summary>
        public static MenuView Open(VisualElement parent)
        {
            current?.root.RemoveFromHierarchy();
            var m = current = new MenuView(parent);
            string to = MenuState.ReturnTo ?? "title"; MenuState.ReturnTo = "title";
            if (to == "heroes") m.HeroSelect();
            else if (to == "find" && MenuState.Queue != null) m.FindMatch();
            else if (to == "campaign") m.Campaign();
            else if (to.StartsWith("viewer:")) m.Viewer(to.Substring(7));
            else if (to == "career") { m.Title(); m.Career_(); }               // back from a highlight's replay
            else if (to == "online" || to == "online-queue") OnlineView.Return(m, to);
            else m.Title();
            // started from a Zenith.net launcher party: once per run, straight to Starfall co-op online (TS Menu.partyCoop)
            if (!partyStarted && ZU.Net.Zenith.Party != null) { partyStarted = true; m.Campaign(); OnlineView.PartyCoop(m, cHero, cLevel, m.Campaign); }
            return m;
        }
        static bool partyStarted;

        /// <summary>a highlight's replay ended (Highlights.Watch's done): Career > History again - on the menu that is up, or on
        /// the next one to open</summary>
        public static void BackToCareer()
        {
            if (current != null && current.root.panel != null)
            {
                MenuState.ReturnTo = "title";
                if (CareerView.Current == null) { current.Title(); current.Career_(); }
            }
            else MenuState.ReturnTo = "career";
        }
        /// <summary>a new play session starts clean (the Editor keeps statics across play sessions without a domain reload)</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { partyStarted = false; }

        /// <summary>hero select over a running Training Grounds match: SWITCH picks, BACK returns to the pause</summary>
        public static MenuView OpenSwap(VisualElement parent, Action<string> pick, Action back)
        {
            var m = new MenuView(parent) { mode = "training", swapPick = pick, swapBack = back };
            MenuState.Queue = null;
            m.HeroSelect(swap: true);
            return m;
        }

        /// <summary>a fresh screen for the online pages (OnlineView)</summary>
        public VisualElement Screen(string cls) => Show(cls);
        public bool IsOpen => root.panel != null;
        /// <summary>the screen showing has this class (the online lobby refreshes itself only while it's up)</summary>
        public bool Showing(string cls) => root.childCount > 0 && root[0].ClassListContains(cls);

        public void Close() { timer?.Pause(); root.RemoveFromHierarchy(); if (current == this) current = null; }

        /// <summary>a fresh screen in the menu root (the TS show(html))</summary>
        VisualElement Show(string cls)
        {
            timer?.Pause(); timer = null;
            root.Clear();
            U.Show(root, true);
            return U.Div(cls, root, pick: true);
        }

        HeroDef Hero(string id) => d.Def(id);
        string HeroId { get => MatchSettings.Hero; set => MatchSettings.Hero = value; }
        string MapId { get => MatchSettings.Map; set => MatchSettings.Map = value; }

        // ================================================================ title
        public void Title()
        {
            var s = Show("title");
            var bg = U.Div("bg", s); U.Bg(bg, U.Sat("map_hanabi")); Filters.Set(bg, Filters.Brightness(0.55f));
            KenBurns(bg);
            // no left fade: style.css has one (.title::after), but its ".title > * { z-index: 1 }" also lifts the .bg
            // above it, so the PC game shows the picture unshaded
            var logo = U.Div("logo", s);
            U.Txt("ZENITH", "lz", logo); U.Txt("//", "li", logo); U.Txt("UMBRA", "lu", logo);
            U.Txt("ELEVEN HEROES. TWO OATHS. ONE ECLIPSE.", "tag", s);
            var c = Career.Ranks.Load();
            var best = new[] { "tank", "damage", "support" }.Select(r => c.roles[r]).OrderByDescending(r => r.rating * (r.games >= Career.Ranks.PLACEMENTS ? 1 : 0)).First();
            var b = U.Div("btns two", s);
            var row1 = U.Div("brow", b);
            U.Btn("PLAY ONLINE\n<size=12><alpha=#B3>with other players<alpha=#FF></size>", "primary", () => OnlineView.Open(this), row1);
            U.Btn("QUICK PLAY", "primary", () => QueueSelect("quickplay"), U.Div("brow", b));
            void Pair(string a, Action fa, string bb, Action fb) { var r = U.Div("brow", b); U.Btn(a, null, fa, r); if (bb != null) U.Btn(bb, null, fb, r); }
            Pair($"COMPETITIVE\n<size=12><alpha=#B3>{Career.Ranks.RankOf(best.rating, best.games).label}<alpha=#FF></size>", () => QueueSelect("competitive"), "AI QUICK MATCH", () => QueueSelect("practice"));
            Pair("STADIUM", () => Mode("stadium"), "CAMPAIGN · STARFALL", Campaign);
            Pair("TRAINING GROUNDS", () => Mode("training"), "CAREER PROFILE", Career_);
            Pair("HERO VIEWER & SKINS", () => Viewer(null), "WATCH AI VS AI", () => Mode("spectate"));
            Pair("AI TEST LAB", () => Mode("aitest"), "SETTINGS", () => Settings(Title));
            Pair("QUIT", Application.Quit, null, null);
            U.Txt($"Full edition · Desktop · Unity · quality {U.Up(ZuSettings.Current.preset)}", "ver", s);
        }

        void Mode(string m)
        {
            MenuState.Queue = null;
            mode = m;
            if (m == "spectate" || m == "aitest") MapSelect();
            else HeroSelect();
        }

        void KenBurns(VisualElement bg)
        {
            float t0 = Time.unscaledTime;
            bg.schedule.Execute(() =>
            {
                // @keyframes kb (30s ease-in-out infinite alternate): scale 1.05 -> 1.18, drifting -2% / -1%
                float k = Mathf.PingPong((Time.unscaledTime - t0) / 30f, 1); k = k * k * (3 - 2 * k);
                float sc = Mathf.Lerp(1.05f, 1.18f, k);
                bg.style.scale = new Scale(new Vector3(sc, sc, 1));
                bg.style.translate = new Translate(new Length(-2 * k, LengthUnit.Percent), new Length(-k, LengthUnit.Percent));
            }).Every(16);
        }

        // ================================================================ queues
        /// <summary>Quick Play / Competitive: pick a role (role queue); AI Quick Match: difficulty and map</summary>
        public void QueueSelect(string q)
        {
            MenuState.Queue = q; mode = q;
            var c = Career.Ranks.Load();
            if (q == "practice")
            {
                var s = Show("modes queue");
                Backdrop(s, 50, 0);
                U.Txt("AI QUICK MATCH\n<size=16><alpha=#B3>You and four AI teammates against an AI team - best-of-3 Control or Mikoshi Rush, no rank on the line<alpha=#FF></size>", "m-h2", s);
                U.Txt("DIFFICULTY", "q-h3", s);
                var diffs = U.Div("diffs", s);
                foreach (var (n, v, desc) in DIFFS)
                {
                    var card = Card(diffs, Math.Abs(v - MenuState.Diff) < 0.01, () => { MenuState.Diff = v; QueueSelect(q); });
                    U.Txt(n, "c-b", card); U.Txt(desc, "c-p", card);
                }
                U.Txt("MAP", "q-h3", s);
                var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mgrid-scroll"); s.Add(sv);
                var grid = U.Div("mgrid mapsq", sv.contentContainer);
                var rnd = MapCard(grid, null, MenuState.MapChoice == "random", () => { MenuState.MapChoice = "random"; QueueSelect(q); });
                U.Txt("?", "rnd", rnd); rnd.Insert(0, rnd[rnd.childCount - 1]);
                U.Txt("Random map", "mc-b", rnd); U.Txt("Any arena, any mode.", "mc-p", rnd);
                foreach (var m in playMaps)
                {
                    var mc = MapCard(grid, m, m.id == MenuState.MapChoice, () => { MenuState.MapChoice = m.id; QueueSelect(q); });
                    U.Txt(m.name, "mc-b", mc); U.Txt(m.objective == "push" ? "MIKOSHI RUSH" : "CONTROL", "mc-p", mc);
                }
                StretchRows(sv, grid, 8);              // (.mapsq: repeat(auto-fill, minmax(200px, 1fr)) = 8 at 1920)
                var bar = Bar(s);
                U.Btn("BACK", null, Title, bar);
                U.Btn("CHOOSE HERO", "primary", () => { MenuState.Role = "flex"; HeroSelect(); }, bar);
                return;
            }
            var roles = q == "competitive" ? new[] { "tank", "damage", "support" } : new[] { "tank", "damage", "support", "flex" };
            var blurb = new Dictionary<string, string> { ["tank"] = "Hold the space, lead the fight.", ["damage"] = "Find the kills, open the fight.", ["support"] = "Keep the team alive, turn the fight.", ["flex"] = "Any hero, any role." };
            var sc = Show("modes queue");
            Backdrop(sc, 50, 0);
            U.Txt($"{MenuState.QUEUE_NAME[q]}\n<size=16><alpha=#B3>{(q == "competitive" ? $"Ranked, role queue - one rank per role. {Career.Ranks.PLACEMENTS} placement matches reveal it; every match after that moves it." : "Unranked, role queue or flex - a lobby matched to your skill, a random map and mode.")}<alpha=#FF></size>", "m-h2", sc);
            var rl = U.Div("roles", sc);
            foreach (var r in roles)
            {
                var card = Card(rl, false, () => { MenuState.Role = r; HeroSelect(); });
                card.AddToClassList("role");
                card.Add(RoleIcon(r));
                U.Txt(MenuState.ROLE_NAME[r], "c-b", card); U.Txt(blurb[r], "c-p", card);
                if (q == "competitive" && r != "flex")
                {
                    var rr = c.roles[r];
                    card.Add(PauseView.Emblem(rr.rating, rr.games, 72));
                    U.Txt(Career.Ranks.RankOf(rr.rating, rr.games).label, "rl", card);
                    U.Txt($"{rr.wins}W - {rr.losses}L", "rec", card);
                }
            }
            var b2 = Bar(sc);
            U.Btn("BACK", null, Title, b2);
        }

        /// <summary>the role icons (.ri): clip-path shapes in gold</summary>
        static VisualElement RoleIcon(string role)
        {
            Shape p = role == "tank" ? Poly.Pct(50, 0, 100, 20, 90, 70, 50, 100, 10, 70, 0, 20)
                : role == "damage" ? Poly.Pct(40, 0, 60, 0, 60, 60, 80, 60, 50, 100, 20, 60, 40, 60)
                : role == "support" ? Poly.Pct(38, 0, 62, 0, 62, 38, 100, 38, 100, 62, 62, 62, 62, 100, 38, 100, 38, 62, 0, 62, 0, 38, 38, 38)
                : new Shape();
            p.AddToClassList("ri"); p.AddToClassList(role);
            return p;
        }

        VisualElement Card(VisualElement parent, bool sel, Action click)
        {
            var c = U.Btn(null, "card" + (sel ? " sel" : ""), click, parent);
            return c;
        }

        VisualElement MapCard(VisualElement parent, MapDef m, bool sel, Action click)
        {
            var c = U.Btn(null, "mc" + (sel ? " sel" : ""), click, parent);
            if (m != null)
            {
                Wide(U.Pic("map_" + m.id, "mc-img", c));      // CSS `.mc img { aspect-ratio: 16/9 }`
            }
            return c;
        }

        /// <summary>CSS aspect-ratio: 16/9 on an image (USS has no aspect-ratio): its height follows its width</summary>
        static VisualElement Wide(VisualElement img)
        {
            img.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float h = e.newRect.width * 9f / 16f;
                if (h > 0 && Mathf.Abs(img.resolvedStyle.height - h) > 0.5f) img.style.height = h;
            });
            return img;
        }

        /// <summary>the web's map grid is a CSS grid that fills the screen's height, its auto rows stretched: tall cards, the
        /// art on top. Here the cards sit in a scroll view, so each card's min-height is set to its share of the view</summary>
        static void StretchRows(ScrollView sv, VisualElement grid, int cols)
        {
            sv.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                int n = grid.childCount, rows = (n + cols - 1) / cols;
                float h = sv.contentViewport.layout.height;
                if (rows == 0 || float.IsNaN(h) || h <= 0) return;
                float cell = Mathf.Max(0, (h - rows * 14f) / rows);          // (each card keeps its 14 px margin below)
                foreach (var c in grid.Children()) c.style.minHeight = cell;
            });
        }

        VisualElement Bar(VisualElement s) => U.Div("bar", s, pick: true);

        void Backdrop(VisualElement s, float ax, float ay, string inner = "#1b2140")
        {
            var g = Grad.Fill(Grad.Radial(ax, ay, (Grad.C(inner), 0), (Grad.C("#05060a"), 70)));
            s.Insert(0, g);
        }

        /// <summary>matchmaking (the lobby is AI, matched to your rating): search, then MATCH FOUND with the map and mode</summary>
        public void FindMatch()
        {
            string q = MenuState.Queue; var c = Career.Ranks.Load();
            var rr = q == "competitive" && c.roles.TryGetValue(MenuState.Role, out var x) ? x : null;
            double mmr = rr != null ? rr.mmr : c.qp.mmr;
            MatchSettings.Opp = Career.Ranks.LobbyRating(mmr);
            MapId = q != "practice" || MenuState.MapChoice == "random" ? MenuState.RandomMap(d).id : MenuState.MapChoice;
            var m = d.Map[MapId];
            var s = Show("loading find");
            var bg = U.Div("bg", s); U.Bg(bg, "map_" + MapId); bg.style.opacity = 0.25f;
            U.Txt($"{MenuState.QUEUE_NAME[q]} · {MenuState.ROLE_NAME[MenuState.Role]}", "ld-h2", s);
            var st = U.Txt("SEARCHING FOR A MATCH  <color=#ffd76a>0:00</color>", "st", s);
            U.Txt($"Matching lobby near {(rr != null ? Career.Ranks.RankOf(mmr, Career.Ranks.PLACEMENTS).label : "your skill")}", "tips", s);
            var bar = Bar(s); bar.AddToClassList("find-bar");
            U.Btn("CANCEL", null, () => QueueSelect(q), bar);
            float t = 0, need = 1.6f + UnityEngine.Random.value * 2.2f;
            timer = s.schedule.Execute(() =>
            {
                t += 0.25f; st.text = $"SEARCHING FOR A MATCH  <color=#ffd76a>0:{(int)t:00}</color>";
                if (t < need) return;
                timer.Pause();
                Sfx("announce");
                var f = Show("loading find found");
                var bg2 = U.Div("bg", f); U.Bg(bg2, U.Sat("map_" + MapId)); Filters.Set(bg2, Filters.Brightness(0.55f));
                U.Txt("MATCH FOUND", "mf", f);
                U.Txt(m.name, "ld-h2", f);
                U.Txt(m.objective == "push" ? "MIKOSHI RUSH · push the festival float through the enemy gate" : "CONTROL · best of 3 rounds on the capture point", "obj", f);
                U.Txt(m.story, "ld-p", f);
                U.Txt($"Lobby rating ≈ {Career.Ranks.RankOf(MatchSettings.Opp, Career.Ranks.PLACEMENTS).label} · WASD move · SPACE jump · LMB / RMB fire · SHIFT / E abilities · Q ultimate · F swoop (Mirei) · TAB stats", "tips", f);
                timer = f.schedule.Execute(Launch);
                timer.ExecuteLater(2200);
            }).Every(250);
        }

        static void Sfx(string id) { try { if (Audio.AudioKit.Has(id)) Audio.AudioKit.Play(id, null); } catch (Exception) { /* no bank */ } }

        // ================================================================ hero select
        VisualElement HeroCard(VisualElement parent, HeroDef h, bool sel, Action click, string extra = "")
        {
            var c = U.Btn(null, $"hc {h.team}{(sel ? " sel" : "")} {extra}", click, parent);
            c.userData = h.id;
            var col = U.Hex(h.color);
            // the card, its corner cut, portrait, team tint and (selected) border as one mesh - see CardFace
            var face = new CardFace(U.Img("portrait_" + h.id), h.team == "zenith" ? Grad.C("#5cc8ff") : Grad.C("#ff3b5c")) { Border = sel ? col : (Color?)null };
            face.AddToClassList("hc-img");
            c.Add(face);
            U.Txt(h.name, "hc-b", c);
            U.Txt(U.Up(h.role), "hc-s", c);
            // aspect-ratio 3/4
            c.RegisterCallback<GeometryChangedEvent>(e => { float hgt = e.newRect.width * 4 / 3; if (Mathf.Abs(c.resolvedStyle.height - hgt) > 0.5f) c.style.height = hgt; });
            return c;
        }

        void Kbd(VisualElement parent, string k) => U.Txt(k, "kbd", parent);

        VisualElement HeroDetail(HeroDef h)
        {
            var col = U.Hex(h.color);
            var hd = U.Div("hd");
            var art = U.Div("art", hd);
            U.Bg(art, U.Img("key_" + h.id) != null ? "key_" + h.id : "portrait_" + h.id);
            // .hd .art: a hairline border (var(--line), from the USS) and box-shadow 0 0 40px -10px var(--c) - the glow in the
            // hero's colour, as a drop-shadow filter (sigma = blur / 2, less the -10px spread)
            Filters.Set(art, UiText.DropShadow(0, 0, 15, col));
            var info = U.Div("info", hd);
            var h2 = U.Txt(h.name, "hd-h2", info); h2.style.color = col;
            U.Txt(U.Up(h.title), "hd-sub", info);
            var meta = U.Div("meta", info);
            var team = U.Txt(MenuState.TEAM_NAME.TryGetValue(h.team ?? "", out var tn) ? tn : h.team, "chip " + h.team, meta);
            U.Txt(U.Up(h.role), "chip", meta);
            U.Txt($"{h.hp + h.armor:0} HP{(h.armor > 0 ? $" ({h.armor:0} armor)" : "")}", "chip", meta);
            if (h.frame == "flyer") U.Txt("FLYER", "chip", meta);
            if (h.frame == "mech") U.Txt("MECHA", "chip", meta);
            if (h.pilot != null)
            {
                var p = U.Div("pilot", info);
                U.Pic("portrait_" + h.pilot.id, "pilot-img", p);
                U.Txt($"<b>Pilot: {h.pilot.name}</b> {h.pilot.bio}", "pilot-t", p);
            }
            U.Txt(h.lore ?? "", "lore", info);
            void Ab(string k, string n, string desc, string counter = null)
            {
                var a = U.Div("ab", info); Kbd(a, k);
                var t = U.Div("ab-t", a);
                U.Txt(n, "ab-b", t); U.Txt(desc ?? "", "ab-p", t);
                if (!string.IsNullOrEmpty(counter)) U.Txt($"⚔ COUNTER: {counter}", "ab-p ctr", t);
            }
            var pr = h.primary; var S = h.secondary;
            string pname = pr.name ?? (pr.kind == "charge" ? "Charged shot" : pr.kind == "melee" ? "Melee strikes" : pr.kind == "beam" ? "Close-range stream" : "Primary fire");
            Ab("LMB", pname, $"{pr.damage:0.##}{(pr.pellets.HasValue ? $"×{pr.pellets}" : "")} dmg{(pr.kind == "beam" ? "/s" : "")}{(pr.splash.HasValue ? $", {pr.splash:0.##}m splash" : "")}{(pr.ammo.HasValue ? $", {pr.ammo} rounds" : "")}{(pr.sweep ? $", {pr.range:0.##}m sweeping arc" : "")}{(!string.IsNullOrEmpty(pr.note) ? $". {pr.note}" : "")}");
            if (S != null && S.IsAbility) Ab("RMB", S.name, S.desc, S.counter);
            else if (S != null) Ab("RMB", S.heal ? "Healing" : "Alt fire", S.heal ? $"Heals allies {S.damage:0.##}{(S.kind == "beam" ? "/s" : "")}" : $"{S.damage:0.##} dmg");
            if (h.ability1 != null) Ab("SHIFT", h.ability1.name, h.ability1.desc, h.ability1.counter);
            if (h.ability2 != null) Ab("E", h.ability2.name, h.ability2.desc, h.ability2.counter);
            if (h.ult != null) Ab("Q", h.ult.name + " (ULT)", h.ult.desc);
            if (h.passive != null) Ab("—", h.passive.name + " (passive)", h.passive.desc);
            if (Roles.ROLE_PASSIVE.TryGetValue(h.role ?? "", out var rp))
            {
                var sr = !string.IsNullOrEmpty(h.subrole) && Roles.SUBROLE.TryGetValue(h.subrole, out var x) ? x : default;
                Ab("◆", $"{rp.name} role{(sr.name != null ? " · " + sr.name : "")}", $"{rp.desc}{(sr.desc != null ? " " + sr.desc : "")} Everyone regenerates {Roles.REGEN_RATE:0} HP/s after {Roles.REGEN_DELAY:0}s without taking damage.");
            }
            Ab("C", "Quick melee", "A fast punch for 40 damage - every hero has one (0.9s cooldown).");
            var rival = Hero(h.rival);
            if (rival != null) U.Txt($"RIVAL: <b><color={rival.color}>{rival.name}</color></b>, {rival.title}.\n<size=14><alpha=#B3>{h.inspiration}<alpha=#FF></size>", "rival", info);
            return hd;
        }

        public void HeroSelect(bool browse = false, bool swap = false)
        {
            string q = MenuState.Queue;
            bool RoleOk(HeroDef x) => q == null || MenuState.Role == "flex" || (MenuState.ROLE_HERO.TryGetValue(MenuState.Role, out var hr) && x.role == hr);
            if (Hero(HeroId) == null || !RoleOk(Hero(HeroId))) HeroId = heroes.First(RoleOk).id;
            var s = Show("select");
            Backdrop(s, 30, 20);
            var top = U.Div("sel-top", s);
            var gsv = new ScrollView(ScrollViewMode.Vertical); gsv.AddToClassList("grid"); top.Add(gsv);
            var grid = gsv.contentContainer;
            var detailSv = new ScrollView(ScrollViewMode.Vertical); detailSv.AddToClassList("detail"); top.Add(detailSv);
            var cards = new List<VisualElement>();
            void Pick(string id)
            {
                HeroId = id;
                foreach (var c in cards)
                {
                    bool on = (string)c.userData == id; U.Toggle(c, "sel", on);
                    var face = c.Q<CardFace>();
                    if (face != null) face.Border = on ? U.Hex(Hero((string)c.userData).color) : (Color?)null;
                }
                detailSv.contentContainer.Clear();
                detailSv.contentContainer.Add(HeroDetail(Hero(id)));
                detailSv.scrollOffset = Vector2.zero;
            }
            foreach (var (team, title, small) in new[] { ("zenith", "ZENITH VANGUARD", "heroes"), ("umbra", "UMBRA SYNDICATE", "villains") })
            {
                U.Txt($"{title} <size=14><alpha=#99>{small}<alpha=#FF></size>", "s-h3 " + team, grid);
                var row = U.Div("row", grid);
                foreach (var h in heroes.Where(x => x.team == team && RoleOk(x)))
                    cards.Add(HeroCard(row, h, h.id == HeroId, () => Pick(h.id)));
            }
            Pick(HeroId);
            var bar = Bar(s);
            if (q != null) U.Txt($"{MenuState.QUEUE_NAME[q]} · {MenuState.ROLE_NAME[MenuState.Role]}{(q == "practice" ? " · " + (DIFFS.FirstOrDefault(x => Math.Abs(x.v - MenuState.Diff) < 0.01).name ?? "") : "")}", "qtag", bar);
            DropdownField mapSel = null, diffSel = null;
            if (!swap && !browse && mode != "training" && q == null)
            {
                var ml = U.Div("bar-label", bar); U.Txt("MAP", "bl", ml);
                var names = playMaps.Select(m => m.name).ToList();
                mapSel = new DropdownField(names, Math.Max(0, playMaps.FindIndex(m => m.id == MapId))); mapSel.AddToClassList("bsel"); ml.Add(mapSel);
                var al = U.Div("bar-label", bar); U.Txt("AI", "bl", al);
                var dv = new[] { 0.35, 0.65, 0.9 };
                double cur = ZuSettings.Current.difficulty;
                int di = Array.IndexOf(dv, dv.OrderBy(v => Math.Abs(v - cur)).First());
                diffSel = new DropdownField(new List<string> { "Cadet", "Vanguard", "Eclipse" }, di); diffSel.AddToClassList("bsel"); al.Add(diffSel);
            }
            U.Btn("BACK", null, () => { if (swap) swapBack?.Invoke(); else if (q != null) QueueSelect(q); else Title(); }, bar);
            if (!browse)
            {
                string go = swap ? "SWITCH" : q != null && q != "practice" ? "FIND MATCH" : mode == "training" ? "ENTER TRAINING" : mode == "stadium" ? "ENTER STADIUM" : "START MATCH";
                U.Btn(go, "primary go", () =>
                {
                    if (swap) { swapPick?.Invoke(HeroId); return; }
                    if (q != null && q != "practice") { FindMatch(); return; }
                    if (q == "practice") { MapId = MenuState.MapChoice == "random" ? MenuState.RandomMap(d).id : MenuState.MapChoice; }
                    if (mapSel != null) MapId = playMaps[Math.Max(0, mapSel.index)].id;
                    if (diffSel != null) { var s2 = ZuSettings.Current; s2.difficulty = new[] { 0.35, 0.65, 0.9 }[Math.Max(0, diffSel.index)]; ZuSettings.Save(s2); }
                    Launch();
                }, bar);
            }
        }

        // ================================================================ maps (watch / AI lab)
        public void MapSelect()
        {
            var s = Show("maps");
            Backdrop(s, 50, 0);
            U.Txt(mode == "aitest" ? "AI TEST LAB\n<size=16><alpha=#B3>bots play every map in turn while the lab checks animation, movement, physics, effects and sound<alpha=#FF></size>" : "WATCH AI VS AI", "m-h2", s);
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mgrid-scroll"); s.Add(sv);
            var grid = U.Div("mgrid", sv.contentContainer);
            var cards = new List<VisualElement>();
            foreach (var m in playMaps)
            {
                var mc = MapCard(grid, m, m.id == MapId, null);
                mc.userData = m.id; cards.Add(mc);
                ((ZButton)mc).clicked = () => { MapId = m.id; foreach (var c in cards) U.Toggle(c, "sel", (string)c.userData == m.id); };
                U.Txt(m.name, "mc-b", mc); U.Txt(m.story, "mc-p", mc);
            }
            StretchRows(sv, grid, 5);                  // (.mgrid: repeat(5, 1fr))
            var bar = Bar(s);
            U.Btn("BACK", null, Title, bar);
            U.Btn(mode == "aitest" ? "RUN ALL MAPS" : "WATCH", "primary go", () => { if (mode == "aitest") AiLab.Begin(MapId); Launch(); }, bar);
        }

        // ================================================================ launch
        /// <summary>the match: map, hero (none when watching), the bots' skill (the lobby's rating in the queues, the chosen
        /// difficulty in AI Quick Match), the view (Quick Play / Competitive / AI Quick Match are first person, Stadium and
        /// the campaign third, the rest per Options > Camera)</summary>
        public void Launch()
        {
            if (mode == "campaign") { PlayLevel(cLevel, cHero); return; }
            string q = MenuState.Queue;
            string map = mode == "training" ? "training" : MapId;
            float skill = q == "practice" ? (float)MenuState.Diff : q != null ? (float)Career.Ranks.SkillFor(MatchSettings.Opp) : (float)ZuSettings.Current.difficulty;
            bool watch = mode == "spectate" || mode == "aitest";
            bool third = watch || (FIXED_VIEW.TryGetValue(mode, out var fixedView) ? fixedView == "third" : ZuSettings.Current.view == "third");
            if (q == "quickplay" && MatchSettings.Opp <= 0) MatchSettings.Opp = Career.Ranks.LobbyRating(Career.Ranks.Load().qp.mmr);
            Close();
            MatchSettings.Start(map, watch ? "" : HeroId, mode, skill, third);
        }

        // ================================================================ options, viewer, career
        void Settings(Action back)
        {
            var host = root.parent;
            root.Clear(); U.Show(root, false);
            OptionsView opts = null;
            opts = OptionsView.Open(host, () => { U.Show(root, true); back(); });
            // Options takes Esc and key capture each frame
            host.schedule.Execute(() => opts.Update()).Until(() => opts == null || !IsAttached(opts));
            bool IsAttached(OptionsView o) => host.Q(className: "opts") != null;
        }

        public void Viewer(string id)
        {
            var host = root.parent;
            root.Clear(); U.Show(root, false);
            HeroViewerView.Open(host, id, () => { U.Show(root, true); Title(); });
        }

        void Career_()
        {
            var host = root.parent;
            root.Clear(); U.Show(root, false);
            CareerView.Open(host, () => { U.Show(root, true); Title(); });
        }

        // ================================================================ campaign
        static string cLevel = "c1_shipyard", cHero = "raijin";
        static int Progress() => PlayerPrefs.GetInt("zu-starfall", 0);
        /// <summary>a won level opens the next (TS localStorage zu-starfall)</summary>
        public static void Unlock(int i) { PlayerPrefs.SetInt("zu-starfall", Math.Max(Progress(), i)); PlayerPrefs.Save(); }

        public void Campaign()
        {
            mode = "campaign";
            MenuState.Queue = null;
            int prog = Progress();
            // a squad member follows the host's level
            if (OnlineView.CoopClient && !string.IsNullOrEmpty(OnlineView.CoopLevel)) cLevel = OnlineView.CoopLevel;
            var ids = d.Campaign.levels.Select(j => (string)j["id"]).ToList();
            var levels = CampaignLevel.All(d);
            if (!levels.ContainsKey(cLevel)) cLevel = ids[0];
            var s = Show("campaign-menu");
            Backdrop(s, 30, 10, "#1d1440");
            U.Txt("OPERATION STARFALL\n<size=16><alpha=#B3>Third-person campaign · 5 levels · hunt the Star-Forger's colossi · solo with AI wingmates or online co-op (up to 4)<alpha=#FF></size>", "m-h2", s);
            var lvls = U.Div("lvls", s);
            for (int i = 0; i < ids.Count; i++)
            {
                var l = levels[ids[i]]; int idx = i;
                bool locked = i > prog;
                var lv = U.Btn(null, "lv" + (l.id == cLevel ? " sel" : "") + (locked ? " locked" : ""), () => { cLevel = l.id; OnlineView.CoopSetLevel(cLevel); Campaign(); }, lvls);
                lv.SetEnabled(!locked);
                Wide(U.Pic("map_" + l.id, "lv-img", lv));     // `.lv img { aspect-ratio: 16/9 }`
                var bi = U.Pic("key_" + l.boss, "bimg", lv);
                d.Boss.TryGetValue(l.boss ?? "", out var boss);
                U.Txt($"{i + 1}. {l.name}", "lv-b", lv);
                U.Txt($"Boss: {boss?.name ?? l.boss}{(locked ? " · locked" : "")}", "lv-s", lv);
            }
            var squad = U.Div("squad", s);
            var left = U.Div("sq-hero", squad);
            U.Txt("YOUR HERO", "s-h3 zenith", left);
            var hs = U.Div("heroes", left);
            foreach (var id in d.Campaign.heroes)
            {
                var h = Hero(id); if (h == null) continue;
                HeroCard(hs, h, id == cHero, () => { cHero = id; OnlineView.CoopSetHero(cHero); Campaign(); }, "cpick");
            }
            var coop = U.Div("coop", squad);
            U.Txt("ONLINE CO-OP", "coop-h4", coop);
            OnlineView.CoopPanel(coop, this, cLevel, cHero, Campaign);
            var bar = Bar(s);
            U.Btn("BACK", null, () => { OnlineView.CoopLeave(); Title(); }, bar);
            if (OnlineView.CoopClient) U.Txt("Waiting for the host to launch...", "st", bar);
            else U.Btn(OnlineView.CoopHost ? "LAUNCH SQUAD" : "START SOLO", "primary go", () => { if (OnlineView.CoopHost) OnlineView.CoopStart(); else PlayLevel(cLevel, cHero); }, bar);
        }

        void PlayLevel(string level, string hero)
        {
            MatchSettings.Level = level;
            var ids = d.Campaign.levels.Select(j => (string)j["id"]).ToList();
            CampaignLevel.All(d).TryGetValue(level, out var L);
            var beats = StoryView.Beats(L?.intro);
            if (ids.IndexOf(level) == 0)
            {
                beats.Add(("img/cine_02.webp", "The night the Colossus fell on Neo-Kurogane, nobody had ever seen a machine that large."));
                beats.Add(("img/cine_04.webp", "The Vanguard launched before sunrise."));
            }
            Close();
            StoryView.Play(beats, () => MatchSettings.Start(level, hero, "campaign", 0.7f, true));
        }
    }
}
