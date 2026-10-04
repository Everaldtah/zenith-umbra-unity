// The hero-specific showpieces the TS Fx.ts / Game.ts / CharacterView.ts own, gathered behind one call:
//   AbilityFx.Register(runner)        (after Fx.MatchFx.Attach: the seal storm draws its bursts with MatchFx.Current)
// It takes the simulation's fx events itself (EventSink) and keeps its views in step from LateUpdate:
//  - SpiritDragons  twinkoi / dragoncoil / dragoncut        Seiran's Twin Koi Torrent, Hayate's Dragon Gate Blade
//  - SealStorm      sealstorm / sealshield / sealstrike / sealburst / sealmend      Kaien's Divine Seal Storm
//  - ChainCage      the 'dohyo' zones and the 'chained' status                      Gantetsu's Grand Dohyo
//  - PuppetSwarm    the 'puppet' summons                                            Hex's puppet army
//  - SunSwarm       the 'sunswarm' zones (Unity-only rework)                        Yuzu's Hundred Suns
//  - CharacterExtras  blinking lids / masked eye glow, ragdoll deaths              every hero view
//  - KillCam        the last seconds replayed from the killer's side when you are eliminated (Unity only)
//  - UltShowcase    attached when UltShowcase.Open started this match
// Runs early in LateUpdate (order -50): after the Animator, before HeroView's held weapons and fingers and before the
// hair / cloth solver (ZuDynamics, 500), so a ragdoll's pose is what they follow.
using UnityEngine;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.Game.Fx
{
    [DefaultExecutionOrder(-50)]
    public sealed class AbilityFx : MonoBehaviour
    {
        MatchRunner r;
        SpiritDragons dragons;
        SealStorm seals;
        ChainCage chains;
        PuppetSwarm swarm;
        SunSwarm suns;
        bool sunsFailed;
        CharacterExtras extras;
        bool ready;

        /// <summary>a lookup MatchRunner can supply for an actor's view object (default: by the name ActorViews gives it)</summary>
        public System.Func<Actor, GameObject> ViewOf;

        public static AbilityFx Register(MatchRunner runner)
        {
            var fx = runner.GetComponent<AbilityFx>();
            if (fx == null) fx = runner.gameObject.AddComponent<AbilityFx>();
            fx.r = runner;
            return fx;
        }

        void Start() { EventSink.OnEvent += OnEvent; }

        void Init()
        {
            if (ready || r == null || r.World == null || MatchFx.Current == null) return;
            ready = true;
            dragons = new SpiritDragons(transform);
            chains = new ChainCage(transform);
            seals = new SealStorm(MatchFx.Current);
            suns = new SunSwarm(MatchFx.Current);
            extras = new CharacterExtras(r, ViewOf);
            KillCam.Attach(r);
            PlayOfTheGame.Attach(r);          // (the Play of the Game and the highlights cut their clips from the kill cam's record)
            // the swarm is built at match start when Hex is in it (the TS: so the preloader compiles it), else on the first puppet
            foreach (var a in r.World.actors) if (a.baseDef.id == "hex") { swarm = new PuppetSwarm(); break; }
        }

        float Now => (float)r.World.time;

        void OnEvent(MatchRunner runner, SimEvent ev)
        {
            if (runner != r || !(ev is FxEvent e)) return;
            Init();
            if (!ready) return;
            float now = Now;
            switch (e.kind)
            {
                case "twinkoi": if (e.to.HasValue) dragons.Twin(e.pos, e.to.Value, now); return;
                case "dragoncoil": if (e.actor != null) dragons.Coil(e.actor, now, e.actor == r.Player && !r.thirdPerson); return;
                case "dragoncut": if (e.to.HasValue) dragons.Streak(e.pos, e.to.Value, now); return;
            }
            seals.OnEvent(e, now);       // Kaien's Divine Seal Storm (sealstorm / sealshield / sealstrike / sealburst / sealmend)
        }

        void LateUpdate() => Sync(Time.deltaTime);

        /// <summary>the per-frame step (LateUpdate calls it; public for a runner that wants to drive it)</summary>
        public void Sync(float dt)
        {
            Init();
            if (!ready) return;
            // (here, never from OnEvent: the showcase adds its dummies, and the world's events are being enumerated there)
            if (UltShowcase.Pending) UltShowcase.Attach(r);
            var w = r.World; float now = Now;
            var kit = MatchFx.Current;
            dragons.Update(now, (q, c, big) => kit?.Emit(q, big ? 3 : 2, c, FxKit.O(speed: big ? 2.2f : 1.2f, life: big ? 0.7f : 0.5f, size: big ? 0.55f : 0.4f, spread: big ? 0.8f : 0.5f)));
            seals.Update(w, now, dt);
            // (fenced: the Unity-only rework must never take the other showpieces down with it)
            try { suns.Update(w, r.Player, now, dt); }
            catch (System.Exception e) { if (!sunsFailed) { sunsFailed = true; Debug.LogException(e); } }
            chains.Update(w, now);
            if (swarm == null) foreach (var a in w.actors) if (a.IsSummon && a.def.id == "puppet") { swarm = new PuppetSwarm(); break; }
            if (swarm != null)
            {
                var me = r.Player; string team = me != null ? me.team : "zenith";
                swarm.Update(w, now, team, a => !(a.Has("stealth", now) && me != null && a.team != me.team && !a.Has("revealed", now)));
            }
            extras.Update(w, KillCam.TimeOr(r, now), dt);      // (the kill cam's moment while it replays)
        }

        void OnDestroy()
        {
            EventSink.OnEvent -= OnEvent;
            dragons?.Dispose(); seals?.Dispose(); suns?.Dispose(); chains?.Dispose(); swarm?.Dispose(); extras?.Dispose();
        }
    }
}
