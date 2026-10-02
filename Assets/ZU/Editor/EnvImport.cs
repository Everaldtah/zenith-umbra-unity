// Map surfaces and skies: `zu_import_env` turns the TS game's environment kit (tools/export/export_env.py ->
// Assets/ZU/Art/Env: CC0 Poly Haven texture sets, per-map painted albedos, per-map HDRIs; Resources/ZUData/env.json
// says which set paints which surface of which map) into Resources/ZUEnv/<map>_<kind>.mat (ZU/Surface when the shader
// exists, else URP Lit: albedo, normal, the URP mask as metallic-gloss + occlusion, tiling = one repeat per `tile`
// metres on world-metric UVs) and Resources/ZUEnv/sky_<map>.mat (the HDRI panorama, turned so its sun sits where the
// map's sun light comes from). `zu_render_quality` sets the URP asset up for big outdoor maps.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZU.Game;

namespace ZU.EditorTools
{
    public class EnvImportSettings : AssetPostprocessor
    {
        const string Root = "Assets/ZU/Art/Env/";
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var t = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (assetPath.Contains("/Sky/skypano_"))
            {
                // the painted sky the player sees (LDR, sRGB), re-projected to equirectangular by export_env.py
                t.textureShape = TextureImporterShape.Texture2D; t.mipmapEnabled = false; t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp;
                t.maxTextureSize = 4096; t.textureCompression = TextureImporterCompression.CompressedHQ; t.sRGBTexture = true;
                return;
            }
            if (assetPath.Contains("/Sky/"))
            {
                // an equirectangular HDR panorama: no mips (a mip seam shows at the wrap), clamped poles, BC6H
                t.textureShape = TextureImporterShape.Texture2D; t.mipmapEnabled = false; t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp;
                t.maxTextureSize = 8192; t.textureCompression = TextureImporterCompression.CompressedHQ; t.sRGBTexture = false;
                return;
            }
            t.maxTextureSize = 2048; t.anisoLevel = 8; t.mipmapEnabled = true; t.wrapMode = TextureWrapMode.Repeat;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            if (n.EndsWith("_n")) t.textureType = TextureImporterType.NormalMap;
            else if (n.EndsWith("_mask")) { t.textureType = TextureImporterType.Default; t.sRGBTexture = false; }
        }
    }

    public static class EnvImport
    {
        const string Env = "Assets/ZU/Art/Env", Out = "Assets/ZU/Resources/ZUEnv";
        static readonly string[] KINDS = { "ground", "wall", "roof", "wood", "trim", "rock" };
        static readonly (string kind, string set)[] COMMON =
        {
            ("stone", "japanese_stone_wall"), ("plaster", "plastered_wall"), ("metal", "metal_plate"), ("rust", "rusty_corrugated_iron"),
            ("brick", "brick_wall_10"), ("planks", "weathered_planks"), ("concrete", "concrete_pavement"),
            ("tiles", "grey_roof_tiles_02"), ("turf", "dry_ground_rocks"), ("cliff", "cliff_side"), ("sand", "cracked_red_ground"),
        };

        [CliCommand("zu_import_env", "Build Resources/ZUEnv surface materials (<map>_<kind>) and sky materials (sky_<map>) from Assets/ZU/Art/Env")]
        public static string ImportEnv()
        {
            var json = Resources.Load<TextAsset>("ZUData/env");
            if (json == null) return "no Resources/ZUData/env.json (run tools/export/export_env.py)";
            var env = JObject.Parse(json.text);
            var sets = (JObject)env["sets"]; var maps = (JObject)env["maps"]; var hdri = (JObject)env["hdri"];
            Directory.CreateDirectory(Out);
            var shader = Shader.Find("ZU/Surface") ?? Shader.Find("Universal Render Pipeline/Lit");
            var data = ZuData.Get();
            int nm = 0, ns = 0; var missing = new List<string>();
            foreach (var mp in maps.Properties())
            {
                string map = mp.Name;
                foreach (var kind in KINDS)
                {
                    var slot = mp.Value[kind] as JObject;
                    if (slot == null) continue;
                    string set = (string)slot["set"];
                    var info = sets[set] as JObject;
                    if (info == null) { missing.Add($"{map}.{kind}: set {set}"); continue; }
                    float tile = (float)info["tile"];
                    var mat = Load($"{Out}/{map}_{kind}.mat", shader);
                    mat.shader = shader;
                    var albedo = Tex($"{Env}/Albedo/{map}_{kind}.jpg");
                    var normal = Tex($"{Env}/Sets/{set}_n.jpg");
                    var mask = Tex($"{Env}/Sets/{set}_mask.png");
                    // no painted albedo: the set's relief under the blockout palette colour (TS: trim = plastered_wall relief)
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetColor("_BaseColor", albedo != null ? Color.white : LevelView.Palette(kind));
                    var scale = new Vector2(1f / tile, 1f / tile);
                    mat.SetTextureScale("_BaseMap", scale);
                    if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
                    if (mask != null)
                    {
                        mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                        mat.SetTexture("_OcclusionMap", mask); mat.EnableKeyword("_OCCLUSIONMAP"); mat.SetFloat("_OcclusionStrength", 1f);
                        mat.SetFloat("_Smoothness", 0.85f); mat.SetFloat("_Metallic", (bool)info["metal"] ? 1f : 0f);
                    }
                    else missing.Add($"{map}.{kind}: no mask");
                    Finish(mat, ROUGH_MIN.TryGetValue(kind, out var rm) ? rm : 0.5f);
                    EditorUtility.SetDirty(mat); nm++;
                }
            }
            // the common kit for the outer world's buildings: a CC0 set's relief under a palette colour (no painted albedo)
            foreach (var (kind, set) in COMMON)
            {
                var info = sets[set] as JObject;
                if (info == null) { missing.Add($"common.{kind}: set {set}"); continue; }
                var mat = Load($"{Out}/common_{kind}.mat", shader);
                mat.shader = shader;
                mat.SetTexture("_BaseMap", null);
                mat.SetColor("_BaseColor", LevelView.Palette(kind));
                mat.SetTextureScale("_BaseMap", Vector2.one / (float)info["tile"]);
                var normal = Tex($"{Env}/Sets/{set}_n.jpg"); var mask = Tex($"{Env}/Sets/{set}_mask.png");
                if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                if (mask != null)
                {
                    mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetTexture("_OcclusionMap", mask); mat.EnableKeyword("_OCCLUSIONMAP");
                    mat.SetFloat("_Smoothness", 0.85f); mat.SetFloat("_Metallic", (bool)info["metal"] ? 1f : 0f);
                }
                Finish(mat, kind == "metal" || kind == "rust" ? 0.35f : 0.6f);
                EditorUtility.SetDirty(mat); nm++;
            }
            // water: ZU/Water when it exists, else a glossy Lit stand-in
            {
                var ws = Shader.Find("ZU/Water");
                var mat = Load($"{Out}/water.mat", ws ?? Shader.Find("Universal Render Pipeline/Lit"));
                if (ws != null) mat.shader = ws;
                else { mat.SetColor("_BaseColor", new Color(0.10f, 0.25f, 0.40f)); mat.SetFloat("_Smoothness", 0.95f); }
                EditorUtility.SetDirty(mat);
            }
            // the outer world's terrain material (referenced here so the shader ships in a build)
            {
                var ts = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (ts != null) { var mat = Load($"{Out}/terrain.mat", ts); mat.enableInstancing = true; EditorUtility.SetDirty(mat); }
            }
            // skies
            var skyShader = Shader.Find("Skybox/Panoramic");
            foreach (var hp in hdri.Properties())
            {
                string map = hp.Name;
                var tex = Tex($"{Env}/Sky/{(string)hp.Value["file"]}");
                if (tex == null) { missing.Add("sky " + map); continue; }
                // the HDRI lights the scene (ambient + reflections); the painted panorama is what you see (below)
                var mat = Load($"{Out}/skyhdr_{map}.mat", skyShader);
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_Mapping", 1); mat.SetFloat("_ImageType", 0); mat.SetFloat("_MirrorOnBack", 0); mat.SetFloat("_Layout", 0);
                mat.SetFloat("_Exposure", 1f);
                mat.SetFloat("_Rotation", SkyRotation(data, map, (float)hp.Value["sunPhi"]));
                EditorUtility.SetDirty(mat); ns++;
            }
            // the painted skies (TS env/sky_<map>.webp, re-projected): one display material per map
            int np = 0;
            foreach (var f in Directory.GetFiles($"{Env}/Sky", "skypano_*.jpg"))
            {
                string map = Path.GetFileNameWithoutExtension(f).Substring("skypano_".Length);
                var tex = Tex(f.Replace('\\', '/'));
                if (tex == null) continue;
                var mat = Load($"{Out}/sky_{map}.mat", skyShader);
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_Mapping", 1); mat.SetFloat("_ImageType", 0); mat.SetFloat("_MirrorOnBack", 0); mat.SetFloat("_Layout", 0);
                mat.SetFloat("_Exposure", 1f); mat.SetFloat("_Rotation", 0);
                EditorUtility.SetDirty(mat); np++;
            }
            AssetDatabase.SaveAssets();
            return $"{nm} surface material(s) ({shader.name}), {ns} HDRI + {np} painted sky material(s) in {Out}" + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : "");
        }

        /// <summary>degrees to turn the panorama so the HDRI's sun stands where the map's sun light comes from. The TS keeps
        /// sunPhi as the sun's bearing atan2(z, x) in three's equirect lookup (u = 0.5 + phi / 2pi); Unity's panoramic
        /// skybox reads u = 0.5 - atan2(z, x) / 2pi, so the same column sits at bearing -sunPhi here, and _Rotation turns
        /// the sky about +Y.</summary>
        public static float SkyRotation(ZU.Sim.Data.GameData data, string map, float sunPhi)
        {
            if (!data.Map.TryGetValue(map, out var m) || m.sun?.dir == null || m.sun.dir.Length != 3) return 0;
            var s = Conv.U(m.sun.dir[0], m.sun.dir[1], m.sun.dir[2]);
            float want = Mathf.Atan2(s.z, s.x), have = -sunPhi;
            float deg = (have - want) * Mathf.Rad2Deg * SkySign;
            return Mathf.Repeat(deg, 360f);
        }
        /// <summary>which way _Rotation turns the sky, measured (zu_sky_check)</summary>
        public const float SkySign = 1f;

        /// <summary>the surface's roughness floor (TS Surfaces.ts zuRoughMin: big flat surfaces never mirror) and ZU/Surface's
        /// inspector toggles kept in step with the keywords set from code (its toggle drawer re-applies the float)</summary>
        static readonly Dictionary<string, float> ROUGH_MIN = new Dictionary<string, float>
        { ["ground"] = 0.72f, ["wall"] = 0.6f, ["rock"] = 0.75f, ["wood"] = 0.55f, ["trim"] = 0.45f, ["roof"] = 0.4f };
        static void Finish(Material mat, float roughMin)
        {
            mat.enableInstancing = true;
            if (mat.HasProperty("_RoughMin")) mat.SetFloat("_RoughMin", roughMin);
            void Sync(string toggle, string keyword) { if (mat.HasProperty(toggle)) mat.SetFloat(toggle, mat.IsKeywordEnabled(keyword) ? 1 : 0); }
            Sync("_UseNormalMap", "_NORMALMAP"); Sync("_UseMetallicGlossMap", "_METALLICSPECGLOSSMAP");
            Sync("_UseOcclusionMap", "_OCCLUSIONMAP"); Sync("_UseEmission", "_EMISSION");
        }

        static Material Load(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            return m;
        }
        static Texture2D Tex(string path) => File.Exists(path) ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;

        [CliCommand("zu_render_quality", "Set the PC URP asset up for big outdoor maps: shadow distance/cascades/resolution, MSAA, depth + opaque textures, SSAO")]
        public static string RenderQuality(
            [CliArg("shadowDistance", "metres")] float shadowDistance = 160,
            [CliArg("msaa", "1, 2, 4 or 8")] int msaa = 4)
        {
            var report = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("PC")) continue;
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                a.shadowDistance = shadowDistance;
                a.shadowCascadeCount = 4;
                a.cascade4Split = new Vector3(0.05f, 0.15f, 0.38f);   // 8 / 24 / 61 / 160 m: crisp near, still shadowed far
                a.mainLightShadowmapResolution = 4096;
                a.msaaSampleCount = msaa;
                a.supportsHDR = true;
                a.supportsCameraDepthTexture = true;
                a.supportsCameraOpaqueTexture = true;
                a.shadowDepthBias = 1f; a.shadowNormalBias = 0.6f;
                EditorUtility.SetDirty(a);
                report.Add($"{path}: shadows {shadowDistance} m x4 cascades @4096, MSAA {msaa}x, HDR, depth + opaque textures");
            }
            // SSAO on the PC renderer: a little stronger and wider than the template's (big outdoor shapes, OW-like contact shadows)
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("PC")) continue;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var so = new SerializedObject(o);
                    var set = so.FindProperty("m_Settings");
                    set.FindPropertyRelative("Intensity").floatValue = 0.7f;
                    set.FindPropertyRelative("Radius").floatValue = 0.45f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    report.Add($"{path}: SSAO intensity 0.7 radius 0.45");
                }
            }
            QualitySettings.lodBias = 1.6f;
            AssetDatabase.SaveAssets();
            return string.Join("; ", report);
        }
    }
}
