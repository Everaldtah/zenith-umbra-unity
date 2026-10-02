// Ability implementations: shared helpers, the ability table and castAbility. Port of zenith-umbra
// src/game/abilities.ts (the top of the file and castAbility). The kits are in Abilities.Kits1/Kits2.cs, the per-step
// upkeep in Abilities.Tick.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;
using static ZU.Sim.Roles;

namespace ZU.Sim
{
    public static partial class Abilities
    {
        /// <summary>Hayate's Dragon Gate Blade: how long the nodachi stays drawn, and the blade he swings with it</summary>
        public const double DRAGONBLADE_SECS = 8;
        public static readonly SlotDef DRAGONBLADE = new SlotDef { kind = "melee", name = "Dragon Gate Blade", damage = 110, rate = 1.25, range = 5, sfx = "katana", fx = "slash" };
        /// <summary>Tenkai-Oh's Dawn Colossus Awakening: how long the giant stands, and the armor it brings</summary>
        public const double TITAN_SECS = 15, TITAN_ARMOR = 600;

        static int ZID = 1;

        static readonly string[] CC = { "stun", "root", "silence", "grounded", "tethered" };
        static readonly string[] DEBUFF = new[] { "brand", "bleed", "wound", "antiheal", "slow", "vuln", "tidemark" }.Concat(CC).ToArray();

        /// <summary>{ kind: 'ability' } (a fresh one each time: World.Damage never keeps it, but nothing may share state)</summary>
        static DmgOpts Ab => new DmgOpts { kind = "ability" };

        static bool CcBlocked(World w, Actor x) => x.Has("ccimmune", w.time) || x.Has("linked", w.time) || x.Has("tachiai", w.time);
        static bool ApplyCC(World w, Actor src, Actor x, string kind, double dur)
        {
            if (CcBlocked(w, x))
            {
                w.Fx("immune", x.Center, new FxOpts { color = "#bfe8ff", actor = x });
                if (x.Has("linked", w.time) && src.def.id == "nocturne")
                {
                    var m = w.actors.FirstOrDefault(o => o.def.id == "mirei" && o.team == x.team);
                    if (m != null) w.Counter(m, src, "Constellation Link shrugs off Silence Aria");
                }
                return false;
            }
            x.Set(kind, w.time, dur, null, src);
            if (kind == "root" || kind == "stun") { if (x.forced != null && x.forced.kind != "knock") Interrupt(w, x, src); }
            return true;
        }

        static readonly HashSet<string> INTERRUPTIBLE = new HashSet<string> { "abysscharge", "flashstep", "dawndrive", "dawncharge", "chainpull", "sunhop" };
        /// <summary>cancel dashes, charges and channels</summary>
        static bool Interrupt(World w, Actor x, Actor by)
        {
            var f = x.forced;
            if (f != null && INTERRUPTIBLE.Contains(f.kind))
            {
                x.forced = null; x.vel.x *= 0.1; x.vel.z *= 0.1;
                w.Fx("interrupt", x.Center, new FxOpts { color = "#ffffff", actor = x }); w.Sfx("interrupt", x.Center);
                if (f.kind == "abysscharge" && by.def.id == "tenkai") w.Counter(by, x, "Dawn Anchor stops the Abyss Charge");
                if (f.kind == "flashstep" && by.def.id == "enra") w.Counter(by, x, "Chain of Oblivion roots the Flash Step");
                x.Clear("charging"); x.sv["pinned"] = 0;
                return true;
            }
            return false;
        }

