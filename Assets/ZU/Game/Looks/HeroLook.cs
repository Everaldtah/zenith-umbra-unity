// The hero look in the body shader (port of the TS CharacterView look: lookUniforms / applySkin / analysePalette / addLook,
// the bind-pose neck line from loadReal, and smear()). The body's URP Lit materials are swapped for copies on ZU/Hero (Lit
// plus the look, Shaders/ZUHeroLook.hlsl), each body mesh for a copy carrying its bind pose in uv3 (model space: the TS
// shader reads `position` there - the head line and the smear's streaks), and every frame the skin, the time and the smear
// go into the renderers' MaterialPropertyBlock (CharacterLook writes them with its rim values; a first-person viewmodel
// calls Apply). Model space = the prefab root's local space, metres: the TS GLB's mesh space with X mirrored.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Looks
{
    public sealed class HeroLook
    {
        /// <summary>speed (m/s) above which fast moves smear (Davis GDC17: stretch the mesh along its motion - automated smear frames)</summary>
        public const float SMEAR_FROM = 12;

        static Shader shader;
        static readonly Dictionary<Material, Material> heroMats = new Dictionary<Material, Material>();
        static readonly Dictionary<(Mesh, Matrix4x4), (Mesh mesh, float y0, float y1)> bindMeshes = new Dictionary<(Mesh, Matrix4x4), (Mesh, float, float)>();
        static readonly Dictionary<Texture, Vector4?> palettes = new Dictionary<Texture, Vector4?>();

        static readonly int ID_REMAP = Shader.PropertyToID("_ZuRemap"), ID_SRC1 = Shader.PropertyToID("_ZuSrc1"), ID_SRC2 = Shader.PropertyToID("_ZuSrc2"),
            ID_DST1 = Shader.PropertyToID("_ZuDst1"), ID_DST2 = Shader.PropertyToID("_ZuDst2"), ID_NEUTRAL = Shader.PropertyToID("_ZuNeutral"),
            ID_HAS1 = Shader.PropertyToID("_ZuHas1"), ID_HAS2 = Shader.PropertyToID("_ZuHas2"), ID_METAL = Shader.PropertyToID("_ZuMetal"),
            ID_KEEPSKIN = Shader.PropertyToID("_ZuKeepSkin"), ID_HEADY = Shader.PropertyToID("_ZuHeadY"), ID_HEADBAND = Shader.PropertyToID("_ZuHeadBand"),
            ID_GLOW = Shader.PropertyToID("_ZuGlow"), ID_PATTERN = Shader.PropertyToID("_ZuPattern"), ID_PATCOL = Shader.PropertyToID("_ZuPatternColor"),
            ID_TIME = Shader.PropertyToID("_ZuTime"), ID_SMEAR = Shader.PropertyToID("_ZuSmear");

        readonly Renderer[] rends;
        /// <summary>per renderer: 0 = keeps its own paint whatever the skin (a part that couldn't take the bind pose, above the neck)</summary>
        readonly float[] remapMask;
        readonly bool skinned;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        /// <summary>first-person viewmodels don't smear (the camera rides the motion)</summary>
        public bool noSmear;
        /// <summary>the skin shown now (TS CharacterView.skin)</summary>
        public string SkinId { get; private set; }
        /// <summary>the costume's two hues as measured (x, y = the first [hue, value], z, w the second: TS zuSrc1 / zuSrc2) and the
        /// neck line (model space; 1e9 = none) - for checks against the TS</summary>
        public Vector4 SrcHues => new Vector4(src1.x, src1.y, src2.x, src2.y);
        public float HeadY => headY;
        HeroSkin.Values skin = HeroSkin.Values.Classic;
        Vector4 src1 = new Vector4(0, 0.5f), src2 = new Vector4(0.5f, 0.5f);      // TS lookUniforms defaults
        float headY = 1e9f, headBand = 0.01f, time;
        Vector3 smear;

        /// <param name="model">the prefab root (model space)</param>
        /// <param name="body">the body's renderers (not its props); their materials and meshes are swapped here</param>
        /// <param name="bindPose">bake the bind pose into the meshes (a first-person viewmodel's arms-only meshes skip it: no
        /// head to keep, no smear)</param>
        public HeroLook(Transform model, IList<Renderer> body, Actor a, bool bindPose = true)
        {
            rends = new Renderer[body.Count]; remapMask = new float[body.Count];
            float y0 = float.PositiveInfinity, y1 = float.NegativeInfinity;
            for (int i = 0; i < body.Count; i++)
            {
                var r = rends[i] = body[i]; remapMask[i] = 1;
                if (r == null) continue;
                r.sharedMaterials = HeroMaterials(r.sharedMaterials);
                if (r is SkinnedMeshRenderer) skinned = true;
                if (!bindPose || model == null) continue;
                var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                var M = model.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var b = BindMesh(mesh, M);
                if (b.mesh != null)
                {
                    if (r is SkinnedMeshRenderer s2) s2.sharedMesh = b.mesh; else r.GetComponent<MeshFilter>().sharedMesh = b.mesh;
                    y0 = Mathf.Min(y0, b.y0); y1 = Mathf.Max(y1, b.y1);
                }
                else remapMask[i] = -1;          // decided once the neck line is known
            }
            // bind-pose neck line (model space): skins recolour the costume, never the hair or face (mechs: the whole frame)
            if (bindPose && model != null && a != null && a.def.frame != "mech" && !float.IsInfinity(y0))
            {
                // the chin sits about halfway between the neck and head joints (tall hair makes a bounding-box ratio useless)
                var nk = Find(model, "neck"); var hd = Find(model, "head");
                headY = nk != null && hd != null ? Y(model, nk) + (Y(model, hd) - Y(model, nk)) * 0.35f : y0 + (y1 - y0) * 0.8f;
                headBand = (y1 - y0) * 0.012f;
            }
            for (int i = 0; i < rends.Length; i++)
                if (remapMask[i] < 0) remapMask[i] = rends[i] != null && model != null && model.InverseTransformPoint(rends[i].bounds.center).y > headY ? 0 : 1;
            // the costume's own hues, for skin palette remaps (the first material with a base map)
            foreach (var r in rends)
            {
                if (r == null) continue;
                Texture map = null;
                foreach (var m in r.sharedMaterials) if (m != null && m.HasProperty("_BaseMap") && (map = m.GetTexture("_BaseMap")) != null) break;
                if (map == null) continue;
                var pal = Palette(map);
                if (pal.HasValue) { src1 = new Vector4(pal.Value.x, pal.Value.y); src2 = new Vector4(pal.Value.z, pal.Value.w); }
                break;
            }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        static float Y(Transform model, Transform t) => model.InverseTransformPoint(t.position).y;

        /// <summary>each frame: the skin (TS setSkin when it changes), the clock the energy lines run on, the smear</summary>
        public void Update(Actor a, double t)
        {
            string id = HeroSkin.Of(a);
            if (id != SkinId) { SkinId = id; skin = HeroSkin.ValuesOf(HeroSkin.Find(a.def.id, id)); }
            time = (float)t;
            smear = Smear(a, t);
        }

        /// <summary>TS CharacterView.smear: the trailing surfaces are dragged back along the velocity (world space here; the
        /// shader takes it into the mesh's space as the TS does with the inverse matrix)</summary>
        Vector3 Smear(Actor a, double t)
        {
            double sp = System.Math.Sqrt(a.vel.x * a.vel.x + a.vel.y * a.vel.y + a.vel.z * a.vel.z);
            // (a burst effect for dashes: sustained speed - Hibiki deep in the Groove - would streak the whole body)
            if (noSmear || sp < SMEAR_FROM || !a.alive || a.Has("rhythm", t) || !skinned) return Vector3.zero;
            double amt = System.Math.Min(0.28, (sp - SMEAR_FROM) / 14 * 0.28);          // metres of drag at the trailing edge (subtle: a hint, not a tear)
            return Conv.U(-a.vel.x * amt / sp, -a.vel.y * amt / sp, -a.vel.z * amt / sp);
        }

        /// <summary>the look's values for body renderer `i` into a block the caller then sets on it</summary>
        public void Write(MaterialPropertyBlock b, int i)
        {
            b.SetFloat(ID_REMAP, skin.remap * (i >= 0 && i < remapMask.Length ? remapMask[i] : 1));
            b.SetVector(ID_SRC1, src1); b.SetVector(ID_SRC2, src2);
            b.SetVector(ID_DST1, skin.dst1); b.SetVector(ID_DST2, skin.dst2); b.SetVector(ID_NEUTRAL, skin.neutral);
            b.SetFloat(ID_HAS1, skin.has1); b.SetFloat(ID_HAS2, skin.has2); b.SetFloat(ID_METAL, skin.metal); b.SetFloat(ID_KEEPSKIN, 1);
            b.SetFloat(ID_HEADY, headY); b.SetFloat(ID_HEADBAND, headBand);
            b.SetFloat(ID_GLOW, skin.glow); b.SetFloat(ID_PATTERN, skin.pattern); b.SetVector(ID_PATCOL, skin.patternColor);
            b.SetFloat(ID_TIME, time); b.SetVector(ID_SMEAR, smear);
        }

        /// <summary>for an owner without its own block (a first-person viewmodel): the values straight onto the renderers</summary>
        public void Apply()
        {
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i]; if (r == null) continue;
                r.GetPropertyBlock(mpb); Write(mpb, i); r.SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------------------------------------- materials, meshes

        /// <summary>the body's URP Lit materials as ZU/Hero copies (one per source material, shared by every instance: the
        /// per-hero values ride the property block); anything else is left as it is</summary>
        public static Material[] HeroMaterials(Material[] src)
        {
            var o = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) o[i] = HeroMaterial(src[i]);
            return o;
        }

        public static Material HeroMaterial(Material src)
        {
            if (src == null || src.shader == null || src.shader.name != "Universal Render Pipeline/Lit") return src;
            if (heroMats.TryGetValue(src, out var m) && m != null) return m;
            shader ??= Resources.Load<Material>("ZULooks/Keep/hero_ms_n")?.shader ?? Shader.Find("ZU/Hero");
            if (shader == null) return src;
            m = new Material(shader) { name = src.name + " (hero)" };
            // property by property and keyword by name: a cross-shader CopyPropertiesFromMaterial carries the source's keyword
            // space along (the keyword-size mismatch errors)
            var sh = src.shader;
            for (int p = 0; p < sh.GetPropertyCount(); p++)
            {
                string n = sh.GetPropertyName(p);
                if (!m.HasProperty(n)) continue;
                switch (sh.GetPropertyType(p))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: m.SetColor(n, src.GetColor(n)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: m.SetVector(n, src.GetVector(n)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float: case UnityEngine.Rendering.ShaderPropertyType.Range: m.SetFloat(n, src.GetFloat(n)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int: m.SetInteger(n, src.GetInteger(n)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: m.SetTexture(n, src.GetTexture(n)); m.SetTextureScale(n, src.GetTextureScale(n)); m.SetTextureOffset(n, src.GetTextureOffset(n)); break;
                }
            }
            foreach (var k in src.enabledKeywords) m.EnableKeyword(k.name);
            m.renderQueue = src.renderQueue;
            m.SetOverrideTag("RenderType", src.GetTag("RenderType", false, "Opaque"));
            foreach (var pass in new[] { "MotionVectors", "DepthOnly", "DepthNormals", "ShadowCaster" }) m.SetShaderPassEnabled(pass, src.GetShaderPassEnabled(pass));
            m.enableInstancing = src.enableInstancing; m.doubleSidedGI = src.doubleSidedGI;
            heroMats[src] = m;
            return m;
        }

        /// <summary>a copy of a body mesh with its bind pose (model space) in uv3, and that pose's height range; cached per
        /// mesh and placement (every instance of a prefab shares one). (null when the mesh can't be read)</summary>
        static (Mesh mesh, float y0, float y1) BindMesh(Mesh src, Matrix4x4 toModel)
        {
            if (src == null) return (null, 0, 0);
            // (a cached copy that was destroyed - an Editor play session ended, the static cache lived on - is made again:
            // handing it back left the hero with no neck line, so a skin recoloured the hair and face in the next session)
            if (bindMeshes.TryGetValue((src, toModel), out var have) && (have.mesh != null || !src.isReadable)) return have;
            if (!src.isReadable) { bindMeshes[(src, toModel)] = (null, 0, 0); return (null, 0, 0); }
            var v = src.vertices; var b = new Vector3[v.Length];
            float y0 = float.PositiveInfinity, y1 = float.NegativeInfinity;
            for (int i = 0; i < v.Length; i++) { var p = toModel.MultiplyPoint3x4(v[i]); b[i] = p; if (p.y < y0) y0 = p.y; if (p.y > y1) y1 = p.y; }
            var m = Object.Instantiate(src); m.name = src.name + " (bind)";
            m.SetUVs(3, b);
            m.UploadMeshData(true);          // (the copy is only drawn: its CPU side goes)
            return bindMeshes[(src, toModel)] = (m, y0, y1);
        }

        // ------------------------------------------------------------------------------------------- the costume's hues

        /// <summary>
        /// TS analysePalette: the costume's two dominant hues (and their mean brightness), measured from the base-colour texture
        /// so skins can remap them: saturated texels only, skin tones excluded. (x, y) = the first [hue, value], (z, w) the
        /// second, in 0..1 (linear value); null when nothing saturated was found. The texture is drawn down to 96 x 96 on the
        /// GPU (the TS draws it into a 96 x 96 canvas), so an unreadable texture works too; cached per texture. A hero texture
        /// takes the web game's own numbers from the baked table first (see Baked).
        /// </summary>
        public static Vector4? Palette(Texture tex)
        {
            if (tex == null || tex.width == 0) return null;
            if (palettes.TryGetValue(tex, out var have)) return have;
            if (UseBakedPalettes && Baked().TryGetValue(tex.name, out var bv)) return palettes[tex] = bv;
            const int N = 96;
            Color32[] px;
            var rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var t2 = new Texture2D(N, N, TextureFormat.RGBA32, false, false);
                t2.ReadPixels(new Rect(0, 0, N, N), 0, 0, false); t2.Apply(false);
                px = t2.GetPixels32();
                if (Application.isPlaying) Object.Destroy(t2); else Object.DestroyImmediate(t2);     // (the editor's look preview)
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
            return palettes[tex] = Analyse(px);
        }

        /// <summary>off switch: false = every palette measured on the GPU as before, the baked table ignored</summary>
        public static bool UseBakedPalettes = true;
        static Dictionary<string, Vector4> baked;

        /// <summary>
        /// the palettes the web game measures, per texture name (Resources/ZULooks/palettes.txt, made by
        /// tools/looks/bake_palettes.py from the full-size images, each model's base maps given its LOD1 = web texture's).
        /// The GPU measure above can't match them: the player only has the imported texture (2048 in a Windows build) and
        /// the blit samples its mipmaps, which average the costume texels down - the hues come out the same, the mean value
        /// (the recolour's brightness normaliser) about 40 % low, so a skin's main colour drew up to 1.6 x brighter than the TS.
        /// </summary>
        static Dictionary<string, Vector4> Baked()
        {
            if (baked != null) return baked;
            baked = new Dictionary<string, Vector4>();
            var t = Resources.Load<TextAsset>("ZULooks/palettes");
            if (t == null) return baked;
            var inv = System.Globalization.CultureInfo.InvariantCulture; var fl = System.Globalization.NumberStyles.Float;
            foreach (var line in t.text.Split('\n'))
            {
                var f = line.Trim().Split(' ');
                if (f.Length != 5 || f[0].StartsWith("#")) continue;
                if (float.TryParse(f[1], fl, inv, out var x) && float.TryParse(f[2], fl, inv, out var y)
                    && float.TryParse(f[3], fl, inv, out var z) && float.TryParse(f[4], fl, inv, out var w))
                    baked[f[0]] = new Vector4(x, y, z, w);
            }
            Resources.UnloadAsset(t);
            return baked;
        }

        /// <summary>the histogram half of analysePalette over 8-bit sRGB texels (the canvas's bytes), number for number</summary>
        public static Vector4? Analyse(Color32[] px)
        {
            const int B = 36;
            var wsum = new double[B]; var vsum = new double[B];
            foreach (var p in px)
            {
                double r = p.r / 255.0, gg = p.g / 255.0, b = p.b / 255.0; int a = p.a;
                if (a < 128) continue;
                double mx = System.Math.Max(r, System.Math.Max(gg, b)), mn = System.Math.Min(r, System.Math.Min(gg, b)), d = mx - mn, s = mx > 0 ? d / mx : 0;
                if (s < 0.22 || mx < 0.12) continue;
                double h = mx == r ? ((gg - b) / d + 6) % 6 : mx == gg ? (b - r) / d + 2 : (r - gg) / d + 4; h /= 6;
                if (h > 0.0 && h < 0.11 && s < 0.6 && mx > 0.3) continue;     // skin tones
                int k = System.Math.Min(B - 1, (int)System.Math.Floor(h * B)); double w = s * mx;
                wsum[k] += w; vsum[k] += w * System.Math.Pow(mx, 2.2);
            }
            double Smooth(int k) => wsum[(k + B - 1) % B] * 0.5 + wsum[k] + wsum[(k + 1) % B] * 0.5;
            int b1 = 0; for (int k = 1; k < B; k++) if (Smooth(k) > Smooth(b1)) b1 = k;
            int b2 = -1; for (int k = 0; k < B; k++) { int dd = System.Math.Min(System.Math.Abs(k - b1), B - System.Math.Abs(k - b1)); if (dd >= 4 && (b2 < 0 || Smooth(k) > Smooth(b2))) b2 = k; }
            if (wsum[b1] == 0) return null;
            Vector2 Hv(int k) => new Vector2((float)((k + 0.5) / B), (float)(wsum[k] != 0 ? vsum[k] / wsum[k] : 0.5));
            var h1 = Hv(b1); var h2 = b2 >= 0 && wsum[b2] > wsum[b1] * 0.08 ? Hv(b2) : Hv((b1 + B / 2) % B);
            return new Vector4(h1.x, h1.y, h2.x, h2.y);
        }
    }
}
