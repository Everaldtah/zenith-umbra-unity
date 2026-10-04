// The loading screen and the match preloader (Menu.launch's loading screen + src/client/Preload.ts): the map's art,
// name and story, a spinner and the controls line; a progress bar under the title walks the phases - the scene and
// its assets loading, the shaders warmed against the match's own lights, then full frames through the real pipeline
// behind the screen - so nothing pops in or hitches when it lifts. The screen lives on the persistent panel's overlay
// layer, so it stays up across the Menu -> Match scene switch.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using ZU.Game.UI;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public static class LoadingView
    {
        const string TIPS = "WASD move · SPACE jump / fly · LMB fire · RMB secondary · C melee · F swoop (Mirei) · SHIFT / E abilities · Q ultimate · R reload · CTRL descend · V first/third person · TAB scoreboard";
        static VisualElement root, bg, fillBar, spin;
        static Label barLabel;
        static float shownAt;
        /// <summary>the next match's tips line instead of the controls (an online match: players / AI / hosting)</summary>
        public static string NextTips;
        static bool loading;
        public static bool Open => root != null && root.parent != null;

        /// <summary>the loading screen (Menu.ts .loading): background art, a title, a line, the spinner, tips</summary>
        public static void Show(string img, string title, string line, string tips, bool spinner = true, float bgOpacity = 1)
        {
            Close();
            var ui = UiRoot.Get();
            root = U.Div("loading", ui.OverlayLayer, pick: true);
            bg = U.Div("bg", root); U.Bg(bg, img);
            bg.style.opacity = bgOpacity;
            Filters.Set(bg, Filters.Brightness(0.55f));
            U.Txt(title, "ld-h2", root);
            if (!string.IsNullOrEmpty(line)) U.Txt(line, "ld-p", root);
            if (spinner) spin = U.Div("spin", root);
            var bar = U.Div("preload", root);
            var track = U.Div("pl-track", bar); fillBar = U.Div("pl-fill", track);
            Grad.Set(fillBar, Grad.Linear(90, (Grad.C("#39d6ff"), 0), (Grad.C("#ffd23f"), 100)));
            barLabel = U.Txt("", "pl-label", bar);
            U.Show(bar, false);
            if (!string.IsNullOrEmpty(tips)) U.Txt(tips, "tips", root);
            shownAt = Time.unscaledTime;
            ui.Tick -= Animate; ui.Tick += Animate;
        }

        /// <summary>Preload.showProgress: a phase label and the overall fraction</summary>
        public static void Progress(string label, float frac)
        {
            if (fillBar == null) return;
            U.Show(fillBar.parent.parent, true);
            fillBar.style.width = new Length(Mathf.Round(Mathf.Clamp01(frac) * 100), LengthUnit.Percent);
            U.Set(barLabel, $"{label.ToUpperInvariant()} · {Mathf.Round(frac * 100)}%");
        }

        public static void Close()
        {
            if (root == null) return;
            root.RemoveFromHierarchy(); root = null; bg = null; fillBar = null; spin = null; barLabel = null;
            UiRoot.Get().Tick -= Animate;
        }

        static void Animate()
        {
            if (root == null) return;
            float t = Time.unscaledTime - shownAt;
            // @keyframes kb (30s ease-in-out infinite alternate): scale 1.05 -> 1.18, drifting -2% / -1%
            if (bg != null)
            {
                float k = Mathf.PingPong(t / 30f, 1); k = k * k * (3 - 2 * k);
                float s = Mathf.Lerp(1.05f, 1.18f, k);
                bg.style.scale = new Scale(new Vector3(s, s, 1));
                bg.style.translate = new Translate(new Length(-2 * k, LengthUnit.Percent), new Length(-1 * k, LengthUnit.Percent));
            }
            // .spin: one turn a second
            if (spin != null) spin.style.rotate = new Rotate(new Angle((t * 360) % 360, AngleUnit.Degree));
        }

        // ------------------------------------------------------------------ match loading
        /// <summary>the loading screen for the match MatchSettings describes, then the Match scene loaded behind it</summary>
        public static void LoadMatch()
        {
            if (loading) return;
            var d = ZuData.Get();
            string map = MatchSettings.Map;
            if (MatchSettings.Mode == "campaign" && CampaignLevel.All(d).TryGetValue(map, out var lv))
                Show("map_" + map, lv.name, d.Boss.TryGetValue(lv.boss ?? "", out var b) ? $"Boss: {b.name} - {b.title}" : "",
                    "Clear the robot waves on each platform, use the jump pads to advance, then bring down the Colossus. Red circles and lines are incoming attacks - move or jump.");
            else
            {
                d.Map.TryGetValue(map, out var m);
                // the training map is the Training Grounds to the player (its data name is the academy's "Proving Grounds")
                string title = map == "training" ? "Training Grounds" : m?.name ?? map;
                Show("map_" + map, title, m?.story ?? "", NextTips ?? TIPS);
                NextTips = null;
            }
            UiRoot.Get().StartCoroutine(Load());
        }

        static IEnumerator Load()
        {
            loading = true;
            yield return null;                                   // one frame so the screen is on before the load blocks
            var op = SceneManager.LoadSceneAsync(MatchSettings.MatchScene);
            if (op == null) { loading = false; Close(); yield break; }
            while (!op.isDone) { Progress("Loading map", op.progress * 0.6f); yield return null; }
            loading = false;
            // the match's Start has built the world; MatchUi.Attach calls Warm() for the rest
        }

        /// <summary>the match is built: draw full frames behind the screen (they compile the variants actually drawn, like the
        /// TS renderer.compile of the scene), then lift it. No Shader.WarmupAllShaders(): warming every loaded shader tripped
        /// URP's fallback keyword spaces (76 "keyword state size mismatch" errors, evera-23) and stalled an online host.</summary>
        public static IEnumerator Warm()
        {
            if (!Open) yield break;
            // the simulation holds still behind the screen (MatchRunner steps on scaled time) - not online, where the host's
            // world and the snapshots it streams keep going
            bool online = ZU.Net.NetMatch.Current != null;
            if (!online) Time.timeScale = 0;
            Progress("Compiling shaders", 0.65f);
            yield return null;
            const int FRAMES = 24;
            for (int i = 0; i < FRAMES; i++) { Progress("Warm-up", 0.7f + 0.3f * i / FRAMES); yield return null; }
            Progress("Ready", 1);
            // (the TS closes 600 ms after the match starts)
            yield return new WaitForSecondsRealtime(0.25f);
            if (!PauseMenu.Paused && !online) Time.timeScale = 1;
            Close();
        }
    }
}
