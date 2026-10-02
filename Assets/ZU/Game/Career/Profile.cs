// Career Profile (src/game/career.ts), after Overwatch 2's Career Profile:
//  - every match's numbers are kept per game mode and per hero (Overview: time played, games won, time per mode and
//    per role, Top Heroes; Statistics: Total / Best (one game) / Average per 10 minutes, filtered by mode and hero)
//  - Hero Skill Rating (Overwatch 2 Season 18): a 0-5000 rating per hero for each ranked queue (5000 = the top of
//    Champion 1). A hero places after 5 matches in which it was played at least 3 minutes and was one of your 3
//    most-played heroes; every hero played in a match is updated by its share of the time. It never feeds matchmaking.
//  - Hero levels (Overwatch 2 Hero Progression): 150 XP per minute played in a qualifying mode; the first 20 levels
//    cost more and more (up to 10,000 XP), every level after that is 10,000 XP (~67 minutes). Badge tiers at levels
//    1/25/50/75/100, ascended portrait tiers at 20/40/60/80.
// Pure functions + a JSON store in the player's data folder (the TS uses localStorage); CareerTracker turns a running
// match into a MatchSummary.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Career
{
    /// <summary>one hero's (or a whole mode's) lifetime numbers</summary>
    [Serializable]
    public class HeroLine
    {
        public double time, games, wins, losses, draws, elims, finalBlows, assists, deaths, damage, healing, mitigated, shots, hits, crits, objTime, objKills, ults;
        /// <summary>best in one game</summary>
        public double bElims, bFinal, bDamage, bHealing, bMitigated, bObjTime, bStreak, bMulti, bAcc;
        /// <summary>hero-specific counters (Actor.stats: heal assists, packs, ...)</summary>
        public Dictionary<string, double> hero = new Dictionary<string, double>();

        public void Add(HeroLine l)
        {
            time += l.time; games += l.games; wins += l.wins; losses += l.losses; draws += l.draws; elims += l.elims; finalBlows += l.finalBlows;
            assists += l.assists; deaths += l.deaths; damage += l.damage; healing += l.healing; mitigated += l.mitigated; shots += l.shots; hits += l.hits;
            crits += l.crits; objTime += l.objTime; objKills += l.objKills; ults += l.ults;
            bElims = Math.Max(bElims, l.bElims); bFinal = Math.Max(bFinal, l.bFinal); bDamage = Math.Max(bDamage, l.bDamage); bHealing = Math.Max(bHealing, l.bHealing);
            bMitigated = Math.Max(bMitigated, l.bMitigated); bObjTime = Math.Max(bObjTime, l.bObjTime); bStreak = Math.Max(bStreak, l.bStreak); bMulti = Math.Max(bMulti, l.bMulti); bAcc = Math.Max(bAcc, l.bAcc);
            foreach (var kv in l.hero) hero[kv.Key] = (hero.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
        }
    }

    /// <summary>one hero's part of one match</summary>
    public class HeroSlice
    {
        public string hero; public double time;
        public double finalBlows, assists, deaths, damage, healing, mitigated, shots, hits, crits, objTime, objKills, ults, streak, multi;
        public Dictionary<string, double> stats = new Dictionary<string, double>();
    }

    public class MatchSummary
    {
        public string mode, map, result;           // result: win | loss | draw | none
        public long at; public double secs;
        public List<HeroSlice> heroes = new List<HeroSlice>();
        /// <summary>the enemy lobby's matchmaking rating on the 0..3999 rank scale (ranked queues)</summary>
        public double? opp;
        public string score;
    }

    [Serializable] public class HeroSR { public double sr; public int games, wins, losses, placed; public double peak; public long last; public HeroSR Clone() => (HeroSR)MemberwiseClone(); }

    [Serializable]
    public class MatchRecord
    {
        public long at; public string mode, map, result; public double secs; public string hero; public List<string> heroes;
        public double elims, deaths, damage, healing, acc; public string score;
        /// <summary>Hero Skill Rating change of the most-played hero (ranked)</summary>
        public double? sr, srDelta;
    }

    [Serializable]
    public class ProfileData
    {
        public int v = 1; public long created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public Dictionary<string, Dictionary<string, HeroLine>> modes = new Dictionary<string, Dictionary<string, HeroLine>>();
        public Dictionary<string, Dictionary<string, HeroSR>> hsr = new Dictionary<string, Dictionary<string, HeroSR>> { ["competitive"] = new Dictionary<string, HeroSR>(), ["online-comp"] = new Dictionary<string, HeroSR>() };
        public Dictionary<string, double> xp = new Dictionary<string, double>();
        public List<MatchRecord> matches = new List<MatchRecord>();
    }

    public struct LevelView { public int level; public double into, need, pct; public int badge, ascended; }
    public sealed class HsrChange { public string hero; public HeroSR before, after; public double delta; public bool placedNow, qualified; }
    public sealed class RecordResult { public List<HsrChange> hsr = new List<HsrChange>(); public List<(string hero, int from, int to, double xp)> levels = new List<(string, int, int, double)>(); }

    public static class CareerProfile
    {
        public static readonly string[] CAREER_MODES = { "online-qp", "online-comp", "quickplay", "competitive", "practice", "skirmish", "stadium", "campaign", "custom", "training" };
        public static readonly Dictionary<string, string> MODE_LABEL = new Dictionary<string, string>
        {
            ["online-qp"] = "Online Quick Play", ["online-comp"] = "Online Competitive", ["quickplay"] = "Quick Play", ["competitive"] = "Competitive",
            ["practice"] = "AI Quick Match", ["skirmish"] = "Play vs AI", ["stadium"] = "Stadium", ["campaign"] = "Campaign", ["custom"] = "Custom Games", ["training"] = "Training Grounds",
        };
        public static readonly string[] RANKED_MODES = { "online-comp", "competitive" };
        /// <summary>modes whose play time levels your heroes (as in Overwatch: not the practice range or custom games)</summary>
        public static readonly string[] XP_MODES = { "online-qp", "online-comp", "quickplay", "competitive", "practice", "skirmish", "stadium", "campaign" };

        static HeroDef Def(string id) => GameData.Current?.Def(id);

        // ---------------------------------------------------------------- reading
        /// <summary>totals for a mode ("all" = every mode) and a hero ("all" = every hero)</summary>
        public static HeroLine Line(ProfileData p, string mode, string hero)
        {
            var o = new HeroLine();
            foreach (var m in mode == "all" ? CAREER_MODES : new[] { mode })
                if (p.modes.TryGetValue(m, out var t))
                    foreach (var kv in t) if (hero == "all" || kv.Key == hero) o.Add(kv.Value);
            return o;
        }
        /// <summary>heroes with any time in that mode</summary>
        public static List<string> HeroesPlayed(ProfileData p, string mode)
        {
            var s = new List<string>();
            foreach (var m in mode == "all" ? CAREER_MODES : new[] { mode })
                if (p.modes.TryGetValue(m, out var t))
                    foreach (var kv in t) if ((kv.Value.time > 0 || kv.Value.games > 0) && !s.Contains(kv.Key)) s.Add(kv.Key);
            return s;
        }
        public static double Per10(HeroLine l, double v) => l.time > 0 ? v / (l.time / 600) : 0;
        public static double WinPct(HeroLine l) => l.wins + l.losses + l.draws > 0 ? l.wins / (l.wins + l.losses + l.draws) * 100 : 0;
        public static double Accuracy(HeroLine l) => l.shots > 0 ? l.hits / l.shots * 100 : 0;
        public static double CritAccuracy(HeroLine l) => l.hits > 0 ? l.crits / l.hits * 100 : 0;
        public static double ElimsPerLife(HeroLine l) => l.elims / Math.Max(1, l.deaths);

        /// <summary>Overwatch's Hero Comparison dropdown</summary>
        public static readonly (string key, string label)[] COMPARE =
        {
            ("time", "Time Played"), ("wins", "Games Won"), ("winPct", "Win Percentage"), ("acc", "Weapon Accuracy"), ("epl", "Eliminations per Life"),
            ("crit", "Critical Hit Accuracy"), ("multi", "Multikill - Best"), ("objKills", "Objective Kills"), ("sr", "Hero Skill Rating"),
        };
        public static double CompareValue(ProfileData p, string mode, string hero, string key)
        {
            if (key == "sr")
            {
                string q = mode == "competitive" ? "competitive" : "online-comp";
                return p.hsr[q].TryGetValue(hero, out var r) && r.placed >= HSR_PLACEMENTS ? r.sr : 0;
            }
            var l = Line(p, mode, hero);
            switch (key)
            {
                case "time": return l.time; case "wins": return l.wins; case "winPct": return WinPct(l); case "acc": return Accuracy(l);
                case "epl": return ElimsPerLife(l); case "crit": return CritAccuracy(l); case "multi": return l.bMulti; case "objKills": return l.objKills;
            }
            return 0;
        }
        /// <summary>heroes ranked by a comparison key (the Overview's Top Heroes)</summary>
        public static List<(string hero, double value)> TopHeroes(ProfileData p, string mode, string key = "time")
        {
            var ids = key == "sr" ? p.hsr[mode == "competitive" ? "competitive" : "online-comp"].Keys.ToList() : HeroesPlayed(p, mode);
            return ids.Select(h => (h, CompareValue(p, mode, h, key))).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
        }
        /// <summary>time played per mode (the Overview's mode bars)</summary>
        public static List<(string mode, double time)> TimeByMode(ProfileData p) =>
            CAREER_MODES.Select(m => (m, Line(p, m, "all").time)).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
        /// <summary>time played, games won per role</summary>
        public static Dictionary<string, HeroLine> ByRole(ProfileData p, string mode)
        {
            var o = new Dictionary<string, HeroLine> { ["tank"] = new HeroLine(), ["damage"] = new HeroLine(), ["support"] = new HeroLine() };
            foreach (var h in HeroesPlayed(p, mode))
            {
                var r = Def(h)?.role; if (r == null) continue;
                o[r == "dps" ? "damage" : r].Add(Line(p, mode, h));
            }
            return o;
        }
        /// <summary>1:23:45 / 12:05 / 0:42</summary>
        public static string FmtTime(double sec)
        {
            long s = (long)Math.Round(sec); long h = s / 3600, m = s % 3600 / 60, ss = s % 60;
            return h > 0 ? $"{h}:{m:00}:{ss:00}" : $"{m}:{ss:00}";
        }
        /// <summary>"12.4 hours" / "35 minutes" / "40 seconds" (Overwatch's Top Heroes wording)</summary>
        public static string FmtHours(double s)
        {
            if (s >= 3600) return (s / 3600).ToString(s >= 36000 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + " hours";
            return s >= 60 ? Plural(Math.Round(s / 60), "minute") : Plural(Math.Round(s), "second");
        }
        public static string Plural(double n, string one, string many = null) => $"{n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} {(n == 1 ? one : many ?? one + "s")}";

        // ---------------------------------------------------------------- hero levels
        public const double XP_PER_MIN = 150;
        public static readonly int[] BADGE_LEVELS = { 1, 25, 50, 75, 100 };
        public static readonly string[] BADGE_COLOR = { "#9aa3b5", "#5fd38d", "#5aa9ff", "#b07cff", "#ffc94a" };
        public static readonly int[] ASCEND_LEVELS = { 20, 40, 60, 80 };
        public static readonly string[] ASCEND_COLOR = { "#5aa9ff", "#b07cff", "#ffc94a", "#ff5d5d" };
        /// <summary>XP from level L to L+1: 500, 1000, ... 9500, then 10,000 every level</summary>
        public static double XpToNext(int level) => level < 20 ? 500 * level : 10000;
        public static LevelView HeroLevel(double xp)
        {
            int level = 1; double left = Math.Max(0, xp);
            // closed form past 20 (95,000 XP to get there)
            if (left >= 95000) { level = 20 + (int)Math.Floor((left - 95000) / 10000); left = (left - 95000) % 10000; }
            else while (left >= XpToNext(level)) { left -= XpToNext(level); level++; }
            double need = XpToNext(level);
            int badge = 0; for (int i = 0; i < BADGE_LEVELS.Length; i++) if (level >= BADGE_LEVELS[i]) badge = i;
            int asc = 0; for (int i = 0; i < ASCEND_LEVELS.Length; i++) if (level >= ASCEND_LEVELS[i]) asc = i + 1;
            return new LevelView { level = level, into = left, need = need, pct = left / need * 100, badge = badge, ascended = asc };
        }
        /// <summary>the profile's level: every hero's level added up (Overwatch's progression medallion)</summary>
        public static int PlayerLevel(ProfileData p) => p.xp.Values.Sum(x => HeroLevel(x).level);

        // ---------------------------------------------------------------- Hero Skill Rating
        public const double HSR_MAX = 5000;
        public const int HSR_PLACEMENTS = 5;
        /// <summary>the 0..3999 rank scale (8 tiers x 5 divisions x 100) -> 0..5000 (5000 = the top of Champion 1)</summary>
        public static double ToHsr(double rating) => Math.Round(Math.Max(0, Math.Min(3999, rating)) * (HSR_MAX / 4000), MidpointRounding.AwayFromZero);
        public static double HsrToRating(double sr) => Math.Max(0, Math.Min(3999, Math.Round(sr * (4000 / HSR_MAX), MidpointRounding.AwayFromZero)));
        /// <summary>how a hero performed against its role's typical numbers (per 10 minutes): -1 .. 1</summary>
        public static double Performance(HeroSlice s)
        {
            var d = Def(s.hero); double mins = Math.Max(1, s.time / 60) / 10;
            if (d == null || s.time < 60) return 0;
            string role = d.role; double el = (s.finalBlows + s.assists) / mins, de = s.deaths / mins, dmg = s.damage / mins, heal = s.healing / mins;
            var b = role == "support" ? (el: 9.0, de: 5.5, dmg: 3500.0, heal: 7000.0) : role == "tank" ? (el: 12.0, de: 5.0, dmg: 7000.0, heal: 0.0) : (el: 13.0, de: 6.5, dmg: 7500.0, heal: 0.0);
            double z = (el - b.el) / b.el * 0.35 + (b.de - de) / b.de * 0.25 + (dmg - b.dmg) / b.dmg * 0.25;
            if (b.heal > 0) z += (heal - b.heal) / b.heal * 0.25;
            return Math.Max(-1, Math.Min(1, z));
        }
        /// <summary>One ranked match for the heroes you played. seed = where an unplaced hero starts (your role rank, 0..3999
        /// scale), opp = the enemy lobby (0..3999 scale). Every hero played is moved by its share of the match time; only
        /// heroes that qualify (>= 3 minutes - or half the match if it was shorter - and among the 3 most played) count a
        /// placement.</summary>
        public static List<HsrChange> ApplyHsr(Dictionary<string, HeroSR> table, List<HeroSlice> slices, bool won, double opp, Func<string, double> seed)
        {
            double total = slices.Sum(x => x.time);
            var o = new List<HsrChange>();
            if (total <= 0) return o;
            double minT = Math.Min(180, total * 0.5);
            var top3 = slices.OrderByDescending(s => s.time).Take(3).Select(s => s.hero).ToList();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var s in slices)
            {
                if (s.time < 20) continue;
                table.TryGetValue(s.hero, out var prev);
                var r = prev != null ? prev.Clone() : new HeroSR { sr = ToHsr(seed(s.hero)) };
                double share = s.time / total; bool qualified = s.time >= minT && top3.Contains(s.hero);
                bool placing = r.placed < HSR_PLACEMENTS;
                double e = Ranks.Expected(r.sr * 0.8, opp);              // (Elo on the 0..3999 scale)
                double k = placing ? 160 : r.games < 20 ? 70 : 45;
                double delta = Math.Round(k * share * ((won ? 1 : 0) - e) + 30 * share * Performance(s), MidpointRounding.AwayFromZero);
                r.sr = Math.Max(0, Math.Min(HSR_MAX, r.sr + delta));
                if (qualified) { r.games++; if (won) r.wins++; else r.losses++; if (placing) r.placed++; }
                r.last = now;
                bool placedNow = placing && r.placed >= HSR_PLACEMENTS;
                if (r.placed >= HSR_PLACEMENTS) r.peak = Math.Max(r.peak, r.sr);
                table[s.hero] = r;
                o.Add(new HsrChange { hero = s.hero, before = prev, after = r, delta = delta, placedNow = placedNow, qualified = qualified });
            }
            return o;
        }

        // ---------------------------------------------------------------- recording
        /// <summary>add one finished match to the profile (Save after)</summary>
        public static RecordResult RecordMatch(ProfileData p, MatchSummary m, Func<string, double> seed = null)
        {
            seed ??= _ => 1800;
            if (!p.modes.TryGetValue(m.mode, out var table)) p.modes[m.mode] = table = new Dictionary<string, HeroLine>();
            double total = m.heroes.Sum(x => x.time);
            var main = m.heroes.OrderByDescending(x => x.time).FirstOrDefault();
            var res = new RecordResult();
            foreach (var s in m.heroes)
            {
                if (!table.TryGetValue(s.hero, out var l)) table[s.hero] = l = new HeroLine();
                // a game counts for a hero played a real part of it (or the only / most-played one)
                bool counts = s == main || s.time >= Math.Min(60, total * 0.25);
                l.time += s.time;
                if (counts && m.result != "none") { l.games++; if (m.result == "win") l.wins++; else if (m.result == "loss") l.losses++; else l.draws++; }
                else if (counts) l.games++;
                double elims = s.finalBlows + s.assists;
                l.elims += elims; l.finalBlows += s.finalBlows; l.assists += s.assists; l.deaths += s.deaths;
                l.damage += s.damage; l.healing += s.healing; l.mitigated += s.mitigated;
                l.shots += s.shots; l.hits += s.hits; l.crits += s.crits; l.objTime += s.objTime; l.objKills += s.objKills; l.ults += s.ults;
                l.bElims = Math.Max(l.bElims, elims); l.bFinal = Math.Max(l.bFinal, s.finalBlows); l.bDamage = Math.Max(l.bDamage, s.damage);
                l.bHealing = Math.Max(l.bHealing, s.healing); l.bMitigated = Math.Max(l.bMitigated, s.mitigated); l.bObjTime = Math.Max(l.bObjTime, s.objTime);
                l.bStreak = Math.Max(l.bStreak, s.streak); l.bMulti = Math.Max(l.bMulti, s.multi >= 2 ? s.multi : 0);
                if (s.shots >= 30) l.bAcc = Math.Max(l.bAcc, Math.Round(s.hits / s.shots * 100));
                foreach (var kv in s.stats) if (!double.IsNaN(kv.Value) && !double.IsInfinity(kv.Value)) l.hero[kv.Key] = (l.hero.TryGetValue(kv.Key, out var hv) ? hv : 0) + kv.Value;
                if (XP_MODES.Contains(m.mode))
                {
                    int from = HeroLevel(p.xp.TryGetValue(s.hero, out var x0) ? x0 : 0).level;
                    double gain = Math.Round(XP_PER_MIN * s.time / 60);
                    p.xp[s.hero] = (p.xp.TryGetValue(s.hero, out var x1) ? x1 : 0) + gain;
                    res.levels.Add((s.hero, from, HeroLevel(p.xp[s.hero]).level, gain));
                }
            }
            string rk = m.mode == "competitive" || m.mode == "online-comp" ? m.mode : null;
            if (rk != null && (m.result == "win" || m.result == "loss")) res.hsr = ApplyHsr(p.hsr[rk], m.heroes, m.result == "win", m.opp ?? 1800, seed);
            if (main != null)
            {
                var ch = res.hsr.FirstOrDefault(h => h.hero == main.hero);
                double sh = m.heroes.Sum(h => h.shots);
                p.matches.Add(new MatchRecord
                {
                    at = m.at, mode = m.mode, map = m.map, result = m.result, secs = m.secs, hero = main.hero, heroes = m.heroes.Select(h => h.hero).ToList(),
                    elims = m.heroes.Sum(h => h.finalBlows + h.assists), deaths = m.heroes.Sum(h => h.deaths),
                    damage = Math.Round(m.heroes.Sum(h => h.damage)), healing = Math.Round(m.heroes.Sum(h => h.healing)),
                    acc = sh > 0 ? Math.Round(m.heroes.Sum(h => h.hits) / sh * 100) : 0, score = m.score,
                    sr = ch?.after.sr, srDelta = ch?.delta,
                });
                if (p.matches.Count > 80) p.matches.RemoveRange(0, p.matches.Count - 80);
            }
            return res;
        }

        // ---------------------------------------------------------------- store
        static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");
        public static ProfileData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var p = JsonConvert.DeserializeObject<ProfileData>(File.ReadAllText(FilePath));
                    if (p != null && p.v == 1)
                    {
                        p.hsr ??= new Dictionary<string, Dictionary<string, HeroSR>>();
                        foreach (var q in RANKED_MODES) if (!p.hsr.ContainsKey(q)) p.hsr[q] = new Dictionary<string, HeroSR>();
                        p.modes ??= new Dictionary<string, Dictionary<string, HeroLine>>(); p.xp ??= new Dictionary<string, double>(); p.matches ??= new List<MatchRecord>();
                        return p;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ZU] career profile unreadable: " + e.Message); }
            return new ProfileData();
        }
        public static void Save(ProfileData p)
        {
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(p)); }
            catch (Exception e) { Debug.LogWarning("[ZU] career profile not saved: " + e.Message); }
        }

        /// <summary>the career mode a match counts under (TS Game.start's careerMode)</summary>
        public static string ModeOf(string mode) => mode switch
        {
            "quickplay" or "competitive" or "practice" or "skirmish" or "stadium" or "campaign" or "training" => mode,
            _ => null,                          // spectate, AI lab, the Ult Viewer: nothing to record
        };

        /// <summary>on or near the objective (the point, or the payload)</summary>
        public static bool OnObjective(World w, Actor a)
        {
            if (w.rules == "push") return Math.Sqrt(Sq(a.pos.x - w.push.pos.x) + Sq(a.pos.z - w.push.pos.z)) < 12;
            var p = w.map.point; if (p == null) return false;
            return Math.Sqrt(Sq(a.pos.x - p[0]) + Sq(a.pos.z - p[2])) < w.point.r * 1.8;
        }
        static double Sq(double v) => v * v;
    }

    /// <summary>Follows the local player through a match: time on each hero (a hero swap starts a new slice), the counters
    /// the World keeps on the Actor, multikills and objective kills. Frame every rendered frame with the SIM time that
    /// passed (pauses don't count), Finish once at the end.</summary>
    public sealed class CareerTracker
    {
        const double MULTI_GAP = 3;   // seconds between eliminations that keep a multikill going
        public readonly string mode, map;
        readonly Dictionary<string, HeroSlice> slices = new Dictionary<string, HeroSlice>();
        public double secs;
        Actor cur;
        Dictionary<string, double> bas = new Dictionary<string, double>();
        int lastKills; double chain, lastKillAt = -99, bestMulti;

        public CareerTracker(string mode, string map) { this.mode = mode; this.map = map; }

        static Dictionary<string, double> Counters(Actor a) => new Dictionary<string, double>
        {
            ["kills"] = a.kills, ["assists"] = a.assists, ["deaths"] = a.deaths, ["damage"] = a.dmgDone, ["healing"] = a.healDone, ["mitigated"] = a.mitigated,
            ["shots"] = a.shots, ["hits"] = a.hits, ["crits"] = a.crits, ["objTime"] = a.objTime, ["ults"] = a.ults,
        };
        HeroSlice Slice(string hero) { if (!slices.TryGetValue(hero, out var s)) slices[hero] = s = new HeroSlice { hero = hero }; return s; }
        double B(string k) => bas.TryGetValue(k, out var v) ? v : 0;

        /// <summary>move what the current actor has done since it was attached into its hero's slice</summary>
        void Close()
        {
            var a = cur; if (a == null) return;
            var c = Counters(a); var s = Slice(a.baseDef.id);
            s.finalBlows += c["kills"] - B("kills"); s.assists += c["assists"] - B("assists"); s.deaths += c["deaths"] - B("deaths");
            s.damage += c["damage"] - B("damage"); s.healing += c["healing"] - B("healing"); s.mitigated += c["mitigated"] - B("mitigated");
            s.shots += c["shots"] - B("shots"); s.hits += c["hits"] - B("hits"); s.crits += c["crits"] - B("crits");
            s.objTime += c["objTime"] - B("objTime"); s.ults += c["ults"] - B("ults");
            s.streak = Math.Max(s.streak, a.bestStreak);
            s.multi = Math.Max(s.multi, bestMulti); bestMulti = 0; chain = 0;
            foreach (var kv in a.stats) if (!double.IsNaN(kv.Value) && !double.IsInfinity(kv.Value)) s.stats[kv.Key] = (s.stats.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value - B("st:" + kv.Key);
            cur = null;
        }
        void Attach(Actor a)
        {
            cur = a;
            bas = Counters(a);
            foreach (var kv in a.stats) bas["st:" + kv.Key] = kv.Value;
            lastKills = a.kills;
        }

        public void Frame(World w, Actor me, double dt)
        {
            if (me == null) return;
            if (me != cur) { Close(); Attach(me); }
            if (dt <= 0 || !string.IsNullOrEmpty(w.winner)) return;
            secs += dt;
            Slice(me.baseDef.id).time += dt;
            // eliminations in quick succession (multikill) and on the objective
            if (me.kills > lastKills)
            {
                int n = me.kills - lastKills; lastKills = me.kills;
                chain = w.time - lastKillAt <= MULTI_GAP ? chain + n : n;
                lastKillAt = w.time;
                bestMulti = Math.Max(bestMulti, chain);
                if (CareerProfile.OnObjective(w, me)) Slice(me.baseDef.id).objKills += n;
            }
        }

        public MatchSummary Finish(World w, Actor me, string result, double? opp = null, string score = null)
        {
            if (me != null && me != cur) { Close(); Attach(me); }
            Close();
            return new MatchSummary { mode = mode, map = map, at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), secs = Math.Round(secs), result = result, heroes = slices.Values.Where(s => s.time > 0.5).ToList(), opp = opp, score = score };
        }
    }
}
