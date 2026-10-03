// Training Grounds dressing (desktop edition), port of src/render/TrainingScene.ts: the Hero Range lane (firing line,
// distance marks, the target's post, its console) and the Spar Arena (a floor outline and corner posts while open; walls
// and a roof of blue holographic grid - ZU/Holo - that rise and seal the box while a spar is on). The TS paints its
// words on canvas cards; here they are glyph quads cut from the game's fonts (ZU/WorldText). Built by MatchRunner when
// the match has a Hero Range; everything is placed in simulation coordinates through Conv.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.Env
{
    public sealed class TrainingScene : MonoBehaviour
    {
        static readonly Color BLUE = Conv.Hex("#3fa9ff"), WIN = Conv.Hex("#58ffb0"), LOSE = Conv.Hex("#ff5d6d");

        MatchRunner runner;
        HeroRange range;
        Spar spar;
        Transform post;
        readonly List<Transform> walls = new List<Transform>();
        Transform roof;
        readonly List<Material> holo = new List<Material>();
        readonly List<Transform> posts = new List<Transform>();
        Material outline;
        float seal = -1;
        // the words: rebuilt when the font atlas is (a dynamic font re-packs its glyphs as new sizes / characters arrive)
        readonly List<(Mesh mesh, Font font, string text, float em, float maxW, float y, Material mat)> words = new List<(Mesh, Font, string, float, float, float, Material)>();

        public static TrainingScene Build(MatchRunner r)
        {
            var m = r.Match;
            if (m?.range == null) return null;
            var go = new GameObject("Training Grounds"); go.transform.SetParent(r.transform, false);
            var t = go.AddComponent<TrainingScene>();
            t.runner = r; t.range = m.range; t.spar = m.spar;
            // dressing only: a shader a build stripped (new Material(null) throws) costs the lane marks / holo box, never the match
            try { t.Make(); }
            catch (System.Exception e) { Debug.LogException(e); Destroy(go); return null; }
            return t;
        }

        // ------------------------------------------------------------------ materials and parts
        static Material Glow(Color c, float opacity = 1)
        {
            var sh = Shader.Find("ZU/FxAlpha");
            var m = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit"));
            c.a = opacity; m.SetColor("_BaseColor", c);
            return m;
        }
        static Material Lit(Color c, float smooth, float metal)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }
        static Material Holo()
        {
            var src = Resources.Load<Material>("ZUFx/holo");
            return src != null ? new Material(src) : new Material(Shader.Find("ZU/Holo"));
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = pos; return t;
        }
        static Transform Part(string name, Transform parent, Mesh mesh, Material m, Vector3 pos, bool shadows = false)
        {
            var t = Node(name, parent, pos);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return t;
        }
        /// <summary>a box w x h x d (sim metres) centred at a sim point</summary>
        Transform Box(string name, Transform parent, double x, double y, double z, float w, float h, float d, Material m, bool shadows = false)
        {
            var t = Part(name, parent, MeshKit.Box(), m, Conv.U(x, y, z), shadows);
            t.localScale = new Vector3(w, h, d);
            return t;
        }

        /// <summary>a quad through four sim-space corners (uv 0,0 / 1,0 / 1,1 / 0,1 in that order)</summary>
        static Mesh Quad(V3 a, V3 b, V3 c, V3 d)
        {
            var m = new Mesh { name = "quad" };
            m.SetVertices(new List<Vector3> { Conv.U(a), Conv.U(b), Conv.U(c), Conv.U(d) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return MeshKit.White(m);
        }

        /// <summary>a rotation that lays a card (local +X = reading direction, +Y = the top of the words) along these sim
        /// directions - the words read the same way they do in the TS scene</summary>
        static Quaternion Face(V3 read, V3 up)
        {
            var r = Conv.U(read).normalized; var u = Conv.U(up).normalized;
            return Quaternion.LookRotation(Vector3.Cross(r, u), u);
        }

        // ------------------------------------------------------------------ words (the TS `label` canvas cards)
        static Font orbitron, rajdhani;
        const int PX = 64;          // glyph size in the font atlas

        /// <summary>glyph quads for `text`, `em` metres tall, centred on x = 0 with the glyphs' middle at y (shrunk to fit maxW)</summary>
        static void Glyphs(Mesh mesh, Font f, string text, float em, float maxW, float y)
        {
            mesh.Clear();
            if (f == null || string.IsNullOrEmpty(text)) return;
            f.RequestCharactersInTexture(text, PX, FontStyle.Normal);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float x = 0, lo = float.MaxValue, hi = float.MinValue;
            foreach (char c in text)
            {
                if (!f.GetCharacterInfo(c, out var ci, PX, FontStyle.Normal)) continue;
                int i = v.Count;
                v.Add(new Vector3(x + ci.minX, ci.minY, 0)); uv.Add(ci.uvBottomLeft);
                v.Add(new Vector3(x + ci.minX, ci.maxY, 0)); uv.Add(ci.uvTopLeft);
                v.Add(new Vector3(x + ci.maxX, ci.maxY, 0)); uv.Add(ci.uvTopRight);
                v.Add(new Vector3(x + ci.maxX, ci.minY, 0)); uv.Add(ci.uvBottomRight);
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                if (c != ' ') { lo = Mathf.Min(lo, ci.minY); hi = Mathf.Max(hi, ci.maxY); }
                x += ci.advance;
            }
            if (v.Count == 0) return;
            float k = em / PX;
            if (x * k > maxW) k = maxW / x;
            float mid = (lo + hi) / 2;
            for (int i = 0; i < v.Count; i++) v[i] = new Vector3((v[i].x - x / 2) * k, (v[i].y - mid) * k + y, 0);
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
        }

        void Words(Transform card, Font f, string text, float em, float maxW, float y, Color color)
        {
            var src = Resources.Load<Material>("ZUFx/worldtext");
            var m = src != null ? new Material(src) : new Material(Shader.Find("ZU/WorldText"));
            m.SetColor("_BaseColor", color);
            if (f != null) m.SetTexture("_MainTex", f.material.mainTexture);
            m.renderQueue = 3001;                   // over the card's backing
            var mesh = new Mesh { name = "words " + text };
            Glyphs(mesh, f, text, em, maxW, y);
            Part("words", card, mesh, m, Vector3.zero);
            words.Add((mesh, f, text, em, maxW, y, m));
        }

        void OnFontRebuilt(Font f)
        {
            foreach (var w in words)
                if (w.font == f) { Glyphs(w.mesh, w.font, w.text, w.em, w.maxW, w.y); w.mat.SetTexture("_MainTex", f.material.mainTexture); }
        }

        /// <summary>a word card w x h metres (TS label): the title, an optional line under it, an optional backing; laid along
        /// the sim directions `read` / `up` at a sim point</summary>
        Transform Label(Transform parent, string text, float w, float h, Color color, V3 at, V3 read, V3 up, string bg = null, string sub = null, bool local = false)
        {
            var card = Node("label " + text, parent, local ? new Vector3((float)-at.x, (float)at.y, (float)at.z) : Conv.U(at));
            card.localRotation = Face(read, up);
            if (bg != null)
            {
                var b = Glow(Conv.Hex(bg.Substring(0, 7)), bg.Length > 7 ? System.Convert.ToInt32(bg.Substring(7, 2), 16) / 255f : 1);
                b.renderQueue = 3000;
                var q = new Mesh { name = "card" };
                q.SetVertices(new List<Vector3> { new Vector3(-w / 2, -h / 2, 0.002f), new Vector3(w / 2, -h / 2, 0.002f), new Vector3(w / 2, h / 2, 0.002f), new Vector3(-w / 2, h / 2, 0.002f) });
                q.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); q.RecalculateNormals(); q.RecalculateBounds();
                Part("card", card, MeshKit.White(q), b, Vector3.zero);
            }
            // (canvas: the title at 52% down the card - 40% with a line under it, which sits at 76%)
            Words(card, orbitron, text, 0.55f * h, 0.96f * w, (0.5f - (sub != null ? 0.4f : 0.52f)) * h, color);
            if (sub != null) Words(card, rajdhani, sub, 0.2f * h, 0.96f * w, (0.5f - 0.76f) * h, new Color(color.r, color.g, color.b, 0.8f));
            return card;
        }

        /// <summary>a console: a dark pedestal, an angled lit screen with the station's name, a ring on the floor</summary>
        void ConsoleAt(Transform parent, double x, double z, double yaw, string title, string sub, string color)
        {
            var g = Node("Console " + title, parent, Conv.U(x, 0, z)); g.localRotation = Conv.Yaw(yaw);
            var c = Conv.Hex(color);
            Part("body", g, MeshKit.Box(), Lit(Conv.Hex("#232733"), 0.55f, 0.65f), new Vector3(0, 0.525f, 0), true).localScale = new Vector3(0.9f, 1.05f, 0.55f);
            // the screen tilted back 0.55 rad (TS rotation.x = -0.55): it faces up and out of the console's front (+z)
            Label(g, title, 1.1f, 0.62f, c, new V3(0, 1.22, 0.05), new V3(1, 0, 0), new V3(0, System.Math.Cos(0.55), -System.Math.Sin(0.55)), "#060e1aeb", sub, local: true);
            Part("ring", g, MeshKit.Annulus(1.05f, 1.15f, 40, 360), Glow(c, 0.6f), new Vector3(0, 0.02f, 0));
            Part("strip", g, MeshKit.Box(), Glow(c), new Vector3(0, 0.9f, 0)).localScale = new Vector3(0.92f, 0.05f, 0.57f);
        }

        // ------------------------------------------------------------------ build
        void Make()
        {
            orbitron ??= Resources.Load<Font>("ZUUI/Fonts/Orbitron-800");
            rajdhani ??= Resources.Load<Font>("ZUUI/Fonts/Rajdhani-600");
            Font.textureRebuilt += OnFontRebuilt;
            var L = HeroRange.LANE;
            // ---------------- Hero Range lane: firing line across, a mark + number every distance, the post ring
            var lane = Node("Hero Range", transform, Vector3.zero);
            void Across(double x, float w, string color, float o = 1) => Box("mark", lane, x, 0.015, L.z, w, 0.02f, 8.4f, Glow(Conv.Hex(color), o));
            Across(L.x0, 0.14f, "#ffd76a");
            // floor words read across the lane (+z), their tops down-range (+x): read from the firing line
            V3 flat = new V3(0, 0, 1), downRange = new V3(1, 0, 0);
            Label(lane, "FIRING LINE", 3.2f, 0.5f, Conv.Hex("#ffd76a"), new V3(L.x0 - 0.5, 0.03, L.z), flat, downRange);
            foreach (var d in L.dists)
            {
                Across(L.x0 + d, 0.06f, "#9fe0ff", 0.55f);
                Label(lane, $"{d} M", 1.6f, 0.6f, Conv.Hex("#cfefff"), new V3(L.x0 + d, 0.03, L.z - 4.9), flat, downRange);
            }
            // the side rails of the lane
            double far = L.dists[L.dists.Length - 1] + 2;
            foreach (var s in new[] { -1, 1 }) Box("rail", lane, L.x0 + far / 2 - 1, 0.015, L.z + s * 4.2, (float)far, 0.02f, 0.06f, Glow(Conv.Hex("#9fe0ff"), 0.4f));
            // TS-PARITY: the TS builds a "HERO RANGE · G · choose a hero · attack or defense" sign over the lane but never adds
            // it to the scene (its lane.add sits inside a trailing comment) - so no sign here either
            post = Node("post", lane, Vector3.zero);
            Part("ring", post, MeshKit.Annulus(0.85f, 1.0f, 40, 360), Glow(Conv.Hex("#ff5d6d"), 0.85f), new Vector3(0, 0.025f, 0));
            Part("disc", post, MeshKit.Annulus(0, 0.85f, 40, 360), Glow(Conv.Hex("#ff5d6d"), 0.12f), new Vector3(0, 0.022f, 0));
            ConsoleAt(lane, L.console.x, L.console.z, -0.75, "HERO RANGE", "press G", "#9fe0ff");
            // ---------------- Spar Arena: outline, corner posts, holographic walls + roof (sealed), its console and sign
            var A = Spar.ARENA; double x0 = A.x - A.hx, x1 = A.x + A.hx, z0 = A.z - A.hz, z1 = A.z + A.hz;
            var arena = Node("Spar Arena", transform, Vector3.zero);
            outline = Glow(BLUE, 0.9f);
            foreach (var (x, z, w, d) in new[] { (A.x, z0, A.hx * 2, 0.12), (A.x, z1, A.hx * 2, 0.12), (x0, A.z, 0.12, A.hz * 2), (x1, A.z, 0.12, A.hz * 2) })
                Box("edge", arena, x, 0.02, z, (float)w, 0.03f, (float)d, outline);
            Part("floor", arena, Quad(new V3(x0, 0.012, z0), new V3(x1, 0.012, z0), new V3(x1, 0.012, z1), new V3(x0, 0.012, z1)), Glow(BLUE, 0.05f), Vector3.zero);
            foreach (var (x, z) in new[] { (x0, z0), (x0, z1), (x1, z0), (x1, z1) })
                posts.Add(Box("post", arena, x, 0.5, z, 0.16f, 1, 0.16f, outline));
            // the walls stand on the floor (their origin): scaling y raises them
            void Wall(V3 a, V3 b, double w)
            {
                var m = Holo(); m.SetVector("_Size", new Vector4((float)w, (float)A.h, 0, 0)); holo.Add(m);
                var t = Part("wall", arena, Quad(a, b, new V3(b.x, A.h, b.z), new V3(a.x, A.h, a.z)), m, Vector3.zero);
                walls.Add(t);
            }
            Wall(new V3(x0, 0, z0), new V3(x1, 0, z0), A.hx * 2); Wall(new V3(x1, 0, z1), new V3(x0, 0, z1), A.hx * 2);
            Wall(new V3(x0, 0, z1), new V3(x0, 0, z0), A.hz * 2); Wall(new V3(x1, 0, z0), new V3(x1, 0, z1), A.hz * 2);
            var rm = Holo(); rm.SetVector("_Size", new Vector4((float)(A.hx * 2), (float)(A.hz * 2), 0, 0)); holo.Add(rm);
            roof = Part("roof", arena, Quad(new V3(x0, A.h, z0), new V3(x1, A.h, z0), new V3(x1, A.h, z1), new V3(x0, A.h, z1)), rm, Vector3.zero);
            // the sign over the south wall, facing out (-z): the TS card turned half round (rotation.y = PI)
            Label(arena, "SPAR ARENA", 5.2f, 1.1f, Conv.Hex("#7fd8ff"), new V3(A.x, 3.1, z0 - 0.05), new V3(-1, 0, 0), new V3(0, 1, 0), sub: "G · pick an opponent · walk in to fight");
            ConsoleAt(arena, Spar.SPAR_CONSOLE.x, Spar.SPAR_CONSOLE.z, System.Math.PI, "SPAR", "press G", "#7fd8ff");
            SetSeal(0);
        }

        void SetSeal(float k)
        {
            seal = k;
            foreach (var w in walls) w.localScale = new Vector3(1, Mathf.Max(0.03f, k), 1);
            roof.gameObject.SetActive(k > 0.98f);
            float h = (float)Spar.ARENA.h;
            foreach (var p in posts) { float sy = 1.2f + k * (h - 1.2f); p.localScale = new Vector3(0.16f, sy, 0.16f); var lp = p.localPosition; lp.y = sy / 2; p.localPosition = lp; }
        }

        void LateUpdate()
        {
            var w = runner != null ? runner.World : null; if (w == null) return;
            float time = (float)w.time, dt = Time.deltaTime;
            var r = range; var s = spar;
            if (r != null) { post.gameObject.SetActive(r.bot != null); post.localPosition = Conv.U(r.Post.x, 0, r.Post.z); }
            // the box rises in ~0.6 s when it seals and sinks back when the spar is over
            float want = s != null && s.Sealed ? 1 : 0;
            if (seal != want) SetSeal(want > seal ? Mathf.Min(1, seal + dt / 0.6f) : Mathf.Max(0, seal - dt / 0.8f));
            // its colour: blue while it fights; the winner's colour once the spar is decided
            var c = s?.phase == "done" ? (s.wins["you"] > s.wins["them"] ? WIN : LOSE) : BLUE;
            foreach (var m in holo) { m.SetFloat("_HoloTime", time); m.SetFloat("_On", 0.5f + seal * 0.5f); m.SetColor("_Color", c); }
            outline.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.9f));
        }

        void OnDestroy() => Font.textureRebuilt -= OnFontRebuilt;
    }
}
