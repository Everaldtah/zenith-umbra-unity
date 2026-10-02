// `zu_preview_hero`: render a hero prefab (or any model asset) on its own - isolated preview scene, its own camera and
// lights - straight to a PNG, from any angle, optionally posed by a clip at a given time. The dependable way for an agent
// to look at a model (a Game-view capture can return a stale frame when the editor isn't repainting).
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZU.EditorTools
{
    public static class ZuPreview
    {
        [CliCommand("zu_preview_hero", "Render a hero prefab (or a model asset path) alone to a PNG: angle, size, an optional clip pose")]
        public static string Preview(
            [CliArg("id", "hero id (Assets/ZU/Art/Heroes/<id>/<id>.prefab) or an asset path")] string id = "kaien",
            [CliArg("yaw", "camera angle around the model, degrees (0 = from the front)")] float yaw = 20,
            [CliArg("out", "PNG path, relative to the project")] string output = "Screenshots/preview.png",
            [CliArg("size", "image size in pixels")] int size = 1024,
            [CliArg("clip", "optional animation take to pose with, e.g. TR_Jog_Fwd (searched in Assets/ZU/Art/Anim)")] string clip = "",
            [CliArg("time", "time into the clip, seconds")] float time = 0.3f,
            [CliArg("zoom", "1 = full body; 2.5 = head and shoulders")] float zoom = 1,
            [CliArg("held", "attach the hero's held weapons (HeldProps) and close the fingers on them, as the game does")] bool held = false)
        {
            string path = id.Contains("/") ? id : $"Assets/ZU/Art/Heroes/{id}/{id}.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return "not found: " + path;
            var pru = new PreviewRenderUtility();
            PlayableGraph graph = default;
            try
            {
                var go = Object.Instantiate(asset);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                pru.AddSingleGO(go);
                // the weapons and finger grips bind to the bind pose, so before the clip poses the rig
                ZU.Game.HeldRig heldRig = null; ZU.Game.Fingers fingers = null;
                if (held)
                {
                    var def = ZU.Game.ZuData.Get().Def(asset.name);
                    var rig = new ZU.Game.FirstPerson.RigPose(go.transform);
                    if (def != null) heldRig = ZU.Game.HeldRig.Attach(go, rig, def);
                    fingers = ZU.Game.Fingers.Build(go.transform);
                }
                // pose: evaluate a humanoid clip through a PlayableGraph (runs the avatar's retargeting in edit mode, unlike
                // AnimationMode sampling, which leaves a humanoid in a preview scene in its bind pose)
                var animator = go.GetComponentInChildren<Animator>();
                if (!string.IsNullOrEmpty(clip))
                {
                    if (animator == null) return "no Animator on " + path;
                    var c = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/ZU/Art/Anim" })
                        .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                        .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                        .FirstOrDefault(x => x.name.EndsWith("|" + clip) || x.name == clip);
                    if (c == null) return "clip not found: " + clip;
                    graph = PlayableGraph.Create("zu_preview");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var po = AnimationPlayableOutput.Create(graph, "pose", animator);
                    var play = AnimationClipPlayable.Create(graph, c);
                    play.SetApplyFootIK(true);
                    play.SetTime(time); play.SetTime(time);   // twice: no delta, so no root motion / events from 0
                    po.SetSourcePlayable(play);
                    graph.Evaluate(0);
                }
                if (heldRig != null) { heldRig.PlacePropAtRest(); heldRig.Place(); }
                if (fingers != null) { var g = ZU.Game.Fingers.BaseGrips(asset.name); fingers.Set(0, g.L); fingers.Set(1, g.R); fingers.Update(1, 1000); }
                // frame the model: its renderers' bounds
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.up, Vector3.one * 2);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float h = Mathf.Max(b.size.y, 0.5f) / zoom;
                var focus = zoom > 1.5f ? new Vector3(b.center.x, b.max.y - h * 0.5f, b.center.z) : b.center;
                var cam = pru.camera;
                cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                float dist = h * 0.62f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + b.extents.z;
                var dir = Quaternion.Euler(8, 180 + yaw, 0) * Vector3.forward;   // 0 = looking at the model's face (+Z is its front)
                cam.transform.position = focus - dir * dist;
                cam.transform.LookAt(focus);
                pru.lights[0].intensity = 1.25f; pru.lights[0].transform.rotation = Quaternion.Euler(35, 150 + yaw, 0);
                pru.lights[1].intensity = 0.55f; pru.lights[1].transform.rotation = Quaternion.Euler(-10, -40 + yaw, 0);
                pru.ambientColor = new Color(0.32f, 0.33f, 0.36f);
                pru.BeginStaticPreview(new Rect(0, 0, size, size));
                pru.Render(true);
                var tex = pru.EndStaticPreview();
                var outPath = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return $"{output}: {asset.name}, bounds {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m, {rs.Length} renderer(s){(string.IsNullOrEmpty(clip) ? "" : $", posed {clip} @ {time:0.00}s")}";
            }
            finally { if (graph.IsValid()) graph.Destroy(); pru.Cleanup(); }
        }
    }
}
