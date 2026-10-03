// A match's sound, frame by frame (TS client/Soundscape.ts, ported, with the bits of Game.ts that feed it): what the
// listener's surroundings sound like (occlusion, indoor room, wall reflections), who the biggest threats are
// (Overwatch's importance buckets decide how loud each enemy is), every looping sound (beams, flames, grinding skates,
// wind, Hibiki's tracks, the map's bed), physics sounds (footsteps by surface and weight, impacts by material, shell
// casings, a ragdoll hitting the floor), the stimuli that make heroes talk (VoiceLines), and the announcer's calls from
// the objective's state.
using System;
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Audio
{
    public class MatchAudio : MonoBehaviour
    {
        MatchRunner r;
        sealed class Prev { public double jump, land, reload; public bool alive, low, swoop, grind, burning, flying; public double spin; }
        readonly Dictionary<int, Prev> prev = new Dictionary<int, Prev>();
        readonly Dictionary<int, float> hitMe = new Dictionary<int, float>();      // enemy id -> when they last hurt the player
        // (the TS keeps this on a.sv.wasLowAt; nothing in the simulation reads it, so it stays on the audio side here)
        readonly Dictionary<int, double> wasLowAt = new Dictionary<int, double>();
        readonly Dictionary<string, object> obj = new Dictionary<string, object>();
        readonly Dictionary<int, bool> aliveSeen = new Dictionary<int, bool>();
        int objMine;
        float tSlow, lastHitSnd = -1;
        bool ultWasReady;
        readonly List<(float at, Action fn)> later = new List<(float, Action)>();
        /// <summary>footsteps from distance walked (one a stride) until the animator raises its own foot plants through Step</summary>
        public static bool StrideSteps = true;
        readonly Dictionary<int, (Vector3 pos, float stride, bool has)> strides = new Dictionary<int, (Vector3, float, bool)>();

        public static MatchAudio Attach(MatchRunner runner)
        {
            var a = runner.gameObject.AddComponent<MatchAudio>();
            a.r = runner;
            return a;
        }

        /// <summary>TS setTimeout: run fn after `secs` of real time</summary>
        void After(float secs, Action fn) => later.Add((Time.unscaledTime + secs, fn));

        void Start()
        {
            EventSink.OnEvent += OnEvent;
            Fx.CharacterExtras.BodyFall += BodyFall;
            var w = r.World; var me = r.Player;
            prev.Clear(); hitMe.Clear(); obj.Clear();
            VoiceLines.Reset(); VoiceLines.Me = me;
            Space.SetSpace(w.map.id);
            var L = w.level;
            // occlusion: a wall between the listener and the sound muffles it; a floor between (upstairs / downstairs) more so
            AudioKit.Occlusion = p =>
            {
                var l = AudioKit.Listener;
                if (L.LineOfSight(Conv.S(l), Conv.S(p + Vector3.up * 0.8f))) return 0;
                return Mathf.Abs(p.y - l.y) > 3 ? 0.9f : 0.65f;
            };
            if (me != null) After(0.9f, () => VoiceLines.Say(me, "select", "chatter"));
            After(0.4f, () => VoiceLines.Announce("match_start"));
        }

        void OnDestroy()
        {
            EventSink.OnEvent -= OnEvent;
            Fx.CharacterExtras.BodyFall -= BodyFall;
            AudioKit.StopAllLoops(); VoiceLines.Reset();
            AudioKit.Occlusion = null; AudioKit.Threat.Clear();
        }

        Rel RelOf(Actor a)
        {
            var me = r.Player;
            if (a == null || me == null) return Rel.None;
            return a == me ? Rel.Self : a.team == me.team ? Rel.Ally : Rel.Enemy;
        }
        static Vector3 U(V3 v) => Conv.U(v);

        /// <summary>footsteps: the surface under the foot, the hero's weight, enemy steps louder than friendly ones</summary>
        public void Step(Actor a, bool heavy)
        {
            var w = r.World; if (w == null) return;
            var rel = RelOf(a);
            string id;
            if (a.def.frame == "mech") id = "mechstep";
            else if (a.def.id == "gantetsu") id = "step_heavy";
            else if (a.def.id == "hibiki") { if (UnityEngine.Random.value < 0.5f) return; id = "skate"; }
            else
            {
                string m = w.level.MatAt(a.pos.x, a.pos.z, a.pos.y + 0.2);
                id = m == "wood" ? "step_wood" : m == "trim" || m == "glass" || m == "window" || (w.map.id == "hangar" && m != "accent") ? "step_metal" : "step_stone";
            }
            AudioKit.Play(id, rel == Rel.Self ? (Vector3?)null : U(a.pos), rel == Rel.Self ? 0.45f : 1, PlayOpts.Of(a, rel));
        }

        /// <summary>the impact sound for a world material (level box materials: wall, trim, window, wood, roof, accent,
        /// ground, rock, paint, glass); the hangar is a metal building, as its footsteps are</summary>
        static string ImpactId(string mat, string map)
        {
            string id, fallback;
            switch (mat)
            {
                case "wood": return "impact_wood";
                case "window": case "glass": id = "impact_glass"; fallback = "impact_metal"; break;
                case "trim": case "accent": return "impact_metal";
                case "roof": id = "impact_tile"; fallback = "impact_stone"; break;
                case "ground": id = "impact_dirt"; fallback = "impact_stone"; break;
                default: return map == "hangar" ? "impact_metal" : "impact_stone";     // wall, rock, paint
            }
            if (map == "hangar" && mat == "roof") return "impact_metal";
            return AudioKit.Has(id) ? id : fallback;
        }

        /// <summary>a ragdoll hitting the floor (TS Game: view.onBodyFall)</summary>
        void BodyFall(Actor a, Vector3 at, float speed) => AudioKit.Play("bodyfall", at, Mathf.Min(1, 0.35f + speed / 10));

        /// <summary>World events that sound: routed with who made them; pain, deaths, kills and casts become voice lines</summary>
        void OnEvent(MatchRunner runner, SimEvent e)
        {
            if (runner != r || r.World == null) return;
            var me = r.Player;
            switch (e)
            {
                case SfxEvent s:
                {
                    // hit / crit ticks are the shooter's feedback (played from the damage below), not a sound at the target
                    if (s.id == "hit" || s.id == "crit") return;
                    var rel = RelOf(s.actor);
                    Vector3? pos = s.pos.HasValue ? U(s.pos.Value) : (Vector3?)null;
                    AudioKit.Play(s.id, rel == Rel.Self ? null : pos, (float)(s.vol ?? 1) * (rel == Rel.Self ? 0.85f : 1), PlayOpts.Of(s.actor, rel));
                    // rotary cannons spit brass: a tinkle at his feet now and then
                    if ((s.id == "chaingun" || s.id == "chaingun2") && s.actor != null && UnityEngine.Random.value < 0.35f)
                    {
                        var p = s.actor.pos; var r2 = rel; var src = s.actor;
                        After(0.22f + UnityEngine.Random.value * 0.26f, () => AudioKit.Play("casing", U(new V3(p.x + (UnityEngine.Random.value - 0.5), p.y, p.z + (UnityEngine.Random.value - 0.5))), 0.55f, PlayOpts.Of(src, r2 == Rel.Self ? Rel.Ally : r2)));
                    }
                    return;
                }
                case FxEvent f when f.kind == "impact" && f.actor == null:
                {
                    // bullets and blades hitting the world sound like what they hit (the building clash): glass panes,
                    // roof tiles and bare ground have their own sounds now, each falling back to the old three until the
                    // bank has it
                    if (UnityEngine.Random.value < 0.6f) AudioKit.Play(ImpactId(f.mat, r.World.map.id), U(f.pos), 0.8f);
                    return;
                }
                case DmgEvent d when !d.heal:
                {
                    if (me != null && d.src == me && d.tgt != me)
                    {
                        float t = Time.unscaledTime;
                        if (t - lastHitSnd > 0.045f) { lastHitSnd = t; AudioKit.Play(d.crit ? "crit" : "hit", null, (d.crit ? 1 : 0.9f) * AudioKit.HitmarkerVol); }
                    }
                    if (me != null && d.tgt == me)
                    {
                        if (d.src != null) hitMe[d.src.id] = Time.unscaledTime;
                        if (d.amt >= 20) AudioKit.Play("impact_body", null, Mathf.Min(1, (float)d.amt / 90) * 0.7f);
                    }
                    if (d.tgt != null && d.tgt.alive && d.amt >= 8)
                    {
                        bool big = d.amt >= 60 || d.tgt.Health / d.tgt.MaxHp < 0.25;
                        VoiceLines.Say(d.tgt, big ? "pain_big" : "pain", "pain", d.src);
                    }
                    return;
                }
                case KillEvent k:
                {
                    if (me != null && k.src == me) AudioKit.Play("kill", null);             // (TS Game: the kill confirm)
                    if (k.tgt != null) VoiceLines.Say(k.tgt, "death", "death");
                    var s = k.src;
                    if (s != null && s.alive && s != k.tgt && k.tgt != null)
                    {
                        bool rival = s.def.rival == k.tgt.def.id || s.baseDef.rival == k.tgt.baseDef.id; var tgt = k.tgt;
                        After(0.45f, () => { if (!VoiceLines.Say(s, rival ? "kill_rival" : "kill", "chatter", tgt) && rival) VoiceLines.Say(s, "kill", "chatter", tgt); });
                    }
                    return;
                }
                case DemechEvent dm: if (dm.tgt != null) VoiceLines.Say(dm.tgt, "eject", "death"); return;
                case CastEvent c when c.actor != null:
                {
                    var a = c.actor; var d = a.def;
                    if (d.ult != null && c.id == d.ult.id)
                    {
                        bool pilot = d.ult.id == "callmech";
                        VoiceLines.Say(a, pilot ? "ult_pilot" : "ult", "critical", null, pilot ? "ult_pilot_ally" : "ult_ally");
                        return;
                    }
                    string key = c.id == d.ability1?.id ? "a1" : c.id == d.ability2?.id ? "a2" : d.secondary != null && d.secondary.IsAbility && c.id == d.secondary.id ? "alt" : "";
                    if (c.id == "crossmix") key = a.Sv("track", 0) != 0 ? "speed_track" : "heal_track";
                    if (key == "") return;
                    // a grab or a charge is a warning the enemy should hear
                    bool warn = (d.id == "enra" && key == "a1") || (d.id == "gantetsu" && key == "a1");
                    VoiceLines.Say(a, key, warn ? "critical" : "chatter", null, key);
                    return;
                }
                case CounterEvent _: AudioKit.Play("counter", null); return;          // (TS Game)
            }
        }

        void Update()
        {
            var w = r.World;
            if (w == null) return;
            for (int i = later.Count - 1; i >= 0; i--) if (Time.unscaledTime >= later[i].at) { var fn = later[i].fn; later.RemoveAt(i); fn(); }
            var me = r.Player; VoiceLines.Me = me;
            var cam = Camera.main;
            if (cam != null) { AudioKit.Listener = cam.transform.position; AudioKit.ListenerForward = cam.transform.forward; }
            Frame(w, me, Time.unscaledDeltaTime);
            UltReady(me);
            if (StrideSteps) Strides(w);
        }

        /// <summary>once per rendered frame (TS Soundscape.frame)</summary>
        void Frame(World w, Actor me, float dt)
        {
            double t = w.time; var L = w.level;
            AudioKit.BeginFrame();
            // ---- the listener's space (4 Hz)
            tSlow -= dt;
            if (tSlow <= 0)
            {
                tSlow = 0.25f;
                var l = Conv.S(AudioKit.Listener);
                Space.SetIndoor(L.CeilingAt(l.x, l.z, l.y) < l.y + 14 ? 1 : 0);
                var f = Conv.S(AudioKit.ListenerForward); f.y = 0; double fl = Math.Sqrt(f.x * f.x + f.z * f.z); if (fl > 1e-6) { f.x /= fl; f.z /= fl; } else { f.x = 0; f.z = 1; }
                var dirs = new[] { (f.x, f.z), (-f.z, f.x), (-f.x, -f.z), (f.z, -f.x) };
                var d = new float[4];
                for (int i = 0; i < 4; i++) { var hit = L.Ray(l, new V3(dirs[i].Item1, 0, dirs[i].Item2), 60); d[i] = hit.HasValue ? (float)hit.Value.t : float.PositiveInfinity; }
                Space.SetReflections(d);
                Threat(w, me);
            }
            // ---- loops
            AudioKit.Loop("amb", "amb_" + w.map.id, null, 0.85f);
            AmbientEmitter.Drive(AudioKit.Listener);
            foreach (var a in w.actors)
            {
                if (!a.alive || a.isRobot) continue;
                var rel = RelOf(a); var o = PlayOpts.Of(a, rel);
                Vector3? at = rel == Rel.Self ? (Vector3?)null : U(a.Center);
                if (a.flameOn) AudioKit.Loop($"fl{a.id}", "flame", at, 0.9f, o);
                if (a.beamOn && a.def.secondary != null && !a.def.secondary.IsAbility) AudioKit.Loop($"bm{a.id}", a.def.secondary.sfx == "healbeam2" ? "healbeam2" : "healbeam", at, 0.7f, o);
                if (a.Has("burning", t)) AudioKit.Loop($"bu{a.id}", "burn", at, 0.6f, o);
                float hs = (float)Math.Sqrt(a.vel.x * a.vel.x + a.vel.z * a.vel.z);
                if (a.def.id == "hibiki")
                {
                    if (a.Has("grinding", t)) AudioKit.Loop($"gr{a.id}", "grind", at, 0.8f, o);
                    else if (a.grounded && hs > 1.5f) { var o2 = o; o2.rate = 0.85f + hs / 30; AudioKit.Loop($"sk{a.id}", "skate_roll", at, Mathf.Min(1, hs / 8) * 0.6f, o2); }
                    // his track plays out of the speaker rig: you hear the groove when you're near him
                    bool amp = a.Has("amp", t);
                    AudioKit.Loop($"mx{a.id}", a.Sv("track", 0) != 0 ? "groove_speed" : "groove_heal", rel == Rel.Self ? (Vector3?)null : U(a.Center), amp ? 0.55f : 0.3f, o);
                }
                float sp = Mathf.Sqrt(hs * hs + (float)(a.vel.y * a.vel.y));
                if (rel == Rel.Self && (a.flying || a.Has("swoop", t) || !a.grounded) && sp > 9) AudioKit.Loop("wind", "wind", null, Mathf.Min(1, (sp - 9) / 12) * 0.7f, o);
                Stimuli(w, a, rel);
            }
            AudioKit.EndFrame();
            // the player's own 'respawn' line as they come back to life - checked over every hero, the dead included (the
            // stimuli above only see the living, so they never saw the change; fixed in both games)
            foreach (var a in w.actors)
            {
                bool was = aliveSeen.TryGetValue(a.id, out var al) ? al : a.alive;
                if (a.alive && !was && RelOf(a) == Rel.Self) VoiceLines.Say(a, "respawn", "chatter");
                aliveSeen[a.id] = a.alive;
            }
            Objective(w, me);
        }

        /// <summary>Overwatch's importance buckets: 1 HIGH, 2 NORMAL, 4-10 LOW, the rest culled; teammates sit in LOW</summary>
        void Threat(World w, Actor me)
        {
            AudioKit.Threat.Clear();
            if (me == null) return;
            double t = w.time; float now = Time.unscaledTime; var e = me.Eye; var md = me.AimDir();
            var scored = new List<(Actor x, double s)>();
            foreach (var x in w.actors)
            {
                if (!x.alive || x == me) continue;
                if (x.team == me.team) { AudioKit.Threat[x.id] = 0.7f; continue; }
                var xe = x.Eye; double vx = e.x - xe.x, vy = e.y - xe.y, vz = e.z - xe.z, d = Math.Sqrt(vx * vx + vy * vy + vz * vz); if (d == 0) d = 1;
                var xd = x.AimDir();
                double s = 0;
                if ((vx * xd.x + vy * xd.y + vz * xd.z) / d > 0.975) s += 40;                       // looking at me
                if (now - (hitMe.TryGetValue(x.id, out var hm) ? hm : -99) < 2) s += 30;              // hurting me
                if (d < 10) s += 20; else if (d < 20) s += 10;                                        // close
                if (t - x.anim.attackAt < 0.5) s += 25;                                               // shooting
                if (t - x.anim.castAt < 3 && x.anim.castId == x.def.ult?.id) s += 35;                 // a dangerous ability
                if ((-vx * md.x - vy * md.y - vz * md.z) / d > 0.985) s += 15;                        // I'm looking at them
                scored.Add((x, s));
            }
            // (a stable sort, as JS's)
            var order = new List<int>(); for (int i = 0; i < scored.Count; i++) order.Add(i);
            order.Sort((a, b) => { int c = scored[b].s.CompareTo(scored[a].s); return c != 0 ? c : a.CompareTo(b); });
            for (int i = 0; i < order.Count; i++) AudioKit.Threat[scored[order[i]].x.id] = i == 0 ? 1.25f : i < 3 ? 1 : i < 10 ? 0.7f : 0.35f;
        }

        /// <summary>edges in a hero's state that make them speak (efforts, calls for help, thanks) or make a physical sound</summary>
        void Stimuli(World w, Actor a, Rel rel)
        {
            double t = w.time;
            var cur = new Prev
            {
                jump = a.anim.jumpAt, land = a.anim.landAt, alive = a.alive, low = a.Health / a.MaxHp < 0.35, swoop = a.Has("swoop", t), grind = a.Has("grinding", t),
                burning = a.Has("burning", t), reload = a.reloadUntil, spin = Math.Max(a.Sv("spin1", 0), a.Sv("spin2", 0)), flying = a.flying,
            };
            if (!prev.TryGetValue(a.id, out var p)) { prev[a.id] = cur; return; }
            Vector3? at = rel == Rel.Self ? (Vector3?)null : U(a.Center); var o = PlayOpts.Of(a, rel);
            if (cur.jump != p.jump && t - cur.jump < 0.2) VoiceLines.Say(a, "jump", "exert");
            if (cur.land != p.land && t - cur.land < 0.2) VoiceLines.Say(a, "land", "exert");
            if (cur.low && !p.low) VoiceLines.Say(a, "low_hp", "chatter");
            if (cur.swoop && !p.swoop) { VoiceLines.Say(a, "swoop", "chatter"); AudioKit.Play("wings", at, 0.8f, o); }
            if (cur.flying && !p.flying && a.def.frame == "flyer") AudioKit.Play("wings", at, 0.6f, o);
            if (cur.grind && !p.grind) VoiceLines.Say(a, "grind", "chatter");
            if (cur.burning && !p.burning) VoiceLines.Say(a, "burn", "pain", a.src.TryGetValue("burning", out var bsrc) ? bsrc : null);
            if (rel == Rel.Self && cur.reload > t && p.reload <= t && a.ammo <= 0) VoiceLines.Say(a, "reload", "chatter");
            if (p.spin > 0.5 && cur.spin <= 0.5 && a.def.dualGuns) AudioKit.Play("spindown", at, 0.7f, o);
            // a solid heal from someone else when he needed it
            double healedAt = a.Sv("healedAt", 0), lowAt = wasLowAt.TryGetValue(a.id, out var wl) ? wl : -99;
            if (healedAt != 0 && t - healedAt < 0.1 && a.Health / a.MaxHp > 0.8 && lowAt > t - 6) { wasLowAt[a.id] = -99; VoiceLines.Say(a, "thanks", "chatter"); }
            if (cur.low) wasLowAt[a.id] = t;
            prev[a.id] = cur;
        }

        /// <summary>the hero's ult-ready line (TS Game: hud.onUltReady - once per charge; the sting itself is MatchUi's, from
        /// the HUD's own OnUltReady)</summary>
        void UltReady(Actor me)
        {
            bool ready = me != null && me.def.ult != null && me.def.ult.charge > 0 && me.ult / me.def.ult.charge >= 1;
            if (ready && !ultWasReady) VoiceLines.Say(me, "ult_ready", "chatter");
            ultWasReady = ready;
        }

        /// <summary>the announcer and the player's objective calls, from the objective's state changes</summary>
        void Objective(World w, Actor me)
        {
            string my = me?.team ?? "zenith"; double t = w.time;
            void Once(string k, object v, Action fn) { if (obj.TryGetValue(k, out var old) && !Equals(old, v)) fn(); obj[k] = v; }
            if (w.winner != null) { Once("win", w.winner, () => VoiceLines.Announce(w.winner == my ? "victory" : "defeat")); return; }
            if (w.rules == "control")
            {
                var P = w.point; var C = w.control;
                Once("open", t >= P.unlockAt && C.phase == "fight", () => { if (t >= P.unlockAt && C.phase == "fight") { VoiceLines.Announce("point_open"); if (me != null) After(1.6f, () => VoiceLines.Say(me, "attack_point", "chatter")); } });
                Once("cap", P.capTeam, () => { if (P.capTeam != null) VoiceLines.Announce(P.capTeam == my ? "we_capture" : "they_capture"); });
                Once("owner", P.owner, () => { if (P.owner != null) VoiceLines.Announce(P.owner == my ? "point_taken" : "point_lost"); if (P.owner != null && P.owner != my && me != null) After(1.5f, () => VoiceLines.Say(me, "contest", "chatter")); });
                Once("ot", C.overtime, () => { if (C.overtime) VoiceLines.Announce("overtime"); });
                Once("wins", $"{C.wins["zenith"]}-{C.wins["umbra"]}", () => { int mine = C.wins[my]; VoiceLines.Announce(mine > objMine ? "round_won" : "round_lost"); objMine = mine; });
                Once("round", C.round, () => VoiceLines.Announce(C.round >= 3 ? "round_3" : "round_2"));
            }
            else if (w.rules == "push")
            {
                var M = w.push;
                Once("open", t >= M.unlockAt, () => { if (t >= M.unlockAt) { VoiceLines.Announce("float_unlock"); if (me != null) After(2.6f, () => VoiceLines.Say(me, "push", "chatter")); } });
                Once("owner", M.owner, () => { if (M.owner != null) VoiceLines.Announce(M.owner == my ? "float_moving" : "float_enemy"); });
                Once("con", M.contested, () => { if (M.contested) VoiceLines.Announce("float_contested"); });
            }
            double left = w.timeLimit - t;
            Once("thirty", left < 30, () => { if (left < 30 && left > 0) VoiceLines.Announce("thirty"); });
        }

        /// <summary>footsteps until the animator's foot plants call Step: one a stride (~0.75 x height) on the ground</summary>
        void Strides(World w)
        {
            foreach (var a in w.actors)
            {
                if (!a.alive || a.isRobot) { strides.Remove(a.id); continue; }
                var pos = U(a.pos);
                strides.TryGetValue(a.id, out var s);
                if (s.has && a.grounded)
                {
                    float moved = new Vector2(pos.x - s.pos.x, pos.z - s.pos.z).magnitude;
                    if (moved < 3) s.stride += moved;
                    float stride = Mathf.Max(0.9f, (float)a.Height * 0.75f);
                    if (s.stride >= stride) { s.stride = 0; Step(a, a.def.frame == "mech"); }
                }
                s.pos = pos; s.has = true;
                strides[a.id] = s;
            }
        }
    }
}
