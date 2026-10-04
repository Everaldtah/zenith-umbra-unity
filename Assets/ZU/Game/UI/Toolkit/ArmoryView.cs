// Stadium Armory (src/client/Armory.ts): between rounds, spend round cash on items (weapon / ability / survival, three
// tiers, 6 slots) and, on rounds 1, 3, 5 and 7, pick one of your hero's powers. READY ends the wait early once everyone
// is set.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ZU.Game.Audio;
using ZU.Sim;

namespace ZU.Game.UI.Toolkit
{
    public sealed class ArmoryView
    {
        static readonly (string id, string name)[] CAT = { ("weapon", "WEAPON"), ("ability", "ABILITY"), ("survival", "SURVIVAL") };

        readonly VisualElement root;
        string tab = "weapon", sig = "";
        World w; Actor me;
        Label timeEl;
        public bool open;
        public Action OnClose;

        public ArmoryView(VisualElement parent)
        {
            root = U.Div("armory", parent, pick: true);
            Grad.Fill(Grad.Radial(50, 0, (new Color(27 / 255f, 33 / 255f, 64 / 255f, 0.96f), 0), (new Color(5 / 255f, 6 / 255f, 10 / 255f, 0.97f), 70)), root, "ar-bg");
            U.Show(root, false);
        }

        public void Show(World world, Actor player) { w = world; me = player; open = true; U.Show(root, true); sig = SigOf(); Render(); }
        public void Hide() { open = false; U.Show(root, false); }

        /// <summary>every frame while open: re-render only when something changed (cash, owned, the tab, the round); the
        /// countdown ticks in place (a rebuild under the cursor would swallow a click and reset hover states)</summary>
        public void Update()
        {
            if (!open || w?.stadium == null || me == null) return;
            var S = w.stadium;
            if (S.phase != "armory") { Hide(); OnClose?.Invoke(); return; }
            var s = SigOf();
            if (s != sig) { sig = s; Render(); }
            if (timeEl != null) U.Set(timeEl, $"{Math.Max(0, Math.Ceiling(S.phaseEnd - w.time)):0}s");
        }

        string SigOf() { var S = w.stadium; return string.Join("|", me.cash, string.Join(",", me.items), string.Join(",", me.powers), tab, S.round, S.PowerPending(me)); }

        static void Sfx(string id) { try { AudioKit.Play(id, null); } catch (Exception) { /* no audio yet */ } }

        void Act(Action a) { a(); sig = SigOf(); Render(); }

        void Render()
        {
            var S = w.stadium;
            // keep the backdrop, rebuild the rest
            while (root.childCount > 1) root.RemoveAt(1);
            double left = Math.Max(0, Math.Ceiling(S.phaseEnd - w.time));
            var powers = Stadium.PowersFor(me.baseDef);
            bool pending = S.PowerPending(me);
            string my = me.team, them = my == "zenith" ? "umbra" : "zenith";

            var top = U.Div("ar-top", root);
            var title = U.Div("ar-title", top);
            U.Txt("ARMORY", "ar-t", title);
            U.Txt($"ROUND {S.round} · {S.wins[my]} - {S.wins[them]} · first to 4", "ar-s", title);
            U.Txt("$" + U.N(me.cash), "ar-cash", top);
            timeEl = U.Txt($"{left:0}s", "ar-time", top);

            if (pending)
            {
                var pw = U.Div("ar-powers", root);
                U.Txt($"CHOOSE A POWER <size=14><alpha=#B3>   round {S.round} power pick - it upgrades {me.baseDef.name}'s kit for the rest of the match<alpha=#FF></size>", "ar-h3", pw);
                var row = U.Div("pw", pw);
                foreach (var p in powers)
                {
                    var b = U.Btn(null, "power", () => Act(() => { if (S.PickPower(me, p.id)) Sfx("ult_ready"); }), row);
                    U.Txt(p.name, "pw-b", b); U.Txt(p.desc, "pw-p", b);
                    b.SetEnabled(!me.powers.Contains(p.id));
                }
            }

            var main = U.Div("ar-main", root);
            var tabs = U.Div("ar-tabs", main);
            foreach (var (id, name) in CAT)
            {
                var b = U.Btn(name, "ar-tab" + (id == tab ? " on" : ""), () => Act(() => tab = id), tabs);
            }
            var items = new ScrollView(ScrollViewMode.Vertical);
            items.AddToClassList("ar-scroll");
            main.Add(items);
            var grid = U.Div("ar-items", items.contentContainer);
            foreach (var i in Stadium.ITEMS.Where(i => i.cat == tab))
            {
                bool owned = me.items.Contains(i.id), full = me.items.Count >= Stadium.MAX_ITEMS, poor = me.cash < i.cost;
                var b = U.Btn(null, $"it {i.tier}{(owned ? " owned" : "")}", () => Act(() =>
                {
                    if (owned) { S.Sell(me, i.id); Sfx("ui_click"); }
                    else if (S.Buy(me, i.id)) Sfx("capture"); else Sfx("whiff");
                }), grid);
                U.Txt(i.name, "it-b", b); U.Txt(U.Up(i.tier), "it-s", b); U.Txt(i.desc, "it-p", b);
                U.Txt(owned ? "SELL · refund" : "$" + U.N(i.cost), "it-e", b);
                if (!owned && (full || poor)) b.SetEnabled(false);
            }

            var bottom = U.Div("ar-bottom", root);
            var own = U.Div("ar-owned", bottom);
            U.Txt($"ITEMS {me.items.Count}/{Stadium.MAX_ITEMS}", "ar-os", own);
            foreach (var id in me.items)
                if (Stadium.ITEM.TryGetValue(id, out var it)) { var l = U.Txt(it.name, "ar-oi " + it.tier, own); l.tooltip = it.desc; }
            if (me.powers.Count > 0)
            {
                U.Txt("POWERS", "ar-os", own);
                foreach (var id in me.powers) U.Txt(powers.FirstOrDefault(p => p.id == id)?.name ?? id, "ar-oi pow", own);
            }
            var ready = U.Btn("READY", "primary ready", () =>
            {
                S.SetReady(me); Sfx("announce"); Hide();
                UnityEngine.Cursor.lockState = CursorLockMode.Locked; UnityEngine.Cursor.visible = false;
                OnClose?.Invoke();
            }, bottom);
            if (pending) { ready.SetEnabled(false); ready.tooltip = "pick a power first"; }
        }
    }
}
