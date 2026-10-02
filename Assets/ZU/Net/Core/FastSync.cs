// Fast Link replication - host-authoritative, for online PvP and campaign co-op. Port of zenith-umbra src/net/FastSync.ts
// (wire-identical: the same binary rows and cold-state JSON, so a Unity client can mirror a TS host and back).
//
// HOST (runs the only simulation):
//  - per peer, a snapshot stream at that link's own rate (Fast Link tiers: 60/40/30/20 Hz direct, 8 Hz through the
//    node), measured continuously and capped by the host's uplink shared between its peers
//  - snapshots are binary: every actor's and projectile's "hot" row each time, and the "cold" state (hero, statuses,
//    animation cues, scores, zones, the objective, your own cooldowns/ammo/ult) as JSON only while the peer hasn't
//    acknowledged its current version (acks ride on the peer's input packets)
//  - inputs arrive as binary with a sequence number and a press counter per button, so a tap is never lost to a
//    dropped packet (it is latched for one simulation step)
//  - lag compensation ("favour the shooter"): while a remote player's weapons and abilities run, everyone else is moved
//    back to where that player saw them (their RTT/2 + interpolation delay, capped at 250 ms) - World.rewind
// CLIENT:
//  - a snapshot buffer: other heroes and projectiles are drawn between the two snapshots around (host time - an adaptive
//    interpolation delay sized from the update rate and the measured jitter), extrapolated briefly on loss
//  - its own hero is predicted locally (movement runs at once on input) and reconciled against the host's position for
//    the last input it applied; the error is eased out
//  - reliable events (damage numbers, kills, sounds, effects) arrive in order on the reliable channel
// The client's mirror actors are its own Actor objects (their ids are this process's): everything from the host is
// keyed by the HOST's actor id (hostId / map) - the TS client looked its own hero up by the local id.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Net
{
    // ------------------------------------------------------------------ host
    public class FastHost
    {
        /// <summary>the per-actor values the renderers / HUD read (timestamps mostly - they rarely change)</summary>
        static readonly string[] SV_KEYS = { "rebirthAt", "fang", "zoom", "riseUntil", "riseAt", "track", "wasLowAt", "tideT0", "tideDur", "spin1", "spin2", "rhythm", "rebornAt",
            "healedAt", "tempo", "swoopProg", "susanooCast", "strikes", "strikeAt", "phase", "lastHitDmg", "groove", "grindSide", "grindNz", "grindNx", "grind",
            "fellAt", "fangTgt", "fangAt", "ammo2", "chargeStart", "rushStart", "deflectStart", "bassAt", "roarAt" };
        const double LAGCOMP_MAX = 0.25;

        internal class Held { public double mx, mz, yaw, pitch; public int bits; }
        public class Peer
        {
            public string id; public AdaptiveTier tier = new AdaptiveTier("medium"); public double next; public int seq;
            public Dictionary<string, int> acked = new Dictionary<string, int>();
            public Dictionary<string, (int v, double at)> sent = new Dictionary<string, (int, double)>();
            public Dictionary<int, List<(string k, int v)>> inflight = new Dictionary<int, List<(string, int)>>();
            internal Queue<int> inflightOrder = new Queue<int>();
            public int lastInput = -1; public int viewMs = 100; public int[] presses; internal Held held = new Held(); public HashSet<string> latch = new HashSet<string>();
            public int bytes;
        }
        class Cold { public string json; public int v; public JToken val; }

        /// <summary>events of this frame's steps (forwarded reliably)</summary>
        public List<JObject> events = new List<JObject>();
        public readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        /// <summary>a player whose link died mid-match (the game hands their hero to the AI)</summary>
        public Action<Actor> OnPeerLost;
        /// <summary>the host's measured uplink (kbit/s), shared between its peers</summary>
        public double upKbps;
        readonly Dictionary<string, Cold> cold = new Dictionary<string, Cold>();
        readonly Writer hot = new Writer(4096), outW = new Writer(8192);
        readonly Dictionary<int, double[]> hist = new Dictionary<int, double[]>();      // actor id -> ring of [t, x, y, z] per step
        readonly Dictionary<int, int> histN = new Dictionary<int, int>();
        double lastSnapBytes = 1200;
        public readonly World w; public readonly INetSession s;

        public FastHost(World w, INetSession s)
        {
            this.w = w; this.s = s;
            s.OnNetBinary = (from, u) => Recv(from, u);
            s.OnNetMessage = (from, m) =>
            {
                // older clients / JSON fallback
                if ((string)m["t"] == "in" && m["i"] is JObject i) { var a = ActorOf(from); if (a != null) ApplyJsonInput(a.input, i); }
            };
            w.rewind = a => Rewind(a);
        }
        static void ApplyJsonInput(SimInput inp, JObject i)
        {
            foreach (var p in i.Properties())
                switch (p.Name)
                {
                    case "mx": inp.mx = (double)p.Value; break; case "mz": inp.mz = (double)p.Value; break;
                    case "yaw": inp.yaw = (double)p.Value; break; case "pitch": inp.pitch = (double)p.Value; break;
                    default: if (p.Value.Type == JTokenType.Boolean) Wire.SetBtn(inp, p.Name, (bool)p.Value); break;
                }
        }
        Actor ActorOf(string peer) => w.actors.FirstOrDefault(x => x.netId == peer);
        Peer PeerOf(string id)
        {
            if (!peers.TryGetValue(id, out var p)) peers[id] = p = new Peer { id = id };
            return p;
        }

        void Recv(string from, byte[] u)
        {
            if (u.Length == 0 || u[0] != K.PK_INPUT) return;
            var p = PeerOf(from);
            InputPacket pk;
            try { pk = K.ReadInput(new Reader(u)); } catch { return; }
            Ack(p, pk.ackSnap);
            if (p.lastInput >= 0 && !K.SeqNewer(pk.seq, p.lastInput)) return;      // older than what we have (reordered)
            p.lastInput = pk.seq; p.viewMs = pk.viewMs;
            p.held = new Held { mx = pk.mx, mz = pk.mz, yaw = pk.yaw, pitch = pk.pitch, bits = pk.held };
            if (p.presses != null) for (int i = 0; i < K.IN_EDGES.Length; i++) if (pk.presses[i] != p.presses[i]) p.latch.Add(K.IN_EDGES[i]);
            p.presses = pk.presses;
            var a = ActorOf(from);
            if (a != null) ApplyHeld(a.input, p.held);
        }
        static void ApplyHeld(SimInput i, Held h)
        {
            i.mx = h.mx; i.mz = h.mz; i.yaw = h.yaw; i.pitch = h.pitch;
            for (int b = 0; b < K.IN_BITS.Length; b++) Wire.SetBtn(i, K.IN_BITS[b], (h.bits & (1 << b)) != 0);
        }
        void Ack(Peer p, int s)
        {
            foreach (var seq in p.inflight.Keys.ToList())
            {
                if (seq == s || K.SeqNewer(s, seq))
                {
                    foreach (var (k, v) in p.inflight[seq]) if ((p.acked.TryGetValue(k, out var a) ? a : -1) < v) p.acked[k] = v;
                    p.inflight.Remove(seq);
                }
            }
        }

        /// <summary>before each simulation step: latched presses are seen for exactly one step</summary>
        public void BeforeStep()
        {
            foreach (var p in peers.Values)
            {
                if (p.latch.Count == 0) continue;
                var a = ActorOf(p.id); if (a == null) { p.latch.Clear(); continue; }
                foreach (var k in p.latch) Wire.SetBtn(a.input, k, true);
            }
        }
        /// <summary>after each simulation step: release the latches, record positions for lag compensation</summary>
        public void AfterStep()
        {
            foreach (var p in peers.Values)
            {
                if (p.latch.Count == 0) continue;
                var a = ActorOf(p.id);
                if (a != null) foreach (var k in p.latch) { int b = Array.IndexOf(K.IN_BITS, k); Wire.SetBtn(a.input, k, b >= 0 && (p.held.bits & (1 << b)) != 0); }
                p.latch.Clear();
            }
            double t = w.time;
            foreach (var a in w.actors)
            {
                if (!hist.TryGetValue(a.id, out var h)) { h = new double[160 * 4]; for (int k = 0; k < h.Length; k++) h[k] = -1e9; hist[a.id] = h; histN[a.id] = 0; }
                int n = histN[a.id], i = (n % 160) * 4;
                h[i] = t; h[i + 1] = a.pos.x; h[i + 2] = a.pos.y; h[i + 3] = a.pos.z;
                histN[a.id] = n + 1;
            }
        }
        /// <summary>where an actor stood at time `t` (from the step history)</summary>
        V3? PosAt(int id, double t)
        {
            if (!hist.TryGetValue(id, out var h) || !histN.TryGetValue(id, out var n) || n == 0) return null;
            int after = -1;
            for (int k = 0; k < Math.Min(n, 160); k++)
            {
                int i = ((n - 1 - k) % 160) * 4;
                if (h[i] <= t)
                {
                    if (after < 0) return new V3(h[i + 1], h[i + 2], h[i + 3]);
                    double t0 = h[i], t1 = h[after], f = t1 > t0 ? (t - t0) / (t1 - t0) : 0;
                    return new V3(h[i + 1] + (h[after + 1] - h[i + 1]) * f, h[i + 2] + (h[after + 2] - h[i + 2]) * f, h[i + 3] + (h[after + 3] - h[i + 3]) * f);
                }
                after = i;
            }
            return null;
        }
        /// <summary>lag compensation: everyone else as the remote shooter saw them; returns the undo</summary>
        Action Rewind(Actor shooter)
        {
            if (!peers.TryGetValue(shooter.netId, out var p)) return null;
            s.Links.TryGetValue(shooter.netId, out var link);
            double back = Math.Min(LAGCOMP_MAX, ((link?.stats.rtt ?? 0) / 2 + p.viewMs) / 1000);
            if (back < 0.005) return null;
            double t = w.time - back;
            var moved = new List<(Actor b, V3 was, V3 q)>();
            foreach (var b in w.actors)
            {
                if (b == shooter || !b.alive) continue;
                var q = PosAt(b.id, t); if (q == null) continue;
                moved.Add((b, b.pos, q.Value));
                b.pos = q.Value;
            }
            return () =>
            {
                // back to the present, keeping anything the shot itself did to them (knockback, pulls)
                foreach (var (b, was, q) in moved) b.pos = new V3(was.x + (b.pos.x - q.x), was.y + (b.pos.y - q.y), was.z + (b.pos.z - q.z));
            };
        }

        /// <summary>call once per sim step with that step's events (before the local game consumes them)</summary>
        public void Capture(IEnumerable<SimEvent> ev)
        {
            foreach (var e in ev) if (!(e is SfxEvent sf && sf.id == "step")) events.Add(Wire.Pack(e));
        }

        // ------------------------------------------------------------------ cold state
        static JObject ActorCold(Actor a, double t)
        {
            var st = new JObject();
            foreach (var kv in a.st) if (kv.Value > t) st[kv.Key] = Wire.R1(kv.Value + 0.049);
            var sv = new JObject();
            foreach (var k in SV_KEYS) if (a.sv.TryGetValue(k, out var v) && double.IsFinite(v)) sv[k] = Wire.R2(v);
            var an = a.anim;
            return new JObject
            {
                ["d"] = a.def.id, ["b"] = a.baseDef?.id ?? a.def.id, ["tm"] = a.team, ["n"] = a.netId ?? "", ["boss"] = a.isBoss ? Wire.Rn(a.def.hp) : 0, ["rob"] = a.isRobot ? 1 : 0, ["own"] = a.owner?.id ?? 0,
                ["st"] = st, ["sv"] = sv, ["f"] = a.forced != null ? (JToken)new JArray(a.forced.kind, Wire.R2(a.forced.until)) : 0,
                ["an"] = new JArray(Wire.R2(an.attackAt), an.attackKind, an.attackSide, Wire.R2(an.castAt), an.castId, Wire.R2(an.hitAt), Wire.R2(an.landAt), Wire.R2(an.jumpAt), Wire.R2(an.fireL), Wire.R2(an.fireR), Wire.R2(an.deflectAt), an.deflectN),
                ["k"] = new JArray(a.kills, a.deaths, a.assists, Wire.Rn(a.dmgDone), Wire.Rn(a.healDone), Wire.R2(a.deathAt), a.respawnAt != 0 ? Wire.R2(a.respawnAt) : 0, Wire.Rn(a.ult / Math.Max(1, a.def.ult.charge) * 100), Wire.Rn(a.mitigated)),
            };
        }
        /// <summary>what only the actor's own player needs</summary>
        static JObject OwnCold(Actor a)
        {
            var cd = new JObject(); foreach (var kv in a.cd) cd[kv.Key] = Wire.R2(kv.Value);
            var x = new JObject(); foreach (var kv in a.stats) x[kv.Key] = Wire.R1(kv.Value);
            return new JObject
            {
                ["u"] = Wire.Rn(a.ult), ["am"] = a.ammo, ["rl"] = Wire.R2(a.reloadUntil), ["cd"] = cd, ["fl"] = Wire.Rn(a.flight), ["ch"] = Wire.R2(a.charge),
                ["sh"] = new JArray(a.shots, a.hits, a.crits, Wire.R1(a.objTime), a.ults, a.bestStreak, a.streak), ["x"] = x, ["cash"] = a.cash, ["it"] = new JArray(a.items), ["pw"] = new JArray(a.powers),
            };
        }
        static JToken S(string v) => v == null ? JValue.CreateNull() : (JToken)v;
        static JObject MatchCold(World w)
        {
            var P = w.point; var C = w.control; var M = w.push;
            return new JObject
            {
                ["r"] = w.rules, ["tl"] = w.timeLimit, ["w"] = S(w.winner),
                ["p"] = new JArray(S(P.owner), Wire.R1(P.capture), S(P.capTeam), Wire.R1(P.progress["zenith"]), Wire.R1(P.progress["umbra"]), P.contested ? 1 : 0, P.unlockAt),
                ["c"] = new JArray(C.round, C.wins["zenith"], C.wins["umbra"], C.phase, Wire.R1(C.phaseEnd), C.overtime ? 1 : 0),
                ["m"] = new JArray(Wire.R1(M.d), Wire.R1(M.best["zenith"]), Wire.R1(M.best["umbra"]), S(M.owner), M.contested ? 1 : 0, M.unlockAt, M.half, Wire.R1(M.pos.x), Wire.R1(M.pos.y), Wire.R1(M.pos.z), M.checkpoint, M.overtime ? 1 : 0),
                ["k"] = new JArray(w.packs.Select(p => Wire.R1(p.readyAt))),
            };
        }
        static JToken ZoneData(Zone z)
        {
            if (z.kind == "tele") return z.data == null ? JValue.CreateNull() : DataToJson(z.data);
            if (z.kind == "tether") return new JObject { ["target"] = z.data != null && z.data.TryGetValue("target", out var t) && t is Actor ta ? (JToken)ta.id : JValue.CreateNull() };
            return JValue.CreateNull();
        }
        static JToken DataToJson(object v)
        {
            switch (v)
            {
                case null: return JValue.CreateNull();
                case Actor a: return a.id;
                case V3 p: return Wire.V(p);
                case IDictionary<string, object> d: { var o = new JObject(); foreach (var kv in d) o[kv.Key] = DataToJson(kv.Value); return o; }
                case string s: return s;
                case bool b: return b;
                case double x: return x;
                case int i: return i;
                default: try { return JToken.FromObject(v); } catch { return JValue.CreateNull(); }
            }
        }
        static JArray ZonesCold(World w) => new JArray(w.zones.Select(z => new JArray(z.id, z.kind, S(z.team), Wire.R1(z.x), Wire.R1(z.y), Wire.R1(z.z), z.r, Wire.R2(z.born), Wire.R2(z.until), ZoneData(z), z.owner?.id ?? 0)));

        void SetCold(string key, JToken val, HashSet<string> keep)
        {
            keep.Add(key);
            string json = val.ToString(Formatting.None);
            if (!cold.TryGetValue(key, out var c)) cold[key] = new Cold { json = json, v = 1, val = val };
            else if (c.json != json) { c.json = json; c.v++; c.val = val; }
        }

        /// <summary>call once per rendered frame: events to everyone, and a snapshot to every peer whose turn it is</summary>
        public void Flush()
        {
            double now = Clock.Now;
            // links that died mid-match: their heroes go to the AI
            foreach (var a in w.actors.ToList())
                if (!string.IsNullOrEmpty(a.netId) && !s.Links.ContainsKey(a.netId)) { var id = a.netId; peers.Remove(id); OnPeerLost?.Invoke(a); if (a.netId == id) a.netId = ""; }
            if (s.Links.Count == 0) { events.Clear(); return; }
            if (events.Count > 0) { s.Broadcast(new JObject { ["t"] = "ev", ["e"] = new JArray(events) }); events = new List<JObject>(); }
            var due = new List<(Peer p, PeerLink link)>();
            int nPeers = s.Links.Count;
            foreach (var kv in s.Links)
            {
                var link = kv.Value;
                if (link.state == "connecting" || link.state == "closed") continue;
                var p = PeerOf(kv.Key);
                link.stats.Tick(now);
                p.tier.Update(link.stats, now, !link.Direct, upKbps > 0 ? upKbps * 0.7 / nPeers : 0, lastSnapBytes);
                if (now >= p.next) { due.Add((p, link)); double iv = 1000.0 / Quality.TIER[p.tier.tier].snapHz; p.next = Math.Max(p.next + iv, now - iv); }
            }
            if (due.Count == 0) return;
            // the shared hot block
            double t = w.time;
            hot.Reset();
            K.WriteHot(hot, w.actors.Select(a => new HotActor
            {
                id = a.id, x = a.pos.x, y = a.pos.y, z = a.pos.z, vx = a.vel.x, vy = a.vel.y, vz = a.vel.z, yaw = a.yaw, pitch = a.pitch,
                hp = Math.Max(0, a.hp), armor = Math.Max(0, a.armor), shield = a.ShieldAmt, bhp = Math.Max(0, a.barrier.hp),
                flags = (a.alive ? K.AF_ALIVE : 0) | (a.grounded ? K.AF_GROUNDED : 0) | (a.flying ? K.AF_FLYING : 0) | (a.barrier.up ? K.AF_BARRIER : 0) | (a.beamOn ? K.AF_BEAM : 0) | (a.flameOn ? K.AF_FLAME : 0) | (a.charging ? K.AF_CHARGING : 0) | (a.isBoss ? K.AF_BOSS : 0),
                scale = a.scale, beam = a.beamTarget?.id ?? 0,
            }).ToList(), w.projs.Select(p => new HotProj { id = p.id, x = p.pos.x, y = p.pos.y, z = p.pos.z, vx = p.vel.x, vy = p.vel.y, vz = p.vel.z }).ToList());
            int hotLen = hot.n; var hotBytes = hot.Raw;
            // the cold state's current versions
            var keep = new HashSet<string>();
            foreach (var a in w.actors)
            {
                SetCold("a" + a.id, ActorCold(a, t), keep);
                if (!string.IsNullOrEmpty(a.netId)) SetCold("o" + a.id, OwnCold(a), keep);
            }
            foreach (var p in w.projs)
                if (!cold.ContainsKey("p" + p.id)) SetCold("p" + p.id, new JArray(S(p.fx), p.heal ? 1 : 0, p.splash, p.owner?.id ?? 0, p.grav, p.r, S(p.team)), keep); else keep.Add("p" + p.id);
            SetCold("m", MatchCold(w), keep);
            SetCold("z", ZonesCold(w), keep);
            if (w.director is Director d) SetCold("d", new JObject { ["state"] = d.state, ["obj"] = d.objective, ["boss"] = d.boss?.id ?? 0, ["lvl"] = S(d.level?.id) }, keep);
            foreach (var k in cold.Keys.Where(k => !keep.Contains(k)).ToList()) cold.Remove(k);
            // one packet per due peer: header + hot + what that peer hasn't acknowledged
            foreach (var (p, link) in due)
            {
                var mine = w.actors.FirstOrDefault(a => a.netId == p.id);
                double resend = Math.Max(60, link.stats.rtt * 1.3);
                var c = new JObject(); var sentKeys = new List<(string, int)>();
                foreach (var kv in cold)
                {
                    string k = kv.Key; var cv = kv.Value;
                    if (k[0] == 'o' && (mine == null || k != "o" + mine.id)) continue;
                    if ((p.acked.TryGetValue(k, out var ak) ? ak : 0) >= cv.v) continue;
                    if (p.sent.TryGetValue(k, out var prev) && prev.v == cv.v && now - prev.at < resend) continue;
                    p.sent[k] = (cv.v, now); sentKeys.Add((k, cv.v));
                    char kind = k[0]; string id = k.Substring(1);
                    if (k.Length > 1 && kind == 'a') ((JObject)(c["a"] ??= new JObject()))[id] = cv.val;
                    else if (k.Length > 1 && kind == 'o') c["o"] = cv.val;
                    else if (k.Length > 1 && kind == 'p') ((JObject)(c["p"] ??= new JObject()))[id] = cv.val;
                    else c[k] = cv.val;
                }
                foreach (var k in p.sent.Keys.Where(k => !cold.ContainsKey(k)).ToList()) { p.sent.Remove(k); p.acked.Remove(k); }
                var o = outW.Reset();
                p.seq = (p.seq + 1) & 0xffff;
                K.WriteSnapHeader(o, new SnapHeader { seq = p.seq, time = t, ackInput = Math.Max(0, p.lastInput), tier = Quality.TierCode(p.tier.tier), hostMs = now });
                o.Bytes(hotBytes, hotLen);
                o.Str(sentKeys.Count > 0 ? c.ToString(Formatting.None) : "");
                if (sentKeys.Count > 0)
                {
                    p.inflight[p.seq] = sentKeys; p.inflightOrder.Enqueue(p.seq);
                    while (p.inflight.Count > 256 && p.inflightOrder.Count > 0) p.inflight.Remove(p.inflightOrder.Dequeue());
                }
                var pkt = o.Done();
                if (!link.SendBin(pkt)) foreach (var (k, _) in sentKeys) p.sent.Remove(k);      // congested: dropped, resend later
                lastSnapBytes = lastSnapBytes * 0.9 + pkt.Length * 0.1;
                p.bytes = pkt.Length;
            }
        }
        /// <summary>per-peer link numbers (host HUD / Tab screen)</summary>
        public LinkInfo Info(string peer)
        {
            if (!s.Links.TryGetValue(peer, out var l)) return null;
            peers.TryGetValue(peer, out var p);
            return new LinkInfo { peer = peer, rtt = (int)K.Round(l.stats.rtt), loss = l.stats.loss, tier = p?.tier.tier ?? "medium", path = l.stats.path, kbps = (int)K.Round(l.stats.kbpsOut) };
        }
    }

    public class LinkInfo { public string peer; public int rtt; public double loss; public string tier, path; public int kbps; }

    // ------------------------------------------------------------------ client
    public class FastClient
    {
        class Snap { public int seq; public double time, recv; public int ackInput; public string tier; public Dictionary<int, HotActor> actors; public List<HotProj> projs; }

        /// <summary>host actor id -> this client's mirror actor</summary>
        public readonly Dictionary<int, Actor> map = new Dictionary<int, Actor>();
        readonly Dictionary<Actor, int> hostIdOf = new Dictionary<Actor, int>();
        public Actor me;
        public Director fakeDirector;
        /// <summary>the host's update tier for this link, the measured snapshot rate and loss, the interpolation delay (ms)</summary>
        public string tier = "medium";
        public double snapHz, loss, interpMs = 70;
        readonly List<Snap> buf = new List<Snap>();
        int lastSeq = -1;
        double offset = double.NaN;                  // host time - local time (s)
        readonly List<(double at, double v)> offs = new List<(double, double)>();
        readonly Dictionary<int, JObject> coldA = new Dictionary<int, JObject>();
        readonly Dictionary<long, JArray> projMeta = new Dictionary<long, JArray>();
        readonly Dictionary<long, Proj> projObj = new Dictionary<long, Proj>();
        JObject own;
        // input
        int inSeq; double inT; readonly int[] presses = new int[K.IN_EDGES.Length];
        readonly Dictionary<string, bool> prevHeld = new Dictionary<string, bool>(); int orHeld;
        readonly List<(int seq, double x, double y, double z)> hist = new List<(int, double, double, double)>();
        double cx, cy, cz;
        int recvCount; double recvAt; int gapSeen, gapExp;
        double arrJit, lastArr;
        readonly Writer w0 = new Writer(64);
        public readonly World w; public readonly INetSession s; readonly Action<SimEvent> onEvent;

        public FastClient(World w, INetSession s, Action<SimEvent> onEvent, CampaignLevel levelDef)
        {
            this.w = w; this.s = s; this.onEvent = onEvent;
            if (levelDef != null)
            {
                // the campaign HUD reads the encounter director: a real one (it registers the campaign's enemy and boss
                // defs), never stepped here - the host drives it, its state arrives as cold state
                fakeDirector = new Director(w, levelDef, w.nav, () => 1);
                w.director = fakeDirector;
            }
            s.OnNetBinary = (from, u) => RecvBin(u);
            s.OnNetMessage = (from, m) => { if ((string)m["t"] == "ev" && m["e"] is JArray e) foreach (var x in e.OfType<JObject>()) Event(x); };
        }
        public PeerLink Link => s.Links.TryGetValue(s.HostId ?? "", out var l) ? l : null;
        Actor ActorByHost(int id) => map.TryGetValue(id, out var a) ? a : null;
        void Event(JObject e)
        {
            var ev = Wire.Unpack(e, ActorByHost);
            if (ev != null) onEvent?.Invoke(ev);
        }

        void RecvBin(byte[] u)
        {
            if (u.Length == 0 || u[0] != K.PK_SNAP) return;
            SnapHeader h; (List<HotActor> actors, List<HotProj> projs) hotRows; string coldStr;
            try { var r = new Reader(u); h = K.ReadSnapHeader(r); hotRows = K.ReadHot(r); coldStr = r.Left >= 4 ? r.Str() : ""; } catch { return; }
            if (lastSeq >= 0 && !K.SeqNewer(h.seq, lastSeq)) return;           // late duplicate / reordered: drop
            double now = Clock.Now;
            if (lastSeq >= 0) { gapExp += (h.seq - lastSeq) & 0xffff; gapSeen++; }
            lastSeq = h.seq;
            recvCount++;
            // arrival jitter (vs the expected interval)
            double iv = 1000.0 / Quality.TIER[Quality.TierOf(h.tier)].snapHz;
            if (lastArr > 0) arrJit += (Math.Abs(now - lastArr - iv) - arrJit) * 0.1;
            lastArr = now;
            tier = Quality.TierOf(h.tier);
            // clock: the least-delayed recent sample is the best estimate of host time - local time
            double sample = h.time - now / 1000;
            offs.Add((now, sample)); while (offs.Count > 0 && now - offs[0].at > 2000) offs.RemoveAt(0);
            double best = offs.Max(o => o.v);
            offset = double.IsNaN(offset) || Math.Abs(best - offset) > 0.25 ? best : offset + (best - offset) * 0.1;
            if (!string.IsNullOrEmpty(coldStr)) { try { ApplyCold(JObject.Parse(coldStr)); } catch { /* bad cold */ } }
            var snap = new Snap { seq = h.seq, time = h.time, recv = now, ackInput = h.ackInput, tier = tier, projs = hotRows.projs, actors = new Dictionary<int, HotActor>() };
            foreach (var a in hotRows.actors) snap.actors[a.id] = a;
            buf.Add(snap); if (buf.Count > 40) buf.RemoveAt(0);
            Reconcile(snap);
        }

        static double D(JToken t, double fallback = 0) => t == null || t.Type == JTokenType.Null ? fallback : (double)t;
        static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;
        void ApplyCold(JObject c)
        {
            if (c["a"] is JObject ca) foreach (var p in ca.Properties()) if (int.TryParse(p.Name, out var id) && p.Value is JObject v) coldA[id] = v;
            if (c["o"] is JObject co) own = co;
            if (c["p"] is JObject cp) foreach (var p in cp.Properties()) if (long.TryParse(p.Name, out var id) && p.Value is JArray v) projMeta[id] = v;
            if (c["m"] is JObject m)
            {
                var P = w.point; var C = w.control; var M = w.push;
                w.rules = Str(m["r"]) ?? w.rules; w.timeLimit = D(m["tl"], w.timeLimit);
                var mp = (JArray)m["p"];
                P.owner = Str(mp[0]); P.capture = D(mp[1]); P.capTeam = Str(mp[2]); P.progress["zenith"] = D(mp[3]); P.progress["umbra"] = D(mp[4]); P.contested = D(mp[5]) != 0; P.unlockAt = D(mp[6]);
                var mc = (JArray)m["c"];
                C.round = (int)D(mc[0]); C.wins["zenith"] = (int)D(mc[1]); C.wins["umbra"] = (int)D(mc[2]); C.phase = Str(mc[3]); C.phaseEnd = D(mc[4]); C.overtime = D(mc[5]) != 0;
                var mm = (JArray)m["m"];
                M.d = D(mm[0]); M.best["zenith"] = D(mm[1]); M.best["umbra"] = D(mm[2]); M.owner = Str(mm[3]); M.contested = D(mm[4]) != 0; M.unlockAt = D(mm[5]); M.half = D(mm[6]);
                M.pos = new V3(D(mm[7]), D(mm[8]), D(mm[9])); M.checkpoint = (int)D(mm[10]); M.overtime = D(mm[11]) != 0;
                if (m["k"] is JArray mk) for (int i = 0; i < mk.Count && i < w.packs.Count; i++) w.packs[i].readyAt = D(mk[i]);
                var win = Str(m["w"]); if (!string.IsNullOrEmpty(win) && string.IsNullOrEmpty(w.winner)) w.winner = win;
            }
            if (c["z"] is JArray z)
                w.zones = z.OfType<JArray>().Select(q =>
                {
                    var kind = Str(q[1]);
                    Dictionary<string, object> data = null;
                    if (kind == "tether") { var tgt = q[9] is JObject tj ? ActorByHost((int)D(tj["target"])) : null; data = new Dictionary<string, object> { ["target"] = tgt }; }
                    else if (q[9] is JObject dj) data = dj.ToObject<Dictionary<string, object>>();
                    return new Zone { id = (int)D(q[0]), kind = kind, team = Str(q[2]), x = D(q[3]), y = D(q[4]), z = D(q[5]), r = D(q[6]), born = D(q[7]), until = D(q[8]), next = 0, data = data, owner = ActorByHost((int)D(q[10])) ?? w.actors.FirstOrDefault() };
                }).ToList();
            if (c["d"] is JObject cd && fakeDirector != null)
            {
                fakeDirector.state = Str(cd["state"]) ?? fakeDirector.state; fakeDirector.objective = Str(cd["obj"]) ?? fakeDirector.objective;
                fakeDirector.boss = D(cd["boss"]) != 0 ? ActorByHost((int)D(cd["boss"])) : null;
            }
        }

        /// <summary>the host's view of my own hero vs my prediction for the input it last applied</summary>
        void Reconcile(Snap s)
        {
            if (me == null || !hostIdOf.TryGetValue(me, out var hid)) return;
            if (!s.actors.TryGetValue(hid, out var h)) return;
            int i = hist.FindIndex(x => x.seq == s.ackInput);
            if (!Predicting(me) || i < 0) return;
            var e = hist[i];
            double ex = h.x - e.x, ey = h.y - e.y, ez = h.z - e.z, err = Math.Sqrt(ex * ex + ey * ey + ez * ez);
            hist.RemoveRange(0, i + 1);
            if (err > 3.5)
            {
                // too far off (a dash, a knockback, a respawn): take the host's word for it
                me.pos = new V3(h.x, h.y, h.z); me.vel = new V3(h.vx, h.vy, h.vz);
                cx = cy = cz = 0; hist.Clear();
                return;
            }
            if (err < 0.02) return;
            cx += ex; cy += ey; cz += ez;
            for (int k = 0; k < hist.Count; k++) { var x = hist[k]; hist[k] = (x.seq, x.x + ex, x.y + ey, x.z + ez); }
        }
        /// <summary>own hero is predicted unless the host is moving it (forced moves, stuns, death)</summary>
        bool Predicting(Actor a)
        {
            double t = w.time;
            return a.alive && a.forced == null && !a.Has("stun", t) && !a.Has("root", t) && !a.Has("knockdown", t) && !a.Has("reborn", t);
        }

        /// <summary>once per rendered frame: send input, place everything for this frame</summary>
        public void Apply(double dt, SimInput myInput)
        {
            double now = Clock.Now;
            if (double.IsNaN(offset)) return;
            double hostNow = now / 1000 + offset;
            w.time = hostNow;
            Stats(now);
            // ---- interpolation delay: ~1.5 update intervals + the arrival jitter, eased
            double iv = 1000.0 / Quality.TIER[tier].snapHz;
            double want = Math.Max(25, Math.Min(300, iv * 1.5 + arrJit * 2 + 6));
            interpMs += (want - interpMs) * Math.Min(1, dt * 1.5);
            double rt = hostNow - interpMs / 1000;
            // ---- input to the host
            if (myInput != null) SendInput(dt, myInput, now);
            if (buf.Count == 0) return;
            var newest = buf[buf.Count - 1];
            // ---- actors: create / remove from the newest snapshot, then place
            var seenIds = new HashSet<int>();
            foreach (var kv in newest.actors)
            {
                int id = kv.Key; var h = kv.Value;
                seenIds.Add(id);
                coldA.TryGetValue(id, out var c);
                if (!map.TryGetValue(id, out var a))
                {
                    if (c == null) continue;                                     // its hero isn't known yet (cold state on its way)
                    var def = new[] { Str(c["b"]), Str(c["d"]) }.FirstOrDefault(x => x != null && w.DefOf(x) != null);
                    if (def == null) continue;
                    a = w.AddHero(def, Str(c["tm"]));
                    a.controller = null; a.noRespawn = true;
                    a.pos = new V3(h.x, h.y, h.z); a.yaw = h.yaw; a.pitch = h.pitch;
                    map[id] = a; hostIdOf[a] = id;
                }
                if (c != null) ApplyActorCold(a, c);
                bool mine = !string.IsNullOrEmpty(a.netId) && a.netId == s.Me;
                if (mine) { if (me != a) { me = a; hist.Clear(); } a.isPlayer = true; }
                ApplyVitals(a, h, mine);
            }
            foreach (var kv in map.Where(kv => !seenIds.Contains(kv.Key)).ToList())
            {
                map.Remove(kv.Key); hostIdOf.Remove(kv.Value); w.actors.Remove(kv.Value);
                if (me == kv.Value) me = null;
            }
            // remote heroes between snapshots
            Snap s0 = null, s1 = null;
            for (int i = buf.Count - 1; i >= 0; i--) if (buf[i].time <= rt) { s0 = buf[i]; s1 = i + 1 < buf.Count ? buf[i + 1] : null; break; }
            foreach (var kv in map)
            {
                var a = kv.Value; int id = kv.Key;
                if (a == me) continue;
                HotActor h0 = default, h1 = default;
                bool has1 = s1 != null && s1.actors.TryGetValue(id, out h1), has0 = s0 != null && s0.actors.TryGetValue(id, out h0);
                if (has0 && has1)
                {
                    double f = Math.Max(0, Math.Min(1, (rt - s0.time) / Math.Max(1e-4, s1.time - s0.time)));
                    a.pos = new V3(h0.x + (h1.x - h0.x) * f, h0.y + (h1.y - h0.y) * f, h0.z + (h1.z - h0.z) * f);
                    a.yaw = K.LerpAngle(h0.yaw, h1.yaw, f); a.pitch = h0.pitch + (h1.pitch - h0.pitch) * f; a.vel = new V3(h1.vx, h1.vy, h1.vz);
                }
                else
                {
                    // past the newest snapshot (loss / a late packet): carry on along its velocity for up to 0.2 s
                    var src = s0 ?? newest;
                    if (!src.actors.TryGetValue(id, out var h) && !newest.actors.TryGetValue(id, out h)) continue;
                    double k = Math.Max(-0.1, Math.Min(0.2, rt - src.time));
                    a.pos = new V3(h.x + h.vx * k, h.y + h.vy * k, h.z + h.vz * k); a.yaw = h.yaw; a.pitch = h.pitch; a.vel = new V3(h.vx, h.vy, h.vz);
                }
                a.input.yaw = a.yaw; a.input.pitch = a.pitch;
            }
            // own hero: predicted movement (input applied at once), corrections eased in
            if (me != null)
            {
                if (Predicting(me))
                {
                    if (myInput != null) { CopyInput(me.input, myInput); me.yaw = myInput.yaw; me.pitch = myInput.pitch; }
                    // (sub-stepped: a slow frame still moves the hero the whole way, as the host's fixed steps will)
                    for (double left = Math.Min(dt, 0.25); left > 1e-4; left -= 1.0 / 60) w.Move(me, Math.Min(left, 1.0 / 60));
                    double k = Math.Min(1, dt * 10);
                    me.pos = new V3(me.pos.x + cx * k, me.pos.y + cy * k, me.pos.z + cz * k);
                    cx -= cx * k; cy -= cy * k; cz -= cz * k;
                }
                else
                {
                    if (hostIdOf.TryGetValue(me, out var hid) && newest.actors.TryGetValue(hid, out var h))
                    {
                        double k = Math.Min(0.1, Math.Max(0, hostNow - newest.time));
                        me.pos = new V3(h.x + h.vx * k, h.y + h.vy * k, h.z + h.vz * k); me.vel = new V3(h.vx, h.vy, h.vz);
                    }
                    hist.Clear(); cx = cy = cz = 0;
                    if (myInput != null) { me.yaw = myInput.yaw; me.pitch = myInput.pitch; }
                }
            }
            // ---- projectiles: the newest rows, drawn at the same moment as the heroes around them
            var projs = new List<Proj>(); double kp = Math.Max(-0.25, Math.Min(0.2, rt - newest.time));
            foreach (var hp in newest.projs)
            {
                if (!projMeta.TryGetValue(hp.id, out var m)) continue;
                if (!projObj.TryGetValue(hp.id, out var p))
                {
                    p = new Proj
                    {
                        id = (int)hp.id, fx = Str(m[0]), heal = D(m[1]) != 0, splash = D(m[2]), owner = ActorByHost((int)D(m[3])) ?? w.actors.FirstOrDefault(), grav = D(m[4]), r = D(m[5]), team = Str(m[6]),
                        dmg = 0, life = 1, crit = 1, born = newest.time,
                    };
                    projObj[hp.id] = p;
                }
                p.pos = new V3(hp.x + hp.vx * kp, hp.y + hp.vy * kp - 0.5 * p.grav * kp * Math.Abs(kp), hp.z + hp.vz * kp);
                p.vel = new V3(hp.vx, hp.vy, hp.vz);
                projs.Add(p);
            }
            var live = new HashSet<long>(newest.projs.Select(p => p.id));
            long oldest = live.Count > 0 ? live.Min() : long.MaxValue;
            foreach (var id in projObj.Keys.Where(id => !live.Contains(id)).ToList()) projObj.Remove(id);
            // (ids only grow: meta older than every live projectile belongs to one that is gone)
            foreach (var id in projMeta.Keys.Where(id => !live.Contains(id) && (id < oldest || projMeta.Count > 600)).ToList()) projMeta.Remove(id);
            w.projs = projs;
            if (own != null && me != null) ApplyOwn(me, own);
        }

        static void CopyInput(SimInput to, SimInput from)
        {
            to.mx = from.mx; to.mz = from.mz; to.yaw = from.yaw; to.pitch = from.pitch;
            to.jump = from.jump; to.jumpHeld = from.jumpHeld; to.descend = from.descend; to.fire = from.fire; to.alt = from.alt;
            to.a1 = from.a1; to.a2 = from.a2; to.ult = from.ult; to.reload = from.reload; to.melee = from.melee; to.swoop = from.swoop;
            if (from.grind.HasValue) to.grind = from.grind;
        }

        void ApplyActorCold(Actor a, JObject c)
        {
            var d = Str(c["d"]);
            if (d != null && a.def.id != d) { var nd = w.DefOf(d); if (nd != null) a.def = nd; }
            a.netId = Str(c["n"]) ?? ""; a.team = Str(c["tm"]) ?? a.team; a.isRobot = D(c["rob"]) != 0;
            double boss = D(c["boss"]);
            if (boss > 0 && !a.isBoss) { a.isBoss = true; if (boss > a.def.hp) { var bd = JObject.FromObject(a.def).ToObject<HeroDef>(); bd.hp = boss; a.def = bd; } }
            a.owner = D(c["own"]) != 0 ? ActorByHost((int)D(c["own"])) : null;
            a.st = c["st"] is JObject st ? st.Properties().ToDictionary(p => p.Name, p => (double)p.Value) : new Dictionary<string, double>();
            if (c["sv"] is JObject sv) foreach (var p in sv.Properties()) a.sv[p.Name] = (double)p.Value;
            a.forced = c["f"] is JArray f && f.Count >= 2 ? new Forced { kind = Str(f[0]), until = D(f[1]) } : null;
            var an = a.anim;
            if (c["an"] is JArray x && x.Count >= 12)
            {
                an.attackAt = D(x[0]); an.attackKind = Str(x[1]) ?? an.attackKind; an.attackSide = D(x[2]); an.castAt = D(x[3]); an.castId = Str(x[4]) ?? "";
                an.hitAt = D(x[5]); an.landAt = D(x[6]); an.jumpAt = D(x[7]); an.fireL = D(x[8]); an.fireR = D(x[9]); an.deflectAt = D(x[10]); an.deflectN = (int)D(x[11]);
            }
            if (c["k"] is JArray k && k.Count >= 7)
            {
                a.kills = (int)D(k[0]); a.deaths = (int)D(k[1]); a.assists = (int)D(k[2]); a.dmgDone = D(k[3]); a.healDone = D(k[4]); a.deathAt = D(k[5]); a.respawnAt = D(k[6]);
                if (k.Count > 8) a.mitigated = D(k[8], a.mitigated);
                if (a != me) a.ult = D(k[7]) / 100 * a.def.ult.charge;
            }
        }
        void ApplyVitals(Actor a, HotActor h, bool mine)
        {
            a.hp = h.hp; a.armor = h.armor; a.maxArmor = Math.Max(a.maxArmor, h.armor);
            a.shields = h.shield > 0 ? new List<Shield> { new Shield { amt = h.shield, until = w.time + 1, kind = "net" } } : new List<Shield>();
            a.alive = (h.flags & K.AF_ALIVE) != 0; a.scale = h.scale > 0 ? h.scale : 1;
            a.barrier.up = (h.flags & K.AF_BARRIER) != 0; a.barrier.hp = h.bhp;
            a.beamOn = (h.flags & K.AF_BEAM) != 0; a.flameOn = (h.flags & K.AF_FLAME) != 0; a.charging = (h.flags & K.AF_CHARGING) != 0;
            a.beamTarget = h.beam != 0 ? ActorByHost(h.beam) : null;
            if (!mine || !Predicting(a)) { a.grounded = (h.flags & K.AF_GROUNDED) != 0; a.flying = (h.flags & K.AF_FLYING) != 0; }
        }
        void ApplyOwn(Actor me, JObject o)
        {
            me.ult = D(o["u"], me.ult); me.ammo = D(o["am"], me.ammo); me.reloadUntil = D(o["rl"], me.reloadUntil);
            me.cd = o["cd"] is JObject cd ? cd.Properties().ToDictionary(p => p.Name, p => (double)p.Value) : me.cd;
            me.flight = D(o["fl"], me.flight); me.charge = D(o["ch"], me.charge);
            if (o["sh"] is JArray sh && sh.Count >= 7)
            {
                me.shots = (int)D(sh[0]); me.hits = (int)D(sh[1]); me.crits = (int)D(sh[2]); me.objTime = D(sh[3]); me.ults = (int)D(sh[4]); me.bestStreak = (int)D(sh[5]); me.streak = (int)D(sh[6]);
            }
            if (o["x"] is JObject x) me.stats = x.Properties().ToDictionary(p => p.Name, p => (double)p.Value);
            me.cash = D(o["cash"], me.cash);
            if (o["it"] is JArray it) me.items = it.Select(t => (string)t).ToList();
            if (o["pw"] is JArray pw) me.powers = pw.Select(t => (string)t).ToList();
        }

        void SendInput(double dt, SimInput i, double now)
        {
            // rising edges since the last frame -> press counters; held = anything held since the last packet
            int bits = 0;
            for (int b = 0; b < K.IN_BITS.Length; b++) if (Wire.Btn(i, K.IN_BITS[b])) bits |= 1 << b;
            for (int e = 0; e < K.IN_EDGES.Length; e++)
            {
                var k = K.IN_EDGES[e]; bool v = Wire.Btn(i, k);
                if (v && !(prevHeld.TryGetValue(k, out var pv) && pv)) presses[e] = (presses[e] + 1) & 0xff;
                prevHeld[k] = v;
            }
            orHeld |= bits;
            inT += dt;
            int hz = Quality.TIER[tier].inputHz;
            if (inT < 1.0 / hz) return;
            inT = 0;
            inSeq = (inSeq + 1) & 0xffff;
            var wr = w0.Reset();
            K.WriteInput(wr, new InputPacket { seq = inSeq, ackSnap = Math.Max(0, lastSeq), yaw = i.yaw, pitch = i.pitch, mx = i.mx, mz = i.mz, held = orHeld | bits, presses = presses, viewMs = (int)K.Round(interpMs) });
            orHeld = 0;
            Link?.SendBin(wr.Done());
            if (me != null) { hist.Add((inSeq, me.pos.x, me.pos.y, me.pos.z)); if (hist.Count > 120) hist.RemoveAt(0); }
        }

        void Stats(double now)
        {
            if (recvAt == 0) recvAt = now;
            if (now - recvAt >= 1000)
            {
                snapHz = recvCount * 1000 / (now - recvAt); recvCount = 0; recvAt = now;
                loss = gapExp > 0 ? Math.Max(0, 1 - (double)gapSeen / gapExp) : 0; gapExp = gapSeen = 0;
            }
        }
    }
}
