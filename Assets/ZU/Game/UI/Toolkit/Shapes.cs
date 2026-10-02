// The CSS the PC game's look leans on that USS has no property for, drawn with Painter2D: skewX() boxes (the health bar,
// the ability tiles, the objective bars), clip-path polygons (the slanted buttons, role icons, the rank emblem's gem),
// the ult's conic-gradient ring, the canvas-drawn reticle and hit marker, the hurt vignette (an inset box-shadow).
// Colours come from the stylesheet as custom properties (--fill, --fill2, --stroke, --stroke-width, --skew, --cut), so
// :hover / .ready / .sel rules restyle them exactly as the CSS does; a hero colour set from code overrides them.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    /// <summary>an element whose background is drawn (Painter2D) from --fill / --fill2 (a gradient's end) / --stroke /
    /// --stroke-width / --skew (CSS skewX degrees) / --cut (the button clip-path's slant, px)</summary>
    public class Shape : VisualElement
    {
        static readonly CustomStyleProperty<Color> P_FILL = new CustomStyleProperty<Color>("--fill");
        static readonly CustomStyleProperty<Color> P_FILL2 = new CustomStyleProperty<Color>("--fill2");
        static readonly CustomStyleProperty<Color> P_STROKE = new CustomStyleProperty<Color>("--stroke");
        static readonly CustomStyleProperty<float> P_SW = new CustomStyleProperty<float>("--stroke-width");
        static readonly CustomStyleProperty<float> P_SKEW = new CustomStyleProperty<float>("--skew");
        static readonly CustomStyleProperty<float> P_CUT = new CustomStyleProperty<float>("--cut");

        public Color fill = Color.clear, fill2 = Color.clear, stroke = Color.clear;
        public float strokeWidth = 1, skew, cut;
        /// <summary>colours set from code (a hero's or a team's) win over the stylesheet</summary>
        public Color? fillOverride, strokeOverride;

        public Shape()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        void OnCustomStyle(CustomStyleResolvedEvent e)
        {
            var cs = e.customStyle;
            fill = cs.TryGetValue(P_FILL, out var f) ? f : Color.clear;
            fill2 = cs.TryGetValue(P_FILL2, out var f2) ? f2 : Color.clear;
            stroke = cs.TryGetValue(P_STROKE, out var s) ? s : Color.clear;
            strokeWidth = cs.TryGetValue(P_SW, out var w) ? w : 1;
            skew = cs.TryGetValue(P_SKEW, out var k) ? k : 0;
            cut = cs.TryGetValue(P_CUT, out var c) ? c : 0;
            MarkDirtyRepaint();
        }

        protected Color Fill => fillOverride ?? fill;
        protected Color Stroke => strokeOverride ?? stroke;

        protected virtual void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            var p = mgc.painter2D;
            var poly = Outline(r.width, r.height);
            FillPoly(p, poly, Fill, fill2, r);
            StrokeEdges(p, poly);
        }

        /// <summary>the background's outline: a skewX parallelogram, or the button's slanted clip-path, or the box</summary>
        protected virtual Vector2[] Outline(float w, float h)
        {
            if (cut > 0) return new[] { new Vector2(cut, 0), new Vector2(w, 0), new Vector2(w - cut, h), new Vector2(0, h) };
            float k = SkewDx(h);
            return new[] { new Vector2(-k, 0), new Vector2(w - k, 0), new Vector2(w + k, h), new Vector2(k, h) };
        }

        /// <summary>CSS skewX(a) about the centre: x shifts by tan(a) * (y - h/2); this is the shift at the bottom edge</summary>
        protected float SkewDx(float h) => Mathf.Tan(skew * Mathf.Deg2Rad) * h / 2;
        /// <summary>a point of the unskewed box, skewed</summary>
        protected Vector2 Sk(float x, float y, float h) => new Vector2(x + Mathf.Tan(skew * Mathf.Deg2Rad) * (y - h / 2), y);

        protected virtual void StrokeEdges(Painter2D p, Vector2[] poly)
        {
            var c = Stroke;
            if (c.a <= 0 || strokeWidth <= 0) return;
            p.strokeColor = c; p.lineWidth = strokeWidth;
            if (cut > 0)
            {
                // a clip-path clips the border too: only the straight top and bottom edges show
                float o = strokeWidth / 2;
                p.BeginPath(); p.MoveTo(poly[0] + new Vector2(0, o)); p.LineTo(poly[1] + new Vector2(0, o)); p.Stroke();
                p.BeginPath(); p.MoveTo(poly[3] - new Vector2(0, o)); p.LineTo(poly[2] - new Vector2(0, o)); p.Stroke();
                return;
            }
            Path(p, Inset(poly, strokeWidth / 2)); p.ClosePath(); p.Stroke();
        }

        public static void Path(Painter2D p, IList<Vector2> pts)
        {
            p.BeginPath(); p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
        }

        /// <summary>fill a polygon with a colour, or a left-to-right gradient when c2 is set (linear-gradient(90deg, ..))</summary>
        public static void FillPoly(Painter2D p, IList<Vector2> pts, Color c, Color c2, Rect r)
        {
            if (c.a <= 0 && c2.a <= 0) return;
            if (c2.a > 0) { p.fillColor = Color.white; p.fillGradient = FillGradient.MakeLinearGradient(c, c2, new Vector2(r.xMin, 0), new Vector2(r.xMax, 0), AddressMode.Clamp); }
            else { p.fillGradient = default; p.fillColor = c; }
            Path(p, pts); p.ClosePath(); p.Fill();
            p.fillGradient = default;
        }

        public static void FillPoly(Painter2D p, IList<Vector2> pts, Color c)
        {
            if (c.a <= 0) return;
            p.fillGradient = default; p.fillColor = c;
            Path(p, pts); p.ClosePath(); p.Fill();
        }

        /// <summary>a vertical gradient fill (linear-gradient(top, bottom))</summary>
        public static void FillPolyV(Painter2D p, IList<Vector2> pts, Color top, Color bottom, float y0, float y1)
        {
            p.fillColor = Color.white;
            p.fillGradient = FillGradient.MakeLinearGradient(top, bottom, new Vector2(0, y0), new Vector2(0, y1), AddressMode.Clamp);
            Path(p, pts); p.ClosePath(); p.Fill();
            p.fillGradient = default;
        }

        /// <summary>shrink a convex polygon toward its centroid by d (so a stroke sits inside the box like a CSS border)</summary>
        protected static Vector2[] Inset(Vector2[] poly, float d)
        {
            var c = Vector2.zero; foreach (var v in poly) c += v; c /= poly.Length;
            var o = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) { var dir = c - poly[i]; float m = dir.magnitude; o[i] = m > d * 2 ? poly[i] + dir / m * d * 1.2f : poly[i]; }
            return o;
        }
    }

    /// <summary>.hp .bar: the skewed health bar - health (white to ice gradient), armor (orange), shields (blue) side by
    /// side as flex items (they shrink together when the sum overflows, as flex-shrink does)</summary>
    public sealed class HpBar : Shape
    {
        float h, a, s;
        public void Set(double hp, double armor, double shield, double max)
        {
            float nh = (float)(hp / max), na = (float)(armor / max), ns = (float)(System.Math.Min(shield, max) / max);
            if (Mathf.Approximately(nh, h) && Mathf.Approximately(na, a) && Mathf.Approximately(ns, s)) return;
            h = nh; a = na; s = ns; MarkDirtyRepaint();
        }

        protected override void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float W = r.width, H = r.height;
            if (W <= 0) return;
            var p = mgc.painter2D;
            var box = Outline(W, H);
            FillPoly(p, box, Fill);
            float sum = h + a + s, k = sum > 1 ? 1 / sum : 1, x = 1;          // 1px inside the border
            float inner = W - 2;
            void Seg(float frac, Color top, Color bottom)
            {
                if (frac <= 0) return;
                float x1 = x + frac * k * inner;
                var q = new[] { Sk(x, 1, H), Sk(x1, 1, H), Sk(x1, H - 1, H), Sk(x, H - 1, H) };
                if (top == bottom) FillPoly(p, q, top); else FillPolyV(p, q, top, bottom, 1, H - 1);
                x = x1;
            }
            Seg(h, Color.white, new Color32(0xcf, 0xe8, 0xff, 0xff));
            Seg(a, new Color32(0xff, 0xb3, 0x47, 0xff), new Color32(0xff, 0xb3, 0x47, 0xff));
            Seg(s, new Color32(0x7f, 0xd3, 0xff, 0xff), new Color32(0x7f, 0xd3, 0xff, 0xff));
            StrokeEdges(p, box);
        }
    }

    /// <summary>a skewed box with a fill rising from the bottom (the ability tile's cooldown) or growing from the left
    /// (the objective bars); --fill is the box, the fill colour is set from code or --stroke's sibling class</summary>
    public sealed class SkewFill : Shape
    {
        public bool vertical;
        public bool fromRight;
        public Color barColor = new Color(1, 1, 1, 0.18f);
        float frac;
        public void SetFrac(float f) { f = Mathf.Clamp01(f); if (!Mathf.Approximately(f, frac)) { frac = f; MarkDirtyRepaint(); } }
        public void SetColor(Color c) { if (c != barColor) { barColor = c; MarkDirtyRepaint(); } }

        protected override void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float W = r.width, H = r.height;
            if (W <= 0) return;
            var p = mgc.painter2D;
            var box = Outline(W, H);
            FillPoly(p, box, Fill);
            if (frac > 0)
            {
                Vector2[] q;
                if (vertical) { float y0 = H * (1 - frac); q = new[] { Sk(0, y0, H), Sk(W, y0, H), Sk(W, H, H), Sk(0, H, H) }; }
                else if (fromRight) { float x0 = W * (1 - frac); q = new[] { Sk(x0, 0, H), Sk(W, 0, H), Sk(W, H, H), Sk(x0, H, H) }; }
                else { float x1 = W * frac; q = new[] { Sk(0, 0, H), Sk(x1, 0, H), Sk(x1, H, H), Sk(0, H, H) }; }
                FillPoly(p, q, barColor);
            }
            StrokeEdges(p, box);
        }
    }

    /// <summary>.ult .ring: conic-gradient(colour p%, rgba(255,255,255,.1) 0) masked to a ring (the mask is a radial
    /// gradient over the box's farthest corner: transparent to 58%, opaque from 60%, so the ring is 0.82..1 of the radius)</summary>
    public sealed class UltRing : Shape
    {
        float pct; Color col = new Color32(0xff, 0xd7, 0x6a, 0xff); bool full;
        public void Set(float p, Color c, bool solid)
        {
            if (Mathf.Approximately(p, pct) && c == col && solid == full) return;
            pct = p; col = c; full = solid; MarkDirtyRepaint();
        }

        protected override void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float R = Mathf.Min(r.width, r.height) / 2;
            if (R <= 0) return;
            var p = mgc.painter2D; var c = r.center;
            float rin = R * Mathf.Sqrt(2) * 0.59f, mid = (R + rin) / 2, wdt = R - rin;
            p.lineWidth = wdt; p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(1, 1, 1, 0.1f);
            p.BeginPath(); p.Arc(c, mid, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise); p.Stroke();
            float sweep = full ? 360 : Mathf.Clamp(pct, 0, 100) * 3.6f;
            if (sweep <= 0) return;
            p.strokeColor = col;
            p.BeginPath(); p.Arc(c, mid, Angle.Degrees(-90), Angle.Degrees(-90 + sweep), ArcDirection.Clockwise); p.Stroke();
        }
    }

    /// <summary>a reticle (Settings > Controls > Reticle, or the per-weapon default): crosshair bars, a circle, a dot,
    /// each with a dark outline - SettingsUI.drawReticle on a canvas, here on the element's centre</summary>
    public sealed class ReticleEl : VisualElement
    {
        ZuSettings.Reticle R;
        float k = 1;
        public ReticleEl() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        public void Set(ZuSettings.Reticle r, float scale = 1) { R = r; k = scale; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext mgc)
        {
            if (R == null) return;
            var p = mgc.painter2D; var c = contentRect.center;
            float t = (float)R.thickness * k, len = (float)R.length * k, gap = (float)R.gap * k, o = Mathf.Max(1, k);
            var col = U.Hex(R.color);
            var dark = new Color(0, 0, 0, (float)(R.outline * R.opacity));
            void Bar(float x0, float y0, float w, float h)
            {
                if (R.outline > 0) Rect(p, x0 - o, y0 - o, w + 2 * o, h + 2 * o, dark);
                Rect(p, x0, y0, w, h, U.A(col, (float)R.opacity));
            }
            if (R.type == "crosshairs" || R.type == "circle+crosshairs" || R.type == "default")
            {
                Bar(c.x - t / 2, c.y - gap - len, t, len); Bar(c.x - t / 2, c.y + gap, t, len);
                Bar(c.x - gap - len, c.y - t / 2, len, t); Bar(c.x + gap, c.y - t / 2, len, t);
            }
            if (R.type == "circle" || R.type == "circle+crosshairs")
            {
                float rad = Mathf.Max(4 * k, gap + (R.type == "circle" ? len * 0.6f : 0));
                p.lineCap = LineCap.Butt;
                if (R.outline > 0) { p.strokeColor = dark; p.lineWidth = t + 2 * o; p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise); p.Stroke(); }
                p.strokeColor = U.A(col, (float)R.opacity); p.lineWidth = t; p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise); p.Stroke();
            }
            if (R.dot > 0)
            {
                float d = (float)R.dot * k * 0.5f;
                if (R.outline > 0) Disc(p, c, d + o, new Color(0, 0, 0, (float)(R.outline * R.dotOpacity)));
                Disc(p, c, d, U.A(col, (float)R.dotOpacity));
            }
        }

        public static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillGradient = default; p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }
        public static void Disc(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillGradient = default; p.fillColor = col;
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise); p.ClosePath(); p.Fill();
        }
    }

    /// <summary>.hitmark: the four spokes of an X round the crosshair (white; red on a critical hit)</summary>
    public sealed class HitMarkEl : VisualElement
    {
        Color col = Color.white;
        public HitMarkEl() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        public void SetColor(Color c) { if (c != col) { col = c; MarkDirtyRepaint(); } }
        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D; var r = contentRect; var c = r.center; float R = r.width / 2;
            p.strokeColor = col; p.lineWidth = 2; p.lineCap = LineCap.Butt;
            foreach (var a in new[] { 45f, 135f, 225f, 315f })
            {
                var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                p.BeginPath(); p.MoveTo(c + d * R * 0.3f); p.LineTo(c + d * R); p.Stroke();
            }
        }
    }

    /// <summary>.hud.hurt: box-shadow inset 0 0 120px rgba(255,30,60,.45) - four edge gradients</summary>
    public sealed class Vignette : VisualElement
    {
        public Color color = new Color(1, 30 / 255f, 60 / 255f, 0.45f);
        public float size = 120;
        public Vignette() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D; var r = contentRect; float s = Mathf.Min(size, r.height / 2), W = r.width, H = r.height;
            var clear = U.A(color, 0);
            void Strip(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 g0, Vector2 g1)
            {
                p.fillColor = Color.white;
                p.fillGradient = FillGradient.MakeLinearGradient(color, clear, g0, g1, AddressMode.Clamp);
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.LineTo(c); p.LineTo(d); p.ClosePath(); p.Fill();
            }
            Strip(new Vector2(0, 0), new Vector2(W, 0), new Vector2(W, s), new Vector2(0, s), new Vector2(0, 0), new Vector2(0, s));
            Strip(new Vector2(0, H - s), new Vector2(W, H - s), new Vector2(W, H), new Vector2(0, H), new Vector2(0, H), new Vector2(0, H - s));
            Strip(new Vector2(0, 0), new Vector2(s, 0), new Vector2(s, H), new Vector2(0, H), new Vector2(0, 0), new Vector2(s, 0));
            Strip(new Vector2(W - s, 0), new Vector2(W, 0), new Vector2(W, H), new Vector2(W - s, H), new Vector2(W, 0), new Vector2(W - s, 0));
            p.fillGradient = default;
        }
    }

    /// <summary>a clip-path polygon: points as (fraction of width, px, fraction of height, px), filled with --fill (or a
    /// colour from code) - the role icons, the hex rank emblem, the hero cards' cut corner</summary>
    public sealed class Poly : Shape
    {
        readonly Vector4[] pts;
        public Poly(params Vector4[] points) { pts = points; }
        /// <summary>CSS polygon() percentages (0..100)</summary>
        public static Poly Pct(params float[] xy)
        {
            var v = new Vector4[xy.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector4(xy[i * 2] / 100f, 0, xy[i * 2 + 1] / 100f, 0);
            return new Poly(v);
        }
        protected override Vector2[] Outline(float w, float h)
        {
            var o = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) o[i] = new Vector2(pts[i].x * w + pts[i].y, pts[i].z * h + pts[i].w);
            return o;
        }
        protected override void StrokeEdges(Painter2D p, Vector2[] poly)
        {
            var c = Stroke;
            if (c.a <= 0 || strokeWidth <= 0) return;
            p.strokeColor = c; p.lineWidth = strokeWidth; Path(p, poly); p.ClosePath(); p.Stroke();
        }
    }

    /// <summary>CSS gradient backgrounds as textures stretched over the box (exact for radial-gradient's ellipse, which
    /// follows the box's aspect ratio): the menu screens' backdrops, the title's fade, the mode cards' shade</summary>
    public static class Grad
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>radial-gradient(ellipse at ax% ay%, stops...) - stops as (colour, percent); ellipse farthest-corner</summary>
        public static Texture2D Radial(float ax, float ay, params (Color c, float at)[] stops)
        {
            string key = "r" + ax + "," + ay + Key(stops);
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            const int W = 192, H = 108;
            t = New(W, H);
            float cx = ax / 100f, cy = ay / 100f;
            float rx = Mathf.Max(cx, 1 - cx) * Mathf.Sqrt(2), ry = Mathf.Max(cy, 1 - cy) * Mathf.Sqrt(2);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;          // v: 0 at the top (CSS)
                    float d = Mathf.Sqrt(Sq((u - cx) / rx) + Sq((v - cy) / ry)) * 100f;
                    px[(H - 1 - y) * W + x] = At(stops, d);
                }
            t.SetPixels(px); t.Apply(false, true);
            return cache[key] = t;
        }

        /// <summary>linear-gradient(angle, stops...): 90deg = left to right, 180deg = top to bottom</summary>
        public static Texture2D Linear(float deg, params (Color c, float at)[] stops)
        {
            string key = "l" + deg + Key(stops);
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            const int N = 256;
            bool horiz = Mathf.Approximately(deg % 180, 90);
            t = horiz ? New(N, 1) : New(1, N);
            var px = new Color[N];
            for (int i = 0; i < N; i++)
            {
                float d = (i + 0.5f) / N * 100f;
                // horizontal: 90deg runs left->right (pixel 0 = left); 270 reversed. vertical: 180deg top->bottom, and
                // texture row 0 is the bottom
                float pos = horiz ? (Mathf.Approximately(deg, 270) ? 100 - d : d) : (Mathf.Approximately(deg, 0) ? d : 100 - d);
                px[i] = At(stops, pos);
            }
            t.SetPixels(px); t.Apply(false, true);
            return cache[key] = t;
        }

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        static float Sq(float v) => v * v;
        static string Key((Color c, float at)[] s) { var b = new System.Text.StringBuilder(); foreach (var x in s) b.Append('|').Append(ColorUtility.ToHtmlStringRGBA(x.c)).Append('@').Append(x.at); return b.ToString(); }

        static Color At((Color c, float at)[] s, float d)
        {
            if (d <= s[0].at) return s[0].c;
            for (int i = 1; i < s.Length; i++)
                if (d <= s[i].at) { float k = (d - s[i - 1].at) / Mathf.Max(0.001f, s[i].at - s[i - 1].at); return Color.Lerp(s[i - 1].c, s[i].c, k); }
            return s[s.Length - 1].c;
        }

        /// <summary>an element filling its parent with the texture stretched over it</summary>
        public static VisualElement Fill(Texture2D t, VisualElement parent = null, string cls = null)
        {
            var e = U.Div("fill " + (cls ?? ""), parent);
            e.style.backgroundImage = new StyleBackground(t);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Length) { x = new Length(100, LengthUnit.Percent), y = new Length(100, LengthUnit.Percent) };
            return e;
        }
        public static void Set(VisualElement e, Texture2D t)
        {
            e.style.backgroundImage = new StyleBackground(t);
            e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }
        public static Color C(string hex, float a = 1) => U.A(U.Hex(hex), a);
    }

    /// <summary>filters (USS can't spell them in this project's import pipeline without the editor to check): grayscale +
    /// brightness for a silenced ability, saturate for hero cards, blur behind the pause menu, drop shadows (glows)</summary>
    public static class Filters
    {
        public static void Set(VisualElement e, params FilterFunction[] f) => e.style.filter = f.Length == 0 ? new StyleList<FilterFunction>(StyleKeyword.None) : new StyleList<FilterFunction>(new List<FilterFunction>(f));
        public static FilterFunction Make(FilterFunctionType t, float v) { var f = new FilterFunction(t); f.AddParameter(new FilterParameter(v)); return f; }
        public static FilterFunction Grayscale(float v) => Make(FilterFunctionType.Grayscale, v);
        public static FilterFunction Blur(float px) => Make(FilterFunctionType.Blur, px);
        public static FilterFunction Contrast(float v) => Make(FilterFunctionType.Contrast, v);
        /// <summary>brightness(b): a tint by grey b (multiplies the colour)</summary>
        public static FilterFunction Brightness(float b) { var f = new FilterFunction(FilterFunctionType.Tint); f.AddParameter(new FilterParameter(new Color(b, b, b, 1))); return f; }
    }
}

