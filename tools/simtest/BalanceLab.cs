// The balance lab: measures every hero on equal terms and tunes the balance table (Assets/ZU/Sim/Data/Balance.cs).
//
//   run.ps1 balance census [--games 400] [--secs 150] [--seed 1] [--workers 6] [--table t.json] [--out census.json] [--yuzu 5]
//   run.ps1 balance tune   [--games 600] [--secs 150] [--iters 8] [--workers 6]     -> BalanceTable.cs, docs/balance-census.json
//
// How a hero is measured (the web game's census - tests/tools/balance.test.ts - fixed the line-ups by faction and map, so a
// hero's numbers were as much its team's): here every game draws a role-locked line-up (one tank, two supports, two damage
// a side) from the WHOLE roster regardless of faction, on each map in turn, and every line-up is played twice with the sides
// swapped. Over a few hundred games every hero has had every other as ally and as enemy on both spawns, so what is left in
// its win rate is the hero. Bots at one skill setting are the instrument: the lab finds kits that are out of line at equal
// skill, it doesn't say what a person will do with one.
//
// Tuning: a hero whose win rate is off 50% has its power scalar (all damage dealt and healing done) moved against the
// error, the same games are replayed, and so on until every hero sits inside the band or its scalar reaches the clamp
// (a clamped hero is reported: its kit or its bot needs work, not a bigger number). The range is lopsided on purpose: a
// bot that wins too much shows real kit power (down to x0.7), a bot that loses may just be a poor bot (up to x1.15 only). Pinned heroes (the user's own
// numbers) are measured, never moved; the others are calibrated with them at their original strength.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.SimTest
{
    static class BalanceLab
    {
        const double DT = 1.0 / 60;
        /// <summary>the scalar's range, the win-rate band that counts as balanced, how hard an error moves the scalar</summary>
        const double P_MIN = 0.85, P_MAX = 1.6, BAND = 0.05, GAIN = 0.9;
        /// <summary>the damage-taken scalar's range: moved only for a hero whose power is at its clamp (or pinned)</summary>
        const double T_MIN = 0.5, T_MAX = 1.5;
        static readonly HashSet<string> PINNED = new HashSet<string> { "yuzu" };
        /// <summary>the user's targets (2026-10-04): these three are the strongest on purpose; everyone else shares what is left
        /// of the zero-sum (about the bottom of the 45-55 % band once the pinned hero's wins are counted)</summary>
        static readonly Dictionary<string, double> TARGET = new Dictionary<string, double> { ["hayate"] = 0.60, ["tenkai"] = 0.60, ["raijin"] = 0.60, ["yuzu"] = 0.60 };
        /// <summary>the win rate the untargeted heroes share: every game hands out five wins, so (5 x games - the targeted
        /// heroes' wins at their targets - the pinned heroes' measured wins) / the others' games</summary>
        static double OthersTarget(GameData data, Census c)
        {
            double wins = 0, games = 0, rest = 0;
            foreach (var kv in c.rows)
            {
                wins += kv.Value.wins;
                if (TARGET.TryGetValue(kv.Key, out var t)) rest += t * kv.Value.games;
                else if (PINNED.Contains(kv.Key)) rest += kv.Value.wins;
                else games += kv.Value.games;
            }
            return games > 0 ? (wins - rest) / games : 0.5;
        }
        static double TargetOf(string id, double others) => TARGET.TryGetValue(id, out var t) ? t : others;
        static readonly CultureInfo INV = CultureInfo.InvariantCulture;

        public sealed class Row { public int games; public double wins, alive, kills, deaths, dmg, heal, mit, ults; }
        public sealed class Census { public int games, errors, draws; public Dictionary<string, Row> rows = new Dictionary<string, Row>(); }

        static string Opt(string[] a, string name, string def) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }

        public static int Run(GameData data, string root, string[] args)
        {
            string sub = args.Length > 1 ? args[1] : "census";
            int games = int.Parse(Opt(args, "--games", sub == "tune" ? "600" : "400")), secs = int.Parse(Opt(args, "--secs", "150"));
            int seed = int.Parse(Opt(args, "--seed", "1")), workers = int.Parse(Opt(args, "--workers", "6")), iters = int.Parse(Opt(args, "--iters", "8"));
            string tablePath = Opt(args, "--table", null), outPath = Opt(args, "--out", null), shard = Opt(args, "--shard", null);
            double yuzu = UnityDivergence.YuzuPower;
            if (tablePath != null) Balance.Table = JsonConvert.DeserializeObject<Dictionary<string, double>>(File.ReadAllText(tablePath));
            if (sub == "census" && shard != null)
            {
                // a worker: its share of the games, the result as JSON
                var p = shard.Split('/');
                var c = Play(data, games, secs, seed, int.Parse(p[0]), int.Parse(p[1]));
                File.WriteAllText(outPath, JsonConvert.SerializeObject(c));
                return 0;
            }
            if (sub == "census")
            {
                var c = Parallel(games, secs, seed, workers, Balance.Table, yuzu);
                Print(data, c, Balance.Table, $"census: {c.games} games ({secs} s cap), seed {seed}, Yuzu x{yuzu:0.##}");
                if (outPath != null) File.WriteAllText(outPath, Report(data, c, Balance.Table, secs, yuzu));
                return c.errors == 0 ? 0 : 1;
            }
            if (sub == "tune")
            {
                var table = new Dictionary<string, double>(Balance.Table);
                foreach (var h in Roster(data)) if (!table.ContainsKey(h.id)) table[h.id] = 1;
                foreach (var id in PINNED) table[id] = 1;
                // before: no table, on the games the result is checked on (the pinned hero as shipped, as in every run here:
                // the others are calibrated against what players will meet)
                var ones = Roster(data).ToDictionary(h => h.id, h => 1.0);
                foreach (var k in table.Keys.Where(k => k.EndsWith(Balance.TAKEN)).ToList()) if (Roster(data).All(h => h.id + Balance.TAKEN != k)) table.Remove(k);
                var before = Parallel(games, secs, seed + 1000, workers, ones, yuzu);
                Print(data, before, ones, $"BEFORE (no table) on the validation games (seed {seed + 1000}), Yuzu x{yuzu:0.##}");
                File.WriteAllText(Path.Combine(root, "docs/balance-before.json"), Report(data, before, ones, secs, yuzu));
                Census c = null;
                for (int it = 1; it <= iters; it++)
                {
                    c = Parallel(games, secs, seed, workers, table, yuzu);
                    double others = OthersTarget(data, c);
                    Print(data, c, table, $"tune {it}/{iters}: {c.games} games, seed {seed}; targets {string.Join(", ", TARGET.Select(kv => $"{kv.Key} {kv.Value:0.00}"))}, the others {others:0.000}");
                    double worst = 0; int moved = 0;
                    foreach (var h in Roster(data))
                    {
                        if (!c.rows.TryGetValue(h.id, out var r) || r.games == 0) continue;
                        bool pinned = PINNED.Contains(h.id);
                        if (pinned && !TARGET.ContainsKey(h.id)) continue;                 // measured only
                        double err = r.wins / r.games - TargetOf(h.id, others);
                        // power first (never a pinned hero's: those numbers are the user's); once it is at its clamp, toughness
                        double p = table[h.id], q = pinned ? p : Math.Min(P_MAX, Math.Max(P_MIN, p * Math.Exp(-GAIN * err)));
                        bool spent = pinned || (q == p && ((p == P_MIN && err > 0) || (p == P_MAX && err < 0)));
                        string tk = h.id + Balance.TAKEN; double t0 = table.TryGetValue(tk, out var tv) ? tv : 1, t1 = t0;
                        if (spent) t1 = Math.Min(T_MAX, Math.Max(T_MIN, t0 * Math.Exp(GAIN * err)));
                        bool stuck = spent && t1 == t0 && ((t0 == T_MIN && err < 0) || (t0 == T_MAX && err > 0));
                        if (!stuck) worst = Math.Max(worst, Math.Abs(err));
                        if (Math.Abs(err) <= 0.02) continue;
                        if (q != p) { table[h.id] = Math.Round(q, 3); moved++; }
                        else if (t1 != t0) { table[tk] = Math.Round(t1, 3); moved++; }
                    }
                    Console.WriteLine($"  worst unclamped error {worst:0.000}, {moved} scalars moved");
                    // (the table so far, after every pass: a run cut short still leaves its best table)
                    File.WriteAllText(Path.Combine(root, "Assets/ZU/Sim/Data/BalanceTable.cs"), TableSource(data, table));
                    if (worst <= 0.03 || moved == 0) break;
                }
                // the check: the same unseen games as `before`, with the table - the game as it ships
                var check = Parallel(games, secs, seed + 1000, workers, table, yuzu);
                Print(data, check, table, $"AFTER (the table) on the validation games (seed {seed + 1000}), Yuzu x{yuzu:0.##}; the others' share {OthersTarget(data, check):0.000}");
                File.WriteAllText(Path.Combine(root, "Assets/ZU/Sim/Data/BalanceTable.cs"), TableSource(data, table));
                File.WriteAllText(Path.Combine(root, "docs/balance-census.json"), Report(data, check, table, secs, yuzu));
                Console.WriteLine("wrote Assets/ZU/Sim/Data/BalanceTable.cs, docs/balance-before.json, docs/balance-census.json");
                return 0;
            }
            Console.Error.WriteLine("balance: census | tune");
            return 2;
        }

        static List<HeroDef> Roster(GameData data) => data.Heroes.Where(h => !h.summoned && (h.role == "tank" || h.role == "dps" || h.role == "support")).ToList();

        // ------------------------------------------------------------------------------------------------ the games
        /// <summary>games `shard` of `of` (every line-up is two games: the sides swapped)</summary>
        static Census Play(GameData data, int games, int secs, int seed, int shard, int of)
        {
            var c = new Census();
            var roster = Roster(data);
            var maps = data.Maps.Where(m => m.id != "training" && !m.retired).ToList();
            for (int g = 0; g < games / 2; g++)
            {
                if (g % of != shard) continue;
                var rng = new Random(seed * 7919 + g);
                List<string> Pick(string role, int n) => roster.Where(h => h.role == role).Select(h => h.id).OrderBy(_ => rng.Next()).Take(n).ToList();
                List<string> t = Pick("tank", 2), s = Pick("support", 4), d = Pick("dps", 4);
                var A = new List<string> { t[0], s[0], s[1], d[0], d[1] }; var B = new List<string> { t[1], s[2], s[3], d[2], d[3] };
                var map = maps[g % maps.Count];
                for (int swap = 0; swap < 2; swap++)
                {
                    Rng.Seed((uint)(seed * 100003 + g * 2 + swap));
                    try { Game(data, c, map, swap == 0 ? A : B, swap == 0 ? B : A, secs); }
                    catch (Exception e) { c.errors++; Console.Error.WriteLine($"game {g}/{swap} on {map.id}: {e.GetType().Name}: {e.Message}"); }
                }
            }
            return c;
        }

        static void Game(GameData data, Census c, MapDef map, List<string> zenith, List<string> umbra, int secs)
        {
            var world = new World(map, "aitest", true, null, null);
            var nav = new BoxNav((BoxLevel)world.level); world.nav = nav;
            var heroes = new List<Actor>();
            foreach (var (ids, team) in new[] { (zenith, "zenith"), (umbra, "umbra") })
                foreach (var id in ids)
                {
                    var a = world.AddHero(id, team);
                    a.controller = new Bot(world, a, nav, 0.8);
                    heroes.Add(a);
                }
            var alive = new double[heroes.Count];
            for (int i = 0; i < secs * 60 && world.winner == null; i++)
            {
                world.Step(DT); world.events.Clear();
                for (int k = 0; k < heroes.Count; k++) if (heroes[k].alive) alive[k] += DT;
            }
            c.games++; if (world.winner == null) c.draws++;
            for (int k = 0; k < heroes.Count; k++)
            {
                var a = heroes[k]; string id = a.baseDef.id;
                if (!c.rows.TryGetValue(id, out var r)) c.rows[id] = r = new Row();
                r.games++; r.wins += world.winner == null ? 0.5 : world.winner == a.team ? 1 : 0;
                r.alive += alive[k]; r.kills += a.kills; r.deaths += a.deaths; r.dmg += a.dmgDone; r.heal += a.healDone; r.mit += a.mitigated; r.ults += a.ults;
            }
        }

        /// <summary>the games split over worker processes (the simulation keeps static state: one match per process at a time)</summary>
        static Census Parallel(int games, int secs, int seed, int workers, Dictionary<string, double> table, double yuzu)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "zu-balance-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(tmp);
            string tablePath = Path.Combine(tmp, "table.json");
            File.WriteAllText(tablePath, JsonConvert.SerializeObject(table));
            string dll = typeof(BalanceLab).Assembly.Location, exe = Environment.ProcessPath;
            var procs = new List<(Process p, string o)>();
            for (int i = 0; i < workers; i++)
            {
                string o = Path.Combine(tmp, $"shard{i}.json");
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var a in new[] { dll, "balance", "census", "--games", games.ToString(), "--secs", secs.ToString(), "--seed", seed.ToString(), "--shard", $"{i}/{workers}",
                    "--table", tablePath, "--out", o, "--yuzu", yuzu.ToString(INV) }) psi.ArgumentList.Add(a);
                procs.Add((Process.Start(psi), o));
            }
            var all = new Census();
            foreach (var (p, o) in procs)
            {
                p.WaitForExit();
                if (!File.Exists(o)) { all.errors++; continue; }
                var c = JsonConvert.DeserializeObject<Census>(File.ReadAllText(o));
                all.games += c.games; all.errors += c.errors; all.draws += c.draws;
                foreach (var kv in c.rows)
                {
                    if (!all.rows.TryGetValue(kv.Key, out var r)) all.rows[kv.Key] = r = new Row();
                    var x = kv.Value;
                    r.games += x.games; r.wins += x.wins; r.alive += x.alive; r.kills += x.kills; r.deaths += x.deaths; r.dmg += x.dmg; r.heal += x.heal; r.mit += x.mit; r.ults += x.ults;
                }
            }
            try { Directory.Delete(tmp, true); } catch (IOException) { /* a worker still closing its file */ }
            return all;
        }

        // ------------------------------------------------------------------------------------------------ reports
        static IEnumerable<(HeroDef h, Row r)> Rows(GameData data, Census c) =>
            Roster(data).Where(h => c.rows.ContainsKey(h.id)).Select(h => (h, c.rows[h.id])).OrderBy(x => x.h.role).ThenByDescending(x => x.Item2.wins / Math.Max(1, x.Item2.games));

        static void Print(GameData data, Census c, Dictionary<string, double> table, string title)
        {
            Console.WriteLine($"\n== {title}; draws {c.draws}, errors {c.errors}");
            Console.WriteLine($"{"hero",-10}{"role",8}{"power",7}{"taken",6}{"games",7}{"win",7}{"K/D",6}{"K/10",6}{"D/10",6}{"dmg/10",8}{"heal/10",8}{"mit/10",8}{"ult/10",7}");
            foreach (var (h, r) in Rows(data, c))
            {
                double min = Math.Max(1e-6, r.alive / 60), wr = r.wins / Math.Max(1, r.games);
                string flag = PINNED.Contains(h.id) ? "  PINNED" : TARGET.TryGetValue(h.id, out var tg) ? (Math.Abs(wr - tg) > 0.03 ? $"  << target {tg:0.00}" : $"  (target {tg:0.00})") : Math.Abs(wr - 0.5) > BAND ? "  <<" : "";
                Console.WriteLine($"{h.id,-10}{h.role,8}{(table.TryGetValue(h.id, out var p) ? p : 1),7:0.000}{(table.TryGetValue(h.id + Balance.TAKEN, out var tkn) ? tkn : 1),6:0.00}{r.games,7}{wr,7:0.000}{r.kills / Math.Max(1, r.deaths),6:0.00}{r.kills / min * 10,6:0.0}{r.deaths / min * 10,6:0.0}" +
                                  $"{r.dmg / min * 10,8:0}{r.heal / min * 10,8:0}{r.mit / min * 10,8:0}{r.ults / min * 10,7:0.0}{flag}");
            }
        }

        static string Report(GameData data, Census c, Dictionary<string, double> table, int secs, double yuzu)
        {
            var heroes = Rows(data, c).Select(x =>
            {
                var (h, r) = x; double min = Math.Max(1e-6, r.alive / 60);
                return new
                {
                    h.id, h.role, power = table.TryGetValue(h.id, out var p) ? p : 1, taken = table.TryGetValue(h.id + Balance.TAKEN, out var tkn) ? tkn : 1,
                    target = TARGET.TryGetValue(h.id, out var tg) ? tg : (double?)null, pinned = PINNED.Contains(h.id), r.games,
                    winRate = Math.Round(r.wins / Math.Max(1, r.games), 3), kd = Math.Round(r.kills / Math.Max(1, r.deaths), 2),
                    killsPer10 = Math.Round(r.kills / min * 10, 1), deathsPer10 = Math.Round(r.deaths / min * 10, 1), dmgPer10 = Math.Round(r.dmg / min * 10),
                    healPer10 = Math.Round(r.heal / min * 10), mitPer10 = Math.Round(r.mit / min * 10), ultsPer10 = Math.Round(r.ults / min * 10, 1),
                };
            }).ToList();
            return JsonConvert.SerializeObject(new { c.games, c.draws, c.errors, secs, yuzuPower = yuzu, band = BAND, clamp = new[] { P_MIN, P_MAX }, heroes }, Formatting.Indented);
        }

        static string TableSource(GameData data, Dictionary<string, double> table)
        {
            double T(string id) => table.TryGetValue(id + Balance.TAKEN, out var t) ? t : 1;
            double Pw(string id) => PINNED.Contains(id) ? 1 : table.TryGetValue(id, out var p) ? p : 1;
            var rows = Roster(data).Where(h => Math.Abs(Pw(h.id) - 1) > 1e-9 || Math.Abs(T(h.id) - 1) > 1e-9)
                .Select(h => $"            (\"{h.id}\", {Pw(h.id).ToString("0.###", INV)}, {T(h.id).ToString("0.###", INV)}),");
            return "// GENERATED by tools/simtest `balance tune` - do not edit by hand; rerun the lab after any kit change (docs/BALANCE.md).\n" +
                   "namespace ZU.Sim.Data\n{\n    public static partial class Balance\n    {\n        static readonly (string id, double power, double taken)[] SHIPPED =\n        {\n" +
                   string.Join("\n", rows) + (rows.Any() ? "\n" : "") + "        };\n    }\n}\n";
        }
    }
}
