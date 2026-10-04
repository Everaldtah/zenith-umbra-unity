// `zu_anim_probe`: the animation quality numbers the TS measures (tests/unit/animsim.test.ts, the AI Test Lab), taken in a
// running match (play mode) over a stretch of seconds, per hero:
//  - slide: planted-foot drift / body speed (the TS metric: a planted foot's world XZ movement per second over the body's
//    speed, while alive, grounded, faster than 1 m/s and not forced) - "planted" = the clip layer's contact > 0.5
//  - clipShare: frames the clip layer drives the legs (Result.ok && legs > 0.5)
//  - pops: the largest per-frame bone rotation (degrees) while moving without a one-shot running - blend-space pops show up
//    as spikes; p99 and the count of frames over 20 degrees
//  - turn: foot drift (m/s) while standing (speed < 0.4) and turning faster than 1.5 rad/s - feet should re-step, not skate
//  - land: the largest per-frame bone rotation in the 0.25 s after a landing
//  - actions: the clip one-shots seen
// `zu_anim_probe_status` reports progress; the result is written to Screenshots/a1/anim_probe.json.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using ZU.Game;
using ZU.Sim;

namespace ZU.EditorTools
{
    public static class AnimProbe
    {
        sealed class S
        {
            public string id; public double slide; public int n, frames, clip; public readonly List<float> pops = new List<float>();
            public double turnDrift; public int turnN; public float land; public readonly HashSet<string> actions = new HashSet<string>();
        }
        sealed class Track { public Vector3[] foot = new Vector3[2]; public bool[] planted = new bool[2]; public bool has; public Quaternion[] rot; public double lastLand = -9, yaw; }
        static readonly HumanBodyBones[] POSE = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg };
        static readonly Dictionary<string, S> stats = new Dictionary<string, S>();
        static readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        static double until, started; static bool running; static string result = "idle";

        [CliCommand("zu_anim_probe", "Measure animation quality (foot slide, pops, turning, landing, clip share) over a stretch of a running match")]
        public static string Probe([CliArg("secs", "seconds of play to measure")] float secs = 45)
        {
            if (!Application.isPlaying) return "enter play mode first";
            stats.Clear(); tracks.Clear();
            started = EditorApplication.timeSinceStartup; until = started + secs; running = true; result = "running";
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            return $"probing for {secs} s";
        }

        [CliCommand("zu_anim_probe_status", "Progress / result of the last zu_anim_probe")]
        public static string Status() => running ? $"running, {until - EditorApplication.timeSinceStartup:0} s left" : result;

        static void Tick()
        {
            if (!running) { EditorApplication.update -= Tick; return; }
            if (!Application.isPlaying || EditorApplication.timeSinceStartup >= until) { Finish(); return; }
            var r = Object.FindAnyObjectByType<MatchRunner>();
            if (r == null || r.World == null) return;
            var w = r.World; float dt = Time.deltaTime; if (dt <= 0) return;
            foreach (var v in Object.FindObjectsByType<HeroView>(FindObjectsSortMode.None))
            {
                int h = v.name.IndexOf(" #"); if (h < 0) continue;
                int e = v.name.IndexOf(' ', h + 2);
                if (!int.TryParse(e > 0 ? v.name.Substring(h + 2, e - h - 2) : v.name.Substring(h + 2), out var aid)) continue;
                var a = w.ById(aid); if (a == null || a.isRobot) continue;
                var anim = v.GetComponentInChildren<Animator>(); if (anim == null || !anim.isHuman) continue;
                if (!stats.TryGetValue(a.def.id, out var s)) stats[a.def.id] = s = new S { id = a.def.id };
                if (!tracks.TryGetValue(aid, out var tr)) tracks[aid] = tr = new Track { rot = new Quaternion[POSE.Length] };
                var L = v.Clips?.Result;
                if (L != null && !string.IsNullOrEmpty(L.action)) s.actions.Add(L.action);
                if (!a.alive) { tr.has = false; continue; }
                s.frames++;
                if (L != null && L.ok && L.legs > 0.5f) s.clip++;
                // the pose this frame (model space) and its biggest per-bone jump since the last
                var inv = Quaternion.Inverse(v.transform.rotation); float jump = 0;
                for (int i = 0; i < POSE.Length; i++)
                {
                    var b = anim.GetBoneTransform(POSE[i]); if (b == null) continue;
                    var q = inv * b.rotation;
                    if (tr.has) jump = Mathf.Max(jump, Quaternion.Angle(tr.rot[i], q));
                    tr.rot[i] = q;
                }
                float sp = (float)System.Math.Sqrt(a.vel.x * a.vel.x + a.vel.z * a.vel.z);
                double yawRate = tr.has ? System.Math.Abs(DeltaYaw(a.yaw, tr.yaw)) / dt : 0;
                bool oneShot = L != null && !string.IsNullOrEmpty(L.action);
                if (tr.has && a.grounded && sp > 1 && !oneShot) s.pops.Add(jump);
                if (a.anim.landAt > tr.lastLand) tr.lastLand = a.anim.landAt;
                if (w.time - tr.lastLand < 0.25) s.land = Mathf.Max(s.land, jump);
                // feet: the TS slide metric, and skating while turning on the spot
                for (int i = 0; i < 2; i++)
                {
                    var f = anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot); if (f == null) continue;
                    var p = f.position; bool planted = L != null && L.ok && L.contact[i] > 0.5f;
                    if (tr.has && planted && tr.planted[i])
                    {
                        float d = new Vector2(p.x - tr.foot[i].x, p.z - tr.foot[i].z).magnitude;
                        if (a.grounded && sp > 1 && a.forced == null) { s.slide += d / dt / sp; s.n++; }
                        if (a.grounded && sp < 0.4f && yawRate > 1.5) { s.turnDrift += d / dt; s.turnN++; }
                    }
                    tr.foot[i] = p; tr.planted[i] = planted;
                }
                tr.yaw = a.yaw; tr.has = true;
            }
        }

        static double DeltaYaw(double a, double b) { double d = a - b; while (d > System.Math.PI) d -= 2 * System.Math.PI; while (d < -System.Math.PI) d += 2 * System.Math.PI; return d; }

        static void Finish()
        {
            running = false; EditorApplication.update -= Tick;
            var rows = stats.Values.OrderBy(s => s.id).Select(s =>
            {
                var pops = s.pops.OrderBy(x => x).ToList();
                float p99 = pops.Count > 0 ? pops[Mathf.Min(pops.Count - 1, (int)(pops.Count * 0.99f))] : 0;
                return $"{{\"id\":\"{s.id}\",\"slide\":{(s.n > 0 ? s.slide / s.n : 0):0.000},\"samples\":{s.n},\"clipShare\":{(s.frames > 0 ? (float)s.clip / s.frames : 0):0.00}," +
                       $"\"popP99\":{p99:0.0},\"pops20\":{pops.Count(x => x > 20)},\"popFrames\":{pops.Count},\"turnDrift\":{(s.turnN > 0 ? s.turnDrift / s.turnN : 0):0.000},\"turnSamples\":{s.turnN}," +
                       $"\"landJump\":{s.land:0.0},\"actions\":\"{string.Join(",", s.actions.OrderBy(x => x))}\"}}";
            });
            string json = "[\n" + string.Join(",\n", rows) + "\n]";
            Directory.CreateDirectory("Screenshots/a1");
            File.WriteAllText("Screenshots/a1/anim_probe.json", json);
            result = "done: " + json.Replace("\n", " ");
        }
    }
}
