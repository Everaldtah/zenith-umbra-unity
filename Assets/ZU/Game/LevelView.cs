// A map on screen, built from its data: the boxes the simulation collides with, ramps, floors and render-only decor
// merged into one mesh per surface (the map's CC0 PBR materials, world-metric UVs and the ZU/Surface bevel / grime
// data), the props, the objective, then the environment - HDRI sky, sun, fog, post (Env/EnvKit) - and the world past
// the walls (Env/OuterWorld). Colliders exist for the camera only; the simulation's level is the authority.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.Env;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game
{
    public class LevelView : MonoBehaviour
    {
        static readonly Dictionary<string, Color> MAT = new Dictionary<string, Color>
        {
            ["wall"] = new Color(0.78f, 0.74f, 0.68f), ["trim"] = new Color(0.36f, 0.30f, 0.27f), ["ground"] = new Color(0.47f, 0.47f, 0.45f),
            ["glass"] = new Color(0.55f, 0.75f, 0.85f, 0.6f), ["accent"] = new Color(0.75f, 0.22f, 0.20f), ["roof"] = new Color(0.30f, 0.33f, 0.40f),
            ["window"] = new Color(1f, 0.85f, 0.55f), ["wood"] = new Color(0.52f, 0.36f, 0.24f), ["rock"] = new Color(0.50f, 0.47f, 0.43f), ["paint"] = new Color(0.86f, 0.80f, 0.62f),
            ["pane"] = new Color(0.10f, 0.12f, 0.15f), ["water"] = new Color(0.12f, 0.27f, 0.42f),
            // the outer world's building materials (Resources/ZUEnv/common_<kind> when imported)
            ["stone"] = new Color(0.62f, 0.60f, 0.56f), ["plaster"] = new Color(0.74f, 0.70f, 0.64f), ["metal"] = new Color(0.55f, 0.56f, 0.58f),
            ["rust"] = new Color(0.55f, 0.36f, 0.24f), ["brick"] = new Color(0.60f, 0.34f, 0.26f), ["planks"] = new Color(0.62f, 0.50f, 0.38f),
            ["concrete"] = new Color(0.62f, 0.61f, 0.58f), ["tiles"] = new Color(0.22f, 0.23f, 0.27f),
            ["turf"] = new Color(0.46f, 0.58f, 0.34f), ["cliff"] = new Color(0.50f, 0.50f, 0.48f), ["sand"] = new Color(0.80f, 0.66f, 0.48f),
        };
        /// <summary>the blockout palette colour of a surface kind (also the tint of kit sets that have no painted albedo)</summary>
        public static Color Palette(string kind) => MAT.TryGetValue(kind ?? "wall", out var c) ? c : MAT["wall"];

        static Shader lit;
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        MapDef map;
        ILevel level;

        /// <summary>the material for a surface kind: the map's PBR set, else the common kit's, else a flat palette colour</summary>
        Material Mat(string kind)
        {
            kind ??= "wall";
            if (mats.TryGetValue(kind, out var m)) return m;
            m = EnvKit.Surface(map, kind);
            if (m == null) m = Resources.Load<Material>("ZUEnv/common_" + kind);
            if (m != null && kind == "window")
            {
                // lit windows: the shipped material (its emissive variant survives the build), brighter at dusk / night
                m = new Material(m) { name = "zu_window" };
                m.SetColor("_EmissionColor", new Color(1f, 0.58f, 0.26f) * (OuterWorld.For(map).lit ? 1.4f : 0.6f));
            }
            if (m == null && kind == "water") m = Resources.Load<Material>("ZUEnv/water");
            if (m == null)
            {
                lit ??= Shader.Find("Universal Render Pipeline/Lit");
                var c = Palette(kind);
                if (kind == "accent") c = Conv.Hex(map.tint, c);
                m = new Material(lit) { name = "zu_" + kind, enableInstancing = true };
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", kind == "glass" || kind == "pane" ? 0.9f : kind == "water" ? 0.95f : kind == "ground" ? 0.15f : 0.3f);
                if (kind == "window")
                {
                    // paper / glass lit from behind: a dark pane glowing warm amber (lantern light), brighter at dusk / night
                    bool night = OuterWorld.For(map).lit;
                    m.SetColor("_BaseColor", new Color(0.18f, 0.13f, 0.09f));
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, 0.58f, 0.26f) * (night ? 1.4f : 0.6f));
                }
            }
            mats[kind] = m;
            return m;
        }

        public static LevelView Build(MapDef map, Transform parent, ILevel level = null)
        {
            var go = new GameObject("Level " + map.id);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<LevelView>();
            v.map = map; v.level = level;
            v.Make();
            return v;
        }

        void Make()
        {
            var solid = new MeshBins(); var deco = new MeshBins();
            // floors: a slab with some depth (a quay over water, a rock skirt under a floating island)
            bool voidMap = map.floors != null && map.floors.Count > 1 && !map.water.HasValue;
            foreach (var f in map.floors ?? new List<Box>())
            {
                float thick = voidMap ? 0.01f : 0.5f, y1 = (float)(f.y ?? 0) + 0.01f, y0 = y1 - 0.01f - thick;
                var c = Conv.U(f.x, (y0 + y1) / 2, f.z);
                solid[f.mat ?? "ground"].Box(c, new Vector3((float)f.w / 2, (y1 - y0) / 2, (float)f.d / 2), bevel: false);
                if (voidMap && f.mat != "trim")
                {
                    float s = Mathf.Min((float)f.w, (float)f.d), l = Mathf.Max((float)f.w, (float)f.d);
                    deco["rock"].Cylinder(Conv.U(f.x, -(s * 0.5f + 3), f.z), l * 0.2f, l * 0.6f, s * 0.5f + 3, 9, top: false);
                }
            }
            foreach (var b in map.boxes ?? new List<Box>())
            {
                if (b.mat == null && IsBoundary(b)) continue;      // the invisible walls around the play space
                if (b.ramp != null) Ramp(solid[b.mat ?? "wall"], b);
                else Boxed(solid[b.mat ?? "wall"], b, grime: true);
            }
            foreach (var b in map.decor ?? new List<Box>())
            {
                if (b.ramp != null) Ramp(deco[b.mat ?? "trim"], b);
                else Boxed(deco[b.mat ?? "trim"], b, grime: false);
            }
            var solidRoot = new GameObject("Solid").transform; solidRoot.SetParent(transform, false);
            var decoRoot = new GameObject("Decor").transform; decoRoot.SetParent(transform, false);
            solid.Emit(solidRoot, Mat);
            deco.Emit(decoRoot, Mat);
            // the camera's collision (MatchCamera's sphere cast): the merged solid meshes
            foreach (var mf in solidRoot.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            var props = new GameObject("Props").transform; props.SetParent(transform, false);
            foreach (var p in map.props ?? new List<Prop>()) Prop(p, props);
            Point();
            Sun();
            OuterWorld.Build(map, transform, Mat);
            EnvKit.Apply(map, transform, OuterWorld.Extent);
        }

        /// <summary>the 4 unmaterialled walls that fence the play space in (they stand on its edge)</summary>
        bool IsBoundary(Box b)
        {
            double X = map.size[0], Z = map.size[1];
            return System.Math.Abs(System.Math.Abs(b.x) - X) < 2.5 && b.d > Z || System.Math.Abs(System.Math.Abs(b.z) - Z) < 2.5 && b.w > X;
        }

        static void Boxed(SurfaceMesh m, Box b, bool grime)
        {
            float y0 = (float)(b.y ?? 0);
            var c = Conv.U(b.x, y0 + b.h / 2, b.z);
            // grime at the foot of walls standing on the ground (TS: every box, the shader fades it over its first metre)
            m.Box(c, new Vector3((float)b.w / 2, (float)b.h / 2, (float)b.d / 2), bevel: true, grime: grime && y0 < 0.3f ? 1 : 0);
        }

        /// <summary>a wedge: the box's footprint, its top rising from y0 to y0 + h along the ramp direction (no bevel)</summary>
        static void Ramp(SurfaceMesh m, Box b)
        {
            double y0 = b.y ?? 0, hx = b.w / 2, hz = b.d / 2;
            V3[] c = { new V3(b.x - hx, y0, b.z - hz), new V3(b.x + hx, y0, b.z - hz), new V3(b.x + hx, y0, b.z + hz), new V3(b.x - hx, y0, b.z + hz) };
            double Top(V3 p) => System.Math.Max(y0 + 0.001, BoxLevel.Top(b, p.x, p.z) ?? y0 + b.h);
            var bottom = new Vector3[4]; var top = new Vector3[4];
            for (int i = 0; i < 4; i++) { bottom[i] = Conv.U(c[i]); top[i] = Conv.U(c[i].x, Top(c[i]), c[i].z); }
            var mid = Vector3.zero; for (int i = 0; i < 4; i++) mid += (bottom[i] + top[i]) / 8;
            void Face(params Vector3[] q)
            {
                // drop the zero-height corner of a ramp's side (it's a triangle)
                var pts = new List<Vector3>();
                foreach (var p in q) if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 1e-8f) pts.Add(p);
                if (pts.Count > 3 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-8f) pts.RemoveAt(pts.Count - 1);
                if (pts.Count < 3) return;
                var centre = Vector3.zero; foreach (var p in pts) centre += p; centre /= pts.Count;
                m.Poly(pts, centre - mid);
            }
            Face(top[0], top[1], top[2], top[3]);
            Face(bottom[0], bottom[3], bottom[2], bottom[1]);
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Face(bottom[i], bottom[j], top[j], top[i]); }
        }

        /// <summary>a prop as the TS MapScene places it: on the ground under it, turned by `rot`, `s` metres tall - the imported
        /// model (Resources/ZUProps, normalised to 1 m) when there is one, else a stand-in post</summary>
        void Prop(Prop p, Transform parent)
        {
            double s = p.s ?? 2, r = p.solid ?? 0.5;
            double ground = level != null ? level.GroundAt(p.x, p.z, 60) : 0;
            double y = p.y ?? System.Math.Max(0, double.IsFinite(ground) ? ground : 0);
            var prefab = Resources.Load<GameObject>("ZUProps/" + p.id);
            if (prefab != null)
            {
                var m = Instantiate(prefab, parent, false);
                m.name = p.id;
                m.transform.localPosition = Conv.U(p.x, y, p.z);
                m.transform.localRotation = Conv.Yaw(p.rot ?? 0);
                m.transform.localScale = Vector3.one * (float)s;
                return;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = p.id + " (stand-in)"; go.transform.SetParent(parent, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = Conv.U(p.x, y + s * 0.45, p.z);
            go.transform.localRotation = Conv.Yaw(p.rot ?? 0);
            go.transform.localScale = new Vector3((float)(r * 2), (float)(s * 0.45), (float)(r * 2));
            go.GetComponent<Renderer>().sharedMaterial = Mat("wood");
        }

        /// <summary>the objective: the capture point ring (Control) - the payload route is drawn by the payload view</summary>
        void Point()
        {
            if (map.objective == "push" || map.point == null || map.point.Length != 3) return;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(ring.GetComponent<Collider>());
            ring.name = "Point"; ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Conv.U(map.point[0], map.point[1] + 0.02, map.point[2]);
            ring.transform.localScale = new Vector3(12, 0.02f, 12);
            var m = new Material(Mat("accent")); m.SetColor("_BaseColor", Conv.Hex(map.tint, Color.cyan) * 0.8f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Conv.Hex(map.tint, Color.cyan) * 0.6f);
            ring.GetComponent<Renderer>().sharedMaterial = m;
        }

        void Sun()
        {
            // (no `??` on Unity objects: a missing component is a "fake null" that ?? treats as present)
            var sunGo = GameObject.Find("ZU Sun"); if (sunGo == null) sunGo = new GameObject("ZU Sun");
            var sun = sunGo.GetComponent<Light>(); if (sun == null) sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.92f;
            if (map.sun != null)
            {
                sun.color = Conv.Hex(map.sun.color, Color.white);
                // three.js (physically-correct lights) shades diffuse as albedo * I * cos / pi; URP has no 1/pi, so the same
                // sun is pi times brighter here unless its intensity is divided by pi
                sun.intensity = (float)map.sun.intensity / Mathf.PI;
                if (map.sun.dir != null && map.sun.dir.Length == 3)
                {
                    // the sim gives the direction toward the sun: light travels the other way
                    var d = Conv.U(map.sun.dir[0], map.sun.dir[1], map.sun.dir[2]).normalized;
                    sunGo.transform.rotation = Quaternion.LookRotation(-d);
                }
            }
            RenderSettings.sun = sun;
            // the fallback ambient (EnvKit replaces it with the HDRI sky's when the map has one)
            if (map.ambient != null && map.ambient.Length == 3)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                float k = System.Convert.ToSingle(map.ambient[2]);
                RenderSettings.ambientSkyColor = Conv.Hex(map.ambient[0] as string) * k;
                RenderSettings.ambientEquatorColor = Color.Lerp(Conv.Hex(map.ambient[0] as string), Conv.Hex(map.ambient[1] as string), 0.5f) * k;
                RenderSettings.ambientGroundColor = Conv.Hex(map.ambient[1] as string) * k;
            }
        }
    }
}
