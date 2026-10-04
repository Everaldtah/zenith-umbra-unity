// The kill cam (the user, 2026-10-04: "a camera view that shows ... what happened to you before you die and who killed you
// ... just like overwatch does", in normal play and in Stadium). Unity only: the web game has none.
//
// Every match keeps the last seconds of what the views draw: 30 times a second a frozen copy of every hero (Actor.Snapshot)
// and every projectile, and the effect / sound events with their sim times. When the local player is eliminated, after a
// beat on the death itself, the same hero views replay that record - the seconds before the death and a moment after -
// from behind the killer's shoulder, the lens kept on the victim, with a banner naming the killer and what each attacker
// dealt. The simulation is never touched: the live match runs on underneath and the view returns to it when the replay
// ends, is skipped (Space / Enter), or the hero is back.
//
// MatchRunner drives it through four calls (its SyncViews / Dispatch): Replay(runner) -> the cam while it plays;
// Past(actor) -> the hero as it was; SwapProjs(world) -> the projectiles as they were; Place(camera); and
// Hold(runner, event) keeps the live match's effects and sounds out of the replay. Without those calls nothing replays.
// Off switch: `-zu-killcam=0` on the command line, or KillCam.Enabled = false.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using ZU.Game.UI;
using ZU.Sim;

namespace ZU.Game.Fx
{
    [DefaultExecutionOrder(9000)]
    public sealed class KillCam : MonoBehaviour, IViewHost
    {
        /// <summary>seconds of record kept; samples a second; the replay's most seconds before the death and its seconds after;
        /// the beat on the death before it starts; what must be left of the respawn wait for a replay to be worth it</summary>
        const float KEEP = 9, RATE = 30, LEAD = 4.5f, TAIL = 0.5f, DELAY = 0.9f, MIN_PLAY = 1.8f;

