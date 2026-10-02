// The recorded sound bank (Resources/ZUAudio from the TS game's public/sfx; tools/export/export_audio.py) played the way
// the TS desktop edition mixes it (audio/Sfx.ts + Bank.ts, ported): category buses (sfx, ambience, voice, announcer) with
// Settings > Sound's volumes, voice ducking (a line pulls the world down so it cuts through; critical lines more), a
// pool of voices with per-sound caps and a global budget (the oldest fades out to make room, never a hard cut), a
// variation never repeated twice in a row, a small pitch spread per category, inverse-distance rolloff from each
// category's reference distance, air absorption (far sounds lose their top end), occlusion behind walls (muffled and
// quieter), the threat mix (MatchAudio sets each actor's importance gain), louder enemy footsteps, your own sounds in
// your head, a burst of the same sound in one instant played once, nothing past 110 m; per-frame keyed loops (beams,
// flames, grinding, wind, the map's bed) that fade out when nobody refreshes them; voice lines with the mech pilot's
// cockpit radio and enemy ult warnings that stay loud; the acoustic space (Space.cs).
//
// Unity has no runtime mixer graph, so the buses and ducks are gains applied to each pooled AudioSource every frame
// (AudioKitDriver), and the reverb sends are each source's reverbZoneMix into the listener's reverb zone (Space.cs).
// No HRTF (Unity needs a spatializer plugin for it): 3D sources use Unity's panning.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ZU.Game.UI.Toolkit;

namespace ZU.Game.Audio
{
    public enum Rel { None, Self, Ally, Enemy }
    public enum Bus { Sfx, Amb, Voice, Announcer }

    /// <summary>TS PlayOpts: who made the sound (threat mixing), their relation to the listener, a reverb send override, a
    /// playback-rate multiplier</summary>
    public struct PlayOpts
    {
        public int actor; public Rel rel; public float? send; public float rate;
        public static PlayOpts Of(ZU.Sim.Actor a, Rel rel) => new PlayOpts { actor = a?.id ?? 0, rel = rel, rate = 1 };
    }

    public static class AudioKit
    {
        // TS CATS: reference distance (m), reverb send, quad-delay send, pitch variance
        static readonly Dictionary<string, (float refDist, float send, float quad, float pv)> CATS = new Dictionary<string, (float, float, float, float)>
        {
            ["weapon"] = (7, 0.22f, 0.55f, 0.06f), ["impact"] = (3.5f, 0.18f, 0.35f, 0.1f), ["ability"] = (7, 0.25f, 0.25f, 0.03f), ["move"] = (3, 0.1f, 0.1f, 0.06f),
            ["step"] = (2.6f, 0.08f, 0.05f, 0.08f), ["feedback"] = (4, 0.05f, 0, 0.02f), ["loop"] = (4, 0.12f, 0, 0), ["amb"] = (1, 0, 0, 0), ["voice"] = (5, 0.12f, 0.05f, 0),
        };
        /// <summary>simultaneous instances of one sound per category (the oldest fades out to make room)</summary>
        static readonly Dictionary<string, int> CAP = new Dictionary<string, int> { ["weapon"] = 5, ["impact"] = 4, ["step"] = 6, ["move"] = 3, ["ability"] = 4, ["feedback"] = 3, ["loop"] = 2, ["amb"] = 1, ["voice"] = 3 };
        const int MAX_LIVE = 48, VOICES = 64;

        /// <summary>where the listener is (MatchAudio sets it each frame from the camera)</summary>
        public static Vector3 Listener;
        public static Vector3 ListenerForward = Vector3.forward;
        /// <summary>how blocked the path from the listener to a point is (0 = clear line of sight, up to 0.9 behind a floor)</summary>
        public static Func<Vector3, float> Occlusion;
        /// <summary>per-actor mix gain from the threat buckets (1.25 HIGH, 1 NORMAL, 0.7 LOW, 0.35 the rest; teammates 0.7)</summary>
        public static readonly Dictionary<int, float> Threat = new Dictionary<int, float>();

