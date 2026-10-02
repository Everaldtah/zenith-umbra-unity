// `zu_shader_check`: look at ZU/Surface and ZU/Water without a map. Builds a few blockout boxes with the ZU vertex
// contract (uv0 world-metric, uv2 box-local + grime, uv3 half extents + foot Y), a non-box ramp, and a water plane over
// a slope, all with procedural test textures, in an isolated PreviewRenderUtility scene; renders them to PNGs from a
// few angles and reports the shaders' compile messages. Nothing in the project is touched.
// (PreviewRenderUtility provides no _CameraDepthTexture / _CameraOpaqueTexture, so the water renders its fallback.)
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class ZUShaderCheck
    {
        [CliCommand("zu_shader_check", "Render ZU/Surface test boxes and a ZU/Water plane to <out>/shader_check_*.png and report shader compile messages")]
        public static string Run(
            [CliArg("out", "output folder, relative to the project")] string outDir = "Screenshots",
            [CliArg("width", "pixels")] int width = 1280,
            [CliArg("height", "pixels")] int height = 720)
        {
            var surface = Shader.Find("ZU/Surface");
            var water = Shader.Find("ZU/Water");
            if (surface == null || water == null) return $"shader missing: surface={(surface != null)} water={(water != null)}";

            var report = new StringBuilder();
            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;   // the PNGs must show the real variants, not the cyan placeholder
            var albedo = MakeAlbedo(); var normal = MakeNormal(); var mask = MakeMask();
            try
            {
                Directory.CreateDirectory(Path.GetFullPath(outDir));

                // ---- surface scene
                var full = SurfaceMaterial(surface, albedo, normal, mask);
                var plain = SurfaceMaterial(surface, albedo, normal, mask);
                foreach (var p in new[] { "_Bevel", "_EdgeLift", "_Grime", "_Cavity", "_Macro" }) plain.SetFloat(p, 0f);
                plain.SetFloat("_RoughMin", 0f);

                var boxes = new List<Vector3>(); var halfs = new List<Vector3>(); var grimes = new List<bool>();
                void Box(float cx, float cy, float cz, float hx, float hy, float hz, bool grime)
                { boxes.Add(new Vector3(cx, cy, cz)); halfs.Add(new Vector3(hx, hy, hz)); grimes.Add(grime); }
                Box(0, -0.15f, 0, 12, 0.15f, 12, false);              // floor slab
                Box(0, 1.6f, -4, 6, 1.6f, 0.35f, true);               // long wall standing on the floor
                Box(0, 3.3f, -4, 6.2f, 0.1f, 0.5f, false);            // trim on its top (thin: bevel capped)
                Box(4.5f, 1.2f, 0, 0.35f, 1.2f, 3.5f, true);          // side wall
                Box(-3, 0.9f, 1, 0.4f, 0.9f, 0.4f, true);             // pillar
                Box(-5.5f, 0.6f, -1.5f, 1.2f, 0.6f, 1.2f, false);     // crate (not on the ground: all 12 edges bevel)
                Box(2, 0.5f, 2.5f, 1.5f, 0.5f, 1.0f, true);           // low block

                var sceneMesh = BuildBoxes(boxes, halfs, grimes);
                // a non-box ramp (uv3 = 1e4): no bevel, no grime, the rest applies
                var ramp = BuildQuad(new Vector3(-1, 0, 4), new Vector3(3, 0, 4), new Vector3(3, 1.2f, 7), new Vector3(-1, 1.2f, 7), Vector3.up);

                var views = new (string name, Vector3 eye, Vector3 at)[]
                {
                    ("surface_1_overview", new Vector3(9, 6.5f, 9), new Vector3(0, 0.8f, -0.5f)),
                    ("surface_2_grazing", new Vector3(-7.5f, 1.1f, -2.2f), new Vector3(3, 1.2f, -3.9f)),
                    ("surface_3_corner", new Vector3(-4.6f, 2.3f, -1.4f), new Vector3(-3, 1.4f, 1)),
                    ("surface_4_wallfoot", new Vector3(1.5f, 1.0f, 0.5f), new Vector3(1.5f, 0.7f, -3.65f)),
                };
                foreach (var v in views)
                    report.AppendLine(Render(outDir, "shader_check_" + v.name + ".png", width, height, v.eye, v.at, 38,
                        new[] { (sceneMesh, full), (ramp, full) }));
                report.AppendLine(Render(outDir, "shader_check_surface_0_layers_off.png", width, height, views[0].eye, views[0].at, 38,
                    new[] { (sceneMesh, plain), (ramp, plain) }));
                // the grime and the macro variation pushed up (full grime over 2.5 m, macro at 4 m cells), so both are
                // unmistakable in a still: at the defaults they are deliberately subtle under sRGB + lighting
                var strong = SurfaceMaterial(surface, albedo, normal, mask);
                strong.SetFloat("_Grime", 1f); strong.SetFloat("_GrimeHeight", 2.5f);
                strong.SetFloat("_Macro", 0.8f); strong.SetFloat("_MacroScale", 4f);
                report.AppendLine(Render(outDir, "shader_check_surface_5_layers_strong.png", width, height, views[0].eye, views[0].at, 38,
                    new[] { (sceneMesh, strong), (ramp, strong) }));

                // ---- water scene: a beach rising out of the water away from the camera, a pier post standing in it,
                // the plane at y = 0 (shoreline where the slope crosses y = 0, at z ~ -4.8)
                var slope = BuildQuad(new Vector3(-14, 2f, -12), new Vector3(14, 2f, -12), new Vector3(14, -3f, 6), new Vector3(-14, -3f, 6), Vector3.up);
                var pierMesh = BuildBoxes(new List<Vector3> { new Vector3(3, 0.5f, -3) }, new List<Vector3> { new Vector3(0.5f, 1.5f, 0.5f) }, new List<bool> { false });
                var plane = BuildQuad(new Vector3(-30, 0, -30), new Vector3(30, 0, -30), new Vector3(30, 0, 30), new Vector3(-30, 0, 30), Vector3.up);
                var waterMapped = new Material(water); waterMapped.SetTexture("_NormalMap", normal); waterMapped.EnableKeyword("_NORMALMAP");
                var waterPlain = new Material(water);
                var waterOff = new Material(water); waterOff.EnableKeyword("_ZU_SCENE_OFF");   // no depth / opaque texture
                report.AppendLine(Render(outDir, "shader_check_water_1_normalmap.png", width, height, new Vector3(0, 3.5f, 12), new Vector3(0, 0, -2), 45,
                    new[] { (slope, full), (pierMesh, full), (plane, waterMapped) }));
                report.AppendLine(Render(outDir, "shader_check_water_2_procedural.png", width, height, new Vector3(-8, 1.6f, 10), new Vector3(2, 0, -4), 45,
                    new[] { (slope, full), (pierMesh, full), (plane, waterPlain) }));
                report.AppendLine(Render(outDir, "shader_check_water_3_fallback.png", width, height, new Vector3(0, 3.5f, 12), new Vector3(0, 0, -2), 45,
                    new[] { (slope, full), (pierMesh, full), (plane, waterOff) }));

                Object.DestroyImmediate(full); Object.DestroyImmediate(plain); Object.DestroyImmediate(strong);
                Object.DestroyImmediate(waterMapped); Object.DestroyImmediate(waterPlain); Object.DestroyImmediate(waterOff);
                foreach (var m in new[] { sceneMesh, ramp, slope, pierMesh, plane }) Object.DestroyImmediate(m);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = prevAsync;
                Object.DestroyImmediate(albedo); Object.DestroyImmediate(normal); Object.DestroyImmediate(mask);
            }

            report.AppendLine(Messages(surface));
            report.AppendLine(Messages(water));
            return report.ToString().TrimEnd();
        }

        // Keyword sets worth compiling per pass: the game's real-world combinations (cascaded soft shadows, Forward+,
        // SSAO, light layers, decals, APV, lightmaps, LOD fade, debug display) that an import alone never touches.
        static readonly (string shader, int pass, string keywords)[] Variants =
        {
            ("ZU/Surface", 0, "_NORMALMAP _METALLICSPECGLOSSMAP _OCCLUSIONMAP _EMISSION _MAIN_LIGHT_SHADOWS_CASCADE _ADDITIONAL_LIGHTS _ADDITIONAL_LIGHT_SHADOWS _SHADOWS_SOFT _SCREEN_SPACE_OCCLUSION _LIGHT_LAYERS _CLUSTER_LIGHT_LOOP _REFLECTION_PROBE_BLENDING _REFLECTION_PROBE_BOX_PROJECTION _LIGHT_COOKIES LOD_FADE_CROSSFADE _DBUFFER_MRT3 FOG_EXP2 _WRITE_RENDERING_LAYERS"),
            ("ZU/Surface", 0, "_METALLICSPECGLOSSMAP _OCCLUSIONMAP _MAIN_LIGHT_SHADOWS _ADDITIONAL_LIGHTS_VERTEX _SHADOWS_SOFT_HIGH PROBE_VOLUMES_L2 SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING _REFLECTION_PROBE_ATLAS FOG_LINEAR"),
            ("ZU/Surface", 0, "_NORMALMAP DEBUG_DISPLAY _SCREEN_SPACE_IRRADIANCE _MAIN_LIGHT_SHADOWS_SCREEN _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT _ALPHAPREMULTIPLY_ON _ADDITIONAL_LIGHTS"),
            ("ZU/Surface", 0, "_NORMALMAP LIGHTMAP_ON DIRLIGHTMAP_COMBINED _CLUSTER_LIGHT_LOOP _MAIN_LIGHT_SHADOWS_CASCADE SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING _SCREEN_SPACE_REFLECTION EVALUATE_SH_MIXED"),
            ("ZU/Surface", 1, "_ALPHATEST_ON _CASTING_PUNCTUAL_LIGHT_SHADOW LOD_FADE_CROSSFADE"),
            ("ZU/Surface", 2, "_NORMALMAP _METALLICSPECGLOSSMAP _OCCLUSIONMAP _EMISSION _MAIN_LIGHT_SHADOWS_CASCADE _SHADOWS_SOFT _GBUFFER_NORMALS_OCT _DBUFFER_MRT3 _RENDER_PASS_ENABLED _CLUSTER_LIGHT_LOOP PROBE_VOLUMES_L1 LOD_FADE_CROSSFADE _WRITE_RENDERING_LAYERS"),
            ("ZU/Surface", 2, "_METALLICSPECGLOSSMAP LIGHTMAP_ON SHADOWS_SHADOWMASK _MAIN_LIGHT_SHADOWS _SCREEN_SPACE_IRRADIANCE"),
            ("ZU/Surface", 3, "_ALPHATEST_ON LOD_FADE_CROSSFADE"),
            ("ZU/Surface", 4, "_NORMALMAP _METALLICSPECGLOSSMAP _WRITE_SMOOTHNESS _GBUFFER_NORMALS_OCT _WRITE_RENDERING_LAYERS LOD_FADE_CROSSFADE"),
            ("ZU/Surface", 4, "_ALPHATEST_ON _WRITE_SMOOTHNESS _SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF"),
            ("ZU/Surface", 5, "_EMISSION _METALLICSPECGLOSSMAP EDITOR_VISUALIZATION"),
            ("ZU/Surface", 6, "_ALPHATEST_ON LOD_FADE_CROSSFADE _ADD_PRECOMPUTED_VELOCITY"),
            ("ZU/Water", 0, "_NORMALMAP _MAIN_LIGHT_SHADOWS_CASCADE _ADDITIONAL_LIGHTS _ADDITIONAL_LIGHT_SHADOWS _SHADOWS_SOFT _LIGHT_LAYERS _CLUSTER_LIGHT_LOOP _REFLECTION_PROBE_BLENDING _REFLECTION_PROBE_BOX_PROJECTION _LIGHT_COOKIES FOG_EXP2 _WRITE_RENDERING_LAYERS"),
            ("ZU/Water", 0, "_ZU_SCENE_OFF _MAIN_LIGHT_SHADOWS _ADDITIONAL_LIGHTS PROBE_VOLUMES_L2 _REFLECTION_PROBE_ATLAS _SHADOWS_SOFT_LOW FOG_LINEAR"),
            ("ZU/Water", 0, "_MAIN_LIGHT_SHADOWS_SCREEN EVALUATE_SH_VERTEX"),
        };

        [CliCommand("zu_shader_variants", "Synchronously compile the keyword variants of ZU/Surface and ZU/Water that the game uses, and report compile messages")]
        public static string CompileVariants()
        {
            var mi = typeof(ShaderUtil).GetMethod("CompilePass", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (mi == null) return "ShaderUtil.CompilePass not found in this editor";
            var sb = new StringBuilder();
            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                foreach (var v in Variants)
                {
                    var sh = Shader.Find(v.shader);
                    var m = new Material(sh);
                    foreach (var k in v.keywords.Split(' ')) m.EnableKeyword(k);
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    try { mi.Invoke(null, new object[] { m, v.pass, true }); sb.AppendLine($"ok   {v.shader} pass {v.pass} ({m.GetPassName(v.pass)}) {t0.ElapsedMilliseconds} ms: {v.keywords}"); }
                    catch (System.Exception e) { sb.AppendLine($"FAIL {v.shader} pass {v.pass}: {(e.InnerException ?? e).Message}"); }
                    Object.DestroyImmediate(m);
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = prevAsync; }
            sb.AppendLine(Messages(Shader.Find("ZU/Surface")));
            sb.AppendLine(Messages(Shader.Find("ZU/Water")));
            return sb.ToString().TrimEnd();
        }

        static string Messages(Shader s)
        {
            var msgs = ShaderUtil.GetShaderMessages(s);
            var sb = new StringBuilder($"{s.name}: error={ShaderUtil.ShaderHasError(s)} messages={msgs.Length}");
            foreach (var m in msgs) sb.Append($"\n  [{m.severity}] {m.platform} line {m.line}: {m.message} {m.messageDetails}");
            return sb.ToString();
        }

        static Material SurfaceMaterial(Shader shader, Texture2D albedo, Texture2D normal, Texture2D mask)
        {
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTextureScale("_BaseMap", Vector2.one / 1.5f);     // one texture repeat every 1.5 m of world UV
            mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_OcclusionMap", mask); mat.EnableKeyword("_OCCLUSIONMAP"); mat.SetFloat("_OcclusionStrength", 1f);
            mat.SetFloat("_Smoothness", 0.85f); mat.SetFloat("_Metallic", 0f);
            mat.SetColor("_BaseColor", new Color(0.85f, 0.78f, 0.66f));
            mat.enableInstancing = true;
            return mat;
        }

        static string Render(string outDir, string file, int width, int height, Vector3 eye, Vector3 at, float fov, (Mesh mesh, Material mat)[] items)
        {
            var pru = new PreviewRenderUtility();
            try
            {
                foreach (var it in items)
                {
                    var go = new GameObject(it.mesh.name);
                    go.AddComponent<MeshFilter>().sharedMesh = it.mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = it.mat;
                    pru.AddSingleGO(go);
                }
                var cam = pru.camera;
                cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.7f, 0.9f);
                cam.transform.position = eye; cam.transform.LookAt(at);
                var sun = pru.lights[0];
                sun.intensity = 1.6f; sun.color = new Color(1f, 0.95f, 0.85f);
                sun.transform.rotation = Quaternion.Euler(48, 210, 0);
                sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.9f;
                pru.lights[1].intensity = 0.3f; pru.lights[1].transform.rotation = Quaternion.Euler(-15, 40, 0);
                pru.ambientColor = new Color(0.36f, 0.4f, 0.48f);
                pru.BeginStaticPreview(new Rect(0, 0, width, height));
                pru.Render(true);
                var tex = pru.EndStaticPreview();
                var path = Path.GetFullPath(Path.Combine(outDir, file));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return $"{outDir}/{file}: {width}x{height}, eye {eye} -> {at}";
            }
            finally { pru.Cleanup(); }
        }

        // ---- meshes with the ZU vertex contract ------------------------------------------------------------------

        struct Face { public Vector3 n, t, b; public Face(Vector3 n, Vector3 t, Vector3 b) { this.n = n; this.t = t; this.b = b; } }
        static readonly Face[] Faces =
        {
            new Face(Vector3.right, Vector3.forward, Vector3.up), new Face(Vector3.left, Vector3.back, Vector3.up),
            new Face(Vector3.up, Vector3.right, Vector3.forward), new Face(Vector3.down, Vector3.right, Vector3.back),
            new Face(Vector3.forward, Vector3.left, Vector3.up),  new Face(Vector3.back, Vector3.right, Vector3.up),
        };

        static Mesh BuildBoxes(List<Vector3> centres, List<Vector3> halfs, List<bool> grimes)
        {
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var tan = new List<Vector4>();
            var uv0 = new List<Vector2>(); var uv2 = new List<Vector4>(); var uv3 = new List<Vector4>(); var tris = new List<int>();
            for (int bi = 0; bi < centres.Count; bi++)
            {
                var c = centres[bi]; var h = halfs[bi]; float footY = c.y - h.y; float gw = grimes[bi] ? 1f : 0f;
                foreach (var f in Faces)
                {
                    float hn = Mathf.Abs(Vector3.Dot(f.n, h)), ht = Mathf.Abs(Vector3.Dot(f.t, h)), hb = Mathf.Abs(Vector3.Dot(f.b, h));
                    float w = Mathf.Sign(Vector3.Dot(Vector3.Cross(f.n, f.t), f.b));   // bitangent = w * cross(n, t) must be b
                    int i0 = pos.Count;
                    foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    {
                        var p = c + f.n * hn + f.t * (su * ht) + f.b * (sv * hb);
                        pos.Add(p); nrm.Add(f.n); tan.Add(new Vector4(f.t.x, f.t.y, f.t.z, w));
                        uv0.Add(new Vector2(Vector3.Dot(p, f.t), Vector3.Dot(p, f.b)));       // world metric
                        uv2.Add(new Vector4(p.x - c.x, p.y - c.y, p.z - c.z, gw));
                        uv3.Add(new Vector4(h.x, h.y, h.z, footY));
                    }
                    AddQuad(tris, pos, i0, f.n);
                }
            }
            return Finish("zu_check_boxes", pos, nrm, tan, uv0, uv2, uv3, tris);
        }

        // a single quad whose front faces `faceToward`; non-box geometry: uv2 = 0, uv3.xyz = 1e4 (no bevel, no grime)
        static Mesh BuildQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 faceToward)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            if (Vector3.Dot(n, faceToward) < 0) n = -n;
            var t = (b - a).normalized; var bt = Vector3.Cross(n, t);
            float w = Mathf.Sign(Vector3.Dot(bt, (d - a)));   // bitangent along a->d
            var pos = new List<Vector3> { a, b, c, d }; var nrm = new List<Vector3>(); var tan = new List<Vector4>();
            var uv0 = new List<Vector2>(); var uv2 = new List<Vector4>(); var uv3 = new List<Vector4>(); var tris = new List<int>();
            foreach (var p in pos)
            {
                nrm.Add(n); tan.Add(new Vector4(t.x, t.y, t.z, w));
                uv0.Add(new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, w * bt)));
                uv2.Add(Vector4.zero); uv3.Add(new Vector4(1e4f, 1e4f, 1e4f, 0));
            }
            AddQuad(tris, pos, 0, n);
            return Finish("zu_check_quad", pos, nrm, tan, uv0, uv2, uv3, tris);
        }

        static void AddQuad(List<int> tris, List<Vector3> pos, int i0, Vector3 n)
        {
            // Unity front face: Cross(p1 - p0, p2 - p0) points along the normal
            bool flip = Vector3.Dot(Vector3.Cross(pos[i0 + 1] - pos[i0], pos[i0 + 2] - pos[i0]), n) < 0;
            if (flip) tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            else tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
        }

        static Mesh Finish(string name, List<Vector3> pos, List<Vector3> nrm, List<Vector4> tan, List<Vector2> uv0, List<Vector4> uv2, List<Vector4> uv3, List<int> tris)
        {
            var m = new Mesh { name = name };
            m.SetVertices(pos); m.SetNormals(nrm); m.SetTangents(tan);
            m.SetUVs(0, uv0); m.SetUVs(2, uv2); m.SetUVs(3, uv3);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- procedural test textures: a brick-ish tile with a height field, so the normal map and the AO read ----

        const int N = 256;
        static float Height(int x, int y)
        {
            // two rows of bricks per tile, offset by half a brick; rounded tops, mortar grooves
            float fy = y / (float)N * 2f; int row = Mathf.FloorToInt(fy); float ry = fy - row;
            float fx = x / (float)N * 2f + (row % 2 == 0 ? 0f : 0.5f); float rx = fx - Mathf.Floor(fx);
            float ex = Mathf.Min(rx, 1 - rx) * 2f, ey = Mathf.Min(ry, 1 - ry) * 2f;
            float edge = Mathf.Min(ex, ey);
            float h = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - 0.08f) / 0.25f));
            h += 0.05f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
            return h;
        }

        static Texture2D MakeAlbedo()
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "zu_check_albedo" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float h = Height(x, y);
                float fy = y / (float)N * 2f; int row = Mathf.FloorToInt(fy);
                float fx = x / (float)N * 2f + (row % 2 == 0 ? 0f : 0.5f);
                float brick = 0.78f + 0.14f * Mathf.PerlinNoise(Mathf.Floor(fx) * 3.7f + row * 1.3f, row * 2.1f);
                float v = Mathf.Lerp(0.42f, brick, h) + 0.04f * (Mathf.PerlinNoise(x * 0.3f, y * 0.3f) - 0.5f);
                px[y * N + x] = new Color(v, v * 0.96f, v * 0.9f, 1f);
            }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeNormal()
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "zu_check_normal" };
            var px = new Color32[N * N];
            const float strength = 6f;
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (Height((x + 1) % N, y) - Height((x + N - 1) % N, y)) * strength;
                float dy = (Height(x, (y + 1) % N) - Height(x, (y + N - 1) % N)) * strength;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeMask()
        {
            // R metallic 0, G AO (mortar dark), B unused, A smoothness
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "zu_check_mask" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float h = Height(x, y);
                px[y * N + x] = new Color(0f, Mathf.Lerp(0.35f, 1f, h), 0f, Mathf.Lerp(0.3f, 0.6f, h));
            }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }
    }
}
