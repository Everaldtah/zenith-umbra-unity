// Positional ambience: a sound that lives at a place on the map - waves along the harbour edge, a lantern string's hum,
// a wind pump's creak, the wind across the cloud sea. The map code adds one to a GameObject under the level root
// (AmbientEmitter.Add), so it goes when the level goes; MatchAudio drives the audible ones each frame through AudioKit's
// loops on the ambience bus. Voice-limited like Overwatch's emitters: only the nearest few of each sound play, a handful
// in all, each fading to silence at its radius so walking past a string of lanterns never stacks into a wall of hum.
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.Audio
{
    public sealed class AmbientEmitter : MonoBehaviour
    {
        /// <summary>the sound bank id (a looping sound: sfxbank category "amb" or "loop")</summary>
        public string id;
        /// <summary>audible within this many metres (fades out toward it)</summary>
        public float radius = 20;
        /// <summary>level at the source</summary>
        public float vol = 1;

        const int PER_ID = 2, TOTAL = 6;
        static readonly List<AmbientEmitter> all = new List<AmbientEmitter>();
        static readonly List<(AmbientEmitter e, float d)> near = new List<(AmbientEmitter, float)>();
        static readonly Dictionary<string, int> perId = new Dictionary<string, int>();

        /// <summary>a looping sound at host's position (host = a child of the level root, so it dies with the level)</summary>
        public static AmbientEmitter Add(GameObject host, string id, float radius = 20, float vol = 1)
        {
            var e = host.AddComponent<AmbientEmitter>();
            e.id = id; e.radius = radius; e.vol = vol;
            return e;
        }

        static int nextKey;
        string key;
        void OnEnable() { key ??= "amb:e" + (++nextKey); all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>once a frame (MatchAudio, between AudioKit.BeginFrame and EndFrame): the nearest emitters within reach play</summary>
        internal static void Drive(Vector3 listener)
        {
            if (all.Count == 0) return;
            near.Clear();
            foreach (var e in all)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                float d = Vector3.Distance(e.transform.position, listener);
                if (d < e.radius) near.Add((e, d));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            perId.Clear();
            int n = 0;
            foreach (var (e, d) in near)
            {
                if (n >= TOTAL) break;
                perId.TryGetValue(e.id, out var k);
                if (k >= PER_ID) continue;
                perId[e.id] = k + 1; n++;
                // fade to silence over the outer third of the radius (on top of the source's own distance rolloff)
                float edge = Mathf.Clamp01((e.radius - d) / (e.radius * 0.33f));
                AudioKit.Loop(e.key, e.id, e.transform.position, e.vol * edge * edge);
            }
        }
    }
}
