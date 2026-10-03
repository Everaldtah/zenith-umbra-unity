// A one-off snapshot of the machine (engine core, no TS original: the web build only had navigator.hardwareConcurrency and
// the WebGL renderer string). The Governor picks its starting quality tier and its memory headroom from it instead of
// probing frame by frame, and the HUD / logs print one line of it so a report from another PC says what it ran on.
// Taken lazily on first use (SystemInfo is main-thread only, so the first touch must come from there).
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.Engine
{
    public sealed class HardwareProfile
    {
        /// <summary>
        /// Coarse machine class. Low: VRAM under 3 GB, or RAM under 8 GB, or fewer than 4 logical cores (an integrated GPU
        /// or an old laptop). High: VRAM 8 GB or more, RAM 16 GB or more and 8 or more cores. Mid: everything between (an RTX
        /// 3050 6 GB with 32 GB of RAM lands here: the VRAM keeps it out of High).
        /// </summary>
        public enum Tier { Low, Mid, High }

        /// <summary>thresholds behind Tier (MB / cores); public so the Governor can read or retune them</summary>
        public static int LowVramMB = 3 * 1024, LowRamMB = 8 * 1024, LowCores = 4;
        public static int HighVramMB = 8 * 1024, HighRamMB = 16 * 1024, HighCores = 8;

        static HardwareProfile current;
        /// <summary>the snapshot, taken on first access</summary>
        public static HardwareProfile Current => current ??= new HardwareProfile();

        public readonly string gpuName, gpuVendor, cpuType, os;
        public readonly GraphicsDeviceType gpuType, api;
        public readonly int vramMB, shaderLevel, cores, cpuMHz, ramMB;
        public readonly bool compute, frameTiming;
        /// <summary>the display's refresh rate (Hz), from Screen.currentResolution.refreshRateRatio</summary>
        public readonly float refreshHz;
        public readonly Tier tier;

        HardwareProfile()
        {
            gpuName = SystemInfo.graphicsDeviceName; gpuVendor = SystemInfo.graphicsDeviceVendor;
            gpuType = api = SystemInfo.graphicsDeviceType;         // (the device type is the graphics API in Unity's terms)
            vramMB = SystemInfo.graphicsMemorySize; shaderLevel = SystemInfo.graphicsShaderLevel; compute = SystemInfo.supportsComputeShaders;
            cpuType = SystemInfo.processorType; cores = SystemInfo.processorCount; cpuMHz = SystemInfo.processorFrequency;
            ramMB = SystemInfo.systemMemorySize; os = SystemInfo.operatingSystem;
            refreshHz = (float)Screen.currentResolution.refreshRateRatio.value;
            frameTiming = FrameTimingManager.IsFeatureEnabled();
            // (an integrated GPU reports shared memory as VRAM, which can read as a lot: the RAM and core floors still catch
            // the small machines that carry one)
            tier = vramMB < LowVramMB || ramMB < LowRamMB || cores < LowCores ? Tier.Low
                 : vramMB >= HighVramMB && ramMB >= HighRamMB && cores >= HighCores ? Tier.High : Tier.Mid;
        }

        /// <summary>one line for the logs: "Mid | NVIDIA GeForce RTX 3050 6GB (Direct3D12, 6144 MB, SM 5.0, compute) | 12x AMD ... 3700 MHz | 32768 MB RAM | 144 Hz | Windows 11 | frame timing on"</summary>
        public string Summary()
        {
            var sb = new StringBuilder(192);
            sb.Append(tier).Append(" | ").Append(gpuName).Append(" (").Append(api).Append(", ").Append(vramMB).Append(" MB, SM ")
              .Append(shaderLevel / 10).Append('.').Append(shaderLevel % 10).Append(compute ? ", compute" : ", no compute").Append(") | ")
              .Append(cores).Append("x ").Append(cpuType).Append(' ').Append(cpuMHz).Append(" MHz | ")
              .Append(ramMB).Append(" MB RAM | ").Append(refreshHz.ToString("0.#")).Append(" Hz | ").Append(os)
              .Append(frameTiming ? " | frame timing on" : " | frame timing off");
            return sb.ToString();
        }
    }
}
