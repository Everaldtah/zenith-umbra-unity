#if UNITY_EDITOR
// `zu_bake_barrels`: Gantetsu's chainguns cut in two the way the TS upgradeChaingun (Hammer.ts) cuts them when the Tripo
// guns load - the front of the fitted gun (the six-barrel cluster, GUN_BARREL of its length back from the muzzle) becomes
// its own mesh hung in a "spin" node on the cluster's own axis, the rest is the receiver - baked once here instead of at
// load. Reads HeldImport's Resources/ZUProps/held_<id>.prefab (unit length, grip at the origin, muzzle on +Z) and writes
// Looks/Resources/ZULooks/split_held_<id>.prefab (+ its meshes in split_held_<id>.asset) in the same frame: the receiver,
// "spin" at the cluster's axis (x, y, 0) holding the barrels, and a "muzzle" marker at (axis, front face) for the flash.
// HeldRig.LoadSplit picks these up for Gantetsu; without them it falls back to the whole gun (no spin).
// (The whole file is editor-only: tools/unitycheck globs Assets/ZU/Game/** and has no UnityEditor.)
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class BarrelBake
    {
        const string Src = "Assets/ZU/Resources/ZUProps", Dst = "Assets/ZU/Game/Looks/Resources/ZULooks";
        static readonly string[] IDS = { "prop_gantetsu_hinoko_v2", "prop_gantetsu_hanabi_v2", "prop_gantetsu_hinoko", "prop_gantetsu_hanabi" };
        /// <summary>how much of the gun's length, from the muzzle back, is the barrel cluster that spins (TS GUN_BARREL; else 0.38)</summary>
        static readonly Dictionary<string, float> GUN_BARREL = new Dictionary<string, float> { { "prop_gantetsu_hinoko_v2", 0.4f }, { "prop_gantetsu_hanabi_v2", 0.36f } };

        [CliCommand("zu_bake_barrels", "Cut Gantetsu's held chainguns into receiver + spinning barrel cluster (TS upgradeChaingun) -> Looks/Resources/ZULooks/split_held_<id>.prefab")]
        public static string Bake([CliArg("id", "one held prop id; empty = all four")] string id = "")
        {
            var report = new List<string>();
            Directory.CreateDirectory(Dst);
            foreach (var pid in IDS)
            {
                if (!string.IsNullOrEmpty(id) && pid != id) continue;
                report.Add(pid + ": " + One(pid, GUN_BARREL.TryGetValue(pid, out var f) ? f : 0.38f));
            }
            AssetDatabase.SaveAssets();
            return string.Join("\n", report);
        }

        /// <summary>one part's geometry in the held prefab's root space</summary>
        sealed class Part
        {
            public Mesh mesh; public Material[] mats; public Matrix4x4 M; public bool flip;
            public Vector3[] pos;
        }

        static string One(string pid, float frac)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Src}/held_{pid}.prefab");
            if (pf == null) return "skipped (no held_ prefab; zu_import_held first)";
            var inst = Object.Instantiate(pf);
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); inst.transform.localScale = Vector3.one;
                var parts = new List<Part>();
                var box = new Bounds(); bool any = false;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh; var r = mf.GetComponent<Renderer>();
                    if (mesh == null || r == null) continue;
                    var p = new Part { mesh = mesh, mats = r.sharedMaterials, M = inst.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix };
                    p.flip = p.M.determinant < 0;
                    var v = mesh.vertices; p.pos = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++)
                    {
                        var q = p.pos[i] = p.M.MultiplyPoint3x4(v[i]);
                        if (!any) { box = new Bounds(q, Vector3.zero); any = true; } else box.Encapsulate(q);
                    }
                    parts.Add(p);
                }
                if (!any) return "no geometry";
                // the cut: triangles wholly in front of it are the barrels (TS: tris with all three z > zCut)
                float zCut = box.max.z - box.size.z * frac;
                var front = new List<int>[parts.Count][]; var back = new List<int>[parts.Count][];
                var bb = new Bounds(); bool anyFront = false; int nf = 0, nb = 0;
                for (int k = 0; k < parts.Count; k++)
                {
                    var p = parts[k]; int subs = p.mesh.subMeshCount;
                    front[k] = new List<int>[subs]; back[k] = new List<int>[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        front[k][s] = new List<int>(); back[k][s] = new List<int>();
                        if (p.mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        var tri = p.mesh.GetTriangles(s);
                        for (int t = 0; t + 2 < tri.Length; t += 3)
                        {
                            int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                            bool inFront = p.pos[a].z > zCut && p.pos[b].z > zCut && p.pos[c].z > zCut;
                            var list = inFront ? front[k][s] : back[k][s];
                            if (p.flip) { list.Add(a); list.Add(c); list.Add(b); } else { list.Add(a); list.Add(b); list.Add(c); }
                            if (!inFront) { nb++; continue; }
                            nf++;
                            foreach (var i in new[] { a, b, c })
                            {
                                var xy = new Vector3(p.pos[i].x, p.pos[i].y, 0);
                                if (!anyFront) { bb = new Bounds(xy, Vector3.zero); anyFront = true; } else bb.Encapsulate(xy);
                            }
                        }
                    }
                }
                if (!anyFront) return $"nothing in front of the cut z {zCut:0.000} (box z {box.min.z:0.000}..{box.max.z:0.000})";
                var ax = new Vector3(bb.center.x, bb.center.y, 0);

                // the split prefab: receiver parts, the spin node on the cluster axis with the barrels, the muzzle marker
                string meshPath = $"{Dst}/split_held_{pid}.asset", prefabPath = $"{Dst}/split_held_{pid}.prefab";
                var root = new GameObject("split_held_" + pid);
                try
                {
                    var spin = new GameObject("spin").transform; spin.SetParent(root.transform, false); spin.localPosition = ax;
                    var muzzle = new GameObject("muzzle").transform; muzzle.SetParent(root.transform, false); muzzle.localPosition = new Vector3(ax.x, ax.y, box.max.z);
                    var meshes = new List<Mesh>();
                    for (int k = 0; k < parts.Count; k++)
                    {
                        var rm = Build(parts[k], back[k], Vector3.zero, $"{pid} receiver {k}", out var rmats);
                        if (rm != null) { meshes.Add(rm); Node(root.transform, "receiver" + (k > 0 ? "_" + k : ""), rm, rmats); }
                        var fm = Build(parts[k], front[k], ax, $"{pid} barrels {k}", out var fmats);
                        if (fm != null) { meshes.Add(fm); Node(spin, "barrels" + (k > 0 ? "_" + k : ""), fm, fmats); }
                    }
                    if (File.Exists(meshPath)) AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(meshes[0], meshPath);
                    for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
                    AssetDatabase.ImportAsset(meshPath);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { Object.DestroyImmediate(root); }
                return $"barrels {nf} tris / receiver {nb} tris; cut z {zCut:0.000} of {box.min.z:0.000}..{box.max.z:0.000} ({frac:0.00}); axis ({ax.x:0.000}, {ax.y:0.000}); muzzle z {box.max.z:0.000}";
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static void Node(Transform parent, string name, Mesh mesh, Material[] mats)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>the given triangles of a part as a new mesh in the prefab root's space less `shift` (compacted: only the
        /// vertices they use; normals and tangents carried through the part's matrix), one submesh per non-empty source
        /// submesh with its material; null when there are none</summary>
        static Mesh Build(Part p, List<int>[] tris, Vector3 shift, string name, out Material[] mats)
        {
            mats = null;
            var src = p.mesh; var remap = new Dictionary<int, int>(); var order = new List<int>();
            var subs = new List<int[]>(); var subMats = new List<Material>();
            for (int s = 0; s < tris.Length; s++)
            {
                if (tris[s].Count == 0) continue;
                var idx = new int[tris[s].Count];
                for (int i = 0; i < idx.Length; i++)
                {
                    int o = tris[s][i];
                    if (!remap.TryGetValue(o, out var n)) { n = remap[o] = order.Count; order.Add(o); }
                    idx[i] = n;
                }
                subs.Add(idx); subMats.Add(p.mats.Length == 0 ? null : p.mats[Mathf.Min(s, p.mats.Length - 1)]);
            }
            if (subs.Count == 0) return null;
            var nrmM = p.M.inverse.transpose; float tw = p.flip ? -1 : 1;
            var srcN = src.normals; var srcT = src.tangents; var srcC = src.colors;
            var v = new Vector3[order.Count];
            var n3 = srcN.Length == src.vertexCount ? new Vector3[order.Count] : null;
            var t4 = srcT.Length == src.vertexCount ? new Vector4[order.Count] : null;
            var c4 = srcC.Length == src.vertexCount ? new Color[order.Count] : null;
            for (int i = 0; i < order.Count; i++)
            {
                int o = order[i];
                v[i] = p.pos[o] - shift;
                if (n3 != null) n3[i] = nrmM.MultiplyVector(srcN[o]).normalized;
                if (t4 != null) { var t = p.M.MultiplyVector(srcT[o]).normalized; t4[i] = new Vector4(t.x, t.y, t.z, srcT[o].w * tw); }
                if (c4 != null) c4[i] = srcC[o];
            }
            var m = new Mesh { name = name };
            if (order.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v;
            if (n3 != null) m.normals = n3;
            if (t4 != null) m.tangents = t4;
            if (c4 != null) m.colors = c4;
            // every UV channel the source has, at its own width (a 4-wide copy of a 2-wide channel doubles it for nothing)
            var uv = new List<Vector4>();
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = (UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch);
                if (!src.HasVertexAttribute(attr)) continue;
                uv.Clear(); src.GetUVs(ch, uv);
                int dim = src.GetVertexAttributeDimension(attr);
                if (dim <= 2) { var o2 = new List<Vector2>(order.Count); foreach (var o in order) o2.Add(uv[o]); m.SetUVs(ch, o2); }
                else if (dim == 3) { var o3 = new List<Vector3>(order.Count); foreach (var o in order) o3.Add(uv[o]); m.SetUVs(ch, o3); }
                else { var o4 = new List<Vector4>(order.Count); foreach (var o in order) o4.Add(uv[o]); m.SetUVs(ch, o4); }
            }
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s, false);
            m.RecalculateBounds();
            if (n3 == null) m.RecalculateNormals();
            mats = subMats.ToArray();
            return m;
        }
    }
}
#endif
