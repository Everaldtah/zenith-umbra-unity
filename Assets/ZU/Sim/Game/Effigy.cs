// Enra - Crimson Effigy: a giant crimson hologram of the oni himself rises behind him and fights beside him for
// EFFIGY_SECS, then fades; a ghost (phased every tick) whose blade sweeps every enemy in sight within EFFIGY_R of Enra.
// Port of zenith-umbra src/game/effigy.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static class Effigy
    {
        /// <summary>how long the effigy stands</summary>
        public const double EFFIGY_SECS = 10;
        /// <summary>its perimeter: enemies this close to Enra (in the flat) are struck, up to EFFIGY_H above or below him</summary>
        public const double EFFIGY_R = 12, EFFIGY_H = 4;
        /// <summary>a sweep: damage, seconds between sweeps, the first one's delay after the rise</summary>
        public const double EFFIGY_HIT_DMG = 45, EFFIGY_HIT_EVERY = 0.9, EFFIGY_HIT_FIRST = 0.8;
        /// <summary>rising out of the ground: this long before the first sweep and before it moves</summary>
        public const double EFFIGY_RISE = 0.6;
        /// <summary>the swing begins this long before its hit lands (so the blade is ON the enemies when the damage ticks)</summary>
        public const double EFFIGY_SWING_LEAD = 0.33 * EFFIGY_HIT_EVERY;
        /// <summary>it stands this far behind Enra and closes on that spot at this speed</summary>
        const double BEHIND = 2.2, FOLLOW = 9;

        static SlotDef None(string id) => new SlotDef { id = id, name = "-", key = "-", cooldown = 999, desc = "" };
        public static readonly HeroDef EFFIGY_DEF = new HeroDef
        {
            id = "enra_effigy", name = "Crimson Effigy", title = "The Oni Unbound", team = "umbra", role = "dps", frame = "human", rival = "",
            hp = 1e6, armor = 0, speed = FOLLOW, height = 2.05 * 3, radius = 0.6, color = "#ff2a2a", glow = "#ff4a2a",
            primary = new SlotDef { kind = "melee", name = "Effigy Blade", damage = EFFIGY_HIT_DMG, rate = 1 / EFFIGY_HIT_EVERY, range = EFFIGY_R, sfx = "none", fx = "none" },
            secondary = None("none"), ability1 = None("none"), ability2 = None("none"), ult = new SlotDef { id = "none", name = "-", key = "-", cooldown = 999, desc = "", charge = 1e9 },
            passive = new Passive { name = "", desc = "" }, lore = "The oni as the old stories drew him: a mountain of red fire with a blade the size of a gate.", inspiration = "", voice = new double[] { 80, 0.9 },
            summoned = true, full = true,
            model = "enra_susanoo", holo = "#ff2a2a", scale = 3,
        };

        public class EffigyBrain : IController
        {
            public double nextHit;
            /// <summary>the swing toward nextHit has begun</summary>
            public bool swung;
            readonly World w; readonly Actor a;
            public EffigyBrain(World w, Actor a) { this.w = w; this.a = a; }

            public void Think(double dt)
            {
                double t = w.time; var i = a.input; var o = a.owner;
                i.fire = i.alt = i.a1 = i.a2 = i.ult = i.melee = i.reload = i.swoop = false; i.jump = false; i.jumpHeld = false;
                i.mx = i.mz = 0;
                // a ghost: nothing hits it, it walks through bodies
                a.Set("phased", t, 0.3);
                if (o == null || !o.alive || t >= a.Sv("until", 0)) { DropEffigy(w, a); return; }
                if (t < a.Sv("riseUntil", 0)) return;
                // stand behind Enra, facing where he faces
                var f = o.Forward();
                double wantX = o.pos.x - f.x * BEHIND, wantZ = o.pos.z - f.z * BEHIND;
                double dx = wantX - a.pos.x, dz = wantZ - a.pos.z, d = M.Hypot(dx, dz);
                i.yaw = o.yaw; i.pitch = 0;
                if (d > 0.4)
                {
                    double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                    double ux = dx / d, uz = dz / d, k = Math.Min(1, d / 2);
                    i.mz = (ux * fx + uz * fz) * k; i.mx = (ux * rx + uz * rz) * k;
                }
                // far behind (Enra dashed off, or it got stuck): it simply appears at his back
                if (d > 14) { a.pos = new V3(wantX, o.pos.y, wantZ); a.vel = V3.Zero; }
                // the swing winds up ahead of its hit
                if (!swung && t >= nextHit - EFFIGY_SWING_LEAD)
                {
                    swung = true;
                    a.anim.attackAt = t; a.anim.attackKind = "primary"; a.anim.attackSide = -(a.anim.attackSide != 0 ? a.anim.attackSide : -1);
                }
                if (t < nextHit) return;
                nextHit = t + EFFIGY_HIT_EVERY; swung = false;
                // the sweep: everyone inside the perimeter it can see
                var eye = new V3(a.pos.x, a.pos.y + a.Height * 0.6, a.pos.z);
                int n = 0;
                foreach (var x in w.Enemies(o))
                {
                    if (!x.alive || x.IsSummon) continue;                             // (training dummies included)
                    if (M.Hypot(x.pos.x - o.pos.x, x.pos.z - o.pos.z) > EFFIGY_R + x.Radius || Math.Abs(x.pos.y - o.pos.y) > EFFIGY_H) continue;
                    if (!w.level.LineOfSight(eye, x.Center)) continue;
                    double dealt = w.Damage(o, x, EFFIGY_HIT_DMG, new DmgOpts { kind = "ability", ability = "effigy", noLifesteal = true });
                    if (dealt > 0) { n++; o.stats["effigyDmg"] = (o.stats.TryGetValue("effigyDmg", out var ed) ? ed : 0) + dealt; w.Fx("hit", x.Center, new FxOpts { color = "#ff2a2a" }); }
                    // shoved back from the blade, off their feet a little
                    if (dealt > 0 && x.def.frame != "mech" && !x.isBoss && !x.Has("ccimmune", t))
                    {
                        double kx = x.pos.x - a.pos.x, kz = x.pos.z - a.pos.z, kl = M.Hypot(kx, kz); if (kl == 0) kl = 1;
                        x.forced = new Forced { vx = kx / kl * 6, vy = 0, vz = kz / kl * 6, until = t + 0.15, kind = "knock" };
                    }
                }
                w.Fx("effigyswing", a.Center, new FxOpts { color = "#ff2a2a", actor = a, r = EFFIGY_R });
                w.Sfx(n > 0 ? "punch" : "whiff", a.Center, a);
            }
        }

        /// <summary>Enra's effigy, standing or gone</summary>
        public static Actor EffigyOf(World w, Actor owner) => w.actors.FirstOrDefault(x => x.IsSummon && x.def == EFFIGY_DEF && x.owner == owner);

        /// <summary>the effigy fades where it stands</summary>
        public static void DropEffigy(World w, Actor e)
        {
            if (!e.alive) return;
            double t = w.time;
            e.alive = false; e.deathAt = t; e.respawnAt = 0; e.forced = null; e.sv["fellAt"] = t;
            e.owner?.Clear("effigy");
            w.Fx("effigyfade", e.Center, new FxOpts { color = "#ff2a2a", actor = e }); w.Sfx("spindown", e.Center, e);
        }

        /// <summary>Crimson Effigy: raise it behind him</summary>
        public static Actor RaiseEffigy(World w, Actor a, double secs = EFFIGY_SECS)
        {
            double t = w.time;
            var e = EffigyOf(w, a);
            if (e != null) DropEffigy(w, e);                             // a second cast replaces the first
            if (e == null)
            {
                e = new Actor(EFFIGY_DEF, a.team) { owner = a, isRobot = true, noRespawn = true, alive = false };
                e.controller = new EffigyBrain(w, e);
                w.actors.Add(e);
            }
            var f = a.Forward();
            var p = new V3(a.pos.x - f.x * BEHIND, a.pos.y, a.pos.z - f.z * BEHIND);
            double X = w.level.Size[0], Z = w.level.Size[1];
            p.x = Math.Max(-X + 2, Math.Min(X - 2, p.x)); p.z = Math.Max(-Z + 2, Math.Min(Z - 2, p.z));
            double g = w.level.GroundAt(p.x, p.z, a.pos.y + 1.5);
            if (!double.IsNegativeInfinity(g) && Math.Abs(g - a.pos.y) < 2) p.y = g;
            e.team = a.team;
            e.pos = p; e.vel = V3.Zero;
            e.yaw = e.input.yaw = a.yaw; e.pitch = 0; e.hp = EFFIGY_DEF.hp; e.armor = 0; e.scale = 1;
            e.shields = new List<Shield>(); e.wounds = new List<Wound>(); e.st = new Dictionary<string, double>(); e.sv = new Dictionary<string, double>(); e.forced = null;
            e.alive = true; e.respawnAt = 0; e.deathAt = -99; e.lastDamagedAt = t;
            e.sv["riseAt"] = t; e.sv["riseUntil"] = t + EFFIGY_RISE; e.sv["until"] = t + secs;
            e.Set("phased", t, 0.3); e.Set("spawnprot", t, EFFIGY_RISE);
            var b = (EffigyBrain)e.controller; b.nextHit = t + EFFIGY_RISE + EFFIGY_HIT_FIRST; b.swung = false;
            a.Set("effigy", t, secs);
            return e;
        }
    }
}
