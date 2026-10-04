// Match construction shared by the game, the AI test lab and headless tests. Port of zenith-umbra src/game/setup.ts
// (the campaign's createCampaign is in Campaign/Director.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public class Match
    {
        public World world; public INav nav; public Actor player; public List<Bot> bots = new List<Bot>();
        /// <summary>Training Grounds (desktop edition): the Hero Range - any hero as a target, attack or defense mode</summary>
        public HeroRange range;
        /// <summary>Training Grounds (desktop edition): the Spar Arena - one-on-one against any hero in a sealed box</summary>
        public Spar spar;
    }

    public static class Setup
    {
        static readonly HashSet<string> PLAYER_MODES = new HashSet<string> { "skirmish", "stadium", "quickplay", "competitive", "practice" };

        /// <param name="level">collision for the map (default: the classic box level); <paramref name="nav"/> likewise</param>
        public static Match CreateMatch(string mapId, string mode, string playerHero, double skill = 0.7, ILevel level = null, INav nav = null)
        {
            var map = GameData.Current.Map[mapId];
            var world = new World(map, mode, true, null, level);
            nav ??= new BoxNav((BoxLevel)world.level);
            world.nav = nav;
            var m = new Match { world = world, nav = nav };
            if (mode == "gallery")
            {
                // animation test bench: one hero runs a scripted routine through every movement / combat state
                var a = world.AddHero(playerHero ?? "raijin", "zenith");
                a.pos = new V3(-10, 0, 0);
                a.controller = new DemoRoutine(world, a);
                world.point.unlockAt = 1e9;
                return m;
            }
            if (mode == "replay")
            {
                // a saved play watched from the menu (Game/AbilityFx/KillCam.Watch): the map with nobody on it and nothing to
                // win - the views draw the clip's heroes
                world.point.unlockAt = 1e9;
                return m;
            }
            if (mode == "training")
            {
                var p = world.AddHero(playerHero ?? "raijin", "zenith");
                p.isPlayer = true; m.player = p;
                var robots = new (string id, double x, double z)[]
                {
                    ("bot_dummy", 8, -4), ("bot_dummy", 8, 4), ("bot_dummy", 14, 0),
                    ("bot_sentry", 30, -10), ("bot_sentry", 30, 10),
                    ("bot_drone", 20, -16), ("bot_drone", 20, 16),
                };
                foreach (var (id, x, z) in robots)
                {
                    var r = world.AddHero(id, "umbra");
                    r.spawn = new[] { x, z }; world.Respawn(r, true);
                    var b = new Bot(world, r, nav, skill); r.controller = b; m.bots.Add(b);
                }
                m.range = world.full ? new HeroRange(world, nav) : null;
                m.spar = world.full ? new Spar(world, nav, m.range) : null;
                return m;
            }
            // the AI lab / headless sims alternate the two-tank team's pick map by map (deterministic, both get exercised)
            int seed = mapId.Sum(c => (int)c) % 2;
            Func<double> rnd = mode == "aitest" ? (Func<double>)(() => seed * 0.99) : Rng.Random;
            foreach (var h in Lineup(playerHero, rnd))
            {
                var a = world.AddHero(h.id);
                if (h.id == playerHero && PLAYER_MODES.Contains(mode)) { a.isPlayer = true; m.player = a; continue; }
                var b = new Bot(world, a, nav, skill);
                a.controller = b; m.bots.Add(b);
            }
            if (mode == "stadium") world.stadium = new Stadium(world);
            return m;
        }

        /// <summary>a seat in an online match: a hero, a side, and who plays it ("local" = this machine, a peer id, "" = AI)</summary>
        public class OnlineSlot { public string hero, team, netId; }

        /// <summary>
        /// Online match (the host's world): the humans' heroes, and AI for every seat still empty - each side is filled to
        /// five (one tank, two damage, two support; roles the humans already cover are skipped, a side never fields the same
        /// hero twice). Few people online = a mostly-AI match; every extra player replaces a bot.
        /// </summary>
        public static Match CreateOnlineMatch(string mapId, string mode, List<OnlineSlot> slots, double skill = 0.7, Func<double> rnd = null, ILevel level = null, INav nav = null)
        {
            rnd ??= Rng.Random;
            var D = GameData.Current;
            var world = new World(D.Map[mapId], mode, true, null, level);
            nav ??= new BoxNav((BoxLevel)world.level);
            world.nav = nav;
            var m = new Match { world = world, nav = nav };
            var HEROES = RosterFor(true);
            var seats = new List<(HeroDef def, OnlineSlot slot)>();
            foreach (var team in new[] { "zenith", "umbra" })
            {
                var humans = slots.Where(s => s.team == team && D.Hero.TryGetValue(s.hero, out var h) && h.team == team).Take(5).ToList();
                var used = new HashSet<string>(humans.Select(s => s.hero));
                foreach (var s in humans) seats.Add((D.Hero[s.hero], s));
                var need = new Dictionary<string, int> { ["tank"] = 1, ["dps"] = 2, ["support"] = 2 };
                foreach (var s in humans) need[D.Hero[s.hero].role]--;
                int open = 5 - humans.Count;
                foreach (var role in new[] { "tank", "support", "dps" })
                {
                    var pool = HEROES.Where(h => h.team == team && h.role == role && !used.Contains(h.id)).ToList();
                    for (int i = pool.Count - 1; i > 0; i--) { int j = (int)Math.Floor(rnd() * (i + 1)); (pool[i], pool[j]) = (pool[j], pool[i]); }
                    foreach (var h in pool.Take(Math.Max(0, Math.Min(open, need[role])))) { seats.Add((h, null)); used.Add(h.id); open--; }
                }
                // a side whose humans doubled up on a role still gets five: any hero left
                foreach (var h in HEROES.Where(h => h.team == team && !used.Contains(h.id)).Take(open).ToList()) { seats.Add((h, null)); used.Add(h.id); }
            }
            // the roster order keeps spawn slots and the scoreboard stable
            seats = seats.OrderBy(s => HEROES.IndexOf(s.def)).ToList();
            foreach (var (def, slot) in seats)
            {
                var a = world.AddHero(def.id);
                if (slot?.netId == "local") { a.isPlayer = true; m.player = a; continue; }
                if (!string.IsNullOrEmpty(slot?.netId)) { a.netId = slot.netId; continue; }
                var b = new Bot(world, a, nav, skill); a.controller = b; m.bots.Add(b);
            }
            return m;
        }

        public static List<HeroDef> RosterFor(bool full) => GameData.Current.Heroes.Where(h => full || !h.full).ToList();

        /// <summary>Role queue, the 5v5 way: one tank, two supports, two damage per side (a random pick where a team has more).</summary>
        public static List<HeroDef> Lineup(string playerHero, Func<double> rnd = null, bool full = true)
        {
            rnd ??= Rng.Random;
            var SLOTS = new (string role, int n)[] { ("tank", 1), ("support", 2), ("dps", 2) };
            var output = new List<HeroDef>();
            var HEROES = RosterFor(full);
            foreach (var team in new[] { "zenith", "umbra" })
                foreach (var (role, n) in SLOTS)
                {
                    var pool = HEROES.Where(h => h.team == team && h.role == role).ToList();
                    var mine = pool.Where(h => h.id == playerHero).ToList(); var rest = pool.Where(h => h.id != playerHero).ToList();
                    for (int i = rest.Count - 1; i > 0; i--) { int j = (int)Math.Floor(rnd() * (i + 1)); (rest[i], rest[j]) = (rest[j], rest[i]); }
                    output.AddRange(mine.Concat(rest).Take(n));
                }
            // keep the roster order (spawn slots, scoreboard) stable
            return HEROES.Where(h => output.Contains(h)).ToList();
        }
    }

    /// <summary>Scripted routine for the gallery / animation test: idle, walk, run, strafe, backpedal, jump, fly, attack, cast, hit.</summary>
    public class DemoRoutine : IController
    {
        readonly World w; readonly Actor a; readonly double t0;
        public string step = "";
        public DemoRoutine(World w, Actor a) { this.w = w; this.a = a; t0 = w.time; }
        public static readonly (string name, double secs)[] STEPS = { ("idle", 2), ("run", 2.5), ("strafe", 2), ("back", 1.5), ("jump", 1.4), ("attack", 1.6), ("alt", 1.2), ("cast", 1.4), ("fly", 3), ("hit", 1), ("idle2", 1) };

        public void Think(double dt)
        {
            var i = a.input;
            double total = STEPS.Sum(x => x.secs);
            double k = (w.time - t0) % total; string s = "idle";
            foreach (var (n, d) in STEPS) { if (k < d) { s = n; break; } k -= d; }
            step = s;
            i.mx = i.mz = 0; i.fire = i.alt = i.jump = i.jumpHeld = false; i.a1 = i.a2 = i.ult = false;
            // walk a circle around the origin so the camera always has room
            double ang = Math.Atan2(a.pos.z, a.pos.x), tangent = ang + Math.PI / 2;
            double fx = Math.Cos(tangent), fz = Math.Sin(tangent);
            i.yaw = s == "strafe" ? Math.Atan2(-a.pos.x, -a.pos.z) : s == "back" ? Math.Atan2(-fx, -fz) : Math.Atan2(fx, fz);
            i.pitch = 0;
            if (s == "run" || s == "jump" || s == "fly") i.mz = 1;
            if (s == "strafe") i.mx = 1;
            if (s == "back") i.mz = -1;
            if (s == "jump") i.jump = Math.Floor((w.time - t0) * 2) % 3 == 0;
            if (s == "fly") { i.jump = true; i.jumpHeld = a.def.frame == "flyer"; }
            if (s == "attack") i.fire = true;
            if (s == "alt") i.alt = true;
            if (s == "cast") a.anim.castAt = w.time - (w.time % 0.7);
            if (s == "hit" && Math.Floor(w.time * 3) != Math.Floor((w.time - 1.0 / 60) * 3)) a.anim.hitAt = w.time;
            // keep them on the circle (radius ~10)
            double r = M.Hypot(a.pos.x, a.pos.z);
            if (r > 12 || r < 8) { double c = (10 - r) / 10; a.vel.x += a.pos.x / (r == 0 ? 1 : r) * c * 2; a.vel.z += a.pos.z / (r == 0 ? 1 : r) * c * 2; }
            a.hp = a.def.hp; a.ammo = 99;
        }
    }
}
