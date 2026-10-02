// The desktop app's front end: what the main menu hands to the match (MatchSettings), the IMGUI look the remaining IMGUI
// screens share (UiStyle), and the pause state (PauseMenu). The screens themselves are UI Toolkit (Toolkit/).
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

    /// <summary>ZENITH//UMBRA's menu look: dark glass panels, the zenith cyan, big condensed type</summary>
    public static class UiStyle
    {
        public static readonly Color Zenith = new Color(0.36f, 0.78f, 1f), Umbra = new Color(1f, 0.23f, 0.36f), Panel = new Color(0.04f, 0.05f, 0.09f, 0.78f), Dim = new Color(0.7f, 0.74f, 0.82f);
        public static GUIStyle Title, H1, H2, Body, Small, Button, ButtonOn, Big, Center;
        static float scale;

        /// <summary>lay the UI out on a 1080-pixel-tall virtual canvas: one GUI.matrix scale, so the layout is the same at
        /// every resolution; returns the canvas size</summary>
        public static Vector2 Canvas()
        {
            float k = Mathf.Max(0.1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(k, k, 1));
            Ensure();
            return new Vector2(Screen.width / k, 1080f);
        }

        public static void Ensure()
        {
            const float s = 1f;
            if (Title != null) return;
            scale = s;
            int F(float px) => Mathf.RoundToInt(px * s);
            Title = new GUIStyle(GUI.skin.label) { fontSize = F(54), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            H1 = new GUIStyle(GUI.skin.label) { fontSize = F(30), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            H2 = new GUIStyle(GUI.skin.label) { fontSize = F(19), fontStyle = FontStyle.Bold, normal = { textColor = Zenith } };
            Body = new GUIStyle(GUI.skin.label) { fontSize = F(16), wordWrap = true, normal = { textColor = new Color(0.9f, 0.92f, 0.96f) } };
            Small = new GUIStyle(Body) { fontSize = F(13), normal = { textColor = Dim } };
            Center = new GUIStyle(Body) { alignment = TextAnchor.MiddleCenter };
            Button = new GUIStyle(GUI.skin.button) { fontSize = F(17), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(F(14), F(10), F(6), F(6)) };
            Button.normal.background = Tex(new Color(1, 1, 1, 0.06f)); Button.hover.background = Tex(new Color(0.36f, 0.78f, 1f, 0.22f)); Button.active.background = Tex(new Color(0.36f, 0.78f, 1f, 0.4f));
            Button.normal.textColor = Button.hover.textColor = Button.active.textColor = Color.white;
            ButtonOn = new GUIStyle(Button); ButtonOn.normal.background = ButtonOn.hover.background = Tex(new Color(0.36f, 0.78f, 1f, 0.45f));
            Big = new GUIStyle(Button) { fontSize = F(30), alignment = TextAnchor.MiddleCenter };
            Big.normal.background = Tex(new Color(1f, 0.62f, 0.16f, 0.92f)); Big.hover.background = Tex(new Color(1f, 0.74f, 0.3f, 1f)); Big.active.background = Tex(new Color(0.9f, 0.5f, 0.1f, 1f));
            Big.normal.textColor = Big.hover.textColor = Big.active.textColor = new Color(0.08f, 0.06f, 0.04f);
        }
        public static float S => scale <= 0 ? 1 : scale;
        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave }; t.SetPixel(0, 0, c); t.Apply(); return t; }
        public static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        public static void Panel_(Rect r) { Box(r, Panel); Box(new Rect(r.x, r.y, r.width, 2 * S), new Color(Zenith.r, Zenith.g, Zenith.b, 0.8f)); }
    }

    /// <summary>Esc pauses the single-player simulation, and a decided match pauses on its result. This is the state -
    /// paused or not, the cursor, the career record of the finished match; the pause, results and Options screens over it
    /// are UI Toolkit (Toolkit/PauseView.cs, the TS Menu.pause / results / queueResults)</summary>
    public static class PauseMenu
    {
        public static bool Paused { get; private set; }
        static bool recorded;
        /// <summary>the finished match's competitive rank change (null in every other mode) and the role it moved</summary>
        public static Career.RankChange LastChange { get; private set; }
        public static string LastRole { get; private set; }
        /// <summary>a screen open over the pause (Options, a key being rebound) takes Esc for itself while this says so</summary>
        public static System.Func<bool> EscTaken;

        public static void Update(MatchRunner r)
        {
            var kb = Keyboard.current;
            bool over = r.World != null && !string.IsNullOrEmpty(r.World.winner);
            if (over && !recorded) { recorded = true; Record(r); }
            if (over && !Paused) Set(true);
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !over && !(EscTaken?.Invoke() ?? false)) Set(!Paused);
        }

        public static void Resume() => Set(false);
        public static void Pause() => Set(true);

        static void Set(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0 : 1;
            Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = p;
        }
        public static void Reset() { Paused = false; Time.timeScale = 1; recorded = false; LastChange = null; LastRole = null; }

        /// <summary>the finished match into the career (TS Menu.queueResults): competitive moves the role's rank, a close
        /// match softening a loss; quick play moves the hidden rating; both join the match history</summary>
        static void Record(MatchRunner r)
        {
            var me = r.Player; var w = r.World;
            if (me == null || (r.mode != "competitive" && r.mode != "quickplay")) return;
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
