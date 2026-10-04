// A hero / robot / summon in the simulation (port of zenith-umbra src/game/Actor.ts).
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public class SimInput
    {
        public double mx, mz;                     // local move: right / forward, -1..1
        public bool jump, jumpHeld, descend;
        public bool fire, alt, a1, a2, ult, reload;
        public bool melee;                        // quick melee (C)
        public bool swoop;                        // Mirei: Starwing Swoop to the ally under the crosshair (F)
        public bool? grind;                       // Hibiki: Mag-Grind held (bots ride on jumpHeld)
        public double yaw, pitch;
        public SimInput Clone() => (SimInput)MemberwiseClone();
    }

    public class Forced
    {
        public double vx, vy, vz, until; public string kind;
        public bool ignoreGravity, scaled; public Action onEnd;
    }
    public class Shield { public double amt, until; public string kind; public Actor src; }
    public class Wound { public Actor src; public double dps, until; }
    public class BarrierState { public double hp, max, regenAt, brokenUntil; public bool up; public BarrierState Clone() => (BarrierState)MemberwiseClone(); }

    /// <summary>Stadium stat mods (src/game/stadium.ts Mods); all zero outside Stadium.</summary>
    public class Mods
    {
        public double weapon, ability, atkspd, cdr, speed, armor, lifesteal, reload, ammo, ultgain, healing;
        public Dictionary<string, double> cdrBy = new Dictionary<string, double>();
    }

    /// <summary>Animation cues the renderer reads (Actor.anim).</summary>
    public class AnimCues
    {
        public AnimCues Clone() => (AnimCues)MemberwiseClone();
        public double attackAt = -9, castAt = -9, hitAt = -9, jumpAt = -9, landAt = -9, stepPhase, fireL = -9, fireR = -9;
        public string attackKind = "primary", castId = "";
        public double attackSide = 1;
        // Hayate's Mirror Water: the last turned shot - when, from where (world direction), how many this window
        public double deflectAt = -9; public V3 deflectDir = new V3(0, 0, 1); public int deflectN;
    }

    public interface IController { void Think(double dt); }

    public class Actor
    {
        static int NEXT = 1;
        public readonly int id = NEXT++;
        public V3 pos, vel;
        public double yaw, pitch;
        public SimInput input = new SimInput();
        public double hp, armor, maxArmor;
        public List<Shield> shields = new List<Shield>();
        /// <summary>open wounds: damage over time that stacks per wound</summary>
        public List<Wound> wounds = new List<Wound>();
        public bool alive = true; public double respawnAt, deathAt;
        /// <summary>a frozen copy for the kill cam (Game/AbilityFx/KillCam.cs): what a view reads of this hero right now, detached
        /// from the live one (same id; never stepped, never in a World)</summary>
        public Actor Snapshot()
        {
            var c = (Actor)MemberwiseClone();
            c.input = input.Clone(); c.anim = anim.Clone(); c.barrier = barrier.Clone();
            c.st = new Dictionary<string, double>(st); c.sv = new Dictionary<string, double>(sv);
            c.controller = null;
            return c;
        }
        public bool grounded; public double lastGroundedAt; public int airJumps; public bool flying; public double flight = 100;
        public Dictionary<string, double> st = new Dictionary<string, double>();     // status -> until (sim time)
        public Dictionary<string, double> sv = new Dictionary<string, double>();     // status values
        public Dictionary<string, Actor> src = new Dictionary<string, Actor>();
        public Dictionary<string, double> cd = new Dictionary<string, double>();     // ability id -> ready at
        public double ammo, reloadUntil, nextShot, nextAlt, nextMelee, charge; public bool charging;
        public double ult;
        public Forced forced;
        public BarrierState barrier = new BarrierState();
        public Actor beamTarget; public bool beamOn, flameOn;
        public double lastDamagedAt = -99, lastHitAt = -99; public Actor lastHitBy;
        public double scale = 1;
        // Stadium
        public double cash; public List<string> items = new List<string>(), powers = new List<string>();
        public Mods mods = new Mods();
        // stats (the Tab screen)
        public int kills, deaths, assists; public double dmgDone, healDone;
        public int shots, hits, crits, ults, streak, bestStreak; public double mitigated, objTime;
        public Dictionary<string, double> stats = new Dictionary<string, double>();
        public AnimCues anim = new AnimCues();
        public bool isPlayer, isRobot, noRespawn, isBoss;
        /// <summary>a summoned body's summoner (Hex's puppets)</summary>
        public Actor owner;
        public string netId = "";
        public IController controller;
        public double[] spawn = { 0, 0 };

        /// <summary>per-cast hit lists (TS: ad-hoc `(a as any)._xHit` sets): who a charge / dash / rush / Warpath / returning Fang already hit</summary>
        public HashSet<int> _chargeHit, _dashHit, _rushHit, _tideHit, _fangBack;

        public HeroDef def;
        public string team;
        /// <summary>the hero as picked: a pilot on foot returns to this on Call Mech / respawn</summary>
        public HeroDef baseDef;

        public Actor(HeroDef def, string team)
        {
            this.def = def; this.team = team; baseDef = def;
            hp = def.hp;
            armor = maxArmor = def.armor;
            ammo = def.primary != null && def.primary.ammo.HasValue && def.primary.ammo.Value > 0 ? def.primary.ammo.Value : 0;
            if (def.id == "tenkai") barrier = new BarrierState { hp = 1400, max = 1400 };
        }

        /// <summary>a hero that takes a given id instead of a fresh one: a recorded hero read back from a clip file, and its
        /// stand-in in a replay (Game/AbilityFx/PlayClip.Disk.cs, KillCam.Watch) - never one that plays in a match</summary>
        public Actor(HeroDef def, string team, int id) : this(def, team) { this.id = id; }

        public bool IsSummon => def.summoned;
        public double MaxHp => def.hp + maxArmor;
        public double Health => hp + armor;
        public double Height => def.height * scale;
        public double Radius => def.radius * scale;
        // level collision capsule: a grown giant still fits through doors and under arches
        public double ColRadius => def.radius * Math.Min(scale, 1.5);
        public double ColHeight => def.height * Math.Min(scale, 1.35);
        public V3 Eye => new V3(pos.x, pos.y + Height * (def.frame == "mech" ? 0.78 : 0.9), pos.z);
        public V3 Center => new V3(pos.x, pos.y + Height * 0.55, pos.z);
        public double ShieldAmt => shields.Sum(s => s.amt);

        public V3 AimDir() { double cp = Math.Cos(pitch); return new V3(Math.Sin(yaw) * cp, Math.Sin(pitch), Math.Cos(yaw) * cp); }
        public V3 Forward() => new V3(Math.Sin(yaw), 0, Math.Cos(yaw));

        public bool Has(string s, double t) => (st.TryGetValue(s, out var u) ? u : -1) > t;
        public void Set(string s, double t, double dur, double? v = null, Actor from = null)
        {
            st[s] = Math.Max(st.TryGetValue(s, out var u) ? u : 0, t + dur);
            if (v.HasValue) sv[s] = v.Value;
            if (from != null) src[s] = from;
        }
        public void Clear(string s) { st.Remove(s); sv.Remove(s); src.Remove(s); }
        public bool Ready(string id, double t) => (cd.TryGetValue(id, out var r) ? r : 0) <= t;
        /// <summary>sv lookup with the TS `?? fallback` semantics</summary>
        public double Sv(string k, double fallback = 0) => sv.TryGetValue(k, out var v) ? v : fallback;
        public double St(string k, double fallback = 0) => st.TryGetValue(k, out var v) ? v : fallback;

        /// <summary>magazine size with Stadium ammo items</summary>
        public double MaxAmmo { get { var P = def.primary; return P != null && P.ammo.HasValue && P.ammo.Value > 0 ? Math.Max(1, Math.Round(P.ammo.Value * (1 + mods.ammo), MidpointRounding.AwayFromZero)) : 0; } }
        public double Rate(double r) => r * (1 + mods.atkspd);
        public double ReloadTime(double r) => r / (1 + mods.reload);
        public double CdLeft(string id, double t) => Math.Max(0, (cd.TryGetValue(id, out var r) ? r : 0) - t);
    }
}
