// Hex - Grand Puppet Theater: fifty masked puppets rise around him and fight for him, then fall lifeless; while they stand
// they pay a life tithe (every teammate near him is mended). Port of zenith-umbra src/game/puppets.ts.
// The puppets are Actors from a pool reused by each cast, flagged by their def (`summoned`).
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static class Puppets
    {
        public const int PUPPET_COUNT = 50;
        /// <summary>how long the army fights before the strings go slack</summary>
        public const double PUPPET_SECS = 15;
        /// <summary>rising out of the floor: untouchable and still for this long</summary>
        public const double PUPPET_RISE = 0.9;
        /// <summary>a claw swipe: damage, reach (m beyond the two radii), seconds between swipes</summary>
        public const double PUPPET_HIT_DMG = 10, PUPPET_HIT_REACH = 0.9, PUPPET_HIT_EVERY = 0.9;
        /// <summary>cut the strings when the puppeteer falls</summary>
        public const bool PUPPETS_FALL_WITH_HEX = true;
        /// <summary>the life tithe: while the army stands, every teammate within this of Hex heals this much a second</summary>
        public const double PUPPET_HEAL_R = 15, PUPPET_HEAL = 20;
        public const string PUPPET_HEAL_COLOR = "#c77dff";

        static SlotDef None(string id) => new SlotDef { id = id, name = "-", key = "-", cooldown = 999, desc = "" };
        public static readonly HeroDef PUPPET_DEF = new HeroDef
        {
            id = "puppet", name = "Puppet", title = "Stitched Student", team = "umbra", role = "dps", frame = "human", rival = "",
            hp = 50, armor = 0, speed = 5.5, height = 1.75, radius = 0.34, color = "#b56dff", glow = "#c77dff",
            // (the claws are swung by the puppet's brain below: the weapon system never fires for a puppet)
            primary = new SlotDef { kind = "melee", name = "Claws", damage = PUPPET_HIT_DMG, rate = 1 / PUPPET_HIT_EVERY, range = 1.6, sfx = "none", fx = "none" },
            secondary = None("none"), ability1 = None("none"), ability2 = None("none"), ult = new SlotDef { id = "none", name = "-", key = "-", cooldown = 999, desc = "", charge = 1e9 },
            passive = new Passive { name = "", desc = "" }, lore = "One of the Dollmaker's students, restitched and obedient.", inspiration = "", voice = new double[] { 300, 0.2 },
            summoned = true, full = true,
        };

        static double D2(V3 a, V3 b) => M.Hypot(a.x - b.x, a.z - b.z);

        public class PuppetBrain : IController
        {
            public Actor target;
            public List<V3> path = new List<V3>();
            public double nextPick, nextPath, nextHit;
            readonly World w; readonly Actor a;
            public PuppetBrain(World w, Actor a) { this.w = w; this.a = a; }

            public void Think(double dt)
            {
                double t = w.time; var i = a.input;
                i.fire = i.alt = i.a1 = i.a2 = i.ult = i.melee = i.reload = i.swoop = false;
                i.jump = false; i.jumpHeld = false;
                if (t < a.Sv("riseUntil", 0)) { i.mx = i.mz = 0; return; }
                if (t >= nextPick)
                {
                    nextPick = t + 0.4 + (a.id % 7) * 0.03;
                    target = PickTarget(w, a);
                }
                var tg = target;
                if (tg == null || !tg.alive) { i.mx = i.mz = 0; target = null; return; }
                double d = D2(tg.pos, a.pos), reach = a.Radius + tg.Radius + PUPPET_HIT_REACH;
                double dy = tg.pos.y - a.pos.y;
                // a path when the way isn't straight (staggered, and a few searches a tick for the whole army)
                if (w.nav != null && t >= nextPath && (d > 5 || Math.Abs(dy) > 1.2) && w.sv["puppetPaths"] < 3)
                {
                    nextPath = t + 1.1 + (a.id % 9) * 0.07;
                    if (!w.level.LineOfSight(a.Eye, tg.Center)) { w.sv["puppetPaths"]++; path = w.nav.Find(a.pos, tg.pos, 9000) ?? new List<V3>(); }
                    else path = new List<V3>();
                }
                while (path.Count > 0 && D2(path[0], a.pos) < 0.9) path.RemoveAt(0);
                var wp = path.Count > 0 ? path[0] : tg.pos;
                double lx = wp.x - a.pos.x, lz = wp.z - a.pos.z, l = M.Hypot(lx, lz); if (l == 0) l = 1;
                i.yaw = Math.Atan2(tg.pos.x - a.pos.x, tg.pos.z - a.pos.z);
                i.pitch = 0;
                if (d > reach * 0.85)
                {
                    // steer along the way, in the puppet's own frame (it faces its target)
                    double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                    double dx = lx / l, dz = lz / l;
                    i.mz = dx * fx + dz * fz; i.mx = dx * rx + dz * rz;
                    // a low ledge in the way: hop it
                    if (a.grounded && path.Count > 0 && wp.y > a.pos.y + 0.4) i.jump = true;
                }
                else { i.mx = i.mz = 0; }
                if (d <= reach && Math.Abs(dy) < 2.2 && t >= nextHit && !a.Has("stun", t) && !a.Has("knockdown", t))
                {
                    nextHit = t + PUPPET_HIT_EVERY * (0.9 + (a.id % 5) * 0.05);
                    a.anim.attackAt = t; a.anim.attackKind = "primary";
                    var owner = a.owner != null && a.owner.team == a.team ? a.owner : a;
                    // the damage is the puppeteer's (his kills, his damage done), but it never feeds his next ultimate
                    double dealt = w.Damage(owner, tg, PUPPET_HIT_DMG, new DmgOpts { kind = "ability", ability = "puppet", noLifesteal = true });
                    if (dealt > 0) { w.Sfx("scratch", tg.Center); owner.stats["puppetDmg"] = (owner.stats.TryGetValue("puppetDmg", out var pd) ? pd : 0) + dealt; }
                }
            }
        }

        /// <summary>the nearest enemy hero, spread out so the whole army doesn't pile onto one body</summary>
        static Actor PickTarget(World w, Actor a)
        {
            double t = w.time;
            var foes = w.actors.Where(x => x.alive && x.team != a.team && !x.IsSummon
                && !x.Has("phased", t) && !(x.Has("stealth", t) && !x.Has("revealed", t))).ToList();
            var pool = foes.Count > 0 ? foes : w.actors.Where(x => x.alive && x.team != a.team && !x.IsSummon).ToList();   // (the veiled, if that's all there is)
            if (pool.Count == 0) return null;
            var army = w.actors.Where(x => x.alive && x.IsSummon && x.team == a.team && x != a).ToList();
            double cap = Math.Ceiling((army.Count + 1) / (double)pool.Count) + 2;
            Actor best = null; double bs = double.PositiveInfinity;
            foreach (var x in pool)
            {
                double d = M.Hypot(x.pos.x - a.pos.x, x.pos.z - a.pos.z) + Math.Abs(x.pos.y - a.pos.y) * 2;
                if (d > 70) continue;
                int on = army.Count(p => (p.controller as PuppetBrain)?.target == x);
                double s = d + (on >= cap ? 25 : 0);
                if (s < bs) { bs = s; best = x; }
            }
            return best;
        }

        /// <summary>this caster's puppets (standing or fallen)</summary>
        public static List<Actor> PuppetsOf(World w, Actor owner) => w.actors.Where(x => x.IsSummon && x.owner == owner).ToList();

        /// <summary>the strings go slack: every puppet of this caster falls where it stands</summary>
        public static int DropPuppets(World w, Actor owner)
        {
            double t = w.time;
            int n = 0;
            foreach (var p in PuppetsOf(w, owner))
            {
                if (!p.alive) continue;
                p.alive = false; p.deathAt = t; p.respawnAt = 0; p.forced = null; p.sv["fellAt"] = t;
                n++;
            }
            owner.Clear("puppeteer");
            if (n > 0) { w.Fx("puppetsfall", owner.Center, new FxOpts { color = "#c77dff", actor = owner }); w.Sfx("spindown", owner.Center, owner); }
            return n;
        }

        /// <summary>spots around the caster where a puppet can stand: rings on his own floor, in sight of him, clear of walls</summary>
        static List<V3> Spots(World w, Actor a, int n)
        {
            var output = new List<V3>();
            double X = w.level.Size[0], Z = w.level.Size[1];
            int ring = 0;
            for (int tries = 0; output.Count < n && tries < 12; tries++, ring++)
            {
                double r = 2.4 + ring * 1.15; int k = (int)Math.Max(6, JsMath.Round((2 * Math.PI * r) / 1.25));
                for (int j = 0; j < k && output.Count < n; j++)
                {
                    double ang = ((double)j / k) * Math.PI * 2 + ring * 0.37;
                    double x = a.pos.x + Math.Cos(ang) * r, z = a.pos.z + Math.Sin(ang) * r;
                    if (Math.Abs(x) > X - 1 || Math.Abs(z) > Z - 1) continue;
                    double g = w.level.GroundAt(x, z, a.pos.y + 1.2);
                    if (double.IsNegativeInfinity(g) || Math.Abs(g - a.pos.y) > 1.6) continue;
                    var p = new V3(x, g, z);
                    var before = p;
                    w.level.Collide(ref p, PUPPET_DEF.radius, PUPPET_DEF.height);
                    if (M.Hypot(p.x - before.x, p.z - before.z) > 0.25) continue;      // it was inside a wall
                    if (!w.level.LineOfSight(a.Eye, new V3(p.x, p.y + 1.2, p.z))) continue;
                    output.Add(p);
                }
            }
            // a cramped room: the rest stand in a tight ring around him
            for (int j = 0; output.Count < n; j++) { double ang = j * 2.4; output.Add(new V3(a.pos.x + Math.Cos(ang) * 1.6, a.pos.y, a.pos.z + Math.Sin(ang) * 1.6)); }
            return output;
        }

        /// <summary>Grand Puppet Theater: raise the army</summary>
        public static int RaisePuppets(World w, Actor a, int count = PUPPET_COUNT, double secs = PUPPET_SECS)
        {
            double t = w.time;
            DropPuppets(w, a);                                  // a second cast replaces the first army
            var pool = PuppetsOf(w, a);
            while (pool.Count < count)
            {
                var np = new Actor(PUPPET_DEF, a.team);
                np.owner = a; np.isRobot = true; np.noRespawn = true; np.alive = false;
                np.controller = new PuppetBrain(w, np);
                w.actors.Add(np); pool.Add(np);
            }
            var at = Spots(w, a, count);
            for (int k = 0; k < count; k++)
            {
                var p = pool[k]; var s = at[k];
                p.team = a.team;
                p.pos = s; p.vel = V3.Zero;
                p.yaw = p.input.yaw = Math.Atan2(s.x - a.pos.x, s.z - a.pos.z);
                p.pitch = 0; p.hp = PUPPET_DEF.hp; p.armor = 0; p.scale = 1;
                p.shields = new List<Shield>(); p.wounds = new List<Wound>(); p.st = new Dictionary<string, double>(); p.sv = new Dictionary<string, double>(); p.forced = null;
                p.alive = true; p.respawnAt = 0; p.deathAt = -99; p.lastDamagedAt = t;
                double rise = PUPPET_RISE + (k % 10) * 0.035;       // they come up in a ripple, not as one
                p.sv["riseAt"] = t; p.sv["riseUntil"] = t + rise;
                p.Set("spawnprot", t, rise);
                var b = (PuppetBrain)p.controller; b.target = null; b.path = new List<V3>(); b.nextPick = t + rise; b.nextPath = 0; b.nextHit = t + rise + 0.2;
            }
            a.Set("puppeteer", t, secs);
            a.sv["puppetsUntil"] = t + secs;
            double cast = t;
            w.After(secs, () => { if (a.sv.TryGetValue("puppetsCast", out var pc) && pc == cast) DropPuppets(w, a); });
            a.sv["puppetsCast"] = cast;
            return count;
        }

        /// <summary>per tick: the path budget, the life tithe, and the puppeteer's own fall</summary>
        public static void TickPuppets(World w, double dt)
        {
            w.sv["puppetPaths"] = 0;
            double t = w.time;
            foreach (var a in w.actors.ToList())
            {
                if (a.IsSummon || !a.sv.ContainsKey("puppetsCast")) continue;
                if (a.alive && a.Has("puppeteer", t) && PuppetsOf(w, a).Any(p => p.alive))
                {
                    // the life tithe: the army mends every teammate in reach of the puppeteer, him included - his healing
                    // done, but never charge toward his next ultimate
                    foreach (var x in w.Allies(a))
                    {
                        if (World.Dist3(x.pos, a.pos) > PUPPET_HEAL_R) continue;
                        x.Set("tithe", t, 0.35);
                        if (x.hp < x.def.hp) { double ult = a.ult; w.Heal(a, x, PUPPET_HEAL * dt, true); a.ult = ult; }
                    }
                }
                else if (!a.alive && PUPPETS_FALL_WITH_HEX && a.deathAt >= a.sv["puppetsCast"] && t - a.deathAt < 0.2)
                {
                    if (PuppetsOf(w, a).Any(p => p.alive)) DropPuppets(w, a);
                }
            }
        }
    }
}
