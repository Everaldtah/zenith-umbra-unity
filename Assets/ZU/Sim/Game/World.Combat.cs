// World combat: damage, wounds, healing, ultimate charge, shields, the mech pilot's ejection, kills.
// Port of zenith-umbra src/game/World.ts (the "combat" section).
using System;
using System.Collections.Generic;
using System.Linq;
using static ZU.Sim.Roles;

namespace ZU.Sim
{
    public partial class World
    {
        public double Damage(Actor src, Actor tgt, double amount, DmgOpts o = null)
        {
            o ??= new DmgOpts();
            double t = time;
            if (!tgt.alive || amount <= 0) return 0;
            if (src != null && src.team == tgt.team && src != tgt) return 0;
            if (tgt.Has("phased", t) || tgt.Has("spawnprot", t) || tgt.Has("reborn", t)) return 0;
            if (tgt.Has("parry", t) && o.kind == "melee" && src != null)
            {
                src.Set("stun", t, 1);
                Sfx("parry", tgt.Center); Fx("parry", tgt.Center, new FxOpts { color = "#8ad8ff", actor = tgt });
                if (src.def.id == "enra") Counter(tgt, src, "Thunder Parry stuns Enra");
                return 0;
            }
            // Mirror Water (Genji's Deflect): a blow or a shot from in front of him is turned on the blade - melee stops dead,
            // hitscan fire goes back out along his aim at whoever is there
            if (src != null && tgt.Has("deflect", t) && (o.kind == "melee" || o.kind == "hitscan") && Deflected(tgt, src.pos, tgt.Center))
            {
                if (o.kind == "hitscan")
                {
                    V3 e = tgt.Eye, d = AimDir(tgt);
                    var lh = level.Ray(e, d, 60); double max = lh.HasValue ? lh.Value.t : 60;
                    var ah = RayActors(e, d, max, x => x.team != tgt.team && x != tgt);
                    double end = ah != null ? ah.t : max; var endP = new V3(e.x + d.x * end, e.y + d.y * end, e.z + d.z * end);
                    Fx("tracer", e, new FxOpts { to = endP, color = tgt.def.glow, actor = tgt });
                    if (ah != null) Damage(tgt, ah.actor, amount, new DmgOpts { crit = o.crit, kind = "hitscan" });
                }
                return 0;
            }
            double dmg = amount;
            // Stadium: weapon / ability power
            if (src != null) dmg *= 1 + (o.kind == "ability" || o.kind == "dot" ? src.mods.ability : src.mods.weapon);
            if (src != null && src.Has("dmgamp", t)) dmg *= 1.3;
            if (src != null && src.Has("titan", t)) dmg *= 1.25;
            if (tgt.Has("vuln", t)) dmg *= 1.3;
            else if (tgt.Has("tidemark", t)) dmg *= Abilities.TIDE_MARK_AMP;     // Tomoe's mark (doesn't stack on the Puppeteer's)
            bool taiko = tgt.Has("taiko", t);
            if (taiko) { tgt.mitigated += dmg * TAIKO_DR; dmg *= 1 - TAIKO_DR; }
            // bruiser tanks shrug off a quarter of every critical hit
            if (o.crit && IsSub(tgt, "bruiser")) { tgt.mitigated += dmg * (1 - BRUISER_CRIT); dmg *= BRUISER_CRIT; }
            if (src != null && src.Has("ambush", t) && o.kind != "dot") { dmg += 50; src.Clear("ambush"); }
            // Hex: Stitched Decoy eats one huge hit
            if (tgt.def.id == "hex" && dmg > 90 && tgt.Ready("decoy", t))
            {
                tgt.cd["decoy"] = t + 15;
                Fx("decoy", tgt.Center, new FxOpts { color = "#c77dff", actor = tgt }); Sfx("decoy", tgt.Center);
                if (src?.def.id == "yuzu") Counter(tgt, src, "Stitched Decoy eats the Dawnshot");
                return 0;
            }
            double dealt = 0, shielded = 0;
            // shields first (temporary health: it feeds the attacker's ultimate at half rate)
            double sm = o.shieldMult ?? 1;
            foreach (var s in tgt.shields)
            {
                if (dmg <= 0) break;
                double take = Math.Min(s.amt, dmg * sm);
                s.amt -= take; dmg -= take / sm; dealt += take; shielded += take;
                (s.src ?? tgt).mitigated += take;
                if (s.amt <= 0 && sm > 1 && s.kind == "wish" && src?.def.id == "gorgoth") Counter(src, tgt, "Null Lance shatters Wish Barrier");
            }
            tgt.shields = tgt.shields.Where(s => s.amt > 0.5).ToList();
            if (dmg > 0 && tgt.armor > 0)
            {
                // armor takes 30% off every hit; wounds bleed straight through it; armor and Taiko together never take
                // more than MITIGATION_CAP off a hit
                double armorMult = o.wound ? 1 : Math.Max(0.7, (1 - MITIGATION_CAP) / (taiko ? 1 - TAIKO_DR : 1));
                double eff = dmg * armorMult;
                double take = Math.Min(tgt.armor, eff);
                tgt.armor -= take; dealt += take; dmg -= take / (eff / dmg);
            }
            if (dmg > 0)
            {
                double floor = tgt.Has("undying", t) ? 1 : 0;
                double take = Math.Min(dmg, Math.Max(0, tgt.hp - floor));
                tgt.hp -= take; dealt += take;
                if (floor > 0 && tgt.hp <= 1.01 && take < dmg) Fx("undying", tgt.Center, new FxOpts { color = "#ffe28a", actor = tgt });
            }
            if (dealt <= 0) return 0;
            tgt.lastDamagedAt = t;
            tgt.anim.hitAt = t;
            if (src != null && src != tgt)
            {
                tgt.lastHitBy = src; tgt.lastHitAt = t; tgt.sv["lastHitDmg"] = dealt;
                if (!attackers.TryGetValue(tgt.id, out var m)) attackers[tgt.id] = m = new Dictionary<int, double>();
                m[src.id] = t;
                // shooting a summoned puppet, or a puppet's own claws, never feeds an ultimate or the damage column
                if (!tgt.IsSummon) src.dmgDone += dealt;
                // ...nor a tank's full share: damage into a tank charges 40% slower, damage into temporary health half as much
                if (!tgt.IsSummon && !SUMMON_ABILITIES.Contains(o.ability ?? "")) GainUlt(src, (dealt - shielded + shielded * OVERHEALTH_ULT) * (tgt.def.role == "tank" ? VS_TANK_ULT : 1));
                // the damage role: whoever they hit heals 15% less for 2 s (so focus fire sticks through a healer)
                if (src.def.role == "dps" && !tgt.IsSummon) tgt.Set("healcut", t, HEALCUT_SECS, null, src);
                // sharpshooters: a critical hit refunds some of the movement ability; recon: a hurt target is revealed
                if (o.crit && IsSub(src, "sharpshooter") && MOVE_ABILITY.TryGetValue(src.def.id, out var id) && src.cd.TryGetValue(id, out var cdv) && cdv > t)
                    src.cd[id] = Math.Max(t, cdv - dealt * SHARPSHOOTER_CD);
                if (IsSub(src, "recon") && tgt.alive && tgt.Health < tgt.MaxHp * 0.5) tgt.Set("revealed", t, RECON_REVEAL);
                if (src.def.id == "yuzu") tgt.Set("marked", t, 3);
                if (src.def.id == "gorgoth") src.armor = Math.Min(src.maxArmor, src.armor + dealt * 0.05);
                // Gantetsu - Roar of the Crowd: critical hits turn half their damage into temporary health (max 150)
                if (src.def.id == "gantetsu" && o.crit)
                {
                    var s = src.shields.FirstOrDefault(x => x.kind == "roar");
                    double before = s != null ? s.amt : 0;
                    if (s != null) { s.amt = Math.Min(150, s.amt + dealt * 0.5); s.until = t + 60; }
                    else src.shields.Add(new Shield { amt = Math.Min(150, dealt * 0.5), until = t + 60, kind = "roar" });
                    src.stats["roar"] = (src.stats.TryGetValue("roar", out var rr) ? rr : 0) + Math.Min(150, before + dealt * 0.5) - before;
                    src.sv["roarAt"] = t;
                }
                if (!o.noLifesteal)
                {
                    double ls = (src.Has("lifesteal", t) ? src.Sv("lifesteal", 0.3) : 0) + src.mods.lifesteal;
                    if (src.Has("asura", t)) ls += 0.3;
                    if (ls > 0) Heal(src, src, dealt * ls, true);
                    if (src.def.id == "nocturne")
                    {
                        var hurt = JsSort.SortBy(Allies(src).Where(x => x.Health < x.MaxHp && Dist3(x.pos, src.pos) < 20).ToList(), (p, q) => p.Health / p.MaxHp - q.Health / q.MaxHp).FirstOrDefault();
                        if (hurt != null) Heal(src, hurt, dealt * 0.5, true);
                    }
                }
            }
            Emit(new DmgEvent { src = src, tgt = tgt, amt = dealt, crit = o.crit, pos = tgt.Center });
            if (tgt.Health <= 0.01 && tgt.hp <= 0.01) Kill(tgt, src);
            return dealt;
        }

