// TEMPORARY: the cross-module contract for modules not ported yet. Each stub has the final C# signature of its TS
// function; when the real port lands (Weapons.cs, Abilities*.cs, Puppets.cs, Susanoo.cs, Stadium.cs) its stub is
// deleted from here. Nothing here may stay once the port is done (tools/simtest checks: `grep PENDING` must be empty).
using System;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static partial class Abilities
    {
        public const double TIDE_HOLD = 0.15, TIDE_MARK_AMP = 1.2;   // PENDING: abilities.ts
        public static bool CastAbility(World w, Actor a, string id, string slot) => false;
        public static void StompLeap(World w, Actor a) { }
        public static void TickAbilities(World w, double dt) { }
        /// <summary>TS castAbility.onProj: a `special` projectile ended (hit or expired)</summary>
        public static void OnProj(World w, Proj p, V3 at, Actor hit) { }
    }

    public static class Weapons
    {
        public static void UpdateWeapons(World w, Actor a, double dt) { }   // PENDING: weapons.ts
    }

    public static class Puppets
    {
        public static readonly HeroDef PUPPET_DEF = new HeroDef { id = "puppet", name = "Puppet", team = "umbra", role = "dps", frame = "human", hp = 50, speed = 5, height = 1.6, radius = 0.4, summoned = true, primary = new SlotDef { kind = "melee", damage = 10, rate = 1, range = 1 }, ult = new SlotDef { id = "none", charge = 1e9 } };   // PENDING: puppets.ts
        public static void DropPuppets(World w, Actor owner) { }
        public static void TickPuppets(World w, double dt) { }
    }

    public static class Susanoo
    {
        public static readonly HeroDef SUSANOO_DEF = new HeroDef { id = "susanoo", name = "Susanoo", team = "zenith", role = "tank", frame = "human", hp = 1, speed = 5, height = 5.4, radius = 1, summoned = true, primary = new SlotDef { kind = "melee", damage = 0, rate = 1, range = 1 }, ult = new SlotDef { id = "none", charge = 1e9 } };   // PENDING: susanoo.ts
        public static void DismissSusanoo(World w, Actor owner) { }
        public static void TickSusanoo(World w) { }
    }

    public class Stadium
    {
        public bool frozen;                               // PENDING: stadium.ts
        public void Update(double dt) { }
    }
}
