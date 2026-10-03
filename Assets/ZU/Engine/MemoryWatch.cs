// Cheap memory readout (engine core, no TS original: a browser tab can't see the machine's RAM). This PC has hung twice
// from RAM exhaustion with the game among the tenants, so the Governor watches the machine's free physical memory and
// acts before Windows starts paging everything: Elevated sustained 5 s = one Resources.UnloadUnusedAssets (at most every
// 120 s); Critical = UnloadUnusedAssets + GC.Collect (at most every 20 s) and a warning in the log; every change of
// Pressure is raised as Governor.PressureChanged so the asset owners (FX, dynamics) can shed load of their own. Two
// sources: the machine (kernel32 GlobalMemoryStatusEx: available physical MB and the load %, the same numbers Task
// Manager shows) and the player's own (Unity's memory profiler counters, which cost nothing to read). Sampled at most twice
// a second; callers may call Sample every frame.
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;

namespace ZU.Engine
{
    public static class MemoryWatch
    {
        /// <summary>
        /// Machine memory pressure. Elevated: under ElevatedAvailMB free (3 GB) or over ElevatedLoadPct in use (85 %): back
        /// off, nothing new big. Critical: under CriticalAvailMB free (1.5 GB) or over CriticalLoadPct (93 %): shed
        /// everything optional now, Windows is about to page. None when the machine readout isn't available.
        /// </summary>
        public enum Pressure { None, Elevated, Critical }

        /// <summary>the Pressure thresholds (MB free / % in use); public for the Governor</summary>
        public static float ElevatedAvailMB = 3 * 1024, CriticalAvailMB = 1.5f * 1024;
        public static float ElevatedLoadPct = 85, CriticalLoadPct = 93;

        /// <summary>how often Sample really samples (s, unscaled)</summary>
        public const float PERIOD = 0.5f;

        // ---- machine (-1 where the platform can't tell)
        /// <summary>physical memory free / installed (MB), and the share in use (%)</summary>
        public static float AvailMB = -1, TotalMB = -1, LoadPct = -1;
        public static Pressure pressure = Pressure.None;

        // ---- this process (MB; -1 for a counter that isn't valid in this player)
        /// <summary>what the OS says the process uses</summary>
        public static long SystemUsedMB = -1;
        /// <summary>what Unity tracks of its own allocations</summary>
        public static long TotalUsedMB = -1;
        /// <summary>the managed (C#) heap reserved</summary>
        public static long GcReservedMB = -1;
        /// <summary>textures, meshes, render targets, as the graphics driver counts them</summary>
        public static long GfxUsedMB = -1;

        /// <summary>unscaled time of the last real sample (-1 before the first)</summary>
        public static float LastSampleTime = -1;

        static bool started;
        static ProfilerRecorder rSys, rTotal, rGc, rGfx;

        /// <summary>
        /// Refresh the readouts if PERIOD has passed (true when it did). Starts the Unity counters on the first call; they're
        /// disposed when the application quits (or play mode ends).
        /// </summary>
        public static bool Sample()
        {
            float t = Time.unscaledTime;
            if (LastSampleTime >= 0 && t - LastSampleTime < PERIOD) return false;
            LastSampleTime = t;
            if (!started) Start();
            ReadMachine();
            SystemUsedMB = Read(rSys); TotalUsedMB = Read(rTotal); GcReservedMB = Read(rGc); GfxUsedMB = Read(rGfx);
            pressure = AvailMB < 0 ? Pressure.None
                     : AvailMB < CriticalAvailMB || LoadPct > CriticalLoadPct ? Pressure.Critical
                     : AvailMB < ElevatedAvailMB || LoadPct > ElevatedLoadPct ? Pressure.Elevated : Pressure.None;
            return true;
        }

        static void Start()
        {
            started = true;
            rSys = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            rTotal = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            rGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            rGfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            Application.quitting += Stop;
        }

        /// <summary>release the counters (Application.quitting does it; a Governor may too)</summary>
        public static void Stop()
        {
            if (!started) return;
            started = false;
            Application.quitting -= Stop;
            rSys.Dispose(); rTotal.Dispose(); rGc.Dispose(); rGfx.Dispose();
            SystemUsedMB = TotalUsedMB = GcReservedMB = GfxUsedMB = -1;
            LastSampleTime = -1;           // (the Editor keeps statics across play sessions without a domain reload: start afresh)
        }

        // a counter a release player doesn't carry reads as invalid (or 0 before its first frame): -1 says "unknown" to the HUD
        static long Read(ProfilerRecorder r) => r.Valid && r.CurrentValue > 0 ? r.CurrentValue >> 20 : -1;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // kernel32 GlobalMemoryStatusEx: dwLength must be set to the struct's size before the call
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        static MEMORYSTATUSEX ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        static void ReadMachine()
        {
            if (GlobalMemoryStatusEx(ref ms))
            {
                AvailMB = ms.ullAvailPhys >> 20; TotalMB = ms.ullTotalPhys >> 20; LoadPct = ms.dwMemoryLoad;
            }
            else AvailMB = TotalMB = LoadPct = -1;
        }
#else
        static void ReadMachine() { AvailMB = TotalMB = LoadPct = -1; }
#endif

        /// <summary>one line for the logs / HUD: "RAM 11234/32640 MB free (66 %, Elevated) | proc 2310 MB | unity 1840 | gc 96 | gfx 1210"</summary>
        public static string Summary() =>
            $"RAM {AvailMB:0}/{TotalMB:0} MB free ({LoadPct:0} %, {pressure}) | proc {SystemUsedMB} MB | unity {TotalUsedMB} | gc {GcReservedMB} | gfx {GfxUsedMB}";
    }
}
