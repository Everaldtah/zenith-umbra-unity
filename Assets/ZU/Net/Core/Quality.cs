// Fast Link: how good is a connection, and how much game state should go over it. Port of zenith-umbra src/net/quality.ts.
//  - LinkStats: round-trip time, jitter and loss of one peer link (binary pings on the unreliable channel), plus the
//    bytes actually sent / received and WebRTC's own bandwidth estimate (getStats availableOutgoingBitrate)
//  - tiers: the update rate a link gets (snapshots per second from the host, inputs per second to it). A link drops a
//    tier at once when it degrades (loss, latency spikes, a send queue building up) and climbs back only after it has
//    been clean for a while, so the rate doesn't flap
//  - SpeedTest(): ping / download / upload against the online node, turned into a host score (the matchmaker gives the
//    match to the player with the best uplink)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ZU.Net
{
    public class TierSpec { public string label; public int snapHz, inputHz; public string color; }

    public static class Quality
    {
        public static readonly string[] TIERS = { "ultra", "high", "medium", "low", "relay" };
        public static readonly Dictionary<string, TierSpec> TIER = new Dictionary<string, TierSpec>
        {
            ["ultra"] = new TierSpec { label = "ULTRA", snapHz = 60, inputHz = 60, color = "#58ffb0" },
            ["high"] = new TierSpec { label = "HIGH", snapHz = 40, inputHz = 60, color = "#9cff6b" },
            ["medium"] = new TierSpec { label = "MEDIUM", snapHz = 30, inputHz = 40, color = "#ffd25a" },
            ["low"] = new TierSpec { label = "LOW", snapHz = 20, inputHz = 30, color = "#ff9a4a" },
            ["relay"] = new TierSpec { label = "RELAY", snapHz = 8, inputHz = 15, color = "#ff5d6d" },
        };
        public static int TierCode(string t) => Array.IndexOf(TIERS, t);
        public static string TierOf(int c) => TIERS[Math.Max(0, Math.Min(TIERS.Length - 1, c))];

        /// <summary>the best tier a link's numbers allow (before hysteresis and bandwidth limits)</summary>
        public static string Classify(double rtt, double jitter, double loss, bool relay = false)
        {
            if (relay) return "relay";
            if (loss > 0.12 || rtt > 260 || jitter > 60) return "low";
            if (loss > 0.05 || rtt > 150 || jitter > 30) return "medium";
            if (loss > 0.015 || rtt > 80 || jitter > 14) return "high";
            return "ultra";
        }

        /// <summary>the fastest tier whose snapshot stream fits in `kbps` (bytes per snapshot known)</summary>
        public static string FitTier(double kbps, double snapBytes, string cap = "ultra")
        {
            for (int i = TierCode(cap); i < TIERS.Length - 1; i++)
            {
                var t = TIERS[i];
                if (TIER[t].snapHz * (snapBytes + 48) * 8 / 1000 <= kbps) return t;     // (+48: SCTP/DTLS/UDP/IP overhead)
            }
            return "low";
        }

        /// <summary>The host score (0..1000): uplink matters most (the host streams the match to everyone), then latency to
        /// the node (a stand-in for latency to the other players), then downlink, the desktop app and CPU cores.</summary>
        public static int HostScore(double rtt, double upKbps, double downKbps, bool desktop, int cores = 4)
        {
            double up = Math.Min(upKbps, 30000) / 30000 * 480, down = Math.Min(downKbps, 60000) / 60000 * 120;
            double lat = Math.Max(0, 250 - rtt) / 250 * 250;
            return (int)K.Round(Math.Max(0, Math.Min(1000, up + down + lat + (desktop ? 100 : 0) + Math.Min(50, cores * 4))));
        }

        // ---------------------------------------------------------------- connection test against the online node
        const string SPEED_KEY = "zu-net-speed";
        public static SpeedResult CachedSpeed(long maxAgeMs = 15 * 60_000)
        {
            try
            {
                var s = NetConfig.Load(SPEED_KEY); if (string.IsNullOrEmpty(s)) return null;
                var r = JsonConvert.DeserializeObject<SpeedResult>(s);
                // (rtt 999 = a test that reached no node, saved before tests like that stopped being kept)
                return r != null && r.rtt < 999 && Clock.Epoch - r.at < maxAgeMs ? r : null;
            }
            catch { return null; }
        }

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        public static async Task<SpeedResult> SpeedTest(string url, bool desktop, Action<string> onStep = null)
        {
            double T() => Clock.Now;
            string sep = url.Contains("?") ? "&" : "?";
            onStep?.Invoke("ping");
            var pings = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                double t0 = T();
                try { var s = await http.GetStringAsync($"{url}{sep}ping=1&n={i}"); JsonConvert.DeserializeObject(s); pings.Add(T() - t0); } catch { /* lost */ }
            }
            var ps = pings.Skip(1).OrderBy(x => x).ToList();                  // (the first may pay for a cold start)
            double rtt = ps.Count > 0 ? ps[ps.Count / 2] : 999;
            double jitter = 0; for (int i = 1; i < ps.Count; i++) jitter += Math.Abs(ps[i] - ps[i - 1]); if (ps.Count > 1) jitter /= ps.Count - 1;
            onStep?.Invoke("download");
            double downKbps = 0;
            try
            {
                int n = 393216; double t0 = T();
                var b = await http.GetByteArrayAsync($"{url}{sep}bw={n}");
                downKbps = b.Length * 8 / Math.Max(1, T() - t0 - rtt * 0.5);
            }
            catch { /* offline */ }
            onStep?.Invoke("upload");
            double upKbps = 0;
            try
            {
                var body = new byte[196608]; var rnd = new Random(); var chunk = new byte[65536]; rnd.NextBytes(chunk);
                for (int i = 0; i < 3; i++) Buffer.BlockCopy(chunk, 0, body, i * 65536, 65536);
                double t0 = T();
                var content = new ByteArrayContent(body); content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var resp = await http.PostAsync($"{url}{sep}bw=1", content); await resp.Content.ReadAsStringAsync();
                upKbps = body.Length * 8 / Math.Max(1, T() - t0 - rtt * 0.5);
            }
            catch { /* offline */ }
            var r = new SpeedResult { rtt = (int)K.Round(rtt), jitter = (int)K.Round(jitter), downKbps = (int)K.Round(downKbps), upKbps = (int)K.Round(upKbps), at = Clock.Epoch };
            r.score = HostScore(r.rtt, r.upKbps, r.downKbps, desktop, Environment.ProcessorCount);
            // the tier you'd get as a client of a good host: latency + jitter to the node, and whether ~60 snapshots/s of
            // a full lobby (~1.4 KB each) fit in your downlink
            r.tier = TIERS[Math.Max(TierCode(Classify(r.rtt, r.jitter, 0)), TierCode(FitTier(r.downKbps * 0.5, 1400)))];
            // a test that reached no node (no ping answered after the cold-start one) measured nothing: it is shown for this
            // run but not kept - kept, the lobby showed "LOW 999 ms, 0.0 Mbps" for 15 minutes after the node came back, as
            // the lobby only re-tests without a cached result (Unity-only; the TS caches every test)
            if (ps.Count > 0) try { NetConfig.Save(SPEED_KEY, JsonConvert.SerializeObject(r)); } catch { /* ignore */ }
            return r;
        }
    }

    public class SpeedResult { public int rtt, jitter, downKbps, upKbps, score; public string tier = "medium"; public long at; }

    public class LinkStats
    {
        public double rtt, jitter, loss;
        /// <summary>measured send / receive rates (kbit/s, last second)</summary>
        public double kbpsOut, kbpsIn;
        /// <summary>WebRTC's estimate of the bandwidth available toward the peer (kbit/s), 0 = unknown</summary>
        public double availKbps;
        /// <summary>how the bytes travel: "lan" (same network), "direct" (over the internet), "turn" (a TURN relay), "node"
        /// (the online node's relay), "?"</summary>
        public string path = "?";
        /// <summary>the send queue is backing up (the link can't keep up with the rate)</summary>
        public bool congested;
        int seq;
        readonly Dictionary<int, double> sent = new Dictionary<int, double>();       // ping seq -> sent at (ms)
        readonly List<bool> window = new List<bool>();                               // last pings: answered?
        double bytesOut, bytesIn, rateAt;
        int samples;

        /// <summary>the next ping to send (its seq)</summary>
        public int NextPing(double now)
        {
            int s = seq = (seq + 1) & 0xffff;
            sent[s] = now;
            // pings older than 2 s are lost
            foreach (var k in sent.Where(e => now - e.Value > 2000).Select(e => e.Key).ToList()) { sent.Remove(k); Push(false); }
            return s;
        }
        public void Pong(int s, double now)
        {
            if (!sent.TryGetValue(s, out var at)) return;
            sent.Remove(s);
            double r = now - at;
            jitter = samples > 0 ? jitter + (Math.Abs(r - rtt) - jitter) * 0.15 : 0;
            rtt = samples > 0 ? rtt + (r - rtt) * (r > rtt ? 0.25 : 0.1) : r;   // spikes count faster
            samples++;
            Push(true);
        }
        void Push(bool ok)
        {
            window.Add(ok); if (window.Count > 40) window.RemoveAt(0);
            loss = window.Count >= 8 ? (double)window.Count(x => !x) / window.Count : 0;
        }
        public void Out(double bytes) => bytesOut += bytes;
        public void In(double bytes) => bytesIn += bytes;
        /// <summary>once a frame: roll the byte counters into rates</summary>
        public void Tick(double now)
        {
            if (rateAt == 0) rateAt = now;
            double dt = now - rateAt;
            if (dt >= 1000)
            {
                kbpsOut = bytesOut * 8 / dt; kbpsIn = bytesIn * 8 / dt;
                bytesOut = bytesIn = 0; rateAt = now;
            }
        }
        public bool Measured => samples >= 3;
    }

    /// <summary>a link's update tier with hysteresis: down at once, up one step after `upAfter` ms of clean link</summary>
    public class AdaptiveTier
    {
        public string tier;
        public double upAfter;
        double goodSince;
        public AdaptiveTier(string start = "medium", double upAfter = 4000) { tier = start; this.upAfter = upAfter; }
        public string Update(LinkStats s, double now, bool relay, double budgetKbps = 0, double snapBytes = 0)
        {
            var T = Quality.TIERS;
            string want = Quality.Classify(s.rtt, s.jitter, s.loss, relay);
            if (budgetKbps > 0 && snapBytes > 0) want = T[Math.Max(Quality.TierCode(want), Quality.TierCode(Quality.FitTier(budgetKbps, snapBytes)))];
            if (s.availKbps > 0 && snapBytes > 0) want = T[Math.Max(Quality.TierCode(want), Quality.TierCode(Quality.FitTier(s.availKbps * 0.7, snapBytes)))];
            if (s.congested && !relay) want = T[Math.Min(T.Length - 2, Math.Max(Quality.TierCode(want), Quality.TierCode(tier) + 1))];
            if (!s.Measured && !relay) want = T[Math.Max(Quality.TierCode(want), Quality.TierCode("medium"))];
            int cur = Quality.TierCode(tier), w = Quality.TierCode(want);
            if (w > cur) { tier = want; goodSince = now; }                                     // worse: drop now
            else if (w < cur)
            {
                if (now - goodSince > upAfter) { tier = T[cur - 1]; goodSince = now; }        // better: one step at a time
            }
            else goodSince = Math.Max(goodSince, now - upAfter / 2);
            return tier;
        }
    }
}
