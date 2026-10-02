// World main step: timers, controllers, every actor's statuses + abilities, projectiles, zones, packs, objectives.
// Port of zenith-umbra src/game/World.ts ("main step", "health packs", "Mikoshi Rush", updateActor).
using System;
using System.Collections.Generic;
using System.Linq;
using static ZU.Sim.Roles;

namespace ZU.Sim
{
    public partial class World
    {
        public void Step(double dt)
        {
            time += dt;
            double t = time;
            if (timers.Count > 0)
            {
                var due = timers.Where(x => x.at <= t).ToList();
                timers = timers.Where(x => x.at > t).ToList();
                foreach (var d in due) d.fn();
            }
            // Stadium Armory: everyone waits at spawn while the teams shop
            if (!(stadium?.frozen ?? false))
            {
                // JS for...of over a live array also visits actors appended mid-loop (summons): index it, don't snapshot
                for (int k = 0; k < actors.Count; k++) { var a = actors[k]; if (a.alive && a.controller != null) a.controller.Think(dt); }
            }
            else
            {
                foreach (var a in actors) { var i = a.input; i.mx = i.mz = 0; i.fire = i.alt = i.a1 = i.a2 = i.ult = i.jump = i.jumpHeld = i.melee = i.reload = i.swoop = false; }
            }
            for (int k = 0; k < actors.Count; k++) UpdateActor(actors[k], dt);
            Separate();
            projs = projs.Where(p => StepProj(p, dt)).ToList();
            Abilities.TickAbilities(this, dt);
            Puppets.TickPuppets(this, dt); Susanoo.TickSusanoo(this);
            zones = zones.Where(z => z.until > t).ToList();
            UpdatePacks();
            if (director != null) director.Update(dt);
            else if (stadium != null) stadium.Update(dt);
            else if (rules == "push") UpdatePush(dt);
            else if (mode != "training") UpdatePoint(dt);
        }

