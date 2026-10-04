// Player settings (src/client/Settings.ts), laid out like Overwatch 2's options (VIDEO / SOUND / CONTROLS / GAMEPLAY /
// ACCESSIBILITY): graphics quality presets that fill in every detail setting (and "custom" once you change one),
// rebindable controls with two bindings per action and per-hero overrides, reticle design, sound mix, HUD and
// accessibility options. Saved as JSON in the player's data folder (the TS uses localStorage); older saves are merged
// over the defaults, so new options appear with their defaults. Field names match the TS so the two read alike.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZU.Game.UI.Toolkit
{
    public sealed class ZuSettings
    {
        // ------------------------------------------------------------------ controls
        public sealed class ActionDef { public string id, label, group, hero; }
        public static readonly ActionDef[] ACTIONS =
        {
            new ActionDef { id = "forward", label = "Move Forward", group = "MOVEMENT" }, new ActionDef { id = "back", label = "Move Backward", group = "MOVEMENT" },
            new ActionDef { id = "left", label = "Move Left", group = "MOVEMENT" }, new ActionDef { id = "right", label = "Move Right", group = "MOVEMENT" },
            new ActionDef { id = "jump", label = "Jump / Fly", group = "MOVEMENT" }, new ActionDef { id = "crouch", label = "Crouch / Descend", group = "MOVEMENT" },
            new ActionDef { id = "fire", label = "Primary Fire", group = "WEAPONS & ABILITIES" }, new ActionDef { id = "alt", label = "Secondary Fire", group = "WEAPONS & ABILITIES" },
            new ActionDef { id = "a1", label = "Ability 1", group = "WEAPONS & ABILITIES" }, new ActionDef { id = "a2", label = "Ability 2", group = "WEAPONS & ABILITIES" },
            new ActionDef { id = "ult", label = "Ultimate Ability", group = "WEAPONS & ABILITIES" }, new ActionDef { id = "reload", label = "Reload", group = "WEAPONS & ABILITIES" },
            new ActionDef { id = "melee", label = "Quick Melee", group = "WEAPONS & ABILITIES" },
            new ActionDef { id = "swoop", label = "Starwing Swoop (Mirei)", group = "HERO", hero = "mirei" },
            new ActionDef { id = "grind", label = "Mag-Grind: hold to wall-ride & climb (Hibiki)", group = "HERO", hero = "hibiki" },
            new ActionDef { id = "view", label = "Toggle First / Third Person", group = "INTERFACE" }, new ActionDef { id = "score", label = "Scoreboard / Stats", group = "INTERFACE" },
            new ActionDef { id = "swap", label = "Change Hero (Training)", group = "INTERFACE" }, new ActionDef { id = "perf", label = "Cycle Performance Stats", group = "INTERFACE" },
        };

        public static Dictionary<string, List<string>> DefaultBinds() => new Dictionary<string, List<string>>
        {
            ["forward"] = new List<string> { "KeyW", "ArrowUp" }, ["back"] = new List<string> { "KeyS", "ArrowDown" }, ["left"] = new List<string> { "KeyA", "ArrowLeft" }, ["right"] = new List<string> { "KeyD", "ArrowRight" },
            ["jump"] = new List<string> { "Space" }, ["crouch"] = new List<string> { "ControlLeft" }, ["fire"] = new List<string> { "Mouse0" }, ["alt"] = new List<string> { "Mouse2" }, ["a1"] = new List<string> { "ShiftLeft", "ShiftRight" }, ["a2"] = new List<string> { "KeyE" },
            ["ult"] = new List<string> { "KeyQ" }, ["reload"] = new List<string> { "KeyR" }, ["melee"] = new List<string> { "KeyC" }, ["swoop"] = new List<string> { "KeyF" }, ["grind"] = new List<string> { "Space" }, ["view"] = new List<string> { "KeyV" }, ["score"] = new List<string> { "Tab" }, ["swap"] = new List<string> { "KeyH" }, ["perf"] = new List<string> { "F8" },
        };
        /// <summary>per-hero defaults (Overwatch keeps hero-specific control sets): Hibiki rides walls on the left mouse button</summary>
        public static Dictionary<string, Dictionary<string, List<string>>> HeroDefaultBinds() => new Dictionary<string, Dictionary<string, List<string>>>
        {
            ["hibiki"] = new Dictionary<string, List<string>> { ["grind"] = new List<string> { "Mouse0" } },
        };

        /// <summary>the readable name of an input code (the TS uses the browser's KeyboardEvent.code names)</summary>
        public static string KeyName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "—";
            switch (code)
            {
                case "Mouse0": return "LEFT MOUSE"; case "Mouse1": return "MIDDLE MOUSE"; case "Mouse2": return "RIGHT MOUSE"; case "Mouse3": return "MOUSE 4"; case "Mouse4": return "MOUSE 5";
                case "WheelUp": return "WHEEL UP"; case "WheelDown": return "WHEEL DOWN"; case "Space": return "SPACE"; case "ShiftLeft": return "L-SHIFT"; case "ShiftRight": return "R-SHIFT";
                case "ControlLeft": return "L-CTRL"; case "ControlRight": return "R-CTRL"; case "AltLeft": return "L-ALT"; case "AltRight": return "R-ALT";
                case "ArrowUp": return "↑"; case "ArrowDown": return "↓"; case "ArrowLeft": return "←"; case "ArrowRight": return "→";
                case "Tab": return "TAB"; case "Enter": return "ENTER"; case "Backspace": return "BACKSPACE"; case "CapsLock": return "CAPS LOCK"; case "Backquote": return "`";
            }
            if (code.StartsWith("Key")) return code.Substring(3);
            if (code.StartsWith("Digit")) return code.Substring(5);
            if (code.StartsWith("Numpad")) return "NUM " + code.Substring(6);
            return code.ToUpperInvariant();
        }
        /// <summary>the short label on the ability bar</summary>
        public static string KeyShort(string code)
        {
            if (string.IsNullOrEmpty(code)) return "—";
            switch (code)
            {
                case "Mouse0": return "LMB"; case "Mouse1": return "MMB"; case "Mouse2": return "RMB"; case "Mouse3": return "M4"; case "Mouse4": return "M5";
                case "ShiftLeft": case "ShiftRight": return "SHIFT"; case "ControlLeft": return "CTRL"; case "Space": return "SPACE"; case "WheelUp": return "WHL▲"; case "WheelDown": return "WHL▼";
            }
            return KeyName(code);
        }

        // ------------------------------------------------------------------ the settings
        public sealed class VideoSettings
        {
            public string displayMode = "borderless";        // windowed | borderless | fullscreen
            public double renderScale = 100;                 // % of native
            public bool dynamicRes = true;
            public double fpsCap;                            // 0 = display refresh
            public string quality = "ultra";                 // low | medium | high | ultra | custom
            public string textures = "high";
            public double texFilter = 16;
            public string fog = "high", reflections = "ultra", shadows = "ultra", model = "ultra", effects = "ultra", lighting = "ultra";
            public string aa = "msaa+fxaa";                  // off | fxaa | msaa | msaa+fxaa
            public string refraction = "high", ao = "medium";
            public bool localReflections = true, bloom = true;
            public string damageFx = "default";
            public double sharpen;
            public double gamma = 1, contrast = 1, brightness = 1;
            public string perfStats = "simple";              // off | simple | advanced
            /// <summary>Unity extra: terrain, skyline and landmarks past the arena's walls (the PC game shows only its painted sky)</summary>
            public bool outerWorld = false;
            /// <summary>Unity extra: a far-only depth of field past the play space (the PC game has none)</summary>
            public bool farDof = false;
            /// <summary>Unity extra: Tripo grass / rock beds on the floors (GroundDressing): off | low | high</summary>
            public string groundDetail = "high";
        }
        public sealed class SoundSettings
        {
            public double master = 0.7, sfx = 1, music = 0.6, voice = 1, announcer = 1, ambience = 0.8, ui = 0.8, hitmarker = 1;
            public string mix = "default";                   // default | headphones (Steam Audio HRTF) | speakers
            public string range = "normal";                  // Dynamic Range: home (full) | normal | night (narrow) - Unity only
            public bool menuMusic = true, background;
            public string latency = "interactive";
        }
        public sealed class Reticle
        {
            public string type = "default";                  // default | circle | crosshairs | circle+crosshairs | dot
            public string color = "#ffffff";
            public double thickness = 2, length = 7, gap = 5, opacity = 1, outline = 0.6, dot = 3, dotOpacity = 1;
            public bool accuracy;
            public Reticle Clone() => (Reticle)MemberwiseClone();
        }
        public sealed class ControlSettings
        {
            public Dictionary<string, List<string>> binds = DefaultBinds();
            public Dictionary<string, Dictionary<string, List<string>>> heroBinds = HeroDefaultBinds();
            public Dictionary<string, double> heroSens = new Dictionary<string, double>();
            public bool invertY;
            public double zoomSens = 1;
            public bool barrierFreeLook = true, freeLookRelative;
            public Reticle reticle = new Reticle();
        }
        public sealed class GameplaySettings
        {
            public bool damageNumbers = true, killFeed = true, hints = true, enemyBars = true, allyBars = true, nameTags = true, hitmarkers = true;
            public double waypointOpacity = 1, hudScale = 1, hudOpacity = 1;
            public bool counterCallouts = true;
        }
        public sealed class AccessSettings
        {
            public string subtitles = "critical";            // none | critical | conversations | all
            public double subSize = 1, subBg = 0.5;
            public string colorblind = "none"; public double cbStrength = 1;
            public string enemyColor = "#ff3b5c", allyColor = "#5cc8ff";
            public double cameraShake = 1, hudShake = 1;
            public bool flashReduction;
        }

        public string preset = "ultra";                      // the desktop edition starts on ULTRA (Settings.detect)
        public double sens = 1, volume = 0.7, fov = 90;
        public string view = "third";
        public double difficulty = 0.65;
        public bool showFps = true;
        public VideoSettings video = new VideoSettings();
        public SoundSettings sound = new SoundSettings();
        public ControlSettings controls = new ControlSettings();
        public GameplaySettings gameplay = new GameplaySettings();
        public AccessSettings access = new AccessSettings();

        /// <summary>what each graphics preset sets every detail option to</summary>
        public static readonly Dictionary<string, Action<VideoSettings>> QUALITY_TABLE = new Dictionary<string, Action<VideoSettings>>
        {
            ["low"] = v => { v.textures = "low"; v.texFilter = 1; v.fog = "low"; v.reflections = "off"; v.shadows = "off"; v.model = "low"; v.effects = "low"; v.lighting = "low"; v.aa = "off"; v.refraction = "low"; v.ao = "off"; v.localReflections = false; v.bloom = false; v.renderScale = 75; },
            ["medium"] = v => { v.textures = "medium"; v.texFilter = 4; v.fog = "medium"; v.reflections = "low"; v.shadows = "low"; v.model = "medium"; v.effects = "medium"; v.lighting = "medium"; v.aa = "fxaa"; v.refraction = "medium"; v.ao = "off"; v.localReflections = false; v.bloom = false; v.renderScale = 100; },
            ["high"] = v => { v.textures = "high"; v.texFilter = 8; v.fog = "high"; v.reflections = "medium"; v.shadows = "medium"; v.model = "high"; v.effects = "high"; v.lighting = "high"; v.aa = "msaa"; v.refraction = "high"; v.ao = "low"; v.localReflections = true; v.bloom = true; v.renderScale = 100; },
            ["ultra"] = v => { v.textures = "high"; v.texFilter = 16; v.fog = "high"; v.reflections = "ultra"; v.shadows = "ultra"; v.model = "ultra"; v.effects = "ultra"; v.lighting = "ultra"; v.aa = "msaa+fxaa"; v.refraction = "high"; v.ao = "medium"; v.localReflections = true; v.bloom = true; v.renderScale = 125; },
        };

        public static ZuSettings Defaults(string preset = "ultra")
        {
            var s = new ZuSettings { preset = preset };
            s.video.quality = preset;
            QUALITY_TABLE[preset](s.video);
            return s;
        }

        /// <summary>apply a graphics preset to every detail option</summary>
        public static void ApplyPreset(ZuSettings s, string p) { s.preset = p; s.video.quality = p; QUALITY_TABLE[p](s.video); }

        /// <summary>the binding a hero actually uses for an action (their override, else the global one)</summary>
        public static List<string> BindsFor(ZuSettings s, string hero, string action)
        {
            if (!string.IsNullOrEmpty(hero) && s.controls.heroBinds.TryGetValue(hero, out var hb) && hb != null && hb.TryGetValue(action, out var own) && own != null) return own;
            return s.controls.binds.TryGetValue(action, out var b) && b != null ? b : new List<string>();
        }

        // ------------------------------------------------------------------ persistence
        static ZuSettings current;
        /// <summary>raised after every change (the HUD, the audio mix and the renderer re-read what they use)</summary>
        public static event Action<ZuSettings> Changed;
        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static ZuSettings Current => current ??= Load();

        public static ZuSettings Load()
        {
            var def = JObject.FromObject(Defaults());
            try
            {
                if (File.Exists(FilePath))
                {
                    var saved = JObject.Parse(File.ReadAllText(FilePath));
                    // night mode moved from the mix preset to its own Dynamic Range setting: an older save keeps it
                    if (saved["sound"] is JObject ss && ss["range"] == null && (string)ss["mix"] == "night") { ss["range"] = "night"; ss["mix"] = "default"; }
                    // the saved preset decides the defaults the save is merged over
                    if (saved["preset"] is JValue pv && pv.Value is string ps && QUALITY_TABLE.ContainsKey(ps)) def = JObject.FromObject(Defaults(ps));
                    Merge(def, saved);
                }
            }
            catch (Exception e) { Debug.LogWarning("[ZU] settings unreadable, using defaults: " + e.Message); }
            var s = def.ToObject<ZuSettings>(Json);
            // a new hero's own defaults join older saves without touching what the player set
            foreach (var kv in HeroDefaultBinds())
            {
                if (!s.controls.heroBinds.TryGetValue(kv.Key, out var hb) || hb == null) s.controls.heroBinds[kv.Key] = hb = new Dictionary<string, List<string>>();
                foreach (var b in kv.Value) if (!hb.ContainsKey(b.Key)) hb[b.Key] = b.Value;
            }
            return s;
        }

        /// <summary>deep-merge a saved object over the defaults: a value replaces the default only when the types agree</summary>
        static void Merge(JObject def, JObject saved)
        {
            foreach (var p in saved.Properties())
            {
                var d = def[p.Name];
                if (d is JObject dobj && p.Value is JObject sobj) Merge(dobj, sobj);
                else if (d == null || d.Type == p.Value.Type || (IsNum(d) && IsNum(p.Value))) def[p.Name] = p.Value.DeepClone();
            }
        }
        static bool IsNum(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        /// <summary>the UI tour runs on the defaults (the TS tour clears localStorage) and saves nothing</summary>
        public static void TourDefaults() => current = Defaults();

        public static void Save(ZuSettings s)
        {
            s.volume = s.sound.master;
            current = s;
            if (UiTour.Active) { Changed?.Invoke(s); return; }
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(s, Formatting.Indented)); }
            catch (Exception e) { Debug.LogWarning("[ZU] settings not saved: " + e.Message); }
            Changed?.Invoke(s);
        }

        public ZuSettings Clone() => JObject.FromObject(this).ToObject<ZuSettings>(Json);

        /// <summary>a read replaces the fields' default lists / dictionaries instead of appending to them</summary>
        static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
    }
}
