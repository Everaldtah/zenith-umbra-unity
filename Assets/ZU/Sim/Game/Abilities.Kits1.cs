// Hero kits, part 1: Tenkai-Oh (+ Haruto), Mirei, Kaien, Raijin, Hayate, Seiran, Yuzu, Gorgoth, Nocturne, Hex, Kagemaru.
// Port of zenith-umbra src/game/abilities.ts (the `I` table, in the TS order). Each returns true when it fired.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public static partial class Abilities
    {
        // ================================================================ Tenkai-Oh
        static bool Anchor(World w, Actor a)
        {
            V3 muz = w.Muzzle(a), aim = w.AimPoint(a, 25);
            w.SpawnProj(a, muz, World.Norm(aim - muz), 48, new ProjOpts { dmg = 40, fx = "fist", special = "anchor", life = 25.0 / 48, r = 0.5 });
            w.Sfx("rocketfist", muz, a);
            return true;
        }
        static bool Sunburst(World w, Actor a)
        {
            foreach (var x in w.Allies(a)) if (World.Dist3(x.pos, a.pos) < 10)
                {
                    var had = DEBUFF.Where(s => x.Has(s, w.time)).ToList();
                    foreach (var s in DEBUFF) x.Clear(s);
                    x.wounds = new List<Wound>();
                    w.zones = w.zones.Where(z => !(z.kind == "tether" && ZoneData<Actor>(z, "target") == x)).ToList();
                    x.Set("ccimmune", w.time, 2);
                    if (had.Contains("brand") || had.Contains("antiheal"))
                    {
                        var foe = (x.src.TryGetValue("brand", out var b) ? b : null) ?? w.actors.FirstOrDefault(o => o.def.id == (had.Contains("brand") ? "enra" : "hex") && o.team != a.team);
                        if (foe != null) w.Counter(a, foe, had.Contains("brand") ? "Purging Sunburst burns away the Eclipse Brand" : "Purging Sunburst purges the Grievous Hex");
                    }
                }
            foreach (var x in w.Enemies(a)) if (World.Dist3(x.pos, a.pos) < 10) w.Damage(a, x, 30, Ab);
            w.Fx("sunburst", a.Center, new FxOpts { r = 10, color = "#ffd76a", actor = a }); w.Sfx("sunburst", a.Center, a);
            return true;
        }
        static bool Dawndrive(World w, Actor a)
        {
            var f = FlatDir(a); double t = w.time;
            a.forced = new Forced { vx = f.x * 13, vy = 11, vz = f.z * 13, until = t + 0.95, kind = "dawndrive" };
            a.Set("ccimmune", t, 1.3);
            w.Sfx("ultcall", a.Center, a); w.Fx("ultflash", a.Center, new FxOpts { color = "#ffd76a", actor = a });
            w.After(0.95, () =>
            {
                if (!a.alive) return;
                a.vel.y = -30;
                w.After(0.12, () =>
                {
                    if (!a.alive) return;
                    var p = a.pos;
                    foreach (var x in w.Enemies(a)) if (World.Dist3(x.pos, p) < 8) { w.Damage(a, x, 200, Ab); ApplyCC(w, a, x, "stun", 1.5); }
                    w.Fx("slam", p, new FxOpts { r = 8, color = "#ffd76a", actor = a }); w.Sfx("slam", p, a);
                });
            });
            return true;
        }
        static bool Dawncharge(World w, Actor a)
        {
            // a thruster-driven shoulder charge: steerable, pins the first enemy it meets, knocks everyone else aside
            var d = FlatDir(a); double t = w.time;
            a.sv["pinned"] = 0; a.sv["chargeWall"] = 0; a.sv["chargeStart"] = t; a.sv["chargeYaw"] = Math.Atan2(d.x, d.z);
            a._chargeHit = new HashSet<int>();
            double sp = 17 * (a.scale > 1 ? 1.25 : 1);
            a.forced = new Forced
            {
                vx = d.x * sp, vy = 0, vz = d.z * sp, until = t + 2.2, kind = "dawncharge", onEnd = () =>
                {
                    var pin = w.actors.FirstOrDefault(x => x.id == a.Sv("pinned", 0));
                    if (pin != null && pin.alive)
                    {
                        bool wall = a.Sv("chargeWall", 0) == 1;
                        w.Damage(a, pin, wall ? 225 : 80, Ab); ApplyCC(w, a, pin, "stun", wall ? 1.0 : 0.4);
                        w.Fx("slam", pin.pos, new FxOpts { r = wall ? 3.5 : 2, color = "#ffd76a", actor = a }); w.Sfx(wall ? "slam" : "punch", pin.pos, a);
                    }
                    else if (a.Sv("chargeWall", 0) == 1) { w.Fx("slam", a.pos, new FxOpts { r = 2.5, color = "#ffd76a", actor = a }); w.Sfx("mechland", a.pos, a); }
                    a.sv["pinned"] = 0; a.vel.x *= 0.2; a.vel.z *= 0.2;
                }
            };
            a.Set("charging", t, 2.2);
            w.Sfx("charge", a.Center, a); w.Sfx("mechjump", a.pos, a); w.Fx("chargetrail", a.Center, new FxOpts { actor = a, color = "#ffd76a", dur = 2.2 });
            return true;
        }
        static bool Shatter(World w, Actor a)
        {
            // raise the hammer overhead, slam it down: a ground shockwave cone knocks down everything standing in front
            double t = w.time;
            a.forced = new Forced { vx = 0, vy = 0, vz = 0, until = t + 0.75, kind = "shatter" };
            a.Set("ccimmune", t, 0.6);
            w.Sfx("ultcall", a.Center, a);
            w.After(0.55, () =>
            {
                if (!a.alive) return;
                var d = FlatDir(a); var o = new V3(a.pos.x + d.x * 1.2 * a.scale, a.pos.y, a.pos.z + d.z * 1.2 * a.scale);
                double len = 16 * (a.scale > 1 ? 1.4 : 1), half = 0.42;
                foreach (var x in w.Enemies(a))
                {
                    double vx = x.pos.x - o.x, vz = x.pos.z - o.z, along = vx * d.x + vz * d.z;
                    if (along < -1 || along > len + x.Radius) continue;
                    double lat = Math.Abs(vx * -d.z + vz * d.x);
                    if (lat > Math.Max(1.5, along * Math.Tan(half)) + x.Radius) continue;
                    // it travels along the ground: fliers, jumpers and anything high above the slam point are untouched
                    double g = w.level.GroundAt(x.pos.x, x.pos.z, x.pos.y + 0.5);
                    if (x.flying || x.pos.y - g > 0.6 || Math.Abs(x.pos.y - a.pos.y) > 3) continue;
                    var dir = World.Norm(new V3(vx, 0, vz)); var bh = w.BarrierHit(a.team, new V3(o.x, o.y + 0.5, o.z), dir, M.Hypot(vx, vz));
                    if (bh != null) { w.HitBarrier(bh.owner, 300, a, bh.owner.Center); continue; }
                    w.Damage(a, x, 75, Ab); if (ApplyCC(w, a, x, "stun", 1.0)) x.Set("knockdown", w.time, 1.0, null, a);
                }
                w.Fx("shatter", o, new FxOpts { to = new V3(o.x + d.x * len, o.y, o.z + d.z * len), r = len, color = "#ffd76a", actor = a });
                w.Sfx("slam", o, a); w.Sfx("boom", o, a);
            });
            return true;
        }
        static bool Pilotroll(World w, Actor a)
        {
            Dash(a, MoveDir(a), 5, 0.3, "roll", w.time);
            w.Sfx("dash", a.pos, a);
            return true;
        }
        static bool Callmech(World w, Actor a)
        {
            // Tenkai-Oh drops out of the sky onto the pilot: full frame, the mech's saved ult comes back with it
            double t = w.time; var def = a.baseDef; double keep = a.Sv("mechUlt", 0);
            a.def = def;
            a.hp = def.hp; a.maxArmor = def.armor; a.armor = def.armor; a.shields = new List<Shield>();
            if (a.barrier.max > 0) a.barrier = new BarrierState { hp = a.barrier.max, max = a.barrier.max };
            a.ammo = def.primary.ammo ?? 0; a.reloadUntil = 0; a.nextShot = t + 0.5;
            a.flight = 100; a.Set("ccimmune", t, 1); a.Set("spawnprot", t, 0.6);
            w.After(0.01, () => { a.ult = keep; });                  // CastAbility zeroes the gauge after this returns
            foreach (var x in w.Enemies(a)) if (World.Dist3(x.pos, a.pos) < 5) w.Damage(a, x, 50, Ab);
            w.Fx("ultflash", a.Center, new FxOpts { color = def.glow, actor = a }); w.Fx("slam", a.pos, new FxOpts { r = 5, color = def.glow, actor = a });
            w.Sfx("mechland", a.pos, a); w.Sfx("ultcall", a.Center, a);
            w.Msg($"{def.name.ToUpperInvariant()} IS BACK", def.color);
            return true;
        }
        static bool Colossus(World w, Actor a)
        {
            // Dawn Colossus Awakening: a pillar of dawnlight, a hop while the frame grows (World scales it up), a landing stomp
            double t = w.time;
            a.Set("titan", t, TITAN_SECS);
            a.Set("ccimmune", t, 1.4);
            a.maxArmor = a.def.armor + TITAN_ARMOR; a.armor = Math.Min(a.maxArmor, a.armor + TITAN_ARMOR);
            a.forced = new Forced { vx = 0, vy = 7, vz = 0, until = t + 0.45, kind = "ascend" };
            w.Sfx("ultcall", a.Center, a); w.Sfx("mechjump", a.pos, a);
            w.Fx("ultflash", a.Center, new FxOpts { color = "#ffd76a", actor = a }); w.Fx("burst", a.Center, new FxOpts { r = 5, color = "#ffd76a" });
            w.Msg("TENKAI-OH · DAWN COLOSSUS AWAKENS", "#ffd76a");
            w.After(1.1, () =>
            {
                if (!a.alive) return;
                var p = a.pos;
                foreach (var x in w.Enemies(a)) if (World.Dist3(x.pos, p) < 9) { w.Damage(a, x, 120, Ab); ApplyCC(w, a, x, "stun", 1); }
                w.Fx("slam", p, new FxOpts { r = 9, color = "#ffd76a", actor = a }); w.Sfx("slam", p, a); w.Sfx("mechland", p, a);
            });
            return true;
        }
        // ================================================================ Mirei
        static bool Constellation(World w, Actor a)
        {
            var linked = w.Allies(a).Where(x => World.Dist3(x.pos, a.pos) < 15 && w.level.LineOfSight(a.Eye, x.Center)).ToList();
            foreach (var x in linked)
            {
                x.Set("linked", w.time, 5, null, a);
                foreach (var s in new[] { "silence", "grounded", "root" }) x.Clear(s);
                w.Fx("link", a.Center, new FxOpts { target = x, actor = a, color = "#bfe8ff", dur = 5 });
            }
            w.Sfx("constellation", a.Center, a);
            return true;
        }
        static bool Wish(World w, Actor a)
        {
            var tg = w.ConeTarget(a, 30, 12, x => x.team == a.team) ?? a;
            w.AddShield(tg, 300, 4, "wish", a);
            w.Fx("wish", tg.Center, new FxOpts { actor = tg, color = "#bfe8ff", dur = 4 }); w.Sfx("wish", tg.Center, a);
            return true;
        }
        static bool RebirthUlt(World w, Actor a)
        {
            // Stellar Rebirth: every teammate who fell in the last 10 s within 15 m stands up where they fell (Rebirth.cs)
            Rebirth.StellarRebirth(w, a);
            return true;
        }
        static bool Nova(World w, Actor a)
        {
            // (the web edition's Nova Requiem)
            foreach (var x in w.Allies(a)) if (World.Dist3(x.pos, a.pos) < 25) { x.Set("hot", w.time, 2.5, 140, a); x.Set("dmgamp", w.time, 4); }
            w.Fx("nova", a.Center, new FxOpts { r = 25, color = "#bfe8ff", actor = a }); w.Sfx("nova", a.Center, a);
            return true;
        }
        // ================================================================ Kaien
        static bool Spiritstep(World w, Actor a)
        {
            if (Sealed(w, a)) return false;
            var from = a.pos;
            var p = BlinkTarget(w, a, MoveDir(a), 10);
            a.pos = p; a.vel = V3.Zero;
            w.Fx("papers", from, new FxOpts { color = "#fff6d8" }); w.Fx("papers", p, new FxOpts { color = "#fff6d8" }); w.Sfx("spiritstep", p, a);
            return true;
        }
        static bool Seal(World w, Actor a)
        {
            var p = w.GroundPoint(a, 20);
            MakeZone(w, a, "seal", p, 7, 6);
            w.Fx("sealcast", p, new FxOpts { r = 7, color = "#ffe28a" }); w.Sfx("seal", p, a);
            return true;
        }
        static bool Sealstorm(World w, Actor a)
        {
            // Divine Seal Storm: for SEALSTORM_SECS the seals swarm - a shield of them on him (re-formed every SEALSTORM_REFORM s
            // once spent) and the rest burst on every enemy within SEALSTORM_R he can see, every SEALSTORM_TICK s
            double t = w.time;
            a.Set("sealstorm", t, SEALSTORM_SECS); a.sv["stormStart"] = t; a.sv["stormNext"] = t + 0.6; a.sv["stormReform"] = t + SEALSTORM_REFORM;
            SealShield(w, a, SEALSTORM_SHIELD, SEALSTORM_SECS);
            w.Fx("sealstorm", a.Center, new FxOpts { r = SEALSTORM_R, color = "#ffe28a", actor = a, dur = SEALSTORM_SECS }); w.Sfx("sanctuary", a.pos, a); w.Sfx("ultcall", a.Center, a);
            return true;
        }
        static bool Sanctuary(World w, Actor a)
        {
            MakeZone(w, a, "sanctuary", a.pos, 10, 5);
            w.Fx("sanctuarycast", a.pos, new FxOpts { r = 10, color = "#ffe28a" }); w.Sfx("sanctuary", a.pos, a);
            return true;
        }
        // ================================================================ Raijin
        static bool Flashstep(World w, Actor a)
        {
            if (Sealed(w, a) || a.Has("root", w.time)) return false;
            var d = MoveDir(a);
            a.sv["dashHits"] = 0;
            a._dashHit = new HashSet<int>();
            Dash(a, d, 12, 0.2, "flashstep", w.time);
            w.Fx("flash", a.Center, new FxOpts { color = "#8ad8ff", actor = a, dur = 0.25 }); w.Sfx("flashstep", a.Center, a);
            return true;
        }
        static bool Parry(World w, Actor a)
        {
            a.Set("parry", w.time, 1.2);
            w.Fx("parrystance", a.Center, new FxOpts { color = "#8ad8ff", actor = a, dur = 1.2 }); w.Sfx("parrystance", a.Center, a);
            return true;
        }
        static bool SusanooUlt(World w, Actor a)
        {
            // Storm Sovereign: the thunder-god giant of himself rises where he stands (Susanoo.cs)
            Susanoo.RaiseSusanoo(w, a);
            w.Fx("ultflash", a.Center, new FxOpts { color = "#8ad8ff", actor = a }); w.Fx("susanoocast", a.Center, new FxOpts { r = 12, color = "#8ad8ff", actor = a, dur = 10 });
            w.Sfx("ultcall", a.Center, a); w.Sfx("thunderclap", a.Center, a);
            w.Msg("RAIJIN · STORM SOVEREIGN", a.def.color);
            return true;
        }
        static bool Judgment(World w, Actor a)
        {
            // (the web edition's Judgment)
            a.Set("judgment", w.time, 6);
            a.cd["flashstep"] = 0;
            w.Fx("ultflash", a.Center, new FxOpts { color = "#8ad8ff", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("thunderclap", a.Center, a);
            return true;
        }
        // ================================================================ Hayate
        static bool Currentdash(World w, Actor a)
        {
            if (Sealed(w, a) || a.Has("root", w.time)) return false;
            var d = MoveDir(a);
            a.sv["dashHits"] = 0;
            a._dashHit = new HashSet<int>();
            Dash(a, d, 15, 0.22, "flashstep", w.time);
            w.Fx("flash", a.Center, new FxOpts { color = "#4fe3c1", actor = a, dur = 0.25 }); w.Sfx("flashstep", a.Center, a);
            return true;
        }
        static bool Mirrorwater(World w, Actor a)
        {
            // after Genji's Deflect: for DEFLECT_SECS everything coming at him from in front is turned on the blade
            if (a.Has("deflect", w.time)) return false;
            a.Set("deflect", w.time, World.DEFLECT_SECS); a.sv["deflectStart"] = w.time; a.anim.deflectN = 0;
            w.Fx("parrystance", a.Center, new FxOpts { color = "#4fe3c1", actor = a, dur = World.DEFLECT_SECS }); w.Sfx("parrystance", a.Center, a);
            return true;
        }
        static bool Dragongate(World w, Actor a)
        {
            // Dragon Gate Blade (after Genji's Dragonblade): the primary becomes a sweeping blade, he moves 30% faster
            if (a.Has("dragonblade", w.time)) return false;
            a.Set("dragonblade", w.time, DRAGONBLADE_SECS);
            a.def = CloneDef(a.baseDef, primary: DRAGONBLADE);
            a.nextShot = w.time + 0.45;                        // the draw
            a.anim.castAt = w.time; a.anim.castId = "dragongate";
            w.Fx("ultflash", a.Center, new FxOpts { color = "#b36bff", actor = a }); w.Sfx("ultcall", a.Center, a);
            // the koi-dragon coils up around him as the nodachi is drawn
            w.Fx("dragoncoil", a.Center, new FxOpts { actor = a, color = "#b36bff" });
            return true;
        }
        // ================================================================ Seiran
        static bool Riverstep(World w, Actor a)
        {
            if (Sealed(w, a) || a.Has("root", w.time)) return false;
            Dash(a, MoveDir(a), 7, 0.18, "lunge", w.time, 2.5);
            a.anim.jumpAt = w.time;
            w.Fx("doublejump", a.pos, new FxOpts { color = "#8ec5ff" }); w.Sfx("doublejump", a.pos, a);
            return true;
        }
        static bool RevealShot(World w, Actor a)
        {
            V3 muz = w.Muzzle(a), aim = w.AimPoint(a, 60);
            w.SpawnProj(a, muz, World.Norm(aim - muz), 90, new ProjOpts { dmg = 20, fx = "reveal", special = "reveal", life = 60.0 / 90, r = 0.15 });
            w.Sfx("bow", muz, a);
            return true;
        }
        static bool Twinkoi(World w, Actor a)
        {
            // two giant spirit koi-dragons spiral out along the aim (flat), straight through walls: each step bites everything
            // within 4.5m of either dragon's head, wherever it is - no line of sight, like Dragonstrike
            var f = a.Forward(); var dir = World.Norm(new V3(f.x, 0, f.z)); var side = new V3(-dir.z, 0, dir.x);
            var o = new V3(a.pos.x + dir.x * 1.5, a.pos.y + 1.2, a.pos.z + dir.z * 1.5);
            var hitAt = new Dictionary<int, double>();
            w.Fx("ultflash", a.Center, new FxOpts { color = "#8ec5ff", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("arrowrain", o, a);
            w.Fx("twinkoi", o, new FxOpts { to = new V3(o.x + dir.x * 45, o.y, o.z + dir.z * 45), color = "#8ec5ff", actor = a });
            for (int k = 0; k <= 18; k++)
            {
                int kk = k;
                w.After(0.25 + kk * 0.1, () =>
                {
                    double along = kk * 2.5, sw = Math.Sin(along * TWIN_W) * TWIN_R;
                    foreach (var sg in new[] { 1, -1 })
                    {
                        var p = new V3(o.x + dir.x * along + side.x * sw * sg, o.y, o.z + dir.z * along + side.z * sw * sg);
                        w.Fx("flash", p, new FxOpts { color = sg > 0 ? "#8ec5ff" : "#3f7fff", dur = 0.35 });
                        foreach (var x in w.Enemies(a))
                        {
                            if (!x.alive || M.Hypot(x.pos.x - p.x, x.pos.z - p.z) > 4.5 || Math.Abs(x.pos.y + 1 - p.y) > 4) continue;
                            if (w.time - (hitAt.TryGetValue(x.id, out var h) ? h : -9) < 0.19) continue;
                            hitAt[x.id] = w.time;
                            w.Damage(a, x, 42, Ab);
                        }
                    }
                });
            }
            return true;
        }
        // ================================================================ Yuzu
        static bool Sunhop(World w, Actor a)
        {
            a.vel.y = 13; a.grounded = false; a.Set("glide", w.time, 1.8); a.anim.jumpAt = w.time;
            w.Fx("sunhop", a.pos, new FxOpts { color = "#ffd27a" }); w.Sfx("sunhop", a.pos, a);
            return true;
        }
        // UNITY DIVERGENCE (the user's rework of 2026-10-03; the web game keeps the TS 3 s rain of 40 arrows): five giant
        // sword-like arrows of sunlight land in a ring round the aim point, one by one (100 each within SUNS_LAND_R of where it
        // lands), then shatter into a swarm of a thousand small arrows that hunts every enemy within SUNS_REACH of the ring for
        // SUNS_SECS (12 every 0.5 s: 360 in all). The sim keeps one zone ("sunswarm") and a per-tick hit on each enemy in reach
        // - no arrow entities; AbilityFx/SunSwarm draws the arrows from the zone's timeline, so a client sees the same from the
        // replicated zone (its id places the ring). Ult charge 2400 (Data/UnityDivergence).
        public const double SUNS_RING = 7, SUNS_REACH = 30, SUNS_SECS = 15, SUNS_LAND_AT = 0.5, SUNS_LAND_STEP = 0.15, SUNS_SPLIT = 1.4,
            SUNS_TICK = 0.5, SUNS_DMG = 12, SUNS_LAND_DMG = 100, SUNS_LAND_R = 3;
        public const int SUNS_N = 5;
        /// <summary>where the zone's i-th giant arrow lands (sim space, on the ground under the ring)</summary>
        public static V3 SunsLanding(World w, Zone z, int i)
        {
            double th = z.id * 2.399963 + i * Math.PI * 2 / SUNS_N, x = z.x + Math.Cos(th) * SUNS_RING, zz = z.z + Math.Sin(th) * SUNS_RING;
            double g = w.level.GroundAt(x, zz, z.y + 4);
            return new V3(x, double.IsInfinity(g) || double.IsNaN(g) || Math.Abs(g - z.y) > 8 ? z.y : g, zz);
        }
        static bool Hundredsuns(World w, Actor a)
        {
            var p = w.GroundPoint(a, 50);
            MakeZone(w, a, "sunswarm", p, SUNS_REACH, SUNS_SPLIT + SUNS_SECS, new Dictionary<string, object> { ["landed"] = 0.0, ["ticks"] = 0.0 });
            w.Sfx("ultcall", a.Center, a);
            return true;
        }
        // ================================================================ Gorgoth
        static bool Plating(World w, Actor a)
        {
            w.AddShield(a, 250, 3, "void", a);
            foreach (var x in w.Allies(a, false)) if (World.Dist3(x.pos, a.pos) < 8) { w.AddShield(x, 100, 3, "void", a); w.Fx("voidshield", x.Center, new FxOpts { actor = x, color = "#ff2244", dur = 3 }); }
            w.Fx("voidshield", a.Center, new FxOpts { actor = a, color = "#ff2244", dur = 3 }); w.Sfx("plating", a.Center, a);
            return true;
        }
        static bool Abysscharge(World w, Actor a)
        {
            var d = FlatDir(a);
            a.sv["pinned"] = 0;
            Dash(a, d, 15, 1.0, "abysscharge", w.time, 0, () =>
            {
                var pin = w.actors.FirstOrDefault(x => x.id == a.Sv("pinned", 0));
                if (pin != null && pin.alive) { w.Damage(a, pin, 60, Ab); ApplyCC(w, a, pin, "stun", 0.6); w.Fx("slam", pin.pos, new FxOpts { r = 2.5, color = "#ff2244" }); w.Sfx("slam", pin.pos, a); }
                a.sv["pinned"] = 0;
            });
            a.Set("charging", w.time, 1.0);
            w.Sfx("charge", a.Center, a); w.Fx("chargetrail", a.Center, new FxOpts { actor = a, color = "#ff2244", dur = 1 });
            return true;
        }
        static bool Nulllance(World w, Actor a)
        {
            var e = new V3(a.pos.x, a.pos.y + a.Height * 0.5, a.pos.z); var d = FlatDir(a);
            a.anim.attackAt = w.time; a.anim.attackKind = "lance";
            // barriers first: the lance is built to crack them
            foreach (var b in w.actors)
            {
                if (!b.alive || !b.barrier.up || b.team == a.team) continue;
                var bf = b.Forward(); double cx = b.pos.x + bf.x * 1.7, cz = b.pos.z + bf.z * 1.7;
                double along = (cx - a.pos.x) * d.x + (cz - a.pos.z) * d.z, lat = Math.Abs((cx - a.pos.x) * -d.z + (cz - a.pos.z) * d.x);
                if (along > 0 && along < 9 && lat < 2.6) w.HitBarrier(b, 70 * 4, a, new V3(cx, b.pos.y + 1.5, cz));
            }
            foreach (var x in w.Enemies(a))
            {
                double vx = x.pos.x - a.pos.x, vz = x.pos.z - a.pos.z;
                double along = vx * d.x + vz * d.z, lat = Math.Abs(vx * -d.z + vz * d.x);
                if (along < 0 || along > 8 + x.Radius || lat > 1.2 + x.Radius || Math.Abs(x.pos.y - a.pos.y) > 3) continue;
                w.Damage(a, x, 70, new DmgOpts { kind = "ability", shieldMult = 4 });
            }
            w.Fx("lance", e, new FxOpts { to = new V3(e.x + d.x * 8, e.y, e.z + d.z * 8), color = "#ff2244", actor = a }); w.Sfx("lance", e, a);
            return true;
        }
        static bool Singularity(World w, Actor a)
        {
            var d = FlatDir(a);
            var p0 = new V3(a.pos.x + d.x * 15, a.pos.y + 1, a.pos.z + d.z * 15);
            var h = w.level.Ray(new V3(a.pos.x, a.pos.y + 1, a.pos.z), d, 15);
            double dd = h.HasValue ? h.Value.t - 1 : 15;
            var p = new V3(a.pos.x + d.x * dd, 0, a.pos.z + d.z * dd);
            p.y = Math.Max(w.level.GroundAt(p.x, p.z, p0.y + 2), a.pos.y - 4);
            MakeZone(w, a, "singularity", p, 10, 2.5);
            w.Fx("singularity", p, new FxOpts { r = 10, color = "#ff2244", dur = 2.5 }); w.Sfx("ultcall", a.Center, a); w.Sfx("singularity", p, a);
            return true;
        }
        // ================================================================ Nocturne
        static bool Silence(World w, Actor a)
        {
            V3 e = a.Eye, d = a.AimDir();
            foreach (var x in w.Enemies(a))
            {
                var c = x.Center; var v = c - e; double l = v.Length;
                if (l > 12 + x.Radius || V3.Dot(v, d) / l < Math.Cos(0.45)) continue;
                if (!w.level.LineOfSight(e, c)) continue;
                if (!ApplyCC(w, a, x, "silence", 1.0)) continue;
                if (x.def.frame == "flyer")
                {
                    x.Set("grounded", w.time, 2.5, null, a); x.flying = false;
                    if (x.def.id == "mirei") w.Counter(a, x, "Silence Aria grounds the Starweaver");
                }
                if (x.forced?.kind == "flashstep") { Interrupt(w, x, a); w.Counter(a, x, "Silence Aria cuts the Flash Step"); }
            }
            w.Fx("soundcone", e, new FxOpts { to = new V3(e.x + d.x * 12, e.y + d.y * 12, e.z + d.z * 12), color = "#ff4d6d", actor = a }); w.Sfx("silence", e, a);
            return true;
        }
        static bool Bloodpact(World w, Actor a)
        {
            var tg = w.ConeTarget(a, 30, 12, x => x.team == a.team) ?? a;
            tg.Set("lifesteal", w.time, 4, 0.3); tg.Set("speed", w.time, 4, 1.25);
            w.Fx("bloodpact", tg.Center, new FxOpts { actor = tg, color = "#ff2d55", dur = 4 }); w.Sfx("bloodpact", tg.Center, a);
            return true;
        }
        static bool Requiem(World w, Actor a)
        {
            foreach (var x in w.actors)
            {
                if (!x.alive || World.Dist3(x.pos, a.pos) > 20) continue;
                if (x.team == a.team) x.Set("hot", w.time, 3, 83, a);
                else x.Set("bleed", w.time, 3, 33, a);
            }
            w.Fx("requiem", a.Center, new FxOpts { r = 20, color = "#ff2d55", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("requiem", a.Center, a);
            return true;
        }
        // ================================================================ Hex
        static bool Marionette(World w, Actor a)
        {
            var tg = w.ConeTarget(a, 20, 9, x => x.team != a.team && w.Perceivable(a, x));
            if (tg == null) return false;
            MakeZone(w, a, "tether", tg.pos, 0, 0.6, new Dictionary<string, object> { ["target"] = tg });
            tg.Set("tethered", w.time, 0.6, null, a);
            w.Fx("strings", a.Center, new FxOpts { target = tg, actor = a, color = "#c77dff", dur = 0.6 }); w.Sfx("strings", a.Center, a);
            w.After(0.6, () =>
            {
                if (!a.alive || !tg.alive || !tg.Has("tethered", w.time - 0.05) || (tg.src.TryGetValue("tethered", out var s) ? s : null) != a) return;
                tg.Clear("tethered");
                if (CcBlocked(w, tg)) { w.Fx("immune", tg.Center, new FxOpts { actor = tg }); return; }
                var d = World.Norm(new V3(a.pos.x - tg.pos.x, 0, a.pos.z - tg.pos.z));
                double dist = Math.Min(8, Math.Max(0, World.Dist3(a.pos, tg.pos) - 2));
                tg.forced = new Forced { vx = d.x * dist / 0.3, vy = 2, vz = d.z * dist / 0.3, until = w.time + 0.3, kind = "pull" };
                w.After(0.3, () => ApplyCC(w, a, tg, "root", 1));
                w.Sfx("yank", tg.Center, a);
            });
            return true;
        }
        static bool Grievous(World w, Actor a)
        {
            V3 muz = w.Muzzle(a), d = a.AimDir();
            w.SpawnProj(a, muz, World.Norm(new V3(d.x, d.y + 0.2, d.z)), 26, new ProjOpts { dmg = 0, fx = "hexbomb", special = "grievous", life = 3, r = 0.25, grav = 16 });
            w.Sfx("throw", muz, a);
            return true;
        }
        static bool Theater(World w, Actor a)
        {
            if (w.full)
            {
                // Grand Puppet Theater: fifty masked puppets rise around him and fight for him (Puppets.cs)
                Puppets.RaisePuppets(w, a);
                w.Fx("theater", a.Center, new FxOpts { r = 12, color = "#c77dff", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("theater", a.Center, a);
                return true;
            }
            // the web edition keeps the original: every enemy within 15m rooted and vulnerable for 2.5s
            foreach (var x in w.Enemies(a))
            {
                if (World.Dist3(x.pos, a.pos) > 15 || !w.level.LineOfSight(a.Eye, x.Center)) continue;
                x.Set("vuln", w.time, 2.5, null, a);
                ApplyCC(w, a, x, "root", 2.5);
                w.Fx("strings", a.Center, new FxOpts { target = x, actor = a, color = "#c77dff", dur = 2.5 });
            }
            w.Fx("theater", a.Center, new FxOpts { r = 15, color = "#c77dff", actor = a }); w.Sfx("ultcall", a.Center, a); w.Sfx("theater", a.Center, a);
            return true;
        }
        // ================================================================ Kagemaru
        static bool Shadowstep(World w, Actor a)
        {
            if (Sealed(w, a)) return false;
            var from = a.pos;
            var aim = w.AimPoint(a, 15);
            var d = World.Norm(new V3(aim.x - a.pos.x, 0, aim.z - a.pos.z));
            double dist = Math.Min(15, M.Hypot(aim.x - a.pos.x, aim.z - a.pos.z));
            var p = BlinkTarget(w, a, d, dist);
            a.pos = p; a.vel = V3.Zero;
            w.Fx("smoke", from, new FxOpts { color = "#5a3d8c" }); w.Fx("smoke", p, new FxOpts { color = "#5a3d8c" }); w.Sfx("shadowstep", p, a);
            return true;
        }
        static bool Veil(World w, Actor a)
        {
            if (Sealed(w, a))
            {
                var k = w.actors.FirstOrDefault(o => o.def.id == "kaien" && o.team != a.team);
                if (k != null) w.Counter(k, a, "Warding Seal denies the Veil of Night");
                return false;
            }
            a.Set("stealth", w.time, 4);
            w.Fx("smoke", a.pos, new FxOpts { color = "#5a3d8c" }); w.Sfx("veil", a.pos, a);
            return true;
        }
        static bool Thousandcuts(World w, Actor a)
        {
            var tgs = JsSort.SortBy(w.Enemies(a).Where(x => World.Dist3(x.pos, a.pos) < 15 && w.level.LineOfSight(a.Eye, x.Center)).ToList(),
                (p, q) => World.Dist3(p.pos, a.pos) - World.Dist3(q.pos, a.pos)).Take(5).ToList();
            if (tgs.Count == 0) return false;
            a.Set("phased", w.time, tgs.Count * 0.2 + 0.1);
            w.Sfx("ultcall", a.Center, a);
            for (int i = 0; i < tgs.Count; i++)
            {
                var x = tgs[i]; int ii = i;
                w.After(0.1 + ii * 0.2, () =>
                {
                    if (!a.alive) return;
                    var from = a.Center;
                    if (x.alive)
                    {
                        var b = x.Forward();
                        var p = new V3(x.pos.x - b.x * 1.4, x.pos.y, x.pos.z - b.z * 1.4);
                        double g = w.level.GroundAt(p.x, p.z, x.pos.y + 1);
                        a.pos = new V3(p.x, g > double.NegativeInfinity ? g : x.pos.y, p.z);
                        a.yaw = a.input.yaw = Math.Atan2(x.pos.x - a.pos.x, x.pos.z - a.pos.z);
                        a.Clear("phased"); w.Damage(a, x, 120, Ab); a.Set("phased", w.time, (tgs.Count - ii) * 0.2);
                        a.anim.attackAt = w.time; a.anim.attackKind = "secondary";
                    }
                    w.Fx("cut", from, new FxOpts { to = a.Center, color = "#9d7bff" }); w.Sfx("cut", a.Center, a);
                });
            }
            return true;
        }
    }
}
