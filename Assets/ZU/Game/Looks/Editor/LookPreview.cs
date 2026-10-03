#if UNITY_EDITOR
// `zu_look_preview`: a hero's body in the look shader (Looks/HeroLook on ZU/Hero) alone in an isolated preview scene,
// straight to PNGs - one per skin (the Unity side of the TS tests/e2e/skins_sheet.mjs Hero Viewer sheet), with the smear at
// a given speed, and Gantetsu's / Tomoe's guns firing (barrels spun up, muzzle flashes on) - so the looks can be compared
// with the TS without play mode. Reports the measured costume hues (TS zuSrc1 / zuSrc2), the neck line and ZU/Hero's
// compile messages.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using ZU.Game;
using ZU.Game.Looks;
using ZU.Sim;

namespace ZU.EditorTools
{
    public static class LookPreview
    {
        [CliCommand("zu_look_preview", "Render a hero in the look shader to <out>/<id>_<skin>.png per skin (smear, held guns firing optional) and report the measured hues")]
        public static string Run(
            [CliArg("id", "hero id (Assets/ZU/Art/Heroes/<id>/<id>.prefab)")] string id = "raijin",
            [CliArg("skins", "comma-separated skin ids, or all")] string skins = "all",
            [CliArg("out", "output folder, relative to the project")] string outDir = "Screenshots/look",
            [CliArg("yaw", "camera angle round the model, degrees (0 = from the front; TS skins_sheet: 0.5 rad)")] float yaw = 28.65f,
            [CliArg("size", "image size in pixels")] int size = 640,
            [CliArg("time", "the look clock (the energy lines), seconds")] float time = 1,
            [CliArg("speed", "the smear: the hero moving forward at this speed, m/s, like the TS viewer's swoop (smears from 12)")] float speed = 0,
            [CliArg("side", "smear sideways (to the hero's left) instead of forward")] bool side = false,
            [CliArg("clip", "a pose take, e.g. TR_Idle (searched in Assets/ZU/Art/Anim); empty = bind pose")] string clip = "TR_Idle",
            [CliArg("clipTime", "time into the clip, seconds")] float clipTime = 0.3f,
            [CliArg("held", "attach the held weapons (HeldProps)")] bool held = false,
            [CliArg("fire", "the guns firing: Gantetsu's barrels spun up and both muzzles flashing, Tomoe's crown flash")] bool fire = false,
            [CliArg("zoom", "1 = full body; 2.5 = head and shoulders")] float zoom = 1)
        {
            string path = $"Assets/ZU/Art/Heroes/{id}/{id}.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return "not found: " + path;
            var def = ZuData.Get()?.Def(id);
            if (def == null) return "no hero def: " + id;
            var report = new StringBuilder();
            var shader = Resources.Load<Material>("ZULooks/Keep/hero_ms_n")?.shader;
            if (shader == null) return "ZULooks/Keep/hero_ms_n missing (or not on a shader)";
            foreach (var m in ShaderUtil.GetShaderMessages(shader)) report.AppendLine($"ZU/Hero {m.severity}: {m.message} ({m.file}:{m.line})");

            var actor = new Actor(def, def.team ?? "zenith") { isPlayer = true, grounded = true };
            // sim frame (yaw 0 faces +z, as in the TS viewer); the look converts it to the scene's frame
            if (speed > 0) actor.vel = side ? new V3(speed, 0, 0) : new V3(0, 0, speed);
            var list = skins == "all"
                ? (ZuData.Get().Skins != null && ZuData.Get().Skins.TryGetValue(id, out var all) && all != null ? all.Select(s => s.id).ToList() : new List<string> { "classic" })
                : skins.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;          // the PNGs must show the real variants, not the cyan placeholder
            var pru = new PreviewRenderUtility();
            PlayableGraph graph = default;
            try
            {
                var go = Object.Instantiate(asset);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                pru.AddSingleGO(go);
                // the body as HeroView hands it to CharacterLook: every renderer, before the props
                var body = go.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();
                var look = new HeroLook(go.transform, body, actor);
                HeldRig heldRig = null; Fingers fingers = null;
                if (held || fire)
                {
                    var rig = new ZU.Game.FirstPerson.RigPose(go.transform);
                    heldRig = HeldRig.Attach(go, rig, def);
                    fingers = Fingers.Build(go.transform);
                }
                var animator = go.GetComponentInChildren<Animator>();
                if (!string.IsNullOrEmpty(clip) && animator != null)
                {
                    var c = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/ZU/Art/Anim" })
                        .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                        .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                        .FirstOrDefault(x => x.name.EndsWith("|" + clip) || x.name == clip);
                    if (c == null) report.AppendLine("clip not found (bind pose): " + clip);
                    else
                    {
                        graph = PlayableGraph.Create("zu_look_preview");
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var po = AnimationPlayableOutput.Create(graph, "pose", animator);
                        var play = AnimationClipPlayable.Create(graph, c);
                        play.SetApplyFootIK(true); play.SetTime(clipTime); play.SetTime(clipTime);
                        po.SetSourcePlayable(play);
                        graph.Evaluate(0);
                    }
                }
                if (heldRig != null)
                {
                    heldRig.PlacePropAtRest(); heldRig.Place();
                    if (fire)
                    {
                        // two frames 50 ms apart: the barrels turn (spin x 38 rad/s), each muzzle fired 10 ms before the second
                        actor.sv["spin1"] = 1; actor.sv["spin2"] = 1;
                        actor.anim.attackKind = "primary";
                        heldRig.UpdateState(actor, time - 0.05, true);
                        actor.anim.fireL = actor.anim.fireR = actor.anim.attackAt = time - 0.01;
                    }
                    heldRig.UpdateState(actor, time, true);
                    heldRig.UpdateDetails(actor, time);
                }
                if (fingers != null) { var g = Fingers.BaseGrips(id); fingers.Set(0, g.L); fingers.Set(1, g.R); fingers.Update(1, 1000); }

                var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.name.Contains("flash")).ToArray();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.up, Vector3.one * 2);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float h = Mathf.Max(b.size.y, 0.5f) / zoom;
                var focus = zoom > 1.5f ? new Vector3(b.center.x, b.max.y - h * 0.5f, b.center.z) : b.center;
                var cam = pru.camera;
                cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                float dist = h * 0.62f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + b.extents.z;
                var dir = Quaternion.Euler(8, 180 + yaw, 0) * Vector3.forward;
                cam.transform.position = focus - dir * dist;
                cam.transform.LookAt(focus);
                pru.lights[0].intensity = 1.25f; pru.lights[0].transform.rotation = Quaternion.Euler(35, 150 + yaw, 0);
                pru.lights[1].intensity = 0.55f; pru.lights[1].transform.rotation = Quaternion.Euler(-10, -40 + yaw, 0);
                pru.ambientColor = new Color(0.32f, 0.33f, 0.36f);

                var hues = look.SrcHues;
                report.AppendLine($"{id}: src1 {hues.x:0.00}/{hues.y:0.00} src2 {hues.z:0.00}/{hues.w:0.00}, neck line y {look.HeadY:0.00}, {body.Length} body renderer(s), smear {(speed >= HeroLook.SMEAR_FROM ? "on" : "off")}");
                var outFull = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outFull);
                foreach (var s in list)
                {
                    HeroSkin.Show(actor, s);
                    look.Update(actor, time);
                    look.Apply();
                    pru.BeginStaticPreview(new Rect(0, 0, size, size));
                    pru.Render(true);
                    var tex = pru.EndStaticPreview();
                    string name = $"{id}_{s}{(speed > 0 ? "_smear" + speed.ToString("0") : "")}{(fire ? "_fire" : held ? "_held" : "")}.png";
                    File.WriteAllBytes(Path.Combine(outFull, name), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    report.AppendLine($"  {outDir}/{name}  (skin {look.SkinId})");
                }
                HeroSkin.Show(actor, null);
            }
            finally { if (graph.IsValid()) graph.Destroy(); pru.Cleanup(); ShaderUtil.allowAsyncCompilation = prevAsync; }
            return report.ToString().TrimEnd();
        }
    }
}
#endif
