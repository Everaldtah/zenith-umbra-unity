// Story cinematics (src/campaign/Cinematic.ts): Ken-Burns storyboard panels with letterbox bars and typewriter
// captions (click / SPACE: next, ESC: skip), and the boss title card when a colossus arrives.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public static class StoryView
    {
        static VisualElement Root() => UiRoot.Get().OverlayLayer;

        static void Sfx(string id, float vol = 1) { try { if (Audio.AudioKit.Has(id)) Audio.AudioKit.Play(id, null, vol); } catch (Exception) { /* no bank */ } }

        /// <summary>play storyboard panels (img = the TS path "img/cine_02.webp" or a bare image name); done runs when finished or skipped</summary>
        public static void Play(IList<(string img, string text)> panels, Action done)
        {
            if (panels == null || panels.Count == 0) { done?.Invoke(); return; }
            var panel = U.Div("cine-panel", Root(), pick: true);
            var img = U.Div("cine-img", panel);
            U.Div("cine-bar top", panel); U.Div("cine-bar bot", panel);
            var cap = U.Txt("", "cine-cap", panel);
            U.Txt("CLICK / SPACE: NEXT · ESC: SKIP", "cine-skip", panel);
            var fade = U.Div("cine-fade", panel);
            int i = 0; bool finished = false;
            float shownAt = 0, nextAt = float.MaxValue; string text = ""; int typed = 0; float typeAt = 0;
            void Finish()
            {
                if (finished) return; finished = true;
                fade.AddToClassList("on");
                panel.schedule.Execute(() => { panel.RemoveFromHierarchy(); done?.Invoke(); }).StartingIn(600);
            }
            void Show()
            {
                if (finished) return;
                if (i >= panels.Count) { Finish(); return; }
                var p = panels[i++];
                fade.AddToClassList("on");
                panel.schedule.Execute(() =>
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(p.img ?? "");
                    U.Bg(img, name);
                    shownAt = Time.unscaledTime;
                    fade.RemoveFromClassList("on");
                    cap.text = ""; text = p.text ?? ""; typed = 0; typeAt = Time.unscaledTime;
                    nextAt = Time.unscaledTime + Mathf.Max(4.5f, text.Length * 0.055f + 1.8f);
                }).StartingIn(i == 1 ? 50 : 600);
            }
            panel.RegisterCallback<ClickEvent>(_ => Show());
            panel.schedule.Execute(() =>
            {
                if (finished) return;
                float now = Time.unscaledTime;
                // @keyframes cinekb (9s ease-out forwards): scale 1.02 -> 1.14, translateX -1.5% -> 1.5%
                float k = Mathf.Clamp01((now - shownAt) / 9f); k = 1 - (1 - k) * (1 - k);
                float sc = Mathf.Lerp(1.02f, 1.14f, k);
                img.style.scale = new Scale(new Vector3(sc, sc, 1));
                img.style.translate = new Translate(new Length(Mathf.Lerp(-1.5f, 1.5f, k), LengthUnit.Percent), 0);
                // the typewriter: a letter every 28 ms, a soft tick every third
                while (typed < text.Length && now - typeAt >= 0.028f) { typeAt += 0.028f; typed++; cap.text = text.Substring(0, typed); if (typed % 3 == 0) Sfx("ui_hover", 0.3f); }
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { Finish(); return; }
                if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || now >= nextAt) { nextAt = float.MaxValue; Show(); }
            }).Every(16);
            Show();
        }

        /// <summary>the campaign level's intro (and, before the first level, the prologue)</summary>
        public static List<(string, string)> Beats(List<CampaignBeat> beats)
        {
            var o = new List<(string, string)>();
            if (beats != null) foreach (var b in beats) o.Add((b.img, b.text));
            return o;
        }

        /// <summary>the non-blocking boss title card (a colossus arrives)</summary>
        public static void BossCard(string id)
        {
            var d = ZuData.Get();
            if (!d.Boss.TryGetValue(id ?? "", out var b)) return;
            var c = U.Hex(b.glow ?? "#ffffff");
            var card = U.Div("cine-boss", Root());
            U.Txt("WARNING · COLOSSUS DETECTED", "cb-s", card);
            var name = U.Txt(b.name, "cb-b", card); name.style.color = c;
            U.Txt(b.title ?? "", "cb-t", card);
            U.Txt("Weak point: " + (b.weak ?? ""), "cb-w", card);
            Sfx("ultcall"); Sfx("thunderclap");
            // @keyframes bossin (3.6s): 0% scale 1.3 transparent, 12% in place, 85% held, 100% gone
            float t0 = Time.unscaledTime;
            card.schedule.Execute(() =>
            {
                float k = (Time.unscaledTime - t0) / 3.6f;
                if (k >= 1) { card.RemoveFromHierarchy(); return; }
                float a = k < 0.12f ? k / 0.12f : k > 0.85f ? 1 - (k - 0.85f) / 0.15f : 1;
                float s = k < 0.12f ? Mathf.Lerp(1.3f, 1, k / 0.12f) : 1;
                card.style.opacity = a; card.style.scale = new Scale(new Vector3(s, s, 1));
            }).Every(16);
        }
    }
}
