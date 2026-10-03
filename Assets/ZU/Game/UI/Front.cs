// The desktop app's front end: what the main menu hands to the match (MatchSettings) and the pause state (PauseMenu).
// The screens themselves are UI Toolkit (Toolkit/).
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ZU.Game.UI
{
    /// <summary>the next match, as chosen in the menu (MatchRunner reads it on Start)</summary>
    public static class MatchSettings
    {
        public static bool Pending;
        public static string Map = "hanabi", Hero = "kaien", Mode = "quickplay";
        public static float Skill = 0.6f;
        public static bool Third = false, Autopilot = false;
        /// <summary>competitive: the enemy lobby's rating the match was made at (the result moves your rank against it)</summary>
        public static double Opp = 1800;
        /// <summary>the campaign level last chosen (mode "campaign": the match's map id is the level id)</summary>
        public static string Level = "c1_shipyard";
        public const string MenuScene = "Menu", MatchScene = "Match";

        public static void Start(string map, string hero, string mode, float skill, bool third)
        {
            Map = map; Hero = hero; Mode = mode; Skill = skill; Third = third; Autopilot = false; Pending = true;
            Time.timeScale = 1;
            // the loading screen, the scene loaded behind it, the match warmed up before it lifts
            Toolkit.LoadingView.LoadMatch();
        }
        public static void BackToMenu() { Time.timeScale = 1; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; SceneManager.LoadScene(MenuScene); }
    }

    /// <summary>Esc pauses the single-player simulation, and a decided match pauses on its result. This is the state -
    /// paused or not, the cursor, the career record of the finished match; the pause, results and Options screens over it
    /// are UI Toolkit (Toolkit/PauseView.cs, the TS Menu.pause / results / queueResults)</summary>
    public static class PauseMenu
    {
        public static bool Paused { get; private set; }
        static bool recorded;
        static float overAt = -1;
        /// <summary>the finished match's competitive rank change (null in every other mode) and the role it moved</summary>
        public static Career.RankChange LastChange { get; private set; }
        public static string LastRole { get; private set; }
        /// <summary>a screen open over the pause (Options, a key being rebound) takes Esc for itself while this says so</summary>
        public static System.Func<bool> EscTaken;

        public static void Update(MatchRunner r)
        {
            var kb = Keyboard.current;
            bool over = r.World != null && !string.IsNullOrEmpty(r.World.winner);
            if (over && !recorded) { recorded = true; Record(r); overAt = Time.unscaledTime; }
            // the end plays out before the result screen (the TS waits 3.5 s, 2.5 s in the campaign)
            if (over && !Paused && r.mode != "aitest" && Time.unscaledTime - overAt >= (r.mode == "campaign" ? 2.5f : 3.5f)) Set(true);
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !over && !(EscTaken?.Invoke() ?? false)) Set(!Paused);
        }

        public static void Resume() => Set(false);
        public static void Pause() => Set(true);

        static void Set(bool p)
        {
            Paused = p;
            // online the world keeps running under the menu (TS: a paused player just stands still); MatchRunner holds their input
            if (ZU.Net.NetMatch.Current == null) Time.timeScale = p ? 0 : 1;
            Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = p;
        }
        public static void Reset() { Paused = false; Time.timeScale = 1; recorded = false; overAt = -1; LastChange = null; LastRole = null; }

        /// <summary>the finished match into the career (TS Menu.queueResults): competitive moves the role's rank, a close
        /// match softening a loss; quick play moves the hidden rating; both join the match history</summary>
        static void Record(MatchRunner r)
        {
            var me = r.Player; var w = r.World;
            // (an online match moves the online ranks, on its own result screen - OnlineView.Results)
            if (me == null || ZU.Net.NetMatch.Current != null || Toolkit.UiTour.Active || (r.mode != "competitive" && r.mode != "quickplay")) return;
            string us = me.team, them = us == "zenith" ? "umbra" : "zenith";
            bool won = w.winner == us;
            string score = w.rules == "push" ? $"{System.Math.Round(w.push.best[us])}m - {System.Math.Round(w.push.best[them])}m" : $"{w.control.wins[us]} - {w.control.wins[them]}";
            bool close = w.rules == "push" ? w.time > w.timeLimit || System.Math.Abs(w.push.best["zenith"] - w.push.best["umbra"]) < 10 : w.control.round >= 3 || w.control.overtime;
            var c = Career.Ranks.Load();
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (r.mode == "competitive")
            {
                string role = Career.Ranks.ROLE_OF.TryGetValue(me.baseDef.role ?? "dps", out var rr) ? rr : "damage";
                var ch = Career.Ranks.ApplyCompetitive(c.roles[role], won, MatchSettings.Opp, close);
                c.roles[role] = ch.after;
                c.best[role] = System.Math.Max(c.best.TryGetValue(role, out var b) ? b : 0, ch.after.rating);
                c.history.Add(new Career.MatchLog { at = now, mode = "competitive", role = role, map = r.mapId, hero = me.baseDef.id, won = won, delta = ch.delta, mods = ch.mods, score = score });
                LastChange = ch; LastRole = role;
            }
            else
            {
                Career.Ranks.ApplyQuickPlay(c, won, MatchSettings.Opp);
                c.history.Add(new Career.MatchLog { at = now, mode = "quickplay", map = r.mapId, hero = me.baseDef.id, won = won, score = score });
            }
            Career.Ranks.Save(c);
        }
    }
}
