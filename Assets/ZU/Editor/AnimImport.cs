// The animation libraries (Assets/ZU/Art/Anim/*.fbx and the licence-restricted Mixamo/ Kevin/ folders, converted from the
// TS game's public/anim GLBs by tools/blender/glb2fbx.py) imported as HUMANOID clips: each library's skeleton gets its own
// humanoid Avatar (HumanRig), the FBX is re-imported against it, and loops are marked - so Mecanim retargets any clip onto
// any hero's avatar.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class AnimImport
    {
        const string Dir = "Assets/ZU/Art/Anim";
        static readonly string[] LOOPS = { "loop", "idle", "walk", "jog", "run", "sprint", "strafe", "crouch_fwd", "crouch_idle", "swim", "fly", "hover", "fall", "dance" };

        [CliCommand("zu_import_anims", "Import every animation library FBX under Assets/ZU/Art/Anim as humanoid clips (one avatar per skeleton)")]
        public static string ImportAll()
        {
            var report = new List<string>();
            // (the first-person libraries under FP/ have their own import: FpImport, zu_import_fp)
            foreach (var path in Directory.GetFiles(Dir, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).Where(p => !p.Contains("/FP/")))
                report.Add(ImportOne(path));
            AssetDatabase.SaveAssets();
            return string.Join(" | ", report);
        }

        /// <summary>fp: a first-person library - every clip plays in place on a rig parked at the eye (all root motion baked
        /// into the pose), only the idle and the beam loop</summary>
        internal static string ImportOne(string path, bool fp = false)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            // first pass: generic, to read the skeleton
            if (imp.animationType != ModelImporterAnimationType.Generic || imp.sourceAvatar != null)
            {
                imp.animationType = ModelImporterAnimationType.Generic; imp.sourceAvatar = null; imp.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var map = HumanRig.MapFor(model.transform);
            if (map == null) return name + ": unknown skeleton";
            var avatar = HumanRig.BuildAvatar(model, map, out var rep);
            if (avatar == null || !avatar.isValid) return name + ": avatar failed " + rep;
            avatar.name = name + "_avatar";
            string avPath = Path.GetDirectoryName(path).Replace('\\', '/') + "/" + name + "_avatar.asset";
            avatar = HumanRig.SaveAvatar(avatar, avPath);
            // second pass: humanoid against that avatar, every take a clip, loops looped
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            imp.sourceAvatar = avatar;
            imp.importAnimation = true;
            imp.animationCompression = ModelImporterAnimationCompression.Optimal;
            imp.resampleCurves = true;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name.ToLowerInvariant();
                bool loop = fp ? (n.EndsWith("fp_idle") || n.EndsWith("fp_beam")) : LOOPS.Any(k => n.Contains(k)) && !n.Contains("start") && !n.Contains("land") && !n.Contains("_to_");
                c.loopTime = loop; c.loopPose = loop;
                // the simulation moves the hero, so clips play in place: horizontal travel goes to the root (centre of mass) and
                // is discarded there (HeroView: applyRootMotion off) - baked into the pose, a jog or slide would carry the body
                // metres from where the hero is. Height and facing stay in the pose (jumps, crouches, turns still read).
                c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = fp;
                c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = fp;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            int n2 = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview__"));
            return $"{name}: {rep}, {n2} humanoid clips ({clips.Count(c => c.loopTime)} loops)";
        }
    }
}
