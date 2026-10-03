// Per-step upkeep for zones, auras, dashes and the Crescent Fang. Port of zenith-umbra src/game/abilities.ts tickAbilities.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZU.Sim
{
    public static partial class Abilities
    {
        public static void TickAbilities(World w, double dt)
        {
            double t = w.time;
            // Kaien - Divine Seal Storm: the hunting seals, and the shield re-forming
            foreach (var a in w.actors.ToList())
            {
                if (!a.alive || !a.Has("sealstorm", t)) continue;
                if (t >= a.Sv("stormReform", 0))
                {
                    a.sv["stormReform"] = t + SEALSTORM_REFORM;
                    if (!a.shields.Any(s => s.kind == "sealshield" && s.amt > 0)) SealShield(w, a, SEALSTORM_SHIELD * 0.5, a.Sv("stormStart", t) + SEALSTORM_SECS - t);
                }
                if (t < a.Sv("stormNext", 0)) continue;
                a.sv["stormNext"] = t + SEALSTORM_TICK;
                int n = 0;
                foreach (var x in w.Enemies(a))
                {
                    // (training dummies included: they are what the Proving Grounds and the Ult Viewer put in front of him)
                    if (!x.alive || x.IsSummon || x.Has("phased", t)) continue;
                    if (World.Dist3(x.pos, a.pos) > SEALSTORM_R + x.Radius || !w.level.LineOfSight(a.Eye, x.Center)) continue;
                    double dealt = w.Damage(a, x, SEALSTORM_DMG, new DmgOpts { kind = "ability", ability = "sealstorm" });
                    if (dealt <= 0) continue;
                    n++; a.stats["sealstormDmg"] = (a.stats.TryGetValue("sealstormDmg", out var sd) ? sd : 0) + dealt;
                    w.Fx("sealstrike", a.Center, new FxOpts { to = x.Center, color = "#ffe28a", actor = a });
                    w.Fx("sealburst", x.Center, new FxOpts { color = "#ffd27a" });
                }
                // the mending seals: allies in reach are healed each beat
                foreach (var x in w.Allies(a))
                {
                    if (x == a || !x.alive || x.IsSummon || World.Dist3(x.pos, a.pos) > SEALSTORM_R || !w.level.LineOfSight(a.Eye, x.Center)) continue;
                    double got = w.Heal(a, x, SEALSTORM_HEAL);
                    if (got > 0) { a.stats["sealstormHeal"] = (a.stats.TryGetValue("sealstormHeal", out var sh) ? sh : 0) + got; w.Fx("sealmend", x.Center, new FxOpts { actor = x, color = "#9dffb0" }); }
                }
                if (n > 0 && t >= a.Sv("stormSfx", 0)) { a.sv["stormSfx"] = t + 0.8; w.Sfx("talisman", a.Center, a); }
            }
            foreach (var z in w.zones.ToList())
            {
                if (t < z.next) continue;
                z.next = t + 0.25;
                const double k = 0.25;
                if (z.kind == "seal")
                {
                    foreach (var x in w.actors.ToList()) if (x.alive && InZone(z, x))
                        {
                            if (x.team != z.team)
                            {
                                x.Set("sealed", t, 0.35); x.Set("revealed", t, 0.35);
                                w.Damage(z.owner, x, 15 * k, new DmgOpts { kind = "dot", noLifesteal = true });
                            }
                            else w.Heal(z.owner, x, 20 * k, true);
                        }
                }
                else if (z.kind == "sanctuary")
                {
                    foreach (var x in w.Allies(z.owner)) if (InZone(z, x)) { x.Set("undying", t, 0.35); w.Heal(z.owner, x, 60 * k, true); }
                }
                else if (z.kind == "grievous")
                {
                    foreach (var x in w.actors.ToList()) if (x.alive && x.team != z.team && InZone(z, x))
                        {
                            x.Set("antiheal", t, 0.5, null, z.owner);
                            w.Damage(z.owner, x, 10 * k, new DmgOpts { kind = "dot", noLifesteal = true });
                        }
                }
                else if (z.kind == "singularity")
                {
                    foreach (var x in w.actors)
                    {
                        if (!x.alive || x.team == z.team || x.def.frame == "mech" || CcBlocked(w, x)) continue;
                        double d = M.Hypot(x.pos.x - z.x, x.pos.z - z.z);
                        if (d > z.r || d < 0.6) continue;
                        x.forced = new Forced { vx = (z.x - x.pos.x) / d * 7, vy = 0.6, vz = (z.z - x.pos.z) / d * 7, until = t + 0.25, kind = "pull" };
                    }
                    if (z.until - t <= 0.26)
                    {
                        foreach (var x in w.Enemies(z.owner)) if (M.Hypot(x.pos.x - z.x, x.pos.z - z.z) < 6) w.Damage(z.owner, x, 150, Ab);
                        w.Fx("implode", new V3(z.x, z.y + 1, z.z), new FxOpts { r = 6, color = "#ff2244" }); w.Sfx("implode", new V3(z.x, z.y, z.z));
                    }
                }
            }
            // arrow rain: finer tick
            foreach (var z in w.zones.ToList())
            {
                if (z.kind != "arrows") continue;
                double left = ZoneNum(z, "left");
                if (!(left > 0 && t - z.born > (40 - left) * 0.075)) continue;
                left--; z.data["left"] = left;
                double ang = Rng.Random() * Math.PI * 2, r = Math.Sqrt(Rng.Random()) * z.r;
                var p = new V3(z.x + Math.Cos(ang) * r, z.y, z.z + Math.Sin(ang) * r);
                foreach (var x in w.Enemies(z.owner)) if (M.Hypot(x.pos.x - p.x, x.pos.z - p.z) < 1.8 + x.Radius) w.Damage(z.owner, x, 25, Ab);
                w.Fx("arrowhit", p, new FxOpts { color = "#ffd27a" });
                if (left % 4 == 0) w.Sfx("arrowhit", p);
            }
            // Yuzu's sun swarm (Unity divergence, see Hundredsuns): the giant arrows land one by one, then every enemy in reach
            // takes a hit each tick while the swarm lasts (sounds and effects are the view's, from the zone's timeline)
            foreach (var z in w.zones.ToList())
            {
                if (z.kind != "sunswarm") continue;
                z.data ??= new Dictionary<string, object>();
                double landed = ZoneNum(z, "landed");
                while (landed < SUNS_N && t - z.born >= SUNS_LAND_AT + landed * SUNS_LAND_STEP)
                {
                    var lp = SunsLanding(w, z, (int)landed);
                    foreach (var x in w.Enemies(z.owner))
                        if (M.Hypot(x.pos.x - lp.x, x.pos.z - lp.z) < SUNS_LAND_R + x.Radius && Math.Abs(x.pos.y - lp.y) < 3)
                            w.Damage(z.owner, x, SUNS_LAND_DMG, new DmgOpts { kind = "ability", ability = "hundredsuns" });
                    landed++; z.data["landed"] = landed;
                }
                while (z.next <= t && z.next < z.until)
                {
                    z.next += SUNS_TICK;
                    foreach (var x in w.Enemies(z.owner))
                        if (M.Hypot(x.pos.x - z.x, x.pos.z - z.z) < z.r) w.Damage(z.owner, x, SUNS_DMG, new DmgOpts { kind = "ability", ability = "hundredsuns" });
                }
            }
            // Gantetsu: Tachiai Rush shoves (and cracks barriers), the stomp lands, Taiko Heartbeat feeds the team's lifesteal
            foreach (var a in w.actors.ToList())
            {
                if (!a.alive) { a.sv["stompArmed"] = 0; continue; }
                if (a.Has("tachiai", t))
                {
                    var hit = a._rushHit ??= new HashSet<int>();
                    double ry = a.Sv("rushYaw", a.yaw), fx = Math.Sin(ry), fz = Math.Cos(ry);
                    foreach (var x in w.Enemies(a))
                    {
                        if (hit.Contains(x.id) || M.Hypot(x.pos.x - a.pos.x, x.pos.z - a.pos.z) > a.Radius + x.Radius + 0.5 || Math.Abs(x.pos.y - a.pos.y) > 2.2) continue;
                        hit.Add(x.id);
                        w.Damage(a, x, 30, Ab); Weapons.Ignite(w, a, x, 6);
                        w.Fx("impact", x.Center, new FxOpts { color = a.def.glow }); w.Sfx("punch", x.Center, a);
                        if (CcBlocked(w, x) || x.def.frame == "mech" || x.isBoss) continue;
                        // shoved aside, off the charge line
                        double side = (x.pos.x - a.pos.x) * -fz + (x.pos.z - a.pos.z) * fx >= 0 ? 1 : -1;
                        x.vel.y = Math.Max(x.vel.y, 3.5); x.grounded = false;
                        x.forced = new Forced { vx = -fz * side * 9 + fx * 6, vy = 0, vz = fx * side * 9 + fz * 6, until = t + 0.3, kind = "knock" };
                    }
                    // COUNTER: ploughing into the Solar Bulwark cracks it
                    foreach (var b in w.actors)
                    {
                        if (!b.alive || b.team == a.team || !b.barrier.up || hit.Contains(-b.id)) continue;
                        var bf = b.Forward(); var c = new V3(b.pos.x + bf.x * 1.7 * b.scale, b.pos.y + 1.5, b.pos.z + bf.z * 1.7 * b.scale);
                        if (M.Hypot(c.x - a.pos.x, c.z - a.pos.z) > a.Radius + 1.3) continue;
                        hit.Add(-b.id);
                        w.HitBarrier(b, 300, a, c);
                        w.Counter(a, b, "Tachiai Rush cracks the Solar Bulwark");
                    }
                }
                else if (a.Sv("rushOn", 0) != 0)
                {
                    // the charge ran its full course (not cut short with SHIFT): it ends in the leap by itself
                    a.sv["rushOn"] = 0;
                    if (t - a.Sv("rushStart", 0) >= RUSH_T - 0.05 && a.grounded && !a.Has("stun", t) && !a.Has("root", t)) StompLeap(w, a);
                }
                if (a.Sv("stompArmed", 0) != 0)
                {
                    if (a.grounded && t - a.anim.jumpAt > 0.1) { a.sv["stompArmed"] = 0; a.Clear("stompair"); ShikoStomp(w, a); }
                    else if (!a.Has("stompair", t)) a.sv["stompArmed"] = 0;
                }
                if (a.Has("taiko", t))
                {
                    foreach (var x in w.Allies(a)) if (World.Dist3(x.pos, a.pos) < 12) x.Set("lifesteal", t, 0.3, x == a ? 1 : 0.5);
                    if (t - a.Sv("taikoBeat", 0) > 0.5) { a.sv["taikoBeat"] = t; w.Fx("taikopulse", a.Center, new FxOpts { actor = a, color = "#ffb35c", r = 12 }); w.Sfx("taikobeat", a.Center, a); }
                }
            }
            // Hibiki: the track he's playing reaches every ally within 12m he can see; Max Volume cranks it; the drop lands
            foreach (var a in w.actors.ToList())
            {
                if (a.def.id != "hibiki") continue;
                if (!a.alive) { a.sv["dropArmed"] = 0; continue; }
                bool amp = a.Has("amp", t), speedTrack = a.Sv("track", 0) != 0;
                foreach (var x in w.Allies(a))
                {
                    if (World.Dist3(x.pos, a.pos) > AURA_R || (x != a && !w.level.LineOfSight(a.Eye, x.Center))) continue;
                    if (speedTrack)
                    {
                        double kk = amp ? 1.6 : 1.25;
                        x.sv["speed"] = x.Has("speed", t) && x.Sv("speedFrom", double.NaN) != a.id ? Math.Max(x.Sv("speed", 1), kk) : kk;
                        x.sv["speedFrom"] = a.id; x.Set("speed", t, 0.3); x.Set("tempo", t, 0.3, amp ? 2 : 1);
                    }
                    else
                    {
                        w.Heal(a, x, (amp ? 56 : 20) * (x == a ? 0.6 : 1) * dt, true); x.Set("groove", t, 0.3, amp ? 2 : 1);
                    }
                }
                if (a.Has("grinding", t))
                {
                    a.sv["grind"] = a.Sv("grind", 0) + dt;
                    if (a.sv["grind"] >= 5 && !a.Has("pumped", t)) { a.Set("pumped", t, 30); w.Sfx("pumped", a.Center, a); }
                }
                if (a.Sv("dropArmed", 0) != 0)
                {
                    double since = t - a.Sv("dropAt", t);
                    if ((a.grounded && since > 0.12) || (a.vel.y < 0 && since > 0.35) || !a.Has("dropair", t)) { a.sv["dropArmed"] = 0; a.Clear("dropair"); BassDropLand(w, a); }
                }
            }
            // Tomoe: the Crescent Fang rides whoever it is stuck in, comes home on its own after a while, cuts its way back
            foreach (var a in w.actors.ToList())
            {
                if (a.def.id != "tomoe") continue;
                double st = a.Sv("fang", 0);
                if (!a.alive) { if (st != 0) { a.sv["fang"] = 0; a.sv["fangTgt"] = 0; } continue; }
                if (st != 0) a.cd["crescent"] = Math.Max(a.cd.TryGetValue("crescent", out var cc) ? cc : 0, t + 0.1);            // no cooldown ticks while the blade is out
                if (st == 1 && !w.projs.Any(q => q.id == a.Sv("fangProj", 0))) { if (a.Sv("fang", 0) == 1) a.sv["fang"] = 0; continue; }
                if (st == 3)
                {
                    var x = w.actors.FirstOrDefault(o => o.id == a.Sv("fangTgt", 0));
                    if (x == null || !x.alive) { var c = x != null ? x.Center : a.Center; a.sv["fangX"] = c.x; a.sv["fangY"] = c.y; a.sv["fangZ"] = c.z; a.sv["fang"] = 2; a.sv["fangAt"] = t; }
                    else if (t - a.Sv("fangAt", t) > FANG_STICK) RecallFang(w, a);
                }
                else if (st == 2 && t - a.Sv("fangAt", t) > FANG_STICK + 2) RecallFang(w, a);
                else if (st == 4)
                {
                    var home = new V3(a.pos.x, a.pos.y + a.Height * 0.6, a.pos.z);
                    var from = new V3(a.Sv("fangX", 0), a.Sv("fangY", 0), a.Sv("fangZ", 0));
                    var v = home - from; double l = v.Length, step = FANG_BACK * dt;
                    if (l <= step + 0.6) { FangHome(w, a); continue; }
                    var to = new V3(from.x + v.x / l * step, from.y + v.y / l * step, from.z + v.z / l * step);
                    var cut = a._fangBack ??= new HashSet<int>();
                    foreach (var x in w.Enemies(a))
                    {
                        if (cut.Contains(x.id)) continue;
                        var c = x.Center; var q = c - from;
                        double s2 = Math.Max(0, Math.Min(step, V3.Dot(q, v) / l));
                        double ddx = from.x + v.x / l * s2 - c.x, ddy = from.y + v.y / l * s2 - c.y, ddz = from.z + v.z / l * s2 - c.z;
                        if (ddx * ddx + ddz * ddz > (x.Radius + 0.5) * (x.Radius + 0.5) || Math.Abs(ddy) > x.Height * 0.55) continue;
                        cut.Add(x.id);
                        w.Damage(a, x, FANG_DMG, Ab); Weapons.Wound(w, a, x, FANG_WOUND);
                        w.Fx("slash", c, new FxOpts { color = a.def.glow }); w.Sfx("chainhit", c, a);
                    }
                    a.sv["fangX"] = to.x; a.sv["fangY"] = to.y; a.sv["fangZ"] = to.z;
                }
            }
            foreach (var z in w.zones)
            {
                if (z.kind != "dohyo" || t >= z.until) continue;
                if (!z.owner.alive) { z.until = t; continue; }
                // the chains: everyone caught stays bound, and an enemy who gets inside the rope (dropped in from above) is bound too
                var trapped = ZoneData<List<int>>(z, "trapped");
                foreach (var x in w.Enemies(z.owner))
                {
                    if (!x.alive) continue;
                    if (!trapped.Contains(x.id))
                    {
                        if (M.Hypot(x.pos.x - z.x, x.pos.z - z.z) > z.r - x.Radius - 0.3 || x.pos.y < z.y - 2 || x.pos.y > z.y + 6) continue;
                        trapped.Add(x.id); w.Sfx("chainhit", x.Center, z.owner);
                    }
                    Leash(w, z.owner, x);
                }
            }
            // dashes that interact with enemies on the way
            foreach (var a in w.actors.ToList())
            {
                if (!a.alive || a.forced == null) continue;
                if (a.forced.kind == "flashstep")
                {
                    var hit = a._dashHit ?? new HashSet<int>();   // TS-PARITY: an unset set is not stored back
                    foreach (var x in w.Enemies(a)) if (!hit.Contains(x.id) && World.Dist3(x.Center, a.Center) < 1.6 + x.Radius) { hit.Add(x.id); w.Damage(a, x, 50, Ab); w.Fx("slash", x.Center, new FxOpts { color = "#8ad8ff" }); }
                }
                else if (a.forced.kind == "dawncharge") DawnchargeTick(w, a, dt, t);
                else if (a.forced.kind == "tide") TideTick(w, a, dt, t);
                else if (a.forced.kind == "abysscharge")
                {
                    var pin = w.actors.FirstOrDefault(x => x.id == a.Sv("pinned", 0));
                    if (pin == null)
                    {
                        pin = w.Enemies(a).FirstOrDefault(x => World.Dist3(x.pos, a.pos) < a.Radius + x.Radius + 0.6 && Math.Abs(x.pos.y - a.pos.y) < 2);
                        if (pin != null)
                        {
                            if (CcBlocked(w, pin) || pin.def.frame == "mech") { w.Damage(a, pin, 60, Ab); a.forced.until = t; pin = null; }
                            else { a.sv["pinned"] = pin.id; w.Sfx("pin", pin.Center, a); }
                        }
                    }
                    if (pin != null && pin.alive) CarryPin(w, a, pin, t);
                }
            }
        }

        static void CarryPin(World w, Actor a, Actor pin, double t)
        {
            var f = a.Forward();
            pin.pos = new V3(a.pos.x + f.x * (a.Radius + pin.Radius + 0.2), a.pos.y, a.pos.z + f.z * (a.Radius + pin.Radius + 0.2));
            pin.vel = V3.Zero; pin.Set("stun", t, 0.1);
            w.level.Collide(ref pin.pos, pin.Radius, pin.Height);
        }

        static void DawnchargeTick(World w, Actor a, double dt, double t)
        {
            // steer toward the aim (slowly: a charging mech carries its momentum)
            double cur = a.Sv("chargeYaw", a.yaw);
            double dy = a.input.yaw - cur; while (dy > Math.PI) dy -= 2 * Math.PI; while (dy < -Math.PI) dy += 2 * Math.PI;
            double ny = cur + Math.Max(-1.3 * dt, Math.Min(1.3 * dt, dy));
            a.sv["chargeYaw"] = ny; a.yaw = ny;
            double sp = M.Hypot(a.forced.vx, a.forced.vz);
            a.forced.vx = Math.Sin(ny) * sp; a.forced.vz = Math.Cos(ny) * sp;
            var hit = a._chargeHit ?? new HashSet<int>();
            var pin = w.actors.FirstOrDefault(x => x.id == a.Sv("pinned", 0));
            foreach (var x in w.Enemies(a))
            {
                if (x == pin || hit.Contains(x.id) || World.Dist3(x.pos, a.pos) > a.Radius + x.Radius + 0.7 || Math.Abs(x.pos.y - a.pos.y) > 2.5 * a.scale) continue;
                hit.Add(x.id);
                if (x.forced?.kind == "abysscharge")
                {
                    // COUNTER: two charging mechs meet head-on - the Abyss Charge breaks, Gorgoth is dazed
                    Interrupt(w, x, a); ApplyCC(w, a, x, "stun", 1.2); w.Damage(a, x, 80, Ab);
                    w.Counter(a, x, "Dawn Charge breaks the Abyss Charge head-on");
                    w.Fx("slam", x.pos, new FxOpts { r = 3, color = "#ffd76a", actor = a }); w.Sfx("slam", x.pos, a);
                    a.forced.until = t; break;
                }
                if (x.Has("tachiai", t))
                {
                    // COUNTER (Gantetsu): an unstoppable rush can't be pinned - the two heavyweights collide and the charge breaks
                    a.forced.until = t; w.Damage(a, x, 30, Ab);
                    w.Counter(x, a, "Tachiai Rush can't be pinned");
                    w.Fx("slam", x.pos, new FxOpts { r = 2.5, color = x.def.glow, actor = x }); w.Sfx("slam", x.pos, a);
                    break;
                }
                if (pin == null && !CcBlocked(w, x) && x.def.frame != "mech" && !x.isBoss) { pin = x; a.sv["pinned"] = x.id; w.Sfx("pin", x.Center, a); continue; }
                if (x.def.frame == "mech" || x.isBoss) { w.Damage(a, x, 60, Ab); a.forced.until = t; w.Fx("slam", x.pos, new FxOpts { r = 2, color = "#ffd76a" }); break; }
                // knocked aside, away from the charge line
                var f = a.Forward(); double side = (x.pos.x - a.pos.x) * -f.z + (x.pos.z - a.pos.z) * f.x >= 0 ? 1 : -1;
                w.Damage(a, x, 30, Ab);
                if (!CcBlocked(w, x)) x.forced = new Forced { vx = -f.z * side * 11 + f.x * 5, vy = 4, vz = f.x * side * 11 + f.z * 5, until = t + 0.3, kind = "knock" };
            }
            a._chargeHit = hit;
            if (pin != null && pin.alive) CarryPin(w, a, pin, t);
        }

        static void TideTick(World w, Actor a, double dt, double t)
        {
            // Crescent Warpath: each enemy under the wheel of blades is passed through once - cut, wounded, marked
            var hit = a._tideHit ??= new HashSet<int>();
            var f = World.Norm(new V3(a.forced.vx, 0, a.forced.vz));
            bool live = a.sv.ContainsKey("tideX") && a.forced.until < 1e8;        // (the Hero Viewer holds the pose without a dash)
            bool air = live && a.forced.ignoreGravity && a.Sv("tideApex", 0) > 0;
            if (live)
            {
                double s = (t - a.sv["tideT0"]) / a.sv["tideDur"];
                // the arc, if it flies: up over the first half, down over the second
                if (air) a.forced.vy = 4 * a.sv["tideApex"] * (1 - 2 * s) / a.sv["tideDur"];
                // stopped by a wall (or the rope of a Grand Dohyo), or a flight down early on higher ground: it ends there
                double want = M.Hypot(a.forced.vx, a.forced.vz) * dt, went = M.Hypot(a.pos.x - a.sv["tideX"], a.pos.z - a.sv["tideZ"]);
                a.sv["tideStuck"] = want > 0.01 && went < want * 0.25 ? a.Sv("tideStuck", 0) + 1 : 0;
                a.sv["tideX"] = a.pos.x; a.sv["tideZ"] = a.pos.z;
                if (a.sv["tideStuck"] >= 3 || (air && a.grounded && s > 0.5)) a.forced.until = t;
            }
            foreach (var x in w.Enemies(a))
            {
                if (hit.Contains(x.id) || x.Has("phased", t)) continue;
                double vx = x.pos.x - a.pos.x, vz = x.pos.z - a.pos.z, along = vx * f.x + vz * f.z, lat = Math.Abs(vx * -f.z + vz * f.x);
                if (along < -a.Radius - x.Radius - 0.5 || along > a.Radius + x.Radius + 1.4 || lat > TIDE_HALF + x.Radius) continue;
                // (if it flies she is up to TIDE_APEX above the floor they stand on)
                if (x.pos.y + x.Height < a.pos.y - TIDE_APEX - 1.5 || x.pos.y > a.pos.y + a.Height + 1) continue;
                if (live && !w.level.LineOfSight(a.Center, x.Center)) continue;
                hit.Add(x.id);
                x.Set("tidemark", t, TIDE_MARK, null, a);
                w.Damage(a, x, TIDE_CUT, Ab); Weapons.Wound(w, a, x, TIDE_WOUND); x.Set("antiheal", t, TIDE_ANTIHEAL, null, a);
                w.Fx("slash", x.Center, new FxOpts { color = a.def.glow }); w.Fx("tidemark", x.Center, new FxOpts { color = TIDE_MARK_COLOR, actor = x, dur = TIDE_MARK }); w.Sfx("reaping", x.Center, a);
                a.stats["tide"] = (a.stats.TryGetValue("tide", out var td) ? td : 0) + 1;
            }
        }
    }
}
