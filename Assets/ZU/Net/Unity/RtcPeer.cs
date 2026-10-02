// IRtcPeer on com.unity.webrtc: one RTCPeerConnection with the TS build's two data channels - "rel" (ordered, reliable)
// and "fast" (unordered, no retransmits) - and the browser's signalling JSON (RTCSessionDescription / RTCIceCandidate
// toJSON()), so a Unity player links to a web / Electron player the same way two browsers do.
// Unity hands every data-channel message over as bytes (no text/binary flag): the protocol tells them apart - JSON
// always starts with '{', the binary packets with their type byte (1-4, Codec).
// Every callback is posted to NetDriver and runs on the main thread, in its Update.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;

namespace ZU.Net.Unity
{
    sealed class RtcPeer : IRtcPeer
    {
        readonly NetDriver driver;
        readonly RTCPeerConnection pc;
        RTCDataChannel rel, fast;
        bool remoteAsked, remoteSet, opened, closed;
        readonly List<JObject> early = new List<JObject>();       // candidates that came before the remote description was set

        public Action<JObject> OnCandidate { get; set; }
        public Action<JObject> OnDescription { get; set; }
        public Action OnOpen { get; set; }
        public Action OnDown { get; set; }
        public Action<string> OnText { get; set; }
        public Action<byte[]> OnBytes { get; set; }

        public RtcPeer(NetDriver driver, IceServer[] ice)
        {
            this.driver = driver;
            var cfg = new RTCConfiguration
            {
                iceServers = ice.Select(s => new RTCIceServer { urls = s.urls, username = s.username ?? "", credential = s.credential ?? "", credentialType = RTCIceCredentialType.Password }).ToArray(),
            };
            pc = new RTCPeerConnection(ref cfg);
            pc.OnIceCandidate = c =>
            {
                if (c == null || string.IsNullOrEmpty(c.Candidate)) return;
                var j = new JObject { ["candidate"] = c.Candidate, ["sdpMid"] = c.SdpMid, ["sdpMLineIndex"] = c.SdpMLineIndex.HasValue ? (JToken)c.SdpMLineIndex.Value : JValue.CreateNull() };
                driver.Post(() => { if (!closed) OnCandidate?.Invoke(j); });
            };
            pc.OnConnectionStateChange = s => { if (s == RTCPeerConnectionState.Failed) driver.Post(() => { if (!closed) OnDown?.Invoke(); }); };
            pc.OnDataChannel = ch => driver.Post(() => Wire(ch));
        }

        public void Start(bool initiator)
        {
            if (!initiator) return;            // the answerer gets the initiator's channels through OnDataChannel
            Wire(pc.CreateDataChannel("rel", new RTCDataChannelInit { ordered = true }));
            Wire(pc.CreateDataChannel("fast", new RTCDataChannelInit { ordered = false, maxRetransmits = 0 }));
            driver.StartCoroutine(Offer());
        }
        IEnumerator Offer()
        {
            var op = pc.CreateOffer(); yield return op;
            if (op.IsError || closed) yield break;
            var d = op.Desc;
            var set = pc.SetLocalDescription(ref d); yield return set;
            if (!set.IsError && !closed) OnDescription?.Invoke(Desc(d));
        }
        static JObject Desc(RTCSessionDescription d) => new JObject { ["type"] = d.type.ToString().ToLowerInvariant(), ["sdp"] = d.sdp };
        static RTCSdpType SdpType(string t) => t == "answer" ? RTCSdpType.Answer : t == "pranswer" ? RTCSdpType.Pranswer : t == "rollback" ? RTCSdpType.Rollback : RTCSdpType.Offer;

