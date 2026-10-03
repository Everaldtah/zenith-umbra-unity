// QA / dev start-up flags: the player jumps past the menu straight into a match.
//   "Zenith Umbra Unity.exe" --zu-map hanabi --zu-hero tenkai --zu-mode quickplay [--zu-third] [--zu-autopilot] [--zu-skill 0.6]
//   --zu-level c1_shipyard (with --zu-mode campaign)
//   --zu-mode ultviewer --zu-hero hayate      (the Ult Viewer: the hero's ult on the gallery bench, as the menu opens it)
// Modes are MatchRunner's (quickplay, competitive, skirmish, practice, aitest, training, spectate, stadium, campaign...).
// Each match marks "[ZU] t=... match live: ..." in the log on the first frame it draws (StartupClock).
// Read once per run; going back to the menu afterwards behaves normally.
using System;
using ZU.Game.UI;

namespace ZU.Game
{
    public static class DevArgs
    {
        static bool used;

        static string Arg(string[] a, string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        /// <summary>called by the menu's Start: true when the flags started a match (the menu shouldn't build)</summary>
        public static bool TryStartMatch()
        {
            if (used) return false;
            used = true;
            var a = Environment.GetCommandLineArgs();
            string map = Arg(a, "--zu-map"), mode = Arg(a, "--zu-mode");
            if (map == null && mode == null) return false;
            string hero = Arg(a, "--zu-hero") ?? "raijin";
            float skill = float.TryParse(Arg(a, "--zu-skill"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0.6f;
            if (mode == "ultviewer")
            {
                StartupClock.Mark($"dev args: ult viewer for {hero}");
                Fx.UltShowcase.Open(hero);
                return true;
            }
            var level = Arg(a, "--zu-level");
            if (level != null) MatchSettings.Level = level;
            StartupClock.Mark($"dev args: {mode ?? "quickplay"} on {map ?? level} as {hero}");
            MatchSettings.Start(map ?? level ?? "hanabi", hero, mode ?? "quickplay", skill, Array.IndexOf(a, "--zu-third") >= 0);
            MatchSettings.Autopilot = Array.IndexOf(a, "--zu-autopilot") >= 0;
            return true;
        }
    }
}
