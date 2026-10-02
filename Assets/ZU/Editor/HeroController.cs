// `zu_build_controller`: the shared humanoid AnimatorController every hero animates with (Assets/ZU/Art/Anim/Hero.controller):
//   Base layer   - Locomotion: a 2D freeform blend of idle / walk / jog / run in every direction (Tripo text-to-motion
//                  gaits, the right-hand strafes mirrored from the left), Air (jump start -> fall loop), Land, Fly,
//                  Stun, Dead.
//   Upper layer  - (upper-body mask) Shoot / Cast / Melee / Hit one-shots over whatever the legs are doing.
// HeroView drives the parameters from the simulation every frame.
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZU.Game;

namespace ZU.EditorTools
{
    public static class HeroController
    {
        const string Anim = "Assets/ZU/Art/Anim";
        const string CtrlPath = Anim + "/Hero.controller";
        const string LibPath = "Assets/ZU/Resources/ZUHeroLibrary.asset";

        static AnimationClip Clip(string file, string take)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath($"{Anim}/{file}.fbx").OfType<AnimationClip>().FirstOrDefault(x => x.name.EndsWith("|" + take) || x.name == take);
            if (c == null) Debug.LogWarning($"ZU: clip {take} not found in {file}");
            return c;
        }

        [CliCommand("zu_build_controller", "Build the shared hero AnimatorController (locomotion blend, air, land, fly, stun, dead + upper-body actions) and register it in the hero library")]
        public static string Build()
        {
            AssetDatabase.DeleteAsset(CtrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            foreach (var p in new[] { "VelX", "VelZ", "Speed", "VelY" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var p in new[] { "Grounded", "Dead", "Stun", "Fly" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Bool);
            foreach (var p in new[] { "Jump", "Land", "Shoot", "Cast", "Melee", "Hit" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Trigger);

            // ---------------- base layer
            var sm = ctrl.layers[0].stateMachine;
            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.FreeformCartesian2D, blendParameter = "VelX", blendParameterY = "VelZ", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            void Add(string take, float x, float z, bool mirror = false, float speed = 1)
            {
                var c = Clip("Tripo", take); if (c == null) return;
                tree.AddChild(c, new Vector2(x, z));
                var ch = tree.children; ch[ch.Length - 1].mirror = mirror; ch[ch.Length - 1].timeScale = speed; tree.children = ch;
            }
            Add("TR_Idle", 0, 0);
            Add("TR_Walk_Fwd", 0, 1.6f); Add("TR_Jog_Fwd", 0, 3.8f); Add("TR_Run_Fwd", 0, 6.2f);
            Add("TR_Walk_Bwd", 0, -1.5f); Add("TR_Jog_Bwd", 0, -3.5f); Add("TR_Run_Bwd", 0, -5.6f);
            Add("TR_Walk_Left", -1.5f, 0); Add("TR_Jog_Left", -3.6f, 0); Add("TR_Run_Left", -5.8f, 0);
            Add("TR_Walk_Left", 1.5f, 0, true); Add("TR_Jog_Left", 3.6f, 0, true); Add("TR_Run_Left", 5.8f, 0, true);
            var loco = sm.AddState("Locomotion"); loco.motion = tree; sm.defaultState = loco;
            var jump = sm.AddState("JumpStart"); jump.motion = Clip("Tripo", "TR_Jump_Start");
            var air = sm.AddState("Air"); air.motion = Clip("UAL1", "Jump_Loop");
            var land = sm.AddState("Land"); land.motion = Clip("Tripo", "TR_Jump_Land"); land.speed = 1.6f;
            var fly = sm.AddState("Fly"); fly.motion = Clip("UAL2", "NinjaJump_Idle_Loop");
            var stun = sm.AddState("Stun"); stun.motion = Clip("Tripo", "TR_Stun_Idle");
            var dead = sm.AddState("Dead"); dead.motion = Clip("Mixamo/Mixamo", "MX_Death_Back") ?? Clip("UAL1", "Death01");

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur, bool exit = false)
            {
                var t = from.AddTransition(to); t.duration = dur; t.hasExitTime = exit; t.exitTime = 0.85f; t.hasFixedDuration = true; return t;
            }
            AnimatorStateTransition Any(AnimatorState to, float dur)
            {
                var t = sm.AddAnyStateTransition(to); t.duration = dur; t.hasExitTime = false; t.hasFixedDuration = true; t.canTransitionToSelf = false; return t;
            }
            Any(dead, 0.12f).AddCondition(AnimatorConditionMode.If, 0, "Dead");
            var toStun = Any(stun, 0.1f); toStun.AddCondition(AnimatorConditionMode.If, 0, "Stun"); toStun.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            T(stun, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Stun");
            T(dead, loco, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var j = T(loco, jump, 0.06f); j.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(jump, air, 0.15f, exit: true);
            var fall = T(loco, air, 0.25f); fall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); fall.AddCondition(AnimatorConditionMode.Less, -2.5f, "VelY");
            T(air, land, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(jump, land, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Land");
            var l2 = T(land, loco, 0.18f, exit: true); l2.exitTime = 0.45f;
            var mv = T(land, loco, 0.12f); mv.AddCondition(AnimatorConditionMode.Greater, 2.5f, "Speed");
            foreach (var from in new[] { loco, air, jump }) T(from, fly, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Fly");
            var unfly = T(fly, air, 0.25f); unfly.AddCondition(AnimatorConditionMode.IfNot, 0, "Fly");

            // ---------------- upper-body action layer
            var mask = new AvatarMask { name = "UpperBody" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            }
            AssetDatabase.AddObjectToAsset(mask, ctrl);
            ctrl.AddLayer("Upper");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask; layers[1].defaultWeight = 1; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var up = ctrl.layers[1].stateMachine;
            var empty = up.AddState("Empty"); up.defaultState = empty;
            void Action(string name, string trigger, AnimationClip clip, float speed = 1, float exitAt = 0.8f)
            {
                var s = up.AddState(name); s.motion = clip; s.speed = speed;
                var t = up.AddAnyStateTransition(s); t.duration = 0.05f; t.hasExitTime = false; t.hasFixedDuration = true; t.canTransitionToSelf = true;
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
                var back = s.AddTransition(empty); back.hasExitTime = true; back.exitTime = exitAt; back.duration = 0.2f; back.hasFixedDuration = true;
            }
            Action("Shoot", "Shoot", Clip("Kevin/Kevin", "KI_Pistol_Shoot"), 1.4f, 0.6f);
            Action("Cast", "Cast", Clip("Kevin/Kevin", "KI_Cast_Direct_1H"), 1.3f);
            Action("Melee", "Melee", Clip("UAL1", "Punch_Jab"), 1.6f, 0.7f);
            Action("Hit", "Hit", Clip("Kevin/Kevin", "KI_Hit_Chest"), 1.2f, 0.7f);
            EditorUtility.SetDirty(ctrl);

            // ---------------- register in the hero library (prefabs found under Art/Heroes)
            var lib = AssetDatabase.LoadAssetAtPath<HeroLibrary>(LibPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<HeroLibrary>(); AssetDatabase.CreateAsset(lib, LibPath); }
            lib.baseController = ctrl;
            foreach (var p in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ZU/Art/Heroes" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && System.IO.Path.GetFileNameWithoutExtension(p) == go.name) lib.Set(go.name, go);
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"{CtrlPath}: locomotion {tree.children.Length} motions, {sm.states.Length} base states, {up.states.Length} upper states; library has {lib.heroes.Count} hero(es): {string.Join(",", lib.heroes.Select(h => h.id))}";
        }
    }
}
