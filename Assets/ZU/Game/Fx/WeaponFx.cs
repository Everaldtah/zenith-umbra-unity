// Gunfire you can read at a glance (port of the TS render/WeaponFx.ts, the desktop edition's): every round is a travelling
// tracer - a white-hot core inside a coloured sheath with a thin rail trail that lingers a beat - fired out of a star-shaped
// muzzle flash; it lands in a spray of hot sparks, a flash, a curl of smoke and a scorch mark that stays on the wall; a
// round into the ground kicks up grit; rotary cannons throw brass that bounces and settles.
// Sprites: Kenney's CC0 Particle Pack (Resources/ZUFx/Sprites, converted from the TS public/fx) - star bursts for the
// flashes, smoke wisps, burn marks, dirt; the procedural star and scorch of the TS stay in the mix. Streaks (tracers, rails,
// sparks) are camera-facing ribbons rebuilt into one mesh each per frame; flashes are additive (ZU/FxAdditive), smoke /
// soot / dust cover (ZU/FxAlpha). All positions are Unity world space.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.Game.Fx
{
    public sealed class WeaponFx
    {
        const int MAX_TR = 256, MAX_SP = 512, MAX_FL = 64, MAX_DC = 96, MAX_CS = 96, MAX_SMOKE = 48, MAX_DUST = 24;
        static readonly string[] FLASH_TEX = { "star_09", "star_06", "star_08" }, SMOKE_TEX = { "smoke_01", "smoke_02", "smoke_04", "smoke_05", "smoke_06", "smoke_07" };
        static readonly string[] SCORCH_TEX = { "scorch_01", "scorch_02", "scorch_03" }, DIRT_TEX = { "dirt_01", "dirt_02", "dirt_03" };

        sealed class Tr { public Vector3 a, b; public float born, dur, len, w; public Color col; public bool rail; }
        sealed class Sp { public Vector3 p, v; public float born, dur, w; public Color col; }
        sealed class Sprite { public Transform t; public MeshRenderer mr; public float born = -99, rot, spin, size; public bool on; }
        sealed class Case { public Transform t; public Vector3 v, spin; public float born = -9; public bool on; }

        readonly Transform root;
        readonly List<Tr> tr = new List<Tr>(); readonly List<Sp> sp = new List<Sp>();
        readonly Ribbons trMesh, railMesh, spMesh;
        readonly Sprite[] flashes = new Sprite[MAX_FL], smoke = new Sprite[MAX_SMOKE], dust = new Sprite[MAX_DUST], decals = new Sprite[MAX_DC];
        readonly Case[] cases = new Case[MAX_CS];
        int flashI, smokeI, dustI, decalI, caseI;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh quad;
        /// <summary>the ground under a point (Unity world x, z, from height y) - casings bounce on it</summary>
        public System.Func<float, float, float, float> ground;

        public WeaponFx(Transform parent, Material additive, Material alpha)
        {
            root = new GameObject("WeaponFx").transform; root.SetParent(parent, false);
            var streak = StreakTexture();
            Material Mat(Material m, Texture t, Color k) { var x = new Material(m) { name = "wfx " + (t != null ? t.name : "") }; x.SetTexture("_BaseMap", t); x.SetColor("_BaseColor", k); return x; }
            // the TS: colour x 1.6 on every streak (toneMapped off), the rail at 0.35 opacity
            trMesh = new Ribbons("tracers", root, Mat(additive, streak, new Color(1.6f, 1.6f, 1.6f, 1)));
            railMesh = new Ribbons("rails", root, Mat(additive, streak, new Color(1.6f, 1.6f, 1.6f, 0.35f)));
            spMesh = new Ribbons("sparks", root, Mat(additive, streak, Color.white));
            var star = StarTexture();
            var flashMats = new Material[4];
            for (int k = 0; k < 4; k++) flashMats[k] = Mat(additive, k == 3 ? star : Tex(FLASH_TEX[k]), Color.white);
            // a quarter keep the procedural star, the rest are Kenney bursts: no two flashes in a burst look alike
            for (int i = 0; i < MAX_FL; i++) flashes[i] = MakeSprite("flash", flashMats[i % 4]);
            // burn marks: Kenney scorches darkened to soot (one in four keeps the procedural mark: a softer, round one)
            var scorch = ScorchTexture();
            var scorchMats = new Material[4];
            for (int k = 0; k < 3; k++) scorchMats[k] = Mat(alpha, Tex(SCORCH_TEX[k]), Conv.Hex("#1c140e"));
            scorchMats[3] = Mat(alpha, scorch, Color.white);
            for (int i = 0; i < MAX_DC; i++) decals[i] = MakeSprite("scorch", scorchMats[i % 4 != 3 ? i % 3 : 3]);
            for (int i = 0; i < MAX_SMOKE; i++) smoke[i] = MakeSprite("smoke", Mat(alpha, Tex(SMOKE_TEX[i % SMOKE_TEX.Length]), Color.white));
            for (int i = 0; i < MAX_DUST; i++) dust[i] = MakeSprite("dust", Mat(alpha, Tex(DIRT_TEX[i % 3]), Conv.Hex("#dccaa6")));
            var brass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "brass" };
            brass.SetColor("_BaseColor", Conv.Hex("#d8a64a")); brass.SetFloat("_Metallic", 0.9f); brass.SetFloat("_Smoothness", 0.7f);
            for (int i = 0; i < MAX_CS; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(g.GetComponent<Collider>());
                g.name = "casing"; g.transform.SetParent(root, false); g.transform.localScale = new Vector3(0.044f, 0.045f, 0.044f);
                var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = brass; r.shadowCastingMode = ShadowCastingMode.Off;
                g.SetActive(false);
                cases[i] = new Case { t = g.transform };
            }
        }

        static Texture2D Tex(string name)
        {
            var t = Resources.Load<Texture2D>("ZUFx/Sprites/" + name);
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        Sprite MakeSprite(string name, Material m)
        {
            quad ??= Quad();
            var g = new GameObject(name); g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            g.SetActive(false);
            return new Sprite { t = g.transform, mr = mr };
        }

        /// <summary>a unit quad in the XY plane facing -Z (a sprite turned to the camera, a decal laid on its normal), white vertex colours</summary>
        static Mesh Quad()
        {
            var m = new Mesh { name = "wfx quad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); m.RecalculateBounds();
            return m;
        }

        void Show(Sprite s, Color c, float alpha)
        {
            c.a = alpha; s.mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); s.mr.SetPropertyBlock(mpb);
        }

        // ------------------------------------------------------------------------------------------------ the TS calls
        /// <summary>a round: tracer from the muzzle to where it landed; `rail` rounds leave a lingering trail (hitscan)</summary>
        public void Tracer(Vector3 from, Vector3 to, Color color, float now, float speed = 240, float w = 0.07f, float len = 3.2f, bool rail = true)
        {
            if (tr.Count >= MAX_TR) tr.RemoveAt(0);
            float d = Vector3.Distance(from, to);
            tr.Add(new Tr { a = from, b = to, born = now, dur = Mathf.Max(0.05f, d / speed), len = Mathf.Min(d, len), w = w, col = color, rail = rail });
        }

        /// <summary>a star-shaped muzzle flash (and a puff of smoke from big guns)</summary>
        public void Muzzle(Vector3 p, Color color, float now, float size = 0.5f, bool smokes = false)
        {
            var s = flashes[flashI]; flashI = (flashI + 1) % MAX_FL;
            s.t.position = p; s.size = size * (0.8f + Random.value * 0.4f); s.rot = Random.value * 180;
            s.on = true; s.born = now; s.t.gameObject.SetActive(true);
            Show(s, Color.Lerp(color, Color.white, 0.45f), 1);
            if (smokes && Random.value < 0.35f) Puff(p, now, 0.35f);
        }

        /// <summary>a round landing on the world: sparks along the bounce, a flash, smoke, a scorch mark facing out of the
        /// surface (n: the surface normal, or none for a hit on a body)</summary>
        public void Impact(Vector3 p, Vector3? n, Color color, float now, bool big = false)
        {
            var N = n.HasValue ? n.Value.normalized : Vector3.up;
            var c = Color.Lerp(color, Conv.Hex("#ffd9a0"), 0.4f);
            int k = big ? 10 : 6;
            for (int i = 0; i < k; i++)
            {
                if (sp.Count >= MAX_SP) sp.RemoveAt(0);
                var v = N * (2 + Random.value * 4) + new Vector3((Random.value - 0.5f) * 6, Random.value * 3, (Random.value - 0.5f) * 6);
                sp.Add(new Sp { p = p, v = v, born = now, dur = 0.18f + Random.value * 0.22f, col = c, w = 0.025f + Random.value * 0.02f });
            }
            Muzzle(p + N * 0.05f, color, now, big ? 0.7f : 0.38f);
            if (Random.value < 0.4f) Puff(p + N * 0.15f, now, 0.5f);
            // a round into the ground throws grit and a low, dusty puff
            if (n.HasValue && N.y > 0.6f && Random.value < (big ? 1 : 0.55f))
            {
                var d = dust[dustI]; dustI = (dustI + 1) % MAX_DUST;
                d.t.position = p + Vector3.up * 0.2f; d.size = big ? 1.0f : 0.6f; d.rot = Random.value * 360; d.spin = 0;
                d.on = true; d.born = now; d.t.gameObject.SetActive(true);
                if (Random.value < 0.5f) Puff(p + Vector3.up * 0.2f, now, big ? 0.8f : 0.5f, Conv.Hex("#b3a186"));
            }
            if (n.HasValue)
            {
                var d = decals[decalI]; decalI = (decalI + 1) % MAX_DC;
                d.t.position = p + N * 0.012f;
                // the quad faces -Z: turn its back (+Z) into the surface so the mark faces out along N, then a random roll
                d.t.rotation = Quaternion.LookRotation(-N) * Quaternion.AngleAxis(Random.value * 360, Vector3.forward);
                d.t.localScale = Vector3.one * (big ? 0.55f : 0.22f + Random.value * 0.1f);
                d.on = true; d.born = now; d.t.gameObject.SetActive(true);
                Show(d, Color.white, 1);
            }
        }

        /// <summary>brass kicked out of a rotary cannon: arcs out to the side, bounces, settles</summary>
        public void Casing(Vector3 p, Vector3 side, float now)
        {
            var C = cases[caseI]; caseI = (caseI + 1) % MAX_CS;
            C.t.position = p; C.on = true; C.born = now; C.t.gameObject.SetActive(true);
            C.v = new Vector3(side.x * (2 + Random.value * 1.5f), 2.5f + Random.value * 1.5f, side.z * (2 + Random.value * 1.5f));
            C.spin = new Vector3(Random.value * 20, Random.value * 20, Random.value * 20);
        }

        void Puff(Vector3 p, float now, float size, Color? color = null)
        {
            var s = smoke[smokeI]; smokeI = (smokeI + 1) % MAX_SMOKE;
            s.t.position = p; s.size = size; s.rot = Random.value * 360; s.spin = (Random.value - 0.5f) * 1.6f * Mathf.Rad2Deg;
            s.on = true; s.born = now; s.t.gameObject.SetActive(true);
            Show(s, color ?? Conv.Hex("#9a938a"), 0);
        }

        // ------------------------------------------------------------------------------------------------ per frame
        public void Update(float now, float dt)
        {
            var cam = Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            // tracers: the head travels muzzle -> target; the rail (a thin line over the whole path) fades behind it
            tr.RemoveAll(t => now - t.born >= t.dur + 0.14f);
            trMesh.Begin(); railMesh.Begin();
            foreach (var t in tr)
            {
                float k = Mathf.Min(1, (now - t.born) / t.dur), L = Vector3.Distance(t.a, t.b); if (L <= 0) L = 1;
                float head = k * L;
                if (k < 1) trMesh.Seg(Vector3.Lerp(t.a, t.b, Mathf.Max(0, head - t.len) / L), Vector3.Lerp(t.a, t.b, head / L), t.w, t.col, camPos);
                if (t.rail)
                {
                    float f = 1 - Mathf.Min(1, (now - t.born) / (t.dur + 0.14f));
                    railMesh.Seg(t.a, Vector3.Lerp(t.a, t.b, k), t.w * 0.4f * f, t.col, camPos);
                }
            }
            trMesh.End(); railMesh.End();
            // sparks: short streaks along their velocity, falling
            sp.RemoveAll(q => now - q.born >= q.dur);
            spMesh.Begin();
            foreach (var q in sp)
            {
                q.v.y -= 14 * dt; q.p += q.v * dt;
                float f = 1 - (now - q.born) / q.dur;
                spMesh.Seg(q.p - q.v * 0.035f, q.p, q.w * (0.4f + 0.6f * f), q.col, camPos);
            }
            spMesh.End();
            // flashes: 50 ms pops
            foreach (var s in flashes)
            {
                if (!s.on) continue;
                float k = (now - s.born) / 0.05f;
                if (k >= 1) { Off(s); continue; }
                Face(s, camPos); s.mr.GetPropertyBlock(mpb); var c = mpb.GetColor("_BaseColor"); Show(s, c, 1 - k * k);
            }
            // smoke: rises, spreads, fades
            foreach (var s in smoke)
            {
                if (!s.on) continue;
                float k = (now - s.born) / 0.9f;
                if (k >= 1) { Off(s); continue; }
                s.t.position += Vector3.up * (dt * 0.6f); s.size *= 1 + dt * 1.4f; s.rot += s.spin * dt;
                Face(s, camPos); s.mr.GetPropertyBlock(mpb); var c = mpb.GetColor("_BaseColor"); Show(s, c, 0.6f * Mathf.Min(1, k * 8) * (1 - k));
            }
            // dust: a fast burst of grit that spreads, sinks and thins out in 0.4 s
            foreach (var s in dust)
            {
                if (!s.on) continue;
                float k = (now - s.born) / 0.4f;
                if (k >= 1) { Off(s); continue; }
                s.size *= 1 + dt * 2.2f; s.t.position -= Vector3.up * (dt * 0.3f);
                Face(s, camPos); Show(s, Conv.Hex("#dccaa6"), 0.95f * (1 - k * k));
            }
            // scorch marks stay a while, then fade
            foreach (var d in decals)
            {
                if (!d.on) continue;
                float age = now - d.born;
                if (age > 9) { Off(d); continue; }
                Show(d, Color.white, age > 6 ? 1 - (age - 6) / 3 : 1);
            }
            // brass
            foreach (var C in cases)
            {
                if (!C.on) continue;
                if (now - C.born > 2.2f) { C.on = false; C.t.gameObject.SetActive(false); continue; }
                C.v.y -= 14 * dt; C.t.position += C.v * dt;
                C.t.Rotate(C.spin * (dt * Mathf.Rad2Deg), Space.Self);
                var pp = C.t.position; float gy = ground != null ? ground(pp.x, pp.z, pp.y + 0.3f) : 0;
                if (pp.y < gy + 0.02f && C.v.y < 0) { C.t.position = new Vector3(pp.x, gy + 0.02f, pp.z); C.v.y *= -0.35f; C.v.x *= 0.5f; C.v.z *= 0.5f; C.spin *= 0.5f; }
            }
        }

        static void Off(Sprite s) { s.on = false; s.t.gameObject.SetActive(false); }
        /// <summary>a sprite turned to the camera, rolled by its own rotation, at its size</summary>
        static void Face(Sprite s, Vector3 cam)
        {
            var to = s.t.position - cam;
            if (to.sqrMagnitude < 1e-6f) return;
            s.t.rotation = Quaternion.LookRotation(to) * Quaternion.AngleAxis(s.rot, Vector3.forward);
            s.t.localScale = Vector3.one * s.size;
        }

        // ------------------------------------------------------------------------------------------------ ribbons
        /// <summary>camera-facing ribbons a -> b rebuilt into one mesh per frame (the TS instanced streak quads)</summary>
        sealed class Ribbons
        {
            readonly Mesh mesh; readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> c = new List<Color>(); readonly List<int> tri = new List<int>();
            public Ribbons(string name, Transform parent, Material m)
            {
                mesh = new Mesh { name = name }; mesh.MarkDynamic();
                var g = new GameObject(name); g.transform.SetParent(parent, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            public void Begin() { v.Clear(); uv.Clear(); c.Clear(); tri.Clear(); }
            /// <summary>TS seg: x along a -> b, y across it facing the camera, the head of the texture (u = 1) at b</summary>
            public void Seg(Vector3 a, Vector3 b, float w, Color col, Vector3 cam)
            {
                var x = b - a; float len = x.magnitude; if (len < 1e-4f) len = 1e-4f; x /= len;
                var mid = (a + b) * 0.5f;
                var y = Vector3.Cross(x, (cam - mid).normalized);
                if (y.sqrMagnitude < 1e-8f) y = Vector3.up; y.Normalize();
                y *= w * 0.5f;
                int i = v.Count;
                v.Add(a - y); v.Add(a + y); v.Add(b + y); v.Add(b - y);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                c.Add(col); c.Add(col); c.Add(col); c.Add(col);
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            }
            public void End()
            {
                mesh.Clear();
                if (v.Count == 0) return;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(tri, 0);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000);
            }
        }

        // ------------------------------------------------------------------------------------------------ textures
        /// <summary>the streak's look across its width (v) and along its length (u): a white-hot core line in a soft sheath,
        /// brightest at the head (u = 1), fading into the tail</summary>
        static Texture2D StreakTexture()
        {
            const int W = 128, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "streak", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = Mathf.Abs(y / (float)(H - 1) - 0.5f) * 2, u = x / (float)(W - 1);
                    float core = Mathf.Max(0, 1 - v / 0.28f), sheath = Mathf.Max(0, 1 - v);
                    float along = Mathf.Min(1, u / 0.3f) * (0.45f + 0.55f * u);
                    float wv = core * 0.95f;
                    px[y * W + x] = new Color32(255, (byte)(255 * (0.8f + 0.2f * wv)), (byte)(255 * (0.55f + 0.45f * wv)), (byte)(255 * Mathf.Min(1, (sheath * sheath * 0.8f + core) * along)));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        /// <summary>the TS canvas star: a soft radial glow with six spikes, alternately long and short</summary>
        static Texture2D StarTexture()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "star", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - 64, dy = y + 0.5f - 64, r = Mathf.Sqrt(dx * dx + dy * dy) / 64;
                    float a = r < 0.18f ? Mathf.Lerp(1, 0.9f, r / 0.18f) : r < 0.45f ? Mathf.Lerp(0.9f, 0.25f, (r - 0.18f) / 0.27f) : r < 1 ? Mathf.Lerp(0.25f, 0, (r - 0.45f) / 0.55f) : 0;
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * Mathf.PI / 3 + (i % 2) * 0.2f, L = i % 2 == 1 ? 44 : 62;
                        float along = dx * Mathf.Cos(ang) + dy * Mathf.Sin(ang), across = -dx * Mathf.Sin(ang) + dy * Mathf.Cos(ang);
                        if (along > 0 && along < L && Mathf.Abs(across) < 5 * (1 - along / L)) a += 0.9f * (1 - along / L);     // 'lighter' adds
                    }
                    px[y * S + x] = new Color(1, 1, 1, Mathf.Min(1, a));
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>the TS canvas scorch: a dark radial burn with soot flecks</summary>
        static Texture2D ScorchTexture()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "scorch", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - 32) * (x + 0.5f - 32) + (y + 0.5f - 32) * (y + 0.5f - 32)) / 32;
                    Color c = r < 0.25f ? Color.Lerp(new Color(10 / 255f, 8 / 255f, 6 / 255f, 0.95f), new Color(25 / 255f, 18 / 255f, 12 / 255f, 0.8f), r / 0.25f)
                        : r < 0.6f ? Color.Lerp(new Color(25 / 255f, 18 / 255f, 12 / 255f, 0.8f), new Color(40 / 255f, 30 / 255f, 20 / 255f, 0.3f), (r - 0.25f) / 0.35f)
                        : r < 1 ? Color.Lerp(new Color(40 / 255f, 30 / 255f, 20 / 255f, 0.3f), new Color(0, 0, 0, 0), (r - 0.6f) / 0.4f) : new Color(0, 0, 0, 0);
                    px[y * S + x] = c;
                }
            var rng = new System.Random(7);
            for (int i = 0; i < 14; i++)
            {
                float cx = 32 + (float)(rng.NextDouble() - 0.5) * 30, cy = 32 + (float)(rng.NextDouble() - 0.5) * 30, rr = 1 + (float)rng.NextDouble() * 3, al = 0.2f + (float)rng.NextDouble() * 0.3f;
                for (int y = Mathf.Max(0, (int)(cy - rr)); y < Mathf.Min(S, (int)(cy + rr) + 1); y++)
                    for (int x = Mathf.Max(0, (int)(cx - rr)); x < Mathf.Min(S, (int)(cx + rr) + 1); x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr * rr) { var c = px[y * S + x]; px[y * S + x] = new Color(c.r * (1 - al), c.g * (1 - al), c.b * (1 - al), c.a + (1 - c.a) * al); }
            }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
