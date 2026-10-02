// The desktop app's front end (IMGUI, styled): what the main menu hands to the match (MatchSettings), the shared look
// (UiStyle), and the pause / end-of-match overlay the match draws. The main menu itself is MainMenu.cs.
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
            SceneManager.LoadScene(MatchScene);
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

    /// <summary>Esc: pause (the single-player simulation stops) with resume / main menu / quit; the result screen when the
    /// match is decided</summary>
    public static class PauseMenu
    {
        public static bool Paused { get; private set; }
        static bool recorded;
        static string resultLine;

        public static void Update(MatchRunner r)
        {
            var kb = Keyboard.current;
            bool over = r.World != null && !string.IsNullOrEmpty(r.World.winner);
            if (over && !recorded) { recorded = true; resultLine = Record(r); }
            if (over && !Paused) Set(true);
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !over) Set(!Paused);
        }

        static void Set(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0 : 1;
            Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = p;
        }
        public static void Reset() { Paused = false; Time.timeScale = 1; recorded = false; resultLine = null; }

        /// <summary>the finished match into the career: competitive moves the role's rank (with the reasons), quick play the
        /// hidden rating; returns the line the result screen shows</summary>
        static string Record(MatchRunner r)
        {
            var me = r.Player;
            if (me == null || (r.mode != "competitive" && r.mode != "quickplay")) return null;
            bool won = r.World.winner == me.team;
            var c = Career.Ranks.Load();
            string line = null;
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (r.mode == "competitive")
            {
                string role = Career.Ranks.ROLE_OF.TryGetValue(me.baseDef.role ?? "dps", out var rr) ? rr : "damage";
                var ch = Career.Ranks.ApplyCompetitive(c.roles[role], won, MatchSettings.Opp);
                c.roles[role] = ch.after;
                var v = Career.Ranks.RankOf(ch.after.rating, ch.after.games);
                if (v.placed && (!c.best.TryGetValue(role, out var b) || ch.after.rating > b)) c.best[role] = ch.after.rating;
                string sign = ch.delta >= 0 ? "+" : "";
                string flag = ch.promoted ? "  PROMOTED" : ch.demoted ? "  DEMOTED" : ch.placedNow ? "  PLACED" : "";
                line = role.ToUpperInvariant() + "  " + v.label + (v.placed ? "  " + v.pct.ToString("0") + "%  (" + sign + ch.delta.ToString("0") + ")" : "") + flag
                       + (ch.mods.Count > 0 ? "\n" + string.Join("  ·  ", ch.mods) : "");
                c.history.Add(new Career.MatchLog { at = now, mode = "competitive", role = role, map = r.mapId, hero = me.def.id, won = won, delta = ch.delta, mods = ch.mods, score = r.World.winner });
            }
            else
            {
                Career.Ranks.ApplyQuickPlay(c, won, MatchSettings.Opp);
                c.history.Add(new Career.MatchLog { at = now, mode = "quickplay", map = r.mapId, hero = me.def.id, won = won, score = r.World.winner });
            }
            Career.Ranks.Save(c);
            return line;
        }

        public static void Draw(MatchRunner r)
        {
            if (!Paused) return;
            var cv = UiStyle.Canvas();
            float s = 1, W = cv.x, H = cv.y;
            UiStyle.Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.45f));
            var panel = new Rect(W / 2 - 230 * s, H / 2 - 210 * s, 460 * s, 420 * s);
            UiStyle.Panel_(panel);
            string winner = r.World?.winner;
            bool over = !string.IsNullOrEmpty(winner);
            string head = !over ? "PAUSED" : r.Player != null ? (winner == r.Player.team ? "VICTORY" : "DEFEAT") : winner.ToUpperInvariant() + " WINS";
            var hs = new GUIStyle(UiStyle.H1) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(44 * s) };
            if (over) hs.normal.textColor = r.Player == null || winner == r.Player.team ? UiStyle.Zenith : UiStyle.Umbra;
            GUI.Label(new Rect(panel.x, panel.y + 24 * s, panel.width, 60 * s), head, hs);
            if (over && !string.IsNullOrEmpty(resultLine))
                GUI.Label(new Rect(panel.x + 20, panel.y + 80 * s, panel.width - 40, 40 * s), resultLine, new GUIStyle(UiStyle.Small) { alignment = TextAnchor.UpperCenter, wordWrap = true });
            float y = panel.y + 120 * s, bw = panel.width - 80 * s, bh = 58 * s;
            var c = new GUIStyle(UiStyle.Button) { alignment = TextAnchor.MiddleCenter };
            if (!over && GUI.Button(new Rect(panel.x + 40 * s, y, bw, bh), "RESUME", c)) Set(false);
            y += bh + 14 * s;
            if (over && GUI.Button(new Rect(panel.x + 40 * s, y - bh - 14 * s, bw, bh), "PLAY AGAIN", c))
            { Reset(); MatchSettings.Start(r.mapId, r.playerHero, r.mode, r.botSkill, r.thirdPerson); }
            if (GUI.Button(new Rect(panel.x + 40 * s, y, bw, bh), "MAIN MENU", c)) { Reset(); MatchSettings.BackToMenu(); }
            y += bh + 14 * s;
            if (GUI.Button(new Rect(panel.x + 40 * s, y, bw, bh), "QUIT TO DESKTOP", c)) Application.Quit();
            GUI.Label(new Rect(panel.x, panel.y + panel.height - 40 * s, panel.width, 30 * s), "Esc resumes  ·  " + (r.World?.map?.name ?? ""), new GUIStyle(UiStyle.Small) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
