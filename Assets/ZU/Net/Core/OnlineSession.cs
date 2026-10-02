// Online play: matchmade Quick Play / Competitive and custom games against other players, through the online node
// (zenith-umbra api/net.js). Port of src/net/Online.ts.
//
// Matchmade: QUEUE -> the node pairs you with whoever else is queued (two players are enough) and names the HOST (the
// best connection) -> ASSEMBLE: for one minute the match stays open - anyone else who queues joins it - while everyone
// links peer-to-peer to the host and picks a hero for their role -> the host STARTS it: every seat still empty on both
// sides is an AI hero. Custom game: a host opens a lobby, players join it from the list, the host starts it.
// In the match the host runs the simulation and streams it (FastSync); this session is the INetSession it uses.
// Tick-driven (the TS timers run in Tick): call Tick() every frame.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Net
{
    public enum Phase { Idle, Queue, Assemble, Playing }

    public class Seat
    {
        public string id, name, team, role; public double mmr; public string hero = ""; public bool ready;
        /// <summary>"connecting" | "p2p" | "relay" | "closed" | null</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string link;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public int ping;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string platform;
        public Seat Clone() => (Seat)MemberwiseClone();
    }
    public class OnlineStart { public string match, q, map; public List<Seat> seats = new List<Seat>(); public long seed; }
    public class ProfileInfo { public JToken card; public int lvl; public string rank; }

    public class OnlineSession : INetSession
    {
        static readonly Dictionary<string, string> ROLE_HERO = new Dictionary<string, string> { ["tank"] = "tank", ["damage"] = "dps", ["support"] = "support" };
        /// <summary>the heroes a seat may pick: its side, its role (flex = any role)</summary>
        public static List<HeroDef> HeroPool(string team, string role) =>
            Setup.RosterFor(true).Where(h => h.team == team && (role == "flex" || (ROLE_HERO.TryGetValue(role ?? "", out var r) && h.role == r))).ToList();

        public readonly NodeLobby lobby;
        public string Role { get; private set; }
        public string HostId { get; private set; } = "";
        public Dictionary<string, PeerLink> Links { get; } = new Dictionary<string, PeerLink>();
        public Phase Phase { get; private set; } = Phase.Idle;
        /// <summary>"qp" | "comp" | "custom"</summary>
        public string Q { get; private set; } = "qp";
        /// <summary>"tank" | "damage" | "support" | "flex"</summary>
        public string QueueRole { get; private set; } = "flex";
        public double Mmr { get; private set; } = 1800;
        /// <summary>the match being assembled</summary>
        public string Match { get; private set; } = ""; public string Map { get; private set; } = ""; public long Seed { get; private set; }
        public List<Seat> Seats { get; private set; } = new List<Seat>();
        /// <summary>when the host starts it (local epoch ms, 0 = not set)</summary>
        public long StartAtMs { get; private set; }
        public int Queued { get; private set; }
        public long QueueSinceMs { get; private set; }
        public SpeedResult Speed { get; private set; } = Quality.CachedSpeed();
        public List<Presence> Players { get; private set; } = new List<Presence>();
        /// <summary>set while you're in a forming match you joined late (the UI says so)</summary>
        public bool Late { get; private set; }
        public Action OnChange;
        public Action<OnlineStart> OnStart;
        public Action<string> OnNotice;
        public Action<string, JObject> OnNetMessage { get; set; }
        public Action<string, byte[]> OnNetBinary { get; set; }
        public readonly string name;
        readonly Func<ProfileInfo> profile;
        JToken profCard; long profAt;
        /// <summary>a queue join / leave waiting for the next poll to carry it</summary>
        JObject pendingMm;
        /// <summary>when this client was put in a forming match (the watchdog below)</summary>
        long matchedAt;
        long startTimerAt;          // (TS startTimer) local epoch ms, 0 = none
        double tickAt;
        readonly List<string> maps;

        public OnlineSession(string name, IList<string> maps, Func<ProfileInfo> profile, string nodeUrl = null)
        {
            this.name = name; this.maps = maps.ToList(); this.profile = profile ?? (() => new ProfileInfo());
            lobby = new NodeLobby(name, "desktop", nodeUrl);
            lobby.OnPlayers = p => { Players = p; OnChange?.Invoke(); };
            lobby.OnStatus = _ => OnChange?.Invoke();
            lobby.OnMessage = m => Handle(m);
            lobby.OnPoll = j =>
            {
                if (!(j["mm"] is JObject mm) || Phase != Phase.Queue) return;
                Queued = (int?)mm["queued"] ?? Queued;
                // (matched in this very poll: the match is in the inbox, handled right after)
                bool matched = j["inbox"] is JArray ib && ib.Any(m => (string)m["t"] == "mm_match");
                if (!((bool?)mm["in"] ?? false) && !matched && Clock.Epoch - QueueSinceMs > 4000) Rejoin();
            };
            lobby.Extra = Extra;
            var pr = this.profile();
            lobby.me.lvl = pr.lvl; lobby.me.rank = pr.rank;
            if (Speed != null) { lobby.me.ping = Speed.rtt; lobby.me.score = Speed.score; }
            lobby.Connect();
        }
        public string Me => lobby.id;
        /// <summary>the node is reachable</summary>
        public bool Online => lobby.ok;
        public Seat MySeat => Seats.FirstOrDefault(s => s.id == Me);
        /// <summary>node clock -> local clock</summary>
        long Local(double nodeMs) => (long)(nodeMs - lobby.nodeOffset);

        /// <summary>every frame: the lobby, the links, the start timer, the 1 s roster / watchdog tick</summary>
        public void Tick()
        {
            lobby.Tick();
            foreach (var l in Links.Values.ToList()) l.Tick();
            long now = Clock.Epoch;
            if (startTimerAt > 0 && now >= startTimerAt) { startTimerAt = 0; Start(); }
            if (Clock.Now - tickAt >= 1000)
            {
                tickAt = Clock.Now;
                // the host refreshes everyone's roster (link state, ping) once a second while the match assembles
                if (Role == "host" && Phase == Phase.Assemble && Links.Count > 0) PushRoster();
                // watchdog: a matchmade host that never links up (closed the game, crashed) or never starts - back to the queue
                if (Role == "client" && Phase == Phase.Assemble && Q != "custom" && matchedAt > 0)
                {
                    Links.TryGetValue(HostId, out var l);
                    bool noLink = l == null && now - matchedAt > 20_000;
                    bool noStart = StartAtMs > 0 && now > StartAtMs + 25_000;
                    if (noLink || noStart)
                    {
                        string q = Q, match = Match;
                        Leave(true);
                        OnNotice?.Invoke("The host stopped responding - back in the queue.");
                        Queue(q, QueueRole, Mmr);
                        // (leave that match on the node too, so the queue doesn't slot us straight back into it)
                        pendingMm["left"] = match;
                    }
                }
            }
        }

        /// <summary>the connection test (ping / down / up to the node); its score decides who hosts</summary>
        public async Task<SpeedResult> Test(Action<string> onStep = null)
        {
            Speed = await Quality.SpeedTest(lobby.url, true, onStep);
            lobby.me.ping = Speed.rtt; lobby.me.score = Speed.score;
            OnChange?.Invoke();
            return Speed;
        }

        JObject Extra()
        {
            var x = new JObject();
            if (pendingMm != null) { x["mm"] = pendingMm; pendingMm = null; }
            else if (Phase == Phase.Queue) x["mm"] = new JObject { ["op"] = "stay", ["q"] = Q, ["role"] = QueueRole, ["mmr"] = K.Round(Mmr), ["score"] = Speed?.score ?? 50 };
            if (Clock.Epoch - profAt > 60_000) { var p = profile(); profCard = p.card; profAt = Clock.Epoch; if (profCard != null) x["prof"] = profCard; }
            return x;
        }

        // ---------------------------------------------------------------- queue
        public void Queue(string q, string role, double mmr)
        {
            Leave(true);
            Q = q; QueueRole = role; Mmr = mmr; Phase = Phase.Queue; QueueSinceMs = Clock.Epoch; Queued = 1;
            lobby.SetStatus("queue", q);
            // the join goes with the next poll; after that every poll says we're still here
            pendingMm = new JObject { ["op"] = "join", ["q"] = q, ["role"] = role, ["mmr"] = K.Round(mmr), ["score"] = Speed?.score ?? 50 };
            lobby.Now();
            OnChange?.Invoke();
        }
        /// <summary>the node lost our queue entry (a missed poll): queue again</summary>
        void Rejoin() { if (Phase == Phase.Queue) { var s = QueueSinceMs; Queue(Q, QueueRole, Mmr); QueueSinceMs = s; } }
        public void CancelQueue() => Leave();

        // ---------------------------------------------------------------- custom games
        public void HostCustom(string map)
        {
            Leave(true);
            Q = "custom"; Role = "host"; HostId = Me; Match = "c" + Rid.Short(); Map = map; Seed = (long)(Rid.Random() * 2147483648.0);
            Seats = new List<Seat> { new Seat { id = Me, name = name, team = "zenith", role = "flex", mmr = Mmr, hero = "", ready = false, platform = "desktop" } };
            Phase = Phase.Assemble; StartAtMs = 0;
            Advertise();
            OnChange?.Invoke();
        }
        public void JoinCustom(string hostId)
        {
            Leave(true);
            Q = "custom"; Role = "client"; HostId = hostId; Phase = Phase.Assemble; Seats = new List<Seat>(); Match = "";
            lobby.Send(hostId, new JObject { ["t"] = "cjoin", ["name"] = name, ["mmr"] = K.Round(Mmr) });
            OnChange?.Invoke();
        }
        public IEnumerable<Presence> CustomGames() => Players.Where(p => p.status == "custom");
        public void SetMap(string map) { if (Role == "host" && Phase == Phase.Assemble) { Map = map; PushRoster(); } }
        /// <summary>host: move a player to the other side</summary>
        public void SwapTeam(string id)
        {
            if (Role != "host") return;
            var s = Seats.FirstOrDefault(x => x.id == id); if (s == null) return;
            var to = s.team == "zenith" ? "umbra" : "zenith";
            if (Seats.Count(x => x.team == to) >= 5) return;
            s.team = to; s.hero = ""; s.ready = false; PushRoster();
        }
        void Advertise()
        {
            if (Q != "custom" || Role != "host") return;
            lobby.SetStatus("custom", Match, $"{name}'s game · {Map} · {Seats.Count}/10");
            lobby.Now();
        }

        // ---------------------------------------------------------------- hero select
        public void Pick(string hero)
        {
            var me = MySeat; if (me == null || Phase != Phase.Assemble) return;
            if (Role == "host") { ApplyPick(Me, hero); return; }
            me.hero = hero; me.ready = true;                 // (optimistic; the host's roster is the truth)
            SendHost(new JObject { ["t"] = "pick", ["hero"] = hero });
            OnChange?.Invoke();
        }
        void ApplyPick(string id, string hero)
        {
            var s = Seats.FirstOrDefault(x => x.id == id);
            if (s == null || !GameData.Current.Hero.ContainsKey(hero) || !HeroPool(s.team, s.role).Any(h => h.id == hero)) return;
            if (Seats.Any(x => x != s && x.team == s.team && x.hero == hero)) return;      // taken on that side
            s.hero = hero; s.ready = true;
            PushRoster();
        }
        /// <summary>host: everyone's roster and the map, to everyone</summary>
        void PushRoster()
        {
            foreach (var s in Seats) if (Links.TryGetValue(s.id, out var l)) { s.link = l.state; s.ping = (int)K.Round(l.stats.rtt); }
            Broadcast(new JObject
            {
                ["t"] = "roster", ["match"] = Match, ["q"] = Q, ["map"] = Map, ["seats"] = JArray.FromObject(Seats),
                ["startAt"] = StartAtMs > 0 ? StartAtMs + lobby.nodeOffset : 0, ["seed"] = Seed,
            });
            if (Q == "custom") Advertise();
            OnChange?.Invoke();
        }
        /// <summary>host: start now (custom games), or when the gather minute is up (matchmade)</summary>
        public void Start()
        {
            if (Role != "host" || Phase != Phase.Assemble) return;
            startTimerAt = 0;
            // players whose link never came up don't make it in (their seat goes to the AI)
            Seats = Seats.Where(s => s.id == Me || (Links.TryGetValue(s.id, out var l) && (l.state == "p2p" || l.state == "relay"))).ToList();
            foreach (var s in Seats.Where(s => string.IsNullOrEmpty(s.hero)))
            {
                var free = HeroPool(s.team, s.role).FirstOrDefault(h => !Seats.Any(x => x.team == s.team && x.hero == h.id));
                s.hero = free?.id ?? HeroPool(s.team, "flex")[0].id;
            }
            if (string.IsNullOrEmpty(Map) && maps.Count > 0) Map = maps[(int)(Seed % maps.Count)];
            var st = new OnlineStart { match = Match, q = Q, map = Map, seats = Seats.Select(s => s.Clone()).ToList(), seed = Seed };
            var msg = JObject.FromObject(st); msg["t"] = "start";
            Broadcast(msg);
            Phase = Phase.Playing;
            lobby.SetStatus("online", Q);
            OnStart?.Invoke(st);
        }

        // ---------------------------------------------------------------- lobby messages
        void Handle(JObject m)
        {
            string t = (string)m["t"], from = (string)m["from"];
            if (t == "mm_match" && (Phase == Phase.Queue || Phase == Phase.Idle))
            {
                // the node formed (or slotted us into) a match
                Phase = Phase.Assemble; Match = (string)m["match"] ?? ""; Q = (string)m["q"] ?? Q; HostId = (string)m["host"] ?? ""; Seed = (long?)m["seed"] ?? 0;
                Late = (bool?)m["late"] ?? false;
                if (maps.Count > 0) Map = maps[(int)(Seed % maps.Count)];
                Seats = (m["players"] as JArray ?? new JArray()).OfType<JObject>().Select(p => new Seat { id = (string)p["id"], name = (string)p["name"], team = (string)p["team"], role = (string)p["role"], mmr = (double?)p["mmr"] ?? 1800, hero = "", ready = false, platform = (string)p["platform"] }).ToList();
                StartAtMs = Local((double?)m["startAt"] ?? Clock.Epoch + 60_000);
                matchedAt = Clock.Epoch;
                lobby.SetStatus("online", Q);
                if (HostId == Me)
                {
                    Role = "host";
                    foreach (var s in Seats) if (s.id != Me) Offer(s.id);
                    ArmStart();
                }
                else Role = "client";
                OnNotice?.Invoke(Late ? "Joined a match that was forming" : "Match found");
                OnChange?.Invoke();
                return;
            }
            if (t == "mm_add" && Role == "host" && (string)m["match"] == Match && Phase == Phase.Assemble)
            {
                var p = m["player"] as JObject; if (p == null) return;
                if (!Seats.Any(s => s.id == (string)p["id"])) Seats.Add(new Seat { id = (string)p["id"], name = (string)p["name"], team = (string)p["team"], role = (string)p["role"], mmr = (double?)p["mmr"] ?? 1800, hero = "", ready = false, platform = (string)p["platform"] });
                var sa = (double?)m["startAt"]; if (sa.HasValue && sa.Value > 0) StartAtMs = Local(sa.Value);
                ArmStart();
                Offer((string)p["id"]);
                PushRoster();
                return;
            }
            if (t == "mm_link" && Role == "client" && from == HostId) { LinkTo(from, (string)m["sid"], false); return; }
            if (t == "cjoin" && Role == "host" && Q == "custom" && Phase == Phase.Assemble)
            {
                if (Seats.Count >= 10 || Seats.Any(s => s.id == from)) { lobby.Send(from, new JObject { ["t"] = "cfull" }); return; }
                var team = Seats.Count(s => s.team == "zenith") <= Seats.Count(s => s.team == "umbra") ? "zenith" : "umbra";
                var nm = (string)m["name"] ?? "Hero";
                Seats.Add(new Seat { id = from, name = nm.Length > 20 ? nm.Substring(0, 20) : nm, team = team, role = "flex", mmr = (double?)m["mmr"] ?? 1800, hero = "", ready = false });
                Offer(from);
                PushRoster();
                return;
            }
            if (t == "cfull" && Role == "client") { OnNotice?.Invoke("That game is full."); Leave(); return; }
            if (t == "sig") { if (Links.TryGetValue(from ?? "", out var l) && l.sid == (string)m["sid"]) l.HandleSignal(m); return; }
            if (t == "relay") { if (Links.TryGetValue(from ?? "", out var l) && l.sid == (string)m["sid"]) l.HandleRelay(m); }
        }
        /// <summary>host: open a link to a player (they answer on mm_link)</summary>
        void Offer(string peer)
        {
            var sid = Rid.Short();
            lobby.Send(peer, new JObject { ["t"] = "mm_link", ["sid"] = sid, ["match"] = Match });
            LinkTo(peer, sid, true);
        }
        void ArmStart()
        {
            startTimerAt = 0;
            if (Q == "custom" || StartAtMs == 0) return;
            startTimerAt = Math.Max(1, StartAtMs);
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
                if (s == "closed")
                {
                    if (Links.TryGetValue(peer, out var cur) && cur == l) Links.Remove(peer);
                    if (Role == "host")
                    {
                        if (Phase == Phase.Assemble) { Seats = Seats.Where(x => x.id != peer).ToList(); PushRoster(); }
                    }
                    else if (peer == HostId)
                    {
                        var was = Phase;
                        Reset();
                        if (was == Phase.Assemble && Q != "custom") { OnNotice?.Invoke("The host left before the start - back in the queue."); Queue(Q, QueueRole, Mmr); }
                        else OnNotice?.Invoke(was == Phase.Playing ? "Lost the connection to the host." : "The game closed.");
                    }
                }
                else if (Role == "host") PushRoster();
                OnChange?.Invoke();
            };
            l.OnMessage = msg => Recv(peer, msg);
            l.OnBinary = u => OnNetBinary?.Invoke(peer, u);
        }
        void UpdateFast() => lobby.fast = Links.Values.Any(l => l.state == "connecting" || l.state == "relay");

        void Recv(string from, JObject m)
        {
            var t = (string)m["t"];
            if (Role == "client" && from == HostId)
            {
                if (t == "roster")
                {
                    Match = (string)m["match"] ?? Match; Q = (string)m["q"] ?? Q; Map = (string)m["map"] ?? Map; Seats = m["seats"]?.ToObject<List<Seat>>() ?? Seats; Seed = (long?)m["seed"] ?? Seed;
                    var sa = (double?)m["startAt"]; if (sa.HasValue && sa.Value > 0) StartAtMs = (long)(sa.Value - lobby.nodeOffset);
                    OnChange?.Invoke(); return;
                }
                if (t == "start")
                {
                    Phase = Phase.Playing; Seats = m["seats"]?.ToObject<List<Seat>>() ?? Seats; Map = (string)m["map"] ?? Map; lobby.SetStatus("online", Q);
                    OnStart?.Invoke(m.ToObject<OnlineStart>()); return;
                }
            }
            else if (Role == "host")
            {
                if (t == "pick") { ApplyPick(from, (string)m["hero"] ?? ""); return; }
                if (t == "leave") { if (Links.TryGetValue(from, out var l)) l.Close(); return; }
            }
            OnNetMessage?.Invoke(from, m);
        }

        public void Broadcast(JObject m, bool reliable = true) { foreach (var l in Links.Values.ToList()) l.Send(m, reliable); }
        public void SendHost(JObject m, bool reliable = true) { if (Links.TryGetValue(HostId ?? "", out var l)) l.Send(m, reliable); }

        void Reset()
        {
            startTimerAt = 0;
            foreach (var l in Links.Values.ToList()) l.Close();
            Links.Clear(); Role = null; Seats = new List<Seat>(); Match = ""; Phase = Phase.Idle; StartAtMs = 0; Late = false; matchedAt = 0;
            OnNetMessage = null; OnNetBinary = null;
        }
        /// <summary>leave whatever we're in (queue, forming match, custom game, a finished match)</summary>
        public void Leave(bool quiet = false)
        {
            string match = Match; var phase = Phase;
            if (Role == "client") SendHost(new JObject { ["t"] = "leave" });
            Reset();
            if (phase == Phase.Queue || phase == Phase.Assemble) pendingMm = new JObject { ["op"] = "leave", ["match"] = match };
            lobby.SetStatus("lobby");
            if (phase != Phase.Idle) lobby.Now();
            if (!quiet) OnChange?.Invoke();
        }
        /// <summary>after a match: back to the online lobby (the links stay up only for the match)</summary>
        public void Finish() => Leave();
        public void Close() { Leave(true); lobby.Close(); }
    }
}