        internal sealed class Voice
        {
            public AudioSource src; public AudioLowPassFilter lp; public AudioHighPassFilter hp; public AudioDistortionFilter drive;
            public string id; public string cat; public Bus bus; public float gain = 1, fade = 1, fadeTarget = 1, fadeTc = 0.004f, stopAt = float.PositiveInfinity, until, t0;
            public bool loop; public string loopKey; public int seen; public float targetGain = 1, cutoff = 22000, cutoffTarget = 22000;
        }
        static readonly List<Voice> pool = new List<Voice>();
        static GameObject host;
        static JObject sfx, vo, banks, subs;
        static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        static readonly Dictionary<string, (AudioClip clip, int take)[]> lines = new Dictionary<string, (AudioClip, int)[]>();
        static readonly Dictionary<string, int> last = new Dictionary<string, int>();
        static readonly Dictionary<string, float> throttle = new Dictionary<string, float>();

        static void Ensure()
        {
            if (host != null) return;
            host = new GameObject("ZU Audio") { hideFlags = HideFlags.HideInHierarchy };   // (not DontSave: play mode must clean it up)
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<AudioKitDriver>();
            pool.Clear(); clips.Clear(); lines.Clear(); last.Clear(); throttle.Clear(); loops.Clear();
            for (int i = 0; i < VOICES; i++)
            {
                var go = new GameObject("voice " + i); go.transform.SetParent(host.transform, false);
                var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.dopplerLevel = 0; s.rolloffMode = AudioRolloffMode.Logarithmic; s.maxDistance = 140;
                var v = new Voice { src = s, lp = go.AddComponent<AudioLowPassFilter>(), hp = go.AddComponent<AudioHighPassFilter>(), drive = go.AddComponent<AudioDistortionFilter>() };
                v.hp.enabled = false; v.drive.enabled = false; v.drive.distortionLevel = 0.15f;
                pool.Add(v);
            }
            Space.Ensure(host);
            var ta = Resources.Load<TextAsset>("ZUData/sfxbank");
            if (ta != null) { var j = JObject.Parse(ta.text); sfx = j["sfx"] as JObject; vo = j["vo"] as JObject; banks = j["banks"] as JObject; subs = j["subs"] as JObject; }
        }

        // ------------------------------------------------------------------------------------------------ the bank
        public static bool Has(string id) { Ensure(); return sfx?[id] != null; }
        static string Cat(string id) => (string)sfx?[id]?["cat"] ?? "ability";
        /// <summary>the hero whose bank speaks for this hero (Tenkai-Oh -> Haruto, Gorgoth -> Vorn)</summary>
        public static string VoiceOf(string heroId) { Ensure(); return (string)banks?[heroId] ?? heroId; }
        public static bool HasLine(string voice, string key) { Ensure(); return ((int?)vo?[voice]?[key] ?? 0) > 0 && LineClips(voice, key).Length > 0; }
        /// <summary>the words of a take (subtitles), "" if unknown</summary>
        public static string Words(string voice, string key, int take) => take >= 0 ? (string)(subs?[voice]?[key] as JArray)?[take] ?? "" : "";

        static AudioClip[] Clips(string key, string folder, int n)
        {
            if (clips.TryGetValue(key, out var c)) return c;
            var list = new List<AudioClip>();
            for (int i = 0; i < n; i++) { var a = Resources.Load<AudioClip>(folder + i); if (a != null) list.Add(a); }
            return clips[key] = list.ToArray();
        }
        static (AudioClip clip, int take)[] LineClips(string voice, string key)
        {
            string k = $"vo:{voice}:{key}";
            if (lines.TryGetValue(k, out var c)) return c;
            int n = (int?)vo?[voice]?[key] ?? 0;
            var list = new List<(AudioClip, int)>();
            // each take remembers its number: the subtitle is looked up by it (a missing take must not shift the others)
            for (int i = 0; i < n; i++) { var a = Resources.Load<AudioClip>($"ZUAudio/vo/{voice}/{key}_{i}"); if (a != null) list.Add((a, i)); }
            return lines[k] = list.ToArray();
        }

