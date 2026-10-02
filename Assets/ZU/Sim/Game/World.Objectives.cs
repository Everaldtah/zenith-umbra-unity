// The Grand Dohyo ring wall, body separation, the capture point (legacy + Overwatch Control rounds) and the match end.
// Port of zenith-umbra src/game/World.ts (ringClamp .. end).
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZU.Sim
{
    public partial class World
    {
        /// <summary>Grand Dohyo: enemies caught in the ring can't step out; the rest can't step in (allies pass freely)</summary>
        void RingClamp(Actor a)
        {
            foreach (var z in zones)
            {
                if (z.kind != "dohyo" || a.team == z.team || !a.alive) continue;
                if (a.pos.y < z.y - 2 || a.pos.y > z.y + RING_H) continue;
                double dx = a.pos.x - z.x, dz = a.pos.z - z.z, d = M.Hypot(dx, dz); if (d == 0) d = 1e-3;
                var trapped = z.data != null && z.data.TryGetValue("trapped", out var tr) ? tr as List<int> : null;
                bool inside = trapped != null && trapped.Contains(a.id);
                if (inside && a.Has("tempo", time) && a.Sv("speed", 1) >= 1.5)
                {
                    // COUNTER (Hibiki): an amped Tempo Rush carries trapped heroes straight through the rope wall
                    trapped.Remove(a.id);
                    var h = actors.FirstOrDefault(x => x.def.id == "hibiki" && x.team == a.team && x.alive);
                    if (h != null && !(z.data.TryGetValue("broke", out var br) && Convert.ToDouble(br) != 0)) { z.data["broke"] = 1.0; Counter(h, z.owner, "Tempo Rush breaks out of the Grand Dohyo"); }
                    continue;
                }
                double lim = inside ? z.r - a.Radius : z.r + a.Radius;
                if (inside ? d <= lim : d >= lim) continue;
                a.pos.x = z.x + dx / d * lim; a.pos.z = z.z + dz / d * lim;
                double outv = (a.vel.x * dx + a.vel.z * dz) / d;          // velocity across the wall
                if (inside ? outv > 0 : outv < 0) { a.vel.x -= dx / d * outv; a.vel.z -= dz / d * outv; }
                level.Collide(ref a.pos, a.ColRadius, a.ColHeight);
            }
        }

        void Separate()
        {
            // (Tomoe in her Crescent Warpath passes through bodies like a ghost)
            var list = actors.Where(a => a.alive && !a.Has("phased", time) && a.forced?.kind != "tide").ToList();
            for (int i = 0; i < list.Count; i++) for (int j = i + 1; j < list.Count; j++)
                {
                    Actor a = list[i], b = list[j];
                    if (a.pos.y > b.pos.y + b.Height || b.pos.y > a.pos.y + a.Height) continue;
                    double dx = b.pos.x - a.pos.x, dz = b.pos.z - a.pos.z, rr = (a.Radius + b.Radius) * 0.85, d2 = dx * dx + dz * dz;
                    if (d2 >= rr * rr) continue;
                    double d = Math.Sqrt(d2); if (d == 0) d = 0.01; double push = rr - d;
                    double ma = a.def.frame == "mech" ? 3 : 1, mb = b.def.frame == "mech" ? 3 : 1;
                    double ka = mb / (ma + mb), kb = ma / (ma + mb);
                    a.pos.x -= dx / d * push * ka; a.pos.z -= dz / d * push * ka;
                    b.pos.x += dx / d * push * kb; b.pos.z += dz / d * push * kb;
                    level.Collide(ref a.pos, a.ColRadius, a.ColHeight); level.Collide(ref b.pos, b.ColRadius, b.ColHeight);
                }
        }

        static string Other(string team) => team == "zenith" ? "umbra" : "zenith";
        static string TeamTitle(string team) => team == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE";
        static string TeamColor(string team) => team == "zenith" ? "#5cc8ff" : "#ff3b5c";

        Dictionary<string, int> OnPoint(bool countTime, double dt)
        {
            double px = map.point[0], py = map.point[1], pz = map.point[2];
            var on = new Dictionary<string, int> { ["zenith"] = 0, ["umbra"] = 0 };
            foreach (var a in actors)
                if (a.alive && !a.isRobot && M.Hypot(a.pos.x - px, a.pos.z - pz) < point.r && a.pos.y > py - 1 && a.pos.y < py + 5) { on[a.team]++; if (countTime) a.objTime += dt; }
            return on;
        }

        // ------------------------------------------------------------------ capture point
        void UpdatePoint(double dt)
        {
            var P = point; double t = time;
            if (winner != null) return;
            if (rules == "control") { UpdateControl(dt); return; }
            if (t < P.unlockAt) return;
            if (t - dt < P.unlockAt) { Msg("THE POINT IS OPEN"); Sfx("announce"); }
            var on = OnPoint(false, dt);
            P.contested = on["zenith"] > 0 && on["umbra"] > 0;
            string solo = P.contested ? null : on["zenith"] > 0 ? "zenith" : on["umbra"] > 0 ? "umbra" : null;
            if (solo != null && solo != P.owner)
            {
                if (P.capTeam != solo) { P.capture = Math.Max(0, P.capture - dt * 25); if (P.capture == 0) P.capTeam = solo; }
                else P.capture = Math.Min(100, P.capture + dt * (12 + 4 * Math.Min(3, on[solo])));
                if (P.capture >= 100)
                {
                    P.owner = solo; P.capture = 0; P.capTeam = null;
                    Msg($"{TeamTitle(solo)} TOOK THE POINT", TeamColor(solo));
                    Sfx("capture");
                }
            }
            else if (solo == null && !P.contested) P.capture = Math.Max(0, P.capture - dt * 8);
            if (P.owner != null && !P.contested)
            {
                P.progress[P.owner] = Math.Min(100, P.progress[P.owner] + dt * (mode == "aitest" ? 4 : 1.6));
                if (P.progress[P.owner] >= 100 && !(on[Other(P.owner)] > 0)) End(P.owner);
            }
            if (t > timeLimit) End(P.progress["zenith"] >= P.progress["umbra"] ? "zenith" : "umbra");
        }

        /// <summary>Overwatch Control: rounds to ROUNDS_TO_WIN, overtime at 99% while an enemy stands on the point.</summary>
        void UpdateControl(double dt)
        {
            var P = point; var C = control; double t = time;
            if (C.phase == "intermission")
            {
                if (t >= C.phaseEnd)
                {
                    C.round++; C.phase = "fight"; C.overtime = false;
                    P.owner = null; P.capture = 0; P.capTeam = null; P.progress = new Dictionary<string, double> { ["zenith"] = 0, ["umbra"] = 0 }; P.contested = false;
                    P.unlockAt = t + 10;
                    Msg($"ROUND {C.round}"); Sfx("announce");
                }
                return;
            }
            if (t < P.unlockAt) return;
            if (t - dt < P.unlockAt) { Msg("THE POINT IS OPEN"); Sfx("announce"); }
            var on = OnPoint(true, dt);
            P.contested = on["zenith"] > 0 && on["umbra"] > 0;
            string solo = P.contested ? null : on["zenith"] > 0 ? "zenith" : on["umbra"] > 0 ? "umbra" : null;
            if (solo != null && solo != P.owner)
            {
                if (P.capTeam != solo) { P.capture = Math.Max(0, P.capture - dt * 25); if (P.capture == 0) P.capTeam = solo; }
                else P.capture = Math.Min(100, P.capture + dt * 11 * (1 + 0.5 * (Math.Min(3, on[solo]) - 1)));
                if (P.capture >= 100)
                {
                    P.owner = solo; P.capture = 0; P.capTeam = null;
                    Msg($"{TeamTitle(solo)} TOOK THE POINT", TeamColor(solo));
                    Sfx("capture");
                }
            }
            else if (solo == null && !P.contested) P.capture = Math.Max(0, P.capture - dt * 8);
            if (P.owner != null)
            {
                string foe = Other(P.owner);
                if (!P.contested) P.progress[P.owner] = Math.Min(100, P.progress[P.owner] + dt * HOLD_RATE);
                // overtime: the round can't be won while an enemy stands on the point
                C.overtime = P.progress[P.owner] >= 99 && on[foe] > 0;
                if (C.overtime) P.progress[P.owner] = Math.Min(P.progress[P.owner], 99);
                if (P.progress[P.owner] >= 100)
                {
                    string w = P.owner;
                    C.wins[w]++;
                    Msg($"{TeamTitle(w)} WINS ROUND {C.round}", TeamColor(w));
                    Sfx("capture");
                    if (C.wins[w] >= ROUNDS_TO_WIN) { End(w); return; }
                    C.phase = "intermission"; C.phaseEnd = t + 7;
                    // everyone back to spawn for the next round (ult charge is kept)
                    After(3, () => { foreach (var a in actors.ToList()) if (!a.isRobot) { double u = a.ult; Respawn(a, true); a.ult = u; } });
                }
            }
            if (t > timeLimit) End(C.wins["zenith"] != C.wins["umbra"] ? (C.wins["zenith"] > C.wins["umbra"] ? "zenith" : "umbra") : P.progress["zenith"] >= P.progress["umbra"] ? "zenith" : "umbra");
        }

        public void End(string w)
        {
            if (winner != null) return;
            winner = w;
            Msg(w == "zenith" ? "ZENITH VANGUARD WINS" : "UMBRA SYNDICATE WINS", TeamColor(w));
            Sfx("victory");
        }
    }
}
