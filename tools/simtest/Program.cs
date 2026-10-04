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
            // (before the data loads: the balance lab measures with the user's Yuzu multiplier at a given value)
            {
                int y = Array.IndexOf(args, "--yuzu");
                if (y >= 0 && y + 1 < args.Length) UnityDivergence.YuzuPower = double.Parse(args[y + 1], System.Globalization.CultureInfo.InvariantCulture);
                // (`-zu-balance=0` is read by Balance itself from the process's command line; neither is a command's own argument)
                if (args.Length > 0 && args[0] != "balance") args = args.Where((x, i) => x != "-zu-balance=0" && !(y >= 0 && (i == y || i == y + 1))).ToArray();
            }
            var data = GameData.Load(Path.Combine(root, "Assets/ZU/Resources/ZUData"));
            var cmd = args.Length > 0 ? args[0] : "smoke";
            switch (cmd)
            {
                case "smoke": return Smoke(data);
                case "roundreset": return RoundReset(data);
                case "plays": return PlaysTest(data);
                case "aimatch": return AiMatch(data, args.Length > 1 ? args[1] : null, args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 120);
                case "stadium": return StadiumMatch(data, args.Length > 1 ? args[1] : "hanabi");
                case "campaign": return Campaign(data, args.Length > 1 ? args[1] : null);
                // (the Training Grounds checks assert the kits' own numbers - a 100-damage hit is 100: the balance table off)
                case "training": Balance.Enabled = false; return Training.Run(data);
                case "balance": return BalanceLab.Run(data, root, args);
                default: Console.Error.WriteLine("unknown command " + cmd); return 2;
            }
        }

        /// <summary>the TS tests/unit/sim.test.ts: 10 bots fight on every playable map without breaking</summary>
        /// <summary>the round reset with a summon in the world: Raijin raises the Susanoo, the round ends, everyone goes back to
        /// spawn. The world must keep stepping (0.2.2 threw KeyNotFoundException 'bladeAt' on every step from here on) and the
        /// giant must be gone until its owner casts again.</summary>
        static int RoundReset(GameData data)
        {
            const double DT = 1.0 / 60;
            Rng.Seed(4242);
            int fails = 0;
            void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what); if (!ok) fails++; }
            foreach (var dismissFirst in new[] { true, false })
            foreach (var stadium in new[] { true, false })
            {
                var world = Setup.CreateMatch("hanabi", "aitest", null, 0.8).world;
                for (int i = 0; i < 120; i++) { world.Step(DT); world.events.Clear(); }
                var raijin = world.actors.FirstOrDefault(a => a.def.id == "raijin");
                if (raijin == null) { Console.WriteLine("  FAIL no raijin in the hanabi roster"); return 1; }
                raijin.alive = true; raijin.hp = raijin.def.hp;
                var giant = Susanoo.RaiseSusanoo(world, raijin);
                for (int i = 0; i < 60; i++) { world.Step(DT); world.events.Clear(); }
                if (dismissFirst) Susanoo.DismissSusanoo(world, raijin);
                string err = null;
                try
                {
                    // (the two reset loops of the game: Stadium takes every actor, a control round every non-robot and every summon)
                    foreach (var a in world.actors.ToList()) if (stadium || !a.isRobot || a.IsSummon) { double u = a.ult; world.RoundRespawn(a); a.ult = u; }
                    foreach (var a in world.actors) a.ult = 0;          // (nobody casts again during the check)
                    for (int i = 0; i < 600 && world.winner == null; i++) { world.Step(DT); world.events.Clear(); foreach (var a in world.actors) a.ult = 0; }
                }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                string which = (stadium ? "Stadium, " : "control, ") + (dismissFirst ? "giant already dismissed" : "giant still up");
                Check(err == null, $"the world steps for 10 s after the round reset ({which}){(err != null ? " - " + err : "")}");
                Check(giant != null && !giant.alive, $"the giant is not revived by the round reset ({which})");
                Check(raijin.alive, $"its owner is back at spawn, alive ({which})");
            }
            // the guard: a giant that is alive without its timeline (the 0.2.2 state) is retired, not read
            {
                var world = Setup.CreateMatch("hanabi", "aitest", null, 0.8).world;
                var raijin = world.actors.First(a => a.def.id == "raijin");
                var giant = Susanoo.RaiseSusanoo(world, raijin);
                Susanoo.DismissSusanoo(world, raijin);
                world.Respawn(giant, true);
                string err = null;
                try { for (int i = 0; i < 120; i++) { world.Step(DT); world.events.Clear(); } }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                Check(err == null, "a giant respawned the old way does not stop the world" + (err != null ? " - " + err : ""));
                Check(!giant.alive, "and is retired on the next step");
            }
            Console.WriteLine(fails == 0 ? "roundreset OK" : $"roundreset FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }

        /// <summary>the Play of the Game detector (Sim/Game/Plays.cs): scripted events with a known answer, then a real match</summary>
        static int PlaysTest(GameData data)
        {
            const double DT = 1.0 / 60;
            int fails = 0;
            void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what); if (!ok) fails++; }
            {
                Rng.Seed(7);
                var w = Setup.CreateMatch("hanabi", "aitest", null, 0.8).world;
                var plays = Plays.Attach(w);
                var z = w.actors.Where(a => a.team == "zenith" && !a.isRobot).ToList();
                var u = w.actors.Where(a => a.team == "umbra" && !a.isRobot).ToList();
                Actor A = z[0], B = z[1], C = z[2];
                // (everyone on the ground, a few metres apart: ordinary shots, so only what the script says is scored)
                for (int i = 0; i < w.actors.Count; i++) { var a = w.actors[i]; a.grounded = true; a.flying = false; a.vel = V3.Zero; a.pos = new V3(i * 2, 0, a.team == "zenith" ? 0 : 8); }
                void Hit(Actor src, Actor tgt, string kind = "bullet", bool crit = false) => w.Emit(new DmgEvent { src = src, tgt = tgt, amt = 60, crit = crit, pos = tgt.Center, kind = kind });
                void Down(Actor src, Actor tgt) { Hit(src, tgt); w.Emit(new KillEvent { src = src, tgt = tgt }); }
                // t = 20: B takes one elimination. t = 40..44: A takes three in a row. t = 70: C stops an ultimate.
                w.time = 20; Down(B, u[0]);
                w.time = 40; Down(A, u[0]); w.time = 42; Down(A, u[1]); w.time = 44; Down(A, u[2]);
                w.time = 70; u[3].ults++; w.Emit(new CastEvent { actor = u[3], id = "ult", name = "Test Ultimate" });
                w.time = 71.5; Down(C, u[3]);
                w.time = 90;
                var best = plays.Best();
                Check(best != null && best.actor == A && best.category == Plays.HIGH && best.Kills == 3, $"three eliminations in 4 s are the play (got {best?.actor.def.id} {best?.category} x{best?.Kills} {best?.score:0})");
                Check(best != null && Math.Abs(best.t0 - 40) < 1e-6 && Math.Abs(best.t1 - 44) < 1e-6, "the play runs from its first event to its last");
                Check(best != null && best.summary.StartsWith("3 eliminations"), $"its line reads '{best?.summary}'");
                var pc = plays.Best(a => a == C);
                Check(pc != null && pc.category == Plays.SHUT && pc.score >= 240, $"stopping an ultimate is a SHUTDOWN, over a single elimination (got {pc?.category} {pc?.score:0}: {pc?.summary})");
                var pb = plays.Best(a => a == B);
                Check(pb != null && pb.category == Plays.HIGH && pb.Kills == 1, "a single elimination is still that player's best play");
                var none = plays.Best(a => a == z[3]);
                Check(none == null, "a player who did nothing has no play");
                // far apart, the same three eliminations are no multikill
                w.time = 100; Down(B, u[0]); w.time = 115; Down(B, u[1]); w.time = 130; Down(B, u[2]);
                w.time = 140;
                Check(plays.Best().actor == A, "three eliminations 15 s apart do not beat three in one window");
                // a save: u[1] has a zenith hero nearly dead when it is stopped
                var victim = z[3]; victim.hp = victim.MaxHp * 0.1; victim.lastHitBy = u[1]; victim.lastHitAt = 149.5; u[1].alive = true;
                w.time = 150; Down(C, u[1]);
                var life = plays.Log.Where(e => e.cat == Plays.LIFE && e.actor == C.id).ToList();
                Check(life.Count == 1 && life[0].pts > 200, $"stopping the one killing a teammate at 10 % health is a LIFESAVER ({life.FirstOrDefault()?.text}, {life.FirstOrDefault()?.pts:0})");
            }
            {
                Rng.Seed(12345);
                var match = Setup.CreateMatch("hanabi", "aitest", null, 0.8);
                var w = match.world; var plays = Plays.Attach(w);
                string err = null;
                try { for (int i = 0; i < 150 * 60 && w.winner == null; i++) { w.Step(DT); w.events.Clear(); } }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                Check(err == null, "a real match runs with the detector listening" + (err != null ? " - " + err : ""));
                var best = plays.Best();
                Check(best != null, "a real match has a Play of the Game");
                if (best != null)
                {
                    Console.WriteLine($"    PLAY OF THE GAME: {best.actor.def.name} - {best.Label} - {best.summary} ({best.score:0} pts, t {best.t0:0.0}-{best.t1:0.0})");
                    foreach (var e in best.events.Where(e => e.text != null)) Console.WriteLine($"      {e.t,6:0.0}  {e.text}  +{e.pts:0}");
                    Check(!best.actor.isRobot && !best.actor.IsSummon, "it belongs to a hero");
                    Check(best.t1 - best.t0 <= Plays.WINDOW + 1e-6, "it fits the window");
                    int withPlay = w.actors.Count(a => !a.isRobot && plays.Best(x => x == a) != null);
                    Console.WriteLine($"    {withPlay} of {w.actors.Count(a => !a.isRobot)} heroes have a best play; {plays.Log.Count} scored events");
                    Check(withPlay >= 6, "most heroes have a best play of their own");
                }
            }
            Console.WriteLine(fails == 0 ? "plays OK" : $"plays FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }

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
