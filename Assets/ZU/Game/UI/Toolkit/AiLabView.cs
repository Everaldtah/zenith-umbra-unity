// AI Test Lab (src/client/AiLab.ts): 10 bots play every map in turn while this validates what the simulation can
// answer - the animation states each hero exercised, every ability cast, the rival counters, the effect and sound
// kinds (sounds with no clip are failures), wall penetration, sinking into floors, moving faster than allowed, and
// each map's result. The live panel is the TS .lab; the report is also written to ailab.json in the player's data
// folder for headless runs. (Foot sliding and bone checks read the procedural animator's internals: n/a here.)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.UI;
using ZU.Sim;

namespace ZU.Game.UI.Toolkit
{
    public static class AiLab
    {
        static readonly string[] ANIM_STATES = { "idle", "run", "strafe", "backpedal", "jump", "fall", "attack", "punch", "cast", "hit", "death" };
        sealed class HeroStats { public HashSet<string> states = new HashSet<string>(); public int penetrate, sink, overspeed, frames; public string rig = "mannequin"; }
        sealed class MapResult { public double secs; public int kills, falls, stuck; public string winner; }

        static readonly Dictionary<string, MapResult> maps = new Dictionary<string, MapResult>();
        static readonly Dictionary<string, HeroStats> heroes = new Dictionary<string, HeroStats>();
        static readonly HashSet<string> casts = new HashSet<string>();
        static readonly Dictionary<string, string> counters = new Dictionary<string, string>();
        static readonly Dictionary<string, int> fxKinds = new Dictionary<string, int>(), sfxIds = new Dictionary<string, int>();
        static readonly HashSet<string> unknownSfx = new HashSet<string>();
        static readonly List<float> fps = new List<float>();
        static float startedAt;
        static int mapIdx;
        public static bool Done { get; private set; }

        /// <summary>a new lab run from the chosen map (the menu's RUN ALL MAPS)</summary>
        public static void Begin(string mapId)
        {
            maps.Clear(); heroes.Clear(); casts.Clear(); counters.Clear(); fxKinds.Clear(); sfxIds.Clear(); unknownSfx.Clear(); fps.Clear();
            startedAt = Time.realtimeSinceStartup; Done = false;
            var list = MenuState.PlayMaps(ZuData.Get());
            mapIdx = Math.Max(0, list.FindIndex(m => m.id == mapId));
        }

        static HeroStats Hs(string id) { if (!heroes.TryGetValue(id, out var s)) heroes[id] = s = new HeroStats(); return s; }

        public static void OnEvent(SimEvent e)
        {
            switch (e)
            {
                case CastEvent c: casts.Add(c.id); break;
                case CounterEvent c: counters[$"{c.actor.def.id}>{c.target.def.id}"] = c.text; break;
                case FxEvent f: fxKinds[f.kind] = (fxKinds.TryGetValue(f.kind, out var n) ? n : 0) + 1; break;
                case SfxEvent s:
                    sfxIds[s.id] = (sfxIds.TryGetValue(s.id, out var m) ? m : 0) + 1;
                    try { if (!Audio.AudioKit.Has(s.id)) unknownSfx.Add(s.id); } catch (Exception) { /* no bank */ }
                    break;
            }
        }