        /// <summary>open wounds bleed; Tomoe drinks from her own (Blood Tide: heals 150% of the wound damage she deals)</summary>
        void BleedWounds(Actor a, double dt)
        {
            double t = time;
            a.wounds = a.wounds.Where(w => w.until > t).ToList();
            foreach (var w in a.wounds.ToList())
            {
                double dealt = Damage(w.src, a, w.dps * dt, new DmgOpts { kind = "dot", noLifesteal = true, wound = true });
                if (dealt > 0 && w.src.alive && w.src.def.id == "tomoe")
                {
                    double h = Heal(w.src, w.src, dealt * 1.5, true);
                    w.src.stats["bloodtide"] = (w.src.stats.TryGetValue("bloodtide", out var b) ? b : 0) + h;
                }
                if (!a.alive) break;
            }
        }

        public double Heal(Actor src, Actor tgt, double amount, bool quiet = false)
        {
            if (!tgt.alive || amount <= 0 || tgt.team != src.team) return 0;
            double amt = amount * (1 + src.mods.healing);
            if (tgt.Has("antiheal", time)) amt *= 0.2;
            if (tgt.Has("healcut", time)) amt *= 1 - HEALCUT;          // hit by a damage hero in the last 2 s
            double room = tgt.def.hp - tgt.hp;
            double h = Math.Min(room, amt);
            tgt.hp += Math.Max(0, h);
            // Stadium: healing past full health restores bought armor
            if (stadium != null && amt > h && tgt.armor < tgt.maxArmor) { double ar = Math.Min(tgt.maxArmor - tgt.armor, amt - Math.Max(0, h)); tgt.armor += ar; h = Math.Max(0, h) + ar; }
            if (h <= 0) return 0;
            if (src != tgt)
            {
                src.healDone += h; GainUlt(src, h * (tgt.def.role == "tank" ? VS_TANK_ULT : 1));
                // medics: healing others heals them
                if (IsSub(src, "medic") && src.hp < src.def.hp) src.hp = Math.Min(src.def.hp, src.hp + h * MEDIC_SELF);
            }
            if (src != tgt) { tgt.sv["healedBy"] = src.id; tgt.sv["healedAt"] = time; }
            if (!quiet || h > 20) Emit(new DmgEvent { src = src, tgt = tgt, amt = h, crit = false, heal = true, pos = tgt.Center });
            return h;
        }

