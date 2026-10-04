// What the ability views (SpiritDragon, ChainCage, SealStorm, PuppetSwarm, Eyes, Ragdoll, UltShowcase) share:
//  - Sim space. The ports keep the TS maths line for line, in the TS's own right-handed space (the simulation's): a
//    Vector3 there is just three floats. Only at the draw call does a point / direction / rotation cross into Unity
//    through Sp.U (the same X mirror as Conv). Rotations cross by conjugation with the mirror: (w, x, y, z) -> (w, x, -y, -z).
//  - InstancedBatch: the TS InstancedMesh (matrices + a per-instance colour or strength), drawn each frame with
//    Graphics.DrawMeshInstanced (up to 1023 a call; every view here draws fewer).
//  - The materials (Resources/ZUAbilityFx/*.mat, so a player build keeps the shaders) and the procedural meshes the TS
//    built from three.js geometries (rings, tori, cylinders, cones, spheres, boxes, planes).
//  - PropParts: a published Tripo prop (Resources/ZUProps/<id>.prefab) as its meshes plus the matrix that normalises them
//    the way the TS normalised the merged geometry - no vertex reads, so the props need no Read/Write import flag.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Sim;

namespace ZU.Game.Fx
{
    /// <summary>sim space (the TS's right-handed space) -> Unity, and back</summary>
    public static class Sp
    {
        /// <summary>a sim-space value as a Vector3 (still in sim space)</summary>
        public static Vector3 V(V3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        public static Vector3 V(double x, double y, double z) => new Vector3((float)x, (float)y, (float)z);
        /// <summary>a sim-space point or direction -> Unity</summary>
        public static Vector3 U(Vector3 s) => new Vector3(-s.x, s.y, s.z);
        /// <summary>a sim-space rotation -> Unity (conjugated by the X mirror)</summary>
        public static Quaternion U(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
        /// <summary>Unity -> sim space (the mirror is its own inverse)</summary>
        public static Vector3 S(Vector3 u) => new Vector3(-u.x, u.y, u.z);
        public static V3 ToV3(Vector3 s) => new V3(s.x, s.y, s.z);
        /// <summary>a sim-space transform as a Unity matrix</summary>
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => Matrix4x4.TRS(U(p), U(q), s);
        /// <summary>three.js Euler(x, y, z, 'YXZ'): Unity's Euler order is the same (radians in, degrees out)</summary>
        public static Quaternion EulerYXZ(float x, float y, float z) => Quaternion.Euler(x * Mathf.Rad2Deg, y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);
        /// <summary>three.js Euler(x, y, z) (default order 'XYZ')</summary>
        public static Quaternion EulerXYZ(float x, float y, float z) =>
            Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        /// <summary>three.js Matrix4.makeBasis(x, y, z) as a rotation, where x = y cross z (a right-handed basis)</summary>
        public static Quaternion Basis(Vector3 y, Vector3 z) => Quaternion.LookRotation(z, y);

        /// <summary>a TS '#rrggbb' colour as the linear value three.js would hold (what per-instance arrays need: they
        /// skip the gamma conversion SetColor does)</summary>
        public static Color Lin(Color srgb) => QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;
        public static Color Hex(string hex) => Conv.Hex(hex);
    }

    /// <summary>the TS InstancedMesh: one mesh drawn at many matrices, each with its own colour (`_Tint`) or strength (`_K`)</summary>
    public sealed class InstancedBatch
    {
        public Mesh mesh;
        public Material mat;
        /// <summary>applied under every instance (a prop's normalisation, PropParts.Part.pre)</summary>
        public Matrix4x4 pre = Matrix4x4.identity;
        public ShadowCastingMode shadows = ShadowCastingMode.Off;
        public bool receiveShadows;
        public int Count => n;
        readonly Matrix4x4[] m;
        readonly Vector4[] tint;
        readonly float[] k;
        readonly bool strength;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        int n;

        /// <param name="strength">true: a float `_K` per instance (the chains); false: a colour `_Tint`</param>
        public InstancedBatch(Mesh mesh, Material mat, int max, bool strength = false)
        {
            this.mesh = mesh; this.mat = mat; this.strength = strength;
            mat.enableInstancing = true;
            m = new Matrix4x4[Mathf.Min(max, 1023)];
            if (strength) k = new float[m.Length]; else tint = new Vector4[m.Length];
        }
        public int Max => m.Length;
        public void Clear() => n = 0;
        /// <summary>an instance; `linearTint` is already linear (Sp.Lin)</summary>
        public void Add(Matrix4x4 world, Color linearTint)
        {
            if (n >= m.Length) return;
            m[n] = world * pre; tint[n] = linearTint; n++;
        }
        public void Add(Matrix4x4 world, float strengthK)
        {
            if (n >= m.Length) return;
            m[n] = world * pre; k[n] = strengthK; n++;
        }
        public void Draw(int layer = 0)
        {
            if (n == 0 || mesh == null || mat == null) return;
            if (strength) mpb.SetFloatArray("_K", k); else mpb.SetVectorArray("_Tint", tint);
            for (int s = 0; s < mesh.subMeshCount; s++)
                Graphics.DrawMeshInstanced(mesh, s, mat, m, n, mpb, shadows, receiveShadows, layer);
        }
    }

    public static class AbilityKit
    {
        // ------------------------------------------------------------------------------------------------ materials
        static readonly Dictionary<string, Material> protos = new Dictionary<string, Material>();
        /// <summary>a fresh material from Resources/ZUAbilityFx/<name> (keeps the shader in a build), else from the shader</summary>
        public static Material Mat(string name, string shader)
        {
            if (!protos.TryGetValue(name, out var p) || p == null)
            {
                p = Resources.Load<Material>("ZUAbilityFx/" + name);
                if (p == null) { var sh = Shader.Find(shader); p = sh != null ? new Material(sh) : new Material(Shader.Find("Universal Render Pipeline/Unlit")); }
                protos[name] = p;
            }
            return new Material(p) { name = name };
        }
        public static Material Dragon() => Mat("dragon", "ZU/DragonHolo");
        public static Material Chain() => Mat("chain", "ZU/ChainHolo");
        public static Material Lit() => Mat("lit", "ZU/AbilityLit");
        public static Material Additive() => Mat("additive", "ZU/AbilityAdditive");

        static Texture2D white;
        public static Texture2D White
        {
            get
            {
                if (white != null) return white;
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "ability white" };
                white.SetPixel(0, 0, Color.white); white.Apply();
                return white;
            }
        }

        /// <summary>the colour map of a material (URP _BaseMap, else the main texture)</summary>
        public static Texture MapOf(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
            return m.HasProperty("_MainTex") ? m.mainTexture : null;
        }

        // ------------------------------------------------------------------------------------------------ meshes
        public sealed class MeshBuilder
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public int Count => v.Count;
            public void Add(Vector3 p, Vector3 nrm, Vector2 u, Color col) { v.Add(p); n.Add(nrm); uv.Add(u); c.Add(col); }
            public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }
            /// <summary>append another builder's geometry, transformed</summary>
            public void Append(MeshBuilder o, Matrix4x4 xf, Color? col = null)
            {
                int b = v.Count;
                var nx = xf.inverse.transpose;                  // normals: the inverse transpose (three's normalMatrix)
                for (int i = 0; i < o.v.Count; i++) Add(xf.MultiplyPoint3x4(o.v[i]), nx.MultiplyVector(o.n[i]).normalized, o.uv[i], col ?? o.c[i]);
                foreach (var i in o.t) t.Add(b + i);
            }
            public Mesh Build(string name, bool recalcNormals = false)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
                if (recalcNormals) m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>three RingGeometry(r0, r1, segs): an annulus in the XY plane facing +Z</summary>
        public static MeshBuilder Ring(float r0, float r1, int segs)
        {
            var b = new MeshBuilder();
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2 / segs, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                b.Add(new Vector3(cs * r0, sn * r0, 0), Vector3.forward, new Vector2((r0 / r1 * cs + 1) / 2, (r0 / r1 * sn + 1) / 2), Color.white);
                b.Add(new Vector3(cs * r1, sn * r1, 0), Vector3.forward, new Vector2((cs + 1) / 2, (sn + 1) / 2), Color.white);
                if (i < segs) { int k = i * 2; b.Tri(k, k + 1, k + 3); b.Tri(k, k + 3, k + 2); }
            }
            return b;
        }
        /// <summary>three PlaneGeometry(w, h, sx, sy): in the XY plane facing +Z, uv (0,0) bottom-left</summary>
        public static MeshBuilder Plane(float w, float h, int sx = 1, int sy = 1)
        {
            var b = new MeshBuilder();
            for (int y = 0; y <= sy; y++)
                for (int x = 0; x <= sx; x++)
                {
                    float u = (float)x / sx, vv = (float)y / sy;
                    b.Add(new Vector3((u - 0.5f) * w, (vv - 0.5f) * h, 0), Vector3.forward, new Vector2(u, vv), Color.white);
                }
            for (int y = 0; y < sy; y++)
                for (int x = 0; x < sx; x++)
                {
                    int a = y * (sx + 1) + x, bb = a + 1, c = a + sx + 1, d = c + 1;
                    b.Tri(a, bb, d); b.Tri(a, d, c);
                }
            return b;
        }
        /// <summary>three TorusGeometry(R, r, radialSegs, tubularSegs): in the XY plane, round +Z</summary>
        public static MeshBuilder Torus(float R, float r, int rs, int ts)
        {
            var b = new MeshBuilder();
            for (int j = 0; j <= rs; j++)
                for (int i = 0; i <= ts; i++)
                {
                    float u = (float)i / ts * Mathf.PI * 2, w = (float)j / rs * Mathf.PI * 2;
                    var centre = new Vector3(R * Mathf.Cos(u), R * Mathf.Sin(u), 0);
                    var p = new Vector3((R + r * Mathf.Cos(w)) * Mathf.Cos(u), (R + r * Mathf.Cos(w)) * Mathf.Sin(u), r * Mathf.Sin(w));
                    b.Add(p, (p - centre).normalized, new Vector2((float)i / ts, (float)j / rs), Color.white);
                }
            for (int j = 1; j <= rs; j++)
                for (int i = 1; i <= ts; i++)
                {
                    int a = (ts + 1) * j + i - 1, bb = (ts + 1) * (j - 1) + i - 1, c = (ts + 1) * (j - 1) + i, d = (ts + 1) * j + i;
                    b.Tri(a, bb, d); b.Tri(bb, c, d);
                }
            return b;
        }
        /// <summary>three CylinderGeometry(rTop, rBottom, h, segs, 1, open): centred on the origin along +Y</summary>
        public static MeshBuilder Cylinder(float rTop, float rBot, float h, int segs, bool open = false)
        {
            var b = new MeshBuilder();
            float slope = (rBot - rTop) / h;
            for (int y = 0; y <= 1; y++)
                for (int i = 0; i <= segs; i++)
                {
                    float a = (float)i / segs * Mathf.PI * 2, r = y == 0 ? rTop : rBot, py = y == 0 ? h / 2 : -h / 2;
                    float sx = Mathf.Sin(a), cz = Mathf.Cos(a);
                    b.Add(new Vector3(r * sx, py, r * cz), new Vector3(sx, slope, cz).normalized, new Vector2((float)i / segs, 1 - y), Color.white);
                }
            for (int i = 0; i < segs; i++) { int a = i, bb = segs + 1 + i, c = segs + 2 + i, d = i + 1; b.Tri(a, bb, d); b.Tri(bb, c, d); }
            if (!open)
                for (int cap = 0; cap <= 1; cap++)
                {
                    float r = cap == 0 ? rTop : rBot, py = cap == 0 ? h / 2 : -h / 2, s = cap == 0 ? 1 : -1;
                    if (r <= 0) continue;
                    int centre = b.Count; b.Add(new Vector3(0, py, 0), new Vector3(0, s, 0), new Vector2(0.5f, 0.5f), Color.white);
                    for (int i = 0; i <= segs; i++) { float a = (float)i / segs * Mathf.PI * 2; b.Add(new Vector3(r * Mathf.Sin(a), py, r * Mathf.Cos(a)), new Vector3(0, s, 0), new Vector2(0.5f + Mathf.Sin(a) / 2, 0.5f + Mathf.Cos(a) / 2), Color.white); }
                    for (int i = 0; i < segs; i++) { if (cap == 0) b.Tri(centre, centre + 1 + i, centre + 2 + i); else b.Tri(centre, centre + 2 + i, centre + 1 + i); }
                }
            return b;
        }
        /// <summary>three ConeGeometry(r, h, segs, 1, open): apex at +h/2</summary>
        public static MeshBuilder Cone(float r, float h, int segs, bool open = false) => Cylinder(0, r, h, segs, open);
        /// <summary>three SphereGeometry(r, ws, hs)</summary>
        public static MeshBuilder Sphere(float r, int ws, int hs)
        {
            var b = new MeshBuilder();
            for (int y = 0; y <= hs; y++)
                for (int x = 0; x <= ws; x++)
                {
                    float u = (float)x / ws, vv = (float)y / hs;
                    var d = new Vector3(-Mathf.Cos(u * Mathf.PI * 2) * Mathf.Sin(vv * Mathf.PI), Mathf.Cos(vv * Mathf.PI), Mathf.Sin(u * Mathf.PI * 2) * Mathf.Sin(vv * Mathf.PI));
                    b.Add(d * r, d, new Vector2(u, 1 - vv), Color.white);
                }
            for (int y = 0; y < hs; y++)
                for (int x = 0; x < ws; x++)
                {
                    int a = y * (ws + 1) + x + 1, bb = y * (ws + 1) + x, c = (y + 1) * (ws + 1) + x, d = (y + 1) * (ws + 1) + x + 1;
                    if (y != 0) b.Tri(a, bb, d);
                    if (y != hs - 1) b.Tri(bb, c, d);
                }
            return b;
        }
        /// <summary>three BoxGeometry(w, h, d)</summary>
        public static MeshBuilder Box(float w, float h, float d)
        {
            var b = new MeshBuilder();
            void Face(Vector3 nrm, Vector3 u, Vector3 v, float su, float sv, float sn)
            {
                int i0 = b.Count;
                for (int k = 0; k < 4; k++)
                {
                    float a = k == 1 || k == 2 ? 1 : -1, c = k >= 2 ? 1 : -1;
                    b.Add(nrm * sn / 2 + u * a * su / 2 + v * c * sv / 2, nrm, new Vector2((a + 1) / 2, (c + 1) / 2), Color.white);
                }
                b.Tri(i0, i0 + 2, i0 + 1); b.Tri(i0, i0 + 3, i0 + 2);
            }
            Face(Vector3.right, Vector3.forward, Vector3.up, d, h, w); Face(Vector3.left, Vector3.back, Vector3.up, d, h, w);
            Face(Vector3.up, Vector3.right, Vector3.forward, w, d, h); Face(Vector3.down, Vector3.right, Vector3.back, w, d, h);
            Face(Vector3.forward, Vector3.left, Vector3.up, w, h, d); Face(Vector3.back, Vector3.right, Vector3.up, w, h, d);
            return b;
        }
    }

    /// <summary>a published prop's meshes (Resources/ZUProps/<id>.prefab) and the matrix that puts them in the frame the TS
    /// normalised its merged geometry to (the bounds are read from the mesh headers, never the vertices)</summary>
    public sealed class PropParts
    {
        public struct Part { public Mesh mesh; public Matrix4x4 pre; public Material source; }
        public readonly List<Part> parts = new List<Part>();
        public Bounds bounds;           // all parts, in the prefab root's frame
        public Texture map;             // the first part's colour map
        public Material firstMat;

        public static PropParts Load(string id)
        {
            var prefab = Resources.Load<GameObject>("ZUProps/" + id);
            return prefab != null ? From(prefab) : null;
        }

        public static PropParts From(GameObject prefab)
        {
            var p = new PropParts();
            var root = prefab.transform;
            bool any = false;
            void Take(Mesh mesh, Transform t, Material[] mats)
            {
                if (mesh == null) return;
                var pre = root.worldToLocalMatrix * t.localToWorldMatrix;
                p.parts.Add(new Part { mesh = mesh, pre = pre, source = mats != null && mats.Length > 0 ? mats[0] : null });
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = pre.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { p.bounds = new Bounds(c, Vector3.zero); any = true; } else p.bounds.Encapsulate(c);
                }
            }
            // a prop with LODs (the Tripo imports: <id>_LOD0/1/2 under a LODGroup) is drawn from its first LOD only - every
            // level at once is the same arrow three times over, z-fighting
            HashSet<Renderer> only = null;
            var lod = prefab.GetComponentInChildren<LODGroup>(true);
            if (lod != null) { var lods = lod.GetLODs(); if (lods.Length > 0) { only = new HashSet<Renderer>(); foreach (var r in lods[0].renderers) if (r != null) only.Add(r); } }
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>();
                if (only != null && only.Count > 0 && !only.Contains(r)) continue;
                Take(mf.sharedMesh, mf.transform, r?.sharedMaterials);
            }
            foreach (var sm in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (only == null || only.Count == 0 || only.Contains(sm)) Take(sm.sharedMesh, sm.transform, sm.sharedMaterials);
            if (p.parts.Count == 0) return null;
            p.firstMat = p.parts[0].source;
            foreach (var part in p.parts) { p.map = AbilityKit.MapOf(part.source); if (p.map != null) { p.firstMat = part.source; break; } }
            return p;
        }

        /// <summary>every part's matrix followed by `norm` (a normalisation in the prefab root's frame)</summary>
        public void Normalise(Matrix4x4 norm)
        {
            for (int i = 0; i < parts.Count; i++) { var q = parts[i]; q.pre = norm * q.pre; parts[i] = q; }
        }
    }
}
