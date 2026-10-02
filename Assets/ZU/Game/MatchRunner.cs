// Runs a match: builds the World from the ported simulation, steps it at a fixed 120 Hz (the desktop edition's rate),
// feeds the local player's controls in, and keeps the views (level, heroes, projectiles, camera, HUD) in step.
// Views draw between the last two simulation steps (render interpolation), so motion is smooth at any frame rate.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public class MatchRunner : MonoBehaviour
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
        /// <summary>0..1 between the previous and the current simulation step (for interpolation)</summary>
        public float Alpha { get; private set; }

        double acc;
        readonly Dictionary<int, (V3 prev, V3 cur)> poses = new Dictionary<int, (V3, V3)>();
        LevelView level;
        readonly Dictionary<int, IActorView> views = new Dictionary<int, IActorView>();
        ProjectileViews projViews;
        PlayerControls controls;
        MatchCamera cam;

        void Start()
        {
            ZuData.Get();
            Rng.Seed((uint)System.Environment.TickCount);
            Match = Setup.CreateMatch(mapId, mode, string.IsNullOrEmpty(playerHero) ? null : playerHero, botSkill);
            level = LevelView.Build(World.map, transform);
            projViews = new ProjectileViews(transform);
            controls = new PlayerControls();
            cam = MatchCamera.Ensure(this);
            if (Player != null && autopilot) { Player.controller = new Bot(World, Player, Match.nav, botSkill); }
            else if (Player != null) controls.Begin(Player);
            Snapshot(); Snapshot();
        }

        void Update()
        {
            if (World == null) return;
            if (!autopilot) controls.Read(Player, World);
            acc += Time.deltaTime;
            int steps = 0;
            while (acc >= DT && steps < 16)
            {
                Snapshot();
                if (!autopilot) controls.Apply(Player, World);
                World.Step(DT);
                acc -= DT; steps++;
                Dispatch();
            }
            if (steps >= 16) acc = 0;
            Alpha = (float)(acc / DT);
            SyncViews();
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
                if (!views.TryGetValue(a.id, out var v)) views[a.id] = v = ActorViews.Create(a, transform);
                v.Sync(this, a);
            }
            projViews.Sync(World, Alpha);
            cam.Sync(this);
        }

        void OnGUI() => Hud.Draw(this);
    }
}
