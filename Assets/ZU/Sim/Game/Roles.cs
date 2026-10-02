// Role and sub-role passives (port of zenith-umbra src/game/roles.ts) - the Overwatch (2026) framework applied to
// ZENITH//UMBRA. See that repo's docs/research/ow_balance_study.md for where every number comes from.
using System.Collections.Generic;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static class Roles
    {
        /// <summary>everyone: health regenerates once no damage has landed for REGEN_DELAY s</summary>
        public const double REGEN_RATE = 20, REGEN_DELAY = 5;
        /// <summary>Enra's Oni Blood: his regeneration starts sooner</summary>
        public const double ONI_REGEN_DELAY = 2.5;
        /// <summary>tanks: ultimate charge from their own damage and healing x0.6</summary>
        public const double TANK_ULT_GEN = 0.6;
        /// <summary>anyone: damage into / healing on a tank gives x0.6 the ultimate charge</summary>
        public const double VS_TANK_ULT = 0.6;
        /// <summary>damage absorbed by temporary health gives half the ultimate charge</summary>
        public const double OVERHEALTH_ULT = 0.5;
        /// <summary>damage heroes: whoever they hit receives HEALCUT less healing for HEALCUT_SECS</summary>
        public const double HEALCUT = 0.15, HEALCUT_SECS = 2;
        /// <summary>the most any mix of armor and damage reduction can take off a hit</summary>
        public const double MITIGATION_CAP = 0.5;
        // sub-roles
        public const double STALWART_KNOCK = 0.6, STALWART_SLOW = 0.6;
        public const double BRUISER_CRIT = 0.75, BRUISER_SPEED = 1.15;
        public const double INITIATOR_HEAL = 40, INITIATOR_CD = 4;
        public const double FLANKER_PACK = 50;
        public const double SHARPSHOOTER_CD = 0.01;
        public const double RECON_REVEAL = 3.5;
        public const double SPECIALIST_RELOAD = 0.5, SPECIALIST_SECS = 3;
        public const double MEDIC_SELF = 0.3;
        public const double TACTICIAN_BANK = 0.25, TACTICIAN_RATE = 0.75;

        public static readonly Dictionary<string, (string name, string desc)> ROLE_PASSIVE = new Dictionary<string, (string, string)>
        {
            ["tank"] = ("Tank", "Your damage and healing build ultimate 40% slower, and damage into you charges the enemy's ultimates 40% slower. Knockback and critical-hit resistance now come from your sub-role."),
            ["dps"] = ("Damage", $"Everyone you hit receives {JsMath.Round(HEALCUT * 100)}% less healing for {HEALCUT_SECS}s."),
            ["support"] = ("Support", "Your healing builds ultimate at full rate (tanks you heal: 60%). Your sub-role gives you your own sustain."),
        };
        public static readonly Dictionary<string, (string name, string desc)> SUBROLE = new Dictionary<string, (string, string)>
        {
            ["stalwart"] = ("Stalwart", "Knockback and slows against you are 40% weaker."),
            ["bruiser"] = ("Bruiser", "Critical hits against you deal 25% less. Move 15% faster while below half health."),
            ["initiator"] = ("Initiator", $"Using a movement ability heals {INITIATOR_HEAL} over 1s (every {INITIATOR_CD}s)."),
            ["flanker"] = ("Flanker", $"Health packs restore {FLANKER_PACK} more."),
            ["sharpshooter"] = ("Sharpshooter", "Critical hits shorten your movement ability’s cooldown (1s per 100 damage)."),
            ["recon"] = ("Recon", $"Damaging an enemy below half health reveals them for {RECON_REVEAL}s."),
            ["specialist"] = ("Specialist", $"Eliminations make you reload 50% faster for {SPECIALIST_SECS}s."),
            ["medic"] = ("Medic", $"Healing allies with your weapon heals you for {JsMath.Round(MEDIC_SELF * 100)}% of it."),
            ["survivor"] = ("Survivor", "Movement abilities start your health regeneration at once."),
            ["tactician"] = ("Tactician", $"Ultimate charge past full is banked: up to {JsMath.Round(TACTICIAN_BANK * 100)}% of your next ultimate."),
        };

        public static string SubroleOf(HeroDef d) => d.subrole;
        public static bool IsSub(Actor a, string s) => a.def.subrole == s;
        /// <summary>the hero's movement ability (what a sharpshooter's crits refund, what starts a survivor's regen)</summary>
        public static readonly Dictionary<string, string> MOVE_ABILITY = new Dictionary<string, string>
        {
            ["yuzu"] = "sunhop", ["seiran"] = "riverstep", ["raijin"] = "flashstep", ["hayate"] = "currentdash", ["kagemaru"] = "shadowstep",
            ["kaien"] = "spiritstep", ["enra"] = "chain", ["tenkai"] = "dawncharge", ["gorgoth"] = "abysscharge", ["gantetsu"] = "tachiai", ["haruto"] = "pilotroll",
        };
    }
}
