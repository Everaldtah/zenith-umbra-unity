// Training Grounds - the Spar Arena (desktop edition): one-on-one against any hero at Easy / Medium / Hard. The opponent
// waits in the arena; walking in seals a blue holographic box around the two of you, and it stays sealed until someone
// has won the spar (first to 1, 2 or 3 rounds). Every round starts from full health at opposite ends after a
// countdown and ends on a kill. Nothing crosses the walls while they are up - no one in or out, no shot in or out.
// Port of zenith-umbra src/game/spar.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    /// <summary>(TS SparOpts) diff "easy" | "medium" | "hard"</summary>
    public class SparOpts
    {
        public string hero, diff; public int firstTo;
        public SparOpts Clone() => (SparOpts)MemberwiseClone();
    }
    /// <summary>winner "you" | "them" | "draw"</summary>
    public class SparRound { public string winner; public double secs, dealt, taken; }
    public class SparResult { public string you, foe, diff; public int[] score; public bool won; }

    public class Spar
    {
        /// <summary>the opponent's AI skill (Bot: aim error and reaction time) per difficulty</summary>
        public static readonly Dictionary<string, double> SPAR_SKILL = new Dictionary<string, double> { ["easy"] = 0.3, ["medium"] = 0.62, ["hard"] = 0.92 };
        /// <summary>the box on the Proving Grounds: centre, half extents and height (clear floor between the spawn and the north wall)</summary>
        public static readonly (double x, double z, double hx, double hz, double h) ARENA = (-29, 17, 9, 7, 9);
        /// <summary>where each side starts a round: the two ends of the box, facing each other</summary>
        public static readonly ((double x, double z, double yaw) you, (double x, double z, double yaw) them) SPAR_START =
            ((ARENA.x - 7, ARENA.z, Math.PI / 2), (ARENA.x + 7, ARENA.z, -Math.PI / 2));
        public static readonly (double x, double z) SPAR_CONSOLE = (ARENA.x - 5, ARENA.z - ARENA.hz - 1.6);
        public const double COUNTDOWN = 3;       // seconds frozen at the ends before FIGHT
        public const double ROUND_END = 3;       // seconds between a kill and the next countdown
        public const double DONE_SECS = 3;       // the result shows this long before the box opens
        public static readonly SparOpts DEFAULT_SPAR = new SparOpts { hero = "kagemaru", diff = "medium", firstTo = 2 };

        public readonly World w; public readonly INav nav; public readonly HeroRange range;
        public SparOpts opts = DEFAULT_SPAR.Clone();
        /// <summary>SparPhase: "off" | "waiting" | "countdown" | "fight" | "roundover" | "done"</summary>
        public string phase = "off";
        public Actor foe;
        public SparBrain brain;
        public double phaseAt;
        public int round;
        public Dictionary<string, int> wins = new Dictionary<string, int> { ["you"] = 0, ["them"] = 0 };
        /// <summary>this round: who won it (set at the first kill), and the damage each way</summary>
        public string roundWinner;
        public double roundAt, dealt, taken;
        public List<SparRound> rounds = new List<SparRound>();
        public List<SparResult> history = new List<SparResult>();
        /// <summary>the box just opened with you inside it: a new spar starts only once you have stepped out and back in</summary>
        public bool needExit;
        /// <summary>a fighter was put on their spot (the view turns your camera to face the opponent)</summary>
        public Action<Actor> onPlace;

        public Spar(World w, INav nav, HeroRange range = null)
        {
            this.w = w; this.nav = nav; this.range = range;
            w.tickers.Add(dt => Tick(dt));
            w.taps.Add(e => Observe(e));
            w.gate = (s, t) => Allow(s, t);
        }

        public Actor Player => w.actors.FirstOrDefault(a => a.isPlayer);
        public bool Sealed => phase == "countdown" || phase == "fight" || phase == "roundover" || phase == "done";
        /// <summary>seconds left in the countdown / before the next round / before the box opens</summary>
        public double Left { get { double d = phase == "countdown" ? COUNTDOWN : phase == "roundover" ? ROUND_END : phase == "done" ? DONE_SECS : 0; return Math.Max(0, phaseAt + d - w.time); } }

        public bool Inside(V3 p, double margin = 0) => Math.Abs(p.x - ARENA.x) <= ARENA.hx + margin && Math.Abs(p.z - ARENA.z) <= ARENA.hz + margin;
        /// <summary>a fighter's side: you or the opponent, or something one of you summoned</summary>
        bool Duelist(Actor a) { var me = Player; var f = foe; return a != null && (a == me || a == f || (a.owner != null && (a.owner == me || a.owner == f))); }

        /// <summary>put the opponent in the arena, waiting for you to walk in (re-arming while sealed is refused)
        /// (TS arm(Partial&lt;SparOpts&gt;): the options left out keep their current values)</summary>
        public Actor Arm(string hero = null, string diff = null, double? firstTo = null)
        {
            if (Sealed) return null;
            var next = opts.Clone();
            if (hero != null) next.hero = hero; if (diff != null) next.diff = diff;
            if (!GameData.Current.Hero.ContainsKey(next.hero)) return null;
            next.firstTo = (int)Math.Max(1, Math.Min(5, JsMath.Round(firstTo ?? next.firstTo)));
            opts = next;
            if (foe == null || foe.baseDef.id != next.hero)
            {
                Remove();
                foe = w.AddHero(next.hero, "umbra");
            }
            var a = foe;
            a.spawn = new[] { SPAR_START.them.x, SPAR_START.them.z };
            brain = new SparBrain(this, a);
            a.controller = brain;
            Reset(a, SPAR_START.them);
            phase = "waiting"; phaseAt = w.time;
            var me = Player;
            needExit = me != null && Inside(me.pos);
            return a;
        }

        /// <summary>the opponent leaves (sealed: you forfeit the spar)</summary>
        public void Cancel()
        {
            if (Sealed && phase != "done") { wins["them"] = Math.Max(wins["them"], opts.firstTo); Finish(); }
            Remove();
            phase = "off";
        }

        void Remove()
        {
            var f = foe; if (f == null) return;
            w.actors = w.actors.Where(x => x != f && x.owner != f).ToList();
            w.zones = w.zones.Where(z => z.owner != f).ToList();
            w.projs = w.projs.Where(p => p.owner != f).ToList();
            f.controller = null; f.alive = false;
            foe = null; brain = null;
        }

        /// <summary>full health, cooldowns and ammo, nothing of theirs left in the world, standing on the spot</summary>
        void Reset(Actor a, (double x, double z, double yaw) at)
        {
            double ult = a.ult;
            w.actors = w.actors.Where(x => !(x.IsSummon && x.owner == a)).ToList();
            w.zones = w.zones.Where(z => z.owner != a).ToList();
            w.projs = w.projs.Where(p => p.owner != a).ToList();
            w.Respawn(a, true);
            a.ult = ult;
            double g = w.level.GroundAt(at.x, at.z, 4);
            a.pos = new V3(at.x, Math.Max(0, g > double.NegativeInfinity ? g : 0), at.z);
            a.vel = V3.Zero;
            a.yaw = a.input.yaw = at.yaw; a.input.pitch = 0;
            a.cd = new Dictionary<string, double>();
            onPlace?.Invoke(a);
        }

        void StartRound()
        {
            var me = Player; var f = foe; if (me == null || f == null) return;
            round++;
            Reset(me, SPAR_START.you); Reset(f, SPAR_START.them);
            roundWinner = null; dealt = taken = 0;
            phase = "countdown"; phaseAt = w.time;
            w.Sfx("announce");          // (the scoreboard and the big centre text are the view's: RangeView)
        }

        void Finish()
        {
            var me = Player; bool won = wins["you"] > wins["them"];
            history.Insert(0, new SparResult { you = me?.def.name ?? "-", foe = foe?.baseDef.name ?? "-", diff = opts.diff, score = new[] { wins["you"], wins["them"] }, won = won });
            if (history.Count > 6) history.RemoveRange(6, history.Count - 6);
            phase = "done"; phaseAt = w.time;
            w.Sfx("announce");
        }

        void Tick(double _dt)
        {
            double t = w.time; var me = Player; var f = foe;
            if (range != null) range.suspended = Sealed;
            if (phase == "off" || f == null) return;
            if (phase == "waiting")
            {
                if (me == null || !me.alive) return;
                if (!Inside(me.pos)) { needExit = false; return; }
                if (needExit) return;
                // you walked in: the box seals
                round = 0; wins = new Dictionary<string, int> { ["you"] = 0, ["them"] = 0 }; rounds = new List<SparRound>();
                StartRound();
                return;
            }
            if (phase == "countdown")
            {
                // frozen at the ends (no damage either way: Allow()); the cooldowns start fresh on FIGHT
                foreach (var (a, s) in new[] { (me, SPAR_START.you), (f, SPAR_START.them) })
                {
                    if (a == null || !a.alive) continue;
                    a.pos.x = s.x; a.pos.z = s.z; a.vel.x = a.vel.z = 0; if (a.vel.y > 0) a.vel.y = 0;
                }
                if (t >= phaseAt + COUNTDOWN)
                {
                    foreach (var a in new[] { me, f }) if (a != null) { a.cd = new Dictionary<string, double>(); a.ammo = a.MaxAmmo; a.reloadUntil = 0; }
                    phase = "fight"; phaseAt = t; roundAt = t;
                }
            }
            else if (phase == "roundover")
            {
                if (t >= phaseAt + ROUND_END)
                {
                    if (wins["you"] >= opts.firstTo || wins["them"] >= opts.firstTo) Finish();
                    else StartRound();
                }
            }
            else if (phase == "done")
            {
                if (t >= phaseAt + DONE_SECS)
                {
                    // the box opens; the opponent is back on its feet at its end, waiting for a rematch
                    Reset(f, SPAR_START.them);
                    phase = "waiting"; phaseAt = t;
                    needExit = me != null && Inside(me.pos);
                }
            }
            if (Sealed) Walls();
        }

        /// <summary>the sealed box: the two of you (and what you summoned) can't leave it, nobody else can come in</summary>
        void Walls()
        {
            double R0(Actor a) => a.Radius + 0.05;
            foreach (var a in w.actors)
            {
                if (!a.alive) continue;
                double loX = ARENA.x - ARENA.hx, loZ = ARENA.z - ARENA.hz, hiX = ARENA.x + ARENA.hx, hiZ = ARENA.z + ARENA.hz;
                if (Duelist(a))
                {
                    double r = R0(a);
                    if (a.pos.x < loX + r) { a.pos.x = loX + r; if (a.vel.x < 0) a.vel.x = 0; }
                    if (a.pos.x > hiX - r) { a.pos.x = hiX - r; if (a.vel.x > 0) a.vel.x = 0; }
                    if (a.pos.z < loZ + r) { a.pos.z = loZ + r; if (a.vel.z < 0) a.vel.z = 0; }
                    if (a.pos.z > hiZ - r) { a.pos.z = hiZ - r; if (a.vel.z > 0) a.vel.z = 0; }
                    double top = ARENA.h - a.Height;
                    if (a.pos.y > top) { a.pos.y = top; if (a.vel.y > 0) a.vel.y = 0; }
                }
                else if (Inside(a.pos, a.Radius))
                {
                    // pushed out through the nearest wall
                    double r = R0(a), dx = a.pos.x - ARENA.x, dz = a.pos.z - ARENA.z;
                    double ex = ARENA.hx + r - Math.Abs(dx), ez = ARENA.hz + r - Math.Abs(dz);
                    if (ex < ez) a.pos.x = ARENA.x + Math.Sign(dx != 0 ? dx : 1) * (ARENA.hx + r); else a.pos.z = ARENA.z + Math.Sign(dz != 0 ? dz : 1) * (ARENA.hz + r);
                }
            }
        }

        /// <summary>while the box is up nothing crosses it, and the two of you only hurt each other between FIGHT and the kill</summary>
        bool Allow(Actor src, Actor tgt)
        {
            if (!Sealed || src == null) return true;
            bool sIn = Duelist(src) || Inside(src.pos), tIn = Duelist(tgt) || Inside(tgt.pos);
            if (sIn != tIn) return false;
            return !sIn || phase == "fight";
        }

        public void Observe(SimEvent e)
        {
            var me = Player; var f = foe;
            if (f == null || me == null) return;
            if (e is DmgEvent d && !d.heal && phase == "fight")
            {
                var src = d.src != null ? (d.src.owner ?? d.src) : null;
                if (d.tgt == f && src == me) dealt += d.amt;
                else if (d.tgt == me && src == f) taken += d.amt;
            }
            if (!(e is KillEvent k) || (k.tgt != me && k.tgt != f)) return;
            if (!Sealed) return;
            // the spar brings the fallen back for the next round (not the world's respawn at the team spawn)
            k.tgt.respawnAt = 0;
            if (phase == "fight")
            {
                string winner = k.tgt == f ? "you" : "them";
                wins[winner]++;
                roundWinner = winner;
                rounds.Add(new SparRound { winner = winner, secs = w.time - roundAt, dealt = dealt, taken = taken });
                phase = "roundover"; phaseAt = w.time;
            }
            else if (phase == "roundover" && phaseAt == w.time && roundWinner != null && roundWinner != "draw")
            {
                // both fell on the same tick: nobody takes the round
                wins[roundWinner]--;
                roundWinner = "draw";
                rounds[rounds.Count - 1].winner = "draw";
            }
        }
    }

    /// <summary>the opponent's controller: the hero AI while the round is on, still and facing you otherwise</summary>
    public class SparBrain : IController
    {
        public readonly Spar s; public readonly Actor a;
        public Bot bot;
        public SparBrain(Spar s, Actor a) { this.s = s; this.a = a; bot = new Bot(s.w, a, s.nav, Spar.SPAR_SKILL[s.opts.diff]); }
        public void Think(double dt)
        {
            var i = a.input; var me = s.Player;
            if (s.phase == "fight") { bot.Think(dt); return; }
            i.fire = i.alt = i.melee = i.a1 = i.a2 = i.ult = i.jump = i.jumpHeld = i.reload = i.swoop = i.descend = false;
            i.mx = i.mz = 0;
            if (me != null && me.alive && (s.phase != "waiting" || s.Inside(me.pos, 12)))
            {
                V3 c = me.Center, e = a.Eye;
                i.yaw = Math.Atan2(c.x - e.x, c.z - e.z); i.pitch = 0;
            }
        }
    }
}
