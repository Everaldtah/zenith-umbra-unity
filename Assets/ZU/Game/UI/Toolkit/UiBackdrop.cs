// backdrop-filter: blur() for the gamma panel. style.css blurs the frozen match behind the pause and results screens
// (".pause { background: rgba(5,6,12,.72); backdrop-filter: blur(6px) }"). UI Toolkit's backdropFilter blurs what is
// already in the panel's target - the screen, while the panel was a screen overlay. With UiGamma the panel draws into
// its own texture, where "behind" is empty, so the blur had nothing to work on and the rebuilt v0.2.2 showed the match
// sharp behind PAUSED. The 3D scene is blurred here instead, in the camera's own pipeline, for as long as an element
// that asked for it is in the panel; the panel (its .72 veil included) is then composited over the blurred picture.
//
// A URP render-graph pass built the way ZU.Engine's SharpenPass is (enqueued from beginCameraRendering, no renderer
// asset edited): camera colour -> horizontal Gaussian -> vertical Gaussian -> becomes the camera colour. It records
// itself only on the camera that resolves the stack and only while a blur is wanted, so it costs nothing in play.
// The blur runs on sRGB-encoded values, as the browser's does (ZUBackdropBlur.shader).
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    public sealed class UiBackdrop : ScriptableRenderPass
    {
        const string ShaderPath = "ZUUI/ZUBackdropBlur";
        static readonly int sigmaId = Shader.PropertyToID("_ZuSigma");

        static UiBackdrop inst;
        static Material mat;
        static Action<ScriptableRenderContext, Camera> onBegin;
        static bool loadTried;
        static VisualElement owner;
        static float cssPx;

        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        UiBackdrop()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 60;      // after the post stack and the sharpen pass (+50)
            profilingSampler = new ProfilingSampler("ZU Backdrop Blur");
            requiresIntermediateTexture = true;
        }

        /// <summary>the scene is being blurred this frame</summary>
        public static bool Active => cssPx > 0 && owner != null && owner.panel != null && mat != null;

        /// <summary>blur the 3D scene by a CSS blur(px) (px = the Gaussian's standard deviation at 1080p) for as long as
        /// this element stays in the panel. Returns false when the gamma panel is off or the shader is missing: the caller
        /// then uses UI Toolkit's own backdropFilter, which works for a screen-overlay panel.</summary>
        public static bool Attach(VisualElement element, float blurCssPx)
        {
            if (!UiGamma.Active || element == null || blurCssPx <= 0) return false;
            if (mat == null) { Load(); if (mat == null) return false; }
            if (inst == null)
            {
                inst = new UiBackdrop();
                onBegin = OnBeginCamera;
                RenderPipelineManager.beginCameraRendering += onBegin;
            }
            owner = element;
            cssPx = blurCssPx;
            return true;
        }

        // play mode without a domain reload: start every run clean (never two camera hooks)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRun()
        {
            if (onBegin != null) RenderPipelineManager.beginCameraRendering -= onBegin;
            onBegin = null; inst = null; owner = null; cssPx = 0;
            if (mat == null) loadTried = false;
        }

        static void Load()
        {
            if (loadTried) return;
            loadTried = true;
            var sh = Resources.Load<Shader>(ShaderPath);
            if (sh == null || !sh.isSupported)
            {
                Debug.LogWarning("[ZU UI] backdrop blur shader Resources/" + ShaderPath + (sh == null ? " not found" : " not supported here") + ": pause and results show the match unblurred");
                return;
            }
            mat = new Material(sh) { name = "ZU backdrop blur", hideFlags = HideFlags.HideAndDontSave };
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cssPx <= 0 || owner == null) return;
            if (owner.panel == null) { owner = null; cssPx = 0; return; }          // the screen that asked for it is gone
            if (cam.cameraType != CameraType.Game || mat == null || inst == null) return;
            var r = cam.GetUniversalAdditionalCameraData().scriptableRenderer;
            if (r != null) r.EnqueuePass(inst);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cam = frameData.Get<UniversalCameraData>();
            if (!cam.resolveFinalTarget || cam.cameraType != CameraType.Game || mat == null || cssPx <= 0) return;
            var res = frameData.Get<UniversalResourceData>();
            if (res.isActiveTargetBackBuffer) return;
            var src = res.cameraColor;
            if (!src.IsValid()) return;

            var desc = renderGraph.GetTextureDesc(src);
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.name = "_ZUBackdropH";
            var h = renderGraph.CreateTexture(desc);
            desc.name = "_ZUBackdropV";
            var v = renderGraph.CreateTexture(desc);

            // CSS pixels are 1080p pixels; the source may be scaled (render scale, dynamic resolution)
            mat.SetFloat(sigmaId, Mathf.Max(0.5f, cssPx * desc.height / 1080f));

            var ph = new RenderGraphUtils.BlitMaterialParameters(src, h, mat, 0, mpb, RenderGraphUtils.FullScreenGeometryType.ProceduralTriangle);
            renderGraph.AddBlitPass(ph, "ZU Backdrop Blur H");
            var pv = new RenderGraphUtils.BlitMaterialParameters(h, v, mat, 1, mpb, RenderGraphUtils.FullScreenGeometryType.ProceduralTriangle);
            renderGraph.AddBlitPass(pv, "ZU Backdrop Blur V");
            res.cameraColor = v;
        }
    }
}
