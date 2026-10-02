// Map props (the TS game's Tripo models: festival stalls, yagura towers, torii, boats, locomotives, ...): `zu_import_props`
// turns each Assets/ZU/Art/Props/<id>/<id>.fbx (tools/blender/glb2fbx.py) into Resources/ZUProps/<id>.prefab - a URP
// material from its baked maps and the model normalised the way the TS MapScene places it: 1 m tall, standing on y = 0,
// centred on the origin. LevelView then sets its height to the map data's `s`.
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var report = new List<string>(); int ok = 0;
            foreach (var dir in Directory.GetDirectories(Src).Select(d => d.Replace('\\', '/')))
            {
                string pid = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(id) && pid != id) continue;
                var r = One(dir, pid);
                if (r == null) ok++; else report.Add(pid + ": " + r);
            }
            AssetDatabase.SaveAssets();
            return $"{ok} prop prefab(s) in {Out}" + (report.Count > 0 ? "; problems: " + string.Join("; ", report) : "");
        }

        /// <summary>props that really are metal keep their metal map; on everything else (wood, paint, cloth, stone) Tripo's
        /// metal channel is noise that turns a red torii into black chrome</summary>
        static bool IsMetal(string pid) => new[] { "loco", "car", "orrery", "telescope", "dish", "crane", "gaspump", "payload", "katana", "blade",
            "axe", "shotgun", "hammer", "shuriken", "nodachi", "sword", "bracer", "fist", "chain", "vending", "crucible", "press" }.Any(k => pid.Contains(k));

        static string One(string dir, string pid)
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
                PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{pid}.prefab");
                return null;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
