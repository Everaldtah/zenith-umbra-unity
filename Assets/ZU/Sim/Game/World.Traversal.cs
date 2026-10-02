// Hero traversal: Hibiki's Mag-Grind + Groove, the Koryu brothers' wall climb, Mirror Water deflection, mantling,
// Mirei's Starwing Swoop. Port of zenith-umbra src/game/World.ts (grindStep .. swoopStep).
using System;
using System.Linq;

namespace ZU.Sim
{
    public partial class World
    {
        /// <summary>Hibiki - Mag-Grind (wall ride + climb), after Lucio. See the TS source for the full rules.</summary>
        bool GrindStep(Actor a, double dt, double spd, bool rooted)
        {
            double t = time; var L = level; var inp = a.input;
            if (a.def.id != "hibiki") return false;
            bool on = a.Has("grinding", t);
            bool hold = inp.grind ?? inp.jumpHeld;
            double look = Math.Max(-1, Math.Min(1, a.pitch / 0.7));           // -1 looking down .. 1 looking well up
            bool Off(bool kick)
            {
                a.Clear("grinding"); a.sv["grindCd"] = t + 0.3;
                if (kick)
                {
                    double up0 = Math.Max(0, look);
                    a.vel.x += a.Sv("grindNx", 0) * (5.5 - 2 * up0); a.vel.z += a.Sv("grindNz", 0) * (5.5 - 2 * up0); a.vel.y = 6.2 + 3.6 * up0;
                    a.anim.jumpAt = t; Sfx("jump", a.pos, a); Fx("doublejump", a.pos, new FxOpts { color = a.def.glow });
                }
                return false;
            }
            if (a.grounded) a.sv["climbT"] = 0;
            if (rooted || a.forced != null || a.Has("stun", t)) return on ? Off(false) : false;
            if (!hold) return on ? Off(true) : false;
            double y = a.pos.y + a.Height * 0.5, reach = a.Radius + 1.1;
            RayHit? Probe(double nx0, double nz0, double py)
            {
                var h = L.Ray(new V3(a.pos.x, py, a.pos.z), new V3(nx0, 0, nz0), reach);
                return h.HasValue && Math.Abs(h.Value.ny) < 0.35 ? h : null;
            }
            double hs = M.Hypot(a.vel.x, a.vel.z);
            if (!on)
            {
                if (t < a.Sv("grindCd", 0)) return false;
                double air = a.pos.y - L.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.2);
                // from the ground he only latches running at a wall while looking up it; in the air, any wall
                bool fromGround = a.grounded || air < 0.5;
                // (or skating fast straight at it - the groove carries him up without having to look)
                if (fromGround && look < 0.12 && !(hs > 8 && (a.vel.x * Math.Sin(a.yaw) + a.vel.z * Math.Cos(a.yaw)) / hs > 0.7)) return false;
                double fx0 = Math.Sin(a.yaw), fz0 = Math.Cos(a.yaw);
                double vx = hs > 0.5 ? a.vel.x / hs : fx0, vz = hs > 0.5 ? a.vel.z / hs : fz0;
                double bestT = double.NaN, bnx = 0, bnz = 0;
                var dirs = fromGround ? new[] { (fx0, fz0), (vx, vz) }
                    : new[] { (-vz, vx), (vz, -vx), (vx * 0.7 - vz * 0.7, vz * 0.7 + vx * 0.7), (vx * 0.7 + vz * 0.7, vz * 0.7 - vx * 0.7), (vx, vz), (fx0, fz0) };
                foreach (var (dx, dz) in dirs)
                {
                    double l = M.Hypot(dx, dz); if (l == 0) l = 1; var h = Probe(dx / l, dz / l, y);
                    if (h.HasValue && (double.IsNaN(bestT) || h.Value.t < bestT)) { double nl = M.Hypot(h.Value.nx, h.Value.nz); if (nl == 0) nl = 1; bestT = h.Value.t; bnx = h.Value.nx / nl; bnz = h.Value.nz / nl; }
                }
                if (double.IsNaN(bestT)) return false;
                a.sv["grindNx"] = bnx; a.sv["grindNz"] = bnz;
                // ride the way he was already travelling along the wall; head-on (or from a standstill) he rides straight up it
                double along = a.vel.x * -bnz + a.vel.z * bnx;
                a.sv["grindDir"] = along >= 0 ? 1 : -1;
                a.sv["grindHeadOn"] = Math.Abs(along) < Math.Max(1.2, hs * 0.35) ? 1 : 0;
                a.sv["climbT"] = 0;
                if (fromGround) { a.grounded = false; a.lastGroundedAt = -9; a.pos.y += 0.05; }
                Sfx("grindstart", a.pos, a);
            }
            else
            {
                // still a wall there? (corners and wall ends drop him off with his momentum)
                var h = Probe(-a.Sv("grindNx", 0), -a.Sv("grindNz", 0), y);
                if (!h.HasValue)
                {
                    // ...unless he's at the top of it: then he mantles onto the roof
                    if (Mantle(a)) return false;
                    return Off(false);
                }
                double nl = M.Hypot(h.Value.nx, h.Value.nz); if (nl == 0) nl = 1; a.sv["grindNx"] = h.Value.nx / nl; a.sv["grindNz"] = h.Value.nz / nl;
            }
            double nx = a.sv["grindNx"], nz = a.sv["grindNz"], dir = a.Sv("grindDir", 1);
            // steering: pushing away from the wall lets go; pushing back along it reverses the ride
            double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
            double ix = fx * inp.mz + rx * inp.mx, iz = fz * inp.mz + rz * inp.mx;
            if (ix * nx + iz * nz > 0.75) return Off(false);
            double tx = -nz * dir, tz = nx * dir;
            if (ix * tx + iz * tz < -0.7) { a.sv["grindDir"] = -dir; a.sv["grindHeadOn"] = 0; }
            // climb: looking up angles the ride upward (for a while), looking down drops along the wall
            a.sv["climbT"] = a.Sv("climbT", 0) + dt;
            // head-on he can only skate a storey or so straight up; riding along the wall he climbs at an angle for longer
            bool headOn = a.Sv("grindHeadOn", 0) != 0; double climbMax = headOn ? 3.0 : 4.5;
            // wall hop: tapping jump on the wall kicks him up it and refreshes the climb (and keeps the groove going)
            if (Pressed(a, "jump"))
            {
                a.vel.y = Math.Max(a.vel.y, 7.5); a.sv["climbT"] = Math.Max(0, a.sv["climbT"] - 1.2); a.anim.jumpAt = t;
                Sfx("jump", a.pos, a);
            }
            // (tapping climbs too, wherever he looks: the rhythm takes him up the wall)
            bool tapped = t - a.Sv("tapAt", -9) < 0.4;
            bool climbing = (look > 0.05 || tapped) && a.sv["climbT"] < climbMax;
            double up = climbing ? Math.Max(look, tapped ? 0.7 : 0) * Math.Min(1, (climbMax - a.sv["climbT"]) / 0.35) : 0;
            // the arena's boundary walls: he can ride them, but the climb stops short of their top (no leaving the map)
            double BX = L.Size[0], BZ = L.Size[1];
            bool border = Math.Abs(a.pos.x) > BX - 2.5 || Math.Abs(a.pos.z) > BZ - 2.5;
            bool capped = border && !Probe(-nx, -nz, a.pos.y + a.Height + 1.2).HasValue;
            if (capped) up = 0;
            // on a wall the Groove lifts him; along the wall it counts for a little over double at most
            double gr = Math.Max(1, a.Sv("rhythm", 1)), spdW = spd / gr * Math.Min(gr, 2.2);
            double sp = spdW * 1.3 * (headOn ? 0.15 : 1 - 0.55 * Math.Max(0, up)), cur = a.vel.x * tx + a.vel.z * tz;
            double v = cur + (sp - cur) * Math.Min(1, dt * 6);
            // a light pull toward the wall keeps the skates on it
            var ph = Probe(-nx, -nz, y);
            double gap = (ph.HasValue ? ph.Value.t : reach) - a.Radius;
            double pull = Math.Max(0, gap - 0.05) * 6;
            a.vel.x = tx * v - nx * pull; a.vel.z = tz * v - nz * pull;
            double lift = Math.Min(2.5, Math.Sqrt(a.Sv("rhythm", 1)));       // the groove climbs faster too
            double vyT = up > 0 ? (headOn ? 7 : 6) * up * lift : look < -0.25 ? -5 * -look : 0;
            a.vel.y += (vyT - a.vel.y) * Math.Min(1, dt * (up > 0 ? 8 : 12));
            if (capped) a.vel.y = Math.Min(a.vel.y, 0);
            // nearing the top of the wall on the way up: over the edge and onto the roof
            if (a.vel.y > 0.5 && (!Probe(-nx, -nz, a.pos.y + a.Height + 0.25).HasValue || !Probe(-nx, -nz, a.pos.y + a.Height + 1.2).HasValue) && Mantle(a)) return false;
            a.Set("grinding", t, 0.15); a.flying = false;
            a.sv["grindUp"] = up;
            // which side the wall is on relative to where he's facing (the animator leans away from it)
            a.sv["grindSide"] = (nx * rx + nz * rz) > 0 ? -1 : 1;
            return true;
        }

