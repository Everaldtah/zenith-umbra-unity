// Audition and stress tools for the mix (play mode): play one sound where you'd hear it in a fight (a distance and a bearing
// from the listener, who made it), or throw a burst of the bank's loudest categories at the listener all at once - the
// overload the master chain must hold under the ceiling without a crackle. Used by the Sound Lab window and the
// zu_audio_* CLI commands (tools/audio/README.md).
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZU.Game.Audio
{
    public static class AudioStress
    {
        /// <summary>every sound id in the bank with its category</summary>
        public static List<(string id, string cat)> Ids()
        {
            var o = new List<(string, string)>();
            var ta = Resources.Load<TextAsset>("ZUData/sfxbank");
            if (ta == null) return o;
            foreach (var kv in (JObject)JObject.Parse(ta.text)["sfx"]) o.Add((kv.Key, (string)kv.Value["cat"] ?? "ability"));
            o.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return o;
        }

        /// <summary>where a sound `dist` m away at `bearing` degrees (0 = straight ahead, 90 = right) from the listener sits</summary>
        public static Vector3 Around(float dist, float bearing)
        {
            var f = AudioKit.ListenerForward; f.y = 0; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
            return AudioKit.Listener + Quaternion.Euler(0, bearing, 0) * f * dist;
        }

        /// <summary>one sound as the listener would hear it: dist 0 = in your head (your own sound)</summary>
        public static void Audition(string id, float dist, float bearing, Rel rel = Rel.Enemy, float vol = 1)
        {
            if (Camera.main != null) { AudioKit.Listener = Camera.main.transform.position; AudioKit.ListenerForward = Camera.main.transform.forward; }
            if (dist <= 0) AudioKit.Play(id, null, vol, Rel.Self);
            else AudioKit.Play(id, Around(dist, bearing), vol, new PlayOpts { rel = rel, rate = 1 });
        }

        /// <summary>a fight's worst instant: `count` weapon / impact / ability sounds within `spread` seconds, all around the
        /// listener between 2 and 25 m, plus your own shots in your head - repeated `bursts` times, `gap` s apart</summary>
        public static void Burst(int count = 40, float spread = 0.25f, int bursts = 6, float gap = 0.6f, int seed = 1)
        {
            var ids = Ids().FindAll(x => x.cat == "weapon" || x.cat == "impact" || x.cat == "ability");
            if (ids.Count == 0) return;
            var host = new GameObject("ZU audio stress") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().Set(ids, count, spread, bursts, gap, seed);
        }

        sealed class Runner : MonoBehaviour
        {
            readonly List<(float at, string id, float dist, float bearing, bool self)> plan = new List<(float, string, float, float, bool)>();
            float t0;
            public void Set(List<(string id, string cat)> ids, int count, float spread, int bursts, float gap, int seed)
            {
                var r = new System.Random(seed);
                for (int b = 0; b < bursts; b++)
                    for (int i = 0; i < count; i++)
                    {
                        var (id, _) = ids[r.Next(ids.Count)];
                        bool self = i % 8 == 0;
                        plan.Add((b * gap + (float)r.NextDouble() * spread, id, 2 + (float)r.NextDouble() * 23, (float)r.NextDouble() * 360, self));
                    }
                plan.Sort((a, c) => a.at.CompareTo(c.at));
                t0 = Time.time;
            }
            void Update()
            {
                float t = Time.time - t0;
                while (plan.Count > 0 && plan[0].at <= t)
                {
                    var p = plan[0]; plan.RemoveAt(0);
                    Audition(p.id, p.self ? 0 : p.dist, p.bearing, Rel.Enemy);
                }
                if (plan.Count == 0) Destroy(gameObject);
            }
        }
    }
}
