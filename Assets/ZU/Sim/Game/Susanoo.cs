// Raijin - Storm Sovereign: a holographic thunder-god giant of himself rises at his back: five seconds of lightning on
// every enemy in the perimeter, then five of the giant striding after them with its blade; then it fades.
// Port of zenith-umbra src/game/susanoo.ts. The giant is a summoned, untouchable ('phased') Actor drawn by its own view.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static class Susanoo
    {
        /// <summary>the perimeter (m) around the spot he cast it</summary>
        public const double SUSANOO_R = 12;
        /// <summary>the two halves (s): the thunder, then the giant's blade</summary>
        public const double SUSANOO_THUNDER = 5, SUSANOO_BLADE = 5;
        /// <summary>the thunder: cadence (s), damage, the first one's stun, its delay; a roof keeps it out</summary>
        public const double STRIKE_EVERY = 1, STRIKE_DMG = 40, STRIKE_STUN = 0.4, STRIKE_FIRST = 0.6;
        /// <summary>the blade: sweep reach (m), damage, cadence (s), walking speed</summary>
        public const double SLASH_R = 5.5, SLASH_DMG = 60, SLASH_EVERY = 1, SUSANOO_SPEED = 5;
        public const string SUSANOO_COLOR = "#8ad8ff";
        /// <summary>where it rises (m): to his right and ahead (or behind him where walls are in the way)</summary>
        public const double SUSANOO_SIDE = 3.5, SUSANOO_AHEAD = 4, SUSANOO_BEHIND = 3;
        /// <summary>first person: the camera pulls out to third person this long after the cast (s)</summary>
        public const double SUSANOO_SHOWCASE = 1.5;
        /// <summary>the blade half, with no one left to cut: it strides back to within this much of his side (m)</summary>
        public const double SUSANOO_FOLLOW = 6;

        static SlotDef None(string id) => new SlotDef { id = id, name = "-", key = "-", cooldown = 999, desc = "" };
        public static readonly HeroDef SUSANOO_DEF = new HeroDef
        {
            id = "susanoo", name = "Storm Sovereign", title = "Raijin's Susanoo", team = "zenith", role = "dps", frame = "human", rival = "",
            hp = 1e6, armor = 0, speed = SUSANOO_SPEED, height = 5.4, radius = 1.2, color = "#ffe066", glow = SUSANOO_COLOR,
            primary = new SlotDef { kind = "melee", name = "Storm Blade", damage = SLASH_DMG, rate = 1 / SLASH_EVERY, range = SLASH_R, sfx = "none", fx = "none" },
            secondary = None("none"), ability1 = None("none"), ability2 = None("none"), ult = new SlotDef { id = "none", name = "-", key = "-", cooldown = 999, desc = "", charge = 1e9 },
            passive = new Passive { name = "", desc = "" }, lore = "The thunder god Raijin sees in the mirror.", inspiration = "", voice = new double[] { 120, 0.3 },
            summoned = true, full = true,
            model = "raijin_susanoo", holo = SUSANOO_COLOR, scale = 3,
        };

        /// <summary>a strike from the sky reaches a target that has open sky above it (roofs keep the thunder out)</summary>
        public static bool SkyOpen(World w, Actor x) => double.IsPositiveInfinity(w.level.CeilingAt(x.pos.x, x.pos.z, x.pos.y + x.Height));

        static (Actor, double) Nearest(IEnumerable<Actor> xs, Actor a)
        {
            Actor b = null; double bd = double.PositiveInfinity;
            foreach (var x in xs) { double d = World.Dist3(x.pos, a.pos); if (d < bd) { bd = d; b = x; } }
            return (b, bd);
        }

        public class SusanooBrain : IController
        {
            public double nextSlash;
            public Actor target;
            readonly World w; readonly Actor a;
            public SusanooBrain(World w, Actor a) { this.w = w; this.a = a; }

            public void Think(double dt)
            {
                double t = w.time; var i = a.input;
                i.fire = i.alt = i.a1 = i.a2 = i.ult = i.melee = i.reload = i.swoop = false; i.jump = false; i.jumpHeld = false;
                i.mx = i.mz = 0;
                var owner = a.owner; var anchor = new V3(a.sv["ax"], a.sv["ay"], a.sv["az"]);
                var foes = w.actors.Where(x => x.alive && x.team != a.team && !x.IsSummon && !x.Has("phased", t)).ToList();
                void Face(V3 p) { i.yaw = Math.Atan2(p.x - a.pos.x, p.z - a.pos.z); i.pitch = 0; }
                // the first half: it stands planted, its free hand to the sky, and hurls the thunder at the nearest foe
                if (t < a.sv["bladeAt"])
                {
                    a.sv["phase"] = 0;
                    var (tgt, _) = Nearest(foes.Where(x => World.Dist3(x.pos, anchor) <= SUSANOO_R + x.Radius), a);
                    if (tgt != null) Face(tgt.pos); else i.yaw = owner.yaw;
                    target = tgt;
                    return;
                }
                a.sv["phase"] = 1;
                // the second half hunts the perimeter around the cast spot AND the ground around Raijin himself
                bool Hunted(Actor x) => World.Dist3(x.pos, anchor) <= SUSANOO_R + x.Radius || (owner.alive && World.Dist3(x.pos, owner.pos) <= SUSANOO_R + x.Radius);
                var inside = foes.Where(Hunted).ToList();
                var (best, bd) = Nearest(inside, a);
                target = best;
                if (best == null)
                {
                    // no one to cut: stride back to his side and stand ready, facing where he looks
                    var f = owner.Forward(); var spot = new V3(owner.pos.x - f.x * SUSANOO_BEHIND, owner.pos.y, owner.pos.z - f.z * SUSANOO_BEHIND);
                    if (M.Hypot(spot.x - a.pos.x, spot.z - a.pos.z) > SUSANOO_FOLLOW) { Face(spot); i.mz = 1; } else i.yaw = owner.yaw;
                    return;
                }
                Face(best.pos);
                // close to blade reach
                double reach = a.Radius + best.Radius + SLASH_R * 0.6;
                if (bd > reach) i.mz = 1;
                if (t >= nextSlash)
                {
                    nextSlash = t + SLASH_EVERY;
                    a.sv["swings"] = a.Sv("swings", 0) + 1;
                    a.anim.attackAt = t; a.anim.attackKind = "primary"; a.anim.attackSide = a.sv["swings"] % 2;
                    int n = 0;
                    foreach (var x in inside)
                    {
                        if (World.Dist3(x.pos, a.pos) > a.Radius + x.Radius + SLASH_R) continue;
                        // the giant's damage is Raijin's but never feeds his next ultimate
                        if (w.Damage(owner, x, SLASH_DMG, new DmgOpts { kind = "ability", ability = "susanoo", noLifesteal = true }) > 0) n++;
                        w.Fx("slash", x.Center, new FxOpts { color = SUSANOO_COLOR });
                    }
                    w.Fx("susanooslash", a.Center, new FxOpts { r = SLASH_R, color = SUSANOO_COLOR, actor = a, dur = 0.5 }); w.Sfx(n > 0 ? "katana" : "whiff", a.Center, a);
                    if (n > 0) w.Sfx("thunder", a.Center, a);
                    owner.stats["susanooHits"] = (owner.stats.TryGetValue("susanooHits", out var sh) ? sh : 0) + n;
                }
            }
        }

        /// <summary>Raijin's standing giant, if one is up</summary>
        public static Actor SusanooOf(World w, Actor owner) => w.actors.FirstOrDefault(x => x.IsSummon && x.owner == owner && x.def.id == "susanoo" && x.alive);

        /// <summary>the giant fades: at ten seconds, or when Raijin falls</summary>
        public static bool DismissSusanoo(World w, Actor owner)
        {
            var s = SusanooOf(w, owner);
            if (s == null) return false;
            s.alive = false; s.deathAt = w.time; s.respawnAt = 0; s.forced = null; s.sv["fellAt"] = w.time;
            owner.Clear("sovereign");
            w.Fx("susanoofade", s.Center, new FxOpts { color = SUSANOO_COLOR, actor = s, dur = 0.8 }); w.Sfx("spindown", s.Center, s);
            return true;
        }

        /// <summary>the thunder: one strike on every enemy in the perimeter with open sky above</summary>
        static int Thunder(World w, Actor owner, Actor s, bool first)
        {
            double t = w.time; var anchor = new V3(s.sv["ax"], s.sv["ay"], s.sv["az"]);
            int n = 0;
            foreach (var x in w.actors.ToList())
            {
                if (!x.alive || x.team == owner.team || x.IsSummon || x.Has("phased", t) || World.Dist3(x.pos, anchor) > SUSANOO_R + x.Radius) continue;
                if (!SkyOpen(w, x)) continue;
                double dealt = w.Damage(owner, x, STRIKE_DMG, new DmgOpts { kind = "ability", ability = "susanoo", noLifesteal = true });
                if (first && !x.Has("ccimmune", t) && !x.isBoss) x.Set("stun", t, STRIKE_STUN, null, owner);
                w.Fx("skybolt", x.pos, new FxOpts { color = SUSANOO_COLOR, actor = owner, target = x }); w.Sfx("thunderclap", x.Center, x);
                if (dealt > 0) n++;
            }
            owner.stats["thunderHits"] = (owner.stats.TryGetValue("thunderHits", out var th) ? th : 0) + n;
            s.sv["strikes"] = s.Sv("strikes", 0) + 1;
            // the giant calls each bolt (the renderer's hurl is timed off sv.strikeAt)
            s.sv["strikeAt"] = t;
            w.Fx("stormcall", s.Center, new FxOpts { color = SUSANOO_COLOR, actor = s });
            return n;
        }

        /// <summary>Storm Sovereign: the giant rises where Raijin stands</summary>
        public static Actor RaiseSusanoo(World w, Actor a)
        {
            double t = w.time;
            DismissSusanoo(w, a);                                // a second cast replaces the first giant
            var s = w.actors.FirstOrDefault(x => x.IsSummon && x.owner == a && x.def.id == "susanoo");
            if (s == null)
            {
                s = new Actor(SUSANOO_DEF, a.team) { owner = a, isRobot = true, noRespawn = true, alive = false };
                s.controller = new SusanooBrain(w, s);
                w.actors.Add(s);
            }
            s.team = a.team;
            // it rises at his right shoulder, a few steps ahead; a wall there: the left shoulder, then his back, then where he stands
            var f = a.Forward(); double rtx = -f.z, rtz = f.x, X = w.level.Size[0], Z = w.level.Size[1], eye = a.pos.y + 1.4;
            var spots = new[] { (SUSANOO_SIDE, SUSANOO_AHEAD), (-SUSANOO_SIDE, SUSANOO_AHEAD), (0.0, -SUSANOO_BEHIND), (0.0, 0.0) }.Select(q => new V3(
                Math.Max(-X + 2, Math.Min(X - 2, a.pos.x + rtx * q.Item1 + f.x * q.Item2)), a.pos.y, Math.Max(-Z + 2, Math.Min(Z - 2, a.pos.z + rtz * q.Item1 + f.z * q.Item2)))).ToList();
            V3 p = spots[3];
            foreach (var q in spots) if (w.level.LineOfSight(new V3(a.pos.x, eye, a.pos.z), new V3(q.x, eye, q.z))) { p = q; break; }
            double g = w.level.GroundAt(p.x, p.z, a.pos.y + 1.5);
            if (!double.IsNegativeInfinity(g) && Math.Abs(g - a.pos.y) < 2) p.y = g;
            s.pos = p; s.vel = V3.Zero;
            s.yaw = s.input.yaw = a.yaw; s.pitch = 0;
            s.hp = SUSANOO_DEF.hp; s.armor = 0; s.scale = 1;
            s.shields = new List<Shield>(); s.wounds = new List<Wound>(); s.st = new Dictionary<string, double>(); s.sv = new Dictionary<string, double>(); s.forced = null;
            s.alive = true; s.respawnAt = 0; s.deathAt = -99; s.lastDamagedAt = t;
            s.Set("phased", t, SUSANOO_THUNDER + SUSANOO_BLADE + 1);          // a hologram: nothing touches it, it touches nothing
            s.Set("ccimmune", t, SUSANOO_THUNDER + SUSANOO_BLADE + 1);
            s.sv["ax"] = a.pos.x; s.sv["ay"] = a.pos.y; s.sv["az"] = a.pos.z; s.sv["risenAt"] = t;
            s.sv["gx"] = p.x; s.sv["gz"] = p.z;                        // where it stands planted through the thunder
            // (the renderer's contract, shared with the Enra effigy: when it rose, when it is fully up, when it goes)
            s.sv["riseAt"] = t; s.sv["riseUntil"] = t + 0.6; s.sv["until"] = t + SUSANOO_THUNDER + SUSANOO_BLADE; s.sv["bladeAt"] = t + SUSANOO_THUNDER; s.sv["fadeAt"] = t + SUSANOO_THUNDER + SUSANOO_BLADE;
            s.sv["nextStrike"] = t + STRIKE_FIRST; s.sv["strikes"] = 0; s.sv["phase"] = 0;
            ((SusanooBrain)s.controller).nextSlash = t + SUSANOO_THUNDER + 0.4;
            a.Set("sovereign", t, SUSANOO_THUNDER + SUSANOO_BLADE);
            double cast = t; a.sv["susanooCast"] = cast;
            w.After(SUSANOO_THUNDER + SUSANOO_BLADE, () => { if (a.sv.TryGetValue("susanooCast", out var c) && c == cast) DismissSusanoo(w, a); });
            return s;
        }

        /// <summary>per tick: the thunder cadence, and the giant fading with its summoner</summary>
        public static void TickSusanoo(World w)
        {
            double t = w.time;
            foreach (var s in w.actors.ToList())
            {
                if (!s.alive || !s.IsSummon || s.def.id != "susanoo" || s.owner == null) continue;
                if (!s.owner.alive) { DismissSusanoo(w, s.owner); continue; }
                // the giant stays planted over its anchor through the thunder, whatever shoves come its way
                if (t < s.sv["bladeAt"]) { s.pos.x = s.sv["gx"]; s.pos.z = s.sv["gz"]; s.vel.x = s.vel.z = 0; }
                if (t < s.sv["bladeAt"] && t >= s.sv["nextStrike"] && s.sv["strikes"] < JsMath.Round(SUSANOO_THUNDER / STRIKE_EVERY))
                {
                    s.sv["nextStrike"] = t + STRIKE_EVERY;
                    Thunder(w, s.owner, s, s.sv["strikes"] == 0);
                }
            }
        }
    }
}
