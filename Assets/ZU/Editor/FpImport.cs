// First-person arm animation: `zu_import_fp` imports the per-hero viewmodel libraries (Assets/ZU/Art/Anim/FP/fp_<hero>.fbx,
// converted from the TS game's public/anim/fp_<hero>.glb by tools/blender/glb2fbx.py in anim mode: the hero's own
// 68-bone rig, no mesh, one action per viewmodel event) as HUMANOID clips with every root channel baked into the pose,
// then builds Assets/ZU/Art/Anim/FP/FP.controller - one state per event, all code-driven (FirstPersonView plays states
// by name, so no transitions) - and an AnimatorOverrideController per hero in Resources/ZUFp/, which is what the
// viewmodel loads. A hero without a library gets no override (FirstPersonView then poses the arms procedurally).
// It also names the Viewmodel layer the overlay camera renders.
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZU.Game.FirstPerson;

namespace ZU.EditorTools
{
    public static class FpImport
    {
        const string Dir = "Assets/ZU/Art/Anim/FP";
        const string CtrlPath = Dir + "/FP.controller";
        const string Out = "Assets/ZU/Resources/ZUFp";

        [CliCommand("zu_import_fp", "Import the first-person arm libraries (Assets/ZU/Art/Anim/FP/fp_<hero>.fbx) as humanoid clips, build the FP controller and the per-hero override controllers, name the Viewmodel layer")]
        public static string Import([CliArg("id", "one hero id; empty = all")] string id = "")
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return "no " + Dir;
            NameLayer();
            var report = new System.Collections.Generic.List<string>();
            var files = Directory.GetFiles(Dir, "fp_*.fbx").Select(p => p.Replace('\\', '/')).ToList();
            foreach (var path in files)
            {
                string hero = Path.GetFileNameWithoutExtension(path).Substring(3);
                if (!string.IsNullOrEmpty(id) && hero != id) continue;
                report.Add(AnimImport.ImportOne(path, fp: true));
            }
            var ctrl = BuildController();
            Directory.CreateDirectory(Out);
            int n = 0;
            foreach (var path in files)
            {
                string hero = Path.GetFileNameWithoutExtension(path).Substring(3);
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
                if (clips.Count == 0) { report.Add(hero + ": no clips"); continue; }
                string op = $"{Out}/fp_{hero}.overrideController";
                var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(op);
                if (oc == null) { oc = new AnimatorOverrideController(ctrl); AssetDatabase.CreateAsset(oc, op); }
                oc.runtimeAnimatorController = ctrl;
                var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
                int found = 0;
                foreach (var ph in ctrl.animationClips.Distinct())
                {
                    var c = clips.FirstOrDefault(x => x.name == ph.name || x.name.EndsWith("|" + ph.name));
                    if (c != null) found++;
                    list.Add(new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(ph, c));   // (null: the placeholder stays - FirstPersonView checks Has())
                }
                oc.ApplyOverrides(list);
                EditorUtility.SetDirty(oc); n++;
                report.Add($"{hero}: {found}/{FpClips.NAMES.Length} events");
            }
            AssetDatabase.SaveAssets();
            return $"{n} override controller(s) in {Out}; " + string.Join(" | ", report);
        }

        [CliCommand("zu_reimport_heroes", "Re-import the hero FBX files (after an import-settings change, e.g. readable meshes for the viewmodel); --id for one hero")]
        public static string ReimportHeroes([CliArg("id", "one hero id; empty = all")] string id = "")
        {
            int n = 0;
            foreach (var path in Directory.GetFiles("Assets/ZU/Art/Heroes", "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                if (!string.IsNullOrEmpty(id) && !path.Contains("/" + id + "/")) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); n++;
            }
            AssetDatabase.SaveAssets();
            return n + " FBX re-imported";
        }

        /// <summary>the base controller: a state per event name holding an empty placeholder clip of that name (the
        /// override swaps the hero's clip in), a Rate float as every state's speed multiplier</summary>
        static AnimatorController BuildController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlPath);
            if (ctrl != null) return ctrl;
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            ctrl.AddParameter(FpClips.RATE, AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var name in FpClips.NAMES)
            {
                var ph = new AnimationClip { name = name };
                var s = new AnimationClipSettings { loopTime = name == "fp_idle" || name == "fp_beam" };
                AnimationUtility.SetAnimationClipSettings(ph, s);
                AssetDatabase.AddObjectToAsset(ph, ctrl);
                var st = sm.AddState(name); st.motion = ph; st.speedParameterActive = true; st.speedParameter = FpClips.RATE;
                if (name == "fp_idle") sm.defaultState = st;
            }
            var p = ctrl.parameters; p[0].defaultFloat = 1; ctrl.parameters = p;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        /// <summary>the layer the viewmodel lives on, named so the editor shows it (the runtime only needs the index)</summary>
        static void NameLayer()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var el = layers.GetArrayElementAtIndex(FirstPersonView.LAYER);
            if (el.stringValue != "Viewmodel") { el.stringValue = "Viewmodel"; tm.ApplyModifiedProperties(); }
        }
    }
}
