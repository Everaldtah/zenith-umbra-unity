// A match's sound, frame by frame (the TS desktop edition's client/Soundscape.ts, the parts that matter most): the map's
// ambient bed, the announcer, every simulation sound event placed in the world and mixed by who made it, footsteps by
// the surface underfoot and the hero's weight, jumps and landings, hit / crit / kill feedback, and the stimuli that make
// heroes talk (ability and ult casts, kills, deaths, pain, low health, respawns). Occlusion: a wall between the listener
// and a sound muffles it; a floor between them more so.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Audio
{
    public class MatchAudio : MonoBehaviour
    {
        MatchRunner r;
        AudioKit.LoopHandle bed;
        sealed class Prev { public double jump = -9, land = -9; public bool alive = true; public bool low; public float stride; public Vector3 pos; public bool has; }
        readonly Dictionary<int, Prev> prev = new Dictionary<int, Prev>();
        float talkUntil;           // one hero line at a time from the player's own team chatter

        public static MatchAudio Attach(MatchRunner runner)
        {
            var a = runner.gameObject.AddComponent<MatchAudio>();
            a.r = runner;
            return a;
        }

        void Start()
        {
            EventSink.OnEvent += OnEvent;
            var w = r.World;
            AudioKit.Occlusion = p =>
            {
                var l = AudioKit.Listener;
                if (w.level.LineOfSight(Conv.S(l), Conv.S(p + Vector3.up * 0.8f))) return 0;
                return Mathf.Abs(p.y - l.y) > 3 ? 0.9f : 0.65f;
            };
            bed = AudioKit.Loop("amb_" + w.map.id, null, 1);
            AudioKit.Announce("match_start");
            if (r.Player != null) Invoke(nameof(Select), 0.9f);
        }
        void Select() { if (r.Player != null) AudioKit.Line(r.Player.def.id, "select", null, Rel.Self); }

        void OnDestroy()
        {
            EventSink.OnEvent -= OnEvent;
            AudioKit.Stop(bed);
            AudioKit.Occlusion = null;
        }

        Rel RelOf(Actor a)
        {
            var me = r.Player;
            if (a == null || me == null) return Rel.None;
            return a == me ? Rel.Self : a.team == me.team ? Rel.Ally : Rel.Enemy;
        }

        void OnEvent(MatchRunner runner, SimEvent e)
        {
            if (runner != r) return;
            var me = r.Player;
            switch (e)
            {
                case SfxEvent s:
                {
                    var rel = RelOf(s.actor);
                    Vector3? pos = s.pos.HasValue ? Conv.U(s.pos.Value) : (Vector3?)null;
                    AudioKit.Play(s.id, rel == Rel.Self ? null : pos, (float)(s.vol ?? 1) * (rel == Rel.Self ? 0.8f : 1), rel);
                    break;
                }
                case DmgEvent d when me != null && d.src == me && !d.heal && d.tgt != me:
                    AudioKit.Play(d.crit ? "crit" : "hit", null, 0.8f, Rel.Self);
                    break;
                case DmgEvent d when d.tgt != null && !d.heal && d.amt >= 40 && d.tgt.alive:
                    Say(d.tgt, d.amt >= 90 ? "pain_big" : "pain", 0.35f);
                    break;
                case KillEvent k:
                    if (me != null && k.src == me) AudioKit.Play("kill", null, 1, Rel.Self);
                    if (k.tgt != null) Say(k.tgt, "death", 0.9f);
                    if (k.src != null && k.src != k.tgt) Say(k.src, k.tgt != null && k.src.def.rival == k.tgt.def.id ? "kill_rival" : "kill", 0.4f);
                    break;
                case CastEvent c when c.actor != null:
                {
                    var def = c.actor.def;
                    string key = c.id == def.ult?.id ? "ult" : c.id == def.ability1?.id ? "a1" : c.id == def.ability2?.id ? "a2" : null;
                    if (key != null) Say(c.actor, key, key == "ult" ? 1f : 0.5f, ult: key == "ult");
                    break;
                }
                case CounterEvent _: AudioKit.Play("counter", null, 1, Rel.Self); break;
            }
        }

        /// <summary>a hero speaks (with probability p): the player's own lines in your head, everyone else in the world;
        /// enemy ults always heard (you have to react to them)</summary>
        void Say(Actor a, string key, float p, bool ult = false)
        {
            if (Random.value > p) return;
            var rel = RelOf(a);
            if (!ult && rel != Rel.Self && Time.time < talkUntil) return;
            float len = AudioKit.Line(a.def.id, key, Conv.U(a.pos) + Vector3.up * (float)a.Height * 0.9f, rel, ult && rel == Rel.Enemy ? 1.2f : 1f);
            if (len > 0 && !ult) talkUntil = Time.time + len * 0.8f;
        }

        void Update()
        {
            var w = r.World;
            if (w == null) return;
            var cam = Camera.main;
            if (cam != null) AudioKit.Listener = cam.transform.position;
            foreach (var a in w.actors)
            {
                if (!prev.TryGetValue(a.id, out var p)) prev[a.id] = p = new Prev { alive = a.alive };
                var pos = Conv.U(a.pos);
                var rel = RelOf(a);
                bool mech = a.def.frame == "mech", heavy = mech || a.def.id == "gantetsu" || a.def.id == "gorgoth";
                // respawn / death
                if (a.alive && !p.alive && rel != Rel.Enemy) Say(a, "respawn", 0.6f);
                p.alive = a.alive;
                if (!a.alive) { p.has = false; continue; }
                // jumps and landings from the animation cues
                var c = a.anim;
                if (c.jumpAt > p.jump) { if (w.time - c.jumpAt < 0.2) AudioKit.Play(mech ? "mechjump" : "jump", rel == Rel.Self ? null : pos, 0.7f, rel); p.jump = c.jumpAt; }
                if (c.landAt > p.land) { if (w.time - c.landAt < 0.2) AudioKit.Play(mech ? "mechland" : heavy ? "land_heavy" : "land", rel == Rel.Self ? null : pos, 0.8f, rel); p.land = c.landAt; }
                // footsteps: one per stride while moving on the ground (stride ~ 0.75 x height)
                if (p.has && a.grounded)
                {
                    float moved = new Vector2(pos.x - p.pos.x, pos.z - p.pos.z).magnitude;
                    if (moved < 3) p.stride += moved;
                    float stride = Mathf.Max(0.9f, (float)a.Height * 0.75f);
                    if (p.stride >= stride) { p.stride = 0; Step(a, rel, pos, mech, heavy); }
                }
                p.pos = pos; p.has = true;
                // the player's own low-health call
                bool low = a.hp + a.armor < a.MaxHp * 0.3;
                if (rel == Rel.Self && low && !p.low) Say(a, "low_hp", 0.8f);
                p.low = low;
            }
        }

        void Step(Actor a, Rel rel, Vector3 pos, bool mech, bool heavy)
        {
            string id;
            if (mech) id = "mechstep";
            else if (a.def.id == "gantetsu") id = "step_heavy";
            else if (a.def.id == "hibiki") { if (Random.value < 0.5f) return; id = "skate"; }
            else
            {
                string m = r.World.level.MatAt(a.pos.x, a.pos.z, a.pos.y + 0.2);
                id = m == "wood" ? "step_wood" : m == "trim" || m == "glass" || m == "window" ? "step_metal" : "step_stone";
            }
            AudioKit.Play(id, rel == Rel.Self ? null : pos, rel == Rel.Self ? 0.45f : 1, rel);
        }
    }
}
