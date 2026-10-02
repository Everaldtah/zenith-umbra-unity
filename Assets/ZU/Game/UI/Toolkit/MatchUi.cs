// The match's UI Toolkit front end, attached by MatchRunner.Start: the HUD (fed the simulation's events and, after the
// views have moved, the frame's state), the Stadium Armory, the pause / results / Options screens, the performance
// readout (F8 cycles it), the ult-ready sound, and the loading screen's warm-up and lift.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.Audio;
using ZU.Game.UI;
using ZU.Sim;

namespace ZU.Game.UI.Toolkit
{
    public sealed class MatchUi : MonoBehaviour
    {
        MatchRunner r;
        HudView hud;
        ArmoryView armory;
        PauseView pause;
        float fpsAvg;

        public static MatchUi Attach(MatchRunner runner)
        {
            var m = runner.gameObject.AddComponent<MatchUi>();
            m.r = runner;
            m.Build();
            return m;
        }

        void Build()
        {
            var ui = UiRoot.Get();
            ui.HudLayer.Clear(); ui.MenuLayer.Clear();
            hud = new HudView(ui.HudLayer);
            hud.Reset(); hud.Show(true);
            hud.OnUltReady = () => Sfx("ult_ready");
            armory = new ArmoryView(ui.MenuLayer);
            pause = new PauseView(ui.MenuLayer);
            EventSink.OnEvent += OnEvent;
            ZuSettings.Changed += OnSettings;
            PauseMenu.EscTaken = () => pause.TakeEsc();
            ZButton.Sfx = id => Sfx(id);
            SettingsApply.Apply(ZuSettings.Current);
            StartCoroutine(LoadingView.Warm());
        }

        void OnDestroy()
        {
            EventSink.OnEvent -= OnEvent;
            ZuSettings.Changed -= OnSettings;
            PauseMenu.EscTaken = null;
            hud?.root.RemoveFromHierarchy();
            armory?.Hide();
            var ui = UiRoot.Get();
            ui.HudLayer.Clear(); ui.MenuLayer.Clear();
        }

        void OnSettings(ZuSettings s) => hud.ApplySettings(s);
        void OnEvent(MatchRunner runner, SimEvent e) { if (runner == r && r.World != null) hud.Event(e, r.Player, r.World.time); }
        static void Sfx(string id) { try { if (AudioKit.Has(id)) AudioKit.Play(id, null); } catch (System.Exception) { /* no bank */ } }

        void LateUpdate()
        {
            var w = r.World; if (w == null) return;
            var s = ZuSettings.Current;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0) fpsAvg = fpsAvg <= 0 ? 1 / dt : Mathf.Lerp(fpsAvg, 1 / dt, 0.05f);
            // F8 (or its rebinding): off -> simple -> advanced
            if (Keys.Pressed(ZuSettings.BindsFor(s, r.Player?.def.id, "perf")))
            {
                var o = new[] { "off", "simple", "advanced" };
                s.video.perfStats = o[(System.Array.IndexOf(o, s.video.perfStats) + 1) % 3];
                ZuSettings.Save(s);
            }
            bool board = !PauseMenu.Paused && Keys.Held(ZuSettings.BindsFor(s, r.Player?.def.id, "score"));
            hud.Update(w, r.Player, Camera.main, w.time, s.video.perfStats != "off" ? fpsAvg : 0, board, r.Player == null ? "SPECTATING · Esc menu" : "");
            if (s.video.perfStats == "advanced")
                hud.Perf(new[]
                {
                    $"{fpsAvg:0} FPS  {1000 / Mathf.Max(1, fpsAvg):0.0} ms",
                    $"render {Screen.width}x{Screen.height}",
                    $"sim 120 Hz  heroes {w.actors.Count}  projectiles {w.projs.Count}",
                });
            else hud.Perf(null);
            // the Stadium Armory between rounds (the cursor is freed by ModeHud while shopping)
            var me = r.Player;
            bool want = w.stadium != null && me != null && !PauseMenu.Paused && ModeHud.Shopping(r);
            if (want && !armory.open) armory.Show(w, me);
            if (!want && armory.open) armory.Hide();
            armory.Update();
            pause.Update(r);
        }
    }
}