        public bool HasRemote => remoteAsked;
        public void SetRemote(JObject sdp)
        {
            remoteAsked = true;
            driver.StartCoroutine(Remote(new RTCSessionDescription { type = SdpType((string)sdp["type"]), sdp = (string)sdp["sdp"] }));
        }
        IEnumerator Remote(RTCSessionDescription d)
        {
            var set = pc.SetRemoteDescription(ref d); yield return set;
            if (set.IsError || closed) yield break;
            remoteSet = true;
            foreach (var c in early) Add(c);
            early.Clear();
            if (d.type != RTCSdpType.Offer) yield break;
            var op = pc.CreateAnswer(); yield return op;
            if (op.IsError || closed) yield break;
            var a = op.Desc;
            var setL = pc.SetLocalDescription(ref a); yield return setL;
            if (!setL.IsError && !closed) OnDescription?.Invoke(Desc(a));
        }
        public void AddCandidate(JObject cand) { if (remoteSet) Add(cand); else early.Add(cand); }
        void Add(JObject c)
        {
            var s = (string)c["candidate"]; if (string.IsNullOrEmpty(s)) return;       // (end of candidates)
            try { pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = s, sdpMid = (string)c["sdpMid"], sdpMLineIndex = (int?)c["sdpMLineIndex"] })); }
            catch { /* a bad candidate is not fatal */ }
        }

        void Wire(RTCDataChannel ch)
        {
            if (ch.Label == "rel") rel = ch; else fast = ch;
            ch.OnOpen = () => driver.Post(CheckOpen);
            ch.OnClose = () => driver.Post(() => { if (!closed) OnDown?.Invoke(); });
            ch.OnMessage = bytes =>
            {
                var b = (byte[])bytes.Clone();
                driver.Post(() =>
                {
                    if (closed) return;
                    if (b.Length > 0 && b[0] == (byte)'{') OnText?.Invoke(Encoding.UTF8.GetString(b)); else OnBytes?.Invoke(b);
                });
            };
            CheckOpen();
        }
        void CheckOpen() { if (!opened && !closed && Open) { opened = true; OnOpen?.Invoke(); } }

        public bool Open => rel != null && fast != null && rel.ReadyState == RTCDataChannelState.Open && fast.ReadyState == RTCDataChannelState.Open;
        public long Buffered(bool reliable) { var ch = reliable ? rel : fast; return ch != null ? (long)ch.BufferedAmount : 0; }
        public void SendText(bool reliable, string s) { var ch = reliable ? rel : fast; if (ch != null && ch.ReadyState == RTCDataChannelState.Open) ch.Send(s); }
        public void SendBytes(bool reliable, byte[] b) { var ch = reliable ? rel : fast; if (ch != null && ch.ReadyState == RTCDataChannelState.Open) ch.Send(b); }

        /// <summary>the selected candidate pair: WebRTC's bandwidth estimate and the path (TS link.ts sampleRtc)</summary>
        public void SampleStats(Action<double, string> result) { if (!closed) driver.StartCoroutine(Stats(result)); }
        IEnumerator Stats(Action<double, string> result)
        {
            var op = pc.GetStats(); yield return op;
            if (op.IsError || closed) yield break;
            using (var rep = op.Value)
            {
                RTCIceCandidatePairStats pair = null;
                foreach (var s in rep.Stats.Values)
                    if (s is RTCIceCandidatePairStats p && p.nominated && p.state == "succeeded") pair = pair != null && pair.bytesSent > p.bytesSent ? pair : p;
                if (pair == null) yield break;
                string Kind(string id) => rep.TryGetValue(id ?? "", out var c) && c is RTCIceCandidateStats cs ? cs.candidateType : null;
                var kinds = new[] { Kind(pair.localCandidateId), Kind(pair.remoteCandidateId) };
                string path = kinds.Contains("relay") ? "turn" : kinds.All(k => k == "host") ? "lan" : "direct";
                result(pair.availableOutgoingBitrate > 0 ? pair.availableOutgoingBitrate / 1000 : 0, path);
            }
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            try { rel?.Close(); fast?.Close(); pc.Close(); pc.Dispose(); } catch { /* ignore */ }
        }
    }
}