        /// <summary>a variation, never the same one twice in a row (TS SampleBank.pick)</summary>
        static int PickIndex(string key, int n)
        {
            if (n <= 1) return n - 1;
            int i = UnityEngine.Random.Range(0, n);
            if (last.TryGetValue(key, out var p) && i == p) i = (i + 1) % n;
            last[key] = i;
            return i;
        }
        static AudioClip Pick(string key, AudioClip[] c) { int i = PickIndex(key, c.Length); return i < 0 ? null : c[i]; }
        /// <summary>a take of a voice line and its number (for the subtitle)</summary>
        public static AudioClip PickLine(string voice, string key, out int take)
        {
            Ensure();
            var c = LineClips(voice, key); int i = PickIndex($"vo:{voice}:{key}", c.Length);
            take = i < 0 ? -1 : c[i].take;
            return i < 0 ? null : c[i].clip;
        }

        // ------------------------------------------------------------------------------------------------ the mix
        /// <summary>Settings > Sound (the TS Sfx.mix): read from the saved settings each frame</summary>
        static ZuSettings.SoundSettings Mix => ZuSettings.Current?.sound;
        static float BusGain(Bus b)
        {
            var m = Mix;
            float sfxV = (float)(m?.sfx ?? 1), amb = (float)(m?.ambience ?? 0.8), voice = (float)(m?.voice ?? 1), ann = (float)(m?.announcer ?? 1);
            switch (b)
            {
                case Bus.Sfx: return 0.8f * sfxV * sfxDuck;                  // headroom: a dense fight sums many sounds
                case Bus.Amb: return 0.9f * amb * ambDuck;
                case Bus.Voice: return voice;
                default: return ann;
            }
        }
        public static float HitmarkerVol => (float)(Mix?.hitmarker ?? 1);
        static float UiVol => (float)(Mix?.ui ?? 0.8);

        // voice ducking: each duck falls toward its target with a 40 ms time constant and recovers with 0.3 s once the line ends
        static float sfxDuck = 1, ambDuck = 1, duckAmount, duckUntil;
        /// <summary>voice lines duck the world a little so they cut through (critical lines duck more)</summary>
        public static void Duck(float amount, float secs)
        {
            duckAmount = amount; duckUntil = Time.unscaledTime + secs;
        }

        /// <summary>mix gain, air absorption and occlusion for a sound at pos (shared by one-shots, loops and voice)</summary>
        static (float gain, float cutoff, float dist, float occ) Spatial(Vector3? pos, string cat, PlayOpts o)
        {
            float gain = 1, cutoff = 20000, dist = 0, occ = 0;
            if (o.actor != 0 && o.rel != Rel.Self && Threat.TryGetValue(o.actor, out var th)) gain *= th;
            if (cat == "step" && o.rel != Rel.None) gain *= o.rel == Rel.Enemy ? 1.45f : o.rel == Rel.Ally ? 0.55f : 0.8f;
            if (pos.HasValue && o.rel != Rel.Self)
            {
                dist = Vector3.Distance(pos.Value, Listener);
                // air absorption: far sounds lose their top end
                cutoff = 20000 * Mathf.Pow(Mathf.Max(0, 1 - dist / 110), 1.8f) + 1400;
                occ = Occlusion != null ? Occlusion(pos.Value) : 0;
                if (occ > 0) { cutoff = Mathf.Min(cutoff, 20000 * (1 - 0.93f * occ) + 700); gain *= 1 - 0.5f * occ; }
            }
            return (gain, cutoff, dist, occ);
        }

        static readonly List<Voice> live = new List<Voice>();
        /// <summary>make room: per-sound caps, then the global voice budget - the oldest fades out in ~12 ms</summary>
        static void Admit(string id, string cat)
        {
            float now = Time.unscaledTime;
            live.Clear();
            foreach (var v in pool) if (!v.loop && v.src.isPlaying && v.fadeTarget > 0 && v.until > now) live.Add(v);
            live.Sort((a, b) => a.t0.CompareTo(b.t0));
            int cap = CAP.TryGetValue(cat, out var c) ? c : 4, same = 0; Voice firstSame = null;
            foreach (var v in live) if (v.id == id) { same++; firstSame ??= v; }
            var drop = same >= cap ? firstSame : live.Count >= MAX_LIVE ? live[0] : null;
            if (drop != null) FadeOut(drop, 0.004f, 0.03f);
        }
        static void FadeOut(Voice v, float tc, float stopIn) { v.fadeTarget = 0; v.fadeTc = tc; v.stopAt = Time.unscaledTime + stopIn; }

