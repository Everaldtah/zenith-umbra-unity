// What the procedural animator reads each frame (port of the TS render/Animator.ts AnimState and
// CharacterView.animState): gameplay state -> animation state. Everything stays in the simulation's frame (the TS one:
// right-handed, +X = the character's left in model space), so ProcAnimator can run the TS maths unchanged.
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Anim
{
    public sealed class Deflect { public float age, x, y, z; }

    public sealed class AnimState
    {
        public float dt, time;
        public Vector3 vel;                 // world velocity (sim frame)
        public float yaw, pitch;
        public bool grounded, flying; public string frame = "";
        public float attackAge, castAge, hitAge, landAge, jumpAge; public string attackKind = "", castId = "";
        public bool stunned, charging, beam, barrier, rooted;
        public bool parry;                  // a blade deflect held (Raijin's Thunder Parry, Hayate's Mirror Water)
        public bool climb;                  // running up a wall (the Koryu brothers)
        public float charge;                // how far a charge weapon is drawn (0..1)
        public bool melee;                  // primary is a melee weapon (bigger swings, lunges)
        public bool hammer;                 // two-handed hammer (Tenkai-Oh): arms follow the hammer's authored swing path
        public string move = "";            // 'dawncharge' | 'shatter' | 'jets' | 'reaping' | 'tide' | ''
        public float swingSide = 1;         // +1 sweeps right-to-left, -1 left-to-right (swings alternate)
        public float? swingSecs;            // a hammer swing's length when it isn't SWING_TIME (a giant's sweep cadence)
        public bool angel, gliding;         // Mirei: angelic flight / slow-fall glide
        public bool dead; public float deathAge;
        public float attackTime, reloadLeft; public float? reloadDur;
        public float scale = 1;             // world metres per model unit
        public Vector3 pos;                 // actor world position (feet, sim frame)
        public string hero = "";
        public Vector2? hitDir;             // model-space direction TO the last attacker (x = the character's left, y = front)
        public bool knocked;                // shoved / pulled / launched
        public float swoop = -1, swoopFlare = 9;
        public bool superjump, slingshot;
        public Vector2? dual;               // twin chainguns: seconds since each gun last fired (x = left, y = right)
        public bool rush;                   // Gantetsu's Tachiai Rush
        public float? tide;                 // Tomoe's Crescent Warpath: seconds into the dash
        public Deflect deflect;             // a shot just turned on the blade
        public bool skyward; public float? skyRise; public float skyStrike = -1;   // Raijin's Susanoo calling the thunder
        public float? rebirth, rising;      // Mirei singing the fallen back / a hero just called back
        public bool leap;                   // Gantetsu's Shiko leap
        public float knockdown;             // knocked flat: seconds left
        public bool skate; public float grind;   // Hibiki: mag-skates / Mag-Grind wall side

        /// <summary>CharacterView.animState: `hammer` = the view carries a hammer-frame prop; `pos` = where the body is drawn
        /// (sim frame); `scale` = world metres per model unit</summary>
        public static AnimState From(Actor a, double time, float dt, bool hammer, Vector3 pos, float scale, AnimState s = null)
        {
            s ??= new AnimState();
            var an = a.anim; var d = a.def; var p = d.primary; double t = time;
            s.dt = dt; s.time = (float)t;
            s.vel = new Vector3((float)a.vel.x, (float)a.vel.y, (float)a.vel.z); s.yaw = (float)a.yaw; s.pitch = (float)a.pitch;
            // a swoop skimming the floor is still flight (no running gait at 20 m/s)
            s.grounded = a.grounded && !a.Has("swoop", t); s.flying = a.flying || d.frame == "drone"; s.frame = d.frame ?? "";
            s.attackAge = (float)(t - an.attackAt); s.attackKind = an.attackKind ?? ""; s.castAge = (float)(t - an.castAt); s.castId = an.castId ?? ""; s.hitAge = (float)(t - an.hitAt);
            s.landAge = (float)(t - an.landAt); s.jumpAge = (float)(t - an.jumpAt); s.stunned = a.Has("stun", t); s.charging = a.charging;
            s.parry = a.Has("parry", t) || a.Has("deflect", t); s.climb = a.Has("wallclimb", t);
            s.deflect = DeflectState(a, t, s.deflect);
            bool sus = d.id == "susanoo";
            s.skyward = sus && a.Sv("phase", 0) == 0;
            s.skyRise = sus ? (float?)(t - a.Sv("riseAt", t)) : null;
            s.skyStrike = sus && a.Sv("strikes", 0) > 0 ? (float)(t - a.Sv("strikeAt", t)) : -1;
            s.rebirth = d.id == "mirei" && a.sv.TryGetValue("rebirthAt", out var rb) && t - rb < 2.4 ? (float?)(t - rb) : null;
            s.rising = a.Has("rising", t) && a.sv.TryGetValue("rebornAt", out var ra) ? (float?)(t - ra) : null;
            s.charge = (float)a.charge; s.beam = a.beamOn || a.flameOn;
            s.barrier = a.barrier.up; s.rooted = a.Has("root", t); s.scale = scale; s.pos = pos;
            var sec = d.secondary;
            s.melee = p.kind == "melee" || (an.attackKind == "secondary" && sec != null && !sec.IsAbility && sec.kind == "melee");
            s.hammer = hammer; s.swingSide = (float)an.attackSide; s.swingSecs = d.id == "enra_effigy" ? (float?)Effigy.EFFIGY_HIT_EVERY : null;
            s.move = a.forced?.kind == "dawncharge" ? "dawncharge" : an.castId == "shatter" && t - an.castAt < 0.8 ? "shatter"
                : a.forced?.kind == "tide" ? "tide" : an.castId == "reaping" && t - an.castAt < Held.REAP_SECS + Held.REAP_STOP ? "reaping"
                : a.flying && (d.jets ?? 0) != 0 ? "jets" : "";
            s.angel = d.id == "mirei"; s.gliding = a.Has("angelglide", t);
            s.hero = d.id;
            s.hitDir = HitDir(a); s.knocked = a.forced != null && (a.forced.kind == "knock" || a.forced.kind == "pull");
            s.swoop = a.Has("swoop", t) ? (float)a.Sv("swoopProg", 0) : -1;
            s.swoopFlare = a.Has("swoopflare", t) ? (float)(0.4 - (a.St("swoopflare") - t)) : 9;
            s.superjump = a.Has("superjump", t); s.slingshot = a.Has("slingshot", t); s.rush = a.Has("tachiai", t);
            s.leap = a.Has("stompair", t); s.knockdown = a.Has("knockdown", t) ? (float)System.Math.Max(0, a.St("knockdown") - t) : 0;
            s.tide = a.forced?.kind == "tide" ? (float?)System.Math.Max(0, t - a.Sv("tideT0", an.castAt)) : null;
            s.skate = d.id == "hibiki"; s.grind = a.Has("grinding", t) ? (float)a.Sv("grindSide", 1) : 0;
            s.dual = d.dualGuns ? (Vector2?)new Vector2((float)(t - an.fireL), (float)(t - an.fireR)) : null;
            s.reloadLeft = (float)System.Math.Max(0, a.reloadUntil - t); s.reloadDur = p.reload.HasValue ? (float?)p.reload.Value : null;
            s.attackTime = an.attackKind == "primary" ? 1 / (float)System.Math.Max(0.1, p.rate) : sec != null && !sec.IsAbility ? 1 / (float)System.Math.Max(0.1, sec.rate) : 0.6f;
            s.dead = !a.alive; s.deathAge = a.alive ? 0 : (float)(t - a.deathAt);
            return s;
        }

        /// <summary>Hayate's Mirror Water: the last turned shot (age, and the way it came from in model space)</summary>
        static Deflect DeflectState(Actor a, double time, Deflect o)
        {
            double age = time - a.anim.deflectAt;
            if (age > 0.3) return null;
            var d = a.anim.deflectDir; double cy = System.Math.Cos(a.yaw), sy = System.Math.Sin(a.yaw);
            o ??= new Deflect();
            o.age = (float)age; o.x = (float)(d.x * cy - d.z * sy); o.y = (float)d.y; o.z = (float)(d.x * sy + d.z * cy);
            return o;
        }

        /// <summary>model-space direction to whoever hit us last (x = the character's left, y = front)</summary>
        static Vector2? HitDir(Actor a)
        {
            var b = a.lastHitBy;
            if (b == null) return null;
            double dx = b.pos.x - a.pos.x, dz = b.pos.z - a.pos.z, l = System.Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-3) return null;
            double cy = System.Math.Cos(a.yaw), sy = System.Math.Sin(a.yaw);
            return new Vector2((float)((dx * cy - dz * sy) / l), (float)((dx * sy + dz * cy) / l));
        }
    }
}
