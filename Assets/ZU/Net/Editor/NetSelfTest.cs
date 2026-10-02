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
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZU.Net.Unity;

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
}
