// Grand Dohyo's binding chains (TS render/ChainCage.ts, ported; Gantetsu's ult, after Overwatch's Cage Fight - where a
// chain leashes everyone in the cage to the device at its centre): a holographic chain rides round the ring at the rope,
// and from the stake in the middle of the ring a chain runs to every enemy the ring holds, ending in a shackle looped
// round them. The chains pay out from the stake when the ring is stamped, sag while their hero stands close and pull
// taut at the rope.
//
// One instanced draw does every chain: the unit is the Tripo link model (prop_chain_link: three interlocking rope-cast
// links) laid along +Z, tiled end to end. Holographic like the spirit dragons (ZU/ChainHolo): a semi-opaque body in the
// ring's colours with a bright rim, scanlines and light running along the links.
// (The TS parked one unit under the world so WebGL compiled the program before the first ult; Unity compiles the shader
// with the scene, so nothing is parked here.)
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class ChainCage
    {
        /// <summary>chain units drawn at once (a ring is ~65, a tether ~12 with its shackle)</summary>
        const int MAX = 360;
        /// <summary>metres of chain one unit covers</summary>
        const float UNIT = 0.9f;
        /// <summary>the stake the chains are made fast to: height of its head above the ring</summary>
        const float STAKE_H = 1.5f;
        static readonly Vector3 Z = Vector3.forward;

        readonly Transform parent;
        readonly List<InstancedBatch> units = new List<InstancedBatch>();
        readonly Material mat = AbilityKit.Chain();
        readonly Material stakeMat = AbilityKit.Chain();
        readonly Dictionary<int, GameObject> stakes = new Dictionary<int, GameObject>();
        static Mesh postMesh, headMesh, footMesh;

        public ChainCage(Transform parent)
        {
            this.parent = parent;
            mat.renderQueue = stakeMat.renderQueue = 3006;          // TS renderOrder 6
            Load();
        }

        /// <summary>the stand-in when the Tripo link is missing: two interlocking oval links along +Z</summary>
        static Mesh PlainLinks()
        {
            var b = new AbilityKit.MeshBuilder();
            for (int k = 0; k < 2; k++)
            {
                var g = AbilityKit.Torus(0.17f, 0.05f, 8, 18);
                // an oval, long axis along Z; every other link turned a quarter
                var xf = Matrix4x4.Translate(new Vector3(0, 0, (k - 0.5f) * UNIT * 0.5f))
                       * (k == 1 ? Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.forward)) : Matrix4x4.identity)
                       * Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.right)) * Matrix4x4.Scale(new Vector3(1, 1.55f, 1));
                b.Append(g, xf);
            }
            return b.Build("chain links (stand-in)");
        }

        /// <summary>the Tripo link model: laid along +Z, one unit long (a little over, so neighbouring units' end links interlock)</summary>
        void Load()
        {
            var p = PropParts.Load("prop_chain_link");
            if (p == null) { units.Add(new InstancedBatch(PlainLinks(), mat, MAX, strength: true)); return; }
            var size = p.bounds.size; var c = p.bounds.center;
            // the chain's run is its longest side
            var rot = Matrix4x4.identity;
            if (size.x >= size.y && size.x >= size.z) rot = Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.up));
            else if (size.y >= size.x && size.y >= size.z) rot = Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.right));
            float len = Mathf.Max(size.x, Mathf.Max(size.y, size.z)), s = UNIT * 1.12f / Mathf.Max(1e-4f, len);
            p.Normalise(Matrix4x4.Scale(Vector3.one * s) * rot * Matrix4x4.Translate(-c));
            foreach (var part in p.parts) units.Add(new InstancedBatch(part.mesh, mat, MAX, strength: true) { pre = part.pre });
            if (p.map != null) { mat.SetTexture("_Map", p.map); mat.SetFloat("_HasMap", 1); }
        }

        /// <summary>one chain unit at `p`, its run along `dir`, rolled `roll` about it, shown at strength `k` (sim space)</summary>
        void Put(Vector3 p, Vector3 dir, float roll, float k)
        {
            var q = Quaternion.FromToRotation(Z, dir) * Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Z);
            var m = Sp.TRS(p, q, Vector3.one);
            foreach (var u in units) u.Add(m, k);
        }

        /// <summary>units along a path of points (evenly `UNIT` apart along it), each shown at `k`</summary>
        void Run(List<Vector3> pts, float k, float roll0 = 0)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var P = (pts[i] + pts[i + 1]) * 0.5f;
                var D = pts[i + 1] - pts[i];
                if (D.sqrMagnitude < 1e-6f) continue;
                Put(P, D.normalized, roll0 + i * 0.35f, k);
            }
        }

        GameObject Stake(Zone z)
        {
            if (stakes.TryGetValue(z.id, out var g)) return g;
            postMesh ??= AbilityKit.Cylinder(0.11f, 0.2f, STAKE_H, 12).Build("stake post");
            headMesh ??= AbilityKit.Torus(0.34f, 0.07f, 8, 24).Build("stake head");
            footMesh ??= AbilityKit.Torus(0.7f, 0.05f, 6, 32).Build("stake foot");
            g = new GameObject("dohyo stake");
            g.transform.SetParent(parent, false);
            void Part(Mesh mesh, float y, bool flat)
            {
                var o = new GameObject("part"); o.transform.SetParent(g.transform, false);
                o.transform.localPosition = new Vector3(0, y, 0);
                if (flat) o.transform.localRotation = Quaternion.AngleAxis(90, Vector3.right);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = o.AddComponent<MeshRenderer>(); mr.sharedMaterial = stakeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            Part(postMesh, STAKE_H / 2, false); Part(headMesh, STAKE_H, true); Part(footMesh, 0.1f, true);
            g.transform.position = Sp.U(Sp.V(z.x, z.y, z.z));
            stakes[z.id] = g;
            return g;
        }

        /// <summary>the ids the ring holds (Abilities: zone data "trapped", a list of actor ids)</summary>
        static IEnumerable<int> Trapped(Zone z)
        {
            if (z.data == null || !z.data.TryGetValue("trapped", out var o) || o == null) yield break;
            if (o is IEnumerable<int> ids) { foreach (var i in ids) yield return i; yield break; }
            if (o is IEnumerable e) foreach (var x in e) yield return Convert.ToInt32(x);
        }

        readonly HashSet<int> live = new HashSet<int>();
        readonly List<Vector3> ring = new List<Vector3>(), pts = new List<Vector3>(), sh = new List<Vector3>();
        readonly List<int> gone = new List<int>();

        public void Update(World w, float now)
        {
            mat.SetFloat("_T", now); stakeMat.SetFloat("_T", now);
            foreach (var u in units) u.Clear();
            live.Clear();
            foreach (var z in w.zones)
            {
                if (z.kind != "dohyo" || now >= z.until) continue;
                live.Add(z.id);
                float left = (float)(z.until - now), grow = Mathf.Min(1, (float)(now - z.born) / 0.45f);
                float k = left < 0.5f ? (Mathf.Sin(now * 60) > 0 ? left * 2 : 0.25f) : 1;      // flickers out with the wall
                var st = Stake(z); st.transform.localScale = new Vector3(1, Mathf.Max(0.01f, grow), 1); st.SetActive(true);
                float zx = (float)z.x, zy = (float)z.y, zz = (float)z.z, zr = (float)z.r;
                // ---- the chain round the ring, at the rope: it crawls slowly round and hangs in swags between the four tassels
                int N = Mathf.CeilToInt(2 * Mathf.PI * zr / UNIT), shown = Mathf.CeilToInt(N * grow); float a0 = now * 0.12f;
                ring.Clear();
                for (int i = 0; i <= shown; i++)
                {
                    float a = a0 + (float)i / N * Mathf.PI * 2;
                    ring.Add(new Vector3(zx + Mathf.Cos(a) * zr, zy + 1.25f + 0.22f * Mathf.Cos((a - a0) * 4 + Mathf.PI), zz + Mathf.Sin(a) * zr));
                }
                Run(ring, k);
                // ---- a chain from the stake to everyone the ring holds
                var head = new Vector3(zx, zy + STAKE_H * grow, zz);
                foreach (int id in Trapped(z))
                {
                    var x = w.ById(id);
                    if (x == null || !x.alive || !x.Has("chained", now)) continue;
                    // summons (Hex's puppet army) are bound like anyone, but drawn without a chain: fifty of them would use up
                    // the chain before the heroes had theirs
                    if (x.IsSummon) continue;
                    var c = Sp.V(x.Center); var end = c; float reach = Vector3.Distance(head, end);
                    // slack: the chain is as long as the ring is wide, so it sags while its hero stands close and is taut at the rope
                    float sag = Mathf.Max(0.08f, Mathf.Min(1.3f, (zr + 0.6f - reach) * 0.22f));
                    int n = Mathf.Max(2, Mathf.CeilToInt(reach / UNIT)), upTo = Mathf.CeilToInt(n * grow);
                    pts.Clear();
                    for (int i = 0; i <= upTo; i++)
                    {
                        float s = (float)i / n; var p = Vector3.LerpUnclamped(head, end, s);
                        p.y = Mathf.Max(zy + 0.14f, p.y - sag * 4 * s * (1 - s) + 0.03f * Mathf.Sin(now * 9 + i * 1.7f + id));   // a live, rattling chain
                        pts.Add(p);
                    }
                    Run(pts, k, id);
                    if (grow < 1) continue;
                    // the shackle: the chain looped once round them
                    // TS-PARITY: x.radius already includes the scale, so a scaled hero's shackle is scaled twice
                    float rr = (float)(x.Radius * x.scale) + 0.2f; int loops = Mathf.Max(4, Mathf.CeilToInt(2 * Mathf.PI * rr / UNIT));
                    sh.Clear();
                    for (int i = 0; i <= loops; i++) { float a = (float)i / loops * Mathf.PI * 2 + now * 0.8f; sh.Add(new Vector3(c.x + Mathf.Cos(a) * rr, c.y - 0.05f + 0.06f * Mathf.Sin(a * 2), c.z + Mathf.Sin(a) * rr)); }
                    Run(sh, k, id + 1);
                }
            }
            foreach (var u in units) u.Draw();
            gone.Clear();
            foreach (var kv in stakes) if (!live.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { UnityEngine.Object.Destroy(stakes[id]); stakes.Remove(id); }
        }

        public void Dispose()
        {
            foreach (var g in stakes.Values) UnityEngine.Object.Destroy(g);
            stakes.Clear();
            UnityEngine.Object.Destroy(mat); UnityEngine.Object.Destroy(stakeMat);
        }
    }
}