namespace ZU.Game.UI.Toolkit
{
    /// <summary>a rank emblem (Menu.ts emblem()): a faceted hexagonal gem in the tier colour with the division numeral -
    /// the inner gem is a 145deg gradient white -> colour -> a darker colour, the outer outline the colour at .7</summary>
    public sealed class EmblemEl : VisualElement
    {
        readonly Color c;
        public EmblemEl(Color color, string numeral, float size)
        {
            c = color;
            pickingMode = PickingMode.Ignore;
            AddToClassList("emblem");
            style.width = style.height = size;
            generateVisualContent += Draw;
            var b = U.Txt(numeral, "em-b", this);
            b.style.fontSize = size * 0.38f;
        }

        static Vector2[] Hex(Rect r, float inset, float sx)
        {
            float x0 = r.xMin + r.width * inset, y0 = r.yMin + r.height * inset, w = r.width * (1 - 2 * inset), h = r.height * (1 - 2 * inset);
            Vector2 P(float fx, float fy) => new Vector2(x0 + w * fx, y0 + h * fy);
            return new[] { P(0.5f, 0), P(1 - sx, 0.25f), P(1 - sx, 0.75f), P(0.5f, 1), P(sx, 0.75f), P(sx, 0.25f) };
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; if (r.width <= 0) return;
            var p = mgc.painter2D;
            var inner = Hex(r, 0.08f, 0.07f);
            var g = new Gradient();
            var dark = Color.Lerp(c, Color.black, 0.45f);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 0.38f), new GradientColorKey(dark, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            p.fillColor = Color.white;
            p.fillGradient = FillGradient.MakeLinearGradient(g, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMax), AddressMode.Clamp);
            Shape.Path(p, inner); p.ClosePath(); p.Fill();
            p.fillGradient = default;
            p.strokeColor = U.A(c, 0.7f); p.lineWidth = 2;
            Shape.Path(p, Hex(r, 0.02f, 0)); p.ClosePath(); p.Stroke();
        }
    }
}
