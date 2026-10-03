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
            /// <summary>the Tripo building style (TripoEnv): the outer-world style, or the map's own for the maps on the
            /// default theme (amatsu japan, kurogane, hangar industry, cathedral / rift gothic)</summary>
            public string bstyle;
            internal Transform tripo;                     // where the skyline's Tripo buildings go (null: procedural only)
        }

        static string BuildingStyle(MapDef map, Theme th)
        {
            switch (map.id)
            {
                case "amatsu": return "japan";
                case "kurogane": return "kurogane";
                case "hangar": return "industry";
                case "cathedral": case "rift": return "gothic";
                default: return th.style.ToString().ToLowerInvariant();
            }
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
            if (th.sea) Water(map, root, water, full);
            if (th.clouds) Clouds(map, root);
            if (!full) return;
            if (!th.clouds && !th.space) Terrain(map, th, root, mat, HeightAt, X, Z);

            var bins = new MeshBins();
            // Tripo building models stand in for the procedural ones wherever the style has one of the archetype
            th.bstyle = BuildingStyle(map, th);
            th.tripo = new GameObject("Tripo Buildings").transform; th.tripo.SetParent(root, false);
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
        /// <summary>the PC game's harbour water (MapScene.ts): ZU/Harbour - the painted, rolling sea, its roll tinted by the
        /// map's colour - on a 700 m plane at the map's water level; with the full outer world it reaches the world's edge</summary>
        static void Water(MapDef map, Transform root, float level, bool full)
        {
            if (float.IsNaN(level)) level = -0.7f;
            var src = Resources.Load<Material>("ZUEnv/zu_harbour");
            if (src == null) { Debug.LogWarning("[ZU] ZUEnv/zu_harbour missing"); return; }
            var m = new Material(src) { name = "zu_sea" };
            m.SetColor("_Glow", Conv.Hex(map.tint, new Color(1f, 0.6f, 0.24f)));
            Sheet("Sea", root, level, full ? Extent * 2 : 700, m);
        }

        /// <summary>the PC game's cloud sea under the floating maps (MapScene.ts): ZU/CloudSea - drifting cloud between the
        /// fog colour and white (violet on the Rift), fading out by 450 m - on a 900 m plane at y -22</summary>
        static void Clouds(MapDef map, Transform root)
        {
            var src = Resources.Load<Material>("ZUEnv/zu_cloudsea");
            if (src == null) { Debug.LogWarning("[ZU] ZUEnv/zu_cloudsea missing"); return; }
            var m = new Material(src) { name = "zu_clouds" };
            m.SetColor("_C1", map.fog != null && map.fog.Length == 3 ? Conv.Hex(map.fog[0] as string, Color.white) : Color.white);
            m.SetColor("_C2", map.id == "rift" ? Conv.Hex("#3a1a66") : Color.white);
            Sheet("Cloud sea", root, -22, 900, m);
        }

        /// <summary>a flat, square, unshadowed sheet `size` m across at height y, centred on the map</summary>
        static void Sheet(string name, Transform root, float y, float size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 m square
            go.name = name; go.transform.SetParent(root, false);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = new Vector3(0, y, 0);
            go.transform.localScale = new Vector3(size / 10, 1, size / 10);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
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
            // a Tripo model of the archetype in the plot (b.Xf places it; `at` = local offset), else false: the procedural one
            bool T(string arch, float wantH, float pw, float pd, float hMax, Vector3 at = default)
            {
                if (th.tripo == null) return false;
                var piece = TripoEnv.Building(th.bstyle, arch, wantH, rng);
                if (piece == null) return false;
                TripoEnv.FitInside(piece, th.tripo, b.Xf.MultiplyPoint3x4(at), b.Xf.rotation.eulerAngles.y, pw, pd, hMax);
                return true;
            }
            switch (th.style)
            {
                case Style.Japan:
                    if (rng.NextDouble() < 0.035 && floors >= 3)
                    {
                        int tiers = 3 + rng.Next(3);
                        if (!T("pagoda", tiers * 5.5f, size * 0.8f, size * 0.8f, 40)) Buildings.Pagoda(b, p, size * 0.55f, tiers);
                        break;
                    }
                    string wall = pick < 0.36 ? "wall" : pick < 0.66 ? "plaster" : "planks";
                    string roof = rng.NextDouble() < 0.5 ? "roof" : "tiles";
                    if (!T(floors <= 1 ? "storefront" : "townhouse", floors * 3.2f + 1.5f, w, d, floors * 3.2f + 4))
                        Buildings.Townhouse(b, rng, p, w, d, floors, th.lit, wall, roof);
                    break;
                case Style.West:
                    if (pick < 0.08) { if (!T("watertower", 12, w * 0.5f, w * 0.5f, 14)) Buildings.WaterTower(b, p, 2.2f + (float)rng.NextDouble()); }
                    else if (!T("storefront", 7, w * 0.8f, d, 10)) Buildings.Storefront(b, rng, p, w * 0.8f, d, 1);
                    break;
                case Style.Industry:
                    if (!T("hall", 6 + floors * 3.5f, w * 1.4f, d * 1.2f, 9 + floors * 3.5f)) Buildings.Hall(b, rng, p, w * 1.4f, d * 1.2f, 6 + floors * 3.5f);
                    if (pick < 0.05)
                    {
                        float ch = 25 + floors * 9 + (float)rng.NextDouble() * 15, cr = 1.4f + (float)rng.NextDouble();
                        var at = p + new Vector3(w * 0.5f, 0, -d * 0.4f);
                        if (!T("chimney", ch, cr * 3, cr * 3, ch, at)) Buildings.Chimney(b, at, ch, cr);
                    }
                    else if (pick < 0.16)
                    {
                        float tr = 3.5f + (float)rng.NextDouble() * 3, tht = 8 + (float)rng.NextDouble() * 8;
                        var at = p + new Vector3(-w * 0.95f, 0, 0);
                        if (!T("tank", tht, tr * 2, tr * 2, tht, at)) Buildings.Tank(b, at, tr, tht);
                    }
                    break;
                case Style.Observatory:
                    if (pick < 0.55 && !T("townhouse", 8, w * 0.8f, d * 0.8f, 10))
                        Buildings.Townhouse(b, rng, p, w * 0.8f, d * 0.8f, Mathf.Min(floors, 2), false, pick < 0.3 ? "plaster" : "planks", "tiles");
                    break;
                default:
                    // the maps on the default theme in their own building style (kurogane towers, gothic houses, academy blocks)
                    string arch = th.bstyle == "academy" || (th.bstyle == "kurogane" && pick < 0.4) ? "block" : "townhouse";
                    if (!T(arch, Mathf.Max(2, floors) * 3.3f, w * 1.3f, d * 1.3f, 40)) Buildings.Block(b, rng, p, w * 1.3f, d * 1.3f, Mathf.Max(2, floors));
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ landmarks
        static void Landmarks(MapDef map, Theme th, System.Random rng, MeshBins b, float X, float Z, System.Func<float, float, float> heightAt)
        {
            Vector3 OnGround(float x, float z) => new Vector3(x, heightAt(x, z) - 0.3f, z);
            // a Tripo model of the archetype in place of the procedural landmark, turned to face the arena (false: none)
            bool T(string arch, Vector3 at, float wantH, float w, float hMax)
            {
                var piece = th.tripo == null ? null : TripoEnv.Building(th.bstyle, arch, wantH, rng);
                if (piece == null) return false;
                TripoEnv.FitInside(piece, th.tripo, at, Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg, w, w, hMax);
                return true;
            }
            void Pagoda(Vector3 at, float w, int tiers) { if (!T("pagoda", at, tiers * 5.5f, w * 1.4f, tiers * 7)) Buildings.Pagoda(b, at, w, tiers); }
            switch (map.id)
            {
                case "hanabi":
                    Buildings.Castle(b, OnGround(-260, 210), 22);                       // across the bay, up the hill
                    Pagoda(OnGround(240, 230), 9, 5);
                    Pagoda(OnGround(-300, -150), 8, 3);
                    break;
                case "kagura":
                    Pagoda(OnGround(0, Z + 95), 11, 5);
                    Buildings.Castle(b, OnGround(-X - 170, -Z - 160), 20);
                    break;
                case "lantern":
                    Buildings.Castle(b, OnGround(X + 150, Z + 120), 24);
                    Pagoda(OnGround(-X - 90, Z + 70), 9, 5);
                    break;
                case "starfall":
                    // observatories on the nearer peaks
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 260 + (float)rng.NextDouble() * 260;
                        float s = 7 + (float)rng.NextDouble() * 6; var at = OnGround(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                        if (!T("tower", at, s * 1.5f, s * 1.3f, s * 2)) Buildings.Observatory(b, at, s);
                    }
                    { var at = OnGround(X + 70, 0); if (!T("tower", at, 21, 18, 28)) Buildings.Observatory(b, at, 14); }
                    break;
                case "foundry":
                    for (int i = 0; i < 6; i++) { var at = OnGround(-X - 60 + i * 14, Z + 75 + (i % 2) * 8); float h = 55 + i * 4; if (!T("chimney", at, h, 7, h)) Buildings.Chimney(b, at, h, 2.4f); }
                    for (int i = 0; i < 4; i++) { var at = OnGround(X + 60, -Z - 30 + i * 16); if (!T("tank", at, 14, 12, 14)) Buildings.Tank(b, at, 6, 14); }
                    break;
                case "gulch":
                case "mile":
                    for (int i = 0; i < 3; i++) { var at = OnGround(-X - 30 - i * 40, Z + 40 + i * 12); if (!T("watertower", at, 13, 8, 15)) Buildings.WaterTower(b, at, 3); }
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
