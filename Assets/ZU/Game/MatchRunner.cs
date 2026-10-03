// Runs a match: builds the World from the ported simulation, steps it at a fixed 120 Hz (the desktop edition's rate),
// feeds the local player's controls in, and keeps the views (level, heroes, projectiles, camera, HUD) in step.
// Views draw between the last two simulation steps (render interpolation), so motion is smooth at any frame rate.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.UI;
using ZU.Net;
using ZU.Sim;

namespace ZU.Game
{
    public class MatchRunner : MonoBehaviour, IViewHost
    {
        public const double DT = 1.0 / 120;

        [Tooltip("map id from the game data (hanabi, cloudstep, kagura, lantern, starfall, foundry, mile, gulch, training)")]
        public string mapId = "hanabi";
        [Tooltip("quickplay | competitive | skirmish | practice | aitest | training | spectate")]
        public string mode = "quickplay";
        [Tooltip("the hero the local player picks (empty = spectate the bots)")]
        public string playerHero = "raijin";
        [Range(0.3f, 0.97f)] public float botSkill = 0.7f;
        public bool thirdPerson;
        [Tooltip("a bot drives the player's hero (demos, captures, AI testing)")]
        public bool autopilot;

        public Match Match { get; private set; }
        public World World => Match?.world;
        public Actor Player => Match?.player;
        // IViewHost: what a hero view reads from whoever drives it (a match here; the Hero Viewer's turntable elsewhere)
        public double SimTime => World.time;
        public bool ThirdPerson => thirdPerson;
        /// <summary>0..1 between the previous and the current simulation step (for interpolation)</summary>
        public float Alpha { get; private set; }

        double acc;
        readonly Dictionary<int, (V3 prev, V3 cur)> poses = new Dictionary<int, (V3, V3)>();
        LevelView level;
        readonly Dictionary<int, IActorView> views = new Dictionary<int, IActorView>();
        readonly Dictionary<int, string> viewDef = new Dictionary<int, string>();     // the def each view was built for
        ProjectileViews projViews;
        PlayerControls controls;
        MatchCamera cam;
        FirstPerson.FirstPersonView fp;

        void Start()
        {
            // the main menu's choice (the scene's own fields are the editor / CLI default)
            if (MatchSettings.Pending)
            {
                mapId = MatchSettings.Map; playerHero = MatchSettings.Hero; mode = MatchSettings.Mode;
                botSkill = MatchSettings.Skill; thirdPerson = MatchSettings.Third; autopilot = MatchSettings.Autopilot;
                MatchSettings.Pending = false;
            }
            PauseMenu.Reset();
            ZuData.Get();
            Rng.Seed((uint)System.Environment.TickCount);
            // online (ZU.Net, TS Game.ts online start): the net layer builds the match - the host from the lobby's slots, a client
            // an empty world the host's snapshots fill (its Match.player arrives with the first snapshot)
            var net = NetMatch.Current;
            if (net != null) Match = net.Build(-1, null, null, e => EventSink.Handle(this, e));
            // the Starfall campaign: the level's own floating-platform map, the squad, and the encounter director
            else if (mode == "campaign")
                Match = Director.CreateCampaign(mapId, new System.Collections.Generic.List<(string, string)> { (string.IsNullOrEmpty(playerHero) ? "raijin" : playerHero, "local") }, botSkill);
            else Match = Setup.CreateMatch(mapId, mode, string.IsNullOrEmpty(playerHero) ? null : playerHero, botSkill);
            level = LevelView.Build(World.map, transform, World.level);
            projViews = new ProjectileViews(transform);
            controls = new PlayerControls();
            cam = MatchCamera.Ensure(this);
            fp = FirstPerson.FirstPersonView.Ensure(this);
            Audio.MatchAudio.Attach(this);
            Fx.MatchFx.Attach(this);
            Fx.AbilityFx.Register(this);            // (after MatchFx: the seal storm draws its bursts through MatchFx.Current)
            UI.Toolkit.MatchUi.Attach(this);         // the HUD, Armory, pause / results / Options (UI Toolkit)
            if (Player != null && autopilot) { Player.controller = new Bot(World, Player, Match.nav, botSkill); }
            else if (Player != null) controls.Begin(Player);
            Snapshot(); Snapshot();
            StartCoroutine(MarkLive());
        }

        /// <summary>start-up timing: the first frame the match is drawn (QA times its shots from this, not a guessed delay)</summary>
        System.Collections.IEnumerator MarkLive()
        {
            yield return null; yield return new WaitForEndOfFrame();
            StartupClock.Mark($"match live: {mode} on {mapId} as {(Player != null ? Player.def.id : "spectator")}");
        }

