// Peer-to-peer game link between two players (port of zenith-umbra src/net/link.ts + session.ts): WebRTC data channels
// signalled through the lobby. "rel" is ordered + reliable (events, roster, chat), "fast" is unordered with no retransmits
// (snapshots, inputs, pings - binary, see Codec). The link measures itself (LinkStats: RTT, jitter, loss, WebRTC's
// bandwidth estimate, the path the bytes take, a backed-up send queue) so the session can pick each link's update rate
// (Fast Link tiers). If a direct connection can't be made within a few seconds (strict NAT/firewall), traffic is relayed
// through the lobby (the online node) instead, and the link keeps trying to go direct in the background.
// WebRTC itself is behind IRtcPeer (ZU.Net.Unity provides it with com.unity.webrtc); without a factory a link is
// relay-only. Signalling JSON is the browser's (RTCSessionDescription / RTCIceCandidate toJSON()), so either build can
// link to the other.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZU.Net
{
    /// <summary>What the in-match sync (FastSync) needs from a network session - the campaign's CoopSession and the online
    /// PvP OnlineSession both provide it (TS session.ts).</summary>
    public interface INetSession
    {
        /// <summary>this player's id on the lobby</summary>
        string Me { get; }
        /// <summary>"host" | "client" | null</summary>
        string Role { get; }
        string HostId { get; }
        /// <summary>host: member id -> link; client: host id -> link</summary>
        Dictionary<string, PeerLink> Links { get; }
        /// <summary>JSON from a peer (reliable events, roster, ...)</summary>
        Action<string, JObject> OnNetMessage { get; set; }
        /// <summary>binary from a peer (snapshots, inputs)</summary>
        Action<string, byte[]> OnNetBinary { get; set; }
        void Broadcast(JObject m, bool reliable = true);
        void SendHost(JObject m, bool reliable = true);
    }

    public class IceServer
    {
        [JsonConverter(typeof(StringOrArray))] public string[] urls;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string username, credential;
    }
    class StringOrArray : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(string[]);
        public override object ReadJson(JsonReader r, Type t, object existing, JsonSerializer s)
        {
            var tok = JToken.Load(r);
            return tok.Type == JTokenType.Array ? tok.Select(x => (string)x).ToArray() : new[] { (string)tok };
        }
        public override void WriteJson(JsonWriter w, object v, JsonSerializer s) => JToken.FromObject(v).WriteTo(w);
    }

    /// <summary>One RTCPeerConnection with the two data channels ("rel" ordered, "fast" unordered with no retransmits). All
    /// callbacks must be raised on the thread that calls the link's Tick (the Unity layer dispatches them there).</summary>
    public interface IRtcPeer
    {
        /// <summary>a local ICE candidate (RTCIceCandidate.toJSON(): candidate, sdpMid, sdpMLineIndex)</summary>
        Action<JObject> OnCandidate { get; set; }
        /// <summary>the local description to send (RTCSessionDescription.toJSON(): type, sdp)</summary>
        Action<JObject> OnDescription { get; set; }
        /// <summary>both channels open</summary>
        Action OnOpen { get; set; }
        /// <summary>a channel closed or the connection failed</summary>
        Action OnDown { get; set; }
        Action<string> OnText { get; set; }
        Action<byte[]> OnBytes { get; set; }
        /// <summary>initiator: create both channels and send an offer; else wait for the remote's channels</summary>
        void Start(bool initiator);
        bool HasRemote { get; }
        /// <summary>apply the remote description (an offer is answered through OnDescription)</summary>
        void SetRemote(JObject sdp);
        void AddCandidate(JObject cand);
        bool Open { get; }
        /// <summary>bytes queued on a channel (RTCDataChannel.bufferedAmount)</summary>
        long Buffered(bool reliable);
        void SendText(bool reliable, string s);
        void SendBytes(bool reliable, byte[] b);
        /// <summary>WebRTC's own view: (availableOutgoingBitrate kbit/s or 0, the path "lan" | "direct" | "turn" or null)</summary>
        void SampleStats(Action<double, string> result);
        void Close();
    }

    public class PeerLink
    {
        /// <summary>WebRTC for new links (null = relay through the node only)</summary>
        public static Func<IceServer[], IRtcPeer> RtcFactory;
        static readonly IceServer[] DEFAULT_ICE =
        {
            new IceServer { urls = new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" } },
            new IceServer { urls = new[] { "stun:stun.cloudflare.com:3478" } },
        };
        static Task<IceServer[]> iceList; static string iceFor;
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        /// <summary>the ICE servers (STUN + any TURN the node hands out), fetched once</summary>
        public static Task<IceServer[]> IceServers(string nodeUrl)
        {
            if (string.IsNullOrEmpty(nodeUrl)) return Task.FromResult(DEFAULT_ICE);
            if (iceList != null && iceFor == nodeUrl) return iceList;
            iceFor = nodeUrl;
            return iceList = Task.Run(async () =>
            {
                try
                {
                    var j = JObject.Parse(await http.GetStringAsync($"{nodeUrl}{(nodeUrl.Contains("?") ? "&" : "?")}ice=1"));
                    var list = j["iceServers"]?.ToObject<IceServer[]>();
                    return list != null && list.Length > 0 ? list : DEFAULT_ICE;
                }
                catch { iceList = null; return DEFAULT_ICE; }
            });
        }
        /// <summary>the send queue above which a fast-channel packet is dropped instead of queued (the link is congested)</summary>
        const long CONGESTED = 96 * 1024;

        /// <summary>"connecting" | "p2p" | "relay" | "closed"</summary>
        public string state = "connecting";
        public LinkStats stats = new LinkStats();
        public Action<JObject> OnMessage;
        public Action<byte[]> OnBinary;
        public Action<string> OnState;
        public readonly string peer, sid; public readonly bool initiator;
        readonly ISignaller lobby;
        IRtcPeer pc;
        readonly List<JObject> pending = new List<JObject>();
        readonly List<JObject> pendingSdp = new List<JObject>();
        long lastRecv = Clock.Epoch;
        readonly double born = Clock.Now; double relayAfter;
        double pingAt, sampleAt;
        Task<IceServer[]> ice;

        public PeerLink(ISignaller lobby, string peer, string sid, bool initiator, bool forceRelay = false, string nodeUrl = null)
        {
            this.lobby = lobby; this.peer = peer; this.sid = sid; this.initiator = initiator;
            if (!forceRelay && RtcFactory != null) ice = IceServers(nodeUrl);
            // no direct channel after 6 s -> relay through the node (P2P may still come up later and take over)
            relayAfter = forceRelay || RtcFactory == null ? 0 : 6000;
        }
        /// <summary>a link over a given transport, set up at once (the headless tests' in-memory pairs)</summary>
        public PeerLink(ISignaller lobby, string peer, string sid, bool initiator, IRtcPeer rtc) : this(lobby, peer, sid, initiator, true)
        {
            relayAfter = double.PositiveInfinity;
            SetupRtc(rtc);
        }
        /// <summary>round trip in ms</summary>
        public double Rtt => stats.rtt;
        public bool Direct => state == "p2p";

        void Set(string s)
        {
            if (state == s) return;
            state = s; stats.path = s == "relay" ? "node" : stats.path == "node" ? "?" : stats.path;
            OnState?.Invoke(s);
        }

        /// <summary>every frame (TS: the 50 ms interval, the 2 s stats sampler and the relay timeout)</summary>
        public void Tick()
        {
            if (state == "closed") return;
            double now = Clock.Now;
            if (ice != null && pc == null && ice.IsCompleted) { var list = ice.Result; ice = null; try { SetupRtc(list); } catch { /* no WebRTC: relay only */ } }
            if (state == "connecting" && now - born >= relayAfter) Set("relay");
            stats.Tick(now);
            // pings: 4 Hz direct, 1 Hz through the node
            if (now - pingAt > (Direct ? 250 : 1000) && state != "connecting")
            {
                pingAt = now;
                SendBin(K.PingPacket(K.PK_PING, stats.NextPing(now)));
            }
            if (now - sampleAt > 2000) { sampleAt = now; SampleRtc(); }
            long t = Clock.Epoch;
            if (t - lastRecv > 12_000 && state != "closed" && state != "connecting") Close();   // partner gone
            if (state == "connecting" && t - lastRecv > 30_000) Close();
        }

        void SetupRtc(IceServer[] list) => SetupRtc(RtcFactory(list));
        void SetupRtc(IRtcPeer rtc)
        {
            var p = pc = rtc;
            p.OnCandidate = c => lobby.Send(peer, new JObject { ["t"] = "sig", ["sid"] = sid, ["cand"] = c });
            p.OnDescription = d => lobby.Send(peer, new JObject { ["t"] = "sig", ["sid"] = sid, ["sdp"] = d });
            p.OnOpen = () => { Set("p2p"); SampleRtc(); };
            p.OnDown = () => { if (state == "p2p") Set("relay"); };
            p.OnText = s => { stats.In(s.Length); try { Receive(JObject.Parse(s)); } catch { /* bad json */ } };
            p.OnBytes = u => { stats.In(u.Length); ReceiveBin(u); };
            p.Start(initiator);
            foreach (var sdp in pendingSdp) HandleSignal(sdp);
            pendingSdp.Clear();
        }

        /// <summary>WebRTC's own view: the selected candidate pair's bandwidth estimate and path type</summary>
        void SampleRtc()
        {
            if (pc == null || state != "p2p") return;
            try { pc.SampleStats((kbps, path) => { if (kbps > 0) stats.availKbps = kbps; if (path != null && state == "p2p") stats.path = path; }); } catch { /* best-effort */ }
        }

        /// <summary>Signalling from the lobby for this session: { sdp } or { cand }</summary>
        public void HandleSignal(JObject m)
        {
            lastRecv = Clock.Epoch;
            if (pc == null) { if (m["cand"] is JObject c0) pending.Add(c0); else if (m["sdp"] != null) pendingSdp.Add(m); return; }
            try
            {
                if (m["sdp"] is JObject sdp)
                {
                    pc.SetRemote(sdp);
                    foreach (var c in pending) pc.AddCandidate(c);
                    pending.Clear();
                }
                else if (m["cand"] is JObject cand)
                {
                    if (pc.HasRemote) pc.AddCandidate(cand); else pending.Add(cand);
                }
            }
            catch { /* a bad candidate is not fatal */ }
        }
        /// <summary>Relayed payload from the lobby: { d: json } or { b: base64 binary }</summary>
        public void HandleRelay(JObject m)
        {
            if (m["b"]?.Type == JTokenType.String) { var u = K.FromB64((string)m["b"]); stats.In(u.Length); ReceiveBin(u); }
            else if (m["d"] is JObject d) Receive(d);
        }

        void Receive(JObject m)
        {
            lastRecv = Clock.Epoch;
            var t = (string)m["t"];
            if (t == "_ping" || t == "_pong") return;          // (older builds' JSON pings)
            OnMessage?.Invoke(m);
        }
        void ReceiveBin(byte[] u)
        {
            lastRecv = Clock.Epoch;
            if (u.Length >= 3 && u[0] == K.PK_PING) { SendBin(K.PingPacket(K.PK_PONG, u[1] | (u[2] << 8))); return; }
            if (u.Length >= 3 && u[0] == K.PK_PONG) { stats.Pong(u[1] | (u[2] << 8), Clock.Now); return; }
            OnBinary?.Invoke(u);
        }

        /// <summary>JSON: reliable = ordered events</summary>
        public void Send(JObject msg, bool reliable = true)
        {
            if (state == "closed") return;
            if (pc != null && pc.Open) { var s = msg.ToString(Formatting.None); stats.Out(s.Length); pc.SendText(reliable, s); return; }
            if (state == "relay") lobby.Send(peer, new JObject { ["t"] = "relay", ["sid"] = sid, ["d"] = msg }, reliable);
        }
        /// <summary>Binary on the fast channel (or reliable). Returns false when the packet was dropped because the send queue
        /// is backed up - the caller's rate is too high for this link right now.</summary>
        public bool SendBin(byte[] u, bool reliable = false)
        {
            if (state == "closed") return false;
            if (pc != null && pc.Open)
            {
                stats.congested = pc.Buffered(reliable) > CONGESTED;
                if (stats.congested && !reliable) return false;
                stats.Out(u.Length + 28);
                pc.SendBytes(reliable, u);
                return true;
            }
            if (state == "relay") { stats.Out(u.Length); lobby.Send(peer, new JObject { ["t"] = "relay", ["sid"] = sid, ["b"] = K.ToB64(u) }, reliable); return true; }
            return false;
        }

        public void Close()
        {
            if (state == "closed") return;
            try { pc?.Close(); } catch { /* ignore */ }
            Set("closed");
        }
    }
}
