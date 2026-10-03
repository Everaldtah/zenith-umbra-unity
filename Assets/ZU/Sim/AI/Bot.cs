// Hero AI: role behaviour (tank / dps / support), nav movement, lead aiming, and per-hero ability logic that deliberately
// plays the rival counters so matches (and the AI test lab) exercise them. Port of zenith-umbra src/ai/Bot.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public class Bot : IController
    {
        /// <summary>modulo, not a subtract loop: a runaway angle (1e20 / Infinity) would otherwise spin forever and freeze the sim</summary>
        static double Wrap(double a)
        {
            if (double.IsNaN(a) || double.IsInfinity(a)) return 0;
            double t = (a + Math.PI) % (2 * Math.PI);
            return (t < 0 ? t + 2 * Math.PI : t) - Math.PI;
        }
        static (double x, double z) Norm2(double x, double z) { double l = M.Hypot(x, z); if (l == 0) l = 1; return (x / l, z / l); }

        public Actor target;
        public Actor ally;          // heal / escort target
        public List<V3> path = new List<V3>();
        public V3? pathGoal;
        public double repathAt, thinkAt, abilityAt;
        public double strafe = 1, strafeUntil;
        double stuckX, stuckZ, stuckT; public int stuckCount;
        public double holdAlt, holdFire, flyUntil;
        string swoopExit = "";      // Mirei: how to leave the current swoop ('' | fwd | up)
        V3? aimAt;
        bool snap;
        public V3 goal;
        public string mode = "objective";    // fight | objective | retreat | support | patrol
        public readonly double react, aimErr;
        public readonly World w; public readonly Actor a; public readonly INav nav; public readonly double skill;

        public Bot(World w, Actor a, INav nav, double skill = 0.7)
        {
            this.w = w; this.a = a; this.nav = nav; this.skill = skill;
            react = 0.35 - skill * 0.25;
            aimErr = 0.09 * (1 - skill) + 0.012;
        }

        public void Think(double dt)
        {
            double t = w.time; var i = a.input;
            i.a1 = i.a2 = i.ult = i.jump = i.reload = i.swoop = false;
            if (a.isRobot) { Robot(dt); return; }
            if (t >= thinkAt) { thinkAt = t + 0.12 + Rng.Random() * 0.05; Decide(); }
            if (t >= abilityAt) { abilityAt = t + react + Rng.Random() * 0.2; Abilities_(); }
            MoveAlong(dt);
            Aim(dt);
            Shoot();
            // Gantetsu mid-rush: jump is the Shiko Stomp's leap and nothing else, judged every tick
            if (a.Has("tachiai", t)) i.jump = a.grounded && StompNow();
            // Mirei mid-swoop: slingshot onward or superjump out
            if (a.Has("swoop", t))
            {
                double prog = a.Sv("swoopProg", 0);
                if (swoopExit == "up" && prog > 0.7) i.descend = true;
                else if (swoopExit == "fwd" && prog > 0.6) i.jump = true;
            }
        }

        // ------------------------------------------------------------------ decisions
        void Decide()
        {
            double t = w.time;
            var foes = w.Enemies(a).Where(x => (w.Perceivable(a, x) && !x.isRobot) || (x.isRobot && x.def.id != "bot_dummy")).ToList();
            // (a phased body - Enra's effigy - can't be hit: no shots at it)
            var vis = foes.Where(x => World.Dist3(x.pos, a.pos) < 55 && w.Visible(a, x) && !x.Has("phased", t)).ToList();
            // target: close + low + visible, sticky
            double Score(Actor x) => World.Dist3(x.pos, a.pos) + x.Health / x.MaxHp * 12 - (x == target ? 8 : 0) - (x.def.id == a.def.rival ? 5 : 0) - (x.Has("marked", t) ? 4 : 0)
                + (x.IsSummon ? 10 : 0)                  // a puppet only when it is much closer than any hero
                - (a.def.id == "gantetsu" && x.Has("knockdown", t) ? 10 : 0);      // Gantetsu: whoever his stomp has put on the ground
            target = JsSort.SortBy(vis, (p, q) => Score(p) - Score(q)).FirstOrDefault();
            // the objective: the capture point, or (Mikoshi Rush) wherever the float is now
            var P = w.rules == "push" ? new[] { w.push.pos.x, w.push.pos.y, w.push.pos.z } : w.map.point;
            bool campaign = w.mode == "campaign";
            bool pointOpen = w.mode != "training" && !campaign && t > (w.rules == "push" ? w.push.unlockAt : w.point.unlockAt) - 4;
            double hurt = a.Health / a.MaxHp;
            string role = a.def.role;
            // low with no healer close: detour to the nearest health pack that's up
            bool medic = w.Allies(a, false).Any(x => x.def.role == "support" && World.Dist3(x.pos, a.pos) < 12);
            HealthPack pack = null;
            if (hurt < 0.45 && !medic)
                pack = JsSort.SortBy(w.packs.Where(p => p.readyAt <= t && World.Dist3(new V3(p.x, p.y, p.z), a.pos) < 28).ToList(),
                    (p, q) => World.Dist3(new V3(p.x, p.y, p.z), a.pos) - World.Dist3(new V3(q.x, q.y, q.z), a.pos)).FirstOrDefault();
            if (role == "support")
            {
                var allies = w.Allies(a, false).Where(x => !x.isRobot).ToList();
                ally = JsSort.SortBy(allies.Where(x => World.Dist3(x.pos, a.pos) < 30).ToList(), (p, q) => p.Health / p.MaxHp - q.Health / q.MaxHp).FirstOrDefault();
                if (ally != null && ally.Health / ally.MaxHp > 0.93) ally = allies.FirstOrDefault(x => x.def.role == "tank") ?? ally;
            }
            // goal selection
            if (pack != null && !(target != null && World.Dist3(target.pos, a.pos) < 4))
            {
                mode = "retreat";
                goal = new V3(pack.x, pack.y, pack.z);
            }
            else if (hurt < 0.3 && role != "tank" && target != null && World.Dist3(target.pos, a.pos) < 14)
            {
                mode = "retreat";
                var sup = w.Allies(a, false).FirstOrDefault(x => x.def.role == "support" && x != a);
                goal = sup != null ? sup.pos : new V3(a.spawn[0], 0, a.spawn[1]);
            }
            else if (role == "support" && ally != null && ally != a)
            {
                mode = "support";
                var al = ally.pos; var tg = target;
                var away = tg != null ? Norm2(al.x - tg.pos.x, al.z - tg.pos.z) : Norm2(a.spawn[0] - al.x, a.spawn[1] - al.z);
                goal = new V3(al.x + away.x * 5, al.y, al.z + away.z * 5);
            }
            else if (target != null && (!pointOpen || World.Dist3(target.pos, new V3(P[0], P[1], P[2])) < 22 || World.Dist3(target.pos, a.pos) < 12))
            {
                mode = "fight";
                var tg = target.pos; double d = World.Dist3(tg, a.pos);
                double want = PreferredRange();
                double k = d > 0.1 ? (d - want) / d : 0;
                goal = new V3(a.pos.x + (tg.x - a.pos.x) * k, tg.y, a.pos.z + (tg.z - a.pos.z) * k);
                if (t > strafeUntil) { strafe = Rng.Random() < 0.5 ? -1 : 1; strafeUntil = t + 0.6 + Rng.Random() * 1.2; }
            }
            else if (campaign)
            {
                // companions escort the human squad
                mode = "objective";
                var lead = w.actors.FirstOrDefault(x => x.alive && x.team == a.team && (x.isPlayer || !string.IsNullOrEmpty(x.netId)));
                var wp = w.director?.Waypoint();
                double ang = (a.id * 2.4) % (Math.PI * 2);
                if (lead == null && wp.HasValue) goal = new V3(wp.Value.x + Math.Cos(ang) * 3, wp.Value.y, wp.Value.z + Math.Sin(ang) * 3);
                else if (lead != null) goal = new V3(lead.pos.x - Math.Sin(lead.yaw) * 3 + Math.Cos(ang) * 3, lead.pos.y, lead.pos.z - Math.Cos(lead.yaw) * 3 + Math.Sin(ang) * 3);
            }
            else if (pointOpen)
            {
                mode = "objective";
                double ang = (a.id * 2.4) % (Math.PI * 2), r = w.rules == "push" ? 2.5 : 3;
                goal = new V3(P[0] + Math.Cos(ang) * r, P[1], P[2] + Math.Sin(ang) * r);
            }
            else
            {
                mode = "objective";
                double f = a.team == "zenith" ? 1 : -1;
                goal = new V3(P[0] - f * 14, P[1], P[2] + ((a.id % 5) - 2) * 3);
            }
            // goals must sit on walkable ground (never in the void next to an island)
            var gp = nav.NearestPoint(goal, 10);
            if (gp.HasValue) goal = gp.Value;
            // repath when the goal moved or on schedule
            if (!pathGoal.HasValue || World.Dist3(pathGoal.Value, goal) > 2.5 || t > repathAt)
            {
                repathAt = t + 1.2 + Rng.Random() * 0.6;
                pathGoal = goal;
                path = nav.Find(a.pos, goal) ?? new List<V3>();
            }
        }

        double PreferredRange()
        {
            var P = a.def.primary; string id = a.def.id;
            if (id == "tenkai") return 3.5;          // hammer reach is 5m: stand just inside it
            if (id == "gantetsu") return 8;          // chainguns: brawling range
            if (id == "tomoe") return 6;             // scattergun and axe: in their faces
            if (P.kind == "melee") return 1.5;
            if (id == "enra") return 4;
            if (id == "gorgoth") return 7;
            if (id == "yuzu") return 26;
            if (id == "kagemaru") return 11;
            if (id == "hibiki") return 10;          // inside his team's 12m aura
            return 15;
        }

        // ------------------------------------------------------------------ movement
        void MoveAlong(double dt)
        {
            double t = w.time; var i = a.input;
            while (path.Count > 0 && M.Hypot(path[0].x - a.pos.x, path[0].z - a.pos.z) < (path.Count > 1 ? 0.7 : 0.4)) path.RemoveAt(0);
            (double x, double z) dir = (0, 0);
            V3? wp = path.Count > 0 ? path[0] : (V3?)null;
            if (wp.HasValue) dir = Norm2(wp.Value.x - a.pos.x, wp.Value.z - a.pos.z);
            else if (M.Hypot(goal.x - a.pos.x, goal.z - a.pos.z) > 1.2 && (a.def.frame == "flyer" || a.def.frame == "drone")) dir = Norm2(goal.x - a.pos.x, goal.z - a.pos.z);
            // combat strafe
            if (mode == "fight" && target != null && World.Dist3(target.pos, a.pos) < 30)
            {
                double tx = target.pos.x - a.pos.x, tz = target.pos.z - a.pos.z, l = M.Hypot(tx, tz); if (l == 0) l = 1;
                double sx = -tz / l * strafe, sz = tx / l * strafe;
                double k = wp.HasValue ? 0.55 : 1;
                dir = Norm2(dir.x * (1 - k) + sx * k, dir.z * (1 - k) + sz * k);
                if (Rng.Random() < 0.004 && a.grounded) i.jump = true;
            }
            // wall avoidance: don't strafe off a ledge into the void
            double aheadX = a.pos.x + dir.x * 1.2, aheadZ = a.pos.z + dir.z * 1.2;
            if (a.def.frame != "drone" && !a.flying)
            {
                double g = w.level.GroundAt(aheadX, aheadZ, a.pos.y + 0.3);
                if (double.IsNegativeInfinity(g) || g < a.pos.y - 5) { dir = wp.HasValue ? Norm2(wp.Value.x - a.pos.x, wp.Value.z - a.pos.z) : (0, 0); strafe *= -1; }
            }
            // brake if momentum is carrying us over an edge
            if (a.grounded && a.def.frame != "drone")
            {
                double sp = M.Hypot(a.vel.x, a.vel.z);
                if (sp > 1)
                {
                    double px = a.pos.x + a.vel.x / sp * (a.Radius + 0.5), pz = a.pos.z + a.vel.z / sp * (a.Radius + 0.5);
                    double g = w.level.GroundAt(px, pz, a.pos.y + 0.3);
                    if (double.IsNegativeInfinity(g) || g < a.pos.y - 5)
                    {
                        var c = nav.NearestPoint(a.pos, 4) ?? a.pos;
                        dir = Norm2(c.x - a.pos.x - a.vel.x * 0.3, c.z - a.pos.z - a.vel.z * 0.3);
                    }
                }
            }
            // world dir -> local input
            double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
            i.mz = dir.x * fx + dir.z * fz;
            i.mx = dir.x * rx + dir.z * rz;
            // step-ups the nav allows but that need a hop (pads are walked onto)
            if (wp.HasValue && wp.Value.y - a.pos.y > 0.6 && a.grounded && M.Hypot(wp.Value.x - a.pos.x, wp.Value.z - a.pos.z) < 2.5) i.jump = true;
            // flyers: take to the air in fights and to reach high goals
            if (a.def.frame == "flyer")
            {
                double hGoal = (pathGoal?.y ?? 0) - a.pos.y;
                bool wantAir = (mode == "fight" || mode == "support") && a.flight > 30 && !a.Has("grounded", t);
                if (wantAir && t > flyUntil) flyUntil = t + 1.5 + Rng.Random() * 1.5;
                double gnd = w.level.GroundAt(a.pos.x, a.pos.z, a.pos.y);
                double alt = a.pos.y - (double.IsNegativeInfinity(gnd) ? a.pos.y - 5 : gnd);
                i.jumpHeld = (t < flyUntil && alt < 4.5 && a.flight > 10) || (hGoal > 1.2 && a.flight > 10) || (double.IsNegativeInfinity(gnd) && a.flight > 5);
                if (i.jumpHeld && a.grounded) i.jump = true;
                i.descend = false;
            }
            else if ((a.def.jets ?? 0) != 0)
            {
                // thruster mechs: burn up to meet an airborne target in hammer range (drones hovering out of reach)
                var tg = target; double up = tg != null ? tg.pos.y - a.pos.y : 0, near = tg != null ? M.Hypot(tg.pos.x - a.pos.x, tg.pos.z - a.pos.z) : 99;
                bool want = tg != null && tg.alive && up > 2.2 && near < 9 && a.flight > 12 && (a.flying || a.flight > 55);
                i.jumpHeld = want ? up > 0.8 : i.jump;
                if (want && a.grounded) i.jump = true;
            }
            else i.jumpHeld = i.jump || t < a.Sv("botGrind", 0);
            // unstick
            if (t - stuckT > 1.2)
            {
                double moved = M.Hypot(a.pos.x - stuckX, a.pos.z - stuckZ);
                bool wanted = M.Hypot(i.mx, i.mz) > 0.3 && !a.Has("root", t) && !a.Has("stun", t) && !a.barrier.up;
                if (wanted && moved < 0.35)
                {
                    stuckCount++; i.jump = true; strafe *= -1; repathAt = 0;
                    path = nav.Find(a.pos, goal) ?? new List<V3>();
                }
                stuckX = a.pos.x; stuckZ = a.pos.z; stuckT = t;
            }
        }

        // ------------------------------------------------------------------ aiming / firing
        V3? AimPoint()
        {
            if (aimAt.HasValue) return aimAt;
            var S = a.def.secondary;
            if (mode == "support" && ally != null && ally != a && ally.Health < ally.MaxHp * 0.97 && !S.IsAbility && S.heal) return ally.Center;
            var tg = target;
            if (tg == null)
            {
                if (path.Count > 0) { var p = path[Math.Min(2, path.Count - 1)]; return new V3(p.x, a.Eye.y, p.z); }
                return null;
            }
            var W = a.def.primary;
            var c = tg.Center;
            double sp = W.speed ?? (W.kind == "charge" ? 100 : 0);
            double tt = sp != 0 ? World.Dist3(a.Eye, c) / sp : 0;
            double e = aimErr * World.Dist3(a.Eye, c);
            double ex = (Rng.Random() - 0.5) * e, ey = (Rng.Random() - 0.5) * e * 0.6, ez = (Rng.Random() - 0.5) * e;
            return new V3(c.x + tg.vel.x * tt + ex, c.y + tg.vel.y * tt * 0.5 + ey, c.z + tg.vel.z * tt + ez);
        }

        void Aim(double dt)
        {
            var i = a.input;
            var pn = AimPoint();
            if (!pn.HasValue) return;
            var p = pn.Value; var e = a.Eye;
            double yaw = Math.Atan2(p.x - e.x, p.z - e.z);
            double pitch = Math.Atan2(p.y - e.y, M.Hypot(p.x - e.x, p.z - e.z));
            if (snap) { i.yaw = yaw; i.pitch = pitch; snap = false; return; }
            double rate = (4 + skill * 8) * dt;
            double dy = Wrap(yaw - i.yaw), dp = pitch - i.pitch;
            i.yaw = Wrap(i.yaw + Math.Max(-rate, Math.Min(rate, dy * Math.Min(1, dt * 12))));
            i.pitch += Math.Max(-rate, Math.Min(rate, dp * Math.Min(1, dt * 12)));
        }

        bool OnTarget(double deg = 6)
        {
            var tg = target;
            if (tg == null) return false;
            V3 e = a.Eye, c = tg.Center;
            double yaw = Math.Atan2(c.x - e.x, c.z - e.z), pitch = Math.Atan2(c.y - e.y, M.Hypot(c.x - e.x, c.z - e.z));
            double tol = deg * Math.PI / 180 + Math.Atan2(tg.Radius, World.Dist3(e, c));
            return Math.Abs(Wrap(yaw - a.input.yaw)) < tol && Math.Abs(pitch - a.input.pitch) < tol + 0.1;
        }

        void Shoot()
        {
            double t = w.time; var i = a.input; var tg = target;
            var P = a.def.primary; var S = a.def.secondary;
            i.fire = false; i.melee = false;
            i.alt = t < holdAlt;
            // supports heal first
            if (!S.IsAbility && S.heal && ally != null && ally != a && ally.Health < ally.MaxHp * 0.97 && World.Dist3(ally.pos, a.pos) < S.range + 2 && w.Visible(a, ally))
            {
                i.alt = true;
                if (S.kind == "beam") return;
                if (!AimAtAlly()) i.alt = false;
                return;
            }
            if (tg == null || !w.Visible(a, tg)) { if ((P.ammo ?? 0) != 0 && a.ammo < P.ammo.Value * 0.4 && tg == null) i.reload = true; if (P.kind == "charge") i.fire = false; return; }
            double d = World.Dist3(tg.pos, a.pos);
            if (P.kind == "charge")
            {
                // hold to charge, release when full and on target
                if (a.charging && a.charge >= 0.95 && OnTarget(2.5)) i.fire = false;
                else i.fire = d < P.range;
                return;
            }
            bool inRange = d < P.range * (P.kind == "hitscan" ? 0.8 : 1) + tg.Radius;
            if (inRange && OnTarget(P.kind == "melee" ? 35 : P.kind == "beam" ? 14 : 5)) i.fire = true;
            // quick melee when an enemy is in arm's reach
            if (d < 1.2 + a.Radius * 1.3 + tg.Radius && t >= a.nextMelee && OnTarget(30) && (P.kind != "melee" || Rng.Random() < 0.15)) i.melee = true;
            // twin chainguns: both triggers down together once on target - emptied into anyone the stomp knocked flat
            if (a.def.dualGuns && inRange && OnTarget(7)) i.alt = true;
            if (a.def.dualGuns && inRange && tg.Has("knockdown", t) && OnTarget(12)) i.fire = i.alt = true;
            // melee secondaries / ranged secondaries
            if (!S.IsAbility && !S.heal && !a.def.dualGuns)
            {
                if (S.kind == "melee" && d < S.range + tg.Radius && OnTarget(35)) i.alt = true;
                if (S.kind == "projectile" && d > 4 && d < S.range && OnTarget(4) && Rng.Random() < 0.25) i.alt = true;
            }
            if (a.def.id == "raijin" && d > P.range + 1) i.fire = false;
        }

        bool AimAtAlly()
        {
            V3 e = a.Eye, c = ally.Center;
            double yaw = Math.Atan2(c.x - e.x, c.z - e.z);
            return Math.Abs(Wrap(yaw - a.input.yaw)) < 0.12;
        }

        /// <summary>Gantetsu's Shiko Stomp: would a slam landing here catch a crowd, or anyone in its heart?</summary>
        bool StompNow()
        {
            var eye = new V3(a.pos.x, a.pos.y + 0.6, a.pos.z);
            int crowd = 0;
            foreach (var x in w.Enemies(a))
            {
                if (x.isRobot && x.def.id == "bot_dummy") continue;
                double d = M.Hypot(x.pos.x - a.pos.x, x.pos.z - a.pos.z) - x.Radius;
                if (d > Abilities.STOMP_R - 1 || Math.Abs(x.pos.y - a.pos.y) > Abilities.STOMP_H || !w.level.LineOfSight(eye, x.Center)) continue;
                if (d < Abilities.STOMP_CORE || ++crowd >= 2) return true;
            }
            return false;
        }

        /// <summary>look at a point and fire an ability on the next tick (slot: a1 | a2 | ult | alt | swoop)</summary>
        void CastAt(string slot, V3? p = null)
        {
            var i = a.input;
            if (p.HasValue) { aimAt = p; snap = true; Aim(0); aimAt = null; }
            if (slot == "alt") { holdAlt = w.time + 0.1; i.alt = true; }
            else if (slot == "a1") i.a1 = true; else if (slot == "a2") i.a2 = true; else if (slot == "ult") i.ult = true; else if (slot == "swoop") i.swoop = true;
        }

        // ------------------------------------------------------------------ per-hero abilities (incl. rival counters)
        void Abilities_()
        {
            double t = w.time; var tg = target;
            if (a.Has("silence", t) || a.Has("stun", t)) return;
            var foes = w.Enemies(a).Where(x => !x.isRobot || x.def.id != "bot_dummy").ToList();
            var allies = w.Allies(a);
            double d = tg != null ? World.Dist3(tg.pos, a.pos) : 99;
            List<Actor> Near(V3 p, double r, List<Actor> list) => list.Where(x => World.Dist3(x.pos, p) < r).ToList();
            bool ultReady = a.ult >= a.def.ult.charge;
            // (bound by the Grand Dohyo's chains, castAbility refuses the dashes and teleports: don't spend the think on them)
            bool Rdy(string id) => a.Ready(id, t) && !(Abilities.LEASHED.Contains(id) && a.Has("chained", t));
            var rival = foes.FirstOrDefault(x => x.def.id == a.def.rival);
            var lowAllies = allies.Where(x => x.Health / x.MaxHp < 0.5 && World.Dist3(x.pos, a.pos) < 20).ToList();
            bool Vis(Actor x) => w.Visible(a, x);
            bool SolidLine(Actor to)
            {
                bool solid = true;
                for (int k = 1; k <= 5 && solid; k++) { double u = k / 5.0; solid = w.level.GroundAt(a.pos.x + (to.pos.x - a.pos.x) * u, a.pos.z + (to.pos.z - a.pos.z) * u, a.pos.y + 1) > a.pos.y - 1.5; }
                return solid;
            }
            switch (a.def.id)
            {
                case "tenkai":
                    {
                        // COUNTER: meet Gorgoth's Abyss Charge head-on with a Dawn Charge
                        if (rival != null && rival.forced?.kind == "abysscharge" && World.Dist3(rival.pos, a.pos) < 20 && Vis(rival) && Rdy("dawncharge")) { CastAt("a1", rival.Center); break; }
                        // Solar Shatter: grounded foes bunched in the cone toward the target
                        if (tg != null && Rdy("shatter"))
                        {
                            double ang = Math.Atan2(tg.pos.x - a.pos.x, tg.pos.z - a.pos.z);
                            var cone = foes.Where(x => Vis(x) && !x.flying && Math.Abs(x.pos.y - a.pos.y) < 1.5 && World.Dist3(x.pos, a.pos) < 14
                                && Math.Abs(Wrap(Math.Atan2(x.pos.x - a.pos.x, x.pos.z - a.pos.z) - ang)) < 0.4).ToList();
                            if (cone.Count >= 2 || (cone.Count == 1 && d < 7 && Rng.Random() < 0.25)) { CastAt("a2", tg.pos); break; }
                        }
                        // Dawn Charge: a visible target on flat, solid ground in charge range (never charge over a drop)
                        if (tg != null && d > 7 && d < 20 && Vis(tg) && Rdy("dawncharge") && Math.Abs(tg.pos.y - a.pos.y) < 1.2 && Rng.Random() < 0.3)
                        {
                            if (SolidLine(tg)) { CastAt("a1", tg.Center); break; }
                        }
                        // Dawn Colossus: wake up the giant when a fight is on
                        if (ultReady && tg != null && d < 12 && (Near(a.pos, 14, foes).Count >= 2 || a.Health / a.MaxHp < 0.6)) { CastAt("ult", tg.pos); break; }
                        // Solar Bulwark: raise when under fire
                        if (t - a.lastDamagedAt < 0.8 && a.barrier.hp > 350 && tg != null && d > 5) holdAlt = t + 1.2 + Rng.Random();
                        break;
                    }
                case "haruto":
                    // pilot on foot: call the mech back the moment the gauge is full, roll out of trouble meanwhile
                    if (ultReady) { CastAt("ult"); break; }
                    if (t - a.lastDamagedAt < 0.4 && Rdy("pilotroll") && Rng.Random() < 0.3) { CastAt("a1"); break; }
                    break;
                case "mirei":
                    {
                        var noct = foes.FirstOrDefault(x => x.def.id == "nocturne");
                        bool ccd = allies.Any(x => new[] { "silence", "grounded", "root", "tethered" }.Any(s => x.Has(s, t)) && World.Dist3(x.pos, a.pos) < 15);
                        // COUNTER: pre-empt Silence Aria when Nocturne closes in on the team
                        if (Rdy("constellation") && (ccd || (noct != null && Near(noct.pos, 15, allies).Count >= 2) || lowAllies.Count >= 2)) { CastAt("a1"); break; }
                        var dying = allies.FirstOrDefault(x => x.Health / x.MaxHp < 0.4 && t - x.lastDamagedAt < 1 && World.Dist3(x.pos, a.pos) < 28 && Vis(x));
                        if (dying != null && Rdy("wish")) { CastAt("a2", dying.Center); break; }
                        if (ultReady && a.def.ult.id == "rebirth")
                        {
                            // Stellar Rebirth: two souls in reach with time left on their respawn (one, if she's the last one standing)
                            var souls = Rebirth.SoulsOf(w, a).Where(x => x.respawnAt - t > 1.2).ToList();
                            if (souls.Count >= 2 || (souls.Count == 1 && !allies.Any(x => x.alive && x != a))) { CastAt("ult"); break; }
                        }
                        else if (ultReady && lowAllies.Count >= 2) { CastAt("ult"); break; }
                        // Starwing Swoop: escape a diver toward the safest teammate, or close on a hurt ally out of beam range
                        if (Rdy("swoop") && !a.Has("swoop", t) && !a.Has("grounded", t))
                        {
                            bool threatened = t - a.lastDamagedAt < 0.6 && a.Health / a.MaxHp < 0.65;
                            double beam = !a.def.secondary.IsAbility ? a.def.secondary.range : 18;
                            var cand = allies.Where(x => x != a && !x.isRobot && Vis(x) && World.Dist3(x.pos, a.pos) < 29 && (threatened ? World.Dist3(x.pos, a.pos) > 6 && Near(x.pos, 8, foes).Count == 0 : World.Dist3(x.pos, a.pos) > beam - 2 && x.Health / x.MaxHp < 0.6)).ToList();
                            JsSort.SortBy(cand, (p, q) => threatened ? World.Dist3(q.pos, a.pos) - World.Dist3(p.pos, a.pos) : p.Health / p.MaxHp - q.Health / q.MaxHp);
                            var to = cand.FirstOrDefault();
                            if (to != null)
                            {
                                double r = Rng.Random();
                                swoopExit = r < 0.35 ? "up" : r < 0.6 ? "fwd" : "";
                                CastAt("swoop", to.Center); break;
                            }
                        }
                        break;
                    }
                case "kaien":
                    {
                        var kage = foes.FirstOrDefault(x => x.def.id == "kagemaru");
                        // COUNTER: seal the Shade Fang (the AI knows roughly where he lurks even when veiled)
                        if (kage != null && Rdy("seal") && World.Dist3(kage.pos, a.pos) < 18 && (kage.Has("stealth", t) || World.Dist3(kage.pos, a.pos) < 10)) { CastAt("a2", kage.pos); break; }
                        if (tg != null && Rdy("seal") && Near(tg.pos, 7, foes).Count >= 2 && d < 20) { CastAt("a2", tg.pos); break; }
                        // Divine Seal Storm: a fight around him, or him being dived
                        if (ultReady && (Near(a.pos, 15, foes).Count >= 2 || (tg != null && d < 9 && t - a.lastDamagedAt < 0.8 && a.Health / a.MaxHp < 0.7))) { CastAt("ult"); break; }
                        if (Rdy("spiritstep") && a.Health / a.MaxHp < 0.4 && tg != null && d < 8) CastAt("a1");
                        break;
                    }
                case "raijin":
                    {
                        var enra = foes.FirstOrDefault(x => x.def.id == "enra");
                        bool chainIncoming = w.projs.Any(p => p.special == "chain" && p.team != a.team && World.Dist3(p.pos, a.pos) < 12);
                        // COUNTER: parry the chain / the oni's fists
                        if (Rdy("parry") && (chainIncoming || (enra != null && World.Dist3(enra.pos, a.pos) < 4.5 && Vis(enra)) || w.projs.Count(p => p.team != a.team && World.Dist3(p.pos, a.pos) < 6) >= 3)) { CastAt("a2"); break; }
                        bool safe = false;
                        if (tg != null)
                        {
                            double lx = a.pos.x + (tg.pos.x - a.pos.x) / d * 12, lz = a.pos.z + (tg.pos.z - a.pos.z) / d * 12;
                            safe = w.level.GroundAt(lx, lz, a.pos.y + 1) > a.pos.y - 3 && w.level.GroundAt((a.pos.x + lx) / 2, (a.pos.z + lz) / 2, a.pos.y + 1) > a.pos.y - 3;
                        }
                        if (tg != null && safe && Rdy("flashstep") && d > 5 && d < 13 && Vis(tg) && !a.Has("sealed", t)) { CastAt("a1", tg.Center); a.input.mz = 1; a.input.mx = 0; break; }
                        if (a.def.ult.id == "susanoo")
                        {
                            // Storm Sovereign: three enemies under open sky within its reach (two if one is a tank), not from the brink
                            var under = Near(a.pos, 10, foes).Where(x => Susanoo.SkyOpen(w, x)).ToList();
                            if (ultReady && (under.Count >= 3 || (under.Count >= 2 && under.Any(x => x.def.role == "tank"))) && a.Health / a.MaxHp > 0.3) CastAt("ult");
                        }
                        else if (ultReady && tg != null && d < 8 && a.Health / a.MaxHp > 0.45) CastAt("ult");
                        break;
                    }
                case "hayate":
                    {
                        var seiran = foes.FirstOrDefault(x => x.def.id == "seiran");
                        // COUNTER: turn his brother's arrows (and any volley) back
                        var volley = w.projs.Where(p => p.team != a.team && World.Dist3(p.pos, a.pos) < 7).ToList();
                        if (Rdy("mirrorwater") && ((seiran != null && Vis(seiran) && World.Dist3(seiran.pos, a.pos) < 40 && t - seiran.anim.attackAt < 0.3) || volley.Count >= 3))
                        { CastAt("a2", seiran != null && Vis(seiran) ? seiran.Center : volley.Count > 0 ? volley[0].pos : (V3?)null); break; }
                        bool safe = false;
                        if (tg != null) { double lx = a.pos.x + (tg.pos.x - a.pos.x) / d * 15, lz = a.pos.z + (tg.pos.z - a.pos.z) / d * 15; safe = w.level.GroundAt(lx, lz, a.pos.y + 1) > a.pos.y - 3; }
                        if (tg != null && safe && Rdy("currentdash") && d > 4 && d < 15 && Vis(tg) && (tg.Health / tg.MaxHp < 0.5 || Rng.Random() < 0.02)) { CastAt("a1", tg.Center); a.input.mz = 1; a.input.mx = 0; break; }
                        if (ultReady && tg != null && d < 14 && Near(tg.pos, 10, foes).Count >= 2) CastAt("ult");
                        break;
                    }
                case "seiran":
                    {
                        var hayate = foes.FirstOrDefault(x => x.def.id == "hayate");
                        if (hayate != null && Rdy("echoarrow") && World.Dist3(hayate.pos, a.pos) < 30 && !Vis(hayate)) { CastAt("a2", hayate.Center); break; }
                        if (Rdy("riverstep") && tg != null && d < 6) { CastAt("a1"); break; }
                        if (ultReady && tg != null && Near(tg.pos, 6, foes).Count >= 2) { CastAt("ult", tg.Center); break; }
                        break;
                    }
                case "yuzu":
                    {
                        var strung = allies.FirstOrDefault(x => x.Has("tethered", t) || x.Has("antiheal", t));
                        // COUNTER: sever Hex's strings / hexes on allies
                        if (strung != null && Rdy("reveal") && World.Dist3(strung.pos, a.pos) < 45) { CastAt("a2", strung.Center); break; }
                        var kage = foes.FirstOrDefault(x => x.def.id == "kagemaru" && x.Has("stealth", t) && allies.Any(al => World.Dist3(al.pos, x.pos) < 10));
                        if (kage != null && Rdy("reveal")) { CastAt("a2", kage.Center); break; }
                        if (Rdy("sunhop") && tg != null && d < 7) { CastAt("a1"); break; }
                        if (ultReady && tg != null && Near(tg.pos, 15, foes).Count >= 2) { CastAt("ult", tg.pos); break; }     // (the Unity swarm reaches 30 m: see Hundredsuns)
                        break;
                    }
                case "gorgoth":
                    {
                        var ten = foes.FirstOrDefault(x => x.def.id == "tenkai");
                        // COUNTER: Null Lance straight into the Solar Bulwark
                        if (ten != null && ten.barrier.up && World.Dist3(ten.pos, a.pos) < 9 && Rdy("nulllance")) { CastAt("a2", ten.Center); break; }
                        var wished = foes.FirstOrDefault(x => x.shields.Any(s => s.kind == "wish") && World.Dist3(x.pos, a.pos) < 8);
                        if (wished != null && Rdy("nulllance")) { CastAt("a2", wished.Center); break; }
                        if (tg != null && d < 8 && Rdy("nulllance") && Rng.Random() < 0.5) { CastAt("a2", tg.Center); break; }
                        if (tg != null && d > 5 && d < 14 && Rdy("abysscharge") && Vis(tg) && Math.Abs(tg.pos.y - a.pos.y) < 1.5) { CastAt("a1", tg.Center); break; }
                        if (Rdy("plating") && t - a.lastDamagedAt < 0.5 && a.Health / a.MaxHp < 0.7) { CastAt("alt"); break; }
                        if (ultReady && tg != null && d < 16 && Near(tg.pos, 9, foes).Count >= 2) CastAt("ult", tg.Center);
                        break;
                    }
                case "nocturne":
                    {
                        var mirei = foes.FirstOrDefault(x => x.def.id == "mirei" && x.flying && World.Dist3(x.pos, a.pos) < 12 && Vis(x));
                        var rai = foes.FirstOrDefault(x => x.def.id == "raijin" && World.Dist3(x.pos, a.pos) < 11 && Vis(x));
                        // COUNTER: ground the Starweaver
                        if (Rdy("silence") && (mirei != null || rai != null)) { CastAt("a1", (mirei ?? rai).Center); break; }
                        if (Rdy("silence") && tg != null && d < 10 && Near(tg.pos, 5, foes).Count >= 2) { CastAt("a1", tg.Center); break; }
                        var fighter = allies.FirstOrDefault(x => x != a && x.def.role != "support" && target != null && World.Dist3(x.pos, target.pos) < 10 && World.Dist3(x.pos, a.pos) < 25 && Vis(x));
                        if (fighter != null && Rdy("bloodpact")) { CastAt("a2", fighter.Center); break; }
                        if (ultReady && lowAllies.Count >= 2) CastAt("ult");
                        break;
                    }
                case "hex":
                    {
                        var heals = foes.Where(x => x.def.role == "support").ToList();
                        var healed = foes.FirstOrDefault(x => heals.Any(h => h.beamTarget == x || (h.def.id == "kaien" && World.Dist3(h.pos, x.pos) < 20)) && World.Dist3(x.pos, a.pos) < 22 && Vis(x));
                        // COUNTER: curse whoever the Zenith healers are keeping alive
                        if (Rdy("grievous") && healed != null && healed.Health / healed.MaxHp < 0.8) { CastAt("a2", healed.pos); break; }
                        var yuzu = foes.FirstOrDefault(x => x.def.id == "yuzu" && World.Dist3(x.pos, a.pos) < 19 && Vis(x));
                        if (Rdy("marionette") && (yuzu != null || (tg != null && d < 19 && Vis(tg)))) { CastAt("a1", (yuzu ?? tg).Center); break; }
                        // Grand Puppet Theater: raise the army when a fight is on
                        var heroes = foes.Where(x => !x.IsSummon).ToList();
                        if (ultReady && (Near(a.pos, 18, heroes).Count >= 2 || (tg != null && !tg.IsSummon && d < 9 && a.Health / a.MaxHp < 0.7))) CastAt("ult");
                        break;
                    }
                case "kagemaru":
                    {
                        // COUNTER: slice Kaien's seals apart
                        var seal = w.zones.FirstOrDefault(z => z.team != a.team && (z.kind == "seal" || z.kind == "sanctuary") && M.Hypot(z.x - a.pos.x, z.z - a.pos.z) < z.r + 9);
                        if (seal != null)
                        {
                            goal = new V3(seal.x, seal.y, seal.z); path = nav.Find(a.pos, goal) ?? new List<V3>();
                            if (M.Hypot(seal.x - a.pos.x, seal.z - a.pos.z) < seal.r + 3.5) { CastAt("alt", new V3(seal.x, seal.y + 1, seal.z)); break; }
                        }
                        if (tg != null && d > 16 && Rdy("veil") && !a.Has("stealth", t)) { CastAt("a2"); break; }
                        if (tg != null && d > 5 && d < 15 && Rdy("shadowstep") && Vis(tg)) { CastAt("a1", tg.pos); break; }
                        if (ultReady && (Near(a.pos, 14, foes).Count >= 2 || (tg != null && tg.hp < 130 && d < 14))) CastAt("ult");
                        break;
                    }
                case "hibiki":
                    {
                        // the track: heal when anyone near is hurt, tempo when the team is healthy and on the move
                        var inAura = allies.Where(x => World.Dist3(x.pos, a.pos) < 12).ToList();
                        var hurtA = inAura.Where(x => x.Health / x.MaxHp < 0.7).ToList();
                        bool wantHeal = hurtA.Count > 0 || a.Health / a.MaxHp < 0.6;
                        double track = a.Sv("track", 0);
                        if (Rdy("crossmix") && (wantHeal ? track == 1 : track != 1 && (tg == null || d > 18) && inAura.Count >= 2)) { CastAt("a1"); break; }
                        // Max Volume: a burst of healing when several are low, or a sprint when the whole team is pushing in
                        if (Rdy("maxvolume") && ((track == 0 && hurtA.Count(x => x.Health / x.MaxHp < 0.5) >= 2) || (track == 1 && tg != null && d < 16 && inAura.Count >= 3))) { CastAt("a2"); break; }
                        // Scratch Wave: knock divers off him and his healers, or off a ledge
                        var diver = foes.FirstOrDefault(x => World.Dist3(x.pos, a.pos) < 5.5 && Vis(x));
                        if (Rdy("scratch") && diver != null) { CastAt("alt", diver.Center); break; }
                        // Bass Drop: the team is taking a beating together, or an enemy ultimate just went up close by
                        bool enemyUlt = foes.Any(x => t - x.anim.castAt < 1.5 && x.anim.castId == x.def.ult.id && World.Dist3(x.pos, a.pos) < 25);
                        if (ultReady && (Near(a.pos, 25, allies).Count(x => x.Health / x.MaxHp < 0.55) >= 2 || (enemyUlt && Near(a.pos, 25, allies).Count >= 2))) { CastAt("ult"); break; }
                        // Mag-Grind: moving between fights with a wall alongside, jump on it and ride
                        if (tg == null && a.grounded && M.Hypot(a.vel.x, a.vel.z) > 4 && t > a.Sv("botGrind", 0) + 3 && Rng.Random() < 0.05)
                        {
                            double hs = M.Hypot(a.vel.x, a.vel.z), vx = a.vel.x / hs, vz = a.vel.z / hs;
                            bool wall = new[] { (-vz, vx), (vz, -vx) }.Any(q => { var h = w.level.Ray(new V3(a.pos.x, a.pos.y + 1, a.pos.z), new V3(q.Item1, 0, q.Item2), 1.6); return h.HasValue && Math.Abs(h.Value.ny) < 0.3; });
                            if (wall) { a.input.jump = true; a.sv["botGrind"] = t + 1.2 + Rng.Random() * 1.5; }
                        }
                        break;
                    }
                case "tomoe":
                    {
                        double st = a.Sv("fang", 0), since = t - a.Sv("fangAt", t);
                        // the Fang is out: yank its victim in once the wound has bitten (at once for a flyer), call it off a wall
                        if (st == 3)
                        {
                            var stuck = w.actors.FirstOrDefault(x => x.id == a.Sv("fangTgt", 0));
                            if (stuck != null && (stuck.flying || (since > 0.5 && World.Dist3(stuck.pos, a.pos) > 5) || since > 2.5)) { CastAt("alt"); break; }
                        }
                        else if (st == 2 && since > 0.8) { CastAt("alt"); break; }
                        // Crescent Warpath: fly through a crowd, or run down a wounded target
                        if (ultReady && tg != null && Vis(tg) && d > 3 && d < 16 && Math.Abs(tg.pos.y - a.pos.y) < 2.5
                            && (Near(tg.pos, 5, foes).Count >= 2 || tg.Health / tg.MaxHp < 0.4 || (Near(tg.pos, 14, allies).Count >= 2 && Near(tg.pos, 6, foes).Count >= 1 && d > 8))) { CastAt("ult", tg.Center); break; }
                        // Horagai War Call: under fire, or the team diving in together
                        if (Rdy("warcall") && ((t - a.lastDamagedAt < 0.6 && a.Health / a.MaxHp < 0.7) || (tg != null && d < 12 && Near(a.pos, 15, allies).Count >= 3))) { CastAt("a1"); break; }
                        // Crescent Reaping: everyone inside axe reach
                        if (Rdy("reaping") && tg != null && d < 5 && Vis(tg)) { CastAt("a2", tg.Center); break; }
                        // Crescent Fang: at range, and the Diva out of the sky first
                        var noc = foes.FirstOrDefault(x => x.def.id == "nocturne" && x.flying && World.Dist3(x.pos, a.pos) < 25 && Vis(x));
                        if (st == 0 && Rdy("crescent") && (noc != null || (tg != null && d > 6 && d < 24 && Vis(tg)))) { CastAt("alt", (noc ?? tg).Center); break; }
                        break;
                    }
                case "gantetsu":
                    {
                        var ten = foes.FirstOrDefault(x => x.def.id == "tenkai");
                        bool rushing = a.Has("tachiai", t);
                        // in the rush or in the air over the slam: nothing else (SHIFT again would end the rush with no leap)
                        if (rushing || a.Has("stompair", t)) break;
                        // COUNTER: rush straight through the Solar Bulwark, or meet a Dawn Charge head-on
                        if (ten != null && Vis(ten) && Rdy("tachiai") && ((ten.barrier.up && World.Dist3(ten.pos, a.pos) < 13) || (ten.forced?.kind == "dawncharge" && World.Dist3(ten.pos, a.pos) < 18))) { CastAt("a1", ten.Center); break; }
                        // Grand Dohyo: chain a crowd inside the ring - or a duel he's winning
                        var ring = foes.Where(x => M.Hypot(x.pos.x - a.pos.x, x.pos.z - a.pos.z) < 8 && Math.Abs(x.pos.y - a.pos.y) < 6).ToList();
                        if (ultReady && (ring.Count >= 2 || (tg != null && d < 7 && tg.Health / tg.MaxHp < 0.5 && a.Health / a.MaxHp > 0.5))) { CastAt("ult", tg?.Center); break; }
                        // Taiko Heartbeat: under fire, the team brawling around him, or the guns on someone his stomp has laid out
                        bool downed = tg != null && tg.Has("knockdown", t) && d < 12 && Vis(tg);
                        if (Rdy("taiko") && ((t - a.lastDamagedAt < 0.6 && a.Health / a.MaxHp < 0.8) || (downed && a.Health / a.MaxHp < 0.9)
                            || Near(a.pos, 12, allies).Count(x => x != a && t - x.lastDamagedAt < 1) >= 2)) { CastAt("a2"); break; }
                        // a target on the ground is there to be shot: no rushing past it
                        if (downed) break;
                        // Tachiai Rush: close the gap on a visible target across solid ground
                        if (tg != null && Vis(tg) && Rdy("tachiai") && d > 6 && d < 18 && Math.Abs(tg.pos.y - a.pos.y) < 1.5 && Rng.Random() < 0.35)
                        {
                            if (SolidLine(tg)) { CastAt("a1", tg.Center); break; }
                        }
                        break;
                    }
                case "enra":
                    {
                        var rai = foes.FirstOrDefault(x => x.def.id == "raijin" && World.Dist3(x.pos, a.pos) < 17 && Vis(x));
                        // COUNTER: chain Raijin (especially mid Flash Step)
                        if (Rdy("chain") && rai != null && (rai.forced?.kind == "flashstep" || World.Dist3(rai.pos, a.pos) > 5)) { CastAt("a1", rai.Center); break; }
                        if (Rdy("chain") && tg != null && d > 7 && d < 17 && Vis(tg)) { CastAt("a1", tg.Center); break; }
                        if (Rdy("brand") && tg != null && d < 6) { CastAt("a2", tg.Center); break; }
                        // Crimson Effigy: a fight in his perimeter, or the rival close and him still standing
                        if (ultReady && ((Near(a.pos, 12, foes).Count >= 2) || (tg != null && d < 8 && a.Health / a.MaxHp > 0.4))) CastAt("ult");
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ training robots
        void Robot(double dt)
        {
            double t = w.time; var i = a.input;
            var players = w.Enemies(a).Where(x => !x.isRobot).ToList();
            var tg = JsSort.SortBy(players.Where(x => World.Dist3(x.pos, a.pos) < 35 && w.Visible(a, x)).ToList(), (p, q) => World.Dist3(p.pos, a.pos) - World.Dist3(q.pos, a.pos)).FirstOrDefault();
            i.fire = false; i.mx = 0; i.mz = 0;
            if (a.def.id == "bot_sentry")
            {
                // patrol along Z in its lane
                var bas = a.spawn;
                double dir = Math.Sin(t * 0.35 + a.id) > 0 ? 1 : -1;
                double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                i.mz = 0 * fx + dir * fz; i.mx = 0 * rx + dir * rz;
                if (Math.Abs(a.pos.z - bas[1]) > 7 && M.Sign(a.pos.z - bas[1]) == dir) { i.mz = 0; i.mx = 0; }
                if (tg != null)
                {
                    target = tg;
                    V3 c = tg.Center, e = a.Eye;
                    i.yaw = Math.Atan2(c.x - e.x, c.z - e.z); i.pitch = Math.Atan2(c.y - e.y, M.Hypot(c.x - e.x, c.z - e.z));
                    i.fire = Math.Sin(t * 1.3 + a.id) > -0.2;
                }
            }
            else if (a.def.id == "bot_drone")
            {
                double r = 6, ang = t * 0.5 + a.id;
                double gx = a.spawn[0] + Math.Cos(ang) * r, gz = a.spawn[1] + Math.Sin(ang) * r;
                var d = Norm2(gx - a.pos.x, gz - a.pos.z);
                double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                i.mz = d.x * fx + d.z * fz; i.mx = d.x * rx + d.z * rz;
                a.sv["hoverY"] = 3 + Math.Sin(t * 0.9 + a.id) * 1.5;
            }
        }
    }
}
