// Start-up timing in the log: "[ZU] t=<seconds since the player started> <what>" at the steps that decide how long the
// menu takes to come up, so a slow start shows which step it is (Player.log in a build, the Console in the Editor).
using UnityEngine;

namespace ZU.Game
{
    public static class StartupClock
    {
        public static void Mark(string what) => Debug.Log($"[ZU] t={Time.realtimeSinceStartup:0.00}s {what}");
    }
}
