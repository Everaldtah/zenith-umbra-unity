// A humanoid clip played left <-> right mirrored (the TS ClipLibrary mirrorClip, which swaps the sides of a baked PoseClip
// and reflects it across the YZ plane): an animation job between a clip and the layer's mixer that swaps every Left / Right
// muscle pair, negates the central side-to-side muscles (spine, chest, neck, head and jaw "Left-Right" / "Twist"), and
// reflects the body: position x -> -x, rotation (x, y, z, w) -> (x, -y, -z, w). The library uses it to fill the strafe
// directions a gait has only one side of.
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZU.Game.Anim
{
    [BurstCompile]
    public struct ClipMirrorJob : IAnimationJob
    {
        public NativeArray<MuscleHandle> muscles;
        public NativeArray<int> partner;
        public NativeArray<float> sign;
        public NativeArray<float> buf;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!stream.isHumanStream) return;
            var h = stream.AsHuman();
            int n = muscles.Length;
            for (int i = 0; i < n; i++) buf[i] = h.GetMuscle(muscles[i]);
            for (int i = 0; i < n; i++) h.SetMuscle(muscles[i], sign[i] * buf[partner[i]]);
            var p = h.bodyLocalPosition; h.bodyLocalPosition = new Vector3(-p.x, p.y, p.z);
            var q = h.bodyLocalRotation; h.bodyLocalRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
        }
    }

    /// <summary>the mirror job's tables (shared by every mirrored clip; built once)</summary>
    public static class ClipMirror
    {
        static MuscleHandle[] handles;
        static int[] partners;
        static float[] signs;

        static void Build()
        {
            if (handles != null) return;
            handles = new MuscleHandle[MuscleHandle.muscleHandleCount];
            MuscleHandle.GetMuscleHandles(handles);
            int n = handles.Length;
            var names = new string[n];
            for (int i = 0; i < n; i++) names[i] = handles[i].name;
            partners = new int[n]; signs = new float[n];
            for (int i = 0; i < n; i++)
            {
                string nm = names[i];
                bool left = nm.StartsWith("Left"), right = nm.StartsWith("Right");
                partners[i] = i; signs[i] = 1;
                if (left || right)
                {
                    string other = left ? "Right" + nm.Substring(4) : "Left" + nm.Substring(5);
                    int j = System.Array.IndexOf(names, other);
                    if (j >= 0) partners[i] = j;
                }
                else if (nm.Contains("Left-Right") || nm.Contains("Twist")) signs[i] = -1;    // a central muscle turning one way turns the other
            }
        }

        /// <summary>a mirror job playable fed by `input` (its tables are freed with Dispose(job))</summary>
        public static AnimationScriptPlayable Create(PlayableGraph graph, Playable input, out ClipMirrorJob job)
        {
            Build();
            int n = handles.Length;
            job = new ClipMirrorJob
            {
                muscles = new NativeArray<MuscleHandle>(handles, Allocator.Persistent),
                partner = new NativeArray<int>(partners, Allocator.Persistent),
                sign = new NativeArray<float>(signs, Allocator.Persistent),
                buf = new NativeArray<float>(n, Allocator.Persistent),
            };
            var sp = AnimationScriptPlayable.Create(graph, job, 1);
            graph.Connect(input, 0, sp, 0);
            sp.SetInputWeight(0, 1);
            return sp;
        }

        public static void Dispose(ClipMirrorJob job)
        {
            if (job.muscles.IsCreated) job.muscles.Dispose();
            if (job.partner.IsCreated) job.partner.Dispose();
            if (job.sign.IsCreated) job.sign.Dispose();
            if (job.buf.IsCreated) job.buf.Dispose();
        }
    }
}
