// `zu_killcam_test`: the kill cam, exercised without a person at the keyboard. In a running match with a local player
// (e.g. MatchSettings.Start("hanabi", "kaien", "quickplay", 0.7f, true) with Autopilot on) it waits until the record has
// some seconds in it, has the nearest enemy deal the player a killing blow (a real kill event, so the cam starts by
// itself), and while the replay runs captures the main camera every `every` seconds and logs the cam's state, where the
// lens is from the victim as it was, and whether the victim's record shows it standing or fallen.
// `zu_killcam_test_status` returns the log. Shots: <out>/killcam_<n>.png.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using ZU.Game;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.EditorTools
{
    public static class KillCamTest
    {
        struct ZuKillCamTest { }
        sealed class Job { public string outDir; public float every; public double nextShot, opened, killedAt; public int shots, phase; public bool done; public readonly List<string> log = new List<string>(); }
        static Job job;
        static readonly CultureInfo INV = CultureInfo.InvariantCulture;

        [CliCommand("zu_killcam_test", "Kill the local player in the running match and capture the kill cam's replay")]
        public static string Test(
            [CliArg("out", "folder, relative to the project")] string output = "Screenshots/killcam",
            [CliArg("every", "seconds between shots during the replay")] float every = 0.5f)
        {
            if (!Application.isPlaying) return "enter play mode first";
            var r = Object.FindAnyObjectByType<MatchRunner>();
            if (r == null || r.World == null || r.Player == null) return "no match with a local player (start one: MatchSettings.Start(\"hanabi\", \"kaien\", \"quickplay\", 0.7f, true))";
            Directory.CreateDirectory(Path.GetFullPath(output));
            job = new Job { outDir = output, every = every, opened = EditorApplication.timeSinceStartup };
            Hook(true);
            return $"waiting for the record to fill, then killing {r.Player.def.id}";
        }

        [CliCommand("zu_killcam_test_status", "Log of the last zu_killcam_test")]
        public static string Status() => job == null ? "idle" : (job.done ? "done" : "running") + $" ({job.shots} shots): " + string.Join(" | ", job.log);

        static void Hook(bool on)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var sys = root.subSystemList[i];
                var list = new List<PlayerLoopSystem>(sys.subSystemList ?? new PlayerLoopSystem[0]);
                list.RemoveAll(x => x.type == typeof(ZuKillCamTest));
                if (on) list.Add(new PlayerLoopSystem { type = typeof(ZuKillCamTest), updateDelegate = Frame });
                sys.subSystemList = list.ToArray(); root.subSystemList[i] = sys;
            }
            PlayerLoop.SetPlayerLoop(root);
        }

        static void Finish(string why) { var j = job; if (j == null) return; j.done = true; if (why != null) j.log.Add(why); Hook(false); }

        static void Frame()
        {
            var j = job;
            if (j == null || j.done) return;
            if (!Application.isPlaying) { Finish("play mode ended"); return; }
            if (EditorApplication.timeSinceStartup - j.opened > 120) { Finish("timed out"); return; }
            var r = Object.FindAnyObjectByType<MatchRunner>();
            var kc = KillCam.Current;
            if (r == null || r.World == null || kc == null) return;
            var w = r.World; var me = r.Player;
            if (me == null) return;
            if (j.phase == 0)
            {
                // some seconds of record with the player alive, then the blow
                if (w.time < 8 || !me.alive) return;
                var foe = w.actors.Where(a => a.alive && a.team != me.team && !a.IsSummon).OrderBy(a => World.Dist3(a.pos, me.pos)).FirstOrDefault();
                foreach (var k in new[] { "spawnprot", "phased", "reborn", "parry", "deflect", "undying" }) me.Clear(k);
                w.Damage(foe, me, me.hp + me.armor + 2000, new DmgOpts { kind = "hitscan" });
                if (me.alive) w.Kill(me, foe);
                j.killedAt = w.time; j.phase = 1;
                j.log.Add($"killed {me.def.id} with {foe?.def.id ?? "nobody"} at t {w.time.ToString("0.0", INV)} (respawn in {(me.respawnAt - w.time).ToString("0.0", INV)} s)");
                return;
            }
            var play = KillCam.Replay(r);
            if (j.phase == 1)
            {
                if (play == null) { if (w.time - j.killedAt > 3) Finish("the replay never started: " + kc.Diag); return; }
                j.phase = 2; j.nextShot = 0; j.log.Add("replay started: " + kc.Diag);
            }
            if (play == null) { Finish("replay over: " + kc.Diag + (me.alive ? "; player alive again" : "")); return; }
            if (j.shots == 0 || play.SimTime >= j.nextShot)
            {
                j.nextShot = play.SimTime + j.every;
                var cam = Camera.main; var past = play.Past(me);
                string state = past == null ? "victim not in the record" : $"victim {(past.alive ? "standing" : "down")}, lens {Vector3.Distance(cam.transform.position, play.DrawPos(past)).ToString("0.0", INV)} m from it";
                var res = ZuCapture.Capture($"{j.outDir}/killcam_{j.shots:00}.png", 960, 540);
                j.log.Add($"t{(play.SimTime - j.killedAt).ToString("+0.00;-0.00", INV)} {state}{(res.StartsWith(j.outDir) ? "" : " (" + res + ")")}");
                j.shots++;
            }
        }
    }
}
