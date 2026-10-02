// `zu_bake_clips`: every humanoid clip of the animation libraries (Assets/ZU/Art/Anim/*.fbx, not FP/) sampled on a reference
// hero and measured the way the TS measures its PoseClips when it bakes them (render/Retarget.ts analyse): the planted feet
// per frame (a foot within 5 cm per leg length of its lowest point, then only while it slides back with the stance), an
// in-place gait's speed and direction (the median backward slide of the planted foot), the phase anchor (the left foot's
// touch-down), plus the per-frame motion curves ClipLibrary's trimAction cuts one-shots by. Written to
// Resources/ZUAnim/ZuClipSet.asset (ZU.Game.Anim.ZuClipSet), which also keeps every clip in a player build.
// Positions are measured in the TS canonical frame: leg lengths, x = the character's LEFT (Unity's -x), z = forward.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using ZU.Game;
using ZU.Game.Anim;

namespace ZU.EditorTools
{
    public static class ClipBake
    {
        const string Dir = "Assets/ZU/Art/Anim", OutDir = "Assets/ZU/Resources/ZUAnim", OutPath = OutDir + "/ZuClipSet.asset";
        const float FPS = 30;
        // the TS RT bones (Rig.ts), as humanoid bones
        static readonly (string name, HumanBodyBones bone)[] RT =
        {
            ("hips", HumanBodyBones.Hips), ("spine", HumanBodyBones.Spine), ("chest", HumanBodyBones.Chest), ("neck", HumanBodyBones.Neck), ("head", HumanBodyBones.Head),
            ("shoulder_L", HumanBodyBones.LeftShoulder), ("upperarm_L", HumanBodyBones.LeftUpperArm), ("forearm_L", HumanBodyBones.LeftLowerArm), ("hand_L", HumanBodyBones.LeftHand),
            ("shoulder_R", HumanBodyBones.RightShoulder), ("upperarm_R", HumanBodyBones.RightUpperArm), ("forearm_R", HumanBodyBones.RightLowerArm), ("hand_R", HumanBodyBones.RightHand),
            ("thigh_L", HumanBodyBones.LeftUpperLeg), ("shin_L", HumanBodyBones.LeftLowerLeg), ("foot_L", HumanBodyBones.LeftFoot),
            ("thigh_R", HumanBodyBones.RightUpperLeg), ("shin_R", HumanBodyBones.RightLowerLeg), ("foot_R", HumanBodyBones.RightFoot),
        };
        const int HIPS = 0, FOOT_L = 15, FOOT_R = 18;

        [CliCommand("zu_bake_clips", "Measure every humanoid clip (contacts, gait speed / direction, phase anchor, motion curves) into Resources/ZUAnim/ZuClipSet.asset")]
        public static string Bake([CliArg("hero", "the reference hero whose avatar samples the clips")] string hero = "kaien")
        {
            var lib = HeroLibrary.Get();
            var entry = lib != null ? lib.Find(hero) : null;
            if (entry == null) return "no hero prefab " + hero;
            var go = Object.Instantiate(entry.prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = go.GetComponentInChildren<Animator>(true);
            var report = new List<string>();
            var graph = PlayableGraph.Create("zu clip bake");
            try
            {
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return hero + ": no humanoid avatar";
                anim.runtimeAnimatorController = null; anim.applyRootMotion = false;
                var bones = new Transform[RT.Length];
                for (int i = 0; i < RT.Length; i++)
                {
                    bones[i] = anim.GetBoneTransform(RT[i].bone);
                    if (bones[i] == null && RT[i].bone == HumanBodyBones.Chest) bones[i] = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
                    if (bones[i] == null && RT[i].bone == HumanBodyBones.LeftShoulder) bones[i] = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    if (bones[i] == null && RT[i].bone == HumanBodyBones.RightShoulder) bones[i] = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    if (bones[i] == null) return $"{hero}: no {RT[i].name} bone";
                }
                var root = go.transform;
                float legLen = Vector3.Distance(bones[13].position, bones[14].position) + Vector3.Distance(bones[14].position, bones[15].position);
                if (legLen < 1e-3f) return hero + ": no leg length";
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "bake", anim);
                var set = AssetDatabase.LoadAssetAtPath<ZuClipSet>(OutPath);
                bool fresh = set == null;
                if (fresh) { Directory.CreateDirectory(OutDir); set = ScriptableObject.CreateInstance<ZuClipSet>(); }
                set.clips.Clear();
                foreach (var path in Directory.GetFiles(Dir, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).Where(p => !p.Contains("/FP/")).OrderBy(p => p))
                {
                    string pack = Path.GetFileNameWithoutExtension(path);
                    int n = 0;
                    foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                    {
                        if (!clip.humanMotion) continue;
                        var cp = AnimationClipPlayable.Create(graph, clip);
                        cp.SetApplyFootIK(false);
                        output.SetSourcePlayable(cp);
                        set.clips.Add(Measure(clip, pack, graph, cp, root, bones, legLen));
                        cp.Destroy();
                        n++;
                    }
                    report.Add($"{pack} {n}");
                }
                if (fresh) AssetDatabase.CreateAsset(set, OutPath);
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                int gaits = set.clips.Count(c => c.loop && c.speed >= 0.2f);
                return $"{OutPath}: {set.clips.Count} clips ({string.Join(", ", report)}); {gaits} travelling loops; leg {legLen:0.00} m on {hero}";
            }
            finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(go); }
        }

