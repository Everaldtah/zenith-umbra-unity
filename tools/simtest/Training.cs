// The Training Grounds tests: the TS tests/unit/herorange.test.ts (the Hero Range, the ultimate charge packs) and
// tests/unit/spar.test.ts (the Spar Arena), check for check. run.ps1 training
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.SimTest
{
    static class Training
    {
        const double DT = 1.0 / 60;
        static int fails, checks;
        static string test;

        static void Expect(bool ok, string what)
        {
            checks++;
            if (ok) return;
            fails++;
            Console.WriteLine($"    FAIL [{test}] {what}");
        }

        static void Run(World w, double secs, Action each = null)
        {
            for (int i = 0; i < JsMath.Round(secs / DT); i++) { each?.Invoke(); w.Step(DT); w.events.Clear(); }
        }

        static (Match m, World w, Actor me, HeroRange r) Grounds(string hero = "raijin")
        {
            var m = Setup.CreateMatch("training", "training", hero);
            var me = m.player; var r = m.range;
            // stand on the firing line, facing down the lane
            me.pos = new V3(HeroRange.LANE.x0, 0, HeroRange.LANE.z); me.yaw = me.input.yaw = Math.PI / 2; me.Clear("spawnprot");
            return (m, m.world, me, r);
        }

        static Actor Deploy(HeroRange r, string hero = null, string mode = null, bool? abilities = null, string move = null, double? dist = null, double? skill = null)
        {
            var a = r.Deploy(hero, mode, abilities, move, dist, skill); a.Clear("spawnprot"); return a;
        }
        static double Off(HeroRange r, Actor a) => M.Hypot(a.pos.x - r.Post.x, a.pos.z - r.Post.z);
        static DmgOpts Hit(bool crit = false) => new DmgOpts { kind = "hitscan", crit = crit };

        static void It(string name, Action body)
        {
            test = name;
            int before = fails;
            try { body(); }
            catch (Exception e) { fails++; Console.WriteLine($"    FAIL [{name}] threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
            Console.WriteLine($"  {(fails == before ? "PASS" : "FAIL")} {name}");
        }

        public static int Run(GameData data)
        {
            Rng.Seed(31337);
            Console.WriteLine("Hero Range");
            HeroRangeTests();
            Console.WriteLine("Ultimate charge packs");
            UltPackTests();
            Console.WriteLine("Spar Arena");
            SparTests();
            Console.WriteLine(fails == 0 ? $"training OK ({checks} checks)" : $"training FAILED ({fails} of {checks} checks)");
            return fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ herorange.test.ts
        static void HeroRangeTests()
        {
            It("every guard ability is the hero's own (slot and id match the kit)", () =>
            {
                foreach (var h in Setup.RosterFor(true))
                {
                    Expect(HeroRange.GUARD.ContainsKey(h.id), h.id + " has a guard entry");
                    if (!HeroRange.GUARD.ContainsKey(h.id)) continue;
                    foreach (var g in HeroRange.GUARD[h.id])
                    {
                        var def = g.slot == "a1" ? h.ability1 : g.slot == "a2" ? h.ability2 : h.secondary;
                        Expect((def != null && def.IsAbility ? def.id : "") == g.id, $"{h.id} {g.slot} is {g.id}");
                    }
                }
            });

            It("deploys any hero, on the enemy side, at its post down the lane", () =>
            {
                var (_, w, _, r) = Grounds();
                foreach (var id in new[] { "gorgoth", "mirei", "raijin", "tenkai" })
                {
                    var a = Deploy(r, hero: id, dist: 15);
                    Expect(a.baseDef.id == id, id + " deployed");
                    Expect(a.team == "umbra", id + " umbra");
                    Expect(Off(r, a) < 0.01, id + " at its post");
                    Expect(r.Post.x - HeroRange.LANE.x0 == 15, id + " post 15 m");
                    Expect(w.actors.Count(x => x.sv != null && x == r.bot) == 1, id + " in the world once");
                }
                // only one range hero at a time
                Expect(w.actors.Count(x => !x.isRobot && !x.isPlayer) == 1, "one range hero");
            });

            It("defense: holds its post facing you and never fires", () =>
            {
                var (_, w, me, r) = Grounds();
                var a = Deploy(r, hero: "kagemaru", mode: "defense", abilities: false, move: "hold", dist: 10);
                double hp = me.Health;
                int fired = 0;
                Run(w, 6, () => { if (a.input.fire || a.input.alt || a.input.a1 || a.input.a2 || a.input.ult) fired++; });
                Expect(fired == 0, $"never fired ({fired})");
                Expect(me.Health == hp, "you took nothing");
                Expect(Off(r, a) < 0.8, $"at its post ({Off(r, a):0.00})");
                // facing the firing line (you)
                Expect(Math.Abs(Math.Atan2(Math.Sin(a.yaw + Math.PI / 2), Math.Cos(a.yaw + Math.PI / 2))) < 0.2, "faces you");
            });

            It("defense strafe: moves side to side but stays in its lane", () =>
            {
                var (_, w, _, r) = Grounds();
                var a = Deploy(r, hero: "hayate", mode: "defense", move: "strafe", dist: 10);
                double minZ = 99, maxZ = -99;
                Run(w, 8, () => { minZ = Math.Min(minZ, a.pos.z); maxZ = Math.Max(maxZ, a.pos.z); });
                Expect(maxZ - minZ > 1.5, $"strafes ({maxZ - minZ:0.00} m)");
                Expect(Math.Max(Math.Abs(maxZ - HeroRange.LANE.z), Math.Abs(minZ - HeroRange.LANE.z)) < HeroRange.LANE.strafe + 1.2, "stays in its lane");
            });

            It("meters your damage: hits, crits, last hit, DPS; a kill logs the time-to-kill and it is back at its post", () =>
            {
                var (_, w, me, r) = Grounds();
                var a = Deploy(r, hero: "kagemaru", mode: "defense");
                // a hit every 0.25 s: 50 + a 100 crit, then 50s until it drops (225 hp)
                int n = 0;
                Run(w, 1.2, () => { if (a.alive && w.time % 0.25 < DT) { n++; w.Damage(me, a, n == 2 ? 100 : 50, Hit(n == 2)); } });
                var s = r.stats;
                Expect(!a.alive, "dropped");
                Expect(s.kills == 1, "1 kill");
                Expect(s.dealt.hits == 4, $"4 hits ({s.dealt.hits})");                    // 50 + 100 + 50 + 25 (the last only what it had left)
                Expect(s.dealt.crits == 1, "1 crit");
                Expect(Math.Abs(s.dealt.total - 225) < 0.0005, $"225 total ({s.dealt.total})");
                Expect(Math.Abs(s.dealt.max - 100) < 0.0005, "biggest hit 100");
                Expect(Math.Abs(s.lastTtk.Value - 0.75) < 0.05, $"ttk 0.75 ({s.lastTtk})");         // first hit to the kill
                var l = s.log[0];
                Expect(l.target == "Kagemaru" && l.mode == "defense" && l.hits == 4 && l.crits == 1, "logged");
                Expect(s.dealt.Dps().Value > 200 && s.dealt.Dps().Value < 260, $"dps ~225 ({s.dealt.Dps()})");     // ~225 over 4 ticks spaced 0.25 s = 225 / 1.0 s
                Run(w, HeroRange.RESPAWN_SECS + 0.2);
                Expect(a.alive, "back up");
                Expect(Off(r, a) < 0.5, "at its post");
                Expect(a.Health == a.MaxHp, "full health");
            });

            It("a shotgun blast counts as one hit; a beam or a bleed adds damage but no hits", () =>
            {
                var t = new Tally();
                for (int i = 0; i < 8; i++) t.Add(1, 6, i == 0, "hitscan");
                Expect(t.hits == 1 && t.last == 48 && t.crits == 1, "8 pellets = 1 hit of 48, 1 crit");
                t.Add(1.5, 3, false, "beam"); t.Add(1.5 + DT, 3, false, "dot");
                Expect(t.hits == 1 && t.total == 54 && t.by["dot"] == 3 && t.by["weapon"] == 51, "beam / dot: no hits");
            });

            It("defense: back to full health a few seconds after the last hit (a clean burst every time)", () =>
            {
                var (_, w, me, r) = Grounds();
                var a = Deploy(r, hero: "gorgoth", mode: "defense");
                w.Damage(me, a, 300, Hit());
                Expect(a.Health < a.MaxHp, "hurt");
                Run(w, HeroRange.RESET_SECS + 0.3);
                Expect(a.Health == a.MaxHp, "back to full");
            });

            It("defense with abilities: guards itself under fire (Tenkai-Oh's sun-shield, Gorgoth's plating)", () =>
            {
                {
                    var (_, w, me, r) = Grounds();
                    var a = Deploy(r, hero: "tenkai", mode: "defense", abilities: true);
                    int up = 0;
                    Run(w, 1.5, () => { if (w.time % 0.3 < DT) w.Damage(me, a, 20, Hit()); if (a.barrier.up) up++; });
                    Expect(up > 30, $"sun-shield up ({up} ticks)");
                }
                {
                    var (_, w, me, r) = Grounds();
                    var a = Deploy(r, hero: "gorgoth", mode: "defense", abilities: true);
                    Run(w, 0.2, () => w.Damage(me, a, 5, Hit()));
                    Expect(a.ShieldAmt > 0, "plating");
                }
                {   // off: nothing
                    var (_, w, me, r) = Grounds();
                    var a = Deploy(r, hero: "gorgoth", mode: "defense", abilities: false);
                    Run(w, 0.5, () => w.Damage(me, a, 5, Hit()));
                    Expect(a.ShieldAmt == 0, "abilities off: no plating");
                }
            });

            It("attack: the hero fights back (and stays leashed to its lane); the meter counts what it deals you", () =>
            {
                foreach (var id in new[] { "kagemaru", "gantetsu", "nocturne" })
                {
                    var (_, w, me, r) = Grounds();
                    me.hp = me.def.hp * 50;            // (so the player outlives the test)
                    var a = Deploy(r, hero: id, mode: "attack", abilities: true, dist: 15, skill: 0.95);
                    double far = 0;
                    Run(w, 10, () => { far = Math.Max(far, Off(r, a)); });
                    Expect(r.stats.taken.total > 20, $"{id} dealt you {r.stats.taken.total:0}");
                    Expect(far < HeroRange.LEASH + 3, $"{id} leashed ({far:0.0} m)");
                }
            });

            It("attack without abilities: weapon only", () =>
            {
                var (_, w, me, r) = Grounds();
                me.hp = me.def.hp * 50;
                var a = Deploy(r, hero: "gorgoth", mode: "attack", abilities: false, dist: 10, skill: 0.95);
                var casts = new HashSet<string>();
                w.taps.Add(e => { if (e is CastEvent c && c.actor == a) casts.Add(c.id); });
                a.ult = a.def.ult.charge;
                Run(w, 10);
                Expect(casts.Count == 0, "no casts: " + string.Join(",", casts));
                Expect(r.stats.taken.total > 20, $"fired its weapon ({r.stats.taken.total:0})");
            });

            It("clear removes the hero and everything it summoned", () =>
            {
                var (_, w, me, r) = Grounds();
                me.hp = me.def.hp * 50;
                var a = Deploy(r, hero: "hex", mode: "attack", abilities: true, dist: 10);
                a.ult = a.def.ult.charge; a.input.ult = true;
                w.Step(DT); a.input.ult = false;
                Run(w, 0.5);
                r.Clear();
                Expect(!w.actors.Contains(a), "gone");
                Expect(!w.actors.Any(x => x.owner == a), "its summons gone");
                Expect(r.bot == null, "no range hero");
            });
        }

        // ------------------------------------------------------------------ herorange.test.ts: ultimate charge packs
        static void UltPackTests()
        {
            It("the Training Grounds has them; touching one makes your ultimate ready, then it is gone for a while", () =>
            {
                var (_, w, me, _) = Grounds();
                Expect(w.ultPacks.Count >= 2, "packs on the map");
                var p = w.ultPacks[0];
                me.ult = 0;
                me.pos = new V3(p.x, p.y, p.z);
                Run(w, 0.1);
                Expect(me.ult == me.def.ult.charge, "ultimate ready");
                Expect(p.readyAt > w.time, "pack gone");
                // gone: spend the ult charge and stand on it again - nothing until it is back
                me.ult = 0; Run(w, 1);
                Expect(me.ult < me.def.ult.charge, "nothing while it is gone");
                Run(w, World.ULT_PACK.respawn);
                Expect(me.ult == me.def.ult.charge, "back after the respawn");
            });

            It("a bot never takes one, and a ready ultimate leaves it there", () =>
            {
                var (_, w, me, r) = Grounds();
                var p = w.ultPacks[0];
                var a = Deploy(r, hero: "kagemaru", mode: "defense");
                a.ult = 0; a.pos = new V3(p.x, p.y, p.z); r.opts.move = "hold";
                w.Step(DT);
                Expect(a.ult < a.def.ult.charge, "the bot took nothing");
                me.ult = me.def.ult.charge; me.pos = new V3(p.x, p.y, p.z);
                Run(w, 0.1);
                Expect(p.readyAt == 0, "left there");
            });

            It("only the desktop edition has them (the web demo stays as it was)", () =>
            {
                Expect(new World("training", "training", false).ultPacks.Count == 0, "none in the lite world");
            });
        }

        // ------------------------------------------------------------------ spar.test.ts
        /// <summary>walk in: stand just inside the box's south wall</summary>
        static void Enter(Actor me) => me.pos = new V3(Spar.ARENA.x, 0, Spar.ARENA.z - Spar.ARENA.hz + 1.5);
        static void Kill(World w, Actor by, Actor a) => w.Damage(by, a, 5000, Hit());
        static void Fight(World w, Spar s) { Run(w, Spar.COUNTDOWN + 0.1); Expect(s.phase == "fight", "FIGHT"); }
        static (Match m, World w, Actor me, Spar s) Arena(string hero = "raijin")
        {
            var m = Setup.CreateMatch("training", "training", hero);
            var me = m.player;
            me.Clear("spawnprot");
            return (m, m.world, me, m.spar);
        }
        static double From(Actor a, (double x, double z, double yaw) at) => M.Hypot(a.pos.x - at.x, a.pos.z - at.z);

        static void SparTests()
        {
            It("the opponent waits in the arena; walking in seals the box and starts round 1 with a countdown", () =>
            {
                var (_, w, me, s) = Arena();
                var f = s.Arm("kagemaru", "hard", 2);
                Expect(s.phase == "waiting", "waiting");
                Expect(s.Inside(f.pos), "in the arena");
                Expect(s.brain.bot.skill == Spar.SPAR_SKILL["hard"], "hard AI");
                Run(w, 1);
                Expect(s.phase == "waiting", "still waiting");
                Enter(me); Run(w, DT);
                Expect(s.phase == "countdown", "countdown");
                Expect(s.Sealed, "sealed");
                // both pinned at their ends through the countdown, and nobody can hurt anyone yet
                me.input.mz = 1;
                Run(w, Spar.COUNTDOWN - 0.3);
                Expect(From(me, Spar.SPAR_START.you) < 0.2, "you pinned");
                Expect(From(f, Spar.SPAR_START.them) < 0.2, "they pinned");
                Expect(w.Damage(me, f, 50, Hit()) == 0, "no damage in the countdown");
                Run(w, 0.4);
                Expect(s.phase == "fight", "FIGHT");
                Expect(w.Damage(me, f, 50, Hit()) == 50, "damage in the fight");
            });

            It("a kill wins the round; the next one starts from full health at the ends; first to N takes the spar and the box opens", () =>
            {
                var (_, w, me, s) = Arena();
                var f = s.Arm("gorgoth", "easy", 2);
                Enter(me); Run(w, DT); Fight(w, s);
                Kill(w, me, f); Kill(w, me, f);           // (a mech: the frame, then the pilot)
                Expect(s.phase == "roundover", "round over");
                Expect(s.wins["you"] == 1 && s.wins["them"] == 0, "1 - 0");
                me.hp = 10;
                Run(w, Spar.ROUND_END + 0.1);
                Expect(s.phase == "countdown", "next countdown");
                Expect(s.round == 2, "round 2");
                Expect(f.alive && f.def.id == "gorgoth" && f.Health == f.MaxHp, "they are back, full");
                Expect(me.Health == me.MaxHp, "you are full");
                Expect(From(me, Spar.SPAR_START.you) < 0.2, "you at your end");
                Run(w, Spar.COUNTDOWN + 0.1);
                Kill(w, me, f); Kill(w, me, f);
                Run(w, Spar.ROUND_END + 0.1);
                Expect(s.phase == "done", "done");
                Expect(s.Sealed, "still sealed");
                var h = s.history[0];
                Expect(h.foe == "Gorgoth" && h.diff == "easy" && h.score[0] == 2 && h.score[1] == 0 && h.won, "won 2 - 0");
                Run(w, Spar.DONE_SECS + 0.1);
                Expect(s.phase == "waiting", "waiting again");
                Expect(!s.Sealed, "open");
                // still standing in the arena: no rematch until you step out and back in
                Run(w, 0.5); Expect(s.phase == "waiting", "no rematch while inside");
                me.pos = new V3(Spar.ARENA.x, 0, Spar.ARENA.z - Spar.ARENA.hz - 3); Run(w, DT);
                Enter(me); Run(w, DT);
                Expect(s.phase == "countdown", "rematch");
            });

            It("losing a round: you get back up at your end for the next round (not at the team spawn)", () =>
            {
                var (_, w, me, s) = Arena();
                var f = s.Arm("kagemaru", "medium", 3);
                Enter(me); Run(w, DT); Fight(w, s);
                Kill(w, f, me);
                Expect(s.wins["you"] == 0 && s.wins["them"] == 1, "0 - 1");
                Expect(!me.alive, "you fell");
                Run(w, Spar.ROUND_END + 0.1);
                Expect(me.alive, "back up");
                Expect(s.Inside(me.pos), "in the arena");
                Run(w, 6);                                   // the world's own respawn never moves you out
                Expect(s.Inside(me.pos), "still in the arena");
            });

            It("the walls: neither of you can leave, nobody else gets in, no shot crosses them", () =>
            {
                var (m, w, me, s) = Arena();
                var f = s.Arm("kagemaru", "easy", 3);
                var r = m.range; var outside = r.Deploy(hero: "raijin", mode: "defense");
                outside.Clear("spawnprot");
                Enter(me); Run(w, DT); Fight(w, s);
                // a dash / teleport out of the box ends at the wall
                me.pos = new V3(Spar.ARENA.x + Spar.ARENA.hx + 5, 0, Spar.ARENA.z); Run(w, DT);
                Expect(s.Inside(me.pos), "you stay in");
                me.pos = new V3(Spar.ARENA.x, Spar.ARENA.h + 4, Spar.ARENA.z); Run(w, DT);
                Expect(me.pos.y + me.Height <= Spar.ARENA.h + 0.01, "under the roof");
                f.pos = new V3(Spar.ARENA.x, 0, Spar.ARENA.z - Spar.ARENA.hz - 6); Run(w, DT);
                Expect(s.Inside(f.pos), "they stay in");
                // an outsider walking in is put back out
                outside.pos = new V3(Spar.ARENA.x + 2, 0, Spar.ARENA.z); Run(w, DT);
                Expect(!s.Inside(outside.pos), "outsider kept out");
                // shots across the wall do nothing, either way
                Expect(w.Damage(me, outside, 50, Hit()) == 0, "no shot out");
                Expect(w.Damage(outside, me, 50, Hit()) == 0, "no shot in");
                // and the Hero Range's hero stands down while the box is up
                Expect(r.suspended, "range hero suspended");
            });

            It("the opponent fights back at every difficulty (a real AI, not a dummy)", () =>
            {
                foreach (var diff in new[] { "easy", "hard" })
                {
                    var (_, w, me, s) = Arena();
                    s.Arm("nocturne", diff, 3);
                    Enter(me); Run(w, DT); Fight(w, s);
                    Run(w, 8);
                    // (each round starts you from full health: count every round's damage)
                    double taken = s.taken + s.rounds.Sum(x => x.taken);
                    Expect(taken > 20, $"{diff}: it dealt you {taken:0}");
                }
            });

            It("ending the spar from the console while sealed is a forfeit and opens the box", () =>
            {
                var (_, w, me, s) = Arena();
                s.Arm("kagemaru", "easy", 2);
                Enter(me); Run(w, DT); Fight(w, s);
                s.Cancel();
                Expect(!s.Sealed, "open");
                Expect(s.foe == null, "opponent gone");
                Expect(!s.history[0].won, "a forfeit");
                Expect(!w.actors.Any(a => a.baseDef.id == "kagemaru"), "out of the world");
            });
        }
    }
}
