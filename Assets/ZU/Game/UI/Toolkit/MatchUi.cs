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
        Career.CareerTracker career;
        double lastSimT;
        public static MatchUi Current { get; private set; }
        /// <summary>the finished match's hero levels and Hero SR changes (the results screens show them)</summary>
        public static Career.RecordResult LastRecord { get; private set; }

        public static MatchUi Attach(MatchRunner runner)
        {
            var m = Current = runner.gameObject.AddComponent<MatchUi>();
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
            // the Career Profile follows the local player (spectating, the AI lab and the Ult Viewer record nothing)
            string cm = Career.CareerProfile.ModeOf(r.mode);
            career = cm != null && r.Player != null ? new Career.CareerTracker(cm, r.mapId) : null;
            LastRecord = null;
            lastSimT = r.World?.time ?? 0;
            StartCoroutine(LoadingView.Warm());
        }

        /// <summary>the match into the Career Profile (TS recordCareerMatch): "win" / "loss" / "draw" at the end, "none"
        /// when it is left early (its time still counts)</summary>
        public void RecordCareer(string result)
        {
            if (career == null || r.World == null) return;
            var t = career; career = null;
            var w = r.World; var me = r.Player;
            string score = null;
            if (me != null)
            {
                string us = me.team, them = us == "zenith" ? "umbra" : "zenith";
                score = w.rules == "push" ? $"{System.Math.Round(w.push.best[us])}m - {System.Math.Round(w.push.best[them])}m" : w.rules == "control" ? $"{w.control.wins[us]} - {w.control.wins[them]}" : null;
            }
            var sum = t.Finish(w, me, result, t.mode == "competitive" || t.mode == "quickplay" ? MatchSettings.Opp : (double?)null, score);
            if (sum.heroes.Count == 0) return;
            var p = Career.CareerProfile.Load();
            var ranks = Career.Ranks.Load();
            LastRecord = Career.CareerProfile.RecordMatch(p, sum, hero =>
            {
                var role = ZU.Sim.Data.GameData.Current?.Def(hero)?.role;
                return role != null && ranks.roles.TryGetValue(role == "dps" ? "damage" : role, out var rr) ? rr.rating : 1800;
            });
            Career.CareerProfile.Save(p);
        }

        void OnDestroy()
        {
            RecordCareer("none");
            if (Current == this) Current = null;
            EventSink.OnEvent -= OnEvent;
            ZuSettings.Changed -= OnSettings;
            PauseMenu.EscTaken = null;
            hud?.root.RemoveFromHierarchy();
            armory?.Hide();
            var ui = UiRoot.Existing;            // (no new panel while the app quits)
            if (ui != null) { ui.HudLayer.Clear(); ui.MenuLayer.Clear(); }
        }

        void OnSettings(ZuSettings s) => hud.ApplySettings(s);
        void OnEvent(MatchRunner runner, SimEvent e) { if (runner == r && r.World != null) hud.Event(e, r.Player, r.World.time); }
        static void Sfx(string id) { try { if (AudioKit.Has(id)) AudioKit.Play(id, null); } catch (System.Exception) { /* no bank */ } }

        void LateUpdate()
        {
            var w = r.World; if (w == null) return;
            var s = ZuSettings.Current;
            // the career clock runs on simulation time (pauses don't count); the result is recorded once it is decided
            career?.Frame(w, r.Player, w.time - lastSimT);
            lastSimT = w.time;
            if (career != null && !string.IsNullOrEmpty(w.winner))
                RecordCareer(r.Player == null ? "none" : w.winner == r.Player.team ? "win" : "loss");
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
