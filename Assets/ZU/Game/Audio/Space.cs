// Acoustic space (TS audio/Space.ts, after Overwatch's "play by sound", GDC 2016): a per-map outdoor reverb, crossfaded
// to a shared indoor room by what's over the listener's head, and the wall reflections - how far the walls are in front
// of / right of / behind / left of the listener, so a shot rings back off the alley walls you're standing between and not
// in an open plaza.
//
// Unity: the TS convolves every send with a generated impulse response and runs a four-tap QUAD DELAY. Without a runtime
// mixer graph this drives one AudioReverbZone that rides on the listener instead: decay, wet level, pre-delay, damping and
// early reflections from the SPACE table (interpolated toward ROOM indoors), and the reflections' delay and level from the
// nearest wall distances. Each source's send is its reverbZoneMix (AudioKit). The same numbers, a different reverb engine:
// tune by ear against the TS.
using UnityEngine;

namespace ZU.Game.Audio
{
    public static class Space
    {
        public struct Acoustic { public float decay, wet, predelay, damp, early; public Acoustic(float decay, float wet, float predelay, float damp, float early) { this.decay = decay; this.wet = wet; this.predelay = predelay; this.damp = damp; this.early = early; } }

        /// <summary>per-map outdoor reverb, plus the shared indoor room (TS SPACE / ROOM)</summary>
        public static readonly System.Collections.Generic.Dictionary<string, Acoustic> SPACE = new System.Collections.Generic.Dictionary<string, Acoustic>
        {
            ["amatsu"] = new Acoustic(1.1f, 0.14f, 0.02f, 5200, 0.25f),
            ["kurogane"] = new Acoustic(1.5f, 0.2f, 0.015f, 4200, 0.5f),
            ["hangar"] = new Acoustic(2.6f, 0.3f, 0.03f, 3800, 0.6f),
            ["cathedral"] = new Acoustic(3.4f, 0.32f, 0.04f, 3200, 0.55f),
            ["rift"] = new Acoustic(2.8f, 0.26f, 0.05f, 2600, 0.2f),
            ["hanabi"] = new Acoustic(1.2f, 0.15f, 0.02f, 5000, 0.35f),
            ["cloudstep"] = new Acoustic(1.0f, 0.12f, 0.025f, 5600, 0.2f),
            ["kagura"] = new Acoustic(1.4f, 0.18f, 0.015f, 4600, 0.55f),
            ["training"] = new Acoustic(1.3f, 0.16f, 0.02f, 4800, 0.4f),
        };
        public static readonly Acoustic ROOM = new Acoustic(0.75f, 0.24f, 0.008f, 4000, 0.8f);

        static AudioReverbZone zone;
        static Acoustic outdoor = SPACE["training"];
        static float indoor, indoorTarget;
        static readonly float[] walls = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };

        internal static void Ensure(GameObject host)
        {
            if (zone != null) return;
            var go = new GameObject("ZU reverb"); go.transform.SetParent(host.transform, false);
            zone = go.AddComponent<AudioReverbZone>();
            zone.reverbPreset = AudioReverbPreset.User;
            zone.minDistance = 5000; zone.maxDistance = 6000;          // the listener is always inside it (it follows him)
            Apply();
        }

        /// <summary>the map's outdoor reverb (indoor rooms share one tight room)</summary>
        public static void SetSpace(string mapId) { outdoor = SPACE.TryGetValue(mapId ?? "", out var a) ? a : SPACE["training"]; indoor = indoorTarget; Apply(); }
        /// <summary>0 = open sky over the listener, 1 = a roof: crossfades the outdoor reverb into the room (0.25 s)</summary>
        public static void SetIndoor(float k) => indoorTarget = Mathf.Clamp01(k);
        /// <summary>distances (m) to the nearest wall in front / right / behind / left of the listener; Infinity = open</summary>
        public static void SetReflections(float[] d) { for (int i = 0; i < 4 && i < d.Length; i++) walls[i] = d[i]; }

        static int MB(float linear) => Mathf.RoundToInt(Mathf.Clamp(2000 * Mathf.Log10(Mathf.Max(1e-5f, linear)), -10000, 0));    // linear gain -> millibels

        internal static void Tick(float dt)
        {
            if (zone == null) return;
            zone.transform.position = AudioKit.Listener;
            indoor += (indoorTarget - indoor) * (1 - Mathf.Exp(-dt / 0.25f));
            Apply();
        }

        static void Apply()
        {
            if (zone == null) return;
            float k = indoor;
            float decay = Mathf.Lerp(outdoor.decay, ROOM.decay, k), wet = Mathf.Lerp(outdoor.wet, ROOM.wet, k), pre = Mathf.Lerp(outdoor.predelay, ROOM.predelay, k);
            float damp = Mathf.Lerp(outdoor.damp, ROOM.damp, k), early = Mathf.Lerp(outdoor.early, ROOM.early, k);
            zone.decayTime = Mathf.Clamp(decay, 0.1f, 20);
            zone.room = MB(wet * 2.2f);                                  // the wet bus level (the sends ride on reverbZoneMix)
            zone.roomHF = MB(Mathf.Clamp01(damp / 6000));                // the IR's darkening: less top end in a damped space
            zone.decayHFRatio = Mathf.Clamp(damp / 5200, 0.1f, 2);
            zone.reverbDelay = Mathf.Clamp(pre, 0, 0.1f);
            // early reflections: the IR's taps, plus the nearest wall's echo (TS QuadDelay: 0.42 / (1 + d / 9) at 2d / 343 s)
            float near = Mathf.Min(Mathf.Min(walls[0], walls[1]), Mathf.Min(walls[2], walls[3]));
            float tap = float.IsInfinity(near) || near > 60 ? 0 : 0.42f / (1 + near / 9);
            zone.reflections = MB(Mathf.Clamp01(early * 0.5f + tap));
            zone.reflectionsDelay = float.IsInfinity(near) || near > 60 ? 0.02f : Mathf.Clamp(2 * near / 343, 0, 0.3f);
        }
    }
}
