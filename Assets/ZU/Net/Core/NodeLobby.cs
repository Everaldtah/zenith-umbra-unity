// Lobby transport through the game's online node (/api/net, zenith-umbra api/net.js): presence, the player list, direct
// messages (WebRTC signalling, matchmaking) and a low-rate relay fallback. Port of src/net/nodeLobby.ts (+ the Presence /
// LobbyMsg types of src/net/lobby.ts; the MQTT lobby is not ported - the Unity edition only talks to a node).
// Tick-driven: the game calls Tick() every frame; HTTP runs on the thread pool and its answers are handled in Tick, on
// the caller's thread, so nothing here touches game state from another thread.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZU.Net
{
    /// <summary>a player on the node (TS lobby.ts Presence). status: lobby | squad | playing | queue | custom | online</summary>
    public class Presence
    {
        public string id, name, status, platform;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string mission;
        public int v;
        /// <summary>online play: a custom game's line, ping to the node, profile level, best rank, host score</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string info, rank;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public int ping, lvl, score;
        /// <summary>Zenith.net launcher party id (the node keeps it): party members find each other's squad</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string party;
        public Presence Clone() => (Presence)MemberwiseClone();
    }

    /// <summary>anything that can deliver a lobby message to a peer</summary>
    public interface ISignaller { void Send(string to, JObject msg, bool reliable = true); }

    public class NodeLobby : ISignaller
    {
        const int PROTO = 1;
        public readonly string id = Rid.Make();
        public Presence me;
        public Dictionary<string, Presence> players = new Dictionary<string, Presence>();
        public Action<List<Presence>> OnPlayers;
        /// <summary>a lobby message: { t, from, mid, ... }</summary>
        public Action<JObject> OnMessage;
        public Action<int> OnStatus;
        /// <summary>set while links are connecting / relaying: poll fast</summary>
        public bool fast;
        /// <summary>extra fields for the next poll (matchmaking, profile card)</summary>
        public Func<JObject> Extra;
        /// <summary>every poll's answer (matchmaking status)</summary>
        public Action<JObject> OnPoll;
        /// <summary>the node's clock minus ours (ms) - match start times come in node time</summary>
        public double nodeOffset;
        public bool ok;
        public int failures;
        public readonly string url;
        public int Brokers => ok ? 1 : 0;

        readonly List<(string to, JObject msg)> outbox = new List<(string, JObject)>();
        readonly HashSet<string> seen = new HashSet<string>(); readonly Queue<string> seenQ = new Queue<string>();
        readonly ConcurrentQueue<Action> done = new ConcurrentQueue<Action>();
        double nextAt; int n; bool busy, closed, started;
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        public NodeLobby(string name, string platform, string url = null)
        {
            this.url = url ?? NetConfig.NodeUrl;
            me = new Presence { id = id, name = name, status = "lobby", platform = platform, v = PROTO };
        }

        public void Connect() { started = true; nextAt = 0; }

        /// <summary>every frame: hand finished polls to their handlers, start the next one when it is due</summary>
        public void Tick()
        {
            while (done.TryDequeue(out var a)) a();
            if (!started || closed || busy) return;
            if (Clock.Now >= nextAt) Poll(false);
        }

        double Delay => fast ? 140 : me.status == "playing" || me.status == "online" ? 2500 : 900;

        void Poll(bool bye)
        {
            if (busy && !bye) return;
            busy = true;
            var batch = outbox.Take(64).ToList(); outbox.RemoveRange(0, batch.Count);
            bool list = n++ % 3 == 0;
            var body = new JObject { ["id"] = id, ["out"] = new JArray(batch.Select(o => new JObject { ["to"] = o.to, ["msg"] = o.msg })), ["list"] = list, ["bye"] = bye };
            if (!bye) body["me"] = JObject.FromObject(me);
            var x = bye ? null : Extra?.Invoke();
            if (x != null) foreach (var p in x.Properties()) body[p.Name] = p.Value;
            string json = body.ToString(Formatting.None);
            long t0 = Clock.Epoch;
            Task.Run(async () =>
            {
                try
                {
                    var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                    if (!resp.IsSuccessStatusCode) throw new HttpRequestException(((int)resp.StatusCode).ToString());
                    var j = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    long t1 = Clock.Epoch;
                    done.Enqueue(() => Answer(j, t0, t1));
                }
                catch { done.Enqueue(() => Failed(batch)); }
            });
        }

        void Answer(JObject j, long t0, long t1)
        {
            busy = false; nextAt = Clock.Now + Delay;
            if (j["t"]?.Type == JTokenType.Integer || j["t"]?.Type == JTokenType.Float)
                nodeOffset += (((double)j["t"] - (t0 + t1) / 2.0) - nodeOffset) * (n < 3 ? 1 : 0.2);
            OnPoll?.Invoke(j);
            if (!ok) { ok = true; OnStatus?.Invoke(1); }
            failures = 0;
            if (j["players"] is JArray pl)
            {
                players = pl.Select(p => p.ToObject<Presence>()).Where(p => p != null && p.v == PROTO && p.id != null).GroupBy(p => p.id).ToDictionary(g => g.Key, g => g.First());
                OnPlayers?.Invoke(players.Values.ToList());
            }
            if (j["inbox"] is JArray inbox)
                foreach (var m in inbox.OfType<JObject>())
                {
                    var mid = (string)m["mid"];
                    if (!string.IsNullOrEmpty(mid))
                    {
                        if (seen.Contains(mid)) continue;
                        seen.Add(mid); seenQ.Enqueue(mid); if (seenQ.Count > 500) seen.Remove(seenQ.Dequeue());
                    }
                    OnMessage?.Invoke(m);
                }
        }

        void Failed(List<(string to, JObject msg)> batch)
        {
            busy = false; nextAt = Clock.Now + Delay;
            outbox.InsertRange(0, batch.Where(o => (string)o.msg["t"] != "relay"));   // retry signalling, drop stale relay traffic
            failures++;
            if (ok && failures > 2) { ok = false; OnStatus?.Invoke(0); }
        }

        public void SetStatus(string status, string mission = null, string info = null) { me = me.Clone(); me.status = status; me.mission = mission; me.info = info; }
        /// <summary>the poll right away (a queue join, a lobby change)</summary>
        public void Now() { if (!closed && !busy) nextAt = 0; }
        public void SetName(string name) { me = me.Clone(); me.name = name.Length > 20 ? name.Substring(0, 20) : name; }

        public void Send(string to, JObject msg, bool reliable = true)
        {
            var m = (JObject)msg.DeepClone(); m["mid"] = Rid.Make(8);
            outbox.Add((to, m));
            if (outbox.Count > 200) outbox.RemoveRange(0, outbox.Count - 200);
            // signalling should not wait for the next idle poll
            if ((string)msg["t"] != "relay" && !fast) nextAt = Math.Min(nextAt, Clock.Now + 30);
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            busy = false; Poll(true);
        }
    }
}