        // ------------------------------------------------------------------ health packs
        void UpdatePacks()
        {
            double t = time;
            foreach (var p in packs)
            {
                if (t < p.readyAt) continue;
                foreach (var a in actors)
                {
                    if (!a.alive || a.isRobot || a.isBoss || M.Hypot(a.pos.x - p.x, a.pos.z - p.z) > 1.1 || Math.Abs(a.pos.y - p.y) > 1.3) continue;
                    bool dot = a.Has("burning", t) || a.Has("brand", t) || a.Has("bleed", t) || a.wounds.Count > 0;
                    if (a.Health >= a.MaxHp - 0.5 && !dot) continue;
                    // a pack heals through healing reduction and burns away damage over time (as in Overwatch)
                    var P = p.big ? PACK_BIG : PACK_SMALL;
                    double left = P.hp + (IsSub(a, "flanker") ? FLANKER_PACK : 0);       // flankers live off the packs
                    double h = Math.Min(left, a.def.hp - a.hp); a.hp += h; left -= h;
                    double ar = Math.Min(left, a.maxArmor - a.armor); a.armor += ar;
                    foreach (var s in new[] { "burning", "brand", "bleed", "wound" }) a.Clear(s);
                    a.wounds = new List<Wound>();
                    p.readyAt = t + P.respawn;
                    a.stats["packs"] = (a.stats.TryGetValue("packs", out var n) ? n : 0) + 1;
                    Emit(new DmgEvent { src = a, tgt = a, amt = h + ar, crit = false, heal = true, pos = a.Center });
                    Fx("healthpack", new V3(p.x, p.y + 0.5, p.z), new FxOpts { color = "#7dffb0", r = p.big ? 1.6 : 1 }); Sfx("healthpack", new V3(p.x, p.y, p.z), a);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ Mikoshi Rush
        void UpdatePush(double dt)
        {
            double t = time; var Mp = push;
            if (winner != null) return;
            if (t < Mp.unlockAt) { Mp.pos = PathAt(Mp.d); return; }
            if (t - dt < Mp.unlockAt) { Msg("THE MIKOSHI RISES - PUSH IT HOME"); Sfx("announce"); }
            var on = new Dictionary<string, int> { ["zenith"] = 0, ["umbra"] = 0 };
            foreach (var a in actors) if (a.alive && !a.isRobot && M.Hypot(a.pos.x - Mp.pos.x, a.pos.z - Mp.pos.z) < 5 && Math.Abs(a.pos.y - Mp.pos.y) < 4) { on[a.team]++; a.objTime += dt; }
            Mp.contested = on["zenith"] > 0 && on["umbra"] > 0;
            Mp.owner = Mp.contested ? Mp.owner : on["zenith"] > 0 ? "zenith" : on["umbra"] > 0 ? "umbra" : null;
            if (!Mp.contested && Mp.owner != null)
            {
                int n = on[Mp.owner]; double sp = FLOAT_SPEED * (1 + 0.1 * (Math.Min(3, n) - 1));
                Mp.d += (Mp.owner == "zenith" ? 1 : -1) * sp * dt;
                Mp.best["zenith"] = Math.Max(Mp.best["zenith"], Mp.d); Mp.best["umbra"] = Math.Max(Mp.best["umbra"], -Mp.d);
                // checkpoints every sixth of the route: the pushing team is told, the defenders hear the alarm
                int cp = (int)Math.Floor(Math.Abs(Mp.d) / (Mp.half / 3));
                if (cp > Mp.checkpoint && cp < 3)
                {
                    Mp.checkpoint = cp;
                    Msg($"{(Mp.owner == "zenith" ? "ZENITH" : "UMBRA")} REACHES CHECKPOINT {cp}", Mp.owner == "zenith" ? "#5cc8ff" : "#ff3b5c"); Sfx("capture");
                }
            }
            Mp.pos = PathAt(Mp.d);
            if (Mp.d >= Mp.half - 0.5) { End("zenith"); return; }
            if (-Mp.d >= Mp.half - 0.5) { End("umbra"); return; }
            // time: the furthest push wins; a push still moving the float (and contested) plays on in overtime
            if (t > timeLimit)
            {
                string lead = Mp.best["zenith"] >= Mp.best["umbra"] ? "zenith" : "umbra";
                Mp.overtime = Mp.contested || (Mp.owner != null && Mp.owner != lead);
                if (!Mp.overtime || t > timeLimit + 30) End(Mp.best["zenith"] == Mp.best["umbra"] ? (Mp.d >= 0 ? "zenith" : "umbra") : lead);
            }
        }

        /// <summary>a button went down this tick (k: a1 | a2 | ult | alt | jump | fire | melee | swoop | descend)</summary>
        public bool Pressed(Actor a, string k)
        {
            prevIn.TryGetValue(a.id, out var p);
            var i = a.input;
            bool now = k switch { "a1" => i.a1, "a2" => i.a2, "ult" => i.ult, "alt" => i.alt, "jump" => i.jump, "fire" => i.fire, "melee" => i.melee, "swoop" => i.swoop, "descend" => i.descend, _ => false };
            bool before = p != null && (k switch { "a1" => p.a1, "a2" => p.a2, "ult" => p.ult, "alt" => p.alt, "jump" => p.jump, "fire" => p.fire, "melee" => p.melee, "swoop" => p.swoop, "descend" => p.descend, _ => false });
            return now && !before;
        }

        void UpdateActor(Actor a, double dt)
        {
            double t = time;
            // Hayate's Dragon Gate Blade ran out (or he fell with it drawn): his own weapons back, full magazine
            if (a.def != a.baseDef && a.def.id == "hayate" && !a.Has("dragonblade", t)) { a.def = a.baseDef; a.ammo = a.MaxAmmo; a.nextShot = Math.Max(a.nextShot, t + 0.3); }
            if (!a.alive)
            {
                if (a.respawnAt != 0 && t >= a.respawnAt && winner == null) Respawn(a);
                return;
            }
            // ---- statuses
            bool Per(string k) => a.Has(k, t);
            Actor Src(string k) => a.src.TryGetValue(k, out var s) ? s : null;
            if (Per("brand")) Damage(Src("brand"), a, 12 * dt, new DmgOpts { kind = "dot", noLifesteal = true });
            if (Per("bleed")) Damage(Src("bleed"), a, a.Sv("bleed", 33) * dt, new DmgOpts { kind = "dot", noLifesteal = true });
            if (Per("hot") && Src("hot") != null) Heal(Src("hot"), a, a.Sv("hot", 0) * dt, true);
            if (Per("burning")) Damage(Src("burning"), a, 16 * dt, new DmgOpts { kind = "dot", noLifesteal = true });
            if (a.wounds.Count > 0) BleedWounds(a, dt);
            if (a.def.id == "gantetsu") { var s = a.shields.FirstOrDefault(x => x.kind == "roar"); if (s != null && t - a.Sv("roarAt", 0) > 2) s.amt -= 12 * dt; }
            // Bass Drop's temporary health holds for a beat, then fades out over 6s
            { var s = a.shields.FirstOrDefault(x => x.kind == "bassdrop"); if (s != null && t - a.Sv("bassAt", 0) > 0.8) s.amt -= 750.0 / 6 * dt; }
            if (Per("linked") && Src("linked") != null) Heal(Src("linked"), a, 25 * dt, true);
            // everyone regenerates once the shooting stops (20 HP/s after 5 s), health first, then armor; Enra's Oni
            // Blood starts at 2.5 s. (Robots and summons keep their own rules.)
            if (!a.isRobot && !a.IsSummon && !a.isBoss && a.lastDamagedAt >= 0 && t - a.lastDamagedAt > (a.def.id == "enra" ? ONI_REGEN_DELAY : REGEN_DELAY))
            {
                double r = REGEN_RATE * dt, h = Math.Max(0, Math.Min(r, a.def.hp - a.hp));
                a.hp += h;
                if (r - h > 0 && a.armor < a.maxArmor) a.armor = Math.Min(a.maxArmor, a.armor + (r - h));
            }
            if (a.isRobot && !a.IsSummon && t - a.lastDamagedAt > 4) a.hp = Math.Min(a.def.hp, a.hp + 40 * dt);
            // the spawn room heals quickly (not the web build's legacy match)
            if (full && mode != "campaign" && t - a.lastDamagedAt > 1.5 && M.Hypot(a.pos.x - a.spawn[0], a.pos.z - a.spawn[1]) < 7)
            {
                a.hp = Math.Min(a.def.hp, a.hp + 150 * dt); a.armor = Math.Min(a.maxArmor, a.armor + 150 * dt);
            }
            a.shields = a.shields.Where(s => s.until > t && s.amt > 0.5).ToList();
            if (a.st.TryGetValue("asura", out var asura) && asura != 0 && !Per("asura") && a.scale > 1) { a.scale = 1; a.maxArmor = a.def.armor; a.armor = Math.Min(a.armor, a.maxArmor); }
            // Dawn Colossus: grow into the giant over ~1s, hold it for the ult's duration, then shrink back and drop the bonus armor
            if (a.st.ContainsKey("titan"))
            {
                bool on = Per("titan");
                a.scale += ((on ? TITAN_SCALE : 1) - a.scale) * Math.Min(1, dt * (on ? 2.6 : 3.2));
                if (!on && a.scale < 1.02)
                {
                    a.scale = 1; a.maxArmor = a.def.armor; a.armor = Math.Min(a.armor, a.maxArmor); a.Clear("titan");
                    Fx("ultflash", a.Center, new FxOpts { color = a.def.glow, actor = a }); Sfx("barrierbreak", a.Center, a);
                }
            }
            if (!a.alive) return;
            a.ult = Math.Max(a.ult, Math.Min(a.def.ult.charge, a.ult + (a.def != a.baseDef ? 20 : mode == "aitest" ? 30 : 5) * dt));   // (never clamps a tactician's bank)
            if (a.barrier.max > 0 && !a.barrier.up && t > a.barrier.regenAt && t > a.barrier.brokenUntil) a.barrier.hp = Math.Min(a.barrier.max, a.barrier.hp + 150 * dt);
            if (a.Has("stealth", t) && (a.Has("revealed", t) || a.Has("sealed", t)))
            {
                a.Clear("stealth");
                var k = actors.FirstOrDefault(x => x.def.id == "kaien" && x.team != a.team);
                if (a.def.id == "kagemaru" && k != null && a.Has("sealed", t)) Counter(k, a, "Warding Seal tears away the Veil of Night");
            }
            Move(a, dt);
            if (!a.alive) return;
            bool stunned = a.Has("stun", t) || a.Has("reborn", t);       // (the reborn can't fight until their guard ends)
            if (!stunned && !a.Has("phased", t))
            {
                Weapons.UpdateWeapons(this, a, dt);
                // Crescent Warpath: a click drops her out of the flight where she is
                if (a.forced?.kind == "tide" && (Pressed(a, "fire") || Pressed(a, "alt")) && t - a.Sv("tideT0", t) > Abilities.TIDE_HOLD) a.forced.until = t;
                bool silenced = a.Has("silence", t);
                if (!silenced)
                {
                    if (a.forced?.kind == "dawncharge" && Pressed(a, "a1") && t - a.Sv("chargeStart", 0) > 0.3) a.forced.until = t;
                    else if (a.Has("tachiai", t) && Pressed(a, "a1") && t - a.Sv("rushStart", 0) > 0.3) a.Clear("tachiai");
                    else if (Pressed(a, "a1")) Abilities.CastAbility(this, a, a.def.ability1.id, "a1");
                    if (Pressed(a, "a2"))
                    {
                        if (a.Has("deflect", t) && t - a.Sv("deflectStart", 0) >= DEFLECT_MIN) a.Clear("deflect");     // Mirror Water ends on E again
                        else Abilities.CastAbility(this, a, a.def.ability2.id, "a2");
                    }
                    if (Pressed(a, "ult") && a.ult >= a.def.ult.charge) Abilities.CastAbility(this, a, a.def.ult.id, "ult");
                    var S = a.def.secondary;
                    if (S.IsAbility && !S.hold && Pressed(a, "alt")) Abilities.CastAbility(this, a, S.id, "alt");
                }
            }
            else { a.barrier.up = false; a.beamOn = false; a.flameOn = false; a.charging = false; }
            var i = a.input;
            prevIn[a.id] = new PrevInput { a1 = i.a1, a2 = i.a2, ult = i.ult, alt = i.alt, jump = i.jump, fire = i.fire, melee = i.melee, swoop = i.swoop, descend = i.descend };
        }
    }
}
