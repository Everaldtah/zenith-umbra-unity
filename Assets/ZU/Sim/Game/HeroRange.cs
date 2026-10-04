// Training Grounds - the Hero Range (desktop edition). Overwatch's Practice Range has a Hero Bot by the spawn room that a
// console turns into any hero; it stands passive, but its passives still apply (armour, damage reduction, knockback).
// Here the console spawns any hero in one of two modes:
//  - DEFENSE: it holds its post facing you and never fires. With its abilities on it guards itself the way a player
//    would (a barrier up under fire, temporary health, a deflect or a parry, a dodge when low) - never anything that
//    damages or disables you. With them off it is Overwatch's passive Hero Bot. It resets to full health 3 s after
//    the last hit, so every burst starts from full and gives a clean time-to-kill.
//  - ATTACK: it fights back with the hero AI (weapon, abilities, ultimate), leashed to its lane, so you can measure
//    what you deal under fire and what that hero deals to you.
// The meter (Unity: RangeView) reads the stats kept here from the world's damage and kill events.
// Port of zenith-umbra src/game/herorange.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    /// <summary>the range hero's settings (TS RangeOpts): mode "attack" | "defense", move "hold" | "strafe"</summary>
    public class RangeOpts
    {
        public string hero;
        public string mode;
        /// <summary>the hero's abilities (and ultimate, in attack mode): off = weapon only / a passive dummy</summary>
        public bool abilities;
        /// <summary>defense: stand still or strafe side to side along the lane</summary>
        public string move;
        /// <summary>metres from the firing line to the hero's post</summary>
        public double dist;
        /// <summary>attack: the AI's aim and reaction (Bot skill)</summary>
        public double skill;
        public RangeOpts Clone() => (RangeOpts)MemberwiseClone();
    }

    /// <summary>DEFENSE with abilities on: what each hero uses to protect itself. "hit" = the moment it is being hit, "hurt" =
    /// under fire below 60% health; hold = keep the input down while hits keep coming (Tenkai-Oh's sun-shield)</summary>
    public class Guard { public string slot, id, on; public bool hold, dodge; }

    /// <summary>a running tally of damage one way (you -> the hero, or the hero -> you)</summary>
    public class Tally
    {
        public double total, last, max; public int hits, crits; public bool lastCrit;
        public Dictionary<string, double> by = new Dictionary<string, double> { ["weapon"] = 0, ["ability"] = 0, ["dot"] = 0 };
        /// <summary>the current burst: first / last damage time, damage, distinct damage ticks; frozen once it ends</summary>
        public double b0 = -1, b1 = -1, bDmg; public int bTicks;
        double lastT = -1; string lastKind = "";

        public void Add(double t, double amt, bool crit, string kind)
        {
            total += amt;
            by[HeroRange.ClassOf(kind)] += amt;
            if (b0 < 0 || t - b1 > HeroRange.BURST_GAP) { b0 = t; bDmg = 0; bTicks = 0; }
            if (t != b1) bTicks++;
            b1 = t; bDmg += amt;
            if (HeroRange.Continuous(kind)) return;
            // a shotgun's pellets (and both of a twin gun's rounds) land on one tick: one hit
            if (t == lastT && kind == lastKind) { if (crit && !lastCrit) crits++; last += amt; lastCrit = lastCrit || crit; }
            else { hits++; last = amt; lastCrit = crit; if (crit) crits++; }
            max = Math.Max(max, last);
            lastT = t; lastKind = kind ?? "";
        }

        /// <summary>sustained damage per second over the burst (the mean damage per tick over the mean gap between ticks)</summary>
        public double? Dps()
        {
            if (bTicks < 2 || b1 <= b0) return null;
            return bDmg / ((b1 - b0) * bTicks / (bTicks - 1));
        }
        public bool Active(double t) => b0 >= 0 && t - b1 <= HeroRange.BURST_GAP;
    }

    /// <summary>one dropped target, for the results table</summary>
    public class RangeResult
    {
        public string you, target, mode; public bool abilities; public double dist;
        public double ttk, dmg; public int hits, crits; public double? dps;
        /// <summary>a mech's frame: when it went down (the pilot fights on)</summary>
        public double? frameDown;
    }

    public class RangeStats
    {
        public Tally dealt = new Tally();
        public Tally taken = new Tally();
        public int kills, deaths;
        /// <summary>first damage of the target's current life (from full health): the time-to-kill clock</summary>
        public double lifeStart = -1, lifeDmg; public int lifeHits, lifeCrits; public double frameDown = -1;
        public double? lastTtk, bestTtk;
        /// <summary>where you stood for the last hit, metres from the target</summary>
        public double lastDist;
        /// <summary>your shots / hits when the stats were last reset (accuracy = the difference)</summary>
        public int shots0, hits0;
        public List<RangeResult> log = new List<RangeResult>();
    }

    /// <summary>
    /// Lives on a training match (Setup.cs, desktop edition). Owns at most one range hero: `Deploy` (re)spawns it with new
    /// options, `Clear` removes it. Watches the world's events through World.taps, so it sees every damage tick at its own
    /// sim time (the views drain World.events once a step).
    /// </summary>
    public class HeroRange
    {
        /// <summary>the lane on the Proving Grounds: firing line at x = x0 (the console beside it), the post `dist` metres down +x</summary>
        public static readonly (double x0, double z, double[] dists, (double x, double z) console, double strafe) LANE = (-14, -21, new double[] { 5, 10, 15, 20, 25 }, (-16.5, -17.5), 3);
        public const double LEASH = 14;            // attack: how far it may leave its post chasing you
        public const double RESPAWN_SECS = 2;      // the hero is back at its post this long after you drop it
        public const double RESET_SECS = 3;        // defense: back to full health this long after the last hit
        public const double BURST_GAP = 2.5;       // no damage for this long ends a burst (its DPS figure freezes)
        public static readonly (string name, double skill)[] SKILLS = { ("RECRUIT", 0.4), ("VETERAN", 0.62), ("ELITE", 0.8), ("LEGEND", 0.95) };
        public static readonly RangeOpts DEFAULT_OPTS = new RangeOpts { hero = "gorgoth", mode = "defense", abilities = false, move = "hold", dist = 10, skill = 0.62 };

        /// <summary>Hex and Enra have nothing purely defensive - Hex still has his passive Stitched Decoy</summary>
        public static readonly Dictionary<string, Guard[]> GUARD = new Dictionary<string, Guard[]>
        {
            ["tenkai"] = new[] { new Guard { slot = "alt", id = "bulwark", on = "hit", hold = true } },
            ["gorgoth"] = new[] { new Guard { slot = "alt", id = "plating", on = "hit" } },
            ["gantetsu"] = new[] { new Guard { slot = "a2", id = "taiko", on = "hit" } },
            ["tomoe"] = new[] { new Guard { slot = "a1", id = "warcall", on = "hit" } },
            ["mirei"] = new[] { new Guard { slot = "a2", id = "wish", on = "hurt" }, new Guard { slot = "a1", id = "constellation", on = "hurt" } },
            ["kaien"] = new[] { new Guard { slot = "a1", id = "spiritstep", on = "hurt", dodge = true } },
            ["nocturne"] = new[] { new Guard { slot = "a2", id = "bloodpact", on = "hurt" } },
            ["hibiki"] = new[] { new Guard { slot = "a2", id = "maxvolume", on = "hurt" } },          // (Healing Groove is his starting track)
            ["raijin"] = new[] { new Guard { slot = "a2", id = "parry", on = "hit" } },
            ["hayate"] = new[] { new Guard { slot = "a2", id = "mirrorwater", on = "hit" } },
            ["yuzu"] = new[] { new Guard { slot = "a1", id = "sunhop", on = "hurt" } },
            ["seiran"] = new[] { new Guard { slot = "a1", id = "riverstep", on = "hurt", dodge = true } },
            ["kagemaru"] = new[] { new Guard { slot = "a2", id = "veil", on = "hurt" } },
            ["hex"] = new Guard[0], ["enra"] = new Guard[0],
        };

        /// <summary>DmgClass: "weapon" | "ability" | "dot"</summary>
        public static string ClassOf(string kind) => kind == "ability" ? "ability" : kind == "dot" ? "dot" : "weapon";
        /// <summary>damage that arrives every tick (a beam, a bleed): it counts toward the totals and DPS but isn't a "hit"</summary>
        public static bool Continuous(string kind) => kind == "dot" || kind == "beam";

        public readonly World w; public readonly INav nav;
        public RangeOpts opts = DEFAULT_OPTS.Clone();
        public Actor bot;
        public RangeBrain brain;
        public RangeStats stats = new RangeStats();
        /// <summary>a spar is on (Spar.cs): the range hero stands down at its post - nothing joins a one-on-one</summary>
        public bool suspended;
        /// <summary>bumped on every deploy / clear: a respawn timer from an older target does nothing</summary>
        int gen;

        public HeroRange(World w, INav nav)
        {
            this.w = w; this.nav = nav;
            w.taps.Add(e => Observe(e));
        }

        public Actor Player => w.actors.FirstOrDefault(a => a.isPlayer);
        public (double x, double z) Post => (LANE.x0 + opts.dist, LANE.z);

        /// <summary>(TS deploy(Partial&lt;RangeOpts&gt;): the options left out keep their current values)</summary>
        public Actor Deploy(string hero = null, string mode = null, bool? abilities = null, string move = null, double? dist = null, double? skill = null)
        {
            var next = opts.Clone();
            if (hero != null) next.hero = hero; if (mode != null) next.mode = mode; if (abilities.HasValue) next.abilities = abilities.Value;
            if (move != null) next.move = move; if (dist.HasValue) next.dist = dist.Value; if (skill.HasValue) next.skill = skill.Value;
            if (!GameData.Current.Hero.ContainsKey(next.hero)) return null;
            bool keep = bot != null && bot.alive && bot.baseDef.id == next.hero;
            opts = next;
            if (!keep)
            {
                Clear(false);
                var a0 = w.AddHero(next.hero, "umbra");
                bot = a0;
            }
            var a = bot;
            a.spawn = new[] { Post.x, Post.z };
            brain = new RangeBrain(this, a);
            a.controller = brain;
            Place();
            ResetStats();
            return a;
        }

        /// <summary>remove the range hero and everything it summoned or fired</summary>
        public void Clear(bool bump = true)
        {
            var a = bot;
            if (bump) gen++;
            if (a == null) return;
            w.actors = w.actors.Where(x => x != a && x.owner != a).ToList();
            w.zones = w.zones.Where(z => z.owner != a).ToList();
            w.projs = w.projs.Where(p => p.owner != a).ToList();
            a.controller = null; a.alive = false;
            bot = null; brain = null;
        }

        /// <summary>back to full at its post, facing the firing line</summary>
        public void Place()
        {
            var a = bot; if (a == null) return;
            gen++;
            var p = Post;
            w.Respawn(a, true);
            double g = w.level.GroundAt(p.x, p.z, 4);
            a.pos = new V3(p.x, Math.Max(0, g > double.NegativeInfinity ? g : 0) + (a.def.frame == "drone" ? 3 : 0), p.z);
            a.yaw = a.input.yaw = -Math.PI / 2; a.input.pitch = 0;
            a.cd = new Dictionary<string, double>();
            NewLife();
        }

        public void ResetStats()
        {
            var me = Player; var s = stats;
            stats = new RangeStats();
            stats.log = s.log;
            stats.shots0 = me?.shots ?? 0; stats.hits0 = me?.hits ?? 0;
        }

        void NewLife() { var s = stats; s.lifeStart = -1; s.lifeDmg = 0; s.lifeHits = 0; s.lifeCrits = 0; s.frameDown = -1; }

        /// <summary>your side of a hit: you, or something you summoned / left behind</summary>
        bool Mine(Actor x) { var me = Player; return me != null && x != null && (x == me || x.owner == me); }
        bool Theirs(Actor x) => bot != null && x != null && (x == bot || x.owner == bot);

        public void Observe(SimEvent e)
        {
            var s = stats; double t = w.time; var a = bot;
            if (a == null) return;
            if (e is DmgEvent d && !d.heal)
            {
                if (d.tgt == a && Mine(d.src))
                {
                    int hitsBefore = s.dealt.hits, critsBefore = s.dealt.crits;
                    s.dealt.Add(t, d.amt, d.crit, d.kind);
                    if (s.lifeStart < 0) s.lifeStart = t;
                    s.lifeDmg += d.amt; s.lifeHits += s.dealt.hits - hitsBefore; s.lifeCrits += s.dealt.crits - critsBefore;
                    var me = Player; if (me != null) s.lastDist = M.Hypot(me.pos.x - a.pos.x, me.pos.z - a.pos.z);
                }
                else if (Mine(d.tgt) && d.tgt == Player && Theirs(d.src)) s.taken.Add(t, d.amt, d.crit, d.kind);
            }
            else if (e is DemechEvent dm && dm.tgt == a && s.lifeStart >= 0) s.frameDown = t - s.lifeStart;
            else if (e is KillEvent k)
            {
                if (k.tgt == a)
                {
                    s.kills++;
                    double ttk = s.lifeStart >= 0 ? t - s.lifeStart : 0;
                    s.lastTtk = ttk; s.bestTtk = s.bestTtk == null ? ttk : Math.Min(s.bestTtk.Value, ttk);
                    var me = Player;
                    s.log.Insert(0, new RangeResult
                    {
                        you = me?.def.name ?? "-", target = a.baseDef.name, mode = opts.mode, abilities = opts.abilities, dist = JsMath.Round(s.lastDist),
                        ttk = ttk, dmg = s.lifeDmg, hits = s.lifeHits, crits = s.lifeCrits, dps = ttk > 0 ? s.lifeDmg / ttk : (double?)null,
                        frameDown = s.frameDown >= 0 ? s.frameDown : (double?)null,
                    });
                    if (s.log.Count > 8) s.log.RemoveRange(8, s.log.Count - 8);
                    // back at its post in RESPAWN_SECS (the world's own respawn would put it in the Umbra spawn after 6 s)
                    a.respawnAt = 0;
                    int g = gen;
                    w.After(RESPAWN_SECS, () => { if (gen == g && bot == a) Place(); });
                }
                else if (k.tgt == Player && Theirs(k.src)) s.deaths++;
            }
        }
    }

    /// <summary>the range hero's controller: the hero AI (attack) or the guard routine (defense)</summary>
    public class RangeBrain : IController
    {
        public readonly HeroRange r; public readonly Actor a;
        public Bot bot;
        public double strafe = 1, strafeUntil, guardAt;

        public RangeBrain(HeroRange r, Actor a)
        {
            this.r = r; this.a = a;
            bot = new Bot(r.w, a, r.nav, r.opts.skill);
        }

        static double Wrap(double x) => Math.Atan2(Math.Sin(x), Math.Cos(x));

        public void Think(double dt)
        {
            var o = r.opts; var w = r.w; var i = a.input; double t = w.time; var me = r.Player;
            if (r.suspended)
            {
                i.fire = i.alt = i.melee = i.a1 = i.a2 = i.ult = i.jump = i.jumpHeld = i.reload = i.swoop = i.descend = false;
                var p0 = r.Post; Steer(p0.x - a.pos.x, p0.z - a.pos.z, M.Hypot(p0.x - a.pos.x, p0.z - a.pos.z) > 0.6 ? 1 : 0);
                return;
            }
            if (o.mode == "attack")
            {
                bot.Think(dt);
                var p0 = r.Post; double off = M.Hypot(a.pos.x - p0.x, a.pos.z - p0.z);
                // leashed: nobody to fight, or chased too far - walk back to the post
                if (bot.target == null || off > HeroRange.LEASH)
                {
                    if (off > HeroRange.LEASH) { i.fire = false; i.alt = false; }
                    Steer(p0.x - a.pos.x, p0.z - a.pos.z, off > 1.2 ? 1 : 0);
                }
                if (!o.abilities)
                {
                    i.a1 = i.a2 = i.ult = false; i.swoop = false;
                    if (a.def.secondary != null && a.def.secondary.IsAbility) { i.alt = false; bot.holdAlt = 0; }
                }
                return;
            }
            // ---- defense: never fires; faces you; holds or strafes; guards itself if allowed
            i.fire = i.alt = i.melee = i.a1 = i.a2 = i.ult = i.jump = i.jumpHeld = i.reload = i.swoop = i.descend = false;
            if (me != null && me.alive)
            {
                V3 c = me.Center, e = a.Eye;
                i.yaw = Math.Atan2(c.x - e.x, c.z - e.z); i.pitch = Math.Atan2(c.y - e.y, M.Hypot(c.x - e.x, c.z - e.z));
            }
            var p = r.Post;
            double dx = p.x - a.pos.x, dz = p.z - a.pos.z;
            if (o.move == "strafe")
            {
                if (t > strafeUntil) { strafe = -strafe; strafeUntil = t + 0.45 + Rng.Random() * 0.9; }
                double side = a.pos.z - p.z;
                if (Math.Abs(side) > HeroRange.LANE.strafe) strafe = side > 0 ? -1 : 1;
                dz = strafe * 4; dx = (p.x - a.pos.x) * 2;
                Steer(dx, dz, 1);
            }
            else Steer(dx, dz, M.Hypot(dx, dz) > 0.6 ? 1 : 0);     // knocked off the spot: walk back
            // between bursts: back to full, the way the training robots get back up
            if (t - a.lastDamagedAt > HeroRange.RESET_SECS && (a.hp < a.def.hp || a.armor < a.maxArmor || a.wounds.Count > 0))
            {
                a.hp = a.def.hp; a.armor = a.maxArmor; a.wounds = new List<Wound>();
                if (a.def != a.baseDef) r.Place();       // a mech pilot: the frame comes back
                else r.stats.lifeStart = -1;
                r.stats.lifeDmg = r.stats.lifeHits = r.stats.lifeCrits = 0; r.stats.frameDown = -1;
            }
            if (o.abilities) GuardSelf();
        }

        /// <summary>move toward a world-space direction (magnitude k) whatever way the hero faces</summary>
        void Steer(double dx, double dz, double k)
        {
            var i = a.input; double l = M.Hypot(dx, dz);
            if (k == 0 || l < 1e-3) { i.mx = i.mz = 0; return; }
            double x = dx / l * k, z = dz / l * k, fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
            i.mz = x * fx + z * fz; i.mx = x * rx + z * rz;
        }

        /// <summary>(TS guard)</summary>
        void GuardSelf()
        {
            var i = a.input; var w = r.w; double t = w.time;
            bool hitNow = t - a.lastDamagedAt < 0.35, underFire = t - a.lastDamagedAt < 1.2, hurt = a.Health / a.MaxHp < 0.6;
            foreach (var g in HeroRange.GUARD.TryGetValue(a.baseDef.id, out var gs) ? gs : new Guard[0])
            {
                if (a.def != a.baseDef) break;          // (a mech's pilot on foot has his own kit)
                if (g.hold) { if (underFire && a.barrier.hp > 40) i.alt = true; continue; }
                bool want = g.on == "hit" ? hitNow : hurt && underFire;
                if (!want || t < guardAt || !a.Ready(g.id, t)) continue;
                if (g.dodge)
                {
                    // step sideways out of the line of fire, not toward the shooter
                    var me = r.Player;
                    double side = (a.pos.z - r.Post.z) > 0 ? -1 : 1;
                    if (me != null) i.yaw = Wrap(Math.Atan2(me.pos.x - a.pos.x, me.pos.z - a.pos.z) + side * Math.PI / 2);
                    i.mz = 1; i.mx = 0;
                }
                if (g.slot == "a1") i.a1 = true; else if (g.slot == "a2") i.a2 = true; else i.alt = true;
                guardAt = t + 0.3;           // released next tick: abilities fire on the press
                break;
            }
        }
    }
}
