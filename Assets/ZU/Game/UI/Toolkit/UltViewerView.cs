// The Ult Viewer's overlay (src/client/UltShowcase.ts mountUi): the card with the hero, the ult's name and description
// and the replay bar, REPLAY [R] / SLOW-MO [T] / BACK TO HERO VIEWER [Esc], the hero strip along the bottom and the
// orbit hint. The showcase itself (dummies, the cast, the cinematic camera) is Fx/UltShowcase.cs; this replaces its
// IMGUI overlay and tells it when a drag starts on the UI (PointerOverUi).
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ZU.Game.Fx;

namespace ZU.Game.UI.Toolkit
{
    public sealed class UltViewerView
    {
        readonly VisualElement el;
        readonly VisualElement fill;
        readonly ZButton slow;
        readonly string heroId;
        int barW = -1;

        public UltViewerView(VisualElement parent, UltShowcase sc)
        {
            heroId = sc.HeroId;
            var d = ZuData.Get();
            var h = sc.Hero.baseDef; var u = h.ult; var c = U.Hex(h.color);
            el = U.Div("ultsc", parent);
            var col = U.Div("ucol", el);
            var card = U.Div("ucard", col, pick: true);
            card.style.borderLeftColor = c;
            card.style.backdropFilter = new StyleList<FilterFunction>(new System.Collections.Generic.List<FilterFunction> { Filters.Blur(4) });
            U.Pic("portrait_" + h.id, "uc-img", card, "key_" + h.id);
            var tx = U.Div("uc-t", card);
            U.Txt($"ULT VIEWER · {U.Up(h.name)}", "uc-s", tx);
            U.Txt(u?.name ?? "", "uc-b", tx).style.color = c;
            U.Txt(u?.desc ?? "", "uc-p", tx);
            var bar = U.Div("ubar", tx); fill = U.Div("ubar-i", bar); fill.style.backgroundColor = c;
            var btns = U.Div("ubtns", col);
            U.Btn("⟲ REPLAY  <size=11>[R]</size>", "ub", () => UltShowcase.Current?.Replay(), btns);
            slow = U.Btn("SLOW-MO  <size=11>[T]</size>", "ub", () => { var s = UltShowcase.Current; if (s != null) s.SlowMo = !s.SlowMo; }, btns);
            U.Btn("BACK TO HERO VIEWER  <size=11>[Esc]</size>", "ub", Back, btns);
            var pick = U.Div("upick", el, pick: true);
            foreach (var x in MenuState.Roster(d))
            {
                var b = U.Btn(null, "up" + (x.id == h.id ? " sel" : ""), () => { if (x.id != heroId) { MenuState.ReturnTo = "viewer:" + x.id; UltShowcase.Current?.Pick(x.id); } }, pick);
                if (x.id == h.id) b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = U.Hex(x.color);
                b.tooltip = $"{x.name}: {x.ult?.name}";
                U.Pic("portrait_" + x.id, "up-img", b, "key_" + x.id);
            }
            U.Txt("Drag to orbit · wheel to zoom", "uhint", el);
            UltShowcase.DrawImgui = false;
            UltShowcase.PointerOverUi = PointerOverPanel;
        }

        static void Back() { MenuState.ReturnTo = "viewer:" + (UltShowcase.Current?.HeroId ?? ""); UltShowcase.Current?.Back(); }

        /// <summary>every frame: the replay bar (whole percents), SLOW-MO lit while it runs, Esc goes back</summary>
        public void Update()
        {
            var s = UltShowcase.Current; if (s == null) return;
            int pct = Mathf.RoundToInt(Mathf.Clamp01(s.Progress) * 100);
            if (pct != barW) { barW = pct; fill.style.width = new Length(pct, LengthUnit.Percent); }
            U.Toggle(slow, "on", s.SlowMo);
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
        }

        public void Close()
        {
            el.RemoveFromHierarchy();
            UltShowcase.DrawImgui = true;
            UltShowcase.PointerOverUi = null;
        }

        /// <summary>is the pointer over something on the panel that takes clicks (a drag starting there is the UI's)</summary>
        public static bool PointerOverPanel()
        {
            var ui = UiRoot.Existing; var m = Mouse.current;
            if (ui?.Panel == null || m == null) return false;
            var sp = m.position.ReadValue();
            var pp = RuntimePanelUtils.ScreenToPanel(ui.Panel, new Vector2(sp.x, Screen.height - sp.y));
            var e = ui.Panel.Pick(pp);
            return e != null && e.pickingMode == PickingMode.Position && e != ui.Root;
        }
    }
}
