// The settings the engine reads directly, applied when they change (the TS Game.applySettings + Settings.quality):
// display mode, frame-rate cap, render scale, MSAA, shadow distance, texture filtering and resolution, master and voice
// volume. HUD options go to the HUD (ZuSettings.Changed); the camera, controls and post-processing read
// ZuSettings.Current themselves.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZU.Game.UI.Toolkit
{
    public static class SettingsApply
    {
        public static void Apply(ZuSettings s)
        {
            var v = s.video;
            // display and frame pacing ("DISPLAY BASED" = the display's refresh, through vsync)
            if (!Application.isEditor)
                Screen.fullScreenMode = v.displayMode == "fullscreen" ? FullScreenMode.ExclusiveFullScreen : v.displayMode == "windowed" ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            QualitySettings.vSyncCount = v.fpsCap > 0 ? 0 : 1;
            Application.targetFrameRate = v.fpsCap > 0 ? (int)v.fpsCap : -1;
            // texture filtering and resolution
            int af = Mathf.Clamp((int)v.texFilter, 1, 16);
            QualitySettings.anisotropicFiltering = af > 1 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
            Texture.SetGlobalAnisotropicFilteringLimits(af, af);
            QualitySettings.globalTextureMipmapLimit = v.textures == "low" ? 2 : v.textures == "medium" ? 1 : 0;
            // the render pipeline asset (the player changes its in-memory copy; in the editor that would rewrite the
            // project's asset, so the editor keeps its own)
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset a)
            {
                a.renderScale = Mathf.Clamp((float)v.renderScale / 100f, 0.5f, 2f);
                a.msaaSampleCount = v.aa == "msaa" || v.aa == "msaa+fxaa" ? 4 : 1;
                a.shadowDistance = v.shadows == "off" ? 0 : v.shadows == "low" ? 60 : v.shadows == "medium" ? 110 : v.shadows == "high" ? 140 : 160;
            }
            // sound
            AudioListener.volume = Mathf.Clamp01((float)s.sound.master);
        }
    }
}