        static void Dash(Actor a, V3 dir, double dist, double dur, string kind, double t, double vy = 0, Action onEnd = null)
        {
            a.forced = new Forced { vx = dir.x * dist / dur, vy = vy, vz = dir.z * dist / dur, until = t + dur, kind = kind, ignoreGravity = vy == 0 && kind != "knock", onEnd = onEnd };
        }
        static V3 FlatDir(Actor a) { var f = a.Forward(); return new V3(f.x, 0, f.z); }
        static V3 MoveDir(Actor a)
        {
            var i = a.input;
            if (M.Hypot(i.mx, i.mz) < 0.2) return FlatDir(a);
            double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
            return World.Norm(new V3(fx * i.mz + rx * i.mx, 0, fz * i.mz + rz * i.mx));
        }
        static Zone MakeZone(World w, Actor a, string kind, V3 p, double r, double dur, Dictionary<string, object> data = null)
        {
            var z = new Zone { id = ZID++, kind = kind, owner = a, team = a.team, x = p.x, y = p.y, z = p.z, r = r, born = w.time, until = w.time + dur, next = w.time, data = data };
            w.zones.Add(z);
            return z;
        }
        static bool InZone(Zone z, Actor x) => M.Hypot(x.pos.x - z.x, x.pos.z - z.z) < z.r + x.Radius * 0.5 && x.pos.y > z.y - 2 && x.pos.y < z.y + 6;
        static T ZoneData<T>(Zone z, string key) where T : class => z.data != null && z.data.TryGetValue(key, out var v) ? v as T : null;
        static double ZoneNum(Zone z, string key) => z.data != null && z.data.TryGetValue(key, out var v) ? Convert.ToDouble(v) : 0;

        /// <summary>Grand Dohyo: a hero bound by the ring's chains can't dash, leap or teleport (the chain holds)</summary>
        public static readonly HashSet<string> LEASHED = new HashSet<string> { "spiritstep", "flashstep", "currentdash", "riverstep", "shadowstep", "dawncharge", "abysscharge", "sunhop", "chain", "pilotroll" };
        static bool Sealed(World w, Actor a)
        {
            if (a.Has("sealed", w.time)) { w.Fx("blocked", a.Center, new FxOpts { color = "#ffe28a", actor = a }); w.Sfx("denied", a.pos, a); return true; }
            return false;
        }
        /// <summary>safe teleport destination along a direction</summary>
        static V3 BlinkTarget(World w, Actor a, V3 dir, double dist)
        {
            var o = new V3(a.pos.x, a.pos.y + 1, a.pos.z);
            var h = w.level.Ray(o, dir, dist);
            double d = h.HasValue ? Math.Max(0, h.Value.t - a.Radius - 0.2) : dist;
            for (; d > 0; d -= 0.5)
            {
                var p = new V3(o.x + dir.x * d, 0, o.z + dir.z * d);
                double g = w.level.GroundAt(p.x, p.z, a.pos.y + 1.5 + dir.y * d, a.Radius);
                if (g > double.NegativeInfinity && g > a.pos.y - 6) { p.y = g; return p; }
            }
            return a.pos;
        }
        /// <summary>TS `{ ...def, primary: X }`</summary>
        static HeroDef CloneDef(HeroDef d, SlotDef primary = null) { var c = d.Clone(); if (primary != null) c.primary = primary; return c; }

