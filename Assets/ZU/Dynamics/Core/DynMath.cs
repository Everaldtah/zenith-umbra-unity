// Small vector helpers the solver needs that Unity.Mathematics doesn't ship. Burst-compatible (pure functions on blittable types).
using Unity.Mathematics;

namespace ZU.Dynamics
{
    public static class DynMath
    {
        /// <summary>Shortest rotation taking unit vector `a` onto unit vector `b` (three.js setFromUnitVectors).</summary>
        public static quaternion FromTo(float3 a, float3 b)
        {
            float d = math.dot(a, b);
            if (d < -0.999999f)
            {
                // antiparallel: 180 degrees about any axis perpendicular to a
                float3 axis = math.cross(new float3(1, 0, 0), a);
                if (math.lengthsq(axis) < 1e-6f) axis = math.cross(new float3(0, 1, 0), a);
                return quaternion.AxisAngle(math.normalize(axis), math.PI);
            }
            float3 c = math.cross(a, b);
            return math.normalize(new quaternion(c.x, c.y, c.z, 1f + d));
        }

        /// <summary>`dir` (unit) pulled back into a cone of half-angle `maxA` about `axis` (unit), on the great circle between them.</summary>
        public static float3 ConeLimit(float3 dir, float3 axis, float maxA)
        {
            float d = math.clamp(math.dot(dir, axis), -1f, 1f);
            float ang = math.acos(d);
            if (ang <= maxA) return dir;
            float3 perp = dir - axis * d;
            float pl = math.length(perp);
            if (pl < 1e-7f) return axis;    // exactly opposite: no preferred side, snap to the axis
            math.sincos(maxA, out float s, out float c);
            return math.normalize(axis * c + perp * (s / pl));
        }

        /// <summary>Closest point on segment ab to p.</summary>
        public static float3 ClosestOnSegment(float3 p, float3 a, float3 b)
        {
            float3 ab = b - a;
            float l2 = math.lengthsq(ab);
            float u = l2 > 1e-9f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return a + ab * u;
        }

        /// <summary>Push `x` out of the capsule (a, b, r) if it is inside. A particle sitting on the axis goes straight up.</summary>
        public static void PushOut(ref float3 x, float3 a, float3 b, float r)
        {
            float3 cp = ClosestOnSegment(x, a, b);
            float3 d = x - cp;
            float l = math.length(d);
            if (l < r)
            {
                if (l > 1e-6f) x = cp + d * (r / l);
                else x = cp + new float3(0, r, 0);
            }
        }

        /// <summary>How deep `x` sits inside the capsule (0 when outside). Diagnostics / tests.</summary>
        public static float Penetration(float3 x, float3 a, float3 b, float r)
        {
            float l = math.length(x - ClosestOnSegment(x, a, b));
            return math.max(0f, r - l);
        }

        /// <summary>Rotate p about the vertical axis through c by `ang` radians (TS yaw convention: +ang turns +Z toward +X).</summary>
        public static float3 YawAbout(float3 p, float3 c, float ang)
        {
            math.sincos(ang, out float sn, out float cs);
            float dx = p.x - c.x, dz = p.z - c.z;
            return new float3(c.x + dx * cs + dz * sn, p.y, c.z - dx * sn + dz * cs);
        }

        public static bool Finite(float3 v) => math.all(math.isfinite(v));

        /// <summary>Wrap an angle to [-pi, pi].</summary>
        public static float WrapAngle(float a) => math.atan2(math.sin(a), math.cos(a));
    }
}