        void Update()
        {
            if (World == null) return;
            PauseMenu.Update(this);
            ModeHud.Update(this);
            bool menu = PauseMenu.Paused || ModeHud.Shopping(this);
            if (!autopilot && !menu) controls.Read(Player, World);
            var net = NetMatch.Current;
            // online, a paused player's world keeps going: their hero just stands still and holds fire (TS Game.ts:
            // `if (!locked || paused) { fire = alt = false; mx = mz = 0 }`)
            if (net != null && menu && Player != null) { var i = Player.input; i.fire = i.alt = false; i.mx = i.mz = 0; }
            if (net != null && !net.StepsWorld)
            {
                // a client never steps the world: the host's snapshots place everything; we send our input and draw
                if (Match.player == null && net.Player != null) { Match.player = net.Player; if (!autopilot) controls.Begin(net.Player); }
                if (Player != null && !autopilot) controls.Apply(Player, World);
                net.Frame(Time.deltaTime, Player?.input);
                Snapshot(); Snapshot(); Alpha = 1;
                SyncViews();
                return;
            }
            acc += Time.deltaTime;
            int steps = 0;
            while (acc >= DT && steps < 16)
            {
                Snapshot();
                if (!autopilot) controls.Apply(Player, World);
                net?.BeforeStep();
                World.Step(DT);
                net?.AfterStep();
                net?.Capture(World.events);         // (before Dispatch clears them: the host streams the step's events)
                acc -= DT; steps++;
                Dispatch();
            }
            if (steps >= 16) acc = 0;
            net?.Frame(Time.deltaTime, null);          // the host: snapshots out
            Alpha = (float)(acc / DT);
            SyncViews();
        }

        void OnDestroy() => NetMatch.Current?.End();

        /// <summary>Training Grounds: take another hero mid-match (TS Game.swapHero) - the new hero stands where the old one
        /// stood, facing the same way; the old one leaves the world and its view goes. Training only; false otherwise.</summary>
        public bool SwapHero(string id)
        {
            var m = Match;
            if (m?.player == null || World.mode != "training") return false;
            var old = m.player; var w = World;
            var a = w.AddHero(id, "zenith");
            a.isPlayer = true; a.pos = old.pos; a.yaw = old.yaw;
            w.actors.Remove(old);
            if (views.TryGetValue(old.id, out var v)) { if (v is Component c && c != null) Destroy(c.gameObject); views.Remove(old.id); }
            viewDef.Remove(old.id); poses.Remove(old.id);
            m.player = a;
            if (!autopilot) controls.Begin(a);
            return true;
        }

        void Snapshot()
        {
            foreach (var a in World.actors)
                poses[a.id] = (poses.TryGetValue(a.id, out var p) ? p.cur : a.pos, a.pos);
        }

        /// <summary>where an actor is drawn this frame (between its last two simulated positions)</summary>
        public Vector3 DrawPos(Actor a)
        {
            if (!poses.TryGetValue(a.id, out var p)) return Conv.U(a.pos);
            return Vector3.LerpUnclamped(Conv.U(p.prev), Conv.U(p.cur), Alpha);
        }

        void Dispatch()
        {
            // the sim's events (sfx / fx / damage / kills / messages) - effects and audio take them from here
            foreach (var e in World.events) EventSink.Handle(this, e);
            World.events.Clear();
        }

        void SyncViews()
        {
            foreach (var a in World.actors)
            {
                // the World swaps hero defs (Tenkai-Oh's pilot ejecting / calling the mech back): rebuild that actor's view
                if (views.TryGetValue(a.id, out var old) && viewDef.TryGetValue(a.id, out var was) && was != a.def.id)
                {
                    if (old is Component oc && oc != null) Destroy(oc.gameObject);
                    views.Remove(a.id);
                }
                // model-less summons (Hex's puppets) are drawn as one swarm (AbilityFx PuppetSwarm), not one view each
                if (a.IsSummon && string.IsNullOrEmpty(a.def.model)) continue;
                if (!views.TryGetValue(a.id, out var v)) { views[a.id] = v = ActorViews.Create(a, transform); viewDef[a.id] = a.def.id; }
                v.Sync(this, a);
            }
            projViews.Sync(World, Alpha);
            cam.Sync(this);
            if (fp != null) fp.Sync(this);          // the local player's arms (first person only; needs the camera placed)
        }
    }
}
