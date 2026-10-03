// Ground dressing (user, 2026-10-03: "code the map floor grass beds, rock beds, etc on Tripo and refine them on Blender"):
// the Tripo grass / rock beds, bushes, boulders and rubble of the map's biome (TripoEnv kind "dressing") scattered by a seed
// over the map's ground floors and drawn GPU-instanced (one call per mesh part and LOD, LOD by distance, culled past the
// draw distance). Render-only - nothing collides with it, so a piece taller than ~0.35 m (`tall`) stands only at a wall's
// foot or by the play space's edge, never in an open lane; the capture point, packs, pads, spawns, the payload route, props
// and everything built on the floor stay clear. Video "Ground Detail" (off | low | high) sets density and draw distance.
// A Unity edition extra (the PC game's floors are bare); nothing is drawn until the biome's pieces are imported.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public sealed class GroundDressing : MonoBehaviour
    {
        /// <summary>the dressing biome of a map</summary>
        public static string Biome(string mapId)
        {
            switch (mapId)
            {
                case "mile": case "gulch": return "desert";
                case "foundry": case "hangar": case "kurogane": return "urban";
                case "cathedral": case "rift": return "gothic";
                case "starfall": return "alpine";
                default: return "temperate";
            }
        }

        // one mesh part of one LOD: its mesh, submesh and (instancing) material, and its place inside the prefab
        sealed class Part { public Mesh mesh; public int sub; public Material mat; public Matrix4x4 local; }
        sealed class Kind
        {
            public TripoEnv.Piece piece;
            public List<Part>[] lods = { new List<Part>(), new List<Part>(), new List<Part>() };
            public readonly List<Matrix4x4> at = new List<Matrix4x4>();      // the instances (world, scale = real size)
            public readonly List<Vector3> pos = new List<Vector3>();
        }

        const float Lod1 = 18, Lod2 = 45;           // metres from the camera
        readonly List<Kind> kinds = new List<Kind>();
        float far;
        readonly Matrix4x4[] buf = new Matrix4x4[1023];
        static readonly Dictionary<Material, Material> instMats = new Dictionary<Material, Material>();

        /// <summary>scatter the biome's pieces over the map's ground (nothing when the option is off or none are imported)</summary>
        public static GroundDressing Build(MapDef map, Transform parent)
        {
            string detail = UI.Toolkit.ZuSettings.Current.video.groundDetail;
            if (detail == "off") return null;
            var pieces = TripoEnv.Dressing(Biome(map.id));
            if (pieces.Count == 0) return null;
            var go = new GameObject("Ground Dressing");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GroundDressing>();
            bool high = detail != "low";
            g.far = high ? 110 : 60;
            foreach (var p in pieces) g.kinds.Add(Parts(p));
            g.Scatter(map, high ? 1f : 0.45f);
            return g;
        }

        static Kind Parts(TripoEnv.Piece p)
        {
            var k = new Kind { piece = p };
            var root = p.prefab.transform.worldToLocalMatrix;
            foreach (var mf in p.prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null) continue;
                // the props pipeline names the meshes <id>_LOD0 / _LOD1 / _LOD2; one without a suffix serves every LOD
                string n = mf.name + " " + mf.sharedMesh.name;
                int lod = n.Contains("_LOD2") ? 2 : n.Contains("_LOD1") ? 1 : n.Contains("_LOD0") ? 0 : -1;
                var local = root * mf.transform.localToWorldMatrix;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var src = r.sharedMaterials.Length > s ? r.sharedMaterials[s] : r.sharedMaterial;
                    if (src == null) continue;
                    var part = new Part { mesh = mf.sharedMesh, sub = s, mat = Instanced(src), local = local };
                    if (lod < 0) for (int l = 0; l < 3; l++) k.lods[l].Add(part);
                    else k.lods[lod].Add(part);
                }
            }
            // a missing LOD takes the next finer one
            for (int l = 1; l < 3; l++) if (k.lods[l].Count == 0) k.lods[l] = k.lods[l - 1];
            return k;
        }

        static Material Instanced(Material src)
        {
            if (src.enableInstancing) return src;
            if (!instMats.TryGetValue(src, out var m)) instMats[src] = m = new Material(src) { name = src.name + " (instanced)", enableInstancing = true };
            return m;
        }

        // ------------------------------------------------------------------------------------------------ the scatter
        /// <summary>seeded jittered-grid scatter over every ground floor (sim / TS coordinates until the instance is made):
        /// patches by a noise mask, flat beds anywhere clear, tall pieces only at wall feet and by the edge</summary>
        void Scatter(MapDef map, float density)
        {
            var rng = new System.Random(Seed(map.id));
            var floors = map.floors ?? new List<Box>();
            var boxes = new List<Box>(); if (map.boxes != null) boxes.AddRange(map.boxes); if (map.decor != null) boxes.AddRange(map.decor);
            double X = map.size[0], Z = map.size[1];
            var flat = kinds.FindAll(k => !k.piece.tall); var tall = kinds.FindAll(k => k.piece.tall);
            const double cell = 2.6;
            for (int fi = 0; fi < floors.Count; fi++)
            {
                var f = floors[fi];
                if ((f.mat ?? "ground") != "ground") continue;
                double top = (f.y ?? 0) + 0.01;
                for (double gx = f.x - f.w / 2 + cell / 2; gx < f.x + f.w / 2; gx += cell)
                    for (double gz = f.z - f.d / 2 + cell / 2; gz < f.z + f.d / 2; gz += cell)
                    {
                        double x = gx + (rng.NextDouble() - 0.5) * cell, z = gz + (rng.NextDouble() - 0.5) * cell;
                        double roll = rng.NextDouble(), yaw = rng.NextDouble() * 360, s = 0.75 + rng.NextDouble() * 0.55; int pick = rng.Next(1 << 20);
                        // patches: the noise mask makes clumps and bare ground between them
                        float patch = Mathf.PerlinNoise((float)x / 9f + 3.1f, (float)z / 9f - 7.7f);
                        if (roll > density * Mathf.SmoothStep(0, 1, (patch - 0.35f) / 0.4f)) continue;
                        if (!Inside(f, x, z, 0.2) || Covered(floors, fi, top, x, z) || !Clear(map, boxes, top, x, z)) continue;
                        bool edge = System.Math.Abs(x) > X - 2.5 || System.Math.Abs(z) > Z - 2.5 || WallFoot(boxes, top, x, z);
                        var from = edge && tall.Count > 0 && (pick & 3) == 0 ? tall : flat;
                        if (from.Count == 0) continue;
                        var k = from[(pick >> 2) % from.Count];
                        float h = k.piece.height_m * (float)s;
                        var p = Conv.U(x, top, z);
                        k.at.Add(Matrix4x4.TRS(p, Quaternion.Euler(0, (float)yaw, 0), Vector3.one * h));
                        k.pos.Add(p);
                    }
            }
        }

        static int Seed(string id) { unchecked { int h = (int)2166136261; foreach (char c in id) h = (h ^ c) * 16777619; return h ^ 0x2d1e55; } }
        static bool Inside(Box f, double x, double z, double m) => System.Math.Abs(x - f.x) < f.w / 2 - m && System.Math.Abs(z - f.z) < f.d / 2 - m;

        /// <summary>another floor over this spot (a deck, a quay, a later ground floor at the same height)</summary>
        static bool Covered(List<Box> floors, int fi, double top, double x, double z)
        {
            for (int i = 0; i < floors.Count; i++)
            {
                if (i == fi) continue;
                var o = floors[i]; double ot = (o.y ?? 0) + 0.01;
                if (!Inside(o, x, z, -0.2)) continue;
                if (ot > top + 0.02 || (System.Math.Abs(ot - top) <= 0.02 && i < fi)) return true;
            }
            return false;
        }

        /// <summary>not on anything built on the floor, nor on the gameplay spots</summary>
        static bool Clear(MapDef map, List<Box> boxes, double top, double x, double z)
        {
            foreach (var b in boxes)
            {
                double y0 = b.y ?? 0;
                if (y0 > top + 2 || y0 + b.h < top - 0.05) continue;          // a bridge overhead, or below the floor
                if (System.Math.Abs(x - b.x) < b.w / 2 + 0.3 && System.Math.Abs(z - b.z) < b.d / 2 + 0.3) return false;
            }
            if (map.point != null && map.point.Length >= 3 && Near(x, z, map.point[0], map.point[2], 7.5)) return false;
            if (map.packs != null) foreach (var p in map.packs) if (Near(x, z, p.x, p.z, 1.8)) return false;
            if (map.pads != null) foreach (var p in map.pads) if (Near(x, z, p.x, p.z, 2.5)) return false;
            if (map.spawns != null) foreach (var s in map.spawns.Values) if (s != null && s.Length >= 2 && Near(x, z, s[0], s[1], 6)) return false;
            if (map.props != null) foreach (var p in map.props) if (Near(x, z, p.x, p.z, System.Math.Max(1.2, (p.s ?? 1) * 0.6))) return false;
            if (map.path != null)
                for (int i = 0; i + 1 < map.path.Count; i++)
                {
                    var a = map.path[i]; var c = map.path[i + 1];
                    if (a.Length >= 2 && c.Length >= 2 && SegDist(x, z, a[0], a[a.Length - 1], c[0], c[c.Length - 1]) < 3.5) return false;
                }
            return true;
        }

        /// <summary>at the foot of a wall: 0.3 - 1.4 m off a box standing on this floor that is at least 1.5 m tall</summary>
        static bool WallFoot(List<Box> boxes, double top, double x, double z)
        {
            foreach (var b in boxes)
            {
                double y0 = b.y ?? 0;
                if (System.Math.Abs(y0 - top) > 0.3 || b.h < 1.5) continue;
                double dx = System.Math.Max(0, System.Math.Abs(x - b.x) - b.w / 2), dz = System.Math.Max(0, System.Math.Abs(z - b.z) - b.d / 2);
                double d = System.Math.Sqrt(dx * dx + dz * dz);
                if (d > 0.3 && d < 1.4) return true;
            }
            return false;
        }

        static bool Near(double x, double z, double px, double pz, double r) => (x - px) * (x - px) + (z - pz) * (z - pz) < r * r;
        static double SegDist(double x, double z, double ax, double az, double bx, double bz)
        {
            double vx = bx - ax, vz = bz - az, l = vx * vx + vz * vz;
            double t = l < 1e-9 ? 0 : System.Math.Max(0, System.Math.Min(1, ((x - ax) * vx + (z - az) * vz) / l));
            double dx = x - (ax + vx * t), dz = z - (az + vz * t);
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------------------------------------ drawing
        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;
            float f2 = far * far, l1 = Lod1 * Lod1, l2 = Lod2 * Lod2;
            foreach (var k in kinds)
                for (int lod = 0; lod < 3; lod++)
                {
                    int n = 0;
                    for (int i = 0; i < k.at.Count; i++)
                    {
                        float d2 = (k.pos[i] - eye).sqrMagnitude;
                        if (d2 > f2) continue;
                        int want = d2 < l1 ? 0 : d2 < l2 ? 1 : 2;
                        if (want != lod) continue;
                        buf[n++] = k.at[i];
                        if (n == buf.Length) { Draw(k, lod, n); n = 0; }
                    }
                    if (n > 0) Draw(k, lod, n);
                }
        }

        readonly Matrix4x4[] tmp = new Matrix4x4[1023];
        void Draw(Kind k, int lod, int n)
        {
            foreach (var part in k.lods[lod])
            {
                for (int i = 0; i < n; i++) tmp[i] = buf[i] * part.local;
                var rp = new RenderParams(part.mat)
                {
                    shadowCastingMode = k.piece.tall ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    worldBounds = new Bounds(transform.position, Vector3.one * 2000),
                };
                Graphics.RenderMeshInstanced(rp, part.mesh, part.sub, tmp, n);
            }
        }
    }
}
