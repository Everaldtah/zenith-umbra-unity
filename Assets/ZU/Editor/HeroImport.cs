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
            if (assetPath.Contains("/Heroes/")) { m.importAnimation = false; m.animationType = ModelImporterAnimationType.Generic; }
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
            var mat1 = MakeMaterial($"{d}/{id}_lod1_tex", $"{d}/{id}_lod1.mat");
            var mat0 = lod0 != null ? MakeMaterial($"{d}/{id}_lod0_tex", $"{d}/{id}_lod0.mat") : null;

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
            string prefabPath = $"{d}/{id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            // a byte-identical prefab isn't rewritten, so reload it: the loaded copy must see the avatar as saved now
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var check = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponentInChildren<Animator>(true);
            if (check == null || check.avatar == null) return prefabPath + ": saved, but its Animator has no avatar";
            return $"{prefabPath}: avatar {report}; LOD0 {(r0.Length > 0 ? $"{r0.Length} renderer(s), {rebound} bones rebound, {unmatched} unmatched" : "none")}; height {h:0.00} -> {want:0.00} m";
        }

        /// <summary>a URP Lit material from a folder of baked maps: base colour, normal, and the glTF metal-roughness map
        /// converted to URP's mask layout (R metallic, A smoothness)</summary>
        static Material MakeMaterial(string texDir, string matPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            if (!AssetDatabase.IsValidFolder(texDir)) return mat;
            var files = Directory.GetFiles(texDir, "*.png").Select(f => f.Replace('\\', '/')).ToArray();
            string F(params string[] keys) => files.FirstOrDefault(f => keys.Any(k => Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(k)) && !f.EndsWith("_urpmask.png"));
            var baseTex = F("basecolor", "base_color", "albedo", "diffuse");
            var normal = F("normal");
            var rm = F("_rm", "metallic", "roughness");
            if (baseTex != null) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex));
            if (normal != null) { mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal)); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1); }
            if (rm != null)
            {
                var mask = ToUrpMask(rm);
                if (mask != null) { mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1); mat.SetFloat("_Metallic", 1); }
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>glTF ORM/RM (G = roughness, B = metallic) -> URP (R = metallic, A = smoothness = 1 - roughness)</summary>
        static Texture2D ToUrpMask(string rmPath)
        {
            string outPath = rmPath.Substring(0, rmPath.Length - 4) + "_urpmask.png";
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!src.LoadImage(File.ReadAllBytes(rmPath))) return null;
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color32(c.b, 0, 0, (byte)(255 - c.g)); }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
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
