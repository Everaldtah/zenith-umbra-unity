// What a hero's body shows beyond its pose (port of the TS CharacterView's look): the team rim light (allies blue and
// faint, enemies red, marked enemies bright; Tomoe's Warpath mark turns anyone it cut blue and pulsing; spawn protection
// flickers), stealth (allies see a ghost, enemies a faint shimmer or nearly nothing), Mirei's Stellar Rebirth (a golden
// translucent figure for the guard), a summoned hologram (lit from within in its colour, rising in, fading out), the
// shield bubble and Tenkai-Oh's Solar Bulwark.
// The rim is an extra additive pass (ZU/HeroRim) on each body renderer: an extra material slot re-renders the mesh. The
// translucent looks swap the body to transparent copies of its own URP Lit materials (the variants are kept in the build by
// Resources/ZUFx/Keep). The bubble and the barrier hang off the match root, so the body's squash and tilt don't move them.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Sim;

namespace ZU.Game
{
    public sealed class CharacterLook
    {
        /// <summary>friendly / enemy outline colours (the TS UI_COLORS; Settings > Accessibility can recolour them)</summary>
        public static Color Ally = Conv.Hex("#5cc8ff"), Enemy = Conv.Hex("#ff3b5c");
        static readonly Color TIDE_BLUE = Conv.Hex("#3fa9ff"), GOLD = Conv.Hex("#ffe9a8");
        static Material rimMat, barrierMat; static Mesh barrierMesh;

        enum Mode { Opaque, Stealth, Reborn, Holo }
        readonly Renderer[] body;
        readonly Material[][] opaque;           // each renderer's own materials (+ the rim pass)
        Material[][] clear;                     // transparent copies, made the first time a translucent look is needed
        Mode mode = Mode.Opaque;
        /// <summary>how opaque the body is drawn this frame (1 solid; stealth / rebirth / hologram less)</summary>
        public float Alpha { get; private set; } = 1;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly Transform shield, barrier; readonly MeshRenderer shieldR, barrierR;
        readonly Color holoCol; readonly bool holo;
        readonly Transform[] jets; readonly MeshRenderer[] jetR;    // foot thrusters (heroes with jets: Tenkai-Oh)

        public CharacterLook(Transform matchRoot, IEnumerable<Renderer> bodyRenderers, Actor a, Material additive)
        {
            rimMat ??= Resources.Load<Material>("ZUFx/rim") ?? new Material(Shader.Find("ZU/HeroRim"));
            body = bodyRenderers.Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();
            opaque = new Material[body.Length][];
            for (int i = 0; i < body.Length; i++)
            {
                var own = body[i].sharedMaterials;
                opaque[i] = own.Concat(new[] { rimMat }).ToArray();
                body[i].sharedMaterials = opaque[i];
            }
            holo = !string.IsNullOrEmpty(a.def.holo);
            holoCol = holo ? Conv.Hex(a.def.holo, Conv.Hex(a.def.glow)) : Color.white;
            if (additive != null)
            {
                shield = Part("shield bubble", matchRoot, ProjectileViews.Sphere(), additive, out shieldR);
                shield.gameObject.SetActive(false);
            }
            if ((a.def.jets ?? 0) != 0 && additive != null)
            {
                jets = new Transform[2]; jetR = new MeshRenderer[2];
                for (int i = 0; i < 2; i++) { jets[i] = Part("jet", matchRoot, Fx.FxKit.ConeMesh, additive, out jetR[i]); jets[i].gameObject.SetActive(false); }
            }
            if (a.barrier.max > 0)
            {
                barrierMat ??= Resources.Load<Material>("ZUFx/barrier") ?? new Material(Shader.Find("ZU/Barrier"));
                barrierMesh ??= Arc(3.2f, 3.8f, 24, -0.72f, 1.44f);
                barrier = Part("solar bulwark", matchRoot, barrierMesh, barrierMat, out barrierR);
                barrier.gameObject.SetActive(false);
            }
        }

        static Transform Part(string name, Transform parent, Mesh mesh, Material m, out MeshRenderer mr)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return g.transform;
        }

