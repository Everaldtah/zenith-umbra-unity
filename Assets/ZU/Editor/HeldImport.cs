// Held-weapon props fitted into the grip frame at import (port of TomoeProps.fitProp): `zu_import_held` builds
// Resources/ZUProps/held_<id>.prefab from each weapon's Resources/ZUProps/<id>.prefab (PropImport's normalised Tripo
// model). Image-to-3D keeps the pose of the picture, so a weapon drawn on the diagonal comes out on the diagonal - its
// bounding box says nothing about where the haft or barrel runs; the object's own axes (PCA over its vertices) do.
// The held prefab is UNIT length along its long axis (HeldRig scales it by size x model height) in the frame HeldRig
// expects: blade / gun / card - long axis on +Z, the thin grip end back (a gun's muzzle forward), centred on the PCA mean;
// axe - haft up +Y from the pommel at the origin, the head's bulky side on +X; bow - limbs on Y, the belly forward (+Z),
// centred; hammer - the pre-oriented Dawnbreaker
// stood on its pommel with the striking face on +X; chaingun - a gun with the grip point (22% from the back, 30% up)
// moved to the origin; shuriken - lying flat (its thinnest axis on +Y), unit diameter.
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace ZU.EditorTools
{
    public static class HeldImport
    {
        const string Dir = "Assets/ZU/Resources/ZUProps";
        static readonly (string id, string kind)[] ITEMS =
        {
            ("prop_raijin_katana", "blade"), ("prop_enra_chainblade", "blade"), ("prop_enra_blade", "blade"), ("prop_enra_bracer", "blade"),
            ("prop_hayate_nodachi", "blade"), ("prop_hayate_shuriken_v2", "shuriken"), ("prop_hayate_shuriken", "shuriken"),
            ("prop_yuzu_bow", "bow"), ("prop_seiran_bow", "bow"), ("prop_kaien_talisman", "blade"),
            ("prop_tomoe_shotgun", "gun"), ("prop_tomoe_blade", "blade"), ("prop_tomoe_axe", "axe"), ("prop_tenkai_hammer", "hammer"),
            ("prop_gantetsu_hanabi_v2", "chaingun"), ("prop_gantetsu_hinoko_v2", "chaingun"), ("prop_gantetsu_hanabi", "chaingun"), ("prop_gantetsu_hinoko", "chaingun"),
        };
        /// <summary>fitProp decides "up" by which side of the gun reaches further out; Hanabi's tall carry handle out-reaches
        /// its drum magazine, so it comes out upside down - rolled over here (Hammer.ts GUN_ROLL)</summary>
        static readonly Dictionary<string, float> GUN_ROLL = new Dictionary<string, float> { { "prop_gantetsu_hanabi", 180 } };

        [CliCommand("zu_import_held", "Fit the held-weapon props into the grip frame (PCA) -> Resources/ZUProps/held_<id>.prefab (or one: --id)")]
        public static string Import([CliArg("id", "one prop id; empty = all")] string id = "")
        {
            var report = new List<string>(); int ok = 0;
            foreach (var (pid, kind) in ITEMS)
            {
                if (!string.IsNullOrEmpty(id) && pid != id) continue;
                var r = One(pid, kind);
                if (r == null || !r.StartsWith("no ")) ok++;
                if (r != null) report.Add(pid + ": " + r);
            }
            AssetDatabase.SaveAssets();
            return $"{ok} held prefab(s) in {Dir}" + (report.Count > 0 ? "; notes: " + string.Join("; ", report) : "");
        }

        static string One(string pid, string kind)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{pid}.prefab");
            if (src == null) return "no " + pid + ".prefab (run zu_import_props)";
            var root = new GameObject("held_" + pid);
            try
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                m.name = "model";
                string note = kind == "hammer" ? Hammer(m, root.transform) : kind == "shuriken" ? Flat(m, root.transform) : Fit(m, root.transform, kind, pid);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/held_{pid}.prefab");
                return note;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>every vertex of the model's meshes in the model root's space (subsampled to ~4000 per mesh)</summary>
        static List<Vector3> Points(GameObject m)
        {
            var pts = new List<Vector3>();
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh; if (mesh == null) continue;
                var v = mesh.vertices; int step = Mathf.Max(1, v.Length / 4000);
                var M = m.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i += step) pts.Add(M.MultiplyPoint3x4(v[i]));
            }
            return pts;
        }

        /// <summary>the pre-oriented Dawnbreaker (PropImport: 1 m tall, pommel on y = 0, centred): only rolled so the striking
        /// face (-X after the FBX mirror) comes round to +X</summary>
        static string Hammer(GameObject m, Transform root)
        {
            m.transform.SetParent(root, false);
            m.transform.localRotation = Quaternion.Euler(0, 180, 0);
            return null;
        }

        /// <summary>a prop laid flat: its thinnest axis turned to +Y, its widest extent scaled to 1, centred</summary>
        static string Flat(GameObject m, Transform root)
        {
            var pts = Points(m);
            var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p);
            var size = b.size; int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var inner = new GameObject("inner").transform; inner.SetParent(root, false);
            m.transform.SetParent(inner, false); m.transform.localPosition = -b.center;
            inner.localRotation = Quaternion.FromToRotation(axes[thin], Vector3.up);
            inner.localScale = Vector3.one / Mathf.Max(1e-6f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            return null;
        }

        static string Fit(GameObject m, Transform root, string kind, string pid)
        {
            var pts = Points(m);
            if (pts.Count < 3) return "no geometry";
            var mean = Vector3.zero; foreach (var p in pts) mean += p; mean /= pts.Count;
            var C = new float[9];
            foreach (var p in pts) { var d = p - mean; float[] dd = { d.x, d.y, d.z }; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) C[i * 3 + j] += dd[i] * dd[j]; }
            Vector3 Mul(Vector3 v) => new Vector3(C[0] * v.x + C[1] * v.y + C[2] * v.z, C[3] * v.x + C[4] * v.y + C[5] * v.z, C[6] * v.x + C[7] * v.y + C[8] * v.z);
            Vector3 Power(Vector3 v, Vector3? not)
            {
                for (int i = 0; i < 60; i++) { if (not.HasValue) v -= not.Value * Vector3.Dot(v, not.Value); v = Mul(v).normalized; }
                if (not.HasValue) v = (v - not.Value * Vector3.Dot(v, not.Value)).normalized;
                return v;
            }
            var a1 = Power(new Vector3(1, 0.7f, 0.4f).normalized, null);
            var a2 = Power(new Vector3(-0.3f, 1, 0.5f).normalized, a1);
            // extent along the long axis, and how bulky each end is (the gun's stock, the axe head, the blade)
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (var p in pts) { float u = Vector3.Dot(p - mean, a1); lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); }
            float span = hi - lo; var ends = new float[2]; var side = new Vector3[2]; var n = new int[2];
            foreach (var p in pts)
            {
                var d = p - mean; float u = (Vector3.Dot(d, a1) - lo) / span; var perp = d - a1 * Vector3.Dot(d, a1);
                int e = u < 0.25f ? 0 : u > 0.75f ? 1 : -1;
                if (e < 0) continue;
                ends[e] = Mathf.Max(ends[e], perp.magnitude); side[e] += perp; n[e]++;
            }
            // gun: the muzzle (thin end) forward (+Z); blade: the grip (thin end) at the back, blade forward; axe: the head up (+Y)
            int heavy = ends[1] > ends[0] ? 1 : 0;
            bool wantHeavyForward = kind != "gun" && kind != "chaingun";
            var dir = a1 * ((heavy == 1) == wantHeavyForward ? 1 : -1);
            var target = kind == "axe" || kind == "bow" ? Vector3.up : Vector3.forward;
            var q = Quaternion.FromToRotation(dir, target);
            // roll about the long axis: the axe head juts out on +X (the swing's cutting side); the gun's grip hangs down (-Y),
            // i.e. its bulkier side across the second axis points down
            Vector3 across;
            if (kind == "axe") across = side[heavy] / Mathf.Max(1, n[heavy]);
            else if (kind == "bow")
            {
                // a bow stands on Y with its belly (the arc's apex) forward: the string runs straight between the tips, so the
                // tips' side across the second axis is the string side and the apex is the other way
                float tipSide = 0; int nt = 0;
                foreach (var p in pts) { var d = p - mean; float u = Vector3.Dot(d, a1); if (Mathf.Abs(u) > 0.42f * span) { tipSide += Vector3.Dot(d, a2); nt++; } }
                across = a2 * (nt > 0 && tipSide > 0 ? -1 : 1);
            }
            else
            {
                float up = 0, dn = 0;
                foreach (var p in pts) { float v = Vector3.Dot(p - mean, a2); up = Mathf.Max(up, v); dn = Mathf.Max(dn, -v); }
                across = a2 * (up > dn ? -1 : 1);          // points toward the flat top
            }
            across = q * across; across -= target * Vector3.Dot(across, target);
            var want = kind == "axe" ? Vector3.right : kind == "bow" ? Vector3.forward : Vector3.up;
            if (across.sqrMagnitude > 1e-8f)
            {
                across.Normalize();
                float ang = Mathf.Atan2(Vector3.Dot(Vector3.Cross(across, want), target), Vector3.Dot(across, want));
                q = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, target) * q;
            }
            var wrap = new GameObject("wrap").transform; wrap.SetParent(root, false);
            var inner = new GameObject("inner").transform; inner.SetParent(wrap, false);
            m.transform.SetParent(inner, false); m.transform.localPosition = -mean;
            inner.localRotation = q;
            // the axe hangs from its pommel (the haft's thin end at the origin); gun and blade stay centred on their grip point
            if (kind == "axe") inner.localPosition = new Vector3(0, heavy == 1 ? -lo : hi, 0);
            wrap.localScale = Vector3.one / Mathf.Max(1e-6f, span);
            if (GUN_ROLL.TryGetValue(pid, out var roll)) wrap.localRotation = Quaternion.Euler(0, 0, roll);
            if (kind == "chaingun")
            {
                // the grip: a quarter of the way along from the back, the receiver sitting just above the fist
                var b = new Bounds(); bool first = true;
                foreach (var p in pts) { var w = wrap.TransformPoint(inner.TransformPoint(p - mean)); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
                var off = new Vector3(-(b.min.x + b.max.x) / 2, -(b.min.y + (b.max.y - b.min.y) * 0.3f), -(b.min.z + (b.max.z - b.min.z) * 0.22f));
                wrap.localPosition = off;
            }
            return $"long axis {a1.ToString("F2")}, span {span:0.00}, heavy end {(heavy == 1 ? "hi" : "lo")} ({ends[0]:0.00} / {ends[1]:0.00})";
        }
    }
}