        static bool? enabled;
        public static bool Enabled
        {
            get { enabled ??= System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-zu-killcam=0") < 0; return enabled.Value; }
            set { enabled = value; }
        }
        public static KillCam Current { get; private set; }

        sealed class Frame { public double t; public Dictionary<int, Actor> actors; public List<Proj> projs; }
        struct Hit { public double t; public string who, color; public double amt; public bool crit; }

        MatchRunner r;
        readonly List<Frame> frames = new List<Frame>();
        readonly List<(double t, SimEvent e)> events = new List<(double, SimEvent)>();
        readonly List<Hit> hits = new List<Hit>();
        double lastSample = -1;
        // the death waiting for its replay, and the replay
        double deathAt = -1; int killerId, victimId; string killerName = "", killerTitle = "", killerColor = "#ffffff";
        bool pending, replaying, redispatch;
        double t0, t1, replayT; int cur, nextEvent;
        Vector3 camPos; Quaternion camRot; bool camInit;
        readonly List<string> recap = new List<string>();

        public static KillCam Attach(MatchRunner runner)
        {
            var k = runner.GetComponent<KillCam>();
            if (k == null) k = runner.gameObject.AddComponent<KillCam>();
            k.r = runner; Current = k;
            return k;
        }

        void OnEnable() { EventSink.OnEvent += OnEvent; }
        void OnDisable() { EventSink.OnEvent -= OnEvent; if (Current == this) Current = null; }

        /// <summary>state in one line, for the Editor's zu_killcam_test</summary>
        public string Diag => $"enabled {Enabled} pending {pending} replaying {replaying} t {(replaying ? replayT - deathAt : 0):+0.00;-0.00} window [{t0 - deathAt:0.00}..{t1 - deathAt:0.00}] " +
                              $"frames {frames.Count} cur {cur} events {events.Count} hits {hits.Count} killer '{killerName}' recap {recap.Count}";

        // ------------------------------------------------------------------------------------------------ MatchRunner's calls
        /// <summary>the cam while it replays for this runner (null: draw the live match)</summary>
        public static KillCam Replay(MatchRunner runner) => Current != null && Current.r == runner && Current.replaying ? Current : null;
        /// <summary>true: a live effect or sound that must not be shown now (the replay is on; it belongs to the present)</summary>
        public static bool Hold(MatchRunner runner, SimEvent e)
        {
            var k = Current;
            return k != null && k.r == runner && k.replaying && !k.redispatch && (e is FxEvent || e is SfxEvent);
        }
        /// <summary>the hero as it was at the replay's moment (null: not in the record - draw it live)</summary>
        public Actor Past(Actor a) => cur < frames.Count && frames[cur].actors.TryGetValue(a.id, out var p) ? p : null;
        /// <summary>the same for anyone holding a live actor outside MatchRunner (the ragdolls, the lids)</summary>
        public static Actor PastOrLive(MatchRunner runner, Actor a) => Replay(runner)?.Past(a) ?? a;
        public static float TimeOr(MatchRunner runner, float now) => Replay(runner) is KillCam k ? (float)k.replayT : now;
        /// <summary>puts the recorded projectiles in the world's list for the views' sync; returns the live list to put back</summary>
        public List<Proj> SwapProjs(World w)
        {
            var live = w.projs;
            w.projs = cur < frames.Count ? frames[cur].projs : new List<Proj>();
            return live;
        }

        // ------------------------------------------------------------------------------------------------ IViewHost: the past
        public double SimTime => replayT;
        public Actor Player => null;                 // (nobody is "you" in the replay: your own body is drawn)
        public bool ThirdPerson => true;
        public Vector3 DrawPos(Actor a)
        {
            var p0 = Conv.U(a.pos);
            if (cur + 1 >= frames.Count || !frames[cur + 1].actors.TryGetValue(a.id, out var n)) return p0;
            var p1 = Conv.U(n.pos);
            if ((p1 - p0).sqrMagnitude > 36) return p0;                 // a respawn or a blink between two samples: no slide
            double span = frames[cur + 1].t - frames[cur].t;
            return Vector3.Lerp(p0, p1, span > 1e-6 ? Mathf.Clamp01((float)((replayT - frames[cur].t) / span)) : 0);
        }

        // ------------------------------------------------------------------------------------------------ the record
        void OnEvent(MatchRunner runner, SimEvent e)
        {
            if (runner != r || r == null || r.World == null || redispatch) return;
            double t = r.World.time;
            var me = r.Player;
            switch (e)
            {
                case FxEvent _: case SfxEvent _: if (!replaying) events.Add((t, e)); break;
                case DmgEvent d when me != null && d.tgt == me && !d.heal && d.src != null && d.src != me:
                    hits.Add(new Hit { t = t, who = (d.src.owner ?? d.src).baseDef.name, color = (d.src.owner ?? d.src).baseDef.color, amt = d.amt, crit = d.crit });
                    break;
                case KillEvent k when me != null && k.tgt == me:
                {
                    var by = k.src != null && k.src != me ? k.src : me.lastHitBy != me ? me.lastHitBy : null;
                    by = by?.owner ?? by;
                    deathAt = t; victimId = me.id; killerId = by?.id ?? 0;
                    killerName = by != null ? by.baseDef.name : ""; killerTitle = by != null ? by.baseDef.title ?? "" : ""; killerColor = by?.baseDef.color ?? "#ffffff";
                    pending = Enabled;
                    break;
                }
            }
        }

        void Sample(World w)
        {
            var f = new Frame { t = w.time, actors = new Dictionary<int, Actor>(w.actors.Count), projs = new List<Proj>(w.projs.Count) };
            foreach (var a in w.actors)
            {
                if (a.IsSummon && string.IsNullOrEmpty(a.def.model)) continue;      // (the puppets are one swarm, not views)
                f.actors[a.id] = a.Snapshot();
            }
            foreach (var p in w.projs) f.projs.Add(p.Clone());
            frames.Add(f);
            double old = w.time - KEEP;
            int drop = 0; while (drop < frames.Count && frames[drop].t < old) drop++;
            if (drop > 0) frames.RemoveRange(0, drop);
            drop = 0; while (drop < events.Count && events[drop].t < old) drop++;
            if (drop > 0) events.RemoveRange(0, drop);
            drop = 0; while (drop < hits.Count && hits[drop].t < old) drop++;
            if (drop > 0) hits.RemoveRange(0, drop);
        }

        // ------------------------------------------------------------------------------------------------ the replay
        void Update()
        {
            if (r == null || r.World == null) return;
            var w = r.World; var me = r.Player;
            if (!replaying)
            {
                if (w.time - lastSample >= 1.0 / RATE) { lastSample = w.time; Sample(w); }
                if (!pending) return;
                if (me == null || me.alive || me.id != victimId || !Enabled) { pending = false; return; }
                if (w.time < deathAt + DELAY || PauseMenu.Paused || ModeHud.Shopping(r)) return;
                pending = false;
                // what is left of the wait for the respawn decides how far back the replay starts
                double left = me.respawnAt > 0 ? me.respawnAt - w.time - 0.4 : LEAD + TAIL;
                if (left < MIN_PLAY || frames.Count < 4) return;
                double lead = System.Math.Min(LEAD, left - TAIL);
                t0 = System.Math.Max(frames[0].t, deathAt - lead); t1 = deathAt + TAIL;
                replayT = t0; cur = 0; nextEvent = 0; camInit = false;
                while (nextEvent < events.Count && events[nextEvent].t < t0) nextEvent++;
                Recap();
                replaying = true;
                return;
            }
            // playing: the clock, the frame under it, the effects and sounds of that moment
            var kb = Keyboard.current;
            bool skip = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
            if (!PauseMenu.Paused) replayT += Time.deltaTime;
            if (skip || replayT >= t1 || me == null || me.alive || !Enabled) { replaying = false; lastSample = -1; return; }
            while (cur + 1 < frames.Count && frames[cur + 1].t <= replayT) cur++;
            redispatch = true;
            try { while (nextEvent < events.Count && events[nextEvent].t <= replayT) { var e = events[nextEvent++].e; EventSink.Handle(r, e); } }
            finally { redispatch = false; }
        }

        void Recap()
        {
            recap.Clear();
            // who did what to you in the seconds before: one line an attacker, the latest first
            foreach (var g in hits.Where(h => h.t >= deathAt - 8 && h.t <= deathAt + 0.1).GroupBy(h => h.who).OrderByDescending(g => g.Max(h => h.t)).Take(4))
            {
                int n = g.Count(); bool crit = g.Any(h => h.crit);
                recap.Add($"{g.Key}   {System.Math.Round(g.Sum(h => h.amt))} damage   {n} hit{(n == 1 ? "" : "s")}{(crit ? "   critical" : "")}");
            }
        }

        /// <summary>the lens: behind the killer's shoulder, turned toward the victim (no killer: a slow circle round the body)</summary>
        public void Place(Transform cam)
        {
            if (cur >= frames.Count) return;
            var f = frames[cur];
            f.actors.TryGetValue(victimId, out var v);
            Actor k = null; if (killerId != 0) f.actors.TryGetValue(killerId, out k);
            Vector3 pos; Quaternion rot;
            if (k != null)
            {
                var feet = DrawPos(k); float eye = (float)(k.Eye.y - k.pos.y), s = Mathf.Max(1, (float)k.scale);
                var aim = Conv.Aim(k.yaw, k.pitch);
                var pivot = feet + Vector3.up * eye;
                pos = pivot + aim * new Vector3(0.7f * s, 0.35f * s, -3.4f * s);
                // keep off walls, as the match camera does
                var d = pos - pivot;
                if (Physics.SphereCast(pivot, 0.2f, d.normalized, out var hit, d.magnitude)) pos = pivot + d.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
                var ahead = pivot + aim * Vector3.forward * 14;
                var look = v != null ? Vector3.Lerp(ahead, DrawPos(v) + Vector3.up * (float)(v.Height * 0.6), 0.6f) : ahead;
                rot = Quaternion.LookRotation((look - pos).sqrMagnitude > 1e-4f ? look - pos : aim * Vector3.forward);
            }
            else if (v != null)
            {
                var c = DrawPos(v) + Vector3.up * (float)(v.Height * 0.6);
                float a = (float)(replayT - t0) * 0.5f;
                pos = c + new Vector3(Mathf.Sin(a) * 5, 2.2f, Mathf.Cos(a) * 5);
                rot = Quaternion.LookRotation(c - pos);
            }
            else return;
            if (!camInit) { camPos = pos; camRot = rot; camInit = true; }
            else { float kq = 1 - Mathf.Exp(-Time.deltaTime * 10); camPos += (pos - camPos) * kq; camRot = Quaternion.Slerp(camRot, rot, kq); }
            cam.SetPositionAndRotation(camPos, camRot);
        }

        // ------------------------------------------------------------------------------------------------ the banner
        GUIStyle title, name, line, hint; Texture2D shade;
        void OnGUI()
        {
            if (!replaying) return;
            float H = Screen.height, W = Screen.width, u = H / 1080f;
            if (title == null)
            {
                shade = new Texture2D(1, 1); shade.SetPixel(0, 0, new Color(0, 0, 0, 0.55f)); shade.Apply();
                title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                name = new GUIStyle(title); line = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                hint = new GUIStyle(line);
            }
            title.fontSize = Mathf.RoundToInt(30 * u); name.fontSize = Mathf.RoundToInt(44 * u); line.fontSize = Mathf.RoundToInt(22 * u); hint.fontSize = Mathf.RoundToInt(18 * u);
            // top: KILL CAM; under it who did it
            GUI.DrawTexture(new Rect(0, 0, W, 150 * u), shade);
            title.normal.textColor = new Color(1, 0.23f, 0.36f);
            GUI.Label(new Rect(0, 14 * u, W, 40 * u), "KILL CAM", title);
            name.normal.textColor = Conv.Hex(killerColor);
            GUI.Label(new Rect(0, 56 * u, W, 56 * u), killerName != "" ? $"ELIMINATED BY  {killerName.ToUpperInvariant()}" : "ELIMINATED", name);
            line.normal.textColor = new Color(0.85f, 0.88f, 0.94f);
            if (killerTitle != "") GUI.Label(new Rect(0, 112 * u, W, 30 * u), killerTitle, line);
            // bottom: what each attacker dealt, and the way out
            float bh = (recap.Count * 30 + 54) * u;
            GUI.DrawTexture(new Rect(W * 0.3f, H - bh - 120 * u, W * 0.4f, bh), shade);
            for (int i = 0; i < recap.Count; i++) GUI.Label(new Rect(W * 0.3f, H - bh - 120 * u + (10 + i * 30) * u, W * 0.4f, 30 * u), recap[i], line);
            hint.normal.textColor = new Color(1, 0.84f, 0.42f);
            GUI.Label(new Rect(W * 0.3f, H - 120 * u - 40 * u, W * 0.4f, 30 * u), "SPACE  skip", hint);
        }
    }
}
