// A map on screen, built from its data: the boxes the simulation collides with, ramps, floors and render-only decor
// merged into one mesh per surface (the map's CC0 PBR materials, world-metric UVs and the ZU/Surface bevel / grime
// data), the props, the objective and pickups (Env/MapObjects), ambient particles, then the environment - HDRI sky,
// sun, fog, post (Env/EnvKit) - and, when enabled, the world past the walls (Env/OuterWorld, a Unity extra). The PC
// game's MapScene.ts is the reference (docs/map-parity.md). Colliders exist for the camera only; the simulation's level
// is the authority.
using System.Collections.Generic;
using System.Linq;
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
            m = TsMaterial(kind);
            if (m == null) m = EnvKit.Surface(map, kind);
            if (m == null) m = Resources.Load<Material>("ZUEnv/common_" + kind);
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

        /// <summary>MapScene.ts's own materials for the kinds the PC game doesn't take from the surface sets: windows (lit
        /// paper at dusk and night - sun under 2 - else day glass with a faint glow behind), road paint, the tint-coloured
        /// accent boxes and the tinted glass. Built on shipped materials so their emissive / transparent variants survive a
        /// build (common_window: URP Lit + _EMISSION; common_glass_clear: transparent + _EMISSION).</summary>
        Material TsMaterial(string kind)
        {
            var tint = Conv.Hex(map.tint, Color.white);
            bool dusk = map.sun == null || map.sun.intensity < 2;
            Material Emissive(string name, Color baseC, Color emit, float smooth, float metal)
            {
                var src = Resources.Load<Material>("ZUEnv/common_window");
                if (src == null) return null;
                var m = new Material(src) { name = name };
                m.SetColor("_BaseColor", baseC); m.SetColor("_EmissionColor", emit);
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                return m;
            }
            switch (kind)
            {
                case "window":
                    return dusk ? Emissive("zu_window", Conv.Hex("#ffe2a8"), Conv.Hex("#ffc46b") * 1.1f, 0.7f, 0)
                                : Emissive("zu_window", Conv.Hex("#7d97ad"), Conv.Hex("#ffd49a") * 0.12f, 0.85f, 0.35f);
                case "paint": return Emissive("zu_paint", Conv.Hex("#f2cf5b"), Conv.Hex("#f2cf5b") * 0.08f, 0.3f, 0);
                case "accent":
                {
                    // the wall texture tinted toward the map's colour, glowing a little in it
                    var m = Emissive("zu_accent", Color.Lerp(tint, Color.white, 0.4f), tint * 0.25f, 0.6f, 0);
                    var wall = EnvKit.Surface(map, "wall");
                    if (m != null && wall != null && wall.HasProperty("_BaseMap"))
                    {
                        m.SetTexture("_BaseMap", wall.GetTexture("_BaseMap"));
                        m.SetTextureScale("_BaseMap", wall.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", wall.GetTextureOffset("_BaseMap"));
                    }
                    return m;
                }
                case "glass":
                {
                    var src = Resources.Load<Material>("ZUEnv/common_glass_clear");
                    if (src == null) return null;
                    var m = new Material(src) { name = "zu_glass" };
                    m.SetColor("_BaseColor", U(tint, 0.35f)); m.SetColor("_EmissionColor", tint * 0.3f);
                    return m;
                }
            }
            return null;
        }
        static Color U(Color c, float a) { c.a = a; return c; }

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
                    // MapScene: a 7-sided cylinder tapering 1 -> 0.35, scaled (max 0.55, min 0.5 + 3, min 0.55), its centre
                    // at -(min 0.25 + 1.5), in the wall texture - the island's underside sinking into the cloud sea
                    float s = Mathf.Min((float)f.w, (float)f.d), l = Mathf.Max((float)f.w, (float)f.d);
                    float h = s * 0.5f + 3, cy = -(s * 0.25f + 1.5f);
                    // (an elliptical footprint: x by l * 0.55, z by s * 0.55)
                    var bins = deco["wall"];
                    var keep = bins.xf;
                    bins.xf = keep * Matrix4x4.TRS(Conv.U(f.x, cy - h / 2, f.z), Quaternion.identity, new Vector3(l * 0.55f, 1, s * 0.55f));
                    bins.Cylinder(Vector3.zero, 0.35f, 1, h, 7, top: false);
                    bins.xf = keep;
                }
            }
            // the rock boxes of Sunset Mile / Iron Gulch wear Tripo rock formations when the env set has them (the box stays the
            // simulation's collider and the camera's; the formation is a touch larger so its edges hide the box's)
            var rocks = TripoEnv.Rocks; int ri = 0;
            Transform rockRoot = null;
            // box-built buildings wearing Tripo shells: their boxes and decor stop being drawn (each box keeps a collider for
            // the camera; the simulation never sees any of this)
            var (shellBoxes, shellDecor) = Shells();
            var boxes = map.boxes ?? new List<Box>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (shellBoxes.Contains(i)) { Collider(shellBoxes.Root, b); continue; }
                if (b.mat == "rock" && b.ramp == null && rocks.Count > 0)
                {
                    if (rockRoot == null) { rockRoot = new GameObject("Rocks").transform; rockRoot.SetParent(transform, false); }
                    DressRock(rockRoot, b, rocks[ri % rocks.Count], ri); ri++;
                    continue;
                }
                // (the perimeter walls of Kagura, Lantern, Starfall, Foundry and the Training Grounds are drawn, as in MapScene)
                if (b.ramp != null) Ramp(solid[b.mat ?? "wall"], b);
                else Boxed(solid[b.mat ?? "wall"], b, grime: true);
            }
            var decor = map.decor ?? new List<Box>();
            for (int i = 0; i < decor.Count; i++)
            {
                var b = decor[i];
                if (shellDecor.Contains(i)) continue;
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
            GroundDressing.Build(map, transform);
            AmbienceSpots.Build(map, transform);
            TintLight();
            MapObjects.Build(map, transform, level, Mat);
            AmbientParticles.Build(map, transform);
            Sun();
            // the world past the walls is a Unity extra the PC game doesn't have (it shows the painted sky above its walls):
            // off, only the PC game's own harbour water and cloud sea are built
            OuterWorld.Build(map, transform, Mat, full: OuterWorld.Enabled);
            EnvKit.Apply(map, transform, OuterWorld.Enabled ? OuterWorld.Extent : 600f);
        }

        /// <summary>the box / decor indices hidden under shells, and the "Buildings" root the shells and their colliders sit in</summary>
        sealed class Hidden : HashSet<int> { public Transform Root; }

        /// <summary>the map's Tripo building shells (TripoEnv.Shells): each fitted to its cluster's bounds; returns what they
        /// cover. Nothing when the map has none (the boxes draw as before).</summary>
        (Hidden boxes, Hidden decor) Shells()
        {
            var hb = new Hidden(); var hd = new Hidden();
            var list = TripoEnv.Shells(map.id);
            if (list.Count == 0) return (hb, hd);
            var root = new GameObject("Buildings").transform; root.SetParent(transform, false);
            hb.Root = hd.Root = root;
            int nb = map.boxes?.Count ?? 0, nd = map.decor?.Count ?? 0;
            foreach (var s in list)
            {
                // a shell whose indices don't fit this map's data (stale manifest) is skipped whole: half a building is worse
                if ((s.boxes != null && s.boxes.Any(i => i < 0 || i >= nb)) || (s.decor != null && s.decor.Any(i => i < 0 || i >= nd))) continue;
                if (s.pos?.Length == 3 && s.s > 0)
                {
                    var go = Instantiate(s.prefab, root, false); go.name = s.id;
                    go.transform.localPosition = new Vector3((float)s.pos[0], (float)s.pos[1], (float)s.pos[2]);
                    go.transform.localScale = Vector3.one * (float)s.s;
                }
                else
                {
                    var size = new Vector3((float)s.size[0], (float)s.size[1], (float)s.size[2]);
                    var center = Conv.U(s.c[0], s.c[1] + s.size[1] / 2, s.c[2]);
                    TripoEnv.Fit(s.prefab, TripoEnv.Unit(s), s.id, root, center, size, s.turns, (float)s.grow);
                }
                if (s.boxes != null) foreach (var i in s.boxes) hb.Add(i);
                if (s.decor != null) foreach (var i in s.decor) hd.Add(i);
            }
            return (hb, hd);
        }

        /// <summary>a hidden box's stand-in for the camera's collision (MatchCamera sphere-casts the level's colliders)</summary>
        static void Collider(Transform parent, Box b)
        {
            float y0 = (float)(b.y ?? 0);
            var col = new GameObject("box collider"); col.transform.SetParent(parent, false);
            col.transform.localPosition = Conv.U(b.x, y0 + b.h / 2, b.z);
            col.AddComponent<BoxCollider>().size = new Vector3((float)b.w, (float)b.h, (float)b.d);
        }

        /// <summary>a Tripo rock fitted round a rock box (seeded turn), with a box collider for the camera's collision</summary>
        static void DressRock(Transform parent, Box b, TripoEnv.Piece piece, int i)
        {
            float y0 = (float)(b.y ?? 0);
            var center = Conv.U(b.x, y0 + b.h / 2, b.z);
            var size = new Vector3((float)b.w, (float)b.h, (float)b.d);
            TripoEnv.FitRock(piece, parent, center, size, (i * 7 + 3) % 4);
            var col = new GameObject("rock collider"); col.transform.SetParent(parent, false); col.transform.localPosition = center;
            col.AddComponent<BoxCollider>().size = size;
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
                // image-to-3D keeps a cherry tree's trunk but loses its thin foliage: MapScene adds a blossom canopy
                if (p.id.Contains("sakura")) Blossoms(m.transform, (float)s);
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

        /// <summary>MapScene's PointLight(tint, 30, 40, 1.6) five metres over the objective (three.js intensity is candela-ish
        /// with physical lights; URP's point light carries the 1/pi the sun does)</summary>
        void TintLight()
        {
            if (map.point == null || map.point.Length != 3) return;
            var go = new GameObject("Tint Light"); go.transform.SetParent(transform, false);
            go.transform.localPosition = Conv.U(map.point[0], map.point[1] + 5, map.point[2]);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = Conv.Hex(map.tint, Color.white); l.range = 40; l.intensity = 30f / Mathf.PI / 4f;
            l.shadows = LightShadows.None;
        }

        /// <summary>MapScene.blossoms: a cherry canopy of 70 faceted puffs (seeded, so every match grows the same tree)</summary>
        static Mesh blossomMesh; static Material blossomMat;
        static void Blossoms(Transform tree, float s)
        {
            if (blossomMesh == null)
            {
                // an icosphere-ish puff: a low-poly sphere is close enough at this size
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                blossomMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
                var src = Resources.Load<Material>("ZUEnv/common_window");
                blossomMat = src != null ? new Material(src) { name = "zu_blossom", enableInstancing = true } : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                blossomMat.SetColor("_BaseColor", Conv.Hex("#ffb3cf")); blossomMat.SetColor("_EmissionColor", Conv.Hex("#ff8fb8") * 0.18f);
                blossomMat.SetFloat("_Smoothness", 0.2f); blossomMat.SetFloat("_Metallic", 0);
            }
            var root = new GameObject("blossoms").transform; root.SetParent(tree.parent, false);
            root.localPosition = tree.localPosition; root.localRotation = tree.localRotation;
            int seed = 7;
            float Rnd() { seed = (int)((long)seed * 16807 % 2147483647); return seed / 2147483647f; }
            for (int i = 0; i < 70; i++)
            {
                float a = Rnd() * Mathf.PI * 2, r = Mathf.Sqrt(Rnd()) * s * 0.42f;
                var pos = new Vector3(Mathf.Cos(a) * r, s * (0.62f + Rnd() * 0.3f) - r * 0.25f, Mathf.Sin(a) * r);
                float k = s * (0.07f + Rnd() * 0.07f) * 2;            // (three's unit icosahedron has radius 1; Unity's sphere diameter 1)
                var rot = Quaternion.Euler(Rnd() * Mathf.Rad2Deg, Rnd() * 6 * Mathf.Rad2Deg, Rnd() * Mathf.Rad2Deg);
                var go = new GameObject("puff"); go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(-pos.x, pos.y, pos.z); go.transform.localRotation = rot; go.transform.localScale = new Vector3(k * 1.3f, k, k * 1.3f);
                go.AddComponent<MeshFilter>().sharedMesh = blossomMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = blossomMat;
            }
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