        /// <summary>the TS CylinderGeometry(r, r, h, segs, 1, open, thetaStart, thetaLength): an open arc round +Z (x = r sin, z = r cos)</summary>
        static Mesh Arc(float r, float h, int segs, float t0, float tl)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float u = (float)i / segs, th = t0 + u * tl;
                var d = new Vector3(-Mathf.Sin(th) * r, 0, Mathf.Cos(th) * r);       // (sim x mirrored)
                v.Add(d + Vector3.up * (h / 2)); uv.Add(new Vector2(u, 1));
                v.Add(d - Vector3.up * (h / 2)); uv.Add(new Vector2(u, 0));
                if (i < segs) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "barrier arc" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        public void Dispose()
        {
            if (shield != null) Object.Destroy(shield.gameObject); if (barrier != null) Object.Destroy(barrier.gameObject);
            if (jets != null) foreach (var j in jets) if (j != null) Object.Destroy(j.gameObject);
        }

        /// <summary>foot thrusters (TS CharacterView jets): flame cones under the soles while flying, base at the sole and the tip
        /// pointing down, flickering, longer with jump held</summary>
        public void Jets(Actor a, Transform footL, Transform footR, bool shown)
        {
            if (jets == null) return;
            bool on = shown && a.alive && a.flying && (a.def.jets ?? 0) != 0;
            for (int i = 0; i < 2; i++)
            {
                var b = i == 0 ? footL : footR;
                jets[i].gameObject.SetActive(on && b != null);
                if (!on || b == null) continue;
                float s = (float)a.scale, k = s * (0.9f + Random.value * 0.25f) * (a.input.jumpHeld ? 1.5f : 1) * 1.1f;
                // ConeGeometry(0.16, 1) with its base at the sole: the cone's apex k below it, +Z (apex -> base) pointing up
                jets[i].SetPositionAndRotation(b.position + Vector3.down * k, Quaternion.LookRotation(Vector3.up));
                jets[i].localScale = new Vector3(0.16f * s, 0.16f * s, k);
                jetR[i].GetPropertyBlock(mpb); var c = Conv.Hex("#ffb347"); c.a = 0.75f; mpb.SetColor("_BaseColor", c); jetR[i].SetPropertyBlock(mpb);
            }
        }