        static ZuClipSet.Info Measure(AnimationClip clip, string pack, PlayableGraph graph, AnimationClipPlayable cp, Transform root, Transform[] bones, float legLen)
        {
            string name = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
            float len = Mathf.Max(1f / FPS, clip.length);
            int n = Mathf.Max(2, Mathf.RoundToInt(len * FPS) + 1), last = n - 1, NB = RT.Length;
            float fps = last / len;
            var p = new Vector3[n * NB]; var q = new Quaternion[n * NB];
            var inv = Quaternion.Inverse(root.rotation);
            for (int f = 0; f < n; f++)
            {
                cp.SetTime(len * f / last);
                graph.Evaluate(0);
                for (int i = 0; i < NB; i++)
                {
                    var u = root.InverseTransformPoint(bones[i].position) / legLen;
                    p[f * NB + i] = new Vector3(-u.x, u.y, u.z);                       // the TS canonical frame: x = left
                    q[f * NB + i] = inv * bones[i].rotation;
                }
            }
            Vector3 P(int f, int i) => p[f * NB + i];
            var info = new ZuClipSet.Info { clip = clip, name = name, pack = pack, loop = clip.isLooping, fps = fps, length = len };
            // contacts: a foot within a few centimetres (per leg length) of its lowest point
            var contact = new byte[n * 2];
            float[] minY = { float.PositiveInfinity, float.PositiveInfinity };
            for (int f = 0; f < n; f++) for (int s = 0; s < 2; s++) minY[s] = Mathf.Min(minY[s], P(f, s == 1 ? FOOT_R : FOOT_L).y);
            for (int f = 0; f < n; f++) for (int s = 0; s < 2; s++) contact[f * 2 + s] = (byte)(P(f, s == 1 ? FOOT_R : FOOT_L).y < minY[s] + 0.05f ? 1 : 0);
            // in-place gait: a planted foot slides backwards (relative to the body) at the travel speed
            if (info.loop)
            {
                var v = new List<Vector3>();
                for (int f = 0; f < last; f++)
                    for (int s = 0; s < 2; s++)
                    {
                        if (contact[f * 2 + s] == 0 || contact[(f + 1) * 2 + s] == 0) continue;
                        int b = s == 1 ? FOOT_R : FOOT_L;
                        var d = (P(f + 1, b) - P(f, b)) * fps; d.y = 0;
                        v.Add(d);
                    }
                var mean = Vector3.zero; foreach (var d in v) mean += d; mean /= Mathf.Max(1, v.Count);
                if (v.Count >= 4 && mean.magnitude > 0.15f)
                {
                    var t = (-mean).normalized;
                    var along = v.Select(d => -Vector3.Dot(d, t)).OrderBy(x => x).ToList();
                    info.speed = along[along.Count / 2];
                    info.travel = new Vector2(t.x, t.z);
                }
            }
            // a moving clip's planted foot also moves WITH the stance: low feet already swinging forward aren't planted
            if (info.speed > 0.2f)
            {
                var tv = new Vector3(info.travel.x, 0, info.travel.y);
                for (int s = 0; s < 2; s++)
                {
                    int b = s == 1 ? FOOT_R : FOOT_L;
                    var ok = new byte[n];
                    for (int f = 0; f < n; f++)
                    {
                        int f0 = Mathf.Max(0, f - 1), f1 = Mathf.Min(last, f + 1);
                        var vv = (P(f1, b) - P(f0, b)) * (fps / Mathf.Max(1, f1 - f0)); vv.y = 0;
                        bool stance = -Vector3.Dot(vv, tv) > 0.6f * info.speed && (vv - tv * Vector3.Dot(vv, tv)).magnitude < 0.5f * info.speed;
                        ok[f] = (byte)(contact[f * 2 + s] != 0 && stance ? 1 : 0);
                    }
                    for (int f = 0; f < n; f++) contact[f * 2 + s] = ok[f];
                }
            }
            // gait phase anchor: the left foot touching down
            for (int f = 1; f < n; f++) if (contact[f * 2] != 0 && contact[(f - 1) * 2] == 0) { info.phase0 = (float)f / last; break; }
            var cs = new char[n];
            for (int f = 0; f < n; f++) cs[f] = (char)('0' + contact[f * 2] + 2 * contact[f * 2 + 1]);
            info.contact = new string(cs);
            // the motion curves: summed joint rotation since the previous frame and since frame 0 (TS activity / the key-pose
            // deviation), the hips' height (a jump's take-off crouch)
            info.act = new float[n]; info.dev = new float[n]; info.hipsY = new float[n];
            for (int f = 0; f < n; f++)
            {
                float a = 0, d = 0;
                for (int i = 0; i < NB; i++)
                {
                    if (f > 0) a += 2 * Mathf.Acos(Mathf.Min(1, Mathf.Abs(Quaternion.Dot(q[(f - 1) * NB + i], q[f * NB + i]))));
                    d += 2 * Mathf.Acos(Mathf.Min(1, Mathf.Abs(Quaternion.Dot(q[i], q[f * NB + i]))));
                }
                info.act[f] = a; info.dev[f] = d; info.hipsY[f] = P(f, HIPS).y;
            }
            return info;
        }
    }
}
