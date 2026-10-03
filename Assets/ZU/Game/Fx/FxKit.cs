// The effects primitives (the TS render/Fx.ts toolkit, ported): a pooled CPU particle system drawn as camera-facing glow
// sprites in one mesh (speed, life, size, gravity, spread, a direction, an upward kick - TS Particles.emit), shockwave
// rings, beams, cones, crescents, wedges and lightning bolts that grow / fade over their life, a flash light, and the
// camera shake budget. Everything is additive (ZU/FxAdditive); colours are HDR so the bloom catches them.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.Game.Fx
{
    public sealed class FxKit
    {
        public float Shake;                         // camera shake budget, decays (MatchCamera reads it)
        public float LodScale = 1;
        readonly Transform root;
        readonly Material add;
        /// <summary>the shared additive material (ZU/FxAdditive): tint per renderer with _BaseColor in a property block</summary>
        public Material Additive => add;
        /// <summary>the FX root (persistent effect objects parent here)</summary>
        public Transform Root => root;

        public FxKit(Transform parent)
        {
            root = new GameObject("FX").transform; root.SetParent(parent, false);
            var m = Resources.Load<Material>("ZUFx/additive");
            add = m != null ? m : new Material(Shader.Find("ZU/FxAdditive") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            InitParticles();
            flash = new GameObject("FX flash").AddComponent<Light>();
            flash.transform.SetParent(root, false);
            flash.type = LightType.Point; flash.range = 18; flash.intensity = 0; flash.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------------------------------------- particles
        const int CAP = 6000;
        readonly Vector3[] pPos = new Vector3[CAP], pVel = new Vector3[CAP];
        readonly Color[] pCol = new Color[CAP];
        readonly float[] pLife = new float[CAP], pMax = new float[CAP], pSize = new float[CAP], pGrav = new float[CAP];
        int next;
        Mesh pMesh; Vector3[] verts; Color[] cols;
        static Texture2D dot;

        void InitParticles()
        {
            pMesh = new Mesh { name = "fx particles", indexFormat = IndexFormat.UInt32 };
            verts = new Vector3[CAP * 4]; cols = new Color[CAP * 4];
            var uv = new Vector2[CAP * 4]; var tri = new int[CAP * 6];
            for (int i = 0; i < CAP; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(0, 1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(1, 0);
                int v = i * 4, t = i * 6;
                tri[t] = v; tri[t + 1] = v + 1; tri[t + 2] = v + 2; tri[t + 3] = v; tri[t + 4] = v + 2; tri[t + 5] = v + 3;
            }
            pMesh.vertices = verts; pMesh.uv = uv; pMesh.colors = cols; pMesh.triangles = tri;
            pMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000);
            pMesh.MarkDynamic();
            var go = new GameObject("FX particles"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = pMesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material(add) { name = "fx particles" };
            m.SetTexture("_BaseMap", Dot());
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        /// <summary>a soft glow dot with a hot centre (TS: alpha falls off to the rim, the centre brightened)</summary>
        static Texture2D Dot()
        {
            if (dot != null) return dot;
            int n = 64; dot = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "fx dot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f).magnitude;
                    float a = 1 - Mathf.SmoothStep(0, 0.5f, d), core = Mathf.Clamp01(1 - d * 2);
                    px[y * n + x] = new Color(1, 1, 1, a) * (1 + core) * 0.5f;
                }
            dot.SetPixels(px); dot.Apply(true);
            return dot;
        }

        public struct Opt { public float speed, life, size, grav, spread, up; public Vector3? dir; }
        public static Opt O(float speed = 4, float life = 0.6f, float size = 0.25f, float grav = 0, float spread = 0.2f, float up = 0, Vector3? dir = null)
            => new Opt { speed = speed, life = life, size = size, grav = grav, spread = spread, up = up, dir = dir };

        /// <summary>TS Particles.emit: count sparks at p in a random sphere of directions (or around `dir`)</summary>
        public void Emit(Vector3 p, int count, Color c, Opt o)
        {
            count = Mathf.Max(1, Mathf.RoundToInt(count * LodScale));
            var hdr = c * 1.6f; hdr.a = 1;
            for (int k = 0; k < count; k++)
            {
                int i = next; next = (next + 1) % CAP;
                pPos[i] = p + new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) * o.spread;
                var v = Random.onUnitSphere;
                float s = o.speed * (0.4f + Random.value * 0.6f);
                if (o.dir.HasValue) v = o.dir.Value + v * 0.35f;
                pVel[i] = v * s + Vector3.up * o.up;
                pCol[i] = hdr;
                pLife[i] = pMax[i] = o.life * (0.6f + Random.value * 0.6f);
                pSize[i] = o.size * (0.6f + Random.value * 0.8f);
                pGrav[i] = o.grav;
            }
        }

        void UpdateParticles(float dt, Camera cam)
        {
            Vector3 right = cam != null ? cam.transform.right : Vector3.right, up = cam != null ? cam.transform.up : Vector3.up;
            float drag = 1 - Mathf.Min(1, dt * 1.5f);
            for (int i = 0; i < CAP; i++)
            {
                int v = i * 4;
                if (pLife[i] <= 0) { verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero; continue; }
                pLife[i] -= dt;
                pVel[i].y -= pGrav[i] * dt;
                pVel[i] *= drag;
                pPos[i] += pVel[i] * dt;
                float k = Mathf.Max(0, pLife[i] / pMax[i]);
                float h = pSize[i] * 0.5f * (0.4f + 0.6f * k);
                Vector3 r = right * h, u = up * h, p = pPos[i];
                verts[v] = p - r - u; verts[v + 1] = p - r + u; verts[v + 2] = p + r + u; verts[v + 3] = p + r - u;
                var c = pCol[i]; c.a = k;
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
            }
            pMesh.vertices = verts; pMesh.colors = cols;
        }

        // ---------------------------------------------------------------------------------------------- timed shapes
        public enum Kind { Ring, Fade, Shatter, Tether, Trail, Shards }
        sealed class Timed
        {
            public GameObject go; public MeshRenderer mr; public float born, dur, r; public Kind kind; public Color col; public float alpha = 0.9f;
            public System.Func<(Vector3 a, Vector3 b, bool on)> ends; public float width; public System.Action<float> tick;
        }
        readonly List<Timed> timed = new List<Timed>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh ringMesh, beamMesh, coneMesh;

        GameObject Shape(string name, Mesh mesh)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = add; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }
        Timed Add(GameObject go, Kind kind, float now, float dur, Color c, float r = 1, float alpha = 0.9f)
        {
            var t = new Timed { go = go, mr = go.GetComponent<MeshRenderer>(), kind = kind, born = now, dur = dur, col = c * 1.4f, r = r, alpha = alpha };
            timed.Add(t); Tint(t, alpha);
            return t;
        }
        void Tint(Timed t, float a)
        {
            if (t.mr == null) return;
            var c = t.col; c.a = Mathf.Clamp01(a);
            t.mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); t.mr.SetPropertyBlock(mpb);
        }

        /// <summary>a shockwave ring growing out to radius r: flat on the ground, or standing (facing the camera)</summary>
        public void Ring(Vector3 p, float r, Color c, float now, float dur = 0.5f, bool flat = true)
        {
            ringMesh ??= Annulus(0.92f, 1f, 64, 0, Mathf.PI * 2);
            var go = Shape("ring", ringMesh);
            go.transform.position = p + Vector3.up * 0.1f;
            var t = Add(go, Kind.Ring, now, dur, c, r);
            // a standing ring turns its face (the annulus normal, +Y) to the camera every frame
            void Face() { var cam = Camera.main; if (cam != null) go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (cam.transform.position - go.transform.position).normalized); }
            if (!flat) { Face(); t.tick = _ => Face(); }
        }

        /// <summary>a straight beam a -> b of width w (a tracer, a tether, a sky column)</summary>
        public void Beam(Vector3 a, Vector3 b, Color c, float now, float dur, float w = 0.06f, System.Func<(Vector3, Vector3, bool)> follow = null)
        {
            beamMesh ??= Tube(1, 1, 8);
            var go = Shape("beam", beamMesh);
            Orient(go.transform, a, b, w);
            var t = Add(go, follow != null ? Kind.Tether : Kind.Fade, now, dur, c);
            t.ends = follow; t.width = w;
        }

        /// <summary>a translucent cone from a toward b, w wide at the far end (sound cones, lances, flame)</summary>
        public GameObject Cone(Vector3 a, Vector3 b, float w, Color c, float now, float dur, float alpha = 0.22f)
        {
            coneMesh ??= Tube(0, 1, 24);
            var go = Shape("cone", coneMesh);
            Orient(go.transform, a, b, 1);
            go.transform.localScale = new Vector3(w, w, (b - a).magnitude);
            if (dur > 0) Add(go, Kind.Fade, now, dur, c, 1, alpha);
            return go;
        }

        /// <summary>a crescent (part of a ring between r0 and r1, angles a0..a1 round local +Z) laid flat then turned by rot</summary>
        public void Arc(Vector3 p, Quaternion rot, float r0, float r1, float a0, float a1, Color c, float now, float dur, float alpha = 0.8f)
        {
            var go = Shape("arc", Annulus(r0, r1, 32, a0, a1));
            go.transform.SetPositionAndRotation(p, rot);
            Add(go, Kind.Fade, now, dur, c, 1, alpha);
        }

        /// <summary>a flat wedge racing out along the ground from p toward `to` (a ground shockwave)</summary>
        public void Wedge(Vector3 p, Vector3 to, float half, Color c, float now, float dur)
        {
            var d = to - p; d.y = 0; float len = d.magnitude;
            var m = new Mesh { name = "wedge" };
            float w = Mathf.Tan(half) * len;
            m.vertices = new[] { Vector3.zero, new Vector3(-w, 0, len), new Vector3(w, 0, len) };
            m.colors = new[] { Color.white, Color.white, Color.white };
            m.uv = new[] { Vector2.zero, Vector2.up, Vector2.one };
            m.triangles = new[] { 0, 1, 2 };
            var go = Shape("wedge", m);
            go.transform.SetPositionAndRotation(p + Vector3.up * 0.08f, Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : Vector3.forward));
            Add(go, Kind.Shatter, now, dur, c, 1, 0.7f);
        }

        /// <summary>a lightning bolt a -> b: a jagged line with kinks bulging in the middle (TS bolt / zigzag)</summary>
        public void Bolt(Vector3 a, Vector3 b, Color c, float now, float width, float dur, float jitter = 1.6f, int segs = 12)
        {
            var go = new GameObject("bolt"); go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = add; lr.positionCount = segs + 1; lr.useWorldSpace = true;
            lr.widthMultiplier = width * 2; lr.numCapVertices = 2; lr.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs, j = i == 0 || i == segs ? 0 : jitter * Mathf.Sin(t * Mathf.PI);
                lr.SetPosition(i, Vector3.Lerp(a, b, t) + new Vector3((Random.value - 0.5f) * j, (Random.value - 0.5f) * j * 0.4f, (Random.value - 0.5f) * j));
            }
            var col = c * 1.6f; col.a = 1; lr.startColor = lr.endColor = col;
            var tm = new Timed { go = go, kind = Kind.Fade, born = now, dur = dur, col = col };
            tm.tick = k => { var cc = col; cc.a = 1 - k; lr.startColor = lr.endColor = cc; };
            timed.Add(tm);
        }

        /// <summary>a timed emitter riding an actor (speed trails, rising shards): `each` runs every frame for dur</summary>
        public void During(float now, float dur, System.Action<float> each)
        {
            timed.Add(new Timed { go = null, kind = Kind.Trail, born = now, dur = dur, tick = each });
        }

        // ---------------------------------------------------------------------------------------------- light
        readonly Light flash; float flashUntil;
        /// <summary>a brief flash of light (TS units: three point-light intensity, ~25..140)</summary>
        public void Light(Vector3 p, Color c, float intensity, float now, float dur = 0.12f)
        {
            flash.transform.position = p + Vector3.up * 0.5f; flash.color = c; flash.intensity = intensity * 0.06f; flashUntil = now + dur;
        }

        // ---------------------------------------------------------------------------------------------- frame
        public void Update(float now, float dt)
        {
            UpdateParticles(dt, Camera.main);
            if (now > flashUntil) flash.intensity *= Mathf.Pow(0.8f, dt * 60);
            Shake *= Mathf.Pow(0.02f, dt);
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                var t = timed[i];
                float k = (now - t.born) / Mathf.Max(1e-4f, t.dur);
                if (k >= 1 || (t.go == null && t.kind != Kind.Trail))
                {
                    if (t.go != null) { var mf = t.go.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != ringMesh && mf.sharedMesh != beamMesh && mf.sharedMesh != coneMesh) Object.Destroy(mf.sharedMesh); Object.Destroy(t.go); }
                    timed.RemoveAt(i); continue;
                }
                if (k < 0) { if (t.go != null) t.go.SetActive(false); continue; }
                if (t.go != null && !t.go.activeSelf) t.go.SetActive(true);
                t.tick?.Invoke(k);
                switch (t.kind)
                {
                    case Kind.Ring: t.go.transform.localScale = Vector3.one * (t.r * (0.2f + 0.8f * Mathf.Sqrt(k))); Tint(t, t.alpha * (1 - k)); break;
                    case Kind.Fade: Tint(t, t.alpha * (1 - k)); break;
                    case Kind.Shatter: { float s = Mathf.Min(1, k * 2.6f); t.go.transform.localScale = new Vector3(s, 1, s); Tint(t, t.alpha * (1 - k * k)); break; }
                    case Kind.Tether:
                        if (t.ends != null)
                        {
                            var (a, b, on) = t.ends();
                            t.go.SetActive(on);
                            if (on) Orient(t.go.transform, a, b, t.width);
                            Tint(t, 0.7f + 0.3f * Mathf.Sin(now * 20));
                        }
                        break;
                }
            }
        }

        // ---------------------------------------------------------------------------------------------- meshes
        /// <summary>the TS beamGeo: an open radius-1 tube from z = 0 to z = 1 (Orient stretches it a -> b)</summary>
        public static Mesh BeamMesh => beamMesh ??= Tube(1, 1, 8);
        /// <summary>the TS ringGeo: an annulus 0.92..1 in the XZ plane (normal +Y)</summary>
        public static Mesh RingMesh => ringMesh ??= Annulus(0.92f, 1f, 64, 0, Mathf.PI * 2);
        /// <summary>an open cone along +Z: apex at z = 0, radius 1 at z = 1 (flames, thrusters)</summary>
        public static Mesh ConeMesh => coneMesh ??= Tube(0, 1, 24);

        public static void Orient(Transform t, Vector3 a, Vector3 b, float w)
        {
            var d = b - a; float m = d.magnitude, l = Mathf.Max(0.01f, m);
            t.SetPositionAndRotation(a, m > 1e-4f ? Quaternion.LookRotation(d / m) : t.rotation);   // (ends together: keep the turn)
            t.localScale = new Vector3(w, w, l);
        }

        /// <summary>an annulus r0..r1 in the XZ plane between angles a0..a1 measured from +Z toward +X (a ring, a crescent)</summary>
        static Mesh Annulus(float r0, float r1, int segs, float a0, float a1)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / segs);
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(d * r0); v.Add(d * r1); uv.Add(new Vector2((float)i / segs, 0)); uv.Add(new Vector2((float)i / segs, 1)); c.Add(Color.white); c.Add(Color.white);
                if (i < segs) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "annulus" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>an open tube along +Z from radius r0 at z = 0 to r1 at z = 1 (beam: 1 -> 1, cone: 0 -> 1)</summary>
        static Mesh Tube(float r0, float r1, int sides)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * r0); v.Add(d * r1 + Vector3.forward); uv.Add(new Vector2((float)i / sides, 0)); uv.Add(new Vector2((float)i / sides, 1)); c.Add(Color.white); c.Add(Color.white);
                if (i < sides) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "tube" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }
    }
}
