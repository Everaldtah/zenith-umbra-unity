// The netcode on real WebRTC (com.unity.webrtc), in the editor's play mode - what tools/nettest can't reach headless:
//   zu_net_selftest [--node <url>]     opens an empty scene, enters play mode and runs:
//     loopback  two PeerLinks in this process, signalled directly: both reach P2P over WebRTC data channels, JSON and
//               binary both ways, pings measure the round trip, getStats names the path
//     node      (with --node: a LOCAL node, e.g. zenith-umbra scripts/net-local.mjs with ZU_GATHER_MS=8000) two
//               OnlineSessions queue, match, link P2P over WebRTC (not the relay), pick, start, and the host streams
//               the match to the client at a direct tier
//   zu_net_selftest_result             the lines so far ("DONE ..." when finished); also Logs/net_selftest.txt
// Then `editor_stop`.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZU.Net.Unity;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Net.EditorTools
{
    [InitializeOnLoad]
    public static class NetSelfTestCommands
    {
        const string KEY = "zu_net_selftest";
        static NetSelfTestCommands()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode) return;
                var args = SessionState.GetString(KEY, "");
                if (args == "") return;
                SessionState.EraseString(KEY);
                var go = new GameObject("ZU.NetSelfTest");
                go.AddComponent<NetSelfTest>().node = args == "-" ? "" : args;
            };
        }

        [CliCommand("zu_net_selftest", "Run the netcode on real WebRTC in play mode: loopback links, and (with --node, a LOCAL node) two sessions matched and streaming")]
        public static string Run([CliArg("node", "a local node URL for the matchmaking phase, e.g. http://localhost:8797/api/net (empty = loopback only)")] string node = "")
        {
            if (EditorApplication.isPlaying) return "already in play mode - editor_stop first";
            if (!string.IsNullOrEmpty(node) && !(node.Contains("localhost") || node.Contains("127.0.0.1"))) return "offline-first: the self-test only talks to a local node";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetString(KEY, string.IsNullOrEmpty(node) ? "-" : node);
            NetSelfTest.Lines.Clear();
            EditorApplication.EnterPlaymode();
            return "entering play mode; poll zu_net_selftest_result";
        }

        [CliCommand("zu_net_selftest_result", "The netcode self-test's lines so far (DONE ... when it has finished)")]
        public static string Result() => NetSelfTest.Lines.Count == 0 ? (File.Exists(NetSelfTest.LogPath) ? File.ReadAllText(NetSelfTest.LogPath) : "(nothing yet)") : string.Join("\n", NetSelfTest.Lines);
    }

    public class NetSelfTest : MonoBehaviour
    {
        public static readonly List<string> Lines = new List<string>();
        public static string LogPath => Path.Combine(Application.dataPath, "../Logs/net_selftest.txt");
        public string node = "";
        int passes, fails;

        void Log(string s) { Lines.Add(s); Debug.Log("[net selftest] " + s); try { File.WriteAllText(LogPath, string.Join("\n", Lines)); } catch { /* ignore */ } }
        void Ok(bool c, string what) { if (c) passes++; else fails++; Log((c ? "ok   " : "FAIL ") + what); }

        IEnumerator Start()
        {
            if (GameData.Current == null)
            {
                string T(string n) => Resources.Load<TextAsset>("ZUData/" + n).text;
                GameData.FromJson(T("heroes"), T("maps"), T("campaign"), T("rules"));
            }
            _ = NetDriver.Instance;                                     // (PlayerPrefs + the WebRTC factory are set at boot)
            Log("webrtc factory: " + (PeerLink.RtcFactory != null));
            yield return Loopback();
            if (!string.IsNullOrEmpty(node)) yield return NodePhase();
            Log($"DONE {passes} passed, {fails} failed");
        }

        static IEnumerator Until(Func<bool> cond, float secs, Action tick)
        {
            float end = Time.realtimeSinceStartup + secs;
            while (Time.realtimeSinceStartup < end && !cond()) { tick(); yield return null; }
        }

        // ---------------------------------------------------------------- two links, signalled directly
        class Direct : ISignaller
        {
            public PeerLink to;
            public void Send(string peer, JObject msg, bool reliable = true)
            {
                var m = (JObject)msg.DeepClone();
                NetDriver.Instance.Post(() => { var t = (string)m["t"]; if (t == "sig") to?.HandleSignal(m); else if (t == "relay") to?.HandleRelay(m); });
            }
        }
        IEnumerator Loopback()
        {
            var sa = new Direct(); var sb = new Direct();
            var a = new PeerLink(sa, "B", "loop", true);
            var b = new PeerLink(sb, "A", "loop", false);
            sa.to = b; sb.to = a;
            var gotA = new List<string>(); var gotB = new List<byte[]>();
            a.OnMessage = m => gotA.Add((string)m["t"]); b.OnBinary = u => gotB.Add(u);
            JObject textAtB = null; b.OnMessage = m => textAtB = m;
            byte[] binAtA = null; a.OnBinary = u => binAtA = u;
            void Tick() { a.Tick(); b.Tick(); }
            float t0 = Time.realtimeSinceStartup;
            yield return Until(() => a.Direct && b.Direct, 20, Tick);
            Ok(a.Direct && b.Direct, $"loopback: both links P2P over WebRTC in {Time.realtimeSinceStartup - t0:0.0} s (a {a.state}, b {b.state})");
            if (!a.Direct || !b.Direct) { a.Close(); b.Close(); yield break; }
            a.Send(new JObject { ["t"] = "hello", ["n"] = "Zénith ✦" });
            b.Send(new JObject { ["t"] = "back" });
            a.SendBin(new byte[] { 2, 7, 7, 7 }); b.SendBin(new byte[] { 1, 9 }, true);
            yield return Until(() => textAtB != null && gotA.Contains("back") && gotB.Any(u => u[0] == 2) && binAtA != null, 5, Tick);
            Ok(textAtB != null && (string)textAtB["n"] == "Zénith ✦", "loopback: JSON on the reliable channel, unicode intact");
            Ok(gotA.Contains("back"), "loopback: JSON the other way");
            Ok(gotB.Any(u => u.Length == 4 && u[0] == 2), "loopback: binary on the fast channel (not mistaken for text)");
            Ok(binAtA != null && binAtA[0] == 1, "loopback: binary on the reliable channel");
            yield return Until(() => a.stats.Measured && a.stats.path != "?", 6, Tick);
            Ok(a.stats.Measured && a.stats.rtt < 100, $"loopback: pings measure the round trip ({a.stats.rtt:0.0} ms)");
            Ok(a.stats.path == "lan", "loopback: getStats names the path: " + a.stats.path);
            a.Close(); b.Close();
        }

        // ---------------------------------------------------------------- two sessions through a local node
        IEnumerator NodePhase()
        {
            var maps = GameData.Current.Maps.Select(m => m.id).ToList();
            var A = new OnlineSession("Alpha", maps, null, node); var B = new OnlineSession("Bravo", maps, null, node);
            OnlineStart stA = null, stB = null; A.OnStart = s => stA = s; B.OnStart = s => stB = s;
            void Tick() { A.Tick(); B.Tick(); }
            yield return Until(() => A.Online && B.Online, 10, Tick);
            Ok(A.Online && B.Online, "node: both sessions reach " + node);
            A.Queue("qp", "flex", 1800); B.Queue("qp", "flex", 1700);
            yield return Until(() => A.Phase == Phase.Assemble && B.Phase == Phase.Assemble, 20, Tick);
            Ok(A.Match != "" && A.Match == B.Match, $"node: matched ({A.Phase}/{B.Phase}) {A.Match}");
            if (A.Match == "" || A.Match != B.Match) { A.Close(); B.Close(); yield break; }
            var host = A.Role == "host" ? A : B; var cli = host == A ? B : A;
            yield return Until(() => host.Links.Values.Any(l => l.Direct) && cli.Links.Values.Any(l => l.Direct), 25, Tick);
            Ok(host.Links.Values.Any(l => l.Direct) && cli.Links.Values.Any(l => l.Direct), "node: host <-> client P2P over WebRTC, signalled through the node: " + string.Join(",", cli.Links.Values.Select(l => l.state)));
            yield return Until(() => cli.MySeat != null, 5, Tick);
            if (cli.MySeat != null) cli.Pick(OnlineSession.HeroPool(cli.MySeat.team, cli.MySeat.role)[0].id);
            yield return Until(() => stA != null && stB != null, 90, Tick);
            Ok(stA != null && stB != null, "node: the host starts at the end of the gather window");
            if (stA == null || stB == null) { A.Close(); B.Close(); yield break; }
            var slots = stA.seats.Select(x => new Setup.OnlineSlot { hero = x.hero, team = x.team, netId = x.id == host.Me ? "local" : x.id }).ToList();
            var hm = Setup.CreateOnlineMatch(stA.map, "quickplay", slots, 0.5);
            var fh = new FastHost(hm.world, host);
            var cw = new World(stA.map, "quickplay"); cw.nav = new BoxNav((BoxLevel)cw.level);
            var fc = new FastClient(cw, cli, e => { }, null);
            double acc = 0; float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end)
            {
                Tick();
                acc += Time.unscaledDeltaTime;
                while (acc >= 1.0 / 120) { fh.BeforeStep(); hm.world.Step(1.0 / 120); fh.AfterStep(); fh.Capture(hm.world.events); hm.world.events.Clear(); acc -= 1.0 / 120; }
                fh.Flush(); fc.Apply(Time.unscaledDeltaTime, new SimInput { mz = 1 });
                yield return null;
            }
            Ok(cw.actors.Count == 10, "node: the client mirrors all 10 heroes over WebRTC: " + cw.actors.Count);
            Ok(fc.tier != "relay" && fc.snapHz > 15, $"node: a direct tier ({fc.tier}, {fc.snapHz:0} snapshots/s)");
            var hl = fh.Info(cli.Me);
            Ok(hl != null && hl.path == "lan", $"node: the host's readout: {hl?.tier} {hl?.path} {hl?.rtt} ms {hl?.kbps} kbit/s");
            A.Close(); B.Close();
        }
    }
}
