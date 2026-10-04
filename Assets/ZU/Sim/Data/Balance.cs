// The balance table: one power scalar per hero, applied to all the damage it deals and all the healing it does (its
// summons' too). The scalars are found by the balance lab (tools/simtest/BalanceLab.cs: mixed-faction, role-locked,
// side-swapped bot matches on every map; each hero's scalar is moved until its win rate sits at 50%) and shipped in the
// generated BalanceTable.cs, so the game, the headless tools and both ends of an online match use the same numbers.
// A hero the user has set by hand (UnityDivergence) is pinned: the lab measures it and leaves it alone. docs/BALANCE.md.
using System.Collections.Generic;

namespace ZU.Sim.Data
{
    public static partial class Balance
    {
        static Dictionary<string, double> table;
        /// <summary>the scalars in force (hero id -> power); the lab swaps this while it tunes</summary>
        public static Dictionary<string, double> Table
        {
            get { if (table == null) { table = new Dictionary<string, double>(); foreach (var (id, p) in SHIPPED) table[id] = p; } return table; }
            set { table = value; }
        }
        static bool? enabled;
        /// <summary>the table's switch: on by default; `-zu-balance=0` on the command line (the player, the Editor, the headless
        /// tools) turns it off - every scalar reads 1, and the simulation is bit for bit what it was before the table</summary>
        public static bool Enabled
        {
            get { enabled ??= System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-zu-balance=0") < 0; return enabled.Value; }
            set { enabled = value; }
        }
        /// <summary>the scalar on everything this actor (or its summoner) deals or heals</summary>
        public static double Power(Actor a)
        {
            if (!Enabled) return 1;
            var h = a.owner ?? a;
            return h.baseDef != null && Table.TryGetValue(h.baseDef.id, out var p) ? p : 1;
        }
    }
}
