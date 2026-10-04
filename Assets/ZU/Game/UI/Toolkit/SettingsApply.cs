// The settings the engine reads directly, applied when they change (the TS Game.applySettings + Settings.quality):
// display mode, frame-rate cap, render scale, MSAA, shadow distance, texture filtering and resolution, master and voice
// volume. HUD options go to the HUD (ZuSettings.Changed); the camera, controls and post-processing read
// ZuSettings.Current themselves.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZU.Engine;

namespace ZU.Game.UI.Toolkit
{
    public static class SettingsApply
    {
        /// <summary>a start-up -screen-fullscreen (Unity's own switch: QA runs in a window next to other work) wins over the
        /// saved display mode</summary>
        static readonly bool screenSwitch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-screen-fullscreen") >= 0;

        public static void Apply(ZuSettings s)
        {
            var v = s.video;
            // display and frame pacing ("DISPLAY BASED" = the display's refresh, through vsync)
            if (!Application.isEditor && !screenSwitch)
                Screen.fullScreenMode = v.displayMode == "fullscreen" ? FullScreenMode.ExclusiveFullScreen : v.displayMode == "windowed" ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            // the engine owns the cap, URP renderScale (settings x dynamic) and the upscaler (ZU.Engine.Perf, docs/ENGINE.md)
            Perf.SetCap((int)v.fpsCap);                                   // 0 = display based; a cap dividing the refresh locks to every k-th vblank
            Perf.SetBaseScale(Mathf.Clamp((float)v.renderScale / 100f, 0.5f, 2f));
            Perf.SetDynamicResolution(v.dynamicRes);                      // off resets the dynamic scale to 1 (TS applySettings)
            Perf.SetUpscaler(Perf.Upscaler.Auto);                         // FSR below native with the web's RCAS 0.25 stops
            Perf.SetSharpen(Mathf.Clamp01((float)v.sharpen / 100f));     // the web's native-scale sharpen pass (ZU.Engine SharpenPass)
            // texture filtering and resolution
            int af = Mathf.Clamp((int)v.texFilter, 1, 16);
            QualitySettings.anisotropicFiltering = af > 1 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
            Texture.SetGlobalAnisotropicFilteringLimits(af, af);
            QualitySettings.globalTextureMipmapLimit = v.textures == "low" ? 2 : v.textures == "medium" ? 1 : 0;
            // the render pipeline asset (the player changes its in-memory copy; in the editor that would rewrite the
            // project's asset, so the editor keeps its own)
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset a)
            {
                a.msaaSampleCount = v.aa == "msaa" || v.aa == "msaa+fxaa" ? 4 : 1;
                a.shadowDistance = v.shadows == "off" ? 0 : v.shadows == "low" ? 60 : v.shadows == "medium" ? 110 : v.shadows == "high" ? 140 : 160;
            }
            // sound
            AudioListener.volume = Mathf.Clamp01((float)s.sound.master);
        }
    }
}
