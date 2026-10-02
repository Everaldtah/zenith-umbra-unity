// The Options screen (src/client/SettingsUI.ts), laid out after Overwatch 2's: VIDEO / SOUND / CONTROLS / GAMEPLAY /
// ACCESSIBILITY tabs, every change applied live and saved at once, a per-tab RESTORE DEFAULTS. CONTROLS rebinds any
// action to any key, mouse button or wheel notch (two bindings per action, like Overwatch), globally or per hero (a
// hero's own overrides and aim sensitivity), and has the reticle designer with a live preview.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class OptionsView
    {
        static readonly (string id, string name)[] TABS = { ("video", "VIDEO"), ("sound", "SOUND"), ("controls", "CONTROLS"), ("gameplay", "GAMEPLAY"), ("access", "ACCESSIBILITY") };
        static readonly Dictionary<string, string> ASIDE = new Dictionary<string, string>
        {
            ["video"] = "Graphics Quality fills in every detail option below it. Render scale and dynamic render scale trade sharpness for frame rate; performance stats (F8) show what the GPU is doing.",
            ["sound"] = "Every volume has its own mix bus. The mix preset changes how sounds are placed around you: HEADPHONES renders true 3D (HRTF), SPEAKERS pans them, NIGHT MODE squeezes the loud and the quiet together.",
            ["gameplay"] = "HUD options apply to every mode. Counter callouts show when a hero lands their rival counter.",
            ["access"] = "Subtitles show hero and announcer voice lines. The color blind filter corrects the whole picture; the UI colors recolor enemy and friendly markers.",
        };
        /// <summary>the colour choices (a browser opens the system colour picker; here a palette)</summary>
        static readonly string[] PALETTE = { "#ffffff", "#ff3b5c", "#ff8a3d", "#ffd76a", "#ffee58", "#7dff9a", "#34d1bf", "#5cc8ff", "#4aa8ff", "#9d7bff", "#c77dff", "#ff5dc8", "#000000" };

        static string lastTab = "video";
        string tab;
        string hero = "";                     // the controls scope: "" = all heroes
        (string action, int slot)? listening;
        int listenFrom;
        readonly VisualElement el;
        readonly Action back;
        VisualElement list, aside;
        ReticleEl preview;
        ScrollView scroll;
        ZuSettings S => ZuSettings.Current;

        OptionsView(VisualElement parent, Action back)
        {
            this.back = back;
            tab = lastTab;
            el = U.Div("opts", parent, pick: true);
            Grad.Fill(Grad.Radial(20, 0, (Grad.C("#1b2140"), 0), (Grad.C("#080a12"), 65)), el);
            Render();
        }

        public static OptionsView Open(VisualElement parent, Action back) => new OptionsView(parent, back);

        public void Close() { listening = null; el.RemoveFromHierarchy(); }

        /// <summary>every frame while open: a key being rebound takes the next key / button / wheel notch; Esc goes back</summary>
        public void Update()
        {
            if (listening != null)
            {
                if (Time.frameCount <= listenFrom) return;      // the click that started listening is not the binding
                var code = Keys.AnyPressed();
                if (code != null) Bound(code);
                return;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Done();
        }

        void Commit(bool rerender = false)
        {
            ZuSettings.Save(S);
            SettingsApply.Apply(S);
            if (rerender) Render(); else Preview();
        }

        void Done() { listening = null; Commit(); Close(); back?.Invoke(); }

        // ------------------------------------------------------------------ rows
        void Head(string t) => U.Txt(t, "h4", list);
        void Note(string t) => U.Txt(t, "note", list);

        VisualElement Row(string label, string desc)
        {
            var r = U.Div("orow", list, pick: true);
            U.Txt(label, "orow-l", r);
            if (!string.IsNullOrEmpty(desc)) r.tooltip = desc;
            return r;
        }

        void Select<T>(string label, Func<T> get, Action<T> set, (T v, string n)[] opts, string desc = "", bool rerender = false)
        {
            var r = Row(label, desc);
            var box = U.Div("sel", r);
            Label b = null;
            void Step(int d)
            {
                int i = Math.Max(0, Array.FindIndex(opts, o => Same(o.v, get())));
                var n = opts[(i + d + opts.Length) % opts.Length];
                set(n.v); b.text = n.n; Commit(rerender);
            }
            U.Btn("◀", "sel-b", () => Step(-1), box);
            var cur = get();
            b = U.Txt(opts.FirstOrDefault(o => Same(o.v, cur)).n ?? Convert.ToString(cur, System.Globalization.CultureInfo.InvariantCulture), "sel-v", box);
            U.Btn("▶", "sel-b", () => Step(1), box);
        }
        static bool Same<T>(T a, T b) => a is double x && b is double y ? Math.Abs(x - y) < 1e-6 : Equals(a, b);

        void Slider(string label, Func<double> get, Action<double> set, double min, double max, double step, Func<double, string> fmt = null, string desc = "")
        {
            fmt ??= v => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var r = Row(label, desc);
            var box = U.Div("sld", r);
            var s = new Slider((float)min, (float)max) { value = (float)get() };
            s.AddToClassList("sld-in");
            box.Add(s);
            var b = U.Txt(fmt(get()), "sld-v", box);
            s.RegisterValueChangedCallback(e =>
            {
                double v = Math.Round((e.newValue - min) / step) * step + min;
                v = Math.Round(v, 4);
                set(v); b.text = fmt(v); Commit();
            });
        }

        void ColorRow(string label, Func<string> get, Action<string> set)
        {
            var r = Row(label, "");
            var box = U.Div("clr", r);
            var sw = U.Div("clr-sw", box);
            Label b = null;
            foreach (var hex in PALETTE)
            {
                var chip = U.Btn(null, "clr-c", () => { set(hex); b.text = hex.ToUpperInvariant(); Paint(); Commit(); }, sw);
                chip.style.backgroundColor = U.Hex(hex);
            }
            b = U.Txt(get().ToUpperInvariant(), "clr-v", box);
            void Paint() { foreach (var c in sw.Children()) U.Toggle(c, "on", U.Css(c.resolvedStyle.backgroundColor).Equals(get(), StringComparison.OrdinalIgnoreCase)); }
            sw.schedule.Execute(Paint);
        }

        void BindRow(ZuSettings.ActionDef a)
        {
            var C = S.controls; bool scoped = hero != "";
            List<string> own = null;
            if (scoped && C.heroBinds.TryGetValue(hero, out var hb) && hb != null) hb.TryGetValue(a.id, out own);
            var b = ZuSettings.BindsFor(S, scoped ? hero : null, a.id);
            bool inherited = scoped && own == null;
            var r = Row(a.label, "");
            r.AddToClassList("bind");
            var keys = U.Div("keys", r);
            for (int slot = 0; slot < 2; slot++)
            {
                int sl = slot;
                bool on = listening?.action == a.id && listening?.slot == slot;
                var k = U.Btn(on ? "PRESS A KEY…" : ZuSettings.KeyName(slot < b.Count ? b[slot] : ""), "key" + (on ? " listen" : "") + (inherited ? " inh" : ""), () => Listen(a.id, sl), keys);
            }
            if (scoped && own != null)
            {
                var ov = U.Btn("↺", "clr-ov", () => { if (C.heroBinds.TryGetValue(hero, out var o)) o.Remove(a.id); Commit(true); }, keys);
                ov.tooltip = "Use the global binding";
            }
            else U.Div("clr-ov-gap", keys);
        }

        // ------------------------------------------------------------------ tabs
        static (string, string)[] Lv(params string[] o) => o.Select(x => (x, x.ToUpperInvariant())).ToArray();
        static readonly (bool, string)[] ON_OFF = { (true, "ON"), (false, "OFF") };

        void Video()
        {
            var v = S.video; var s = S;
            void Custom() => v.quality = "custom";
            Head("DISPLAY");
            Select("Display Mode", () => v.displayMode, x => v.displayMode = x, new[] { ("fullscreen", "FULLSCREEN"), ("borderless", "BORDERLESS WINDOWED"), ("windowed", "WINDOWED") });
            Slider("Field of View", () => s.fov, x => s.fov = x, 70, 110, 1, x => $"{x:0}");
            Select("Camera", () => s.view, x => s.view = x, new[] { ("first", "FIRST PERSON"), ("third", "THIRD PERSON") }, "Quick play and competitive are first person (as in Overwatch 2); V toggles where allowed.");
            Select("Frame Rate Cap", () => v.fpsCap, x => v.fpsCap = x, new[] { (0.0, "DISPLAY BASED"), (30.0, "30"), (60.0, "60"), (120.0, "120"), (144.0, "144"), (165.0, "165"), (240.0, "240"), (300.0, "300") });
            Slider("Render Scale", () => v.renderScale, x => { v.renderScale = x; Custom(); }, 50, 200, 5, x => $"{x:0}%");
            Select("Dynamic Render Scale", () => v.dynamicRes, x => v.dynamicRes = x, ON_OFF, "Lowers the render scale only when the GPU is what holds the frame rate back (never for a CPU-bound frame); FSR upscales back to full resolution.");
            Slider("Brightness", () => v.brightness, x => v.brightness = x, 0.5, 1.5, 0.01, x => U.F(x, 2));
            Slider("Contrast", () => v.contrast, x => v.contrast = x, 0.5, 1.5, 0.01, x => U.F(x, 2));
            Slider("Gamma", () => v.gamma, x => v.gamma = x, 0.5, 1.5, 0.01, x => U.F(x, 2));
            Slider("Image Sharpening", () => v.sharpen, x => v.sharpen = x, 0, 100, 1, x => $"{x:0}");
            Head("GRAPHICS QUALITY");
            Select("Graphics Quality", () => v.quality, x => { if (x != "custom") ZuSettings.ApplyPreset(s, x); }, new[] { ("low", "LOW"), ("medium", "MEDIUM"), ("high", "HIGH"), ("ultra", "ULTRA"), ("custom", "CUSTOM") }, "Sets every detail option below; changing any of them makes it CUSTOM.", true);
            Select("Texture Quality", () => v.textures, x => { v.textures = x; Custom(); }, Lv("low", "medium", "high"), "Applies to the next map loaded.");
            Select("Texture Filtering Quality", () => v.texFilter, x => { v.texFilter = x; Custom(); }, new[] { (1.0, "LOW - 1X"), (2.0, "MEDIUM - 2X"), (4.0, "HIGH - 4X"), (8.0, "HIGH - 8X"), (16.0, "EPIC - 16X") });
            Select("Fog Detail", () => v.fog, x => { v.fog = x; Custom(); }, Lv("low", "medium", "high"));
            Select("Dynamic Reflections", () => v.reflections, x => { v.reflections = x; Custom(); }, Lv("off", "low", "medium", "high", "ultra"));
            Select("Shadow Detail", () => v.shadows, x => { v.shadows = x; Custom(); }, Lv("off", "low", "medium", "high", "ultra"));
            Select("Effects Detail", () => v.effects, x => { v.effects = x; Custom(); }, Lv("low", "medium", "high", "ultra"), "Particle counts for weapons, abilities and impacts.");
            Select("Lighting Quality", () => v.lighting, x => { v.lighting = x; Custom(); }, Lv("low", "medium", "high", "ultra"), "Soft shadow filtering and light count.");
            Select("Antialias Quality", () => v.aa, x => { v.aa = x; Custom(); }, new[] { ("off", "OFF"), ("fxaa", "LOW - FXAA"), ("msaa", "MEDIUM - MSAA"), ("msaa+fxaa", "HIGH - MSAA + FXAA") }, "MSAA changes take effect after a restart.");
            Select("Refraction / Glow Quality", () => v.refraction, x => { v.refraction = x; Custom(); }, Lv("low", "medium", "high"), "Resolution of glow, energy and shield effects.");
            Select("Ambient Occlusion", () => v.ao, x => { v.ao = x; Custom(); }, Lv("off", "low", "medium", "high"));
            Select("Local Reflections", () => v.localReflections, x => { v.localReflections = x; Custom(); }, ON_OFF);
            Select("Bloom", () => v.bloom, x => { v.bloom = x; Custom(); }, ON_OFF);
            Select("Outer World (Unity)", () => v.outerWorld, x => { v.outerWorld = x; }, ON_OFF, "Terrain, skyline and landmarks past the arena's walls - a Unity edition extra (the PC game shows its painted sky there). Applies to the next map loaded.");
            Select("Far Depth of Field (Unity)", () => v.farDof, x => { v.farDof = x; }, ON_OFF, "Softens the far distance past the play space - a Unity edition extra. Applies to the next map loaded.");
            Select("Damage FX", () => v.damageFx, x => v.damageFx = x, new[] { ("low", "LOW"), ("default", "DEFAULT"), ("high", "HIGH") }, "How much impact sparks and damage effects fill the screen.");
            Head("PERFORMANCE");
            Select("Performance Stats", () => v.perfStats, x => v.perfStats = x, new[] { ("off", "OFF"), ("simple", "SIMPLE"), ("advanced", "ADVANCED") }, "F8 cycles it in game.");
        }

        void Sound()
        {
            var o = S.sound;
            string Pct(double x) => $"{Math.Round(x * 100)}";
            Head("VOLUME");
            Slider("Master Volume", () => o.master, x => o.master = x, 0, 1, 0.01, Pct);
            Slider("Sound Effects Volume", () => o.sfx, x => o.sfx = x, 0, 1, 0.01, Pct);
            Slider("Music Volume", () => o.music, x => o.music = x, 0, 1, 0.01, Pct);
            Slider("Voice Volume", () => o.voice, x => o.voice = x, 0, 1, 0.01, Pct);
            Slider("Announcer Volume", () => o.announcer, x => o.announcer = x, 0, 1, 0.01, Pct);
            Slider("Ambience Volume", () => o.ambience, x => o.ambience = x, 0, 1, 0.01, Pct);
            Slider("Interface Volume", () => o.ui, x => o.ui = x, 0, 1, 0.01, Pct);
            Slider("Hit Marker Volume", () => o.hitmarker, x => o.hitmarker = x, 0, 1, 0.01, Pct);
            Head("MIX");
            Select("Mix Preset", () => o.mix, x => o.mix = x, new[] { ("default", "DEFAULT"), ("headphones", "HEADPHONES (3D)"), ("speakers", "SPEAKERS"), ("night", "NIGHT MODE") },
                "Headphones: full HRTF 3D positioning. Speakers: plain panning. Night: a narrow dynamic range for low volume.");
            Select("Play Menu Music", () => o.menuMusic, x => o.menuMusic = x, ON_OFF);
            Select("Sound in Background", () => o.background, x => o.background = x, ON_OFF, "Keep playing sound when the game window is not focused.");
            Select("Audio Latency", () => o.latency, x => o.latency = x, new[] { ("interactive", "LOWEST"), ("balanced", "BALANCED"), ("playback", "SMOOTHEST") }, "Smoother trades delay for fewer crackles on busy systems (applies after a restart).");
        }

        void Controls()
        {
            var C = S.controls; var d = ZuData.Get();
            var roster = MenuState.Roster(d);
            string HeroName(string id) => d.Def(id)?.name ?? id;
            var scope = U.Div("scope", list);
            U.Txt("CONTROLS FOR", "scope-l", scope);
            var choices = new List<string> { "ALL HEROES" }; choices.AddRange(roster.Select(h => U.Up(h.name)));
            var dd = new DropdownField(choices, hero == "" ? 0 : Math.Max(0, roster.FindIndex(h => h.id == hero) + 1));
            dd.AddToClassList("hsel");
            dd.RegisterValueChangedCallback(e => { int i = dd.index; hero = i <= 0 ? "" : roster[i - 1].id; listening = null; Render(); });
            scope.Add(dd);
            if (hero != "") Note($"Bindings you set here apply only to {HeroName(hero)}. Dimmed keys follow the ALL HEROES set; ↺ returns an action to it.");
            Head("MOUSE");
            Slider("Sensitivity", () => S.sens, x => S.sens = x, 0.1, 5, 0.01, x => U.F(x * 15, 1), "Overwatch-style scale (15 = default).");
            if (hero != "")
            {
                string h = hero;
                Slider($"Sensitivity - {HeroName(h)}", () => (C.heroSens.TryGetValue(h, out var hs) ? hs : 1) * 100, x => { if (Math.Abs(x - 100) < 1e-6) C.heroSens.Remove(h); else C.heroSens[h] = x / 100; }, 25, 200, 1, x => $"{x:0}%", "Relative to your global sensitivity.");
            }
            Select("Invert Vertical Look", () => C.invertY, x => C.invertY = x, ON_OFF);
            Slider("Relative Aim Sensitivity While Zoomed", () => C.zoomSens * 100, x => C.zoomSens = x / 100, 25, 150, 1, x => $"{x:0}%");
            foreach (var g in new[] { "MOVEMENT", "WEAPONS & ABILITIES", "HERO", "INTERFACE" })
            {
                var acts = ZuSettings.ACTIONS.Where(a => a.group == g && (a.hero == null || hero == "" || a.hero == hero)).ToList();
                if (acts.Count == 0) continue;
                Head(g);
                foreach (var a in acts) BindRow(a);
            }
            if (hero == "" || hero == "tenkai")
            {
                Head("TENKAI-OH");
                Select("Barrier Free Look (hold Primary Fire)", () => C.barrierFreeLook, x => C.barrierFreeLook = x, ON_OFF, "With the Solar Bulwark up, hold primary fire to look around while the shield keeps its facing.");
                Select("Movement Relative to Camera During Free Look", () => C.freeLookRelative, x => C.freeLookRelative = x, ON_OFF);
            }
            Head("RETICLE");
            var R = C.reticle;
            Select("Type", () => R.type, x => R.type = x, new[] { ("default", "DEFAULT (PER HERO)"), ("circle", "CIRCLE"), ("crosshairs", "CROSSHAIRS"), ("circle+crosshairs", "CIRCLE AND CROSSHAIRS"), ("dot", "DOT") }, "", true);
            Select("Show Accuracy", () => R.accuracy, x => R.accuracy = x, ON_OFF);
            ColorRow("Color", () => R.color, x => R.color = x);
            Slider("Thickness", () => R.thickness, x => R.thickness = x, 1, 15, 1);
            Slider("Crosshair Length", () => R.length, x => R.length = x, 1, 100, 1);
            Slider("Center Gap", () => R.gap, x => R.gap = x, 0, 100, 1);
            Slider("Opacity", () => R.opacity * 100, x => R.opacity = x / 100, 0, 100, 1, x => $"{x:0}%");
            Slider("Outline Opacity", () => R.outline * 100, x => R.outline = x / 100, 0, 100, 1, x => $"{x:0}%");
            Slider("Dot Size", () => R.dot, x => R.dot = x, 0, 30, 1);
            Slider("Dot Opacity", () => R.dotOpacity * 100, x => R.dotOpacity = x / 100, 0, 100, 1, x => $"{x:0}%");
        }

        void Gameplay()
        {
            var g = S.gameplay; var s = S;
            Head("GENERAL");
            Select("AI Difficulty (Practice)", () => s.difficulty, x => s.difficulty = x, new[] { (0.35, "EASY"), (0.65, "MEDIUM"), (0.9, "HARD") });
            Select("Show Hints", () => g.hints, x => g.hints = x, ON_OFF);
            Select("Counter Callouts", () => g.counterCallouts, x => g.counterCallouts = x, ON_OFF, "The COUNTER banner when a hero counters their rival.");
            Head("HUD");
            Slider("HUD Scale", () => g.hudScale * 100, x => g.hudScale = x / 100, 70, 130, 1, x => $"{x:0}%");
            Slider("HUD Opacity", () => g.hudOpacity * 100, x => g.hudOpacity = x / 100, 30, 100, 1, x => $"{x:0}%");
            Select("Kill Feed", () => g.killFeed, x => g.killFeed = x, ON_OFF);
            Select("Damage Numbers", () => g.damageNumbers, x => g.damageNumbers = x, ON_OFF);
            Select("Hit Marker", () => g.hitmarkers, x => g.hitmarkers = x, ON_OFF);
            Slider("Objective Waypoint Opacity", () => g.waypointOpacity * 100, x => g.waypointOpacity = x / 100, 0, 100, 1, x => $"{x:0}%");
            Head("HEALTH BARS");
            Select("Enemy Health Bars", () => g.enemyBars, x => g.enemyBars = x, ON_OFF);
            Select("Friendly Health Bars", () => g.allyBars, x => g.allyBars = x, ON_OFF);
            Select("Name Tags", () => g.nameTags, x => g.nameTags = x, ON_OFF);
        }

        void Access()
        {
            var a = S.access;
            Head("SUBTITLES");
            Select("Subtitles", () => a.subtitles, x => a.subtitles = x, new[] { ("none", "OFF"), ("critical", "CRITICAL (ULTIMATES)"), ("conversations", "CONVERSATIONS"), ("all", "ALL VOICE LINES") });
            Slider("Subtitle Size", () => a.subSize * 100, x => a.subSize = x / 100, 70, 180, 5, x => $"{x:0}%");
            Slider("Subtitle Background Opacity", () => a.subBg * 100, x => a.subBg = x / 100, 0, 100, 1, x => $"{x:0}%");
            Head("COLOR");
            Select("Color Blind Options Filter", () => a.colorblind, x => a.colorblind = x, new[] { ("none", "OFF"), ("protanopia", "PROTANOPIA"), ("deuteranopia", "DEUTERANOPIA"), ("tritanopia", "TRITANOPIA") });
            Slider("Filter Strength", () => a.cbStrength * 100, x => a.cbStrength = x / 100, 0, 100, 1, x => $"{x:0}%");
            ColorRow("Enemy UI Color", () => a.enemyColor, x => a.enemyColor = x);
            ColorRow("Friendly UI Color", () => a.allyColor, x => a.allyColor = x);
            Head("MOTION & FLASH");
            Slider("Camera Shake", () => a.cameraShake * 100, x => a.cameraShake = x / 100, 0, 100, 1, x => $"{x:0}%");
            Slider("Screen Shake (HUD)", () => a.hudShake * 100, x => a.hudShake = x / 100, 0, 100, 1, x => $"{x:0}%");
            Select("Reduce Flashing", () => a.flashReduction, x => a.flashReduction = x, ON_OFF, "Softens muzzle flashes, ultimate flashes and explosion lights.");
        }

        // ------------------------------------------------------------------ frame
        void Render()
        {
            lastTab = tab;
            float keep = scroll?.verticalScroller.value ?? 0;
            while (el.childCount > 1) el.RemoveAt(1);           // (the backdrop stays)
            var header = U.Div("o-header", el);
            U.Txt("OPTIONS", "o-h2", header);
            var nav = U.Div("o-nav", header);
            foreach (var (k, n) in TABS)
                U.Btn(n, "tab" + (k == tab ? " on" : ""), () => { tab = k; listening = null; hero = k == "controls" ? hero : hero; Render(); scroll.verticalScroller.value = 0; }, nav);
            var main = U.Div("o-main", el);
            scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("o-list");
            main.Add(scroll);
            list = scroll.contentContainer;
            aside = U.Div("o-aside", main);
            switch (tab)
            {
                case "video": Video(); break;
                case "sound": Sound(); break;
                case "controls": Controls(); break;
                case "gameplay": Gameplay(); break;
                case "access": Access(); break;
            }
            preview = null;
            if (tab == "controls")
            {
                U.Txt("RETICLE PREVIEW", "h4", aside);
                var rp = U.Div("rprev", aside);
                Grad.Set(rp, Grad.Linear(180, (Grad.C("#39445c"), 0), (Grad.C("#39445c"), 62), (Grad.C("#5a6a8a"), 62.01f), (Grad.C("#5a6a8a"), 100)));
                preview = new ReticleEl(); preview.AddToClassList("fill"); rp.Add(preview);
                U.Txt("Click a key box, then press any key, mouse button or wheel notch. ESC cancels, BACKSPACE clears. A key already used by another action moves to this one.", "note", aside);
            }
            else U.Txt(ASIDE[tab], "note", aside);
            var footer = U.Div("o-footer", el);
            U.Btn("RESTORE DEFAULTS", "rst", Restore, footer);
            U.Btn("BACK", "primary done", Done, footer);
            Preview();
            scroll.schedule.Execute(() => scroll.verticalScroller.value = keep);
        }

        void Listen(string action, int slot)
        {
            listening = (action, slot);
            listenFrom = Time.frameCount;
            Render();
        }

        void Bound(string code)
        {
            var L = listening; listening = null;
            if (L == null || code == "Escape") { Render(); return; }
            var C = S.controls;
            List<string> Get(string a) => new List<string>(ZuSettings.BindsFor(S, hero == "" ? null : hero, a));
            void Put(string a, List<string> v)
            {
                if (hero != "") { if (!C.heroBinds.TryGetValue(hero, out var hb) || hb == null) C.heroBinds[hero] = hb = new Dictionary<string, List<string>>(); hb[a] = v; }
                else C.binds[a] = v;
            }
            var cur = Get(L.Value.action);
            if (code == "Backspace" || code == "Delete") { if (L.Value.slot < cur.Count) cur.RemoveAt(L.Value.slot); }
            else
            {
                // a key belongs to one action at a time in a scope (as in Overwatch): take it off any other action
                foreach (var a in ZuSettings.ACTIONS)
                    if (a.id != L.Value.action && (a.hero == null || hero == "" || a.hero == hero))
                    {
                        var other = Get(a.id);
                        if (other.Contains(code) && !(a.hero != null && hero == "")) Put(a.id, other.Where(c => c != code).ToList());
                    }
                if (cur.Contains(code)) cur.Remove(code);
                int at = Math.Min(L.Value.slot, cur.Count);
                if (at < cur.Count) cur[at] = code; else cur.Add(code);
            }
            Put(L.Value.action, cur.Where(c => !string.IsNullOrEmpty(c)).Take(2).ToList());
            Commit(true);
        }

        void Restore()
        {
            var d = ZuSettings.Defaults(S.preset); var s = S;
            if (tab == "video") { s.video = d.video; ZuSettings.ApplyPreset(s, s.preset); s.fov = d.fov; }
            if (tab == "sound") s.sound = d.sound;
            if (tab == "controls")
            {
                if (hero != "")
                {
                    s.controls.heroBinds.Remove(hero);
                    if (ZuSettings.HeroDefaultBinds().TryGetValue(hero, out var hd)) s.controls.heroBinds[hero] = hd;
                    s.controls.heroSens.Remove(hero);
                }
                else { s.controls = d.controls; s.sens = d.sens; }
            }
            if (tab == "gameplay") { s.gameplay = d.gameplay; s.difficulty = d.difficulty; }
            if (tab == "access") s.access = d.access;
            Commit(true);
        }

        /// <summary>the reticle preview on the controls tab (drawn at 2x, as the TS canvas is)</summary>
        void Preview() => preview?.Set(S.controls.reticle, 2);
    }
}
