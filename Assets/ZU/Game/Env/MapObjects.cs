// The objects players read a match from (src/render/MapScene.ts): the capture point (a ring in the owner's colour, a
// disc with the capture progress arc and rings rippling outward, a 40 m beam), the health packs (a pedestal with a
// floating, turning cross; a ring that refills while one respawns), the jump pads (ring, disc, a bobbing arrow along the
// launch) and, on Mikoshi Rush maps, the festival float riding the payload position with its lit route. Built with the
// level, driven every frame from the match's world.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public sealed class MapObjects : MonoBehaviour
    {
        static readonly Color Z = Conv.Hex("#5cc8ff"), UM = Conv.Hex("#ff3b5c"), CONTEST = Conv.Hex("#ffcc33");

        MapDef map; ILevel level; MatchRunner runner;
        // the point
        Material ringMat, discMat, arcMat, beamMat, rippleMat;
        Mesh arcMesh; int arcPct = -1;
        readonly List<Transform> ripples = new List<Transform>();
        // packs and pads
        sealed class PackView { public Transform cross, ring; public Material ringMat; public bool big; }
        readonly List<PackView> packs = new List<PackView>();
        readonly List<(Transform g, Transform arrow)> pads = new List<(Transform, Transform)>();
        // the float
        Transform floatT; Bounds floatBox; Renderer[] floatRends; V3 lastPush; bool hasPush;

        public static MapObjects Build(MapDef map, Transform parent, ILevel level, System.Func<string, Material> mat)
        {
            var go = new GameObject("Map Objects"); go.transform.SetParent(parent, false);
            var o = go.AddComponent<MapObjects>();
            o.map = map; o.level = level; o.runner = parent.GetComponentInParent<MatchRunner>();
            o.Point(); o.Packs(); o.Pads(); o.Float();
            return o;
        }

        double Ground(double x, double z, double from)
        {
            double g = level != null ? level.GroundAt(x, z, from) : 0;
            return double.IsInfinity(g) || double.IsNaN(g) ? 0 : g;
        }

        // ------------------------------------------------------------------ materials
        static Material Unlit(Color c, bool additive = false)
        {
            var sh = Shader.Find(additive ? "ZU/FxAdditive" : "ZU/FxAlpha");
            var m = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            return m;
        }
        static Material Lit(Color c, float smooth, float metal)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }
        /// <summary>an emissive lit material (a copy of the shipped window material, so the emission variant ships)</summary>
        static Material Glow(Color baseC, Color emit)
        {
            var src = Resources.Load<Material>("ZUEnv/common_window");
            var m = src != null ? new Material(src) : Lit(baseC, 0.7f, 0);
            m.SetColor("_BaseColor", baseC); m.SetColor("_EmissionColor", emit); m.SetFloat("_Smoothness", 0.7f); m.SetFloat("_Metallic", 0);
            return m;
        }
        static Color A(Color c, float a) { c.a = a; return c; }

        GameObject Part(string name, Transform parent, Mesh mesh, Material m, Vector3 pos, bool shadows = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ------------------------------------------------------------------ the capture point
        void Point()
        {
            if (map.id == "training" || map.objective == "push" || map.point == null || map.point.Length != 3) return;
            var root = new GameObject("Point").transform; root.SetParent(transform, false);
            root.localPosition = Conv.U(map.point[0], map.point[1], map.point[2]);
            ringMat = Unlit(A(Color.white, 0.9f));
            Part("ring", root, MeshKit.Torus(6, 0.12f, 64, 8), ringMat, new Vector3(0, 0.06f, 0));
            // the disc: a faint base, the capture progress arc on its rim (0.86 .. 0.96 of the radius), rings rippling out
            discMat = Unlit(A(Color.white, 0.08f));
            Part("disc", root, MeshKit.Annulus(0, 6, 64, 360), discMat, new Vector3(0, 0.05f, 0));
            arcMat = Unlit(A(Color.white, 0.8f));
            arcMesh = new Mesh { name = "point arc" };
            Part("arc", root, arcMesh, arcMat, new Vector3(0, 0.055f, 0));
            rippleMat = Unlit(A(Color.white, 0.25f));
            var unit = MeshKit.Annulus(0.98f, 1.0f, 64, 360);
            for (int i = 0; i < 4; i++) ripples.Add(Part("ripple", root, unit, rippleMat, new Vector3(0, 0.052f, 0)).transform);
            beamMat = Unlit(A(Color.white, 0.12f), additive: true);
            Part("beam", root, MeshKit.OpenCylinder(0.4f, 40, 12), beamMat, Vector3.zero);
        }

        // ------------------------------------------------------------------ health packs
        void Packs()
        {
            var w = runner?.World;
            var list = new List<(double x, double y, double z, bool big)>();
            if (w != null) foreach (var p in w.packs) list.Add((p.x, p.y, p.z, p.big));
            else if (map.packs != null) foreach (var p in map.packs) list.Add((p.x, p.y ?? System.Math.Max(0, Ground(p.x, p.z, 0.3)), p.z, p.big));
            if (list.Count == 0) return;
            var baseMat = Lit(Conv.Hex("#e9f3f2"), 0.6f, 0.2f);
            var rimMat = Unlit(Conv.Hex("#2fe3b0"));
            var crossMat = Glow(Conv.Hex("#b8ffd9"), Conv.Hex("#29f0a0") * 1.6f);
            var haloMat = Unlit(A(Conv.Hex("#29f0a0"), 0.18f), additive: true);
            var cube = MeshKit.Box();
            foreach (var (x, y, z, big) in list)
            {
                float k = big ? 1.35f : 1;
                var g = new GameObject("Health Pack").transform; g.SetParent(transform, false);
                g.localPosition = Conv.U(x, y, z);
                Part("base", g, MeshKit.Cylinder(0.72f * k, 0.62f * k, 0.22f, 20), baseMat, Vector3.zero, shadows: true);
                Part("rim", g, MeshKit.Torus(0.62f * k, 0.05f, 28, 6), rimMat, new Vector3(0, 0.23f, 0));
                var cross = new GameObject("cross").transform; cross.SetParent(g, false); cross.localPosition = new Vector3(0, 0.95f, 0);
                Part("h", cross, cube, crossMat, Vector3.zero).transform.localScale = new Vector3(0.62f * k, 0.2f * k, 0.2f * k);
                Part("v", cross, cube, crossMat, Vector3.zero).transform.localScale = new Vector3(0.2f * k, 0.62f * k, 0.2f * k);
                if (big) Part("halo", cross, MeshKit.Sphere(), haloMat, Vector3.zero).transform.localScale = Vector3.one * 1.1f;
                var rm = Unlit(A(Conv.Hex("#2fe3b0"), 0.8f));
                var ring = Part("ring", g, MeshKit.Annulus(0.66f * k, 0.78f * k, 32, 360), rm, new Vector3(0, 0.25f, 0)).transform;
                packs.Add(new PackView { cross = cross, ring = ring, ringMat = rm, big = big });
            }
        }

        // ------------------------------------------------------------------ jump pads
        void Pads()
        {
            if (map.pads == null) return;
            var tint = Conv.Hex(map.tint, Color.cyan);
            var ringMat = Unlit(tint); var discMat = Unlit(A(tint, 0.35f), additive: true); var arrowMat = Unlit(A(tint, 0.6f));
            foreach (var p in map.pads)
            {
                double y = p.y ?? Ground(p.x, p.z, 10);
                var g = new GameObject("Jump Pad").transform; g.SetParent(transform, false);
                g.localPosition = Conv.U(p.x, y + 0.05, p.z);
                Part("ring", g, MeshKit.Torus(1.3f, 0.15f, 32, 8), ringMat, Vector3.zero);
                Part("disc", g, MeshKit.Annulus(0, 1.3f, 32, 360), discMat, new Vector3(0, 0.02f, 0));
                var arrow = Part("arrow", g, MeshKit.Cone(0.5f, 1, 3), arrowMat, new Vector3(0, 1.2f, 0)).transform;
                var dir = Conv.U(p.vx, p.vy, p.vz).normalized;
                arrow.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                pads.Add((g, arrow));
            }
        }

        // ------------------------------------------------------------------ Mikoshi Rush: the float and its route
        void Float()
        {
            if (map.objective != "push" || map.path == null || map.path.Count < 2) return;
            var tint = Conv.Hex(map.tint, Color.white);
            var pts = new List<Vector3>();
            foreach (var q in map.path) pts.Add(Conv.U(q[0], System.Math.Max(0, Ground(q[0], q[1], 20)) + 0.06, q[1]));
            Part("Route", transform, MeshKit.Tube(MeshKit.CatmullRom(pts, pts.Count * 12), 0.14f, 6), Unlit(A(tint, 0.55f)), Vector3.zero);
            floatT = new GameObject("Mikoshi").transform; floatT.SetParent(transform, false);
            var prefab = Resources.Load<GameObject>("ZUProps/" + (map.payload ?? "prop_kagura_mikoshi"));
            if (prefab != null)
            {
                // the payload model at 3.4 m (prop prefabs are 1 m tall), turned inside the float when it was modelled facing
                // another way (payloadYaw)
                var turn = new GameObject("turn").transform; turn.SetParent(floatT, false);
                turn.localRotation = Conv.Yaw(map.payload != null ? map.payloadYaw ?? 0 : 0);
                var m = Instantiate(prefab, turn, false); m.transform.localScale = Vector3.one * 3.4f;
            }
            else
            {
                // MapScene's stand-in float: a white sled with a glowing skirt, a red body, a gold roof and bird, four posts
                var cube = MeshKit.Box();
                var gold = Lit(Conv.Hex("#e8b64a"), 0.7f, 0.75f); var red = Lit(Conv.Hex("#b3122e"), 0.55f, 0);
                Part("sled", floatT, cube, Lit(Conv.Hex("#f4f6fb"), 0.65f, 0.3f), new Vector3(0, 0.55f, 0), true).transform.localScale = new Vector3(3.2f, 0.5f, 2.2f);
                Part("glow", floatT, cube, Unlit(Conv.Hex("#5ff4ff")), new Vector3(0, 0.28f, 0)).transform.localScale = new Vector3(3.0f, 0.08f, 2.0f);
                Part("body", floatT, cube, red, new Vector3(0, 1.5f, 0), true).transform.localScale = new Vector3(1.8f, 1.3f, 1.5f);
                var roof = Part("roof", floatT, MeshKit.Cone(1.55f, 0.9f, 4), gold, new Vector3(0, 2.15f, 0), true).transform; roof.localRotation = Quaternion.Euler(0, 45, 0);
                Part("bird", floatT, MeshKit.Sphere(), gold, new Vector3(0, 3.2f, 0), true).transform.localScale = Vector3.one * 0.5f;
                foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                    Part("post", floatT, MeshKit.Cylinder(0.08f, 0.08f, 1.4f, 8), gold, new Vector3(sx * 0.95f, 0.8f, sz * 0.8f), true);
            }
            var lg = new GameObject("light"); lg.transform.SetParent(floatT, false); lg.transform.localPosition = new Vector3(0, 2.2f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = Conv.Hex("#ffd27a"); l.range = 12; l.intensity = 14f / Mathf.PI / 4f; l.shadows = LightShadows.None;
            // the float's box in its own space (the camera inside it hides its body: no black wall over the view)
            floatRends = floatT.GetComponentsInChildren<Renderer>();
            floatBox = new Bounds(new Vector3(0, 1.7f, 0), new Vector3(3.4f, 3.4f, 3.4f));
            if (floatRends.Length > 0)
            {
                var b = floatRends[0].bounds; foreach (var r in floatRends) b.Encapsulate(r.bounds);
                floatBox = new Bounds(floatT.InverseTransformPoint(b.center), b.size);
            }
            floatBox.Expand(0.7f);
        }

        // ------------------------------------------------------------------ every frame
        void LateUpdate()
        {
            var w = runner != null ? runner.World : null;
            float t = w != null ? (float)w.time : Time.time;
            string viewer = runner?.Player?.team ?? "zenith";
            if (w != null && ringMat != null)
            {
                var P = w.point;
                var col = P.contested ? CONTEST : P.owner == null ? Color.white : P.owner == viewer ? Z : UM;
                ringMat.SetColor("_BaseColor", A(col, 0.9f));
                beamMat.SetColor("_BaseColor", A(col, 0.12f));
                var dc = P.capTeam != null ? (P.capTeam == viewer ? Z : UM) : col;
                discMat.SetColor("_BaseColor", A(dc, 0.08f)); arcMat.SetColor("_BaseColor", A(dc, 0.8f)); rippleMat.SetColor("_BaseColor", A(dc, 0.25f));
                int pct = Mathf.Clamp(Mathf.RoundToInt((float)P.capture), 0, 100);
                if (pct != arcPct) { arcPct = pct; MeshKit.Annulus(6 * 0.86f, 6 * 0.96f, 64, pct * 3.6f, arcMesh); }
                // rings drift outward at 0.15 of the radius a second, four at a time
                for (int i = 0; i < ripples.Count; i++)
                {
                    float r = Mathf.Repeat(t * 0.15f + i * 0.25f, 1f) * 6f * 0.96f;
                    ripples[i].localScale = new Vector3(Mathf.Max(0.01f, r), 1, Mathf.Max(0.01f, r));
                }
            }
            if (w != null)
                for (int i = 0; i < packs.Count && i < w.packs.Count; i++)
                {
                    var p = packs[i]; double left = w.packs[i].readyAt - w.time, total = p.big ? 15 : 10;
                    bool ready = left <= 0;
                    p.cross.gameObject.SetActive(ready);
                    p.cross.localPosition = new Vector3(0, 0.95f + Mathf.Sin(t * 2.4f + i) * 0.08f, 0);
                    p.cross.localRotation = Quaternion.Euler(0, -t * 1.4f * Mathf.Rad2Deg, 0);
                    p.ringMat.SetColor("_BaseColor", A(Conv.Hex("#2fe3b0"), ready ? 0.8f : 0.35f));
                    float s = ready ? 1 : Mathf.Max(0.05f, (float)(1 - left / total));
                    p.ring.localScale = new Vector3(s, 1, s);
                }
            foreach (var (g, arrow) in pads)
            {
                arrow.localPosition = new Vector3(0, 1.1f + Mathf.Sin(t * 4) * 0.25f, 0);
                g.localRotation = Quaternion.Euler(0, -t * 0.5f * Mathf.Rad2Deg, 0);
            }
            if (floatT != null && w != null)
            {
                var pos = w.push.pos;
                floatT.localPosition = Conv.U(pos.x, pos.y + 0.35 + Mathf.Sin(t * 1.8f) * 0.08f, pos.z);
                if (hasPush)
                {
                    double dx = pos.x - lastPush.x, dz = pos.z - lastPush.z;
                    if (dx * dx + dz * dz > 1e-5) floatT.localRotation = Conv.Yaw(System.Math.Atan2(dx, dz));
                }
                lastPush = pos; hasPush = true;
                var cam = Camera.main;
                if (cam != null)
                {
                    bool inside = floatBox.Contains(floatT.InverseTransformPoint(cam.transform.position));
                    foreach (var r in floatRends) if (r != null) r.enabled = !inside;
                }
            }
        }
    }

    /// <summary>the few procedural shapes MapScene uses (three.js Torus / Ring / Cylinder / Cone / Tube geometries)</summary>
    public static class MeshKit
    {
        static Mesh box, sphere;
        public static Mesh Box() { if (box == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); box = White(Object.Instantiate(g.GetComponent<MeshFilter>().sharedMesh)); Object.Destroy(g); } return box; }
        public static Mesh Sphere() { if (sphere == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = White(Object.Instantiate(g.GetComponent<MeshFilter>().sharedMesh)); Object.Destroy(g); } return sphere; }

        /// <summary>white vertex colours: the ZU/Fx shaders multiply by them (a mesh without colours could read black)</summary>
        public static Mesh White(Mesh m)
        {
            var c = new Color32[m.vertexCount];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(255, 255, 255, 255);
            m.colors32 = c;
            return m;
        }

        /// <summary>a torus lying flat (in the XZ plane): radius R, tube r</summary>
        public static Mesh Torus(float R, float r, int segs, int sides)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2 / segs; var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2 / sides; var d = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c * R + d * r); n.Add(d);
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j, a1 = a0 + sides + 1;
                    tri.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 });
                }
            var m = new Mesh { name = "torus" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return White(m);
        }

        /// <summary>a flat ring facing up from r0 to r1, `arcDeg` of it (into `into` when given, rebuilt in place)</summary>
        public static Mesh Annulus(float r0, float r1, int segs, float arcDeg, Mesh into = null)
        {
            var m = into ?? new Mesh { name = "ring" };
            m.Clear();
            int n = Mathf.Max(1, Mathf.CeilToInt(segs * Mathf.Clamp(arcDeg, 0, 360) / 360f));
            if (arcDeg <= 0.01f) return m;
            var v = new List<Vector3>(); var tri = new List<int>(); var nr = new List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                // from 12 o'clock, clockwise as seen from above (the TS disc's arc reads the same way)
                float a = Mathf.PI / 2 - i * arcDeg * Mathf.Deg2Rad / n;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r0); v.Add(d * r1); nr.Add(Vector3.up); nr.Add(Vector3.up);
            }
            for (int i = 0; i < n; i++) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 }); }
            // both faces (it's seen from above, but a camera can dip under a raised point)
            int c = v.Count; for (int i = 0; i < c; i++) { v.Add(v[i]); nr.Add(Vector3.down); }
            int t = tri.Count; for (int i = 0; i < t; i += 3) tri.AddRange(new[] { tri[i] + c, tri[i + 2] + c, tri[i + 1] + c });
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return White(m);
        }

        /// <summary>a closed tapered cylinder standing on its base (bottom radius r0, top r1)</summary>
        public static Mesh Cylinder(float r0, float r1, float h, int sides)
        {
            var sm = new SurfaceMesh();
            sm.Cylinder(Vector3.zero, r0, r1, h, sides, top: true, bottom: true);
            return White(sm.ToMesh("cylinder"));
        }

        /// <summary>an open tube (the point's beam), centred on its height</summary>
        public static Mesh OpenCylinder(float r, float h, int sides)
        {
            var sm = new SurfaceMesh();
            sm.Cylinder(new Vector3(0, -h / 2 + h / 2, 0), r, r, h, sides, top: false, bottom: false);
            return White(sm.ToMesh("beam"));
        }

        /// <summary>a cone pointing up (+Y), `sides` faces, centred on its height (three's ConeGeometry)</summary>
        public static Mesh Cone(float r, float h, int sides)
        {
            var sm = new SurfaceMesh();
            sm.Cylinder(new Vector3(0, -h / 2, 0), r, 0.001f, h, sides, top: false, bottom: true);
            return White(sm.ToMesh("cone"));
        }

        /// <summary>uniform Catmull-Rom through the points (three's CatmullRomCurve3 'catmullrom', tension 0.5), n samples</summary>
        public static List<Vector3> CatmullRom(List<Vector3> p, int n)
        {
            var o = new List<Vector3>();
            int segs = p.Count - 1;
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n * segs; int k = Mathf.Min(segs - 1, Mathf.FloorToInt(u)); float s = u - k;
                Vector3 p0 = p[Mathf.Max(0, k - 1)], p1 = p[k], p2 = p[k + 1], p3 = p[Mathf.Min(p.Count - 1, k + 2)];
                o.Add(0.5f * (2 * p1 + (-p0 + p2) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * s * s + (-p0 + 3 * p1 - 3 * p2 + p3) * s * s * s));
            }
            return o;
        }

        /// <summary>a tube of radius r along the points</summary>
        public static Mesh Tube(List<Vector3> pts, float r, int sides)
        {
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                var fwd = (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                var side = Vector3.Cross(Vector3.up, fwd).normalized; if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                var up = Vector3.Cross(fwd, side);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides; var d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    v.Add(pts[i] + d * r); nr.Add(d);
                }
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j, a1 = a0 + sides + 1;
                    tri.AddRange(new[] { a0, a1, a0 + 1, a0 + 1, a1, a1 + 1 });
                }
            var m = new Mesh { name = "tube", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return White(m);
        }
    }
}
