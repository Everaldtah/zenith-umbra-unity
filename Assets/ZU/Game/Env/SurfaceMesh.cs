// Builds the meshes the map surfaces are drawn with: one mesh per material (the blockout boxes, decor, the outer world's
// buildings all merge into a handful of draw calls), with the vertex data the ZU/Surface shader reads:
//   uv0 = world-metric UV (metres; the material tiles 1 / tile),
//   uv2 = xyz the vertex relative to its box's centre, w the grime weight (walls standing on the ground),
//   uv3 = xyz the box's half extents (1e4 = not a box: no painted bevel), w the world Y of the box's foot.
// Everything is in Unity world space (sim coordinates already converted by Conv.U); boxes are axis aligned.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.Game.Env
{
    public sealed class SurfaceMesh
    {
        public const float NoBox = 1e4f;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector4> t = new List<Vector4>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector4> uv2 = new List<Vector4>();
        readonly List<Vector4> uv3 = new List<Vector4>();
        readonly List<int> idx = new List<int>();
        public int VertexCount => v.Count;

        /// <summary>a transform applied to everything added (buildings turned to face the arena); while it rotates, boxes
        /// draw without the painted bevel (ZU/Surface reads box data in world axes)</summary>
        public Matrix4x4 xf = Matrix4x4.identity;
        bool Rotated => Mathf.Abs(xf.m01) + Mathf.Abs(xf.m02) + Mathf.Abs(xf.m10) + Mathf.Abs(xf.m12) + Mathf.Abs(xf.m20) + Mathf.Abs(xf.m21) > 1e-4f;
        Vector3 P(Vector3 p) => xf.MultiplyPoint3x4(p);
        Vector3 D(Vector3 d) => xf.MultiplyVector(d).normalized;

        // the six faces of a box: outward normal, the face's "right" (U) and "up" (V) as seen from outside, in Unity's
        // left-handed frame, so textures read upright and unmirrored on walls
        static readonly (Vector3 n, Vector3 u, Vector3 v)[] FACES =
        {
            (Vector3.right,   Vector3.forward, Vector3.up),
            (Vector3.left,    Vector3.back,    Vector3.up),
            (Vector3.forward, Vector3.left,    Vector3.up),
            (Vector3.back,    Vector3.right,   Vector3.up),
            (Vector3.up,      Vector3.right,   Vector3.forward),
            (Vector3.down,    Vector3.right,   Vector3.back),
        };

        /// <summary>an axis-aligned box. bevel: draw its edges rounded (ZU/Surface); grime: darken its foot;
        /// skipBottom for boxes nobody sees from below</summary>
        public void Box(Vector3 center, Vector3 half, bool bevel = true, float grime = 0, bool skipBottom = false)
        {
            bool rot = Rotated;
            var wc = P(center);
            float foot = wc.y - half.y;
            var bh = bevel && !rot ? (Vector3)half : new Vector3(NoBox, NoBox, NoBox);
            for (int f = 0; f < 6; f++)
            {
                if (skipBottom && f == 5) continue;
                var (fn0, fu0, fv0) = FACES[f];
                float hn = Mathf.Abs(Vector3.Dot(half, fn0)), hu = Mathf.Abs(Vector3.Dot(half, fu0)), hv = Mathf.Abs(Vector3.Dot(half, fv0));
                if (hu < 1e-5f || hv < 1e-5f) continue;
                var c = center + fn0 * hn;
                var bl = c - fu0 * hu - fv0 * hv; var tl = c - fu0 * hu + fv0 * hv; var tr = c + fu0 * hu + fv0 * hv; var br = c + fu0 * hu - fv0 * hv;
                Vector3 fn = D(fn0), fu = D(fu0), fv = D(fv0);
                int k = v.Count;
                foreach (var p0 in new[] { bl, tl, tr, br })
                {
                    var p = P(p0);
                    v.Add(p); n.Add(fn); t.Add(Tangent(fn, fu, fv));
                    uv.Add(new Vector2(Vector3.Dot(p, fu), Vector3.Dot(p, fv)));
                    uv2.Add(new Vector4(p.x - wc.x, p.y - wc.y, p.z - wc.z, grime));
                    uv3.Add(new Vector4(bh.x, bh.y, bh.z, foot));
                }
                idx.Add(k); idx.Add(k + 1); idx.Add(k + 2); idx.Add(k); idx.Add(k + 2); idx.Add(k + 3);
            }
        }

        /// <summary>a flat polygon (convex, 3+ points, any winding): faces `outward`, UV planar along its own slope
        /// (U = the horizontal edge direction, V = up the face) so roof tiles run down the slope</summary>
        public void Poly(IList<Vector3> pts, Vector3 outward)
        {
            if (pts.Count < 3) return;
            if (xf != Matrix4x4.identity) { var tp = new List<Vector3>(pts.Count); foreach (var q in pts) tp.Add(P(q)); pts = tp; outward = xf.MultiplyVector(outward); }
            var nrm = Vector3.zero;
            for (int i = 1; i + 1 < pts.Count; i++) nrm += Vector3.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
            if (nrm.sqrMagnitude < 1e-12f) return;
            nrm.Normalize();
            bool flip = Vector3.Dot(nrm, outward) < 0;
            if (flip) nrm = -nrm;
            var fu = Vector3.Cross(Vector3.up, nrm); if (fu.sqrMagnitude < 1e-6f) fu = Vector3.right; fu.Normalize();
            var fv = Vector3.Cross(nrm, fu).normalized;
            int k = v.Count;
            foreach (var p in pts)
            {
                v.Add(p); n.Add(nrm); t.Add(Tangent(nrm, fu, fv));
                uv.Add(new Vector2(Vector3.Dot(p, fu), Vector3.Dot(p, fv)));
                uv2.Add(Vector4.zero); uv3.Add(new Vector4(NoBox, NoBox, NoBox, p.y));
            }
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                // Unity's front face: cross(b - a, c - a) points at the viewer, so keep the order whose cross is outward
                if (!flip) { idx.Add(k); idx.Add(k + i); idx.Add(k + i + 1); }
                else { idx.Add(k); idx.Add(k + i + 1); idx.Add(k + i); }
            }
        }

        /// <summary>a vertical prism / frustum around the Y axis (towers, chimneys, tanks, posts, rock skirts):
        /// `sides` segments, radius r0 at y0 to r1 at y1, optional caps, smooth sides, UV wraps in metres</summary>
        public void Cylinder(Vector3 baseCenter, float r0, float r1, float height, int sides = 12, bool top = true, bool bottom = false, float phase = 0)
        {
            sides = Mathf.Max(3, sides);
            float circ = Mathf.PI * (r0 + r1);
            int k = v.Count;
            float slope = (r0 - r1) / Mathf.Max(1e-4f, height);
            for (int i = 0; i <= sides; i++)
            {
                float a = phase + i * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var nrm = (dir + Vector3.up * slope).normalized;
                var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                float u = (float)i / sides * circ;
                foreach (var (r, y) in new[] { (r0, 0f), (r1, height) })
                {
                    var p = P(baseCenter + dir * r + Vector3.up * y);
                    var tw = D(tan);
                    v.Add(p); n.Add(D(nrm)); t.Add(new Vector4(tw.x, tw.y, tw.z, -1));   // u runs with the angle; cross(n, t) is down, so w = -1
                    uv.Add(new Vector2(u, p.y)); uv2.Add(Vector4.zero); uv3.Add(new Vector4(NoBox, NoBox, NoBox, p.y));
                }
            }
            for (int i = 0; i < sides; i++)
            {
                int a = k + i * 2, b = a + 2;
                idx.Add(a); idx.Add(a + 1); idx.Add(b + 1); idx.Add(a); idx.Add(b + 1); idx.Add(b);
            }
            if (top && r1 > 1e-3f) Cap(baseCenter + Vector3.up * height, r1, sides, true, phase);
            if (bottom && r0 > 1e-3f) Cap(baseCenter, r0, sides, false, phase);
        }

        void Cap(Vector3 c, float r, int sides, bool up, float phase)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < sides; i++) { float a = phase + i * Mathf.PI * 2 / sides; pts.Add(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r); }
            Poly(pts, up ? Vector3.up : Vector3.down);
        }

        /// <summary>a dome (hemisphere scaled), for observatories and lantern tops</summary>
        public void Dome(Vector3 baseCenter, float radius, float height, int sides = 16, int rings = 6)
        {
            int k = v.Count;
            for (int j = 0; j <= rings; j++)
            {
                float el = j * Mathf.PI * 0.5f / rings;
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                    var p = P(baseCenter + new Vector3(d.x * radius, d.y * height, d.z * radius));
                    var nrm = D(new Vector3(d.x / radius, d.y / Mathf.Max(1e-3f, height), d.z / radius));
                    var tan = D(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)));
                    v.Add(p); n.Add(nrm); t.Add(new Vector4(tan.x, tan.y, tan.z, -1));
                    uv.Add(new Vector2(a * radius, el * radius)); uv2.Add(Vector4.zero); uv3.Add(new Vector4(NoBox, NoBox, NoBox, baseCenter.y));
                }
            }
            int row = sides + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = k + j * row + i, b = a + row;
                    idx.Add(a); idx.Add(b); idx.Add(b + 1); idx.Add(a); idx.Add(b + 1); idx.Add(a + 1);
                }
        }

        static Vector4 Tangent(Vector3 nrm, Vector3 u, Vector3 vv)
        {
            // Unity: bitangent = cross(normal, tangent) * w
            float w = Vector3.Dot(Vector3.Cross(nrm, u), vv) >= 0 ? 1f : -1f;
            return new Vector4(u.x, u.y, u.z, w);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetTangents(t);
            m.SetUVs(0, uv); m.SetUVs(2, uv2); m.SetUVs(3, uv3);
            m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>meshes being built for one parent, one SurfaceMesh per material key</summary>
    public sealed class MeshBins
    {
        readonly Dictionary<string, SurfaceMesh> bins = new Dictionary<string, SurfaceMesh>();
        Matrix4x4 m_xf = Matrix4x4.identity;
        public SurfaceMesh this[string key] { get { if (!bins.TryGetValue(key, out var m)) bins[key] = m = new SurfaceMesh { xf = m_xf }; return m; } }
        /// <summary>the transform every bin applies to what is added next (place / turn a whole building at once)</summary>
        public Matrix4x4 Xf { get => m_xf; set { m_xf = value; foreach (var m in bins.Values) m.xf = value; } }

        /// <summary>turn every bin into a static mesh renderer under `parent` with the material `mat(key)`</summary>
        public void Emit(Transform parent, System.Func<string, Material> mat, bool castShadows = true, string prefix = "")
        {
            foreach (var kv in bins)
            {
                if (kv.Value.VertexCount == 0) continue;
                var go = new GameObject(prefix + kv.Key);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh(prefix + kv.Key);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat(kv.Key);
                r.shadowCastingMode = castShadows && kv.Key != "ground" ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.isStatic = true;
            }
            bins.Clear();
        }
    }
}
