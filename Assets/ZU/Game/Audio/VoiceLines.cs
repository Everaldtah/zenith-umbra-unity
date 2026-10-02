// Hero voice lines (TS audio/Voice.ts, ported), after Overwatch's dialog system (GDC 2016, "Play by Sound"): every line
// is the answer to a STIMULUS - a jump, a hit, a death, an ultimate - and the stimulus decides its CATEGORY (priority)
// and who HEARS it:
//
//   category   priority  examples                                  broadcast
//   critical   5         ultimate, "COME HERE!"                    enemies + self hear the ult line, allies their own line
//   death      4         death cry                                 everyone nearby
//   pain       3         hit grunts, burning                       the hero and whoever hurt them (the "involved")
//   chatter    2         kills, callouts, thanks, low health       the hero's team (kills: the killer)
//   exert      1         jump / landing efforts                    the player only
//
// One line per hero at a time (a higher category cuts a lower one off), at most three heroes talking at once, per-line
// cooldowns so nobody repeats themselves, and every line lands in 3D from the hero's head. Mech pilots speak over the
// cockpit radio. The announcer sits above it all. Each line that starts raises OnLine (the subtitles).
using System;
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Audio
{
    public static class VoiceLines
    {
        static readonly Dictionary<string, int> PRI = new Dictionary<string, int> { ["critical"] = 5, ["death"] = 4, ["pain"] = 3, ["chatter"] = 2, ["exert"] = 1 };
        /// <summary>minimum seconds between two lines of the same key from the same hero</summary>
        static readonly Dictionary<string, float> CD = new Dictionary<string, float>
        {
            ["pain"] = 1.1f, ["pain_big"] = 2.5f, ["jump"] = 2.2f, ["land"] = 2.5f, ["burn"] = 6, ["kill"] = 4, ["low_hp"] = 12, ["thanks"] = 14, ["reload"] = 12, ["a1"] = 5, ["a2"] = 5, ["alt"] = 5,
            ["heal_track"] = 3, ["speed_track"] = 3, ["swoop"] = 5, ["grind"] = 14, ["contest"] = 20, ["attack_point"] = 30, ["defend_point"] = 30, ["push"] = 30, ["ult_ready"] = 40,
        };

        sealed class Talk { public AudioKit.LineHandle h; public int pri; public float until; }
        static readonly Dictionary<int, Talk> talking = new Dictionary<int, Talk>();
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();
        static Talk announcer;
        static float Now => Time.unscaledTime;          // (TS: performance.now - real time, not the match clock)

        /// <summary>the listener's hero (null = spectating)</summary>
        public static Actor Me;
        /// <summary>subtitles: (speaker name, colour, words, category, seconds) - the HUD decides what to show</summary>
        public static event Action<string, string, string, string, float> OnLine;

        public static void Reset()
        {
            foreach (var t in talking.Values) t.h?.Stop();
            talking.Clear(); last.Clear();
            announcer?.h?.Stop(); announcer = null;
        }

        /// <summary>the hero whose bank speaks for this actor (Tenkai-Oh -> Haruto, Gorgoth -> Vorn)</summary>
        static string BankOf(Actor a) => AudioKit.VoiceOf(a.def.id);

        /// <summary>
        /// a hero says `key` if the listener is in the audience for `cat`; `involved` narrows pain to the involved (and a kill
        /// line to the killer and the killed); allies hear a critical line as `allyKey`. Returns true if a line started.
        /// </summary>
        public static bool Say(Actor a, string key, string cat, Actor involved = null, string allyKey = null)
        {
            if (a == null) return false;
            var me = Me;
            bool self = me != null && a == me, ally = me != null && a.team == me.team && !self, enemy = me != null && a.team != me.team;
            // who hears it
            if (cat == "exert" && !self) return false;
            if (cat == "pain" && !self && involved != me) return false;
            if (cat == "chatter" && enemy && key != "kill") return false;
            if (cat == "chatter" && key == "kill" && !self && involved != me) return false;
            // allies hear an ult in their own words
            if (cat == "critical" && ally && allyKey != null) key = allyKey;
            var bank = BankOf(a);
            if (!AudioKit.HasLine(bank, key)) return false;
            float t = Now; string lk = $"{a.id}:{key}";
            if (t - (last.TryGetValue(lk, out var lt) ? lt : -99) < (CD.TryGetValue(key, out var cd) ? cd : 2)) return false;
            int pri = PRI[cat];
            // one line per hero; a higher category cuts a lower one off
            if (talking.TryGetValue(a.id, out var cur) && cur.until > t) { if (cur.pri >= pri && cat != "death") return false; cur.h?.Stop(); }
            // three heroes at once at most (critical and death always get through)
            int liveN = 0; foreach (var x in talking.Values) if (x.until > t) liveN++;
            if (liveN >= 3 && pri < 4) return false;
            var clip = AudioKit.PickLine(bank, key, out int take);
            if (clip == null) return false;
            var pos = Conv.U(a.pos) + Vector3.up * (float)(a.Height * 0.9);
            bool radio = a.def.frame == "mech" && a.def.pilot != null;
            var rel = self ? Rel.Self : ally ? Rel.Ally : Rel.Enemy;
            var played = AudioKit.PlayLine(clip, self ? (Vector3?)null : pos, cat == "critical" ? 1.15f : cat == "exert" ? 0.7f : 1, rel, radio, cat == "critical" && enemy);
            if (played == null) return false;
            var words = AudioKit.Words(bank, key, take);
            if (!string.IsNullOrEmpty(words)) OnLine?.Invoke(a.def.name, a.def.color, words, cat, played.dur);
            last[lk] = t;
            talking[a.id] = new Talk { h = played, pri = pri, until = t + played.dur };
            if (cat == "critical" || cat == "death" || self) AudioKit.Duck(cat == "critical" ? 0.45f : 0.25f, played.dur);
            return true;
        }

        /// <summary>the arena announcer (2D, over everything)</summary>
        public static void Announce(string key)
        {
            if (!AudioKit.HasLine("announcer", key)) return;
            float t = Now; string lk = $"ann:{key}";
            if (t - (last.TryGetValue(lk, out var lt) ? lt : -99) < 4) return;
            last[lk] = t;
            var clip = AudioKit.PickLine("announcer", key, out int take);
            if (clip == null) return;
            announcer?.h?.Stop();
            var p = AudioKit.PlayLine(clip, null, 1.05f, Rel.Self, announcer: true);
            if (p == null) return;
            announcer = new Talk { h = p, pri = 9, until = t + p.dur };
            AudioKit.Duck(0.35f, p.dur);
            var words = AudioKit.Words("announcer", key, take);
            if (!string.IsNullOrEmpty(words)) OnLine?.Invoke("", "#ffffff", words, "announcer", p.dur);
        }
    }
}
