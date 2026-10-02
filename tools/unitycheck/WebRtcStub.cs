// Compile-only stand-in for the com.unity.webrtc 3.0.0 API that Assets/ZU/Net/Unity uses (RtcPeer), for a worktree whose
// editor hasn't resolved the package yet (UnityCheck.csproj takes the package's real sources from Library/PackageCache
// when they are there). Signatures as the package declares them; nothing here runs.
using System;
using System.Collections.Generic;

namespace Unity.WebRTC
{
    public delegate void DelegateOnIceCandidate(RTCIceCandidate candidate);
    public delegate void DelegateOnConnectionStateChange(RTCPeerConnectionState state);
    public delegate void DelegateOnDataChannel(RTCDataChannel channel);
    public delegate void DelegateOnOpen();
    public delegate void DelegateOnClose();
    public delegate void DelegateOnMessage(byte[] bytes);

    public enum RTCIceCredentialType { Password = 0, OAuth = 1 }
    public enum RTCSdpType { Offer, Pranswer, Answer, Rollback }
    public enum RTCPeerConnectionState { New = 0, Connecting = 1, Connected = 2, Disconnected = 3, Failed = 4, Closed = 5 }
    public enum RTCDataChannelState { Connecting, Open, Closing, Closed }

    public struct RTCIceServer { public string credential; public RTCIceCredentialType credentialType; public string[] urls; public string username; }
    public struct RTCConfiguration { public RTCIceServer[] iceServers; }
    public struct RTCSessionDescription { public RTCSdpType type; public string sdp; }

    public class RTCIceCandidateInit { public string candidate; public string sdpMid; public int? sdpMLineIndex; }
    public class RTCIceCandidate : IDisposable
    {
        public RTCIceCandidate(RTCIceCandidateInit candidateInfo = null) { }
        public string Candidate => null;
        public string SdpMid => null;
        public int? SdpMLineIndex => null;
        public void Dispose() { }
    }

    public class RTCDataChannelInit { public bool? ordered; public int? maxPacketLifeTime; public int? maxRetransmits; public string protocol; }
    public class RTCDataChannel : IDisposable
    {
        public string Label => null;
        public DelegateOnOpen OnOpen { get; set; }
        public DelegateOnClose OnClose { get; set; }
        public DelegateOnMessage OnMessage { get; set; }
        public RTCDataChannelState ReadyState => RTCDataChannelState.Closed;
        public ulong BufferedAmount => 0;
        public void Send(string msg) { }
        public void Send(byte[] msg) { }
        public void Close() { }
        public void Dispose() { }
    }

    public class AsyncOperationBase : UnityEngine.CustomYieldInstruction
    {
        public bool IsError { get; internal set; }
        public bool IsDone { get; internal set; }
        public override bool keepWaiting => !IsDone;
    }
    public class RTCSessionDescriptionAsyncOperation : AsyncOperationBase { public RTCSessionDescription Desc { get; internal set; } }
    public class RTCSetSessionDescriptionAsyncOperation : AsyncOperationBase { }
    public class RTCStatsReportAsyncOperation : AsyncOperationBase { public RTCStatsReport Value { get; private set; } }

    public class RTCStats { public string Id => null; }
    public class RTCIceCandidatePairStats : RTCStats
    {
        public string localCandidateId => null;
        public string remoteCandidateId => null;
        public string state => null;
        public bool nominated => false;
        public ulong bytesSent => 0;
        public double availableOutgoingBitrate => 0;
    }
    public class RTCIceCandidateStats : RTCStats { public string candidateType => null; }
    public class RTCStatsReport : IDisposable
    {
        public IDictionary<string, RTCStats> Stats => new Dictionary<string, RTCStats>();
        public bool TryGetValue(string id, out RTCStats stats) { stats = null; return false; }
        public void Dispose() { }
    }

    public class RTCPeerConnection : IDisposable
    {
        public RTCPeerConnection(ref RTCConfiguration configuration) { }
        public DelegateOnIceCandidate OnIceCandidate { get; set; }
        public DelegateOnConnectionStateChange OnConnectionStateChange { get; set; }
        public DelegateOnDataChannel OnDataChannel { get; set; }
        public RTCDataChannel CreateDataChannel(string label, RTCDataChannelInit options = null) => null;
        public RTCSessionDescriptionAsyncOperation CreateOffer() => null;
        public RTCSessionDescriptionAsyncOperation CreateAnswer() => null;
        public RTCSetSessionDescriptionAsyncOperation SetLocalDescription(ref RTCSessionDescription desc) => null;
        public RTCSetSessionDescriptionAsyncOperation SetRemoteDescription(ref RTCSessionDescription desc) => null;
        public bool AddIceCandidate(RTCIceCandidate candidate) => false;
        public RTCStatsReportAsyncOperation GetStats() => null;
        public void Close() { }
        public void Dispose() { }
    }
}
