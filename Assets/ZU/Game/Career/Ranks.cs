// TS-PARITY: src/game/ranks.ts
// Competitive ranks and matchmaking ratings (desktop edition), in the spirit of Overwatch 2's ranked system:
//  - a rank per role (tank / damage / support): 8 tiers x 5 divisions (5 lowest -> 1), 100 rating points per division
//  - a hidden matchmaking rating (MMR, Elo) per role and one for Quick Play; bots are matched to it (their skill)
//  - 5 placement matches per role before the rank shows (they move the rating the most)
//  - every match moves your progress in the division by a %, and the result screen says why (rank modifiers):
//    Win Streak / Loss Streak, Uphill Battle, Expected, Reversal, Consolation, Hard-Fought, Calibration, Demotion
//    Protection (three matches at 0% of a division before you drop out of it)
// Pure functions + a small JSON store in the player's profile folder (the TS kept it in localStorage).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ZU.Game.Career
{
    [Serializable] public class RoleRank { public double mmr = 1800, rating = 1800; public int games, wins, losses, streak, shield; public RoleRank Clone() => (RoleRank)MemberwiseClone(); }
    [Serializable] public class QuickPlayRank { public double mmr = 1800; public int games, wins; }
    [Serializable] public class MatchLog { public double at; public string mode, role, map, hero, score; public bool won; public double? delta; public List<string> mods; }
    [Serializable]
    public class CareerData
    {
        public int season = 1;
        public Dictionary<string, RoleRank> roles = new Dictionary<string, RoleRank> { ["tank"] = new RoleRank(), ["damage"] = new RoleRank(), ["support"] = new RoleRank() };
        public QuickPlayRank qp = new QuickPlayRank();
        public List<MatchLog> history = new List<MatchLog>();
        public Dictionary<string, double> best = new Dictionary<string, double>();
        /// <summary>online play against other players (TS Career.online / oqp / obest): its own role ranks and Quick Play
        /// rating - matches against AI lobbies don't move them</summary>
        public Dictionary<string, RoleRank> online = new Dictionary<string, RoleRank> { ["tank"] = new RoleRank(), ["damage"] = new RoleRank(), ["support"] = new RoleRank() };
        public QuickPlayRank oqp = new QuickPlayRank();
        public Dictionary<string, double> obest = new Dictionary<string, double>();
    }

    public struct RankView { public int tier, division; public double pct; public string name, label; public Color color; public bool placed; }

    public sealed class RankChange { public RoleRank before, after; public double delta; public List<string> mods; public bool promoted, demoted, placedNow; }

    public static class Ranks
    {
        public static readonly Dictionary<string, string> ROLE_OF = new Dictionary<string, string> { ["tank"] = "tank", ["dps"] = "damage", ["support"] = "support" };
        public static readonly string[] TIERS = { "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Master", "Grandmaster", "Champion" };
        public static readonly string[] TIER_COLOR = { "#b07a4a", "#c3ccd8", "#f2c14e", "#6fd6d0", "#7aa7ff", "#f0a35e", "#c77dff", "#ff5d7a" };
        public const int PLACEMENTS = 5;
        const int DIV = 100, TIER_SPAN = DIV * 5;
        static readonly int TOP = TIERS.Length * TIER_SPAN - 1;   // 0 .. 3999

        static double JsRound(double x) => Math.Floor(x + 0.5);

        /// <summary>the visible rank for a rating: tier, division (5..1), % through the division</summary>
        public static RankView RankOf(double rating, int games = PLACEMENTS)
        {
            double r = Math.Max(0, Math.Min(TOP, rating));
            int tier = (int)Math.Floor(r / TIER_SPAN); double within = r - tier * TIER_SPAN;
            int division = 5 - (int)Math.Floor(within / DIV); double pct = within % DIV;
            bool placed = games >= PLACEMENTS;
            return new RankView { tier = tier, name = TIERS[tier], division = division, pct = pct, color = Conv.Hex(TIER_COLOR[tier], Color.white), placed = placed, label = placed ? $"{TIERS[tier]} {division}" : $"Placements {games}/{PLACEMENTS}" };
        }

        /// <summary>Elo expectation of beating a lobby rated `opp`</summary>
        public static double Expected(double mmr, double opp) => 1 / (1 + Math.Pow(10, (opp - mmr) / 400));
        /// <summary>bot skill (0..1, the AI's aim and decision speed) that plays at a matchmaking rating</summary>
        public static double SkillFor(double mmr) => Math.Max(0.3, Math.Min(0.97, 0.3 + (mmr - 800) / 3200 * 0.67));

        /// <summary>one competitive match for a role: opp = the enemy lobby's rating, close = it went the distance</summary>
        public static RankChange ApplyCompetitive(RoleRank prev, bool won, double opp, bool close = false)
        {
            var r = prev.Clone();
            bool placing = r.games < PLACEMENTS, calib = !placing && r.games < PLACEMENTS + 5;
            double e = Expected(r.mmr, opp);
            // hidden rating: Elo, larger steps while it is still uncertain
            r.mmr = Math.Max(0, Math.Min(TOP, r.mmr + (placing ? 64 : calib ? 40 : 28) * ((won ? 1 : 0) - e)));
            var mods = new List<string>();
            double delta;
            if (placing)
            {
                // placements move the visible rating straight toward the hidden one
                delta = JsRound((r.mmr - r.rating) * 0.6 + (won ? 30 : -30));
                mods.Add("Placement");
            }
            else
            {
                delta = won ? 24 : -22;
                double pull = (r.mmr - r.rating) * 0.12;          // the visible rank converges on the matchmaking rating
                delta += JsRound(pull);
                if (won && r.streak >= 2) { delta += 6 + 2 * Math.Min(3, r.streak - 2); mods.Add("Win Streak"); }
                if (!won && r.streak <= -2) { delta -= 4 + 2 * Math.Min(3, -r.streak - 2); mods.Add("Loss Streak"); }
                if (won && r.streak <= -2) { delta += 5; mods.Add("Reversal"); }
                if (!won && r.streak >= 2) { delta += 5; mods.Add("Consolation"); }
                if (!won && e < 0.4) { delta = JsRound(delta * 0.6); mods.Add("Uphill Battle"); }
                if (won && e > 0.65) { delta = JsRound(delta * 0.7); mods.Add("Expected"); }
                if (close) { delta += won ? 3 : 2; mods.Add(won ? "Hard-Fought Win" : "Hard-Fought Loss"); }
                if (calib) { delta = JsRound(delta * 1.5); mods.Add("Calibration"); }
            }
            var before = RankOf(r.rating, r.games);
            double next = Math.Max(0, Math.Min(TOP, r.rating + delta));
            // demotion protection: at the bottom of a division you stay put for up to three losses before dropping
            double floor = Math.Floor(r.rating / DIV) * DIV;
            if (!placing && !won && next < floor)
            {
                if (r.shield < 3) { r.shield++; next = floor; mods.Add($"Demotion Protection {r.shield}/3"); }
                else r.shield = 0;                                // protection used up: this loss drops you
            }
            if (won) r.shield = 0;
            double realDelta = next - r.rating;
            r.rating = next;
            r.games++; if (won) r.wins++; else r.losses++;
            r.streak = won ? Math.Max(1, r.streak + 1) : Math.Min(-1, r.streak - 1);
            var after = RankOf(r.rating, r.games);
            int Step(RankView v) => v.tier * 5 + (5 - v.division);
            return new RankChange
            {
                before = prev, after = r, delta = realDelta, mods = mods,
                promoted = after.placed && before.placed && Step(after) > Step(before),
                demoted = after.placed && before.placed && Step(after) < Step(before),
                placedNow = !before.placed && after.placed,
            };
        }

        /// <summary>Quick Play: only a hidden matchmaking rating moves (online = the online Quick Play rating)</summary>
        public static void ApplyQuickPlay(CareerData c, bool won, double opp, bool online = false)
        {
            var q = online ? c.oqp : c.qp;
            q.mmr = Math.Max(0, Math.Min(TOP, q.mmr + 24 * ((won ? 1 : 0) - Expected(q.mmr, opp))));
            q.games++; if (won) q.wins++;
        }

        // ------------------------------------------------------------------------------------------------ the store
        static string PathOf => System.IO.Path.Combine(Application.persistentDataPath, "career-v1.json");
        public static CareerData Load()
        {
            try
            {
                if (File.Exists(PathOf))
                {
                    var c = JsonConvert.DeserializeObject<CareerData>(File.ReadAllText(PathOf));
                    if (c?.roles != null)
                    {
                        // saves from before online play: its tables start fresh
                        c.online ??= new Dictionary<string, RoleRank>();
                        foreach (var r in new[] { "tank", "damage", "support" }) if (!c.online.ContainsKey(r)) c.online[r] = new RoleRank();
                        c.oqp ??= new QuickPlayRank(); c.obest ??= new Dictionary<string, double>();
                        return c;
                    }
                }
            }
            catch { /* a damaged profile starts fresh */ }
            return new CareerData();
        }
        public static void Save(CareerData c)
        {
            if (c.history.Count > 30) c.history = c.history.GetRange(c.history.Count - 30, 30);
            try { File.WriteAllText(PathOf, JsonConvert.SerializeObject(c, Formatting.Indented)); } catch { /* read-only profile */ }
        }
        /// <summary>the lobby's rating for a queue: your rating with a little spread (matchmaking tolerance)</summary>
        public static double LobbyRating(double mmr) => mmr + (UnityEngine.Random.value - 0.5) * 160;
    }
}