        static Voice Free()
        {
            Voice best = null;
            foreach (var v in pool) { if (!v.src.isPlaying) return v; if (!v.loop && (best == null || v.until < best.until)) best = v; }
            return best;   // steal the one-shot closest to finishing
        }

        static Voice Start(AudioClip clip, Vector3? pos, float gain, float refDist, float pv, Rel rel, float rate, bool loop, Bus bus, float cutoff, float send, string id, string cat)
        {
            var v = Free();
            if (v == null) return null;
            var s = v.src;
            s.Stop();
            s.clip = clip; s.loop = loop;
            bool spatial = pos.HasValue && rel != Rel.Self;
            if (spatial) s.transform.position = pos.Value;
            s.spatialBlend = spatial ? 1 : 0;
            s.minDistance = Mathf.Max(0.5f, refDist);
            s.pitch = rate * (1 + (UnityEngine.Random.value - 0.5f) * 2 * Mathf.Min(0.03f, pv));     // pitch spread kept small
            s.reverbZoneMix = Mathf.Clamp(send * 2, 0, 1.1f);
            v.lp.cutoffFrequency = v.cutoff = v.cutoffTarget = cutoff; v.lp.lowpassResonanceQ = 0.7f;
            v.hp.enabled = false; v.drive.enabled = false;
            v.id = id; v.cat = cat; v.bus = bus; v.loop = loop; v.loopKey = null;
            v.gain = v.targetGain = gain; v.fade = 1; v.fadeTarget = 1; v.fadeTc = 0.004f; v.stopAt = float.PositiveInfinity;
            v.t0 = Time.unscaledTime;
            v.until = v.t0 + (loop ? 1e6f : clip.length / Mathf.Max(0.1f, s.pitch));
            s.volume = Mathf.Clamp01(v.gain * BusGain(bus));
            s.Play();
            return v;
        }

        /// <summary>a sound effect by id; pos null = in your head (UI, your own sounds)</summary>
        public static void Play(string id, Vector3? pos, float vol = 1, Rel rel = Rel.None, float rate = 1) => Play(id, pos, vol, new PlayOpts { rel = rel, rate = rate });

        /// <summary>a sound effect by id, mixed by who made it (TS Sfx.play -> playSample)</summary>
        public static void Play(string id, Vector3? pos, float vol, PlayOpts o)
        {
            Ensure();
            var meta = sfx?[id];
            if (meta == null) return;
            string cat = (string)meta["cat"] ?? "ability";
            float now = Time.unscaledTime;
            if (throttle.TryGetValue(id, out var t) && now - t < (cat == "weapon" ? 0.03f : 0.02f)) return;
            throttle[id] = now;
            if (pos.HasValue && o.rel != Rel.Self && Vector3.Distance(pos.Value, Listener) > 110) return;
            var clip = Pick(id, Clips("sfx:" + id, $"ZUAudio/sfx/{id}/", (int)meta["n"]));
            if (clip == null) return;
            var C = CATS.TryGetValue(cat, out var cc) ? cc : CATS["ability"];
            var S = Spatial(pos, cat, o);
            if (S.gain < 0.05f) return;           // culled by the threat mix
            Admit(id, cat);
            // TS-PARITY: hit / crit / kill take the hit-marker volume here and again where MatchAudio plays them, as in the TS
            float v = vol * S.gain * (o.rel == Rel.Self ? 0.9f : 1) * (id.StartsWith("ui_") ? UiVol : id == "hit" || id == "crit" || id == "kill" ? HitmarkerVol : 1);
            // space: more reverb with distance (the far layer of a gunshot is mostly tail)
            float send = (o.send ?? C.send) * (1 + Mathf.Min(1.2f, S.dist / 45)) * (1 + S.occ * 0.6f);
            Start(clip, pos, v, C.refDist, C.pv, o.rel, o.rate <= 0 ? 1 : o.rate, false, Bus.Sfx, S.cutoff, send, id, cat);
        }

