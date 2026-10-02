// Primary / secondary fire for every hero. Port of zenith-umbra src/game/weapons.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static class Weapons
    {
        static V3 SpreadDir(V3 d, double s)
        {
            if (s == 0) return d;
            // random direction inside a cone of half-angle s (u drawn first, then r: the TS order)
            double u = Rng.Random() * 2 * Math.PI, r = Math.Sqrt(Rng.Random()) * s;
            var up = Math.Abs(d.y) < 0.95 ? new V3(0, 1, 0) : new V3(1, 0, 0);
            var a = World.Norm(new V3(d.y * up.z - d.z * up.y, d.z * up.x - d.x * up.z, d.x * up.y - d.y * up.x));
            var b = new V3(d.y * a.z - d.z * a.y, d.z * a.x - d.x * a.z, d.x * a.y - d.y * a.x);
            double cu = Math.Cos(u) * r, su = Math.Sin(u) * r;
            return World.Norm(new V3(d.x + a.x * cu + b.x * su, d.y + a.y * cu + b.y * su, d.z + a.z * cu + b.z * su));
        }

        static void BreakStealth(World w, Actor a)
        {
            if (a.Has("stealth", w.time)) { a.Clear("stealth"); a.Set("ambush", w.time, 0.6); w.Sfx("unveil", a.pos, a); }
        }

        /// <summary>everything hostile inside a frontal wedge (cosMin = cos of the half-angle) gets hit once; returns the hit count</summary>
        static int MeleeArc(World w, Actor a, double reach, double dmg, double cosMin, double vert, Action<Actor> onHit = null)
        {
            var f = a.Forward();
            int n = 0;
            a.shots++;
            foreach (var x in w.Enemies(a))
            {
                double vx = x.pos.x - a.pos.x, vz = x.pos.z - a.pos.z, l = M.Hypot(vx, vz);
                if (l > reach + x.Radius || Math.Abs(x.pos.y - a.pos.y) > vert) continue;
                if ((vx * f.x + vz * f.z) / (l == 0 ? 1 : l) < cosMin && l > x.Radius) continue;
                w.Damage(a, x, dmg, new DmgOpts { kind = "melee" }); n++;
                w.Fx("slash", x.Center, new FxOpts { color = a.def.glow });
                onHit?.Invoke(x);
            }
            if (n > 0) a.hits++;
            return n;
        }

        public const double QUICK_MELEE_DAMAGE = 40, QUICK_MELEE_COOLDOWN = 0.9;

        /// <summary>Heat from incendiary rounds / Tachiai Rush: builds on the target, bleeds off at 5/s, and at 10 the target
        /// catches fire for 2.5s (16 dmg/s) - and becomes a critical target for Gantetsu's volatile chaingun.</summary>
        public static void Ignite(World w, Actor src, Actor x, double heat)
        {
            double t = w.time;
            double h = Math.Max(0, x.Sv("heat", 0) - (t - x.Sv("heatAt", t)) * 5) + heat;
            x.sv["heat"] = h; x.sv["heatAt"] = t;
            if (h < 10) return;
            x.sv["heat"] = 0;
            if (!x.Has("burning", t)) { w.Fx("ignite", x.Center, new FxOpts { actor = x, color = "#ff8a3d" }); w.Sfx("ignite", x.Center); src.stats["ignites"] = (src.stats.TryGetValue("ignites", out var n) ? n : 0) + 1; }
            x.Set("burning", t, 2.5, null, src);
        }

        /// <summary>A wound: `total` damage bled over `dur` seconds. Wounds stack; packs and cleanses close them.</summary>
        public static void Wound(World w, Actor src, Actor x, double total, double dur = 3)
        {
            if (!x.alive || x.team == src.team) return;
            x.wounds.Add(new Wound { src = src, dps = total / dur, until = w.time + dur });
            x.Set("wound", w.time, dur, null, src);
            w.Fx("wound", x.Center, new FxOpts { actor = x, color = "#ff2d55" });
            // COUNTER: armor is no answer to a wound
            if (src.def.id == "tomoe" && x.def.id == "gantetsu" && x.armor > 0 && w.time - x.Sv("woundCounterAt", -99) > 12)
            {
                x.sv["woundCounterAt"] = w.time;
                w.Counter(src, x, "Tomoe's wounds bleed through Gantetsu's armor");
            }
        }

        /// <summary>C: every hero's quick melee - a short jab in front of them</summary>
        public static void QuickMelee(World w, Actor a)
        {
            double t = w.time;
            a.nextMelee = t + QUICK_MELEE_COOLDOWN;
            a.anim.attackAt = t; a.anim.attackKind = "punch";
            BreakStealth(w, a);
            // Tomoe: with the Crescent Fang in hand, the jab cuts - a wound (15 over 3s)
            bool cut = a.def.id == "tomoe" && a.Sv("fang", 0) == 0;
            int n = MeleeArc(w, a, 1.2 + a.Radius * 1.3, QUICK_MELEE_DAMAGE, 0.45, 2 * a.scale, cut ? (Action<Actor>)(x => Wound(w, a, x, 15)) : null);
            w.Sfx(n > 0 ? "punch" : "whiff", a.Center, a);
            if (n > 0) w.Fx("impact", new V3(a.pos.x + a.Forward().x * (a.Radius + 0.9), a.pos.y + a.Height * 0.6, a.pos.z + a.Forward().z * (a.Radius + 0.9)), new FxOpts { color = a.def.glow });
        }

        /// <param name="fallFrom">where hitscan damage starts falling off, as a fraction of the weapon's range</param>
        public static void Fire(World w, Actor a, SlotDef W, string slot, double mult = 1, double spreadMult = 1, double fallFrom = 0.5)
        {
            double t = w.time;
            bool dual = a.def.dualGuns;
            // twin chainguns: each hand has its own fire cue (the animator kicks that gun), and neither is a "secondary swing"
            a.anim.attackAt = t; a.anim.attackKind = dual ? "primary" : slot;
            if (dual) { if (slot == "primary") a.anim.fireL = t; else a.anim.fireR = t; }
            BreakStealth(w, a);
            var muz = w.Muzzle(a, slot);
            var aim = w.AimPoint(a, W.range * 1.5);
            var bas = World.Norm(new V3(aim.x - muz.x, aim.y - muz.y, aim.z - muz.z));
            if (W.kind == "projectile" || W.kind == "charge")
            {
                int n = W.pellets ?? 1;
                if (!W.heal) a.shots += n;
                for (int i = 0; i < n; i++)
                {
                    var d = n > 1 ? SpreadDir(bas, (W.spread ?? 0.03) * (i == 0 ? 0 : 1)) : bas;
                    var o = new ProjOpts
                    {
                        dmg = W.damage * mult, splash = W.splash ?? 0, heal = W.heal, fx = W.fx, life = W.range / (W.speed ?? 50) * 1.3,
                        r = W.heal ? 0.35 : (W.splash ?? 0) != 0 ? 0.2 : 0.1, crit = W.kind == "charge" && mult > 0.99 ? 2 : 1.5,
                        homing = W.heal ? 5 : a.def.id == "kaien" ? 1.2 : 0, grav = 0,
                    };
                    if (W.mesh != null) { o.mesh = W.mesh; o.spin = W.spin ?? 30; }                       // a prop projectile (Hayate's shuriken)
                    if ((W.bounce ?? 0) != 0) { o.bounce = W.bounce; o.seek = W.seek ?? 12; o.r = 0.2; }   // ...that ricochets
                    w.SpawnProj(a, muz, d, (W.speed ?? 50) * (W.kind == "charge" ? 0.5 + 0.5 * mult : 1), o);
                }
            }
            else if (W.kind == "hitscan")
            {
                int n = W.pellets ?? 1; var e = a.Eye;
                var ed = World.Norm(new V3(aim.x - e.x, aim.y - e.y, aim.z - e.z));
                // Map keeps insertion order: keep it explicit
                var order = new List<Actor>(); var hits = new Dictionary<Actor, (double dmg, bool head)>();
                for (int i = 0; i < n; i++)
                {
                    var d = SpreadDir(ed, (W.spread ?? 0) * spreadMult);
                    var lh = w.level.Ray(e, d, W.range);
                    double max = lh.HasValue ? lh.Value.t : W.range;
                    var bh = w.BarrierHit(a.team, e, d, max);
                    if (bh != null) max = bh.t;
                    var ah = w.RayActors(e, d, max, x => x.team != a.team);
                    double end = ah != null ? ah.t : max;
                    var endP = new V3(e.x + d.x * end, e.y + d.y * end, e.z + d.z * end);
                    a.shots++;
                    if (ah != null)
                    {
                        a.hits++; if (ah.head) a.crits++;
                        double f0 = W.range * fallFrom, fall = end > f0 ? 1 - (end - f0) / W.range : 1;
                        if (!hits.TryGetValue(ah.actor, out var h)) { h = (0, false); order.Add(ah.actor); }
                        h.dmg += W.damage * Math.Max(0.3, fall) * (ah.head ? 1.5 : 1); h.head = h.head || ah.head;
                        hits[ah.actor] = h;
                    }
                    else if (bh != null) w.HitBarrier(bh.owner, W.damage, a, endP);
                    else if (lh.HasValue) w.Fx("impact", endP, new FxOpts { color = a.def.glow, mat = lh.Value.mat, n = new V3(lh.Value.nx, lh.Value.ny, lh.Value.nz) });
                    if (i < 4)
                    {
                        bool show = !dual;
                        if (dual) { a.sv["tracerN"] = a.Sv("tracerN", 0) + 1; show = a.sv["tracerN"] % 2 == 0; }
                        if (show) w.Fx("tracer", muz, new FxOpts { to = endP, color = a.def.glow, actor = a });
                    }
                }
                foreach (var x in order)
                {
                    var h = hits[x];
                    double dmg = h.dmg; bool crit = h.head;
                    // Hanabi (volatile, right gun): every round into a burning enemy is a critical hit
                    if (dual && slot == "secondary" && x.Has("burning", t)) { dmg *= h.head ? 1.3 : 1.75; crit = true; a.stats["volatile"] = (a.stats.TryGetValue("volatile", out var vv) ? vv : 0) + 1; }
                    w.Damage(a, x, dmg, new DmgOpts { crit = crit, kind = "hitscan" });
                    // Hinoko (incendiary, left gun): sustained fire sets them alight
                    if (dual && slot == "primary") Ignite(w, a, x, 1);
                    if (!dual || crit || Rng.Random() < 0.35) w.Sfx(crit ? "crit" : "hit", x.Center);
                }
            }
            else if (W.kind == "melee")
            {
                if (W.sweep)
                {
                    // two-handed hammer: swings alternate sides; the blow lands after a short wind-up
                    double prev = a.Sv("swing", 0);
                    a.sv["swing"] = -(prev != 0 ? prev : -1);
                    a.anim.attackSide = a.sv["swing"];
                    double side = a.sv["swing"];
                    w.After(W.delay ?? 0, () =>
                    {
                        if (!a.alive || a.Has("stun", w.time)) return;
                        double reach = W.range * (a.scale > 1 ? 1 + (a.scale - 1) * 0.5 : 1);
                        w.Fx("hammer", a.Center, new FxOpts { color = a.def.glow, actor = a, side = side, r = reach });
                        // hits are knocked along the swing (sideways to his facing, the way the head is travelling)
                        var f0 = a.Forward();
                        int n = MeleeArc(w, a, reach, W.damage * mult, 0.1, a.Height * 1.3, x =>
                        {
                            if (x.def.frame == "mech" || x.isBoss || x.Has("ccimmune", w.time) || x.Has("tachiai", w.time)) return;
                            double kx = -f0.z * side * 5.5 + f0.x * 2, kz = f0.x * side * 5.5 + f0.z * 2;
                            x.forced = new Forced { vx = kx, vy = 0, vz = kz, until = w.time + 0.18, kind = "knock" };
                        });
                        // the head of the hammer also batters enemy barriers it passes through
                        var f = a.Forward();
                        foreach (var o in w.Enemies(a))
                        {
                            if (!o.barrier.up) continue;
                            double bx = o.pos.x + o.Forward().x * 1.7 * o.scale - a.pos.x, bz = o.pos.z + o.Forward().z * 1.7 * o.scale - a.pos.z;
                            double l = M.Hypot(bx, bz);
                            if (l < reach + 1.5 && (bx * f.x + bz * f.z) / (l == 0 ? 1 : l) > 0.1) w.HitBarrier(o, W.damage * mult, a, o.Center);
                        }
                        if (n > 0) w.Sfx("punch", a.Center, a); else w.Sfx("whiff", a.pos, a);
                    });
                }
                else
                {
                    var f = a.Forward();
                    if (a.Has("dragonblade", t))
                    {
                        // Dragon Gate Blade: the koi-dragon streaks through the cut (one per slash)
                        w.Fx("dragoncut", a.Center, new FxOpts { to = new V3(a.Center.x + f.x * 4.5, a.Center.y, a.Center.z + f.z * 4.5), color = "#b36bff" });
                    }
                    int n = MeleeArc(w, a, W.range * a.scale, W.damage * mult, 0.35, 2.5, x =>
                    {
                        if (a.Has("judgment", t))
                        {
                            var chain = w.Enemies(a).Where(o => o != x && World.Dist3(o.pos, x.pos) < 8).Take(2).ToList();
                            foreach (var o in chain) { w.Damage(a, o, 40, new DmgOpts { kind = "ability" }); w.Fx("lightning", x.Center, new FxOpts { to = o.Center, color = "#8ad8ff" }); }
                            if (chain.Count > 0) w.Sfx("chainlightning", x.Center);
                        }
                        if (a.def.id == "enra" && slot == "secondary" && x.def.frame != "mech")
                            x.forced = new Forced { vx = f.x * 14, vy = 3, vz = f.z * 14, until = t + 0.25, kind = "knock" };
                    });
                    if (a.def.id == "kagemaru" && slot == "secondary")
                    {
                        // Severing Fang: slash through enemy seals / sanctums / curse zones
                        var p = new V3(a.pos.x + f.x * 2, a.pos.y, a.pos.z + f.z * 2);
                        foreach (var z in w.zones)
                        {
                            if (z.team == a.team || !(z.kind == "seal" || z.kind == "sanctuary" || z.kind == "wishzone")) continue;
                            if (M.Hypot(z.x - p.x, z.z - p.z) < z.r + 3.5)
                            {
                                z.until = t; w.Fx("zonebreak", new V3(z.x, z.y, z.z), new FxOpts { r = z.r, color = "#9d7bff" }); w.Sfx("zonebreak", p);
                                w.Counter(a, z.owner, z.kind == "seal" ? "Severing Fang cuts the Warding Seal" : "Severing Fang cuts the Sanctuary");
                            }
                        }
                    }
                    w.Fx("swing", a.Center, new FxOpts { color = a.def.glow, actor = a });
                    if (n == 0) w.Sfx("whiff", a.pos, a);
                }
            }
            // chainguns: one rattle per three rounds (16 rounds/s per gun would drown the mix - "play by sound")
            if (!dual) w.Sfx(W.sfx, muz, a);
            else
            {
                string k = slot == "primary" ? "sfxL" : "sfxR";
                a.sv[k] = a.Sv(k, 0) + 1;
                if (a.sv[k] % 3 == 1) w.Sfx(W.sfx, muz, a);
            }
        }

        /// <summary>Gantetsu's twin rotary chainguns: LMB the left (Hinoko), RMB the right (Hanabi), both for twice the lead.</summary>
        static void DualGuns(World w, Actor a, double dt)
        {
            double t = w.time; var inp = a.input; var P = a.def.primary; var S = a.def.secondary;
            double max2 = Math.Max(1, JsMath.Round((S.ammo ?? 0) * (1 + a.mods.ammo)));
            if (!a.sv.ContainsKey("ammo2")) a.sv["ammo2"] = max2;
            bool endless = a.Has("dohyo", t);
            if (endless) a.reloadUntil = 0;
            if (a.reloadUntil != 0 && t >= a.reloadUntil) { a.reloadUntil = 0; a.ammo = a.MaxAmmo; a.sv["ammo2"] = max2; }
            void Reload() { if (a.reloadUntil == 0 && !endless) { a.reloadUntil = t + a.ReloadTime(P.reload ?? 1.7); w.Sfx("reload", a.pos, a); } }
            if (inp.reload && (a.ammo < a.MaxAmmo || a.sv["ammo2"] < max2)) Reload();
            bool busy = a.barrier.up || (a.forced != null && a.forced.kind != "knock" && a.forced.kind != "pull");
            if (inp.melee && !busy && t >= a.nextMelee) { QuickMelee(w, a); a.nextShot = Math.Max(a.nextShot, t + 0.35); a.nextAlt = Math.Max(a.nextAlt, t + 0.35); }
            bool both = inp.fire && inp.alt;
            foreach (var (want, W, slot) in new[] { (inp.fire, P, "primary"), (inp.alt, S, "secondary") })
            {
                string key = slot == "primary" ? "spin1" : "spin2";
                bool on = want && !busy && a.reloadUntil == 0;
                if (on && !(a.Sv(key, 0) > 0.05)) w.Sfx("spinup", a.pos, a);
                a.sv[key] = Math.Max(0, Math.Min(1, a.Sv(key, 0) + (on ? dt / 0.35 : -dt / 0.8)));
                if (!on || t < (slot == "primary" ? a.nextShot : a.nextAlt)) continue;
                if ((slot == "primary" ? a.ammo : a.sv["ammo2"]) <= 0 && !endless) { Reload(); continue; }
                // both triggers: a wider cone and the falloff from 10 m instead of 16
                Fire(w, a, W, slot, 1, both ? 1.7 : 1, both ? 0.3 : 0.5);
                // keep the cadence exact while the trigger is held; after a pause, count from now (no catch-up burst)
                double iv = 1 / (a.Rate(W.rate) * (0.4 + 0.6 * a.sv[key])), prev = slot == "primary" ? a.nextShot : a.nextAlt;
                double nx = t - prev > iv ? t + iv : prev + iv;
                if (slot == "primary") a.nextShot = nx; else a.nextAlt = nx;
                if (!endless) { if (slot == "primary") a.ammo--; else a.sv["ammo2"]--; }
            }
        }

        public static void UpdateWeapons(World w, Actor a, double dt)
        {
            if (a.def.dualGuns && !a.def.secondary.IsAbility) { DualGuns(w, a, dt); return; }
            double t = w.time; var P = a.def.primary; var inp = a.input;
            bool pAmmo = (P.ammo ?? 0) != 0;
            if (a.reloadUntil != 0 && t >= a.reloadUntil) { a.reloadUntil = 0; a.ammo = a.MaxAmmo; }
            if (pAmmo && inp.reload && a.ammo < a.MaxAmmo && a.reloadUntil == 0 && P.kind != "charge") { a.reloadUntil = t + a.ReloadTime(P.reload ?? 1.5); w.Sfx("reload", a.pos, a); }
            var S = a.def.secondary;
            // ---- secondary holds
            if (S.IsAbility)
            {
                if (S.id == "bulwark")
                {
                    bool want = inp.alt && a.barrier.hp > 1 && t > a.barrier.brokenUntil;
                    if (want && !a.barrier.up) w.Sfx("barrierup", a.pos, a);
                    a.barrier.up = want;
                }
                if (S.id == "zoom") a.sv["zoom"] = inp.alt ? 1 : 0;
            }
            bool busy = a.barrier.up || (a.forced != null && a.forced.kind != "knock" && a.forced.kind != "pull");
            // ---- quick melee (interrupts a reload, not a wind-up already in flight)
            if (inp.melee && !busy && t >= a.nextMelee && t >= a.nextShot - (1 / a.Rate(P.rate)) * 0.5) { QuickMelee(w, a); a.nextShot = Math.Max(a.nextShot, t + 0.35); }
            // ---- primary
            if (P.kind == "charge")
            {
                if (inp.fire && !busy && t >= a.nextShot) { if (!a.charging) w.Sfx("bowdraw", a.pos, a); a.charging = true; a.charge = Math.Min(1, a.charge + dt / 0.9); }
                else if (a.charging)
                {
                    a.charging = false;
                    if (!busy) Fire(w, a, P, "primary", 0.3 + 0.7 * a.charge);
                    a.charge = 0; a.nextShot = t + 1 / a.Rate(P.rate) * 0.6;
                }
            }
            else if (P.kind == "beam")
            {
                bool on = inp.fire && !busy;
                if (on && !a.flameOn) w.Sfx("flamestart", a.pos, a);
                a.flameOn = on;
                if (on && t >= a.nextShot)
                {
                    a.nextShot = t + 1 / a.Rate(P.rate);
                    a.anim.attackAt = t; a.anim.attackKind = "primary";
                    BreakStealth(w, a);
                    double range = P.range * (a.Has("asura", t) ? 1.5 : 1) * a.scale;
                    V3 e = a.Eye, d = a.AimDir();
                    a.shots++;
                    bool touched = false;
                    foreach (var x in w.Enemies(a))
                    {
                        var c = x.Center; var v = new V3(c.x - e.x, c.y - e.y, c.z - e.z); double l = M.Hypot(v.x, v.y, v.z);
                        if (l > range + x.Radius) continue;
                        if ((v.x * d.x + v.y * d.y + v.z * d.z) / l < Math.Cos(0.26) && l > 1.5) continue;
                        if (!w.level.LineOfSight(e, c)) continue;
                        var bh = w.BarrierHit(a.team, e, World.Norm(v), l);
                        if (bh != null) { w.HitBarrier(bh.owner, P.damage / P.rate, a, c); continue; }
                        w.Damage(a, x, P.damage / P.rate, new DmgOpts { kind = "beam" }); touched = true;
                    }
                    if (touched) a.hits++;
                }
            }
            else if (inp.fire && !busy && t >= a.nextShot && a.reloadUntil == 0 && P.damage > 0)
            {
                if (!pAmmo || a.ammo > 0)
                {
                    // one round now; a burst weapon (Hibiki's Subwoofer Blaster) follows up with the rest of the burst
                    void Round()
                    {
                        if (!a.alive || a.reloadUntil != 0 || (pAmmo && a.ammo <= 0) || a.Has("stun", w.time)) return;
                        Fire(w, a, P, "primary");
                        if (pAmmo) { a.ammo--; if (a.ammo <= 0) { a.reloadUntil = w.time + a.ReloadTime(P.reload ?? 1.5); w.Sfx("reload", a.pos, a); } }
                    }
                    Round();
                    for (int i = 1; i < (P.burst ?? 1); i++) w.After((P.burstGap ?? 0.07) * i, Round);
                    a.nextShot = t + 1 / a.Rate(P.rate);
                }
            }
            // ---- secondary weapons
            if (!S.IsAbility)
            {
                if (S.kind == "beam")
                {
                    // heal beam: lock onto the ally under the crosshair and stay on them
                    var tg = a.beamTarget;
                    if (inp.alt && !busy)
                    {
                        bool keep = tg != null && tg.alive && World.Dist3(tg.Center, a.Eye) < S.range + 4 && w.level.LineOfSight(a.Eye, tg.Center);
                        if (!keep) tg = w.ConeTarget(a, S.range, 22, x => x.team == a.team);
                        if (tg != null && !a.beamOn) w.Sfx(S.sfx, a.pos, a);
                        a.beamTarget = tg; a.beamOn = tg != null;
                        if (tg != null && t >= a.nextAlt)
                        {
                            a.nextAlt = t + 1 / a.Rate(S.rate);
                            a.anim.attackAt = t; a.anim.attackKind = "secondary";
                            w.Heal(a, tg, S.damage / S.rate, true);
                        }
                    }
                    else { a.beamOn = false; a.beamTarget = null; }
                }
                else if (inp.alt && !busy && t >= a.nextAlt)
                {
                    a.nextAlt = t + 1 / a.Rate(S.rate);
                    Fire(w, a, S, "secondary");
                }
            }
        }
    }
}
