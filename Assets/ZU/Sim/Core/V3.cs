// Plain 3-vector for the simulation (no UnityEngine dependency, so the sim runs in EditMode tests and headless tools).
// Mirrors the TS `V3 { x, y, z }` object as a struct of doubles (JS numbers are doubles: same precision, so the C# sim
// reproduces the TS sim to the last bit in the parity tests). TS code that mutated a shared V3 is ported with refs.
using System;

namespace ZU.Sim
{
    [Serializable]
    public struct V3
    {
        public double x, y, z;
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static readonly V3 Zero = new V3(0, 0, 0), Up = new V3(0, 1, 0);

        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator -(V3 a) => new V3(-a.x, -a.y, -a.z);
        public static V3 operator *(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 operator *(double s, V3 a) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 operator /(V3 a, double s) => new V3(a.x / s, a.y / s, a.z / s);

        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public double LengthXZ => Math.Sqrt(x * x + z * z);
        public static double Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        /// <summary>TS norm(): a zero vector stays zero (length treated as 1).</summary>
        public V3 Norm() { var l = Length; if (l == 0) l = 1; return new V3(x / l, y / l, z / l); }
        public static double Dist(V3 a, V3 b) => (a - b).Length;
        public static double DistXZ(V3 a, V3 b) { double dx = a.x - b.x, dz = a.z - b.z; return Math.Sqrt(dx * dx + dz * dz); }
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    /// <summary>JS Math helpers the port leans on, with JS semantics where they differ from C#.</summary>
    public static class M
    {
        public const double PI = Math.PI;
        public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
        public static double Hypot(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        /// <summary>JS Math.sign: 0 for 0.</summary>
        public static double Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0;
        /// <summary>wrap an angle to (-PI, PI]</summary>
        public static double WrapAngle(double a) { while (a > PI) a -= 2 * PI; while (a <= -PI) a += 2 * PI; return a; }
    }
}