        // ------------------------------------------------------------------------------------------------ voice lines
        /// <summary>a playing voice line: stop() fades it out (TS playLine's handle)</summary>
        public sealed class LineHandle { internal Voice v; internal AudioClip clip; public float dur; public void Stop() { if (v != null && v.src.clip == clip && v.src.isPlaying) FadeOut(v, 0.03f, 0.12f); } }

        /// <summary>a voice line on the voice bus (the director decides who hears what): radio = a mech pilot over the cockpit
        /// comms; ult = an enemy ult warning, loud wherever it comes from; announcer = the announcer's bus</summary>
        public static LineHandle PlayLine(AudioClip clip, Vector3? pos, float vol, Rel rel, bool radio = false, bool ult = false, bool announcer = false)
        {
            Ensure();
            if (clip == null) return null;
            var S = Spatial(pos, "voice", new PlayOpts { rel = rel });
            // enemy ult warnings stay loud wherever they come from (you have to hear them to react)
            float gv = vol * (ult ? Mathf.Max(0.85f, S.gain) : S.gain);
            var v = Start(clip, pos, gv, ult ? 30 : 6, 0, rel, 1, false, announcer ? Bus.Announcer : Bus.Voice, ult ? 20000 : S.cutoff, 0.1f, "vo", "voice");
            if (v == null) return null;
            if (radio)
            {
                // band-limited with a little grit: the cockpit comms
                v.hp.enabled = true; v.hp.cutoffFrequency = 320;
                v.lp.cutoffFrequency = v.cutoff = v.cutoffTarget = Mathf.Min(v.cutoff, 3600);
                v.drive.enabled = true;
            }
            return new LineHandle { v = v, clip = clip, dur = clip.length };
        }

        /// <summary>a hero's voice line straight out (no director: menus, previews); returns its length, 0 if none</summary>
        public static float Line(string heroId, string key, Vector3? pos, Rel rel, float vol = 1)
        {
            var voice = VoiceOf(heroId);
            var clip = PickLine(voice, key, out _);
            var h = clip != null ? PlayLine(clip, pos, vol, rel) : null;
            return h?.dur ?? 0;
        }
        public static void Announce(string key) => VoiceLines.Announce(key);

        // ------------------------------------------------------------------------------------------------ loops
        static readonly Dictionary<string, Voice> loops = new Dictionary<string, Voice>();
        static int frame;
        public static void BeginFrame() { Ensure(); frame++; }
        /// <summary>keep a looping sound going this frame (call every frame it should play); loops not refreshed fade out
        /// (EndFrame). Keys starting "amb" go on the ambience bus.</summary>
        public static void Loop(string key, string id, Vector3? pos, float vol, PlayOpts o = default)
        {
            Ensure();
            var meta = sfx?[id];
            if (meta == null) return;
            if (loops.TryGetValue(key, out var L) && (L.id != id || !L.src.isPlaying || L.loopKey != key)) { StopLoop(key); L = null; }
            string cat = key.StartsWith("amb") ? "amb" : "loop";
            var S = Spatial(pos, cat, o);
            var C = CATS[cat];
            if (L == null)
            {
                var clip = Pick(id, Clips("sfx:" + id, $"ZUAudio/sfx/{id}/", (int)meta["n"]));
                if (clip == null) return;
                L = Start(clip, pos, 0, C.refDist + 2, 0, o.rel, 1, true, cat == "amb" ? Bus.Amb : Bus.Sfx, S.cutoff, cat == "amb" ? 0 : 0.12f, id, cat);
                if (L == null) return;
                L.src.time = UnityEngine.Random.value * clip.length * 0.999f;     // start somewhere in the loop
                L.loopKey = key; L.gain = 0;
                loops[key] = L;
            }
            L.seen = frame;
            L.targetGain = vol * S.gain; L.cutoffTarget = S.cutoff;
            if (o.rate > 0) L.src.pitch = Mathf.Lerp(L.src.pitch, o.rate, 0.2f);
            if (pos.HasValue && o.rel != Rel.Self) L.src.transform.position = pos.Value;
        }
        /// <summary>fade out loops nobody refreshed this frame</summary>
        public static void EndFrame()
        {
            List<string> gone = null;
            foreach (var kv in loops) if (kv.Value.seen != frame) (gone ??= new List<string>()).Add(kv.Key);
            if (gone != null) foreach (var k in gone) StopLoop(k);
        }
        public static void StopLoop(string key)
        {
            if (!loops.TryGetValue(key, out var L)) return;
            loops.Remove(key);
            if (L.loopKey == key) { L.targetGain = 0; FadeOut(L, 0.08f, 0.5f); L.loopKey = null; }
        }
        public static void StopAllLoops() { foreach (var k in new List<string>(loops.Keys)) StopLoop(k); }
        public static void StopAll() { foreach (var v in pool) { v.src.Stop(); v.until = 0; v.loopKey = null; } loops.Clear(); }

