// The web game's native-scale image sharpening pass (port of zenith-umbra src/client/Game.ts:54-61 / :276 / :325,
// engine core) as a URP 17 Render Graph pass. The Options slider "Image Sharpening" (0..100) drives it: the web ran a
// 5-tap unsharp mask as the LAST pass of its post chain (after tone mapping + sRGB, the colour grade and SMAA), but only
// at render scale >= 0.99 - below native AMD FSR's RCAS sharpens the upscaled picture, and this pass would only sharpen
// the aliasing it upscales. URP's own RCAS likewise runs only below scale 1, so without this pass the slider does
// nothing at native. The shader (Resources/ZUEngine/Sharpen.shader) is the web maths, in the web's encoding.
// WHERE it lands in URP 17.6 (UniversalRendererRenderGraph.OnAfterRendering): custom passes in the
// [AfterRenderingPostProcessing, AfterRendering) window are recorded AFTER PostProcess.RenderPostProcessing (SMAA, DoF,
// TAA/STP, motion blur, bloom, UberPost = tone mapping + colour grade, all at AfterRenderingPostProcessing - 1) and
// BEFORE RenderFinalPostProcessing (FXAA / FSR RCAS / TAA sharpening, which URP guarantees runs after user passes) and
// the FinalBlit to the backbuffer. So: after SMAA / TAA and the grade like the web; FXAA (if the project picks it) runs
// after us instead of before - accepted. AfterRendering (1000) is already past the final blit (the active target is the
// backbuffer), so AfterRenderingPostProcessing is the latest safe point; +50 within that window sorts us after other
// owners' full-screen features at the same injection point (FullScreenPassRendererFeature defaults to +0), as the web
// sharpened last.
// WHY no renderer asset is edited: renderer assets belong to other people (the lead's Universal Renderer Data), so the
// pass is injected from code - RenderPipelineManager.beginCameraRendering -> camera.GetUniversalAdditionalCameraData()
// .scriptableRenderer.EnqueuePass(pass). URP clears its pass queue only at the END of each camera's rendering
// (ScriptableRenderer.cs:1227), so an enqueue made in beginCameraRendering survives into AddRenderPasses / SortStable.
// Only Game cameras are enqueued (no Preview / Reflection / SceneView), and the pass records itself only on the camera
// that resolves the stack (UniversalCameraData.resolveFinalTarget), once per frame, when the material loaded, the amount
// is > 0 and Perf.RenderScale >= 0.99 (no double sharpening on top of RCAS). Zero per-frame allocation: the pass, the
// material, its property block, the delegate and the property id are cached; _Amount is written only when it changes.
// Like the web's pass this is a SETTING, not an optimisation: it runs even with Perf.Enabled == false.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ZU.Engine
{
    internal sealed class SharpenPass : ScriptableRenderPass
    {
        /// <summary>the web's gain: shader amount = slider fraction x 0.6 (Game.ts:276)</summary>
        public const float WebGain = 0.6f;
        /// <summary>the web's gate (Game.ts:325): the pass runs only at render scale &gt;= this; below it FSR's RCAS sharpens</summary>
        public const float MinScale = 0.99f;
        const string ShaderPath = "ZUEngine/Sharpen";

        static readonly int amountId = Shader.PropertyToID("_Amount");
        static SharpenPass inst;
        static Material mat;
        static Action<ScriptableRenderContext, Camera> onBegin;
        static bool inited, loadTried;
        static float amount;

        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        SharpenPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 50;
            profilingSampler = new ProfilingSampler("ZU Sharpen");
            requiresIntermediateTexture = true;            // never let URP render straight to the backbuffer while we run
        }

        /// <summary>the slider fraction 0..1 as set</summary>
        public static float Amount => amount;
        /// <summary>the shader loaded and the material exists (false after a failed load: the pass stays off)</summary>
        public static bool Ready => mat != null;
        /// <summary>the pass would run this frame: amount &gt; 0, the applied render scale &gt;= 0.99 and the material loaded</summary>
        public static bool Active => amount > 0 && Perf.RenderScale >= MinScale && mat != null;

        /// <summary>subscribe the camera hook once (Perf.Init). The shader loads lazily on the first frame the slider is &gt; 0.</summary>
        internal static void Init()
        {
            if (inited) return;
            inited = true;
            inst = new SharpenPass();
            onBegin = OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += onBegin;
        }

        /// <summary>a new run (Perf.ResetRun: play mode without a domain reload): drop the old play's camera hook (Init
        /// re-subscribes once; never two handlers), slider back to 0. The material is kept when it survived (HideAndDontSave,
        /// no leak: one per domain); a destroyed one is reloaded on demand.</summary>
        internal static void ResetRun()
        {
            if (onBegin != null) RenderPipelineManager.beginCameraRendering -= onBegin;
            onBegin = null; inst = null; inited = false;
            amount = 0;
            if (mat != null) mat.SetFloat(amountId, 0); else loadTried = false;
        }

        /// <summary>the slider fraction 0..1; the material gets amount x 0.6, written only on change</summary>
        internal static void SetAmount(float amount01)
        {
            float a = Mathf.Clamp01(amount01);
            if (a == amount) return;
            amount = a;
            if (mat != null) mat.SetFloat(amountId, amount * WebGain);
        }

        // one attempt: a missing / unsupported shader logs once and the pass stays off (Ready == false)
        static void Load()
        {
            if (loadTried) return;
            loadTried = true;
            var sh = Resources.Load<Shader>(ShaderPath);
            if (sh == null || !sh.isSupported)
            {
                Debug.LogWarning($"[ZU.Engine] sharpen shader Resources/{ShaderPath} {(sh == null ? "not found" : "not supported here")}: the Image Sharpening slider does nothing at native scale");
                return;
            }
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetFloat(amountId, amount * WebGain);
        }

        // RenderPipelineManager.beginCameraRendering: fires for every camera URP renders, overlays of a stack included
        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (amount <= 0 || cam.cameraType != CameraType.Game || Perf.RenderScale < MinScale) return;
            if (mat == null) { Load(); if (mat == null) return; }
            var r = cam.GetUniversalAdditionalCameraData().scriptableRenderer;
            if (r != null) r.EnqueuePass(inst);           // per camera, re-queued every frame (URP empties the queue after each camera)
        }

        /// <summary>record: blit camera colour -> a temp texture through the sharpen material, then make the temp the camera colour</summary>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cam = frameData.Get<UniversalCameraData>();
            // the stack's resolving camera only (once per frame per stack); a camera scale below native = FSR / STP territory,
            // RCAS sharpens there (URP's imageScalingMode is internal; its renderScale is already snapped to 1 within 0.05)
            if (!cam.resolveFinalTarget || cam.cameraType != CameraType.Game || cam.renderScale < MinScale) return;
            if (mat == null) return;
            var res = frameData.Get<UniversalResourceData>();
            if (res.isActiveTargetBackBuffer) return;      // somebody already resolved to the screen: nothing left to swap
            var src = res.cameraColor;
            if (!src.IsValid()) return;
            // the same size / format as the camera colour (the post-processed, still-linear picture), single-sampled like
            // URP's own post-process targets (PostProcessUtils.MakeCompatible)
            var desc = renderGraph.GetTextureDesc(src);
            desc.name = "_ZUSharpen";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            var dst = renderGraph.CreateTexture(desc);
            // Blit.hlsl Vert = a procedural full-screen triangle; the helper binds _BlitTexture / _BlitScaleBias on the block
            var p = new RenderGraphUtils.BlitMaterialParameters(src, dst, mat, 0, mpb, RenderGraphUtils.FullScreenGeometryType.ProceduralTriangle);
            renderGraph.AddBlitPass(p, "ZU Sharpen");
            res.cameraColor = dst;                         // the final post / final blit read cameraColor: they now read the sharpened picture
        }
    }
}
