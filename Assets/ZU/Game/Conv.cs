// The simulation is right-handed (it was written for Three.js); Unity is left-handed. Both have +Y up and the sim's
// forward at yaw 0 is +Z, so the conversion mirrors X: sim (x, y, z) -> Unity (-x, y, z), and a sim yaw turns the other
// way: Unity's Y rotation (degrees) = -yaw. Everything that crosses the boundary goes through here.
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public static class Conv
    {
        public static Vector3 U(V3 v) => new Vector3((float)-v.x, (float)v.y, (float)v.z);
        public static Vector3 U(double x, double y, double z) => new Vector3((float)-x, (float)y, (float)z);
        public static V3 S(Vector3 v) => new V3(-v.x, v.y, v.z);
        /// <summary>sim yaw (radians) -> Unity Y angle (degrees)</summary>
        public static float YawDeg(double yaw) => (float)(-yaw * Mathf.Rad2Deg);
        /// <summary>sim pitch (radians, + = up) -> Unity X angle (degrees, + = down)</summary>
        public static float PitchDeg(double pitch) => (float)(-pitch * Mathf.Rad2Deg);
        public static Quaternion Yaw(double yaw) => Quaternion.Euler(0, YawDeg(yaw), 0);
        public static Quaternion Aim(double yaw, double pitch) => Quaternion.Euler(PitchDeg(pitch), YawDeg(yaw), 0);

        public static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }
        public static Color Hex(string hex) => Hex(hex, Color.white);
    }
}
