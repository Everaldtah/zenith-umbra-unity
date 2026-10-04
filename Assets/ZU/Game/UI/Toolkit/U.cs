// Small DOM-like helpers for building the PC game's screens in UI Toolkit: elements with class lists (the TS builds its
// screens from HTML strings; these keep the same class names so zu.uss matches style.css rule for rule), clickable
// "buttons" without the default theme's look, images from Resources/ZUImg (the PC game's public/img), number and time
// formats.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    public static class U
    {
        static readonly CultureInfo EN = CultureInfo.GetCultureInfo("en-US");

        /// <summary>a div: an element with these classes (space separated), appended to the parent</summary>
        public static VisualElement Div(string cls = null, VisualElement parent = null, bool pick = false)
        {
            var e = new VisualElement { pickingMode = pick ? PickingMode.Position : PickingMode.Ignore };
            Classes(e, cls);
            parent?.Add(e);
            return e;
        }

        /// <summary>a text element (rich text: &lt;b&gt;, &lt;color&gt;, &lt;size&gt;, &lt;alpha&gt;)</summary>
        public static Label Txt(string text, string cls = null, VisualElement parent = null)
        {
            var l = new Label(text ?? "") { pickingMode = PickingMode.Ignore, enableRichText = true };
            l.AddToClassList("t");
            Classes(l, cls);
            parent?.Add(l);
            return l;
        }

        /// <summary>a button the stylesheet draws (class "zb" + cls): a click handler, hover / active / disabled states,
        /// the hover and click sounds; none of the default theme's button look</summary>
        public static ZButton Btn(string text, string cls, Action onClick, VisualElement parent = null)
        {
            var b = new ZButton(text, onClick);
            Classes(b, cls);
            parent?.Add(b);
            return b;
        }

        public static void Classes(VisualElement e, string cls)
        {
            if (string.IsNullOrEmpty(cls)) return;
            foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        }

        public static void Toggle(VisualElement e, string cls, bool on) { if (on) e.AddToClassList(cls); else e.RemoveFromClassList(cls); }
        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        public static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None;

        /// <summary>set a label's text only when it changed (no relayout every frame)</summary>
        public static void Set(Label l, string text) { text ??= ""; if (l.text != text) l.text = text; }

        // ------------------------------------------------------------------ images (the PC game's public/img)
        static readonly Dictionary<string, Texture2D> imgs = new Dictionary<string, Texture2D>();
        /// <summary>public/img/{name}.webp, imported as Resources/ZUImg/{name} (null if the game has no such image)</summary>
        /// <summary>the copy of a picture with the web's constant saturate(1.2) baked in (`<name>_sat`: .title .bg and .loading .bg;
        /// UI Toolkit has no saturate filter), else the picture itself</summary>
        public static string Sat(string name) => name != null && Img(name + "_sat") != null ? name + "_sat" : name;

        public static Texture2D Img(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (imgs.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("ZUImg/" + name);
            imgs[name] = t;
            return t;
        }

        /// <summary>an &lt;img&gt;: the picture covers the box (object-fit: cover); hidden when the image is missing
        /// (the TS onerror="this.style.visibility='hidden'")</summary>
        public static VisualElement Pic(string name, string cls = null, VisualElement parent = null, string fallback = null)
        {
            var e = Div("img " + (cls ?? ""), parent);
            var t = Img(name) ?? Img(fallback);
            if (t != null) e.style.backgroundImage = new StyleBackground(t);
            else e.style.visibility = Visibility.Hidden;
            return e;
        }

        /// <summary>a CSS background-image (background-size: cover, centred)</summary>
        public static void Bg(VisualElement e, string name)
        {
            var t = Img(name);
            e.style.backgroundImage = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        // ------------------------------------------------------------------ formats and colours
        /// <summary>toLocaleString('en-US') of a rounded number: 12,345</summary>
        public static string N(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("#,0", EN);
        /// <summary>m:ss</summary>
        public static string Clock(double s) { s = Math.Max(0, s); return $"{(int)(s / 60)}:{(int)(s % 60):00}"; }
        /// <summary>toFixed(d)</summary>
        public static string F(double v, int d) => v.ToString("F" + d, EN);
        public static string Up(string s) => (s ?? "").ToUpperInvariant();

        public static Color Hex(string hex, Color fallback) => Conv.Hex(hex, fallback);
        public static Color Hex(string hex) => Conv.Hex(hex, Color.white);
        public static string Css(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static Color A(Color c, float a) { c.a = a; return c; }
    }

    /// <summary>a &lt;button&gt;: a drawn Shape (the clip-path slant, gradients) with a label and a Clickable (no theme styles to fight), the
    /// :hover / :active / :disabled pseudo-states for the stylesheet, and the UI sounds the TS menu plays</summary>
    public class ZButton : Shape
    {
        public readonly Label label;
        public Action clicked;
        public static Action<string> Sfx;           // set by the audio side: "ui_hover" / "ui_click"

        public ZButton(string text, Action onClick)
        {
            AddToClassList("zb");
            pickingMode = PickingMode.Position;     // (a Shape is click-through by default)
            focusable = true;
            label = new Label(text ?? "") { pickingMode = PickingMode.Ignore, enableRichText = true };
            label.AddToClassList("zb-label");
            Add(label);
            if (text == null) label.style.display = DisplayStyle.None;      // a button whose content is built from children
            clicked = onClick;
            this.AddManipulator(new Clickable(() => { Sfx?.Invoke("ui_click"); clicked?.Invoke(); }));
            RegisterCallback<PointerEnterEvent>(_ => { if (enabledSelf) Sfx?.Invoke("ui_hover"); });
        }

        public string Text { get => label.text; set { if (label.text != value) label.text = value ?? ""; label.style.display = value == null ? DisplayStyle.None : DisplayStyle.Flex; } }
    }
}
