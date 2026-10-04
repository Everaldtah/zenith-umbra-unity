// Two text differences between the PC game's web page and UI Toolkit in Unity 6000.6, fixed for every text element of
// the panel without touching zu.uss (which stays a rule-for-rule port of style.css):
//
// 1. LETTER SPACING. This Unity version renders UI Toolkit text with the Advanced Text Generator unless told otherwise,
//    and that generator ignores character spacing. zu.uss has 85 letter-spacing rules (the tagline's 7.2px, every
//    button's 2.2px, the tabs, the headings); none of them showed in v0.2.2. The standard generator honours them, so the
//    root and every text element are set to it.
//
// 2. GLOW. style.css draws its neon glows with text-shadow (the logo's "0 0 30px"). UI Toolkit draws a text shadow from
//    the glyph's distance field, whose reach is the font atlas padding - about a tenth of the glyph size for a font made
//    from a .ttf. A 30px blur on the 108px logo came out as a hard 10px band around the letters. A shadow that needs more
//    reach than the atlas has becomes a drop-shadow FILTER instead (a real Gaussian of the rendered text, any size), with
//    sigma = blur radius / 2 as CSS defines the text-shadow blur.
//
// Elements are picked up as they appear: the screens build and rebuild their elements all the time, so each frame the
// panel's text elements are walked once and the new ones handled a frame after they first show (their styles are
// resolved by then).
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    public static class UiText
    {
        /// <summary>false: text-shadow stays as UI Toolkit draws it (clipped by the atlas padding)</summary>
        public static bool Glow = true;

        /// <summary>false: letter-spacing stays as the generator applies it (scaled by fontSize / 100)</summary>
        public static bool FixSpacing = true;

        /// <summary>a shadow becomes a filter when its blur is at least this many pixels...</summary>
        const float MinBlur = 8f;
        /// <summary>...and more than this share of the font size (the distance field's reach is about a tenth)</summary>
        const float Reach = 0.12f;

        sealed class Seen { public int frames; public bool done; }
        static readonly ConditionalWeakTable<TextElement, Seen> seen = new ConditionalWeakTable<TextElement, Seen>();
        static readonly List<TextElement> buf = new List<TextElement>(512);
        static UQueryState<TextElement> query;
        static VisualElement root;

        public static void Install(UiRoot ui)
        {
            if (ui == null || ui.Root == null) return;
            root = ui.Root;
            root.style.unityTextGenerator = TextGeneratorType.Standard;
            query = root.Query<TextElement>().Build();
            ui.Tick += Scan;
            Debug.Log("[ZU UI] text: standard generator (letter-spacing on), glow filters " + (Glow && Filters.Enabled ? "on" : "off"));
        }

        static void Scan()
        {
            if (root == null) return;
            buf.Clear();
            query.ToList(buf);
            for (int i = 0; i < buf.Count; i++)
            {
                var te = buf[i];
                var s = seen.GetOrCreateValue(te);
                if (s.done) continue;
                if (s.frames == 0) te.style.unityTextGenerator = TextGeneratorType.Standard;
                if (te.panel == null) continue;
                if (++s.frames < 2) continue;                 // styles resolve when the panel next updates
                s.done = true;
                Spacing(te);
                if (Glow) ToFilter(te);
            }
            buf.Clear();
        }

        // The standard generator hands the USS letter-spacing (pixels) to TextCore as its character spacing, which TextCore
        // reads in hundredths of the font size (advance += spacing * fontSize / 100). A 7.2px rule on 24px text so moved
        // the letters 1.7px apart: every tracked label got 18-34 % of its spacing (evera-7a measured the tagline at 475px
        // wide against the web's 679, the ARMORY heading 177 against 190). The resolved pixel value is turned into the
        // unit TextCore expects and set on the text element itself - text elements are leaves, so nothing inherits it.
        // An element whose font size changes later keeps the value computed here.
        static void Spacing(TextElement te)
        {
            if (!FixSpacing) return;
            float ls = te.resolvedStyle.letterSpacing, fs = te.resolvedStyle.fontSize;
            if (Mathf.Abs(ls) > 0.01f && fs > 0.5f) te.style.letterSpacing = ls * 100f / fs;
        }

        static void ToFilter(TextElement te)
        {
            if (!Filters.Enabled) return;
            var sh = te.resolvedStyle.textShadow;
            float size = te.resolvedStyle.fontSize;
            if (sh.color.a <= 0.01f || sh.blurRadius < MinBlur || sh.blurRadius <= size * Reach) return;
            if (te.style.filter.keyword != StyleKeyword.Null || (te.style.filter.value != null && te.style.filter.value.Count > 0)) return;   // it has a filter of its own
            te.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 0f, color = new Color(0, 0, 0, 0) };
            Filters.Set(te, DropShadow(sh.offset.x, sh.offset.y, sh.blurRadius * 0.5f, sh.color));
        }

        /// <summary>drop-shadow(x y sigma colour): the element's own pixels, blurred and tinted, drawn under it</summary>
        public static FilterFunction DropShadow(float x, float y, float sigma, Color c)
        {
            var f = new FilterFunction(FilterFunctionType.DropShadow);
            f.AddParameter(new FilterParameter(x));
            f.AddParameter(new FilterParameter(y));
            f.AddParameter(new FilterParameter(sigma));
            f.AddParameter(new FilterParameter(c));
            return f;
        }
    }
}
