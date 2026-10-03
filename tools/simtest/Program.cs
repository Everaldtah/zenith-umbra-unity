// Headless entry point for the simulation: smoke tests now, parity scenarios as the port lands.
using System;
using System.IO;
using System.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.SimTest
{
    static class Program
    {
        static int Main(string[] args)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            var data = GameData.Load(Path.Combine(root, "Assets/ZU/Resources/ZUData"));
            var cmd = args.Length > 0 ? args[0] : "smoke";
            switch (cmd)
            {
                case "smoke": return Smoke(data);
                case "aimatch": return AiMatch(data, args.Length > 1 ? args[1] : null, args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 120);
                case "stadium": return StadiumMatch(data, args.Length > 1 ? args[1] : "hanabi");
                case "campaign": return Campaign(data, args.Length > 1 ? args[1] : null);
                case "training": return Training.Run(data);
                default: Console.Error.WriteLine("unknown command " + cmd); return 2;
            }
        }

        /// <summary>the TS tests/unit/sim.test.ts: 10 bots fight on every playable map without breaking</summary>
        static int AiMatch(GameData data, string only, double secs)
        {
            const double DT = 1.0 / 60;
            int fails = 0;
            Rng.Seed(12345);
            foreach (var m in data.Maps.Where(x => x.id != "training" && !x.retired && (only == null || x.id == only)))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var match = Setup.CreateMatch(m.id, "aitest", null, 0.8);
                var world = match.world;
                string err = null;
                try
                {
                    for (int i = 0; i < secs * 60 && world.winner == null; i++)
                    {
                        world.Step(DT);
                        world.events.Clear();
                        foreach (var a in world.actors)
                        {
                            if (double.IsNaN(a.pos.x + a.pos.y + a.pos.z) || double.IsInfinity(a.pos.x + a.pos.y + a.pos.z)) { err = $"{a.def.id} pos not finite"; break; }
                            if (double.IsNaN(a.hp)) { err = $"{a.def.id} hp NaN"; break; }
                        }
                        if (err != null) break;
                    }
                }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace; }
                int kills = world.actors.Sum(a => a.kills), deaths = world.actors.Sum(a => a.deaths), stuck = match.bots.Sum(b => b.stuckCount);
                int casts = world.stats.casts.Values.Sum();
                bool ok = err == null && kills > 3 && casts > 20;
                if (!ok) fails++;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {m.id,-10} {world.time,6:0}s kills {kills,3} env {deaths - kills,2} stuck {stuck,3} casts {casts,4} counters {world.stats.counters,3} winner {world.winner ?? "-",-6} ({sw.ElapsedMilliseconds} ms)");
                if (err != null) Console.WriteLine("    " + err);
                else Console.WriteLine("    " + string.Join(" ", world.actors.Where(a => !a.IsSummon).Select(a => $"{a.def.id}:{a.kills}/{a.deaths} d{Math.Round(a.dmgDone)} h{Math.Round(a.healDone)}")));
            }
            return fails == 0 ? 0 : 1;
        }

        /// <summary>a whole Stadium match between bots: rounds, the Armory between them (cash, items, powers), a winner</summary>
        static int StadiumMatch(GameData data, string map)
        {
            const double DT = 1.0 / 60;
            Rng.Seed(777);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var match = Setup.CreateMatch(map, "stadium", null, 0.8);
            var w = match.world; var s = w.stadium;
            if (s == null) { Console.WriteLine("FAIL no stadium"); return 1; }
            int lastRound = 0; string err = null;
            try
            {
                for (int i = 0; i < 3600 * 60 && w.winner == null; i++)
                {
                    w.Step(DT);
                    foreach (var e in w.events) if (e is MsgEvent m && m.text.StartsWith("ROUND")) Console.WriteLine($"  {w.time,7:0.0}s  {m.text}");
                    w.events.Clear();
                    if (s.round != lastRound && s.phase == "armory") lastRound = s.round;
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace; }
            bool ok = err == null && w.winner != null && s.wins[w.winner] == Stadium.ROUNDS_TO_WIN;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} stadium {map}: winner {w.winner ?? "-"} {s.wins["zenith"]}-{s.wins["umbra"]} after {s.round} rounds, {w.time:0}s sim, {sw.ElapsedMilliseconds} ms");
            foreach (var a in w.actors.Where(x => !x.IsSummon))
                Console.WriteLine($"    {a.team,-6} {a.def.id,-9} cash {a.cash,6:0}  items [{string.Join(",", a.items)}]  powers [{string.Join(",", a.powers.Select(p => p.Substring(p.IndexOf('_') + 1)))}]  {a.kills}/{a.deaths}");
            if (err != null) Console.WriteLine("    " + err);
            return ok ? 0 : 1;
        }

        /// <summary>each Starfall level played by the AI squad: encounters, waves, the boss (and Qel'Varis after Omega Genesis)</summary>
        static int Campaign(GameData data, string only)
        {
            const double DT = 1.0 / 60;
            int fails = 0;
            Rng.Seed(4242);
            foreach (var lvl in CampaignLevel.All(data).Values.Where(l => only == null || l.id == only))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var m = Director.CreateCampaign(lvl.id, new System.Collections.Generic.List<(string, string)>(), 0.8);
                var w = m.world; var d = (Director)w.director;
                string err = null, lastState = null;
                try
                {
                    for (int i = 0; i < 900 * 60 && w.winner == null; i++)
                    {
                        w.Step(DT); w.events.Clear();
                        if (d.state != lastState) { Console.WriteLine($"  {w.time,6:0.0}s  {d.state,-8} enc {d.enc} wave {d.wave}  {d.objective}"); lastState = d.state; }
                    }
                }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace; }
                bool ok = err == null && w.winner == "zenith";
                if (!ok) fails++;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {lvl.id,-12} {lvl.name}: {d.state}, cleared {d.cleared.Count}/{lvl.encounters.Count}, boss {(d.boss != null ? d.boss.def.id + (d.boss.alive ? " alive " + Math.Round(d.boss.hp) : " down") : "-")}, {w.time:0}s sim, {sw.ElapsedMilliseconds} ms");
                if (err != null) Console.WriteLine("    " + err);
            }
            return fails == 0 ? 0 : 1;
        }

        static int Smoke(GameData data)
        {
            Console.WriteLine($"heroes {data.Heroes.Count}, maps {data.Maps.Count}");
            int fails = 0;
            foreach (var m in data.Maps)
            {
                var lvl = new BoxLevel(m);
                var sp = m.spawns["zenith"];
                var g = lvl.GroundAt(sp[0], sp[1], 1);
                var p = new V3(sp[0], g, sp[1]);
                lvl.Collide(ref p, 0.45, 1.8);
                var los = lvl.LineOfSight(new V3(sp[0], g + 1.6, sp[1]), new V3(m.point[0], m.point[1] + 1, m.point[2]));
                Console.WriteLine($"  {m.id,-10} boxes {m.boxes.Count,4}  spawn ground {g,6:0.00}  spawn->point LOS {los}");
                if (double.IsNegativeInfinity(g)) { fails++; Console.WriteLine("    FAIL: zenith spawn has no ground"); }
            }
            Console.WriteLine(fails == 0 ? "smoke OK" : $"smoke FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }
    }
}