        /// <summary>a readout of the mix for checks from the CLI: the ducks, what's playing per bus, each voice line's filter
        /// chain, the reverb zone, and the output level</summary>
        public static string Diag()
        {
            Ensure();
            var sb = new System.Text.StringBuilder();
            sb.Append($"sfxDuck {sfxDuck:0.00} ambDuck {ambDuck:0.00} duck {(Time.unscaledTime < duckUntil ? duckAmount : 0):0.00}; ");
            var n = new Dictionary<Bus, int>();
            foreach (var v in pool) if (v.src.isPlaying) n[v.bus] = (n.TryGetValue(v.bus, out var k) ? k : 0) + 1;
            foreach (var kv in n) sb.Append($"{kv.Key} {kv.Value} ");
            sb.Append($"loops {loops.Count}; ");
            foreach (var v in pool)
                if (v.src.isPlaying && v.cat == "voice")
                    sb.Append($"[{v.src.clip?.name} vol {v.src.volume:0.00} 3d {v.src.spatialBlend:0} lp {v.lp.cutoffFrequency:0} hp {(v.hp.enabled ? v.hp.cutoffFrequency.ToString("0") : "off")} drive {(v.drive.enabled ? "on" : "off")}] ");
            sb.Append(Space.Diag());
            var buf = new float[1024]; AudioListener.GetOutputData(buf, 0);
            double sum = 0, peak = 0; foreach (var x in buf) { sum += x * x; peak = Math.Max(peak, Math.Abs(x)); }
            sb.Append($" out rms {Math.Sqrt(sum / buf.Length):0.000} peak {peak:0.000}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ per frame
        /// <summary>the buses, ducks, fades and loop gains applied to every voice (AudioKitDriver, every frame)</summary>
        internal static void Tick(float dt)
        {
            float now = Time.unscaledTime;
            // ducks: the world toward 1 - amount x k while a line plays, back to 1 after it
            float dk = now < duckUntil ? duckAmount : 0, kDown = 1 - Mathf.Exp(-dt / 0.04f), kUp = 1 - Mathf.Exp(-dt / 0.3f);
            float ws = 1 - dk * 0.35f, wa = 1 - dk;
            sfxDuck += (ws - sfxDuck) * (ws < sfxDuck ? kDown : kUp);
            ambDuck += (wa - ambDuck) * (wa < ambDuck ? kDown : kUp);
            float kLoop = 1 - Mathf.Exp(-dt / 0.08f), kCut = 1 - Mathf.Exp(-dt / 0.1f);
            foreach (var v in pool)
            {
                if (!v.src.isPlaying) continue;
                if (now >= v.stopAt) { v.src.Stop(); v.until = 0; continue; }
                if (v.loop) { v.gain += (v.targetGain - v.gain) * kLoop; v.cutoff += (v.cutoffTarget - v.cutoff) * kCut; v.lp.cutoffFrequency = v.cutoff; }
                if (v.fade != v.fadeTarget) v.fade += (v.fadeTarget - v.fade) * (1 - Mathf.Exp(-dt / Mathf.Max(1e-4f, v.fadeTc)));
                v.src.volume = Mathf.Clamp01(v.gain * v.fade * BusGain(v.bus));
            }
            Space.Tick(dt);
        }
    }

    /// <summary>runs AudioKit's per-frame mix (lives on the audio host, so menus get it too)</summary>
    public sealed class AudioKitDriver : MonoBehaviour
    {
        void LateUpdate() => AudioKit.Tick(Time.unscaledDeltaTime);
    }
}