        /// <summary>each frame after the pose: `shown` = the body is drawn at all (not the local player in first person)</summary>
        public void Update(Actor a, IViewHost host, double t, Vector3 drawPos, bool shown)
        {
            float time = (float)t;
            var me = host?.Player;
            string viewer = me != null ? me.team : "zenith";
            bool ally = a.team == viewer;
            // ---- the body's look
            float rim, alpha = 1;
            var rimCol = ally ? Ally : Enemy;
            Mode want = Mode.Opaque;
            if (!a.alive)
            {
                rim = 0;
                if (holo) { want = Mode.Holo; alpha = 0.82f * (1 - Mathf.Min(1, (float)(t - a.deathAt) / 0.45f)); }   // a summon dismissed fades where it stands
            }
            else
            {
                bool stealth = a.Has("stealth", t);
                bool seen = me == null || host is MatchRunner mr && mr.World != null && mr.World.Perceivable(me, a);
                bool reborn = a.Has("reborn", t) && !holo;
                rim = ally ? 0.25f : (a.Has("revealed", t) || a.Has("marked", t) ? 1.6f : 0.7f);
                if (reborn) { want = Mode.Reborn; rim = 1.4f + 0.4f * Mathf.Sin(time * 6); alpha = 0.6f; }
                if (holo)
                {
                    // rising out of the ground over the rise window, then a slow pulse of the glow
                    double ra = a.Sv("riseAt", t), ru = a.Sv("riseUntil", -1);
                    float rise = ru > 0 && t < ru ? Mathf.Max(0, (float)((t - ra) / System.Math.Max(0.01, ru - ra))) : 1;
                    float k = rise * rise * (3 - 2 * rise);
                    want = Mode.Holo; alpha = 0.82f * Mathf.Min(1, 0.2f + k);
                    rim = 1.6f + 0.6f * Mathf.Sin(time * 4); rimCol = Color.Lerp(holoCol, Color.white, 0.4f);
                }
                if (stealth && !holo && !reborn) { want = Mode.Stealth; alpha = ally ? 0.35f : seen ? 0.25f : 0.04f; }
                // cut by the Crescent Warpath ('tidemark'): blue while weakened - for both teams
                if (a.Has("tidemark", t)) { rimCol = TIDE_BLUE; rim = 2.1f + 0.5f * Mathf.Sin(time * 5); }
                if (a.Has("spawnprot", t)) rim = 1.2f + Mathf.Sin(time * 20) * 0.5f;
            }
            SetMode(want, alpha);
            Alpha = want == Mode.Opaque ? 1 : alpha;
            for (int i = 0; i < body.Length; i++)
            {
                var r = body[i]; if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_RimColor", rimCol); mpb.SetFloat("_Rim", rim * (want == Mode.Stealth ? alpha : 1)); mpb.SetFloat("_Fill", 0);
                r.SetPropertyBlock(mpb);
            }
            if (want != Mode.Opaque) foreach (var ms in clear) foreach (var m in ms) if (m != null && m != rimMat) { var c = m.GetColor("_BaseColor"); c.a = alpha; m.SetColor("_BaseColor", c); }
            // ---- shields bubble
            if (shield != null)
            {
                bool on = shown && a.alive && (a.ShieldAmt > 1 || a.Has("parry", t) || a.Has("undying", t));
                shield.gameObject.SetActive(on);
                if (on)
                {
                    float H = (float)a.Height, R = H * 0.62f;
                    shield.localScale = new Vector3(R * 0.8f, R, R * 0.8f);
                    shield.position = drawPos + Vector3.up * (H * 0.5f);
                    var c = Conv.Hex(a.Has("parry", t) ? "#8ad8ff" : a.shields.Any(s => s.kind == "void") ? "#ff2244" : a.Has("undying", t) ? "#ffe28a" : "#bfe8ff");
                    c.a = 0.12f + 0.06f * Mathf.Sin(time * 8);
                    shieldR.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); shieldR.SetPropertyBlock(mpb);
                }
            }
            // ---- Solar Bulwark: the arc centred so its face sits 1.7 m in front, growing with Tenkai-Oh's giant form
            if (barrier != null)
            {
                bool on = shown && a.alive && a.barrier.up;
                barrier.gameObject.SetActive(on);
                if (on)
                {
                    float s = (float)a.scale; var yaw = Conv.Yaw(a.yaw);
                    barrier.SetPositionAndRotation(drawPos + yaw * new Vector3(0, 1.9f * s, (1.7f - 3.2f) * s), yaw);
                    barrier.localScale = Vector3.one * s;
                    barrierR.GetPropertyBlock(mpb); mpb.SetColor("_Color", Conv.Hex(a.def.glow)); mpb.SetFloat("_Hp", (float)(a.barrier.hp / System.Math.Max(1, a.barrier.max))); barrierR.SetPropertyBlock(mpb);
                }
            }
        }

        /// <summary>opaque, or the body's transparent copies set up for a look (the TS m.transparent / opacity / color swaps)</summary>
        void SetMode(Mode m, float alpha)
        {
            if (m == mode) return;
            mode = m;
            if (m == Mode.Opaque) { for (int i = 0; i < body.Length; i++) if (body[i] != null) body[i].sharedMaterials = opaque[i]; return; }
            clear ??= opaque.Select(ms => ms.Select(x => x == rimMat || x == null ? x : new Material(x)).ToArray()).ToArray();
            for (int i = 0; i < body.Length; i++)
            {
                for (int k = 0; k < clear[i].Length; k++)
                {
                    var c = clear[i][k]; var src = opaque[i][k];
                    if (c == null || c == rimMat) continue;
                    c.CopyPropertiesFromMaterial(src); c.shaderKeywords = src.shaderKeywords;
                    // TS: reborn / hologram keep writing depth (a solid-enough figure), stealth doesn't
                    Transparent(c, m != Mode.Stealth);
                    if (m == Mode.Reborn) { c.SetColor("_BaseColor", GOLD); c.EnableKeyword("_EMISSION"); c.SetColor("_EmissionColor", GOLD * 0.7f); }
                    else if (m == Mode.Holo)
                    {
                        c.SetColor("_BaseColor", Color.Lerp(holoCol, Color.white, 0.3f)); c.EnableKeyword("_EMISSION"); c.SetColor("_EmissionColor", holoCol * 0.85f);
                        c.SetFloat("_Metallic", 0); c.SetFloat("_Smoothness", 0.4f); c.DisableKeyword("_METALLICSPECGLOSSMAP");
                    }
                }
                if (body[i] != null) body[i].sharedMaterials = clear[i];
            }
        }

        /// <summary>a URP Lit material switched to alpha blending at runtime</summary>
        static void Transparent(Material m, bool zwrite)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", zwrite ? 1 : 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