        /// <summary>Ultimate charge from damage or healing: a tank's own output charges 40% slower, a tactician banks what
        /// comes past full - a quarter of the next ultimate, at 75% rate.</summary>
        public void GainUlt(Actor a, double amt)
        {
            if (amt <= 0 || !a.alive) return;
            double cost = a.def.ult.charge;
            double g = amt * (1 + a.mods.ultgain) * (a.def.role == "tank" && a.def == a.baseDef ? TANK_ULT_GEN : 1);
            if (IsSub(a, "tactician")) { if (a.ult >= cost) g *= TACTICIAN_RATE; a.ult = Math.Min(cost * (1 + TACTICIAN_BANK), a.ult + g); }
            else a.ult = Math.Min(cost, a.ult + g);
        }

        public void AddShield(Actor tgt, double amt, double dur, string kind, Actor src = null)
        {
            tgt.shields = tgt.shields.Where(s => s.kind != kind).ToList();
            tgt.shields.Add(new Shield { amt = amt, until = time + dur, kind = kind, src = src });
        }

        /// <summary>a destroyed frame: explode it, pop the pilot out on foot (keeps the mech's ult for later)</summary>
        public void Demech(Actor a, Actor src)
        {
            double t = time; var p = D.Pilots[a.def.id]; var f = a.Forward();
            var killer = src != null && src != a ? src : (a.lastHitBy != null && t - a.lastHitAt < 6 ? a.lastHitBy : null);
            Fx("burst", a.Center, new FxOpts { r = 4, color = a.def.glow }); Fx("eject", new V3(a.pos.x, a.pos.y + a.Height * 0.62, a.pos.z), new FxOpts { color = a.def.glow, actor = a });
            Sfx("mechdown", a.Center); Sfx("eject", a.Center);
            Msg($"{a.def.name.ToUpperInvariant()} DOWN - {p.name.ToUpperInvariant()} FIGHTS ON", a.def.color);
            Emit(new DemechEvent { src = killer, tgt = a });
            a.sv["mechUlt"] = a.ult;
            a.Clear("titan"); a.scale = 1;
            a.def = p;
            a.hp = p.hp; a.maxArmor = 0; a.armor = 0; a.shields = new List<Shield>();
            a.barrier.up = false; a.forced = null; a.flying = false; a.charging = false;
            a.ult = 0; a.ammo = p.primary.ammo ?? 0; a.reloadUntil = 0; a.nextShot = t + 0.4;
            a.vel = new V3(-f.x * 4, 9, -f.z * 4); a.grounded = false;
            a.Set("spawnprot", t, 0.8);
        }