        /// <summary>Hibiki's Groove (and Hayate's Sun-Alloy Frame): speed follows how fast jump is being tapped.</summary>
        double GrooveStep(Actor a, double dt, double max = GROOVE_MAX)
        {
            double t = time;
            if (Pressed(a, "jump"))
            {
                double gap = t - a.Sv("tapAt", -9);
                a.sv["tapAt"] = t;
                double inst = gap > 0.04 ? Math.Min(12, 1 / gap) : 12;
                a.sv["tapRate"] = a.Sv("tapRate", 0) * 0.4 + inst * 0.6;
            }
            // a slower beat shows at once: the rate is capped by the gap since the last tap (none for over a second = stopped)
            double since = t - a.Sv("tapAt", -9);
            a.sv["tapRate"] = since > 1.2 ? 0 : Math.Min(a.Sv("tapRate", 0), 1 / Math.Max(1e-3, since) * 1.15);
            double x = Math.Max(0, Math.Min(1, (a.Sv("tapRate", 0) - GROOVE_TAPS[0]) / (GROOVE_TAPS[1] - GROOVE_TAPS[0])));
            double target = 1 + (max - 1) * x;
            double g0 = a.Sv("rhythm", 1);
            a.sv["rhythm"] = g0 + (target - g0) * Math.Min(1, dt * (target > g0 ? 2.2 : 3.5));
            if (a.sv["rhythm"] > 1.5) a.Set("rhythm", t, 0.25);
            // deep in the groove his skates leave a light trail
            if (a.sv["rhythm"] > Math.Min(4, max * 0.7) && t >= a.Sv("rhythmFx", 0)) { a.sv["rhythmFx"] = t + 0.3; Fx("chargetrail", a.pos, new FxOpts { actor = a, dur = 0.4, color = a.def.glow }); }
            return a.sv["rhythm"];
        }

