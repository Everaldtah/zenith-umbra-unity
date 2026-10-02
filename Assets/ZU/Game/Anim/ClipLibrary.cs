// The animation clip library (port of ClipLibrary.ts): the imported humanoid clips (Quaternius UAL 1 & 2, Mixamo, CMU,
// Kevin Iglesias, Tripo text-to-motion; Assets/ZU/Art/Anim) sorted into gameplay slots, as the TS sorts its PoseClips.
//
// Resources/ZUAnim/manifest.json (the TS public/anim/manifest.json):
//   slots: pin clips to a slot by name; exclude: never use these; heroes: a hero's own set for a slot; casts: ability id ->
//   clip (and the seconds it should take); trim: hand-picked active windows.
// Slots come from clip names (tolerant of every pack's naming), except locomotion: walk / jog / run / sprint clips are placed
// in an 8-way blend space by the direction and speed they ACTUALLY travel (measured from the feet by `zu_bake_clips`:
// ZuClipSet), and missing directions are filled by mirroring (left <-> right, ClipMirror) and time-reversal (forward ->
// backward). One-shots are cut to their active window (trimAction).
//
// A Clip here is the TS PoseClip's role without the baked poses: the AnimationClip, the frame window it plays, its
// mirror / reverse flags, and the measurements (speed, travel, contacts, phase anchor) carried over from the bake.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZU.Game.Anim
{
    public sealed class ClipLibrary
    {
        public enum Slot { idle, jump_start, jump_loop, land, death, hit, punch, melee, roll, dash, slide, vault, flip, cast, @throw, stun, shoot, block, reload }
        static readonly string[] GAIT_ORDER = { "walk", "jog", "run", "sprint" };
        /// <summary>slots whose clips play as loops (everything else is a one-shot and gets trimmed)</summary>
        static readonly HashSet<Slot> LOOPED = new HashSet<Slot> { Slot.idle, Slot.jump_loop, Slot.stun };
        /// <summary>one-shots that are a gesture into a key pose (trimmed to rise + short hold), not continuous full-body travel</summary>
        static readonly HashSet<Slot> KEYPOSE = new HashSet<Slot> { Slot.cast, Slot.@throw, Slot.punch, Slot.block, Slot.hit, Slot.shoot };
        // variants that aren't the plain, unarmed, healthy movement set
        static readonly Regex VARIANT = new Regex(" (crouch|sneak|swim|carry|injur|limp|zombie|drunk|sit|lie|lying|prone|crawl|ladder|climb|pistol|rifle|gun|sword|bow|spear|shield|torch|push|pull|wheel|drive|ride|dance|talk|wave|cheer|clap|emote|interact|pick|fish|farm|chop|mine|sad|happy|angry|tired|old|female|feminine|masc|turn|start|stop|to|dodge|roll|dash|flip|jump|slide|vault)");

        static string Words(string n) => " " + Regex.Replace(Regex.Replace(n, "([a-z])([A-Z])", "$1 $2"), "[^a-zA-Z0-9]+", " ").ToLowerInvariant() + " ";

        /// <summary>the gameplay slot a clip belongs to, from its name (null: locomotion or unused)</summary>
        public static Slot? SlotOf(string name, bool loop)
        {
            string w = Words(name);
            bool Has(string re) => Regex.IsMatch(w, re);
            if (Has(" (death|dying|die|dies|dead|killed) ") || Has("death")) return Has("idle|pose") && loop ? (Slot?)null : Slot.death;
            if (Has(" (hit|hurt|react|reaction|flinch|damage|damaged|impact) ") && !Has("attack|combo|punch|kick|slash|strike")) return Slot.hit;
            if (Has(" (stun|stunned|dizzy|stagger|knocked) ")) return Slot.stun;
            if (Has(" (land|landing) ")) return Slot.land;
            if (Has(" jump ") || Has(" (fall|falling|airborne|air) "))
            {
                if (Has(" (loop|idle|air|mid|fall|falling|airborne) ") || loop) return Slot.jump_loop;
                return Slot.jump_start;
            }
            if (Has(" (roll|dodge|evade|tumble) ")) return Slot.roll;
            if (Has(" (vault|mantle|hurdle|parkour|wallrun|wall run|ledge) ")) return Has(" flip ") ? Slot.flip : Slot.vault;
            if (Has(" (flip|backflip|frontflip|somersault|cartwheel) ")) return Slot.flip;
            if (Has(" (slide|sliding) ")) return Slot.slide;
            if (Has(" (dash|lunge) ") && !Has("attack|slash")) return Slot.dash;
            if (Has(" (reload|reloading) ")) return Slot.reload;
            if (Has(" (block|guard|parry) ")) return Slot.block;
            if (Has(" (throw|toss|grenade) ")) return Slot.@throw;
            if (Has(" (punch|jab|hook|uppercut|kick|cross|elbow|knee strike) ")) return Slot.punch;
            if (Has(" (sword|melee|slash|katana|combo|attack|strike|swing|stab|blade|axe|chop|thrust|cleave|hit \\d) ")) return Slot.melee;
            if (Has(" (spell|cast|casting|magic|buff|summon|channel|shout|roar|power) ")) return Slot.cast;
            if (Has(" (shoot|shooting|fire|firing|aim) ")) return Slot.shoot;
            if (Has(" idle ") && loop && !Has("crouch|sit|lie|swim|pistol|rifle|gun|sword|bow|spell|talk|dance|sad|injur|tired|wounded")) return Slot.idle;
            return null;
        }

        static string GaitOf(Clip c)
        {
            string w = Words(c.name);
            if (!c.loop || c.speed < 0.2f || VARIANT.IsMatch(w)) return null;
            foreach (var g in GAIT_ORDER) if (w.Contains(" " + g)) return g;
            if (Regex.IsMatch(w, " (strafe|move|locomotion|forward|fwd|backward|bwd|back|left|right) ")) return "walk";
            return null;
        }

        // ---------------------------------------------------------------- a clip: a window of an AnimationClip, maybe mirrored / reversed
        public sealed class Clip
        {
            public ZuClipSet.Info src;
            public string name;
            public AnimationClip clip => src.clip;
            public bool loop, mirror, reverse;
            public float speed; public Vector2 travel; public float phase0;
            /// <summary>the source frames this plays [f0, f1] (a trimmed one-shot is a window of its clip)</summary>
            public int f0, f1;
            public float fps => src.fps;
            public int frames => f1 - f0 + 1;
            public float duration => (f1 - f0) / Mathf.Max(1e-3f, fps);

            /// <summary>clip time -> its own frame position (loops wrap, one-shots clamp), 0..frames-1</summary>
            public float FrameAt(float t)
            {
                float x = t / Mathf.Max(1e-4f, duration);
                x = loop ? x - Mathf.Floor(x) : Mathf.Clamp01(x);
                return x * (frames - 1);
            }
            /// <summary>clip time -> the source AnimationClip's time</summary>
            public float SourceTime(float t)
            {
                float fx = FrameAt(t);
                return (f0 + (reverse ? frames - 1 - fx : fx)) / Mathf.Max(1e-3f, fps);
            }
            /// <summary>the feet planted at this clip time: bit 0 left, bit 1 right</summary>
            public int ContactAt(float t)
            {
                int f = Mathf.RoundToInt(FrameAt(t));
                int sf = f0 + (reverse ? frames - 1 - f : f);
                int c = src.contact != null && sf < src.contact.Length ? src.contact[Mathf.Clamp(sf, 0, src.contact.Length - 1)] - '0' : 0;
                return mirror ? ((c & 1) << 1) | ((c & 2) >> 1) : c;
            }
            public Clip Copy() => (Clip)MemberwiseClone();
        }

        // ---------------------------------------------------------------- derived clips (fill 8-way coverage)
        static float Phase0Of(Clip c)
        {
            for (int f = 1; f < c.frames; f++)
            {
                float t0 = c.duration * (f - 1) / Mathf.Max(1, c.frames - 1), t1 = c.duration * f / Mathf.Max(1, c.frames - 1);
                if ((c.ContactAt(t1) & 1) != 0 && (c.ContactAt(t0) & 1) == 0) return (float)f / (c.frames - 1);
            }
            return 0;
        }
        /// <summary>left <-> right mirror (ClipMirror plays it mirrored): the feet swap, the travel reflects</summary>
        public static Clip MirrorClip(Clip c)
        {
            var m = c.Copy(); m.name = c.name + " (mirror)"; m.mirror = !c.mirror; m.travel = new Vector2(-c.travel.x, c.travel.y);
            m.phase0 = Phase0Of(m);
            return m;
        }
        /// <summary>time reversal: a forward walk played backwards is a convincing backpedal</summary>
        public static Clip ReverseClip(Clip c)
        {
            var r = c.Copy(); r.name = c.name + " (reverse)"; r.reverse = !c.reverse; r.travel = -c.travel;
            r.phase0 = Phase0Of(r);
            return r;
        }

        // ---------------------------------------------------------------- one-shot trimming (gameplay-first timing)
        /// <summary>frames [f0, f1] of a clip as its own one-shot</summary>
        public static Clip SubClip(Clip c, float f0, float f1)
        {
            int a = Mathf.Max(0, Mathf.Min(c.frames - 2, Mathf.RoundToInt(f0))), b = Mathf.Max(a + 1, Mathf.Min(c.frames - 1, Mathf.RoundToInt(f1)));
            if (a == 0 && b == c.frames - 1 && !c.loop) return c;
            var s = c.Copy(); s.f0 = c.f0 + a; s.f1 = c.f0 + b; s.loop = false; s.phase0 = 0;
            return s;
        }

        /// <summary>
        /// Cut a one-shot to the part that matters in game (TS trimAction). Mocap and library clips start and end in idle; played
        /// whole and time-squeezed, the gesture turns into a blur. The active window is where the (smoothed) motion rises above
        /// 20% of its peak. Player-triggered moves keep only ~2 frames of lead-in (Overwatch rule: no anticipation on player
        /// actions). Jumps start at the bottom of the crouch; deaths keep their final pose; key-pose gestures keep the rise and
        /// a short hold (the walk back to idle is the layer's blend-out).
        /// </summary>
        public static Clip TrimAction(Clip c, Slot slot, float[] manual = null)
        {
            if (manual != null && manual.Length >= 2) return SubClip(c, manual[0] * c.fps, manual[1] * c.fps);
            int n = c.frames;
            float[] e = c.src.act ?? new float[0];
            float A(int f) { int sf = c.f0 + f; return sf >= 0 && sf < e.Length ? e[sf] : 0; }
            var es = new float[n]; float peak = 0;
            for (int f = 0; f < n; f++)
            {
                float sum = 0; int k = 0;
                for (int j = Mathf.Max(1, f - 2); j <= Mathf.Min(n - 1, f + 2); j++) { sum += A(j); k++; }
                es[f] = k > 0 ? sum / k : 0; peak = Mathf.Max(peak, es[f]);
            }
            if (peak < 1e-3f) return SubClip(c, 0, n - 1);
            int a = 0, b = n - 1;
            while (a < n - 1 && es[a] < peak * 0.2f) a++;
            while (b > a && es[b] < peak * 0.2f) b--;
            int lead = slot == Slot.hit || slot == Slot.death ? 0 : Mathf.RoundToInt(c.fps * 0.07f);
            int f0 = Mathf.Max(0, a - lead), f1 = Mathf.Min(n - 1, b + Mathf.RoundToInt(c.fps * 0.12f));
            if (slot == Slot.jump_start && c.src.hipsY != null)
            {
                // take-off: from the bottom of the crouch (the lowest hips before the jump's peak)
                float hy(int f) => c.src.hipsY[Mathf.Clamp(c.f0 + f, 0, c.src.hipsY.Length - 1)];
                int lo = 0; for (int f = 0; f < Mathf.FloorToInt(n * 0.8f); f++) if (hy(f) < hy(lo)) lo = f;
                f0 = Mathf.Max(0, lo - 1);
            }
            if (KEYPOSE.Contains(slot) && c.src.dev != null)
            {
                // gestures (casts, throws, strikes, reactions): mocap rises into a key pose, holds it, and walks back to idle. Keep
                // the rise and a short hold; the walk-back is replaced by the layer's blend-out (the recovery is interruptible)
                float D0 = c.src.dev[Mathf.Clamp(c.f0, 0, c.src.dev.Length - 1)];
                var dev = new float[n]; float md = 0;
                // (the bake measures the deviation from the clip's frame 0; this window's own frame 0 is subtracted in angle
                // space only approximately - exact for an untrimmed clip, the TS's case)
                for (int f = 0; f < n; f++) { dev[f] = Mathf.Abs(c.src.dev[Mathf.Clamp(c.f0 + f, 0, c.src.dev.Length - 1)] - D0); md = Mathf.Max(md, dev[f]); }
                if (md > 0.05f)
                {
                    int s0 = 0; while (s0 < n - 1 && dev[s0] < md * 0.25f) s0++;
                    int k = s0; while (k < n - 1 && dev[k] < md * 0.85f) k++;
                    int en = k; while (en < n - 1 && dev[en + 1] >= md * 0.85f && en - k < c.fps * 0.25f) en++;
                    f0 = Mathf.Max(0, s0 - lead); f1 = Mathf.Min(n - 1, en + Mathf.RoundToInt(c.fps * 0.08f));
                }
            }
            if (slot == Slot.death) f1 = n - 1;
            return SubClip(c, f0, f1);
        }

        static float AngleOf(Clip c) => Mathf.Atan2(c.travel.x, c.travel.y);
        static float AngDiff(float a, float b) { float d = a - b; while (d > Mathf.PI) d -= 2 * Mathf.PI; while (d < -Mathf.PI) d += 2 * Mathf.PI; return d; }

        // ---------------------------------------------------------------- library
        public sealed class Gait { public string name; public float speed; public List<Clip> clips; }
        public readonly Dictionary<Slot, List<Clip>> slots = new Dictionary<Slot, List<Clip>>();
        public readonly List<Gait> gaits = new List<Gait>();
        /// <summary>per-ability clips (manifest "casts"), already trimmed: ability id -> clip + seconds it should take</summary>
        public readonly Dictionary<string, (Clip clip, float? target)> casts = new Dictionary<string, (Clip, float?)>();
        /// <summary>per-hero slot overrides (manifest "heroes"), trimmed</summary>
        public readonly Dictionary<string, Dictionary<Slot, List<Clip>>> heroSlots = new Dictionary<string, Dictionary<Slot, List<Clip>>>();
        public readonly List<Clip> clips = new List<Clip>();

        static ClipLibrary cached; static bool tried;
        /// <summary>the library from the baked clip set and the manifest (null without a bake: the Mecanim controller stays)</summary>
        public static ClipLibrary Get()
        {
            if (cached != null || tried) return cached;
            tried = true;
            var set = ZuClipSet.Get();
            var mf = Resources.Load<TextAsset>("ZUAnim/manifest");
            if (set == null || set.clips.Count == 0) return null;
            try { cached = new ClipLibrary(set, mf != null ? JObject.Parse(mf.text) : new JObject()); }
            catch (Exception ex) { Debug.LogWarning("[ZU] clip library: " + ex.Message); cached = null; }
            return cached;
        }

        static Slot ParseSlot(string s) => (Slot)Enum.Parse(typeof(Slot), s == "throw" ? "throw" : s);

        public ClipLibrary(ZuClipSet set, JObject m)
        {
            foreach (var i in set.clips)
                if (i != null && i.clip != null && i.Frames >= 2)
                    clips.Add(new Clip { src = i, name = i.name, loop = i.loop, speed = i.speed, travel = i.travel, phase0 = i.phase0, f0 = 0, f1 = i.Frames - 1 });
            Clip Find(string n) => clips.FirstOrDefault(c => c.name == n);
            var excluded = new HashSet<string>((m["exclude"] as JArray)?.Select(x => (string)x) ?? Enumerable.Empty<string>());
            var slotsJ = m["slots"] as JObject;
            var pinned = new HashSet<string>(slotsJ?.Properties().SelectMany(p => (p.Value as JArray)?.Select(x => (string)x) ?? Enumerable.Empty<string>()) ?? Enumerable.Empty<string>());
            var trim = m["trim"] as JObject;
            float[] Trim(string n) => trim?[n] is JArray a && a.Count >= 2 ? new[] { (float)a[0], (float)a[1] } : null;
            var byGait = new Dictionary<string, List<Clip>>();
            foreach (var c in clips)
            {
                if (excluded.Contains(c.name) || pinned.Contains(c.name)) continue;
                var g = GaitOf(c);
                if (g != null) { if (!byGait.TryGetValue(g, out var l)) byGait[g] = l = new List<Clip>(); l.Add(c); continue; }
                var s = SlotOf(c.name, c.loop);
                if (s.HasValue) Add(s.Value, c);
            }
            if (slotsJ != null)
                foreach (var p in slotsJ.Properties())
                    slots[ParseSlot(p.Name)] = ((p.Value as JArray)?.Select(x => Find((string)x)).Where(c => c != null).ToList()) ?? new List<Clip>();
            // plain names first ("Idle" before "Idle_Talking"); combos keep their authored hit order
            foreach (var s in slots.Keys.ToList())
            {
                if (slotsJ?[s.ToString()] != null) continue;
                var list = slots[s];
                if (s == Slot.melee || s == Slot.punch) list.Sort((a, b) => NaturalCompare(a.name, b.name));
                else list.Sort((a, b) => a.name.Length != b.name.Length ? a.name.Length.CompareTo(b.name.Length) : string.CompareOrdinal(a.name, b.name));
            }
            // one-shots: cut to their active window (idle, stun and the air loop stay whole loops)
            foreach (var s in slots.Keys.ToList()) if (!LOOPED.Contains(s)) slots[s] = slots[s].Select(c => TrimAction(c, s, Trim(c.name))).ToList();
            if (m["heroes"] is JObject heroes)
                foreach (var h in heroes.Properties())
                {
                    var mm = new Dictionary<Slot, List<Clip>>();
                    foreach (var p in ((JObject)h.Value).Properties())
                    {
                        var s = ParseSlot(p.Name);
                        var l = ((p.Value as JArray)?.Select(x => Find((string)x)).Where(c => c != null).ToList()) ?? new List<Clip>();
                        if (l.Count > 0) mm[s] = LOOPED.Contains(s) ? l : l.Select(c => TrimAction(c, s, Trim(c.name))).ToList();
                    }
                    heroSlots[h.Name] = mm;
                }
            if (m["casts"] is JObject castsJ)
                foreach (var p in castsJ.Properties())
                {
                    string name; float? target = null;
                    if (p.Value is JArray a) { name = (string)a[0]; if (a.Count > 1) target = (float)a[1]; } else name = (string)p.Value;
                    var c = Find(name);
                    if (c != null) casts[p.Name] = (TrimAction(c, Slot.cast, Trim(name)), target);
                    else Debug.LogWarning($"[ZU] anim: cast clip not found {p.Name} {name}");
                }
            foreach (var kv in byGait)
            {
                var set2 = new List<Clip>(kv.Value);
                // backpedal from the forward clip, strafes from their mirror image
                bool Covered(float a) => set2.Any(c => Mathf.Abs(AngDiff(AngleOf(c), a)) < 0.35f);
                foreach (var c in set2.ToList()) { var mm = MirrorClip(c); if (!Covered(AngleOf(mm))) set2.Add(mm); }
                var fwd = set2.Where(c => Mathf.Abs(AngleOf(c)) < 0.35f).OrderBy(c => c.name.Length).FirstOrDefault();
                if (fwd != null && !Covered(Mathf.PI)) set2.Add(ReverseClip(fwd));
                // one clip per direction (the plainest name)
                var dedup = new List<Clip>();
                foreach (var c in set2.OrderBy(c => c.name.Length)) if (!dedup.Any(d => Mathf.Abs(AngDiff(AngleOf(d), AngleOf(c))) < 0.2f)) dedup.Add(c);
                var fw = dedup.Where(c => Mathf.Abs(AngleOf(c)) < 0.8f).ToList();
                var src = fw.Count > 0 ? fw : dedup;
                float speed = src.Sum(c => c.speed) / Mathf.Max(1, src.Count);
                gaits.Add(new Gait { name = kv.Key, speed = speed, clips = dedup.OrderBy(AngleOf).ToList() });
            }
            gaits.Sort((a, b) => a.speed.CompareTo(b.speed));
            Debug.Log($"[ZU] anim: {clips.Count} clips; {string.Join(", ", gaits.Select(g => $"{g.name} x{g.clips.Count}"))}; slots {string.Join(", ", slots.Where(kv => kv.Value.Count > 0).Select(kv => $"{kv.Key} {kv.Value.Count}"))}");
        }

        /// <summary>JS localeCompare(..., { numeric: true }) for the combo order ("Hit 2" before "Hit 10")</summary>
        static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    int c = long.Parse(a.Substring(si, i - si)).CompareTo(long.Parse(b.Substring(sj, j - sj)));
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        void Add(Slot s, Clip c) { if (!slots.TryGetValue(s, out var l)) slots[s] = l = new List<Clip>(); l.Add(c); }
        public List<Clip> Get(Slot s, string hero = null)
        {
            if (hero != null && heroSlots.TryGetValue(hero, out var hs) && hs.TryGetValue(s, out var l) && l.Count > 0) return l;
            return slots.TryGetValue(s, out var g) ? g : new List<Clip>();
        }
        public bool Has(Slot s, string hero = null) => Get(s, hero).Count > 0;
        public bool HasLocomotion => gaits.Count > 0;

        /// <summary>
        /// 8-way / multi-gait blend: clip weights for travelling at `speed` (leg lengths / s) toward `angle` (canonical, 0 =
        /// forward, +pi/2 = the character's left). Two gaits by speed x two neighbouring directions each.
        /// </summary>
        public List<(Clip clip, float w)> Blend(float angle, float speed)
        {
            var output = new List<(Clip, float)>();
            var G = gaits;
            if (G.Count == 0) return output;
            int g0 = 0, g1 = 0; float k = 0;
            if (speed >= G[G.Count - 1].speed) g0 = g1 = G.Count - 1;
            else if (speed > G[0].speed)
            {
                while (g1 < G.Count - 1 && G[g1].speed < speed) g1++;
                g0 = g1 - 1; k = (speed - G[g0].speed) / Mathf.Max(1e-6f, G[g1].speed - G[g0].speed);
            }
            void Push(Gait g, float w)
            {
                if (w <= 1e-4f) return;
                var cs = g.clips;
                if (cs.Count == 1) { output.Add((cs[0], w)); return; }
                // nearest clip on each side of the wanted direction, weighted by angular distance
                Clip lo = null, hi = null; float dlo = float.NegativeInfinity, dhi = float.PositiveInfinity;
                foreach (var c in cs)
                {
                    float d = AngDiff(AngleOf(c), angle);
                    if (d <= 0 && d > dlo) { dlo = d; lo = c; }
                    if (d >= 0 && d < dhi) { dhi = d; hi = c; }
                }
                if (lo == null || hi == null)
                {
                    // no clip on one side: wrap-around neighbours
                    var sorted = cs.OrderBy(c => AngDiff(AngleOf(c), angle)).ToList();
                    lo ??= sorted[sorted.Count - 1]; hi ??= sorted[0];
                    dlo = AngDiff(AngleOf(lo), angle); dhi = AngDiff(AngleOf(hi), angle);
                    if (dlo > 0) dlo -= 2 * Mathf.PI;
                    if (dhi < 0) dhi += 2 * Mathf.PI;
                }
                if (lo == hi) { output.Add((lo, w)); return; }
                float span = dhi - dlo, u = span > 1e-6f ? -dlo / span : 0;
                // clips more than ~100 degrees apart blend poorly: favour the closer one
                float uu = span > 1.75f ? (u < 0.5f ? 0 : 1) * 0.3f + u * 0.7f : u;
                output.Add((lo, w * (1 - uu))); output.Add((hi, w * uu));
            }
            Push(G[g0], 1 - k);
            if (g1 != g0) Push(G[g1], k);
            output.RemoveAll(e => e.Item2 <= 1e-4f);
            return output;
        }
    }
}
