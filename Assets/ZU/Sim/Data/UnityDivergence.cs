// The Unity edition's deliberate differences from the TS game data. tools/export writes heroes.json from the TS (the
// source of truth), so a Unity-only change is applied here on load, never edited into the JSON. Each one is the user's
// call; tools/parity/check.py lists them as intentional, not as gaps.
namespace ZU.Sim.Data
{
    public static class UnityDivergence
    {
        public static void Apply(GameData d)
        {
            // Yuzu's Hundred Suns (the user's rework, 2026-10-03): five giant arrows, then a 15 s swarm - Abilities.Hundredsuns
            if (d.Hero != null && d.Hero.TryGetValue("yuzu", out var yuzu) && yuzu.ult != null)
            {
                yuzu.ult.charge = 2400;
                yuzu.ult.desc = "Five giant sword-arrows of sunlight slam down in a ring at the target point (100 damage where each lands), " +
                                "then shatter into a thousand small arrows that hunt every enemy within 30m for 15s (24 damage a second).";
            }
        }
    }
}
