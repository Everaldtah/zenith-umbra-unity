// `zu_ult_shots`: the Ult Viewer (UltShowcase) for one hero, captured at fixed simulation times after the cast - the Unity
// half of the ult-by-ult comparison with the web game's Ult Viewer (the same hero, the same moments, the same camera
// routine). Play mode must be on; the command opens the showcase and returns, the shots land as the ult plays (an editor
// update hook), and `zu_ult_shots_status` reports progress. Shots: <out>/<hero>_<n>.png.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using ZU.Game;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.EditorTools
{
    public static class UltShots
    {
        sealed class Job { public string hero, outDir; public float[] times; public int w, h, next; public double t0 = -1, opened; public bool done; public string log = ""; }
        static Job job;

        [CliCommand("zu_ult_shots", "Run the Ult Viewer for a hero (play mode) and capture it at fixed sim times after the cast")]
        public static string Shots(
            [CliArg("hero", "hero id")] string hero,
            [CliArg("times", "seconds after the cast, comma separated")] string times = "0.25,1.15,2.45,4.05",
            [CliArg("out", "folder, relative to the project")] string output = "Screenshots/ult",
            [CliArg("width", "pixels")] int width = 1280,
            [CliArg("height", "pixels")] int height = 720)
        {
            if (!Application.isPlaying) return "enter play mode first (editor_play)";
            var ts = times.Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).OrderBy(x => x).ToArray();
            Directory.CreateDirectory(Path.GetFullPath(output));
            EditorApplication.update -= Tick;
            job = new Job { hero = hero, outDir = output, times = ts, w = width, h = height, opened = EditorApplication.timeSinceStartup };
            UltShowcase.Open(hero);
            EditorApplication.update += Tick;
            return $"started {hero}: {ts.Length} shots at {times}";
        }

        [CliCommand("zu_ult_shots_status", "Progress of the last zu_ult_shots run")]
        public static string Status() => job == null ? "idle" : $"{job.hero}: {(job.done ? "done" : job.t0 < 0 ? "waiting for the cast" : "shooting")} {job.next}/{job.times.Length}{job.log}";

        static void Tick()
        {
            var j = job;
            if (j == null || j.done) { EditorApplication.update -= Tick; return; }
            if (!Application.isPlaying) { j.done = true; j.log += "; play mode ended"; return; }
            if (EditorApplication.timeSinceStartup - j.opened > 90) { j.done = true; j.log += "; timed out"; return; }
            var s = UltShowcase.Current;
            if (s == null || s.HeroId != j.hero) return;
            var r = Object.FindAnyObjectByType<MatchRunner>();
            if (r == null || r.World == null) return;
            double t = r.World.time;
            if (j.t0 < 0) { if (s.Progress < 1) j.t0 = t; else return; }        // the cast: the replay bar starts draining
            while (j.next < j.times.Length && t - j.t0 >= j.times[j.next])
            {
                string path = $"{j.outDir}/{j.hero}_{j.next + 1}.png";
                var res = ZuCapture.Capture(path, j.w, j.h);
                if (!res.StartsWith(j.outDir)) j.log += "; " + res;
                j.next++;
            }
            if (j.next >= j.times.Length) { j.done = true; EditorApplication.update -= Tick; }
        }

        // ---- `zu_death_shots`: a ragdoll death, measured. Kills a living hero of the given id in the running match (a
        // killing blow from an enemy, so the fling is a real one), then at fixed seconds after the death shoots the body from
        // the side and logs each leg: the thigh and shin directions' world-up component (a leg pointing at the sky reads
        // ~+1, lying flat ~0), the feet's height over the pelvis, and whether the ragdoll sleeps. Shots:
        // <out>/<hero>_death<n>.png; the readings come back from `zu_death_shots_status`.
        sealed class Death { public string hero, outDir; public float[] times; public int id, next; public double t0; public bool done; public string log = ""; }
        static Death death;

        [CliCommand("zu_death_shots", "Kill a hero in the running match and shoot + measure its ragdoll at fixed seconds after the death")]
        public static string DeathShots(
            [CliArg("hero", "hero id")] string hero,
            [CliArg("times", "seconds after the death, comma separated")] string times = "0.15,0.4,0.8,1.3,2.0,2.5",
            [CliArg("out", "folder, relative to the project")] string output = "Screenshots/death")
        {
            if (!Application.isPlaying) return "enter play mode first (editor_play)";
            var r = Object.FindAnyObjectByType<MatchRunner>();
            if (r == null || r.World == null) return "no match";
            var w = r.World;
            var tgt = w.actors.FirstOrDefault(a => a.alive && a.def.id == hero && !a.isRobot);
            if (tgt == null) return "no living " + hero + " - in the match: " + string.Join(",", w.actors.Where(a => a.alive && !a.isRobot).Select(a => a.def.id).Distinct());
            var src = w.actors.FirstOrDefault(a => a.alive && a.team != tgt.team);
            foreach (var k in new[] { "spawnprot", "phased", "reborn", "parry", "deflect" }) tgt.Clear(k);
            w.Damage(src, tgt, tgt.hp + tgt.armor + 400, new DmgOpts { kind = "hitscan" });
            if (tgt.alive) w.Kill(tgt, src);
            var ts = times.Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).OrderBy(x => x).ToArray();
            Directory.CreateDirectory(Path.GetFullPath(output));
            EditorApplication.update -= DeathTick;
            death = new Death { hero = hero, outDir = output, times = ts, id = tgt.id, t0 = tgt.deathAt };
            EditorApplication.update += DeathTick;
            return $"killed {hero} #{tgt.id} (by {src?.def.id ?? "nobody"}, alive {tgt.alive}); {ts.Length} shots at {times}";
        }

        [CliCommand("zu_death_shots_status", "Progress + leg readings of the last zu_death_shots run")]
        public static string DeathStatus() => death == null ? "idle" : $"{death.hero}: {(death.done ? "done" : "shooting")} {death.next}/{death.times.Length}{death.log}";

        static void DeathTick()
        {
            var j = death;
            if (j == null || j.done) { EditorApplication.update -= DeathTick; return; }
            if (!Application.isPlaying) { j.done = true; j.log += "; play mode ended"; return; }
            var r = Object.FindAnyObjectByType<MatchRunner>(); if (r == null || r.World == null) return;
            double t = r.World.time - j.t0;
            if (j.next >= j.times.Length || t < j.times[j.next]) return;
            HeroView view = null;
            foreach (var v in Object.FindObjectsByType<HeroView>(FindObjectsSortMode.None))
            {
                int h = v.name.IndexOf(" #"); if (h < 0) continue;
                int e = v.name.IndexOf(' ', h + 2);
                if (int.TryParse(e > 0 ? v.name.Substring(h + 2, e - h - 2) : v.name.Substring(h + 2), out var aid) && aid == j.id) { view = v; break; }
            }
            var anim = view != null ? view.GetComponentInChildren<Animator>() : null;
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null) { j.log += $"; t{t:0.00} no body"; j.next++; return; }
            string Leg(HumanBodyBones up, HumanBodyBones lo, HumanBodyBones ft)
            {
                Transform a = anim.GetBoneTransform(up), b = anim.GetBoneTransform(lo), c = anim.GetBoneTransform(ft);
                if (a == null || b == null || c == null) return "?";
                return $"thigh {(b.position - a.position).normalized.y:+0.00;-0.00} shin {(c.position - b.position).normalized.y:+0.00;-0.00} foot {c.position.y - hips.position.y:+0.00;-0.00}";
            }
            var hp = hips.position;
            var res = ZuCapture.Capture($"{j.outDir}/{j.hero}_death{j.next + 1}.png", 960, 540,
                $"{(hp.x + 2.6f).ToString(CultureInfo.InvariantCulture)},{(hp.y + 1.4f).ToString(CultureInfo.InvariantCulture)},{(hp.z + 2.6f).ToString(CultureInfo.InvariantCulture)}",
                $"{hp.x.ToString(CultureInfo.InvariantCulture)},{hp.y.ToString(CultureInfo.InvariantCulture)},{hp.z.ToString(CultureInfo.InvariantCulture)}", 50);
            if (!res.StartsWith(j.outDir)) j.log += "; " + res;
            j.log += $"; t{t:0.00} L[{Leg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot)}] R[{Leg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)}]";
            j.next++;
            if (j.next >= j.times.Length) { j.done = true; EditorApplication.update -= DeathTick; }
        }
    }
}