        static readonly Dictionary<string, Func<World, Actor, bool>> I = new Dictionary<string, Func<World, Actor, bool>>
        {
            // Tenkai-Oh / Haruto
            ["anchor"] = Anchor, ["sunburst"] = Sunburst, ["dawndrive"] = Dawndrive, ["dawncharge"] = Dawncharge, ["shatter"] = Shatter,
            ["pilotroll"] = Pilotroll, ["callmech"] = Callmech, ["colossus"] = Colossus,
            // Mirei
            ["constellation"] = Constellation, ["wish"] = Wish, ["rebirth"] = RebirthUlt, ["nova"] = Nova,
            // Kaien
            ["spiritstep"] = Spiritstep, ["seal"] = Seal, ["sealstorm"] = Sealstorm, ["sanctuary"] = Sanctuary,
            // Raijin
            ["flashstep"] = Flashstep, ["parry"] = Parry, ["susanoo"] = SusanooUlt, ["judgment"] = Judgment,
            // Hayate
            ["currentdash"] = Currentdash, ["mirrorwater"] = Mirrorwater, ["dragongate"] = Dragongate,
            // Seiran
            ["riverstep"] = Riverstep, ["echoarrow"] = RevealShot, ["twinkoi"] = Twinkoi,
            // Yuzu
            ["sunhop"] = Sunhop, ["reveal"] = RevealShot, ["hundredsuns"] = Hundredsuns,
            // Gorgoth
            ["plating"] = Plating, ["abysscharge"] = Abysscharge, ["nulllance"] = Nulllance, ["singularity"] = Singularity,
            // Nocturne
            ["silence"] = Silence, ["bloodpact"] = Bloodpact, ["requiem"] = Requiem,
            // Hex
            ["marionette"] = Marionette, ["grievous"] = Grievous, ["theater"] = Theater,
            // Kagemaru
            ["shadowstep"] = Shadowstep, ["veil"] = Veil, ["thousandcuts"] = Thousandcuts,
            // Enra
            ["chain"] = Chain, ["brand"] = Brand, ["effigy"] = EffigyUlt, ["asura"] = Asura,
            // Gantetsu
            ["tachiai"] = Tachiai, ["taiko"] = Taiko, ["dohyo"] = Dohyo,
            // Hibiki
            ["crossmix"] = Crossmix, ["maxvolume"] = Maxvolume, ["scratch"] = Scratch, ["bassdrop"] = Bassdrop,
            // Tomoe
            ["crescent"] = Crescent, ["warcall"] = Warcall, ["reaping"] = Reaping, ["tide"] = Tide,
        };
        public static bool Has(string id) => I.ContainsKey(id);

        /// <param name="slot">a1 | a2 | ult | alt</param>
        public static bool CastAbility(World w, Actor a, string id, string slot)
        {
            double t = w.time;
            if (id == "none" || !I.ContainsKey(id)) return false;
            // Tomoe: RMB with the blade out calls it back (stuck anywhere) - no cooldown involved
            double fang = a.Sv("fang", 0);
            if (id == "crescent" && fang != 0) { if (fang == 2 || fang == 3) { RecallFang(w, a); return true; } return false; }
            if (slot != "ult" && !a.Ready(id, t)) return false;
            if (a.forced != null && !(a.forced.kind == "knock" || a.forced.kind == "pull") && id != "thousandcuts") return false;
            if (LEASHED.Contains(id) && a.Has("chained", t)) { w.Fx("blocked", a.Center, new FxOpts { color = "#ffd27a", actor = a }); w.Sfx("denied", a.pos, a); return false; }
            if (a.Has("stealth", t) && id != "veil") { a.Clear("stealth"); a.Set("ambush", t, 0.6); }
            bool ok = I[id](w, a);
            if (!ok) return false;
            var def = slot == "a1" ? a.def.ability1 : slot == "a2" ? a.def.ability2 : slot == "ult" ? a.def.ult : (a.def.secondary.IsAbility ? a.def.secondary : null);
            // (a tactician keeps what was banked past full: up to a quarter of the next ultimate)
            if (slot == "ult") { a.ult = IsSub(a, "tactician") ? Math.Min(a.def.ult.charge * TACTICIAN_BANK, Math.Max(0, a.ult - a.def.ult.charge)) : 0; a.ults++; }
            else if (def != null) a.cd[id] = t + def.cooldown * (1 - a.mods.cdr) * (1 - (a.mods.cdrBy.TryGetValue(id, out var by) ? by : 0));   // Stadium cooldown items / powers
            a.anim.castAt = t; a.anim.castId = id;
            w.stats.casts[id] = (w.stats.casts.TryGetValue(id, out var n) ? n : 0) + 1;
            w.Emit(new CastEvent { actor = a, id = id, name = def?.name ?? id });
            return true;
        }
    }
}
