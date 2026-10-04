// Play of the Game: which few seconds of a match were its best play, and whose.
//
// UNITY ONLY (the web game has none). The method is Overwatch's, as Blizzard describes it in U.S. patent 10,456,680
// ("Determining play of the game based on gameplay events") and in its developers' talks:
//   - the match is a log of events; every event that matters is given a score in one of four CATEGORIES:
//       HIGH SCORE    eliminations in quick succession. A multikill grows faster than its count, a solo kill and an
//                     environmental kill are worth more, a kill made with an ultimate running is worth less (those
//                     would otherwise win every time), a kill on the objective a little more;
//       LIFESAVER     an enemy stopped while a teammate was about to die to it, and a fallen teammate brought back;
//       SHARPSHOOTER  a hard shot: far away, on a target in the air or moving fast, from the air, a critical hit;
//       SHUTDOWN      an enemy stopped just as it used its ultimate (worth more with the team it threatened close by),
//                     and one hero's ability turning another's back on it (the game's counters);
//   - a SLIDING WINDOW as long as the replay passes over the log; for every player and category the scores of the
//     events inside the window are added up, and the best window is that player's play in that category;
//   - HIGH SCORE is the default; another category takes the play when it clears its own bar and beats it.
// A fifth, IMPACT (damage dealt and healing done in the window), is never the Play of the Game on its own merit; it is the
// fallback that gives every player who did anything a "best moment" for their highlights.
//
// The detector only listens (World.taps); it never changes the simulation.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZU.Sim
{
    public sealed class PlayEvent
    {
        public double t; public int actor; public string cat; public double pts; public string text;
        public bool kill;           // an elimination (the multikill ladder counts these)
    }

    public sealed class Play
    {
        public Actor actor; public string category; public double score;
        /// <summary>the first and last scored event of the play (sim seconds); a replay adds its lead-in and its tail</summary>
        public double t0, t1;
        public List<PlayEvent> events = new List<PlayEvent>();
        public int Kills => events.Count(e => e.kill);
        /// <summary>one line for the banner: "4 eliminations in 6.2 s", "Shut down Enra's ultimate", ...</summary>
        public string summary = "";
        public string Label => Plays.LABEL.TryGetValue(category ?? "", out var l) ? l : "BEST MOMENT";
    }

    public sealed class Plays
    {
        public const string HIGH = "high", LIFE = "life", SHARP = "sharp", SHUT = "shut", IMPACT = "impact";
        public static readonly Dictionary<string, string> LABEL = new Dictionary<string, string>
        { [HIGH] = "HIGH SCORE", [LIFE] = "LIFESAVER", [SHARP] = "SHARPSHOOTER", [SHUT] = "SHUTDOWN", [IMPACT] = "BEST MOMENT" };
        /// <summary>the window: as long as the replay of a play may be</summary>
        public const double WINDOW = 10;
        /// <summary>what a category needs before it can be the Play of the Game (HIGH: a double kill; the others: one clean
        /// event of their kind)</summary>
        static readonly Dictionary<string, double> BAR = new Dictionary<string, double> { [HIGH] = 250, [SHUT] = 240, [LIFE] = 200, [SHARP] = 190 };

        readonly World w;
        readonly List<PlayEvent> log = new List<PlayEvent>();
        readonly Dictionary<int, int> ults = new Dictionary<int, int>();                       // actor -> ultimates cast so far
        readonly Dictionary<int, (double t, string name)> ultAt = new Dictionary<int, (double, string)>();
        readonly Dictionary<int, double> lastKillAt = new Dictionary<int, double>();
        // victim -> attacker -> (when, kind, crit, distance, the victim was in the air, its speed, the attacker was in the air)
        readonly Dictionary<int, Dictionary<int, Hit>> hits = new Dictionary<int, Dictionary<int, Hit>>();
        struct Hit { public double t; public string kind; public bool crit, tgtAir, srcAir; public double dist, speed; }

        public IReadOnlyList<PlayEvent> Log => log;

        Plays(World w) { this.w = w; }

        /// <summary>listen to this world from now on</summary>
        public static Plays Attach(World w)
        {
            var p = new Plays(w);
            w.taps.Add(p.On);
            return p;
        }

        static Actor Who(Actor a) => a == null ? null : a.owner != null && a.IsSummon ? a.owner : a;
        static bool Counts(Actor a) => a != null && !a.isRobot && !a.IsSummon;
        static double Dist(V3 a, V3 b) { double x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return Math.Sqrt(x * x + y * y + z * z); }

        void Add(Actor a, string cat, double pts, string text, bool kill = false)
        {
            if (!Counts(a) || pts <= 0) return;
            log.Add(new PlayEvent { t = w.time, actor = a.id, cat = cat, pts = pts, text = text, kill = kill });
        }

        V3? Objective()
        {
            if (w.rules == "push" && w.push != null) return w.push.pos;
            var p = w.map?.point;
            return p != null && p.Length >= 3 ? new V3(p[0], p[1], p[2]) : (V3?)null;
        }

        // ------------------------------------------------------------------------------------------------ the log
        void On(SimEvent e)
        {
            double t = w.time;
            switch (e)
            {
                case CastEvent c when c.actor != null:
                {
                    int n = c.actor.ults; ults.TryGetValue(c.actor.id, out int had);
                    if (n > had) { ults[c.actor.id] = n; ultAt[c.actor.id] = (t, c.name ?? "ultimate"); }
                    break;
                }
                case DmgEvent d when d.src != null && d.tgt != null && d.src != d.tgt:
                {
                    var src = Who(d.src);
                    if (d.heal)
                    {
                        // a heal that reaches a teammate who is nearly gone and under fire
                        if (Counts(src) && d.tgt.alive && d.amt >= 40 && t - d.tgt.lastHitAt < 1.5 && d.tgt.hp - d.amt <= 0.3 * d.tgt.MaxHp)
                            Add(src, LIFE, 60, $"Kept {d.tgt.def.name} alive");
                        if (Counts(src)) Add(src, IMPACT, d.amt * 0.5, null);
                        break;
                    }
                    if (src == null || src.team == d.tgt.team) break;
                    if (!hits.TryGetValue(d.tgt.id, out var m)) hits[d.tgt.id] = m = new Dictionary<int, Hit>();
                    m[src.id] = new Hit
                    {
                        t = t, kind = d.kind, crit = d.crit, dist = Dist(d.src.pos, d.tgt.pos),
                        tgtAir = !d.tgt.grounded, srcAir = !d.src.grounded && !d.src.flying,
                        speed = Math.Sqrt(d.tgt.vel.x * d.tgt.vel.x + d.tgt.vel.z * d.tgt.vel.z),
                    };
                    if (Counts(src) && !d.tgt.IsSummon) Add(src, IMPACT, d.amt * (d.tgt.isRobot ? 0.15 : 0.5), null);
                    break;
                }
                case KillEvent k: Kill(k.tgt, Who(k.src), false); break;
                case DemechEvent dm: Kill(dm.tgt, Who(dm.src), true); break;
                case CounterEvent c when Counts(Who(c.actor)) && c.target != null:
                    Add(Who(c.actor), SHUT, 160, string.IsNullOrEmpty(c.text) ? $"Countered {c.target.def.name}" : c.text);
                    break;
                case FxEvent f when f.kind == "rebirth" && f.actor != null:
                {
                    // (Rebirth.Resurrect: the one brought back carries who did it)
                    var by = w.actors.FirstOrDefault(x => x.id == (int)f.actor.Sv("rebornBy", -1));
                    if (Counts(by) && by != f.actor) Add(by, LIFE, 220, $"Revived {f.actor.def.name}");
                    break;
                }
            }
        }

        void Kill(Actor tgt, Actor by, bool demech)
        {
            double t = w.time;
            hits.TryGetValue(tgt.id, out var m);
            if (!Counts(by) || tgt == null || tgt.IsSummon || by.team == tgt.team) { hits.Remove(tgt?.id ?? 0); return; }
            Hit h = default; bool hit = m != null && m.TryGetValue(by.id, out h);
            string victim = tgt.def.name;
            // (training dummies and campaign minions are worth a fraction; a campaign boss a great deal)
            double worth = tgt.def.id != null && tgt.def.id.StartsWith("boss_") ? 3 : tgt.isRobot ? 0.3 : 1;

            // ---- HIGH SCORE
            bool solo = m == null || !m.Any(kv => kv.Key != by.id && t - kv.Value.t < 6);
            bool env = !hit || t - h.t > 0.35;                      // nothing of the killer's hit it as it died: the map did it
            bool ulting = ultAt.TryGetValue(by.id, out var bu) && t - bu.t < 10;
            var obj = Objective();
            bool onPoint = obj.HasValue && Dist(obj.Value, tgt.pos) < 14;
            double pts = (demech ? 70 : 100) * worth;
            if (solo) pts *= 1.25;
            if (env) pts *= 1.4;
            if (ulting) pts *= 0.8;
            if (onPoint) pts += 30;
            if (lastKillAt.TryGetValue(by.id, out double prev) && t - prev <= 2.5) pts += 40;       // in quick succession
            lastKillAt[by.id] = t;
            var tags = new List<string>();
            if (solo) tags.Add("solo");
            if (env) tags.Add("environmental");
            Add(by, HIGH, pts, (demech ? $"Broke {victim}'s frame" : $"Eliminated {victim}") + (tags.Count > 0 ? $" ({string.Join(", ", tags)})" : ""), !demech);

            // ---- SHARPSHOOTER: a hard shot with the weapon (not an ability, not a melee blow)
            if (hit && !env && h.kind != "ability" && h.kind != "melee" && !demech)
            {
                double s = 0; var how = new List<string>();
                if (h.dist >= 18) { s += Math.Min(140, (h.dist - 18) * 6); how.Add($"{h.dist:0} m"); }
                if (h.tgtAir) { s += 70; how.Add("airborne target"); }
                if (h.srcAir) { s += 40; how.Add("from the air"); }
                if (h.crit) { s += 50; how.Add("critical"); }
                if (h.speed > 9) { s += 30; how.Add("moving target"); }
                if (s >= 70) Add(by, SHARP, (80 + s) * worth, $"Hard shot on {victim} ({string.Join(", ", how)})");
            }

            // ---- SHUTDOWN: it had just used its ultimate
            if (ultAt.TryGetValue(tgt.id, out var tu) && t - tu.t < 4 && !demech)
            {
                int near = w.actors.Count(x => x.alive && x.team == by.team && x != by && !x.IsSummon && Dist(x.pos, tgt.pos) < 15);
                Add(by, SHUT, (240 + (near >= 2 ? 80 : 0)) * worth, $"Shut down {victim}'s {tu.name}");
                ultAt.Remove(tgt.id);
            }

            // ---- LIFESAVER: a teammate it was killing lives
            foreach (var a in w.actors)
            {
                if (!a.alive || a == by || a.team != by.team || a.IsSummon || a.isRobot) continue;
                if (Who(a.lastHitBy) != tgt || t - a.lastHitAt > 2) continue;
                double frac = a.hp / Math.Max(1, a.MaxHp);
                if (frac > 0.35) continue;
                Add(by, LIFE, 150 + (0.35 - frac) / 0.35 * 100, $"Saved {a.def.name}");
            }
            hits.Remove(tgt.id);
        }

        // ------------------------------------------------------------------------------------------------ the window
        /// <summary>a player's best window in one category (null: nothing of that kind)</summary>
        Play BestOf(Actor a, string cat, double from, double to)
        {
            Play best = null;
            var ev = log.Where(e => e.actor == a.id && e.cat == cat && e.t >= from && e.t <= to).ToList();
            for (int i = 0; i < ev.Count; i++)
            {
                double sum = 0; int kills = 0, j = i;
                for (; j < ev.Count && ev[j].t <= ev[i].t + WINDOW; j++)
                {
                    // the multikill ladder: the second kill of a window counts 1.5 times, the third twice, ...
                    if (ev[j].kill) { sum += ev[j].pts * (1 + 0.5 * kills); kills++; } else sum += ev[j].pts;
                }
                if (best != null && sum <= best.score) continue;
                best = new Play { actor = a, category = cat, score = sum, t0 = ev[i].t, t1 = ev[j - 1].t, events = ev.GetRange(i, j - i) };
            }
            if (best != null) best.summary = Summary(best);
            return best;
        }

        static string Summary(Play p)
        {
            var named = p.events.Where(e => e.text != null).ToList();
            switch (p.category)
            {
                case HIGH:
                {
                    int n = p.Kills;
                    if (n >= 2) return $"{n} eliminations in {Math.Max(0.1, p.t1 - p.t0):0.0} s";
                    return named.Count > 0 ? named[0].text : "Elimination";
                }
                case IMPACT: return $"{Math.Round(p.score * 2)} damage and healing in {WINDOW:0} s";
                default: return named.Count == 1 ? named[0].text : named.Count > 1 ? $"{named[0].text} +{named.Count - 1} more" : "";
            }
        }

        /// <summary>the best play so far among the players the filter lets in (null filter: everyone).
        /// High Score is the default; a category that clears its bar and scores higher takes it; with nothing over a bar, the
        /// highest of the four; with none of the four at all, the window of greatest impact.</summary>
        public Play Best(Func<Actor, bool> who = null, double from = 0, double to = double.PositiveInfinity)
        {
            Play over = null, any = null, impact = null;
            foreach (var a in w.actors)
            {
                if (!Counts(a) || (who != null && !who(a))) continue;
                foreach (var cat in new[] { HIGH, SHUT, LIFE, SHARP })
                {
                    var p = BestOf(a, cat, from, to);
                    if (p == null) continue;
                    if (p.score >= BAR[cat] && Better(p, over)) over = p;
                    if (Better(p, any)) any = p;
                }
                var im = BestOf(a, IMPACT, from, to);
                if (im != null && im.score >= 20 && Better(im, impact)) impact = im;
            }
            return over ?? any ?? impact;
        }

        // (the higher score; level: the side that won, then the later play)
        bool Better(Play p, Play than)
        {
            if (than == null) return true;
            if (Math.Abs(p.score - than.score) > 1e-6) return p.score > than.score;
            bool pw = p.actor.team == w.winner, tw = than.actor.team == w.winner;
            if (pw != tw) return pw;
            return p.t0 > than.t0;
        }
    }
}
