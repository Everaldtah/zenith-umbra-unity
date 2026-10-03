// Every humanoid clip the clip layer can play (Resources/ZUAnim/ZuClipSet.asset, written by `zu_bake_clips`), with what the
// TS measures on each clip when it bakes its PoseClips (Retarget.ts analyse): the travel speed and direction of a gait,
// the planted feet per frame, the phase anchor, and the per-frame motion curves one-shot trimming reads (ClipLibrary
// trimAction). The asset also keeps every clip in a player build.
// Units and frame as the TS's: leg lengths, the canonical frame (x = the character's LEFT, z = forward, y = up).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.Anim
{
    [CreateAssetMenu(menuName = "ZU/Clip Set")]
    public sealed class ZuClipSet : ScriptableObject
    {
        [Serializable]
        public sealed class Info
        {
            public AnimationClip clip;
            /// <summary>the take's name as the TS manifest calls it ("TR_Idle", "MX_Death_Back")</summary>
            public string name;
            public string pack;
            public bool loop;
            public float fps, length;
            /// <summary>leg lengths per second the clip travels (0 = stationary), and its unit direction (x = left, y = forward)</summary>
            public float speed; public Vector2 travel;
            /// <summary>normalised time of the left foot's touch-down (phase-syncs gait clips)</summary>
            public float phase0;
            /// <summary>per frame: bit 0 = the left foot planted, bit 1 = the right ('0'..'3')</summary>
            public string contact;
            /// <summary>per frame: summed joint rotation since the previous frame / since frame 0 (radians), the hips' height (leg lengths)</summary>
            public float[] act, dev, hipsY;
            public int Frames => contact?.Length ?? 0;
        }

        public List<Info> clips = new List<Info>();

        static ZuClipSet cached;
        static bool tried;
        public static ZuClipSet Get()
        {
            if (cached == null && !tried) { tried = true; StartupClock.Mark("clip set loading"); cached = Resources.Load<ZuClipSet>("ZUAnim/ZuClipSet"); StartupClock.Mark("clip set loaded"); }
            return cached;
        }
    }
}
