// The world beyond a map's walls - what makes an arena read as a place (Overwatch's maps sit in towns, harbours and
// mountains that run to the horizon): terrain that is flat around the play space and rises into hills and mountains,
// rings of buildings in the map's style that grow taller with distance (a layered skyline), a landmark or two, trees,
// and water or a cloud sea where the map has them. Seeded by the map id, so every match builds the same world.
// Render-only: the simulation's level is the play space; nothing out here collides.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public static class OuterWorld
    {
        public enum Style { Japan, West, Industry, Observatory, Sky, Academy }

        public sealed class Theme
        {
            public Style style;
            public float hill = 30, mountain = 160;       // terrain amplitudes (m)
            public float flat = 40;                       // flat apron around the play space (m)
            public bool sea, clouds, mesas;
            public bool space;                            // the Starfall campaign: platforms in orbit, nothing below
            public float shore = 150;                     // sea maps: distance from the centre to the far shore
            public float density = 1;                     // buildings per ring
            public float minH = 1, maxH = 4;              // storeys near -> far
            public bool lit;                              // lit windows (dusk / night)
            public string[] trees = new string[0];
            public int treeCount = 220;
        }

        public static Theme For(MapDef map)
        {
            // the campaign levels (c1_shipyard .. c5_citadel): floating platforms in space
            if (map.id.Length > 2 && map.id[0] == 'c' && char.IsDigit(map.id[1]) && map.id[2] == '_')
                return new Theme { style = Style.Sky, space = true, hill = 0, mountain = 0, trees = new string[0], treeCount = 0, lit = true };
            switch (map.id)
            {
                case "hanabi": return new Theme { style = Style.Japan, sea = true, shore = 175, hill = 45, mountain = 190, lit = true, trees = new[] { "prop_amatsu_sakura", "prop_cloud_teatree" }, treeCount = 520, minH = 2, maxH = 4 };
                case "kagura": return new Theme { style = Style.Japan, hill = 35, mountain = 220, flat = 34, density = 1.3f, trees = new[] { "prop_amatsu_sakura", "prop_cloud_teatree" }, minH = 2, maxH = 5 };
                case "lantern": return new Theme { style = Style.Japan, hill = 40, mountain = 200, lit = true, density = 1.2f, trees = new[] { "prop_amatsu_sakura" }, minH = 2, maxH = 4 };
                case "cloudstep": return new Theme { style = Style.Sky, clouds = true, hill = 0, mountain = 330, trees = new[] { "prop_cloud_teatree" }, treeCount = 0 };
                case "starfall": return new Theme { style = Style.Observatory, hill = 60, mountain = 300, flat = 30, trees = new[] { "prop_star_pine" }, treeCount = 420, density = 0.5f };
                case "foundry": return new Theme { style = Style.Industry, hill = 40, mountain = 180, lit = true, density = 1.1f, trees = new string[0], treeCount = 0 };
                case "mile": return new Theme { style = Style.West, mesas = true, hill = 18, mountain = 120, flat = 120, density = 0.55f, trees = new string[0], treeCount = 0 };   // open desert, mesas on the horizon
                case "gulch": return new Theme { style = Style.West, mesas = true, hill = 55, mountain = 150, flat = 40, density = 0.6f, trees = new string[0], treeCount = 0 };   // a canyon
                default: return new Theme { style = Style.Academy, hill = 25, mountain = 140, trees = new[] { "prop_cloud_teatree" }, density = 0.6f };
            }
        }

        public const float Extent = 900f;       // how far the world reaches from the centre (m)
        /// <summary>the outer world is a Unity extra (the PC game draws only its painted sky past the walls): Options decides</summary>
        public static bool Enabled => UI.Toolkit.ZuSettings.Current.video.outerWorld;

        /// <summary>build it under `parent`; `mat(kind)` resolves surface kinds to the map's materials</summary>
        public static void Build(MapDef map, Transform parent, System.Func<string, Material> mat, bool full = true)
        {
            var th = For(map);
            var root = new GameObject("Outer World").transform; root.SetParent(parent, false);
            var rng = new System.Random(map.id.GetHashCode() ^ 0x5eed);
            float X = (float)map.size[0], Z = (float)map.size[1];
            float water = map.water.HasValue ? (float)map.water.Value : float.NaN;
            float HeightAt(float x, float z) => Height(th, X, Z, x, z);

            // (the PC game's own harbour water and cloud sea, MapScene.ts, stay when the rest of the outer world is off)
            if (th.sea) Water(map, root, water, mat);
            if (th.clouds) Clouds(map, root);
            if (!full) return;
            if (!th.clouds && !th.space) Terrain(map, th, root, mat, HeightAt, X, Z);

            var bins = new MeshBins();
            Skyline(th, rng, bins, X, Z, HeightAt, water);
            // the Tripo landscape set pieces for the map's style stand in for the procedural landmarks when there are any
            if (!Vistas(th, rng, root, X, Z, HeightAt, water)) Landmarks(map, th, rng, bins, X, Z, HeightAt);
            bins.Emit(root, mat, prefix: "outer ");
            Trees(th, rng, root, X, Z, HeightAt, water);
            Showpieces(map, th, rng, root, X, Z, HeightAt);
        }

        // ------------------------------------------------------------------------------------------------ Tripo vistas
        /// <summary>the style's Tripo set pieces (TripoEnv, kind vista) on the ring 120 - 600 m out: seeded angles spread round
        /// the arena, each on the terrain at its foot (sunk a little), turned to face the arena, sized from its height_m
        /// (a little larger the further out); never in the sea. False when the style has none (the landmarks stay).</summary>
        static bool Vistas(Theme th, System.Random rng, Transform root, float X, float Z, System.Func<float, float, float> heightAt, float water)
        {
            var list = TripoEnv.Vistas(th.style.ToString());
            if (list.Count == 0) return false;
            var parent = new GameObject("Vistas").transform; parent.SetParent(root, false);
            int n = Mathf.Clamp(list.Count * 2, 4, 10);
            float inner = Mathf.Max(X, Z) + 120, outer = 600;
            float phase = (float)(rng.NextDouble() * Mathf.PI * 2);
            for (int i = 0; i < n; i++)
            {
                var piece = list[i % list.Count];
                // spread round the ring with a seeded wobble; try a few spots to stay out of the sea
                for (int tries = 0; tries < 6; tries++)
                {
                    float a = phase + (i + (float)rng.NextDouble() * 0.6f - 0.3f) * Mathf.PI * 2 / n;
                    float r = Mathf.Lerp(inner, outer, (float)rng.NextDouble());
                    float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    float y = heightAt(x, z);
                    if (!float.IsNaN(water) && y < water + 2) continue;
                    float h = piece.height_m * (0.85f + (float)rng.NextDouble() * 0.3f) * (1 + (r - inner) / Mathf.Max(1, outer - inner) * 0.5f);
                    float yaw = Mathf.Atan2(-x, -z) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 50;
                    TripoEnv.Place(piece, parent, new Vector3(x, y - h * 0.05f, z), yaw, h);
                    break;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------------ terrain
        /// <summary>distance (m) from the play rectangle's edge, 0 inside</summary>
        static float Outside(float X, float Z, float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x) - X), dz = Mathf.Max(0, Mathf.Abs(z) - Z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Fbm(float x, float z, int oct = 5)
        {
            float v = 0, a = 0.5f, f = 1;
            for (int i = 0; i < oct; i++) { v += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f - 17.3f * i); f *= 2.03f; a *= 0.5f; }
            return v;   // ~0..1, mean ~0.47
        }
        static float Ridged(float x, float z)
        {
            float v = 0, a = 0.55f, f = 1;
            for (int i = 0; i < 5; i++) { float n = 1 - Mathf.Abs(Mathf.PerlinNoise(x * f + 11.1f * i, z * f + 5.3f * i) * 2 - 1); v += a * n * n; f *= 2.1f; a *= 0.5f; }
            return v;
        }

        /// <summary>the ground height of the outer world at (x, z), Unity world space</summary>
        public static float Height(Theme th, float X, float Z, float x, float z)
        {
            float d = Outside(X, Z, x, z);
            if (th.clouds)
                return -260 + th.mountain * 1.2f * Mathf.SmoothStep(0, 1, (Mathf.Sqrt(x * x + z * z) - 330) / 300) * Ridged(x / 260, z / 260);
            if (d <= 0) return -0.06f;
            float h;
            if (th.sea)
            {
                float r = Mathf.Sqrt(x * x + z * z), shore = th.shore + (Fbm(x / 120, z / 120, 3) - 0.5f) * 70;
                float land = Mathf.SmoothStep(0, 1, (r - shore) / 40);
                h = Mathf.Lerp(-9f, 1.5f, land) + land * th.hill * Mathf.SmoothStep(0, 1, (r - shore - 30) / 160) * (0.4f + Fbm(x / 160, z / 160));
            }
            else
            {
                float apron = Mathf.SmoothStep(0, 1, (d - th.flat) / 140);
                h = th.hill * apron * (0.35f + Fbm(x / 170, z / 170));
                if (th.mesas)
                {
                    // terraced mesas / canyon walls: quantised noise with steep risers
                    float m = Fbm(x / 210, z / 210, 4) * Mathf.SmoothStep(0, 1, (d - th.flat * 0.8f) / 110);
                    float steps = m * 6, k = Mathf.Floor(steps), f = steps - k;
                    h = Mathf.Max(h * 0.4f, (k + Mathf.SmoothStep(0.72f, 1, f)) * 13f - 8f);
                }
            }
            float rr = Mathf.Sqrt(x * x + z * z);
            // mountains: broad massifs (fbm) carrying ridges, not a field of spikes
            float massif = Fbm(x / 520 + 3.1f, z / 520 - 1.7f, 4);
            h += th.mountain * Mathf.SmoothStep(0, 1, (rr - 280) / 420) * (0.25f + 0.75f * Mathf.SmoothStep(0.3f, 0.7f, massif)) * (0.45f + 0.55f * Ridged(x / 420, z / 420));
            return h;
        }

        static void Terrain(MapDef map, Theme th, Transform root, System.Func<string, Material> mat, System.Func<float, float, float> heightAt, float X, float Z)
        {
            int res = 513;
            float size = Extent * 2;
            var heights = new float[res, res];
            float lo = float.MaxValue, hi = float.MinValue;
            var raw = new float[res, res];
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float x = -Extent + i * size / (res - 1), z = -Extent + j * size / (res - 1);
                    float h = heightAt(x, z);
                    raw[j, i] = h; lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            hi = Mathf.Max(hi, lo + 1);
            for (int j = 0; j < res; j++) for (int i = 0; i < res; i++) heights[j, i] = (raw[j, i] - lo) / (hi - lo);
            var td = new TerrainData { heightmapResolution = res };
            td.size = new Vector3(size, hi - lo, size);
            td.SetHeights(0, 0, heights);

            // layers: the map's ground (around the play space), turf / scrub / sand on the hills, cliff rock on the steeps
            bool west = th.style == Style.West, dry = west || th.style == Style.Industry;
            var ground = Layer(mat("ground"), 6, Color.white);
            var rock = Layer(west ? mat("rock") : mat("cliff"), 14, west ? new Color(0.9f, 0.8f, 0.72f) : Color.white);
            Color turf = west ? Color.white : th.style == Style.Industry ? new Color(0.62f, 0.56f, 0.46f) : new Color(0.46f, 0.58f, 0.34f);
            var hill = Layer(west ? mat("sand") : mat("turf"), 9, turf, grassAlbedo: !dry);
            td.terrainLayers = new[] { ground, hill, rock };
            int ar = 512; td.alphamapResolution = ar;
            var alpha = new float[ar, ar, 3];
            for (int j = 0; j < ar; j++)
                for (int i = 0; i < ar; i++)
                {
                    float nx = (float)i / (ar - 1), nz = (float)j / (ar - 1);
                    float x = -Extent + nx * size, z = -Extent + nz * size;
                    float steep = td.GetSteepness(nx, nz);
                    float d = Outside(X, Z, x, z);
                    float hgt = td.GetInterpolatedHeight(nx, nz) + lo;
                    float r = Mathf.Clamp01((steep - 28) / 14);
                    float g = Mathf.Clamp01(1 - (d - 10) / 50) * (1 - r);
                    if (th.sea) g = Mathf.Max(g, Mathf.Clamp01(1 - (hgt - 0.5f) / 3f) * (1 - r));   // sand / quay stone at the waterline
                    float hl = Mathf.Max(0, 1 - r - g);
                    alpha[j, i, 0] = g; alpha[j, i, 1] = hl; alpha[j, i, 2] = r;
                }
            td.SetAlphamaps(0, 0, alpha);
            var go = UnityEngine.Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain"; go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(-Extent, lo, -Extent);
            var t = go.GetComponent<UnityEngine.Terrain>();
            var tm = Resources.Load<Material>("ZUEnv/terrain");
            if (tm != null) t.materialTemplate = tm;
            t.heightmapPixelError = 3; t.basemapDistance = 500; t.shadowCastingMode = ShadowCastingMode.On;
            t.drawInstanced = true;
            var col = go.GetComponent<TerrainCollider>(); if (col != null) Object.Destroy(col);   // the camera collides with the level, not the hills
        }

        static TerrainLayer Layer(Material m, float tile, Color tint, bool grassAlbedo = false)
        {
            var l = new TerrainLayer { tileSize = new Vector2(tile, tile) };
            if (m != null)
            {
                l.diffuseTexture = grassAlbedo ? GrassTexture() : m.GetTexture("_BaseMap") as Texture2D;
                l.normalMapTexture = m.GetTexture("_BumpMap") as Texture2D;
                l.maskMapTexture = m.GetTexture("_MetallicGlossMap") as Texture2D;
                l.normalScale = 1;
            }
            if (l.diffuseTexture == null) l.diffuseTexture = Texture2D.whiteTexture;
            var c = m != null && !grassAlbedo ? m.GetColor("_BaseColor") * tint : tint;
            l.diffuseRemapMax = new Vector4(c.r, c.g, c.b, 1);
            l.diffuseRemapMin = Vector4.zero;
            l.maskMapRemapMin = Vector4.zero; l.maskMapRemapMax = Vector4.one;
            l.smoothness = 0.1f; l.metallic = 0;
            return l;
        }

        static Texture2D grass;
        /// <summary>a 512 px tileable turf albedo (no grass set in the CC0 kit): layered value noise, tinted by the layer</summary>
        static Texture2D GrassTexture()
        {
            if (grass != null) return grass;
            int n = 512; grass = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "zu_turf", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float u = (float)i / n, v = (float)j / n, s = 0, a = 0.5f; int f = 4;
                    for (int o = 0; o < 5; o++) { s += a * TilePerlin(u, v, f, o); a *= 0.5f; f *= 2; }
                    float b = 0.72f + (s - 0.5f) * 0.7f;
                    float y = Mathf.Clamp01(b + (TilePerlin(u, v, 64, 9) - 0.5f) * 0.25f);
                    px[j * n + i] = new Color(y * 0.95f, y, y * 0.85f, 1);
                }
            grass.SetPixels32(px); grass.Apply(true);
            return grass;
        }
        /// <summary>tileable Perlin: blend the four wrapped samples</summary>
        static float TilePerlin(float u, float v, int freq, int seed)
        {
            float x = u * freq, y = v * freq, o = seed * 17.17f;
            float a = Mathf.PerlinNoise(x + o, y + o), b = Mathf.PerlinNoise(x - freq + o, y + o), c = Mathf.PerlinNoise(x + o, y - freq + o), d = Mathf.PerlinNoise(x - freq + o, y - freq + o);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        // ------------------------------------------------------------------------------------------------ water / clouds
        static void Water(MapDef map, Transform root, float level, System.Func<string, Material> mat)
        {
            if (float.IsNaN(level)) level = -0.7f;
            // the TS harbour palette (deep #1d3f73, shallow #3f86b8), pulled toward the map's fog so the sea sits in its light
            var fog = map.fog != null && map.fog.Length == 3 ? Conv.Hex(map.fog[0] as string, Color.gray) : Color.gray;
            var wm = new Material(mat("water")) { name = "zu_sea" };
            if (wm.HasProperty("_DeepColor")) wm.SetColor("_DeepColor", Color.Lerp(Conv.Hex("#1d3f73"), fog, 0.35f));
            if (wm.HasProperty("_ShallowColor")) wm.SetColor("_ShallowColor", Color.Lerp(Conv.Hex("#3f86b8"), fog, 0.25f));
            if (!wm.HasProperty("_DeepColor")) wm.SetColor("_BaseColor", Color.Lerp(Conv.Hex("#1d3f73"), fog, 0.35f));
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Sea"; go.transform.SetParent(root, false);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = new Vector3(0, level, 0);
            go.transform.localScale = new Vector3(Extent * 0.2f, 1, Extent * 0.2f);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = wm; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Clouds(MapDef map, Transform root)
        {
            var fog = map.fog != null && map.fog.Length == 3 ? Conv.Hex(map.fog[0] as string, Color.white) : Color.white;
            var tex = CloudTexture();
            float[] ys = { -22, -34, -52 }; float[] alpha = { 0.92f, 0.75f, 0.6f };
            for (int i = 0; i < ys.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
                go.name = "Cloud sea " + i; go.transform.SetParent(root, false);
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.localPosition = new Vector3(0, ys[i], 0);
                go.transform.localScale = new Vector3(Extent * 0.2f, 1, Extent * 0.2f);
                var m = Unlit(tex, Color.Lerp(Color.white, fog, 0.35f + i * 0.2f), alpha[i]);
                m.SetTextureScale("_BaseMap", new Vector2(6 + i * 2, 6 + i * 2));
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
                go.AddComponent<Scroll>().speed = new Vector2(0.004f + i * 0.002f, 0.0025f - i * 0.001f);
            }
        }

        static Material Unlit(Texture tex, Color c, float a)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "zu_clouds" };
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, a));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static Texture2D clouds;
        static Texture2D CloudTexture()
        {
            if (clouds != null) return clouds;
            int n = 512; clouds = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "zu_clouds", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float u = (float)i / n, v = (float)j / n, s = 0, a = 0.5f; int f = 3;
                    for (int o = 0; o < 6; o++) { s += a * TilePerlin(u, v, f, o + 3); a *= 0.5f; f *= 2; }
                    float cov = Mathf.SmoothStep(0.38f, 0.72f, s);
                    float shade = Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0.45f, 0.85f, s));
                    px[j * n + i] = new Color(shade, shade, shade * 1.02f, cov);
                }
            clouds.SetPixels32(px); clouds.Apply(true);
            return clouds;
        }

        sealed class Scroll : MonoBehaviour
        {
            public Vector2 speed; Material m; Vector2 off;
            void Start() { m = GetComponent<MeshRenderer>().material; }
            void Update() { off += speed * Time.deltaTime; m.SetTextureOffset("_BaseMap", off); }
        }

        // ------------------------------------------------------------------------------------------------ skyline
        static void Skyline(Theme th, System.Random rng, MeshBins b, float X, float Z, System.Func<float, float, float> heightAt, float water)
        {
            if (th.style == Style.Sky)
            {
                // floating islands at every distance, high and low (in orbit: bare rock, debris below and above)
                int n = th.space ? 60 : 38;
                for (int i = 0; i < n; i++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 130 + (float)rng.NextDouble() * 560;
                    float y = th.space ? -120 + (float)rng.NextDouble() * 220 : -10 + (float)rng.NextDouble() * 70 - r * 0.03f;
                    var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    if (th.space) b["rock"].Cylinder(p - Vector3.up * 6, 2 + (float)rng.NextDouble() * 10, 6 + (float)rng.NextDouble() * 16, 6 + (float)rng.NextDouble() * 14, 7, top: true, bottom: true, phase: (float)rng.NextDouble() * 6);
                    else Buildings.FloatingIsland(b, rng, p, 4 + (float)rng.NextDouble() * 14 + r * 0.02f);
                }
                return;
            }
            // neighbourhoods: rejection-sampled footprints, dense along the arena's edge, then clusters with open ground
            // between them; every building turned to face the arena (the front row squared to its walls)
            float reach = th.sea ? 440 : 330;
            var placed = new List<Vector3>();                       // x, z, footprint radius
            var grid = new Dictionary<(int, int), List<int>>();
            int target = Mathf.RoundToInt(560 * th.density), tries = 0;
            while (placed.Count < target && tries++ < target * 30)
            {
                float x = ((float)rng.NextDouble() * 2 - 1) * (X + reach), z = ((float)rng.NextDouble() * 2 - 1) * (Z + reach);
                float d = Outside(X, Z, x, z);
                if (d < 5 || d > reach) continue;
                float cluster = Mathf.PerlinNoise(x / 85 + 7.3f, z / 85 - 2.1f);
                float want = d < 28 ? 0.95f : Mathf.Lerp(1f, 0.55f, d / reach) * (cluster > 0.42f ? 1 : 0.06f);
                if (rng.NextDouble() > want) continue;
                float gy = heightAt(x, z);
                if (th.sea && gy < 1.0f) continue;                                       // not in the bay
                if (!th.sea && gy > 30 && th.style != Style.Observatory) continue;        // not up the mountainsides
                float steep = Mathf.Abs(heightAt(x + 5, z) - gy) + Mathf.Abs(heightAt(x, z + 5) - gy);
                if (steep > 4.5f) continue;
                float k = Mathf.Clamp01(d / reach);
                float size = Mathf.Lerp(9, 17, k) * (0.75f + (float)rng.NextDouble() * 0.5f);
                float rad = size * 0.56f + 0.8f;
                if (Overlaps(grid, placed, x, z, rad)) continue;
                int id = placed.Count; placed.Add(new Vector3(x, z, rad));
                var cell = ((int)Mathf.Floor(x / 20), (int)Mathf.Floor(z / 20));
                if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                list.Add(id);
                float yaw = Mathf.Atan2(-x, -z) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 26;
                if (d < 30) yaw = Mathf.Round(yaw / 90) * 90;
                b.Xf = Matrix4x4.TRS(new Vector3(x, gy - 0.25f, z), Quaternion.Euler(0, yaw, 0), Vector3.one);
                int floors = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(th.minH, th.maxH, k) * (0.65f + (float)rng.NextDouble() * 0.7f)), 1, 8);
                Place(th, rng, b, size, floors);
            }
            b.Xf = Matrix4x4.identity;
        }

        static bool Overlaps(Dictionary<(int, int), List<int>> grid, List<Vector3> placed, float x, float z, float r)
        {
            int cx = (int)Mathf.Floor(x / 20), cz = (int)Mathf.Floor(z / 20);
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                    if (grid.TryGetValue((cx + i, cz + j), out var list))
                        foreach (var k in list)
                        {
                            var p = placed[k]; float dx = p.x - x, dz = p.y - z, rr = p.z + r;
                            if (dx * dx + dz * dz < rr * rr) return true;
                        }
            return false;
        }

        /// <summary>one building at the local origin (MeshBins.Xf places and turns it; local +Z faces the arena)</summary>
        static void Place(Theme th, System.Random rng, MeshBins b, float size, int floors)
        {
            var p = Vector3.zero;
            float w = size, d = size * (0.62f + (float)rng.NextDouble() * 0.25f);
            double pick = rng.NextDouble();
            switch (th.style)
            {
                case Style.Japan:
                    if (rng.NextDouble() < 0.035 && floors >= 3) { Buildings.Pagoda(b, p, size * 0.55f, 3 + rng.Next(3)); break; }
                    string wall = pick < 0.36 ? "wall" : pick < 0.66 ? "plaster" : "planks";
                    string roof = rng.NextDouble() < 0.5 ? "roof" : "tiles";
                    Buildings.Townhouse(b, rng, p, w, d, floors, th.lit, wall, roof);
                    break;
                case Style.West:
                    if (pick < 0.08) Buildings.WaterTower(b, p, 2.2f + (float)rng.NextDouble());
                    else Buildings.Storefront(b, rng, p, w * 0.8f, d, 1);
                    break;
                case Style.Industry:
                    Buildings.Hall(b, rng, p, w * 1.4f, d * 1.2f, 6 + floors * 3.5f);
                    if (pick < 0.05) Buildings.Chimney(b, p + new Vector3(w * 0.5f, 0, -d * 0.4f), 25 + floors * 9 + (float)rng.NextDouble() * 15, 1.4f + (float)rng.NextDouble());
                    else if (pick < 0.16) Buildings.Tank(b, p + new Vector3(-w * 0.95f, 0, 0), 3.5f + (float)rng.NextDouble() * 3, 8 + (float)rng.NextDouble() * 8);
                    break;
                case Style.Observatory:
                    if (pick < 0.55) Buildings.Townhouse(b, rng, p, w * 0.8f, d * 0.8f, Mathf.Min(floors, 2), false, pick < 0.3 ? "plaster" : "planks", "tiles");
                    break;
                default:
                    Buildings.Block(b, rng, p, w * 1.3f, d * 1.3f, Mathf.Max(2, floors));
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ landmarks
        static void Landmarks(MapDef map, Theme th, System.Random rng, MeshBins b, float X, float Z, System.Func<float, float, float> heightAt)
        {
            Vector3 OnGround(float x, float z) => new Vector3(x, heightAt(x, z) - 0.3f, z);
            switch (map.id)
            {
                case "hanabi":
                    Buildings.Castle(b, OnGround(-260, 210), 22);                       // across the bay, up the hill
                    Buildings.Pagoda(b, OnGround(240, 230), 9, 5);
                    Buildings.Pagoda(b, OnGround(-300, -150), 8, 3);
                    break;
                case "kagura":
                    Buildings.Pagoda(b, OnGround(0, Z + 95), 11, 5);
                    Buildings.Castle(b, OnGround(-X - 170, -Z - 160), 20);
                    break;
                case "lantern":
                    Buildings.Castle(b, OnGround(X + 150, Z + 120), 24);
                    Buildings.Pagoda(b, OnGround(-X - 90, Z + 70), 9, 5);
                    break;
                case "starfall":
                    // observatories on the nearer peaks
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 260 + (float)rng.NextDouble() * 260;
                        Buildings.Observatory(b, OnGround(Mathf.Cos(a) * r, Mathf.Sin(a) * r), 7 + (float)rng.NextDouble() * 6);
                    }
                    Buildings.Observatory(b, OnGround(X + 70, 0), 14);
                    break;
                case "foundry":
                    for (int i = 0; i < 6; i++) Buildings.Chimney(b, OnGround(-X - 60 + i * 14, Z + 75 + (i % 2) * 8), 55 + i * 4, 2.4f);
                    for (int i = 0; i < 4; i++) Buildings.Tank(b, OnGround(X + 60, -Z - 30 + i * 16), 6, 14);
                    break;
                case "gulch":
                case "mile":
                    for (int i = 0; i < 3; i++) Buildings.WaterTower(b, OnGround(-X - 30 - i * 40, Z + 40 + i * 12), 3);
                    break;
                case "cloudstep":
                    Buildings.FloatingIsland(b, rng, new Vector3(0, 30, Z + 120), 24);
                    break;
            }
        }

        /// <summary>map props scaled up as scenery (a giant torii standing in Hanabi's bay, boats, fishing junk)</summary>
        static void Showpieces(MapDef map, Theme th, System.Random rng, Transform root, float X, float Z, System.Func<float, float, float> heightAt)
        {
            void Put(string id, Vector3 p, float height, float yawDeg)
            {
                var pf = Resources.Load<GameObject>("ZUProps/" + id);
                if (pf == null) return;
                var go = Object.Instantiate(pf, root, false);
                go.transform.localPosition = p; go.transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
                go.transform.localScale = Vector3.one * height;
            }
            float sea = map.water.HasValue ? (float)map.water.Value : 0;
            switch (map.id)
            {
                case "hanabi":
                    Put("prop_amatsu_torii", new Vector3(0, sea - 1.5f, -Z - 75), 26, 0);
                    for (int i = 0; i < 7; i++)
                    {
                        float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 95 + (float)rng.NextDouble() * 50;
                        Put("prop_hanabi_boat", new Vector3(Mathf.Cos(a) * r, sea - 0.4f, Mathf.Sin(a) * r), 4.5f, (float)rng.NextDouble() * 360);
                    }
                    break;
                case "kagura":
                case "lantern":
                    Put("prop_amatsu_torii", new Vector3(-X - 30, heightAt(-X - 30, 0), 0), 14, 90);
                    break;
                case "gulch":
                    Put("prop_gulch_loco", new Vector3(X + 40, heightAt(X + 40, Z + 10), Z + 10), 6, 90);
                    break;
                case "mile":
                    for (int i = 0; i < 6; i++) Put("prop_mile_sign", new Vector3(-X - 20 - i * 35, heightAt(-X - 20 - i * 35, -Z - 18), -Z - 18), 9, 180);
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ trees
        static void Trees(Theme th, System.Random rng, Transform root, float X, float Z, System.Func<float, float, float> heightAt, float water)
        {
            if (th.trees.Length == 0 || th.treeCount == 0) return;
            var prefabs = new List<GameObject>();
            foreach (var id in th.trees) { var p = Resources.Load<GameObject>("ZUProps/" + id); if (p != null) prefabs.Add(p); }
            if (prefabs.Count == 0) return;
            var parent = new GameObject("Trees").transform; parent.SetParent(root, false);
            int placed = 0, tries = 0;
            while (placed < th.treeCount && tries++ < th.treeCount * 12)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 60 + (float)Mathf.Pow((float)rng.NextDouble(), 0.8f) * 520;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                if (Outside(X, Z, x, z) < 14) continue;
                if (Mathf.PerlinNoise(x / 85 + 7.3f, z / 85 - 2.1f) > 0.46f && rng.NextDouble() < 0.8) continue;   // mostly outside the towns
                float y = heightAt(x, z);
                if (th.sea && y < 1.5f) continue;
                float steep = Mathf.Abs(heightAt(x + 3, z) - y) + Mathf.Abs(heightAt(x, z + 3) - y);
                if (steep > 2.6f) continue;
                var pf = prefabs[rng.Next(prefabs.Count)];
                var go = Object.Instantiate(pf, parent, false);
                go.transform.localPosition = new Vector3(x, y - 0.2f, z);
                go.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0);
                go.transform.localScale = Vector3.one * (6 + (float)rng.NextDouble() * 6);
                placed++;
            }
        }
    }
}
