// `zu_capture`: render the scene's main camera (in play mode: the match as the player sees it, minus the IMGUI HUD) into
// an off-screen texture and save it as a PNG. Unlike a Game-view capture it never returns a stale frame, and it works
// while the editor is unfocused.
using System.IO;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.EditorTools
{
    public static class ZuCapture
    {
        [CliCommand("zu_capture", "Render the main camera to a PNG (play mode or edit mode)")]
        public static string Capture(
            [CliArg("out", "PNG path, relative to the project")] string output = "Screenshots/capture.png",
            [CliArg("width", "pixels")] int width = 1600,
            [CliArg("height", "pixels")] int height = 900)
        {
            var cam = Camera.main;
            if (cam == null) return "no main camera";
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = true });
            var prev = RenderTexture.active;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { var t = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = t; }
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                var path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return $"{output}: {width}x{height} from '{cam.name}' at {cam.transform.position}, frame {Time.frameCount}, t {Time.time:0.0}s";
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}
