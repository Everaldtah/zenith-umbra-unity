// The VRAM tier (engine core, no TS original: a browser tab never saw the card's memory). What it is: a floor under the
// texture mipmap limit, picked once per process from the card's VRAM, so a 6 GB card loads the project's 4K textures from
// their 2K mip and never holds the full set. Why: QA on an RTX 3050 6 GB (default ULTRA preset, a 1280x720 window) peaked
// at 5.6-5.8 GB of VRAM on every match load and held 5.1-5.2 GB through an AI-vs-AI match - 0.3 GB of headroom, so a 6 GB
// player with Chrome open sat at the edge of a D3D out-of-memory crash on load. The cause is texture resolution: 386 of
// the project's 1044 textures import at 4096x4096 BC7 (about 22 MB each with mips; the Default platform block says 4096
// and the Standalone override is off), and one match loads one map's props plus 10 heroes - roughly 4 GB of textures.
// The render targets are about 0.25 GB at 1080p (render scale 125 %, 4x MSAA HDR, a 4096 main-light shadow atlas with 4
// cascades, a 2048 additional-light atlas). Drawing the 4K textures from their 2K mip (QualitySettings.globalTextureMipmapLimit
// 1) is a quarter of the texture memory; the 1K mip (limit 2) a sixteenth.
// The tiers (SystemInfo.graphicsMemorySize, as HardwareProfile reads it): 7680 MB and up (8 GB cards) floor 0 = no change;
// 4608 MB and up (6 GB) floor 1; below floor 2 (4 GB and smaller); and at least 1 on HardwareProfile's Low tier, because an
// integrated GPU reports shared memory as VRAM, which can read as a lot, while the RAM / core floors still catch that
// machine. Big cards are never touched: quality is never a hidden cap. The player's own texture setting sits on top of the
// floor (the SettingsApply hook: max(setting, floor), and an explicit top choice that ignores the floor); TextureFloorNote
// labels the Options row. The main-light shadow atlas follows the computed floor too - 4096 -> 2048 on a floor tier (a
// quarter of that atlas), never from the player's texture choice and never at 7680 MB and up - and only where Perf may
// write the URP asset (a player; in the Editor the asset is the project's file on disk).
// Applied as early as the process allows, so the FIRST scene's textures are already loaded at the floor and the full-size
// upload never happens: Prime from Perf's SubsystemRegistration hook (before the first scene; the limit only, no asset
// write, no log) and Init from Perf.Init (PerfDriver.Boot, BeforeSceneLoad: the limit again, the atlas, the one log line).
// The command line -zu-texfloor=N (or -zu-texfloor N; 0, 1 or 2) forces the floor: 0 = off = the old behaviour, the A/B
// switch for the before / after measurement (it flips the atlas step too, so the two runs differ by this file only).
// The floor is a per-process constant (the card does not change between plays); Init's log and atlas pass run once per
// run (ResetRun, play mode without a domain reload). Independent of -zu-engine=0: a memory guard, not a frame-time
// optimisation.
// Editor plays keep the floor (the lead sees the real 2K result) but, since QualitySettings is the project's asset there
// and Unity does not revert it when Play mode stops, the limit as it was before the play is captured once per run and
// written back on Application.quitting, so no save or build after a 6 GB play ships limit 1 to every card.
using UnityEngine;

namespace ZU.Engine
{
    public static class VramBudget
    {
        /// <summary>the tier thresholds (MB of SystemInfo.graphicsMemorySize): at or above FullVramMB no floor; at or above
        /// HalfVramMB floor 1 (4K textures from their 2K mip); below, floor 2. Public so a test can retune them.</summary>
        public static int FullVramMB = 7680, HalfVramMB = 4608;
        /// <summary>the main-light shadow atlas on a floor tier (an atlas above it is lowered to it)</summary>
        public const int SHADOW_ATLAS_FLOOR = 2048;
        const string SWITCH = "-zu-texfloor";

        // computed: the floor is known (the graphics device answered); inited: this run's Init (atlas + log) is done
        static bool computed, inited, forced;
        static int floor, tierFloor;
        // the atlas write of this process (-1 = none): the asset keeps the value, so the log reports it once
        static int shadowFrom = -1, shadowTo = -1;
        // Editor only: the project's mipmap limit as it was before this play touched it, written back when the play stops
        // (Application.quitting); one cached delegate per run, unsubscribed in ResetRun so handlers never stack across plays
        static bool captured;
        static int capturedLimit;
        static System.Action onQuitting;

        // ------------------------------------------------------------------ read-only state

        /// <summary>the floor under QualitySettings.globalTextureMipmapLimit: 0 (8 GB cards and up: unchanged), 1 (6 GB: 4K
        /// textures from their 2K mip) or 2 (4 GB and smaller, or a Low hardware tier). The command line's value when
        /// -zu-texfloor forced it. Computed on first read (main thread).</summary>
        public static int TextureMipFloor { get { Compute(); return floor; } }
        /// <summary>the floor the VRAM / tier rule computed, before the command line (what the switch overrode)</summary>
        public static int TierFloor { get { Compute(); return tierFloor; } }
        /// <summary>-zu-texfloor forced the floor</summary>
        public static bool Forced { get { Compute(); return forced; } }
        /// <summary>the Options row label for the texture setting: "2K on this card" (floor 1), "1K on this card" (floor 2),
        /// null when there is no floor</summary>
        public static string TextureFloorNote => TextureMipFloor == 1 ? "2K on this card" : TextureMipFloor == 2 ? "1K on this card" : null;
        /// <summary>the main-light shadow atlas as this process lowered it (from -&gt; to; -1, -1 when it did not)</summary>
        public static int ShadowAtlasFrom => shadowFrom;
        public static int ShadowAtlasTo => shadowTo;