        public static void Frame(World w, float dt)
        {
            double t = w.time;
            if (dt > 0) { fps.Add(1 / dt); if (fps.Count > 600) fps.RemoveAt(0); }
            var lib = HeroLibrary.Get();
            foreach (var a in w.actors)
            {
                if (a.isRobot) continue;
                var s = Hs(a.def.id);
                s.rig = lib?.Find(a.def.id) != null ? "rigged" : "mannequin";
                if (!a.alive) { if (t - a.deathAt < 0.5) s.states.Add("death"); continue; }
                s.frames++;
                // the animation states actually exercised
                double sp = Math.Sqrt(a.vel.x * a.vel.x + a.vel.z * a.vel.z);
                var f = a.Forward();
                double fwd = (a.vel.x * f.x + a.vel.z * f.z) / (sp > 0 ? sp : 1);
                if (a.grounded && sp < 0.3) s.states.Add("idle");
                if (a.grounded && sp > 1) s.states.Add(fwd > 0.6 ? "run" : fwd < -0.6 ? "backpedal" : "strafe");
                if (!a.grounded && a.vel.y > 1) s.states.Add("jump");
                if (!a.grounded && a.vel.y < -1) s.states.Add("fall");
                if (t - a.anim.attackAt < 0.05) s.states.Add(a.anim.attackKind == "punch" ? "punch" : "attack");
                if (t - a.anim.castAt < 0.05) s.states.Add("cast");
                if (t - a.anim.hitAt < 0.05) s.states.Add("hit");
                if (a.flying) s.states.Add("fly");
                // physics
                var p = a.pos;
                if (w.level.Collide(ref p, a.ColRadius * 0.8, a.ColHeight)) s.penetrate++;
                double g = w.level.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.5);
                if (a.grounded && g > a.pos.y + 0.15) s.sink++;
                if (a.forced == null && a.grounded && sp > a.def.speed * 1.6 * Math.Max(1, a.scale) + 0.5 && !a.Has("padflight", t)) s.overspeed++;
            }
        }

        public static void EndMap(World w, List<Bot> bots)
        {
            int kills = w.actors.Sum(a => a.kills), deaths = w.actors.Sum(a => a.deaths);
            maps[w.map.id] = new MapResult { secs = Math.Round(w.time), kills = kills, falls = deaths - kills, stuck = bots?.Sum(b => b.stuckCount) ?? 0, winner = w.winner };
        }

        /// <summary>the next map of the run (after the last: the run is done and starts over from the first)</summary>
        public static string NextMap()
        {
            var list = MenuState.PlayMaps(ZuData.Get());
            mapIdx++;
            if (mapIdx >= list.Count) { Done = true; mapIdx = 0; }
            return list[mapIdx].id;
        }

        static IEnumerable<string> AllAbilities()
        {
            foreach (var h in MenuState.Roster(ZuData.Get()))
            {
                if (h.ability1 != null) yield return h.ability1.id;
                if (h.ability2 != null) yield return h.ability2.id;
                if (h.ult != null) yield return h.ult.id;
                if (h.secondary != null && h.secondary.IsAbility && h.secondary.id != "bulwark" && h.secondary.id != "zoom") yield return h.secondary.id;
            }
        }

        public sealed class Abilities { public int cast, of; public List<string> missing; }
        public sealed class Counters { public int seen, of; public Dictionary<string, string> list; }
        public sealed class Effects { public int kinds; public Dictionary<string, int> counts; }
        public sealed class Sounds { public int ids; public List<string> unknown; public Dictionary<string, int> counts; }
        public sealed class Perf { public double avgFps, p5Fps; }
        public sealed class Report
        {
            public bool pass; public List<string> fails = new List<string>();
            public object maps, heroes; public Abilities abilities; public Counters counters; public Effects effects; public Sounds sounds; public Perf perf; public double minutes;
        }

