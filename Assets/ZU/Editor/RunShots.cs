// `zu_run_strip`: a hero's run, frame by frame - the arm carriage measured the way tools/runstudy measures the source clips
// and the Overwatch footage (docs/research/run_arm_study.md), and shot from behind and from the side.
// Play mode on the animation bench (MatchSettings.Start("training", hero, "gallery", ...): DemoRoutine walks the hero
// through idle / run / strafe / ...). The command waits for the routine's "run" step, then every frame until the step ends
// records, per arm, the hand's offset from its shoulder along the body's forward / up / outward axes (in arm lengths) and
// the elbow's flexion, plus the torso's lean; every `every`-th frame is captured. `zu_run_strip_status` returns the
// summary: mean [min..max] per arm, and the share of the run with BOTH hands behind the shoulders (the fault the user saw).
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using ZU.Game;
using ZU.Sim;

namespace ZU.EditorTools
{
    public static class RunShots
    {
        struct ZuRunStrip { }
        sealed class Job
        {
            public string hero, outDir, want; public int every, frame, shots; public double opened; public bool done, began; public string log = "";
            public readonly List<float[]> rows = new List<float[]>();      // L fwd, up, out, flex, R fwd, up, out, flex, lean
        }
        static Job job;
        static readonly CultureInfo INV = CultureInfo.InvariantCulture;

        [CliCommand("zu_run_strip", "Measure and shoot a hero's run on the animation bench (gallery mode), frame by frame")]
        public static string Strip(
            [CliArg("hero", "hero id (the bench's hero: start it with MatchSettings.Start(\"training\", hero, \"gallery\", 0.7f, true))")] string hero,
            [CliArg("out", "folder, relative to the project")] string output = "Screenshots/runstrip",
            [CliArg("every", "capture every n-th frame (0 = measure only)")] int every = 4,
            [CliArg("step", "the bench step to measure: run | strafe | back")] string step = "run")
        {
            if (!Application.isPlaying) return "enter play mode first";
            Directory.CreateDirectory(Path.GetFullPath(output));
            job = new Job { hero = hero, outDir = output, every = every, want = step, opened = EditorApplication.timeSinceStartup };
            Hook(true);
            return $"waiting for {hero}'s {step} step";
        }

        [CliCommand("zu_run_strip_status", "Progress / summary of the last zu_run_strip")]
        public static string Status()
        {
            var j = job; if (j == null) return "idle";
            if (!j.done) return $"{j.hero}: {(j.began ? "measuring" : "waiting")} {j.rows.Count} frames{j.log}";
            if (j.rows.Count == 0) return $"{j.hero}: done, no frames{j.log}";
            string Col(int c) { var v = j.rows.Select(r => r[c]).ToList(); return $"{v.Average().ToString("+0.00;-0.00", INV)} [{v.Min().ToString("+0.00;-0.00", INV)}..{v.Max().ToString("+0.00;-0.00", INV)}]"; }
            float behind = j.rows.Count(r => r[0] < -0.1f && r[4] < -0.1f) / (float)j.rows.Count;
            return $"{j.hero}: done {j.rows.Count} frames, {j.shots} shots | L hand fwd {Col(0)} up {Col(1)} out {Col(2)} flex {Col(3)} | R hand fwd {Col(4)} up {Col(5)} out {Col(6)} flex {Col(7)} | lean {Col(8)} deg | both hands behind {(behind * 100).ToString("0", INV)}%{j.log}";
        }

        static void Hook(bool on)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var sys = root.subSystemList[i];
                var list = new List<PlayerLoopSystem>(sys.subSystemList ?? new PlayerLoopSystem[0]);
                list.RemoveAll(x => x.type == typeof(ZuRunStrip));
                if (on) list.Add(new PlayerLoopSystem { type = typeof(ZuRunStrip), updateDelegate = Frame });
                sys.subSystemList = list.ToArray(); root.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(root);
        }

        static void Finish(string why) { var j = job; if (j == null) return; j.done = true; if (why != null) j.log += "; " + why; Hook(false); }

        static void Frame()
        {
            var j = job;
            if (j == null || j.done) return;
            if (!Application.isPlaying) { Finish("play mode ended"); return; }
            if (EditorApplication.timeSinceStartup - j.opened > 90) { Finish("timed out"); return; }
            var r = Object.FindAnyObjectByType<MatchRunner>();
            if (r == null || r.World == null || r.World.actors.Count == 0) return;
            var a = r.World.actors[0];
            if (a.baseDef.id != j.hero || !(a.controller is DemoRoutine demo)) return;
            if (demo.step != j.want) { if (j.began) Finish(null); return; }
            HeroView view = null;
            foreach (var v in Object.FindObjectsByType<HeroView>(FindObjectsSortMode.None)) if (v.name.Contains(" #" + a.id)) { view = v; break; }
            var anim = view != null ? view.GetComponentInChildren<Animator>() : null;
            if (anim == null || !anim.isHuman) { Finish("no humanoid view"); return; }
            j.began = true;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips); var chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            // the body's axes from the hero's facing (the view's root turns with the yaw)
            Vector3 fwd = view.transform.forward, right = view.transform.right, up = Vector3.up;
            var row = new float[9];
            for (int i = 0; i < 2; i++)
            {
                Transform ua = B(i == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), la = B(i == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                    h = B(i == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (ua == null || la == null || h == null) { Finish("arm bones missing"); return; }
                float L = Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, h.position);
                var d = h.position - ua.position;
                row[i * 4] = Vector3.Dot(d, fwd) / L; row[i * 4 + 1] = d.y / L; row[i * 4 + 2] = Vector3.Dot(d, i == 0 ? -right : right) / L;
                row[i * 4 + 3] = Vector3.Angle(la.position - ua.position, h.position - la.position);
            }
            var sp = chest.position - hips.position;
            row[8] = Mathf.Atan2(Vector3.Dot(sp, fwd), sp.y) * Mathf.Rad2Deg;
            j.rows.Add(row);
            if (j.every > 0 && j.frame % j.every == 0 && j.shots < 16)
            {
                var c = hips.position;
                string V(Vector3 p) => $"{p.x.ToString(INV)},{p.y.ToString(INV)},{p.z.ToString(INV)}";
                ZuCapture.Capture($"{j.outDir}/{j.hero}_back_{j.shots:00}.png", 360, 480, V(c - fwd * 3.0f + up * 0.5f), V(c + up * 0.1f), 40);
                ZuCapture.Capture($"{j.outDir}/{j.hero}_side_{j.shots:00}.png", 360, 480, V(c + right * 3.0f + up * 0.3f), V(c + up * 0.1f), 40);
                j.shots++;
            }
            j.frame++;
        }
    }
}