        // ------------------------------------------------------------------ apply

        /// <summary>the earliest apply (Perf's SubsystemRegistration hook: before the first scene's textures load): compute
        /// the floor and raise the mipmap limit to it. No asset write, no log - Init does those. Nothing when the graphics
        /// device has not answered yet (graphicsMemorySize 0): then Init, a moment later, computes.</summary>
        internal static void Prime()
        {
            Compute();
            Raise();
        }

        /// <summary>once per run, from Perf.Init (after the hardware line is logged): the limit again (a settings write in
        /// between is kept at or above the floor), the main-light shadow atlas where Perf may write the URP asset, and the
        /// start-up log line.</summary>
        internal static void Init()
        {
            if (inited) return;
            inited = true;
            Compute();
            if (!computed) return;                         // (no graphics device: nothing to size; the next run tries again)
            Raise();
            // the atlas: the computed floor's tiers only (never the player's texture choice, which may sit above the floor);
            // an asset already at or below the floor is left alone. Editor: the asset is the project's file - never written
            if (floor >= 1 && Perf.AllowAssetWrites)
            {
                var a = Perf.Asset;
                if (a != null && a.mainLightShadowmapResolution > SHADOW_ATLAS_FLOOR)
                {
                    shadowFrom = a.mainLightShadowmapResolution;
                    a.mainLightShadowmapResolution = SHADOW_ATLAS_FLOOR;
                    shadowTo = a.mainLightShadowmapResolution;
                }
            }
            Debug.Log(Line());
        }

        /// <summary>a new run (Perf.ResetRun: play mode without a domain reload): Init runs again from Perf.Init, the Editor
        /// capture is taken again (the old play's quitting handler unsubscribed first). The floor itself is a per-process
        /// constant and stays computed.</summary>
        internal static void ResetRun()
        {
            inited = false;
            if (onQuitting != null) { Application.quitting -= onQuitting; onQuitting = null; }
            captured = false;
        }

        // the tier rule, once per process. graphicsMemorySize reads 0 before the graphics device is up: not computed yet,
        // and HardwareProfile is not snapshotted either (its VRAM would be cached wrong)
        static void Compute()
        {
            if (computed) return;
            if (SystemInfo.graphicsMemorySize <= 0) return;
            computed = true;
            var hw = HardwareProfile.Current;
            tierFloor = hw.vramMB >= FullVramMB ? 0 : hw.vramMB >= HalfVramMB ? 1 : 2;
            if (hw.tier == HardwareProfile.Tier.Low && tierFloor < 1) tierFloor = 1;
            int f = Perf.FlagInt(SWITCH, -1);
            forced = f >= 0 && f <= 2;                     // (another value: ignored, the rule stands)
            floor = forced ? f : tierFloor;
        }

        // the limit is only ever raised here: the player's setting may ask for less (low / medium) and the hook keeps the max
        static void Raise()
        {
            if (!computed || floor <= 0) return;
            Capture();
            if (QualitySettings.globalTextureMipmapLimit < floor) QualitySettings.globalTextureMipmapLimit = floor;
        }

        // Editor only, once per run, before the first write of a play: QualitySettings is the project's asset there and
        // Unity does not revert it when Play mode stops, so a 6 GB card's play would leave limit 1 in the PC quality level
        // for a later save or build to ship to every card. The floor stays active for the play (the real 2K result is
        // seen); on Application.quitting (the Editor raises it when Play mode stops) the captured value goes back, which
        // also undoes the settings hook's in-play writes: the project file is left exactly as before the play. A built
        // player captures and restores nothing.
        static void Capture()
        {
            if (captured || !Application.isEditor) return;
            captured = true;
            capturedLimit = QualitySettings.globalTextureMipmapLimit;
            onQuitting = RestoreCaptured;
            Application.quitting += onQuitting;
        }

        static void RestoreCaptured() { QualitySettings.globalTextureMipmapLimit = capturedLimit; }

        // "[ZU.Engine] VRAM tier: 6.0 GB -> texture floor 1 (4K -> 2K), -zu-texfloor=0 to disable, main-light shadows 4096 -> 2048"
        // "[ZU.Engine] VRAM tier: 12.0 GB -> no texture floor (full resolution)"; a forced floor says so in place of the switch hint
        static string Line()
        {
            string gb = (HardwareProfile.Current.vramMB / 1024f).ToString("0.0") + " GB";
            string s = floor <= 0
                ? "[ZU.Engine] VRAM tier: " + gb + " -> no texture floor (full resolution)" + (forced ? ", forced by " + SWITCH + "=0" : "")
                : "[ZU.Engine] VRAM tier: " + gb + " -> texture floor " + floor + (floor == 1 ? " (4K -> 2K)" : " (4K -> 1K)")
                  + (forced ? ", forced by " + SWITCH + "=" + floor : ", " + SWITCH + "=0 to disable");
            if (shadowFrom > 0) s += ", main-light shadows " + shadowFrom + " -> " + shadowTo;
            return s;
        }
    }
}
