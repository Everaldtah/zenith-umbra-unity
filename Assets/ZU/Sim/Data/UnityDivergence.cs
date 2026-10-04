// The Unity edition's deliberate differences from the TS game data. tools/export writes heroes.json from the TS (the
// source of truth), so a Unity-only change is applied here on load, never edited into the JSON. Each one is the user's
// call; tools/parity/check.py lists them as intentional, not as gaps.
namespace ZU.Sim.Data
{
    public static class UnityDivergence
    {
        /// <summary>the user, 2026-10-04: "make yuzu 5 times stronger" - the damage of her Dawnshot and of Hundred Suns (the
        /// landings and the swarm) times this. Pinned: the balance lab never tunes her (it sets this to 1 before the data
        /// loads to calibrate the others against her original numbers).</summary>
        public static double YuzuPower = 5;

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
