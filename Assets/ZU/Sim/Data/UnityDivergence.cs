// The Unity edition's deliberate differences from the TS game data. tools/export writes heroes.json from the TS (the
// source of truth), so a Unity-only change is applied here on load, never edited into the JSON. Each one is the user's
// call; tools/parity/check.py lists them as intentional, not as gaps.
namespace ZU.Sim.Data
{
    public static class UnityDivergence
    {
        /// <summary>the user, 2026-10-04: Yuzu twice as strong (asked as x5, then x3, then - shown she still won nine games in
        /// ten - "put yuzu at 2x"): the damage of her Dawnshot and of Hundred Suns (the landings and the swarm) times this.
        /// Pinned: the balance lab never moves her damage; she is one of the four 60 % heroes through her toughness.</summary>
        public static double YuzuPower = 2;

        public static void Apply(GameData d)
        {
            if (d.Hero != null && d.Hero.TryGetValue("yuzu", out var yuzu) && yuzu.ult != null)
            {
                if (yuzu.primary != null) yuzu.primary.damage *= YuzuPower;
                // Yuzu's Hundred Suns (the user's rework, 2026-10-03): five giant arrows, then a 15 s swarm - Abilities.Hundredsuns
                yuzu.ult.charge = 2400;
                yuzu.ult.desc = $"Five giant sword-arrows of sunlight slam down in a ring at the target point ({Abilities.SunsLandDmg:0} damage where each lands), " +
                                $"then shatter into a thousand small arrows that hunt every enemy within 30m for 15s ({Abilities.SunsDmg / Abilities.SUNS_TICK:0} damage a second).";
            }
        }
    }
}
