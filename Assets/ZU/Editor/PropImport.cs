// Map props (the TS game's Tripo models: festival stalls, yagura towers, torii, boats, locomotives, ...): `zu_import_props`
// turns each Assets/ZU/Art/Props/<id>/<id>.fbx (tools/blender/glb2fbx.py) into Resources/ZUProps/<id>.prefab - a URP
// material from its baked maps and the model normalised the way the TS MapScene places it: 1 m tall, standing on y = 0,
// centred on the origin. LevelView then sets its height to the map data's `s`.
// A prop whose FBX has <id>_LOD0/_LOD1/_LOD2 meshes (glb2fbx 'prop' mode) gets one LODGroup on the prefab root (the engine's
// prop convention, tuned at lodBias 1): LOD1 below 25 % of the screen, LOD2 below 10 %, culled below 2 % - except landmarks
// (placed taller than LandmarkS m anywhere in maps.json), whose LOD2 stays to the horizon. No fade, one shared material.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class PropImport
    {
        const string Src = "Assets/ZU/Art/Props", Out = "Assets/ZU/Resources/ZUProps";

        [CliCommand("zu_import_props", "Build Resources/ZUProps/<id>.prefab for every prop FBX under Assets/ZU/Art/Props (or one: --id)")]
        public static string ImportProps([CliArg("id", "one prop id; empty = all")] string id = "")
        {
            if (!AssetDatabase.IsValidFolder(Src)) return "no " + Src;
            Directory.CreateDirectory(Out);
            var sizes = MapSizes();
            var report = new List<string>(); var lodded = new List<string>(); int ok = 0;
            foreach (var dir in Directory.GetDirectories(Src).Select(d => d.Replace('\\', '/')))
            {
                string pid = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(id) && pid != id) continue;
                var r = One(dir, pid, sizes.TryGetValue(pid, out var s) ? s : 0f, lodded);
                if (r == null) ok++; else report.Add(pid + ": " + r);
            }
            AssetDatabase.SaveAssets();
            return $"{ok} prop prefab(s) in {Out}" + (lodded.Count > 0 ? "; LODs: " + string.Join(", ", lodded) : "")
                + (report.Count > 0 ? "; problems: " + string.Join("; ", report) : "");
        }

        /// <summary>map props placed taller than this (metres) are landmarks, seen from across the map: their LOD2 is never culled</summary>
        const float LandmarkS = 6f;

        /// <summary>the tallest `s` each prop id is placed at in any map (maps.json prop entries are flat objects)</summary>
        static Dictionary<string, float> MapSizes()
        {
            var sizes = new Dictionary<string, float>();
            var text = File.ReadAllText("Assets/ZU/Resources/ZUData/maps.json");
            foreach (Match m in Regex.Matches(text, "\"id\"\\s*:\\s*\"(prop_[^\"]+)\"[^{}]*?\"s\"\\s*:\\s*([0-9.]+)"))
            {
                float s = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!sizes.TryGetValue(m.Groups[1].Value, out var was) || s > was) sizes[m.Groups[1].Value] = s;
            }
            return sizes;
        }

        /// <summary>props that really are metal keep their metal map; on everything else (wood, paint, cloth, stone) Tripo's
        /// metal channel is noise that turns a red torii into black chrome</summary>
        static bool IsMetal(string pid) => new[] { "loco", "car", "orrery", "telescope", "dish", "crane", "gaspump", "payload", "katana", "blade",
            "axe", "shotgun", "hammer", "shuriken", "nodachi", "sword", "bracer", "fist", "chain", "vending", "crucible", "press" }.Any(k => pid.Contains(k));

        static string One(string dir, string pid, float mapS, List<string> lodded)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{pid}.fbx");
            if (model == null) return "no " + pid + ".fbx";
            var mat = HeroImport.MakeMaterial($"{dir}/{pid}_tex", $"{dir}/{pid}.mat", 1024, IsMetal(pid) ? 1f : 0.15f);
            var root = new GameObject(pid);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "model";
                inst.transform.SetParent(root.transform, false);
                var rs = inst.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) return "no renderers";
                foreach (var r in rs)
                {
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                // normalise: 1 m tall, base on y = 0, centred in X / Z (TS MapScene.placeProp: scale s / h, lift by -min.y, centre)
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y < 1e-4f) return "flat model";
                float k = 1f / b.size.y;
                inst.transform.localScale *= k;
                inst.transform.localPosition = new Vector3(-b.center.x * k, -b.min.y * k, -b.center.z * k);
                // LODs: the importer puts its own LODGroup (default transitions) on the model for _LODn meshes - replace it
                var lv = Enumerable.Range(0, 3).Select(i => rs.Where(r => r.name.EndsWith("_LOD" + i)).ToArray()).ToArray();
                if (lv.All(l => l.Length > 0))
                {
                    foreach (var g in inst.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);
                    bool landmark = mapS > LandmarkS;
                    var lods = root.AddComponent<LODGroup>();
                    lods.fadeMode = LODFadeMode.None;
                    lods.SetLODs(new[] { new LOD(0.25f, lv[0]), new LOD(0.10f, lv[1]), new LOD(landmark ? 0f : 0.02f, lv[2]) });
                    lods.RecalculateBounds();
                    lodded.Add(pid + (landmark ? " (landmark)" : ""));
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{pid}.prefab");
                return null;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