        public static Report MakeReport()
        {
            var r = new Report();
            var hs = new Dictionary<string, object>();
            foreach (var kv in heroes)
            {
                var s = kv.Value; var missing = ANIM_STATES.Where(x => !s.states.Contains(x) && x != "backpedal" && x != "strafe").ToList();
                hs[kv.Key] = new { s.rig, states = s.states.OrderBy(x => x).ToList(), missing, penetrationFrames = s.penetrate, sinkFrames = s.sink, s.overspeed, s.frames };
                if (s.frames > 600 && (double)s.penetrate / s.frames > 0.02) r.fails.Add($"{kv.Key}: wall penetration {(double)s.penetrate / s.frames * 100:0.0}% of frames");
                if (s.frames > 600 && (double)s.sink / s.frames > 0.02) r.fails.Add($"{kv.Key}: sinking into floor");
                if (s.overspeed > Math.Max(30, s.frames * 0.01)) r.fails.Add($"{kv.Key}: moving faster than allowed ({s.overspeed} frames)");
            }
            var all = AllAbilities().ToList();
            var pairs = MenuState.Roster(ZuData.Get()).Select(h => $"{h.id}>{h.rival}").ToList();
            if (unknownSfx.Count > 0) r.fails.Add("sounds without a clip: " + string.Join(", ", unknownSfx));
            var sorted = fps.OrderBy(x => x).ToList();
            r.pass = r.fails.Count == 0; r.maps = maps; r.heroes = hs;
            r.abilities = new Abilities { cast = casts.Count, of = all.Count, missing = all.Where(x => !casts.Contains(x)).ToList() };
            r.counters = new Counters { seen = pairs.Count(counters.ContainsKey), of = pairs.Count, list = counters };
            r.effects = new Effects { kinds = fxKinds.Count, counts = fxKinds };
            r.sounds = new Sounds { ids = sfxIds.Count, unknown = unknownSfx.ToList(), counts = sfxIds };
            r.perf = new Perf { avgFps = Math.Round(fps.Count > 0 ? fps.Average() : 0, 1), p5Fps = Math.Round(sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.05)] : 0, 1) };
            r.minutes = Math.Round((Time.realtimeSinceStartup - startedAt) / 60, 1);
            return r;
        }

        // ------------------------------------------------------------------ the live panel (.lab)
        public static void Render(VisualElement panel)
        {
            var r = MakeReport();
            panel.Clear();
            string Ok(bool b) => b ? "<color=#7dff9a>✔</color>" : "<color=#ff6b81>✖</color>";
            U.Txt($"AI TEST LAB {(r.pass ? "<color=#7dff9a>PASSING</color>" : "<color=#ff6b81>ISSUES</color>")}", "lab-h4", panel);
            var ab = r.abilities; var co = r.counters; var fx = r.effects; var so = r.sounds; var pf = r.perf;
            U.Txt($"Abilities {Ok(ab.missing.Count == 0)} {ab.cast}/{ab.of} · Counters {Ok(co.seen >= 8)} {co.seen}/{co.of} · FX kinds {fx.kinds} · SFX {so.ids} · {pf.avgFps} fps", "lab-s", panel);
            var t = U.Div("lab-t", panel);
            var hr = U.Div("lab-tr th", t);
            foreach (var h in new[] { "hero", "rig", "anim states", "foot slide", "wall pen." }) U.Txt(h, "lab-td", hr);
            foreach (var kv in heroes)
            {
                var s = kv.Value; var missing = ANIM_STATES.Where(x => !s.states.Contains(x) && x != "backpedal" && x != "strafe").ToList();
                var tr = U.Div("lab-tr", t);
                U.Txt(kv.Key, "lab-td", tr); U.Txt(s.rig, "lab-td", tr);
                U.Txt($"{Ok(missing.Count == 0)} {s.states.Count}/{ANIM_STATES.Length}{(missing.Count > 0 ? $" <size=10><alpha=#99>-{string.Join(",", missing)}</alpha></size>" : "")}", "lab-td", tr);
                U.Txt("<alpha=#99>n/a</alpha>", "lab-td", tr);
                U.Txt($"{Ok((double)s.penetrate / Math.Max(1, s.frames) < 0.02)} {s.penetrate}", "lab-td", tr);
            }
            U.Txt(string.Join("\n", maps.Select(m => $"{m.Key}: {m.Value.winner ?? "-"} · {m.Value.kills}K · {m.Value.falls} falls · {m.Value.stuck} unstick")), "lab-maps", panel);
            if (r.fails.Count > 0) U.Txt("<color=#ff6b81>" + string.Join("\n", r.fails.Take(6)) + "</color>", "lab-s", panel);
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "ailab.json"), JsonConvert.SerializeObject(r, Formatting.Indented)); }
            catch (Exception) { /* read-only data folder */ }
        }
    }
}
