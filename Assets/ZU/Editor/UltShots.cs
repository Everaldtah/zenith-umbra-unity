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
    }
}
