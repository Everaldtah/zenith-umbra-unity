// The Menu scene's entry point: the UI Toolkit front end (Toolkit/MenuView.cs, the TS Menu.ts screens) on the
// persistent panel, opened on the screen the last match handed back to. The scene keeps this component (and its
// camera), so the class name stays.
using UnityEngine;
using ZU.Game.UI.Toolkit;

namespace ZU.Game.UI
{
    public class MainMenu : MonoBehaviour
    {
        MenuView menu;

        void Start()
        {
            StartupClock.Mark("menu scene start");
            Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            PauseMenu.Reset();
            ZuData.Get();
            var ui = UiRoot.Get();
            ui.HudLayer.Clear(); ui.MenuLayer.Clear();
            LoadingView.Close();
            ZButton.Sfx = id => { try { if (Audio.AudioKit.Has(id)) Audio.AudioKit.Play(id, null); } catch (System.Exception) { /* no bank */ } };
            SettingsApply.Apply(ZuSettings.Current);
            if (DevArgs.TryStartMatch()) return;             // QA flags (--zu-map / --zu-mode ...): straight into a match
            menu = MenuView.Open(ui.MenuLayer);
            StartupClock.Mark("menu built");
            StartCoroutine(FirstFrame());
        }

        System.Collections.IEnumerator FirstFrame() { yield return null; yield return new WaitForEndOfFrame(); StartupClock.Mark("menu first frame drawn"); }

        void OnDestroy() => menu?.Close();
    }
}
