// The UI audit tour: `ZenithUmbraUnity.exe -zu-tour <folder>` walks every front-end screen in the order of the web
// game's tests/e2e audit tour - title, the queues, hero select, maps, the campaign menu, the Career Profile's tabs,
// every Options tab, the Hero Viewer, the online lobby, a match's loading screen / HUD / Tab screen / pause / result,
// the Training Grounds, Stadium's Armory, the Ult Viewer - saving <folder>/<step>.png at each, then quits. The two sets
// of screenshots compare one to one. While it runs nothing is written to the player's career or settings.
using System.Collections;
using System.IO;
using UnityEngine;
using ZU.Game.UI;

namespace ZU.Game.UI.Toolkit
{
    public sealed class UiTour : MonoBehaviour
    {
        /// <summary>a tour is running: the career and the settings stay untouched</summary>
        public static bool Active { get; private set; }
        /// <summary>the Tab screen held from the tour (no key to press)</summary>
        public static bool ForceBoard;
        string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-zu-tour")
                {
                    var go = new GameObject("ZU UI Tour"); DontDestroyOnLoad(go);
                    var t = go.AddComponent<UiTour>(); t.dir = args[i + 1];
                    Directory.CreateDirectory(t.dir);
                    Active = true;
                    ZuSettings.TourDefaults();
                    return;
                }
        }

        IEnumerator Shot(string name, float settle = 0.6f)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            Debug.Log("[ZU tour] shot " + name);
            yield return null; yield return null;
        }

        static MenuView Menu => MenuView.Current;
        static IEnumerator Until(System.Func<bool> ok, float secs = 60)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!ok() && Time.realtimeSinceStartup - t0 < secs) yield return null;
        }
        static MatchRunner Runner => FindAnyObjectByType<MatchRunner>();

        IEnumerator Start()
        {
            yield return Until(() => Menu != null);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("01_title");
            Menu.QueueSelect("quickplay"); yield return Shot("02_queue_quickplay");
            Menu.QueueSelect("competitive"); yield return Shot("03_queue_competitive");
            Menu.QueueSelect("practice"); yield return Shot("04_queue_practice", 0.9f);
            MenuState.Queue = "quickplay"; MenuState.Role = "flex"; MatchSettings.Hero = "raijin"; Menu.HeroSelect(); yield return Shot("05_heroselect_queue", 0.9f);
            MenuState.Queue = null; Menu.ModeName = "skirmish"; MatchSettings.Hero = "tenkai"; Menu.HeroSelect(); yield return Shot("06_heroselect_skirmish", 0.9f);
            Menu.ModeName = "spectate"; Menu.MapSelect(); yield return Shot("07_mapselect", 0.9f);
            Menu.Campaign(); yield return Shot("08_campaign", 0.9f);
            Menu.OpenCareer(); yield return Shot("09_career_overview");
            foreach (var t in new[] { "stats", "ratings", "progress", "history" }) { CareerView.Current?.SetTab(t); yield return Shot("10_career_" + t, 0.4f); }
            CareerView.Current?.Close(); Menu.Title();
            Menu.OpenSettings();
            foreach (var t in new[] { "video", "sound", "controls", "gameplay", "access" }) { OptionsView.Current?.SetTab(t); yield return Shot("11_options_" + t, 0.4f); }
            OptionsView.Current?.Close(); Menu.Title();
            Menu.Viewer("raijin"); yield return Shot("12_heroviewer", 5f);
            HeroViewerView.Current?.CloseToTitle();
            yield return null;
            OnlineView.Open(Menu); yield return Shot("13_online_lobby", 2.5f);
            Menu.Title();

            // a match: the loading screen, the HUD (AI Quick Match, Hanabi, Raijin, first person), Tab, pause, the result
            MenuState.Queue = "practice"; MenuState.Diff = 0.62;
            MatchSettings.Start("hanabi", "raijin", "practice", 0.62f, false);
            yield return Shot("14_loading", 0.35f);
            yield return Until(() => Runner != null && Runner.World != null && !LoadingView.Open);
            yield return new WaitForSecondsRealtime(15f);
            yield return Shot("15_hud");
            ForceBoard = true; yield return Shot("16_tab", 0.7f); ForceBoard = false;
            PauseMenu.Pause(); yield return Shot("17_pause", 0.7f);
            PauseMenu.Resume(); yield return new WaitForSecondsRealtime(0.3f);
            Runner.World.End(Runner.Player?.team ?? "zenith");
            yield return Shot("18_results", 5f);
            PauseMenu.Reset();

            // the Training Grounds
            MenuState.Queue = null;
            MatchSettings.Start("training", "raijin", "training", 0.65f, false);
            yield return Until(() => Runner != null && Runner.World != null && Runner.World.mode == "training" && !LoadingView.Open);
            yield return Shot("19_training", 3f);

            // Stadium: the Armory at the first round
            MatchSettings.Start("hanabi", "raijin", "stadium", 0.65f, true);
            yield return Until(() => Runner != null && Runner.World != null && Runner.World.mode == "stadium" && !LoadingView.Open);
            yield return Shot("20_armory", 3f);

            // the Ult Viewer
            Fx.UltShowcase.Open("raijin");
            yield return Until(() => Fx.UltShowcase.Current != null && !LoadingView.Open);
            yield return Shot("21_ultviewer", 5f);
            Debug.Log("[ZU tour] done");
            Application.Quit();
        }
    }
}
