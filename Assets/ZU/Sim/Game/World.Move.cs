// World movement integration (port of zenith-umbra src/game/World.ts `move`): forced motion, speed modifiers, flight,
// jets, air control, jumps, sub-stepped integration against the level, ledge guards, ground snap, jump pads, the void.
using System;
using static ZU.Sim.Roles;

namespace ZU.Sim
{
    public partial class World
    {
        /// <summary>movement integration only (also used by co-op clients to predict their own hero)</summary>
        public void Move(Actor a, double dt)
        {
            double t = time; var d = a.def; var inp = a.input; var L = level;
            a.yaw = inp.yaw; a.pitch = Math.Max(-1.45, Math.Min(1.45, inp.pitch));
            bool wasGrounded = a.grounded;
            // colossi can't be dragged, pulled or knocked around by heroes
            if (a.isBoss && a.forced != null && (a.forced.kind == "pull" || a.forced.kind == "knock")) a.forced = null;
            // Tachiai Rush is unstoppable: shoves and pulls slide off it
            if (a.forced != null && a.Has("tachiai", t) && (a.forced.kind == "pull" || a.forced.kind == "knock")) a.forced = null;
            // stalwart tanks take 40% less knockback
            if (a.forced?.kind == "knock" && !a.forced.scaled && IsSub(a, "stalwart")) { var f = a.forced; f.vx *= STALWART_KNOCK; f.vy *= STALWART_KNOCK; f.vz *= STALWART_KNOCK; f.scaled = true; }
            if (a.forced != null)
            {
                var f = a.forced;
                if (t >= f.until) { a.forced = null; f.onEnd?.Invoke(); a.vel.x *= 0.3; a.vel.z *= 0.3; if (f.ignoreGravity) a.vel.y = Math.Min(a.vel.y, 0); }
                else { a.vel = new V3(f.vx, f.ignoreGravity ? f.vy : a.vel.y - G * dt, f.vz); }
            }
            if (a.forced == null)
            {
                double spd = d.speed * (1 + a.mods.speed);
                if (a.Has("slow", t)) spd *= 1 - 0.2 * (IsSub(a, "stalwart") ? STALWART_SLOW : 1);     // (stalwarts: slows 40% weaker)
                if (a.Has("speed", t)) spd *= a.Sv("speed", 1.25);
                if (IsSub(a, "bruiser") && a.Health < a.MaxHp * 0.5) spd *= BRUISER_SPEED;              // bruisers run when hurt
                if (a.Has("titan", t)) spd *= 1.2;
                if (a.Has("judgment", t) || a.Has("stealth", t)) spd *= 1.3;
                if (a.charging) spd *= 0.7;
                if (a.barrier.up) spd *= 0.65;
                if (a.flameOn) spd *= 0.9;
                if (a.Has("reapwind", t)) spd *= 0.55;           // Tomoe heaving the axe round
                if (d.id == "hibiki") spd *= GrooveStep(a, dt);
                else if (d.id == "hayate") spd *= GrooveStep(a, dt, KOI_GROOVE_MAX);   // Sun-Alloy Frame: tap jump faster, run faster
                if (a.Has("dragonblade", t)) spd *= 1.3;         // Hayate's Dragon Gate Blade
                bool rooted = a.Has("root", t) || a.Has("stun", t) || a.Has("rising", t);
                double mx = inp.mx, mz = inp.mz;
                double ml = M.Hypot(mx, mz); if (ml > 1) { mx /= ml; mz /= ml; }
                if (mz < 0) mz *= 0.9;
                double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                double wx = (fx * mz + rx * mx) * spd, wz = (fz * mz + rz * mx) * spd;
                if (rooted) { wx = 0; wz = 0; }
                // Gantetsu - Tachiai Rush: a straight-ahead charge the aim steers only slowly; SPACE ends it in a Shiko Stomp
                bool rush = a.Has("tachiai", t);
                if (rush)
                {
                    double cur = a.Sv("rushYaw", a.yaw);
                    double dy = inp.yaw - cur; while (dy > Math.PI) dy -= 2 * Math.PI; while (dy < -Math.PI) dy += 2 * Math.PI;
                    double ny = cur + Math.Max(-2.2 * dt, Math.Min(2.2 * dt, dy));
                    a.sv["rushYaw"] = ny;
                    wx = Math.Sin(ny) * spd * 1.85; wz = Math.Cos(ny) * spd * 1.85;
                    if (Pressed(a, "jump") && a.grounded) Abilities.StompLeap(this, a);
                }
                // ...and past the top of the leap he drives himself down into the slam
                if (a.Sv("stompArmed", 0) != 0 && !a.grounded && a.vel.y < 0) a.vel.y -= G * 1.5 * dt;
                // Mirei - Starwing Swoop: the swoop drives her velocity this step (flight, steering and gravity sit it out)
                bool swooping = SwoopStep(a, dt) || GrindStep(a, dt, spd, rooted) || ClimbStep(a, dt, rooted);
                // flight
                bool canFly = (d.frame == "flyer" || d.frame == "drone" || (d.jets ?? 0) != 0) && !a.Has("grounded", t) && !rooted && !swooping;
                if (d.frame == "drone") a.flying = true;
                else if (canFly && inp.jumpHeld && a.flight > 5 && (!a.grounded || !Pressed(a, "jump"))) a.flying = true;
                if (!canFly || a.flight <= 0) a.flying = false;
                bool jets = (d.jets ?? 0) != 0;
                if (a.flying && d.frame != "drone")
                {
                    double target = inp.jumpHeld ? (jets ? 6.5 : 5.5) : inp.descend ? -7 : jets ? -1.8 : -1.1;
                    a.vel.y += (target - a.vel.y) * Math.Min(1, dt * 5);
                    // thrusters burn a fixed fuel tank (d.jets seconds); wings hover cheaply
                    a.flight -= (jets ? 100 / d.jets.Value : inp.jumpHeld ? 15 : 4.5) * dt;
                    if (jets) { wx *= 1.45; wz *= 1.45; if (t >= a.Sv("jetSfx", 0)) { a.sv["jetSfx"] = t + 0.35; Sfx("flame", a.pos, a); } }
                    if (a.grounded && !inp.jumpHeld) a.flying = false;
                }
                else if (d.frame == "drone")
                {
                    // over the void groundAt is -Infinity: hold the altitude of the last solid ground instead of diving forever
                    double gnd = L.GroundAt(a.pos.x, a.pos.z, a.pos.y);
                    if (!double.IsInfinity(gnd) && !double.IsNaN(gnd)) a.sv["hoverGround"] = gnd;
                    double want = a.sv.TryGetValue("hoverY", out var hy) ? hy : (a.sv.TryGetValue("hoverGround", out var hg) ? hg : a.pos.y - 3.2) + 3.2;
                    a.vel.y += ((want - a.pos.y) * 2 - a.vel.y) * Math.Min(1, dt * 3);
                }
                // air control: a slingshot / superjump out of a swoop carries its momentum (steering nudges it, not brakes it)
                double k = a.grounded ? 14 : a.flying ? 4 : a.Has("slingshot", t) || a.Has("superjump", t) ? 0.55 : 2.5;
                // Hibiki skates (like Lucio): pushes up to speed, glides when you let go, carves through turns, keeps momentum
                if (d.id == "hibiki")
                {
                    double want = M.Hypot(wx, wz), cur = M.Hypot(a.vel.x, a.vel.z);
                    bool turning = want > 0.1 && cur > 0.5 && (wx * a.vel.x + wz * a.vel.z) / (want * cur) < 0.3;
                    k = a.grounded ? (want < 0.1 ? 1.6 : turning ? 4 : want > cur ? 5.5 : 3) : 1.4;
                }
                if (!swooping)
                {
                    a.vel.x += (wx - a.vel.x) * Math.Min(1, dt * k);
                    a.vel.z += (wz - a.vel.z) * Math.Min(1, dt * k);
                }
                if (Pressed(a, "jump") && !rooted && !rush && !swooping)
                {
                    if (a.grounded || t - a.lastGroundedAt < 0.1)
                    {
                        a.vel.y = d.frame == "mech" ? 8 : 8.6; a.grounded = false; a.anim.jumpAt = t; a.lastGroundedAt = -9;
                        a.airJumps = d.id == "raijin" || d.id == "hayate" ? 1 : 0;
                        Sfx(d.frame == "mech" ? "mechjump" : "jump", a.pos, a);
                    }
                    else if (a.airJumps > 0)
                    {
                        a.airJumps--; a.vel.y = 8.2; a.anim.jumpAt = t; Sfx("doublejump", a.pos, a); Fx("doublejump", a.pos, new FxOpts { color = d.glow });
                    }
                }
                if (!a.flying && d.frame != "drone" && !swooping) a.vel.y -= G * dt * (a.Has("glide", t) && a.vel.y < 0 ? 0.18 : 1);
                // Mirei's angelic descent: holding SPACE while falling floats her down slowly instead of dropping
                if (d.id == "mirei" && !a.flying && !a.grounded && !swooping && inp.jumpHeld && a.vel.y < -2.2 && a.forced == null) { a.vel.y = -2.2; a.Set("angelglide", t, 0.15); }
            }
            // integrate with sub-steps so fast dashes don't tunnel
            // a non-finite velocity would make the sub-step count infinite and freeze the whole simulation
            if (!IsFinite(a.vel.x + a.vel.y + a.vel.z)) a.vel = V3.Zero;
            double sp = M.Hypot(a.vel.x, a.vel.y, a.vel.z) * dt;
            int n = (int)Math.Min(64, Math.Max(1, Math.Ceiling(sp / 0.3)));
            bool hitWall = false;
            double y0 = a.pos.y, x0 = a.pos.x, z0 = a.pos.z;
            for (int i = 0; i < n; i++)
            {
                double head = a.pos.y + a.ColHeight;
                a.pos.x += a.vel.x * dt / n; a.pos.y += a.vel.y * dt / n; a.pos.z += a.vel.z * dt / n;
                // ceilings: rising into a slab (an upper floor, a roof, a door lintel) stops the climb
                if (a.vel.y > 0)
                {
                    double c = L.CeilingAt(a.pos.x, a.pos.z, head);
                    if (a.pos.y + a.ColHeight > c) { a.pos.y = c - a.ColHeight - 0.01; a.vel.y = 0; }
                }
                if (L.Collide(ref a.pos, a.ColRadius, a.ColHeight)) hitWall = true;
            }
            if (hitWall && (a.forced?.kind == "abysscharge" || a.forced?.kind == "dawncharge")) { if (a.forced.kind == "dawncharge") a.sv["chargeWall"] = 1; a.forced.until = t; }
            // a swoop that runs into a wall stops dead instead of grinding along it
            if (hitWall && a.Has("swoop", t) && M.Hypot(a.pos.x - x0, a.pos.z - z0) < M.Hypot(a.vel.x, a.vel.z) * dt * 0.25) { a.Clear("swoop"); a.cd["swoop"] = t + 1.6; a.vel.x *= 0.2; a.vel.z *= 0.2; }
            RingClamp(a);
            double X = L.Size[0], Z = L.Size[1];
            a.pos.x = Math.Max(-X - 1, Math.Min(X + 1, a.pos.x)); a.pos.z = Math.Max(-Z - 1, Math.Min(Z + 1, a.pos.z));
            // AI walkers never step off a ledge into the void on their own (knockbacks / pulls still can)
            if (a.controller != null && !a.isPlayer && ((wasGrounded && a.forced == null && a.vel.y <= 0) || a.isBoss) && !a.flying && a.def.frame != "drone"
                && L.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.3) < a.pos.y - 5)
            {
                a.pos.x = x0; a.pos.z = z0; a.vel.x = 0; a.vel.z = 0;
                if (a.isBoss) a.forced = null;    // colossi stop at the edge instead of charging into the void
            }
            // colossi remember their last solid footing; separation pushes or charges that end over the void snap back
            if (a.isBoss && a.def.frame != "drone")
            {
                if (L.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.5) > a.pos.y - 3) { a.sv["safeX"] = a.pos.x; a.sv["safeZ"] = a.pos.z; a.sv["safeY"] = a.pos.y; }
                else if (a.sv.ContainsKey("safeX")) { a.pos.x = a.sv["safeX"]; a.pos.z = a.sv["safeZ"]; a.pos.y = Math.Max(a.pos.y, a.sv["safeY"]); a.vel.x = a.vel.z = 0; a.forced = null; }
            }
            // sweep from last frame's height so fast falls can't tunnel through thin floors
            double g = L.GroundAt(a.pos.x, a.pos.z, Math.Max(a.pos.y, Math.Min(y0, a.pos.y + 3)), a.Radius);
            if (a.pos.y <= g + 1e-3 && a.vel.y <= 0.01)
            {
                if (!wasGrounded && a.vel.y < -7) { a.anim.landAt = t; Sfx(d.frame == "mech" ? "mechland" : "land", a.pos, a); if (d.frame == "mech") Fx("dust", a.pos, new FxOpts { r = 2.5 }); }
                a.pos.y = g; a.vel.y = 0; a.grounded = true; a.lastGroundedAt = t;
            }
            else if (wasGrounded && a.vel.y <= 0 && !a.flying && a.pos.y - g < STEP + 0.05 && a.forced == null)
            {
                a.pos.y = g; a.vel.y = 0; a.grounded = true; a.lastGroundedAt = t;
            }
            else if (a.pos.y < g && a.vel.y > 0)
            {
                a.pos.y = g;
            }
            else a.grounded = false;
            if (a.grounded) { a.flight = Math.Min(100, a.flight + 32 * dt); a.flying = false; }
            var pad = a.grounded && a.forced == null ? L.PadAt(a.pos.x, a.pos.z, a.pos.y) : null;
            if (pad != null)
            {
                a.vel = new V3(pad.vx, pad.vy, pad.vz); a.grounded = false; a.lastGroundedAt = -9; a.anim.jumpAt = t;
                Sfx("pad", a.pos, a); Fx("pad", a.pos, new FxOpts { color = map.tint });
                a.Set("padflight", t, 2.5); a.sv["padvx"] = pad.vx; a.sv["padvz"] = pad.vz;
            }
            if (a.Has("padflight", t) && !a.grounded && a.forced == null)
            {
                // pads carry you over the gap: hold the launch velocity instead of letting input brake it
                if (a.Sv("padvx", 0) != 0 || a.Sv("padvz", 0) != 0) { a.vel.x = a.sv["padvx"]; a.vel.z = a.sv["padvz"]; }
            }
            if (a.grounded) a.Clear("padflight");
            if (a.pos.y < L.KillY)
            {
                Fx("fall", a.pos, new FxOpts());
                Kill(a, null);
            }
        }

        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
