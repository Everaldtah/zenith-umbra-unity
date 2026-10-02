// A networked match (online PvP or campaign co-op): what the front end prepares when a session starts a match, and what
// the match runner calls while it plays - the TS Game.start({ net }) path (src/client/Game.ts + OnlineUI.launch).
//   UI:        NetMatch.Prepare(start, onlineSession)  or  NetMatch.PrepareCoop(level, squad, coopSession)
//   runner:    var m = NetMatch.Current.Build(skill, level, nav, onClientEvent)   (host: the real match; client: a mirror)
//              each sim step (host only - a client never steps its world):
//                  BeforeStep(); world.Step(dt); AfterStep(); Capture(that step's events)
//              each rendered frame: Frame(dt, the local player's input)
//              when the match is over / left: End()
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Net
{
    public sealed class NetMatch
    {
        /// <summary>the match being played over the network (null offline)</summary>
        public static NetMatch Current { get; private set; }

        public readonly INetSession Session;
        public bool IsHost => Session.Role == "host";
        /// <summary>the World mode: "quickplay" | "competitive" | "campaign"</summary>
        public readonly string Mode;
        /// <summary>the map id (PvP) or the campaign level id (co-op)</summary>
        public readonly string Map;
        /// <summary>this player's hero</summary>
        public readonly string Hero;
        /// <summary>online PvP: the start message (seats, seed); null in co-op</summary>
        public readonly OnlineStart Start;
        /// <summary>online PvP: "online-qp" | "online-comp" | "custom" (the career mode the result is filed under)</summary>
        public readonly string Career;
        /// <summary>online PvP: this player's queued role, side, the enemy humans' average rating (AI play at the humans' average)</summary>
        public readonly string MyRole, MyTeam;
        public readonly double Opp, AvgMmr;
        /// <summary>the AI's skill for this match (TS ranks.skillFor of the humans' average rating; co-op: the host's difficulty)</summary>
        public readonly double Skill;
        readonly List<Setup.OnlineSlot> slots;
        readonly List<(string hero, string netId)> squad;
        public FastHost Host { get; private set; }
        public FastClient Client { get; private set; }
        public Match Built { get; private set; }

        NetMatch(INetSession s, string mode, string map, string hero, OnlineStart st, string career, string myRole, string myTeam, double opp, double avg, double skill,
            List<Setup.OnlineSlot> slots, List<(string, string)> squad)
        {
            Session = s; Mode = mode; Map = map; Hero = hero; Start = st; Career = career; MyRole = myRole; MyTeam = myTeam; Opp = opp; AvgMmr = avg; Skill = skill;
            this.slots = slots; this.squad = squad;
        }

        /// <summary>TS ranks.skillFor (also ZU.Game.Career.Ranks.SkillFor)</summary>
        public static double SkillFor(double mmr) => Math.Max(0.3, Math.Min(0.97, 0.3 + (mmr - 800) / 3200 * 0.67));

        /// <summary>OnlineSession.OnStart -> this, then start the match (null: the match started without you - your link to the
        /// host never came up; the session has been left)</summary>
        public static NetMatch Prepare(OnlineStart st, OnlineSession s)
        {
            var me = st.seats.FirstOrDefault(x => x.id == s.Me);
            if (me == null) { s.Leave(); return null; }
            string mode = st.q == "comp" ? "competitive" : "quickplay";
            string career = st.q == "comp" ? "online-comp" : st.q == "qp" ? "online-qp" : "custom";
            double avg = st.seats.Sum(x => x.mmr) / Math.Max(1, st.seats.Count);
            var enemy = st.seats.Where(x => x.team != me.team).ToList();
            double opp = enemy.Count > 0 ? enemy.Average(x => x.mmr) : avg;
            var sl = st.seats.Select(x => new Setup.OnlineSlot { hero = x.hero, team = x.team, netId = x.id == s.Me ? "local" : x.id }).ToList();
            return Current = new NetMatch(s, mode, st.map, me.hero, st, career, me.role, me.team, opp, avg, SkillFor(avg), sl, null);
        }
        /// <summary>CoopSession.OnStart -> this, then start the campaign level</summary>
        public static NetMatch PrepareCoop(string level, List<Member> members, CoopSession s, double skill = 0.7)
        {
            var me = members.FirstOrDefault(x => x.id == s.Me);
            var sq = members.Select(x => (x.hero, x.id == s.Me ? "local" : x.id)).ToList();
            return Current = new NetMatch(s, "campaign", level, me?.hero ?? "tenkai", null, "campaign", null, "zenith", 0, 0, skill, null, sq);
        }

        /// <summary>the world to play. Host: the match with the humans' heroes (+ AI); client: an empty mirror world, filled from
        /// the host's snapshots. `level` / `nav`: the map's collision and navigation (defaults: the box level).
        /// `onClientEvent`: what the client does with the host's events (damage numbers, kills, sounds, effects).</summary>
        public Match Build(double skillOverride = -1, ILevel level = null, INav nav = null, Action<SimEvent> onClientEvent = null)
        {
            double skill = skillOverride >= 0 ? skillOverride : Skill;
            var D = GameData.Current;
            if (!IsHost)
            {
                bool camp = Mode == "campaign";
                CampaignLevel lvl = camp ? CampaignLevel.All(D)[Map] : null;
                var w = new World(camp ? lvl.map : D.Map[Map], Mode, true, null, level);
                w.nav = nav ?? new BoxNav((BoxLevel)w.level);
                Client = new FastClient(w, Session, onClientEvent, lvl);
                return Built = new Match { world = w, nav = w.nav, player = null };
            }
            Built = Mode == "campaign"
                ? Director.CreateCampaign(Map, squad, skill, level, nav)
                : Setup.CreateOnlineMatch(Map, Mode, slots, skill, null, level, nav);
            Host = new FastHost(Built.world, Session) { upKbps = Quality.CachedSpeed(60 * 60_000)?.upKbps ?? 0 };
            // a player who drops out mid-match: the AI takes over their hero (as Overwatch backfills)
            Host.OnPeerLost = a =>
            {
                var m = Built; if (m == null || m.world != Host.w) return;
                a.netId = "";
                if (a.isRobot || m.world.mode == "campaign" && a.team != "zenith") return;
                var b = new Bot(m.world, a, m.nav, skill); a.controller = b; m.bots.Add(b);
                m.world.Msg("A PLAYER LEFT - THE AI TAKES THEIR HERO", "#ffd76a");
            };
            return Built;
        }

        /// <summary>a client never steps its world (the host's snapshots place everything)</summary>
        public bool StepsWorld => IsHost;
        /// <summary>the local player's hero: the host's own, or (client) the mirror of it once the first snapshots are in</summary>
        public Actor Player => IsHost ? Built?.player : Client?.me;
        public void BeforeStep() => Host?.BeforeStep();
        public void AfterStep() => Host?.AfterStep();
        public void Capture(IEnumerable<SimEvent> stepEvents) => Host?.Capture(stepEvents);
        /// <summary>once per rendered frame: the host streams its snapshots; a client sends its input and places everything</summary>
        public void Frame(double dt, SimInput myInput)
        {
            if (Host != null) Host.Flush();
            else Client?.Apply(dt, myInput);
        }
        /// <summary>the match is over or left: stop syncing (OnlineSession.Finish is the front end's: 4 s after a host's result)</summary>
        public void End()
        {
            if (Built?.world != null && Built.world.rewind != null) Built.world.rewind = null;
            if (Current == this) Current = null;
        }

        // ---------------------------------------------------------------- the in-match readout (top-left net HUD)
        /// <summary>host: every peer's link</summary>
        public List<LinkInfo> PeerLinks() => Host == null ? new List<LinkInfo>() : Session.Links.Keys.Select(Host.Info).Where(x => x != null).ToList();
        PeerLink HostLink => Client?.Link;
        public string Tier => Client?.tier ?? "medium";
        public double SnapHz => Client?.snapHz ?? 0;
        public double Loss => Client?.loss ?? 0;
        public double InterpMs => Client?.interpMs ?? 0;
        public int Rtt => (int)K.Round(HostLink?.stats.rtt ?? 0);
        public string Path => HostLink?.stats.path ?? "?";
    }
}
