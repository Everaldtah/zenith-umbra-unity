// Blinking (TS render/Eyes.ts, ported): the heroes' eyes are painted into their textures, so each eye gets a small lid -
// a patch in the face's skin tone with a lash line along its lower edge - hung from the head bone just in front of the
// painted eye. It drops from the upper lid to cover the eye and lifts again. Eye centre, size, facing and the lid / lash
// colours come from the model itself (assetgen/blender/eyes.py: MediaPipe face landmarks on a front render, raycast onto
// the mesh), carried over in Resources/ZUAbilityFx/eyes.json (the TS public/models/manifest.json `eyes` / `glowEyes`).
// Timing follows how people blink: every 2-6 s, ~150 ms, a double blink now and then; a squeeze when hit; shut in death.
//
// The eye data is in the GLB's model space; in Unity that is the hero rig's own frame (the FBX root under the prefab, the
// Animator's transform) with X mirrored (Sp.U). Placement maths runs in that model space as in the TS.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZU.Game.Fx
{
    /// <summary>a painted eye found on the model (eyes.py), model space: centre, facing, width / height, lid + lash sRGB</summary>
    public sealed class EyeInfo
    {
        public Vector3 p, n; public float w, h; public Color skin, lash;

        static Dictionary<string, (EyeInfo[] eyes, EyeInfo[] glow)> table;
        /// <summary>the eyes measured on a hero's model (null when it has none)</summary>
        public static (EyeInfo[] eyes, EyeInfo[] glow) For(string modelId)
        {
            if (table == null)
            {
                table = new Dictionary<string, (EyeInfo[], EyeInfo[])>();
                var ta = Resources.Load<TextAsset>("ZUAbilityFx/eyes");
                if (ta != null)
                    foreach (var kv in JObject.Parse(ta.text))
                        table[kv.Key] = (Parse(kv.Value["eyes"] as JArray), Parse(kv.Value["glowEyes"] as JArray));
            }
            return table.TryGetValue(modelId, out var e) ? e : (null, null);
        }
        static EyeInfo[] Parse(JArray a)
        {
            if (a == null) return null;
            var list = new List<EyeInfo>();
            Vector3 V(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
            Color C(JToken t) => new Color((float)t[0], (float)t[1], (float)t[2], 1);
            foreach (var e in a) list.Add(new EyeInfo { p = V(e["p"]), n = V(e["n"]), w = (float)e["w"], h = (float)e["h"], skin = C(e["skin"]), lash = C(e["lash"]) });
            return list.ToArray();
        }
    }

    public sealed class Eyelids
    {
        readonly List<Transform> lids = new List<Transform>();
        readonly List<Renderer> rends = new List<Renderer>();
        readonly List<float> sy = new List<float>();
        readonly List<Object> owned = new List<Object>();
        float next = 1 + Random.value * 4;
        float blinkAt = -9;
        bool dbl;
        float lastHit = -9;

        /// <summary>the lid's texture: skin with crease shading under the brow and the lash line along the bottom edge
        /// (slightly curved, thick at the outer corner) - the TS canvas, drawn here per pixel (sRGB, as the canvas)</summary>
        static Texture2D LidTexture(Color skin, Color lash)
        {
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, true) { name = "eyelid", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[32 * 32];
            for (int cy = 0; cy < 32; cy++)               // canvas row (0 = top)
                for (int x = 0; x < 32; x++)
                {
                    float fy = cy + 0.5f, fx = x + 0.5f;
                    // the crease: black at 10% at the top fading out by 60% of the way down
                    float shade = 0.10f * Mathf.Max(0, 1 - fy / 32 / 0.6f);
                    var c = Color.Lerp(skin, Color.black, shade);
                    // the lash line: below the curve from (0, 25) through (16, 31) to (32, 23)
                    float u = fx / 32, yc = (1 - u) * (1 - u) * 25 + 2 * (1 - u) * u * 31 + u * u * 23;
                    if (fy >= yc) c = lash;
                    c.a = 1;
                    px[(31 - cy) * 32 + x] = c;            // (Unity rows run bottom-up; the canvas texture was flipped the same way)
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Mesh lidMesh;
        /// <summary>a gently curved patch (so its corners tuck into the face), pivot along the top edge: scale.y = how far it has closed</summary>
        static Mesh LidMesh()
        {
            if (lidMesh != null) return lidMesh;
            var b = AbilityKit.Plane(1, 1, 6, 1);
            for (int i = 0; i < b.Count; i++)
            {
                var v = b.v[i]; v.z = -0.35f * v.x * v.x; v.y -= 0.5f; b.v[i] = v;
                b.n[i] = new Vector3(0.7f * v.x, 0, 1).normalized;
            }
            return lidMesh = b.Build("eyelid");
        }

        /// <summary>model: the hero rig's frame (the space eyes.py measured in); head: its head bone</summary>
        public Eyelids(Transform model, Transform head, EyeInfo[] eyes, float emissive)
        {
            foreach (var e in eyes)
            {
                var map = LidTexture(e.skin, e.lash);
                var mat = AbilityKit.Lit();
                mat.SetTexture("_BaseMap", map);
                mat.SetVector("_EmissionColor", new Vector4(emissive, emissive, emissive, 1)); mat.SetFloat("_EmitFromBase", 1);
                mat.SetFloat("_Roughness", 0.72f); mat.SetFloat("_Metalness", 0.04f);
                mat.SetFloat("_OffsetFactor", -4); mat.SetFloat("_OffsetUnits", -4); mat.SetFloat("_Cull", 2);
                owned.Add(map); owned.Add(mat);
                var lid = new GameObject("eyelid");
                lid.AddComponent<MeshFilter>().sharedMesh = LidMesh();
                var mr = lid.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // model-space frame: facing the eye's normal, up = model up, the top edge on the upper lid
                var n = e.n; n.y *= 0.3f; n.Normalize();
                var x = Vector3.Cross(new Vector3(0, 1, 0), n).normalized; var y = Vector3.Cross(n, x);
                var top = e.p + y * (e.h * 0.62f) + n * Mathf.Max(0.0018f, e.w * 0.07f);
                float ms = model.lossyScale.x;
                lid.transform.SetPositionAndRotation(model.TransformPoint(Sp.U(top)), model.rotation * Sp.U(Sp.Basis(y, n)));
                lid.transform.localScale = new Vector3(e.w * 1.35f, e.h * 1.3f, 1) * ms;
                lid.transform.SetParent(head, true);
                sy.Add(lid.transform.localScale.y);
                mr.enabled = false;
                lids.Add(lid.transform); rends.Add(mr);
            }
        }

        /// <summary>closed amount (0 open .. 1 shut) this frame</summary>
        float Closed(float time, float hitAt, bool dead)
        {
            if (dead) return 1;
            if (time >= next) { blinkAt = time; dbl = Random.value < 0.18f; next = time + 2 + Random.value * 4; }
            float B(float t0) { float u = (time - t0) / 0.15f; return u < 0 || u > 1 ? 0 : u < 0.4f ? u / 0.4f : u < 0.55f ? 1 : 1 - (u - 0.55f) / 0.45f; }
            float c = Mathf.Max(B(blinkAt), dbl ? B(blinkAt + 0.22f) : 0);
            // flinch: a hard squeeze on a fresh hit
            if (hitAt > lastHit) lastHit = hitAt;
            float ha = time - lastHit;
            if (ha >= 0 && ha < 0.28f) c = Mathf.Max(c, 0.85f * (1 - ha / 0.28f));
            return c;
        }

        /// <summary>shown: whether the hero's body is drawn this frame</summary>
        public void Update(float time, float hitAt, bool dead, bool shown)
        {
            float c = Closed(time, hitAt, dead);
            for (int i = 0; i < lids.Count; i++)
            {
                rends[i].enabled = shown && c > 0.03f;
                var s = lids[i].localScale; s.y = sy[i] * Mathf.Max(0.03f, c); lids[i].localScale = s;
            }
        }

        public void Dispose()
        {
            foreach (var l in lids) if (l != null) Object.Destroy(l.gameObject);
            foreach (var o in owned) Object.Destroy(o);
            lids.Clear(); rends.Clear();
        }
    }

    /// <summary>
    /// Masked heroes' eyes (Hex's porcelain mask, Kagemaru's skull): a glow in each dark socket instead of a blink - the
    /// read Overwatch gives its masked characters. Camera-facing additive sprites hung from the head bone just inside the
    /// socket (the mask hides them from behind), breathing slowly, flaring on a cast or a hit, dark in death.
    /// </summary>
    public sealed class EyeGlow
    {
        readonly List<Transform> sprites = new List<Transform>();
        readonly List<Renderer> rends = new List<Renderer>();
        readonly List<float> s0 = new List<float>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly Texture2D tex; readonly Material mat;
        float flare, lastCast = -9, lastHit = -9;
        static Mesh quad;

        public EyeGlow(Transform model, Transform head, EyeInfo[] eyes, string color)
        {
            var col = Sp.Hex(color);
            // the TS canvas gradient: white-hot centre, the hero's colour, fading out at the rim
            tex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "eye glow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[64 * 64];
            Color Rgb(float a, float k = 1) => new Color(Mathf.Min(1, col.r * k), Mathf.Min(1, col.g * k), Mathf.Min(1, col.b * k), a);
            Color[] stops = { new Color(1, 1, 1, 1), Rgb(1, 1.4f), Rgb(0.55f), Rgb(0) }; float[] at = { 0, 0.18f, 0.45f, 1 };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Mathf.Min(1, new Vector2(x + 0.5f - 32, y + 0.5f - 32).magnitude / 32);
                    int k = 0; while (k < 2 && d > at[k + 1]) k++;
                    px[y * 64 + x] = Color.Lerp(stops[k], stops[k + 1], (d - at[k]) / (at[k + 1] - at[k]));
                }
            tex.SetPixels(px); tex.Apply(true);
            mat = AbilityKit.Additive(); mat.SetTexture("_BaseMap", tex); mat.renderQueue = 3002;      // TS renderOrder 2
            quad ??= AbilityKit.Plane(1, 1).Build("eye glow quad");
            float ms = model.lossyScale.x;
            foreach (var e in eyes)
            {
                var sp = new GameObject("eye glow");
                sp.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = sp.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                // just in front of the socket (the sockets are painted, not holes): the head still hides it from behind
                var n = e.n.normalized;
                sp.transform.position = model.TransformPoint(Sp.U(e.p + n * (e.w * 0.35f)));
                float s = e.w * 2.6f * ms;
                sp.transform.localScale = Vector3.one * s;
                sp.transform.SetParent(head, true);
                s0.Add(sp.transform.localScale.x);
                sprites.Add(sp.transform); rends.Add(mr);
            }
        }

        public void Update(float time, float castAt, float hitAt, bool dead, bool shown)
        {
            if (castAt > lastCast) { lastCast = castAt; flare = 1; }
            if (hitAt > lastHit) { lastHit = hitAt; flare = Mathf.Max(flare, 0.5f); }
            flare = Mathf.Max(0, flare - 0.04f);          // TS-PARITY: a fixed step a frame, as in the TS
            float k = dead ? 0 : 0.85f + 0.15f * Mathf.Sin(time * 2.1f) + flare * 0.9f;
            var cam = Camera.main;
            for (int i = 0; i < sprites.Count; i++)
            {
                var sp = sprites[i];
                float s = s0[i] * (0.9f + 0.35f * k);
                sp.localScale = Vector3.one * s;
                if (cam != null) sp.rotation = cam.transform.rotation;           // a sprite: square to the screen
                rends[i].GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", new Color(1, 1, 1, Mathf.Min(1, k))); rends[i].SetPropertyBlock(mpb);
                rends[i].enabled = shown && k > 0.01f;
            }
        }

        public void Dispose()
        {
            foreach (var s in sprites) if (s != null) Object.Destroy(s.gameObject);
            sprites.Clear(); rends.Clear();
            Object.Destroy(tex); Object.Destroy(mat);
        }
    }
}