        /// <summary>The Koryu brothers climb (after Genji): run at a wall and jump (or keep tapping) and run up it.</summary>
        bool ClimbStep(Actor a, double dt, bool rooted)
        {
            string id = a.def.id;
            if (id != "hayate" && id != "seiran") return false;
            double t = time; var inp = a.input; var L = level;
            if (a.grounded) a.sv["climbLeft"] = CLIMB_SECS;
            bool Stop() { if (a.Has("wallclimb", t)) a.Clear("wallclimb"); return false; }
            if (rooted || a.forced != null || a.Has("stun", t) || inp.mz < (id == "hayate" ? 0.05 : 0.3)) return Stop();
            double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), reach = a.Radius + 0.5;
            RayHit? Wall(double y) { var h = L.Ray(new V3(a.pos.x, y, a.pos.z), new V3(fx, 0, fz), reach); return h.HasValue && Math.Abs(h.Value.ny) < 0.35 ? h : null; }
            var hw = Wall(a.pos.y + a.Height * 0.55);
            bool on = a.Has("wallclimb", t);
            if (!hw.HasValue)
            {
                // the wall ended under his hands: over the top and onto the roof
                if (on && Mantle(a)) { a.Clear("wallclimb"); return false; }
                return Stop();
            }
            double X = L.Size[0], Z = L.Size[1];
            bool border = Math.Abs(a.pos.x) > X - 2.5 || Math.Abs(a.pos.z) > Z - 2.5;
            // the arena's own walls: Seiran stops short of them; Hayate climbs them one Cyber-Agility climb (7.8 m) per air
            if (border && id != "hayate") return Stop();
            if (a.grounded) a.sv["edgeClimb"] = 0;
            if (border)
            {
                double top = L.GroundAt(a.pos.x + fx * (reach + 0.4), a.pos.z + fz * (reach + 0.4), 500);
                if ((IsFinite(top) && a.pos.y >= top - a.Height - 0.3) || a.Sv("edgeClimb", 0) >= 7.8) { a.vel.y = Math.Min(a.vel.y, 0); return Stop(); }
            }
            bool tap = Pressed(a, "jump");
            // (holding jump into a wall starts a climb too - Genji's - as does Seiran just moving into one in the air)
            bool want = tap || inp.jumpHeld || (id == "seiran" && !a.grounded && (on || a.vel.y < 2));
            if (!want) return on;
            // Hayate climbs any wall for as long as jump is held; Seiran has CLIMB_SECS per touch of the ground
            if (id != "hayate")
            {
                if (tap) a.sv["climbLeft"] = Math.Min(CLIMB_SECS, a.Sv("climbLeft", CLIMB_SECS) + 0.14);
                if (a.Sv("climbLeft", CLIMB_SECS) <= 0) return Stop();
                a.sv["climbLeft"] = a.Sv("climbLeft", CLIMB_SECS) - dt;
            }
            double nl = M.Hypot(hw.Value.nx, hw.Value.nz); if (nl == 0) nl = 1;
            a.sv["grindNx"] = hw.Value.nx / nl; a.sv["grindNz"] = hw.Value.nz / nl; a.sv["grindDir"] = 1;
            // Hayate runs up at Genji's 7.8 m/s, faster with the jump-tap rhythm
            double up = (id == "hayate" ? 7.8 * Math.Min(KOI_GROOVE_MAX, Math.Max(1, a.Sv("rhythm", 1))) : 6.8) * (a.Has("dragonblade", t) ? 1.25 : 1);
            a.vel.y = up; a.vel.x = -a.sv["grindNx"] * 1.2; a.vel.z = -a.sv["grindNz"] * 1.2;          // hug the wall
            if (border) a.sv["edgeClimb"] = a.Sv("edgeClimb", 0) + up * dt;
            a.grounded = false; a.flying = false; a.lastGroundedAt = -9;
            if (!on) { a.anim.jumpAt = t; Sfx("jump", a.pos, a); }
            a.Set("wallclimb", t, 0.18);
            // reaching the top: the wall is gone at head height - vault over
            if (!Wall(a.pos.y + a.Height + 0.35).HasValue && Mantle(a)) { a.Clear("wallclimb"); return false; }
            return true;
        }

        /// <summary>where an actor is aiming (unit vector from yaw and pitch)</summary>
        public V3 AimDir(Actor a) { double c = Math.Cos(a.pitch); return new V3(Math.Sin(a.yaw) * c, Math.Sin(a.pitch), Math.Cos(a.yaw) * c); }

        /// <summary>Mirror Water: is something coming at him from in front (within 90 degrees of where he faces)?</summary>
        bool Deflected(Actor x, V3 from, V3 at)
        {
            double dx = from.x - x.pos.x, dz = from.z - x.pos.z, l = M.Hypot(dx, dz);
            if (l > 1e-3 && (dx * Math.Sin(x.yaw) + dz * Math.Cos(x.yaw)) / l < 0) return false;
            double dy = from.y - x.Center.y, n = M.Hypot(l, dy); if (n == 0) n = 1;
            var dir = new V3(dx / n, dy / n, dz / n);
            x.anim.deflectAt = time; x.anim.deflectDir = dir; x.anim.deflectN++;
            x.stats["deflects"] = (x.stats.TryGetValue("deflects", out var dd) ? dd : 0) + 1;
            Sfx("parry", at); Fx("deflect", at, new FxOpts { color = "#8ad8ff", actor = x, n = dir });
            return true;
        }

        /// <summary>top-out: if the wall ends in a roof within reach above his feet, pop up over the edge and land on it</summary>
        bool Mantle(Actor a)
        {
            var L = level; double nx = a.Sv("grindNx", 0), nz = a.Sv("grindNz", 0);
            double X = L.Size[0], Z = L.Size[1];
            foreach (var inset in new[] { a.Radius + 0.35, a.Radius + 0.8 })
            {
                double px = a.pos.x - nx * inset, pz = a.pos.z - nz * inset;
                // never onto the arena's boundary walls
                if (Math.Abs(px) > X - 1 || Math.Abs(pz) > Z - 1) return false;
                double roof = L.GroundAt(px, pz, a.pos.y + 2.8, a.Radius * 0.5);
                // his hands reach the edge: the roof is at most ~2.4m above his skates
                if (!IsFinite(roof) || roof < a.pos.y - 0.3 || roof > a.pos.y + 2.4) continue;
                // room to stand up there?
                if (L.CeilingAt(px, pz, roof + 0.05) < roof + a.Height * 0.9) continue;
                double t = time;
                a.pos = new V3(px, roof + 0.02, pz);
                double tx = -a.Sv("grindNz", 0) * a.Sv("grindDir", 1), tz = a.Sv("grindNx", 0) * a.Sv("grindDir", 1);
                double along = a.vel.x * tx + a.vel.z * tz;
                a.vel = new V3(-nx * 3 + tx * along * 0.6, 0, -nz * 3 + tz * along * 0.6);
                a.grounded = true; a.lastGroundedAt = t; a.anim.landAt = t;
                a.Clear("grinding"); a.sv["grindCd"] = t + 0.35; a.sv["climbT"] = 0;
                a.stats["mantles"] = (a.stats.TryGetValue("mantles", out var mm) ? mm : 0) + 1;
                Sfx("land", a.pos, a); Fx("doublejump", a.pos, new FxOpts { color = a.def.glow });
                return true;
            }
            return false;
        }

        /// <summary>Mirei's Starwing Swoop (a guardian-angel dash) toward the ally under the crosshair.</summary>
        bool SwoopStep(Actor a, double dt)
        {
            double t = time;
            if (a.def.id != "mirei" || !full) return false;
            bool blocked = a.Has("grounded", t) || a.Has("root", t) || a.Has("stun", t) || a.Has("silence", t) || a.forced != null;
            if (!a.Has("swoop", t))
            {
                if (!Pressed(a, "swoop") || blocked || !a.Ready("swoop", t)) return false;
                var tg0 = ConeTarget(a, 30, 14, x => x.team == a.team && x != a && !x.isRobot);
                if (tg0 == null) { Sfx("denied", a.pos, a); return false; }
                a.sv["swoopT"] = tg0.id; a.sv["swoopStart"] = t; a.sv["swoopD0"] = Dist3(tg0.Center, a.Center);
                a.Set("swoop", t, 2.4); a.flying = false; a.grounded = false; a.lastGroundedAt = -9;
                a.stats["swoops"] = (a.stats.TryGetValue("swoops", out var sw) ? sw : 0) + 1;
                Sfx("swoop", a.Center, a); Fx("swoop", a.Center, new FxOpts { actor = a, target = tg0, color = a.def.glow });
            }
            var tg = actors.FirstOrDefault(x => a.sv.TryGetValue("swoopT", out var st) && x.id == st);
            void End(double keep) { a.Clear("swoop"); a.cd["swoop"] = t + 1.6; a.sv["swoopEndAt"] = t; a.vel.x *= keep; a.vel.y *= keep; a.vel.z *= keep; }
            if (tg == null || !tg.alive || blocked) { End(0.5); return false; }
            var v = new V3(tg.pos.x - a.pos.x, tg.pos.y + tg.Height * 0.35 - a.pos.y - a.Height * 0.35, tg.pos.z - a.pos.z);
            double dist = M.Hypot(v.x, v.y, v.z); if (dist == 0) dist = 1e-3; var dir = new V3(v.x / dist, v.y / dist, v.z / dist);
            double age = t - a.Sv("swoopStart", t), sp = Math.Min(21, 13 + age * 16);
            double prog = Math.Min(1, age / Math.Max(0.35, a.Sv("swoopD0", 12) / 17));
            a.sv["swoopProg"] = prog;
            if (Pressed(a, "jump"))
            {
                // slingshot: fling onward with the swoop's momentum (more the further into the swoop)
                double k = 0.55 + 0.45 * prog;
                a.vel = new V3(dir.x * sp * k, Math.Max(0, dir.y * sp * k) + 7 * k, dir.z * sp * k);
                End(1); a.Set("slingshot", t, 1.2); a.anim.jumpAt = t;
                Sfx("sunhop", a.pos, a); Fx("swoopburst", a.Center, new FxOpts { actor = a, color = a.def.glow });
                return true;
            }
            if (Pressed(a, "descend"))
            {
                // superjump: straight up, then Angelic descent takes over while SPACE is held
                double k = 0.55 + 0.45 * prog;
                a.vel = new V3(a.vel.x * 0.2, 10 + 7 * k, a.vel.z * 0.2);
                End(1); a.Set("superjump", t, 1.4); a.anim.jumpAt = t;
                Sfx("sunhop", a.pos, a); Fx("swoopburst", a.Center, new FxOpts { actor = a, color = a.def.glow });
                return true;
            }
            if (dist < 1.2 + tg.Radius + a.Radius)
            {
                // arrival: flare the wings and bleed off most of the speed beside the ally
                a.vel = new V3(dir.x * sp * 0.28, Math.Max(dir.y * sp * 0.2, 0) + 1.2, dir.z * sp * 0.28);
                End(1); a.Set("swoopflare", t, 0.4);
                return true;
            }
            a.vel = new V3(dir.x * sp, dir.y * sp, dir.z * sp);
            // skim just above the floor rather than sliding along it
            if (a.vel.y < 0.4 && a.pos.y - level.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.5) < 0.5) a.vel.y = 0.4;
            a.grounded = false;
            return true;
        }
    }
}
