// The recorded sound bank (Resources/ZUAudio from the TS game's public/sfx; tools/export/export_audio.py) played the way
// the TS desktop edition mixes it (audio/Sfx.ts): a pool of voices, a variation never repeated twice in a row, a small
// pitch spread per category, inverse-distance rolloff from each category's reference distance, air absorption (far
// sounds lose their top end), occlusion behind walls (muffled and quieter), your own sounds in your head, a burst of
// the same sound in one instant played once, and nothing past 110 m. Loops (beams, flames, the map's bed) by handle.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZU.Game.Audio
{
    public enum Rel { None, Self, Ally, Enemy }

    public static class AudioKit
    {
        // TS CATS: reference distance (m), pitch variance
        static readonly Dictionary<string, (float refDist, float pv, float vol)> CATS = new Dictionary<string, (float, float, float)>
        {
            ["weapon"] = (7, 0.06f, 0.9f), ["impact"] = (3.5f, 0.1f, 0.85f), ["ability"] = (7, 0.03f, 0.95f), ["move"] = (3, 0.06f, 0.7f),
            ["step"] = (2.6f, 0.08f, 0.55f), ["feedback"] = (4, 0.02f, 0.8f), ["loop"] = (4, 0, 0.7f), ["amb"] = (1, 0, 0.45f), ["voice"] = (5, 0, 1f),
        };
        public static float Master = 0.9f, VoiceVol = 1f;
        /// <summary>where the listener is and what it can see (MatchAudio sets them each frame)</summary>
        public static Vector3 Listener;
        public static System.Func<Vector3, float> Occlusion;     // 0 = clear line of sight, up to 0.9 behind a floor

        internal sealed class Voice { public AudioSource src; public AudioLowPassFilter lp; public float until; }
        static readonly List<Voice> pool = new List<Voice>();
        static GameObject host;
        static JObject sfx, vo, banks;
        static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        static readonly Dictionary<string, int> last = new Dictionary<string, int>();
        static readonly Dictionary<string, float> throttle = new Dictionary<string, float>();
        const int VOICES = 40;

        static void Ensure()
        {
            if (host != null) return;
            host = new GameObject("ZU Audio") { hideFlags = HideFlags.HideInHierarchy };   // (not DontSave: play mode must clean it up)
            Object.DontDestroyOnLoad(host);
            pool.Clear(); clips.Clear(); last.Clear(); throttle.Clear();
            for (int i = 0; i < VOICES; i++)
            {
                var go = new GameObject("voice " + i); go.transform.SetParent(host.transform, false);
                var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.dopplerLevel = 0; s.rolloffMode = AudioRolloffMode.Logarithmic; s.maxDistance = 140;
                pool.Add(new Voice { src = s, lp = go.AddComponent<AudioLowPassFilter>() });
            }
            var ta = Resources.Load<TextAsset>("ZUData/sfxbank");
            if (ta != null) { var j = JObject.Parse(ta.text); sfx = j["sfx"] as JObject; vo = j["vo"] as JObject; banks = j["banks"] as JObject; }
        }

        public static bool Has(string id) { Ensure(); return sfx?[id] != null; }
        static string Cat(string id) => (string)sfx?[id]?["cat"] ?? "ability";

        static AudioClip[] Clips(string key, string folder, int n)
        {
            if (clips.TryGetValue(key, out var c)) return c;
            var list = new List<AudioClip>();
            for (int i = 0; i < n; i++) { var a = Resources.Load<AudioClip>(folder + i); if (a != null) list.Add(a); }
            return clips[key] = list.ToArray();
        }

        static AudioClip Pick(string key, AudioClip[] c)
        {
            if (c.Length == 0) return null;
            int prev = last.TryGetValue(key, out var p) ? p : -1, i = Random.Range(0, c.Length);
            if (c.Length > 1 && i == prev) i = (i + 1) % c.Length;
            last[key] = i;
            return c[i];
        }

        static Voice Free()
        {
            Voice best = null;
            foreach (var v in pool) { if (!v.src.isPlaying) return v; if (best == null || v.until < best.until) best = v; }
            return best;   // steal the one closest to finishing
        }

        /// <summary>a sound effect by id; pos null = in your head; rel: whose it is (steps are louder from enemies)</summary>
        public static void Play(string id, Vector3? pos, float vol = 1, Rel rel = Rel.None, float rate = 1)
        {
            Ensure();
            var meta = sfx?[id];
            if (meta == null) return;
            string cat = (string)meta["cat"] ?? "ability";
            float now = Time.unscaledTime;
            if (throttle.TryGetValue(id, out var t) && now - t < (cat == "weapon" ? 0.03f : 0.02f)) return;
            throttle[id] = now;
            if (pos.HasValue && rel != Rel.Self && Vector3.Distance(pos.Value, Listener) > 110) return;
            var clip = Pick(id, Clips("sfx:" + id, $"ZUAudio/sfx/{id}/", (int)meta["n"]));
            if (clip == null) return;
            var C = CATS.TryGetValue(cat, out var cc) ? cc : CATS["ability"];
            float gain = vol * C.vol;
            if (cat == "step") gain *= rel == Rel.Enemy ? 1.45f : rel == Rel.Ally ? 0.55f : 0.8f;
            Start(clip, pos, gain, C.refDist, C.pv, rel, rate, false);
        }

        static Voice Start(AudioClip clip, Vector3? pos, float gain, float refDist, float pv, Rel rel, float rate, bool loop)
        {
            var v = Free();
            var s = v.src;
            s.Stop();
            s.clip = clip; s.loop = loop;
            bool spatial = pos.HasValue && rel != Rel.Self;
            float cutoff = 22000, dist = 0;
            if (spatial)
            {
                dist = Vector3.Distance(pos.Value, Listener);
                cutoff = 20000 * Mathf.Pow(Mathf.Max(0, 1 - dist / 110), 1.8f) + 1400;      // air absorption
                float occ = Occlusion != null ? Occlusion(pos.Value) : 0;
                if (occ > 0) { cutoff = Mathf.Min(cutoff, 20000 * (1 - 0.93f * occ) + 700); gain *= 1 - 0.5f * occ; }
                s.transform.position = pos.Value;
            }
            s.spatialBlend = spatial ? 1 : 0;
            s.minDistance = Mathf.Max(0.5f, refDist);
            s.volume = Mathf.Clamp01(gain * Master * (rel == Rel.Self ? 0.9f : 1));
            s.pitch = rate * (1 + (Random.value - 0.5f) * 2 * Mathf.Min(0.03f, pv));
            v.lp.cutoffFrequency = cutoff;
            s.Play();
            v.until = Time.unscaledTime + (loop ? 1e6f : clip.length / Mathf.Max(0.1f, s.pitch));
            return v;
        }

        // ------------------------------------------------------------------------------------------------ voice
        /// <summary>a hero's voice line (bank voices: mechs speak through their pilots); returns its length, 0 if none</summary>
        public static float Line(string heroId, string key, Vector3? pos, Rel rel, float vol = 1)
        {
            Ensure();
            string voice = (string)banks?[heroId] ?? heroId;
            int n = (int?)vo?[voice]?[key] ?? 0;
            if (n == 0) return 0;
            var clip = Pick($"vo:{voice}:{key}", LineClips(voice, key, n));
            if (clip == null) return 0;
            Start(clip, rel == Rel.Self ? null : pos, vol * VoiceVol, CATS["voice"].refDist, 0, rel, 1, false);
            return clip.length;
        }
        static AudioClip[] LineClips(string voice, string key, int n)
        {
            string k = $"vo:{voice}:{key}";
            if (clips.TryGetValue(k, out var c)) return c;
            var list = new List<AudioClip>();
            for (int i = 0; i < n; i++) { var a = Resources.Load<AudioClip>($"ZUAudio/vo/{voice}/{key}_{i}"); if (a != null) list.Add(a); }
            return clips[k] = list.ToArray();
        }
        public static float Announce(string key) => Line("announcer", key, null, Rel.Self, 0.9f);

        // ------------------------------------------------------------------------------------------------ loops
        public sealed class LoopHandle { internal Voice v; internal AudioClip clip; public bool Alive => v != null && v.src.isPlaying && v.src.clip == clip; }
        public static LoopHandle Loop(string id, Vector3? pos, float vol = 1, Rel rel = Rel.None)
        {
            Ensure();
            var meta = sfx?[id];
            if (meta == null) return null;
            var clip = Pick(id, Clips("sfx:" + id, $"ZUAudio/sfx/{id}/", (int)meta["n"]));
            if (clip == null) return null;
            string cat = (string)meta["cat"] ?? "loop";
            var C = CATS.TryGetValue(cat, out var cc) ? cc : CATS["loop"];
            var v = Start(clip, pos, vol * C.vol, C.refDist, 0, rel, 1, true);
            return new LoopHandle { v = v, clip = clip };
        }
        public static void Move(LoopHandle h, Vector3 pos) { if (h != null && h.Alive) h.v.src.transform.position = pos; }
        public static void Stop(LoopHandle h) { if (h != null && h.Alive) { h.v.src.Stop(); h.v.until = 0; } }
        public static void StopAll() { foreach (var v in pool) { v.src.Stop(); v.until = 0; } }
    }
}
