// Hero models: import settings for everything under Assets/ZU/Art/Heroes, and `zu_import_hero <id>` which turns a hero's
// two FBX files (tools/blender/glb2fbx.py: <id>_lod0.fbx = the HD Tripo mesh, <id>_lod1.fbx = the game mesh, the same
// skeleton) into Assets/ZU/Art/Heroes/<id>/<id>.prefab: the game rig with a humanoid Avatar, the HD mesh rebound onto the
// game skeleton as LOD0, URP materials from the baked maps, scaled to the hero's height from the game data.
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using ZU.Game;

namespace ZU.EditorTools
{
    public class HeroImportSettings : AssetPostprocessor
    {
        const string Root = "Assets/ZU/Art/";
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;   // ZU builds the materials itself
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.optimizeGameObjects = false;
            // keep the FBX's top-level node (Blender's Armature, carrying the -90 deg axis conversion and any unit scale) as a
            // child: collapsed into the model root, that rotation sits where an Avatar's skeleton expects an identity root,
            // and humanoid clips then come out with the body tens of metres off and turned on its back
            m.preserveHierarchy = true;
            m.importBlendShapes = true;
            m.importCameras = false; m.importLights = false;
            // the viewmodel cuts the arms out of the hero mesh at runtime (FirstPerson.ArmsMesh): the vertices must stay readable
            if (assetPath.Contains("/Heroes/")) { m.importAnimation = false; m.animationType = ModelImporterAnimationType.Generic; m.isReadable = true; }
            if (assetPath.Contains("/Props/")) { m.importAnimation = false; m.animationType = ModelImporterAnimationType.None; }
        }
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var t = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            t.maxTextureSize = 4096; t.textureCompression = TextureImporterCompression.CompressedHQ; t.mipmapEnabled = true;
            if (n.Contains("normal")) t.textureType = TextureImporterType.NormalMap;
            else if (n.Contains("_rm") || n.Contains("rough") || n.Contains("metal") || n.EndsWith("_mask")) { t.textureType = TextureImporterType.Default; t.sRGBTexture = false; }
        }
    }

    public static class HeroImport
    {
        const string Dir = "Assets/ZU/Art/Heroes";

        [CliCommand("zu_import_hero", "Build a hero's prefab (avatar, LOD0 HD mesh on the game skeleton, URP materials, true height) from <id>_lod0.fbx / <id>_lod1.fbx")]
        public static string Import([CliArg("id", "hero id, e.g. kaien")] string id)
        {
            var data = ZuData.Get();
            var def = data.Def(id);
            if (def == null) return "unknown hero " + id;
            string d = $"{Dir}/{id}";
            var lod1 = AssetDatabase.LoadAssetAtPath<GameObject>($"{d}/{id}_lod1.fbx");
            var lod0 = AssetDatabase.LoadAssetAtPath<GameObject>($"{d}/{id}_lod0.fbx");
            if (lod1 == null) return $"missing {d}/{id}_lod1.fbx (run tools/blender/glb2fbx.py first)";

            // ---- the avatar (from the game rig: both LODs share its skeleton)
            var map = HumanRig.MapFor(lod1.transform);
            if (map == null) return "no known skeleton under " + id + "_lod1";
            var avatar = HumanRig.BuildAvatar(lod1, map, out var report);
            if (avatar == null || !avatar.isValid) return "avatar failed: " + report;
            avatar.name = id + "_avatar";
            string avPath = $"{d}/{id}_avatar.asset";
            avatar = HumanRig.SaveAvatar(avatar, avPath);
            if (!avatar.isValid || !avatar.isHuman) return "avatar lost validity when saved: " + avPath;

            // ---- materials
            bool mech = def.frame == "mech";
            float metalCap = mech ? 0.7f : 0.25f, selfLight = id == "mirei" ? 0.04f : 0.16f;   // TS CharacterView: BRIGHT_SUITS get less
            var mat1 = MakeMaterial($"{d}/{id}_lod1_tex", $"{d}/{id}_lod1.mat", lod0 != null ? 1024 : 2048, metalCap, selfLight);   // LOD1 is only seen far off when an HD LOD0 exists
            var mat0 = lod0 != null ? MakeMaterial($"{d}/{id}_lod0_tex", $"{d}/{id}_lod0.mat", 2048, metalCap, selfLight) : null;

            // ---- the prefab: the game rig + Animator, the HD mesh rebound onto its bones as LOD0
            var root = new GameObject(id);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(lod1);
            rig.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.name = "rig";
            var anim = rig.GetComponent<Animator>(); if (anim == null) anim = rig.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var bones = rig.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var r1 = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in r1) { r.sharedMaterial = mat1; r.updateWhenOffscreen = false; }
            SkinnedMeshRenderer[] r0 = new SkinnedMeshRenderer[0];
            int rebound = 0, unmatched = 0;
            if (lod0 != null)
            {
                var hd = Object.Instantiate(lod0);
                r0 = hd.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var r in r0)
                {
                    r.bones = r.bones.Select(b => { if (b != null && bones.TryGetValue(b.name, out var nb)) { rebound++; return nb; } unmatched++; return b; }).ToArray();
                    if (r.rootBone != null && bones.TryGetValue(r.rootBone.name, out var rb)) r.rootBone = rb;
                    r.transform.SetParent(rig.transform, false);
                    r.name = "hd_" + r.name;
                    r.sharedMaterial = mat0;
                }
                Object.DestroyImmediate(hd);
            }
            if (r0.Length > 0)
            {
                var lods = root.AddComponent<LODGroup>();
                lods.SetLODs(new[] { new LOD(0.18f, r0), new LOD(0.01f, r1) });
                lods.RecalculateBounds();
            }
            // ---- true height: the game data says how tall the hero stands (the rig's head top is the reference)
            var b1 = new Bounds(rig.transform.position, Vector3.zero);
            foreach (var r in r1) { r.sharedMesh.RecalculateBounds(); b1.Encapsulate(r.bounds); }
            float h = b1.size.y, want = (float)def.height;
            if (h > 0.01f) rig.transform.localScale *= want / h;
            // hair and cloth: the solver binds to the chains (hair_*, skirt_*, cape_*, sleeve_*) in this bind pose on spawn
            var dyn = root.AddComponent<ZU.Dynamics.ZuDynamics>();
            dyn.heroId = id;
            string prefabPath = $"{d}/{id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            // a byte-identical prefab isn't rewritten, so reload it: the loaded copy must see the avatar as saved now
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var check = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponentInChildren<Animator>(true);
            if (check == null || check.avatar == null) return prefabPath + ": saved, but its Animator has no avatar";
            int chains = new[] { "hair_B", "hair_L", "hair_R", "hair_T", "skirt_F", "skirt_L", "skirt_B", "skirt_R", "cape_B", "sleeve_L", "sleeve_R" }
                .Count(pf => bones.ContainsKey(pf + "_1") && bones.ContainsKey(pf + "_2"));
            return $"{prefabPath}: avatar {report}; LOD0 {(r0.Length > 0 ? $"{r0.Length} renderer(s), {rebound} bones rebound, {unmatched} unmatched" : "none")}; height {h:0.00} -> {want:0.00} m; {chains} dynamic chain(s)";
        }

        /// <summary>a URP Lit material from a folder of baked maps: base colour, normal, and the glTF metal-roughness map
        /// converted to URP's mask layout (R metallic, A smoothness), scaled by the GLB's material factors
        /// (gltf_material.json, tools/export/gltf_factors.py). metalCap: Tripo's generated metal channel is noise on
        /// cloth, skin, wood and paint (the TS drops it on characters) - capped unless the thing really is metal.
        /// selfLight: the TS's hint of albedo self-light that keeps dark heroes readable (CharacterView: 0.16).</summary>
        internal static Material MakeMaterial(string texDir, string matPath, int maskMax = 2048, float metalCap = 1f, float selfLight = 0f)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            if (!AssetDatabase.IsValidFolder(texDir)) return mat;
            var files = Directory.GetFiles(texDir).Where(f => f.EndsWith(".png") || f.EndsWith(".jpg")).Select(f => f.Replace('\\', '/')).ToArray();
            string F(params string[] keys) => files.FirstOrDefault(f => keys.Any(k => Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(k)) && !f.EndsWith("_urpmask.png"));
            var baseTex = F("basecolor", "base_color", "albedo", "diffuse");
            var normal = F("normal");
            var rm = F("_rm", "metallic", "roughness");
            // the glTF factors the textures are multiplied by (three applies them; the FBX doesn't carry them)
            float mf = 1, rf = 1; var bc = Color.white;
            string fj = $"{texDir}/gltf_material.json";
            if (File.Exists(fj))
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(fj));
                mf = (float)(j["metallicFactor"] ?? 1); rf = (float)(j["roughnessFactor"] ?? 1);
                if (j["baseColorFactor"] is Newtonsoft.Json.Linq.JArray a && a.Count >= 3) bc = new Color((float)a[0], (float)a[1], (float)a[2], 1);
            }
            var baseMap = baseTex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex) : null;
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", bc);
            if (normal != null) { mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal)); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1); }
            if (rm != null)
            {
                var mask = ToUrpMask(rm, maskMax, Mathf.Min(mf, 1f), metalCap, rf);
                if (mask != null) { mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1); mat.SetFloat("_Metallic", 1); }
            }
            else { mat.DisableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Metallic", Mathf.Min(mf, metalCap)); mat.SetFloat("_Smoothness", 1 - rf * 0.7f); }
            if (selfLight > 0 && baseMap != null)
            {
                mat.EnableKeyword("_EMISSION"); mat.SetTexture("_EmissionMap", baseMap); mat.SetColor("_EmissionColor", Color.white * selfLight);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else { mat.DisableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>glTF ORM/RM (G = roughness, B = metallic) -> URP (R = metallic x metalFactor (capped), A = smoothness =
        /// 1 - roughness x roughFactor)</summary>
        static Texture2D ToUrpMask(string rmPath, int maxSize, float metalFactor = 1, float metalCap = 1, float roughFactor = 1)
        {
            string outPath = rmPath.Substring(0, rmPath.Length - 4) + "_urpmask.png";
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!src.LoadImage(File.ReadAllBytes(rmPath))) return null;
            var px = src.GetPixels32();
            float cap = Mathf.Clamp01(metalCap) * 255f;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32((byte)Mathf.Min(cap, c.b * metalFactor), 0, 0, (byte)(255 - Mathf.Clamp(c.g * roughFactor, 0, 255)));
            }
            // metallic / smoothness at up to maxSize (2048 = half a 4K albedo, as games pack them; 1024 for the distance LOD): a quarter of the pixels to store,
            // nothing visible lost - box-filtered 2x steps
            int w = src.width, h = src.height;
            while (w > maxSize && h > 1 && (w & 1) == 0 && (h & 1) == 0)
            {
                int w2 = w / 2, h2 = h / 2; var half = new Color32[w2 * h2];
                for (int y = 0; y < h2; y++)
                    for (int x = 0; x < w2; x++)
                    {
                        Color32 a = px[(2 * y) * w + 2 * x], b = px[(2 * y) * w + 2 * x + 1], c = px[(2 * y + 1) * w + 2 * x], d = px[(2 * y + 1) * w + 2 * x + 1];
                        half[y * w2 + x] = new Color32((byte)((a.r + b.r + c.r + d.r + 2) / 4), 0, 0, (byte)((a.a + b.a + c.a + d.a + 2) / 4));
                    }
                px = half; w = w2; h = h2;
            }
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(px); dst.Apply();
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src); Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(outPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            ti.sRGBTexture = false; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
