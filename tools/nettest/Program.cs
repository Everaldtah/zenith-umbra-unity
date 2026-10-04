// Headless netcode tests (ports of zenith-umbra tests/unit/online.test.ts, plus the C# port's own):
//   unit                 wire format, tiers, the online match setup, host <-> client replication in memory, the press latch,
//                        events on the wire, links over an in-memory transport
//   node [url]           two OnlineSessions matched through a real node (zenith-umbra scripts/net-local.mjs, ZU_GATHER_MS
//                        short), linked through its relay, a match streamed host -> client
//   golden <vectors>     byte-exact packets vs the TS codec (vectors written by tools/nettest/golden.mjs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using ZU.Net;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.NetTest
{
    static class Program
    {
        static int fails, passes;
        static void Ok(bool c, string what) { if (c) passes++; else { fails++; Console.WriteLine("  FAIL " + what); } }
        static void Near(double a, double b, double tol, string what) => Ok(Math.Abs(a - b) <= tol, $"{what}: {a} vs {b} (+-{tol})");
        static void Test(string name, Action body)
        {
            int f0 = fails;
            try { body(); } catch (Exception e) { fails++; Console.WriteLine("  FAIL " + name + ": " + e); }
            Console.WriteLine((fails == f0 ? "ok   " : "FAIL ") + name);
        }

        static int Main(string[] args)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            GameData.Load(Path.Combine(root, "Assets/ZU/Resources/ZUData"));
            var cmd = args.Length > 0 ? args[0] : "unit";
            switch (cmd)
            {
                case "unit": Unit(); break;
                case "node": NodeTest(args.Length > 1 ? args[1] : NetConfig.DEFAULT_NODE); break;
                case "golden": Golden(args[1]); break;
                default: Console.Error.WriteLine("unknown command " + cmd); return 2;
            }
            Console.WriteLine($"{passes} passed, {fails} failed");
            return fails == 0 ? 0 : 1;
        }

        // ================================================================ unit
        static void Unit()
        {
            Test("wire: round-trips a snapshot (header, hot rows, cold JSON)", () =>
            {
                var w = new Writer(16);
                K.WriteSnapHeader(w, new SnapHeader { seq = 65535, time = 123.456, ackInput = 77, tier = 2, hostMs = 99999 });
                K.WriteHot(w, new[] { new HotActor { id = 7, x = 12.345, y = 1.5, z = -40.25, vx = 3.21, vy = -9.8, vz = 0.5, yaw = -2.5, pitch = 0.3, hp = 250, armor = 25, shield = 75, bhp = 1400, flags = K.AF_ALIVE | K.AF_GROUNDED, scale = 2.2, beam = 3 } },
                    new[] { new HotProj { id = 100001, x = 1, y = 2, z = 3, vx = 60, vy = -4, vz = 0 } });
                w.Str(new JObject { ["a"] = new JObject { ["7"] = new JObject { ["d"] = "raijin" } } }.ToString(Newtonsoft.Json.Formatting.None));
                var r = new Reader(w.Done());
                var h = K.ReadSnapHeader(r);
                Ok(h.seq == 65535 && h.ackInput == 77 && h.tier == 2, "header");
                Near(h.time, 123.456, 1e-3, "time");
                var (actors, projs) = K.ReadHot(r);
                var a = actors[0];
                Ok(a.id == 7 && a.hp == 250 && a.armor == 25 && a.shield == 75 && a.bhp == 1400 && a.beam == 3 && a.flags == (K.AF_ALIVE | K.AF_GROUNDED), "actor ints");
                Near(a.x, 12.345, 1e-4, "x"); Near(a.vx, 3.21, 1e-2, "vx"); Near(a.yaw, -2.5, 1e-3, "yaw"); Near(a.scale, 2.2, 0.05, "scale");
                Ok(projs[0].id == 100001, "proj id"); Near(projs[0].vx, 60, 0.05, "proj vx");
                Ok((string)JObject.Parse(r.Str())["a"]["7"]["d"] == "raijin", "cold");
                Ok(K.ACTOR_BYTES == 36, "36-byte actor rows");
            });
            Test("wire: round-trips an input packet; sequence numbers wrap", () =>
            {
                var w = new Writer(8);
                K.WriteInput(w, new InputPacket { seq = 3, ackSnap = 65000, yaw = 1.25, pitch = -0.4, mx = -1, mz = 0.7, held = 5, presses = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 255 }, viewMs = 85 });
                var p = K.ReadInput(new Reader(w.Done()));
                Ok(p.seq == 3 && p.ackSnap == 65000 && p.held == 5 && p.viewMs == 85, "fields");
                Near(p.mx, -1, 1e-9, "mx"); Near(p.mz, 0.7, 1e-9, "mz");
                Ok(p.presses.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 255 }), "presses");
                Ok(K.SeqNewer(2, 65530) && !K.SeqNewer(65530, 2), "seq wrap");
                Ok(K.FromB64(K.ToB64(new byte[] { 0, 255, 7 })).SequenceEqual(new byte[] { 0, 255, 7 }), "base64");
            });
            Test("wire: JS rounding and truncation", () =>
            {
                Ok(K.Round(2.5) == 3 && K.Round(-2.5) == -2 && K.Round(-0.4) == 0, "Math.round half up");
                var w = new Writer(8); w.U8(54.99).U8(300).U32(-1.5).I16(-2.5);
                var r = new Reader(w.Done());
                Ok(r.U8() == 54, "u8 truncates (v & 0xff)"); Ok(r.U8() == 44, "u8 wraps"); Ok(r.U32() == 4294967295, "u32 = v >>> 0"); Ok(r.I16() == -2, "i16 rounds half up");
            });
            Test("tiers: classifies links and fits rates to bandwidth", () =>
            {
                Ok(Quality.Classify(25, 3, 0) == "ultra", "ultra"); Ok(Quality.Classify(120, 10, 0) == "high", "high");
                Ok(Quality.Classify(180, 10, 0) == "medium", "medium"); Ok(Quality.Classify(40, 4, 0.2) == "low", "low");
                Ok(Quality.Classify(20, 1, 0, true) == "relay", "relay");
                Ok(Quality.FitTier(100000, 1400) == "ultra", "fit ultra"); Ok(Quality.FitTier(300, 1400) == "low", "fit low");
            });
            Test("tiers: drop at once, climb back one step at a time after a clean spell", () =>
            {
                var s = new LinkStats(); double now = 0;
                for (int i = 0; i < 10; i++) { int n = s.NextPing(now); s.Pong(n, now + 20); now += 250; }
                var t = new AdaptiveTier("ultra", 4000);
                Ok(t.Update(s, now, false) == "ultra", "starts ultra");
                s.rtt = 200;
                Ok(t.Update(s, now += 100, false) == "medium", "spike drops at once");
                s.rtt = 20;
                Ok(t.Update(s, now += 1000, false) == "medium", "no climb before 4 s");
                Ok(t.Update(s, now += 4100, false) == "high", "one step up");
                Ok(t.Update(s, now += 4100, false) == "ultra", "another step");
                Ok(Quality.TIER["ultra"].snapHz == 60, "ultra = 60 Hz");
            });
            Test("an online match: humans take their seats, AI fills the rest of both sides", () =>
            {
                var m = Setup.CreateOnlineMatch("hanabi", "quickplay", new List<Setup.OnlineSlot> { new Setup.OnlineSlot { hero = "raijin", team = "zenith", netId = "local" }, new Setup.OnlineSlot { hero = "enra", team = "umbra", netId = "peer1" } }, 0.6, () => 0.3);
                var w = m.world;
                Ok(w.actors.Count == 10, "10 heroes: " + w.actors.Count);
                Ok(m.player?.def.id == "raijin", "the local player");
                Ok(w.actors.FirstOrDefault(a => a.netId == "peer1")?.def.id == "enra", "the remote player");
                Ok(m.bots.Count == 8, "8 bots");
                foreach (var team in new[] { "zenith", "umbra" })
                {
                    var side = w.actors.Where(a => a.team == team).ToList();
                    Ok(side.Count == 5, team + " has 5"); Ok(side.Select(a => a.def.id).Distinct().Count() == 5, team + " no duplicates");
                    Ok(side.Count(a => a.def.role == "tank") == 1, team + " one tank");
                }
            });
            Test("events: packed with actor ids, unpacked onto the client's actors", () =>
            {
                var m = Setup.CreateMatch("hanabi", "skirmish", "raijin");
                Actor a = m.world.actors[0], b = m.world.actors[1];
                var j = Wire.Pack(new DmgEvent { src = a, tgt = b, amt = 42.5, crit = true, heal = false, pos = new V3(1, 2, 3) });
                Ok((string)j["t"] == "dmg" && (int)j["src"] == a.id && (int)j["tgt"] == b.id, "packed: " + j);
                var back = Wire.Unpack(JObject.Parse(j.ToString()), id => id == a.id ? b : id == b.id ? a : null) as DmgEvent;
                Ok(back != null && back.src == b && back.tgt == a && back.amt == 42.5 && back.crit && back.pos.z == 3, "unpacked onto mapped actors");
                Ok(Wire.Unpack(j, id => null) == null, "an unknown actor drops the event");
                var fx = Wire.Pack(new FxEvent("spark", new V3(4, 5, 6), new FxOpts { r = 2, color = "#fff" }));
                var fxb = Wire.Unpack(fx, id => null) as FxEvent;
                Ok(fxb != null && fxb.kind == "spark" && fxb.r == 2 && fxb.color == "#fff" && fxb.to == null && fxb.pos.y == 5, "fx with optional fields: " + fx);
            });
            Test("links over a transport: pings measure the round trip, JSON and binary both ways", () =>
            {
                var (h, c, hp, cp) = MemLinks();
                var gotText = new List<string>(); var gotBin = new List<byte[]>();
                c.OnMessage = m => gotText.Add((string)m["t"]); h.OnBinary = u => gotBin.Add(u);
                for (int i = 0; i < 40; i++) { h.Tick(); c.Tick(); hp.Deliver(); cp.Deliver(); Thread.Sleep(30); }      // (4 Hz pings: 3+ samples)
                h.Send(new JObject { ["t"] = "hello" }); c.SendBin(new byte[] { 9, 1, 2 });
                hp.Deliver(); cp.Deliver();
                Ok(h.state == "p2p" && c.state == "p2p", "both p2p");
                Ok(gotText.Contains("hello"), "json arrives"); Ok(gotBin.Any(u => u[0] == 9), "binary arrives");
                Ok(h.stats.Measured && h.stats.rtt > 0 && h.stats.rtt < 200, "rtt measured: " + h.stats.rtt);
            });
            Test("replication: mirrors the host world, applies the client's input on the host, rewinds for lag compensation", Replication);
            Test("a press between two packets is never lost (latched for one step)", Latch);
        }

        // ---------------------------------------------------------------- in-memory transport
        class MemPeer : IRtcPeer
        {
            public MemPeer other; public readonly Queue<object> inbox = new Queue<object>(); bool open;
            public Action<JObject> OnCandidate { get; set; }
            public Action<JObject> OnDescription { get; set; }
            public Action OnOpen { get; set; }
            public Action OnDown { get; set; }
            public Action<string> OnText { get; set; }
            public Action<byte[]> OnBytes { get; set; }
            public void Start(bool initiator) { open = true; OnOpen?.Invoke(); }
            public bool HasRemote => true;
            public void SetRemote(JObject sdp) { }
            public void AddCandidate(JObject cand) { }
            public bool Open => open;
            public long Buffered(bool reliable) => 0;
            public void SendText(bool reliable, string s) => other.inbox.Enqueue(s);
            public void SendBytes(bool reliable, byte[] b) => other.inbox.Enqueue((byte[])b.Clone());
            public void SampleStats(Action<double, string> result) => result(0, "lan");
            public void Close() => open = false;
            /// <summary>everything queued for this side</summary>
            public void Deliver() { while (inbox.Count > 0) { var x = inbox.Dequeue(); if (x is string s) OnText?.Invoke(s); else OnBytes?.Invoke((byte[])x); } }
        }
        class NoLobby : ISignaller { public void Send(string to, JObject msg, bool reliable = true) { } }
        static (PeerLink host, PeerLink client, MemPeer hostPeer, MemPeer clientPeer) MemLinks()
        {
            var hp = new MemPeer(); var cp = new MemPeer(); hp.other = cp; cp.other = hp;
            var h = new PeerLink(new NoLobby(), "C", "s1", true, hp);
            var c = new PeerLink(new NoLobby(), "H", "s1", false, cp);
            return (h, c, hp, cp);
        }
        class TestSession : INetSession
        {
            public string Me { get; set; }
            public string Role { get; set; }
            public string HostId { get; set; }
            public Dictionary<string, PeerLink> Links { get; } = new Dictionary<string, PeerLink>();
            public Action<string, JObject> OnNetMessage { get; set; }
            public Action<string, byte[]> OnNetBinary { get; set; }
            public void Broadcast(JObject m, bool reliable = true) { foreach (var l in Links.Values) l.Send(m, reliable); }
            public void SendHost(JObject m, bool reliable = true) { if (Links.TryGetValue(HostId, out var l)) l.Send(m, reliable); }
            public void Add(string peer, PeerLink l) { Links[peer] = l; l.OnMessage = m => OnNetMessage?.Invoke(peer, m); l.OnBinary = u => OnNetBinary?.Invoke(peer, u); }
        }
        static (TestSession host, TestSession client, Action deliver, MemPeer hostPeer, Action tick) Pair()
        {
            var (h, c, hp, cp) = MemLinks();
            var hs = new TestSession { Me = "H", Role = "host", HostId = "H" }; hs.Add("C", h);
            var cs = new TestSession { Me = "C", Role = "client", HostId = "H" }; cs.Add("H", c);
            return (hs, cs, () => { hp.Deliver(); cp.Deliver(); }, hp, () => { h.Tick(); c.Tick(); });
        }
        static SimInput In(double mx = 0, double mz = 0, double yaw = 0, bool a1 = false) => new SimInput { mx = mx, mz = mz, yaw = yaw, a1 = a1 };

        static void Replication()
        {
            var (hs, cs, deliver, _, tick) = Pair();
            var m = Setup.CreateOnlineMatch("hanabi", "quickplay", new List<Setup.OnlineSlot> { new Setup.OnlineSlot { hero = "raijin", team = "zenith", netId = "local" }, new Setup.OnlineSlot { hero = "yuzu", team = "zenith", netId = "C" } }, 0.6, () => 0.5);
            var hw = m.world;
            var host = new FastHost(hw, hs);
            var cw = new World("hanabi", "quickplay");
            var client = new FastClient(cw, cs, e => { }, null);
            var remote = hw.actors.First(a => a.netId == "C");
            const double DT = 1.0 / 120;
            var x0 = remote.pos;
            // ~1.5 s of "frames": 2 host steps per frame, snapshots + inputs delivered each frame
            for (int f = 0; f < 90; f++)
            {
                tick();
                for (int s = 0; s < 2; s++) { host.BeforeStep(); hw.Step(DT); host.AfterStep(); host.Capture(hw.events); hw.events.Clear(); }
                host.Flush();
                deliver();
                client.Apply(1.0 / 60, In(0, f > 30 ? 1 : 0, remote.yaw));
                deliver();
                Thread.Sleep(16);
            }
            Ok(cw.actors.Count == hw.actors.Count, $"the client knows every hero ({cw.actors.Count}/{hw.actors.Count})");
            Ok(client.me?.def.id == "yuzu", "and which one is its own: " + client.me?.def.id);
            Ok(client.snapHz > 5, "snapshots flow: " + client.snapHz);
            double moved = Math.Sqrt(Math.Pow(remote.pos.x - x0.x, 2) + Math.Pow(remote.pos.z - x0.z, 2));
            Ok(moved > 2, "the host moved the client's hero by its input: " + moved);
            if (client.me != null)
            {
                double err = Math.Sqrt(Math.Pow(client.me.pos.x - remote.pos.x, 2) + Math.Pow(client.me.pos.z - remote.pos.z, 2));
                Ok(err < 1.5, "the client's prediction agrees with the host: " + err);
            }
            var other = hw.actors.First(a => a != remote && a.alive);
            var mirror = cw.actors.First(a => a.def.id == other.def.id && a.team == other.team);
            double d = Math.Sqrt(Math.Pow(mirror.pos.x - other.pos.x, 2) + Math.Pow(mirror.pos.z - other.pos.z, 2));
            Ok(d < 2.5, "other heroes sit near their host positions: " + d);
            Ok(cw.rules == hw.rules, "match state through the cold channel");
            Ok(hw.rewind != null, "lag compensation installed");
            double before = other.pos.x;
            var undo = hw.rewind(remote);
            if (undo != null) { undo(); Near(other.pos.x, before, 1e-6, "rewind undone"); }
            // who plays whom (PlayerNames): the two people by username, every AI seat Bot 1..8 through both teams, and the
            // client's mirror world - other actor objects, another order - gives every hero the same name
            var seats = new List<Seat> { new Seat { id = "H", name = "x", team = "zenith", hero = "raijin" }, new Seat { id = "C", name = " y<b> ", team = "zenith", hero = "yuzu" } };
            var hn = PlayerNames.Assign(seats, hw); var cn = PlayerNames.Assign(seats, cw);
            string Key(Actor a) => a.team + "/" + (a.baseDef ?? a.def).id;
            var hk = hn.ToDictionary(kv => Key(kv.Key), kv => kv.Value); var ck = cn.ToDictionary(kv => Key(kv.Key), kv => kv.Value);
            Ok(hk.Count == 10 && hk["zenith/raijin"] == "x" && hk["zenith/yuzu"] == "y‹b›", "names: the two players by username: " + string.Join(", ", hk.Select(kv => kv.Key + "=" + kv.Value)));
            var bots = hn.Where(kv => kv.Value.StartsWith("Bot ")).ToList();
            Ok(bots.Count == 8 && bots.Select(kv => kv.Value).Distinct().Count() == 8 && Enumerable.Range(1, 8).All(i => bots.Any(kv => kv.Value == "Bot " + i)), "names: the eight AI seats are Bot 1..8");
            Ok(bots.Where(kv => kv.Key.team == "zenith").All(kv => int.Parse(kv.Value.Substring(4)) <= 3) && bots.Where(kv => kv.Key.team == "umbra").All(kv => int.Parse(kv.Value.Substring(4)) >= 4), "names: Zenith's bots are 1..3, Umbra's 4..8");
            Ok(ck.Count == hk.Count && hk.All(kv => ck.TryGetValue(kv.Key, out var v) && v == kv.Value), "names: the client names every hero as the host does: " + string.Join(", ", ck.Select(kv => kv.Key + "=" + kv.Value)));
        }

        static void Latch()
        {
            var (hs, cs, deliver, hostPeer, tick) = Pair();
            var m = Setup.CreateOnlineMatch("hanabi", "quickplay", new List<Setup.OnlineSlot> { new Setup.OnlineSlot { hero = "raijin", team = "zenith", netId = "local" }, new Setup.OnlineSlot { hero = "yuzu", team = "zenith", netId = "C" } }, 0.6, () => 0.5);
            var hw = m.world; var host = new FastHost(hw, hs); var remote = hw.actors.First(a => a.netId == "C");
            var cw = new World("hanabi", "quickplay"); var client = new FastClient(cw, cs, e => { }, null);
            host.Flush(); deliver();
            client.Apply(1, In()); deliver();                     // first packet: the counters' baseline
            // a1 tapped and released inside one send interval, and the packet carrying it is LOST...
            client.Apply(0.001, In(a1: true));
            client.Apply(1, In());
            int queued = hostPeer.inbox.Count(x => x is byte[] b && b[0] == K.PK_INPUT);
            Ok(queued == 1, "one input packet sent: " + queued); hostPeer.inbox.Clear();
            // ...the next packet says a1 is up, but its press counter moved: the host still sees the press for one step
            client.Apply(1, In()); deliver();
            host.BeforeStep(); bool seen = remote.input.a1; hw.Step(1.0 / 120); host.AfterStep();
            Ok(seen, "the press is seen"); Ok(!remote.input.a1, "for one step");
        }

        // ================================================================ node: two sessions through a real node
        static void NodeTest(string url)
        {
            Test($"two players are matched through the node ({url}), link through its relay, and the host streams the match", () =>
            {
                var maps = new List<string> { "hanabi" };
                var A = new OnlineSession("Alpha", maps, null, url); var B = new OnlineSession("Bravo", maps, null, url);
                OnlineStart stA = null, stB = null;
                A.OnStart = s => stA = s; B.OnStart = s => stB = s;
                var notices = new List<string>(); A.OnNotice = n => notices.Add("A: " + n); B.OnNotice = n => notices.Add("B: " + n);
                void Pump(double ms) { var end = Clock.Now + ms; while (Clock.Now < end) { A.Tick(); B.Tick(); Thread.Sleep(15); } }
                bool Until(Func<bool> c, double ms) { var end = Clock.Now + ms; while (Clock.Now < end) { A.Tick(); B.Tick(); if (c()) return true; Thread.Sleep(15); } return c(); }
                Ok(Until(() => A.Online && B.Online, 8000), "both reach the node");
                A.Queue("qp", "flex", 1800); B.Queue("qp", "flex", 1700);
                Ok(Until(() => A.Phase == Phase.Assemble && B.Phase == Phase.Assemble, 15000), $"a match forms from two queued players (A {A.Phase}, B {B.Phase}) {string.Join(" | ", notices)}");
                Ok(A.Match == B.Match && A.Match != "", "the same match: " + A.Match);
                var host = A.Role == "host" ? A : B; var cli = host == A ? B : A;
                Ok(cli.Role == "client" && host.HostId == host.Me && cli.HostId == host.Me, "one host, one client");
                Ok(Until(() => host.Links.Values.Any(l => l.state == "relay") && cli.Links.Values.Any(l => l.state == "relay"), 10000), "linked (relay: no WebRTC headless)");
                // the client picks a hero for its seat through the link; the host sees it
                Ok(Until(() => cli.MySeat != null, 5000), "the client has its seat");
                var pool = OnlineSession.HeroPool(cli.MySeat.team, cli.MySeat.role);
                cli.Pick(pool[0].id);
                Ok(Until(() => host.Seats.Any(s => s.id == cli.Me && s.hero == pool[0].id), 8000), "the host has the client's pick: " + pool[0].id);
                Ok(Until(() => stA != null && stB != null, 70000), "the host starts at the end of the gather window, both get the start");
                if (stA == null || stB == null) return;
                Ok(stA.seats.Count == 2 && stA.map == "hanabi", "two humans on hanabi");
                // the match itself: the host world streamed to the client through the node's relay
                var slots = stA.seats.Select(x => new Setup.OnlineSlot { hero = x.hero, team = x.team, netId = x.id == host.Me ? "local" : x.id }).ToList();
                var hm = Setup.CreateOnlineMatch(stA.map, "quickplay", slots, 0.5);
                var fh = new FastHost(hm.world, host);
                var cw = new World(stA.map, "quickplay");
                var fc = new FastClient(cw, cli, e => { }, null);
                var end = Clock.Now + 6000;
                while (Clock.Now < end)
                {
                    A.Tick(); B.Tick();
                    fh.BeforeStep(); hm.world.Step(1.0 / 60); fh.AfterStep(); fh.Capture(hm.world.events); hm.world.events.Clear();
                    fh.Flush(); fc.Apply(1.0 / 60, In(0, 1));
                    Thread.Sleep(16);
                }
                Ok(cw.actors.Count == 10, "the client mirrors all 10 heroes through the relay: " + cw.actors.Count);
                Ok(fc.me != null && fc.me.def.id == pool[0].id, "its own hero: " + fc.me?.def.id);
                Ok(fc.tier == "relay", "the host streams at the relay tier: " + fc.tier);
                A.Close(); B.Close();
            });
        }

        // ================================================================ golden: byte-exact vs the TS codec
        static void Golden(string file)
        {
            var v = JObject.Parse(File.ReadAllText(file));
            Test("golden: the snapshot packet is byte-identical to the TS codec's", () =>
            {
                var s = (JObject)v["snap"]; var w = new Writer(16);
                var h = (JObject)s["header"];
                K.WriteSnapHeader(w, new SnapHeader { seq = (int)h["seq"], time = (double)h["time"], ackInput = (int)h["ackInput"], tier = (int)h["tier"], hostMs = (double)h["hostMs"] });
                K.WriteHot(w, s["actors"].Select(a => a.ToObject<HotActor>()).ToList(), s["projs"].Select(p => p.ToObject<HotProj>()).ToList());
                w.Str((string)s["cold"]);
                var mine = BitConverter.ToString(w.Done()).Replace("-", "").ToLowerInvariant();
                Ok(mine == (string)s["hex"], $"snapshot\n    C#: {mine}\n    TS: {(string)s["hex"]}");
            });
            Test("golden: the input packet is byte-identical to the TS codec's", () =>
            {
                var p = v["input"]["packet"].ToObject<InputPacket>(); var w = new Writer(8);
                K.WriteInput(w, p);
                var mine = BitConverter.ToString(w.Done()).Replace("-", "").ToLowerInvariant();
                Ok(mine == (string)v["input"]["hex"], $"input\n    C#: {mine}\n    TS: {(string)v["input"]["hex"]}");
            });
            Test("golden: the TS packets read back the same in C#", () =>
            {
                var bytes = Enumerable.Range(0, ((string)v["snap"]["hex"]).Length / 2).Select(i => Convert.ToByte(((string)v["snap"]["hex"]).Substring(i * 2, 2), 16)).ToArray();
                var r = new Reader(bytes); var h = K.ReadSnapHeader(r); var (actors, projs) = K.ReadHot(r);
                Ok(h.seq == (int)v["snap"]["header"]["seq"] && actors.Count == ((JArray)v["snap"]["actors"]).Count && projs.Count == ((JArray)v["snap"]["projs"]).Count, "counts");
                Ok(r.Str() == (string)v["snap"]["cold"], "cold string");
            });
        }
    }
}
