// World projectiles, barriers and the Grand Dohyo ring wall. Port of zenith-umbra src/game/World.ts ("projectiles").
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZU.Sim
{
    public partial class World
    {
        public Proj SpawnProj(Actor owner, V3 from, V3 dir, double speed, ProjOpts o)
        {
            var p = new Proj
            {
                id = PID++, owner = owner, team = owner.team, pos = from, vel = new V3(dir.x * speed, dir.y * speed, dir.z * speed),
                dmg = 0, splash = 0, heal = false, fx = "sun", life = 2, r = 0.12, grav = 0, crit = 1.5, born = time,
            };
            if (o != null)
            {
                if (o.dmg.HasValue) p.dmg = o.dmg.Value; if (o.splash.HasValue) p.splash = o.splash.Value; if (o.life.HasValue) p.life = o.life.Value;
                if (o.r.HasValue) p.r = o.r.Value; if (o.grav.HasValue) p.grav = o.grav.Value; if (o.crit.HasValue) p.crit = o.crit.Value;
                if (o.heal.HasValue) p.heal = o.heal.Value; if (o.pierce.HasValue) p.pierce = o.pierce.Value; if (o.fx != null) p.fx = o.fx;
                p.special = o.special; p.homing = o.homing; p.bounce = o.bounce; p.seek = o.seek; p.mesh = o.mesh; p.spin = o.spin;
            }
            projs.Add(p);
            return p;
        }

        /// <summary>A ricochet shuriken looks for its next prey: the nearest enemy within `seek` m of the thrower it hasn't
        /// cut yet, in its line of sight. It snaps toward them and keeps turning on them (SEEK_TURN). false = nothing to hunt.</summary>
        bool SeekNext(Proj p)
        {
            var o = p.owner; double t = time;
            Actor best = null; double bd = double.PositiveInfinity;
            foreach (var x in actors)
            {
                if (!x.alive || x == o || x.team == p.team || p.hits.Contains(x.id) || x.Has("phased", t) || x.Has("spawnprot", t)) continue;
                double d = Dist3(x.pos, o.pos);
                if (d > (p.seek ?? 0) || !level.LineOfSight(p.pos, x.Center)) continue;
                if (d < bd) { bd = d; best = x; }
            }
            if (best == null) { p.seekTgt = 0; return false; }
            double sp = M.Hypot(p.vel.x, p.vel.y, p.vel.z); var c = best.Center; var v = Norm(new V3(c.x - p.pos.x, c.y - p.pos.y, c.z - p.pos.z));
            p.vel = new V3(v.x * sp, v.y * sp, v.z * sp); p.seekTgt = best.id; p.life = Math.Max(p.life, SEEK_LIFE);
            return true;
        }

        static V3 Steer(V3 vel, V3 toward, double k)
        {
            double sp = M.Hypot(vel.x, vel.y, vel.z);
            var nv = Norm(new V3(vel.x / sp * (1 - k) + toward.x * k, vel.y / sp * (1 - k) + toward.y * k, vel.z / sp * (1 - k) + toward.z * k));
            return new V3(nv.x * sp, nv.y * sp, nv.z * sp);
        }

        bool StepProj(Proj p, double dt)
        {
            p.life -= dt;
            if (p.life <= 0) { if (p.special != null) ProjEnd(p, p.pos, null); return false; }
            // a hunting shuriken keeps turning on its prey; prey lost (dead, cut already, out of sight), it looks for the next
            if (p.seekTgt.HasValue && p.seekTgt.Value != 0)
            {
                var x = actors.FirstOrDefault(a2 => a2.id == p.seekTgt.Value);
                if (x != null && x.alive && !p.hits.Contains(x.id) && !x.Has("phased", time))
                {
                    var c = x.Center; var v = Norm(new V3(c.x - p.pos.x, c.y - p.pos.y, c.z - p.pos.z));
                    p.vel = Steer(p.vel, v, Math.Min(1, SEEK_TURN * dt));
                }
                else if (!SeekNext(p)) p.life = Math.Min(p.life, 0.35);
            }
            if (p.homing.HasValue && p.homing.Value != 0)
            {
                var cand = actors.Where(x => x.alive && (p.heal ? x.team == p.team && x != p.owner && x.Health < x.MaxHp : x.team != p.team)).ToList();
                double sp = M.Hypot(p.vel.x, p.vel.y, p.vel.z);
                Actor best = null; double bd = 0.97;
                foreach (var x in cand)
                {
                    var c = x.Center; var v = new V3(c.x - p.pos.x, c.y - p.pos.y, c.z - p.pos.z); double l = M.Hypot(v.x, v.y, v.z);
                    double dot = (v.x * p.vel.x + v.y * p.vel.y + v.z * p.vel.z) / (l * sp);
                    if (dot > bd && l < 30) { bd = dot; best = x; }
                }
                if (best != null)
                {
                    var c = best.Center; var v = Norm(new V3(c.x - p.pos.x, c.y - p.pos.y, c.z - p.pos.z));
                    p.vel = Steer(p.vel, v, Math.Min(1, p.homing.Value * dt));
                }
            }
            p.vel.y -= p.grav * dt;
            V3 a = p.pos, b = new V3(a.x + p.vel.x * dt, a.y + p.vel.y * dt, a.z + p.vel.z * dt);
            double len = M.Hypot(b.x - a.x, b.y - a.y, b.z - a.z);
            var dir = new V3((b.x - a.x) / len, (b.y - a.y) / len, (b.z - a.z) / len);
            // barriers (Solar Bulwark) block enemy projectiles
            var bh = BarrierHit(p.team, a, dir, len);
            var lh = level.Ray(a, dir, len);
            double tEnd = Math.Min(len, Math.Min(lh.HasValue ? lh.Value.t : len, bh != null ? bh.t : len));
            // actors
            foreach (var x in actors.ToList())
            {
                if (!x.alive || x == p.owner || p.hits.Contains(x.id) || x.Has("phased", time)) continue;
                bool friendly = x.team == p.team;
                if (p.heal ? !friendly : friendly) continue;
                double hr = HeadR(x); var hc = HeadC(x);
                var th = RaySphere(a, dir, hc, hr + p.r);
                double? hitT = th.HasValue && th.Value <= tEnd ? th : null; bool head = hitT.HasValue;
                if (!hitT.HasValue)
                {
                    double r = x.Radius * 0.9 + p.r;
                    var s = SegSeg(a, new V3(a.x + dir.x * tEnd, a.y + dir.y * tEnd, a.z + dir.z * tEnd), new V3(x.pos.x, x.pos.y + 0.2, x.pos.z), new V3(x.pos.x, x.pos.y + x.Height - hr, x.pos.z));
                    if (s.d2 <= r * r) hitT = s.s * tEnd;
                }
                if (!hitT.HasValue) continue;
                var hitAt = new V3(a.x + dir.x * hitT.Value, a.y + dir.y * hitT.Value, a.z + dir.z * hitT.Value);
                // Mirror Water (Genji's Deflect): a shot from in front of him leaves again along his aim, now his
                if (!friendly && x.Has("deflect", time) && !p.heal && Deflected(x, p.pos, hitAt))
                {
                    double sp = M.Hypot(p.vel.x, p.vel.y, p.vel.z); V3 d = AimDir(x), e = x.Eye;
                    p.owner = x; p.team = x.team; p.vel = new V3(d.x * sp, d.y * sp, d.z * sp); p.hits.Clear(); p.life = Math.Max(p.life, 1);
                    p.pos = new V3(e.x + d.x * 0.5, e.y - 0.1 + d.y * 0.5, e.z + d.z * 0.5);
                    return true;
                }
                // Thunder Parry: reflect
                if (!friendly && x.Has("parry", time) && !p.heal)
                {
                    p.owner = x; p.team = x.team; p.vel = -p.vel; p.hits.Clear(); p.life = Math.Max(p.life, 1);
                    Sfx("parry", x.Center); Fx("parry", x.Center, new FxOpts { color = "#8ad8ff", actor = x });
                    if (p.special == "chain") Counter(x, p.owner, "Thunder Parry reflects the Chain of Oblivion");   // TS-PARITY: owner already swapped to x
                    return true;
                }
                if (p.heal) { Heal(p.owner, x, p.dmg); Fx("healhit", hitAt, new FxOpts { color = p.owner.def.glow }); Sfx("healhit", hitAt); }
                else if (p.special != null) { ProjEnd(p, hitAt, x); return false; }
                else
                {
                    p.owner.hits++; if (head) p.owner.crits++;
                    Damage(p.owner, x, p.dmg * (head ? p.crit : 1), new DmgOpts { crit = head, kind = "proj" });
                    Fx("hit", hitAt, new FxOpts { color = p.owner.def.glow });
                    Sfx(head ? "crit" : "hit", hitAt);
                }
                if (p.splash != 0) Splash(p, hitAt, x);
                p.hits.Add(x.id);
                // a ricochet shuriken out of a body: on to the next enemy in the thrower's perimeter, SEEK_DMG of the damage
                if (p.seek.HasValue && p.seek.Value != 0 && !p.heal)
                {
                    if (!SeekNext(p)) return false;
                    p.dmg *= SEEK_DMG; p.pos = hitAt;
                    Fx("hit", hitAt, new FxOpts { color = p.owner.def.glow }); Sfx("shuriken", hitAt);
                    return true;
                }
                if (!p.pierce) return false;
            }
            if (tEnd < len)
            {
                var hp = new V3(a.x + dir.x * tEnd, a.y + dir.y * tEnd, a.z + dir.z * tEnd);
                // a ricochet shuriken skips off the wall (mirrored on its face) and turns on the nearest enemy in the perimeter
                if (p.bounce.HasValue && p.bounce.Value != 0 && lh.HasValue && !(bh != null && bh.t <= tEnd + 1e-6))
                {
                    p.bounce--; p.bounced = (p.bounced ?? 0) + 1;
                    var n = new V3(lh.Value.nx, lh.Value.ny, lh.Value.nz); double k = 2 * (p.vel.x * n.x + p.vel.y * n.y + p.vel.z * n.z);
                    p.vel = new V3(p.vel.x - k * n.x, p.vel.y - k * n.y, p.vel.z - k * n.z);
                    p.pos = new V3(hp.x + n.x * 0.08, hp.y + n.y * 0.08, hp.z + n.z * 0.08);
                    Fx("impact", hp, new FxOpts { color = p.owner.def.glow, mat = lh.Value.mat, n = n }); Sfx("impact_metal", hp);
                    SeekNext(p);
                    return true;
                }
                if (bh != null && bh.t <= tEnd + 1e-6) HitBarrier(bh.owner, p.dmg, p.owner, hp);
                else Fx("impact", hp, new FxOpts { color = p.owner.def.glow });
                if (p.special != null) ProjEnd(p, hp, null);
                else if (p.splash != 0) Splash(p, hp, null);
                return false;
            }
            p.pos = b;
            return true;
        }

        void Splash(Proj p, V3 at, Actor direct)
        {
            foreach (var x in Enemies(p.owner))
            {
                if (x == direct) continue;
                double d = Dist3(x.Center, at);
                if (d < p.splash + x.Radius) Damage(p.owner, x, p.dmg * 0.5 * (1 - d / (p.splash + x.Radius) * 0.5), new DmgOpts { kind = "splash" });
            }
            Fx("burst", at, new FxOpts { r = p.splash, color = p.owner.def.glow });
            Sfx("boom", at);
        }

        void ProjEnd(Proj p, V3 at, Actor hit) => Abilities.OnProj(this, p, at, hit);

        public class BarrierHitInfo { public double t; public Actor owner; }

        public BarrierHitInfo BarrierHit(string team, V3 o, V3 d, double max)
        {
            BarrierHitInfo best = null;
            // Grand Dohyo: a sacred rope wall around the ring - enemy fire can't cross it in either direction
            foreach (var z in zones)
            {
                if (z.kind != "dohyo" || z.team == team) continue;
                double ox = o.x - z.x, oz = o.z - z.z, A = d.x * d.x + d.z * d.z;
                if (A < 1e-8) continue;
                double B = 2 * (ox * d.x + oz * d.z), C = ox * ox + oz * oz - z.r * z.r, disc = B * B - 4 * A * C;
                if (disc < 0) continue;
                double s = Math.Sqrt(disc);
                foreach (var tt in new[] { (-B - s) / (2 * A), (-B + s) / (2 * A) })
                {
                    if (tt < 0 || tt > max || (best != null && tt > best.t)) continue;
                    double y = o.y + d.y * tt;
                    if (y < z.y - 1 || y > z.y + RING_H) continue;
                    best = new BarrierHitInfo { t = tt, owner = z.owner }; break;
                }
            }
            foreach (var a in actors)
            {
                if (!a.alive || !a.barrier.up || a.team == team) continue;
                var f = a.Forward(); double k = a.scale; var c = new V3(a.pos.x + f.x * 1.7 * k, a.pos.y, a.pos.z + f.z * 1.7 * k);
                double den = d.x * f.x + d.z * f.z;
                if (den >= -1e-4) continue;                 // only blocks shots coming at its face
                double t = ((c.x - o.x) * f.x + (c.z - o.z) * f.z) / den;
                if (t < 0 || t > max || (best != null && t > best.t)) continue;
                double hx = o.x + d.x * t - c.x, hz = o.z + d.z * t - c.z, hy = o.y + d.y * t - c.y;
                double lateral = Math.Abs(hx * -f.z + hz * f.x);
                if (lateral > 2.4 * k || hy < -0.2 || hy > 3.8 * k) continue;
                best = new BarrierHitInfo { t = t, owner = a };
            }
            return best;
        }

        public void HitBarrier(Actor owner, double dmg, Actor src, V3 at)
        {
            // no shield of its own: the shot struck a Grand Dohyo wall (it has no health, it just lasts its 6 seconds)
            if (owner.barrier.max == 0) { Fx("ringhit", at, new FxOpts { color = "#ffe6a8" }); owner.mitigated += dmg; return; }
            owner.barrier.hp -= dmg; owner.mitigated += Math.Max(0, dmg + Math.Min(0, owner.barrier.hp));
            owner.barrier.regenAt = time + 2;
            Fx("barrierhit", at, new FxOpts { color = owner.def.glow, actor = owner });
            Sfx("barrierhit", at);
            if (owner.barrier.hp <= 0)
            {
                owner.barrier.hp = 0; owner.barrier.up = false; owner.barrier.brokenUntil = time + 5;
                Sfx("barrierbreak", at); Fx("barrierbreak", at, new FxOpts { color = owner.def.glow, actor = owner });
                if (src.def.id == "gorgoth") Counter(src, owner, "Null Lance shatters the Solar Bulwark");
            }
        }
    }
}
