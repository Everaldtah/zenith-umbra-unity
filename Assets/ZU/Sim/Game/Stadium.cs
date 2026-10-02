// TS-PARITY: src/game/stadium.ts
// STADIUM - the third-person, round-based mode (after Overwatch 2's Stadium): first team to 4 round wins takes the
// match. Between rounds every hero shops in the Armory with the cash they earned (damage, healing, eliminations,
// assists, round result): stat ITEMS in three categories and three tiers (6 slots), and one hero POWER at the start of
// rounds 1, 3, 5 and 7 - each power upgrades a piece of that hero's own kit.
//
// Items and powers only ever change the actor's `mods` (Actor.Mods); World / weapons / abilities read them.
// Outside Stadium every mod is zero, so no other mode changes.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public sealed class StadiumItem
    {
        public string id, name, cat, tier, desc; public double cost;
        public double weapon, ability, atkspd, cdr, speed, armor, lifesteal, reload, ammo, ultgain, healing;
    }

    public sealed class StadiumPower { public string id, name, desc; public Action<Mods> apply; }

    public class Stadium
    {
        // ------------------------------------------------------------------------------------------------ the Armory
        /// <summary>the Armory stock (original names; tiers roughly x1 / x3.5 / x8 in price and power)</summary>
        public static readonly List<StadiumItem> ITEMS = new List<StadiumItem>
        {
            // ---- weapon
            new StadiumItem { id = "honed_edge", name = "Honed Edge", cat = "weapon", tier = "common", cost = 1000, weapon = 0.05, desc = "+5% Weapon Power" },
            new StadiumItem { id = "quickdraw_grip", name = "Quickdraw Grip", cat = "weapon", tier = "common", cost = 1000, atkspd = 0.05, reload = 0.15, desc = "+5% Attack Speed, +15% Reload Speed" },
            new StadiumItem { id = "deep_magazine", name = "Deep Magazine", cat = "weapon", tier = "common", cost = 1000, ammo = 0.25, weapon = 0.02, desc = "+25% Max Ammo, +2% Weapon Power" },
            new StadiumItem { id = "sunforged_barrel", name = "Sunforged Barrel", cat = "weapon", tier = "rare", cost = 3750, weapon = 0.12, atkspd = 0.05, desc = "+12% Weapon Power, +5% Attack Speed" },
            new StadiumItem { id = "bloodthirst_sigil", name = "Bloodthirst Sigil", cat = "weapon", tier = "rare", cost = 4000, weapon = 0.08, lifesteal = 0.12, desc = "+8% Weapon Power, +12% Life Steal" },
            new StadiumItem { id = "eclipse_trigger", name = "Eclipse Trigger", cat = "weapon", tier = "epic", cost = 9500, weapon = 0.2, atkspd = 0.12, ammo = 0.2, desc = "+20% Weapon Power, +12% Attack Speed, +20% Max Ammo" },
            // ---- ability
            new StadiumItem { id = "focus_charm", name = "Focus Charm", cat = "ability", tier = "common", cost = 1000, ability = 0.05, ultgain = 0.05, desc = "+5% Ability Power, +5% Ultimate Charge" },
            new StadiumItem { id = "clockwork_heart", name = "Clockwork Heart", cat = "ability", tier = "common", cost = 1250, cdr = 0.06, desc = "-6% Ability Cooldowns" },
            new StadiumItem { id = "starlit_rosary", name = "Starlit Rosary", cat = "ability", tier = "rare", cost = 4000, ability = 0.12, healing = 0.1, desc = "+12% Ability Power, +10% Healing" },
            new StadiumItem { id = "rift_capacitor", name = "Rift Capacitor", cat = "ability", tier = "rare", cost = 4000, cdr = 0.12, ultgain = 0.1, desc = "-12% Ability Cooldowns, +10% Ultimate Charge" },
            new StadiumItem { id = "crown_of_dawn", name = "Crown of Dawn", cat = "ability", tier = "epic", cost = 10000, ability = 0.25, cdr = 0.1, ultgain = 0.15, desc = "+25% Ability Power, -10% Cooldowns, +15% Ultimate Charge" },
            // ---- survival
            new StadiumItem { id = "padded_plating", name = "Padded Plating", cat = "survival", tier = "common", cost = 1000, armor = 25, desc = "+25 Armor" },
            new StadiumItem { id = "runner_soles", name = "Runner Soles", cat = "survival", tier = "common", cost = 1000, speed = 0.05, armor = 10, desc = "+5% Move Speed, +10 Armor" },
            new StadiumItem { id = "medic_satchel", name = "Medic Satchel", cat = "survival", tier = "common", cost = 1250, healing = 0.1, desc = "+10% Healing" },
            new StadiumItem { id = "aegis_weave", name = "Aegis Weave", cat = "survival", tier = "rare", cost = 4000, armor = 75, speed = 0.03, desc = "+75 Armor, +3% Move Speed" },
            new StadiumItem { id = "vampiric_mantle", name = "Vampiric Mantle", cat = "survival", tier = "rare", cost = 4500, armor = 40, lifesteal = 0.1, desc = "+40 Armor, +10% Life Steal" },
            new StadiumItem { id = "colossus_core", name = "Colossus Core", cat = "survival", tier = "epic", cost = 10000, armor = 150, speed = 0.06, healing = 0.1, desc = "+150 Armor, +6% Move Speed, +10% Healing" },
        };
        public static readonly Dictionary<string, StadiumItem> ITEM = ITEMS.ToDictionary(i => i.id);
        public const int MAX_ITEMS = 6;

        /// <summary>six powers per hero, each one built from a piece of that hero's kit; a hero picks four over a match</summary>
        public static List<StadiumPower> PowersFor(HeroDef def)
        {
            SlotDef p = def.primary, S = def.secondary, a1 = def.ability1, a2 = def.ability2, u = def.ult;
            string pname = p.name ?? (p.kind == "melee" ? "Blade" : p.kind == "beam" ? "Stream" : p.kind == "charge" ? "Bow" : "Weapon");
            string guard = def.role == "tank" ? "Unbreakable Frame" : def.role == "support" ? "Guardian Grace" : "Killer Instinct";
            string guardDesc = def.role == "tank" ? "+150 Armor and 10% Life Steal." : def.role == "support" ? "+25% Healing and +50 Armor." : "+10% Weapon Power and 15% Life Steal.";
            double By(Mods m, string id) => m.cdrBy.TryGetValue(id, out var v) ? v : 0;
            return new List<StadiumPower>
            {
                new StadiumPower { id = $"{def.id}_a1", name = $"{a1.name}: Overclock", desc = $"{a1.name} recharges 35% faster and hits 15% harder.", apply = m => { m.cdrBy[a1.id] = By(m, a1.id) + 0.35; m.ability += 0.15; } },
                new StadiumPower { id = $"{def.id}_a2", name = $"{a2.name}: Amplified", desc = $"{a2.name} recharges 25% faster; abilities gain 20% power.", apply = m => { m.cdrBy[a2.id] = By(m, a2.id) + 0.25; m.ability += 0.2; } },
                new StadiumPower { id = $"{def.id}_ult", name = $"{u.name}: Resonance", desc = $"{u.name} charges 35% faster.", apply = m => { m.ultgain += 0.35; } },
                new StadiumPower { id = $"{def.id}_wpn", name = $"{pname}: Signature", desc = $"{pname} deals 15% more damage and fires 10% faster.", apply = m => { m.weapon += 0.15; m.atkspd += 0.1; } },
                new StadiumPower { id = $"{def.id}_guard", name = guard, desc = guardDesc, apply = m =>
                {
                    if (def.role == "tank") { m.armor += 150; m.lifesteal += 0.1; }
                    else if (def.role == "support") { m.healing += 0.25; m.armor += 50; }
                    else { m.weapon += 0.1; m.lifesteal += 0.15; }
                } },
                new StadiumPower { id = $"{def.id}_move", name = S != null && S.IsAbility ? $"{S.name}: Momentum" : "Momentum", desc = "+10% Move Speed, +30% Reload Speed, -10% Cooldowns.", apply = m => { m.speed += 0.1; m.reload += 0.3; m.cdr += 0.1; } },
            };
        }

        /// <summary>recompute an actor's mods from what it owns</summary>
        public static void Recalc(Actor a)
        {
            var m = new Mods();
            foreach (var id in a.items)
            {
                if (!ITEM.TryGetValue(id, out var it)) continue;
                m.weapon += it.weapon; m.ability += it.ability; m.atkspd += it.atkspd; m.cdr += it.cdr; m.speed += it.speed; m.armor += it.armor;
                m.lifesteal += it.lifesteal; m.reload += it.reload; m.ammo += it.ammo; m.ultgain += it.ultgain; m.healing += it.healing;
            }
            var P = PowersFor(a.baseDef);
            foreach (var id in a.powers) P.FirstOrDefault(p => p.id == id)?.apply(m);
            m.cdr = Math.Min(0.5, m.cdr);
            a.mods = m;
            // bought between rounds: the armor and the bigger magazine are there for the next round straight away
            a.maxArmor = a.def.armor + m.armor; a.armor = a.maxArmor;
            if (a.reloadUntil == 0) a.ammo = a.MaxAmmo;          // TS-PARITY: `if (!a.reloadUntil)`
        }

        // ------------------------------------------------------------------------------------------------ the match flow
        public const int ROUNDS_TO_WIN = 4;
        public const double ARMORY_SECS = 25, FIRST_ARMORY_SECS = 35, ROUND_SECS = 120, START_CASH = 3500;
        public static readonly int[] POWER_ROUNDS = { 1, 3, 5, 7 };

        public readonly World w;
        public int round = 1;
        public readonly Dictionary<string, int> wins = new Dictionary<string, int> { ["zenith"] = 0, ["umbra"] = 0 };
        public string phase = "armory";            // armory | fight | over
        public double phaseEnd, roundStart;
        public (string winner, string reason)? lastRound;
        /// <summary>humans who pressed READY in the Armory (the phase ends early once all are ready)</summary>
        public readonly HashSet<int> ready = new HashSet<int>();
        readonly Dictionary<int, (double dmg, double heal, int kills, int assists)> @base = new Dictionary<int, (double, double, int, int)>();

        public Stadium(World w)
        {
            this.w = w;
            foreach (var a in w.actors) { a.cash = START_CASH; Recalc(a); }
            phaseEnd = w.time + FIRST_ARMORY_SECS;
            Snapshot();
        }

        public bool IsPowerRound => Array.IndexOf(POWER_ROUNDS, round) >= 0;
        /// <summary>can this actor pick a power right now?</summary>
        public bool PowerPending(Actor a) => phase == "armory" && IsPowerRound && a.powers.Count < Array.IndexOf(POWER_ROUNDS, round) + 1;

        public bool Buy(Actor a, string id)
        {
            if (!ITEM.TryGetValue(id, out var it) || phase != "armory" || a.cash < it.cost || a.items.Count >= MAX_ITEMS || a.items.Contains(id)) return false;
            a.cash -= it.cost; a.items.Add(id); Recalc(a); w.Sfx("ui_buy", a.pos, a);
            return true;
        }
        public bool Sell(Actor a, string id)
        {
            int i = a.items.IndexOf(id);
            if (i < 0 || phase != "armory") return false;
            a.items.RemoveAt(i); a.cash += ITEM[id].cost; Recalc(a);
            return true;
        }
        public bool PickPower(Actor a, string id)
        {
            if (!PowerPending(a) || a.powers.Contains(id) || !PowersFor(a.baseDef).Any(p => p.id == id)) return false;
            a.powers.Add(id); Recalc(a); w.Sfx("ult_ready", a.pos, a);
            return true;
        }
        public void SetReady(Actor a) => ready.Add(a.id);

        /// <summary>frozen at spawn while the teams shop (World.Step reads it)</summary>
        public bool frozen => phase != "fight";

        public void Update(double dt)
        {
            double t = w.time;
            if (phase == "armory")
            {
                // TS-PARITY: a for-of over w.actors (shopping never adds or removes actors)
                foreach (var a in w.actors) if (!a.isPlayer && string.IsNullOrEmpty(a.netId)) BotShop(this, a);
                var humans = w.actors.Where(a => a.isPlayer).ToList();
                if (humans.Count > 0 && humans.All(h => ready.Contains(h.id) && !PowerPending(h))) phaseEnd = Math.Min(phaseEnd, t + 3);
                if (t >= phaseEnd) StartRound();
                return;
            }
            if (phase != "fight") return;
            UpdatePoint(dt);
        }

        void StartRound()
        {
            double t = w.time;
            phase = "fight"; roundStart = t; ready.Clear();
            // humans who never picked: the first power is theirs (no one enters a power round empty-handed)
            foreach (var a in w.actors)
                if (PowerPending(a)) PickPower(a, PowersFor(a.baseDef).First(p => !a.powers.Contains(p.id)).id);
            var P = w.point;
            P.owner = null; P.capture = 0; P.capTeam = null; P.progress = new Dictionary<string, double> { ["zenith"] = 0, ["umbra"] = 0 }; P.contested = false; P.unlockAt = t + 6;
            w.Msg($"ROUND {round} - FIGHT!", "#ffd76a"); w.Sfx("announce");
        }

        void UpdatePoint(double dt)
        {
            var P = w.point; double t = w.time;
            if (t < P.unlockAt) return;
            if (t - dt < P.unlockAt) { w.Msg("THE POINT IS OPEN"); w.Sfx("announce"); }
            double px = w.map.point[0], py = w.map.point[1], pz = w.map.point[2];
            var on = new Dictionary<string, int> { ["zenith"] = 0, ["umbra"] = 0 };
            foreach (var a in w.actors)
                if (a.alive && M.Hypot(a.pos.x - px, a.pos.z - pz) < P.r && a.pos.y > py - 1 && a.pos.y < py + 5) on[a.team]++;
            P.contested = on["zenith"] > 0 && on["umbra"] > 0;
            string solo = P.contested ? null : on["zenith"] > 0 ? "zenith" : on["umbra"] > 0 ? "umbra" : null;
            if (solo != null && solo != P.owner)
            {
                if (P.capTeam != solo) { P.capture = Math.Max(0, P.capture - dt * 30); if (P.capture == 0) P.capTeam = solo; }
                else P.capture = Math.Min(100, P.capture + dt * (16 + 5 * Math.Min(3, on[solo])));
                if (P.capture >= 100)
                {
                    P.owner = solo; P.capture = 0; P.capTeam = null;
                    w.Msg($"{(solo == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE")} TOOK THE POINT", solo == "zenith" ? "#5cc8ff" : "#ff3b5c");
                    w.Sfx("capture");
                }
            }
            else if (solo == null && !P.contested) P.capture = Math.Max(0, P.capture - dt * 10);
            if (P.owner != null && !P.contested)
            {
                P.progress[P.owner] = Math.Min(100, P.progress[P.owner] + dt * 3.2);
                string other = P.owner == "zenith" ? "umbra" : "zenith";
                if (P.progress[P.owner] >= 100 && on[other] == 0) { EndRound(P.owner, "point secured"); return; }
            }
            if (t - roundStart > ROUND_SECS && !P.contested)
            {
                double z = P.progress["zenith"], u = P.progress["umbra"];
                if (z != u) { EndRound(z > u ? "zenith" : "umbra", "time"); return; }
                int K(string team) => w.actors.Where(a => a.team == team).Sum(a => a.kills - (@base.TryGetValue(a.id, out var b) ? b.kills : 0));
                EndRound(K("zenith") >= K("umbra") ? "zenith" : "umbra", "eliminations");
            }
        }

        void Snapshot() { foreach (var a in w.actors) @base[a.id] = (a.dmgDone, a.healDone, a.kills, a.assists); }

        /// <summary>cash for the round just played: performance + the round result (the losing side gets a catch-up bonus)</summary>
        void Payout(string winner)
        {
            foreach (var a in w.actors)
            {
                var b = @base.TryGetValue(a.id, out var v) ? v : (0, 0, 0, 0);
                double earned = (a.dmgDone - b.dmg) * 1.0 + (a.healDone - b.heal) * 1.1 + (a.kills - b.kills) * 350 + (a.assists - b.assists) * 150;
                a.cash += JsMath.Round(Math.Min(9000, earned) + (a.team == winner ? 2000 : 2800));
            }
            Snapshot();
        }

        void EndRound(string winner, string reason)
        {
            double t = w.time;
            wins[winner]++;
            lastRound = (winner, reason);
            string name = winner == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE";
            w.Msg($"ROUND {round} - {name} ({wins["zenith"]}-{wins["umbra"]})", winner == "zenith" ? "#5cc8ff" : "#ff3b5c");
            Payout(winner);
            if (wins[winner] >= ROUNDS_TO_WIN) { phase = "over"; w.End(winner); return; }
            // the next round: everyone back to spawn, fresh, shopping; ultimate charge carries over
            round++;
            phase = "armory"; phaseEnd = t + ARMORY_SECS;
            w.projs = new List<Proj>(); w.zones = new List<Zone>(); w.timers = new List<Timer>();
            foreach (var a in w.actors) { double ult = a.ult; w.Respawn(a, true); a.cd = new Dictionary<string, double>(); a.ult = ult; }
            w.Sfx("victory");
        }

        // ------------------------------------------------------------------------------------------------ AI shopping
        static readonly Dictionary<string, string[]> WANT = new Dictionary<string, string[]>
        {
            ["tank"] = new[] { "survival", "ability", "weapon" }, ["support"] = new[] { "ability", "survival", "weapon" }, ["dps"] = new[] { "weapon", "ability", "survival" },
        };

        /// <summary>bots: take a power when one is due, then buy the best item they can afford in their role's order (once a phase)</summary>
        public static void BotShop(Stadium s, Actor a)
        {
            if (s.PowerPending(a))
            {
                var P = PowersFor(a.baseDef); string role = a.baseDef.role;
                string[] order = role == "support" ? new[] { "_ult", "_guard", "_a2", "_a1", "_move", "_wpn" }
                    : role == "tank" ? new[] { "_guard", "_a1", "_ult", "_a2", "_move", "_wpn" }
                    : new[] { "_wpn", "_a1", "_ult", "_guard", "_a2", "_move" };
                var pick = order.Select(k => P.FirstOrDefault(p => p.id.EndsWith(k))).FirstOrDefault(p => p != null && !a.powers.Contains(p.id));
                if (pick != null) s.PickPower(a, pick.id);
            }
            if ((a.sv.TryGetValue("shoppedRound", out var sr) ? sr : 0) == s.round) return;
            a.sv["shoppedRound"] = s.round;
            var cats = WANT.TryGetValue(a.baseDef.role ?? "", out var c) ? c : WANT["dps"];
            for (int guard = 0; guard < 8; guard++)
            {
                if (a.items.Count >= MAX_ITEMS)
                {
                    // full: trade the cheapest item up when a pricier one is affordable
                    var cheapest = JsSort.SortBy(new List<string>(a.items), (x, y) => ITEM[x].cost - ITEM[y].cost)[0];
                    var up = JsSort.SortBy(ITEMS.Where(i => !a.items.Contains(i.id) && i.cost > ITEM[cheapest].cost * 2 && i.cost <= a.cash + ITEM[cheapest].cost && cats.Take(2).Contains(i.cat)).ToList(), (x, y) => y.cost - x.cost).FirstOrDefault();
                    if (up == null) break;
                    s.Sell(a, cheapest); if (!s.Buy(a, up.id)) break;
                    continue;
                }
                var pick = cats.SelectMany(ct => JsSort.SortBy(ITEMS.Where(i => i.cat == ct && !a.items.Contains(i.id) && i.cost <= a.cash).ToList(), (x, y) => y.cost - x.cost)).FirstOrDefault();
                if (pick == null || !s.Buy(a, pick.id)) break;
            }
        }
    }
}
