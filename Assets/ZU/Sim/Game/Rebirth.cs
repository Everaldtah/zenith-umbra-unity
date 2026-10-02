// Mirei - Stellar Rebirth: every teammate who fell within the last ten seconds inside her perimeter stands up again where
// they fell, at full health (after Mercy's launch-era Resurrect). Port of zenith-umbra src/game/rebirth.ts.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZU.Sim
{
    public static class Rebirth
    {
        /// <summary>the perimeter (m, through walls)</summary>
        public const double REBIRTH_R = 15;
        /// <summary>how long a fallen teammate's soul can still be called back (s from the death)</summary>
        public const double REBIRTH_WINDOW = 10;
        /// <summary>the reborn: untouchable and unable to fight ('reborn'), unable to walk for the first part ('rising')</summary>
        public const double REBIRTH_GUARD = 2.25, REBIRTH_RISE = 1.5;
        /// <summary>her own guard while she sings them back (s)</summary>
        public const double REBIRTH_SELF_GUARD = 1.5;
        public const string REBIRTH_COLOR = "#ffe9a8";

        /// <summary>a fallen teammate she could still call back</summary>
        public static List<Actor> SoulsOf(World w, Actor a, double r = REBIRTH_R)
        {
            double t = w.time;
            return w.actors.Where(x => x != a && x.team == a.team && !x.alive && !x.IsSummon
                && !x.noRespawn && x.respawnAt > t && t - x.deathAt <= REBIRTH_WINDOW && World.Dist3(x.pos, a.pos) <= r).ToList();
        }

        /// <summary>the soul of a fallen hero the renderer can show: any teammate a living Mirei could still call back</summary>
        public static bool SoulLingers(World w, Actor x)
        {
            double t = w.time;
            if (x.alive || x.IsSummon || x.noRespawn || x.respawnAt <= t || t - x.deathAt > REBIRTH_WINDOW) return false;
            return w.actors.Any(m => m.alive && m.team == x.team && m.def.ult.id == "rebirth");
        }

        /// <summary>stand one fallen hero up where they fell</summary>
        public static void Resurrect(World w, Actor by, Actor x)
        {
            double t = w.time;
            var at = x.pos;
            // (a body that fell into the void, or is sinking through the floor, comes up on the nearest footing)
            double g = w.level.GroundAt(at.x, at.z, at.y + 1.5);
            if (g > double.NegativeInfinity && Math.Abs(g - at.y) < 3) at.y = g;
            w.Respawn(x);
            x.pos = at; x.vel = V3.Zero;
            x.yaw = x.input.yaw = Math.Atan2(by.pos.x - at.x, by.pos.z - at.z);      // facing whoever sang them back
            x.Clear("spawnprot");
            x.Set("reborn", t, REBIRTH_GUARD); x.Set("rising", t, REBIRTH_RISE);
            x.sv["rebornAt"] = t; x.sv["rebornBy"] = by.id;
            w.Fx("rebirth", x.Center, new FxOpts { color = REBIRTH_COLOR, actor = x, dur = REBIRTH_GUARD });
            w.Sfx("rebirth", x.Center, x);
            w.Msg($"{x.def.name.ToUpperInvariant()} RETURNS", REBIRTH_COLOR);
            by.stats["rebirths"] = (by.stats.TryGetValue("rebirths", out var n) ? n : 0) + 1;
        }

        /// <summary>Stellar Rebirth: the cast. Returns how many stood up.</summary>
        public static int StellarRebirth(World w, Actor a)
        {
            double t = w.time;
            var souls = SoulsOf(w, a);
            a.Set("spawnprot", t, REBIRTH_SELF_GUARD);
            a.sv["rebirthAt"] = t;
            w.Fx("rebirthcast", a.Center, new FxOpts { r = REBIRTH_R, color = REBIRTH_COLOR, actor = a, dur = REBIRTH_GUARD });
            w.Sfx("ultcall", a.Center, a); w.Sfx("rebirthcast", a.Center, a);
            foreach (var x in souls) Resurrect(w, a, x);
            w.Msg(souls.Count > 0 ? $"MIREI · STELLAR REBIRTH · {souls.Count} RETURN" : "MIREI · STELLAR REBIRTH", a.def.color);
            return souls.Count;
        }
    }
}