        public void Kill(Actor tgt, Actor src)
        {
            if (!tgt.alive) return;
            if (tgt.IsSummon)
            {
                // a puppet falls: no kill feed, no kill or assist, no respawn
                tgt.alive = false; tgt.deathAt = time; tgt.respawnAt = 0; tgt.forced = null; tgt.wounds = new List<Wound>(); tgt.sv["fellAt"] = time;
                attackers.Remove(tgt.id);
                Fx("puppetfall", tgt.Center, new FxOpts { color = tgt.def.glow, actor = tgt });
                return;
            }
            if (tgt.def == tgt.baseDef && D.Pilots.ContainsKey(tgt.def.id)) { Demech(tgt, src); return; }
            tgt.alive = false; tgt.deathAt = time; tgt.deaths++;
            tgt.respawnAt = tgt.noRespawn ? 0 : time + (tgt.isRobot ? 3 : mode == "aitest" ? 4 : mode == "campaign" ? 8 : 6);
            tgt.forced = null; tgt.flying = false; tgt.barrier.up = false; tgt.beamOn = false; tgt.flameOn = false; tgt.wounds = new List<Wound>();
            var killer = src != null && src != tgt ? src : (tgt.lastHitBy != null && time - tgt.lastHitAt < 6 ? tgt.lastHitBy : null);
            if (killer != null)
            {
                killer.kills++; killer.streak++; killer.bestStreak = Math.Max(killer.bestStreak, killer.streak);
                if (killer.Has("judgment", time)) killer.cd["flashstep"] = 0;
                if (killer.def.id == "hayate") killer.cd["currentdash"] = 0;     // Current Dash resets on an elimination
                // the healer keeping the killer alive gets the assist (Overwatch's healing assists)
                var healer = time - killer.Sv("healedAt", -99) < 4 ? actors.FirstOrDefault(x => killer.sv.TryGetValue("healedBy", out var hb) && x.id == hb) : null;
                if (healer != null && healer != killer) healer.stats["healAssists"] = (healer.stats.TryGetValue("healAssists", out var ha) ? ha : 0) + 1;
            }
            tgt.streak = 0;
            if (attackers.TryGetValue(tgt.id, out var m))
                foreach (var kv in m) if (time - kv.Value < 6 && kv.Key != killer?.id) { var asx = actors.FirstOrDefault(x => x.id == kv.Key); if (asx != null) asx.assists++; }
            attackers.Remove(tgt.id);
            zones = zones.Where(z => !(z.owner == tgt && z.kind == "tether")).ToList();
            Emit(new KillEvent { src = killer, tgt = tgt });
            director?.OnKill(tgt, killer);
            Sfx(tgt.def.frame == "mech" ? "mechdown" : tgt.isRobot ? "botdown" : "down", tgt.Center);
            Fx("death", tgt.Center, new FxOpts { color = tgt.def.glow, actor = tgt });
            if (tgt.def.pilot != null)
            {
                // the pilot punches out of the cockpit before the frame collapses
                Fx("eject", new V3(tgt.pos.x, tgt.pos.y + tgt.Height * 0.62, tgt.pos.z), new FxOpts { color = tgt.def.glow, actor = tgt });
                Sfx("eject", tgt.Center);
                Msg($"{tgt.def.pilot.name.ToUpperInvariant()} EJECTS!", tgt.def.color);
            }
        }
    }
}
