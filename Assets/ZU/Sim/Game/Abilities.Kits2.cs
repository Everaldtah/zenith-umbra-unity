// Hero kits, part 2: Enra, Gantetsu, Hibiki, Tomoe - their helpers (the Crescent Fang, Bass Drop, Shiko Stomp, the
// Grand Dohyo leash) and the projectile specials. Port of zenith-umbra src/game/abilities.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static partial class Abilities
    {
        // ================================================================ Enra
        static bool Chain(World w, Actor a)
        {
            V3 muz = w.Muzzle(a), aim = w.AimPoint(a, 18);
            w.SpawnProj(a, muz, World.Norm(aim - muz), 55, new ProjOpts { dmg = 25, fx = "chain", special = "chain", life = 18.0 / 55, r = 0.35 });
            w.Sfx("chainthrow", muz, a);
            return true;
        }
        static bool Brand(World w, Actor a)
        {
            var f = FlatDir(a);
            foreach (var x in w.Enemies(a))
            {
                double vx = x.pos.x - a.pos.x, vz = x.pos.z - a.pos.z, l = M.Hypot(vx, vz);
                if (l > 6 + x.Radius || (vx * f.x + vz * f.z) / (l == 0 ? 1 : l) < Math.Cos(0.62) || Math.Abs(x.pos.y - a.pos.y) > 3) continue;
                x.Set("brand", w.time, 5, null, a); x.Set("slow", w.time, 5);
            }
            w.Fx("brandcone", a.Center, new FxOpts { to = new V3(a.pos.x + f.x * 6, a.pos.y + 1, a.pos.z + f.z * 6), color = "#ff6a2a", actor = a }); w.Sfx("brand", a.Center, a);
            return true;
        }
        static bool EffigyUlt(World w, Actor a)
        {
            // Crimson Effigy (Effigy.cs): the giant hologram rises behind him and sweeps the perimeter for EFFIGY_SECS
            Effigy.RaiseEffigy(w, a);
            w.Fx("ultflash", a.Center, new FxOpts { color = "#ff2a2a", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("roar", a.Center, a);
            return true;
        }
        static bool Asura(World w, Actor a)
        {
            a.Set("asura", w.time, 8);
            a.scale = 1.25; a.maxArmor = a.def.armor + 150; a.armor += 150;
            w.Fx("ultflash", a.Center, new FxOpts { color = "#ff6a2a", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("roar", a.Center, a);
            return true;
        }
        // ================================================================ Gantetsu
        static bool Tachiai(World w, Actor a)
        {
            if (a.Has("root", w.time)) return false;
            double t = w.time;
            a.Set("tachiai", t, RUSH_T); a.sv["rushYaw"] = a.yaw; a.sv["rushStart"] = t; a.sv["rushOn"] = 1;
            a._rushHit = new HashSet<int>();
            w.Sfx("charge", a.Center, a); w.Sfx("roar", a.Center, a);
            w.Fx("chargetrail", a.Center, new FxOpts { actor = a, color = a.def.glow, dur = RUSH_T });
            return true;
        }
        static bool Taiko(World w, Actor a)
        {
            a.Set("taiko", w.time, 3); a.sv["taikoBeat"] = w.time;
            w.Fx("taiko", a.Center, new FxOpts { actor = a, color = "#ffb35c", r = 12 }); w.Sfx("taiko", a.Center, a);
            return true;
        }
        static bool Dohyo(World w, Actor a)
        {
            // the ring is stamped where he stands: everyone of the other team inside it is caught for the bout
            double t = w.time, g = w.level.GroundAt(a.pos.x, a.pos.z, a.pos.y + 0.5);
            var p = new V3(a.pos.x, g > double.NegativeInfinity ? g : a.pos.y, a.pos.z);
            var trapped = w.Enemies(a).Where(x => M.Hypot(x.pos.x - p.x, x.pos.z - p.z) < 9 + x.Radius * 0.5 && Math.Abs(x.pos.y - p.y) < 6).Select(x => x.id).ToList();
            MakeZone(w, a, "dohyo", p, 9, DOHYO_T, new Dictionary<string, object> { ["trapped"] = trapped });
            a.Set("dohyo", t, DOHYO_T);
            // the binding chains snap onto everyone caught: dashes and flight end where they stand
            foreach (var x in w.Enemies(a)) if (trapped.Contains(x.id)) { Leash(w, a, x); w.Sfx("chainhit", x.Center, a); }
            a.reloadUntil = 0; a.ammo = a.MaxAmmo; if (!a.def.secondary.IsAbility) a.sv["ammo2"] = Math.Max(1, JsMath.Round((a.def.secondary.ammo ?? 0) * (1 + a.mods.ammo)));
            w.Fx("ultflash", a.Center, new FxOpts { color = a.def.glow, actor = a }); w.Fx("slam", p, new FxOpts { r = 9, color = "#ffe6a8", actor = a });
            w.Sfx("ultcall", a.Center, a); w.Sfx("dohyo", p, a); w.Sfx("slam", p, a);
            w.Msg("GANTETSU · GRAND DOHYO", a.def.color);
            return true;
        }
        // ================================================================ Hibiki
        static bool Crossmix(World w, Actor a)
        {
            // swap tracks: 0 = Healing Groove, 1 = Tempo Rush (an amped track stays amped through the swap)
            a.sv["track"] = a.Sv("track", 0) != 0 ? 0 : 1;
            bool speed = a.sv["track"] != 0;
            w.Fx("crossmix", a.Center, new FxOpts { actor = a, color = speed ? "#ffd23f" : "#7dffcf", r = AURA_R });
            w.Sfx(speed ? "track_speed" : "track_heal", a.Center, a);
            return true;
        }
        static bool Maxvolume(World w, Actor a)
        {
            a.Set("amp", w.time, 3);
            w.Fx("amp", a.Center, new FxOpts { actor = a, color = a.Sv("track", 0) != 0 ? "#ffd23f" : "#7dffcf", r = AURA_R });
            w.Sfx("amp", a.Center, a);
            return true;
        }
        static bool Scratch(World w, Actor a)
        {
            double t = w.time; var f = a.Forward(); bool pumped = a.Has("pumped", t);
            double dmg = pumped ? 52.5 : 35, push = pumped ? 1.25 : 1;
            int n = 0;
            foreach (var x in w.Enemies(a))
            {
                var v = new V3(x.pos.x - a.pos.x, x.Center.y - a.Eye.y, x.pos.z - a.pos.z); double l = M.Hypot(v.x, v.z);
                if (l > 8 + x.Radius || Math.Abs(v.y) > 3.5) continue;
                if ((v.x * f.x + v.z * f.z) / (l == 0 ? 1 : l) < 0.5 && l > x.Radius + 0.5) continue;
                if (!w.level.LineOfSight(a.Eye, x.Center)) continue;
                w.Damage(a, x, dmg, Ab); n++;
                if (CcBlocked(w, x) || x.def.frame == "mech" || x.isBoss || x.Has("tachiai", t)) continue;
                double nx = l > 0.1 ? v.x / l : f.x, nz = l > 0.1 ? v.z / l : f.z;
                x.vel.y = Math.Max(x.vel.y, 3.2 * push); x.grounded = false; x.lastGroundedAt = -9;
                x.forced = new Forced { vx = nx * 13 * push, vy = 0, vz = nz * 13 * push, until = t + 0.32, kind = "knock" };
            }
            if (pumped) a.Clear("pumped");
            a.sv["grind"] = 0;
            w.Fx("scratchwave", a.Eye, new FxOpts { actor = a, color = pumped ? "#ffd23f" : "#9ef6ff", r = 8, side = pumped ? 1 : 0 });
            w.Sfx(pumped ? "scratch_big" : "scratch", a.Center, a);
            if (n > 0) a.hits++;
            a.shots++;
            return true;
        }
        static bool Bassdrop(World w, Actor a)
        {
            // the leap: straight up; the drop lands when he touches down (or at the top of the arc if he's airborne)
            double t = w.time;
            a.vel.y = Math.Max(a.vel.y, 8.5); a.grounded = false; a.lastGroundedAt = -9; a.anim.jumpAt = t;
            a.Set("dropair", t, 1.2); a.sv["dropArmed"] = 1; a.sv["dropAt"] = t;
            w.Fx("ultflash", a.Center, new FxOpts { color = a.def.glow, actor = a });
            w.Sfx("ultcall", a.Center, a); w.Sfx("bassrise", a.Center, a);
            return true;
        }
        // ================================================================ Tomoe
        static bool Crescent(World w, Actor a)
        {
            // thrown from the left hand along the crosshair; it sticks in whatever it meets (OnProj 'crescent')
            double t = w.time; V3 e = a.Eye, f = a.Forward(); double lx = Math.Cos(a.yaw), lz = -Math.Sin(a.yaw);
            var from = new V3(e.x + lx * 0.3 * a.scale + f.x * 0.35, e.y - 0.3 * a.scale, e.z + lz * 0.3 * a.scale + f.z * 0.35);
            var aim = w.AimPoint(a, FANG_RANGE);
            var p = w.SpawnProj(a, from, World.Norm(aim - from), FANG_SPEED, new ProjOpts { dmg = FANG_DMG, fx = "crescent", special = "crescent", life = FANG_RANGE / FANG_SPEED, r = 0.3, grav = 1.5 });
            a.sv["fang"] = 1; a.sv["fangProj"] = p.id; a.sv["fangAt"] = t;
            w.Sfx("throw", from, a);
            return true;
        }
        static bool Warcall(World w, Actor a)
        {
            double t = w.time;
            int n = 0;
            foreach (var x in w.Allies(a))
            {
                if (World.Dist3(x.pos, a.pos) > WARCALL_R || (x != a && !w.level.LineOfSight(a.Eye, x.Center))) continue;
                w.AddShield(x, x == a ? 150 : 75, 3, "warcall", a);
                x.sv["speed"] = x.Has("speed", t) ? Math.Max(x.Sv("speed", 1), 1.3) : 1.3; x.sv["speedFrom"] = a.id; x.Set("speed", t, 3);
                x.Set("warcall", t, 3);
                if (x != a) { n++; w.Fx("warcallally", x.Center, new FxOpts { actor = x, color = a.def.glow }); }
            }
            a.stats["warcall"] = (a.stats.TryGetValue("warcall", out var wc) ? wc : 0) + n;
            w.Fx("warcall", a.Center, new FxOpts { actor = a, color = a.def.glow, r = WARCALL_R }); w.Sfx("warcall", a.Center, a);
            return true;
        }
        static bool Reaping(World w, Actor a)
        {
            // the axe comes off her back and round in a heavy cleave; she slows as she heaves it
            double t = w.time;
            a.Set("reapwind", t, 0.62);
            w.Sfx("reapwind", a.Center, a);
            w.After(REAP_HIT, () =>
            {
                if (!a.alive || a.Has("stun", w.time)) return;
                var f = a.Forward();
                int n = 0;
                foreach (var x in w.Enemies(a))
                {
                    double vx = x.pos.x - a.pos.x, vz = x.pos.z - a.pos.z, l = M.Hypot(vx, vz);
                    if (l > REAP_R + x.Radius || Math.Abs(x.pos.y - a.pos.y) > 2.6 * a.scale) continue;
                    if ((vx * f.x + vz * f.z) / (l == 0 ? 1 : l) < 0.34 && l > x.Radius + 0.4) continue;
                    if (!w.level.LineOfSight(a.Eye, x.Center)) continue;
                    w.Damage(a, x, 90, Ab); Weapons.Wound(w, a, x, 40); n++;
                    w.Fx("slash", x.Center, new FxOpts { color = a.def.glow });
                }
                // every enemy cut shortens the cooldown by a second
                if (n > 0) { a.cd["reaping"] = Math.Max(w.time, (a.cd.TryGetValue("reaping", out var rc) ? rc : 0) - n); a.hits++; }
                a.shots++;
                w.Fx("reaping", a.Center, new FxOpts { actor = a, color = a.def.glow, r = REAP_R }); w.Sfx(n > 0 ? "reaping" : "whiff", a.Center, a);
                if (n > 0) w.Sfx("impact_body", a.Center, a);
            });
            return true;
        }
        static bool Tide(World w, Actor a)
        {
            // Crescent Warpath: she zooms the lane along the ground, the axe wheeling around her, straight through every body
            // in the way; whoever she passes is cut, wounded, starved of healing and MARKED (tidemark)
            double t = w.time; var d = FlatDir(a);
            double dist = TideReach(w, a, d), dur = Math.Max(TIDE_MIN_SECS, dist / TIDE_SPEED);
            a._tideHit = new HashSet<int>();
            a.sv["tideT0"] = t; a.sv["tideDur"] = dur; a.sv["tideApex"] = TIDE_APEX > 0 ? Math.Max(1.2, TIDE_APEX * Math.Sqrt(dist / TIDE_LEN)) : 0;
            a.sv["tideX"] = a.pos.x; a.sv["tideZ"] = a.pos.z; a.sv["tideStuck"] = 0;
            bool air = a.sv["tideApex"] > 0;
            a.forced = new Forced
            {
                vx = d.x * dist / dur, vy = air ? 4 * a.sv["tideApex"] / dur : 0, vz = d.z * dist / dur, until = t + dur, kind = "tide", ignoreGravity = air, onEnd = () =>
                {
                    // (the end of the lane, a click, a wall: wherever it ends, she stops there)
                    a.Clear("tideult"); a.Set("ccimmune", w.time, 0.15);
                    a.cd["reaping"] = 0;
                    if (a.Sv("fang", 0) != 0) FangHome(w, a); else a.cd["crescent"] = 0;
                    w.Fx("slam", a.pos, new FxOpts { r = 3, color = a.def.glow, actor = a }); w.Sfx("slam", a.pos, a);
                }
            };
            if (air) { a.grounded = false; a.lastGroundedAt = -9; a.anim.jumpAt = t; }
            a.Set("ccimmune", t, dur + 0.15); a.Set("tideult", t, dur + 0.05);
            w.Fx("ultflash", a.Center, new FxOpts { color = a.def.glow, actor = a }); w.Fx("chargetrail", a.Center, new FxOpts { actor = a, color = a.def.glow, dur = dur });
            w.Sfx("ultcall", a.Center, a); w.Sfx("charge", a.Center, a); w.Sfx("roar", a.Center, a);
            w.Msg("TOMOE · CRESCENT WARPATH", a.def.color);
            return true;
        }

        // ---------------------------------------------------------------- Tomoe's numbers
        public const double FANG_DMG = 55, FANG_WOUND = 30, FANG_SPEED = 42, FANG_RANGE = 30, FANG_BACK = 46, FANG_STICK = 6;
        public const double WARCALL_R = 15, REAP_R = 5.5, REAP_HIT = 0.42;
        /// <summary>Crescent Warpath: the dash (m, m/s), the top of the arc if it flies (0 = along the ground), the lane half-width</summary>
        public const double TIDE_LEN = 20, TIDE_SPEED = 28, TIDE_APEX = 0, TIDE_HALF = 2.5, TIDE_MIN_SECS = 0.35;
        public const double TIDE_CUT = 40, TIDE_WOUND = 90, TIDE_ANTIHEAL = 3.5;
        public const double TIDE_MARK = 8, TIDE_MARK_AMP = 1.2; public const string TIDE_MARK_COLOR = "#4aa8ff";
        /// <summary>a click ends the flight, once it has been under way this long (s)</summary>
        public const double TIDE_HOLD = 0.15;
        const double TIDE_DROP = 6;

        /// <summary>how far Crescent Warpath carries her along d: TIDE_LEN, pulled in to the last spot inside the arena with footing</summary>
        public static double TideReach(World w, Actor a, V3 d)
        {
            double X = w.level.Size[0], Z = w.level.Size[1];
            double reach = 0;
            for (int s = 1; s <= TIDE_LEN; s++)
            {
                double x = a.pos.x + d.x * s, z = a.pos.z + d.z * s;
                if (Math.Abs(x) > X - a.Radius || Math.Abs(z) > Z - a.Radius) break;
                if (w.level.GroundAt(x, z, a.pos.y + TIDE_APEX + 1, a.Radius) > a.pos.y - TIDE_DROP) reach = s;
            }
            return reach;
        }
        /// <summary>Twin Koi Torrent's double helix - radius (m), turn (rad/m)</summary>
        public const double TWIN_R = 2.2, TWIN_W = 0.3;

        /// <summary>where the Crescent Fang is right now (flying, stuck, riding an enemy or on its way home), or null in her hand</summary>
        public static V3? FangPos(World w, Actor a)
        {
            double st = a.Sv("fang", 0);
            if (st == 1) { var p = w.projs.FirstOrDefault(q => q.id == a.Sv("fangProj", 0)); return p != null ? p.pos : (V3?)null; }
            if (st == 3) { var x = w.actors.FirstOrDefault(o => o.id == a.Sv("fangTgt", 0)); return x != null ? x.Center : (V3?)null; }
            if (st == 2 || st == 4) return new V3(a.Sv("fangX", 0), a.Sv("fangY", 0), a.Sv("fangZ", 0));
            return null;
        }
        /// <summary>RMB again: call the blade back - out of an enemy it hauls them toward her first</summary>
        static void RecallFang(World w, Actor a)
        {
            double t = w.time;
            if (a.Sv("fang", 0) == 3)
            {
                var x = w.actors.FirstOrDefault(o => o.id == a.Sv("fangTgt", 0));
                if (x != null && x.alive)
                {
                    var p = x.Center;
                    a.sv["fangX"] = p.x; a.sv["fangY"] = p.y; a.sv["fangZ"] = p.z;
                    if (!CcBlocked(w, x) && x.def.frame != "mech" && !x.isBoss && !x.Has("tachiai", t))
                    {
                        var d = World.Norm(new V3(a.pos.x - x.pos.x, 0, a.pos.z - x.pos.z));
                        double dist = Math.Min(12, Math.Max(0, World.Dist3(a.pos, x.pos) - (a.Radius + x.Radius + 1.2)));
                        Interrupt(w, x, a);
                        x.forced = new Forced { vx = d.x * dist / 0.34, vy = 2.5, vz = d.z * dist / 0.34, until = t + 0.34, kind = "pull" };
                        a.stats["yanks"] = (a.stats.TryGetValue("yanks", out var yk) ? yk : 0) + 1;
                        // COUNTER: out of the sky - a flyer is dragged down and grounded
                        if (x.flying || x.def.frame == "flyer")
                        {
                            x.flying = false; ApplyCC(w, a, x, "grounded", 1.5);
                            if (x.def.id == "nocturne") w.Counter(a, x, "Crescent Fang drags Lady Nocturne out of the sky");
                        }
                        w.Fx("chainline", a.Center, new FxOpts { target = x, color = a.def.glow, dur = 0.34 }); w.Sfx("yank", x.Center, a);
                    }
                }
            }
            a.sv["fang"] = 4; a.sv["fangAt"] = t;
            a._fangBack = new HashSet<int> { (int)a.Sv("fangTgt", -1) };
            a.anim.castAt = t; a.anim.castId = "recall";
            w.Sfx("fangreturn", new V3(a.Sv("fangX", 0), a.Sv("fangY", 0), a.Sv("fangZ", 0)), a);
        }
        /// <summary>the blade is back in her hand: the cooldown starts now</summary>
        static void FangHome(World w, Actor a)
        {
            double t = w.time;
            a.sv["fang"] = 0; a.sv["fangTgt"] = 0;
            double cd = a.def.secondary != null && a.def.secondary.IsAbility ? a.def.secondary.cooldown : 6;
            a.cd["crescent"] = a.Has("tideult", t) ? t : t + cd * (1 - a.mods.cdr) * (1 - (a.mods.cdrBy.TryGetValue("crescent", out var cb) ? cb : 0));
            w.Sfx("fangcatch", a.Center, a);
        }

        /// <summary>Hibiki's aura reach (Crossmix / Max Volume)</summary>
        public const double AURA_R = 12;
        const double BASS_HP = 750, BASS_R = 30;

        /// <summary>Bass Drop lands: every ally in range he can see gets the drop's temporary health (it fades over 6s after a beat)</summary>
        static void BassDropLand(World w, Actor a)
        {
            double t = w.time; var p = a.pos;
            int n = 0;
            foreach (var x in w.Allies(a))
            {
                if (World.Dist3(x.pos, p) > BASS_R || (x != a && !w.level.LineOfSight(a.Eye, x.Center))) continue;
                x.shields = x.shields.Where(s => s.kind != "bassdrop").ToList();
                x.shields.Add(new Shield { amt = BASS_HP, until = t + 7, kind = "bassdrop", src = a });
                x.sv["bassAt"] = t; n++;
                w.Fx("bassshield", x.Center, new FxOpts { actor = x, color = a.def.glow });
            }
            a.stats["bassdrop"] = (a.stats.TryGetValue("bassdrop", out var bd) ? bd : 0) + n;
            w.Fx("bassdrop", p, new FxOpts { r = BASS_R, color = a.def.glow, actor = a }); w.Fx("slam", p, new FxOpts { r = 7, color = "#9ef6ff", actor = a });
            w.Sfx("bassdrop", p, a); w.Sfx("slam", p, a);
            w.Msg("HIBIKI · BASS DROP", a.def.color);
        }

        /// <summary>Tachiai Rush: how long the charge runs before it ends in the leap by itself</summary>
        public const double RUSH_T = 2.5;
        /// <summary>Grand Dohyo: how long the ring and its chains hold</summary>
        public const double DOHYO_T = 8;
        /// <summary>Shiko Stomp: the slam's reach, the heart of it, and how high it reaches</summary>
        public const double STOMP_R = 7, STOMP_CORE = 2.5, STOMP_H = 3;

        /// <summary>the leap out of a Tachiai Rush: up, then driven down into the slam</summary>
        public static void StompLeap(World w, Actor a)
        {
            double t = w.time;
            a.vel.y = 10; a.grounded = false; a.lastGroundedAt = -9; a.anim.jumpAt = t;
            a.Clear("tachiai"); a.sv["rushOn"] = 0; a.Set("stompair", t, 2.5); a.sv["stompArmed"] = 1;
            w.Sfx("mechjump", a.pos, a);
        }

        /// <summary>Grand Dohyo: bind a hero to the ring - no flight, no dashes, held inside the rope</summary>
        static void Leash(World w, Actor by, Actor x)
        {
            double t = w.time;
            x.Set("chained", t, 0.3, null, by);
            if (x.def.frame == "mech" || x.isBoss) return;
            x.Set("grounded", t, 0.3, null, by); x.flying = false;
            if (x.forced != null && x.forced.kind != "knock" && x.forced.kind != "pull") Interrupt(w, x, by);
        }

        /// <summary>Shiko Stomp: everyone within 7m the shockwave can reach is thrown back, knocked down, stunned, set alight</summary>
        static void ShikoStomp(World w, Actor a)
        {
            double t = w.time; var p = a.pos; var eye = new V3(p.x, p.y + 0.6, p.z);
            foreach (var x in w.Enemies(a))
            {
                double dx = x.pos.x - p.x, dz = x.pos.z - p.z, d = M.Hypot(dx, dz);
                if (d > STOMP_R + x.Radius || Math.Abs(x.pos.y - p.y) > STOMP_H) continue;
                if (!w.level.LineOfSight(eye, x.Center)) continue;                 // a wall between them takes the shockwave
                bool core = d < STOMP_CORE + x.Radius;
                w.Damage(a, x, core ? 120 : 60, Ab); Weapons.Ignite(w, a, x, 8);
                if (!x.alive || x.def.frame == "mech" || x.isBoss || !ApplyCC(w, a, x, "stun", core ? 0.9 : 0.7)) continue;
                x.Set("knockdown", t, core ? 0.9 : 0.7, null, a);
                // swept off their feet, away from the landing: a low hop and a shove, then flat on the ground
                double nx = d > 0.1 ? dx / d : Math.Sin(a.yaw), nz = d > 0.1 ? dz / d : Math.Cos(a.yaw);
                x.flying = false; x.vel.y = 4; x.grounded = false;
                x.forced = new Forced { vx = nx * 8.5, vy = 0, vz = nz * 8.5, until = t + 0.3, kind = "knock" };
            }
            w.Fx("slam", p, new FxOpts { r = STOMP_R, color = a.def.glow, actor = a }); w.Fx("stomp", p, new FxOpts { r = STOMP_R, color = "#ffb35c", actor = a }); w.Fx("dust", p, new FxOpts { r = 4 });
            w.Sfx("slam", p, a); w.Sfx("mechland", p, a);
        }

        // ------------------------------------------------------------------ projectile specials (TS castAbility.onProj)
        public static void OnProj(World w, Proj p, V3 at, Actor hit)
        {
            var a = p.owner; double t = w.time;
            switch (p.special)
            {
                case "anchor":
                    if (hit != null)
                    {
                        w.Damage(a, hit, 40, Ab);
                        Interrupt(w, hit, a);
                        if (hit.def.frame != "mech" || hit.forced == null)
                        {
                            var d = World.Norm(new V3(a.pos.x - hit.pos.x, 0, a.pos.z - hit.pos.z));
                            double dist = Math.Max(0, World.Dist3(a.pos, hit.pos) * 0.7 - 1.5);
                            if (!CcBlocked(w, hit)) hit.forced = new Forced { vx = d.x * dist / 0.35, vy = 3, vz = d.z * dist / 0.35, until = t + 0.35, kind = "pull" };
                        }
                        w.Fx("chainline", a.Center, new FxOpts { target = hit, color = "#ffd76a", dur = 0.35 }); w.Sfx("anchorhit", at, a);
                    }
                    w.Fx("impact", at, new FxOpts { color = "#ffd76a" });
                    break;
                case "reveal":
                    {
                        foreach (var x in w.Enemies(a)) if (World.Dist3(x.pos, at) < 10)
                            {
                                if (x.Has("stealth", t) && x.def.id == "kagemaru") w.Counter(a, x, "Revealing Dawn Arrow exposes the Shade Fang");
                                x.Set("revealed", t, 5); x.Clear("stealth");
                            }
                        bool severed = false;
                        foreach (var x in w.Allies(a)) if (World.Dist3(x.pos, at) < 10)
                            {
                                if (x.Has("tethered", t)) { x.Clear("tethered"); severed = true; }
                                if (x.Has("antiheal", t)) { x.Clear("antiheal"); severed = true; }
                            }
                        foreach (var z in w.zones) if (z.team != a.team && (z.kind == "grievous" || z.kind == "tether") && M.Hypot(z.x - at.x, z.z - at.z) < 10 + z.r) { z.until = t; severed = true; }
                        if (hit != null) w.Damage(a, hit, 20, new DmgOpts { kind = "proj" });
                        if (severed) { var hx = w.actors.FirstOrDefault(o => o.def.id == "hex" && o.team != a.team); if (hx != null) w.Counter(a, hx, "Dawn Arrow severs the Marionette Strings"); }
                        w.Fx("revealburst", at, new FxOpts { r = 10, color = "#ffd27a" }); w.Sfx("reveal", at, a);
                        break;
                    }
                case "grievous":
                    {
                        double g = w.level.GroundAt(at.x, at.z, at.y + 0.5);
                        MakeZone(w, a, "grievous", new V3(at.x, g > double.NegativeInfinity ? g : at.y, at.z), 6, 3);
                        w.Fx("hexburst", at, new FxOpts { r = 6, color = "#c77dff", dur = 4 }); w.Sfx("hexburst", at, a);
                        break;
                    }
                case "crescent":
                    if (a.Sv("fang", 0) != 1 || a.Sv("fangProj", 0) != p.id) break;
                    if (hit != null)
                    {
                        w.Damage(a, hit, FANG_DMG, Ab); Weapons.Wound(w, a, hit, FANG_WOUND);
                        a.hits++;
                        if (hit.alive) { a.sv["fang"] = 3; a.sv["fangTgt"] = hit.id; a.sv["fangAt"] = t; w.Sfx("chainhit", at, a); }
                        else { a.sv["fang"] = 2; a.sv["fangX"] = at.x; a.sv["fangY"] = at.y; a.sv["fangZ"] = at.z; a.sv["fangAt"] = t; }
                        w.Fx("slash", at, new FxOpts { color = a.def.glow });
                    }
                    else if (p.life <= 0)
                    {
                        // out of range in open air: it turns and comes home
                        a.sv["fangX"] = at.x; a.sv["fangY"] = at.y; a.sv["fangZ"] = at.z; a.sv["fang"] = 4; a.sv["fangAt"] = t;
                        a._fangBack = new HashSet<int>();
                    }
                    else
                    {
                        a.sv["fang"] = 2; a.sv["fangX"] = at.x; a.sv["fangY"] = at.y; a.sv["fangZ"] = at.z; a.sv["fangAt"] = t;
                        w.Fx("impact", at, new FxOpts { color = a.def.glow }); w.Sfx("impact_metal", at, a);
                    }
                    a.shots++;
                    break;
                case "chain":
                    if (hit != null)
                    {
                        w.Damage(a, hit, 25, Ab);
                        bool wasDash = hit.forced?.kind == "flashstep";
                        Interrupt(w, hit, a);
                        bool rooted = ApplyCC(w, a, hit, "root", 1.2);
                        if (wasDash && rooted && hit.def.id == "raijin" && a.def.id == "enra") { }   // counter text emitted by Interrupt()
                        else if (rooted && hit.def.id == "raijin" && a.def.id == "enra") w.Counter(a, hit, "Chain of Oblivion locks Raijin down");
                        // haul the thrower in
                        var d = World.Norm(new V3(hit.pos.x - a.pos.x, 0, hit.pos.z - a.pos.z));
                        double dist = Math.Max(0, World.Dist3(a.pos, hit.pos) - 2);
                        if (dist > 0.5) Dash(a, d, dist, Math.Max(0.15, dist / 30), "chainpull", t);
                        w.Fx("chainline", a.Center, new FxOpts { target = hit, color = "#ff6a2a", dur = 0.4 }); w.Sfx("chainhit", at, a);
                    }
                    break;
            }
        }

        /// <summary>Divine Seal Storm: the window, the shield and how often it re-forms, the hunting seals' reach, beat and bite</summary>
        public const double SEALSTORM_SECS = 15, SEALSTORM_SHIELD = 300, SEALSTORM_REFORM = 4, SEALSTORM_R = 18, SEALSTORM_TICK = 0.5, SEALSTORM_DMG = 14;
        /// <summary>...and the mending seals: every ally within reach he can see, healed this much a beat</summary>
        public const double SEALSTORM_HEAL = 11;

        /// <summary>the seals close around him: a shield of them (a fresh one replaces what is left)</summary>
        static void SealShield(World w, Actor a, double amt, double secs)
        {
            a.shields = a.shields.Where(s => s.kind != "sealshield").ToList();
            a.shields.Add(new Shield { amt = amt, until = w.time + secs, kind = "sealshield", src = a });
            w.Fx("sealshield", a.Center, new FxOpts { actor = a, color = "#ffe28a" });
        }
    }
}
