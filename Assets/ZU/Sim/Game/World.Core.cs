// Deterministic fixed-step simulation: movement, combat, projectiles, zones, capture point.
// Port of zenith-umbra src/game/World.ts, split into partial files (Core, Combat, Projectiles, Step, Move, Objectives).
// Rendering, audio and HUD only read state and drain `events`.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public partial class World
    {
        /// <summary>Tenkai-Oh's ult: 3.3m x 2.2 = 7.3m, four times an average hero's height</summary>
        public const double TITAN_SCALE = 2.2;
        public const double G = 24;
        public static readonly HashSet<string> SUMMON_ABILITIES = new HashSet<string> { "puppet", "susanoo", "effigy" };
        public const double GROOVE_MAX = 20;
        public static readonly double[] GROOVE_TAPS = { 1.5, 8.5 };
        public const double KOI_GROOVE_MAX = 5;
        public const double DEFLECT_SECS = 2, DEFLECT_MIN = 0.27;
        public const double CLIMB_SECS = 2.2;
        public const double RING_H = 8;
        public const int ROUNDS_TO_WIN = 2;
        public const double HOLD_RATE = 1.3;
        public const double FLOAT_SPEED = 1.2;
        public const double PUSH_TIME = 300;
        public static readonly (double hp, double respawn) PACK_SMALL = (75, 10), PACK_BIG = (250, 15);
        /// <summary>ultimate charge packs (Training Grounds, desktop edition): a player who touches one has their ultimate ready at once</summary>
        public static readonly (double respawn, double r) ULT_PACK = (8, 1.1);
        public const double SEEK_DMG = 0.7, SEEK_TURN = 9, SEEK_LIFE = 0.9;
        public const double TAIKO_DR = 0.3;
        const double STEP = LevelConst.STEP;

        static int PID = 1;

        public double time;
        public ILevel level;
        public MapDef map;
        public string mode;    // training | skirmish | stadium | spectate | aitest | campaign | gallery | quickplay | competitive | practice
        public List<Actor> actors = new List<Actor>();
        public List<Proj> projs = new List<Proj>();
        public List<Zone> zones = new List<Zone>();
        public List<SimEvent> events = new List<SimEvent>();
        /// <summary>listeners that see every event the moment it is emitted, at its own sim time (the Hero Range's meter)</summary>
        public List<Action<SimEvent>> taps = new List<Action<SimEvent>>();
        /// <summary>run at the end of every step (Training Grounds: the spar arena's rounds and walls)</summary>
        public List<Action<double>> tickers = new List<Action<double>>();
        /// <summary>may veto a hit before anything is applied (the sealed spar box: nothing crosses its walls)</summary>
        public Func<Actor, Actor, bool> gate;
        public List<Timer> timers = new List<Timer>();
        public Dictionary<int, PrevInput> prevIn = new Dictionary<int, PrevInput>();
        public Dictionary<int, Dictionary<int, double>> attackers = new Dictionary<int, Dictionary<int, double>>();
        // capture point
        public PointState point = new PointState();
        public string winner;
        public double timeLimit = 360;
        public string rules = "legacy";   // legacy | control | push
        /// <summary>desktop edition features (Mirei's swoop, the new modes)</summary>
        public bool full = true;
        public ControlState control = new ControlState();
        public PushState push = new PushState();
        List<V3> pathPts = new List<V3>(); List<double> pathCum = new List<double>();
        public List<HealthPack> packs = new List<HealthPack>();
        public List<UltPack> ultPacks = new List<UltPack>();
        /// <summary>campaign hooks: enemy/boss definitions and the encounter director</summary>
        public Dictionary<string, HeroDef> extraDefs;
        /// <summary>the match's path finder (summoned armies route around walls with it)</summary>
        public INav nav;
        /// <summary>per-tick scratch values</summary>
        public Dictionary<string, double> sv = new Dictionary<string, double> { ["puppetPaths"] = 0 };
        public IDirector director;
        /// <summary>online host: lag compensation - moves everyone else back to where a remote shooter saw them while that
        /// shooter's weapons and abilities run; returns the undo (ZU.Net FastHost)</summary>
        public Func<Actor, Action> rewind;
        /// <summary>Stadium mode: rounds, the Armory, cash (null in every other mode)</summary>
        public Stadium stadium;
        public WorldStats stats = new WorldStats();

        static GameData D => GameData.Current;

        /// <param name="level">the collision to use (BoxLevel for the classic arenas and parity tests; PhysXLevel in Unity)</param>
        public World(MapDef map, string mode, bool full = true, string rules = null, ILevel level = null)
        {
            this.full = full;
            this.map = map;
            this.mode = mode;
            this.level = level ?? new BoxLevel(map);
            extraDefs = new Dictionary<string, HeroDef> { ["puppet"] = Puppets.PUPPET_DEF, ["susanoo"] = Susanoo.SUSANOO_DEF };
            this.rules = rules ?? (this.full && (mode == "quickplay" || mode == "competitive" || mode == "practice" || mode == "spectate") ? (map.objective ?? "control") : "legacy");
            foreach (var p in map.packs ?? new List<Data.Pack>())
            {
                var y = p.y ?? Math.Max(0, this.level.GroundAt(p.x, p.z, 0.3));
                packs.Add(new HealthPack { x = p.x, y = y, z = p.z, big = p.big, readyAt = 0 });
            }
            if (this.full) foreach (var p in map.ultPacks ?? new List<Data.Pack>()) ultPacks.Add(new UltPack { x = p.x, y = p.y ?? Math.Max(0, this.level.GroundAt(p.x, p.z, 0.3)), z = p.z, readyAt = 0 });
            if (this.rules == "control") { point.unlockAt = 12; timeLimit = 1500; }
            if (this.rules == "push")
            {
                var path = map.path ?? new List<double[]> { new[] { -map.size[0] + 6, 0 }, new[] { map.size[0] - 6, 0 } };
                double cum = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    double x = path[i][0], z = path[i][1];
                    var p = new V3(x, Math.Max(0, this.level.GroundAt(x, z, 20)), z);
                    if (i > 0) cum += M.Hypot(x - pathPts[i - 1].x, z - pathPts[i - 1].z);
                    pathPts.Add(p); pathCum.Add(cum);
                }
                push.half = cum / 2;
                push.pos = PathAt(0);
                timeLimit = PUSH_TIME;
            }
        }

        public World(string mapId, string mode, bool full = true, string rules = null) : this(D.Map[mapId], mode, full, rules) { }

        /// <summary>a point on the push path, `d` metres from its centre (+ toward the Umbra end)</summary>
        public V3 PathAt(double d)
        {
            double s = Math.Max(0, Math.Min(push.half * 2, push.half + d));
            var C = pathCum; var P = pathPts;
            int i = 1; while (i < C.Count - 1 && C[i] < s) i++;
            double k = (s - C[i - 1]) / Math.Max(1e-6, C[i] - C[i - 1]);
            double x = P[i - 1].x + (P[i].x - P[i - 1].x) * k, z = P[i - 1].z + (P[i].z - P[i - 1].z) * k;
            return new V3(x, Math.Max(0, level.GroundAt(x, z, 20)), z);
        }

        // ------------------------------------------------------------------ setup
        public HeroDef DefOf(string heroId) => D.Def(heroId) ?? (extraDefs.TryGetValue(heroId, out var x) ? x : null);

        public Actor AddHero(string heroId, string team = null)
        {
            var def = DefOf(heroId);
            var a = new Actor(def, team ?? def.team);
            a.isRobot = D.Robot.ContainsKey(heroId) || def.summoned;
            a.spawn = map.spawns[a.team];
            actors.Add(a);
            Respawn(a, true);
            return a;
        }

        /// <summary>back to spawn for a new round. A summon (Raijin's Susanoo, Enra's effigy, a puppet) is not a player: it is
        /// retired here, and only its own ultimate brings it back.
        /// UNITY-DIVERGENCE: the TS respawns the pooled summon too - alive, with an empty sv, where every `t &lt; sv.x` is just
        /// false. In C# the same read throws KeyNotFoundException in the summon's tick on every step, so World.Step never
        /// finished again: from the second round on the match stood still (the installed 0.2.2 logged 4646 of them in one
        /// session).</summary>
        public void RoundRespawn(Actor a)
        {
            if (a.IsSummon) { a.alive = false; a.respawnAt = 0; a.forced = null; a.deathAt = -99; return; }
            Respawn(a, true);
        }

        public void Respawn(Actor a, bool first = false)
        {
            if (!first && !a.IsSummon && a.sv.ContainsKey("puppetsCast")) Puppets.DropPuppets(this, a);
            if (!first && !a.IsSummon && a.sv.ContainsKey("susanooCast")) Susanoo.DismissSusanoo(this, a);
            double sx = a.spawn[0], sz = a.spawn[1];
            int i = actors.Where(o => o.team == a.team).ToList().IndexOf(a);
            double ang = i * 1.3;
            a.pos = new V3(sx + Math.Cos(ang) * 2.5 * (first ? 1 : Rng.Random() + 0.5), 0, sz + Math.Sin(ang) * 3);
            // the floor at spawn level (spawn rooms have roofs: sampling from high up would put the team on top of them)
            double g0 = level.GroundAt(a.pos.x, a.pos.z, 1);
            a.pos.y = Math.Max(0, g0 > double.NegativeInfinity ? g0 : level.GroundAt(a.pos.x, a.pos.z, 30));
            if (a.def.frame == "drone") a.pos.y += 3;
            a.vel = V3.Zero;
            a.yaw = a.team == "zenith" ? Math.PI / 2 : -Math.PI / 2;
            if (a.isRobot) a.yaw = -Math.PI / 2;
            a.pitch = 0;
            a.def = a.baseDef;
            a.hp = a.def.hp; a.maxArmor = a.def.armor + a.mods.armor; a.armor = a.maxArmor; a.scale = 1;
            a.shields = new List<Shield>(); a.wounds = new List<Wound>(); a.st = new Dictionary<string, double>(); a.sv = new Dictionary<string, double>(); a.src = new Dictionary<string, Actor>(); a.forced = null;
            a.alive = true; a.respawnAt = 0; a.flight = 100; a.flying = false;
            a.ammo = a.MaxAmmo; a.reloadUntil = 0;
            if (a.def.dualGuns && !a.def.secondary.IsAbility) a.sv["ammo2"] = Math.Max(1, JsMath.Round((a.def.secondary.ammo ?? 0) * (1 + a.mods.ammo)));
            if (a.barrier.max > 0) a.barrier = new BarrierState { hp = a.barrier.max, max = a.barrier.max };
            a.Set("spawnprot", time, first ? 0 : 2);
            Emit(new FxEvent("spawn", a.pos, new FxOpts { color = a.def.glow, actor = a }));
        }

        // ------------------------------------------------------------------ helpers
        public void Emit(SimEvent e)
        {
            events.Add(e);
            foreach (var f0 in taps) f0(e);
            if (e is SfxEvent s) stats.sfx[s.id] = (stats.sfx.TryGetValue(s.id, out var n) ? n : 0) + 1;
            else if (e is FxEvent f) stats.fx[f.kind] = (stats.fx.TryGetValue(f.kind, out var n2) ? n2 : 0) + 1;
            else if (e is CounterEvent) stats.counters++;
        }
        public void Sfx(string id, V3? pos = null, Actor actor = null) => Emit(new SfxEvent { id = id, pos = pos, actor = actor });
        public void Fx(string kind, V3 pos, FxOpts extra = null) => Emit(new FxEvent(kind, pos, extra));
        public void After(double delay, Action fn) => timers.Add(new Timer { at = time + delay, fn = fn });
        public void Msg(string text, string color = null) => Emit(new MsgEvent { text = text, color = color });
        public void Counter(Actor actor, Actor target, string text) => Emit(new CounterEvent { actor = actor, target = target, text = text });

        public List<Actor> Enemies(Actor a) => actors.Where(o => o.alive && o.team != a.team).ToList();
        public List<Actor> Allies(Actor a, bool self = true) => actors.Where(o => o.alive && o.team == a.team && (self || o != a)).ToList();
        public List<Actor> Within(V3 p, double r, Func<Actor, bool> filter) => actors.Where(o => o.alive && filter(o) && Dist3(o.Center, p) < r + o.Radius).ToList();
        public bool Visible(Actor a, Actor b) => level.LineOfSight(a.Eye, b.Center);
        /// <summary>can `viewer` see `b`? (stealth)</summary>
        public bool Perceivable(Actor viewer, Actor b)
        {
            if (b.team == viewer.team) return true;
            if (!b.Has("stealth", time)) return true;
            if (b.Has("revealed", time)) return true;
            return Dist3(viewer.pos, b.pos) < 2.5;
        }

        public V3 Muzzle(Actor a, string slot = null)
        {
            if (a.def.dualGuns)
            {
                // twin chainguns held at the hips: LMB = the left gun, RMB = the right; barrels along the aim
                double side = slot == "secondary" ? 1 : -1, rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw), k = a.scale; var d = a.AimDir();
                return new V3(a.pos.x + rx * side * 0.52 * k + d.x * 1.15 * k, a.pos.y + a.Height * 0.48 + d.y * 1.15 * k, a.pos.z + rz * side * 0.52 * k + d.z * 1.15 * k);
            }
            double r = a.def.frame == "mech" ? 1.1 : 0.32, dd = a.def.frame == "mech" ? 0.7 : 0.25;
            V3 e = a.Eye, f = a.Forward(); double rx2 = -Math.Cos(a.yaw), rz2 = Math.Sin(a.yaw);
            return new V3(e.x + rx2 * r * a.scale + f.x * 0.4, e.y - dd * a.scale, e.z + rz2 * r * a.scale + f.z * 0.4);
        }

        /// <summary>where the crosshair ray lands (level or enemy), from the eye</summary>
        public V3 AimPoint(Actor a, double max = 150)
        {
            V3 o = a.Eye, d = a.AimDir();
            var lh = level.Ray(o, d, max);
            double t = lh.HasValue ? lh.Value.t : max;
            var ah = RayActors(o, d, t, x => x.team != a.team && x != a);
            if (ah != null) t = ah.t;
            return new V3(o.x + d.x * t, o.y + d.y * t, o.z + d.z * t);
        }

        public class ActorHit { public Actor actor; public double t; public bool head; }

        /// <summary>ray vs actor capsules + head spheres</summary>
        public ActorHit RayActors(V3 o, V3 d, double max, Func<Actor, bool> filter)
        {
            ActorHit best = null;
            foreach (var x in actors)
            {
                if (!x.alive || !filter(x) || x.Has("phased", time)) continue;
                double hr = HeadR(x); var hc = HeadC(x);
                var th = RaySphere(o, d, hc, hr);
                if (th.HasValue && th.Value <= max && (best == null || th.Value < best.t)) { best = new ActorHit { actor = x, t = th.Value, head = true }; continue; }
                double r = x.Radius * 0.9;
                V3 a0 = new V3(x.pos.x, x.pos.y + r, x.pos.z), a1 = new V3(x.pos.x, x.pos.y + x.Height - hr * 1.6, x.pos.z);
                var end = new V3(o.x + d.x * max, o.y + d.y * max, o.z + d.z * max);
                var s = SegSeg(o, end, a0, a1);
                if (s.d2 <= r * r)
                {
                    double t = Math.Max(0, s.s * max - Math.Sqrt(Math.Max(0, r * r - s.d2)));
                    if (best == null || t < best.t) best = new ActorHit { actor = x, t = t, head = false };
                }
            }
            return best;
        }

        /// <summary>nearest target near the crosshair within an aim cone (lock-on abilities / heal beams)</summary>
        public Actor ConeTarget(Actor a, double range, double deg, Func<Actor, bool> filter)
        {
            V3 e = a.Eye, d = a.AimDir(); double cos = Math.Cos(deg * Math.PI / 180);
            Actor best = null; double bs = -1;
            foreach (var x in actors)
            {
                if (!x.alive || x == a || !filter(x)) continue;
                var c = x.Center; var v = new V3(c.x - e.x, c.y - e.y, c.z - e.z); double l = M.Hypot(v.x, v.y, v.z);
                if (l > range + x.Radius) continue;
                double dot = (v.x * d.x + v.y * d.y + v.z * d.z) / (l == 0 ? 1 : l);
                if (dot < cos && l > x.Radius * 1.5) continue;
                if (!level.LineOfSight(e, c)) continue;
                double score = dot - l / range * 0.05;
                if (score > bs) { bs = score; best = x; }
            }
            return best;
        }

        public V3 GroundPoint(Actor a, double range)
        {
            var p = AimPoint(a, range);
            double g = level.GroundAt(p.x, p.z, p.y + 0.5);
            return new V3(p.x, double.IsNegativeInfinity(g) ? a.pos.y : g, p.z);
        }

        public Actor ById(int id) => actors.FirstOrDefault(x => x.id == id);

        // ------------------------------------------------------------------ math (TS module-level helpers)
        public static double Dist3(V3 a, V3 b) => M.Hypot(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 Norm(V3 v) { double l = M.Hypot(v.x, v.y, v.z); if (l == 0) l = 1; return new V3(v.x / l, v.y / l, v.z / l); }
        public static double HeadR(Actor x) => x.Height * (x.def.frame == "mech" ? 0.1 : 0.085) + 0.04;
        public static V3 HeadC(Actor x) => new V3(x.pos.x, x.pos.y + x.Height - HeadR(x) * 1.1, x.pos.z);

        public static double? RaySphere(V3 o, V3 d, V3 c, double r)
        {
            double ox = o.x - c.x, oy = o.y - c.y, oz = o.z - c.z;
            double b = ox * d.x + oy * d.y + oz * d.z, cc = ox * ox + oy * oy + oz * oz - r * r;
            double disc = b * b - cc;
            if (disc < 0) return null;
            double t = -b - Math.Sqrt(disc);
            return t >= 0 ? t : (cc < 0 ? 0 : (double?)null);
        }

        public struct SegResult { public double d2, s, t; }
        /// <summary>closest points between segments p1q1 and p2q2 (Ericson). s is the param along the first segment.</summary>
        public static SegResult SegSeg(V3 p1, V3 q1, V3 p2, V3 q2)
        {
            V3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = V3.Dot(d1, d1), e = V3.Dot(d2, d2), f = V3.Dot(d2, r);
            double s = 0, t = 0;
            if (a <= 1e-9 && e <= 1e-9) { s = t = 0; }
            else if (a <= 1e-9) { t = M.Clamp01(f / e); }
            else
            {
                double c = V3.Dot(d1, r);
                if (e <= 1e-9) { s = M.Clamp01(-c / a); }
                else
                {
                    double b = V3.Dot(d1, d2), den = a * e - b * b;
                    s = den != 0 ? M.Clamp01((b * f - c * e) / den) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = M.Clamp01(-c / a); } else if (t > 1) { t = 1; s = M.Clamp01((b - c) / a); }
                }
            }
            V3 c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            double dx = c1.x - c2.x, dy = c1.y - c2.y, dz = c1.z - c2.z;
            return new SegResult { d2 = dx * dx + dy * dy + dz * dz, s = s, t = t };
        }
    }
}
