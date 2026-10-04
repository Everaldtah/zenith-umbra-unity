// CSS colour maths for the front end. The PC game's UI is a web page: every translucent rule in style.css (and so in
// zu.uss) is blended by the browser on sRGB-ENCODED values. This project renders in LINEAR colour space, where UI
// Toolkit decodes its colours and the GPU blends the decoded values - rgba(255,255,255,0.06) over the near-black menu
// then lands at about 80/255 instead of 15/255. That one difference is what made the v0.2.2 menus look "blocky": the
// glass buttons, steppers, kill-feed rows and round box were opaque grey slabs, and every dim hairline a bright one.
//
// The fix Unity offers is PanelSettings.forceGammaRendering, which only takes effect when the panel draws into a UNORM
// render texture: the panel then blends encoded values exactly as a browser does. This component owns that texture (the
// size of the screen, re-made on a resize) and puts it on the screen at the end of every frame, after the cameras.
//
// The last step - the finished UI over the 3D scene - happens on an sRGB backbuffer, where the GPU blends linear values
// again and cannot read what is under it. Opaque UI (every menu screen has an opaque backdrop) is exact. For see-through
// UI over the scene (the HUD, the pause veil) the shader picks the alpha that is exact for dark glass and keeps the
// plain linear blend for bright pixels (text edges), so nothing gets a dark fringe. See ZUUiComposite.shader.
//
// Switches (player command line): -zu-uigamma=0 puts the panel back on the screen directly (the v0.2.2 look);
// -zu-uiflip=1 flips the composite vertically, should a graphics API hand the texture over upside down.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    public sealed class UiGamma : MonoBehaviour
    {
        const string ShaderPath = "ZUUI/ZUUiComposite";
        static readonly int FlipId = Shader.PropertyToID("_ZuFlipY");
        static readonly int EncodedId = Shader.PropertyToID("_ZuEncoded");

        /// <summary>the panel draws into the gamma texture (false: straight to the screen, as before)</summary>
        public static bool Active { get; private set; }

        PanelSettings ps;
        RenderTexture rt;
        Material mat;
        bool flip;

        /// <summary>give the panel its gamma target; call before the document is first enabled. Does nothing (and the
        /// panel stays a screen overlay) in a gamma project, with -zu-uigamma=0, or when the shader or format is missing.</summary>
        public static void Attach(GameObject host, PanelSettings panel)
        {
            Active = false;
            if (host == null || panel == null) return;
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return;      // a gamma project already blends like CSS
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-zu-uigamma=0") >= 0) { Debug.Log("[ZU UI] gamma panel off (-zu-uigamma=0)"); return; }
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.Render)) return;
            var sh = Resources.Load<Shader>(ShaderPath);
            if (sh == null || !sh.isSupported) { Debug.LogWarning("[ZU UI] Resources/" + ShaderPath + " is missing or unsupported - the panel blends in linear space"); return; }

            var g = host.AddComponent<UiGamma>();
            g.ps = panel;
            g.mat = new Material(sh) { name = "ZU UI composite", hideFlags = HideFlags.HideAndDontSave };
            g.flip = Array.IndexOf(args, "-zu-uiflip=1") >= 0;
            panel.forceGammaRendering = true;
            panel.clearColor = true;                                    // the texture starts every frame transparent
            panel.colorClearValue = new Color(0, 0, 0, 0);
            panel.clearDepthStencil = true;
            g.Allocate();
            Active = g.rt != null;
            if (Active) Debug.Log("[ZU UI] gamma panel on: " + g.rt.width + "x" + g.rt.height + " UNORM, composite at frame end");
        }

        static int W => Mathf.Max(1, Screen.width);
        static int H => Mathf.Max(1, Screen.height);

        void Allocate()
        {
            var n = new RenderTexture(W, H, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                name = "ZU UI (gamma)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave,
            };
            if (!n.Create()) { Destroy(n); return; }
            ps.targetTexture = n;
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = n;
        }

        void OnEnable() { if (ps != null) StartCoroutine(Composite()); }

        void Update()
        {
            // the window was resized, or the graphics device was reset and the texture lost
            if (Active && (rt == null || rt.width != W || rt.height != H || !rt.IsCreated())) Allocate();
        }

        // After the cameras, the overlay panels and IMGUI; before the frame is presented (and before a
        // ScreenCapture grabs it). One full-screen draw.
        IEnumerator Composite()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!Active || rt == null || mat == null || !rt.IsCreated()) continue;
                mat.SetFloat(FlipId, flip ? 1f : 0f);
                // a backbuffer that is not sRGB takes encoded values as they are, and then blends them like CSS: exact
                mat.SetFloat(EncodedId, Display.main != null && Display.main.requiresSrgbBlitToBackbuffer ? 1f : 0f);
                Graphics.Blit(rt, (RenderTexture)null, mat, 0);
            }
        }

        void OnDestroy()
        {
            Active = false;
            if (ps != null) ps.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (mat != null) { Destroy(mat); mat = null; }
        }
    }
}
