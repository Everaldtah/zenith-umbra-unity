// Overwatch-style first-person model (port of FirstPerson.ts armsOnly): keep only the triangles skinned (mostly) to the arm
// chain - hands, forearms, sleeves and whatever the hands carry. Torso, collar, shoulder armour, hair and legs drop out of
// the viewmodel, so a high collar or a big pauldron can't fill the screen. Built once per hero from the prefab's skinned
// mesh and cached; the world view keeps the shared mesh.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZU.Game.FirstPerson
{
    public static class ArmsMesh
    {
        static readonly Regex ARM = new Regex(@"^(upperarm|forearm|hand|(thumb|index|middle|ring|pinky)\d)_[LR]$");
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public struct Stats { public int kept, total; }

        /// <summary>the arms-only copy of `smr.sharedMesh` for this hero (keep / drape / squeeze as the style says), from the cache</summary>
        public static Mesh For(string key, SkinnedMeshRenderer smr, float keep, float drape, float squeeze, out Stats stats)
        {
            stats = default;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Build(smr, keep, drape, squeeze, out stats);
            m.name = key + "_arms";
            cache[key] = m;
            return m;
        }

        public static Mesh Build(SkinnedMeshRenderer smr, float keep, float drape, float squeeze, out Stats stats)
        {
            var src = smr.sharedMesh;
            var bones = smr.bones;
            int nb = bones.Length;
            var arm = new bool[nb];
            for (int i = 0; i < nb; i++) arm[i] = bones[i] != null && ARM.IsMatch(bones[i].name);
            var bw = src.boneWeights;                       // 4 influences per vertex (Tripo rigs)
            int n = src.vertexCount;
            var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = bw[i]; float s = 0;
                if (arm[b.boneIndex0]) s += b.weight0; if (arm[b.boneIndex1]) s += b.weight1;
                if (arm[b.boneIndex2]) s += b.weight2; if (arm[b.boneIndex3]) s += b.weight3;
                w[i] = s;
            }
            // drape: each vertex's distance (bind space) from the segment of its heaviest arm bone; far = cloth hanging off the arm.
            // squeeze: a wide sleeve pulled in to a slim tube around the bone
            var far = new bool[n];
            Vector3[] pos = src.vertices;
            bool moved = false;
            if (drape > 0 || squeeze > 0)
            {
                var binds = src.bindposes;
                int Idx(string name) { for (int i = 0; i < nb; i++) if (bones[i] != null && bones[i].name == name) return i; return -1; }
                Vector3? At(int i) => i < 0 ? (Vector3?)null : (Vector3)binds[i].inverse.GetColumn(3);     // the bone's bind position in mesh space
                var seg = new Dictionary<int, (Vector3 a, Vector3 b)>();
                float limit = 0, limitS = 0;
                foreach (var S in new[] { "L", "R" })
                {
                    int iu = Idx("upperarm_" + S), ifa = Idx("forearm_" + S), ih = Idx("hand_" + S);
                    var ua = At(iu); var fa = At(ifa); var hd = At(ih);
                    if (ua == null || fa == null || hd == null) continue;
                    var tip = hd.Value + (hd.Value - fa.Value) * 0.45f;
                    seg[iu] = (ua.Value, fa.Value); seg[ifa] = (fa.Value, hd.Value); seg[ih] = (hd.Value, tip);
                    float fl = Vector3.Distance(fa.Value, hd.Value);
                    limit = Mathf.Max(limit, fl * drape); limitS = Mathf.Max(limitS, fl * squeeze);
                }
                if (limit > 0 || limitS > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var b = bw[i]; int best = -1; float bwt = 0;
                        void Try(int bi, float x) { if (seg.ContainsKey(bi) && x > bwt) { bwt = x; best = bi; } }
                        Try(b.boneIndex0, b.weight0); Try(b.boneIndex1, b.weight1); Try(b.boneIndex2, b.weight2); Try(b.boneIndex3, b.weight3);
                        if (best < 0) continue;
                        var (a, bb) = seg[best];
                        var p = pos[i]; var ab = bb - a;
                        float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
                        var c = a + ab * u;
                        float d = Vector3.Distance(p, c);
                        if (limit > 0 && d > limit) far[i] = true;
                        if (limitS > 0 && d > limitS) { pos[i] = c + (p - c) * (limitS / d); moved = true; }
                    }
                }
            }
            // triangles: a copy of the mesh with only the arm triangles, per submesh (the materials stay aligned)
            var mesh = Object.Instantiate(src);
            int total = 0, kept = 0;
            var subs = new List<int[]>();
            for (int s = 0; i_sub(s, src); s++)
            {
                var tri = src.GetTriangles(s);
                var outp = new List<int>(tri.Length);
                for (int t = 0; t + 2 < tri.Length; t += 3)
                {
                    int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                    if (w[i0] + w[i1] + w[i2] >= keep * 3 && !(far[i0] || far[i1] || far[i2])) { outp.Add(i0); outp.Add(i1); outp.Add(i2); }
                }
                total += tri.Length / 3; kept += outp.Count / 3;
                subs.Add(outp.ToArray());
            }
            if (moved) mesh.vertices = pos;
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s, false);
            if (moved) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // the arms wave around the camera: a generous local bound so the overlay camera never culls them
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 2);
            stats = new Stats { kept = kept, total = total };
            return mesh;
        }
        static bool i_sub(int s, Mesh m) => s < m.subMeshCount;
    }
}
