// Humanoid avatars for every skeleton in the project: the heroes' own rig (rig_hero / Tripo rigs: hips, spine,
// upperarm_L ...) and the four animation libraries' skeletons (Unreal-mannequin UAL, Mixamo, Kevin Iglesias, CMU/DAZ).
// Built with AvatarBuilder from the model's own bind pose, after posing the arms into a T-pose (what the avatar
// configurator's "Enforce T-Pose" does), so Mecanim's muscle space is the same on every rig and any clip plays on any hero.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class HumanRig
    {
        /// <summary>Unity human bone name -> this skeleton's transform name (only the bones it has)</summary>
        public static readonly Dictionary<string, string> ZU = Build(new[]
        {
            "Hips:hips", "Spine:spine", "Chest:chest", "Neck:neck", "Head:head",
            "LeftShoulder:shoulder_L", "LeftUpperArm:upperarm_L", "LeftLowerArm:forearm_L", "LeftHand:hand_L",
            "RightShoulder:shoulder_R", "RightUpperArm:upperarm_R", "RightLowerArm:forearm_R", "RightHand:hand_R",
            "LeftUpperLeg:thigh_L", "LeftLowerLeg:shin_L", "LeftFoot:foot_L", "LeftToes:toe_L",
            "RightUpperLeg:thigh_R", "RightLowerLeg:shin_R", "RightFoot:foot_R", "RightToes:toe_R",
        }, Fingers("{f}{n}_{s}", "thumb", "index", "middle", "ring", "pinky", "L", "R"));

        public static readonly Dictionary<string, string> UE = Build(new[]
        {
            "Hips:pelvis", "Spine:spine_01", "Chest:spine_02", "UpperChest:spine_03", "Neck:neck_01", "Head:Head",
            "LeftShoulder:clavicle_l", "LeftUpperArm:upperarm_l", "LeftLowerArm:lowerarm_l", "LeftHand:hand_l",
            "RightShoulder:clavicle_r", "RightUpperArm:upperarm_r", "RightLowerArm:lowerarm_r", "RightHand:hand_r",
            "LeftUpperLeg:thigh_l", "LeftLowerLeg:calf_l", "LeftFoot:foot_l", "LeftToes:ball_l",
            "RightUpperLeg:thigh_r", "RightLowerLeg:calf_r", "RightFoot:foot_r", "RightToes:ball_r",
        }, Fingers("{f}_0{n}_{s}", "thumb", "index", "middle", "ring", "pinky", "l", "r"));

        public static readonly Dictionary<string, string> MIXAMO = Build(new[]
        {
            "Hips:mixamorig:Hips", "Spine:mixamorig:Spine", "Chest:mixamorig:Spine1", "UpperChest:mixamorig:Spine2", "Neck:mixamorig:Neck", "Head:mixamorig:Head",
            "LeftShoulder:mixamorig:LeftShoulder", "LeftUpperArm:mixamorig:LeftArm", "LeftLowerArm:mixamorig:LeftForeArm", "LeftHand:mixamorig:LeftHand",
            "RightShoulder:mixamorig:RightShoulder", "RightUpperArm:mixamorig:RightArm", "RightLowerArm:mixamorig:RightForeArm", "RightHand:mixamorig:RightHand",
            "LeftUpperLeg:mixamorig:LeftUpLeg", "LeftLowerLeg:mixamorig:LeftLeg", "LeftFoot:mixamorig:LeftFoot", "LeftToes:mixamorig:LeftToeBase",
            "RightUpperLeg:mixamorig:RightUpLeg", "RightLowerLeg:mixamorig:RightLeg", "RightFoot:mixamorig:RightFoot", "RightToes:mixamorig:RightToeBase",
        }, Fingers("mixamorig:{S}Hand{F}{n}", "Thumb", "Index", "Middle", "Ring", "Pinky", "Left", "Right"));

        public static readonly Dictionary<string, string> KEVIN = Build(new[]
        {
            "Hips:B-hips", "Spine:B-spine", "Chest:B-chest", "Neck:B-neck", "Head:B-head", "Jaw:B-jaw",
            "LeftShoulder:B-shoulder.L", "LeftUpperArm:B-upperArm.L", "LeftLowerArm:B-forearm.L", "LeftHand:B-hand.L",
            "RightShoulder:B-shoulder.R", "RightUpperArm:B-upperArm.R", "RightLowerArm:B-forearm.R", "RightHand:B-hand.R",
            "LeftUpperLeg:B-thigh.L", "LeftLowerLeg:B-shin.L", "LeftFoot:B-foot.L", "LeftToes:B-toe.L",
            "RightUpperLeg:B-thigh.R", "RightLowerLeg:B-shin.R", "RightFoot:B-foot.R", "RightToes:B-toe.R",
        }, Fingers("B-{fk}0{n}.{s}", "thumb", "indexFinger", "middleFinger", "ringFinger", "pinky", "L", "R"));

        public static readonly Dictionary<string, string> CMU = Build(new[]
        {
            "Hips:hip", "Spine:abdomen", "Chest:chest", "Neck:neck", "Head:head",
            "LeftShoulder:lCollar", "LeftUpperArm:lShldr", "LeftLowerArm:lForeArm", "LeftHand:lHand",
            "RightShoulder:rCollar", "RightUpperArm:rShldr", "RightLowerArm:rForeArm", "RightHand:rHand",
            "LeftUpperLeg:lThigh", "LeftLowerLeg:lShin", "LeftFoot:lFoot",
            "RightUpperLeg:rThigh", "RightLowerLeg:rShin", "RightFoot:rFoot",
            // two-joint fingers: proximal + intermediate
            "Left Thumb Proximal:lThumb1", "Left Thumb Intermediate:lThumb2", "Left Index Proximal:lIndex1", "Left Index Intermediate:lIndex2",
            "Left Middle Proximal:lMid1", "Left Middle Intermediate:lMid2", "Left Ring Proximal:lRing1", "Left Ring Intermediate:lRing2",
            "Left Little Proximal:lPinky1", "Left Little Intermediate:lPinky2",
            "Right Thumb Proximal:rThumb1", "Right Thumb Intermediate:rThumb2", "Right Index Proximal:rIndex1", "Right Index Intermediate:rIndex2",
            "Right Middle Proximal:rMid1", "Right Middle Intermediate:rMid2", "Right Ring Proximal:rRing1", "Right Ring Intermediate:rRing2",
            "Right Little Proximal:rPinky1", "Right Little Intermediate:rPinky2",
        }, new string[0]);

        /// <summary>pick the map whose hips bone this hierarchy has</summary>
        public static Dictionary<string, string> MapFor(Transform root)
        {
            var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            foreach (var m in new[] { ZU, UE, MIXAMO, KEVIN, CMU }) if (names.Contains(m["Hips"])) return m;
            return null;
        }

        static Dictionary<string, string> Build(string[] body, string[] fingers)
        {
            var d = new Dictionary<string, string>();
            foreach (var e in body.Concat(fingers)) { int i = e.IndexOf(':'); d[e.Substring(0, i)] = e.Substring(i + 1); }
            return d;
        }

        /// <summary>finger names from a pattern: {f} finger (as given), {F} capitalised, {fk} the lib's own key, {n} joint 1..3, {s} side, {S} side word</summary>
        static string[] Fingers(string pattern, string thumb, string index, string middle, string ring, string little, string left, string right)
        {
            var output = new List<string>();
            var fingers = new[] { ("Thumb", thumb), ("Index", index), ("Middle", middle), ("Ring", ring), ("Little", little) };
            var joints = new[] { "Proximal", "Intermediate", "Distal" };
            foreach (var (sideU, side) in new[] { ("Left", left), ("Right", right) })
                foreach (var (fu, f) in fingers)
                    for (int n = 1; n <= 3; n++)
                    {
                        string name = pattern.Replace("{fk}", f).Replace("{f}", f).Replace("{F}", f).Replace("{n}", n.ToString()).Replace("{s}", side).Replace("{S}", side);
                        output.Add($"{sideU} {fu} {joints[n - 1]}:{name}");
                    }
            return output.ToArray();
        }

        /// <summary>save an avatar asset, replacing an existing one in place: same GUID and file id, so the prefabs and model
        /// importers that point at it stay connected (delete-and-recreate leaves loaded prefabs holding a dead reference)</summary>
        public static Avatar SaveAvatar(Avatar avatar, string path)
        {
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Avatar>(path);
            if (existing == null) { UnityEditor.AssetDatabase.CreateAsset(avatar, path); return avatar; }
            string name = existing.name;
            UnityEditor.EditorUtility.CopySerialized(avatar, existing);
            existing.name = name;
            UnityEditor.EditorUtility.SetDirty(existing);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(existing);
            UnityEngine.Object.DestroyImmediate(avatar);
            return existing;
        }

        /// <summary>
        /// A humanoid Avatar for `model` (an instance of the imported rig, in its bind pose). The arms and legs are posed
        /// into a T-pose on a temporary copy first (upper arms horizontal along the body's left/right, forearms and hands
        /// in line, legs straight down), and the skeleton is recorded from that copy.
        /// </summary>
        public static Avatar BuildAvatar(GameObject model, Dictionary<string, string> map, out string report)
        {
            var copy = UnityEngine.Object.Instantiate(model);
            copy.name = model.name;          // the skeleton root must carry the model's own name
            try
            {
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var byName = new Dictionary<string, Transform>();
                foreach (var t in copy.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                Transform B(string human) => map.TryGetValue(human, out var n) && byName.TryGetValue(n, out var t) ? t : null;
                var missing = new[] { "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" }
                    .Where(h => B(h) == null).ToList();
                if (missing.Count > 0) { report = "missing required bones: " + string.Join(", ", missing); return null; }

                // the body's frame from the bind pose: up = hips -> head, side = left thigh -> right thigh
                Vector3 up = (B("Head").position - B("Hips").position).normalized;
                Vector3 right = (B("RightUpperLeg").position - B("LeftUpperLeg").position); right = (right - Vector3.Dot(right, up) * up).normalized;
                // T-pose: each arm segment points straight out sideways, each leg segment straight down
                void Aim(string bone, string child, Vector3 dir)
                {
                    Transform a = B(bone), c = B(child);
                    if (a == null || c == null) return;
                    var cur = c.position - a.position;
                    if (cur.sqrMagnitude < 1e-8f) return;
                    a.rotation = Quaternion.FromToRotation(cur, dir) * a.rotation;
                }
                Aim("LeftUpperArm", "LeftLowerArm", -right); Aim("LeftLowerArm", "LeftHand", -right);
                Aim("RightUpperArm", "RightLowerArm", right); Aim("RightLowerArm", "RightHand", right);
                Aim("LeftUpperLeg", "LeftLowerLeg", -up); Aim("LeftLowerLeg", "LeftFoot", -up);
                Aim("RightUpperLeg", "RightLowerLeg", -up); Aim("RightLowerLeg", "RightFoot", -up);

                var human = new List<HumanBone>();
                foreach (var hb in HumanTrait.BoneName)
                {
                    if (!map.TryGetValue(hb, out var n) || !byName.ContainsKey(n)) continue;
                    human.Add(new HumanBone { humanName = hb, boneName = n, limit = new HumanLimit { useDefaultValues = true } });
                }
                var skeleton = copy.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                }).ToArray();
                // the root's own entry keeps the model at the origin
                skeleton[0].position = Vector3.zero; skeleton[0].rotation = Quaternion.identity;
                var desc = new HumanDescription
                {
                    human = human.ToArray(), skeleton = skeleton,
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false,
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(copy, desc);
                report = $"{human.Count} human bones, valid {avatar.isValid}, human {avatar.isHuman}";
                return avatar;
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
    }
}
