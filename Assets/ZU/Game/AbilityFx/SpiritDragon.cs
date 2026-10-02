// Spirit koi-dragons for the Koryu ultimates (TS render/SpiritDragon.ts, ported), built the way Overwatch builds Hanzo's
// and Genji's:
//  - Seiran's Twin Koi Torrent (Dragonstrike): a summoning sigil opens on the aim line and two blue koi-dragons pour out
//    of it, twisting round each other in a double helix as they swim 45 m straight through the level, then dissolve.
//  - Hayate's Dragon Gate Blade (Dragonblade): as the nodachi is drawn a violet koi-dragon coils up around him and pours
//    into the blade; every cut of the ult then streaks the dragon through its target along the cut.
// The dragons are Tripo models rigged by assetgen/blender/rig_dragon.py: a straight rest spine (head toward +Z) skinned to
// a flat chain of joints (sp00 = the snout). Each frame every joint is laid on the path the head has already swum, at its
// own rest distance behind the head (follow-the-leader: the body pours along the spiral like water through a pipe
// instead of sliding as a rigid mesh), with a travelling swim wave and a corkscrew roll on top. Joints still "behind" the
// start of the path collapse to a point, so the dragons emerge out of the sigil / the blade.
//
// Unity: the TS bound the skeleton with an identity bind, so a joint's skinning matrix is just "where its slice of the
// body goes" (M below). Here the dragon mesh is baked once into that same rest frame (Bake) with its joint indices and
// weights in UV2 / UV3, and ZU/DragonHolo skins it with the M matrices directly (_Bones) - no Transform per joint, no
// SkinnedMeshRenderer. The model must be imported Read/Write (the bake reads its vertices). The paths and frames are
// computed in sim space exactly as the TS does and cross into Unity only in the matrices (see Sp).
using System;
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class SpiritDragons
    {
        /// <summary>centreline of a swim at path distance u (metres from where the dragon emerges), t seconds after it was
        /// summoned, in sim space</summary>
        public delegate Vector3 Path(float u, float t);
        /// <summary>mist shed from a swimming dragon: Unity position, colour, big (the snout) or not</summary>
        public delegate void Emit(Vector3 p, Color color, bool big);

        public const int MAX_JOINTS = 48;                // ZU/DragonHolo's _Bones
        public static readonly Dictionary<string, string> DRAGON_MODEL = new Dictionary<string, string> { ["seiran"] = "seiran_dragon", ["hayate"] = "hayate_dragon" };
        static readonly string[] COL_SEIRAN = { "#4f9dff", "#8fd0ff" }, COL_HAYATE = { "#b36bff" };

        /// <summary>a dragon model baked into its rest frame (shared by every swim of that model)</summary>
        sealed class Baked { public Mesh mesh; public float[] restZ; public float headZ, len; public Texture map; }
        sealed class Rig
        {
            public string id; public GameObject go; public MeshRenderer mr; public Baked b;
            public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public readonly Matrix4x4[] bones = new Matrix4x4[MAX_JOINTS];
            public float seed;
        }
        sealed class Swim
        {
            public Rig rig; public Path path; public float born, dur, delay, speed, lead, scale;
            /// <summary>cross-section relative to length: a coiling Dragonblade dragon is slender, Dragonstrike's twins are massive</summary>
            public float girth;
            public float wave, roll, phase; public Color color; public float lastEmit;
        }
        sealed class Sigil { public GameObject obj; public float born, dur, size; public Material mat; public Transform[] parts; }

        public readonly Transform group;
        readonly Material mat;
        readonly Dictionary<string, Baked> baked = new Dictionary<string, Baked>();
        readonly Dictionary<string, List<Rig>> free = new Dictionary<string, List<Rig>>();
        List<Swim> swims = new List<Swim>();
        List<Sigil> sigils = new List<Sigil>();
        readonly List<Vector3> P = new List<Vector3>(), T = new List<Vector3>();
        readonly List<float> sq = new List<float>();
        static readonly Vector3 UP = Vector3.up, XAX = Vector3.right;

        public SpiritDragons(Transform parent)
        {
            group = new GameObject("spirit dragons").transform; group.SetParent(parent, false);
            mat = AbilityKit.Dragon();
            mat.renderQueue = 3005;                                           // TS renderOrder 5
            foreach (var id in DRAGON_MODEL.Values) Bake(id);                  // warm: bake both models at match start
        }

        // ------------------------------------------------------------------ rigs
        /// <summary>where a dragon model lives: the hero library (HeroImport: Art/Heroes/<id>/<id>.prefab, a generic rig, the
        /// mesh Read/Write), else Resources/ZUProps</summary>
        public static GameObject DragonPrefab(string id)
        {
            var e = HeroLibrary.Get()?.Find(id);
            if (e != null) return e.prefab;
            return Resources.Load<GameObject>("ZUProps/" + id);
        }

        Baked Bake(string id)
        {
            if (baked.TryGetValue(id, out var have)) return have;
            baked[id] = null;                                                  // (tried: a missing model is not retried every cast)
            var prefab = DragonPrefab(id);
            if (prefab == null) return null;
            // under an inactive holder: nothing on the prefab wakes up (HeroImport puts the hair / cloth solver on its prefabs)
            var holder = new GameObject("dragon bake"); holder.SetActive(false);
            var inst = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            try
            {
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var src = smr != null ? smr.sharedMesh : null;
                if (src == null) return null;
                if (!src.isReadable) { Debug.LogWarning($"[ZU] {id}: the dragon mesh needs Read/Write enabled on import (SpiritDragons bakes it)"); return null; }
                var bones = smr.bones; var bind = src.bindposes;
                if (bones.Length == 0 || bind.Length != bones.Length) return null;
                // the model's frame: the prefab root's (whatever axis conversion the import left on its children)
                var Q = inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var rest = new Vector3[bones.Length];
                for (int i = 0; i < bones.Length; i++) rest[i] = (Q * bind[i].inverse).GetColumn(3);
                // joints snout -> tail: rig_dragon.py names them sp00 (the snout) .. spNN; anything else falls back to the TS's
                // order (rest z, highest first)
                var order = new List<int>();
                for (int i = 0; i < bones.Length; i++) order.Add(i);
                bool named = true;
                int Num(int i) { var n = bones[i] != null ? bones[i].name : ""; return n.StartsWith("sp") && int.TryParse(n.Substring(2), out var k) ? k : int.MaxValue; }
                foreach (var i in order) if (Num(i) == int.MaxValue) named = false;
                if (named) order.Sort((a, c) => Num(a).CompareTo(Num(c)));
                else order.Sort((a, c) => rest[c].z.CompareTo(rest[a].z));
                if (order.Count > MAX_JOINTS) { Debug.LogWarning($"[ZU] {id}: {order.Count} joints, ZU/DragonHolo takes {MAX_JOINTS}"); return null; }
                var rank = new int[bones.Length];
                for (int k = 0; k < order.Count; k++) rank[order[k]] = k;
                // the rest frame the TS works in: spine along +Z with the snout toward +Z, up +Y, the spine through x = y = 0
                var axis = (rest[order[0]] - rest[order[order.Count - 1]]).normalized;
                if (axis.sqrMagnitude < 0.5f) axis = Vector3.forward;
                var F = Matrix4x4.Rotate(Quaternion.LookRotation(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right)).inverse;
                Vector3 mid = Vector3.zero;
                foreach (var r in rest) mid += F.MultiplyPoint3x4(r);
                mid /= rest.Length;
                var C = Matrix4x4.Translate(new Vector3(-mid.x, -mid.y, 0)) * F * Q;
                var b = new Baked { restZ = new float[order.Count] };
                for (int k = 0; k < order.Count; k++) b.restZ[k] = (C * bind[order[k]].inverse).GetColumn(3).z;
                b.headZ = float.NegativeInfinity; float tailZ = float.PositiveInfinity;
                foreach (var z in b.restZ) { b.headZ = Mathf.Max(b.headZ, z); tailZ = Mathf.Min(tailZ, z); }
                b.len = Mathf.Max(0.01f, b.headZ - tailZ);
                // the mesh in that frame, its joints renumbered snout -> tail
                var vs = src.vertices; var ns = src.normals; var weights = src.boneWeights;
                var bv = new Vector3[vs.Length]; var bn = new Vector3[vs.Length];
                var bi = new List<Vector4>(vs.Length); var bw = new List<Vector4>(vs.Length);
                for (int i = 0; i < vs.Length; i++)
                {
                    bv[i] = C.MultiplyPoint3x4(vs[i]);
                    bn[i] = ns.Length == vs.Length ? C.MultiplyVector(ns[i]).normalized : Vector3.up;
                    var w = i < weights.Length ? weights[i] : new BoneWeight { boneIndex0 = order[0], weight0 = 1 };
                    bi.Add(new Vector4(rank[w.boneIndex0], rank[w.boneIndex1], rank[w.boneIndex2], rank[w.boneIndex3]));
                    bw.Add(new Vector4(w.weight0, w.weight1, w.weight2, w.weight3));
                }
                var m = new Mesh { name = id + " (baked)", indexFormat = vs.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.vertices = bv; m.normals = bn; m.uv = src.uv;
                m.SetUVs(2, bi); m.SetUVs(3, bw);
                m.triangles = src.triangles;
                m.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);       // posed in the shader: never culled (TS frustumCulled = false)
                b.mesh = m;
                b.map = AbilityKit.MapOf(smr.sharedMaterial);
                return baked[id] = b;
            }
            finally { UnityEngine.Object.Destroy(holder); }
        }

        Rig GetRig(string id)
        {
            if (free.TryGetValue(id, out var pool) && pool.Count > 0) { var r0 = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); r0.go.SetActive(true); return r0; }
            var b = Bake(id);
            if (b == null) return null;
            var go = new GameObject(id); go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var rig = new Rig { id = id, go = go, mr = mr, b = b };
            rig.mpb.SetTexture("_BaseMap", b.map != null ? b.map : AbilityKit.White);
            rig.mpb.SetFloat("_Head", b.headZ); rig.mpb.SetFloat("_Len", b.len);
            return rig;
        }
        void Release(Rig r)
        {
            r.go.SetActive(false);
            if (!free.TryGetValue(r.id, out var pool)) free[r.id] = pool = new List<Rig>();
            pool.Add(r);
        }

        void Spawn(string id, float now, Swim o)
        {
            var rig = GetRig(id);
            if (rig == null) return;
            rig.seed = UnityEngine.Random.value * 100;
            var c = Sp.Lin(o.color);
            rig.mpb.SetVector("_Col", c); rig.mpb.SetVector("_Glow", Color.Lerp(c, Color.white, 0.3f));
            rig.mpb.SetFloat("_Seed", rig.seed);
            o.rig = rig; o.born = now; o.lastEmit = 0;
            swims.Add(o);
        }

        // ------------------------------------------------------------------ the three motions
        /// <summary>Twin Koi Torrent: sigil at `o`, two dragons in a double helix along o -> to (matches the ult's bite path)</summary>
        public void Twin(V3 o, V3 to, float now, float delay = 0.25f, float speed = 25)
        {
            var d = new Vector3((float)(to.x - o.x), 0, (float)(to.z - o.z)).normalized; var side = new Vector3(-d.z, 0, d.x);
            float R = (float)Abilities.TWIN_R, W = (float)Abilities.TWIN_W;   // helix radius and turn rate: the same sway the ult's bites follow
            var O = Sp.V(o);
            SigilAt(O, d, now, Sp.Hex(COL_SEIRAN[0]), 1.1f);
            float[] phases = { 0, Mathf.PI };
            for (int k = 0; k < 2; k++)
            {
                float ph = phases[k];
                Spawn(DRAGON_MODEL["seiran"], now, new Swim
                {
                    path = (u, _) => new Vector3(O.x + d.x * u + side.x * R * Mathf.Sin(W * u + ph), O.y + R * 0.7f * Mathf.Cos(W * u + ph), O.z + d.z * u + side.z * R * Mathf.Sin(W * u + ph)),
                    dur = delay + 68 / speed, delay = delay, speed = speed, lead = 2, scale = 2.2f, girth = 1.3f, wave = 0.22f, roll = 0.5f, phase = ph, color = Sp.Hex(COL_SEIRAN[k]),
                });
            }
        }
        /// <summary>Dragon Gate: the koi-dragon coils up around Hayate as the blade is drawn (follows him through the teleport cuts)</summary>
        public void Coil(Actor a, float now, bool firstPerson)
        {
            float r = firstPerson ? 1.8f : 1.45f;               // wide enough that the slender body wraps him, never through him
            Spawn(DRAGON_MODEL["hayate"], now, new Swim
            {
                path = (u, _) => new Vector3((float)a.pos.x + Mathf.Cos(u / r) * r, (float)a.pos.y + 0.1f + u * 0.14f, (float)a.pos.z + Mathf.Sin(u / r) * r),   // ground to over his head in ~1.2 turns/s
                dur = 1.2f, delay = 0, speed = 17, lead = 0, scale = 0.8f, girth = 0.4f, wave = 0.12f, roll = 0.35f, phase = 0, color = Sp.Hex(COL_HAYATE[0]),
            });
        }
        /// <summary>one Dragon Gate cut: the dragon streaks through the target along the cut, corkscrewing</summary>
        public void Streak(V3 from, V3 to, float now)
        {
            var F = Sp.V(from);
            var d = Sp.V(to) - F; float L = d.magnitude; if (L == 0) L = 1;
            d /= L;
            var s1 = Mathf.Abs(d.y) > 0.9f ? XAX : Vector3.Cross(UP, d).normalized; var s2 = Vector3.Cross(d, s1);
            Spawn(DRAGON_MODEL["hayate"], now, new Swim
            {
                path = (u, _) => F + d * u + s1 * (Mathf.Cos(u * 1.6f) * 0.45f) + s2 * (Mathf.Sin(u * 1.6f) * 0.45f),
                dur = 0.5f, delay = 0, speed = Mathf.Max(40, (L + 6) / 0.3f), lead = 0, scale = 0.55f, girth = 0.5f, wave = 0.1f, roll = 0.9f, phase = 0, color = Sp.Hex(COL_HAYATE[0]),
            });
        }

        // ------------------------------------------------------------------ the summoning sigil
        static Mesh[] SIGIL_RINGS; static Mesh SIGIL_PETAL;
        /// <summary>Dragonstrike's summoning sigil: concentric rings and a spinning eight-petal seal facing down the aim (sim space)</summary>
        void SigilAt(Vector3 o, Vector3 d, float now, Color c, float size = 1)
        {
            if (SIGIL_RINGS == null)
            {
                SIGIL_RINGS = new[] { AbilityKit.Ring(2.3f, 2.5f, 64).Build("sigil ring"), AbilityKit.Ring(1.55f, 1.65f, 64).Build("sigil ring"), AbilityKit.Ring(0.5f, 0.62f, 32).Build("sigil ring") };
                SIGIL_PETAL = AbilityKit.Plane(0.16f, 1.1f).Build("sigil petal");
            }
            // one material for the whole seal (every part fades together) on shared geometry
            var g = new GameObject("koi sigil");
            g.transform.SetParent(group, false);
            var m = AbilityKit.Additive(); m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0));
            var parts = new List<Transform>();
            Transform Mk(Mesh mesh)
            {
                var me = new GameObject("part"); me.transform.SetParent(g.transform, false);
                me.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = me.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                parts.Add(me.transform);
                return me.transform;
            }
            foreach (var ring in SIGIL_RINGS) Mk(ring);
            for (int k = 0; k < 8; k++)
            {
                var petal = Mk(SIGIL_PETAL);
                petal.localPosition = new Vector3(Mathf.Cos(k * Mathf.PI / 4) * 1.95f, Mathf.Sin(k * Mathf.PI / 4) * 1.95f, 0);
                petal.localRotation = Quaternion.AngleAxis((k * Mathf.PI / 4 + Mathf.PI / 2) * Mathf.Rad2Deg, Vector3.forward);
            }
            g.transform.localScale = Vector3.one * size;
            // three's lookAt turns an object's +Z at the target, as Unity's LookRotation does
            g.transform.SetPositionAndRotation(Sp.U(o), Quaternion.LookRotation(Sp.U(d)));
            sigils.Add(new Sigil { obj = g, born = now, dur = 1.5f, size = size, mat = m, parts = parts.ToArray() });
        }

        // ------------------------------------------------------------------ per frame
        public void Update(float now, Emit emit)
        {
            foreach (var sg in sigils)
            {
                float k = (now - sg.born) / sg.dur;
                sg.obj.transform.localScale = Vector3.one * (sg.size * (0.3f + 0.7f * Mathf.Min(1, k * 5)));
                // TS-PARITY: the spin is a fixed step a frame (faster at a higher frame rate), as in the TS
                for (int i = 0; i < sg.parts.Length; i++) sg.parts[i].localRotation *= Quaternion.AngleAxis((i % 2 == 1 ? -1 : 1) * 0.02f * Mathf.Rad2Deg, Vector3.forward);
                var c = sg.mat.GetColor("_BaseColor");
                c.a = Mathf.Min(1, k * 6) * (1 - Mathf.Max(0, (k - 0.55f) / 0.45f));
                sg.mat.SetColor("_BaseColor", c);
                if (k >= 1) { UnityEngine.Object.Destroy(sg.obj); UnityEngine.Object.Destroy(sg.mat); }   // geometry is shared (SIGIL_*)
            }
            sigils = sigils.FindAll(sg => now - sg.born < sg.dur);
            swims = swims.FindAll(s =>
            {
                float t = now - s.born;
                if (t >= s.dur) { Release(s.rig); return false; }
                Pose(s, t);
                float fade = Mathf.Min(1, t / 0.3f) * Mathf.Min(1, (s.dur - t) / 0.5f);   // materialise, then burn away
                s.rig.mpb.SetFloat("_Alpha", fade);
                s.rig.mpb.SetFloat("_T", now);
                s.rig.mr.SetPropertyBlock(s.rig.mpb);
                if (now - s.lastEmit > 0.035f && fade > 0.2f)      // mist shed from the snout and a random stretch of the body
                {
                    s.lastEmit = now;
                    int n = s.rig.b.restZ.Length;
                    emit(Sp.U(P[0]), s.color, true);
                    emit(Sp.U(P[1 + Mathf.FloorToInt(UnityEngine.Random.value * (n - 1))]), s.color, false);
                }
                return true;
            });
        }

        /// <summary>lay every joint on the swum path (follow-the-leader), add the swim wave, then orient each by its neighbours</summary>
        void Pose(Swim s, float t)
        {
            var r = s.rig.b; int n = r.restZ.Length; float sc = s.scale;
            float uh = Mathf.Max(0, t - s.delay) * s.speed + s.lead;
            while (P.Count < n) { P.Add(Vector3.zero); T.Add(Vector3.zero); sq.Add(1); }
            for (int i = 0; i < n; i++)
            {
                float back = (r.headZ - r.restZ[i]) * sc, u = uh - back;
                sq[i] = SmoothStep(u, -1.2f * sc, 0.4f * sc);                       // still inside the sigil / blade: collapsed
                P[i] = s.path(Mathf.Max(0, u), t);
            }
            // travelling swim wave (grows toward the tail) across the direction of travel
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Min(n - 1, i + 1), b = Mathf.Max(0, i - 1);
                var tmp = P[b] - P[a];
                if (tmp.sqrMagnitude < 1e-8f) tmp = new Vector3(0, 0, 1);
                T[i] = tmp.normalized;
            }
            for (int i = 0; i < n; i++)
            {
                float back = (r.headZ - r.restZ[i]) / r.len;
                float w = s.wave * sc * s.girth * (0.25f + 0.75f * back) * Mathf.Sin(back * 9.5f - t * 9 + s.phase) * sq[i];
                var nn = Vector3.Cross(Mathf.Abs(T[i].y) > 0.95f ? XAX : UP, T[i]).normalized;
                P[i] += nn * w;
            }
            var bounds = new Bounds(Sp.U(P[0]), Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Min(n - 1, i + 1), b = Mathf.Max(0, i - 1);
                var tmp = P[b] - P[a];
                if (tmp.sqrMagnitude > 1e-8f) T[i] = tmp.normalized;
                var Ti = T[i];
                var nn = Vector3.Cross(Mathf.Abs(Ti.y) > 0.95f ? XAX : UP, Ti).normalized;
                var bb = Vector3.Cross(Ti, nn);
                float roll = s.roll * Mathf.Sin((uh - (r.headZ - r.restZ[i]) * sc) * 0.36f + s.phase);
                float cr = Mathf.Cos(roll), sr = Mathf.Sin(roll);
                var nr = nn * cr + bb * sr;
                var br = -nn * sr + bb * cr;
                float q = sc * s.girth * Mathf.Max(0.001f, sq[i]), z = r.restZ[i];
                var Pi = P[i];
                // the TS matrix (sim space) has columns n q, b q, T sc and P - T sc z; mirrored into Unity (conjugated by the
                // X flip, which also mirrors the baked rest frame) it is -U(n) q, U(b) q, U(T) sc, U(P - T sc z)
                var c0 = -Sp.U(nr) * q; var c1 = Sp.U(br) * q; var c2 = Sp.U(Ti) * sc; var c3 = Sp.U(Pi - Ti * (sc * z));
                var M = new Matrix4x4(new Vector4(c0.x, c0.y, c0.z, 0), new Vector4(c1.x, c1.y, c1.z, 0), new Vector4(c2.x, c2.y, c2.z, 0), new Vector4(c3.x, c3.y, c3.z, 1));
                s.rig.bones[i] = M;
                bounds.Encapsulate(Sp.U(Pi));
            }
            bounds.Expand(sc * s.girth * 4 + 2);
            s.rig.mpb.SetMatrixArray("_Bones", s.rig.bones);
            s.rig.mr.bounds = bounds;
        }

        static float SmoothStep(float x, float min, float max)
        {
            if (x <= min) return 0;
            if (x >= max) return 1;
            x = (x - min) / (max - min);
            return x * x * (3 - 2 * x);
        }

        public void Dispose()
        {
            foreach (var sg in sigils) { UnityEngine.Object.Destroy(sg.obj); UnityEngine.Object.Destroy(sg.mat); }
            sigils.Clear(); swims.Clear();
            if (group != null) UnityEngine.Object.Destroy(group.gameObject);
            UnityEngine.Object.Destroy(mat);
        }
    }
}
