// The one place the online node's address lives, the clocks the netcode runs on, and a small key-value store for what the
// TS kept in localStorage (the last connection test). The Unity layer (ZU.Net.Unity.NetDriver) backs the store with
// PlayerPrefs; headless tests leave it in memory.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZU.Net
{
    public static class NetConfig
    {
        /// <summary>offline-first (the user's rule for the Unity edition): a local node (zenith-umbra scripts/net-local.mjs)
        /// until the user has tried online play and chooses otherwise - never the live Vercel node by default</summary>
        public const string DEFAULT_NODE = "http://localhost:8788/api/net";
        const string KEY = "zu-net-node";

        /// <summary>persistent key-value storage (PlayerPrefs in the game; memory headless)</summary>
        public static Func<string, string> Load = k => mem.TryGetValue(k, out var v) ? v : null;
        public static Action<string, string> Save = (k, v) => mem[k] = v;
        static readonly Dictionary<string, string> mem = new Dictionary<string, string>();

        /// <summary>the online node's /api/net endpoint (get / set, persisted)</summary>
        public static string NodeUrl
        {
            get { var v = Load(KEY); return string.IsNullOrWhiteSpace(v) ? DEFAULT_NODE : v.Trim(); }
            set => Save(KEY, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
        }
    }

    /// <summary>the netcode's clocks: Now = monotonic ms (TS performance.now()), Epoch = wall-clock ms (TS Date.now()).
    /// Tests drive both through Override.</summary>
    public static class Clock
    {
        static readonly Stopwatch sw = Stopwatch.StartNew();
        /// <summary>a fake clock for tests: (monotonic ms, epoch ms)</summary>
        public static Func<(double now, long epoch)> Override;
        public static double Now => Override != null ? Override().now : sw.Elapsed.TotalMilliseconds;
        public static long Epoch => Override != null ? Override().epoch : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>random ids in the TS alphabet (no look-alike letters)</summary>
    public static class Rid
    {
        static readonly Random rng = new Random();
        const string A = "abcdefghijkmnpqrstuvwxyz23456789";
        public static string Make(int n = 10) { lock (rng) { var c = new char[n]; for (int i = 0; i < n; i++) c[i] = A[rng.Next(32)]; return new string(c); } }
        /// <summary>TS `Math.random().toString(36).slice(2, 10)`</summary>
        public static string Short() { const string B = "0123456789abcdefghijklmnopqrstuvwxyz"; lock (rng) { var c = new char[8]; for (int i = 0; i < 8; i++) c[i] = B[rng.Next(36)]; return new string(c); } }
        public static double Random() { lock (rng) return rng.NextDouble(); }
    }
}
