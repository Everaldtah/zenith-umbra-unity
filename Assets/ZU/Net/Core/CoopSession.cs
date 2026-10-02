// Campaign co-op (up to 4): squads are advertised through the online node; the host opens a PeerLink to every member.
// The host runs the whole simulation; clients send their input and render the host's snapshots (see FastSync). Port of
// zenith-umbra src/net/Coop.ts (the node lobby only - the TS's public MQTT broker fallback is not ported).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZU.Net
{
    public class Member
    {
        public string id, name, hero;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string link;
    }

    public class CoopSession : INetSession
    {
        public readonly NodeLobby lobby;
        public string Role { get; private set; }
        public Dictionary<string, PeerLink> Links { get; } = new Dictionary<string, PeerLink>();       // host: member id -> link ; client: host id -> link
        public List<Member> Squad { get; private set; } = new List<Member>();
        public string Level { get; private set; } = "";
        public string HostId { get; private set; } = "";
        public Action<List<Member>> OnSquad;
        public Action<List<Presence>> OnPlayers;
        public Action<string, List<Member>> OnStart;
        public Action<string> OnLeft;
        public Action<int> OnStatus;
        public Action<string, JObject> OnNetMessage { get; set; }
        public Action<string, byte[]> OnNetBinary { get; set; }

        public CoopSession(string name, string platform = "desktop", string nodeUrl = null)
        {
            lobby = new NodeLobby(name, platform, nodeUrl);
            lobby.OnPlayers = p => OnPlayers?.Invoke(p);
            lobby.OnStatus = n => OnStatus?.Invoke(n);
            lobby.OnMessage = m => Handle(m);
            lobby.Connect();
        }
        public string Me => lobby.id;
        /// <summary>the name other players see (on the node and in the squad)</summary>
        public void SetName(string n)
        {
            n = string.IsNullOrWhiteSpace(n) ? "Vanguard" : n.Trim();
            lobby.SetName(n);
            var me = Squad.FirstOrDefault(x => x.id == Me); if (me != null) { me.name = lobby.me.name; EmitSquad(); }
        }
        public IEnumerable<Presence> Squads() => lobby.players.Values.Where(p => p.status == "squad");

        /// <summary>every frame: the lobby and the links</summary>
        public void Tick()
        {
            lobby.Tick();
            foreach (var l in Links.Values.ToList()) l.Tick();
        }
        void UpdateFast() => lobby.fast = Links.Values.Any(l => l.state == "connecting" || l.state == "relay");

        public void Host(string hero, string level)
        {
            Role = "host"; Level = level; HostId = Me;
            Squad = new List<Member> { new Member { id = Me, name = lobby.me.name, hero = hero } };
            lobby.SetStatus("squad", level);
            EmitSquad();
        }
        public void Join(string hostId, string hero)
        {
            Role = "client"; HostId = hostId;
            lobby.Send(hostId, new JObject { ["t"] = "join", ["hero"] = hero, ["name"] = lobby.me.name });
        }
        public void SetHero(string hero)
        {
            if (Role == "host") { Squad[0].hero = hero; EmitSquad(); }
            else SendHost(new JObject { ["t"] = "hero", ["hero"] = hero });
        }
        public void SetLevel(string level) { if (Role == "host") { Level = level; lobby.SetStatus("squad", level); EmitSquad(); } }

        void Handle(JObject m)
        {
            string t = (string)m["t"], from = (string)m["from"];
            if (t == "join" && Role == "host")
            {
                if (Squad.Count >= 4 || Squad.Any(s => s.id == from)) { lobby.Send(from, new JObject { ["t"] = "full" }); return; }
                var sid = Rid.Short();
                var nm = (string)m["name"] ?? "Hero";
                Squad.Add(new Member { id = from, name = nm.Length > 20 ? nm.Substring(0, 20) : nm, hero = (string)m["hero"] ?? "mirei" });
                lobby.Send(from, new JObject { ["t"] = "accept", ["sid"] = sid });
                LinkTo(from, sid, true);
                EmitSquad();
            }
            else if (t == "accept" && Role == "client" && from == HostId) LinkTo(from, (string)m["sid"], false);
            else if (t == "full") { Role = null; OnLeft?.Invoke("That squad is full."); }
            else if (t == "sig") { if (Links.TryGetValue(from ?? "", out var l) && l.sid == (string)m["sid"]) l.HandleSignal(m); }
            else if (t == "relay") { if (Links.TryGetValue(from ?? "", out var l) && l.sid == (string)m["sid"]) l.HandleRelay(m); }
        }

        void LinkTo(string peer, string sid, bool initiator)
        {
            if (Links.TryGetValue(peer, out var old)) old.Close();
            var l = new PeerLink(lobby, peer, sid, initiator, false, lobby.url);
            Links[peer] = l;
            UpdateFast();
            l.OnState = s =>
            {
                UpdateFast();
                var mem = Squad.FirstOrDefault(x => x.id == peer); if (mem != null) mem.link = s;
                if (s == "closed")
                {
                    Links.Remove(peer);
                    if (Role == "host") { Squad = Squad.Where(x => x.id != peer).ToList(); EmitSquad(); }
                    else OnLeft?.Invoke("Lost connection to the squad host.");
                }
                else if (Role == "host") EmitSquad();
            };
            l.OnMessage = msg => Recv(peer, msg);
            l.OnBinary = u => OnNetBinary?.Invoke(peer, u);
        }

        void Recv(string from, JObject m)
        {
            var t = (string)m["t"];
            if (Role == "client")
            {
                if (t == "squad") { Squad = m["squad"]?.ToObject<List<Member>>() ?? Squad; Level = (string)m["level"] ?? Level; OnSquad?.Invoke(Squad); return; }
                if (t == "start") { OnStart?.Invoke((string)m["level"], m["squad"]?.ToObject<List<Member>>() ?? new List<Member>()); return; }
            }
            else if (Role == "host")
            {
                if (t == "hero") { var mem = Squad.FirstOrDefault(x => x.id == from); if (mem != null) { mem.hero = (string)m["hero"] ?? mem.hero; EmitSquad(); } return; }
                if (t == "leave") { if (Links.TryGetValue(from, out var l)) l.Close(); return; }
            }
            OnNetMessage?.Invoke(from, m);
        }

        static JArray Plain(IEnumerable<Member> squad) => new JArray(squad.Select(s => new JObject { ["id"] = s.id, ["name"] = s.name, ["hero"] = s.hero }));
        void EmitSquad()
        {
            OnSquad?.Invoke(Squad);
            if (Role == "host") Broadcast(new JObject { ["t"] = "squad", ["squad"] = Plain(Squad), ["level"] = Level });
        }
        public void Broadcast(JObject m, bool reliable = true) { foreach (var l in Links.Values.ToList()) l.Send(m, reliable); }
        public void SendHost(JObject m, bool reliable = true) { if (Links.TryGetValue(HostId ?? "", out var l)) l.Send(m, reliable); }

        public void Start()
        {
            if (Role != "host") return;
            lobby.SetStatus("playing", Level);
            var squad = Squad.Select(s => new Member { id = s.id, name = s.name, hero = s.hero }).ToList();
            Broadcast(new JObject { ["t"] = "start", ["level"] = Level, ["squad"] = Plain(squad) });
            OnStart?.Invoke(Level, squad);
        }
        public void Leave()
        {
            if (Role == "client") SendHost(new JObject { ["t"] = "leave" });
            foreach (var l in Links.Values.ToList()) l.Close();
            Links.Clear(); Role = null; Squad = new List<Member>();
            lobby.SetStatus("lobby");
        }
        public void Close() { Leave(); lobby.Close(); }
    }
}
