// TS-PARITY: src/campaign/Director.ts (+ the Level type of src/campaign/data.ts, createCampaign of src/game/setup.ts)
// OPERATION STARFALL's campaign director: encounter triggers, waves, checkpoints, boss fights (telegraphed attack
// patterns), win / wipe. Runs inside World.Step on the host (solo or co-op); clients only see its results.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public sealed class CampaignBeat { public string img, text; }
    public sealed class CampaignLevel
    {
        public string id, name, boss;
        public MapDef map;
        public List<Encounter> encounters;
        public double[] arena;
        public List<CampaignBeat> intro, outro;

        static Dictionary<string, CampaignLevel> cache;
        /// <summary>the campaign's levels from the exported data (campaign.json)</summary>
        public static Dictionary<string, CampaignLevel> All(GameData d)
        {
            if (cache != null) return cache;
            cache = new Dictionary<string, CampaignLevel>();
            foreach (var j in d.Campaign?.levels ?? new List<JObject>()) { var l = j.ToObject<CampaignLevel>(); cache[l.id] = l; }
            return cache;
        }
    }

    /// <summary>a director cue for the client (a boss intro card, the outro)</summary>
    public sealed class DirectorCue { public string t, id; }

    public sealed class Director : IDirector
    {
        sealed class Tele { public string shape; public double x, z, x2, z2, r, dmg, knock; public string fx, color; }

        public readonly World w;
        public readonly CampaignLevel level;
        public readonly INav nav;
        readonly Func<int> players;
        public string state = "explore";           // explore | fight | boss | victory | wipe
        public int enc = -1, wave = -1;
        public readonly HashSet<int> cleared = new HashSet<int>();
        public List<Actor> enemies = new List<Actor>();
        public Actor boss;
        public bool phase2;
        public string objective = "Advance to the first platform";
        public double nextWaveAt, wipeAt, bossIntroAt = -1;
        public readonly List<DirectorCue> events = new List<DirectorCue>();      // story / cinematic cues for the client
        readonly Dictionary<int, IController> brains = new Dictionary<int, IController>();
        readonly Dictionary<string, HeroDef> bosses = new Dictionary<string, HeroDef>();

        public Director(World w, CampaignLevel level, INav nav, Func<int> players)
        {
            this.w = w; this.level = level; this.nav = nav; this.players = players;
            var d = GameData.Current;
            foreach (var e in d.Enemies ?? new List<HeroDef>()) w.extraDefs[e.id] = e;
            foreach (var b in d.Bosses ?? new List<HeroDef>()) { w.extraDefs[b.id] = b; bosses[b.id] = b; }
        }

        public List<Actor> Heroes => w.actors.Where(a => a.team == "zenith").ToList();
        public int Scale() => Math.Max(1, players());

        static double FloorOr(double g, double fallback) => double.IsFinite(g) ? g : fallback;

        public Actor Spawn(string id, double x, double z)
        {
            var a = w.AddHero(id, "umbra");
            a.noRespawn = true;
            a.spawn = new[] { x, z };
            double g = w.level.GroundAt(x, z, 40);
            a.pos = new V3(x, g > double.NegativeInfinity ? g : 0, z);
            if (a.def.frame == "drone") a.pos = new V3(a.pos.x, a.pos.y + 3, a.pos.z);
            a.yaw = -Math.PI / 2;
            if (bosses.TryGetValue(id, out var b))
            {
                double hp = JsMath.Round(b.hp * (1 + b.hpPerPlayer * (Scale() - 1)));
                a.hp = hp; var def = a.def.Clone(); def.hp = hp; a.def = def;
                a.isBoss = true;
                var br = new BossBrain(this, a, b);
                brains[a.id] = br; a.controller = br;
            }
            else
            {
                if (id == "minion_sentinel") a.barrier = new BarrierState { hp = 600, max = 600, up = false, regenAt = 0, brokenUntil = 0 };
                var br = new MinionBrain(this, a);
                brains[a.id] = br; a.controller = br;
            }
            w.Fx("spawn", a.pos, new FxOpts { color = a.def.glow, actor = a }); w.Sfx("pad", a.pos);
            enemies.Add(a);
            return a;
        }

        void StartWave(int e, int wv)
        {
            var E = level.encounters[e];
            var waves = E.waves[wv];
            int extra = Scale() - 1, i = 0;
            foreach (var pair in waves)
            {
                // (JSON pairs: [id, count] - plain values or JValues depending on how the level was read)
                string id = pair[0] is JValue jid ? (string)jid.Value : pair[0]?.ToString(); int n = Convert.ToInt32(pair[1] is JValue jn ? jn.Value : pair[1]);
                int count = n + (int)Math.Floor(extra * n * 0.5);
                for (int k = 0; k < count; k++, i++)
                {
                    double ang = i * 2.39996 + wv, r = 6 + (i % 3) * 3;
                    Spawn(id, E.at[0] + 8 + Math.Cos(ang) * r, E.at[1] + Math.Sin(ang) * r);
                }
            }
            w.Msg(wv == 0 ? "HOSTILES INBOUND" : $"WAVE {wv + 1}", "#c77dff");
            w.Sfx("announce");
            objective = $"Destroy the Star-Forger's robots (wave {wv + 1}/{E.waves.Count})";
        }

        public void Update(double dt)
        {
            double t = w.time;
            foreach (var z in w.zones)
                if (z.kind == "tele" && z.data != null && !(bool)z.data["done"] && t >= Convert.ToDouble(z.data["fireAt"]))
                { Resolve((Tele)z.data["tele"], z.owner); z.data["done"] = true; z.until = t + 0.25; }
            enemies = enemies.Where(e => e.alive || t - e.deathAt < 4).ToList();
            w.actors = w.actors.Where(a => !(a.noRespawn && !a.alive && t - a.deathAt > 4)).ToList();
            var heroes = Heroes; var alive = heroes.Where(h => h.alive).ToList();
            // squad wipe: everyone down at once -> regroup at the checkpoint
            if (heroes.Count > 0 && alive.Count == 0 && state != "wipe" && state != "victory")
            {
                state = "wipe"; wipeAt = t + 4;
                w.Msg("SQUAD DOWN - REGROUPING", "#ff3b5c");
            }
            if (state == "wipe")
            {
                if (t >= wipeAt)
                {
                    foreach (var h in heroes) { h.respawnAt = 0; w.Respawn(h); }
                    state = boss != null && boss.alive ? "boss" : enemies.Any(e => e.alive) ? "fight" : "explore";
                }
                return;
            }
            if (state == "explore")
            {
                for (int i = 0; i < level.encounters.Count; i++)
                {
                    var E = level.encounters[i];
                    if (cleared.Contains(i) || state != "explore") continue;
                    if (alive.Any(h => M.Hypot(h.pos.x - E.at[0], h.pos.z - E.at[1]) < E.r + 6)) { enc = i; wave = 0; state = "fight"; StartWave(i, 0); }
                }
                double ax = level.arena[0], az = level.arena[1];
                if (state == "explore" && cleared.Count == level.encounters.Count && alive.Any(h => M.Hypot(h.pos.x - ax, h.pos.z - az) < 26)) StartBoss(level.boss);
                if (state == "explore") objective = cleared.Count == level.encounters.Count ? $"Enter the arena - {bosses[level.boss].name} awaits" : "Advance - use the jump pads";
            }
            else if (state == "fight")
            {
                if (!enemies.Any(e => e.alive))
                {
                    if (nextWaveAt == 0) nextWaveAt = t + 2.5;
                    else if (t >= nextWaveAt)
                    {
                        nextWaveAt = 0;
                        var E = level.encounters[enc];
                        if (wave + 1 < E.waves.Count) { wave++; StartWave(enc, wave); }
                        else
                        {
                            cleared.Add(enc); state = "explore";
                            foreach (var h in heroes) h.spawn = new[] { E.at[0], E.at[1] };
                            w.Msg("AREA SECURED - CHECKPOINT", "#5cc8ff"); w.Sfx("capture");
                            foreach (var h in alive) w.Heal(h, h, h.MaxHp);
                        }
                    }
                }
            }
            else if (state == "boss")
            {
                if (boss != null && !boss.alive)
                {
                    if (level.boss == "boss_genesis" && !phase2)
                    {
                        // phase two: the Star-Forger himself steps out of the wreck
                        phase2 = true;
                        events.Add(new DirectorCue { t = "bossintro", id = "qelvaris" });
                        w.Msg("QEL'VARIS EMERGES FROM THE WRECKAGE", "#ffd24a");
                        boss = Spawn("qelvaris", boss.pos.x - 6, boss.pos.z);
                        bossIntroAt = t;
                    }
                    else
                    {
                        state = "victory"; objective = "Victory";
                        foreach (var e in enemies) if (e.alive) w.Kill(e, null);
                        w.End("zenith");
                        events.Add(new DirectorCue { t = "outro" });
                    }
                }
            }
        }

        public void StartBoss(string id)
        {
            double ax = level.arena[0], az = level.arena[1];
            boss = Spawn(id, ax + 18, az);
            state = "boss"; bossIntroAt = w.time;
            objective = $"Destroy {bosses[id].name} - aim for {bosses[id].weak}";
            events.Add(new DirectorCue { t = "bossintro", id = id });
            w.Msg($"{bosses[id].name} - {bosses[id].title}", bosses[id].glow);
            w.Sfx("ultcall"); w.Sfx("mechdown");
            foreach (var h in Heroes) h.spawn = new[] { ax - 26, az };
        }

        public void OnKill(Actor a, Actor src) { brains.Remove(a.id); }

        /// <summary>where the squad should head next (used by AI companions when no human is leading)</summary>
        public V3? Waypoint()
        {
            int next = -1;
            for (int i = 0; i < level.encounters.Count; i++) if (!cleared.Contains(i)) { next = i; break; }
            double x, z;
            if (state == "boss" && boss != null) { x = boss.pos.x - 10; z = boss.pos.z; }
            else if (state == "fight") { x = level.encounters[enc].at[0] + 4; z = level.encounters[enc].at[1]; }
            else if (next >= 0) { x = level.encounters[next].at[0]; z = level.encounters[next].at[1]; }
            else { x = level.arena[0]; z = level.arena[1]; }
            return new V3(x, Math.Max(0, w.level.GroundAt(x, z, 20)), z);
        }

        // ------------------------------------------------------------------------------------------------ telegraphed damage
        void TeleAt(Actor owner, Tele tl, double delay)
        {
            w.zones.Add(new Zone
            {
                id = (int)Math.Floor(Rng.Random() * 1e9), kind = "tele", owner = owner, team = owner.team,
                x = tl.x, y = FloorOr(w.level.GroundAt(tl.x, tl.z, owner.pos.y + 20), owner.pos.y), z = tl.z, r = tl.r,
                born = w.time, until = w.time + delay + 0.3, next = 1e9,
                data = new Dictionary<string, object> { ["tele"] = tl, ["shape"] = tl.shape, ["fireAt"] = w.time + delay, ["done"] = false, ["color"] = tl.color, ["x2"] = tl.x2, ["z2"] = tl.z2 },
            });
        }

        void Resolve(Tele tl, Actor owner)
        {
            foreach (var h in Heroes)
            {
                if (!h.alive) continue;
                bool hit;
                if (tl.shape == "circle") hit = M.Hypot(h.pos.x - tl.x, h.pos.z - tl.z) < tl.r + h.Radius * 0.5;
                else
                {
                    double ax = tl.x, az = tl.z, bx = tl.x2, bz = tl.z2;
                    double vx = bx - ax, vz = bz - az, L2 = vx * vx + vz * vz;
                    double k = Math.Max(0, Math.Min(1, ((h.pos.x - ax) * vx + (h.pos.z - az) * vz) / L2));
                    hit = M.Hypot(h.pos.x - (ax + vx * k), h.pos.z - (az + vz * k)) < tl.r + h.Radius * 0.5;
                }
                if (hit && h.pos.y - w.level.GroundAt(h.pos.x, h.pos.z, h.pos.y + 1) < 2.2)
                {
                    w.Damage(owner, h, tl.dmg, new DmgOpts { kind = "ability" });
                    if (tl.knock != 0 && h.def.frame != "mech" && !h.Has("ccimmune", w.time))
                    {
                        var d = World.Norm(new V3(h.pos.x - tl.x, 0, h.pos.z - tl.z));
                        h.forced = new Forced { vx = d.x * tl.knock, vy = 7, vz = d.z * tl.knock, until = w.time + 0.3, kind = "knock" };
                    }
                }
            }
            var p = new V3(tl.x, FloorOr(w.level.GroundAt(tl.x, tl.z, 40), 0), tl.z);
            w.Fx(tl.fx, p, new FxOpts { r = tl.r, color = tl.color }); w.Sfx(tl.fx == "slam" ? "slam" : "boom", p);
        }

        // ================================================================================================ minions
        sealed class MinionBrain : IController
        {
            readonly Director d; readonly Actor a;
            Actor target; List<V3> path = new List<V3>(); double repath, nextThink;
            public MinionBrain(Director d, Actor a) { this.d = d; this.a = a; }

            public void Think(double dt)
            {
                var w = d.w; double t = w.time; var i = a.input;
                i.fire = false; i.alt = false; i.jump = false; i.jumpHeld = false;
                if (t >= nextThink)
                {
                    nextThink = t + 0.25;
                    var heroes = w.actors.Where(h => h.alive && h.team == "zenith").ToList();
                    target = JsSort.SortBy(heroes.Where(h => World.Dist3(h.pos, a.pos) < 60).ToList(), (p, q) => World.Dist3(p.pos, a.pos) - World.Dist3(q.pos, a.pos)).FirstOrDefault();
                    if (target != null && a.def.frame != "drone" && t >= repath) { repath = t + 1; path = d.nav.Find(a.pos, target.pos) ?? new List<V3>(); }
                }
                var tg = target;
                if (tg == null) { i.mx = i.mz = 0; return; }
                V3 c = tg.Center, e = a.Eye;
                i.yaw = Math.Atan2(c.x - e.x, c.z - e.z);
                i.pitch = Math.Atan2(c.y - e.y, M.Hypot(c.x - e.x, c.z - e.z)) + (Rng.Random() - 0.5) * 0.06;
                double dd = World.Dist3(tg.pos, a.pos);
                double tox = (tg.pos.x - a.pos.x) / (dd == 0 ? 1 : dd), toz = (tg.pos.z - a.pos.z) / (dd == 0 ? 1 : dd);
                string id = a.def.id;
                void Steer(double dx, double dz)
                {
                    double fx = Math.Sin(a.yaw), fz = Math.Cos(a.yaw), rx = -Math.Cos(a.yaw), rz = Math.Sin(a.yaw);
                    i.mz = dx * fx + dz * fz; i.mx = dx * rx + dz * rz;
                }
                void Follow()
                {
                    while (path.Count > 0 && M.Hypot(path[0].x - a.pos.x, path[0].z - a.pos.z) < 0.8) path.RemoveAt(0);
                    var wp = path.Count > 0 ? path[0] : tg.pos; double l = M.Hypot(wp.x - a.pos.x, wp.z - a.pos.z); if (l == 0) l = 1;
                    Steer((wp.x - a.pos.x) / l, (wp.z - a.pos.z) / l);
                }
                bool vis = w.Visible(a, tg);
                if (id == "minion_lancer")
                {
                    if (dd > 2.6) Follow(); else Steer(0, 0);
                    i.fire = dd < 3.6 && vis;
                }
                else if (id == "minion_sentinel")
                {
                    if (dd > 16) Follow(); else Steer(-toz * 0.4, tox * 0.4);
                    double cyc = (t + a.id) % 5;
                    i.alt = false;
                    a.barrier.up = cyc < 2.8 && a.barrier.hp > 50;
                    i.fire = !a.barrier.up && vis && dd < 40;
                }
                else if (id == "minion_swarmer")
                {
                    double ang = t * 0.9 + a.id;
                    double gx = tg.pos.x + Math.Cos(ang) * 8, gz = tg.pos.z + Math.Sin(ang) * 8;
                    double l = M.Hypot(gx - a.pos.x, gz - a.pos.z); if (l == 0) l = 1;
                    Steer((gx - a.pos.x) / l, (gz - a.pos.z) / l);
                    a.sv["hoverY"] = tg.pos.y + 4 + Math.Sin(t * 2 + a.id) * 1.2;
                    i.fire = vis && Math.Sin(t * 1.3 + a.id) > -0.3;
                }
                else if (id == "minion_bomber")
                {
                    Steer(tox, toz);
                    a.sv["hoverY"] = tg.pos.y + 1.2;
                    if (dd < 2.4)
                    {
                        foreach (var h in w.actors.ToList()) if (h.alive && h.team != a.team && World.Dist3(h.pos, a.pos) < 4.5) w.Damage(a, h, 55, new DmgOpts { kind = "splash" });
                        w.Fx("burst", a.Center, new FxOpts { r = 4, color = "#ff2d55" }); w.Sfx("boom", a.Center);
                        w.Kill(a, null);
                    }
                }
            }
        }

        // ================================================================================================ bosses
        sealed class BossBrain : IController
        {
            readonly Director d; readonly Actor a; readonly HeroDef def;
            double nextAttack = 3, busyUntil, nextThink; Actor target;
            (double t0, double dur, double from, double to, double len)? sweep;
            (double until, Actor tg)? beam;
            (double t0, HashSet<int> hit)? shock;
            bool submerged;

            public BossBrain(Director d, Actor a, HeroDef def)
            {
                this.d = d; this.a = a; this.def = def;
                a.sv["hoverY"] = a.pos.y + (def.frame == "drone" ? 7 : 0);
            }

            public void Think(double dt)
            {
                var w = d.w; double t = w.time; var i = a.input;
                i.fire = false; i.a1 = false;
                var heroes = w.actors.Where(h => h.alive && h.team == "zenith").ToList();
                if (t >= nextThink)
                {
                    nextThink = t + 0.5;
                    JsSort.SortBy(heroes, (p, q) => World.Dist3(p.pos, a.pos) - World.Dist3(q.pos, a.pos));
                    int pick = Rng.Random() < 0.3 ? 1 : 0;
                    target = pick < heroes.Count ? heroes[pick] : heroes.Count > 0 ? heroes[0] : null;
                }
                var tg = target;
                if (tg == null) { i.mx = i.mz = 0; return; }
                V3 c = tg.Center, e = a.Eye;
                if (beam == null) i.yaw = Math.Atan2(c.x - e.x, c.z - e.z);
                i.pitch = Math.Atan2(c.y - e.y, M.Hypot(c.x - e.x, c.z - e.z));
                double dd = M.Hypot(tg.pos.x - a.pos.x, tg.pos.z - a.pos.z);
                // movement: keep a fighting distance, drift around the arena
                double want = def.frame == "drone" ? 16 : def.id == "qelvaris" ? 12 : 10;
                double k = t < busyUntil ? 0 : dd > want + 3 ? 1 : dd < want - 3 ? -0.6 : 0;
                i.mz = k; i.mx = Math.Sin(t * 0.3 + a.id) * 0.5;
                // flying colossi are leashed to the arena: backing off / strafing must not carry them out over the void,
                // where the melee heroes can never reach them
                double ax = d.level.arena[0], az = d.level.arena[1], ox = ax - a.pos.x, oz = az - a.pos.z, od = M.Hypot(ox, oz);
                if (def.frame == "drone" && od > 17)
                {
                    double yaw = a.yaw, pull = Math.Min(1, (od - 17) / 5);
                    double fz = (ox * Math.Sin(yaw) + oz * Math.Cos(yaw)) / od, fx = (ox * -Math.Cos(yaw) + oz * Math.Sin(yaw)) / od;
                    i.mz = i.mz * (1 - pull) + fz * pull; i.mx = i.mx * (1 - pull) + fx * pull;
                }
                if (def.frame == "drone")
                {
                    // over the void there is no ground: keep the last hover height (a -Infinity target froze the simulation)
                    double g = w.level.GroundAt(a.pos.x, a.pos.z, 60);
                    if (double.IsFinite(g)) a.sv["hoverY"] = g + (submerged ? -20 : 4.5 + Math.Sin(t * 0.6) * 1.5);
                }
                i.fire = !submerged && t > busyUntil && Rng.Random() < 0.6;
                // ---- continuous attacks
                if (sweep.HasValue)
                {
                    var s = sweep.Value; double k2 = (t - s.t0) / s.dur;
                    if (k2 >= 1) sweep = null;
                    else
                    {
                        double ang = s.from + (s.to - s.from) * k2;
                        var o = new V3(a.pos.x, a.pos.y + 1, a.pos.z); var end = new V3(o.x + Math.Sin(ang) * s.len, o.y, o.z + Math.Cos(ang) * s.len);
                        w.Fx("bossbeam", new V3(o.x, a.pos.y + a.Height * 0.5, o.z), new FxOpts { to = end, color = def.glow });
                        foreach (var h in heroes)
                        {
                            double vx = end.x - o.x, vz = end.z - o.z, L2 = vx * vx + vz * vz;
                            double q = Math.Max(0, Math.Min(1, ((h.pos.x - o.x) * vx + (h.pos.z - o.z) * vz) / L2));
                            double dist = M.Hypot(h.pos.x - (o.x + vx * q), h.pos.z - (o.z + vz * q));
                            if (dist < 1.3 && h.pos.y - a.pos.y < 1.4) w.Damage(a, h, 110 * dt, new DmgOpts { kind = "ability", noLifesteal = true });   // jump it!
                        }
                    }
                }
                if (beam.HasValue)
                {
                    var bm = beam.Value;
                    if (t > bm.until || !bm.tg.alive) beam = null;
                    else
                    {
                        var bt = bm.tg; var o = new V3(a.pos.x, a.pos.y + a.Height * 0.7, a.pos.z);
                        var dv = World.Norm(new V3(bt.Center.x - o.x, bt.Center.y - o.y, bt.Center.z - o.z));
                        double L = World.Dist3(o, bt.Center);
                        var bar = w.BarrierHit(a.team, o, dv, L); var lh = w.level.Ray(o, dv, L);
                        double endT = Math.Min(bar != null ? bar.t : L, lh.HasValue ? lh.Value.t : L);
                        var end = new V3(o.x + dv.x * endT, o.y + dv.y * endT, o.z + dv.z * endT);
                        w.Fx("bossbeam", o, new FxOpts { to = end, color = def.glow });
                        if (bar != null && bar.t <= endT + 0.01) w.HitBarrier(bar.owner, 60 * dt, a, end);
                        else if (!lh.HasValue || lh.Value.t >= L - 0.5) w.Damage(a, bt, 45 * dt, new DmgOpts { kind = "beam", noLifesteal = true });
                    }
                }
                if (shock.HasValue)
                {
                    var sh = shock.Value; double r = (t - sh.t0) * 12;
                    if (r > 32) shock = null;
                    else foreach (var h in heroes)
                            if (!sh.hit.Contains(h.id) && Math.Abs(M.Hypot(h.pos.x - a.pos.x, h.pos.z - a.pos.z) - r) < 1.1 && h.pos.y - w.level.GroundAt(h.pos.x, h.pos.z, h.pos.y + 1) < 1)
                            { sh.hit.Add(h.id); w.Damage(a, h, 45, new DmgOpts { kind = "ability" }); }
                }
                if (t < nextAttack || t < busyUntil) return;
                // ---- choose the next attack (faster when hurt)
                double hurt = 1 - a.Health / a.MaxHp;
                nextAttack = t + 4.2 - hurt * 1.8 + Rng.Random();
                string atk = def.attacks[(int)Math.Floor(Rng.Random() * def.attacks.Length)];
                a.anim.castAt = t; a.anim.castId = atk;
                string col = def.glow;
                switch (atk)
                {
                    case "stomp": d.TeleAt(a, new Tele { shape = "circle", x = tg.pos.x, z = tg.pos.z, r = 7, dmg = 70, knock = 14, fx = "slam", color = col }, 1.3); busyUntil = t + 1.4; w.Sfx("mechjump", a.pos); break;
                    case "bite": { var f = a.Forward(); d.TeleAt(a, new Tele { shape = "circle", x = a.pos.x + f.x * 8, z = a.pos.z + f.z * 8, r = 6, dmg = 80, knock = 10, fx = "slam", color = col }, 0.9); busyUntil = t + 1; break; }
                    case "charge":
                    {
                        var f = World.Norm(new V3(tg.pos.x - a.pos.x, 0, tg.pos.z - a.pos.z));
                        const double L = 30;
                        d.TeleAt(a, new Tele { shape = "line", x = a.pos.x, z = a.pos.z, x2 = a.pos.x + f.x * L, z2 = a.pos.z + f.z * L, r = a.Radius, dmg = 60, knock = 16, fx = "dust", color = col }, 1.2);
                        w.After(1.1, () => { if (a.alive) a.forced = new Forced { vx = f.x * L / 0.8, vy = 0, vz = f.z * L / 0.8, until = w.time + 0.8, kind = "bosscharge", ignoreGravity = true }; });
                        busyUntil = t + 2; w.Sfx("charge", a.pos); break;
                    }
                    case "barrage": case "firerain":
                    {
                        int n = atk == "firerain" ? 14 : 8;
                        for (int q = 0; q < n; q++)
                        {
                            var h = heroes[q % heroes.Count]; double ang = Rng.Random() * 6.28, r = Rng.Random() * 7;
                            d.TeleAt(a, new Tele { shape = "circle", x = h.pos.x + Math.Cos(ang) * r, z = h.pos.z + Math.Sin(ang) * r, r = 3.2, dmg = 40, knock = 6, fx = "burst", color = col }, 1.4 + q * 0.12);
                        }
                        w.Fx("ultflash", a.Center, new FxOpts { color = col, actor = a }); w.Sfx("rocketfist", a.pos); break;
                    }
                    case "sweep":
                    {
                        double b0 = a.yaw;
                        sweep = (t + 0.8, 1.8, b0 - 1.2, b0 + 1.2, 34);
                        w.Msg("JUMP THE BEAM!", col); w.Sfx("lance", a.pos); busyUntil = t + 2.8; break;
                    }
                    case "beam": beam = (t + 2.5, tg); w.Sfx("charge", a.pos); busyUntil = t + 2.6; break;
                    case "shockwave": shock = (t + 0.6, new HashSet<int>()); w.Fx("sunburst", a.pos, new FxOpts { r = 30, color = col }); w.Sfx("slam", a.pos); break;
                    case "crescents": case "halo": case "orbs":
                    {
                        int n = atk == "halo" ? 12 : atk == "orbs" ? 5 : 3;
                        for (int q = 0; q < n; q++)
                        {
                            double ang = atk == "halo" ? (double)q / n * Math.PI * 2 : a.yaw + (q - (n - 1) / 2.0) * 0.25;
                            var from = new V3(a.pos.x, a.pos.y + Math.Min(a.Height * 0.5, 4), a.pos.z);
                            var dir = atk == "orbs" ? World.Norm(new V3(Math.Sin(ang), 0.4, Math.Cos(ang)))
                                : World.Norm(new V3(Math.Sin(ang), atk == "crescents" ? (c.y - from.y) / Math.Max(1, dd) : 0, Math.Cos(ang)));
                            w.SpawnProj(a, from, dir, atk == "orbs" ? 16 : 26, new ProjOpts { dmg = atk == "orbs" ? 22 : 35, fx = "hex", life = 3, r = atk == "crescents" ? 1.1 : 0.5, homing = atk == "orbs" ? 2.5 : 0 });
                        }
                        w.Sfx("silence", a.pos); break;
                    }
                    case "summon":
                    {
                        int n = 2 + d.Scale();
                        for (int q = 0; q < n; q++) d.Spawn(def.summon, a.pos.x + Math.Cos(q * 2) * 6, a.pos.z + Math.Sin(q * 2) * 6);
                        w.Msg("REINFORCEMENTS", "#c77dff"); break;
                    }
                    case "blink": case "warp":
                    {
                        var h = heroes[(int)Math.Floor(Rng.Random() * heroes.Count)];
                        double ang = Rng.Random() * 6.28, r = def.id == "qelvaris" ? 8 : 14;
                        double px = h.pos.x + Math.Cos(ang) * r, pz = h.pos.z + Math.Sin(ang) * r;
                        if (w.level.GroundAt(px, pz, a.pos.y + 5) > double.NegativeInfinity)
                        { w.Fx("smoke", a.pos, new FxOpts { color = col }); a.pos = new V3(px, a.pos.y, pz); w.Fx("smoke", a.pos, new FxOpts { color = col }); w.Sfx("shadowstep", a.pos); }
                        break;
                    }
                    case "dive":
                    {
                        submerged = true; a.Set("phased", t, 2.6); w.Fx("burst", a.pos, new FxOpts { r = 6, color = col });
                        double px = tg.pos.x, pz = tg.pos.z;
                        d.TeleAt(a, new Tele { shape = "circle", x = px, z = pz, r = 6, dmg = 85, knock = 16, fx = "slam", color = col }, 2);
                        w.After(2, () => { submerged = false; a.pos = new V3(px, FloorOr(w.level.GroundAt(px, pz, 40), a.pos.y), pz); });
                        busyUntil = t + 2.5; w.Sfx("singularity", a.pos); break;
                    }
                    case "divebomb":
                    {
                        double px = tg.pos.x, pz = tg.pos.z;
                        d.TeleAt(a, new Tele { shape = "circle", x = px, z = pz, r = 7, dmg = 75, knock = 14, fx = "slam", color = col }, 1.5);
                        w.After(1.1, () => { if (!a.alive) return; double dx = px - a.pos.x, dz = pz - a.pos.z; a.forced = new Forced { vx = dx / 0.4, vy = -8, vz = dz / 0.4, until = w.time + 0.4, kind = "bosscharge", ignoreGravity = true }; });
                        busyUntil = t + 2; break;
                    }
                    case "gravity":
                    {
                        var p = tg.pos;
                        w.zones.Add(new Zone { id = (int)Math.Floor(Rng.Random() * 1e9), kind = "singularity", owner = a, team = a.team, x = p.x, y = p.y, z = p.z, r = 8, born = t, until = t + 2.5, next = t, data = null });
                        w.Fx("singularity", p, new FxOpts { r = 8, color = col, dur = 2.5 }); w.Sfx("singularity", p); break;
                    }
                }
            }
        }

        // ================================================================================================ setup
        /// <summary>TS createCampaign: the level, a squad (humans first, then AI companions up to three), the director</summary>
        public static Match CreateCampaign(string levelId, List<(string hero, string netId)> squad, double skill = 0.7, ILevel collision = null, INav navOverride = null)
        {
            var lvl = CampaignLevel.All(GameData.Current)[levelId];
            var world = new World(lvl.map, "campaign", true, null, collision);
            var nav = navOverride ?? new BoxNav((BoxLevel)world.level);
            world.nav = nav;
            var m = new Match { world = world, nav = nav };
            int humans = squad.Count;
            var director = new Director(world, lvl, nav, () => { int n = world.actors.Count(a => a.team == "zenith" && (a.isPlayer || !string.IsNullOrEmpty(a.netId))); return n > 0 ? n : 1; });
            world.director = director;
            foreach (var s in squad)
            {
                var a = world.AddHero(s.hero, "zenith");
                a.netId = s.netId;
                if (s.netId == "local") { a.isPlayer = true; m.player = a; a.netId = ""; }
            }
            var used = new HashSet<string>(squad.Select(s => s.hero));
            foreach (var h in GameData.Current.Campaign?.heroes ?? new string[0])
            {
                if (world.actors.Count(a => a.team == "zenith") >= Math.Max(3, humans)) break;
                if (used.Contains(h)) continue;
                var a = world.AddHero(h, "zenith");
                var b = new Bot(world, a, nav, skill); a.controller = b; m.bots.Add(b);
            }
            return m;
        }
    }
}
